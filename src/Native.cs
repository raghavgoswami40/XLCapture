// Win32 interop and screen-grab helpers.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SnapLite
{
    class WinInfo
    {
        public IntPtr Handle; public string Title; public Rectangle Rect;
    }

    static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }
        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct INPUT { [FieldOffset(0)] public int type; [FieldOffset(8)] public MOUSEINPUT mi; }

        public delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        // Per-monitor DPI awareness, so screen pixels, window bounds (DWM), UI Automation and mouse hooks all
        // use the same physical coordinates. A merely system-aware process sees other-DPI monitors through
        // Windows' scaling, which shifted and stretched the frozen screen in the capture overlay.
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dx, out uint dy);
        static readonly Dictionary<string, float> monitorScale = new Dictionary<string, float>();
        // a monitor's own scale (1.0 = 100%, 2.5 = 250%), whatever the thread's awareness is at the moment
        public static float MonitorScale(Screen sc)
        {
            float v; if (monitorScale.TryGetValue(sc.DeviceName, out v)) return v;
            v = Dpi.K;
            WithDpi(true, delegate
            {
                try
                {
                    uint dx, dy; var h = MonitorFromPoint(new POINT { X = sc.Bounds.X + sc.Bounds.Width / 2, Y = sc.Bounds.Y + sc.Bounds.Height / 2 }, 2);
                    if (h != IntPtr.Zero && GetDpiForMonitor(h, 0, out dx, out dy) == 0 && dx > 0) v = dx / 96f;
                }
                catch { }
            });
            monitorScale[sc.DeviceName] = v; return v;
        }
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        // Run something with the thread's DPI awareness set to "system" (the layout's own scale, stretched by Windows on monitors with another scale)
        // or "per monitor" (real pixels). A window keeps the awareness it was created with; while it handles a message the thread follows the window.
        public static void WithDpi(bool perMonitor, Action a)
        {
            IntPtr prev = IntPtr.Zero;
            try { prev = SetThreadDpiAwarenessContext(new IntPtr(perMonitor ? -4 : -2)); } catch (EntryPointNotFoundException) { }
            try { a(); } finally { if (prev != IntPtr.Zero) { try { SetThreadDpiAwarenessContext(prev); } catch (EntryPointNotFoundException) { } } }
        }
        public static void UsePerMonitorDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }

        // Wait until windows we just made transparent are gone from the screen (one or two DWM frames)
        public static void WaitForRepaint()
        {
            System.Windows.Forms.Application.DoEvents();
            try { DwmFlush(); DwmFlush(); } catch (DllNotFoundException) { System.Threading.Thread.Sleep(50); }
        }
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO ii);
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public static void MoveOffscreen(IntPtr h) { SetWindowPos(h, IntPtr.Zero, -32000, -32000, 0, 0, 0x0001 | 0x0004 | 0x0010); }
        public static void MoveTo(IntPtr h, int x, int y) { SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); }

        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);

        // ---- global input hooks (session recording)
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public int mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);

        public static IntPtr RootAt(Point p) { var h = WindowFromPoint(new POINT { X = p.X, Y = p.Y }); return h == IntPtr.Zero ? h : GetAncestor(h, 2); }
        public static IntPtr Root(IntPtr h) { return h == IntPtr.Zero ? h : GetAncestor(h, 2); }
        public static int ProcessIdOf(IntPtr h) { uint pid; GetWindowThreadProcessId(h, out pid); return (int)pid; }
        public static string TitleOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
        // the window's visible bounds (without the invisible resize border), screen pixels
        public static Rectangle WindowBounds(IntPtr h)
        {
            RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(h, out r)) return Rectangle.Empty;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        public static bool IsMinimized(IntPtr h) { return IsIconic(h); }
        // keep a window out of every screenshot (Windows 10 2004 and later)
        public static void ExcludeFromCapture(IntPtr h) { try { SetWindowDisplayAffinity(h, 0x11); } catch { } }

        // Visible child windows (panels, panes, controls) of a top-level window, screen coordinates.
        // Same approach as Snagit's window finder: walk the children, keep the visible ones with a real size.
        public static List<Rectangle> ChildRects(IntPtr parent, Rectangle clip, int minW, int minH)
        {
            var list = new List<Rectangle>();
            try
            {
                EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
                {
                    if (!IsWindowVisible(h)) return true;
                    RECT r; if (!GetWindowRect(h, out r)) return true;
                    var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); rect.Intersect(clip);
                    if (rect.Width >= minW && rect.Height >= minH) list.Add(rect);
                    return list.Count < 4000;
                }, IntPtr.Zero);
            }
            catch { }
            return list;
        }

        // The biggest visible child window of a given class, screen coordinates (a browser's page area is a child of its window)
        public static Rectangle LargestChild(IntPtr parent, string className)
        {
            Rectangle best = Rectangle.Empty;
            try
            {
                EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
                {
                    if (!IsWindowVisible(h) || ClassOf(h) != className) return true;
                    RECT r; if (!GetWindowRect(h, out r)) return true;
                    var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    if ((long)rect.Width * rect.Height > (long)best.Width * best.Height) best = rect;
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return best;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLongX(IntPtr h, int index);
        [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
        public static string ClassOf(IntPtr h) { var sb = new StringBuilder(128); GetClassName(h, sb, 128); return sb.ToString(); }

        // Visible top-level windows in z-order (front to back), screen coordinates. Skips what you could not
        // actually click on: SnapLite's own (hidden) windows, cloaked and fully transparent ones, click-through
        // overlays; keeps the taskbar, which has no title.
        public static List<WinInfo> Windows()
        {
            var list = new List<WinInfo>(); int me = System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                if (ProcessIdOf(h) == me) return true;
                if (GetWindowTextLength(h) == 0) { string cls = ClassOf(h); if (cls != "Shell_TrayWnd" && cls != "Shell_SecondaryTrayWnd") return true; }
                int cloaked; DwmGetWindowAttribute(h, 14, out cloaked, 4);
                if (cloaked != 0) return true;
                int ex = GetWindowLongX(h, -20);
                if ((ex & 0x80000) != 0)        // layered: invisible when fully transparent, and not a target when click-through
                {
                    uint key, flags; byte alpha;
                    if (GetLayeredWindowAttributes(h, out key, out alpha, out flags) && (flags & 2) != 0 && alpha < 8) return true;
                    if ((ex & 0x20) != 0) return true;
                }
                RECT r;
                if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(h, out r)) return true;
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width <= 8 || rect.Height <= 8) return true;
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                list.Add(new WinInfo { Handle = h, Title = sb.ToString(), Rect = rect });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);

        // Screen pixels for a rectangle in virtual-screen coordinates (black where no monitor is).
        // Blits straight into a DIB section, one monitor at a time: going through a GDI+ bitmap's HDC, or
        // asking for the whole bounding box of several 4K monitors in one BitBlt, took ~1.7 s on 3 x 4K.
        // With DWM the screen DC already includes layered windows, so plain SRCCOPY is enough.
        public static Bitmap Grab(Rectangle r, bool withCursor)
        {
            int w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96, 96);
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bits, dib = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
            try
            {
                if (dib == IntPtr.Zero) throw new InvalidOperationException("Not enough memory to capture the screen.");
                IntPtr old = SelectObject(mem, dib);
                foreach (var m in Screen.AllScreens)
                {
                    var part = Rectangle.Intersect(m.Bounds, r); if (part.IsEmpty) continue;
                    BitBlt(mem, part.X - r.X, part.Y - r.Y, part.Width, part.Height, screen, part.X, part.Y, 0x00CC0020);
                }
                SelectObject(mem, old);
                // copy into the managed bitmap, making every pixel opaque (BitBlt leaves alpha undefined)
                var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                unsafe
                {
                    for (int y = 0; y < h; y++)
                    {
                        uint* src = (uint*)bits + (long)y * w, dst = (uint*)((byte*)d.Scan0 + (long)y * d.Stride);
                        for (int x = 0; x < w; x++) dst[x] = src[x] | 0xFF000000;
                    }
                }
                bmp.UnlockBits(d);
            }
            finally { if (dib != IntPtr.Zero) DeleteObject(dib); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
            if (withCursor) DrawCursor(bmp, r.Location);
            return bmp;
        }
        public static Bitmap GrabVirtualScreen(bool withCursor) { return Grab(SystemInformation.VirtualScreen, withCursor); }

        static void ForceOpaque(Bitmap b)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            unsafe
            {
                byte* p = (byte*)d.Scan0;
                for (int y = 0; y < b.Height; y++)
                {
                    byte* row = p + y * d.Stride;
                    for (int x = 0; x < b.Width; x++) row[x * 4 + 3] = 255;
                }
            }
            b.UnlockBits(d);
        }

        public static void DrawCursor(Bitmap bmp, Point origin)
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0) return;
            ICONINFO ii; int hx = 0, hy = 0;
            if (GetIconInfo(ci.hCursor, out ii)) { hx = ii.xHotspot; hy = ii.yHotspot; if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask); if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor); }
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try { DrawIconEx(hdc, ci.pt.X - origin.X - hx, ci.pt.Y - origin.Y - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3); }
                finally { g.ReleaseHdc(hdc); }
            }
        }

        public static Point CursorPos { get { var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) }; GetCursorInfo(ref ci); return new Point(ci.pt.X, ci.pt.Y); } }

        public static void MouseWheel(int notches)
        {
            var i = new INPUT { type = 0 };
            i.mi.mouseData = -notches * 120; i.mi.dwFlags = 0x0800;     // MOUSEEVENTF_WHEEL
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }
        public static void MouseWheelH(int notches)
        {
            var i = new INPUT { type = 0 };
            i.mi.mouseData = notches * 120; i.mi.dwFlags = 0x1000;      // MOUSEEVENTF_HWHEEL
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
