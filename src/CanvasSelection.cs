using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
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
        internal void Rectangle(Vector2 a, Vector2 b, SelectionCombine combine, bool wrap, bool antialias = false)
        {
            ValidateSize();
            if (antialias)
            {
                ValidateFinite(a); ValidateFinite(b);
                var coverage = new byte[Width * Height];
                double left = Math.Min(a.x, b.x), right = Math.Max(a.x, b.x);
                double bottom = Math.Min(a.y, b.y), top = Math.Max(a.y, b.y);
                int firstX = wrap ? 0 : (int)Math.Floor(Math.Max(0, Math.Min(Width, left)));
                int lastX = wrap ? Width : (int)Math.Ceiling(Math.Max(0, Math.Min(Width, right)));
                int firstY = wrap ? 0 : (int)Math.Floor(Math.Max(0, Math.Min(Height, bottom)));
                int lastY = wrap ? Height : (int)Math.Ceiling(Math.Max(0, Math.Min(Height, top)));
                for (int y = firstY; y < lastY; y++)
                {
                    double vertical = IntervalCoverage(bottom, top, y, Height, wrap);
                    if (vertical <= 0) continue;
                    for (int x = firstX; x < lastX; x++)
                        coverage[y * Width + x] = CoverageByte(vertical * IntervalCoverage(left, right, x, Width, wrap));
                }
                Set(coverage, combine);
                return;
            }
            var values = new byte[Width * Height];
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x)), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x));
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y)), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y));
            if (wrap) y1 = Math.Min(y1, y0 + Height);
            else { y0 = Math.Max(0, y0); y1 = Math.Min(Height, y1); }
            for (int y = y0; y < y1; y++) FillSpan(values, y, x0, x1, wrap);
            Set(values, combine);
        }
        internal void Ellipse(Vector2 a, Vector2 b, SelectionCombine combine, bool wrap, bool antialias = false)
        {
            ValidateSize();
            if (antialias)
            {
                ValidateFinite(a); ValidateFinite(b);
                double centerX = ((double)a.x + b.x) * .5, centerY = ((double)a.y + b.y) * .5;
                double radiusX = Math.Abs((double)b.x - a.x) * .5, radiusY = Math.Abs((double)b.y - a.y) * .5;
                byte[] coverage;
                if (radiusX <= 0 || radiusY <= 0) coverage = new byte[Width * Height];
                else coverage = RasterizeAntialiasedCoverage(Width, Height, centerY - radiusY, centerY + radiusY, wrap,
                    (scan, spans) =>
                    {
                        double dy = (scan - centerY) / radiusY;
                        if (Math.Abs(dy) >= 1) return;
                        double halfSpan = radiusX * Math.Sqrt(Math.Max(0, 1 - dy * dy));
                        spans.Add(new CoverageSpan(centerX - halfSpan, centerX + halfSpan));
                    });
                Set(coverage, combine);
                return;
            }
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
        internal void Polygon(IReadOnlyList<Vector2> points, SelectionCombine combine, bool wrap, bool antialias = false)
            => RasterizePolygon(points, combine, wrap, 256, antialias);
        internal void Freehand(IReadOnlyList<Vector2> points, SelectionCombine combine, bool wrap, bool antialias = false)
            => RasterizePolygon(points, combine, wrap, 4096, antialias);
        private void RasterizePolygon(IReadOnlyList<Vector2> points, SelectionCombine combine, bool wrap, int maximumPoints, bool antialias)
        {
            ValidateSize();
            if (points == null || points.Count < 3 || points.Count > maximumPoints)
                throw new InvalidOperationException($"A selection outline needs 3–{maximumPoints} points.");
            for (int i = 0; i < points.Count; i++)
                if (float.IsNaN(points[i].x) || float.IsNaN(points[i].y) || float.IsInfinity(points[i].x) || float.IsInfinity(points[i].y))
                    throw new ArgumentException("Selection points must be finite.");
            float minY = points[0].y, maxY = minY;
            for (int i = 1; i < points.Count; i++) { minY = Mathf.Min(minY, points[i].y); maxY = Mathf.Max(maxY, points[i].y); }
            if (antialias)
            {
                var antialiasedIntersections = new List<double>(points.Count);
                byte[] coverage = RasterizeAntialiasedCoverage(Width, Height, minY, maxY, wrap,
                    (scan, spans) =>
                    {
                        antialiasedIntersections.Clear();
                        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                        {
                            Vector2 a = points[j], b = points[i];
                            if ((a.y > scan) == (b.y > scan)) continue;
                            antialiasedIntersections.Add(a.x + (scan - a.y) * ((double)b.x - a.x) / ((double)b.y - a.y));
                        }
                        antialiasedIntersections.Sort();
                        for (int i = 0; i + 1 < antialiasedIntersections.Count; i += 2)
                            spans.Add(new CoverageSpan(antialiasedIntersections[i], antialiasedIntersections[i + 1]));
                    });
                Set(coverage, combine);
                return;
            }
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
        private static void ValidateFinite(Vector2 point)
        {
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y))
                throw new ArgumentException("Selection points must be finite.");
        }
        private static byte CoverageByte(double value)
            => (byte)Math.Round(Math.Max(0, Math.Min(1, value)) * 255, MidpointRounding.AwayFromZero);
        private static double IntervalCoverage(double start, double end, int pixel, int period, bool wrap)
        {
            double length = end - start;
            if (length <= 0) return 0;
            if (!wrap) return Math.Max(0, Math.Min(pixel + 1, end) - Math.Max(pixel, start));
            if (length >= period) return 1;
            start = ((start % period) + period) % period;
            end = start + length;
            double coverage = Math.Max(0, Math.Min(pixel + 1, end) - Math.Max(pixel, start));
            if (end > period) coverage += Math.Max(0, Math.Min(pixel + 1, end - period) - pixel);
            return Math.Min(1, coverage);
        }
        internal readonly struct CoverageSpan : IComparable<CoverageSpan>
        {
            internal readonly double left, right;
            internal CoverageSpan(double left, double right) { this.left = left; this.right = right; }
            public int CompareTo(CoverageSpan other)
            {
                int result = left.CompareTo(other.left);
                return result == 0 ? right.CompareTo(other.right) : result;
            }
        }
        internal static byte[] RasterizeAntialiasedCoverage(int width, int height, double minY, double maxY, bool wrap,
            Action<double, List<CoverageSpan>> collectSpans)
        {
            const int samples = 8;
            var values = new byte[width * height];
            if (maxY <= minY) return values;
            if (wrap && maxY - minY > height * 16.0)
                throw new InvalidOperationException("The selection spans too many canvas repeats. Zoom in before selecting.");
            int y0 = wrap ? 0 : (int)Math.Floor(Math.Max(0, Math.Min(height, minY)));
            int y1 = wrap ? height : (int)Math.Ceiling(Math.Max(0, Math.Min(height, maxY)));
            var rawSpans = new List<CoverageSpan>();
            var spans = new List<CoverageSpan>();
            var verticalSpans = new List<CoverageSpan>(2);
            var difference = new double[width + 1];
            double verticalLength = maxY - minY;
            double wrappedBottom = wrap ? ((minY % height) + height) % height : 0;
            double wrappedTop = wrappedBottom + verticalLength;
            for (int y = y0; y < y1; y++)
            {
                verticalSpans.Clear();
                if (!wrap) AddClippedSpan(verticalSpans, minY, maxY, y, y + 1);
                else if (verticalLength >= height) verticalSpans.Add(new CoverageSpan(y, y + 1));
                else
                {
                    AddClippedSpan(verticalSpans, wrappedBottom, wrappedTop, y, y + 1);
                    if (wrappedTop > height) AddClippedSpan(verticalSpans, 0, wrappedTop - height, y, y + 1);
                }
                int dirtyMin = width, dirtyMax = -1;
                foreach (CoverageSpan vertical in verticalSpans)
                for (int sample = 0; sample < samples; sample++)
                {
                    double weight = (vertical.right - vertical.left) / samples;
                    double scan = vertical.left + (sample + .5) * weight;
                    rawSpans.Clear();
                    if (!wrap) collectSpans(scan, rawSpans);
                    else
                    {
                        double first = Math.Ceiling((minY - scan) / height);
                        double last = Math.Floor((maxY - scan) / height);
                        int count = (int)Math.Max(0, Math.Min(17, last - first + 1));
                        for (int repeat = 0; repeat < count; repeat++) collectSpans(scan + (first + repeat) * height, rawSpans);
                    }
                    spans.Clear();
                    foreach (CoverageSpan span in rawSpans)
                    {
                        if (span.right <= span.left) continue;
                        if (!wrap)
                        {
                            double left = Math.Max(0, span.left), right = Math.Min(width, span.right);
                            if (right > left) spans.Add(new CoverageSpan(left, right));
                        }
                        else if (span.right - span.left >= width) spans.Add(new CoverageSpan(0, width));
                        else
                        {
                            double left = ((span.left % width) + width) % width;
                            double right = left + (span.right - span.left);
                            spans.Add(new CoverageSpan(left, Math.Min(width, right)));
                            if (right > width) spans.Add(new CoverageSpan(0, right - width));
                        }
                    }
                    if (spans.Count == 0) continue;
                    spans.Sort();
                    double mergedLeft = spans[0].left, mergedRight = spans[0].right;
                    for (int i = 1; i <= spans.Count; i++)
                    {
                        if (i < spans.Count && spans[i].left <= mergedRight)
                        { mergedRight = Math.Max(mergedRight, spans[i].right); continue; }
                        AccumulateSpan(difference, mergedLeft, mergedRight, weight, ref dirtyMin, ref dirtyMax);
                        if (i < spans.Count) { mergedLeft = spans[i].left; mergedRight = spans[i].right; }
                    }
                }
                double coverage = 0;
                for (int x = dirtyMin; x <= dirtyMax; x++)
                {
                    coverage += difference[x];
                    difference[x] = 0;
                    if (x < width) values[y * width + x] = CoverageByte(coverage);
                }
            }
            return values;
        }
        private static void AddClippedSpan(List<CoverageSpan> spans, double left, double right, double clipLeft, double clipRight)
        {
            left = Math.Max(left, clipLeft); right = Math.Min(right, clipRight);
            if (right > left) spans.Add(new CoverageSpan(left, right));
        }
        private static void AccumulateSpan(double[] difference, double left, double right, double weight,
            ref int dirtyMin, ref int dirtyMax)
        {
            int first = (int)Math.Floor(left), last = (int)Math.Floor(right);
            dirtyMin = Math.Min(dirtyMin, first);
            dirtyMax = Math.Max(dirtyMax, last);
            if (first == last)
            {
                double coverage = (right - left) * weight;
                difference[first] += coverage; difference[first + 1] -= coverage;
                dirtyMax = Math.Max(dirtyMax, first + 1);
                return;
            }
            double firstCoverage = (first + 1 - left) * weight;
            difference[first] += firstCoverage; difference[first + 1] -= firstCoverage;
            if (last > first + 1) { difference[first + 1] += weight; difference[last] -= weight; }
            double lastCoverage = (right - last) * weight;
            if (lastCoverage > 0)
            {
                difference[last] += lastCoverage; difference[last + 1] -= lastCoverage;
                dirtyMax = Math.Max(dirtyMax, last + 1);
            }
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
