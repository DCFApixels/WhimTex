using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentJsonSafetyTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static void Check(bool value, string message) { checks++; UnityBRun.Check(!(!value), message); }
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static string Revision(WhimTexDocument doc)
    {
        try { return (string)typeof(WhimTexApi).GetMethod("Revision", F).Invoke(null, new object[] { doc }); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    static void Bind(WhimTexDocument doc, string path) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentService")
        .GetMethod("Bind", F).Invoke(null, new object[] { doc, path });
    [Serializable] public class Reply { public bool success, saved; public State document; }
    [Serializable] public class State { public string revision; public int width; }
    static Reply Parse(string json)
    {
        var converter = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        return (Reply)converter.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { json, typeof(Reply) });
    }
    static string Request(string path, string json, string suffix = "") =>
        WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"write\",\"compile\":false,\"assetPath\":\"" + path + "\",\"json\":" + json + suffix + "}");
    static string ExecuteRun()
    {
        checks = 0;
        var doc = UnityBRun.Create<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 8;
        string path = UnityBRun.AssetPath("__WhimTexJsonSafety_") + Guid.NewGuid().ToString("N") + ".whimtex.json";
        string tiff = path.Replace(".whimtex.json", ".tiff");
        var pixels = UnityBRun.Track(new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave });
        RenderTexture previous = RenderTexture.active;
        try
        {
            var drawing = new DrawingLayerBehaviour();
            doc.layers.Add(new Layer(drawing));
            Call(doc.layers[0], "AssignNewId");
            Call(drawing, "AdoptStoredTexture", pixels);
            Bind(doc, path);
            pixels.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); pixels.Apply();
            string before = Revision(doc);
            Check(before == Revision(doc), "Unchanged Drawing revision is unstable.");
            pixels.SetPixel(0, 0, Color.black); pixels.Apply();
            string edited = Revision(doc);
            Check(before != edited, "JSON revision ignores CPU pixels.");
            pixels.SetPixel(0, 0, Color.red); pixels.Apply();
            Check(before == Revision(doc), "Restoring pixels did not restore revision.");
            var surface = (RenderTexture)Call(drawing, "EnsurePaintSurface", 2, 2);
            RenderTexture.active = surface; GL.Clear(false, true, Color.cyan); RenderTexture.active = previous;
            typeof(DrawingLayerBehaviour).GetField("paintSurfaceDirty", F).SetValue(drawing, true);
            string painted = Revision(doc);
            Check(painted != before && painted == Revision(doc), "Pending GPU pixels are missing or revision drifts after synchronization.");
            Check(RenderTexture.active == previous, "Revision changed caller render target.");
            Check(!(bool)typeof(DrawingLayerBehaviour).GetField("paintSurfaceDirty", F).GetValue(drawing), "Pending pixels were not synchronized.");
            Call(drawing, "ReleaseTransientResources");
            doc.layers.Clear();
            doc.layers.Add(new Layer(new ColorFillLayerBehaviour()));
            Call(doc.layers[0], "AssignNewId");
            doc.outputFilter = FilterMode.Point;
            string json = WhimTexDocumentJson.Write(doc).Json;
            foreach (string destination in new[] { path, tiff })
            {
                var preview = Parse(Request(destination, json, ",\"save\":false"));
                Check(preview.success && !preview.saved && preview.document.width == 8, "Unsaved write did not return a prospective snapshot.");
                Check(!File.Exists(destination) && !File.Exists(destination + ".meta"), "save:false created a document or importer metadata.");
            }
            Check(Parse(Request(path, json)).saved, "Default write must save.");
            byte[] original = File.ReadAllBytes(path);
            byte[] meta = File.ReadAllBytes(path + ".meta");
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            string revision = Parse(WhimTexApi.Inspect(path)).document.revision;
            doc.width = 16;
            string changed = WhimTexDocumentJson.Write(doc).Json;
            var result = Parse(Request(path, changed, ",\"save\":false,\"expectedRevision\":\"" + revision + "\""));
            Check(result.success && !result.saved && result.document.width == 16, "Existing-file dry write failed.");
            Check(Convert.ToBase64String(original) == Convert.ToBase64String(File.ReadAllBytes(path)) && timestamp == File.GetLastWriteTimeUtc(path), "save:false changed file bytes or timestamp.");
            Check(Convert.ToBase64String(meta) == Convert.ToBase64String(File.ReadAllBytes(path + ".meta")), "save:false changed importer metadata.");
            Check(Parse(WhimTexApi.Inspect(path)).document.revision == revision, "Dry write changed saved revision.");
            Check(!Parse(Request(path, changed, ",\"save\":false,\"expectedRevision\":\"stale\"")).success, "Dry write ignored revision conflict.");

            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            foreach (bool resize in new[] { false, true })
            {
                string fragment = WhimTexDocumentJson.WriteLayers(doc, doc.layers, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var clipboard = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { fragment, 32, 32 });
                Check(clipboard.GetType().GetField("CanvasFilter", F) == null, "Retired source-filter override is still exposed.");
                var destination = UnityBRun.Create<WhimTexDocument>();
                destination.width = destination.height = 32;
                destination.outputFilter = FilterMode.Trilinear;
                destination.outputSrgb = false;
                var window = UnityBRun.Create<WhimTexWindow>();
                try
                {
                    Call(window, "SetDocument", destination);
                    Call(window, "PasteProceduralClipboard", clipboard, resize);
                    Check(destination.outputFilter == FilterMode.Trilinear && !destination.outputSrgb, "Paste changed destination output settings.");
                    Check(destination.width == (resize ? 16 : 32) && destination.height == (resize ? 8 : 32), "Paste ignored canvas-size choice.");
                    Check(destination.layers.Count == 1, "Paste did not add its layer.");
                }
                finally { Undo.ClearUndo(destination); Object.DestroyImmediate(window); if (destination != null) Object.DestroyImmediate(destination); }
            }
            return "";
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(doc);
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            if (File.Exists(tiff)) AssetDatabase.DeleteAsset(tiff);
        }
    }
    public static string Run() => UnityBRun.Run("DocumentJsonSafetySmoke.Run", () => ExecuteRun());
}

