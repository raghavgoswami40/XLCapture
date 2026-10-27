// Editor building blocks: Quick Styles grid and the recent-captures tray.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SnapLite
{
    class QStyle
    {
        public Color Stroke, Fill = Color.Transparent, TextCol = Color.White;
        public float Width = 4; public bool Shadow, ArrowStart, ArrowEnd = true; public Dash Dash = Dash.Solid;
        public ShapeKind Shape = ShapeKind.Rect; public StepStyle Step; public BlurKind Blur; public int Strength = 10, Dim = 150; public float Zoom = 2; public bool Round = true;
    }

    static class QuickStyles
    {
        public static readonly string[] Themes = { "Light table", "Bright", "Muted", "Mono" };
        static Color H(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        public static Color[] Palette(string theme)
        {
            switch (theme)
            {
                case "Bright": return new[] { H(0xE53935), H(0x43A047), H(0x1E88E5), H(0xFDD835) };
                case "Muted": return new[] { H(0xB5523B), H(0x4E7A5A), H(0x4A6FA5), H(0xC9A227) };
                case "Mono": return new[] { H(0x14202B), H(0x5B6B7A), H(0x9AA6B2), H(0xFFFFFF) };
                default: return new[] { Theme.Mark, Theme.Ok, Theme.Cobalt, H(0xF5C518) };
            }
        }
        // the grid for one tool kind: rows are variants, columns are the theme's four colours
        public static List<QStyle> For(string kind, string theme)
        {
            var c = Palette(theme); var l = new List<QStyle>();
            Action<Func<Color, QStyle>> row = delegate(Func<Color, QStyle> f) { foreach (var col in c) l.Add(f(col)); };
            switch (kind)
            {
                case "arrow":
                    row(x => new QStyle { Stroke = x, Width = 8 });
                    row(x => new QStyle { Stroke = x, Width = 8, Shadow = true });
                    row(x => new QStyle { Stroke = x, Width = 5, Dash = Dash.Dashed, ArrowStart = true });
                    break;
                case "line":
                    row(x => new QStyle { Stroke = x, Width = 5, ArrowEnd = false });
                    row(x => new QStyle { Stroke = x, Width = 5, ArrowEnd = false, Dash = Dash.Dashed });
                    row(x => new QStyle { Stroke = x, Width = 3, ArrowEnd = false, Dash = Dash.Dotted, Shadow = true });
                    break;
                case "shape":
                    row(x => new QStyle { Stroke = x, Width = 5, Shape = ShapeKind.Rounded });
                    row(x => new QStyle { Stroke = x, Width = 0, Fill = x, Shape = ShapeKind.Rect, Shadow = true });
                    row(x => new QStyle { Stroke = x, Width = 4, Fill = Color.FromArgb(60, x), Shape = ShapeKind.Ellipse });
                    break;
                case "callout":
                    row(x => new QStyle { Stroke = x, Fill = x, TextCol = x.GetBrightness() > 0.7f ? Theme.Ink : Color.White, Width = 2, Shape = ShapeKind.Rounded });
                    row(x => new QStyle { Stroke = x, Fill = Color.White, TextCol = Theme.Ink, Width = 3, Shape = ShapeKind.Rounded });
                    row(x => new QStyle { Stroke = x, Fill = x, TextCol = x.GetBrightness() > 0.7f ? Theme.Ink : Color.White, Width = 2, Shape = ShapeKind.Ellipse, Shadow = true });
                    break;
                case "text":
                    row(x => new QStyle { Stroke = x });
                    row(x => new QStyle { Stroke = x.GetBrightness() > 0.7f ? Theme.Ink : Color.White, Fill = x });
                    row(x => new QStyle { Stroke = x, Shadow = true });
                    break;
                case "step":
                    row(x => new QStyle { Fill = x, Stroke = x.GetBrightness() > 0.7f ? Theme.Ink : Color.White, Width = 3 });
                    row(x => new QStyle { Fill = Color.White, Stroke = x, Width = 3, Shadow = true });
                    break;
                case "stamp":
                    row(x => new QStyle { Stroke = x });
                    row(x => new QStyle { Stroke = x, Shadow = true });
                    break;
                case "pen":
                    row(x => new QStyle { Stroke = x, Width = 3 });
                    row(x => new QStyle { Stroke = x, Width = 7 });
                    row(x => new QStyle { Stroke = x, Width = 12 });
                    break;
                case "blur":
                    foreach (var s in new[] { 6, 12, 20 }) l.Add(new QStyle { Blur = BlurKind.Pixelate, Strength = s });
                    foreach (var s in new[] { 6, 14 }) l.Add(new QStyle { Blur = BlurKind.Smooth, Strength = s });
                    break;
                case "spot":
                    foreach (var d in new[] { 90, 150, 210 }) l.Add(new QStyle { Shape = ShapeKind.Rounded, Dim = d });
                    foreach (var d in new[] { 120, 190 }) l.Add(new QStyle { Shape = ShapeKind.Ellipse, Dim = d });
                    break;
                case "magnify":
                    foreach (var z in new[] { 1.5f, 2f, 3f }) l.Add(new QStyle { Zoom = z, Round = true, Stroke = Theme.Cobalt, Width = 3 });
                    foreach (var z in new[] { 2f, 3f }) l.Add(new QStyle { Zoom = z, Round = false, Stroke = Theme.Ink, Width = 3 });
                    break;
            }
            return l;
        }

        public static void Apply(QStyle q, string kind, ToolStyle s)
        {
            s.Shadow = q.Shadow;
            switch (kind)
            {
                case "arrow": case "line": s.Stroke = q.Stroke; s.Width = q.Width; s.Dash = q.Dash; s.ArrowStart = q.ArrowStart; s.ArrowEnd = q.ArrowEnd; break;
                case "shape": s.Stroke = q.Stroke; s.Width = q.Width; s.Fill = q.Fill; s.Shape = q.Shape; break;
                case "callout": s.Stroke = q.Stroke; s.Fill = q.Fill; s.TextCol = q.TextCol; s.Width = q.Width; s.Shape = q.Shape; break;
                case "text": s.Stroke = q.Stroke; s.Fill = q.Fill; break;
                case "step": s.Fill = q.Fill; s.Stroke = q.Stroke; break;
                case "stamp": s.Stroke = q.Stroke; break;
                case "pen": s.Stroke = q.Stroke; s.Width = q.Width; break;
                case "blur": s.Blur = q.Blur; s.BlurStrength = q.Strength; break;
                case "spot": s.SpotKind = q.Shape; s.SpotDim = q.Dim; break;
                case "magnify": s.MagZoom = q.Zoom; s.MagRound = q.Round; s.Stroke = q.Stroke; s.Width = q.Width; break;
            }
        }

        // a sample object drawn inside the tile, so the tile shows exactly what the tool will make
        public static Obj Sample(QStyle q, string kind, RectangleF r, Ic glyph)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            switch (kind)
            {
                case "arrow": case "line":
                    return new LineObj { A = new PointF(r.X + r.Width * 0.18f, r.Bottom - r.Height * 0.2f), B = new PointF(r.Right - r.Width * 0.18f, r.Y + r.Height * 0.2f), Stroke = q.Stroke, Width = Math.Min(q.Width, 6), Dash = q.Dash, Shadow = q.Shadow, ArrowStart = q.ArrowStart, ArrowEnd = q.ArrowEnd };
                case "shape": return new ShapeObj { A = new PointF(r.X + 10, r.Y + 10), B = new PointF(r.Right - 10, r.Bottom - 10), Stroke = q.Stroke, Width = Math.Min(q.Width, 4), Fill = q.Fill, Kind = q.Shape, Corner = 8, Shadow = q.Shadow };
                case "callout":
                    var co = new CalloutObj { A = new PointF(r.X + 7, r.Y + 7), B = new PointF(r.Right - 7, r.Bottom - 18), Stroke = q.Stroke, Fill = q.Fill, TextCol = q.TextCol, Width = 2, Kind = q.Shape, Text = "Aa", Px = 14, Shadow = q.Shadow };
                    co.Tail = new PointF(-12, 22); return co;
                case "text":
                    var t = new TextObj { A = new PointF(r.X + 10, cy - 12), Text = "Text", Px = 20, Stroke = q.Stroke, Fill = q.Fill, Shadow = q.Shadow }; t.Measure(); t.A = new PointF(cx - t.Size.Width / 2, cy - t.Size.Height / 2); t.Measure(); return t;
                case "step": return new StepObj { A = new PointF(cx - 17, cy - 17), B = new PointF(cx + 17, cy + 17), Number = 1, Fill = q.Fill, Stroke = q.Stroke, Width = 2, Shadow = q.Shadow };
                case "stamp": return new StampObj { A = new PointF(cx - 18, cy - 18), B = new PointF(cx + 18, cy + 18), Glyph = glyph, Stroke = q.Stroke, Shadow = q.Shadow };
                case "pen":
                    var p = new PenObj { Stroke = q.Stroke, Width = Math.Min(q.Width, 9) };
                    for (int i = 0; i <= 12; i++) { float u = i / 12f; p.Pts.Add(new PointF(r.X + 10 + u * (r.Width - 20), cy + (float)Math.Sin(u * Math.PI * 2) * r.Height * 0.25f)); }
                    return p;
                case "blur": return new BlurObj { A = new PointF(r.X + 6, r.Y + 6), B = new PointF(r.Right - 6, r.Bottom - 6), Kind = q.Blur, Strength = Math.Max(2, q.Strength / 2) };
                case "spot": return new SpotlightObj { A = new PointF(r.X + 16, r.Y + 12), B = new PointF(r.Right - 16, r.Bottom - 12), Kind = q.Shape, Dim = q.Dim };
                case "magnify": return new MagnifyObj { A = new PointF(cx - 22, cy - 22), B = new PointF(cx + 22, cy + 22), Zoom = q.Zoom, Round = q.Round, Stroke = q.Stroke, Width = 2, Shadow = false };
            }
            return null;
        }
    }

    // grid of style tiles
    class StyleGrid : Control
    {
        List<QStyle> styles = new List<QStyle>(); string kind = ""; Ic glyph = Ic.Check; int hover = -1;
        public int Selected = -1; public event Action<QStyle> Picked;
        static Bitmap sampleBase;
        readonly int cols = 4;
        int Cell { get { return (Width - Dpi.S(12)) / cols; } }

        public StyleGrid() { DoubleBuffered = true; Cursor = Theme.Hand; BackColor = Theme.Sheet; TabStop = true; AccessibleName = "Quick styles"; }
        public void Set(string kind, string theme, Ic stampGlyph)
        {
            this.kind = kind; glyph = stampGlyph; styles = QuickStyles.For(kind, theme); Selected = -1; hover = -1;
            int rows = styles.Count == 0 ? 1 : (styles.Count + cols - 1) / cols;
            Height = styles.Count == 0 ? Dpi.S(46) : rows * Cell + Dpi.S(12);
            Invalidate();
        }
        static Bitmap SampleBase(int w, int h)
        {
            if (sampleBase != null && sampleBase.Width >= w && sampleBase.Height >= h) return sampleBase;
            sampleBase = new Bitmap(Math.Max(w, 200), Math.Max(h, 120));
            using (var g = Graphics.FromImage(sampleBase))
            {
                g.Clear(Color.White); var rnd = new Random(4);
                for (int y = 6; y < sampleBase.Height; y += 9) using (var b = new SolidBrush(Color.FromArgb(70 + rnd.Next(80), 90, 110, 140))) g.FillRectangle(b, 6 + rnd.Next(10), y, 30 + rnd.Next(sampleBase.Width - 50), 4);
            }
            return sampleBase;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Sheet);
            if (styles.Count == 0)
            {
                using (var f = Theme.Body(9f)) g.DrawString("This tool has no quick styles.", f, new SolidBrush(Theme.Slate), Dpi.S(12), Dpi.S(14));
                return;
            }
            int cell = Cell;
            for (int i = 0; i < styles.Count; i++)
            {
                var r = new Rectangle(Dpi.S(6) + (i % cols) * cell, Dpi.S(6) + (i / cols) * cell, cell - Dpi.S(6), cell - Dpi.S(6));
                using (var p = Theme.RoundRect(r, Dpi.S(4))) using (var b = new SolidBrush(kind == "blur" || kind == "spot" || kind == "magnify" ? Color.White : i == hover ? Color.White : Color.FromArgb(0xFB, 0xFC, 0xFD))) g.FillPath(b, p);
                // draw the sample in a 64-unit space scaled into the tile
                var st = g.Save();
                float k = r.Width / 64f; g.TranslateTransform(r.X, r.Y); g.ScaleTransform(k, k); g.SetClip(new RectangleF(0, 0, 64, 64));
                var sb = SampleBase(64, 64);
                if (kind == "blur" || kind == "spot" || kind == "magnify") g.DrawImage(sb, 0, 0, 64, 64);
                var o = QuickStyles.Sample(styles[i], kind, new RectangleF(0, 0, 64, 64), glyph);
                if (o != null) o.Render(g, sb);
                g.Restore(st);
                using (var p = Theme.RoundRect(r, Dpi.S(4))) using (var pen = new Pen(i == Selected ? Theme.Cobalt : i == hover ? Theme.Mist : Theme.Rule, i == Selected ? Dpi.F(2.5f) : 1f)) g.DrawPath(pen, p);
            }
            if (Focused) using (var pen = new Pen(Theme.Cobalt) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        int At(Point p) { int cell = Cell; int c = (p.X - Dpi.S(6)) / cell, r = (p.Y - Dpi.S(6)) / cell; int i = r * cols + c; return c >= 0 && c < cols && r >= 0 && i < styles.Count ? i : -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { int i = At(e.Location); if (i >= 0) Pick(i); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (styles.Count == 0) return;
            int s = Math.Max(0, Selected);
            if (e.KeyCode == Keys.Right) s = Math.Min(styles.Count - 1, s + 1); else if (e.KeyCode == Keys.Left) s = Math.Max(0, s - 1);
            else if (e.KeyCode == Keys.Down) s = Math.Min(styles.Count - 1, s + cols); else if (e.KeyCode == Keys.Up) s = Math.Max(0, s - cols);
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Pick(s); return; } else return;
            Selected = s; Invalidate(); e.Handled = true;
        }
        protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Left || c == Keys.Right || c == Keys.Up || c == Keys.Down || base.IsInputKey(k); }
        void Pick(int i) { Selected = i; Invalidate(); if (Picked != null) Picked(styles[i]); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); int rows = styles.Count == 0 ? 1 : (styles.Count + cols - 1) / cols; int h = styles.Count == 0 ? Dpi.S(46) : rows * Cell + Dpi.S(12); if (Height != h) Height = h; Invalidate(); }
    }

    // ------------------------------------------------------------------ recent tray
    class TrayItem { public string Path; public Doc Doc; public string Badge; }

    // Slim horizontal scrollbar: charcoal thumb on a pale track, drag the thumb or click the track.
    class SlimScrollBar : Control
    {
        public int Max, Page, Value;                       // Max = furthest offset; Page = visible width; content = Max + Page
        public event EventHandler ValueChanged;
        bool drag, over; int grab;
        public SlimScrollBar() { DoubleBuffered = true; SetStyle(ControlStyles.Selectable, false); Cursor = Theme.Hand; BackColor = Theme.Paper; AccessibleName = "Scroll recent captures"; AccessibleRole = AccessibleRole.ScrollBar; }
        Rectangle Track { get { return new Rectangle(Dpi.S(8), Dpi.S(4), Math.Max(1, Width - Dpi.S(16)), Height - Dpi.S(8)); } }
        Rectangle Thumb
        {
            get
            {
                var t = Track; int total = Max + Math.Max(1, Page);
                int w = Math.Max(Dpi.S(36), (int)((long)t.Width * Math.Max(1, Page) / total)); w = Math.Min(w, t.Width);
                int x = Max <= 0 ? t.X : t.X + (int)((long)(t.Width - w) * Value / Max);
                return new Rectangle(x, t.Y, w, t.Height);
            }
        }
        void Set(int v) { v = Math.Max(0, Math.Min(Max, v)); if (v == Value) return; Value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        public void Sync(int value, int max, int page) { Value = value; Max = max; Page = page; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; using (var b = new SolidBrush(BackColor)) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None; g.FillRectangle(b, ClientRectangle); }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var t = Track; var th = Thumb;
            using (var p = Rounded(t, t.Height / 2)) using (var b = new SolidBrush(Theme.Rule)) g.FillPath(b, p);
            if (Max <= 0) return;       // everything fits: just the empty track
            using (var p = Rounded(th, th.Height / 2)) using (var b = new SolidBrush(drag || over ? Theme.Charcoal : Theme.Mix(Theme.Charcoal, Theme.Rule, 0.35f))) g.FillPath(b, p);
        }
        static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath(); int d = Math.Max(2, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 90, 180); p.AddArc(r.Right - d, r.Y, d, d, 270, 180); p.CloseFigure(); return p;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || Max <= 0) return;
            var th = Thumb;
            if (th.Contains(e.Location)) { drag = true; grab = e.X - th.X; Capture = true; }
            else Set(Value + (e.X < th.X ? -1 : 1) * Math.Max(1, Page - Dpi.S(40)));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool o = Thumb.Contains(e.Location); if (o != over) { over = o; Invalidate(); }
            if (!drag) return;
            var t = Track; int w = Thumb.Width; int span = Math.Max(1, t.Width - w);
            Set((int)((long)(e.X - grab - t.X) * Max / span));
        }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { if (over) { over = false; Invalidate(); } }
        protected override void OnMouseWheel(MouseEventArgs e) { Set(Value - e.Delta); }
    }

    class RecentTray : Control
    {
        public readonly List<TrayItem> Items = new List<TrayItem>();
        public string CurrentPath; public Doc CurrentDoc;
        public event Action<TrayItem> Open, Context, Delete;
        int hover = -1; int scroll; bool overDelete;
        readonly ToolTip tip = new ToolTip();
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int CellW { get { return Dpi.S(184); } }

        public RecentTray() { DoubleBuffered = true; Cursor = Theme.Hand; BackColor = Theme.Paper; TabStop = true; AccessibleName = "Recent captures"; AccessibleDescription = "Arrow keys move between captures. Delete removes the selected one."; }

        Rectangle CellRect(int i) { return new Rectangle(Dpi.S(8) - scroll + i * (CellW + Dpi.S(8)), Dpi.S(8), CellW, Height - Dpi.S(16)); }
        // the delete button that appears in the top-right corner of the card under the pointer
        Rectangle DeleteRect(int i) { var r = CellRect(i); int s = Dpi.S(30); return new Rectangle(r.Right - s - Dpi.S(6), r.Y + Dpi.S(6), s, s); }

        static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static void Forget(string path) { lock (cache) { Bitmap b; if (cache.TryGetValue(path, out b) && b != null) b.Dispose(); cache.Remove(path); } }

        public void ScrollToCurrent()
        {
            int i = Items.FindIndex(t => IsCurrent(t)); if (i < 0) return;
            int x = i * (CellW + Dpi.S(8)); if (x < scroll || x + CellW > scroll + Width) scroll = Math.Max(0, x - Dpi.S(20)); Clamp(); Invalidate();
        }
        void Clamp() { int max = ScrollMax; scroll = Math.Max(0, Math.Min(max, scroll)); if (Scrolled != null) Scrolled(this, EventArgs.Empty); }
        // for the scrollbar under the tray
        public event EventHandler Scrolled;
        public int ScrollMax { get { return Math.Max(0, Items.Count * (CellW + Dpi.S(8)) + Dpi.S(16) - Width); } }
        public int ScrollValue { get { return scroll; } set { scroll = value; Clamp(); Invalidate(); } }
        public int ContentWidth { get { return Items.Count * (CellW + Dpi.S(8)) + Dpi.S(16); } }
        public void Resync() { Clamp(); Invalidate(); }
        bool IsCurrent(TrayItem t) { return (t.Doc != null && ReferenceEquals(t.Doc, CurrentDoc)) || (t.Path != null && CurrentPath != null && string.Equals(t.Path, CurrentPath, StringComparison.OrdinalIgnoreCase)); }

        Bitmap Thumb(TrayItem t)
        {
            if (t.Doc != null && t.Path == null) return null;
            string key = t.Path; Bitmap b;
            lock (cache) if (cache.TryGetValue(key, out b)) return b;
            if (t.Badge == "MP4" || t.Badge == "AVI") return null;
            lock (loading) { if (loading.Contains(key)) return null; loading.Add(key); }
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap th = null;
                try
                {
                    Bitmap src = key.EndsWith(Project.Ext, StringComparison.OrdinalIgnoreCase) ? Project.ReadPreview(key) : SessionDoc.IsSessionFile(key) ? SessionDoc.ReadThumbnail(key, 400) : null;
                    if (src == null && !key.EndsWith(Project.Ext, StringComparison.OrdinalIgnoreCase) && !SessionDoc.IsSessionFile(key)) using (var fs = new FileStream(key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var im = Image.FromStream(fs, false, false)) src = new Bitmap(im);
                    if (src != null) { float k = Math.Min(1f, 320f / Math.Max(src.Width, src.Height)); th = Effects.Resize(src, Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k))); src.Dispose(); }
                }
                catch { }
                lock (cache) cache[key] = th; lock (loading) loading.Remove(key);
                try { BeginInvoke((Action)Invalidate); } catch { }
            });
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Paper);
            if (Items.Count == 0) { using (var f = Theme.Body(10f)) g.DrawString("Captures you take appear here.", f, new SolidBrush(Theme.Slate), Dpi.S(16), Dpi.S(50)); return; }
            int gap = Dpi.S(8), x0 = Dpi.S(8) - scroll;
            for (int i = 0; i < Items.Count; i++)
            {
                var r = new Rectangle(x0 + i * (CellW + gap), Dpi.S(8), CellW, Height - Dpi.S(16));
                if (r.Right < 0 || r.Left > Width) continue;
                var t = Items[i]; bool cur = IsCurrent(t);
                using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, r);
                var ar = new Rectangle(r.X + Dpi.S(4), r.Y + Dpi.S(4), r.Width - Dpi.S(8), r.Height - Dpi.S(8));
                Bitmap th = t.Doc != null && t.Path == null ? null : Thumb(t);
                Bitmap live = null;
                if (th == null && t.Doc != null) th = live = MiniOf(t.Doc);
                if (th != null)
                {
                    float k = Math.Min(ar.Width / (float)th.Width, ar.Height / (float)th.Height); int w = (int)(th.Width * k), h = (int)(th.Height * k);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(th, ar.X + (ar.Width - w) / 2, ar.Y + (ar.Height - h) / 2, w, h);
                }
                else if (t.Badge == "MP4" || t.Badge == "AVI")
                {
                    using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, ar);
                    Icons.Draw(g, Ic.Video, new RectangleF(ar.X + ar.Width / 2 - Dpi.S(18), ar.Y + ar.Height / 2 - Dpi.S(18), Dpi.S(36), Dpi.S(36)), Color.White);
                }
                if (live != null) live.Dispose();
                using (var pen = new Pen(cur ? Theme.Cobalt : i == hover ? Theme.Mist : Theme.Rule, cur ? Dpi.F(3f) : 1f)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                if (t.Badge != null)
                    using (var f = Theme.Label(8f))
                    {
                        var sz = g.MeasureString(t.Badge, f); var br = new RectangleF(r.X + Dpi.S(8), r.Bottom - sz.Height - Dpi.S(10), sz.Width + Dpi.S(8), sz.Height + Dpi.S(2));
                        using (var b = new SolidBrush(Color.FromArgb(220, Theme.Ink))) g.FillRectangle(b, br);
                        g.DrawString(t.Badge, f, Brushes.White, br.X + Dpi.S(4), br.Y + 1);
                    }
                if (i == hover && Delete != null)
                {
                    var dr = DeleteRect(i);
                    using (var path = Rounded(dr, Dpi.S(6)))
                    {
                        using (var sh = new SolidBrush(Color.FromArgb(40, Theme.Ink))) { g.TranslateTransform(0, Dpi.F(1.5f)); g.FillPath(sh, path); g.ResetTransform(); }
                        using (var b = new SolidBrush(overDelete ? Theme.Mark : Color.White)) g.FillPath(b, path);
                        if (!overDelete) using (var pen = new Pen(Theme.Rule)) g.DrawPath(pen, path);
                    }
                    int pad = Dpi.S(6);
                    Icons.Draw(g, Ic.Trash, new RectangleF(dr.X + pad, dr.Y + pad, dr.Width - pad * 2, dr.Height - pad * 2), overDelete ? Color.White : Theme.InkSoft);
                }
                else if (t.Doc != null && t.Doc.Dirty) using (var b = new SolidBrush(Theme.Mark)) g.FillEllipse(b, r.Right - Dpi.S(16), r.Y + Dpi.S(6), Dpi.S(9), Dpi.S(9));
            }
            if (Focused) using (var pen = new Pen(Theme.Cobalt) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        static Bitmap MiniOf(Doc d)
        {
            try { using (var f = d.Render()) { float k = Math.Min(1f, 320f / Math.Max(f.Width, f.Height)); return Effects.Resize(f, Math.Max(1, (int)(f.Width * k)), Math.Max(1, (int)(f.Height * k))); } } catch { return null; }
        }
        int At(Point p) { int gap = Dpi.S(8); int x = p.X + scroll - Dpi.S(8); if (x < 0) return -1; int i = x / (CellW + gap); return i < Items.Count && x % (CellW + gap) < CellW ? i : -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(e.Location); bool od = h >= 0 && Delete != null && DeleteRect(h).Contains(e.Location);
            if (h != hover || od != overDelete)
            {
                hover = h; overDelete = od; Invalidate();
                tip.SetToolTip(this, od ? (Items[h].Path == null ? "Discard" : "Delete") : null);
            }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; overDelete = false; tip.SetToolTip(this, null); Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus(); int i = At(e.Location); if (i < 0) return;
            if (e.Button == MouseButtons.Left && Delete != null && DeleteRect(i).Contains(e.Location)) { overDelete = false; Delete(Items[i]); return; }
            if (e.Button == MouseButtons.Right) { if (Context != null) Context(Items[i]); }
            else if (Open != null) Open(Items[i]);
        }
        protected override void OnMouseWheel(MouseEventArgs e) { scroll -= e.Delta; Clamp(); Invalidate(); var he = e as HandledMouseEventArgs; if (he != null) he.Handled = true; }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int i = Items.FindIndex(IsCurrent);
            if (e.KeyCode == Keys.Right && i < Items.Count - 1 && Open != null) Open(Items[i + 1]);
            else if (e.KeyCode == Keys.Left && i > 0 && Open != null) Open(Items[i - 1]);
            else if (e.KeyCode == Keys.Delete && i >= 0 && Delete != null) Delete(Items[i]);
        }
        protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Left || c == Keys.Right || base.IsInputKey(k); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Clamp(); Invalidate(); }
        protected override void Dispose(bool d) { if (d) tip.Dispose(); base.Dispose(d); }

        // dev check: draw as if the pointer were over card i (and over its delete button)
        internal void FakeHover(int i, bool onDelete) { hover = i; overDelete = onDelete; Invalidate(); }
    }
}
