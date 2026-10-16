using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace DCFApixels.WhimTex
{
    internal static partial class ArtisticDistanceField
    {
        // Independent, bounded distance-aware coloring. Hard constraints are the
        // corner-chain adjacencies; nearby nonadjacent edges supply soft costs.
        // There is deliberately no all-pairs graph or cross-contour recoloring.
        static int[] ColorDistanceContour(List<float2> path, bool closed, List<int> corners)
        {
            int count = path.Count, edgeCount = math.max(0, closed ? count : count - 1);
            var masks = new int[edgeCount];
            for (int i = 0; i < edgeCount; i++) masks[i] = 7;
            if (edgeCount < 2 || corners.Count == 0) return masks;

            var starts = new List<int>(corners.Count + 3);
            if (!closed) starts.Add(0);
            foreach (int corner in corners)
                if (corner >= 0 && corner < edgeCount) starts.Add(corner);
            starts.Sort();
            int unique = 0;
            for (int i = 0; i < starts.Count; i++)
                if (unique == 0 || starts[i] != starts[unique - 1]) starts[unique++] = starts[i];
            if (unique < starts.Count) starts.RemoveRange(unique, starts.Count - unique);
            if (starts.Count == 0) return masks;
            // A one-corner closed loop still needs distinct colors on the two
            // sides of that corner. Add two smooth, artificial chain boundaries.
            if (closed && starts.Count == 1 && edgeCount >= 3)
            {
                int first = starts[0];
                starts.Add((first + math.max(1, edgeCount / 3)) % edgeCount);
                starts.Add((first + math.max(2, 2 * edgeCount / 3)) % edgeCount);
                starts.Sort();
            }
            int chains = starts.Count;
            if (chains < 2) return masks;
            var edgeChains = new int[edgeCount];
            var colors = new int[chains];
            for (int chain = 0; chain < chains; chain++)
            {
                // Start mostly bipartite so proximity relaxation can actually
                // choose the third color. A repeating 0/1/2 pattern locks every
                // vertex of a 3k-cycle against any one-vertex recoloring.
                colors[chain] = chain & 1;
                if (closed && chain == chains - 1 && colors[chain] == colors[0])
                    colors[chain] = 2;
                int end = chain + 1 < chains ? starts[chain + 1] : closed ? starts[0] : edgeCount;
                for (int edge = starts[chain]; ; edge = (edge + 1) % edgeCount)
                {
                    edgeChains[edge] = chain;
                    if (edge + 1 == end || closed && (edge + 1) % edgeCount == end) break;
                }
            }

            // Very complicated contours retain valid topology-only coloring.
            // These bounds affect the optional optimization, not the contour.
            const int maxChains = 16384, neighborsPerChain = 8, maxCells = 32768;
            if (chains <= maxChains)
            {
                var neighbors = new int[chains * neighborsPerChain];
                var distances = new float[neighbors.Length];
                for (int i = 0; i < neighbors.Length; i++) { neighbors[i] = -1; distances[i] = float.MaxValue; }
                float2 minimum = path[0], maximum = path[0];
                for (int i = 1; i < count; i++) { minimum = math.min(minimum, path[i]); maximum = math.max(maximum, path[i]); }
                float2 extent = maximum - minimum;
                float cellSize = math.max(2, math.sqrt(math.max(0, extent.x * extent.y) / maxCells));
                if (math.isfinite(cellSize))
                {
                    var cells = new Dictionary<int2, QualityColorBucket>(math.min(edgeCount, maxCells));
                    for (int edge = 0; edge < edgeCount; edge++)
                    {
                        float2 a = path[edge], b = path[(edge + 1) % count];
                        if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b))) continue;
                        int chain = edgeChains[edge], budget = 96;
                        int steps = (int)math.clamp(math.ceil(math.distance(a, b) / cellSize), 1, 8);
                        int2 previousCell = new int2(int.MinValue);
                        for (int sample = 0; sample <= steps; sample++)
                        {
                            int2 cell = (int2)math.floor((math.lerp(a, b, (float)sample / steps) - minimum) / cellSize);
                            if (math.all(cell == previousCell)) continue;
                            previousCell = cell;
                            for (int y = -1; y <= 1 && budget > 0; y++)
                                for (int x = -1; x <= 1 && budget > 0; x++)
                                {
                                    if (!cells.TryGetValue(cell + new int2(x, y), out var bucket)) continue;
                                    for (int k = 0; k < bucket.edges.Length && budget > 0; k++)
                                    {
                                        int other = bucket.edges[k], otherChain = edgeChains[other];
                                        budget--;
                                        if (chain == otherChain || math.abs(chain - otherChain) == 1 ||
                                            closed && math.abs(chain - otherChain) == chains - 1) continue;
                                        float distance = QualitySegmentDistanceSquared(a, b, path[other], path[(other + 1) % count]);
                                        if (!math.isfinite(distance)) continue;
                                        RememberQualityNeighbor(neighbors, distances, chain, otherChain, distance, neighborsPerChain);
                                        RememberQualityNeighbor(neighbors, distances, otherChain, chain, distance, neighborsPerChain);
                                    }
                                }
                            bool exists = cells.TryGetValue(cell, out var current);
                            if (!exists && cells.Count >= maxCells) continue;
                            current.Add(edge, edgeChains);
                            cells[cell] = current;
                        }
                    }
                    // A few deterministic relaxation sweeps, never violating a
                    // corner adjacency even when soft proximity costs conflict.
                    for (int sweep = 0; sweep < 3; sweep++)
                        for (int ordinal = 0; ordinal < chains; ordinal++)
                        {
                            int chain = (sweep & 1) == 0 ? ordinal : chains - 1 - ordinal;
                            int before = chain > 0 ? colors[chain - 1] : closed ? colors[chains - 1] : -1;
                            int after = chain + 1 < chains ? colors[chain + 1] : closed ? colors[0] : -1;
                            int selected = colors[chain];
                            double best = QualityColorCost(neighbors, distances, colors, chain, selected, neighborsPerChain);
                            for (int color = 0; color < 3; color++)
                            {
                                if (color == before || color == after || color == selected) continue;
                                double cost = QualityColorCost(neighbors, distances, colors, chain, color, neighborsPerChain);
                                if (cost + 1e-8 < best) { best = cost; selected = color; }
                            }
                            colors[chain] = selected;
                        }
                }
            }
            for (int i = 0; i < edgeCount; i++) masks[i] = 7 ^ (1 << colors[edgeChains[i]]);
            return masks;
        }

        struct QualityColorBucket
        {
            public FixedList64Bytes<int> edges;
            public void Add(int edge, int[] chains)
            {
                for (int i = 0; i < edges.Length; i++)
                    if (chains[edges[i]] == chains[edge]) { edges[i] = edge; return; }
                if (edges.Length < 8) edges.Add(edge);
                else edges[edge % 8] = edge;
            }
        }

        static void RememberQualityNeighbor(int[] neighbors, float[] distances, int chain, int other, float distance, int capacity)
        {
            int first = chain * capacity, worst = first;
            for (int i = first; i < first + capacity; i++)
            {
                if (neighbors[i] == other) { distances[i] = math.min(distances[i], distance); return; }
                if (distances[i] > distances[worst]) worst = i;
            }
            if (distance < distances[worst]) { neighbors[worst] = other; distances[worst] = distance; }
        }

        static double QualityColorCost(int[] neighbors, float[] distances, int[] colors, int chain, int color, int capacity)
        {
            double result = 0;
            for (int i = chain * capacity; i < (chain + 1) * capacity; i++)
                if (neighbors[i] >= 0 && colors[neighbors[i]] == color) result += 1.0 / (.25 + distances[i]);
            return result;
        }

        static float QualitySegmentDistanceSquared(float2 a, float2 b, float2 c, float2 d)
        {
            float2 u = b - a, v = d - c, r = c - a;
            double cross = (double)u.x * v.y - (double)u.y * v.x;
            if (math.abs(cross) > 1e-12)
            {
                double t = ((double)r.x * v.y - (double)r.y * v.x) / cross;
                double s = ((double)r.x * u.y - (double)r.y * u.x) / cross;
                if (t >= 0 && t <= 1 && s >= 0 && s <= 1) return 0;
            }
            return math.min(math.min(QualityPointSegmentDistanceSquared(a, c, d), QualityPointSegmentDistanceSquared(b, c, d)),
                math.min(QualityPointSegmentDistanceSquared(c, a, b), QualityPointSegmentDistanceSquared(d, a, b)));
        }

        static float QualityPointSegmentDistanceSquared(float2 p, float2 a, float2 b)
        {
            float2 delta = b - a;
            float length = math.lengthsq(delta);
            return math.distancesq(p, a + delta * (length > 1e-16f ? math.saturate(math.dot(p - a, delta) / length) : 0));
        }

        // Only texture interpolation changes: RGB's median at every integer
        // sample and the true-distance alpha are preserved exactly. The direct
        // reference is this same colored/pseudo field, never a round-distance
        // replacement. Near-boundary queries without a certified sign veto a
        // correction rather than guessing the sign of a reconstructed contour.
        static void CorrectMtsdfQuality(NativeArray<float4> field, Geometry geometry, int width, int height)
        {
            if (width <= 0 || height <= 0 || (long)width * height != field.Length)
                throw new ArgumentException("MTSDF field dimensions do not match.");
            if (geometry == null || geometry.roots.Length == 0 || geometry.rootCosines.Length != geometry.roots.Length) return;
            using var protectedTexels = new NativeArray<byte>(field.Length, Allocator.TempJob);
            using var flags = new NativeArray<byte>(((width + 1) / 2) * ((height + 1) / 2),
                Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var writableProtection = protectedTexels;
            foreach (Feature feature in geometry.features)
            {
                if (feature.vertex != 0 || math.dot(math.normalizesafe(feature.b - feature.a), feature.previousDirection) >= .8660254f) continue;
                float2 point = feature.a;
                if (!math.all(math.isfinite(point)) || point.x < -1 || point.y < -1 || point.x > width || point.y > height) continue;
                int2 first = (int2)math.floor(point);
                // Also cover both sides when a corner is exactly on a texel.
                int left = first.x - (point.x == first.x ? 1 : 0), bottom = first.y - (point.y == first.y ? 1 : 0);
                for (int y = math.max(0, bottom); y <= math.min(height - 1, first.y + 1); y++)
                    for (int x = math.max(0, left); x <= math.min(width - 1, first.x + 1); x++)
                        writableProtection[y * width + x] = 1;
            }
            // Within a parity phase no two changed texels share a bilinear cell.
            // This makes the verified one-texel proposal match the applied one,
            // instead of combining individually safe but collectively bad edits.
            JobHandle correction = default;
            try
            {
                for (int phase = 0; phase < 4; phase++)
                {
                    int columns = (width + 1 - (phase & 1)) / 2;
                    int rows = (height + 1 - (phase >> 1)) / 2;
                    int count = columns * rows;
                    if (count == 0) continue;
                    correction = new MtsdfQualityClassifyJob { field = field, protectedTexels = protectedTexels, flags = flags,
                        features = geometry.features, nodes = geometry.nodes, roots = geometry.roots, cosines = geometry.rootCosines,
                        segments = geometry.segments,
                        width = width, height = height, phase = phase, columns = columns, unrestricted = geometry.sharpMode == SharpDistanceMode.Unrestricted
                    }.Schedule(count, 64, correction);
                    correction = new MtsdfQualityApplyJob { field = field, flags = flags, width = width, columns = columns, phase = phase }
                        .Schedule(count, 128, correction);
                }
            }
            finally { correction.Complete(); }
        }

        struct QualityInterpolation
        {
            public float3 constant, linear, quadratic;
            public float3 Sample(float t) => constant + t * (linear + t * quadratic);
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct MtsdfQualityClassifyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> field;
            [ReadOnly] public NativeArray<byte> protectedTexels;
            [ReadOnly] public NativeArray<Feature> features;
            [ReadOnly] public NativeArray<SegmentParameters> segments;
            [ReadOnly] public NativeArray<Node> nodes;
            [ReadOnly] public NativeArray<int> roots;
            [ReadOnly] public NativeArray<float> cosines;
            [WriteOnly] public NativeArray<byte> flags;
            public int width, height, phase, columns;
            public bool unrestricted;

            public void Execute(int slot)
            {
                // Compact flags describe only this phase; all slots are overwritten.
                flags[slot] = 0;
                int x = 2 * (slot % columns) + (phase & 1), y = 2 * (slot / columns) + (phase >> 1);
                int index = y * width + x;
                if (protectedTexels[index] != 0) return;
                float4 value = field[index];
                if (!math.all(math.isfinite(value)) || math.abs(value.w) <= .75f ||
                    math.cmax(math.abs(value)) >= EmptyDistance * .5f || math.cmax(value.xyz) - math.cmin(value.xyz) < .0001f) return;
                bool improves = false;
                int queries = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 || x + dx < 0 || y + dy < 0 || x + dx >= width || y + dy >= height) continue;
                    if (!CheckDirection(index, new int2(dx, dy), ref improves, ref queries)) return;
                }
                if (improves) flags[slot] = 1;
            }

            bool CheckDirection(int index, int2 direction, ref bool improves, ref int queries)
            {
                bool diagonal = direction.x != 0 && direction.y != 0;
                float4 a = field[index], d = field[index + direction.x + direction.y * width];
                float4 b = diagonal ? field[index + direction.x] : d;
                float4 c = diagonal ? field[index + direction.y * width] : a;
                if (!math.all(math.isfinite(b)) || !math.all(math.isfinite(c)) || !math.all(math.isfinite(d))) return false;
                float3 replacement = new float3(Median(a.xyz)), delta = replacement - a.xyz;
                var oldCurve = new QualityInterpolation { constant = a.xyz,
                    linear = diagonal ? b.xyz + c.xyz - 2 * a.xyz : d.xyz - a.xyz,
                    quadratic = diagonal ? a.xyz - b.xyz - c.xyz + d.xyz : new float3(0) };
                var newCurve = new QualityInterpolation { constant = replacement,
                    linear = oldCurve.linear - (diagonal ? 2 : 1) * delta,
                    quadratic = oldCurve.quadratic + (diagonal ? delta : new float3(0)) };
                var candidates = new FixedList512Bytes<float>();
                QualityCriticalPoints(oldCurve, ref candidates);
                QualityCriticalPoints(newCurve, ref candidates);
                // Additional validation points supplement, not replace, the
                // analytical crossings, zero crossings and quadratic extrema.
                QualityAddCandidate(.25f, ref candidates);
                QualityAddCandidate(.5f, ref candidates);
                QualityAddCandidate(.75f, ref candidates);
                float2 origin = new float2(index % width, index / width);
                for (int k = 0; k < candidates.Length; k++)
                {
                    float t = candidates[k], oldMedian = Median(oldCurve.Sample(t)), newMedian = Median(newCurve.Sample(t));
                    if (math.abs(newMedian - oldMedian) <= .005f) continue;
                    if (!QualityCertifiedSign(a.w, b.w, c.w, d.w, t, diagonal, out bool inside)) return false;
                    // A fixed work budget retains original samples on unusually
                    // complicated cells; it never accepts a partial validation.
                    if (++queries > 64) return false;
                    float reference = Median(EvaluateContours(origin + t * (float2)direction, features, nodes, roots, cosines, inside, unrestricted, segments).xyz);
                    if (!math.isfinite(reference) || math.abs(reference) >= EmptyDistance * .5f ||
                        (inside ? -reference : reference) <= .02f) return false;
                    float oldError = math.abs(oldMedian - reference), newError = math.abs(newMedian - reference);
                    if (newError > oldError + .01f) return false;
                    if (oldError > .05f && newError + .01f < .8f * oldError) improves = true;
                }
                return true;
            }
        }

        static bool QualityCertifiedSign(float a, float b, float c, float d, float t, bool diagonal, out bool inside)
        {
            int sign = 0;
            if (diagonal)
            {
                if (!QualityCertifyAnchor(a, 1.41421356237f * t, ref sign) ||
                    !QualityCertifyAnchor(d, 1.41421356237f * (1 - t), ref sign) ||
                    !QualityCertifyAnchor(b, math.sqrt(t * t + (1 - t) * (1 - t)), ref sign) ||
                    !QualityCertifyAnchor(c, math.sqrt(t * t + (1 - t) * (1 - t)), ref sign))
                { inside = false; return false; }
            }
            else if (!QualityCertifyAnchor(a, t, ref sign) || !QualityCertifyAnchor(d, 1 - t, ref sign))
            { inside = false; return false; }
            inside = sign < 0;
            return sign != 0;
        }

        static bool QualityCertifyAnchor(float value, float radius, ref int sign)
        {
            if (!math.isfinite(value) || math.abs(value) >= EmptyDistance * .5f || math.abs(value) <= radius + .02f) return true;
            int candidate = value < 0 ? -1 : 1;
            if (sign != 0 && sign != candidate) return false;
            sign = candidate;
            return true;
        }

        static void QualityCriticalPoints(QualityInterpolation curve, ref FixedList512Bytes<float> candidates)
        {
            for (int i = 0; i < 3; i++)
            {
                int next = (i + 1) % 3;
                QualityPolynomialRoots(curve.quadratic[i] - curve.quadratic[next], curve.linear[i] - curve.linear[next],
                    curve.constant[i] - curve.constant[next], ref candidates);
                QualityPolynomialRoots(curve.quadratic[i], curve.linear[i], curve.constant[i], ref candidates);
                if (math.abs(curve.quadratic[i]) > 1e-10f)
                    QualityAddCandidate(-curve.linear[i] / (2 * curve.quadratic[i]), ref candidates);
            }
        }

        static void QualityPolynomialRoots(double quadratic, double linear, double constant, ref FixedList512Bytes<float> candidates)
        {
            double scale = math.max(1, math.max(math.abs(quadratic), math.max(math.abs(linear), math.abs(constant))));
            if (math.abs(quadratic) <= 1e-10 * scale)
            {
                if (math.abs(linear) > 1e-10 * scale) QualityAddCandidate((float)(-constant / linear), ref candidates);
                return;
            }
            double discriminant = linear * linear - 4 * quadratic * constant;
            if (discriminant < -1e-12 * scale * scale) return;
            double root = math.sqrt(math.max(0, discriminant));
            double q = -.5 * (linear + (linear < 0 ? -root : root));
            if (math.abs(q) <= 1e-20 * scale) QualityAddCandidate((float)(-.5 * linear / quadratic), ref candidates);
            else
            {
                QualityAddCandidate((float)(q / quadratic), ref candidates);
                QualityAddCandidate((float)(constant / q), ref candidates);
            }
        }

        static void QualityAddCandidate(float t, ref FixedList512Bytes<float> candidates)
        {
            if (!math.isfinite(t) || t <= .0001f || t >= .9999f) return;
            for (int i = 0; i < candidates.Length; i++) if (math.abs(candidates[i] - t) <= .00001f) return;
            // At most 33 entries: two curves, each with six pair roots, six
            // channel-zero roots and three extrema, plus three validation points.
            candidates.Add(t);
        }

        [BurstCompile]
        struct MtsdfQualityApplyJob : IJobParallelFor
        {
            // Each compact slot maps to a unique texel in this parity phase.
            [NativeDisableParallelForRestriction] public NativeArray<float4> field;
            [ReadOnly] public NativeArray<byte> flags;
            public int width, columns, phase;
            public void Execute(int slot)
            {
                if (flags[slot] == 0) return;
                int index = (2 * (slot / columns) + (phase >> 1)) * width + 2 * (slot % columns) + (phase & 1);
                float4 value = field[index];
                value.xyz = new float3(Median(value.xyz));
                field[index] = value;
            }
        }
    }
}
