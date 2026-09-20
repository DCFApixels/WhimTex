// run_script entry TiffAgentApiSmoke.Run. Creates and removes only its own temporary assets.
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class TiffAgentApiSmoke
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
    private static string Revision(string response)
    {
        const string marker = "\"revision\":\"";
        int start = response.IndexOf(marker, StringComparison.Ordinal);
        Check(start >= 0, "TIFF inspect includes revision");
        start += marker.Length;
        int end = response.IndexOf('"', start);
        Check(end > start, "TIFF revision is readable");
        return response.Substring(start, end - start);
    }

    public static string Run()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folder = "Assets/WhimTexTiffAgent_" + Guid.NewGuid().ToString("N");
        string preview = "Temp/WhimTex/" + Guid.NewGuid().ToString("N") + ".png";
        string previewFull = Path.Combine(projectRoot, preview);
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        string tiff = folder + "/Generated.tiff";
        string legacy = folder + "/Legacy.asset";
        string migrated = folder + "/Migrated.tiff";
        TextureCompositor legacyDocument = null;
        try
        {
            string describe = WhimTexApi.Describe();
            Check(describe.Contains("\"agentModes\"") && describe.Contains("whimtex_batch_execute") &&
                describe.Contains("whimtex_headless_live") && describe.Contains("whimtex_assistant_live"),
                "Describe exposes the three agent modes");
            string create = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + tiff + "\",\"create\":true,\"width\":32,\"height\":32,\"operations\":[{\"op\":\"add\",\"type\":\"color\",\"as\":\"base\",\"settings\":{\"name\":\"Base\",\"color\":[0.2,0.4,0.8,1]}}]}");
            Check(create.Contains("\"success\":true"), "TIFF create through ExecuteJson");
            string inspect = WhimTexApi.Inspect(tiff);
            Check(inspect.Contains("\"success\":true") && inspect.Contains("Generated"), "TIFF inspect");
            string revision = Revision(inspect);
            string storage = WhimTexApi.InspectStorage(tiff);
            Check(storage.Contains("\"success\":true") && storage.Contains("document"), "TIFF storage inspection");
            string validation = WhimTexApi.Validate(tiff, true);
            Check(validation.Contains("\"success\":true") && validation.Contains("\"valid\":true") && validation.Contains("\"rendered\":true"), "TIFF validation and optional render");
            string status = WhimTexApi.Status(tiff);
            Check(status.Contains("\"success\":true") && status.Contains("\"diskRevision\":"), "TIFF status");
            string dryRun = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + tiff + "\",\"expectedRevision\":\"" + revision + "\",\"dryRun\":true,\"operations\":[{\"op\":\"add\",\"type\":\"color\",\"as\":\"probe\",\"settings\":{\"name\":\"Probe\",\"color\":[0,1,0,1]}}]}");
            Check(dryRun.Contains("\"success\":true") && dryRun.Contains("\"dryRun\":true"), "TIFF dry-run preflight");
            Check(!WhimTexApi.Inspect(tiff).Contains("Probe"), "TIFF dry-run does not persist changes");
            string edit = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + tiff + "\",\"expectedRevision\":\"" + revision + "\",\"operations\":[{\"op\":\"add\",\"type\":\"color\",\"as\":\"overlay\",\"settings\":{\"name\":\"Overlay\",\"color\":[1,0.1,0.05,1]}}]}");
            Check(edit.Contains("\"success\":true") && edit.Contains("\"saved\":true"), "TIFF edit and save through ExecuteJson");
            string edited = WhimTexApi.Inspect(tiff);
            Check(edited.Contains("Overlay"), "TIFF edit survives reload");
            string render = WhimTexApi.Render(tiff, preview, 64);
            Check(render.Contains("\"success\":true") && File.Exists(previewFull), "TIFF render");

            legacyDocument = ScriptableObject.CreateInstance<TextureCompositor>();
            legacyDocument.hideFlags = HideFlags.HideAndDontSave;
            legacyDocument.width = 32; legacyDocument.height = 32;
            legacyDocument.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.red }));
            typeof(TextureCompositor).GetMethod("SaveWithOutput", Any).Invoke(legacyDocument, new object[] { legacy });
            UnityEngine.Object.DestroyImmediate(legacyDocument); legacyDocument = null;
            string migration = WhimTexApi.Migrate(legacy, migrated);
            Check(migration.Contains("\"success\":true") && File.Exists(Path.Combine(projectRoot, migrated)), "legacy to TIFF migration");
            Check(File.Exists(Path.Combine(projectRoot, legacy)), "legacy source remains after migration");
            string legacyValidation = WhimTexApi.Validate(legacy, false);
            Check(legacyValidation.Contains("\"success\":true") && legacyValidation.Contains("\"valid\":true"), "legacy validation remains available");
            string legacyRevision = Revision(WhimTexApi.Inspect(legacy));
            string legacyEdit = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + legacy + "\",\"expectedRevision\":\"" + legacyRevision + "\",\"operations\":[]}");
            Check(legacyEdit.Contains("\"success\":false") && legacyEdit.Contains("legacy_read_only"), "legacy save is rejected by agent batch API");
            string legacyCreate = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + folder + "/New.asset\",\"create\":true,\"width\":8,\"height\":8,\"operations\":[]}");
            Check(legacyCreate.Contains("\"success\":false") && legacyCreate.Contains("legacy_read_only"), "legacy creation is rejected by agent batch API");
            return "PASS: TIFF create/edit, legacy read/migrate, legacy write rejection, and source preservation.";
        }
        finally
        {
            if (legacyDocument != null) UnityEngine.Object.DestroyImmediate(legacyDocument);
            if (File.Exists(previewFull)) File.Delete(previewFull);
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
