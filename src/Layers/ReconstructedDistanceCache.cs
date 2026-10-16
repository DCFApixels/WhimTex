using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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
        NativeArray<float4> channels;
        int width, height, channel;
        byte threshold;
        DistanceMetric metric;
        SharpDistanceMode sharpMode;
        float miter;
        float merge;
        bool fieldReady;
        long used;
        internal int PreparationCount { get; private set; }
        internal int DistanceCount { get; private set; }
        internal long RetainedBytes { get; private set; }

        internal static bool Supports(DistanceMetric algorithm) =>
            algorithm >= DistanceMetric.EuclideanAntialiased && algorithm <= DistanceMetric.MTSDF ||
            algorithm == DistanceMetric.CornerFit || algorithm == DistanceMetric.Spring ||
            algorithm == DistanceMetric.Rbf || algorithm == DistanceMetric.RbfDual;

        // Max Distance and other presentation parameters deliberately do not
        // enter this API. Classic modes retain their final unnormalized field;
        // reconstruction experiments additionally retain prepared geometry.
        internal void ComputeField(NativeArray<Color32> source, NativeArray<float> output, NativeArray<float4> multiChannel,
            int w, int h, byte boundary, int sourceChannel, DistanceMetric algorithm, float miterLimit,
            float contourMergeDistance, SharpDistanceMode mode)
        {
            DistanceFieldUtility.ValidatePhysical(source.Length, output.Length, w, h, sourceChannel, algorithm, miterLimit, contourMergeDistance);
            DistanceFieldUtility.ValidateSharpMode(mode);
            if (!Supports(algorithm)) throw new ArgumentOutOfRangeException(nameof(algorithm));
            bool multi = algorithm == DistanceMetric.MTSDF;
            if (multiChannel.IsCreated && (!multi || multiChannel.Length != source.Length))
                throw new ArgumentException("Multi-channel output must match the MTSDF source.");
            if (algorithm > DistanceMetric.MTSDF)
            {
                Compute(source, output, w, h, boundary, sourceChannel, algorithm, miterLimit, mode);
                return;
            }
            if (algorithm != DistanceMetric.Sharp && !multi) mode = SharpDistanceMode.Unrestricted;
            if (algorithm != DistanceMetric.Sharp || mode == SharpDistanceMode.Unrestricted) miterLimit = 4;
            if (algorithm != DistanceMetric.Sharp && !multi) contourMergeDistance = 0;
            long bytes = (long)source.Length * (multi ? 20 : 8);
            if (bytes > BudgetBytes)
            {
                WhimTexProfiler.Event("sdf.cache", "cache", "bypass:budget", bytes);
                Dispose();
                if (multiChannel.IsCreated)
                {
                    DistanceFieldUtility.ComputeMtsdfDistancePhysical(source, multiChannel, w, h, boundary, sourceChannel, contourMergeDistance, mode);
                    new CopyMedianJob { source = multiChannel, output = output }.Schedule(output.Length, 128).Complete();
                }
                else DistanceFieldUtility.ComputeArtisticDistancePhysical(source, output, w, h, boundary, sourceChannel, algorithm, miterLimit, contourMergeDistance, mode);
                PreparationCount++; DistanceCount++;
                return;
            }
            bool same = fieldReady && input.IsCreated && width == w && height == h && channel == sourceChannel &&
                threshold == boundary && metric == algorithm && sharpMode == mode && miter == miterLimit && merge == contourMergeDistance;
            string miss = same ? "miss:source_pixels" : "miss:not_ready_or_settings";
            using (DistanceFieldTiming.Measure("reconstruction.match"))
                if (same)
                    for (int i = 0; i < source.Length; i++)
                    {
                        Color32 a = source[i], b = input[i];
                        if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) { same = false; break; }
                    }
            WhimTexProfiler.Event("sdf.cache", "cache", same ? "hit" : miss, RetainedBytes);
            if (!same)
            {
                Dispose();
                MakeRoom(bytes);
                try
                {
                    using var timing = DistanceFieldTiming.Measure("field.prepare");
                    input = new NativeArray<Color32>(source, Allocator.Persistent);
                    if (multi)
                    {
                        channels = new NativeArray<float4>(source.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        DistanceFieldUtility.ComputeMtsdfDistancePhysical(input, channels, w, h, boundary, sourceChannel, contourMergeDistance, mode);
                    }
                    else
                    {
                        distances = new NativeArray<float>(source.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        DistanceFieldUtility.ComputeArtisticDistancePhysical(input, distances, w, h, boundary, sourceChannel, algorithm, miterLimit, contourMergeDistance, mode);
                    }
                    width = w; height = h; channel = sourceChannel; threshold = boundary; metric = algorithm;
                    sharpMode = mode; miter = miterLimit; merge = contourMergeDistance;
                    fieldReady = true; PreparationCount++; DistanceCount++;
                    RetainedBytes = bytes; retained.Add(this); totalBytes += bytes;
                }
                catch { Dispose(); throw; }
            }
            used = ++clock;
            WhimTexProfiler.NativeCacheBytes(this, RetainedBytes);
            using (DistanceFieldTiming.Measure("reconstruction.copy"))
            {
                if (multi)
                {
                    if (multiChannel.IsCreated) NativeArray<float4>.Copy(channels, multiChannel);
                    new CopyMedianJob { source = channels, output = output }.Schedule(output.Length, 128).Complete();
                }
                else NativeArray<float>.Copy(distances, output);
            }
        }

        [BurstCompile]
        struct CopyMedianJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> source;
            [WriteOnly] public NativeArray<float> output;
            public void Execute(int index) => output[index] = ArtisticDistanceField.Median(source[index].xyz);
        }

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
            bool rbf = algorithm == DistanceMetric.Rbf || algorithm == DistanceMetric.RbfDual;
            if (algorithm != DistanceMetric.CornerFit && algorithm != DistanceMetric.Spring && !rbf)
                throw new ArgumentOutOfRangeException(nameof(algorithm));
            if (mode == SharpDistanceMode.Unrestricted) miterLimit = 4;
            if ((long)source.Length * 8 > BudgetBytes)
            {
                WhimTexProfiler.Event("sdf.cache", "cache", "bypass:budget");
                Dispose();
                if (rbf)
                    RbfDistanceField.Compute(source, output, w, h, boundary, sourceChannel, miterLimit, mode, algorithm == DistanceMetric.RbfDual);
                else ArtisticDistanceField.Compute(source, output, w, h, boundary, sourceChannel, algorithm, miterLimit, sharpMode: mode);
                PreparationCount++; DistanceCount++;
                return;
            }
            bool same = input.IsCreated && width == w && height == h && channel == sourceChannel &&
                threshold == boundary && metric == algorithm && sharpMode == mode;
            string miss = same ? "miss:source_pixels" : "miss:not_ready_or_settings";
            using (DistanceFieldTiming.Measure("reconstruction.match"))
                if (same)
                    for (int i = 0; i < source.Length; i++)
                    {
                        Color32 a = source[i], b = input[i];
                        if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) { same = false; break; }
                    }
            WhimTexProfiler.Event("sdf.geometry.cache", "cache", same ? "hit" : miss, RetainedBytes);
            if (!same)
            {
                Dispose();
                try
                {
                    geometry = rbf ? RbfDistanceField.Prepare(source, w, h, boundary, sourceChannel, sharpMode: mode, dualContouring: algorithm == DistanceMetric.RbfDual)
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
            WhimTexProfiler.NativeCacheBytes(this, RetainedBytes);
            try
            {
                WhimTexProfiler.Event("sdf.cache", "cache", fieldReady && miter == miterLimit ? "hit" : "miss:field_parameters", RetainedBytes);
                if (!fieldReady || miter != miterLimit)
                {
                    fieldReady = false;
                    if (rbf && geometry == null)
                        RbfDistanceField.Compute(input, distances, width, height, threshold, channel, miterLimit, mode, metric == DistanceMetric.RbfDual);
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
            WhimTexProfiler.NativeCacheBytes(this, 0);
            if (retained.Remove(this)) totalBytes -= RetainedBytes;
            geometry?.Dispose(); geometry = null;
            if (input.IsCreated) input.Dispose(); input = default;
            if (distances.IsCreated) distances.Dispose(); distances = default;
            if (channels.IsCreated) channels.Dispose(); channels = default;
            RetainedBytes = 0; fieldReady = false;
        }
    }
}
