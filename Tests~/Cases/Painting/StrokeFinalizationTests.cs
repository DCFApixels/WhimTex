using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    public static string RunStrokeBeginningOrder() => TestContext.Run("Stroke preparation closes old data before preparing new data", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        foreach (bool dense in new[] { false, true })
        {
            using var f = new ReuseFixture();
            var point = new Vector2(.3f, .4f);
            var parameters = Call(Call(Settings(new Color(-.2f, 4, .5f, .7f)), "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", Get(f, "mask"));
            Call(f.drawing, "PrepareBufferedStroke", 256, 256, "Old stroke"); Call(f.drawing, "BeginStroke", point);
            Call(f.drawing, "PaintPoint", point, 256, 256, parameters);
            var expected = StrokePreview(f.drawing);
            Call(f.drawing, dense ? "PrepareStroke" : "PrepareBufferedStroke", 256, 256, "New stroke");
            test.True(!(bool)Get(f.drawing, "HasBufferedStroke"), "Preparation completes the old pending operation");
            Exact(test, expected, f.Capture(), "Old HDR/masked data are committed before new preparation");
            test.Equal("New stroke", (string)Get(f.drawing, "pixelUndoLabel"), "New Undo label survives old completion");
            var surface = Get(f.drawing, "paintSurface");
            Call(f.drawing, "BeginStroke", point);
            test.Equal("New stroke", (string)Get(f.drawing, "pixelUndoLabel"), "Begin does not finish newly prepared data");
            if (dense) test.True((bool)Get(f.drawing, "tiledDenseEdit") && ReferenceEquals(surface, Get(f.drawing, "paintSurface")), "Dense preparation remains usable during begin");
            Call(f.drawing, "EndStroke");
            test.True(!(bool)Get(f.drawing, "tiledDenseEdit"), "Untouched dense operation is released on completion");
            test.True(Get(f.drawing, "pixelUndoLabel") == null, "Completed empty operation releases its Undo label");
            Exact(test, expected, f.Capture(), "Empty next operation leaves old pixels unchanged");
        }
    });

    public static string RunStrokeDocumentSwitch() => TestContext.Run("Document replacement commits then releases only the old document", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using var f = new ReuseFixture();
        var preferences = new UnityBToolPreferences();
        var next = ScriptableObject.CreateInstance<WhimTexDocument>(); next.hideFlags = HideFlags.HideAndDontSave;
        WhimTexWindow window = null;
        var changed = typeof(WhimTexDocument).GetEvent("Changed", S);
        byte[] committed = null;
        Action<WhimTexDocument> observer = doc => { if (ReferenceEquals(doc, f.document)) committed = f.Capture(); };
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>(); preferences.Own(window);
            Call(window, "SetDocument", f.document); Call(window, "SelectOnlyLayer", f.drawing.Id);
            Set(window, "canvasTool", Enum.Parse(Get(window, "canvasTool").GetType(), "Brush"));
            var point = new Vector2(.06f, .5f);
            var parameters = Call(Call(Settings(new Color(-.4f, 4f, .7f, .65f)), "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", Get(f, "mask"));
            Call(f.drawing, "PrepareBufferedStroke", 256, 256, "Owned switch stroke"); Call(f.drawing, "BeginStroke", point);
            Call(f.drawing, "PaintPoint", point, 256, 256, parameters);
            var expected = StrokePreview(f.drawing); Set(window, "paintingLayer", f.drawing);
            changed.GetAddMethod(true).Invoke(null, new object[] { observer });
            Call(window, "SetDocument", next);
            test.True(committed != null, "Old document publishes the finished stroke before disposal");
            Exact(test, expected, committed, "Document switch commits exact HDR/masked pixels first");
            test.True(f.document == null && f.original == null, "Old temporary document and owned pixels are released");
            test.True(Get(f.drawing, "tiledSurface") == null && Get(window, "paintingLayer") == null, "No old GPU surface or gesture remains");
            test.True(ReferenceEquals(next, Get(window, "activeDocument")), "Replacement document is active");
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            test.Equal(0L, ReuseStat<long>("PaintTileTransfer", "StagingBytes"), "Old readback staging is released");
        }
        finally
        {
            changed.GetRemoveMethod(true).Invoke(null, new object[] { observer });
            if (window != null) { Set(window, "activeDocument", null); window.DiscardChanges(); Object.DestroyImmediate(window); }
            Object.DestroyImmediate(next); preferences.Dispose(); preferences.Verify();
        }
    });

    public static string BeginStrokeReload(string id) => TestContext.Run("Arm real reload with a masked HDR stroke", test =>
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Owned run GUID required");
        ReusePreflight(test);
        var preferences = new UnityBToolPreferences();
        var f = new ReuseFixture();
        WhimTexWindow window = null;
        bool armed = false;
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>(); preferences.Own(window);
            window.name = "Stroke reload " + id; f.document.name = window.name;
            ((Texture2D)Get(f, "mask")).name = window.name;
            Call(window, "SetDocument", f.document); Call(window, "SelectOnlyLayer", f.drawing.Id);
            Set(window, "canvasTool", Enum.Parse(Get(window, "canvasTool").GetType(), "Brush"));
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/stroke-reload", id));
            if (Directory.Exists(root)) throw new IOException("Use a fresh owned reload GUID.");
            Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "before.bin"), f.Capture());
            Undo.IncrementCurrentGroup();
            var point = new Vector2(.06f, .5f);
            var parameters = Call(Call(Settings(new Color(-.4f, 4f, .7f, .65f)), "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", Get(f, "mask"));
            Call(f.drawing, "PrepareBufferedStroke", 256, 256, "Owned reload stroke"); Call(f.drawing, "BeginStroke", point);
            Call(f.drawing, "PaintPoint", point, 256, 256, parameters);
            File.WriteAllBytes(Path.Combine(root, "expected.bin"), StrokePreview(f.drawing));
            Set(window, "paintingLayer", f.drawing); Set(window, "paintingPointerId", 7);
            window.ShowUtility();
            AppDomain.CurrentDomain.SetData("StrokeReload." + id, true);
            test.True((bool)Get(f.drawing, "HasBufferedStroke"), "Stroke is still pending before actual recompile");
            armed = true;
        }
        finally
        {
            // No disposable test callback may cross the domain boundary.
            Application.logMessageReceived -= (Application.LogCallback)Get(f, "logger");
            if (!armed) { if (window != null) { Set(window, "activeDocument", null); Object.DestroyImmediate(window); } f.Dispose(); }
            preferences.Dispose(); preferences.Verify();
        }
    });

    public static string VerifyStrokeReload(string id) => TestContext.Run("Real reload retains exact pixels and native Undo/Redo", test =>
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Owned run GUID required");
        var preferences = new UnityBToolPreferences();
        WhimTexWindow window = null; WhimTexDocument document = null;
        try
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexWindow>()) if (candidate.name == "Stroke reload " + id) window = candidate;
            test.True(window != null, "The owned window survived the actual reload");
            preferences.Own(window); document = (WhimTexDocument)Get(window, "activeDocument");
            test.True(AppDomain.CurrentDomain.GetData("StrokeReload." + id) == null, "Actual managed domain changed");
            var drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/stroke-reload", id));
            var expected = File.ReadAllBytes(Path.Combine(root, "expected.bin"));
            var before = File.ReadAllBytes(Path.Combine(root, "before.bin"));
            var region = new RectInt(-256, 0, 768, 256);
            Exact(test, expected, Probe(drawing, region), "Signed/HDR/masked pixels survive actual reload");
            test.True(!(bool)Get(drawing, "HasBufferedStroke") && Get(window, "paintingLayer") == null, "No stale gesture after reload");
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, before, Probe(drawing, region), "Undo after reload");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, expected, Probe(drawing, region), "Redo after reload");
            test.Equal(0, ReuseStat<int>("PaintTileTransfer", "PendingCount"), "Reload drained transfers");
            test.Equal(0L, ReuseStat<long>("PaintHistoryStore", "PendingBytes"), "Reload completed journal writers");
        }
        finally
        {
            if (document != null) PaintTileHistoryTests.ReleaseOwnedHistory(document);
            if (window != null) { window.DiscardChanges(); UnityBRun.CloseOwned(window); }
            foreach (var mask in Resources.FindObjectsOfTypeAll<Texture2D>()) if (mask.name == "Stroke reload " + id) Object.DestroyImmediate(mask);
            preferences.Dispose(); preferences.Verify();
        }
    });

    public static string RunStrokeFinalization(string scenario) => TestContext.Run("Stroke finalization: " + scenario, test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using var f = new ReuseFixture();
        var before = f.Capture();
        byte[] retained = null;
        if (scenario == "native-undo") { f.Stroke(new Color(2, -.2f, .4f, .7f)); retained = f.Capture(); }
        var preferences = new UnityBToolPreferences();
        WhimTexWindow window = null;
        try
        {
            if (scenario != "cancel" && scenario != "disk" && scenario != "quota" && scenario != "detach" && scenario != "document-disable")
            {
                window = ScriptableObject.CreateInstance<WhimTexWindow>(); preferences.Own(window);
                Call(window, "SetDocument", f.document);
                Call(window, "SelectOnlyLayer", f.drawing.Id);
                Set(window, "canvasTool", Enum.Parse(Get(window, "canvasTool").GetType(), "Brush"));
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            var parameters = Call(Settings(new Color(-.4f, 4f, .7f, .65f)), "GetStrokeParameters", false, (Color?)null);
            parameters = Call(parameters, "WithSelectionMask", Get(f, "mask"));
            var point = new Vector2(.06f, .5f);
            Call(f.drawing, "PrepareBufferedStroke", 256, 256, "Owned lifecycle stroke");
            Call(f.drawing, "BeginStroke", point);
            Call(f.drawing, "PaintPoint", point, 256, 256, parameters);
            var expected = StrokePreview(f.drawing);
            test.True(!ReuseEqual(before, expected), "Pending masked signed-HDR stroke is visible outside Transform");
            Set(f.drawing, "clipStrokeToInitialShape", true);
            Set(f.drawing, "strokeWrapCanvas", true);
            if (window != null)
            {
                Set(window, "paintingLayer", f.drawing); Set(window, "paintingPointerId", 7);
                Set(window, "paintingMouseButton", 0); Set(window, "paintingErase", true);
                Set(window, "paintingSmootherActive", true); Set(window, "paintingPointerMoved", true);
                Set(window, "paintingGuideIndex", 3);
            }
            switch (scenario)
            {
                case "cancel": Call(f.drawing, "CancelStroke"); break;
                case "detach": Call(f.drawing, "ReleasePaintResources"); break;
                case "document-disable": Call(f.document, "OnDisable"); break;
                case "quota":
                    Call(disk.disk, "SetLimits", (int?)1, (int?)50);
                    var peer = Activator.CreateInstance(Product("PaintUndoDisk"), F, null, new object[] { disk.home, Guid.NewGuid().ToString("N") }, null);
                    try
                    {
                        var reservation = new object[] { Path.Combine((string)Get(peer, "Root"), "occupied"), 990L * 1024 * 1024, null, false };
                        test.True((bool)Call(peer, "Reserve", reservation), "Owned peer reserves the quota without writing gigabytes");
                        bool rejected = false;
                        try { Call(f.drawing, "EndStroke"); }
                        catch (TargetInvocationException error) when (error.InnerException is IOException) { rejected = true; }
                        test.True(rejected, "Disk quota refusal rolls back the pending stroke");
                    }
                    finally { Call(peer, "Close"); }
                    break;
                case "disk":
                    // Reject a reservation deterministically without filling the physical disk.
                    string root = (string)Get(disk.disk, "Root");
                    string state = Path.Combine(root, "steps");
                    Directory.CreateDirectory(state);
                    string held = Path.Combine(root, "held-steps");
                    Directory.Move(state, held); File.WriteAllText(state, "owned I/O failure");
                    bool refused = false;
                    try { Call(f.drawing, "EndStroke"); }
                    catch (TargetInvocationException error) when (error.InnerException is IOException) { refused = true; }
                    finally { File.Delete(state); Directory.Move(held, state); }
                    test.True(refused, "History reservation failure reaches the caller");
                    break;
                case "memory":
                    var pool = Get(Get(f.drawing, "tiledSurface"), "pool");
                    long budget = (long)Get(pool, "budget");
                    Set(pool, "budget", (long)Get(pool, "AllocatedBytes"));
                    Set(f.drawing, "clipStrokeToInitialShape", false); Set(f.drawing, "strokeWrapCanvas", false);
                    Set(window, "lastPaintingUv", point); Set(window, "hasLastPaintingUv", true);
                    bool limited = false;
                    try { Call(window, "PaintTowardsLayerPoint", new Vector2(10, .5f)); }
                    catch (TargetInvocationException error) when (error.InnerException is DCFApixels.WhimTex.TiledPaint.TiledPaintMemoryLimitException) { limited = true; }
                    finally { Set(pool, "budget", budget); }
                    test.True(limited, "GPU page limit interrupts input without allocating above the limit");
                    break;
                case "undo": Call(window, "OnUndoRedo"); break;
                case "native-undo": Undo.PerformUndo(); break;
                case "layer": Call(window, "SelectOnlyLayer", (object)null); break;
                case "tool": Call(window, "ChangeCanvasTool", Enum.Parse(Get(window, "canvasTool").GetType(), "Transform")); break;
                case "close": Call(window, "OnDisable"); break;
                case "reload": Static("PaintTileTransfer", "DrainAll"); break;
                default: throw new ArgumentException(scenario);
            }
            bool cancelled = scenario == "cancel" || scenario == "disk" || scenario == "quota" || scenario == "undo" || scenario == "native-undo" || scenario == "memory";
            Exact(test, cancelled ? before : expected, f.Capture(), "Transition keeps exact Half pixels");
            test.True(!(bool)Get(f.drawing, "HasBufferedStroke"), "No buffered stroke survives");
            test.True(!(bool)Get(f.drawing, "clipStrokeToInitialShape") && !(bool)Get(f.drawing, "strokeWrapCanvas"), "Stroke constraints are released");
            if (window != null && scenario != "reload")
            {
                test.True(Get(window, "paintingLayer") == null, "Window releases the layer");
                test.Equal(-1, (int)Get(window, "paintingPointerId"), "Pointer is released");
                test.Equal(-1, (int)Get(window, "paintingMouseButton"), "Mouse button is reset");
                test.True(!(bool)Get(window, "paintingErase") && !(bool)Get(window, "paintingSmootherActive"), "Erase and smoother state are reset");
                test.Equal(-1, (int)Get(window, "paintingGuideIndex"), "Guide is released");
            }
            f.cursor = (Object)Get(f.drawing, "PixelUndoObject");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            if (!cancelled)
            {
                Call(f.drawing, "EndStroke"); Call(f.drawing, "EndStroke");
                f.Undo(); Exact(test, before, f.Capture(), "Transition creates exactly one Undo step");
                f.Redo(); Exact(test, expected, f.Capture(), "Redo restores exact masked HDR pixels");
            }
            if (retained != null) { f.Redo(); Exact(test, retained, f.Capture(), "Native Redo restores only the committed stroke"); before = retained; }
            f.Stroke(new Color(3, -.5f, .2f, .7f)); var next = f.Capture();
            f.Undo(); Exact(test, cancelled ? before : expected, f.Capture(), "Next stroke Undo after transition");
            f.Redo(); Exact(test, next, f.Capture(), "Next stroke Redo after transition");
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            test.Equal(0, ReuseStat<int>("PaintTileTransfer", "PendingCount"), "Transfer queue is drained");
            test.Equal(0L, ReuseStat<long>("PaintTileTransfer", "StagingBytes"), "Staging is released");
        }
        finally
        {
            if (window != null)
            {
                Set(window, "paintingLayer", null); Set(window, "activeDocument", null);
                window.DiscardChanges(); Object.DestroyImmediate(window);
            }
            preferences.Dispose(); preferences.Verify();
        }
    });

    static byte[] StrokePreview(DrawingLayerBehaviour drawing)
    {
        var previous = RenderTexture.active;
        var target = new RenderTexture(768, 256, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var read = new Texture2D(768, 256, TextureFormat.RGBAHalf, false, true);
        try
        {
            target.Create();
            ((DCFApixels.WhimTex.TiledPaint.TiledPaintSurface)Get(drawing, "tiledSurface")).CopyRegion(new RectInt(-256, 0, 768, 256), target, true);
            RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 768, 256), 0, 0, false);
            return read.GetRawTextureData<byte>().ToArray();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(target); Object.DestroyImmediate(read); }
    }
}
