// run_script entry DocumentStorageBoundaryTests.Run. Uses only unique test-owned paths.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentStorageBoundaryTests
{
    static int checks;
    static void Check(bool ok, string message) { checks++; UnityBRun.Check(!(!ok), "FAIL: " + message); }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (WhimTexDocumentException) { Check(true, message); return; }
        Check(false, message);
    }
    static string ExecuteRun()
    {
        string folder = UnityBRun.AssetPath("WhimTexStorageBoundary_") + Guid.NewGuid().ToString("N");
        UnityBRun.EnsureFolder(folder);
        var document = UnityBRun.Create<WhimTexDocument>();
        WhimTexDocument unexpected = null;
        try
        {
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            document.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.red }));
            string archived = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/Compatibility0125/activeDocument.asset";
            Check(!WhimTexDocumentFile.IsDocument(archived), "archived asset is not a document");
            Check(!WhimTexDocumentFile.TryLoad(archived, out unexpected, out _) && unexpected == null, "asset load rejected");
            byte[] before = File.ReadAllBytes(archived);
            Reject(() => WhimTexDocumentFile.Save(document, folder + "/Rejected.asset"), "asset save rejected");
            Check(!File.Exists(folder + "/Rejected.asset") && !File.Exists(folder + "/Rejected.tiff"), "rejected save writes nothing");
            Check(File.ReadAllBytes(archived).SequenceEqual(before), "archived source unchanged");
            string tiff = WhimTexDocumentFile.Save(document, folder + "/Modern.tiff");
            string disguised = folder + "/Disguised.asset";
            File.Copy(tiff, disguised);
            Check(!WhimTexDocumentFile.IsDocument(disguised), "renamed TIFF cannot bypass extension boundary");
            Check(!WhimTexDocumentFile.TryLoad(disguised, out unexpected, out _), "renamed TIFF load rejected");
            foreach (string response in new[] {
                WhimTexApi.Inspect(disguised), WhimTexApi.Status(disguised), WhimTexApi.Validate(disguised, false),
                WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + disguised + "\",\"dryRun\":true,\"operations\":[]}")
            }) Check(response.Contains("\"success\":false") && response.Contains("invalid_path"), "agent asset path rejected");
            Check(typeof(WhimTexApi).GetMethod("Migrate") == null, "migration API removed");
            Check(typeof(WhimTexDocument).GetMethod("SaveLegacyAssetForCompatibility", BindingFlags.NonPublic | BindingFlags.Instance) == null, "legacy writer removed");
            string json = WhimTexDocumentFile.Save(document, folder + "/Modern.json");
            Check(WhimTexDocumentFile.IsDocument(tiff) && WhimTexDocumentFile.IsDocument(json), "TIFF and JSON remain supported");
            return "";
        }
        finally
        {
            if (unexpected != null) UnityEngine.Object.DestroyImmediate(unexpected);
            UnityEngine.Object.DestroyImmediate(document);
            AssetDatabase.DeleteAsset(folder);
        }
    }
    public static string Run() => UnityBRun.Run("DocumentStorageBoundarySmoke.Run", () => ExecuteRun());
}

