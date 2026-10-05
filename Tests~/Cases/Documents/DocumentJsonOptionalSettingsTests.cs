// Pipeline run_script entry DocumentJsonOptionalSettingsTests.Run. Temporary documents only.
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentJsonOptionalSettingsTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Layers = "\"layers\":[{\"id\":\"incoming\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\",\"storedColor\":[1,0,0,1]}}]";
    static int checks;
    static void Check(bool value, string message) { checks++; UnityBRun.Check(!(!value), message); }
    static string Json(string settings) => "{\"format\":\"whimtex.document\",\"version\":1," +
        (settings == null ? "" : "\"document\":" + settings + ",") + Layers + "}";
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    [Serializable] public class Reply { public bool success, saved; public State document; }
    [Serializable] public class State { public string revision; public int width, height; }
    static Reply Parse(string json)
    {
        var converter = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        return (Reply)converter.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
            .Invoke(null, new object[] { json, typeof(Reply) });
    }
    static string Request(string action, string path, string json, string extra = "") => WhimTexApi.DocumentJson(
        "{\"apiVersion\":1,\"compile\":false,\"action\":\"" + action + "\",\"assetPath\":\"" + path + "\",\"json\":" + json + extra + "}");
    static string ExecuteRun()
    {
        checks = 0;
        string path = UnityBRun.AssetPath("__WhimTexOptionalJson_") + Guid.NewGuid().ToString("N") + ".whimtex.json";
        using var defaults = WhimTexDocumentJson.Read(Json("{}"), false);
        int defaultWidth = defaults.Document.width, defaultHeight = defaults.Document.height;
        var destination = UnityBRun.Create<TextureCompositor>();
        destination.width = 32; destination.height = 16;
        destination.outputFilter = FilterMode.Trilinear; destination.outputSrgb = false;
        try
        {
            WhimTexDocumentFile.SaveJson(destination, path);
            string revision = Parse(WhimTexApi.Inspect(path)).document.revision;
            string unchanged = File.ReadAllText(path);
            string[] cases = { null, "{}", "{\"width\":64}", "{\"height\":24}", "{\"outputFilter\":\"Point\",\"outputSrgb\":true}" };
            for (int i = 0; i < cases.Length; i++)
            {
                string json = Json(cases[i]);
                int sourceWidth = i == 2 ? 64 : 32, sourceHeight = i == 3 ? 24 : 16;
                using (var opened = WhimTexDocumentJson.Read(json, false))
                {
                    Check(opened.Document.width == (i == 2 ? 64 : defaultWidth) && opened.Document.height == (i == 3 ? 24 : defaultHeight),
                        "Open did not use independent version-default dimensions: " + i);
                    Check(opened.Document.layers.Count == 1 && opened.Document.layers[0].Id == "incoming", "Open lost content.");
                    if (i == 4) Check(opened.Document.outputFilter == FilterMode.Point && opened.Document.outputSrgb, "Explicit settings lost.");
                    foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                    {
                        string saved = WhimTexDocumentJson.Write(opened.Document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                        Check(saved.Contains("\"document\"") && saved.Contains("\"width\": " + opened.Document.width) &&
                            saved.Contains("\"height\": " + opened.Document.height), "Writer dropped canvas context: " + mode);
                        using var roundtrip = WhimTexDocumentJson.Read(saved, false);
                        Check(roundtrip.Document.width == opened.Document.width && roundtrip.Document.height == opened.Document.height, "Save changed size.");
                    }
                }
                Check(Parse(Request("validate", path, json)).success, "Validation requires document.");
                var write = Parse(Request("write", path, json, ",\"save\":false,\"expectedRevision\":\"" + revision + "\""));
                Check(write.success && !write.saved && write.document.width == (i == 2 ? 64 : defaultWidth) &&
                    write.document.height == (i == 3 ? 24 : defaultHeight), "Write did not use format defaults.");
                var insert = Parse(Request("insert", path, json, ",\"save\":false,\"expectedRevision\":\"" + revision + "\""));
                Check(insert.success && !insert.saved && insert.document.width == 32 && insert.document.height == 16, "API insert changed size.");
                Check(File.ReadAllText(path) == unchanged, "Dry operation wrote to destination.");
                foreach (bool resize in new[] { false, true })
                {
                    using var clipboard = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F)
                        .Invoke(null, new object[] { json, 32, 16 });
                    var tree = (TextureCompositor)clipboard.GetType().GetField("Document", F).GetValue(clipboard);
                    bool hasCanvas = (bool)clipboard.GetType().GetField("HasCanvas", F).GetValue(clipboard);
                    Check(hasCanvas == (i == 2 || i == 3), "Size prompt is requested without explicit dimensions.");
                    Check(tree.width == sourceWidth && tree.height == sourceHeight, "Clipboard did not inherit missing axis from destination.");
                    var target = UnityBRun.Create<TextureCompositor>();
                    target.width = 32; target.height = 16; target.outputSrgb = false; target.outputFilter = FilterMode.Trilinear;
                    var window = UnityBRun.Create<TextureCompositorWindow>();
                    try
                    {
                        Call(window, "SetCompositor", target);
                        Call(window, "PasteProceduralClipboard", clipboard, resize && hasCanvas);
                        Check(target.width == (resize && hasCanvas ? sourceWidth : 32) && target.height == (resize && hasCanvas ? sourceHeight : 16), "Paste changed an unspecified axis.");
                        Check(!target.outputSrgb && target.outputFilter == FilterMode.Trilinear, "Paste changed output settings.");
                        Check(target.layers.Count == 1 && target.layers[0].Id != "incoming", "Paste did not insert/remap layer.");
                    }
                    finally { Undo.ClearUndo(target); Object.DestroyImmediate(window); if (target != null) Object.DestroyImmediate(target); }
                }
            }
            foreach (string settings in new[] { "null", "[]", "\"invalid\"", "1", "{\"unknown\":1}", "{\"width\":0}", "{\"height\":-1}", "{\"width\":16384,\"height\":16384}" })
            {
                bool rejected = false;
                try { using var read = WhimTexDocumentJson.Read(Json(settings), false); }
                catch (WhimTexDocumentException) { rejected = true; }
                Check(rejected, "Invalid present document accepted: " + settings);
            }
            Check(Parse(Request("insert", path, Json(null), ",\"expectedRevision\":\"" + revision + "\"")).success, "Saved insertion without document failed.");
            var loaded = WhimTexDocumentFile.Load(path);
            try { Check(loaded.width == 32 && loaded.height == 16 && loaded.layers.Count == 1 && !loaded.outputSrgb, "Saved insertion changed destination."); }
            finally { Object.DestroyImmediate(loaded); }
            return "";
        }
        finally { Object.DestroyImmediate(destination); if (File.Exists(path)) AssetDatabase.DeleteAsset(path); }
    }
    public static string Run() => UnityBRun.Run("DocumentJsonOptionalSettingsSmoke.Run", () => ExecuteRun());
}

