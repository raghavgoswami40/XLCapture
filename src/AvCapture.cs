// What a video recording can add to the screen: the sound the computer is playing (WASAPI loopback, written to a WAV)
// and the webcam (Windows' media frame reader; the newest picture is drawn into a corner of every recorded frame).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SnapLite
{
    // ------------------------------------------------------------------ system audio (what the speakers play)
    class SystemAudio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }
        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }

        IAudioClient client; IAudioCaptureClient capture; Thread th; volatile bool run, paused; FileStream fs; long dataBytes;
        int channels, rate; bool isFloat; int bits;
        public volatile int Level;
        public bool HasAudio { get { return dataBytes > 4000; } }

        public static bool Available
        {
            get
            {
                try { var e = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom(); IMMDevice d; return e.GetDefaultAudioEndpoint(0, 0, out d) == 0 && d != null; }
                catch { return false; }
            }
        }

        public bool Start(string wavPath)
        {
            try
            {
                var e = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom(); IMMDevice dev; if (e.GetDefaultAudioEndpoint(0, 0, out dev) != 0) return false;
                var iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"); object o; if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return false;
                client = (IAudioClient)o;
                IntPtr fmt; if (client.GetMixFormat(out fmt) != 0) return false;
                int tag = Marshal.ReadInt16(fmt, 0) & 0xFFFF; channels = Marshal.ReadInt16(fmt, 2); rate = Marshal.ReadInt32(fmt, 4); bits = Marshal.ReadInt16(fmt, 14);
                isFloat = tag == 3; if (tag == 0xFFFE) isFloat = Marshal.ReadInt16(fmt, 24) == 3;
                // 0 = shared mode, 0x00020000 = loopback: capture what the default speakers play
                if (client.Initialize(0, 0x00020000, 10000000, 0, fmt, IntPtr.Zero) != 0) return false;
                var cid = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"); object c; if (client.GetService(ref cid, out c) != 0) return false;
                capture = (IAudioCaptureClient)c;
                fs = new FileStream(wavPath, FileMode.Create); fs.Write(new byte[44], 0, 44); dataBytes = 0;
                run = true; if (client.Start() != 0) { run = false; fs.Dispose(); return false; }
                th = new Thread(Loop) { IsBackground = true }; th.Start();
                return true;
            }
            catch { return false; }
        }
        public void Pause(bool p) { paused = p; }

        // The loopback only delivers packets while something is playing; silence is written in between so the sound stays in step with the picture.
        void Loop()
        {
            int outCh = Math.Min(2, Math.Max(1, channels)), frameBytes = outCh * 2; var sw = Stopwatch.StartNew(); long written = 0; double active = 0, last = 0; bool wasPaused = false;
            while (run)
            {
                double now = sw.Elapsed.TotalSeconds; if (!paused) active += now - last; last = now;
                try
                {
                    uint size; capture.GetNextPacketSize(out size);
                    while (size > 0 && run)
                    {
                        IntPtr data; uint frames, flags; ulong dp, qp; if (capture.GetBuffer(out data, out frames, out flags, out dp, out qp) != 0) break;
                        var pcm = new byte[frames * frameBytes]; bool silent = (flags & 2) != 0; int peak = 0;
                        if (!silent) for (int f = 0; f < frames; f++)
                            for (int c = 0; c < outCh; c++)
                            {
                                short s;
                                if (isFloat) { float v = BitConverter.ToSingle(ReadBytes(data, (f * channels + c) * 4, 4), 0); s = (short)Math.Max(-32768, Math.Min(32767, (int)(v * 32767f))); }
                                else if (bits == 16) s = Marshal.ReadInt16(data, (f * channels + c) * 2);
                                else s = (short)(Marshal.ReadInt32(data, (f * channels + c) * 4) >> 16);
                                pcm[f * frameBytes + c * 2] = (byte)(s & 255); pcm[f * frameBytes + c * 2 + 1] = (byte)((s >> 8) & 255); peak = Math.Max(peak, Math.Abs((int)s));
                            }
                        capture.ReleaseBuffer(frames); Level = peak;
                        if (!paused) { Pad(ref written, active, frames, frameBytes); fs.Write(pcm, 0, pcm.Length); written += frames; dataBytes += pcm.Length; }
                        capture.GetNextPacketSize(out size);
                    }
                    if (!paused) { Pad(ref written, active, 0, frameBytes); }
                }
                catch { }
                if (paused != wasPaused) wasPaused = paused;
                Thread.Sleep(10);
            }
        }
        // silence for any time that has passed with no sound, so the file is as long as the recording
        void Pad(ref long written, double activeSeconds, uint incoming, int frameBytes)
        {
            long expected = (long)(activeSeconds * rate) - incoming, gap = expected - written;
            if (gap > rate / 40) { var z = new byte[gap * frameBytes]; fs.Write(z, 0, z.Length); written += gap; dataBytes += z.Length; }
        }
        static byte[] ReadBytes(IntPtr p, int offset, int n) { var b = new byte[n]; Marshal.Copy(IntPtr.Add(p, offset), b, 0, n); return b; }

        public void Stop()
        {
            if (!run) return; run = false; if (th != null) th.Join(800);
            try { client.Stop(); } catch { }
            int outCh = Math.Min(2, Math.Max(1, channels));
            fs.Position = 0; var w = new BinaryWriter(fs);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((int)(36 + dataBytes)); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)outCh);
            w.Write(rate); w.Write(rate * outCh * 2); w.Write((short)(outCh * 2)); w.Write((short)16); w.Write(Encoding.ASCII.GetBytes("data")); w.Write((int)dataBytes); w.Flush();
            fs.Dispose();
        }
    }

    // ------------------------------------------------------------------ webcam
    class Camera
    {
        [ComImport, Guid("905a0fef-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IBufferByteAccess { [PreserveSig] int Buffer(out IntPtr value); }
        Thread poll; volatile bool polling;
        Windows.Media.Capture.MediaCapture cap; Windows.Media.Capture.Frames.MediaFrameReader reader; Bitmap latest; readonly object lk = new object();
        static bool? have;
        public static bool Available
        {
            get
            {
                if (have.HasValue) return have.Value;
                try { var d = Winrt.Wait(Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.VideoCapture)); have = d.Count > 0; }
                catch { have = false; }
                return have.Value;
            }
        }
        public string Error = "";

        public bool Start()
        {
            try
            {
                var groups = Winrt.Wait(Windows.Media.Capture.Frames.MediaFrameSourceGroup.FindAllAsync()); Windows.Media.Capture.Frames.MediaFrameSourceGroup pick = null;
                foreach (var g in groups) if (g.SourceInfos.Any(s => s.SourceKind == Windows.Media.Capture.Frames.MediaFrameSourceKind.Color)) { pick = g; break; }
                if (pick == null) { Error = "No camera was found."; return false; }
                cap = new Windows.Media.Capture.MediaCapture();
                Winrt.Wait(cap.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings
                {
                    SourceGroup = pick, SharingMode = Windows.Media.Capture.MediaCaptureSharingMode.SharedReadOnly,
                    MemoryPreference = Windows.Media.Capture.MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Video
                }));
                var src = cap.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == Windows.Media.Capture.Frames.MediaFrameSourceKind.Color);
                if (src == null) { Error = "The camera has no colour stream."; return false; }
                reader = Winrt.Wait(cap.CreateFrameReaderAsync(src, Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8));
                                var st = Winrt.Wait(reader.StartAsync());
                if (st != Windows.Media.Capture.Frames.MediaFrameReaderStartStatus.Success) { Error = "The camera could not start (" + st + "). Check that desktop apps may use the camera in Windows privacy settings."; return false; }
                polling = true; poll = new Thread(delegate() { while (polling) { OnFrame(reader); Thread.Sleep(33); } }) { IsBackground = true }; poll.Start();
                return true;
            }
            catch (Exception ex) { Error = ex.Message; return false; }
        }

        void OnFrame(Windows.Media.Capture.Frames.MediaFrameReader sender)
        {
            try
            {
                using (var fr = sender.TryAcquireLatestFrame())
                {
                    if (fr == null || fr.VideoMediaFrame == null) return;
                    var sb = fr.VideoMediaFrame.SoftwareBitmap; if (sb == null) return;
                    Windows.Graphics.Imaging.SoftwareBitmap bgra = sb.BitmapPixelFormat == Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8 ? sb : Windows.Graphics.Imaging.SoftwareBitmap.Convert(sb, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8);
                    int w = bgra.PixelWidth, h = bgra.PixelHeight;
                    var buf = new Windows.Storage.Streams.Buffer((uint)(w * h * 4)); bgra.CopyToBuffer(buf);
                    IntPtr src; if (((IBufferByteAccess)(object)buf).Buffer(out src) != 0) return;
                    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb); var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++) { Marshal.Copy(IntPtr.Add(src, y * w * 4), row, 0, w * 4); Marshal.Copy(row, 0, IntPtr.Add(bd.Scan0, y * bd.Stride), w * 4); }
                    bmp.UnlockBits(bd);
                    if (!ReferenceEquals(bgra, sb)) bgra.Dispose(); sb.Dispose();
                    lock (lk) { if (latest != null) latest.Dispose(); latest = bmp; }
                }
            }
            catch { }
        }

        // a copy of the newest picture, or null if none has arrived yet
        public Bitmap Frame() { lock (lk) return latest == null ? null : (Bitmap)latest.Clone(); }

        public void Stop()
        {
            polling = false; if (poll != null) poll.Join(500);
            try { if (reader != null) { Winrt.Wait(reader.StopAsync()); reader.Dispose(); } } catch { }
            try { if (cap != null) cap.Dispose(); } catch { }
            lock (lk) { if (latest != null) { latest.Dispose(); latest = null; } }
        }
    }

    // ------------------------------------------------------------------ what the pointer looks like in a recording
    // Highlight: a soft disc in the chosen colour under the pointer. Click animation: a ring that opens out and fades from every click.
    static class CursorFx
    {
        class Click { public Point At; public long Stamp; }
        static readonly List<Click> clicks = new List<Click>(); static Thread watcher; static volatile bool on;
        const double AnimSeconds = 0.55;

        public static void Start()
        {
            if (on) return; on = true; lock (clicks) clicks.Clear();
            watcher = new Thread(delegate()
            {
                bool wasDown = false;
                while (on)
                {
                    bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;
                    if (down && !wasDown) lock (clicks) { clicks.Add(new Click { At = Native.CursorPos, Stamp = Stopwatch.GetTimestamp() }); if (clicks.Count > 8) clicks.RemoveAt(0); }
                    wasDown = down; Thread.Sleep(6);
                }
            }) { IsBackground = true }; watcher.Start();
        }
        public static void Stop() { on = false; }

        public static void Draw(Bitmap bmp, Rectangle area)
        {
            if (!Settings.VidHighlight && !Settings.VidClick) return;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (Settings.VidHighlight)
                {
                    var p = Native.CursorPos; int x = p.X - area.X, y = p.Y - area.Y, r = Dpi.S(24);
                    if (x > -r && y > -r && x < bmp.Width + r && y < bmp.Height + r)
                        using (var b = new SolidBrush(Color.FromArgb(120, Settings.VidHighlightColor))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                }
                if (Settings.VidClick)
                {
                    long now = Stopwatch.GetTimestamp(); Click[] list; lock (clicks) list = clicks.ToArray();
                    foreach (var c in list)
                    {
                        double age = (now - c.Stamp) / (double)Stopwatch.Frequency; if (age < 0 || age > AnimSeconds) continue;
                        double k = age / AnimSeconds, ease = 1 - (1 - k) * (1 - k); float r = Dpi.F((float)(8 + 30 * ease)); int a = (int)(230 * (1 - k));
                        int x = c.At.X - area.X, y = c.At.Y - area.Y;
                        using (var b = new SolidBrush(Color.FromArgb(a / 4, Settings.VidClickColor))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                        using (var pen = new Pen(Color.FromArgb(a, Settings.VidClickColor), Dpi.F(3f))) g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
                    }
                }
            }
        }

        // the webcam picture in the bottom-right corner, with a white edge
        public static void DrawCamera(Bitmap bmp, Bitmap cam)
        {
            if (cam == null) return;
            int w = Math.Max(Dpi.S(160), bmp.Width / 5), h = (int)(w * (float)cam.Height / cam.Width), m = Dpi.S(16);
            if (h > bmp.Height / 2) { h = bmp.Height / 2; w = (int)(h * (float)cam.Width / cam.Height); }
            var r = new Rectangle(bmp.Width - w - m, bmp.Height - h - m, w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                using (var p = Theme.RoundRect(r, Dpi.S(12)))
                {
                    g.SetClip(p); g.DrawImage(cam, r); g.ResetClip();
                    using (var pen = new Pen(Color.White, Dpi.F(3f))) g.DrawPath(pen, p);
                }
            }
        }
    }
}
