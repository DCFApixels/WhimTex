using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Automatic, single-loop adaptation of the spring forces in Matusovic,
    // Parakkat and Eisemann, Interactive Depixelization of Pixel Art through
    // Spring Simulation (2023), section 3.3, equations 1-4 and 6:
    // https://doi.org/10.1111/cgf.14743
    // https://graphics.tudelft.nl/Publications-new/2023/MPE23/preprint.pdf
    // This independently written implementation does not reproduce their pixel
    // clustering, shared region network, interactive constraints or Bezier fit.
    internal static class SpringDistanceContour
    {
        const float MaximumTravel = .45f;
        const float ClearanceFraction = .2f;
        const float MaximumStep = .1f;
        const int MaximumIterations = 80;
        const double MinimumLength = 1e-6;

        // Pixel centres have integer coordinates; the final edge closes the loop.
        // Borrowed input is never changed. A rejected/unchanged proposal returns
        // the original list. Accepted output retains vertex count, order and winding.
        // The caller must still validate topology across OTHER contours/holes.
        internal static List<float2> Refine(List<float2> contour, NativeArray<Color32> input,
            int width, int height, byte threshold, int channel)
        {
            if (contour == null || contour.Count < 3 || contour.Count > int.MaxValue / 2 ||
                !input.IsCreated || width < 1 || height < 1 || (long)width * height > input.Length)
                return contour;

            int count = contour.Count;
            var original = contour.ToArray();
            for (int i = 0; i < count; i++)
                if (!Finite(original[i]) || original[i].x < 0 || original[i].y < 0 ||
                    original[i].x > width - 1 || original[i].y > height - 1)
                    return contour;

            var lengths = new float[count];
            var arc = new double[count + 1];
            for (int i = 0; i < count; i++)
            {
                lengths[i] = math.length(original[Next(i, count)] - original[i]);
                if (lengths[i] <= MinimumLength) return contour;
                arc[i + 1] = arc[i] + lengths[i];
            }
            double originalArea = AreaAndCentre(original, out _);
            if (!Finite(originalArea) || Math.Abs(originalArea) < 1e-6) return contour;

            // Every endpoint of an edge moves by at most 20% of that edge's
            // original separation from ANY nonadjacent edge. Both edges together
            // can close at most 40% of the gap: at least 60% survives throughout
            // the simulation, including between iterations. This also protects
            // narrow necks and slits; an area constraint alone cannot protect them.
            // A static BVH avoids a full quadratic edge-pair pass on ordinary loops.
            var index = new EdgeIndex(original);
            var clearance = new float[count];
            for (int i = 0; i < count; i++)
            {
                clearance[i] = index.Clearance(i, MaximumTravel / ClearanceFraction);
                // Touching/self-intersecting input has no safe tube here.
                if (clearance[i] <= 1e-5f) return contour;
            }

            var travel = new float[count];
            var stiffness = new float[count];
            for (int i = 0; i < count; i++)
            {
                int previous = Previous(i, count), next = Next(i, count);
                travel[i] = math.min(MaximumTravel, ClearanceFraction *
                    math.min(math.min(lengths[previous], lengths[i]),
                        math.min(clearance[previous], clearance[i])));
                float2 incoming = (original[i] - original[previous]) / lengths[previous];
                float2 outgoing = (original[next] - original[i]) / lengths[i];
                float2 tangent = incoming + outgoing;
                if (math.lengthsq(tangent) < 1e-6f ||
                    IsCorner(original, arc, i) ||
                    !SourceSupportsBoundary(input, width, height, threshold, channel,
                        original[i], tangent, out stiffness[i]))
                    travel[i] = 0;
            }
            // Near-reversals cannot rotate into overlapping adjacent edges. Pin
            // all three endpoints, including small caps without long flank evidence.
            for (int i = 0; i < count; i++)
            {
                int previous = Previous(i, count), next = Next(i, count);
                float2 incoming = (original[i] - original[previous]) / lengths[previous];
                float2 outgoing = (original[next] - original[i]) / lengths[i];
                if (math.dot(incoming, outgoing) < -.5f)
                    travel[previous] = travel[i] = travel[next] = 0;
            }

            var current = (float2[])original.Clone();
            var proposed = new float2[count];
            var forces = new float2[count];
            for (int iteration = 0; iteration < MaximumIterations; iteration++)
            {
                double area = AreaAndCentre(current, out float2 centre);
                if (!Finite(area) || !Finite(centre) || area * originalArea <= 0) return contour;
                float areaScale = (float)(1 - Math.Sqrt(area / originalArea));
                float maximumForce = 0;
                for (int i = 0; i < count; i++)
                {
                    if (travel[i] == 0) { forces[i] = float2.zero; continue; }
                    int previous = Previous(i, count), next = Next(i, count);
                    float2 offset = original[i] - current[i];
                    // Eq. 1 is a quadratic restoring force, not a linear tether.
                    float2 originForce = stiffness[i] * math.length(offset) * offset;
                    // Eq. 2 with symmetric, fixed per-edge stiffness. Dividing by
                    // initial length compensates for nonuniform marching samples:
                    // an initially straight run has balanced tangential tension.
                    float2 neighbourForce = (current[previous] - current[i]) / math.max(.25f, lengths[previous]) +
                        (current[next] - current[i]) / math.max(.25f, lengths[i]);
                    // Eq. 3 for this loop only; signed-area ratio works for holes
                    // as well as outer loops. The centroid uses shoelace moments.
                    float2 areaForce = (current[i] - centre) * areaScale;
                    forces[i] = originForce + neighbourForce + areaForce;
                    if (!Finite(forces[i])) return contour;
                    maximumForce = math.max(maximumForce, math.length(forces[i]));
                }
                if (maximumForce < 1e-4f) break;
                float step = MaximumStep / math.max(1, maximumForce); // Eq. 6.
                bool accepted = false;
                float moved = 0;
                for (int attempt = 0; attempt < 10; attempt++, step *= .5f)
                {
                    moved = 0;
                    for (int i = 0; i < count; i++)
                    {
                        float2 offset = current[i] + step * forces[i] - original[i];
                        float distance = math.length(offset);
                        proposed[i] = travel[i] == 0 ? original[i] : original[i] +
                            offset * (distance > travel[i] ? travel[i] / distance : 1);
                        // Float addition at large coordinates can round a projected
                        // point outside its tiny clearance budget. Keep the last
                        // admissible location instead of weakening that budget.
                        double dx = (double)proposed[i].x - original[i].x;
                        double dy = (double)proposed[i].y - original[i].y;
                        if (dx * dx + dy * dy > (double)travel[i] * travel[i]) proposed[i] = current[i];
                        moved = math.max(moved, math.lengthsq(proposed[i] - current[i]));
                    }
                    if (Admissible(proposed, original, lengths, originalArea))
                    { accepted = true; break; }
                }
                if (!accepted) break;
                var swap = current; current = proposed; proposed = swap;
                // Projection can converge while constrained nodes still have force.
                if (moved < 1e-8f) break;
            }

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (!Finite(current[i])) return contour;
                changed |= math.any(current[i] != original[i]);
            }
            return changed ? new List<float2>(current) : contour;
        }

        static bool Admissible(float2[] points, float2[] original, float[] lengths, double originalArea)
        {
            double ratio = AreaAndCentre(points, out _) / originalArea;
            // Backtracking bounds area drift; the paper's area force is a soft
            // preference, not an exact conservation or topology guarantee.
            if (!Finite(ratio) || ratio < .95 || ratio > 1.05) return false;
            for (int i = 0; i < points.Length; i++)
            {
                if (!Finite(points[i])) return false;
                int next = Next(i, points.Length), previous = Previous(i, points.Length);
                float2 outgoing = points[next] - points[i];
                if (math.dot(outgoing, original[next] - original[i]) < .5f * lengths[i] * lengths[i])
                    return false;
                float2 incoming = points[i] - points[previous];
                double cross = Cross(incoming, outgoing);
                if (Math.Abs(cross) < 1e-10 && math.dot(incoming, outgoing) < 0) return false;
            }
            return true;
        }

        static bool IsCorner(float2[] points, double[] arc, int i)
        {
            // Use a physical multi-pixel support, never a single raster turn.
            // Localized turns with two stable flanks are anchored; a repeated
            // staircase has parallel far flanks and does not become a corner.
            if (arc[points.Length] < 6) return false;
            float2 p = points[i];
            float2 a = Sample(points, arc, arc[i] - .9);
            float2 b = Sample(points, arc, arc[i] - 1.9);
            float2 c = Sample(points, arc, arc[i] - 2.9);
            float2 d = Sample(points, arc, arc[i] + .9);
            float2 e = Sample(points, arc, arc[i] + 1.9);
            float2 f = Sample(points, arc, arc[i] + 2.9);
            float2 nearIn = Unit(p - a), nearOut = Unit(d - p);
            float2 farIn = Unit(a - c), farOut = Unit(f - d);
            if (math.lengthsq(farIn) < .5f || math.lengthsq(farOut) < .5f) return true;
            float turn = 1 - math.dot(farIn, farOut);
            if (turn < .18f || 1 - math.dot(nearIn, nearOut) < .45f * turn) return false;
            if (math.dot(nearIn, farIn) < .8f || math.dot(nearOut, farOut) < .8f) return false;
            return math.dot(Unit(a - b), Unit(b - c)) >= .92f &&
                math.dot(Unit(e - d), Unit(f - e)) >= .92f &&
                Math.Abs(Cross(b - c, farIn)) <= .25 &&
                Math.Abs(Cross(e - d, farOut)) <= .25;
        }

        static float2 Sample(float2[] points, double[] arc, double distance)
        {
            double perimeter = arc[points.Length];
            if (distance < 0) distance += perimeter;
            else if (distance >= perimeter) distance -= perimeter;
            int lower = 0, upper = points.Length;
            while (lower + 1 < upper)
            {
                int middle = (lower + upper) / 2;
                if (arc[middle] <= distance) lower = middle;
                else upper = middle;
            }
            float t = (float)((distance - arc[lower]) / (arc[lower + 1] - arc[lower]));
            return math.lerp(points[lower], points[Next(lower, points.Length)], t);
        }

        static bool SourceSupportsBoundary(NativeArray<Color32> input, int width, int height,
            byte threshold, int channel, float2 p, float2 tangent, out float stiffness)
        {
            float2 normal = Unit(new float2(-tangent.y, tangent.x));
            float a = Read(input, width, height, channel, p - .625f * normal);
            float b = Read(input, width, height, channel, p + .625f * normal);
            // Read the original texels, not interpolated coverage, when estimating
            // partial-coverage evidence. Otherwise a binary edge looks antialiased.
            int x = math.clamp((int)math.floor(p.x), 0, width - 1);
            int y = math.clamp((int)math.floor(p.y), 0, height - 1);
            int x1 = math.min(x + 1, width - 1), y1 = math.min(y + 1, height - 1);
            float partial = Partial(input[y * width + x], channel) + Partial(input[y * width + x1], channel) +
                Partial(input[y1 * width + x], channel) + Partial(input[y1 * width + x1], channel);
            stiffness = .8f + .3f * partial;
            // Ambiguous or subpixel-thin support is kept fixed locally. Global
            // raster/topology acceptance remains the caller's responsibility.
            return (a > threshold) != (b > threshold);
        }

        static float Partial(Color32 color, int channel)
        {
            float v = DistanceFieldSource.Value(color, channel) / 255f;
            return 4 * v * (1 - v);
        }

        static float Read(NativeArray<Color32> input, int width, int height, int channel, float2 p)
        {
            p = math.clamp(p, float2.zero, new float2(width - 1, height - 1));
            int x = (int)math.floor(p.x), y = (int)math.floor(p.y);
            int x1 = math.min(x + 1, width - 1), y1 = math.min(y + 1, height - 1);
            return math.lerp(
                math.lerp(DistanceFieldSource.Value(input[y * width + x], channel),
                    DistanceFieldSource.Value(input[y * width + x1], channel), p.x - x),
                math.lerp(DistanceFieldSource.Value(input[y1 * width + x], channel),
                    DistanceFieldSource.Value(input[y1 * width + x1], channel), p.x - x), p.y - y);
        }

        static double AreaAndCentre(float2[] points, out float2 centre)
        {
            // Translate before taking moments, then accumulate in double, so a
            // small loop far from the image origin does not lose its area.
            float2 origin = points[0];
            double twiceArea = 0, momentX = 0, momentY = 0;
            for (int i = 0; i < points.Length; i++)
            {
                float2 a = points[i] - origin, b = points[Next(i, points.Length)] - origin;
                double cross = Cross(a, b);
                twiceArea += cross;
                momentX += ((double)a.x + b.x) * cross;
                momentY += ((double)a.y + b.y) * cross;
            }
            centre = Math.Abs(twiceArea) < 1e-12 ? origin :
                origin + new float2((float)(momentX / (3 * twiceArea)), (float)(momentY / (3 * twiceArea)));
            return .5 * twiceArea;
        }

        static float2 Unit(float2 v) => v / math.max(1e-12f, math.length(v));
        static int Previous(int i, int count) => i == 0 ? count - 1 : i - 1;
        static int Next(int i, int count) => i + 1 == count ? 0 : i + 1;
        static double Cross(float2 a, float2 b) => (double)a.x * b.y - (double)a.y * b.x;
        static bool Finite(float2 p) => math.all(math.isfinite(p));
        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        sealed class EdgeIndex
        {
            struct Branch
            {
                internal float2 minimum, maximum;
                internal int start, count, left, right;
            }

            sealed class CentreComparer : IComparer<int>
            {
                readonly float2[] points;
                readonly int axis;
                internal CentreComparer(float2[] points, int axis) { this.points = points; this.axis = axis; }
                public int Compare(int a, int b)
                {
                    float ca = points[a][axis] + points[Next(a, points.Length)][axis];
                    float cb = points[b][axis] + points[Next(b, points.Length)][axis];
                    int result = ca.CompareTo(cb);
                    return result != 0 ? result : a.CompareTo(b);
                }
            }

            readonly float2[] points;
            readonly int[] order;
            readonly Branch[] branches;
            readonly CentreComparer byX, byY;
            int used;

            internal EdgeIndex(float2[] points)
            {
                this.points = points;
                order = new int[points.Length];
                branches = new Branch[points.Length * 2];
                byX = new CentreComparer(points, 0); byY = new CentreComparer(points, 1);
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Build(0, points.Length);
            }

            int Build(int start, int count)
            {
                int id = used++;
                float2 minimum = new float2(float.PositiveInfinity), maximum = new float2(float.NegativeInfinity);
                for (int i = start; i < start + count; i++)
                {
                    int edge = order[i];
                    minimum = math.min(minimum, math.min(points[edge], points[Next(edge, points.Length)]));
                    maximum = math.max(maximum, math.max(points[edge], points[Next(edge, points.Length)]));
                }
                var branch = new Branch { minimum = minimum, maximum = maximum, start = start, count = count };
                if (count > 8)
                {
                    float2 extent = maximum - minimum;
                    Array.Sort(order, start, count, extent.x >= extent.y ? byX : byY);
                    int half = count / 2;
                    branch.left = Build(start, half); branch.right = Build(start + half, count - half);
                    branch.count = 0;
                }
                branches[id] = branch;
                return id;
            }

            internal float Clearance(int edge, float limit)
            {
                float2 a = points[edge], b = points[Next(edge, points.Length)];
                double squared = (double)limit * limit;
                Query(0, edge, a, b, math.min(a, b), math.max(a, b), ref squared);
                return (float)Math.Sqrt(squared);
            }

            void Query(int id, int edge, float2 a, float2 b, float2 minimum, float2 maximum, ref double squared)
            {
                Branch branch = branches[id];
                float2 gap = math.max(float2.zero, math.max(minimum - branch.maximum, branch.minimum - maximum));
                if ((double)gap.x * gap.x + (double)gap.y * gap.y >= squared) return;
                if (branch.count == 0)
                {
                    Query(branch.left, edge, a, b, minimum, maximum, ref squared);
                    if (squared > 0) Query(branch.right, edge, a, b, minimum, maximum, ref squared);
                    return;
                }
                int previous = Previous(edge, points.Length), next = Next(edge, points.Length);
                for (int i = branch.start; i < branch.start + branch.count; i++)
                {
                    int other = order[i];
                    if (other == edge || other == previous || other == next) continue;
                    float2 c = points[other], d = points[Next(other, points.Length)];
                    squared = Math.Min(squared, SegmentDistanceSquared(a, b, c, d));
                    if (squared == 0) return;
                }
            }
        }

        static double SegmentDistanceSquared(float2 a, float2 b, float2 c, float2 d)
        {
            if (math.all(math.max(math.min(a, b), math.min(c, d)) <= math.min(math.max(a, b), math.max(c, d))))
            {
                double abC = Orientation(a, b, c), abD = Orientation(a, b, d);
                double cdA = Orientation(c, d, a), cdB = Orientation(c, d, b);
                if (((abC <= 0 && abD >= 0) || (abC >= 0 && abD <= 0)) &&
                    ((cdA <= 0 && cdB >= 0) || (cdA >= 0 && cdB <= 0))) return 0;
            }
            return Math.Min(Math.Min(PointDistanceSquared(a, c, d), PointDistanceSquared(b, c, d)),
                Math.Min(PointDistanceSquared(c, a, b), PointDistanceSquared(d, a, b)));
        }

        static double PointDistanceSquared(float2 p, float2 a, float2 b)
        {
            double dx = (double)b.x - a.x, dy = (double)b.y - a.y;
            double px = (double)p.x - a.x, py = (double)p.y - a.y;
            double length = dx * dx + dy * dy;
            double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, (px * dx + py * dy) / length));
            double x = px - t * dx, y = py - t * dy;
            return x * x + y * y;
        }

        static double Orientation(float2 a, float2 b, float2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);
    }
}
