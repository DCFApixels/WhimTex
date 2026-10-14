// Entry: ReconstructedCacheTests.Run(). Explicit support: Tests~/Framework/TestApi.cs.
// CPU-only regression, 45s body budget. No files, assets, windows or timing benchmark.
// Reflection binds package-owned methods and counters only, never Unity internals.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DCFApixels.WhimTex;
using Unity.Collections;
using UnityEngine;
using WhimTex.Tests;

public static class ReconstructedCacheTests
{
    const int BudgetMilliseconds = 45000;
    const long RetentionBudget = 64L * 1024 * 1024;
    const int Width = 32, Height = 24;

    public static string Run() => TestContext.Run(
        "Reconstructed cache: exact fields, invalidation, owned source, shared 64MiB LRU and disposal", t =>
    {
        var clock = Stopwatch.StartNew();
        var api = new CacheApi(t, clock);
        CheckSequence(t, api, DistanceMetric.CornerFit);
        CheckSequence(t, api, DistanceMetric.Spring);
        CheckOwners(t, api);
        CheckBudgetAndLru(t, api);
        api.CheckTime("All owned caches and buffers disposed");
    });

    static void CheckSequence(TestContext t, CacheApi api, DistanceMetric mode)
    {
        var pixels = Fixture(Width, Height);
        using var source = new NativeArray<Color32>(pixels, Allocator.TempJob);
        using var output = new NativeArray<float>(pixels.Length, Allocator.TempJob);
        using var expected = new NativeArray<float>(pixels.Length, Allocator.TempJob);
        using var cache = api.Create();
        Counts(t, cache, 0, 0, mode + " new owner");
        t.Equal(0L, cache.RetainedBytes, "New cache retains no buffers");

        void Step(string label, int preparations, int distances, float miter = 4,
            byte threshold = 128, int channel = 0, int width = Width, int height = Height)
            => CheckCall(t, api, cache, pixels, source, output, expected, width, height,
                threshold, channel, mode, miter, preparations, distances, mode + "/" + label);

        Step("cold", 1, 1);
        Step("same call rewrites poisoned caller output", 1, 1);
        // An equal pixel snapshot in another native allocation is still the same key.
        using (var copy = new NativeArray<Color32>(pixels, Allocator.TempJob))
            CheckCall(t, api, cache, pixels, copy, output, expected, Width, Height,
                128, 0, mode, 4, 1, 1, mode + "/equal bytes at another address");
        Step("miter 8 changes field only", 1, 2, miter: 8);
        Step("same miter 8 reuses field", 1, 2, miter: 8);
        Step("miter 1.1 changes field only", 1, 3, miter: 1.1f);
        Step("return to miter 4 replaces the one field", 1, 4);
        Step("threshold 77", 2, 5, threshold: 77);
        Step("same threshold 77", 2, 5, threshold: 77);
        Step("threshold 192", 3, 6, threshold: 192);
        Step("return to threshold 128 replaces the one preparation", 4, 7);

        int index = 0;
        while (index < pixels.Length && (pixels[index].a == 0 || pixels[index].a == 255)) index++;
        t.True(index < pixels.Length, "Fixture contains fractional edge coverage");
        Color32 saved = pixels[index], changed = saved;
        changed.a = (byte)(255 - changed.a);
        pixels[index] = changed; var writable = source; writable[index] = changed;
        Step("selected-channel byte changed in the same allocation", 5, 8);
        pixels[index] = saved; writable[index] = saved;
        Step("restore selected byte", 6, 9);
        changed = saved; changed.r ^= 1;
        pixels[index] = changed; writable[index] = changed;
        Step("unselected RGB byte still invalidates exact source snapshot", 7, 10);
        pixels[index] = saved; writable[index] = saved;
        Step("restore RGB byte", 8, 11);

        Step("different dimensions with the same pixel count", 9, 12, width: Height, height: Width);
        Step("restore dimensions", 10, 13);
        Step("red channel", 11, 14, channel: 1);
        Step("green channel", 12, 15, channel: 2);
        Step("blue channel", 13, 16, channel: 3);
        Step("luminance channel", 14, 17, channel: 4);
        Step("restore alpha channel", 15, 18);
        DistanceMetric other = mode == DistanceMetric.CornerFit ? DistanceMetric.Spring : DistanceMetric.CornerFit;
        CheckCall(t, api, cache, pixels, source, output, expected, Width, Height,
            128, 0, other, 4, 16, 19, mode + "/metric changes preparation");
        Step("restore metric replaces the one preparation", 17, 20);
        Step("final identical call", 17, 20);

        cache.Dispose();
        t.Equal(0L, cache.RetainedBytes, mode + " Dispose releases all retained bytes");
        cache.Dispose();
        t.Equal(0L, cache.RetainedBytes, mode + " repeated Dispose is harmless");
        // Counters after Dispose may be preserved or reset; neither is required here.
        SameSource(t, pixels, source, mode + " disposal leaves borrowed source unchanged");
        api.CheckTime(mode + " complete");
    }

    static void CheckOwners(TestContext t, CacheApi api)
    {
        var pixels = Fixture(Width, Height);
        using var output = new NativeArray<float>(pixels.Length, Allocator.TempJob);
        using var expected = new NativeArray<float>(pixels.Length, Allocator.TempJob);
        using var first = api.Create();
        using var second = api.Create();
        using (var borrowed = new NativeArray<Color32>(pixels, Allocator.TempJob))
            CheckCall(t, api, first, pixels, borrowed, output, expected, Width, Height,
                128, 0, DistanceMetric.CornerFit, 4, 1, 1, "First owner borrows a temporary source");
        // The cache must own its snapshot after the caller releases that allocation.
        using var replacement = new NativeArray<Color32>(pixels, Allocator.TempJob);
        CheckCall(t, api, first, pixels, replacement, output, expected, Width, Height,
            128, 0, DistanceMetric.CornerFit, 4, 1, 1, "First owner survives caller source disposal");
        CheckCall(t, api, second, pixels, replacement, output, expected, Width, Height,
            77, 0, DistanceMetric.Spring, 8, 1, 1, "Second owner has independent counters and preparation");
        Counts(t, first, 1, 1, "Second owner does not rebuild first owner");
        second.Dispose();
        t.Equal(0L, second.RetainedBytes, "Second owner disposed");
        t.True(first.RetainedBytes > 0, "Disposing second owner preserves first owner");
        CheckCall(t, api, first, pixels, replacement, output, expected, Width, Height,
            128, 0, DistanceMetric.CornerFit, 4, 1, 1, "First owner's field remains reusable");
        first.Dispose();
        t.Equal(0L, first.RetainedBytes, "First owner disposed");
        api.CheckTime("Owner isolation complete");
    }

    static void CheckBudgetAndLru(TestContext t, CacheApi api)
    {
        // Uniform masks force retention pressure without fitting thousands of
        // contours. At most twelve owners and one shared 1024x1024 input/output pair.
        const int size = 1024, maximumOwners = 12;
        using var source = new NativeArray<Color32>(size * size, Allocator.TempJob);
        using var output = new NativeArray<float>(source.Length, Allocator.TempJob);
        using var expected = new NativeArray<float>(source.Length, Allocator.TempJob);
        Poison(expected);
        api.Uncached(source, expected, size, size, 128, 0, DistanceMetric.CornerFit, 4, 0);
        var owners = new List<Cache>(maximumOwners);
        bool evicted = false;
        try
        {
            void Fill(Cache cache, int preparations, int distances, string label)
            {
                api.CheckTime(label);
                Poison(output);
                cache.Compute(source, output, size, size, 128, 0, DistanceMetric.CornerFit, 4);
                Counts(t, cache, preparations, distances, label);
                SameField(t, expected, output, label);
                t.True(cache.RetainedBytes > 0, label + " entry fits the retention budget");
                CheckRetained(t, owners);
                api.CheckTime(label + " complete");
            }

            owners.Add(api.Create()); Fill(owners[0], 1, 1, "Oldest owner");
            owners.Add(api.Create()); Fill(owners[1], 1, 1, "Frequently used owner");
            for (int i = 2; i < maximumOwners; i++)
            {
                Fill(owners[1], 1, 1, "A field hit refreshes LRU recency");
                owners.Add(api.Create()); Fill(owners[i], 1, 1, "Pressure owner " + i);
                t.True(owners[1].RetainedBytes > 0, "Shared pressure preserves recently used entry");
                if (owners[0].RetainedBytes == 0) { evicted = true; break; }
                for (int earlier = 2; earlier < i; earlier++)
                    t.True(owners[earlier].RetainedBytes > 0, "Oldest entry must be evicted before newer entries");
            }
            t.True(evicted, "Bounded twelve-owner pressure exercises the shared 64MiB budget");
            Fill(owners[1], 1, 1, "Recent owner remains a full field hit after eviction");
            // Eviction counters may persist or reset along with Dispose.
            int preparationsBefore = owners[0].PreparationCount, distancesBefore = owners[0].DistanceCount;
            Fill(owners[0], preparationsBefore + 1, distancesBefore + 1, "Evicted owner rebuilds both stages");
            t.True(owners[1].RetainedBytes > 0, "Rebuilt oldest owner preserves refreshed hot entry");
            for (int i = 0; i < source.Length; i++)
            {
                Color32 p = source[i];
                if (p.r != 0 || p.g != 0 || p.b != 0 || p.a != 0)
                    t.True(false, "Uniform pressure source was modified at pixel " + i);
            }
            t.True(true, "All pressure calls preserve borrowed source bytes");
        }
        finally
        {
            // Attempt every owned cleanup even if one disposal fails.
            Exception cleanupError = null;
            foreach (var cache in owners)
            {
                try
                {
                    cache.Dispose();
                    t.Equal(0L, cache.RetainedBytes, "Pressure owner retains zero bytes after Dispose");
                }
                catch (Exception error) { if (cleanupError == null) cleanupError = error; }
            }
            if (cleanupError != null) throw cleanupError;
        }
        api.CheckTime("Shared budget/LRU cleanup complete");
    }

    static void CheckRetained(TestContext t, List<Cache> owners)
    {
        long total = 0;
        foreach (var cache in owners)
        {
            long bytes = cache.RetainedBytes;
            t.True(bytes >= 0 && bytes <= RetentionBudget, "Per-owner retained bytes are bounded");
            total += bytes;
        }
        t.True(total <= RetentionBudget, "All live test owners share one 64MiB retention budget");
    }

    static void CheckCall(TestContext t, CacheApi api, Cache cache, Color32[] pixels,
        NativeArray<Color32> source, NativeArray<float> output, NativeArray<float> expected,
        int width, int height, byte threshold, int channel, DistanceMetric mode, float miter,
        int preparations, int distances, string label)
    {
        api.CheckTime(label);
        Poison(expected);
        api.Uncached(source, expected, width, height, threshold, channel, mode, miter, 0);
        SameSource(t, pixels, source, label + " uncached input");
        Poison(output);
        cache.Compute(source, output, width, height, threshold, channel, mode, miter);
        Counts(t, cache, preparations, distances, label);
        SameSource(t, pixels, source, label + " cached input");
        SameField(t, expected, output, label);
        t.True(cache.RetainedBytes > 0 && cache.RetainedBytes <= RetentionBudget,
            label + " small fixture retains a bounded preparation and field");
        api.CheckTime(label + " complete");
    }

    static void Counts(TestContext t, Cache cache, int preparations, int distances, string label)
    {
        t.Equal(preparations, cache.PreparationCount, label + " PreparationCount");
        t.Equal(distances, cache.DistanceCount, label + " DistanceCount");
    }

    static void Poison(NativeArray<float> output)
    {
        for (int i = 0; i < output.Length; i++) output[i] = float.NaN;
    }

    static void SameField(TestContext t, NativeArray<float> expected, NativeArray<float> actual, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " field length");
        for (int i = 0; i < expected.Length; i++)
        {
            float a = expected[i], b = actual[i];
            if (!float.IsFinite(a) || !float.IsFinite(b))
                t.True(false, label + " non-finite or unwritten field at pixel " + i);
            if (BitConverter.SingleToInt32Bits(a) != BitConverter.SingleToInt32Bits(b))
                t.True(false, label + " exact float mismatch at pixel " + i + ": expected " + a + ", got " + b);
        }
        t.True(true, label + " all pixels finite and bit-identical to uncached ComputeArtisticDistance");
    }

    static void SameSource(TestContext t, Color32[] expected, NativeArray<Color32> actual, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " source length");
        for (int i = 0; i < expected.Length; i++)
        {
            Color32 a = expected[i], b = actual[i];
            if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a)
                t.True(false, label + " source byte mismatch at pixel " + i);
        }
        t.True(true, label + " every source byte is read-only");
    }

    static Color32[] Fixture(int width, int height)
    {
        // Independent 8x8 area coverage of a subpixel triangle; the other
        // channels are different masks so channel-key changes are observable.
        const int samples = 8;
        var pixels = new Color32[width * height];
        double cx = width * .5 + .25, bottom = height * .2 + .25;
        double tip = height * .84 + .25, halfWidth = width * .36;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int covered = 0;
            for (int sy = 0; sy < samples; sy++) for (int sx = 0; sx < samples; sx++)
            {
                double px = x - .5 + (sx + .5) / samples, py = y - .5 + (sy + .5) / samples;
                if (py >= bottom && py <= tip && Math.Abs(px - cx) <= halfWidth * (tip - py) / (tip - bottom))
                    covered++;
            }
            byte alpha = (byte)((covered * 255 + samples * samples / 2) / (samples * samples));
            byte red = (byte)Math.Max(0, Math.Min(255, Math.Round(128 + (x - width * .43) * 73)));
            byte green = (byte)Math.Max(0, Math.Min(255, Math.Round(128 + (y - height * .57) * 61)));
            pixels[y * width + x] = new Color32(red, green, (byte)(255 - alpha), alpha);
        }
        return pixels;
    }

    delegate void CachedCompute(NativeArray<Color32> source, NativeArray<float> output,
        int width, int height, byte threshold, int channel, DistanceMetric metric, float miterLimit);
    delegate void UncachedCompute(NativeArray<Color32> source, NativeArray<float> output,
        int width, int height, byte threshold, int channel, DistanceMetric metric, float miterLimit, float merge);

    sealed class CacheApi
    {
        readonly TestContext test;
        readonly Stopwatch clock;
        readonly Type cacheType;
        readonly MethodInfo compute;
        readonly PropertyInfo preparations, distances, retainedBytes;
        internal readonly UncachedCompute Uncached;

        internal CacheApi(TestContext test, Stopwatch clock)
        {
            this.test = test; this.clock = clock;
            var assembly = typeof(SDFLayerBehaviour).Assembly;
            cacheType = assembly.GetType("DCFApixels.WhimTex.ReconstructedDistanceCache", true);
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var signature = new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>),
                typeof(int), typeof(int), typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float) };
            compute = cacheType.GetMethod("Compute", instance, null, signature, null);
            preparations = cacheType.GetProperty("PreparationCount", instance);
            distances = cacheType.GetProperty("DistanceCount", instance);
            retainedBytes = cacheType.GetProperty("RetainedBytes", instance);
            test.True(typeof(IDisposable).IsAssignableFrom(cacheType), "Package cache implements IDisposable");
            test.True(compute != null && compute.ReturnType == typeof(void), "Exact package cache Compute signature");
            test.True(preparations != null && preparations.PropertyType == typeof(int) && preparations.CanRead,
                "PreparationCount is an observable int counter");
            test.True(distances != null && distances.PropertyType == typeof(int) && distances.CanRead,
                "DistanceCount is an observable int counter");
            test.True(retainedBytes != null && retainedBytes.PropertyType == typeof(long) && retainedBytes.CanRead,
                "RetainedBytes is an observable long counter");
            var utility = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility", true);
            var reference = utility.GetMethod("ComputeArtisticDistance", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int),
                    typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float), typeof(float) }, null);
            test.True(reference != null && reference.ReturnType == typeof(void), "Exact uncached package reference signature");
            Uncached = (UncachedCompute)Delegate.CreateDelegate(typeof(UncachedCompute), reference);
        }

        internal Cache Create() => new Cache(this, Activator.CreateInstance(cacheType, true));
        internal void CheckTime(string label) => test.True(clock.ElapsedMilliseconds < BudgetMilliseconds,
            label + ": 45s body budget, elapsed=" + clock.ElapsedMilliseconds + "ms");

        internal int Preparations(object target) => (int)preparations.GetValue(target);
        internal int Distances(object target) => (int)distances.GetValue(target);
        internal long Bytes(object target) => (long)retainedBytes.GetValue(target);
        internal CachedCompute Bind(object target)
            => (CachedCompute)Delegate.CreateDelegate(typeof(CachedCompute), target, compute);
    }

    sealed class Cache : IDisposable
    {
        readonly CacheApi api;
        readonly object target;
        internal readonly CachedCompute Compute;
        internal int PreparationCount => api.Preparations(target);
        internal int DistanceCount => api.Distances(target);
        internal long RetainedBytes => api.Bytes(target);

        internal Cache(CacheApi api, object target)
        {
            this.api = api; this.target = target;
            try { Compute = api.Bind(target); }
            catch { ((IDisposable)target).Dispose(); throw; }
        }

        public void Dispose() => ((IDisposable)target).Dispose();
    }
}
