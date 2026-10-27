// XLCapture Editor (Snagit-style): menu bar, ribbon of modes and tools, Quick Styles + Tool Properties panel,
// bottom bar and recent-captures tray. One window; captures switch inside it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    class Scroller : Panel
    {
        public Scroller() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }

    partial class EditorForm : DpiForm
    {
        public static readonly List<EditorForm> Open = new List<EditorForm>();
        public static EditorForm Main;
        public static Action NewCaptureRequested;
        public static readonly List<Tool> Favorites = new List<Tool> { Tool.Arrow, Tool.Text, Tool.Callout, Tool.Step, Tool.Blur };

        class Session { public Doc Doc; public DocCanvas Canvas; public bool Fitted; }
        readonly List<Session> sessions = new List<Session>();
        Session cur;
        public Doc Doc { get { return cur == null ? null : cur.Doc; } }
        DocCanvas canvas { get { return cur == null ? null : cur.Canvas; } }
        public DocCanvas Canvas { get { return canvas; } }
        readonly ToolStyle style = new ToolStyle();
        Tool activeTool = Tool.Arrow;

        // chrome
        readonly MenuStrip menu; readonly SoftToolStrip ribbon; readonly ToolStripLabel spacer;
        ToolStripButton modeEditor, modeLibrary; ToolStripDropDownButton moreBtn, favBtn, shareBtn;
        readonly Dictionary<Tool, ToolStripButton> toolBtns = new Dictionary<Tool, ToolStripButton>();
        readonly Panel center, empty; readonly Scroller scroller; LibraryView library;
        readonly Panel right; readonly FlowLayoutPanel rightFlow;
        readonly Panel trayPanel, trayBar; internal readonly RecentTray tray; readonly SlimScrollBar trayScroll; readonly Label status;
        readonly ComboBox zoomBox; readonly Button sizeBtn, hideRecent, tagBtn, effectsBtn, propsBtn;
        readonly ToolTip tips = new ToolTip();
        bool libraryMode, sessionMode, showEffects, building, rightOn = true, trayOn = true;
        SessionView sessionView;
        string trayFilter = "all";                                   // all | images | videos | sessions
        readonly List<Button> chips = new List<Button>();

        static readonly Tool[] ribbonTools = { Tool.Arrow, Tool.Text, Tool.Callout, Tool.Shape, Tool.Stamp, Tool.Fill, Tool.Select, Tool.SelRect };
        static readonly Tool[] moreTools = { Tool.Line, Tool.Step, Tool.Pen, Tool.Marker, Tool.Highlighter, Tool.Magnify, Tool.Spotlight, Tool.Blur, Tool.Crop, Tool.Cut, Tool.SmartMove, Tool.SelEllipse, Tool.Lasso, Tool.Wand, Tool.Eyedropper };

        public static Ic IconOf(Tool t)
        {
            switch (t)
            {
                case Tool.Select: return Ic.Move; case Tool.Arrow: return Ic.Arrow; case Tool.Line: return Ic.Line; case Tool.Shape: return Ic.Shape; case Tool.Callout: return Ic.Callout;
                case Tool.Text: return Ic.Text; case Tool.Stamp: return Ic.Stamp; case Tool.Step: return Ic.Step; case Tool.Pen: return Ic.Pen; case Tool.Marker: return Ic.Marker;
                case Tool.Highlighter: return Ic.Eraser; case Tool.Fill: return Ic.Fill; case Tool.Magnify: return Ic.Magnify; case Tool.Spotlight: return Ic.Spotlight; case Tool.Blur: return Ic.Blur;
                case Tool.Crop: return Ic.Crop; case Tool.Cut: return Ic.Cut; case Tool.SmartMove: return Ic.Wand; case Tool.SelRect: return Ic.SelectRect; case Tool.SelEllipse: return Ic.Lasso;
                case Tool.Lasso: return Ic.Draw; case Tool.Wand: return Ic.Wand; case Tool.Eyedropper: return Ic.Dropper;
            }
            return Ic.Select;
        }
        public static string NameOf(Tool t)
        {
            switch (t)
            {
                case Tool.Select: return "Move"; case Tool.SelRect: return "Selection"; case Tool.SelEllipse: return "Ellipse select"; case Tool.Lasso: return "Freehand select";
                case Tool.Wand: return "Magic wand"; case Tool.SmartMove: return "Smart Move"; case Tool.Cut: return "Cut Out"; case Tool.Highlighter: return "Highlighter";
            }
            return t.ToString();
        }
        static readonly Dictionary<Keys, Tool> hotkeys = new Dictionary<Keys, Tool>
        {
            { Keys.V, Tool.Select }, { Keys.A, Tool.Arrow }, { Keys.L, Tool.Line }, { Keys.R, Tool.Shape }, { Keys.C, Tool.Callout }, { Keys.T, Tool.Text },
            { Keys.G, Tool.Stamp }, { Keys.N, Tool.Step }, { Keys.P, Tool.Pen }, { Keys.M, Tool.Marker }, { Keys.H, Tool.Highlighter }, { Keys.F, Tool.Fill },
            { Keys.Z, Tool.Magnify }, { Keys.O, Tool.Spotlight }, { Keys.B, Tool.Blur }, { Keys.K, Tool.Crop }, { Keys.X, Tool.Cut }, { Keys.W, Tool.SmartMove },
            { Keys.E, Tool.SelRect }, { Keys.J, Tool.SelEllipse }, { Keys.Q, Tool.Lasso }, { Keys.Y, Tool.Wand }, { Keys.I, Tool.Eyedropper }
        };

        // ------------------------------------------------------------ opening
        public static void ShowEditor()
        {
            // made "system DPI aware": the same size on every monitor (Windows stretches it), instead of the startup monitor's pixels on all of them
            if (Main == null || Main.IsDisposed) { Native.WithDpi(false, delegate { Main = new EditorForm(); Main.Show(); }); Main.OpenNewest(); }
            else { Main.Show(); if (Main.WindowState == FormWindowState.Minimized) Main.WindowState = FormWindowState.Normal; }
            Main.Activate();
        }
        public static void ShowLibrary() { ShowEditor(); Main.SetMode(true); }

        public EditorForm()
        {
            Text = "XLCapture Editor"; Icon = AppIcon.Get(); KeyPreview = true; Font = Theme.Body(9f); BackColor = Theme.Paper; ForeColor = Theme.Ink;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(wa.Width - Dpi.S(40), Dpi.S(1500)), Math.Min(wa.Height - Dpi.S(40), Dpi.S(940)));
            StartPosition = FormStartPosition.CenterScreen; MinimumSize = Dpi.S(1000, 640);
            style.Raise(); style.Changed += delegate { StyleChanged(); };

            // ---- center: canvas / library / empty state
            center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Paper };
            scroller = new Scroller { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Paper, Visible = false };
            scroller.MouseEnter += delegate { if (canvas != null && !(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown) && !(ActiveControl is ComboBox)) canvas.Focus(); };
            scroller.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) == 0 || canvas == null) return;
                canvas.SetZoom(canvas.Zoom * (float)Math.Pow(1.12, e.Delta / 120.0)); ((HandledMouseEventArgs)e).Handled = true;
            };
            empty = BuildEmpty();
            center.Controls.Add(scroller); center.Controls.Add(empty);

            // ---- right panel
            right = new Panel { Dock = DockStyle.Right, Width = Dpi.S(344), BackColor = Theme.Sheet };
            right.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, 0, 0, right.Height); };
            rightFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Sheet, Padding = Dpi.P(10, 4, 6, 12) };
            right.Controls.Add(rightFlow);

            // ---- bottom bar + recent tray
            trayPanel = new Panel { Dock = DockStyle.Bottom, Height = Dpi.S(44 + 146 + 16), BackColor = Theme.Paper };
            trayBar = new Panel { Dock = DockStyle.Top, Height = Dpi.S(44), BackColor = Theme.Sheet };
            trayBar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) { e.Graphics.DrawLine(p, 0, 0, trayBar.Width, 0); e.Graphics.DrawLine(p, 0, trayBar.Height - 1, trayBar.Width, trayBar.Height - 1); } };
            hideRecent = BarButton(Ic.Recent, "Hide Recent", delegate { trayOn = !trayOn; ApplyTray(); });
            tagBtn = BarButton(Ic.Tag, "Tag", delegate { TagCurrent(); });
            status = new Label { AutoSize = false, ForeColor = Theme.Slate, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Body(9f), AutoEllipsis = true };
            zoomBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = Dpi.S(90), Font = Theme.Body(9.5f), FlatStyle = FlatStyle.System, AccessibleName = "Zoom" };
            zoomBox.Items.AddRange(new object[] { "Fit", "25%", "50%", "75%", "100%", "150%", "200%", "300%", "400%" });
            zoomBox.SelectionChangeCommitted += delegate { ApplyZoomText((string)zoomBox.SelectedItem); };
            zoomBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { ApplyZoomText(zoomBox.Text); e.SuppressKeyPress = true; } };
            sizeBtn = BarButton(Ic.Image, "", delegate { if (cur != null) ResizeDialog(); }); tips.SetToolTip(sizeBtn, "Image size. Click to resize.");
            effectsBtn = BarButton(Ic.Effects, "Effects", delegate { ShowPanel(true); });
            propsBtn = BarButton(Ic.Gear, "Properties", delegate { ShowPanel(false); });
            var zoomIcon = new Label { Image = Icons.Get(Ic.ZoomIn, Dpi.S(18), Theme.InkSoft), Size = Dpi.S(24, 24) };
            trayBar.Controls.AddRange(new Control[] { hideRecent, tagBtn, status, zoomIcon, zoomBox, sizeBtn, effectsBtn, propsBtn });
            // filter chips for the recent tray: All / Images / Videos / Sessions
            foreach (var kv in new[] { new KeyValuePair<string, string>("all", "All"), new KeyValuePair<string, string>("images", "Images"), new KeyValuePair<string, string>("videos", "Videos"), new KeyValuePair<string, string>("sessions", "Sessions") })
            {
                var key = kv.Key;
                var c = new Button { Text = kv.Value, Tag = key, FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = Dpi.S(30), MinimumSize = Dpi.S(30, 30), Padding = Dpi.P(8, 0, 8, 0), Cursor = Theme.Hand, Font = Theme.Body(9f), AccessibleName = "Show " + kv.Value.ToLower() + " in the recent tray", AccessibleRole = AccessibleRole.RadioButton };
                c.FlatAppearance.BorderColor = Theme.Rule; c.FlatAppearance.MouseOverBackColor = Theme.CobaltTint;
                c.Click += delegate { trayFilter = key; StyleChips(); RefreshTray(); };
                chips.Add(c); trayBar.Controls.Add(c);
            }
            StyleChips();
            this.zoomIcon = zoomIcon;
            trayBar.Resize += delegate { LayoutTrayBar(); };
            tray = new RecentTray { Dock = DockStyle.Fill };
            tray.Open += OpenTrayItem; tray.Context += TrayMenu; tray.Delete += DeleteTrayItem;
            trayScroll = new SlimScrollBar { Dock = DockStyle.Bottom, Height = Dpi.S(16) };
            trayScroll.ValueChanged += delegate { tray.ScrollValue = trayScroll.Value; };
            tray.Scrolled += delegate { trayScroll.Sync(tray.ScrollValue, tray.ScrollMax, tray.Width); };
            tray.Resize += delegate { tray.Resync(); };
            trayPanel.Controls.Add(tray); trayPanel.Controls.Add(trayScroll); trayPanel.Controls.Add(trayBar);

            // ---- ribbon and menu
            ribbon = BuildRibbon(out spacer);
            menu = BuildMenu();
            MainMenuStrip = menu;

            Controls.Add(center); Controls.Add(right); Controls.Add(trayPanel); Controls.Add(ribbon); Controls.Add(menu);
            SelectTool(Tool.Arrow);
            ShowPanel(false);
            UpdateStatus();
            Open.Add(this); Main = this;
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (!ConfirmClose()) e.Cancel = true; };
            FormClosed += delegate { Open.Remove(this); if (Main == this) Main = null; foreach (var x in sessions) x.Canvas.Dispose(); };
            Shown += delegate { RefreshTray(); };
        }

        Button BarButton(Ic icon, string text, EventHandler click)
        {
            var b = new Button { Text = text.Length > 0 ? " " + text : "", Image = Icons.Get(icon, Dpi.S(18), Theme.InkSoft), TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = Dpi.S(32), MinimumSize = Dpi.S(32, 32), Padding = Dpi.P(6, 0, 6, 0), Cursor = Theme.Hand, Font = Theme.Body(9.5f), BackColor = Theme.Sheet, ForeColor = Theme.Ink, AccessibleName = text.Length > 0 ? text : "Image size" };
            b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Theme.Rule; b.FlatAppearance.CheckedBackColor = Theme.CobaltTint;
            b.Click += click; return b;
        }

        Panel BuildEmpty()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Paper };
            var t = new Label { Text = "Nothing open yet", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Theme.LabelFamily, 16f, FontStyle.Bold), ForeColor = Theme.Ink, Size = Dpi.S(520, 40) };
            var s = new Label { Text = "Take a capture, or pick one from the recent tray below.", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Slate, Font = Theme.Body(10.5f), Size = Dpi.S(520, 28) };
            var cap = new FlatButton("New capture", true) { Size = Dpi.S(130, 36) }; cap.Click += delegate { if (NewCaptureRequested != null) NewCaptureRequested(); };
            var open = new FlatButton("Open a file...", false) { Size = Dpi.S(130, 36) }; open.Click += delegate { OpenFile(); };
            p.Controls.AddRange(new Control[] { t, s, cap, open });
            p.Resize += delegate
            {
                int cx = p.Width / 2, cy = p.Height / 2 - Dpi.S(40);
                t.Location = new Point(cx - t.Width / 2, cy); s.Location = new Point(cx - s.Width / 2, cy + Dpi.S(44));
                cap.Location = new Point(cx - cap.Width - Dpi.S(6), cy + Dpi.S(90)); open.Location = new Point(cx + Dpi.S(6), cy + Dpi.S(90));
            };
            return p;
        }

        // ------------------------------------------------------------ ribbon
        ToolStripButton RB(Ic icon, string text, string tip, EventHandler click)
        {
            var b = new ToolStripButton(text, Icons.Get(icon, Dpi.S(26), Theme.Ink)) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText, ToolTipText = tip, Padding = Dpi.P(6, 2, 6, 2), Margin = Dpi.P(1, 3, 1, 3), AccessibleName = text.Length > 0 ? text : tip };
            b.Click += click; return b;
        }
        ToolStripDropDownButton RDD(Ic icon, string text, string tip)
        {
            var d = new ToolStripDropDownButton(text, Icons.Get(icon, Dpi.S(26), Theme.Ink)) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText, ToolTipText = tip, Padding = Dpi.P(6, 2, 6, 2), Margin = Dpi.P(1, 3, 1, 3), ShowDropDownArrow = false, AccessibleName = text };
            d.DropDown.Renderer = new ThemeRenderer(); d.DropDown.Font = Theme.Body(9.5f); return d;
        }
        SoftToolStrip BuildRibbon(out ToolStripLabel spacerOut)
        {
            var r = new SoftToolStrip { Dock = DockStyle.Top, AutoSize = false, Height = Dpi.S(74), ImageScalingSize = Dpi.S(26, 26), Padding = Dpi.P(8, 2, 8, 2), Font = Theme.Body(9.5f), BackColor = Theme.Sheet };
            r.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, r.Height - 1, r.Width, r.Height - 1); };
            modeEditor = RB(Ic.EditBox, "Editor", "Edit the current capture", delegate { SetMode(false); });
            modeLibrary = RB(Ic.Grid, "Library", "Browse every capture (Ctrl+L)", delegate { SetMode(true); });
            var capture = RB(Ic.Record, "Capture", "Take a new capture with the capture window's settings (Ctrl+N)", delegate { if (NewCaptureRequested != null) NewCaptureRequested(); });
            var create = RDD(Ic.Create, "Create", "Make something new");
            create.DropDownItems.Add(MI("New blank image...", null, delegate { NewBlank(); }));
            create.DropDownItems.Add(MI("New image from clipboard", null, delegate { NewFromClipboard(); }));
            create.DropDownItems.Add(new ToolStripSeparator());
            create.DropDownItems.Add(MI("Combine pictures into one...", null, delegate { CombineFiles(); }));
            create.DropDownItems.Add(MI("Make a PDF from pictures...", null, delegate { PdfFromFiles(); }));
            r.Items.AddRange(new ToolStripItem[] { modeEditor, modeLibrary, capture, create, new ToolStripSeparator() });
            spacerOut = new ToolStripLabel("") { AutoSize = false, Width = Dpi.S(20) };
            r.Items.Add(spacerOut);

            favBtn = RDD(Ic.Star, "Favorites", "Your favourite tools");
            favBtn.DropDownOpening += delegate { BuildFavorites(); };
            BuildFavorites();
            r.Items.Add(favBtn);
            foreach (var t in ribbonTools)
            {
                var tool = t;
                var b = RB(IconOf(t), NameOf(t), NameOf(t) + "  (" + hotkeys.First(kv => kv.Value == t).Key + ")", delegate { SelectTool(tool); });
                b.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) FavMenu(tool, Cursor.Position); };
                toolBtns[t] = b; r.Items.Add(b);
            }
            moreBtn = RDD(Ic.Grid, "More", "More tools");
            foreach (var t in moreTools)
            {
                var tool = t; var key = hotkeys.First(kv => kv.Value == t).Key;
                var mi = new ToolStripMenuItem(NameOf(t), Icons.Get(IconOf(t), Dpi.S(18), Theme.Ink)) { ShortcutKeyDisplayString = key.ToString() };
                mi.Click += delegate { SelectTool(tool); };
                mi.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) FavMenu(tool, Cursor.Position); };
                moreBtn.DropDownItems.Add(mi);
            }
            moreBtn.DropDownItems.Add(new ToolStripSeparator());
            moreBtn.DropDownItems.Add(M("Grab text (OCR)", delegate { GrabText(); }));
            r.Items.Add(moreBtn);
            r.Items.Add(RB(Ic.Undo, "Undo", "Undo (Ctrl+Z)", delegate { if (cur != null) { Doc.Undo(); canvas.Sel.Clear(); OnSelectionChanged(); } }));
            r.Items.Add(RB(Ic.Redo, "Redo", "Redo (Ctrl+Y)", delegate { if (cur != null) { Doc.Redo(); canvas.Sel.Clear(); OnSelectionChanged(); } }));

            shareBtn = new ToolStripDropDownButton("Share", Icons.Get(Ic.Share, Dpi.S(20), Color.White)) { Alignment = ToolStripItemAlignment.Right, Tag = "primary", Padding = Dpi.P(12, 6, 12, 6), Margin = Dpi.P(4, 14, 4, 14), Font = Theme.BodyBold(10f), TextImageRelation = TextImageRelation.ImageBeforeText, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, AccessibleName = "Share" };
            shareBtn.DropDown.Renderer = new ThemeRenderer(); shareBtn.DropDown.Font = Theme.Body(9.5f);
            var src = BuildShareMenu(); shareBtn.DropDownItems.AddRange(src.DropDownItems.Cast<ToolStripItem>().ToArray());
            var copyAll = new ToolStripButton("Copy All", Icons.Get(Ic.Copy, Dpi.S(20), Theme.Ink)) { Alignment = ToolStripItemAlignment.Right, TextImageRelation = TextImageRelation.ImageBeforeText, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, Padding = Dpi.P(8, 6, 8, 6), Margin = Dpi.P(4, 14, 4, 14), Font = Theme.Body(10f), ToolTipText = "Copy the whole picture with its annotations (Ctrl+Shift+C)" };
            copyAll.Click += delegate { if (cur != null) CopyImage(); };
            r.Items.Add(shareBtn); r.Items.Add(copyAll);

            bool laying = false;
            r.Layout += delegate
            {
                if (laying) return; laying = true;
                try
                {
                    int left = 0, mid = 0, rt = 0; bool afterSpacer = false;
                    foreach (ToolStripItem it in r.Items)
                    {
                        if (it == spacer) { afterSpacer = true; continue; }
                        int w = it.Width + it.Margin.Horizontal;
                        if (it.Alignment == ToolStripItemAlignment.Right) rt += w; else if (afterSpacer) mid += w; else left += w;
                    }
                    int free = r.Width - r.Padding.Horizontal - left - rt - mid - Dpi.S(20);
                    int want = Math.Max(Dpi.S(8), free / 2 + (left > 0 ? 0 : 0) - 0);
                    // centre the tool group in the window, not just in the leftover space
                    int ideal = (r.Width - mid) / 2 - left - r.Padding.Left;
                    want = Math.Max(Dpi.S(8), Math.Min(free, ideal));
                    if (Math.Abs(spacer.Width - want) > 2) spacer.Width = want;
                }
                finally { laying = false; }
            };
            return r;
        }
        void BuildFavorites()
        {
            favBtn.DropDownItems.Clear();
            foreach (var t in Favorites)
            {
                var tool = t; var mi = new ToolStripMenuItem(NameOf(t), Icons.Get(IconOf(t), Dpi.S(18), Theme.Ink)); mi.Click += delegate { SelectTool(tool); };
                favBtn.DropDownItems.Add(mi);
            }
            favBtn.DropDownItems.Add(new ToolStripSeparator());
            var hint = new ToolStripMenuItem("Right-click any tool to add or remove it") { Enabled = false }; favBtn.DropDownItems.Add(hint);
        }
        void FavMenu(Tool t, Point screen)
        {
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f) };
            if (Favorites.Contains(t)) m.Items.Add("Remove " + NameOf(t) + " from Favorites", null, delegate { Favorites.Remove(t); });
            else m.Items.Add("Add " + NameOf(t) + " to Favorites", null, delegate { Favorites.Add(t); });
            m.Show(screen);
        }

        // ------------------------------------------------------------ menu bar
        ToolStripMenuItem DD(Ic icon, string text, string tip, params ToolStripItem[] items)
        {
            var d = new ToolStripMenuItem(text) { ToolTipText = tip };
            d.DropDown.Renderer = new ThemeRenderer(); d.DropDown.BackColor = Theme.Sheet; d.DropDown.Font = Theme.Body(9.5f);
            d.DropDownItems.AddRange(items); return d;
        }
        // menu items that need an open picture
        ToolStripMenuItem M(string text, EventHandler click) { return M(text, null, click); }
        ToolStripMenuItem M(string text, string keys, EventHandler click)
        {
            var m = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys };
            m.Click += delegate(object s, EventArgs e) { if (cur == null) { Info("Open or capture a picture first."); return; } click(s, e); };
            return m;
        }
        static ToolStripMenuItem MI(string text, string keys, EventHandler click) { var m = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys }; m.Click += click; return m; }

        // the app mark for the menu bar; Icon.ToBitmap fails on icons with PNG frames, so the built-in logo is used
        static Image MenuMark(int size)
        {
            try { var m = Brand.MarkTile(size); if (m != null) return m; } catch { }
            var b = new Bitmap(size, size); using (var g = Graphics.FromImage(b)) g.Clear(Theme.Charcoal); return b;
        }
        MenuStrip BuildMenu()
        {
            var ms = new MenuStrip { BackColor = Theme.Sheet, Font = Theme.Body(9.5f), Renderer = new ThemeRenderer(), Padding = Dpi.P(6, 2, 0, 2), ImageScalingSize = Dpi.S(20, 20) };
            ms.Items.Add(new ToolStripLabel(MenuMark(Dpi.S(22))) { AutoSize = false, Size = Dpi.S(22, 22), ImageScaling = ToolStripItemImageScaling.SizeToFit, Margin = Dpi.P(0, 0, 6, 0) });
            ms.Items.Add(DD(Ic.Open, "&File", "",
                MI("New capture", "Ctrl+N", delegate { if (NewCaptureRequested != null) NewCaptureRequested(); }),
                MI("New blank image...", null, delegate { NewBlank(); }), MI("New image from clipboard", null, delegate { NewFromClipboard(); }),
                MI("Open...", "Ctrl+O", delegate { OpenFile(); }), new ToolStripSeparator(),
                M("Save project", "Ctrl+S", delegate { SaveProject(false); }), M("Save project as...", "Ctrl+Shift+S", delegate { SaveProject(true); }),
                M("Export picture...", "Ctrl+E", delegate { ExportImage(); }), M("Print...", "Ctrl+P", delegate { PrintImage(); }), new ToolStripSeparator(),
                M("Close this picture", "Ctrl+W", delegate { CloseCurrent(); }), MI("Exit editor", null, delegate { Close(); })));
            var arrange = BuildArrangeMenu(); arrange.Text = "Arrange";
            ms.Items.Add(DD(Ic.Undo, "&Edit", "",
                M("Undo", "Ctrl+Z", delegate { Doc.Undo(); canvas.Sel.Clear(); OnSelectionChanged(); }), M("Redo", "Ctrl+Y", delegate { Doc.Redo(); canvas.Sel.Clear(); OnSelectionChanged(); }), new ToolStripSeparator(),
                M("Cut", "Ctrl+X", delegate { CutSel(); }), M("Copy", "Ctrl+C", delegate { CopySel(); }), M("Paste", "Ctrl+V", delegate { canvas.Paste(); }),
                M("Copy All", "Ctrl+Shift+C", delegate { CopyImage(); }), new ToolStripSeparator(), arrange));
            var img = BuildImageMenu(); img.Text = "&Image";
            img.DropDownItems.Add(new ToolStripSeparator());
            var filt = BuildFilterMenu(); filt.Text = "Filters"; img.DropDownItems.Add(filt);
            ms.Items.Add(img);
            ms.Items.Add(DD(Ic.Video, "&Video", "",
                MI("Open a recording...", null, delegate { OpenRecording(false); }), MI("Trim a video...", null, delegate { OpenRecording(true); })));
            var share = BuildShareMenu(); share.Text = "&Share"; ms.Items.Add(share);
            var showTray = new ToolStripMenuItem("Recent tray") { Checked = true, CheckOnClick = true }; showTray.CheckedChanged += delegate { trayOn = showTray.Checked; ApplyTray(); };
            var showRight = new ToolStripMenuItem("Side panel") { Checked = true, CheckOnClick = true }; showRight.CheckedChanged += delegate { rightOn = showRight.Checked; ApplyRight(); };
            ms.Items.Add(DD(Ic.ZoomIn, "&View", "",
                M("Zoom in", "Ctrl++", delegate { canvas.SetZoom(canvas.Zoom * 1.25f); }), M("Zoom out", "Ctrl+-", delegate { canvas.SetZoom(canvas.Zoom / 1.25f); }),
                M("Actual size", "Ctrl+1", delegate { canvas.SetZoom(1f); }), M("Fit in window", "Ctrl+0", delegate { Fit(); }), new ToolStripSeparator(),
                MI("Library", "Ctrl+L", delegate { SetMode(!libraryMode); }), showTray, showRight));
            ms.Items.Add(DD(Ic.Question, "&Help", "",
                MI("Keyboard shortcuts", null, delegate { MessageBox.Show(this, "Tools: V move, A arrow, T text, C callout, R shape, G stamp, N step, F fill, P pen, M marker, H highlighter, Z magnify, O spotlight, B blur, K crop, X cut out, W smart move, E/J/Q/Y selections, I eyedropper.\n\nCtrl+Z undo, Ctrl+Y redo, Ctrl+C copy, Ctrl+Shift+C copy all, Ctrl+V paste, Ctrl+D duplicate, Del delete, arrows nudge, Shift constrains, Space+drag pans, Ctrl+wheel zooms.", "Shortcuts"); })));
            return ms;
        }

        // ------------------------------------------------------------ modes, panels, tray
        void SetMode(bool lib)
        {
            if (sessionMode) { if (!LeaveSession()) return; sessionMode = false; if (sessionView != null) sessionView.Visible = false; }
            libraryMode = lib;
            modeEditor.Checked = !lib; modeLibrary.Checked = lib;
            if (lib && library == null)
            {
                library = new LibraryView { Dock = DockStyle.Fill, Visible = false };
                library.OpenRequested += delegate { SetMode(false); };
                center.Controls.Add(library);
            }
            if (library != null) { library.Visible = lib; if (lib) { library.BringToFront(); library.RefreshList(); } }
            scroller.Visible = !lib && cur != null; empty.Visible = !lib && cur == null;
            foreach (ToolStripItem it in ribbon.Items) if (it != modeEditor && it != modeLibrary && !(it.Text == "Capture") && !(it.Text == "Create") && !(it is ToolStripSeparator) && it != spacer) it.Enabled = !lib;
            ApplyRight(); ApplyTray();
            Text = lib ? "XLCapture Editor - Library" : TitleText();
        }
        void ApplyRight() { right.Visible = rightOn && !libraryMode && !sessionMode; }
        void StyleChips()
        {
            foreach (var c in chips)
            {
                bool on = (string)c.Tag == trayFilter;
                c.BackColor = on ? Theme.AccentSoft : Color.White; c.ForeColor = Theme.Ink;
                c.FlatAppearance.BorderColor = on ? Theme.AccentSoft : Theme.Rule; c.FlatAppearance.MouseOverBackColor = on ? Theme.AccentSoftHover : Theme.CobaltTint;
                c.AccessibleDescription = on ? "selected" : "";
            }
            LayoutTrayBar();
        }
        static string KindOf(TrayItem t)
        {
            if (t.Badge == "SESSION") return "sessions";
            if (t.Badge == "MP4" || t.Badge == "AVI" || t.Badge == "GIF") return "videos";
            return "images";
        }

        // ------------------------------------------------------------ sessions
        public static void ShowSession(string path) { ShowEditor(); Main.OpenSession(path); }
        // out of the way, for recording
        public static void MinimiseAll() { if (Main != null && !Main.IsDisposed && Main.Visible) Main.WindowState = FormWindowState.Minimized; }
        public void OpenSession(string path)
        {
            if (!LeaveSession()) return;
            SessionDoc d;
            try { d = SessionDoc.Load(path); } catch (Exception ex) { Info("Could not open " + Path.GetFileName(path) + ": " + ex.Message); return; }
            if (sessionView == null)
            {
                sessionView = new SessionView { Dock = DockStyle.Fill, Visible = false };
                sessionView.Message += Info;
                sessionView.OpenInEditor += delegate(Doc doc) { SetSessionMode(false); OpenDoc(doc); };
                sessionView.Saved += delegate { RefreshTray(); Text = sessionView.Doc.Name + " - XLCapture Editor"; UpdateSessionLabel(); };
                sessionView.NameEdited += delegate { UpdateSessionLabel(); };
                sessionView.ContinueRequested += delegate { var p = sessionView.Doc.Path; if (p != null && Hub.Instance != null) Hub.Instance.ContinueSession(p); };
                center.Controls.Add(sessionView);
            }
            sessionView.Load(d);
            SetSessionMode(true);
            tray.CurrentDoc = null; tray.CurrentPath = path; tray.Invalidate(); tray.ScrollToCurrent();
        }
        void SetSessionMode(bool on)
        {
            sessionMode = on;
            if (on && libraryMode) { libraryMode = false; if (library != null) library.Visible = false; }
            modeEditor.Checked = !libraryMode; modeLibrary.Checked = libraryMode;
            if (sessionView != null) { sessionView.Visible = on; if (on) sessionView.BringToFront(); }
            scroller.Visible = !on && !libraryMode && cur != null; empty.Visible = !on && !libraryMode && cur == null;
            foreach (ToolStripItem it in ribbon.Items) if (it != modeEditor && it != modeLibrary && !(it.Text == "Capture") && !(it.Text == "Create") && !(it is ToolStripSeparator) && it != spacer) it.Enabled = !on && !libraryMode;
            ApplyRight(); ApplyTray();
            Text = on ? sessionView.Doc.Name + " - XLCapture Editor" : TitleText();
            UpdateSessionLabel();
            if (!on) { tray.CurrentDoc = Doc; tray.CurrentPath = Doc == null ? null : (Doc.ProjectPath ?? Doc.Path); tray.Invalidate(); }
        }
        // ask before leaving a session with unsaved edits; false = stay
        bool LeaveSession()
        {
            if (sessionView == null || sessionView.Doc == null || !sessionView.Doc.Dirty) return true;
            var r = MessageBox.Show(this, "Save your changes to the session " + sessionView.Doc.Name + "?", "XLCapture Editor", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return sessionView.Save();
            sessionView.Doc.Dirty = false; return true;
        }
        void ApplyTray()
        {
            trayPanel.Visible = !libraryMode;
            tray.Visible = trayOn; trayScroll.Visible = trayOn; trayPanel.Height = trayOn ? Dpi.S(44 + 146 + 16) : Dpi.S(44);
            hideRecent.Text = trayOn ? " Hide Recent" : " Show Recent";
        }
        void ShowPanel(bool effects)
        {
            showEffects = effects; rightOn = true; ApplyRight();
            effectsBtn.BackColor = effects ? Theme.CobaltTint : Theme.Sheet; propsBtn.BackColor = effects ? Theme.Sheet : Theme.CobaltTint;
            RebuildRight();
        }

        public void RefreshTray()
        {
            var items = new List<TrayItem>();
            foreach (var s in sessions.Where(s => s.Doc.Path == null && s.Doc.ProjectPath == null)) items.Add(new TrayItem { Doc = s.Doc, Badge = "NEW" });
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                string[] ok = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".snapl", ".mp4", ".avi", SessionDoc.Ext, SessionDoc.ShareExt };
                var files = new DirectoryInfo(Settings.Folder).GetFiles().Where(f => ok.Contains(f.Extension.ToLowerInvariant())).OrderByDescending(f => f.LastWriteTime).Take(80);
                foreach (var f in files)
                {
                    string e = f.Extension.ToLowerInvariant();
                    var s = sessions.FirstOrDefault(x => Same(x.Doc.Path, f.FullName) || Same(x.Doc.ProjectPath, f.FullName));
                    items.Add(new TrayItem { Path = f.FullName, Doc = s == null ? null : s.Doc, Badge = e == ".mp4" ? "MP4" : e == ".avi" ? "AVI" : e == ".gif" ? "GIF" : e == ".snapl" ? "PROJECT" : SessionDoc.IsSessionExt(e) ? "SESSION" : null });
                }
            }
            catch { }
            // pictures opened from elsewhere still show in the tray while open
            foreach (var s in sessions.Where(s => (s.Doc.Path != null || s.Doc.ProjectPath != null) && !items.Any(i => ReferenceEquals(i.Doc, s.Doc)))) items.Insert(0, new TrayItem { Path = s.Doc.ProjectPath ?? s.Doc.Path, Doc = s.Doc });
            // counts on the filter chips, then keep only the chosen kind
            var counts = new Dictionary<string, int> { { "all", items.Count }, { "images", 0 }, { "videos", 0 }, { "sessions", 0 } };
            foreach (var it in items) counts[KindOf(it)]++;
            foreach (var c in chips) { var k = (string)c.Tag; string label = k == "all" ? "All" : char.ToUpper(k[0]) + k.Substring(1); c.Text = label + "  " + counts[k]; }
            LayoutTrayBar();
            if (trayFilter != "all") items = items.Where(i => KindOf(i) == trayFilter).ToList();
            tray.Items.Clear(); tray.Items.AddRange(items); tray.Resync();
            tray.CurrentDoc = sessionMode ? null : Doc; tray.CurrentPath = sessionMode && sessionView != null && sessionView.Doc != null ? sessionView.Doc.Path : Doc == null ? null : (Doc.ProjectPath ?? Doc.Path);
            tray.Invalidate();
        }
        static bool Same(string a, string b) { return a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        void OpenTrayItem(TrayItem t)
        {
            if (t.Badge == "MP4" || t.Badge == "AVI") { new VideoDone(t.Path).Show(this); return; }
            if (t.Badge == "SESSION") { OpenSession(t.Path); return; }
            if (t.Doc != null) { OpenDoc(t.Doc); return; }
            try { OpenDoc(LoadAny(t.Path)); } catch (Exception ex) { Info("Could not open " + Path.GetFileName(t.Path) + ": " + ex.Message); }
        }
        void TrayMenu(TrayItem t)
        {
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f) };
            m.Items.Add("Open", null, delegate { OpenTrayItem(t); });
            if (t.Path != null)
            {
                m.Items.Add("Show in folder", null, delegate { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + t.Path + "\""); });
                m.Items.Add("Tags...", null, delegate { EditTags(t.Path); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Delete", null, delegate { DeleteTrayItem(t); });
            }
            else m.Items.Add("Discard", null, delegate { DeleteTrayItem(t); });
            m.Show(Cursor.Position);
        }
        // tray card delete button, Delete key and context menu: files go to the Recycle Bin, unsaved captures are discarded
        void DeleteTrayItem(TrayItem t)
        {
            bool saved = t.Path != null;
            string what = saved ? Path.GetFileName(t.Path) : "this unsaved capture";
            if (!Dialogs.ConfirmDelete(this, saved ? "Delete" : "Discard", saved ? "Move " + what + " to the Recycle Bin?" : "Discard " + what + "? It has not been saved.", saved ? "Move to Recycle Bin" : "Discard")) return;
            if (saved && sessionMode && sessionView != null && sessionView.Doc != null && string.Equals(sessionView.Doc.Path, t.Path, StringComparison.OrdinalIgnoreCase))
            { sessionView.Doc.Dirty = false; SetSessionMode(false); }
            var s = sessions.FirstOrDefault(x => t.Doc != null && ReferenceEquals(x.Doc, t.Doc)); if (s != null) { s.Doc.Dirty = false; CloseSession(s); }
            if (saved)
                try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(t.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); LibraryStore.Forget(t.Path); RecentTray.Forget(t.Path); } catch (Exception ex) { Info(ex.Message); }
            RefreshTray();
            Info(saved ? what + " moved to the Recycle Bin." : "Capture discarded.");
        }
        void TagCurrent() { if (Doc == null) return; var p = Doc.ProjectPath ?? Doc.Path; if (p == null) { Info("Save the picture first, then tag it."); return; } EditTags(p); }
        void EditTags(string path)
        {
            var s = Dialogs.Prompt(this, "Tags (separate with commas)", string.Join(", ", LibraryStore.Get(path).Tags)); if (s == null) return;
            LibraryStore.Get(path).Tags = s.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().TrimStart('#')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            LibraryStore.Save(); Info("Tags saved.");
        }

        // ------------------------------------------------------------ documents
        void OpenNewest()
        {
            if (sessions.Count > 0) return;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                string[] ok = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".snapl" };
                var f = new DirectoryInfo(Settings.Folder).GetFiles().Where(x => ok.Contains(x.Extension.ToLowerInvariant())).OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                if (f != null) OpenDoc(LoadAny(f.FullName));
            }
            catch { }
        }
        public void OpenDoc(Doc d)
        {
            if (sessionMode) { if (!LeaveSession()) return; SetSessionMode(false); }
            if (libraryMode) SetMode(false);
            var s = sessions.FirstOrDefault(x => ReferenceEquals(x.Doc, d) || Same(x.Doc.Path, d.Path) && x.Doc.ProjectPath == d.ProjectPath || Same(x.Doc.ProjectPath, d.ProjectPath));
            if (s == null)
            {
                s = new Session { Doc = d, Canvas = new DocCanvas(d) { Location = Point.Empty } };
                var c = s.Canvas; c.Style = style;
                c.ZoomChanged += delegate { if (canvas == c) UpdateStatus(); };
                c.SelectionChanged += delegate { if (canvas == c) OnSelectionChanged(); };
                c.Message += delegate(string m) { Info(m); };
                c.EditText += EditObjectText;
                c.PickedUp += delegate       // a drawing tool picked up an existing object: show the Select tool as the active one, without cutting the drag short
                {
                    if (canvas != c) return; activeTool = Tool.Select; foreach (var kv in toolBtns) kv.Value.Checked = kv.Key == Tool.Select;
                    if (moreBtn != null) { moreBtn.Text = "More"; moreBtn.Image = Icons.Get(Ic.Grid, Dpi.S(26), Theme.Ink); moreBtn.Tag = null; }
                    RebuildRight(); Info("Selected. Drag to move, use the handles to resize, press Delete to remove. Hold Alt to draw over an existing shape.");
                };
                c.ColorPicked += delegate(Color col, bool asFill) { if (asFill) style.Fill = col; else style.Stroke = col; StyleChanged(); RebuildRight(); Info("Picked " + ColorTranslator.ToHtml(col)); };
                d.Changed += delegate { if (Doc == d) { Text = TitleText(); UpdateStatus(); } tray.Invalidate(); };
                sessions.Add(s);
            }
            Switch(s);
        }
        void Switch(Session s)
        {
            if (cur != s)
            {
                scroller.SuspendLayout();
                if (cur != null) scroller.Controls.Remove(cur.Canvas);
                cur = s; scroller.Controls.Add(s.Canvas);
                scroller.ResumeLayout();
            }
            empty.Visible = false; scroller.Visible = !libraryMode;
            s.Canvas.SetTool(activeTool);
            if (!s.Fitted) { s.Fitted = true; BeginInvoke((Action)Fit); }
            Text = TitleText(); RebuildRight(); UpdateStatus(); RefreshTray(); tray.ScrollToCurrent();
        }
        void CloseCurrent() { if (cur != null) CloseSession(cur); }
        void CloseSession(Session s)
        {
            if (s.Doc.Dirty)
            {
                var r = MessageBox.Show(this, "Save your changes to " + s.Doc.Name + " as an editable project?", "Close picture", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) return; if (r == DialogResult.Yes) SaveSession(s);
            }
            int i = sessions.IndexOf(s); sessions.Remove(s);
            if (cur == s) { scroller.Controls.Remove(s.Canvas); cur = null; }
            s.Canvas.Dispose();
            if (sessions.Count > 0) Switch(sessions[Math.Min(i, sessions.Count - 1)]);
            else { scroller.Visible = false; empty.Visible = !libraryMode; Text = TitleText(); RebuildRight(); UpdateStatus(); RefreshTray(); }
        }
        void SaveSession(Session s)
        {
            string p = s.Doc.ProjectPath ?? Path.Combine(s.Doc.Path != null ? Path.GetDirectoryName(s.Doc.Path) : Settings.Folder, s.Doc.Name + Project.Ext);
            try { Project.Save(s.Doc, p); } catch (Exception ex) { MessageBox.Show(this, "Could not save " + s.Doc.Name + ": " + ex.Message); }
        }
        public bool ConfirmClose()
        {
            if (!LeaveSession()) return false;
            var dirty = sessions.Where(s => s.Doc.Dirty).ToList();
            if (dirty.Count == 0) return true;
            var r = MessageBox.Show(this, "Save your changes to " + (dirty.Count == 1 ? dirty[0].Doc.Name : dirty.Count + " pictures") + " as editable projects?", "XLCapture Editor", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) foreach (var s in dirty) SaveSession(s);
            foreach (var s in dirty) s.Doc.Dirty = false;
            return true;
        }
        void NewBlank()
        {
            var r = Dialogs.Form(this, "New blank image", "Create",
                new Dialogs.Field { Label = "Width (px)", Kind = "int", Value = 1280, Min = 1, Max = 20000 }, new Dialogs.Field { Label = "Height (px)", Kind = "int", Value = 720, Min = 1, Max = 20000 },
                new Dialogs.Field { Label = "Background", Kind = "color", Value = Color.White });
            if (r == null) return;
            var b = new Bitmap((int)r["Width (px)"], (int)r["Height (px)"], PixelFormat.Format32bppArgb); using (var g = Graphics.FromImage(b)) g.Clear((Color)r["Background"]);
            OpenDoc(new Doc(b) { Name = "Untitled " + DateTime.Now.ToString("HH.mm.ss"), Dirty = true });
        }
        void NewFromClipboard()
        {
            if (!Clipboard.ContainsImage()) { Info("The clipboard has no picture."); return; }
            using (var img = Clipboard.GetImage()) OpenDoc(new Doc(new Bitmap(img)) { Name = "Clipboard " + DateTime.Now.ToString("HH.mm.ss"), Dirty = true });
        }
        List<string> PickPictures(string title)
        {
            using (var d = new OpenFileDialog { Title = title, Multiselect = true, InitialDirectory = Settings.Folder, Filter = "Pictures and projects|*.snapl;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" })
                return d.ShowDialog(this) == DialogResult.OK ? d.FileNames.ToList() : null;
        }
        LibraryView Lib() { if (library == null) { library = new LibraryView { Dock = DockStyle.Fill, Visible = false }; library.OpenRequested += delegate { SetMode(false); }; center.Controls.Add(library); } return library; }
        void CombineFiles() { var l = PickPictures("Choose two or more pictures"); if (l != null && l.Count > 1) Lib().Combine(l); else if (l != null) Info("Choose at least two pictures."); }
        void PdfFromFiles() { var l = PickPictures("Choose the pages, in order"); if (l != null && l.Count > 0) Lib().PdfExport(l); }
        void OpenRecording(bool trim)
        {
            using (var d = new OpenFileDialog { InitialDirectory = Settings.Folder, Filter = "Recordings|*.mp4;*.avi;*.gif" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (trim && !d.FileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) new TrimForm(d.FileName).ShowDialog(this); else new VideoDone(d.FileName).Show(this);
            }
        }

        // ------------------------------------------------------------ tools and the side panel
        public void SelectTool(Tool t)
        {
            activeTool = t;
            if (canvas != null) canvas.SetTool(t);
            foreach (var kv in toolBtns) kv.Value.Checked = kv.Key == t;
            bool more = moreTools.Contains(t);
            if (moreBtn != null)
            {
                moreBtn.Text = more ? NameOf(t) : "More"; moreBtn.Image = Icons.Get(more ? IconOf(t) : Ic.Grid, Dpi.S(26), Theme.Ink); moreBtn.Tag = more ? "active" : null;
            }
            RebuildRight();
            Info(ToolHint(t));
        }
        static string ToolHint(Tool t)
        {
            switch (t)
            {
                case Tool.Select: return "Click to select. Drag to move. Double-click text to edit. Shift-click adds to the selection.";
                case Tool.Arrow: case Tool.Line: return "Drag to draw. Hold Shift for 45 degree angles.";
                case Tool.Shape: return "Drag to draw. Hold Shift for a square or circle.";
                case Tool.Callout: return "Drag a bubble, type its text, then pull the red handle to aim the tail.";
                case Tool.Text: return "Click where the text should start.";
                case Tool.Stamp: return "Click to place the chosen stamp.";
                case Tool.Step: return "Click to place the next numbered step.";
                case Tool.Pen: case Tool.Marker: case Tool.Highlighter: return "Drag to draw freehand. Hold Shift for a straight stroke.";
                case Tool.Fill: return "Click an area to fill it with the fill colour.";
                case Tool.Magnify: return "Drag a lens over the detail you want enlarged.";
                case Tool.Spotlight: return "Drag over the area to keep bright. The rest dims.";
                case Tool.Blur: return "Drag over anything sensitive to blur or pixelate it.";
                case Tool.Crop: return "Drag the crop area, adjust its handles, then press Enter.";
                case Tool.Cut: return "Drag across the picture. That band is removed and the halves join.";
                case Tool.SmartMove: return "Drag around a region. It lifts out as a movable object and the gap is filled.";
                case Tool.SelRect: case Tool.SelEllipse: case Tool.Lasso: return "Drag to select pixels. Then copy, cut, fill, crop or lift them.";
                case Tool.Wand: return "Click a colour to select every matching pixel.";
                case Tool.Eyedropper: return "Click to pick a colour. Alt-click picks the fill.";
            }
            return "";
        }

        Obj Single { get { return canvas != null && canvas.Sel.Count == 1 ? canvas.Sel[0] : null; } }
        string PropsKind()
        {
            var o = Single;
            if (activeTool == Tool.Select && o != null)
            {
                if (o is LineObj) return ((LineObj)o).ArrowEnd || ((LineObj)o).ArrowStart ? "arrow" : "line";
                if (o is ShapeObj) return "shape"; if (o is CalloutObj) return "callout"; if (o is TextObj) return "text"; if (o is StampObj) return "stamp";
                if (o is StepObj) return "step"; if (o is PenObj) return "pen"; if (o is BlurObj) return "blur"; if (o is SpotlightObj) return "spot";
                if (o is MagnifyObj) return "magnify"; if (o is ImageObj) return "image";
            }
            switch (activeTool)
            {
                case Tool.Arrow: return "arrow"; case Tool.Line: return "line"; case Tool.Shape: return "shape"; case Tool.Callout: return "callout";
                case Tool.Text: return "text"; case Tool.Stamp: return "stamp"; case Tool.Step: return "step";
                case Tool.Pen: case Tool.Marker: case Tool.Highlighter: return "pen"; case Tool.Fill: return "fill"; case Tool.Magnify: return "magnify";
                case Tool.Spotlight: return "spot"; case Tool.Blur: return "blur"; case Tool.Crop: return "crop"; case Tool.Cut: return "cut";
                case Tool.SmartMove: return "smart"; case Tool.SelRect: case Tool.SelEllipse: case Tool.Lasso: case Tool.Wand: return "sel"; case Tool.Eyedropper: return "eyedrop";
            }
            return canvas != null && canvas.Sel.Count > 1 ? "multi" : "select";
        }
        void StyleChanged() { if (building) return; if (canvas != null && canvas.Sel.Count > 0 && activeTool == Tool.Select) canvas.ApplyStyleToSelection(); }
        ToolStyle S { get { return style; } }
        int RowW { get { return right.Width - Dpi.S(40); } }

        void RebuildRight()
        {
            if (right == null || rightFlow == null) return;
            building = true;
            rightFlow.SuspendLayout();
            foreach (Control c in rightFlow.Controls.Cast<Control>().ToList()) { rightFlow.Controls.Remove(c); c.Dispose(); }
            if (showEffects) BuildEffects(); else BuildProperties();
            rightFlow.ResumeLayout();
            building = false;
        }
        Label Header(string t)
        {
            var l = new Label { Text = t, AutoSize = false, Width = RowW, Height = Dpi.S(40), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Theme.LabelFamily, 12f, FontStyle.Bold), ForeColor = Theme.Ink, Margin = new Padding(0, Dpi.S(4), 0, Dpi.S(2)) };
            l.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, l.Height - 1, l.Width, l.Height - 1); };
            rightFlow.Controls.Add(l); return l;
        }
        Panel Row(int h) { var p = new Panel { Width = RowW, Height = Dpi.S(h), BackColor = Theme.Sheet, Margin = new Padding(0, 0, 0, Dpi.S(4)) }; rightFlow.Controls.Add(p); return p; }
        Label RowLabel(Panel p, string t, int y)
        {
            var l = new Label { Text = t, AutoSize = false, Location = new Point(0, Dpi.S(y)), Size = Dpi.S(76, 24), ForeColor = Theme.InkSoft, TextAlign = ContentAlignment.MiddleRight }; p.Controls.Add(l); return l;
        }
        void ComboRow(string label, string[] items, int idx, Action<int> set)
        {
            var p = Row(34); RowLabel(p, label, 5);
            var c = new ThemedCombo { Location = new Point(Dpi.S(82), Dpi.S(4)), Width = RowW - Dpi.S(84), Font = Theme.Body(9.5f), AccessibleName = label };
            c.Items.AddRange(items); c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, idx));
            c.SelectedIndexChanged += delegate { set(c.SelectedIndex); S.Raise(); };
            p.Controls.Add(c);
        }
        void Slider(string label, int min, int max, int value, Action<int> set)
        {
            var s = new SliderRow(label, min, max, value, 0) { Width = RowW, Margin = new Padding(0, 0, 0, Dpi.S(2)) };
            s.Width = RowW; s.ValueChanged += delegate(int v) { set(v); S.Raise(); };
            // re-place children for the actual width
            s.Bar.Width = RowW - Dpi.S(80 + 64); s.Box.Left = RowW - Dpi.S(58);
            rightFlow.Controls.Add(s);
        }
        void CheckRow(string label, bool value, Action<bool> set)
        {
            var p = Row(34); RowLabel(p, label, 5);
            var t = new ToggleSwitch(value) { Location = new Point(Dpi.S(84), Dpi.S(4)), AccessibleName = label };
            t.Toggled += delegate { set(t.Checked); S.Raise(); }; p.Controls.Add(t);
        }
        void Hint(string text)
        {
            var l = new Label { Text = text, AutoSize = false, Width = RowW, Height = Dpi.S(52), ForeColor = Theme.Slate, Font = Theme.Body(9.5f), Margin = new Padding(0, Dpi.S(4), 0, Dpi.S(4)) };
            rightFlow.Controls.Add(l);
        }
        void Buttons(params KeyValuePair<string, Action>[] bs)
        {
            var f = new FlowLayoutPanel { Width = RowW, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new Size(RowW, 0), WrapContents = true, BackColor = Theme.Sheet, Margin = new Padding(0, Dpi.S(2), 0, Dpi.S(4)) };
            foreach (var kv in bs) { var a = kv.Value; var b = new FlatButton(kv.Key, false) { AutoSize = true, Height = Dpi.S(32), Margin = new Padding(0, 0, Dpi.S(6), Dpi.S(6)) }; b.Click += delegate { if (cur != null) a(); }; f.Controls.Add(b); }
            rightFlow.Controls.Add(f);
        }
        static KeyValuePair<string, Action> B(string t, Action a) { return new KeyValuePair<string, Action>(t, a); }

        void ColorsRow(string k)
        {
            var p = Row(70);
            int x = 0;
            Action<string, Func<Color>, Action<Color>, bool> chip = delegate(string label, Func<Color> get, Action<Color> set, bool none)
            {
                var l = new Label { Text = label, AutoSize = false, Location = new Point(x, 0), Size = Dpi.S(64, 20), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.InkSoft };
                var c = new ColorChip(get(), none, label + " colour") { Location = new Point(x + Dpi.S(6), Dpi.S(22)), Size = Dpi.S(52, 40), BackColor = Theme.Sheet };
                c.ColorChanged += delegate { set(c.Value); S.Raise(); };
                c.PickRequested += delegate { SelectTool(Tool.Eyedropper); Info("Click a pixel to pick its colour."); };
                p.Controls.Add(l); p.Controls.Add(c); x += Dpi.S(72);
            };
            if (k == "text") { chip("Color", () => S.Stroke, c => S.Stroke = c, false); chip("Back", () => S.Fill, c => S.Fill = c, true); }
            else if (k == "callout") { chip("Fill", () => S.Fill.A > 0 ? S.Fill : S.Stroke, c => { S.Fill = c; S.Stroke = c; }, false); chip("Text", () => S.TextCol, c => S.TextCol = c, false); }
            else if (k == "step") { chip("Fill", () => S.Fill.A > 0 ? S.Fill : S.Stroke, c => S.Fill = c, false); chip("Number", () => S.Stroke, c => S.Stroke = c, false); }
            else if (k == "shape" || k == "fill") { chip("Color", () => S.Stroke, c => S.Stroke = c, false); chip("Fill", () => S.Fill, c => S.Fill = c, true); }
            else chip("Color", () => S.Stroke, c => S.Stroke = c, false);
            if (k != "fill")
            {
                var l = new Label { Text = "Shadow", AutoSize = false, Location = new Point(x, 0), Size = Dpi.S(64, 20), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.InkSoft };
                var t = new ToggleSwitch(S.Shadow) { Location = new Point(x + Dpi.S(8), Dpi.S(30)), AccessibleName = "Shadow" };
                t.Toggled += delegate { S.Shadow = t.Checked; S.Raise(); };
                p.Controls.Add(l); p.Controls.Add(t);
            }
        }

        void BuildProperties()
        {
            string k = PropsKind();
            bool hasStyles = QuickStyles.For(k, Settings.QuickTheme).Count > 0;
            Header("Quick Styles");
            if (hasStyles)
            {
                var tr = Row(34); RowLabel(tr, "Theme:", 5);
                var tb = new ThemedCombo { Location = new Point(Dpi.S(82), Dpi.S(4)), Width = RowW - Dpi.S(84), Font = Theme.Body(9.5f), AccessibleName = "Quick style theme" };
                tb.Items.AddRange(QuickStyles.Themes); tb.SelectedIndex = Math.Max(0, Array.IndexOf(QuickStyles.Themes, Settings.QuickTheme));
                tr.Controls.Add(tb);
                var grid = new StyleGrid { Width = RowW, Margin = new Padding(0, 0, 0, Dpi.S(4)) };
                grid.Set(k, Settings.QuickTheme, S.Stamp);
                grid.Picked += delegate(QStyle q) { QuickStyles.Apply(q, k, S); StyleChanged(); var keep = grid; RebuildPropsOnly(); };
                tb.SelectedIndexChanged += delegate { Settings.QuickTheme = (string)tb.SelectedItem; Settings.Save(); grid.Set(k, Settings.QuickTheme, S.Stamp); };
                rightFlow.Controls.Add(grid);
            }
            else Hint(cur == null ? "Open or capture a picture to start editing." : "This tool has no quick styles.");
            Header("Tool Properties");
            propsAnchor = rightFlow.Controls.Count;
            BuildToolProps(k);
        }
        int propsAnchor;
        // rebuild only the Tool Properties part (keeps the style grid and its selection)
        void RebuildPropsOnly()
        {
            if (showEffects) return;
            building = true; rightFlow.SuspendLayout();
            while (rightFlow.Controls.Count > propsAnchor) { var c = rightFlow.Controls[propsAnchor]; rightFlow.Controls.Remove(c); c.Dispose(); }
            BuildToolProps(PropsKind());
            rightFlow.ResumeLayout(); building = false;
        }

        void BuildToolProps(string k)
        {
            if (cur == null) { Hint("No picture is open."); return; }
            bool styled = k == "arrow" || k == "line" || k == "shape" || k == "callout" || k == "text" || k == "stamp" || k == "step" || k == "pen" || k == "fill" || k == "magnify";
            if (styled) ColorsRow(k);
            switch (k)
            {
                case "arrow":
                    ComboRow("Start", new[] { "None", "Arrow" }, S.ArrowStart ? 1 : 0, i => S.ArrowStart = i == 1);
                    ComboRow("End", new[] { "None", "Arrow" }, S.ArrowEnd ? 1 : 0, i => S.ArrowEnd = i == 1);
                    ComboRow("Line", new[] { "Solid", "Dashed", "Dotted" }, (int)S.Dash, i => S.Dash = (Dash)i);
                    Slider("Width", 1, 60, (int)S.Width, v => S.Width = v); break;
                case "line":
                    ComboRow("Line", new[] { "Solid", "Dashed", "Dotted" }, (int)S.Dash, i => S.Dash = (Dash)i);
                    Slider("Width", 1, 60, (int)S.Width, v => S.Width = v); break;
                case "shape":
                    ComboRow("Shape", new[] { "Rectangle", "Rounded", "Ellipse", "Triangle", "Diamond", "Hexagon", "Star" }, (int)S.Shape, i => S.Shape = (ShapeKind)i);
                    ComboRow("Line", new[] { "Solid", "Dashed", "Dotted" }, (int)S.Dash, i => S.Dash = (Dash)i);
                    Slider("Width", 0, 60, (int)S.Width, v => S.Width = v);
                    Slider("Corner", 1, 200, (int)S.Corner, v => S.Corner = v); break;
                case "callout":
                    ComboRow("Shape", new[] { "Rounded", "Rectangle", "Ellipse" }, S.Shape == ShapeKind.Ellipse ? 2 : S.Shape == ShapeKind.Rect ? 1 : 0, i => S.Shape = i == 2 ? ShapeKind.Ellipse : i == 1 ? ShapeKind.Rect : ShapeKind.Rounded);
                    Slider("Text size", 8, 120, (int)S.FontPx, v => S.FontPx = v);
                    Slider("Outline", 0, 20, (int)S.Width, v => S.Width = v); break;
                case "text":
                    var fonts = new[] { Theme.BodyFamily, Theme.LabelFamily, "Segoe UI", "Arial", "Georgia", "Times New Roman", "Consolas", "Comic Sans MS", "Impact" }.Distinct().ToArray();
                    ComboRow("Font", fonts, Math.Max(0, Array.IndexOf(fonts, S.Font)), i => S.Font = fonts[i]);
                    Slider("Size", 6, 200, (int)S.FontPx, v => S.FontPx = v);
                    ComboRow("Align", new[] { "Left", "Centre", "Right" }, S.Align == StringAlignment.Near ? 0 : S.Align == StringAlignment.Center ? 1 : 2, i => S.Align = i == 0 ? StringAlignment.Near : i == 1 ? StringAlignment.Center : StringAlignment.Far);
                    CheckRow("Bold", S.Bold, b => S.Bold = b); CheckRow("Italic", S.Italic, b => S.Italic = b); CheckRow("Underline", S.Underline, b => S.Underline = b); break;
                case "stamp": StampGrid(); break;
                case "step":
                    ComboRow("Style", new[] { "1 2 3", "A B C", "I II III" }, (int)S.Step, i => S.Step = (StepStyle)i);
                    Slider("Size", 1, 12, (int)S.Width, v => S.Width = v);
                    Hint("Next step: " + Doc.NextStepNumber()); break;
                case "pen": Slider("Width", 1, 60, (int)S.Width, v => S.Width = v); break;
                case "fill": Slider("Tolerance", 0, 255, S.Tolerance, v => S.Tolerance = v); break;
                case "blur":
                    ComboRow("Mode", new[] { "Pixelate", "Smooth" }, (int)S.Blur, i => S.Blur = (BlurKind)i);
                    Slider("Strength", 2, 60, S.BlurStrength, v => S.BlurStrength = v); break;
                case "spot":
                    ComboRow("Shape", new[] { "Rectangle", "Rounded", "Ellipse" }, S.SpotKind == ShapeKind.Rect ? 0 : S.SpotKind == ShapeKind.Rounded ? 1 : 2, i => S.SpotKind = i == 0 ? ShapeKind.Rect : i == 1 ? ShapeKind.Rounded : ShapeKind.Ellipse);
                    Slider("Dim", 20, 235, S.SpotDim, v => S.SpotDim = v); break;
                case "magnify":
                    Slider("Zoom x10", 10, 80, (int)(S.MagZoom * 10), v => S.MagZoom = v / 10f);
                    Slider("Border", 0, 20, (int)S.Width, v => S.Width = v);
                    CheckRow("Round", S.MagRound, b => S.MagRound = b); break;
                case "crop": Hint("Drag an area, adjust its handles, then press Enter."); Buttons(B("Apply crop", () => canvas.ApplyCrop()), B("Cancel", () => canvas.CancelCrop())); break;
                case "cut": Hint("Drag across the picture. A horizontal or vertical band is removed and the halves join."); break;
                case "smart": Hint("Drag around a region. It lifts out as a movable object and the gap is filled from its surroundings."); break;
                case "sel":
                    if (activeTool == Tool.Wand) { Slider("Tolerance", 0, 255, S.Tolerance, v => S.Tolerance = v); CheckRow("Contiguous", S.Contiguous, b => S.Contiguous = b); }
                    Buttons(B("Copy", () => canvas.SelectionCopy()), B("Cut", () => canvas.SelectionCut()), B("Delete", () => canvas.SelectionDelete()), B("Fill", () => canvas.SelectionFill(S.Fill.A > 0 ? S.Fill : S.Stroke)),
                        B("Crop to selection", () => canvas.SelectionCrop()), B("Invert", () => canvas.SelectionInvert()), B("Lift to move", () => { canvas.LiftSelection(true); SelectTool(Tool.Select); }));
                    break;
                case "eyedrop": Hint("Click a pixel to pick it into the colour. Alt-click picks the fill."); break;
                case "multi": Hint(canvas.Sel.Count + " objects selected."); AlignRows(); break;
                case "image": Hint("Pasted or lifted picture."); break;
                case "select": Hint("Select an object to change its style, or choose a tool above."); break;
            }
            if (k == "arrow" || k == "line" || k == "shape" || k == "callout" || k == "text" || k == "stamp" || k == "step" || k == "pen" || k == "blur" || k == "spot" || k == "magnify" || k == "image")
                Slider("Opacity", 5, 100, (int)(S.Opacity * 100), v => S.Opacity = v / 100f);
            if (Single != null && activeTool == Tool.Select) AlignRows();
            var type = TypeForKind(k);
            if (type != null)
            {
                var b = new FlatButton("Select All (" + KindName(k) + ")", false) { Width = RowW, Height = Dpi.S(38), Margin = new Padding(0, Dpi.S(10), 0, 0) };
                b.Click += delegate { SelectTool(Tool.Select); canvas.SelectWhere(o => type.IsInstanceOfType(o) && (k != "arrow" || ((LineObj)o).ArrowEnd || ((LineObj)o).ArrowStart) && (k != "line" || !(((LineObj)o).ArrowEnd || ((LineObj)o).ArrowStart))); OnSelectionChanged(); };
                rightFlow.Controls.Add(b);
            }
        }
        static Type TypeForKind(string k)
        {
            switch (k)
            {
                case "arrow": case "line": return typeof(LineObj); case "shape": return typeof(ShapeObj); case "callout": return typeof(CalloutObj); case "text": return typeof(TextObj);
                case "stamp": return typeof(StampObj); case "step": return typeof(StepObj); case "pen": return typeof(PenObj); case "blur": return typeof(BlurObj);
                case "spot": return typeof(SpotlightObj); case "magnify": return typeof(MagnifyObj);
            }
            return null;
        }
        static string KindName(string k) { switch (k) { case "spot": return "Spotlight"; case "pen": return "Pen"; default: return char.ToUpper(k[0]) + k.Substring(1); } }
        void AlignRows()
        {
            Buttons(B("Align left", () => canvas.Align("left")), B("Centre", () => canvas.Align("hcenter")), B("Align right", () => canvas.Align("right")),
                B("Top", () => canvas.Align("top")), B("Middle", () => canvas.Align("vcenter")), B("Bottom", () => canvas.Align("bottom")),
                B("Bring to front", () => canvas.Order("front")), B("Send to back", () => canvas.Order("back")), B("Duplicate", () => canvas.Duplicate()), B("Delete", () => canvas.DeleteSelected()));
        }
        void StampGrid()
        {
            var f = new FlowLayoutPanel { Width = RowW, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new Size(RowW, 0), BackColor = Theme.Sheet, Margin = new Padding(0, Dpi.S(2), 0, Dpi.S(6)) };
            var glyphs = new[] { Ic.Check, Ic.Close, Ic.Question, Ic.Exclaim, Ic.Info, Ic.Warning, Ic.Bug, Ic.Flag, Ic.Pin, Ic.Bulb, Ic.Star, Ic.Heart, Ic.Cursor, Ic.Arrow };
            foreach (var gl in glyphs)
            {
                var g2 = gl;
                var b = new Button { Width = Dpi.S(40), Height = Dpi.S(40), FlatStyle = FlatStyle.Flat, BackColor = S.Stamp == g2 ? Theme.CobaltTint : Color.White, Margin = new Padding(0, 0, Dpi.S(4), Dpi.S(4)), Cursor = Theme.Hand, Image = Icons.Get(g2, Dpi.S(24), S.Stroke), AccessibleName = g2 + " stamp" };
                b.FlatAppearance.BorderColor = S.Stamp == g2 ? Theme.Cobalt : Theme.Rule;
                b.Click += delegate { S.Stamp = g2; S.Raise(); RebuildRight(); };
                f.Controls.Add(b);
            }
            rightFlow.Controls.Add(f);
        }

        void BuildEffects()
        {
            Header("Effects");
            if (cur == null) { Hint("Open or capture a picture first."); return; }
            var items = new List<KeyValuePair<string, Action>> {
                B("Border...", BorderDialog), B("Drop shadow...", ShadowDialog), B("Edges...", EdgeDialog), B("Perspective...", PerspectiveDialog),
                B("Adjust colour...", AdjustDialog), B("Grayscale", () => Doc.ApplyPixels(Effects.Grayscale)), B("Sepia", () => Doc.ApplyPixels(Effects.Sepia)), B("Invert", () => Doc.ApplyPixels(Effects.Invert)),
                B("Posterize...", PosterizeDialog), B("Sharpen", () => Doc.ApplyPixels(Effects.Sharpen)), B("Blur...", BlurDialog), B("Emboss", () => Doc.ApplyPixels(Effects.Emboss)),
                B("Detect edges", () => Doc.ApplyPixels(Effects.EdgeDetect)), B("Replace colour...", ReplaceColorDialog), B("Transparent background", MakeTransparent), B("Watermark...", Watermark) };
            var ic = new[] { Ic.Fit, Ic.Layers, Ic.Cut, Ic.Order, Ic.Effects, Ic.Image, Ic.Image, Ic.Image, Ic.Grid, Ic.Star, Ic.Blur, Ic.Shape, Ic.Draw, Ic.Dropper, Ic.Grid, Ic.Stamp };
            for (int i = 0; i < items.Count; i++)
            {
                var a = items[i].Value;
                var b = new Button { Text = "   " + items[i].Key, Image = Icons.Get(ic[i], Dpi.S(20), Theme.Ink), TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft,
                    Width = RowW, Height = Dpi.S(40), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Theme.Hand, Font = Theme.Body(10f), Margin = new Padding(0, 0, 0, Dpi.S(4)), Padding = Dpi.P(8, 0, 0, 0) };
                b.FlatAppearance.BorderColor = Theme.Rule; b.FlatAppearance.MouseOverBackColor = Theme.CobaltTint;
                b.Click += delegate { if (cur != null) a(); };
                rightFlow.Controls.Add(b);
            }
            Header("Image");
            Buttons(B("Resize...", ResizeDialog), B("Canvas size...", CanvasDialog), B("Trim borders", Trim), B("Rotate right", () => Doc.RotateCW()), B("Rotate left", () => Doc.RotateCCW()),
                B("Flip", () => Doc.FlipH()), B("Flatten", () => { Doc.Flatten(); canvas.Sel.Clear(); OnSelectionChanged(); }));
        }

        // ------------------------------------------------------------ state
        void OnSelectionChanged()
        {
            if (canvas != null && canvas.Sel.Count > 0) canvas.PullStyleFromSelection();
            RebuildRight();
        }
        void RefreshLayers() { tray.Invalidate(); }
        void UpdateStatus()
        {
            if (cur == null) { sizeBtn.Text = ""; zoomBox.Text = ""; return; }
            sizeBtn.Text = " " + Doc.Width + " x " + Doc.Height + "px";
            if (!zoomBox.Focused) zoomBox.Text = (int)Math.Round(canvas.Zoom * 100) + "%";
            LayoutTrayBar();
        }
        Label zoomIcon;
        // the bottom bar names the open session, so one can be told from another at a glance
        Label sessLabel;
        void UpdateSessionLabel()
        {
            if (sessLabel == null)
            {
                sessLabel = new Label { AutoSize = false, ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleRight, Font = Theme.BodyBold(9.5f), AutoEllipsis = true, Visible = false, AccessibleName = "Session name" };
                trayBar.Controls.Add(sessLabel);
            }
            var d = sessionMode && sessionView != null ? sessionView.Doc : null;
            sessLabel.Visible = d != null;
            foreach (Control c in new Control[] { zoomIcon, zoomBox, sizeBtn, effectsBtn, propsBtn }) if (c != null) c.Visible = d == null;
            if (d != null) status.Text = "";      // a hint left over from the picture editor
            if (d != null) sessLabel.Text = "Session: " + d.Name + "  ·  " + d.Steps.Count + (d.Steps.Count == 1 ? " step" : " steps");
            LayoutTrayBar();
        }
        void LayoutTrayBar()
        {
            if (zoomIcon == null) return;
            if (sessLabel != null && sessLabel.Visible)
            {
                int y0 = (trayBar.Height - Dpi.S(32)) / 2, x0 = Dpi.S(10);
                hideRecent.Location = new Point(x0, y0); x0 += hideRecent.Width + Dpi.S(4); tagBtn.Location = new Point(x0, y0); x0 += tagBtn.Width + Dpi.S(14);
                foreach (var c in chips) { c.Location = new Point(x0, (trayBar.Height - c.Height) / 2); x0 += c.Width + Dpi.S(4); }
                x0 += Dpi.S(10);
                int sw = Math.Max(Dpi.S(160), Math.Min(Dpi.S(520), trayBar.Width / 3));
                sessLabel.Size = new Size(sw, Dpi.S(32)); sessLabel.Location = new Point(trayBar.Width - Dpi.S(14) - sw, y0);
                status.Location = new Point(x0, y0); status.Size = new Size(Math.Max(10, sessLabel.Left - x0 - Dpi.S(12)), Dpi.S(32));
                return;
            }
            int y = (trayBar.Height - Dpi.S(32)) / 2, x = Dpi.S(10);
            hideRecent.Location = new Point(x, y); x += hideRecent.Width + Dpi.S(4); tagBtn.Location = new Point(x, y); x += tagBtn.Width + Dpi.S(14);
            foreach (var c in chips) { c.Location = new Point(x, (trayBar.Height - c.Height) / 2); x += c.Width + Dpi.S(4); }
            x += Dpi.S(10);
            int rx = trayBar.Width - Dpi.S(10) - propsBtn.Width; propsBtn.Location = new Point(rx, y); rx -= effectsBtn.Width + Dpi.S(4); effectsBtn.Location = new Point(rx, y);
            int cw = zoomIcon.Width + zoomBox.Width + Dpi.S(14) + sizeBtn.Width, cx = (trayBar.Width - cw) / 2;
            zoomIcon.Location = new Point(cx, (trayBar.Height - zoomIcon.Height) / 2); zoomBox.Location = new Point(cx + zoomIcon.Width + Dpi.S(2), (trayBar.Height - zoomBox.Height) / 2);
            sizeBtn.Location = new Point(zoomBox.Right + Dpi.S(12), y);
            status.Location = new Point(x, y); status.Size = new Size(Math.Max(10, zoomIcon.Left - x - Dpi.S(12)), Dpi.S(32));
        }
        void ApplyZoomText(string t)
        {
            if (canvas == null || string.IsNullOrEmpty(t)) return;
            if (t.StartsWith("Fit")) { Fit(); return; }
            float v; if (float.TryParse(t.Replace("%", "").Trim(), out v) && v > 0) canvas.SetZoom(v / 100f);
            canvas.Focus();
        }
        void Info(string s) { if (status != null) status.Text = s; }
        string TitleText() { return Doc == null ? "XLCapture Editor" : Doc.Name + (Doc.Dirty ? " *" : "") + " - XLCapture Editor"; }
        public void Fit()
        {
            if (canvas == null) return;
            float z = Math.Min((scroller.ClientSize.Width - DocCanvas.Margin * 2f) / Doc.Width, (scroller.ClientSize.Height - DocCanvas.Margin * 2f) / Doc.Height);
            canvas.SetZoom(Math.Max(0.05f, Math.Min(1f, z)));
            CentreCanvas();
        }
        void CentreCanvas()
        {
            if (canvas == null) return;
            int x = Math.Max(0, (scroller.ClientSize.Width - canvas.Width) / 2), y = Math.Max(0, (scroller.ClientSize.Height - canvas.Height) / 2);
            if (canvas.Width >= scroller.ClientSize.Width) x = scroller.AutoScrollPosition.X; if (canvas.Height >= scroller.ClientSize.Height) y = scroller.AutoScrollPosition.Y;
            canvas.Location = new Point(x, y);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); CentreCanvas(); }

        void EditObjectText(Obj o)
        {
            var t = o as TextObj; var c = o as CalloutObj;
            string current = t != null ? t.Text : c != null ? c.Text : "";
            var s = Dialogs.Prompt(this, t != null ? "Edit text" : "Edit callout", current);
            if (s == null) return;
            Doc.Checkpoint();
            if (t != null) { t.Text = s; t.Measure(); } else if (c != null) c.Text = s;
            Doc.Raise();
        }
        void CopySel() { if (canvas.HasSelection) canvas.SelectionCopy(); else if (canvas.Sel.Count > 0) canvas.CopyObjects(); else CopyImage(); }
        void CutSel() { if (canvas.HasSelection) canvas.SelectionCut(); else { canvas.CopyObjects(); canvas.DeleteSelected(); } }

        // ------------------------------------------------------------ keyboard
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool typing = ActiveControl is TextBoxBase || ActiveControl is NumericUpDown || ActiveControl is ComboBox || (ActiveControl != null && ActiveControl.Parent is NumericUpDown);
            if (keyData == (Keys.Control | Keys.N)) { if (NewCaptureRequested != null) NewCaptureRequested(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenFile(); return true; }
            if (keyData == (Keys.Control | Keys.L)) { SetMode(!libraryMode); return true; }
            if (sessionMode && !typing && keyData == (Keys.Control | Keys.S)) { sessionView.Save(); return true; }
            if (typing || cur == null || libraryMode || sessionMode) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == (Keys.Control | Keys.Z)) { Doc.Undo(); canvas.Sel.Clear(); OnSelectionChanged(); return true; }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { Doc.Redo(); canvas.Sel.Clear(); OnSelectionChanged(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveProject(false); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveProject(true); return true; }
            if (keyData == (Keys.Control | Keys.E)) { ExportImage(); return true; }
            if (keyData == (Keys.Control | Keys.W)) { CloseCurrent(); return true; }
            if (keyData == (Keys.Control | Keys.A)) { canvas.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.D)) { canvas.Duplicate(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.C)) { CopyImage(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { CopySel(); return true; }
            if (keyData == (Keys.Control | Keys.X)) { CutSel(); return true; }
            if (keyData == (Keys.Control | Keys.V)) { canvas.Paste(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { PrintImage(); return true; }
            if (keyData == (Keys.Control | Keys.D0)) { Fit(); return true; }
            if (keyData == (Keys.Control | Keys.D1)) { canvas.SetZoom(1f); return true; }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add)) { canvas.SetZoom(canvas.Zoom * 1.25f); return true; }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract)) { canvas.SetZoom(canvas.Zoom / 1.25f); return true; }
            Tool t;
            if (hotkeys.TryGetValue(keyData, out t)) { SelectTool(t); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
