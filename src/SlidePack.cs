// Exports a session as a slide pack folder: slides.json (the process, the activity, the tasks, and every slide with its steps) and
// one clean picture per screen. The xlcapture-slides skill (build_deck.py) turns that folder into a PowerPoint on the client's template:
// the picture goes in the template's picture frame, each box becomes a native rectangle on it, and each step text becomes a text box,
// so text that was placed outside the screenshot in the editor sits outside the picture on the slide.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SnapLite
{
    static class SlidePack
    {
        public const string PromptFile = "claude-prompt.txt";

        static Dictionary<string, object> Sub(SessionDoc doc, string key)
        {
            return doc.Procedure != null && doc.Procedure.ContainsKey(key) ? doc.Procedure[key] as Dictionary<string, object> : null;
        }
        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        // "6.1 Open the project" to ("6.1", "Open the project")
        internal static void SplitTask(string task, out string code, out string title)
        {
            task = (task ?? "").Trim(); int i = task.IndexOf(' ');
            if (i > 0 && char.IsDigit(task[0])) { code = task.Substring(0, i); title = task.Substring(i + 1).Trim(); } else { code = ""; title = task; }
        }
        static Dictionary<string, object> Rect(RectangleF r) { return new Dictionary<string, object> { { "x", Math.Round(r.X, 1) }, { "y", Math.Round(r.Y, 1) }, { "w", Math.Round(r.Width, 1) }, { "h", Math.Round(r.Height, 1) } }; }

        // the process diagram Process Manager exported beside the procedure file ("<same name> - <time> - Flow Diagram....png"), newest first
        internal static string FindDiagram(string procedureFile)
        {
            try
            {
                if (string.IsNullOrEmpty(procedureFile) || !File.Exists(procedureFile)) return null;
                string dir = Path.GetDirectoryName(procedureFile), name = Path.GetFileNameWithoutExtension(procedureFile); int cut = name.LastIndexOf(" - ");
                string prefix = cut > 0 ? name.Substring(0, cut) : name;
                return Directory.GetFiles(dir, "*.png").Where(f => { string n = Path.GetFileName(f); return n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.IndexOf("Flow Diagram", StringComparison.OrdinalIgnoreCase) >= 0; })
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            }
            catch { return null; }
        }

        public static string Write(SessionDoc doc, string parent, string template) { return Write(doc, parent, template, null); }
        // existingDeck: a deck this activity is to be added to (the pack names it; the deck itself is never copied or changed)
        public static string Write(SessionDoc doc, string parent, string template, string existingDeck)
        {
            string slug = new string((doc.Name ?? "slides").Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : ' ').ToArray()).Trim();
            while (slug.Contains("  ")) slug = slug.Replace("  ", " ");
            if (slug.Length == 0) slug = "slides"; if (slug.Length > 50) slug = slug.Substring(0, 50).Trim();
            string dir = Path.Combine(parent, slug + " - slide pack");
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(parent, slug + " - slide pack " + n);
            Directory.CreateDirectory(dir);

            var slides = new List<object>(); var tasks = new List<Dictionary<string, object>>(); string prevTask = null; int no = 0;
            bool showText = doc.ShowText; doc.ShowText = true;      // the texts are always exported, whether or not the editor shows them
            try
            {
                foreach (var g in doc.Groups())
                {
                    var first = g[0]; Rectangle view; int fullW;
                    using (var full = SessionDoc.Decode(SessionDoc.PictureOf(g))) { if (full == null) continue; fullW = full.Width; }
                    // the screen's own picture with no marks, as chosen and cropped in the editor
                    using (var pic = doc.CleanPicture(g, out view))
                    {
                        if (pic == null) continue;
                        no++; string file = "slide-" + no.ToString("00") + ".png"; pic.Save(Path.Combine(dir, file), ImageFormat.Png);
                        string tcode, ttitle; SplitTask(first.Task, out tcode, out ttitle);
                        bool cont = first.Task.Length > 0 && first.Task == prevTask; prevTask = first.Task;
                        var steps = new List<object>(); int idx = 0; var all = new Rectangle(0, 0, pic.Width, pic.Height);
                        foreach (var s in g)
                        {
                            idx++;
                            // everything below is in the exported picture's own pixels (the crop's corner is 0,0)
                            var box = new Rectangle(s.Box.X - view.X, s.Box.Y - view.Y, s.Box.Width, s.Box.Height);
                            object boxObj = s.Kind != "scroll" && all.IntersectsWith(box) ? Rect(box) : null;
                            object textObj = null;
                            var tb = doc.TextBoxIn(s, fullW, view);
                            if (tb.HasValue)
                            {
                                var r = new RectangleF(tb.Value.X - view.X, tb.Value.Y - view.Y, tb.Value.Width, tb.Value.Height);
                                var d = Rect(r); d["outside"] = !all.Contains((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)); d["placed"] = s.TextPos.HasValue; textObj = d;
                            }
                            steps.Add(new Dictionary<string, object> { { "no", s.Seq }, { "index", idx }, { "text", s.Plain }, { "actionCode", s.ActionCode }, { "label", s.Label ?? "" }, { "kind", s.Kind }, { "box", boxObj }, { "textBox", textObj } });
                        }
                        slides.Add(new Dictionary<string, object> {
                            { "no", no }, { "task", first.Task }, { "taskCode", tcode }, { "taskTitle", ttitle }, { "continued", cont },
                            { "image", file }, { "width", pic.Width }, { "height", pic.Height }, { "steps", steps } });
                        Dictionary<string, object> t = null;
                        if (first.Task.Length > 0) { t = tasks.FirstOrDefault(x => (string)x["task"] == first.Task); if (t == null) { t = new Dictionary<string, object> { { "task", first.Task }, { "code", tcode }, { "title", ttitle }, { "firstSlide", no }, { "slides", new List<object>() } }; tasks.Add(t); } }
                        if (t != null) ((List<object>)t["slides"]).Add(no);
                    }
                }
            }
            finally { doc.ShowText = showText; }
            if (no == 0) { try { Directory.Delete(dir, true); } catch { } throw new InvalidOperationException("The session has no screens with a picture."); }

            string templateName = null;
            if (string.IsNullOrEmpty(existingDeck) && !string.IsNullOrEmpty(template) && File.Exists(template)) { templateName = "template" + Path.GetExtension(template).ToLowerInvariant(); File.Copy(template, Path.Combine(dir, templateName), true); }

            var process = Sub(doc, "process"); var activity = Sub(doc, "activity");
            string source = doc.Procedure != null && doc.Procedure.ContainsKey("source") ? Convert.ToString(doc.Procedure["source"]) : "";
            string diagramFile = FindDiagram(source), diagramName = null;
            if (diagramFile != null) { diagramName = "diagram.png"; File.Copy(diagramFile, Path.Combine(dir, diagramName), true); }
            var root = new Dictionary<string, object> {
                { "schemaVersion", 2 }, { "name", doc.Name },
                { "generatedAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") },
                { "process", process ?? new Dictionary<string, object>() }, { "activity", activity ?? new Dictionary<string, object>() },
                { "template", templateName }, { "appendTo", string.IsNullOrEmpty(existingDeck) ? null : existingDeck },
                // the process flow diagram, when Process Manager's export is beside the procedure file; highlight (the activity's node) is filled in from looking at the picture
                { "diagram", diagramName == null ? null : new Dictionary<string, object> { { "image", diagramName }, { "highlight", null } } },
                { "tasks", tasks.Cast<object>().ToList() },
                { "procedure", doc.Procedure != null && doc.Procedure.ContainsKey("tasks") ? doc.Procedure["tasks"] : new List<object>() },
                { "slides", slides } };
            File.WriteAllText(Path.Combine(dir, "slides.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(root), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, PromptFile), Prompt(dir, Str(activity, "title").Length > 0 ? Str(activity, "title") : doc.Name, existingDeck), new UTF8Encoding(false));
            return dir;
        }

        public static string Prompt(string dir, string activity) { return Prompt(dir, activity, null); }
        public static string Prompt(string dir, string activity, string existingDeck)
        {
            if (!string.IsNullOrEmpty(existingDeck))
                return "Use the xlcapture-slides skill to add the activity \"" + activity + "\" to the existing deck '" + existingDeck + "', from the slide pack folder '" + dir + "'. Follow the skill: look at diagram.png (if there is one) to fill in the highlight, run build_deck.py with --append, check the result, and save the updated deck as a new copy in that folder. Do not change the original deck.";
            return "Use the xlcapture-slides skill to build the slide pack for \"" + activity + "\" from the folder '" + dir + "'. Follow the skill: use template.pptx in that folder, look at diagram.png (if there is one) to fill in the highlight, run build_deck.py, check the result, and save the PowerPoint in that same folder.";
        }
    }
}
