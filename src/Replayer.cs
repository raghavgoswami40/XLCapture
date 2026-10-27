// Replay: do a saved session again, quickly and without asking an AI anything, with new values in the input steps.
// Every replayed step goes through Driver.Perform, so each run records itself as a new session (a picture of
// what the bot did, ready to become another guide). The values typed are never written to that file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SnapLite
{
    static class Replayer
    {
        public class Options
        {
            public Dictionary<string, string> Values = new Dictionary<string, string>();
            public bool Strict, AllowCoords = true, ShowBar = true;
            public int FindTimeoutMs = 8000;
            // progress for whoever is watching: (step number, total, step text, state) with state 1 running, 2 done, 3 done with a warning, 4 failed
            public Action<int, int, string, int> OnStep;
            public string RunNote = "";         // "Run 2 of 3" in a batch
            public ReplayBar Bar;               // a batch shares one bar
        }
        public class Result { public bool Ok; public string Error, Saved; public int Done, Total; public double Seconds; public List<string> Log = new List<string>(); }

        public static bool Running { get; private set; }
        static volatile bool cancel;
        public static void Cancel() { cancel = true; }
        static bool Cancelled() { return cancel || (Native.GetAsyncKeyState(0x13) & 0x8000) != 0; }      // Pause/Break stops a run

        public static List<string> Missing(SessionDoc d, Dictionary<string, string> values)
        {
            var l = new List<string>();
            foreach (var n in d.ParamNames()) { string v; if ((!values.TryGetValue(n, out v) || v == null) && d.ParamDefault(n) == null && !d.ParamOptional(n)) l.Add(n); }
            return l;
        }

        public static Result Run(SessionDoc src, Options o)
        {
            var res = new Result { Total = src.Steps.Count };
            if (Running) { res.Error = "A replay is already running."; return res; }
            var values = new Dictionary<string, string>(o.Values);
            foreach (var n in src.ParamNames()) if (!values.ContainsKey(n) || values[n] == null) { var d = src.ParamDefault(n); if (d != null) values[n] = d; }
            var miss = Missing(src, values);
            if (miss.Count > 0) { res.Error = "A value is needed for: " + string.Join(", ", miss.ToArray()); return res; }
            if (src.Steps.Count == 0) { res.Error = "This session has no steps."; return res; }

            Running = true; cancel = false;
            var w = new SessionWriter("Replay - " + src.Name + " (" + DateTime.Now.ToString("HH.mm.ss") + ")", src.Procedure);
            ReplayBar bar = o.Bar; bool ownBar = false;
            if (bar == null && o.ShowBar) { bar = ReplayBar.Open(); ownBar = true; }
            int total = src.Steps.Count; var clock = System.Diagnostics.Stopwatch.StartNew();
            Action<int, string, int> report = delegate(int idx, string txt, int st)
            {
                if (bar != null) bar.Step(idx, total, txt, st);
                if (o.OnStep != null) { try { o.OnStep(idx, total, txt, st); } catch { } }
            };
            if (bar != null) bar.Begin(total, o.RunNote);
            try
            {
                int i = 0;
                foreach (var s in src.Steps)
                {
                    if (Cancelled()) { res.Error = "Stopped."; break; }
                    i++;
                    if (s.Decor) { report(i, s.Plain, 2); res.Done++; continue; }      // a copy made by Duplicate screenshot: shown in the guide, not acted on
                    report(i, s.Plain, 1);
                    var t = Target.FromStep(s); t.Abs = GuessScreenPoint(s);
                    string val = ""; if (s.Param.Length > 0) values.TryGetValue(s.Param, out val); val = val ?? "";
                    if (s.Param.Length > 0 && val.Length == 0 && src.ParamOptional(s.Param))       // an optional input left blank: that step is skipped
                    {
                        res.Log.Add("Step " + i + ": " + s.Plain + " - skipped, no value was given"); report(i, s.Plain, 2); res.Done++; continue;
                    }
                    string action = s.Kind == "scroll" ? "scroll" : s.Param.Length > 0 ? (s.Typed ? "type" : "choose") : "click";
                    string text = action == "type" || action == "choose" ? val : action == "scroll" ? s.ScrollDirection : null;
                    var oc = Driver.Perform(w, t, action, text, s.Param, s.StepText,
                        new Driver.Options { FindTimeoutMs = o.FindTimeoutMs, AllowCoords = o.AllowCoords, Strict = o.Strict, Expect = s.Expect });
                    string line = "Step " + i + ": " + s.Plain + (oc.Ok ? " - done (" + oc.Method + ")" : " - FAILED: " + oc.Error);
                    if (oc.Ok && oc.Warning != null) line += " [" + oc.Warning + "]";
                    if (oc.Ok && s.Typed && s.Param.Length == 0) line += " [typed value was not recorded: clicked the field only]";
                    res.Log.Add(line);
                    if (!oc.Ok) { report(i, s.Plain, 4); res.Error = "Step " + i + " (" + s.Plain + "): " + oc.Error; break; }
                    report(i, s.Plain, oc.Warning != null ? 3 : 2);
                    res.Done++;
                }
            }
            catch (Exception ex) { res.Error = ex.Message; }
            finally
            {
                if (bar != null && ownBar) bar.CloseBar();
                res.Seconds = clock.Elapsed.TotalSeconds;
                Running = false;
                try { if (w.Doc.Steps.Count > 0) res.Saved = w.Save(); } catch (Exception ex) { res.Log.Add("The replay's own session could not be saved: " + ex.Message); }
                w.Dispose();
            }
            res.Ok = res.Error == null;
            return res;
        }

        // a session recorded on a whole screen: the step's position says where on that screen it was
        static System.Drawing.Point? GuessScreenPoint(SessionStep s)
        {
            if (s.Vw <= 0 || s.Vh <= 0 || s.Kind == "scroll") return null;
            foreach (var sc in Screen.AllScreens)
                if (sc.Bounds.Width == s.Vw && sc.Bounds.Height == s.Vh) return new System.Drawing.Point(sc.Bounds.X + s.X, sc.Bounds.Y + s.Y);
            return null;
        }

        // one run per row; values come from the column names. A failed row stops the batch, because the app may be left half-way through.
        public static List<Result> RunBatch(SessionDoc src, List<Dictionary<string, string>> rows, Options o)
        {
            var all = new List<Result>();
            var bar = o.ShowBar ? ReplayBar.Open() : null; int k = 0;
            try
            {
                foreach (var row in rows)
                {
                    k++;
                    var oo = new Options { Strict = o.Strict, AllowCoords = o.AllowCoords, ShowBar = false, Bar = bar, FindTimeoutMs = o.FindTimeoutMs, OnStep = o.OnStep, RunNote = "Run " + k + " of " + rows.Count, Values = new Dictionary<string, string>(o.Values) };
                    foreach (var kv in row) oo.Values[kv.Key] = kv.Value;
                    var r = Run(src, oo); all.Add(r);
                    if (!r.Ok) break;
                }
            }
            finally { if (bar != null) bar.CloseBar(); }
            return all;
        }

        // CSV with a header row naming the inputs; quotes and commas inside quotes are handled
        public static List<Dictionary<string, string>> ParseCsv(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿');
            var rows = new List<List<string>>(); var cur = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == ',') { cur.Add(sb.ToString()); sb.Length = 0; }
                else if (c == '\n' || c == '\r') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; cur.Add(sb.ToString()); sb.Length = 0; rows.Add(cur); cur = new List<string>(); }
                else sb.Append(c);
            }
            if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); rows.Add(cur); }
            rows = rows.Where(r => r.Any(x => x.Trim().Length > 0)).ToList();
            var result = new List<Dictionary<string, string>>(); if (rows.Count < 2) return result;
            var head = rows[0].Select(h => h.Trim()).ToList();
            foreach (var r in rows.Skip(1)) { var d = new Dictionary<string, string>(); for (int i = 0; i < head.Count && head[i].Length > 0; i++) d[head[i]] = i < r.Count ? r[i] : ""; result.Add(d); }
            return result;
        }
    }
}
