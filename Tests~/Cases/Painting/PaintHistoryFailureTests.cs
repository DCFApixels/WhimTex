using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    static object FailOwnedHistoryPage(TestContext test, ReuseFixture f)
    {
        var step = Static("PaintHistoryStore", "Get", (string)Get(f.cursor, "Revision"));
        // Transfers are still queued: only this fixture's pending publication is blocked.
        string blocked = (string)Static("PaintHistoryStore", "PagePath", step, 0) + ".tmp";
        Directory.CreateDirectory(blocked);
        Static("PaintTileTransfer", "Drain");
        var page = ((Array)Get(step, "payload")).GetValue(0);
        bool failed = false;
        try { ((Task)Get(page, "write")).GetAwaiter().GetResult(); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { failed = true; }
        test.True(failed && Get(page, "native") != null, "Real background file publication failed and retained its immutable payload");
        Set(page, "errorReported", true); // Expected injection; do not pollute the user's console.
        Static("PaintHistoryStore", "FlushWrites");
        return step;
    }

    public static string RunFailedHistoryBoundary(string boundary) => TestContext.Run("Failed paint history: " + boundary, test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using var f = new ReuseFixture();
        var before = f.Capture();
        f.Stroke(new Color(-.4f, 4f, .7f, .65f));
        FailOwnedHistoryPage(test, f);
        var expected = f.Capture();
        f.Undo(); Exact(test, before, f.Capture(), "Exact masked HDR Undo before boundary");
        f.Redo(); Exact(test, expected, f.Capture(), "Exact masked HDR Redo before boundary");
        if (boundary == "reload") Static("PaintTileTransfer", "DrainAll");
        else if (boundary == "disable") Call(f.document, "OnDisable");
        else throw new ArgumentException("Unknown boundary");
        Exact(test, expected, f.Capture(), "Current image survives boundary with the write failure still active");
        f.Undo(); Exact(test, before, f.Capture(), "Exact masked HDR Undo after boundary");
        f.Redo(); Exact(test, expected, f.Capture(), "Exact masked HDR Redo after boundary");
        Exact(test, f.originalBytes, f.original.GetRawTextureData<byte>().ToArray(), "Hidden straight RGB remains exact");
    });

    static string FailureRoot(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Owned run GUID required");
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/history-failure", id));
    }
    public static string RequestFailedHistoryReload()
    {
        EditorUtility.RequestScriptReload();
        return "Requested a managed reload through the public Editor API.";
    }

    // Invoke Begin, request a normal Editor recompile, then invoke Verify with the same fresh ID.
    public static string BeginFailedHistoryReload(string id) => BeginFailedHistoryReloadCore(id, true);
    public static string BeginFailedHistoryReloadEmpty(string id) => BeginFailedHistoryReloadCore(id, false);
    static string BeginFailedHistoryReloadCore(string id, bool existing) => TestContext.Run("Arm failed-write history across a real reload", test =>
    {
        string root = FailureRoot(id);
        ReusePreflight(test);
        if (Directory.Exists(root)) throw new IOException("Use a fresh owned reload GUID.");
        var preferences = new UnityBToolPreferences();
        var f = new ReuseFixture(); WhimTexWindow window = null; bool armed = false;
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>(); preferences.Own(window);
            window.name = "Failed history " + id; f.document.name = window.name;
            ((Texture2D)Get(f, "mask")).name = window.name;
            Call(window, "SetDocument", f.document); Call(window, "SelectOnlyLayer", f.drawing.Id);
            window.ShowUtility();
            Directory.CreateDirectory(root);
            if (existing)
            {
                f.Stroke(new Color(2.5f, -.2f, .5f, .4f));
                Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            }
            File.WriteAllBytes(Path.Combine(root, "before.bin"), f.Capture());
            File.WriteAllBytes(Path.Combine(root, "original.bin"), f.originalBytes);
            f.Stroke(new Color(-.4f, 4f, .7f, .65f)); FailOwnedHistoryPage(test, f);
            File.WriteAllBytes(Path.Combine(root, "expected.bin"), f.Capture());
            AppDomain.CurrentDomain.SetData("FailedHistory." + id, true);
            armed = true;
        }
        finally
        {
            Application.logMessageReceived -= (Application.LogCallback)Get(f, "logger");
            if (!armed) { if (window != null) { Set(window, "activeDocument", null); Object.DestroyImmediate(window); } f.Dispose(); }
            preferences.Dispose(); preferences.Verify();
        }
    });

    public static string VerifyFailedHistoryReload(string id) => VerifyFailedHistoryReloadCore(id, true);
    public static string VerifyFailedHistoryClose(string id) => VerifyFailedHistoryReloadCore(id, false);
    static string VerifyFailedHistoryReloadCore(string id, bool reloaded) => TestContext.Run("Failed-write replay and saved close; reload=" + reloaded, test =>
    {
        string root = FailureRoot(id);
        var preferences = new UnityBToolPreferences();
        WhimTexWindow window = null; WhimTexDocument document = null, reopened = null;
        try
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexWindow>()) if (candidate.name == "Failed history " + id) window = candidate;
            test.True(window != null, "Owned window survives actual reload");
            preferences.Own(window); document = (WhimTexDocument)Get(window, "activeDocument");
            test.Equal(reloaded, AppDomain.CurrentDomain.GetData("FailedHistory." + id) == null, "Expected domain boundary");
            var drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            var cursor = (Object)Get(drawing, "PixelUndoObject");
            var step = Static("PaintHistoryStore", "Get", (string)Get(cursor, "Revision"));
            test.True(Directory.Exists((string)Static("PaintHistoryStore", "PagePath", step, 0) + ".tmp"), "Write failure remains active after reload");
            var region = new RectInt(-256, 0, 768, 256);
            var expected = File.ReadAllBytes(Path.Combine(root, "expected.bin"));
            var before = File.ReadAllBytes(Path.Combine(root, "before.bin"));
            Exact(test, expected, Probe(drawing, region), "Signed HDR/masked image survives failed write and reload");
            for (int i = 0; i < 2; i++)
            {
                string state() => Get(cursor, "Revision") + "/" + Get(cursor, "Applied") + "\n" + string.Join(";", ((IEnumerable)Get(cursor, "undoRecords")).Cast<object>().Select(value => JsonUtility.ToJson(value)));
                File.WriteAllText(Path.Combine(root, "state-before-undo-" + i + ".json"), state());
                Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo");
                File.WriteAllText(Path.Combine(root, "state-after-undo-" + i + ".json"), state());
                File.WriteAllBytes(Path.Combine(root, "actual-undo-" + i + ".bin"), Probe(drawing, region));
                test.Equal((string)Get(step, "parent"), (string)Get(cursor, "Revision"), "Native Undo selects the stroke parent");
                Exact(test, before, Probe(drawing, region), "Exact Undo after reload");
                Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, expected, Probe(drawing, region), "Exact Redo after reload");
            }
            long retained = ReuseStat<long>("PaintHistoryStore", "PendingBytes");
            test.True(retained > 0, "Recovered page stays in memory while disk is unavailable");
            if (reloaded) Static("PaintTileTransfer", "DrainAll");
            test.Equal(retained, ReuseStat<long>("PaintHistoryStore", "PendingBytes"), "Repeated preparation does not duplicate retained pages");

            // Saving the document is independent of publishing its Undo journal.
            Call(document, "SyncDrawingLayerTextures");
            string path = Path.Combine(root, "saved.tiff");
            using (var container = new WhimTexDocumentContainer())
            {
                container.Set("document", (byte[])Static("WhimTexDocumentSerializer", "Serialize", document, container), System.IO.Compression.CompressionLevel.Fastest);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                Static("WhimTexTiffCarrier", "WriteTo", stream, container, Get(drawing, "StoredTexture"), (bool?)false, WhimTexOutputPrecision.Float32);
            }
            window.DiscardChanges(); UnityBRun.CloseOwned(window); window = null;
            test.True(document == null, "Actual close destroys the owned temporary document"); document = null;
            test.True(cursor == null, "Actual close destroys the source's history owner");
            Static("PaintHistoryStore", "RemoveUnusedHistories");
            test.Equal(0L, ReuseStat<long>("PaintHistoryStore", "PendingBytes"), "Closing the owner releases the failed pages");
            using (var container = (WhimTexDocumentContainer)Static("WhimTexTiffCarrier", "OpenContainer", path))
            {
                var decoded = Static("WhimTexDocumentSerializer", "Deserialize", container.Get("document"), container, typeof(WhimTexDocument), null, false);
                reopened = (WhimTexDocument)Get(decoded, "Model");
            }
            var restored = (DrawingLayerBehaviour)reopened.layers[0].Behaviour;
            Exact(test, expected, Probe(restored, region), "Saving and reopening after failure preserve exact unbounded masked HDR pixels");
            Exact(test, File.ReadAllBytes(Path.Combine(root, "original.bin")), ((Texture2D)Get(restored, "StoredTexture")).GetRawTextureData<byte>().ToArray(), "Saved original retains hidden RGB");
        }
        finally
        {
            if (document != null) PaintTileHistoryTests.ReleaseOwnedHistory(document);
            if (window != null) { window.DiscardChanges(); UnityBRun.CloseOwned(window); }
            if (reopened != null) Object.DestroyImmediate(reopened);
            foreach (var mask in Resources.FindObjectsOfTypeAll<Texture2D>()) if (mask.name == "Failed history " + id) Object.DestroyImmediate(mask);
            Static("PaintHistoryStore", "RemoveUnusedHistories");
            preferences.Dispose(); preferences.Verify();
        }
    });
}
