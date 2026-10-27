// A session recorded without a procedure, on another monitor when there is one: the step count (the one big thing), the last step, Pause and Stop.
// Like the recording bar it never takes focus and is kept out of every screenshot.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapLite
{
    class RecordingPanel : Form
    {
        public static int? PreviewSteps; public static string PreviewLast;     // for the pictures the checks take
        readonly FlatButton pause, stop; readonly Timer blink = new Timer { Interval = 600 }; bool on = true;
        public RecordingPanel(Screen screen)
        {
            Text = "Recording"; FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Charcoal; DoubleBuffered = true;
            ClientSize = Dpi.S(360, 376);
            // laid out in the startup scale; Windows stretches it to this monitor's, so the monitor's work area is measured in layout pixels
            var wa = Dpi.WorkAreaV(screen);
            Location = new Point(wa.Right - Width - Dpi.S(12), wa.Y + Dpi.S(12));
            var swap = new FlatButton("Switch window", false) { Location = new Point(Dpi.S(24), Dpi.S(212)), Size = Dpi.S(312, 40), Icon = Ic.Fit, OnDark = true };
            swap.FlatAppearance.BorderColor = Theme.Slate; swap.FlatAppearance.MouseOverBackColor = Theme.Slate; swap.Click += delegate { SessionRecorder.SwitchWindow(); };
            pause = new FlatButton("Pause", false) { Location = new Point(Dpi.S(24), Dpi.S(264)), Size = Dpi.S(150, 40), Icon = Ic.Pause, OnDark = true };
            pause.FlatAppearance.BorderColor = Theme.Slate; pause.FlatAppearance.MouseOverBackColor = Theme.Slate;
            stop = new FlatButton("Stop", true) { Location = new Point(Dpi.S(186), Dpi.S(264)), Size = Dpi.S(150, 40), Icon = Ic.Stop, Tone = Theme.Mark };
            pause.Click += delegate { SessionRecorder.TogglePause(); }; stop.Click += delegate { SessionRecorder.Stop(); };
            var again = new FlatButton("Start again", false) { Location = new Point(Dpi.S(24), Dpi.S(316)), Size = Dpi.S(150, 40), Icon = Ic.Reset, OnDark = true };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(186), Dpi.S(316)), Size = Dpi.S(150, 40), Icon = Ic.Close, OnDark = true };
            again.Click += delegate { SessionRecorder.Restart(); }; cancel.Click += delegate { SessionRecorder.Cancel(); };
            var tip = new ToolTip { ShowAlways = true }; tip.SetToolTip(again, "Throw away the steps so far and start again"); tip.SetToolTip(cancel, "Throw this recording away: nothing is saved");
            Controls.AddRange(new Control[] { swap, pause, stop, again, cancel });
            blink.Tick += delegate { on = !on; Invalidate(new Rectangle(0, 0, Width, Dpi.S(56))); };
            Shown += delegate { if (Environment.GetEnvironmentVariable("XLC_SHOW_STATUS") == null) Native.ExcludeFromCapture(Handle); blink.Start(); Refresh2(); };      // the checks photograph it, so they ask for it to be visible
            FormClosed += delegate { blink.Dispose(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }
        public void Refresh2() { pause.Text = SessionRecorder.Paused ? "Resume" : "Pause"; pause.Icon = SessionRecorder.Paused ? Ic.Record : Ic.Pause; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int steps = PreviewSteps ?? SessionRecorder.Count; string last = PreviewLast ?? SessionRecorder.LastStep; bool paused = SessionRecorder.Paused;
            // the state, with a dot that breathes while recording
            var dot = new RectangleF(Dpi.S(24), Dpi.S(26), Dpi.F(12), Dpi.F(12));
            using (var b = new SolidBrush(paused ? Theme.Mist : on ? Theme.Mark : Color.FromArgb(110, Theme.Mark))) g.FillEllipse(b, dot);
            using (var f = Theme.Body(10.5f)) TextRenderer.DrawText(g, paused ? "Paused" : "Recording", f, new Rectangle(Dpi.S(44), Dpi.S(20), Dpi.S(200), Dpi.S(24)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            // the step count
            string n = steps.ToString();
            using (var f = new Font(Theme.LabelFamily, Dpi.F(66f), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var sz = TextRenderer.MeasureText(g, n, f, new Size(2000, 200), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, n, f, new Point(Dpi.S(20), Dpi.S(52)), Theme.Accent, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                using (var f2 = Theme.Body(12f)) TextRenderer.DrawText(g, steps == 1 ? "step" : "steps", f2, new Point(Dpi.S(20) + sz.Width + Dpi.S(10), Dpi.S(52) + sz.Height - Dpi.S(34)), Theme.Mist, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
            // what was recorded last
            using (var p = new Pen(Theme.InkSoft)) g.DrawLine(p, Dpi.S(24), Dpi.S(150), Width - Dpi.S(24) - 16, Dpi.S(150));
            using (var f = Theme.Body(9.5f)) TextRenderer.DrawText(g, "Last step", f, new Point(Dpi.S(24), Dpi.S(160)), Theme.Slate, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            using (var f = Theme.Body(10.5f)) TextRenderer.DrawText(g, string.IsNullOrEmpty(last) ? "Nothing yet. Click in the window you are recording." : last, f, new Rectangle(Dpi.S(24), Dpi.S(180), ClientSize.Width - Dpi.S(48), Dpi.S(22)), string.IsNullOrEmpty(last) ? Theme.Mist : Color.White, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }
}
