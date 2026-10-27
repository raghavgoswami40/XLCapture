// A document: a base bitmap plus editable annotation objects, with undo/redo and image operations.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace SnapLite
{
    class Doc
    {
        class State { public Bitmap Base; public Obj[] Objs; }

        public Bitmap Base;
        public List<Obj> Objs = new List<Obj>();
        public string Path;                 // file the capture was saved to (may be null)
        public string ProjectPath;          // .snapl project file, once saved
        public string Name = "Untitled";
        public List<string> Tags = new List<string>();
        public bool Favorite;
        public DateTime Created = DateTime.Now;
        public bool Dirty;

        readonly Stack<State> undo = new Stack<State>(), redo = new Stack<State>();
        public event EventHandler Changed;

        public Doc(Bitmap b) { Base = Effects.To32(b); }

        public int Width { get { return Base.Width; } }
        public int Height { get { return Base.Height; } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public void Raise() { Dirty = true; if (Changed != null) Changed(this, EventArgs.Empty); }

        // ---- history
        State Snap() { return new State { Base = Base, Objs = Objs.Select(o => o.Clone()).ToArray() }; }
        void Restore(State s) { Base = s.Base; Objs = s.Objs.Select(o => o.Clone()).ToList(); }
        // call before mutating anything
        public void Checkpoint()
        {
            undo.Push(Snap()); redo.Clear();
            while (undo.Count > 80) { var keep = undo.Reverse().Skip(1).Reverse().ToArray(); undo.Clear(); foreach (var s in keep.Reverse()) undo.Push(s); }
        }
        public void Undo() { if (undo.Count == 0) return; redo.Push(Snap()); Restore(undo.Pop()); Raise(); }
        public void Redo() { if (redo.Count == 0) return; undo.Push(Snap()); Restore(redo.Pop()); Raise(); }

        // ---- rendering
        public Bitmap Render() { return Render(true); }
        public Bitmap Render(bool withObjects)
        {
            var bmp = new Bitmap(Base.Width, Base.Height, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(Base, 0, 0, Base.Width, Base.Height);
                g.CompositingMode = CompositingMode.SourceOver;
                if (withObjects) foreach (var o in Objs) o.Render(g, Base);
            }
            return bmp;
        }
        public int NextStepNumber() { var s = Objs.OfType<StepObj>().Select(o => o.Number).DefaultIfEmpty(0).Max(); return s + 1; }

        // ---- object operations
        public void Add(Obj o) { Checkpoint(); Objs.Add(o); Raise(); }
        public void Remove(Obj o) { Checkpoint(); Objs.Remove(o); Raise(); }
        public void BringToFront(Obj o) { Checkpoint(); Objs.Remove(o); Objs.Add(o); Raise(); }
        public void SendToBack(Obj o) { Checkpoint(); Objs.Remove(o); Objs.Insert(0, o); Raise(); }
        public void Forward(Obj o) { int i = Objs.IndexOf(o); if (i < 0 || i == Objs.Count - 1) return; Checkpoint(); Objs.RemoveAt(i); Objs.Insert(i + 1, o); Raise(); }
        public void Backward(Obj o) { int i = Objs.IndexOf(o); if (i <= 0) return; Checkpoint(); Objs.RemoveAt(i); Objs.Insert(i - 1, o); Raise(); }

        // ---- base-image operations (objects follow the picture)
        void SetBase(Bitmap nb) { if (!ReferenceEquals(nb, Base)) Base = nb; }

        public void Crop(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, Base.Width, Base.Height)); if (r.Width < 2 || r.Height < 2) return;
            Checkpoint();
            SetBase(Effects.Crop(Base, r));
            foreach (var o in Objs) o.Translate(-r.X, -r.Y);
            Raise();
        }
        public void Resize(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h); Checkpoint();
            float sx = w / (float)Base.Width, sy = h / (float)Base.Height;
            SetBase(Effects.Resize(Base, w, h));
            foreach (var o in Objs) o.Scale(sx, sy);
            Raise();
        }
        public void CanvasSize(int w, int h, float ax, float ay, Color fill)
        {
            Checkpoint(); Point off;
            SetBase(Effects.CanvasSize(Base, w, h, ax, ay, fill, out off));
            foreach (var o in Objs) o.Translate(off.X, off.Y);
            Raise();
        }
        public void RotateCW() { Checkpoint(); float h = Base.Height; SetBase(Effects.RotateFlip(Base, RotateFlipType.Rotate90FlipNone)); foreach (var o in Objs) o.RotateCW(h); Raise(); }
        public void RotateCCW() { Checkpoint(); float w = Base.Width; SetBase(Effects.RotateFlip(Base, RotateFlipType.Rotate270FlipNone)); foreach (var o in Objs) o.RotateCCW(w); Raise(); }
        public void Rotate180() { Checkpoint(); float w = Base.Width, h = Base.Height; SetBase(Effects.RotateFlip(Base, RotateFlipType.Rotate180FlipNone)); foreach (var o in Objs) { o.FlipH(w); o.FlipV(h); } Raise(); }
        public void FlipH() { Checkpoint(); float w = Base.Width; SetBase(Effects.RotateFlip(Base, RotateFlipType.RotateNoneFlipX)); foreach (var o in Objs) o.FlipH(w); Raise(); }
        public void FlipV() { Checkpoint(); float h = Base.Height; SetBase(Effects.RotateFlip(Base, RotateFlipType.RotateNoneFlipY)); foreach (var o in Objs) o.FlipV(h); Raise(); }
        public void CutBand(bool horizontal, int start, int length)
        {
            if (length < 1) return; Checkpoint();
            SetBase(Effects.CutBand(Base, horizontal, start, length));
            foreach (var o in Objs)
            {
                var c = o.Center();
                float v = horizontal ? c.Y : c.X;
                if (v >= start + length) { if (horizontal) o.Translate(0, -length); else o.Translate(-length, 0); }
            }
            Raise();
        }
        // pixel-only effects: annotations stay where they are
        public void ApplyPixels(Func<Bitmap, Bitmap> op) { Checkpoint(); SetBase(op(Base)); Raise(); }
        // effects that grow the canvas (border, shadow, edges): shift annotations by the added margin
        public void ApplyGrowing(Func<Bitmap, Bitmap> op)
        {
            Checkpoint(); var nb = op(Base);
            float dx = (nb.Width - Base.Width) / 2f, dy = (nb.Height - Base.Height) / 2f;
            SetBase(nb);
            foreach (var o in Objs) o.Translate(dx, dy);
            Raise();
        }
        // burn all annotations into the picture
        public void Flatten() { Checkpoint(); var f = Render(); Base = f; Objs.Clear(); Raise(); }

        // Lift a rectangle of pixels into a movable image object; the hole is filled from its surroundings.
        public ImageObj Lift(Rectangle r, bool fillHole)
        {
            r.Intersect(new Rectangle(0, 0, Base.Width, Base.Height)); if (r.Width < 2 || r.Height < 2) return null;
            Checkpoint();
            var piece = Effects.Crop(Base, r);
            var nb = Effects.To32(Base);
            if (fillHole) Effects.InpaintRect(nb, r);
            Base = nb;
            var o = new ImageObj { Img = piece, A = r.Location, B = new PointF(r.Right, r.Bottom) };
            Objs.Add(o); Raise();
            return o;
        }
    }
}

