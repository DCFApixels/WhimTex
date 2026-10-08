// Pipeline run_script entry CompactPortableClipboardTests.Run. Covers the current shared clipboard format.
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class CompactPortableClipboardTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static string BodyRun()
    {
        int checks = 0;
        void Check(bool ok, string message) { T.True(ok, message); }
        const string json = "{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"layer0\",\"behaviour\":{\"$type\":\"DrawingLayerBehaviour\"}},{\"id\":\"layer1\",\"behaviour\":{\"$type\":\"FileLayerBehaviour\"}},{\"id\":\"layer2\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}},{\"id\":\"layer3\",\"behaviour\":{\"$type\":\"GradientLayerBehaviour\"}},{\"id\":\"layer4\",\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\"}},{\"id\":\"layer5\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\"}},{\"id\":\"layer6\",\"behaviour\":{\"$type\":\"OutlineLayerBehaviour\"}},{\"id\":\"layer7\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\"}},{\"id\":\"layer8\",\"behaviour\":{\"$type\":\"NormalMapLayerBehaviour\"}},{\"id\":\"layer9\",\"behaviour\":{\"$type\":\"BlurLayerBehaviour\"}},{\"id\":\"layer10\",\"behaviour\":{\"$type\":\"SharpenLayerBehaviour\"}},{\"id\":\"layer11\",\"behaviour\":{\"$type\":\"MakeSeamlessLayerBehaviour\"}},{\"id\":\"layer12\",\"behaviour\":{\"$type\":\"ShaderProcessorLayerBehaviour\"}},{\"id\":\"layer13\",\"behaviour\":{\"$type\":\"GroupLayerBehaviour\"},\"group\":true},{\"id\":\"layer14\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}}],\"document\":{\"width\":256,\"height\":128,\"outputFilter\":\"Point\"}}";
        using var input = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 256, 128 });
        var document = (WhimTexDocument)input.GetType().GetField("Document", F).GetValue(input);
        var noise = (NoiseLayerBehaviour)document.layers[4].Behaviour;
        noise.seed = 731; noise.warp = NoiseLayerBehaviour.WarpType.None; noise.warpStrength = 73;
        document.layers[4].enabled = false;
        var compact = WhimTexDocumentJson.WriteLayers(document, document.layers, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact });
        var full = WhimTexDocumentJson.WriteLayers(document, document.layers, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full });
        Check(compact.Json.Length < full.Json.Length, "Compact did not reduce the file.");
        Check(compact.Json.Contains("whimtex.document") && !compact.Json.Contains("whimtex.layers"), "Writer still emits a legacy format.");
        Check(!compact.Json.Contains("\"warpStrength\""), "Inactive field retained.");
        using var read = WhimTexDocumentJson.Read(compact.Json, false);
        Check(!compact.Json.Contains("\"kind\": \"fragment\"") && read.Document.layers.Count == document.layers.Count, "Selected-layer hierarchy lost or obsolete discriminator written.");
        Check(read.Document.width == 256 && read.Document.height == 128 && read.Document.outputFilter == FilterMode.Point, "Document context lost.");
        for (int i = 0; i < document.layers.Count; i++)
        {
            Check(read.Document.layers[i].Behaviour.GetType() == document.layers[i].Behaviour.GetType(), "Behaviour type changed.");
            Check(read.Document.layers[i].Id == document.layers[i].Id, "Stored identity changed.");
            Check(read.Document.layers[i].transform.Equals(document.layers[i].transform), "Transform changed.");
        }
        var restoredNoise = (NoiseLayerBehaviour)read.Document.layers[4].Behaviour;
        Check(restoredNoise.seed == 731 && !read.Document.layers[4].enabled, "Disabled layer or active settings omitted.");
        Check(restoredNoise.warpStrength == 1, "Versioned inactive default changed.");
        string again = WhimTexDocumentJson.WriteLayers(read.Document, read.Document.layers, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact }).Json;
        Check(compact.Json == again, "Compact round trip is not stable.");
        return null;
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}

