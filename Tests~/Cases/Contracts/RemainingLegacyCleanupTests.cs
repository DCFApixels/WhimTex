// run_script entry RemainingLegacyCleanupTests.Run. Only transient owned objects.
using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class RemainingLegacyCleanupTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Package = typeof(ShaderFX).Assembly;
    static int checks;
    static void Check(bool value, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
    static object Field(object value, string name) => value.GetType().GetField(name, F).GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static string Code(ShaderFX fx) => (string)Field(fx, "code");
    static List<ShaderFXParameter> Parameters(ShaderFX fx) => (List<ShaderFXParameter>)Field(fx, "parameters");
    static object Call(string type, string method, params object[] args) => Package.GetType("DCFApixels.WhimTex." + type)
        .GetMethod(method, F).Invoke(null, args);
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (WhimTexDocumentException) { WhimTex.Tests.UnityC.FixtureContext.Context.True(true, "Expected rejection was observed"); checks++; return; }
        Check(false, message);
    }
    static string ExecuteRun()
    {
        checks = 0;
        Check(typeof(ShaderFXParameter).GetField("declaredInCode", F) == null, "No manual/code mode flag");
        Check(Package.GetType("DCFApixels.WhimTex.ShaderFXParameterDrawer") == null, "No manual drawer");
        Check(typeof(WhimTexWindow).GetField("documentFilePath", F) == null &&
            typeof(WhimTexWindow).GetField("documentFileGuid", F) == null &&
            typeof(WhimTexWindow).GetField("documentFileOwner", F) == null, "No duplicated window binding");
        foreach (string kind in new[] { "document", "fragment", "layers" })
            Reject(() => WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":2,\"kind\":\"" + kind + "\",\"layers\":[]}", false).Dispose(), "Root kind accepted");
        const string explicitAxes = "{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[" +
            "{\"id\":\"noise\",\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\",\"scale\":23,\"scaleY\":23,\"warpScale\":3,\"warpScaleY\":3}}," +
            "{\"id\":\"pattern\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\",\"pattern\":{\"size\":37,\"sizeY\":37}}}," +
            "{\"id\":\"shape\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\",\"rectangleCorners\":[{\"amount\":0.4},{\"amount\":0.2},{\"amount\":0.4},{}]}}]}";
        using (var read = WhimTexDocumentJson.Read(explicitAxes, false))
        {
            var noise = (NoiseLayerBehaviour)read.Document.layers[0].Behaviour;
            var pattern = ((ColorFillLayerBehaviour)read.Document.layers[1].Behaviour).pattern;
            var shape = (ShapeLayerBehaviour)read.Document.layers[2].Behaviour;
            Check(noise.scaleY == 23 && noise.warpScaleY == 3, "Explicit Noise axes retained");
            Check(pattern.sizeY == 37, "Explicit Pattern axis retained");
            Check(shape.rectangleCorners[0].amount == .4f && shape.rectangleCorners[1].amount == .2f &&
                shape.rectangleCorners[2].amount == .4f && shape.rectangleCorners[3].amount == 0, "Explicit corners retained");
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var written = WhimTexDocumentJson.Write(read.Document, new WhimTexJsonWriteOptions { Mode = mode });
                using var again = WhimTexDocumentJson.Read(written.Json, false);
                Check(((NoiseLayerBehaviour)again.Document.layers[0].Behaviour).Scale == noise.Scale, "Explicit axes roundtrip " + mode);
                var corners = ((ShapeLayerBehaviour)again.Document.layers[2].Behaviour).rectangleCorners;
                for (int i = 0; i < 4; i++) Check(corners[i].Equals(shape.rectangleCorners[i]), "Explicit corners roundtrip " + mode);
            }
        }
        Check(new NoiseLayerBehaviour().scaleY == 8 && new NoiseLayerBehaviour().warpScaleY == 1, "New Noise explicit defaults");
        Check(new FillPatternSettings().sizeY == 64 && new ShapeLayerBehaviour().rectangleCorners[0].amount == 0, "New Pattern/Shape explicit defaults");

        using (var omitted = WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[" +
            "{\"id\":\"n\",\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\",\"scale\":23,\"warpScale\":3}}," +
            "{\"id\":\"p\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\",\"pattern\":{\"size\":37}}}]}" , false))
        {
            var noise = (NoiseLayerBehaviour)omitted.Document.layers[0].Behaviour;
            Check(noise.scaleY == 8 && noise.warpScaleY == 1 && noise.scaleZ == 1, "Omitted Noise axes use fixed defaults, not X");
            Check(((ColorFillLayerBehaviour)omitted.Document.layers[1].Behaviour).pattern.sizeY == 64, "Omitted Pattern axis uses fixed default");
        }

        foreach (bool declared in new[] { false, true })
        {
            string json = "{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"fx-layer\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$type\":\"ShaderFX\",\"parameters\":[{\"name\":\"_Removed\",\"type\":\"Float\",\"declaredInCode\":" + (declared ? "true" : "false") + "}]}]}]}";
            Reject(() => WhimTexDocumentJson.Read(json, false).Dispose(), "Removed declaration metadata accepted");
        }

        // An unapplied current draft retains stored values without generating declarations.
        var source = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<ShaderFX>());
        ShaderFX loaded = null;
        ShaderFX copied = null;
        var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
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
            Check(Parameters(loaded).Count == saved.Count, "All stored draft parameter types retained");
            Check(Code(loaded).StartsWith("// @whimtex-effect Tests/Saved\n"), "Header preserved");
            foreach (var old in saved)
            {
                var modern = Parameters(loaded).Find(p => p.name == old.name);
                Check(modern != null && modern.id == old.id && modern.controls.Count == 0, "Stored draft and stable ID " + old.type);
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
            Check(Code(copied) == canonical && Parameters(copied).Count == saved.Count, "Current draft roundtrip");
            Check(((IReadOnlyList<string>)nextRead.GetType().GetProperty("SkippedFields", F).GetValue(nextRead)).Count == 0, "No lost/unknown fields");
            return "PASS: remaining legacy cleanup checks=" + checks + "; explicit axes/corners, retired metadata rejection, stored FX draft types/IDs/values, arbitrary curves/gradients, texture sampling and idempotence.";
        }
        finally
        {
            foreach (var fx in new[] { loaded, copied })
            {
                if (fx == null) continue;
                var image = Parameters(fx).Find(p => p.type == ShaderFXParameterType.Texture2D)?.textureValue;
                if (image != null && image != texture) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image);
                WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(fx);
            }
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(source); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(texture);
        }
    }

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("RemainingLegacyCleanupTests.Run", () => { ExecuteRun(); });
}
