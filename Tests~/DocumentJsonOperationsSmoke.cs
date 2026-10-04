// Pipeline run_script entry DocumentJsonOperationsSmoke.Run. Temporary documents only.
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentJsonOperationsSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    [Serializable] public class Reply { public bool success; public State document; }
    [Serializable] public class State { public string revision; }
    static Reply Api(string text)
    {
        var converter = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var reply = (Reply)converter.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
            .Invoke(null, new object[] { text, typeof(Reply) });
        Check(reply.success, text);
        return reply;
    }
    static Layer Add(TextureCompositor doc, LayerBehaviour behaviour)
    {
        var layer = new Layer(behaviour);
        typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
        doc.layers.Add(layer);
        return layer;
    }
    static string Request(string action, string json, string fields = "") => WhimTexApi.DocumentJson(
        "{\"apiVersion\":1,\"compile\":false,\"action\":\"" + action + "\",\"json\":" + json + fields + "}");
    static string PathField(string path) => ",\"assetPath\":\"" + path + "\"";

    public static string Run()
    {
        checks = 0;
        var source = ScriptableObject.CreateInstance<TextureCompositor>();
        string prefix = "Assets/__WhimTexJsonOperations_" + Guid.NewGuid().ToString("N");
        string selectedPath = prefix + "_selected.whimtex.json", destinationPath = prefix + "_destination.whimtex.json";
        try
        {
            source.width = 64; source.height = 32; source.outputFilter = FilterMode.Point; source.outputSrgb = false;
            var selected = Add(source, new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Ellipse });
            Add(source, new ColorFillLayerBehaviour());
            typeof(TextureCompositor).GetMethod("NormalizeModel", F).Invoke(source, null);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var options = new WhimTexJsonWriteOptions { Mode = mode };
                string json = WhimTexDocumentJson.WriteLayers(source, new[] { selected }, options).Json;
                string whole = WhimTexDocumentJson.Write(source, options).Json;
                Check(!json.Contains("\"kind\": \"document\"") && !json.Contains("\"kind\": \"fragment\"") &&
                    !whole.Contains("\"kind\": \"document\""), "Writer emitted a discriminator.");
                using (var read = WhimTexDocumentJson.Read(json, false))
                {
                    Check(read.Document.layers.Count == 1 && read.Document.layers[0].Id == selected.Id, "Selected export lost selection or identity.");
                    Check(((ShapeLayerBehaviour)read.Document.layers[0].Behaviour).kind == ShapeLayerBehaviour.ShapeKind.Ellipse, "Shape kind was removed.");
                    Check(WhimTexDocumentJson.Write(read.Document, options).Json == json, "Selection is not an ordinary document on roundtrip.");
                }
                string validated = Request("validate", json);
                Api(validated);
                Check(!validated.Contains("\"kind\""), "Validation returns obsolete kind.");

                // Exercise the file-open loader directly, without opening a window or touching user documents.
                foreach (string oldKind in new string[] { null })
                {
                    string input = oldKind == null ? json : json.Insert(1, "\"kind\":\"" + oldKind + "\",");
                    File.WriteAllText(selectedPath, input);
                    var opened = WhimTexDocumentFile.Load(selectedPath);
                    try
                    {
                        Check(opened.width == 64 && opened.height == 32 && !opened.outputSrgb && opened.outputFilter == FilterMode.Point,
                            "Open did not restore output settings: " + oldKind);
                        Check(opened.layers.Count == 1 && opened.layers[0].Id == selected.Id, "Open changed selected-layer content: " + oldKind);
                    }
                    finally { Object.DestroyImmediate(opened); }
                    Api(Request("write", input, PathField(selectedPath) + ",\"save\":false,\"expectedRevision\":\"" +
                        Api(WhimTexApi.Inspect(selectedPath)).document.revision + "\""));
                }
                if (File.Exists(selectedPath)) AssetDatabase.DeleteAsset(selectedPath);
                Api(Request("write", json, PathField(selectedPath)));
                Check(!File.ReadAllText(selectedPath).Contains("\"kind\": \"fragment\""), "Write retained discriminator.");

                var destination = ScriptableObject.CreateInstance<TextureCompositor>();
                string keptId;
                try
                {
                    destination.width = 16; destination.height = 8;
                    destination.outputFilter = FilterMode.Trilinear; destination.outputSrgb = true;
                    keptId = Add(destination, new ColorFillLayerBehaviour()).Id;
                    WhimTexDocumentFile.SaveJson(destination, destinationPath);
                }
                finally { Object.DestroyImmediate(destination); }
                string revision = Api(WhimTexApi.Inspect(destinationPath)).document.revision;
                Api(Request("insert", json, PathField(destinationPath) + ",\"expectedRevision\":\"" + revision + "\""));
                string insertedId;
                using (var inserted = WhimTexDocumentJson.Read(File.ReadAllText(destinationPath), false))
                {
                    Check(inserted.Document.width == 16 && inserted.Document.height == 8 && inserted.Document.outputSrgb &&
                        inserted.Document.outputFilter == FilterMode.Trilinear, "Insert overwrote destination settings.");
                    Check(inserted.Document.layers.Count == 2 && inserted.Document.layers[1].Id == keptId, "Insert replaced existing layers.");
                    insertedId = inserted.Document.layers[0].Id;
                    Check(insertedId != selected.Id, "Insert did not remap ID.");
                }
                revision = Api(WhimTexApi.Inspect(destinationPath)).document.revision;
                Api(Request("replace", json, PathField(destinationPath) + ",\"layerId\":\"" + insertedId + "\",\"expectedRevision\":\"" + revision + "\""));
                using (var replaced = WhimTexDocumentJson.Read(File.ReadAllText(destinationPath), false))
                    Check(replaced.Document.layers.Count == 2 && replaced.Document.layers[0].Id == insertedId &&
                        replaced.Document.layers[1].Id == keptId && replaced.Document.width == 16, "Explicit replacement changed unrelated content.");
                AssetDatabase.DeleteAsset(selectedPath);
                AssetDatabase.DeleteAsset(destinationPath);
            }
            return "PASS: " + checks + " unified JSON operation checks across all three write modes.";
        }
        finally
        {
            Object.DestroyImmediate(source);
            foreach (string path in new[] { selectedPath, destinationPath })
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        }
    }
}
