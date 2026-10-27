// The skill that tells Claude Code how to work with XLCapture travels inside the program: xlcapture-slides (build the slide pack PowerPoint
// from an exported folder). It is put in the user's own Claude folder the first time it is needed, and again when this program carries a newer version of it.
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SnapLite
{
    static class SkillInstaller
    {
        public static readonly string[] Names = { "xlcapture-slides" };
        static string Target(string name) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "skills", name, "SKILL.md"); }
        static int VersionOf(string text) { var m = Regex.Match(text ?? "", @"<!--\s*xlcapture-skill:\s*(\d+)\s*-->"); return m.Success ? int.Parse(m.Groups[1].Value) : 0; }

        public static string Embedded() { return Embedded(Names[0]); }
        public static string Embedded(string name) { return Embedded(name, ".md"); }
        public static string Embedded(string name, string ext)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("XLCapture." + name + ext))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, System.Text.Encoding.UTF8)) return r.ReadToEnd();
            }
        }
        // returns true when every skill is in place
        public static bool EnsureInstalled()
        {
            RemoveLegacy();
            bool all = true;
            foreach (var n in Names) all &= EnsureInstalled(n);
            return all;
        }
        static bool EnsureInstalled(string name)
        {
            try
            {
                string mine = Embedded(name); if (mine == null) return false;
                if (name == "xlcapture-slides")
                {
                    string py = Embedded("xlcapture-build-deck", ".py"), pyTarget = Path.Combine(Path.GetDirectoryName(Target(name)), "build_deck.py");
                    if (py != null && (!File.Exists(pyTarget) || File.ReadAllText(pyTarget) != py)) { Directory.CreateDirectory(Path.GetDirectoryName(pyTarget)); File.WriteAllText(pyTarget, py, new System.Text.UTF8Encoding(false)); }
                }
                string t = Target(name), have = File.Exists(t) ? File.ReadAllText(t) : null;
                if (have != null && VersionOf(have) >= VersionOf(mine)) return true;
                Directory.CreateDirectory(Path.GetDirectoryName(t)); File.WriteAllText(t, mine, new System.Text.UTF8Encoding(false)); return true;
            }
            catch { return false; }
        }
        // the skill earlier versions put there to drive XLCapture through a procedure: this version cannot be driven that way, so take it out again (only if XLCapture put it there)
        static void RemoveLegacy()
        {
            try { string t = Target("snaplite-teach"); if (File.Exists(t) && VersionOf(File.ReadAllText(t)) > 0) Directory.Delete(Path.GetDirectoryName(t), true); } catch { }
        }
        // for the uninstaller: take the skills out again if XLCapture put them there
        public static void Remove()
        {
            foreach (var n in new[] { Names[0], "snaplite-teach" })
                try { string t = Target(n); if (File.Exists(t) && VersionOf(File.ReadAllText(t)) > 0) Directory.Delete(Path.GetDirectoryName(t), true); } catch { }
        }
    }
}
