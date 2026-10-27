// The driver: SnapLite doing the clicking itself. It finds a control through Windows UI Automation (the same
// way the session recorder names what you click, and the way Chrome and Edge expose page elements), acts on it
// (a UI Automation pattern first, the real mouse as a fallback), and writes every action as a normal session step
// with the screenshot taken just before it. Learning runs and replays both go through Driver.Perform.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace SnapLite
{
    // what to find: a window (process + title) and a control inside it
    class Target
    {
        public string Process = "", Title = "", Label = "", Role = "", Id = "", ClassName = "", AncestorPath = "";
        public string Task = "", ActionCode = "";       // which procedure task and action this step carries out (kept on the step, not used to find anything)
        public int Index = -1;          // an element number from the latest "observe"
        public Point? Abs;              // last resort: the recorded screen position

        public static Target FromStep(SessionStep s)
        {
            var t = new Target { Label = s.Label ?? "", Role = s.Role ?? "", Id = s.TestId ?? "", ClassName = s.ClassName ?? "", AncestorPath = s.AncestorPath ?? "", Task = s.Task ?? "", ActionCode = s.ActionCode ?? "" };
            string u = s.Url ?? "";
            if (u.StartsWith("app://")) { u = u.Substring(6); int i = u.IndexOf('/'); if (i >= 0) { t.Process = u.Substring(0, i); t.Title = u.Substring(i + 1); } else t.Process = u; }
            if (t.Title.Length == 0) t.Title = s.Title ?? "";
            return t;
        }
        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        public static Target FromRequest(Dictionary<string, object> req)
        {
            var w = req.ContainsKey("window") ? req["window"] as Dictionary<string, object> : null;
            var e = req.ContainsKey("target") ? req["target"] as Dictionary<string, object> : null;
            var t = new Target { Process = Str(w, "process"), Title = Str(w, "title"), Label = Str(e, "label"), Role = Str(e, "role"), Id = Str(e, "id"), ClassName = Str(e, "className"), AncestorPath = Str(e, "path"), Task = Str(req, "task"), ActionCode = Str(req, "actionCode") };
            object ix; if (e != null && e.TryGetValue("index", out ix) && ix != null) { try { t.Index = Convert.ToInt32(ix); } catch { } }
            return t;
        }
    }

    class Outcome { public bool Ok; public string Error, Method, Warning; public SessionStep Step; }

    // builds a session one step at a time, deciding (like the recorder) when a step starts a new screen
    class SessionWriter : IDisposable
    {
        public readonly SessionDoc Doc;
        Bitmap groupFrame; Rectangle groupArea; readonly List<Rectangle> anchors = new List<Rectangle>(); string lastKey, lastTask = "";
        public string CaptureMode = "screen";           // "window": each picture is the window; otherwise the monitor the action is on
        public SessionWriter(string name, Dictionary<string, object> procedure)
        {
            Doc = new SessionDoc { Id = SessionDoc.NewId(), StartedAt = DateTime.Now, Name = name, Procedure = procedure };
            var cap = procedure != null && procedure.ContainsKey("capture") ? procedure["capture"] as Dictionary<string, object> : null;
            if (cap != null && cap.ContainsKey("mode") && Convert.ToString(cap["mode"]) == "window") CaptureMode = "window";
        }
        // takes ownership of frame
        public void Add(SessionStep s, Bitmap frame, Rectangle area, string key)
        {
            // every task starts a new screen, so the session reads task by task
            bool fresh = Doc.Steps.Count == 0 || (s.Task ?? "") != lastTask || key != lastKey || area != groupArea || !SessionRecorder.AnchorsIntact(groupFrame, anchors, frame);
            lastTask = s.Task ?? "";
            if (fresh)
            {
                if (groupFrame != null) groupFrame.Dispose();
                groupFrame = frame; groupArea = area; anchors.Clear(); s.Frame = SessionDoc.Encode(frame);
            }
            else { s.Frame = SessionDoc.EncodeJpeg(frame); frame.Dispose(); }      // kept so the editor can start a new screenshot from this step
            s.BreakBefore = fresh; s.Vw = area.Width; s.Vh = area.Height;
            anchors.Add(s.Box); lastKey = key;
            Doc.Steps.Add(s); s.Seq = Doc.Steps.Count; s.Id = Doc.Id + "_" + s.Seq.ToString("00000");
        }
        public string Save()
        {
            Doc.Renumber(); Directory.CreateDirectory(Settings.Folder);
            string path = Doc.DefaultPath(Settings.Folder); Doc.Save(path); if (!NoLibrary) LibraryStore.Touch(path);
            return path;
        }
        public static bool NoLibrary;       // self-tests must not add their sessions to the person's recent list
        public void Dispose() { if (groupFrame != null) { groupFrame.Dispose(); groupFrame = null; } }
    }

    static class Driver
    {
        public class Options { public int FindTimeoutMs = 8000; public bool AllowCoords, ForceMouse, Strict; public string Expect; }
        public static int SettleMs = 250;
        public static List<AutomationElement> Observed = new List<AutomationElement>();   // elements numbered by the latest observe

        static readonly Dictionary<int, string> procNames = new Dictionary<int, string>();
        public static string ProcName(int pid)
        {
            string n; lock (procNames) { if (procNames.TryGetValue(pid, out n)) return n; }
            try { n = Process.GetProcessById(pid).ProcessName; } catch { n = "unknown"; }
            lock (procNames) procNames[pid] = n; return n;
        }
        static long Stamp() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; }

        // ------------------------------------------------------------ windows
        public static List<string[]> Windows()
        {
            var l = new List<string[]>();
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                try { string n = (w.Current.Name ?? "").Trim(); if (n.Length == 0 || w.Current.NativeWindowHandle == 0) continue; l.Add(new[] { ProcName(w.Current.ProcessId), n }); } catch { }
            }
            return l;
        }

        public static AutomationElement FindWindow(string proc, string title, int timeoutMs)
        {
            proc = proc ?? ""; title = title ?? "";
            var sw = Stopwatch.StartNew();
            do
            {
                AutomationElement best = null; int bestScore = 0;
                foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
                {
                    try
                    {
                        if (w.Current.NativeWindowHandle == 0) continue;
                        if (proc.Length > 0 && !ProcName(w.Current.ProcessId).Equals(proc, StringComparison.OrdinalIgnoreCase)) continue;
                        string name = (w.Current.Name ?? "");
                        int score = 1;
                        if (title.Length > 0 && name.Length > 0)
                        {
                            if (name == title) score = 4;
                            else if (name.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) score = 3;
                        }
                        else if (title.Length == 0 && proc.Length == 0) continue;
                        if (score > bestScore) { best = w; bestScore = score; }
                    }
                    catch { }
                }
                if (best != null) return best;
                Thread.Sleep(250);
            } while (sw.ElapsedMilliseconds < timeoutMs);
            return null;
        }

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public static void Raise(AutomationElement win)
        {
            try
            {
                var h = new IntPtr(win.Current.NativeWindowHandle);
                if (Native.IsMinimized(h)) { ShowWindow(h, 9); Thread.Sleep(200); }
                if (Native.GetForegroundWindow() == h) return;
                keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);     // an Alt tap lifts Windows' foreground lock
                Native.SetForegroundWindow(h); Thread.Sleep(150);
            }
            catch { }
        }

        // ------------------------------------------------------------ finding a control
        static readonly ControlType[] Interactive = {
            ControlType.Button, ControlType.Edit, ControlType.ComboBox, ControlType.Hyperlink, ControlType.MenuItem, ControlType.TabItem, ControlType.CheckBox,
            ControlType.RadioButton, ControlType.ListItem, ControlType.TreeItem, ControlType.Spinner, ControlType.SplitButton, ControlType.DataItem, ControlType.Slider };
        static Condition InteractiveCondition()
        {
            return new OrCondition(Interactive.Select(c => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, c)).ToArray());
        }
        public static List<AutomationElement> InteractiveElements(AutomationElement win, int max)
        {
            var l = new List<AutomationElement>();
            foreach (AutomationElement e in win.FindAll(TreeScope.Descendants, InteractiveCondition()))
            {
                try { if (e.Current.IsOffscreen) continue; var b = e.Current.BoundingRectangle; if (b.IsEmpty || b.Width < 2 || b.Height < 2) continue; } catch { continue; }
                l.Add(e); if (l.Count >= max) break;
            }
            return l;
        }

        static int Score(AutomationElement e, Target t)
        {
            int s = 0;
            try
            {
                var c = e.Current;
                if (t.Id.Length > 0 && c.AutomationId == t.Id) s += 4;
                string n = c.Name ?? "";
                if (t.Label.Length > 0) { if (n == t.Label) s += 3; else if (n.IndexOf(t.Label, StringComparison.OrdinalIgnoreCase) >= 0 || (n.Length > 2 && t.Label.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) s += 1; }
                if (t.Role.Length > 0 && Ui.RoleOf(e) == t.Role) s += 2;
                if (t.ClassName.Length > 0 && c.ClassName == t.ClassName) s += 1;
                if (t.AncestorPath.Length > 0)
                {
                    string p = Ui.AncestorPath(e);
                    if (p == t.AncestorPath) s += 2; else if (p.Length > 0) { string a = p.Split('>').Last().Trim(), b = t.AncestorPath.Split('>').Last().Trim(); if (a == b) s += 1; }
                }
                if (c.IsOffscreen) s -= 3;
                if (!c.IsEnabled) s -= 1;
            }
            catch { return -99; }
            return s;
        }

        static AutomationElement FindOnce(AutomationElement win, Target t)
        {
            var cands = new List<AutomationElement>();
            Action<AutomationElementCollection> add = delegate(AutomationElementCollection col) { foreach (AutomationElement e in col) cands.Add(e); };
            if (t.Id.Length > 0) add(win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, t.Id)));
            if (t.Label.Length > 0) add(win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, t.Label)));
            if (cands.Count == 0) foreach (AutomationElement e in win.FindAll(TreeScope.Descendants, InteractiveCondition())) cands.Add(e);
            AutomationElement best = null; int bestScore = int.MinValue; var seen = new HashSet<string>();
            bool named = t.Id.Length > 0 || t.Label.Length > 0;
            foreach (var e in cands)
            {
                string rid; try { rid = string.Join(".", e.GetRuntimeId()); } catch { continue; }
                if (!seen.Add(rid)) continue;
                // never settle for a look-alike: a control recorded with a name or id must still carry it (renamed controls need re-learning)
                if (named && !SameName(e, t)) continue;
                int s = Score(e, t);
                if (s > bestScore) { best = e; bestScore = s; }
            }
            int need = named ? 3 : 5;       // an unnamed control (an icon) must match on role, class and place
            return best != null && bestScore >= need ? best : null;
        }
        static bool SameName(AutomationElement e, Target t)
        {
            try
            {
                var c = e.Current;
                if (t.Id.Length > 0 && c.AutomationId == t.Id) return true;
                string n = c.Name ?? "";
                return t.Label.Length > 0 && (n == t.Label || n.IndexOf(t.Label, StringComparison.OrdinalIgnoreCase) >= 0 || (n.Length > 2 && t.Label.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch { return false; }
        }

        public static AutomationElement FindElement(AutomationElement win, Target t, int timeoutMs)
        {
            if (t.Index >= 0 && t.Index < Observed.Count)
            {
                try { var o = Observed[t.Index]; var n = o.Current.Name; return o; } catch { }
            }
            var sw = Stopwatch.StartNew();
            do
            {
                AutomationElement e = null;
                try { e = FindOnce(win, t); } catch { }
                if (e != null) return e;
                Thread.Sleep(300);
            } while (sw.ElapsedMilliseconds < timeoutMs);
            return null;
        }

        // ------------------------------------------------------------ acting
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

        static INPUT Mouse(uint flags) { var i = new INPUT { type = 0 }; i.u.mi.dwFlags = flags; return i; }
        static INPUT Key(ushort vk, ushort scan, uint flags) { var i = new INPUT { type = 1 }; i.u.ki.wVk = vk; i.u.ki.wScan = scan; i.u.ki.dwFlags = flags; return i; }
        static void Send(params INPUT[] inputs) { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }

        public static void MouseClick(Point p)
        {
            Native.SetCursorPos(p.X, p.Y); Thread.Sleep(60);
            Send(Mouse(0x0002)); Thread.Sleep(40); Send(Mouse(0x0004));
        }
        static Point Center(AutomationElement e)
        {
            var b = e.Current.BoundingRectangle;
            if (b.IsEmpty || double.IsInfinity(b.X) || double.IsInfinity(b.Width)) throw new InvalidOperationException("The control has no position on screen.");
            return new Point((int)(b.X + b.Width / 2), (int)(b.Y + b.Height / 2));
        }
        // some Invoke calls (a button that opens a modal dialog) do not return until the dialog closes
        static void RunWithTimeout(Action a, int ms)
        {
            var th = new Thread(delegate() { try { a(); } catch { } }) { IsBackground = true };
            th.Start(); th.Join(ms);
        }

        public static string Activate(AutomationElement el, bool forceMouse)
        {
            string role = Ui.RoleOf(el);
            bool field = role == "Edit" || role == "Document" || role == "ComboBox" || role == "Spinner";
            if (!forceMouse)
            {
                object p;
                if (field) { try { el.SetFocus(); return "focus"; } catch { } }
                else
                {
                    try { if (el.TryGetCurrentPattern(InvokePattern.Pattern, out p)) { var ip = (InvokePattern)p; RunWithTimeout(delegate { ip.Invoke(); }, 1500); return "invoke"; } } catch { }
                    try { if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out p)) { ((SelectionItemPattern)p).Select(); return "select"; } } catch { }
                    try { if (el.TryGetCurrentPattern(TogglePattern.Pattern, out p)) { ((TogglePattern)p).Toggle(); return "toggle"; } } catch { }
                    try
                    {
                        if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out p))
                        {
                            var ec = (ExpandCollapsePattern)p;
                            if (ec.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) ec.Expand(); else ec.Collapse();
                            return "expand";
                        }
                    }
                    catch { }
                }
            }
            MouseClick(Center(el)); return "mouse";
        }

        public static string TypeText(AutomationElement el, string text)
        {
            try { el.SetFocus(); } catch { MouseClick(Center(el)); }
            Thread.Sleep(90);
            Send(Key(0x11, 0, 0), Key(0x41, 0, 0), Key(0x41, 0, 2), Key(0x11, 0, 2));       // Ctrl+A: replace what is there
            Thread.Sleep(50);
            text = text ?? "";
            if (text.Length == 0) Send(Key(0x2E, 0, 0), Key(0x2E, 0, 2));                    // Delete
            else
            {
                var l = new List<INPUT>();
                foreach (char ch in text)
                {
                    if (ch == '\r') continue;
                    if (ch == '\n') { l.Add(Key(0x0D, 0, 0)); l.Add(Key(0x0D, 0, 2)); continue; }
                    l.Add(Key(0, ch, 0x0004)); l.Add(Key(0, ch, 0x0004 | 0x0002));           // KEYEVENTF_UNICODE, then key up
                }
                for (int i = 0; i < l.Count; i += 64) Send(l.Skip(i).Take(64).ToArray());
            }
            Thread.Sleep(120);
            try
            {
                object p;
                if (text.Length > 0 && el.TryGetCurrentPattern(ValuePattern.Pattern, out p))
                {
                    var vp = (ValuePattern)p;
                    if (!vp.Current.IsReadOnly && vp.Current.Value != text) { vp.SetValue(text); return "set-value"; }
                }
            }
            catch { }
            return "keys";
        }

        // ------------------------------------------------------------ one step: find, record, act
        static Outcome Fail(string msg) { return new Outcome { Ok = false, Error = msg }; }

        // action: "click" | "type" | "choose" | "scroll" (text is the typed value, the option to pick, or the scroll direction).
        // "choose" clicks the control whose name is the option; the option's name is a value, so it is never saved on the step.
        public static Outcome Perform(SessionWriter w, Target t, string action, string text, string param, string stepText, Options opt)
        {
            opt = opt ?? new Options();
            bool choose = action == "choose";
            if (choose) { t.Label = text ?? ""; t.Id = ""; }
            var win = FindWindow(t.Process, t.Title, opt.FindTimeoutMs);
            if (win == null) return Fail("The window \"" + t.Process + " - " + t.Title + "\" was not found.");
            Raise(win);
            IntPtr hwnd = new IntPtr(win.Current.NativeWindowHandle);
            string title = win.Current.Name ?? "", proc = ProcName(win.Current.ProcessId);

            AutomationElement el = null; Point center; string method = null, warn = null;
            if (action == "scroll")
            {
                var wb = Native.WindowBounds(hwnd); center = new Point(wb.X + wb.Width / 2, wb.Y + wb.Height / 2);
            }
            else
            {
                el = FindElement(win, t, opt.FindTimeoutMs);
                if (el != null) { try { center = Center(el); } catch (Exception ex) { return Fail(ex.Message); } }
                else if (opt.AllowCoords && t.Abs.HasValue) { center = t.Abs.Value; warn = "control not found, used the recorded screen position"; }
                else return Fail("Could not find " + (t.Label.Length > 0 ? "\"" + t.Label + "\"" : "the control") + (t.Role.Length > 0 ? " (" + t.Role + ")" : "") + " in \"" + title + "\".");
            }

            var area = Screen.FromPoint(center).Bounds;
            if (w.CaptureMode == "window") { var wb2 = Native.WindowBounds(hwnd); if (!wb2.IsEmpty && wb2.Width > 40 && wb2.Height > 40) area = wb2; }
            var frame = Native.Grab(area, false);                // the screen as it is before the action
            var info = el != null ? SessionRecorder.From(el) : new SessionRecorder.El { Name = t.Label, Role = t.Role, Id = t.Id, ClassName = t.ClassName };
            bool scroll = action == "scroll";
            var s = new SessionStep
            {
                Kind = scroll ? "scroll" : "click", ScrollDirection = scroll ? (text ?? "down") : null, Ts = Stamp(),
                X = center.X - area.X, Y = center.Y - area.Y,
                Box = scroll ? new Rectangle(center.X - area.X - 14, center.Y - area.Y - 14, 28, 28) : SessionRecorder.BoxFor(info, center, area),
                Url = "app://" + proc + "/" + title, Title = title,
                Label = scroll ? "Scroll " + (text ?? "down") : choose ? "" : (info.Name.Length > 0 ? info.Name : t.Label), Role = scroll ? "scrollbar" : (info.Role.Length > 0 ? info.Role : t.Role),
                Container = scroll ? "window" : "", TestId = choose ? "" : info.Id.Length > 0 ? info.Id : t.Id,
                ClassName = info.ClassName.Length > 0 ? info.ClassName : t.ClassName, AncestorPath = el != null ? Ui.AncestorPath(el) : t.AncestorPath,
                UrlBar = Ui.UrlBar(win, proc), Param = param ?? "", Typed = action == "type", StepText = string.IsNullOrWhiteSpace(stepText) ? null : stepText,
                Task = t.Task ?? "", ActionCode = t.ActionCode ?? ""
            };
            w.Add(s, frame, area, proc + "::" + title);

            try
            {
                if (scroll)
                {
                    Native.SetCursorPos(center.X, center.Y); Thread.Sleep(60);
                    string d = text ?? "down";
                    if (d == "down") Native.MouseWheel(3); else if (d == "up") Native.MouseWheel(-3); else if (d == "right") Native.MouseWheelH(3); else Native.MouseWheelH(-3);
                    method = "wheel";
                }
                else if (action == "type")
                {
                    if (el == null) { MouseClick(center); Thread.Sleep(90); Send(Key(0x11, 0, 0), Key(0x41, 0, 0), Key(0x41, 0, 2), Key(0x11, 0, 2)); method = "coords"; TypeRaw(text); }
                    else method = TypeText(el, text);
                    Thread.Sleep(150);
                    using (var f = Native.Grab(area, false)) s.AfterFrame = SessionDoc.Encode(f);
                }
                else method = el != null ? Activate(el, opt.ForceMouse || choose) : MouseClickAt(center);   // picking from a list needs a real click so the app sees the selection change
            }
            catch (Exception ex) { return new Outcome { Ok = false, Error = "The action failed: " + ex.Message, Step = s }; }

            Thread.Sleep(SettleMs);
            // what the window is called now, to check a replay is still on the path it was recorded on
            string after = TitleNow(win, proc);
            if (!string.IsNullOrEmpty(opt.Expect) && opt.Expect.StartsWith("title:"))
            {
                string want = opt.Expect.Substring(6); var sw = Stopwatch.StartNew();
                while (after != want && sw.ElapsedMilliseconds < 5000) { Thread.Sleep(250); after = TitleNow(win, proc); }
                if (after != want)
                {
                    string msg = "afterwards the window was \"" + after + "\", not \"" + want + "\" as when recorded";
                    if (opt.Strict) return new Outcome { Ok = false, Error = "Not on the recorded path: " + msg, Step = s };
                    warn = (warn == null ? "" : warn + "; ") + msg;
                }
            }
            if (!string.IsNullOrEmpty(after) && after != title) s.Expect = "title:" + after;
            return new Outcome { Ok = true, Method = method, Warning = warn, Step = s };
        }

        static string MouseClickAt(Point p) { MouseClick(p); return "coords"; }
        static void TypeRaw(string text)
        {
            var l = new List<INPUT>();
            foreach (char ch in text ?? "") { if (ch == '\r') continue; if (ch == '\n') { l.Add(Key(0x0D, 0, 0)); l.Add(Key(0x0D, 0, 2)); continue; } l.Add(Key(0, ch, 0x0004)); l.Add(Key(0, ch, 0x0006)); }
            for (int i = 0; i < l.Count; i += 64) Send(l.Skip(i).Take(64).ToArray());
        }
        // a dialog may have replaced the window, so fall back to the newest window of the same program
        static string TitleNow(AutomationElement win, string proc)
        {
            try { return win.Current.Name ?? ""; }
            catch { var w2 = FindWindow(proc, "", 600); try { return w2 == null ? "" : w2.Current.Name ?? ""; } catch { return ""; } }
        }
    }
}
