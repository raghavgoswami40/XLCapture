// Small UI Automation helpers shared by the recorder, the driver and replay:
// how to name an element's place in a window, and how to read the address of a browser page.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation;

namespace SnapLite
{
    static class Ui
    {
        static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore" };
        public static bool IsBrowser(string proc) { return !string.IsNullOrEmpty(proc) && Browsers.Contains(proc.ToLowerInvariant()); }

        public static string RoleOf(AutomationElement a)
        {
            try { var pn = a.Current.ControlType != null ? a.Current.ControlType.ProgrammaticName : ""; return pn.StartsWith("ControlType.") ? pn.Substring(12) : pn; }
            catch { return ""; }
        }

        // the named containers around an element, outermost first, without the window itself (its title changes):
        // "Pane: Main > Group: Cost items". Used to tell apart two controls that share a name.
        public static string AncestorPath(AutomationElement a)
        {
            var parts = new List<string>();
            try
            {
                var w = TreeWalker.ControlViewWalker; var p = a == null ? null : w.GetParent(a); int depth = 0;
                while (p != null && depth++ < 12 && !Equals(p, AutomationElement.RootElement))
                {
                    string role = RoleOf(p);
                    if (role == "Window") break;
                    string name = ""; try { name = (p.Current.Name ?? "").Trim(); } catch { }
                    if (name.Length > 0 && (role == "Pane" || role == "Group" || role == "Tab" || role == "ToolBar" || role == "Document" || role == "List" || role == "Table" || role == "DataGrid" || role == "Tree" || role == "MenuBar" || role == "Menu"))
                        parts.Add(role + ": " + (name.Length > 40 ? name.Substring(0, 40) : name));
                    p = w.GetParent(p);
                }
            }
            catch { }
            parts.Reverse();
            return string.Join(" > ", parts.Skip(Math.Max(0, parts.Count - 3)).ToArray());
        }

        // a browser page's address, without the query string (which can hold tokens or personal data)
        static IntPtr cacheWin; static string cacheTitle, cacheUrl;     // one lookup per page, not per click
        public static string UrlBarOf(IntPtr hwnd, string proc)
        {
            if (!IsBrowser(proc) || hwnd == IntPtr.Zero) return "";
            string title = Native.TitleOf(hwnd);
            if (hwnd == cacheWin && title == cacheTitle) return cacheUrl;
            string url = "";
            try { url = UrlBar(AutomationElement.FromHandle(hwnd), proc); } catch { }
            cacheWin = hwnd; cacheTitle = title; cacheUrl = url; return url;
        }
        public static string UrlBar(AutomationElement win, string proc)
        {
            if (!IsBrowser(proc) || win == null) return "";
            try
            {
                var edits = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                foreach (AutomationElement e in edits)
                {
                    string n = (e.Current.Name ?? "").ToLowerInvariant();
                    if (n.IndexOf("address") < 0 && n.IndexOf("url") < 0 && n.IndexOf("enter address") < 0) continue;
                    object p; if (!e.TryGetCurrentPattern(ValuePattern.Pattern, out p)) continue;
                    string v = ((ValuePattern)p).Current.Value ?? ""; int q = v.IndexOf('?'); if (q >= 0) v = v.Substring(0, q);
                    return v.Trim();
                }
            }
            catch { }
            return "";
        }
    }
}
