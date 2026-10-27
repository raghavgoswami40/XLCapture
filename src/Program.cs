using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SnapLite
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Native.UsePerMonitorDpi();
            // The list of monitors WinForms keeps is made once, here, with real pixels. A system-DPI-aware window (the editor) would otherwise make it
            // with stretched ones, and capture would be off on the other monitors. It is made again with real pixels whenever the monitors change.
            var monitors = Screen.AllScreens;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += delegate { Native.WithDpi(true, delegate { var again = Screen.AllScreens; }); };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 1 && args[0] == "--install-skill") return SkillInstaller.EnsureInstalled() ? 0 : 1;      // used by the installer
#if DEVCHECKS      // the test switches change settings and the library on purpose: they must not touch the person's own
            Settings.DevFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xlcapture-dev-settings.txt"); LibraryStore.DevFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xlcapture-dev-library.txt");
#endif
            Settings.Load(); Hub.LoadProfiles();
            SkillInstaller.EnsureInstalled();
#if DEVCHECKS      // the developer's test switches: left out of the program that is shared
            if (args.Length >= 1 && args[0] == "--editor-shot") return DevChecks.EditorShot(args);
            if (args.Length >= 1 && args[0] == "--round-shot") return DevChecks.RoundShot(args);
            if (args.Length >= 1 && args[0] == "--ocr") return DevChecks.OcrCheck(args);
            if (args.Length >= 1 && args[0] == "--hub-shot") return DevChecks.HubShot(args);
            if (args.Length >= 1 && args[0] == "--rec-test") return DevChecks.RecTest(args);
            if (args.Length >= 1 && args[0] == "--doc-test") return DevChecks.DocTest(args);
            if (args.Length >= 1 && args[0] == "--overlay-shot") return DevChecks.OverlayShot(args);
            if (args.Length >= 1 && args[0] == "--grab-timing") return DevChecks.GrabTiming(args);
            if (args.Length >= 1 && args[0] == "--tray-hover") return DevChecks.TrayHoverShot(args);
            if (args.Length >= 1 && args[0] == "--presets-shot") return DevChecks.PresetsShot(args);
            if (args.Length >= 1 && args[0] == "--window-pick") return DevChecks.WindowPick(args);
            if (args.Length >= 1 && args[0] == "--session-test") return DevChecks.SessionTest(args);
            if (args.Length >= 1 && args[0] == "--open-editor") return DevChecks.OpenEditor(args);
            if (args.Length >= 1 && args[0] == "--dialogs-shot") return DevChecks.DialogsShot(args);
            if (args.Length >= 1 && args[0] == "--pick-test") return DevChecks.PickTest(args);
            if (args.Length >= 1 && args[0] == "--share-test") return DevChecks.ShareTest(args);
            if (args.Length >= 1 && args[0] == "--slides-test") return DevChecks.SlidesTest(args);
            if (args.Length >= 1 && args[0] == "--plan-test") return DevChecks.PlanTest(args);
            if (args.Length >= 1 && args[0] == "--dpi-probe") return DevChecks.DpiProbe(args);
            if (args.Length >= 1 && args[0] == "--look-test") return DevChecks.LookTest(args);
            if (args.Length >= 1 && args[0] == "--hub-monitors") return DevChecks.HubMonitors(args);
            if (args.Length >= 1 && args[0] == "--hub-capture") return DevChecks.HubCapture(args);
            if (args.Length >= 1 && args[0] == "--look-test2") return DevChecks.LookTest2(args);
            if (args.Length >= 1 && args[0] == "--editor-monitors") return DevChecks.EditorMonitors(args);
            if (args.Length >= 1 && args[0] == "--status-shot") return DevChecks.StatusShot(args);
            if (args.Length >= 1 && args[0] == "--security-test") return DevChecks.SecurityTest(args);
            if (args.Length >= 1 && args[0] == "--menu-shot") return DevChecks.MenuShot(args);
            if (args.Length >= 1 && args[0] == "--av-test") return DevChecks.AvTest(args);
            if (args.Length >= 1 && args[0] == "--picker-shot") return DevChecks.PickerShot(args);
            if (args.Length >= 1 && args[0] == "--session-shot") return DevChecks.SessionShot(args);
            if (args.Length >= 1 && args[0] == "--video-test") return DevChecks.VideoTest(args);
            if (args.Length >= 1 && args[0] == "--stitch-test") return DevChecks.StitchTest(args);
            if (args.Length >= 1 && args[0] == "--driver-test") return DevChecks.DriverTest(args);
            if (args.Length >= 1 && args[0] == "--browser-test") return DevChecks.BrowserTest(args);
            if (args.Length >= 1 && args[0] == "--bot-shot") return DevChecks.BotShot(args);
            if (args.Length >= 1 && args[0] == "--proc-test") return DevChecks.ProcTest(args);
            if (args.Length >= 1 && args[0] == "--wizard-shot") return DevChecks.WizardShot(args);
            if (args.Length >= 1 && args[0] == "--editor-test") return DevChecks.EditorTest(args);
            if (args.Length >= 1 && args[0] == "--capture-shot") return DevChecks.CaptureShot(args);
            if (args.Length >= 1 && args[0] == "--pixel-test") return DevChecks.PixelTest(args);
#endif
            if (args.Length >= 1 && args[0] == "--cmd") return RemoteControl.Client(args);

            bool created;
            using (var m = new Mutex(true, "SnapLite.SingleInstance.v2", out created))
            {
                if (!created) { MessageBox.Show("XLCapture is already running. Look for its icon in the system tray.", "XLCapture"); return 0; }
                // the program used to start with Windows as "SnapLite": move that entry to the new name and path
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        if (k != null && k.GetValue("SnapLite") != null) { k.DeleteValue("SnapLite", false); k.SetValue("XLCapture", "\"" + Application.ExecutablePath + "\" --tray"); }
                }
                catch { }
                // the capture window is laid out in the startup scale and stretched by Windows on a monitor with another scale (like the editor); its window is made here so it keeps that
                Hub hub = null; Native.WithDpi(false, delegate { hub = new Hub(); var made = hub.Handle; });
                EditorForm.NewCaptureRequested = delegate { Native.WithDpi(true, delegate { hub.CaptureCurrent(); }); };      // the editor is system-DPI aware; capturing needs real pixels
                if (args.Contains("--control")) RemoteControl.Start();
                bool tray = args.Contains("--tray");
                var files = args.Where(a => File.Exists(a)).ToList();
                if (!tray) Native.WithDpi(false, delegate { hub.Show(); });
                foreach (var f in files) { try { EditorForm.Show(EditorForm.LoadAny(f)); } catch { } }
                Application.Run();
            }
            return 0;
        }
    }
}
