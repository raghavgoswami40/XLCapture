// Export, print, email, FTP and the Share menu.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace SnapLite
{
    static class Exporter
    {
        public static ImageCodecInfo Codec(ImageFormat f) { return ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == f.Guid); }

        public static void Save(Bitmap bmp, string path, int jpegQuality)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg": case ".jpeg":
                    using (var flat = Flatten(bmp, Color.White))
                    {
                        var ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)jpegQuality);
                        flat.Save(path, Codec(ImageFormat.Jpeg), ep);
                    }
                    break;
                case ".bmp": using (var flat = Flatten(bmp, Color.White)) flat.Save(path, ImageFormat.Bmp); break;
                case ".gif": bmp.Save(path, ImageFormat.Gif); break;
                case ".tif": case ".tiff": bmp.Save(path, ImageFormat.Tiff); break;
                case ".pdf": Pdf.Write(path, new[] { bmp }, jpegQuality); break;
                default: bmp.Save(path, ImageFormat.Png); break;
            }
        }
        // JPEG/BMP have no alpha: put the picture on a solid colour first
        public static Bitmap Flatten(Bitmap src, Color back)
        {
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb); b.SetResolution(96, 96);
            using (var g = Graphics.FromImage(b)) { g.Clear(back); g.DrawImage(src, 0, 0, src.Width, src.Height); }
            return b;
        }
    }

    // Minimal PDF writer: one JPEG-compressed image per page, page size = image size at 96 dpi.
    static class Pdf
    {
        public static void Write(string path, IList<Bitmap> pages, int quality)
        {
            var objs = new List<byte[]>(); var ms = new MemoryStream();
            Action<string> w = delegate(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); };
            var offsets = new List<long>();
            w("%PDF-1.4\n%âãÏÓ\n");
            int n = pages.Count; // object ids: 1 catalog, 2 pages, then 3 objects per page (page, content, image)
            Action<int, Action> obj = delegate(int id, Action body) { while (offsets.Count < id) offsets.Add(0); offsets[id - 1] = ms.Position; w(id + " 0 obj\n"); body(); w("endobj\n"); };
            obj(1, delegate { w("<< /Type /Catalog /Pages 2 0 R >>\n"); });
            obj(2, delegate { w("<< /Type /Pages /Count " + n + " /Kids [" + string.Join(" ", Enumerable.Range(0, n).Select(i => (3 + i * 3) + " 0 R")) + "] >>\n"); });
            for (int i = 0; i < n; i++)
            {
                int pid = 3 + i * 3, cid = pid + 1, iid = pid + 2;
                var bmp = pages[i];
                double pw = bmp.Width * 0.75, ph = bmp.Height * 0.75;
                byte[] jpg;
                using (var flat = Exporter.Flatten(bmp, Color.White)) using (var m2 = new MemoryStream())
                {
                    var ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                    flat.Save(m2, Exporter.Codec(ImageFormat.Jpeg), ep); jpg = m2.ToArray();
                }
                string pwS = pw.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), phS = ph.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                obj(pid, delegate { w("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + pwS + " " + phS + "] /Contents " + cid + " 0 R /Resources << /XObject << /Im0 " + iid + " 0 R >> >> >>\n"); });
                string content = "q " + pwS + " 0 0 " + phS + " 0 0 cm /Im0 Do Q";
                obj(cid, delegate { w("<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream\n"); });
                obj(iid, delegate
                {
                    w("<< /Type /XObject /Subtype /Image /Width " + bmp.Width + " /Height " + bmp.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpg.Length + " >>\nstream\n");
                    ms.Write(jpg, 0, jpg.Length); w("\nendstream\n");
                });
            }
            long xref = ms.Position; int count = offsets.Count + 1;
            w("xref\n0 " + count + "\n0000000000 65535 f \n");
            foreach (var o in offsets) w(o.ToString("0000000000") + " 00000 n \n");
            w("trailer\n<< /Size " + count + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            File.WriteAllBytes(path, ms.ToArray());
        }
    }

    static class Com
    {
        public static object Get(object o, string name, params object[] a) { return o.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, o, a); }
        public static void Set(object o, string name, object v) { o.GetType().InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, o, new[] { v }); }
        public static object Call(object o, string name, params object[] a) { return o.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, o, a); }
    }

    // Export/share actions shared by the capture window and the editor.
    static class Sharing
    {
        public static void SaveAs(IWin32Window owner, Bitmap bmp, string name)
        {
            using (var d = new SaveFileDialog { FileName = name, InitialDirectory = Settings.Folder, Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap|*.bmp|GIF image|*.gif|TIFF image|*.tif|PDF document|*.pdf" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                try { Exporter.Save(bmp, d.FileName, 92); } catch (Exception ex) { MessageBox.Show(owner, "Could not export: " + ex.Message, "Export"); }
            }
        }
        public static void Print(IWin32Window owner, Bitmap src)
        {
            var copy = new Bitmap(src); var pd = new PrintDocument();
            pd.PrintPage += delegate(object s, PrintPageEventArgs e)
            {
                var area = e.MarginBounds;
                float k = Math.Min(area.Width / (float)copy.Width, area.Height / (float)copy.Height); k = Math.Min(k, 100f / 96f);
                int w = (int)(copy.Width * k), h = (int)(copy.Height * k);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(copy, area.Left + (area.Width - w) / 2, area.Top, w, h);
            };
            using (var dlg = new PrintPreviewDialog { Document = pd, Width = 1000, Height = 760 }) dlg.ShowDialog(owner);
            copy.Dispose();
        }
        public static void Email(string path, string subject)
        {
            if (path == null || !File.Exists(path)) return;
            try
            {
                var t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) throw new InvalidOperationException("Outlook is not installed");
                var app = Activator.CreateInstance(t); var mail = Com.Call(app, "CreateItem", 0);
                Com.Set(mail, "Subject", subject); Com.Call(Com.Get(mail, "Attachments"), "Add", path); Com.Call(mail, "Display");
            }
            catch
            {
                try { Process.Start("mailto:?subject=" + Uri.EscapeDataString(subject)); } catch { }
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
                Hub.Toast("Outlook was not found", "Drag the picture from the folder that opened into your message.");
            }
        }
        public static void Ftp(IWin32Window owner, string path, string fileName)
        {
            if (path == null || !File.Exists(path)) return;
            var r = Dialogs.Form(owner, "Upload by FTP", "Upload",
                new Dialogs.Field { Label = "Server folder URL", Kind = "text", Value = Settings.FtpUrl ?? "ftp://example.com/images/" },
                new Dialogs.Field { Label = "User name", Kind = "text", Value = Settings.FtpUser ?? "" },
                new Dialogs.Field { Label = "Password", Kind = "password", Value = "" },
                new Dialogs.Field { Label = "File name", Kind = "text", Value = fileName });
            if (r == null) return;
            Settings.FtpUrl = (string)r["Server folder URL"]; Settings.FtpUser = (string)r["User name"]; Settings.Save();
            string url = Settings.FtpUrl.TrimEnd('/') + "/" + Uri.EscapeDataString((string)r["File name"]);
            try
            {
                var req = (FtpWebRequest)WebRequest.Create(url);
                req.Method = WebRequestMethods.Ftp.UploadFile; req.Credentials = new NetworkCredential((string)r["User name"], (string)r["Password"]); req.UsePassive = true; req.EnableSsl = true;       // FTPS: the password and the picture are not sent in the clear
                var bytes = File.ReadAllBytes(path); req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = (FtpWebResponse)req.GetResponse()) Hub.Toast("Uploaded", resp.StatusDescription.Trim());
                Clipboard.SetText(url);
            }
            catch (Exception ex) { MessageBox.Show(owner, "Upload failed: " + ex.Message + "\r\n\r\nXLCapture only uploads over a secure FTPS connection. If the server offers only plain FTP, ask for FTPS or SFTP.", "FTP"); }
        }
    }

    partial class EditorForm
    {
        ToolStripMenuItem BuildShareMenu()
        {
            return DD(Ic.Share, "&Share", "Export and share",
                M("Copy picture to clipboard", "Ctrl+Shift+C", delegate { CopyImage(); }),
                M("Export picture...", "Ctrl+E", delegate { ExportImage(); }),
                M("Export as PDF...", delegate { ExportImage(".pdf"); }),
                new ToolStripSeparator(),
                M("Print...", "Ctrl+P", delegate { PrintImage(); }),
                M("Email...", delegate { Sharing.Email(TempExport(".png"), Doc.Name); }),
                M("Upload by FTP...", delegate { Sharing.Ftp(this, TempExport(".png"), Doc.Name + ".png"); }),
                new ToolStripSeparator(),
                M("Open in the default image editor", delegate { OpenExternal(); }),
                M("Show file in folder", delegate { ShowInFolder(); }),
                M("Copy file path", delegate { if (Doc.Path != null) Clipboard.SetText(Doc.Path); }));
        }

        public void CopyImage()
        {
            using (var f = Doc.Render()) Clipboard.SetImage(f);
            Info("Copied the picture to the clipboard.");
        }
        public void ExportImage() { ExportImage(null); }
        public void ExportImage(string forceExt)
        {
            using (var d = new SaveFileDialog { FileName = Doc.Name, InitialDirectory = Settings.Folder, Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap|*.bmp|GIF image|*.gif|TIFF image|*.tif|PDF document|*.pdf" })
            {
                if (forceExt == ".pdf") d.FilterIndex = 6;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { using (var f = Doc.Render()) Exporter.Save(f, d.FileName, 92); Info("Exported " + d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, "Could not export: " + ex.Message, "Export"); }
            }
        }
        string TempExport(string ext)
        {
            string p = Path.Combine(Path.GetTempPath(), (Doc.Name ?? "XLCapture") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
            using (var f = Doc.Render()) Exporter.Save(f, p, 92);
            return p;
        }
        void OpenExternal() { try { Process.Start(TempExport(".png")); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }
        void ShowInFolder()
        {
            if (Doc.Path != null && File.Exists(Doc.Path)) Process.Start("explorer.exe", "/select,\"" + Doc.Path + "\"");
            else Process.Start("explorer.exe", Settings.Folder);
        }
        void PrintImage() { using (var f = Doc.Render()) Sharing.Print(this, f); }
    }
}