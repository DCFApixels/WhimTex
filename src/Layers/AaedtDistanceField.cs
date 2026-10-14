// Adapted from Stefan Gustavson's edtaa3 (Copyright 2009-2012, MIT).
// See ThirdPartyNotices.md for the source revision and full license.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class AaedtDistanceField
    {
        internal static void Compute(NativeArray<Color32> input, NativeArray<float> output,
            int width, int height, byte threshold, int channel)
        {
            using var timing = DistanceFieldTiming.Measure("field.aaedt");
            using var coverage = new NativeArray<float>(input.Length, Allocator.TempJob);
            using var gradients = new NativeArray<float2>(input.Length, Allocator.TempJob);
            using var distances = new NativeArray<float>(input.Length, Allocator.TempJob);
            using var nearest = new NativeArray<int>(input.Length, Allocator.TempJob);
            var prepare = new CoverageJob { input = input, coverage = coverage, threshold = threshold, channel = channel }
                .Schedule(input.Length, 128);
            var gradient = new GradientJob { coverage = coverage, gradients = gradients, width = width, height = height }
                .Schedule(input.Length, 128, prepare);
            new TransformJob { coverage = coverage, gradients = gradients, distances = distances,
                nearest = nearest, output = output, width = width, height = height }.Schedule(gradient).Complete();
        }

        [BurstCompile]
        struct CoverageJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> input;
            [WriteOnly] public NativeArray<float> coverage;
            public int channel;
            public byte threshold;
            public void Execute(int i)
            {
                float value = math.clamp(DistanceFieldSource.Value(input[i], channel), 0f, 255f);
                // Keep 0/255 as empty/full coverage, and place the requested
                // contour at .5. Extreme thresholds have no two-sided AA range.
                coverage[i] = threshold == 0 || threshold == 255 ? (value > threshold ? 1f : 0f)
                    : value <= threshold ? .5f * value / threshold
                    : .5f + .5f * (value - threshold) / (255 - threshold);
            }
        }

        [BurstCompile]
        struct GradientJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> coverage;
            [WriteOnly] public NativeArray<float2> gradients;
            public int width, height;
            float At(int x, int y) => coverage[math.clamp(y, 0, height - 1) * width + math.clamp(x, 0, width - 1)];
            public void Execute(int i)
            {
                if (coverage[i] <= 0 || coverage[i] >= 1) { gradients[i] = default; return; }
                int x = i % width, y = i / width;
                float nw = At(x - 1, y - 1), ne = At(x + 1, y - 1);
                float sw = At(x - 1, y + 1), se = At(x + 1, y + 1);
                const float diagonalWeight = 1.41421356237f;
                gradients[i] = math.normalizesafe(new float2(
                    -nw - diagonalWeight * At(x - 1, y) - sw + ne + diagonalWeight * At(x + 1, y) + se,
                    -nw - diagonalWeight * At(x, y - 1) - ne + sw + diagonalWeight * At(x, y + 1) + se));
            }
        }

        // Inverse area coverage of a straight edge through a unit pixel.
        internal static float EdgeDistance(float2 direction, float coverage)
        {
            float2 g = math.abs(math.normalizesafe(direction));
            float major = math.max(g.x, g.y), minor = math.min(g.x, g.y);
            if (minor == 0) return .5f - coverage;
            float split = .5f * minor / major;
            if (coverage < split) return .5f * (major + minor) - math.sqrt(2f * major * minor * coverage);
            if (coverage < 1f - split) return (.5f - coverage) * major;
            return -.5f * (major + minor) + math.sqrt(2f * major * minor * (1f - coverage));
        }

        [BurstCompile]
        struct TransformJob : IJob
        {
            [ReadOnly] public NativeArray<float> coverage;
            [ReadOnly] public NativeArray<float2> gradients;
            public NativeArray<float> distances, output;
            public NativeArray<int> nearest;
            public int width, height;
            const float Far = 1e10f, Epsilon = .0001f;
            bool inverted;
            float Coverage(int i) => inverted ? 1f - coverage[i] : coverage[i];

            public void Execute()
            {
                bool inside = false, outside = false;
                for (int i = 0; i < coverage.Length; i++)
                {
                    inside |= coverage[i] > .5f;
                    outside |= coverage[i] <= .5f;
                }
                if (!inside || !outside)
                {
                    for (int i = 0; i < output.Length; i++) output[i] = inside ? -Far : Far;
                    return;
                }
                for (int phase = 0; phase < 2; phase++)
                {
                    inverted = phase != 0;
                    for (int i = 0; i < distances.Length; i++)
                    {
                        float a = Coverage(i);
                        nearest[i] = a > 0 ? i : -1;
                        distances[i] = a <= 0 ? Far : a < 1 ? EdgeDistance(gradients[i], a) : 0;
                    }
                    bool changed;
                    do
                    {
                        changed = false;
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int i = y * width + x;
                                if (distances[i] <= 0) continue;
                                if (x > 0) changed |= Update(i, i - 1, x, y);
                                if (y > 0)
                                {
                                    if (x > 0) changed |= Update(i, i - width - 1, x, y);
                                    changed |= Update(i, i - width, x, y);
                                    if (x + 1 < width) changed |= Update(i, i - width + 1, x, y);
                                }
                            }
                            for (int x = width - 2; x >= 0; x--)
                            {
                                int i = y * width + x;
                                if (distances[i] > 0) changed |= Update(i, i + 1, x, y);
                            }
                        }
                        for (int y = height - 1; y >= 0; y--)
                        {
                            for (int x = width - 1; x >= 0; x--)
                            {
                                int i = y * width + x;
                                if (distances[i] <= 0) continue;
                                if (x + 1 < width) changed |= Update(i, i + 1, x, y);
                                if (y + 1 < height)
                                {
                                    if (x + 1 < width) changed |= Update(i, i + width + 1, x, y);
                                    changed |= Update(i, i + width, x, y);
                                    if (x > 0) changed |= Update(i, i + width - 1, x, y);
                                }
                            }
                            for (int x = 1; x < width; x++)
                            {
                                int i = y * width + x;
                                if (distances[i] > 0) changed |= Update(i, i - 1, x, y);
                            }
                        }
                    } while (changed);
                    for (int i = 0; i < output.Length; i++)
                    {
                        float distance = math.max(0, distances[i]);
                        output[i] = phase == 0 ? distance : output[i] - distance;
                    }
                }
            }

            bool Update(int i, int neighbour, int x, int y)
            {
                int seed = nearest[neighbour];
                if (seed < 0 || seed == nearest[i]) return false;
                float2 delta = new float2(x - seed % width, y - seed / width);
                float length = math.length(delta);
                float candidate = length + EdgeDistance(length == 0 ? gradients[seed] : delta, Coverage(seed));
                // Strict descent prevents cycling. Use a tighter update
                // tolerance than edtaa3's .001: rejected small improvements
                // can otherwise hide better seeds further along the sweep.
                // This local tolerance is not a global distance-error bound.
                if (candidate >= distances[i] - Epsilon) return false;
                distances[i] = candidate;
                nearest[i] = seed;
                return true;
            }
        }
    }
}
