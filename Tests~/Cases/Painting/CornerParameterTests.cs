// Explicit support: Framework/TestApi.cs. Owned CPU buffers and project-Temp artifacts.
// Capture must run before the production optimization. Verify never regenerates its reference.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DCFApixels.WhimTex;
using Unity.Collections;
using UnityEngine;
using WhimTex.Tests;

public static class CornerParameterTests
{
    const string DirectoryName = "Temp/WhimTex/distance-fields/corner-parameters";
    const string SourcePath = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/DistanceRaster/star-13-inner-0364-phase3-known-failure.png";
    static readonly float[] Miters = { 1.1f, 4, 8, 64 };
    static readonly DistanceMetric[] Modes = { DistanceMetric.CornerFit, DistanceMetric.Spring };
    static string PathFor(string key)
    {
        if (key.Length > 80 || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key.Contains("..")) throw new ArgumentException("Invalid snapshot key");
        return Path.Combine(DirectoryName, key + ".bin");
    }
    static string Hash(string path)
    {
        using var sha = SHA256.Create(); using var file = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
    }
    public static string Capture(string key)
    {
        int checks = 0;
        string result = TestContext.Run("Pre-change native corner-field reference capture", t =>
        {
            var api = new Api(); var budget = Stopwatch.StartNew(); string path = PathFor(key);
            t.True(!File.Exists(path), "Never overwrite a captured reference");
            Directory.CreateDirectory(DirectoryName);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536);
            using var writer = new BinaryWriter(stream);
            writer.Write("WT-CORNER-PARAMETERS-1"); writer.Write(12);
            foreach (var data in Inputs(t)) foreach (var mode in Modes)
            {
                using var input = new NativeArray<Color32>(data.pixels, Allocator.TempJob);
                using var output = new NativeArray<float>(data.pixels.Length, Allocator.TempJob);
                using var geometry = api.Prepare(input, data.size, data.threshold, mode);
                api.Compute(geometry, input, output, data.size, data.threshold, mode, 4); // warm kernel outside samples
                writer.Write(data.size); writer.Write(data.threshold); writer.Write((int)mode);
                foreach (Color32 p in data.pixels) { writer.Write(p.r); writer.Write(p.g); writer.Write(p.b); writer.Write(p.a); }
                foreach (float miter in Miters)
                {
                    t.True(budget.Elapsed.TotalSeconds < 55, "Capture body below 55s");
                    var clock = Stopwatch.StartNew(); api.Compute(geometry, input, output, data.size, data.threshold, mode, miter);
                    writer.Write(miter); writer.Write(clock.Elapsed.TotalMilliseconds);
                    foreach (float value in output.ToArray()) { t.True(float.IsFinite(value), "Finite baseline"); writer.Write(value); }
                }
                for (int i = 0; i < data.pixels.Length; i++) t.Equal(data.pixels[i], input[i], "Input remains owned and unchanged");
            }
            checks = t.Checks;
        });
        if (result.Contains("\"status\":\"passed\""))
            return TestContext.Result("skipped", checks, "Reference saved for verification, SHA256=" + Hash(PathFor(key))).ToJson();
        return result;
    }

    public static string Verify(string key, string expectedSha)
    {
        if (!File.Exists(PathFor(key))) return TestContext.Result("skipped", 0,
            "Pre-change session snapshot is unavailable; it cannot be regenerated with the optimized kernel.").ToJson();
        return TestContext.Run("Prepared CornerFit preserves pre-change fields; Spring records intentional relaxation change", t =>
    {
        string path = PathFor(key); t.Equal(expectedSha, Hash(path), "Frozen pre-change reference SHA256");
        var api = new Api(); var budget = Stopwatch.StartNew();
        t.True(api.HasPreparedCorners, "The optimized geometry contains prepared corner parameters");
        var rows = new List<string> { "size,algorithm,threshold,miter,beforeMs,afterMs,maxDelta,changedBits" };
        using var reader = new BinaryReader(File.OpenRead(path));
        t.Equal("WT-CORNER-PARAMETERS-1", reader.ReadString(), "Snapshot version");
        int count = reader.ReadInt32(); t.Equal(12, count, "Exact bounded geometry groups");
        for (int group = 0; group < count; group++)
        {
            int size = reader.ReadInt32(); byte threshold = reader.ReadByte(); var mode = (DistanceMetric)reader.ReadInt32();
            t.True(size == 64 || size == 512, "Bounded source dimensions");
            t.True(mode == DistanceMetric.CornerFit || mode == DistanceMetric.Spring, "Exact supported modes");
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            using var input = new NativeArray<Color32>(pixels, Allocator.TempJob);
            using var output = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            using var geometry = api.Prepare(input, size, threshold, mode);
            api.Compute(geometry, input, output, size, threshold, mode, 4);
            foreach (float expectedMiter in Miters)
            {
                t.True(budget.Elapsed.TotalSeconds < 55, "Verification body below 55s");
                float miter = reader.ReadSingle(); double before = reader.ReadDouble(); t.Equal(expectedMiter, miter, "Bounded miter sequence");
                var expected = new float[pixels.Length]; for (int i = 0; i < expected.Length; i++) expected[i] = reader.ReadSingle();
                var clock = Stopwatch.StartNew(); api.Compute(geometry, input, output, size, threshold, mode, miter); double after = clock.Elapsed.TotalMilliseconds;
                double maximum = 0; int changed = 0;
                for (int i = 0; i < expected.Length; i++)
                {
                    float actual = output[i]; t.True(float.IsFinite(actual), "Finite optimized field");
                    maximum = Math.Max(maximum, Math.Abs((double)actual - expected[i]));
                    if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected[i])) changed++;
                }
                // Spring deliberately stopped rejecting relaxation against source
                // coverage after this snapshot. It is now a historical comparison,
                // not an equivalence oracle; never regenerate the frozen reference.
                if (mode == DistanceMetric.CornerFit)
                    t.Equal(0, changed, "CornerFit pre-change float values remain bit-identical");
                rows.Add(size + "," + mode + "," + threshold + "," + miter.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + "," + before.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + "," + after.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + "," + maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + changed);
            }
            for (int i = 0; i < pixels.Length; i++) t.Equal(pixels[i], input[i], "Optimized computation leaves source unchanged");
        }
        t.Equal(reader.BaseStream.Length, reader.BaseStream.Position, "Entire reference consumed");
        File.WriteAllLines(Path.Combine(DirectoryName, key + "-comparison.csv"), rows);
        });
    }

    struct Data { internal int size; internal byte threshold; internal Color32[] pixels; }
    static IEnumerable<Data> Inputs(TestContext t)
    {
        byte[] bytes = File.ReadAllBytes(SourcePath);
        using (var sha = SHA256.Create()) t.Equal("98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC",
            BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""), "Original raster input");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        Color32[] source;
        try { t.True(texture.LoadImage(bytes), "Decode fixture"); t.Equal(512, texture.width, "Fixture size"); source = texture.GetPixels32(); }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        foreach (byte threshold in new byte[] { 77, 128, 192 }) yield return new Data { size = 512, threshold = threshold, pixels = source };
        foreach (double angle in new[] { 12.0, 30.0, 80.0 })
        {
            var pixels = new Color32[64 * 64]; double halfWidth = 38 * Math.Tan(angle * Math.PI / 360);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                int coverage = 0;
                for (int j = 0; j < 8; j++) for (int i = 0; i < 8; i++)
                {
                    double px = x - .5 + (i + .5) / 8, py = y - .5 + (j + .5) / 8;
                    if (py >= 10.25 && py <= 48.25 && Math.Abs(px - 31.25) <= halfWidth * (48.25 - py) / 38) coverage++;
                }
                byte a = (byte)Math.Round(255.0 * coverage / 64); pixels[y * 64 + x] = new Color32(a, a, a, a);
            }
            yield return new Data { size = 64, threshold = 128, pixels = pixels };
        }
    }

    sealed class Api
    {
        readonly MethodInfo prepare, compute;
        internal readonly bool HasPreparedCorners;
        internal Api()
        {
            var type = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
            prepare = type.GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic);
            compute = type.GetMethod("ComputePrepared", BindingFlags.Static | BindingFlags.NonPublic);
            HasPreparedCorners = type.GetNestedType("Geometry", BindingFlags.NonPublic).GetField("corners") != null;
        }
        internal IDisposable Prepare(NativeArray<Color32> source, int size, byte threshold, DistanceMetric mode)
            => (IDisposable)prepare.Invoke(null, new object[] { source, size, size, threshold, 0, mode });
        internal void Compute(IDisposable geometry, NativeArray<Color32> source, NativeArray<float> output, int size, byte threshold, DistanceMetric mode, float miter)
            => compute.Invoke(null, new object[] { geometry, source, output, size, threshold, 0, mode, miter });
    }
}
