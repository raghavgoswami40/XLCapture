// The name and the logos. The pictures are built into the program (see build.ps1), so there is nothing to ship beside it.
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SnapLite
{
    class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "About " + Brand.Name; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.Body(10f); ClientSize = Dpi.S(520, 400);
            var top = new Panel { Dock = DockStyle.Top, Height = Dpi.S(190), BackColor = Brand.Dark };
            top.Paint += delegate(object s, PaintEventArgs e)
            {
                var f = Brand.Full; if (f == null) return;
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                int h = Dpi.S(130), w = (int)(h * (float)f.Width / f.Height); e.Graphics.DrawImage(f, (top.Width - w) / 2, (top.Height - h) / 2, w, h);
            };
            var title = new Label { Text = Brand.Name, AutoSize = false, Location = new Point(Dpi.S(32), Dpi.S(212)), Size = Dpi.S(456, 32), Font = Theme.Semi(17f), ForeColor = Theme.Ink };
            var text = new Label { AutoSize = false, Location = new Point(Dpi.S(32), Dpi.S(250)), Size = Dpi.S(456, 90), ForeColor = Theme.Slate,
                Text = "A local capture, recording and annotation tool, with ClickBot to repeat a recorded process for you.\r\n\r\nNo accounts, no uploads unless you choose FTP or email." };
            var ok = new FlatButton("Close", true) { Location = new Point(Dpi.S(388), Dpi.S(346)), Size = Dpi.S(100, 38), DialogResult = DialogResult.OK };
            Controls.AddRange(new Control[] { top, title, text, ok }); AcceptButton = ok; CancelButton = ok;
        }
    }

    static class Brand
    {
        public const string Name = "XLCapture", EditorName = "XLCapture Editor";
        public const string Version = "1.0.0";                                  // shown to people and used to name the installer
        public static readonly Color Dark = Color.FromArgb(21, 21, 21);          // the logos' own background
        public static readonly Color Gold = Color.FromArgb(255, 195, 0);          // the logos' yellow
        static Bitmap mark, full; static Icon icon;

        static Stream Res(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
        static Bitmap Load(string name) { using (var s = Res(name)) { if (s == null) return null; using (var im = Image.FromStream(s)) return new Bitmap(im); } }
        // the logos are shared pictures: draw them, do not dispose them
        public static Bitmap Mark { get { return mark ?? (mark = Load("XLCapture.logo-mark.png")); } }
        public static Bitmap Full { get { return full ?? (full = Load("XLCapture.logo-full.png")); } }
        public static Icon Icon
        {
            get
            {
                if (icon != null) return icon;
                using (var s = Res("XLCapture.icon.ico")) if (s != null) icon = new Icon(s);
                return icon;
            }
        }
        // the short logo on a dark rounded tile of any size, for places that are not dark themselves
        public static Bitmap MarkTile(int size)
        {
            var b = new Bitmap(size, size); var m = Mark; if (m == null) return b;
            using (var g = System.Drawing.Graphics.FromImage(b))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                using (var p = Theme.RoundRect(new RectangleF(0, 0, size - 1, size - 1), size * 0.2f)) using (var br = new SolidBrush(Dark)) g.FillPath(br, p);
                float pad = size * 0.10f, avail = size - pad * 2, k = Math.Min(avail / m.Width, avail / m.Height), w = m.Width * k, h = m.Height * k;
                g.DrawImage(m, (size - w) / 2, (size - h) / 2, w, h);
            }
            return b;
        }
    }
}

