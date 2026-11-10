using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class HeightPreviewTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly A = typeof(WhimTexDocument).Assembly;
    static object New(string name) => Activator.CreateInstance(A.GetType("DCFApixels.WhimTex." + name), true);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Channel(string name) => Enum.Parse(A.GetType("DCFApixels.WhimTex.HeightPreviewRenderer+Channel"), name);
    static ShaderFX Fx(WhimTexDocument doc, string code)
    {
        var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
            new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>), typeof(string) }, null);
        var fx = (ShaderFX)create.Invoke(null, new object[] { doc, code, new List<ShaderParameter>(), null });
        Call(fx, "CompileAgentDraft"); return fx;
    }
    static void Release(RenderTexture rt) { if (rt != null) A.GetType("DCFApixels.WhimTex.ProfiledRenderTexture").GetMethod("ReleaseTemporary", F).Invoke(null, new object[] { rt }); }
    static Color[] Read(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); return cpu.GetPixels(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
    }
    static void Png(RenderTexture rt, string path)
    {
        var values = Read(rt);
        for (int i = 0; i < values.Length; i++) if (QualitySettings.activeColorSpace == ColorSpace.Linear) values[i] = values[i].gamma;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        try { image.SetPixels(values); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { Object.DestroyImmediate(image); }
    }
    static string DirectoryFor(string name)
    {
        string path = "Temp/WhimTex/height-preview-implementation/" + name + "-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(path); return path;
    }
    static Color Sample(Color[] data, int width, int height, float u, float v)
    {
        float x = u * width - .5f, y = v * height - .5f;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        Color At(int a, int b) => data[Mathf.Clamp(b, 0, height - 1) * width + Mathf.Clamp(a, 0, width - 1)];
        return Color.LerpUnclamped(Color.LerpUnclamped(At(x0, y0), At(x0 + 1, y0), x - x0),
            Color.LerpUnclamped(At(x0, y0 + 1), At(x0 + 1, y0 + 1), x - x0), y - y0);
    }

    public static string Render(int depth) => TestContext.Run("Textured parallel height preview / " + depth, t =>
    {
        string folder = DirectoryFor("render-" + depth);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var renderer = (IDisposable)New("HeightPreviewRenderer");
        const int size = 128, width = 37;
        var source = new Texture2D(width, depth, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        try
        {
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/HeightPreview.compute");
            foreach (var m in ShaderUtil.GetComputeShaderMessages(compute)) t.True(false, "Compute diagnostic: " + m.message);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/HeightPreviewDisplay.shader");
            t.True(!ShaderUtil.ShaderHasError(shader), "Display shader compiles");
            var data = new Color[width * depth];
            for (int pattern = 0; pattern < 3; pattern++)
            {
                for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width, v = (y + .5f) / depth;
                    float h = pattern == 0 ? .2f : pattern == 1 ? (Math.Abs(y - depth / 3) <= 2 ? .85f : 0) : .65f * Mathf.Sin(v * 19) + .15f;
                    data[y * width + x] = new Color(h, 1.5f + u * 2, -.25f - v, .7f);
                }
                source.SetPixels(data); source.Apply();
                Call(renderer, "Render", source, new Vector2Int(depth, depth), size, Channel("R"), .5f);
                var geometry = (RenderTexture)Get(renderer, "Geometry");
                var values = Read(geometry);
                int visible = 0, raised = 0;
                double maxProjectionError = 0;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    Color g = values[y * size + x];
                    if (g.a < .5f) continue;
                    visible++;
                    float h = Sample(data, width, depth, g.r, g.g).r;
                    double screenY = (.4 + ((g.g - .5) + h * .5) * .70710678118 * .84) * size;
                    maxProjectionError = Math.Max(maxProjectionError, Math.Abs(screenY - (y + .5)));
                    if (h > .3f) raised++;
                    t.True(float.IsFinite(g.r + g.g + g.b) && g.r >= 0 && g.r <= 1 && g.g >= 0 && g.g <= 1, "Finite surface UV");
                }
                t.True(visible > size * size / 5, "Surface is present");
                t.True(maxProjectionError < .3, "Ray intersection agrees with sampled surface, px=" + maxProjectionError);
                if (pattern == 1) t.True(raised > 1000, "Five-pixel ridge survives at native depth resolution");
                var output = (RenderTexture)Call(renderer, "Display", source, 15, true, true, false);
                var colored = Read(output);
                Png(output, folder + "/output-" + pattern + ".png");
                Png(geometry, folder + "/geometry-" + pattern + ".png");
                double error = 0; string worst = ""; bool aboveOne = false, negative = false;
                for (int i = 0; i < values.Length; i++)
                {
                    Color g = values[i], color = colored[i];
                    if (g.a < .5) { t.Near(0, color.a, 0, "Background remains transparent at " + i + "; flipped coverage=" + values[(size - 1 - i / size) * size + i % size].a); continue; }
                    Color wanted = Sample(data, width, depth, g.r, g.g); float alpha = wanted.a;
                    wanted *= g.b; wanted.a = alpha;
                    if (QualitySettings.activeColorSpace == ColorSpace.Gamma) wanted = wanted.gamma;
                    for (int c = 0; c < 4; c++) if (Math.Abs(color[c] - wanted[c]) > error)
                    { error = Math.Abs(color[c] - wanted[c]); worst = " index=" + i + " geometry=" + g.ToString("F6") + " wanted=" + wanted.ToString("F6") + " actual=" + color.ToString("F6"); }
                    aboveOne |= color.g > 1; negative |= color.b < 0;
                }
                t.True(error < .015, "Texture follows surface UV including HDR, error=" + error + worst);
                t.True(aboveOne && negative, "HDR values above one and below zero survive");
                Png(output, folder + "/pattern-" + pattern + ".png");
                Call(renderer, "Display", source, 8, true, true, false);
                t.True(ReferenceEquals(geometry, Get(renderer, "Geometry")), "Channel display does not rebuild geometry");
                var again = Read(geometry);
                for (int i = 0; i < values.Length; i++) t.Equal(values[i], again[i], "Geometry unchanged by display channels");
                Call(renderer, "Render", source, new Vector2Int(depth, depth / 2), size, Channel("A"), -.25f);
                t.True(ReferenceEquals(geometry, Get(renderer, "Geometry")), "Same-sized output is reused across geometry edits");
            }
            var levels = new[] { -.25f, 0, .5f, 1, 1.25f };
            for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++)
            {
                int band = x * levels.Length / width;
                data[y * width + x] = new Color(levels[band], levels[(band + 1) % 5], levels[(band + 2) % 5], levels[(band + 3) % 5]);
            }
            source.SetPixels(data); source.Apply();
            foreach (string channel in new[] { "R", "G", "B", "A", "Luminance" })
            {
                int channelIndex = Array.IndexOf(new[] { "R", "G", "B", "A" }, channel);
                // Zero scale must not hide values outside the source's normalized range.
                Call(renderer, "Render", source, new Vector2Int(depth, depth), size, Channel(channel), 0f);
                var geometry = (RenderTexture)Get(renderer, "Geometry"); var positions = Read(geometry);
                int below = 0, above = 0, inside = 0, endpoints = 0;
                foreach (bool textured in new[] { true, false })
                foreach (bool encodeGamma in new[] { true, false })
                {
                    // Surface channel switches cannot suppress the diagnostic magenta.
                    var baseline = Read((RenderTexture)Call(renderer, "Display", source, 8, textured, encodeGamma, false));
                    var output = (RenderTexture)Call(renderer, "Display", source, 8, textured, encodeGamma, true);
                    var highlighted = Read(output);
                    for (int i = 0; i < positions.Length; i++)
                    {
                        var position = positions[i];
                        if (position.a < .5f) { t.Equal(baseline[i], highlighted[i], "Background unchanged"); continue; }
                        var sampled = Sample(data, width, depth, position.r, position.g);
                        float h = channel == "Luminance" ? sampled.r * .2126f + sampled.g * .7152f + sampled.b * .0722f : sampled[channelIndex];
                        if (h < 0 || h > 1)
                        {
                            if (h < 0) below++; else above++;
                            t.Equal(Color.magenta, highlighted[i], "Out-of-range height is opaque unlit magenta: " + channel);
                        }
                        else
                        {
                            inside++; if (h == 0 || h == 1) endpoints++;
                            t.Equal(baseline[i], highlighted[i], "Normalized height retains its existing appearance: " + channel);
                        }
                    }
                    if (channel == "R" && textured && encodeGamma) Png(output, folder + "/range-highlight.png");
                    var restored = Read((RenderTexture)Call(renderer, "Display", source, 8, textured, encodeGamma, false));
                    var unchanged = Read(geometry);
                    for (int i = 0; i < positions.Length; i++)
                    {
                        t.Equal(baseline[i], restored[i], "Disabling highlight restores the exact output");
                        t.Equal(positions[i], unchanged[i], "Highlight does not change relief geometry");
                    }
                }
                t.True(inside > 0, "Normalized samples tested: " + channel);
                if (channel != "Luminance") t.True(below > 0 && above > 0 && endpoints > 0, "Both bounds and exact endpoints tested: " + channel);
            }
            // A readable colored surface for visual review, separate from the extreme HDR fixtures.
            for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width, v = (y + .5f) / depth;
                float h = .65f * Mathf.Exp(-((u-.4f)*(u-.4f)+(v-.5f)*(v-.5f))*28) + .15f*Mathf.Sin(v*18)*Mathf.Sin(u*12);
                data[y * width + x] = new Color(h, .18f + .7f*u, .2f + .7f*v, 1);
            }
            source.SetPixels(data); source.Apply();
            Call(renderer, "Render", source, new Vector2Int(depth, depth), 256, Channel("R"), .5f);
            Png((RenderTexture)Call(renderer, "Display", source, 15, true, true, false), folder + "/textured-relief.png");
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Renderer restores caller GPU state");
            File.WriteAllText(folder + "/result.txt", "Native depth: " + depth + "; textured HDR, alpha, signed height, narrow ridge, channel independence passed.");
        }
        finally { renderer.Dispose(); Object.DestroyImmediate(source); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });

    public static string Inputs() => TestContext.Run("Height input uses normal composition semantics", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width = 127; doc.height = 93;
        var old = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var owned = new List<Layer>();
        var fill = new ColorFillLayerBehaviour { color = new Color(2, -.3f, .4f, .6f) };
        Layer layer = fill; layer.colorRange = LayerColorRange.HDR; owned.Add(layer);
        Layer group = new GroupLayerBehaviour { compositing = GroupCompositing.Isolated }; group.colorRange = LayerColorRange.HDR; owned.Add(group);
        Layer mask = new ColorFillLayerBehaviour { color = new Color(.2f, .4f, .7f, .5f) }; owned.Add(mask);
        var reduced = RenderTexture.GetTemporary(23, 93, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var effects = new List<ShaderFX>();
        try
        {
            effects.Add(Fx(doc, "float4 ApplyFX(PinnedUV uv, float4 color) { return color * float4(.5,.75,1.25,1); }"));
            effects.Add(Fx(doc, "float4 ApplyFX(PinnedUV uv, float4 color) { return SampleInput((float2)uv + float2(17.0/_InputSize.x,0)); }"));
            effects.Add(Fx(doc, "float4 ApplyFX(PinnedUV uv, float4 color) { return color + float4(_WhimTexTime*.1,0,0,0); }"));
            t.True(effects[0].IsPixelLocal && !effects[1].IsPixelLocal, "Both local and spatial input planning are exercised");
            Layer blur = new BlurLayerBehaviour { radius = 3, inputMode = EffectInputMode.Specific, TargetLayerId = layer.Id }; owned.Add(blur);
            for (int variant = 0; variant < 10; variant++)
            {
                doc.layers.Clear(); group.children.Clear(); group.fx.Clear(); layer.fx.Clear(); layer.clippingMask = variant == 1;
                fill.mode = variant >= 3 ? ColorFillLayerBehaviour.FillMode.UV : ColorFillLayerBehaviour.FillMode.Color;
                if (variant == 4 || variant == 5) layer.fx.Add(effects[variant - 4]);
                if (variant == 2 || variant == 7 || variant == 9) { group.children.Add(layer); doc.layers.Add(group); }
                else { doc.layers.Add(layer); if (variant == 1) doc.layers.Add(mask); }
                if (variant == 6) doc.layers.Insert(0, blur);
                if (variant == 7) { layer.fx.Add(effects[2]); group.fx.Add(effects[2]); doc.SetPreviewTime(.2); }
                if (variant == 8 || variant == 9)
                {
                    var sequence = variant == 8 ? layer.fx : group.fx;
                    sequence.Add(effects[1]); sequence.Add(effects[2]);
                }
                Call(doc, "NormalizeModel");
                ((BlurLayerBehaviour)blur.Behaviour).TargetLayerId = layer.Id;
                Layer selected = variant == 2 || variant == 7 || variant == 9 ? group : variant == 6 ? blur : layer;
                var baseline = (RenderTexture)(variant >= 7 ? Call(doc, "RenderShaderGraphPreview", selected, 127, .75f) : Call(doc, "RenderLayerPreviewFallback", selected, 127));
                RenderTexture.active = old; GL.sRGBWrite = srgb;
                var height = (RenderTexture)Call(doc, "RenderHeightPreviewInput", selected, 23, variant >= 7 ? (float?).75f : null);
                t.True(RenderTexture.active == old && GL.sRGBWrite == srgb, "Input acquisition restores GPU state");
                try
                {
                    t.True(baseline != null && height != null, "Both inputs are rendered for variant " + variant);
                    Graphics.Blit(baseline, reduced);
                    RenderTexture.active = old; GL.sRGBWrite = srgb;
                    var a = Read(reduced); var b = Read(height);
                    t.Equal(a.Length, b.Length, "Column input shape");
                    for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++) t.Near(a[i][c], b[i][c], .004, "Composition/clip/group parity variant=" + variant + " pixel=" + i + " expected=" + a[i] + " actual=" + b[i]);
                    t.True(!(bool)Get(doc, "RenderingViewport") && !(bool)Get(doc, "CapturingInspectorFX"), "Preview scope restored");
                }
                finally { Release(baseline); Release(height); }
            }
            t.True(RenderTexture.active == old && GL.sRGBWrite == srgb, "Input acquisition restores GPU state");
        }
        finally { RenderTexture.ReleaseTemporary(reduced); foreach (var l in owned) Call(l, "ReleaseTransientResources"); foreach (var fx in effects) Object.DestroyImmediate(fx); Object.DestroyImmediate(doc); RenderTexture.active = old; GL.sRGBWrite = srgb; }
    });

    [Serializable] public sealed class Timing { public string mode; public int size, brush; public double median, p95; public long managedBytes; }
    [Serializable] public sealed class Timings { public string gpu, unity; public List<Timing> rows = new(); }
    public static string Painting(int side, int diameter) => TestContext.Run("Image/Height brush cost / " + side + "/" + diameter, t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width = doc.height = side;
        var brush = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        Layer layer = brush; doc.layers.Add(layer); Call(doc, "NormalizeModel");
        var cache = (IDisposable)New("EffectRenderCache");
        var renderer = (IDisposable)New("HeightPreviewRenderer");
        var old = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var fence = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var mask = new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
        var report = new Timings { gpu = SystemInfo.graphicsDeviceName, unity = Application.unityVersion };
        string directory = DirectoryFor("paint-" + side + "-" + diameter);
        void Complete(Texture source) { Graphics.Blit(source, fence); RenderTexture.active = fence; cpu.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); RenderTexture.active = old; }
        try
        {
            var masking = new Color[256]; for (int i = 0; i < 256; i++) masking[i] = new Color((i % 16) / 15f, 0, 0, 1);
            mask.SetPixels(masking); mask.Apply();
            var settings = New("PaintToolSettings"); Set(settings, "brushSize", (float)diameter); Set(settings, "brushSpacing", .2f);
            Set(settings, "brushHardness", .3f); Set(settings, "brushColor", new Color(2, .5f, -.1f, .4f));
            Set(Get(settings, "dynamics"), "opacity", .7f);
            var p = Call(settings, "GetStrokeParameters", false, (Color?)null); p = Call(p, "WithSelectionMask", mask);
            var regionType = A.GetType("DCFApixels.WhimTex.CanvasRenderRegion");
            var region = Activator.CreateInstance(regionType, F, null, new object[] { new RectInt(0, 0, side, side), Math.Max(1, side / 512) }, null);
            var surface = (RenderTexture)Call(brush, "EnsurePaintSurface", side, side);
            t.Equal(RenderTextureFormat.ARGBHalf, surface.format, "Painting stays Half"); Complete(surface);
            t.Equal(side, surface.width, "Real native-size paint surface");
            for (int round = 0; round < 2; round++)
            foreach (string mode in round == 0 ? new[] { "Image", "Height" } : new[] { "Height", "Image" })
            {
                var samples = new List<double>(); long allocated = 0;
                Vector2 Point(int n) { float a = n * .173f; return new Vector2(.5f + .4f * Mathf.Sin(a * 3), .5f + .4f * Mathf.Sin(a * 2 + .4f)); }
                Call(brush, "BeginStroke", Point(0));
                Call(brush, "PaintPoint", Point(0), side, side, p);
                for (int n = 0; n < 18; n++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
                    Call(brush, "PaintSegment", Point(Math.Max(0, n - 1)), Point(n), side, side, false, p);
                    t.True(Get(brush, "advancedStroke") != null, "Real tiled stroke is active");
                    var canvas = (RenderTexture)Call(doc, "RenderCanvasViewport", region, cache, true, brush);
                    RenderTexture input = null;
                    try
                    {
                        var held = Get(brush, "brushPreviewSurface");
                        if (mode == "Height")
                        {
                            input = (RenderTexture)Call(doc, "RenderHeightPreviewInput", layer, 256, null);
                            Call(renderer, "Render", input, new Vector2Int(side, side), 256, Channel("R"), .5f);
                            Complete((Texture)Call(renderer, "Display", input, 15, true, true, false));
                            t.True(ReferenceEquals(held, Get(brush, "brushPreviewSurface")), "Height must not replace the canvas stroke cache");
                            t.Equal(side, input.height, "Native depth retained during stroke");
                            t.True(input.width <= 256, "Height input width is bounded");
                        }
                        else
                        {
                            input = (RenderTexture)Call(doc, "RenderLayerPreviewFallback", layer, 256);
                            Complete(input);
                        }
                    }
                    finally { Release(canvas); Release(input); }
                    watch.Stop(); if (n > 3) { samples.Add(watch.Elapsed.TotalMilliseconds); allocated += GC.GetAllocatedBytesForCurrentThread() - before; }
                }
                var verification = (RenderTexture)Call(doc, "RenderHeightPreviewInput", layer, 32, null);
                try
                {
                    var pixels = Read(verification); bool painted = false;
                    foreach (var pixel in pixels) painted |= pixel.a > .001f;
                    t.True(painted, "Measured stroke really changes the rendered image");
                }
                finally { Release(verification); }
                Call(brush, "CancelStroke");
                samples.Sort(); report.rows.Add(new Timing { mode = mode, size = side, brush = diameter,
                    median = samples[samples.Count / 2], p95 = samples[Math.Min(samples.Count - 1, (int)(samples.Count * .95))], managedBytes = allocated / samples.Count });
            }
            // JsonUtility does not serialize nested row types from ephemeral test assemblies.
            var csv = new System.Text.StringBuilder("mode,size,brush,medianMs,p95Ms,managedBytesPerFrame\n");
            foreach (var row in report.rows) csv.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3:F5},{4:F5},{5}\n", row.mode, row.size, row.brush, row.median, row.p95, row.managedBytes);
            File.WriteAllText(directory + "/timing.csv", csv.ToString());
            File.WriteAllText(directory + "/device.txt", report.gpu + " / " + report.unity + "\nPer-frame CPU submission + GPU completion using 1x1 readback; 4 warmups excluded. No Undo readback/commit timed.");
            t.True(!(bool)Get(doc, "RenderingViewport") && !(bool)Get(doc, "CapturingInspectorFX"), "Painting preview scopes restored");
        }
        finally
        {
            Call(brush, "CancelStroke"); Set(brush, "paintSurfaceDirty", false); Call(brush, "ReleaseTransientResources");
            renderer.Dispose(); cache.Dispose(); Object.DestroyImmediate(doc); Object.DestroyImmediate(cpu); Object.DestroyImmediate(mask);
            RenderTexture.ReleaseTemporary(fence); RenderTexture.active = old; GL.sRGBWrite = srgb;
        }
    });
}
