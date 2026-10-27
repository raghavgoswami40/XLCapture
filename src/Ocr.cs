// Text recognition through the Windows OCR engine (Windows.Media.Ocr), called from classic .NET.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace SnapLite
{
    class OcrWord { public string Text; public Rectangle Box; }
    class OcrResult { public string Text; public List<OcrWord> Words = new List<OcrWord>(); public string Language; }

    static class Ocr
    {
        static T Wait<T>(IAsyncOperation<T> op)
        {
            var ev = new ManualResetEvent(false);
            op.Completed = delegate(IAsyncOperation<T> o, AsyncStatus s) { ev.Set(); };
            ev.WaitOne();
            return op.GetResults();
        }
        static void Wait(IAsyncOperationWithProgress<uint, uint> op)
        {
            var ev = new ManualResetEvent(false);
            op.Completed = delegate(IAsyncOperationWithProgress<uint, uint> o, AsyncStatus s) { ev.Set(); };
            ev.WaitOne();
        }

        public static bool Available
        {
            get { try { return OcrEngine.TryCreateFromUserProfileLanguages() != null; } catch { return false; } }
        }

        public static OcrResult Recognize(Bitmap bmp)
        {
            // very small images OCR poorly: enlarge, then map the boxes back
            float up = 1f; Bitmap work = bmp;
            if (bmp.Width < 600 && bmp.Height < 600) { up = 2f; work = Effects.Resize(bmp, bmp.Width * 2, bmp.Height * 2); }
            int max = (int)OcrEngine.MaxImageDimension;
            if (work.Width > max || work.Height > max) throw new InvalidOperationException("The picture is too large for text recognition (limit " + max + " px). Crop it first.");
            byte[] png; using (var ms = new MemoryStream()) { work.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
            if (!ReferenceEquals(work, bmp)) work.Dispose();

            var stream = new InMemoryRandomAccessStream();
            var dw = new DataWriter(stream.GetOutputStreamAt(0)); dw.WriteBytes(png); Wait(dw.StoreAsync());
            var decoder = Wait(BitmapDecoder.CreateAsync(stream));
            var sb = Wait(decoder.GetSoftwareBitmapAsync());
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) throw new InvalidOperationException("Windows has no text-recognition language installed. Add one in Settings > Time & language > Language & region.");
            var res = Wait(engine.RecognizeAsync(sb));
            var r = new OcrResult { Text = res.Text, Language = engine.RecognizerLanguage.DisplayName };
            foreach (var line in res.Lines)
                foreach (var w in line.Words)
                {
                    var b = w.BoundingRect;
                    r.Words.Add(new OcrWord { Text = w.Text, Box = new Rectangle((int)(b.X / up), (int)(b.Y / up), (int)(b.Width / up), (int)(b.Height / up)) });
                }
            return r;
        }
    }

    partial class EditorForm
    {
        void GrabText()
        {
            Bitmap src = null;
            try
            {
                Cursor = Cursors.WaitCursor; status.Text = "Reading text...";
                Application.DoEvents();
                using (var flat = Doc.Render())
                {
                    src = canvas.HasSelection ? Effects.Crop(flat, canvas.SelectionBounds) : (Bitmap)flat.Clone();
                    var r = Ocr.Recognize(src);
                    if (string.IsNullOrWhiteSpace(r.Text)) { status.Text = "No text found."; MessageBox.Show(this, "No text was found in " + (canvas.HasSelection ? "the selection" : "the picture") + ".", "Grab text"); return; }
                    Clipboard.SetText(r.Text);
                    status.Text = "Copied " + r.Words.Count + " words to the clipboard (" + r.Language + ").";
                    ShowText(r.Text);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Grab text"); status.Text = "Text recognition failed."; }
            finally { Cursor = Cursors.Default; if (src != null) src.Dispose(); }
        }

        void ShowText(string text)
        {
            using (var f = Dialogs.Base("Text found in the picture", 560, 420))
            {
                var tb = new TextBox { Multiline = true, ReadOnly = false, ScrollBars = ScrollBars.Vertical, Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), Location = new Point(Dpi.S(14), Dpi.S(14)), Size = Dpi.S(532, 338), Font = Theme.Body(10.5f), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
                var copy = new FlatButton("Copy again", true) { Location = new Point(Dpi.S(346), Dpi.S(366)), Size = Dpi.S(110, 38) };
                var close = new FlatButton("Close", false) { DialogResult = DialogResult.Cancel, Location = new Point(Dpi.S(464), Dpi.S(366)), Size = Dpi.S(84, 38) };
                copy.Click += delegate { Clipboard.SetText(tb.SelectionLength > 0 ? tb.SelectedText : tb.Text); };
                f.Controls.AddRange(new Control[] { tb, copy, close }); f.CancelButton = close;
                f.Controls.Add(new Label { Text = "Already copied. Select part of the text to copy only that.", AutoSize = false, Size = Dpi.S(320, 24), Location = new Point(Dpi.S(14), Dpi.S(374)), ForeColor = Theme.Slate, Font = Theme.Body(9f) });
                f.Shown += delegate { tb.Select(0, 0); };
                f.ShowDialog(this);
            }
        }
    }
}
