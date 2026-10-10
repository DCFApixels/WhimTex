using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentJsonApiTests
{
    [Serializable] public class Reply { public bool success; public string error; public State document; }
    [Serializable] public class State { public string revision; }
    static Reply Check(string json)
    {
        var converter = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var reply = (Reply)converter.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { json, typeof(Reply) });
        UnityBRun.Check(!(!reply.success), json);
        return reply;
    }
    static string ExecuteRun()
    {
        string path = UnityBRun.AssetPath("WhimTexJsonApi_") + Guid.NewGuid().ToString("N") + ".whimtex.json";
        const string sourcePath = "Assets/Learn/Pass/Sphere_Distortion.tiff";
        byte[] originalSource = File.ReadAllBytes(sourcePath);
        byte[] originalMeta = File.Exists(sourcePath + ".meta") ? File.ReadAllBytes(sourcePath + ".meta") : null;
        var source = UnityBRun.Track(WhimTexDocumentFile.Load(sourcePath));
        try
        {
            string json = WhimTexDocumentJson.Write(source).Json;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"write\",\"assetPath\":\"" + path + "\",\"json\":" + json + "}"));
            var first = Check(WhimTexApi.Inspect(path));
            var second = Check(WhimTexApi.Inspect(path));
            UnityBRun.Check(!(first.document.revision != second.document.revision), "JSON revision changed on read.");
            Check(WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + first.document.revision +
                "\",\"operations\":[{\"op\":\"resize\",\"width\":128,\"height\":128}]}"));
            using var read = WhimTexDocumentJson.Read(File.ReadAllText(path));
            UnityBRun.Check(!(read.Document.width != 128), "Batch failed to save JSON.");
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"serialize\",\"assetPath\":\"" + path + "\"}"));
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"validate\",\"json\":" + json + "}"));
            string revision = Check(WhimTexApi.Inspect(path)).document.revision;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"write\",\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + revision + "\",\"mode\":\"Compact\",\"json\":" + json + "}"));
            UnityBRun.Check(!(File.ReadAllText(path).Contains("\"grainColor\"")), "Write ignored Compact mode.");
            string fragment = "{\"format\":\"whimtex.document\",\"version\":2,\"document\":{\"width\":32,\"height\":32},\"layers\":[{\"id\":\"incoming\",\"layerName\":\"Inserted\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}}]}";
            revision = Check(WhimTexApi.Inspect(path)).document.revision;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"insert\",\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + revision + "\",\"json\":" + fragment + "}"));
            string replacementId;
            using (var inserted = WhimTexDocumentJson.Read(File.ReadAllText(path)))
            {
                UnityBRun.Check(!(inserted.Document.layers.Count != source.layers.Count + 1 || inserted.Document.width != source.width), "Insert overwrote context or hierarchy.");
                replacementId = inserted.Document.layers[0].Id;
                UnityBRun.Check(!(replacementId == "incoming"), "Insert did not remap IDs.");
            }
            revision = Check(WhimTexApi.Inspect(path)).document.revision;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"replace\",\"assetPath\":\"" + path + "\",\"layerId\":\"" + replacementId + "\",\"expectedRevision\":\"" + revision + "\",\"json\":" + fragment.Replace("ColorFillLayerBehaviour", "NoiseLayerBehaviour") + "}"));
            using (var replaced = WhimTexDocumentJson.Read(File.ReadAllText(path)))
                UnityBRun.Check(!(replaced.Document.layers[0].Id != replacementId || !(replaced.Document.layers[0].Behaviour is NoiseLayerBehaviour)), "Replacement changed identity or failed.");
            UnityBRun.Check(originalSource.AsSpan().SequenceEqual(File.ReadAllBytes(sourcePath)), "JSON API operations preserve source TIFF bytes.");
            UnityBRun.Check(originalMeta == null ? !File.Exists(sourcePath + ".meta") : originalMeta.AsSpan().SequenceEqual(File.ReadAllBytes(sourcePath + ".meta")), "JSON API operations preserve source TIFF import settings.");
            return "";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        }
    }
    public static string Run() => UnityBRun.Run("DocumentJsonApiSmoke.Run", () => ExecuteRun());
}
