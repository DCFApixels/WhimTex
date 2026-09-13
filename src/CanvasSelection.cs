using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    internal enum SelectionCombine { Replace, Add, Subtract, Intersect }

    // Window-local, canvas-space coverage. No source pixels or document settings live here.
    internal sealed class CanvasSelection
    {
        internal const int MaximumPixels = 16777216;
        internal readonly int Width, Height;
        internal byte[] Coverage { get; private set; }
        internal RectInt Bounds { get; private set; }
        internal bool Active => Coverage != null;
        internal int Revision { get; private set; }

        internal CanvasSelection(int width, int height) { Width = width; Height = height; }
        internal void ValidateSize()
        {
            if (Width < 1 || Height < 1 || (long)Width * Height > MaximumPixels)
                throw new InvalidOperationException("Area selections support up to 16,777,216 canvas pixels.");
        }
        internal void Clear() { Coverage = null; Bounds = default; Revision++; }
        internal void All()
        {
            ValidateSize();
            var values = new byte[Width * Height];
            Array.Fill(values, (byte)255);
            Set(values, SelectionCombine.Replace);
        }
        internal void Invert()
        {
            ValidateSize();
            var values = new byte[Width * Height];
            for (int i = 0; i < values.Length; i++) values[i] = (byte)(255 - (Coverage == null ? 255 : Coverage[i]));
            Set(values, SelectionCombine.Replace);
        }
        internal void Set(byte[] values, SelectionCombine combine)
        {
            ValidateSize();
            if (values == null || values.Length != Width * Height) throw new ArgumentException("Invalid selection dimensions.");
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0, i = 0; y < Height; y++)
            for (int x = 0; x < Width; x++, i++)
            {
                int a = Coverage == null ? (combine == SelectionCombine.Add ? 0 : 255) : Coverage[i];
                int b = values[i];
                if (combine == SelectionCombine.Add) b = Math.Max(a, b);
                else if (combine == SelectionCombine.Subtract) b = (a * (255 - b) + 127) / 255;
                else if (combine == SelectionCombine.Intersect) b = (a * b + 127) / 255;
                values[i] = (byte)b;
                if (b == 0) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            Coverage = values;
            Bounds = maxX < 0 ? default : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            Revision++;
        }
        internal float Sample(Vector2 uv, bool wrap)
        {
            if (!Active) return 1f;
            if (float.IsNaN(uv.x) || float.IsNaN(uv.y) || float.IsInfinity(uv.x) || float.IsInfinity(uv.y)) return 0f;
            if (wrap) uv = TiledCanvasUtility.Wrap(uv);
            if (uv.x < 0f || uv.y < 0f || uv.x >= 1f || uv.y >= 1f) return 0f;
            return Coverage[Mathf.FloorToInt(uv.y * Height) * Width + Mathf.FloorToInt(uv.x * Width)] / 255f;
        }
        internal void Rectangle(Vector2 a, Vector2 b, SelectionCombine combine, bool wrap)
        {
            ValidateSize();
            var values = new byte[Width * Height];
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x)), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x));
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y)), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y));
            if (wrap) y1 = Math.Min(y1, y0 + Height);
            else { y0 = Math.Max(0, y0); y1 = Math.Min(Height, y1); }
            for (int y = y0; y < y1; y++) FillSpan(values, y, x0, x1, wrap);
            Set(values, combine);
        }
        internal void Ellipse(Vector2 a, Vector2 b, SelectionCombine combine, bool wrap)
        {
            ValidateSize();
            var values = new byte[Width * Height];
            Vector2 center = (a + b) * .5f;
            Vector2 radius = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)) * .5f;
            if (radius.x > 0f && radius.y > 0f)
            {
                int y0 = Mathf.CeilToInt(center.y - radius.y - .5f), y1 = Mathf.CeilToInt(center.y + radius.y - .5f);
                if (wrap && (long)y1 - y0 > Height * 16L)
                    throw new InvalidOperationException("The ellipse spans too many canvas repeats. Zoom in before selecting.");
                if (!wrap) { y0 = Math.Max(0, y0); y1 = Math.Min(Height, y1); }
                for (int y = y0; y < y1; y++)
                {
                    float dy = (y + .5f - center.y) / radius.y;
                    float halfSpan = radius.x * Mathf.Sqrt(Mathf.Max(0f, 1f - dy * dy));
                    FillSpan(values, y, Mathf.CeilToInt(center.x - halfSpan - .5f),
                        Mathf.CeilToInt(center.x + halfSpan - .5f), wrap);
                }
            }
            Set(values, combine);
        }
        internal void Polygon(IReadOnlyList<Vector2> points, SelectionCombine combine, bool wrap)
        {
            ValidateSize();
            if (points == null || points.Count < 3 || points.Count > 256)
                throw new InvalidOperationException("A polygon selection needs 3–256 vertices.");
            float minY = points[0].y, maxY = minY;
            for (int i = 1; i < points.Count; i++) { minY = Mathf.Min(minY, points[i].y); maxY = Mathf.Max(maxY, points[i].y); }
            int y0 = Mathf.CeilToInt(minY - .5f), y1 = Mathf.CeilToInt(maxY - .5f);
            if (wrap && (long)y1 - y0 > Height * 16L)
                throw new InvalidOperationException("The polygon spans too many canvas repeats. Zoom in before selecting.");
            if (!wrap) { y0 = Math.Max(0, y0); y1 = Math.Min(Height, y1); }
            var values = new byte[Width * Height];
            var intersections = new List<float>(points.Count);
            for (int y = y0; y < y1; y++)
            {
                float scan = y + .5f;
                intersections.Clear();
                for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                {
                    Vector2 a = points[j], b = points[i];
                    if ((a.y > scan) == (b.y > scan)) continue;
                    intersections.Add(a.x + (scan - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                intersections.Sort();
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                    FillSpan(values, y, Mathf.CeilToInt(intersections[i] - .5f), Mathf.CeilToInt(intersections[i + 1] - .5f), wrap);
            }
            Set(values, combine);
        }
        private void FillSpan(byte[] values, int y, int x0, int x1, bool wrap)
        {
            if (!wrap)
            {
                x0 = Math.Max(0, x0); x1 = Math.Min(Width, x1);
                if (x1 > x0) Array.Fill(values, (byte)255, y * Width + x0, x1 - x0);
                return;
            }
            y = ((y % Height) + Height) % Height;
            int count = (int)Math.Min(Width, Math.Max(0L, (long)x1 - x0));
            int start = ((x0 % Width) + Width) % Width;
            int first = Math.Min(count, Width - start);
            Array.Fill(values, (byte)255, y * Width + start, first);
            if (count > first) Array.Fill(values, (byte)255, y * Width, count - first);
        }
    }
}
