// Capture engine: the pick overlay (region, fixed, window, element, multi-area, freehand) and the capture pipeline.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;

namespace SnapLite
{
    enum CapMode { Region, Fixed, Window, Full, Multi, Freehand, Element, Scroll, ScrollH, Text, Video, Gif, AllInOne, Session }

    class PickResult
    {
        public bool Cancel;
        public CapMode Action = CapMode.Region;        // All-in-One: what to do with the area
        public Rectangle Rect;                          // image coordinates (0,0 = virtual-screen top-left)
        public bool Dragged;                            // Session pick: the area was dragged by hand (not a window or pane that was clicked)
        public List<Rectangle> Areas = new List<Rectangle>();
        public List<Point> Poly;
    }

    class PickOverlay : Form
    {
        readonly Bitmap shot, bright, dimmed; readonly CapMode mode; readonly Point origin;
        readonly List<WinInfo> wins = new List<WinInfo>();                       // overlay coordinates, front to back
        readonly Dictionary<IntPtr, List<Rectangle>> children = new Dictionary<IntPtr, List<Rectangle>>();
        Rectangle sel, hover; Point start, cursor; bool dragging, keyDrag;
        readonly List<Rectangle> areas = new List<Rectangle>();
        List<Point> poly;
        public PickResult Result = new PickResult { Cancel = true };
        DateTime lastProbe = DateTime.MinValue;
        int fw, fh;
        bool fsHover, showHelp, loupeOn = true;

        public PickOverlay(Bitmap screen, CapMode mode)
        {
            shot = screen; this.mode = mode;
            var vs = SystemInformation.VirtualScreen; origin = vs.Location;
            fw = Settings.FixedW; fh = Settings.FixedH;
            if (Snaps)
                foreach (var w in Native.Windows()) { var r = w.Rect; r.Offset(-vs.X, -vs.Y); wins.Add(new WinInfo { Handle = w.Handle, Title = w.Title, Rect = r }); }
            // premultiplied copies (GDI+ blits these several times faster), made in one pass over the pixels
            MakeLayers(shot, out bright, out dimmed);
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = vs;
            TopMost = true; ShowInTaskbar = false; Cursor = Cursors.Cross; KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            cursor = PointToClient(Control.MousePosition);
            if (Snaps) hover = WindowAt(cursor);
        }

        // bright = the frozen screen; dimmed = it under a 49% ink veil (same as filling rgba(8,14,22,125) over it)
        static void MakeLayers(Bitmap src, out Bitmap bright, out Bitmap dimmed)
        {
            int w = src.Width, h = src.Height; var all = new Rectangle(0, 0, w, h);
            bright = new Bitmap(w, h, PixelFormat.Format32bppPArgb); dimmed = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var s = src.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var b = bright.LockBits(all, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            var d = dimmed.LockBits(all, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            const int keep = 130, vb = 8 * 125 / 255, vg = 14 * 125 / 255, vr = 22 * 125 / 255;
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    uint* sp = (uint*)((byte*)s.Scan0 + (long)y * s.Stride), bp = (uint*)((byte*)b.Scan0 + (long)y * b.Stride), dp = (uint*)((byte*)d.Scan0 + (long)y * d.Stride);
                    for (int x = 0; x < w; x++)
                    {
                        uint c = sp[x] | 0xFF000000; bp[x] = c;
                        uint cb = c & 0xFF, cg = (c >> 8) & 0xFF, cr = (c >> 16) & 0xFF;
                        dp[x] = 0xFF000000 | ((cr * keep / 255 + vr) << 16) | ((cg * keep / 255 + vg) << 8) | (cb * keep / 255 + vb);
                    }
                }
            }
            src.UnlockBits(s); bright.UnlockBits(b); dimmed.UnlockBits(d);
        }

        // Repaint only what can change when the pointer moves: the monitors under the old and new pointer,
        // the crosshair strips and the selection / hover boxes. A full repaint of 3 x 4K takes ~110 ms.
        Region lastDirty;
        void InvalidateAround()
        {
            var now = new Region(MonitorAt(cursor));
            int pad = Dpi.S(60);
            now.Union(new Rectangle(0, cursor.Y - 2, Width, 5)); now.Union(new Rectangle(cursor.X - 2, 0, 5, Height));
            foreach (var r in new[] { sel, hover }) if (!r.IsEmpty) { var x = r; x.Inflate(pad, pad); now.Union(x); }
            if (poly != null && poly.Count > 0) { int x0 = poly.Min(p => p.X), y0 = poly.Min(p => p.Y); now.Union(Rectangle.FromLTRB(x0 - pad, y0 - pad, poly.Max(p => p.X) + pad, poly.Max(p => p.Y) + pad)); }
            if (choosing) foreach (var a in actionRects) { var x = a.Key; x.Inflate(pad, pad); now.Union(x); }
            if (lastDirty != null) { using (var both = now.Clone()) { both.Union(lastDirty); Invalidate(both); } lastDirty.Dispose(); }
            else Invalidate(now);
            lastDirty = now;
        }

        // ---- which features this mode offers
        // Smart pick, the same as when a session is recorded: the window or pane under the pointer is highlighted (a browser shows just its page), a click takes it,
        // a drag takes the area drawn, the wheel steps to a larger or smaller area and F takes the monitor. Used by the picture modes that take an area and by video and GIF.
        bool Smart { get { return mode == CapMode.Region || mode == CapMode.Scroll || mode == CapMode.ScrollH || mode == CapMode.Video || mode == CapMode.Gif; } }
        bool Snaps { get { return mode == CapMode.Window || mode == CapMode.AllInOne || mode == CapMode.Session || Smart; } }
        bool AioLike { get { return mode == CapMode.AllInOne || mode == CapMode.Session || Smart; } }
        bool FullscreenAllowed { get { return mode == CapMode.AllInOne || mode == CapMode.Session || mode == CapMode.Region || mode == CapMode.Window || mode == CapMode.Video || mode == CapMode.Gif || mode == CapMode.Text; } }
        bool RegionAllowed { get { return mode == CapMode.AllInOne || mode == CapMode.Session || mode == CapMode.Region || mode == CapMode.Video || mode == CapMode.Gif || mode == CapMode.Text || mode == CapMode.Scroll || mode == CapMode.ScrollH || mode == CapMode.Multi; } }

        // the monitor under a point, in overlay coordinates
        Rectangle MonitorAt(Point p)
        {
            var b = Screen.FromPoint(new Point(p.X + origin.X, p.Y + origin.Y)).Bounds; b.Offset(-origin.X, -origin.Y); return Clip(b);
        }
        // Fullscreen button: centred at the top of the monitor under the pointer, like Snagit's
        Rectangle FullscreenButton()
        {
            var m = MonitorAt(cursor); int w = Dpi.S(128), h = Dpi.S(34);
            return new Rectangle(m.X + (m.Width - w) / 2, m.Y + Dpi.S(16), w, h);
        }
        bool ShowFsButton { get { return FullscreenAllowed && !choosing && !dragging && !showHelp; } }

        // Window or panel under a point. In the title-bar band the whole window is picked. Elsewhere the
        // window, its child windows and the panes UI Automation reports form a chain from the whole window down
        // to the smallest area; the first sizeable pane is highlighted and the mouse wheel steps up or down the chain.
        readonly Dictionary<IntPtr, List<Rectangle>> panes = new Dictionary<IntPtr, List<Rectangle>>();   // from UI Automation, overlay coordinates
        readonly HashSet<IntPtr> paneLoading = new HashSet<IntPtr>();
        int paneLevel; IntPtr levelWin; bool levelPinned;
        // A browser window is its page: the part under the tabs and the address bar. Chrome, Edge and other Chromium programs keep the page in a child window;
        // Firefox exposes it as a document. Empty when the window is not a browser.
        readonly Dictionary<IntPtr, Rectangle> content = new Dictionary<IntPtr, Rectangle>();
        Rectangle ContentOf(WinInfo w)
        {
            Rectangle r; if (content.TryGetValue(w.Handle, out r)) return r;
            r = Rectangle.Empty;
            try
            {
                string cls = Native.ClassOf(w.Handle); Rectangle scr = Rectangle.Empty;
                if (cls == "Chrome_WidgetWin_1") scr = Native.LargestChild(w.Handle, "Chrome_RenderWidgetHostHWND");
                bool small = scr.Width < Dpi.S(160) || scr.Height < Dpi.S(120);
                if (small && (cls == "Chrome_WidgetWin_1" || cls == "MozillaWindowClass"))
                {
                    // no page window (Firefox, or a Chromium window that keeps its page in the main window): the page is the document UI Automation reports
                    var root = AutomationElement.FromHandle(w.Handle); scr = Rectangle.Empty;
                    // Chrome builds the page's accessibility tree when it is first asked for it, so the first answer can be empty: ask again a moment later
                    for (int attempt = 0; attempt < 4 && scr.IsEmpty; attempt++)
                    {
                        if (attempt > 0) Thread.Sleep(150);
                        var doc = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                        if (doc != null) { var b = doc.Current.BoundingRectangle; if (!b.IsEmpty && !double.IsInfinity(b.Width)) scr = Rectangle.Round(new RectangleF((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height)); }
                    }
                }
                if (scr.Width >= Dpi.S(160) && scr.Height >= Dpi.S(120))
                {
                    scr.Offset(-origin.X, -origin.Y);
                    // Chrome on a monitor with another scale than the main one reports the page's size right but its position wrong (outside its own window):
                    // the page fills the window from its bottom edge, so put it there
                    var win = Rectangle.Inflate(w.Rect, 6, 6);
                    if (!win.Contains(scr) && scr.Width <= w.Rect.Width + 6 && scr.Height < w.Rect.Height) scr = new Rectangle(w.Rect.X + (w.Rect.Width - scr.Width) / 2, w.Rect.Bottom - scr.Height, scr.Width, scr.Height);
                    r = Clip(scr);
                }
            }
            catch { }
            content[w.Handle] = r; return r;
        }
        Rectangle WindowAt(Point p)
        {
            foreach (var w in wins)
            {
                if (!w.Rect.Contains(p)) continue;
                if (w.Handle != levelWin) { levelWin = w.Handle; paneLevel = 0; }
                var page = ContentOf(w);
                if (page.Width > 0)
                {
                    // the page first; the wheel goes out to the whole window
                    paneLevel = Math.Max(0, Math.Min(1, paneLevel));
                    return paneLevel == 0 ? page : Clip(w.Rect);
                }
                if (p.Y - w.Rect.Top <= Dpi.S(40) && w.Rect.Height > Dpi.S(160)) return Clip(w.Rect);
                List<Rectangle> kids;
                if (!children.TryGetValue(w.Handle, out kids))
                {
                    var clip = w.Rect; clip.Offset(origin.X, origin.Y);
                    kids = Native.ChildRects(w.Handle, clip, Dpi.S(48), Dpi.S(28)).Select(r => { r.Offset(-origin.X, -origin.Y); return r; }).ToList();
                    children[w.Handle] = kids;
                }
                List<Rectangle> ui; if (!panes.TryGetValue(w.Handle, out ui)) { ui = null; LoadPanes(w); }
                long whole = (long)w.Rect.Width * w.Rect.Height;
                var chain = new List<Rectangle>();
                foreach (var list in new[] { kids, ui })
                {
                    if (list == null) continue;
                    foreach (var k in list)
                    {
                        long a = (long)k.Width * k.Height;
                        if (!k.Contains(p) || a >= whole * 0.92 || k.Width < Dpi.S(48) || k.Height < Dpi.S(28)) continue;
                        bool dup = false; foreach (var c in chain) if (Math.Abs(c.X - k.X) < 4 && Math.Abs(c.Y - k.Y) < 4 && Math.Abs(c.Width - k.Width) < 6 && Math.Abs(c.Height - k.Height) < 6) { dup = true; break; }
                        if (!dup) chain.Add(k);
                    }
                }
                chain.Sort((a, b) => ((long)a.Width * a.Height).CompareTo((long)b.Width * b.Height));
                chain.Add(w.Rect);
                int def = chain.FindIndex(c => c.Width >= Dpi.S(140) && c.Height >= Dpi.S(80)); if (def < 0) def = chain.Count - 1;
                int idx = Math.Max(0, Math.Min(chain.Count - 1, def + paneLevel)); paneLevel = idx - def;     // the wheel stops at both ends
                return Clip(chain[idx]);
            }
            return Rectangle.Empty;
        }
        // ask UI Automation, off the UI thread, for the panes, toolbars, lists and documents inside a window
        void LoadPanes(WinInfo w)
        {
            if (!paneLoading.Add(w.Handle)) return;
            var handle = w.Handle; int ox = origin.X, oy = origin.Y;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var res = new List<Rectangle>();
                try
                {
                    var root = AutomationElement.FromHandle(handle);
                    var cr = new CacheRequest(); cr.Add(AutomationElement.BoundingRectangleProperty); cr.Add(AutomationElement.IsOffscreenProperty); cr.TreeScope = TreeScope.Element;
                    var types = new[] { ControlType.Pane, ControlType.Group, ControlType.Document, ControlType.ToolBar, ControlType.Tab, ControlType.List, ControlType.Tree, ControlType.Table, ControlType.DataGrid, ControlType.StatusBar, ControlType.MenuBar, ControlType.Custom };
                    var cond = new OrCondition(types.Select(t => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, t)).ToArray());
                    AutomationElementCollection col; using (cr.Activate()) col = root.FindAll(TreeScope.Descendants, cond);
                    foreach (AutomationElement el in col)
                    {
                        if (res.Count > 1500) break;
                        if (el.Cached.IsOffscreen) continue;
                        var r = el.Cached.BoundingRectangle; if (r.IsEmpty || double.IsInfinity(r.Width) || r.Width < 40 || r.Height < 24) continue;
                        res.Add(Rectangle.Round(new RectangleF((float)(r.X - ox), (float)(r.Y - oy), (float)r.Width, (float)r.Height)));
                    }
                }
                catch { }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        panes[handle] = res; paneLoading.Remove(handle);
                        if (Snaps && !dragging && !choosing && levelWin == handle) { hover = WindowAt(cursor); InvalidateAround(); }
                    });
                }
                catch { }
            });
        }

        // ---- All-in-One: after an area is chosen, a bar of actions appears under it
        bool choosing; readonly List<KeyValuePair<Rectangle, CapMode>> actionRects = new List<KeyValuePair<Rectangle, CapMode>>(); int actionHover = -1;
        static readonly CapMode[] aioActions = { CapMode.Region, CapMode.Video, CapMode.Gif, CapMode.Scroll, CapMode.ScrollH, CapMode.Text };
        static readonly string[] aioNames = { "Image", "Video", "GIF", "Scroll", "Panorama", "Text" };
        static readonly Ic[] aioIcons = { Ic.Camera, Ic.Video, Ic.Clock, Ic.Order, Ic.Move, Ic.Ocr };
        static readonly Keys[] aioKeys = { Keys.I, Keys.V, Keys.G, Keys.S, Keys.P, Keys.T };
        void LayoutActions()
        {
            actionRects.Clear();
            int bw = Dpi.S(78), bh = Dpi.S(62), gap = Dpi.S(4), total = aioActions.Length * bw + (aioActions.Length - 1) * gap + Dpi.S(12);
            int x = sel.X + (sel.Width - total) / 2, y = sel.Bottom + Dpi.S(10);
            if (y + bh + Dpi.S(12) > shot.Height) y = sel.Bottom - bh - Dpi.S(22);
            x = Math.Max(Dpi.S(6), Math.Min(shot.Width - total - Dpi.S(6), x));
            for (int i = 0; i < aioActions.Length; i++) actionRects.Add(new KeyValuePair<Rectangle, CapMode>(new Rectangle(x + Dpi.S(6) + i * (bw + gap), y + Dpi.S(6), bw, bh), aioActions[i]));
        }
        void Choose(CapMode a) { Result = new PickResult { Cancel = false, Rect = sel, Action = a }; DialogResult = DialogResult.OK; Close(); }

        void Finish(bool ok)
        {
            if (ok && mode == CapMode.AllInOne && !choosing)
            {
                if (hover.Width > 0 && sel.Width < 4) sel = hover;
                if (sel.Width < 4 || sel.Height < 4) return;
                choosing = true; fsHover = false; LayoutActions(); Cursor = Cursors.Default; Invalidate(); return;
            }
            Result = new PickResult { Cancel = !ok };
            if (ok)
            {
                if (mode == CapMode.Multi) { Result.Areas = areas.ToList(); Result.Rect = areas.Aggregate(Rectangle.Union); }
                else if (mode == CapMode.Freehand && poly != null) { Result.Poly = poly; Result.Rect = PolyBounds(poly); }
                else if (mode == CapMode.Session || Smart) { bool drag = sel.Width >= 4 && sel.Height >= 4; Result.Rect = drag ? sel : hover; Result.Dragged = drag; }
                else Result.Rect = mode == CapMode.Window || mode == CapMode.Element ? hover : sel;
                if (Result.Rect.Width < 2 || Result.Rect.Height < 2) Result.Cancel = true;
            }
            DialogResult = ok ? DialogResult.OK : DialogResult.Cancel; Close();
        }
        // pick an exact rectangle (full screen, a window from the keyboard)
        void FinishWith(Rectangle r)
        {
            if (r.Width < 2 || r.Height < 2) return;
            dragging = keyDrag = false;
            if (mode == CapMode.AllInOne) { sel = r; hover = Rectangle.Empty; Finish(true); return; }
            Result = new PickResult { Cancel = false, Rect = r }; DialogResult = DialogResult.OK; Close();
        }
        static Rectangle PolyBounds(List<Point> p) { return Rectangle.FromLTRB(p.Min(a => a.X), p.Min(a => a.Y), p.Max(a => a.X) + 1, p.Max(a => a.Y) + 1); }
        Rectangle Clip(Rectangle r) { r.Intersect(new Rectangle(Point.Empty, shot.Size)); return r; }

        // ---- keyboard
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1) { showHelp = !showHelp; Invalidate(); return; }
            if (e.KeyCode == Keys.Escape)
            {
                if (showHelp) { showHelp = false; Invalidate(); return; }
                if (choosing) { choosing = false; sel = Rectangle.Empty; Cursor = Cursors.Cross; Invalidate(); return; }
                if (keyDrag) { keyDrag = dragging = false; sel = Rectangle.Empty; Invalidate(); return; }
                Finish(false); return;
            }
            if (choosing)
            {
                if (e.KeyCode == Keys.Enter) { Choose(CapMode.Region); return; }
                int k = Array.IndexOf(aioKeys, e.KeyCode); if (k >= 0) Choose(aioActions[k]);
                return;
            }
            if (e.KeyCode == Keys.M) { loupeOn = !loupeOn; Invalidate(); return; }
            if (e.KeyCode == Keys.F && FullscreenAllowed && !dragging) { FinishWith(MonitorAt(cursor)); return; }
            if (mode == CapMode.Fixed)
            {
                int step = e.Shift ? 10 : 1;
                if (e.KeyCode == Keys.Right) fw += step; if (e.KeyCode == Keys.Left) fw = Math.Max(16, fw - step);
                if (e.KeyCode == Keys.Down) fh += step; if (e.KeyCode == Keys.Up) fh = Math.Max(16, fh - step);
                if (e.KeyCode == Keys.Enter) { Settings.FixedW = fw; Settings.FixedH = fh; sel = FixedRect(cursor); Finish(sel.Width > 0); return; }
                Invalidate(); return;
            }
            // Space starts a region at the pointer without holding the mouse button (Snagit does the same)
            if (e.KeyCode == Keys.Space && RegionAllowed && !dragging && mode != CapMode.Freehand)
            {
                keyDrag = dragging = true; start = cursor; sel = Rectangle.Empty; fsHover = false; Invalidate(); return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                if (mode == CapMode.Multi && !dragging) { Finish(areas.Count > 0); return; }
                if (dragging) { RegionDone(); return; }
                if (Snaps && hover.Width > 0) { if (mode == CapMode.AllInOne) sel = hover; Finish(true); return; }
                return;
            }
            // arrow keys move the crosshairs one pixel (Shift: ten)
            int dx = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0, dy = e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : 0;
            if (dx != 0 || dy != 0)
            {
                int s = e.Shift ? 10 : 1; var p = Cursor.Position; Cursor.Position = new Point(p.X + dx * s, p.Y + dy * s);
                e.Handled = true;
            }
        }
        protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Left || c == Keys.Right || c == Keys.Up || c == Keys.Down || base.IsInputKey(k); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Snaps && !dragging && !choosing)
            {
                paneLevel += e.Delta > 0 ? 1 : -1; hover = WindowAt(cursor); InvalidateAround(); return;      // up: a larger area, down: a smaller one
            }
            if (mode != CapMode.Fixed) return;
            float k = e.Delta > 0 ? 1.05f : 0.95f; fw = Math.Max(16, (int)(fw * k)); fh = Math.Max(16, (int)(fh * k)); Invalidate();
        }

        // ---- mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { if (mode == CapMode.Multi && areas.Count > 0) Finish(true); else Finish(false); return; }
            if (e.Button != MouseButtons.Left) return;
            if (showHelp) { showHelp = false; Invalidate(); return; }
            if (choosing)
            {
                foreach (var a in actionRects) if (a.Key.Contains(e.Location)) { Choose(a.Value); return; }
                choosing = false; Cursor = Cursors.Cross;       // clicked elsewhere: start a new selection
            }
            if (ShowFsButton && FullscreenButton().Contains(e.Location)) { FinishWith(MonitorAt(e.Location)); return; }
            if (keyDrag) { cursor = e.Location; UpdateDrag(e.Location); RegionDone(); return; }   // second corner of a Space-started region
            switch (mode)
            {
                case CapMode.Window: case CapMode.Element: if (hover.Width > 0) Finish(true); break;
                case CapMode.Fixed: Settings.FixedW = fw; Settings.FixedH = fh; sel = FixedRect(e.Location); Finish(sel.Width > 0); break;
                case CapMode.Freehand: dragging = true; poly = new List<Point> { e.Location }; break;
                default: dragging = true; start = e.Location; sel = Rectangle.Empty; break;
            }
        }
        Rectangle FixedRect(Point p) { return Clip(new Rectangle(p.X - fw / 2, p.Y - fh / 2, fw, fh)); }
        void UpdateDrag(Point p) { sel = Clip(Rectangle.FromLTRB(Math.Min(start.X, p.X), Math.Min(start.Y, p.Y), Math.Max(start.X, p.X), Math.Max(start.Y, p.Y))); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            cursor = e.Location;
            if (choosing)
            {
                int h = -1; for (int i = 0; i < actionRects.Count; i++) if (actionRects[i].Key.Contains(e.Location)) h = i;
                if (h != actionHover) { actionHover = h; Cursor = h >= 0 ? Theme.Hand : Cursors.Default; Invalidate(); }
                return;
            }
            // hovering the Fullscreen button previews the whole monitor
            bool overFs = ShowFsButton && FullscreenButton().Contains(e.Location);
            if (overFs != fsHover) { fsHover = overFs; Cursor = overFs ? Theme.Hand : Cursors.Cross; }
            if (overFs) { InvalidateAround(); return; }
            if (Snaps && !dragging) hover = WindowAt(e.Location);
            else if (mode == CapMode.Element) { if ((DateTime.Now - lastProbe).TotalMilliseconds > 45) { lastProbe = DateTime.Now; ProbeElement(e.Location); } }
            else if (mode == CapMode.Fixed) sel = FixedRect(e.Location);
            else if (dragging && mode == CapMode.Freehand) { if (Math.Abs(poly[poly.Count - 1].X - e.X) + Math.Abs(poly[poly.Count - 1].Y - e.Y) > 2) poly.Add(e.Location); }
            else if (dragging) UpdateDrag(e.Location);
            InvalidateAround();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || keyDrag) return;
            RegionDone();
        }
        void RegionDone()
        {
            dragging = false; keyDrag = false;
            if (mode == CapMode.Freehand) { if (poly != null && poly.Count > 5) Finish(true); else poly = null; return; }
            if (AioLike && (sel.Width < 4 || sel.Height < 4)) { sel = Rectangle.Empty; Finish(true); return; }   // a click picks the window under the pointer
            if (sel.Width < 4 || sel.Height < 4) { sel = Rectangle.Empty; Invalidate(); return; }
            if (mode == CapMode.Multi) { areas.Add(sel); sel = Rectangle.Empty; Invalidate(); return; }
            Finish(true);
        }

        // ask UI Automation what lives under the cursor; the overlay steps aside for the query
        void ProbeElement(Point p)
        {
            try
            {
                var old = Location;
                Native.MoveOffscreen(Handle);
                AutomationElement el = null;
                try { el = AutomationElement.FromPoint(new System.Windows.Point(p.X + origin.X, p.Y + origin.Y)); } finally { Native.MoveTo(Handle, old.X, old.Y); }
                if (el == null) return;
                var r = el.Current.BoundingRectangle;
                if (r.IsEmpty || double.IsInfinity(r.Width)) return;
                hover = Clip(Rectangle.Round(new RectangleF((float)(r.X - origin.X), (float)(r.Y - origin.Y), (float)r.Width, (float)r.Height)));
                elementName = el.Current.Name;
            }
            catch { }
        }
        string elementName = "";

        // ---- painting
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.None;
            g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImageUnscaled(dimmed, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            Rectangle area = mode == CapMode.Window || mode == CapMode.Element ? hover : sel;
            if (AioLike && !choosing && !dragging) area = hover;
            bool fsOn = fsHover && ShowFsButton;
            if (fsOn) area = MonitorAt(cursor);

            foreach (var a in areas) { g.DrawImage(bright, a, a, GraphicsUnit.Pixel); using (var p = new Pen(Theme.Cobalt, 2f)) g.DrawRectangle(p, a); }
            if (mode == CapMode.Freehand && poly != null && poly.Count > 1)
            {
                using (var gp = new GraphicsPath()) { gp.AddPolygon(poly.ToArray()); g.SetClip(gp); g.DrawImageUnscaled(bright, 0, 0); g.ResetClip(); }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(Color.White, 2f)) g.DrawPolygon(p, poly.ToArray());
                using (var p = new Pen(Theme.Cobalt, 2f) { DashStyle = DashStyle.Dash }) g.DrawPolygon(p, poly.ToArray());
                g.SmoothingMode = SmoothingMode.None;
            }
            if (area.Width > 0 && area.Height > 0)
            {
                g.DrawImage(bright, area, area, GraphicsUnit.Pixel);
                using (var p = new Pen(Theme.Cobalt, Dpi.F(fsOn ? 4f : 2f))) g.DrawRectangle(p, area);
                if (!fsOn) Theme.CropMarks(g, area, Dpi.S(5), Dpi.S(14), Color.White);
                string label = fsOn ? "Full screen  " + area.Width + " x " + area.Height : area.Width + " x " + area.Height + (mode == CapMode.Element && elementName.Length > 0 ? "   " + Trim(elementName) : "");
                if (!fsOn) Chip(g, label, area.Left, area.Top - Dpi.S(30)); else { var fb = FullscreenButton(); Chip(g, label, fb.X, fb.Bottom + Dpi.S(8)); }
            }
            else if (mode != CapMode.Window && mode != CapMode.Element && mode != CapMode.Freehand)
            {
                using (var p = new Pen(Color.FromArgb(150, 255, 255, 255)) { DashStyle = DashStyle.Dash })
                { g.DrawLine(p, 0, cursor.Y, Width, cursor.Y); g.DrawLine(p, cursor.X, 0, cursor.X, Height); }
            }
            if (mode == CapMode.Multi && areas.Count > 0)
                for (int i = 0; i < areas.Count; i++) Chip(g, (i + 1).ToString(), areas[i].Left, areas[i].Top - Dpi.S(30));
            if (choosing) { DrawActions(g); Banner(g); return; }
            if (ShowFsButton) DrawFsButton(g);
            if (loupeOn && !fsOn && mode != CapMode.Window && mode != CapMode.Element && !(AioLike && !dragging && hover.Width > 0)) Loupe(g);
            if (showHelp) DrawHelp(g); else Banner(g);
        }
        void DrawFsButton(Graphics g)
        {
            var r = FullscreenButton();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.RoundRect(r, r.Height / 2f))
            {
                using (var b = new SolidBrush(fsHover ? Theme.Cobalt : Color.FromArgb(235, Theme.Ink))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
            Icons.Draw(g, Ic.Fit, new RectangleF(r.X + Dpi.S(14), r.Y + (r.Height - Dpi.S(16)) / 2f, Dpi.S(16), Dpi.S(16)), Color.White);
            using (var f = Theme.BodyBold(8.5f)) using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                g.DrawString("Fullscreen", f, Brushes.White, new RectangleF(r.X + Dpi.S(38), r.Y, r.Width - Dpi.S(40), r.Height), sf);
            g.SmoothingMode = SmoothingMode.None;
        }
        void DrawActions(Graphics g)
        {
            if (actionRects.Count == 0) return;
            var all = actionRects.Select(a => a.Key).Aggregate(Rectangle.Union); all.Inflate(Dpi.S(6), Dpi.S(6));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.RoundRect(all, Dpi.S(8))) { using (var b = new SolidBrush(Theme.Sheet)) g.FillPath(b, p); using (var pen = new Pen(Theme.Rule)) g.DrawPath(pen, p); }
            for (int i = 0; i < actionRects.Count; i++)
            {
                var r = actionRects[i].Key; bool h = i == actionHover;
                if (h) using (var p = Theme.RoundRect(r, Dpi.S(6))) using (var b = new SolidBrush(Theme.CobaltTint)) g.FillPath(b, p);
                Icons.Draw(g, aioIcons[i], new RectangleF(r.X + (r.Width - Dpi.S(26)) / 2f, r.Y + Dpi.S(8), Dpi.S(26), Dpi.S(26)), i == 0 ? Theme.Cobalt : i == 1 || i == 2 ? Theme.Mark : Theme.Ink);
                using (var f = Theme.Body(7.5f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(aioNames[i] + " (" + aioKeys[i] + ")", f, new SolidBrush(Theme.Ink), new RectangleF(r.X, r.Y + Dpi.S(38), r.Width, Dpi.S(20)), sf);
            }
            g.SmoothingMode = SmoothingMode.None;
        }
        // F1 help: the keys this mode understands, as Snagit's capture help does
        void DrawHelp(Graphics g)
        {
            var rows = new List<KeyValuePair<string, string>>();
            Action<string, string> add = delegate(string k, string t) { rows.Add(new KeyValuePair<string, string>(k, t)); };
            if (Snaps) { add("Click", mode == CapMode.AllInOne ? "Pick the highlighted window or panel" : "Capture the highlighted window or panel"); add("Wheel", "Highlight a larger or smaller area (window, pane, toolbar...)"); }
            if (RegionAllowed) { add("Drag", "Select a region"); add("Space", "Start a region at the pointer, click to finish"); }
            if (mode == CapMode.Fixed) { add("Arrows", "Resize the frame (Shift: by ten)"); add("Wheel", "Scale the frame"); }
            add("Enter", Snaps ? "Finish the selection or pick the highlighted window" : "Finish the selection");
            if (mode != CapMode.Fixed) add("Arrows", "Move the crosshairs one pixel (Shift: ten)");
            if (FullscreenAllowed) add("F", "Capture the full screen you are pointing at");
            if (mode == CapMode.Multi) add("Right-click", "Finish all the areas");
            add("M", "Show or hide the magnifier");
            add("Esc", "Cancel");
            add("F1", "Close this help");
            var m = MonitorAt(cursor);
            using (var kf = Theme.Label(8f)) using (var tf = Theme.Body(8.5f)) using (var hf = new Font(Theme.LabelFamily, 11f, FontStyle.Bold))
            {
                int rowH = Dpi.S(28), keyW = Dpi.S(92), w = Dpi.S(430), h = Dpi.S(58) + rows.Count * rowH + Dpi.S(12);
                var r = new Rectangle(m.X + (m.Width - w) / 2, m.Y + (m.Height - h) / 2, w, h);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Theme.RoundRect(r, Dpi.S(10))) { using (var b = new SolidBrush(Theme.Sheet)) g.FillPath(b, p); using (var pen = new Pen(Theme.Rule)) g.DrawPath(pen, p); }
                g.DrawString("Capture help", hf, new SolidBrush(Theme.Ink), r.X + Dpi.S(20), r.Y + Dpi.S(14));
                int y = r.Y + Dpi.S(50);
                foreach (var row in rows)
                {
                    var ksz = g.MeasureString(row.Key, kf); var kr = new RectangleF(r.X + Dpi.S(20), y + Dpi.S(3), Math.Max(Dpi.S(38), ksz.Width + Dpi.S(14)), Dpi.S(21));
                    using (var p = Theme.RoundRect(kr, Dpi.S(5))) using (var b = new SolidBrush(Theme.Ink)) g.FillPath(b, p);
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(row.Key, kf, Brushes.White, kr, sf);
                    g.DrawString(row.Value, tf, new SolidBrush(Theme.InkSoft), r.X + Dpi.S(20) + keyW + Dpi.S(8), y + Dpi.S(5));
                    y += rowH;
                }
                g.SmoothingMode = SmoothingMode.None;
            }
        }
        static string Trim(string s) { return s.Length > 40 ? s.Substring(0, 40) + "..." : s; }
        void Chip(Graphics g, string text, int x, int y)
        {
            using (var f = Theme.Label(8f))
            {
                var sz = g.MeasureString(text, f); y = Math.Max(2, y);
                using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, x, y, sz.Width + Dpi.S(14), sz.Height + 4);
                g.DrawString(text, f, Brushes.White, x + Dpi.S(7), y + 2);
            }
        }
        void Loupe(Graphics g)
        {
            int n = 13, z = Dpi.S(9), box = n * z; var c = cursor;
            int lx = c.X + Dpi.S(26), ly = c.Y + Dpi.S(26);
            if (lx + box + 8 > Width) lx = c.X - box - Dpi.S(26); if (ly + box + Dpi.S(46) > Height) ly = c.Y - box - Dpi.S(60);
            var src = new Rectangle(c.X - n / 2, c.Y - n / 2, n, n);
            var oi = g.InterpolationMode; var op = g.PixelOffsetMode; g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bright, new Rectangle(lx, ly, box, box), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = oi; g.PixelOffsetMode = op;
            using (var p = new Pen(Theme.Ink, 2f)) g.DrawRectangle(p, lx, ly, box, box);
            using (var p = new Pen(Theme.Mark, 1.5f)) g.DrawRectangle(p, lx + (n / 2) * z, ly + (n / 2) * z, z, z);
            Color px = c.X >= 0 && c.Y >= 0 && c.X < shot.Width && c.Y < shot.Height ? shot.GetPixel(c.X, c.Y) : Color.Black;
            Chip(g, (c.X + origin.X) + ", " + (c.Y + origin.Y) + "   #" + px.R.ToString("X2") + px.G.ToString("X2") + px.B.ToString("X2"), lx, ly + box + 2);
        }
        // hint line at the bottom of the monitor under the pointer (the top is where the Fullscreen button sits)
        void Banner(Graphics g)
        {
            string t;
            if (choosing) t = "Choose what to capture. Enter takes a picture";
            else if (keyDrag) t = "Move to the other corner, then click or press Enter";
            else switch (mode)
                {
                    case CapMode.AllInOne: t = "Click a window or panel, or drag an area     Wheel: larger or smaller area"; break;
                    case CapMode.Session: t = "Click the window to record (a browser: just its page), or drag an area     Wheel: larger or smaller area"; break;
                    case CapMode.Window: t = "Click a window or panel     Wheel: larger or smaller area"; break; case CapMode.Element: t = "Click a button, panel or other element"; break;
                    case CapMode.Fixed: t = "Click to place a " + fw + " x " + fh + " area. Arrows or wheel resize it"; break;
                    case CapMode.Multi: t = "Drag several areas, then Enter or right-click to finish"; break;
                    case CapMode.Freehand: t = "Hold the mouse button and draw around the area"; break;
                    case CapMode.Region: t = "Click a window or panel (a browser: just its page), or drag an area     Wheel: larger or smaller area"; break;
                    case CapMode.Scroll: t = "Click the scrolling window or panel, or drag over the scrolling area     Wheel: larger or smaller area"; break;
                    case CapMode.ScrollH: t = "Click the horizontally scrolling window or panel, or drag over it     Wheel: larger or smaller area"; break;
                    case CapMode.Text: t = "Drag over the text to copy"; break;
                    case CapMode.Video: case CapMode.Gif: t = "Click the window to record (a browser: just its page), or drag the area     Wheel: larger or smaller area"; break;
                    default: t = "Drag to select an area"; break;
                }
            t += (FullscreenAllowed && !choosing ? "     F full screen" : "") + "     F1 help     Esc cancel";
            var m = MonitorAt(cursor);
            using (var f = Theme.Body(8.5f))
            {
                var sz = g.MeasureString(t, f);
                float x = m.X + (m.Width - sz.Width) / 2, y = m.Bottom - sz.Height - Dpi.S(30);
                using (var b = new SolidBrush(Color.FromArgb(235, Theme.Ink))) g.FillRectangle(b, x - Dpi.S(11), y - Dpi.S(5), sz.Width + Dpi.S(22), sz.Height + Dpi.S(10));
                g.DrawString(t, f, Brushes.White, x, y);
            }
        }
        protected override void Dispose(bool d) { if (d) { dimmed.Dispose(); bright.Dispose(); if (lastDirty != null) lastDirty.Dispose(); } base.Dispose(d); }

        // dev check: render this overlay in a given state without showing it
        internal Rectangle HoverAt(Point at) { cursor = at; hover = WindowAt(at); return hover; }
        internal Bitmap Snapshot(Point at, bool overFullscreen, bool help)
        {
            cursor = at; fsHover = false; if (Snaps) hover = WindowAt(at); showHelp = help;
            if (overFullscreen) { cursor = new Point(FullscreenButton().X + 10, FullscreenButton().Y + 10); fsHover = true; }
            var b = new Bitmap(shot.Width, shot.Height);
            using (var g = Graphics.FromImage(b)) OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, shot.Size)));
            return b;
        }
    }

    static class Capturer
    {
        public static Rectangle LastRegion; public static CapMode LastMode = CapMode.Region; public static bool HasLast;

        // Capture one picture. Returns null when cancelled or when the result went somewhere else (text mode).
        public static Doc Run(CapMode mode, bool repeatLast)
        {
            var hidden = EditorForm.Open.Where(f => f.Visible).ToList();
            foreach (var f in hidden) f.Opacity = 0;
            if (Hub.Instance != null) Hub.Instance.HideForCapture();
            Native.WaitForRepaint();
            Bitmap shot = Native.GrabVirtualScreen(Settings.ShowCursor);
            try
            {
                foreach (var f in hidden) f.Opacity = 1;
                if (mode == CapMode.Full) return Make(shot, null, mode);
                PickResult pick;
                if (repeatLast && HasLast) pick = new PickResult { Rect = LastRegion };
                else
                {
                    using (var ov = new PickOverlay(shot, mode)) { ov.ShowDialog(); pick = ov.Result; }
                }
                if (pick.Cancel) return null;
                LastRegion = pick.Rect; LastMode = mode; HasLast = true;
                if (mode == CapMode.AllInOne) mode = pick.Action;     // the user chose image / video / GIF / scroll / text

                if (mode == CapMode.Text) { TextGrab(shot, pick.Rect); return null; }
                var vs = SystemInformation.VirtualScreen; var screenRect = pick.Rect; screenRect.Offset(vs.X, vs.Y);
                if (mode == CapMode.Video || mode == CapMode.Gif) { Recorder.Start(screenRect, mode == CapMode.Gif); return null; }
                if (mode == CapMode.Scroll || mode == CapMode.ScrollH) return ScrollDoc(screenRect, mode == CapMode.ScrollH);
                return Make(shot, pick, mode);
            }
            finally { shot.Dispose(); if (Hub.Instance != null) Hub.Instance.ShowAfterCapture(); }
        }

        static Doc Make(Bitmap shot, PickResult pick, CapMode mode)
        {
            Bitmap result;
            if (pick == null) result = (Bitmap)shot.Clone();
            else if (pick.Poly != null)
            {
                var b = pick.Rect; result = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(result)) using (var gp = new GraphicsPath())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    gp.AddPolygon(pick.Poly.Select(p => new Point(p.X - b.X, p.Y - b.Y)).ToArray());
                    g.SetClip(gp); g.DrawImage(shot, new Rectangle(0, 0, b.Width, b.Height), b.X, b.Y, b.Width, b.Height, GraphicsUnit.Pixel);
                }
            }
            else if (pick.Areas.Count > 1)
            {
                int gap = 12, w = pick.Areas.Sum(a => a.Width) + gap * (pick.Areas.Count - 1), h = pick.Areas.Max(a => a.Height);
                result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(result)) { int x = 0; foreach (var a in pick.Areas) { g.DrawImage(shot, new Rectangle(x, 0, a.Width, a.Height), a.X, a.Y, a.Width, a.Height, GraphicsUnit.Pixel); x += a.Width + gap; } }
            }
            else result = Effects.Crop(shot, pick.Rect);

            string name = "Capture " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            var doc = new Doc(result) { Name = name, Dirty = false };
            Directory.CreateDirectory(Settings.Folder);
            string path = Path.Combine(Settings.Folder, name + ".png"); int n = 2;
            while (File.Exists(path)) path = Path.Combine(Settings.Folder, name + " (" + n++ + ").png");
            result.Save(path, ImageFormat.Png); doc.Path = path; doc.Name = Path.GetFileNameWithoutExtension(path);
            LibraryStore.Touch(path);
            return doc;
        }

        static Doc ScrollDoc(Rectangle screenRect, bool horizontal)
        {
            Thread.Sleep(200);
            Hub.Toast("Scrolling capture", "Keep the mouse still. Press Esc to stop early.");
            string note; var bmp = ScrollCapture.Capture(screenRect, horizontal, 80, null, out note);
            if (bmp == null) { Hub.Toast("Nothing captured", note.Length > 0 ? note : "The area did not scroll."); return null; }
            var doc = SaveBitmap(bmp);
            if (note.Length > 0) Hub.Toast("Scrolling capture done", note);
            return doc;
        }

        static Doc SaveBitmap(Bitmap result)
        {
            string name = "Capture " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            var doc = new Doc(result) { Name = name, Dirty = false };
            Directory.CreateDirectory(Settings.Folder);
            string path = Path.Combine(Settings.Folder, name + ".png"); int n = 2;
            while (File.Exists(path)) path = Path.Combine(Settings.Folder, name + " (" + n++ + ").png");
            result.Save(path, ImageFormat.Png); doc.Path = path; doc.Name = Path.GetFileNameWithoutExtension(path);
            LibraryStore.Touch(path);
            return doc;
        }

        static void TextGrab(Bitmap shot, Rectangle r)
        {
            try
            {
                using (var piece = Effects.Crop(shot, r))
                {
                    var res = Ocr.Recognize(piece);
                    if (string.IsNullOrWhiteSpace(res.Text)) { Hub.Toast("No text found", "Try a tighter selection around the text."); return; }
                    Clipboard.SetText(res.Text);
                    Hub.Toast("Text copied (" + res.Words.Count + " words)", res.Text.Length > 90 ? res.Text.Substring(0, 90) + "..." : res.Text);
                }
            }
            catch (Exception ex) { Hub.Toast("Text capture failed", ex.Message); }
        }

        // Pick a region or window on a frozen screen; returns it in screen pixels (empty when cancelled).
        public static Rectangle PickScreen(CapMode overlayMode)
        {
            if (Hub.Instance != null) Hub.Instance.HideForCapture();
            Native.WaitForRepaint();
            Bitmap shot = Native.GrabVirtualScreen(false);
            try
            {
                PickResult pick;
                using (var ov = new PickOverlay(shot, overlayMode)) { ov.ShowDialog(); pick = ov.Result; }
                if (pick.Cancel) return Rectangle.Empty;
                var vs = SystemInformation.VirtualScreen; var r = pick.Rect; r.Offset(vs.X, vs.Y); return r;
            }
            finally { shot.Dispose(); if (Hub.Instance != null) Hub.Instance.ShowAfterCapture(); }
        }

        // Record session: hover a window (a browser shows just its page) and click it, or drag an area, or press F for the monitor.
        // Returns the area in screen pixels, or null when cancelled.
        public static PickResult PickSession()
        {
            if (Hub.Instance != null) Hub.Instance.HideForCapture();
            Native.WaitForRepaint();
            Bitmap shot = Native.GrabVirtualScreen(false);
            try
            {
                PickResult pick;
                using (var ov = new PickOverlay(shot, CapMode.Session)) { ov.ShowDialog(); pick = ov.Result; }
                if (pick.Cancel) return null;
                var vs = SystemInformation.VirtualScreen; var r = pick.Rect; r.Offset(vs.X, vs.Y); pick.Rect = r; return pick;
            }
            finally { shot.Dispose(); if (Hub.Instance != null) Hub.Instance.ShowAfterCapture(); }
        }

        // Video tab: pick a region or window on a frozen screen, then record that area live.
        public static void RecordPicked(CapMode overlayMode, bool gif)
        {
            if (Hub.Instance != null) Hub.Instance.HideForCapture();
            Native.WaitForRepaint();
            Bitmap shot = Native.GrabVirtualScreen(false);
            try
            {
                PickResult pick;
                using (var ov = new PickOverlay(shot, overlayMode == CapMode.Window ? CapMode.Window : (gif ? CapMode.Gif : CapMode.Video))) { ov.ShowDialog(); pick = ov.Result; }
                if (pick.Cancel) return;
                var vs = SystemInformation.VirtualScreen; var r = pick.Rect; r.Offset(vs.X, vs.Y);
                Recorder.Start(r, gif);
            }
            finally { shot.Dispose(); if (Hub.Instance != null) Hub.Instance.ShowAfterCapture(); }
        }

        // Finish a picture capture the way the capture window says: effect, share, clipboard, editor.
        public static void Deliver(Doc doc)
        {
            if (doc == null) return;
            string fx = Settings.ImgEffect;
            if (fx != "none")
            {
                try
                {
                    switch (fx)
                    {
                        case "border": doc.Base = Effects.Border(doc.Base, 6, Theme.Ink); break;
                        case "shadow": doc.Base = Effects.DropShadow(doc.Base, 5, 7, 10, Color.Black, 40); break;
                        case "torn": doc.Base = Effects.Edge(doc.Base, EdgeKind.Torn, 14, Environment.TickCount); break;
                        case "fade": doc.Base = Effects.Edge(doc.Base, EdgeKind.Fade, 18, 1); break;
                        case "gray": doc.Base = Effects.Grayscale(doc.Base); break;
                        case "sepia": doc.Base = Effects.Sepia(doc.Base); break;
                    }
                    if (doc.Path != null) doc.Base.Save(doc.Path, ImageFormat.Png);
                }
                catch (Exception ex) { Hub.Toast("Effect skipped", ex.Message); }
            }
            if (Settings.CopyToClip) { try { using (var f = doc.Render()) Clipboard.SetImage(f); } catch { } }
            switch (Settings.ImgShare)
            {
                case "file": using (var f = doc.Render()) Sharing.SaveAs(null, f, doc.Name); break;
                case "email": Sharing.Email(doc.Path, doc.Name); break;
                case "ftp": Sharing.Ftp(null, doc.Path, doc.Name + ".png"); break;
                case "print": using (var f = doc.Render()) Sharing.Print(null, f); break;
                case "folder": if (doc.Path != null) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + doc.Path + "\""); break;
            }
            if (Settings.PreviewInEditor) EditorForm.Show(doc);
            else
            {
                string path = doc.Path;
                Hub.Toast(Settings.CopyToClip ? "Copied to the clipboard" : "Capture saved", doc.Name + "  (click to open in the editor)", delegate { try { EditorForm.Show(EditorForm.LoadAny(path)); } catch { } });
            }
            if (Hub.Instance != null) Hub.Instance.RefreshRecent();
        }
    }
}
