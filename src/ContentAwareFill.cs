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
        private const float TextureWeight = 4f;
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
            internal Vector2[] texture, workingTexture, spareTexture;
            internal byte[] target, allowed;
            internal bool[] valid;
            internal int[] candidates, matches;
            internal float[] errors, errorScale;
            internal int[] usageScratch;
            internal float[] reusePenalty;
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
            BuildTextureFeatures(baseLevel, cancel);
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
            int completed = 0, steps = levels.Count * iterations + 1;
            for (int l = levels.Count - 1; l >= 0; l--)
            {
                Level level = levels[l];
                Prepare(level, coarse, quality == 0 ? 2 : 3, cancel);
                if (coarse == null) InitializeFromBoundary(level, random, cancel);
                for (int pass = 0; pass < iterations; pass++)
                {
                    Match(level, (pass & 1) == 0, ref random, cancel);
                    Vote(level, cancel);
                    progress?.Invoke(++completed / (float)steps);
                }
                if (coarse != null)
                {
                    coarse.source = coarse.working = coarse.spare = null;
                    coarse.texture = coarse.workingTexture = coarse.spareTexture = null;
                    coarse.matches = coarse.candidates = null; coarse.errors = coarse.errorScale = null; coarse.valid = null;
                    coarse.usageScratch = null; coarse.reusePenalty = null;
                }
                coarse = level;
            }
            // Re-evaluate matches against the converged image. Best proposals preserve detail;
            // source-aware compatible voting softens patch switches only in smooth neighborhoods.
            // Texture/best-proposal foundation: Newson et al., IPOL 2017, sections 3.2/3.3.
            // The selective blend is our adaptation, not their reconstruction (no reference code reused).
            Match(baseLevel, true, ref random, cancel);
            ReconstructFinal(baseLevel, cancel);
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(1f);
            return new Result { width = input.width, height = input.height, pixels = baseLevel.working, target = input.target };
        }
        private static Level Downsample(Level source, CancellationToken cancel)
        {
            int w = (source.width + 1) / 2, h = (source.height + 1) / 2;
            var result = new Level { width = w, height = h, source = new Color[w * h], texture = new Vector2[w * h], target = new byte[w * h], allowed = new byte[w * h] };
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
                    // Preserve full-resolution texture energy; re-deriving it from blurred colors loses detail.
                    result.texture[i] = source.texture[(y * 2) * source.width + x * 2];
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
            level.workingTexture = (Vector2[])level.texture.Clone(); level.spareTexture = (Vector2[])level.texture.Clone();
            level.errorScale = new float[n];
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
                        int match = coarse.matches[c];
                        int sx = Math.Min(w - 1, match % coarse.width * 2 + x % 2), sy = Math.Min(h - 1, match / coarse.width * 2 + y % 2);
                        int guess = sy * w + sx;
                        if (level.valid[guess]) level.matches[i] = guess;
                    }
                    // Upsample donor coordinates, not the coarse reconstructed colors. Re-seed
                    // from this level's source so lost contrast/detail is not inherited as a target.
                    // An invalid upsampled patch keeps the valid nearest-donor fallback above.
                    level.working[i] = level.source[level.matches[i]];
                    level.workingTexture[i] = level.texture[level.matches[i]];
                    int support = 0;
                    for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
                    {
                        int tx = x + dx, ty = y + dy;
                        if ((uint)tx >= w || (uint)ty >= h) continue;
                        int j = ty * w + tx;
                        if (level.target[j] != 0) support++;
                        else if (IsDonor(level.source[j])) support += 4;
                    }
                    level.errorScale[i] = 1f / Math.Max(1, support);
                }
            }
        }
        private struct PatchSample
        {
            internal int offset;
            internal double red, green, blue, alpha, textureX, textureY, weight;
        }

        // The target patch is identical for every candidate at this pixel. Prepare its bounds,
        // validity and premultiplied values once, preserving the original accumulation order.
        private static int PrepareSamples(Level level, int x, int y, PatchSample[] samples, bool[] known = null)
        {
            int w = level.width, h = level.height, r = level.radius, count = 0;
            for (int dy = -r; dy <= r; dy++)
            {
                int ty = y + dy;
                if ((uint)ty >= h) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int tx = x + dx;
                    if ((uint)tx >= w) continue;
                    int i = ty * w + tx;
                    if (known != null && !known[i]) continue;
                    Color a = level.working[i];
                    if (level.target[i] == 0 && !IsDonor(a)) continue;
                    Vector2 texture = level.workingTexture[i];
                    samples[count++] = new PatchSample
                    {
                        offset = dy * w + dx,
                        red = a.r * (double)a.a, green = a.g * (double)a.a, blue = a.b * (double)a.a,
                        alpha = a.a, textureX = texture.x, textureY = texture.y,
                        weight = level.target[i] == 0 ? 4 : 1
                    };
                }
            }
            return count;
        }

        private static float Cost(Level level, int candidate, PatchSample[] samples, int count, float limit)
        {
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                ref PatchSample sample = ref samples[i];
                int donor = candidate + sample.offset;
                Color b = level.source[donor];
                double dr = sample.red - b.r * (double)b.a, dg = sample.green - b.g * (double)b.a;
                double db = sample.blue - b.b * (double)b.a, da = sample.alpha - b.a;
                Vector2 texture = level.texture[donor];
                double fx = sample.textureX - texture.x, fy = sample.textureY - texture.y;
                sum += sample.weight * (dr * dr + dg * dg + db * db + da * da + TextureWeight * (fx * fx + fy * fy));
                if (sum >= limit) return limit;
            }
            return count == 0 ? 0 : (float)Math.Min(float.MaxValue, sum);
        }
        // Coarsest-level onion peel: compare only known context, then publish a whole ring together.
        // Repaired pixels provide context, never new source donors. Unreachable targets retain the
        // nearest-donor fallback from Prepare (e.g. a hole separated from visible pixels by transparency).
        private static void InitializeFromBoundary(Level level, Random random, CancellationToken cancel)
        {
            if (level.radius == 0) return;
            int w = level.width, h = level.height, n = w * h;
            var known = new bool[n];
            var patch = new PatchSample[(2 * level.radius + 1) * (2 * level.radius + 1)];
            var queued = new bool[n];
            var queue = new int[n];
            int head = 0, tail = 0;
            for (int i = 0; i < n; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                known[i] = level.target[i] == 0 && IsDonor(level.source[i]);
            }
            void EnqueueNeighbors(int i)
            {
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= w || (uint)ny >= h) continue;
                    int j = ny * w + nx;
                    if (level.target[j] == 0 || queued[j]) continue;
                    queued[j] = true; queue[tail++] = j;
                }
            }
            for (int i = 0; i < n; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (known[i]) EnqueueNeighbors(i);
            }
            while (head < tail)
            {
                int end = tail;
                for (int q = head; q < end; q++)
                {
                    cancel.ThrowIfCancellationRequested();
                    int i = queue[q], x = i % w, y = i / w;
                    int best = level.matches[i];
                    int count = PrepareSamples(level, x, y, patch, known);
                    float error = Cost(level, best, patch, count, float.MaxValue);
                    void Try(int candidate)
                    {
                        if (!level.valid[candidate] || candidate == best) return;
                        float next = Cost(level, candidate, patch, count, error);
                        if (next < error) { best = candidate; error = next; }
                    }
                    // Continue offsets from earlier rings, but never read another pixel in this ring.
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= w || (uint)ny >= h || !known[ny * w + nx]) continue;
                        int match = level.matches[ny * w + nx];
                        int sx = match % w - dx, sy = match / w - dy;
                        if ((uint)sx < w && (uint)sy < h) Try(sy * w + sx);
                    }
                    // Exhaustive for small candidate sets; stratified, seed-controlled coverage otherwise.
                    // The cap also bounds work if sparse donors prevent building a small pyramid level.
                    int samples = Math.Min(512, level.candidates.Length);
                    for (int s = 0; s < samples && error > 0; s++)
                    {
                        if ((s & 63) == 0) cancel.ThrowIfCancellationRequested();
                        int start = (int)((long)s * level.candidates.Length / samples);
                        int stop = (int)((long)(s + 1) * level.candidates.Length / samples);
                        Try(level.candidates[start + random.Next(stop - start)]);
                    }
                    level.matches[i] = best;
                }
                for (int q = head; q < end; q++)
                {
                    if ((q & 255) == 0) cancel.ThrowIfCancellationRequested();
                    int i = queue[q], donor = level.matches[i];
                    level.working[i] = level.source[donor];
                    level.workingTexture[i] = level.texture[donor];
                    known[i] = true;
                    EnqueueNeighbors(i);
                }
                head = end;
            }
        }

        // Freeze source-pixel coverage for the entire pass. Count patch footprints, not just
        // their centers: shifting a heavily reused patch by one pixel must not reset its usage.
        // Normal overlapping patches in a coherent copy are free, as are repeated offsets.
        private static bool BuildReusePenalty(Level level, CancellationToken cancel)
        {
            int r = level.radius;
            if (r == 0) return false;
            int w = level.width, h = level.height, n = w * h;
            var sums = level.usageScratch ??= new int[n];
            var penalties = level.reusePenalty ??= new float[n];
            Array.Clear(sums, 0, n);
            int targets = 0, donors = 0;
            for (int i = 0; i < n; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (level.allowed[i] != 0) donors++;
                if (level.target[i] == 0) continue;
                targets++;
                int match = level.matches[i], x = match % w, y = match / w;
                int x0 = x - r, x1 = x + r + 1, y0 = y - r, y1 = y + r + 1;
                sums[y0 * w + x0]++;
                if (x1 < w) sums[y0 * w + x1]--;
                if (y1 < h) sums[y1 * w + x0]--;
                if (x1 < w && y1 < h) sums[y1 * w + x1]++;
            }
            // Rectangle differences -> coverage -> integral coverage, in the same scratch buffer.
            // At the working-pixel limit even 49 samples per target fit into an Int32.
            for (int pass = 0; pass < 2; pass++)
                for (int y = 0; y < h; y++)
                {
                    cancel.ThrowIfCancellationRequested();
                    int row = 0;
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        row += sums[i]; sums[i] = row + (y > 0 ? sums[i - w] : 0);
                    }
                }
            int area = (2 * r + 1) * (2 * r + 1);
            double allowance = 2d * area * Math.Max(1d, targets / (double)Math.Max(1, donors));
            bool active = false;
            for (int c = 0; c < level.candidates.Length; c++)
            {
                if ((c & 4095) == 0) cancel.ThrowIfCancellationRequested();
                int candidate = level.candidates[c];
                int x = candidate % w, y = candidate / w, x0 = x - r - 1, y0 = y - r - 1;
                int total = sums[(y + r) * w + x + r];
                if (x0 >= 0) total -= sums[(y + r) * w + x0];
                if (y0 >= 0) total -= sums[y0 * w + x + r];
                if (x0 >= 0 && y0 >= 0) total += sums[y0 * w + x0];
                double average = total / (double)area;
                penalties[candidate] = average > allowance ? (float)(1 - allowance / average) : 0;
                active |= penalties[candidate] > 0;
            }
            return active;
        }

        private struct MatchCandidate
        {
            internal int donor;
            internal float cost;
        }

        private static int SelectReusableCandidate(MatchCandidate[] candidates, int count, int best, float[] penalties)
        {
            float minimum = candidates[best].cost;
            double limit = minimum * 1.15d;
            double score = minimum + minimum * .2d * penalties[candidates[best].donor];
            int selected = best;
            for (int i = 0; i < count; i++)
            {
                if (candidates[i].cost > limit) continue;
                double next = candidates[i].cost + minimum * .2d * penalties[candidates[i].donor];
                if (next < score) { selected = i; score = next; }
            }
            return selected;
        }

        private static void Match(Level level, bool forward, ref Random random, CancellationToken cancel)
        {
            int w = level.width, h = level.height, direction = forward ? 1 : -1;
            var patch = new PatchSample[(2 * level.radius + 1) * (2 * level.radius + 1)];
            bool balanceReuse = BuildReusePenalty(level, cancel);
            // Initial + two propagation + global + logarithmic random-search proposals.
            // 32 is sufficient even for a one-pixel-wide image at MaximumWorkingPixels.
            var alternatives = balanceReuse ? new MatchCandidate[32] : null;
            for (int y = forward ? 0 : h - 1; (uint)y < h; y += direction)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = forward ? 0 : w - 1; (uint)x < w; x += direction)
                {
                    int i = y * w + x; if (level.target[i] == 0) continue;
                    if ((x & 63) == 0) cancel.ThrowIfCancellationRequested();
                    int count = PrepareSamples(level, x, y, patch);
                    int best = level.matches[i]; float cost = Cost(level, best, patch, count, float.MaxValue);
                    int alternativeCount = 0, appearanceBest = 0;
                    if (balanceReuse) alternatives[alternativeCount++] = new MatchCandidate { donor = best, cost = cost };
                    void Try(int sx, int sy)
                    {
                        if ((uint)sx >= w || (uint)sy >= h) return;
                        int candidate = sy * w + sx;
                        if (!level.valid[candidate] || candidate == best) return;
                        float limit = balanceReuse ? (float)Math.Min(float.MaxValue, cost * 1.15d) : cost;
                        float next = Cost(level, candidate, patch, count, limit);
                        // Cost returns limit when cut short; never retain a clipped estimate.
                        if (next >= limit) return;
                        if (balanceReuse)
                            alternatives[alternativeCount++] = new MatchCandidate { donor = candidate, cost = next };
                        if (next < cost)
                        {
                            best = candidate; cost = next;
                            appearanceBest = alternativeCount - 1;
                        }
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
                    if (balanceReuse)
                    {
                        // Compare against the best appearance found in this pass, not the last
                        // penalized incumbent. This prevents cumulative quality-budget drift.
                        var selected = alternatives[SelectReusableCandidate(alternatives, alternativeCount, appearanceBest, level.reusePenalty)];
                        best = selected.donor; cost = selected.cost;
                    }
                    // Voting and reconstruction use only appearance error, never usage penalties.
                    level.matches[i] = best; level.errors[i] = cost * level.errorScale[i];
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
                    double textureX = 0, textureY = 0;
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= w || (uint)ny >= h) continue;
                        int neighbor = ny * w + nx;
                        if (level.target[neighbor] == 0) continue;
                        int match = level.matches[neighbor];
                        int donor = match - dy * w - dx;
                        Color c = level.source[donor];
                        double weight = 1 / (1 + Math.Sqrt(level.errors[neighbor]));
                        red += c.r * (double)c.a * weight; green += c.g * (double)c.a * weight;
                        blue += c.b * (double)c.a * weight; alpha += c.a * weight; weights += weight;
                        textureX += level.texture[donor].x * weight; textureY += level.texture[donor].y * weight;
                    }
                    next[i] = alpha > 0 && weights > 0 ? new Color((float)(red / alpha), (float)(green / alpha), (float)(blue / alpha), (float)(alpha / weights))
                        : level.source[level.matches[i]];
                    level.spareTexture[i] = weights > 0 ? new Vector2((float)(textureX / weights), (float)(textureY / weights))
                        : level.texture[level.matches[i]];
                }
            }
            level.spare = level.working;
            level.working = next;
            var previousTexture = level.workingTexture;
            level.workingTexture = level.spareTexture; level.spareTexture = previousTexture;
        }

        private static void BuildTextureFeatures(Level level, CancellationToken cancel)
        {
            int w = level.width, h = level.height;
            var gradient = new Vector4[w * h];
            level.texture = new Vector2[w * h];
            bool Known(int i) => level.target[i] == 0 && IsDonor(level.source[i]);
            float Difference(Color a, Color b) => (Math.Abs(a.r * a.a - b.r * b.a) + Math.Abs(a.g * a.a - b.g * b.a) +
                Math.Abs(a.b * a.a - b.b * b.a)) / 3f + Math.Abs(a.a - b.a);
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!Known(i)) continue;
                    if (x + 1 < w && Known(i + 1)) { gradient[i].x = Difference(level.source[i], level.source[i + 1]); gradient[i].z = 1; }
                    if (y + 1 < h && Known(i + w)) { gradient[i].y = Difference(level.source[i], level.source[i + w]); gradient[i].w = 1; }
                }
            }
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++)
                {
                    Vector4 sum = default;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = x + dx, sy = y + dy;
                        if ((uint)sx < w && (uint)sy < h) sum += gradient[sy * w + sx];
                    }
                    level.texture[y * w + x] = new Vector2(sum.z > 0 ? sum.x / sum.z : 0, sum.w > 0 ? sum.y / sum.w : 0);
                }
            }
        }

        private static void ReconstructFinal(Level level, CancellationToken cancel)
        {
            int w = level.width, h = level.height, r = level.radius;
            // Bounded per-run cache (120 KiB), not another full-resolution image buffer.
            // Donor neighborhoods repeat heavily across overlapping proposals.
            var profiles = new ProfileCacheEntry[2048];
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (level.target[i] == 0) continue;
                    int best = level.matches[i]; float error = level.errors[i];
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= w || (uint)ny >= h) continue;
                        int neighbor = ny * w + nx;
                        if (level.target[neighbor] == 0 || level.errors[neighbor] >= error) continue;
                        error = level.errors[neighbor]; best = level.matches[neighbor] - dy * w - dx;
                    }
                    level.working[i] = ReconstructCompatible(level, x, y, best, profiles);
                }
            }
        }

        private struct DonorProfile
        {
            internal Vector4 color, dx, dy;
            internal float variation, smoothness;
        }

        private struct ProfileCacheEntry
        {
            internal int key;
            internal DonorProfile profile;
        }

        private static DonorProfile Profile(Level level, int donor, ProfileCacheEntry[] cache)
        {
            if (cache == null) return BuildProfile(level, donor);
            // Mix row and column bits; low-bit indexing thrashes on power-of-two image widths.
            int slot = (int)(unchecked((uint)donor * 2654435761u) >> 21);
            ref ProfileCacheEntry entry = ref cache[slot];
            if (entry.key != donor + 1) { entry.profile = BuildProfile(level, donor); entry.key = donor + 1; }
            return entry.profile;
        }

        private static Vector4 Premultiplied(Color color) => new Vector4(color.r * color.a,
            color.g * color.a, color.b * color.a, color.a);

        // Classify ORIGINAL donor neighborhoods, never the reconstructed image: a patch switch
        // in the reconstruction is an artifact, not evidence of useful high-frequency detail.
        private static DonorProfile BuildProfile(Level level, int donor)
        {
            var result = new DonorProfile { color = Premultiplied(level.source[donor]) };
            int w = level.width, x = donor % w, y = donor / w;
            if (x == 0 || x == w - 1 || y == 0 || y == level.height - 1 ||
                level.allowed[donor - 1] == 0 || level.allowed[donor + 1] == 0 ||
                level.allowed[donor - w] == 0 || level.allowed[donor + w] == 0) return result;
            Vector4 left = Premultiplied(level.source[donor - 1]) - result.color;
            Vector4 right = Premultiplied(level.source[donor + 1]) - result.color;
            Vector4 down = Premultiplied(level.source[donor - w]) - result.color;
            Vector4 up = Premultiplied(level.source[donor + w]) - result.color;
            result.dx = (right - left) * .5f; result.dy = (up - down) * .5f;
            result.variation = left.sqrMagnitude + right.sqrMagnitude + down.sqrMagnitude + up.sqrMagnitude;
            float curvature = (right + left).sqrMagnitude + (up + down).sqrMagnitude;
            float roughness = result.variation > 1e-20f ? curvature / result.variation : 0;
            float t = Math.Clamp((roughness - .04f) / .46f, 0, 1);
            result.smoothness = 1 - t * t * (3 - 2 * t);
            return result;
        }

        private static Color ReconstructCompatible(Level level, int x, int y, int best, ProfileCacheEntry[] profiles)
        {
            Color original = level.source[best];
            if (level.radius == 0) return original;
            DonorProfile anchor = Profile(level, best, profiles);
            if (anchor.smoothness <= 0 || anchor.variation <= 1e-20f) return original;
            int w = level.width, h = level.height, r = level.radius;
            double red = 0, green = 0, blue = 0, alpha = 0, weights = 0;
            bool different = false;
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= w || (uint)ny >= h) continue;
                int neighbor = ny * w + nx;
                if (level.target[neighbor] == 0) continue;
                int donor = level.matches[neighbor] - dy * w - dx;
                DonorProfile candidate = Profile(level, donor, profiles);
                if (candidate.smoothness <= 0) continue;
                // Compare RGBA and signed spatial changes, not luminance alone. Scale the
                // bandwidth with source variation so HDR and dark images use the same rule.
                float colorDistance = (candidate.color - anchor.color).sqrMagnitude / (anchor.variation * (8 * r * r));
                float patternDistance = ((candidate.dx - anchor.dx).sqrMagnitude +
                    (candidate.dy - anchor.dy).sqrMagnitude) / anchor.variation;
                float distance = colorDistance + patternDistance;
                if (distance >= 1) continue;
                double weight = candidate.smoothness * (1 - distance) * (1 - distance) /
                    (1 + Math.Sqrt(level.errors[neighbor]));
                Color c = level.source[donor];
                red += candidate.color.x * weight; green += candidate.color.y * weight;
                blue += candidate.color.z * weight; alpha += c.a * weight; weights += weight;
                different |= !c.Equals(original);
            }
            if (!different || weights <= 0 || alpha <= 0) return original;
            // Softly retain the best proposal near fine detail; never filter untouched pixels.
            double blend = anchor.smoothness, keep = 1 - blend;
            double a = original.a * keep + alpha / weights * blend;
            return new Color((float)((anchor.color.x * keep + red / weights * blend) / a),
                (float)((anchor.color.y * keep + green / weights * blend) / a),
                (float)((anchor.color.z * keep + blue / weights * blend) / a), (float)a);
        }
    }
}
