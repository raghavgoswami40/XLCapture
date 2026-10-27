// The control channel: a local named pipe that lets another program on this PC (Claude Code, through the
// "snaplite-teach" skill) look at an app, click and type in it, and record what it did as a session.
// One JSON object per request, one JSON object per reply. It is off unless SnapLite is started with --control,
// and only the signed-in Windows user can open the pipe.
//   SnapLite.exe --cmd "{...}" | --cmd @request.json | --cmd -     (add --out reply.json to also write the reply to a file)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;

namespace SnapLite
{
    static class RemoteControl
    {
        public const string PipeName = "SnapLite.Control.v1";
        static Thread server; static volatile bool running;
        static SessionWriter writer; static readonly object gate = new object();
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        // what Claude is asking XLCapture to do, and how it went: the status window shows it ("cmd:click" before, "ok:click" after)
        public static event Action<string, Dictionary<string, object>> Progress;
        static void Raise(string kind, Dictionary<string, object> d) { var h = Progress; if (h != null) { try { h(kind, d); } catch { } } }
        // end a session that was left open (the run was stopped or Claude ended), keeping the steps so far; returns the saved file or null
        public static string EndOpenSession()
        {
            SessionWriter w; lock (gate) { w = writer; writer = null; }
            if (w == null) return null;
            using (w) { try { return w.Doc.Steps.Count > 0 ? w.Save() : null; } catch { return null; } }
        }
        public static void Start()
        {
            if (running) return;
            running = true;
            server = new Thread(Serve) { IsBackground = true, Name = "snaplite-control" };
            server.Start();
        }

        static PipeSecurity Security()
        {
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return ps;
        }

        static void Serve()
        {
            while (running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 1 << 16, 1 << 16, Security()))
                    {
                        pipe.WaitForConnection();
                        var rd = new StreamReader(pipe, new UTF8Encoding(false)); var wr = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                        string line = rd.ReadLine();
                        if (line != null) wr.WriteLine(Handle(line));
                        try { pipe.WaitForPipeDrain(); } catch { }
                    }
                }
                catch (Exception ex) { Debug.WriteLine(ex); Thread.Sleep(300); }
            }
        }

        static string Handle(string line)
        {
            Dictionary<string, object> res;
            try
            {
                var req = Json.DeserializeObject(line) as Dictionary<string, object>;
                if (req == null || !req.ContainsKey("cmd")) throw new ArgumentException("Send a JSON object with a \"cmd\" field.");
                string cmdName = Convert.ToString(req["cmd"]); Raise("cmd:" + cmdName, req);
                res = Dispatch(cmdName, req);
                if (!res.ContainsKey("ok")) res["ok"] = true;
                Raise("ok:" + cmdName, res);
            }
            catch (Exception ex) { res = new Dictionary<string, object> { { "ok", false }, { "error", ex.Message } }; }
            return Json.Serialize(res);
        }

        static string Str(Dictionary<string, object> d, string k, string def) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : def; }
        static Dictionary<string, object> Obj(params object[] kv)
        {
            var d = new Dictionary<string, object>(); for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d;
        }
        static Dictionary<string, object> Rect(Rectangle r) { return Obj("x", r.X, "y", r.Y, "w", r.Width, "h", r.Height); }

        static Dictionary<string, object> Dispatch(string cmd, Dictionary<string, object> req)
        {
            switch (cmd)
            {
                case "ping": return Obj("version", "snaplite-control-1", "recording", writer != null, "folder", Settings.Folder);
                case "windows": return Windows();
                case "observe": return Observe(req);
                case "session-start": return SessionStart(req);
                case "session-status": lock (gate) return Obj("recording", writer != null, "steps", writer == null ? 0 : writer.Doc.Steps.Count);
                case "click": case "type": case "choose": case "scroll": return Act(cmd, req);
                case "session-end": return SessionEnd(req);
                case "replay": return Replay(req);
                case "export": return Export(req);
                case "stop": Replayer.Cancel(); return Obj();
            }
            throw new ArgumentException("Unknown command \"" + cmd + "\". Commands: ping, windows, observe, session-start, click, type, choose, scroll, session-end, session-status, replay, export, stop.");
        }

        static Dictionary<string, object> Windows()
        {
            return Obj("windows", Driver.Windows().Select(w => (object)Obj("process", w[0], "title", w[1])).ToList());
        }

        // a picture of the window and the controls in it that can be acted on, numbered for use as target.index
        static Dictionary<string, object> Observe(Dictionary<string, object> req)
        {
            var t = Target.FromRequest(req);
            var win = Driver.FindWindow(t.Process, t.Title, 4000);
            if (win == null) throw new InvalidOperationException("The window \"" + t.Process + " - " + t.Title + "\" was not found. Use the windows command to list what is open.");
            Driver.Raise(win);
            int max = 300; object m; if (req.TryGetValue("max", out m) && m != null) { try { max = Math.Max(10, Math.Min(1000, Convert.ToInt32(m))); } catch { } }
            var h = new IntPtr(win.Current.NativeWindowHandle); string proc = Driver.ProcName(win.Current.ProcessId);
            var items = Driver.InteractiveElements(win, max);
            Driver.Observed = items;
            var els = new List<object>();
            for (int i = 0; i < items.Count; i++)
            {
                try
                {
                    var c = items[i].Current; var b = c.BoundingRectangle;
                    els.Add(Obj("index", i, "label", c.Name ?? "", "role", Ui.RoleOf(items[i]), "id", c.AutomationId ?? "", "className", c.ClassName ?? "", "path", Ui.AncestorPath(items[i]),
                        "enabled", c.IsEnabled, "bounds", Rect(Rectangle.Round(new RectangleF((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height)))));
                }
                catch { }
            }
            string shot = null;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "SnapLite"); Directory.CreateDirectory(dir);
                foreach (var old in Directory.GetFiles(dir, "observe-*.png")) { try { if (File.GetLastWriteTime(old) < DateTime.Now.AddHours(-1)) File.Delete(old); } catch { } }
                var wb = Native.WindowBounds(h);
                if (!wb.IsEmpty) using (var bmp = Native.Grab(wb, false)) { shot = Path.Combine(dir, "observe-" + DateTime.Now.ToString("HHmmss-fff") + ".png"); bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png); }
            }
            catch { }
            return Obj("window", Obj("process", proc, "title", win.Current.Name ?? "", "urlBar", Ui.UrlBar(win, proc), "bounds", Rect(Native.WindowBounds(h))),
                "screenshot", shot, "elements", els, "count", els.Count);
        }

        static Dictionary<string, object> SessionStart(Dictionary<string, object> req)
        {
            lock (gate)
            {
                if (writer != null) throw new InvalidOperationException("A session is already being recorded. Send session-end first.");
                var proc = req.ContainsKey("procedure") ? req["procedure"] as Dictionary<string, object> : null;
                string name = Str(req, "name", "");
                if (name.Length == 0 && proc != null) name = Str(proc, "name", "");
                if (name.Length == 0) name = "Session " + DateTime.Now.ToString("yyyy-MM-dd HH.mm");
                writer = new SessionWriter(name, proc);
                return Obj("name", name);
            }
        }

        static Dictionary<string, object> Act(string cmd, Dictionary<string, object> req)
        {
            SessionWriter w; lock (gate) w = writer;
            if (w == null) throw new InvalidOperationException("No session is being recorded. Send session-start first.");
            var t = Target.FromRequest(req);
            string text = cmd == "type" || cmd == "choose" ? Str(req, "text", "") : cmd == "scroll" ? Str(req, "direction", "down") : null;
            var opt = new Driver.Options { FindTimeoutMs = 8000, ForceMouse = Str(req, "mouse", "false") == "True" || Str(req, "mouse", "false") == "true" };
            // a typed value is never saved in the session: only the name of the input slot it fills
            var o = Driver.Perform(w, t, cmd, text, Str(req, "param", ""), Str(req, "stepText", ""), opt);
            if (!o.Ok) throw new InvalidOperationException(o.Error);
            return Obj("step", w.Doc.Steps.Count, "method", o.Method, "warning", o.Warning, "windowNow", o.Step.Title, "expect", o.Step.Expect);
        }

        static Dictionary<string, object> SessionEnd(Dictionary<string, object> req)
        {
            SessionWriter w; lock (gate) { w = writer; writer = null; }
            if (w == null) throw new InvalidOperationException("No session is being recorded.");
            using (w)
            {
                if (Str(req, "save", "true") == "false" || w.Doc.Steps.Count == 0) return Obj("saved", null, "steps", w.Doc.Steps.Count);
                string path = w.Save();
                return Obj("saved", path, "steps", w.Doc.Steps.Count, "screens", w.Doc.Groups().Count, "inputs", w.Doc.ParamNames().Cast<object>().ToList());
            }
        }

        static Dictionary<string, object> Replay(Dictionary<string, object> req)
        {
            string path = Str(req, "path", ""); if (!File.Exists(path)) throw new FileNotFoundException("No session file at " + path);
            var doc = SessionDoc.Load(path);
            var opt = new Replayer.Options { Strict = Str(req, "strict", "false").ToLowerInvariant() == "true" };
            var vals = req.ContainsKey("values") ? req["values"] as Dictionary<string, object> : null;
            if (vals != null) foreach (var kv in vals) opt.Values[kv.Key] = kv.Value == null ? "" : Convert.ToString(kv.Value);
            string csv = Str(req, "csv", "");
            var results = new List<Replayer.Result>();
            if (csv.Length > 0) results = Replayer.RunBatch(doc, Replayer.ParseCsv(csv), opt);
            else results.Add(Replayer.Run(doc, opt));
            bool ok = results.All(r => r.Ok);
            return Obj("ok", ok, "runs", results.Select(r => (object)Obj("ok", r.Ok, "error", r.Error, "done", r.Done, "total", r.Total, "saved", r.Saved, "log", r.Log.Cast<object>().ToList())).ToList(),
                "error", ok ? null : results.First(r => !r.Ok).Error);
        }

        static Dictionary<string, object> Export(Dictionary<string, object> req)
        {
            string path = Str(req, "path", ""); if (!File.Exists(path)) throw new FileNotFoundException("No session file at " + path);
            string folder = Str(req, "folder", Path.GetDirectoryName(path));
            return Obj("folder", GuideExport.Write(SessionDoc.Load(path), folder));
        }

        // ------------------------------------------------------------ client: SnapLite.exe --cmd
        public static int Client(string[] args)
        {
            string json = null, outFile = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length) outFile = args[++i];
                else if (json == null) json = args[i];
            }
            if (json == "-") json = Console.In.ReadToEnd();
            else if (json != null && json.StartsWith("@")) json = File.ReadAllText(json.Substring(1), Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return Say("{\"ok\":false,\"error\":\"Usage: XLCapture.exe --cmd <json> | @file | -  [--out reply.json]\"}", outFile, 2);
            json = json.Replace("\r", " ").Replace("\n", " ");

            var pipe = Connect(1500);
            if (pipe == null)
            {
                var running = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Where(p => p.Id != Process.GetCurrentProcess().Id).ToList();
                if (running.Count > 0) return Say("{\"ok\":false,\"error\":\"XLCapture is running without its control channel. Close it, then run XLCapture.exe --tray --control (or send this command again and it will start XLCapture for you).\"}", outFile, 3);
                Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--tray --control") { UseShellExecute = false });
                var sw = Stopwatch.StartNew();
                while (pipe == null && sw.ElapsedMilliseconds < 20000) pipe = Connect(1000);
                if (pipe == null) return Say("{\"ok\":false,\"error\":\"XLCapture did not start its control channel.\"}", outFile, 3);
            }
            using (pipe)
            {
                var wr = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true }; var rd = new StreamReader(pipe, new UTF8Encoding(false));
                wr.WriteLine(json);
                string reply = rd.ReadLine() ?? "{\"ok\":false,\"error\":\"No reply.\"}";
                return Say(reply, outFile, reply.Contains("\"ok\":false") ? 1 : 0);
            }
        }
        static NamedPipeClientStream Connect(int ms)
        {
            var p = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            try { p.Connect(ms); return p; } catch { p.Dispose(); return null; }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        static int Say(string reply, string outFile, int code)
        {
            if (outFile != null) File.WriteAllText(outFile, reply, new UTF8Encoding(false));
            try { AttachConsole(-1); } catch { }
            try { var o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true }; o.WriteLine(reply); } catch { }
            return code;
        }
    }
}
