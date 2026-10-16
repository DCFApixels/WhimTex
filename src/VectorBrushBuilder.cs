using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>Transient, incremental soft-coverage rasterizer; it owns no Unity objects.</summary>
    public sealed class VectorBrushBuilder
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

        Vector2 previous;
        float previousRadius;

        public void Begin(int width, int height)
        {
            VectorBrushDraft.CheckSize(width, height);
            Width = width; Height = height;
            // Never reuse the previous stroke's storage: a layer may still own it.
            Coverage = new byte[width * height];
            DirtyRect = new RectInt(0, 0, width, height);
            SampleCount = 0; IsDrawing = true;
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
            return Rasterize(start, pixel, startRadius, radius);
        }

        public VectorBrushDraft Finish()
        {
            if (!IsDrawing) throw new InvalidOperationException("No active vector brush stroke.");
            IsDrawing = false;
            return new VectorBrushDraft { width = Width, height = Height, coverage = Coverage };
        }

        bool Rasterize(Vector2 a, Vector2 b, float ra, float rb)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude, extent = Mathf.Max(ra, rb) + .5f;
            Vector2 direction = length > 0 ? delta / length : Vector2.zero;
            float slope = length > 0 ? (rb - ra) / length : 0;
            float slopeFactor = Mathf.Abs(slope) < 1 ? slope / Mathf.Sqrt(1 - slope * slope) : 0;
            int lowY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - extent));
            int highY = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + extent));
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
                int lowX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - extent));
                int highX = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + extent));
                for (int x = lowX; x <= highX; x++)
                {
                    Vector2 point = new Vector2(x + .5f, row);
                    float distance;
                    if (length <= 1e-6f || Mathf.Abs(slope) >= 1)
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
                    int index = y * Width + x;
                    if (value <= Coverage[index]) continue;
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
    }
}
