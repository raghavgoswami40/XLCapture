// One vector icon set, drawn in code: 24-unit grid, single stroke weight, round caps.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SnapLite
{
    enum Ic
    {
        Select, Arrow, Line, Shape, Callout, Text, Stamp, Step, Pen, Marker, Fill, Magnify, Spotlight, Blur,
        Crop, Cut, SelectRect, Move, Undo, Redo, Copy, Save, Open, Capture, Library, Share, ZoomIn, ZoomOut, Fit,
        Settings, Trash, Record, Image, Effects, Ocr, Check, Close, Plus, Folder, Print, Stop, Pause, Clock, Star,
        Heart, Question, Exclaim, Info, Warning, Bug, Flag, Pin, Bulb, Cursor, Eraser, Wand, Lasso, Order, Layers,
        Camera, Video, Gear, Reset, EditBox, Chevron, Mic, Speaker, Webcam, Draw, AllInOne, Tag, Recent, Create, Dropper, Grid, Session, Bot, Prev
    }

    static class Icons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(Ic k, int size, Color c)
        {
            string key = k + "|" + size + "|" + c.ToArgb();
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            b = new Bitmap(size, size);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Draw(g, k, new RectangleF(0, 0, size, size), c);
            }
            cache[key] = b;
            return b;
        }

        public static void Draw(Graphics g, Ic k, RectangleF box, Color c)
        {
            var state = g.Save();
            float s = Math.Min(box.Width, box.Height) / 24f;
            g.TranslateTransform(box.X + (box.Width - 24 * s) / 2, box.Y + (box.Height - 24 * s) / 2);
            g.ScaleTransform(s, s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(c, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var f = new SolidBrush(c))
                Glyph(g, k, p, f, c);
            g.Restore(state);
        }

        static void L(Graphics g, Pen p, float x1, float y1, float x2, float y2) { g.DrawLine(p, x1, y1, x2, y2); }
        static void Poly(Graphics g, Pen p, params float[] v)
        {
            var pts = new PointF[v.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(v[2 * i], v[2 * i + 1]);
            g.DrawPolygon(p, pts);
        }
        static void Lines(Graphics g, Pen p, params float[] v)
        {
            var pts = new PointF[v.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(v[2 * i], v[2 * i + 1]);
            g.DrawLines(p, pts);
        }
        static void FillPoly(Graphics g, Brush b, params float[] v)
        {
            var pts = new PointF[v.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(v[2 * i], v[2 * i + 1]);
            g.FillPolygon(b, pts);
        }

        static void Glyph(Graphics g, Ic k, Pen p, Brush f, Color c)
        {
            switch (k)
            {
                case Ic.Select: Poly(g, p, 6, 4, 6, 18, 10, 14.5f, 13, 20, 15.5f, 19, 12.5f, 13, 18, 13); break;
                case Ic.Cursor: FillPoly(g, f, 6, 4, 6, 18, 10, 14.5f, 13, 20, 15.5f, 19, 12.5f, 13, 18, 13); break;
                case Ic.Arrow: L(g, p, 5, 19, 19, 5); Lines(g, p, 10, 5, 19, 5, 19, 14); break;
                case Ic.Line: L(g, p, 5, 19, 19, 5); g.DrawEllipse(p, 3.5f, 17.5f, 3, 3); g.DrawEllipse(p, 17.5f, 3.5f, 3, 3); break;
                case Ic.Shape: g.DrawRectangle(p, 4, 8, 11, 11); g.DrawEllipse(p, 10, 4, 10, 10); break;
                case Ic.Callout:
                    using (var path = Theme.RoundRect(new RectangleF(3.5f, 4.5f, 17, 11), 2.5f)) g.DrawPath(p, path);
                    Lines(g, p, 8, 15.5f, 7, 20, 12.5f, 15.5f); break;
                case Ic.Text: L(g, p, 6, 6, 18, 6); L(g, p, 12, 6, 12, 19); L(g, p, 9.5f, 19, 14.5f, 19); break;
                case Ic.Stamp: g.DrawEllipse(p, 4, 4, 16, 16); Poly(g, p, 12, 7.5f, 13.4f, 10.7f, 16.8f, 11, 14.2f, 13.3f, 15, 16.6f, 12, 14.8f, 9, 16.6f, 9.8f, 13.3f, 7.2f, 11, 10.6f, 10.7f); break;
                case Ic.Step:
                    g.DrawEllipse(p, 3.5f, 3.5f, 17, 17);
                    Lines(g, p, 10, 9.5f, 12.2f, 8, 12.2f, 16); break;
                case Ic.Pen: Poly(g, p, 4, 20, 5, 15, 15, 5, 19, 9, 9, 19); L(g, p, 13, 7, 17, 11); break;
                case Ic.Marker: Poly(g, p, 5, 19, 6, 15, 15, 6, 18, 9, 9, 18); g.FillRectangle(f, 4, 20, 16, 1.6f); break;
                case Ic.Fill: Poly(g, p, 5, 12, 11, 6, 17, 12, 11, 18); L(g, p, 11, 6, 9, 3.5f); g.FillEllipse(f, 17.5f, 15, 3.5f, 3.5f); break;
                case Ic.Magnify: g.DrawEllipse(p, 4, 4, 11, 11); L(g, p, 13, 13, 19.5f, 19.5f); L(g, p, 9.5f, 7.5f, 9.5f, 11.5f); L(g, p, 7.5f, 9.5f, 11.5f, 9.5f); break;
                case Ic.Spotlight: g.DrawRectangle(p, 3.5f, 5, 17, 14); g.FillEllipse(f, 8, 8.5f, 8, 8); break;
                case Ic.Blur:
                    for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                        if ((x + y) % 2 == 0) g.FillRectangle(f, 4.5f + x * 5.2f, 4.5f + y * 5.2f, 4.2f, 4.2f);
                    break;
                case Ic.Crop: L(g, p, 7, 3, 7, 17); L(g, p, 7, 17, 21, 17); L(g, p, 3, 7, 17, 7); L(g, p, 17, 7, 17, 21); break;
                case Ic.Cut: g.DrawEllipse(p, 4, 14.5f, 4.5f, 4.5f); g.DrawEllipse(p, 15.5f, 14.5f, 4.5f, 4.5f); L(g, p, 7, 14, 17, 4); L(g, p, 17, 14, 7, 4); break;
                case Ic.SelectRect: p.DashStyle = DashStyle.Dash; g.DrawRectangle(p, 4.5f, 5.5f, 15, 13); break;
                case Ic.Lasso: p.DashStyle = DashStyle.Dash; g.DrawEllipse(p, 3.5f, 5, 17, 11); p.DashStyle = DashStyle.Solid; Lines(g, p, 9, 16, 8, 20, 11, 21); break;
                case Ic.Wand: L(g, p, 4.5f, 19.5f, 15, 9); L(g, p, 16, 3.5f, 16, 7); L(g, p, 14, 5.5f, 18, 5.5f); L(g, p, 19, 10, 19, 13); L(g, p, 17.5f, 11.5f, 20.5f, 11.5f); break;
                case Ic.Move: L(g, p, 12, 3, 12, 21); L(g, p, 3, 12, 21, 12); Lines(g, p, 9.5f, 5.5f, 12, 3, 14.5f, 5.5f); Lines(g, p, 9.5f, 18.5f, 12, 21, 14.5f, 18.5f); Lines(g, p, 5.5f, 9.5f, 3, 12, 5.5f, 14.5f); Lines(g, p, 18.5f, 9.5f, 21, 12, 18.5f, 14.5f); break;
                case Ic.Undo: Lines(g, p, 8, 6, 4, 10, 8, 14); g.DrawArc(p, 4, 6, 16, 12, 270, 160); L(g, p, 4, 10, 13, 10); break;
                case Ic.Redo: Lines(g, p, 16, 6, 20, 10, 16, 14); g.DrawArc(p, 4, 6, 16, 12, 110, 160); L(g, p, 20, 10, 11, 10); break;
                case Ic.Copy: g.DrawRectangle(p, 8.5f, 8.5f, 11, 11); Lines(g, p, 15.5f, 8.5f, 15.5f, 4.5f, 4.5f, 4.5f, 4.5f, 15.5f, 8.5f, 15.5f); break;
                case Ic.Save: Poly(g, p, 4.5f, 4.5f, 17, 4.5f, 19.5f, 7, 19.5f, 19.5f, 4.5f, 19.5f); g.DrawRectangle(p, 8, 4.5f, 7, 5); g.DrawRectangle(p, 7.5f, 13, 9, 6.5f); break;
                case Ic.Open: Lines(g, p, 3.5f, 18.5f, 3.5f, 6.5f, 9, 6.5f, 11, 9, 19.5f, 9, 19.5f, 12); Poly(g, p, 3.5f, 18.5f, 6.5f, 12, 21, 12, 18, 18.5f); break;
                case Ic.Capture:
                    Lines(g, p, 3.5f, 8, 3.5f, 3.5f, 8, 3.5f); Lines(g, p, 16, 3.5f, 20.5f, 3.5f, 20.5f, 8);
                    Lines(g, p, 3.5f, 16, 3.5f, 20.5f, 8, 20.5f); Lines(g, p, 16, 20.5f, 20.5f, 20.5f, 20.5f, 16);
                    g.FillEllipse(f, 9.5f, 9.5f, 5, 5); break;
                case Ic.Library: for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) g.DrawRectangle(p, 4 + x * 8.5f, 4 + y * 8.5f, 7, 7); g.DrawRectangle(p, 4, 19.5f, 15.5f, 0.1f); break;
                case Ic.Share: g.DrawEllipse(p, 4, 9.5f, 4, 4); g.DrawEllipse(p, 16, 4, 4, 4); g.DrawEllipse(p, 16, 16, 4, 4); L(g, p, 7.7f, 10.8f, 16.3f, 6.4f); L(g, p, 7.7f, 13, 16.3f, 17.4f); break;
                case Ic.ZoomIn: g.DrawEllipse(p, 4, 4, 11, 11); L(g, p, 13, 13, 19.5f, 19.5f); L(g, p, 7, 9.5f, 12, 9.5f); L(g, p, 9.5f, 7, 9.5f, 12); break;
                case Ic.ZoomOut: g.DrawEllipse(p, 4, 4, 11, 11); L(g, p, 13, 13, 19.5f, 19.5f); L(g, p, 7, 9.5f, 12, 9.5f); break;
                case Ic.Fit: Lines(g, p, 4, 9, 4, 4, 9, 4); Lines(g, p, 15, 4, 20, 4, 20, 9); Lines(g, p, 4, 15, 4, 20, 9, 20); Lines(g, p, 15, 20, 20, 20, 20, 15); break;
                case Ic.Settings:
                    L(g, p, 4, 7, 20, 7); L(g, p, 4, 17, 20, 17); g.FillEllipse(f, 14, 4.5f, 5, 5); g.FillEllipse(f, 5, 14.5f, 5, 5); break;
                case Ic.Trash: L(g, p, 4.5f, 7, 19.5f, 7); Lines(g, p, 9, 7, 9, 4.5f, 15, 4.5f, 15, 7); Poly(g, p, 6.5f, 7, 7.5f, 20, 16.5f, 20, 17.5f, 7); break;
                case Ic.Record: g.FillEllipse(new SolidBrush(Theme.Mark), 6, 6, 12, 12); g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); break;
                case Ic.Stop: g.FillRectangle(f, 6.5f, 6.5f, 11, 11); break;
                case Ic.Pause: g.FillRectangle(f, 7, 6, 3.5f, 12); g.FillRectangle(f, 13.5f, 6, 3.5f, 12); break;
                case Ic.Image: g.DrawRectangle(p, 3.5f, 5, 17, 14); g.DrawEllipse(p, 7, 8, 3, 3); Lines(g, p, 4, 18, 10, 13, 14, 16, 17, 13, 20, 16); break;
                case Ic.Effects: Poly(g, p, 12, 3, 14, 10, 21, 12, 14, 14, 12, 21, 10, 14, 3, 12, 10, 10); break;
                case Ic.Ocr: Lines(g, p, 3.5f, 8, 3.5f, 3.5f, 8, 3.5f); Lines(g, p, 16, 3.5f, 20.5f, 3.5f, 20.5f, 8); Lines(g, p, 3.5f, 16, 3.5f, 20.5f, 8, 20.5f); Lines(g, p, 16, 20.5f, 20.5f, 20.5f, 20.5f, 16); L(g, p, 8, 9, 16, 9); L(g, p, 12, 9, 12, 16); break;
                case Ic.Check: Lines(g, p, 5, 12.5f, 10, 17.5f, 19, 7); break;
                case Ic.Close: L(g, p, 6, 6, 18, 18); L(g, p, 18, 6, 6, 18); break;
                case Ic.Plus: L(g, p, 12, 5, 12, 19); L(g, p, 5, 12, 19, 12); break;
                case Ic.Folder: Poly(g, p, 3.5f, 18.5f, 3.5f, 5.5f, 9, 5.5f, 11, 8, 20.5f, 8, 20.5f, 18.5f); break;
                case Ic.Print: g.DrawRectangle(p, 4, 9, 16, 8); g.DrawRectangle(p, 7.5f, 4, 9, 5); g.DrawRectangle(p, 7.5f, 14, 9, 6); break;
                case Ic.Clock: g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); Lines(g, p, 12, 7, 12, 12, 15.5f, 14); break;
                case Ic.Star: Poly(g, p, 12, 3.5f, 14.7f, 9.2f, 21, 10, 16.3f, 14.2f, 17.6f, 20.5f, 12, 17.4f, 6.4f, 20.5f, 7.7f, 14.2f, 3, 10, 9.3f, 9.2f); break;
                case Ic.Heart: using (var gp = new GraphicsPath()) { gp.AddBezier(12, 20, 2, 12, 5, 4, 12, 9.5f); gp.AddBezier(12, 9.5f, 19, 4, 22, 12, 12, 20); g.DrawPath(p, gp); } break;
                case Ic.Question: g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); g.DrawArc(p, 9, 7.5f, 6, 5.5f, 180, 200); L(g, p, 12, 13, 12, 14.5f); g.FillEllipse(f, 11.2f, 16.2f, 1.7f, 1.7f); break;
                case Ic.Exclaim: g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); L(g, p, 12, 7.5f, 12, 13.5f); g.FillEllipse(f, 11.1f, 15.8f, 1.9f, 1.9f); break;
                case Ic.Info: g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); L(g, p, 12, 11, 12, 16.5f); g.FillEllipse(f, 11.1f, 6.8f, 1.9f, 1.9f); break;
                case Ic.Warning: Poly(g, p, 12, 4, 21, 19.5f, 3, 19.5f); L(g, p, 12, 10, 12, 14.5f); g.FillEllipse(f, 11.1f, 16, 1.9f, 1.9f); break;
                case Ic.Bug: g.DrawEllipse(p, 8, 7, 8, 12); L(g, p, 12, 7, 12, 19); L(g, p, 8, 11, 4, 9.5f); L(g, p, 16, 11, 20, 9.5f); L(g, p, 8, 15, 4, 16.5f); L(g, p, 16, 15, 20, 16.5f); Lines(g, p, 10, 7, 9, 4.5f); Lines(g, p, 14, 7, 15, 4.5f); break;
                case Ic.Flag: L(g, p, 6, 4, 6, 21); Poly(g, p, 6, 5, 18.5f, 5, 15.5f, 9, 18.5f, 13, 6, 13); break;
                case Ic.Pin: g.DrawEllipse(p, 7.5f, 3.5f, 9, 9); L(g, p, 12, 12.5f, 12, 21); g.FillEllipse(f, 10.5f, 6.5f, 3, 3); break;
                case Ic.Bulb: g.DrawEllipse(p, 6.5f, 3.5f, 11, 11); L(g, p, 9.5f, 17.5f, 14.5f, 17.5f); L(g, p, 10.5f, 20.5f, 13.5f, 20.5f); L(g, p, 9.5f, 14.5f, 9.5f, 17.5f); L(g, p, 14.5f, 14.5f, 14.5f, 17.5f); break;
                case Ic.Eraser: Poly(g, p, 4, 15, 12, 5, 20, 11, 12, 20, 8, 20); L(g, p, 8, 11, 16, 17); break;
                case Ic.Order: g.DrawRectangle(p, 4, 4, 10, 10); g.FillRectangle(f, 10, 10, 10, 10); break;
                case Ic.Layers: Poly(g, p, 12, 4, 21, 9, 12, 14, 3, 9); Lines(g, p, 3, 13, 12, 18, 21, 13); break;
                case Ic.Camera:
                    using (var gp = Theme.RoundRect(new RectangleF(3, 7, 18, 13), 2.5f)) g.DrawPath(p, gp);
                    Lines(g, p, 8.5f, 7, 10, 4.5f, 14, 4.5f, 15.5f, 7); g.DrawEllipse(p, 8.5f, 9.5f, 7, 7); break;
                case Ic.Video:
                    using (var gp = Theme.RoundRect(new RectangleF(3, 7, 13, 11), 2.5f)) g.DrawPath(p, gp);
                    Poly(g, p, 16, 11, 21, 8, 21, 17, 16, 14); break;
                case Ic.Gear:
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; L(g, p, 12 + (float)Math.Cos(a) * 6.5f, 12 + (float)Math.Sin(a) * 6.5f, 12 + (float)Math.Cos(a) * 9f, 12 + (float)Math.Sin(a) * 9f); }
                    g.DrawEllipse(p, 6, 6, 12, 12); g.DrawEllipse(p, 9.5f, 9.5f, 5, 5); break;
                case Ic.Reset:      // an open ring that turns back on itself, with an L-shaped arrowhead at the top left, a little heavier than the other icons
                    using (var pw = new Pen(c, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawArc(pw, 4.2f, 4.2f, 15.6f, 15.6f, 222f, 286f);
                        g.DrawLines(pw, new[] { new PointF(5f, 2.9f), new PointF(5f, 8.3f), new PointF(10.4f, 8.3f) });
                    }
                    break;
                case Ic.EditBox: Lines(g, p, 12, 4.5f, 4.5f, 4.5f, 4.5f, 19.5f, 19.5f, 19.5f, 19.5f, 12); Poly(g, p, 10, 14, 10.5f, 11, 18, 3.5f, 20.5f, 6, 13, 13.5f); break;
                case Ic.Chevron: Lines(g, p, 9, 5, 16, 12, 9, 19); break;
                case Ic.Prev: Lines(g, p, 15, 5, 8, 12, 15, 19); break;
                case Ic.Mic: using (var gp = Theme.RoundRect(new RectangleF(9, 3.5f, 6, 11), 3)) g.DrawPath(p, gp); g.DrawArc(p, 5.5f, 6, 13, 12, 0, 180); L(g, p, 12, 18, 12, 21); break;
                case Ic.Speaker: Poly(g, p, 4, 9, 8, 9, 13, 5, 13, 19, 8, 15, 4, 15); g.DrawArc(p, 11, 8, 6, 8, -60, 120); g.DrawArc(p, 11, 5, 10, 14, -60, 120); break;
                case Ic.Webcam: g.DrawEllipse(p, 6, 3.5f, 12, 12); g.DrawEllipse(p, 10, 7.5f, 4, 4); Lines(g, p, 8, 15, 6, 20.5f, 18, 20.5f, 16, 15); break;
                case Ic.Draw: Lines(g, p, 3.5f, 17, 8, 12, 11, 15, 16, 8, 20.5f, 12); g.FillEllipse(f, 18.5f, 4, 3.5f, 3.5f); break;
                case Ic.AllInOne: g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); g.FillEllipse(f, 7.5f, 7.5f, 9, 9); break;
                case Ic.Tag: Poly(g, p, 4, 4, 12, 4, 20.5f, 12.5f, 12.5f, 20.5f, 4, 12); g.FillEllipse(f, 7, 7, 3, 3); break;
                case Ic.Recent: g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); Lines(g, p, 12, 7, 12, 12, 8.5f, 14); break;
                case Ic.Create: g.DrawEllipse(p, 3.5f, 3.5f, 17, 17); L(g, p, 12, 7.5f, 12, 16.5f); L(g, p, 7.5f, 12, 16.5f, 12); break;
                case Ic.Dropper: Poly(g, p, 4.5f, 19.5f, 5, 16, 13, 8, 16, 11, 8, 19); L(g, p, 13, 8, 15.5f, 5.5f); g.FillEllipse(f, 14, 3, 7, 7); break;
                case Ic.Session: Poly(g, p, 4, 3.5f, 4, 15, 7, 12.2f, 9.3f, 17, 11.3f, 16.2f, 9.1f, 11.4f, 13, 11.4f); g.DrawEllipse(p, 13.5f, 13.5f, 8, 8); L(g, p, 17.5f, 15.5f, 17.5f, 19.5f); break;
                case Ic.Bot: Poly(g, p, 4, 3.5f, 4, 15, 7, 12.2f, 9.3f, 17, 11.3f, 16.2f, 9.1f, 11.4f, 13, 11.4f); g.DrawArc(p, 13, 13, 8.5f, 8.5f, 20, 280); Lines(g, p, 18.37f, 11.42f, 19.4f, 13.6f, 17.0f, 13.82f); break;
                case Ic.Grid: for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) g.FillRectangle(f, 4 + x * 6, 4 + y * 6, 4, 4); break;
            }
        }
    }
}
