using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class ClassicDistanceCacheTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run() => TestContext.Run("Classic SDF field preparation and remapping", t =>
    {
        const int size = 48;
        var assembly = typeof(SDFLayerBehaviour).Assembly;
        var cacheType = assembly.GetType("DCFApixels.WhimTex.ReconstructedDistanceCache", true);
        var count = cacheType.GetProperty("DistanceCount", Hidden);
        var bytes = cacheType.GetProperty("RetainedBytes", Hidden);
        var owners = new List<LayerBehaviour>();
        var objects = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { objects.Add(value); return value; }
        RenderTexture previous = RenderTexture.active, input = null, output = null;
        bool srgb = GL.sRGBWrite;
        double coldMs = 0, reusedMs = 0;
        int pairs = 0;
        object Cache(LayerBehaviour b) => b.GetType().GetField("reconstructedCache", Hidden).GetValue(b);
        int Builds(LayerBehaviour b) => (int)count.GetValue(Cache(b));
        void Release(LayerBehaviour b) => b.GetType().GetMethod("ReleaseTransientResources", Hidden).Invoke(b, null);
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = doc.height = size;
            var source = Own(new Texture2D(size, size, TextureFormat.RGBA32, false, true));
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float d = Mathf.Max(Mathf.Abs(x - 23.25f) - (34.7f - y) * .48f, Mathf.Max(9.3f - y, y - 34.7f));
                byte a = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(.5f - d));
                pixels[y * size + x] = new Color32(a, (byte)(255 - a), 255, a);
            }
            source.SetPixels32(pixels); source.Apply();
            var read = Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            input = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            GL.sRGBWrite = false; Graphics.Blit(source, input);
            object context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true),
                doc, input, size, size, 1f, false, false, null);
            Color[] Render(LayerBehaviour b, out double ms)
            {
                RenderTexture.active = input; GL.sRGBWrite = false;
                var timer = Stopwatch.StartNew();
                output = (RenderTexture)b.GetType().GetMethod("Render", Hidden).Invoke(b, new[] { context });
                t.True(RenderTexture.active == input && !GL.sRGBWrite, "Caller rendering state preserved");
                RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                var result = read.GetPixels(); ms = timer.Elapsed.TotalMilliseconds;
                RenderTexture.active = input; RenderTexture.ReleaseTemporary(output); output = null;
                return result;
            }
            void Same(Color[] a, Color[] b, string label)
            {
                float delta = 0;
                for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++)
                {
                    if (!float.IsFinite(b[i][c])) { t.True(false, label + " finite output"); return; }
                    delta = Mathf.Max(delta, Mathf.Abs(a[i][c] - b[i][c]));
                }
                t.Near(0, delta, .000002, label + " reused matches fresh output");
            }
            foreach (DistanceMetric metric in new[] { DistanceMetric.EuclideanAntialiased, DistanceMetric.Sharp,
                DistanceMetric.Bevel, DistanceMetric.Diamond, DistanceMetric.Square, DistanceMetric.MTSDF })
            foreach (bool outline in new[] { false, true })
            {
                LayerBehaviour Create(bool edited)
                {
                    LayerBehaviour b = outline
                        ? (LayerBehaviour)new OutlineLayerBehaviour { metric = metric, sharpMode = SharpDistanceMode.GuardedSharp, outlineWidth = edited ? 9 : 3 }
                        : new SDFLayerBehaviour { metric = metric, sharpMode = SharpDistanceMode.GuardedSharp,
                            encoding = SDFLayerBehaviour.OutputEncoding.LinearData, maxDistanceNormalization = edited ? 24 : 8 };
                    owners.Add(b); doc.layers.Add(b.Owner); return b;
                }
                var warm = Create(false);
                var before = Render(warm, out _);
                t.Equal(1, Builds(warm), metric + " initial field calculated once");
                if (warm is SDFLayerBehaviour sdf) sdf.maxDistanceNormalization = 24;
                if (warm is OutlineLayerBehaviour line) line.outlineWidth = 9;
                var reused = Render(warm, out double hitMs);
                t.Equal(1, Builds(warm), metric + " presentation edit does not recalculate field");
                bool changed = false;
                for (int i = 0; i < before.Length; i++) changed |= before[i] != reused[i];
                t.True(changed, metric + " changed distance visibly changes output");
                var fresh = Create(true);
                var reference = Render(fresh, out double fullMs);
                t.Equal(1, Builds(fresh), metric + " independent field actually rebuilt");
                Same(reference, reused, metric + (outline ? "/Outline" : "/SDF"));
                coldMs += fullMs; reusedMs += hitMs; pairs++;
                if (!outline && metric == DistanceMetric.MTSDF)
                {
                    var warmSdf = (SDFLayerBehaviour)warm;
                    var freshSdf = (SDFLayerBehaviour)fresh;
                    warmSdf.encoding = freshSdf.encoding = SDFLayerBehaviour.OutputEncoding.MultiChannelData;
                    reused = Render(warm, out _);
                    t.Equal(1, Builds(warm), "MTSDF output mode reuses all four channels");
                    Release(fresh); reference = Render(fresh, out _);
                    Same(reference, reused, "MTSDF raw RGBA");
                    warmSdf.maxDistanceNormalization = freshSdf.maxDistanceNormalization = 15;
                    reused = Render(warm, out _);
                    t.Equal(1, Builds(warm), "MTSDF raw range reuses field");
                    Release(fresh); reference = Render(fresh, out _);
                    Same(reference, reused, "MTSDF remapped raw RGBA");
                }
                if (!outline && metric == DistanceMetric.Sharp)
                {
                    var s = (SDFLayerBehaviour)warm;
                    s.threshold = 77; Render(warm, out _); t.Equal(2, Builds(warm), "Threshold rebuilds field");
                    s.contourMergeDistance = .5f; Render(warm, out _); t.Equal(3, Builds(warm), "Merge rebuilds field");
                    s.miterLimit = 8; Render(warm, out _); t.Equal(4, Builds(warm), "Guarded miter rebuilds field");
                    s.sharpMode = SharpDistanceMode.Unrestricted; Render(warm, out _); t.Equal(5, Builds(warm), "Corner mode rebuilds field");
                    pixels[20 * size + 23].a = 0; source.SetPixels32(pixels); source.Apply(); Graphics.Blit(source, input);
                    Render(warm, out _); t.Equal(6, Builds(warm), "Mutated source rebuilds field");
                }
                object owned = Cache(warm); Release(warm); Release(fresh);
                t.Equal(0L, (long)bytes.GetValue(owned), "Explicit release frees retained arrays");
            }
            UnityEngine.Debug.Log("Classic SDF full-render comparison (not a benchmark): " + pairs + " pairs; fresh total " + coldMs.ToString("F2") + "ms, reused total " + reusedMs.ToString("F2") + "ms.");
        }
        finally
        {
            foreach (var b in owners) Release(b);
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            if (input != null) RenderTexture.ReleaseTemporary(input);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    });
}
