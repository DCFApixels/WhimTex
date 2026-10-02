using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentJsonApiSmoke
{
    [Serializable] public class Reply { public bool success; public string error; public State document; }
    [Serializable] public class State { public string revision; }
    static Reply Check(string json)
    {
        var converter = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var reply = (Reply)converter.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { json, typeof(Reply) });
        if (!reply.success) throw new Exception(json);
        return reply;
    }
    public static string Run()
    {
        string path = "Assets/WhimTexJsonApi_" + Guid.NewGuid().ToString("N") + ".whimtex.json";
        var source = WhimTexDocumentFile.Load("Assets/Learn/Pass/Sphere_Distortion.tiff");
        try
        {
            string json = WhimTexDocumentJson.Write(source).Json;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"write\",\"assetPath\":\"" + path + "\",\"json\":" + json + "}"));
            var first = Check(WhimTexApi.Inspect(path));
            var second = Check(WhimTexApi.Inspect(path));
            if (first.document.revision != second.document.revision) throw new Exception("JSON revision changed on read.");
            Check(WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + first.document.revision +
                "\",\"operations\":[{\"op\":\"resize\",\"width\":128,\"height\":128}]}"));
            using var read = WhimTexDocumentJson.Read(File.ReadAllText(path));
            if (read.Document.width != 128) throw new Exception("Batch failed to save JSON.");
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"serialize\",\"assetPath\":\"" + path + "\"}"));
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"validate\",\"json\":" + json + "}"));
            string revision = Check(WhimTexApi.Inspect(path)).document.revision;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"write\",\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + revision + "\",\"mode\":\"Compact\",\"json\":" + json + "}"));
            if (File.ReadAllText(path).Contains("\"whiteNoiseColor\"")) throw new Exception("Write ignored Compact mode.");
            string fragment = "{\"format\":\"whimtex.document\",\"version\":1,\"document\":{\"width\":32,\"height\":32},\"layers\":[{\"id\":\"incoming\",\"layerName\":\"Inserted\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}}]}";
            revision = Check(WhimTexApi.Inspect(path)).document.revision;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"insert\",\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + revision + "\",\"json\":" + fragment + "}"));
            string replacementId;
            using (var inserted = WhimTexDocumentJson.Read(File.ReadAllText(path)))
            {
                if (inserted.Document.layers.Count != source.layers.Count + 1 || inserted.Document.width != source.width) throw new Exception("Insert overwrote context or hierarchy.");
                replacementId = inserted.Document.layers[0].Id;
                if (replacementId == "incoming") throw new Exception("Insert did not remap IDs.");
            }
            revision = Check(WhimTexApi.Inspect(path)).document.revision;
            Check(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"replace\",\"assetPath\":\"" + path + "\",\"layerId\":\"" + replacementId + "\",\"expectedRevision\":\"" + revision + "\",\"json\":" + fragment.Replace("ColorFillLayerBehaviour", "NoiseLayerBehaviour") + "}"));
            using (var replaced = WhimTexDocumentJson.Read(File.ReadAllText(path)))
                if (replaced.Document.layers[0].Id != replacementId || !(replaced.Document.layers[0].Behaviour is NoiseLayerBehaviour)) throw new Exception("Replacement changed identity or failed.");
            return "PASS: JSON create, stable revision, batch, serialize, validate, Compact overwrite, insert and replace.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        }
    }
}
