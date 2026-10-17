using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Source-pixel coverage, independent of storage and of the stroke's bounding box.
    internal sealed class PaintTileSet
    {
        internal const int Size = 256;
        private bool[] marked;
        private readonly List<int> indices = new List<int>();
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        private int Columns => (Width + Size - 1) / Size;
        internal int Count => indices.Count;
        internal long PixelCount { get { long count = 0; for (int i = 0; i < Count; i++) { var r = this[i]; count += (long)r.width * r.height; } return count; } }
        internal RectInt this[int i]
        {
            get { int x = indices[i] % Columns * Size, y = indices[i] / Columns * Size;
                return new RectInt(x, y, Mathf.Min(Size, Width - x), Mathf.Min(Size, Height - y)); }
        }
        internal void Reset(int width, int height)
        {
            Clear(); Width = width; Height = height;
            int length = Columns * ((height + Size - 1) / Size);
            if (marked == null || marked.Length != length) marked = new bool[length];
        }
        internal void Clear() { if (marked != null) foreach (int i in indices) marked[i] = false; indices.Clear(); }
        internal void IncludeUv(Rect uv)
        {
            if (uv.width <= 0 || uv.height <= 0) return;
            Include(new RectInt(Mathf.FloorToInt(uv.xMin * Width) - 1, Mathf.FloorToInt(uv.yMin * Height) - 1,
                Mathf.CeilToInt(uv.xMax * Width) - Mathf.FloorToInt(uv.xMin * Width) + 2,
                Mathf.CeilToInt(uv.yMax * Height) - Mathf.FloorToInt(uv.yMin * Height) + 2));
        }
        internal void Include(RectInt bounds)
        {
            int x0 = Mathf.Clamp(bounds.xMin, 0, Width), y0 = Mathf.Clamp(bounds.yMin, 0, Height);
            int x1 = Mathf.Clamp(bounds.xMax, 0, Width), y1 = Mathf.Clamp(bounds.yMax, 0, Height);
            if (x1 <= x0 || y1 <= y0) return;
            for (int y = y0 / Size; y <= (y1 - 1) / Size; y++)
            for (int x = x0 / Size; x <= (x1 - 1) / Size; x++)
            { int i = y * Columns + x; if (!marked[i]) { marked[i] = true; indices.Add(i); } }
        }
        internal void Union(PaintTileSet other) { for (int i = 0; i < other.Count; i++) Include(other[i]); }
        internal RectInt[] ToArray() { var result = new RectInt[Count]; for (int i = 0; i < Count; i++) result[i] = this[i]; return result; }
    }
}
