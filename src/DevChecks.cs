#if DEVCHECKS
// Command-line checks used while developing (not part of normal use).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    class PixelProbe : Control
    {
        public PixelProbe() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.FromArgb(0, 120, 255)); }
    }

    class ProbeButton : Button
    {
        public ProbeButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.FromArgb(0, 200, 0)); }
    }
    // paints a little at a time, to find which step leaves the stray line: 1 background, 2 + circle, 3 + text, 4 + cursor and fonts set as the real controls do
    class ProbeStaged : RadioButton
    {
        readonly int stage;
        public ProbeStaged(int stage)
        {
            this.stage = stage; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            if (stage >= 4) { Cursor = Theme.Hand; AutoSize = false; Height = 28; Font = Theme.Semi(10f); ForeColor = Theme.Ink; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Color.White)) g.FillRectangle(bg, ClientRectangle);
            if (stage >= 2) { using (var pen = new Pen(Color.Black, 1.8f)) g.DrawEllipse(pen, 1.5f, 5, 20, 20); }
            if (stage >= 3) TextRenderer.DrawText(g, Text, Font, new Rectangle(30, 0, Width - 30, Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
    class ProbeRadio : RadioButton
    {
        public ProbeRadio() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.FromArgb(0, 200, 0)); }
    }

    static class DevChecks
    {
        static readonly string Me = System.Diagnostics.Process.GetCurrentProcess().ProcessName;     // the program's own process name, for finding its own test windows
        // open the editor on an image, add one of each annotation, save a screenshot of the window
        // a picture of the form as it is on screen (controls that paint themselves over the system's, like the drop-down boxes, do not show in DrawToBitmap)
        static Bitmap Shot(Form f)
        {
            var b = new Bitmap(f.Width, f.Height);
            try
            {
                bool top = f.TopMost; f.TopMost = true; f.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(250); Application.DoEvents();
                using (var g = Graphics.FromImage(b)) g.CopyFromScreen(f.Location, Point.Empty, f.Size);
                f.TopMost = top;
            }
            catch { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); }
            return b;
        }

        public static int EditorShot(string[] args)
        {
            if (args.Length > 3) Settings.Folder = args[3];
            var doc = EditorForm.LoadAny(args[1]);
            var f = EditorForm.Show(doc); f.StartPosition = FormStartPosition.Manual; f.Location = new Point(40, 40); Application.DoEvents();
            f.SelectTool(Tool.Select);
            doc.Objs.Add(new ShapeObj { Kind = ShapeKind.Rounded, A = new PointF(60, 60), B = new PointF(300, 160), Stroke = Theme.Mark, Width = 4 });
            doc.Objs.Add(new LineObj { A = new PointF(340, 220), B = new PointF(520, 120), Width = 5, Stroke = Theme.Cobalt });
            doc.Objs.Add(new StepObj { A = new PointF(80, 220), B = new PointF(122, 262), Number = 1 });
            doc.Objs.Add(new StepObj { A = new PointF(140, 220), B = new PointF(182, 262), Number = 2 });
            var co = new CalloutObj { A = new PointF(360, 300), B = new PointF(600, 370), Text = "Check this setting", Tail = new PointF(-60, 70) };
            doc.Objs.Add(co);
            var tx = new TextObj { A = new PointF(80, 330), Text = "Light table editor", Px = 30 }; tx.Measure(); doc.Objs.Add(tx);
            doc.Objs.Add(new BlurObj { A = new PointF(620, 60), B = new PointF(820, 140) });
            doc.Objs.Add(new MagnifyObj { A = new PointF(640, 200), B = new PointF(780, 320), Zoom = 2.5f });
            doc.Raise();
            f.Canvas.Sel = new List<Obj> { co }; f.Canvas.Style.Raise();
            f.SelectTool(Tool.Select); f.Canvas.Sel = new List<Obj> { co };
            f.Fit(); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            f.TopMost = true; f.Activate(); f.RefreshTray(); Application.DoEvents(); System.Threading.Thread.Sleep(1200); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            using (var bmp = Shot(f)) bmp.Save(args[2], ImageFormat.Png);
            f.SelectTool(Tool.Arrow); Application.DoEvents(); System.Threading.Thread.Sleep(400); Application.DoEvents();
            using (var bmp = Shot(f)) bmp.Save(args[2].Replace(".png", "-arrow.png"), ImageFormat.Png);
            foreach (var o in doc.Objs) { } doc.Dirty = false;
            return 0;
        }

        // synthetic clip -> MJPEG AVI -> MP4 (Windows media composer), plus a GIF; reports sizes and errors
        public static int VideoTest(string[] args)
        {
            string dir = Path.GetDirectoryName(args[1]); var log = new System.Text.StringBuilder();
            int w = 640, h = 360, fps = 15; string avi = Path.Combine(dir, "t.avi"), mp4 = Path.Combine(dir, "t.mp4"), gif = Path.Combine(dir, "t.gif");
            try
            {
                var aw = new AviWriter(avi, w, h, fps); var frames = new List<byte[]>();
                for (int i = 0; i < 45; i++)
                {
                    using (var b = new Bitmap(w, h)) using (var g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.FromArgb(20 + i * 4, 40, 200 - i * 3));
                        g.FillEllipse(Brushes.Gold, 20 + i * 12, 120 + (int)(60 * Math.Sin(i / 4.0)), 80, 80);
                        using (var f = new Font("Segoe UI", 28f)) g.DrawString("frame " + i, f, Brushes.White, 20, 20);
                        aw.Add(aw.Encode(b));
                        int st; var d = Effects.Bytes(b, out st); if (i % 5 == 0) frames.Add(d);
                    }
                }
                aw.Finish(); log.AppendLine("avi bytes=" + new FileInfo(avi).Length);
                string err; bool ok = MediaBridge.Mp4(avi, null, mp4, w, h, fps, null, out err);
                log.AppendLine("mp4 ok=" + ok + " err=" + err + " bytes=" + (File.Exists(mp4) ? new FileInfo(mp4).Length : -1));
                if (ok) { try { log.AppendLine("mp4 duration=" + MediaBridge.Duration(mp4).TotalSeconds.ToString("0.00") + "s"); } catch (Exception ex) { log.AppendLine("duration err " + ex.Message); } }
                using (var fs = new FileStream(gif, FileMode.Create))
                {
                    var gw = new GifWriter(fs, w, h, frames);
                    foreach (var f in frames) gw.AddFrame(f, 12);
                    gw.Finish();
                }
                log.AppendLine("gif bytes=" + new FileInfo(gif).Length);
                using (var im = Image.FromFile(gif)) log.AppendLine("gif frames=" + im.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time) + " size=" + im.Width + "x" + im.Height);
            }
            catch (Exception ex) { log.AppendLine("EXCEPTION " + ex); }
            File.WriteAllText(args[1], log.ToString());
            return 0;
        }

        // capture window with the presets list open, plus the hotkey dialog
        public static int PresetsShot(string[] args)
        {
            var hub = new Hub(); hub.StartPosition = FormStartPosition.Manual; hub.Location = new Point(40, 40); hub.Show(); Application.DoEvents();
            hub.SetTab("image"); hub.TogglePresets(); Application.DoEvents(); System.Threading.Thread.Sleep(600); Application.DoEvents();
            using (var bmp = Shot(hub)) bmp.Save(args[1] + "-presets.png", ImageFormat.Png);
            var dlg = (Form)typeof(HotkeyDialog).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0].Invoke(new object[] { "Record session", "Ctrl+Shift+R", (Func<string, string>)(k => k == "Ctrl+Shift+1" ? "Region" : null) });
            dlg.StartPosition = FormStartPosition.Manual; dlg.Location = new Point(60, 60); dlg.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(300);
            using (var bmp = Shot(dlg)) bmp.Save(args[1] + "-hotkey.png", ImageFormat.Png);
            dlg.Close(); hub.Quit(); return 0;
        }

        // window picking on the live desktop: what the overlay highlights at a few points, before and after UI Automation panes arrive
        public static int WindowPick(string[] args)
        {
            var log = new System.Text.StringBuilder(); var vs = SystemInformation.VirtualScreen;
            var wins = Native.Windows(); log.AppendLine(wins.Count + " pickable windows:");
            foreach (var w in wins.Take(12)) log.AppendLine("   " + Native.ClassOf(w.Handle) + " | " + w.Title + " | " + w.Rect);
            using (var shot = Native.GrabVirtualScreen(false)) using (var ov = new PickOverlay(shot, CapMode.Window))
            {
                ov.Show(); Application.DoEvents();
                var prim = Screen.PrimaryScreen.Bounds; var pts = new[] { new Point(prim.X - vs.X + prim.Width / 2, prim.Y - vs.Y + prim.Height / 2), new Point(prim.X - vs.X + 200, prim.Y - vs.Y + 600), new Point(prim.X - vs.X + prim.Width - 300, prim.Y - vs.Y + 500) };
                int n = 0;
                foreach (var phase in new[] { "before panes", "after panes" })
                {
                    foreach (var pt in pts) { var r = ov.HoverAt(pt); log.AppendLine(phase + " at " + pt + ": " + r + " (" + r.Width + "x" + r.Height + ")"); }
                    if (n++ == 0) { var t0 = DateTime.Now; while ((DateTime.Now - t0).TotalSeconds < 8) { Application.DoEvents(); System.Threading.Thread.Sleep(30); } }
                }
                for (int i = 0; i < pts.Length; i++) { ov.HoverAt(pts[i]); using (var b = ov.Snapshot(pts[i], false, false)) using (var sm = new Bitmap(b, b.Width / 4, b.Height / 4)) sm.Save(args[1] + "-pt" + i + ".png", ImageFormat.Png); }
                ov.Close();
            }
            File.WriteAllText(args[1] + ".txt", log.ToString()); return 0;
        }

        public static int HubShot(string[] args)
        {
            string sample = args.Length > 3 ? args[3] : null;
            if (sample != null) { Settings.Folder = sample; }
            var hub = new Hub(); hub.StartPosition = FormStartPosition.Manual; hub.Location = new Point(40, 40); hub.Show(); Application.DoEvents();
            System.Threading.Thread.Sleep(400); Application.DoEvents();
            hub.TopMost = true; hub.SetTab("image"); hub.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(500); Application.DoEvents();
            using (var bmp = Shot(hub)) bmp.Save(args[1], ImageFormat.Png);
            hub.SetTab("video"); Application.DoEvents(); System.Threading.Thread.Sleep(400); Application.DoEvents();
            using (var bmp = Shot(hub)) bmp.Save(args[1].Replace(".png", "-video.png"), ImageFormat.Png);
            hub.SetTab("aio"); Application.DoEvents(); System.Threading.Thread.Sleep(400); Application.DoEvents();
            using (var bmp = Shot(hub)) bmp.Save(args[1].Replace(".png", "-aio.png"), ImageFormat.Png);
            hub.SetTab("image"); hub.TopMost = false;
            if (args.Length > 2)
            {
                try { ShotLib(args); } catch (Exception ex) { File.WriteAllText(args[2] + ".err", ex.ToString()); }
            }
            hub.Quit(); return 0;
        }
        static void ShotLib(string[] args)
        {
            {
                var lib = new LibraryForm(); lib.StartPosition = FormStartPosition.Manual; lib.Location = new Point(60, 60); lib.Show(); Application.DoEvents();
                System.Threading.Thread.Sleep(900); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
                lib.TopMost = true; lib.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(500); Application.DoEvents();
                using (var bmp = Native.Grab(lib.Bounds, false)) bmp.Save(args[2], ImageFormat.Png);
                File.WriteAllText(args[2] + ".txt", lib.DebugBounds());
                lib.Close();
            }
        }

        // record a real screen region for a few seconds as MP4 then GIF, using the app's own recorder
        public static int RecTest(string[] args)
        {
            Settings.Folder = args[2]; Directory.CreateDirectory(Settings.Folder);
            var hub = new Hub(); hub.Show(); Application.DoEvents();
            var log = new System.Text.StringBuilder(); log.AppendLine("mic available=" + Mic.Available);
            Settings.VidCamera = Camera.Available; Settings.VidSysAudio = SystemAudio.Available; Settings.VidHighlight = true; Settings.VidClick = true; log.AppendLine("camera=" + Settings.VidCamera + " sys=" + Settings.VidSysAudio);
            foreach (bool gif in new[] { false, true })
            {
                var before = Directory.GetFiles(Settings.Folder).ToList();
                Recorder.Start(new Rectangle(200, 200, 640, 360), gif);
                var t0 = DateTime.Now; while ((DateTime.Now - t0).TotalSeconds < 3.2) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                Recorder.Stop();
                var t1 = DateTime.Now; string found = null;
                while ((DateTime.Now - t1).TotalSeconds < 60 && !Application.OpenForms.Cast<Form>().Any(f => f.Text == "Recording saved")) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
                found = Directory.GetFiles(Settings.Folder).Except(before).FirstOrDefault(f => !f.EndsWith(".tmp"));
                log.AppendLine((gif ? "gif" : "video") + " -> " + (found == null ? "NOT CREATED" : Path.GetFileName(found) + " " + new FileInfo(found).Length + " bytes"));
                if (found != null && found.EndsWith(".mp4")) { try { log.AppendLine("  duration " + MediaBridge.Duration(found).TotalSeconds.ToString("0.0") + "s"); } catch (Exception ex) { log.AppendLine("  " + ex.Message); } }
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList()) if (f.Text == "Recording saved") f.Close();
            }
            File.WriteAllText(args[1], log.ToString()); hub.Quit(); return 0;
        }

        // project round-trip and image operations with undo
        public static int DocTest(string[] args)
        {
            var log = new System.Text.StringBuilder();
            Func<string, bool, bool> chk = delegate(string n, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + n); return ok; };
            var src = new Bitmap(400, 300); using (var g = Graphics.FromImage(src)) { g.Clear(Color.White); g.FillRectangle(Brushes.SteelBlue, 50, 40, 120, 80); }
            var doc = new Doc(src) { Name = "t" };
            var logo = new Bitmap(40, 40); using (var g = Graphics.FromImage(logo)) g.Clear(Color.Orange);
            doc.Objs.Add(new ShapeObj { A = new PointF(10, 10), B = new PointF(120, 90), Stroke = Color.Red, Width = 4 });
            doc.Objs.Add(new LineObj { A = new PointF(200, 200), B = new PointF(300, 120) });
            var pen = new PenObj(); pen.Pts.AddRange(new[] { new PointF(5, 5), new PointF(50, 60), new PointF(90, 20) }); doc.Objs.Add(pen);
            var tx = new TextObj { A = new PointF(150, 30), Text = "hello" }; tx.Measure(); doc.Objs.Add(tx);
            doc.Objs.Add(new ImageObj { Img = logo, A = new PointF(300, 20), B = new PointF(340, 60) });
            doc.Objs.Add(new StepObj { A = new PointF(20, 200), B = new PointF(50, 230), Number = 4 });
            string path = Path.Combine(Path.GetDirectoryName(args[1]), "t.snapl");
            Project.Save(doc, path);
            var back = Project.Load(path);
            chk("project keeps object count (" + back.Objs.Count + ")", back.Objs.Count == 6);
            chk("project keeps types", string.Join(",", back.Objs.Select(o => o.GetType().Name)) == string.Join(",", doc.Objs.Select(o => o.GetType().Name)));
            using (var a = doc.Render()) using (var b = back.Render()) { int s1, s2; var x = Effects.Bytes(a, out s1); var y = Effects.Bytes(b, out s2); long diff = 0; for (int i = 0; i < x.Length; i++) diff += Math.Abs(x[i] - y[i]); chk("project renders identically (diff " + diff + ")", diff == 0); }
            chk("image object survived", ((ImageObj)back.Objs[4]).Img != null && ((ImageObj)back.Objs[4]).Img.Width == 40);
            // image ops
            doc.RotateCW(); chk("rotate swaps size (" + doc.Width + "x" + doc.Height + ")", doc.Width == 300 && doc.Height == 400);
            var step = (StepObj)doc.Objs[5]; chk("objects turn with the picture (step rotation " + step.Rotation + ")", Math.Abs(step.Rotation - 90) < 0.1);
            doc.Crop(new Rectangle(10, 10, 200, 100)); chk("crop (" + doc.Width + "x" + doc.Height + ")", doc.Width == 200 && doc.Height == 100);
            doc.Resize(100, 50); chk("resize (" + doc.Width + "x" + doc.Height + ")", doc.Width == 100 && doc.Height == 50);
            doc.CutBand(false, 20, 30); chk("cut band (" + doc.Width + "x" + doc.Height + ")", doc.Width == 70);
            doc.Undo(); doc.Undo(); doc.Undo(); chk("3 undos return to the rotated state (" + doc.Width + "x" + doc.Height + ")", doc.Width == 300 && doc.Height == 400);
            doc.Undo(); doc.Undo(); chk("undo to original (" + doc.Width + "x" + doc.Height + ")", doc.Width == 400 && doc.Height == 300);
            doc.Redo(); chk("redo rotate", doc.Width == 300);
            // effects
            using (var e1 = Effects.Border(src, 10, Color.Black)) chk("border grows canvas", e1.Width == 420);
            using (var e2 = Effects.DropShadow(src, 6, 6, 8, Color.Black, 50)) chk("shadow grows canvas (" + e2.Width + ")", e2.Width > 400);
            using (var e3 = Effects.Adjust(src, 20, 10, -30, 15, 1.1f)) chk("adjust keeps size", e3.Width == 400);
            using (var e4 = Effects.GaussianBlur(src, 5)) chk("blur keeps size", e4.Height == 300);
            using (var e5 = Effects.Perspective(src, new PointF(40, 0), new PointF(360, 0), new PointF(400, 300), new PointF(0, 300), 400, 300)) chk("perspective keeps size", e5.Width == 400);
            Rectangle ch; using (var e6 = Effects.FloodFill(src, new Point(5, 5), Color.Lime, 10, out ch)) chk("flood fill finds region (" + ch + ")", ch.Width > 100);
            using (var e7 = Effects.Edge(src, EdgeKind.Torn, 12, 1)) chk("torn edge keeps size", e7.Width == 400);
            var mask = Effects.WandMask(src, new Point(60, 50), 10, true); chk("wand selects the blue block", mask.Count(m => m) == 120 * 80);
            // pdf structure
            string pdf = Path.Combine(Path.GetDirectoryName(args[1]), "t.pdf"); Pdf.Write(pdf, new[] { src, logo }, 85);
            var pb = File.ReadAllBytes(pdf); var ptxt = System.Text.Encoding.ASCII.GetString(pb);
            chk("pdf has 2 pages + xref + EOF", ptxt.Contains("/Count 2") && ptxt.Contains("startxref") && ptxt.TrimEnd().EndsWith("%%EOF"));
            File.WriteAllText(args[1], log.ToString());
            return 0;
        }

        // render the capture overlay in three states from a picture standing in for the screen
        public static int OverlayShot(string[] args)
        {
            var vs = SystemInformation.VirtualScreen;
            using (var src = new Bitmap(args[1]))
            using (var fake = new Bitmap(vs.Width, vs.Height))
            {
                using (var g = Graphics.FromImage(fake)) { g.Clear(Color.FromArgb(40, 60, 80)); g.DrawImage(src, 0, 0, vs.Width, vs.Height); }
                var prim = Screen.PrimaryScreen.Bounds; var c = new Point(prim.X - vs.X + prim.Width / 2, prim.Y - vs.Y + prim.Height / 2);
                using (var ov = new PickOverlay(fake, CapMode.AllInOne))
                {
                    using (var b = ov.Snapshot(c, false, false)) b.Save(args[2] + "-aio.png", ImageFormat.Png);
                    using (var b = ov.Snapshot(c, true, false)) b.Save(args[2] + "-fullscreen.png", ImageFormat.Png);
                    using (var b = ov.Snapshot(c, false, true)) b.Save(args[2] + "-help.png", ImageFormat.Png);
                }
            }
            return 0;
        }

        // drive a real window through UI Automation: learn a short process, save it, replay it with other values,
        // run it from a CSV, and do the same through the control channel. args: --driver-test <log> <folder>
        public static int DriverTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool ok = true;
            Action<bool, string> check = delegate(bool c, string m) { log.AppendLine((c ? "PASS  " : "FAIL  ") + m); if (!c) ok = false; };
            string dir = args[2]; Directory.CreateDirectory(dir); Settings.Folder = dir; SessionWriter.NoLibrary = true;

            Form form = null; TextBox box = null; Label result = null, pickLabel = null; ListBox pickList = null; var ready = new System.Threading.ManualResetEvent(false);
            var ui = new System.Threading.Thread(delegate()
            {
                form = new Form { Text = "SnapLite Driver Test", Width = 480, Height = 340, StartPosition = FormStartPosition.Manual, Location = new Point(120, 120) };
                box = new TextBox { Name = "txtName", AccessibleName = "Item name", Location = new Point(20, 20), Width = 300 };
                var add = new Button { Name = "btnAdd", Text = "Add item", Location = new Point(20, 60), Width = 120 };
                result = new Label { Name = "lblResult", AccessibleName = "Result", Location = new Point(20, 110), Width = 420 };
                add.Click += delegate { result.Text = "Added: " + box.Text; };
                pickList = new ListBox { Name = "lstPick", AccessibleName = "Pick one", Location = new Point(20, 140), Size = new Size(200, 110), IntegralHeight = false }; pickList.Items.AddRange(new object[] { "Alpha", "Beta", "Gamma" });
                pickLabel = new Label { Name = "lblPick", AccessibleName = "Picked", Location = new Point(240, 140), Width = 200 };
                pickList.SelectedIndexChanged += delegate { pickLabel.Text = "Picked: " + pickList.SelectedItem; };
                form.Controls.AddRange(new Control[] { box, add, result, pickList, pickLabel });
                form.Shown += delegate { ready.Set(); };
                Application.Run(form);
            });
            ui.SetApartmentState(System.Threading.ApartmentState.STA); ui.Start();
            if (!ready.WaitOne(10000)) { File.WriteAllText(args[1], "FAIL  the test window did not open"); return 1; }
            Func<string> read = delegate { return (string)form.Invoke((Func<string>)delegate { return result.Text; }); };
            Action reset = delegate { form.Invoke((Action)delegate { box.Text = ""; result.Text = ""; pickList.ClearSelected(); pickLabel.Text = ""; }); };
            Func<string> readPick = delegate { return (string)form.Invoke((Func<string>)delegate { return pickLabel.Text; }); };

            var work = new System.Threading.Thread(delegate()
            {
                try
                {
                    System.Threading.Thread.Sleep(600);
                    var proc = new Dictionary<string, object> { { "name", "Driver test" }, { "params", new Dictionary<string, object> { { "item_name", new Dictionary<string, object> { { "type", "text" }, { "default", "" } } } } } };
                    var w = new SessionWriter("Driver test", proc);
                    var win = new Target { Process = Me, Title = "SnapLite Driver Test" };
                    var t1 = new Target { Process = win.Process, Title = win.Title, Label = "Item name", Role = "Edit" };
                    var t2 = new Target { Process = win.Process, Title = win.Title, Label = "Add item", Role = "Button" };
                    var o1 = Driver.Perform(w, t1, "type", "Alpha", "item_name", "", new Driver.Options());
                    check(o1.Ok, "learning: typed into the field (" + (o1.Ok ? o1.Method : o1.Error) + ")");
                    log.AppendLine("      right after typing the box held: \"" + (string)form.Invoke((Func<string>)delegate { return box.Text; }) + "\"");
                    System.Threading.Thread.Sleep(1500);
                    log.AppendLine("      1.5 s later it held: \"" + (string)form.Invoke((Func<string>)delegate { return box.Text; }) + "\"");
                    var o2 = Driver.Perform(w, t2, "click", null, "", "", new Driver.Options());
                    check(o2.Ok, "learning: clicked the button (" + (o2.Ok ? o2.Method : o2.Error) + ")");
                    check(read() == "Added: Alpha", "the app received the typed value and the click (\"" + read() + "\")");
                    log.AppendLine("      textbox now: \"" + (string)form.Invoke((Func<string>)delegate { return box.Text; }) + "\"; foreground was test window: " + (Native.TitleOf(Native.GetForegroundWindow())));
                    string path = w.Save(); w.Dispose();
                    var doc = SessionDoc.Load(path);
                    check(doc.Steps.Count == 2 && doc.Steps[0].Param == "item_name" && doc.Steps[0].Typed && doc.Steps[0].Frame != null && doc.Steps[0].AfterFrame != null, "saved session reloads: 2 steps, input slot kept, screenshots kept");
                    check(doc.Steps[0].ClassName.Length > 0 && doc.Steps[1].Role == "Button" && doc.Steps[1].Label == "Add item", "targets kept: " + doc.Steps[0].ClassName + " / " + doc.Steps[1].Role + " " + doc.Steps[1].Label);
                    check(doc.ParamNames().SequenceEqual(new[] { "item_name" }) && doc.Procedure != null, "input slots: " + string.Join(",", doc.ParamNames().ToArray()));
                    var bytes = File.ReadAllBytes(path); string asText = System.Text.Encoding.ASCII.GetString(bytes);
                    check(asText.IndexOf("Alpha") < 0, "the typed value is not stored in the session file");

                    reset();
                    var r = Replayer.Run(doc, new Replayer.Options { ShowBar = false, Values = new Dictionary<string, string> { { "item_name", "Beta" } } });
                    check(r.Ok, "replay with a different value ran" + (r.Ok ? "" : ": " + r.Error));
                    check(read() == "Added: Beta", "replay put the new value in the app (\"" + read() + "\")");
                    check(r.Saved != null && File.Exists(r.Saved) && SessionDoc.Load(r.Saved).Steps.Count == 2, "the replay recorded itself as a new session");
                    check(File.ReadAllBytes(r.Saved).Length > 0 && System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(r.Saved)).IndexOf("Beta") < 0, "the replayed value is not stored either");

                    var export = GuideExport.Write(doc, dir);
                    check(File.Exists(Path.Combine(export, "clicks.json")) && File.Exists(Path.Combine(export, "shot-01.png")), "guide folder written: " + Path.GetFileName(export));

                    string csv = Path.Combine(dir, "rows.csv"); File.WriteAllText(csv, "item_name\r\nGamma\r\n\"Delta, with comma\"\r\n");
                    var rows = Replayer.ParseCsv(csv); check(rows.Count == 2 && rows[1]["item_name"] == "Delta, with comma", "csv parsed: " + rows.Count + " rows");
                    reset();
                    var batch = Replayer.RunBatch(doc, rows, new Replayer.Options { ShowBar = false });
                    check(batch.Count == 2 && batch.All(x => x.Ok) && read() == "Added: Delta, with comma", "batch of 2 runs (\"" + read() + "\")");

                    // a procedure session: steps grouped by task, a chosen option kept as an input (never as text), an optional input that can be left blank
                    reset();
                    var proc2 = new Dictionary<string, object> { { "name", "Tasks test" }, { "params", new Dictionary<string, object> {
                        { "item_name", new Dictionary<string, object> { { "type", "text" }, { "label", "Item name" }, { "optional", true }, { "task", "1 Add an item" }, { "action", "1.1" } } },
                        { "pick", new Dictionary<string, object> { { "type", "text" }, { "label", "Pick one" }, { "optional", false }, { "task", "2 Pick one" }, { "action", "2.1" } } } } } };
                    var w2 = new SessionWriter("Tasks test", proc2);
                    var ta = new Target { Process = Me, Title = "SnapLite Driver Test", Label = "Item name", Role = "Edit", Task = "1 Add an item", ActionCode = "1.1" };
                    var tb2 = new Target { Process = Me, Title = "SnapLite Driver Test", Label = "Add item", Role = "Button", Task = "1 Add an item", ActionCode = "1.2" };
                    var tc = new Target { Process = Me, Title = "SnapLite Driver Test", Role = "ListItem", Task = "2 Pick one", ActionCode = "2.1" };
                    check(Driver.Perform(w2, ta, "type", "Delta", "item_name", "Enter the item name in \"Item name\"", new Driver.Options()).Ok, "tasks: typed the first action");
                    check(Driver.Perform(w2, tb2, "click", null, "", "Click \"Add item\"", new Driver.Options()).Ok, "tasks: clicked the second action");
                    var oc = Driver.Perform(w2, tc, "choose", "Beta", "pick", "Choose the option in \"Pick one\"", new Driver.Options());
                    check(oc.Ok, "tasks: chose an option by its name (" + (oc.Ok ? oc.Method : oc.Error) + ")");
                    System.Threading.Thread.Sleep(400);
                    log.AppendLine("      pick box on screen " + (Rectangle)form.Invoke((Func<Rectangle>)delegate { return pickList.RectangleToScreen(pickList.ClientRectangle); }) + ", selected index " + (int)form.Invoke((Func<int>)delegate { return pickList.SelectedIndex; }) + ", step at " + oc.Step.X + "," + oc.Step.Y + " (area " + oc.Step.Vw + "x" + oc.Step.Vh + "), cursor " + System.Windows.Forms.Cursor.Position);
                    check(readPick() == "Picked: Beta", "tasks: the app received the choice (\"" + readPick() + "\")");
                    string p2 = w2.Save(); w2.Dispose();
                    var d3 = SessionDoc.Load(p2); var groups = d3.Groups();
                    check(groups.Count == 2 && groups[0][0].Task == "1 Add an item" && groups[1][0].Task == "2 Pick one" && groups[1][0].BreakBefore, "tasks: the session is grouped by task (" + groups.Count + " groups: " + string.Join(" | ", groups.Select(g => g[0].Task + " x" + g.Count).ToArray()) + ")");
                    check(d3.Steps[2].Param == "pick" && !d3.Steps[2].Typed && d3.Steps[2].Label == "" && d3.Steps[0].StepText.StartsWith("Enter the item name") && d3.Steps[2].ActionCode == "2.1", "tasks: the chosen option's name is not stored; the action text is");
                    check(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(p2)).IndexOf("\"Beta\"") < 0 && System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(p2)).IndexOf("Delta") < 0, "tasks: neither the typed value nor the chosen option is in the file");
                    var sum = SessionDoc.ReadSummary(p2);
                    check(sum != null && sum.Tasks == 2 && sum.TaskNames.Count == 2 && sum.Specs.Count == 2 && sum.Specs[0].Optional && sum.Specs[0].Label == "Item name" && sum.Specs[1].Task == "2 Pick one", "tasks: the summary lists tasks and the inputs to ask for");
                    reset();
                    var rr = Replayer.Run(d3, new Replayer.Options { ShowBar = false, Values = new Dictionary<string, string> { { "pick", "Gamma" } } });
                    check(rr.Ok && rr.Log.Any(x => x.Contains("skipped")) && readPick() == "Picked: Gamma" && read() == "Added: ", "tasks: replay chose a different option and skipped the blank optional input (" + (rr.Ok ? readPick() : rr.Error) + ")");
                    check(SessionDoc.Load(rr.Saved).Groups().Count == 2, "tasks: the replay's own session is grouped by task too");
                    var export2 = GuideExport.Write(d3, dir); var cj = File.ReadAllText(Path.Combine(export2, "clicks.json"));
                    check(cj.Contains("\"task\":\"2 Pick one\"") && cj.Contains("Enter the item name"), "tasks: the guide folder carries each task and the action text");

                    doc.Procedure = null;
                    var miss = Replayer.Run(doc, new Replayer.Options { ShowBar = false });
                    check(!miss.Ok && miss.Error.Contains("item_name"), "a missing input value is refused before anything is clicked: " + miss.Error);

                    // a target that is not there: stops, names the step
                    var d2 = SessionDoc.Load(path); d2.Steps[1].Label = "No such button"; d2.Steps[1].TestId = "";
                    var bad = Replayer.Run(d2, new Replayer.Options { ShowBar = false, AllowCoords = false, FindTimeoutMs = 1500, Values = new Dictionary<string, string> { { "item_name", "X" } } });
                    check(!bad.Ok && bad.Error.StartsWith("Step 2") && bad.Done == 1, "a missing control stops the run at that step: " + bad.Error);

                    // the same thing through the control channel
                    RemoteControl.Start(); System.Threading.Thread.Sleep(400);
                    Func<string, string> cmd = delegate(string json)
                    {
                        string o = Path.Combine(dir, "reply.json"); if (File.Exists(o)) File.Delete(o);
                        RemoteControl.Client(new[] { "--cmd", json, "--out", o }); return File.Exists(o) ? File.ReadAllText(o) : "";
                    };
                    check(cmd("{\"cmd\":\"ping\"}").Contains("\"ok\":true"), "control channel answers");
                    check(cmd("{\"cmd\":\"windows\"}").Contains("SnapLite Driver Test"), "control: windows lists the test window");
                    string win0 = "\"window\":{\"process\":\"" + Me + "\",\"title\":\"SnapLite Driver Test\"}";
                    string obs = cmd("{\"cmd\":\"observe\"," + win0 + "}");
                    check(obs.Contains("\"Add item\"") && obs.Contains("\"Item name\"") && obs.Contains(".png"), "control: observe sees both controls and saved a screenshot");
                    reset();
                    check(cmd("{\"cmd\":\"session-start\",\"name\":\"Via control\"}").Contains("\"ok\":true"), "control: session started");
                    check(cmd("{\"cmd\":\"type\"," + win0 + ",\"target\":{\"label\":\"Item name\",\"role\":\"Edit\"},\"text\":\"Omega\",\"param\":\"item_name\"}").Contains("\"ok\":true"), "control: type");
                    check(cmd("{\"cmd\":\"click\"," + win0 + ",\"target\":{\"label\":\"Add item\"},\"stepText\":\"Add it\"}").Contains("\"ok\":true"), "control: click");
                    check(read() == "Added: Omega", "control: the app got it (\"" + read() + "\")");
                    string end = cmd("{\"cmd\":\"session-end\"}");
                    check(end.Contains("\"steps\":2") && end.Contains("item_name"), "control: session saved with its input slot");
                    check(cmd("{\"cmd\":\"click\"," + win0 + ",\"target\":{\"label\":\"Add item\"}}").Contains("\"ok\":false"), "control: acting with no session is refused");
                }
                catch (Exception ex) { check(false, "exception: " + ex); }
            });
            work.Start(); work.Join(180000);
            try { form.Invoke((Action)delegate { form.Close(); }); } catch { }
            File.WriteAllText(args[1], log.ToString() + (ok ? "ALL PASSED" : "SOME FAILED"));
            return ok ? 0 : 1;
        }

        // read a Process Manager export and show what Claude would do with every action. args: --proc-test <log> <xlsx or csv>
        public static int ProcTest(string[] args)
        {
            var log = new System.Text.StringBuilder();
            try
            {
                var acts = ProcedureFile.Load(args[2]);
                log.AppendLine(acts.Count + " activities with actions: " + string.Join(" | ", acts.Select(a => a.Code + " " + a.Title + " [" + a.System + "] " + a.Tasks.Count + " tasks, " + a.ActionCount + " actions").ToArray()));
                foreach (var act in acts)
                {
                    var specs = ProcedureFile.Specs(act);
                    log.AppendLine(); log.AppendLine("== " + act.Code + " " + act.Title + ": " + specs.Count + " inputs");
                    foreach (var t in act.Tasks)
                    {
                        log.AppendLine(" " + t.Full);
                        foreach (var a in t.Actions)
                            log.AppendLine("   " + a.Code.PadRight(7) + ProcAction.KindName(a.Kind).PadRight(17) + a.Text + "   [clicks: " + a.PlanText() + "]" + (a.Kind == "enter" || a.Kind == "choose" ? "   -> " + a.Param + " [" + a.Type + (a.Choices.Count > 0 ? ": " + string.Join("/", a.Choices.ToArray()) : "") + (a.Optional ? ", optional" : "") + "]" : a.Kind == "skip" ? "   (" + a.Why + ")" : ""));
                    }
                }
            }
            catch (Exception ex) { log.AppendLine("FAILED " + ex); }
            File.WriteAllText(args[1], log.ToString(), System.Text.Encoding.UTF8); return 0;
        }

        // the session editor's functions, driven the way the buttons and the mouse drive them. args: --editor-test <log> <folder>
        public static int EditorTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool ok = true;
            Action<bool, string> check = delegate(bool c, string m) { log.AppendLine((c ? "PASS  " : "FAIL  ") + m); if (!c) ok = false; };
            string dir = args[2]; Directory.CreateDirectory(dir); Settings.Folder = dir; SessionWriter.NoLibrary = true;
            var doc = new SessionDoc { Id = SessionDoc.NewId(), Name = "Editor test", StartedAt = DateTime.Now };
            byte[] png1, png2, png3;
            using (var b = new Bitmap(1600, 900)) using (var g = Graphics.FromImage(b)) { g.Clear(Color.FromArgb(240, 244, 250)); g.FillRectangle(Brushes.White, 100, 100, 600, 80); png1 = SessionDoc.Encode(b); g.FillRectangle(Brushes.LightGoldenrodYellow, 100, 300, 600, 200); png2 = SessionDoc.EncodeJpeg(b); g.Clear(Color.LightGray); png3 = SessionDoc.Encode(b); }
            Func<int, string, bool, byte[], SessionStep> mk = delegate(int n, string task, bool brk, byte[] fr)
            { return new SessionStep { Seq = n, Id = doc.Id + "_" + n.ToString("00000"), Kind = "click", Label = "Item " + n, Role = "Button", Task = task, BreakBefore = brk, Frame = fr, Vw = 1600, Vh = 900, Box = new Rectangle(100 + n * 40, 100 + n * 20, 120, 40), X = 160 + n * 40, Y = 120 + n * 20, Url = "app://test/Test" }; };
            doc.Steps.Add(mk(1, "1 First", true, png1)); doc.Steps.Add(mk(2, "1 First", false, png2)); doc.Steps.Add(mk(3, "1 First", false, png2));
            doc.Steps.Add(mk(4, "1 First", true, png1)); doc.Steps.Add(mk(5, "1 First", false, png2)); doc.Steps.Add(mk(6, "2 Second", true, png3));
            var host = new Form { Width = 1500, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) }; var v = new SessionView { Dock = DockStyle.Fill }; host.Controls.Add(v); host.Show(); v.Load(doc); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            Func<string, object[], object> call = delegate(string m, object[] a) { return typeof(SessionView).GetMethod(m, fl).Invoke(v, a); };
            Action<int> show = delegate(int i) { call("ShowScreen", new object[] { i }); Application.DoEvents(); };
            Action<SessionStep> pick = delegate(SessionStep s) { call("SelectStep", new object[] { s }); Application.DoEvents(); };
            check(doc.Groups().Count == 3, "three screens to start with");

            show(1); call("MergeWithPrevious", null); check(doc.Groups().Count == 2 && doc.Groups()[0].Count == 5, "merge with previous: screens 1 and 2 became one with 5 steps");
            show(1); call("MergeWithPrevious", null); check(doc.Groups().Count == 2, "merge across two different tasks is refused");
            call("Undo", null); check(doc.Groups().Count == 3 && doc.Steps[3].BreakBefore, "undo brings the merged screen back");

            pick(doc.Steps[1]); call("NewScreenshotFromHere", null);
            check(doc.Groups().Count == 4 && doc.Steps[1].BreakBefore && doc.Steps[1].Frame != null && !ReferenceEquals(SessionDoc.PictureOf(doc.Groups()[1]), SessionDoc.PictureOf(doc.Groups()[0])), "new screenshot from here: step 2 starts a screen with its own picture");
            call("Undo", null); check(doc.Groups().Count == 3, "undo takes the split back");

            // the step text is edited in the list itself, not in a pop-up
            Func<TextBox> inl = () => (TextBox)typeof(SessionView).GetField("inline", fl).GetValue(v);
            pick(doc.Steps[1]); call("EditText", null); Application.DoEvents();
            check(inl() != null && inl().Visible && inl().Text == doc.Steps[1].Text, "editing a step: its text opens in a box over its row in the list");
            inl().Text = "Open the menu"; call("EndInline", new object[] { true });
            check(inl() == null && doc.Steps[1].StepText == "Open the menu" && doc.Steps[1].StepTextEdited, "editing a step: Enter keeps the new text");
            call("Undo", null); check(string.IsNullOrEmpty(doc.Steps[1].StepText), "undo takes the typed text back");
            pick(doc.Steps[1]); call("EditText", null); Application.DoEvents(); inl().Text = "Not kept"; call("EndInline", new object[] { false });
            check(string.IsNullOrEmpty(doc.Steps[1].StepText), "editing a step: Esc leaves the text as it was");
            pick(doc.Steps[1]); var stepMenu = (ContextMenuStrip)call("StepMenu", null); var names = stepMenu.Items.Cast<ToolStripItem>().Select(i => i.Text).ToList();
            check(!names.Contains("Edit step text") && names.Contains("New screenshot from here") && names.Contains("Delete step"), "right-click on a step: no \"Edit step text\", has \"New screenshot from here\" (" + string.Join(" | ", names.Where(x => x.Length > 0).ToArray()) + ")");

            show(0); call("StartCrop", null); call("ApplyCrop", null);
            var first = doc.Groups()[0][0]; Rectangle view; Bitmap clean; using (clean = doc.CleanPicture(doc.Groups()[0], out view)) check(!first.Crop.IsEmpty && clean.Width == first.Crop.Width && view == first.Crop, "crop: the screen now shows " + first.Crop);
            using (var marked = doc.RenderGroup(doc.Groups()[0], null)) check(marked.Width == first.Crop.Width && marked.Height == first.Crop.Height, "crop: marked pictures and PDF pages use the cropped size");
            string p1 = Path.Combine(dir, "e1.clicktrail"); doc.Save(p1); var back = SessionDoc.Load(p1); check(back.Steps[0].Crop == first.Crop, "crop survives save and load");
            call("RemoveCrop", null); check(doc.Steps[0].Crop.IsEmpty, "whole screenshot removes the crop");

            pick(doc.Steps[2]); var added = (SessionStep)call("AddStepCore", new object[] { false });
            check(added != null && added.Manual && doc.Steps.IndexOf(added) == 3 && added.Role == "step" && doc.Groups().Count == 3, "add a step: a manual step follows the selected one on the same screen");
            added.StepText = "Read the message"; call("Undo", null); check(!doc.Steps.Contains(added), "undo removes the added step");
            added = (SessionStep)call("AddStepCore", new object[] { false }); added.StepText = "Read the message";

            byte[] custom; using (var b = new Bitmap(800, 450)) using (var g = Graphics.FromImage(b)) { g.Clear(Color.Orange); custom = SessionDoc.Encode(b); }
            show(0); call("UseCustom", new object[] { custom }); check(ReferenceEquals(SessionDoc.PictureOf(doc.Groups()[0]), custom) && doc.Steps[0].Frame != null, "paste screenshot: the pasted picture is shown and the recorded one is kept");
            string p2 = Path.Combine(dir, "e2.clicktrail"); doc.Save(p2); var b2 = SessionDoc.Load(p2);
            check(b2.Steps[0].CustomFrame != null && b2.Steps[0].Frame != null && b2.Steps.Any(s => s.Manual && s.StepText == "Read the message"), "pasted picture and manual step survive save and load");
            call("UseOriginal", null); check(doc.Steps[0].CustomFrame == null, "use original goes back to the recorded picture");

            // choose which screenshot the screen shows
            var pics = SessionDoc.Pictures(doc.Groups()[0]); check(pics.Count >= 3, "which screenshot: " + pics.Count + " pictures to choose between (" + string.Join(", ", pics.Select(p => p.Value).ToArray()) + ")");
            doc.Steps[0].FrameFrom = pics[1].Key; check(ReferenceEquals(SessionDoc.PictureOf(doc.Groups()[0]), doc.Steps[1].Frame), "which screenshot: the chosen step's own picture is used");
            doc.Steps[0].FrameFrom = "";

            // drag a box, resize it, move its number with the mouse
            show(0); pick(doc.Steps[0]); Application.DoEvents();
            var preview = (Panel)typeof(SessionView).GetField("preview", fl).GetValue(v);
            Func<PointF, Point> toScreen = delegate(PointF ip)
            {
                var vw = (Rectangle)call("View", null); float k = (float)call("Fit", new object[] { vw }); var r = (Rectangle)call("Dest", new object[] { vw, k });
                return new Point((int)(r.X + (ip.X - vw.X) * k), (int)(r.Y + (ip.Y - vw.Y) * k));
            };
            Func<string, Point, object> mouse = delegate(string m, Point p) { return typeof(SessionView).GetMethod(m, fl).Invoke(v, new object[] { preview, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0) }); };
            var s0 = doc.Steps[0]; var before = s0.Box;
            var c0 = toScreen(new PointF(before.X + before.Width / 2f, before.Y + 5));       // inside this step's box and clear of the next one's
            mouse("PreviewDown", c0); mouse("PreviewMove", new Point(c0.X + 60, c0.Y + 30)); mouse("PreviewUp", new Point(c0.X + 60, c0.Y + 30));
            check(s0.Box.X > before.X + 20 && s0.Box.Y > before.Y + 5 && s0.Box.Size == before.Size && s0.BoxEdited, "move the box by dragging it: " + before + " to " + s0.Box);
            before = s0.Box; var hnd = toScreen(new PointF(before.Right, before.Bottom));
            mouse("PreviewDown", hnd); mouse("PreviewMove", new Point(hnd.X + 40, hnd.Y + 20)); mouse("PreviewUp", new Point(hnd.X + 40, hnd.Y + 20));
            check(s0.Box.Width > before.Width + 15 && s0.Box.Height > before.Height + 5 && s0.Box.Location == before.Location, "resize the box by its corner: " + before.Size + " to " + s0.Box.Size);
            var bc = SessionDoc.BadgeCenter(s0, 1600); var bs = toScreen(bc);
            mouse("PreviewDown", bs); mouse("PreviewMove", new Point(bs.X - 30, bs.Y - 20)); mouse("PreviewUp", new Point(bs.X - 30, bs.Y - 20));
            check(s0.BadgePos.HasValue && s0.BadgePos.Value.X < bc.X - 10, "move the number by dragging it: " + bc + " to " + s0.BadgePos);
            call("Undo", null); call("Undo", null); call("Undo", null); check(!s0.BadgePos.HasValue && s0.Box == doc.Steps[0].Box, "three undos put the box and number back");

            show(1); int stepsBefore = doc.Steps.Count; int screensBefore = doc.Groups().Count; call("DeleteScreen", null);
            check(doc.Groups().Count == screensBefore - 1 && doc.Steps.Count < stepsBefore, "delete screenshot removes the screen and its steps");
            call("Undo", null); check(doc.Groups().Count == screensBefore && doc.Steps.Count == stepsBefore, "undo brings the deleted screen back");

            var export = GuideExport.Write(doc, dir); var cj = File.ReadAllText(Path.Combine(export, "clicks.json"));
            check(cj.Contains("\"manual\":true") && cj.Contains("shot-01.png"), "the guide folder has the manual step and the pictures");
            show(0); Application.DoEvents(); using (var bmp = Shot(host)) bmp.Save(args[1] + ".png", ImageFormat.Png);
            host.Close(); File.WriteAllText(args[1], log.ToString() + (ok ? "ALL PASSED" : "SOME FAILED")); return ok ? 0 : 1;
        }

        // find where a stray 1px line around a custom control comes from: draw one on a plain form, look at the pixels around it
        public static int PixelTest(string[] args)
        {
            var log = new System.Text.StringBuilder();
            var f = new Form { Width = 520, Height = 320, StartPosition = FormStartPosition.Manual, Location = new Point(80, 80), BackColor = Color.White, Font = Theme.Body(9.5f) };
            var b1 = new FlatButton("Secondary", false) { Location = new Point(40, 40), Size = new Size(160, 40) };
            var b2 = new FlatButton("Primary", true) { Location = new Point(240, 40), Size = new Size(160, 40) };
            var rb = new ThemedRadio { Text = "A radio", Location = new Point(40, 120), Width = 200 };
            var cb = new ThemedCheck { Text = "A check", Location = new Point(40, 170), Width = 200 };
            var plain = new PixelProbe { Location = new Point(280, 120), Size = new Size(120, 40) };      // a control that paints one solid colour and nothing else
            var pb = new ProbeButton { Location = new Point(280, 180), Size = new Size(100, 40) }; var pr = new ProbeRadio { Location = new Point(400, 180), Size = new Size(80, 40) };
            var st1 = new ProbeStaged(1) { Text = "s1", Location = new Point(280, 230), Size = new Size(60, 28) }; var st2 = new ProbeStaged(2) { Text = "s2", Location = new Point(350, 230), Size = new Size(60, 28) };
            var st3 = new ProbeStaged(3) { Text = "s3", Location = new Point(420, 230), Size = new Size(60, 28) }; var st4 = new ProbeStaged(4) { Text = "s4", Location = new Point(40, 230), Width = 120 };
            f.Controls.AddRange(new Control[] { b1, b2, rb, cb, plain, pb, pr, st1, st2, st3, st4 }); f.TopMost = true; f.Show(); f.Activate();
            Application.DoEvents(); System.Threading.Thread.Sleep(600); Application.DoEvents();
            var cl = f.PointToScreen(Point.Empty);
            using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(cl, Point.Empty, bmp.Size);
                Func<Control, string> probe = delegate(Control c)
                {
                    var sb = new System.Text.StringBuilder(c.GetType().Name + " at " + c.Location + " size " + c.Size + ": ");
                    int[] ys = { c.Top - 2, c.Top - 1, c.Top, c.Top + 1, c.Top + c.Height / 2, c.Bottom - 1, c.Bottom };
                    foreach (int y in ys) { var p = bmp.GetPixel(c.Left + c.Width / 2, Math.Max(0, Math.Min(bmp.Height - 1, y))); sb.Append("y" + (y - c.Top) + "=" + p.R + " "); }
                    int[] xs = { c.Left - 2, c.Left - 1, c.Left, c.Left + 1 };
                    sb.Append(" | left edge at mid height: "); foreach (int x in xs) { var p = bmp.GetPixel(Math.Max(0, x), c.Top + c.Height / 2); sb.Append("x" + (x - c.Left) + "=" + p.R + " "); }
                    return sb.ToString();
                };
                foreach (Control c in f.Controls) log.AppendLine(probe(c));
                bmp.Save(args[1] + ".png", ImageFormat.Png);
            }
            f.Close(); File.WriteAllText(args[1], log.ToString()); return 0;
        }

        // the "what to capture, is the screen ready" form. args: --capture-shot <png prefix>
        public static int CaptureShot(string[] args)
        {
            var ctor = typeof(CaptureTargetForm).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
            foreach (bool region in new[] { true, false })
            {
                var f = (Form)ctor.Invoke(new object[] { "Record a session", "Choose what to record. Every click inside it becomes a numbered step with a screenshot.", "Start recording", "window", region });
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(60, 40); f.TopMost = false; f.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(400); Application.DoEvents();
                var ready = f.Controls.OfType<CheckBox>().First(); ready.Checked = true; Application.DoEvents();
                using (var b = Shot(f)) b.Save(args[1] + (region ? "-region.png" : "-claude.png"), ImageFormat.Png);
                f.Close();
            }
            var about = new AboutForm { StartPosition = FormStartPosition.Manual, Location = new Point(60, 40) }; about.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            using (var b = Shot(about)) b.Save(args[1] + "-about.png", ImageFormat.Png); about.Close();
            return 0;
        }

        // the three pages of the procedure wizard, and the values form. args: --wizard-shot <prefix> <xlsx>
        public static int WizardShot(string[] args)
        {
            var w = new ProcedureWizard(); w.StartPosition = FormStartPosition.Manual; w.Location = new Point(60, 40); w.Show(); Application.DoEvents();
            Action snap = delegate { Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents(); };
            snap(); using (var b = Shot(w)) b.Save(args[1] + "-1-file.png", ImageFormat.Png);
            w.LoadFile(args[2]); snap(); w.Preview(0, 0); snap(); using (var b = Shot(w)) b.Save(args[1] + "-2-activity.png", ImageFormat.Png);
            w.Preview(0, 1); snap(); using (var b = Shot(w)) b.Save(args[1] + "-3-actions.png", ImageFormat.Png);
            w.Preview(0, 2); snap(); using (var b = Shot(w)) b.Save(args[1] + "-4-values.png", ImageFormat.Png);
            w.Close(); return 0;
        }

        // pictures of the ClickBot tab: empty, with sample sessions, and after a stopped run. args: --bot-shot <prefix> <folder>
        public static int BotShot(string[] args)
        {
            string dir = args[2], realFolder = Settings.Folder, realTab = Settings.CapTab; Directory.CreateDirectory(dir); Settings.Folder = dir; SessionWriter.NoLibrary = true;
            foreach (var f in Directory.GetFiles(dir, "*" + SessionDoc.Ext)) File.Delete(f);
            var hub = new Hub(); hub.StartPosition = FormStartPosition.Manual; hub.Location = new Point(40, 40); hub.Show(); Application.DoEvents();
            hub.TopMost = true; hub.SetTab("bot"); hub.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(500); Application.DoEvents();
            using (var bmp = Shot(hub)) bmp.Save(args[1] + "-empty.png", ImageFormat.Png);
            File.WriteAllText(args[1] + "-size.txt", "K " + Dpi.K + ", window " + hub.Size + ", client " + hub.ClientSize + ", wanted client " + Dpi.S(890, 442) + ", screen work area " + Screen.FromControl(hub).WorkingArea + ", location " + hub.Location);

            Func<string, int, string, string, string[], SessionDoc> make = delegate(string name, int steps, string app, string title, string[] inputs)
            {
                var d = new SessionDoc { Id = SessionDoc.NewId() + name.Length, Name = name, StartedAt = DateTime.Now.AddMinutes(-30) };
                var ps = new Dictionary<string, object>();
                foreach (var i in inputs) ps[i] = new Dictionary<string, object> { { "type", "text" }, { "default", i == "quantity" ? "1" : "" } };
                if (inputs.Length > 0) d.Procedure = new Dictionary<string, object> { { "name", name }, { "params", ps } };
                using (var bmp = new Bitmap(800, 450)) using (var g = Graphics.FromImage(bmp)) { g.Clear(Theme.Sheet); g.FillRectangle(Brushes.White, 40, 40, 720, 60); var png = SessionDoc.Encode(bmp);
                    for (int s = 0; s < steps; s++)
                        d.Steps.Add(new SessionStep { Seq = s + 1, BreakBefore = s % 3 == 0, Frame = s % 3 == 0 ? png : null, Vw = 800, Vh = 450, Box = new Rectangle(60, 50, 100, 30), Label = "Item " + s, Role = "Button", Url = "app://" + app + "/" + title, Title = title, Param = s == 1 && inputs.Length > 0 ? inputs[0] : "", Typed = s == 1 && inputs.Length > 0 }); }
                d.Renumber(); return d;
            };
            var a = make("Create a cost item", 6, "msedge", "InEight Control", new[] { "cost_item_name", "quantity" }); a.Save(a.DefaultPath(dir));
            System.Threading.Thread.Sleep(1100);
            var b = make("Approve a LEM", 9, "InEight.Estimate", "Estimate", new string[0]); b.Save(b.DefaultPath(dir));
            System.Threading.Thread.Sleep(1100);
            var c = make("Replay - Create a cost item (09.41.12)", 6, "msedge", "InEight Control", new[] { "cost_item_name" }); c.Save(c.DefaultPath(dir));
            hub.SetTab("image"); Application.DoEvents(); hub.SetTab("bot"); Application.DoEvents(); System.Threading.Thread.Sleep(500); Application.DoEvents();
            using (var bmp = Shot(hub)) bmp.Save(args[1] + "-list.png", ImageFormat.Png);

            // a run that stopped at step 4
            var panel = hub.Controls.Cast<Control>().SelectMany(x => x is Card ? x.Controls.Cast<Control>() : new Control[0]).OfType<ClickBotPanel>().FirstOrDefault();
            if (panel != null)
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                ((ListBox)typeof(ClickBotPanel).GetField("list", flags).GetValue(panel)).SelectedIndex = 2;
                Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
                using (var bmp = Shot(hub)) bmp.Save(args[1] + "-inputs.png", ImageFormat.Png);
                var ribbon = (StepRibbon)typeof(ClickBotPanel).GetField("ribbon", flags).GetValue(panel);
                ribbon.Reset(6); ribbon.Set(1, 2); ribbon.Set(2, 2); ribbon.Set(3, 3); ribbon.Set(4, 4);
                var res = new Replayer.Result { Total = 6, Done = 3, Error = "Step 4 (Click \"Save\" button): Could not find \"Save\" (Button) in \"InEight Control\"." };
                var st = typeof(ClickBotPanel).GetMethod("Say", flags); st.Invoke(panel, new object[] { res.Error + "\r\nA control may have been renamed, or the screen is different. Open this run to see where it stopped.", Theme.Mark });
                ((LinkLabel)typeof(ClickBotPanel).GetField("openRun", flags).GetValue(panel)).Visible = true; ((LinkLabel)typeof(ClickBotPanel).GetField("openOrig", flags).GetValue(panel)).Visible = true;
                var title = (Label)typeof(ClickBotPanel).GetField("title", flags).GetValue(panel); title.Text = title.Text;
                typeof(ClickBotPanel).GetMethod("DoLayout", flags).Invoke(panel, null);
                Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
                using (var bmp = Shot(hub)) bmp.Save(args[1] + "-stopped.png", ImageFormat.Png);
            }
            hub.SetTab("session"); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            using (var bmp = Shot(hub)) bmp.Save(args[1] + "-sessions-tab.png", ImageFormat.Png);
            hub.SetTab("image"); hub.TopMost = false;
            Settings.Folder = realFolder; Settings.CapTab = realTab; Settings.Save();     // a picture check must not change the person's settings
            hub.Quit(); return 0;
        }

        // the same, against a web page in a real browser (UI Automation sees page elements). args: --browser-test <log> <folder> <browser exe>
        public static int BrowserTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool ok = true;
            Action<bool, string> check = delegate(bool c, string m) { log.AppendLine((c ? "PASS  " : "FAIL  ") + m); if (!c) ok = false; };
            string dir = args[2]; Directory.CreateDirectory(dir); Settings.Folder = dir; SessionWriter.NoLibrary = true;
            string page = Path.Combine(dir, "form.html");
            File.WriteAllText(page, "<!doctype html><html><head><title>SnapLite Browser Test</title></head><body><h1>Add an item</h1>" +
                "<label for=n>Item name</label> <input id=n type=text> <button id=b onclick=\"document.getElementById('r').textContent='Added: '+document.getElementById('n').value\">Add item</button><p id=r></p></body></html>");
            string exe = args[3]; string profile = Path.Combine(dir, "profile");
            var proc = System.Diagnostics.Process.Start(exe, "--new-window --no-first-run --no-default-browser-check --user-data-dir=\"" + profile + "\" \"" + new Uri(page).AbsoluteUri + "\"");
            string pn = Path.GetFileNameWithoutExtension(exe);
            var work = new System.Threading.Thread(delegate()
            {
                try
                {
                    var win = Driver.FindWindow(pn, "SnapLite Browser Test", 30000);
                    check(win != null, "the browser window opened");
                    if (win == null) return;
                    System.Threading.Thread.Sleep(2500);
                    var w = new SessionWriter("Browser test", new Dictionary<string, object> { { "name", "Browser test" } });
                    var t1 = new Target { Process = pn, Title = "SnapLite Browser Test", Label = "Item name", Role = "Edit" };
                    var t2 = new Target { Process = pn, Title = "SnapLite Browser Test", Label = "Add item", Role = "Button" };
                    var o1 = Driver.Perform(w, t1, "type", "Zeta", "item_name", "", new Driver.Options { FindTimeoutMs = 20000 });
                    check(o1.Ok, "learning: typed into the page's text box (" + (o1.Ok ? o1.Method : o1.Error) + ")");
                    var o2 = Driver.Perform(w, t2, "click", null, "", "", new Driver.Options { FindTimeoutMs = 20000 });
                    check(o2.Ok, "learning: clicked the page's button (" + (o2.Ok ? o2.Method : o2.Error) + ")");
                    Func<string, bool> shows = delegate(string text)
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            var ww = Driver.FindWindow(pn, "SnapLite Browser Test", 2000);
                            if (ww != null) foreach (System.Windows.Automation.AutomationElement e in ww.FindAll(System.Windows.Automation.TreeScope.Descendants, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, text))) return true;
                            System.Threading.Thread.Sleep(300);
                        }
                        return false;
                    };
                    check(shows("Added: Zeta"), "the page shows \"Added: Zeta\"");
                    string path = w.Save(); w.Dispose();
                    var doc = SessionDoc.Load(path);
                    check(doc.Steps[0].UrlBar.Contains("form.html"), "the page address was captured: \"" + doc.Steps[0].UrlBar + "\"");
                    check(doc.Steps[0].TestId == "n", "the page element's id was captured as the test id: \"" + doc.Steps[0].TestId + "\"");
                    var r = Replayer.Run(doc, new Replayer.Options { ShowBar = false, FindTimeoutMs = 20000, Values = new Dictionary<string, string> { { "item_name", "Eta" } } });
                    check(r.Ok, "replay with a different value ran" + (r.Ok ? "" : ": " + r.Error + " | " + string.Join(" | ", r.Log.ToArray())));
                    check(shows("Added: Eta"), "the page shows \"Added: Eta\" after the replay");
                }
                catch (Exception ex) { check(false, "exception: " + ex); }
            });
            work.Start(); work.Join(240000);
            try { foreach (var p in System.Diagnostics.Process.GetProcessesByName(pn)) { try { if (p.StartInfo != null && p.MainWindowTitle.Contains("SnapLite Browser Test")) p.CloseMainWindow(); } catch { } } } catch { }
            File.WriteAllText(args[1], log.ToString() + (ok ? "ALL PASSED" : "SOME FAILED"));
            return ok ? 0 : 1;
        }

        // read real ClickTrail files, re-save copies, reload them, and render the first screen of each
        public static int SessionTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); string outDir = args[2];
            for (int i = 3; i < args.Length; i++)
            {
                try
                {
                    var a = SessionDoc.Load(args[i]);
                    int frames = a.Steps.Count(s => s.Frame != null), after = a.Steps.Count(s => s.AfterFrame != null);
                    log.AppendLine(Path.GetFileName(args[i]) + ": name=\"" + a.Name + "\" steps=" + a.Steps.Count + " screens=" + a.Groups().Count + " frames=" + frames + " afterFrames=" + after);
                    log.AppendLine("   first steps: " + string.Join(" | ", a.Steps.Take(4).Select(s => s.Seq + ". " + s.Text)));
                    string copy = Path.Combine(outDir, "copy" + i + SessionDoc.Ext);
                    a.Path = null; a.Save(copy);
                    var b = SessionDoc.Load(copy);
                    bool same = b.Steps.Count == a.Steps.Count && b.Steps.Zip(a.Steps, (x, y) => x.Label == y.Label && x.Box == y.Box && (x.Frame == null) == (y.Frame == null) && (x.Frame == null || x.Frame.SequenceEqual(y.Frame))).All(v => v);
                    log.AppendLine("   re-saved and reloaded identical: " + same);
                    using (var r = b.RenderGroup(b.Groups()[0], null)) if (r != null) { r.Save(Path.Combine(outDir, "render" + i + ".png"), ImageFormat.Png); log.AppendLine("   rendered screen 1: " + r.Width + "x" + r.Height); }
                }
                catch (Exception ex) { log.AppendLine(Path.GetFileName(args[i]) + ": FAILED " + ex.Message); }
            }
            File.WriteAllText(args[1], log.ToString());
            return 0;
        }

        // open a session in the editor and capture the window, plus the capture window's Sessions tab
        // system audio, camera and the cursor options form. args: --av-test <prefix>
        public static int AvTest(string[] args)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("system audio available: " + SystemAudio.Available + ", camera available: " + Camera.Available);
            if (SystemAudio.Available)
            {
                string wav = args[1] + "-sys.wav"; var s = new SystemAudio(); bool ok = s.Start(wav); log.AppendLine("loopback start: " + ok);
                if (ok) { System.Threading.Thread.Sleep(2500); s.Stop(); log.AppendLine("loopback wav bytes: " + new FileInfo(wav).Length + " (about 2.5 s is " + 2.5 * 48000 * 4 + " or so)"); }
            }
            if (Camera.Available)
            {
                var c = new Camera(); bool ok = c.Start(); log.AppendLine("camera start: " + ok + " " + c.Error);
                if (ok) { Bitmap f = null; for (int i = 0; i < 40 && f == null; i++) { System.Threading.Thread.Sleep(100); f = c.Frame(); } log.AppendLine("camera frame: " + (f == null ? "none" : f.Width + "x" + f.Height)); if (f != null) { f.Save(args[1] + "-cam.png", ImageFormat.Png); f.Dispose(); } c.Stop(); }
            }
            var form = new CursorOptionsForm(); form.StartPosition = FormStartPosition.Manual; form.Location = new Point(60, 60); form.TopMost = true; form.Show();
            Application.DoEvents(); System.Threading.Thread.Sleep(500); Application.DoEvents();
            using (var b = Shot(form)) b.Save(args[1] + "-cursor.png", ImageFormat.Png); form.Close();
            File.WriteAllText(args[1] + ".txt", log.ToString()); return 0;
        }
        // the pop-up menu, a drop-down list, and a session drawn with the new look. args: --menu-shot <prefix> <session.clicktrail>
        public static int MenuShot(string[] args)
        {
            var f = new Form { Text = "menu test", StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), ClientSize = new Size(640, 420), BackColor = Color.White, TopMost = true };
            var combo = new ThemedCombo { Location = new Point(330, 30), Width = 220 }; combo.Items.AddRange(new object[] { "XLCapture Editor", "Default video player", "Folder only", "Another choice" }); combo.SelectedIndex = 1; f.Controls.Add(combo);
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), Font = Theme.Body(9.5f), ShowItemToolTips = true };
            m.Items.Add(new ToolStripMenuItem("Duplicate screenshot")); m.Items.Add(new ToolStripMenuItem("Merge with previous") { Enabled = false }); m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Show boxes") { Checked = true }); m.Items.Add(new ToolStripMenuItem("Show text on screenshots")); m.Items.Add(new ToolStripSeparator()); m.Items.Add(new ToolStripMenuItem("Delete screenshot"));
            f.Show(); Application.DoEvents();
            m.Show(f, new Point(30, 30)); Application.DoEvents(); System.Threading.Thread.Sleep(700); Application.DoEvents();
            combo.DroppedDown = true; Application.DoEvents(); System.Threading.Thread.Sleep(700); Application.DoEvents();
            using (var b = new Bitmap(f.Width, f.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(f.Location, Point.Empty, f.Size); b.Save(args[1] + "-menu.png", ImageFormat.Png); }
            combo.DroppedDown = false; m.Close(); f.Close();
            if (args.Length > 2)
            {
                var doc = SessionDoc.Load(args[2]); doc.ShowText = true; doc.BoxShape = "rounded"; doc.StepSize = 20; doc.TextWeight = "semibold"; doc.TextSize = 20;
                using (var bmp = doc.RenderGroup(doc.Groups()[0], null)) bmp.Save(args[1] + "-look.png", ImageFormat.Png);
                int before = doc.Steps.Count; var g0 = doc.Groups()[0]; var copies = g0.Select(s => { var c = s.Clone(); c.Decor = true; return c; }).ToList();
                var lf = new SessionLookForm(doc, delegate { }); lf.StartPosition = FormStartPosition.Manual; lf.Location = new Point(60, 40); lf.TopMost = true; lf.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(600); Application.DoEvents();
                using (var b = new Bitmap(lf.Width, lf.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(lf.Location, Point.Empty, lf.Size); b.Save(args[1] + "-appearance.png", ImageFormat.Png); }
                lf.Close();
                var tmp = Path.Combine(Path.GetDirectoryName(args[1]), "look-roundtrip.clicktrail"); doc.Save(tmp); var back = SessionDoc.Load(tmp);
                File.WriteAllText(args[1] + ".txt", "steps=" + before + " copies=" + copies.Count + "\r\nroundtrip: showText=" + back.ShowText + " shape=" + back.BoxShape + " size=" + back.StepSize + " weight=" + back.TextWeight + " font=" + back.TextFont + " textSize=" + back.TextSize);
            }
            return 0;
        }
        // opens the editor the way the button does and writes any exception. args: --open-editor <log>
        public static int OpenEditor(string[] args)
        {
            var log = new System.Text.StringBuilder();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { log.AppendLine(e.Exception.ToString()); };
            try { EditorForm.ShowEditor(); for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); } }
            catch (Exception ex) { log.AppendLine("THROWN " + ex); }
            log.AppendLine("done"); File.WriteAllText(args[1], log.ToString()); return 0;
        }
        // every dialog that people see, photographed while open, to check that text and buttons fit. args: --dialogs-shot <prefix> <session.clicktrail> <video.mp4>
        public static int DialogsShot(string[] args)
        {
            var log = new System.Text.StringBuilder(); string prefix = args[1]; string sess = args.Length > 2 ? args[2] : null, vid = args.Length > 3 ? args[3] : null;
            Action<string, Action> run = delegate(string name, Action open)
            {
                var t = new System.Windows.Forms.Timer { Interval = 900 };
                t.Tick += delegate
                {
                    t.Stop(); var f = Form.ActiveForm ?? Application.OpenForms.Cast<Form>().LastOrDefault(x => x.Visible && x.Modal);
                    if (f != null) { using (var b = new Bitmap(f.Width, f.Height)) { using (var g = Graphics.FromHwnd(IntPtr.Zero)) { } using (var g = Graphics.FromImage(b)) g.CopyFromScreen(f.Location, Point.Empty, f.Size); b.Save(prefix + "-" + name + ".png", ImageFormat.Png); } log.AppendLine(name + " " + f.Width + "x" + f.Height); f.DialogResult = DialogResult.Cancel; f.Close(); }
                    else log.AppendLine(name + " NO FORM");
                };
                t.Start();
                try { open(); } catch (Exception ex) { log.AppendLine(name + " THROWN " + ex.Message); }
                t.Dispose();
            };
            var host = new Form { Text = "host", StartPosition = FormStartPosition.Manual, Location = new Point(200, 120), Size = new Size(900, 600), TopMost = true }; host.Show(); Application.DoEvents();
            run("prompt", delegate { Dialogs.Prompt(host, "Edit callout", "Text"); });
            run("form", delegate { Dialogs.Form(host, "Canvas size", "Apply", new Dialogs.Field { Label = "Width (px)", Kind = "int", Value = 800, Min = 1, Max = 9999 }, new Dialogs.Field { Label = "Anchor", Kind = "choice", Choices = new[] { "Top left", "Centre" }, Value = "Centre" }, new Dialogs.Field { Label = "New area colour", Kind = "color", Value = Color.White }); });
            Settings.AskDelete = true; run("confirm", delegate { Dialogs.ConfirmDelete(host, "Delete", "Move capture.png to the Recycle Bin?", "Move to Recycle Bin"); });
            foreach (var withPlan in new[] { false, true })      // the small recording bar (one monitor only), without and with a procedure
            {
                var bar = new SessionBar(Screen.PrimaryScreen.Bounds, withPlan); bar.Show(); for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(40); }
                using (var b = new Bitmap(bar.Width, bar.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(bar.Location, Point.Empty, bar.Size); b.Save(prefix + (withPlan ? "-sessionbar-plan.png" : "-sessionbar.png"), ImageFormat.Png); }
                bar.Close();
            }
            run("confirmrec", delegate { Dialogs.ConfirmAction(host, "Cancel this recording?", "The 14 steps recorded so far will be thrown away. Nothing is saved.", "Cancel recording", "Keep recording"); });
            run("color", delegate { Color c; ColorPickerDialog.Pick(host, Color.Orange, out c); });
            run("cursor", delegate { using (var f = new CursorOptionsForm()) f.ShowDialog(host); });
            run("about", delegate { using (var f = new AboutForm()) f.ShowDialog(host); });
            run("settings", delegate { using (var f = new SettingsForm()) f.ShowDialog(host); });
            run("target", delegate { CaptureTargetForm.Ask(host, "Where should Claude capture?", "Claude captures one window, or the full screen.", "Start", "window", false); });
            run("slideoptions", delegate { using (var f = new SlidePackOptionsForm(@"C:\Templates\Ventia Template 1.pptx", "")) f.ShowDialog(host); });
            run("slidedone", delegate { string d = @"C:\Users\Someone\Pictures\Close the issue - slide pack"; using (var f = new SlidePackDoneForm(d, SlidePack.Prompt(d, "Close the issue"), true)) f.ShowDialog(host); });
            run("checklist", delegate { var act = SampleActivity(); SessionRecorder.BeginPlan(act); SessionRecorder.ToggleDoneForTest(0); SessionRecorder.ToggleDoneForTest(1); ChecklistForm.PreviewSteps = 14; using (var f = new ChecklistForm(act, Screen.PrimaryScreen)) f.ShowDialog(host); ChecklistForm.PreviewSteps = null; });
            run("recpanel", delegate { RecordingPanel.PreviewSteps = 14; RecordingPanel.PreviewLast = "Click \"Save\" button"; using (var f = new RecordingPanel(Screen.PrimaryScreen)) f.ShowDialog(host); RecordingPanel.PreviewSteps = null; RecordingPanel.PreviewLast = null; });
            if (vid != null) { run("videodone", delegate { using (var f = new VideoDone(vid)) f.ShowDialog(host); }); run("trim", delegate { using (var f = new TrimForm(vid)) f.ShowDialog(host); }); }
            if (sess != null) { run("replay", delegate { using (var f = new ReplayDialog(SessionDoc.Load(sess))) f.ShowDialog(host); }); }
            var doc = new Doc(new Bitmap(800, 500)) { Name = "t" }; var ed = EditorForm.Show(doc); Application.DoEvents();
            Action<string, string, object[]> viaEditor = delegate(string name, string method, object[] a)
            {
                run(name, delegate { var mi = typeof(EditorForm).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public); if (mi == null) { log.AppendLine(name + " NO METHOD " + method); return; } mi.Invoke(ed, a); });
            };
            viaEditor("resize", "ResizeDialog", new object[0]); viaEditor("canvas", "CanvasDialog", new object[0]); viaEditor("rotate", "RotateAny", new object[0]);
            viaEditor("ocr", "ShowText", new object[] { "Sample text found in the picture.\r\nSecond line." });
            foreach (var m in new[] { "BorderDialog", "ShadowDialog", "EdgeDialog", "PerspectiveDialog" }) viaEditor(m.ToLower(), m, new object[0]);
            ed.Close(); host.Close();
            File.WriteAllText(prefix + ".txt", log.ToString()); return 0;
        }
        // a drawing tool must pick up an existing shape instead of drawing a new one. args: --pick-test <log>
        public static int PickTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool ok = true;
            var doc = new Doc(new Bitmap(600, 400)) { Name = "t" }; var shape = new ShapeObj { Kind = ShapeKind.Rect, A = new PointF(100, 100), B = new PointF(300, 200), Stroke = Color.Red, Width = 4 }; doc.Add(shape);
            var c = new DocCanvas(doc); c.SetTool(Tool.Shape); c.CreateControl();
            Func<MouseEventArgs> at = () => new MouseEventArgs(MouseButtons.Left, 1, DocCanvas.Margin + 100, DocCanvas.Margin + 150, 0);       // on the left edge of the shape (zoom 1)
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            for (int w = 0; w < 100 && Control.ModifierKeys != Keys.None; w++) System.Threading.Thread.Sleep(100);        /* the tool reads the real Alt key; wait for the person to let go of it */
            bool picked = false; c.PickedUp += delegate { picked = true; };
            typeof(DocCanvas).GetMethod("OnMouseDown", flags).Invoke(c, new object[] { at() });
            log.AppendLine("tool after pressing on the shape: " + c.ActiveTool + ", picked=" + picked + ", selected=" + c.Sel.Count + ", objects=" + doc.Objs.Count);
            ok &= c.ActiveTool == Tool.Select && picked && c.Sel.Count == 1 && doc.Objs.Count == 1;
            typeof(DocCanvas).GetMethod("OnMouseUp", flags).Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, DocCanvas.Margin + 100, DocCanvas.Margin + 150, 0) });
            c.SetTool(Tool.Shape);
            typeof(DocCanvas).GetMethod("OnMouseDown", flags).Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, DocCanvas.Margin + 450, DocCanvas.Margin + 300, 0) });
            log.AppendLine("tool after pressing on empty space: " + c.ActiveTool);
            ok &= c.ActiveTool == Tool.Shape;
            log.AppendLine(ok ? "ALL PASSED" : "FAILED"); File.WriteAllText(args[1], log.ToString()); return ok ? 0 : 1;
        }
        // sharing a session, and moving its text. args: --share-test <log> <session.clicktrail> <folder>
        public static int ShareTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool all = true; Action<string, bool> chk = delegate(string n, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + n); all &= ok; };
            Directory.CreateDirectory(args[3]); var doc = SessionDoc.Load(args[2]); doc.ShowText = true; doc.Steps[0].TextPos = new PointF(333, 222); doc.Name = "Shared test";
            string shared = Path.Combine(args[3], "t.xlsession"), orig = doc.Path;
            doc.SaveShared(shared);
            chk("saving a shared copy leaves the open session where it was", doc.Path == orig && File.Exists(orig));
            var head = File.ReadAllBytes(shared).Take(8).ToArray();
            chk("the shared file is not a plain session (starts with XLCSHR01)", System.Text.Encoding.ASCII.GetString(head) == "XLCSHR01");
            chk("the plain session reader refuses it (magic differs)", System.Text.Encoding.ASCII.GetString(head) != "CTSESS01");
            var back = SessionDoc.Load(shared);
            chk("XLCapture opens it: same steps and name", back.Steps.Count == doc.Steps.Count && back.Name == "Shared test");
            chk("the moved text position survives", back.Steps[0].TextPos.HasValue && Math.Abs(back.Steps[0].TextPos.Value.X - 333) < 0.01f && Math.Abs(back.Steps[0].TextPos.Value.Y - 222) < 0.01f);
            chk("the screenshots survive", SessionDoc.PictureOf(back.Groups()[0]) != null && SessionDoc.PictureOf(back.Groups()[0]).Length > 100);
            var tb = back.TextBox(back.Steps[0], 1600, 900); chk("the text label sits where it was moved to", tb.HasValue && Math.Abs(tb.Value.X - 333) < 1);
            var bytes = File.ReadAllBytes(shared); bytes[bytes.Length / 2] ^= 0x55; string bad = Path.Combine(args[3], "bad.xlsession"); File.WriteAllBytes(bad, bytes);
            bool refused = false; try { SessionDoc.Load(bad); } catch (InvalidDataException) { refused = true; } chk("a changed shared file is refused", refused);
            bool summary = SessionDoc.ReadSummary(shared) != null; chk("lists (summary) read a shared file", summary);
            back.Save(back.Path); chk("saving a shared session keeps it shared", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(shared).Take(8).ToArray()) == "XLCSHR01");
            using (var bmp = back.RenderGroup(back.Groups()[0], null)) bmp.Save(Path.Combine(args[3], "moved-text.png"), ImageFormat.Png);
            back.ShowNumbers = false; string hid = Path.Combine(args[3], "hidden-numbers.xlsession"); back.SaveShared(hid); var back2 = SessionDoc.Load(hid);
            chk("hiding the step numbers is saved and reopens hidden", !back2.ShowNumbers);
            using (var na = back.RenderGroup(back.Groups()[0], null)) { back.ShowNumbers = true; using (var nb = back.RenderGroup(back.Groups()[0], null)) chk("a picture without numbers differs from one with them", !SessionDoc.Encode(na).SequenceEqual(SessionDoc.Encode(nb))); }
            chk("a font that is not installed falls back to Segoe UI", SessionDoc.UsableFont("No Such Font Zz") == "Segoe UI" && SessionDoc.UsableFont("Segoe UI") == "Segoe UI");
            back.TextNumber = true; chk("the text label can carry the step number (" + back.LabelText(back.Steps[0]) + ")", back.LabelText(back.Steps[0]).StartsWith(back.Steps[0].Seq + ". "));
            var g0b = back.Groups()[0]; back.ShowText = true; back.Steps[0].TextPos = new PointF(-400, 40);
            using (var pic0 = SessionDoc.Decode(SessionDoc.PictureOf(g0b)))
            {
                var view0 = SessionDoc.ViewOf(g0b, pic0.Width, pic0.Height);
                using (var grown = back.RenderGroup(g0b, null)) chk("text moved outside the screenshot makes the picture wider (" + view0.Width + " to " + grown.Width + ") with a transparent margin", grown.Width > view0.Width && grown.GetPixel(2, grown.Height - 2).A == 0);
                var tbo = back.TextBoxIn(g0b[0], pic0.Width, view0).Value; var extR = back.OutsideExtent(g0b, pic0.Width, view0);
                back.TextStyle = "callout"; using (var cs = back.RenderGroup(g0b, null)) { var px = cs.GetPixel((int)(tbo.X - extR.X) + 3, (int)(tbo.Y + tbo.Height / 2 - extR.Y)); chk("with Callout in a box the text outside the screenshot still has its white box (pixel " + px.R + "," + px.G + "," + px.B + " alpha " + px.A + ")", px.A > 200 && px.R > 235 && px.G > 235 && px.B > 235); }
                back.CalloutOpacity = 50; back.CalloutColor = Color.FromArgb(255, 200, 0); using (var tp = back.RenderGroup(g0b, null)) { var px = tp.GetPixel((int)(tbo.X - extR.X) + 3, (int)(tbo.Y + tbo.Height / 2 - extR.Y)); chk("the callout box can be a chosen colour at 50% (pixel " + px.R + "," + px.G + "," + px.B + " alpha " + px.A + ")", Math.Abs(px.A - 128) <= 3 && Math.Abs(px.R - 255) <= 2 && Math.Abs(px.G - 200) <= 2 && px.B <= 2); /* half-transparent pixels are stored premultiplied, so a channel can be off by one */ }
                string fillFile = Path.Combine(args[3], "fill.xlsession"); back.SaveShared(fillFile); var fillBack = SessionDoc.Load(fillFile); chk("the callout colour and opacity are saved with the session", fillBack.CalloutOpacity == 50 && fillBack.CalloutColor.ToArgb() == Color.FromArgb(255, 255, 200, 0).ToArgb());
                back.CalloutOpacity = 100; back.CalloutColor = Color.White;
                back.TextStyle = "plain"; using (var ps = back.RenderGroup(g0b, null)) { var px = ps.GetPixel((int)(tbo.X - extR.X) + 3, (int)(tbo.Y + tbo.Height / 2 - extR.Y)); chk("with Plain text the text outside the screenshot has no box (alpha " + px.A + ")", px.A < 30); }
                back.TextStyle = "callout";
                int cornerX = (int)(tbo.X - extR.X) + 1, cornerY = (int)(tbo.Y - extR.Y) + 1;
                back.CalloutShape = "rounded"; using (var rb = back.RenderGroup(g0b, null)) { var px = rb.GetPixel(cornerX, cornerY); chk("a rounded callout leaves its corner empty (alpha " + px.A + ")", px.A < 60); }
                back.CalloutShape = "square"; using (var sb = back.RenderGroup(g0b, null)) { var px = sb.GetPixel(cornerX, cornerY); chk("a square callout fills its corner (alpha " + px.A + ")", px.A > 200); }
                string shapeFile = Path.Combine(args[3], "shape.xlsession"); back.SaveShared(shapeFile); chk("the callout shape is saved with the session", SessionDoc.Load(shapeFile).CalloutShape == "square");
                back.CalloutShape = "rounded"; back.SaveShared(shapeFile); chk("a rounded callout (the default) loads back as rounded", SessionDoc.Load(shapeFile).CalloutShape == "rounded");
                back.Steps[0].TextPos = null; using (var same = back.RenderGroup(g0b, null)) chk("text inside the screenshot leaves the picture its own size", same.Width == view0.Width && same.Height == view0.Height);
                {   // how round a rounded box is: set per box, drawn, saved with the session
                    bool oText = back.ShowText, oNum = back.ShowNumbers, oBoxes = back.ShowBoxes; string oShape = back.BoxShape; var s0 = back.Steps[0]; float oRound = s0.Round;
                    back.ShowText = false; back.ShowNumbers = false; back.ShowBoxes = true; back.BoxShape = "rounded";
                    int bx = s0.Box.X - view0.X, by = s0.Box.Y - view0.Y;          // the top-left corner: no other step's box passes through it
                    Func<Bitmap, bool> inked = delegate(Bitmap bm) { var px = bm.GetPixel(bx, by); return px.R > 180 && px.G < 90 && px.B < 90; };
                    s0.Round = 0f; using (var sq = back.RenderGroup(g0b, null)) chk("a box with roundness 0 has square corners", inked(sq));
                    s0.Round = 0.5f; using (var rd = back.RenderGroup(g0b, null)) { var px = rd.GetPixel(bx, by); chk("a box with the most roundness has no ink in its corner (pixel " + px.R + "," + px.G + "," + px.B + " at " + bx + "," + by + ")", !inked(rd)); }
                    s0.Round = 0.37f; string roundFile = Path.Combine(args[3], "round.xlsession"); back.SaveShared(roundFile); chk("a box's roundness is saved with the session", Math.Abs(SessionDoc.Load(roundFile).Steps[0].Round - 0.37f) < 0.01f);
                    s0.Round = -1f; back.SaveShared(roundFile); chk("a box left on automatic roundness loads back as automatic", SessionDoc.Load(roundFile).Steps[0].Round < 0);
                    chk("automatic roundness gives corners of at least 3 pixels (" + SessionDoc.CornerRadius(s0).ToString("0.0") + ")", SessionDoc.CornerRadius(s0) >= 3f);
                    s0.Round = 0.5f; chk("the most roundness is half the box's shorter side", Math.Abs(SessionDoc.CornerRadius(s0) - Math.Min(s0.Box.Width, s0.Box.Height) / 2f) < 0.01f);
                    s0.Round = oRound; back.ShowText = oText; back.ShowNumbers = oNum; back.ShowBoxes = oBoxes; back.BoxShape = oShape;
                }
            }
            using (var shot = back.RenderGroup(g0b, null)) { var data = SessionView.PictureData(shot); var png = data.GetData("PNG") as MemoryStream; var bmc = data.GetData(DataFormats.Bitmap) as Bitmap; chk("copying a screenshot puts a bitmap and a PNG of the marked-up picture on the clipboard (" + shot.Width + "x" + shot.Height + ")", png != null && png.Length > 1000 && png.ToArray()[1] == (byte)'P' && bmc != null && bmc.Width == shot.Width && bmc.Height == shot.Height); }
            Settings.ColorPresets.Add(new[] { "Test preset", "#112233", "#445566", "#778899" }); Settings.Save(); Settings.ColorPresets.Clear(); Settings.Load();
            chk("a named colour preset is saved and comes back", Settings.ColorPresets.Any(p => p[0] == "Test preset" && p[1] == "#112233" && p[2] == "#445566" && p[3] == "#778899"));
            // a preset the person saves also holds the font, its size and its weight, and applying it sets them
            Settings.ColorPresets.Add(new[] { "Test font preset", "#112233", "#445566", "#778899", "Georgia", "20", "bold" }); Settings.Save(); Settings.ColorPresets.Clear(); Settings.Load();
            chk("a preset with a font is saved and comes back (font, size, weight)", Settings.ColorPresets.Any(p => p[0] == "Test font preset" && p.Length == 7 && p[4] == "Georgia" && p[5] == "20" && p[6] == "bold"));
            Settings.ColorPresets.RemoveAll(p => p[0] == "Test font preset");
            back.TextFont = "Georgia"; back.TextSize = 20; back.TextWeight = "bold";
            using (var lf = new SessionLookForm(back, delegate { }))
            {
                var mk = typeof(SessionLookForm).GetMethod("MakeEntry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var entry = (string[])mk.Invoke(lf, new object[] { "Mine", false, null });
                chk("Save as preset stores the font as it is now (" + string.Join(" | ", entry) + ")", entry.Length == 7 && entry[4] == "Georgia" && entry[5] == "20" && entry[6] == "bold");
                var ap = typeof(SessionLookForm).GetMethod("ApplyPreset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                ap.Invoke(lf, new object[] { new[] { "Other", "#112233", "#445566", "#778899", "Segoe UI", "12", "regular" } });
                chk("applying a preset with a font sets the font, size and weight (" + back.TextFont + ", " + back.TextSize + ", " + back.TextWeight + ")", back.TextFont == "Segoe UI" && back.TextSize == 12 && back.TextWeight == "regular");
                ap.Invoke(lf, new object[] { new[] { "Old", "#AA0000", "#00AA00", "#0000AA" } });
                chk("a preset without a font leaves the font as it is", back.TextFont == "Segoe UI" && back.TextSize == 12 && back.TextWeight == "regular");
            }
            Settings.ColorPresets.RemoveAll(p => p[0] == "Test preset"); Settings.Save();
            log.AppendLine(all ? "ALL PASSED" : "SOME FAILED"); File.WriteAllText(args[1], log.ToString()); return all ? 0 : 1;
        }
        // reading a Process Manager workbook, and the slide pack folder. args: --slides-test <log> <session> <folder> [<procedure.xlsx>]
        public static int SlidesTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool all = true; Action<string, bool> chk = delegate(string n, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + n); all &= ok; };
            try
            {
                ProcActivity act = null;
                if (args.Length > 4 && File.Exists(args[4]))
                {
                    var acts = ProcedureFile.Load(args[4]);
                    act = acts.FirstOrDefault(a => a.Title.Contains("InEight Billings"));
                    chk("the workbook is read although its first sheet is the cover (" + acts.Count + " activity)", act != null);
                    if (act != null)
                    {
                        chk("the activity has its 8 tasks and 67 actions (" + act.Tasks.Count + " and " + act.ActionCount + ")", act.Tasks.Count == 8 && act.ActionCount == 67);
                        chk("the cover gives the process: " + act.ProcessCode + " " + act.ProcessTitle + " V" + act.ProcessVersion + ", " + act.Client, act.ProcessCode == "1.4.1" && act.ProcessTitle == "Create Delivery Project" && act.ProcessVersion == "0.0" && act.Client == "Ventia");
                        chk("the system comes from the System column (" + act.System + ")", act.System.Length > 0);
                    }
                }
                else log.AppendLine("SKIP the workbook was not given");
                if (act == null) act = new ProcActivity { Code = "1", Title = "Sample activity", System = "Test", ProcessCode = "9.9", ProcessTitle = "Sample process", ProcessVersion = "1.0", Client = "Client" };

                Directory.CreateDirectory(args[3]); var doc = SessionDoc.Load(args[2]); var groups = doc.Groups();
                for (int gi = 0; gi < groups.Count; gi++) foreach (var s in groups[gi]) { s.Task = gi < 2 ? "6.1 Open the project" : "6.2 Next task"; s.ActionCode = "6.1." + (gi + 1); }
                groups[0][0].StepText = "Navigate to the main menu";
                doc.Procedure = ProcedureFile.ProcedureObject(act, new List<InputSpec>(), null); if (args.Length > 4 && File.Exists(args[4])) doc.Procedure["source"] = args[4];
                groups[0][0].TextPos = new PointF(-420, 60);
                string tpl = args.Length > 5 && File.Exists(args[5]) ? args[5] : Path.Combine(args[3], "my template.pptx"); if (!File.Exists(tpl)) File.WriteAllText(tpl, "not really a deck");
                string dir = SlidePack.Write(doc, args[3], tpl);
                var root = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(Path.Combine(dir, "slides.json"))) as Dictionary<string, object>;
                var slides = ((System.Collections.IEnumerable)root["slides"]).Cast<Dictionary<string, object>>().ToList();
                chk("one slide per screen (" + slides.Count + " of " + groups.Count + ")", slides.Count == groups.Count);
                chk("every slide has its picture", slides.All(sl => File.Exists(Path.Combine(dir, (string)sl["image"]))));
                chk("the first slide carries the task and its step text", (string)slides[0]["task"] == "6.1 Open the project" && (string)slides[0]["taskCode"] == "6.1" && (string)slides[0]["taskTitle"] == "Open the project"
                    && ((System.Collections.IEnumerable)slides[0]["steps"]).Cast<Dictionary<string, object>>().First()["text"].ToString() == "Navigate to the main menu");
                var s0 = ((System.Collections.IEnumerable)slides[0]["steps"]).Cast<Dictionary<string, object>>().ToList();
                chk("the picture is the clean screenshot (no marks): its size is the editor's view", Convert.ToInt32(slides[0]["width"]) > 0 && s0.Count == groups[0].Count);
                var tb0 = s0[0]["textBox"] as Dictionary<string, object>;
                chk("text moved outside the screenshot is exported outside it (x " + (tb0 == null ? "none" : tb0["x"].ToString()) + ")", tb0 != null && (bool)tb0["outside"] && Convert.ToDouble(tb0["x"]) < 0);
                chk("a box on the picture is exported in the picture's pixels", s0[0]["box"] is Dictionary<string, object> && Convert.ToDouble(((Dictionary<string, object>)s0[0]["box"])["w"]) > 0);
                chk("steps are numbered 1.. within the slide", Convert.ToInt32(s0[0]["index"]) == 1);
                if (slides.Count > 1) chk("the second slide of the same task is marked continued", (bool)slides[1]["continued"] && !(bool)slides[0]["continued"]);
                if (slides.Count > 2) chk("a new task starts a new section", !(bool)slides[2]["continued"] && ((System.Collections.IEnumerable)root["tasks"]).Cast<object>().Count() == 2);
                var proc = (Dictionary<string, object>)root["process"]; var ac = (Dictionary<string, object>)root["activity"];
                chk("the process and activity travel with the pack", (string)proc["code"] == act.ProcessCode && (string)proc["title"] == act.ProcessTitle && (string)ac["title"] == act.Title);
                chk("the template is copied into the pack and named in slides.json", (string)root["template"] == "template.pptx" && File.Exists(Path.Combine(dir, "template.pptx")));
                chk("the sentence for Claude is saved and names the folder", File.ReadAllText(Path.Combine(dir, SlidePack.PromptFile)).Contains(dir) && File.ReadAllText(Path.Combine(dir, SlidePack.PromptFile)).Contains("xlcapture-slides"));
                chk("the skill and its deck builder are inside the program", SkillInstaller.Embedded("xlcapture-slides") != null && SkillInstaller.Embedded("xlcapture-slides").Contains("slides.json") && SkillInstaller.Embedded("xlcapture-build-deck", ".py") != null);
                log.AppendLine("PACK " + dir);
                string c, ti; SlidePack.SplitTask("6.2 Do the next thing", out c, out ti); chk("a task name splits into code and title", c == "6.2" && ti == "Do the next thing");
            }
            catch (Exception ex) { log.AppendLine("FAIL THROWN " + ex); all = false; }
            log.AppendLine(all ? "ALL PASSED" : "SOME FAILED"); File.WriteAllText(args[1], log.ToString()); return all ? 0 : 1;
        }
        static ProcActivity SampleActivity()
        {
            var act = new ProcActivity { Code = "2", Title = "Conform estimate job" };
            var t1 = new ProcTask { Code = "2.1", Title = "Duplicate estimate" }; var t2 = new ProcTask { Code = "2.2", Title = "Remove scope" };
            string[] a1 = { "Log into InEight Platform", "Click the top-left hamburger menu", "Click your Organization Name e.g. Ventia", "Launch \"Estimate\"" };
            string[] a2 = { "Open the \"File\" menu", "Select \"Create a new Job from Existing Job\"" };
            int n = 0; foreach (var s in a1) t1.Actions.Add(new ProcAction { Code = "2.1." + (++n), Text = s, Task = t1 }); n = 0;
            foreach (var s in a2) t2.Actions.Add(new ProcAction { Code = "2.2." + (++n), Text = s, Task = t2 });
            act.Tasks.Add(t1); act.Tasks.Add(t2); return act;
        }
        // which action a recorded click belongs to, the checklist ticks, and where a session records. args: --plan-test <log>
        public static int PlanTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool all = true; Action<string, bool> chk = delegate(string n, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + n); all &= ok; };
            try
            {
                var act = SampleActivity(); SessionRecorder.BeginPlan(act);
                Func<string, string, SessionStep> click = (label, kind) => new SessionStep { Kind = kind, Label = label };
                var s1 = click("Login", "click"); bool tc = SessionRecorder.Tag(s1);
                chk("a click that names nothing goes with the first action of the open task and ticks nothing (" + s1.ActionCode + ")", s1.ActionCode == "2.1.1" && s1.StepText == "Log into InEight Platform" && SessionRecorder.DoneCount == 0 && tc);
                var s2 = click("Menu", "click"); SessionRecorder.Tag(s2);
                chk("a click on \"Menu\" is the hamburger menu action (" + s2.ActionCode + ")", s2.ActionCode == "2.1.2" && SessionRecorder.DoneCount == 0);
                var sc = click("", "scroll"); SessionRecorder.Tag(sc);
                chk("a scroll goes with the last action used (" + sc.ActionCode + ")", sc.ActionCode == "2.1.2");
                var s4 = click("Estimate", "click"); SessionRecorder.Tag(s4);
                chk("a click on \"Estimate\" is the Launch \"Estimate\" action, skipping the one before it (" + s4.ActionCode + ")", s4.ActionCode == "2.1.4");
                var s5 = click("", "click"); SessionRecorder.Tag(s5);
                chk("a click with no label goes with the first action not used yet (" + s5.ActionCode + ")", s5.ActionCode == "2.1.3");
                chk("nothing was ticked by any of those clicks", SessionRecorder.DoneCount == 0);
                SessionRecorder.SetTaskForTest(1);
                var s6 = click("File", "click"); bool tc2 = SessionRecorder.Tag(s6);
                chk("after Next task the clicks belong to the next task, on a new screen (" + s6.Task + ")", s6.ActionCode == "2.2.1" && tc2 && s6.Task.StartsWith("2.2"));
                SessionRecorder.ToggleDoneForTest(2);
                chk("an action is ticked by hand (" + SessionRecorder.DoneCount + " done)", SessionRecorder.IsDone(2) && SessionRecorder.DoneCount == 1);
                chk("the checklist has a panel for each task (" + SessionRecorder.TaskCount + ")", SessionRecorder.TaskCount == 2 && SessionRecorder.TaskPos == 1);
                // where a session records: only clicks inside the chosen area
                var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static; var T = typeof(SessionRecorder);
                T.GetField("target", fl).SetValue(null, "area"); T.GetField("fixedArea", fl).SetValue(null, new Rectangle(100, 100, 400, 300)); T.GetField("targetHwnd", fl).SetValue(null, IntPtr.Zero);
                var area = T.GetMethod("AreaFor", fl);
                chk("a click inside the chosen area is recorded", ((Rectangle)area.Invoke(null, new object[] { new Point(200, 200) })) == new Rectangle(100, 100, 400, 300));
                // a browser (or other Chromium program) on this desktop: its window is just its page
                var vs = SystemInformation.VirtualScreen; int seen = 0;
                using (var bmp = new Bitmap(vs.Width, vs.Height)) using (var ov = new PickOverlay(bmp, CapMode.Window))
                {
                    var cm = typeof(PickOverlay).GetMethod("ContentOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var w in Native.Windows().Where(x => Native.ClassOf(x.Handle) == "Chrome_WidgetWin_1"))
                    {
                        var r = w.Rect; r.Offset(-vs.X, -vs.Y); var c = (Rectangle)cm.Invoke(ov, new object[] { new WinInfo { Handle = w.Handle, Title = w.Title, Rect = r } }); seen++;
                        chk("a Chromium window (" + w.Title + ") is only its page: " + c + " inside " + r, c.Width > 0 && c.Height > 0 && c.Top >= r.Top && c.Height <= r.Height && c.Width <= r.Width && (!w.Title.EndsWith("Google Chrome") || c.Top > r.Top));
                    }
                }
                if (seen == 0) log.AppendLine("SKIP no Chromium browser window is open to try the page-only window on");
                chk("a click outside the chosen area is not recorded", ((Rectangle)area.Invoke(null, new object[] { new Point(600, 200) })).IsEmpty && ((Rectangle)area.Invoke(null, new object[] { new Point(99, 200) })).IsEmpty);
                // changing the window in the middle of a task: the new area counts, the old one does not
                SessionRecorder.ApplyTarget("area", new Rectangle(700, 100, 300, 200), IntPtr.Zero);
                chk("after changing the window a click in the new area is recorded", ((Rectangle)area.Invoke(null, new object[] { new Point(800, 150) })) == new Rectangle(700, 100, 300, 200));
                chk("after changing the window a click in the old area is not recorded", ((Rectangle)area.Invoke(null, new object[] { new Point(200, 200) })).IsEmpty);
                // carrying on a saved session: the checklist is back on the task of the last step, with the actions used so far used and ticked
                var old = new SessionDoc { Id = "x", Name = "old" };
                old.Steps.Add(new SessionStep { Task = "2.1 Duplicate estimate", ActionCode = "2.1.1" }); old.Steps.Add(new SessionStep { Task = "2.1 Duplicate estimate", ActionCode = "2.1.2" });
                old.Steps.Add(new SessionStep { Task = "2.2 Remove scope", ActionCode = "2.2.1" });
                SessionRecorder.ResumeForTest(SampleActivity(), old);
                chk("a continued session opens on the task of its last step (" + SessionRecorder.TaskPos + ")", SessionRecorder.TaskPos == 1);
                chk("the actions already done are ticked again (" + SessionRecorder.DoneCount + ")", SessionRecorder.DoneCount == 3 && SessionRecorder.IsDone(0) && SessionRecorder.IsDone(4));
                var s7 = click("", "click"); SessionRecorder.Tag(s7);
                chk("the next click goes with the next action not used yet (" + s7.ActionCode + ")", s7.ActionCode == "2.2.2");
                // starting a recording again: no steps, the checklist unticked and back on the first task, the first click goes with the first action
                SessionRecorder.RestartForTest(SampleActivity());
                chk("starting again leaves no steps, nothing ticked and the first task open (" + SessionRecorder.Count + " steps, " + SessionRecorder.DoneCount + " ticked, task " + SessionRecorder.TaskPos + ")", SessionRecorder.Count == 0 && SessionRecorder.DoneCount == 0 && SessionRecorder.TaskPos == 0);
                var s8 = click("", "click"); SessionRecorder.Tag(s8);
                chk("after starting again the first click goes with the first action (" + s8.ActionCode + ")", s8.ActionCode == "2.1.1");
            }
            catch (Exception ex) { log.AppendLine("FAIL THROWN " + ex); all = false; }
            log.AppendLine(all ? "ALL PASSED" : "SOME FAILED"); File.WriteAllText(args[1], log.ToString()); return all ? 0 : 1;
        }
        // where does a window made "system DPI aware" land, and how big is it in real pixels? args: --dpi-probe <log>
        public static int DpiProbe(string[] args)
        {
            var log = new System.Text.StringBuilder();
            foreach (var sc in Screen.AllScreens) log.AppendLine("monitor " + sc.DeviceName + " " + sc.Bounds + " scale " + Native.MonitorScale(sc) + (sc.Primary ? " primary" : ""));
            Func<string, Func<Screen, Point>, int> run = delegate(string name, Func<Screen, Point> where)
            {
                foreach (var sc in Screen.AllScreens.Where(s => !s.Primary))
                {
                    Form f = null;
                    Native.WithDpi(false, delegate
                    {
                        f = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, TopMost = true, BackColor = Color.OrangeRed };
                        f.Size = new Size(Dpi.S(300), Dpi.S(100)); f.Location = where(sc); f.Show();
                    });
                    for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                    using (var b = Native.GrabVirtualScreen(false))
                    {
                        var vs = SystemInformation.VirtualScreen; int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1; var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        unsafe { for (int y = 0; y < b.Height; y += 2) { uint* row = (uint*)((byte*)d.Scan0 + (long)y * d.Stride); for (int x = 0; x < b.Width; x += 2) { uint c = row[x]; int r = (int)((c >> 16) & 255), g = (int)((c >> 8) & 255), bl = (int)(c & 255); if (r > 240 && g > 50 && g < 90 && bl < 30) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; } } } }
                        b.UnlockBits(d);
                        log.AppendLine(name + " on " + sc.DeviceName + ": asked for " + f.Location + ", it is " + (x1 < 0 ? "not found" : "at " + (x0 + vs.X) + "," + (y0 + vs.Y) + " and " + (x1 - x0 + 2) + " x " + (y1 - y0 + 2) + " real pixels") + "  (monitor bounds " + sc.Bounds + ")");
                    }
                    f.Close();
                }
                return 0;
            };
            run("physical position", sc => new Point(sc.Bounds.X + 100, sc.Bounds.Y + 100));
            run("position x ratio", sc => { float r = Dpi.Ratio(sc); return new Point((int)((sc.Bounds.X + 100) * r), (int)((sc.Bounds.Y + 100) * r)); });
            run("origin + offset x ratio", sc => { float r = Dpi.Ratio(sc); return new Point(sc.Bounds.X + (int)(100 * r), sc.Bounds.Y + (int)(100 * r)); });
            File.WriteAllText(args[1], log.ToString()); return 0;
        }
        // the editor on every monitor, photographed. args: --editor-monitors <prefix> <session>
        public static int EditorMonitors(string[] args)
        {
            var log = new System.Text.StringBuilder(); Settings.Folder = Path.GetDirectoryName(args[2]);
            EditorForm.ShowSession(args[2]); var f = EditorForm.Main; for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            int n = 0;
            foreach (var sc in Screen.AllScreens)
            {
                Native.WithDpi(false, delegate { f.StartPosition = FormStartPosition.Manual; float r = Dpi.Ratio(sc); f.Location = new Point((int)((sc.Bounds.X + 40) * r), (int)((sc.Bounds.Y + 40) * r)); });
                for (int i = 0; i < 25; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
                var wb = Native.WindowBounds(f.Handle);
                log.AppendLine("monitor " + sc.DeviceName + " " + sc.Bounds + " scale " + Native.MonitorScale(sc) + ": the editor measures " + wb + " there (" + (int)(100.0 * wb.Width / sc.Bounds.Width) + "% of the monitor's width)");
                using (var b = Native.Grab(sc.Bounds, false)) b.Save(args[1] + "-" + n++ + ".png", ImageFormat.Png);
            }
            File.WriteAllText(args[1] + ".txt", log.ToString()); EditorForm.Main.Close(); return 0;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc p, IntPtr l);
        delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        static string AwarenessOf(IntPtr h) { try { return new[] { "unaware", "system", "per-monitor" }[GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(h))]; } catch { return "?"; } }
        // how the drop-down list windows of this process are made, counted by awareness
        static string ListsSummary()
        {
            int sys = 0, per = 0; uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr l) { uint pid; GetWindowThreadProcessId(h, out pid); if (pid != me) return true; var c = new System.Text.StringBuilder(64); GetClassName(h, c, 64); if (c.ToString() == "ComboLBox") { if (AwarenessOf(h) == "system") sys++; else per++; } return true; }, IntPtr.Zero);
            return sys + " system-aware, " + per + " per-monitor";
        }
        static string Awareness(Control c) { try { return new[] { "unaware", "system", "per-monitor" }[GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(c.Handle))]; } catch { return "?"; } }
        static Control FindOfType(Control root, Type t) { foreach (Control c in root.Controls) { if (t.IsInstanceOfType(c)) return c; var f = FindOfType(c, t); if (f != null) return f; } return null; }
        // press the editor's Appearance button the way a click does and see what comes up. args: --look-test <prefix> <session>
        public static int LookTest(string[] args)
        {
            var log = new System.Text.StringBuilder(); Settings.Folder = Path.GetDirectoryName(args[2]);
            Hub hubForTest = null; if (args.Length > 4 && args[4] == "hub") { hubForTest = new Hub(); hubForTest.Show(); for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); } }
            EditorForm.ShowSession(args[2]); var ed = EditorForm.Main; for (int i = 0; i < 25; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            var onScreen = args.Length > 3 && args[3] == "p" ? Screen.PrimaryScreen : Screen.AllScreens.Where(s => !s.Primary).Skip(args.Length > 3 ? int.Parse(args[3]) : 0).First(); float rr = Dpi.Ratio(onScreen);
            ed.StartPosition = FormStartPosition.Manual; Native.WithDpi(false, delegate { ed.Location = new Point((int)((onScreen.Bounds.X + 60) * rr), (int)((onScreen.Bounds.Y + 60) * rr)); }); for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            var sv = (SessionView)FindOfType(ed, typeof(SessionView));
            var mb = (FlatButton[])typeof(SessionView).GetField("menuButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(sv);
            log.AppendLine("editor on " + onScreen.DeviceName + " " + onScreen.Bounds + ": " + Awareness(ed) + ", real window " + Native.WindowBounds(ed.Handle) + ", Appearance button is '" + mb[1].Text + "'");
            bool done = false, afterDone = false, okInside = false, okParked = false, okTick = false, okPick = false;
            var timer = new System.Windows.Forms.Timer { Interval = 1800 };
            timer.Tick += delegate
            {
                timer.Stop();      // runs inside the dialog's own message loop
                var look = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Appearance");
                if (look == null) log.AppendLine("NO APPEARANCE WINDOW");
                else
                {
                    log.AppendLine("editor window enabled at the Windows level while the dialog is open: " + IsWindowEnabled(ed.Handle)); log.AppendLine("drop-down list windows of this process: " + ListsSummary()); log.AppendLine("combo boxes inside the dialog: " + look.Controls.OfType<ThemedCombo>().Count() + ", with a window handle: " + look.Controls.OfType<ThemedCombo>().Count(c => c.IsHandleCreated) + ", really inside the dialog window: " + look.Controls.OfType<ThemedCombo>().Count(c => c.IsHandleCreated && GetParent(c.Handle) == look.Handle));
                    Func<Control, string> parked = null; parked = delegate(Control root0) { var bad = new System.Collections.Generic.List<string>(); Action<Control> walk = null; walk = delegate(Control c) { foreach (Control k in c.Controls) { if (k.IsHandleCreated && c.IsHandleCreated && GetParent(k.Handle) != c.Handle) bad.Add(k.GetType().Name); walk(k); } }; walk(root0); return bad.Count == 0 ? "none" : string.Join(", ", bad.ToArray()); };
                    log.AppendLine("controls whose window is not inside their parent's window: dialog " + parked(look) + "; editor " + parked(ed)); okParked = parked(look) == "none" && parked(ed) == "none";
                    okInside = look.Controls.OfType<ThemedCombo>().Count(c => c.IsHandleCreated && GetParent(c.Handle) == look.Handle) == 8;
                    { var c0 = look.Controls.OfType<ThemedCombo>().First(); var par = GetParent(c0.Handle); var sbn = new System.Text.StringBuilder(64); GetWindowText(par, sbn, 64); var cls = new System.Text.StringBuilder(80); GetClassName(par, cls, 80);
                      log.AppendLine("first combo: managed parent is the dialog " + (c0.Parent == look) + "; its window's parent is '" + sbn + "' (" + cls + ", " + AwarenessOf(par) + "), the combo itself is " + AwarenessOf(c0.Handle) + ", the dialog is " + AwarenessOf(look.Handle)); } log.AppendLine("appearance window: " + Awareness(look) + ", visible " + look.Visible + ", enabled " + look.Enabled + ", bounds " + look.Bounds + ", client " + look.ClientSize + ", controls " + look.Controls.Count + ", editor enabled " + ed.Enabled);
                    var vs = SystemInformation.VirtualScreen; log.AppendLine("virtual screen " + vs + ", the dialog really is at " + Native.WindowBounds(look.Handle));
                    using (var b = Native.GrabVirtualScreen(false)) b.Save(args[1] + ".png", ImageFormat.Png);
                    // real mouse: move to the controls (UI Automation gives real screen positions) and click
                    Func<Control, Point> centre = delegate(Control c) { var r = System.Windows.Automation.AutomationElement.FromHandle(c.Handle).Current.BoundingRectangle; return new Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)); };
                    Action<Point> clickAt = delegate(Point pt) { SetCursorPos(pt.X, pt.Y); System.Threading.Thread.Sleep(120); mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4, 0, 0, 0, UIntPtr.Zero); for (int i = 0; i < 8; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); } };
                    var boxes = look.Controls.OfType<ThemedCheck>().First(); bool before = boxes.Checked; var cp = centre(boxes);
                    clickAt(cp); log.AppendLine("clicked the checkbox '" + boxes.Text + "' at " + cp + ": checked " + before + " -> " + boxes.Checked); okTick = before != boxes.Checked;
                    var combo = look.Controls.OfType<ThemedCombo>().Skip(1).First(); var cc = centre(combo); clickAt(cc); log.AppendLine("clicked a drop-down at " + cc + ": list open " + combo.DroppedDown);
                    using (var b2 = Native.GrabVirtualScreen(false)) b2.Save(args[1] + "-combo.png", ImageFormat.Png);
                    if (combo.DroppedDown)
                    {   // pick the second item with the mouse: the list is just under the box, one row (26 layout pixels at this monitor's scale) per item
                        var br = System.Windows.Automation.AutomationElement.FromHandle(combo.Handle).Current.BoundingRectangle; float ms = Native.MonitorScale(Screen.FromPoint(cc)) ;
                        var item = new Point(cc.X, (int)(cc.Y + combo.Height * ms / Dpi.K / 2f + 26 * ms * 1.5f)); clickAt(item);
                        log.AppendLine("picked the 2nd item at " + item + ": selected index " + combo.SelectedIndex + " (" + combo.Text + "), list " + (combo.DroppedDown ? "still open" : "closed")); okPick = combo.SelectedIndex == 1;
                    }
                    if (combo.DroppedDown) { clickAt(centre(look.Controls.OfType<Label>().First())); }      // click elsewhere: the list closes
                    log.AppendLine("after clicking elsewhere the list is " + (combo.DroppedDown ? "still open" : "closed"));
                    var done2 = look.Controls.OfType<FlatButton>().First(b => b.Text == "Done"); int doneClicks = 0; done2.Click += delegate { doneClicks++; }; var dc = centre(done2); clickAt(dc); log.AppendLine("the Done button got " + doneClicks + " click(s); dialog result " + look.DialogResult + ", cursor at " + Cursor.Position);
                    log.AppendLine("clicked Done at " + dc + ": the window is " + (look.IsDisposed || !look.Visible ? "closed" : "STILL OPEN"));
                    if (!look.IsDisposed && look.Visible) { look.DialogResult = DialogResult.Cancel; look.Close(); }
                    var after = new System.Windows.Forms.Timer { Interval = 700 };
                    after.Tick += delegate { after.Stop(); log.AppendLine("after the dialog closed: editor window enabled " + IsWindowEnabled(ed.Handle) + ", active form " + (Form.ActiveForm == null ? "none" : Form.ActiveForm.Text)); afterDone = true; };
                    after.Start();
                }
                done = true;
            };
            var sw = System.Diagnostics.Stopwatch.StartNew(); long shownAt = -1;
            var watch = new System.Windows.Forms.Timer { Interval = 25 };
            watch.Tick += delegate { var f = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x.Text == "Appearance" && x.Visible); if (f != null && shownAt < 0) { shownAt = sw.ElapsedMilliseconds; log.AppendLine("the Appearance window is on screen " + shownAt + " ms after the click"); watch.Stop(); } };
            watch.Start();
            timer.Start();
            if (args.Length > 4 && args[4] == "viatimer")      // the dialog made from a timer's message instead of the button's: the thread is per-monitor then
            { var go = new System.Windows.Forms.Timer { Interval = 50 }; go.Tick += delegate { go.Stop(); Native.WithDpi(true, delegate { typeof(SessionView).GetMethod("ShowLook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(sv, null); }); }; go.Start(); }
            else PostMessage(mb[1].Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < 300 && !(done && afterDone); i++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
            if (!done) log.AppendLine("TIMED OUT before the dialog could be inspected");
            bool pass = done && afterDone && okInside && okParked && okTick && okPick;
            log.AppendLine((okInside ? "PASS" : "FAIL") + " the Appearance window's drop-down lists sit inside it");
            log.AppendLine((okParked ? "PASS" : "FAIL") + " no control of the dialog or the editor is outside its parent's window");
            log.AppendLine((okTick ? "PASS" : "FAIL") + " a real click on a check box in the window changes it");
            log.AppendLine((okPick ? "PASS" : "FAIL") + " a real click picks an item from a drop-down list");
            log.AppendLine(pass ? "ALL PASSED" : "SOME FAILED"); File.WriteAllText(args[1] + ".txt", log.ToString()); Environment.Exit(pass ? 0 : 1); return pass ? 0 : 1;
        }
        // the real route to the editor: record, press Stop on the recording bar, then press Appearance. args: --look-test2 <prefix> <folder>
        public static int LookTest2(string[] args)
        {
            var log = new System.Text.StringBuilder(); Directory.CreateDirectory(args[2]); Settings.Folder = args[2];
            var hub = new Hub(); hub.Show(); for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            var T = typeof(SessionRecorder); var st = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
            if (args.Length > 3 && args[3] == "plan") SessionRecorder.Activity = SampleActivity();
            SessionRecorder.Start("monitor", Screen.PrimaryScreen.Bounds, IntPtr.Zero);
            for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            // one step, as a click would make it
            using (var bmp = new Bitmap(800, 450)) using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.LightGray); var step = new SessionStep { Kind = "click", Label = "Item", Role = "Button", X = 100, Y = 100, Box = new Rectangle(80, 80, 60, 30), Url = "app://test/Test", Title = "Test" };
                var push = T.GetMethod("Push", st); var clone = new Bitmap(bmp); push.Invoke(null, new object[] { step, clone, new Rectangle(0, 0, 800, 450), "test::Test" });
            }
            var bar = (Form)T.GetField("bar", st).GetValue(null); var chk = T.GetField("checklist", st).GetValue(null);
            log.AppendLine("recording bar: " + (bar != null ? Awareness(bar) : "none") + ", checklist: " + (chk != null ? "yes" : "none"));
            Form stopHost = bar ?? (Form)chk ?? (Form)T.GetField("recPanel", st).GetValue(null); log.AppendLine("Stop button lives in: " + stopHost.GetType().Name + " (" + Awareness(stopHost) + ")");
            Func<Control, FlatButton> findStop = null; findStop = delegate(Control c) { foreach (Control k in c.Controls) { var fb = k as FlatButton; if (fb != null && fb.Text == "Stop") return fb; var r = findStop(k); if (r != null) return r; } return null; };
            var stopBtn = findStop(stopHost);
            bool opened = false; var wait = new System.Windows.Forms.Timer { Interval = 100 };
            PostMessage(stopBtn.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);       // Stop, the way a click does it
            for (int i = 0; i < 100 && EditorForm.Main == null; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            var ed = EditorForm.Main; for (int i = 0; i < 25; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            if (ed == null) { log.AppendLine("NO EDITOR"); File.WriteAllText(args[1] + ".txt", log.ToString()); return 1; }
            log.AppendLine("editor: " + Awareness(ed) + ", " + ed.Bounds);
            var sv = (SessionView)FindOfType(ed, typeof(SessionView));
            var mb = (FlatButton[])typeof(SessionView).GetField("menuButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(sv);
            bool done = false;
            var timer = new System.Windows.Forms.Timer { Interval = 1800 };
            timer.Tick += delegate
            {
                timer.Stop(); var look = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Appearance");
                if (look == null) log.AppendLine("NO APPEARANCE WINDOW");
                else { log.AppendLine("Appearance: " + Awareness(look) + ", combo boxes " + look.Controls.OfType<ThemedCombo>().Count() + ", with a window handle " + look.Controls.OfType<ThemedCombo>().Count(c => c.IsHandleCreated)); look.DialogResult = DialogResult.Cancel; look.Close(); }
                done = true;
            };
            timer.Start(); PostMessage(mb[1].Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < 200 && !done; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
            File.WriteAllText(args[1] + ".txt", log.ToString()); return 0;
        }
        // the capture window on every monitor: the same size on each, not the main monitor's pixels everywhere. args: --hub-monitors <prefix>
        public static int HubMonitors(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool all = true; Hub hub = null;
            Native.WithDpi(false, delegate { hub = new Hub(); var h = hub.Handle; hub.StartPosition = FormStartPosition.Manual; hub.Show(); });
            for (int i = 0; i < 15; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            int n = 0;
            foreach (var sc in Screen.AllScreens)
            {
                float r = Dpi.Ratio(sc), ms = Native.MonitorScale(sc);
                Native.WithDpi(false, delegate { hub.Location = new Point((int)((sc.Bounds.X + 40) * r), (int)((sc.Bounds.Y + 40) * r)); });
                for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
                var wb = Native.WindowBounds(hub.Handle); double wantW = 890 * ms, share = 100.0 * wb.Width / sc.Bounds.Width;
                bool ok = wb.Width <= wantW * 1.1 + 60 && wb.Width >= wantW * 0.9;
                log.AppendLine((ok ? "PASS" : "FAIL") + " capture window on " + sc.DeviceName + " (scale " + ms + "): " + wb.Width + " x " + wb.Height + " real pixels, " + (int)share + "% of the monitor's width (about " + wantW + " wanted)"); all &= ok;
                using (var b = Native.Grab(sc.Bounds, false)) b.Save(args[1] + "-" + n++ + ".png", ImageFormat.Png);
            }
            log.AppendLine(all ? "ALL PASSED" : "SOME FAILED"); File.WriteAllText(args[1] + ".txt", log.ToString()); Environment.Exit(all ? 0 : 1); return all ? 0 : 1;
        }
        // start a capture from the (system-DPI aware) capture window: the picking screen must still cover the real desktop in real pixels. args: --hub-capture <log>
        public static int HubCapture(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool ok = false, done = false; Hub hub = null;
            Native.WithDpi(false, delegate { hub = new Hub(); var h = hub.Handle; hub.Show(); });
            for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate
            {
                timer.Stop(); var ov = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is PickOverlay);
                if (ov == null) log.AppendLine("FAIL no picking screen came up");
                else
                {
                    Rectangle real = Rectangle.Empty; Native.WithDpi(true, delegate { real = SystemInformation.VirtualScreen; });
                    var wb = Native.WindowBounds(ov.Handle);
                    ok = Awareness(ov) == "per-monitor" && wb.Width == real.Width && wb.Height == real.Height;
                    log.AppendLine((ok ? "PASS" : "FAIL") + " the picking screen is " + Awareness(ov) + " and " + wb.Width + " x " + wb.Height + " real pixels; the desktop is " + real.Width + " x " + real.Height);
                    ov.Close();
                }
                done = true;
            };
            timer.Start();
            hub.Begin(Modes.All[0]);
            for (int i = 0; i < 200 && !done; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
            log.AppendLine(ok ? "ALL PASSED" : "SOME FAILED"); File.WriteAllText(args[1], log.ToString()); Environment.Exit(ok ? 0 : 1); return ok ? 0 : 1;
        }
        // the Claude status window, fed the same events the control channel raises. args: --status-shot <prefix>
        public static int StatusShot(string[] args)
        {
            var f = new ClaudeStatusForm("Raise Issue", 5); f.Show(); Application.DoEvents();
            var raise = typeof(RemoteControl).GetMethod("Raise", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Action<string, string, string, string> ev = delegate(string kind, string task, string code, string text)
            {
                var d = new Dictionary<string, object> { { "task", task }, { "actionCode", code }, { "stepText", text } }; raise.Invoke(null, new object[] { kind, d });
                for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
            };
            Action<string> snap = delegate(string n) { Application.DoEvents(); System.Threading.Thread.Sleep(200); Application.DoEvents(); using (var b = new Bitmap(f.Width, f.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(f.Location, Point.Empty, f.Size); b.Save(args[1] + "-" + n + ".png", ImageFormat.Png); } };
            snap("start");
            ev("cmd:click", "3.1 Navigate to the Issue log register", "3.1.1", "Click the hamburger menu"); ev("ok:click", "", "", "");
            ev("cmd:click", "3.1 Navigate to the Issue log register", "3.1.2", "Select the \"Project\" under Favorites"); ev("ok:click", "", "", "");
            ev("cmd:type", "3.3 Enter the issue details", "3.3.1", "Enter the issue name in \"Issue name\"");
            snap("working");
            ev("ok:type", "", "", "");
            f.Finish(true, "Saved the session Raise Issue with 12 steps.", "", "C:\\x\\Raise Issue.clicktrail"); snap("done");
            var f2 = new ClaudeStatusForm("Raise Issue", 5); f2.Location = new Point(f.Left, f.Top - f.Height - 20); f2.Show(); Application.DoEvents();
            f2.Finish(false, "The window \"Change\" was not found. Open InEight and try again.", "", ""); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            using (var b = new Bitmap(f2.Width, f2.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(f2.Location, Point.Empty, f2.Size); b.Save(args[1] + "-failed.png", ImageFormat.Png); }
            f2.Close(); f.Close(); return 0;
        }
        // a crafted project file must not be able to run code or exhaust memory. args: --security-test <log> <folder>
        public static int SecurityTest(string[] args)
        {
            try { return SecurityTestCore(args); } catch (Exception ex) { File.WriteAllText(args[1], "CRASH " + ex); return 2; }
        }
        static int SecurityTestCore(string[] args)
        {
            var log = new System.Text.StringBuilder(); bool all = true; Action<string, bool> chk = delegate(string n, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + n); all &= ok; };
            Directory.CreateDirectory(args[2]);
            // 1. every kind of drawn object still saves and opens through the allow-list
            var types = typeof(Obj).Assembly.GetTypes().Where(t => typeof(Obj).IsAssignableFrom(t) && !t.IsAbstract).ToList(); var objs = new List<Obj>(); var failed = new List<string>();
            foreach (var t in types) { try { var o = (Obj)Activator.CreateInstance(t); o.A = new PointF(20 + objs.Count, 20); o.B = new PointF(90 + objs.Count, 80); objs.Add(o); } catch (Exception ex) { failed.Add(t.Name + ": " + ex.Message); } }
            chk("every object class could be made (" + types.Count + ")", failed.Count == 0);
            var doc = new Doc(new Bitmap(200, 120)) { Name = "sec" }; foreach (var o in objs) doc.Objs.Add(o);
            string good = Path.Combine(args[2], "good.snapl"); Project.Save(doc, good);
            try { var back = Project.Load(good); chk("all " + objs.Count + " object kinds survive save and open", back.Objs.Count == objs.Count); }
            catch (Exception ex) { chk("all object kinds survive save and open: " + ex.Message, false); }
            // 2. files saved before the rename name the assembly SnapLite: the binder maps it to this one
            string old = "System.Collections.Generic.List`1[[SnapLite.Obj, SnapLite, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]";
            Type mapped = null; try { mapped = Type.GetType(ProjectBinder.Remap(old) + ", mscorlib", false); } catch { }
            chk("a list of objects from an old SnapLite file maps to this program", mapped == typeof(List<Obj>));
            var topAsm = "SnapLite, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"; Type topType = null; try { topType = new ProjectBinder().BindToType(topAsm, "SnapLite.ShapeObj"); } catch { }
            chk("an object class from an old SnapLite file (assembly named SnapLite) binds to this program", topType == typeof(ShapeObj));
            // 3. a project that names some other .NET type is refused, and nothing runs
            string evil = Path.Combine(args[2], "evil.snapl"); File.Copy(good, evil, true);
            using (var fs = new FileStream(evil, FileMode.Open)) using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Update))
            {
                zip.GetEntry("objects.bin").Delete(); var e = zip.CreateEntry("objects.bin");
                using (var s = e.Open()) new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter().Serialize(s, new List<object> { new Uri("http://example.invalid/"), new System.Collections.Hashtable() });
            }
            bool refused = false; string why = ""; try { Project.Load(evil); } catch (System.Runtime.Serialization.SerializationException ex) { refused = true; why = ex.Message; } catch (Exception ex) { why = ex.GetType().Name + " " + ex.Message; }
            chk("a project naming another .NET type is refused (" + (why.Length > 70 ? why.Substring(0, 70) : why) + ")", refused);
            using (var ms = new MemoryStream())
            {
                var bf = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter(); bf.Serialize(ms, new List<object> { new Uri("http://example.invalid/") });
                ms.Position = 0; var plain = (List<object>)new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter().Deserialize(ms);
                chk("(for comparison) the plain reader builds any type named in the file: it made a " + plain[0].GetType().Name, plain[0] is Uri);
            }
            // 4. a project whose data would unpack to far too much is refused
            string bomb = Path.Combine(args[2], "bomb.snapl"); File.Copy(good, bomb, true);
            using (var fs = new FileStream(bomb, FileMode.Open)) using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Update))
            {
                zip.GetEntry("objects.bin").Delete(); var e = zip.CreateEntry("objects.bin", System.IO.Compression.CompressionLevel.Optimal);
                using (var s = e.Open()) { var zeros = new byte[1 << 20]; for (int i = 0; i < 80; i++) s.Write(zeros, 0, zeros.Length); }
            }
            bool bombRefused = false; try { Project.Load(bomb); } catch (InvalidDataException) { bombRefused = true; } catch { }
            chk("a project that unpacks to 80 MB of objects is refused (the file is " + new FileInfo(bomb).Length / 1024 + " KB)", bombRefused);
            // 5. the safe types: a hand-made file with only allowed types still opens
            log.AppendLine(all ? "ALL PASSED" : "SOME FAILED"); File.WriteAllText(args[1], log.ToString()); return all ? 0 : 1;
        }
        public static int PickerShot(string[] args)
        {
            var d = ColorPickerDialog.ForShot(Color.FromArgb(0xFF, 0xC3, 0x00)); d.StartPosition = FormStartPosition.Manual; d.Location = new Point(60, 60); d.TopMost = true; d.Show();
            Application.DoEvents(); System.Threading.Thread.Sleep(600); Application.DoEvents();
            using (var b = Shot(d)) b.Save(args[1], ImageFormat.Png);
            // typed values round-trip
            d.Close(); return 0;
        }
        // the session editor with rounded boxes and a step selected (its round corner dot shows), then the step's right-click menu. args: --round-shot <prefix> <session.clicktrail> <folder>
        public static int RoundShot(string[] args)
        {
            var log = new System.Text.StringBuilder(); string where = "start";
            try
            {
                Settings.Folder = args[3];
                var hub = new Hub(); hub.Show(); Application.DoEvents(); where = "open session";
                EditorForm.ShowSession(args[2]);
                var f = EditorForm.Main; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(30, 30);
                Application.DoEvents(); System.Threading.Thread.Sleep(1500); Application.DoEvents(); where = "find the session view";
                var nb = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var sv = (SessionView)typeof(EditorForm).GetField("sessionView", nb).GetValue(f); where = "set the look";
                log.AppendLine("session path: " + args[2] + " exists " + File.Exists(args[2]) + "; session view " + (sv == null ? "missing" : "there, document " + (sv.Doc == null ? "missing" : sv.Doc.Steps.Count + " steps")) + "; editor title " + f.Text);
                sv.Doc.BoxShape = "rounded"; var st = sv.Doc.Steps.Count > 2 ? sv.Doc.Steps[2] : sv.Doc.Steps[0]; st.Round = 0.12f; where = "select the step";
                typeof(SessionView).GetMethod("SelectStep", nb).Invoke(sv, new object[] { st }); where = "first picture";
                sv.Invalidate(true); Application.DoEvents(); System.Threading.Thread.Sleep(600); Application.DoEvents();
                using (var b = Shot(f)) b.Save(args[1] + "-dot.png", ImageFormat.Png); where = "step menu";
                var menu = (ContextMenuStrip)typeof(SessionView).GetMethod("StepMenu", nb).Invoke(sv, null);
                var list = (Control)typeof(SessionView).GetField("list", nb).GetValue(sv);
                menu.Show(list, new Point(60, 80)); for (int i = 0; i < 25; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(40); } where = "second picture";       // the menu fades in: wait until it is solid
                using (var b = Shot(f)) b.Save(args[1] + "-menu.png", ImageFormat.Png);
                menu.Close(); sv.Doc.Dirty = false; where = "quit"; log.AppendLine("done"); File.WriteAllText(args[1] + ".txt", log.ToString()); hub.Quit();
            }
            catch (Exception ex) { log.AppendLine("FAILED at " + where + ": " + ex); File.WriteAllText(args[1] + ".txt", log.ToString()); return 1; }
            return 0;
        }
        public static int SessionShot(string[] args)
        {
            Settings.Folder = args[3];
            var hub = new Hub(); hub.SetTab("session"); hub.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            using (var b = Shot(hub)) b.Save(args[1] + "-hub.png", ImageFormat.Png);
            hub.SetTab("image");
            EditorForm.ShowSession(args[2]);
            var f = EditorForm.Main; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(30, 30);
            Application.DoEvents(); System.Threading.Thread.Sleep(1500); Application.DoEvents(); f.RefreshTray(); Application.DoEvents(); System.Threading.Thread.Sleep(800); Application.DoEvents();
            using (var b = Shot(f)) b.Save(args[1] + "-editor.png", ImageFormat.Png);
            hub.Quit(); return 0;
        }

        // time each stage between pressing Capture and the overlay being ready, on the real monitors
        public static int GrabTiming(string[] args)
        {
            var log = new System.Text.StringBuilder(); var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var s in Screen.AllScreens) log.AppendLine("monitor " + s.DeviceName + " " + s.Bounds + (s.Primary ? " primary" : ""));
            log.AppendLine("virtual screen " + SystemInformation.VirtualScreen + ", Dpi.K " + Dpi.K);
            for (int run = 0; run < 3; run++)
            {
                sw.Restart(); Native.WaitForRepaint(); long t0 = sw.ElapsedMilliseconds;
                sw.Restart(); var shot = Native.GrabVirtualScreen(false); long t1 = sw.ElapsedMilliseconds;
                sw.Restart(); var ov = new PickOverlay(shot, CapMode.AllInOne); long t2 = sw.ElapsedMilliseconds;
                var vs0 = SystemInformation.VirtualScreen; var pr = Screen.PrimaryScreen.Bounds; var at = new Point(pr.X - vs0.X + pr.Width / 3, pr.Y - vs0.Y + pr.Height / 3);
                sw.Restart(); using (var b = ov.Snapshot(at, false, false)) { if (run == 0) { using (var sm = new Bitmap(b, b.Width / 4, b.Height / 4)) sm.Save(args[1] + "-overlay.png", ImageFormat.Png); } } long t3 = sw.ElapsedMilliseconds;
                log.AppendLine("run " + run + ": wait " + t0 + " ms, grab " + t1 + " ms, overlay setup " + t2 + " ms, one full repaint " + t3 + " ms");
                if (run == 0) shot.Save(args[1] + "-shot.png", ImageFormat.Png);
                ov.Dispose(); shot.Dispose();
            }
            File.WriteAllText(args[1] + ".txt", log.ToString()); return 0;
        }

        // the recent tray with the pointer over a card, and over that card's delete button
        public static int TrayHoverShot(string[] args)
        {
            Settings.Folder = args[2];
            EditorForm.ShowEditor(); var f = EditorForm.Main; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(30, 30);
            Application.DoEvents(); f.RefreshTray(); Application.DoEvents(); System.Threading.Thread.Sleep(1200); Application.DoEvents();
            var tr = f.tray; var pt = tr.PointToScreen(Point.Empty); var fo = f.PointToScreen(Point.Empty);
            for (int k = 0; k < 2; k++)
            {
                tr.FakeHover(1, k == 1); Application.DoEvents();
                using (var b = Shot(f))
                {
                    int ox = pt.X - fo.X + (b.Width - f.ClientSize.Width) / 2, oy = pt.Y - fo.Y + (b.Height - f.ClientSize.Height) - (b.Width - f.ClientSize.Width) / 2;
                    var crop = Rectangle.Intersect(new Rectangle(ox, oy, Math.Min(tr.Width, Dpi.S(620)), tr.Height), new Rectangle(0, 0, b.Width, b.Height));
                    using (var c = b.Clone(crop, b.PixelFormat)) c.Save(args[1] + (k == 0 ? "-hover.png" : "-hoverdelete.png"), ImageFormat.Png);
                }
            }
            f.Close(); return 0;
        }

        public static int OcrCheck(string[] args)
        {
            using (var b = new Bitmap(args[1]))
            {
                var r = Ocr.Recognize(b);
                File.WriteAllText(args[2], "lang=" + r.Language + "\nwords=" + r.Words.Count + "\n" + r.Text);
            }
            return 0;
        }

        // build a tall synthetic page, "scroll" it by random offsets, and check the stitcher rebuilds it
        public static int StitchTest(string[] args)
        {
            int W = 600, H = 3000, win = 500; var rnd = new Random(3);
            var page = new Bitmap(W, H);
            using (var g = Graphics.FromImage(page))
            {
                g.Clear(Color.White);
                for (int i = 0; i < 90; i++)
                {
                    using (var b = new SolidBrush(Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)))) g.FillRectangle(b, rnd.Next(W - 80), i * 33, 40 + rnd.Next(200), 18);
                    using (var f = new Font("Segoe UI", 10f)) g.DrawString("Line " + i + " lorem ipsum dolor sit amet " + rnd.Next(99999), f, Brushes.Black, 10, i * 33 + 1);
                }
            }
            var shots = new List<Bitmap>(); int y = 0;
            while (true)
            {
                shots.Add(page.Clone(new Rectangle(0, Math.Min(y, H - win), W, win), PixelFormat.Format32bppArgb));
                if (y >= H - win) break;
                y += 180 + rnd.Next(60);
            }
            shots.Add((Bitmap)shots[shots.Count - 1].Clone());   // end of page: identical frame
            string note; var res = ScrollCapture.Stitch(shots, out note);
            int diff = -1; double avg = 0;
            if (res != null && res.Height == H) { int s1, s2; var a = Effects.Bytes(res, out s1); var b = Effects.Bytes(page, out s2); long sum = 0; for (int i = 0; i < a.Length; i += 4) sum += Math.Abs(a[i] - b[i]); avg = sum / (double)(a.Length / 4); diff = 0; }
            File.WriteAllText(args[1], "frames=" + shots.Count + "\nexpected height=" + H + "\ngot height=" + (res == null ? -1 : res.Height) + "\navg pixel diff=" + avg.ToString("0.000") + "\nnote=" + note + "\n");
            return 0;
        }
    }
}
#endif
