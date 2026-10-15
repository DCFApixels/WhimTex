using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Independent, bounded 2D experiment based on Cheng & Gingold,
    // "RBF Your SDF", arXiv:2609.06209v3, equations (1)-(3) and section 4.
    // This is not the authors' implementation. No upstream source is vendored.
    // Adaptations: raster-derived distances, fixed overlapping local patches,
    // per-patch tangent refinement, and half-pixel contour extraction. The final
    // stage uses experimental unrestricted sharp joins, not Euclidean redistancing.
    internal static partial class RbfDistanceField
    {
        const int SampleSide = 5, SampleCount = SampleSide * SampleSide;
        const int MaxCenters = SampleCount * 2, MaxSystem = MaxCenters + 3;
        const int ArcCapacity = SampleCount + 2;
        const int Stride = 4, Iterations = 10;
        const double Scale = 4, Tau = 2 * Math.PI;
        const double DuplicateRadius = 5e-4, FeasibleTolerance = 1e-4;

        internal static void Compute(NativeArray<Color32> input, NativeArray<float> output,
            int width, int height, byte threshold, int channel, float miterLimit = 4,
            SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted, bool dualContouring = false)
        {
            var metric = dualContouring ? DistanceMetric.RbfDual : DistanceMetric.Rbf;
            DistanceFieldUtility.Validate(input.Length, output.Length, width, height, channel, metric, miterLimit, 0);
            // Degenerate one-dimensional input has no 2D reconstruction problem.
            if (width == 1 || height == 1)
            {
                DistanceFieldUtility.ComputeSignedDistance(input, output, width, height, threshold, channel,
                    DistanceMetric.EuclideanAntialiased);
                return;
            }
            using var geometry = Prepare(input, width, height, threshold, channel, Allocator.TempJob, sharpMode, dualContouring);
            ArtisticDistanceField.ComputePrepared(geometry, input, output, width, threshold, channel, metric, miterLimit);
        }

        internal static ArtisticDistanceField.Geometry Prepare(NativeArray<Color32> input,
            int width, int height, byte threshold, int channel, Allocator allocator = Allocator.Persistent,
            SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted, bool dualContouring = false)
        {
            DistanceFieldUtility.Validate(input.Length, input.Length, width, height, channel, DistanceMetric.Rbf, 4, 0);
            if (width == 1 || height == 1) return null;
            using var timing = DistanceFieldTiming.Measure("field.rbf");
            int gridWidth = checked(width * 2 - 1), gridHeight = checked(height * 2 - 1);
            if ((long)gridWidth * gridHeight > 16777216)
                throw new InvalidOperationException("Experimental RBF reconstruction exceeds 16 million refinement samples. Reduce source resolution.");
            using var source = new NativeArray<float>(input.Length, Allocator.TempJob);
            // Finite segment distance is a consistent Euclidean input for the
            // tangent-circle constraints. Alpha itself is not a distance field.
            ArtisticDistanceField.ComputeContour(input, source, width, height, threshold, channel);
            int columns = (width - 1 + Stride - 1) / Stride + 1;
            int rows = (height - 1 + Stride - 1) / Stride + 1;
            int patches = checked(columns * rows);
            using var centers = new NativeArray<double2>(patches * MaxCenters, Allocator.TempJob);
            using var weights = new NativeArray<double>(patches * MaxSystem, Allocator.TempJob);
            using var counts = new NativeArray<int>(patches, Allocator.TempJob);
            using var failures = new NativeArray<int>(patches, Allocator.TempJob);
            using (DistanceFieldTiming.Measure("rbf.fit"))
            {
                using var active = new NativeList<int>(Allocator.TempJob);
                for (int patch = 0; patch < patches; patch++)
                {
                    int cx = math.min(patch % columns * Stride, width - 1);
                    int cy = math.min(patch / columns * Stride, height - 1);
                    // Same boundary band as the solver; inactive models remain empty.
                    if (math.abs(source[cy * width + cx]) <= 8) active.Add(patch);
                }
                // Each lane owns one reusable workspace. Cap total Temp scratch
                // at about 4.6 MiB, without a main-thread wait every 128 patches.
                int lanes = math.min(128, active.Length);
                if (lanes > 0)
                    new FitPatchesJob { source = source, width = width, height = height, columns = columns,
                        active = active.AsArray(), lanes = lanes,
                        centers = centers, weights = weights, counts = counts, failures = failures }
                        .Schedule(lanes, 1).Complete();
            }
            int failed = 0;
            for (int i = 0; i < failures.Length; i++) failed += failures[i];
            if (failed != 0)
                throw new InvalidOperationException("RBF interpolation could not construct a finite local field (" + failed + " patches).");
            using var field = new NativeArray<float>(checked(gridWidth * gridHeight), Allocator.TempJob);
            using (DistanceFieldTiming.Measure("rbf.sample"))
                new SampleFieldJob { source = source, width = width, height = height, columns = columns,
                    rows = rows, centers = centers, weights = weights, counts = counts,
                    field = field, gridWidth = gridWidth }.Schedule(field.Length, 64).Complete();
            using (DistanceFieldTiming.Measure("rbf.prepareSharp"))
            {
                if (dualContouring)
                    return PrepareDualContour(new ContinuousField { source = source, width = width, height = height,
                        columns = columns, rows = rows, centers = centers, weights = weights, counts = counts },
                        field, gridWidth, gridHeight, input, width, height, threshold, channel, allocator, sharpMode);
                return ArtisticDistanceField.PrepareRbfContour(field, gridWidth, gridHeight, input, width, height,
                    threshold, channel, allocator, sharpMode);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct FitPatchesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> source;
            [ReadOnly] public NativeArray<int> active;
            [NativeDisableParallelForRestriction] public NativeArray<double2> centers;
            [NativeDisableParallelForRestriction] public NativeArray<double> weights;
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> counts, failures;
            public int width, height, columns, lanes;

            public void Execute(int local)
            {
                using var points = new NativeArray<double2>(MaxCenters, Allocator.Temp);
                using var values = new NativeArray<double>(MaxCenters, Allocator.Temp);
                using var modelPoints = new NativeArray<double2>(MaxCenters, Allocator.Temp);
                using var modelWeights = new NativeArray<double>(MaxSystem, Allocator.Temp);
                using var solution = new NativeArray<double>(MaxSystem, Allocator.Temp);
                using var matrix = new NativeArray<double>(MaxSystem * (MaxSystem + 1), Allocator.Temp);
                using var arcs = new NativeArray<double2>(SampleCount * ArcCapacity, Allocator.Temp);
                using var arcCounts = new NativeArray<int>(SampleCount, Allocator.Temp);
                using var arcLengths = new NativeArray<double>(SampleCount, Allocator.Temp);
                using var tangents = new NativeArray<double2>(SampleCount, Allocator.Temp);
                using var directions = new NativeArray<double2>(SampleCount, Allocator.Temp);
                using var status = new NativeArray<int>(SampleCount, Allocator.Temp);
                // Interleave the active patches to spread expensive fits across
                // lanes. Every model and every workspace still has one writer.
                for (int index = local; index < active.Length; index += lanes)
                {
                    int patch = active[index];
                    int cx = math.min(patch % columns * Stride, width - 1);
                    int cy = math.min(patch / columns * Stride, height - 1);
                    Fit(patch, cx, cy, points, values, modelPoints, modelWeights, solution, matrix,
                        arcs, arcCounts, arcLengths, tangents, directions, status);
                }
            }

            void Fit(int patch, int cx, int cy, NativeArray<double2> points, NativeArray<double> values,
                NativeArray<double2> modelPoints, NativeArray<double> modelWeights,
                NativeArray<double> solution, NativeArray<double> matrix, NativeArray<double2> arcs,
                NativeArray<int> arcCounts, NativeArray<double> arcLengths, NativeArray<double2> tangents,
                NativeArray<double2> directions, NativeArray<int> status)
            {
                for (int i = 0; i < SampleCount; i++)
                {
                    arcCounts[i] = 0; arcLengths[i] = 0;
                    tangents[i] = default; directions[i] = default; status[i] = 0;
                }
                // All other scratch is written before use below or in Solve.
                int samples = 0;
                for (int y = -4; y <= 4; y += 2)
                for (int x = -4; x <= 4; x += 2)
                {
                    int sx = math.clamp(cx + x, 0, width - 1), sy = math.clamp(cy + y, 0, height - 1);
                    double2 p = new double2(sx - cx, sy - cy) / Scale;
                    if (HasPoint(points, samples, p, 1e-12)) continue;
                    points[samples] = p;
                    values[samples++] = source[sy * width + sx] / Scale;
                }
                if (!Solve(points, values, samples, matrix, solution)) { failures[patch] = 1; return; }
                int fitted = samples;
                CopyModel(points, solution, fitted, modelPoints, modelWeights);
                for (int i = 0; i < samples; i++)
                {
                    double d = values[i];
                    if (math.abs(d) < 1e-10) continue;
                    var exposed = ExposedArcs(points, values, samples, i);
                    arcCounts[i] = exposed.Length;
                    double length = 0;
                    for (int a = 0; a < exposed.Length; a++)
                    {
                        arcs[i * ArcCapacity + a] = exposed[a];
                        length += math.abs(d) * (exposed[a].y - exposed[a].x);
                    }
                    arcLengths[i] = length;
                    // A collapsed exposed region identifies a geometrically
                    // constrained corner; reject remote/mirrored candidates.
                    if (exposed.Length > 0 && length < 1e-8)
                    {
                        double best = .1;
                        for (int a = 0; a < exposed.Length; a++)
                        {
                            double theta = (exposed[a].x + exposed[a].y) * .5;
                            double2 p = points[i] + math.abs(d) * Unit(theta);
                            double error = math.abs(EvaluateValue(modelPoints, modelWeights, fitted, p));
                            if (error < best) { best = error; tangents[i] = p; status[i] = 2; }
                        }
                    }
                }
                // status: 0 no accepted point, 1 projected, 2 fixed corner.
                for (int iteration = 0; iteration <= Iterations; iteration++)
                {
                    if (iteration > 0)
                    for (int i = 0; i < samples; i++)
                    {
                        double d = values[i];
                        if (math.abs(d) < 1e-10 || status[i] == 2 || arcCounts[i] == 0) continue;
                        if (iteration == 1 && arcLengths[i] >= Tau * math.abs(d) - 1e-8) continue;
                        double2 g = directions[i];
                        if (math.lengthsq(g) == 0)
                        {
                            double best = double.PositiveInfinity;
                            for (int k = 0; k < 64; k++)
                            {
                                double2 u = Unit(k * Tau / 64);
                                double f = EvaluateValue(modelPoints, modelWeights, fitted, points[i] - d * u) / d;
                                if (f < best) { best = f; g = u; }
                            }
                        }
                        g = Project(modelPoints, modelWeights, fitted, points[i], d, g, iteration <= 2 ? .2 : 1);
                        double2 candidate = points[i] - d * g;
                        double gap = ClosestArc(arcs, arcCounts[i], i * ArcCapacity, points[i], math.abs(d), candidate, out double2 nearest);
                        directions[i] = g;
                        if (gap <= FeasibleTolerance || (iteration >= 7 && gap < .1 && arcLengths[i] < .1))
                        {
                            tangents[i] = gap <= FeasibleTolerance ? candidate : nearest;
                            status[i] = 1;
                        }
                        // An invalid update never replaces a previously valid point.
                    }
                    int n = samples;
                    for (int pass = 2; pass >= 1; pass--)
                    for (int i = 0; i < samples; i++)
                        if (status[i] == pass && !HasPoint(points, n, tangents[i], DuplicateRadius))
                        { points[n] = tangents[i]; values[n++] = 0; }
                    if (!Solve(points, values, n, matrix, solution))
                        continue; // Keep the last nonsingular RBF, not another SDF mode.
                    fitted = n;
                    CopyModel(points, solution, fitted, modelPoints, modelWeights);
                }
                // A fixed slice per patch keeps the parallel writes disjoint.
                for (int i = 0; i < fitted; i++) centers[patch * MaxCenters + i] = modelPoints[i];
                for (int i = 0; i < fitted + 3; i++) weights[patch * MaxSystem + i] = modelWeights[i];
                counts[patch] = fitted;
            }
        }

        static void CopyModel(NativeArray<double2> p, NativeArray<double> w, int n,
            NativeArray<double2> modelP, NativeArray<double> modelW)
        {
            for (int i = 0; i < n; i++) modelP[i] = p[i];
            for (int i = 0; i < n + 3; i++) modelW[i] = w[i];
        }

        static bool HasPoint(NativeArray<double2> points, int count, double2 p, double radius)
        {
            for (int i = 0; i < count; i++) if (math.lengthsq(points[i] - p) < radius * radius) return true;
            return false;
        }

        static double2 Unit(double angle) => new double2(math.cos(angle), math.sin(angle));

        // Intersect a circle with the exterior of every other sample disk.
        // This small fixed stencil uses all pairs instead of a power diagram.
        static FixedList512Bytes<double2> ExposedArcs(NativeArray<double2> p, NativeArray<double> d, int n, int i)
        {
            var result = new FixedList512Bytes<double2>();
            result.Add(new double2(0, Tau));
            double r = math.abs(d[i]);
            const double angularSlack = 1e-4;
            for (int j = 0; j < n && result.Length > 0; j++)
            {
                if (i == j) continue;
                double2 delta = p[j] - p[i];
                double distance = math.length(delta), other = math.abs(d[j]);
                if (distance < 1e-12) continue;
                double cosine = (distance * distance + r * r - other * other) / (2 * distance * r);
                if (cosine >= 1) continue;
                if (cosine < -1 - angularSlack * angularSlack * .5) { result.Clear(); break; }
                double half = math.max(0, math.acos(math.max(-1, cosine)) - angularSlack);
                double start = math.atan2(delta.y, delta.x) - half;
                start -= math.floor(start / Tau) * Tau;
                double end = start + 2 * half;
                result = SubtractArc(result, start, math.min(Tau, end));
                if (end > Tau) result = SubtractArc(result, 0, end - Tau);
            }
            if (result.Length > 1 && result[0].x == 0 && result[result.Length - 1].y == Tau)
            {
                var first = result[0];
                var last = result[result.Length - 1];
                result.RemoveAt(0);
                result[result.Length - 1] = new double2(last.x, first.y + Tau);
            }
            for (int k = 0; k < result.Length; k++)
            {
                double2 a = result[k];
                if (a.y - a.x >= Tau - 1e-10) continue;
                double shrink = math.min(angularSlack, (a.y - a.x) * .5);
                result[k] = new double2(a.x + shrink, a.y - shrink);
            }
            return result;
        }

        static FixedList512Bytes<double2> SubtractArc(FixedList512Bytes<double2> arcs, double start, double end)
        {
            var result = new FixedList512Bytes<double2>();
            for (int i = 0; i < arcs.Length; i++)
            {
                double2 a = arcs[i];
                if (end < a.x || start > a.y) result.Add(a);
                else
                {
                    if (start >= a.x) result.Add(new double2(a.x, math.min(start, a.y)));
                    if (end <= a.y) result.Add(new double2(math.max(end, a.x), a.y));
                }
            }
            return result;
        }

        static double ClosestArc(NativeArray<double2> arcs, int count, int offset, double2 center, double radius,
            double2 candidate, out double2 closest)
        {
            double theta = math.atan2(candidate.y - center.y, candidate.x - center.x);
            double best = double.PositiveInfinity;
            closest = candidate;
            for (int i = 0; i < count; i++)
            {
                var arc = arcs[offset + i];
                double t = theta - arc.x;
                t -= math.floor(t / Tau) * Tau;
                if (t <= arc.y - arc.x) { closest = candidate; return 0; }
                double2 a = center + radius * Unit(arc.x), b = center + radius * Unit(arc.y);
                double da = math.lengthsq(a - candidate), db = math.lengthsq(b - candidate);
                if (da < best) { best = da; closest = a; }
                if (db < best) { best = db; closest = b; }
            }
            return math.sqrt(best);
        }

        static double2 Project(NativeArray<double2> centers, NativeArray<double> weights, int n,
            double2 p, double d, double2 direction, double maxStep)
        {
            for (int step = 0; step < 10; step++)
            {
                double value = Evaluate(centers, weights, n, p - d * direction, out double2 gradient) / d;
                double2 tangent = gradient - math.dot(gradient, direction) * direction;
                double norm = math.length(tangent);
                if (norm < 1e-4) break;
                double rate = math.min(1, maxStep / norm);
                bool accepted = false;
                for (int trial = 0; trial < 12; trial++)
                {
                    double2 next = math.normalize(direction + rate * tangent);
                    double nextValue = EvaluateValue(centers, weights, n, p - d * next) / d;
                    if (nextValue <= value - 1e-4 * rate * norm * norm)
                    { direction = next; accepted = true; break; }
                    rate *= .5;
                }
                if (!accepted) break;
            }
            return direction;
        }

        // Cubic RBF plus affine polynomial, with all three moment constraints.
        // Double precision and pivoting matter for nearly coincident tangencies.
        static bool Solve(NativeArray<double2> p, NativeArray<double> values, int n,
            NativeArray<double> a, NativeArray<double> solution)
        {
            int size = n + 3, pitch = size + 1;
            for (int i = 0; i < size * pitch; i++) a[i] = 0;
            for (int i = 0; i < n; i++)
            {
                int row = i * pitch;
                for (int j = 0; j < n; j++)
                { double r2 = math.lengthsq(p[i] - p[j]); a[row + j] = r2 * math.sqrt(r2); }
                a[row + n] = a[n * pitch + i] = 1;
                a[row + n + 1] = a[(n + 1) * pitch + i] = p[i].x;
                a[row + n + 2] = a[(n + 2) * pitch + i] = p[i].y;
                a[row + size] = values[i];
            }
            for (int k = 0; k < size; k++)
            {
                int pivot = k;
                for (int i = k + 1; i < size; i++)
                    if (math.abs(a[i * pitch + k]) > math.abs(a[pivot * pitch + k])) pivot = i;
                double divisor = a[pivot * pitch + k];
                if (!math.isfinite(divisor) || math.abs(divisor) < 1e-13) return false;
                if (pivot != k)
                    for (int j = k; j <= size; j++)
                    { double temp = a[k * pitch + j]; a[k * pitch + j] = a[pivot * pitch + j]; a[pivot * pitch + j] = temp; }
                for (int i = k + 1; i < size; i++)
                {
                    double factor = a[i * pitch + k] / divisor;
                    for (int j = k + 1; j <= size; j++) a[i * pitch + j] -= factor * a[k * pitch + j];
                }
            }
            for (int i = size - 1; i >= 0; i--)
            {
                double value = a[i * pitch + size];
                for (int j = i + 1; j < size; j++) value -= a[i * pitch + j] * solution[j];
                solution[i] = value / a[i * pitch + i];
                if (!math.isfinite(solution[i])) return false;
            }
            // Reject numerically unusable fits instead of emitting distant spikes.
            for (int i = 0; i < n; i++)
                if (math.abs(EvaluateValue(p, solution, n, p[i]) - values[i]) > 1e-5) return false;
            return true;
        }

        // Preserve the scalar operation order of Evaluate. Most callers do not
        // need the derivative; only the tangent projection requests both.
        static double EvaluateValue(NativeArray<double2> centers, NativeArray<double> weights, int n,
            double2 p, int centerOffset = 0, int weightOffset = 0)
        {
            double value = weights[weightOffset + n] + weights[weightOffset + n + 1] * p.x + weights[weightOffset + n + 2] * p.y;
            for (int i = 0; i < n; i++)
            {
                double2 delta = p - centers[centerOffset + i];
                double r2 = math.lengthsq(delta), r = math.sqrt(r2), w = weights[weightOffset + i];
                value += w * r2 * r;
            }
            return value;
        }

        static double Evaluate(NativeArray<double2> centers, NativeArray<double> weights, int n,
            double2 p, out double2 gradient, int centerOffset = 0, int weightOffset = 0)
        {
            double value = weights[weightOffset + n] + weights[weightOffset + n + 1] * p.x + weights[weightOffset + n + 2] * p.y;
            gradient = new double2(weights[weightOffset + n + 1], weights[weightOffset + n + 2]);
            for (int i = 0; i < n; i++)
            {
                double2 delta = p - centers[centerOffset + i];
                double r2 = math.lengthsq(delta), r = math.sqrt(r2), w = weights[weightOffset + i];
                value += w * r2 * r;
                gradient += (3 * w * r) * delta;
            }
            return value;
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct SampleFieldJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> source;
            [ReadOnly] public NativeArray<double2> centers;
            [ReadOnly] public NativeArray<double> weights;
            [ReadOnly] public NativeArray<int> counts;
            [WriteOnly] public NativeArray<float> field;
            public int width, height, columns, rows, gridWidth;
            public void Execute(int i)
            {
                float2 p = new float2(i % gridWidth, i / gridWidth) * .5f;
                int x = (int)p.x, y = (int)p.y, x1 = math.min(x + 1, width - 1), y1 = math.min(y + 1, height - 1);
                float seed = math.lerp(math.lerp(source[y * width + x], source[y * width + x1], math.frac(p.x)),
                    math.lerp(source[y1 * width + x], source[y1 * width + x1], math.frac(p.x)), math.frac(p.y));
                double sum = 0, total = 0;
                int cellX = x / Stride, cellY = y / Stride;
                for (int py = math.max(0, cellY - 1); py <= math.min(rows - 1, cellY + 2); py++)
                {
                    double qy = ((double)p.y - math.min(py * Stride, height - 1)) / Scale;
                    if (math.abs(qy) >= 1) continue;
                    for (int px = math.max(0, cellX - 1); px <= math.min(columns - 1, cellX + 2); px++)
                    {
                        int patch = py * columns + px, n = counts[patch];
                        if (n == 0) continue;
                        double2 q = new double2(((double)p.x - math.min(px * Stride, width - 1)) / Scale, qy);
                        double r2 = math.lengthsq(q);
                        if (r2 >= 1) continue;
                        double r = math.sqrt(r2);
                        double s = 1 - r, weight = s * s * s * s * (4 * r + 1);
                        sum += weight * EvaluateValue(centers, weights, n, q, patch * MaxCenters, patch * MaxSystem) * Scale;
                        total += weight;
                    }
                }
                field[i] = total > 1e-20 ? (float)(sum / total) : seed;
            }
        }
    }
}
