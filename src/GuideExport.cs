// Exports a session as a ClickTrail-style folder (clicks.json + shot-NN.png) so the click-guide-builder skill
// can turn it into a PowerPoint how-to guide (step text on the left, the numbered screenshot on the right).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SnapLite
{
    static class GuideExport
    {
        public static string Write(SessionDoc doc, string parent)
        {
            string slug = new string((doc.Name ?? "guide").Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            if (slug.Length == 0) slug = "guide"; if (slug.Length > 40) slug = slug.Substring(0, 40).Trim('-');
            string dir = Path.Combine(parent, DateTime.Now.ToString("yyyy-MM-dd_HHmm") + "_" + slug);
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(parent, DateTime.Now.ToString("yyyy-MM-dd_HHmm") + "_" + slug + "-" + n);
            Directory.CreateDirectory(dir);

            var shots = new List<object>(); var hosts = new List<string>(); int no = 0;
            foreach (var g in doc.Groups())
            {
                no++; var first = g[0]; string file = null; int pw = first.Vw, ph = first.Vh; Rectangle view = Rectangle.Empty;
                // the screen's own picture with no marks, as chosen and cropped in the editor
                using (var pic = doc.CleanPicture(g, out view))
                    if (pic != null) { file = "shot-" + no.ToString("00") + ".png"; pic.Save(Path.Combine(dir, file), System.Drawing.Imaging.ImageFormat.Png); pw = pic.Width; ph = pic.Height; }
                var clicks = new List<object>(); int marker = 0;
                foreach (var s in g)
                {
                    marker++;
                    var box = new Rectangle(s.Box.X - view.X, s.Box.Y - view.Y, s.Box.Width, s.Box.Height);
                    bool outside = file != null && !new Rectangle(0, 0, pw, ph).IntersectsWith(box);
                    clicks.Add(new Dictionary<string, object> {
                        { "marker", marker }, { "label", s.Label }, { "text", "" }, { "role", (s.Role ?? "").ToLowerInvariant() }, { "tag", "" },
                        { "container", s.AncestorPath }, { "heading", "" }, { "classHint", "" }, { "testId", s.TestId }, { "kind", s.Manual ? "manual" : s.Kind },
                        { "scrollDirection", s.ScrollDirection }, { "manual", s.Manual }, { "viaKeyboard", s.Keyboard }, { "typedInField", s.Typed },
                        { "needsImage", string.IsNullOrEmpty(s.Label) && !s.Manual }, { "screenshotMayBeLate", false }, { "boxEdited", s.BoxEdited }, { "outsideCrop", outside },
                        { "stepText", string.IsNullOrWhiteSpace(s.StepText) ? null : s.StepText }, { "task", s.Task }, { "actionCode", s.ActionCode },
                        { "position", new Dictionary<string, object> { { "x", s.X - view.X }, { "y", s.Y - view.Y } } },
                        { "box", new Dictionary<string, object> { { "x", box.X }, { "y", box.Y }, { "w", box.Width }, { "h", box.Height } } },
                        { "badge", s.BadgePos.HasValue ? new Dictionary<string, object> { { "x", s.BadgePos.Value.X - view.X }, { "y", s.BadgePos.Value.Y - view.Y } } : null } });
                }
                string url = !string.IsNullOrEmpty(first.UrlBar) ? first.UrlBar : first.Url;
                try { var u = new Uri(first.UrlBar ?? ""); if (!hosts.Contains(u.Host)) hosts.Add(u.Host); } catch { }
                shots.Add(new Dictionary<string, object> {
                    { "file", file }, { "width", pw }, { "height", ph }, { "customScreenshot", first.CustomFrame != null },
                    { "pageTitle", first.Title }, { "url", url }, { "task", first.Task }, { "clicks", clicks } });
            }
            var root = new Dictionary<string, object> {
                { "schemaVersion", 1 },
                { "style", new Dictionary<string, object> { { "boxColor", System.Drawing.ColorTranslator.ToHtml(System.Drawing.Color.FromArgb(255, doc.BoxColor)).ToLowerInvariant() }, { "stepColor", System.Drawing.ColorTranslator.ToHtml(System.Drawing.Color.FromArgb(255, doc.StepColor)).ToLowerInvariant() } } },
                { "session", new Dictionary<string, object> { { "id", doc.Id }, { "name", doc.Name }, { "recordedAt", doc.StartedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }, { "hosts", hosts } } },
                { "screenshots", shots } };
            File.WriteAllText(Path.Combine(dir, "clicks.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(root), new UTF8Encoding(false));
            return dir;
        }
    }
}
