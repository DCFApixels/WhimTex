// Owned unsaved documents and RTs. Supports TestApi and authenticated raster manifest.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class ReconstructionRenderCacheTests
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    public static string Run() => TestContext.Run("Native reconstruction: staged render reuse, identical output and interaction timing", t =>
    {
        var assembly = typeof(SDFLayerBehaviour).Assembly;
        var timing = assembly.GetType("DCFApixels.WhimTex.DistanceFieldTiming", true).GetField("receiver", BindingFlags.NonPublic | BindingFlags.Static);
        object previousTiming = timing.GetValue(null);
        var objects = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { objects.Add(value); return value; }
        RenderTexture previous = RenderTexture.active, input = null, output = null;
        bool previousSrgb = GL.sRGBWrite;
        var timer = Stopwatch.StartNew();
        var records = new List<string> { "layer,algorithm,edit,fullRebuildMs,reusedMs,maxPixelDelta" };
        var phases = new Dictionary<string, int>();
        void Release(LayerBehaviour behaviour) => behaviour.GetType().GetMethod("ReleaseTransientResources", Hidden).Invoke(behaviour, null);
        try
        {
            byte[] png = File.ReadAllBytes("Packages/com.dcfapixels.whimtex/Tests~/Fixtures/DistanceRaster/star-13-inner-0364-phase3-known-failure.png");
            using (var sha = System.Security.Cryptography.SHA256.Create())
                t.Equal("98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC",
                    BitConverter.ToString(sha.ComputeHash(png)).Replace("-", ""), "Original raster SHA256");
            var source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            t.True(source.LoadImage(png), "Decode source");
            int size = source.width;
            t.Equal(512, size, "Fixture dimensions");
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = doc.height = size;
            var read = Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            input = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            GL.sRGBWrite = false; Graphics.Blit(source, input);
            object context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true),
                doc, input, size, size, 1f, false, false, null);
            timing.SetValue(null, (Action<string, double>)((name, milliseconds) => {
                phases.TryGetValue(name, out int count); phases[name] = count + 1;
            }));
            int Phase(string name) => phases.TryGetValue(name, out int count) ? count : 0;
            Color[] Render(LayerBehaviour behaviour, out double ms)
            {
                t.True(timer.Elapsed.TotalSeconds < 55, "Bounded native test execution");
                phases.Clear(); RenderTexture.active = input; GL.sRGBWrite = false;
                var elapsed = Stopwatch.StartNew();
                output = (RenderTexture)behaviour.GetType().GetMethod("Render", Hidden).Invoke(behaviour, new[] { context });
                ms = elapsed.Elapsed.TotalMilliseconds;
                t.True(RenderTexture.active == input && !GL.sRGBWrite, "Caller GPU state preserved");
                RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                Color[] pixels = read.GetPixels();
                RenderTexture.active = input; RenderTexture.ReleaseTemporary(output); output = null;
                return pixels;
            }
            foreach (var metric in new[] { DistanceMetric.CornerFit, DistanceMetric.Spring })
            foreach (bool outline in new[] { false, true })
            {
                LayerBehaviour Create(int edit)
                {
                    if (outline) return new OutlineLayerBehaviour { metric = metric, sharpMode = SharpDistanceMode.GuardedSharp, miterLimit = edit == 3 ? 8 : 4,
                        outlineWidth = 4 + edit * 3, outlineSoftness = 1, fillCenter = true };
                    return new SDFLayerBehaviour { metric = metric, sharpMode = SharpDistanceMode.GuardedSharp, miterLimit = edit == 3 ? 8 : 4,
                        threshold = 128, maxDistanceNormalization = 16 + edit * 7,
                        encoding = SDFLayerBehaviour.OutputEncoding.LinearData };
                }
                var warm = Create(0); doc.layers.Add(warm.Owner);
                Render(warm, out _);
                t.Equal(1, Phase("reconstruction.prepare"), "First render prepares contour");
                t.Equal(1, Phase("distance.jobs"), "First render computes field");
                try
                {
                    for (int edit = 1; edit <= 3; edit++)
                    {
                        if (warm is SDFLayerBehaviour sdf) { sdf.maxDistanceNormalization = 16 + edit * 7; sdf.miterLimit = edit == 3 ? 8 : 4; }
                        if (warm is OutlineLayerBehaviour line) { line.outlineWidth = 4 + edit * 3; line.miterLimit = edit == 3 ? 8 : 4; }
                        var reused = Render(warm, out double reusedMs);
                        t.Equal(0, Phase("reconstruction.prepare"), "Parameter edit reuses geometry");
                        t.Equal(edit == 3 ? 1 : 0, Phase("distance.jobs"), "Only miter edit recalculates distance");
                        var fresh = Create(edit); doc.layers.Add(fresh.Owner);
                        try
                        {
                            var reference = Render(fresh, out double fullMs);
                            t.Equal(1, Phase("reconstruction.prepare"), "Independent fresh instance really rebuilds geometry");
                            float maximum = 0;
                            for (int i = 0; i < reference.Length; i++)
                            {
                                Color a = reference[i], b = reused[i];
                                t.True(float.IsFinite(b.r) && float.IsFinite(b.g) && float.IsFinite(b.b) && float.IsFinite(b.a), "Finite reused output");
                                maximum = Math.Max(maximum, Math.Max(Math.Max(Math.Abs(a.r - b.r), Math.Abs(a.g - b.g)),
                                    Math.Max(Math.Abs(a.b - b.b), Math.Abs(a.a - b.a))));
                            }
                            t.Near(0, maximum, .000002, "Reuse preserves all rendered channels");
                            records.Add((outline ? "Outline" : "SDF") + "," + metric + "," + (edit == 3 ? "miter" : "range-" + edit)
                                + "," + fullMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                                + "," + reusedMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                                + "," + maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        finally { Release(fresh); }
                    }
                }
                finally { Release(warm); }
            }
            string directory = "Temp/WhimTex/distance-fields/preparation-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory); File.WriteAllLines(Path.Combine(directory, "interaction.csv"), records);
        }
        finally
        {
            timing.SetValue(null, previousTiming);
            RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            if (input != null) RenderTexture.ReleaseTemporary(input);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    });
}
