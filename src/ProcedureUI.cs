// The windows for procedures: the form that asks for the values a run needs, the wizard that turns a Process
// Manager export into a job for Claude, and the launcher that starts Claude Code on that job.
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
    // The values a run needs, grouped by the task they belong to. Used before teaching and before every replay.
    class ValuesPanel : Panel
    {
        readonly List<InputSpec> specs; readonly Dictionary<string, Control> ctl = new Dictionary<string, Control>(); readonly ToolTip tips = new ToolTip();

        public ValuesPanel(List<InputSpec> specs, Dictionary<string, string> initial, int width)
        {
            this.specs = specs; AutoScroll = true; BackColor = Color.White; Font = Theme.Body(9.5f); Width = width;
            int inner = width - Dpi.S(48), y = Dpi.S(8); string lastTask = null;
            foreach (var s in specs)
            {
                if (s.Task != lastTask && s.Task.Length > 0)
                {
                    if (lastTask != null) { y += Dpi.S(6); Controls.Add(new Panel { Location = new Point(Dpi.S(16), y), Size = new Size(inner, 1), BackColor = Theme.Rule, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right }); y += Dpi.S(12); }
                    Controls.Add(new Label { Text = s.Task, AutoSize = false, Location = new Point(Dpi.S(16), y), Size = new Size(inner, Dpi.S(22)), Font = Theme.BodyBold(10f), ForeColor = Theme.Ink, AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right });
                    y += Dpi.S(28); lastTask = s.Task;
                }
                var l = new Label { Text = s.Label + (s.Optional ? "   optional" : ""), AutoSize = false, Location = new Point(Dpi.S(16), y), Size = new Size(inner, Dpi.S(18)), ForeColor = Theme.Slate, Font = Theme.Body(9.25f), AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                Controls.Add(l); y += Dpi.S(19);
                string have; initial.TryGetValue(s.Param, out have); have = have ?? "";
                Control c;
                if (s.Choices.Count > 0)
                {
                    var cb = new ThemedCombo { Location = new Point(Dpi.S(16), y), Width = Dpi.S(240), Font = Theme.Body(10f) };
                    if (s.Optional) cb.Items.Add(""); foreach (var ch in s.Choices) cb.Items.Add(ch);
                    int i = cb.Items.IndexOf(have); cb.SelectedIndex = i >= 0 ? i : (s.Optional ? 0 : -1); c = cb; y += Dpi.S(32);
                }
                else if (s.Type == "file")
                {
                    var tb = new TextBox { Location = new Point(Dpi.S(16), y), Width = inner - Dpi.S(92), BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body(10f), Text = have, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                    var br = new FlatButton("Browse...", false) { Location = new Point(Dpi.S(16) + inner - Dpi.S(84), y - 1), Size = Dpi.S(84, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
                    br.Click += delegate { using (var f = new OpenFileDialog { Title = s.Label }) if (f.ShowDialog(FindForm()) == DialogResult.OK) tb.Text = f.FileName; };
                    Controls.Add(br); c = tb; y += Dpi.S(32);
                }
                else
                {
                    bool big = System.Text.RegularExpressions.Regex.IsMatch(s.Label.ToLowerInvariant(), "description|comments|summary|explanation|note");
                    var tb = new TextBox { Location = new Point(Dpi.S(16), y), Width = inner, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body(10f), Text = have, Multiline = big, Height = big ? Dpi.S(56) : Dpi.S(26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                    if (s.Type == "date") tips.SetToolTip(tb, "Type the date the way the app shows dates");
                    c = tb; y += big ? Dpi.S(62) : Dpi.S(32);
                }
                c.AccessibleName = s.Label; Controls.Add(c); ctl[s.Param] = c; y += Dpi.S(8);
            }
            if (specs.Count == 0) Controls.Add(new Label { Text = "Nothing to enter. Claude only clicks in this activity.", AutoSize = false, Location = new Point(Dpi.S(16), Dpi.S(16)), Size = new Size(inner, Dpi.S(40)), ForeColor = Theme.Slate });
        }

        static string Text(Control c) { var cb = c as ComboBox; return cb != null ? (cb.SelectedItem == null ? "" : cb.SelectedItem.ToString()) : c.Text; }
        public Dictionary<string, string> Values() { var d = new Dictionary<string, string>(); foreach (var kv in ctl) d[kv.Key] = Text(kv.Value).Trim(); return d; }
        // the first required value that is still empty; it is scrolled to and focused
        public InputSpec FirstMissing()
        {
            foreach (var s in specs) if (!s.Optional && Text(ctl[s.Param]).Trim().Length == 0) { ScrollControlIntoView(ctl[s.Param]); ctl[s.Param].Focus(); return s; }
            return null;
        }
    }

    class ValuesForm : Form
    {
        readonly ValuesPanel panel; public Dictionary<string, string> Result;
        public ValuesForm(string title, string intro, List<InputSpec> specs, Dictionary<string, string> initial, string okText)
        {
            Text = title; FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.Body(9.5f);
            ClientSize = new Size(Dpi.S(560), Dpi.S(Math.Min(640, 190 + specs.Count * 66))); MinimumSize = new Size(Dpi.S(480), Dpi.S(300));
            var head = new Label { Text = intro, Dock = DockStyle.Top, Height = Dpi.S(64), Padding = Dpi.P(16, 14, 16, 0), ForeColor = Theme.Slate };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Dpi.S(60), BackColor = Theme.Sheet };
            var ok = new FlatButton(okText, true) { Size = Dpi.S(150, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; ok.Location = new Point(bar.Width - Dpi.S(150 + 16 + 96 + 8), Dpi.S(13));
            var cancel = new FlatButton("Cancel", false) { Size = Dpi.S(96, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right }; cancel.Location = new Point(bar.Width - Dpi.S(96 + 16), Dpi.S(13));
            bar.Controls.AddRange(new Control[] { ok, cancel });
            bar.Layout += delegate { ok.Location = new Point(bar.Width - Dpi.S(150 + 16 + 96 + 8), Dpi.S(13)); cancel.Location = new Point(bar.Width - Dpi.S(96 + 16), Dpi.S(13)); };
            panel = new ValuesPanel(specs, initial, ClientSize.Width) { Dock = DockStyle.Fill };
            Controls.Add(panel); Controls.Add(head); Controls.Add(bar); panel.BringToFront();
            AcceptButton = null; CancelButton = cancel;
            ok.Click += delegate
            {
                var miss = panel.FirstMissing();
                if (miss != null) { MessageBox.Show(this, "Enter a value for \"" + miss.Label + "\" first.", Text); return; }
                Result = panel.Values(); DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        }
    }

    static class ClaudeLauncher
    {
        public static string Find()
        {
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) { try { string p = Path.Combine(d.Trim(), "claude.exe"); if (d.Trim().Length > 0 && File.Exists(p)) return p; } catch { } }
            string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"); if (File.Exists(home)) return home;
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
                var found = new List<string>();
                if (Directory.Exists(root))
                    foreach (var prof in Directory.GetDirectories(root))
                    {
                        string cc = Path.Combine(prof, "claude-code"); if (!Directory.Exists(cc)) continue;
                        foreach (var ver in Directory.GetDirectories(cc)) foreach (var h in Directory.GetDirectories(ver)) { string e = Path.Combine(h, "claude.exe"); if (File.Exists(e)) found.Add(e); }
                    }
                return found.OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
            }
            catch { return null; }
        }

        public static string Prompt(string jobPath)
        {
            SkillInstaller.EnsureInstalled();      // the skill goes into the user's Claude folder if it is not there yet
            return "XLCapture's program file is '" + Application.ExecutablePath + "'. Use the snaplite-teach skill to teach the activity in the job file at '" + jobPath + "'. Follow the job file exactly: perform each action in InEight through XLCapture, " +
                "use the values written in the file, and save one session named after the activity with its steps grouped by task. Do not stop to ask me anything the job file already answers.";
        }

        // Claude Code opens in its own console window, so you can watch it, and it asks before it runs anything
        public static bool Start(string jobPath, out string message)
        {
            var exe = Find();
            string prompt = Prompt(jobPath);
            RemoteControl.Start();              // the channel Claude drives SnapLite through
            if (exe == null) { try { Clipboard.SetText(prompt); } catch { } message = "Claude Code was not found on this PC. The instructions are on your clipboard: paste them into a Claude Code session."; return false; }
            try
            {
                Process.Start(new ProcessStartInfo(exe, "\"" + prompt.Replace("\"", "'") + "\"") { UseShellExecute = true, WorkingDirectory = ProcedureFile.JobsFolder });
                message = "Claude Code is starting in its own window."; return true;
            }
            catch (Exception ex) { try { Clipboard.SetText(prompt); } catch { } message = "Claude Code could not start (" + ex.Message + "). The instructions are on your clipboard."; return false; }
        }
    }

    // ---- the wizard's two lists, drawn by hand so the selection, the chips and the task headings use the accent
    class ActivityList : ListBox
    {
        int hover = -1;
        public ActivityList()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Dpi.S(68); BorderStyle = BorderStyle.None; BackColor = Color.White; IntegralHeight = false; DoubleBuffered = true; AccessibleName = "Activities";
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = IndexFromPoint(e.Location); if (i != hover) { hover = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var b = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Theme.Backdrop(g, sel ? Theme.AccentTint : e.Index == hover ? Theme.Sheet : Color.White, b);
            if (e.Index < 0 || e.Index >= Items.Count) return;
            using (var p = new Pen(Theme.Rule)) g.DrawLine(p, b.X + Dpi.S(20), b.Bottom - 1, b.Right - Dpi.S(20), b.Bottom - 1);
            var a = (ProcActivity)Items[e.Index];
            if (sel) using (var br = new SolidBrush(Theme.Accent)) g.FillRectangle(br, b.X, b.Y + Dpi.S(8), Dpi.S(4), b.Height - Dpi.S(16));
            using (var f = Theme.Semi(11.5f)) using (var br = new SolidBrush(Theme.Ink)) using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(a.Title, f, br, new RectangleF(b.X + Dpi.S(22), b.Y + Dpi.S(11), b.Width - Dpi.S(130), Dpi.S(24)), sf);
            int x = b.X + Dpi.S(22), y = b.Y + Dpi.S(39);
            if (a.System.Length > 0) { var r = WizardDraw.Chip(g, new Point(x, y), a.System, true, sel); x = r.Right + Dpi.S(10); }
            using (var f = Theme.Body(9.5f)) using (var br = new SolidBrush(Theme.Slate))
                g.DrawString(a.Tasks.Count + (a.Tasks.Count == 1 ? " task, " : " tasks, ") + a.ActionCount + (a.ActionCount == 1 ? " action" : " actions"), f, br, x, y + 1);
            using (var f = Theme.Semi(10f)) using (var br = new SolidBrush(Theme.Mist)) using (var sf = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString("Activity " + a.Code, f, br, new RectangleF(b.Right - Dpi.S(110), b.Y + Dpi.S(13), Dpi.S(94), Dpi.S(22)), sf);
        }
    }

    // Pick the procedure file and the activity a session recording follows
    class ProcedurePickForm : Form
    {
        readonly Label fileLabel; readonly ActivityList list; readonly FlatButton choose, ok, none, cancel;
        List<ProcActivity> acts = new List<ProcActivity>();
        public ProcActivity Chosen; public string FilePath = "";
        public ProcedurePickForm(string file, string code)
        {
            Text = "Follow a procedure"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.Body(9.5f); ClientSize = Dpi.S(720, 530);
            var head = new Label { AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(20)), Size = Dpi.S(672, 32), Font = Theme.Semi(15f), ForeColor = Theme.Ink, Text = "Which activity are you recording?" };
            var sub = new Label { AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(56)), Size = Dpi.S(672, 44), ForeColor = Theme.Slate, Text = "Choose the Process Manager export, then the activity. While you record, a small bar shows the action to do next, and each click is tagged with it. The editor then groups your steps by task and uses the action texts." };
            choose = new FlatButton("Choose the procedure file...", false) { Location = new Point(Dpi.S(24), Dpi.S(108)), Size = Dpi.S(230, 38), Icon = Ic.Open };
            fileLabel = new Label { AutoSize = false, Location = new Point(Dpi.S(268), Dpi.S(115)), Size = Dpi.S(428, 24), ForeColor = Theme.Slate, AutoEllipsis = true, Text = "An Excel or CSV export from Process Manager" };
            var holder = new Panel { Location = new Point(Dpi.S(24), Dpi.S(156)), Size = Dpi.S(672, 280), BackColor = Color.White, Padding = new Padding(1) };
            holder.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawRectangle(p, 0, 0, holder.Width - 1, holder.Height - 1); };
            list = new ActivityList { Dock = DockStyle.Fill }; holder.Controls.Add(list);
            none = new FlatButton("Record without a procedure", false) { Location = new Point(Dpi.S(24), Dpi.S(466)), Size = Dpi.S(220, 38) };
            ok = new FlatButton("Use this activity", true) { Location = new Point(Dpi.S(436), Dpi.S(466)), Size = Dpi.S(160, 38), Enabled = false };
            cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(604), Dpi.S(466)), Size = Dpi.S(92, 38) };
            Controls.AddRange(new Control[] { head, sub, choose, fileLabel, holder, none, ok, cancel });
            choose.Click += delegate
            {
                using (var f = new OpenFileDialog { Filter = "Process Manager export|*.xlsx;*.csv|All files|*.*", Title = "Choose the procedure file" })
                    if (f.ShowDialog(this) == DialogResult.OK) LoadFile(f.FileName, "");
            };
            list.SelectedIndexChanged += delegate { ok.Enabled = list.SelectedItem != null; };
            list.DoubleClick += delegate { if (list.SelectedItem != null) Accept(); };
            ok.Click += delegate { Accept(); };
            none.Click += delegate { DialogResult = DialogResult.Ignore; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            CancelButton = cancel;
            if (!string.IsNullOrEmpty(file) && File.Exists(file)) LoadFile(file, code);
        }
        void Accept() { Chosen = (ProcActivity)list.SelectedItem; DialogResult = DialogResult.OK; Close(); }
        void LoadFile(string path, string code)
        {
            try
            {
                acts = ProcedureFile.Load(path); FilePath = path; fileLabel.Text = Path.GetFileName(path); fileLabel.ForeColor = Theme.Ink;
                list.Items.Clear(); foreach (var a in acts) list.Items.Add(a);
                int at = 0; for (int i = 0; i < acts.Count; i++) if (acts[i].Code == code) at = i;
                if (acts.Count > 0) list.SelectedIndex = at;
            }
            catch (Exception ex) { fileLabel.Text = "Could not read that file: " + ex.Message; fileLabel.ForeColor = Theme.Mark; list.Items.Clear(); acts.Clear(); ok.Enabled = false; }
        }
    }

    class ActionList : ListBox
    {
        int hover = -1; bool fixing;
        public ActionList()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Dpi.S(38); BorderStyle = BorderStyle.None; BackColor = Color.White; IntegralHeight = false; DoubleBuffered = true; SelectionMode = SelectionMode.MultiExtended; AccessibleName = "Actions";
        }
        // where the columns start, for the rows and for the header above them
        public static int[] Columns(int width)
        {
            // code | action | what Claude will do | the value to enter (editable) | the clicks shown
            int code = Dpi.S(20), codeW = Dpi.S(52), chipW = Dpi.S(138), valueW = Dpi.S(190), gap = Dpi.S(8), rest = width - code - codeW - chipW - valueW - gap * 3 - Dpi.S(20);
            int actionW = (int)(rest * 0.5), clicksW = rest - actionW;
            int x1 = code + codeW, x2 = x1 + actionW + gap, x3 = x2 + chipW + gap, x4 = x3 + valueW + gap;
            return new[] { code, x1, x2, x3, x4, actionW, chipW, valueW, clicksW };
        }
        // a click on the value cell of an action that takes a value asks the window to edit it
        public event Action<ProcAction, Rectangle> EditValue;
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
            int i = IndexFromPoint(e.Location); var a = i >= 0 ? Items[i] as ProcAction : null; if (a == null || (a.Kind != "enter" && a.Kind != "choose")) return;
            var cols = Columns(Width - SystemInformation.VerticalScrollBarWidth); var b = GetItemRectangle(i);
            var cell = new Rectangle(cols[3], b.Y + Dpi.S(5), cols[7], b.Height - Dpi.S(11));
            if (cell.Contains(e.Location) && EditValue != null) EditValue(a, cell);
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = IndexFromPoint(e.Location); if (i != hover) { hover = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { int i = IndexFromPoint(e.Location); if (i >= 0 && !GetSelected(i) && Items[i] is ProcAction) { ClearSelected(); SetSelected(i, true); } }
            base.OnMouseDown(e);
        }
        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            if (!fixing) { fixing = true; foreach (int i in SelectedIndices.Cast<int>().ToList()) if (Items[i] is ProcTask) SetSelected(i, false); fixing = false; }   // a task heading is not a choice
            base.OnSelectedIndexChanged(e);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var b = e.Bounds; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (e.Index < 0 || e.Index >= Items.Count) { Theme.Backdrop(g, Color.White, b); return; }
            var cols = Columns(Width - SystemInformation.VerticalScrollBarWidth);
            var t = Items[e.Index] as ProcTask;
            if (t != null)
            {
                Theme.Backdrop(g, Theme.Sheet, b);
                using (var br = new SolidBrush(Theme.Accent)) g.FillRectangle(br, b.X + Dpi.S(8), b.Y + Dpi.S(9), Dpi.S(4), b.Height - Dpi.S(18));
                using (var f = Theme.Semi(10.5f)) using (var br = new SolidBrush(Theme.Ink)) g.DrawString(t.Full, f, br, b.X + Dpi.S(20), b.Y + Dpi.S(9));
                using (var f = Theme.Body(9f)) using (var br = new SolidBrush(Theme.Slate)) g.DrawString(t.Actions.Count + (t.Actions.Count == 1 ? " action" : " actions"), f, br, b.Right - Dpi.S(90) - SystemInformation.VerticalScrollBarWidth, b.Y + Dpi.S(11));
                return;
            }
            var a = (ProcAction)Items[e.Index]; bool sel = (e.State & DrawItemState.Selected) != 0;
            Theme.Backdrop(g, sel ? Theme.AccentTint : e.Index == hover ? Theme.Sheet : Color.White, b);
            using (var f = Theme.Body(9.5f)) using (var br = new SolidBrush(Theme.Slate)) g.DrawString(a.Code, f, br, b.X + cols[0], b.Y + Dpi.S(10));
            Color ink = a.Kind == "skip" ? Theme.Mist : Theme.Ink;
            using (var f = Theme.Body(10f)) using (var br = new SolidBrush(ink)) using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(a.Text, f, br, new RectangleF(b.X + cols[1], b.Y + Dpi.S(9), cols[5], Dpi.S(22)), sf);
            WizardDraw.KindChip(g, new Point(b.X + cols[2], b.Y + Dpi.S(8)), a.Kind, sel);
            if (a.Kind == "enter" || a.Kind == "choose")      // the value cell: type or pick it here, it is what Claude enters
            {
                var cell = new Rectangle(b.X + cols[3], b.Y + Dpi.S(5), cols[7], b.Height - Dpi.S(11)); bool has = a.Value.Length > 0;
                using (var p = Theme.Pill(new RectangleF(cell.X + 0.5f, cell.Y + 0.5f, cell.Width - 1, cell.Height - 1)))
                {
                    using (var br = new SolidBrush(Color.White)) g.FillPath(br, p);
                    using (var pen = new Pen(has ? Theme.Charcoal : (a.Optional ? Theme.Rule : Theme.Mark), has ? 1.4f : 1.2f)) g.DrawPath(pen, p);
                }
                string shown = has ? a.Value : (a.Kind == "choose" ? "Choose..." : a.Optional ? "Optional" : "Enter a value...");
                using (var f = Theme.Body(9.5f)) using (var br = new SolidBrush(has ? Theme.Ink : Theme.Mist)) using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center })
                    g.DrawString(shown, f, br, new RectangleF(cell.X + Dpi.S(12), cell.Y, cell.Width - Dpi.S(22), cell.Height), sf);
            }
            if (a.Kind != "skip")
                using (var f = Theme.Body(9.5f)) using (var br = new SolidBrush(Theme.Slate)) using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(a.PlanText(), f, br, new RectangleF(b.X + cols[4], b.Y + Dpi.S(10), Math.Max(10, cols[8] - SystemInformation.VerticalScrollBarWidth / 2), Dpi.S(22)), sf);
            using (var p = new Pen(Color.FromArgb(0xEE, 0xF1, 0xF4))) g.DrawLine(p, b.X + cols[0], b.Bottom - 1, b.Right - Dpi.S(12), b.Bottom - 1);
        }
    }

    static class WizardDraw
    {
        // a small capsule label; returns where it was drawn
        public static Rectangle Chip(Graphics g, Point at, string text, bool tint, bool onSelected)
        {
            using (var f = Theme.Semi(8.75f))
            {
                var sz = g.MeasureString(text, f); int w = (int)sz.Width + Dpi.S(16), h = Dpi.S(22); var r = new Rectangle(at.X, at.Y, w, h);
                using (var p = Theme.Pill(r)) using (var br = new SolidBrush(tint ? (onSelected ? Color.White : Theme.AccentTint) : Theme.Sheet)) g.FillPath(br, p);
                using (var br = new SolidBrush(tint ? Theme.AccentInk : Theme.Slate)) g.DrawString(text, f, br, r.X + Dpi.S(8), r.Y + (h - sz.Height) / 2 + 1);
                return r;
            }
        }
        // what Claude does with an action: the two that need a value are accent chips, a plain click is quiet, "by hand" is dashed
        public static void KindChip(Graphics g, Point at, string kind, bool onSelected)
        {
            string text = kind == "skip" ? "By hand" : ProcAction.KindName(kind); int w = Dpi.S(128), h = Dpi.S(22); var r = new Rectangle(at.X, at.Y, w, h);
            using (var f = Theme.Semi(8.75f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var p = Theme.Pill(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1)))
            {
                if (kind == "enter" || kind == "choose")
                {
                    using (var br = new SolidBrush(Theme.AccentSoft)) g.FillPath(br, p);
                    using (var ink = new SolidBrush(Theme.Ink)) g.DrawString(text, f, ink, r, sf);
                }
                else
                {
                    using (var br = new SolidBrush(onSelected ? Color.White : Color.White)) g.FillPath(br, p);
                    using (var pen = new Pen(kind == "skip" ? Theme.Mist : Theme.Rule, 1.2f) { DashStyle = kind == "skip" ? DashStyle.Dash : DashStyle.Solid }) g.DrawPath(pen, p);
                    using (var br = new SolidBrush(kind == "skip" ? Theme.Mist : Theme.Slate)) g.DrawString(text, f, br, r, sf);
                }
            }
        }
    }

    class ProcedureWizard : Form
    {
        int page; List<ProcActivity> acts = new List<ProcActivity>(); ProcActivity act; List<InputSpec> specs = new List<InputSpec>();
        readonly Label head, sub, fileLabel, hint1, hint2;
        readonly Panel rail, p1, p2, body; readonly ActivityList actList; readonly ActionList actionList; readonly FlatButton choose, back, next, copy, cancel;
        public string JobPath; Control editor;
        static readonly string[] StepName = { "Procedure file", "Actions and values" };
        static readonly string[] StepNote = { "Choose the export and the activity", "What Claude does, and what it enters" };

        public ProcedureWizard()
        {
            Text = "Add a procedure"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.Body(9.5f); ClientSize = Dpi.S(1180, 650);

            // the left rail: where you are in the three steps
            rail = new Panel { Dock = DockStyle.Left, Width = Dpi.S(250), BackColor = Brand.Dark };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(rail, true, null);
            rail.Paint += PaintRail;

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            head = new Label { AutoSize = false, Location = new Point(Dpi.S(36), Dpi.S(26)), Size = Dpi.S(870, 36), Font = Theme.Semi(18f), ForeColor = Theme.Ink, AutoEllipsis = true };
            sub = new Label { AutoSize = false, Location = new Point(Dpi.S(36), Dpi.S(66)), Size = Dpi.S(870, 40), ForeColor = Theme.Slate, Font = Theme.Body(10f) };
            body = new Panel { Location = new Point(Dpi.S(36), Dpi.S(116)), Size = Dpi.S(870, 424) };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Dpi.S(74), BackColor = Theme.Sheet };
            bar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
            copy = new FlatButton("Copy instructions", false) { Location = new Point(Dpi.S(36), Dpi.S(18)), Size = Dpi.S(170, 38), Visible = false };
            cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(810), Dpi.S(18)), Size = Dpi.S(96, 38) };
            next = new FlatButton("Next", true) { Location = new Point(Dpi.S(500), Dpi.S(18)), Size = Dpi.S(122, 38) };
            back = new FlatButton("Back", false) { Location = new Point(Dpi.S(396), Dpi.S(18)), Size = Dpi.S(96, 38) };
            bar.Controls.AddRange(new Control[] { copy, back, next, cancel });

            // page 1: the file, then the activity
            p1 = new Panel { Dock = DockStyle.Fill };
            choose = new FlatButton("Choose the procedure file...", false) { Location = new Point(0, 0), Size = Dpi.S(240, 40), Icon = Ic.Open };
            fileLabel = new Label { AutoSize = false, Location = new Point(Dpi.S(254), Dpi.S(9)), Size = Dpi.S(616, 24), ForeColor = Theme.Slate, AutoEllipsis = true, Text = "An Excel or CSV export from Process Manager" };
            var actHolder = new Panel { Location = new Point(0, Dpi.S(56)), Size = Dpi.S(870, 316), BackColor = Color.White }; actHolder.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawRectangle(p, 0, 0, actHolder.Width - 1, actHolder.Height - 1); };
            actList = new ActivityList { Dock = DockStyle.Fill }; actHolder.Padding = new Padding(1); actHolder.Controls.Add(actList);
            hint1 = new Label { AutoSize = false, Location = new Point(0, Dpi.S(380)), Size = Dpi.S(870, 44), ForeColor = Theme.Slate, Text = "Claude captures one activity at a time. The session is named after the activity, and its steps are grouped by task." };
            p1.Controls.AddRange(new Control[] { choose, fileLabel, actHolder, hint1 });

            // page 2: what Claude does with each action, task by task
            p2 = new Panel { Dock = DockStyle.Fill };
            var colHead = new Panel { Location = new Point(0, 0), Size = Dpi.S(870, 32), BackColor = Color.White };
            colHead.Paint += delegate(object s, PaintEventArgs e)
            {
                var c = ActionList.Columns(colHead.Width - SystemInformation.VerticalScrollBarWidth); string[] names = { "Code", "Action", "Claude will", "Value to enter", "Clicks shown on screen" };
                using (var f = Theme.Semi(9f)) using (var br = new SolidBrush(Theme.Slate)) for (int i = 0; i < 5; i++) e.Graphics.DrawString(names[i], f, br, c[i], Dpi.S(8));
                using (var p = new Pen(Theme.Ink, 2f)) e.Graphics.DrawLine(p, 0, colHead.Height - 1, colHead.Width, colHead.Height - 1);
            };
            actionList = new ActionList { Location = new Point(0, Dpi.S(32)), Size = Dpi.S(870, 334) };
            hint2 = new Label { AutoSize = false, Location = new Point(0, Dpi.S(374)), Size = Dpi.S(870, 48), ForeColor = Theme.Slate, Text = "Each name in double quotes is a click, shown in the screenshots in that order. Click a value cell to type or pick what Claude enters. Right-click an action to change what Claude does with it. \"By hand\" actions are left out of the session." };
            var menu = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f) };
            foreach (var k in new[] { "click", "enter", "choose", "skip" }) { string kind = k; menu.Items.Add(k == "skip" ? "By hand" : ProcAction.KindName(k), null, delegate { SetKind(kind); }); }
            actionList.ContextMenuStrip = menu; actionList.EditValue += EditValue; actionList.MouseWheel += delegate { CloseEditor(true); };
            p2.Controls.AddRange(new Control[] { colHead, actionList, hint2 });

            body.Controls.AddRange(new Control[] { p1, p2 });
            main.Controls.AddRange(new Control[] { head, sub, body, bar });
            Controls.Add(main); Controls.Add(rail);

            choose.Click += delegate { ChooseFile(); };
            actList.SelectedIndexChanged += delegate { next.Enabled = actList.SelectedItem != null; };
            actList.DoubleClick += delegate { if (actList.SelectedItem != null) Next(); };
            back.Click += delegate { GoTo(page - 1); }; next.Click += delegate { Next(); }; cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            copy.Click += delegate { Finish(false); };
            GoTo(0);
        }

        void PaintRail(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // the app's mark, then what this window is for
            var logo = Brand.Full;
            if (logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                int lw = rail.Width - Dpi.S(56), lh = (int)(lw * (float)logo.Height / logo.Width); g.DrawImage(logo, Dpi.S(28), Dpi.S(26), lw, lh);
            }
            using (var p = new Pen(Theme.InkSoft)) g.DrawLine(p, Dpi.S(34), Dpi.S(126), rail.Width - Dpi.S(34), Dpi.S(126));
            using (var f = Theme.Semi(14f)) g.DrawString("Add a procedure", f, Brushes.White, Dpi.S(34), Dpi.S(142));
            using (var f = Theme.Body(9.75f)) using (var br = new SolidBrush(Theme.Mist)) g.DrawString("Teach Claude one activity", f, br, Dpi.S(34), Dpi.S(170));
            int y0 = Dpi.S(226), step = Dpi.S(96), d = Dpi.S(32), cx = Dpi.S(34);
            for (int i = 0; i < StepName.Length; i++)
            {
                int y = y0 + i * step; var circ = new RectangleF(cx, y, d, d); bool done = i < page, now = i == page;
                if (i < StepName.Length - 1) using (var pen = new Pen(done ? Theme.AccentSoft : Theme.InkSoft, 2f)) g.DrawLine(pen, cx + d / 2f, y + d + Dpi.S(6), cx + d / 2f, y + step - Dpi.S(6));
                if (now) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, circ);
                else if (done) { using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, circ); }
                else using (var pen = new Pen(Theme.Slate, 1.6f)) g.DrawEllipse(pen, circ);
                if (done) using (var pen = new Pen(Theme.Ink, Dpi.F(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(circ.X + d * 0.28f, circ.Y + d * 0.52f), new PointF(circ.X + d * 0.44f, circ.Y + d * 0.68f), new PointF(circ.X + d * 0.74f, circ.Y + d * 0.34f) });
                else using (var f = Theme.Semi(11f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString((i + 1).ToString(), f, now ? Brushes.Black : Brushes.LightSlateGray, circ, sf);
                using (var f = Theme.Semi(11.5f)) using (var br = new SolidBrush(now ? Color.White : done ? Color.FromArgb(220, 255, 255, 255) : Theme.Mist)) g.DrawString(StepName[i], f, br, cx + d + Dpi.S(16), y + Dpi.S(1));
                using (var f = Theme.Body(9.25f)) using (var br = new SolidBrush(now ? Theme.Mist : Theme.Slate)) g.DrawString(StepNote[i], f, br, new RectangleF(cx + d + Dpi.S(16), y + Dpi.S(26), rail.Width - cx - d - Dpi.S(36), Dpi.S(40)));
            }
        }

        void ChooseFile()
        {
            using (var f = new OpenFileDialog { Filter = "Process Manager export|*.xlsx;*.csv|All files|*.*", Title = "Choose the procedure file" })
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                LoadFile(f.FileName);
            }
        }
        internal void LoadFile(string path)
        {
            try
            {
                acts = ProcedureFile.Load(path); fileLabel.Text = Path.GetFileName(path); fileLabel.ForeColor = Theme.Ink;
                actList.Items.Clear(); foreach (var a in acts) actList.Items.Add(a);
                hint1.Text = acts.Count == 0 ? "That file has no activity with tasks and actions to capture." : acts.Count + (acts.Count == 1 ? " activity has" : " activities have") + " actions Claude can do. Choose the one to capture. The session is named after it, and its steps are grouped by task.";
                next.Enabled = false; if (acts.Count == 1) actList.SelectedIndex = 0;
            }
            catch (Exception ex) { fileLabel.Text = "Could not read that file: " + ex.Message; fileLabel.ForeColor = Theme.Mark; actList.Items.Clear(); acts.Clear(); next.Enabled = false; }
        }
        // for the self-check pictures: pick an activity and open a page
        internal void Preview(int activityIndex, int toPage)
        {
            actList.SelectedIndex = activityIndex; act = (ProcActivity)actList.Items[activityIndex];
            if (toPage >= 1) GoTo(1);
            if (toPage >= 2) { int n = 0; foreach (var t in act.Tasks) foreach (var a in t.Actions) if ((a.Kind == "enter" || a.Kind == "choose") && n++ < 3) a.Value = a.Choices.Count > 0 ? a.Choices[0] : "Sample value"; FillActions(); }
        }

        void GoTo(int n)
        {
            CloseEditor(false); page = n; p1.Visible = n == 0; p2.Visible = n == 1; back.Visible = n > 0; copy.Visible = n == 1;
            next.Width = n == 1 ? Dpi.S(176) : Dpi.S(122); next.Left = Dpi.S(906) - Dpi.S(96) - Dpi.S(8) - next.Width; back.Left = next.Left - Dpi.S(104);
            if (n == 0) { head.Text = "Choose the procedure"; sub.Text = "Pick the Process Manager export, then the one activity Claude should capture."; next.Text = "Next"; next.Enabled = actList.SelectedItem != null; }
            else if (n == 1) { head.Text = act.Title; sub.Text = "Claude clicks, enters or chooses as shown. For each action that takes a value, click its Value cell and enter it. The values are not saved in the session, only the names of the inputs, so you can run it again with other values."; next.Text = "Teach with Claude"; next.Enabled = true; FillActions(); }
            rail.Invalidate();
        }

        void FillActions()
        {
            actionList.BeginUpdate(); actionList.Items.Clear();
            foreach (var t in act.Tasks) { actionList.Items.Add(t); foreach (var a in t.Actions) actionList.Items.Add(a); }
            actionList.EndUpdate();
        }
        void SetKind(string kind)
        {
            foreach (var o in actionList.SelectedItems.Cast<object>().ToList()) { var a = o as ProcAction; if (a == null) continue; a.Kind = kind; ProcedureFile.Replan(a); }
            actionList.Invalidate();
        }

        // edit a value in place: a text box, or a list when the action has fixed options
        void EditValue(ProcAction a, Rectangle cell)
        {
            CloseEditor(true); Control c;
            if (a.Choices.Count > 0)
            {
                var cb = new ThemedCombo { Font = Theme.Body(10f) }; if (a.Optional) cb.Items.Add(""); foreach (var ch in a.Choices) cb.Items.Add(ch);
                int k = cb.Items.IndexOf(a.Value); cb.SelectedIndex = k >= 0 ? k : (a.Optional ? 0 : -1);
                cb.SelectionChangeCommitted += delegate { a.Value = cb.SelectedItem == null ? "" : cb.SelectedItem.ToString(); BeginInvoke((Action)delegate { CloseEditor(false); }); };
                c = cb;
            }
            else
            {
                var tb = new TextBox { BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body(10f), Text = a.Value };
                tb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CloseEditor(true); } else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseEditor(false); } };
                c = tb;
            }
            c.SetBounds(cell.X + 1, cell.Y + Math.Max(0, (cell.Height - c.Height) / 2), cell.Width - 2, c.Height);
            c.Tag = a; c.AccessibleName = "Value for " + a.Text; c.Leave += delegate { if (editor == c) CloseEditor(true); };
            editor = c; actionList.Controls.Add(c); c.Focus();
            var t2 = c as TextBox; if (t2 != null) t2.SelectAll();
            var cb2 = c as ComboBox; if (cb2 != null) cb2.DroppedDown = true;
        }
        void CloseEditor(bool commit)
        {
            var c = editor; if (c == null) return; editor = null;
            if (commit) { var a = c.Tag as ProcAction; var tb = c as TextBox; if (a != null && tb != null) a.Value = tb.Text.Trim(); }
            actionList.Controls.Remove(c); c.Dispose(); actionList.Invalidate();
        }

        void Next()
        {
            if (page == 0)
            {
                if (actList.SelectedItem == null) return;
                act = (ProcActivity)actList.SelectedItem; GoTo(1);
            }
            else Finish(true);
        }

        void Finish(bool launch)
        {
            CloseEditor(true); specs = ProcedureFile.Specs(act);       // sets each action's input name
            var vals = new Dictionary<string, string>();
            foreach (var t in act.Tasks) foreach (var a in t.Actions) if (a.Param.Length > 0) vals[a.Param] = a.Value.Trim();
            foreach (var t in act.Tasks) foreach (var a in t.Actions)
                if (a.Param.Length > 0 && !a.Optional && a.Value.Trim().Length == 0)
                {
                    int ix = actionList.Items.IndexOf(a); if (ix >= 0) { actionList.ClearSelected(); actionList.SelectedIndex = ix; actionList.TopIndex = Math.Max(0, ix - 2); }
                    MessageBox.Show(this, "Enter a value for \"" + (a.Field.Length > 0 ? a.Field : a.Text) + "\" (action " + a.Code + "). Click its Value cell, or choose \"Do by hand\" for that action.", "Add a procedure"); return;
                }
            // which window to capture, or the full screen, and whether the screen is ready
            var cap = CaptureTargetForm.Ask(this, "Where should Claude capture?", "Claude captures one window, or the full screen. Get the screen ready now: Claude starts as soon as you press the button.", launch ? "Start Claude" : "Make the instructions", "window", false);
            if (cap == null) return;
            try { JobPath = ProcedureFile.WriteJob(act, specs, vals, cap); }
            catch (Exception ex) { MessageBox.Show(this, "The job file could not be written: " + ex.Message, "Add a procedure"); return; }
            string msg;
            if (launch)
            {
                var owner = Owner; bool ok = ClaudeRun.Start(JobPath, out msg);
                if (!ok) { MessageBox.Show(this, msg, "Add a procedure"); return; }
                if (owner != null) owner.WindowState = FormWindowState.Minimized;      // out of the way: the status window shows where Claude is
            }
            else { RemoteControl.Start(); try { Clipboard.SetText(ClaudeLauncher.Prompt(JobPath)); } catch { } MessageBox.Show(this, "The instructions are on your clipboard. Paste them into a Claude Code session.", "Add a procedure"); }
            DialogResult = DialogResult.OK; Close();
        }
    }
}
