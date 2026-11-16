using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
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
    [Serializable] public sealed class InitializationSample
    {
        public int repeat;
        public double createSubmitMs, createReadyMs, firstPaintSubmitMs, firstPaintReadyMs, commitMs;
        public long originalBytes, retainedSurfaceBytes;
    }
    [Serializable] public sealed class InitializationReport
    {
        public string name, unity, gpu, renderHash;
        public int side = 8192, diameter;
        public bool hdr, pencil, lazy, outside;
        public TestResult result;
        public List<InitializationSample> samples = new();
        public string scope = "Fresh owned Drawing sources, one warmup (repeat=-1), five measured repeats. Creation includes model normalization and optional InitializeCanvas; ready includes a 1x1 GPU readback fence. First paint includes PrepareBufferedStroke/BeginStroke/PaintPoint plus a separate 1x1 pending-source fence. Soft selection in every case. Commit, full-byte checks, Undo/Redo, cleanup and mask creation are outside first-result timing. No mouse/UI/presentation or profiler timing; originalBytes excludes the GPU copy and driver allocations.";
    }

    public static string RunInitialization(string name, bool hdr, int diameter, bool pencil, bool lazy, bool outside)
    {
        if (diameter < 1 || diameter > 4096 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Bounded initialization case required.");
        string path = "Temp/WhimTex/drawing-initialization/" + name + "-" + Guid.NewGuid().ToString("N") + ".json";
        var report = new InitializationReport { name = name, hdr = hdr, diameter = diameter, pencil = pencil,
            lazy = lazy, outside = outside, unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName };
        string result = TestContext.Run("Drawing creation and first result: " + path, test =>
        {
            ReusePreflight(test);
            test.True(SystemInfo.maxTextureSize >= 8192 && SystemInfo.supportsAsyncGPUReadback, "Graphical 8K readback required");
            using var disk = new PaintTileHistoryTests.DiskScope();
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var mask = new Texture2D(8192, 8192, TextureFormat.R8, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var fence = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var maskBytes = mask.GetRawTextureData<byte>();
                for (int i = 0; i < maskBytes.Length; i++) maskBytes[i] = (byte)(32 + (i % 8192) * 223 / 8191);
                mask.Apply(false, false); test.True(fence.Create(), "Owned fence created");
                for (int repeat = -1; repeat < 5; repeat++)
                {
                    var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
                    doc.width = doc.height = 8192;
                    DrawingLayerBehaviour drawing = null; Texture2D original = null; Object cursor = null;
                    try
                    {
                        var settings = Settings(hdr ? new Color(3f, -.4f, .7f, .65f) : new Color(.7f, .2f, .4f, .65f));
                        Set(settings, "brushSize", (float)diameter); Set(settings, "pencilSize", diameter);
                        object parameters = Call(settings, pencil ? "GetPencilParameters" : "GetStrokeParameters", false, (Color?)null);
                        parameters = Call(parameters, "WithSelectionMask", mask);
                        var row = new InitializationSample { repeat = repeat };
                        long start = Stopwatch.GetTimestamp();
                        drawing = new DrawingLayerBehaviour();
                        var layer = new Layer(drawing) { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard,
                            blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard };
                        layer.transform.position = outside ? new Vector2(6144, 0) : Vector2.zero;
                        doc.layers.Add(layer); Call(doc, "NormalizeModel");
                        if (!lazy) Call(drawing, "InitializeCanvas", 8192, 8192);
                        row.createSubmitMs = InitElapsed(start);
                        original = (Texture2D)Get(drawing, "StoredTexture");
                        if (original != null) InitFence(original);
                        row.createReadyMs = InitElapsed(start);
                        var surface = (RenderTexture)Get(drawing, "paintSurface");
                        row.retainedSurfaceBytes = surface == null ? 0 : (long)surface.width * surface.height * 8;
                        var at = new Vector2(outside ? -.25f : .5f, .5f);
                        var sourceToCanvas = (TextureTransform)Get(layer, "PixelCanvasTransform");
                        test.True(Vector2.Distance(sourceToCanvas.Map(at, Vector2.one * 8192), Vector2.one * .5f) < .00001f,
                            "Signed source UV keeps the same visible canvas position");
                        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                        start = Stopwatch.GetTimestamp();
                        Call(drawing, "PrepareBufferedStroke", 8192, 8192, "Owned initialization stroke");
                        Call(drawing, "BeginStroke", at); Call(drawing, "PaintPoint", at, 8192, 8192, parameters);
                        row.firstPaintSubmitMs = InitElapsed(start);
                        var tiled = (TiledPaintSurface)Get(drawing, "tiledSurface");
                        tiled.CopyRegion(new RectInt(Mathf.FloorToInt(at.x * 8192), 4096, 1, 1), fence, true);
                        var pendingRead = InitFence(fence); row.firstPaintReadyMs = InitElapsed(start);
                        bool pendingVisible = false;
                        foreach (byte value in pendingRead.GetData<byte>()) pendingVisible |= value != 0;
                        test.True(pendingVisible, "First-result fence reads the pending masked stroke");
                        start = Stopwatch.GetTimestamp(); Call(drawing, "EndStroke"); row.commitMs = InitElapsed(start);
                        original = (Texture2D)Get(drawing, "StoredTexture"); cursor = (Object)Get(drawing, "PixelUndoObject");
                        Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
                        Call(doc, "MarkChanged");
                        row.originalBytes = original.GetRawTextureData<byte>().Length;
                        test.Equal(hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, original.format, "Source format");
                        test.Equal(!hdr, original.isDataSRGB, "Source encoding");
                        test.Equal(8192, original.width, "Source width"); test.Equal(8192, original.height, "Source height");
                        test.Equal(1, original.mipmapCount, "No source mipmaps");
                        test.True(InitZero(original.GetRawTextureData<ulong>()), "Entire original CPU source remains zero behind sparse pixels");
                        if (repeat == 0)
                        {
                            var region = new RectInt(Mathf.FloorToInt(at.x * 8192) - diameter / 2 - 8, 4096 - diameter / 2 - 8, diameter + 16, diameter + 16);
                            byte[] expected = Probe(drawing, region);
                            using var sha = SHA256.Create(); report.renderHash = BitConverter.ToString(sha.ComputeHash(expected)).Replace("-", "").ToLowerInvariant();
                            bool nonzero = false; foreach (byte value in expected) nonzero |= value != 0;
                            test.True(nonzero, "First masked stroke is visible in its signed source region");
                            Undo.PerformUndo(); drawing = (DrawingLayerBehaviour)doc.layers[0].Behaviour;
                            byte[] empty = Probe(drawing, region); bool cleared = true; foreach (byte value in empty) cleared &= value == 0;
                            test.True(cleared, "First stroke Undo restores empty GPU pixels");
                            Undo.PerformRedo(); drawing = (DrawingLayerBehaviour)doc.layers[0].Behaviour;
                            Exact(test, expected, Probe(drawing, region), "First stroke Redo");
                            Call(drawing, "InitializeCanvas", 256, 256);
                            test.True(ReferenceEquals(original, Get(drawing, "StoredTexture")), "Reinitialization keeps source identity");
                            Exact(test, expected, Probe(drawing, region), "Reinitialization keeps existing paint");
                        }
                        report.samples.Add(row);
                    }
                    finally
                    {
                        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
                        if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
                        if (original != null) Undo.ClearUndo(original);
                        Undo.ClearUndo(doc);
                        if (drawing != null) Call(drawing, "ReleaseTransientResources");
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
        report.result = JsonUtility.FromJson<TestResult>(result);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var json = System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        File.WriteAllText(path, (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report }));
        return result;
    }

    static double InitElapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    static bool InitZero(NativeArray<ulong> values)
    { for (int i = 0; i < values.Length; i++) if (values[i] != 0) return false; return true; }
    static AsyncGPUReadbackRequest InitFence(Texture texture)
    {
        var request = AsyncGPUReadback.Request(texture, 0, 0, 1, 0, 1, 0, 1);
        request.WaitForCompletion();
        if (request.hasError) throw new InvalidOperationException("Owned initialization GPU fence failed.");
        return request;
    }
}
