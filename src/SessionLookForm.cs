// Appearance of a session's screenshots: colour presets, the boxes (shown or not, shape, colour), the step numbers (shown or not, size,
// colour), and the text drawn beside each number (shown or not, with the number or not, style, font, size, colour, weight).
// Changes show at once; Cancel puts everything back.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    class SessionLookForm : Form
    {
        readonly SessionDoc doc; readonly Action changed; readonly Panel preview; bool busy;
        readonly ThemedCheck showBoxes, showNum, showText, textNum; readonly ThemedCombo preset, shape, stepSize, tstyle, font, textSize, weight;
        readonly ColorSwatch boxColor, stepColor, textColor, fillColor; readonly ThemedCombo fillOpacity; readonly Label fontNote; readonly FlatButton savePreset, delPreset;
        static readonly int[] Sizes = { 8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 40 };
        static readonly int[] TextSizes = { 10, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48 };
        static readonly string[] Shapes = { "rect", "rounded", "ellipse" }, ShapeNames = { "Rectangle", "Rounded", "Ellipse" };
        static readonly string[] Weights = { "regular", "semibold", "bold" }, WeightNames = { "Regular", "Semibold", "Bold" };
        // name, box, number, text: a preset sets all three colours at once. One the person saves also holds the font, its size and its weight (7 parts); the built-in ones leave the font alone.
        static readonly string[][] BuiltIn = {
            new[] { "XLCapture red", "#D62B2B", "#D62B2B", "#1B1B1B" }, new[] { "Gold and charcoal", "#FFC300", "#282928", "#282928" }, new[] { "Blue", "#2563EB", "#2563EB", "#16294D" },
            new[] { "Green", "#16A34A", "#16A34A", "#14391F" }, new[] { "Charcoal", "#282928", "#282928", "#282928" } };
        static List<string[]> AllPresets() { var l = new List<string[]>(BuiltIn); l.AddRange(Settings.ColorPresets); return l; }
        static Color Html(string s) { try { return ColorTranslator.FromHtml(s); } catch { return Color.Black; } }
        static string Hex(Color c) { return ColorTranslator.ToHtml(Color.FromArgb(255, c)).ToUpperInvariant(); }

        public SessionLookForm(SessionDoc doc, Action changed)
        {
            this.doc = doc; this.changed = changed;
            Text = "Appearance"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White; Font = Theme.Body(10f); Icon = AppIcon.Get(); ClientSize = Dpi.S(800, 610);
            int m = Dpi.S(24), x2 = Dpi.S(420), w1 = Dpi.S(360), w2 = Dpi.S(356), sw = Dpi.S(64);
            Func<int, int, int, string, Label> head = delegate(int x, int y, int w, string s) { var l = new Label { Text = s, AutoSize = false, Location = new Point(x, y), Size = new Size(w, Dpi.S(24)), Font = Theme.Semi(10.5f), ForeColor = Theme.Ink }; Controls.Add(l); return l; };
            Func<int, int, string, Label> lab = delegate(int x, int y, string s) { var l = new Label { Text = s, AutoSize = false, Location = new Point(x, y + Dpi.S(4)), Size = new Size(Dpi.S(78), Dpi.S(22)), ForeColor = Theme.Slate, Font = Theme.Body(9.5f) }; Controls.Add(l); return l; };
            int y0 = Dpi.S(20);

            // ---- colour presets: one name sets the colours of the boxes, the numbers and the text together
            head(m, y0, Dpi.S(300), "Preset"); y0 += Dpi.S(30);
            preset = Combo(m, y0, Dpi.S(260));
            savePreset = new FlatButton("Save as preset...", false) { Location = new Point(m + Dpi.S(274), y0 - Dpi.S(2)), Size = Dpi.S(160, 34) }; Controls.Add(savePreset);
            delPreset = new FlatButton("Delete", false) { Location = new Point(m + Dpi.S(442), y0 - Dpi.S(2)), Size = Dpi.S(96, 34) }; Controls.Add(delPreset);
            var presetHint = new Label { Text = "A preset sets the colours of the boxes, the numbers and the text, and the font, its size and its weight.", AutoSize = false, Location = new Point(m + Dpi.S(552), y0 - Dpi.S(8)), Size = new Size(Dpi.S(224), Dpi.S(54)), ForeColor = Theme.Slate, Font = Theme.Body(8.5f) }; Controls.Add(presetHint);
            y0 += Dpi.S(56);
            int yL = y0, yR = y0;

            // ---- left: boxes and step numbers
            head(m, yL, w1, "Boxes"); yL += Dpi.S(30);
            showBoxes = new ThemedCheck { Text = "Show boxes", Width = Dpi.S(260), Location = new Point(m, yL), Checked = doc.ShowBoxes }; Controls.Add(showBoxes); yL += Dpi.S(34);
            lab(m, yL, "Shape"); shape = Combo(m + Dpi.S(88), yL, Dpi.S(170)); shape.Items.AddRange(ShapeNames); shape.SelectedIndex = Math.Max(0, Array.IndexOf(Shapes, doc.BoxShape));
            boxColor = new ColorSwatch(doc.BoxColor) { Title = "Box colour", Location = new Point(m + w1 - sw, yL - Dpi.S(1)), AccessibleName = "Box colour" }; Controls.Add(boxColor); yL += Dpi.S(48);
            head(m, yL, w1, "Step numbers"); yL += Dpi.S(30);
            showNum = new ThemedCheck { Text = "Show step numbers", Width = Dpi.S(260), Location = new Point(m, yL), Checked = doc.ShowNumbers }; Controls.Add(showNum); yL += Dpi.S(34);
            lab(m, yL, "Size"); stepSize = Combo(m + Dpi.S(88), yL, Dpi.S(170)); foreach (int s in Sizes) stepSize.Items.Add(s + " px"); stepSize.SelectedIndex = NearestIndex(Sizes, doc.StepSize);
            stepColor = new ColorSwatch(doc.StepColor) { Title = "Number colour", Location = new Point(m + w1 - sw, yL - Dpi.S(1)), AccessibleName = "Number colour" }; Controls.Add(stepColor); yL += Dpi.S(48);

            // ---- right: the text on the screenshot
            head(x2, yR, w2, "Text on the screenshot"); yR += Dpi.S(30);
            showText = new ThemedCheck { Text = "Show each step's text", Width = Dpi.S(320), Location = new Point(x2, yR), Checked = doc.ShowText }; Controls.Add(showText); yR += Dpi.S(34);
            textNum = new ThemedCheck { Text = "Include the step number in the text", Width = Dpi.S(340), Location = new Point(x2, yR), Checked = doc.TextNumber }; Controls.Add(textNum);
            new ToolTip().SetToolTip(textNum, "Shows the text as \"2. Click Save\""); yR += Dpi.S(34);
            lab(x2, yR, "Style"); tstyle = Combo(x2 + Dpi.S(88), yR, Dpi.S(220)); tstyle.Items.AddRange(new object[] { "Callout, rounded box", "Callout, square box", "Plain text" }); tstyle.SelectedIndex = doc.TextStyle == "plain" ? 2 : doc.CalloutShape == "square" ? 1 : 0; yR += Dpi.S(38);
            lab(x2, yR, "Box fill"); fillColor = new ColorSwatch(doc.CalloutColor) { Title = "Callout box colour", Location = new Point(x2 + Dpi.S(88), yR - Dpi.S(1)), AccessibleName = "Callout box colour" }; Controls.Add(fillColor);
            fillOpacity = Combo(x2 + Dpi.S(88) + sw + Dpi.S(12), yR, Dpi.S(130)); for (int o = 100; o >= 0; o -= 10) fillOpacity.Items.Add(o + "% solid"); fillOpacity.SelectedIndex = Math.Max(0, Math.Min(10, (100 - doc.CalloutOpacity + 5) / 10)); yR += Dpi.S(44);
            lab(x2, yR, "Font"); font = Combo(x2 + Dpi.S(88), yR, w2 - Dpi.S(88));
            fontNote = new Label { AutoSize = false, Location = new Point(x2 + Dpi.S(88), yR + Dpi.S(34)), Size = new Size(w2 - Dpi.S(88), Dpi.S(18)), Font = Theme.Body(8.5f), ForeColor = Theme.Slate }; Controls.Add(fontNote); yR += Dpi.S(58);
            var fams = new InstalledFontCollection().Families.Select(f => f.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            // the two house fonts first (Neue Haas Grotesk Text Pro comes with Microsoft 365 and is listed whether or not it has been downloaded to this PC), then the rest
            foreach (var house in new[] { "Neue Haas Grotesk Text Pro", "Segoe UI" }) { fams.RemoveAll(n => string.Equals(n, house, StringComparison.OrdinalIgnoreCase)); fams.Insert(0, house); }
            if (!fams.Contains(doc.TextFont)) fams.Insert(0, doc.TextFont);
            foreach (var f in fams) font.Items.Add(f); font.SelectedItem = doc.TextFont;
            fontNote.Text = SessionDoc.FontInstalled(doc.TextFont) ? "" : "Not installed on this PC, so Segoe UI is used here. It shows when the font is installed.";
            lab(x2, yR, "Size"); textSize = Combo(x2 + Dpi.S(88), yR, Dpi.S(120)); foreach (int s in TextSizes) textSize.Items.Add(s + " px"); textSize.SelectedIndex = NearestIndex(TextSizes, doc.TextSize);
            textColor = new ColorSwatch(doc.TextColor) { Title = "Text colour", Location = new Point(x2 + w2 - sw, yR - Dpi.S(1)), AccessibleName = "Text colour" }; Controls.Add(textColor); yR += Dpi.S(38);
            lab(x2, yR, "Weight"); weight = Combo(x2 + Dpi.S(88), yR, Dpi.S(160)); weight.Items.AddRange(WeightNames); weight.SelectedIndex = Math.Max(0, Array.IndexOf(Weights, doc.TextWeight)); yR += Dpi.S(48);

            int py = Math.Max(yL, yR) + Dpi.S(8);
            preview = new Panel { Location = new Point(m, py), Size = new Size(Dpi.S(800) - 2 * m, Dpi.S(124)), BackColor = Color.White };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(preview, true, null);
            preview.Paint += PaintPreview; Controls.Add(preview);
            int by = py + Dpi.S(124) + Dpi.S(22);
            var ok = new FlatButton("Done", true) { Location = new Point(Dpi.S(800) - m - Dpi.S(96), by), Size = Dpi.S(96, 38), DialogResult = DialogResult.OK };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(800) - m - Dpi.S(96) - Dpi.S(8) - Dpi.S(88), by), Size = Dpi.S(88, 38), DialogResult = DialogResult.Cancel };
            Controls.Add(cancel); Controls.Add(ok); AcceptButton = ok; CancelButton = cancel; ClientSize = new Size(Dpi.S(800), by + Dpi.S(38) + Dpi.S(24));

            FillPresets(); SyncPreset();
            EventHandler any = delegate { Apply(); };
            EventHandler colours = delegate { if (busy) return; Apply(); SyncPreset(); };
            showBoxes.CheckedChanged += any; showText.CheckedChanged += any; showNum.CheckedChanged += any; textNum.CheckedChanged += any; shape.SelectedIndexChanged += any; stepSize.SelectedIndexChanged += any; font.SelectedIndexChanged += colours;
            textSize.SelectedIndexChanged += colours; weight.SelectedIndexChanged += colours; tstyle.SelectedIndexChanged += any; fillOpacity.SelectedIndexChanged += any; fillColor.ColorChanged += any;
            boxColor.ColorChanged += colours; stepColor.ColorChanged += colours; textColor.ColorChanged += colours;
            preset.SelectedIndexChanged += delegate { if (busy || preset.SelectedIndex <= 0) return; ApplyPreset(AllPresets()[preset.SelectedIndex - 1]); };
            savePreset.Click += delegate { SavePreset(); }; delPreset.Click += delegate { DeletePreset(); };
        }

        ThemedCombo Combo(int x, int y, int w)
        {
            var c = new ThemedCombo { Location = new Point(x, y), Width = w, Font = Theme.Body(10f), DropDownHeight = Dpi.S(300) }; Controls.Add(c); return c;
        }
        static int NearestIndex(int[] a, int v) { int best = 0; for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - v) < Math.Abs(a[best] - v)) best = i; return best; }

        // ---- presets
        void FillPresets() { busy = true; preset.Items.Clear(); preset.Items.Add("Custom"); foreach (var p in AllPresets()) preset.Items.Add(p[0]); busy = false; }
        // the font part of the window as it is now: family, size, weight
        string[] FontNow() { return new[] { font.SelectedItem != null ? font.SelectedItem.ToString() : doc.TextFont, TextSizes[Math.Max(0, textSize.SelectedIndex)].ToString(), Weights[Math.Max(0, weight.SelectedIndex)] }; }
        // show the preset whose colours (and font, when it has one) are the ones set now, else "Custom"
        void SyncPreset()
        {
            busy = true; int idx = 0; var all = AllPresets(); var fn = FontNow();
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (Hex(boxColor.Color) != p[1].ToUpperInvariant() || Hex(stepColor.Color) != p[2].ToUpperInvariant() || Hex(textColor.Color) != p[3].ToUpperInvariant()) continue;
                if (p.Length >= 7 && !(string.Equals(p[4], fn[0], StringComparison.OrdinalIgnoreCase) && p[5] == fn[1] && p[6] == fn[2])) continue;
                idx = i + 1; break;
            }
            preset.SelectedIndex = idx; delPreset.Enabled = idx > BuiltIn.Length; busy = false;
        }
        void ApplyPreset(string[] p)
        {
            busy = true; boxColor.Color = Html(p[1]); stepColor.Color = Html(p[2]); textColor.Color = Html(p[3]);
            if (p.Length >= 7)
            {
                if (!font.Items.Contains(p[4])) font.Items.Add(p[4]);
                font.SelectedItem = p[4];
                int px; if (int.TryParse(p[5], out px)) textSize.SelectedIndex = NearestIndex(TextSizes, px);
                int w = Array.IndexOf(Weights, p[6]); if (w >= 0) weight.SelectedIndex = w;
            }
            busy = false;
            Apply(); SyncPreset();
        }
        // what "Save as preset" stores: the colours, then the font, its size and its weight as they are now
        internal string[] MakeEntry(string name, bool allColours, string pick)
        {
            var fn = FontNow();
            return allColours ? new[] { name, pick, pick, pick, fn[0], fn[1], fn[2] } : new[] { name, Hex(boxColor.Color), Hex(stepColor.Color), Hex(textColor.Color), fn[0], fn[1], fn[2] };
        }
        void SavePreset()
        {
            var r = Dialogs.Form(this, "Save preset", "Save",
                new Dialogs.Field { Label = "Name", Kind = "text", Value = "" },
                new Dialogs.Field { Label = "Colour", Kind = "color", Value = boxColor.Color },
                new Dialogs.Field { Label = "For all colours", Kind = "check", Value = true });
            if (r == null) return;
            string name = new string(((string)r["Name"] ?? "").Where(c => c != '|' && c != '=' && c != '\r' && c != '\n').ToArray()).Trim();
            if (name.Length == 0) return;
            if (BuiltIn.Any(b => string.Equals(b[0], name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "\"" + name + "\" is the name of a built-in preset. Choose another name.", "Preset"); return; }
            // ticked: the chosen colour is used for the boxes, the numbers and the text; not ticked: the three colours as they are now. The font, its size and its weight are saved either way.
            var entry = MakeEntry(name, (bool)r["For all colours"], Hex((Color)r["Colour"]));
            int at = Settings.ColorPresets.FindIndex(p => string.Equals(p[0], name, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) Settings.ColorPresets[at] = entry; else Settings.ColorPresets.Add(entry);
            Settings.Save(); FillPresets(); ApplyPreset(entry);        // and it is applied at once
        }
        void DeletePreset()
        {
            int i = preset.SelectedIndex - 1 - BuiltIn.Length; if (i < 0 || i >= Settings.ColorPresets.Count) return;
            if (MessageBox.Show(this, "Delete the preset \"" + Settings.ColorPresets[i][0] + "\"?", "Preset", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Settings.ColorPresets.RemoveAt(i); Settings.Save(); FillPresets(); SyncPreset();
        }

        void Apply()
        {
            if (busy) return;
            doc.ShowBoxes = showBoxes.Checked; doc.ShowNumbers = showNum.Checked; doc.ShowText = showText.Checked; doc.TextNumber = textNum.Checked; doc.BoxShape = Shapes[Math.Max(0, shape.SelectedIndex)];
            doc.StepSize = Sizes[Math.Max(0, stepSize.SelectedIndex)]; doc.TextSize = TextSizes[Math.Max(0, textSize.SelectedIndex)]; doc.TextWeight = Weights[Math.Max(0, weight.SelectedIndex)]; doc.TextStyle = tstyle.SelectedIndex == 2 ? "plain" : "callout"; if (tstyle.SelectedIndex != 2) doc.CalloutShape = tstyle.SelectedIndex == 1 ? "square" : "rounded";
            doc.CalloutColor = fillColor.Color; doc.CalloutOpacity = Math.Max(0, 100 - 10 * Math.Max(0, fillOpacity.SelectedIndex)); fillColor.Enabled = fillOpacity.Enabled = tstyle.SelectedIndex != 2;
            if (font.SelectedItem != null) doc.TextFont = font.SelectedItem.ToString();
            fontNote.Text = SessionDoc.FontInstalled(doc.TextFont) ? "" : "Not installed on this PC, so Segoe UI is used here. It shows when the font is installed.";
            doc.BoxColor = boxColor.Color; doc.StepColor = stepColor.Color; doc.TextColor = textColor.Color;
            preview.Invalidate(); changed();
        }

        // a sample screen with one step, drawn the way a 1600 pixel wide screenshot is
        void PaintPreview(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var r = new Rectangle(0, 0, preview.Width - 1, preview.Height - 1);
            using (var p = Theme.RoundRect(r, Dpi.S(8)))
            {
                g.SetClip(p);
                using (var b = new LinearGradientBrush(r, Color.FromArgb(0xF4, 0xF6, 0xF9), Color.FromArgb(0xE2, 0xE7, 0xEE), 90f)) g.FillRectangle(b, r);
                using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, Dpi.S(16), Dpi.S(14), Dpi.S(150), Dpi.S(12));
                using (var b = new SolidBrush(Color.FromArgb(0xC9, 0xD1, 0xDB))) { g.FillRectangle(b, Dpi.S(16), Dpi.S(34), Dpi.S(200), Dpi.S(9)); g.FillRectangle(b, Dpi.S(16), Dpi.S(50), Dpi.S(170), Dpi.S(9)); }
                var btn = new Rectangle(Dpi.S(30), Dpi.S(72), Dpi.S(104), Dpi.S(34));
                using (var p2 = Theme.RoundRect(btn, Dpi.S(6))) { using (var b = new SolidBrush(Color.FromArgb(0x3B, 0x6F, 0xD8))) g.FillPath(b, p2); }
                using (var f = Theme.Semi(9.5f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("Save", f, Brushes.White, btn, sf);
                var step = new SessionStep { Seq = 2, Kind = "click", Label = "Save", Role = "Button", Box = btn };
                doc.DrawMarks(g, new List<SessionStep> { step }, null, 1600);
                g.ResetClip();
                using (var pen = new Pen(Theme.Rule)) g.DrawPath(pen, p);
            }
        }
    }
}
