using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

// Session comparison: Capture must run before changing the production kernel.
// All calls rebuild the field; only Editor/compiler state is shared.
public static class RbfOptimizationTests
{
    public static string Capture(string suite) => Execute(suite, true);
    public static string Compare(string suite) => Execute(suite, false);

    static string Execute(string suite, bool capture)
    {
        string root = Path.GetFullPath("Temp/WhimTex/rbf-optimization-20261014");
        string path = Path.Combine(root, suite + ".bin");
        if (!capture && !File.Exists(path))
            return TestContext.Result("skipped", 0, "Pre-change snapshot unavailable: " + path).ToJson();
        return TestContext.Run("RBF cold construction / bitwise comparison: " + suite, t =>
        {
            t.True(suite == "small" || suite == "star512" || suite == "rings512" || suite == "star1024", "Bounded suite");
            t.True(!capture || !File.Exists(path), "Never overwrite a pre-change snapshot");
            Directory.CreateDirectory(root);
            var assembly = typeof(SDFLayerBehaviour).Assembly;
            var compute = assembly.GetType("DCFApixels.WhimTex.RbfDistanceField", true)
                .GetMethod("Compute", BindingFlags.Static | BindingFlags.NonPublic);
            var receiver = assembly.GetType("DCFApixels.WhimTex.DistanceFieldTiming", true)
                .GetField("receiver", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = receiver.GetValue(null);
            var phases = new Dictionary<string, double>();
            var report = new System.Text.StringBuilder("case,repeat,totalMs,phase,phaseMs\n");
            using var snapshot = new MemoryStream();
            using var writer = new BinaryWriter(snapshot);
            byte[] expected = capture ? null : File.ReadAllBytes(path);
            if (!capture)
                t.Equal(File.ReadAllText(path + ".sha256"), Hash(expected), "Frozen snapshot SHA256");
            try
            {
                receiver.SetValue(null, (Action<string, double>)((name, ms) =>
                {
                    phases.TryGetValue(name, out double sum); phases[name] = sum + ms;
                }));
                void Check(string shape, int w, int h, byte threshold, int channel, SharpDistanceMode mode, int repeats)
                {
                    string label = $"{shape}-{w}x{h}-t{threshold}-c{channel}-{mode}";
                    var pixels = Input(shape, w, h);
                    using var input = new NativeArray<Color32>(pixels, Allocator.TempJob);
                    using var output = new NativeArray<float>(pixels.Length, Allocator.TempJob);
                    writer.Write(label);
                    // Authenticate the actual raster too, independently of the output.
                    foreach (var p in pixels) { writer.Write(p.r); writer.Write(p.g); writer.Write(p.b); writer.Write(p.a); }
                    int[] first = null;
                    for (int repeat = 0; repeat < repeats; repeat++)
                    {
                        phases.Clear();
                        var watch = Stopwatch.StartNew();
                        compute.Invoke(null, new object[] { input, output, w, h, threshold, channel, 4f, mode, false });
                        double elapsed = watch.Elapsed.TotalMilliseconds;
                        foreach (var phase in phases)
                            report.AppendLine(label + "," + repeat + "," + elapsed.ToString("R", CultureInfo.InvariantCulture)
                                + "," + phase.Key + "," + phase.Value.ToString("R", CultureInfo.InvariantCulture));
                        int nonfinite = 0, changed = 0;
                        if (first == null) first = new int[output.Length];
                        for (int i = 0; i < output.Length; i++)
                        {
                            if (!float.IsFinite(output[i])) nonfinite++;
                            int bits = math.asint(output[i]);
                            if (repeat == 0) { first[i] = bits; writer.Write(bits); }
                            else if (bits != first[i]) changed++;
                        }
                        t.Equal(0, nonfinite, label + " finite output");
                        t.Equal(0, changed, label + " deterministic fresh build");
                    }
                }
                if (suite == "small")
                {
                    foreach (var mode in new[] { SharpDistanceMode.Unrestricted, SharpDistanceMode.GuardedSharp })
                    foreach (byte threshold in new byte[] { 77, 128, 192 })
                    {
                        Check("star", 49, 37, threshold, 0, mode, 1);
                        Check("acute", 64, 67, threshold, 0, mode, 1);
                        Check("rings", 33, 29, threshold, 1, mode, 1);
                    }
                    foreach (string shape in new[] { "empty", "full", "edge" })
                        Check(shape, 17, 10, 128, 0, SharpDistanceMode.GuardedSharp, 1);
                    foreach (int w in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })
                        Check("edge", w, 7, 128, 0, SharpDistanceMode.Unrestricted, 1);
                }
                else
                {
                    int size = suite == "star1024" ? 1024 : 512;
                    Check(suite == "rings512" ? "rings" : "star", size, size, 128, 0, SharpDistanceMode.GuardedSharp, 5);
                }
            }
            finally { receiver.SetValue(null, previous); }
            byte[] actual = snapshot.ToArray();
            string tag = capture ? "before" : "after-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(root, suite + "-" + tag + ".csv"), report.ToString());
            if (capture)
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                stream.Write(actual, 0, actual.Length);
                File.WriteAllText(path + ".sha256", Hash(actual));
                File.WriteAllText(path + ".source-sha256", Hash(File.ReadAllBytes(
                    "Packages/com.dcfapixels.whimtex/src/Layers/RbfDistanceField.cs")));
            }
            else
            {
                t.Equal(expected.Length, actual.Length, "Same input/output snapshot layout");
                int differences = 0;
                for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) differences++;
                t.Equal(0, differences, "Every source and field byte matches pre-change snapshot");
            }
            t.True(actual.Length > 0, "Snapshot retained; timings are diagnostic, not an acceptance threshold");
        });
    }

    static string Hash(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    static Color32[] Input(string shape, int w, int h)
    {
        var result = new Color32[w * h];
        var vertices = new Vector2[shape == "acute" ? 26 : 10];
        float size = Mathf.Min(w, h);
        for (int i = 0; i < vertices.Length; i++)
        {
            float angle = Mathf.PI * .5f + i * 2 * Mathf.PI / vertices.Length;
            float radius = size * (i % 2 == 0 ? .36f : shape == "acute" ? .09f : .16f);
            vertices[i] = new Vector2(w * .5f + .23f, h * .5f - .31f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            float distance = float.PositiveInfinity;
            if (shape == "rings")
            {
                float tile = size / (size > 100 ? 6 : 2);
                float dx = Mathf.Repeat(x + .5f, tile) - tile * .5f, dy = Mathf.Repeat(y + .5f, tile) - tile * .5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                distance = Mathf.Max(r - tile * .34f, tile * .19f - r);
            }
            else if (shape == "edge") distance = x - w * .55f + (y - h * .4f) * .31f;
            else if (shape == "full") distance = -10;
            else if (shape != "empty")
            {
                Vector2 p = new Vector2(x + .5f, y + .5f);
                bool inside = false;
                for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
                {
                    Vector2 a = vertices[j], b = vertices[i], e = b - a;
                    distance = Mathf.Min(distance, (p - a - Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude) * e).magnitude);
                    if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
                }
                if (inside) distance = -distance;
            }
            byte coverage = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(.5f - distance));
            result[y * w + x] = new Color32(coverage, 255, 255, coverage);
        }
        return result;
    }
}
