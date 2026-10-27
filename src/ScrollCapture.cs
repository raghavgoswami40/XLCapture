// Scrolling capture: scroll the content under a region, grab each step, and stitch by matching overlapping rows.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SnapLite
{
    static class ScrollCapture
    {
        const int VK_ESCAPE = 0x1B;

        // gray samples of a frame: rows x sampled columns
        class Frame { public byte[] G; public int W, H; public Bitmap Bmp; }

        static Frame Prep(Bitmap b)
        {
            int stride; var d = Effects.Bytes(b, out stride);
            int step = Math.Max(1, b.Width / 160), cols = (b.Width + step - 1) / step;
            var g = new byte[cols * b.Height];
            for (int y = 0; y < b.Height; y++) for (int i = 0; i < cols; i++)
                { int o = y * stride + Math.Min(b.Width - 1, i * step) * 4; g[y * cols + i] = (byte)((d[o] * 29 + d[o + 1] * 150 + d[o + 2] * 77) >> 8); }
            return new Frame { G = g, W = cols, H = b.Height, Bmp = b };
        }
        static double RowDiff(Frame a, int ra, Frame b, int rb)
        {
            int s = 0; int ia = ra * a.W, ib = rb * b.W;
            for (int i = 0; i < a.W; i++) s += Math.Abs(a.G[ia + i] - b.G[ib + i]);
            return s / (double)a.W;
        }
        // how far did the content move between prev and cur? (rows). Returns -1 if no confident match.
        static int FindShift(Frame prev, Frame cur, out double best)
        {
            int H = prev.H; best = double.MaxValue; int bestS = -1;
            int bandTop = (int)(H * 0.22), bandBot = (int)(H * 0.5);
            for (int s = 1; s < (int)(H * 0.92); s++)
            {
                int lo = bandTop, hi = Math.Min(bandBot, H - s);       // rows of cur that exist in prev after shifting by s
                if (hi - lo < 24) { lo = Math.Max(0, H - s - 60); hi = H - s; if (hi - lo < 12) break; }
                double sum = 0; int n = 0;
                for (int r = lo; r < hi; r += 2) { sum += RowDiff(prev, r + s, cur, r); n++; if (sum / n > best * 1.6 + 6 && n > 8) { sum = double.MaxValue; n = 1; break; } }
                double score = sum / Math.Max(1, n);
                if (score < best) { best = score; bestS = s; }
            }
            return bestS;
        }
        static double SameScore(Frame a, Frame b)
        {
            double sum = 0; int n = 0; for (int r = 0; r < a.H; r += 3) { sum += RowDiff(a, r, b, r); n++; } return sum / Math.Max(1, n);
        }

        public static Bitmap Stitch(List<Bitmap> shots, out string note)
        {
            note = "";
            if (shots.Count == 0) return null;
            var parts = new List<Bitmap> { shots[0] }; var prev = Prep(shots[0]);
            int total = shots[0].Height;
            var heights = new List<int> { shots[0].Height };
            for (int i = 1; i < shots.Count; i++)
            {
                var cur = Prep(shots[i]);
                if (SameScore(prev, cur) < 0.6) { note = "Reached the end of the page."; break; }
                double score; int s = FindShift(prev, cur, out score);
                if (s <= 0 || score > 14) { note = "Stopped: the page content changed while scrolling (matching score " + score.ToString("0.0") + ")."; break; }
                // new rows are the bottom s rows of cur
                var piece = shots[i].Clone(new Rectangle(0, shots[i].Height - s, shots[i].Width, s), PixelFormat.Format32bppArgb);
                parts.Add(piece); total += s; prev = cur;
            }
            var result = new Bitmap(shots[0].Width, total, PixelFormat.Format32bppArgb); result.SetResolution(96, 96);
            using (var g = Graphics.FromImage(result)) { int y = 0; foreach (var p in parts) { g.DrawImage(p, 0, y, p.Width, p.Height); y += p.Height; } }
            foreach (var p in parts) if (!ReferenceEquals(p, shots[0]) && !shots.Contains(p)) p.Dispose();
            return result;
        }

        // Scroll the area and grab as it moves. horizontal=true scrolls sideways (panorama).
        public static Bitmap Capture(Rectangle screenRect, bool horizontal, int maxSteps, Action<string> progress, out string note)
        {
            var shots = new List<Bitmap>(); note = "";
            var oldPos = Native.CursorPos;
            try
            {
                var centre = new Point(screenRect.X + screenRect.Width / 2, screenRect.Y + screenRect.Height / 2);
                Native.SetCursorPos(centre.X, centre.Y); Thread.Sleep(250);
                int notches = 3;
                for (int i = 0; i < maxSteps; i++)
                {
                    if ((Native.GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) { note = "Stopped with Esc."; break; }
                    var bmp = Native.Grab(screenRect, false);
                    if (horizontal) bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    shots.Add(bmp);
                    if (progress != null) progress("Scrolling capture: step " + (i + 1) + " (Esc stops)");
                    if (shots.Count >= 2)
                    {
                        // end of page: the last two grabs are the same
                        if (SameScore(Prep(shots[shots.Count - 2]), Prep(shots[shots.Count - 1])) < 0.6) break;
                    }
                    if (horizontal) Native.MouseWheelH(notches); else Native.MouseWheel(notches);
                    Thread.Sleep(380);
                    Application.DoEvents();
                    // after the first move, tune the step so the page moves about 40% of the area
                    if (i == 0)
                    {
                        var probe = Native.Grab(screenRect, false); if (horizontal) probe.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        double sc; int s = FindShift(Prep(shots[0]), Prep(probe), out sc); probe.Dispose();
                        if (s > 0 && sc < 14) notches = Math.Max(1, Math.Min(12, (int)Math.Round(notches * (shots[0].Height * 0.42) / s)));
                        else if (s <= 0) { note = "The area did not scroll."; break; }
                    }
                }
            }
            finally { Native.SetCursorPos(oldPos.X, oldPos.Y); }
            if (shots.Count == 0) return null;
            string n2; var stitched = Stitch(shots, out n2);
            if (note.Length == 0) note = n2;
            foreach (var s in shots) s.Dispose();
            if (horizontal && stitched != null) { stitched.RotateFlip(RotateFlipType.Rotate270FlipNone); }
            return stitched;
        }
    }
}
