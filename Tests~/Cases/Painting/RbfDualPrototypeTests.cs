using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class RbfDualPrototypeTests
{
    public static string Run() => TestContext.Run("RBF Dual Contouring prototype", t =>
    {
        const int w = 64, h = 64;
        var assembly = typeof(SDFLayerBehaviour).Assembly;
        var compute = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility")
            .GetMethod("ComputeArtisticDistance", BindingFlags.Public | BindingFlags.Static);
        var cacheType = assembly.GetType("DCFApixels.WhimTex.ReconstructedDistanceCache");
        var cacheCompute = cacheType.GetMethod("Compute", BindingFlags.NonPublic | BindingFlags.Instance);
        var count = cacheType.GetProperty("DistanceCount", BindingFlags.NonPublic | BindingFlags.Instance);
        var preparation = cacheType.GetProperty("PreparationCount", BindingFlags.NonPublic | BindingFlags.Instance);
        var frames = new List<Color32[]>();
        double maxDifference = 0;
        foreach (string shape in new[] { "star", "acute-star", "ring", "edge", "empty", "full" })
        {
            var pixels = Input(shape, w, h);
            using var input = new NativeArray<Color32>(pixels, Allocator.TempJob);
            using var original = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            using var dual = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            using var again = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            byte threshold = shape == "acute-star" ? (byte)77 : shape == "ring" ? (byte)192 : (byte)128;
            foreach (var mode in new[] { SharpDistanceMode.GuardedSharp, SharpDistanceMode.Unrestricted })
            {
                compute.Invoke(null, new object[] { input, original, w, h, threshold, 0, DistanceMetric.Rbf, 4f, 0f, mode });
                compute.Invoke(null, new object[] { input, dual, w, h, threshold, 0, DistanceMetric.RbfDual, 4f, 0f, mode });
                int nonfinite = 0, wrongSign = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (!float.IsFinite(dual[i])) nonfinite++;
                    if (Math.Abs(original[i]) > 2 && (original[i] < 0) != (dual[i] < 0)) wrongSign++;
                    maxDifference = Math.Max(maxDifference, Math.Abs(original[i] - dual[i]));
                }
                t.Equal(0, nonfinite, shape + "/" + mode + " finite field");
                t.Equal(0, wrongSign, shape + "/" + mode + " no sign reversal away from boundary");
                if (mode == SharpDistanceMode.GuardedSharp && shape == "star")
                {
                    using var cache = (IDisposable)Activator.CreateInstance(cacheType, true);
                    cacheCompute.Invoke(cache, new object[] { input, again, w, h, threshold, 0, DistanceMetric.RbfDual, 4f, mode });
                    cacheCompute.Invoke(cache, new object[] { input, again, w, h, threshold, 0, DistanceMetric.RbfDual, 4f, mode });
                    t.Equal(1, (int)count.GetValue(cache), "Repeated display reuses Dual field");
                    t.Equal(1, (int)preparation.GetValue(cache), "Repeated display reuses Dual contour");
                    int changed = 0;
                    for (int i = 0; i < again.Length; i++) if (dual[i] != again[i]) changed++;
                    t.Equal(0, changed, "Cached and direct Dual match exactly");
                    cacheCompute.Invoke(cache, new object[] { input, again, w, h, threshold, 0, DistanceMetric.Rbf, 4f, mode });
                    t.Equal(2, (int)preparation.GetValue(cache), "Switching to original RBF invalidates contour key");
                    changed = 0;
                    for (int i = 0; i < again.Length; i++) if (original[i] != again[i]) changed++;
                    t.Equal(0, changed, "Original algorithm restored on switch");
                }
                if (mode == SharpDistanceMode.GuardedSharp)
                {
                    var image = new Color32[w * h * 3];
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) for (int col = 0; col < 3; col++)
                    {
                        int i = y * w + x;
                        float value = col == 0 ? 1 - pixels[i].a / 255f : Mathf.Clamp01(.5f + (col == 1 ? original[i] : dual[i]) / 12);
                        byte c = (byte)Mathf.RoundToInt(value * 255);
                        image[y * w * 3 + col * w + x] = new Color32(c, c, c, 255);
                    }
                    frames.Add(image);
                }
            }
        }
        t.True(maxDifference > .001, "Dual is a distinct reconstruction, not an alias");
        // Exercise the one-dimensional fallback through the public dispatcher.
        using (var input = new NativeArray<Color32>(Input("edge", 1, 7), Allocator.TempJob))
        using (var output = new NativeArray<float>(7, Allocator.TempJob))
        {
            compute.Invoke(null, new object[] { input, output, 1, 7, (byte)128, 0, DistanceMetric.RbfDual, 4f, 0f, SharpDistanceMode.GuardedSharp });
            for (int i = 0; i < output.Length; i++) t.True(float.IsFinite(output[i]), "1D finite fallback");
        }
        string directory = Path.GetFullPath("Temp/WhimTex/rbf-dual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var preview = new Texture2D(w * 3, h * frames.Count, TextureFormat.RGBA32, false, true);
        try
        {
            for (int i = 0; i < frames.Count; i++) preview.SetPixels32(0, (frames.Count - 1 - i) * h, w * 3, h, frames[i]);
            preview.Apply();
            File.WriteAllBytes(Path.Combine(directory, "comparison.png"), preview.EncodeToPNG());
            File.WriteAllText(Path.Combine(directory, "readme.txt"), "Columns: source, RBF Your SDF, RBF Dual Contouring. Guarded Sharp.\nRows: star (128), acute-star (77), ring (192), edge (128), empty, full.\nPrototype only, not an accuracy certification. Maximum field difference: " + maxDifference);
            Debug.Log("RBF Dual prototype preview: " + directory);
        }
        finally { UnityEngine.Object.DestroyImmediate(preview); }
    });

    static Color32[] Input(string shape, int w, int h)
    {
        var result = new Color32[w * h];
        var vertices = new Vector2[shape == "acute-star" ? 26 : 10];
        float size = Mathf.Min(w, h);
        for (int i = 0; i < vertices.Length; i++)
        {
            float angle = Mathf.PI * .5f + i * 2 * Mathf.PI / vertices.Length;
            vertices[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size *
                (i % 2 == 0 ? .32f : shape == "acute-star" ? .1f : .16f);
        }
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            Vector2 p = new Vector2(x - w * .5f + .23f, y - h * .5f - .31f);
            float distance = float.PositiveInfinity;
            if (shape == "ring") distance = Mathf.Abs(p.magnitude - size * .23f) - size * .055f;
            else if (shape == "edge") distance = p.x + p.y * .4f;
            else if (shape == "full") distance = -10;
            else if (shape != "empty")
            {
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
            result[y * w + x] = new Color32(255, 255, 255, coverage);
        }
        return result;
    }
}
