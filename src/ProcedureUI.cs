// The window for choosing the procedure activity a session follows: the Process Manager export (activities > tasks > actions) and its list.
// The checklist shown while recording is in Checklist.cs.
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
    static class ChipDraw
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
    }

    // ---- the list of activities, drawn by hand so the selection and the task counts use the accent
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
            if (a.System.Length > 0) { var r = ChipDraw.Chip(g, new Point(x, y), a.System, true, sel); x = r.Right + Dpi.S(10); }
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
}
