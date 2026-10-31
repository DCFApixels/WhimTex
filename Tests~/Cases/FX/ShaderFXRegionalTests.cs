using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Detached documents only. Large targets are fenced through a separate 1x1 RT;
// never read a full 8K target back to the CPU.
public static partial class ShaderFXRegionalTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    static object Region(RectInt rect, int step = 1) => Activator.CreateInstance(T("CanvasRenderRegion"), F, null, new object[] { rect, step }, null);
    const string Local = "// @param texture2D _Map\nfloat4 ApplyFX(PinnedUV uv,float4 color){return float4(color.rgb*SampleTexture(_Map,uv).rgb,color.a);}";
    const string Spatial = "float4 ApplyFX(PinnedUV _pinnedUv, float4 color){ float2 uv = _pinnedUv;return SampleInput(uv+float2(19.0/_InputSize.x,0));}";

    public static string RunPreviewResolution() => TestContext.Run("Native magnified inspector inputs", PreviewResolution);

    static void PreviewResolution(TestContext t)
    {
        using var f = new Fixture();
        f.doc.width = 768; f.doc.height = 64;
        var layer = new Layer(new ColorFillLayerBehaviour()); f.doc.layers.Add(layer);
        layer.fx.Add(f.Fx("float4 ApplyFX(PinnedUV uv,float4 color){float2 p=floor(uv*_CanvasSize.xy);float v=fmod(p.x+p.y,2);return float4(v,v,v,1);}"));
        var consumer = f.Fx("float4 ApplyFX(float4 color){return color;}"); layer.fx.Add(consumer);
        Call(f.doc, "NormalizeModel");
        foreach (bool native in new[] { false, true, false })
        {
            int captured = 0;
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            using (f.Capture((fx, owner, context, material, input) =>
            {
                if (fx != consumer) return;
                captured++;
                t.Equal(native ? 768 : 512, input.width, "Native inputs bypass the reduced inspector cap only when requested");
                t.Equal(native ? 64 : 43, input.height, "Inspector input retains canvas proportions");
                if (!native) return;
                var pixels = f.Read(input);
                bool exact = true;
                for (int y = 0; y < input.height; y++) for (int x = 0; x < input.width; x++)
                    exact &= Mathf.Abs(pixels[y * input.width + x].r - ((x + y) % 2)) < .001f;
                t.True(exact, "Native inspector input retains every checker pixel before preview sampling");
            })) Call(f.doc, "CaptureInspectorFXPrefix", layer, 2, native);
            t.Equal(1, captured, "One input frame captured");
            t.True(RenderTexture.active == active && GL.sRGBWrite == srgb, "Inspector capture restores caller graphics state");
        }
    }

    public static string Run() => TestContext.Run("Regional FX dependencies, complete inspector inputs and render isolation", t =>
    {
        PreviewResolution(t);
        using var f = new Fixture();
        var source = new Layer(new GradientLayerBehaviour()) { enabled = false };
        var consumer = new Layer(new ColorFillLayerBehaviour { color = new Color(2, .7f, .4f, .63f) })
            { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        f.doc.layers.Add(consumer); f.doc.layers.Add(source);
        var local = f.Fx(Local); consumer.fx.Add(local);
        var parameter = ((IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(local))[0];
        Call(f.doc, "NormalizeModel");
        parameter.textureSource = ShaderFXTextureSource.Layer; parameter.textureLayerId = source.Id;
        t.True(!string.IsNullOrEmpty(source.Id), "Source ID assigned before linking");
        t.True(local.IsPixelLocal, "Pinned texture consumer verified");
        f.Compare(t, "local layer dependency");
        parameter.textureSource = ShaderFXTextureSource.Self;
        f.Compare(t, "Self reads current input");
        var texture = f.Own(new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp });
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); texture.Apply();
        parameter.textureSource = ShaderFXTextureSource.Texture; parameter.textureValue = texture;
        // A two-texel bilinear ramp hits hardware fraction-rounding ties. The HDR
        // multiplier is ~4.95, so one 1/256 interpolation step is ~0.0194.
        f.Compare(t, "full external Texture with regional main", .022f);
        parameter.textureSource = ShaderFXTextureSource.Layer;
        var spatial = f.Fx(Spatial); source.fx.Add(spatial);
        f.Compare(t, "spatial dependency does not promote consumer");
        var seen = new Dictionary<ShaderFX, int>();
        using (f.Capture((effect, layer, ctx, mat, input) => seen[effect] = input.width))
        {
            f.ClearCache(); f.RenderAndRelease(new RectInt(64, 32, 192, 224));
            t.Equal(512, seen[spatial], "Only dependency branch reads full width");
            t.Equal(192, seen[local], "Consumer reads region after dependency crop");
        }
        consumer.fx.Insert(0, f.Fx("float4 ApplyFX(float4 color){return color*.9;}"));
        consumer.fx.Insert(1, f.Fx(Spatial));
        f.Compare(t, "local-spatial-local chain");
        using (f.Capture((effect, layer, ctx, mat, input) => seen[effect] = input.width))
        {
            f.ClearCache(); f.RenderAndRelease(new RectInt(64, 32, 192, 224));
            t.Equal(512, seen[consumer.fx[0]], "Local prefix stays full before spatial consumer");
            t.Equal(512, seen[consumer.fx[1]], "Spatial stage reads full");
            t.Equal(192, seen[local], "Local suffix returns to region");
        }
        consumer.fx.RemoveRange(0, 2);
        var group = new Layer(new GroupLayerBehaviour());
        f.doc.layers.Remove(source); source.enabled = true; group.children.Add(source); group.enabled = false; f.doc.layers.Add(group);
        Call(f.doc, "NormalizeModel"); parameter.textureLayerId = group.Id;
        f.Compare(t, "hidden group texture with spatial child");
        consumer.clippingMask = true; group.enabled = true;
        f.Compare(t, "consumer clipping");
        consumer.clippingMask = false; group.enabled = false;
        parameter.textureLayerId = "missing"; f.Compare(t, "missing dependency");
        parameter.textureLayerId = consumer.Id; f.Compare(t, "cycle rejected");
        Inspector(t, f);
        InspectorBrush(t, f);
    });

    static void InspectorBrush(TestContext t, Fixture f)
    {
        const int w = 1031, h = 773;
        f.doc.layers.Clear(); f.doc.width = w; f.doc.height = h;
        var fx = f.Fx("float4 ApplyFX(PinnedUV uv,float4 color){return color;}");
        object Field(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
        void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
        var mask = f.Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp });
        mask.SetPixels(new[] { Color.black, Color.white, Color.gray, Color.white }); mask.Apply();
        foreach (string mode in new[] { "standard", "hdr", "multiply", "stamp", "erase", "locked", "point", "repeat", "mirror" })
        {
            bool hdr = mode != "standard", fallback = mode == "repeat" || mode == "mirror";
            var drawing = new DrawingLayerBehaviour();
            var layer = new Layer(drawing) { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard,
                blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard,
                filterMode = mode == "point" ? LayerFilterMode.Point : LayerFilterMode.Bilinear };
            layer.fx.Add(fx); f.doc.layers.Add(layer); Call(f.doc, "NormalizeModel");
            var source = f.Own(new Texture2D(w, h, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr));
            var colors = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                colors[y * w + x] = new Color(hdr ? -.3f : .1f, hdr ? 2.4f : .7f, x / (float)w,
                    x % 9 == 0 ? 0 : x % 11 == 0 ? .0001f : .3f);
            source.SetPixels(colors); source.Apply(); Call(drawing, "AdoptStoredTexture", source);
            var settings = Activator.CreateInstance(T("PaintToolSettings"), true);
            var dynamics = Field(settings, "dynamics");
            Set(settings, "brushSize", 420f); Set(settings, "brushColor", new Color(3.5f, -.2f, .7f, .013f));
            Set(dynamics, "opacity", .47f); Set(dynamics, "flow", .35f);
            if (mode == "multiply" || mode == "stamp") Set(dynamics, "blend", BlendMode.Multiply);
            if (mode == "stamp") Set(dynamics, "blendApplication", Enum.Parse(Field(dynamics, "blendApplication").GetType(), "Stamp"));
            var parameters = Call(settings, "GetStrokeParameters", mode == "erase", (Color?)null);
            parameters = Call(parameters, "WithSelectionMask", mask);
            if (mode == "locked") parameters = Call(parameters, "WithWriteProtection", 5);
            try
            {
                Call(drawing, "BeginStroke", new Vector2(.2f, .2f));
                for (int y = 0; y < 3; y++) for (int x = 0; x < 4; x++)
                    Call(drawing, "PaintPoint", new Vector2((x + .5f) / 4, (y + .5f) / 3), w, h, parameters);
                for (int variant = 0; variant < 4; variant++)
                {
                    bool active = variant < 2, transformed = variant % 2 != 0;
                    if (variant == 2) Call(drawing, "CancelStroke");
                    layer.transform.rotation = transformed ? 13 : 0;
                    layer.transform.scale = transformed ? new Vector2(.73f, .87f) : Vector2.one;
                    // Warm a canvas cache, then leave additional damage pending for it.
                    f.ClearCache(); f.RenderAndRelease(new RectInt(192, 160, 512, 384));
                    if (active) Call(drawing, "PaintPoint", new Vector2(.51f, .37f), w, h, parameters);
                    var retained = Field(drawing, "brushPreviewSurface");
                    int dirty = (int)Field(drawing, "brushPreviewTiles").GetType().GetProperty("Count", F).GetValue(Field(drawing, "brushPreviewTiles"));
                    if (active) t.True(dirty > 0, "Canvas has pending paint damage before inspector");
                    if (fallback) layer.transform.tiling = mode == "repeat" ? TransformTilingMode.Repeat : TransformTilingMode.Mirror;
                    Call(f.doc, "RefreshTransformHierarchy");
                    var native = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    try
                    {
                        Call(drawing, "BlitStraightSurface", native, 1f);
                        var expected = f.Read(native);
                        var frame = typeof(Layer).GetProperty("RenderTransform", F).GetValue(layer);
                        var matrix = (ProjectiveMatrix)Call(frame, "ToMatrix", w, h); matrix.TryInverse(out var inverse);
                        int captured = 0;
                        using (f.Capture((effect, owner, ctx, bound, input) =>
                        {
                            if (effect != fx) return;
                            captured++; var actual = f.Read(input); Color[] fallbackExpected = null;
                            // Point sampling must use the same GPU coordinate precision:
                            // a CPU double exactly beside an integer can choose the other texel.
                            if ((fallback && active) || mode == "point")
                            {
                                var material = new Material((Material)T("WhimTexMaterials").GetProperty("Transform", F).GetValue(null));
                                var oracle = RenderTexture.GetTemporary(input.width, input.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                                try
                                {
                                    Call(layer, "ConfigureSourceTransform", material, native, ctx, null, false, null, false);
                                    Graphics.Blit(native, oracle, material); oracle = (RenderTexture)Call(f.doc, "FinishStage", oracle, false, default(LayerChannelMapping));
                                    fallbackExpected = f.Read(oracle);
                                }
                                finally { Object.DestroyImmediate(material); RenderTexture.ReleaseTemporary(oracle); }
                            }
                            float error = 0, hiddenError = 0; int worst = 0, different = 0; Double2 worstUv = default;
                            for (int y = 0; y < input.height; y++) for (int x = 0; x < input.width; x++)
                            {
                                int i = y * input.width + x;
                                var uv = inverse.Point(new Double2((x + .5) / input.width, (y + .5) / input.height));
                                var reference = fallbackExpected != null ? fallbackExpected[i] : InspectorSample(expected, w, h, uv, mode == "point", layer.transform.tiling);
                                if (mode == "point")
                                {
                                    // Rasterized subrects and a full quad can round opposite
                                    // ways within 1/1024 source pixel of a Point boundary.
                                    // Require one of those exact adjacent texels, not an average.
                                    float best = InspectorDifference(actual[i], reference);
                                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                                    {
                                        var candidate = InspectorSample(expected, w, h,
                                            new Double2(uv.x + dx / (1024.0 * w), uv.y + dy / (1024.0 * h)), true);
                                        float delta = InspectorDifference(actual[i], candidate);
                                        if (delta < best) { best = delta; reference = candidate; }
                                    }
                                }
                                for (int c = 0; c < 4; c++)
                                {
                                    if (c < 3 && actual[i].a == 0 && reference.a == 0)
                                        hiddenError = Mathf.Max(hiddenError,
                                            Mathf.Abs(actual[i][c] - reference[c]) / Mathf.Max(1, Mathf.Abs(reference[c])));
                                    float a = c == 3 ? actual[i].a : actual[i][c] * actual[i].a;
                                    float b = c == 3 ? reference.a : reference[c] * reference.a;
                                    float delta = Mathf.Abs(a - b) / Mathf.Max(1, Mathf.Abs(b));
                                    if (delta > .003f) different++;
                                    if (delta > error) { error = delta; worst = i; worstUv = uv; }
                                }
                            }
                            t.True(error < .003f, mode + " inspector vs native reference; transformed=" + transformed + " error=" + error +
                                " active=" + active + " worst=" + worst + " uvPixels=" + worstUv.x * w + "," + worstUv.y * h + " actual=" + actual[worst] + " components=" + different);
                            t.True(hiddenError < .003f, mode + " preserves hidden RGB at zero alpha: " + hiddenError);
                            t.Equal(512, input.width, "Inspector is bounded without cropping");
                        }))
                        {
                            Call(f.doc, "CaptureInspectorFXPrefix", layer, 1, false);
                            if (active)
                            {
                                Call(drawing, "PaintPoint", new Vector2(.7f, .6f), w, h, parameters);
                                Call(drawing, "BlitStraightSurface", native, 1f); expected = f.Read(native);
                                dirty = (int)Field(drawing, "brushPreviewTiles").GetType().GetProperty("Count", F).GetValue(Field(drawing, "brushPreviewTiles"));
                                Call(f.doc, "CaptureInspectorFXPrefix", layer, 1, false);
                                // Compare both incremental and forced full rebuild with the same oracle.
                                Set(drawing, "brushInspectorValid", false);
                                Call(f.doc, "CaptureInspectorFXPrefix", layer, 1, false);
                            }
                        }
                        t.Equal(active ? 3 : 1, captured, "Inspector inputs captured");
                        t.True(ReferenceEquals(retained, Field(drawing, "brushPreviewSurface")), "Inspector preserves retained canvas texture");
                        t.Equal(dirty, (int)Field(drawing, "brushPreviewTiles").GetType().GetProperty("Count", F).GetValue(Field(drawing, "brushPreviewTiles")), "Inspector leaves canvas paint damage pending");
                    }
                    finally { RenderTexture.ReleaseTemporary(native); layer.transform.tiling = TransformTilingMode.Clip; }
                }
            }
            finally { Call(drawing, "CancelStroke"); Call(drawing, "ReleaseTransientResources"); Call(settings, "ReleasePresetTip"); f.doc.layers.Clear(); }
        }
    }

    static Color InspectorSample(Color[] pixels, int w, int h, Double2 uv, bool point, TransformTilingMode tiling = TransformTilingMode.Clip)
    {
        if (tiling == TransformTilingMode.Clip && (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)) return Color.clear;
        int Address(int x, int size)
        {
            if (tiling == TransformTilingMode.Repeat) return (x % size + size) % size;
            if (tiling == TransformTilingMode.Mirror)
            { int period = size * 2; x = (x % period + period) % period; return x < size ? x : period - 1 - x; }
            return Mathf.Clamp(x, 0, size - 1);
        }
        Color Fetch(int x, int y) => pixels[Address(y, h) * w + Address(x, w)];
        if (point) return Fetch((int)(uv.x * w), (int)(uv.y * h));
        double px = uv.x * w - .5, py = uv.y * h - .5; int ix = (int)Math.Floor(px), iy = (int)Math.Floor(py);
        return Color.LerpUnclamped(Color.LerpUnclamped(Fetch(ix, iy), Fetch(ix + 1, iy), (float)(px - ix)),
            Color.LerpUnclamped(Fetch(ix, iy + 1), Fetch(ix + 1, iy + 1), (float)(px - ix)), (float)(py - iy));
    }

    static float InspectorDifference(Color a, Color b)
    {
        float error = Mathf.Abs(a.a - b.a);
        for (int c = 0; c < 3; c++)
            error = Mathf.Max(error, Mathf.Abs(a[c] * a.a - b[c] * b.a) / Mathf.Max(1, Mathf.Abs(b[c] * b.a)));
        return error;
    }

    static void Inspector(TestContext t, Fixture f)
    {
        f.doc.layers.Clear(); f.doc.width = 2048; f.doc.height = 1024;
        var layer = new Layer(new GradientLayerBehaviour()); f.doc.layers.Add(layer);
        var fx = f.Fx(@"// @preview(Show)
float4 ApplyFX(PinnedUV uv,float4 color){return SampleInput(uv);}
#ifdef WHIMTEX_PREVIEW
float4 Show(float2 uv){return SampleInput(float2(.05,.5));}
#endif");
        layer.fx.Add(fx); Call(f.doc, "NormalizeModel");
        var control = new ShaderFXParameterControl { previewFunction = "Show" };
        string code = (string)T("ShaderFXSourceBuilder").GetMethod("BuildPreview", F).Invoke(null, new object[] { fx, control, null });
        var shader = f.Own(ShaderUtil.CreateShaderAsset(code, false));
        var material = f.Own(new Material(shader)); ShaderUtil.CompilePass(material, 0, true);
        var hidden = Activator.CreateInstance(T("ShaderFXPreviewElement"), F, null,
            new object[] { fx, null, control, false, f.doc, layer, 0 }, null);
        var previewCache = T("ShaderFXPreviewCache").GetMethod("Acquire", F).Invoke(null, new object[] { fx });
        T("ShaderFXPreviewElement").GetField("cache", F).SetValue(hidden, previewCache);
        Call(previewCache, "GetProgram", control, null);
        bool requested = false, captured = false;
        try
        {
        using (f.Capture((effect, owner, ctx, bound, input) =>
        {
            if (effect != fx) return;
            bool inspector = (bool)typeof(WhimTexDocument).GetProperty("CapturingInspectorFX", F).GetValue(f.doc);
            if (!inspector)
            {
                if (requested) return;
                requested = true;
                Call(hidden, "Capture", fx, layer, 0, ctx, bound, input);
                t.True((bool)hidden.GetType().GetField("inputDirty", F).GetValue(hidden), "Hidden inspector remembers input invalidation");
                t.True(((UnityEngine.UIElements.Image)hidden).image == null, "Canvas notification does not draw inspector");
                bool viewport = (bool)typeof(WhimTexDocument).GetProperty("RenderingViewport", F).GetValue(f.doc);
                t.True(viewport, "Canvas frame is regional");
                var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
                Call(f.doc, "CaptureInspectorFXPrefix", layer, 1, false);
                t.True((bool)typeof(WhimTexDocument).GetProperty("RenderingViewport", F).GetValue(f.doc), "Nested inspector restores viewport");
                t.True(RenderTexture.active == active && GL.sRGBWrite == srgb, "Nested inspector restores graphics state");
                return;
            }
            captured = true;
            t.Equal(512, input.width, "Inspector has independent bounded full input");
            t.Equal(256, input.height, "Inspector keeps aspect ratio");
            t.True(!(bool)typeof(WhimTexDocument).GetProperty("RenderingViewport", F).GetValue(f.doc), "Inspector uses complete coordinates");
            material.CopyPropertiesFromMaterial(bound);
            var result = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(input, result, material);
                var actual = f.Read(result)[0];
                var reference = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                try
                {
                    Graphics.Blit(input, reference, Vector2.zero, new Vector2(.05f, .5f));
                    t.Near(f.Read(reference)[0].r, actual.r, .001, "Callback samples outside canvas crop correctly");
                }
                finally { RenderTexture.ReleaseTemporary(reference); }
            }
            finally { RenderTexture.ReleaseTemporary(result); material.SetTexture("_MainTex", null); }
        }))
        { f.ClearCache(); f.RenderAndRelease(new RectInt(1300, 200, 192, 224)); }
        }
        finally { Call(previewCache, "Release"); }
        t.True(requested && captured, "Dedicated inspector request observed");
        // Processor backdrop must obey the same preview resolution.
        f.doc.layers.Clear();
        var processor = new Layer(new ShaderProcessorLayerBehaviour()); processor.fx.Add(fx);
        var backdrop = new Layer(new GradientLayerBehaviour()); var backdropFx = f.Fx("float4 ApplyFX(float4 color){return color;}");
        backdrop.fx.Add(backdropFx); f.doc.layers.Add(processor); f.doc.layers.Add(backdrop); Call(f.doc, "NormalizeModel");
        int backdropWidth = 0;
        using (f.Capture((effect, owner, ctx, bound, input) => { if (effect == backdropFx) backdropWidth = input.width; }))
            Call(f.doc, "CaptureInspectorFXPrefix", processor, 1, false);
        t.Equal(512, backdropWidth, "Processor inspector does not construct full-size backdrop");
    }

    public static string Processors() => TestContext.Run("Regional processor backdrop, groups, cache and spatial fallbacks", t =>
    {
        using var f = new Fixture();
        var texture = f.Own(new Texture2D(512, 384, TextureFormat.RGBAHalf, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        var pixels = new Color[512 * 384];
        for (int y = 0; y < 384; y++) for (int x = 0; x < 512; x++)
            pixels[y * 512 + x] = new Color(x / 127f - .4f, y / 93f, .3f, x % 17 == 0 ? 0 : x % 11 == 0 ? .001f : .6f);
        texture.SetPixels(pixels); texture.Apply();
        var source = new Layer(new FileLayerBehaviour { sourceTexture = texture })
            { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var processor = new Layer(new ShaderProcessorLayerBehaviour());
        var local = f.Fx("float4 ApplyFX(PinnedUV uv,float4 color){return SampleInput(uv)*float4(.6,.7,.8,1);}");
        var sourceFx = f.Fx("float4 ApplyFX(float4 color){return color;}");
        processor.fx.Add(local); source.fx.Add(sourceFx);
        f.doc.layers.Add(processor); f.doc.layers.Add(source); Call(f.doc, "NormalizeModel");
        f.Compare(t, "processor identity HDR");
        CheckProcessorOutputs(t, f, processor);
        var counts = new Dictionary<ShaderFX, int>(); var widths = new Dictionary<ShaderFX, int>();
        using (f.Capture((effect, owner, ctx, mat, input) =>
        { counts.TryGetValue(effect, out int n); counts[effect] = n + 1; widths[effect] = input.width; }))
        {
            f.ClearCache(); f.RenderAndRelease(new RectInt(64, 32, 192, 224));
            t.Equal(1, counts[sourceFx], "Backdrop generated once, reused by processor");
            t.Equal(192, widths[local], "Processor reads regional accumulator");
            t.Equal(192, widths[sourceFx], "Backdrop stays regional");
        }
        processor.opacity = .43f; processor.channelMapping[2] = ChannelMappingSource.R;
        f.Compare(t, "processor opacity mapping");
        processor.blendMode = BlendMode.Multiply; f.Compare(t, "processor multiply");
        processor.blendMode = BlendMode.Normal; processor.opacity = 1; processor.channelMapping = default;
        var second = new Layer(new ShaderProcessorLayerBehaviour()); second.fx.Add(local);
        f.doc.layers.Insert(0, second); Call(f.doc, "NormalizeModel");
        f.Compare(t, "processor chain"); f.doc.layers.Remove(second);
        var group = new Layer(new GroupLayerBehaviour()); group.children.Add(processor);
        f.doc.layers[0] = group; Call(f.doc, "NormalizeModel");
        f.Compare(t, "pass-through external backdrop");
        CheckProcessorOutputs(t, f, processor);
        // Retain the exact cached viewport while changing an external group backdrop.
        texture.SetPixel(250, 150, new Color(3, .05f, .8f, .9f)); texture.Apply();
        f.Compare(t, "pass-through cache invalidates external texture", clearCache: false);
        group.opacity = .35f; f.Compare(t, "partial pass-through opacity");
        group.transform.rotation = 19; group.transform.scale = new Vector2(.83f, 1.13f);
        f.Compare(t, "transformed parent, identity processor");
        processor.fx.Clear();
        var coordinates = f.Fx("float4 ApplyFX(PinnedUV uv,float4 color){return color*float4(LayerToLocal(float2(uv.x,uv.y)),1,1);}");
        processor.fx.Add(coordinates);
        f.Compare(t, "processor FX keeps parent coordinate frame", .012f);
        processor.fx.Clear(); processor.fx.Add(local); group.transform = TextureTransform.Default; group.opacity = 1;
        // A link from the external backdrop to its consuming processor is a cycle.
        var usable = typeof(WhimTexDocument).GetMethod("IsUsableShaderTexture", F, null, new[] { typeof(Layer), typeof(string) }, null);
        t.True(!(bool)usable.Invoke(f.doc, new object[] { source, processor.Id }), "External backdrop cycle rejected");
        group.compositing = GroupCompositing.Isolated;
        f.Compare(t, "isolated group has no external backdrop");
        f.doc.layers.Remove(source); group.children.Add(source); Call(f.doc, "NormalizeModel");
        f.Compare(t, "isolated internal backdrop");
        group.clippingMask = true; f.doc.layers.Add(new Layer(new ShapeLayerBehaviour())); Call(f.doc, "NormalizeModel");
        f.Compare(t, "clipped processor group", visible: true);
        group.clippingMask = false; f.doc.layers.RemoveAt(1);
        group.children.Remove(source); f.doc.layers.Add(source); group.compositing = GroupCompositing.PassThrough;
        Call(f.doc, "NormalizeModel");
        var target = new Layer(new BlurLayerBehaviour { radius = 0, inputMode = EffectInputMode.Specific, TargetLayerId = processor.Id });
        f.doc.layers.Insert(0, target); Call(f.doc, "NormalizeModel");
        f.Compare(t, "Target reads processor with external backdrop");
        processor.enabled = false; f.Compare(t, "disabled processor Target bypass");
        processor.enabled = true; f.doc.layers.Remove(target); Call(target.Behaviour, "ReleaseTransientResources");
        var textureConsumer = new Layer(new ColorFillLayerBehaviour());
        var textureFx = f.Fx(Local); textureConsumer.fx.Add(textureFx); f.doc.layers.Insert(0, textureConsumer);
        Call(f.doc, "NormalizeModel");
        var processorTexture = ((IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(textureFx))[0];
        processorTexture.textureSource = ShaderFXTextureSource.Layer; processorTexture.textureLayerId = processor.Id;
        f.Compare(t, "Processor as texture input keeps external backdrop");
        f.doc.layers.Remove(textureConsumer);
        foreach (bool moved in new[] { false, true })
        {
            processor.transform.position = moved ? new Vector2(29, -13) : Vector2.zero;
            if (!moved) processor.fx.Insert(0, f.Fx(Spatial));
            f.Compare(t, moved ? "moved processor fallback" : "spatial processor fallback");
            counts.Clear(); widths.Clear();
            using (f.Capture((effect, owner, ctx, mat, input) => widths[effect] = input.width))
            {
                f.ClearCache(); f.RenderAndRelease(new RectInt(64, 32, 192, 224));
                t.Equal(512, widths[sourceFx], "Fallback reconstructs complete external backdrop");
                t.Equal(192, widths[local], "Local suffix remains regional after fallback");
            }
            if (!moved) processor.fx.RemoveAt(0);
        }
        processor.transform = TextureTransform.Default; processor.transform.storage = TransformStorage.Projective;
        processor.transform.matrix = ProjectiveMatrix.Identity;
        f.Compare(t, "projective identity processor");
        // Spatial texture dependencies must not promote the accumulator branch.
        var dependency = new Layer(new GradientLayerBehaviour()) { enabled = false }; dependency.fx.Add(f.Fx(Spatial));
        f.doc.layers.Add(dependency); processor.fx.Clear(); var maskFx = f.Fx(Local); processor.fx.Add(maskFx);
        Call(f.doc, "NormalizeModel");
        var parameter = ((IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(maskFx))[0];
        parameter.textureSource = ShaderFXTextureSource.Layer; parameter.textureLayerId = dependency.Id;
        f.Compare(t, "local processor with spatial mask");
        widths.Clear();
        using (f.Capture((effect, owner, ctx, mat, input) => widths[effect] = input.width))
        {
            f.ClearCache(); f.RenderAndRelease(new RectInt(64, 32, 192, 224));
            t.Equal(192, widths[sourceFx], "Backdrop remains regional with spatial mask");
            t.Equal(512, widths[dependency.fx[0]], "Mask alone renders full");
            t.Equal(192, widths[maskFx], "Consumer remains regional");
        }
    });

    static void CheckProcessorOutputs(TestContext t, Fixture f, Layer processor)
    {
        var full = (RenderTexture)Call(f.doc, "RenderCanvas", f.doc.width);
        Color[] expected;
        try { expected = f.Read(full); }
        finally { RenderTexture.ReleaseTemporary(full); }
        f.RenderAndRelease(new RectInt(64, 32, 192, 224));
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var thumbnail = (RenderTexture)Call(f.doc, "RenderAgentLayerPreview", processor, f.doc.width);
        try
        {
            t.Equal(f.doc.width, thumbnail.width, "Processor thumbnail is complete after regional render");
            var actual = f.Read(thumbnail); float error = 0;
            for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                error = Mathf.Max(error, Mathf.Abs(actual[i][c] - expected[i][c]));
            t.True(error < .006f, "Processor thumbnail matches composite: " + error);
        }
        finally { RenderTexture.ReleaseTemporary(thumbnail); }
        var export = (Texture2D)Call(f.doc, "RenderPsdPixels", processor, false);
        try
        {
            t.Equal(f.doc.width, export.width, "Processor export is complete after regional render");
            var actual = export.GetPixels(); float error = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                var p = expected[i]; // Export encodes and clamps the signed HDR working result to RGBA8.
                var encoded = new Color(Mathf.Clamp01(p.r), Mathf.Clamp01(p.g), Mathf.Clamp01(p.b), Mathf.Clamp01(p.a)).gamma;
                for (int c = 0; c < 4; c++) error = Mathf.Max(error, Mathf.Abs(actual[i][c] - encoded[c]));
            }
            t.True(error < .006f, "Processor export matches encoded composite: " + error);
        }
        finally { Object.DestroyImmediate(export); }
        t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Processor outputs preserve caller render state");
    }

    public static string ProcessorBenchmark() => TestContext.Run("8K processor rendering and active masked HDR strokes", t =>
    {
        using var f = new Fixture(); f.doc.width = f.doc.height = 8192;
        var processor = new Layer(new ShaderProcessorLayerBehaviour());
        processor.fx.Add(f.Fx("float4 ApplyFX(PinnedUV uv,float4 color){return SampleInput(uv)*float4(.6,.7,.8,1);}"));
        var drawing = new DrawingLayerBehaviour();
        var drawingLayer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var fill = new Layer(new ColorFillLayerBehaviour { color = new Color(.7f, .4f, .2f, .6f) });
        var settings = Activator.CreateInstance(T("PaintToolSettings"), true);
        settings.GetType().GetField("brushSize", F).SetValue(settings, 2048f);
        settings.GetType().GetField("brushColor", F).SetValue(settings, new Color(3.5f, -.2f, .7f, .013f));
        var dynamics = settings.GetType().GetField("dynamics", F).GetValue(settings);
        dynamics.GetType().GetField("opacity", F).SetValue(dynamics, .47f);
        dynamics.GetType().GetField("flow", F).SetValue(dynamics, .35f);
        var parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
        var mask = f.Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp });
        mask.SetPixels(new[] { Color.black, Color.white, Color.gray, Color.white }); mask.Apply();
        parameters = Call(parameters, "WithSelectionMask", mask);
        string phase = (int)Call(f.doc, "ViewportFullPrefix", processor, true) < 0 ? "regional" : "full";
        var rows = new List<string> { "GPU: " + SystemInfo.graphicsDeviceName + ", processor path: " + phase };
        string folder = Path.GetFullPath("Temp/WhimTex/regional-processor"); Directory.CreateDirectory(folder);
        try
        {
            foreach (bool paint in new[] { false, true })
            foreach (int step in new[] { 1, 8 })
            {
                f.doc.layers.Clear(); f.doc.layers.Add(processor); f.doc.layers.Add(paint ? drawingLayer : fill);
                Call(f.doc, "NormalizeModel"); f.ClearCache();
                var rect = step == 1 ? new RectInt(3456, 3584, 1024, 768) : new RectInt(0, 0, 8192, 8192);
                if (paint) Call(drawing, "BeginStroke", new Vector2(.5f, .5f));
                var times = new List<double>(); float maxError = 0;
                try
                {
                    for (int i = 0; i < 12; i++)
                    {
                        f.ClearCache();
                        var watch = Stopwatch.StartNew();
                        if (paint) Call(drawing, "PaintPoint", new Vector2(.47f + i * .005f, .5f), 8192, 8192, parameters);
                        var rt = (RenderTexture)Call(f.doc, "RenderCanvasViewport", Region(rect, step), f.cache, paint, paint ? drawing : null);
                        try
                        {
                            var fence = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                            try { Graphics.Blit(rt, fence); f.Read(fence); }
                            finally { RenderTexture.ReleaseTemporary(fence); }
                            watch.Stop(); if (i > 1) times.Add(watch.Elapsed.TotalMilliseconds);
                            var small = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                            try
                            {
                                Graphics.Blit(rt, small); var pixels = f.Read(small);
                                string path = Path.Combine(folder, $"full-paint{paint}-step{step}-frame{i}.rgba");
                                if (phase == "full")
                                {
                                    using var file = new BinaryWriter(File.Create(path));
                                    foreach (var p in pixels) for (int c = 0; c < 4; c++) file.Write(p[c]);
                                }
                                else
                                {
                                    t.True(File.Exists(path), "A separately run full-path reference is required");
                                    using var file = new BinaryReader(File.OpenRead(path));
                                    foreach (var p in pixels)
                                    {
                                        var expected = new Color(file.ReadSingle(), file.ReadSingle(), file.ReadSingle(), file.ReadSingle());
                                        for (int c = 0; c < 4; c++)
                                        {
                                            float a = c == 3 ? p.a : p[c] * p.a;
                                            float b = c == 3 ? expected.a : expected[c] * expected.a;
                                            maxError = Mathf.Max(maxError, Mathf.Abs(a - b));
                                        }
                                    }
                                }
                            }
                            finally { RenderTexture.ReleaseTemporary(small); }
                        }
                        finally { RenderTexture.ReleaseTemporary(rt); }
                    }
                    times.Sort(); string memory = "";
                    if (paint)
                    {
                        var preview = (RenderTexture)drawing.GetType().GetField("brushPreviewSurface", F).GetValue(drawing);
                        memory = $", brushPreview={preview.width}x{preview.height} {preview.format}";
                    }
                    rows.Add($"paint2048={paint}, step={step}, ms min/median/max={times[0]:F2}/{times[times.Count / 2]:F2}/{times[times.Count - 1]:F2}, visibleError={maxError}{memory}");
                    t.True(maxError < .002f, "Full/regional processor output parity " + maxError);
                }
                finally { if (paint) Call(drawing, "CancelStroke"); }
            }
            File.WriteAllLines(Path.Combine(folder, phase + "-benchmark.txt"), rows);
        }
        finally
        {
            Call(settings, "ReleasePresetTip"); Call(drawing, "ReleaseTransientResources");
            if (!f.doc.layers.Contains(drawingLayer)) f.doc.layers.Add(drawingLayer);
        }
    });

    public static string Benchmark() => TestContext.Run("8K regional versus equivalent full-prefix FX", t =>
    {
        using var f = new Fixture(); f.doc.width = f.doc.height = 8192;
        var layer = new Layer(new ColorFillLayerBehaviour()); f.doc.layers.Add(layer);
        var local = f.Fx(Local);
        var full = f.Fx(Local.Replace("PinnedUV", "float2"));
        // Same math and texture, but the spatial signature takes the previous full-prefix path.
        t.True(local.IsPixelLocal && !full.IsPixelLocal, "Compared authoring contracts differ only in locality");
        foreach (var fx in new[] { local, full })
        {
            var p = ((IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(fx))[0];
            p.textureSource = ShaderFXTextureSource.Self;
        }
        var rows = new List<string>();
        rows.Add("GPU: " + SystemInfo.graphicsDeviceName);
        foreach (int step in new[] { 1, 8 })
        {
            var rect = step == 1 ? new RectInt(3456, 3584, 1024, 768) : new RectInt(0, 0, 8192, 8192);
            Color[] reference = null;
            foreach (var fx in new[] { full, local })
            {
                layer.fx.Clear(); layer.fx.Add(fx); Call(f.doc, "NormalizeModel");
                var times = new List<double>(); int stageWidth = 0, stageHeight = 0;
                using (f.Capture((effect, owner, ctx, bound, input) =>
                { if (effect == fx) { stageWidth = input.width; stageHeight = input.height; } }))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        f.ClearCache(); var watch = Stopwatch.StartNew();
                        var rt = f.Render(rect, step);
                        try
                        {
                            var sample = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                            try
                            {
                                Graphics.Blit(rt, sample);
                                var pixel = f.Read(sample); // Completion fence, only 16 bytes read back.
                                if (reference == null) reference = pixel;
                                for (int c = 0; c < 4; c++) t.Near(reference[0][c], pixel[0][c], .001, "Equivalent 8K output");
                            }
                            finally { RenderTexture.ReleaseTemporary(sample); }
                        }
                        finally { RenderTexture.ReleaseTemporary(rt); }
                        watch.Stop(); if (i > 0) times.Add(watch.Elapsed.TotalMilliseconds);
                    }
                }
                times.Sort();
                rows.Add($"step={step}, local={fx.IsPixelLocal}, ms min/median/max={times[0]:F2}/{times[1]:F2}/{times[2]:F2}, FX={stageWidth}x{stageHeight}, one ARGBFloat target MiB={stageWidth*(long)stageHeight*16/1048576.0:F2}");
                t.Equal(fx == local || step == 8 ? rect.width / step : 8192, stageWidth, "Expected stage size");
            }
        }
        StrokeBenchmark(t, f, local, full, rows);
        string directory = Path.GetFullPath("Temp/WhimTex/regional-fx/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "benchmark.txt"), rows);
        t.True(rows.Count == 9, "All bounded 8K comparisons completed");
    });

    static void StrokeBenchmark(TestContext t, Fixture f, ShaderFX local, ShaderFX full, List<string> rows)
    {
        f.doc.layers.Clear();
        var drawing = new DrawingLayerBehaviour();
        var layer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        f.doc.layers.Add(layer); Call(f.doc, "NormalizeModel");
        var settings = Activator.CreateInstance(T("PaintToolSettings"), true);
        settings.GetType().GetField("brushSize", F).SetValue(settings, 1024f);
        settings.GetType().GetField("brushColor", F).SetValue(settings, new Color(3.5f, -.2f, .7f, .013f));
        var dynamics = settings.GetType().GetField("dynamics", F).GetValue(settings);
        dynamics.GetType().GetField("opacity", F).SetValue(dynamics, .47f);
        dynamics.GetType().GetField("flow", F).SetValue(dynamics, .35f);
        var parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
        var mask = f.Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp });
        mask.SetPixels(new[] { Color.black, Color.white, Color.gray, Color.white }); mask.Apply();
        parameters = Call(parameters, "WithSelectionMask", mask);
        var initial = RenderTexture.active;
        try
        {
            foreach (int step in new[] { 1, 8 })
            {
                var rect = step == 1 ? new RectInt(3456, 3584, 1024, 768) : new RectInt(0, 0, 8192, 8192);
                var expected = new List<Color[]>();
                foreach (var fx in new[] { full, local })
                {
                    layer.fx.Clear(); layer.fx.Add(fx); f.ClearCache();
                    Call(drawing, "BeginStroke", new Vector2(.5f, .5f));
                    var times = new List<double>();
                    try
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            var watch = Stopwatch.StartNew();
                            Call(drawing, "PaintPoint", new Vector2(.47f + i * .01f, .5f), 8192, 8192, parameters);
                            var rt = (RenderTexture)Call(f.doc, "RenderCanvasViewport", Region(rect, step), f.cache, true, drawing);
                            try
                            {
                                var pixelTarget = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                                try { Graphics.Blit(rt, pixelTarget); f.Read(pixelTarget); } // Only the completion fence is timed.
                                finally { RenderTexture.ReleaseTemporary(pixelTarget); }
                                watch.Stop();
                                // A small independent image read exercises more than one mask/paint sample.
                                var small = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                                try
                                {
                                    Graphics.Blit(rt, small); var pixels = f.Read(small);
                                    if (fx == full) expected.Add(pixels);
                                    else
                                    {
                                        float error = 0;
                                        for (int k = 0; k < pixels.Length; k++) for (int c = 0; c < 4; c++)
                                        {
                                            float a = c == 3 ? pixels[k].a : pixels[k][c] * pixels[k].a;
                                            float b = c == 3 ? expected[i][k].a : expected[i][k][c] * expected[i][k].a;
                                            error = Mathf.Max(error, Mathf.Abs(a - b));
                                        }
                                        t.True(error < .002f, "8K active masked low-opacity HDR stroke parity " + error);
                                    }
                                }
                                finally { RenderTexture.ReleaseTemporary(small); }
                            }
                            finally { RenderTexture.ReleaseTemporary(rt); }
                            if (i > 0) times.Add(watch.Elapsed.TotalMilliseconds);
                        }
                        times.Sort();
                        var preview = (RenderTexture)drawing.GetType().GetField("brushPreviewSurface", F).GetValue(drawing);
                        rows.Add($"brush1024 masked HDR step={step}, local={fx.IsPixelLocal}, ms min/median/max={times[0]:F2}/{times[1]:F2}/{times[2]:F2}, brush preview={preview.width}x{preview.height}, format={preview.format}");
                        if (fx == local) t.Equal(rect.width / step, preview.width, "Brush preview stays regional with texture-reading local FX");
                    }
                    finally { Call(drawing, "CancelStroke"); }
                }
            }
        }
        finally
        {
            Call(settings, "ReleasePresetTip"); Call(drawing, "ReleaseTransientResources");
            RenderTexture.active = initial;
        }
    }

    sealed class Fixture : IDisposable
    {
        internal readonly WhimTexDocument doc;
        readonly List<Object> owned = new List<Object>();
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        internal IDisposable cache;
        internal Fixture()
        {
            doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            try { doc.width = 512; doc.height = 384; ClearCache(); }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
                throw;
            }
        }
        internal A Own<A>(A obj) where A : Object { obj.hideFlags = HideFlags.HideAndDontSave; owned.Add(obj); return obj; }
        internal ShaderFX Fx(string source)
        {
            var fx = Own((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { doc, source, new List<ShaderFXParameter>() }));
            Call(fx, "ApplyAgentDraft"); return fx;
        }
        internal void ClearCache() { cache?.Dispose(); cache = (IDisposable)Activator.CreateInstance(T("EffectRenderCache"), true); }
        internal RenderTexture Render(RectInt rect, int step = 1) => (RenderTexture)Call(doc, "RenderCanvasViewport", Region(rect, step), cache, false, null);
        internal void RenderAndRelease(RectInt rect) { var rt = Render(rect); RenderTexture.ReleaseTemporary(rt); }
        internal Color[] Read(RenderTexture rt)
        {
            var active = RenderTexture.active;
            var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); cpu.Apply(); return cpu.GetPixels(); }
            finally { RenderTexture.active = active; Object.DestroyImmediate(cpu); }
        }
        internal void Compare(TestContext t, string name, float tolerance = .006f, bool clearCache = true, bool visible = false)
        {
            if (clearCache) ClearCache(); var full = (RenderTexture)Call(doc, "RenderCanvas", doc.width);
            Color[] expected;
            try { expected = Read(full); } finally { RenderTexture.ReleaseTemporary(full); }
            var regions = new[] { new RectInt(64, 32, 192, 224), new RectInt(224, 128, 192, 224) };
            if (!clearCache) Array.Reverse(regions); // Reuse the last cached view before panning.
            foreach (var rect in regions)
            {
                var rt = Render(rect);
                try
                {
                    var actual = Read(rt); float error = 0; int worst = 0;
                    for (int y = 0; y < rect.height; y++) for (int x = 0; x < rect.width; x++)
                    {
                        var a = actual[y * rect.width + x]; var b = expected[(rect.y + y) * doc.width + rect.x + x];
                        // Geometry at effectively zero AA coverage has no stable straight RGB.
                        if (visible) { a = new Color(a.r * a.a, a.g * a.a, a.b * a.a, a.a); b = new Color(b.r * b.a, b.g * b.a, b.b * b.a, b.a); }
                        for (int c = 0; c < 4; c++) if (Mathf.Abs(a[c] - b[c]) > error) { error = Mathf.Abs(a[c] - b[c]); worst = y * rect.width + x; }
                    }
                    t.True(error < tolerance, name + " full/crop error " + error + " at " + worst +
                        " actual=" + actual[worst] + " expected=" + expected[(rect.y + worst / rect.width) * doc.width + rect.x + worst % rect.width]);
                }
                finally { RenderTexture.ReleaseTemporary(rt); }
            }
        }
        internal IDisposable Capture(Action<ShaderFX, Layer, object, Material, RenderTexture> callback)
        {
            var evt = T("ShaderFXPreviewRendering").GetEvent("Frame", F);
            var signature = evt.EventHandlerType.GetMethod("Invoke").GetParameters();
            var args = new ParameterExpression[signature.Length];
            for (int i = 0; i < args.Length; i++) args[i] = Expression.Parameter(signature[i].ParameterType);
            var body = Expression.Invoke(Expression.Constant(callback), args[0], args[1], Expression.Convert(args[3], typeof(object)), args[4], args[5]);
            var handler = Expression.Lambda(evt.EventHandlerType, body, args).Compile();
            evt.GetAddMethod(true).Invoke(null, new object[] { handler });
            return new Subscription(evt, handler);
        }
        public void Dispose()
        {
            var failures = new List<Exception>();
            void ReleaseOwned(Action release)
            {
                try { release(); }
                catch (Exception error) { failures.Add(error); }
            }
            ReleaseOwned(() => cache?.Dispose());
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var value = owned[i];
                ReleaseOwned(() => { if (value != null) Object.DestroyImmediate(value); });
            }
            ReleaseOwned(() => RenderTexture.active = previous);
            ReleaseOwned(() => GL.sRGBWrite = srgb);
            if (failures.Count != 0) throw new AggregateException("Owned regional FX cleanup unfinished; recovery required", failures);
        }
    }
    sealed class Subscription : IDisposable
    {
        readonly EventInfo evt; readonly Delegate handler;
        internal Subscription(EventInfo evt, Delegate handler) { this.evt = evt; this.handler = handler; }
        public void Dispose() => evt.GetRemoveMethod(true).Invoke(null, new object[] { handler });
    }
}
