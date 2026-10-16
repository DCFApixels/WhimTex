using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Portable scan conversion of resolved polygon coverage. AA integrates horizontal spans
    /// exactly at eight vertical samples; Step evaluates only the destination pixel center.
    /// Scan scratch storage is bounded; prepared coverage is owned by the layer cache.
    /// </summary>
    internal static class VectorRasterizer
    {
        internal const int MaxRasterPixels = 16777216;
        private const long MaxScanWork = 100000000;
        private const int MaxEdges = 1048576;

        internal sealed class Coverage : IDisposable
        {
            internal readonly int shapeIndex, x, y;
            internal readonly Texture2D texture;
            internal readonly WhimTexGradientPalette fillPalette = new WhimTexGradientPalette(), strokePalette = new WhimTexGradientPalette();
            internal Coverage(int index, int x, int y, int width, int height)
            {
                shapeIndex = index; this.x = x; this.y = y;
                texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { name = "Vector coverage", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            public void Dispose()
            { UnityEngine.Object.DestroyImmediate(texture); fillPalette.Dispose(); strokePalette.Dispose(); }
        }
        internal sealed class CoverageBudgetExceeded : Exception { }

        // Render-only Boolean output. Preserve the former normalized-float round trip,
        // adjacent-point filtering, seam and bounds without constructing editable anchors.
        internal sealed class PreparedShape
        {
            internal readonly List<List<Vector2>> closedPaths;
            internal readonly Paths64 fillInput;
            internal readonly Vector2 reference, minimum, maximum;
            internal readonly int pointCount;
            internal readonly ulong hash;
            internal readonly List<VectorContour> fallbackContours;

            internal PreparedShape(Paths64 source, Vector2 reference, ref int contours, ref int anchors)
            {
                this.reference = reference;
                closedPaths = new List<List<Vector2>>(source.Count);
                fillInput = new Paths64(source.Count);
                Vector2 min = Vector2.zero, max = reference;
                ulong key = 14695981039346656037UL;
                void Hash(int value) { unchecked { key = (key ^ (uint)value) * 1099511628211UL; } }
                Hash(source.Count);
                if ((contours += source.Count) > VectorGeometry.MaxContours) throw VectorGeometry.Limit("contour count");
                int flattened = 0;
                bool needsSubdivision = false;
                const float minimumToleranceSquared = (float)(1d / (StrokeGeometry.Precision * StrokeGeometry.Precision));
                foreach (var vertices in source)
                {
                    if ((anchors += vertices.Count) > VectorGeometry.MaxPoints) throw VectorGeometry.Limit("anchor count");
                    Hash(vertices.Count);
                    var path = new List<Vector2>(vertices.Count);
                    Vector2 first = default, previous = default;
                    bool hasPrevious = false;
                    foreach (var vertex in vertices)
                    {
                        var normalized = new Vector2((float)(vertex.X / StrokeGeometry.Precision / reference.x),
                            (float)(vertex.Y / StrokeGeometry.Precision / reference.y));
                        var p = Vector2.Scale(normalized, reference);
                        if (!VectorGeometry.Finite(p) || Math.Abs(p.x) > VectorGeometry.MaxCoordinate || Math.Abs(p.y) > VectorGeometry.MaxCoordinate)
                            throw VectorGeometry.Limit("coordinate range");
                        min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                        Hash(p.x.GetHashCode()); Hash(p.y.GetHashCode());
                        // At extreme coordinates even a zero-handle straight cubic can
                        // subdivide because of float cancellation in its flatness test.
                        if (hasPrevious) needsSubdivision |= VectorGeometry.DistanceSquared(p, previous, p, out _) > minimumToleranceSquared;
                        else first = p;
                        previous = p; hasPrevious = true;
                        if (path.Count == 0 || path[path.Count - 1] != p) path.Add(p);
                    }
                    if (hasPrevious) needsSubdivision |= VectorGeometry.DistanceSquared(first, previous, first, out _) > minimumToleranceSquared;
                    // Flattening closes the last segment and then removes its repeated endpoint.
                    if (path.Count > 1 && path[0] == path[path.Count - 1]) path.RemoveAt(path.Count - 1);
                    flattened += path.Count;
                    closedPaths.Add(path);
                    if (path.Count >= 3) fillInput.Add(StrokeGeometry.ToPath(path));
                }
                minimum = min; maximum = max; pointCount = flattened; hash = key;
                if (needsSubdivision)
                {
                    fallbackContours = new List<VectorContour>(source.Count);
                    foreach (var vertices in source)
                    {
                        var contour = new VectorContour();
                        foreach (var p in vertices) contour.points.Add(new VectorPoint(new Vector2(
                            (float)(p.X / StrokeGeometry.Precision / reference.x), (float)(p.Y / StrokeGeometry.Precision / reference.y))));
                        fallbackContours.Add(contour);
                    }
                }
            }
        }

        internal static List<Coverage> Prepare(VectorLayerBehaviour model, int width, int height, ProjectiveMatrix matrix, bool clip)
        {
            var result = new List<Coverage>();
            try { Rasterize(model, null, width, height, matrix, clip, result); return result; }
            catch { foreach (var coverage in result) coverage.Dispose(); throw; }
        }

        internal static void ValidateSize(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize ||
                (long)width * height > MaxRasterPixels) throw VectorGeometry.Limit("raster pixel count");
        }

        internal static void Render(VectorLayerBehaviour model, Texture2D target, ProjectiveMatrix localToCanvas, bool clipToUnit)
            => Rasterize(model, target, target.width, target.height, localToCanvas, clipToUnit, null);

        private static void Rasterize(VectorLayerBehaviour model, Texture2D target, int width, int height,
            ProjectiveMatrix localToCanvas, bool clipToUnit, List<Coverage> prepared)
        {
            using var geometryBuild = StrokeGeometry.BeginBuild();
            var pixels = target != null ? target.GetRawTextureData<Color>() : default;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = default;
            if (!localToCanvas.IsFinite) throw new InvalidOperationException("Vector transform must be finite.");
            if (!localToCanvas.TryInverse(out var canvasToLocal)) return; // A collapsed layer has no coverage.
            var toPixels = ProjectiveMatrix.Scale(width, height) * localToCanvas *
                ProjectiveMatrix.Scale(1d / model.referenceSize.x, 1d / model.referenceSize.y);
            var viewport = new Paths64 { StrokeGeometry.ToPath(new List<Vector2>
                { Vector2.zero, new Vector2(model.referenceSize.x, 0), model.referenceSize, new Vector2(0, model.referenceSize.y) }) };
            var row = new CoverageRow(width);
            int flattenedCount = 0, edgeCount = 0;
            long work = 0, coverageBytes = 0;
            for (int shapeIndex = 0; shapeIndex < model.shapes.Count; shapeIndex++)
            {
                var shape = model.shapes[shapeIndex];
                if (!shape.visible || (shape.fillMode == VectorFillMode.None && (!shape.stroke || shape.strokeMode == VectorFillMode.None || shape.strokeWidth == 0))) continue;
                var ready = model.PreparedContours?[shapeIndex];
                float tolerance = FlattenTolerance(shape, model.referenceSize, toPixels, clipToUnit, prepared: ready);
                bool direct = ready != null && ready.fallbackContours == null;
                var fillInput = direct ? ready.fillInput : new Paths64();
                var closedPaths = direct ? ready.closedPaths : new List<List<Vector2>>();
                var openPaths = new List<List<Vector2>>();
                if (ready != null && !ready.reference.Equals(model.referenceSize)) throw new InvalidOperationException("Prepared Vector reference size changed.");
                if (direct)
                {
                    if ((flattenedCount += ready.pointCount) > VectorGeometry.MaxFlattenedPoints) throw VectorGeometry.Limit("total flattened point count");
                }
                else
                {
                    var contours = ready?.fallbackContours ?? shape.contours;
                    var flattened = prepared != null ? VectorContourPreparation.Flatten(contours, model.referenceSize, tolerance) : null;
                    for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
                    {
                        var contour = contours[contourIndex];
                        var path = flattened != null ? flattened[contourIndex] : new List<Vector2>();
                        if (flattened == null) VectorGeometry.FlattenContour(contour, model.referenceSize, tolerance, path);
                        if ((flattenedCount += path.Count) > VectorGeometry.MaxFlattenedPoints) throw VectorGeometry.Limit("total flattened point count");
                        if (contour.closed)
                        {
                            closedPaths.Add(path);
                            if (path.Count >= 3) fillInput.Add(StrokeGeometry.ToPath(path));
                        }
                        else openPaths.Add(path);
                    }
                }
                var interior = fillInput.Count == 0 ? fillInput : StrokeGeometry.Resolve(fillInput, rule:
                    shape.fillRule == VectorFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
                Paths64 strokes = new Paths64();
                if (shape.stroke && shape.strokeMode != VectorFillMode.None && shape.strokeWidth > 0)
                {
                    strokes = StrokeGeometry.Outline(closedPaths, openPaths, interior, StrokeStyle.From(shape), tolerance);
                }
                var fills = shape.fillMode == VectorFillMode.None ? new Paths64() : interior;
                if (clipToUnit)
                {
                    if (fills.Count > 0) fills = StrokeGeometry.Resolve(fills, viewport, ClipType.Intersection);
                    if (strokes.Count > 0) strokes = StrokeGeometry.Resolve(strokes, viewport, ClipType.Intersection);
                }
                var fill = new ScanPath(fills, toPixels, width, height, ref edgeCount);
                var stroke = new ScanPath(strokes, toPixels, width, height, ref edgeCount);
                Coverage coverage = null;
                if (prepared != null)
                {
                    int x = Math.Min(fill.minX, stroke.minX), y = Math.Min(fill.minY, stroke.minY);
                    int w = Math.Max(fill.maxX, stroke.maxX) - x, h = Math.Max(fill.maxY, stroke.maxY) - y;
                    if (w <= 0 || h <= 0) continue;
                    // Bound retained GPU/CPU payload; very large compound figures retain the exact CPU path.
                    coverageBytes += (long)w * h * 16 * 2;
                    if (coverageBytes > 128L * 1024 * 1024) throw new CoverageBudgetExceeded();
                    coverage = new Coverage(shapeIndex, x, y, w, h); prepared.Add(coverage);
                }
                if (coverage != null)
                    VectorCoveragePreparation.Render(fill.Snapshot(), stroke.Snapshot(), coverage, model.edgeMode == VectorEdgeMode.Step, ref work);
                else RenderShape(shape, model, pixels, width, height, fill, stroke, canvasToLocal, row, ref work, null);
                coverage?.texture.Apply(false, false);
            }
        }

        internal static float FlattenTolerance(VectorShape shape, Vector2 reference, ProjectiveMatrix m, bool clip, bool includeStroke = true,
            PreparedShape prepared = null)
        {
            double x0 = 0, y0 = 0, x1 = reference.x, y1 = reference.y;
            if (!clip)
            {
                if (prepared != null)
                {
                    Include(prepared.minimum); Include(prepared.maximum);
                }
                else
                {
                    foreach (var contour in shape.contours) foreach (var p in contour.points)
                    {
                        Include(Vector2.Scale(p.position, reference));
                        Include(Vector2.Scale(p.position + p.inTangent, reference));
                        Include(Vector2.Scale(p.position + p.outTangent, reference));
                    }
                }
                double expand = includeStroke && shape.stroke ? shape.strokeWidth * Math.Max(1, shape.miterLimit) : 0;
                x0 -= expand; y0 -= expand; x1 += expand; y1 += expand;
            }
            void Include(Vector2 p) { x0 = Math.Min(x0, p.x); x1 = Math.Max(x1, p.x); y0 = Math.Min(y0, p.y); y1 = Math.Max(y1, p.y); }
            double w00 = m.m20 * x0 + m.m21 * y0 + m.m22, w01 = m.m20 * x0 + m.m21 * y1 + m.m22;
            double w10 = m.m20 * x1 + m.m21 * y0 + m.m22, w11 = m.m20 * x1 + m.m21 * y1 + m.m22;
            if (!(w00 > 0 && w01 > 0 && w10 > 0 && w11 > 0) && !(w00 < 0 && w01 < 0 && w10 < 0 && w11 < 0))
                throw new InvalidOperationException("Vector geometry crosses the projective horizon. Move the points or adjust the transform.");
            double minW = Math.Min(Math.Min(Math.Abs(w00), Math.Abs(w01)), Math.Min(Math.Abs(w10), Math.Abs(w11)));
            double mx = Math.Max(Math.Abs(x0), Math.Abs(x1)), my = Math.Max(Math.Abs(y0), Math.Abs(y1));
            double nx = Math.Abs(m.m00) * mx + Math.Abs(m.m01) * my + Math.Abs(m.m02);
            double ny = Math.Abs(m.m10) * mx + Math.Abs(m.m11) * my + Math.Abs(m.m12);
            double a = Math.Abs(m.m00) / minW + nx * Math.Abs(m.m20) / (minW * minW);
            double b = Math.Abs(m.m01) / minW + nx * Math.Abs(m.m21) / (minW * minW);
            double c = Math.Abs(m.m10) / minW + ny * Math.Abs(m.m20) / (minW * minW);
            double d = Math.Abs(m.m11) / minW + ny * Math.Abs(m.m21) / (minW * minW);
            double magnification = Math.Sqrt(a * a + b * b + c * c + d * d);
            if (!ProjectiveMatrix.Finite(magnification) || magnification > 1024) throw VectorGeometry.Limit("transform magnification");
            return (float)Math.Max(1d / StrokeGeometry.Precision, Math.Min(.25, .125 / Math.Max(.001, magnification)));
        }

        private sealed class CoverageRow
        {
            internal readonly float[] fill, stroke, intersection;
            internal readonly List<Span> fillSpans = new List<Span>(), strokeSpans = new List<Span>();
            internal CoverageRow(int width) { fill = new float[width]; stroke = new float[width]; intersection = new float[width]; }
            internal void Clear() { Array.Clear(fill, 0, fill.Length); Array.Clear(stroke, 0, stroke.Length); Array.Clear(intersection, 0, intersection.Length); }
        }
        private readonly struct Span
        {
            internal readonly double left, right;
            internal Span(double left, double right) { this.left = left; this.right = right; }
        }
        private sealed class Edge
        {
            internal double y0, y1, x0, slope;
            internal int winding;
        }
        private readonly struct Crossing : IComparable<Crossing>
        {
            internal readonly double x;
            internal readonly int winding;
            internal Crossing(double x, int winding) { this.x = x; this.winding = winding; }
            public int CompareTo(Crossing other) => x.CompareTo(other.x);
        }
        private sealed class ScanPath
        {
            private readonly List<Edge> edges = new List<Edge>(), active = new List<Edge>();
            private readonly List<Crossing> crossings = new List<Crossing>();
            private int next;
            internal int minX, maxX, minY, maxY;

            internal VectorCoveragePreparation.Edge[] Snapshot()
            {
                var result = new VectorCoveragePreparation.Edge[edges.Count];
                for (int i = 0; i < edges.Count; i++)
                {
                    var e = edges[i];
                    result[i] = new VectorCoveragePreparation.Edge { x0 = e.x0, y0 = e.y0, y1 = e.y1, slope = e.slope, winding = e.winding };
                }
                return result;
            }

            internal ScanPath(Paths64 paths, ProjectiveMatrix matrix, int width, int height, ref int edgeCount)
            {
                minX = width; minY = height; maxX = maxY = 0;
                foreach (var path in paths)
                {
                    if (path.Count < 3) continue;
                    Double2 a = Map(path[path.Count - 1]);
                    foreach (var vertex in path)
                    {
                        Double2 b = Map(vertex);
                        minX = Math.Min(minX, (int)Math.Max(0, Math.Min(width, Math.Floor(b.x))));
                        maxX = Math.Max(maxX, (int)Math.Max(0, Math.Min(width, Math.Ceiling(b.x))));
                        minY = Math.Min(minY, (int)Math.Max(0, Math.Min(height, Math.Floor(b.y))));
                        maxY = Math.Max(maxY, (int)Math.Max(0, Math.Min(height, Math.Ceiling(b.y))));
                        if (a.y != b.y && Math.Max(a.y, b.y) > 0 && Math.Min(a.y, b.y) < height)
                        {
                            if (++edgeCount > MaxEdges) throw VectorGeometry.Limit("scan edge count");
                            bool up = b.y > a.y;
                            var low = up ? a : b; var high = up ? b : a;
                            edges.Add(new Edge { x0 = low.x, y0 = low.y, y1 = high.y,
                                slope = (high.x - low.x) / (high.y - low.y), winding = up ? 1 : -1 });
                        }
                        a = b;
                    }
                }
                edges.Sort((a, b) => a.y0.CompareTo(b.y0));
                Double2 Map(Point64 p)
                {
                    if (!matrix.TryPoint(new Double2(p.X / StrokeGeometry.Precision, p.Y / StrokeGeometry.Precision), out var result) ||
                        Math.Abs(result.x) > 1e12 || Math.Abs(result.y) > 1e12) throw VectorGeometry.Limit("transformed coordinate range");
                    return result;
                }
            }

            internal void Scan(double y, int width, List<Span> spans, ref long work)
            {
                spans.Clear(); crossings.Clear();
                while (next < edges.Count && edges[next].y0 <= y) active.Add(edges[next++]);
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    Edge e = active[i];
                    if (e.y1 <= y) { active[i] = active[active.Count - 1]; active.RemoveAt(active.Count - 1); continue; }
                    crossings.Add(new Crossing(e.x0 + (y - e.y0) * e.slope, e.winding));
                }
                work += crossings.Count;
                if (work > MaxScanWork) throw VectorGeometry.Limit("scan conversion work");
                crossings.Sort();
                int winding = 0; double start = 0;
                for (int i = 0; i < crossings.Count;)
                {
                    double x = crossings[i].x; int delta = 0;
                    do { delta += crossings[i++].winding; } while (i < crossings.Count && crossings[i].x == x);
                    int before = winding; winding += delta;
                    if (before == 0 && winding != 0) start = x;
                    else if (before != 0 && winding == 0)
                    {
                        double left = Math.Max(0, start), right = Math.Min(width, x);
                        if (right > left) spans.Add(new Span(left, right));
                    }
                }
            }
        }

        private static void RenderShape(VectorShape shape, VectorLayerBehaviour model, NativeArray<Color> pixels,
            int width, int height, ScanPath fill, ScanPath stroke, ProjectiveMatrix canvasToLocal, CoverageRow row, ref long work, Coverage coverage)
        {
            var mask = coverage != null ? coverage.texture.GetRawTextureData<Color>() : default;
            bool step = model.edgeMode == VectorEdgeMode.Step;
            int samples = step ? 1 : 8;
            int y0 = Math.Min(fill.minY, stroke.minY), y1 = Math.Max(fill.maxY, stroke.maxY);
            int x0 = Math.Min(fill.minX, stroke.minX), x1 = Math.Max(fill.maxX, stroke.maxX);
            Color solid = HdrUtility.Decode(shape.fillColor), solidInk = HdrUtility.Decode(shape.strokeColor);
            var paint = new FigureFill(shape.gradientStart, shape.gradientEnd, model.referenceSize, shape.gradientRepetitions);
            var strokePaint = new FigureFill(shape.strokeGradientStart, shape.strokeGradientEnd, model.referenceSize, shape.strokeGradientRepetitions);
            bool gradientFill = FigureFill.IsGradient(shape.fillMode), gradientStroke = FigureFill.IsGradient(shape.strokeMode);
            for (int y = y0; y < y1; y++)
            {
                row.Clear();
                for (int sample = 0; sample < samples; sample++)
                {
                    double sy = y + (sample + .5) / samples;
                    fill.Scan(sy, width, row.fillSpans, ref work); stroke.Scan(sy, width, row.strokeSpans, ref work);
                    foreach (var span in row.fillSpans) Accumulate(row.fill, span, samples, step);
                    foreach (var span in row.strokeSpans) Accumulate(row.stroke, span, samples, step);
                    int a = 0, b = 0;
                    while (a < row.fillSpans.Count && b < row.strokeSpans.Count)
                    {
                        Span f = row.fillSpans[a], s = row.strokeSpans[b];
                        double left = Math.Max(f.left, s.left), right = Math.Min(f.right, s.right);
                        if (right > left) Accumulate(row.intersection, new Span(left, right), samples, step);
                        if (f.right <= s.right) a++; else b++;
                    }
                }
                work += x1 - x0;
                if (work > MaxScanWork) throw VectorGeometry.Limit("pixel shading work");
                for (int x = x0; x < x1; x++)
                {
                    float fc = Mathf.Clamp01(row.fill[x]), sc = Mathf.Clamp01(row.stroke[x]);
                    if (coverage != null)
                    {
                        mask[(y - coverage.y) * coverage.texture.width + x - coverage.x] =
                            new Color(fc, sc, Mathf.Clamp01(row.intersection[x]), 0);
                        continue;
                    }
                    if (fc == 0 && sc == 0) continue;
                    Color color = solid, ink = solidInk;
                    if (fc > 0 && gradientFill || sc > 0 && gradientStroke)
                    {
                        if (!canvasToLocal.TryPoint(new Double2((x + .5) / width, (y + .5) / height), out var uv))
                            throw new InvalidOperationException("Vector gradient sample crosses the projective horizon.");
                        double px = uv.x * model.referenceSize.x, py = uv.y * model.referenceSize.y;
                        if (fc > 0 && gradientFill) color = GradientColor(shape.gradient, paint.Coordinate(shape.fillMode, px, py));
                        if (sc > 0 && gradientStroke) ink = GradientColor(shape.strokeGradient, strokePaint.Coordinate(shape.strokeMode, px, py));
                    }
                    // Integrate paint order with the actual fill/stroke overlap coverage. Multiplying
                    // independently averaged coverages would produce dark fringes along shared edges.
                    float sa = ink.a * sc;
                    float fa = color.a * Mathf.Max(0, fc - ink.a * Mathf.Clamp01(row.intersection[x]));
                    float alpha = Mathf.Clamp01(sa + fa);
                    Color source = color * fa + ink * sa;
                    Color destination = pixels[y * width + x];
                    float back = destination.a * (1 - alpha), outAlpha = alpha + back;
                    Color result = outAlpha > 1e-12f ? (source + destination * back) / outAlpha : default;
                    result.a = outAlpha; pixels[y * width + x] = result;
                }
            }
        }

        private static Color GradientColor(WhimTexGradient gradient, double t)
        {
            if (!ProjectiveMatrix.Finite(t) || Math.Abs(t) > float.MaxValue) throw VectorGeometry.Limit("gradient coordinate range");
            return HdrUtility.Decode(gradient.EvaluateEncoded((float)t));
        }

        private static void Accumulate(float[] coverage, Span span, int samples, bool step)
        {
            if (step)
            {
                int start = Math.Max(0, (int)Math.Ceiling(span.left - .5));
                int end = Math.Min(coverage.Length, (int)Math.Ceiling(span.right - .5));
                for (int x = start; x < end; x++) coverage[x] = 1;
            }
            else
            {
                int start = (int)Math.Floor(span.left), end = Math.Min(coverage.Length, (int)Math.Ceiling(span.right));
                for (int x = start; x < end; x++) coverage[x] += (float)(Math.Min(x + 1, span.right) - Math.Max(x, span.left)) / samples;
            }
        }
    }
}
