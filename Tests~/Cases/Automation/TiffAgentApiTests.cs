// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// run_script entry TiffAgentApiTests.Run. Creates and removes only its own temporary assets.
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class TiffAgentApiTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("TiffAgentApiTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRun();
    });

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Check(bool value, string message) { context.True(value, message); }
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

    private static void ExecuteRun()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folder = fixture.AssetFolder();
        string preview = "Temp/WhimTex/" + Guid.NewGuid().ToString("N") + ".png";
        string previewFull = Path.Combine(projectRoot, preview);
        // GUID asset folder already created by fixture.
        string tiff = folder + "/Generated.tiff";
        string legacy = folder + "/Legacy.asset";
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

            Check(WhimTexApi.Inspect(legacy).Contains("invalid_path"), "asset inspection is rejected");
            Check(WhimTexApi.Validate(legacy, false).Contains("invalid_path"), "asset validation is rejected");
            string legacyEdit = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + legacy + "\",\"dryRun\":true,\"operations\":[]}");
            Check(legacyEdit.Contains("invalid_path"), "asset dry-run is rejected");
            string legacyCreate = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + folder + "/New.asset\",\"create\":true,\"width\":8,\"height\":8,\"operations\":[]}");
            Check(legacyCreate.Contains("\"success\":false") && legacyCreate.Contains("invalid_path"), "legacy creation is rejected by agent batch API");
            return;
        }
        finally
        {
            if (File.Exists(previewFull)) File.Delete(previewFull);
            MigrationD.DeleteAsset(folder);
        }
    }
}

