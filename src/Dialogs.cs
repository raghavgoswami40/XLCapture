// Small themed dialogs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SnapLite
{
    // The app's one button: a capsule in the accent colour. The main action is filled, every other button is the same
    // colour as an outline. Optional icon on the left; with no text it is a round icon button.
    class FlatButton : Button
    {
        public bool Primary, OnDark, Bare, Quiet, Chevron, ChevronUp; public Ic? Icon; public Color? Tone; bool hover, down;     // Tone: a filled button in another colour; Quiet: no edge until the pointer is on it (toolbar-style); Chevron: a small arrow after the text, for a button that opens a menu
        public FlatButton(string text, bool primary)
        {
            Text = text; Primary = primary; FlatStyle = FlatStyle.Flat; Cursor = Theme.Hand; UseMnemonic = false;
            Font = Theme.Semi(9.5f); Height = Dpi.S(36); UseVisualStyleBackColor = false; TabStop = true;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        int IconSize { get { return Dpi.S(18); } }
        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = Dpi.S(36);
            if (Text.Length > 0) w = TextRenderer.MeasureText(Text, Font, new Size(10000, 100), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Dpi.S(36) + (Icon.HasValue ? IconSize + Dpi.S(8) : 0) + (Chevron ? Dpi.S(18) : 0);
            return new Size(Math.Max(w, Dpi.S(36)), Math.Max(Height, Dpi.S(36)));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Theme.Paper, ClientRectangle);
            if (Bare)       // just the icon, in its colour, with a faint round halo under the pointer (the reset button)
            {
                Color c = Enabled ? (Tone ?? Theme.Charcoal) : Theme.Mist;
                if (hover || down) using (var b = new SolidBrush(down ? Theme.Press : Theme.Hover)) using (var p = Theme.RoundRect(new RectangleF(1, 1, Width - 3, Height - 3), Dpi.F(Theme.Radius))) g.FillPath(b, p);
                if (Icon.HasValue) { int s = (int)(Math.Min(Width, Height) * 0.62f); Icons.Draw(g, Icon.Value, new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s), c); }
                if (Focused && ShowFocusCues) using (var p = Theme.RoundRect(new RectangleF(1, 1, Width - 3, Height - 3), Dpi.F(Theme.Radius))) using (var pen = new Pen(Theme.Accent, Dpi.F(1.5f))) g.DrawPath(pen, p);
                return;
            }
            // one shape for every button: a rectangle with the controls' corner, a 1px edge, and the accent only on the main action
            float pw = 1f, ins = 1f;        // the whole outline sits inside the control, so no part of it reaches the first row or column
            var r = new RectangleF(ins, ins, Width - 2 * ins - 1f, Height - 2 * ins - 1f);
            Color fill, line, ink;
            if (!Enabled) { fill = Primary ? Theme.Rule : OnDark || Quiet ? Color.Transparent : Theme.Sheet; line = OnDark ? Theme.Slate : Primary || Quiet ? Color.Transparent : Theme.Rule; ink = OnDark ? Theme.Slate : Theme.Mist; }
            else if (Primary)
            {
                Color tone = Tone ?? Theme.Accent;
                fill = down ? (Tone.HasValue ? Theme.Mix(tone, Color.Black, 0.3f) : Theme.AccentDeep) : hover ? (Tone.HasValue ? Theme.Mix(tone, Color.Black, 0.12f) : Theme.AccentDark) : tone;
                line = fill; ink = Color.White; if (OnDark) line = Color.FromArgb(230, 255, 255, 255);
            }
            else if (OnDark) { fill = down ? Color.FromArgb(70, 255, 255, 255) : hover ? Color.FromArgb(40, 255, 255, 255) : Color.Transparent; line = Color.FromArgb(150, 255, 255, 255); ink = Color.White; }
            else if (Quiet) { fill = down ? Theme.Press : hover ? Theme.Hover : Color.Transparent; line = Color.Transparent; ink = Theme.InkSoft; }
            else { fill = down ? Theme.Press : hover ? Theme.Hover : Color.White; line = hover || down ? Theme.Mist : Theme.Line; ink = Theme.Ink; }
            float rad = Dpi.F(Theme.Radius);
            using (var p = Theme.RoundRect(r, rad))
            {
                if (fill != Color.Transparent) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (line != Color.Transparent) using (var pen = new Pen(line, pw)) g.DrawPath(pen, p);
            }
            if (Focused && ShowFocusCues) using (var p = Theme.RoundRect(new RectangleF(ins + 1.5f, ins + 1.5f, r.Width - 3f, r.Height - 3f), Math.Max(1f, rad - 1.5f))) using (var pen = new Pen(OnDark || Primary ? Color.White : Theme.Accent, Dpi.F(1.5f))) g.DrawPath(pen, p);
            int tw = Text.Length == 0 ? 0 : TextRenderer.MeasureText(g, Text, Font, new Size(10000, 100), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int cw = Chevron ? Dpi.S(18) : 0;
            if (Chevron)        // a small down arrow on the right
            {
                float cx = Width - Dpi.S(20), cy = Height / 2f, a = Dpi.F(4f);
                using (var pen = new Pen(ink, Dpi.F(1.6f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                    g.DrawLines(pen, ChevronUp ? new[] { new PointF(cx - a, cy + a / 2f), new PointF(cx, cy - a / 2f), new PointF(cx + a, cy + a / 2f) } : new[] { new PointF(cx - a, cy - a / 2f), new PointF(cx, cy + a / 2f), new PointF(cx + a, cy - a / 2f) });
            }
            int iw = Icon.HasValue ? IconSize : 0, gap = (Icon.HasValue && tw > 0) ? Dpi.S(8) : 0, total = iw + gap + tw, x = (Width - cw - total) / 2;
            if (Icon.HasValue) { Icons.Draw(g, Icon.Value, new RectangleF(x, (Height - IconSize) / 2f, IconSize, IconSize), ink); x += iw + gap; }
            if (tw > 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Math.Max(10, Width - cw - x - Dpi.S(8)), Height), ink, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // A drop-down list: a white box with a 1px edge and a chevron on the right; the edge darkens under the pointer and turns
    // the accent with focus or while open. The list itself is drawn by Theme.Combo.
    class ThemedCombo : ComboBox
    {
        bool hover;
        public ThemedCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList; Theme.Combo(this); Font = Theme.Body(9.5f); Cursor = Theme.Hand; IntegralHeight = false;      // the row height: see Theme.Combo
        }
        public int RowHeight = Dpi.S(26);
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnDropDown(EventArgs e) { base.OnDropDown(e); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F || m.Msg == 0x0014) using (var g = Graphics.FromHwnd(Handle)) PaintBox(g);    // WM_PAINT / WM_ERASEBKGND: draw our own box over the system one
        }
        void PaintBox(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            bool open = DroppedDown, focus = Focused || open;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Color.White, 0, 0, Width, Height);
            float pw = focus ? Dpi.F(1.5f) : 1f, ins = pw / 2f + 0.5f; var r = new RectangleF(ins, ins, Width - 2 * ins - 1f, Height - 2 * ins - 1f); float rad = Dpi.F(Theme.Radius);
            Color frame = !Enabled ? Theme.Rule : focus ? Theme.Accent : hover ? Theme.Mist : Theme.Line;
            using (var p = Theme.RoundRect(r, rad)) { using (var b = new SolidBrush(Enabled ? Color.White : Theme.Sheet)) g.FillPath(b, p); using (var pen = new Pen(frame, pw)) g.DrawPath(pen, p); }
            int tab = Dpi.S(28);
            float cx = r.Right - Dpi.F(16), cy = Height / 2f, d = Dpi.F(4f);
            using (var pen = new Pen(!Enabled ? Theme.Mist : open ? Theme.Accent : Theme.Slate, Dpi.F(1.6f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(pen, open ? new[] { new PointF(cx - d, cy + d * 0.45f), new PointF(cx, cy - d * 0.55f), new PointF(cx + d, cy + d * 0.45f) } : new[] { new PointF(cx - d, cy - d * 0.45f), new PointF(cx, cy + d * 0.55f), new PointF(cx + d, cy - d * 0.45f) });
            string t = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : "";
            TextRenderer.DrawText(g, t, Font, new Rectangle(Dpi.S(12), 0, Width - tab - Dpi.S(26), Height), Enabled ? Theme.Ink : Theme.Mist, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    // a check box and a radio button that draw their mark in the accent colour (the system ones use the Windows accent)
    class ThemedCheck : CheckBox
    {
        bool hover;
        public ThemedCheck() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Theme.Hand; AutoSize = false; Height = Dpi.S(26); Font = Theme.Body(9.5f); ForeColor = Theme.InkSoft; }
        public override Size GetPreferredSize(Size s) { return new Size(TextRenderer.MeasureText(Text, Font).Width + Dpi.S(34), Dpi.S(26)); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Color.White, ClientRectangle);
            int s = Dpi.S(18); var box = new RectangleF(1.5f, (Height - s) / 2f, s, s);
            using (var p = Theme.RoundRect(box, Dpi.S(5)))
            {
                if (Checked) using (var b = new SolidBrush(!Enabled ? Theme.Rule : hover ? Theme.AccentDark : Theme.Accent)) g.FillPath(b, p);
                else { using (var b = new SolidBrush(hover ? Theme.Hover : Color.White)) g.FillPath(b, p); using (var pen = new Pen(!Enabled ? Theme.Rule : hover ? Theme.Slate : Theme.Mist, Dpi.F(1.2f))) g.DrawPath(pen, p); }
            }
            if (Checked) using (var pen = new Pen(Color.White, Dpi.F(2f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(box.X + s * 0.25f, box.Y + s * 0.52f), new PointF(box.X + s * 0.43f, box.Y + s * 0.70f), new PointF(box.X + s * 0.75f, box.Y + s * 0.30f) });
            if (Focused && ShowFocusCues) using (var p = Theme.RoundRect(new RectangleF(box.X - 1f, box.Y - 2f, s + 3f, s + 4f), Dpi.S(6))) using (var pen = new Pen(Theme.Accent, Dpi.F(1.5f))) g.DrawPath(pen, p);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(s + Dpi.S(10), 0, Width - s - Dpi.S(10), Height), Enabled ? ForeColor : Theme.Mist, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
    class ThemedRadio : RadioButton
    {
        bool hover;
        public ThemedRadio() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Theme.Hand; AutoSize = false; Height = Dpi.S(28); Font = Theme.Semi(10f); ForeColor = Theme.Ink; }
        public override Size GetPreferredSize(Size s) { return new Size(TextRenderer.MeasureText(Text, Font).Width + Dpi.S(36), Dpi.S(28)); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Color.White, ClientRectangle);
            int s = Dpi.S(20); var box = new RectangleF(1.5f, (Height - s) / 2f, s, s);
            using (var b = new SolidBrush(hover && !Checked ? Theme.Hover : Color.White)) g.FillEllipse(b, box);
            using (var pen = new Pen(Checked ? Theme.Accent : hover ? Theme.Slate : Theme.Mist, Checked ? Dpi.F(1.8f) : Dpi.F(1.2f))) g.DrawEllipse(pen, box);
            if (Checked) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, box.X + s * 0.27f, box.Y + s * 0.27f, s * 0.46f, s * 0.46f);
            if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Accent, Dpi.F(1.5f))) g.DrawEllipse(pen, box.X - 1f, box.Y - 2f, s + 3f, s + 4f);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(s + Dpi.S(10), 0, Width - s - Dpi.S(10), Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    static class Dialogs
    {
        // "Move to the Recycle Bin?" with a box to stop asking. Returns true to go ahead. Ticking the box only counts when you confirm.
        public static bool ConfirmDelete(IWin32Window owner, string title, string text, string okText)
        {
            if (!Settings.AskDelete) return true;
            using (var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.White, Font = Theme.Body(10f), Icon = AppIcon.Get(), ClientSize = Dpi.S(460, 214)
            })
            {
                var q = new Label { Text = text, AutoSize = false, Location = new Point(Dpi.S(28), Dpi.S(26)), Size = Dpi.S(404, 72), ForeColor = Theme.Ink, Font = Theme.Semi(11f) };
                var again = new ThemedCheck { Text = "Don't ask me again", Width = Dpi.S(300), Location = new Point(Dpi.S(28), Dpi.S(108)) };
                var hint = new Label { Text = "You can turn this back on in Preferences.", AutoSize = false, Location = new Point(Dpi.S(52), Dpi.S(134)), Size = Dpi.S(380, 22), ForeColor = Theme.Slate, Font = Theme.Body(9f) };
                var ok = new FlatButton(okText, true) { Location = new Point(Dpi.S(190), Dpi.S(164)), Size = Dpi.S(150, 38), DialogResult = DialogResult.OK };
                var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(348), Dpi.S(164)), Size = Dpi.S(88, 38), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { q, again, hint, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                if (again.Checked) { Settings.AskDelete = false; Settings.Save(); }
                return true;
            }
        }

        // "Are you sure?" for something that throws work away (a recording being cancelled or started again). Always on top, so it shows over the app being recorded.
        // Enter keeps what there is: the safe answer is the default. Returns true to go ahead.
        public static bool ConfirmAction(IWin32Window owner, string title, string text, string okText, string keepText)
        {
            using (var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, TopMost = true,
                StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent, BackColor = Color.White, Font = Theme.Body(10f), Icon = AppIcon.Get(), ClientSize = Dpi.S(460, 176)
            })
            {
                var q = new Label { Text = text, AutoSize = false, Location = new Point(Dpi.S(28), Dpi.S(26)), Size = Dpi.S(404, 92), ForeColor = Theme.Ink, Font = Theme.Body(10.5f) };
                var ok = new FlatButton(okText, true) { Location = new Point(Dpi.S(28), Dpi.S(126)), Size = Dpi.S(200, 38), DialogResult = DialogResult.OK, Tone = Theme.Mark };
                var keep = new FlatButton(keepText, false) { Location = new Point(Dpi.S(236), Dpi.S(126)), Size = Dpi.S(196, 38), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { q, ok, keep }); f.AcceptButton = keep; f.CancelButton = keep;
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }

        // a shared session clicks and types in the viewer's apps when it runs: ask first. Returns true to go ahead.
        public static bool ConfirmSharedRun(IWin32Window owner, string path)
        {
            if (path == null || !path.EndsWith(SessionDoc.ShareExt, StringComparison.OrdinalIgnoreCase)) return true;
            return MessageBox.Show(owner, "This session was shared with you. When it runs, XLCapture clicks and types in your own apps by itself.\r\n\r\nRun it only if you know who made it and what it does.\r\n\r\nRun it?",
                "Run a shared session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public static Form Base(string title, int w, int h)
        {
            var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = Dpi.S(w, h),
                BackColor = Theme.Sheet, ForeColor = Theme.Ink, Font = Theme.Body(9.5f), Icon = AppIcon.Get()
            };
            return f;
        }

        public static string Prompt(IWin32Window owner, string title, string initial)
        {
            using (var f = Base(title, 480, 220))
            {
                var tb = new TextBox { Multiline = true, AcceptsReturn = true, Text = initial, Location = new Point(Dpi.S(20), Dpi.S(20)), Size = Dpi.S(440, 118), Font = Theme.Body(11f), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
                var ok = new FlatButton("OK", true) { DialogResult = DialogResult.OK, Location = new Point(Dpi.S(284), Dpi.S(160)), Size = Dpi.S(84, 38) };
                var cancel = new FlatButton("Cancel", false) { DialogResult = DialogResult.Cancel, Location = new Point(Dpi.S(376), Dpi.S(160)), Size = Dpi.S(84, 38) };
                var hint = new Label { Text = "Enter adds a line. Ctrl+Enter confirms.", AutoSize = false, Location = new Point(Dpi.S(20), Dpi.S(166)), Size = Dpi.S(254, 24), ForeColor = Theme.Slate, Font = Theme.Body(9f) };
                f.Controls.AddRange(new Control[] { tb, ok, cancel, hint });
                f.CancelButton = cancel;
                tb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.OK; f.Close(); e.SuppressKeyPress = true; } };
                f.Shown += delegate { tb.Focus(); tb.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
            }
        }

        // generic labelled-field dialog; returns null on cancel
        public class Field
        {
            public string Label; public string Kind;       // "int", "text", "check", "color", "choice"
            public object Value; public string[] Choices; public int Min = 0, Max = 100000;
        }
        public static Dictionary<string, object> Form(IWin32Window owner, string title, string okText, params Field[] fields)
        {
            int rowH = 40, h = 28 + fields.Length * rowH + 72;
            using (var f = Base(title, 420, h))
            {
                var ctrls = new List<Control>(); int y = 20;
                foreach (var fd in fields)
                {
                    var lb = new Label { Text = fd.Label, Location = new Point(Dpi.S(20), Dpi.S(y + 5)), Size = Dpi.S(140, 22), ForeColor = Theme.Slate };
                    Control c;
                    if (fd.Kind == "int") c = new NumericUpDown { Minimum = fd.Min, Maximum = fd.Max, Value = Math.Max(fd.Min, Math.Min(fd.Max, Convert.ToInt32(fd.Value))), Width = Dpi.S(210) };
                    else if (fd.Kind == "check") c = new ThemedCheck { Checked = (bool)fd.Value, Width = Dpi.S(210), Text = "" };
                    else if (fd.Kind == "choice") { var cb = new ThemedCombo { Width = Dpi.S(210) }; cb.Items.AddRange(fd.Choices); cb.SelectedIndex = Math.Max(0, Array.IndexOf(fd.Choices, (string)fd.Value)); c = cb; }
                    else if (fd.Kind == "color") { var b = new Button { Width = Dpi.S(210), Height = Dpi.S(28), FlatStyle = FlatStyle.Flat, BackColor = (Color)fd.Value, Tag = fd.Value }; b.Click += delegate { Color pc; if (ColorPickerDialog.Pick(f, (Color)b.Tag, out pc)) { b.Tag = pc; b.BackColor = pc; } }; c = b; }
                    else c = new TextBox { Text = Convert.ToString(fd.Value), Width = Dpi.S(210), UseSystemPasswordChar = fd.Kind == "password" };
                    c.Location = new Point(Dpi.S(176), Dpi.S(y)); c.Tag = c.Tag ?? fd.Kind;
                    if (fd.Kind == "color") c.Tag = ((Button)c).Tag;
                    f.Controls.Add(lb); f.Controls.Add(c); ctrls.Add(c); y += rowH;
                }
                var ok = new FlatButton(okText, true) { DialogResult = DialogResult.OK, Location = new Point(Dpi.S(420 - 20 - 84 - 8 - 100), Dpi.S(h - 56)), Size = Dpi.S(100, 38) };
                var cancel = new FlatButton("Cancel", false) { DialogResult = DialogResult.Cancel, Location = new Point(Dpi.S(420 - 20 - 84), Dpi.S(h - 56)), Size = Dpi.S(84, 38) };
                f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                var res = new Dictionary<string, object>();
                for (int i = 0; i < fields.Length; i++)
                {
                    var c = ctrls[i]; object v;
                    if (c is NumericUpDown) v = (int)((NumericUpDown)c).Value; else if (c is CheckBox) v = ((CheckBox)c).Checked;
                    else if (c is ComboBox) v = ((ComboBox)c).SelectedItem; else if (c is Button) v = (Color)c.Tag; else v = c.Text;
                    res[fields[i].Label] = v;
                }
                return res;
            }
        }
    }
}

