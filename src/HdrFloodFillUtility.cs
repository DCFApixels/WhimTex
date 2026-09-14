using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class HdrFloodFillUtility
    {
        internal static bool Fill(
            NativeArray<Color> source, NativeArray<Color> reference, NativeArray<byte> valid,
            NativeArray<Color> output, int width, int height, int seed, Color color,
            int tolerance, int expand, bool antialias, bool contiguous = true, bool standard = false)
        {
            int length = checked(width * height);
            if (width <= 0 || height <= 0 || source.Length != length || reference.Length != length ||
                valid.Length != length || output.Length != length)
                throw new System.ArgumentException("Fill buffers must match the positive source dimensions.");
            using var region = new NativeArray<byte>(length, Allocator.TempJob);
            using var queue = new NativeArray<int>(contiguous ? length : 0, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var distance = new NativeArray<float>(length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var changed = new NativeArray<int>(1, Allocator.TempJob);
            new RegionJob
            {
                reference = reference, valid = valid, region = region, queue = queue,
                width = width, height = height, seed = seed, tolerance = Mathf.Clamp(tolerance, 0, 255),
                contiguous = contiguous
            }.Run();
            new ExpandJob
            {
                region = region, distance = distance, width = width, height = height,
                propagate = antialias || expand > 0
            }.Run();
            new ColorJob
            {
                source = source, valid = valid, distance = distance, output = output, changed = changed,
                color = standard ? HdrUtility.Saturate(HdrUtility.Safe(color)) : HdrUtility.Safe(color),
                expand = Mathf.Clamp(expand, 0, 32), antialias = antialias, standard = standard
            }.Run();
            return changed[0] != 0;
        }

        [BurstCompile]
        private struct RegionJob : IJob
        {
            [ReadOnly] public NativeArray<Color> reference;
            [ReadOnly] public NativeArray<byte> valid;
            public NativeArray<byte> region;
            public NativeArray<int> queue;
            public int width, height, seed, tolerance;
            public bool contiguous;
            private int tail;
            private Color target;

            public void Execute()
            {
                if (seed < 0 || seed >= region.Length || valid[seed] == 0) return;
                target = HdrUtility.Safe(reference[seed]);
                if (!contiguous)
                {
                    for (int i = 0; i < region.Length; i++) Visit(i);
                    return;
                }
                tail = 0;
                Visit(seed);
                for (int head = 0; head < tail; head++)
                {
                    int index = queue[head];
                    int x = index % width;
                    if (x > 0) Visit(index - 1);
                    if (x + 1 < width) Visit(index + 1);
                    if (index >= width) Visit(index - width);
                    if (index + width < width * height) Visit(index + width);
                }
            }

            private void Visit(int index)
            {
                if (region[index] != 0) return;
                region[index] = 1;
                if (valid[index] == 0) return;
                Color sample = HdrUtility.Safe(reference[index]);
                // Compare to the seed, not the last visited neighbor (which would leak along gradients).
                // Premultiplication ignores hidden RGB in transparent pixels without ignoring alpha edges.
                float limit = tolerance / 255f;
                if (Mathf.Abs(sample.a - target.a) > limit ||
                    Mathf.Abs(sample.r * sample.a - target.r * target.a) > limit ||
                    Mathf.Abs(sample.g * sample.a - target.g * target.a) > limit ||
                    Mathf.Abs(sample.b * sample.a - target.b * target.a) > limit) return;
                region[index] = 2;
                if (contiguous) queue[tail++] = index;
            }
        }

        [BurstCompile]
        private struct ExpandJob : IJob
        {
            [ReadOnly] public NativeArray<byte> region;
            public NativeArray<float> distance;
            public int width, height;
            public bool propagate;

            public void Execute()
            {
                // Two-pass chamfer distance: source-pixel expansion and a soft outer edge, without
                // repeated per-radius dilations. This approximates Euclidean distance on the pixel grid.
                for (int i = 0; i < distance.Length; i++) distance[i] = region[i] == 2 ? 0f : 100000f;
                if (!propagate) return;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    float d = distance[i];
                    if (x > 0) d = Mathf.Min(d, distance[i - 1] + 1f);
                    if (y > 0)
                    {
                        d = Mathf.Min(d, distance[i - width] + 1f);
                        if (x > 0) d = Mathf.Min(d, distance[i - width - 1] + 1.41421356f);
                        if (x + 1 < width) d = Mathf.Min(d, distance[i - width + 1] + 1.41421356f);
                    }
                    distance[i] = d;
                }
                for (int y = height - 1; y >= 0; y--)
                for (int x = width - 1; x >= 0; x--)
                {
                    int i = y * width + x;
                    float d = distance[i];
                    if (x + 1 < width) d = Mathf.Min(d, distance[i + 1] + 1f);
                    if (y + 1 < height)
                    {
                        d = Mathf.Min(d, distance[i + width] + 1f);
                        if (x > 0) d = Mathf.Min(d, distance[i + width - 1] + 1.41421356f);
                        if (x + 1 < width) d = Mathf.Min(d, distance[i + width + 1] + 1.41421356f);
                    }
                    distance[i] = d;
                }
            }
        }

        [BurstCompile]
        private struct ColorJob : IJob
        {
            [ReadOnly] public NativeArray<Color> source;
            [ReadOnly] public NativeArray<byte> valid;
            [ReadOnly] public NativeArray<float> distance;
            public NativeArray<Color> output;
            public NativeArray<int> changed;
            public Color color;
            public int expand;
            public bool antialias, standard;

            public void Execute()
            {
                for (int i = 0; i < source.Length; i++)
                {
                    Color before = source[i];
                    output[i] = before;
                    if (valid[i] == 0) continue;
                    float coverage = antialias ? Mathf.Clamp01(expand + 1.5f - distance[i]) : distance[i] <= expand ? 1f : 0f;
                    float alpha = color.a * coverage;
                    if (alpha <= 0f) continue;
                    Color read = HdrUtility.Safe(before);
                    if (standard) read = HdrUtility.Saturate(read);
                    float retained = read.a * (1f - alpha);
                    float resultAlpha = alpha + retained;
                    Color after = new Color(
                        (color.r * alpha + read.r * retained) / resultAlpha,
                        (color.g * alpha + read.g * retained) / resultAlpha,
                        (color.b * alpha + read.b * retained) / resultAlpha, resultAlpha);
                    output[i] = after;
                    if (after.r != before.r || after.g != before.g || after.b != before.b || after.a != before.a)
                        changed[0] = 1;
                }
            }

        }

        [BurstCompile]
        internal struct CopyReferenceJob : IJobParallelFor
        {
            public bool standard;
            [ReadOnly] public NativeArray<Color> source;
            [WriteOnly] public NativeArray<Color> reference;
            [WriteOnly] public NativeArray<byte> valid;

            public void Execute(int index)
            {
                reference[index] = standard ? HdrUtility.Saturate(source[index]) : source[index];
                valid[index] = 1;
            }
        }

        [BurstCompile]
        internal struct ProjectReferenceJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color> composite;
            [WriteOnly] public NativeArray<Color> reference;
            [WriteOnly] public NativeArray<byte> valid;
            public int width, compositeWidth, compositeHeight;
            public Vector2 origin, stepX, stepY;

            public void Execute(int index)
            {
                Vector2 uv = origin + stepX * (index % width + 0.5f) + stepY * (index / width + 0.5f);
                bool inside = uv.x >= 0f && uv.y >= 0f && uv.x < 1f && uv.y < 1f;
                valid[index] = inside ? (byte)1 : (byte)0;
                reference[index] = inside
                    ? composite[Mathf.Min(Mathf.FloorToInt(uv.y * compositeHeight), compositeHeight - 1) * compositeWidth +
                        Mathf.Min(Mathf.FloorToInt(uv.x * compositeWidth), compositeWidth - 1)]
                    : default;
            }
        }
    }
}
