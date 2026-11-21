using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class DrawingGpuRecoveryTests
{
    const int Side = 256;
    const int ProtectedWidth = 128;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static string Run(string scenario = "all") => TestContext.Run("Drawing GPU recovery: " + scenario, test =>
    {
        test.Equal(0, PendingTransfers(), "Editor begins without pending paint transfers");
        test.True(!(bool)Call(Product("PaintHistoryStore"), "HasActiveStroke"), "Editor begins without an active paint gesture");
        using var disk = new PaintTileHistoryTests.DiskScope();
        switch (scenario)
        {
            case "all":
                Dense(test);
                Dense(test, destroySurface: true);
                Tiled(test);
                TiledActive(test);
                SmudgeProtection(test);
                MixedPrecision(test);
                Staging(test);
                StagingIsolation(test);
                break;
            case "dense": Dense(test); break;
            case "dense-destroy": Dense(test, destroySurface: true); break;
            case "tiled": Tiled(test); break;
            case "tiled-active": TiledActive(test); break;
            case "smudge-protection": SmudgeProtection(test); break;
            case "mixed-precision": MixedPrecision(test); break;
            case "staging": Staging(test); break;
            case "staging-isolation": StagingIsolation(test); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        test.Equal(0, PendingTransfers(), "Owned paint transfers are released");
    });

    static void Dense(TestContext test, bool destroySurface = false)
    {
        using var f = new Fixture(false);
        var before = f.Capture();
        var beforeSource = f.CaptureSource();
        f.Smudge(.32f);
        var live = f.Capture();
        AssertMaskedEdit(test, before, live, "Dense Smudge");
        f.Commit(flush: true);
        var committedSource = f.CaptureSource();
        Exact(test, live, f.Capture(), "Dense commit matches its live canvas");

        if (destroySurface)
        {
            var surface = f.WorkingTexture;
            test.True(!(bool)Get(f.Drawing, "HasTiledSource") && surface != null && surface.IsCreated(),
                "Committed HDR stroke has an ordinary Drawing surface");
            Object.DestroyImmediate(surface);
            test.True(surface == null && !ReferenceEquals(surface, null), "Destroyed native object leaves a managed wrapper");
            var first = f.Capture();
            var second = f.Capture();
            Exact(test, live, first, "First rendered frame after DestroyImmediate matches the committed canvas");
            Exact(test, live, second, "Second rendered frame after DestroyImmediate matches the committed canvas");
            Exact(test, committedSource, f.CaptureSource(), "Recreated surface keeps every masked HDR Half component");
            UndoRedo(test, f, before, live, "Destroyed dense surface", beforeSource, committedSource);
            return;
        }

        Lose(test, f.WorkingTexture, "Committed dense surface");
        Exact(test, live, f.Capture(), "Dense canvas rebuilds from the committed image");
        Exact(test, committedSource, f.CaptureSource(), "Dense reconstruction keeps every working Half bit");

        f.Smudge(.75f);
        AssertMaskedEdit(test, live, f.Capture(), "Uncommitted dense Smudge");
        Lose(test, f.WorkingTexture, "Dense Smudge working surface");
        Exact(test, live, f.Capture(), "Dense Smudge loss rolls back to the last committed image");
        FinishCancelled(test, f, live, "Dense Smudge");
        Lose(test, f.WorkingTexture, "Dense surface before native Undo");
        UndoRedo(test, f, before, live, "Dense", beforeSource, committedSource);
    }

    static void MixedPrecision(TestContext test)
    {
        // Each dense Smudge affects a different tile; only the second tile later gets a sparse override.
        using var f = new Fixture(false, standard: true, height: Side * 2);
        var originalCanvas = f.Capture();
        var original = f.CaptureSource();
        f.Smudge(.25f);
        f.Commit(flush: true);
        var firstDense = f.CaptureSource();
        var firstCanvas = f.Capture();
        test.Equal(TextureFormat.RGBA32, ((Texture2D)Get(f.Drawing, "StoredTexture")).format, "Standard source uses 8-bit storage");
        CheckEdit(original, firstDense, 0, "First dense Smudge");
        f.Smudge(.75f);
        f.Commit(flush: true);
        var dense = f.CaptureSource();
        CheckEdit(firstDense, dense, 1, "Second dense Smudge");

        Call(f.Drawing, "PrepareBufferedStroke", Side, Side * 2, "Owned mixed-storage transition");
        Call(f.Drawing, "CancelStroke");
        Exact(test, dense, f.CaptureSource(), "Initial tiled base preserves every dense Half component");
        var migratedCanvas = f.Capture();
        CheckLoss(migratedCanvas, dense, "Dense base without sparse writes");

        f.Paint(false, .75f);
        f.Commit(flush: true);
        var brush = f.CaptureSource();
        var brushCanvas = f.Capture();
        CheckEdit(dense, brush, 1, "Sparse Brush");
        CheckLoss(brushCanvas, brush, "Dense base with a newer sparse tile");

        Undo.PerformUndo();
        Exact(test, migratedCanvas, f.Capture(), "Undo sparse Brush restores Smudge canvas");
        Exact(test, dense, f.CaptureSource(), "Undo sparse Brush restores exact dense base");
        Lose(test, FirstSourcePage(f.Surface), "GPU page immediately before undoing the second dense Smudge");
        Undo.PerformUndo();
        Exact(test, firstCanvas, f.Capture(), "First frame after dense Undo preserves the other tile");
        Exact(test, firstDense, f.CaptureSource(), "Undo after loss restores untouched and changed Half pixels");
        Undo.PerformUndo();
        Exact(test, originalCanvas, f.Capture(), "Undo dense Smudge restores original canvas");
        Exact(test, original, f.CaptureSource(), "Undo dense Smudge restores original Half pixels");
        Undo.PerformRedo();
        Exact(test, firstCanvas, f.Capture(), "Redo first dense Smudge restores canvas");
        Exact(test, firstDense, f.CaptureSource(), "Redo first dense Smudge restores Half precision");
        Undo.PerformRedo();
        Exact(test, migratedCanvas, f.Capture(), "Redo dense Smudge restores canvas");
        Exact(test, dense, f.CaptureSource(), "Redo dense Smudge restores Half precision");
        Undo.PerformRedo();
        Exact(test, brushCanvas, f.Capture(), "Redo sparse Brush restores canvas");
        Exact(test, brush, f.CaptureSource(), "Redo sparse Brush restores both tiles");
        CheckLoss(brushCanvas, brush, "Rebuilt GPU state after native Redo");

        void CheckLoss(ushort[] canvas, ushort[] source, string label)
        {
            Lose(test, FirstSourcePage(f.Surface), label);
            Exact(test, canvas, f.Capture(), label + " canvas is bit-exact after GPU loss");
            Exact(test, source, f.CaptureSource(), label + " Half source is bit-exact after GPU loss");
        }
        void CheckEdit(ushort[] before, ushort[] after, int tileY, string label)
        {
            int soft = 0, full = 0;
            for (int i = 0; i < before.Length / 4; i++)
            {
                if (before.AsSpan(i * 4, 4).SequenceEqual(after.AsSpan(i * 4, 4))) continue;
                int x = i % Side;
                if (i / (Side * Side) != tileY || x < ProtectedWidth)
                    throw new InvalidOperationException(label + " changed protected pixels or the other tile");
                if (x < 160) soft++; else full++;
            }
            test.True(soft > 0 && full > 0, label + " writes pixels under both soft and full coverage");
        }
    }

    static void Tiled(TestContext test)
    {
        using var f = new Fixture(true);
        var before = f.Capture();
        var beforeSource = f.CaptureSource();
        f.Paint(false, .35f);
        var live = f.Capture();
        AssertMaskedEdit(test, before, live, "Tiled Brush");
        f.Commit(flush: false);
        var committedSource = f.CaptureSource();
        test.True(PendingTransfers() > 0, "Committed tiles still have an owned transfer queued");

        Lose(test, FirstSourcePage(f.Surface), "Committed tile page with readback queued");
        Exact(test, live, f.Capture(), "Tiled canvas recovers with the newest committed image");
        test.Equal(0, PendingTransfers(), "Recovery drains authoritative backing before rebuilding tiles");
        Exact(test, committedSource, f.CaptureSource(), "Tiled reconstruction keeps every working Half bit");

        Lose(test, FirstSourcePage(f.Surface), "Committed tile page with settled backing");
        Exact(test, live, f.Capture(), "A later loss can rebuild again from settled backing");
        Lose(test, FirstSourcePage(f.Surface), "Tile page before native Undo");
        UndoRedo(test, f, before, live, "Tiled", beforeSource, committedSource);
    }

    static void TiledActive(TestContext test)
    {
        using var f = new Fixture(true);
        var before = f.Capture();
        f.Paint(false, .3f);
        var live = f.Capture();
        f.Commit(flush: true);

        foreach (bool pencil in new[] { false, true })
        {
            var tool = pencil ? "Pencil" : "Brush";
            f.Paint(pencil, .75f);
            AssertMaskedEdit(test, live, f.Capture(), "Uncommitted tiled " + tool);
            Lose(test, f.Surface.PendingTiles.First().Texture, "Pending " + tool + " page");
            Exact(test, live, f.Capture(), tool + " loss rolls back the pending ink");
            FinishCancelled(test, f, live, tool);
        }

        f.Smudge(.75f);
        AssertMaskedEdit(test, live, f.Capture(), "Uncommitted tiled Smudge");
        Lose(test, f.WorkingTexture, "Tiled Smudge dense working surface");
        Exact(test, live, f.Capture(), "Smudge working surface loss keeps committed tiles");
        FinishCancelled(test, f, live, "Tiled Smudge");

        f.Blur(.75f);
        AssertMaskedEdit(test, live, f.Capture(), "Uncommitted tiled Blur");
        var originalPage = ((TiledPaintSnapshot)Get(f.Drawing, "regionBefore")).Tiles.First().Texture;
        var previewPage = FirstSourcePage(f.Surface);
        Lose(test, previewPage, "Published Blur region page");
        if (originalPage != previewPage) Lose(test, originalPage, "Pinned Blur input page");
        Exact(test, live, f.Capture(), "Blur rolls back from backing when the pinned input is also lost");
        FinishCancelled(test, f, live, "Tiled Blur");

        f.Paint(false, .75f);
        AssertMaskedEdit(test, live, f.Capture(), "Uncommitted Brush before release");
        Lose(test, f.Surface.PendingTiles.First().Texture, "Pending Brush page before release");
        bool reportedLoss = false;
        try { Call(f.Drawing, "EndStroke"); }
        catch (TiledPaintResourceLostException) { reportedLoss = true; }
        test.True(reportedLoss, "Ending a lost Brush directly reports the GPU loss once");
        Exact(test, live, f.Capture(), "Direct release after GPU loss restores the committed image");
        FinishCancelled(test, f, live, "Direct Brush release");

        UndoRedo(test, f, before, live, "Tiled active loss");
        f.Paint(true, .75f);
        var next = f.Capture();
        AssertMaskedEdit(test, live, next, "New Pencil stroke after recovery");
        f.Commit(flush: true);
        Exact(test, next, f.Capture(), "Recovered layer can commit another exact masked stroke");
        UndoRedo(test, f, live, next, "New Pencil stroke");
    }

    static void SmudgeProtection(TestContext test)
    {
        foreach (bool tiled in new[] { false, true })
        {
            using var f = new Fixture(tiled);
            var initial = f.Capture();
            var initialSource = f.CaptureSource();
            f.Smudge(.32f, 7);
            var committed = f.Capture();
            AssertMaskedEdit(test, initial, committed, "Smudge with protected alpha");
            for (int i = 3; i < initial.Length; i += 4)
                if (initial[i] != committed[i]) throw new InvalidOperationException("Smudge changed protected alpha before GPU loss");
            f.Commit(flush: true);
            var committedSource = f.CaptureSource();
            foreach (string observer in new[] { "continue", "preview", "release", "cancel" })
            {
                string label = (tiled ? "Tiled" : "Dense") + " Smudge snapshot loss on " + observer;
                f.Smudge(.75f, 7);
                AssertMaskedEdit(test, committed, f.Capture(), label + " live stroke");
                ulong version = (ulong)Get(f.Drawing, "PaintGpuRecoveryVersion");
                Lose(test, (RenderTexture)Get(f.Drawing, "paintWriteBefore"), label);
                bool reported = false;
                try
                {
                    switch (observer)
                    {
                        case "continue": f.SmudgeSegment(.8f, .9f, .75f); break;
                        case "preview": Exact(test, committed, f.Capture(), label); break;
                        case "release": Call(f.Drawing, "EndStroke"); break;
                        case "cancel": Call(f.Drawing, "CancelStroke"); break;
                    }
                }
                catch (TiledPaintResourceLostException) { reported = true; }
                test.Equal(observer == "continue" || observer == "release", reported, label + " caller notification");
                test.Equal(version + 1, (ulong)Get(f.Drawing, "PaintGpuRecoveryVersion"), label + " recovered exactly once");
                FinishCancelled(test, f, committed, label);
                Exact(test, committedSource, f.CaptureSource(), label + " restores every Half component");
            }
            UndoRedo(test, f, initial, committed, "Committed Smudge after interrupted strokes", initialSource, committedSource);
        }
    }

    static void FinishCancelled(TestContext test, Fixture f, ushort[] committed, string label)
    {
        test.True(!(bool)Get(f.Drawing, "HasBufferedStroke"), label + " has no buffered gesture after recovery");
        Call(f.Drawing, "EndStroke");
        Exact(test, committed, f.Capture(), label + " release cannot commit discarded pixels");
        test.Equal(0, PendingTransfers(), label + " release does not queue history for discarded pixels");
    }

    static void Staging(TestContext test)
    {
        var staging = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave };
        using var destination = new NativeArray<byte>(16 * 16 * 8, Allocator.Temp);
        try
        {
            staging.Create();
            Lose(test, staging, "Owned history staging surface");
            var transfer = Product("PaintTileTransfer");
            var page = transfer.GetNestedType("Page", BindingFlags.NonPublic);
            var lost = transfer.GetNestedType("StagingLostException", BindingFlags.NonPublic);
            bool reportedLoss = false;
            try { Call(page, "Read", staging, false, destination); }
            catch (Exception error) when (error.GetType() == lost) { reportedLoss = true; }
            test.True(reportedLoss, "Reading released staging reports lost history pixels");
            test.True(!staging.IsCreated(), "The failed read does not re-create empty staging");
        }
        finally { Object.DestroyImmediate(staging); }
    }

    static void UndoRedo(TestContext test, Fixture f, ushort[] before, ushort[] after, string label,
        ushort[] beforeSource = null, ushort[] afterSource = null)
    {
        Undo.PerformUndo();
        Exact(test, before, f.Capture(), label + " native Undo remains exact");
        if (beforeSource != null) Exact(test, beforeSource, f.CaptureSource(), label + " Undo keeps every working Half bit");
        Undo.PerformRedo();
        Exact(test, after, f.Capture(), label + " native Redo remains exact");
        if (afterSource != null) Exact(test, afterSource, f.CaptureSource(), label + " Redo keeps every working Half bit");
    }

    static void StagingIsolation(TestContext test)
    {
        CheckStagingIsolation(test, background: false);
        CheckStagingIsolation(test, background: true);
    }

    static void CheckStagingIsolation(TestContext test, bool background)
    {
        using var a = new Fixture(true);
        using var b = new Fixture(true);
        var bBefore = b.Capture();
        var bSourceBefore = b.CaptureSource();

        // These three commits run on one editor tick. At most two pages can submit readbacks.
        foreach (float y in new[] { .2f, .5f, .8f })
        {
            a.Paint(false, y);
            a.Commit(flush: false);
        }
        var aHistory = Get(a.Drawing, "PixelUndoObject");
        var transfer = Product("PaintTileTransfer");
        var store = Product("PaintHistoryStore");
        var queue = (System.Collections.IEnumerable)transfer.GetField("pending", F).GetValue(null);
        var page = queue.Cast<object>().Single(p => (string)Get(Get(p, "step"), "id") == (string)Get(aHistory, "Revision"));
        test.True(!(bool)Get(page, "readbackStarted"), "A's third real Undo page is still waiting for its first CPU read");
        var step = Get(page, "step");
        test.Equal((string)Get(aHistory, "Identity"), (string)Get(step, "owner"), "The lost page belongs to A");
        string aRevision = (string)Get(step, "id");
        string reservation = Path.Combine((string)Call(store, "StepDirectory", aRevision), "reservation");
        test.True(File.Exists(reservation), "A's queued step starts with a disk reservation");
        try
        {
            Lose(test, (RenderTexture)Get(page, "after"), "A's queued staging page");
            if (background) PumpUpdates();
            b.Paint(false, .6f);
            var bLive = b.Capture();
            AssertMaskedEdit(test, bBefore, bLive, "B while A's history page is lost");
            b.Commit(flush: false);
            var bSourceAfter = b.CaptureSource();
            if (background) PumpUpdates();
            Call(b.Drawing, "SyncPendingSurfaceToTexture");
            Call(store, "FlushWrites");
            int durableParents = 0;
            for (string id = (string)Get(step, "parent"); id.Length != 0;)
            {
                var older = Call(store, "Get", id);
                Call(store, "Validate", older);
                test.True(File.Exists((string)Call(store, "PagePath", older, 0)), "A's earlier healthy page is durable");
                durableParents++;
                id = (string)Get(older, "parent");
            }
            test.Equal(2, durableParents, "Both earlier strokes in A remain readable from disk");
            test.True(!File.Exists(reservation), "The incomplete step relinquishes its worst-case disk reservation");
            test.Equal(0, PendingTransfers(), "A's failed page does not strand B in the transfer queue");
            test.Equal(0L, (long)transfer.GetProperty("StagingBytes", F).GetValue(null), "The queue releases its full staging budget");
            var bHistory = Get(b.Drawing, "PixelUndoObject");
            test.True(!string.IsNullOrEmpty((string)Get(bHistory, "Revision")), "B published its own Undo step");
            Call(b.Drawing, "ReleaseTiledRuntime");
            Exact(test, bLive, b.Capture(), "B rebuilds the exact committed image from its persisted tiles");
            Exact(test, bSourceAfter, b.CaptureSource(), "B persists every signed HDR/alpha Half component");
            bool reported = false;
            try { Call(a.Drawing, "SyncPendingSurfaceToTexture"); }
            catch (Exception e) when (e.GetType() == Product("PaintTileTransfer").GetNestedType("StagingLostException", F)) { reported = true; }
            test.True(reported, "A explicitly reports its lost history at synchronization");
            var aRecord = ((System.Collections.IEnumerable)Get(aHistory, "undoRecords")).Cast<object>()
                .Single(record => (string)Get(record, "after") == aRevision);
            Undo.PerformUndo();
            Exact(test, bBefore, b.Capture(), "Undo B after loss in A");
            Exact(test, bSourceBefore, b.CaptureSource(), "Undo B restores all Half components");
            Undo.PerformUndo();
            test.True(!(bool)Get(aRecord, "active"), "Native Undo crosses A's damaged step");
            Exact(test, bBefore, b.Capture(), "Undoing A leaves B untouched");
            Undo.PerformRedo();
            test.True((bool)Get(aRecord, "active"), "Native Redo crosses A's damaged step");
            Exact(test, bBefore, b.Capture(), "Redoing A leaves B untouched");
            Undo.PerformRedo();
            Exact(test, bLive, b.Capture(), "Redo B after crossing A's damaged step");
            Exact(test, bSourceAfter, b.CaptureSource(), "Redo B restores all Half components");
        }
        finally
        {
            // On the unfixed implementation Drain retries this missing page even during teardown.
            // Cancel only the owned, irrecoverable revision so teardown can free its native resources.
            step.GetType().GetField("cancelled", F).SetValue(step, true);
            Flush();
            PaintTileHistoryTests.ReleaseOwnedHistory(a.Drawing);
        }

        void PumpUpdates()
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (PendingTransfers() > 0 && timeout.ElapsedMilliseconds < 5000)
            {
                Call(transfer, "Update");
                if (PendingTransfers() > 0) System.Threading.Thread.Sleep(1);
            }
            test.Equal(0, PendingTransfers(), "The background transfer loop continues without a forced drain");
        }
    }

    static void AssertMaskedEdit(TestContext test, ushort[] before, ushort[] after, string label)
    {
        bool changed = false, signedHdr = false;
        for (int i = 0; i < Side * Side; i++)
        {
            int c = i * 4;
            if (before[c] == after[c] && before[c + 1] == after[c + 1] &&
                before[c + 2] == after[c + 2] && before[c + 3] == after[c + 3]) continue;
            if (i % Side < ProtectedWidth)
                throw new InvalidOperationException(label + " changed a masked pixel at " + i % Side + "," + i / Side);
            changed = true;
            float r = Mathf.HalfToFloat(after[c]), g = Mathf.HalfToFloat(after[c + 1]), b = Mathf.HalfToFloat(after[c + 2]);
            signedHdr |= Mathf.Max(r, g, b) > 1f && Mathf.Min(r, g, b) < 0f;
        }
        test.True(changed, label + " changes pixels inside the selection");
        test.True(signedHdr, label + " carries signed HDR inside the selection");
    }

    static void Exact(TestContext test, ushort[] expected, ushort[] actual, string label)
        => test.True(expected.SequenceEqual(actual), label);

    static void Lose(TestContext test, RenderTexture texture, string label)
    {
        test.True(texture != null && texture.IsCreated(), label + " was allocated");
        texture.Release();
        test.True(!texture.IsCreated(), label + " native GPU storage was released");
    }

    static RenderTexture FirstSourcePage(TiledPaintSurface surface)
    {
        using var snapshot = surface.AcquireSnapshot();
        return snapshot.Tiles.First().Texture;
    }

    static Type Product(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        try { return type.GetMethod(name, F).Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    static object Get(object target, string name) => target.GetType().GetField(name, F)?.GetValue(target)
        ?? target.GetType().GetProperty(name, F)?.GetValue(target);
    static int PendingTransfers() => (int)Product("PaintTileTransfer").GetProperty("PendingCount", F).GetValue(null);
    static void Flush()
    {
        Call(Product("PaintTileTransfer"), "Drain");
        Call(Product("PaintHistoryStore"), "FlushWrites");
    }

    sealed class Fixture : IDisposable
    {
        readonly WhimTexDocument doc;
        readonly Texture2D original, mask;
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        int group;

        internal DrawingLayerBehaviour Drawing => (DrawingLayerBehaviour)doc.layers[0].Behaviour;
        internal TiledPaintSurface Surface => (TiledPaintSurface)Get(Drawing, "tiledSurface");
        internal RenderTexture WorkingTexture => (RenderTexture)Get(Drawing, "paintSurface");

        internal Fixture(bool tiled, bool standard = false, int height = Side)
        {
            doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave;
            doc.width = Side; doc.height = height;
            original = new Texture2D(Side, height, standard ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf, false, !standard) { hideFlags = HideFlags.HideAndDontSave };
            mask = new Texture2D(Side, height, TextureFormat.R8, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            try
            {
                var data = new Color[Side * height];
                var left = standard ? new Color(.84f, .13f, .31f, .43f) : new Color(3.25f, -.5f, .35f, .61f);
                var right = standard ? new Color(.11f, .53f, .95f, .72f) : new Color(-.25f, .35f, 2.5f, .84f);
                var coverage = mask.GetRawTextureData<byte>();
                for (int i = 0; i < data.Length; i++)
                {
                    int x = i % Side;
                    data[i] = x < 142 ? left : right;
                    coverage[i] = x < ProtectedWidth ? (byte)0 : x < 160 ? (byte)128 : byte.MaxValue;
                }
                original.SetPixels(data);
                original.Apply(false, false);
                mask.Apply(false, false);
                var drawing = new DrawingLayerBehaviour();
                doc.layers.Add(new Layer(drawing) { colorRange = standard ? LayerColorRange.Standard : LayerColorRange.HDR, blendRange = LayerBlendRange.HDR, filterMode = LayerFilterMode.Point });
                Call(drawing, "AdoptStoredTexture", original);
                Call(doc, "NormalizeModel");
                if (tiled) Call(drawing, "EnsureTiledSource", Side, height);
                Call(doc, "ResetUndoTrackingAfterLoad");
            }
            catch { Dispose(); throw; }
        }

        internal ushort[] Capture()
        {
            var pixels = doc.ComposeCanvas();
            try { return pixels.GetRawTextureData<ushort>().ToArray(); }
            finally { Object.DestroyImmediate(pixels); }
        }

        internal ushort[] CaptureSource()
        {
            if (Surface == null) return Read(WorkingTexture);
            var target = new RenderTexture(Side, doc.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Surface.CopyRegion(new RectInt(0, 0, Side, doc.height), target, false);
                return Read(target);
            }
            finally { Object.DestroyImmediate(target); }
        }

        static ushort[] Read(RenderTexture surface)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(surface.width, surface.height, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                RenderTexture.active = surface;
                pixels.ReadPixels(new Rect(0, 0, surface.width, surface.height), 0, 0, false);
                return pixels.GetRawTextureData<ushort>().ToArray();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
        }

        void Prepare(string label, bool dense, Vector2 start)
        {
            Undo.IncrementCurrentGroup();
            group = Undo.GetCurrentGroup();
            Call(Drawing, dense ? "PrepareStroke" : "PrepareBufferedStroke", Side, doc.height, label);
            Call(Drawing, "BeginStroke", start);
        }

        internal void Paint(bool pencil, float y)
        {
            var p = new Vector2(.59f, y);
            Prepare(pencil ? "Owned recovery Pencil" : "Owned recovery Brush", false, p);
            var type = Product("PaintToolSettings");
            var settings = Activator.CreateInstance(type, true);
            // Keep blue negative even over the positive HDR background at full coverage.
            type.GetField("brushColor", F).SetValue(settings, pencil ? new Color(1.5f, 3f, -4f, .9f) : new Color(1.5f, 3f, -.5f, .7f));
            type.GetField("brushSize", F).SetValue(settings, 70f);
            type.GetField("pencilSize", F).SetValue(settings, 65);
            var parameters = Call(settings, pencil ? "GetPencilParameters" : "GetStrokeParameters", false, null);
            Call(Drawing, "PaintPoint", p, Side, doc.height, Call(parameters, "WithSelectionMask", mask));
        }

        internal void Smudge(float y, int channels = 15)
        {
            var start = new Vector2(.46f, y);
            Prepare("Owned recovery Smudge", true, start);
            Call(Drawing, "ConfigureStrokeWriteProtection", channels);
            Call(Drawing, "BeginSmudgeStroke", start, Side, doc.height, 56f, null, false, 1f);
            SmudgeSegment(start.x, .8f, y);
        }

        internal void SmudgeSegment(float from, float to, float y) =>
            Call(Drawing, "SmudgeSegment", new Vector2(from, y), new Vector2(to, y), Side, doc.height, .8f, .9f, 1f, mask);

        internal void Blur(float y)
        {
            var center = new Vector2(.558f, y);
            Prepare("Owned recovery Blur", false, center);
            Call(Drawing, "ConfigureStrokeWriteProtection", 15);
            Call(Drawing, "BlurSegment", center, center, Side, doc.height, 56f, .8f, .9f, null, false, mask);
        }

        internal void Commit(bool flush)
        {
            Call(Drawing, "EndStroke");
            Call(doc, "MarkChanged");
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
            if (flush) Flush();
        }

        public void Dispose()
        {
            try
            {
                if (doc != null && doc.layers.Count > 0)
                {
                    var drawing = Drawing;
                    Call(drawing, "ReleaseTiledRuntime");
                    Call(drawing, "ReleasePaintSurface");
                    Flush();
                    PaintTileHistoryTests.ReleaseOwnedHistory(drawing);
                    Undo.ClearUndo(doc);
                    Call(drawing, "ReleaseTransientResources");
                    doc.layers.Clear();
                }
            }
            finally
            {
                if (doc != null) Object.DestroyImmediate(doc);
                if (original != null) Object.DestroyImmediate(original);
                if (mask != null) Object.DestroyImmediate(mask);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
            }
        }
    }
}
