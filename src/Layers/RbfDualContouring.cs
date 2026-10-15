using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class RbfDistanceField
    {
        // Independent 2D Hermite dual-contouring experiment, not a port of ODC.
        // Query the continuous RBF (not a bilinear approximation of its grid),
        // fit one bounded QEF vertex per cell/component, then connect adjacent
        // dual vertices across sign-changing grid edges. Ambiguous cells retain
        // two components. A coarse grid can still miss sub-cell features.
        static ArtisticDistanceField.Geometry PrepareDualContour(ContinuousField sampler,
            NativeArray<float> field, int gridWidth, int gridHeight, NativeArray<Color32> input,
            int width, int height, byte threshold, int channel, Allocator allocator, SharpDistanceMode mode)
        {
            for (int i = 0; i < field.Length; i++)
                if (!float.IsFinite(field[i])) throw new InvalidOperationException("RBF produced a non-finite interpolant.");
            var pending = new Dictionary<int, DualEdge>();
            var segments = new List<ArtisticDistanceField.Feature>();
            int batchRows = math.max(1, 65536 / (gridWidth - 1));
            int vertices = 0;
            for (int first = 0; first + 1 < gridHeight; first += batchRows)
            {
                int count = math.min(batchRows, gridHeight - 1 - first);
                using var stream = new NativeStream(count, Allocator.TempJob);
                new DualExtractJob { sampler = sampler, field = field, gridWidth = gridWidth,
                    first = first, writer = stream.AsWriter() }.Schedule(count, 1).Complete();
                var reader = stream.AsReader();
                for (int row = 0; row < count; row++)
                {
                    int length = reader.BeginForEachIndex(row);
                    for (int i = 0; i < length; i++)
                    {
                        DualVertex vertex = reader.Read<DualVertex>();
                        Join(vertex.key0, vertex.point, vertex.root0);
                        Join(vertex.key1, vertex.point, vertex.root1);
                        if (++vertices > 2000000)
                            throw new InvalidOperationException("RBF Dual contour exceeds two million vertices. Reduce source detail or resolution.");
                    }
                    reader.EndForEachIndex();
                }
            }
            // A contour crossing the image boundary is open, not connected to
            // another unrelated boundary crossing. Finish it at its actual root.
            foreach (var edge in pending.Values) Add(edge.point, edge.root);
            return ArtisticDistanceField.BuildRbfContourGeometry(segments, field, gridWidth, gridHeight,
                input, width, height, threshold, channel, allocator, mode, fitFlanks: false);

            void Add(float2 a, float2 b)
            {
                if (math.any(a != b)) segments.Add(new ArtisticDistanceField.Feature { a = a, b = b });
            }
            void Join(int key, float2 point, float2 root)
            {
                if (pending.TryGetValue(key, out DualEdge edge))
                {
                    Add(edge.point, point);
                    pending.Remove(key);
                }
                else pending.Add(key, new DualEdge { point = point, root = root });
            }
        }

        struct DualEdge { public float2 point, root; }
        struct DualVertex { public float2 point, root0, root1; public int key0, key1; }
        struct Hermite { public double2 point, normal; public int key; }

        struct ContinuousField
        {
            [ReadOnly] public NativeArray<float> source;
            [ReadOnly] public NativeArray<double2> centers;
            [ReadOnly] public NativeArray<double> weights;
            [ReadOnly] public NativeArray<int> counts;
            public int width, height, columns, rows;

            public double Value(double2 p) => Sample(p, false, out _);
            public double Sample(double2 p, bool derivatives, out double2 gradient)
            {
                p = math.clamp(p, 0, new double2(width - 1, height - 1));
                int x = (int)p.x, y = (int)p.y;
                int cellX = x / Stride, cellY = y / Stride;
                double sum = 0, total = 0;
                double2 sumGradient = 0, totalGradient = 0;
                for (int py = math.max(0, cellY - 1); py <= math.min(rows - 1, cellY + 2); py++)
                for (int px = math.max(0, cellX - 1); px <= math.min(columns - 1, cellX + 2); px++)
                {
                    int patch = py * columns + px, n = counts[patch];
                    if (n == 0) continue;
                    double2 q = (p - new double2(math.min(px * Stride, width - 1), math.min(py * Stride, height - 1))) / Scale;
                    double r2 = math.lengthsq(q);
                    if (r2 >= 1) continue;
                    double r = math.sqrt(r2), s = 1 - r;
                    double weight = s * s * s * s * (4 * r + 1);
                    double2 localGradient = 0;
                    double value = (derivatives ? Evaluate(centers, weights, n, q, out localGradient, patch * MaxCenters, patch * MaxSystem)
                        : EvaluateValue(centers, weights, n, q, patch * MaxCenters, patch * MaxSystem)) * Scale;
                    sum += weight * value; total += weight;
                    if (derivatives)
                    {
                        double2 weightGradient = (-20 * s * s * s / Scale) * q;
                        sumGradient += weight * localGradient + value * weightGradient;
                        totalGradient += weightGradient;
                    }
                }
                if (total > 1e-20)
                {
                    double value = sum / total;
                    gradient = (sumGradient - value * totalGradient) / total;
                    return value;
                }
                int x1 = math.min(x + 1, width - 1), y1 = math.min(y + 1, height - 1);
                double a = source[y * width + x], b = source[y * width + x1];
                double c = source[y1 * width + x], d = source[y1 * width + x1];
                double2 f = math.frac(p);
                gradient = new double2(math.lerp(b - a, d - c, f.y), math.lerp(c - a, d - b, f.x));
                return math.lerp(math.lerp(a, b, f.x), math.lerp(c, d, f.x), f.y);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct DualExtractJob : IJobParallelFor
        {
            public ContinuousField sampler;
            [ReadOnly] public NativeArray<float> field;
            public NativeStream.Writer writer;
            public int gridWidth, first;

            public void Execute(int row)
            {
                writer.BeginForEachIndex(row);
                int y = first + row;
                for (int x = 0; x + 1 < gridWidth; x++)
                {
                    float a = field[y * gridWidth + x], b = field[y * gridWidth + x + 1];
                    float c = field[(y + 1) * gridWidth + x + 1], d = field[(y + 1) * gridWidth + x];
                    var edges = new FixedList512Bytes<Hermite>();
                    if ((a < 0) != (b < 0)) edges.Add(Edge(x, y, false, a, b));
                    if ((b < 0) != (c < 0)) edges.Add(Edge(x + 1, y, true, b, c));
                    if ((d < 0) != (c < 0)) edges.Add(Edge(x, y + 1, false, d, c));
                    if ((a < 0) != (d < 0)) edges.Add(Edge(x, y, true, a, d));
                    if (edges.Length == 2) Emit(edges[0], edges[1], x, y, true);
                    else if (edges.Length == 4)
                    {
                        bool adjacent = (sampler.Value(new double2(x + .5, y + .5) * .5) < 0) == (a < 0);
                        // Do not fit two components to the same cell vertex.
                        // Their chord midpoints retain the selected topology.
                        Emit(edges[0], edges[adjacent ? 1 : 3], x, y, false);
                        Emit(edges[adjacent ? 2 : 1], edges[adjacent ? 3 : 2], x, y, false);
                    }
                }
                writer.EndForEachIndex();
            }

            Hermite Edge(int x, int y, bool vertical, float f0, float f1)
            {
                // Identical ordering in both adjoining cells gives identical roots.
                double2 p0 = new double2(x, y) * .5;
                double2 delta = vertical ? new double2(0, .5) : new double2(.5, 0);
                double lo = 0, hi = 1;
                double t;
                if (f0 == 0) t = 0;
                else if (f1 == 0) t = 1;
                else
                {
                    for (int i = 0; i < 14; i++)
                    {
                        double middle = (lo + hi) * .5;
                        if ((sampler.Value(p0 + delta * middle) < 0) == (f0 < 0)) lo = middle;
                        else hi = middle;
                    }
                    t = (lo + hi) * .5;
                }
                double2 p = p0 + delta * t;
                sampler.Sample(p, true, out double2 normal);
                double length = math.length(normal);
                normal = length > 1e-12 && math.all(math.isfinite(normal)) ? normal / length : double2.zero;
                return new Hermite { point = p, normal = normal, key = (y * gridWidth + x) * 2 + (vertical ? 1 : 0) };
            }

            void Emit(Hermite a, Hermite b, int x, int y, bool fit)
            {
                if (math.lengthsq(a.point - b.point) < 1e-20) return;
                double2 point = (a.point + b.point) * .5;
                if (fit) point = SolveQef(a, b, new double2(x, y) * .5);
                writer.Write(new DualVertex { point = (float2)point, root0 = (float2)a.point, root1 = (float2)b.point,
                    key0 = a.key, key1 = b.key });
            }

            static double2 SolveQef(Hermite a, Hermite b, double2 cell)
            {
                double2 center = (a.point + b.point) * .5;
                double2 chord = b.point - a.point;
                double2 fallback = math.normalizesafe(new double2(-chord.y, chord.x));
                if (math.lengthsq(a.normal) < .5) a.normal = fallback;
                if (math.lengthsq(b.normal) < .5) b.normal = fallback;
                // Very small regularization selects the nearest solution for a
                // straight edge, rather than pushing a rank-deficient fit away.
                const double regularization = 1e-8;
                double aa = a.normal.x * a.normal.x + b.normal.x * b.normal.x + regularization;
                double ab = a.normal.x * a.normal.y + b.normal.x * b.normal.y;
                double bb = a.normal.y * a.normal.y + b.normal.y * b.normal.y + regularization;
                double2 rhs = a.normal * math.dot(a.normal, a.point - center) + b.normal * math.dot(b.normal, b.point - center);
                // Solve in cell-local coordinates and test the box boundary:
                // componentwise clamping an unconstrained QEF is not its minimum.
                double2 lower = cell + 1e-5 - center, upper = cell + .5 - 1e-5 - center;
                double det = aa * bb - ab * ab;
                double2 best = math.clamp(double2.zero, lower, upper);
                double error = Error(best, a, b, center);
                if (det > 1e-20)
                {
                    double2 candidate = new double2(bb * rhs.x - ab * rhs.y, aa * rhs.y - ab * rhs.x) / det;
                    if (math.all(candidate >= lower) && math.all(candidate <= upper)) Consider(candidate, a, b, center, ref best, ref error);
                }
                for (int side = 0; side < 2; side++)
                {
                    double vx = side == 0 ? lower.x : upper.x;
                    Consider(new double2(vx, math.clamp((rhs.y - ab * vx) / bb, lower.y, upper.y)), a, b, center, ref best, ref error);
                    double vy = side == 0 ? lower.y : upper.y;
                    Consider(new double2(math.clamp((rhs.x - ab * vy) / aa, lower.x, upper.x), vy), a, b, center, ref best, ref error);
                }
                return center + best;
            }

            static double Error(double2 p, Hermite a, Hermite b, double2 center)
            {
                double da = math.dot(a.normal, p + center - a.point), db = math.dot(b.normal, p + center - b.point);
                return da * da + db * db + 1e-8 * math.lengthsq(p);
            }

            static void Consider(double2 p, Hermite a, Hermite b, double2 center, ref double2 best, ref double error)
            {
                double e = Error(p, a, b, center);
                if (e < error) { best = p; error = e; }
            }
        }
    }
}
