// The two small windows around a replay: the form that asks for the input values before it starts,
// and the bar shown while it runs (kept out of every screenshot, never takes focus from the app being driven).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    class ReplayDialog : Form
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public List<Dictionary<string, string>> Rows;
        public bool Strict;
        readonly SessionDoc doc; readonly List<string> names; readonly Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>();
        readonly Label csvLabel; readonly CheckBox strictBox;

        public ReplayDialog(SessionDoc d)
        {
            doc = d; names = d.ParamNames();
            Text = "Replay session"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Paper; Font = Theme.Body(9.5f);
            int rows = Math.Min(Math.Max(names.Count, 1), 7);
            ClientSize = new Size(Dpi.S(520), Dpi.S(190 + rows * 58));

            var intro = new Label
            {
                Location = new Point(Dpi.S(18), Dpi.S(14)), Size = Dpi.S(484, 50), ForeColor = Theme.InkSoft,
                Text = names.Count == 0 ? "This session has no inputs. It will be replayed exactly as it was recorded, then saved as a new session."
                    : "XLCapture will repeat this session by itself, fast, with the values below. Enter them now: it asks for nothing once it starts. Press Pause/Break to stop it."
            };
            var panel = new Panel { Location = new Point(Dpi.S(18), Dpi.S(70)), Size = Dpi.S(484, rows * 58), AutoScroll = true };
            int y = 0;
            foreach (var n in names)
            {
                var l = new Label { Text = n.Replace('_', ' '), AutoSize = true, Location = new Point(0, y), ForeColor = Theme.Slate };
                var t = new TextBox { Location = new Point(0, y + Dpi.S(20)), Width = Dpi.S(462), BorderStyle = BorderStyle.FixedSingle, AccessibleName = n, Text = d.ParamDefault(n) ?? "" };
                panel.Controls.Add(l); panel.Controls.Add(t); boxes[n] = t; y += Dpi.S(58);
            }
            int by = Dpi.S(70 + rows * 58 + 8);
            csvLabel = new Label { Location = new Point(Dpi.S(150), by + Dpi.S(6)), Size = Dpi.S(350, 22), ForeColor = Theme.Slate, Text = "" };
            var csv = new FlatButton("Load CSV (batch)...", false) { Location = new Point(Dpi.S(18), by), Size = Dpi.S(180, 32) };
            csv.Click += delegate { LoadCsv(); };
            strictBox = new ThemedCheck { Text = "Stop if a screen is not the one that was recorded", Width = Dpi.S(420), Location = new Point(Dpi.S(18), by + Dpi.S(44)) };
            var run = new FlatButton("Run replay", true) { Location = new Point(Dpi.S(300), by + Dpi.S(80)), Size = Dpi.S(100, 34) };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(406), by + Dpi.S(80)), Size = Dpi.S(96, 34) };
            run.Click += delegate { Accepted(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.AddRange(new Control[] { intro, panel, csv, csvLabel, strictBox, run, cancel });
            AcceptButton = run; CancelButton = cancel;
            ClientSize = new Size(ClientSize.Width, by + Dpi.S(80 + 34 + 16));
        }

        void LoadCsv()
        {
            using (var f = new OpenFileDialog { Filter = "CSV file|*.csv|All files|*.*", Title = "A CSV with a header row naming the inputs, then one row per run" })
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var rows = Replayer.ParseCsv(f.FileName);
                    if (rows.Count == 0) { MessageBox.Show(this, "That file has no data rows. The first row must name the inputs, the rows after it hold the values.", "Replay"); return; }
                    var missing = names.Where(n => doc.ParamDefault(n) == null && !rows[0].ContainsKey(n)).ToList();
                    if (missing.Count > 0) { MessageBox.Show(this, "The CSV has no column for: " + string.Join(", ", missing.ToArray()), "Replay"); return; }
                    Rows = rows; csvLabel.Text = rows.Count + (rows.Count == 1 ? " run" : " runs") + " from " + Path.GetFileName(f.FileName);
                    foreach (var b in boxes.Values) b.Enabled = false;
                }
                catch (Exception ex) { MessageBox.Show(this, "Could not read that file: " + ex.Message, "Replay"); }
            }
        }
        void Accepted()
        {
            foreach (var kv in boxes) Values[kv.Key] = kv.Value.Text;
            Strict = strictBox.Checked; DialogResult = DialogResult.OK; Close();
        }
    }

    class ReplayBar : Form
    {
        readonly Label text, sub; readonly FlatButton stop; readonly System.Windows.Forms.Timer blink = new System.Windows.Forms.Timer { Interval = 600 }; bool on = true; readonly Panel dot;

        readonly StepRibbon ribbon; string note = "";

        public static ReplayBar Open()
        {
            var hub = Hub.Instance; if (hub == null || hub.IsDisposed || !hub.IsHandleCreated) return null;
            ReplayBar b = null;
            try { hub.Invoke((Action)delegate { b = new ReplayBar(); b.Show(); }); } catch { return null; }
            return b;
        }
        // a new run starts: empty ribbon, and a note such as "Run 2 of 3" when several runs share the bar
        public void Begin(int total, string runNote)
        {
            try { if (IsHandleCreated) BeginInvoke((Action)delegate { note = string.IsNullOrEmpty(runNote) ? "" : runNote + ", "; ribbon.Reset(total); text.Text = "Replaying"; sub.Text = ""; }); } catch { }
        }
        // state: 1 running, 2 done, 3 done with a warning, 4 failed
        public void Step(int i, int n, string step, int state)
        {
            try
            {
                if (IsHandleCreated) BeginInvoke((Action)delegate
                {
                    ribbon.Set(i, state);
                    if (state == 1) { text.Text = note + "step " + i + " of " + n; sub.Text = step; }
                });
            }
            catch { }
        }
        public void CloseBar() { try { if (IsHandleCreated) BeginInvoke((Action)Close); } catch { } }

        ReplayBar()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Ink;
            Size = Dpi.S(470, 82);
            var wa = Screen.PrimaryScreen.WorkingArea; Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Bottom - Height - Dpi.S(16));
            ribbon = new StepRibbon { Location = new Point(Dpi.S(16), Dpi.S(62)), Size = Dpi.S(438, 8), BackColor = Theme.Ink, OnDark = true };
            dot = new Panel { Location = new Point(Dpi.S(16), Dpi.S(23)), Size = Dpi.S(16, 16), BackColor = Theme.Ink };
            dot.Paint += delegate(object s, PaintEventArgs e) { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using (var b = new SolidBrush(on ? Theme.Mark : Color.FromArgb(120, Theme.Mark))) e.Graphics.FillEllipse(b, 0, 0, dot.Width - 1, dot.Height - 1); };
            text = new Label { Location = new Point(Dpi.S(40), Dpi.S(8)), Size = Dpi.S(290, 22), ForeColor = Color.White, Font = Theme.BodyBold(10.5f), AccessibleRole = AccessibleRole.StatusBar };
            sub = new Label { Location = new Point(Dpi.S(40), Dpi.S(32)), Size = Dpi.S(290, 22), ForeColor = Theme.Mist, Font = Theme.Body(9f) };
            stop = new FlatButton("Stop (Pause)", true) { Location = new Point(Dpi.S(336), Dpi.S(14)), Size = Dpi.S(124, 34), OnDark = true };
            stop.FlatAppearance.BorderColor = Theme.Mark; stop.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xC6, 0x34, 0x1E);
            stop.Click += delegate { Replayer.Cancel(); text.Text = "Stopping..."; };
            Controls.AddRange(new Control[] { dot, text, sub, stop, ribbon });
            blink.Tick += delegate { on = !on; dot.Invalidate(); };
            Shown += delegate { Native.ExcludeFromCapture(Handle); blink.Start(); };
            FormClosed += delegate { blink.Dispose(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }
        protected override void OnPaint(PaintEventArgs e) { using (var p = new Pen(Theme.InkSoft)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }
}
