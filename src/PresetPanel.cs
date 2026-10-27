using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    // one line in the presets list: a heading, a built-in capture, or a saved preset
    class PresetRow
    {
        public string Key, Name, Sub; public Ic Icon; public Profile P; public bool Header;
    }

    // Snagit-style presets list: every capture type and every saved preset, with its hotkey (or "Add Hotkey") and a gear
    class PresetPanel : Control
    {
        public readonly List<PresetRow> Rows = new List<PresetRow>();
        public Func<PresetRow, string> HotkeyOf = r => "";
        public event Action<PresetRow> HotkeyClicked, GearClicked, Activated;
        int hover = -1, hoverPart, selected = -1, scroll;     // hoverPart: 0 row, 1 hotkey, 2 gear
        int RowH { get { return Dpi.S(42); } }
        int HeadH { get { return Dpi.S(30); } }
        int GearW { get { return Dpi.S(34); } }
        int HotW { get { return Dpi.S(170); } }

        public PresetPanel()
        {
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; TabStop = true; Cursor = Cursors.Default;
            AccessibleName = "Presets and hotkeys"; AccessibleRole = AccessibleRole.List;
        }

        public void SetRows(IEnumerable<PresetRow> rows) { Rows.Clear(); Rows.AddRange(rows); selected = Math.Min(selected, Rows.Count - 1); Clamp(); Invalidate(); }
        int HeightOf(PresetRow r) { return r.Header ? HeadH : RowH; }
        int TopOf(int i) { int y = -scroll; for (int k = 0; k < i; k++) y += HeightOf(Rows[k]); return y; }
        int Total() { int y = 0; foreach (var r in Rows) y += HeightOf(r); return y; }
        void Clamp() { scroll = Math.Max(0, Math.Min(Math.Max(0, Total() - Height), scroll)); }
        int At(Point p) { int y = -scroll; for (int i = 0; i < Rows.Count; i++) { int h = HeightOf(Rows[i]); if (p.Y >= y && p.Y < y + h) return i; y += h; } return -1; }
        Rectangle GearRect(int i) { return new Rectangle(Width - GearW - Dpi.S(8), TopOf(i) + (RowH - Dpi.S(30)) / 2, GearW, Dpi.S(30)); }
        Rectangle HotRect(int i) { return new Rectangle(GearRect(i).X - HotW - Dpi.S(6), TopOf(i) + (RowH - Dpi.S(30)) / 2, HotW, Dpi.S(30)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var nowrap = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
            int y = -scroll;
            for (int i = 0; i < Rows.Count; i++)
            {
                var r = Rows[i]; int h = HeightOf(r);
                if (y + h < 0 || y > Height) { y += h; continue; }
                var band = new Rectangle(0, y, Width, h);
                if (r.Header)
                {
                    using (var f = Theme.BodyBold(9f)) g.DrawString(r.Name, f, new SolidBrush(Theme.Slate), new RectangleF(Dpi.S(14), y, Width - Dpi.S(28), h), new StringFormat { LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    using (var p = new Pen(Theme.Rule)) g.DrawLine(p, Dpi.S(14), y + h - 1, Width - Dpi.S(14), y + h - 1);
                    y += h; continue;
                }
                if (i == hover || i == selected) using (var b = new SolidBrush(i == selected && Focused ? Theme.CobaltTint : Theme.Sheet)) g.FillRectangle(b, band);
                Icons.Draw(g, r.Icon, new RectangleF(Dpi.S(14), y + (h - Dpi.S(22)) / 2f, Dpi.S(22), Dpi.S(22)), Theme.InkSoft);
                int textW = HotRect(i).X - Dpi.S(56) - Dpi.S(8);
                using (var f = Theme.BodyBold(10f)) g.DrawString(r.Name, f, new SolidBrush(Theme.Ink), new RectangleF(Dpi.S(50), y + Dpi.S(3), textW, Dpi.S(20)), nowrap);
                using (var f = Theme.Body(8.5f)) g.DrawString(r.Sub ?? "", f, new SolidBrush(Theme.Slate), new RectangleF(Dpi.S(50), y + Dpi.S(22), textW, Dpi.S(16)), nowrap);

                // hotkey: a keycap when set, "Add Hotkey" when not
                var hr = HotRect(i); string hk = HotkeyOf(r); bool has = !string.IsNullOrEmpty(hk); bool hHot = i == hover && hoverPart == 1;
                if (has)
                {
                    using (var f = Theme.Body(9.5f))
                    {
                        var sz = g.MeasureString(hk, f); int w = Math.Min(hr.Width, (int)sz.Width + Dpi.S(20)); var cap = new Rectangle(hr.Right - w, hr.Y, w, hr.Height);
                        using (var path = Round(cap, Dpi.S(6)))
                        {
                            using (var b = new SolidBrush(hHot ? Theme.CobaltTint : Color.White)) g.FillPath(b, path);
                            using (var p = new Pen(hHot ? Theme.Cobalt : Theme.Rule)) g.DrawPath(p, path);
                        }
                        g.DrawString(hk, f, new SolidBrush(Theme.Ink), cap, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap });
                    }
                }
                else using (var f = Theme.Body(9.5f)) g.DrawString("Add Hotkey", f, new SolidBrush(hHot ? Theme.AccentInk : Theme.Mist), hr, new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
                var gr = GearRect(i); bool gHot = i == hover && hoverPart == 2;
                if (gHot) using (var b = new SolidBrush(Theme.Rule)) g.FillEllipse(b, gr.X + (gr.Width - gr.Height) / 2, gr.Y, gr.Height, gr.Height);
                Icons.Draw(g, Ic.Gear, new RectangleF(gr.X + (gr.Width - Dpi.S(20)) / 2f, gr.Y + (gr.Height - Dpi.S(20)) / 2f, Dpi.S(20), Dpi.S(20)), Theme.Ink);
                using (var p = new Pen(Color.FromArgb(235, 238, 241))) g.DrawLine(p, Dpi.S(14), y + h - 1, Width - Dpi.S(14), y + h - 1);
                y += h;
            }
            if (Focused) using (var p = new Pen(Theme.Cobalt) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = At(e.Location); int part = 0;
            if (i >= 0 && Rows[i].Header) i = -1;
            if (i >= 0) { if (GearRect(i).Contains(e.Location)) part = 2; else if (HotRect(i).Contains(e.Location)) part = 1; }
            Cursor = part > 0 ? Theme.Hand : Cursors.Default;
            if (i != hover || part != hoverPart) { hover = i; hoverPart = part; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; hoverPart = 0; Cursor = Cursors.Default; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus(); int i = At(e.Location); if (i < 0 || Rows[i].Header) return;
            selected = i; Invalidate();
            if (e.Button == MouseButtons.Right) { if (GearClicked != null) GearClicked(Rows[i]); return; }
            if (GearRect(i).Contains(e.Location)) { if (GearClicked != null) GearClicked(Rows[i]); }
            else if (HotRect(i).Contains(e.Location)) { if (HotkeyClicked != null) HotkeyClicked(Rows[i]); }
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = At(e.Location); if (i < 0 || Rows[i].Header || GearRect(i).Contains(e.Location) || HotRect(i).Contains(e.Location)) return;
            if (Activated != null) Activated(Rows[i]);
        }
        protected override void OnMouseWheel(MouseEventArgs e) { scroll -= e.Delta / 2; Clamp(); Invalidate(); }
        protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Up || c == Keys.Down || c == Keys.Enter || c == Keys.F2 || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int dir = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : 0;
            if (dir != 0)
            {
                int i = selected; do { i += dir; } while (i >= 0 && i < Rows.Count && Rows[i].Header);
                if (i >= 0 && i < Rows.Count) { selected = i; int top = TopOf(i); if (top < 0) scroll += top; else if (top + RowH > Height) scroll += top + RowH - Height; Clamp(); Invalidate(); }
                e.Handled = true;
            }
            else if (selected >= 0 && selected < Rows.Count)
            {
                if (e.KeyCode == Keys.Enter && Activated != null) { Activated(Rows[selected]); e.Handled = true; }
                else if ((e.KeyCode == Keys.F2 || e.KeyCode == Keys.Apps) && HotkeyClicked != null) { HotkeyClicked(Rows[selected]); e.Handled = true; }
            }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (selected < 0) selected = Rows.FindIndex(r => !r.Header); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Clamp(); Invalidate(); }
    }

    // "Press the keys you want": captures a Ctrl / Shift / Alt combination (or a function key) for one hotkey
    class HotkeyDialog : DpiForm
    {
        readonly Label keys, note; readonly FlatButton save; readonly Func<string, string> conflict;
        string text;
        public string Result;        // null = cancelled, "" = removed

        HotkeyDialog(string name, string current, Func<string, string> conflict)
        {
            this.conflict = conflict; text = current ?? "";
            Text = "Hotkey"; Icon = AppIcon.Get(); BackColor = Theme.Sheet; ForeColor = Theme.Ink; Font = Theme.Body(9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = Dpi.S(440, 214);
            Controls.Add(new Label { Text = "Press the keys for “" + name + "”", Location = new Point(Dpi.S(18), Dpi.S(16)), Size = Dpi.S(404, 24), Font = Theme.BodyBold(11f) });
            keys = new Label { Location = new Point(Dpi.S(18), Dpi.S(50)), Size = Dpi.S(404, 52), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = Theme.BodyBold(16f), AccessibleName = "New hotkey" };
            note = new Label { Location = new Point(Dpi.S(18), Dpi.S(108)), Size = Dpi.S(404, 40), ForeColor = Theme.Slate };
            save = new FlatButton("Save", true) { Location = new Point(Dpi.S(236), Dpi.S(162)), Size = Dpi.S(88, 36) };
            var remove = new FlatButton("Remove", false) { Location = new Point(Dpi.S(18), Dpi.S(162)), Size = Dpi.S(96, 36) };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(332), Dpi.S(162)), Size = Dpi.S(90, 36), DialogResult = DialogResult.Cancel };
            save.Click += delegate { Result = text; DialogResult = DialogResult.OK; };
            remove.Click += delegate { Result = ""; DialogResult = DialogResult.OK; };
            Controls.AddRange(new Control[] { keys, note, save, remove, cancel }); CancelButton = cancel;
            Refresh2();
        }

        void Refresh2()
        {
            keys.Text = text.Length == 0 ? "Not set" : text; keys.ForeColor = text.Length == 0 ? Theme.Mist : Theme.Ink;
            string clash = text.Length == 0 ? null : conflict(text);
            if (clash != null) { note.Text = text + " is already used by " + clash + ". Press a different combination."; note.ForeColor = Theme.Mark; }
            else { note.Text = "Use Ctrl, Shift or Alt with a key, or a function key on its own. Backspace clears the box."; note.ForeColor = Theme.Slate; }
            save.Enabled = clash == null;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            if (key == Keys.Escape && mods == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.Enter && mods == Keys.None && save.Enabled) return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.Tab && mods == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            if ((key == Keys.Back || key == Keys.Delete) && mods == Keys.None) { text = ""; Refresh2(); return true; }
            bool fkey = (key >= Keys.F1 && key <= Keys.F24) || key == Keys.PrintScreen;
            if (mods == Keys.None && !fkey) { note.Text = "Add Ctrl, Shift or Alt, or choose a function key."; note.ForeColor = Theme.Mark; return true; }
            var parts = new List<string>();
            if ((mods & Keys.Control) != 0) parts.Add("Ctrl"); if ((mods & Keys.Shift) != 0) parts.Add("Shift"); if ((mods & Keys.Alt) != 0) parts.Add("Alt");
            string kn = key.ToString(); if (kn.Length == 2 && kn[0] == 'D' && char.IsDigit(kn[1])) kn = kn.Substring(1);
            parts.Add(kn); string combo = string.Join("+", parts); uint mo, vk;
            if (!Hub.ParseKeys(combo, out mo, out vk)) { note.Text = "That key cannot be used as a hotkey."; note.ForeColor = Theme.Mark; return true; }
            text = combo; Refresh2(); return true;
        }

        // null when cancelled; "" when the hotkey was removed
        public static string Ask(IWin32Window owner, string name, string current, Func<string, string> conflict)
        {
            using (var d = new HotkeyDialog(name, current, conflict)) return d.ShowDialog(owner) == DialogResult.OK ? d.Result : null;
        }
    }
}
