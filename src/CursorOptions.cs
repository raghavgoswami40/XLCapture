// The Cursor options of a video recording: highlight the pointer, and show a ring on every click, each with its own colour.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapLite
{
    class CursorOptionsForm : Form
    {
        readonly ThemedCheck hl, click; readonly ColorSwatch hlColor, clickColor; readonly Panel preview;

        public CursorOptionsForm()
        {
            Text = "Cursor"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White; Font = Theme.Body(10f); Icon = AppIcon.Get(); ClientSize = Dpi.S(380, 346);
            int m = Dpi.S(24), w = Dpi.S(380) - 2 * m;
            hl = new ThemedCheck { Text = "Highlight cursor", Checked = Settings.VidHighlight, Width = Dpi.S(220), Location = new Point(m, Dpi.S(24)), Font = Theme.Semi(10.5f), ForeColor = Theme.Ink };
            hlColor = new ColorSwatch(Settings.VidHighlightColor) { Title = "Highlight colour", Location = new Point(m + w - Dpi.S(64), Dpi.S(22)), AccessibleName = "Highlight colour" };
            click = new ThemedCheck { Text = "Click animation", Checked = Settings.VidClick, Width = Dpi.S(220), Location = new Point(m, Dpi.S(70)), Font = Theme.Semi(10.5f), ForeColor = Theme.Ink };
            clickColor = new ColorSwatch(Settings.VidClickColor) { Title = "Click animation colour", Location = new Point(m + w - Dpi.S(64), Dpi.S(68)), AccessibleName = "Click animation colour" };
            var note = new Label { Text = "The highlight is a soft disc under the pointer. The click animation is a ring that opens out from the pointer each time you click.", AutoSize = false, Location = new Point(m, Dpi.S(112)), Size = new Size(w, Dpi.S(56)), ForeColor = Theme.Slate, Font = Theme.Body(9f) };
            preview = new Panel { Location = new Point(m, Dpi.S(176)), Size = new Size(w, Dpi.S(100)), BackColor = Color.White };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(preview, true, null);
            preview.Paint += PaintPreview;
            EventHandler redraw = delegate { preview.Invalidate(); };
            hl.CheckedChanged += redraw; click.CheckedChanged += redraw; hlColor.ColorChanged += redraw; clickColor.ColorChanged += redraw;
            var ok = new FlatButton("Done", true) { Location = new Point(Dpi.S(380) - m - Dpi.S(96), Dpi.S(294)), Size = Dpi.S(96, 38), DialogResult = DialogResult.OK };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(380) - m - Dpi.S(96) - Dpi.S(8) - Dpi.S(88), Dpi.S(294)), Size = Dpi.S(88, 38), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { hl, hlColor, click, clickColor, note, preview, cancel, ok }); AcceptButton = ok; CancelButton = cancel;
        }

        public void Apply()
        {
            Settings.VidHighlight = hl.Checked; Settings.VidHighlightColor = hlColor.Color; Settings.VidClick = click.Checked; Settings.VidClickColor = clickColor.Color; Settings.Save();
        }

        // a small stand-in for the screen with the pointer on it, as it will look in the recording
        void PaintPreview(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var r = new Rectangle(0, 0, preview.Width - 1, preview.Height - 1);
            using (var p = Theme.RoundRect(r, Dpi.S(8)))
            {
                g.SetClip(p);
                using (var b = new LinearGradientBrush(r, Color.FromArgb(0xF4, 0xF6, 0xF9), Color.FromArgb(0xDD, 0xE3, 0xEA), 90f)) g.FillRectangle(b, r);
                using (var b = new SolidBrush(Color.FromArgb(0xFF, 0xFF, 0xFF))) g.FillRectangle(b, Dpi.S(14), Dpi.S(14), Dpi.S(120), Dpi.S(10));
                using (var b = new SolidBrush(Color.FromArgb(0xC9, 0xD1, 0xDB))) { g.FillRectangle(b, Dpi.S(14), Dpi.S(32), Dpi.S(180), Dpi.S(8)); g.FillRectangle(b, Dpi.S(14), Dpi.S(46), Dpi.S(150), Dpi.S(8)); }
                int cx = preview.Width / 2 + Dpi.S(30), cy = preview.Height / 2;
                if (hl.Checked) { int rr = Dpi.S(24); using (var b = new SolidBrush(Color.FromArgb(120, hlColor.Color))) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2); }
                if (click.Checked)
                {
                    float rr = Dpi.F(26f);
                    using (var b = new SolidBrush(Color.FromArgb(40, clickColor.Color))) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
                    using (var pen = new Pen(Color.FromArgb(200, clickColor.Color), Dpi.F(3f))) g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                }
                var arrow = new[] { new PointF(0, 0), new PointF(0, 17), new PointF(4.5f, 13), new PointF(7.5f, 20), new PointF(10, 19), new PointF(7, 12), new PointF(13, 12) };
                for (int i = 0; i < arrow.Length; i++) arrow[i] = new PointF(cx + arrow[i].X * Dpi.F(1.2f), cy + arrow[i].Y * Dpi.F(1.2f));
                g.FillPolygon(Brushes.Black, arrow); using (var pen = new Pen(Color.White, 1.4f) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, arrow);
                g.ResetClip();
                using (var pen = new Pen(Theme.Rule)) g.DrawPath(pen, p);
            }
        }
    }
}
