// The editing surface: drawing tools, selection, handles, snapping, raster selections, crop and cut-out.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    enum Tool
    {
        Select, Arrow, Line, Shape, Callout, Text, Stamp, Step, Pen, Marker, Highlighter,
        Fill, Magnify, Spotlight, Blur, Crop, Cut, SelRect, SelEllipse, Lasso, Wand, SmartMove, Eyedropper
    }

    class ToolStyle
    {
        public Color Stroke = Theme.Mark, Fill = Color.Transparent, TextCol = Color.White;
        public float Width = 3, Opacity = 1f, Corner = 10, MagZoom = 2f, FontPx = 22;
        public bool Shadow, ArrowStart, ArrowEnd = true, MagRound = true, Bold = true, Italic, Underline, Contiguous = true;
        public Dash Dash = Dash.Solid;
        public ShapeKind Shape = ShapeKind.Rect, SpotKind = ShapeKind.Rounded;
        public Ic Stamp = Ic.Check;
        public StepStyle Step = StepStyle.Number;
        public string Font = Theme.BodyFamily;
        public StringAlignment Align = StringAlignment.Near;
        public BlurKind Blur = BlurKind.Pixelate;
        public int BlurStrength = 10, Tolerance = 24, SpotDim = 150;
        public event EventHandler Changed;
        public void Raise() { if (Changed != null) Changed(this, EventArgs.Empty); }
    }

    class DocCanvas : Control
    {
        public static readonly int Margin = Dpi.S(56);

        public readonly Doc Doc;
        public ToolStyle Style = new ToolStyle();
        public List<Obj> Sel = new List<Obj>();
        public Tool ActiveTool = Tool.Arrow;
        float zoom = 1f;

        public event EventHandler SelectionChanged, ZoomChanged, ViewChanged;
        public event Action<Color, bool> ColorPicked;
        public event Action<string> Message;
        public event Action<Obj> EditText;

        // interaction state
        Obj cur;                        // object being drawn
        bool dragging;
        PointF downImg, lastImg;
        int handle = -1;                // active handle
        bool moved;
        RectangleF origBounds; PointF origCenter; float origRot;
        PointF origA, origB; PointF origTip;
        List<PointF> origPts;
        RectangleF? marquee;
        RectangleF cropRect; bool hasCrop; int cropHandle = -1;
        RectangleF cutRect; bool cutting;
        List<PointF> lassoPts;
        GraphicsPath selPath; bool[] selMask; Rectangle selBounds;
        Bitmap selOverlay;
        float antsPhase;
        readonly Timer ants = new Timer { Interval = 120 };
        readonly List<float> guidesX = new List<float>(), guidesY = new List<float>();
        bool spaceDown; Point panStart; bool panning;

        public DocCanvas(Doc doc)
        {
            Doc = doc;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            doc.Changed += delegate { UpdateSize(); Invalidate(); if (ViewChanged != null) ViewChanged(this, EventArgs.Empty); };
            ants.Tick += delegate { antsPhase = (antsPhase + 1) % 8; if (HasSelection) Invalidate(); };
            ants.Start();
            UpdateSize();
        }
        protected override void Dispose(bool disposing) { if (disposing) { ants.Stop(); ants.Dispose(); } base.Dispose(disposing); }

        // ------------------------------------------------------------ geometry
        public float Zoom { get { return zoom; } }
        public void SetZoom(float z, PointF? anchorImg)
        {
            z = Math.Max(0.05f, Math.Min(16f, z));
            if (Math.Abs(z - zoom) < 0.0001f) return;
            zoom = z; UpdateSize(); Invalidate();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }
        public void SetZoom(float z) { SetZoom(z, null); }
        void UpdateSize()
        {
            Size = new Size((int)Math.Ceiling(Doc.Width * zoom) + Margin * 2, (int)Math.Ceiling(Doc.Height * zoom) + Margin * 2);
        }
        PointF ToImg(Point p) { return new PointF((p.X - Margin) / zoom, (p.Y - Margin) / zoom); }
        PointF ToScr(PointF p) { return new PointF(Margin + p.X * zoom, Margin + p.Y * zoom); }
        Rectangle ImageRectScr { get { return new Rectangle(Margin, Margin, (int)Math.Ceiling(Doc.Width * zoom), (int)Math.Ceiling(Doc.Height * zoom)); } }

        public bool HasSelection { get { return selMask != null && selBounds.Width > 0; } }
        public bool HasCrop { get { return hasCrop; } }
        public Rectangle SelectionBounds { get { return selBounds; } }

        // ------------------------------------------------------------ tools
        public void SetTool(Tool t)
        {
            CommitOrCancelPending();
            ActiveTool = t;
            if (t != Tool.Select) { Sel.Clear(); RaiseSel(); }
            Cursor = t == Tool.Select ? Cursors.Default : t == Tool.Eyedropper ? Theme.Hand : Cursors.Cross;
            Invalidate();
        }
        void CommitOrCancelPending() { hasCrop = false; cutting = false; cur = null; dragging = false; }
        void RaiseSel() { if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }
        void Say(string s) { if (Message != null) Message(s); }

        public void ClearRasterSelection()
        {
            selPath = null; selMask = null; selBounds = Rectangle.Empty;
            if (selOverlay != null) { selOverlay.Dispose(); selOverlay = null; }
            Invalidate();
        }

        // ------------------------------------------------------------ painting
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Paper);
            var ir = ImageRectScr;
            Theme.CropMarks(g, ir, 6, 14, Theme.Mist);
            // transparency checkerboard + image + objects, drawn at zoom
            g.SetClip(ir);
            Theme.Checker(g, ir, 10);
            g.ResetClip();
            var st = g.Save();
            g.TranslateTransform(Margin, Margin); g.ScaleTransform(zoom, zoom);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = zoom < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.DrawImage(Doc.Base, 0, 0, Doc.Width, Doc.Height);
            foreach (var o in Doc.Objs) o.Render(g, Doc.Base);
            if (cur != null) cur.Render(g, Doc.Base);
            g.Restore(st);

            DrawRasterSelection(g);
            DrawSelectionChrome(g);
            DrawPendingTools(g);
            DrawGuides(g);
            if (marquee.HasValue)
            {
                var m = marquee.Value; var r = Rectangle.Round(new RectangleF(ToScr(m.Location), new SizeF(m.Width * zoom, m.Height * zoom)));
                using (var b = new SolidBrush(Color.FromArgb(40, Theme.Cobalt))) g.FillRectangle(b, r);
                using (var p = new Pen(Theme.Cobalt)) g.DrawRectangle(p, r);
            }
        }

        void DrawRasterSelection(Graphics g)
        {
            if (!HasSelection) return;
            if (selPath != null)
            {
                var st = g.Save(); g.TranslateTransform(Margin, Margin); g.ScaleTransform(zoom, zoom);
                using (var p = new Pen(Color.White, 1.5f / zoom)) g.DrawPath(p, selPath);
                using (var p = new Pen(Theme.Ink, 1.5f / zoom) { DashStyle = DashStyle.Custom, DashPattern = new[] { 4f, 4f }, DashOffset = antsPhase })
                    g.DrawPath(p, selPath);
                g.Restore(st);
            }
            else if (selOverlay != null)
            {
                var r = new Rectangle((int)(Margin + selBounds.X * zoom), (int)(Margin + selBounds.Y * zoom), (int)(selBounds.Width * zoom), (int)(selBounds.Height * zoom));
                g.DrawImage(selOverlay, r);
                using (var p = new Pen(Theme.Ink, 1f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r);
            }
        }

        public static RectangleF[] HandleRects(RectangleF r, float size)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, h = size / 2;
            var pts = new[] { new PointF(r.Left, r.Top), new PointF(cx, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, cy),
                              new PointF(r.Right, r.Bottom), new PointF(cx, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, cy) };
            return pts.Select(p => new RectangleF(p.X - h, p.Y - h, size, size)).ToArray();
        }

        void DrawSelectionChrome(Graphics g)
        {
            foreach (var o in Sel)
            {
                var st = g.Save();
                g.TranslateTransform(Margin, Margin); g.ScaleTransform(zoom, zoom);
                if (o.Rotation != 0 && o.Rotatable)
                { var c = o.Center(); g.TranslateTransform(c.X, c.Y); g.RotateTransform(o.Rotation); g.TranslateTransform(-c.X, -c.Y); }
                float hs = Dpi.F(8f) / zoom;
                var r = o.Bounds();
                using (var p = new Pen(Theme.Cobalt, 1f / zoom) { DashStyle = DashStyle.Dash })
                {
                    if (o.IsLinear) { g.DrawLine(p, o.A, o.B); }
                    else g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                }
                if (Sel.Count == 1 && !o.Locked)
                {
                    using (var fb = new SolidBrush(Color.White)) using (var pn = new Pen(Theme.Cobalt, 1.4f / zoom))
                    {
                        if (o.IsLinear)
                        {
                            foreach (var pt in new[] { o.A, o.B }) { var hr = new RectangleF(pt.X - hs / 2, pt.Y - hs / 2, hs, hs); g.FillEllipse(fb, hr); g.DrawEllipse(pn, hr); }
                        }
                        else
                        {
                            foreach (var hr in HandleRects(r, hs)) { g.FillRectangle(fb, hr); g.DrawRectangle(pn, hr.X, hr.Y, hr.Width, hr.Height); }
                            if (o.Rotatable)
                            {
                                var top = new PointF(r.X + r.Width / 2, r.Top); var rp = new PointF(top.X, top.Y - Dpi.F(24f) / zoom);
                                g.DrawLine(pn, top, rp);
                                var hr = new RectangleF(rp.X - hs / 2, rp.Y - hs / 2, hs, hs); g.FillEllipse(fb, hr); g.DrawEllipse(pn, hr);
                            }
                            var co = o as CalloutObj;
                            if (co != null)
                            {
                                var tip = co.TipAbsolute(); var hr = new RectangleF(tip.X - hs / 2, tip.Y - hs / 2, hs, hs);
                                using (var tb = new SolidBrush(Theme.Mark)) g.FillEllipse(tb, hr);
                                g.DrawEllipse(pn, hr);
                            }
                        }
                    }
                }
                g.Restore(st);
            }
        }

        void DrawPendingTools(Graphics g)
        {
            if (hasCrop)
            {
                var full = ImageRectScr; var r = Rectangle.Round(new RectangleF(ToScr(cropRect.Location), new SizeF(cropRect.Width * zoom, cropRect.Height * zoom)));
                using (var b = new SolidBrush(Color.FromArgb(130, 10, 18, 28)))
                {
                    g.FillRectangle(b, full.Left, full.Top, full.Width, Math.Max(0, r.Top - full.Top));
                    g.FillRectangle(b, full.Left, r.Bottom, full.Width, Math.Max(0, full.Bottom - r.Bottom));
                    g.FillRectangle(b, full.Left, r.Top, Math.Max(0, r.Left - full.Left), r.Height);
                    g.FillRectangle(b, r.Right, r.Top, Math.Max(0, full.Right - r.Right), r.Height);
                }
                // rule-of-thirds guides
                using (var p = new Pen(Color.FromArgb(120, 255, 255, 255)))
                {
                    for (int i = 1; i < 3; i++) { g.DrawLine(p, r.Left + r.Width * i / 3, r.Top, r.Left + r.Width * i / 3, r.Bottom); g.DrawLine(p, r.Left, r.Top + r.Height * i / 3, r.Right, r.Top + r.Height * i / 3); }
                }
                using (var p = new Pen(Color.White, 1.5f)) g.DrawRectangle(p, r);
                using (var fb = new SolidBrush(Color.White)) using (var pn = new Pen(Theme.Ink))
                    foreach (var hr in HandleRects(r, Dpi.S(9))) { g.FillRectangle(fb, hr); g.DrawRectangle(pn, hr.X, hr.Y, hr.Width, hr.Height); }
                string label = (int)cropRect.Width + " x " + (int)cropRect.Height + "   Enter to crop, Esc to cancel";
                using (var f = Theme.Label(9f))
                {
                    var sz = g.MeasureString(label, f);
                    var lr = new RectangleF(r.Left, Math.Max(2, r.Top - sz.Height - 6), sz.Width + 12, sz.Height + 2);
                    using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, lr);
                    g.DrawString(label, f, Brushes.White, lr.X + 6, lr.Y + 1);
                }
            }
            if (cutting)
            {
                var r = Rectangle.Round(new RectangleF(ToScr(cutRect.Location), new SizeF(cutRect.Width * zoom, cutRect.Height * zoom)));
                using (var b = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(180, Theme.Mark), Color.FromArgb(40, Theme.Mark))) g.FillRectangle(b, r);
                using (var p = new Pen(Theme.Mark)) g.DrawRectangle(p, r);
            }
            if (lassoPts != null && lassoPts.Count > 1 && ActiveTool == Tool.Lasso)
                using (var p = new Pen(Theme.Cobalt, 1.5f)) g.DrawLines(p, lassoPts.Select(ToScr).ToArray());
        }

        void DrawGuides(Graphics g)
        {
            using (var p = new Pen(Color.FromArgb(220, 0xE8, 0x2C, 0xA8), 1f) { DashStyle = DashStyle.Dash })
            {
                foreach (var x in guidesX) { var s = ToScr(new PointF(x, 0)); g.DrawLine(p, s.X, Margin - 8, s.X, Margin + Doc.Height * zoom + 8); }
                foreach (var y in guidesY) { var s = ToScr(new PointF(0, y)); g.DrawLine(p, Margin - 8, s.Y, Margin + Doc.Width * zoom + 8, s.Y); }
            }
        }

        // ------------------------------------------------------------ hit testing
        Obj TopHit(PointF p)
        {
            for (int i = Doc.Objs.Count - 1; i >= 0; i--) { var o = Doc.Objs[i]; if (!o.Locked && o.HitTest(p)) return o; }
            return null;
        }
        // returns handle index (0-7 resize, 8 rotate, 9 callout tip, 10/11 line ends) or -1
        int HandleAt(Obj o, PointF img)
        {
            float tol = Dpi.F(7f) / zoom, hs = Dpi.F(8f) / zoom;
            if (o.IsLinear)
            {
                if (Dist(img, o.A) <= tol + 2) return 10;
                if (Dist(img, o.B) <= tol + 2) return 11;
                return -1;
            }
            var co = o as CalloutObj;
            if (co != null && Dist(img, o.ToLocal(co.TipAbsolute())) <= tol + 2) return 9;
            var lp = o.ToLocal(img);
            var r = o.Bounds();
            if (o.Rotatable)
            {
                var rp = new PointF(r.X + r.Width / 2, r.Top - Dpi.F(24f) / zoom);
                if (Dist(lp, rp) <= tol + 2) return 8;
            }
            var hrs = HandleRects(r, hs + Dpi.F(4f) / zoom);
            for (int i = 0; i < 8; i++) if (hrs[i].Contains(lp)) return i;
            return -1;
        }
        static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        // ------------------------------------------------------------ mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && spaceDown)) { panning = true; panStart = e.Location; Cursor = Cursors.SizeAll; return; }
            if (e.Button != MouseButtons.Left) return;
            var p = ToImg(e.Location);
            downImg = lastImg = p; dragging = true; moved = false; guidesX.Clear(); guidesY.Clear();

            // with a drawing tool, pressing on something already drawn picks it up to move, resize or delete it (hold Alt to draw on top of it instead)
            if (IsDrawingTool(ActiveTool) && (ModifierKeys & Keys.Alt) == 0)
            {
                bool onHandle = Sel.Count == 1 && !Sel[0].Locked && HandleAt(Sel[0], p) >= 0;
                if (onHandle || TopHit(p) != null)
                {
                    var keep = Sel.ToList(); SetTool(Tool.Select); Sel = onHandle ? keep : Sel; dragging = true; downImg = lastImg = p; SelectDown(p, e); if (PickedUp != null) PickedUp(); return;
                }
            }
            switch (ActiveTool)
            {
                case Tool.Select: SelectDown(p, e); return;
                case Tool.Eyedropper:
                    if (p.X >= 0 && p.Y >= 0 && p.X < Doc.Width && p.Y < Doc.Height)
                    {
                        using (var flat = Doc.Render()) { var c = flat.GetPixel((int)p.X, (int)p.Y); if (ColorPicked != null) ColorPicked(Color.FromArgb(255, c), (ModifierKeys & Keys.Alt) != 0); }
                    }
                    dragging = false; return;
                case Tool.Text:
                    {
                        dragging = false;
                        var t = new TextObj { A = p, Stroke = Style.Stroke, Fill = Style.Fill, FontName = Style.Font, Px = Style.FontPx, Bold = Style.Bold, Italic = Style.Italic, Underline = Style.Underline, Align = Style.Align, Opacity = Style.Opacity, Shadow = Style.Shadow, Text = "" };
                        var s = Dialogs.Prompt(FindForm(), "Add text", "");
                        if (!string.IsNullOrEmpty(s)) { t.Text = s; t.Measure(); Doc.Add(t); Sel = new List<Obj> { t }; RaiseSel(); SetTool(Tool.Select); Sel = new List<Obj> { t }; RaiseSel(); }
                        return;
                    }
                case Tool.Stamp:
                    {
                        dragging = false; float sz = 40;
                        var st = new StampObj { Glyph = Style.Stamp, Stroke = Style.Stroke, Opacity = Style.Opacity, Shadow = Style.Shadow, A = new PointF(p.X - sz / 2, p.Y - sz / 2), B = new PointF(p.X + sz / 2, p.Y + sz / 2) };
                        Doc.Add(st); return;
                    }
                case Tool.Step:
                    {
                        dragging = false; float sz = 18 + Style.Width * 4;
                        var st = new StepObj { Number = Doc.NextStepNumber(), Style = Style.Step, Fill = Style.Fill.A > 0 ? Style.Fill : Style.Stroke, Opacity = Style.Opacity, Shadow = Style.Shadow, A = new PointF(p.X - sz / 2, p.Y - sz / 2), B = new PointF(p.X + sz / 2, p.Y + sz / 2) };
                        Doc.Add(st); return;
                    }
                case Tool.Fill:
                    {
                        dragging = false;
                        if (p.X < 0 || p.Y < 0 || p.X >= Doc.Width || p.Y >= Doc.Height) return;
                        Color fc = Style.Fill.A > 0 ? Style.Fill : Style.Stroke;
                        Rectangle ch; var pt = new Point((int)p.X, (int)p.Y);
                        Doc.ApplyPixels(b => Effects.FloodFill(b, pt, fc, Style.Tolerance, out ch));
                        return;
                    }
                case Tool.Wand:
                    {
                        dragging = false;
                        if (p.X < 0 || p.Y < 0 || p.X >= Doc.Width || p.Y >= Doc.Height) return;
                        using (var flat = Doc.Render()) SetMask(Effects.WandMask(flat, new Point((int)p.X, (int)p.Y), Style.Tolerance, Style.Contiguous), null);
                        return;
                    }
                case Tool.Crop:
                    if (hasCrop) { cropHandle = CropHandleAt(e.Location); if (cropHandle >= 0) return; if (!cropRect.Contains(p)) { hasCrop = false; } else { cropHandle = 99; return; } }
                    hasCrop = false; cropHandle = -1; cropRect = new RectangleF(Clamp(p), SizeF.Empty); cropHandle = 5; downImg = cropRect.Location; hasCrop = true; return;
                case Tool.Cut: cutting = true; cutRect = new RectangleF(p, SizeF.Empty); return;
                case Tool.SelRect: case Tool.SelEllipse: case Tool.SmartMove: ClearRasterSelection(); selPath = new GraphicsPath(); selBounds = Rectangle.Empty; return;
                case Tool.Lasso: ClearRasterSelection(); lassoPts = new List<PointF> { p }; return;
                case Tool.Pen: case Tool.Marker: case Tool.Highlighter:
                    {
                        var pn = new PenObj { Mode = ActiveTool == Tool.Pen ? PenMode.Pen : ActiveTool == Tool.Marker ? PenMode.Marker : PenMode.Highlighter, Stroke = ActiveTool == Tool.Highlighter && Style.Stroke == Theme.Mark ? Color.FromArgb(255, 214, 0) : Style.Stroke, Width = Style.Width, Opacity = Style.Opacity, Shadow = Style.Shadow };
                        pn.Pts.Add(p); cur = pn; return;
                    }
                default: cur = MakeShapeTool(p); return;
            }
        }

        Obj MakeShapeTool(PointF p)
        {
            Obj o;
            switch (ActiveTool)
            {
                case Tool.Arrow: o = new LineObj { ArrowStart = Style.ArrowStart, ArrowEnd = Style.ArrowEnd }; break;
                case Tool.Line: o = new LineObj { ArrowStart = false, ArrowEnd = false }; break;
                case Tool.Shape: o = new ShapeObj { Kind = Style.Shape, Corner = Style.Corner }; break;
                case Tool.Callout: o = new CalloutObj { Text = "Text", Px = Style.FontPx, TextCol = Style.TextCol, Kind = Style.Shape == ShapeKind.Ellipse ? ShapeKind.Ellipse : Style.Shape == ShapeKind.Rect ? ShapeKind.Rect : ShapeKind.Rounded }; break;
                case Tool.Magnify: o = new MagnifyObj { Zoom = Style.MagZoom, Round = Style.MagRound }; break;
                case Tool.Spotlight: o = new SpotlightObj { Kind = Style.SpotKind, Dim = Style.SpotDim }; break;
                case Tool.Blur: o = new BlurObj { Kind = Style.Blur, Strength = Style.BlurStrength }; break;
                default: return null;
            }
            o.A = p; o.B = p; o.Opacity = Style.Opacity; o.Shadow = Style.Shadow;
            if (!(o is BlurObj) && !(o is SpotlightObj) && !(o is MagnifyObj)) { o.Stroke = Style.Stroke; o.Width = Style.Width; o.Dash = Style.Dash; }
            if (o is MagnifyObj) { o.Stroke = Style.Stroke; o.Width = Math.Max(2, Style.Width); }
            if (o is ShapeObj || o is CalloutObj) o.Fill = o is CalloutObj ? (Style.Fill.A > 0 ? Style.Fill : Style.Stroke) : Style.Fill;
            return o;
        }

        PointF Clamp(PointF p) { return new PointF(Math.Max(0, Math.Min(Doc.Width, p.X)), Math.Max(0, Math.Min(Doc.Height, p.Y))); }

        public event Action PickedUp;      // a drawing tool handed over to the selection tool because an existing object was clicked
        static bool IsDrawingTool(Tool t) { return t == Tool.Arrow || t == Tool.Line || t == Tool.Shape || t == Tool.Callout || t == Tool.Magnify || t == Tool.Spotlight || t == Tool.Blur || t == Tool.Text || t == Tool.Stamp || t == Tool.Step; }
        void SelectDown(PointF p, MouseEventArgs e)
        {
            // handles of the single selected object first
            if (Sel.Count == 1 && !Sel[0].Locked)
            {
                int h = HandleAt(Sel[0], p);
                if (h >= 0) { BeginHandleDrag(Sel[0], h, p); return; }
            }
            var hit = TopHit(p);
            bool add = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            if (hit == null) { if (!add) { Sel.Clear(); RaiseSel(); } marquee = new RectangleF(p, SizeF.Empty); handle = -2; Invalidate(); return; }
            if (add) { if (Sel.Contains(hit)) Sel.Remove(hit); else Sel.Add(hit); }
            else if (!Sel.Contains(hit)) Sel = new List<Obj> { hit };
            RaiseSel();
            handle = -1; Doc.Checkpoint(); moved = false;
            Invalidate();
        }
        void BeginHandleDrag(Obj o, int h, PointF p)
        {
            handle = h; Doc.Checkpoint(); moved = false;
            origBounds = o.Bounds(); origCenter = o.Center(); origRot = o.Rotation; origA = o.A; origB = o.B;
            var co = o as CalloutObj; if (co != null) origTip = co.Tail;
            var pen = o as PenObj; origPts = pen != null ? new List<PointF>(pen.Pts) : null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (panning)
            {
                var sc = Parent as ScrollableControl;
                if (sc != null) sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X - (e.X - panStart.X), -sc.AutoScrollPosition.Y - (e.Y - panStart.Y));
                return;
            }
            var p = ToImg(e.Location);
            if (!dragging)
            {
                UpdateHoverCursor(p, e.Location);
                return;
            }
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            if (cur != null) { UpdateDraw(p, shift); }
            else if (ActiveTool == Tool.Select) SelectDrag(p, shift);
            else if (ActiveTool == Tool.Crop && hasCrop) CropDrag(p, e.Location);
            else if (cutting) { cutRect = Math.Abs(p.X - downImg.X) >= Math.Abs(p.Y - downImg.Y) ? RectangleF.FromLTRB(Math.Min(downImg.X, p.X), 0, Math.Max(downImg.X, p.X), Doc.Height) : RectangleF.FromLTRB(0, Math.Min(downImg.Y, p.Y), Doc.Width, Math.Max(downImg.Y, p.Y)); Invalidate(); }
            else if (ActiveTool == Tool.SelRect || ActiveTool == Tool.SelEllipse || ActiveTool == Tool.SmartMove)
            {
                var r = Obj.Norm(Clamp(downImg), Clamp(p));
                if (shift) { float m = Math.Max(r.Width, r.Height); r = new RectangleF(r.X, r.Y, m, m); }
                selPath = new GraphicsPath();
                if (ActiveTool == Tool.SelEllipse) selPath.AddEllipse(r); else selPath.AddRectangle(r);
                selBounds = Rectangle.Round(r); selMask = null; Invalidate();
            }
            else if (ActiveTool == Tool.Lasso && lassoPts != null) { lassoPts.Add(Clamp(p)); Invalidate(); }
            lastImg = p;
        }

        void UpdateHoverCursor(PointF p, Point scr)
        {
            if (spaceDown) { Cursor = Theme.Hand; return; }
            if (ActiveTool == Tool.Select)
            {
                Cursor = Cursors.Default;
                if (Sel.Count == 1 && !Sel[0].Locked)
                {
                    int h = HandleAt(Sel[0], p);
                    if (h == 8) Cursor = Cursors.Cross; else if (h >= 9) Cursor = Theme.Hand;
                    else if (h >= 0) { int[] m = { 0, 1, 2, 3, 2, 1, 0, 3 }; Cursor = (m[h] == 0) ? Cursors.SizeNWSE : (m[h] == 2) ? Cursors.SizeNESW : (m[h] == 1) ? Cursors.SizeNS : Cursors.SizeWE; if (h == 2 || h == 6) Cursor = Cursors.SizeNESW; if (h == 0 || h == 4) Cursor = Cursors.SizeNWSE; }
                    else if (Sel[0].HitTest(p)) Cursor = Cursors.SizeAll;
                }
                else if (TopHit(p) != null) Cursor = Cursors.SizeAll;
            }
            else if (ActiveTool == Tool.Crop && hasCrop)
            {
                int h = CropHandleAt(scr);
                Cursor = h < 0 ? (cropRect.Contains(p) ? Cursors.SizeAll : Cursors.Cross) : (h == 1 || h == 5) ? Cursors.SizeNS : (h == 3 || h == 7) ? Cursors.SizeWE : (h == 0 || h == 4) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            }
        }

        void UpdateDraw(PointF p, bool shift)
        {
            var pen = cur as PenObj;
            if (pen != null)
            {
                var last = pen.Pts[pen.Pts.Count - 1];
                if (shift && pen.Pts.Count >= 1) { var first = pen.Pts[0]; if (pen.Pts.Count > 1) pen.Pts.RemoveRange(1, pen.Pts.Count - 1); pen.Pts.Add(Math.Abs(p.X - first.X) > Math.Abs(p.Y - first.Y) ? new PointF(p.X, first.Y) : new PointF(first.X, p.Y)); }
                else if (Dist(last, p) > 1.2f / zoom) pen.Pts.Add(p);
                Invalidate(); return;
            }
            var line = cur as LineObj;
            if (line != null && shift)
            {
                float dx = p.X - cur.A.X, dy = p.Y - cur.A.Y; double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4), len = Math.Sqrt(dx * dx + dy * dy);
                p = new PointF(cur.A.X + (float)(Math.Cos(ang) * len), cur.A.Y + (float)(Math.Sin(ang) * len));
            }
            else if (shift && !(cur is LineObj))
            {
                float dx = p.X - cur.A.X, dy = p.Y - cur.A.Y, m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                p = new PointF(cur.A.X + Math.Sign(dx) * m, cur.A.Y + Math.Sign(dy) * m);
            }
            cur.B = p; Invalidate();
        }

        void SelectDrag(PointF p, bool shift)
        {
            if (handle == -2 && marquee.HasValue) { marquee = Obj.Norm(downImg, p); Invalidate(); return; }
            float dx = p.X - lastImg.X, dy = p.Y - lastImg.Y;
            if (handle >= 0 && Sel.Count == 1) { HandleDrag(Sel[0], p, shift); moved = true; Doc.Raise(); return; }
            if (Sel.Count > 0 && handle == -1 && (Math.Abs(p.X - downImg.X) > 1 || Math.Abs(p.Y - downImg.Y) > 1 || moved))
            {
                moved = true;
                foreach (var o in Sel) if (!o.Locked) o.Translate(dx, dy);
                if (Sel.Count == 1) Snap(Sel[0]);
                Doc.Raise();
            }
            lastImg = p;
        }

        // pull a moving object to image edges/centres and to other objects' edges
        void Snap(Obj o)
        {
            guidesX.Clear(); guidesY.Clear();
            float tol = Dpi.F(6f) / zoom; var r = o.Bounds();
            var xs = new List<float> { 0, Doc.Width / 2f, Doc.Width }; var ys = new List<float> { 0, Doc.Height / 2f, Doc.Height };
            foreach (var other in Doc.Objs) { if (other == o) continue; var b = other.Bounds(); xs.Add(b.Left); xs.Add(b.Right); xs.Add(b.X + b.Width / 2); ys.Add(b.Top); ys.Add(b.Bottom); ys.Add(b.Y + b.Height / 2); }
            float bestX = tol + 1, bestY = tol + 1, sx = 0, sy = 0, gx = 0, gy = 0;
            foreach (var mine in new[] { r.Left, r.Right, r.X + r.Width / 2 })
                foreach (var t in xs) { float d = t - mine; if (Math.Abs(d) < bestX) { bestX = Math.Abs(d); sx = d; gx = t; } }
            foreach (var mine in new[] { r.Top, r.Bottom, r.Y + r.Height / 2 })
                foreach (var t in ys) { float d = t - mine; if (Math.Abs(d) < bestY) { bestY = Math.Abs(d); sy = d; gy = t; } }
            if (bestX <= tol) { o.Translate(sx, 0); guidesX.Add(gx); }
            if (bestY <= tol) { o.Translate(0, sy); guidesY.Add(gy); }
        }

        void HandleDrag(Obj o, PointF p, bool shift)
        {
            if (handle == 10 || handle == 11)
            {
                if (shift) { var anchor = handle == 10 ? o.B : o.A; float dx = p.X - anchor.X, dy = p.Y - anchor.Y; double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4), len = Math.Sqrt(dx * dx + dy * dy); p = new PointF(anchor.X + (float)(Math.Cos(ang) * len), anchor.Y + (float)(Math.Sin(ang) * len)); }
                if (handle == 10) o.A = p; else o.B = p; return;
            }
            var co = o as CalloutObj;
            if (handle == 9 && co != null) { co.SetTipAbsolute(o.ToLocal(p)); return; }
            if (handle == 8)
            {
                var c = origCenter; double a = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI + 90;
                if (shift) a = Math.Round(a / 15) * 15;
                o.Rotation = (float)((a % 360 + 360) % 360); return;
            }
            // resize in the object's own (unrotated) space, then re-pin the opposite corner in world space
            var m = Rotate(p, origCenter, -origRot);
            var r = origBounds;
            float l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
            if (handle == 0 || handle == 6 || handle == 7) l = m.X;
            if (handle == 2 || handle == 3 || handle == 4) rr = m.X;
            if (handle == 0 || handle == 1 || handle == 2) t = m.Y;
            if (handle == 4 || handle == 5 || handle == 6) b = m.Y;
            if (shift && handle % 2 == 0 && r.Height > 0)
            {
                float ratio = r.Width / r.Height, w2 = Math.Abs(rr - l), h2 = Math.Abs(b - t);
                if (w2 / ratio > h2) { float nh = w2 / ratio; if (handle == 0 || handle == 2) t = b - Math.Sign(b - t == 0 ? 1 : b - t) * nh; else b = t + Math.Sign(b - t == 0 ? 1 : b - t) * nh; }
                else { float nw = h2 * ratio; if (handle == 0 || handle == 6) l = rr - Math.Sign(rr - l == 0 ? 1 : rr - l) * nw; else rr = l + Math.Sign(rr - l == 0 ? 1 : rr - l) * nw; }
            }
            var nr = RectangleF.FromLTRB(Math.Min(l, rr), Math.Min(t, b), Math.Max(l, rr), Math.Max(t, b));
            if (nr.Width < 4) nr.Width = 4; if (nr.Height < 4) nr.Height = 4;
            if (o.UsesPoints && origPts != null) { var pen = (PenObj)o; pen.Pts = new List<PointF>(origPts); }
            if (origRot != 0 && o.Rotatable)
            {
                // opposite corner in world space before and after
                PointF oppOld = OppositeCorner(origBounds, handle), oppNew = OppositeCorner(nr, handle);
                var worldOld = Rotate(oppOld, origCenter, origRot);
                var cNew = new PointF(nr.X + nr.Width / 2, nr.Y + nr.Height / 2);
                var worldNew = Rotate(oppNew, cNew, origRot);
                nr.Offset(worldOld.X - worldNew.X, worldOld.Y - worldNew.Y);
            }
            o.SetBounds(nr);
        }
        static PointF OppositeCorner(RectangleF r, int h)
        {
            float x = (h == 0 || h == 6 || h == 7) ? r.Right : (h == 2 || h == 3 || h == 4) ? r.Left : r.X + r.Width / 2;
            float y = (h == 0 || h == 1 || h == 2) ? r.Bottom : (h == 4 || h == 5 || h == 6) ? r.Top : r.Y + r.Height / 2;
            return new PointF(x, y);
        }
        static PointF Rotate(PointF p, PointF c, float deg)
        {
            double a = deg * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a); float dx = p.X - c.X, dy = p.Y - c.Y;
            return new PointF(c.X + (float)(dx * cs - dy * sn), c.Y + (float)(dx * sn + dy * cs));
        }

        // ---- crop rectangle editing
        int CropHandleAt(Point scr)
        {
            var r = new RectangleF(ToScr(cropRect.Location), new SizeF(cropRect.Width * zoom, cropRect.Height * zoom));
            var hs = HandleRects(r, Dpi.S(14));
            for (int i = 0; i < 8; i++) if (hs[i].Contains(scr)) return i;
            return -1;
        }
        void CropDrag(PointF p, Point scr)
        {
            p = Clamp(p);
            if (cropHandle == 99) { float dx = p.X - lastImg.X, dy = p.Y - lastImg.Y; cropRect.Offset(dx, dy); if (cropRect.Left < 0) cropRect.X = 0; if (cropRect.Top < 0) cropRect.Y = 0; if (cropRect.Right > Doc.Width) cropRect.X = Doc.Width - cropRect.Width; if (cropRect.Bottom > Doc.Height) cropRect.Y = Doc.Height - cropRect.Height; }
            else
            {
                float l = cropRect.Left, t = cropRect.Top, r = cropRect.Right, b = cropRect.Bottom;
                int h = cropHandle;
                if (h == 5 && downImg == cropRect.Location) { r = p.X; b = p.Y; l = downImg.X; t = downImg.Y; }
                else
                {
                    if (h == 0 || h == 6 || h == 7) l = p.X; if (h == 2 || h == 3 || h == 4) r = p.X;
                    if (h == 0 || h == 1 || h == 2) t = p.Y; if (h == 4 || h == 5 || h == 6) b = p.Y;
                }
                cropRect = RectangleF.FromLTRB(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b));
            }
            lastImg = p; Invalidate();
        }
        public void ApplyCrop()
        {
            if (!hasCrop) return;
            var r = Rectangle.Round(cropRect); hasCrop = false;
            if (r.Width > 3 && r.Height > 3) Doc.Crop(r);
            Invalidate();
        }
        public void CancelCrop() { hasCrop = false; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (panning) { panning = false; Cursor = ActiveTool == Tool.Select ? Cursors.Default : Cursors.Cross; return; }
            if (!dragging) return;
            dragging = false; guidesX.Clear(); guidesY.Clear();
            var p = ToImg(e.Location);

            if (cur != null)
            {
                var o = cur; cur = null;
                var pen = o as PenObj;
                bool ok = pen != null ? true : (Math.Abs(o.A.X - o.B.X) > 3 || Math.Abs(o.A.Y - o.B.Y) > 3);
                if (!ok && (o is ShapeObj || o is CalloutObj || o is MagnifyObj || o is BlurObj || o is SpotlightObj) && (ModifierKeys & Keys.Alt) == 0)
                {   // a plain click drops a default-sized object
                    float s = o is CalloutObj ? 140 : 90; o.B = new PointF(o.A.X + s, o.A.Y + (o is CalloutObj ? 60 : s * 0.6f)); ok = true;
                }
                if (ok)
                {
                    if (o is LineObj) { var l = (LineObj)o; }
                    Doc.Add(o);
                    var co = o as CalloutObj;
                    if (co != null)
                    {
                        var s = Dialogs.Prompt(FindForm(), "Callout text", co.Text);
                        if (s != null) co.Text = s;
                        co.Tail = new PointF(-co.Bounds().Width * 0.25f, co.Bounds().Height * 0.5f + 34);
                        Doc.Raise();
                    }
                    if (o is PenObj == false) { Sel = new List<Obj> { o }; }
                    else Sel.Clear();
                    RaiseSel();
                    if (!(o is PenObj)) { var keep = ActiveTool; /* stay on the drawing tool; handles appear after switching */ }
                }
                Invalidate(); return;
            }
            if (ActiveTool == Tool.Select)
            {
                if (handle == -2 && marquee.HasValue)
                {
                    var m = marquee.Value; marquee = null;
                    Sel = Doc.Objs.Where(o => !o.Locked && m.IntersectsWith(o.Bounds())).ToList(); RaiseSel(); Invalidate();
                }
                else if (!moved && handle == -1)
                {
                    /* click without drag: a checkpoint was pushed for nothing, drop it */
                    DropEmptyCheckpoint();
                }
                else if (moved) Doc.Raise();
                handle = -1; return;
            }
            if (cutting)
            {
                cutting = false;
                bool horiz = cutRect.Width >= Doc.Width - 1;
                int start = horiz ? (int)cutRect.Top : (int)cutRect.Left, len = horiz ? (int)cutRect.Height : (int)cutRect.Width;
                if (len > 2) { Doc.CutBand(horiz, start, len); Say("Cut out " + len + " px"); }
                Invalidate(); return;
            }
            if (ActiveTool == Tool.Crop) { cropHandle = -1; if (hasCrop && (cropRect.Width < 4 || cropRect.Height < 4)) hasCrop = false; Invalidate(); return; }
            if (ActiveTool == Tool.SelRect || ActiveTool == Tool.SelEllipse || ActiveTool == Tool.SmartMove)
            {
                if (selBounds.Width < 3 || selBounds.Height < 3) { ClearRasterSelection(); return; }
                var path = selPath; SetMask(MaskFromPath(path, Doc.Width, Doc.Height), path);
                if (ActiveTool == Tool.SmartMove) { LiftSelection(true); SetTool(Tool.Select); }
                return;
            }
            if (ActiveTool == Tool.Lasso && lassoPts != null)
            {
                if (lassoPts.Count > 3) { var gp = new GraphicsPath(); gp.AddPolygon(lassoPts.ToArray()); SetMask(MaskFromPath(gp, Doc.Width, Doc.Height), gp); }
                lassoPts = null; Invalidate();
            }
        }
        void DropEmptyCheckpoint()
        {
            // history is private to Doc: an unmoved click costs one undo step, which is harmless but noisy
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var p = ToImg(e.Location);
            if (ActiveTool == Tool.Crop && hasCrop && cropRect.Contains(p)) { ApplyCrop(); return; }
            if (ActiveTool == Tool.Select)
            {
                var hit = TopHit(p);
                if (hit != null && EditText != null && (hit is TextObj || hit is CalloutObj)) EditText(hit);
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                SetZoom(zoom * (float)Math.Pow(1.12, e.Delta / 120.0));
                var he = e as HandledMouseEventArgs; if (he != null) he.Handled = true;
                return;
            }
            base.OnMouseWheel(e);
        }

        // ------------------------------------------------------------ keyboard
        protected override bool IsInputKey(Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { spaceDown = true; Cursor = Theme.Hand; e.Handled = true; return; }
            if (e.KeyCode == Keys.Enter && hasCrop) { ApplyCrop(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Escape) { hasCrop = false; cutting = false; ClearRasterSelection(); Sel.Clear(); RaiseSel(); Invalidate(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                if (HasSelection) { SelectionDelete(); e.Handled = true; return; }
                DeleteSelected(); e.Handled = true; return;
            }
            if (Sel.Count > 0 && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                float step = e.Shift ? 10 : 1;
                float dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0, dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                Doc.Checkpoint(); foreach (var o in Sel) if (!o.Locked) o.Translate(dx, dy); Doc.Raise(); e.Handled = true;
            }
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) { spaceDown = false; Cursor = ActiveTool == Tool.Select ? Cursors.Default : Cursors.Cross; }
        }

        // ------------------------------------------------------------ object commands
        public void DeleteSelected()
        {
            if (Sel.Count == 0) return;
            Doc.Checkpoint(); foreach (var o in Sel) Doc.Objs.Remove(o); Sel.Clear(); Doc.Raise(); RaiseSel();
        }
        public void SelectWhere(Func<Obj, bool> pred) { SetTool(Tool.Select); Sel = Doc.Objs.Where(o => !o.Locked && pred(o)).ToList(); RaiseSel(); Invalidate(); }
        public void SelectAll() { SetTool(Tool.Select); Sel = Doc.Objs.Where(o => !o.Locked).ToList(); RaiseSel(); Invalidate(); }
        public void Duplicate()
        {
            if (Sel.Count == 0) return;
            Doc.Checkpoint(); var copies = new List<Obj>();
            foreach (var o in Sel) { var c = o.Clone(); c.Translate(14, 14); Doc.Objs.Add(c); copies.Add(c); }
            Sel = copies; Doc.Raise(); RaiseSel();
        }
        public void CopyObjects()
        {
            if (Sel.Count == 0) return;
            ObjClipboard = Sel.Select(o => o.Clone()).ToList();
        }
        public static List<Obj> ObjClipboard;
        public void Paste()
        {
            if (Clipboard.ContainsImage())
            {
                using (var img = (Bitmap)Clipboard.GetImage())
                {
                    var bmp = Effects.To32(img);
                    var o = new ImageObj { Img = bmp, A = new PointF(20, 20), B = new PointF(20 + bmp.Width, 20 + bmp.Height) };
                    // keep very large pastes inside the picture
                    float k = Math.Min(1f, Math.Min((Doc.Width - 40f) / bmp.Width, (Doc.Height - 40f) / bmp.Height)); if (k < 1) o.B = new PointF(20 + bmp.Width * k, 20 + bmp.Height * k);
                    Doc.Add(o); SetTool(Tool.Select); Sel = new List<Obj> { o }; RaiseSel(); Invalidate(); return;
                }
            }
            if (ObjClipboard != null && ObjClipboard.Count > 0)
            {
                Doc.Checkpoint(); var made = new List<Obj>();
                foreach (var o in ObjClipboard) { var c = o.Clone(); c.Translate(16, 16); Doc.Objs.Add(c); made.Add(c); }
                SetTool(Tool.Select); Sel = made; Doc.Raise(); RaiseSel(); return;
            }
            if (Clipboard.ContainsText())
            {
                var t = new TextObj { A = new PointF(30, 30), Stroke = Style.Stroke, Px = Style.FontPx, Text = Clipboard.GetText() }; t.Measure(); Doc.Add(t);
                SetTool(Tool.Select); Sel = new List<Obj> { t }; RaiseSel();
            }
        }
        public void ApplyStyleToSelection()
        {
            if (Sel.Count == 0) return;
            Doc.Checkpoint();
            foreach (var o in Sel)
            {
                o.Opacity = Style.Opacity; o.Shadow = Style.Shadow;
                if (!(o is BlurObj) && !(o is SpotlightObj) && !(o is ImageObj)) { o.Stroke = Style.Stroke; o.Dash = Style.Dash; }
                if (!(o is StampObj) && !(o is BlurObj)) o.Width = Style.Width;
                if (o is ShapeObj) { var s = (ShapeObj)o; s.Fill = Style.Fill; s.Kind = Style.Shape; s.Corner = Style.Corner; }
                if (o is LineObj) { var l = (LineObj)o; l.ArrowStart = Style.ArrowStart; l.ArrowEnd = Style.ArrowEnd; }
                if (o is StepObj) { var s = (StepObj)o; s.Style = Style.Step; s.Fill = Style.Fill.A > 0 ? Style.Fill : Style.Stroke; }
                if (o is StampObj) ((StampObj)o).Glyph = Style.Stamp;
                if (o is TextObj) { var t = (TextObj)o; t.FontName = Style.Font; t.Px = Style.FontPx; t.Bold = Style.Bold; t.Italic = Style.Italic; t.Underline = Style.Underline; t.Align = Style.Align; t.Fill = Style.Fill; t.Measure(); }
                if (o is CalloutObj) { var c = (CalloutObj)o; c.Px = Style.FontPx; c.TextCol = Style.TextCol; c.Fill = Style.Fill.A > 0 ? Style.Fill : Style.Stroke; c.Kind = Style.Shape == ShapeKind.Ellipse ? ShapeKind.Ellipse : Style.Shape == ShapeKind.Rect ? ShapeKind.Rect : ShapeKind.Rounded; }
                if (o is BlurObj) { var b = (BlurObj)o; b.Kind = Style.Blur; b.Strength = Style.BlurStrength; }
                if (o is SpotlightObj) { var s = (SpotlightObj)o; s.Kind = Style.SpotKind; s.Dim = Style.SpotDim; }
                if (o is MagnifyObj) { var m = (MagnifyObj)o; m.Zoom = Style.MagZoom; m.Round = Style.MagRound; }
            }
            Doc.Raise();
        }
        // load the style panel from the first selected object
        public void PullStyleFromSelection()
        {
            if (Sel.Count == 0) return;
            var o = Sel[0];
            Style.Opacity = o.Opacity; Style.Shadow = o.Shadow; Style.Dash = o.Dash;
            if (!(o is ImageObj) && !(o is BlurObj) && !(o is SpotlightObj)) Style.Stroke = o.Stroke;
            Style.Width = o.Width <= 0 ? Style.Width : o.Width;
            if (o is ShapeObj) { var s = (ShapeObj)o; Style.Fill = s.Fill; Style.Shape = s.Kind; Style.Corner = s.Corner; }
            if (o is LineObj) { var l = (LineObj)o; Style.ArrowStart = l.ArrowStart; Style.ArrowEnd = l.ArrowEnd; }
            if (o is TextObj) { var t = (TextObj)o; Style.Font = t.FontName; Style.FontPx = t.Px; Style.Bold = t.Bold; Style.Italic = t.Italic; Style.Underline = t.Underline; Style.Align = t.Align; Style.Fill = t.Fill; }
            if (o is CalloutObj) { var c = (CalloutObj)o; Style.FontPx = c.Px; Style.TextCol = c.TextCol; Style.Fill = c.Fill; }
            if (o is StepObj) { var s = (StepObj)o; Style.Step = s.Style; Style.Fill = s.Fill; }
            if (o is StampObj) Style.Stamp = ((StampObj)o).Glyph;
            if (o is BlurObj) { var b = (BlurObj)o; Style.Blur = b.Kind; Style.BlurStrength = b.Strength; }
            if (o is SpotlightObj) { var s = (SpotlightObj)o; Style.SpotKind = s.Kind; Style.SpotDim = s.Dim; }
            if (o is MagnifyObj) { var m = (MagnifyObj)o; Style.MagZoom = m.Zoom; Style.MagRound = m.Round; }
        }
        public void Align(string how)
        {
            if (Sel.Count == 0) return;
            Doc.Checkpoint();
            var all = Sel.Count == 1 ? new RectangleF(0, 0, Doc.Width, Doc.Height) : Sel.Select(o => o.Bounds()).Aggregate(RectangleF.Union);
            foreach (var o in Sel)
            {
                var b = o.Bounds(); float dx = 0, dy = 0;
                switch (how)
                {
                    case "left": dx = all.Left - b.Left; break;
                    case "right": dx = all.Right - b.Right; break;
                    case "hcenter": dx = all.X + all.Width / 2 - (b.X + b.Width / 2); break;
                    case "top": dy = all.Top - b.Top; break;
                    case "bottom": dy = all.Bottom - b.Bottom; break;
                    case "vcenter": dy = all.Y + all.Height / 2 - (b.Y + b.Height / 2); break;
                }
                o.Translate(dx, dy);
            }
            if (Sel.Count >= 3 && (how == "hdist" || how == "vdist")) { }
            Doc.Raise();
        }
        public void Distribute(bool horizontal)
        {
            if (Sel.Count < 3) return;
            Doc.Checkpoint();
            var ord = Sel.OrderBy(o => horizontal ? o.Center().X : o.Center().Y).ToList();
            float a = horizontal ? ord.First().Center().X : ord.First().Center().Y, z = horizontal ? ord.Last().Center().X : ord.Last().Center().Y;
            for (int i = 1; i < ord.Count - 1; i++)
            {
                float target = a + (z - a) * i / (ord.Count - 1); var c = ord[i].Center();
                if (horizontal) ord[i].Translate(target - c.X, 0); else ord[i].Translate(0, target - c.Y);
            }
            Doc.Raise();
        }
        public void Order(string how)
        {
            foreach (var o in Sel.ToList())
            {
                if (how == "front") Doc.BringToFront(o); else if (how == "back") Doc.SendToBack(o); else if (how == "forward") Doc.Forward(o); else Doc.Backward(o);
            }
        }
        public void ToggleLock() { if (Sel.Count == 0) return; Doc.Checkpoint(); bool l = !Sel[0].Locked; foreach (var o in Sel) o.Locked = l; if (l) { Sel.Clear(); RaiseSel(); } Doc.Raise(); }

        // ------------------------------------------------------------ raster selections
        static bool[] MaskFromPath(GraphicsPath path, int w, int h)
        {
            using (var bm = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bm)) { g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.None; g.FillPath(Brushes.White, path); }
                int stride; var d = Effects.Bytes(bm, out stride); var m = new bool[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = d[y * stride + x * 4 + 3] > 127;
                return m;
            }
        }
        void SetMask(bool[] mask, GraphicsPath path)
        {
            int w = Doc.Width, h = Doc.Height, x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (mask[y * w + x]) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x1 < 0) { ClearRasterSelection(); return; }
            selMask = mask; selPath = path; selBounds = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            if (selOverlay != null) selOverlay.Dispose(); selOverlay = null;
            if (path == null)
            {
                selOverlay = new Bitmap(selBounds.Width, selBounds.Height, PixelFormat.Format32bppArgb);
                for (int y = 0; y < selBounds.Height; y++) for (int x = 0; x < selBounds.Width; x++)
                        if (mask[(y + selBounds.Y) * w + x + selBounds.X]) selOverlay.SetPixel(x, y, Color.FromArgb(90, Theme.Cobalt));
            }
            Say("Selection " + selBounds.Width + " x " + selBounds.Height);
            Invalidate();
        }
        Bitmap SelectionPixels()
        {
            var piece = new Bitmap(selBounds.Width, selBounds.Height, PixelFormat.Format32bppArgb);
            using (var flat = Doc.Render())
                for (int y = 0; y < selBounds.Height; y++) for (int x = 0; x < selBounds.Width; x++)
                        if (selMask[(y + selBounds.Y) * Doc.Width + x + selBounds.X]) piece.SetPixel(x, y, flat.GetPixel(x + selBounds.X, y + selBounds.Y));
            return piece;
        }
        public void SelectionCopy()
        {
            if (!HasSelection) return;
            using (var p = SelectionPixels()) Clipboard.SetImage(p); Say("Selection copied");
        }
        public void SelectionDelete()
        {
            if (!HasSelection) return;
            var m = selMask; int w = Doc.Width;
            Doc.ApplyPixels(b => { var nb = Effects.To32(b); for (int y = selBounds.Top; y < selBounds.Bottom; y++) for (int x = selBounds.Left; x < selBounds.Right; x++) if (m[y * w + x]) nb.SetPixel(x, y, Color.Transparent); return nb; });
            ClearRasterSelection();
        }
        public void SelectionCut() { SelectionCopy(); SelectionDelete(); }
        public void SelectionFill(Color c)
        {
            if (!HasSelection) return;
            var m = selMask; int w = Doc.Width;
            Doc.ApplyPixels(b => { var nb = Effects.To32(b); for (int y = selBounds.Top; y < selBounds.Bottom; y++) for (int x = selBounds.Left; x < selBounds.Right; x++) if (m[y * w + x]) nb.SetPixel(x, y, c); return nb; });
        }
        public void SelectionCrop() { if (!HasSelection) return; var r = selBounds; ClearRasterSelection(); Doc.Crop(r); }
        public void SelectionInvert()
        {
            if (!HasSelection) return;
            var m = new bool[selMask.Length]; for (int i = 0; i < m.Length; i++) m[i] = !selMask[i]; SetMask(m, null);
        }
        // turn the selection into a movable object (Smart Move); the hole is rebuilt from its surroundings
        public void LiftSelection(bool fillHole)
        {
            if (!HasSelection) return;
            var m = selMask; int w = Doc.Width; var b = selBounds;
            using (var piece = SelectionPixels())
            {
                var img = new Bitmap(piece);
                Doc.Checkpoint();
                var nb = Effects.To32(Doc.Base);
                if (fillHole)
                {
                    using (var filled = Effects.To32(Doc.Base))
                    {
                        Effects.InpaintRect(filled, b);
                        for (int y = b.Top; y < b.Bottom; y++) for (int x = b.Left; x < b.Right; x++) if (m[y * w + x]) nb.SetPixel(x, y, filled.GetPixel(x, y));
                    }
                }
                Doc.Base = nb;
                var o = new ImageObj { Img = img, A = b.Location, B = new PointF(b.Right, b.Bottom) };
                Doc.Objs.Add(o); Doc.Raise();
                ClearRasterSelection(); Sel = new List<Obj> { o }; RaiseSel();
            }
        }
    }
}

