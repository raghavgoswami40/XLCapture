// The procedure as a checklist, on another monitor while a session is recorded. One task is one page: its title, a strip of pages (one segment per task, a click
// opens that page), and the task's actions with a box to tick by hand. Next task and Previous task turn the page. The step count, Pause and Stop are here too.
// Nothing is ticked by a click that is recorded. Like the recording bar it never takes focus and is kept out of every screenshot.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    class ChecklistForm : Form
    {
        class Row { public ProcAction Action; public int Index; }
        readonly ProcActivity act; readonly List<ProcTask> tasks; readonly ListBox list; readonly Panel header, pager; readonly FlatButton prev, next, pause, stop;
        readonly List<Rectangle> segments = new List<Rectangle>(); readonly List<Row> rows = new List<Row>(); int shown = -1;
        public static int? PreviewSteps;     // for the pictures the checks take

        public ChecklistForm(ProcActivity act, Screen screen)
        {
            this.act = act; Text = "Checklist  " + act.Title; FormBorderStyle = FormBorderStyle.SizableToolWindow; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Sheet; Font = Theme.Body(10f);
            // laid out in the startup scale; Windows stretches it to this monitor's, so the monitor's work area is measured in layout pixels
            var wa = Dpi.WorkAreaV(screen);
            int w = Math.Min(Dpi.S(520), Math.Max(Dpi.S(380), wa.Width / 3));
            Bounds = new Rectangle(wa.Right - w - Dpi.S(12), wa.Y + Dpi.S(12), w, wa.Height - Dpi.S(24));
            tasks = SessionRecorder.Tasks ?? act.Tasks.Where(t => t.Actions.Count > 0).ToList();

            header = new Panel { Dock = DockStyle.Top, Height = Dpi.S(92), BackColor = Theme.Charcoal }; EnableDoubleBuffer(header); header.Paint += PaintHeader;
            pager = new Panel { Dock = DockStyle.Top, Height = Dpi.S(150), BackColor = Theme.Sheet }; EnableDoubleBuffer(pager); pager.Paint += PaintPager; pager.MouseDown += PagerClicked;
            list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawVariable, BackColor = Color.White, IntegralHeight = false, AccessibleName = "Actions of this task" };
            list.MeasureItem += Measure; list.DrawItem += Draw; list.MouseDown += Clicked;
            var foot = new Panel { Dock = DockStyle.Bottom, Height = Dpi.S(248), BackColor = Theme.Sheet };
            foot.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, 0, foot.Width, 0); };
            prev = new FlatButton("Previous task", false) { Location = new Point(Dpi.S(16), Dpi.S(14)), Size = Dpi.S(172, 40), Icon = Ic.Prev };
            next = new FlatButton("Next task", true) { Location = new Point(Dpi.S(198), Dpi.S(14)), Size = Dpi.S(172, 40), Icon = Ic.Chevron };
            pause = new FlatButton("Pause", false) { Location = new Point(Dpi.S(16), Dpi.S(72)), Size = Dpi.S(172, 40), Icon = Ic.Pause };
            stop = new FlatButton("Stop", true) { Location = new Point(Dpi.S(198), Dpi.S(72)), Size = Dpi.S(172, 40), Icon = Ic.Stop, Tone = Theme.Mark };
            prev.Click += delegate { SessionRecorder.MoveTask(-1); }; next.Click += delegate { SessionRecorder.MoveTask(1); };
            pause.Click += delegate { SessionRecorder.TogglePause(); }; stop.Click += delegate { SessionRecorder.Stop(); };
            var swap = new FlatButton("Switch window", false) { Location = new Point(Dpi.S(16), Dpi.S(130)), Size = Dpi.S(354, 40), Icon = Ic.Fit };
            swap.Click += delegate { SessionRecorder.SwitchWindow(); };
            var again = new FlatButton("Start again", false) { Location = new Point(Dpi.S(16), Dpi.S(188)), Size = Dpi.S(172, 40), Icon = Ic.Reset };
            var cancel = new FlatButton("Cancel recording", false) { Location = new Point(Dpi.S(198), Dpi.S(188)), Size = Dpi.S(172, 40), Icon = Ic.Close };
            again.Click += delegate { SessionRecorder.Restart(); }; cancel.Click += delegate { SessionRecorder.Cancel(); };
            var tip = new ToolTip { ShowAlways = true }; tip.SetToolTip(again, "Throw away the steps so far and start again (the checklist is unticked)"); tip.SetToolTip(cancel, "Throw this recording away: nothing is saved");
            foot.Controls.AddRange(new Control[] { prev, next, pause, stop, swap, again, cancel });
            // the two buttons of each row share the width
            Action layoutFoot = delegate { int m = Dpi.S(16), gap = Dpi.S(10), bw = Math.Max(Dpi.S(100), (foot.Width - 2 * m - gap) / 2); prev.Width = pause.Width = again.Width = bw; next.Width = stop.Width = cancel.Width = bw; prev.Left = pause.Left = again.Left = m; next.Left = stop.Left = cancel.Left = m + bw + gap; swap.Left = m; swap.Width = foot.Width - 2 * m; };
            foot.Resize += delegate { layoutFoot(); }; layoutFoot();
            Controls.Add(list); Controls.Add(foot); Controls.Add(pager); Controls.Add(header);
            Rebuild(SessionRecorder.TaskPos);
            Shown += delegate { if (Environment.GetEnvironmentVariable("XLC_SHOW_STATUS") == null) Native.ExcludeFromCapture(Handle); Refresh2(); };      // the checks photograph it, so they ask for it to be visible
        }
        static void EnableDoubleBuffer(Control c) { typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(c, true, null); }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }

        int Pos { get { return Math.Max(0, Math.Min(tasks.Count - 1, SessionRecorder.TaskPos)); } }
        int FirstIndexOf(int task) { int n = 0; for (int i = 0; i < task; i++) n += tasks[i].Actions.Count; return n; }
        int Ticked(int task) { int first = FirstIndexOf(task), c = 0; for (int k = 0; k < tasks[task].Actions.Count; k++) if (SessionRecorder.IsDone(first + k)) c++; return c; }

        // the page: the open task's actions
        void Rebuild(int task)
        {
            shown = task; rows.Clear(); list.Items.Clear(); if (task < 0 || task >= tasks.Count) return;
            int first = FirstIndexOf(task);
            for (int k = 0; k < tasks[task].Actions.Count; k++) rows.Add(new Row { Action = tasks[task].Actions[k], Index = first + k });
            foreach (var r in rows) list.Items.Add(r);
        }

        // ---- the header: the activity, whether it is recording, and the step count
        void PaintHeader(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; int W = header.Width;
            int steps = PreviewSteps ?? SessionRecorder.Count; bool paused = SessionRecorder.Paused;
            using (var f = Theme.Semi(12f)) TextRenderer.DrawText(g, act.Title, f, new Rectangle(Dpi.S(18), Dpi.S(16), W - Dpi.S(150), Dpi.S(28)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (var b = new SolidBrush(paused ? Theme.Mist : Theme.Mark)) g.FillEllipse(b, Dpi.F(18), Dpi.F(56), Dpi.F(10), Dpi.F(10));
            using (var f = Theme.Body(9.5f)) TextRenderer.DrawText(g, paused ? "Paused" : "Recording", f, new Rectangle(Dpi.S(36), Dpi.S(52), Dpi.S(160), Dpi.S(20)), Theme.Mist, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            // the step count, the one number that matters while recording
            using (var f = new Font(Theme.LabelFamily, Dpi.F(40f), FontStyle.Bold, GraphicsUnit.Pixel)) TextRenderer.DrawText(g, steps.ToString(), f, new Rectangle(W - Dpi.S(150), Dpi.S(12), Dpi.S(132), Dpi.S(50)), Theme.Accent, TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.SingleLine);
            using (var f = Theme.Body(9.5f)) TextRenderer.DrawText(g, steps == 1 ? "step" : "steps", f, new Rectangle(W - Dpi.S(150), Dpi.S(62), Dpi.S(132), Dpi.S(20)), Theme.Mist, TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        // ---- the strip of pages and the open task's title
        void PaintPager(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; int W = pager.Width; int pos = Pos;
            var t = tasks[pos];
            using (var f = Theme.Body(9.5f)) TextRenderer.DrawText(g, "Task " + (pos + 1) + " of " + tasks.Count, f, new Rectangle(Dpi.S(18), Dpi.S(14), Dpi.S(200), Dpi.S(20)), Theme.Slate, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int total = t.Actions.Count, ticked = Ticked(pos);
            using (var f = Theme.Semi(9.5f)) TextRenderer.DrawText(g, ticked + " of " + total + " ticked", f, new Rectangle(W - Dpi.S(190), Dpi.S(14), Dpi.S(172), Dpi.S(20)), ticked == total && total > 0 ? Theme.AccentInk : Theme.Slate, TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.SingleLine);
            // one segment per task: the open one gold, a finished one charcoal, the rest pale
            segments.Clear(); int n = tasks.Count, gap = Dpi.S(6), x0 = Dpi.S(18), avail = W - 2 * x0, sw = Math.Max(Dpi.S(10), Math.Min(Dpi.S(64), (avail - gap * (n - 1)) / n)), y = Dpi.S(44), h = Dpi.S(8);
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle(x0 + i * (sw + gap), y, sw, h); segments.Add(r);
                bool open = i == pos, finished = tasks[i].Actions.Count > 0 && Ticked(i) == tasks[i].Actions.Count;
                using (var p = Theme.RoundRect(r, h / 2f)) using (var b = new SolidBrush(open ? Theme.Accent : finished ? Theme.Charcoal : Theme.Rule)) g.FillPath(b, p);
            }
            using (var f = Theme.Semi(14f)) TextRenderer.DrawText(g, t.Full, f, new Rectangle(Dpi.S(18), Dpi.S(70), W - Dpi.S(36), Dpi.S(66)), Theme.Ink, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Theme.Rule)) g.DrawLine(p, 0, pager.Height - 1, W, pager.Height - 1);
        }
        void PagerClicked(object s, MouseEventArgs e)
        {
            for (int i = 0; i < segments.Count; i++) { var r = segments[i]; r.Inflate(Dpi.S(3), Dpi.S(10)); if (r.Contains(e.Location)) { SessionRecorder.SetTask(i); return; } }
        }

        // ---- the actions
        int TextWidth { get { return Math.Max(Dpi.S(120), list.ClientSize.Width - Dpi.S(110)); } }
        void Measure(object s, MeasureItemEventArgs e)
        {
            var r = rows[e.Index];
            int h = TextRenderer.MeasureText(r.Action.Text ?? "", Theme.Body(10.5f), new Size(TextWidth, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            e.ItemHeight = Math.Max(Dpi.S(48), h + Dpi.S(26));
        }
        void Draw(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= rows.Count) return;
            var r = rows[e.Index]; var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; var b = e.Bounds;
            bool isDone = SessionRecorder.IsDone(r.Index);
            Theme.Backdrop(g, Color.White, b);
            var box = new RectangleF(b.X + Dpi.S(20), b.Y + Dpi.S(13), Dpi.F(24), Dpi.F(24));
            if (isDone)
            {
                using (var br = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(br, box);
                using (var pen = new Pen(Theme.Ink, Dpi.F(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(box.X + box.Width * 0.27f, box.Y + box.Height * 0.52f), new PointF(box.X + box.Width * 0.44f, box.Y + box.Height * 0.69f), new PointF(box.X + box.Width * 0.75f, box.Y + box.Height * 0.32f) });
            }
            else using (var pen = new Pen(Theme.Charcoal, Dpi.F(1.8f))) g.DrawEllipse(pen, box);
            using (var f = Theme.Body(10.5f)) TextRenderer.DrawText(g, r.Action.Text ?? "", f, new Rectangle(b.X + Dpi.S(58), b.Y + Dpi.S(13), TextWidth, b.Height - Dpi.S(16)), isDone ? Theme.Slate : Theme.Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            using (var f = Theme.Body(8.5f)) TextRenderer.DrawText(g, r.Action.Code, f, new Rectangle(b.Right - Dpi.S(34) - Dpi.S(40), b.Y + Dpi.S(14), Dpi.S(48), Dpi.S(18)), Theme.Mist, TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.SingleLine);
            using (var p = new Pen(Theme.Rule)) g.DrawLine(p, b.X + Dpi.S(58), b.Bottom - 1, b.Right - Dpi.S(16), b.Bottom - 1);
        }
        void Clicked(object s, MouseEventArgs e)
        {
            int i = list.IndexFromPoint(e.Location); if (i < 0 || i >= rows.Count) return;
            SessionRecorder.ToggleDone(rows[i].Index);          // tick or untick it
        }
        // redraw, and turn to the page of the current task
        public void Refresh2()
        {
            int pos = Pos;
            if (pos != shown) Rebuild(pos);
            prev.Enabled = pos > 0; next.Enabled = pos < tasks.Count - 1; next.Text = pos < tasks.Count - 1 ? "Next task" : "Last task";
            pause.Text = SessionRecorder.Paused ? "Resume" : "Pause"; pause.Icon = SessionRecorder.Paused ? Ic.Record : Ic.Pause;
            header.Invalidate(); pager.Invalidate(); list.Invalidate();
        }
    }
}
