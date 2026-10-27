// Claude does the clicks without a terminal window: Claude Code runs hidden (print mode), and this small status window shows where
// it is, taken from the commands Claude sends to XLCapture (which action, which task, how far along). Stop ends the run and keeps
// the steps recorded so far.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SnapLite
{
    static class ClaudeRun
    {
        static Process proc; static ClaudeStatusForm form; static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static bool Running { get { return proc != null && !proc.HasExited; } }

        public static bool Start(string jobPath, out string message)
        {
            if (Running) { message = "Claude is already capturing an activity. Stop it first."; return false; }
            var exe = ClaudeLauncher.Find(); string prompt = ClaudeLauncher.Prompt(jobPath) + " Run the XLCapture commands with the Bash tool.";
            RemoteControl.Start();
            if (exe == null) { try { Clipboard.SetText(prompt); } catch { } message = "Claude Code was not found on this PC. The instructions are on your clipboard: paste them into a Claude Code session."; return false; }
            string name = "this activity"; int total = 0;
            try
            {
                var job = Json.DeserializeObject(File.ReadAllText(jobPath)) as Dictionary<string, object>;
                if (job != null)
                {
                    object v; if (job.TryGetValue("sessionName", out v) && v != null) name = Convert.ToString(v);
                    if (job.TryGetValue("tasks", out v) && v is object[])
                        foreach (var t in (object[])v) { var td = t as Dictionary<string, object>; object acts; if (td != null && td.TryGetValue("actions", out acts) && acts is object[]) foreach (var a in (object[])acts) { var ad = a as Dictionary<string, object>; if (ad != null && Convert.ToString(ad["kind"]) != "skip") total++; } }
                }
            }
            catch { }
            try
            {
                // print mode cannot ask questions, so what it may do is listed here: read files, write its request files, use skills, and run XLCapture commands; anything else is refused
                // writes only inside the working folder (acceptEdits stops at its edge), and one kind of command: this program's own, started with --cmd.
                // A wider rule such as Bash(*XLCapture.exe*) lets any command through that merely mentions the name, for example in a URL.
                string args = "-p \"" + prompt.Replace("\"", "'") + "\" --output-format stream-json --verbose --permission-mode acceptEdits --allowedTools \"Read\" \"Skill\" \"Bash(\\\"" + Application.ExecutablePath + "\\\" --cmd:*)\"";
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = ProcedureFile.JobsFolder
                };
                form = new ClaudeStatusForm(name, total); form.Show();
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                string last = ""; bool gotResult = false; var err = new StringBuilder();
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) Line(e.Data, ref last, ref gotResult); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data) && err.Length < 2000) err.AppendLine(e.Data); };
                p.Exited += delegate
                {
                    CleanScreens();
                    bool res = gotResult; string text = last, e2 = err.ToString().Trim(); string saved = RemoteControl.EndOpenSession();      // a run that stopped part-way still keeps its steps
                    var f = form; if (f != null) f.BeginInvoke((Action)delegate { f.Finish(res && !f.Failed, text, e2, saved); });
                };
                p.Start(); p.StandardInput.Close(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); proc = p;
                form.StopRequested += Stop;
                var ff = form; ff.FormClosed += delegate { if (form == ff) form = null; };
                message = "Claude is starting."; return true;
            }
            catch (Exception ex) { if (form != null) { form.Close(); form = null; } try { Clipboard.SetText(prompt); } catch { } message = "Claude Code could not start (" + ex.Message + "). The instructions are on your clipboard."; return false; }
        }

        // one line of Claude Code's output: what it is doing, and how it ended
        static void Line(string line, ref string last, ref bool gotResult)
        {
            try
            {
                var d = Json.DeserializeObject(line) as Dictionary<string, object>; if (d == null) return; string type = d.ContainsKey("type") ? Convert.ToString(d["type"]) : "";
                var f = form; if (f == null) return;
                if (type == "assistant")
                {
                    var msg = d.ContainsKey("message") ? d["message"] as Dictionary<string, object> : null; var content = msg != null && msg.ContainsKey("content") ? msg["content"] as object[] : null; if (content == null) return;
                    foreach (var c in content) { var cd = c as Dictionary<string, object>; if (cd == null) continue; if (Convert.ToString(cd["type"]) == "text") last = Convert.ToString(cd["text"]); else if (Convert.ToString(cd["type"]) == "tool_use") f.BeginInvoke((Action)(() => f.Thinking())); }
                }
                else if (type == "result")
                {
                    gotResult = true; bool isErr = d.ContainsKey("is_error") && d["is_error"] is bool && (bool)d["is_error"]; string sub = d.ContainsKey("subtype") ? Convert.ToString(d["subtype"]) : "";
                    if (d.ContainsKey("result") && d["result"] != null) last = Convert.ToString(d["result"]);
                    if (isErr || sub != "success") f.BeginInvoke((Action)(() => f.Failed = true));
                }
            }
            catch { }
        }

        // the pictures of other people's windows that "observe" leaves in the temp folder are not needed once the run is over
        static void CleanScreens()
        {
            try { string dir = Path.Combine(Path.GetTempPath(), "SnapLite"); if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "observe-*.png")) { try { File.Delete(f); } catch { } } } catch { }
        }

        public static void Stop()
        {
            var p = proc; if (p == null) return;
            try { if (!p.HasExited) { var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }); if (k != null) k.WaitForExit(4000); } } catch { try { p.Kill(); } catch { } }
        }
    }

    // the status window: no terminal, just where Claude is
    class ClaudeStatusForm : Form
    {
        readonly string session; readonly int total; readonly Label head, task, action, count, elapsed, note; readonly Panel bar; readonly FlatButton stop, open, close; readonly Timer tick = new Timer { Interval = 80 };
        readonly HashSet<string> seen = new HashSet<string>(); readonly Stopwatch clock = Stopwatch.StartNew(); int steps; bool finished, ok; float phase; string savedPath = "";
        public bool Failed; public event Action StopRequested;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);

        public ClaudeStatusForm(string session, int total)
        {
            this.session = session; this.total = total;
            Text = "ClickBot"; FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true; TopMost = true; Icon = AppIcon.Get();
            BackColor = Color.White; Font = Theme.Body(9.5f); StartPosition = FormStartPosition.Manual; ClientSize = Dpi.S(500, 292);
            var wa = Screen.PrimaryScreen.WorkingArea; Location = new Point(wa.Right - Width - Dpi.S(16), wa.Bottom - Height - Dpi.S(16));
            int m = Dpi.S(24), w = Dpi.S(500) - 2 * m;
            head = new Label { Text = "Claude is capturing " + session, AutoSize = false, Location = new Point(m, Dpi.S(20)), Size = new Size(w, Dpi.S(54)), Font = Theme.Semi(12.5f), ForeColor = Theme.Ink };
            task = new Label { Text = "Getting ready...", AutoSize = false, Location = new Point(m, Dpi.S(80)), Size = new Size(w, Dpi.S(22)), Font = Theme.Semi(10f), ForeColor = Theme.AccentInk, AutoEllipsis = true };
            action = new Label { Text = "Looking at the screen", AutoSize = false, Location = new Point(m, Dpi.S(104)), Size = new Size(w, Dpi.S(44)), ForeColor = Theme.InkSoft };
            bar = new Panel { Location = new Point(m, Dpi.S(156)), Size = new Size(w, Dpi.S(12)), BackColor = Color.White };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(bar, true, null); bar.Paint += PaintBar;
            count = new Label { AutoSize = false, Location = new Point(m, Dpi.S(176)), Size = new Size(Dpi.S(260), Dpi.S(22)), ForeColor = Theme.Slate, Font = Theme.Body(9f) };
            elapsed = new Label { AutoSize = false, Location = new Point(m + w - Dpi.S(120), Dpi.S(176)), Size = new Size(Dpi.S(120), Dpi.S(22)), ForeColor = Theme.Slate, Font = Theme.Body(9f), TextAlign = ContentAlignment.TopRight };
            note = new Label { Text = "Please leave the keyboard and mouse alone until it finishes.", AutoSize = false, Location = new Point(m, Dpi.S(206)), Size = new Size(w, Dpi.S(22)), ForeColor = Theme.Slate, Font = Theme.Body(9f) };
            stop = new FlatButton("Stop", false) { Location = new Point(Dpi.S(500) - m - Dpi.S(110), Dpi.S(240)), Size = Dpi.S(110, 38), Tone = null };
            open = new FlatButton("Open in ClickBot", true) { Location = new Point(Dpi.S(500) - m - Dpi.S(110) - Dpi.S(8) - Dpi.S(190), Dpi.S(240)), Size = Dpi.S(190, 38), Visible = false };
            close = new FlatButton("Close", false) { Location = new Point(Dpi.S(500) - m - Dpi.S(110), Dpi.S(240)), Size = Dpi.S(110, 38), Visible = false };
            stop.Click += delegate { stop.Enabled = false; stop.Text = "Stopping..."; action.Text = "Stopping and keeping the steps so far..."; if (StopRequested != null) StopRequested(); };
            open.Click += delegate { ShowClickBot(); Close(); }; close.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { head, task, action, bar, count, elapsed, note, stop, open, close });
            UpdateCount();
            RemoteControl.Progress += OnProgress; FormClosed += delegate { RemoteControl.Progress -= OnProgress; tick.Stop(); tick.Dispose(); };
            tick.Tick += delegate { phase += 0.035f; if (phase > 1f) phase -= 1f; var el = clock.Elapsed; elapsed.Text = ((int)el.TotalMinutes).ToString("00") + ":" + el.Seconds.ToString("00"); bar.Invalidate(); }; tick.Start();
            Shown += delegate { if (Environment.GetEnvironmentVariable("XLC_SHOW_STATUS") == null) { try { SetWindowDisplayAffinity(Handle, 0x11); } catch { } } };      // never part of the screenshots Claude takes
        }

        void ShowClickBot()
        {
            var h = Hub.Instance; if (h == null) return;
            h.Show(); if (h.WindowState == FormWindowState.Minimized) h.WindowState = FormWindowState.Normal; h.SetTab("bot"); h.Activate();
        }

        // the commands Claude sends to XLCapture tell us where it is
        void OnProgress(string kind, Dictionary<string, object> d)
        {
            if (IsDisposed || finished) return;
            try
            {
                BeginInvoke((Action)delegate
                {
                    if (finished) return;
                    string cmd = kind.Substring(kind.IndexOf(':') + 1); bool after = kind.StartsWith("ok:");
                    if (cmd == "observe" && !after) action.Text = "Looking at the screen";
                    else if ((cmd == "click" || cmd == "type" || cmd == "choose" || cmd == "scroll") && !after)
                    {
                        string t = Get(d, "task"), code = Get(d, "actionCode"), text = Get(d, "stepText");
                        if (t.Length > 0) task.Text = t;
                        if (code.Length > 0 && seen.Add(code)) UpdateCount();
                        action.Text = text.Length > 0 ? text : (cmd == "type" ? "Typing" : cmd == "choose" ? "Choosing an option" : cmd == "scroll" ? "Scrolling" : "Clicking");
                    }
                    else if (cmd == "session-start" && after) { note.Text = "Please leave the keyboard and mouse alone until it finishes."; }
                    else if (cmd == "session-end" && after) { action.Text = "Saving the session"; }
                    if (after && (cmd == "click" || cmd == "type" || cmd == "choose" || cmd == "scroll")) { steps++; UpdateCount(); }
                });
            }
            catch { }
        }
        static string Get(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        public void Thinking() { if (!finished && action.Text.Length == 0) action.Text = "Working it out"; }
        void UpdateCount() { count.Text = total > 0 ? "Action " + Math.Min(seen.Count, total) + " of " + total + "  ·  " + steps + (steps == 1 ? " click recorded" : " clicks recorded") : steps + (steps == 1 ? " click recorded" : " clicks recorded"); bar.Invalidate(); }

        // the run is over: say how it went
        public void Finish(bool success, string lastText, string errText, string savedPath)
        {
            if (finished) return; finished = true; ok = success; this.savedPath = savedPath ?? ""; tick.Stop();
            bool partial = !success && this.savedPath.Length > 0;
            head.Text = success ? "Claude finished " + session : stop.Text == "Stopping..." ? "Stopped" : partial ? "Claude stopped part-way" : "Claude could not finish";
            head.ForeColor = success ? Theme.Ink : Theme.Mark;
            task.Text = success ? "All done" : "";
            string detail = (lastText ?? "").Trim(); if (detail.Length == 0) detail = (errText ?? "").Trim();
            if (detail.Length > 220) detail = detail.Substring(0, 217) + "...";
            action.Text = success ? (this.savedPath.Length > 0 ? "The session is saved in ClickBot." : "Claude did not save a session. " + detail) : (detail.Length > 0 ? detail : "No message came back.") + (partial ? "\r\nThe steps so far were kept as a session." : "");
            note.Text = ""; Failed = !success; bar.Invalidate();
            stop.Visible = false; close.Visible = true; open.Visible = this.savedPath.Length > 0; ControlBox = true;
            TopMost = true; Activate();
        }

        void PaintBar(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; Theme.Backdrop(g, Color.White, bar.ClientRectangle);
            var r = new RectangleF(0, 1, bar.Width - 1, bar.Height - 2);
            using (var p = Theme.Pill(r)) using (var b = new SolidBrush(Theme.Rule)) g.FillPath(b, p);
            using (var p = Theme.Pill(r))
            {
                g.SetClip(p);
                Color c = finished && !ok ? Theme.Mark : Theme.Accent;
                if (finished) { float f = ok ? 1f : (total > 0 ? Math.Min(1f, seen.Count / (float)total) : 0.5f); using (var b = new SolidBrush(c)) g.FillRectangle(b, r.X, r.Y, r.Width * f, r.Height); }
                else if (total > 0 && seen.Count > 0) { float f = Math.Min(1f, seen.Count / (float)total); using (var b = new SolidBrush(c)) g.FillRectangle(b, r.X, r.Y, r.Width * f, r.Height); using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillRectangle(b, r.X + (r.Width * f) * phase - 30, r.Y, 30, r.Height); }
                else { float wd = r.Width * 0.28f, x = (r.Width + wd) * phase - wd; using (var b = new SolidBrush(c)) g.FillRectangle(b, x, r.Y, wd, r.Height); }       // waiting for the first action
                g.ResetClip();
            }
        }
    }
}

