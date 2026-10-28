// The editor's view of a session: steps grouped by screen (or task), a preview with numbered boxes you can drag,
// and the editing functions of ClickTrail's session editor: merge a screen with the previous one, start a new
// screenshot from a step, add a step, move and resize the box, move the number, crop, paste a picture,
// choose which picture a screen shows, delete a step or a whole screen, and undo. Plus what turns a session into
// something to share (export pictures, export a PDF guide, a guide folder, a slide pack).
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
    class SessionView : UserControl
    {
        public SessionDoc Doc { get; private set; }
        public event Action<Doc> OpenInEditor;
        public event Action<string> Message;
        public event EventHandler Saved, ContinueRequested, NameEdited;

        readonly TextBox name; readonly Label meta; readonly ListBox list; readonly Panel preview; readonly Label screenLabel;
        readonly ToolTip tips = new ToolTip(); Label zoomLabel; readonly Panel tools; TextBox inline; SessionStep inlineFor; readonly ComboBox pictureBox; readonly Label pictureLabel;
        readonly ToolStripMenuItem bDup, bMerge, bSplit, bCrop, bWhole, bPaste, bOriginal, bDelScreen, bPics, bPdf;
        readonly FlatButton bUndo, bApply, bCancelCrop, cropBtn; readonly FlatButton[] menuButtons;
        public ToolStripItem[] ShareItems { get; private set; }        // what the editor's Share button offers while a session is open
        List<List<SessionStep>> groups = new List<List<SessionStep>>();
        int screen; SessionStep current; bool loading;
        Bitmap baseBmp; byte[] baseRef;                        // the screen's picture as it is, decoded once

        class Row { public int Group = -1; public SessionStep Step; }

        // ---- undo: a snapshot of every step's editable state, kept before each change
        class StepState { public SessionStep S; public bool Break, Typed, BoxEdited, TextEdited; public byte[] Frame, Custom; public string From, Text, Param; public Rectangle Crop, Box; public PointF? Badge, TextPos; public int X, Y; public float Round; }
        readonly List<List<StepState>> undoStack = new List<List<StepState>>();
        List<StepState> Snapshot() { return Doc.Steps.Select(s => new StepState { S = s, Break = s.BreakBefore, Typed = s.Typed, BoxEdited = s.BoxEdited, TextEdited = s.StepTextEdited, Frame = s.Frame, Custom = s.CustomFrame, From = s.FrameFrom, Text = s.StepText, Param = s.Param, Crop = s.Crop, Box = s.Box, Badge = s.BadgePos, TextPos = s.TextPos, X = s.X, Y = s.Y, Round = s.Round }).ToList(); }
        readonly List<List<StepState>> redoStack = new List<List<StepState>>();
        public event EventHandler HistoryChanged;
        public bool CanUndo { get { return undoStack.Count > 0; } }
        public bool CanRedo { get { return redoStack.Count > 0; } }
        public void UndoChange() { Undo(); }
        public void RedoChange() { Redo(); }
        void HistoryMoved() { nudged = null; bUndo.Enabled = undoStack.Count > 0; if (HistoryChanged != null) HistoryChanged(this, EventArgs.Empty); }
        void Push(List<StepState> snap) { undoStack.Add(snap); if (undoStack.Count > 40) undoStack.RemoveAt(0); redoStack.Clear(); HistoryMoved(); }
        void Push() { Push(Snapshot()); }
        void Undo()
        {
            if (undoStack.Count == 0) { Say("There is nothing to undo."); return; }
            var snap = undoStack[undoStack.Count - 1]; undoStack.RemoveAt(undoStack.Count - 1); redoStack.Add(Snapshot());
            Restore(snap);
        }
        void Redo()
        {
            if (redoStack.Count == 0) { Say("There is nothing to redo."); return; }
            var snap = redoStack[redoStack.Count - 1]; redoStack.RemoveAt(redoStack.Count - 1); undoStack.Add(Snapshot());
            Restore(snap);
        }
        void Restore(List<StepState> snap)
        {
            Doc.Steps.Clear();
            foreach (var t in snap) { var s = t.S; s.BreakBefore = t.Break; s.Typed = t.Typed; s.BoxEdited = t.BoxEdited; s.StepTextEdited = t.TextEdited; s.Frame = t.Frame; s.CustomFrame = t.Custom; s.FrameFrom = t.From; s.StepText = t.Text; s.Param = t.Param; s.Crop = t.Crop; s.Box = t.Box; s.BadgePos = t.Badge; s.TextPos = t.TextPos; s.X = t.X; s.Y = t.Y; s.Round = t.Round; Doc.Steps.Add(s); }
            Doc.Renumber(); Doc.Dirty = true; cropMode = false; var keep = current; current = null; Rebuild(Math.Min(screen, Math.Max(0, Doc.Groups().Count - 1)));
            if (keep != null && Doc.Steps.Contains(keep)) SelectStep(keep);
            HistoryMoved();
        }

        public SessionView()
        {
            BackColor = Theme.Paper; Font = Theme.Body(9.5f);
            var left = new Panel { Dock = DockStyle.Left, Width = Dpi.S(430), BackColor = Theme.Sheet };
            left.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, left.Width - 1, 0, left.Width - 1, left.Height); };
            var head = new Panel { Dock = DockStyle.Top, Height = Dpi.S(92), BackColor = Theme.Sheet, Padding = Dpi.P(14, 12, 14, 0) };
            var nameLbl = new Label { Text = "Session name", AutoSize = true, Location = new Point(Dpi.S(14), Dpi.S(10)), ForeColor = Theme.Slate };
            name = new TextBox { Location = new Point(Dpi.S(14), Dpi.S(30)), Width = Dpi.S(398), Font = Theme.BodyBold(11f), BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Session name" };
            name.TextChanged += delegate { if (loading || Doc == null) return; Doc.Name = name.Text; Doc.Dirty = true; if (NameEdited != null) NameEdited(this, EventArgs.Empty); };
            meta = new Label { AutoSize = false, Location = new Point(Dpi.S(14), Dpi.S(64)), Size = Dpi.S(398, 22), ForeColor = Theme.Slate };
            head.Controls.AddRange(new Control[] { nameLbl, name, meta });

            list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawVariable, BackColor = Theme.Sheet, IntegralHeight = false, AccessibleName = "Steps" };
            list.MeasureItem += delegate(object s, MeasureItemEventArgs e) { var r = list.Items[e.Index] as Row; e.ItemHeight = r != null && r.Step == null ? Dpi.S(44) : Dpi.S(46); };
            list.DrawItem += DrawRow;
            list.SelectedIndexChanged += delegate { EndInline(true); var r = list.SelectedItem as Row; if (r == null) return; if (r.Step != null) { current = r.Step; ShowScreen(GroupOf(r.Step)); } else { current = null; ShowScreen(r.Group); } };
            list.MouseDoubleClick += delegate(object s, MouseEventArgs e)
            {
                int i = list.IndexFromPoint(e.Location); if (i < 0) return; var r = list.Items[i] as Row; if (r == null || r.Step == null) return;
                list.SelectedIndex = i; EditText();
            };
            list.MouseWheel += delegate { EndInline(true); };
            list.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return; int i = list.IndexFromPoint(e.Location); if (i < 0) return;
                list.SelectedIndex = i; var r = list.Items[i] as Row; if (r == null) return;
                (r.Step != null ? StepMenu() : ScreenMenu()).Show(list, e.Location);
            };
            list.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { DeleteStep(); e.Handled = true; } else if (e.KeyCode == Keys.F2 || e.KeyCode == Keys.Enter) { EditText(); e.Handled = true; } };

            // step tools
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Dpi.S(54), BackColor = Theme.Sheet, Padding = Dpi.P(10, 10, 10, 0), WrapContents = false };
            var stepMenu = NewMenu();
            MenuItem(stepMenu, "Edit step text", "Change the instruction for the selected step (F2)", EditText);
            MenuItem(stepMenu, "Add a step", "Add a step by hand to this screen, for something that is not a click", AddStep);
            MenuItem(stepMenu, "Delete step", "Remove the selected step (Del)", DeleteStep);
            stepMenu.Items.Add(new ToolStripSeparator());
            MenuItem(stepMenu, "Move up", "Move the selected step earlier", delegate { Move(-1); });
            MenuItem(stepMenu, "Move down", "Move the selected step later", delegate { Move(1); });
            actions.Controls.Add(MenuButton("Step", "Edit, add, delete or move the selected step", stepMenu, 32, 0));
            left.Controls.Add(list); left.Controls.Add(actions); left.Controls.Add(head);

            // the bar over the picture: moving between screens and editing on the left; zoom and Save on the right
            var bar = new Panel { Dock = DockStyle.Top, Height = Dpi.S(52), BackColor = Theme.Sheet };
            bar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1); };
            Func<Padding, FlowLayoutPanel> half = delegate(Padding pad) { return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Sheet, Padding = pad }; };
            var barL = half(Dpi.P(12, 10, 0, 0)); barL.Dock = DockStyle.Left;
            var barR = half(Dpi.P(0, 10, 12, 0)); barR.Dock = DockStyle.Right;
            bar.Controls.Add(barR); bar.Controls.Add(barL);
            // text, an icon, or both; a button with only an icon is round and carries its name as the tip
            Func<FlowLayoutPanel, string, Ic?, string, Action, bool, int, FlatButton> mk = delegate(FlowLayoutPanel host, string text, Ic? icon, string tip, Action a, bool primary, int h)
            {
                var b = new FlatButton(text, primary) { Icon = icon, Height = Dpi.S(h), Margin = new Padding(0, 0, Dpi.S(6), 0), AccessibleName = tip };
                if (text.Length == 0) { b.AutoSize = false; b.Size = new Size(Dpi.S(38), Dpi.S(h)); } else b.AutoSize = true;
                tips.SetToolTip(b, tip); b.Click += delegate { a(); }; host.Controls.Add(b); return b;
            };
            mk(barL, "", Ic.Prev, "Previous screen", delegate { if (screen > 0) { current = null; ShowScreen(screen - 1); SelectGroupRow(screen); } }, false, 32);
            screenLabel = new Label { AutoSize = false, Size = Dpi.S(124, 32), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.InkSoft, Font = Theme.Label(10f), Margin = new Padding(0, 0, Dpi.S(6), 0) };
            barL.Controls.Add(screenLabel);
            mk(barL, "", Ic.Chevron, "Next screen", delegate { if (screen < groups.Count - 1) { current = null; ShowScreen(screen + 1); SelectGroupRow(screen); } }, false, 32);
            barL.Controls.Add(new Panel { Width = Dpi.S(14), Height = 4 });
            mk(barL, "Edit this screen", Ic.EditBox, "Open this screen in the editor with its boxes and numbers as editable objects", EditScreen, false, 32);
            Func<Ic, string, double, FlatButton> zb = delegate(Ic ic, string tip, double f) { return mk(barR, "", ic, tip, delegate { ZoomAt(f, new Point(preview.Width / 2, preview.Height / 2)); }, false, 32); };
            zb(Ic.ZoomOut, "Zoom out (Ctrl + mouse wheel, or pinch on the touchpad)", 1 / 1.25);
            zoomLabel = new Label { AutoSize = false, Size = Dpi.S(54, 32), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.InkSoft, Font = Theme.Label(10f), Margin = new Padding(0, 0, Dpi.S(6), 0) }; barR.Controls.Add(zoomLabel);
            zb(Ic.ZoomIn, "Zoom in (Ctrl + mouse wheel, or pinch on the touchpad)", 1.25);
            mk(barR, "", Ic.Fit, "Fit: show the whole screenshot", ZoomReset, false, 32);
            barR.Controls.Add(new Panel { Width = Dpi.S(10), Height = 4 });
            mk(barR, "Continue recording", Ic.Record, "Save this session, then carry on recording: the new steps are added after the last one", delegate { if (Save() && ContinueRequested != null) ContinueRequested(this, EventArgs.Empty); }, false, 32);
            mk(barR, "Save", Ic.Save, "Save the session (Ctrl+S)", delegate { Save(); }, true, 32).Margin = new Padding(0);

            // the screen tools, under it: the screen, its look and Crop on the left; undo on the right (what used to be Export is the editor's Share button)
            tools = new Panel { Dock = DockStyle.Top, Height = Dpi.S(54), BackColor = Theme.Sheet };
            tools.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, tools.Height - 1, tools.Width, tools.Height - 1); };
            var toolsL = half(Dpi.P(12, 8, 0, 0)); toolsL.Dock = DockStyle.Left;
            var toolsR = half(Dpi.P(0, 8, 12, 0)); toolsR.Dock = DockStyle.Right;
            tools.Controls.Add(toolsR); tools.Controls.Add(toolsL);
            var screenMenu = NewMenu();
            MenuItem(screenMenu, "Copy screenshot", "Copy this screen with its boxes, numbers and text, to paste into another app", CopyScreenshot);
            bDup = MenuItem(screenMenu, "Duplicate screenshot", "Make a copy of this screen right after it, with its steps, to show it again with other boxes or notes", DuplicateScreen);
            bMerge = MenuItem(screenMenu, "Merge with previous", "Use the previous screen's screenshot for this screen's steps too", MergeWithPrevious);
            bSplit = MenuItem(screenMenu, "New screenshot from here", "Start a new screen at the selected step, with the picture taken at that step", NewScreenshotFromHere);
            screenMenu.Items.Add(new ToolStripSeparator());
            bCrop = MenuItem(screenMenu, "Crop", "Show only part of the screenshot", StartCrop);
            bWhole = MenuItem(screenMenu, "Whole screenshot", "Remove the crop", RemoveCrop);
            bPaste = MenuItem(screenMenu, "Paste screenshot", "Replace this screen's picture with one from the clipboard (you can also drop an image file on it)", PasteScreenshot);
            bOriginal = MenuItem(screenMenu, "Use original", "Go back to the recorded screenshot", UseOriginal);
            screenMenu.Items.Add(new ToolStripSeparator());
            bDelScreen = MenuItem(screenMenu, "Delete screenshot", "Delete this screen and all its steps (Undo brings it back)", DeleteScreen);
            var exportMenu = NewMenu();
            MenuItem(exportMenu, "Share session...", "Save a copy that only XLCapture can open, to send to a colleague", ShareSession);
            exportMenu.Items.Add(new ToolStripSeparator());
            bPics = MenuItem(exportMenu, "Pictures...", "Save every screen, marked up, as PNG files", ExportPictures);
            bPdf = MenuItem(exportMenu, "PDF guide...", "One page per screen with its numbered steps", ExportPdf);
            MenuItem(exportMenu, "Guide folder...", "Export clicks.json + screenshots for the click-guide-builder skill (makes a PowerPoint guide)", ExportGuide);
            MenuItem(exportMenu, "Slide pack for Claude...", "One slide per screenshot, grouped by task: a folder Claude turns into the slide pack PowerPoint", ExportSlidePack);
            ShareItems = exportMenu.Items.Cast<ToolStripItem>().ToArray();       // shown by the Share button in the editor's ribbon, not by a button here
            var screenBtn = MenuButton("Screen", "Merge, crop, replace or delete this screen's screenshot", screenMenu, 34, 6); var appearBtn = AppearanceButton();
            toolsL.Controls.Add(screenBtn); toolsL.Controls.Add(appearBtn);
            cropBtn = mk(toolsL, "Crop", Ic.Crop, "Show only part of the screenshot (then Enter keeps it, Esc leaves it)", StartCrop, false, 34);
            menuButtons = new[] { screenBtn, appearBtn, cropBtn };
            bApply = mk(toolsR, "Apply crop", Ic.Check, "Keep the selected part (Enter)", ApplyCrop, true, 34); bCancelCrop = mk(toolsR, "Cancel", null, "Leave the crop as it was (Esc)", CancelCrop, false, 34);
            bApply.Visible = false; bCancelCrop.Visible = false;
            bUndo = mk(toolsR, "", Ic.Undo, "Undo the last change (Ctrl+Z)", Undo, false, 34); bUndo.Enabled = false;
            pictureLabel = new Label { Text = "Screenshot:", AutoSize = true, Margin = new Padding(8, Dpi.S(6), 4, 0), ForeColor = Theme.Slate, Visible = false };
            pictureBox = new ThemedCombo { Width = Dpi.S(210), Margin = new Padding(0, Dpi.S(2), 0, 0), Visible = false, AccessibleName = "Which screenshot to use" };
            pictureBox.SelectedIndexChanged += delegate
            {
                if (loading || groups.Count == 0 || pictureBox.SelectedIndex < 0) return;
                var key = ((KeyValuePair<string, string>)pictureBox.SelectedItem).Key; var first = groups[screen][0];
                if (key == SessionDoc.PictureKey(groups[screen])) return;
                Push(); first.FrameFrom = key; Doc.Dirty = true; ShowScreen(screen);
            };
            toolsL.Controls.Add(pictureLabel); toolsL.Controls.Add(pictureBox);

            preview = new ZoomPanel { Dock = DockStyle.Fill, BackColor = Theme.Paper, AllowDrop = true }; preview.MouseWheel += PreviewWheel;
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(preview, true, null);
            preview.Paint += PaintPreview; preview.Resize += delegate { ClampPan(); UpdateZoomLabel(); preview.Invalidate(); };
            preview.MouseDown += PreviewDown; preview.MouseMove += PreviewMove; preview.MouseUp += PreviewUp; preview.MouseLeave += delegate { if (drag == Drag.None) SetHover(null); };
            preview.DragEnter += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap)) e.Effect = DragDropEffects.Copy; };
            preview.DragDrop += PreviewDrop;
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(preview); right.Controls.Add(tools); right.Controls.Add(bar);
            Controls.Add(right); Controls.Add(left);
        }

        public void Load(SessionDoc d)
        {
            Doc = d; loading = true; name.Text = d.Name; loading = false; undoStack.Clear(); redoStack.Clear(); HistoryMoved();
            Rebuild(0); if (groups.Count > 0) SelectGroupRow(0);
        }
        void Rebuild(int keepScreen)
        {
            EndInline(true);
            groups = Doc.Groups();
            int taskCount = Doc.Steps.Select(x => x.Task).Where(x => x.Length > 0).Distinct().Count();
            meta.Text = (taskCount > 0 ? taskCount + (taskCount == 1 ? " task, " : " tasks, ") : "") + Doc.Steps.Count + (Doc.Steps.Count == 1 ? " step" : " steps") + " on " + groups.Count + (groups.Count == 1 ? " screen" : " screens") + "   ·   recorded " + Doc.StartedAt.ToString("d MMM yyyy HH:mm");
            list.BeginUpdate(); list.Items.Clear();
            for (int i = 0; i < groups.Count; i++) { list.Items.Add(new Row { Group = i }); foreach (var s in groups[i]) list.Items.Add(new Row { Group = i, Step = s }); }
            list.EndUpdate();
            ShowScreen(Math.Max(0, Math.Min(groups.Count - 1, keepScreen)));
        }
        int GroupOf(SessionStep s) { for (int i = 0; i < groups.Count; i++) if (groups[i].Contains(s)) return i; return 0; }
        void SelectGroupRow(int g) { for (int i = 0; i < list.Items.Count; i++) { var r = (Row)list.Items[i]; if (r.Group == g && r.Step == null) { list.SelectedIndex = i; return; } } }
        void SelectStep(SessionStep s) { for (int i = 0; i < list.Items.Count; i++) if (((Row)list.Items[i]).Step == s) { list.SelectedIndex = i; return; } }

        // ------------------------------------------------------------ the picture
        void ShowScreen(int g)
        {
            screen = g; pan = PointF.Empty;
            byte[] pic = groups.Count > 0 && g >= 0 && g < groups.Count ? SessionDoc.PictureOf(groups[g]) : null;
            if (!ReferenceEquals(pic, baseRef) || (pic != null && baseBmp == null))     // decode again only when the picture is another one
            {
                if (baseBmp != null) { baseBmp.Dispose(); baseBmp = null; }
                baseRef = pic; if (pic != null) { try { baseBmp = SessionDoc.Decode(pic); } catch { baseBmp = null; } }
            }
            screenLabel.Text = groups.Count == 0 ? "No screens" : "Screen " + (g + 1) + " of " + groups.Count;
            UpdateTools(); UpdateZoomLabel(); preview.Invalidate();
        }
        // one button that opens the Appearance window: boxes, numbers and the text on the screenshots
        FlatButton AppearanceButton()
        {
            var b = new FlatButton("Appearance", false) { AutoSize = true, Icon = Ic.Settings, Height = Dpi.S(34), Margin = new Padding(0, 0, Dpi.S(6), 0), AccessibleName = "Appearance" };
            tips.SetToolTip(b, "Boxes, numbers and the text on the screenshots: show or hide, shape, size, font and colours"); b.Click += delegate { ShowLook(); };
            return b;
        }
        ContextMenuStrip NewMenu() { return new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f), ShowItemToolTips = true, ImageScalingSize = Dpi.S(4, 4) }; }
        ToolStripMenuItem MenuItem(ContextMenuStrip m, string text, string tip, Action a)
        {
            var i = new ToolStripMenuItem(text) { ToolTipText = tip, Padding = Dpi.P(4, 5, 4, 5) }; i.Click += delegate { a(); }; m.Items.Add(i); return i;
        }
        // a capsule button with a chevron that opens its menu just under it
        FlatButton MenuButton(string text, string tip, ContextMenuStrip m, int height, int gap)
        {
            var b = new FlatButton(text, false) { AutoSize = true, Chevron = true, Height = Dpi.S(height), Margin = new Padding(0, 0, Dpi.S(gap), 0), AccessibleName = text + " menu" };
            tips.SetToolTip(b, tip); b.Click += delegate { m.Show(b, new Point(0, b.Height + Dpi.S(2))); };
            m.Opened += delegate { b.ChevronUp = true; b.Invalidate(); }; m.Closed += delegate { b.ChevronUp = false; b.Invalidate(); };
            return b;
        }
        void UpdateTools()
        {
            bool has = groups.Count > 0 && screen < groups.Count; var first = has ? groups[screen][0] : null;
            foreach (var b in menuButtons) b.Visible = !cropMode;
            bUndo.Visible = !cropMode; bApply.Visible = cropMode; bCancelCrop.Visible = cropMode;
            if (!has) return;
            bMerge.Enabled = screen > 0; bSplit.Enabled = current != null && groups[screen][0] != current;
            bCrop.Enabled = cropBtn.Enabled = baseBmp != null; bWhole.Visible = cropMode || !first.Crop.IsEmpty; bOriginal.Visible = !cropMode && first.CustomFrame != null; bUndo.Enabled = undoStack.Count > 0;
            var pics = SessionDoc.Pictures(groups[screen]); bool choose = !cropMode && pics.Count > 1 && first.CustomFrame == null;
            pictureLabel.Visible = choose; pictureBox.Visible = choose;
            if (choose)
            {
                loading = true; pictureBox.DisplayMember = "Value"; pictureBox.Items.Clear(); foreach (var p in pics) pictureBox.Items.Add(p);
                string key = SessionDoc.PictureKey(groups[screen]); for (int i = 0; i < pics.Count; i++) if (pics[i].Key == key) pictureBox.SelectedIndex = i;
                loading = false;
            }
        }

        Rectangle View() { return baseBmp == null ? Rectangle.Empty : (cropMode ? new Rectangle(0, 0, baseBmp.Width, baseBmp.Height) : SessionDoc.ViewOf(groups[screen], baseBmp.Width, baseBmp.Height)); }
        float zoomK = 1f; PointF pan; bool panning; Point panFrom; PointF panStart0;     // zoom on top of "fit the window", and where the picture has been moved to
        float BaseFit(Rectangle v) { int pad = Dpi.S(28); return Math.Max(0.02f, Math.Min(Math.Min((preview.Width - pad * 2f) / v.Width, (preview.Height - pad * 2f) / v.Height), 1f)); }
        float Fit(Rectangle v) { return BaseFit(v) * zoomK; }
        Rectangle Dest(Rectangle v, float k) { int w = (int)(v.Width * k), h = (int)(v.Height * k); return new Rectangle((preview.Width - w) / 2 + (int)pan.X, (preview.Height - h) / 2 + (int)pan.Y, w, h); }
        void ClampPan()
        {
            if (baseBmp == null || zoomK <= 1.001f) { pan = PointF.Empty; return; }
            var v = View(); float k = Fit(v); float w = v.Width * k, h = v.Height * k, pad = Dpi.S(60);
            float mx = Math.Max(0, (w - preview.Width) / 2 + pad), my = Math.Max(0, (h - preview.Height) / 2 + pad);
            pan = new PointF(Math.Max(-mx, Math.Min(mx, pan.X)), Math.Max(-my, Math.Min(my, pan.Y)));
        }
        // zoom about a point on the picture (the mouse position, or the middle), so what is under the pointer stays put
        void ZoomAt(double factor, Point at)
        {
            if (baseBmp == null || groups.Count == 0) return;
            var v = View(); var ip = ToImage(at);
            zoomK = (float)Math.Max(1.0, Math.Min(10.0, zoomK * factor)); if (zoomK < 1.001f) { zoomK = 1f; pan = PointF.Empty; }
            float k1 = Fit(v); var r1 = Dest(v, k1);
            pan = new PointF(pan.X + at.X - (r1.X + (ip.X - v.X) * k1), pan.Y + at.Y - (r1.Y + (ip.Y - v.Y) * k1)); ClampPan();
            UpdateZoomLabel(); preview.Invalidate();
        }
        void ZoomReset() { zoomK = 1f; pan = PointF.Empty; UpdateZoomLabel(); preview.Invalidate(); }
        void UpdateZoomLabel() { if (zoomLabel == null) return; zoomLabel.Text = baseBmp == null ? "" : (int)Math.Round(Fit(View()) * 100) + "%"; }
        void PreviewWheel(object s, MouseEventArgs e)
        {
            if (baseBmp == null) return; var he = e as HandledMouseEventArgs;
            if ((ModifierKeys & Keys.Control) != 0) { ZoomAt(Math.Pow(1.12, e.Delta / 120.0), e.Location); if (he != null) he.Handled = true; }          // a touchpad pinch arrives as Ctrl + wheel
            else if (zoomK > 1.001f) { pan = new PointF(pan.X + ((ModifierKeys & Keys.Shift) != 0 ? e.Delta : 0), pan.Y + ((ModifierKeys & Keys.Shift) != 0 ? 0 : e.Delta)); ClampPan(); preview.Invalidate(); if (he != null) he.Handled = true; }
        }
        PointF ToImage(Point p) { var v = View(); float k = Fit(v); var r = Dest(v, k); return new PointF(v.X + (p.X - r.X) / k, v.Y + (p.Y - r.Y) / k); }

        void PaintPreview(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Paper);
            if (baseBmp == null)
            {
                using (var f = Theme.Body(11f)) g.DrawString(Doc == null || Doc.Steps.Count == 0 ? "This session has no steps." : "This screen has no picture. Paste one with Paste screenshot, or drop an image here.", f, new SolidBrush(Theme.Slate), Dpi.S(24), Dpi.S(24));
                return;
            }
            var v = View(); float k = Fit(v); var r = Dest(v, k);
            Theme.CropMarks(g, r, Dpi.S(6), Dpi.S(14), Theme.Mist);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(baseBmp, r, v.X, v.Y, v.Width, v.Height, GraphicsUnit.Pixel);
            if (cropMode) { PaintCrop(g, v, k, r); return; }
            var st = g.Save(); g.SetClip(r);
            g.TranslateTransform(r.X - v.X * k, r.Y - v.Y * k); g.ScaleTransform(k, k);
            Doc.DrawMarks(g, groups[screen], current, baseBmp.Width, true, v, 1);
            g.ResetClip(); Doc.DrawMarks(g, groups[screen], current, baseBmp.Width, true, v, 2);          // text outside the screenshot: plain, on the pasteboard
            var hv = CornersShownFor();      // the four corners to drag to resize a box show only while the pointer is over it, and not while its roundness is being set
            if (hv != null) for (int c = 0; c < 4; c++) { var hr = CornerHandle(hv, c); g.FillRectangle(Brushes.White, hr); using (var p = new Pen(Doc.BoxColor, 2f * baseBmp.Width / 1600f)) g.DrawRectangle(p, hr.X, hr.Y, hr.Width, hr.Height); }
            if (Doc.ShowBoxes && current != null && groups[screen].Contains(current) && current.Kind != "scroll")
            {
                if (Doc.BoxShape == "rounded")       // the round dot on the top edge: drag it sideways to change how round this box's corners are
                {
                    var rh = RoundHandle(current); g.FillEllipse(Brushes.White, rh); using (var p = new Pen(Doc.BoxColor, 2f * baseBmp.Width / 1600f)) g.DrawEllipse(p, rh);
                }
            }
            g.Restore(st);
        }
        // the dot that sets a rounded box's corner radius: on the top edge, that far in from the top-right corner
        RectangleF RoundHandle(SessionStep s) { float h = 10f * Math.Max(1f, baseBmp.Width / 1600f); return new RectangleF(s.Box.Right - Math.Max(SessionDoc.CornerRadius(s), 1.6f * h) - h / 2, s.Box.Y - h / 2, h, h); }      // never closer to the corner than its resize handle
        bool OnRoundHandle(SessionStep s, PointF pt)
        {
            if (!Doc.ShowBoxes || Doc.BoxShape != "rounded" || s == null || s.Kind == "scroll") return false;
            var rh = RoundHandle(s); float cx = rh.X + rh.Width / 2, cy = rh.Y + rh.Height / 2; return Math.Sqrt((pt.X - cx) * (pt.X - cx) + (pt.Y - cy) * (pt.Y - cy)) <= rh.Width * 0.9f;
        }
        // the four corner handles that resize the selected box: 0 top left, 1 top right, 2 bottom left, 3 bottom right
        RectangleF CornerHandle(SessionStep s, int c)
        {
            float h = 10f * Math.Max(1f, baseBmp.Width / 1600f); var b = s.Box; float x = c % 2 == 0 ? b.X : b.Right, y = c < 2 ? b.Y : b.Bottom;
            return new RectangleF(x - h / 2, y - h / 2, h, h);
        }
        int CornerAt(SessionStep s, PointF pt)
        {
            if (s == null || s.Kind == "scroll" || !Doc.ShowBoxes) return -1;
            PointF bc = Doc.ShowNumbers ? SessionDoc.BadgeCenter(s, baseBmp.Width, Doc.StepSize) : PointF.Empty; float br = SessionDoc.BadgeRadius(baseBmp.Width, Doc.StepSize);
            for (int c = 3; c >= 0; c--)
            {
                var hr = CornerHandle(s, c); var r = hr; r.Inflate(r.Width * 0.3f, r.Height * 0.3f); if (!r.Contains(pt)) continue;
                // the number sits over the top left corner: where the pointer is nearer the number's centre than the handle's, it is the number that is taken
                if (Doc.ShowNumbers) { float dn = Dist(pt, bc), dh = Dist(pt, new PointF(hr.X + hr.Width / 2, hr.Y + hr.Height / 2)); if (dn <= br && dn <= dh) continue; }
                return c;
            }
            return -1;
        }
        // the box the pointer is over (the selected one first), or null
        SessionStep ShapeUnder(PointF pt)
        {
            if (baseBmp == null || groups.Count == 0 || !Doc.ShowBoxes) return null;
            var g = groups[screen]; float pad = 10f * Math.Max(1f, baseBmp.Width / 1600f);
            Func<SessionStep, bool> over = delegate(SessionStep s) { if (s.Kind == "scroll") return false; var b = s.Box; return new RectangleF(b.X - pad, b.Y - pad, b.Width + 2 * pad, b.Height + 2 * pad).Contains(pt); };
            if (current != null && g.Contains(current) && over(current)) return current;
            for (int i = g.Count - 1; i >= 0; i--) if (over(g[i])) return g[i];
            return null;
        }
        SessionStep hoverStep;
        SessionStep CornersShownFor() { return drag == Drag.Round || drag == Drag.Badge || drag == Drag.Text || hoverStep == null || !Doc.ShowBoxes || baseBmp == null || groups.Count == 0 || !groups[screen].Contains(hoverStep) ? null : hoverStep; }
        void SetHover(SessionStep s) { if (hoverStep == s) return; hoverStep = s; preview.Invalidate(); }
        static float Dist(PointF a, PointF b) { return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        int resizeCorner;

        // ------------------------------------------------------------ dragging boxes and numbers
        enum Drag { None, MoveBox, Resize, Round, Badge, Text, CropMove, CropSize, CropNew }
        float roundStart;
        Drag drag; PointF dragFrom; Rectangle boxStart, cropStart; PointF? badgeStart, textStart; PointF textOrigin; int cropHandle; List<StepState> pending; int startX, startY;
        bool cropMode; Rectangle cropRect;

        void PreviewDown(object s, MouseEventArgs e)
        {
            if (baseBmp != null && e.Button == MouseButtons.Right && groups.Count > 0 && !cropMode) { PreviewMenu(e.Location); return; }
            if (baseBmp == null || e.Button != MouseButtons.Left || groups.Count == 0) return;
            var pt = ToImage(e.Location); dragFrom = pt;
            if (cropMode) { StartCropDrag(pt); return; }
            var g = groups[screen]; float br = SessionDoc.BadgeRadius(baseBmp.Width, Doc.StepSize);
            SessionStep hit = null; Drag mode = Drag.None;
            var under = ShapeUnder(pt); if (under != null) { int c = CornerAt(under, pt); if (c >= 0) { hit = under; mode = Drag.Resize; resizeCorner = c; } }
            if (hit == null && current != null && g.Contains(current) && OnRoundHandle(current, pt)) { hit = current; mode = Drag.Round; }
            if (hit == null && Doc.ShowText) for (int i = g.Count - 1; i >= 0; i--) { var tb = Doc.TextBox(g[i], baseBmp.Width, baseBmp.Height); if (tb.HasValue && tb.Value.Contains(pt)) { hit = g[i]; mode = Drag.Text; textOrigin = tb.Value.Location; break; } }
            if (hit == null && Doc.ShowNumbers) for (int i = g.Count - 1; i >= 0; i--) { var c = SessionDoc.BadgeCenter(g[i], baseBmp.Width, Doc.StepSize); if (Math.Sqrt((pt.X - c.X) * (pt.X - c.X) + (pt.Y - c.Y) * (pt.Y - c.Y)) <= br) { hit = g[i]; mode = Drag.Badge; break; } }
            if (hit == null) for (int i = g.Count - 1; i >= 0; i--) { var b = g[i].Box; var rr = new RectangleF(b.X - 4, b.Y - 4, b.Width + 8, b.Height + 8); if (rr.Contains(pt)) { hit = g[i]; mode = Drag.MoveBox; break; } }
            if (hit == null) { if (zoomK > 1.001f) { panning = true; panFrom = e.Location; panStart0 = pan; preview.Cursor = Cursors.SizeAll; } return; }
            if (hit != current) { current = hit; SelectStep(hit); }
            hoverStep = hit; drag = mode; roundStart = hit.Round; boxStart = hit.Box; badgeStart = hit.BadgePos; textStart = hit.TextPos; startX = hit.X; startY = hit.Y; pending = Snapshot();
            if (mode == Drag.Badge && !hit.BadgePos.HasValue) badgeStart = SessionDoc.BadgeCenter(hit, baseBmp.Width, Doc.StepSize);
        }
        void PreviewMove(object s, MouseEventArgs e)
        {
            if (baseBmp == null || groups.Count == 0) return;
            if (panning) { pan = new PointF(panStart0.X + e.X - panFrom.X, panStart0.Y + e.Y - panFrom.Y); ClampPan(); preview.Invalidate(); return; }
            var pt = ToImage(e.Location);
            if (drag == Drag.None)
            {
                Cursor = Cursors.Default; if (cropMode) { Cursor = CropCursor(pt); return; }
                var g = groups[screen]; var under = ShapeUnder(pt); SetHover(under); int hc = under != null ? CornerAt(under, pt) : -1;
                if (hc >= 0) Cursor = hc == 0 || hc == 3 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                else if (current != null && g.Contains(current) && OnRoundHandle(current, pt)) Cursor = Cursors.SizeWE;
                else foreach (var st in g) if (st.Box.Contains((int)pt.X, (int)pt.Y)) { Cursor = Cursors.SizeAll; break; }
                if (Doc.ShowText) foreach (var st in g) { var tb = Doc.TextBox(st, baseBmp.Width, baseBmp.Height); if (tb.HasValue && tb.Value.Contains(pt)) { Cursor = Cursors.SizeAll; break; } }
                return;
            }
            float dx = pt.X - dragFrom.X, dy = pt.Y - dragFrom.Y;
            if (drag == Drag.CropMove || drag == Drag.CropSize || drag == Drag.CropNew) { DragCrop(pt); preview.Invalidate(); return; }
            var cur = current; if (cur == null) return;
            if (drag == Drag.Round)
            {
                float m = Math.Min(cur.Box.Width, cur.Box.Height);
                cur.Round = m <= 0 ? -1f : Math.Max(0f, Math.Min(m / 2f, cur.Box.Right - pt.X)) / m; preview.Invalidate(); return;
            }
            if (drag == Drag.MoveBox)
            {
                int nx = (int)Math.Round(Math.Max(0, Math.Min(baseBmp.Width - boxStart.Width, boxStart.X + dx))), ny = (int)Math.Round(Math.Max(0, Math.Min(baseBmp.Height - boxStart.Height, boxStart.Y + dy)));
                cur.Box = new Rectangle(nx, ny, boxStart.Width, boxStart.Height);
                if (cur.Kind == "scroll") { cur.X = startX + (nx - boxStart.X); cur.Y = startY + (ny - boxStart.Y); }
                if (badgeStart.HasValue) cur.BadgePos = new PointF(badgeStart.Value.X + (nx - boxStart.X), badgeStart.Value.Y + (ny - boxStart.Y));    // the number travels with its box
                if (textStart.HasValue) cur.TextPos = new PointF(textStart.Value.X + (nx - boxStart.X), textStart.Value.Y + (ny - boxStart.Y));          // and so does a text that was moved
            }
            else if (drag == Drag.Resize)
            {   // the corner being dragged moves; the opposite corner stays where it was
                int l = boxStart.X, t = boxStart.Y, r = boxStart.Right, b = boxStart.Bottom;
                if (resizeCorner % 2 == 0) l = (int)Math.Round(Math.Max(0, Math.Min(r - 8, boxStart.X + dx))); else r = (int)Math.Round(Math.Max(l + 8, Math.Min(baseBmp.Width, boxStart.Right + dx)));
                if (resizeCorner < 2) t = (int)Math.Round(Math.Max(0, Math.Min(b - 8, boxStart.Y + dy))); else b = (int)Math.Round(Math.Max(t + 8, Math.Min(baseBmp.Height, boxStart.Bottom + dy)));
                cur.Box = Rectangle.FromLTRB(l, t, r, b);
            }
            else if (drag == Drag.Text) cur.TextPos = new PointF(textOrigin.X + dx, textOrigin.Y + dy);
            else if (drag == Drag.Badge) cur.BadgePos = new PointF(Math.Max(0, Math.Min(baseBmp.Width, badgeStart.Value.X + dx)), Math.Max(0, Math.Min(baseBmp.Height, badgeStart.Value.Y + dy)));
            cur.BoxEdited = true; preview.Invalidate();
        }
        void PreviewUp(object s, MouseEventArgs e)
        {
            if (panning) { panning = false; preview.Cursor = Cursors.Default; return; }
            if (drag == Drag.None) return;
            var d = drag; drag = Drag.None;
            if (d == Drag.CropMove || d == Drag.CropSize || d == Drag.CropNew) { if (cropRect.Width < 40 || cropRect.Height < 40) cropRect = cropStart; preview.Invalidate(); return; }
            var cur = current; if (cur != null && (cur.Box != boxStart || cur.BadgePos != badgeStart || cur.TextPos != textStart || cur.Round != roundStart) && pending != null) { Push(pending); Doc.Dirty = true; }
            pending = null; UpdateTools(); list.Invalidate();
        }

        // ------------------------------------------------------------ cropping
        void StartCrop()
        {
            if (baseBmp == null) { Say("This screen has no picture."); return; }
            var first = groups[screen][0]; var full = new Rectangle(0, 0, baseBmp.Width, baseBmp.Height);
            cropRect = !first.Crop.IsEmpty && full.Contains(first.Crop) ? first.Crop : new Rectangle((int)(full.Width * 0.05), (int)(full.Height * 0.05), (int)(full.Width * 0.9), (int)(full.Height * 0.9));
            cropStart = cropRect; cropMode = true; UpdateTools(); preview.Invalidate();
        }
        void ApplyCrop()
        {
            if (!cropMode) return;
            var first = groups[screen][0]; Push(); var full = new Rectangle(0, 0, baseBmp.Width, baseBmp.Height);
            bool whole = cropRect.X <= 2 && cropRect.Y <= 2 && cropRect.Width >= full.Width - 4 && cropRect.Height >= full.Height - 4;
            first.Crop = whole ? Rectangle.Empty : cropRect; Doc.Dirty = true; cropMode = false; UpdateTools(); preview.Invalidate();
        }
        void CancelCrop() { cropMode = false; UpdateTools(); preview.Invalidate(); }
        void RemoveCrop()
        {
            if (cropMode) { cropRect = new Rectangle(0, 0, baseBmp.Width, baseBmp.Height); return; }
            var first = groups[screen][0]; if (first.Crop.IsEmpty) return;
            Push(); first.Crop = Rectangle.Empty; Doc.Dirty = true; UpdateTools(); preview.Invalidate();
        }
        Rectangle[] Handles(float tol)
        {
            var c = cropRect; int mx = c.X + c.Width / 2, my = c.Y + c.Height / 2; int t = (int)tol;
            return new[] { new Rectangle(c.X - t, c.Y - t, 2 * t, 2 * t), new Rectangle(mx - t, c.Y - t, 2 * t, 2 * t), new Rectangle(c.Right - t, c.Y - t, 2 * t, 2 * t), new Rectangle(c.Right - t, my - t, 2 * t, 2 * t),
                new Rectangle(c.Right - t, c.Bottom - t, 2 * t, 2 * t), new Rectangle(mx - t, c.Bottom - t, 2 * t, 2 * t), new Rectangle(c.X - t, c.Bottom - t, 2 * t, 2 * t), new Rectangle(c.X - t, my - t, 2 * t, 2 * t) };
        }
        float Tol() { var v = View(); return 9f / Math.Max(0.05f, Fit(v)); }
        int HandleAt(PointF pt) { var hs = Handles(Tol()); for (int i = 0; i < hs.Length; i++) if (hs[i].Contains((int)pt.X, (int)pt.Y)) return i; return -1; }
        Cursor CropCursor(PointF pt)
        {
            int h = HandleAt(pt); if (h >= 0) return (h == 0 || h == 4) ? Cursors.SizeNWSE : (h == 2 || h == 6) ? Cursors.SizeNESW : (h == 1 || h == 5) ? Cursors.SizeNS : Cursors.SizeWE;
            return cropRect.Contains((int)pt.X, (int)pt.Y) ? Cursors.SizeAll : Cursors.Cross;
        }
        void StartCropDrag(PointF pt)
        {
            cropStart = cropRect; cropHandle = HandleAt(pt);
            if (cropHandle >= 0) drag = Drag.CropSize; else if (cropRect.Contains((int)pt.X, (int)pt.Y)) drag = Drag.CropMove; else { drag = Drag.CropNew; cropRect = new Rectangle((int)pt.X, (int)pt.Y, 0, 0); }
        }
        void DragCrop(PointF pt)
        {
            int W = baseBmp.Width, H = baseBmp.Height; float dx = pt.X - dragFrom.X, dy = pt.Y - dragFrom.Y; var c = cropStart;
            if (drag == Drag.CropMove)
                cropRect = new Rectangle((int)Math.Max(0, Math.Min(W - c.Width, c.X + dx)), (int)Math.Max(0, Math.Min(H - c.Height, c.Y + dy)), c.Width, c.Height);
            else if (drag == Drag.CropNew)
            {
                int x0 = (int)Math.Max(0, Math.Min(W, dragFrom.X)), y0 = (int)Math.Max(0, Math.Min(H, dragFrom.Y)), x1 = (int)Math.Max(0, Math.Min(W, pt.X)), y1 = (int)Math.Max(0, Math.Min(H, pt.Y));
                cropRect = Rectangle.FromLTRB(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
            }
            else
            {
                int l = c.Left, t = c.Top, r = c.Right, b = c.Bottom, h = cropHandle;
                if (h == 0 || h == 6 || h == 7) l = (int)Math.Max(0, Math.Min(r - 40, c.Left + dx));
                if (h == 2 || h == 3 || h == 4) r = (int)Math.Min(W, Math.Max(l + 40, c.Right + dx));
                if (h == 0 || h == 1 || h == 2) t = (int)Math.Max(0, Math.Min(b - 40, c.Top + dy));
                if (h == 4 || h == 5 || h == 6) b = (int)Math.Min(H, Math.Max(t + 40, c.Bottom + dy));
                cropRect = Rectangle.FromLTRB(l, t, r, b);
            }
        }
        void PaintCrop(Graphics g, Rectangle v, float k, Rectangle r)
        {
            Func<Rectangle, Rectangle> toScreen = delegate(Rectangle c) { return new Rectangle(r.X + (int)(c.X * k), r.Y + (int)(c.Y * k), (int)(c.Width * k), (int)(c.Height * k)); };
            var cr = toScreen(cropRect);
            using (var dim = new SolidBrush(Color.FromArgb(150, Theme.Ink)))
            {
                g.FillRectangle(dim, r.X, r.Y, r.Width, cr.Y - r.Y); g.FillRectangle(dim, r.X, cr.Bottom, r.Width, r.Bottom - cr.Bottom);
                g.FillRectangle(dim, r.X, cr.Y, cr.X - r.X, cr.Height); g.FillRectangle(dim, cr.Right, cr.Y, r.Right - cr.Right, cr.Height);
            }
            using (var p = new Pen(Color.White, 2f)) g.DrawRectangle(p, cr);
            foreach (var h in Handles(0f)) { var hs = toScreen(new Rectangle(h.X, h.Y, 0, 0)); var sq = new Rectangle(hs.X - Dpi.S(4), hs.Y - Dpi.S(4), Dpi.S(8), Dpi.S(8)); g.FillRectangle(Brushes.White, sq); g.DrawRectangle(Pens.Black, sq); }
            string t = cropRect.Width + " x " + cropRect.Height;
            using (var f = Theme.BodyBold(9f)) { var sz = g.MeasureString(t, f); var lr = new RectangleF(cr.X, Math.Max(r.Y, cr.Y - sz.Height - 4), sz.Width + 10, sz.Height + 2); g.FillRectangle(new SolidBrush(Theme.Ink), lr); g.DrawString(t, f, Brushes.White, lr.X + 5, lr.Y + 1); }
        }

        // ------------------------------------------------------------ screens and steps
        // the editor's Copy button: this screen, with its boxes, numbers and text, on the clipboard
        public void CopyCurrentScreen() { CopyScreenshot(); }
        void MergeWithPrevious()
        {
            if (groups.Count == 0) return;
            if (screen == 0) { Say("This is the first screen: there is nothing before it to merge with."); return; }
            var first = groups[screen][0]; var prev = groups[screen - 1][0];
            if (first.Task != prev.Task) { Say("These screens belong to different tasks. Screens can only be merged within a task."); return; }
            Push(); first.BreakBefore = false; first.CustomFrame = null; first.FrameFrom = ""; first.Crop = Rectangle.Empty; Doc.Dirty = true;
            var keep = current; current = null; Rebuild(screen - 1); if (keep != null) SelectStep(keep);
        }
        void NewScreenshotFromHere()
        {
            var s = Selected; if (s == null) { Say("Select the step the new screenshot should start from."); return; }
            int gi = GroupOf(s); var g = groups[gi];
            if (g[0] == s) { Say("This step already starts its own screenshot."); return; }
            Push();
            if (s.Frame == null) s.Frame = SessionDoc.PictureOf(g);      // a session recorded before each step kept its own picture: start from the same one
            s.BreakBefore = true; s.CustomFrame = null; s.FrameFrom = ""; s.Crop = Rectangle.Empty; Doc.Dirty = true;
            Rebuild(gi + 1); SelectStep(s);
        }
        void AddStep() { AddStepCore(true); }
        internal SessionStep AddStepCore(bool prompt)
        {
            if (groups.Count == 0 || baseBmp == null) { Say("Add a step to a screen that has a picture."); return null; }
            var g = groups[screen]; var first = g[0]; var after = current != null && g.Contains(current) ? current : g[g.Count - 1];
            var v = View(); int w = Math.Max(24, (int)(v.Width * 0.09)), h = Math.Max(18, (int)(v.Height * 0.055)); int cx = v.X + v.Width / 2, cy = v.Y + v.Height / 2;
            var s = new SessionStep { Kind = "click", Manual = true, Role = "step", BoxEdited = true, Ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds, X = cx, Y = cy,
                Box = new Rectangle(cx - w / 2, cy - h / 2, w, h), Vw = first.Vw, Vh = first.Vh, Url = first.Url, Title = first.Title, Task = first.Task, ActionCode = after.ActionCode, UrlBar = first.UrlBar };
            Push(); int i = Doc.Steps.IndexOf(after) + 1; Doc.Steps.Insert(i, s); s.Id = Doc.Id + "_m" + DateTime.Now.Ticks; Doc.Renumber(); Doc.Dirty = true;
            Rebuild(screen); SelectStep(s); if (prompt) EditText();
            return s;
        }
        void PasteScreenshot()
        {
            if (groups.Count == 0) return;
            Bitmap bmp = null;
            try { if (Clipboard.ContainsImage()) { var im = Clipboard.GetImage(); if (im != null) bmp = new Bitmap(im); } } catch { }
            if (bmp == null) { Say("There is no picture on the clipboard."); return; }
            using (bmp) UseCustom(SessionDoc.Encode(bmp));
        }
        void UseCustom(byte[] png)
        {
            var first = groups[screen][0]; Push(); first.CustomFrame = png; first.Crop = Rectangle.Empty; first.FrameFrom = ""; Doc.Dirty = true; ShowScreen(screen);
        }
        void PreviewDrop(object s, DragEventArgs e)
        {
            if (groups.Count == 0) return;
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var f = ((string[])e.Data.GetData(DataFormats.FileDrop)).FirstOrDefault(x => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(Path.GetExtension(x).ToLowerInvariant()));
                    if (f == null) { Say("That is not a picture."); return; }
                    using (var im = Image.FromFile(f)) using (var bmp = new Bitmap(im)) UseCustom(SessionDoc.Encode(bmp));
                }
                else if (e.Data.GetDataPresent(DataFormats.Bitmap)) { using (var bmp = new Bitmap((Image)e.Data.GetData(DataFormats.Bitmap))) UseCustom(SessionDoc.Encode(bmp)); }
            }
            catch (Exception ex) { Say("Could not use that picture: " + ex.Message); }
        }
        void UseOriginal()
        {
            var first = groups[screen][0]; if (first.CustomFrame == null) return;
            Push(); first.CustomFrame = null; first.Crop = Rectangle.Empty; Doc.Dirty = true; ShowScreen(screen); UpdateTools();
        }
        void DeleteScreen()
        {
            if (groups.Count == 0) return;
            var g = groups[screen]; Push();
            foreach (var s in g.ToList()) Doc.Steps.Remove(s);
            Doc.Renumber(); Doc.Dirty = true; current = null; baseRef = null;
            Rebuild(Math.Min(screen, Math.Max(0, Doc.Groups().Count - 1))); Say("Deleted the screenshot and its " + g.Count + (g.Count == 1 ? " step" : " steps") + ". Undo brings it back.");
        }
        // ------------------------------------------------------------ copies, menus and looks
        // a picture for the clipboard: a plain bitmap (anything transparent shown on white, for programs that ignore transparency) and a PNG that keeps it
        internal static DataObject PictureData(Bitmap rendered)
        {
            var d = new DataObject();
            using (var flat = new Bitmap(rendered.Width, rendered.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(rendered, 0, 0, rendered.Width, rendered.Height); }
                d.SetData(DataFormats.Bitmap, true, new Bitmap(flat));
            }
            var ms = new MemoryStream(); rendered.Save(ms, ImageFormat.Png); d.SetData("PNG", false, ms);
            return d;
        }
        void CopyScreenshot()
        {
            if (groups.Count == 0) return;
            try
            {
                using (var b = Doc.RenderGroup(groups[screen], null))
                {
                    if (b == null) { Say("This screen has no picture to copy."); return; }
                    Clipboard.SetDataObject(PictureData(b), true);
                }
                Say("Copied screen " + (screen + 1) + " with its boxes, numbers and text. Paste it into another app.");
            }
            catch (Exception ex) { Say("Could not copy the screenshot: " + ex.Message); }
        }
        void DuplicateScreen()
        {
            if (groups.Count == 0) return;
            var g = groups[screen]; Push();
            int at = Doc.Steps.IndexOf(g[g.Count - 1]) + 1; var copies = new List<SessionStep>(); long stamp = DateTime.Now.Ticks;
            foreach (var s in g)
            {
                var c = s.Clone(); c.Id = Doc.Id + "_d" + stamp + "_" + copies.Count; c.Decor = true; c.Param = ""; c.Typed = false; c.Expect = ""; c.BreakBefore = copies.Count == 0; copies.Add(c);
            }
            var first = copies[0]; first.Frame = SessionDoc.PictureOf(g); first.AfterFrame = null; first.FrameFrom = ""; first.CustomFrame = g[0].CustomFrame; first.Crop = g[0].Crop;
            Doc.Steps.InsertRange(at, copies); Doc.Renumber(); Doc.Dirty = true; current = null; baseRef = null;
            Rebuild(screen + 1); SelectGroupRow(screen); Say("Copied the screenshot and its " + g.Count + (g.Count == 1 ? " step" : " steps") + ". The copy is only shown, a replay does not act on it. Undo removes it.");
        }
        ContextMenuStrip StepMenu()
        {
            var m = NewMenu();
            var ns = MenuItem(m, "New screenshot from here", "Start a new screen at this step, with the picture taken at that step", NewScreenshotFromHere);
            ns.Enabled = current != null && groups.Count > 0 && groups[GroupOf(current)][0] != current;
            var mp = MenuItem(m, "Merge with previous", "Use the previous screen's screenshot for this step's screen too", MergeWithPrevious); mp.Enabled = current != null && groups.Count > 0 && GroupOf(current) > 0;
            if (current != null && current.Round >= 0 && Doc.BoxShape == "rounded") MenuItem(m, "Reset box corners", "Go back to the automatic roundness for this box", delegate { Push(); current.Round = -1; Doc.Dirty = true; preview.Invalidate(); });
            m.Items.Add(new ToolStripSeparator());
            MenuItem(m, "Copy screenshot", "Copy this screen with its boxes, numbers and text, to paste into another app", CopyScreenshot);
            MenuItem(m, "Duplicate screenshot", "Make a copy of this screen right after it", DuplicateScreen);
            if (current != null && current.TextPos.HasValue) MenuItem(m, "Put the text back beside its number", "Undo moving this step's text", delegate { Push(); current.TextPos = null; Doc.Dirty = true; preview.Invalidate(); });
            m.Items.Add(new ToolStripSeparator());
            MenuItem(m, "Delete step", "Remove this step (Del)", DeleteStep);
            return m;
        }
        ContextMenuStrip ScreenMenu()
        {
            var m = NewMenu();
            MenuItem(m, "Copy screenshot", "Copy this screen with its boxes, numbers and text, to paste into another app", CopyScreenshot);
            MenuItem(m, "Duplicate screenshot", "Make a copy of this screen right after it", DuplicateScreen);
            var merge = MenuItem(m, "Merge with previous", "Use the previous screen's screenshot for this screen's steps too", MergeWithPrevious); merge.Enabled = screen > 0;
            m.Items.Add(new ToolStripSeparator());
            MenuItem(m, "Delete screenshot", "Delete this screen and all its steps (Undo brings it back)", DeleteScreen);
            return m;
        }
        void PreviewMenu(Point at)
        {
            var pt = ToImage(at); var g = groups[screen]; float br = SessionDoc.BadgeRadius(baseBmp.Width, Doc.StepSize); SessionStep hit = null;
            for (int i = g.Count - 1; i >= 0 && hit == null && Doc.ShowNumbers; i--) { var c = SessionDoc.BadgeCenter(g[i], baseBmp.Width, Doc.StepSize); if (Math.Sqrt((pt.X - c.X) * (pt.X - c.X) + (pt.Y - c.Y) * (pt.Y - c.Y)) <= br) hit = g[i]; }
            for (int i = g.Count - 1; i >= 0 && hit == null; i--) { var b = g[i].Box; if (new RectangleF(b.X - 4, b.Y - 4, b.Width + 8, b.Height + 8).Contains(pt)) hit = g[i]; }
            if (hit != null) { current = hit; SelectStep(hit); StepMenu().Show(preview, at); } else ScreenMenu().Show(preview, at);
        }
        void ShareSession()
        {
            if (Doc == null || Doc.Steps.Count == 0) { Say("There is nothing to share yet."); return; }
            Directory.CreateDirectory(Settings.Folder);
            using (var d = new SaveFileDialog { Title = "Share session", Filter = "XLCapture shared session|*" + SessionDoc.ShareExt, DefaultExt = "xlsession", AddExtension = true, InitialDirectory = Settings.Folder,
                FileName = string.Join("_", (Doc.Name ?? "session").Split(Path.GetInvalidFileNameChars())) })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { Doc.SaveShared(d.FileName); Say("Saved " + Path.GetFileName(d.FileName) + ". Send it to a colleague: other programs cannot read it and XLCapture opens it (File > Open). It is a lock against casual use, not strong security: do not rely on it for secrets."); }
                catch (Exception ex) { Say("Could not save the shared session: " + ex.Message); }
            }
        }
        void ShowLook()
        {
            var before = Doc.GetLook();
            using (var f = new SessionLookForm(Doc, delegate { Doc.Dirty = true; list.Invalidate(); preview.Invalidate(); }))
                if (f.ShowDialog(FindForm()) != DialogResult.OK) { Doc.SetLook(before); list.Invalidate(); preview.Invalidate(); }
        }
        void PickColour(bool box)
        {
            Color c; if (!ColorPickerDialog.Pick(FindForm(), box ? Doc.BoxColor : Doc.StepColor, box ? "Box colour" : "Number colour", out c)) return;
            if (box) Doc.BoxColor = c; else Doc.StepColor = c; Doc.Dirty = true; list.Invalidate(); preview.Invalidate();
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var row = (Row)list.Items[e.Index]; var g = e.Graphics; bool sel = (e.State & DrawItemState.Selected) != 0;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Backdrop(g, sel ? Theme.CobaltTint : Theme.Sheet, e.Bounds);
            if (row.Step == null)
            {
                var first = groups[row.Group][0];
                // a session made from a procedure reads task by task; one recorded by hand reads screen by screen
                bool sameTask = first.Task.Length > 0 && row.Group > 0 && groups[row.Group - 1][0].Task == first.Task;
                string t = first.Task.Length > 0 ? first.Task + (sameTask ? "   (continued)" : "") : "Screen " + (row.Group + 1) + (first.Title.Length > 0 ? "  ·  " + first.Title : "");
                using (var f = Theme.Label(10f)) g.DrawString(t, f, new SolidBrush(Theme.InkSoft), new RectangleF(e.Bounds.X + Dpi.S(14), e.Bounds.Y + Dpi.S(16), e.Bounds.Width - Dpi.S(20), Dpi.S(22)), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                using (var p = new Pen(Theme.Rule)) g.DrawLine(p, e.Bounds.X + Dpi.S(14), e.Bounds.Bottom - 1, e.Bounds.Right - Dpi.S(14), e.Bounds.Bottom - 1);
                return;
            }
            var st = row.Step; float r = Dpi.F(13); var c = new PointF(e.Bounds.X + Dpi.S(34), e.Bounds.Y + e.Bounds.Height / 2f);
            using (var b = new SolidBrush(Doc.StepColor)) g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);
            using (var f = Theme.Label(9f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(st.Seq.ToString(), f, Brushes.White, new RectangleF(c.X - r, c.Y - r, r * 2, r * 2), sf);
            using (var f = Theme.Body(10f)) g.DrawString(st.Plain, f, new SolidBrush(st.Manual && string.IsNullOrWhiteSpace(st.StepText) ? Theme.Mist : Theme.Ink), new RectangleF(e.Bounds.X + Dpi.S(58), e.Bounds.Y + Dpi.S(5), e.Bounds.Width - Dpi.S(70), Dpi.S(20)), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            string sub = st.Manual ? "Added by you" : (st.Kind == "scroll" ? "Scroll" : st.Keyboard ? "Keyboard" : "Click") + (st.Typed ? " and typing" : "") + (st.Role.Length > 0 && st.Kind != "scroll" ? "  ·  " + st.Role : "") + (st.StepTextEdited ? "  ·  edited" : "");
            using (var f = Theme.Body(8.5f)) g.DrawString(sub, f, new SolidBrush(Theme.Slate), e.Bounds.X + Dpi.S(58), e.Bounds.Y + Dpi.S(25));
        }

        // ------------------------------------------------------------ editing
        SessionStep Selected { get { var r = list.SelectedItem as Row; return r == null ? null : r.Step; } }
        // the step's instruction is edited right in the list: a text box over its row (Enter or clicking away keeps it, Esc leaves it as it was)
        void EditText()
        {
            var s = Selected; if (s == null) { Say("Select a step first."); return; }
            BeginInline(list.SelectedIndex, s);
        }
        void BeginInline(int idx, SessionStep s)
        {
            EndInline(true); if (idx < 0) return;
            var b = list.GetItemRectangle(idx);
            var box = new TextBox
            {
                Font = Theme.Body(10f), BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Step " + s.Seq + " instruction", Text = s.Manual && string.IsNullOrWhiteSpace(s.StepText) ? "" : s.Text,
                Bounds = new Rectangle(b.X + Dpi.S(54), b.Y + (b.Height - Dpi.S(28)) / 2, Math.Max(Dpi.S(80), b.Width - Dpi.S(54) - Dpi.S(10)), Dpi.S(28))
            };
            inline = box; inlineFor = s;
            box.KeyDown += delegate(object x, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EndInline(true); list.Focus(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndInline(false); list.Focus(); }
            };
            box.LostFocus += delegate { EndInline(true); };
            list.Controls.Add(box); box.Focus(); box.SelectAll();
        }
        void EndInline(bool commit)
        {
            var box = inline; var s = inlineFor; if (box == null) return;
            inline = null; inlineFor = null;
            string text = box.Text.Trim(); list.Controls.Remove(box); box.Dispose();
            if (commit && s != null && Doc != null && Doc.Steps.Contains(s))
            {
                string old = s.Manual && string.IsNullOrWhiteSpace(s.StepText) ? "" : s.Text;
                if (text != (old ?? "").Trim()) { Push(); s.StepText = text; s.StepTextEdited = text.Length > 0; Doc.Dirty = true; }
            }
            list.Invalidate(); if (preview != null) preview.Invalidate();
        }
        void DeleteStep()
        {
            var s = Selected; if (s == null) { Say("Select a step first."); return; }
            int i = Doc.Steps.IndexOf(s); Push();
            // the first step of a screen holds that screen's settings: hand them to the next step on the same screen
            if (s.BreakBefore && i + 1 < Doc.Steps.Count && !Doc.Steps[i + 1].BreakBefore)
            {
                var n = Doc.Steps[i + 1]; n.BreakBefore = true; if (n.Frame == null) n.Frame = s.Frame; n.CustomFrame = s.CustomFrame; n.FrameFrom = s.FrameFrom; n.Crop = s.Crop;
            }
            Doc.Steps.RemoveAt(i); Doc.Renumber(); Doc.Dirty = true; current = null; baseRef = null;
            Rebuild(screen);
            if (Doc.Steps.Count > 0) SelectStep(Doc.Steps[Math.Min(i, Doc.Steps.Count - 1)]);
        }
        void Move(int d)
        {
            var s = Selected; if (s == null) { Say("Select a step first."); return; }
            int i = Doc.Steps.IndexOf(s), j = i + d; if (j < 0 || j >= Doc.Steps.Count) return;
            // steps move within their own screen only: the boxes belong to that screen's picture
            if (GroupOf(Doc.Steps[j]) != GroupOf(s)) { Say("A step can only move within its own screen."); return; }
            Push();
            if (s.BreakBefore || Doc.Steps[j].BreakBefore)
            {   // the screen's settings stay with whichever step is now first
                var a = Doc.Steps[Math.Min(i, j)]; var b = Doc.Steps[Math.Max(i, j)];
                b.BreakBefore = true; a.BreakBefore = false;
                var cf = a.CustomFrame; a.CustomFrame = b.CustomFrame; b.CustomFrame = cf; var ff = a.FrameFrom; a.FrameFrom = b.FrameFrom; b.FrameFrom = ff; var cr = a.Crop; a.Crop = b.Crop; b.Crop = cr;
                if (b.Frame == null) b.Frame = a.Frame; if (a.Frame == null) a.Frame = b.Frame;
            }
            Doc.Steps[i] = Doc.Steps[j]; Doc.Steps[j] = s; Doc.Renumber(); Doc.Dirty = true; baseRef = null;
            Rebuild(screen); SelectStep(s);
        }
        void Say(string m) { if (Message != null) Message(m); }

        DateTime nudgeAt; SessionStep nudged;
        bool NudgeStep(Keys keyData)
        {
            int step = (keyData & Keys.Shift) != 0 ? 10 : 1, dx = 0, dy = 0;
            switch (keyData & ~Keys.Shift) { case Keys.Left: dx = -step; break; case Keys.Right: dx = step; break; case Keys.Up: dy = -step; break; case Keys.Down: dy = step; break; default: return false; }
            var s = Selected; if (s == null || !groups[screen].Contains(s)) return false;
            var b = s.Box; int nx = Math.Max(0, Math.Min(baseBmp.Width - b.Width, b.X + dx)), ny = Math.Max(0, Math.Min(baseBmp.Height - b.Height, b.Y + dy));
            int mx = nx - b.X, my = ny - b.Y; if (mx == 0 && my == 0) return true;
            // a run of key presses is one change to undo
            if (nudged != s || (DateTime.UtcNow - nudgeAt).TotalMilliseconds > 900) Push();
            nudged = s; nudgeAt = DateTime.UtcNow;
            s.Box = new Rectangle(nx, ny, b.Width, b.Height);
            if (s.Kind == "scroll") { s.X += mx; s.Y += my; }
            if (s.BadgePos.HasValue) s.BadgePos = new PointF(s.BadgePos.Value.X + mx, s.BadgePos.Value.Y + my);
            if (s.TextPos.HasValue) s.TextPos = new PointF(s.TextPos.Value.X + mx, s.TextPos.Value.Y + my);
            s.BoxEdited = true; Doc.Dirty = true; preview.Invalidate(); list.Invalidate(); return true;
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (cropMode && keyData == Keys.Enter) { ApplyCrop(); return true; }
            if (cropMode && keyData == Keys.Escape) { CancelCrop(); return true; }
            if (keyData == (Keys.Control | Keys.Z) && !(ActiveControl is TextBox)) { Undo(); return true; }
            if ((keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) && !(ActiveControl is TextBox)) { Redo(); return true; }
            if (!cropMode && Doc != null && ActiveControl == preview && Selected != null && baseBmp != null && NudgeStep(keyData)) return true;      // an arrow key moves the selected box a pixel (Shift: ten)
            if (keyData == Keys.Delete && !cropMode && Doc != null && (ActiveControl == preview || ActiveControl == list) && Selected != null) { DeleteStep(); return true; }      // the picture holds the focus while the pointer is over it, so the key is caught here, not by the list
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public bool Save()
        {
            if (Doc == null) return false;
            if (cropMode) { Say("Apply or cancel the crop first."); return false; }
            try
            {
                string dir = Doc.Path != null ? System.IO.Path.GetDirectoryName(Doc.Path) : Settings.Folder;
                Doc.Save(Doc.DefaultPath(dir));
                Say("Saved " + System.IO.Path.GetFileName(Doc.Path));
                if (Saved != null) Saved(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Could not save the session: " + ex.Message, "Save"); return false; }
        }

        void EditScreen()
        {
            if (groups.Count == 0) return;
            if (SessionDoc.PictureOf(groups[screen]) == null) { Say("This screen has no picture."); return; }
            if (OpenInEditor != null) OpenInEditor(Doc.ScreenToDoc(groups[screen], screen + 1));
        }
        void ExportGuide()
        {
            if (Doc == null || Doc.Steps.Count == 0) return;
            using (var d = new FolderBrowserDialog { Description = "Choose where to put the guide folder (clicks.json and the screenshots)", SelectedPath = Settings.Folder })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { Say("Saved " + GuideExport.Write(Doc, d.SelectedPath) + ". Ask Claude to build the guide from this folder."); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Could not export: " + ex.Message, "Guide folder"); }
            }
        }
        // a folder with slides.json and one picture per screen, plus the slide template when there is one, and the sentence to give Claude
        void ExportSlidePack()
        {
            if (Doc == null || Doc.Steps.Count == 0) return;
            string template, existing;
            using (var o = new SlidePackOptionsForm(Settings.SlideTemplate, Settings.SlideExisting))
            {
                if (o.ShowDialog(FindForm()) != DialogResult.OK) return;
                template = o.Template; existing = o.ExistingChosen;
                if (o.Template.Length > 0) Settings.SlideTemplate = o.Template;
                if (o.Existing.Length > 0) Settings.SlideExisting = o.Existing;
                Settings.Save();
            }
            using (var d = new FolderBrowserDialog { Description = "Choose where to put the slide pack folder", SelectedPath = Settings.Folder })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    string dir = SlidePack.Write(Doc, d.SelectedPath, template, existing);
                    string act = Doc.Procedure != null && Doc.Procedure.ContainsKey("activity") && Doc.Procedure["activity"] is Dictionary<string, object> ? Convert.ToString(((Dictionary<string, object>)Doc.Procedure["activity"])["title"]) : Doc.Name;
                    string prompt = SlidePack.Prompt(dir, act, existing); bool copied = true;
                    try { Clipboard.SetText(prompt); } catch { copied = false; }
                    SkillInstaller.EnsureInstalled();
                    using (var done = new SlidePackDoneForm(dir, prompt, copied)) done.ShowDialog(FindForm());
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Could not export: " + ex.Message, "Slide pack"); }
            }
        }
        void ExportPictures()
        {
            if (groups.Count == 0) return;
            using (var d = new FolderBrowserDialog { Description = "Choose a folder for the pictures", SelectedPath = Settings.Folder })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                int n = 0;
                for (int i = 0; i < groups.Count; i++)
                    using (var b = Doc.RenderGroup(groups[i], null)) { if (b == null) continue; b.Save(System.IO.Path.Combine(d.SelectedPath, Doc.Name.Replace(":", "") + " - " + (i + 1).ToString("00") + ".png"), ImageFormat.Png); n++; }
                Say("Saved " + n + " pictures to " + d.SelectedPath);
            }
        }
        // a printable guide: each page is a marked-up screen with its numbered instructions underneath
        void ExportPdf()
        {
            if (groups.Count == 0) return;
            using (var d = new SaveFileDialog { Filter = "PDF document|*.pdf", FileName = Doc.Name, InitialDirectory = Settings.Folder })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                var pages = new List<Bitmap>();
                try
                {
                    for (int i = 0; i < groups.Count; i++)
                    {
                        using (var shot = Doc.RenderGroup(groups[i], null))
                        {
                            if (shot == null) continue;
                            int W = Math.Max(1000, shot.Width), pad = W / 30, lineH = W / 46;
                            float k = (W - pad * 2f) / shot.Width; int sh = (int)(shot.Height * k);
                            int H = pad + lineH * 2 + sh + pad + groups[i].Count * lineH * 3 / 2 + pad;
                            var page = new Bitmap(W, H); pages.Add(page);
                            using (var g = Graphics.FromImage(page))
                            {
                                g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                string heading = groups[i][0].Task.Length > 0 ? groups[i][0].Task : "screen " + (i + 1);
                                using (var f = new Font(Theme.LabelFamily, lineH * 1.1f, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString(Doc.Name + "  —  " + heading, f, new SolidBrush(Theme.Ink), pad, pad);
                                g.DrawImage(shot, pad, pad + lineH * 2, W - pad * 2, sh);
                                using (var pen = new Pen(Theme.Rule, 2)) g.DrawRectangle(pen, pad, pad + lineH * 2, W - pad * 2, sh);
                                int y = pad + lineH * 2 + sh + pad;
                                using (var f = new Font(Theme.BodyFamily, lineH * 0.85f, GraphicsUnit.Pixel))
                                    foreach (var s in groups[i])
                                    {
                                        float r = lineH * 0.6f;
                                        using (var b = new SolidBrush(Doc.StepColor)) g.FillEllipse(b, pad, y, r * 2, r * 2);
                                        using (var nf = new Font(Theme.LabelFamily, r, FontStyle.Bold, GraphicsUnit.Pixel)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                            g.DrawString(s.Seq.ToString(), nf, Brushes.White, new RectangleF(pad, y, r * 2, r * 2), sf);
                                        g.DrawString(s.Plain, f, new SolidBrush(Theme.Ink), pad + r * 2 + lineH / 2, y + r - f.Height / 2f);
                                        y += lineH * 3 / 2;
                                    }
                            }
                        }
                    }
                    Pdf.Write(d.FileName, pages, 88);
                    Say("Saved a " + pages.Count + "-page guide to " + d.FileName);
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Could not export: " + ex.Message, "Export PDF"); }
                finally { foreach (var p in pages) p.Dispose(); }
            }
        }
        protected override void Dispose(bool disposing) { if (disposing && baseBmp != null) baseBmp.Dispose(); base.Dispose(disposing); }
    }
}

namespace SnapLite
{
    // a panel that takes the keyboard focus when the pointer is over it, so the mouse wheel (and Ctrl + wheel to zoom) reaches it
    class ZoomPanel : System.Windows.Forms.Panel
    {
        public ZoomPanel() { SetStyle(System.Windows.Forms.ControlStyles.Selectable, true); TabStop = true; }
        protected override void OnMouseEnter(System.EventArgs e) { base.OnMouseEnter(e); if (!(FindForm() != null && FindForm().ActiveControl is System.Windows.Forms.TextBoxBase)) Focus(); }
    }
}
