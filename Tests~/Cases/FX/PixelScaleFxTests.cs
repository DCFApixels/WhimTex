using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityC;

public static class PixelScaleFxTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Metadata = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true);
    static TestContext C => FixtureContext.Context;
    static FixtureScope Scope => FixtureContext.Scope;
    static List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)Metadata.GetMethod("Parse", Flags)
        .Invoke(null, new object[] { source, false, null });
    static void Normalize(WhimTexDocument doc) => typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(doc, null);

    public static string Run() => FixtureContext.Run("Logical pixel sizes in native effects and ShaderFX", () =>
    {
        MetadataContract();
        ShaderContract();
        ArtisticLengths();
    });

    static void MetadataContract()
    {
        var declarations = Parse("// @param px hidden label(Width) float _A = 3 [0 .. ~32]\n" +
            "// @param label(Offset) px float2 _B = (-2, 4)\n// @param px float3 _C = (1, 2, 3)\n" +
            "// @param px float4 _D = (1, 2, 3, 4)\n// @param float _Unmarked = 2\n");
        C.Equal(5, declarations.Count, "All supported numeric lengths parse");
        for (int i = 0; i < 4; i++) C.True(declarations[i].pixelUnits, "Pixel length declaration " + i);
        C.True(!declarations[4].pixelUnits, "Unmarked numbers remain dimensionless");
        C.Equal(2, Parse("// @param px float _A = 2\n// @param px float _A [0 .. 8]\n// @param px float2 _B\n").Count,
            "Repeated controls agree on units");
        foreach (string body in new[] { "px bool _A", "px enum _A {A:0,B:1}", "px normal _A", "px point _A",
            "px color _A", "px texture2D _A", "px transform2D _A", "px curve _A", "px gradient _A",
            "px seed float _A [0 .. 100]", "px px float _A", "px float _A\n// @param float _A" })
        {
            bool rejected = false;
            try { Parse("// @param " + body); }
            catch (TargetInvocationException error) when (error.InnerException is FormatException) { rejected = true; }
            C.True(rejected, "Ambiguous or non-length px metadata rejected: " + body);
        }
        var contracts = new Dictionary<string, string[]>
        {
            ["BevelEmboss"] = new[] { "_Depth", "_Smoothing" }, ["EdgeOutline"] = new[] { "_Thickness", "_Softness" },
            ["Pixelate"] = new[] { "_PixelSize", "_Offset" }, ["ChromaticAberration"] = new[] { "_Amount" },
            ["CRT"] = new[] { "_ScanlineSpacing", "_ChromaticAberration" },
            ["VHS"] = new[] { "_ChromaBleed", "_LineJitter", "_TrackingHeight", "_TrackingOffset" },
            ["DisplacementMap"] = new[] { "_StrengthX", "_StrengthY", "_Strength", "_Depth" },
            ["Halftone"] = new[] { "_DotSize", "_PlateSpread", "_CyanOffset", "_RedOffset" },
            ["DigitalGlitch"] = new[] { "_TearLineHeight", "_TearJitter", "_TearShift", "_BlockWidth", "_BlockHeight", "_BlockShiftX", "_BlockShiftY", "_RGBSplit", "_AlphaJitter" }
        };
        foreach (var pair in contracts)
        {
            var values = Parse(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + pair.Key + ".hlsl"));
            foreach (string name in pair.Value) C.True(values.Find(p => p.name == name)?.pixelUnits == true, pair.Key + " pixel length " + name);
            foreach (var value in values) if (value.controls.Exists(control => control.seed)) C.True(!value.pixelUnits, "Seed remains an index");
        }
    }

    static WhimTexDocument Document(int width = 48, int height = 36)
    {
        var doc = Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = width; doc.height = height;
        doc.outputPrecision = WhimTexOutputPrecision.Float32; doc.outputSrgb = false;
        return doc;
    }

    static ShaderFX Effect(WhimTexDocument doc, string source)
    {
        var fx = Scope.Own((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", Flags, null,
            new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
            .Invoke(null, new object[] { doc, source, new List<ShaderFXParameter>() }));
        typeof(ShaderFX).GetMethod("ApplyAgentDraft", Flags).Invoke(fx, null);
        C.True(!(bool)typeof(ShaderFX).GetProperty("LastApplyFailed", Flags).GetValue(fx), "FX compiles with pixel contract");
        return fx;
    }

    static Color[] Preview(WhimTexDocument doc, Layer layer, int size)
    {
        Normalize(doc);
        var rendered = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderAgentLayerPreview", Flags).Invoke(doc, new object[] { layer, size });
        var previous = RenderTexture.active;
        var read = Scope.Own(new Texture2D(rendered.width, rendered.height, TextureFormat.RGBAFloat, false, true));
        try
        {
            RenderTexture.active = rendered;
            read.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0, false);
            return read.GetPixels();
        }
        finally { RenderTexture.active = previous; Scope.Release(rendered); Scope.Destroy(read); }
    }

    static void ExpectedShader(Color value, float multiplier, string label, bool encodedPsd = false)
    {
        var expected = new Color(.2f * multiplier, .31f * multiplier, .25f, 1);
        if (encodedPsd) expected = expected.gamma;
        double tolerance = encodedPsd ? 1d / 255 : .001;
        C.Near(expected.r, value.r, tolerance, label + " scalar upload");
        C.Near(expected.g, value.g, tolerance, label + " vector upload and _PixelScale");
        C.Near(expected.b, value.b, tolerance, label + " unmarked number");
        C.Near(1, value.a, .001, label + " physical coverage");
    }

    static void ShaderContract()
    {
        var doc = Document();
        const string source = "// @param px float _Radius = 2 [0 .. ~8]\n// @param px float2 _Offset = (3, 4)\n" +
            "// @param float _Raw = 0.25\nfloat4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return float4(_Radius * .1, _Offset.x * .1 + _PixelScale * .01, _Raw, 1); }";
        var fx = Effect(doc, source);
        Layer leaf = new ColorFillLayerBehaviour(); leaf.colorRange = LayerColorRange.HDR; leaf.fx.Add(fx); doc.layers.Add(leaf);
        foreach (float p in new[] { 1f, 2f, .5f, 2f, 1f })
        {
            doc.pixelScale = p;
            ExpectedShader(Preview(doc, leaf, 48)[0], p, "Full/thumbnail refresh P=" + p);
            ExpectedShader(Preview(doc, leaf, 24)[0], p, "Reduced preview P=" + p);
            var export = Scope.Own((Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdPixels", Flags)
                .Invoke(doc, new object[] { leaf, false }));
            ExpectedShader(export.GetPixels()[0], p, "8-bit sRGB PSD export P=" + p, encodedPsd: true); Scope.Destroy(export);
        }
        var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Flags).GetValue(fx);
        C.Near(2, parameters[0].floatValue, 0, "Rendering leaves scalar data logical");
        C.Near(3, parameters[1].vectorValue.x, 0, "Rendering leaves vector data logical");
        string preset = (string)typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter", true)
            .GetMethod("BuildSource", Flags).Invoke(null, new object[] { fx, "Test/Logical Pixels" });
        C.True(Parse(preset)[0].pixelUnits && Parse(preset)[1].pixelUnits, "Preset export retains px declarations");
        C.Near(2, Parse(preset)[0].floatValue, 0, "Preset stores original logical default");
        doc.pixelScale = 2;
        Layer group = new GroupLayerBehaviour(); group.colorRange = LayerColorRange.HDR;
        leaf.fx.Clear(); group.children.Add(leaf); group.fx.Add(fx); doc.layers.Clear(); doc.layers.Add(group);
        ExpectedShader(Preview(doc, group, 48)[0], 2, "Group FX");
        group.fx.Clear(); leaf.fx.Add(fx);
        var target = new BlurLayerBehaviour { strength = 0, inputMode = EffectInputMode.Specific, TargetLayerId = leaf.Id };
        target.colorRange = LayerColorRange.HDR; doc.layers.Insert(0, target);
        ExpectedShader(Preview(doc, target.Owner, 48)[0], 2, "Target input FX");
        doc.layers.Clear(); group.children.Clear(); doc.layers.Add(leaf);
        Layer bottom = new ColorFillLayerBehaviour(); bottom.colorRange = LayerColorRange.HDR;
        leaf.clippingMask = true; doc.layers.Add(bottom);
        var composite = Scope.Own(doc.ComposeCanvas());
        ExpectedShader(composite.GetPixels()[0], 2, "Clipping composite FX"); Scope.Destroy(composite);
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            using var loaded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json);
            C.True(loaded.Warnings.Count == 0, "JSON logical FX roundtrip " + mode);
            ExpectedShader(Preview(loaded.Document, loaded.Document.layers[0], 48)[0], 2, "JSON upload " + mode);
        }
    }

    static void ArtisticLengths()
    {
        var doc = Document(49, 37);
        var texture = Scope.Own(new Texture2D(49, 37, TextureFormat.RGBAFloat, false, true));
        texture.hideFlags = HideFlags.HideAndDontSave;
        var input = new Color[49 * 37];
        for (int y = 0; y < 37; y++) for (int x = 0; x < 49; x++)
            input[y * 49 + x] = new Color(x / 48f, y / 36f, (x + y) % 5 / 4f,
                x >= 13 && x <= 35 && y >= 10 && y <= 26 ? 1f : 0f);
        texture.SetPixels(input); texture.Apply(false, false);
        Layer leaf = new FileLayerBehaviour { sourceTexture = texture }; leaf.colorRange = LayerColorRange.HDR;
        var ownedLayers = new List<Layer> { leaf };
        void Compare(Layer effect, Action twice, Action restore, string name)
        {
            if (!ownedLayers.Contains(effect)) ownedLayers.Add(effect);
            doc.layers.Clear(); doc.layers.Add(effect); doc.layers.Add(leaf); effect.colorRange = LayerColorRange.HDR;
            foreach (int size in new[] { 49, 25 })
            {
                doc.pixelScale = 2; var scaled = Preview(doc, effect, size);
                doc.pixelScale = 1; twice(); var reference = Preview(doc, effect, size); restore();
                C.Equal(reference.Length, scaled.Length, name + " render dimensions");
                double maximum = 0;
                for (int i = 0; i < scaled.Length; i++) for (int channel = 0; channel < 4; channel++)
                    maximum = Math.Max(maximum, Math.Abs(scaled[i][channel] - reference[i][channel]));
                C.Near(0, maximum, .002, name + " matches explicit physical dimensions, preview " + size);
            }
        }
        try
        {
        var gaussian = new BlurLayerBehaviour { radius = 3 };
        Compare(gaussian, () => gaussian.radius = 6, () => gaussian.radius = 3, "Gaussian radius");
        var linear = new BlurLayerBehaviour { mode = BlurType.Linear, distance = 6, angle = 27 };
        Compare(linear, () => linear.distance = 12, () => linear.distance = 6, "Linear distance");
        var circular = new BlurLayerBehaviour { mode = BlurType.Circular, arc = 21 };
        Compare(circular, () => { }, () => { }, "Circular angle remains dimensionless");
        var sharpen = new SharpenLayerBehaviour { radius = 2, strength = .7f };
        Compare(sharpen, () => sharpen.radius = 4, () => sharpen.radius = 2, "Sharpen radius");
        var outline = new OutlineLayerBehaviour { outlineWidth = 3, outlineSoftness = 2, outlineOffset = 1 };
        Compare(outline, () => { outline.outlineWidth = 6; outline.outlineSoftness = 4; outline.outlineOffset = 2; },
            () => { outline.outlineWidth = 3; outline.outlineSoftness = 2; outline.outlineOffset = 1; }, "Outline lengths");
        var sdf = new SDFLayerBehaviour { insideDistance = 3, outsideDistance = 4, contourOffset = 1,
            maxDistanceNormalization = 12, sourceOffset = new Vector2(1, 2) };
        Compare(sdf, () => { sdf.insideDistance = 6; sdf.outsideDistance = 8; sdf.contourOffset = 2; sdf.maxDistanceNormalization = 24; sdf.sourceOffset = new Vector2(2, 4); },
            () => { sdf.insideDistance = 3; sdf.outsideDistance = 4; sdf.contourOffset = 1; sdf.maxDistanceNormalization = 12; sdf.sourceOffset = new Vector2(1, 2); }, "SDF distances and offset");
        foreach (var metric in new[] { DistanceMetric.Sharp, DistanceMetric.MTSDF })
        {
            var merged = new SDFLayerBehaviour { metric = metric, contourMergeDistance = .75f, maxDistanceNormalization = 12,
                encoding = metric == DistanceMetric.MTSDF ? SDFLayerBehaviour.OutputEncoding.MultiChannelData : SDFLayerBehaviour.OutputEncoding.LinearData };
            Compare(merged, () => { merged.contourMergeDistance = 1.5f; merged.maxDistanceNormalization = 24; },
                () => { merged.contourMergeDistance = .75f; merged.maxDistanceNormalization = 12; }, "Nonzero Contour Merge " + metric);
            doc.layers.Clear(); doc.layers.Add(merged); doc.layers.Add(leaf);
            merged.contourMergeDistance = 2; doc.pixelScale = 2;
            foreach (int size in new[] { 49, 25 })
            {
                var pixels = Preview(doc, merged.Owner, size);
                bool finite = true;
                foreach (var value in pixels) for (int channel = 0; channel < 4; channel++)
                    if (!float.IsFinite(value[channel])) finite = false;
                C.True(finite, "Maximum logical Contour Merge can exceed 2 physical pixels safely: " + metric + "/" + size);
            }
            C.Near(2, merged.contourMergeDistance, 0, "Rendering preserves the logical 0..2 merge contract");
        }
        var normal = new NormalMapLayerBehaviour { mode = NormalMapLayerBehaviour.GenerationMode.Texture,
            smoothing = 1, mediumRadius = 2, largeRadius = 4, strength = 4 };
        Compare(normal, () => { normal.smoothing = 2; normal.mediumRadius = 4; normal.largeRadius = 8; normal.strength = 8; },
            () => { normal.smoothing = 1; normal.mediumRadius = 2; normal.largeRadius = 4; normal.strength = 4; }, "Normal surface lengths");
        }
        finally
        {
            // Keep every detached test layer reachable by the owned document's resource cleanup.
            doc.layers.Clear();
            doc.layers.AddRange(ownedLayers);
        }
    }
}
