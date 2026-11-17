using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    const string FrozenLifeKey = "WhimTex.Tests.FrozenLifecycle";
    [Serializable] public sealed class FrozenLifeRow
    {
        public string stage;
        public long cachedBytes, activeBytes, surfaceBytes, originalBytes, driverBytes;
        public int cachedOwners;
    }
    [Serializable] public sealed class FrozenLifeReport
    {
        public string result, sourceHash, fixtureHash, unity;
        public bool bounded;
        public List<FrozenLifeRow> rows = new();
    }
    static string LifeHash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
    public static string PollFrozenLife(string id)
    {
        var state = AppDomain.CurrentDomain.GetData(FrozenLifeKey) as object[];
        return state != null && (string)state[0] == id ? (string)state[1] : TestContext.Result("failed", 0, "Missing lifecycle job").ToJson();
    }
    public static string CancelFrozenLife(string id)
    {
        var state = AppDomain.CurrentDomain.GetData(FrozenLifeKey) as object[];
        if (state != null && (string)state[0] == id && state[2] is Action cancel) cancel();
        return PollFrozenLife(id);
    }
    public static string StartFrozenLife(string id, bool bounded)
    {
        var state = AppDomain.CurrentDomain.GetData(FrozenLifeKey) as object[];
        if (state != null && state[2] != null) throw new InvalidOperationException("Lifecycle job already active");
        var t = new TestContext(); var report = new FrozenLifeReport { bounded = bounded, unity = Application.unityVersion,
            sourceHash = LifeHash("Packages/com.dcfapixels.whimtex/src/Layers/TiledPaint/TiledPaintSurface.cs"),
            fixtureHash = LifeHash("Packages/com.dcfapixels.whimtex/Tests~/Cases/Painting/FrozenInputLifecycleTests.cs") };
        state = new object[] { id, TestContext.Result("running", 0, "Frozen lifecycle").ToJson(), null };
        AppDomain.CurrentDomain.SetData(FrozenLifeKey, state);
        IEnumerator sequence = null; EditorApplication.CallbackFunction tick = null;
        UnityEditor.AssemblyReloadEvents.AssemblyReloadCallback reload = null; bool finished = false;
        void Finish(Exception error)
        {
            if (finished) return; finished = true;
            EditorApplication.update -= tick; AssemblyReloadEvents.beforeAssemblyReload -= reload;
            try { (sequence as IDisposable)?.Dispose(); } catch (Exception cleanup) { error = cleanup; }
            report.result = TestContext.Result(error == null ? "passed" : error is OperationCanceledException ? "cancelled" : "failed",
                t.Checks, "Four 8K Drawing layers, two documents: switching, close and native Undo/Redo",
                error == null ? Array.Empty<string>() : new[] { error.ToString() }).ToJson();
            Directory.CreateDirectory("Temp/WhimTex/frozen-lifecycle");
            var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
            File.WriteAllText("Temp/WhimTex/frozen-lifecycle/" + id + ".json",
                (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report }));
            state[1] = report.result; state[2] = null;
        }
        tick = () => { try { if (!sequence.MoveNext()) Finish(null); } catch (Exception e) { Finish(e); } };
        reload = () => Finish(new OperationCanceledException("Editor reload"));
        state[2] = (Action)(() => Finish(new OperationCanceledException("Requested cancellation")));
        try
        {
            ReusePreflight(t);
            t.True(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlaying, "Idle Editor");
            t.True(SystemInfo.maxTextureSize >= 8192 && SystemInfo.graphicsMemorySize >= 7000, "Four real 8K HDR originals require an 8 GiB-class GPU");
            sequence = FrozenLifeSequence(t, report);
            EditorApplication.update += tick; AssemblyReloadEvents.beforeAssemblyReload += reload;
        }
        catch (Exception e) { Finish(e); }
        return PollFrozenLife(id);
    }
    static IEnumerator FrozenLifeSequence(TestContext t, FrozenLifeReport report)
    {
        const int side = 8192; const long pairBytes = 576L * 1024 * 1024;
        using var disk = new PaintTileHistoryTests.DiskScope();
        var docs = new WhimTexDocument[2]; var drawings = new DrawingLayerBehaviour[4]; var masks = new Texture2D[2];
        var originals = new Texture2D[4]; var cursors = new Object[4]; var copies = new HashSet<RenderTexture>();
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var errors = new List<string>(); Application.LogCallback logger = (text, stack, kind) =>
        { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(text); };
        Application.logMessageReceived += logger;
        long InputBytes(object holder)
        {
            if (holder == null) return 0;
            var rt = (RenderTexture)Get(holder, "Texture");
            if (rt == null) return 0; copies.Add(rt);
            return (long)rt.width * rt.height * GraphicsFormatUtility.GetBlockSize(rt.graphicsFormat);
        }
        void Measure(string stage)
        {
            var row = new FrozenLifeRow { stage = stage, driverBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() };
            for (int i = 0; i < drawings.Length; i++)
            {
                if (originals[i] != null) row.originalBytes += (long)originals[i].width * originals[i].height * 8;
                var surface = drawings[i] == null ? null : (TiledPaintSurface)Get(drawings[i], "tiledSurface");
                if (surface == null) continue;
                long cached = InputBytes(Get(surface, "cachedSelection")) + InputBytes(Get(surface, "cachedOriginal"));
                row.cachedBytes += cached; if (cached != 0) row.cachedOwners++;
                row.activeBytes += InputBytes(Get(surface, "selection")) + InputBytes(Get(surface, "originalStraight"));
                row.surfaceBytes += surface.AllocatedBytes;
                t.True(surface.AllocatedBytes <= 2L * 1024 * 1024 * 1024, "Per-surface cap at " + stage);
            }
            report.rows.Add(row);
            if (stage == "four-layers-warm")
            {
                t.Equal(report.bounded ? 1 : 4, row.cachedOwners, "Measured idle owners after four real strokes");
                t.Equal((report.bounded ? 1 : 4) * pairBytes, row.cachedBytes, "Exact total idle snapshot payload");
            }
            if (report.bounded) t.True(row.cachedOwners <= 1 && row.cachedBytes <= pairBytes, "One idle input pair across owned documents at " + stage);
        }
        object Parameters(int index, int step)
        {
            var settings = Settings(new Color(3 + step, -.5f, 2, .65f)); Set(settings, "brushSize", 81f);
            Set(Get(settings, "dynamics"), "writeChannels", 9);
            return Call(Call(settings, "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", masks[index / 2]);
        }
        Vector2 At(int index) => new Vector2((index & 1) == 0 ? .5f : -.25f, .5f);
        RectInt Region(int index) => new RectInt((int)(At(index).x * side) - 8, side / 2 - 8, 16, 16);
        byte[] View(int index)
        {
            Call(drawings[index], "EnsureTiledSource", side, side);
            return Probe(drawings[index], Region(index));
        }
        void Stroke(int index, int step)
        {
            var drawing = drawings[index]; Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Call(drawing, "PrepareBufferedStroke", side, side, "Owned lifecycle stroke");
            Call(drawing, "BeginStroke", At(index)); Call(drawing, "PaintPoint", At(index), side, side, Parameters(index, step));
            Measure("pending-" + index + "-" + step);
            Call(drawing, "EndStroke"); Call(drawing, "FlushTiledStorage"); Static("PaintHistoryStore", "FlushWrites");
            cursors[index] = (Object)Get(drawing, "PixelUndoObject");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            Measure("committed-" + index + "-" + step);
        }
        try
        {
            for (int d = 0; d < docs.Length; d++)
            {
                docs[d] = ScriptableObject.CreateInstance<WhimTexDocument>(); docs[d].hideFlags = HideFlags.HideAndDontSave;
                docs[d].width = docs[d].height = side;
                masks[d] = new Texture2D(side, side, TextureFormat.R8, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var bytes = masks[d].GetRawTextureData<byte>();
                for (int p = 0; p < bytes.Length; p++) bytes[p] = (byte)(32 + (p % side) * 223 / (side - 1));
                masks[d].Apply(false, false); yield return null;
            }
            for (int i = 0; i < drawings.Length; i++)
            {
                var drawing = drawings[i] = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                var layer = drawing.Owner; docs[i / 2].layers.Add(layer); Call(docs[i / 2], "NormalizeModel");
                layer.transform.position = (i & 1) == 0 ? Vector2.zero : new Vector2(6144, 0);
                Call(drawing, "InitializeCanvas", side, side); originals[i] = (Texture2D)Get(drawing, "StoredTexture");
                t.Equal(TextureFormat.RGBAHalf, originals[i].format, "Real HDR original format");
                // Transparent source with signed hidden RGB exercises protected channels.
                var raw = originals[i].GetRawTextureData<half4>();
                raw[side / 2 * side + side / 2] = new half4(new float4(4 + i, -.5f, 2, 0));
                originals[i].Apply(false, false);
                Stroke(i, 0);
                var pixel = View(i); int offset = (8 * 16 + 8) * 8;
                float red = math.f16tof32(BitConverter.ToUInt16(pixel, offset));
                float green = math.f16tof32(BitConverter.ToUInt16(pixel, offset + 2));
                float alpha = math.f16tof32(BitConverter.ToUInt16(pixel, offset + 6));
                t.True(alpha > 0 && alpha < .65f, "Soft mask modulates the real stroke alpha");
                t.True(red > alpha, "HDR straight red exceeds one");
                if ((i & 1) == 0) t.True(green < 0, "Protected signed hidden green survives painting");
                yield return null;
            }
            Measure("four-layers-warm");
            for (int i = 0; i < 4; i++)
            {
                int index = new[] { 0, 2, 1, 3 }[i]; var drawing = drawings[index];
                var before = StorageBytes(drawing); var beforeGpu = View(index);
                Stroke(index, i + 1); var after = StorageBytes(drawing); var afterGpu = View(index);
                t.True(!ReuseEqual(beforeGpu, afterGpu), "Real masked protected stroke changes GPU result");
                Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo");
                ExactTiles(t, before, StorageBytes(drawing), "Cross-document native Undo");
                Exact(t, beforeGpu, View(index), "Undo restores GPU HDR bytes");
                Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo");
                ExactTiles(t, after, StorageBytes(drawing), "Cross-document native Redo");
                Exact(t, afterGpu, View(index), "Redo restores GPU HDR bytes");
                Measure("undo-redo-" + index); yield return null;
            }
            // Actual document destruction runs OnDisable/OnDestroy without clearing layers first.
            for (int d = 0; d < docs.Length; d++)
            {
                Object.DestroyImmediate(docs[d]); docs[d] = null; yield return null;
                for (int i = d * 2; i < d * 2 + 2; i++)
                {
                    t.True(Get(drawings[i], "tiledSurface") == null, "Close releases runtime");
                    t.True(originals[i] == null, "Close destroys owned original");
                }
                Measure("closed-document-" + d);
            }
            foreach (var copy in copies) t.True(copy == null, "All owned snapshot allocations destroyed");
            for (int i = 0; i < 4; i++) yield return null;
            Measure("settled-after-close"); t.True(errors.Count == 0, string.Join("\n", errors));
        }
        finally
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            for (int i = 0; i < cursors.Length; i++) if (cursors[i] != null) { Undo.ClearUndo(cursors[i]); Object.DestroyImmediate(cursors[i]); }
            for (int d = 0; d < docs.Length; d++) if (docs[d] != null) { Undo.ClearUndo(docs[d]); Object.DestroyImmediate(docs[d]); }
            foreach (var original in originals) if (original != null) { Undo.ClearUndo(original); Object.DestroyImmediate(original); }
            foreach (var mask in masks) if (mask != null) Object.DestroyImmediate(mask);
            Application.logMessageReceived -= logger; RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    }
}
