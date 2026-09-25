using System;
using System.Threading;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum HealingSampleMode { CurrentLayer, [InspectorName("Current & Below")] CurrentAndBelow }
    internal enum HealingQuality { Fast, Balanced, High }

    internal static class HealingBrushUtility
    {
        internal const int MaximumWorkingPixels = 1048576;

        internal static void CheckWorkingSize(RectInt bounds)
        {
            if ((long)bounds.width * bounds.height > MaximumWorkingPixels)
                throw new InvalidOperationException("Use a shorter stroke or a smaller brush/search area (maximum 1 million working pixels).");
        }

        // A full-axis crop cannot use the untouched gap from stroke bounds. Cut through
        // the least painted band instead, so opposite sides of a repaired seam remain adjacent.
        internal static RectInt RecenterTiledRegion(RectInt bounds, ref byte[] coverage, int canvasWidth, int canvasHeight)
        {
            bool fullX = bounds.width == canvasWidth, fullY = bounds.height == canvasHeight;
            if (!fullX && !fullY) return bounds;
            int w = bounds.width, h = bounds.height;
            var columns = fullX ? new int[w] : null;
            var rows = fullY ? new int[h] : null;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (coverage[y * w + x] == 0) continue;
                if (fullX) columns[x]++;
                if (fullY) rows[y]++;
            }
            int dx = Cut(columns), dy = Cut(rows);
            if (dx == 0 && dy == 0) return bounds;
            var shifted = new byte[coverage.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                shifted[y * w + x] = coverage[((y + dy) % h) * w + (x + dx) % w];
            coverage = shifted;
            bounds.x = (bounds.x + dx) % canvasWidth;
            bounds.y = (bounds.y + dy) % canvasHeight;
            return bounds;
        }

        private static int Cut(int[] counts)
        {
            if (counts == null) return 0;
            int minimum = int.MaxValue, maximum = 0;
            foreach (int count in counts) { minimum = Math.Min(minimum, count); maximum = Math.Max(maximum, count); }
            if (minimum == maximum) return 0;
            int first = Array.FindIndex(counts, value => value != minimum);
            int run = 0, longest = 0, end = 0;
            for (int step = 1; step <= counts.Length; step++)
            {
                int i = (first + step) % counts.Length;
                if (counts[i] != minimum) run = 0;
                else if (++run > longest) { longest = run; end = i; }
            }
            return (end - longest / 2 + counts.Length) % counts.Length;
        }

        internal static ContentAwareFill.Result Heal(Color[] source, byte[] coverage, int width, int height,
            bool transparentOnly, int quality, int seed, CancellationToken cancel, Action<float> progress)
        {
            int count = checked(width * height);
            if (count > MaximumWorkingPixels || source.Length != count || coverage.Length != count)
                throw new ArgumentException("Invalid healing region.");
            var target = (byte[])coverage.Clone();
            var donors = new byte[count];
            for (int i = 0; i < count; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                // Even the feathered stroke edge is excluded from donor patches.
                donors[i] = coverage[i] == 0 ? (byte)255 : (byte)0;
                if (transparentOnly && source[i].a > .001f) target[i] = 0;
            }
            return ContentAwareFill.Run(new ContentAwareFill.Input
            {
                width = width, height = height, pixels = source, target = target,
                donors = donors, quality = quality, seed = seed
            }, cancel, progress);
        }
    }
}
