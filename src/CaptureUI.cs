// The question asked before a session starts: which window to capture, or the full screen, and is the screen ready.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    class CaptureChoice
    {
        public string Mode = "screen";                 // window | screen (every screen) | monitor | region
        public IntPtr Hwnd; public string Process = "", Title = ""; public Rectangle Area;
        public Dictionary<string, object> ToObject() { return new Dictionary<string, object> { { "mode", Mode }, { "process", Process }, { "title", Title } }; }
    }

    class CaptureTargetForm : Form
    {
        readonly RadioButton rbWindow, rbScreen, rbRegion; readonly ListBox winList; readonly ComboBox screens; readonly CheckBox ready; readonly FlatButton ok;        readonly List<WinInfo> wins = new List<WinInfo>(); public CaptureChoice Result;

        // returns null when cancelled
        public static CaptureChoice Ask(IWin32Window owner, string title, string intro, string okText, string defaultMode, bool allowRegion)
        {
            using (var f = new CaptureTargetForm(title, intro, okText, defaultMode, allowRegion)) return f.ShowDialog(owner) == DialogResult.OK ? f.Result : null;
        }

        CaptureTargetForm(string title, string intro, string okText, string defaultMode, bool allowRegion)
        {
            Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Icon = AppIcon.Get(); TopMost = true;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.White; Font = Theme.Body(9.5f); ClientSize = Dpi.S(560, allowRegion ? 560 : 530);

            Controls.Add(new Label { Text = intro, AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(18)), Size = Dpi.S(512, 44), ForeColor = Theme.Slate });
            rbWindow = new ThemedRadio { Text = "One window", Width = Dpi.S(200), Location = new Point(Dpi.S(24), Dpi.S(72)), Checked = defaultMode == "window" || defaultMode == "" };
            var refresh = new LinkLabel { Text = "Refresh the list", AutoSize = true, Location = new Point(Dpi.S(420), Dpi.S(76)), LinkColor = Theme.AccentInk, ActiveLinkColor = Theme.Charcoal, LinkBehavior = LinkBehavior.HoverUnderline };
            winList = new ListBox { Location = new Point(Dpi.S(44), Dpi.S(102)), Size = Dpi.S(492, 170), BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body(10f), IntegralHeight = false, AccessibleName = "Open windows" }; Theme.List(winList);
            rbScreen = new ThemedRadio { Text = "The full screen", Width = Dpi.S(260), Location = new Point(Dpi.S(24), Dpi.S(288)), Checked = defaultMode == "all" || defaultMode == "screen" || defaultMode == "monitor" };
            screens = new ThemedCombo { Location = new Point(Dpi.S(44), Dpi.S(318)), Width = Dpi.S(340), Font = Theme.Body(10f) };
            screens.Items.Add("Every screen");
            foreach (var sc in Screen.AllScreens) screens.Items.Add("Only " + (sc.Primary ? "the main screen" : "the screen at " + sc.Bounds.X + ", " + sc.Bounds.Y) + "  (" + sc.Bounds.Width + " x " + sc.Bounds.Height + ")");
            screens.SelectedIndex = 0;
            int y = 356;
            rbRegion = new ThemedRadio { Text = "A region of the screen (you drag it after you press the button)", Width = Dpi.S(500), Location = new Point(Dpi.S(24), Dpi.S(y)), Visible = allowRegion, Checked = defaultMode == "region" };
            if (allowRegion) y += 40;
            var rule = new Panel { Location = new Point(Dpi.S(24), Dpi.S(y)), Size = Dpi.S(512, 1), BackColor = Theme.Rule };
            var q = new Label { Text = "Is the screen ready?", AutoSize = true, Location = new Point(Dpi.S(24), Dpi.S(y + 14)), Font = Theme.BodyBold(11f), ForeColor = Theme.Ink };
            var note = new Label { Text = "Open the page the steps start from and close anything that should not appear in the screenshots. Recording starts as soon as you press the button.", AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(y + 40)), Size = Dpi.S(512, 40), ForeColor = Theme.Slate };
            ready = new ThemedCheck { Text = "The screen is ready", Width = Dpi.S(300), Location = new Point(Dpi.S(24), Dpi.S(y + 86)), ForeColor = Theme.Ink };
            ok = new FlatButton(okText, true) { Location = new Point(Dpi.S(320), Dpi.S(y + 124)), Size = Dpi.S(130, 36), Enabled = false };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(458), Dpi.S(y + 124)), Size = Dpi.S(78, 36), DialogResult = DialogResult.Cancel };
            ClientSize = new Size(ClientSize.Width, Dpi.S(y + 176));
            Controls.AddRange(new Control[] { rbWindow, refresh, winList, rbScreen, screens, rbRegion, rule, q, note, ready, ok, cancel }); CancelButton = cancel;

            Action state = delegate
            {
                winList.Enabled = rbWindow.Checked; screens.Enabled = rbScreen.Checked;
                ok.Enabled = ready.Checked && (!rbWindow.Checked || winList.SelectedIndex >= 0);
            };
            rbWindow.CheckedChanged += delegate { state(); }; rbScreen.CheckedChanged += delegate { state(); }; rbRegion.CheckedChanged += delegate { state(); };
            ready.CheckedChanged += delegate { state(); }; winList.SelectedIndexChanged += delegate { state(); };
            winList.DoubleClick += delegate { if (ok.Enabled) Accept(); };
            refresh.Click += delegate { Fill(); state(); };
            ok.Click += delegate { Accept(); };
            Fill(); state();
            if (!rbWindow.Checked && !rbScreen.Checked && !rbRegion.Checked) rbWindow.Checked = true;
        }

        void Fill()
        {
            string keep = winList.SelectedItem as string; winList.Items.Clear(); wins.Clear();
            foreach (var w in Native.Windows())
            {
                if (string.IsNullOrWhiteSpace(w.Title)) continue;
                string proc = Driver.ProcName(Native.ProcessIdOf(w.Handle));
                if (proc.Equals("explorer", StringComparison.OrdinalIgnoreCase) && (w.Title == "Program Manager" || w.Title.Length == 0)) continue;
                wins.Add(w); winList.Items.Add(w.Title + "   (" + proc + ")");
            }
            int i = keep == null ? -1 : winList.Items.IndexOf(keep);
            winList.SelectedIndex = i >= 0 ? i : (winList.Items.Count > 0 ? 0 : -1);
        }

        void Accept()
        {
            var c = new CaptureChoice();
            if (rbWindow.Checked)
            {
                var w = wins[winList.SelectedIndex]; c.Mode = "window"; c.Hwnd = w.Handle; c.Title = w.Title; c.Area = w.Rect; c.Process = Driver.ProcName(Native.ProcessIdOf(w.Handle));
            }
            else if (rbRegion.Checked && rbRegion.Visible) c.Mode = "region";
            else if (screens.SelectedIndex > 0) { c.Mode = "monitor"; c.Area = Screen.AllScreens[screens.SelectedIndex - 1].Bounds; }
            else c.Mode = "screen";
            Result = c; DialogResult = DialogResult.OK; Close();
        }
    }
}
