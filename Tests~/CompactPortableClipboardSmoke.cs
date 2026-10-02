// Pipeline run_script entry CompactPortableClipboardSmoke.Run. Covers the current shared clipboard format.
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class CompactPortableClipboardSmoke
{
    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static string Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        const string legacy = "{'format':'whimtex.layers','version':1,'canvas':{'width':256,'height':128,'filter':'Point'},'layers':" +
            "[{'type':'drawing'},{'type':'file'},{'type':'color'},{'type':'gradient'},{'type':'noise'},{'type':'shape'}," +
            "{'type':'outline'},{'type':'sdf'},{'type':'normalMap'},{'type':'blur'},{'type':'sharpen'}," +
            "{'type':'makeSeamless'},{'type':'shaderProcessor'},{'type':'group'},{'type':'color'}]}";
        using var input = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { legacy, 256, 128 });
        var document = (TextureCompositor)input.GetType().GetField("Document", F).GetValue(input);
        document.outputFilter = FilterMode.Point; // Legacy clipboard carries this separately for the paste destination.
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
        return "PASS: " + checks + " shared compact clipboard checks across all layer types.";
    }
}
