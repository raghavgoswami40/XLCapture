// Sessions: a recorded sequence of clicks, typing and scrolls, one screenshot per step.
// Saved as a .clicktrail file in the same layout ClickTrail uses (found with REA in its renderer/review/lib/session.js):
//   8 bytes "CTSESS01" | 4 bytes header length (LE) | UTF-8 JSON header | screenshots appended one after another.
// Step records carry the same field names, so ClickTrail can open SnapLite sessions and SnapLite can open ClickTrail's.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SnapLite
{
    class SessionStep
    {
        public string Id; public int Seq; public bool BreakBefore;
        public string Kind = "click";              // click | scroll
        public string ScrollDirection;             // up | down | left | right
        public bool Keyboard, Typed;
        public long Ts;
        public int X, Y;                           // the action, in pixels of the frame
        public Rectangle Box;                      // what was acted on, in pixels of the frame
        public int Vw, Vh;
        public string Url = "", Title = "", Label = "", Role = "", Container = "", TestId = "";
        // for replay: what to find again (ClassName, AncestorPath), the page address in a browser, the input slot this step fills
        // (the NAME of a {{placeholder}}, never the typed value) and what the screen should look like afterwards ("title:...")
        public string ClassName = "", AncestorPath = "", UrlBar = "", Param = "", Expect = "";
        // the procedure task and action this step carries out ("3.3 Enter the issue details", "3.3.1"): a session is grouped by task
        public string Task = "", ActionCode = "";
        public string StepText; public bool StepTextEdited;
        public byte[] Frame, AfterFrame;           // PNG bytes (a later step on the same screen keeps its own picture as a JPEG, to start a new screenshot from)
        // what the editor can change about a screen is stored on the screen's first step: which picture, a pasted picture, a crop
        public byte[] CustomFrame; public string FrameFrom = ""; public Rectangle Crop = Rectangle.Empty;
        public bool Manual, BoxEdited; public PointF? BadgePos;
        public float Round = -1;                   // how round this step's box corners are when the shape is rounded: a fraction of the shorter side (0 square .. 0.5 a capsule); -1 is the automatic roundness
        public PointF? TextPos;                    // where the step text sits on the screenshot (top-left), if it was moved; otherwise beside the number
        public bool Decor;                         // a copy made by Duplicate screenshot: drawn and exported, but a replay does not act on it
        public SessionStep Clone() { var c = (SessionStep)MemberwiseClone(); if (Extra != null) c.Extra = new Dictionary<string, object>(Extra); return c; }
        public Dictionary<string, object> Extra;   // unknown fields from another app, kept on save

        // ClickTrail's generated instructions use **bold** markdown; show them without the markers
        public string Plain { get { return (Text ?? "").Replace("**", "").Replace("__", ""); } }

        // the instruction shown for this step: the edited text if any, otherwise one made from what was recorded
        public string Text
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(StepText)) return StepText;
                if (Manual) return "Describe this step";
                if (Kind == "scroll") return "Scroll " + (ScrollDirection ?? "down");
                string what = Label.Length > 0 ? "“" + Short(Label) + "”" : RoleWord().Length > 0 ? "the " + RoleWord() : "here";
                if (Param.Length > 0) return Typed ? "Enter {{" + Param + "}} in " + what : "Choose {{" + Param + "}}";
                if (Typed && IsField()) return (Keyboard ? "Type in " : "Click ") + what + (Keyboard ? "" : " and type");
                return "Click " + what + (Label.Length > 0 && RoleWord().Length > 0 ? " " + RoleWord() : "");
            }
        }
        bool IsField() { return Role == "Edit" || Role == "Document" || Role == "ComboBox" || Role == "Spinner" || Role == "SpinBox"; }
        string RoleWord()
        {
            switch (Role)
            {
                case "Button": return "button"; case "MenuItem": return "menu item"; case "TabItem": return "tab"; case "Hyperlink": return "link";
                case "CheckBox": return "check box"; case "RadioButton": return "option"; case "ListItem": return "item"; case "TreeItem": return "item";
                case "Edit": return "box"; case "ComboBox": return "list"; case "Document": return "document";
            }
            return "";
        }
        static string Short(string s) { s = s.Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length > 60 ? s.Substring(0, 57) + "..." : s; }
    }

    class SessionDoc
    {
        public string Id, Name = "Untitled session", Path; public DateTime StartedAt = DateTime.Now;
        public List<SessionStep> Steps = new List<SessionStep>();
        public Color BoxColor = Theme.Mark, StepColor = Theme.Mark;
        // how the marks look: whether boxes show, their shape, the size of the numbers, and the text drawn beside each number
        public bool ShowBoxes = true, ShowNumbers = true, ShowText = false, TextNumber = false; public string BoxShape = "rect";            // rect | rounded | ellipse
        public int StepSize = 14;                                                                   // radius of a number, in pixels of a 1600 px wide picture
        public string TextStyle = "callout"; public Color CalloutColor = Color.White; public int CalloutOpacity = 100;        // the callout box: its fill colour and how solid it is (100 = solid)
        public string CalloutShape = "rounded";                                                    // the callout box's corners: rounded | square
        public string TextFont = "Segoe UI", TextWeight = "semibold"; public int TextSize = 16; public Color TextColor = Color.FromArgb(0x1B, 0x1B, 0x1B);   // weight: regular | semibold | bold
        public bool Dirty;

        public class Look
        {
            public bool ShowBoxes, ShowNumbers, ShowText, TextNumber; public string BoxShape, TextStyle, CalloutShape, TextFont, TextWeight; public int StepSize, TextSize, CalloutOpacity; public Color BoxColor, StepColor, TextColor, CalloutColor;
        }
        public Look GetLook() { return new Look { ShowBoxes = ShowBoxes, ShowNumbers = ShowNumbers, ShowText = ShowText, TextNumber = TextNumber, BoxShape = BoxShape, TextStyle = TextStyle, CalloutShape = CalloutShape, CalloutColor = CalloutColor, CalloutOpacity = CalloutOpacity, TextFont = TextFont, TextWeight = TextWeight, StepSize = StepSize, TextSize = TextSize, BoxColor = BoxColor, StepColor = StepColor, TextColor = TextColor }; }
        public void SetLook(Look l) { ShowBoxes = l.ShowBoxes; ShowNumbers = l.ShowNumbers; ShowText = l.ShowText; TextNumber = l.TextNumber; BoxShape = l.BoxShape; TextStyle = l.TextStyle; CalloutShape = l.CalloutShape; CalloutColor = l.CalloutColor; CalloutOpacity = l.CalloutOpacity; TextFont = l.TextFont; TextWeight = l.TextWeight; StepSize = l.StepSize; TextSize = l.TextSize; BoxColor = l.BoxColor; StepColor = l.StepColor; TextColor = l.TextColor; }

        // the font for the text on a screenshot, in pixels; "semibold" uses the family's Semibold face when it has one
        static System.Collections.Generic.HashSet<string> installedFonts;
        public static bool FontInstalled(string name)
        {
            if (installedFonts == null) { var h = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var f in new System.Drawing.Text.InstalledFontCollection().Families) h.Add(f.Name); installedFonts = h; }
            return installedFonts.Contains(name);
        }
        public static string UsableFont(string name) { return string.IsNullOrEmpty(name) || !FontInstalled(name) ? "Segoe UI" : name; }
        public Font MakeTextFont(float px)
        {
            string fam = UsableFont(TextFont); FontStyle st = FontStyle.Regular;
            if (TextWeight == "bold") st = FontStyle.Bold;
            else if (TextWeight == "semibold")
            {
                bool has = false; try { using (var f = new FontFamily(fam + " Semibold")) has = f.IsStyleAvailable(FontStyle.Regular); } catch { }
                if (has) fam = fam + " Semibold"; else st = FontStyle.Bold;
            }
            try { return new Font(fam, px, st, GraphicsUnit.Pixel); } catch { return new Font("Segoe UI", px, st, GraphicsUnit.Pixel); }
        }
        public Dictionary<string, object> ExtraStyle;
        // the procedure this session was learned from: { name, steps:[text...], params:{ slot:{ type, default } } }
        public Dictionary<string, object> Procedure;

        // the input slots a replay asks for: every step's Param, then any declared in the procedure, in order
        public List<string> ParamNames()
        {
            var l = new List<string>();
            foreach (var s in Steps) if (!string.IsNullOrEmpty(s.Param) && !l.Contains(s.Param)) l.Add(s.Param);
            var ps = Procedure != null && Procedure.ContainsKey("params") ? Procedure["params"] as Dictionary<string, object> : null;
            if (ps != null) foreach (var k in ps.Keys) if (!l.Contains(k)) l.Add(k);
            return l;
        }
        // an optional input left blank means "skip that step"
        public bool ParamOptional(string name)
        {
            var ps = Procedure != null && Procedure.ContainsKey("params") ? Procedure["params"] as Dictionary<string, object> : null;
            var p = ps != null && ps.ContainsKey(name) ? ps[name] as Dictionary<string, object> : null;
            return p != null && p.ContainsKey("optional") && p["optional"] is bool && (bool)p["optional"];
        }
        public string ParamDefault(string name)
        {
            var ps = Procedure != null && Procedure.ContainsKey("params") ? Procedure["params"] as Dictionary<string, object> : null;
            var p = ps != null && ps.ContainsKey(name) ? ps[name] as Dictionary<string, object> : null;
            return p != null && p.ContainsKey("default") && p["default"] != null ? Convert.ToString(p["default"]) : null;
        }

        public const string Ext = ".clicktrail";
        public const string ShareExt = ".xlsession";         // a shared copy: encrypted, only XLCapture can open it
        public static bool IsSessionExt(string ext) { return string.Equals(ext, Ext, StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ShareExt, StringComparison.OrdinalIgnoreCase); }
        public static bool IsSessionFile(string path) { return path != null && IsSessionExt(System.IO.Path.GetExtension(path)); }
        static Stream OpenForRead(string path)
        {
            if (path.EndsWith(ShareExt, StringComparison.OrdinalIgnoreCase)) return new MemoryStream(SharedFile.Unprotect(path));
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        // save a shared copy without the open session moving to it
        public void SaveShared(string path) { var op = Path; var od = Dirty; Path = null; try { Save(path); } finally { Path = op; Dirty = od; } }
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("CTSESS01");

        public static string NewId() { return DateTime.Now.ToString("yyyyMMddHHmmss"); }

        // screens: each starts at a step with BreakBefore; the screen's picture is its first step's frame
        public List<List<SessionStep>> Groups()
        {
            var g = new List<List<SessionStep>>();
            foreach (var s in Steps) { if (g.Count == 0 || s.BreakBefore) g.Add(new List<SessionStep>()); g[g.Count - 1].Add(s); }
            return g;
        }
        public void Renumber() { for (int i = 0; i < Steps.Count; i++) { Steps[i].Seq = i + 1; if (i == 0) Steps[i].BreakBefore = true; } }

        // ------------------------------------------------------------ file
        public string DefaultPath(string folder)
        {
            string clean = new string((Name ?? "").Where(c => !"<>:\"/\\|?*".Contains(c) && c >= 32).ToArray()).Trim().TrimEnd('.', ' ');
            if (clean.Length == 0) clean = "Untitled session"; if (clean.Length > 60) clean = clean.Substring(0, 60);
            return System.IO.Path.Combine(folder, clean + " (" + Id + ")" + Ext);
        }

        public void Save(string path)
        {
            var blobs = new List<byte[]>(); long offset = 0;
            Func<byte[], Dictionary<string, object>> add = delegate(byte[] b)
            {
                var r = new Dictionary<string, object> { { "offset", offset }, { "size", b.LongLength }, { "type", b.Length > 1 && b[0] == 0xFF && b[1] == 0xD8 ? "image/jpeg" : "image/png" } };
                blobs.Add(b); offset += b.LongLength; return r;
            };
            var recs = new List<object>();
            foreach (var s in Steps)
            {
                var r = s.Extra != null ? new Dictionary<string, object>(s.Extra) : new Dictionary<string, object>();
                Action<string, object> put = delegate(string k, object v) { r[k] = v; };
                put("id", s.Id ?? (Id + "_" + s.Seq.ToString("00000"))); put("sessionId", Id); put("seq", s.Seq); put("order", s.Seq);
                put("breakBefore", s.BreakBefore); put("late", false); put("captureError", "");
                put("kind", s.Manual ? "manual" : s.Kind); put("scrollDirection", s.ScrollDirection); put("manual", s.Manual); put("keyboard", s.Keyboard); put("typed", s.Typed);
                put("ts", s.Ts); put("x", s.X); put("y", s.Y);
                put("box", new Dictionary<string, object> { { "x", s.Box.X }, { "y", s.Box.Y }, { "w", s.Box.Width }, { "h", s.Box.Height } });
                put("vw", s.Vw); put("vh", s.Vh); put("dpr", 1); put("scrollX", 0); put("scrollY", 0);
                put("url", s.Url); put("title", s.Title); put("label", s.Label); put("text", ""); put("role", s.Role); put("tag", "");
                put("container", s.Container); put("heading", ""); put("classHint", ""); put("testId", s.TestId);
                put("boxEdited", s.BoxEdited); if (s.Decor) put("decor", true); else r.Remove("decor");
                if (s.Round >= 0) put("boxRound", Math.Round(s.Round, 3)); else r.Remove("boxRound");
                if (s.TextPos.HasValue) put("textPos", new Dictionary<string, object> { { "x", s.TextPos.Value.X }, { "y", s.TextPos.Value.Y } }); else r.Remove("textPos");
                put("badgePos", s.BadgePos.HasValue ? new Dictionary<string, object> { { "x", s.BadgePos.Value.X }, { "y", s.BadgePos.Value.Y } } : null);
                put("crop", s.Crop.IsEmpty ? null : new Dictionary<string, object> { { "x", s.Crop.X }, { "y", s.Crop.Y }, { "w", s.Crop.Width }, { "h", s.Crop.Height } });
                if (s.FrameFrom.Length > 0) put("frameFrom", s.FrameFrom); else r.Remove("frameFrom");
                Action<string, string> opt = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) r[k] = v; else r.Remove(k); };
                opt("className", s.ClassName); opt("ancestorPath", s.AncestorPath); opt("urlBar", s.UrlBar); opt("param", s.Param); opt("expect", s.Expect);
                opt("task", s.Task); opt("actionCode", s.ActionCode);
                if (!string.IsNullOrWhiteSpace(s.StepText)) { put("stepText", s.StepText); if (s.StepTextEdited) put("stepTextEdited", true); } else { r.Remove("stepText"); r.Remove("stepTextEdited"); }
                var bl = new Dictionary<string, object>();
                if (s.Frame != null) { bl["frame"] = add(s.Frame); put("frameW", s.Vw); put("frameH", s.Vh); }
                if (s.CustomFrame != null) bl["customFrame"] = add(s.CustomFrame);
                if (s.AfterFrame != null) { var a = add(s.AfterFrame); a["meta"] = new Dictionary<string, object> { { "w", s.Vw }, { "h", s.Vh }, { "source", "capture" }, { "late", false } }; bl["afterFrame"] = a; }
                put("_blobs", bl);
                recs.Add(r);
            }
            var style = ExtraStyle != null ? new Dictionary<string, object>(ExtraStyle) : new Dictionary<string, object>();
            style["boxColor"] = ColorTranslator.ToHtml(Color.FromArgb(255, BoxColor)).ToLowerInvariant(); style["stepColor"] = ColorTranslator.ToHtml(Color.FromArgb(255, StepColor)).ToLowerInvariant();
            style["showBoxes"] = ShowBoxes; style["showNumbers"] = ShowNumbers; style["showText"] = ShowText; style["textNumber"] = TextNumber; style["boxShape"] = BoxShape; style["stepSize"] = StepSize;
            style["textStyle"] = TextStyle; if (CalloutShape == "square") style["calloutShape"] = CalloutShape; style["calloutColor"] = ColorTranslator.ToHtml(Color.FromArgb(255, CalloutColor)).ToLowerInvariant(); style["calloutOpacity"] = CalloutOpacity; style["textFont"] = TextFont; style["textWeight"] = TextWeight; style["textSize"] = TextSize; style["textColor"] = ColorTranslator.ToHtml(Color.FromArgb(255, TextColor)).ToLowerInvariant();
            var header = new Dictionary<string, object> {
                { "format", 1 }, { "app", "ClickTrail" }, { "producer", "XLCapture" }, { "savedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                { "session", new Dictionary<string, object> { { "id", Id }, { "name", Name }, { "startedAt", (long)(StartedAt.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds } } },
                { "style", style }, { "records", recs } };
            if (Procedure != null) header["procedure"] = Procedure;
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(header);
            var hb = Encoding.UTF8.GetBytes(json);
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create))
            {
                fs.Write(Magic, 0, 8); fs.Write(BitConverter.GetBytes((uint)hb.Length), 0, 4); fs.Write(hb, 0, hb.Length);
                foreach (var b in blobs) fs.Write(b, 0, b.Length);
            }
            if (path.EndsWith(ShareExt, StringComparison.OrdinalIgnoreCase)) SharedFile.Protect(tmp);
            if (Path != null && File.Exists(Path) && !string.Equals(Path, path, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(Path); } catch { } }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            Path = path; Dirty = false;
        }

        static Dictionary<string, object> ReadHeader(Stream fs, out long baseOffset)
        {
            var head = new byte[12];
            if (fs.Read(head, 0, 12) < 12 || !head.Take(8).SequenceEqual(Magic)) throw new InvalidDataException("This is not a session file.");
            uint len = BitConverter.ToUInt32(head, 8);
            if (12 + (long)len > fs.Length) throw new InvalidDataException("This session file is incomplete or damaged.");
            var hb = new byte[len]; int got = 0; while (got < len) { int n = fs.Read(hb, got, (int)len - got); if (n <= 0) break; got += n; }
            var header = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(Encoding.UTF8.GetString(hb)) as Dictionary<string, object>;
            if (header == null || !header.ContainsKey("session") || !header.ContainsKey("records")) throw new InvalidDataException("This is not a session file.");
            baseOffset = 12 + len;
            return header;
        }
        static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        static int I(Dictionary<string, object> d, string k) { object v; if (d != null && d.TryGetValue(k, out v) && v != null) { try { return Convert.ToInt32(v); } catch { } } return 0; }
        static bool B(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }
        static byte[] Blob(Stream fs, long b, object refObj)
        {
            var r = refObj as Dictionary<string, object>; if (r == null) return null;
            long off = Convert.ToInt64(r["offset"]), size = Convert.ToInt64(r["size"]);
            if (off < 0 || size <= 0 || b + off + size > fs.Length) throw new InvalidDataException("This session file is incomplete or damaged (a screenshot is missing).");
            var buf = new byte[size]; fs.Position = b + off; int got = 0; while (got < size) { int n = fs.Read(buf, got, (int)size - got); if (n <= 0) break; got += n; }
            return buf;
        }

        public static SessionDoc Load(string path)
        {
            using (var fs = OpenForRead(path))
            {
                long b; var h = ReadHeader(fs, out b);
                var sess = h["session"] as Dictionary<string, object>;
                var doc = new SessionDoc { Id = S(sess, "id"), Path = path };
                doc.Name = S(sess, "name"); if (doc.Name.Length == 0) doc.Name = System.IO.Path.GetFileNameWithoutExtension(path);
                object st; if (sess.TryGetValue("startedAt", out st) && st != null) { try { doc.StartedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(Convert.ToDouble(st)).ToLocalTime(); } catch { } }
                if (h.ContainsKey("procedure")) doc.Procedure = h["procedure"] as Dictionary<string, object>;
                var style = h.ContainsKey("style") ? h["style"] as Dictionary<string, object> : null;
                if (style != null)
                {
                    doc.ExtraStyle = style;
                    try { var c = S(style, "boxColor"); if (c.Length == 7) doc.BoxColor = ColorTranslator.FromHtml(c); c = S(style, "stepColor"); if (c.Length == 7) doc.StepColor = ColorTranslator.FromHtml(c); } catch { }
                    try
                    {
                        object v;
                        if (style.TryGetValue("showBoxes", out v) && v is bool) doc.ShowBoxes = (bool)v;
                        if (style.TryGetValue("showText", out v) && v is bool) doc.ShowText = (bool)v;
                        if (style.TryGetValue("showNumbers", out v) && v is bool) doc.ShowNumbers = (bool)v;
                        if (style.TryGetValue("textNumber", out v) && v is bool) doc.TextNumber = (bool)v;
                        var shape = S(style, "boxShape"); if (shape == "rect" || shape == "rounded" || shape == "ellipse") doc.BoxShape = shape;
                        int sz = I(style, "stepSize"); if (sz >= 6 && sz <= 60) doc.StepSize = sz;
                        var cc = S(style, "calloutColor"); if (cc.Length == 7) doc.CalloutColor = ColorTranslator.FromHtml(cc);
                        object co; if (style.TryGetValue("calloutOpacity", out co) && co != null) { int ov = I(style, "calloutOpacity"); if (ov >= 0 && ov <= 100) doc.CalloutOpacity = ov; }
                        var tst = S(style, "textStyle"); if (tst == "callout" || tst == "plain") doc.TextStyle = tst;
                        doc.CalloutShape = S(style, "calloutShape") == "square" ? "square" : "rounded";
                        var tf = S(style, "textFont"); if (tf.Length > 0) doc.TextFont = tf;
                        var tw = S(style, "textWeight"); if (tw == "regular" || tw == "semibold" || tw == "bold") doc.TextWeight = tw;
                        int ts = I(style, "textSize"); if (ts >= 6 && ts <= 80) doc.TextSize = ts;
                        var tc = S(style, "textColor"); if (tc.Length == 7) doc.TextColor = ColorTranslator.FromHtml(tc);
                    }
                    catch { }
                }
                foreach (var o in (object[])h["records"])
                {
                    var r = o as Dictionary<string, object>; if (r == null) continue;
                    var bx = r.ContainsKey("box") ? r["box"] as Dictionary<string, object> : null;
                    var bl = r.ContainsKey("_blobs") ? r["_blobs"] as Dictionary<string, object> : null;
                    var s = new SessionStep
                    {
                        Id = S(r, "id"), Seq = I(r, "seq"), BreakBefore = B(r, "breakBefore"), Kind = S(r, "kind").Length > 0 ? S(r, "kind") : "click",
                        ScrollDirection = S(r, "scrollDirection").Length > 0 ? S(r, "scrollDirection") : null, Keyboard = B(r, "keyboard"), Typed = B(r, "typed"),
                        X = I(r, "x"), Y = I(r, "y"), Vw = I(r, "vw"), Vh = I(r, "vh"),
                        Box = bx == null ? Rectangle.Empty : new Rectangle(I(bx, "x"), I(bx, "y"), I(bx, "w"), I(bx, "h")),
                        Url = S(r, "url"), Title = S(r, "title"), Label = S(r, "label"), Role = S(r, "role"), Container = S(r, "container"), TestId = S(r, "testId"),
                        ClassName = S(r, "className"), AncestorPath = S(r, "ancestorPath"), UrlBar = S(r, "urlBar"), Param = S(r, "param"), Expect = S(r, "expect"), Task = S(r, "task"), ActionCode = S(r, "actionCode"),
                        StepText = S(r, "stepText"), StepTextEdited = B(r, "stepTextEdited"), Extra = r,
                        Manual = B(r, "manual"), BoxEdited = B(r, "boxEdited"), Decor = B(r, "decor"), FrameFrom = S(r, "frameFrom")
                    };
                    var cr = r.ContainsKey("crop") ? r["crop"] as Dictionary<string, object> : null;
                    if (cr != null) s.Crop = new Rectangle(I(cr, "x"), I(cr, "y"), I(cr, "w"), I(cr, "h"));
                    var bp = r.ContainsKey("badgePos") ? r["badgePos"] as Dictionary<string, object> : null;
                    if (bp != null) { try { s.BadgePos = new PointF(Convert.ToSingle(bp["x"]), Convert.ToSingle(bp["y"])); } catch { } }
                    object brv; if (r.TryGetValue("boxRound", out brv) && brv != null) { try { float rv = Convert.ToSingle(brv, System.Globalization.CultureInfo.InvariantCulture); if (rv >= 0 && rv <= 0.5f) s.Round = rv; } catch { } }
                    var tp = r.ContainsKey("textPos") ? r["textPos"] as Dictionary<string, object> : null;
                    if (tp != null) { try { s.TextPos = new PointF(Convert.ToSingle(tp["x"]), Convert.ToSingle(tp["y"])); } catch { } }
                    if (s.Kind == "manual") { s.Kind = "click"; s.Manual = true; }
                    object ts; if (r.TryGetValue("ts", out ts) && ts != null) try { s.Ts = Convert.ToInt64(ts); } catch { }
                    if (bl != null)
                    {
                        // a picture the person pasted in ClickTrail replaces the recorded one on screen, but the recorded one is kept
                        object f; if (bl.TryGetValue("frame", out f)) s.Frame = Blob(fs, b, f);
                        if (bl.TryGetValue("customFrame", out f)) s.CustomFrame = Blob(fs, b, f);
                        if (bl.TryGetValue("afterFrame", out f)) s.AfterFrame = Blob(fs, b, f);
                    }
                    s.Extra.Remove("_blobs");
                    doc.Steps.Add(s);
                }
                if (doc.Id.Length == 0) doc.Id = NewId();
                return doc;
            }
        }

        // the first screenshot, for thumbnails; reads only what it needs
        public static Bitmap ReadThumbnail(string path, int max)
        {
            try
            {
                using (var fs = OpenForRead(path))
                {
                    long b; var h = ReadHeader(fs, out b);
                    foreach (var o in (object[])h["records"])
                    {
                        var r = o as Dictionary<string, object>; var bl = r != null && r.ContainsKey("_blobs") ? r["_blobs"] as Dictionary<string, object> : null;
                        object f; if (bl == null || !(bl.TryGetValue("customFrame", out f) || bl.TryGetValue("frame", out f))) continue;
                        using (var ms = new MemoryStream(Blob(fs, b, f))) using (var im = Image.FromStream(ms))
                        {
                            float k = Math.Min(1f, max / (float)Math.Max(im.Width, im.Height));
                            using (var full = new Bitmap(im)) return Effects.Resize(full, Math.Max(1, (int)(im.Width * k)), Math.Max(1, (int)(im.Height * k)));
                        }
                    }
                }
            }
            catch { }
            return null;
        }
        // what a list of sessions needs: name, size, which apps, which inputs (with defaults); reads only the header
        public class Summary
        {
            public string Path, Name; public DateTime Saved; public int Steps, Screens, Tasks; public bool IsRun;
            public List<string> Inputs = new List<string>(), Apps = new List<string>(); public Dictionary<string, string> Defaults = new Dictionary<string, string>();
            public List<InputSpec> Specs = new List<InputSpec>();       // what to ask for before a run, with label, kind and task
            public List<string> TaskNames = new List<string>();
        }
        static string AppName(string proc)
        {
            switch ((proc ?? "").ToLowerInvariant()) { case "msedge": return "Edge"; case "chrome": return "Chrome"; case "firefox": return "Firefox"; case "brave": return "Brave"; }
            return proc;
        }
        public static Summary ReadSummary(string path)
        {
            try
            {
                using (var fs = OpenForRead(path))
                {
                    long b; var h = ReadHeader(fs, out b);
                    var sess = h["session"] as Dictionary<string, object>;
                    var s = new Summary { Path = path, Name = S(sess, "name"), Saved = File.GetLastWriteTime(path) };
                    if (s.Name.Length == 0) s.Name = System.IO.Path.GetFileNameWithoutExtension(path);
                    s.IsRun = s.Name.StartsWith("Replay - ");
                    var taskNames = new List<string>();
                    foreach (var o in (object[])h["records"])
                    {
                        var r = o as Dictionary<string, object>; if (r == null) continue;
                        s.Steps++; if (B(r, "breakBefore")) s.Screens++;
                        string tk = S(r, "task"); if (tk.Length > 0 && !taskNames.Contains(tk)) taskNames.Add(tk);
                        string p = S(r, "param"); if (p.Length > 0 && !s.Inputs.Contains(p)) s.Inputs.Add(p);
                        string u = S(r, "url");
                        if (u.StartsWith("app://")) { u = u.Substring(6); int i = u.IndexOf('/'); string app = AppName(i >= 0 ? u.Substring(0, i) : u); if (app.Length > 0 && !s.Apps.Contains(app)) s.Apps.Add(app); }
                    }
                    s.Tasks = taskNames.Count; s.TaskNames = taskNames;
                    var pr = h.ContainsKey("procedure") ? h["procedure"] as Dictionary<string, object> : null;
                    var ps = pr != null && pr.ContainsKey("params") ? pr["params"] as Dictionary<string, object> : null;
                    if (ps != null)
                        foreach (var kv in ps)
                        {
                            if (!s.Inputs.Contains(kv.Key)) s.Inputs.Add(kv.Key);
                            var d = kv.Value as Dictionary<string, object>;
                            if (d != null && d.ContainsKey("default") && d["default"] != null) s.Defaults[kv.Key] = Convert.ToString(d["default"]);
                        }
                    foreach (var name in s.Inputs) s.Specs.Add(InputSpec.From(name, ps != null && ps.ContainsKey(name) ? ps[name] as Dictionary<string, object> : null));
                    return s;
                }
            }
            catch { return null; }
        }
        public static int CountSteps(string path)
        {
            try { using (var fs = OpenForRead(path)) { long b; return ((object[])ReadHeader(fs, out b)["records"]).Length; } } catch { return -1; }
        }

        // ------------------------------------------------------------ pictures
        public static Bitmap Decode(byte[] png) { if (png == null) return null; using (var ms = new MemoryStream(png)) using (var im = Image.FromStream(ms)) return new Bitmap(im); }
        public static byte[] Encode(Bitmap b) { using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }
        // the picture a later step on a screen keeps for itself: much smaller than a PNG of a 4K screen
        public static byte[] EncodeJpeg(Bitmap b)
        {
            var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg"); if (codec == null) return Encode(b);
            using (var ms = new MemoryStream()) using (var ps = new EncoderParameters(1))
            {
                ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L); b.Save(ms, codec, ps); return ms.ToArray();
            }
        }

        // ---- which picture a screen shows: a pasted one, else the one chosen, else the first step's own
        public static byte[] PictureOf(List<SessionStep> g)
        {
            var f = g[0]; if (f.CustomFrame != null) return f.CustomFrame;
            if (f.FrameFrom.Length > 0)
                foreach (var s in g)
                {
                    if (f.FrameFrom == s.Id + ":f" && s.Frame != null) return s.Frame;
                    if (f.FrameFrom == s.Id + ":a" && s.AfterFrame != null) return s.AfterFrame;
                }
            return f.Frame;
        }
        // the pictures one can choose between for a screen: key and what to call it
        public static List<KeyValuePair<string, string>> Pictures(List<SessionStep> g)
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var s in g)
            {
                if (s.Frame != null) l.Add(new KeyValuePair<string, string>(s.Id + ":f", "Taken at step " + s.Seq));
                if (s.AfterFrame != null) l.Add(new KeyValuePair<string, string>(s.Id + ":a", "After typing in step " + s.Seq));
            }
            return l;
        }
        public static string PictureKey(List<SessionStep> g)
        {
            var f = g[0]; if (f.FrameFrom.Length > 0 && Pictures(g).Any(p => p.Key == f.FrameFrom)) return f.FrameFrom;
            return f.Frame != null ? f.Id + ":f" : "";
        }
        // the part of the picture that is shown: the crop if it is sensible, else all of it
        public static Rectangle ViewOf(List<SessionStep> g, int w, int h)
        {
            var c = g[0].Crop; var all = new Rectangle(0, 0, w, h);
            if (c.IsEmpty || c.Width < 40 || c.Height < 40 || !all.Contains(c)) return all;
            return c;
        }

        public static PointF BadgeCenter(SessionStep s, float imageWidth, float baseRadius = 14)
        {
            if (s.BadgePos.HasValue) return s.BadgePos.Value;
            float k = Math.Max(1f, imageWidth / 1600f), r = baseRadius * k;
            return new PointF(Math.Max(r + 2, s.Box.X - r * 0.4f), Math.Max(r + 2, s.Box.Y - r * 0.4f));
        }
        public static float BadgeRadius(float imageWidth, float baseRadius = 14) { return baseRadius * Math.Max(1f, imageWidth / 1600f); }

        // the corner radius of a step's rounded box, in picture pixels: what was set by dragging the dot on the box, else the automatic roundness
        public static float CornerRadius(SessionStep s)
        {
            float m = Math.Min(s.Box.Width, s.Box.Height); if (m <= 0) return 0;
            return s.Round >= 0 ? Math.Min(0.5f, s.Round) * m : Math.Max(3f, m * 0.28f);
        }
        // the box outline in the chosen shape
        public GraphicsPath BoxPath(Rectangle b, SessionStep step = null)
        {
            var r = new RectangleF(b.X, b.Y, b.Width, b.Height);
            if (BoxShape == "ellipse") { r.Inflate(r.Width * 0.06f, r.Height * 0.12f); var gp = new GraphicsPath(); gp.AddEllipse(r); return gp; }
            if (BoxShape == "rounded") return Theme.RoundRect(r, step != null ? CornerRadius(step) : Math.Max(3f, Math.Min(r.Width, r.Height) * 0.28f));
            var rc = new GraphicsPath(); rc.AddRectangle(r); return rc;
        }

        // the text of a step's label, with its number in front when that is on: "2. Click Save"
        public string LabelText(SessionStep s) { string x = s.Plain; return TextNumber ? s.Seq + ". " + x : x; }

        // the boxes, numbered badges and (if on) the step text for one screen, in the picture's own pixels.
        // view is the part of the picture that is shown (the crop). A label whose middle lies outside it is "outside text": plain text with no box,
        // which sits on the pasteboard around the screenshot (as a white callout box or as plain text, like the rest). part: 0 everything, 1 the marks and the text inside, 2 only the text outside.
        public void DrawMarks(Graphics gr, List<SessionStep> g, SessionStep highlight, float imageWidth, bool showBoxes = true, Rectangle view = default(Rectangle), int part = 0)
        {
            gr.SmoothingMode = SmoothingMode.AntiAlias; gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            float k = Math.Max(1f, imageWidth / 1600f);
            RectangleF bounds = view.IsEmpty ? gr.VisibleClipBounds : (RectangleF)view;
            foreach (var s in g)
            {
                bool hi = highlight == null || s == highlight;
                float r = StepSize * k; var center = BadgeCenter(s, imageWidth, StepSize);
                if (part != 2)
                {
                    var c = Color.FromArgb(hi ? 255 : 150, BoxColor);
                    if (s.Kind == "scroll") DrawScroll(gr, s, c, k);
                    else if (ShowBoxes && showBoxes) using (var p = new Pen(c, 3f * k)) using (var path = BoxPath(s.Box, s)) gr.DrawPath(p, path);
                    if (ShowNumbers)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(hi ? 255 : 170, StepColor))) gr.FillEllipse(b, center.X - r, center.Y - r, r * 2, r * 2);
                        using (var p = new Pen(Color.White, 2f * k)) gr.DrawEllipse(p, center.X - r, center.Y - r, r * 2, r * 2);
                        using (var f = new Font(Theme.LabelFamily, r * 1.05f, FontStyle.Bold, GraphicsUnit.Pixel)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            gr.DrawString(s.Seq.ToString(), f, Brushes.White, new RectangleF(center.X - r, center.Y - r + k, r * 2, r * 2), sf);
                    }
                }
                if (!ShowText) continue;
                var tr = TextRect(gr, s, center, r, k, bounds); if (!tr.HasValue) continue;
                bool outside = IsOutside(tr.Value, view);
                if (part == 0 || (part == 1 && !outside) || (part == 2 && outside)) DrawStepText(gr, s, tr.Value, k, hi, outside);
            }
        }
        static bool IsOutside(RectangleF label, Rectangle view)
        {
            if (view.IsEmpty) return false;
            return !view.Contains((int)(label.X + label.Width / 2), (int)(label.Y + label.Height / 2));
        }

        // where a step's text label sits and how big it is, in the picture's pixels (null when the text is off or the step has none)
        public RectangleF? TextBox(SessionStep s, float imageWidth, float imageHeight) { return TextBoxIn(s, imageWidth, new Rectangle(0, 0, (int)imageWidth, (int)imageHeight)); }
        public RectangleF? TextBoxIn(SessionStep s, float imageWidth, Rectangle view)
        {
            if (!ShowText) return null;
            using (var bm = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bm))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias; float k = Math.Max(1f, imageWidth / 1600f), r = StepSize * k;
                return TextRect(g, s, BadgeCenter(s, imageWidth, StepSize), r, k, view);
            }
        }
        // the whole picture including any text that was moved outside the screenshot (image coordinates)
        public Rectangle OutsideExtent(List<SessionStep> g, float imageWidth, Rectangle view)
        {
            float x0 = view.Left, y0 = view.Top, x1 = view.Right, y1 = view.Bottom;
            if (ShowText)
                foreach (var s in g)
                {
                    var tb = TextBoxIn(s, imageWidth, view); if (!tb.HasValue || !IsOutside(tb.Value, view)) continue;
                    x0 = Math.Min(x0, tb.Value.Left); y0 = Math.Min(y0, tb.Value.Top); x1 = Math.Max(x1, tb.Value.Right); y1 = Math.Max(y1, tb.Value.Bottom);
                }
            return Rectangle.FromLTRB((int)Math.Floor(x0), (int)Math.Floor(y0), (int)Math.Ceiling(x1), (int)Math.Ceiling(y1));
        }
        RectangleF? TextRect(Graphics gr, SessionStep s, PointF center, float r, float k, RectangleF bounds)
        {
            if (s.Manual && string.IsNullOrWhiteSpace(s.StepText)) return null;
            string text = LabelText(s); if (string.IsNullOrWhiteSpace(s.Plain)) return null;
            using (var f = MakeTextFont(Math.Max(6f, TextSize * k)))
            {
                float maxW = 360f * k, pad = 7f * k; var sz = gr.MeasureString(text, f, (int)(maxW - 2 * pad), new StringFormat());
                float w = sz.Width + 2 * pad, h = sz.Height + pad * 1.2f, x, y;
                if (s.TextPos.HasValue)         // where it was put, even outside the screenshot (within a few picture widths)
                {
                    x = Math.Max(bounds.Left - 4 * bounds.Width, Math.Min(bounds.Right + 3 * bounds.Width, s.TextPos.Value.X));
                    y = Math.Max(bounds.Top - 4 * bounds.Height, Math.Min(bounds.Bottom + 3 * bounds.Height, s.TextPos.Value.Y));
                }
                else        // beside its number, kept inside the screenshot
                {
                    x = center.X + r + 6 * k; y = center.Y - h / 2;
                    if (bounds.Width > 0 && x + w > bounds.Right - 2 * k) x = center.X - r - 6 * k - w;
                    if (x < bounds.Left + 2 * k) x = bounds.Left + 2 * k;
                    if (bounds.Width > 0 && x + w > bounds.Right - 2 * k) x = Math.Max(bounds.Left + 2 * k, bounds.Right - 2 * k - w);
                    if (bounds.Height > 0) { if (y + h > bounds.Bottom - 2 * k) y = bounds.Bottom - 2 * k - h; if (y < bounds.Top + 2 * k) y = bounds.Top + 2 * k; }
                }
                return new RectangleF(x, y, w, h);
            }
        }

        // the step's instruction in a box (white, with the box colour as its edge; rounded or square) or as plain text; outside the screenshot it keeps the same style
        void DrawStepText(Graphics gr, SessionStep s, RectangleF rect, float k, bool hi, bool outside)
        {
            float pad = 7f * k; string text = LabelText(s);
            using (var f = MakeTextFont(Math.Max(6f, TextSize * k)))
            {
                if (TextStyle != "plain")        // a callout: the text in a box, with rounded or square corners
                    using (var path = Theme.RoundRect(rect, CalloutShape == "square" ? 0f : Math.Min(rect.Height / 2f, 9f * k)))
                    {
                        using (var b = new SolidBrush(Color.FromArgb((int)Math.Round(255 * Math.Max(0, Math.Min(100, CalloutOpacity)) / 100.0), CalloutColor))) gr.FillPath(b, path);
                        using (var pen = new Pen(Color.FromArgb(hi ? 255 : 150, BoxColor), 1.5f * k)) gr.DrawPath(pen, path);
                    }
                else        // plain text, no box: only a faint shadow so it stays readable
                    using (var sb = new SolidBrush(Color.FromArgb(hi ? 90 : 50, 0, 0, 0))) gr.DrawString(text, f, sb, new RectangleF(rect.X + pad + k, rect.Y + pad * 0.6f + k, rect.Width - pad, rect.Height));
                using (var b = new SolidBrush(Color.FromArgb(hi ? 255 : 170, TextColor))) gr.DrawString(text, f, b, new RectangleF(rect.X + pad, rect.Y + pad * 0.6f, rect.Width - pad, rect.Height));
            }
        }

        // the screen's picture with no marks, cropped if it has a crop; origin is where the crop starts in the whole picture
        public Bitmap CleanPicture(List<SessionStep> g, out Rectangle view)
        {
            var bmp = Decode(PictureOf(g)); view = Rectangle.Empty; if (bmp == null) return null;
            view = ViewOf(g, bmp.Width, bmp.Height);
            if (view.Width == bmp.Width && view.Height == bmp.Height) return bmp;
            var cut = bmp.Clone(view, PixelFormat.Format32bppArgb); bmp.Dispose(); return cut;
        }

        // one screen with every step on it marked: a box around what was acted on and its number (cropped if it has a crop).
        // Text moved outside the screenshot makes the picture larger, with a transparent margin and plain text in it.
        public Bitmap RenderGroup(List<SessionStep> g, SessionStep highlight)
        {
            var bmp = Decode(PictureOf(g)); if (bmp == null) return null;
            var view = ViewOf(g, bmp.Width, bmp.Height);
            using (var gr = Graphics.FromImage(bmp)) DrawMarks(gr, g, highlight, bmp.Width, true, view, 1);
            var ext = OutsideExtent(g, bmp.Width, view);
            if (ext == view)
            {
                if (view.Width == bmp.Width && view.Height == bmp.Height) return bmp;
                var cut = bmp.Clone(view, PixelFormat.Format32bppArgb); bmp.Dispose(); return cut;
            }
            var result = new Bitmap(ext.Width, ext.Height, PixelFormat.Format32bppArgb);
            using (var gr = Graphics.FromImage(result))
            {
                gr.Clear(Color.Transparent);
                gr.DrawImage(bmp, new Rectangle(view.X - ext.X, view.Y - ext.Y, view.Width, view.Height), view, GraphicsUnit.Pixel);
                gr.TranslateTransform(-ext.X, -ext.Y); DrawMarks(gr, g, highlight, bmp.Width, true, view, 2);
            }
            bmp.Dispose(); return result;
        }
        static void DrawScroll(Graphics g, SessionStep s, Color c, float k)
        {
            float cx = s.X, cy = s.Y, L = 26 * k; var dir = s.ScrollDirection ?? "down";
            using (var p = new Pen(c, 4f * k) { EndCap = LineCap.Round, StartCap = LineCap.Round, CustomEndCap = new AdjustableArrowCap(3, 3, true) })
            {
                if (dir == "up") g.DrawLine(p, cx, cy + L, cx, cy - L); else if (dir == "left") g.DrawLine(p, cx + L, cy, cx - L, cy);
                else if (dir == "right") g.DrawLine(p, cx - L, cy, cx + L, cy); else g.DrawLine(p, cx, cy - L, cx, cy + L);
            }
        }

        // an editable SnapLite picture of one screen: boxes and numbered steps as objects
        public Doc ScreenToDoc(List<SessionStep> g, int screenNo)
        {
            Rectangle view; var bmp = CleanPicture(g, out view);
            var doc = new Doc(bmp) { Name = Name + " - screen " + screenNo, Dirty = true };
            float fullW = view.Width, k = Math.Max(1f, fullW / 1600f); float dx = -view.X, dy = -view.Y;
            foreach (var s in g)
            {
                if (s.Kind != "scroll" && ShowBoxes) doc.Objs.Add(new ShapeObj { Kind = BoxShape == "ellipse" ? ShapeKind.Ellipse : BoxShape == "rounded" ? ShapeKind.Rounded : ShapeKind.Rect, Corner = s.Round >= 0 ? Math.Min(0.5f, s.Round) * Math.Min(s.Box.Width, s.Box.Height) : 10, A = new PointF(s.Box.X + dx, s.Box.Y + dy), B = new PointF(s.Box.Right + dx, s.Box.Bottom + dy), Stroke = BoxColor, Width = 3 * k });
                float r = StepSize * k; var c = BadgeCenter(s, fullW, StepSize); c = new PointF(c.X + dx, c.Y + dy);
                if (s.Kind == "scroll" && !s.BadgePos.HasValue) c = new PointF(s.X + dx, s.Y + dy);
                if (ShowNumbers) doc.Objs.Add(new StepObj { Number = s.Seq, A = new PointF(c.X - r, c.Y - r), B = new PointF(c.X + r, c.Y + r), Fill = StepColor, Stroke = Color.White, Width = 2 });
                if (ShowText && !(s.Manual && string.IsNullOrWhiteSpace(s.StepText)) && !string.IsNullOrWhiteSpace(s.Plain))
                {
                    var tx = new TextObj { Text = LabelText(s), FontName = UsableFont(TextFont), Px = Math.Max(6, TextSize * k), Bold = TextWeight != "regular", Stroke = TextColor, WrapWidth = 340 * k, A = s.TextPos.HasValue ? new PointF(s.TextPos.Value.X + dx + 7 * k, s.TextPos.Value.Y + dy + 4 * k) : new PointF(c.X + r + 6 * k, c.Y - r * 0.6f) };
                    tx.Measure(); doc.Objs.Add(tx);
                }
            }
            return doc;
        }
    }
}

namespace SnapLite
{
    // A shared session is the session file, encrypted and signed with a key built into XLCapture, so it opens only in XLCapture
    // (ClickTrail and other tools see unreadable data). It is a lock against casual use, not a defence against someone with the program's code.
    static class SharedFile
    {
        static readonly byte[] Magic = System.Text.Encoding.ASCII.GetBytes("XLCSHR01");
        static byte[] encKey, macKey;
        static byte[] Derive(string purpose)
        {
            using (var d = new System.Security.Cryptography.Rfc2898DeriveBytes("XLYOUR.XLCapture.shared-session.v1:" + purpose, System.Text.Encoding.UTF8.GetBytes("xlcapture-share-salt"), 20000)) return d.GetBytes(32);
        }
        static void Keys() { if (encKey == null) { encKey = Derive("enc"); macKey = Derive("mac"); } }
        static byte[] Mac(byte[] data) { using (var h = new System.Security.Cryptography.HMACSHA256(macKey)) return h.ComputeHash(data); }

        // rewrite a session file in place as an encrypted one
        public static void Protect(string path)
        {
            Keys(); var plain = File.ReadAllBytes(path);
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.KeySize = 256; aes.Key = encKey; aes.GenerateIV();
                byte[] cipher; using (var enc = aes.CreateEncryptor()) cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                var body = new byte[16 + cipher.Length]; Buffer.BlockCopy(aes.IV, 0, body, 0, 16); Buffer.BlockCopy(cipher, 0, body, 16, cipher.Length);
                var mac = Mac(body);
                using (var fs = new FileStream(path, FileMode.Create)) { fs.Write(Magic, 0, Magic.Length); fs.Write(body, 0, body.Length); fs.Write(mac, 0, mac.Length); }
            }
        }
        // the session file inside a shared one; throws if it was changed or did not come from XLCapture
        public static byte[] Unprotect(string path)
        {
            Keys(); var all = File.ReadAllBytes(path);
            if (all.Length < Magic.Length + 16 + 16 + 32) throw new InvalidDataException("This shared session is incomplete or damaged.");
            for (int i = 0; i < Magic.Length; i++) if (all[i] != Magic[i]) throw new InvalidDataException("This is not a session shared from XLCapture.");
            int bodyLen = all.Length - Magic.Length - 32; var body = new byte[bodyLen]; Buffer.BlockCopy(all, Magic.Length, body, 0, bodyLen);
            var mac = new byte[32]; Buffer.BlockCopy(all, all.Length - 32, mac, 0, 32); var want = Mac(body); int diff = 0; for (int i = 0; i < 32; i++) diff |= mac[i] ^ want[i];
            if (diff != 0) throw new InvalidDataException("This shared session was changed or is damaged, so XLCapture will not open it.");
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.KeySize = 256; aes.Key = encKey; var iv = new byte[16]; Buffer.BlockCopy(body, 0, iv, 0, 16); aes.IV = iv;
                using (var dec = aes.CreateDecryptor()) return dec.TransformFinalBlock(body, 16, body.Length - 16);
            }
        }
    }
}
