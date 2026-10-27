// Theme tokens and drawing helpers: the "light table" look.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    // Pixel sizes in code are written for 96 dpi; S() scales them to the real screen density.
    static class Dpi
    {
        public static readonly float K = Measure();
        static float Measure() { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); }
        public static int S(int v) { return (int)Math.Round(v * K); }
        public static float F(float v) { return v * K; }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public static Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }
        // A window made "system DPI aware" is laid out in the startup scale (K) and stretched by Windows on a monitor with another scale.
        // So on a monitor, a size in layout pixels is 1/Ratio real pixels, and that monitor's work area is wa * Ratio layout pixels wide.
        public static float Ratio(Screen sc) { return K / Native.MonitorScale(sc); }
        // such a window is placed in layout pixels too: a real position (x, y) on a monitor is (x * Ratio, y * Ratio). This is the monitor's work area in those.
        public static Rectangle WorkAreaV(Screen sc) { var wa = sc.WorkingArea; float r = Ratio(sc); return new Rectangle((int)(wa.X * r), (int)(wa.Y * r), (int)(wa.Width * r), (int)(wa.Height * r)); }
    }

    static class Theme
    {
        // "Calm workbench": a neutral, slightly warm surface, ink type, one teal-green accent for the action and the selection,
        // and red kept for recording and for the marks people draw. Separation comes from a tone step or a 1px line, never a heavy outline.
        public static readonly Color Ink = Color.FromArgb(0x1C, 0x1F, 0x22);
        public static readonly Color InkSoft = Color.FromArgb(0x3A, 0x3F, 0x44);
        public static readonly Color Slate = Color.FromArgb(0x5E, 0x65, 0x6C);           // secondary text (5.9:1 on white)
        public static readonly Color Mist = Color.FromArgb(0x8D, 0x94, 0x9A);            // disabled text, placeholder marks
        public static readonly Color Rule = Color.FromArgb(0xE2, 0xE4, 0xE1);            // dividers, card edges
        public static readonly Color Line = Color.FromArgb(0xC9, 0xCD, 0xC9);            // the edge of a control you can press or type in
        public static readonly Color Paper = Color.FromArgb(0xF3, 0xF4, 0xF2);           // window ground
        public static readonly Color Sheet = Color.FromArgb(0xFA, 0xFA, 0xF9);           // panels
        public static readonly Color Hover = Color.FromArgb(0xEC, 0xEE, 0xEB);           // a quiet control under the pointer
        public static readonly Color Press = Color.FromArgb(0xE2, 0xE5, 0xE1);
        public static readonly Color White = Color.White;
        // The accent goes on the one main action, the chosen tab's icon, switches that are on, check marks and focus. It reads as text on white (5.3:1).
        public static readonly Color Accent = Color.FromArgb(0x0B, 0x7A, 0x6A);
        public static readonly Color AccentDark = Color.FromArgb(0x08, 0x66, 0x58);       // hover on a filled accent button, lines that must show on white
        public static readonly Color AccentDeep = Color.FromArgb(0x06, 0x52, 0x47);       // pressed
        public static readonly Color AccentTint = Color.FromArgb(0xE3, 0xF2, 0xEE);       // selected rows, hover on accent things
        public static readonly Color AccentInk = Accent;                                   // text and small marks on white
        public static readonly Color AccentSoft = Color.FromArgb(0xC6, 0xE8, 0xE0);       // big filled shapes; ink text sits on it
        public static readonly Color AccentSoftHover = Color.FromArgb(0xB4, 0xE0, 0xD6);
        public static readonly Color AccentSoftDown = Color.FromArgb(0xA0, 0xD6, 0xCA);
        public static readonly Color Charcoal = Color.FromArgb(0x2A, 0x2E, 0x31);         // dark chrome: the recording bar, check marks on light fills
        public static Color Mix(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        // older code still asks for these names: they are the accent now
        public static readonly Color Cobalt = Accent;           // (the old name, kept so older code keeps compiling)
        public static readonly Color CobaltDark = AccentDark;
        public static readonly Color CobaltTint = AccentTint;
        public static readonly Color Mark = Color.FromArgb(0xD6, 0x2B, 0x2B);              // errors, and the default colour of the boxes and numbers on screenshots, which must stand out on any screen
        public static readonly Color Ok = Color.FromArgb(0x1A, 0x8A, 0x57);
        public static readonly Color Warn = Color.FromArgb(0xB7, 0x6E, 0x00);

        public const int Radius = 6;            // controls; cards use Dpi.S(12)

        static string Pick(params string[] names)
        {
            var installed = new InstalledFontCollection().Families.Select(f => f.Name).ToList();
            foreach (var n in names) if (installed.Contains(n)) return n;
            return "Segoe UI";
        }
        // one family everywhere; weight and size carry the hierarchy
        public static readonly string BodyFamily = "Segoe UI";
        public static readonly string LabelFamily = "Segoe UI";

        public static Font Body(float pt) { return new Font(BodyFamily, pt, FontStyle.Regular); }
        public static Font BodyBold(float pt) { return new Font(BodyFamily, pt, FontStyle.Bold); }
        public static Font Label(float pt) { return new Font(LabelFamily, pt, FontStyle.Regular); }
        // the emphasised weight of the one family: Segoe UI Semibold (friendlier than Bold, and a touch narrower, so nothing that fitted in Bold is cut off)
        static readonly string SemiFamily = Pick("Segoe UI Semibold");
        public static Font Semi(float pt) { return SemiFamily == "Segoe UI Semibold" ? new Font(SemiFamily, pt, FontStyle.Regular) : new Font(BodyFamily, pt, FontStyle.Bold); }

        // Fill a plain rectangle with no anti-aliasing. With smoothing on, GDI+ blends the outer pixel rows with whatever the control's
        // buffer held (black), which left a grey line along the top and left of every custom button. Backgrounds go through here.
        public static void Backdrop(Graphics g, Color c, Rectangle r)
        {
            var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
            g.SmoothingMode = mode;
        }
        public static void Backdrop(Graphics g, Color c, int x, int y, int w, int h) { Backdrop(g, c, new Rectangle(x, y, w, h)); }

        // a capsule: the corners are half the height
        public static GraphicsPath Pill(RectangleF r) { return RoundRect(r, Math.Min(r.Width, r.Height) / 2f); }

        // a plain list of text rows with the accent as its selection (the system list highlights in the Windows accent colour)
        // the system's own hand: the hand WinForms ships (Cursors.Hand) is an old fixed-size bitmap that Windows stretches on a sharp screen
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance, int id);
        public static readonly Cursor Hand = SystemHand();
        static Cursor SystemHand() { try { var h = LoadCursor(IntPtr.Zero, 32649); if (h != IntPtr.Zero) return new Cursor(h); } catch { } return Cursors.Hand; }

        public static void List(ListBox lb)
        {
            lb.DrawMode = DrawMode.OwnerDrawFixed; lb.ItemHeight = Dpi.S(30); lb.BackColor = Color.White;
            lb.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? AccentTint : Color.White)) e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index < 0) return;
                if (sel) using (var b = new SolidBrush(Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 3, Dpi.S(3), e.Bounds.Height - 6);
                using (var f = Body(lb.Font.Size)) using (var br = new SolidBrush(lb.Enabled ? Ink : Mist)) using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    e.Graphics.DrawString(lb.GetItemText(lb.Items[e.Index]), f, br, new RectangleF(e.Bounds.X + Dpi.S(12), e.Bounds.Y, e.Bounds.Width - Dpi.S(16), e.Bounds.Height), sf);
            };
        }

        // a drop-down list that paints its choices in the theme (the system one highlights in the Windows accent colour)
        // the light popup shared by every menu and drop-down list: white, a soft shadow, a grey hover
        public static readonly Color MenuBack = Color.White, MenuHover = Hover, MenuLine = Rule, MenuMute = Mist, MenuText = Ink;
        public static void Combo(ComboBox c)
        {
            // The row height is set when the window is made, not now: setting ItemHeight on an owner-drawn combo box makes its window straight away,
            // before it has a parent, and it is then made inside WinForms' hidden parking window. That window may have another DPI awareness than the
            // form the combo is added to (the capture window is per-monitor, the editor system-aware), and the combo can never be moved into its form.
            int rowHeight = Dpi.S(30); var thisCombo = c;
            c.DrawMode = DrawMode.OwnerDrawFixed; c.FlatStyle = FlatStyle.Flat; c.BackColor = Color.White; c.ForeColor = Ink;
            c.HandleCreated += delegate { thisCombo.ItemHeight = thisCombo is ThemedCombo ? ((ThemedCombo)thisCombo).RowHeight : rowHeight; };
            c.DropDown += delegate { int n = Math.Max(1, Math.Min(c.Items.Count, c.MaxDropDownItems)); c.DropDownHeight = n * c.ItemHeight + 6; };
            c.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0, sel = (e.State & DrawItemState.Selected) != 0 && !edit;
                if (!edit)      // the list that drops down: light, like the menus
                {
                    var g = e.Graphics; Backdrop(g, MenuBack, e.Bounds);
                    if (sel) { g.SmoothingMode = SmoothingMode.AntiAlias; using (var p = RoundRect(new RectangleF(e.Bounds.X + 4, e.Bounds.Y + 1, e.Bounds.Width - 8, e.Bounds.Height - 2), 7)) using (var b = new SolidBrush(MenuHover)) g.FillPath(b, p); }
                    if (e.Index < 0) return;
                    using (var f = Body(c.Font.Size)) using (var br = new SolidBrush(sel ? Ink : InkSoft)) using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                        g.DrawString(c.GetItemText(c.Items[e.Index]), f, br, new RectangleF(e.Bounds.X + Dpi.S(12), e.Bounds.Y, e.Bounds.Width - Dpi.S(36), e.Bounds.Height), sf);
                    return;
                }
                using (var b = new SolidBrush(sel ? AccentTint : Color.White)) e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index < 0) return;
                using (var f = Body(c.Font.Size)) using (var br = new SolidBrush(c.Enabled ? Ink : Mist)) using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    e.Graphics.DrawString(c.GetItemText(c.Items[e.Index]), f, br, new RectangleF(e.Bounds.X + Dpi.S(6), e.Bounds.Y, e.Bounds.Width - Dpi.S(8), e.Bounds.Height), sf);
            };
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (rad <= 0 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Printer's crop marks just outside a rectangle: the signature motif.
        public static void CropMarks(Graphics g, Rectangle r, int gap, int len, Color c)
        {
            using (var p = new Pen(c, 1f))
            {
                // top-left
                g.DrawLine(p, r.Left - gap - len, r.Top, r.Left - gap, r.Top);
                g.DrawLine(p, r.Left, r.Top - gap - len, r.Left, r.Top - gap);
                // top-right
                g.DrawLine(p, r.Right + gap, r.Top, r.Right + gap + len, r.Top);
                g.DrawLine(p, r.Right, r.Top - gap - len, r.Right, r.Top - gap);
                // bottom-left
                g.DrawLine(p, r.Left - gap - len, r.Bottom, r.Left - gap, r.Bottom);
                g.DrawLine(p, r.Left, r.Bottom + gap, r.Left, r.Bottom + gap + len);
                // bottom-right
                g.DrawLine(p, r.Right + gap, r.Bottom, r.Right + gap + len, r.Bottom);
                g.DrawLine(p, r.Right, r.Bottom + gap, r.Right, r.Bottom + gap + len);
            }
        }

        public static void Checker(Graphics g, Rectangle r, int cell)
        {
            using (var a = new SolidBrush(Color.FromArgb(0xFF, 0xFF, 0xFF)))
            using (var b = new SolidBrush(Color.FromArgb(0xE3, 0xE7, 0xEB)))
            {
                g.FillRectangle(a, r);
                for (int y = 0; y < r.Height; y += cell)
                    for (int x = 0; x < r.Width; x += cell)
                        if (((x / cell) + (y / cell)) % 2 == 1)
                            g.FillRectangle(b, r.X + x, r.Y + y, Math.Min(cell, r.Width - x), Math.Min(cell, r.Height - y));
            }
        }
    }

    static class AppIcon
    {
        static Icon icon;
        public static Icon Get()
        {
            if (icon != null) return icon;
            icon = Brand.Icon; if (icon != null) return icon;          // the XLCapture mark, built in
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var p = Theme.RoundRect(new RectangleF(1, 1, 30, 30), 6)) using (var b = new SolidBrush(Theme.Ink)) g.FillPath(b, p);
                    using (var pen = new Pen(Color.White, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {   // crop marks around a vermilion dot: the app's signature
                        g.DrawLines(pen, new[] { new Point(8, 14), new Point(8, 8), new Point(14, 8) });
                        g.DrawLines(pen, new[] { new Point(18, 24), new Point(24, 24), new Point(24, 18) });
                    }
                    using (var b = new SolidBrush(Theme.Mark)) g.FillEllipse(b, 12.5f, 12.5f, 7, 7);
                }
                icon = Icon.FromHandle(bmp.GetHicon());
            }
            return icon;
        }
    }

    // Owner-drawn look for ToolStrip / ContextMenuStrip so chrome matches the theme.
    class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) { RoundedEdges = false; }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var b = e.Item as ToolStripButton;
            var r = new Rectangle(1, 1, e.Item.Width - 3, e.Item.Height - 3);
            if ((e.Item.Tag as string) == "primary")
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Theme.RoundRect(r, Dpi.F(Theme.Radius))) using (var br = new SolidBrush(e.Item.Pressed ? Theme.AccentDeep : e.Item.Selected ? Theme.AccentDark : Theme.Accent)) e.Graphics.FillPath(br, p);
                return;
            }
            bool on = (b != null && b.Checked) || (e.Item.Tag as string) == "active";
            Color fill = on ? Theme.AccentTint : e.Item.Pressed ? Theme.Press : e.Item.Selected ? Theme.Hover : Color.Transparent;
            if (fill == Color.Transparent) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.RoundRect(r, Dpi.F(Theme.Radius))) using (var br = new SolidBrush(fill))
                e.Graphics.FillPath(br, p);
        }
        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) { OnRenderButtonBackground(e); }
        static bool Pop(ToolStripItem i) { return i != null && i.Owner is ToolStripDropDown && !(i.Owner is ToolStripOverflow); }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var dd = e.ToolStrip as ToolStripDropDown;
            if (dd != null && !(dd is ToolStripOverflow)) { MenuFx.Attach(dd); using (var b = new SolidBrush(Theme.MenuBack)) e.Graphics.FillRectangle(b, e.AffectedBounds); return; }
            using (var b = new SolidBrush(e.ToolStrip.BackColor)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var dd = e.ToolStrip as ToolStripDropDown; if (dd == null || dd is ToolStripOverflow) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.RoundRect(new RectangleF(0.5f, 0.5f, dd.Width - 2, dd.Height - 2), Dpi.F(10))) using (var pen = new Pen(Theme.Line, 1f)) e.Graphics.DrawPath(pen, p);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { if (e.ToolStrip is ToolStripDropDown) return; base.OnRenderImageMargin(e); }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            bool pop = Pop(e.Item);
            using (var p = new Pen(pop ? Theme.MenuLine : Theme.Rule))
            {
                if (e.Vertical) e.Graphics.DrawLine(p, e.Item.Width / 2, 4, e.Item.Width / 2, e.Item.Height - 4);
                else e.Graphics.DrawLine(p, pop ? 12 : 6, e.Item.Height / 2, e.Item.Width - (pop ? 12 : 6), e.Item.Height / 2);
            }
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (Pop(e.Item)) { e.TextColor = e.Item.Enabled ? Theme.MenuText : Theme.MenuMute; base.OnRenderItemText(e); return; }
            e.TextColor = (e.Item.Tag as string) == "primary" ? Color.White : e.Item.Enabled ? Theme.Ink : Theme.Mist;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (Pop(e.Item))
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Theme.RoundRect(new RectangleF(5, 1, e.Item.Width - 10, e.Item.Height - 2), 8)) using (var b = new SolidBrush(Theme.MenuHover)) e.Graphics.FillPath(b, p);
                return;
            }
            if (!e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.RoundRect(new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), Dpi.F(Theme.Radius))) using (var b = new SolidBrush(e.Item.Pressed ? Theme.Press : Theme.Hover)) e.Graphics.FillPath(b, p);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = (e.Item != null && (e.Item.Tag as string) == "primary") ? Color.White : Pop(e.Item) ? Theme.Slate : Theme.Ink; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (!Pop(e.Item)) { base.OnRenderItemCheck(e); return; }
            var r = e.ImageRectangle; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, a = Math.Min(r.Width, r.Height) * 0.3f;
            using (var pen = new Pen(Theme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) e.Graphics.DrawLines(pen, new[] { new PointF(cx - a, cy), new PointF(cx - a * 0.25f, cy + a * 0.8f), new PointF(cx + a, cy - a * 0.8f) });
        }
        protected override void Initialize(ToolStrip toolStrip) { base.Initialize(toolStrip); var dd = toolStrip as ToolStripDropDown; if (dd != null && !(dd is ToolStripOverflow)) MenuFx.Attach(dd); }
    }

    // rounded corners and a quick fade-in for every pop-up menu
    static class MenuFx
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripDropDown, object> done = new System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripDropDown, object>();
        public static void Attach(ToolStripDropDown dd)
        {
            object o; if (done.TryGetValue(dd, out o)) return; done.Add(dd, new object());
            dd.DropShadowEnabled = true; dd.BackColor = Theme.MenuBack; dd.Padding = new Padding(0, Dpi.S(4), 0, Dpi.S(4));
            Action round = delegate { try { using (var p = Theme.RoundRect(new RectangleF(0, 0, dd.Width, dd.Height), Dpi.F(10))) dd.Region = new Region(p); } catch { } };
            dd.SizeChanged += delegate { round(); };
            dd.Opening += delegate { foreach (ToolStripItem it in dd.Items) { var mi = it as ToolStripMenuItem; if (mi != null && mi.Padding.Vertical < Dpi.S(8)) mi.Padding = Dpi.P(6, 5, 6, 5); } };
            dd.Opened += delegate
            {
                round(); dd.Opacity = 0.0;
                var t = new System.Windows.Forms.Timer { Interval = 15 }; int n = 0;
                t.Tick += delegate { n++; try { dd.Opacity = Math.Min(0.98, n * 0.2); } catch { } if (n >= 5 || !dd.Visible) { try { dd.Opacity = 0.98; } catch { } t.Stop(); t.Dispose(); } };
                t.Start();
            };
            round();
        }
    }

    class ThemeColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color MenuBorder { get { return Theme.Rule; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Sheet; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Sheet; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Sheet; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Sheet; } }
        public override Color SeparatorDark { get { return Theme.Rule; } }
    }
}
