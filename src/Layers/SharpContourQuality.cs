using System;
using System.Collections.Generic;
using Unity.Mathematics;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    internal static partial class ArtisticDistanceField
    {
        // Raster-only prototype. Agreement between independent support lengths
        // certifies a corner; no source-vector knowledge or display range is used.
        struct AdaptiveCorner
        {
            public int index, radius;
            public float2 point, left, right;
            public float score;
        }

        static int2 CornerKey(float2 p) => (int2)math.round(p * 4096);

        static List<List<float2>> ImproveSharpContours(List<List<float2>> contours, HashSet<int2> reliable)
        {
            using var timing = DistanceFieldTiming.Measure("contour.adaptiveSharp");
            var result = new List<List<float2>>(contours.Count);
            var obstacles = new List<Path64>(contours.Count);
            foreach (var path in contours) obstacles.Add(QuantizeContour(path));
            for (int i = 0; i < contours.Count; i++)
            {
                var local = new HashSet<int2>();
                var fitted = FitAdaptiveCorners(contours[i], local);
                // A local fit must not consume a hole or another small component.
                if (!ReferenceEquals(fitted, contours[i]) && !FittingPreservesHoles(contours[i], fitted, obstacles))
                { fitted = contours[i]; local.Clear(); }
                result.Add(fitted);
                reliable.UnionWith(local);
            }
            result = NormalizeContours(result);
            // Construct uncertain joins once in geometry. Pixel-by-pixel rejection
            // of a miter is not a substitute for a continuous boundary.
            var rounded = new List<List<float2>>(result.Count);
            obstacles.Clear();
            foreach (var path in result) obstacles.Add(QuantizeContour(path));
            foreach (var path in result)
            {
                var cap = RoundUncertainCorners(path, reliable);
                rounded.Add(FittingPreservesHoles(path, cap, obstacles) ? cap : path);
            }
            return rounded;
        }

        static List<float2> FitAdaptiveCorners(List<float2> raw, HashSet<int2> reliable)
        {
            float perimeter = 0;
            var arc = new float[raw.Count + 1];
            for (int i = 0; i < raw.Count; i++) arc[i + 1] = perimeter += math.distance(raw[i], raw[(i + 1) % raw.Count]);
            if (perimeter < 8) return raw;
            int count = (int)Math.Ceiling(perimeter / .5f);
            // Pathological noisy masks keep their contour, rather than allocating
            // unbounded multi-scale fitting work in an interactive prototype.
            if (count > 131072) return raw;
            float step = perimeter / count;
            var samples = new float2[count];
            int segment = 0;
            for (int i = 0; i < count; i++)
            {
                float s = i * step;
                while (segment + 1 < raw.Count && arc[segment + 1] < s) segment++;
                samples[i] = math.lerp(raw[segment], raw[(segment + 1) % raw.Count],
                    (s - arc[segment]) / math.max(1e-10f, arc[segment + 1] - arc[segment]));
            }
            int Wrap(int i) => (i % count + count) % count;
            float2 At(int i) => samples[Wrap(i)];
            int probe = (int)Math.Ceiling(2 / step), maxGap = (int)Math.Ceiling(3 / step);
            var candidates = new List<int>();
            for (int i = 0; i < count; i++)
                if (math.dot(math.normalizesafe(At(i) - At(i - probe)), math.normalizesafe(At(i + probe) - At(i))) < .94f)
                    candidates.Add(i);
            if (candidates.Count == 0) return raw;
            var spans = new List<int>();
            var fits = new List<DistanceContourFit.Line[]>();
            foreach (int length in new[] { 2, 4, 8, 16 })
            {
                int span = (int)Math.Ceiling(length / step);
                if (2 * (span + maxGap) >= count) break;
                var required = new bool[count];
                foreach (int i in candidates)
                    for (int gap = 1; gap <= maxGap; gap++)
                        required[Wrap(i - gap - span)] = required[Wrap(i + gap)] = true;
                spans.Add(span); fits.Add(DistanceContourFit.Compute(samples, span, required));
            }
            var proposals = new List<AdaptiveCorner>();
            foreach (int i in candidates)
            {
                AdaptiveCorner best = new AdaptiveCorner { score = float.MaxValue };
                for (int gap = 1; gap <= maxGap; gap++) for (int scale = 1; scale < spans.Count; scale++)
                {
                    int shorter = spans[scale - 1], longer = spans[scale];
                    var l0 = fits[scale - 1][Wrap(i - gap - shorter)]; var r0 = fits[scale - 1][Wrap(i + gap)];
                    var l1 = fits[scale][Wrap(i - gap - longer)]; var r1 = fits[scale][Wrap(i + gap)];
                    if (!l0.valid || !r0.valid || !l1.valid || !r1.valid) continue;
                    if (math.dot(l0.direction, l1.direction) < .995f || math.dot(r0.direction, r1.direction) < .995f) continue;
                    if (!IntersectAdaptive(l0, r0, out float2 p0) || !IntersectAdaptive(l1, r1, out float2 point)) continue;
                    // AA uncertainty is measured ACROSS the two observed walls.
                    // Testing only Euclidean tip displacement unfairly rejects
                    // acute intersections: a small normal error is amplified
                    // along their bisector. Still bound longitudinal ambiguity
                    // by the actually observed support, not by display distance.
                    float2 shift = p0 - point;
                    float agreement = math.max(math.abs(CrossAdaptive(shift, l1.direction)), math.abs(CrossAdaptive(shift, r1.direction)));
                    if (agreement > .2f || math.length(shift) > math.min(2, longer * step * .25f)) continue;
                    float cosine = math.length(l1.direction + r1.direction) * .5f;
                    float turn = CrossAdaptive(l1.direction, r1.direction);
                    // Reject smooth, locally straight fits; no artistic minimum angle.
                    if (math.dot(l1.direction, r1.direction) > .94f || math.abs(turn) < 1e-5f) continue;
                    int radius = gap + math.max(1, (int)Math.Ceiling(1 / step));
                    float2 left = l1.center + math.dot(At(i - radius) - l1.center, l1.direction) * l1.direction;
                    float2 right = r1.center + math.dot(At(i + radius) - r1.center, r1.direction) * r1.direction;
                    if (math.dot(point - left, l1.direction) <= .2f || math.dot(right - point, r1.direction) <= .2f) continue;
                    float error = 0;
                    for (int k = -radius; k <= radius; k++)
                        error = math.max(error, math.min(SquaredSegmentDistance(At(i + k), left, point), SquaredSegmentDistance(At(i + k), point, right)));
                    if (error > .55f * .55f) continue;
                    float capDistance = float.MaxValue;
                    for (int k = -gap; k < gap; k++) capDistance = math.min(capDistance, SquaredSegmentDistance(point, At(i + k), At(i + k + 1)));
                    float extrapolation = math.min(longer * step * .5f, .75f / math.max(.05f, cosine));
                    if (capDistance > extrapolation * extrapolation) continue;
                    // Prefer scale agreement and raster residual over angle size.
                    float score = agreement + math.sqrt(error) + math.sqrt(l1.error + r1.error) + .025f * gap - .005f * longer;
                    if (score >= best.score) continue;
                    best = new AdaptiveCorner { index = i, radius = radius, point = point, left = left, right = right, score = score };
                }
                if (best.score < float.MaxValue) proposals.Add(best);
            }
            proposals.Sort((a, b) => { int order = a.score.CompareTo(b.score); return order != 0 ? order : a.index.CompareTo(b.index); });
            var occupied = new bool[count];
            var selected = new List<AdaptiveCorner>();
            int comparisons = 2000000;
            foreach (var proposal in proposals)
            {
                if (comparisons <= 0) break;
                bool blocked = false;
                for (int k = -proposal.radius - 1; k <= proposal.radius + 1; k++) blocked |= occupied[Wrap(proposal.index + k)];
                if (blocked || !AdaptivePatchSafe(raw, arc, perimeter, proposal, step, ref comparisons)) continue;
                for (int k = -proposal.radius; k <= proposal.radius; k++) occupied[Wrap(proposal.index + k)] = true;
                selected.Add(proposal);
            }
            if (selected.Count == 0) return raw;
            // Begin at a gap so a replacement never straddles the traversal origin.
            int origin = 0;
            while (origin < count && occupied[origin]) origin++;
            if (origin == count) return raw;
            float startArc = origin * step;
            selected.Sort((a, b) => Wrap(a.index - origin).CompareTo(Wrap(b.index - origin)));
            var output = new List<float2>(raw.Count);
            int rawIndex = 0;
            while (rawIndex < raw.Count && arc[rawIndex] < startArc) rawIndex++;
            int cursor = rawIndex;
            float RawArc(int k) => arc[k % raw.Count] + (k / raw.Count) * perimeter;
            foreach (var corner in selected)
            {
                float center = startArc + Wrap(corner.index - origin) * step;
                float from = center - corner.radius * step, to = center + corner.radius * step;
                while (cursor < rawIndex + raw.Count && RawArc(cursor) < from) output.Add(raw[cursor++ % raw.Count]);
                output.Add(At(corner.index - corner.radius));
                if (math.distancesq(output[output.Count - 1], corner.left) > 1e-10f) output.Add(corner.left);
                output.Add(corner.point); reliable.Add(CornerKey(corner.point));
                output.Add(corner.right);
                if (math.distancesq(corner.right, At(corner.index + corner.radius)) > 1e-10f) output.Add(At(corner.index + corner.radius));
                while (cursor < rawIndex + raw.Count && RawArc(cursor) <= to) cursor++;
            }
            while (cursor < rawIndex + raw.Count) output.Add(raw[cursor++ % raw.Count]);
            if (output.Count < 3 || SignedArea(output) * SignedArea(raw) <= 0) { reliable.Clear(); return raw; }
            return output;
        }

        static float CrossAdaptive(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        static bool IntersectAdaptive(DistanceContourFit.Line a, DistanceContourFit.Line b, out float2 point)
        {
            float cross = CrossAdaptive(a.direction, b.direction);
            point = math.abs(cross) > 1e-5f ? a.center + CrossAdaptive(b.center - a.center, b.direction) / cross * a.direction : default;
            return math.abs(cross) > 1e-5f && math.all(math.isfinite(point));
        }

        static bool CrossesAdaptive(float2 a, float2 b, float2 c, float2 d)
        {
            if (math.any(math.max(a, b) < math.min(c, d)) || math.any(math.max(c, d) < math.min(a, b))) return false;
            return CrossAdaptive(b - a, c - a) * CrossAdaptive(b - a, d - a) < -1e-9f &&
                CrossAdaptive(d - c, a - c) * CrossAdaptive(d - c, b - c) < -1e-9f;
        }

        static bool AdaptivePatchSafe(List<float2> raw, float[] arc, float perimeter, AdaptiveCorner corner, float step, ref int comparisons)
        {
            float from = (corner.index - corner.radius) * step, to = (corner.index + corner.radius) * step;
            for (int i = 0; i < raw.Count; i++)
            {
                if (--comparisons < 0) return false;
                float start = arc[i];
                while (start < from - perimeter * .5f) start += perimeter;
                while (start > to + perimeter * .5f) start -= perimeter;
                float end = start + arc[i + 1] - arc[i];
                if (start <= to && end >= from) continue;
                float2 a = raw[i], b = raw[(i + 1) % raw.Count];
                if (CrossesAdaptive(corner.left, corner.point, a, b) || CrossesAdaptive(corner.point, corner.right, a, b)) return false;
            }
            return true;
        }

        static List<float2> RoundUncertainCorners(List<float2> path, HashSet<int2> reliable)
        {
            var result = new List<float2>(path.Count);
            int comparisons = 2000000;
            for (int i = 0; i < path.Count; i++)
            {
                float2 p = path[i], a = path[(i + path.Count - 1) % path.Count], b = path[(i + 1) % path.Count];
                float2 incoming = math.normalizesafe(p - a), outgoing = math.normalizesafe(b - p);
                if (reliable.Contains(CornerKey(p)) || math.dot(incoming, outgoing) > .94f) { result.Add(p); continue; }
                // Quadratic cap, tangent to both existing sides. Its maximum
                // displacement is <= .15px; it never extrapolates a new point.
                float trim = math.min(.3f, .24f * math.min(math.distance(p, a), math.distance(p, b)));
                if (trim < 1e-4f) { result.Add(p); continue; }
                float2 start = p - incoming * trim, end = p + outgoing * trim;
                bool safe = true;
                for (int j = 0; j < path.Count && safe; j++)
                {
                    // No partial acceptance when topology validation runs out
                    // of work budget; finite original geometry is the fallback.
                    if (--comparisons < 0) return path;
                    if (j == i || (j + 1) % path.Count == i) continue;
                    safe = !CrossesAdaptive(start, end, path[j], path[(j + 1) % path.Count]);
                }
                if (!safe) { result.Add(p); continue; }
                for (int k = 0; k <= 4; k++)
                {
                    float t = k * .25f;
                    result.Add((1 - t) * (1 - t) * start + 2 * t * (1 - t) * p + t * t * end);
                }
            }
            return result;
        }
    }
}
