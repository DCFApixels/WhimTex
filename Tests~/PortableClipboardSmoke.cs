using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;

public static class PortableClipboardSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static string Main()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        object Read(string json) => typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 128, 128 });
        TextureCompositor Doc(object data) => (TextureCompositor)data.GetType().GetField("Document", F).GetValue(data);
        string Write(TextureCompositor doc, List<Layer> roots) => (string)typeof(WhimTexApi).GetMethod("WritePortableClipboard", F).Invoke(null, new object[] { doc, roots });
        void Compile(object data) => data.GetType().GetMethod("Compile", F).Invoke(data, null);
        void Reject(TextureCompositor doc, List<Layer> roots)
        {
            try { Write(doc, roots); } catch (TargetInvocationException) { checks++; return; }
            throw new Exception("Unsupported selection exported.");
        }
        const string prefix = "{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":";
        using var source = (IDisposable)Read(prefix + "[{\"type\":\"group\",\"transform\":{\"position\":[12,8],\"rotation\":20},\"children\":[{\"type\":\"sdf\",\"id\":\"sdf\",\"target\":\"shape\"},{\"type\":\"shape\",\"id\":\"shape\"},{\"type\":\"gradient\",\"properties\":{\"gradientOptions\":{\"type\":\"Radial\"}}}]}]}");
        var doc = Doc(source);
        string json = Write(doc, doc.layers);
        using var restored = (IDisposable)Read(json);
        var copy = Doc(restored);
        Check(copy.layers[0].transform.Equals(doc.layers[0].transform), "Group transform");
        var children = copy.layers[0].children;
        Check(children.Count == 3, "Children");
        Check(((TargetedLayerBehaviour)children[0].Behaviour).TargetLayerId == children[1].Id, "Remapped Target");
        Check(children[1].Id != doc.layers[0].children[1].Id, "New IDs");
        Reject(doc, new List<Layer> { doc.layers[0].children[0] });

        using var fxData = (IDisposable)Read(prefix + "[{\"type\":\"color\",\"id\":\"source\"},{\"type\":\"color\",\"fx\":[{\"enabled\":false,\"code\":\"// @param float _Gain = 2\\n// @param gradient _Ramp\\n// @param texture2D _Map\\nfloat4 ApplyFX(float2 uv,float4 color){return tex2D(_Map,uv)*_Ramp_Sample(0.5)*_Gain;}\",\"textures\":{\"_Map\":{\"layer\":\"source\"}},\"gradients\":{\"_Ramp\":[{\"time\":0,\"color\":[1,0,0,1]},{\"time\":1,\"color\":[0,1,0,1]}]}}]}]}");
        Compile(fxData);
        var fxDoc = Doc(fxData);
        using var fxCopy = (IDisposable)Read(Write(fxDoc, fxDoc.layers));
        Compile(fxCopy);
        var effect = (ShaderFX)Doc(fxCopy).layers[1].modifiers[0];
        Check(!(bool)typeof(ShaderFX).GetProperty("Active", F).GetValue(effect), "Disabled FX retained");
        var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
        Check(parameters.Find(p => p.name == "_Gain").floatValue == 2, "FX scalar");
        Check(parameters.Find(p => p.name == "_Map").textureLayerId == Doc(fxCopy).layers[0].Id, "FX texture ID");
        Check(parameters.Find(p => p.name == "_Ramp").gradientValue.ColorKeys[0].color.r == 1, "FX gradient");

        using var drawingData = (IDisposable)Read(prefix + "[{\"type\":\"drawing\",\"url\":\"https://example.com/image.png\",\"transform\":{\"scale\":[2,3]}}]}");
        var drawingDoc = Doc(drawingData);
        var drawing = (DrawingLayerBehaviour)drawingDoc.layers[0].Behaviour;
        var texture = new Texture2D(4,4);
        typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", F).Invoke(drawing, new object[] { texture });
        typeof(DrawingLayerBehaviour).GetMethod("RememberImageUrl", F).Invoke(drawing, new object[] { "https://example.com/image.png" });
        drawingDoc.layers[0].transform.rotation = 15;
        using var drawingCopy = (IDisposable)Read(Write(drawingDoc, drawingDoc.layers));
        Check(Doc(drawingCopy).layers[0].transform.scale == new Double2(2,3), "Drawing explicit scale");
        Check(Doc(drawingCopy).layers[0].transform.rotation == 15, "Transform does not invalidate URL");
        var revision = typeof(DrawingLayerBehaviour).GetField("pixelsRevision", F);
        revision.SetValue(drawing, (int)revision.GetValue(drawing) + 1);
        string emptyJson = Write(drawingDoc, drawingDoc.layers);
        Check(emptyJson.Contains("contentOmitted"), "Edited Drawing becomes placeholder");
        using var emptyCopy = (IDisposable)Read(emptyJson);
        Check(Doc(emptyCopy).layers[0].Behaviour is DrawingLayerBehaviour, "Drawing placeholder retained");
        Check(((List<string>)emptyCopy.GetType().GetField("Warnings", F).GetValue(emptyCopy)).Count == 1, "Missing pixels warning");
        revision.SetValue(drawing, (int)revision.GetValue(drawing) - 1);
        Check(Write(drawingDoc, drawingDoc.layers).Contains("https://example.com/image.png"), "Undo restores provenance");
        using var fileData = (IDisposable)Read(prefix + "[{\"type\":\"file\",\"id\":\"file\"},{\"type\":\"sdf\",\"target\":\"file\"}]}");
        var fileDoc = Doc(fileData);
        using var fileCopy = (IDisposable)Read(Write(fileDoc, fileDoc.layers));
        Check(((TargetedLayerBehaviour)Doc(fileCopy).layers[1].Behaviour).TargetLayerId == Doc(fileCopy).layers[0].Id, "Reference to empty File retained");
        const string missingGuid = "ffffffffffffffffffffffffffffffff";
        using var missing = (IDisposable)Read(prefix + "[{\"type\":\"file\",\"asset\":{\"guid\":\"" + missingGuid + "\",\"localId\":\"2800000\"}}]}");
        Check(((FileLayerBehaviour)Doc(missing).layers[0].Behaviour).sourceTexture == null, "Unknown GUID stays empty");
        Check(Write(Doc(missing), Doc(missing).layers).Contains(missingGuid), "Unknown GUID survives recopy");
        Texture2D assetTexture = null;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Packages/com.dcfapixels.whimtex" }))
        {
            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                if (candidate is Texture2D tex) { assetTexture = tex; break; }
            if (assetTexture != null) break;
        }
        Check(assetTexture != null, "Existing texture test fixture");
        ((FileLayerBehaviour)fileDoc.layers[0].Behaviour).sourceTexture = assetTexture;
        using var resolved = (IDisposable)Read(Write(fileDoc, fileDoc.layers));
        Check(((FileLayerBehaviour)Doc(resolved).layers[0].Behaviour).sourceTexture == assetTexture, "Asset GUID and local ID resolve exact texture");
        return "PASS: " + checks + " portable hierarchy, transforms, Target/FX references, values, URL provenance and rejection checks.";
    }
}
