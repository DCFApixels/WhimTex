using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class SharpQualityPrototypeTests
{
    const int Size = 96;
    const string Baseline = "Temp/WhimTex/sharp-quality-baseline-20261014.bin";
    static readonly string[] Shapes = { "star", "star", "acute", "acute", "small", "ring", "islands", "edge" };
    static readonly byte[] Thresholds = { 77, 192, 77, 128, 128, 192, 128, 128 };
    public static string Run(string phase) => TestContext.Run("Sharp quality prototype " + phase, t =>
    {
        bool capture = phase == "baseline";
        t.True(capture || phase == "after", "Explicit baseline or after phase");
        var compute = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility")
            .GetMethod("ComputeArtisticDistance", BindingFlags.Public | BindingFlags.Static);
        Directory.CreateDirectory("Temp/WhimTex");
        using var stream = new FileStream(Baseline, capture ? FileMode.CreateNew : FileMode.Open,
            capture ? FileAccess.Write : FileAccess.Read);
        using var writer = capture ? new BinaryWriter(stream) : null;
        using var reader = capture ? null : new BinaryReader(stream);
        var picture = new Color32[Size * 5 * Size * Shapes.Length];
        double difference = 0;
        for (int row = 0; row < Shapes.Length; row++)
        {
            Color32[] pixels = Input(Shapes[row]);
            using var input = new NativeArray<Color32>(pixels, Allocator.TempJob);
            using var output = new NativeArray<float>(pixels.Length, Allocator.TempJob);
            for (int algorithm = 0; algorithm < 2; algorithm++)
            {
                var metric = algorithm == 0 ? DistanceMetric.Sharp : DistanceMetric.MTSDF;
                compute.Invoke(null, new object[] { input, output, Size, Size, Thresholds[row], 0, metric, 32f, 0f, SharpDistanceMode.GuardedSharp });
                int invalid = 0;
                for (int i = 0; i < output.Length; i++)
                {
                    float current = output[i];
                    if (!float.IsFinite(current)) invalid++;
                    if (capture) writer.Write(current);
                    float old = capture ? current : reader.ReadSingle();
                    difference = Math.Max(difference, Math.Abs(current - old));
                    int x = i % Size, y = i / Size, at = ((Shapes.Length - 1 - row) * Size + y) * Size * 5 + x;
                    byte source = (byte)(255 - pixels[i].a);
                    picture[at] = new Color32(source, source, source, 255);
                    picture[at + (1 + algorithm * 2) * Size] = Gray(old);
                    picture[at + (2 + algorithm * 2) * Size] = Gray(current);
                }
                t.Equal(0, invalid, Shapes[row] + "/" + Thresholds[row] + "/" + metric + " finite");
                // This is a small prototype sanity check, not an accuracy certificate.
                if (Shapes[row] == "ring")
                {
                    t.True(output[Size * (Size / 2) + Size / 2] > 0, metric + " preserves hole");
                    t.True(output[Size * (Size / 2) + Size / 2 + 23] < 0, metric + " preserves ring interior");
                }
                if (!capture && algorithm == 1 && (row == 2 || row == 5))
                {
                    var multi = compute.DeclaringType.GetMethod("ComputeMtsdfDistance", BindingFlags.Public | BindingFlags.Static);
                    using var guarded = new NativeArray<float4>(pixels.Length, Allocator.TempJob);
                    using var raw = new NativeArray<float4>(pixels.Length, Allocator.TempJob);
                    multi.Invoke(null, new object[] { input, guarded, Size, Size, Thresholds[row], 0, 0f, SharpDistanceMode.GuardedSharp });
                    multi.Invoke(null, new object[] { input, raw, Size, Size, Thresholds[row], 0, 0f, SharpDistanceMode.Unrestricted });
                    int alphaChanges = 0, scalarChanges = 0, invalidChannels = 0;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        if (guarded[i].w != raw[i].w) alphaChanges++;
                        float3 rgb = guarded[i].xyz;
                        float median = math.max(math.min(rgb.x, rgb.y), math.min(math.max(rgb.x, rgb.y), rgb.z));
                        if (median != output[i]) scalarChanges++;
                        if (!math.all(math.isfinite(guarded[i])) || !math.all(math.isfinite(raw[i]))) invalidChannels++;
                    }
                    t.Equal(0, alphaChanges, "Corner Mode preserves true-distance alpha: " + Shapes[row]);
                    t.Equal(0, scalarChanges, "Scalar MTSDF is corrected RGB median: " + Shapes[row]);
                    t.Equal(0, invalidChannels, "All four channels finite in both policies: " + Shapes[row]);
                    compute.Invoke(null, new object[] { input, output, Size, Size, Thresholds[row], 0, DistanceMetric.Sharp, 32f, 0f, SharpDistanceMode.Unrestricted });
                    int invalidRaw = 0;
                    for (int i = 0; i < output.Length; i++) if (!float.IsFinite(output[i])) invalidRaw++;
                    t.Equal(0, invalidRaw, "Unrestricted Sharp finite: " + Shapes[row]);
                }
            }
        }
        if (!capture) t.True(difference > .001, "Prototype differs from baseline");
        string directory = Path.GetFullPath("Temp/WhimTex/sharp-quality-" + phase + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var texture = new Texture2D(Size * 5, Size * Shapes.Length, TextureFormat.RGBA32, false, true);
        try
        {
            texture.SetPixels32(picture); texture.Apply();
            File.WriteAllBytes(Path.Combine(directory, "comparison.png"), texture.EncodeToPNG());
            File.WriteAllText(Path.Combine(directory, "readme.txt"), "Columns: source, old Sharp, new Sharp, old MTSDF, new MTSDF. Guarded Sharp, miter 32.\nRows: star77, star192, acute77, acute128, small128, ring192, islands128, edge128.\nMaximum difference: " + difference);
            Debug.Log("Sharp quality prototype: " + directory);
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    });

    static Color32 Gray(float distance)
    {
        byte c = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(.5f + distance / 16));
        return new Color32(c, c, c, 255);
    }
    static Color32[] Input(string shape)
    {
        var vertices = new Vector2[shape == "acute" ? 26 : 10];
        float radius = shape == "small" ? 8 : 25;
        for (int i = 0; i < vertices.Length; i++)
        {
            float angle = Mathf.PI * .5f + i * 2 * Mathf.PI / vertices.Length;
            vertices[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius *
                (i % 2 == 0 ? 1 : shape == "acute" ? .364f : .5f);
        }
        var result = new Color32[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            Vector2 p = new Vector2(x - Size * .5f + .23f, y - Size * .5f - .31f);
            float distance;
            if (shape == "ring") distance = Mathf.Abs(p.magnitude - 23) - 4;
            else if (shape == "edge") distance = p.x + p.y * .4f;
            else if (shape == "islands") distance = Mathf.Min(Polygon(p + new Vector2(19, 0), vertices), Polygon(p - new Vector2(19, 2), vertices));
            else distance = Polygon(p, vertices);
            result[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(.5f - distance)));
        }
        return result;
    }
    static float Polygon(Vector2 p, Vector2[] vertices)
    {
        float distance = float.PositiveInfinity; bool inside = false;
        for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
        {
            Vector2 a = vertices[j], b = vertices[i], e = b - a;
            distance = Mathf.Min(distance, (p - a - Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude) * e).magnitude);
            if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
        }
        return inside ? -distance : distance;
    }
}
