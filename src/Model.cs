// Annotation object model. Every object can be moved, resized, rotated, faded and shadowed.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace SnapLite
{
    enum Dash { Solid, Dashed, Dotted }
    enum ShapeKind { Rect, Rounded, Ellipse, Triangle, Diamond, Hexagon, Star }
    enum PenMode { Pen, Marker, Highlighter }
    enum StepStyle { Number, Letter, Roman }
    enum BlurKind { Pixelate, Smooth }

    [Serializable]
    abstract class Obj
    {
        public PointF A, B;
        public Color Stroke = Theme.Mark;
        public Color Fill = Color.Transparent;
        public float Width = 3;
        public float Opacity = 1f;
        public bool Shadow;
        public Dash Dash = Dash.Solid;
        public float Rotation;          // degrees, about the bounds centre
        public bool Locked;

        [ThreadStatic] static bool shadowPass;
        protected static bool InShadowPass { get { return shadowPass; } }

        public abstract string Label { get; }
        public virtual bool IsLinear { get { return false; } }
        public virtual bool UsesPoints { get { return false; } }
        public virtual bool KeepsSquare { get { return false; } }
        public virtual bool Rotatable { get { return !IsLinear && !UsesPoints; } }

        protected abstract void DrawBody(Graphics g, Bitmap baseImg);

        public virtual RectangleF Bounds() { return Norm(A, B); }
        public PointF Center() { var r = Bounds(); return new PointF(r.X + r.Width / 2, r.Y + r.Height / 2); }

        public static RectangleF Norm(PointF a, PointF b)
        {
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }
        protected Color Fade(Color c)
        {
            if (shadowPass) return Color.FromArgb((int)(70 * Opacity * (c.A / 255f)), 0, 0, 0);
            return Color.FromArgb((int)Math.Round(c.A * Opacity), c);
        }
        protected Pen MakePen(float widthScale)
        {
            var p = new Pen(Fade(Stroke), Math.Max(0.5f, Width * widthScale))
            { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (Dash == Dash.Dashed) p.DashStyle = DashStyle.Dash;
            else if (Dash == Dash.Dotted) { p.DashStyle = DashStyle.Dot; }
            return p;
        }
        protected Pen MakePen() { return MakePen(1f); }

        public void Render(Graphics g, Bitmap baseImg)
        {
            var st = g.Save();
            if (Rotation != 0 && Rotatable)
            {
                var c = Center();
                g.TranslateTransform(c.X, c.Y); g.RotateTransform(Rotation); g.TranslateTransform(-c.X, -c.Y);
            }
            if (Shadow)
            {
                shadowPass = true;
                var s2 = g.Save();
                g.TranslateTransform(3, 3);
                DrawBody(g, baseImg);
                g.Restore(s2);
                shadowPass = false;
            }
            DrawBody(g, baseImg);
            g.Restore(st);
        }

        public PointF ToLocal(PointF p)
        {
            if (Rotation == 0 || !Rotatable) return p;
            var c = Center(); double a = -Rotation * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            float dx = p.X - c.X, dy = p.Y - c.Y;
            return new PointF(c.X + (float)(dx * cos - dy * sin), c.Y + (float)(dx * sin + dy * cos));
        }
        public bool HitTest(PointF p) { return HitLocal(ToLocal(p)); }
        protected virtual bool HitLocal(PointF p) { var r = Bounds(); r.Inflate(5, 5); return r.Contains(p); }

        public virtual void Translate(float dx, float dy) { A.X += dx; A.Y += dy; B.X += dx; B.Y += dy; }
        public virtual void SetBounds(RectangleF r)
        {
            if (KeepsSquare) { float m = Math.Max(r.Width, r.Height); r.Width = m; r.Height = m; }
            A = new PointF(r.Left, r.Top); B = new PointF(r.Right, r.Bottom);
        }
        public virtual void Scale(float sx, float sy)
        {
            A = new PointF(A.X * sx, A.Y * sy); B = new PointF(B.X * sx, B.Y * sy);
            Width = Math.Max(0.5f, Width * (sx + sy) / 2);
        }
        public virtual void MapPts(Func<PointF, PointF> f) { A = f(A); B = f(B); }
        public virtual Obj Clone() { return (Obj)MemberwiseClone(); }

        // whole-image 90-degree turns and flips keep an object's size and turn it with the picture
        public void RotateCW(float oldH)
        {
            if (!Rotatable) { MapPts(delegate(PointF p) { return new PointF(oldH - p.Y, p.X); }); return; }
            var r = Bounds(); var c = Center(); var nc = new PointF(oldH - c.Y, c.X);
            A = new PointF(nc.X - r.Width / 2, nc.Y - r.Height / 2); B = new PointF(nc.X + r.Width / 2, nc.Y + r.Height / 2);
            Rotation = (Rotation + 90) % 360;
        }
        public void RotateCCW(float oldW)
        {
            if (!Rotatable) { MapPts(delegate(PointF p) { return new PointF(p.Y, oldW - p.X); }); return; }
            var r = Bounds(); var c = Center(); var nc = new PointF(c.Y, oldW - c.X);
            A = new PointF(nc.X - r.Width / 2, nc.Y - r.Height / 2); B = new PointF(nc.X + r.Width / 2, nc.Y + r.Height / 2);
            Rotation = (Rotation + 270) % 360;
        }
        public void FlipH(float w)
        {
            if (!Rotatable) { MapPts(delegate(PointF p) { return new PointF(w - p.X, p.Y); }); return; }
            var r = Bounds(); float cx = w - Center().X, cy = Center().Y;
            A = new PointF(cx - r.Width / 2, cy - r.Height / 2); B = new PointF(cx + r.Width / 2, cy + r.Height / 2);
            Rotation = (360 - Rotation) % 360;
        }
        public void FlipV(float h)
        {
            if (!Rotatable) { MapPts(delegate(PointF p) { return new PointF(p.X, h - p.Y); }); return; }
            var r = Bounds(); float cx = Center().X, cy = h - Center().Y;
            A = new PointF(cx - r.Width / 2, cy - r.Height / 2); B = new PointF(cx + r.Width / 2, cy + r.Height / 2);
            Rotation = (360 - Rotation) % 360;
        }

        protected static float DistToSeg(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            float t = l2 <= 0 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2;
            t = Math.Max(0, Math.Min(1, t));
            float cx = a.X + t * dx, cy = a.Y + t * dy;
            return (float)Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
        }
    }

    // ---------------------------------------------------------------- line / arrow
    [Serializable]
    class LineObj : Obj
    {
        public bool ArrowStart, ArrowEnd = true;
        public override string Label { get { return ArrowEnd || ArrowStart ? "Arrow" : "Line"; } }
        public override bool IsLinear { get { return true; } }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            using (var p = MakePen())
            {
                float head = Math.Max(3f, 2.4f + 0.2f * Width);
                if (ArrowEnd) p.CustomEndCap = new AdjustableArrowCap(head, head * 1.15f, true);
                if (ArrowStart) p.CustomStartCap = new AdjustableArrowCap(head, head * 1.15f, true);
                g.DrawLine(p, A, B);
            }
        }
        protected override bool HitLocal(PointF p) { return DistToSeg(p, A, B) <= Math.Max(7, Width / 2 + 5); }
    }

    // ---------------------------------------------------------------- shapes
    [Serializable]
    class ShapeObj : Obj
    {
        public ShapeKind Kind = ShapeKind.Rect;
        public float Corner = 10;
        public override string Label { get { return Kind.ToString(); } }

        public static GraphicsPath PathFor(ShapeKind k, RectangleF r, float corner)
        {
            var gp = new GraphicsPath();
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            switch (k)
            {
                case ShapeKind.Rect: gp.AddRectangle(r); break;
                case ShapeKind.Rounded: return Theme.RoundRect(r, Math.Min(corner, Math.Min(r.Width, r.Height) / 2));
                case ShapeKind.Ellipse: gp.AddEllipse(r); break;
                case ShapeKind.Triangle: gp.AddPolygon(new[] { new PointF(cx, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) }); break;
                case ShapeKind.Diamond: gp.AddPolygon(new[] { new PointF(cx, r.Top), new PointF(r.Right, cy), new PointF(cx, r.Bottom), new PointF(r.Left, cy) }); break;
                case ShapeKind.Hexagon:
                    gp.AddPolygon(new[] { new PointF(r.X + r.Width * .25f, r.Top), new PointF(r.X + r.Width * .75f, r.Top), new PointF(r.Right, cy),
                        new PointF(r.X + r.Width * .75f, r.Bottom), new PointF(r.X + r.Width * .25f, r.Bottom), new PointF(r.Left, cy) }); break;
                case ShapeKind.Star:
                    var pts = new PointF[10];
                    for (int i = 0; i < 10; i++)
                    {
                        double a = -Math.PI / 2 + i * Math.PI / 5; float rr = i % 2 == 0 ? 1f : 0.42f;
                        pts[i] = new PointF(cx + (float)Math.Cos(a) * r.Width / 2 * rr, cy + (float)Math.Sin(a) * r.Height / 2 * rr);
                    }
                    gp.AddPolygon(pts); break;
            }
            return gp;
        }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            var r = Bounds();
            using (var path = PathFor(Kind, r, Corner))
            {
                if (Fill.A > 0) using (var b = new SolidBrush(Fade(Fill))) g.FillPath(b, path);
                if (Width > 0 && Stroke.A > 0) using (var p = MakePen()) g.DrawPath(p, path);
            }
        }
        protected override bool HitLocal(PointF p)
        {
            var r = Bounds();
            var outer = r; outer.Inflate(5, 5);
            if (!outer.Contains(p)) return false;
            if (Fill.A > 0) return true;
            float band = Math.Max(9, Width + 7);
            var inner = r; inner.Inflate(-band, -band);
            if (Kind == ShapeKind.Ellipse)
            {
                float rx = r.Width / 2 + 5, ry = r.Height / 2 + 5, dx = (p.X - (r.X + r.Width / 2)) / rx, dy = (p.Y - (r.Y + r.Height / 2)) / ry;
                if (dx * dx + dy * dy > 1) return false;
                float ix = Math.Max(1, r.Width / 2 - band), iy = Math.Max(1, r.Height / 2 - band);
                float ex = (p.X - (r.X + r.Width / 2)) / ix, ey = (p.Y - (r.Y + r.Height / 2)) / iy;
                return ex * ex + ey * ey >= 1;
            }
            return inner.Width <= 0 || inner.Height <= 0 || !inner.Contains(p);
        }
    }

    // ---------------------------------------------------------------- text and callout
    [Serializable]
    class TextObj : Obj
    {
        public string Text = "";
        public string FontName = Theme.BodyFamily;
        public float Px = 20;
        public bool Bold = true, Italic, Underline;
        public StringAlignment Align = StringAlignment.Near;
        public float WrapWidth;         // 0 = no wrap
        public SizeF Size;
        public override string Label { get { return "Text"; } }

        public TextObj() { Stroke = Theme.Ink; }
        public void Measure()
        {
            using (var bm = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bm)) using (var f = MakeFont())
            {
                var sz = WrapWidth > 0 ? g.MeasureString(Text, f, (int)WrapWidth) : g.MeasureString(Text, f);
                Size = new SizeF(WrapWidth > 0 ? WrapWidth : sz.Width, sz.Height);
            }
            B = new PointF(A.X + Size.Width, A.Y + Size.Height);
        }
        public Font MakeFont()
        {
            var st = (Bold ? FontStyle.Bold : 0) | (Italic ? FontStyle.Italic : 0) | (Underline ? FontStyle.Underline : 0);
            return new Font(FontName, Math.Max(6, Px), st, GraphicsUnit.Pixel);
        }
        public override void SetBounds(RectangleF r)
        {
            A = r.Location; WrapWidth = Math.Max(20, r.Width); Measure();
        }
        public override void Scale(float sx, float sy)
        {
            A = new PointF(A.X * sx, A.Y * sy); Px *= (sx + sy) / 2; if (WrapWidth > 0) WrapWidth *= sx; Measure();
        }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            var r = Bounds();
            if (Fill.A > 0) using (var b = new SolidBrush(Fade(Fill))) g.FillRectangle(b, r.X - 4, r.Y - 2, r.Width + 8, r.Height + 4);
            var old = g.TextRenderingHint; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (var f = MakeFont()) using (var b = new SolidBrush(Fade(Stroke)))
            using (var sf = new StringFormat { Alignment = Align })
                g.DrawString(Text, f, b, r, sf);
            g.TextRenderingHint = old;
        }
    }

    [Serializable]
    class CalloutObj : Obj
    {
        public string Text = "Text";
        public float Px = 18;
        public Color TextCol = Color.White;
        public PointF Tail = new PointF(-40, 70);      // offset of the tail tip from the centre, in unrotated space
        public ShapeKind Kind = ShapeKind.Rounded;
        public override string Label { get { return "Callout"; } }
        public CalloutObj() { Stroke = Theme.Mark; Fill = Theme.Mark; Width = 2; }

        PointF TailTip() { var c = Center(); return new PointF(c.X + Tail.X, c.Y + Tail.Y); }
        public override RectangleF Bounds() { return Norm(A, B); }
        public override void Scale(float sx, float sy) { base.Scale(sx, sy); Tail = new PointF(Tail.X * sx, Tail.Y * sy); Px *= (sx + sy) / 2; }
        public void SetTipAbsolute(PointF p) { var c = Center(); Tail = new PointF(p.X - c.X, p.Y - c.Y); }
        public PointF TipAbsolute() { return TailTip(); }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            var r = Bounds(); var tip = TailTip(); var c = Center();
            using (var body = ShapeObj.PathFor(Kind == ShapeKind.Rect ? ShapeKind.Rect : Kind == ShapeKind.Ellipse ? ShapeKind.Ellipse : ShapeKind.Rounded, r, 12))
            using (var tail = new GraphicsPath())
            {
                // tail: a narrow triangle from the body edge to the tip
                float ang = (float)Math.Atan2(tip.Y - c.Y, tip.X - c.X);
                float half = Math.Min(r.Width, r.Height) * 0.16f + 4;
                float px = -(float)Math.Sin(ang), py = (float)Math.Cos(ang);
                float rr = Math.Min(r.Width, r.Height) * 0.35f;
                var b1 = new PointF(c.X + px * half + (float)Math.Cos(ang) * rr, c.Y + py * half + (float)Math.Sin(ang) * rr);
                var b2 = new PointF(c.X - px * half + (float)Math.Cos(ang) * rr, c.Y - py * half + (float)Math.Sin(ang) * rr);
                tail.AddPolygon(new[] { b1, tip, b2 });
                using (var region = new Region(body)) { region.Union(tail);
                    if (Fill.A > 0) using (var br = new SolidBrush(Fade(Fill))) g.FillRegion(br, region); }
                if (Width > 0)
                    using (var p = MakePen())
                    {
                        // outline = body outline + tail edges outside the body
                        g.DrawPath(p, body);
                        var st = g.Save(); g.SetClip(body, CombineMode.Exclude); g.DrawPath(p, tail); g.Restore(st);
                    }
            }
            var old = g.TextRenderingHint; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var tr = r; tr.Inflate(-8, -6);
            using (var f = new Font(Theme.BodyFamily, Math.Max(6, Px), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Fade(TextCol)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Text, f, b, tr, sf);
            g.TextRenderingHint = old;
        }
        protected override bool HitLocal(PointF p)
        {
            var r = Bounds(); r.Inflate(5, 5);
            if (r.Contains(p)) return true;
            return DistToSeg(p, Center(), TailTip()) < 9;
        }
    }

    // ---------------------------------------------------------------- stamp and step
    [Serializable]
    class StampObj : Obj
    {
        public Ic Glyph = Ic.Check;
        public override string Label { get { return "Stamp"; } }
        public override bool KeepsSquare { get { return true; } }
        public StampObj() { Width = 1; }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            var r = Bounds();
            Icons.Draw(g, Glyph, r, Fade(Stroke));
        }
    }

    [Serializable]
    class StepObj : Obj
    {
        public int Number = 1;
        public StepStyle Style = StepStyle.Number;
        public override string Label { get { return "Step"; } }
        public override bool KeepsSquare { get { return true; } }
        public StepObj() { Fill = Theme.Mark; Stroke = Color.White; Width = 2; }

        public string Text()
        {
            if (Style == StepStyle.Letter) { int n = Math.Max(1, Number) - 1; string s = ""; do { s = (char)('A' + n % 26) + s; n = n / 26 - 1; } while (n >= 0); return s; }
            if (Style == StepStyle.Roman)
            {
                int n = Math.Max(1, Math.Min(3999, Number)); string s = "";
                int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
                string[] t = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
                for (int i = 0; i < v.Length; i++) while (n >= v[i]) { s += t[i]; n -= v[i]; }
                return s;
            }
            return Number.ToString();
        }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            var r = Bounds();
            using (var b = new SolidBrush(Fade(Fill))) g.FillEllipse(b, r);
            using (var p = new Pen(Fade(Stroke), Math.Max(1, Width))) g.DrawEllipse(p, r);
            string t = Text();
            using (var f = new Font(Theme.LabelFamily, r.Height * (t.Length > 2 ? 0.34f : t.Length > 1 ? 0.46f : 0.58f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Fade(Stroke)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                var old = g.TextRenderingHint; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.DrawString(t, f, br, new RectangleF(r.X, r.Y + r.Height * 0.03f, r.Width, r.Height), sf);
                g.TextRenderingHint = old;
            }
        }
    }

    // ---------------------------------------------------------------- freehand pen / marker / highlighter
    [Serializable]
    class PenObj : Obj
    {
        public List<PointF> Pts = new List<PointF>();
        public PenMode Mode = PenMode.Pen;
        public override string Label { get { return Mode.ToString(); } }
        public override bool UsesPoints { get { return true; } }
        public override bool Rotatable { get { return false; } }

        public override RectangleF Bounds()
        {
            if (Pts.Count == 0) return new RectangleF(A.X, A.Y, 0, 0);
            float x0 = Pts.Min(p => p.X), y0 = Pts.Min(p => p.Y), x1 = Pts.Max(p => p.X), y1 = Pts.Max(p => p.Y);
            return RectangleF.FromLTRB(x0, y0, x1, y1);
        }
        public override Obj Clone() { var o = (PenObj)MemberwiseClone(); o.Pts = new List<PointF>(Pts); return o; }
        public override void Translate(float dx, float dy)
        { for (int i = 0; i < Pts.Count; i++) Pts[i] = new PointF(Pts[i].X + dx, Pts[i].Y + dy); }
        public override void MapPts(Func<PointF, PointF> f) { for (int i = 0; i < Pts.Count; i++) Pts[i] = f(Pts[i]); }
        public override void Scale(float sx, float sy) { MapPts(delegate(PointF p) { return new PointF(p.X * sx, p.Y * sy); }); Width = Math.Max(0.5f, Width * (sx + sy) / 2); }
        public override void SetBounds(RectangleF r)
        {
            var o = Bounds(); float sx = o.Width < 1 ? 1 : r.Width / o.Width, sy = o.Height < 1 ? 1 : r.Height / o.Height;
            MapPts(delegate(PointF p) { return new PointF(r.X + (p.X - o.X) * sx, r.Y + (p.Y - o.Y) * sy); });
        }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            if (Pts.Count == 0) return;
            float w = Mode == PenMode.Highlighter ? Width * 4 : Mode == PenMode.Marker ? Width * 2 : Width;
            Color c = Mode == PenMode.Pen ? Stroke : Color.FromArgb(Mode == PenMode.Highlighter ? 105 : 190, Stroke);
            using (var p = new Pen(Fade(c), Math.Max(1, w)) { LineJoin = LineJoin.Round })
            {
                p.StartCap = Mode == PenMode.Highlighter ? LineCap.Square : LineCap.Round;
                p.EndCap = p.StartCap;
                if (Pts.Count == 1) { g.FillEllipse(new SolidBrush(p.Color), Pts[0].X - w / 2, Pts[0].Y - w / 2, w, w); return; }
                if (Pts.Count == 2) g.DrawLine(p, Pts[0], Pts[1]);
                else g.DrawCurve(p, Pts.ToArray(), 0.4f);
            }
        }
        protected override bool HitLocal(PointF p)
        {
            float tol = Math.Max(8, Width * (Mode == PenMode.Highlighter ? 2 : 1) + 5);
            for (int i = 1; i < Pts.Count; i++) if (DistToSeg(p, Pts[i - 1], Pts[i]) <= tol) return true;
            return Pts.Count == 1 && Math.Abs(p.X - Pts[0].X) < tol && Math.Abs(p.Y - Pts[0].Y) < tol;
        }
    }

    // ---------------------------------------------------------------- redaction / focus / zoom
    [Serializable]
    class BlurObj : Obj
    {
        public BlurKind Kind = BlurKind.Pixelate;
        public int Strength = 10;
        public override string Label { get { return "Blur"; } }
        public BlurObj() { Width = 0; }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            var r = Rectangle.Round(Bounds());
            r.Intersect(new Rectangle(0, 0, baseImg.Width, baseImg.Height));
            if (r.Width < 2 || r.Height < 2) return;
            using (var piece = Kind == BlurKind.Pixelate ? Effects.Pixelate(baseImg, r, Math.Max(2, Strength)) : Effects.BlurRegion(baseImg, r, Strength))
                g.DrawImage(piece, r);
        }
    }

    [Serializable]
    class SpotlightObj : Obj
    {
        public ShapeKind Kind = ShapeKind.Rounded;
        public int Dim = 150;
        public override string Label { get { return "Spotlight"; } }
        public SpotlightObj() { Width = 0; }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddRectangle(new RectangleF(-4, -4, baseImg.Width + 8, baseImg.Height + 8));
                path.AddPath(ShapeObj.PathFor(Kind, Bounds(), 12), false);
                using (var b = new SolidBrush(Fade(Color.FromArgb(Dim, 8, 14, 22)))) g.FillPath(b, path);
            }
        }
        protected override bool HitLocal(PointF p) { var r = Bounds(); return r.Contains(p); }
    }

    [Serializable]
    class MagnifyObj : Obj
    {
        public float Zoom = 2f;
        public bool Round = true;
        public override string Label { get { return "Magnify"; } }
        public MagnifyObj() { Stroke = Theme.Cobalt; Width = 3; Shadow = true; }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            var r = Bounds(); if (r.Width < 4 || r.Height < 4) return;
            float sw = r.Width / Zoom, sh = r.Height / Zoom;
            var src = new RectangleF(r.X + (r.Width - sw) / 2, r.Y + (r.Height - sh) / 2, sw, sh);
            using (var path = Round ? ShapeObj.PathFor(ShapeKind.Ellipse, r, 0) : ShapeObj.PathFor(ShapeKind.Rounded, r, 8))
            {
                var st = g.Save(); g.SetClip(path, CombineMode.Intersect);
                var oi = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var op = g.PixelOffsetMode; g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    if (!Rectangle.Round(r).IsEmpty)
                        g.DrawImage(baseImg, Rectangle.Round(r), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
                g.InterpolationMode = oi; g.PixelOffsetMode = op; g.Restore(st);
                using (var p = MakePen()) g.DrawPath(p, path);
            }
        }
    }

    // ---------------------------------------------------------------- pasted / lifted image
    [Serializable]
    class ImageObj : Obj
    {
        [NonSerialized] public Bitmap Img;
        byte[] png;
        [OnSerializing]
        void OnSer(StreamingContext c)
        {
            if (Img == null) { png = null; return; }
            using (var ms = new MemoryStream()) { Img.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
        }
        [OnDeserialized]
        void OnDeser(StreamingContext c)
        {
            if (png == null) return;
            using (var ms = new MemoryStream(png)) using (var im = Image.FromStream(ms)) Img = new Bitmap(im);
            png = null;
        }
        public override string Label { get { return "Image"; } }
        public ImageObj() { Width = 0; }
        protected override void DrawBody(Graphics g, Bitmap baseImg)
        {
            if (Img == null) return;
            var r = Bounds();
            var oi = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var op = g.PixelOffsetMode; g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix { Matrix33 = InShadowPass ? 0.28f * Opacity : Opacity };
                if (InShadowPass) { cm.Matrix00 = cm.Matrix11 = cm.Matrix22 = 0; }
                ia.SetColorMatrix(cm);
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(Img, Rectangle.Round(r), 0, 0, Img.Width, Img.Height, GraphicsUnit.Pixel, ia);
            }
            g.InterpolationMode = oi; g.PixelOffsetMode = op;
            if (Width > 0 && Stroke.A > 0) using (var p = MakePen()) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        }
    }
}

