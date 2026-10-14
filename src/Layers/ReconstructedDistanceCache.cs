using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // One current source, optional prepared contour and distance field per owner.
    // Presentation settings never enter the preparation key. Old revisions are
    // replaced, not accumulated. All retained native allocations share a budget.
    internal sealed class ReconstructedDistanceCache : IDisposable
    {
        internal const long BudgetBytes = 64L * 1024 * 1024;
        static readonly HashSet<ReconstructedDistanceCache> retained = new HashSet<ReconstructedDistanceCache>();
        static long totalBytes, clock;
        ArtisticDistanceField.Geometry geometry;
        NativeArray<Color32> input;
        NativeArray<float> distances;
        int width, height, channel;
        byte threshold;
        DistanceMetric metric;
        SharpDistanceMode sharpMode;
        float miter;
        bool fieldReady;
        long used;
        internal int PreparationCount { get; private set; }
        internal int DistanceCount { get; private set; }
        internal long RetainedBytes { get; private set; }

        static ReconstructedDistanceCache()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            UnityEditor.EditorApplication.quitting += ReleaseAll;
        }

        static void ReleaseAll()
        {
            while (retained.Count > 0)
            {
                ReconstructedDistanceCache first = null;
                foreach (var item in retained) { first = item; break; }
                first.Dispose();
            }
        }

        internal void Compute(NativeArray<Color32> source, NativeArray<float> output, int w, int h,
            byte boundary, int sourceChannel, DistanceMetric algorithm, float miterLimit,
            SharpDistanceMode mode = SharpDistanceMode.Unrestricted)
        {
            DistanceFieldUtility.Validate(source.Length, output.Length, w, h, sourceChannel, algorithm, miterLimit, 0);
            DistanceFieldUtility.ValidateSharpMode(mode);
            if (algorithm != DistanceMetric.CornerFit && algorithm != DistanceMetric.Spring && algorithm != DistanceMetric.Rbf)
                throw new ArgumentOutOfRangeException(nameof(algorithm));
            if (mode == SharpDistanceMode.Unrestricted) miterLimit = 4;
            if ((long)source.Length * 8 > BudgetBytes)
            {
                Dispose();
                if (algorithm == DistanceMetric.Rbf)
                    RbfDistanceField.Compute(source, output, w, h, boundary, sourceChannel, miterLimit, mode);
                else ArtisticDistanceField.Compute(source, output, w, h, boundary, sourceChannel, algorithm, miterLimit, sharpMode: mode);
                PreparationCount++; DistanceCount++;
                return;
            }
            bool same = input.IsCreated && width == w && height == h && channel == sourceChannel &&
                threshold == boundary && metric == algorithm && sharpMode == mode;
            using (DistanceFieldTiming.Measure("reconstruction.match"))
                if (same)
                    for (int i = 0; i < source.Length; i++)
                    {
                        Color32 a = source[i], b = input[i];
                        if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) { same = false; break; }
                    }
            if (!same)
            {
                Dispose();
                try
                {
                    geometry = algorithm == DistanceMetric.Rbf ? RbfDistanceField.Prepare(source, w, h, boundary, sourceChannel, sharpMode: mode)
                        : ArtisticDistanceField.Prepare(source, w, h, boundary, sourceChannel, algorithm, mode);
                    PreparationCount++;
                    RetainedBytes = (geometry?.ByteCount ?? 0) + (long)source.Length * 8;
                    if (geometry != null && RetainedBytes > BudgetBytes)
                    {
                        ArtisticDistanceField.ComputePrepared(geometry, source, output, w, boundary, sourceChannel, algorithm, miterLimit);
                        DistanceCount++; Dispose(); return;
                    }
                    input = new NativeArray<Color32>(source, Allocator.Persistent);
                    distances = new NativeArray<float>(output.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    width = w; height = h; channel = sourceChannel; threshold = boundary; metric = algorithm; sharpMode = mode;
                    MakeRoom(RetainedBytes);
                    retained.Add(this); totalBytes += RetainedBytes;
                }
                catch { Dispose(); throw; }
            }
            used = ++clock;
            try
            {
                if (!fieldReady || miter != miterLimit)
                {
                    fieldReady = false;
                    if (metric == DistanceMetric.Rbf && geometry == null)
                        RbfDistanceField.Compute(input, distances, width, height, threshold, channel, miterLimit, mode);
                    else ArtisticDistanceField.ComputePrepared(geometry, input, distances, width, threshold, channel, metric, miterLimit);
                    DistanceCount++; miter = miterLimit; fieldReady = true;
                }
                using (DistanceFieldTiming.Measure("reconstruction.copy")) NativeArray<float>.Copy(distances, output);
            }
            catch { Dispose(); throw; }
        }

        static void MakeRoom(long needed)
        {
            while (totalBytes + needed > BudgetBytes && retained.Count > 0)
            {
                ReconstructedDistanceCache oldest = null;
                foreach (var candidate in retained)
                    if (oldest == null || candidate.used < oldest.used) oldest = candidate;
                oldest.Dispose();
            }
        }

        public void Dispose()
        {
            if (retained.Remove(this)) totalBytes -= RetainedBytes;
            geometry?.Dispose(); geometry = null;
            if (input.IsCreated) input.Dispose(); input = default;
            if (distances.IsCreated) distances.Dispose(); distances = default;
            RetainedBytes = 0; fieldReady = false;
        }
    }
}
