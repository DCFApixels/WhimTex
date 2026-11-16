using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Expected versions are captured from the rendered source before any Undo. The
// journal, CPU patching and restore path never generate their own expected bytes.
public static partial class TiledDrawingHistoryTests
{
    public static string RunReuseExisting(string scenario)
    {
        ReusePreflight(new TestContext());
        using var disk = new PaintTileHistoryTests.DiskScope();
        return scenario switch
        {
            "existing" => RunExisting(),
            "conversion" => RunStorageConversions(),
            "bridges" => RunBridges(),
            _ => throw new ArgumentException("Unknown existing history scenario.")
        };
    }

    public static string RunReuse8K(int diameter, bool inside)
    {
        if (diameter != 2048 && diameter != 4096) throw new ArgumentOutOfRangeException(nameof(diameter));
        ReusePreflight(new TestContext());
        using var disk = new PaintTileHistoryTests.DiskScope();
        return Run8KCore(8192, false, inside, diameter);
    }

    public static string RunReuseQueued() => TestContext.Run("Sparse Before reuse: queued masked HDR strokes, native Undo and branching", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using var f = new ReuseFixture();
        var baseline = f.Capture();
        var versions = new List<byte[]> { baseline };
        for (int i = 0; i < 3; i++)
        {
            var sample = f.Stroke(new Color(2.5f + i, -.4f - i * .1f, .7f, .35f + i * .15f));
            test.Equal(sample.tiles * 256L * 256 * 8, sample.reads, "Only After is read back for queued stroke " + i);
            versions.Add(f.Capture());
        }
        test.True(ReuseStat<int>("PaintTileTransfer", "PendingCount") > 0, "All strokes remain queued before native Undo");
        test.True(!ReuseEqual(versions[1], versions[3]), "Overlapping strokes produce different immutable expected versions");
        f.Undo(); Exact(test, versions[2], f.Capture(), "Immediate Undo while reuse transfers are pending");
        f.Undo(); Exact(test, versions[1], f.Capture(), "Second queued Undo");
        f.Redo(); Exact(test, versions[2], f.Capture(), "Redo after queued Undo");
        f.Stroke(new Color(-.5f, 4.5f, 1.5f, .8f));
        var branch = f.Capture();
        test.True(!ReuseEqual(versions[3], branch), "Replacement branch differs from discarded future");
        f.Undo(); Exact(test, versions[2], f.Capture(), "Branch Undo restores its actual parent");
        f.Redo(); Exact(test, branch, f.Capture(), "Branch Redo");
        Undo.PerformRedo(); Exact(test, branch, f.Capture(), "Discarded future cannot reappear");
        Exact(test, f.originalBytes, f.original.GetRawTextureData<byte>().ToArray(), "Hidden straight RGB and original HDR base remain exact");
        test.Equal(0L, ReuseStat<long>("PaintTileTransfer", "StagingBytes"), "Undo drains all owned staging");
    });

    public static string RunReuseLostDestinations() => TestContext.Run("Sparse Before reuse survives dead tile caches and native layer removal", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using (var f = new ReuseFixture())
        {
            f.Stroke(new Color(3.5f, -.5f, .7f, .55f)); var first = f.Capture();
            f.Stroke(new Color(-.4f, 4.5f, .25f, .65f)); var second = f.Capture();
            test.True(ReuseStat<int>("PaintTileTransfer", "PendingCount") > 0, "Repeated tile versions are still pending");
            // Drop only this fixture's disposable native backing. The journal and
            // runtime snapshots must still own everything needed for exact replay.
            foreach (var tile in (IEnumerable)Get(f.drawing, "TiledPixels"))
                Object.DestroyImmediate((Texture2D)Get(tile, "texture"));
            f.Undo(); Exact(test, first, f.Capture(), "Dead CPU cache reconstructs the previous tile version");
            f.Redo(); Exact(test, second, f.Capture(), "Dead CPU cache reconstructs the latest tile version");
        }
        using (var f = new ReuseFixture(true))
        {
            f.Stroke(new Color(3f, -.25f, .5f, .45f), false);
            f.Stroke(new Color(1.5f, 4f, -.5f, .7f), false);
            f.Stroke(new Color(5f, -.75f, .2f, .5f), false);
            var expected = f.Capture();
            test.True(ReuseStat<int>("PaintTileTransfer", "PendingCount") > 0, "Native creation Undo begins with reused versions queued");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(f.creationGroup); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); test.Equal(0, f.document.layers.Count, "Native Undo removes the newly created layer");
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            test.True((bool)Get(f.cursor, "HasLiveHistory"), "The native Redo record retains the owner while its anchor is destroyed");
            Static("PaintHistoryStore", "RemoveUnusedHistories");
            for (int i = 0; i < 2; i++)
            {
                Undo.PerformRedo(); test.Equal(1, f.document.layers.Count, "Native Redo recreates the layer");
                f.drawing = (DrawingLayerBehaviour)f.document.layers[0].Behaviour;
                Call(f.drawing, "ReconcilePixelUndo");
                Exact(test, expected, f.Capture(), "Native layer recreation preserves all queued versions " + i);
                if (i == 0) { Undo.PerformUndo(); test.Equal(0, f.document.layers.Count, "Repeated native removal"); }
            }
        }
    });

    public static string RunReuseFallback() => TestContext.Run("Sparse Before reuse falls back for unknown CPU revisions and retired history", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using var f = new ReuseFixture();
        f.Stroke(new Color(2, -.25f, .75f, .5f));
        Call(f.drawing, "FlushTiledStorage"); Static("PaintHistoryStore", "FlushWrites");
        // Imported/native storage can differ from the last paint revision. Model
        // it through the public texture API and the normal runtime invalidation.
        Call(f.drawing, "ReleaseTiledRuntime");
        var values = new Color[256 * 256];
        for (int i = 0; i < values.Length; i++) values[i] = new Color(4f + i % 9 * .02f, -.75f, .4f, i % 11 == 0 ? 0f : .4f);
        foreach (var tile in (IEnumerable)Get(f.drawing, "TiledPixels"))
        {
            var texture = (Texture2D)Get(tile, "texture"); texture.SetPixels(values); texture.Apply(false, false);
        }
        var imported = f.Capture();
        var unknown = f.Stroke(new Color(.3f, 5f, -.5f, .35f));
        test.Equal(unknown.tiles * 256L * 256 * 16, unknown.reads, "Unknown version keeps the complete GPU Before fallback");
        var first = f.Capture();
        var known = f.Stroke(new Color(5.5f, -.5f, .2f, .65f));
        test.Equal(known.tiles * 256L * 256 * 8, known.reads, "The next known version reuses Before again");
        var second = f.Capture();
        f.Undo(); Exact(test, first, f.Capture(), "Known version Undo after an unknown imported source");
        f.Undo(); Exact(test, imported, f.Capture(), "Unknown version Undo retains exact imported Half bytes");
        f.Redo(); f.Redo(); Exact(test, second, f.Capture(), "Both versions Redo");
        f.Stroke(new Color(3.2f, -.2f, 1.4f, .55f)); var beforeClear = f.Capture();
        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
        Call(f.cursor, "ClearHistory");
        f.Stroke(new Color(-.4f, 3.5f, 2.5f, .45f)); var afterClear = f.Capture();
        f.Undo(); Exact(test, beforeClear, f.Capture(), "Retired version is not used as a dangling Before reference");
        f.Redo(); Exact(test, afterClear, f.Capture(), "New Redo after history retirement");
        Call(f.drawing, "ReleaseTiledRuntime"); Exact(test, afterClear, f.Capture(), "Runtime rebuild reads exact synchronized CPU tiles");
    });

    public static string RunReuseRetry() => TestContext.Run("Sparse Before recovery retries a missing prior page without publishing partial bytes", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using var f = new ReuseFixture();
        f.Stroke(new Color(3.5f, -.4f, .7f, .6f)); var first = f.Capture();
        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
        var prior = Static("PaintHistoryStore", "Get", (string)Get(f.cursor, "Revision"));
        string path = (string)Static("PaintHistoryStore", "PagePath", prior, 0);
        string held = path + ".reuse-test-held";
        test.True(File.Exists(path) && !File.Exists(held), "The prior immutable page is fully published in the owned journal");
        f.Stroke(new Color(-.3f, 4.5f, 1.5f, .75f)); var second = f.Capture();
        test.True(ReuseStat<int>("PaintTileTransfer", "PendingCount") > 0, "The next version is queued before failure injection");
        // The document-wide Undo listener normally drains immediately. Temporarily
        // detach only this owned layer so the fixture can inject the I/O failure
        // between native cursor change and the explicit reconciliation below.
        var layer = f.document.layers[0]; f.document.layers.Clear();
        try { Undo.PerformUndo(); }
        finally { f.document.layers.Add(layer); }
        File.Move(path, held);
        Exception failure = null;
        try
        {
            try { Static("PaintTileTransfer", "Drain"); }
            catch (Exception error) { failure = error; while (failure.InnerException != null) failure = failure.InnerException; }
            test.True(failure is IOException, "Missing previous version rejects Before construction with an I/O error");
            test.True(ReuseStat<int>("PaintTileTransfer", "PendingCount") > 0, "Failed preparation retains the exact immutable After for retry");
        }
        finally { if (File.Exists(held)) File.Move(held, path); }
        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
        Call(f.drawing, "ReconcilePixelUndo");
        Exact(test, first, f.Capture(), "Retry reconstructed every Before byte after interrupted initialization");
        f.Redo(); Exact(test, second, f.Capture(), "Retry preserves exact Redo");
    });

    static T ReuseStat<T>(string type, string name) => (T)Product(type).GetProperty(name, S).GetValue(null);
    static bool ReuseEqual(byte[] a, byte[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static void ReusePreflight(TestContext test)
    {
        test.Equal(0, ReuseStat<int>("PaintTileTransfer", "PendingCount"), "Finish existing user transfers before running this fixture");
        test.Equal(0L, ReuseStat<long>("PaintHistoryStore", "PendingBytes"), "Finish existing user history writes first");
        test.True(!ReuseStat<bool>("PaintHistoryStore", "MaintenanceBusy"), "Finish checkpoint maintenance first");
        test.True(!(bool)Static("PaintHistoryStore", "HasActiveStroke"), "No active paint gesture");
        foreach (var history in Resources.FindObjectsOfTypeAll(Product("DrawingPixelHistory")))
            test.True(!(bool)Get(history, "HasLiveHistory") && string.IsNullOrEmpty((string)Get(history, "Revision")),
                "Existing user paint Undo/Redo is preserved; this fixture requires an idle journal");
    }

    sealed class ReuseFixture : IDisposable
    {
        internal readonly WhimTexDocument document;
        internal DrawingLayerBehaviour drawing;
        internal readonly Texture2D original;
        internal readonly byte[] originalBytes;
        internal Object cursor;
        internal readonly int creationGroup;
        readonly Texture2D mask;
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool previousSrgb = GL.sRGBWrite;
        readonly List<string> errors = new();
        readonly Application.LogCallback logger;
        internal ReuseFixture(bool nativeCreation = false)
        {
            logger = (text, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text); };
            Application.logMessageReceived += logger;
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 256;
            original = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var colors = new Color[256 * 256];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(1.5f + i % 13 * .02f, -.25f, .3f, i % 7 == 0 ? 0f : .55f);
            original.SetPixels(colors); original.Apply(false, false); originalBytes = original.GetRawTextureData<byte>().ToArray();
            if (nativeCreation)
            {
                UnityEditor.Undo.IncrementCurrentGroup(); creationGroup = UnityEditor.Undo.GetCurrentGroup();
                UnityEditor.Undo.RegisterCreatedObjectUndo(original, "Owned reuse layer creation");
                UnityEditor.Undo.RegisterCompleteObjectUndo(document, "Owned reuse layer creation");
            }
            drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            Call(drawing, "AdoptStoredTexture", original); document.layers.Add(drawing); Call(document, "NormalizeModel");
            mask = new Texture2D(256, 256, TextureFormat.R8, false, true)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var bytes = mask.GetRawTextureData<byte>();
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(40 + i % 256 * 215 / 255);
            mask.Apply(false, false);
        }
        internal byte[] Capture() => Probe(drawing, new RectInt(-256, 0, 768, 256));
        internal (long reads, int tiles) Stroke(Color color, bool ownGroup = true)
        {
            int group = -1;
            if (ownGroup) { UnityEditor.Undo.IncrementCurrentGroup(); group = UnityEditor.Undo.GetCurrentGroup(); }
            var settings = Settings(color);
            var parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
            parameters = Call(parameters, "WithSelectionMask", mask);
            long reads = ReuseStat<long>("PaintTileTransfer", "ReadbackBytes");
            var point = new Vector2(.06f, .5f);
            Call(drawing, "PrepareBufferedStroke", 256, 256, "Owned reuse stroke");
            Call(drawing, "BeginStroke", point); Call(drawing, "PaintPoint", point, 256, 256, parameters); Call(drawing, "EndStroke");
            cursor = (Object)Get(drawing, "PixelUndoObject");
            if (ownGroup) { UnityEditor.Undo.FlushUndoRecordObjects(); UnityEditor.Undo.CollapseUndoOperations(group); UnityEditor.Undo.IncrementCurrentGroup(); }
            return (ReuseStat<long>("PaintTileTransfer", "ReadbackBytes") - reads, (int)Get(drawing, "LastPaintTileCount"));
        }
        internal void Undo() { UnityEditor.Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); }
        internal void Redo() { UnityEditor.Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); }
        public void Dispose()
        {
            var cleanup = new List<Exception>();
            void Clean(Action action) { try { action(); } catch (Exception e) { cleanup.Add(e); } }
            Clean(() => Static("PaintTileTransfer", "Drain")); Clean(() => Static("PaintHistoryStore", "FlushWrites"));
            if (cursor == null && drawing != null) Clean(() => cursor = (Object)Get(drawing, "PixelUndoObject"));
            if (cursor != null) Clean(() => { UnityEditor.Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); });
            if (document != null) Clean(() => UnityEditor.Undo.ClearUndo(document));
            if (original != null) Clean(() => UnityEditor.Undo.ClearUndo(original));
            if (drawing != null) Clean(() => Call(drawing, "ReleaseTransientResources"));
            if (document != null) Clean(() => { document.layers.Clear(); Object.DestroyImmediate(document); });
            if (mask != null) Clean(() => Object.DestroyImmediate(mask));
            RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            Application.logMessageReceived -= logger;
            if (errors.Count != 0) cleanup.Add(new InvalidOperationException("Unexpected Unity errors: " + string.Join("\n", errors)));
            if (cleanup.Count != 0) throw new AggregateException("Owned reuse fixture cleanup failed", cleanup);
        }
    }
}
