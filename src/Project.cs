// .snapl project file (a zip): base.png, objects.bin, meta.txt, preview.png. Plus settings persistence.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Windows.Forms;

namespace SnapLite
{
    static class Settings
    {
        public static string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapLite");
        public static int DelaySec = 0;
        public static bool ShowCursor = true;
        public static string FtpUrl, FtpUser;
        public static Dictionary<string, string> Hotkeys = new Dictionary<string, string>
        {
            { "capture", "Ctrl+Shift+C" }, { "session", "Ctrl+Shift+R" }, { "region", "Ctrl+Shift+1" }, { "window", "Ctrl+Shift+2" }, { "full", "Ctrl+Shift+3" }, { "scroll", "Ctrl+Shift+4" },
            { "object", "Ctrl+Shift+5" }, { "freehand", "Ctrl+Shift+6" }, { "text", "Ctrl+Shift+7" }, { "video", "Ctrl+Shift+8" }, { "repeat", "Ctrl+Shift+9" }
        };
        public static readonly Dictionary<string, string> DefaultHotkeys = new Dictionary<string, string>(Hotkeys);
        public static int FixedW = 1280, FixedH = 720;
        public static string AutoAction = "editor";      // editor | clipboard | save-only

        // capture window state (Snagit-style tabs)
        public static string CapTab = "image";           // aio | image | video
        public static string ImgSel = "region", ImgEffect = "none", ImgShare = "none";
        public static bool PreviewInEditor = true, CopyToClip = false, DelayOn = false;
        public static string VidSel = "region", VidFormat = "mp4", VidOpenIn = "editor";
        public static bool VidCursor = true, MicOn = true;
        public static bool VidHighlight = false, VidClick = true, VidCamera = false, VidSysAudio = false;      // cursor highlight, click animation, webcam overlay, system sound
        public static Color VidHighlightColor = ColorTranslator.FromHtml("#E2F200"), VidClickColor = ColorTranslator.FromHtml("#9A9A9A");
        public static string QuickTheme = "Light table";
        public static string SesTarget = "all", SesOpenIn = "editor";     // all | monitor | region | window
        public static bool SesTyping = true, SesScroll = true;
        public static string SesProcFile = "", SesProcCode = "", SesProcTitle = "", SlideTemplate = "", SlideExisting = "";     // the procedure a session follows (file, activity code, its title), and the slide template for the slide pack
        public static bool AskDelete = true;
        public static List<string[]> ColorPresets = new List<string[]>();      // the user's own presets: name, box, number, text (html colours), and (newer ones) the font, its size and its weight             // ask before deleting a capture (the "don't ask again" box turns it off)

        public static string DevFile;      // the test builds keep their settings here, never in the person's own file
        static string FilePath { get { return DevFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapLite", "settings.txt"); } }
        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('='); if (i < 1) continue; string k = line.Substring(0, i), v = line.Substring(i + 1);
                    switch (k)
                    {
                        case "folder": if (Directory.Exists(Path.GetDirectoryName(v.TrimEnd('\\')) ?? v) || Directory.Exists(v)) Folder = v; break;
                        case "delay": int.TryParse(v, out DelaySec); break;
                        case "cursor": ShowCursor = v == "1"; break;
                        case "ftpurl": FtpUrl = v; break; case "ftpuser": FtpUser = v; break;
                        case "fixedw": int.TryParse(v, out FixedW); break; case "fixedh": int.TryParse(v, out FixedH); break;
                        case "auto": AutoAction = v; break;
                        case "tab": CapTab = v; break; case "imgsel": ImgSel = v; break; case "imgfx": ImgEffect = v; break; case "imgshare": ImgShare = v; break;
                        case "preview": PreviewInEditor = v == "1"; break; case "clip": CopyToClip = v == "1"; break; case "delayon": DelayOn = v == "1"; break;
                        case "vidsel": VidSel = v; break; case "vidfmt": VidFormat = v; break; case "vidopen": VidOpenIn = v; break;
                        case "vidcursor": VidCursor = v == "1"; break; case "mic": MicOn = v == "1"; break; case "qtheme": QuickTheme = v; break;
                        case "askdelete": AskDelete = v != "0"; break;
                        case "cpreset": { var cp = v.Split('|'); if ((cp.Length == 4 || cp.Length == 7) && cp[0].Length > 0 && !ColorPresets.Any(x => x[0] == cp[0])) ColorPresets.Add(cp); } break;
                        case "vidhl": VidHighlight = v == "1"; break; case "vidclick": VidClick = v == "1"; break; case "vidcam": VidCamera = v == "1"; break; case "vidsys": VidSysAudio = v == "1"; break;
                        case "vidhlcolor": try { VidHighlightColor = ColorTranslator.FromHtml(v); } catch { } break; case "vidclickcolor": try { VidClickColor = ColorTranslator.FromHtml(v); } catch { } break;
                        case "sesprocfile": SesProcFile = v; break; case "sesproccode": SesProcCode = v; break; case "sesproctitle": SesProcTitle = v; break; case "slidetemplate": SlideTemplate = v; break; case "slideexisting": SlideExisting = v; break;
                        case "sestarget": SesTarget = v; break; case "sesopen": SesOpenIn = v; break; case "sestyping": SesTyping = v == "1"; break; case "sesscroll": SesScroll = v == "1"; break;
                        default: if (k.StartsWith("hk.")) Hotkeys[k.Substring(3)] = v; break;
                    }
                }
            }
            catch { }
        }
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.AppendLine("folder=" + Folder); sb.AppendLine("delay=" + DelaySec); sb.AppendLine("cursor=" + (ShowCursor ? "1" : "0"));
                sb.AppendLine("ftpurl=" + FtpUrl); sb.AppendLine("ftpuser=" + FtpUser); sb.AppendLine("fixedw=" + FixedW); sb.AppendLine("fixedh=" + FixedH); sb.AppendLine("auto=" + AutoAction);
                sb.AppendLine("tab=" + CapTab); sb.AppendLine("imgsel=" + ImgSel); sb.AppendLine("imgfx=" + ImgEffect); sb.AppendLine("imgshare=" + ImgShare);
                sb.AppendLine("preview=" + (PreviewInEditor ? 1 : 0)); sb.AppendLine("clip=" + (CopyToClip ? 1 : 0)); sb.AppendLine("delayon=" + (DelayOn ? 1 : 0));
                sb.AppendLine("vidsel=" + VidSel); sb.AppendLine("vidfmt=" + VidFormat); sb.AppendLine("vidopen=" + VidOpenIn);
                sb.AppendLine("vidcursor=" + (VidCursor ? 1 : 0)); sb.AppendLine("mic=" + (MicOn ? 1 : 0)); sb.AppendLine("qtheme=" + QuickTheme);
                sb.AppendLine("sestarget=" + SesTarget); sb.AppendLine("sesopen=" + SesOpenIn); sb.AppendLine("sestyping=" + (SesTyping ? 1 : 0)); sb.AppendLine("sesscroll=" + (SesScroll ? 1 : 0));
                sb.AppendLine("sesprocfile=" + SesProcFile); sb.AppendLine("sesproccode=" + SesProcCode); sb.AppendLine("sesproctitle=" + SesProcTitle); sb.AppendLine("slidetemplate=" + SlideTemplate); sb.AppendLine("slideexisting=" + SlideExisting);
                sb.AppendLine("askdelete=" + (AskDelete ? 1 : 0));
                foreach (var cp in ColorPresets) sb.AppendLine("cpreset=" + string.Join("|", cp));
                sb.AppendLine("vidhl=" + (VidHighlight ? 1 : 0)); sb.AppendLine("vidclick=" + (VidClick ? 1 : 0)); sb.AppendLine("vidcam=" + (VidCamera ? 1 : 0)); sb.AppendLine("vidsys=" + (VidSysAudio ? 1 : 0));
                sb.AppendLine("vidhlcolor=" + ColorTranslator.ToHtml(VidHighlightColor)); sb.AppendLine("vidclickcolor=" + ColorTranslator.ToHtml(VidClickColor));
                foreach (var kv in Hotkeys) sb.AppendLine("hk." + kv.Key + "=" + kv.Value);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }

    // A project file holds its drawn objects in .NET's binary format, which can build any type named in the file. A crafted .snapl
    // could therefore run code when opened. This binder lets the reader create only XLCapture's own object classes and the few plain
    // .NET types they contain; anything else is refused. Files saved before the program was renamed name the assembly "SnapLite".
    sealed class ProjectBinder : System.Runtime.Serialization.SerializationBinder
    {
        static readonly string Mine = typeof(ProjectBinder).Assembly.FullName;
        static readonly System.Reflection.Assembly MineAsm = typeof(ProjectBinder).Assembly;
        public static string Remap(string name) { return System.Text.RegularExpressions.Regex.Replace(name ?? "", @"(?<=^|, )SnapLite(?=,|\]|$)(, Version=[^,\]]*)?(, Culture=[^,\]]*)?(, PublicKeyToken=[^,\]]*)?", Mine); }
        public static bool Allowed(Type t)
        {
            if (t == null) return false;
            if (t.IsArray) return Allowed(t.GetElementType());
            if (t.IsGenericType) return t.GetGenericTypeDefinition() == typeof(List<>) && t.GetGenericArguments().All(Allowed);
            if (t.Assembly == MineAsm) return t.IsEnum || t.IsValueType || typeof(Obj).IsAssignableFrom(t);
            if (t.Assembly == typeof(string).Assembly) return t.IsPrimitive || t == typeof(string) || t == typeof(decimal);
            if (t.Assembly == typeof(Color).Assembly) return t == typeof(Color) || t == typeof(PointF) || t == typeof(SizeF) || t == typeof(RectangleF) || t == typeof(Point) || t == typeof(Size) || t == typeof(Rectangle) || t.IsEnum;
            return false;
        }
        public override Type BindToType(string assemblyName, string typeName)
        {
            Type t = null;
            try { t = Type.GetType(Remap(typeName) + ", " + Remap(assemblyName), false); } catch { }
            if (!Allowed(t)) throw new System.Runtime.Serialization.SerializationException("This project file contains data that XLCapture does not accept (" + typeName + "), so it was not opened.");
            return t;
        }
    }

    static class Project
    {
        // read a stream into memory, refusing one that is larger than it should be (a "zip bomb")
        static MemoryStream ReadLimited(Stream s, long max)
        {
            var ms = new MemoryStream(); var buf = new byte[81920]; int n; long total = 0;
            while ((n = s.Read(buf, 0, buf.Length)) > 0) { total += n; if (total > max) throw new InvalidDataException("This project file is larger than XLCapture accepts."); ms.Write(buf, 0, n); }
            ms.Position = 0; return ms;
        }
        public const string Ext = ".snapl";

        public static void Save(Doc doc, string path)
        {
            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = new FileStream(tmp, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Put(zip, "base.png", delegate(Stream s) { doc.Base.Save(s, ImageFormat.Png); });
                Put(zip, "objects.bin", delegate(Stream s) { new BinaryFormatter().Serialize(s, doc.Objs.ToList()); });
                Put(zip, "meta.txt", delegate(Stream s)
                {
                    var b = Encoding.UTF8.GetBytes("name=" + doc.Name + "\nfav=" + (doc.Favorite ? 1 : 0) + "\ncreated=" + doc.Created.ToString("o") + "\ntags=" + string.Join(",", doc.Tags) + "\n");
                    s.Write(b, 0, b.Length);
                });
                Put(zip, "preview.png", delegate(Stream s)
                {
                    using (var flat = doc.Render())
                    {
                        float k = Math.Min(1f, 360f / Math.Max(flat.Width, flat.Height));
                        using (var th = k < 1 ? Effects.Resize(flat, Math.Max(1, (int)(flat.Width * k)), Math.Max(1, (int)(flat.Height * k))) : (Bitmap)flat.Clone()) th.Save(s, ImageFormat.Png);
                    }
                });
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            doc.ProjectPath = path; doc.Dirty = false;
        }
        static void Put(ZipArchive z, string name, Action<Stream> write)
        {
            var e = z.CreateEntry(name, CompressionLevel.Fastest);
            using (var s = e.Open()) write(s);
        }

        public static Doc Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                Bitmap baseImg;
                var eb = zip.GetEntry("base.png"); if (eb == null) throw new InvalidDataException("This is not an XLCapture project: it has no picture."); using (var s = eb.Open()) using (var ms = ReadLimited(s, 512L << 20)) using (var im = Image.FromStream(ms)) baseImg = new Bitmap(im);
                var doc = new Doc(baseImg) { Path = path, ProjectPath = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) };
                var eo = zip.GetEntry("objects.bin");
                if (eo != null)
                    using (var s = eo.Open()) using (var ms = ReadLimited(s, 64L << 20)) doc.Objs = ((List<Obj>)new BinaryFormatter { Binder = new ProjectBinder() }.Deserialize(ms));
                var em = zip.GetEntry("meta.txt");
                if (em != null)
                    using (var sr = new StreamReader(em.Open()))
                        foreach (var line in sr.ReadToEnd().Split('\n'))
                        {
                            int i = line.IndexOf('='); if (i < 1) continue; string k = line.Substring(0, i), v = line.Substring(i + 1).Trim();
                            if (k == "name") doc.Name = v; else if (k == "fav") doc.Favorite = v == "1";
                            else if (k == "created") { DateTime d; if (DateTime.TryParse(v, null, System.Globalization.DateTimeStyles.RoundtripKind, out d)) doc.Created = d; }
                            else if (k == "tags") doc.Tags = v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        }
                doc.Dirty = false;
                return doc;
            }
        }
        // read just the preview (fast) for the library
        public static Bitmap ReadPreview(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var e = zip.GetEntry("preview.png") ?? zip.GetEntry("base.png"); if (e == null) return null;
                    using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); ms.Position = 0; using (var im = Image.FromStream(ms)) return new Bitmap(im); }
                }
            }
            catch { return null; }
        }
    }

    partial class EditorForm
    {
        public static EditorForm Show(Doc d)
        {
            ShowEditor(); Main.OpenDoc(d); return Main;
        }

        public static Doc LoadAny(string path)
        {
            if (path.EndsWith(Project.Ext, StringComparison.OrdinalIgnoreCase)) return Project.Load(path);
            Bitmap copy; using (var im = Image.FromFile(path)) copy = new Bitmap(im);
            return new Doc(copy) { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path), Dirty = false };
        }

        void OpenFile()
        {
            using (var d = new OpenFileDialog { InitialDirectory = Settings.Folder, Filter = "Pictures, projects and shared sessions|*.snapl;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.xlsession|All files|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.FileName.EndsWith(SessionDoc.ShareExt, StringComparison.OrdinalIgnoreCase))        // a session shared with you: kept in the sessions folder, still encrypted
                {
                    try
                    {
                        SharedFile.Unprotect(d.FileName); Directory.CreateDirectory(Settings.Folder);
                        string dst = Path.Combine(Settings.Folder, Path.GetFileName(d.FileName)); int n = 1;
                        while (File.Exists(dst) && !string.Equals(Path.GetFullPath(dst), Path.GetFullPath(d.FileName), StringComparison.OrdinalIgnoreCase)) dst = Path.Combine(Settings.Folder, Path.GetFileNameWithoutExtension(d.FileName) + " (" + (++n) + ")" + SessionDoc.ShareExt);
                        if (!string.Equals(Path.GetFullPath(dst), Path.GetFullPath(d.FileName), StringComparison.OrdinalIgnoreCase)) File.Copy(d.FileName, dst);
                        ShowSession(dst); RefreshTray();
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open shared session"); }
                    return;
                }
                try { Show(LoadAny(d.FileName)); } catch (Exception ex) { MessageBox.Show(this, "Could not open that file: " + ex.Message, "Open"); }
            }
        }

        void SaveProject(bool saveAs)
        {
            string path = Doc.ProjectPath;
            if (path == null || saveAs)
            {
                Directory.CreateDirectory(Settings.Folder);
                using (var d = new SaveFileDialog { InitialDirectory = Settings.Folder, FileName = Doc.Name, Filter = "XLCapture project|*.snapl" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    path = d.FileName;
                    Doc.Name = System.IO.Path.GetFileNameWithoutExtension(path);
                }
            }
            try { Project.Save(Doc, path); Text = TitleText(); Info("Saved " + path); }
            catch (Exception ex) { MessageBox.Show(this, "Could not save: " + ex.Message, "Save"); }
        }
    }
}
