using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class RbfPrototypeTests
{
    // A bounded prototype smoke test, not a sharp-corner quality certification.
    public static string Run() => TestContext.Run("RBF prototype: small raster fields, distinct output and retained field", t =>
    {
        const int w = 48, h = 40;
        var assembly = typeof(SDFLayerBehaviour).Assembly;
        CheckContourMembership(t, assembly);
        var cornerJob = assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField")
            .GetNestedType("CornerParametersJob", BindingFlags.NonPublic);
        var makeCorner = cornerJob.GetMethod("Make", BindingFlags.NonPublic | BindingFlags.Static);
        var incoming = math.normalize(new float2(.001f, 1));
        var outgoing = math.normalize(new float2(.001f, -1));
        var guarded = makeCorner.Invoke(null, new object[] { incoming, outgoing, false });
        var unrestricted = makeCorner.Invoke(null, new object[] { incoming, outgoing, true });
        var valid = unrestricted.GetType().GetField("valid");
        t.Equal(0, (int)valid.GetValue(guarded), "Existing modes keep their corner limit");
        t.Equal(1, (int)valid.GetValue(unrestricted), "RBF accepts a finite corner beyond the former 64x limit");
        var compute = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility")
            .GetMethod("ComputeSignedDistance", BindingFlags.Public | BindingFlags.Static);
        var cacheType = assembly.GetType("DCFApixels.WhimTex.ReconstructedDistanceCache");
        var cacheCompute = cacheType.GetMethod("Compute", BindingFlags.NonPublic | BindingFlags.Instance);
        var count = cacheType.GetProperty("DistanceCount", BindingFlags.NonPublic | BindingFlags.Instance);
        var prepared = cacheType.GetProperty("PreparationCount", BindingFlags.NonPublic | BindingFlags.Instance);
        var frames = new List<Color32[]>();
        var timings = new System.Text.StringBuilder();
        double largestDifference = 0;
        foreach (string shape in new[] { "circle", "star", "acute-star", "empty" })
        {
            var pixels = new Color32[w * h];
            var polygon = new Vector2[10];
            for (int k = 0; k < polygon.Length; k++)
            {
                float angle = Mathf.PI * .5f + k * Mathf.PI / 5;
                polygon[k] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (k % 2 == 0 ? 15 : shape == "acute-star" ? 2 : 6);
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x - 23.2f, y - 19.3f);
                float d = shape == "circle" ? p.magnitude - 12 : PolygonDistance(p, polygon);
                byte a = shape == "empty" ? (byte)0 : (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(.5f - d));
                pixels[y * w + x] = new Color32(255, 255, 255, a);
            }
            using var input = new NativeArray<Color32>(pixels, Allocator.TempJob);
            using var round = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            using var contour = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            using var result = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            using var repeated = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            compute.Invoke(null, new object[] { input, round, w, h, (byte)128, 0, DistanceMetric.EuclideanAntialiased });
            compute.Invoke(null, new object[] { input, contour, w, h, (byte)128, 0, DistanceMetric.Contour });
            var cache = (IDisposable)Activator.CreateInstance(cacheType, true);
            try
            {
                var watch = Stopwatch.StartNew();
                cacheCompute.Invoke(cache, new object[] { input, result, w, h, (byte)128, 0, DistanceMetric.Rbf, 4f });
                double firstMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                cacheCompute.Invoke(cache, new object[] { input, repeated, w, h, (byte)128, 0, DistanceMetric.Rbf, 4f });
                timings.AppendLine($"{shape}: first={firstMs:F3}ms retained={watch.Elapsed.TotalMilliseconds:F3}ms");
                t.Equal(1, (int)count.GetValue(cache), shape + " retains field for repeated remap requests");
                int nonfinite = 0, altered = 0, wrongSign = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (!float.IsFinite(result[i])) nonfinite++;
                    if (result[i] != repeated[i]) altered++;
                    if (Math.Abs(contour[i]) > 1 && (result[i] < 0) != (contour[i] < 0)) wrongSign++;
                    if (shape != "empty") largestDifference = Math.Max(largestDifference, Math.Abs(contour[i] - result[i]));
                }
                t.Equal(0, nonfinite, shape + " finite field");
                t.Equal(0, altered, shape + " exact retained result");
                t.Equal(0, wrongSign, shape + " no sign inversion away from the boundary");
                if (shape != "empty") t.True(result[19 * w + 23] < 0 && result[0] > 0, shape + " inside/outside sign");
                cacheCompute.Invoke(cache, new object[] { input, repeated, w, h, (byte)128, 0, DistanceMetric.Rbf, 1f });
                t.Equal(1, (int)prepared.GetValue(cache), shape + " miter reuses RBF preparation");
                t.Equal(1, (int)count.GetValue(cache), shape + " unrestricted RBF ignores Miter Limit without rebuilding");
                double miterDifference = 0;
                for (int i = 0; i < pixels.Length; i++) miterDifference = Math.Max(miterDifference, Math.Abs(result[i] - repeated[i]));
                t.Equal(0d, miterDifference, shape + " no forced bevel from retained Miter Limit");
                if (shape == "star")
                {
                    // Above the upper tip, a sharp extension is closer than the
                    // finite-contour Euclidean distance used by the previous mode.
                    int probe = 38 * w + 23;
                    t.True(result[probe] > 0 && result[probe] < contour[probe] - .5f, "Star upper tip extends sharply");
                    timings.AppendLine("Sharp tip: " + result[probe] + "; Contour: " + contour[probe] + "; miter delta: " + miterDifference);
                }
                var preview = new Color32[w * h * 4];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                for (int column = 0; column < 4; column++)
                {
                    int i = y * w + x;
                    float v = column == 0 ? 1 - pixels[i].a / 255f
                        : Mathf.Clamp01(((column == 1 ? round[i] : column == 2 ? contour[i] : result[i]) + 4) / 8);
                    byte b = (byte)Mathf.RoundToInt(255 * v);
                    preview[y * w * 4 + column * w + x] = new Color32(b, b, b, 255);
                }
                frames.Add(preview);
            }
            finally { cache.Dispose(); }
        }
        t.True(largestDifference > 1e-4, "RBF is not an alias of Contour AA");
        var image = new Texture2D(w * 4, h * frames.Count, TextureFormat.RGBA32, false, true);
        try
        {
            for (int i = 0; i < frames.Count; i++) image.SetPixels32(0, (frames.Count - 1 - i) * h, w * 4, h, frames[i]);
            image.Apply();
            string dir = Path.GetFullPath("Temp/WhimTex/rbf-prototype-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "comparison.png"), image.EncodeToPNG());
            File.WriteAllText(Path.Combine(dir, "observations.txt"), "Columns: source, Round AA, Contour AA, RBF. Rows: circle, star, acute-star, empty.\n" +
                "Not a quality benchmark; first invocation may include Burst compilation.\n" + timings + "Max difference from Contour: " + largestDifference);
            UnityEngine.Debug.Log("RBF prototype preview: " + dir);
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    });

    static void CheckContourMembership(TestContext t, Assembly assembly)
    {
        var type = assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField");
        var changes = type.GetMethod("ReconstructedSignChanges", BindingFlags.NonPublic | BindingFlags.Static);
        var normalize = type.GetMethod("NormalizeContours", BindingFlags.NonPublic | BindingFlags.Static);
        const int width = 12, height = 10;
        List<float2> Rect(float x0, float y0, float x1, float y1) => new List<float2>
        {
            new float2(x0, y0), new float2(x1, y0), new float2(x1, y1), new float2(x0, y1)
        };
        bool Inside(List<List<float2>> paths, int x, int y)
        {
            bool inside = false;
            foreach (var path in paths)
                for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
                {
                    float2 a = path[j], b = path[i];
                    if ((a.y > y) != (b.y > y) && x < a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y))
                        inside = !inside;
                }
            return inside;
        }
        void Check(string name, List<List<float2>> original, List<List<float2>> fitted)
        {
            var actual = (byte[])changes.Invoke(null, new object[] { original, fitted, width, height });
            int mismatches = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                if ((actual[y * width + x] != 0) != (Inside(original, x, y) ^ Inside(fitted, x, y))) mismatches++;
            t.Equal(0, mismatches, name + " preserves region membership");
        }
        var adjacent = new List<List<float2>> { Rect(1.25f, 1, 4.25f, 7), Rect(4.25f, 1, 8.25f, 7) };
        var united = (List<List<float2>>)normalize.Invoke(null, new object[] { adjacent });
        t.Equal(1, united.Count, "Normalization merges two touching contours into one");
        Check("Merged contours", adjacent, united);
        Check("Split contours", united, adjacent);
        var outer = Rect(-2, -1, 9, 8);
        var hole = Rect(3, 2, 6, 5);
        hole.Reverse();
        var ring = new List<List<float2>> { outer, hole };
        Check("Removed hole", ring, new List<List<float2>> { outer });
        Check("Added hole", new List<List<float2>> { outer }, ring);
        Check("Reordered contours", ring, new List<List<float2>> { hole, outer });
        Check("All contours removed", ring, new List<List<float2>>());
        Check("Initially empty region", new List<List<float2>>(), ring);
        Check("Changed contour with unchanged hole", ring, new List<List<float2>> { Rect(1.5f, 0, 10, 9.5f), hole });
    }

    static float PolygonDistance(Vector2 p, Vector2[] points)
    {
        float best = float.PositiveInfinity;
        bool inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            Vector2 a = points[j], b = points[i], e = b - a;
            best = Mathf.Min(best, (p - a - Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude) * e).magnitude);
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside ? -best : best;
    }
}
