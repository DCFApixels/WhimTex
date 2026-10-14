using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Experimental inverse raster fit. The pixel model is box-filtered coverage,
    // not arbitrary opacity/blur. A failed model comparison keeps the input path.
    internal static class RasterCornerDistanceContour
    {
        internal const float RecoveryRadius = 1.5f;
        const long MaximumEdgeIntegrations = 16000000;
        struct IntegrationBudget
        {
            public long remaining;
            public bool exhausted;
            public bool Spend(long count)
            {
                if (count > remaining) { exhausted = true; return false; }
                remaining -= count;
                return true;
            }
        }
        struct Line { public float2 center, direction; public bool valid; }
        sealed class Patch
        {
            public int first, last;
            public float2 vertex;
            public float gain;
            public List<float2> old;
            public List<Pixel> pixels;
            public float before, maximumBeforeError;
        }
        struct Pixel { public float2 point; public float baseline, target, oldArea; }

        internal static List<float2> Refine(List<float2> raw, NativeArray<Color32> input,
            int width, int height, byte threshold, int channel, HashSet<float2> verified)
            => Refine(raw, input, width, height, threshold, channel, verified, null);

        // allContours contains the current raw loop exactly once, with foreground
        // on the left (outer loops positive, holes negative). Calls are sequential:
        // later loops must see earlier accepted replacements. Null retains the
        // isolated-loop contract, including a hole in an implicit filled exterior.
        internal static List<float2> Refine(List<float2> raw, NativeArray<Color32> input,
            int width, int height, byte threshold, int channel, HashSet<float2> verified,
            List<List<float2>> allContours)
        {
            int n = raw.Count;
            if (n < 8 || n > 16384 || threshold < 16 || threshold > 239) return raw;
            float orientation = math.sign(Area(raw));
            if (orientation == 0) return raw;
            var candidates = new List<Patch>();
            var baseline = new Dictionary<int, float>();
            long compositeEdges = EdgeCount(raw, allContours);
            float background = allContours == null && orientation < 0 ? 1 : 0;
            var budget = new IntegrationBudget { remaining = MaximumEdgeIntegrations };
            int attempts = 0;
            float2 lastCandidate = new float2(-1e20f);
            for (int i = 0; i < n; i++)
            {
                int left = Walk(raw, i, -1, 3), right = Walk(raw, i, 1, 3);
                if (left == right || left == i || right == i) continue;
                float2 incoming = math.normalizesafe(raw[i] - raw[left]);
                float2 outgoing = math.normalizesafe(raw[right] - raw[i]);
                if (math.dot(incoming, outgoing) > .90f || math.distance(lastCandidate, raw[i]) < .65f) continue;
                if (++attempts > 256) break;
                lastCandidate = raw[i];
                Patch best = null;
                foreach (float gap in new[] { .75f, 1.5f, 2.25f })
                {
                    int l1 = Walk(raw, i, -1, gap), r0 = Walk(raw, i, 1, gap);
                    int l0 = Walk(raw, l1, -1, 2.5f), r1 = Walk(raw, r0, 1, 2.5f);
                    int count = (r1 - l0 + n) % n;
                    if (count < 4 || count >= n - 3) continue;
                    Line a = Fit(raw, l0, l1), b = Fit(raw, r0, r1);
                    if (!a.valid || !b.valid) continue;
                    float determinant = Cross(a.direction, b.direction);
                    if (math.abs(determinant) < .04f || math.dot(a.direction, b.direction) > .95f) continue;
                    float2 vertex = a.center + a.direction * (Cross(b.center - a.center, b.direction) / determinant);
                    if (!math.all(math.isfinite(vertex)) || math.distance(vertex, raw[i]) > RecoveryRadius) continue;
                    if (math.dot(vertex - a.center, a.direction) <= 0 || math.dot(vertex - b.center, b.direction) >= 0) continue;
                    // The corner must be stable when the sample window is shortened;
                    // a stair-step or a curved flank must not become a long ray.
                    Line shortA = Fit(raw, l0, l1, .5f, 0);
                    Line shortB = Fit(raw, r0, r1, 0, .5f);
                    if (!shortA.valid || !shortB.valid || math.dot(a.direction, shortA.direction) < .999f ||
                        math.dot(b.direction, shortB.direction) < .999f) continue;
                    var old = new List<float2>(count + 1);
                    for (int k = 0; k <= count; k++) old.Add(raw[(l0 + k) % n]);
                    Patch patch = CreatePatch(raw, old, vertex, l0, r1, baseline, input, width, height, threshold, channel,
                        allContours, compositeEdges, background, ref budget);
                    if (budget.exhausted) return raw;
                    if (patch == null) continue;
                    float score = Error(patch, vertex, ref budget, out float maximumError);
                    if (budget.exhausted) return raw;
                    // A small bounded coordinate search optimizes the actual AA
                    // coverage, not just the intersection of threshold polylines.
                    foreach (float step in new[] { .15f, .05f })
                    {
                        float2 center = vertex;
                        for (int axis = 0; axis < 2; axis++) for (int sign = -1; sign <= 1; sign += 2)
                        {
                            float2 trial = center; trial[axis] += sign * step;
                            if (math.distance(trial, raw[i]) > RecoveryRadius) continue;
                            float error = Error(patch, trial, ref budget, out float trialMaximumError);
                            if (budget.exhausted) return raw;
                            if (error < score) { score = error; vertex = trial; maximumError = trialMaximumError; }
                        }
                    }
                    bool improves = score < patch.before * .85f && patch.before - score >= .002f;
                    // An already correct corner cannot deliver a 15% improvement.
                    // Certify it only when BOTH models agree with every pixel to
                    // one input code step and the replacement does not worsen SSE.
                    // Recheck the emitted flanks after coordinate optimization.
                    float quantum = .5f / math.min(threshold, 255 - threshold) + 1e-5f;
                    bool equivalent = math.distance(vertex, raw[i]) <= .25f &&
                        patch.maximumBeforeError <= quantum && maximumError <= quantum &&
                        score <= patch.before + 1e-7f * patch.pixels.Count &&
                        math.dot(math.normalizesafe(vertex - old[0]), a.direction) >= .999f &&
                        math.dot(math.normalizesafe(old[old.Count - 1] - vertex), b.direction) >= .999f;
                    if (!float.IsFinite(score) || score > .0144f * patch.pixels.Count || !improves && !equivalent) continue;
                    patch.vertex = vertex; patch.gain = patch.before - score;
                    if (best == null || patch.gain > best.gain) best = patch;
                }
                if (best != null) candidates.Add(best);
            }
            if (candidates.Count == 0) return raw;
            candidates.Sort((a, b) => b.gain.CompareTo(a.gain));
            var occupied = new bool[n];
            var pixelUse = new HashSet<int>();
            var accepted = new Dictionary<int, Patch>();
            foreach (Patch patch in candidates)
            {
                int count = (patch.last - patch.first + n) % n;
                bool conflict = false;
                for (int k = 0; k <= count; k++) if (occupied[(patch.first + k) % n]) { conflict = true; break; }
                if (conflict) continue;
                foreach (Pixel p in patch.pixels) if (pixelUse.Contains((int)p.point.y * width + (int)p.point.x)) { conflict = true; break; }
                if (conflict) continue;
                for (int k = 0; k <= count; k++) occupied[(patch.first + k) % n] = true;
                foreach (Pixel p in patch.pixels) pixelUse.Add((int)p.point.y * width + (int)p.point.x);
                accepted.Add(patch.first, patch);
            }
            if (accepted.Count == 0) return raw;
            int start = 0;
            // Start at an anchor, so a patch crossing index zero remains contiguous.
            foreach (int first in accepted.Keys) { start = first; break; }
            var result = new List<float2>();
            for (int consumed = 0; consumed < n;)
            {
                int index = (start + consumed) % n;
                result.Add(raw[index]);
                if (accepted.TryGetValue(index, out Patch patch))
                {
                    result.Add(patch.vertex); verified.Add(patch.vertex);
                    consumed += (patch.last - patch.first + n) % n;
                }
                else consumed++;
            }
            if (Area(result) * orientation <= 0) { verified.Clear(); return raw; }
            return result;
        }

        static Patch CreatePatch(List<float2> raw, List<float2> old, float2 vertex, int first, int last,
            Dictionary<int, float> baseline, NativeArray<Color32> input, int width, int height, byte threshold, int channel,
            List<List<float2>> allContours, long compositeEdges, float background, ref IntegrationBudget budget)
        {
            float2 min = vertex, max = vertex;
            foreach (float2 p in old) { min = math.min(min, p); max = math.max(max, p); }
            int2 lo = math.max(0, (int2)math.floor(min - 1)), hi = math.min(new int2(width - 1, height - 1), (int2)math.ceil(max + 1));
            long pixelCount = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1);
            if (hi.x < lo.x || hi.y < lo.y || pixelCount > 256) return null;
            int fractional = 0;
            // Reject evidence-free windows BEFORE any whole-contour integration.
            for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
            {
                float value = DistanceFieldSource.Value(input[y * width + x], channel);
                if (value > 4 && value < 251) fractional++;
            }
            if (fractional < 3) return null;
            if (!budget.Spend(pixelCount * old.Count)) return null;
            var patch = new Patch { first = first, last = last, vertex = vertex, old = old, pixels = new List<Pixel>((int)pixelCount) };
            for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
            {
                int id = y * width + x;
                var point = new float2(x, y);
                if (!baseline.TryGetValue(id, out float coverage))
                {
                    if (!budget.Spend(compositeEdges)) return null;
                    coverage = CompositeArea(raw, allContours, point, background);
                    baseline.Add(id, coverage);
                }
                float value = DistanceFieldSource.Value(input[id], channel);
                float target = Coverage(value, threshold);
                patch.pixels.Add(new Pixel { point = point, baseline = coverage, target = target, oldArea = PixelArea(old, point) });
                float error = math.saturate(coverage) - target;
                patch.before += error * error;
                patch.maximumBeforeError = math.max(patch.maximumBeforeError, math.abs(error));
            }
            return patch;
        }

        static float Error(Patch patch, float2 vertex, ref IntegrationBudget budget, out float maximumError)
        {
            maximumError = float.PositiveInfinity;
            float2 a = patch.old[0], b = patch.old[patch.old.Count - 1];
            if (math.distance(a, vertex) < 2 || math.distance(b, vertex) < 2) return float.PositiveInfinity;
            if (!budget.Spend(3L * patch.pixels.Count)) return float.PositiveInfinity;
            maximumError = 0;
            float error = 0;
            foreach (Pixel p in patch.pixels)
            {
                float area = EdgeArea(a, vertex, p.point) + EdgeArea(vertex, b, p.point) + EdgeArea(b, a, p.point);
                // Apply the oriented patch delta BEFORE saturating the composite.
                float coverage = math.saturate(p.baseline + area - p.oldArea);
                if (p.target < .01f && coverage > .12f ||
                    p.target > .99f && coverage < .88f) return float.PositiveInfinity;
                float delta = coverage - p.target;
                error += delta * delta;
                maximumError = math.max(maximumError, math.abs(delta));
            }
            return error;
        }

        static long EdgeCount(List<float2> raw, List<List<float2>> allContours)
        {
            if (allContours == null) return raw.Count;
            long count = 0;
            foreach (var path in allContours) count += path.Count;
            return count;
        }

        // Keep the unsaturated signed sum for subsequent local deltas. In
        // particular, two disjoint components can cover different parts of the
        // same pixel; neither may be compared alone against that pixel's alpha.
        static float CompositeArea(List<float2> raw, List<List<float2>> allContours, float2 point, float background)
        {
            if (allContours == null) return background + PixelArea(raw, point);
            double area = 0;
            foreach (var path in allContours) area += PixelArea(path, point);
            return (float)area;
        }

        static float Coverage(float value, byte threshold)
        {
            float t = math.clamp(threshold, 1, 254);
            return value <= t ? .5f * value / t : .5f + .5f * (value - t) / (255 - t);
        }
        static int Walk(List<float2> points, int start, int direction, float distance)
        {
            int i = start, n = points.Count;
            for (int k = 1; k < n; k++)
            {
                int next = (i + direction + n) % n;
                distance -= math.distance(points[i], points[next]); i = next;
                if (distance <= 0) break;
            }
            return i;
        }
        static Line Fit(List<float2> points, int first, int last, float trimStart = 0, float trimEnd = 0)
        {
            var samples = new List<float2>();
            int n = points.Count, count = (last - first + n) % n;
            float total = 0;
            for (int k = 0; k < count; k++) total += math.distance(points[(first + k) % n], points[(first + k + 1) % n]);
            float end = total - trimEnd;
            // Trim in arc-length space, not by skipping complete raster edges.
            // A requested half-pixel trim must not discard a 1.4-pixel diagonal.
            float length = 0, nextSample = trimStart;
            for (int k = 0; k < count; k++)
            {
                float2 a = points[(first + k) % n], b = points[(first + k + 1) % n];
                float d = math.distance(a, b);
                while (nextSample <= length + d && nextSample <= end && d > 1e-7f)
                { samples.Add(math.lerp(a, b, (nextSample - length) / d)); nextSample += .5f; }
                length += d;
            }
            if (samples.Count < 4 || end - trimStart < 1.9f) return default;
            float2 center = 0;
            foreach (float2 p in samples) center += p;
            center /= samples.Count;
            double xx = 0, xy = 0, yy = 0;
            foreach (float2 p in samples) { float2 d = p - center; xx += d.x * d.x; xy += d.x * d.y; yy += d.y * d.y; }
            double angle = .5 * Math.Atan2(2 * xy, xx - yy);
            float2 direction = new float2((float)Math.Cos(angle), (float)Math.Sin(angle));
            if (math.dot(direction, points[last] - points[first]) < 0) direction = -direction;
            float maxError = 0, squared = 0;
            foreach (float2 p in samples) { float e = math.abs(Cross(p - center, direction)); maxError = math.max(maxError, e); squared += e * e; }
            return new Line { center = center, direction = direction, valid = maxError < .25f && squared / samples.Count < .015f };
        }
        internal static float Area(List<float2> path)
        {
            if (path.Count < 3) return 0;
            double sum = 0;
            float2 origin = path[0];
            for (int i = 1; i + 1 < path.Count; i++)
            {
                float2 a = path[i] - origin, b = path[i + 1] - origin;
                sum += (double)a.x * b.y - (double)a.y * b.x;
            }
            return (float)(sum * .5);
        }
        internal static float PixelArea(List<float2> path, float2 center)
        {
            double sum = 0;
            for (int i = 0; i < path.Count; i++) sum += EdgeArea(path[i], path[(i + 1) % path.Count], center);
            return (float)sum;
        }
        // Exact box coverage via Green's theorem, integrating clipped linear
        // edges. Unlike supersampling, small vertex moves have continuous error.
        static float EdgeArea(float2 a, float2 b, float2 center)
        {
            a -= center - .5f; b -= center - .5f;
            double dx = (double)b.x - a.x, dy = (double)b.y - a.y;
            if (Math.Abs(dx) < 1e-12) return 0;
            double t0 = Math.Max(0, Math.Min(-a.x / dx, (1 - a.x) / dx));
            double t1 = Math.Min(1, Math.Max(-a.x / dx, (1 - a.x) / dx));
            if (t1 <= t0) return 0;
            double s0 = t0, s1 = t1;
            if (Math.Abs(dy) > 1e-12)
            {
                s0 = Math.Max(t0, Math.Min(t1, Math.Min(-a.y / dy, (1 - a.y) / dy)));
                s1 = Math.Max(t0, Math.Min(t1, Math.Max(-a.y / dy, (1 - a.y) / dy)));
            }
            double Integral(double from, double to) => .5 * (to - from) *
                (Math.Max(0, Math.Min(1, a.y + from * dy)) + Math.Max(0, Math.Min(1, a.y + to * dy)));
            return (float)(-dx * (Integral(t0, s0) + Integral(s0, s1) + Integral(s1, t1)));
        }
        static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
    }
}
