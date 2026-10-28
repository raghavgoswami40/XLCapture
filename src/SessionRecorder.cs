// Session recording, modelled on ClickTrail's desktop recorder (src/recorder.js, studied with REA):
//  - a global hook sees every left click, wheel turn and keystroke in any app;
//  - each click's screenshot is taken inside the hook, before the click reaches the app, so it shows the screen as it was;
//  - Windows UI Automation names and outlines the control that was clicked (on a background thread);
//  - steps on the same unchanged screen share one screenshot ("screens"); typing earns an "after" screenshot;
//  - scrolls become their own steps, and XLCapture's own windows are never recorded.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace SnapLite
{
    static class SessionRecorder
    {
        public static bool Active, Paused;
        public static int Count { get { lock (gate) return doc == null ? 0 : doc.Steps.Count; } }
        static readonly object gate = new object();
        static SessionDoc doc;
        static string target = "all"; static Rectangle fixedArea; static IntPtr targetHwnd; static int targetPid; static int areaDx, areaDy;
        static IntPtr mouseHook, kbHook; static Native.HookProc mouseProc, kbProc;
        static Thread worker; static BlockingCollection<Action> queue;
        static SessionBar bar;
        // following a procedure: the activity's tasks and their actions. The checklist shows one task at a time; the person moves on with Next task
        // and ticks the actions by hand. Nothing is ticked by a click.
        public static ProcActivity Activity;
        static List<ProcAction> plan; static List<ProcTask> tasks; static int taskPos; static string lastAction = "", lastTask = ""; static bool[] done, used; static ChecklistForm checklist; static RecordingPanel recPanel;
        // what was recorded last, for the recording panel
        internal static string LastStep { get { lock (gate) { if (doc == null || doc.Steps.Count == 0) return ""; return doc.Steps[doc.Steps.Count - 1].Plain; } } }
        internal static bool IsDone(int i) { lock (gate) return done != null && i >= 0 && i < done.Length && done[i]; }
        internal static int DoneCount { get { lock (gate) { int n = 0; if (done != null) foreach (var d in done) if (d) n++; return n; } } }
        internal static int PlanCount { get { return plan == null ? 0 : plan.Count; } }
        internal static List<ProcTask> Tasks { get { return tasks; } }
        internal static int TaskCount { get { return tasks == null ? 0 : tasks.Count; } }
        internal static int TaskPos { get { lock (gate) return taskPos; } }
        static void RefreshBars()
        {
            var b = bar; if (b != null) { try { b.BeginInvoke((Action)b.Refresh2); } catch { } }
            var c = checklist; if (c != null) { try { c.BeginInvoke((Action)c.Refresh2); } catch { } }
            var r = recPanel; if (r != null) { try { r.BeginInvoke((Action)r.Refresh2); } catch { } }
        }
        static void StartPlan(ProcActivity act)
        {
            tasks = act.Tasks.Where(x => x.Actions.Count > 0).ToList(); plan = tasks.SelectMany(x => x.Actions).ToList();
            if (plan.Count == 0) { tasks = null; plan = null; done = used = null; return; }
            done = new bool[plan.Count]; used = new bool[plan.Count]; taskPos = 0; lastAction = ""; lastTask = "";
        }
        // the plan without the hooks, for the checks in tools\verify.ps1
        internal static void BeginPlan(ProcActivity act) { StartPlan(act); }
        // The task and action a step belongs to. The task is the one shown on the checklist. The action: a click whose label is in the text of an action of that task not yet used,
        // else the first one not yet used; a scroll goes with the last action used. This only labels the step: it ticks nothing.
        // Returns true when the step starts another task (so it starts a new screen).
        internal static bool Tag(SessionStep s)
        {
            if (plan == null || tasks == null) return false;
            ProcTask tk; ProcAction pa;
            lock (gate)
            {
                tk = tasks[Math.Max(0, Math.Min(taskPos, tasks.Count - 1))]; int start = 0; for (int i = 0; i < tasks.IndexOf(tk); i++) start += tasks[i].Actions.Count;
                int n = tk.Actions.Count, use = -1;
                if (s.Kind != "scroll")
                {
                    for (int j = 0; j < n && use < 0; j++) if (!used[start + j] && LooksLike(tk.Actions[j], s)) use = j;
                    for (int j = 0; j < n && use < 0; j++) if (!used[start + j]) use = j;
                    if (use < 0) use = n - 1;
                    used[start + use] = true;
                }
                else { for (int j = 0; j < n; j++) if (used[start + j]) use = j; if (use < 0) use = 0; }
                pa = tk.Actions[use];
            }
            // the step carries its task and action (the checklist and the grouping use them); its text stays the one made from what was clicked
            string name = tk.Full; s.Task = name; s.ActionCode = pa.Code; lastAction = pa.Code;
            bool change = name != lastTask; lastTask = name; return change;
        }
        // does what was clicked (its label) appear in the action's text?
        static bool LooksLike(ProcAction a, SessionStep s)
        {
            string label = (s.Label ?? "").Trim().ToLowerInvariant(); if (label.Length < 3) return false;
            string text = (a.Text ?? "").ToLowerInvariant(); if (text.Contains(label)) return true;
            var words = System.Text.RegularExpressions.Regex.Split(label, @"[^\p{L}\p{N}]+").Where(w => w.Length >= 3).ToList(); if (words.Count == 0) return false;
            int hit = words.Count(w => text.Contains(w)); return hit >= 1 && hit * 2 >= words.Count;
        }
        internal static void RestartForTest(ProcActivity act) { Activity = act; InitRecording(null); }
        internal static void ToggleDoneForTest(int i) { lock (gate) done[i] = !done[i]; }
        internal static void SetTaskForTest(int i) { lock (gate) taskPos = i; }
        internal static void ResumeForTest(ProcActivity act, SessionDoc d) { doc = d; StartPlan(act); ResumePlan(); }
        // ticking is by hand
        public static void ToggleDone(int i) { if (!Active || plan == null) return; Post(delegate { lock (gate) { if (i >= 0 && i < done.Length) done[i] = !done[i]; } RefreshBars(); }); }
        // Next task / Previous task / a task's panel. Queued with the clicks, so a click made just before the move stays with the old task.
        public static void SetTask(int i) { if (!Active || tasks == null) return; Post(delegate { lock (gate) taskPos = Math.Max(0, Math.Min(tasks.Count - 1, i)); RefreshBars(); }); }
        public static void MoveTask(int d) { if (!Active || tasks == null) return; Post(delegate { lock (gate) taskPos = Math.Max(0, Math.Min(tasks.Count - 1, taskPos + d)); RefreshBars(); }); }
        static readonly int ownPid = Process.GetCurrentProcess().Id;
        static readonly Dictionary<int, string> procNames = new Dictionary<int, string>();

        // screen grouping
        static Bitmap groupFrame; static Rectangle groupArea; static readonly List<Rectangle> anchors = new List<Rectangle>(); static string lastWindowKey;
        // typing
        static SessionStep dirty; static Rectangle dirtyArea; static string lastStepRid;
        // scrolling
        class Gesture { public string Dir; public Point Pt; public Rectangle Area; public Bitmap Frame; public IntPtr Root; public int Pid; }
        static Gesture gesture; static string lastScrollDir; static System.Threading.Timer scrollTimer;
        const int ScrollSettleMs = 650;

        // ------------------------------------------------------------ start / stop
        public static void Start(string mode, Rectangle area, IntPtr window)
        {
            if (Active) return;
            target = mode; fixedArea = area; targetHwnd = window; targetPid = window == IntPtr.Zero ? 0 : Native.ProcessIdOf(window);
            areaDx = areaDy = 0;
            if (target == "area" && window != IntPtr.Zero) { var wb = Native.WindowBounds(window); areaDx = area.X - wb.X; areaDy = area.Y - wb.Y; }     // where the area sits in its window
            // continuing a saved session: its steps stay, the new ones follow on a new screen
            resumePath = Resume != null ? Resume.Path : null;
            var startDoc = Resume; Resume = null; picking = false;
            InitRecording(startDoc);
            queue = new BlockingCollection<Action>();
            worker = new Thread(Work) { IsBackground = true, Name = "session-worker" }; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            scrollTimer = new System.Threading.Timer(delegate { Post(EndScroll); }, null, Timeout.Infinite, Timeout.Infinite);
            mouseProc = MouseProc; kbProc = KeyProc;
            var mod = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, mod, 0);
            kbHook = Native.SetWindowsHookEx(13, kbProc, mod, 0);
            if (mouseHook == IntPtr.Zero) { Hub.Toast("Session recording could not start", "Windows refused the mouse hook."); Cleanup(); return; }
            Active = true; Paused = false;
            var barArea = area.IsEmpty ? Screen.FromPoint(Cursor.Position).Bounds : area;
            // On another monitor, when there is one (never the one being recorded): with a procedure the checklist, without one the recording panel. Both show the step count
            // and have Pause and Stop. With one monitor only, the small recording bar has them. All are laid out in the startup scale and stretched by Windows.
            Screen other = Screen.AllScreens.Where(sc => !sc.Bounds.IntersectsWith(Rectangle.Inflate(Screen.FromRectangle(barArea).Bounds, -2, -2))).FirstOrDefault();
            if (other != null && plan != null) Native.WithDpi(false, delegate { checklist = new ChecklistForm(Activity, other); checklist.Show(); });
            else if (other != null) Native.WithDpi(false, delegate { recPanel = new RecordingPanel(other); recPanel.Show(); });
            else Native.WithDpi(false, delegate { bar = new SessionBar(barArea, plan != null); bar.Show(); });
            Hub.Toast("Recording a session", "Use the app you are documenting. Each click becomes a step. Press " + HotkeyText() + " or Stop to finish.");
        }
        // the state of a recording at its beginning: a new session (or the saved one being continued), no screen, no typing, the procedure's checklist unticked
        static void InitRecording(SessionDoc resumeDoc)
        {
            bool resuming = resumeDoc != null;
            doc = resuming ? resumeDoc : new SessionDoc { Id = SessionDoc.NewId(), StartedAt = DateTime.Now, Name = "Session " + DateTime.Now.ToString("yyyy-MM-dd HH.mm") };
            if (!resuming) SessionLookForm.ApplyDefaultPreset(doc);      // a new recording starts with the preset the person set as the default
            if (groupFrame != null) { groupFrame.Dispose(); } groupFrame = null; groupArea = Rectangle.Empty; anchors.Clear(); lastWindowKey = null; dirty = null; lastStepRid = null; gesture = null; lastScrollDir = null;
            plan = null; tasks = null; done = used = null; taskPos = 0; lastAction = ""; lastTask = "";
            if (Activity != null)
            {
                StartPlan(Activity);
                if (plan != null && !resuming) { doc.Name = Activity.Title + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm"); doc.Procedure = ProcedureFile.ProcedureObject(Activity, new List<InputSpec>()); doc.Procedure["source"] = Settings.SesProcFile; }
                else if (plan != null) ResumePlan();
            }
        }
        static string resumePath;        // the saved session being continued, if any: starting again goes back to it as it was saved
        // Ask before throwing away what was recorded. Clicks and keys made while the question is open are not recorded.
        static bool AskToDiscard(string title, string text, string okText)
        {
            if (Count == 0) return true;      // nothing recorded yet: nothing to lose
            picking = true;
            try { return Dialogs.ConfirmAction(null, title, text, okText, "Keep recording"); } finally { picking = false; }
        }
        // Start the recording again: the steps so far are thrown away and the next click is step 1. The window being recorded, and the checklist, stay as they are.
        public static void Restart()
        {
            if (!Active || picking) return;
            int n = Count;
            if (!AskToDiscard("Start this recording again?", "The " + n + (n == 1 ? " step" : " steps") + " recorded so far will be thrown away" + (resumePath != null ? " (the saved session goes back to how it was)" : "") + ", and your next click will be step 1.", "Start again")) return;
            var reset = new ManualResetEvent(false);
            Post(delegate { try { lock (gate) { if (scrollTimer != null) scrollTimer.Change(Timeout.Infinite, Timeout.Infinite); InitRecording(resumePath != null ? SessionDoc.Load(resumePath) : null); } } finally { reset.Set(); } });
            reset.WaitOne(5000); RefreshBars();
            Hub.Toast("Recording started again", "The earlier steps were thrown away. Your next click is step 1.");
        }
        // Stop recording and throw it away: nothing is saved and the editor does not open. A saved session being continued stays as it was saved.
        public static void Cancel()
        {
            if (!Active || picking) return;
            int n = Count;
            if (!AskToDiscard("Cancel this recording?", "The " + n + (n == 1 ? " step" : " steps") + " recorded so far will be thrown away. Nothing is saved.", "Cancel recording")) return;
            Active = false; Paused = false;
            Unhook();
            if (queue != null) queue.CompleteAdding();
            if (worker != null) worker.Join(3000);
            if (bar != null) { bar.Close(); bar = null; }
            Cleanup();
            if (Hub.Instance != null) { Hub.Instance.UpdateCaptureButton(); Hub.Instance.RestoreAfterSession(); }
            Hub.Toast("Recording cancelled", "Nothing was saved.");
        }
        // A saved session to carry on with: set before Start. The procedure's place is found again from the steps already recorded: the task of the last step is the open
        // page, and the actions those steps used are used (and ticked) again.
        internal static SessionDoc Resume;
        static void ResumePlan()
        {
            int pos = 0; string lastCode = "", lastT = "";
            foreach (var s in doc.Steps)
            {
                if (string.IsNullOrEmpty(s.Task)) continue;
                int ti = tasks.FindIndex(t => t.Full == s.Task); if (ti < 0) continue;
                pos = ti; lastT = s.Task;
                if (!string.IsNullOrEmpty(s.ActionCode))
                {
                    int start = 0; for (int i = 0; i < ti; i++) start += tasks[i].Actions.Count;
                    int j = tasks[ti].Actions.FindIndex(a => a.Code == s.ActionCode); if (j >= 0) { used[start + j] = true; done[start + j] = true; lastCode = s.ActionCode; }
                }
            }
            taskPos = pos; lastAction = lastCode; lastTask = lastT;
        }
        // ---- changing the window being recorded, in the middle of a task. The pick screen is the one used to start; the clicks made while choosing are not recorded.
        static volatile bool picking;
        public static void SwitchWindow() { if (Active && !picking && Hub.Instance != null) Hub.Instance.SwitchTarget(); }
        internal static void BeginSwitch() { picking = true; Post(delegate { EndScroll(); CommitTyping(true); }); }
        internal static void EndSwitch() { picking = false; Post(delegate { lastWindowKey = null; }); RefreshBars(); }
        internal static void ApplyTarget(string mode, Rectangle area, IntPtr window)
        {
            target = mode; fixedArea = area; targetHwnd = window; targetPid = window == IntPtr.Zero ? 0 : Native.ProcessIdOf(window);
            areaDx = areaDy = 0;
            if (target == "area" && window != IntPtr.Zero) { var wb = Native.WindowBounds(window); areaDx = area.X - wb.X; areaDy = area.Y - wb.Y; }
            Post(delegate { groupArea = Rectangle.Empty; });
        }
        internal static string TargetName
        {
            get
            {
                if (target == "area" && targetHwnd != IntPtr.Zero) { var t = Native.TitleOf(targetHwnd); return string.IsNullOrEmpty(t) ? ProcName(targetPid) : t; }
                if (target == "monitor") return "a whole screen";
                if (target == "area") return "a part of the screen";
                return "every screen";
            }
        }
        static string HotkeyText() { string k; return Settings.Hotkeys.TryGetValue("session", out k) && k.Length > 0 ? k : "Stop"; }

        public static void TogglePause()
        {
            if (!Active) return; Paused = !Paused;
            if (!Paused) Post(delegate { lastWindowKey = null; });     // the next step starts a fresh screen, like ClickTrail's resume
            RefreshBars();
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false; Paused = false;
            Unhook();
            var done = new ManualResetEvent(false);
            Post(delegate { EndScroll(); CommitTyping(true); done.Set(); });
            queue.CompleteAdding();
            done.WaitOne(8000); worker.Join(3000);
            if (bar != null) { bar.Close(); bar = null; }
            if (checklist != null) { checklist.Close(); checklist = null; }
            if (recPanel != null) { recPanel.Close(); recPanel = null; }
            var finished = doc; Cleanup();
            if (Hub.Instance != null) { Hub.Instance.UpdateCaptureButton(); Hub.Instance.RestoreAfterSession(); }       // the capture window comes back first, so the editor that opens next is the one in front
            if (finished == null || finished.Steps.Count == 0) { Hub.Toast("Nothing recorded", "No clicks landed in the area being recorded."); return; }
            finished.Renumber();
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                string path = !string.IsNullOrEmpty(finished.Path) && File.Exists(finished.Path) ? finished.Path : finished.DefaultPath(Settings.Folder); finished.Save(path);
                LibraryStore.Touch(path);
                if (Hub.Instance != null) Hub.Instance.RefreshRecent();
                if (Settings.SesOpenIn == "folder") Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else EditorForm.ShowSession(path);
                Hub.Toast("Session saved", finished.Steps.Count + " steps on " + finished.Groups().Count + " screens");
            }
            catch (Exception ex) { MessageBox.Show("The session could not be saved: " + ex.Message, "XLCapture"); }
        }
        static void Unhook()
        {
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            if (kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(kbHook); kbHook = IntPtr.Zero; }
        }
        static void Cleanup()
        {
            Unhook(); Active = false;
            if (scrollTimer != null) { scrollTimer.Dispose(); scrollTimer = null; }
            if (groupFrame != null) { groupFrame.Dispose(); groupFrame = null; }
            if (checklist != null) { try { checklist.Close(); } catch { } checklist = null; }
            if (recPanel != null) { try { recPanel.Close(); } catch { } recPanel = null; }
            plan = null; tasks = null; done = used = null; Activity = null; doc = null; Resume = null; picking = false;
        }
        static void Post(Action a) { var q = queue; if (q == null) return; try { if (!q.IsAddingCompleted) q.Add(a); } catch (InvalidOperationException) { } }
        static void Work()
        {
            try { foreach (var a in queue.GetConsumingEnumerable()) { try { a(); } catch (Exception ex) { Debug.WriteLine(ex); } } } catch { }
        }

        // ------------------------------------------------------------ where a step is captured
        static Rectangle AreaFor(Point p)
        {
            switch (target)
            {
                case "monitor": case "region": return fixedArea.Contains(p) ? fixedArea : Rectangle.Empty;
                case "area":
                {
                    var r = fixedArea;
                    if (targetHwnd != IntPtr.Zero)      // a window (or a pane of it) was picked: the area goes with the window
                    {
                        if (Native.IsMinimized(targetHwnd)) return Rectangle.Empty;
                        var wb = Native.WindowBounds(targetHwnd); r = new Rectangle(wb.X + areaDx, wb.Y + areaDy, fixedArea.Width, fixedArea.Height);
                    }
                    return r.Contains(p) ? r : Rectangle.Empty;
                }
                case "window":
                    if (Native.IsMinimized(targetHwnd)) return Rectangle.Empty;
                    var w = Native.WindowBounds(targetHwnd); return w.Contains(p) ? w : Rectangle.Empty;
                default: return Screen.FromPoint(p).Bounds;
            }
        }
        static bool Accept(IntPtr root, int pid)
        {
            if (pid == ownPid) return false;                                              // SnapLite itself
            if ((target == "window" || (target == "area" && targetHwnd != IntPtr.Zero)) && root != targetHwnd && pid != targetPid) return false; // something drawn over the chosen window
            return true;
        }
        static string ProcName(int pid)
        {
            string n; if (procNames.TryGetValue(pid, out n)) return n;
            try { n = Process.GetProcessById(pid).ProcessName; } catch { n = "unknown"; }
            procNames[pid] = n; return n;
        }

        // ------------------------------------------------------------ hooks (UI thread; keep them fast)
        static IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Active && !Paused && !picking)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    if (msg == 0x201 || msg == 0x20A || msg == 0x20E)
                    {
                        var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                        var pt = new Point(info.pt.X, info.pt.Y);
                        if (msg == 0x201) OnClick(pt);
                        else OnWheel(pt, (short)((info.mouseData >> 16) & 0xffff), msg == 0x20E);
                    }
                }
                catch { }
            }
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
        static void OnClick(Point pt)
        {
            var root = Native.RootAt(pt); int pid = Native.ProcessIdOf(root);
            if (!Accept(root, pid)) return;
            var area = AreaFor(pt); if (area.IsEmpty) return;
            var frame = Native.Grab(area, false);      // before the app sees the click
            Post(delegate { HandleClick(pt, area, frame, root, pid); });
        }
        static void OnWheel(Point pt, short delta, bool horizontal)
        {
            string dir = horizontal ? (delta > 0 ? "right" : "left") : (delta > 0 ? "up" : "down");
            lock (gate)
            {
                if (gesture != null) { gesture.Dir = dir; scrollTimer.Change(ScrollSettleMs, Timeout.Infinite); return; }
            }
            var root = Native.RootAt(pt); int pid = Native.ProcessIdOf(root);
            if (!Accept(root, pid)) return;
            var area = AreaFor(pt); if (area.IsEmpty) return;
            var g = new Gesture { Dir = dir, Pt = pt, Area = area, Frame = Native.Grab(area, false), Root = root, Pid = pid };
            lock (gate) gesture = g;
            scrollTimer.Change(ScrollSettleMs, Timeout.Infinite);
        }
        static IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Active && !Paused && !picking)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x100 || msg == 0x104)
                {
                    var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int vk = k.vkCode;
                    // Ctrl+Shift+W: change the window being recorded
                    if (vk == 87 && (Native.GetAsyncKeyState(17) & 0x8000) != 0 && (Native.GetAsyncKeyState(16) & 0x8000) != 0) { var h = Hub.Instance; if (h != null) h.BeginInvoke((Action)SwitchWindow); return (IntPtr)1; }
                    // Ctrl+Shift+Right / Left: next / previous task of the procedure
                    if (plan != null && (vk == 39 || vk == 37) && (Native.GetAsyncKeyState(17) & 0x8000) != 0 && (Native.GetAsyncKeyState(16) & 0x8000) != 0) { MoveTask(vk == 39 ? 1 : -1); return (IntPtr)1; }
                    bool modifierKey = vk == 16 || vk == 17 || vk == 18 || vk == 91 || vk == 92 || (vk >= 160 && vk <= 165);
                    // shortcuts (Ctrl/Alt/Win held) are commands, not text - including SnapLite's own hotkeys
                    bool chord = (Native.GetAsyncKeyState(17) & 0x8000) != 0 || (Native.GetAsyncKeyState(18) & 0x8000) != 0 || (Native.GetAsyncKeyState(91) & 0x8000) != 0 || (Native.GetAsyncKeyState(92) & 0x8000) != 0;
                    if (!modifierKey && !chord && Settings.SesTyping) Post(HandleKey);
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // ------------------------------------------------------------ worker
        internal class El { public string Name = "", Role = "", Id = "", Rid, ClassName = "", Path = ""; public Rectangle Bounds; }
        static El Inspect(Point pt)
        {
            try
            {
                var a = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
                var e = From(a); e.Path = Ui.AncestorPath(a); return e;
            }
            catch { return new El(); }
        }
        internal static El From(AutomationElement a)
        {
            var e = new El(); if (a == null) return e;
            try
            {
                var c = a.Current;
                e.Name = c.Name ?? ""; e.Id = c.AutomationId ?? ""; e.ClassName = c.ClassName ?? "";
                var pn = c.ControlType != null ? c.ControlType.ProgrammaticName : ""; e.Role = pn.StartsWith("ControlType.") ? pn.Substring(12) : pn;
                var r = c.BoundingRectangle; if (!r.IsEmpty && !double.IsInfinity(r.Width)) e.Bounds = Rectangle.Round(new RectangleF((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height));
                var rid = a.GetRuntimeId(); e.Rid = rid == null ? null : string.Join(".", rid);
            }
            catch { }
            return e;
        }
        // ClickTrail's rule: trust the element's own bounds only when they are plausible for ONE control containing the click
        internal static Rectangle BoxFor(El el, Point pt, Rectangle area)
        {
            var b = el.Bounds; bool ok = b.Width >= 4 && b.Height >= 4;
            if (ok)
            {
                int tol = 3; if (pt.X < b.X - tol || pt.X > b.Right + tol || pt.Y < b.Y - tol || pt.Y > b.Bottom + tol) ok = false;
            }
            if (ok)
            {
                double frac = (double)b.Width * b.Height / ((double)area.Width * area.Height);
                bool field = el.Role == "Edit" || el.Role == "ComboBox" || el.Role == "Spinner" || el.Role == "Document";
                bool container = new[] { "Pane", "Window", "Group", "Custom", "Tab", "Table", "DataGrid", "List", "Tree", "ToolBar", "MenuBar", "StatusBar" }.Contains(el.Role);
                bool tooBig = field ? (frac > 0.35 || b.Width > area.Width * 0.95 || b.Height > area.Height * 0.6)
                    : container ? (frac > 0.04 || b.Width > area.Width * 0.5 || b.Height > area.Height * 0.35)
                    : (frac > 0.10 || b.Height > area.Height * 0.4);
                if (!tooBig) { var r = b; r.Offset(-area.X, -area.Y); r.Intersect(new Rectangle(0, 0, area.Width, area.Height)); if (r.Width >= 4 && r.Height >= 4) return r; }
            }
            int w = (int)Math.Round(Math.Min(Math.Max(area.Width * 0.03, 36), 120)), h = (int)Math.Round(Math.Min(Math.Max(area.Width * 0.02, 28), 80));
            int bx = Math.Min(Math.Max(pt.X - area.X - w / 2, 0), Math.Max(0, area.Width - w)), by = Math.Min(Math.Max(pt.Y - area.Y - h / 2, 0), Math.Max(0, area.Height - h));
            return new Rectangle(bx, by, w, h);
        }
        // has the screen under the earlier steps of this screen changed? then the next step needs a new picture
        static bool AnchorsIntact(Bitmap frame) { return AnchorsIntact(groupFrame, anchors, frame); }
        internal static bool AnchorsIntact(Bitmap groupFrame, List<Rectangle> anchors, Bitmap frame)
        {
            if (groupFrame == null || groupFrame.Size != frame.Size) return false;
            foreach (var a in anchors)
            {
                var r = Rectangle.Inflate(a, 2, 2); r.Intersect(new Rectangle(Point.Empty, frame.Size)); if (r.Width < 2 || r.Height < 2) continue;
                using (var x = Effects.Crop(groupFrame, r)) using (var y = Effects.Crop(frame, r))
                {
                    int s1, s2; var bx = Effects.Bytes(x, out s1); var by = Effects.Bytes(y, out s2); long sum = 0, n = 0;
                    for (int i = 0; i + 2 < bx.Length && i + 2 < by.Length; i += 12) { sum += Math.Abs(bx[i] - by[i]) + Math.Abs(bx[i + 1] - by[i + 1]) + Math.Abs(bx[i + 2] - by[i + 2]); n += 3; }
                    if (n > 0 && sum / (double)n > 14) return false;
                }
            }
            return true;
        }
        // push a step; decides whether it starts a new screen. Takes ownership of frame.
        static SessionStep Push(SessionStep s, Bitmap frame, Rectangle area, string windowKey)
        {
            bool newScreen, taskChange = false;
            lock (gate) newScreen = doc.Steps.Count == 0;
            taskChange = Tag(s);
            newScreen = newScreen || taskChange || windowKey != lastWindowKey || area != groupArea || !AnchorsIntact(frame);
            if (newScreen)
            {
                if (groupFrame != null) groupFrame.Dispose();
                groupFrame = frame; groupArea = area; anchors.Clear();
                s.Frame = SessionDoc.Encode(frame);
            }
            else s.Frame = SessionDoc.EncodeJpeg(frame);      // kept so the editor can start a new screenshot from this step
            s.BreakBefore = newScreen; s.Vw = area.Width; s.Vh = area.Height;
            anchors.Add(s.Box); lastWindowKey = windowKey;
            lock (gate) { doc.Steps.Add(s); s.Seq = doc.Steps.Count; s.Id = doc.Id + "_" + s.Seq.ToString("00000"); }
            RefreshBars();
            return s;
        }

        static void HandleClick(Point pt, Rectangle area, Bitmap frame, IntPtr root, int pid)
        {
            if (doc == null) { frame.Dispose(); return; }
            EndScroll(); lastScrollDir = null;
            // typing into the previous field is finished: this click's picture shows the result
            if (dirty != null) { dirty.Typed = true; dirty.AfterFrame = SessionDoc.Encode(frame); dirty = null; }
            var el = Inspect(pt);
            string title = Native.TitleOf(root), proc = ProcName(pid);
            var s = new SessionStep
            {
                Kind = "click", Ts = Stamp(), X = pt.X - area.X, Y = pt.Y - area.Y, Box = BoxFor(el, pt, area),
                Url = "app://" + proc + "/" + title, Title = title, Label = el.Name, Role = el.Role, TestId = el.Id,
                ClassName = el.ClassName, AncestorPath = el.Path, UrlBar = Ui.UrlBarOf(root, proc)
            };
            bool keep = false;
            var pushed = Push(s, frame, area, proc + "::" + title);
            keep = ReferenceEquals(groupFrame, frame);
            if (!keep) frame.Dispose();
            lastStepRid = el.Rid; dirtyArea = area;
        }
        static void HandleKey()
        {
            if (doc == null || dirty != null) return;
            AutomationElement f; try { f = AutomationElement.FocusedElement; } catch { return; }
            var el = From(f);
            if (el.Role != "Edit" && el.Role != "Document" && el.Role != "ComboBox" && el.Role != "Spinner") return;
            SessionStep last; lock (gate) last = doc.Steps.Count > 0 ? doc.Steps[doc.Steps.Count - 1] : null;
            if (el.Rid != null && el.Rid == lastStepRid && last != null) { last.Typed = true; dirty = last; return; }   // typing into the field just clicked
            // tabbed into a field without clicking it: a keyboard step of its own
            var root = Native.Root(Native.GetForegroundWindow()); int pid = Native.ProcessIdOf(root);
            if (!Accept(root, pid)) return;
            var c = el.Bounds.IsEmpty ? Cursor.Position : new Point(el.Bounds.X + el.Bounds.Width / 2, el.Bounds.Y + el.Bounds.Height / 2);
            var area = AreaFor(c); if (area.IsEmpty) return;
            var frame = Native.Grab(area, false);
            string title = Native.TitleOf(root), proc = ProcName(pid);
            var s = new SessionStep { Kind = "click", Keyboard = true, Typed = true, Ts = Stamp(), X = c.X - area.X, Y = c.Y - area.Y, Box = BoxFor(el, c, area), Url = "app://" + proc + "/" + title, Title = title, Label = el.Name, Role = el.Role, TestId = el.Id };
            Push(s, frame, area, proc + "::" + title);
            if (!ReferenceEquals(groupFrame, frame)) frame.Dispose();
            lastStepRid = el.Rid; dirty = s; dirtyArea = area;
        }
        static void CommitTyping(bool atStop)
        {
            if (dirty == null || dirty.AfterFrame != null) { dirty = null; return; }
            try { using (var f = Native.Grab(dirtyArea, false)) dirty.AfterFrame = SessionDoc.Encode(f); } catch { }
            dirty = null;
        }
        static void EndScroll()
        {
            Gesture g; lock (gate) { g = gesture; gesture = null; }
            if (g == null || doc == null) return;
            if (!Settings.SesScroll || lastScrollDir == g.Dir) { lastScrollDir = g.Dir; g.Frame.Dispose(); return; }   // folded into the previous scroll
            lastScrollDir = g.Dir;
            string title = Native.TitleOf(g.Root), proc = ProcName(g.Pid);
            var s = new SessionStep
            {
                Kind = "scroll", ScrollDirection = g.Dir, Ts = Stamp(), X = g.Pt.X - g.Area.X, Y = g.Pt.Y - g.Area.Y, Box = new Rectangle(g.Pt.X - g.Area.X - 14, g.Pt.Y - g.Area.Y - 14, 28, 28),
                Url = "app://" + proc + "/" + title, Title = title, Label = "Scroll " + g.Dir, Role = "scrollbar", Container = "window"
            };
            Push(s, g.Frame, g.Area, proc + "::" + title);
            if (!ReferenceEquals(groupFrame, g.Frame)) g.Frame.Dispose();
        }
        static long Stamp() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; }
    }

    // The small bar shown while recording. It is kept out of every screenshot.
    class SessionBar : Form
    {
        readonly Label text; readonly FlatButton pause, stop; readonly Panel dot; readonly Timer blink = new Timer { Interval = 600 }; bool on = true;
        public SessionBar(Rectangle area, bool withPlan)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Ink;
            int extra = withPlan ? Dpi.S(110) : 0;
            Size = new Size(Dpi.S(584) + extra, Dpi.S(54));
            var wa = Dpi.WorkAreaV(Screen.FromRectangle(area));      // the work area in layout pixels
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Bottom - Height - Dpi.S(16));
            dot = new Panel { Location = new Point(Dpi.S(16), Dpi.S(19)), Size = Dpi.S(16, 16), BackColor = Theme.Ink };
            dot.Paint += delegate(object s, PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(SessionRecorder.Paused ? Theme.Mist : on ? Theme.Mark : Color.FromArgb(120, Theme.Mark))) e.Graphics.FillEllipse(b, 0, 0, dot.Width - 1, dot.Height - 1); };
            text = new Label { Location = new Point(Dpi.S(40), Dpi.S(9)), Size = new Size(Dpi.S(170) + extra, Dpi.S(36)), ForeColor = Color.White, Font = Theme.Body(10.5f), TextAlign = ContentAlignment.MiddleLeft, AccessibleRole = AccessibleRole.StatusBar };
            // start again and cancel are small icon buttons (a tooltip says what each does); the labelled buttons that follow are the ones used all the time
            var tip = new ToolTip { ShowAlways = true };
            var again = new FlatButton("", false) { Location = new Point(Dpi.S(214) + extra, Dpi.S(11)), Size = Dpi.S(36, 32), OnDark = true, Icon = Ic.Reset, AccessibleName = "Start this recording again" };
            var cancel = new FlatButton("", false) { Location = new Point(Dpi.S(254) + extra, Dpi.S(11)), Size = Dpi.S(36, 32), OnDark = true, Icon = Ic.Close, AccessibleName = "Cancel this recording" };
            tip.SetToolTip(again, "Start again: throw away the steps so far"); tip.SetToolTip(cancel, "Cancel: throw this recording away");
            again.Click += delegate { SessionRecorder.Restart(); }; cancel.Click += delegate { SessionRecorder.Cancel(); };
            var swap = new FlatButton("Window", false) { Location = new Point(Dpi.S(300) + extra, Dpi.S(11)), Size = Dpi.S(106, 32), OnDark = true, Icon = Ic.Fit };
            swap.FlatAppearance.BorderColor = Theme.Slate; swap.FlatAppearance.MouseOverBackColor = Theme.Slate; swap.Click += delegate { SessionRecorder.SwitchWindow(); };
            pause = new FlatButton("Pause", false) { Location = new Point(Dpi.S(412) + extra, Dpi.S(11)), Size = Dpi.S(78, 32), OnDark = true };
            pause.FlatAppearance.BorderColor = Theme.Slate; pause.FlatAppearance.MouseOverBackColor = Theme.Slate;
            stop = new FlatButton("Stop", true) { Location = new Point(Dpi.S(496) + extra, Dpi.S(11)), Size = Dpi.S(78, 32), OnDark = true, Tone = Theme.Mark };
            stop.FlatAppearance.BorderColor = Theme.Mark; stop.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xC6, 0x34, 0x1E);
            pause.Click += delegate { SessionRecorder.TogglePause(); };
            stop.Click += delegate { SessionRecorder.Stop(); };
            Controls.AddRange(new Control[] { dot, text, again, cancel, swap, pause, stop });
            blink.Tick += delegate { on = !on; dot.Invalidate(); };
            Shown += delegate { if (Environment.GetEnvironmentVariable("XLC_SHOW_STATUS") == null) Native.ExcludeFromCapture(Handle); blink.Start(); Refresh2(); };
            FormClosed += delegate { blink.Dispose(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }   // never takes focus from the app being recorded
        public void Refresh2()
        {
            int n = SessionRecorder.Count;
            int total = SessionRecorder.TaskCount;
            text.Text = (SessionRecorder.Paused ? "Paused" : "Recording") + "  ·  " + n + (n == 1 ? " step" : " steps") + (total > 0 ? "  ·  task " + (SessionRecorder.TaskPos + 1) + " of " + total : "");
            pause.Text = SessionRecorder.Paused ? "Resume" : "Pause"; dot.Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) { using (var p = new Pen(Theme.InkSoft)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }
}
