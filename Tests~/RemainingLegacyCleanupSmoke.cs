// run_script entry RemainingLegacyCleanupSmoke.Run. Only transient owned objects.
using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class RemainingLegacyCleanupSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Package = typeof(ShaderFX).Assembly;
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static object Field(object value, string name) => value.GetType().GetField(name, F).GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static string Code(ShaderFX fx) => (string)Field(fx, "code");
    static List<ShaderFXParameter> Parameters(ShaderFX fx) => (List<ShaderFXParameter>)Field(fx, "parameters");
    static object Call(string type, string method, params object[] args) => Package.GetType("DCFApixels.WhimTex." + type)
        .GetMethod(method, F).Invoke(null, args);
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (WhimTexDocumentException) { checks++; return; }
        throw new Exception(message);
    }
    public static string Run()
    {
        checks = 0;
        Check(typeof(ShaderFXParameter).GetField("declaredInCode", F) == null, "No manual/code mode flag");
        Check(Package.GetType("DCFApixels.WhimTex.ShaderFXParameterDrawer") == null, "No manual drawer");
        Check(typeof(TextureCompositorWindow).GetField("documentFilePath", F) == null &&
            typeof(TextureCompositorWindow).GetField("documentFileGuid", F) == null &&
            typeof(TextureCompositorWindow).GetField("documentFileOwner", F) == null, "No duplicated window binding");
        foreach (string kind in new[] { "document", "fragment", "layers" })
            Reject(() => WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":1,\"kind\":\"" + kind + "\",\"layers\":[]}", false).Dispose(), "Root kind accepted");
        const string oldAxes = "{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[" +
            "{\"id\":\"noise\",\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\",\"scale\":23,\"warpScale\":3}}," +
            "{\"id\":\"pattern\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\",\"pattern\":{\"size\":37}}}," +
            "{\"id\":\"shape\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\",\"roundness\":0.4,\"cornerRoundness\":[-1,0.2,-1,0]}}]}";
        using (var read = WhimTexDocumentJson.Read(oldAxes, false))
        {
            var noise = (NoiseLayerBehaviour)read.Document.layers[0].Behaviour;
            var pattern = ((ColorFillLayerBehaviour)read.Document.layers[1].Behaviour).pattern;
            var shape = (ShapeLayerBehaviour)read.Document.layers[2].Behaviour;
            Check(noise.scaleY == 23 && noise.warpScaleY == 3, "Omitted Compact axes normalized");
            Check(pattern.sizeY == 37, "Pattern omitted axis normalized");
            Check(shape.cornerRoundness == new Vector4(.4f, .2f, .4f, 0), "Each inherited corner normalized independently");
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var written = WhimTexDocumentJson.Write(read.Document, new WhimTexJsonWriteOptions { Mode = mode });
                using var again = WhimTexDocumentJson.Read(written.Json, false);
                Check(((NoiseLayerBehaviour)again.Document.layers[0].Behaviour).Scale == noise.Scale, "Explicit axes roundtrip " + mode);
                Check(((ShapeLayerBehaviour)again.Document.layers[2].Behaviour).cornerRoundness == shape.cornerRoundness, "Explicit corners roundtrip " + mode);
            }
        }
        Check(new NoiseLayerBehaviour().scaleY == 8 && new NoiseLayerBehaviour().warpScaleY == 1, "New Noise explicit defaults");
        Check(new FillPatternSettings().sizeY == 64 && new ShapeLayerBehaviour().cornerRoundness == Vector4.zero, "New Pattern/Shape explicit defaults");

        foreach (bool declared in new[] { false, true })
        {
            const string removed = "float4 ApplyFX(float2 uv, float4 color) { return color; }";
            string json = "{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"fx-layer\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"modifiers\":[{\"$type\":\"ShaderFX\",\"code\":\"" + removed +
                "\",\"parameters\":[{\"name\":\"_Removed\",\"type\":\"Float\",\"floatValue\":0.625,\"declaredInCode\":" + (declared ? "true" : "false") + "}]}]}]}";
            using var read = WhimTexDocumentJson.Read(json, false);
            var effect = (ShaderFX)read.Document.layers[0].modifiers[0];
            Check(Parameters(effect).Count == (declared ? 0 : 1), "Only saved manual definitions are converted; declared=" + declared);
            if (declared) Check(Code(effect) == removed, "A removed 0.12.5 code declaration is not resurrected");
            else
            {
                Set(effect, "code", removed);
                string jsonDraft = WhimTexDocumentJson.Write(read.Document).Json;
                using var again = WhimTexDocumentJson.Read(jsonDraft, false);
                Check(Parameters((ShaderFX)again.Document.layers[0].modifiers[0]).Count == 0, "New JSON does not restore an unapplied declaration deletion");
                Check(Parameters(effect).Count == 1, "JSON writing does not mutate the pending authoring state");
            }
        }

        // Simulate the named-field 0.12.5 model without regenerating any frozen fixture.
        var source = ScriptableObject.CreateInstance<ShaderFX>();
        ShaderFX loaded = null;
        ShaderFX copied = null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); texture.Apply();
        texture.filterMode = FilterMode.Point; texture.wrapModeU = TextureWrapMode.Repeat;
        var saved = new List<ShaderFXParameter>();
        try
        {
            foreach (ShaderFXParameterType type in Enum.GetValues(typeof(ShaderFXParameterType)))
            {
                var p = new ShaderFXParameter { name = "_Saved" + type, type = type, floatValue = .625f,
                    colorValue = new Color(2, -.2f, .4f, .3f), vectorValue = new Vector4(.2f, .7f, -.4f, 8),
                    textureValue = type == ShaderFXParameterType.Texture2D ? texture : null,
                    transformValue = new ShaderFXTransform { position = new Double2(.2, .8), size = new Double2(.7, 1.2), rotation = 31 },
                    gradientValue = new WhimTexGradient(),
                    curveValue = new AnimationCurve(new Keyframe(0, 2), new Keyframe(.3f, -.7f), new Keyframe(1, 3)) };
                p.gradientValue.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.green, .4f), new GradientColorKey(Color.blue, 1) },
                    new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.8f, 1) });
                saved.Add(p);
            }
            Set(source, "code", "// @whimtex-effect Tests/Saved\nfloat4 ApplyFX(float2 uv, float4 color) { return color; }");
            Set(source, "parameters", saved);
            using var container = new WhimTexDocumentContainer();
            byte[] bytes = (byte[])Call("WhimTexDocumentSerializer", "Serialize", source, container);
            var read = Call("WhimTexDocumentSerializer", "Deserialize", bytes, container, typeof(ShaderFX), null, false);
            loaded = (ShaderFX)read.GetType().GetProperty("Model", F).GetValue(read);
            Check(Parameters(loaded).Count == saved.Count, "All saved parameter types declared");
            Check(Code(loaded).StartsWith("// @whimtex-effect Tests/Saved\n"), "Header preserved");
            foreach (var old in saved)
            {
                var modern = Parameters(loaded).Find(p => p.name == old.name);
                Check(modern != null && modern.id == old.id && modern.controls.Count == 1, "Declaration and stable ID " + old.type);
                Check(modern.floatValue == old.floatValue && modern.colorValue == old.colorValue && modern.vectorValue == old.vectorValue, "Values " + old.type);
                Check(modern.gradientValue.Equals(old.gradientValue) && modern.curveValue.Equals(old.curveValue), "Arbitrary gradient/curve " + old.type);
                Check(modern.transformValue.position == old.transformValue.position && modern.transformValue.size == old.transformValue.size && modern.transformValue.rotation == old.transformValue.rotation, "Transform " + old.type);
            }
            var image = Parameters(loaded).Find(p => p.type == ShaderFXParameterType.Texture2D).textureValue;
            Check(image != null && image.filterMode == texture.filterMode && image.wrapModeU == texture.wrapModeU && image.GetPixel(0, 0) == Color.red, "Texture value and sampling");
            string canonical = Code(loaded);
            using var nextContainer = new WhimTexDocumentContainer();
            byte[] next = (byte[])Call("WhimTexDocumentSerializer", "Serialize", loaded, nextContainer);
            var nextRead = Call("WhimTexDocumentSerializer", "Deserialize", next, nextContainer, typeof(ShaderFX), null, false);
            copied = (ShaderFX)nextRead.GetType().GetProperty("Model", F).GetValue(nextRead);
            Check(Code(copied) == canonical && Parameters(copied).Count == saved.Count, "Conversion idempotent");
            Check(((IReadOnlyList<string>)nextRead.GetType().GetProperty("SkippedFields", F).GetValue(nextRead)).Count == 0, "No lost/unknown fields");
            return "PASS: remaining legacy cleanup checks=" + checks + "; explicit axes/corners, root kind rejection, all 13 saved FX types/IDs/values, arbitrary curves/gradients, texture sampling and idempotence.";
        }
        finally
        {
            foreach (var fx in new[] { loaded, copied })
            {
                if (fx == null) continue;
                var image = Parameters(fx).Find(p => p.type == ShaderFXParameterType.Texture2D)?.textureValue;
                if (image != null && image != texture) UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(fx);
            }
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
