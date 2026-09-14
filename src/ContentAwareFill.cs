using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Independent managed implementation of PatchMatch propagation/random search and patch voting.
    // No Unity objects or editor APIs are used by the worker.
    internal static class ContentAwareFill
    {
        internal const int MaximumWorkingPixels = 4194304;
        internal sealed class Input
        {
            internal int width, height, seed, quality = 1;
            internal Color[] pixels;
            internal byte[] target, donors;
        }
        internal sealed class Result
        {
            internal int width, height;
            internal Color[] pixels;
            internal byte[] target;
        }
        private sealed class Level
        {
            internal int width, height;
            internal Color[] source, working, spare;
            internal byte[] target, allowed;
            internal bool[] valid;
            internal int[] candidates, matches;
            internal float[] errors;
            internal int radius;
        }
        private struct Random
        {
            internal uint state;
            internal int Next(int count)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                return (int)(state % (uint)count);
            }
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        internal static bool IsDonor(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a) &&
            Math.Abs(c.r) <= 65504 && Math.Abs(c.g) <= 65504 && Math.Abs(c.b) <= 65504 && c.a > .001f && c.a <= 1;

        // Chamfer distances approximate Euclidean distance to the nearest unselected pixel.
        // Image borders also count as selection borders. Width is inward, never an expansion.
        internal static byte[] TargetMask(byte[] selection, Color[] pixels, int width, int height,
            bool innerBorder, int borderWidth, bool transparentOnly, CancellationToken cancel)
        {
            if (selection == null || selection.Length != (long)width * height || pixels.Length != selection.Length)
                throw new ArgumentException("Invalid fill selection dimensions.");
            var mask = (byte[])selection.Clone();
            if (innerBorder)
            {
                borderWidth = Math.Max(1, borderWidth);
                var distance = new float[mask.Length];
                const float diagonal = 1.41421356237f;
                for (int y = 0; y < height; y++)
                {
                    cancel.ThrowIfCancellationRequested();
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        float d = mask[i] == 0 ? 0 : x == 0 || y == 0 || x == width - 1 || y == height - 1 ? 1 : borderWidth + 2;
                        if (x > 0) d = Math.Min(d, distance[i - 1] + 1);
                        if (y > 0)
                        {
                            d = Math.Min(d, distance[i - width] + 1);
                            if (x > 0) d = Math.Min(d, distance[i - width - 1] + diagonal);
                            if (x + 1 < width) d = Math.Min(d, distance[i - width + 1] + diagonal);
                        }
                        distance[i] = d;
                    }
                }
                for (int y = height - 1; y >= 0; y--)
                {
                    cancel.ThrowIfCancellationRequested();
                    for (int x = width - 1; x >= 0; x--)
                    {
                        int i = y * width + x; float d = distance[i];
                        if (x + 1 < width) d = Math.Min(d, distance[i + 1] + 1);
                        if (y + 1 < height)
                        {
                            d = Math.Min(d, distance[i + width] + 1);
                            if (x > 0) d = Math.Min(d, distance[i + width - 1] + diagonal);
                            if (x + 1 < width) d = Math.Min(d, distance[i + width + 1] + diagonal);
                        }
                        distance[i] = d;
                        if (d > borderWidth) mask[i] = 0;
                    }
                }
            }
            if (transparentOnly)
                for (int i = 0; i < mask.Length; i++)
                {
                    if ((i & 16383) == 0) cancel.ThrowIfCancellationRequested();
                    if (pixels[i].a > .001f) mask[i] = 0;
                }
            return mask;
        }

        internal static Result Run(Input input, CancellationToken cancel, Action<float> progress = null)
        {
            int count = checked(input.width * input.height);
            if (input.width < 1 || input.height < 1 || count > MaximumWorkingPixels || input.pixels?.Length != count ||
                input.target?.Length != count || input.donors?.Length != count)
                throw new ArgumentException("Invalid or oversized fill input. Reduce the selection or sampling area.");
            cancel.ThrowIfCancellationRequested();
            var baseLevel = new Level { width = input.width, height = input.height, source = input.pixels,
                target = input.target, allowed = new byte[count] };
            int targets = 0, donors = 0;
            for (int i = 0; i < count; i++)
            {
                if ((i & 16383) == 0) cancel.ThrowIfCancellationRequested();
                if (input.target[i] != 0) targets++;
                else if (input.donors[i] != 0 && IsDonor(input.pixels[i])) { baseLevel.allowed[i] = 255; donors++; }
            }
            if (targets == 0) throw new InvalidOperationException("No pixels to fill. Change the selection, border width or Transparent Only setting.");
            if (donors == 0) throw new InvalidOperationException("No usable source pixels outside the fill area. Widen the sampling area or choose a source containing visible pixels.");
            var levels = new List<Level> { baseLevel };
            while (Math.Max(levels[levels.Count - 1].width, levels[levels.Count - 1].height) > 64)
            {
                cancel.ThrowIfCancellationRequested();
                Level next = Downsample(levels[levels.Count - 1], cancel);
                bool hasDonor = false, hasTarget = false;
                for (int i = 0; i < next.allowed.Length; i++) { hasDonor |= next.allowed[i] != 0; hasTarget |= next.target[i] != 0; }
                if (!hasDonor || !hasTarget) break;
                levels.Add(next);
            }
            var random = new Random { state = unchecked((uint)input.seed * 747796405u + 2891336453u) | 1u };
            Level coarse = null;
            int quality = Math.Max(0, Math.Min(2, input.quality));
            int iterations = quality == 0 ? 3 : quality == 1 ? 5 : 8;
            for (int l = levels.Count - 1; l >= 0; l--)
            {
                Level level = levels[l];
                Prepare(level, coarse, quality == 0 ? 2 : 3, cancel);
                for (int pass = 0; pass < iterations; pass++)
                {
                    Match(level, (pass & 1) == 0, ref random, cancel);
                    Vote(level, cancel);
                    progress?.Invoke((levels.Count - 1 - l + (pass + 1f) / iterations) / levels.Count);
                }
                if (coarse != null)
                { coarse.source = coarse.working = coarse.spare = null; coarse.matches = coarse.candidates = null; coarse.errors = null; coarse.valid = null; }
                coarse = level;
            }
            cancel.ThrowIfCancellationRequested();
            return new Result { width = input.width, height = input.height, pixels = baseLevel.working, target = input.target };
        }
        private static Level Downsample(Level source, CancellationToken cancel)
        {
            int w = (source.width + 1) / 2, h = (source.height + 1) / 2;
            var result = new Level { width = w, height = h, source = new Color[w * h], target = new byte[w * h], allowed = new byte[w * h] };
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++)
                {
                    Color sum = default; int samples = 0; bool allowed = true; byte target = 0;
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = x * 2 + dx, sy = y * 2 + dy;
                        if (sx >= source.width || sy >= source.height) continue;
                        int si = sy * source.width + sx;
                        target = Math.Max(target, source.target[si]); allowed &= source.allowed[si] != 0;
                        if (IsDonor(source.source[si]) && source.target[si] == 0) { sum += source.source[si]; samples++; }
                    }
                    int i = y * w + x;
                    result.source[i] = samples > 0 ? sum / samples : Color.clear;
                    result.target[i] = target; result.allowed[i] = allowed && target == 0 ? (byte)255 : (byte)0;
                }
            }
            return result;
        }
        private static void Prepare(Level level, Level coarse, int radius, CancellationToken cancel)
        {
            int w = level.width, h = level.height, n = w * h;
            level.working = (Color[])level.source.Clone(); level.matches = new int[n]; level.errors = new float[n];
            level.spare = (Color[])level.source.Clone();
            Array.Fill(level.matches, -1);
            var integral = new int[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested(); int row = 0;
                for (int x = 0; x < w; x++) { row += level.allowed[y * w + x] == 0 ? 1 : 0; integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + row; }
            }
            var candidates = new List<int>();
            for (; radius >= 0; radius--)
            {
                candidates.Clear();
                for (int y = radius; y < h - radius; y++)
                {
                    cancel.ThrowIfCancellationRequested();
                    for (int x = radius; x < w - radius; x++)
                    {
                        int x0 = x - radius, x1 = x + radius + 1, y0 = y - radius, y1 = y + radius + 1;
                        if (integral[y1 * (w + 1) + x1] - integral[y0 * (w + 1) + x1] -
                            integral[y1 * (w + 1) + x0] + integral[y0 * (w + 1) + x0] == 0) candidates.Add(y * w + x);
                    }
                }
                if (candidates.Count > 0) break;
            }
            if (candidates.Count == 0) throw new InvalidOperationException("The sampling area contains no valid source patches.");
            level.radius = radius; level.candidates = candidates.ToArray(); level.valid = new bool[n];
            var queue = new int[n]; int head = 0, tail = 0;
            foreach (int i in level.candidates) { level.valid[i] = true; level.matches[i] = i; queue[tail++] = i; }
            while (head < tail)
            {
                if ((head & 4095) == 0) cancel.ThrowIfCancellationRequested();
                int i = queue[head++], x = i % w;
                void Visit(int j) { if (level.matches[j] >= 0) return; level.matches[j] = level.matches[i]; queue[tail++] = j; }
                if (x > 0) Visit(i - 1); if (x + 1 < w) Visit(i + 1);
                if (i >= w) Visit(i - w); if (i + w < n) Visit(i + w);
            }
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (level.target[i] == 0) continue;
                    if (coarse != null)
                    {
                        int c = (y / 2) * coarse.width + x / 2;
                        level.working[i] = coarse.working[c];
                        int match = coarse.matches[c];
                        int sx = Math.Min(w - 1, match % coarse.width * 2 + x % 2), sy = Math.Min(h - 1, match / coarse.width * 2 + y % 2);
                        int guess = sy * w + sx;
                        if (level.valid[guess]) level.matches[i] = guess;
                    }
                    else level.working[i] = level.source[level.matches[i]];
                }
            }
        }
        private static float Cost(Level level, int x, int y, int candidate, float limit)
        {
            int w = level.width, h = level.height, sx = candidate % w, sy = candidate / w, r = level.radius;
            double sum = 0; int samples = 0;
            for (int dy = -r; dy <= r; dy++)
            {
                int ty = y + dy;
                if ((uint)ty >= h) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int tx = x + dx;
                    if ((uint)tx >= w) continue;
                    int i = ty * w + tx;
                    Color a = level.working[i], b = level.source[(sy + dy) * w + sx + dx];
                    if (level.target[i] == 0 && !IsDonor(a)) continue;
                    double weight = level.target[i] == 0 ? 4 : 1;
                    double dr = a.r * (double)a.a - b.r * (double)b.a, dg = a.g * (double)a.a - b.g * (double)b.a;
                    double db = a.b * (double)a.a - b.b * (double)b.a, da = a.a - (double)b.a;
                    sum += weight * (dr * dr + dg * dg + db * db + da * da); samples++;
                    // Compare unnormalized costs: the sampled target positions are the same for every candidate.
                    if (sum >= limit) return limit;
                }
            }
            return samples == 0 ? 0 : (float)Math.Min(float.MaxValue, sum);
        }
        private static void Match(Level level, bool forward, ref Random random, CancellationToken cancel)
        {
            int w = level.width, h = level.height, direction = forward ? 1 : -1;
            for (int y = forward ? 0 : h - 1; (uint)y < h; y += direction)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = forward ? 0 : w - 1; (uint)x < w; x += direction)
                {
                    int i = y * w + x; if (level.target[i] == 0) continue;
                    if ((x & 63) == 0) cancel.ThrowIfCancellationRequested();
                    int best = level.matches[i]; float cost = Cost(level, x, y, best, float.MaxValue);
                    void Try(int sx, int sy)
                    {
                        if ((uint)sx >= w || (uint)sy >= h) return;
                        int candidate = sy * w + sx;
                        if (!level.valid[candidate] || candidate == best) return;
                        float next = Cost(level, x, y, candidate, cost);
                        if (next < cost) { best = candidate; cost = next; }
                    }
                    int nx = x - direction, ny = y - direction;
                    if ((uint)nx < w && level.target[i - direction] != 0)
                    { int match = level.matches[i - direction]; Try(match % w + direction, match / w); }
                    if ((uint)ny < h && level.target[i - direction * w] != 0)
                    { int match = level.matches[i - direction * w]; Try(match % w, match / w + direction); }
                    int global = level.candidates[random.Next(level.candidates.Length)]; Try(global % w, global / w);
                    for (int range = Math.Max(w, h); range >= 1; range /= 2)
                    {
                        int sx = best % w, sy = best / w;
                        Try(Math.Max(0, Math.Min(w - 1, sx + random.Next(range * 2 + 1) - range)),
                            Math.Max(0, Math.Min(h - 1, sy + random.Next(range * 2 + 1) - range)));
                    }
                    level.matches[i] = best; level.errors[i] = cost;
                }
            }
        }
        private static void Vote(Level level, CancellationToken cancel)
        {
            int w = level.width, h = level.height, r = level.radius;
            // A separate result prevents scan-order bias while neighboring patches vote.
            var next = level.spare;
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; if (level.target[i] == 0) continue;
                    double red = 0, green = 0, blue = 0, alpha = 0, weights = 0;
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= w || (uint)ny >= h) continue;
                        int neighbor = ny * w + nx;
                        if (level.target[neighbor] == 0) continue;
                        int match = level.matches[neighbor];
                        Color c = level.source[match - dy * w - dx];
                        double weight = 1 / (1 + Math.Sqrt(level.errors[neighbor]));
                        red += c.r * (double)c.a * weight; green += c.g * (double)c.a * weight;
                        blue += c.b * (double)c.a * weight; alpha += c.a * weight; weights += weight;
                    }
                    next[i] = alpha > 0 && weights > 0 ? new Color((float)(red / alpha), (float)(green / alpha), (float)(blue / alpha), (float)(alpha / weights))
                        : level.source[level.matches[i]];
                }
            }
            level.spare = level.working;
            level.working = next;
        }
    }
}
