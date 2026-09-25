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
