// Procedures: the Process Manager export (activities > tasks > actions), read so a session can follow one: the checklist shown while recording and the slide pack.
// One session is made per activity; its tasks become the groups of the session; every action says whether Claude only
// clicks, enters a value, or chooses an option, and the values are asked for before Claude starts.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace SnapLite
{
    // one value a run needs: what to call it, what kind it is, where in the procedure it belongs
    class InputSpec
    {
        public string Param = "", Label = "", Type = "text", Task = "", ActionCode = "", ActionText = "";
        public List<string> Choices = new List<string>(); public bool Optional;

        public static string Pretty(string name) { string s = (name ?? "").Replace('_', ' ').Trim(); return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1); }
        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        public static InputSpec From(string name, Dictionary<string, object> d)
        {
            var s = new InputSpec { Param = name, Label = Str(d, "label"), Type = Str(d, "type"), Task = Str(d, "task"), ActionCode = Str(d, "action"), ActionText = Str(d, "text") };
            if (s.Label.Length == 0) s.Label = Pretty(name);
            if (s.Type.Length == 0) s.Type = "text";
            object o; if (d != null && d.TryGetValue("choices", out o) && o is object[]) foreach (var c in (object[])o) s.Choices.Add(Convert.ToString(c));
            s.Optional = d != null && d.TryGetValue("optional", out o) && o is bool && (bool)o;
            return s;
        }
    }

    class ProcAction
    {
        public string Code = "", Text = "", Kind = "click";           // click | enter | choose | skip
        public string Field = "", Type = "text", Why = ""; public bool Optional; public List<string> Choices = new List<string>();
        public string Param = "", Value = ""; public ProcTask Task;
        public List<PlanStep> Plan = new List<PlanStep>();       // the clicks on screen, in order: every quoted name is one click
        public string PlanText()
        {
            return string.Join("  >  ", Plan.Select(p => p.Do == "type" ? "type in " + (p.Target.Length > 0 ? p.Target : "the field") : p.Do == "choose" ? "choose the option" : p.Target).ToArray());
        }
        public static string KindName(string k) { switch (k) { case "enter": return "Enter a value"; case "choose": return "Choose an option"; case "skip": return "Do by hand"; } return "Click"; }
    }
    // one thing done on screen: click a named control, type into one, or pick an option (the option is a value, so it has no name here)
    class PlanStep { public string Do = "click", Target = "", Locator = ""; }
    class ProcTask { public string Code = "", Title = ""; public List<ProcAction> Actions = new List<ProcAction>(); public string Full { get { return Code + " " + Title; } } }
    class ProcActivity
    {
        public string Code = "", Title = "", System = ""; public List<ProcTask> Tasks = new List<ProcTask>();
        public string ProcessCode = "", ProcessTitle = "", ProcessVersion = "", Client = "", Parent = "";        // the process this activity belongs to, from the workbook's Cover sheet
        public int ActionCount { get { return Tasks.Sum(t => t.Actions.Count); } }
    }

    static class ProcedureFile
    {
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

        // ------------------------------------------------------------ reading the export
        public static List<ProcActivity> Load(string path)
        {
            List<Dictionary<string, string>> rows = null; Dictionary<string, string> cover = null;
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) rows = ParseCsv(path);
            else
            {
                var sheets = ReadXlsxSheets(path);
                foreach (var sh in sheets) { var tbl = Tabulate(sh.Value); if (tbl.Count > 0 && tbl[0].ContainsKey("Node Type")) { rows = tbl; break; } }
                cover = CoverOf(sheets);
            }
            if (rows == null || (rows.Count > 0 && !rows[0].ContainsKey("Node Type"))) throw new InvalidDataException("This does not look like a Process Manager export: no sheet has a \"Node Type\" column.");
            var list = new List<ProcActivity>(); ProcActivity cur = null; ProcTask task = null;
            foreach (var r in rows)
            {
                string type = Get(r, "Node Type").Trim(), code = Get(r, "Code").Trim(), title = Get(r, "Title").Trim();
                if (type == "Activity")
                {
                    string sys = Get(r, "Systems").Trim(); if (sys.Length == 0) sys = Get(r, "System").Trim();
                    cur = new ProcActivity { Code = code, Title = title, System = sys }; ApplyCover(cur, cover); list.Add(cur); task = null;
                }
                else if (type == "Task" && cur != null) { task = new ProcTask { Code = code, Title = title }; cur.Tasks.Add(task); }
                else if (type == "Action" && task != null) { var a = new ProcAction { Code = code, Text = title, Task = task }; Classify(a); task.Actions.Add(a); }
                else { cur = null; task = null; }     // start, end, decision, branch, sub process: nothing to click
            }
            return list.Where(a => a.ActionCount > 0).ToList();
        }
        static string Get(Dictionary<string, string> r, string k) { string v; return r.TryGetValue(k, out v) && v != null ? v : ""; }

        // the Cover sheet: "1.4.1 CREATE DELIVERY PROJECT V0.0" in the first cell, then label and value pairs (Client Company, Parent...)
        static Dictionary<string, string> CoverOf(List<KeyValuePair<string, List<Dictionary<string, string>>>> sheets)
        {
            var sh = sheets.FirstOrDefault(s => string.Equals(s.Key, "Cover", StringComparison.OrdinalIgnoreCase)); if (sh.Value == null || sh.Value.Count == 0) return null;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string head = Get(sh.Value[0], "A").Trim(); var m = Regex.Match(head, @"^(\S+)\s+(.*?)(?:\s+V(\d+(?:\.\d+)*))?$");
            if (m.Success) { d["code"] = m.Groups[1].Value; d["title"] = Nice(m.Groups[2].Value); d["version"] = m.Groups[3].Value; } else d["title"] = Nice(head);
            foreach (var r in sh.Value) { string k = Get(r, "A").Trim(), v = Get(r, "B").Trim(); if (k.Length > 0 && v.Length > 0 && !d.ContainsKey(k)) d[k] = v; }
            return d;
        }
        // "CREATE DELIVERY PROJECT" to "Create Delivery Project"; mixed case is left as it is
        static string Nice(string s)
        {
            s = (s ?? "").Trim(); if (s != s.ToUpperInvariant()) return s;
            return string.Join(" ", s.ToLowerInvariant().Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1)).ToArray());
        }
        static void ApplyCover(ProcActivity a, Dictionary<string, string> c)
        {
            if (c == null) return; string v;
            if (c.TryGetValue("code", out v)) a.ProcessCode = v; if (c.TryGetValue("title", out v)) a.ProcessTitle = v; if (c.TryGetValue("version", out v)) a.ProcessVersion = v;
            if (c.TryGetValue("Client Company", out v)) a.Client = v; if (c.TryGetValue("Parent", out v)) a.Parent = v;
        }

        // the first row of a sheet names the columns; every other row becomes a record
        static List<Dictionary<string, string>> Tabulate(List<Dictionary<string, string>> raw)
        {
            var table = new List<Dictionary<string, string>>(); if (raw.Count == 0) return table; var head = raw[0];
            for (int i = 1; i < raw.Count; i++) { var rec = new Dictionary<string, string>(); foreach (var h in head) rec[h.Value.Trim()] = raw[i].ContainsKey(h.Key) ? raw[i][h.Key] : ""; table.Add(rec); }
            return table;
        }

        // every sheet of the workbook, by name, as rows of column letter -> text
        static List<KeyValuePair<string, List<Dictionary<string, string>>>> ReadXlsxSheets(string path)
        {
            // Excel may still have the file open: read it without asking for exclusive access
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                var se = z.GetEntry("xl/sharedStrings.xml");
                if (se != null) using (var s = se.Open()) { var d = new XmlDocument(); d.Load(s); foreach (XmlNode si in d.GetElementsByTagName("si")) shared.Add(string.Concat(si.SelectNodes(".//*[local-name()='t']").Cast<XmlNode>().Select(n => n.InnerText).ToArray())); }
                // the sheet names and where each one is stored
                var order = new List<KeyValuePair<string, string>>();
                try
                {
                    var wb = new XmlDocument(); using (var s = z.GetEntry("xl/workbook.xml").Open()) wb.Load(s);
                    var rels = new XmlDocument(); using (var s = z.GetEntry("xl/_rels/workbook.xml.rels").Open()) rels.Load(s);
                    var target = new Dictionary<string, string>(); foreach (XmlNode r in rels.SelectNodes("//*[local-name()='Relationship']")) target[r.Attributes["Id"].Value] = r.Attributes["Target"].Value;
                    foreach (XmlNode sh in wb.SelectNodes("//*[local-name()='sheet']"))
                    {
                        XmlAttribute rid = null; foreach (XmlAttribute a in sh.Attributes) if (a.LocalName == "id") rid = a;
                        string tg; if (rid != null && target.TryGetValue(rid.Value, out tg)) order.Add(new KeyValuePair<string, string>(sh.Attributes["name"].Value, "xl/" + tg.TrimStart('/').Replace("xl/", "")));
                    }
                }
                catch { order.Clear(); }
                if (order.Count == 0) foreach (var e in z.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml")).OrderBy(e => e.FullName)) order.Add(new KeyValuePair<string, string>(e.FullName, e.FullName));
                if (order.Count == 0) throw new InvalidDataException("This workbook has no sheet.");
                var result = new List<KeyValuePair<string, List<Dictionary<string, string>>>>();
                foreach (var o in order)
                {
                    var entry = z.GetEntry(o.Value); if (entry == null) continue;
                    var doc = new XmlDocument(); using (var s = entry.Open()) doc.Load(s);
                    var raw = new List<Dictionary<string, string>>();
                    foreach (XmlNode row in doc.SelectNodes("//*[local-name()='row']"))
                    {
                        var cells = new Dictionary<string, string>();
                        foreach (XmlNode c in row.SelectNodes("*[local-name()='c']"))
                        {
                            string rf = c.Attributes["r"] == null ? "" : c.Attributes["r"].Value; string col = new string(rf.TakeWhile(char.IsLetter).ToArray());
                            string t = c.Attributes["t"] == null ? "" : c.Attributes["t"].Value; var v = c.SelectSingleNode("*[local-name()='v']"); string val = "";
                            if (t == "s" && v != null) { int i; if (int.TryParse(v.InnerText, out i) && i >= 0 && i < shared.Count) val = shared[i]; }
                            else if (t == "inlineStr") val = c.InnerText;
                            else if (v != null) val = v.InnerText;
                            cells[col] = val;
                        }
                        raw.Add(cells);
                    }
                    result.Add(new KeyValuePair<string, List<Dictionary<string, string>>>(o.Key, raw));
                }
                return result;
            }
        }

        // ------------------------------------------------------------ what Claude does with each action
        static readonly Regex Quoted = new Regex("[\"“]([^\"”]+)[\"”]");
        static string Cap(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1); }
        // the words after the verb and its "the / a / an": "enter the issue name in ..." gives "issue name"
        static string Phrase(string l)
        {
            var m = Regex.Match(l, @"^(?:enter|type|update|select|add|search for|click on|click|use)\s+(?:the |a |an |any |your )?(.+)$"); if (!m.Success) return "";
            string p = m.Groups[1].Value; int cut = p.Length;
            foreach (var sep in new[] { " in ", " under ", " for ", " and ", " using ", " to ", " beside ", " next ", ", ", " from ", " of ", " based " }) { int i = p.IndexOf(sep); if (i > 0 && i < cut) cut = i; }
            return p.Substring(0, cut).Trim();
        }

        public static void Classify(ProcAction a) { ClassifyKind(a); Replan(a); }

        // Every double-quoted name in an action is a click on the screen. 'Select "Closed" in "Issue status"' is two clicks, the
        // field first and then the option. The name after "beside" or "next to" only says where to look, it is not clicked.
        public static void Replan(ProcAction a)
        {
            a.Plan = new List<PlanStep>(); if (a.Kind == "skip") return;
            string t = a.Text.Trim().TrimEnd('.'), l = t.ToLowerInvariant();
            var q = Quoted.Matches(t).Cast<Match>().Select(m => m.Groups[1].Value).ToList(); var ordered = new List<string>(q); string locator = "";
            var pair = Regex.Match(t, "[\"“]([^\"”]+)[\"”]\\s+(?:in|under|from|within)\\s+[\"“]([^\"”]+)[\"”]", RegexOptions.IgnoreCase);
            if (pair.Success)                         // "X" in "Y": the field Y is clicked before its option X
            {
                string x = pair.Groups[1].Value, y = pair.Groups[2].Value; int ix = ordered.IndexOf(x);
                if (ix >= 0 && ix + 1 < ordered.Count && ordered[ix + 1] == y) { ordered[ix] = y; ordered[ix + 1] = x; }
            }
            var beside = Regex.Match(t, "[\"“]([^\"”]+)[\"”]\\s+(?:beside|next to)\\s+[\"“]([^\"”]+)[\"”]", RegexOptions.IgnoreCase);
            if (beside.Success) { locator = beside.Groups[2].Value; int i2 = ordered.IndexOf(locator); if (i2 >= 0) ordered.RemoveAt(i2); }
            Action<string, string> add = delegate(string d, string target) { a.Plan.Add(new PlanStep { Do = d, Target = target, Locator = d == "click" ? "" : locator }); };

            if (a.Kind == "click") { foreach (var s in ordered) add("click", s); return; }
            if (a.Kind == "enter")
            {
                if (a.Type == "file") { add("type", ""); foreach (var s in ordered) add("click", s); return; }          // type the path, then click "Open"
                if (l.Contains(" and enter ")) { foreach (var s in ordered) add("click", s); add("type", locator); return; }   // click "Add", then type in the field that opens
                for (int i = 0; i < ordered.Count - 1; i++) add("click", ordered[i]);
                add("type", ordered.Count > 0 ? ordered[ordered.Count - 1] : ""); return;                                   // typing into a field is its click
            }
            if (a.Kind == "choose")
            {
                if (a.Type == "date") { for (int i = 0; i < ordered.Count - 1; i++) add("click", ordered[i]); add("type", ordered.Count > 0 ? ordered[ordered.Count - 1] : ""); return; }
                bool controlWord = Regex.IsMatch(l, "the relevant [\"“]") || (q.Count > 0 && q[0].ToLowerInvariant() == "checkbox");
                if (controlWord) { a.Plan.Add(new PlanStep { Do = "choose", Target = "", Locator = q.Count > 0 ? q[0] : "" }); return; }
                if (l.Contains(" and click") || l.StartsWith("click")) { add("choose", ""); foreach (var s in ordered) add("click", s); return; }   // pick the item, then click "Edit"
                foreach (var s in ordered) add("click", s); add("choose", "");                                              // open the field, then pick
            }
        }

        static void ClassifyKind(ProcAction a)
        {
            string t = a.Text.Trim().TrimEnd('.'), l = t.ToLowerInvariant();
            var q = Quoted.Matches(t).Cast<Match>().Select(m => m.Groups[1].Value).ToList(); string last = q.Count > 0 ? q[q.Count - 1] : "";
            a.Optional = l.Contains("if required") || l.Contains("optional"); a.Kind = "click"; a.Type = "text"; a.Choices = new List<string>(); a.Why = "";
            string phrase = Phrase(l); a.Field = Cap(last.Length > 0 ? last : phrase);

            if (l.StartsWith("repeat")) { a.Kind = "skip"; a.Why = "How many times is for a person to decide"; return; }
            if (l.StartsWith("review") || l.StartsWith("check ") || l.StartsWith("read ")) { a.Kind = "skip"; a.Why = "Reading, not clicking"; return; }

            if (Regex.IsMatch(l, @"^select the file\b")) { a.Kind = "enter"; a.Type = "file"; a.Field = "File to attach"; return; }
            if (Regex.IsMatch(l, @"^select yes or no\b")) { a.Kind = "choose"; a.Choices.Add("Yes"); a.Choices.Add("No"); a.Field = Cap(last.Length > 0 ? last : "Yes or no"); return; }
            if (l.StartsWith("select"))
            {
                bool quotedFirst = Regex.IsMatch(t, "^[Ss]elect\\s+(the\\s+)?[\"“]");
                string firstQ = q.Count > 0 ? q[0].ToLowerInvariant() : "";
                if (quotedFirst && firstQ == "checkbox") { var fm = Regex.Match(l, @"for the (.+)$"); a.Kind = "choose"; a.Field = Cap(fm.Success ? fm.Groups[1].Value : "items"); return; }
                if (quotedFirst) return;                                                       // select "Closed" in ...: a named control, a plain click
                if (l.Contains(" and click")) { a.Kind = "choose"; a.Field = Cap(phrase); return; }
                if (l.Contains(" tab") || l.Contains(" icon") || l.Contains(" button")) return;
                a.Kind = "choose";
                a.Field = Cap(last.Length > 0 && Regex.IsMatch(l, " in [\"“]") ? last : (phrase.Length > 0 ? phrase : last));
                if (Regex.IsMatch(l, @"^select the date\b")) a.Type = "date";
                return;
            }
            if (l.StartsWith("click") && l.Contains("the relevant"))
            {
                a.Kind = "choose";
                var m = Regex.Match(l, @"the (relevant [^""“]+?)(?: in | from |$)");
                a.Field = Cap(Regex.IsMatch(l, "the relevant [\"“]") && last.Length > 0 ? last : (m.Success ? m.Groups[1].Value : phrase));
                return;
            }
            var search = Regex.Match(l, @"search for (?:an? |the )?([a-z ]+)$");
            if (l.StartsWith("use the") && search.Success) { a.Kind = "enter"; a.Field = Cap(search.Groups[1].Value.Trim() + " to search for"); return; }
            if (l.StartsWith("search for")) { a.Kind = "enter"; a.Field = Cap(phrase + " to search for"); return; }
            if (Regex.IsMatch(l, @"^(enter|type|update)\b") || Regex.IsMatch(l, @"^add (a )?(notes?|comments?)\b") || l.Contains(" and enter "))
            {
                a.Kind = "enter";
                if (Regex.IsMatch(l, @"^add (a )?(notes?|comments?)\b")) a.Field = Cap(Regex.Match(l, @"^add (?:a )?(notes?|comments?)").Groups[1].Value);
                if (Regex.IsMatch(l, @"\b(cost(?! item)|days|quantity|qty|hours|amount)\b|number of") && !l.Contains("note")) a.Type = "number";
            }
        }

        // a short unique name for each input, from its field
        static string Slug(string s) { var sb = new StringBuilder(); foreach (char c in (s ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_'); string r = Regex.Replace(sb.ToString(), "_+", "_").Trim('_'); return r.Length == 0 ? "value" : (r.Length > 40 ? r.Substring(0, 40).Trim('_') : r); }

        // the inputs this activity needs, in order, with unique names
        public static List<InputSpec> Specs(ProcActivity act)
        {
            var list = new List<InputSpec>(); var used = new HashSet<string>();
            foreach (var t in act.Tasks)
                foreach (var a in t.Actions)
                {
                    if (a.Kind != "enter" && a.Kind != "choose") { a.Param = ""; continue; }
                    string p = Slug(a.Field); if (!used.Add(p)) { p = p + "_" + a.Code.Replace('.', '_'); used.Add(p); }
                    a.Param = p;
                    list.Add(new InputSpec { Param = p, Label = a.Field.Length > 0 ? a.Field : "Value", Type = a.Type, Task = t.Full, ActionCode = a.Code, ActionText = a.Text, Choices = new List<string>(a.Choices), Optional = a.Optional });
                }
            return list;
        }

        // ------------------------------------------------------------ the procedure stored in the session
        public static Dictionary<string, object> ProcedureObject(ProcActivity act, List<InputSpec> specs)
        {
            var ps = new Dictionary<string, object>();
            foreach (var s in specs)
                ps[s.Param] = new Dictionary<string, object> { { "type", s.Type }, { "label", s.Label }, { "optional", s.Optional }, { "task", s.Task }, { "action", s.ActionCode }, { "text", s.ActionText }, { "choices", s.Choices.Cast<object>().ToArray() } };
            return new Dictionary<string, object> {
                { "name", act.Title }, { "activity", new Dictionary<string, object> { { "code", act.Code }, { "title", act.Title }, { "system", act.System } } },
                { "process", new Dictionary<string, object> { { "code", act.ProcessCode }, { "title", act.ProcessTitle }, { "version", act.ProcessVersion }, { "client", act.Client }, { "parent", act.Parent } } },
                { "capture", new Dictionary<string, object> { { "mode", "screen" } } },
                { "tasks", act.Tasks.Select(t => (object)new Dictionary<string, object> { { "code", t.Code }, { "title", t.Title },
                    { "actions", t.Actions.Select(a => (object)new Dictionary<string, object> { { "code", a.Code }, { "text", a.Text }, { "kind", a.Kind }, { "param", a.Param } }).ToList() } }).ToList() },
                { "steps", act.Tasks.SelectMany(t => t.Actions).Select(a => (object)a.Text).ToList() },
                { "params", ps } };
        }
    }
}
