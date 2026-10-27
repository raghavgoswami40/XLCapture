// Library: every capture in the captures folder, with search, favourites, tags, combine and PDF export.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SnapLite
{
    class LibEntry { public string Path; public bool Fav; public List<string> Tags = new List<string>(); }

    static class LibraryStore
    {
        static readonly Dictionary<string, LibEntry> map = new Dictionary<string, LibEntry>(StringComparer.OrdinalIgnoreCase);
        static bool loaded;
        public static string DevFile;      // the test builds keep their list here, never in the person's own file
        static string FilePath { get { return DevFile ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapLite", "library.txt"); } }

        static void Load()
        {
            if (loaded) return; loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split('\t'); if (p.Length < 3) continue;
                    map[p[0]] = new LibEntry { Path = p[0], Fav = p[1] == "1", Tags = p[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList() };
                }
            }
            catch { }
        }
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, map.Values.Where(e => e.Fav || e.Tags.Count > 0).Select(e => e.Path + "\t" + (e.Fav ? "1" : "0") + "\t" + string.Join(",", e.Tags)));
            }
            catch { }
        }
        public static LibEntry Get(string path) { Load(); LibEntry e; if (!map.TryGetValue(path, out e)) { e = new LibEntry { Path = path }; map[path] = e; } return e; }
        public static void Touch(string path) { Get(path); }
        public static void Rename(string from, string to) { Load(); LibEntry e; if (map.TryGetValue(from, out e)) { map.Remove(from); e.Path = to; map[to] = e; Save(); } }
        public static void Forget(string path) { Load(); map.Remove(path); Save(); }
        public static IEnumerable<string> AllTags() { Load(); return map.Values.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t); }
    }

    class LibraryView : UserControl
    {

        static readonly string[] ImgExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
        static readonly string[] VidExt = { ".mp4", ".avi" };

        readonly ListView list;
        readonly TextBox search;
        readonly ComboBox filter, sort;
        readonly Label count;
        readonly Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> files = new List<string>();
        FileSystemWatcher watcher;
        int tileW = Dpi.S(212), tileH = Dpi.S(176);

        public event Action<string> OpenRequested;
        public LibraryView()
        {
            BackColor = Theme.Paper; ForeColor = Theme.Ink; Font = Theme.Body(9f);


            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Dpi.S(52), BackColor = Theme.Sheet, Padding = Dpi.P(12, 10, 12, 0), WrapContents = false };
            bar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1); };
            search = new TextBox { Width = Dpi.S(240), Font = Theme.Body(10f), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, Dpi.S(10), 0), BackColor = Color.White };
            search.TextChanged += delegate { RefreshList(); };
            filter = new ThemedCombo { Width = Dpi.S(160), Margin = new Padding(0, 0, Dpi.S(8), 0), Font = Theme.Body(9.5f) };
            sort = new ThemedCombo { Width = Dpi.S(140), Margin = new Padding(0, 0, Dpi.S(8), 0), Font = Theme.Body(9.5f) };
            sort.Items.AddRange(new object[] { "Newest first", "Oldest first", "Name", "Size" }); sort.SelectedIndex = 0;
            sort.SelectedIndexChanged += delegate { RefreshList(); }; filter.SelectedIndexChanged += delegate { RefreshList(); };
            var refresh = new FlatButton("Refresh", false) { Margin = new Padding(0, 0, Dpi.S(8), 0), Width = Dpi.S(78) }; refresh.Click += delegate { thumbs.Clear(); RefreshList(); };
            var open = new FlatButton("Open folder", false) { Width = Dpi.S(100) }; open.Click += delegate { Directory.CreateDirectory(Settings.Folder); System.Diagnostics.Process.Start("explorer.exe", Settings.Folder); };
            count = new Label { AutoSize = true, ForeColor = Theme.Slate, Margin = new Padding(Dpi.S(14), Dpi.S(7), 0, 0) };
            bar.Controls.AddRange(new Control[] { search, filter, sort, refresh, open, count });

            list = new ListView { Dock = DockStyle.Fill, View = View.Tile, OwnerDraw = true, MultiSelect = true, BackColor = Theme.Paper, BorderStyle = BorderStyle.None, HideSelection = false, Margin = Padding.Empty };
            list.TileSize = new Size(tileW, tileH);
            list.DrawItem += DrawTile; list.ItemActivate += delegate { OpenSelected(); };
            list.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) DeleteSelected(); else if (e.KeyCode == Keys.Enter) OpenSelected(); else if (e.Control && e.KeyCode == Keys.A) foreach (ListViewItem i in list.Items) i.Selected = true; };
            list.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowMenu(e.Location); };
            Controls.Add(list); Controls.Add(bar);

            Directory.CreateDirectory(Settings.Folder);
            try
            {
                watcher = new FileSystemWatcher(Settings.Folder) { EnableRaisingEvents = true };
                Action ping = delegate { try { BeginInvoke((Action)RefreshList); } catch { } };
                watcher.Created += delegate { ping(); }; watcher.Deleted += delegate { ping(); }; watcher.Renamed += delegate { ping(); };
            }
            catch { }
            Disposed += delegate { if (watcher != null) watcher.Dispose(); foreach (var b in thumbs.Values) b.Dispose(); };
            BuildFilter(); RefreshList();
        }

        public string DebugBounds() { return string.Join(" | ", list.Items.Cast<ListViewItem>().Select(i => i.Bounds.ToString())) + " tile=" + list.TileSize + " view=" + list.View + " client=" + list.ClientSize; }

        void BuildFilter()
        {
            string keep = filter.SelectedItem as string;
            filter.Items.Clear(); filter.Items.AddRange(new object[] { "Everything", "Favourites", "Pictures", "Videos", "Sessions", "Projects" });
            foreach (var t in LibraryStore.AllTags()) filter.Items.Add("Tag: " + t);
            int i = keep == null ? 0 : filter.Items.IndexOf(keep); filter.SelectedIndex = i < 0 ? 0 : i;
        }

        public void RefreshList()
        {
            if (list == null || IsDisposed) return;
            Directory.CreateDirectory(Settings.Folder);
            var all = Directory.GetFiles(Settings.Folder).Where(f => { var e = Path.GetExtension(f).ToLowerInvariant(); return ImgExt.Contains(e) || VidExt.Contains(e) || e == Project.Ext || SessionDoc.IsSessionExt(e); }).ToList();
            string q = search.Text.Trim().ToLowerInvariant(); string flt = filter.SelectedItem as string ?? "Everything";
            Func<string, bool> pass = delegate(string f)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant(); var en = LibraryStore.Get(f);
                if (q.Length > 0 && Path.GetFileNameWithoutExtension(f).ToLowerInvariant().IndexOf(q) < 0 && !en.Tags.Any(t => t.ToLowerInvariant().Contains(q))) return false;
                if (flt == "Favourites") return en.Fav; if (flt == "Pictures") return ImgExt.Contains(ext); if (flt == "Videos") return VidExt.Contains(ext) || ext == ".gif"; if (flt == "Sessions") return ext == SessionDoc.Ext; if (flt == "Projects") return ext == Project.Ext;
                if (flt.StartsWith("Tag: ")) return en.Tags.Contains(flt.Substring(5), StringComparer.OrdinalIgnoreCase);
                return true;
            };
            var sel = all.Where(pass);
            switch (sort.SelectedIndex)
            {
                case 1: sel = sel.OrderBy(f => File.GetLastWriteTime(f)); break; case 2: sel = sel.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase); break;
                case 3: sel = sel.OrderByDescending(f => new FileInfo(f).Length); break; default: sel = sel.OrderByDescending(f => File.GetLastWriteTime(f)); break;
            }
            files = sel.ToList();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var f in files) list.Items.Add(new ListViewItem(Path.GetFileNameWithoutExtension(f)) { Tag = f });
            list.EndUpdate();
            count.Text = files.Count + (files.Count == 1 ? " item" : " items") + (all.Count != files.Count ? " of " + all.Count : "");
        }

        void DrawTile(object sender, DrawListViewItemEventArgs e)
        {
            var g = e.Graphics; string path = (string)e.Item.Tag; var r = e.Bounds; bool sel = e.Item.Selected;
            g.SmoothingMode = SmoothingMode.AntiAlias; using (var bgb = new SolidBrush(Theme.Paper)) g.FillRectangle(bgb, r);
            var tile = new Rectangle(r.X + Dpi.S(6), r.Y + Dpi.S(6), r.Width - Dpi.S(12), r.Height - Dpi.S(12));
            using (var p = Theme.RoundRect(tile, Theme.Radius)) { using (var b = new SolidBrush(sel ? Theme.CobaltTint : Theme.Sheet)) g.FillPath(b, p); using (var pen = new Pen(sel ? Theme.Cobalt : Theme.Rule, sel ? 2f : 1f)) g.DrawPath(pen, p); }
            var area = new Rectangle(tile.X + Dpi.S(8), tile.Y + Dpi.S(8), tile.Width - Dpi.S(16), tile.Height - Dpi.S(46));
            Bitmap th; if (thumbs.TryGetValue(path, out th) && th != null)
            {
                float k = Math.Min(area.Width / (float)th.Width, area.Height / (float)th.Height); int w = (int)(th.Width * k), h = (int)(th.Height * k);
                var dr = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
                Theme.Checker(g, dr, 6); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(th, dr);
                using (var p = new Pen(Theme.Rule)) g.DrawRectangle(p, dr);
            }
            else
            {
                if (VidExt.Contains(Path.GetExtension(path).ToLowerInvariant())) { using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, area); Icons.Draw(g, Ic.Record, new RectangleF(area.X + area.Width / 2 - 20, area.Y + area.Height / 2 - 20, 40, 40), Color.White); }
                else Queue(path);
            }
            var en = LibraryStore.Get(path);
            using (var f = Theme.Body(8.5f)) using (var b = new SolidBrush(Theme.Ink))
            {
                var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString(e.Item.Text, f, b, new RectangleF(tile.X + Dpi.S(8), tile.Bottom - Dpi.S(36), tile.Width - Dpi.S(32), Dpi.S(16)), sf);
                string meta = Path.GetExtension(path).TrimStart('.').ToUpperInvariant() + "  " + File.GetLastWriteTime(path).ToString("d MMM HH:mm") + (en.Tags.Count > 0 ? "  #" + string.Join(" #", en.Tags) : "");
                using (var b2 = new SolidBrush(Theme.Slate)) g.DrawString(meta, f, b2, new RectangleF(tile.X + Dpi.S(8), tile.Bottom - Dpi.S(20), tile.Width - Dpi.S(12), Dpi.S(16)), sf);
            }
            if (en.Fav) Icons.Draw(g, Ic.Star, new RectangleF(tile.Right - Dpi.S(26), tile.Bottom - Dpi.S(38), Dpi.S(18), Dpi.S(18)), Theme.Mark);
        }

        void Queue(string path)
        {
            if (loading.Contains(path)) return; loading.Add(path);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap th = null;
                try
                {
                    Bitmap src = path.EndsWith(Project.Ext, StringComparison.OrdinalIgnoreCase) ? Project.ReadPreview(path) : SessionDoc.IsSessionFile(path) ? SessionDoc.ReadThumbnail(path, 400) : LoadSafe(path);
                    if (src != null) { float k = Math.Min(1f, 360f / Math.Max(src.Width, src.Height)); th = k < 1 ? Effects.Resize(src, Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k))) : src; if (!ReferenceEquals(th, src)) src.Dispose(); }
                }
                catch { }
                try { BeginInvoke((Action)delegate { thumbs[path] = th; loading.Remove(path); list.Invalidate(); }); } catch { }
            });
        }
        static Bitmap LoadSafe(string p)
        {
            try { using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var im = Image.FromStream(fs, false, false)) return new Bitmap(im); } catch { return null; }
        }

        List<string> Selected() { return list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList(); }
        void OpenSelected()
        {
            foreach (var p in Selected())
            {
                var ext = Path.GetExtension(p).ToLowerInvariant();
                if (VidExt.Contains(ext)) { try { System.Diagnostics.Process.Start(p); } catch { } continue; }
                if (SessionDoc.IsSessionExt(ext)) { EditorForm.ShowSession(p); continue; }
                try { EditorForm.Show(EditorForm.LoadAny(p)); if (OpenRequested != null) OpenRequested(p); } catch (Exception ex) { MessageBox.Show(this, "Could not open " + Path.GetFileName(p) + ": " + ex.Message); }
            }
        }
        void DeleteSelected()
        {
            var sel = Selected(); if (sel.Count == 0) return;
            if (!Dialogs.ConfirmDelete(this, "Delete", "Move " + (sel.Count == 1 ? Path.GetFileName(sel[0]) : sel.Count + " items") + " to the Recycle Bin?", "Move to Recycle Bin")) return;
            foreach (var p in sel)
            {
                try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); LibraryStore.Forget(p); thumbs.Remove(p); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            }
            RefreshList();
        }

        void ShowMenu(Point at)
        {
            var m = new ContextMenuStrip { Renderer = new ThemeRenderer(), BackColor = Theme.Sheet, Font = Theme.Body(9f) };
            var sel = Selected(); bool any = sel.Count > 0;
            Func<string, EventHandler, ToolStripMenuItem> it = delegate(string t, EventHandler h) { var i = new ToolStripMenuItem(t) { Enabled = any }; i.Click += h; return i; };
            m.Items.Add(it("Open", delegate { OpenSelected(); }));
            m.Items.Add(it("Favourite on / off", delegate { foreach (var p in sel) { var e = LibraryStore.Get(p); e.Fav = !e.Fav; } LibraryStore.Save(); BuildFilter(); list.Invalidate(); }));
            m.Items.Add(it("Edit tags...", delegate { EditTags(sel); }));
            m.Items.Add(it("Rename...", delegate { Rename(sel[0]); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(it("Copy picture", delegate { var b = LoadSafe(sel[0]); if (b == null && sel[0].EndsWith(Project.Ext)) b = Project.ReadPreview(sel[0]); if (b != null) Clipboard.SetImage(b); }));
            m.Items.Add(new ToolStripMenuItem("Combine into one picture...") { Enabled = sel.Count > 1 }); m.Items[m.Items.Count - 1].Click += delegate { Combine(sel); };
            m.Items.Add(new ToolStripMenuItem("Export as one PDF...") { Enabled = any }); m.Items[m.Items.Count - 1].Click += delegate { PdfExport(sel); };
            m.Items.Add(it("Show in folder", delegate { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + sel[0] + "\""); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(it("Delete", delegate { DeleteSelected(); }));
            m.Show(list, at);
        }

        void EditTags(List<string> sel)
        {
            var cur = LibraryStore.Get(sel[0]).Tags;
            var s = Dialogs.Prompt(this, "Tags (separate with commas)", string.Join(", ", cur)); if (s == null) return;
            var tags = s.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().TrimStart('#')).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var p in sel) LibraryStore.Get(p).Tags = tags.ToList();
            LibraryStore.Save(); BuildFilter(); list.Invalidate();
        }
        void Rename(string path)
        {
            var s = Dialogs.Prompt(this, "Rename", Path.GetFileNameWithoutExtension(path)); if (string.IsNullOrWhiteSpace(s)) return;
            s = string.Join("_", s.Trim().Split(Path.GetInvalidFileNameChars()));
            string np = Path.Combine(Path.GetDirectoryName(path), s + Path.GetExtension(path));
            if (File.Exists(np)) { MessageBox.Show(this, "A file with that name already exists."); return; }
            try { File.Move(path, np); LibraryStore.Rename(path, np); thumbs.Remove(path); RefreshList(); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }

        Bitmap Pic(string p)
        {
            if (p.EndsWith(Project.Ext, StringComparison.OrdinalIgnoreCase)) { try { var d = Project.Load(p); return d.Render(); } catch { return null; } }
            return LoadSafe(p);
        }
        public void Combine(List<string> sel)
        {
            var r = Dialogs.Form(this, "Combine pictures", "Combine",
                new Dialogs.Field { Label = "Layout", Kind = "choice", Choices = new[] { "Row", "Column", "Grid" }, Value = "Grid" },
                new Dialogs.Field { Label = "Columns (grid)", Kind = "int", Value = 2, Min = 1, Max = 12 },
                new Dialogs.Field { Label = "Spacing (px)", Kind = "int", Value = 24, Min = 0, Max = 200 },
                new Dialogs.Field { Label = "Match sizes", Kind = "check", Value = true },
                new Dialogs.Field { Label = "Show file names", Kind = "check", Value = false },
                new Dialogs.Field { Label = "Background", Kind = "color", Value = Color.White });
            if (r == null) return;
            var pics = sel.Select(Pic).Where(b => b != null).ToList(); if (pics.Count < 2) return;
            string layout = (string)r["Layout"]; int cols = layout == "Row" ? pics.Count : layout == "Column" ? 1 : Math.Max(1, (int)r["Columns (grid)"]);
            int gap = (int)r["Spacing (px)"]; bool same = (bool)r["Match sizes"]; bool names = (bool)r["Show file names"]; var bg = (Color)r["Background"];
            int cellW = same ? (int)pics.Average(p => p.Width) : 0;
            var scaled = pics.Select(p => same ? Effects.Resize(p, cellW, Math.Max(1, (int)(p.Height * (cellW / (double)p.Width)))) : p).ToList();
            int rows = (scaled.Count + cols - 1) / cols; int cap = names ? 26 : 0;
            var colW = new int[cols]; var rowH = new int[rows];
            for (int i = 0; i < scaled.Count; i++) { colW[i % cols] = Math.Max(colW[i % cols], scaled[i].Width); rowH[i / cols] = Math.Max(rowH[i / cols], scaled[i].Height + cap); }
            int W = colW.Sum() + gap * (cols + 1), H = rowH.Sum() + gap * (rows + 1);
            var res = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(res))
            {
                g.Clear(bg); g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                for (int i = 0; i < scaled.Count; i++)
                {
                    int cx = gap + colW.Take(i % cols).Sum() + gap * (i % cols), cy = gap + rowH.Take(i / cols).Sum() + gap * (i / cols);
                    g.DrawImage(scaled[i], cx, cy, scaled[i].Width, scaled[i].Height);
                    if (names) using (var f = Theme.Body(11f)) using (var b = new SolidBrush(Theme.Ink)) g.DrawString(Path.GetFileNameWithoutExtension(sel[i]), f, b, cx, cy + scaled[i].Height + 4);
                }
            }
            var doc = new Doc(res) { Name = "Combined " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"), Dirty = true };
            EditorForm.Show(doc);
        }
        public void PdfExport(List<string> sel)
        {
            using (var d = new SaveFileDialog { Filter = "PDF document|*.pdf", FileName = "XLCapture " + DateTime.Now.ToString("yyyy-MM-dd"), InitialDirectory = Settings.Folder })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var pics = sel.Select(Pic).Where(b => b != null).ToList();
                try { Pdf.Write(d.FileName, pics, 90); MessageBox.Show(this, "Saved " + pics.Count + " page" + (pics.Count == 1 ? "" : "s") + " to " + d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            }
        }
    }
    class LibraryForm : DpiForm
    {
        public readonly LibraryView View;
        public LibraryForm()
        {
            Text = "XLCapture - Library"; Icon = AppIcon.Get(); BackColor = Theme.Paper; StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen.WorkingArea; ClientSize = new Size(Math.Min(wa.Width - 80, Dpi.S(1180)), Math.Min(wa.Height - 100, Dpi.S(740)));
            View = new LibraryView { Dock = DockStyle.Fill }; Controls.Add(View);
        }
        public string DebugBounds() { return View.DebugBounds(); }
    }
}