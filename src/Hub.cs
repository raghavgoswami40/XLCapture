// The capture window (Snagit-style): tabs, options card, round Capture button, presets. Also tray, hotkeys, countdown.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace SnapLite
{
    class ModeDef { public CapMode Mode; public string Key; public string Name, Hint; public Ic Icon; public bool Repeat; }

    static class Modes
    {
        public static readonly ModeDef[] All =
        {
            new ModeDef { Mode = CapMode.AllInOne, Key = "capture", Name = "Capture (current tab)", Hint = "Uses the settings in the capture window", Icon = Ic.AllInOne },
            new ModeDef { Mode = CapMode.Region, Key = "region", Name = "Region", Hint = "Drag any rectangle on screen", Icon = Ic.SelectRect },
            new ModeDef { Mode = CapMode.Window, Key = "window", Name = "Window", Hint = "Click the window to capture", Icon = Ic.Image },
            new ModeDef { Mode = CapMode.Full, Key = "full", Name = "Full screen", Hint = "Every monitor at once", Icon = Ic.Fit },
            new ModeDef { Mode = CapMode.Scroll, Key = "scroll", Name = "Scrolling window", Hint = "Scrolls for you and stitches a tall picture", Icon = Ic.Order },
            new ModeDef { Mode = CapMode.ScrollH, Key = "scrollh", Name = "Panorama", Hint = "Scrolls sideways and stitches a wide picture", Icon = Ic.Move },
            new ModeDef { Mode = CapMode.Element, Key = "object", Name = "UI element", Hint = "Snap to a button, panel or control", Icon = Ic.Cursor },
            new ModeDef { Mode = CapMode.Fixed, Key = "fixed", Name = "Fixed size", Hint = "Place a set-size frame", Icon = Ic.Crop },
            new ModeDef { Mode = CapMode.Multi, Key = "multi", Name = "Multiple areas", Hint = "Pick several areas, joined in one picture", Icon = Ic.Layers },
            new ModeDef { Mode = CapMode.Freehand, Key = "freehand", Name = "Freehand", Hint = "Draw around an irregular shape", Icon = Ic.Lasso },
            new ModeDef { Mode = CapMode.Text, Key = "text", Name = "Grab text", Hint = "Copy the words in an area (OCR)", Icon = Ic.Ocr },
            new ModeDef { Mode = CapMode.Video, Key = "video", Name = "Record video", Hint = "Screen recording to MP4", Icon = Ic.Video },
            new ModeDef { Mode = CapMode.Gif, Key = "gif", Name = "Record GIF", Hint = "Short looping animation", Icon = Ic.Clock },
            new ModeDef { Mode = CapMode.Session, Key = "session", Name = "Record session", Hint = "Each click becomes a numbered step with a screenshot", Icon = Ic.Session },
            new ModeDef { Mode = CapMode.Region, Key = "repeat", Name = "Repeat last capture", Hint = "Same area again", Icon = Ic.Redo, Repeat = true },
        };
        // Image tab "Selection" choices, in display order
        public static readonly string[] ImageKeys = { "region", "window", "full", "scroll", "scrollh", "object", "fixed", "multi", "freehand", "text" };
        public static ModeDef ByKey(string k) { return All.FirstOrDefault(m => m.Key == k) ?? All[1]; }
    }

    class Profile { public string Name; public Dictionary<string, string> Data = new Dictionary<string, string>(); }

    class HotkeyWindow : NativeWindow
    {
        public event Action<int> Pressed;
        public HotkeyWindow() { CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    class CountdownForm : Form
    {
        int left; readonly Timer t = new Timer { Interval = 1000 }; readonly Action done;
        public CountdownForm(int seconds, Action done)
        {
            left = seconds; this.done = done;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Ink;
            Size = Dpi.S(150, 150); var wa = Screen.PrimaryScreen.WorkingArea; Location = new Point(wa.Right - Width - Dpi.S(24), wa.Top + Dpi.S(24));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            t.Tick += delegate { left--; if (left <= 0) { t.Stop(); Close(); } else Invalidate(); };
            FormClosed += delegate { t.Dispose(); if (left <= 0 && this.done != null) this.done(); };
            Click += delegate { left = 0; t.Stop(); Close(); };
            Shown += delegate { t.Start(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Ink);
            Theme.CropMarks(g, new Rectangle(Dpi.S(26), Dpi.S(26), Width - Dpi.S(52), Height - Dpi.S(52)), 0, Dpi.S(14), Color.FromArgb(120, 255, 255, 255));
            using (var f = new Font(Theme.LabelFamily, Dpi.F(54f), FontStyle.Bold, GraphicsUnit.Pixel)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(left.ToString(), f, Brushes.White, new RectangleF(0, 0, Width, Height - Dpi.S(14)), sf);
            using (var f = Theme.Body(8.5f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString("click to start now", f, new SolidBrush(Theme.Mist), new RectangleF(0, Height - Dpi.S(30), Width, Dpi.S(20)), sf);
        }
    }

    // white rounded card that hosts the tab's options
    class Card : Panel
    {
        public Card() { BackColor = Color.White; DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Dpi.S(12))) using (var pen = new Pen(Theme.Rule)) e.Graphics.DrawPath(pen, p);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var p = Theme.RoundRect(new RectangleF(0, 0, Width, Height), Dpi.S(12))) Region = new Region(p);
        }
    }

    class Hub : DpiForm
    {
        public static Hub Instance;
        readonly NotifyIcon tray; readonly HotkeyWindow hk = new HotkeyWindow();
        readonly Dictionary<int, string> hotIds = new Dictionary<int, string>();
        readonly TabButton tabAio, tabImg, tabVid, tabSes; readonly Card card; readonly RoundButton capture; readonly KeyHint hotkeyLabel;
        readonly Panel presetsPanel; readonly PresetPanel presetList; readonly ToolTip tips = new ToolTip();
        bool busy, quitting, presetsOpen;
        public static List<Profile> Profiles = new List<Profile>();
        const int BaseH = 442, PresetH = 250;
        int minW;
        // the window can be made wider, not taller: everything in it sits at a fixed height
        protected override void AfterDpiChange() { minW = Width; LockHeight(); }
        void LockHeight() { MinimumSize = new Size(minW, Height); MaximumSize = new Size(20000, Height); }

        public Hub()
        {
            Instance = this;
            Text = "XLCapture"; Icon = AppIcon.Get(); BackColor = Theme.Paper; ForeColor = Theme.Ink; Font = Theme.Body(9.5f);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = Dpi.S(890, BaseH); KeyPreview = true;
            minW = Width; LockHeight();

            var menu = new MenuStrip { BackColor = Theme.Paper, Font = Theme.Body(10f), Renderer = new ThemeRenderer(), Padding = Dpi.P(8, 4, 0, 2), ImageScalingSize = Dpi.S(22, 22) };
            var logo = new ToolStripLabel(Brand.MarkTile(Dpi.S(26))) { Margin = Dpi.P(0, 0, 10, 0), AutoSize = false, Size = Dpi.S(26, 26), ImageScaling = ToolStripItemImageScaling.None, AccessibleName = "XLCapture" };
            menu.Items.Add(logo);
            menu.Items.Add(Menu("&File", MI("New capture", "Ctrl+N", delegate { CaptureCurrent(); }), MI("Open XLCapture Editor", "Ctrl+E", delegate { OpenEditor(); }),
                MI("Open picture...", "Ctrl+O", delegate { OpenPicture(); }), MI("Library", null, delegate { EditorForm.ShowLibrary(); }), new ToolStripSeparator(),
                MI("Hide to tray", null, delegate { Hide(); }), MI("Exit XLCapture", null, delegate { Quit(); })));
            menu.Items.Add(Menu("&Edit", MI("Reset this tab", null, delegate { ResetTab(); }), MI("Preferences and hotkeys...", null, delegate { OpenSettings(); })));
            var top = new ToolStripMenuItem("Keep on top") { CheckOnClick = true }; top.CheckedChanged += delegate { TopMost = top.Checked; };
            menu.Items.Add(Menu("&View", top, MI("Presets", null, delegate { TogglePresets(); }), MI("Library", null, delegate { EditorForm.ShowLibrary(); })));
            menu.Items.Add(Menu("&Help", MI("Keyboard shortcuts", null, delegate { ShowShortcuts(); }), MI("About XLCapture", null, delegate { using (var a = new AboutForm()) a.ShowDialog(this); })));
            MainMenuStrip = menu;

            tabAio = new TabButton(Ic.AllInOne, "All-in-One") { Location = new Point(Dpi.S(6), Dpi.S(40)), Size = Dpi.S(104, 64) };
            tabImg = new TabButton(Ic.Camera, "Image") { Location = new Point(Dpi.S(6), Dpi.S(104)), Size = Dpi.S(104, 64) };
            tabVid = new TabButton(Ic.Video, "Video") { Location = new Point(Dpi.S(6), Dpi.S(168)), Size = Dpi.S(104, 64) };
            tabSes = new TabButton(Ic.Session, "Sessions") { Location = new Point(Dpi.S(6), Dpi.S(232)), Size = Dpi.S(104, 64) };
            tabAio.Click += delegate { SetTab("aio"); }; tabImg.Click += delegate { SetTab("image"); }; tabVid.Click += delegate { SetTab("video"); }; tabSes.Click += delegate { SetTab("session"); };

            card = new Card { Location = new Point(Dpi.S(118), Dpi.S(44)), Size = Dpi.S(600, 316) };
            capture = new RoundButton("Capture") { Location = new Point(Dpi.S(738), Dpi.S(76)), Size = Dpi.S(130, 130) };
            capture.Click += delegate { CaptureCurrent(); };
            hotkeyLabel = new KeyHint { Location = new Point(Dpi.S(720), Dpi.S(216)), Size = Dpi.S(166, 30) };
            tips.SetToolTip(hotkeyLabel, "The hotkey works from anywhere, even with this window hidden. Change it in Edit > Preferences and hotkeys.");
            // under the hotkey, a labelled reset (an unlabelled arrow on its own was easy to miss and hard to guess)
            var reset = new FlatButton("Reset tab", false) { Location = new Point(Dpi.S(749), Dpi.S(318)), Size = Dpi.S(108, 32), Icon = Ic.Reset, Quiet = true, Font = Theme.Body(9.5f), AccessibleName = "Reset this tab" };
            reset.Click += delegate { ResetTab(); }; tips.SetToolTip(reset, "Put this tab's settings back to their defaults");

            // the bottom bar: presets on the left as quiet toolbar buttons, the editor on the right as the one outlined button
            var presets = new FlatButton("Presets", false) { Location = new Point(Dpi.S(10), Dpi.S(396)), Size = Dpi.S(112, 34), Icon = Ic.Gear, Quiet = true, Chevron = true };
            presets.Click += delegate { TogglePresets(); }; tips.SetToolTip(presets, "Show the capture types and your saved presets, with their hotkeys");
            var add = new FlatButton("Save preset", false) { Location = new Point(Dpi.S(126), Dpi.S(396)), Size = Dpi.S(124, 34), Icon = Ic.Plus, Quiet = true, AccessibleName = "New preset" };
            tips.SetToolTip(add, "Save the current settings as a preset");
            add.Click += delegate { SavePreset(); };
            var openEd = new FlatButton("Open XLCapture Editor", false) { Location = new Point(Dpi.S(668), Dpi.S(396)), Size = Dpi.S(208, 34), Icon = Ic.EditBox };
            openEd.Click += delegate { OpenEditor(); };

            presetsPanel = new Panel { Location = new Point(Dpi.S(14), Dpi.S(BaseH)), Size = Dpi.S(862, PresetH - 14), BackColor = Color.White, Visible = false };
            presetList = new PresetPanel { Dock = DockStyle.Fill, HotkeyOf = r => HotkeyText(r.Key) };
            presetList.HotkeyClicked += EditHotkey; presetList.GearClicked += PresetMenu;
            presetList.Activated += RunRow;
            presetsPanel.Controls.Add(presetList);

            // widening the window stretches the card and keeps the Capture button on the right
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            capture.Anchor = hotkeyLabel.Anchor = reset.Anchor = openEd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            presetsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.AddRange(new Control[] { tabAio, tabImg, tabVid, tabSes, card, capture, hotkeyLabel, reset, presets, add, openEd, presetsPanel });
            Controls.Add(menu);

            // tray + hotkeys
            var trayMenu = new ContextMenuStrip { Renderer = new ThemeRenderer(), BackColor = Theme.Sheet, Font = Theme.Body(9f) };
            trayMenu.Items.Add("Capture", null, delegate { CaptureCurrent(); });
            foreach (var m in Modes.All.Skip(1)) { var md = m; trayMenu.Items.Add(md.Name, null, delegate { Begin(md); }); }
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Show capture window", null, delegate { ShowHub(); });
            trayMenu.Items.Add("Open XLCapture Editor", null, delegate { OpenEditor(); });
            trayMenu.Items.Add("Exit", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = AppIcon.Get(), Text = "XLCapture", Visible = true, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { ShowHub(); };
            tray.BalloonTipClicked += delegate { if (toastClick != null) { var a = toastClick; toastClick = null; a(); } };
            hk.Pressed += delegate(int id) { string k; if (hotIds.TryGetValue(id, out k)) BeginInvoke((Action)delegate { RunKey(k); }); };

            var forceHandle = Handle;   // tray mode never shows the window, but hotkeys and recorder callbacks need a handle to marshal onto
            RefreshPresets(); SetTab(Settings.CapTab); RegisterHotkeys();
            Shown += delegate { ActiveControl = capture; };   // Enter captures straight away
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); Toast("XLCapture is still running", "Your hotkeys keep working. Double-click the tray icon to come back."); } };
            FormClosed += delegate { UnregisterAll(); tray.Visible = false; tray.Dispose(); Application.ExitThread(); };
        }

        // the full logo (mark and name) as a dark chip of a given height, to sit on a light bar
        static Bitmap FullChip(int h)
        {
            var f = Brand.Full; int w = f == null ? h * 2 : (int)(h * (float)f.Width / f.Height); var b = new Bitmap(w, h);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var p = Theme.RoundRect(new RectangleF(0, 0, w - 1, h - 1), h * 0.22f)) using (var br = new SolidBrush(Brand.Dark)) g.FillPath(br, p);
                if (f != null) { g.SetClip(Theme.RoundRect(new RectangleF(0, 0, w - 1, h - 1), h * 0.22f)); g.DrawImage(f, 0, 0, w, h); }
            }
            return b;
        }
        static ToolStripMenuItem Menu(string text, params ToolStripItem[] items) { var m = new ToolStripMenuItem(text); m.DropDown.Renderer = new ThemeRenderer(); m.DropDownItems.AddRange(items); return m; }
        static ToolStripMenuItem MI(string text, string keys, EventHandler h) { var m = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys }; m.Click += h; return m; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { CaptureCurrent(); return true; }
            if (keyData == (Keys.Control | Keys.E)) { OpenEditor(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenPicture(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------------------------------------------- tabs and the options card
        public void SetTab(string tab)
        {
            if (tab != "aio" && tab != "video" && tab != "session") tab = "image";
            Settings.CapTab = tab; Settings.Save();
            tabAio.Selected = tab == "aio"; tabImg.Selected = tab == "image"; tabVid.Selected = tab == "video"; tabSes.Selected = tab == "session";
            tabAio.Invalidate(); tabImg.Invalidate(); tabVid.Invalidate(); tabSes.Invalidate();
            BuildCard();
            string hkText; Settings.Hotkeys.TryGetValue("capture", out hkText);
            hotkeyLabel.Text = string.IsNullOrEmpty(hkText) ? "No hotkey" : hkText;
            UpdateCaptureButton();
        }

        public void UpdateCaptureButton()
        {
            string tab = Settings.CapTab;
            capture.Text = tab == "session" ? (SessionRecorder.Active ? "Stop" : "Record") : tab == "video" ? "Record" : "Capture";
            capture.Fill = capture.Text == "Capture" ? Theme.Accent : Theme.Mark;      // red means it records, or stops a recording
            capture.Invalidate();
            string k; Settings.Hotkeys.TryGetValue(tab == "session" ? "session" : "capture", out k);
            hotkeyLabel.Text = string.IsNullOrEmpty(k) ? "No hotkey" : k;
        }

        int RowY(int i) { return Dpi.S(58 + i * 58); }
        // each column of the card opens with a short heading that says what its rows are for
        void Heading(string t, int x)
        {
            card.Controls.Add(new Label { Text = t, AutoSize = false, Location = new Point(Dpi.S(x), Dpi.S(18)), Size = Dpi.S(240, 24), ForeColor = Theme.Ink, Font = Theme.Semi(10.5f), UseMnemonic = false });
        }
        Label RowLabel(string t, int x, int i, int w)
        {
            var l = new Label { Text = t, AutoSize = false, Location = new Point(x, RowY(i) + Dpi.S(4)), Size = new Size(Dpi.S(w), Dpi.S(26)), ForeColor = Theme.InkSoft, Font = Theme.Body(10f), UseMnemonic = false };
            card.Controls.Add(l); return l;
        }
        ComboBox RowCombo(int i, string[] items, int index, Action<int> changed)
        {
            var c = new ThemedCombo { Location = new Point(Dpi.S(112), RowY(i)), Width = Dpi.S(190), Font = Theme.Body(10f) };
            c.Items.AddRange(items); c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, index));
            c.SelectedIndexChanged += delegate { changed(c.SelectedIndex); Settings.Save(); };
            card.Controls.Add(c); return c;
        }
        ToggleSwitch RowToggle(int i, string label, bool value, Action<bool> changed, string disabledWhy)
        {
            var l = RowLabel(label, Dpi.S(344), i, 150);
            var t = new ToggleSwitch(value) { Location = new Point(Dpi.S(500), RowY(i) + Dpi.S(2)), AccessibleName = label, Enabled = disabledWhy == null };
            t.Toggled += delegate { changed(t.Checked); Settings.Save(); };
            if (disabledWhy != null) { l.ForeColor = Theme.Mist; tips.SetToolTip(t, disabledWhy); tips.SetToolTip(l, disabledWhy); }
            card.Controls.Add(t); return t;
        }
        Button Chevron(int i, EventHandler click, bool enabled, string tip)
        {
            var b = new FlatButton("", false) { Location = new Point(Dpi.S(556), RowY(i) - Dpi.S(1)), Size = Dpi.S(30, 30), Icon = Ic.Chevron, Bare = true, Tone = Theme.Slate, Enabled = enabled, AccessibleName = tip };
            b.Click += click; tips.SetToolTip(b, tip);
            card.Controls.Add(b); return b;
        }

        void BuildCard()
        {
            card.SuspendLayout();
            foreach (Control c in card.Controls.Cast<Control>().ToList()) { card.Controls.Remove(c); c.Dispose(); }
            card.Padding = Padding.Empty;
            string[] shares = { "None", "Save as a file...", "Email", "FTP upload", "Print", "Show in folder" };
            string[] shareKeys = { "none", "file", "email", "ftp", "print", "folder" };
            Action<int> share = delegate(int i) { Settings.ImgShare = shareKeys[i]; };
            int shareIdx = Math.Max(0, Array.IndexOf(shareKeys, Settings.ImgShare));

            if (Settings.CapTab == "session")
            {
                Heading("Session", 20); Heading("Options", 344);
                string[] tk = { "pick", "all" };
                RowLabel("Record", Dpi.S(20), 0, 92);
                RowCombo(0, new[] { "Pick on screen", "Every screen" }, Settings.SesTarget == "all" ? 1 : 0, delegate(int i) { Settings.SesTarget = tk[i]; });
                string[] ok = { "editor", "folder" };
                RowLabel("Open in", Dpi.S(20), 1, 92); RowCombo(1, new[] { "XLCapture Editor", "Folder only" }, Settings.SesOpenIn == "folder" ? 1 : 0, delegate(int i) { Settings.SesOpenIn = ok[i]; });
                var hint = new Label { Text = "Press Record, then click a window (a browser: just its page) or drag an area. Clicks outside it are not recorded.", Location = new Point(Dpi.S(20), RowY(2) - Dpi.S(6)), Size = Dpi.S(300, 66), ForeColor = Theme.Slate, Font = Theme.Body(9.5f) };
                card.Controls.Add(hint);
                var procBtn = new FlatButton(Settings.SesProcFile.Length > 0 ? "Following: " + Settings.SesProcTitle : "Follow a procedure (optional)...", false) { Location = new Point(Dpi.S(20), RowY(2) + Dpi.S(64)), Size = Dpi.S(282, 34), Icon = Ic.Open, Font = Theme.Body(9.5f), AccessibleName = "Follow a procedure" };
                procBtn.Click += delegate { PickProcedure(); };
                card.Controls.Add(procBtn);
                RowToggle(0, "Record typing", Settings.SesTyping, delegate(bool b) { Settings.SesTyping = b; }, null);
                RowToggle(1, "Record scrolling", Settings.SesScroll, delegate(bool b) { Settings.SesScroll = b; }, null);
                var delayLabel = RowLabel(DelayText(), Dpi.S(344), 2, 150);
                var dt = new ToggleSwitch(Settings.DelayOn) { Location = new Point(Dpi.S(500), RowY(2) + Dpi.S(2)), AccessibleName = "Time delay" };
                dt.Toggled += delegate { Settings.DelayOn = dt.Checked; if (Settings.DelaySec <= 0) Settings.DelaySec = 3; delayLabel.Text = DelayText(); Settings.Save(); };
                card.Controls.Add(dt);
                Chevron(2, delegate(object s, EventArgs e) { DelayMenu((Control)s, delayLabel, dt); }, true, "Choose the delay");
            }
            else if (Settings.CapTab == "video")
            {
                Heading("Record", 20); Heading("Include", 344);
                string[] selKeys = { "region", "window", "full" };
                RowLabel("Selection", Dpi.S(20), 0, 92); RowCombo(0, new[] { "Region", "Window", "Full screen" }, Math.Max(0, Array.IndexOf(selKeys, Settings.VidSel)), delegate(int i) { Settings.VidSel = selKeys[i]; });
                RowLabel("Format", Dpi.S(20), 1, 92); RowCombo(1, new[] { "MP4 video", "Animated GIF" }, Settings.VidFormat == "gif" ? 1 : 0, delegate(int i) { Settings.VidFormat = i == 1 ? "gif" : "mp4"; });
                string[] openKeys = { "editor", "player", "folder" };
                RowLabel("Open in", Dpi.S(20), 2, 92); RowCombo(2, new[] { "XLCapture Editor", "Default video player", "Folder only" }, Math.Max(0, Array.IndexOf(openKeys, Settings.VidOpenIn)), delegate(int i) { Settings.VidOpenIn = openKeys[i]; });
                bool camOk = Camera.Available;
                var cam = RowToggle(0, "Camera", Settings.VidCamera && camOk, delegate(bool b) { Settings.VidCamera = b; }, camOk ? null : "No camera was found");
                if (camOk) tips.SetToolTip(cam, "Show your webcam in the bottom-right corner of the recording");
                RowToggle(1, "Cursor", Settings.VidCursor, delegate(bool b) { Settings.VidCursor = b; }, null);
                Chevron(1, delegate { using (var f = new CursorOptionsForm()) if (f.ShowDialog(this) == DialogResult.OK) f.Apply(); }, true, "Cursor options: highlight and click animation");
                RowToggle(2, "Microphone", Settings.MicOn && Mic.Available, delegate(bool b) { Settings.MicOn = b; }, Mic.Available ? null : "No microphone was found");
                bool sysOk = SystemAudio.Available;
                RowToggle(3, "System audio", Settings.VidSysAudio && sysOk, delegate(bool b) { Settings.VidSysAudio = b; }, sysOk ? null : "No sound output device was found");
            }
            else
            {
                Heading("Capture", 20); Heading("Options", 344);
                if (Settings.CapTab == "aio")
                {
                    // the selection is always "whatever you point at", so the tab says how it works instead of showing a locked drop-down
                    var hint = new Label { Text = "Click a window or drag an area. Then choose what to make of it: a picture, a video, a GIF, a scrolling capture or its text.", Location = new Point(Dpi.S(20), RowY(0) + Dpi.S(2)), Size = Dpi.S(290, 70), ForeColor = Theme.InkSoft, Font = Theme.Body(10f) };
                    card.Controls.Add(hint);
                    RowLabel("Share", Dpi.S(20), 2, 92); RowCombo(2, shares, shareIdx, share);
                }
                else
                {
                    string[] names = Modes.ImageKeys.Select(k => Modes.ByKey(k).Name).ToArray();
                    RowLabel("Selection", Dpi.S(20), 0, 92);
                    RowCombo(0, names, Math.Max(0, Array.IndexOf(Modes.ImageKeys, Settings.ImgSel)), delegate(int i) { Settings.ImgSel = Modes.ImageKeys[i]; });
                    var gear = new FlatButton("", false) { Location = new Point(Dpi.S(304), RowY(0) - Dpi.S(1)), Size = Dpi.S(30, 30), Icon = Ic.Gear, Bare = true, Tone = Theme.Slate, AccessibleName = "Selection options" };
                    tips.SetToolTip(gear, "Selection options: fixed size, repeat the last selection");
                    gear.Click += delegate { SelectionMenu(gear); }; card.Controls.Add(gear);
                    string[] fx = { "None", "Border", "Drop shadow", "Torn edge", "Soft edge", "Grayscale", "Sepia" }; string[] fxKeys = { "none", "border", "shadow", "torn", "fade", "gray", "sepia" };
                    RowLabel("Effects", Dpi.S(20), 1, 92); RowCombo(1, fx, Math.Max(0, Array.IndexOf(fxKeys, Settings.ImgEffect)), delegate(int i) { Settings.ImgEffect = fxKeys[i]; });
                    RowLabel("Share", Dpi.S(20), 2, 92); RowCombo(2, shares, shareIdx, share);
                }
                RowToggle(0, "Preview in editor", Settings.PreviewInEditor, delegate(bool b) { Settings.PreviewInEditor = b; }, null);
                RowToggle(1, "Copy to clipboard", Settings.CopyToClip, delegate(bool b) { Settings.CopyToClip = b; }, null);
                RowToggle(2, "Cursor", Settings.ShowCursor, delegate(bool b) { Settings.ShowCursor = b; }, null);
                var delayLabel = RowLabel(DelayText(), Dpi.S(344), 3, 150);
                var dt = new ToggleSwitch(Settings.DelayOn) { Location = new Point(Dpi.S(500), RowY(3) + Dpi.S(2)), AccessibleName = "Time delay" };
                dt.Toggled += delegate { Settings.DelayOn = dt.Checked; if (Settings.DelaySec <= 0) Settings.DelaySec = 3; delayLabel.Text = DelayText(); Settings.Save(); };
                card.Controls.Add(dt);
                Chevron(3, delegate(object s, EventArgs e) { DelayMenu((Control)s, delayLabel, dt); }, true, "Choose the delay");
            }
            card.ResumeLayout();
        }
        static string DelayText() { return "Time delay" + (Settings.DelayOn ? " (" + Math.Max(1, Settings.DelaySec) + "s)" : ""); }
        void DelayMenu(Control at, Label label, ToggleSwitch sw)
        {
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f) };
            foreach (var s in new[] { 1, 2, 3, 5, 10, 15, 30 })
            {
                int sec = s; var it = new ToolStripMenuItem(sec + (sec == 1 ? " second" : " seconds")) { Checked = Settings.DelayOn && Settings.DelaySec == sec };
                it.Click += delegate { Settings.DelaySec = sec; Settings.DelayOn = true; sw.Checked = true; label.Text = DelayText(); Settings.Save(); };
                m.Items.Add(it);
            }
            m.Show(at, new Point(0, at.Height));
        }
        void SelectionMenu(Control at)
        {
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f) };
            m.Items.Add("Fixed size: " + Settings.FixedW + " x " + Settings.FixedH + "...", null, delegate
            {
                var r = Dialogs.Form(this, "Fixed size", "Save", new Dialogs.Field { Label = "Width (px)", Kind = "int", Value = Settings.FixedW, Min = 16, Max = 16000 }, new Dialogs.Field { Label = "Height (px)", Kind = "int", Value = Settings.FixedH, Min = 16, Max = 16000 });
                if (r != null) { Settings.FixedW = (int)r["Width (px)"]; Settings.FixedH = (int)r["Height (px)"]; Settings.Save(); }
            });
            m.Items.Add("Repeat the last selection", null, delegate { Begin(Modes.ByKey("repeat")); });
            m.Items.Add("Preferences and hotkeys...", null, delegate { OpenSettings(); });
            m.Show(at, new Point(0, at.Height));
        }
        void ResetTab()
        {
            if (Settings.CapTab == "video") { Settings.VidSel = "region"; Settings.VidFormat = "mp4"; Settings.VidOpenIn = "editor"; Settings.VidCursor = true; Settings.MicOn = true; }
            else { Settings.ImgSel = "region"; Settings.ImgEffect = "none"; Settings.ImgShare = "none"; Settings.PreviewInEditor = true; Settings.CopyToClip = false; Settings.ShowCursor = true; Settings.DelayOn = false; }
            Settings.Save(); BuildCard();
        }

        // ---------------------------------------------------------------- presets
        public void TogglePresets()
        {
            presetsOpen = !presetsOpen; presetsPanel.Visible = presetsOpen;
            MaximumSize = Size.Empty; MinimumSize = new Size(minW, 0);
            ClientSize = new Size(ClientSize.Width, Dpi.S(presetsOpen ? BaseH + PresetH : BaseH));
            LockHeight();
        }
        static readonly string[] PresetKeys = { "tab", "imgsel", "imgfx", "imgshare", "preview", "clip", "cursor", "delayon", "delay", "vidsel", "vidfmt", "vidopen", "vidcursor", "mic" };
        static Dictionary<string, string> Snapshot()
        {
            return new Dictionary<string, string> {
                { "tab", Settings.CapTab }, { "imgsel", Settings.ImgSel }, { "imgfx", Settings.ImgEffect }, { "imgshare", Settings.ImgShare },
                { "preview", Settings.PreviewInEditor ? "1" : "0" }, { "clip", Settings.CopyToClip ? "1" : "0" }, { "cursor", Settings.ShowCursor ? "1" : "0" },
                { "delayon", Settings.DelayOn ? "1" : "0" }, { "delay", Settings.DelaySec.ToString() }, { "vidsel", Settings.VidSel }, { "vidfmt", Settings.VidFormat },
                { "vidopen", Settings.VidOpenIn }, { "vidcursor", Settings.VidCursor ? "1" : "0" }, { "mic", Settings.MicOn ? "1" : "0" } };
        }
        void ApplyPreset(Profile p)
        {
            Func<string, string, string> g = delegate(string k, string d) { string v; return p.Data.TryGetValue(k, out v) ? v : d; };
            Settings.ImgSel = g("imgsel", "region"); Settings.ImgEffect = g("imgfx", "none"); Settings.ImgShare = g("imgshare", "none");
            Settings.PreviewInEditor = g("preview", "1") == "1"; Settings.CopyToClip = g("clip", "0") == "1"; Settings.ShowCursor = g("cursor", "1") == "1";
            Settings.DelayOn = g("delayon", "0") == "1"; int dl; if (int.TryParse(g("delay", "0"), out dl)) Settings.DelaySec = dl;
            Settings.VidSel = g("vidsel", "region"); Settings.VidFormat = g("vidfmt", "mp4"); Settings.VidOpenIn = g("vidopen", "editor");
            Settings.VidCursor = g("vidcursor", "1") == "1"; Settings.MicOn = g("mic", "1") == "1";
            Settings.Save(); SetTab(g("tab", "image"));
        }
        string Summary(Profile p)
        {
            string v; p.Data.TryGetValue("tab", out v);
            if (v == "video") return "Video, " + (p.Data["vidsel"]) + ", " + (p.Data["vidfmt"] == "gif" ? "GIF" : "MP4");
            if (v == "aio") return "All-in-One";
            var parts = new List<string> { "Image", Modes.ByKey(p.Data["imgsel"]).Name };
            if (p.Data["imgfx"] != "none") parts.Add(p.Data["imgfx"]); if (p.Data["delayon"] == "1") parts.Add(p.Data["delay"] + "s delay");
            return string.Join(", ", parts);
        }
        void SavePreset()
        {
            var n = Dialogs.Prompt(this, "Name this preset", Settings.CapTab == "video" ? "Video" : Modes.ByKey(Settings.ImgSel).Name);
            if (string.IsNullOrWhiteSpace(n)) return; n = n.Trim().Replace("\t", " ").Replace("=", "-");
            Profiles.RemoveAll(p => p.Name == n); Profiles.Add(new Profile { Name = n, Data = Snapshot() });
            SaveProfiles(); RefreshPresets(); if (!presetsOpen) TogglePresets();
        }
        // ---- presets list: capture types and saved presets, each with an editable hotkey
        const string PresetPrefix = "preset:";
        static string HotkeyText(string key) { string v; return Settings.Hotkeys.TryGetValue(key, out v) ? v : ""; }
        void RefreshPresets()
        {
            var rows = new List<PresetRow> { new PresetRow { Header = true, Name = "Capture types" } };
            foreach (var m in Modes.All) rows.Add(new PresetRow { Key = m.Key, Name = m.Key == "capture" ? "Capture button" : m.Name, Sub = m.Key == "capture" ? "Whatever the capture window is set to" : m.Hint, Icon = m.Icon });
            rows.Add(new PresetRow { Header = true, Name = Profiles.Count == 0 ? "My presets \u2014 press + to save the current settings" : "My presets" });
            foreach (var p in Profiles) rows.Add(new PresetRow { Key = PresetPrefix + p.Name, Name = p.Name, Sub = Summary(p), P = p, Icon = p.Data.ContainsKey("tab") && p.Data["tab"] == "video" ? Ic.Video : p.Data.ContainsKey("tab") && p.Data["tab"] == "session" ? Ic.Session : Ic.Camera });
            presetList.SetRows(rows);
        }
        // run what a hotkey or a double-click on a row stands for
        void RunKey(string k)
        {
            if (k.StartsWith(PresetPrefix)) { var p = Profiles.FirstOrDefault(x => PresetPrefix + x.Name == k); if (p != null) { ApplyPreset(p); CaptureCurrent(); } }
            else if (k == "capture") CaptureCurrent();
            else Begin(Modes.ByKey(k));
        }
        void RunRow(PresetRow r) { RunKey(r.Key); }
        string NameOfKey(string key)
        {
            if (key.StartsWith(PresetPrefix)) return "the preset \u201C" + key.Substring(PresetPrefix.Length) + "\u201D";
            var m = Modes.All.FirstOrDefault(x => x.Key == key); return m == null ? key : (key == "capture" ? "the Capture button" : m.Name);
        }
        void EditHotkey(PresetRow r)
        {
            string own = r.Key;
            var res = HotkeyDialog.Ask(this, r.Name, HotkeyText(own), delegate(string combo)
            {
                foreach (var kv in Settings.Hotkeys) if (kv.Key != own && string.Equals(kv.Value, combo, StringComparison.OrdinalIgnoreCase)) return NameOfKey(kv.Key);
                return null;
            });
            if (res == null) return;
            Settings.Hotkeys[own] = res; Settings.Save(); RegisterHotkeys(); UpdateCaptureButton(); presetList.Invalidate();
        }
        void PresetMenu(PresetRow r)
        {
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f) };
            m.Items.Add(r.P != null ? "Capture with this preset" : "Capture now", null, delegate { RunRow(r); });
            m.Items.Add(HotkeyText(r.Key).Length == 0 ? "Add hotkey..." : "Change hotkey...", null, delegate { EditHotkey(r); });
            if (HotkeyText(r.Key).Length > 0) m.Items.Add("Remove hotkey", null, delegate { Settings.Hotkeys[r.Key] = ""; Settings.Save(); RegisterHotkeys(); UpdateCaptureButton(); presetList.Invalidate(); });
            string def; if (r.P == null && Settings.DefaultHotkeys.TryGetValue(r.Key, out def) && !string.Equals(HotkeyText(r.Key), def, StringComparison.OrdinalIgnoreCase)
                && !Settings.Hotkeys.Any(kv => kv.Key != r.Key && string.Equals(kv.Value, def, StringComparison.OrdinalIgnoreCase)))
                m.Items.Add("Reset to " + def, null, delegate { Settings.Hotkeys[r.Key] = def; Settings.Save(); RegisterHotkeys(); UpdateCaptureButton(); presetList.Invalidate(); });
            if (r.P != null)
            {
                var p = r.P; m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Update with current settings", null, delegate { p.Data = Snapshot(); SaveProfiles(); RefreshPresets(); });
                m.Items.Add("Rename...", null, delegate
                {
                    var n = Dialogs.Prompt(this, "Rename preset", p.Name); if (string.IsNullOrWhiteSpace(n)) return; n = n.Trim().Replace("\t", " ").Replace("=", "-");
                    string oldKey = PresetPrefix + p.Name, newKey = PresetPrefix + n, hk; if (oldKey == newKey) return;
                    if (Settings.Hotkeys.TryGetValue(oldKey, out hk)) { Settings.Hotkeys.Remove(oldKey); Settings.Hotkeys[newKey] = hk; Settings.Save(); }
                    p.Name = n; SaveProfiles(); RefreshPresets(); RegisterHotkeys();
                });
                m.Items.Add("Delete", null, delegate { Settings.Hotkeys.Remove(PresetPrefix + p.Name); Settings.Save(); Profiles.Remove(p); SaveProfiles(); RefreshPresets(); RegisterHotkeys(); });
            }
            m.Show(Cursor.Position);
        }
        static string ProfPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapLite", "presets.txt"); } }
        public static void LoadProfiles()
        {
            try
            {
                if (!File.Exists(ProfPath)) return;
                foreach (var l in File.ReadAllLines(ProfPath))
                {
                    var parts = l.Split('\t'); if (parts.Length < 2) continue;
                    var p = new Profile { Name = parts[0] };
                    foreach (var kv in parts.Skip(1)) { int i = kv.IndexOf('='); if (i > 0) p.Data[kv.Substring(0, i)] = kv.Substring(i + 1); }
                    foreach (var k in PresetKeys) if (!p.Data.ContainsKey(k)) p.Data[k] = Snapshot()[k];
                    Profiles.Add(p);
                }
            }
            catch { }
        }
        static void SaveProfiles()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ProfPath)); File.WriteAllLines(ProfPath, Profiles.Select(p => p.Name + "\t" + string.Join("\t", p.Data.Select(kv => kv.Key + "=" + kv.Value)))); } catch { }
        }

        // ---------------------------------------------------------------- misc
        public void Quit() { quitting = true; foreach (var f in EditorForm.Open.ToList()) { if (!f.ConfirmClose()) { quitting = false; return; } } Close(); }
        public void ShowHub() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }
        public void HideForCapture() { if (Visible) Opacity = 0; }
        public void ShowAfterCapture() { Opacity = 1; }
        public void RefreshRecent() { if (EditorForm.Main != null && !EditorForm.Main.IsDisposed) EditorForm.Main.RefreshTray(); }
        void OpenEditor() { EditorForm.ShowEditor(); }
        void OpenSettings() { new SettingsForm().ShowDialog(this); RegisterHotkeys(); SetTab(Settings.CapTab); }
        void OpenPicture()
        {
            using (var d = new OpenFileDialog { InitialDirectory = Settings.Folder, Filter = "Pictures and projects|*.snapl;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" })
                if (d.ShowDialog(this) == DialogResult.OK) { try { EditorForm.Show(EditorForm.LoadAny(d.FileName)); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }
        }
        void ShowShortcuts()
        {
            var lines = Modes.All.Where(m => Settings.Hotkeys.ContainsKey(m.Key) && Settings.Hotkeys[m.Key].Length > 0).Select(m => Settings.Hotkeys[m.Key].PadRight(18) + m.Name);
            MessageBox.Show(this, "Global hotkeys\n\n" + string.Join("\n", lines) + "\n\nIn the editor: V select, A arrow, T text, C callout, R shape, G stamp, N step, F fill, K crop, Ctrl+Z undo.", "Keyboard shortcuts");
        }
        static Action toastClick;
        public static void Toast(string title, string text) { Toast(title, text, null); }
        public static void Toast(string title, string text, Action onClick)
        {
            if (Instance == null) return;
            toastClick = onClick;
            try { Instance.tray.ShowBalloonTip(3500, title, string.IsNullOrEmpty(text) ? " " : text, ToolTipIcon.None); } catch { }
        }

        // ---------------------------------------------------------------- hotkeys
        public static bool ParseKeys(string s, out uint mod, out uint vk)
        {
            mod = 0; vk = 0; if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var part in s.Split('+').Select(p => p.Trim()))
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mod |= Native.MOD_CONTROL; else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mod |= Native.MOD_SHIFT;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mod |= Native.MOD_ALT; else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) mod |= Native.MOD_WIN;
                else { Keys k; string name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part; if (Enum.TryParse(name, true, out k)) vk = (uint)k; else return false; }
            }
            return vk != 0;
        }
        void UnregisterAll() { foreach (var id in hotIds.Keys) Native.UnregisterHotKey(hk.Handle, id); hotIds.Clear(); }
        public void RegisterHotkeys()
        {
            UnregisterAll(); var failed = new List<string>(); int id = 1;
            foreach (var m in Modes.All)
            {
                string s; if (!Settings.Hotkeys.TryGetValue(m.Key, out s) || string.IsNullOrWhiteSpace(s)) continue;
                uint mod, vk; if (!ParseKeys(s, out mod, out vk)) { failed.Add(s); continue; }
                if (Native.RegisterHotKey(hk.Handle, id, mod | Native.MOD_NOREPEAT, vk)) hotIds[id] = m.Key; else failed.Add(s);
                id++;
            }
            foreach (var p in Profiles)
            {
                string key = PresetPrefix + p.Name, s; if (!Settings.Hotkeys.TryGetValue(key, out s) || string.IsNullOrWhiteSpace(s)) continue;
                uint mod, vk; if (!ParseKeys(s, out mod, out vk)) { failed.Add(s); continue; }
                if (Native.RegisterHotKey(hk.Handle, id, mod | Native.MOD_NOREPEAT, vk)) hotIds[id] = key; else failed.Add(s);
                id++;
            }
            if (failed.Count > 0) Toast("Some hotkeys are taken", string.Join(", ", failed) + " is used by another program. Change it in Preferences.");
        }

        // ---------------------------------------------------------------- capture
        // capture with whatever the capture window is set to
        public void CaptureCurrent()
        {
            if (SessionRecorder.Active) { ToggleSession(); return; }
            if (Recorder.Active) { Recorder.Stop(); return; }
            if (Settings.CapTab == "session") { ToggleSession(); return; }
            if (busy) return;
            if (Settings.CapTab == "video")
            {
                bool gif = Settings.VidFormat == "gif";
                Delayed(delegate
                {
                    if (Settings.VidSel == "full") { var scr = Screen.FromPoint(Cursor.Position).Bounds; Recorder.Start(scr, gif); }
                    else Capturer.RecordPicked(Settings.VidSel == "window" ? CapMode.Window : CapMode.Region, gif);
                });
                return;
            }
            Begin(Settings.CapTab == "aio" ? Modes.All[0] : Modes.ByKey(Settings.ImgSel));
        }
        // which activity of a Process Manager export the next session follows (or none)
        void PickProcedure()
        {
            using (var f = new ProcedurePickForm(Settings.SesProcFile, Settings.SesProcCode))
            {
                var r = f.ShowDialog(this);
                if (r == DialogResult.OK && f.Chosen != null) { Settings.SesProcFile = f.FilePath; Settings.SesProcCode = f.Chosen.Code; Settings.SesProcTitle = f.Chosen.Title; }
                else if (r == DialogResult.Ignore) { Settings.SesProcFile = ""; Settings.SesProcCode = ""; Settings.SesProcTitle = ""; }
                else return;
                Settings.Save(); BuildCard();
            }
        }
        ProcActivity PlannedActivity()
        {
            if (Settings.SesProcFile.Length == 0) return null;
            try { return ProcedureFile.Load(Settings.SesProcFile).FirstOrDefault(a => a.Code == Settings.SesProcCode && a.Title == Settings.SesProcTitle) ?? ProcedureFile.Load(Settings.SesProcFile).FirstOrDefault(a => a.Code == Settings.SesProcCode); }
            catch (Exception ex) { Hub.Toast("The procedure could not be used", ex.Message + " Recording without it."); return null; }
        }
        // start or stop a session recording with the Sessions tab's settings
        public void ToggleSession()
        {
            if (SessionRecorder.Active) { SessionRecorder.Stop(); ShowAfterSession(); return; }
            if (busy || Recorder.Active) return;
            Delayed(delegate
            {
                string t = "all"; Rectangle area = Rectangle.Empty; IntPtr win = IntPtr.Zero;
                if (Settings.SesTarget != "all")
                {
                    // point at a window (a browser: its page only) and click it, or drag an area, or press F for the monitor; only clicks inside it are recorded
                    var pick = Capturer.PickSession(); if (pick == null) return;
                    area = pick.Rect; t = "area";
                    foreach (var sc in Screen.AllScreens) if (sc.Bounds == area) t = "monitor";
                    if (t == "area" && !pick.Dragged) win = Native.Root(Native.RootAt(new Point(area.X + area.Width / 2, area.Y + area.Height / 2)));
                }
                wasVisible = Visible; Hide();
                if (win != IntPtr.Zero) Native.SetForegroundWindow(win);
                SessionRecorder.Activity = PlannedActivity();
                SessionRecorder.Start(t, area, win);
                UpdateCaptureButton();
                if (!SessionRecorder.Active) ShowAfterSession();
            });
        }
        // Change the window (or screen, or area) being recorded, in the middle of a task: the same pick screen as at the start. Clicks made while choosing are not recorded.
        public void SwitchTarget()
        {
            if (!SessionRecorder.Active || busy) return;
            busy = true; SessionRecorder.BeginSwitch();
            BeginInvoke((Action)delegate
            {
                try
                {
                    Native.WithDpi(true, delegate
                    {
                        string t; Rectangle area; IntPtr win; if (!PickTarget(out t, out area, out win)) return;
                        SessionRecorder.ApplyTarget(t, area, win);
                        if (win != IntPtr.Zero) Native.SetForegroundWindow(win);
                        Hub.Toast("Recording " + SessionRecorder.TargetName, "Your next click in it is the next step.");
                    });
                }
                finally { SessionRecorder.EndSwitch(); busy = false; }
            });
        }
        // the pick screen's answer as a recording target: "area" (a window, a pane of it, or a dragged area) or "monitor"
        bool PickTarget(out string t, out Rectangle area, out IntPtr win)
        {
            t = "all"; area = Rectangle.Empty; win = IntPtr.Zero;
            var pick = Capturer.PickSession(); if (pick == null) return false;
            area = pick.Rect; t = "area";
            foreach (var sc in Screen.AllScreens) if (sc.Bounds == area) t = "monitor";
            if (t == "area" && !pick.Dragged) win = Native.Root(Native.RootAt(new Point(area.X + area.Width / 2, area.Y + area.Height / 2)));
            return true;
        }
        // Carry on recording a saved session: choose where, then the new steps follow the old ones. The editor steps aside while recording.
        public void ContinueSession(string path)
        {
            if (SessionRecorder.Active || busy || Recorder.Active) return;
            SessionDoc d; try { d = SessionDoc.Load(path); } catch (Exception ex) { Hub.Toast("The session could not be opened", ex.Message); return; }
            EditorForm.MinimiseAll();
            Delayed(delegate
            {
                System.Threading.Thread.Sleep(350);      // the editor is out of the way before the screen is looked at
                string t = "all"; Rectangle area = Rectangle.Empty; IntPtr win = IntPtr.Zero;
                if (Settings.SesTarget != "all") { if (!PickTarget(out t, out area, out win)) { EditorForm.ShowSession(path); return; } }
                ProcActivity act = null;
                try
                {
                    var pr = d.Procedure; var src = pr != null && pr.ContainsKey("source") ? Convert.ToString(pr["source"]) : "";
                    var ai = pr != null && pr.ContainsKey("activity") ? pr["activity"] as Dictionary<string, object> : null;
                    if (ai != null && src.Length > 0 && System.IO.File.Exists(src)) { string code = Convert.ToString(ai["code"]), title = Convert.ToString(ai["title"]); var all = ProcedureFile.Load(src); act = all.FirstOrDefault(a => a.Code == code && a.Title == title) ?? all.FirstOrDefault(a => a.Code == code); }
                }
                catch { act = null; }
                if (act == null && d.Procedure != null) Hub.Toast("Continuing without the checklist", "The procedure file this session followed was not found.");
                wasVisible = Visible; Hide();
                if (win != IntPtr.Zero) Native.SetForegroundWindow(win);
                SessionRecorder.Resume = d; SessionRecorder.Activity = act;
                SessionRecorder.Start(t, area, win);
                UpdateCaptureButton();
                if (!SessionRecorder.Active) { ShowAfterSession(); EditorForm.ShowSession(path); }
            });
        }
        bool wasVisible;
        void ShowAfterSession() { UpdateCaptureButton(); RestoreAfterSession(); }
        // The capture window steps aside while a session is recorded (it would be in every screenshot and in the way of the clicks).
        // When the recording ends it comes back, whatever "Open in" says, if it was open when the recording began; a window that was hidden stays hidden.
        public void RestoreAfterSession() { if (!wasVisible) return; wasVisible = false; ShowHub(); }

        public void Begin(ModeDef m)
        {
            if (m.Mode == CapMode.Session) { ToggleSession(); return; }
            if (SessionRecorder.Active) return;
            if (Recorder.Active) { Recorder.Stop(); return; }
            if (busy) return;
            Delayed(delegate { Run(m); });
        }
        void Delayed(Action a)
        {
            busy = true;
            Action go = delegate { BeginInvoke((Action)delegate { try { Native.WithDpi(true, a); } finally { busy = false; } }); };      // the capture window is system-DPI aware; capturing needs real pixels
            int d = Settings.DelayOn ? Math.Max(1, Settings.DelaySec) : 0;
            if (d <= 0) { go(); return; }
            var cd = new CountdownForm(d, go); cd.Show();
        }
        void Run(ModeDef m)
        {
            try { Capturer.Deliver(Capturer.Run(m.Mode, m.Repeat)); }
            catch (Exception ex) { MessageBox.Show("Capture failed: " + ex.Message, "XLCapture"); }
        }
    }

    class SettingsForm : DpiForm
    {
        readonly Dictionary<string, TextBox> keys = new Dictionary<string, TextBox>();
        public SettingsForm()
        {
            DesignPixels = true;
            Text = "Preferences"; Icon = AppIcon.Get(); BackColor = Theme.Sheet; ForeColor = Theme.Ink; Font = Theme.Body(9f);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(640, 600);

            int y = 18;
            Controls.Add(new Label { Text = "Captures folder", Location = new Point(18, y + 4), AutoSize = true, ForeColor = Theme.Slate });
            var folder = new TextBox { Text = Settings.Folder, Location = new Point(150, y), Width = 360, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
            var browse = new FlatButton("Browse...", false) { Location = new Point(518, y - 3), Width = 100, Height = 28 };
            browse.Click += delegate { using (var d = new FolderBrowserDialog { SelectedPath = folder.Text }) if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath; };
            y += 44;
            Controls.Add(new Label { Text = "Fixed size capture", Location = new Point(18, y + 4), AutoSize = true, ForeColor = Theme.Slate });
            var fw = new NumericUpDown { Minimum = 16, Maximum = 16000, Value = Settings.FixedW, Location = new Point(150, y), Width = 80 };
            var fh = new NumericUpDown { Minimum = 16, Maximum = 16000, Value = Settings.FixedH, Location = new Point(250, y), Width = 80 };
            Controls.Add(new Label { Text = "x", Location = new Point(236, y + 4), AutoSize = true }); Controls.Add(new Label { Text = "pixels (wide x high)", Location = new Point(338, y + 4), AutoSize = true, ForeColor = Theme.Slate });
            y += 44;
            Controls.Add(new Label { Text = "Hotkeys", Location = new Point(18, y), AutoSize = true, Font = Theme.BodyBold(10f) });
            Controls.Add(new Label { Text = "Click a box and press the new combination. Delete clears it.", Location = new Point(110, y + 2), AutoSize = true, ForeColor = Theme.Slate });
            y += 30; int y0 = y;
            for (int i = 0; i < Modes.All.Length; i++)
            {
                var m = Modes.All[i];
                int x = 18 + (i % 2) * 308, yy = y0 + (i / 2) * 34;
                Controls.Add(new Label { Text = m.Name, Location = new Point(x, yy + 4), Size = new Size(130, 20), ForeColor = Theme.Slate });
                var tb = new TextBox { Location = new Point(x + 130, yy), Width = 160, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Text = Settings.Hotkeys.ContainsKey(m.Key) ? Settings.Hotkeys[m.Key] : "", AccessibleName = m.Name + " hotkey" };
                tb.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    e.SuppressKeyPress = true; e.Handled = true;
                    if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) { tb.Text = ""; return; }
                    if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;
                    var parts = new List<string>(); if (e.Control) parts.Add("Ctrl"); if (e.Shift) parts.Add("Shift"); if (e.Alt) parts.Add("Alt");
                    if (parts.Count == 0 && !(e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F24) && e.KeyCode != Keys.PrintScreen) { tb.Text = "Use Ctrl, Shift or Alt"; return; }
                    string kn = e.KeyCode.ToString(); if (kn.Length == 2 && kn[0] == 'D' && char.IsDigit(kn[1])) kn = kn.Substring(1);
                    parts.Add(kn); tb.Text = string.Join("+", parts);
                };
                keys[m.Key] = tb; Controls.Add(tb);
            }
            y = y0 + ((Modes.All.Length + 1) / 2) * 34 + 14;
            var start = new ThemedCheck { Text = "Start XLCapture when I sign in to Windows", Location = new Point(18, y), Width = 420, Height = 26, Checked = IsAutostart() }; Controls.Add(start);
            Controls.Add(new Label { Text = "Off by default. Adds one entry to your user startup list.", Location = new Point(44, y + 24), AutoSize = true, ForeColor = Theme.Slate });
            var askDel = new ThemedCheck { Text = "Ask before deleting a capture", Location = new Point(18, y + 54), Width = 420, Height = 26, Checked = Settings.AskDelete }; Controls.Add(askDel);
            var ok = new FlatButton("Save", true) { Location = new Point(428, 550), Size = new Size(90, 34), DialogResult = DialogResult.OK };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(526, 550), Size = new Size(90, 34), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { folder, browse, fw, fh, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
            FormClosing += delegate
            {
                if (DialogResult != DialogResult.OK) return;
                Settings.Folder = folder.Text; Settings.FixedW = (int)fw.Value; Settings.FixedH = (int)fh.Value;
                foreach (var kv in keys) { uint mo, vk; if (kv.Value.Text.Length == 0 || Hub.ParseKeys(kv.Value.Text, out mo, out vk)) Settings.Hotkeys[kv.Key] = kv.Value.Text; }
                Settings.AskDelete = askDel.Checked; Settings.Save(); SetAutostart(start.Checked);
            };
        }
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static bool IsAutostart() { try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && (k.GetValue("XLCapture") != null || k.GetValue("SnapLite") != null); } catch { return false; } }
        static void SetAutostart(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                { k.DeleteValue("SnapLite", false); if (on) k.SetValue("XLCapture", "\"" + Application.ExecutablePath + "\" --tray"); else k.DeleteValue("XLCapture", false); }
            }
            catch { }
        }
    }
}
