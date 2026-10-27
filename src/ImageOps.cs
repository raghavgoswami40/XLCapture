// Image, Filters and Arrange menus plus the live-preview dialog.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SnapLite
{
    class Slider { public string Name; public int Min, Max, Value; public string[] Labels; public string Suffix = ""; }

    // Sliders on the left, live preview on the right. fx(source, values, colour, scale) runs on a reduced copy while dragging.
    class PreviewDialog : DpiForm
    {
        public int[] Values; public Color ColorValue;
        readonly Bitmap small; readonly float scale;
        readonly Func<Bitmap, int[], Color, float, Bitmap> fx;
        readonly List<TrackBar> bars = new List<TrackBar>(); readonly List<Label> vals = new List<Label>();
        readonly Slider[] sliders; Bitmap preview; readonly Panel view; readonly Timer debounce = new Timer { Interval = 70 };
        ColorChip chip;

        public PreviewDialog(IWin32Window owner, string title, Bitmap source, Slider[] sliders, Func<Bitmap, int[], Color, float, Bitmap> fx, string colorLabel, Color color)
        {
            DesignPixels = true;
            this.sliders = sliders; this.fx = fx;
            Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Theme.Sheet; ForeColor = Theme.Ink; Font = Theme.Body(9f); Icon = AppIcon.Get(); AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            float k = Math.Min(1f, Math.Min(520f / source.Width, 360f / source.Height));
            scale = k;
            small = k < 1 ? Effects.Resize(source, Math.Max(1, (int)(source.Width * k)), Math.Max(1, (int)(source.Height * k))) : Effects.To32(source);
            int rows = sliders.Length + (colorLabel != null ? 1 : 0), h = Math.Max(420, 90 + rows * 52 + 60);
            ClientSize = new Size(900, h);
            int y = 16;
            foreach (var s in sliders)
            {
                Controls.Add(new Label { Text = s.Name, Location = new Point(16, y), AutoSize = true, ForeColor = Theme.Slate });
                var v = new Label { Location = new Point(300, y), Size = new Size(64, 20), TextAlign = ContentAlignment.TopRight, Font = Theme.Label(9f) };
                var tb = new TrackBar { Minimum = s.Min, Maximum = s.Max, Value = Math.Max(s.Min, Math.Min(s.Max, s.Value)), TickStyle = TickStyle.None, Location = new Point(10, y + 18), Width = 356, Height = 28 };
                tb.ValueChanged += delegate { UpdateLabels(); debounce.Stop(); debounce.Start(); };
                bars.Add(tb); vals.Add(v); Controls.Add(tb); Controls.Add(v); y += 52;
            }
            if (colorLabel != null)
            {
                Controls.Add(new Label { Text = colorLabel, Location = new Point(16, y + 4), AutoSize = true, ForeColor = Theme.Slate });
                chip = new ColorChip(color, false, colorLabel) { Location = new Point(120, y), BackColor = Theme.Sheet };
                chip.ColorChanged += delegate { debounce.Stop(); debounce.Start(); };
                Controls.Add(chip); y += 44;
            }
            view = new Panel { Location = new Point(384, 16), Size = new Size(500, h - 84), BackColor = Theme.Paper };
            view.Paint += PaintPreview;
            Controls.Add(view);
            var ok = new FlatButton("Apply", true) { DialogResult = DialogResult.OK, Location = new Point(ClientSize.Width - 190, h - 48), Size = new Size(84, 32) };
            var cancel = new FlatButton("Cancel", false) { DialogResult = DialogResult.Cancel, Location = new Point(ClientSize.Width - 98, h - 48), Size = new Size(84, 32) };
            Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
            debounce.Tick += delegate { debounce.Stop(); Render(); };
            UpdateLabels(); Render();
            FormClosing += delegate { Values = bars.Select(b => b.Value).ToArray(); if (chip != null) ColorValue = chip.Value; debounce.Dispose(); if (preview != null) preview.Dispose(); small.Dispose(); };
        }
        void UpdateLabels()
        {
            for (int i = 0; i < bars.Count; i++) { var s = sliders[i]; int v = bars[i].Value; vals[i].Text = s.Labels != null && v >= 0 && v < s.Labels.Length ? s.Labels[v] : v + s.Suffix; }
        }
        void Render()
        {
            var vs = bars.Select(b => b.Value).ToArray();
            try { var p = fx(small, vs, chip != null ? chip.Value : Color.Black, scale); if (preview != null) preview.Dispose(); preview = p; } catch { }
            view.Invalidate();
        }
        void PaintPreview(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Paper);
            if (preview == null) return;
            float k = Math.Min(1f, Math.Min((view.Width - 24f) / preview.Width, (view.Height - 24f) / preview.Height));
            int w = (int)(preview.Width * k), h = (int)(preview.Height * k);
            var r = new Rectangle((view.Width - w) / 2, (view.Height - h) / 2, w, h);
            Theme.Checker(g, r, 8);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(preview, r);
            using (var p = new Pen(Theme.Rule)) g.DrawRectangle(p, r);
        }
    }

    partial class EditorForm
    {
        Bitmap Source { get { return Doc.Base; } }


        // ------------------------------------------------------------ Image menu
        ToolStripMenuItem BuildImageMenu()
        {
            var rot = new ToolStripMenuItem("Rotate");
            rot.DropDownItems.AddRange(new ToolStripItem[] {
                M("90 degrees right", delegate { Doc.RotateCW(); canvas.Sel.Clear(); }), M("90 degrees left", delegate { Doc.RotateCCW(); canvas.Sel.Clear(); }),
                M("180 degrees", delegate { Doc.Rotate180(); }), M("Any angle...", delegate { RotateAny(); }),
                new ToolStripSeparator(), M("Flip horizontally", delegate { Doc.FlipH(); }), M("Flip vertically", delegate { Doc.FlipV(); }) });
            return DD(Ic.Image, "Image", "Image menu",
                M("Resize image...", delegate { ResizeDialog(); }), M("Canvas size...", delegate { CanvasDialog(); }), M("Trim borders", delegate { Trim(); }),
                rot, new ToolStripSeparator(),
                M("Perspective...", delegate { PerspectiveDialog(); }), M("Border...", delegate { BorderDialog(); }), M("Drop shadow...", delegate { ShadowDialog(); }),
                M("Edge effects...", delegate { EdgeDialog(); }), M("Add watermark...", delegate { Watermark(); }),
                new ToolStripSeparator(), M("Flatten annotations into the picture", delegate { Doc.Flatten(); canvas.Sel.Clear(); OnSelectionChanged(); }));
        }

        void ResizeDialog()
        {
            using (var f = Dialogs.Base("Resize image", 400, 270))
            {
                var w = new NumField(1, 30000, Doc.Width, 90) { Location = new Point(Dpi.S(150), Dpi.S(24)) };
                var h = new NumField(1, 30000, Doc.Height, 90) { Location = new Point(Dpi.S(150), Dpi.S(66)) };
                var pct = new NumField(1, 1000, 100, 90) { Location = new Point(Dpi.S(150), Dpi.S(108)) };
                var lk = new ThemedCheck { Text = "Keep proportions", Checked = true, Width = Dpi.S(220), Location = new Point(Dpi.S(150), Dpi.S(152)) };
                f.Controls.Add(new Label { Text = "Width (px)", Location = new Point(Dpi.S(24), Dpi.S(28)), AutoSize = true, ForeColor = Theme.Slate });
                f.Controls.Add(new Label { Text = "Height (px)", Location = new Point(Dpi.S(24), Dpi.S(70)), AutoSize = true, ForeColor = Theme.Slate });
                f.Controls.Add(new Label { Text = "Scale (%)", Location = new Point(Dpi.S(24), Dpi.S(112)), AutoSize = true, ForeColor = Theme.Slate });
                bool busy = false; float ar = Doc.Width / (float)Doc.Height;
                w.ValueChanged += delegate { if (busy) return; busy = true; if (lk.Checked) h.Value = Math.Max(1, (decimal)Math.Round((double)w.Value / ar)); pct.Value = Math.Max(1, Math.Min(1000, Math.Round(w.Value * 100 / Doc.Width))); busy = false; };
                h.ValueChanged += delegate { if (busy) return; busy = true; if (lk.Checked) w.Value = Math.Max(1, (decimal)Math.Round((double)h.Value * ar)); pct.Value = Math.Max(1, Math.Min(1000, Math.Round(h.Value * 100 / Doc.Height))); busy = false; };
                pct.ValueChanged += delegate { if (busy) return; busy = true; w.Value = Math.Max(1, Math.Round(Doc.Width * pct.Value / 100)); h.Value = Math.Max(1, Math.Round(Doc.Height * pct.Value / 100)); busy = false; };
                var ok = new FlatButton("Resize", true) { DialogResult = DialogResult.OK, Location = new Point(Dpi.S(184), Dpi.S(206)), Size = Dpi.S(100, 38) };
                var cancel = new FlatButton("Cancel", false) { DialogResult = DialogResult.Cancel, Location = new Point(Dpi.S(292), Dpi.S(206)), Size = Dpi.S(84, 38) };
                f.Controls.AddRange(new Control[] { w, h, pct, lk, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) == DialogResult.OK) { Doc.Resize((int)w.Value, (int)h.Value); canvas.Sel.Clear(); Fit(); Info("Resized to " + Doc.Width + " x " + Doc.Height); }
            }
        }
        void CanvasDialog()
        {
            string[] anchors = { "Top left", "Top", "Top right", "Left", "Centre", "Right", "Bottom left", "Bottom", "Bottom right" };
            var r = Dialogs.Form(this, "Canvas size", "Apply",
                new Dialogs.Field { Label = "Width (px)", Kind = "int", Value = Doc.Width, Min = 1, Max = 30000 },
                new Dialogs.Field { Label = "Height (px)", Kind = "int", Value = Doc.Height, Min = 1, Max = 30000 },
                new Dialogs.Field { Label = "Anchor", Kind = "choice", Choices = anchors, Value = "Centre" },
                new Dialogs.Field { Label = "New area colour", Kind = "color", Value = Color.White });
            if (r == null) return;
            int a = Array.IndexOf(anchors, (string)r["Anchor"]);
            Doc.CanvasSize((int)r["Width (px)"], (int)r["Height (px)"], (a % 3) / 2f, (a / 3) / 2f, (Color)r["New area colour"]);
            Fit();
        }
        void Trim()
        {
            var rc = Effects.AutoCropRect(Doc.Base, 6);
            if (rc.Width == Doc.Width && rc.Height == Doc.Height) { Info("Nothing to trim: the borders are not a single colour."); return; }
            Doc.Crop(rc); Fit(); Info("Trimmed to " + rc.Width + " x " + rc.Height);
        }
        void RotateAny()
        {
            var d = new PreviewDialog(this, "Rotate by angle", Source, new[] { new Slider { Name = "Angle", Min = -180, Max = 180, Value = 0, Suffix = " deg" } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Effects.RotateAny(src, v[0], Color.Transparent); }, null, Color.Black);
            if (d.ShowDialog(this) == DialogResult.OK && d.Values[0] != 0) { int a = d.Values[0]; Doc.ApplyGrowing(b => Effects.RotateAny(b, a, Color.Transparent)); Fit(); }
        }
        void PerspectiveDialog()
        {
            var d = new PreviewDialog(this, "Perspective", Source, new[] {
                new Slider { Name = "Squeeze top edge", Min = 0, Max = 45, Value = 0, Suffix = " %" }, new Slider { Name = "Squeeze bottom edge", Min = 0, Max = 45, Value = 0, Suffix = " %" },
                new Slider { Name = "Squeeze left edge", Min = 0, Max = 45, Value = 0, Suffix = " %" }, new Slider { Name = "Squeeze right edge", Min = 0, Max = 45, Value = 0, Suffix = " %" } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Persp(src, v); }, null, Color.Black);
            if (d.ShowDialog(this) == DialogResult.OK && d.Values.Any(x => x != 0)) { var v = d.Values; Doc.ApplyPixels(b => Persp(b, v)); }
        }
        static Bitmap Persp(Bitmap src, int[] v)
        {
            float w = src.Width, h = src.Height;
            return Effects.Perspective(src, new PointF(w * v[0] / 100f, h * v[2] / 100f), new PointF(w * (1 - v[0] / 100f), h * v[3] / 100f),
                new PointF(w * (1 - v[1] / 100f), h * (1 - v[3] / 100f)), new PointF(w * v[1] / 100f, h * (1 - v[2] / 100f)), src.Width, src.Height);
        }
        void BorderDialog()
        {
            var d = new PreviewDialog(this, "Border", Source, new[] { new Slider { Name = "Thickness", Min = 1, Max = 80, Value = 8, Suffix = " px" } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Effects.Border(src, Math.Max(1, (int)(v[0] * s)), c); }, "Colour", Theme.Ink);
            if (d.ShowDialog(this) == DialogResult.OK) { int w = d.Values[0]; var c = d.ColorValue; Doc.ApplyGrowing(b => Effects.Border(b, w, c)); Fit(); }
        }
        void ShadowDialog()
        {
            var d = new PreviewDialog(this, "Drop shadow", Source, new[] {
                new Slider { Name = "Offset right", Min = -40, Max = 40, Value = 6, Suffix = " px" }, new Slider { Name = "Offset down", Min = -40, Max = 40, Value = 8, Suffix = " px" },
                new Slider { Name = "Blur", Min = 0, Max = 40, Value = 10, Suffix = " px" }, new Slider { Name = "Opacity", Min = 5, Max = 100, Value = 45, Suffix = " %" } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Effects.DropShadow(src, (int)(v[0] * s), (int)(v[1] * s), (int)(v[2] * s), c, v[3]); }, "Colour", Color.Black);
            if (d.ShowDialog(this) == DialogResult.OK) { var v = d.Values; var c = d.ColorValue; Doc.ApplyGrowing(b => Effects.DropShadow(b, v[0], v[1], v[2], c, v[3])); Fit(); }
        }
        void EdgeDialog()
        {
            var d = new PreviewDialog(this, "Edge effects", Source, new[] {
                new Slider { Name = "Style", Min = 0, Max = 3, Value = 0, Labels = new[] { "Torn paper", "Wave", "Sawtooth", "Soft fade" } },
                new Slider { Name = "Depth", Min = 2, Max = 60, Value = 14, Suffix = " px" } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Effects.Edge(src, (EdgeKind)v[0], Math.Max(2, (int)(v[1] * s)), 7); }, null, Color.Black);
            if (d.ShowDialog(this) == DialogResult.OK) { var v = d.Values; Doc.ApplyPixels(b => Effects.Edge(b, (EdgeKind)v[0], v[1], 7)); }
        }
        void Watermark()
        {
            using (var o = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" })
            {
                if (o.ShowDialog(this) != DialogResult.OK) return;
                Bitmap bmp; using (var im = Image.FromFile(o.FileName)) bmp = Effects.To32(new Bitmap(im));
                float k = Math.Min(1f, Math.Min(Doc.Width * 0.3f / bmp.Width, Doc.Height * 0.3f / bmp.Height));
                float w = bmp.Width * k, h = bmp.Height * k, m = 16;
                var obj = new ImageObj { Img = bmp, A = new PointF(Doc.Width - w - m, Doc.Height - h - m), B = new PointF(Doc.Width - m, Doc.Height - m), Opacity = 0.5f };
                Doc.Add(obj); SelectTool(Tool.Select); canvas.Sel = new List<Obj> { obj }; OnSelectionChanged();
            }
        }

        // ------------------------------------------------------------ Filters menu
        ToolStripMenuItem BuildFilterMenu()
        {
            return DD(Ic.Effects, "Filters", "Colour and filters",
                M("Adjust colour...", delegate { AdjustDialog(); }),
                M("Replace colour...", delegate { ReplaceColorDialog(); }), M("Make background transparent", delegate { MakeTransparent(); }),
                new ToolStripSeparator(),
                M("Grayscale", delegate { Doc.ApplyPixels(Effects.Grayscale); }), M("Sepia", delegate { Doc.ApplyPixels(Effects.Sepia); }), M("Invert colours", delegate { Doc.ApplyPixels(Effects.Invert); }),
                M("Posterize...", delegate { PosterizeDialog(); }),
                new ToolStripSeparator(),
                M("Sharpen", delegate { Doc.ApplyPixels(Effects.Sharpen); }), M("Blur...", delegate { BlurDialog(); }), M("Emboss", delegate { Doc.ApplyPixels(Effects.Emboss); }), M("Detect edges", delegate { Doc.ApplyPixels(Effects.EdgeDetect); }));
        }
        void AdjustDialog()
        {
            var d = new PreviewDialog(this, "Adjust colour", Source, new[] {
                new Slider { Name = "Brightness", Min = -100, Max = 100, Value = 0 }, new Slider { Name = "Contrast", Min = -100, Max = 100, Value = 0 },
                new Slider { Name = "Saturation", Min = -100, Max = 100, Value = 0 }, new Slider { Name = "Hue", Min = -180, Max = 180, Value = 0, Suffix = " deg" },
                new Slider { Name = "Gamma", Min = 30, Max = 300, Value = 100, Suffix = " %" } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Effects.Adjust(src, v[0], v[1], v[2], v[3], v[4] / 100f); }, null, Color.Black);
            if (d.ShowDialog(this) == DialogResult.OK) { var v = d.Values; Doc.ApplyPixels(b => Effects.Adjust(b, v[0], v[1], v[2], v[3], v[4] / 100f)); }
        }
        void PosterizeDialog()
        {
            var d = new PreviewDialog(this, "Posterize", Source, new[] { new Slider { Name = "Levels per channel", Min = 2, Max = 16, Value = 5 } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Effects.Posterize(src, v[0]); }, null, Color.Black);
            if (d.ShowDialog(this) == DialogResult.OK) { int l = d.Values[0]; Doc.ApplyPixels(b => Effects.Posterize(b, l)); }
        }
        void BlurDialog()
        {
            var d = new PreviewDialog(this, "Blur picture", Source, new[] { new Slider { Name = "Radius", Min = 1, Max = 40, Value = 6, Suffix = " px" } },
                delegate(Bitmap src, int[] v, Color c, float s) { return Effects.GaussianBlur(src, Math.Max(1, (int)(v[0] * s))); }, null, Color.Black);
            if (d.ShowDialog(this) == DialogResult.OK) { int r = d.Values[0]; Doc.ApplyPixels(b => Effects.GaussianBlur(b, r)); }
        }
        void ReplaceColorDialog()
        {
            var r = Dialogs.Form(this, "Replace colour", "Replace",
                new Dialogs.Field { Label = "Find colour", Kind = "color", Value = canvas.Style.Stroke },
                new Dialogs.Field { Label = "Replace with", Kind = "color", Value = Color.White },
                new Dialogs.Field { Label = "Tolerance (0-255)", Kind = "int", Value = 30, Min = 0, Max = 255 },
                new Dialogs.Field { Label = "Make transparent", Kind = "check", Value = false });
            if (r == null) return;
            var from = (Color)r["Find colour"]; var to = (Color)r["Replace with"]; int tol = (int)r["Tolerance (0-255)"]; bool tr = (bool)r["Make transparent"];
            Doc.ApplyPixels(b => Effects.ReplaceColor(b, from, to, tol, tr));
        }
        void MakeTransparent()
        {
            var c = Doc.Base.GetPixel(0, 0);
            Doc.ApplyPixels(b => Effects.ReplaceColor(b, c, Color.Transparent, 18, true)); Info("Made the corner colour transparent.");
        }

        // ------------------------------------------------------------ Arrange menu
        ToolStripMenuItem BuildArrangeMenu()
        {
            var align = new ToolStripMenuItem("Align");
            align.DropDownItems.AddRange(new ToolStripItem[] { M("Left", delegate { canvas.Align("left"); }), M("Centre horizontally", delegate { canvas.Align("hcenter"); }), M("Right", delegate { canvas.Align("right"); }),
                new ToolStripSeparator(), M("Top", delegate { canvas.Align("top"); }), M("Centre vertically", delegate { canvas.Align("vcenter"); }), M("Bottom", delegate { canvas.Align("bottom"); }),
                new ToolStripSeparator(), M("Distribute horizontally", delegate { canvas.Distribute(true); }), M("Distribute vertically", delegate { canvas.Distribute(false); }) });
            var order = new ToolStripMenuItem("Order");
            order.DropDownItems.AddRange(new ToolStripItem[] { M("Bring to front", delegate { canvas.Order("front"); }), M("Bring forward", delegate { canvas.Order("forward"); }), M("Send backward", delegate { canvas.Order("backward"); }), M("Send to back", delegate { canvas.Order("back"); }) });
            return DD(Ic.Layers, "Arrange", "Arrange objects",
                M("Select all objects", "Ctrl+A", delegate { canvas.SelectAll(); }), M("Duplicate", "Ctrl+D", delegate { canvas.Duplicate(); }),
                M("Copy", "Ctrl+C", delegate { canvas.CopyObjects(); }), M("Paste", "Ctrl+V", delegate { canvas.Paste(); }), M("Delete", "Del", delegate { canvas.DeleteSelected(); }),
                new ToolStripSeparator(), order, align, M("Lock or unlock", delegate { canvas.ToggleLock(); RefreshLayers(); }));
        }
    }
}
