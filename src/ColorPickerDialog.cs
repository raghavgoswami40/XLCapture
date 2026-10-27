// The app's one colour picker (replaces the Windows colour dialog everywhere): a saturation/brightness square, a hue slider,
// an eyedropper that reads any pixel on screen, and the value as HEX, RGB, CSS or HSL that you can also type into.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SnapLite
{
    static class Hsv
    {
        public static Color ToColor(float h, float s, float v)
        {
            h = ((h % 360f) + 360f) % 360f; float c = v * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }
        public static void From(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = d == 0 ? 0 : max == r ? 60f * (((g - b) / d + 6) % 6) : max == g ? 60f * ((b - r) / d + 2) : 60f * ((r - g) / d + 4);
            s = max == 0 ? 0 : d / max; v = max;
        }
        public static void ToHsl(Color c, out int h, out int s, out int l) { h = (int)Math.Round(c.GetHue()); s = (int)Math.Round(c.GetSaturation() * 100); l = (int)Math.Round(c.GetBrightness() * 100); }
        public static Color FromHsl(float h, float s, float l)
        {
            s /= 100f; l /= 100f; float c = (1 - Math.Abs(2 * l - 1)) * s, hh = (((h % 360f) + 360f) % 360f);
            float v = l + c / 2f, sv = v == 0 ? 0 : 2 * (1 - l / v); return ToColor(hh, Math.Max(0, Math.Min(1, sv)), Math.Max(0, Math.Min(1, v)));
        }
    }

    // the square: saturation left to right, brightness bottom to top
    class SvSquare : Control
    {
        public float H, S = 1, V = 1; public event EventHandler Changed; bool drag;
        public SvSquare() { DoubleBuffered = true; Cursor = Cursors.Cross; AccessibleName = "Saturation and brightness"; }
        RectangleF Box { get { float p = Dpi.F(8); return new RectangleF(p, p, Width - 2 * p - 1, Height - 2 * p - 1); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Backdrop(g, Parent != null ? Parent.BackColor : Color.White, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias; var r = Box;
            using (var p = Theme.RoundRect(Rectangle.Round(r), Dpi.S(6)))
            {
                g.SetClip(p);
                using (var b = new LinearGradientBrush(r, Color.White, Hsv.ToColor(H, 1, 1), LinearGradientMode.Horizontal)) g.FillRectangle(b, r);
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(0, 0, 0, 0), Color.Black, LinearGradientMode.Vertical)) g.FillRectangle(b, r);
                g.ResetClip();
                using (var pen = new Pen(Theme.Rule)) g.DrawPath(pen, p);
            }
            float x = r.X + S * r.Width, y = r.Y + (1 - V) * r.Height, rad = Dpi.F(8f);
            using (var pen = new Pen(Color.FromArgb(140, 0, 0, 0), Dpi.F(4f))) g.DrawEllipse(pen, x - rad, y - rad, rad * 2, rad * 2);
            using (var pen = new Pen(Color.White, Dpi.F(2.5f))) g.DrawEllipse(pen, x - rad, y - rad, rad * 2, rad * 2);
        }
        void Pick(Point p) { var r = Box; S = Math.Max(0, Math.Min(1, (p.X - r.X) / r.Width)); V = 1 - Math.Max(0, Math.Min(1, (p.Y - r.Y) / r.Height)); Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; Pick(e.Location); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) Pick(e.Location); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            float d = e.Shift ? 0.1f : 0.02f; bool hit = true;
            switch (e.KeyCode) { case Keys.Left: S -= d; break; case Keys.Right: S += d; break; case Keys.Up: V += d; break; case Keys.Down: V -= d; break; default: hit = false; break; }
            if (!hit) return; S = Math.Max(0, Math.Min(1, S)); V = Math.Max(0, Math.Min(1, V)); Invalidate(); e.Handled = true; if (Changed != null) Changed(this, EventArgs.Empty);
        }
    }

    // the rainbow slider
    class HueSlider : Control
    {
        public float H; public event EventHandler Changed; bool drag;
        public HueSlider() { DoubleBuffered = true; Cursor = Theme.Hand; AccessibleName = "Hue"; }
        RectangleF Track { get { return new RectangleF(Dpi.F(9), Height / 2f - Dpi.F(6), Width - Dpi.F(18), Dpi.F(12)); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Backdrop(g, Parent != null ? Parent.BackColor : Color.White, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias; var t = Track;
            using (var p = Theme.Pill(t))
            {
                g.SetClip(p);
                using (var b = new LinearGradientBrush(t, Color.Red, Color.Red, LinearGradientMode.Horizontal))
                {
                    var cb = new ColorBlend(7) { Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f }, Colors = new[] { Color.FromArgb(255, 0, 0), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 0, 0) } };
                    b.InterpolationColors = cb; g.FillRectangle(b, t);
                }
                g.ResetClip();
            }
            float x = t.X + H / 360f * t.Width, rad = Dpi.F(8f);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x - rad, Height / 2f - rad, rad * 2, rad * 2);
            using (var pen = new Pen(Theme.Charcoal, Dpi.F(2f))) g.DrawEllipse(pen, x - rad, Height / 2f - rad, rad * 2, rad * 2);
            if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Charcoal) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        void Pick(int px) { var t = Track; H = Math.Max(0, Math.Min(360, (px - t.X) / t.Width * 360f)); Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); drag = true; Capture = true; Pick(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) Pick(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right) return; H = Math.Max(0, Math.Min(360, H + (e.KeyCode == Keys.Left ? -1 : 1) * (e.Shift ? 15 : 3))); Invalidate(); e.Handled = true; if (Changed != null) Changed(this, EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    class ColorPickerDialog : Form
    {
        readonly SvSquare sv = new SvSquare(); readonly HueSlider hue = new HueSlider();
        readonly ThemedCombo mode = new ThemedCombo(); readonly TextBox value = new TextBox();
        readonly Panel swatch = new Panel(), oldSwatch = new Panel(); readonly FlatButton drop; readonly Label msg;
        bool busy; Color start, current;
        public Color Result { get { return current; } }

        // Opens the picker on `initial`. Returns true when the user chose a colour (in `chosen`).
        public static bool Pick(IWin32Window owner, Color initial, string title, out Color chosen)
        {
            using (var d = new ColorPickerDialog(initial, title)) { bool ok = d.ShowDialog(owner) == DialogResult.OK; chosen = ok ? d.Result : initial; return ok; }
        }
        public static ColorPickerDialog ForShot(Color c) { return new ColorPickerDialog(c, "Box colour"); }
        public static bool Pick(IWin32Window owner, Color initial, out Color chosen) { return Pick(owner, initial, "Choose a colour", out chosen); }

        ColorPickerDialog(Color initial, string title)
        {
            start = Color.FromArgb(255, initial); current = start;
            Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White; Font = Theme.Body(10f); Icon = AppIcon.Get(); ClientSize = Dpi.S(336, 456); KeyPreview = true;
            int m = Dpi.S(20), w = Dpi.S(336) - 2 * m;
            sv.Location = new Point(m - Dpi.S(8), m - Dpi.S(8)); sv.Size = new Size(w + Dpi.S(16), Dpi.S(216));
            drop = new FlatButton("", false) { Icon = Ic.Dropper, Size = Dpi.S(40, 40), Location = new Point(m, Dpi.S(236)), AccessibleName = "Pick a colour from the screen" };
            new ToolTip().SetToolTip(drop, "Pick a colour from anywhere on the screen");
            hue.Location = new Point(m + Dpi.S(52), Dpi.S(236) + Dpi.S(10)); hue.Size = new Size(w - Dpi.S(52), Dpi.S(20));
            var nowLbl = new Label { Text = "New", AutoSize = true, ForeColor = Theme.Slate, Font = Theme.Body(9f), Location = new Point(m, Dpi.S(290)) };
            swatch.Location = new Point(m, Dpi.S(308)); swatch.Size = Dpi.S(64, 36); swatch.Paint += PaintSwatch;
            var wasLbl = new Label { Text = "Current", AutoSize = true, ForeColor = Theme.Slate, Font = Theme.Body(9f), Location = new Point(m + Dpi.S(72), Dpi.S(290)) };
            oldSwatch.Location = new Point(m + Dpi.S(72), Dpi.S(308)); oldSwatch.Size = Dpi.S(64, 36); oldSwatch.Paint += PaintSwatch; oldSwatch.Tag = "old";
            mode.Items.AddRange(new object[] { "HEX", "RGB", "CSS", "HSL" }); mode.SelectedIndex = 0; mode.Width = Dpi.S(78); mode.Location = new Point(m + Dpi.S(160), Dpi.S(312)); mode.AccessibleName = "Colour format";
            value.BorderStyle = BorderStyle.FixedSingle; value.Font = Theme.Body(10f); value.Location = new Point(m, Dpi.S(358)); value.Width = w; value.AccessibleName = "Colour value";
            msg = new Label { AutoSize = false, Location = new Point(m, Dpi.S(384)), Size = new Size(w, Dpi.S(18)), ForeColor = Theme.Mark, Font = Theme.Body(8.5f) };
            var ok = new FlatButton("Select", true) { Location = new Point(Dpi.S(336) - m - Dpi.S(88) - Dpi.S(8) - Dpi.S(108), Dpi.S(404)), Size = Dpi.S(108, 38), DialogResult = DialogResult.OK };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(336) - m - Dpi.S(88), Dpi.S(404)), Size = Dpi.S(88, 38), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { sv, drop, hue, nowLbl, swatch, wasLbl, oldSwatch, mode, value, msg, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
            float h, s, v; Hsv.From(start, out h, out s, out v); hue.H = h; sv.H = h; sv.S = s; sv.V = v;
            sv.Changed += delegate { Update(false); }; hue.Changed += delegate { sv.H = hue.H; Update(false); };
            mode.SelectedIndexChanged += delegate { ShowValue(); };
            value.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Apply(); } };
            value.Leave += delegate { Apply(); };
            drop.Click += delegate { Eyedrop(); };
            Update(true);
        }

        void PaintSwatch(object s, PaintEventArgs e)
        {
            var p = (Panel)s; var c = p.Tag as string == "old" ? start : current;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.Backdrop(e.Graphics, Color.White, p.ClientRectangle);
            var r = new RectangleF(1, 1, p.Width - 3, p.Height - 3);
            using (var path = Theme.RoundRect(Rectangle.Round(r), Dpi.S(6))) { using (var b = new SolidBrush(c)) e.Graphics.FillPath(b, path); using (var pen = new Pen(Theme.Rule)) e.Graphics.DrawPath(pen, path); }
        }

        void Update(bool fromInit)
        {
            current = Hsv.ToColor(hue.H, sv.S, sv.V);
            if (fromInit) { current = start; }
            swatch.Invalidate(); oldSwatch.Invalidate(); sv.Invalidate(); hue.Invalidate(); ShowValue();
        }
        void ShowValue()
        {
            busy = true; var c = current; string t;
            switch (mode.SelectedIndex)
            {
                case 1: t = c.R + ", " + c.G + ", " + c.B; break;
                case 2: t = "rgb(" + c.R + ", " + c.G + ", " + c.B + ")"; break;
                case 3: int h, s, l; Hsv.ToHsl(c, out h, out s, out l); t = h + ", " + s + "%, " + l + "%"; break;
                default: t = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); break;
            }
            value.Text = t; msg.Text = ""; busy = false;
        }
        // read what was typed in the current format
        void Apply()
        {
            if (busy) return; string t = value.Text.Trim(); Color c;
            if (!Parse(t, mode.SelectedIndex, out c)) { msg.Text = mode.SelectedIndex == 0 ? "Use a hex value such as #FFC300." : "Use three numbers, such as 255, 195, 0."; return; }
            current = c; float h, s, v; Hsv.From(c, out h, out s, out v);
            if (s > 0 && v > 0) hue.H = h; sv.H = hue.H; sv.S = s; sv.V = v; swatch.Invalidate(); sv.Invalidate(); hue.Invalidate(); ShowValue();
        }
        static bool Parse(string t, int mode, out Color c)
        {
            c = Color.Black;
            if (mode == 0 || t.StartsWith("#"))
            {
                string x = t.TrimStart('#'); if (x.Length == 3) x = "" + x[0] + x[0] + x[1] + x[1] + x[2] + x[2];
                int n; if (x.Length != 6 || !int.TryParse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return false;
                c = Color.FromArgb((n >> 16) & 255, (n >> 8) & 255, n & 255); return true;
            }
            var ms = Regex.Matches(t, @"\d+(\.\d+)?"); if (ms.Count < 3) return false;
            double a = double.Parse(ms[0].Value, CultureInfo.InvariantCulture), b = double.Parse(ms[1].Value, CultureInfo.InvariantCulture), d = double.Parse(ms[2].Value, CultureInfo.InvariantCulture);
            if (mode == 3) { c = Hsv.FromHsl((float)a, (float)Math.Min(100, b), (float)Math.Min(100, d)); return true; }
            c = Color.FromArgb((int)Math.Min(255, a), (int)Math.Min(255, b), (int)Math.Min(255, d)); return true;
        }

        // click the dropper, then click any pixel on screen (Esc cancels)
        void Eyedrop()
        {
            Cursor = Cursors.Cross; Capture = true; msg.ForeColor = Theme.Slate; msg.Text = "Click anywhere on the screen. Esc cancels.";
            MouseEventHandler down = null; KeyEventHandler key = null;
            Action finish = delegate { MouseDown -= down; KeyDown -= key; Capture = false; Cursor = Cursors.Default; msg.ForeColor = Theme.Mark; };
            down = delegate(object o, MouseEventArgs e)
            {
                var p = Cursor.Position; Color c;
                using (var bmp = new Bitmap(1, 1)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(p, Point.Empty, new Size(1, 1)); c = bmp.GetPixel(0, 0); }
                finish(); value.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); mode.SelectedIndex = 0; Apply();
            };
            key = delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; finish(); msg.Text = ""; } };
            MouseDown += down; KeyDown += key;
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (DialogResult == DialogResult.OK) Apply(); base.OnFormClosing(e); }
    }

    // a small colour button: the colour in a rounded box with a chevron; click opens the colour picker
    class ColorSwatch : Control
    {
        Color color; bool hover; public string Title = "Choose a colour"; public event EventHandler ColorChanged;
        public ColorSwatch(Color c) { color = c; Size = Dpi.S(64, 30); Cursor = Theme.Hand; DoubleBuffered = true; TabStop = true; AccessibleRole = AccessibleRole.PushButton; }
        public Color Color { get { return color; } set { color = value; Invalidate(); } }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e); Color c; if (ColorPickerDialog.Pick(FindForm(), color, Title, out c)) { color = c; Invalidate(); if (ColorChanged != null) ColorChanged(this, EventArgs.Empty); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Backdrop(g, Parent != null ? Parent.BackColor : Color.White, ClientRectangle); g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var p = Theme.RoundRect(Rectangle.Round(r), Dpi.S(8)))
            {
                using (var b = new SolidBrush(Color.White)) g.FillPath(b, p);
                using (var pen = new Pen(hover || Focused ? Theme.Charcoal : Theme.Rule, hover || Focused ? 1.6f : 1.2f)) g.DrawPath(pen, p);
            }
            var sw = new Rectangle(Dpi.S(6), Dpi.S(6), Width - Dpi.S(34), Height - Dpi.S(12));
            using (var p = Theme.RoundRect(sw, Dpi.S(4))) { using (var b = new SolidBrush(color)) g.FillPath(b, p); using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0))) g.DrawPath(pen, p); }
            float cx = Width - Dpi.S(14), cy = Height / 2f, a = Dpi.F(3.5f);
            using (var pen = new Pen(Theme.Charcoal, Dpi.F(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(pen, new[] { new PointF(cx - a, cy - a / 2f), new PointF(cx, cy + a / 2f), new PointF(cx + a, cy - a / 2f) });
        }
    }
}
