using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;

public static class OptionalLayerGradientTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static void Check(bool ok, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, message); checks++; }
    static bool Enabled(LayerBehaviour source) => source is NoiseLayerBehaviour noise
        ? noise.encoding == NoiseLayerBehaviour.OutputEncoding.Gradient : ((SDFLayerBehaviour)source).encoding == SDFLayerBehaviour.OutputEncoding.Gradient;
    static void Enable(LayerBehaviour source, bool value)
    {
        if (source is NoiseLayerBehaviour noise) noise.encoding = value ? NoiseLayerBehaviour.OutputEncoding.Gradient : NoiseLayerBehaviour.OutputEncoding.ColorValues;
        else ((SDFLayerBehaviour)source).encoding = value ? SDFLayerBehaviour.OutputEncoding.Gradient : SDFLayerBehaviour.OutputEncoding.LinearData;
    }
    static IDisposable Read(string layers)
    {
        try { return (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F)
            .Invoke(null, new object[] { "{\"format\":\"whimtex.document\",\"version\":2,\"layers\":" + layers + "}", 48, 48 }); }
        catch (TargetInvocationException error) { throw error.GetBaseException(); }
    }
    static WhimTexDocument Document(IDisposable data) => (WhimTexDocument)data.GetType().GetField("Document", F).GetValue(data);
    static Color[] Render(WhimTexDocument doc, Layer layer)
    {
        var old = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var rt = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreview", F).Invoke(doc, new object[] { layer, 48 });
        Check(RenderTexture.active == old && GL.sRGBWrite == srgb, "Preserves render state");
        var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true));
        try
        {
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            return texture.GetPixels();
        }
        finally { RenderTexture.active = old; WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(texture); }
    }
    static void Near(Color actual, Color expected, string message, float tolerance = .015f)
    {
        for (int c = 0; c < 4; c++) Check(Mathf.Abs(actual[c] - expected[c]) < tolerance,
            message + " channel " + c + ": " + actual + " / " + expected);
    }
    static Color Decode(Color color)
    {
        for (int c = 0; c < 3; c++)
        {
            float value = Mathf.Abs(color[c]);
            color[c] = Mathf.Sign(color[c]) * (value <= .04045f ? value / 12.92f : Mathf.Pow((value + .055f) / 1.055f, 2.4f));
        }
        return color;
    }
    static void Cache(WhimTexDocument doc)
    {
        var layer = doc.layers[0];
        var cacheType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache");
        using var cache = (IDisposable)Activator.CreateInstance(cacheType, true);
        void Compare()
        {
            var old = RenderTexture.active;
            var output = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderCanvasWithCache", F)
                .Invoke(doc, new object[] { 48, cache, false, null });
            var read = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(48, 48, TextureFormat.RGBAFloat, false, true));
            var fresh = doc.ComposeCanvas();
            try
            {
                RenderTexture.active = output;
                read.ReadPixels(new Rect(0, 0, 48, 48), 0, 0, false);
                var a = read.GetPixels(); var b = fresh.GetPixels();
                for (int i = 0; i < a.Length; i++) Near(a[i], b[i], "Cached/composite parity");
            }
            finally
            {
                RenderTexture.active = old; WhimTex.Tests.UnityC.FixtureContext.Scope.Release(output);
                WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(read); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(fresh);
            }
        }
        Compare(); Compare();
        if (layer.Behaviour is TargetedLayerBehaviour)
            Check((int)cacheType.GetProperty("Hits", F).GetValue(cache) > 0, "Unchanged effect render uses cache");
        Enable(layer.Behaviour, false);
        Compare();
        Enable(layer.Behaviour, true);
        layer.Behaviour.GetType().GetField("gradient").SetValue(layer.Behaviour, GradientUtility.Create(new[] { new GradientColorKey(Color.green, 0) }));
        Compare();
        var group = new GroupLayerBehaviour();
        doc.layers.RemoveAt(0); doc.layers.Insert(0, group);
        group.layers.Add(layer);
        group.layers.Add(new ColorFillLayerBehaviour { color = new Color(1, 1, 1, .4f) });
        layer.clippingMask = true;
        typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
        Compare();
    }
    static void UI(WhimTexDocument doc, LayerBehaviour source, Type editor)
    {
        var window = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<OptionalGradientTestWindow>());
        window.titleContent = new GUIContent("Gradient controls test");
        window.ShowUtility();
        try
        {
        var root = window.rootVisualElement;
        Type bindingType = typeof(NoiseLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings");
        object bindings = Activator.CreateInstance(bindingType, true);
        void Refresh() => bindingType.GetMethod("Refresh", F).Invoke(bindings, new object[] { true });
        Action<string, Action> change = (_, action) => { action(); Refresh(); };
        var args = source is NoiseLayerBehaviour
            ? new object[] { root, source, doc, change, bindings }
            : new object[] { root, source, doc, change, bindings, (Action<VisualElement, TargetedLayerBehaviour>)((_, __) => { }) };
        editor.GetMethod("BuildFields", F).Invoke(null, args);
        Refresh();
        var field = root.Q<WhimTexGradientValueField>();
        if (source is NoiseLayerBehaviour noise)
        {
            Check(!root.Query<Toggle>().ToList().Any(t => t.label == "Apply Gradient"), "Noise has no gradient checkbox");
            var output = root.Q<PopupField<NoiseLayerBehaviour.OutputEncoding>>();
            var inverted = root.Query<Toggle>().ToList().Single(t => t.label == "Inverted");
            bool Hidden(VisualElement element) => element.ClassListContains("whimtex-hidden");
            Check(output.value == NoiseLayerBehaviour.OutputEncoding.LinearData && Hidden(field) && !Hidden(inverted), "Default Linear Data shows Inverted");
            output.value = NoiseLayerBehaviour.OutputEncoding.LinearData;
            Check(noise.encoding == NoiseLayerBehaviour.OutputEncoding.LinearData && Hidden(field) && !Hidden(inverted), "Linear Data shows Inverted");
            output.value = NoiseLayerBehaviour.OutputEncoding.Gradient;
            Check(!Hidden(field) && !Hidden(inverted), "Gradient shows palette and Inverted");
            noise.noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise;
            noise.whiteNoiseColor = NoiseLayerBehaviour.WhiteNoiseColor.Color;
            Refresh();
            Check(output.value == NoiseLayerBehaviour.OutputEncoding.ColorValues && output.choices.Count == 2 && Hidden(field) && !Hidden(inverted), "Color noise exposes supported outputs");
            Check(noise.encoding == NoiseLayerBehaviour.OutputEncoding.Gradient, "Color noise retains requested output");
            noise.whiteNoiseColor = NoiseLayerBehaviour.WhiteNoiseColor.Monochrome;
            Refresh();
            Check(output.value == NoiseLayerBehaviour.OutputEncoding.Gradient && output.choices.Count == 3 && !Hidden(field), "Monochrome restores controls");
        }
        else
        {
            Check(!root.Query<Toggle>().ToList().Any(t => t.label == "Apply Gradient"), "SDF has no gradient checkbox");
            var output = root.Query<EnumField>().ToList().Single(f => f.label == "Output");
            var inverted = root.Query<Toggle>().ToList().Single(t => t.label == "Inverted");
            bool Hidden(VisualElement element) => element.ClassListContains("whimtex-hidden");
            Check(Enabled(source) && !Hidden(field) && !Hidden(inverted), "SDF gradient keeps Inverted available");
            output.value = SDFLayerBehaviour.OutputEncoding.LinearData;
            Check(!Enabled(source) && Hidden(field) && !Hidden(inverted), "SDF Linear Data shows Inverted");
            output.value = SDFLayerBehaviour.OutputEncoding.Gradient;
            Check(Enabled(source) && !Hidden(field) && !Hidden(inverted), "SDF restores gradient");
        }
        }
        finally { global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window); }
    }
    static string ExecuteMain()
    {
        checks = 0;
        foreach (string name in new[] { "Noise", "SdfGradient" })
        {
            var shader = Shader.Find("Hidden/WhimTex/" + name);
            Check(shader != null && shader.isSupported, "Supported " + name);
            foreach (var message in UnityEditor.ShaderUtil.GetShaderMessages(shader))
                Check(message.severity.ToString() != "Error", message.message);
        }
        using var noiseData = Read("[{\"id\":\"noise\",\"colorRange\":\"HDR\",\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\"}}]");
        using var sdfData = Read("[{\"id\":\"sdf\",\"colorRange\":\"HDR\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\",\"inputMode\":\"Specific\",\"targetLayerId\":\"shape\",\"maxDistanceNormalization\":16}},{\"id\":\"shape\",\"enabled\":false,\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\",\"kind\":\"Ellipse\"},\"transform\":{\"scale\":{\"x\":0.5,\"y\":0.5}}}]");
        var noiseDoc = Document(noiseData); var sdfDoc = Document(sdfData);
        var noise = (NoiseLayerBehaviour)noiseDoc.layers[0].Behaviour;
        var sdf = (SDFLayerBehaviour)sdfDoc.layers[0].Behaviour;
        Check(noise.encoding == NoiseLayerBehaviour.OutputEncoding.LinearData && Enabled(sdf), "Noise defaults Linear Data; SDF defaults Gradient");
        Check(Enum.GetValues(typeof(SDFLayerBehaviour.OutputEncoding)).Length == 2, "SDF has exactly two outputs");
        Check(noise.gradient.Mode == WhimTexGradientMode.Perceptual && sdf.gradient.Mode == WhimTexGradientMode.Perceptual, "Both default Perceptual");
        UI(noiseDoc, noise, typeof(NoiseLayerEditorWindow));
        UI(sdfDoc, sdf, typeof(SDFLayerEditorWindow));
        var palette = GradientUtility.Create(new[] { new GradientColorKey(new Color(.1f,.3f,1.4f), 0),
            new GradientColorKey(new Color(1.2f,.2f,.05f), 1) }, new[] { new GradientAlphaKey(.3f, 0), new GradientAlphaKey(.9f, 1) });
        palette.Mode = WhimTexGradientMode.Linear;
        noise.gradient = palette.Clone(); sdf.gradient = palette.Clone();
        noise.encoding = NoiseLayerBehaviour.OutputEncoding.LinearData;
        foreach (NoiseLayerBehaviour.NoiseType type in Enum.GetValues(typeof(NoiseLayerBehaviour.NoiseType)))
        {
            noise.noiseType = type;
            noise.encoding = NoiseLayerBehaviour.OutputEncoding.LinearData;
            var raw = Render(noiseDoc, noise.Owner);
            noise.encoding = NoiseLayerBehaviour.OutputEncoding.Gradient;
            var mapped = Render(noiseDoc, noise.Owner);
            for (int i = 0; i < raw.Length; i++) Near(mapped[i], Decode(palette.Evaluate(raw[i].r)), "Noise palette " + type, .022f);
            noise.inverted = true;
            var encoded = Render(noiseDoc, noise.Owner);
            for (int i = 0; i < mapped.Length; i++) Near(encoded[i], Decode(palette.Evaluate(1 - raw[i].r)), "Noise inversion before gradient", .022f);
            noise.inverted = false;
        }
        foreach (var type in new[] { NoiseLayerBehaviour.NoiseType.WhiteNoise, NoiseLayerBehaviour.NoiseType.BlueNoise })
        {
            noise.noiseType = type; noise.whiteNoiseColor = NoiseLayerBehaviour.WhiteNoiseColor.Color;
            noise.encoding = NoiseLayerBehaviour.OutputEncoding.ColorValues; var raw = Render(noiseDoc, noise.Owner);
            noise.encoding = NoiseLayerBehaviour.OutputEncoding.Gradient; var bypass = Render(noiseDoc, noise.Owner);
            for (int i = 0; i < raw.Length; i++) Near(bypass[i], raw[i], "RGB noise bypass");
        }
        Enable(sdf, false);
        var distance = Render(sdfDoc, sdf.Owner);
        sdf.inverted = true;
        var inverse = Render(sdfDoc, sdf.Owner);
        for (int i = 0; i < distance.Length; i++) Near(inverse[i], new Color(1-distance[i].r, 1-distance[i].r, 1-distance[i].r, 1), "SDF Linear Data inversion");
        Enable(sdf, true);
        var sdfMapped = Render(sdfDoc, sdf.Owner);
        sdf.inverted = false;
        var nonInverted = Render(sdfDoc, sdf.Owner);
        for (int i = 0; i < distance.Length; i++)
        {
            Near(distance[i], new Color(distance[i].r, distance[i].r, distance[i].r, 1), "SDF disabled opaque grayscale");
            Near(nonInverted[i], Decode(palette.Evaluate(distance[i].r)), "SDF palette from linear data", .022f);
            Near(sdfMapped[i], Decode(palette.Evaluate(inverse[i].r)), "SDF inversion before gradient", .022f);
        }
        foreach (var doc in new[] { noiseDoc, sdfDoc })
        {
            var source = doc.layers[0].Behaviour;
            // Colored grain bypasses gradient mapping; use monochrome grain for the active-palette clipboard case.
            if (source is NoiseLayerBehaviour grain) grain.whiteNoiseColor = NoiseLayerBehaviour.WhiteNoiseColor.Monochrome;
            Enable(source, false);
            foreach (WhimTexGradientMode mode in Enum.GetValues(typeof(WhimTexGradientMode)))
            {
                ((WhimTexGradient)source.GetType().GetField("gradient").GetValue(source)).Mode = mode;
                using var full = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions {Mode = WhimTexJsonWriteMode.Full}).Json);
                var fullCopy = full.Document.layers[0].Behaviour;
                Check(!Enabled(fullCopy) && ((WhimTexGradient)fullCopy.GetType().GetField("gradient").GetValue(fullCopy)).Equals(source.GetType().GetField("gradient").GetValue(source)), "Full JSON preserves inactive palette " + mode);
                Enable(source, true);
                string json = (string)typeof(WhimTexApi).GetMethod("WritePortableClipboard", F).Invoke(null, new object[] { doc, doc.layers });
                using var copied = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 48, 48 });
                var copy = Document(copied).layers[0].Behaviour;
                Check(Enabled(copy), "Clipboard preserves active gradient output");
                Check(((WhimTexGradient)copy.GetType().GetField("gradient").GetValue(copy)).Equals(source.GetType().GetField("gradient").GetValue(source)), "Clipboard preserves entire palette " + mode + ": " + JsonUtility.ToJson(source.GetType().GetField("gradient").GetValue(source)) + " -> " + JsonUtility.ToJson(copy.GetType().GetField("gradient").GetValue(copy)));
                Enable(source, false);
                string serialized = JsonUtility.ToJson(source);
                var restored = JsonUtility.FromJson(serialized, source.GetType());
                Check(!Enabled((LayerBehaviour)restored), "Unity serialization preserves output");
            }
        }
        noise.whiteNoiseColor = NoiseLayerBehaviour.WhiteNoiseColor.Monochrome;
        Enable(noise, true); noise.gradient = palette.Clone();
        var thumbA = noise.GetPreviewTexture(32).GetPixels();
        Enable(noise, false);
        var thumbB = noise.GetPreviewTexture(32).GetPixels();
        Check(thumbA.Where((c, i) => c != thumbB[i]).Any(), "Thumbnail invalidates on toggle");
        Enable(noise, true); noise.gradient = GradientUtility.CreateLinearWhiteToBlack();
        var thumbC = noise.GetPreviewTexture(32).GetPixels();
        Check(thumbA.Where((c, i) => c != thumbC[i]).Any(), "Thumbnail invalidates on palette");
        noise.gradient = palette.Clone(); sdf.gradient = palette.Clone(); Enable(sdf, true);
        System.IO.Directory.CreateDirectory(WhimTex.Tests.UnityC.FixtureContext.Scope.Temp);
        foreach (var doc in new[] { noiseDoc, sdfDoc })
        {
            var image = doc.ComposeCanvas();
            try { File.WriteAllBytes(WhimTex.Tests.UnityC.FixtureContext.Scope.Temp + "/" + doc.layers[0].Behaviour + ".png", image.EncodeToPNG()); }
            finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image); }
            Cache(doc);
        }
        return "PASS: " + checks + " optional layer gradient checks; transient models only.";
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("OptionalLayerGradientTests.Main", () => { ExecuteMain(); });
}

public sealed class OptionalGradientTestWindow : UnityEditor.EditorWindow { }
