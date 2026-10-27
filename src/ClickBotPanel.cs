// The ClickBot tab: pick a saved session, fill in its inputs (or load a CSV for many runs), press Run, watch the
// step ribbon fill as the bot works. The big round button on the capture window is the Run button.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    // One segment per step: grey until it is reached, blue while it runs, then green, amber (done, with a warning) or red (failed).
    class StepRibbon : Control
    {
        int[] state = new int[0]; public bool OnDark;
        public StepRibbon() { DoubleBuffered = true; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); AccessibleName = "Replay progress"; AccessibleRole = AccessibleRole.ProgressBar; }
        public void Reset(int total) { state = new int[Math.Max(0, total)]; Invalidate(); }
        public void Set(int step, int st) { if (step < 1 || step > state.Length) return; state[step - 1] = st; AccessibleDescription = step + " of " + state.Length; Invalidate(); }
        static Color Of(int st, bool dark)
        {
            switch (st) { case 1: return Theme.Cobalt; case 2: return Theme.Ok; case 3: return Theme.Warn; case 4: return Theme.Mark; }
            return dark ? Theme.InkSoft : Theme.Rule;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Backdrop(g, Parent != null ? Parent.BackColor : Theme.White, ClientRectangle);
            int n = state.Length;
            if (n == 0) { using (var p = Theme.RoundRect(new RectangleF(0, 0, Width, Height), Height / 2f)) using (var b = new SolidBrush(Of(0, OnDark))) g.FillPath(b, p); return; }
            float gap = n > 40 ? 1f : Dpi.F(3), seg = Math.Max(1.5f, (Width - gap * (n - 1)) / n);
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(i * (seg + gap), 0, seg, Height);
                using (var b = new SolidBrush(Of(state[i], OnDark)))
                {
                    if (seg >= 6) using (var p = Theme.RoundRect(r, Height / 2f)) g.FillPath(b, p); else g.FillRectangle(b, r);
                }
            }
        }
    }

    class PickHeader { public string Text; }
    class PickItem { public SessionDoc.Summary S; }

    class ClickBotPanel : Panel
    {
        static string lastPath;                                  // the selection survives switching tabs
        static readonly Dictionary<string, Dictionary<string, string>> remembered = new Dictionary<string, Dictionary<string, string>>();   // values live in memory only, never in a file
        readonly ListBox list, taskList; readonly Panel empty, right; readonly Label emptyTitle, emptyText, title, meta, idle, csvChip, status, valuesInfo;
        readonly FlatButton csvBtn, valuesBtn; readonly LinkLabel csvClear, openRun, openOrig; readonly CheckBox strict; readonly StepRibbon ribbon; readonly ToolTip tips = new ToolTip();
        Dictionary<string, string> values = new Dictionary<string, string>();
        List<Dictionary<string, string>> rows; int hover = -1; string runPath; Form hooked; bool busy, suppress;

        public ClickBotPanel()
        {
            BackColor = Color.White; Font = Theme.Body(9.5f); DoubleBuffered = true;

            list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawVariable, IntegralHeight = false, BackColor = Color.White, AccessibleName = "Saved sessions" };
            list.MeasureItem += delegate(object s, MeasureItemEventArgs e) { e.ItemHeight = list.Items[e.Index] is PickHeader ? Dpi.S(32) : Dpi.S(58); };
            list.DrawItem += DrawRow;
            list.MouseMove += delegate(object s, MouseEventArgs e) { int i = list.IndexFromPoint(e.Location); if (i != hover) { hover = i; list.Invalidate(); } };
            list.MouseLeave += delegate { hover = -1; list.Invalidate(); };
            list.SelectedIndexChanged += delegate { if (!suppress) OnPick(); };
            list.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) { int i = list.IndexFromPoint(e.Location); if (i >= 0 && list.Items[i] is PickItem) { list.SelectedIndex = i; RowMenu(); } } };
            var divider = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Rule };

            emptyTitle = new Label { Text = "No sessions yet", Font = Theme.BodyBold(10.5f), ForeColor = Theme.Ink, AutoSize = false, Location = new Point(Dpi.S(16), Dpi.S(10)), Size = Dpi.S(204, 22) };
            emptyText = new Label { Text = "Add a procedure and Claude captures one of its activities for you, or record one yourself in the Sessions tab. Saved sessions show up here.", ForeColor = Theme.Slate, AutoSize = false, Location = new Point(Dpi.S(16), Dpi.S(36)), Size = Dpi.S(204, 110) };
            empty = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Visible = false };
            empty.Controls.AddRange(new Control[] { emptyTitle, emptyText });

            // the left side: the sessions, and the way to add a procedure
            var addBar = new Panel { Dock = DockStyle.Bottom, Height = Dpi.S(56), BackColor = Theme.Sheet };
            addBar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, 0, addBar.Width, 0); };
            var add = new FlatButton("Add a procedure...", true) { Location = new Point(Dpi.S(14), Dpi.S(9)), Size = Dpi.S(208, 38), Icon = Ic.Plus };
            tips.SetToolTip(add, "Choose a Process Manager export and the activity Claude should capture");
            add.Click += delegate { AddProcedure(); };
            addBar.Controls.Add(add); addBar.Resize += delegate { add.Width = Math.Max(Dpi.S(120), addBar.Width - Dpi.S(28)); };
            var leftPane = new Panel { Dock = DockStyle.Left, Width = Dpi.S(236), BackColor = Color.White };
            leftPane.Controls.Add(list); leftPane.Controls.Add(empty); leftPane.Controls.Add(addBar);

            right = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            title = new Label { AutoSize = false, Font = Theme.BodyBold(12.5f), ForeColor = Theme.Ink, AutoEllipsis = true };
            meta = new Label { AutoSize = false, ForeColor = Theme.Slate, AutoEllipsis = true };
            idle = new Label { AutoSize = false, ForeColor = Theme.Slate, Text = "Choose a session on the left, or add a procedure to have Claude capture an activity.\r\n\r\nClickBot repeats a session for you: it asks for the values it needs, you press Run, and it works by itself. XLCapture minimizes while it runs. Press Pause/Break to stop." };
            taskList = new ListBox { BorderStyle = BorderStyle.None, BackColor = Theme.Sheet, IntegralHeight = false, Font = Theme.Body(9.25f), ForeColor = Theme.InkSoft, SelectionMode = SelectionMode.None, AccessibleName = "Tasks in this session" };
            valuesInfo = new Label { AutoSize = false, ForeColor = Theme.Slate };
            valuesBtn = new FlatButton("Enter values...", false) { Height = Dpi.S(28) }; valuesBtn.Click += delegate { EnterValues(); };
            csvBtn = new FlatButton("Use a CSV for many runs...", false) { Height = Dpi.S(28) }; csvBtn.Click += delegate { LoadCsv(); };
            tips.SetToolTip(csvBtn, "A CSV with a header row naming the inputs, then one row per run");
            csvChip = new Label { AutoSize = false, ForeColor = Theme.AccentInk, Font = Theme.BodyBold(9.5f), TextAlign = ContentAlignment.MiddleLeft, Visible = false };
            csvClear = new LinkLabel { Text = "Remove", AutoSize = true, Visible = false, LinkColor = Theme.AccentInk, ActiveLinkColor = Theme.Charcoal, LinkBehavior = LinkBehavior.HoverUnderline, AccessibleName = "Remove the CSV" };
            csvClear.Click += delegate { ClearCsv(); };
            strict = new ThemedCheck { Text = "Stop if a screen isn't the one that was recorded", Width = Dpi.S(360) };
            ribbon = new StepRibbon { Height = Dpi.S(8) };
            status = new Label { AutoSize = false, ForeColor = Theme.Slate };
            openRun = new LinkLabel { Text = "Open this run", AutoSize = true, Visible = false, LinkColor = Theme.AccentInk, ActiveLinkColor = Theme.Charcoal, LinkBehavior = LinkBehavior.HoverUnderline };
            openOrig = new LinkLabel { Text = "Open the session", AutoSize = true, Visible = false, LinkColor = Theme.AccentInk, ActiveLinkColor = Theme.Charcoal, LinkBehavior = LinkBehavior.HoverUnderline };
            openRun.Click += delegate { if (runPath != null) EditorForm.ShowSession(runPath); };
            openOrig.Click += delegate { var it = Selected(); if (it != null) EditorForm.ShowSession(it.S.Path); };
            // the task list, input summary and the values / CSV buttons are not shown on this page; Run still asks for any value it needs
            right.Controls.AddRange(new Control[] { title, meta, idle, strict, ribbon, status, openRun, openOrig });
            right.Resize += delegate { DoLayout(); };

            Controls.Add(right); Controls.Add(divider); Controls.Add(leftPane);
            ParentChanged += delegate { Hook(); };
            Disposed += delegate { if (hooked != null) hooked.Activated -= OnActivated; };
            Reload(); DoLayout(); OnPick();
        }

        // sessions saved while another window had focus appear when the capture window comes back
        void Hook() { var f = FindForm(); if (f != null && f != hooked) { if (hooked != null) hooked.Activated -= OnActivated; hooked = f; f.Activated += OnActivated; } }
        void OnActivated(object s, EventArgs e) { if (!busy && IsHandleCreated) Reload(); }

        PickItem Selected() { return list.SelectedItem as PickItem; }
        static string Pretty(string name) { return name.Replace('_', ' '); }

        // ------------------------------------------------------------ the list
        public void Reload()
        {
            string prev = Selected() != null ? Selected().S.Path : null; string keep = prev ?? lastPath;
            var all = new List<SessionDoc.Summary>();
            try
            {
                if (Directory.Exists(Settings.Folder))
                    foreach (var f in new DirectoryInfo(Settings.Folder).GetFiles().Where(x => SessionDoc.IsSessionExt(x.Extension)).OrderByDescending(x => x.LastWriteTime).Take(80))
                    { var s = SessionDoc.ReadSummary(f.FullName); if (s != null && s.Steps > 0) all.Add(s); }
            }
            catch { }
            suppress = true;           // rebuilding the list must not wipe the inputs or the last result when the choice has not changed
            try
            {
                list.BeginUpdate(); list.Items.Clear();
                var learned = all.Where(s => !s.IsRun).ToList(); var runs = all.Where(s => s.IsRun).Take(12).ToList();
                if (learned.Count > 0) { list.Items.Add(new PickHeader { Text = "Saved sessions" }); foreach (var s in learned) list.Items.Add(new PickItem { S = s }); }
                if (runs.Count > 0) { list.Items.Add(new PickHeader { Text = "Earlier runs" }); foreach (var s in runs) list.Items.Add(new PickItem { S = s }); }
                int at = -1;
                for (int i = 0; i < list.Items.Count; i++) { var it = list.Items[i] as PickItem; if (it != null && at < 0 && it.S.Path == keep) at = i; }
                if (at < 0) for (int i = 0; i < list.Items.Count; i++) if (list.Items[i] is PickItem) { at = i; break; }
                list.EndUpdate();
                empty.Visible = all.Count == 0; list.Visible = all.Count > 0;
                if (at >= 0) list.SelectedIndex = at;
            }
            finally { suppress = false; }
            string now = Selected() != null ? Selected().S.Path : null;
            if (now != prev) OnPick();
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var b = e.Bounds; var obj = list.Items[e.Index];
            Theme.Backdrop(g, Color.White, b);
            var h = obj as PickHeader;
            if (h != null) { using (var f = Theme.BodyBold(9f)) using (var br = new SolidBrush(Theme.Slate)) g.DrawString(h.Text, f, br, b.X + Dpi.S(16), b.Y + Dpi.S(12)); return; }
            var it = (PickItem)obj; bool sel = (e.State & DrawItemState.Selected) != 0, hov = e.Index == hover;
            if (sel) using (var br = new SolidBrush(Theme.CobaltTint)) g.FillRectangle(br, b);
            else if (hov) Theme.Backdrop(g, Theme.Sheet, b);
            if (sel) using (var br = new SolidBrush(Theme.Cobalt)) g.FillRectangle(br, b.X, b.Y + Dpi.S(6), Dpi.S(3), b.Height - Dpi.S(12));
            Icons.Draw(g, it.S.IsRun ? Ic.Recent : Ic.Session, new RectangleF(b.X + Dpi.S(16), b.Y + Dpi.S(17), Dpi.S(22), Dpi.S(22)), sel ? Theme.AccentInk : Theme.Mist);

            int x = b.X + Dpi.S(48), right = b.Right - Dpi.S(12);
            using (var f = Theme.BodyBold(10f)) using (var br = new SolidBrush(Theme.Ink)) using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(it.S.Name, f, br, new RectangleF(x, b.Y + Dpi.S(9), right - x, Dpi.S(22)), sf);
            // the inputs badge sits on the second line, so a long name keeps the full width of the first
            string pill = it.S.Inputs.Count == 0 ? null : it.S.Inputs.Count + (it.S.Inputs.Count == 1 ? " input" : " inputs");
            if (pill != null)
                using (var f = Theme.BodyBold(8.5f))
                {
                    var sz = g.MeasureString(pill, f); int pw = (int)sz.Width + Dpi.S(12), ph = Dpi.S(19); var pr = new Rectangle(right - pw, b.Y + Dpi.S(30), pw, ph);
                    using (var gp = Theme.RoundRect(pr, ph / 2f)) using (var br = new SolidBrush(sel ? Color.White : Theme.CobaltTint)) g.FillPath(br, gp);
                    using (var br = new SolidBrush(Theme.AccentInk)) g.DrawString(pill, f, br, pr.X + Dpi.S(6), pr.Y + (ph - sz.Height) / 2 + 1);
                    right = pr.X - Dpi.S(6);
                }
            string sub = it.S.Steps + (it.S.Steps == 1 ? " step" : " steps") + (it.S.Apps.Count > 0 ? " in " + string.Join(", ", it.S.Apps.ToArray()) : "");
            using (var f = Theme.Body(9f)) using (var br = new SolidBrush(Theme.Slate)) using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(sub, f, br, new RectangleF(x, b.Y + Dpi.S(32), Math.Max(10, right - x), Dpi.S(20)), sf);
        }

        void RowMenu()
        {
            var it = Selected(); if (it == null) return;
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f) };
            m.Items.Add("Open in the editor", null, delegate { EditorForm.ShowSession(it.S.Path); });
            m.Items.Add("Show in folder", null, delegate { Process.Start("explorer.exe", "/select,\"" + it.S.Path + "\""); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Refresh the list", null, delegate { Reload(); });
            m.Show(Cursor.Position);
        }

        // ------------------------------------------------------------ the right side
        void OnPick()
        {
            if (list.SelectedItem is PickHeader)      // headers are not choices: move to the first session under it
            {
                int i = list.SelectedIndex + 1; if (i < list.Items.Count && list.Items[i] is PickItem) { list.SelectedIndex = i; return; }
                list.SelectedIndex = -1; return;
            }
            var it = Selected(); if (it != null) lastPath = it.S.Path;
            ClearCsv();
            bool has = it != null;
            idle.Visible = !has;
            foreach (Control c in new Control[] { title, meta, valuesInfo, strict, ribbon, status }) c.Visible = has;
            valuesBtn.Visible = has && it.S.Inputs.Count > 0; csvBtn.Visible = has && it.S.Inputs.Count > 0; taskList.Visible = has && it.S.TaskNames.Count > 0;
            openRun.Visible = false; openOrig.Visible = false; runPath = null;
            if (has)
            {
                Dictionary<string, string> mem; values = remembered.TryGetValue(it.S.Path, out mem) ? new Dictionary<string, string>(mem) : new Dictionary<string, string>();
                title.Text = it.S.Name;
                string tk = it.S.Tasks > 0 ? it.S.Tasks + (it.S.Tasks == 1 ? " task, " : " tasks, ") : "";
                meta.Text = it.S.IsRun ? "A run the bot recorded. Open it to see what it did." : tk + it.S.Steps + (it.S.Steps == 1 ? " step" : " steps") + (it.S.Apps.Count > 0 ? " in " + string.Join(" and ", it.S.Apps.ToArray()) : "") + ", saved " + When(it.S.Saved);
                taskList.Items.Clear(); foreach (var n in it.S.TaskNames) taskList.Items.Add(n);
                ribbon.Reset(it.S.Steps); RefreshValuesInfo();
                Say(it.S.IsRun ? "This is a record of a past run." : "Ready. XLCapture minimizes while the bot works; press Pause/Break to stop it.", Theme.Slate);
            }
            DoLayout();
        }

        // ------------------------------------------------------------ the values a run needs
        static bool Needed(SessionDoc.Summary s, InputSpec sp) { return !sp.Optional && !s.Defaults.ContainsKey(sp.Param); }
        bool AllFilled(PickItem it) { foreach (var sp in it.S.Specs) { string v; if (Needed(it.S, sp) && (!values.TryGetValue(sp.Param, out v) || v.Length == 0)) return false; } return true; }
        void RefreshValuesInfo()
        {
            var it = Selected(); if (it == null) return;
            int n = it.S.Specs.Count, tasks = it.S.Specs.Select(x => x.Task).Distinct().Count(x => x.Length > 0);
            if (n == 0) { valuesInfo.Text = "No inputs. It repeats exactly as recorded."; return; }
            int need = it.S.Specs.Count(x => Needed(it.S, x)), have = it.S.Specs.Count(x => { string v; return Needed(it.S, x) && values.TryGetValue(x.Param, out v) && v.Length > 0; });
            string where = tasks > 1 ? " across " + tasks + " tasks" : "";
            valuesInfo.Text = n + (n == 1 ? " input" : " inputs") + where + ". " + (rows != null ? "The CSV supplies them." : have == need ? (need == 0 ? "All have a default." : "All " + need + " required values are entered.") : have + " of " + need + " required values entered.");
            valuesInfo.ForeColor = rows == null && have < need ? Theme.Slate : Theme.Ok;
            valuesBtn.Text = have == 0 ? "Enter values..." : "Change values...";
        }
        // the form that asks for the values; returns false if it was cancelled
        bool EnterValues()
        {
            var it = Selected(); if (it == null) return false;
            var start = new Dictionary<string, string>(); foreach (var kv in it.S.Defaults) start[kv.Key] = kv.Value; foreach (var kv in values) start[kv.Key] = kv.Value;
            using (var f = new ValuesForm(it.S.Name, "Enter what the bot should type or choose in this run. Nothing here is saved in the session, so you can run it again with other values.", it.S.Specs, start, "Use these values"))
            {
                if (f.ShowDialog(FindForm()) != DialogResult.OK) return false;
                values = f.Result; remembered[it.S.Path] = new Dictionary<string, string>(values); RefreshValuesInfo(); return true;
            }
        }
        void AddProcedure()
        {
            using (var w = new ProcedureWizard()) w.ShowDialog(FindForm());
            Reload();
        }
        static string When(DateTime t)
        {
            if (t.Date == DateTime.Today) return "today " + t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return "yesterday " + t.ToString("HH:mm");
            return t.ToString("d MMM HH:mm");
        }

        void DoLayout()
        {
            int x = Dpi.S(20), w = Math.Max(Dpi.S(180), right.ClientSize.Width - x - Dpi.S(16)); int y = Dpi.S(12);
            idle.SetBounds(x, y + Dpi.S(4), w, Dpi.S(170));
            title.SetBounds(x, y, w, Dpi.S(26)); meta.SetBounds(x, y + Dpi.S(26), w, Dpi.S(20));
            taskList.SetBounds(x, y + Dpi.S(54), w, Dpi.S(72));
            int vy = y + Dpi.S(56);
            valuesInfo.SetBounds(x, vy, w, Dpi.S(20));
            int row = vy;
            valuesBtn.SetBounds(x, row, Dpi.S(142), Dpi.S(28));
            csvBtn.SetBounds(x + Dpi.S(150), row, Dpi.S(184), Dpi.S(28));
            csvChip.SetBounds(x + Dpi.S(150) + Dpi.S(190), row, Math.Max(40, w - Dpi.S(150 + 190 + 64)), Dpi.S(28));
            csvClear.Location = new Point(x + w - Dpi.S(54), row + Dpi.S(6));
            strict.Location = new Point(x, row + Dpi.S(34));
            ribbon.SetBounds(x, row + Dpi.S(66), w, Dpi.S(8));
            status.SetBounds(x, row + Dpi.S(78), w, Dpi.S(38));
            openRun.Location = new Point(x, row + Dpi.S(118)); openOrig.Location = new Point(x + openRun.Width + Dpi.S(20), row + Dpi.S(118));
        }

        void Say(string text, Color c) { status.Text = text; status.ForeColor = c; }

        void LoadCsv()
        {
            var it = Selected(); if (it == null) return;
            using (var f = new OpenFileDialog { Filter = "CSV file|*.csv|All files|*.*", Title = "A CSV with a header row naming the inputs, then one row per run" })
            {
                if (f.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    var r = Replayer.ParseCsv(f.FileName);
                    if (r.Count == 0) { Say("That file has no data rows. Row 1 names the inputs, the rows after it hold the values.", Theme.Warn); return; }
                    var missing = it.S.Inputs.Where(n => !it.S.Defaults.ContainsKey(n) && !r[0].ContainsKey(n)).ToList();
                    if (missing.Count > 0) { Say("The CSV has no column for " + string.Join(", ", missing.Select(Pretty).ToArray()) + ".", Theme.Warn); return; }
                    rows = r; csvChip.Text = r.Count + (r.Count == 1 ? " run" : " runs") + " from " + Path.GetFileName(f.FileName); csvChip.Visible = true; csvClear.Visible = true; valuesBtn.Enabled = false; RefreshValuesInfo();
                    Say("Ready to run " + r.Count + (r.Count == 1 ? " row." : " rows, one after another."), Theme.Slate);
                }
                catch (Exception ex) { Say("Could not read that file: " + ex.Message, Theme.Mark); }
            }
        }
        void ClearCsv() { rows = null; csvChip.Visible = false; csvClear.Visible = false; valuesBtn.Enabled = true; if (Selected() != null) RefreshValuesInfo(); }

        // ------------------------------------------------------------ running
        public void Run()
        {
            if (Replayer.Running) { Replayer.Cancel(); return; }
            var it = Selected(); if (it == null) { Say("Choose a session on the left first.", Theme.Warn); return; }
            if (it.S.IsRun) { Say("This is a record of a past run. Choose a saved session to run.", Theme.Warn); return; }
            if (!Dialogs.ConfirmSharedRun(FindForm(), it.S.Path)) return;
            // it asks for whatever it still needs before it starts
            if (rows == null && !AllFilled(it) && !EnterValues()) return;
            var runValues = new Dictionary<string, string>(values);
            SessionDoc doc;
            try { doc = SessionDoc.Load(it.S.Path); } catch (Exception ex) { Say("Could not open this session: " + ex.Message, Theme.Mark); return; }
            if (doc.Steps.Count == 0) { Say("This session has no steps.", Theme.Warn); return; }

            var opt = new Replayer.Options { Strict = strict.Checked, Values = runValues, ShowBar = true };
            opt.OnStep = delegate(int i, int n, string text, int st)
            {
                try { BeginInvoke((Action)delegate { if (i == 1 && st == 1) ribbon.Reset(n); ribbon.Set(i, st); }); } catch { }
            };
            ribbon.Reset(doc.Steps.Count); openRun.Visible = false; openOrig.Visible = false; busy = true;
            Say("Running. XLCapture is stepping aside...", Theme.AccentInk);
            var owner = FindForm(); var state = owner.WindowState; owner.WindowState = FormWindowState.Minimized;
            var rowsNow = rows; var clock = Stopwatch.StartNew();
            var th = new System.Threading.Thread(delegate()
            {
                var results = rowsNow != null ? Replayer.RunBatch(doc, rowsNow, opt) : new List<Replayer.Result> { Replayer.Run(doc, opt) };
                double secs = clock.Elapsed.TotalSeconds;
                try { BeginInvoke((Action)delegate { owner.WindowState = state; owner.Activate(); Finish(it, results, secs); }); } catch { }
            }) { IsBackground = true, Name = "clickbot" };
            th.Start();
        }

        void Finish(PickItem it, List<Replayer.Result> results, double secs)
        {
            busy = false;
            var last = results.Last(); bool ok = results.All(r => r.Ok); bool many = results.Count > 1 || rows != null;
            string s = secs.ToString("0.0") + " s";
            if (ok)
                Say(many ? "Done. " + results.Count + " runs finished in " + s + "." : "Done. " + last.Done + " of " + last.Total + " steps in " + s + ".", Theme.Ok);
            else if (last.Error == "Stopped.")
                Say("You stopped the run after " + last.Done + " of " + last.Total + " steps.", Theme.Warn);
            else
                Say((many ? "Run " + results.Count + " stopped. " : "Stopped. ") + last.Error + "\r\nA control may have been renamed, or the screen is different. Open this run to see where it stopped.", Theme.Mark);
            runPath = last.Saved; openRun.Visible = runPath != null; openOrig.Visible = true; DoLayout();
            Reload();
            if (Hub.Instance != null) Hub.Instance.UpdateCaptureButton();
        }
    }
}
