// Screen recording: frames -> MJPEG AVI (or GIF), microphone -> WAV, then Windows' media composer makes the MP4.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

using Timer = System.Windows.Forms.Timer;

namespace SnapLite
{
    static class Winrt
    {
        public static T Wait<T>(IAsyncOperation<T> op) { var ev = new ManualResetEvent(false); op.Completed = delegate(IAsyncOperation<T> o, AsyncStatus s) { ev.Set(); }; ev.WaitOne(); return op.GetResults(); }
        public static void Wait(IAsyncAction op) { var ev = new ManualResetEvent(false); op.Completed = delegate(IAsyncAction o, AsyncStatus s) { ev.Set(); }; ev.WaitOne(); op.GetResults(); }
        public static void Wait<T, P>(IAsyncOperationWithProgress<T, P> op, Action<P> progress)
        {
            var ev = new ManualResetEvent(false);
            if (progress != null) op.Progress = delegate(IAsyncOperationWithProgress<T, P> o, P p) { progress(p); };
            op.Completed = delegate(IAsyncOperationWithProgress<T, P> o, AsyncStatus s) { ev.Set(); }; ev.WaitOne();
        }
    }

    // ------------------------------------------------------------------ microphone
    class Mic
    {
        [StructLayout(LayoutKind.Sequential)] struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)] struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInStop(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        IntPtr dev; readonly IntPtr[] hdrs = new IntPtr[6], bufs = new IntPtr[6]; Thread th; volatile bool run, paused; FileStream fs; long dataBytes;
        const int BufSize = 16384;
        public static bool Available { get { try { return waveInGetNumDevs() > 0; } catch { return false; } } }
        public volatile int Level;

        public bool Start(string wavPath)
        {
            var f = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = 44100, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = 88200 };
            if (waveInOpen(out dev, -1, ref f, IntPtr.Zero, IntPtr.Zero, 0) != 0) return false;
            fs = new FileStream(wavPath, FileMode.Create); fs.Write(new byte[44], 0, 44); dataBytes = 0;
            int hs = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < hdrs.Length; i++)
            {
                bufs[i] = Marshal.AllocHGlobal(BufSize); hdrs[i] = Marshal.AllocHGlobal(hs);
                var h = new WAVEHDR { lpData = bufs[i], dwBufferLength = BufSize }; Marshal.StructureToPtr(h, hdrs[i], false);
                waveInPrepareHeader(dev, hdrs[i], hs); waveInAddBuffer(dev, hdrs[i], hs);
            }
            run = true; waveInStart(dev);
            th = new Thread(Loop) { IsBackground = true }; th.Start();
            return true;
        }
        public void Pause(bool p) { paused = p; }
        void Loop()
        {
            int hs = Marshal.SizeOf(typeof(WAVEHDR)); var tmp = new byte[BufSize];
            while (run)
            {
                bool any = false;
                for (int i = 0; i < hdrs.Length; i++)
                {
                    var h = (WAVEHDR)Marshal.PtrToStructure(hdrs[i], typeof(WAVEHDR));
                    if ((h.dwFlags & 1) != 0 && h.dwBytesRecorded > 0)
                    {
                        Marshal.Copy(bufs[i], tmp, 0, (int)h.dwBytesRecorded);
                        int peak = 0; for (int k = 0; k + 1 < h.dwBytesRecorded; k += 64) peak = Math.Max(peak, Math.Abs((short)(tmp[k] | (tmp[k + 1] << 8)))); Level = peak;
                        if (!paused) { fs.Write(tmp, 0, (int)h.dwBytesRecorded); dataBytes += h.dwBytesRecorded; }
                        h.dwFlags &= ~1u; h.dwBytesRecorded = 0; Marshal.StructureToPtr(h, hdrs[i], false); waveInAddBuffer(dev, hdrs[i], hs); any = true;
                    }
                }
                if (!any) Thread.Sleep(15);
            }
        }
        public void Stop()
        {
            if (!run) return; run = false; if (th != null) th.Join(500);
            waveInStop(dev); waveInReset(dev); int hs = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < hdrs.Length; i++) { waveInUnprepareHeader(dev, hdrs[i], hs); Marshal.FreeHGlobal(hdrs[i]); Marshal.FreeHGlobal(bufs[i]); }
            waveInClose(dev);
            fs.Position = 0;
            var w = new BinaryWriter(fs);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((int)(36 + dataBytes)); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(44100); w.Write(88200); w.Write((short)2); w.Write((short)16); w.Write(Encoding.ASCII.GetBytes("data")); w.Write((int)dataBytes); w.Flush();
            fs.Dispose();
        }
        public bool HasAudio { get { return dataBytes > 4000; } }
    }

    // ------------------------------------------------------------------ MJPEG AVI
    class AviWriter
    {
        readonly string tempFrames, path; readonly FileStream fr; readonly List<int> sizes = new List<int>(); readonly int w, h, fps; readonly ImageCodecInfo jpeg; readonly EncoderParameters ep;
        public AviWriter(string path, int w, int h, int fps)
        {
            this.path = path; this.w = w; this.h = h; this.fps = fps; tempFrames = path + ".frames";
            fr = new FileStream(tempFrames, FileMode.Create); jpeg = Exporter.Codec(ImageFormat.Jpeg);
            ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
        }
        public byte[] Encode(Bitmap frame)
        {
            using (var ms = new MemoryStream()) { frame.Save(ms, jpeg, ep); return ms.ToArray(); }
        }
        public void Add(byte[] jpg) { fr.Write(jpg, 0, jpg.Length); if ((jpg.Length & 1) == 1) { fr.WriteByte(0); sizes.Add(jpg.Length + 1); } else sizes.Add(jpg.Length); realSizes.Add(jpg.Length); }
        readonly List<int> realSizes = new List<int>();
        public int Frames { get { return sizes.Count; } }
        public void Finish()
        {
            fr.Flush(); fr.Dispose();
            int n = sizes.Count; long moviBytes = 4 + sizes.Sum(s => 8L + s);
            using (var o = new FileStream(path, FileMode.Create)) using (var bw = new BinaryWriter(o))
            {
                Action<string> cc = delegate(string s) { bw.Write(Encoding.ASCII.GetBytes(s)); };
                int hdrlSize = 4 + 8 + 56 + 8 + 4 + 8 + 56 + 8 + 40; // 'hdrl' + avih + LIST strl(strh strf)
                long riff = 4 + (8 + hdrlSize) + (8 + moviBytes) + (8 + 16L * n);
                cc("RIFF"); bw.Write((int)riff); cc("AVI ");
                cc("LIST"); bw.Write(hdrlSize); cc("hdrl");
                cc("avih"); bw.Write(56); bw.Write(1000000 / fps); bw.Write(0); bw.Write(0); bw.Write(0x10); bw.Write(n); bw.Write(0); bw.Write(1); bw.Write(w * h * 3); bw.Write(w); bw.Write(h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                cc("LIST"); bw.Write(4 + 8 + 56 + 8 + 40); cc("strl");
                cc("strh"); bw.Write(56); cc("vids"); cc("MJPG"); bw.Write(0); bw.Write((short)0); bw.Write((short)0); bw.Write(0); bw.Write(1); bw.Write(fps); bw.Write(0); bw.Write(n); bw.Write(w * h * 3); bw.Write(-1); bw.Write(0); bw.Write((short)0); bw.Write((short)0); bw.Write((short)w); bw.Write((short)h);
                cc("strf"); bw.Write(40); bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)24); cc("MJPG"); bw.Write(w * h * 3); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                cc("LIST"); bw.Write((int)moviBytes); cc("movi");
                bw.Flush();
                using (var src = new FileStream(tempFrames, FileMode.Open))
                {
                    var buf = new byte[1 << 16];
                    for (int fi = 0; fi < n; fi++) { cc("00dc"); bw.Write(realSizes[fi]); int left = sizes[fi]; while (left > 0) { int r = src.Read(buf, 0, Math.Min(buf.Length, left)); if (r <= 0) break; bw.Write(buf, 0, r); left -= r; } }
                }
                cc("idx1"); bw.Write(16 * n); int off = 4; for (int fi = 0; fi < n; fi++) { cc("00dc"); bw.Write(0x10); bw.Write(off); bw.Write(realSizes[fi]); off += 8 + sizes[fi]; }
            }
            try { File.Delete(tempFrames); } catch { }
        }
        public void Abort() { try { fr.Dispose(); File.Delete(tempFrames); } catch { } }
    }

    // ------------------------------------------------------------------ GIF
    class GifWriter
    {
        readonly Stream s; readonly int w, h; readonly byte[] pal = new byte[768]; readonly byte[] lut = new byte[32768];
        public GifWriter(Stream s, int w, int h, IList<byte[]> samples) { this.s = s; this.w = w; this.h = h; BuildPalette(samples); WriteHeader(); }

        class Box { public List<int> Bins; public int Count; }
        void BuildPalette(IList<byte[]> samples)
        {
            var hist = new int[32768];
            foreach (var bgra in samples) for (int i = 0; i + 3 < bgra.Length; i += 4 * 3) hist[((bgra[i + 2] >> 3) << 10) | ((bgra[i + 1] >> 3) << 5) | (bgra[i] >> 3)]++;
            var all = new List<int>(); for (int i = 0; i < 32768; i++) if (hist[i] > 0) all.Add(i);
            if (all.Count == 0) all.Add(0);
            var boxes = new List<Box> { new Box { Bins = all, Count = all.Sum(b => hist[b]) } };
            while (boxes.Count < 256)
            {
                var big = boxes.Where(b => b.Bins.Count > 1).OrderByDescending(b => b.Count).FirstOrDefault(); if (big == null) break;
                int[] mn = { 31, 31, 31 }, mx = { 0, 0, 0 };
                foreach (var b in big.Bins) { int[] c = { b >> 10, (b >> 5) & 31, b & 31 }; for (int k = 0; k < 3; k++) { mn[k] = Math.Min(mn[k], c[k]); mx[k] = Math.Max(mx[k], c[k]); } }
                int ch = 0; for (int k = 1; k < 3; k++) if (mx[k] - mn[k] > mx[ch] - mn[ch]) ch = k;
                int shift = ch == 0 ? 10 : ch == 1 ? 5 : 0;
                var sorted = big.Bins.OrderBy(b => (b >> shift) & 31).ToList(); int half = big.Count / 2, acc = 0, cut = 0;
                for (; cut < sorted.Count - 1; cut++) { acc += hist[sorted[cut]]; if (acc >= half) break; }
                var a = sorted.Take(cut + 1).ToList(); var bb = sorted.Skip(cut + 1).ToList(); if (bb.Count == 0) break;
                boxes.Remove(big); boxes.Add(new Box { Bins = a, Count = a.Sum(x => hist[x]) }); boxes.Add(new Box { Bins = bb, Count = bb.Sum(x => hist[x]) });
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                long r = 0, g = 0, b = 0, n = 0;
                foreach (var bin in boxes[i].Bins) { int c = hist[bin]; r += (bin >> 10) * c; g += ((bin >> 5) & 31) * c; b += (bin & 31) * c; n += c; lut[bin] = (byte)i; }
                n = Math.Max(1, n); pal[i * 3] = (byte)Math.Min(255, (r * 8 / n) + 4); pal[i * 3 + 1] = (byte)Math.Min(255, (g * 8 / n) + 4); pal[i * 3 + 2] = (byte)Math.Min(255, (b * 8 / n) + 4);
            }
            // colours not seen in the samples: nearest palette entry
            int used = boxes.Count; var seen = new bool[32768]; foreach (var bx in boxes) foreach (var bin in bx.Bins) seen[bin] = true;
            for (int bin = 0; bin < 32768; bin++) if (!seen[bin])
                {
                    int r = (bin >> 10) * 8 + 4, g = ((bin >> 5) & 31) * 8 + 4, b = (bin & 31) * 8 + 4, best = 0, bd = int.MaxValue;
                    for (int i = 0; i < used; i++) { int dr = pal[i * 3] - r, dg = pal[i * 3 + 1] - g, db = pal[i * 3 + 2] - b, d = dr * dr + dg * dg + db * db; if (d < bd) { bd = d; best = i; } }
                    lut[bin] = (byte)best;
                }
        }
        void WriteHeader()
        {
            var bw = new BinaryWriter(s);
            bw.Write(Encoding.ASCII.GetBytes("GIF89a")); bw.Write((short)w); bw.Write((short)h); bw.Write((byte)0xF7); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write(pal);
            bw.Write(new byte[] { 0x21, 0xFF, 0x0B }); bw.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); bw.Write(new byte[] { 3, 1, 0, 0, 0 });
            bw.Flush();
        }
        public void AddFrame(byte[] bgra, int delayCs)
        {
            var idx = new byte[w * h];
            for (int i = 0, p = 0; p < idx.Length; p++, i += 4) idx[p] = lut[((bgra[i + 2] >> 3) << 10) | ((bgra[i + 1] >> 3) << 5) | (bgra[i] >> 3)];
            var bw = new BinaryWriter(s);
            bw.Write(new byte[] { 0x21, 0xF9, 4, 0 }); bw.Write((short)delayCs); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((byte)0x2C); bw.Write((short)0); bw.Write((short)0); bw.Write((short)w); bw.Write((short)h); bw.Write((byte)0);
            bw.Write((byte)8); Lzw(idx, bw); bw.Write((byte)0); bw.Flush();
        }
        static void Lzw(byte[] data, BinaryWriter bw)
        {
            const int clear = 256, eoi = 257; int codeSize = 9, next = 258; var dict = new Dictionary<int, int>();
            var block = new List<byte>(255); int bitBuf = 0, bitCnt = 0;
            Action<int> emit = delegate(int code)
            {
                bitBuf |= code << bitCnt; bitCnt += codeSize;
                while (bitCnt >= 8) { block.Add((byte)(bitBuf & 255)); bitBuf >>= 8; bitCnt -= 8; if (block.Count == 255) { bw.Write((byte)255); bw.Write(block.ToArray()); block.Clear(); } }
            };
            emit(clear); int prefix = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int c = data[i], key = (prefix << 8) | c, v;
                if (dict.TryGetValue(key, out v)) { prefix = v; continue; }
                emit(prefix);
                if (next < 4096) { dict[key] = next++; if (next - 1 == (1 << codeSize) && codeSize < 12) codeSize++; }
                else { emit(clear); dict.Clear(); next = 258; codeSize = 9; }
                prefix = c;
            }
            emit(prefix); emit(eoi);
            if (bitCnt > 0) block.Add((byte)(bitBuf & 255));
            if (block.Count > 0) { bw.Write((byte)block.Count); bw.Write(block.ToArray()); }
        }
        public void Finish() { s.WriteByte(0x3B); s.Flush(); }
    }

    // ------------------------------------------------------------------ MP4 via Windows media composition
    static class MediaBridge
    {
        public static bool Mp4(string video, IEnumerable<string> wavs, string mp4, int w, int h, int fps, Action<double> progress, out string error)
        {
            error = "";
            try
            {
                var vf = Winrt.Wait(StorageFile.GetFileFromPathAsync(video));
                var clip = Winrt.Wait(MediaClip.CreateFromFileAsync(vf));
                var comp = new MediaComposition(); comp.Clips.Add(clip);
                if (wavs != null) foreach (var wav in wavs) if (wav != null && File.Exists(wav)) { var af = Winrt.Wait(StorageFile.GetFileFromPathAsync(wav)); var track = Winrt.Wait(BackgroundAudioTrack.CreateFromFileAsync(af)); comp.BackgroundAudioTracks.Add(track); }
                return Render(comp, mp4, w, h, fps, progress, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public static TimeSpan Duration(string path)
        {
            var f = Winrt.Wait(StorageFile.GetFileFromPathAsync(path)); var clip = Winrt.Wait(MediaClip.CreateFromFileAsync(f)); return clip.OriginalDuration;
        }
        public static bool Trim(string src, string dst, TimeSpan from, TimeSpan to, Action<double> progress, out string error)
        {
            error = "";
            try
            {
                var f = Winrt.Wait(StorageFile.GetFileFromPathAsync(src)); var clip = Winrt.Wait(MediaClip.CreateFromFileAsync(f));
                int w = 0, h = 0; var pr = clip.GetVideoEncodingProperties(); w = (int)pr.Width; h = (int)pr.Height;
                clip.TrimTimeFromStart = from; clip.TrimTimeFromEnd = clip.OriginalDuration - to;
                var comp = new MediaComposition(); comp.Clips.Add(clip);
                return Render(comp, dst, w, h, 15, progress, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        static bool Render(MediaComposition comp, string mp4, int w, int h, int fps, Action<double> progress, out string error)
        {
            error = "";
            var folder = Winrt.Wait(StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(mp4)));
            var outFile = Winrt.Wait(folder.CreateFileAsync(Path.GetFileName(mp4), CreationCollisionOption.ReplaceExisting));
            var prof = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            if (w > 0 && h > 0) { prof.Video.Width = (uint)(w & ~1); prof.Video.Height = (uint)(h & ~1); prof.Video.Bitrate = (uint)Math.Max(1500000, Math.Min(12000000, (long)w * h * fps / 3)); }
            prof.Video.FrameRate.Numerator = (uint)fps; prof.Video.FrameRate.Denominator = 1;
            var op = comp.RenderToFileAsync(outFile, MediaTrimmingPreference.Fast, prof);
            Winrt.Wait(op, progress);
            var res = op.GetResults();
            if (res != TranscodeFailureReason.None) { error = "Windows could not encode the video (" + res + ")."; return false; }
            return true;
        }
    }

    // ------------------------------------------------------------------ recording UI
    class FrameForm : Form
    {
        public FrameForm(Rectangle area)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            int b = Dpi.S(3); Bounds = new Rectangle(area.X - b, area.Y - b, area.Width + 2 * b, area.Height + 2 * b);
            BackColor = Color.Magenta; TransparencyKey = Color.Magenta; DoubleBuffered = true;
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80020 | 0x08000000; return cp; } }   // click-through + no activate
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; int b = Dpi.S(3);
            using (var p = new Pen(Theme.Mark, b)) g.DrawRectangle(p, b / 2, b / 2, Width - b, Height - b);
        }
    }

    class RecBar : Form
    {
        readonly Label time; readonly FlatButton pause, stop; readonly Timer t = new Timer { Interval = 250 }; readonly CheckBox mic; readonly Panel meter;
        public RecBar(Rectangle area, bool gif)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Ink;
            Size = Dpi.S(gif ? 330 : 440, 54);
            var scr = Screen.FromRectangle(area).WorkingArea;
            int x = Math.Max(scr.Left, Math.Min(scr.Right - Width, area.X + (area.Width - Width) / 2)); int y = area.Bottom + Dpi.S(14); if (y + Height > scr.Bottom) y = Math.Max(scr.Top, area.Top - Height - Dpi.S(14));
            Location = new Point(x, y);
            var dot = new Panel { Location = Dpi.S(14, 19).ToPoint(), Size = Dpi.S(16, 16), BackColor = Theme.Ink }; dot.Paint += delegate(object s, PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(Recorder.Paused ? Theme.Mist : Theme.Mark)) e.Graphics.FillEllipse(b, 0, 0, dot.Width - 1, dot.Height - 1); };
            time = new Label { Location = new Point(Dpi.S(38), Dpi.S(14)), Size = Dpi.S(92, 26), ForeColor = Color.White, Font = new Font(Theme.LabelFamily, 13f, FontStyle.Bold), Text = "00:00" };
            pause = new FlatButton("Pause", false) { Location = new Point(Dpi.S(136), Dpi.S(11)), Size = Dpi.S(76, 32), BackColor = Theme.InkSoft, ForeColor = Color.White }; pause.FlatAppearance.BorderColor = Theme.Slate; pause.FlatAppearance.MouseOverBackColor = Theme.Slate;
            stop = new FlatButton("Stop", true) { Location = new Point(Dpi.S(gif ? 220 : 218), Dpi.S(11)), Size = Dpi.S(88, 32), BackColor = Theme.Mark }; stop.FlatAppearance.BorderColor = Theme.Mark; stop.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xC6, 0x34, 0x1E);
            pause.Click += delegate { Recorder.TogglePause(); pause.Text = Recorder.Paused ? "Resume" : "Pause"; dot.Invalidate(); };
            stop.Click += delegate { Recorder.Stop(); };
            Controls.AddRange(new Control[] { dot, time, pause, stop });
            if (!gif)
            {
                mic = new CheckBox { Text = "Mic", Checked = Recorder.MicOn, ForeColor = Color.White, AutoSize = true, Location = new Point(Dpi.S(318), Dpi.S(17)), Enabled = Mic.Available };
                mic.CheckedChanged += delegate { Recorder.MicOn = mic.Checked; };
                meter = new Panel { Location = new Point(Dpi.S(372), Dpi.S(21)), Size = Dpi.S(54, 10), BackColor = Theme.InkSoft };
                meter.Paint += delegate(object s, PaintEventArgs e) { int w = (int)(meter.Width * Math.Min(1.0, Recorder.MicLevel / 12000.0)); using (var b = new SolidBrush(Theme.Ok)) e.Graphics.FillRectangle(b, 0, 0, w, meter.Height); };
                Controls.Add(mic); Controls.Add(meter);
            }
            t.Tick += delegate { var el = Recorder.Elapsed; time.Text = ((int)el.TotalMinutes).ToString("00") + ":" + el.Seconds.ToString("00"); if (meter != null) meter.Invalidate(); };
            t.Start();
            FormClosed += delegate { t.Dispose(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e) { using (var p = new Pen(Theme.InkSoft)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }

    static class SizeExt { public static Point ToPoint(this Size s) { return new Point(s.Width, s.Height); } }

    static class Recorder
    {
        public static bool Active, Paused, MicOn = true; public static int MicLevel { get { return mic != null ? mic.Level : 0; } }
        static Thread worker; static volatile bool stopFlag; static Rectangle area; static bool gif; static Mic mic; static SystemAudio sys; static Camera cam; static string tmpSys; public static string Warning = "";
        static FrameForm frame; static RecBar bar; static Stopwatch clock = new Stopwatch(); static TimeSpan pausedTotal; static DateTime pauseStart;
        static string name, dir, tmpAvi, tmpWav; static AviWriter avi; static List<byte[]> gifFrames; static int gw, gh;
        public static TimeSpan Elapsed { get { return clock.Elapsed - pausedTotal - (Paused ? DateTime.Now - pauseStart : TimeSpan.Zero); } }

        public static void Start(Rectangle screenRect, bool asGif)
        {
            if (Active) return;
            area = new Rectangle(screenRect.X, screenRect.Y, screenRect.Width & ~1, screenRect.Height & ~1); gif = asGif;
            Directory.CreateDirectory(Settings.Folder);
            name = (gif ? "Recording " : "Recording ") + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"); dir = Settings.Folder;
            tmpAvi = Path.Combine(Path.GetTempPath(), "snaplite-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".avi"); tmpWav = Path.ChangeExtension(tmpAvi, ".wav");
            int fps = gif ? 8 : 15; Paused = false; stopFlag = false; pausedTotal = TimeSpan.Zero;
            if (gif)
            {
                float k = Math.Min(1f, 720f / area.Width); gw = Math.Max(2, (int)(area.Width * k) & ~1); gh = Math.Max(2, (int)(area.Height * k) & ~1); gifFrames = new List<byte[]>();
            }
            else
            {
                avi = new AviWriter(tmpAvi, area.Width, area.Height, fps);
                MicOn = Settings.MicOn; mic = null; if (MicOn && Mic.Available) { var m = new Mic(); if (m.Start(tmpWav)) mic = m; }
                tmpSys = Path.ChangeExtension(tmpAvi, ".sys.wav"); sys = null; Warning = "";
                if (Settings.VidSysAudio) { var s = new SystemAudio(); if (s.Start(tmpSys)) sys = s; else Warning = "System audio could not start. "; }
                cam = null; if (Settings.VidCamera) { var c = new Camera(); if (c.Start()) cam = c; else Warning += c.Error; }
            }
            CursorFx.Start();
            frame = new FrameForm(area); frame.Show(); bar = new RecBar(area, gif); bar.Show();
            Active = true; clock.Restart();
            if (Warning.Length > 0) Hub.Toast("Recording without everything", Warning.Trim());
            worker = new Thread(delegate() { Loop(fps); }) { IsBackground = true }; worker.Start();
        }

        static void Loop(int fps)
        {
            long tick = 0; byte[] last = null; int delayCs = 100 / fps;
            while (!stopFlag)
            {
                if (Paused) { Thread.Sleep(30); tick = (long)(Elapsed.TotalSeconds * fps); continue; }
                long want = (long)(Elapsed.TotalSeconds * fps);
                if (want < tick) { Thread.Sleep(4); continue; }
                using (var bmp = Native.Grab(area, Settings.VidCursor))
                {
                    if (Settings.VidCursor) CursorFx.Draw(bmp, area);
                    if (cam != null) { using (var cf = cam.Frame()) CursorFx.DrawCamera(bmp, cf); }
                    if (gif)
                    {
                        using (var sm = gw == bmp.Width ? (Bitmap)bmp.Clone() : Effects.Resize(bmp, gw, gh))
                        {
                            int stride; var d = Effects.Bytes(sm, out stride); if (stride == gw * 4) gifFrames.Add(d); else { var c = new byte[gw * gh * 4]; for (int y = 0; y < gh; y++) Buffer.BlockCopy(d, y * stride, c, y * gw * 4, gw * 4); gifFrames.Add(c); }
                        }
                        if (gifFrames.Count > 900) { stopFlag = true; }
                    }
                    else last = avi.Encode(bmp);
                }
                if (!gif)
                {
                    // late? repeat the last frame so the clip keeps real time
                    do { avi.Add(last); tick++; } while (tick < (long)(Elapsed.TotalSeconds * fps) && !stopFlag && tick - want < 4);
                }
                else tick++;
            }
        }

        public static void TogglePause()
        {
            if (!Active) return; Paused = !Paused;
            if (Paused) { pauseStart = DateTime.Now; if (mic != null) mic.Pause(true); if (sys != null) sys.Pause(true); }
            else { pausedTotal += DateTime.Now - pauseStart; if (mic != null) mic.Pause(false); if (sys != null) sys.Pause(false); }
        }

        public static void Stop()
        {
            if (!Active) return; Active = false; stopFlag = true;
            if (Paused) { pausedTotal += DateTime.Now - pauseStart; Paused = false; }
            worker.Join(2000); clock.Stop();
            if (bar != null) { bar.Close(); bar = null; } if (frame != null) { frame.Close(); frame = null; }
            CursorFx.Stop(); bool haveAudio = false, haveSys = false;
            if (mic != null) { mic.Stop(); haveAudio = mic.HasAudio; mic = null; }
            if (sys != null) { sys.Stop(); haveSys = sys.HasAudio; sys = null; }
            if (cam != null) { cam.Stop(); cam = null; }
            var scrStop = Hub.Instance; Hub.Toast("Saving your recording", "One moment...");
            ThreadPool.QueueUserWorkItem(delegate { try { Finish(haveAudio, haveSys); } catch (Exception ex) { Post(delegate { MessageBox.Show("The recording could not be saved: " + ex.Message, "XLCapture"); }); } });
        }
        static void Post(Action a) { if (Hub.Instance != null) { try { Hub.Instance.BeginInvoke(a); } catch { } } }

        static void Finish(bool haveAudio, bool haveSys)
        {
            string final;
            if (gif)
            {
                final = Path.Combine(dir, name + ".gif");
                using (var fs = new FileStream(final, FileMode.Create))
                {
                    var samples = new List<byte[]>(); for (int i = 0; i < gifFrames.Count; i += Math.Max(1, gifFrames.Count / 12)) samples.Add(gifFrames[i]);
                    var gw2 = new GifWriter(fs, gw, gh, samples);
                    foreach (var f in gifFrames) gw2.AddFrame(f, 12);
                    gw2.Finish();
                }
                gifFrames = null;
            }
            else
            {
                avi.Finish();
                final = Path.Combine(dir, name + ".mp4"); string err;
                Post(delegate { Hub.Toast("Encoding MP4", "Converting with Windows media tools..."); });
                var sounds = new List<string>(); if (haveAudio) sounds.Add(tmpWav); if (haveSys) sounds.Add(tmpSys);
                bool ok = MediaBridge.Mp4(tmpAvi, sounds, final, area.Width, area.Height, 15, null, out err);
                if (!ok)
                {
                    // fall back to keeping the AVI so nothing is lost
                    final = Path.Combine(dir, name + ".avi"); File.Copy(tmpAvi, final, true);
                    Post(delegate { Hub.Toast("Saved as AVI", "MP4 encoding failed: " + err + " The AVI plays in most players."); });
                }
                try { File.Delete(tmpAvi); File.Delete(tmpWav); File.Delete(tmpSys); } catch { }
            }
            LibraryStore.Touch(final);
            string saved = final;
            Post(delegate
            {
                Hub.Instance.RefreshRecent();
                if (Settings.VidOpenIn == "player") { try { Process.Start(saved); } catch { } }
                else if (Settings.VidOpenIn == "folder") Process.Start("explorer.exe", "/select,\"" + saved + "\"");
                else { EditorForm.ShowEditor(); new VideoDone(saved).Show(); }
            });
        }
    }

    class VideoDone : DpiForm
    {
        public VideoDone(string path)
        {
            DesignPixels = true;
            Text = "Recording saved"; Icon = AppIcon.Get(); BackColor = Theme.Sheet; ForeColor = Theme.Ink; Font = Theme.Body(9f); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(480, 190); TopMost = true;
            long kb = new FileInfo(path).Length / 1024;
            Controls.Add(new Label { Text = Path.GetFileName(path), Location = new Point(18, 16), Size = new Size(444, 24), Font = Theme.BodyBold(11f) });
            Controls.Add(new Label { Text = (kb > 2048 ? (kb / 1024) + " MB" : kb + " KB") + " in " + Path.GetDirectoryName(path), Location = new Point(18, 44), Size = new Size(444, 36), ForeColor = Theme.Slate });
            var play = new FlatButton("Play", true) { Location = new Point(18, 108), Size = new Size(84, 36) }; play.Click += delegate { try { Process.Start(path); } catch { } };
            var trim = new FlatButton("Trim...", false) { Location = new Point(110, 108), Size = new Size(84, 36), Enabled = !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) }; trim.Click += delegate { new TrimForm(path).ShowDialog(this); };
            var folder = new FlatButton("Show in folder", false) { Location = new Point(202, 108), Size = new Size(118, 36) }; folder.Click += delegate { Process.Start("explorer.exe", "/select,\"" + path + "\""); };
            var done = new FlatButton("Done", false) { Location = new Point(378, 108), Size = new Size(84, 36), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { play, trim, folder, done }); CancelButton = done;
        }
    }

    class TrimForm : DpiForm
    {
        readonly string path; TimeSpan dur; readonly NumericUpDown from, to; readonly Label info;
        public TrimForm(string p)
        {
            DesignPixels = true;
            path = p; Text = "Trim video"; Icon = AppIcon.Get(); BackColor = Theme.Sheet; ForeColor = Theme.Ink; Font = Theme.Body(9f); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(420, 230);
            try { dur = MediaBridge.Duration(p); } catch (Exception ex) { dur = TimeSpan.Zero; MessageBox.Show("Could not read this video: " + ex.Message); }
            info = new Label { Text = "Length " + dur.TotalSeconds.ToString("0.0") + " seconds. Choose the part to keep.", Location = new Point(18, 16), Size = new Size(384, 22), ForeColor = Theme.Slate };
            Controls.Add(info);
            Controls.Add(new Label { Text = "Start (seconds)", Location = new Point(18, 56), AutoSize = true, ForeColor = Theme.Slate }); Controls.Add(new Label { Text = "End (seconds)", Location = new Point(18, 96), AutoSize = true, ForeColor = Theme.Slate });
            from = new NumericUpDown { DecimalPlaces = 1, Minimum = 0, Maximum = Math.Max(1, (decimal)dur.TotalSeconds), Increment = 0.5m, Location = new Point(150, 52), Width = 100 };
            to = new NumericUpDown { DecimalPlaces = 1, Minimum = 0, Maximum = Math.Max(1, (decimal)dur.TotalSeconds), Value = Math.Max(1, (decimal)dur.TotalSeconds), Increment = 0.5m, Location = new Point(150, 92), Width = 100 };
            Controls.Add(from); Controls.Add(to);
            var go = new FlatButton("Save trimmed copy", true) { Location = new Point(138, 168), Size = new Size(182, 36) };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(328, 168), Size = new Size(76, 36), DialogResult = DialogResult.Cancel };
            go.Click += delegate
            {
                if (to.Value - from.Value < 0.3m) { MessageBox.Show(this, "The end must be later than the start."); return; }
                string dst = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + " (trimmed).mp4"); string err;
                Cursor = Cursors.WaitCursor; go.Enabled = false; info.Text = "Encoding...";
                var f = (double)from.Value; var t = (double)to.Value; bool ok = false;
                var th = new Thread(delegate() { ok = MediaBridge.Trim(path, dst, TimeSpan.FromSeconds(f), TimeSpan.FromSeconds(t), null, out err); }); th.Start();
                while (th.IsAlive) { Application.DoEvents(); Thread.Sleep(40); }
                Cursor = Cursors.Default;
                if (ok) { LibraryStore.Touch(dst); if (Hub.Instance != null) Hub.Instance.RefreshRecent(); MessageBox.Show(this, "Saved " + dst); Close(); } else { go.Enabled = true; info.Text = "Could not trim this video."; }
            };
            Controls.AddRange(new Control[] { go, cancel }); CancelButton = cancel;
        }
    }
}
