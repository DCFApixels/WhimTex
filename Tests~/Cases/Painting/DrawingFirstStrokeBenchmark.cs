using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    [Serializable] public sealed class FirstStrokeSample
    {
        public int pair, order;
        public bool oldInitialization;
        public double createSubmitMs, createReadyMs, settleMs, prepareMs, beginMs, paintMs,
            paintSubmitMs, copyMs, requestMs, waitMs, paintReadyMs, controlFenceMs, commitMs;
    }
    [Serializable] public sealed class FirstStrokeReport
    {
        public string name, unity, gpu, result;
        public int diameter, pairs, batch;
        public bool settle;
        public List<FirstStrokeSample> samples = new();
    }

    // Both initializers run against the same compiled painting code. The old sequence
    // is reproduced only in this fixture using the unchanged production methods.
    public static string RunFirstStrokePairs(int diameter, int pairs, int batch, bool settle)
    {
        if ((diameter != 81 && diameter != 4096) || pairs < 2 || pairs > 16 || batch < 0 || batch > 9)
            throw new ArgumentException("Bounded 8K first-stroke comparison required");
        string name = "paired-" + diameter + "-" + batch + (settle ? "-settled" : "");
        string path = "Temp/WhimTex/drawing-initialization/" + name + "-" + Guid.NewGuid().ToString("N") + ".json";
        var report = new FirstStrokeReport { name = name, diameter = diameter, pairs = pairs, batch = batch,
            settle = settle, unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName };
        report.result = TestContext.Run("Paired first stroke: " + path, test =>
        {
            ReusePreflight(test);
            test.True(SystemInfo.maxTextureSize >= 8192 && SystemInfo.supportsAsyncGPUReadback, "8K GPU readback available");
            using var disk = new PaintTileHistoryTests.DiskScope();
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var mask = new Texture2D(8192, 8192, TextureFormat.R8, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var fence = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave };
            byte[] expected = null;
            try
            {
                var bytes = mask.GetRawTextureData<byte>();
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(32 + (i % 8192) * 223 / 8191);
                mask.Apply(false, false); test.True(fence.Create(), "Owned fence created");
                RenderTexture.active = fence; GL.Clear(false, true, Color.clear); RenderTexture.active = previous;
                InitFence(fence);
                // One warmup pair; alternate AB/BA so creation and cache drift cannot
                // systematically favor either implementation. Batch flips first order.
                for (int pair = -1; pair < pairs; pair++)
                    for (int order = 0; order < 2; order++)
                    {
                        bool old = ((pair + batch + 2) % 2 == 0) == (order == 0);
                        var row = new FirstStrokeSample { pair = pair, order = order, oldInitialization = old };
                        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
                        doc.width = doc.height = 8192;
                        var drawing = new DrawingLayerBehaviour(); Texture2D original = null; Object cursor = null;
                        try
                        {
                            var settings = Settings(new Color(3f, -.4f, .7f, .65f)); Set(settings, "brushSize", (float)diameter);
                            object parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
                            parameters = Call(parameters, "WithSelectionMask", mask);
                            var layer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                            long start = Stopwatch.GetTimestamp();
                            doc.layers.Add(layer); Call(doc, "NormalizeModel");
                            if (old)
                            {
                                Call(drawing, "EnsurePaintSurface", 8192, 8192);
                                Call(drawing, "SyncSurfaceToTexture"); Set(drawing, "tiledBaseEmpty", true);
                            }
                            else Call(drawing, "InitializeCanvas", 8192, 8192);
                            row.createSubmitMs = InitElapsed(start);
                            original = (Texture2D)Get(drawing, "StoredTexture"); InitFence(original);
                            row.createReadyMs = InitElapsed(start);
                            test.True((Get(drawing, "paintSurface") != null) == old, "Distinct old/new initializer loaded");
                            test.True((bool)Get(drawing, "tiledBaseEmpty"), "Neither path imports empty base into tiles");
                            if (settle)
                            {
                                long settled = Stopwatch.GetTimestamp(); InitFence(fence); InitFence(fence);
                                row.settleMs = InitElapsed(settled);
                            }
                            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                            var at = Vector2.one * .5f;
                            start = Stopwatch.GetTimestamp();
                            Call(drawing, "PrepareBufferedStroke", 8192, 8192, "Owned paired first stroke");
                            long prepared = Stopwatch.GetTimestamp(); row.prepareMs = InitElapsed(start);
                            Call(drawing, "BeginStroke", at);
                            long begun = Stopwatch.GetTimestamp(); row.beginMs = InitElapsed(prepared);
                            Call(drawing, "PaintPoint", at, 8192, 8192, parameters);
                            long painted = Stopwatch.GetTimestamp(); row.paintMs = InitElapsed(begun);
                            row.paintSubmitMs = InitElapsed(start);
                            var tiled = (TiledPaintSurface)Get(drawing, "tiledSurface");
                            tiled.CopyRegion(new RectInt(4096, 4096, 1, 1), fence, true);
                            row.copyMs = InitElapsed(painted);
                            long requested = Stopwatch.GetTimestamp();
                            var request = AsyncGPUReadback.Request(fence, 0, 0, 1, 0, 1, 0, 1);
                            row.requestMs = InitElapsed(requested); long waiting = Stopwatch.GetTimestamp();
                            request.WaitForCompletion(); row.waitMs = InitElapsed(waiting); row.paintReadyMs = InitElapsed(start);
                            test.True(!request.hasError, "Pending result readback completed");
                            byte[] pixel = request.GetData<byte>().ToArray();
                            if (expected == null)
                            {
                                expected = pixel; bool nonzero = false;
                                foreach (byte v in pixel) nonzero |= v != 0;
                                test.True(nonzero, "Masked pending stroke is visible");
                            }
                            else Exact(test, expected, pixel, "Identical masked HDR pending pixel across A/B pairs");
                            long control = Stopwatch.GetTimestamp(); InitFence(fence); row.controlFenceMs = InitElapsed(control);
                            start = Stopwatch.GetTimestamp(); Call(drawing, "EndStroke"); row.commitMs = InitElapsed(start);
                            cursor = (Object)Get(drawing, "PixelUndoObject");
                            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
                            report.samples.Add(row);
                        }
                        finally
                        {
                            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
                            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
                            if (original != null) Undo.ClearUndo(original);
                            Undo.ClearUndo(doc); Call(drawing, "ReleaseTransientResources");
                            doc.layers.Clear(); Object.DestroyImmediate(doc);
                        }
                    }
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                Object.DestroyImmediate(mask); fence.Release(); Object.DestroyImmediate(fence);
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var json = System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        File.WriteAllText(path, (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report }));
        return report.result;
    }
}
