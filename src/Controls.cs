// Themed controls: colour chip with palette popup, compact numeric field, section label.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    static class Palette
    {
        public static readonly Color[] Swatches =
        {
            Color.FromArgb(0xE8,0x45,0x2C), Color.FromArgb(0xF0,0x8A,0x24), Color.FromArgb(0xF5,0xC5,0x18), Color.FromArgb(0x1A,0x8A,0x57),
            Color.FromArgb(0x12,0xA3,0x9A), Color.FromArgb(0x27,0x48,0xD8), Color.FromArgb(0x7A,0x4F,0xD6), Color.FromArgb(0xD6,0x33,0x6C),
            Color.FromArgb(0x14,0x20,0x2B), Color.FromArgb(0x5B,0x6B,0x7A), Color.FromArgb(0xC4,0xCC,0xD4), Color.FromArgb(0xFF,0xFF,0xFF)
        };
    }

    class PalettePanel : Control
    {
        public Color Picked; public bool PickedNone; public bool Eyedropper;
        public event EventHandler Done;
        readonly bool allowNone;
        readonly List<Rectangle> cells = new List<Rectangle>();
        Rectangle noneR, moreR, dropR;
        int hover = -1;

        public PalettePanel(bool allowNone)
        {
            this.allowNone = allowNone; DoubleBuffered = true; BackColor = Theme.Sheet; Cursor = Theme.Hand;
            int cell = Dpi.S(26), gap = Dpi.S(6), x0 = Dpi.S(10), y0 = Dpi.S(10), cols = 6, bw = Dpi.S(58), bh = Dpi.S(26);
            for (int i = 0; i < Palette.Swatches.Length; i++)
                cells.Add(new Rectangle(x0 + (i % cols) * (cell + gap), y0 + (i / cols) * (cell + gap), cell, cell));
            int yb = y0 + 2 * (cell + gap) + 2;
            noneR = new Rectangle(x0, yb, bw, bh); dropR = new Rectangle(x0 + bw + gap, yb, bw, bh); moreR = new Rectangle(x0 + 2 * (bw + gap), yb, bw, bh);
            Size = new Size(x0 * 2 + cols * (cell + gap) - gap, yb + bh + x0);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Sheet);
            for (int i = 0; i < cells.Count; i++)
            {
                using (var p = Theme.RoundRect(cells[i], 4)) using (var b = new SolidBrush(Palette.Swatches[i])) g.FillPath(b, p);
                using (var p = Theme.RoundRect(cells[i], 4)) using (var pen = new Pen(i == hover ? Theme.Cobalt : Theme.Rule, i == hover ? 2f : 1f)) g.DrawPath(pen, p);
            }
            using (var f = Theme.Label(8.5f))
            {
                Chip(g, noneR, allowNone ? "None" : "-", f, allowNone);
                Chip(g, dropR, "Pick", f, true);
                Chip(g, moreR, "More", f, true);
            }
        }
        void Chip(Graphics g, Rectangle r, string t, Font f, bool on)
        {
            using (var p = Theme.RoundRect(r, Dpi.F(Theme.Radius))) { using (var b = new SolidBrush(Theme.White)) g.FillPath(b, p); using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, p); }
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var b = new SolidBrush(on ? Theme.Ink : Theme.Mist)) g.DrawString(t, f, b, r, sf);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1; for (int i = 0; i < cells.Count; i++) if (cells[i].Contains(e.Location)) h = i;
            if (h != hover) { hover = h; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            for (int i = 0; i < cells.Count; i++) if (cells[i].Contains(e.Location)) { Picked = Palette.Swatches[i]; PickedNone = false; Fire(); return; }
            if (allowNone && noneR.Contains(e.Location)) { PickedNone = true; Fire(); return; }
            if (dropR.Contains(e.Location)) { Eyedropper = true; Fire(); return; }
            if (moreR.Contains(e.Location))
            {
                Color c; if (ColorPickerDialog.Pick(FindForm(), Picked, out c)) { Picked = c; PickedNone = false; Fire(); }
            }
        }
        void Fire() { if (Done != null) Done(this, EventArgs.Empty); }
    }

    class ColorChip : Control
    {
        Color color; public bool AllowNone; public string Tip;
        public event EventHandler ColorChanged;
        public event Action PickRequested;
        readonly ToolTip tt = new ToolTip();

        public ColorChip(Color c, bool allowNone, string tip)
        {
            color = c; AllowNone = allowNone; Size = Dpi.S(40, 30); Cursor = Theme.Hand; DoubleBuffered = true;
            tt.SetToolTip(this, tip); Tip = tip; TabStop = true;
        }
        public Color Value
        {
            get { return color; }
            set { color = value; Invalidate(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent != null ? Parent.BackColor : Theme.Sheet);
            var r = new Rectangle(1, 3, Width - 3, Height - 7);
            using (var p = Theme.RoundRect(r, 4))
            {
                if (color.A == 0) { using (var b = new SolidBrush(Color.White)) g.FillPath(b, p); using (var pen = new Pen(Theme.Mark, 2f)) { var st = g.Save(); g.SetClip(p); g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Top); g.Restore(st); } }
                else { Theme.Checker(g, r, 5); using (var b = new SolidBrush(color)) g.FillPath(b, p); }
                using (var pen = new Pen(Focused ? Theme.Cobalt : Theme.Mist, Focused ? 2f : 1f)) g.DrawPath(pen, p);
            }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Pop(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Pop(); }
        void Pop()
        {
            var pal = new PalettePanel(AllowNone) { Picked = color };
            var host = new ToolStripControlHost(pal) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = pal.Size };
            var dd = new ToolStripDropDown { Padding = Padding.Empty, BackColor = Theme.Sheet, DropShadowEnabled = true, AutoClose = true, Renderer = new ThemeRenderer() };
            dd.Items.Add(host);
            pal.Done += delegate
            {
                dd.Close();
                if (pal.Eyedropper) { if (PickRequested != null) PickRequested(); return; }
                color = pal.PickedNone ? Color.Transparent : pal.Picked; Invalidate();
                if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
            };
            dd.Show(this, new Point(0, Height + 2));
        }
    }

    class SectionLabel : Label
    {
        public SectionLabel(string t)
        {
            Text = t; AutoSize = true; ForeColor = Theme.Slate; Font = Theme.Label(8.5f);
            Margin = new Padding(Dpi.S(10), Dpi.S(11), 3, 0);
        }
    }

    // compact integer/decimal field that matches the toolbar height
    class NumField : NumericUpDown
    {
        public NumField(decimal min, decimal max, decimal val, int width)
        {
            Minimum = min; Maximum = max; Value = Math.Max(min, Math.Min(max, val)); Width = Dpi.S(width); Font = Theme.Body(9f);
            BorderStyle = BorderStyle.FixedSingle; BackColor = Color.White; Margin = new Padding(2, Dpi.S(5), 2, 0);
        }
    }

    // On/off switch with an animated knob; label lives beside it (never inside), per the UX rule on visible labels.
    class ToggleSwitch : Control
    {
        bool on; float knob; readonly Timer anim = new Timer { Interval = 15 };
        public event EventHandler Toggled;
        public string DisabledReason;
        public ToggleSwitch(bool value)
        {
            on = value; knob = value ? 1 : 0; Size = Dpi.S(50, 26); Cursor = Theme.Hand; TabStop = true; DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            anim.Tick += delegate { float t = on ? 1 : 0; knob += (t - knob) * 0.35f; if (Math.Abs(t - knob) < 0.02f) { knob = t; anim.Stop(); } Invalidate(); };
        }
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; anim.Start(); AccessibleDescription = on ? "On" : "Off"; }
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (!Enabled) return; Checked = !on; if (Toggled != null) Toggled(this, EventArgs.Empty); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Theme.White, ClientRectangle);
            var r = new RectangleF(1.5f, 1.5f, Width - 3, Height - 3);
            Color off = Theme.Mist, onC = Theme.Accent;      // off is grey, on is the accent; the knob is always white, so the colour alone says on or off
            Color track = !Enabled ? Theme.Rule : Blend(off, onC, knob);
            using (var p = Theme.RoundRect(r, r.Height / 2)) using (var b = new SolidBrush(track)) g.FillPath(b, p);
            if (Focused && ShowFocusCues) using (var p = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f)) using (var pen = new Pen(Theme.AccentDark, 1.5f)) g.DrawPath(pen, p);
            float d = r.Height - Dpi.F(6), x = r.X + Dpi.F(3) + (r.Width - d - Dpi.F(6)) * knob;
            using (var b = new SolidBrush(Enabled ? Color.White : Theme.Sheet)) g.FillEllipse(b, x, r.Y + Dpi.F(3), d, d);
        }
        static Color Blend(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        protected override void Dispose(bool d) { if (d) anim.Dispose(); base.Dispose(d); }
    }

    // The big round capture button.
    class RoundButton : Control
    {
        bool hover, down; public Color Fill = Theme.Accent;          // the accent to take a picture, red when it records
        public RoundButton(string text) { Text = text; Cursor = Theme.Hand; TabStop = true; DoubleBuffered = true; AccessibleName = text; AccessibleRole = AccessibleRole.PushButton;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        // The main action is a squircle with a viewfinder in its corners (the same crop marks as the app's icon)
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Theme.Paper, ClientRectangle);
            float inset = down ? Dpi.F(6) : Dpi.F(3);
            var r = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
            var c = down ? Theme.Mix(Fill, Color.Black, 0.25f) : hover ? Theme.Mix(Fill, Color.Black, 0.12f) : Fill;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            float m = r.Width * 0.24f, len = r.Width * 0.12f;       // the four corner brackets, inside the circle
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), Dpi.F(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })        // a viewfinder, as in the logo
            {
                float l = r.X + m, t = r.Y + m, rr = r.Right - m, bb = r.Bottom - m;
                g.DrawLines(pen, new[] { new PointF(l, t + len), new PointF(l, t), new PointF(l + len, t) });
                g.DrawLines(pen, new[] { new PointF(rr - len, t), new PointF(rr, t), new PointF(rr, t + len) });
                g.DrawLines(pen, new[] { new PointF(l, bb - len), new PointF(l, bb), new PointF(l + len, bb) });
                g.DrawLines(pen, new[] { new PointF(rr - len, bb), new PointF(rr, bb), new PointF(rr, bb - len) });
            }
            if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Mix(Fill, Color.Black, 0.2f), Dpi.F(1.5f))) g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
            using (var f = Theme.Semi(14f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Text, f, Brushes.White, r, sf);
        }
    }

    // Icon-over-label tab used for All-in-One / Image / Video.
    class TabButton : Control
    {
        public Ic Icon; public bool Selected; public string Badge; bool hover;     // Badge: a small gold tag in the corner, such as "Beta"
        public TabButton(Ic icon, string text) { Icon = icon; Text = text; Cursor = Theme.Hand; TabStop = true; DoubleBuffered = true; AccessibleName = text; AccessibleRole = AccessibleRole.PageTab; Size = Dpi.S(104, 80); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent != null ? Parent.BackColor : Theme.Paper);
            // the chosen tab is a white sheet lifted off the window's grey with its icon in the accent; the others stay quiet until the pointer is on them
            var shape = new RectangleF(Dpi.S(6) + 0.5f, Dpi.S(3) + 0.5f, Width - Dpi.S(12) - 1, Height - Dpi.S(6) - 1);
            using (var p = Theme.RoundRect(shape, Dpi.S(10)))
            {
                if (Selected) { using (var b = new SolidBrush(Color.White)) g.FillPath(b, p); using (var pen = new Pen(Theme.Rule)) g.DrawPath(pen, p); }
                else if (hover) using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, p);
            }
            int s = Dpi.S(24);
            Icons.Draw(g, Icon, new RectangleF(shape.X + (shape.Width - s) / 2, shape.Y + Dpi.S(9), s, s), Selected ? Theme.Accent : Theme.Slate);
            using (var f = Selected ? Theme.Semi(9.5f) : Theme.Body(9.5f)) using (var br = new SolidBrush(Selected ? Theme.Ink : Theme.InkSoft)) using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(Text, f, br, new RectangleF(0, shape.Y + Dpi.S(9) + s + Dpi.S(3), Width, Dpi.S(20)), sf);
            if (!string.IsNullOrEmpty(Badge))
                using (var f = Theme.Semi(7f))
                {
                    var sz = TextRenderer.MeasureText(g, Badge, f, new Size(200, 40), TextFormatFlags.NoPadding); int bw = sz.Width + Dpi.S(10), bh = Dpi.S(14);
                    var br = new Rectangle((int)shape.Right - bw - Dpi.S(5), (int)shape.Y + Dpi.S(5), bw, bh);
                    using (var gp = Theme.Pill(br)) using (var fill = new SolidBrush(Theme.AccentTint)) g.FillPath(fill, gp);
                    TextRenderer.DrawText(g, Badge, f, br, Theme.AccentDeep, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            if (Focused && ShowFocusCues) using (var p = Theme.RoundRect(new RectangleF(shape.X + 1.5f, shape.Y + 1.5f, shape.Width - 3, shape.Height - 3), Dpi.S(9))) using (var pen = new Pen(Theme.Accent, Dpi.F(1.5f))) g.DrawPath(pen, p);
        }
    }

    // A hotkey shown as keys: "Ctrl+Shift+C" becomes three small keycaps. Any other text (a sentence, "No hotkey") is shown as plain secondary text.
    class KeyHint : Control
    {
        public KeyHint() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Theme.Paper, ClientRectangle);
            string t = Text ?? ""; var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            if (t.Length == 0 || t.IndexOf(' ') >= 0 || t.IndexOf('+') < 0)
            {
                using (var f = Theme.Body(9.5f)) TextRenderer.DrawText(g, t, f, ClientRectangle, Theme.Slate, flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                return;
            }
            var keys = t.Split('+'); int pad = Dpi.S(7), gap = Dpi.S(4), h = Math.Min(Height - 2, Dpi.S(24));
            using (var f = Theme.Semi(8.5f))
            {
                var widths = keys.Select(k => Math.Max(h, TextRenderer.MeasureText(g, k, f, new Size(200, 40), TextFormatFlags.NoPadding).Width + 2 * pad)).ToArray();
                int total = widths.Sum() + gap * (keys.Length - 1), x = (Width - total) / 2, y = (Height - h) / 2;
                for (int i = 0; i < keys.Length; i++)
                {
                    var r = new RectangleF(x + 0.5f, y + 0.5f, widths[i] - 1, h - 1);
                    using (var p = Theme.RoundRect(r, Dpi.F(5))) { using (var b = new SolidBrush(Color.White)) g.FillPath(b, p); using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, p); }
                    TextRenderer.DrawText(g, keys[i], f, new Rectangle(x, y, widths[i], h), Theme.InkSoft, flags | TextFormatFlags.HorizontalCenter);
                    x += widths[i] + gap;
                }
            }
        }
    }

    // Label + slider + number box, kept in sync (Tool Properties rows).
    class SliderRow : Panel
    {
        public readonly TrackBar Bar; public readonly NumericUpDown Box; bool sync; public event Action<int> ValueChanged;
        public SliderRow(string label, int min, int max, int value, int width)
        {
            Width = Dpi.S(width); Height = Dpi.S(34); BackColor = Theme.Sheet; Margin = new Padding(0, 0, 0, Dpi.S(2));
            var l = new Label { Text = label, AutoSize = false, Location = new Point(0, Dpi.S(8)), Size = Dpi.S(76, 20), ForeColor = Theme.InkSoft, TextAlign = ContentAlignment.MiddleRight };
            Bar = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickStyle = TickStyle.None, AutoSize = false, Location = new Point(Dpi.S(80), Dpi.S(4)), Size = new Size(Width - Dpi.S(80 + 64), Dpi.S(28)), BackColor = Theme.Sheet };
            Box = new NumericUpDown { Minimum = min, Maximum = max, Value = Bar.Value, Location = new Point(Width - Dpi.S(58), Dpi.S(5)), Width = Dpi.S(56), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
            Bar.AccessibleName = label; Box.AccessibleName = label;
            Bar.ValueChanged += delegate { if (sync) return; sync = true; Box.Value = Bar.Value; sync = false; if (ValueChanged != null) ValueChanged(Bar.Value); };
            Box.ValueChanged += delegate { if (sync) return; sync = true; Bar.Value = (int)Box.Value; sync = false; if (ValueChanged != null) ValueChanged((int)Box.Value); };
            Controls.AddRange(new Control[] { l, Bar, Box });
        }
    }

    class SoftToolStrip : ToolStrip
    {
        public SoftToolStrip()
        {
            GripStyle = ToolStripGripStyle.Hidden; Renderer = new ThemeRenderer(); BackColor = Theme.Sheet; ForeColor = Theme.Ink;
            Font = Theme.Body(9f); ImageScalingSize = Dpi.S(20, 20); Padding = Dpi.P(6, 3, 6, 3);
        }
    }

    // The app draws in physical pixels (per-monitor DPI aware, so screen capture lines up on every monitor), and
    // its layout is sized once for the startup scaling. When Windows resizes a window for another monitor's
    // scaling the controls do not follow and get clipped, so these forms keep their size instead: same pixels,
    // still sharp, never cut off.
    class DpiForm : Form
    {
        const int WM_DPICHANGED = 0x02E0;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        Size lastClient; bool dpiBusy; uint dpiRec;
        // a form whose layout was written in plain 96 dpi pixels: its controls are scaled to this screen once, when it loads, so the
        // text (which is sized in points and grows with the screen) is never cut off
        protected bool DesignPixels; bool designScaled;
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (DesignPixels && !designScaled && Dpi.K > 1.01f)
            {
                designScaled = true; var old = Size; AutoScaleMode = AutoScaleMode.None;
                Scale(new SizeF(Dpi.K, Dpi.K)); lastClient = ClientSize;
                if (StartPosition == FormStartPosition.CenterParent || StartPosition == FormStartPosition.CenterScreen)
                {
                    var wa = Screen.FromControl(this).WorkingArea;
                    Left = Math.Max(wa.Left, Math.Min(wa.Right - Width, Left - (Width - old.Width) / 2)); Top = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, Top - (Height - old.Height) / 2));
                }
            }
        }
        uint WindowDpi() { try { return GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { return 96; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); dpiRec = WindowDpi(); if (lastClient.IsEmpty) lastClient = ClientSize; }
        // remember the size the layout was made for; a size change that only comes from the monitor's thicker or
        // thinner title bar (the window's DPI differs from the last one seen) is not a real resize
        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            if (!dpiBusy && IsHandleCreated && WindowState == FormWindowState.Normal && ClientSize.Width > 0 && WindowDpi() == dpiRec) lastClient = ClientSize;
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AdjustWindowRectExForDpi(ref RECT r, uint style, bool menu, uint exStyle, uint dpi);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
        // WinForms works out frame sizes for the system scaling, not this monitor's, and the real frame is not
        // updated yet when the DPI message arrives, so ask Windows for the frame at the new DPI
        void RestoreClient()
        {
            try
            {
                var r = new RECT { L = 0, T = 0, R = lastClient.Width, B = lastClient.Height };
                if (!AdjustWindowRectExForDpi(ref r, (uint)GetWindowLong(Handle, -16), false, (uint)GetWindowLong(Handle, -20), WindowDpi())) throw new InvalidOperationException();
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, r.R - r.L, r.B - r.T, 0x0002 | 0x0004 | 0x0010);   // no move, no z-order, no activate
            }
            catch (Exception) { ClientSize = lastClient; }
        }
        // called once the window is back at its layout size on the new monitor
        Size savedMin, savedMax;
        protected virtual void AfterDpiChange() { MinimumSize = savedMin; MaximumSize = savedMax; }
        // put the client area back to the layout size using the frame of the monitor the window is on now
        void FixFrame()
        {
            if (lastClient.IsEmpty || !IsHandleCreated || WindowState != FormWindowState.Normal) return;
            savedMin = MinimumSize; savedMax = MaximumSize; MaximumSize = Size.Empty; MinimumSize = Size.Empty;
            RestoreClient(); dpiRec = WindowDpi(); AfterDpiChange();
        }
        // a window first shown on a monitor with different scaling gets a thicker or thinner frame than WinForms planned for
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated && !dpiBusy) BeginInvoke((Action)FixFrame);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                m.Result = IntPtr.Zero;
                // the title bar and borders change size with the monitor, so give the client area back its old size
                if (!dpiBusy && WindowState == FormWindowState.Normal && !lastClient.IsEmpty)
                {
                    dpiBusy = true;
                    BeginInvoke((Action)delegate { try { FixFrame(); } finally { dpiBusy = false; } });
                }
                return;
            }
            base.WndProc(ref m);
        }
    }
}
