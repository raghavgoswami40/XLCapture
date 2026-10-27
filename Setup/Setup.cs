// XLCapture Setup: one file that installs XLCapture for the person running it (no administrator rights needed), and takes it away again.
//   XLCapture-Setup.exe                 asks, then installs
//   XLCapture-Setup.exe /S              silent install        (/D=C:\folder  install folder, /NODESKTOP, /NOSTART, /NOLAUNCH)
//   Uninstall.exe                       in the install folder; also in Windows Settings > Apps
//   Uninstall.exe /S                    silent uninstall
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace XLCaptureSetup
{
    static class Info
    {
        public const string Name = "XLCapture", Publisher = "XLYOUR";
        public static string Version { get { return ReadText("version.txt").Trim(); } }
        public static string DefaultFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name); } }
        public static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"); } }
        public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"); } }
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\XLCapture", RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
        public static string ReadText(string name) { using (var s = Resource(name)) { if (s == null) return ""; using (var r = new StreamReader(s)) return r.ReadToEnd(); } }
    }

    class Options { public string Folder = Info.DefaultFolder; public bool Desktop = true, Startup = false, Launch = true; }

    static class Installer
    {
        // .NET Framework 4.8 or newer is part of Windows 10 (1903 and later) and Windows 11
        public static bool HasDotNet48()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")) { object v = k == null ? null : k.GetValue("Release"); return v != null && Convert.ToInt32(v) >= 528040; } }
            catch { return false; }
        }
        // stops copies of XLCapture that run from this folder (never one that runs from somewhere else)
        public static void StopRunning(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            foreach (var p in Process.GetProcessesByName("XLCapture"))
            {
                try
                {
                    string exe = p.MainModule.FileName; if (!exe.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!p.CloseMainWindow() || !p.WaitForExit(3000)) { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
            }
        }

        static void Link(string lnk, string target, string args, string icon, string where)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
            object l = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk }); var lt = l.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, l, new object[] { target });
            lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, l, new object[] { args });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, l, new object[] { where });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, l, new object[] { icon });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, l, new object[] { "Capture, record and annotate. ClickBot repeats a process for you." });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, l, null);
        }

        // returns null when it worked, otherwise what went wrong
        public static string Install(Options o, Action<string> say)
        {
            try
            {
                if (!HasDotNet48()) return "XLCapture needs .NET Framework 4.8, which comes with Windows 10 (May 2019 update or later) and Windows 11. Update Windows, then run this setup again.";
                say("Closing XLCapture if it is open");
                Directory.CreateDirectory(o.Folder); StopRunning(o.Folder);
                string exe = Path.Combine(o.Folder, "XLCapture.exe"), uninstaller = Path.Combine(o.Folder, "Uninstall.exe");

                say("Copying the program");
                if (File.Exists(exe)) { string old = exe + ".old"; try { if (File.Exists(old)) File.Delete(old); File.Move(exe, old); } catch { } }
                using (var s = Info.Resource("XLCapture.exe")) using (var f = new FileStream(exe, FileMode.Create)) s.CopyTo(f);
                if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase)) File.Copy(Application.ExecutablePath, uninstaller, true);
                try { File.Delete(exe + ".old"); } catch { }

                say("Adding shortcuts");
                Link(Info.StartMenuLink, exe, "", exe + ",0", o.Folder);
                if (o.Desktop) Link(Info.DesktopLink, exe, "", exe + ",0", o.Folder); else { try { File.Delete(Info.DesktopLink); } catch { } }
                using (var k = Registry.CurrentUser.CreateSubKey(Info.RunKey)) { if (o.Startup) k.SetValue("XLCapture", "\"" + exe + "\" --tray"); else k.DeleteValue("XLCapture", false); }

                say("Registering with Windows");
                using (var k = Registry.CurrentUser.CreateSubKey(Info.UninstallKey))
                {
                    k.SetValue("DisplayName", Info.Name); k.SetValue("DisplayVersion", Info.Version); k.SetValue("Publisher", Info.Publisher);
                    k.SetValue("InstallLocation", o.Folder); k.SetValue("DisplayIcon", exe); k.SetValue("UninstallString", "\"" + uninstaller + "\"");
                    k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /S"); k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    try { k.SetValue("EstimatedSize", (int)((new FileInfo(exe).Length + new FileInfo(uninstaller).Length) / 1024), RegistryValueKind.DWord); } catch { }
                }

                say("Setting up ClickBot's Claude skill");
                try { var p = Process.Start(new ProcessStartInfo(exe, "--install-skill") { UseShellExecute = false, CreateNoWindow = true }); if (p != null) p.WaitForExit(15000); } catch { }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static string Uninstall(string folder, Action<string> say)
        {
            try
            {
                StopRunning(folder);
                say("Removing shortcuts"); try { File.Delete(Info.StartMenuLink); } catch { } try { File.Delete(Info.DesktopLink); } catch { }
                using (var k = Registry.CurrentUser.OpenSubKey(Info.RunKey, true)) { if (k != null) k.DeleteValue("XLCapture", false); }
                Registry.CurrentUser.DeleteSubKeyTree(Info.UninstallKey, false);
                say("Removing the program");
                string self = Path.GetFullPath(Application.ExecutablePath);
                foreach (var f in Directory.GetFiles(folder)) { if (string.Equals(Path.GetFullPath(f), self, StringComparison.OrdinalIgnoreCase)) continue; try { File.Delete(f); } catch { } }
                // the running uninstaller deletes itself, and the folder, a moment after it has closed
                bool runningHere = self.StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
                if (runningHere) Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\" & rmdir \"" + folder + "\"") { CreateNoWindow = true, UseShellExecute = false });
                else { try { Directory.Delete(folder, false); } catch { } }
                // the Claude skill XLCapture put in the person's Claude folder
                foreach (string name in new[] { "snaplite-teach", "xlcapture-slides" })
                    try
                    {
                        string skill = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "skills", name);
                        string md = Path.Combine(skill, "SKILL.md"); if (File.Exists(md) && File.ReadAllText(md).Contains("xlcapture-skill:")) Directory.Delete(skill, true);
                    }
                    catch { }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool silent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase)), uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            var o = new Options();
            foreach (var a in args)
            {
                if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) o.Folder = a.Substring(3).Trim('"');
                else if (a.Equals("/NODESKTOP", StringComparison.OrdinalIgnoreCase)) o.Desktop = false;
                else if (a.Equals("/NOLAUNCH", StringComparison.OrdinalIgnoreCase)) o.Launch = false;
                else if (a.Equals("/STARTUP", StringComparison.OrdinalIgnoreCase)) o.Startup = true;
            }
            bool isUninstaller = Path.GetFileName(Application.ExecutablePath).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase);
            if (isUninstaller || uninstall)
            {
                string folder = isUninstaller ? Path.GetDirectoryName(Application.ExecutablePath) : o.Folder;
                if (!silent && MessageBox.Show("Remove XLCapture from this PC?\r\n\r\nYour captures, recordings and sessions are kept.", "XLCapture Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                string err = Installer.Uninstall(folder, delegate { });
                if (!silent) MessageBox.Show(err == null ? "XLCapture was removed. Your captures and sessions were kept." : "Not everything could be removed: " + err, "XLCapture Setup");
                return err == null ? 0 : 1;
            }
            if (silent) { string err = Installer.Install(o, delegate { }); if (err == null && o.Launch) Launch(o); return err == null ? 0 : 1; }
            Application.Run(new SetupForm(o)); return 0;
        }
        public static void Launch(Options o) { try { Process.Start(new ProcessStartInfo(Path.Combine(o.Folder, "XLCapture.exe")) { WorkingDirectory = o.Folder }); } catch { } }
    }

    // the window: a dark band with the logo, what will happen, two ticks, Install
    class SetupForm : Form
    {
        static readonly Color Dark = Color.FromArgb(0x15, 0x15, 0x15), Gold = Color.FromArgb(0xFF, 0xC3, 0x00), Ink = Color.FromArgb(0x1B, 0x1B, 0x1B), Slate = Color.FromArgb(0x5F, 0x6B, 0x7A);
        readonly Options o; readonly CheckBox desktop, startup, launch; readonly Button go, close; readonly Label title, body, status; readonly TextBox folder; readonly Panel band; Image logo; bool busy, finished;

        public SetupForm(Options o)
        {
            this.o = o; Text = "XLCapture Setup"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = true; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 440); BackColor = Color.White; Font = new Font("Segoe UI", 10f);
            try { using (var s = Info.Resource("setup.ico")) if (s != null) Icon = new Icon(s); } catch { }
            try { using (var s = Info.Resource("logo-full.png")) if (s != null) logo = Image.FromStream(s); } catch { }
            band = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = Dark }; band.Paint += delegate(object s, PaintEventArgs e)
            {
                if (logo == null) return; e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; int h = 70, w = (int)(h * (float)logo.Width / logo.Height); e.Graphics.DrawImage(logo, 36, (band.Height - h) / 2, w, h);
            };
            title = new Label { Text = "Install XLCapture " + Info.Version, Font = new Font("Segoe UI Semibold", 15f), ForeColor = Ink, Location = new Point(36, 140), Size = new Size(490, 34) };
            body = new Label { Text = "Capture the screen, record video, mark up screenshots, and let ClickBot repeat a process for you. XLCapture is installed for you only; no administrator rights are needed.", ForeColor = Slate, Location = new Point(36, 178), Size = new Size(490, 60) };
            var fl = new Label { Text = "Install to", ForeColor = Slate, Location = new Point(36, 248), AutoSize = true };
            folder = new TextBox { Text = o.Folder, Location = new Point(36, 270), Width = 380, BorderStyle = BorderStyle.FixedSingle };
            var browse = new Button { Text = "Browse...", Location = new Point(426, 268), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat };
            browse.Click += delegate { using (var d = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Choose the folder to install XLCapture in" }) if (d.ShowDialog(this) == DialogResult.OK) folder.Text = Path.Combine(d.SelectedPath, d.SelectedPath.EndsWith("XLCapture", StringComparison.OrdinalIgnoreCase) ? "" : "XLCapture").TrimEnd('\\'); };
            desktop = new CheckBox { Text = "Put a shortcut on the desktop", Checked = true, Location = new Point(36, 314), AutoSize = true };
            startup = new CheckBox { Text = "Start XLCapture when I sign in to Windows", Checked = false, Location = new Point(36, 342), AutoSize = true };
            launch = new CheckBox { Text = "Open XLCapture when setup finishes", Checked = true, Location = new Point(36, 370), AutoSize = true };
            status = new Label { ForeColor = Slate, Location = new Point(36, 404), Size = new Size(250, 24) };
            go = new PillButton { Text = "Install", Primary = true, Location = new Point(300, 392), Size = new Size(110, 38) }; close = new PillButton { Text = "Cancel", Primary = false, Location = new Point(418, 392), Size = new Size(108, 38) };
            go.Click += delegate { if (finished) { if (launch.Checked) Program.Launch(o); Close(); } else Run(); }; close.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { band, title, body, fl, folder, browse, desktop, startup, launch, status, go, close }); AcceptButton = go;
            if (!Installer.HasDotNet48()) { status.ForeColor = Color.FromArgb(0xB0, 0x20, 0x20); status.Text = "This PC needs .NET Framework 4.8."; }
        }

        // a rounded gold (or outlined) button
        static Button Style(Button b, bool primary) { return b; }
        void Run()
        {
            if (busy) return; busy = true; go.Enabled = false; close.Enabled = false;
            o.Folder = folder.Text.Trim(); o.Desktop = desktop.Checked; o.Startup = startup.Checked; o.Launch = launch.Checked;
            foreach (Control c in new Control[] { folder, desktop, startup, launch }) c.Enabled = false;
            var t = new System.Threading.Thread(delegate()
            {
                string err = Installer.Install(o, delegate(string s) { try { BeginInvoke((Action)delegate { status.ForeColor = Slate; status.Text = s + "..."; }); } catch { } });
                BeginInvoke((Action)delegate { Done(err); });
            }) { IsBackground = true }; t.Start();
        }

        void Done(string err)
        {
            busy = false; close.Enabled = true;
            if (err != null) { title.Text = "Setup could not finish"; body.Text = err; status.Text = ""; go.Text = "Try again"; go.Enabled = true; foreach (Control c in new Control[] { folder, desktop, startup, launch }) c.Enabled = true; return; }
            title.Text = "XLCapture is installed"; body.Text = "Find it in the Start menu" + (o.Desktop ? " and on the desktop" : "") + ". To remove it later, use Windows Settings > Apps, or Uninstall in " + o.Folder + ".\r\n\r\nClickBot's \"Teach with Claude\" also needs Claude Code on this PC, signed in.";
            status.Text = ""; go.Text = "Finish"; go.Enabled = true; close.Visible = false; launch.Enabled = true; folder.Visible = false; desktop.Visible = false; startup.Visible = false;
            foreach (Control c in Controls) if (c is Label && c != title && c != body && c != status) c.Visible = false;
            foreach (Control c in Controls) if (c is Button && c.Text == "Browse...") c.Visible = false;
            launch.Location = new Point(36, 270);
            finished = true;
        }
    }

    class PillButton : Button
    {
        static readonly Color Gold = Color.FromArgb(0xFF, 0xC3, 0x00), Ink = Color.FromArgb(0x1B, 0x1B, 0x1B), Off = Color.FromArgb(0xD5, 0xDA, 0xDF);
        public bool Primary; bool hover;
        public PillButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; Font = new Font("Segoe UI Semibold", 10f); TabStop = true; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent != null ? Parent.BackColor : Color.White);
            var r = new RectangleF(1, 1, Width - 3, Height - 3); float d = r.Height; var p = new GraphicsPath(); p.AddArc(r.X, r.Y, d, d, 90, 180); p.AddArc(r.Right - d, r.Y, d, d, 270, 180); p.CloseFigure();
            using (p)
            {
                if (Primary) using (var br = new SolidBrush(!Enabled ? Off : hover ? Color.FromArgb(0xFF, 0xD3, 0x40) : Gold)) g.FillPath(br, p);
                else if (hover && Enabled) using (var br = new SolidBrush(Color.FromArgb(0xFF, 0xF3, 0xC4))) g.FillPath(br, p);
                using (var pen = new Pen(Primary ? (Enabled ? Gold : Off) : (Enabled ? Ink : Off), 1.6f)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), Enabled ? Ink : Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
