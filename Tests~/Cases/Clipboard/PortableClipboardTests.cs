using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;

public static class PortableClipboardTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static string ExecuteMain()
    {
        int checks = 0;
        void Check(bool ok, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, message); checks++; }
        object Read(string json)
        {
            try { return typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 128, 128 }); }
            catch (TargetInvocationException error) { throw error.GetBaseException(); }
        }
        WhimTexDocument Doc(object data) => (WhimTexDocument)data.GetType().GetField("Document", F).GetValue(data);
        string Write(WhimTexDocument doc, List<Layer> roots) => (string)typeof(WhimTexApi).GetMethod("WritePortableClipboard", F).Invoke(null, new object[] { doc, roots });
        void Compile(object data) => data.GetType().GetMethod("Compile", F).Invoke(data, null);
        void Reject(WhimTexDocument doc, List<Layer> roots)
        {
            try { Write(doc, roots); } catch (TargetInvocationException) { WhimTex.Tests.UnityC.FixtureContext.Context.True(true, "Expected rejection was observed"); checks++; return; }
            WhimTex.Tests.UnityC.FixtureContext.Context.True(false, "Unsupported selection exported.");
        }
        using var source = (IDisposable)Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"group\",\"group\":true,\"behaviour\":{\"$type\":\"GroupLayerBehaviour\"},\"transform\":{\"position\":{\"x\":12,\"y\":8},\"rotation\":20},\"children\":[{\"id\":\"sdf\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\",\"inputMode\":\"Specific\",\"targetLayerId\":\"shape\"}},{\"id\":\"shape\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\"}},{\"id\":\"gradient\",\"behaviour\":{\"$type\":\"GradientLayerBehaviour\",\"gradientType\":\"Radial\"}}]}]}");
        var doc = Doc(source);
        string json = Write(doc, doc.layers);
        using var restored = (IDisposable)Read(json);
        var copy = Doc(restored);
        Check(copy.layers[0].transform.Equals(doc.layers[0].transform), "Group transform");
        var children = copy.layers[0].children;
        Check(children.Count == 3, "Children");
        Check(((TargetedLayerBehaviour)children[0].Behaviour).TargetLayerId == children[1].Id, "Remapped Target");
        Check(children[1].Id == doc.layers[0].children[1].Id, "Stored IDs survive reading; paste remaps them");
        Reject(doc, new List<Layer> { doc.layers[0].children[0] });

        using var fxData = (IDisposable)Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"source\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}},{\"id\":\"effect\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$type\":\"ShaderFX\",\"active\":false,\"code\":\"// @param float _Gain = 2\\n// @param gradient _Ramp\\n// @param texture2D _Map\\nfloat4 ApplyFX(float2 uv,float4 color){return tex2D(_Map,uv)*_Ramp_Sample(0.5)*_Gain;}\",\"parameters\":[{\"name\":\"_Gain\",\"type\":\"Float\",\"floatValue\":2},{\"name\":\"_Ramp\",\"type\":\"Gradient\"},{\"name\":\"_Map\",\"type\":\"Texture2D\",\"textureSource\":\"Layer\",\"textureLayerId\":\"source\"}]}]}]}");
        Compile(fxData);
        var fxDoc = Doc(fxData);
        var sourceParameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fxDoc.layers[1].fx[0]);
        sourceParameters.Find(p => p.name == "_Ramp").gradientValue = GradientUtility.Create(new[] {
            new GradientColorKey(Color.red, 0), new GradientColorKey(Color.green, 1) });
        using var fxCopy = (IDisposable)Read(Write(fxDoc, fxDoc.layers));
        Compile(fxCopy);
        var effect = (ShaderFX)Doc(fxCopy).layers[1].fx[0];
        Check(!(bool)typeof(ShaderFX).GetProperty("Active", F).GetValue(effect), "Disabled FX retained");
        var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
        Check(parameters.Find(p => p.name == "_Gain").floatValue == 2, "FX scalar");
        Check(parameters.Find(p => p.name == "_Map").textureLayerId == Doc(fxCopy).layers[0].Id, "FX texture ID");
        Check(parameters.Find(p => p.name == "_Ramp").gradientValue.ColorKeys[0].color.r == 1, "FX gradient");

        using var drawingData = (IDisposable)Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"drawing\",\"behaviour\":{\"$type\":\"DrawingLayerBehaviour\"},\"transform\":{\"scale\":{\"x\":2,\"y\":3}}}]}");
        var drawingDoc = Doc(drawingData);
        var drawing = (DrawingLayerBehaviour)drawingDoc.layers[0].Behaviour;
        var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(4,4));
        typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", F).Invoke(drawing, new object[] { texture });
        drawingDoc.layers[0].transform.rotation = 15;
        using var drawingCopy = (IDisposable)Read(Write(drawingDoc, drawingDoc.layers));
        Check(Doc(drawingCopy).layers[0].transform.scale == new Double2(2,3), "Drawing explicit scale");
        Check(Doc(drawingCopy).layers[0].transform.rotation == 15, "Drawing rotation retained");
        string emptyJson = Write(drawingDoc, drawingDoc.layers);
        Check(emptyJson.Contains("contentOmitted"), "Nonempty Drawing becomes placeholder");
        using var emptyCopy = (IDisposable)Read(emptyJson);
        Check(Doc(emptyCopy).layers[0].Behaviour is DrawingLayerBehaviour, "Drawing placeholder retained");
        Check(((List<string>)emptyCopy.GetType().GetField("Warnings", F).GetValue(emptyCopy)).Count == 1, "Missing pixels warning");
        Check(!Write(drawingDoc, drawingDoc.layers).Contains("https://example.com/image.png"), "Unified JSON does not disguise unsupported Drawing pixels as an image URL");
        using var fileData = (IDisposable)Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"file\",\"behaviour\":{\"$type\":\"FileLayerBehaviour\"}},{\"id\":\"sdf\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\",\"inputMode\":\"Specific\",\"targetLayerId\":\"file\"}}]}");
        var fileDoc = Doc(fileData);
        using var fileCopy = (IDisposable)Read(Write(fileDoc, fileDoc.layers));
        Check(((TargetedLayerBehaviour)Doc(fileCopy).layers[1].Behaviour).TargetLayerId == Doc(fileCopy).layers[0].Id, "Reference to empty File retained");
        const string missingGuid = "ffffffffffffffffffffffffffffffff";
        using var missing = (IDisposable)Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"missing\",\"behaviour\":{\"$type\":\"FileLayerBehaviour\",\"sourceTexture\":{\"$asset\":{\"guid\":\"ffffffffffffffffffffffffffffffff\",\"path\":\"\",\"localId\":\"2800000\",\"type\":\"UnityEngine.Texture2D\"}}}}]}");
        Check(((FileLayerBehaviour)Doc(missing).layers[0].Behaviour).sourceTexture == null, "Unknown GUID stays empty");
        Check(Write(Doc(missing), Doc(missing).layers).Contains(missingGuid), "Unknown GUID survives recopy");
        Texture2D assetTexture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(2, 2, TextureFormat.RGBA32, false));
        AssetDatabase.CreateAsset(assetTexture, WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder() + "/Reference.asset");
        Check(assetTexture != null, "Existing texture test fixture");
        ((FileLayerBehaviour)fileDoc.layers[0].Behaviour).sourceTexture = assetTexture;
        using var resolved = (IDisposable)Read(Write(fileDoc, fileDoc.layers));
        Check(((FileLayerBehaviour)Doc(resolved).layers[0].Behaviour).sourceTexture == assetTexture, "Asset GUID and local ID resolve exact texture");
        return "PASS: " + checks + " portable hierarchy, transforms, Target/FX references, values, Drawing omission and rejection checks.";
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("PortableClipboardTests.Main", () => { ExecuteMain(); });
}
