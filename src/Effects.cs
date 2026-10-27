// Pixel operations. Every method returns a new bitmap (inputs are never mutated) so undo is just a reference swap.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SnapLite
{
    enum EdgeKind { Torn, Wave, Sawtooth, Fade }

    static class Effects
    {
        // ------------------------------------------------------------ plumbing
        public static Bitmap To32(Bitmap src)
        {
            if (src.PixelFormat == PixelFormat.Format32bppArgb) return (Bitmap)src.Clone();
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            b.SetResolution(96, 96);
            using (var g = Graphics.FromImage(b)) g.DrawImage(src, 0, 0, src.Width, src.Height);
            return b;
        }
        public static byte[] Bytes(Bitmap b, out int stride)
        {
            var r = new Rectangle(0, 0, b.Width, b.Height);
            var d = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            stride = d.Stride;
            var arr = new byte[Math.Abs(d.Stride) * b.Height];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, arr, 0, arr.Length);
            b.UnlockBits(d);
            return arr;
        }
        public static Bitmap FromBytes(byte[] arr, int w, int h, int stride)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            b.SetResolution(96, 96);
            var d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(arr, 0, d.Scan0, Math.Min(arr.Length, Math.Abs(d.Stride) * h));
            b.UnlockBits(d);
            return b;
        }
        static Bitmap Blank(int w, int h, Color fill)
        {
            var b = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
            b.SetResolution(96, 96);
            if (fill.A > 0) using (var g = Graphics.FromImage(b)) g.Clear(fill);
            return b;
        }
        static byte Clamp(double v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

        // ------------------------------------------------------------ geometry
        public static Bitmap Crop(Bitmap src, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (r.Width < 1 || r.Height < 1) return To32(src);
            return src.Clone(r, PixelFormat.Format32bppArgb);
        }
        public static Bitmap Resize(Bitmap src, int w, int h)
        {
            var b = Blank(w, h, Color.Transparent);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return b;
        }
        // anchor: 0..1 within the new canvas; returns the new bitmap and the offset the old image landed at
        public static Bitmap CanvasSize(Bitmap src, int w, int h, float ax, float ay, Color fill, out Point offset)
        {
            var b = Blank(w, h, fill);
            offset = new Point((int)Math.Round((b.Width - src.Width) * ax), (int)Math.Round((b.Height - src.Height) * ay));
            using (var g = Graphics.FromImage(b)) g.DrawImage(src, offset.X, offset.Y, src.Width, src.Height);
            return b;
        }
        public static Bitmap RotateFlip(Bitmap src, RotateFlipType t)
        {
            var b = To32(src); b.RotateFlip(t); return b;
        }
        public static Bitmap RotateAny(Bitmap src, float deg, Color fill)
        {
            double a = deg * Math.PI / 180, c = Math.Abs(Math.Cos(a)), s = Math.Abs(Math.Sin(a));
            int w = (int)Math.Ceiling(src.Width * c + src.Height * s), h = (int)Math.Ceiling(src.Width * s + src.Height * c);
            var b = Blank(w, h, fill);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(w / 2f, h / 2f); g.RotateTransform(deg); g.TranslateTransform(-src.Width / 2f, -src.Height / 2f);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return b;
        }
        // Snagit-style "cut out": remove a horizontal or vertical band and join the halves.
        public static Bitmap CutBand(Bitmap src, bool horizontalBand, int start, int length)
        {
            if (horizontalBand)
            {
                start = Math.Max(0, Math.Min(src.Height - 1, start)); length = Math.Min(length, src.Height - start);
                if (length >= src.Height) return To32(src);
                var b = Blank(src.Width, src.Height - length, Color.Transparent);
                using (var g = Graphics.FromImage(b))
                {
                    g.DrawImage(src, new Rectangle(0, 0, src.Width, start), 0, 0, src.Width, start, GraphicsUnit.Pixel);
                    g.DrawImage(src, new Rectangle(0, start, src.Width, src.Height - start - length), 0, start + length, src.Width, src.Height - start - length, GraphicsUnit.Pixel);
                }
                return b;
            }
            else
            {
                start = Math.Max(0, Math.Min(src.Width - 1, start)); length = Math.Min(length, src.Width - start);
                if (length >= src.Width) return To32(src);
                var b = Blank(src.Width - length, src.Height, Color.Transparent);
                using (var g = Graphics.FromImage(b))
                {
                    g.DrawImage(src, new Rectangle(0, 0, start, src.Height), 0, 0, start, src.Height, GraphicsUnit.Pixel);
                    g.DrawImage(src, new Rectangle(start, 0, src.Width - start - length, src.Height), start + length, 0, src.Width - start - length, src.Height, GraphicsUnit.Pixel);
                }
                return b;
            }
        }
        // Rectangle of content once uniform border colour is removed.
        public static Rectangle AutoCropRect(Bitmap src, int tolerance)
        {
            int stride; var d = Bytes(src, out stride); int w = src.Width, h = src.Height;
            int bi = 0; byte br = d[bi + 2], bg = d[bi + 1], bb = d[bi], ba = d[bi + 3];
            Func<int, int, bool> same = delegate(int x, int y)
            {
                int i = y * stride + x * 4;
                return Math.Abs(d[i] - bb) <= tolerance && Math.Abs(d[i + 1] - bg) <= tolerance && Math.Abs(d[i + 2] - br) <= tolerance && Math.Abs(d[i + 3] - ba) <= tolerance;
            };
            int top = 0, bottom = h - 1, left = 0, right = w - 1;
            while (top < h && RowSame(same, top, 0, w)) top++;
            while (bottom > top && RowSame(same, bottom, 0, w)) bottom--;
            while (left < w && ColSame(same, left, top, bottom + 1)) left++;
            while (right > left && ColSame(same, right, top, bottom + 1)) right--;
            if (top >= h || left >= w) return new Rectangle(0, 0, w, h);
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
        static bool RowSame(Func<int, int, bool> f, int y, int x0, int x1) { for (int x = x0; x < x1; x++) if (!f(x, y)) return false; return true; }
        static bool ColSame(Func<int, int, bool> f, int x, int y0, int y1) { for (int y = y0; y < y1; y++) if (!f(x, y)) return false; return true; }

        // ------------------------------------------------------------ decoration
        public static Bitmap Border(Bitmap src, int width, Color color)
        {
            var b = Blank(src.Width + width * 2, src.Height + width * 2, color);
            using (var g = Graphics.FromImage(b)) g.DrawImage(src, width, width, src.Width, src.Height);
            return b;
        }
        public static Bitmap DropShadow(Bitmap src, int dx, int dy, int blur, Color color, int opacity)
        {
            int pad = blur * 2 + Math.Max(Math.Abs(dx), Math.Abs(dy));
            int w = src.Width + pad * 2, h = src.Height + pad * 2;
            var mask = Blank(w, h, Color.Transparent);
            using (var g = Graphics.FromImage(mask))
            using (var br = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, opacity * 255 / 100)), color)))
                g.FillRectangle(br, pad + dx, pad + dy, src.Width, src.Height);
            // an image with transparency casts a shadow from its alpha channel
            if (HasAlpha(src))
            {
                int s1; var sb = Bytes(src, out s1); int s2; var mb = Bytes(mask, out s2);
                Array.Clear(mb, 0, mb.Length);
                for (int y = 0; y < src.Height; y++) for (int x = 0; x < src.Width; x++)
                    {
                        int yy = y + pad + dy, xx = x + pad + dx; if (yy < 0 || xx < 0 || yy >= h || xx >= w) continue;
                        int i = yy * s2 + xx * 4, j = y * s1 + x * 4;
                        mb[i] = color.B; mb[i + 1] = color.G; mb[i + 2] = color.R; mb[i + 3] = (byte)(sb[j + 3] * opacity / 100);
                    }
                mask.Dispose(); mask = FromBytes(mb, w, h, s2);
            }
            var soft = blur > 0 ? GaussianBlur(mask, blur) : mask;
            if (soft != mask) mask.Dispose();
            using (var g = Graphics.FromImage(soft)) g.DrawImage(src, pad, pad, src.Width, src.Height);
            return soft;
        }
        static bool HasAlpha(Bitmap b)
        {
            int stride; var d = Bytes(b, out stride);
            for (int y = 0; y < b.Height; y += 3) for (int x = 0; x < b.Width; x += 3) if (d[y * stride + x * 4 + 3] < 250) return true;
            return false;
        }
        // Irregular edge on all four sides (torn paper, wave, sawtooth) or a soft fade.
        public static Bitmap Edge(Bitmap src, EdgeKind kind, int depth, int seed)
        {
            int w = src.Width, h = src.Height;
            int stride; var d = Bytes(src, out stride);
            var rnd = new Random(seed);
            Func<int, double[]> profile = delegate(int n)
            {
                var p = new double[n];
                if (kind == EdgeKind.Torn)
                {
                    double v = depth / 2.0;
                    for (int i = 0; i < n; i++) { v += (rnd.NextDouble() - 0.5) * depth * 0.55; v = Math.Max(0, Math.Min(depth, v)); p[i] = v; }
                }
                else if (kind == EdgeKind.Wave) for (int i = 0; i < n; i++) p[i] = depth / 2.0 * (1 + Math.Sin(i / (double)Math.Max(6, depth) * 1.3));
                else if (kind == EdgeKind.Sawtooth) { int tooth = Math.Max(6, depth * 2); for (int i = 0; i < n; i++) { int t = i % tooth; p[i] = depth * (t < tooth / 2 ? t / (tooth / 2.0) : 2 - t / (tooth / 2.0)); } }
                else for (int i = 0; i < n; i++) p[i] = depth;
                return p;
            };
            var top = profile(w); var bot = profile(w); var left = profile(h); var right = profile(h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double cut = 0; double alpha = 1;
                    double dt = y - top[x], db = (h - 1 - y) - bot[x], dl = x - left[y], dr = (w - 1 - x) - right[y];
                    double m = Math.Min(Math.Min(dt, db), Math.Min(dl, dr));
                    if (kind == EdgeKind.Fade) alpha = Math.Max(0, Math.Min(1, Math.Min(Math.Min(y, h - 1 - y), Math.Min(x, w - 1 - x)) / (double)Math.Max(1, depth)));
                    else alpha = m < 0 ? 0 : m < 1.2 ? m / 1.2 : 1;
                    if (cut == 0 && alpha < 1) { int i = y * stride + x * 4 + 3; d[i] = (byte)(d[i] * alpha); }
                }
            return FromBytes(d, w, h, stride);
        }

        // ------------------------------------------------------------ colour
        static Bitmap Matrix(Bitmap src, ColorMatrix cm, float gamma)
        {
            var b = Blank(src.Width, src.Height, Color.Transparent);
            using (var g = Graphics.FromImage(b))
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                if (Math.Abs(gamma - 1f) > 0.001f) ia.SetGamma(gamma);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return b;
        }
        // brightness/contrast/saturation in -100..100, hue in degrees, gamma around 1
        public static Bitmap Adjust(Bitmap src, int brightness, int contrast, int saturation, int hue, float gamma)
        {
            float bri = brightness / 100f, con = 1f + contrast / 100f, sat = 1f + saturation / 100f;
            float t = (1f - con) / 2f + bri;
            var C = new float[][] { new float[] { con, 0, 0, 0, 0 }, new float[] { 0, con, 0, 0, 0 }, new float[] { 0, 0, con, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { t, t, t, 0, 1 } };
            float lr = 0.3086f, lg = 0.6094f, lb = 0.0820f;
            float sr = (1 - sat) * lr, sg = (1 - sat) * lg, sb = (1 - sat) * lb;
            var S = new float[][] { new float[] { sr + sat, sr, sr, 0, 0 }, new float[] { sg, sg + sat, sg, 0, 0 }, new float[] { sb, sb, sb + sat, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } };
            double a = hue * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
            var H = new float[][] {
                new float[] { (float)(lr + cs * (1 - lr) + sn * -lr), (float)(lr + cs * -lr + sn * 0.143), (float)(lr + cs * -lr + sn * -(1 - lr)), 0, 0 },
                new float[] { (float)(lg + cs * -lg + sn * -lg), (float)(lg + cs * (1 - lg) + sn * 0.140), (float)(lg + cs * -lg + sn * lg), 0, 0 },
                new float[] { (float)(lb + cs * -lb + sn * (1 - lb)), (float)(lb + cs * -lb + sn * -0.283), (float)(lb + cs * (1 - lb) + sn * lb), 0, 0 },
                new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } };
            var m = Mul(Mul(H, S), C);
            return Matrix(src, new ColorMatrix(m), gamma);
        }
        static float[][] Mul(float[][] a, float[][] b)
        {
            var r = new float[5][];
            for (int i = 0; i < 5; i++) { r[i] = new float[5]; for (int j = 0; j < 5; j++) { float s = 0; for (int k = 0; k < 5; k++) s += a[i][k] * b[k][j]; r[i][j] = s; } }
            return r;
        }
        public static Bitmap Grayscale(Bitmap src)
        {
            var m = new float[][] { new float[] { .299f, .299f, .299f, 0, 0 }, new float[] { .587f, .587f, .587f, 0, 0 }, new float[] { .114f, .114f, .114f, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } };
            return Matrix(src, new ColorMatrix(m), 1f);
        }
        public static Bitmap Sepia(Bitmap src)
        {
            var m = new float[][] { new float[] { .393f, .349f, .272f, 0, 0 }, new float[] { .769f, .686f, .534f, 0, 0 }, new float[] { .189f, .168f, .131f, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } };
            return Matrix(src, new ColorMatrix(m), 1f);
        }
        public static Bitmap Invert(Bitmap src)
        {
            var m = new float[][] { new float[] { -1, 0, 0, 0, 0 }, new float[] { 0, -1, 0, 0, 0 }, new float[] { 0, 0, -1, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 1, 1, 1, 0, 1 } };
            return Matrix(src, new ColorMatrix(m), 1f);
        }
        public static Bitmap Posterize(Bitmap src, int levels)
        {
            levels = Math.Max(2, Math.Min(32, levels));
            int stride; var d = Bytes(src, out stride);
            double step = 255.0 / (levels - 1);
            for (int y = 0; y < src.Height; y++) for (int x = 0; x < src.Width; x++)
                { int i = y * stride + x * 4; for (int c = 0; c < 3; c++) d[i + c] = Clamp(Math.Round(d[i + c] / step) * step); }
            return FromBytes(d, src.Width, src.Height, stride);
        }
        // replace one colour (within a tolerance 0..255) by another
        public static Bitmap ReplaceColor(Bitmap src, Color from, Color to, int tolerance, bool toTransparent)
        {
            int stride; var d = Bytes(src, out stride);
            for (int y = 0; y < src.Height; y++) for (int x = 0; x < src.Width; x++)
                {
                    int i = y * stride + x * 4;
                    int dist = Math.Max(Math.Abs(d[i] - from.B), Math.Max(Math.Abs(d[i + 1] - from.G), Math.Abs(d[i + 2] - from.R)));
                    if (dist <= tolerance && d[i + 3] > 0)
                    {
                        // keep anti-aliased edges soft: blend by how close the pixel is
                        double k = tolerance == 0 ? 1 : 1 - dist / (double)(tolerance + 1) * 0.0;
                        if (toTransparent) d[i + 3] = 0;
                        else { d[i] = to.B; d[i + 1] = to.G; d[i + 2] = to.R; }
                    }
                }
            return FromBytes(d, src.Width, src.Height, stride);
        }
        public static Color PixelAt(Bitmap b, int x, int y)
        {
            x = Math.Max(0, Math.Min(b.Width - 1, x)); y = Math.Max(0, Math.Min(b.Height - 1, y));
            return b.GetPixel(x, y);
        }
        public static Bitmap FloodFill(Bitmap src, Point seed, Color fill, int tolerance, out Rectangle changed)
        {
            int w = src.Width, h = src.Height; changed = Rectangle.Empty;
            if (seed.X < 0 || seed.Y < 0 || seed.X >= w || seed.Y >= h) return To32(src);
            int stride; var d = Bytes(src, out stride);
            int si = seed.Y * stride + seed.X * 4; byte sb = d[si], sg = d[si + 1], sr = d[si + 2], sa = d[si + 3];
            var seen = new bool[w * h]; var stack = new Stack<int>(); stack.Push(seed.Y * w + seed.X);
            int minx = w, miny = h, maxx = 0, maxy = 0;
            while (stack.Count > 0)
            {
                int p = stack.Pop(); if (seen[p]) continue;
                int x = p % w, y = p / w;
                int i = y * stride + x * 4;
                if (Math.Max(Math.Max(Math.Abs(d[i] - sb), Math.Abs(d[i + 1] - sg)), Math.Max(Math.Abs(d[i + 2] - sr), Math.Abs(d[i + 3] - sa))) > tolerance) continue;
                seen[p] = true;
                d[i] = fill.B; d[i + 1] = fill.G; d[i + 2] = fill.R; d[i + 3] = fill.A;
                if (x < minx) minx = x; if (x > maxx) maxx = x; if (y < miny) miny = y; if (y > maxy) maxy = y;
                if (x > 0) stack.Push(p - 1); if (x < w - 1) stack.Push(p + 1); if (y > 0) stack.Push(p - w); if (y < h - 1) stack.Push(p + w);
            }
            changed = Rectangle.FromLTRB(minx, miny, maxx + 1, maxy + 1);
            return FromBytes(d, w, h, stride);
        }
        // pixels matching the clicked colour become a selection mask (magic wand)
        public static bool[] WandMask(Bitmap src, Point seed, int tolerance, bool contiguous)
        {
            int w = src.Width, h = src.Height; var mask = new bool[w * h];
            if (seed.X < 0 || seed.Y < 0 || seed.X >= w || seed.Y >= h) return mask;
            int stride; var d = Bytes(src, out stride);
            int si = seed.Y * stride + seed.X * 4; byte sb = d[si], sg = d[si + 1], sr = d[si + 2], sa = d[si + 3];
            Func<int, int, bool> match = delegate(int x, int y)
            {
                int i = y * stride + x * 4;
                return Math.Max(Math.Max(Math.Abs(d[i] - sb), Math.Abs(d[i + 1] - sg)), Math.Max(Math.Abs(d[i + 2] - sr), Math.Abs(d[i + 3] - sa))) <= tolerance;
            };
            if (!contiguous) { for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) mask[y * w + x] = match(x, y); return mask; }
            var stack = new Stack<int>(); stack.Push(seed.Y * w + seed.X);
            while (stack.Count > 0)
            {
                int p = stack.Pop(); if (mask[p]) continue; int x = p % w, y = p / w;
                if (!match(x, y)) continue; mask[p] = true;
                if (x > 0) stack.Push(p - 1); if (x < w - 1) stack.Push(p + 1); if (y > 0) stack.Push(p - w); if (y < h - 1) stack.Push(p + w);
            }
            return mask;
        }

        // ------------------------------------------------------------ filters
        public static Bitmap Convolve(Bitmap src, double[] k, double divisor, double bias)
        {
            int w = src.Width, h = src.Height, stride; var s = Bytes(src, out stride); var o = (byte[])s.Clone();
            for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        double acc = 0; int n = 0;
                        for (int ky = -1; ky <= 1; ky++) for (int kx = -1; kx <= 1; kx++) acc += s[(y + ky) * stride + (x + kx) * 4 + c] * k[n++];
                        o[y * stride + x * 4 + c] = Clamp(acc / divisor + bias);
                    }
            return FromBytes(o, w, h, stride);
        }
        public static Bitmap Sharpen(Bitmap src) { return Convolve(src, new double[] { 0, -1, 0, -1, 5, -1, 0, -1, 0 }, 1, 0); }
        public static Bitmap Emboss(Bitmap src) { return Convolve(src, new double[] { -2, -1, 0, -1, 1, 1, 0, 1, 2 }, 1, 128); }
        public static Bitmap EdgeDetect(Bitmap src) { return Convolve(src, new double[] { -1, -1, -1, -1, 8, -1, -1, -1, -1 }, 1, 0); }

        // three box passes approximate a gaussian; separable so it stays fast
        public static Bitmap GaussianBlur(Bitmap src, int radius)
        {
            if (radius < 1) return To32(src);
            int w = src.Width, h = src.Height, stride; var a = Bytes(src, out stride); var b = new byte[a.Length];
            for (int pass = 0; pass < 3; pass++) { BoxH(a, b, w, h, stride, radius); BoxV(b, a, w, h, stride, radius); }
            return FromBytes(a, w, h, stride);
        }
        static void BoxH(byte[] s, byte[] d, int w, int h, int stride, int r)
        {
            for (int y = 0; y < h; y++)
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0, cnt = 0, row = y * stride;
                    for (int x = -r; x <= r; x++) { int xx = Math.Max(0, Math.Min(w - 1, x)); sum += s[row + xx * 4 + c]; cnt++; }
                    for (int x = 0; x < w; x++)
                    {
                        d[row + x * 4 + c] = (byte)(sum / cnt);
                        int add = Math.Min(w - 1, x + r + 1), rem = Math.Max(0, x - r);
                        sum += s[row + add * 4 + c] - s[row + rem * 4 + c];
                    }
                }
        }
        static void BoxV(byte[] s, byte[] d, int w, int h, int stride, int r)
        {
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0, cnt = 0;
                    for (int y = -r; y <= r; y++) { int yy = Math.Max(0, Math.Min(h - 1, y)); sum += s[yy * stride + x * 4 + c]; cnt++; }
                    for (int y = 0; y < h; y++)
                    {
                        d[y * stride + x * 4 + c] = (byte)(sum / cnt);
                        int add = Math.Min(h - 1, y + r + 1), rem = Math.Max(0, y - r);
                        sum += s[add * stride + x * 4 + c] - s[rem * stride + x * 4 + c];
                    }
                }
        }
        public static Bitmap Pixelate(Bitmap src, Rectangle r, int block)
        {
            using (var piece = src.Clone(r, PixelFormat.Format32bppArgb))
            {
                var o = Blank(r.Width, r.Height, Color.Transparent);
                using (var small = new Bitmap(Math.Max(1, r.Width / block), Math.Max(1, r.Height / block)))
                {
                    using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(piece, 0, 0, small.Width, small.Height); }
                    using (var g = Graphics.FromImage(o)) { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(small, 0, 0, o.Width, o.Height); }
                }
                return o;
            }
        }
        public static Bitmap BlurRegion(Bitmap src, Rectangle r, int radius)
        {
            using (var piece = src.Clone(r, PixelFormat.Format32bppArgb)) return GaussianBlur(piece, Math.Max(1, radius));
        }

        // ------------------------------------------------------------ perspective
        // Map the image onto a quadrilateral inside a box of the same size.
        public static Bitmap Perspective(Bitmap src, PointF tl, PointF tr, PointF br, PointF bl, int outW, int outH)
        {
            int w = src.Width, h = src.Height, ss; var s = Bytes(src, out ss);
            var dst = new byte[outW * outH * 4];
            // homography from destination quad to the unit source rectangle
            double[] H = SolveHomography(new[] { tl, tr, br, bl }, new[] { new PointF(0, 0), new PointF(w, 0), new PointF(w, h), new PointF(0, h) });
            for (int y = 0; y < outH; y++)
                for (int x = 0; x < outW; x++)
                {
                    double den = H[6] * x + H[7] * y + 1;
                    double sx = (H[0] * x + H[1] * y + H[2]) / den, sy = (H[3] * x + H[4] * y + H[5]) / den;
                    if (sx < 0 || sy < 0 || sx > w - 1 || sy > h - 1) continue;
                    int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
                    double fx = sx - x0, fy = sy - y0;
                    int o = (y * outW + x) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        double v = s[y0 * ss + x0 * 4 + c] * (1 - fx) * (1 - fy) + s[y0 * ss + x1 * 4 + c] * fx * (1 - fy) + s[y1 * ss + x0 * 4 + c] * (1 - fx) * fy + s[y1 * ss + x1 * 4 + c] * fx * fy;
                        dst[o + c] = Clamp(v);
                    }
                }
            return FromBytes(dst, outW, outH, outW * 4);
        }
        static double[] SolveHomography(PointF[] from, PointF[] to)
        {
            var A = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                double x = from[i].X, y = from[i].Y, u = to[i].X, v = to[i].Y;
                A[2 * i, 0] = x; A[2 * i, 1] = y; A[2 * i, 2] = 1; A[2 * i, 6] = -u * x; A[2 * i, 7] = -u * y; A[2 * i, 8] = u;
                A[2 * i + 1, 3] = x; A[2 * i + 1, 4] = y; A[2 * i + 1, 5] = 1; A[2 * i + 1, 6] = -v * x; A[2 * i + 1, 7] = -v * y; A[2 * i + 1, 8] = v;
            }
            for (int c = 0; c < 8; c++)
            {
                int piv = c; for (int r = c + 1; r < 8; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[piv, c])) piv = r;
                for (int k = 0; k < 9; k++) { double t = A[c, k]; A[c, k] = A[piv, k]; A[piv, k] = t; }
                double d = A[c, c]; if (Math.Abs(d) < 1e-12) continue;
                for (int k = c; k < 9; k++) A[c, k] /= d;
                for (int r = 0; r < 8; r++) if (r != c) { double f = A[r, c]; for (int k = c; k < 9; k++) A[r, k] -= f * A[c, k]; }
            }
            var H = new double[8]; for (int i = 0; i < 8; i++) H[i] = A[i, 8];
            return H;
        }

        // Fill a hole by smearing the surrounding pixels inwards (used by Smart Move).
        public static void InpaintRect(Bitmap bmp, Rectangle hole)
        {
            hole.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height)); if (hole.Width < 1 || hole.Height < 1) return;
            int w = bmp.Width, h = bmp.Height, stride; var d = Bytes(bmp, out stride);
            // horizontal and vertical interpolation between the nearest pixels outside the hole, averaged
            for (int y = hole.Top; y < hole.Bottom; y++)
                for (int x = hole.Left; x < hole.Right; x++)
                {
                    int lx = Math.Max(0, hole.Left - 1), rx = Math.Min(w - 1, hole.Right), ty = Math.Max(0, hole.Top - 1), by = Math.Min(h - 1, hole.Bottom);
                    double tx = hole.Width <= 0 ? 0 : (x - hole.Left + 0.5) / hole.Width, tv = (y - hole.Top + 0.5) / hole.Height;
                    int o = y * stride + x * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        double hor = d[y * stride + lx * 4 + c] * (1 - tx) + d[y * stride + rx * 4 + c] * tx;
                        double ver = d[ty * stride + x * 4 + c] * (1 - tv) + d[by * stride + x * 4 + c] * tv;
                        d[o + c] = Clamp((hor + ver) / 2);
                    }
                }
            var tmp = FromBytes(d, w, h, stride);
            var rd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(d, 0, rd.Scan0, Math.Min(d.Length, Math.Abs(rd.Stride) * h));
            bmp.UnlockBits(rd); tmp.Dispose();
        }
    }
}
