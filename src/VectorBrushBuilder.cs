using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum VectorBrushShape { Circle, Square, Diamond }

    /// <summary>Transient, incremental soft-coverage rasterizer; it owns no Unity objects.</summary>
    public sealed partial class VectorBrushBuilder
    {
        public const int MaxSamples = 65536;
        public int Width { get; private set; }
        public int Height { get; private set; }
        /// <summary>Borrow for raster preview. Do not modify or retain across Begin.</summary>
        public byte[] Coverage { get; private set; }
        public RectInt DirtyRect { get; private set; }
        public int SampleCount { get; private set; }
        public bool IsFull => SampleCount >= MaxSamples;
        public bool IsDrawing { get; private set; }
        public Rect Frame { get; private set; }
        public bool HasChanges { get; private set; }

        Vector2 previous;
        float previousRadius;
        VectorBrushShape shape;
        bool editing, erase, hasCoverage;
        Vector2 pixelStep;
        string shapeId;
        float tolerance, expansion;

        public void Begin(int width, int height, VectorBrushShape shape = VectorBrushShape.Circle)
        {
            VectorBrushDraft.CheckSize(width, height);
            Reset(shape);
            Width = width; Height = height;
            // Never reuse the previous stroke's storage: a layer may still own it.
            Coverage = new byte[width * height];
            DirtyRect = new RectInt(0, 0, width, height);
            Frame = new Rect(0, 0, 1, 1);
            pixelStep = new Vector2(1f / width, 1f / height);
        }

        void Reset(VectorBrushShape shape)
        {
            if (shape < VectorBrushShape.Circle || shape > VectorBrushShape.Diamond)
                throw new ArgumentOutOfRangeException(nameof(shape));
            this.shape = shape;
            SampleCount = 0; IsDrawing = true;
            HasChanges = hasCoverage = clipEnabled = false;
            editing = erase = false; shapeId = null;
            tolerance = 1; expansion = 0;
        }

        /// <summary>Keep only an empty grid cell until the first visible contribution.</summary>
        public void BeginEmpty(int gridWidth, int gridHeight, VectorBrushShape shape, float tolerance, bool erase = false)
        {
            VectorBrushDraft.CheckSize(gridWidth, gridHeight);
            if (!VectorBrushDraft.Finite(tolerance) || tolerance < VectorBrushDraft.MinTolerance || tolerance > VectorBrushDraft.MaxTolerance)
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            Reset(shape);
            editing = true; this.erase = erase; this.tolerance = tolerance;
            shapeId = Guid.NewGuid().ToString("N");
            Width = Height = 1; Coverage = new byte[1];
            pixelStep = new Vector2(1f / gridWidth, 1f / gridHeight);
            Frame = new Rect(Vector2.zero, pixelStep);
            DirtyRect = new RectInt(0, 0, 1, 1);
        }

        /// <summary>Edit a private copy of the source; growing storage never resamples existing pixels.</summary>
        public void Begin(VectorBrushDraft source, VectorBrushShape shape, bool erase)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.Validate();
            Reset(shape);
            editing = true; this.erase = erase;
            shapeId = source.shapeId; tolerance = source.tolerance; expansion = source.expansion;
            pixelStep = new Vector2(source.frame.width / source.width, source.frame.height / source.height);
            int left = source.width, bottom = source.height, right = -1, top = -1;
            for (int y = 0; y < source.height; y++) for (int x = 0; x < source.width; x++)
                if (source.coverage[y * source.width + x] != 0)
                { left = Math.Min(left, x); bottom = Math.Min(bottom, y); right = Math.Max(right, x); top = Math.Max(top, y); }
            hasCoverage = right >= left;
            if (!hasCoverage) { left = source.width / 2; bottom = source.height / 2; right = left; top = bottom; }
            Width = right - left + 1; Height = top - bottom + 1;
            Coverage = new byte[Width * Height];
            for (int y = 0; y < Height; y++) Array.Copy(source.coverage, (y + bottom) * source.width + left, Coverage, y * Width, Width);
            Frame = new Rect(source.frame.min + Vector2.Scale(new Vector2(left, bottom), pixelStep), Vector2.Scale(new Vector2(Width, Height), pixelStep));
            DirtyRect = new RectInt(0, 0, Width, Height);
        }

        public Vector2 ToPixel(Vector2 local) => new Vector2((local.x - Frame.xMin) * Width / Frame.width, (local.y - Frame.yMin) * Height / Frame.height);

        Vector2Int Grow(RectInt affected)
        {
            if (!editing || erase) return default;
            int left = hasCoverage ? Math.Min(0, affected.xMin) : affected.xMin;
            int bottom = hasCoverage ? Math.Min(0, affected.yMin) : affected.yMin;
            int right = hasCoverage ? Math.Max(Width, affected.xMax) : affected.xMax;
            int top = hasCoverage ? Math.Max(Height, affected.yMax) : affected.yMax;
            if (left == 0 && bottom == 0 && right == Width && top == Height) return default;
            int width = right - left, height = top - bottom;
            VectorBrushDraft.CheckSize(width, height);
            var next = new byte[width * height];
            if (hasCoverage)
                for (int y = 0; y < Height; y++) Array.Copy(Coverage, y * Width, next, (y - bottom) * width - left, Width);
            var shift = new Vector2Int(left, bottom);
            Frame = new Rect(Frame.min + Vector2.Scale(shift, pixelStep), Vector2.Scale(new Vector2(width, height), pixelStep));
            Width = width; Height = height; Coverage = next;
            previous -= (Vector2)shift;
            ShiftCanvasClip(shift);
            DirtyRect = new RectInt(0, 0, width, height);
            return shift;
        }

        public RectInt ConsumeDirtyRect()
        {
            RectInt result = DirtyRect;
            DirtyRect = default;
            return result;
        }

        /// <summary>Positions/radii are mask pixels, y up; returns whether coverage changed.</summary>
        public bool AddPoint(Vector2 pixel, float radius)
        {
            if (!IsDrawing) throw new InvalidOperationException("Begin a vector brush stroke first.");
            if (!VectorBrushDraft.Finite(pixel.x) || !VectorBrushDraft.Finite(pixel.y)
                || Mathf.Abs(pixel.x) > 1000000 || Mathf.Abs(pixel.y) > 1000000
                || !VectorBrushDraft.Finite(radius) || radius < 0 || radius > VectorBrushDraft.MaxDimension)
                throw new ArgumentOutOfRangeException(nameof(pixel), "Brush coordinates must be finite and bounded; radius must be 0 to 1024 mask pixels.");
            if (IsFull) return false; // UI can expose IsFull; no unbounded sample history is retained.
            Vector2 start = SampleCount == 0 ? pixel : previous;
            float startRadius = SampleCount == 0 ? radius : previousRadius;
            previous = pixel; previousRadius = radius; SampleCount++;
            if (startRadius == 0 && radius == 0) return false;
            // Keep the real pointer history, even outside the canvas. Clip the swept
            // footprint before allocating, not its endpoints (which would bend the stroke).
            if (!RasterBounds(start, pixel, startRadius, radius, out RectInt affected)) return false;
            Vector2Int shift = Grow(affected);
            affected.position -= shift;
            bool changed = Rasterize(start - (Vector2)shift, pixel - (Vector2)shift, startRadius, radius, affected);
            HasChanges |= changed;
            if (changed && !erase) hasCoverage = true;
            return changed;
        }

        public VectorBrushDraft Finish()
        {
            if (!IsDrawing) throw new InvalidOperationException("No active vector brush stroke.");
            IsDrawing = false;
            if (editing) TrimStorage();
            var result = new VectorBrushDraft { width = Width, height = Height, coverage = Coverage, frame = Frame };
            if (editing) { result.shapeId = shapeId; result.tolerance = tolerance; result.expansion = expansion; }
            return result;
        }

        bool Rasterize(Vector2 a, Vector2 b, float ra, float rb, RectInt affected)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude, extent = Mathf.Max(ra, rb) + .5f;
            Vector2 direction = length > 0 ? delta / length : Vector2.zero;
            float slope = length > 0 ? (rb - ra) / length : 0;
            float slopeFactor = Mathf.Abs(slope) < 1 ? slope / Mathf.Sqrt(1 - slope * slope) : 0;
            Span<Vector2> hull = stackalloc Vector2[16];
            int hullCount = shape == VectorBrushShape.Circle ? 0 : BuildTipHull(a, b, ra, rb, hull);
            int lowY = affected.yMin, highY = affected.yMax - 1;
            bool clipPixels = !CanvasContains(affected);
            int changedX = Width, changedY = Height, changedRight = -1, changedTop = -1;
            for (int y = lowY; y <= highY; y++)
            {
                // Restrict each row to the swept disc envelope, including long diagonal moves.
                float t0 = 0, t1 = 1, row = y + .5f;
                if (Mathf.Abs(delta.y) > 1e-6f)
                {
                    float first = (row - extent - a.y) / delta.y;
                    float last = (row + extent - a.y) / delta.y;
                    t0 = Mathf.Clamp01(Mathf.Min(first, last));
                    t1 = Mathf.Clamp01(Mathf.Max(first, last));
                }
                float x0 = a.x + delta.x * t0, x1 = a.x + delta.x * t1;
                int lowX = Mathf.Max(affected.xMin, Mathf.FloorToInt(Mathf.Min(x0, x1) - extent));
                int highX = Mathf.Min(affected.xMax - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + extent));
                if (clipPixels) ClipRow(y, ref lowX, ref highX);
                for (int x = lowX; x <= highX; x++)
                {
                    byte canvasCoverage = clipPixels ? CanvasCoverage(x, y) : (byte)255;
                    if (canvasCoverage == 0) continue;
                    Vector2 point = new Vector2(x + .5f, row);
                    float distance;
                    if (hullCount > 0)
                    {
                        distance = PolygonDistance(point, hull.Slice(0, hullCount));
                        // Inside, use the best tip distance along the sweep. Unlike distance to
                        // the hull's artificial caps, this composes identically across samples.
                        if (distance < 0) distance = TipInteriorDistance(point - a, delta, ra, rb);
                    }
                    else if (length <= 1e-6f || Mathf.Abs(slope) >= 1)
                        distance = Mathf.Min((point - a).magnitude - ra, (point - b).magnitude - rb);
                    else
                    {
                        Vector2 offset = point - a;
                        float along = Vector2.Dot(offset, direction);
                        float across = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
                        float travel = Mathf.Clamp(along + across * slopeFactor, 0, length);
                        distance = (point - (a + direction * travel)).magnitude - (ra + slope * travel);
                    }
                    byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(.5f - distance) * 255);
                    value = Math.Min(value, canvasCoverage);
                    int index = y * Width + x;
                    value = erase ? (byte)Math.Min(Coverage[index], 255 - value) : Math.Max(Coverage[index], value);
                    if (value == Coverage[index]) continue;
                    Coverage[index] = value;
                    changedX = Mathf.Min(changedX, x); changedRight = Mathf.Max(changedRight, x);
                    changedY = Mathf.Min(changedY, y); changedTop = Mathf.Max(changedTop, y);
                }
            }
            if (changedRight < changedX) return false;
            RectInt changed = new RectInt(changedX, changedY, changedRight - changedX + 1, changedTop - changedY + 1);
            if (DirtyRect.width == 0) DirtyRect = changed;
            else DirtyRect = new RectInt(Mathf.Min(DirtyRect.xMin, changed.xMin), Mathf.Min(DirtyRect.yMin, changed.yMin),
                Mathf.Max(DirtyRect.xMax, changed.xMax) - Mathf.Min(DirtyRect.xMin, changed.xMin),
                Mathf.Max(DirtyRect.yMax, changed.yMax) - Mathf.Min(DirtyRect.yMin, changed.yMin));
            return true;
        }

        void TrimStorage()
        {
            int left = Width, bottom = Height, right = -1, top = -1;
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                if (Coverage[y * Width + x] != 0)
                { left = Math.Min(left, x); bottom = Math.Min(bottom, y); right = Math.Max(right, x); top = Math.Max(top, y); }
            if (right < left) left = bottom = right = top = 0;
            int width = right - left + 1, height = top - bottom + 1;
            if (width == Width && height == Height) return;
            var trimmed = new byte[width * height];
            for (int y = 0; y < height; y++) Array.Copy(Coverage, (y + bottom) * Width + left, trimmed, y * width, width);
            Frame = new Rect(Frame.min + Vector2.Scale(new Vector2(left, bottom), pixelStep), Vector2.Scale(new Vector2(width, height), pixelStep));
            Width = width; Height = height; Coverage = trimmed;
            DirtyRect = new RectInt(0, 0, width, height);
        }

        // The convex hull of the endpoint tips is their exact linear sweep, including size changes.
        int BuildTipHull(Vector2 a, Vector2 b, float ra, float rb, Span<Vector2> hull)
        {
            Span<Vector2> points = stackalloc Vector2[8];
            for (int end = 0; end < 2; end++)
            {
                Vector2 p = end == 0 ? a : b;
                float r = end == 0 ? ra : rb;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 offset = shape == VectorBrushShape.Square
                        ? new Vector2(i == 0 || i == 3 ? -r : r, i < 2 ? -r : r)
                        : i == 0 ? new Vector2(-r, 0) : i == 1 ? new Vector2(0, -r)
                        : i == 2 ? new Vector2(r, 0) : new Vector2(0, r);
                    points[end * 4 + i] = p + offset;
                }
            }
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 p = points[i]; int j = i;
                while (j > 0 && (points[j - 1].x > p.x || points[j - 1].x == p.x && points[j - 1].y > p.y))
                { points[j] = points[j - 1]; j--; }
                points[j] = p;
            }
            int count = 0;
            for (int i = 0; i < points.Length; i++)
            {
                while (count >= 2 && Cross(hull[count - 1] - hull[count - 2], points[i] - hull[count - 1]) <= 0) count--;
                hull[count++] = points[i];
            }
            int lower = count + 1;
            for (int i = points.Length - 2; i >= 0; i--)
            {
                while (count >= lower && Cross(hull[count - 1] - hull[count - 2], points[i] - hull[count - 1]) <= 0) count--;
                hull[count++] = points[i];
            }
            return count - 1;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        float TipInteriorDistance(Vector2 offset, Vector2 delta, float ra, float rb)
        {
            if (shape == VectorBrushShape.Diamond)
            {
                const float diagonal = .7071067811865475f;
                offset = new Vector2(offset.x + offset.y, offset.y - offset.x) * diagonal;
                delta = new Vector2(delta.x + delta.y, delta.y - delta.x) * diagonal;
                ra *= diagonal; rb *= diagonal;
            }
            float dr = rb - ra;
            Span<float> starts = stackalloc float[4] { offset.x - ra, -offset.x - ra, offset.y - ra, -offset.y - ra };
            Span<float> slopes = stackalloc float[4] { -delta.x - dr, delta.x - dr, -delta.y - dr, delta.y - dr };
            float best = Mathf.Min(TipDistanceAt(starts, slopes, 0), TipDistanceAt(starts, slopes, 1));
            // The minimum of the convex piecewise-linear interior distance is at an
            // endpoint or an intersection of two supporting planes.
            for (int i = 0; i < 4 && best > -.5f; i++) for (int j = i + 1; j < 4 && best > -.5f; j++)
            {
                float denominator = slopes[i] - slopes[j];
                if (Mathf.Abs(denominator) < 1e-12f) continue;
                float t = (starts[j] - starts[i]) / denominator;
                if (t > 0 && t < 1) best = Mathf.Min(best, TipDistanceAt(starts, slopes, t));
            }
            return best;
        }

        static float TipDistanceAt(ReadOnlySpan<float> starts, ReadOnlySpan<float> slopes, float t)
            => Mathf.Max(Mathf.Max(starts[0] + slopes[0] * t, starts[1] + slopes[1] * t),
                Mathf.Max(starts[2] + slopes[2] * t, starts[3] + slopes[3] * t));

        static float PolygonDistance(Vector2 point, ReadOnlySpan<Vector2> hull)
        {
            float squared = float.MaxValue; bool inside = true;
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 a = hull[i], edge = hull[(i + 1) % hull.Length] - a, offset = point - a;
                inside &= Cross(edge, offset) >= 0;
                float t = edge.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(offset, edge) / edge.sqrMagnitude) : 0;
                squared = Mathf.Min(squared, (offset - edge * t).sqrMagnitude);
            }
            return Mathf.Sqrt(squared) * (inside ? -1 : 1);
        }
    }
}
