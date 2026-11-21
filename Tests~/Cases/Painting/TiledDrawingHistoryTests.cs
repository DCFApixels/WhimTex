using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Own transient documents and Undo groups only. No windows or input simulation.
public static partial class TiledDrawingHistoryTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Product(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static object Get(object value, string name) => value.GetType().GetField(name, F)?.GetValue(value) ?? value.GetType().GetProperty(name, F)?.GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static object Static(string type, string name, params object[] args) => Product(type).GetMethod(name, S).Invoke(null, args);

    [Serializable] public sealed class Timing8K
    {
        public string name;
        public double prepareMs, firstPaintMs, segmentMs, endStrokeMs, flushMs;
        public long managedBytes, poolGpuBytes, tileCpuBytes, originalCpuBytes, readbackStagingBytes;
        public int tiles;
    }
    [Serializable] public sealed class Report8K { public int side; public double initializationMs; public List<Timing8K> strokes = new(); }
    public static string Run8K(int side = 8192) => Run8KCore(side, false, false);
    public static string RunInside8K(int side = 8192) => Run8KCore(side, false, true);
    public static string RunDense8K(int side = 8192) => Run8KCore(side, true, true);
    public static string RunLarge8K(int side, bool inside) => side == 8192
        ? Run8KCore(side, false, inside, 4096) : throw new ArgumentOutOfRangeException(nameof(side));
    static string Run8KCore(int side, bool dense, bool inside, int diameter = 2048) => TestContext.Run((dense ? "Retained dense full completion baseline at " : "Production tiled Drawing large strokes and exact signed history at ") + side + ", brush " + diameter, test =>
    {
        if (side < 2048 || side > 8192) throw new ArgumentOutOfRangeException(nameof(side));
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = side;
        var drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var mask = new Texture2D(side, side, TextureFormat.R8, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Object cursor = null; Texture2D original = null;
        var report = new Report8K { side = side };
        try
        {
            var maskBytes = mask.GetRawTextureData<byte>();
            for (int i = 0; i < maskBytes.Length; i++) maskBytes[i] = (byte)((long)(i % side) * 255 / (side - 1));
            mask.Apply(false, false); document.layers.Add(drawing); Call(document, "NormalizeModel");
            // Isolated reference only: reproduce the previous dense Brush/Pencil
            // pipeline, including its real four-plane disk history and completion.
            if (dense) Set(drawing, "renderingBrushTipPreview", true);
            var initialization = System.Diagnostics.Stopwatch.StartNew();
            Call(drawing, "InitializeCanvas", side, side);
            if (!dense) Call(drawing, "EnsureTiledSource", side, side);
            report.initializationMs = initialization.Elapsed.TotalMilliseconds;
            var versions = new List<Dictionary<Vector2Int, byte[]>> { new() };
            var centers = inside ? diameter == 4096
                ? new[] { new Vector2(.27f, .27f), new Vector2(.70f, .70f), new Vector2(.5f, .5f) }
                : new[] { new Vector2(.2f, .25f), new Vector2(.65f, .75f), new Vector2(.5f, .5f) }
                : new[] { new Vector2(-.04f, .35f), new Vector2(1.06f, .8f), new Vector2(.5f, .5f) };
            for (int stroke = 0; stroke < centers.Length; stroke++)
            {
                bool pencil = stroke == 2; var settings = Settings(new Color(-.5f, 4f, .8f, .75f));
                Set(settings, "brushSize", (float)diameter); Set(settings, "pencilSize", diameter);
                object parameters = Call(settings, pencil ? "GetPencilParameters" : "GetStrokeParameters", false, (Color?)null);
                if (pencil) parameters = Call(parameters, "WithSelectionMask", mask);
                var row = new Timing8K { name = pencil ? "Pencil " + diameter + " with selection" : "Brush " + diameter + " " + stroke };
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Call(drawing, dense ? "PrepareStroke" : "PrepareBufferedStroke", side, side, "Owned large stroke comparison");
                Call(drawing, "BeginStroke", centers[stroke]); row.prepareMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart(); Call(drawing, "PaintPoint", centers[stroke], side, side, parameters); row.firstPaintMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart(); Call(drawing, "PaintSegment", centers[stroke], centers[stroke] + new Vector2(.03f, .02f), side, side, false, parameters);
                row.segmentMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart(); Call(drawing, "EndStroke"); row.endStrokeMs = watch.Elapsed.TotalMilliseconds;
                row.managedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                row.readbackStagingBytes = (long)Product("PaintTileTransfer").GetProperty("StagingBytes", S).GetValue(null);
                watch.Restart(); Call(drawing, "FlushTiledStorage"); Static("PaintHistoryStore", "FlushWrites"); row.flushMs = watch.Elapsed.TotalMilliseconds;
                cursor = (Object)Get(drawing, "PixelUndoObject"); original = (Texture2D)Get(drawing, "StoredTexture");
                var version = dense ? null : StorageBytes(drawing); if (!dense) versions.Add(version);
                row.tiles = dense ? (int)Get(drawing, "LastPaintTileCount") : version.Count;
                row.tileCpuBytes = dense ? 0 : version.Count * 256L * 256 * 8;
                row.originalCpuBytes = (long)original.width * original.height * 8;
                row.poolGpuBytes = dense ? (long)side * side * 8 : ((TiledPaintSurface)Get(drawing, "tiledSurface")).AllocatedBytes; report.strokes.Add(row);
                test.Equal(side, (int)Get(drawing, "TiledSourceWidth"), "Unbounded stroke keeps original UV width");
                test.Equal(side, (int)Get(drawing, "TiledSourceHeight"), "Unbounded stroke keeps original UV height");
                if (dense)
                {
                    test.True(!(bool)Get(drawing, "HasTiledSource"), "Reference uses retained dense source and native completion");
                    test.True(row.tiles > 0, "Dense reference actually commits affected blocks");
                }
                else test.True(version.Count > versions[stroke].Count, "Large stroke adds independent source tiles");
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            }
            if (dense) return;
            bool negative = false, outside = false, signedHdr = false;
            foreach (var tile in (IEnumerable)Get(drawing, "TiledPixels"))
            {
                var key = (Vector2Int)Get(tile, "key"); negative |= key.x < 0 || key.y < 0; outside |= key.x >= side / 256 || key.y >= side / 256;
                var data = ((Texture2D)Get(tile, "texture")).GetRawTextureData<Unity.Mathematics.half4>();
                foreach (var half in data) { Unity.Mathematics.float4 value = half; signedHdr |= value.x < 0 && value.y > 1; }
            }
            if (!inside) test.True(negative && outside, "Paint exists on both negative and beyond-canvas source tiles");
            test.True(signedHdr, "Signed and above-one HDR survived large strokes and readback");
            for (int i = 2; i >= 0; i--)
            { Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); ExactTiles(test, versions[i], StorageBytes(drawing), "8K Undo " + i); }
            for (int i = 1; i <= 3; i++)
            { Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); ExactTiles(test, versions[i], StorageBytes(drawing), "8K Redo " + i); }
            Call(drawing, "ReleaseTiledRuntime"); Call(drawing, "EnsureTiledSource", side, side);
            var final = versions[3]; var first = default(Vector2Int); foreach (var key in final.Keys) { first = key; break; }
            Exact(test, final[first], Probe(drawing, new RectInt(first * 256, new Vector2Int(256, 256))), "Recreated signed GPU tile after complete Undo/Redo");
        }
        finally
        {
            System.IO.Directory.CreateDirectory("Temp/WhimTex/tiled-drawing-integration");
            System.IO.File.WriteAllText("Temp/WhimTex/tiled-drawing-integration/" + (diameter == 4096 ? "large4096-" : "") + (dense ? "history-current-8k-timings.json" : inside ? "history-raster-inside-8k-timings.json" : "history-8k-timings.json"),
                (string)Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true)
                    .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report }));
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Undo.ClearUndo(document); if (original != null) Undo.ClearUndo(original);
            Call(drawing, "ReleaseTransientResources"); document.layers.Clear(); Object.DestroyImmediate(document); Object.DestroyImmediate(mask);
        }
    });

    static Dictionary<Vector2Int, byte[]> StorageBytes(DrawingLayerBehaviour drawing)
    {
        Call(drawing, "FlushTiledStorage"); var result = new Dictionary<Vector2Int, byte[]>();
        foreach (var tile in (IEnumerable)Get(drawing, "TiledPixels"))
            result.Add((Vector2Int)Get(tile, "key"), ((Texture2D)Get(tile, "texture")).GetRawTextureData<byte>().ToArray());
        return result;
    }
    static void ExactTiles(TestContext test, Dictionary<Vector2Int, byte[]> expected, Dictionary<Vector2Int, byte[]> actual, string label)
    {
        test.Equal(expected.Count, actual.Count, label + " tile count");
        foreach (var tile in expected)
            test.True(actual.TryGetValue(tile.Key, out var bytes) && EqualBytes(tile.Value, bytes), label + " exact raw Half tile " + tile.Key);
    }

    public static string RunStorageConversions() => TestContext.Run("Tiled Drawing 8-bit conversion includes signed HDR tiles and exact native Undo", test =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 256;
        var drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var original = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
        Object cursor = null;
        try
        {
            original.SetPixels(new Color[256 * 256]); original.Apply(false, false);
            Call(drawing, "AdoptStoredTexture", original); document.layers.Add(drawing); Call(document, "NormalizeModel");
            var region = new RectInt(-256, -256, 768, 768);
            Stroke(drawing, new Vector2(-.2f, .4f), new Color(4f, -.5f, .23f, .6f));
            cursor = (Object)Get(drawing, "PixelUndoObject");
            var before = Probe(drawing, region);
            Convert(drawing, document);
            var converted = Probe(drawing, region);
            test.Equal(TextureFormat.RGBA32, original.format, "Original storage converts to RGBA32");
            test.True(!EqualBytes(before, converted), "HDR overflow tiles are actually quantized");
            Normalized(test, converted, region.size);
            Undo.PerformUndo(); drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour; Call(drawing, "InvalidatePaintSurface");
            test.Equal(TextureFormat.RGBAHalf, original.format, "Undo restores original HDR layout");
            Exact(test, before, Probe(drawing, region), "Conversion Undo restores all signed source bytes");
            Undo.PerformRedo(); drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour; Call(drawing, "InvalidatePaintSurface");
            Exact(test, converted, Probe(drawing, region), "Conversion Redo restores all rounded tiles");

            // The base remains RGBA32 while a later HDR stroke can still populate
            // Half tiles. Repeating conversion must not return early on the base.
            Stroke(drawing, new Vector2(.5f, -.25f), new Color(3f, -.2f, 2f, .8f));
            var beforeSecond = Probe(drawing, region);
            Convert(drawing, document); var second = Probe(drawing, region);
            test.True(!EqualBytes(beforeSecond, second), "Already-RGBA32 base still quantizes modified Half tiles");
            Normalized(test, second, region.size);
            Undo.PerformUndo(); drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour; Call(drawing, "InvalidatePaintSurface");
            Exact(test, beforeSecond, Probe(drawing, region), "Second conversion Undo is exact");
            Undo.PerformRedo(); drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour; Call(drawing, "InvalidatePaintSurface");
            Exact(test, second, Probe(drawing, region), "Second conversion Redo is exact");
        }
        finally
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            foreach (var tile in (IEnumerable)Get(drawing, "TiledPixels"))
            { var texture = (Texture2D)Get(tile, "texture"); if (texture != null) Undo.ClearUndo(texture); }
            Undo.ClearUndo(document); if (original != null) Undo.ClearUndo(original);
            Call(drawing, "ReleaseTransientResources"); document.layers.Clear(); Object.DestroyImmediate(document);
        }
    });

    static void Convert(DrawingLayerBehaviour drawing, WhimTexDocument document)
    {
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.RegisterCompleteObjectUndo(document, "Owned storage conversion");
        Call(drawing, "ConvertTo8Bit");
        Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
    }
    static bool EqualBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    static void Normalized(TestContext test, byte[] data, Vector2Int size)
    {
        var texture = new Texture2D(size.x, size.y, TextureFormat.RGBAHalf, false, true);
        try
        {
            texture.LoadRawTextureData(data); var colors = texture.GetPixels(); int outside = 0;
            foreach (var color in colors)
                if (color.r < 0 || color.g < 0 || color.b < 0 || color.a < 0 || color.r > 1 || color.g > 1 || color.b > 1 || color.a > 1) outside++;
            test.Equal(0, outside, "All converted original-frame and overflow channels stay normalized");
        }
        finally { Object.DestroyImmediate(texture); }
    }

    public static string RunBridges() => TestContext.Run("Filled base survives first Raster stroke and dense edit cancellation", test =>
    {
        var previous = RenderTexture.active;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 256;
        var drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        Object cursor = null; Texture2D original = null;
        try
        {
            document.layers.Add(drawing); Call(document, "NormalizeModel");
            var fillColors = new Color[256 * 256];
            for (int i = 0; i < fillColors.Length; i++) fillColors[i] = new Color(2f, -.5f, .25f, 1f);
            using var fill = new NativeArray<Color>(fillColors, Allocator.Temp);
            typeof(DrawingLayerBehaviour).GetMethod("ApplyFillPixels", F, null,
                new[] { typeof(NativeArray<Color>), typeof(int), typeof(int), typeof(string) }, null)
                .Invoke(drawing, new object[] { fill, 256, 256, "Owned fill before Raster" });
            Call(drawing, "SyncPendingSurfaceToTexture"); original = (Texture2D)Get(drawing, "StoredTexture");
            cursor = (Object)Get(drawing, "PixelUndoObject");
            var sourceRegion = new RectInt(0, 0, 256, 256);
            var filled = original.GetRawTextureData<byte>().ToArray();
            Exact(test, filled, Probe(drawing, sourceRegion), "First Raster import retains preceding HDR fill");
            Stroke(drawing, new Vector2(-.5f, -.5f), new Color(3f, .4f, -.3f, .7f));
            Exact(test, filled, Probe(drawing, sourceRegion), "Outside Brush retains existing filled base");
            var region = new RectInt(-256, -256, 768, 768);
            var before = Probe(drawing, region);
            Call(drawing, "PrepareStroke", 256, 256, "Owned dense cancellation");
            Call(drawing, "BeginStroke", new Vector2(.5f, .5f));
            test.True((bool)Get(drawing, "tiledDenseEdit"), "BeginStroke retains prepared dense bridge");
            Call(drawing, "CapturePixelEdit", new RectInt(0, 0, 256, 256));
            RenderTexture.active = (RenderTexture)Get(drawing, "paintSurface"); GL.Clear(false, true, new Color(4f, 2f, -.75f, 1));
            RenderTexture.active = previous; Set(drawing, "paintSurfaceDirty", true);
            Call(drawing, "CancelStroke");
            test.True(!(bool)Get(drawing, "tiledDenseEdit"), "Cancel releases the dense bridge");
            Exact(test, before, Probe(drawing, region), "Dense cancellation preserves source and overflow exactly");
            Exact(test, filled, original.GetRawTextureData<byte>().ToArray(), "Dense cancellation keeps original straight pixels");
            Stroke(drawing, new Vector2(.3f, .4f), new Color(.4f, 3f, -.2f, .7f));
            var beforeFill = Probe(drawing, region); var beforeTiles = StorageBytes(drawing);
            var source = (NativeArray<Color>)Call(drawing, "ReadSourcePixels", Allocator.Temp);
            try
            {
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) source[y * 256 + x] = new Color(1f, 2f, 3f, 1f);
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                typeof(DrawingLayerBehaviour).GetMethod("ApplyFillPixels", F, null,
                    new[] { typeof(NativeArray<Color>), typeof(int), typeof(int), typeof(string) }, null)
                    .Invoke(drawing, new object[] { source, 256, 256, "Owned Fill after Raster" });
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            }
            finally { source.Dispose(); }
            var afterFill = Probe(drawing, region); var afterTiles = StorageBytes(drawing);
            foreach (var tile in beforeTiles)
                if (tile.Key != Vector2Int.zero)
                    test.True(afterTiles.TryGetValue(tile.Key, out var data) && EqualBytes(tile.Value, data), "Fill retains exact overflow tile " + tile.Key);
            int unchanged = 0;
            for (int y = 0; y < region.height; y++) for (int x = 0; x < region.width; x++)
            {
                int sourceX = x + region.x, sourceY = y + region.y;
                if (sourceX >= 0 && sourceX < 16 && sourceY >= 0 && sourceY < 16) continue;
                int offset = (y * region.width + x) * 8;
                for (int channelByte = 0; channelByte < 8; channelByte++) if (beforeFill[offset + channelByte] != afterFill[offset + channelByte]) unchanged++;
            }
            test.Equal(0, unchanged, "Fill keeps every untouched original-frame and overflow Half byte");
            test.True(!EqualBytes(beforeFill, afterFill), "Fill changes the requested source region");
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, beforeFill, Probe(drawing, region), "Fill after Raster Undo");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, afterFill, Probe(drawing, region), "Fill after Raster Redo");
            Undo.IncrementCurrentGroup(); int clearGroup = Undo.GetCurrentGroup();
            Call(drawing, "PrepareStroke", 256, 256, "Owned Clear after Raster");
            test.True((bool)Get(drawing, "tiledDenseEdit"), "Clear begins through the actual prepared dense UI path");
            Call(drawing, "ClearSurface", 256, 256); Call(drawing, "EndStroke");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(clearGroup); Undo.IncrementCurrentGroup();
            test.True(!(bool)Get(drawing, "tiledDenseEdit") && Get(drawing, "paintSurface") == null, "Clear releases its stale dense override");
            var cleared = Probe(drawing, region);
            Exact(test, new byte[cleared.Length], cleared, "Clear erases original frame and every overflow pixel");
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, afterFill, Probe(drawing, region), "Clear after Raster Undo");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, cleared, Probe(drawing, region), "Clear after Raster Redo");
            Call(drawing, "ReleaseTransientResources");
            test.True(cursor == null, "Permanent source disposal releases its native paint history owner");
        }
        finally
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Undo.ClearUndo(document); if (original != null) Undo.ClearUndo(original);
            Call(drawing, "ReleaseTransientResources"); document.layers.Clear(); Object.DestroyImmediate(document);
            RenderTexture.active = previous;
        }
    });

    public static string RunExisting() => TestContext.Run("Tiled Drawing history branching, checkpoints and cancellation", test =>
    {
        var previous = RenderTexture.active;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 256;
        var drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var original = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
        Object cursor = null;
        try
        {
            var colors = new Color[256 * 256];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(2f + i % 13 * .02f, -.25f, .3f, .75f);
            original.SetPixels(colors); original.Apply(false, false);
            var baseBytes = original.GetRawTextureData<byte>().ToArray();
            Call(drawing, "AdoptStoredTexture", original); document.layers.Add(drawing); Call(document, "NormalizeModel");
            var region = new RectInt(-256, -256, 768, 768);
            var baseline = Probe(drawing, region);
            Stroke(drawing, new Vector2(-.1f, .4f), new Color(4, -.5f, .2f, .6f));
            var first = Probe(drawing, region);
            cursor = (Object)Get(drawing, "PixelUndoObject");
            Stroke(drawing, new Vector2(.98f, -.06f), new Color(-.3f, 1.8f, 2.4f, .9f));
            var second = Probe(drawing, region);
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, first, Probe(drawing, region), "Second stroke Undo");
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, baseline, Probe(drawing, region), "First-touch overrides disappear on Undo");
            test.True(!(bool)Get(drawing, "HasTiledPixels"), "No sparse overrides remain at original baseline");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, first, Probe(drawing, region), "First Redo");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, second, Probe(drawing, region), "Second Redo");
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo");
            Stroke(drawing, new Vector2(.35f, .45f), new Color(.1f, 3, -.2f, .35f));
            var branch = Probe(drawing, region);
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, first, Probe(drawing, region), "New branch Undo");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, branch, Probe(drawing, region), "New branch Redo");
            Call(drawing, "FlushTiledStorage"); Call(drawing, "ReleaseTiledRuntime");
            Exact(test, branch, Probe(drawing, region), "Exact runtime reconstruction from CPU tiles");
            var settings = Settings(new Color(5, 2, 1, 1));
            Call(drawing, "PrepareBufferedStroke", 256, 256, "Owned cancelled tiled stroke");
            Call(drawing, "BeginStroke", new Vector2(1.5f, -.3f));
            Call(drawing, "PaintPoint", new Vector2(1.5f, -.3f), 256, 256, Call(settings, "GetStrokeParameters", false, (Color?)null));
            Call(drawing, "CancelStroke");
            Exact(test, branch, Probe(drawing, region), "Cancelled pending stroke does not alter source");
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            Call(cursor, "ClearHistory");
            Call(drawing, "ReleaseTiledRuntime");
            Exact(test, branch, Probe(drawing, region), "Clearing history retains signed-coordinate source and checkpoint");
            Stroke(drawing, new Vector2(-.3f, -.2f), new Color(2, 1, 4, .4f));
            var afterClear = Probe(drawing, region);
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, branch, Probe(drawing, region), "New Undo after checkpoint clear");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Exact(test, afterClear, Probe(drawing, region), "New Redo after checkpoint clear");
            Exact(test, baseBytes, original.GetRawTextureData<byte>().ToArray(), "Original straight base bytes remain unchanged");
            CancelledTransfer(test);
        }
        finally
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Undo.ClearUndo(document); if (original != null) Undo.ClearUndo(original);
            Call(drawing, "ReleaseTransientResources"); document.layers.Clear(); Object.DestroyImmediate(document);
            RenderTexture.active = previous;
        }
    });

    static void CancelledTransfer(TestContext test)
    {
        Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
        var previous = RenderTexture.active;
        var target = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
        Object history = null; object step = null;
        try
        {
            var pixels = new Color[256 * 256]; Array.Fill(pixels, new Color(.25f, .5f, .75f, .8f));
            target.SetPixels(pixels); target.Apply(false, false); byte[] before = target.GetRawTextureData<byte>().ToArray();
            history = (Object)Static("DrawingPixelHistory", "Find", target, true);
            var blocks = new[] { new RectInt(-256, 0, 256, 256) };
            step = Call(history, "ReserveSparse", (object)blocks);
            var pageType = Product("PaintTileTransfer").GetNestedType("Page", BindingFlags.NonPublic);
            var page = Activator.CreateInstance(pageType, F, null,
                new object[] { target, step, 0, blocks, 256, false, new[] { target }, new[] { false }, null }, null);
            RenderTexture.active = (RenderTexture)Get(page, "after"); GL.Clear(false, true, new Color(3f, -.4f, .2f, .6f));
            RenderTexture.active = previous; Call(page, "Queue");
            test.True((int)Product("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null) > 0,
                "Abandoned revision starts with a genuinely queued immutable GPU readback");
            // This is the state reached by a transaction failure after Queue:
            // the abandoned result must never reach authoritative CPU backing.
            Set(step, "cancelled", true); Static("PaintTileTransfer", "Drain");
            Exact(test, before, target.GetRawTextureData<byte>().ToArray(), "Cancelled transfer leaves durable CPU bytes untouched");
            test.Equal(0, (int)Product("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null), "Cancellation releases queued pages");
            test.Equal(0L, (long)Product("PaintTileTransfer").GetProperty("StagingBytes", S).GetValue(null), "Cancellation releases staging textures");
            test.Equal(0, (int)Get(step, "completedWrites"), "Cancelled data never enters a journal writer");
        }
        finally
        {
            if (step != null) { Set(step, "cancelled", true); Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "DeleteStep", step); }
            if (history != null) Object.DestroyImmediate(history);
            Object.DestroyImmediate(target); RenderTexture.active = previous;
        }
    }

    static object Settings(Color color)
    {
        var settings = Activator.CreateInstance(Product("PaintToolSettings"), true);
        Set(settings, "brushSize", 110f); Set(settings, "brushColor", color); Set(settings, "brushHardness", .6f);
        return settings;
    }
    static void Stroke(DrawingLayerBehaviour drawing, Vector2 point, Color color)
    {
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        var settings = Settings(color);
        Call(drawing, "PrepareBufferedStroke", 256, 256, "Owned sparse history stroke");
        Call(drawing, "BeginStroke", point);
        Call(drawing, "PaintPoint", point, 256, 256, Call(settings, "GetStrokeParameters", false, (Color?)null));
        Call(drawing, "EndStroke");
        Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
    }

    public static string Run() => TestContext.Run("Tiled Drawing exact history and deleted transfer destinations", test =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 256;
        DrawingLayerBehaviour drawing = null; Texture2D original = null; Object cursor = null;
        var logs = new List<string>();
        Application.LogCallback logger = (text, stack, type) => { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) logs.Add(text); };
        Application.logMessageReceived += logger;
        try
        {
            const string label = "Owned tiled new Drawing history";
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(label);
            original = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            original.SetPixels(new Color[256 * 256]); original.Apply(false, false);
            Undo.RegisterCreatedObjectUndo(original, label);
            // Creating a native object flushes existing Undo recordings. Register
            // the document afterwards so its Redo snapshot includes the layer.
            Undo.RegisterCompleteObjectUndo(document, label);
            drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            Call(drawing, "AdoptStoredTexture", original);
            document.layers.Add(drawing); Call(document, "NormalizeModel");
            var settings = Activator.CreateInstance(Product("PaintToolSettings"), true);
            Set(settings, "brushSize", 110f); Set(settings, "brushColor", new Color(3.25f, -.125f, .75f, .7f));
            Set(settings, "brushHardness", .6f);
            var parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
            Call(drawing, "PrepareBufferedStroke", 256, 256, label);
            Call(drawing, "BeginStroke", new Vector2(-.12f, .4f));
            Call(drawing, "PaintPoint", new Vector2(-.12f, .4f), 256, 256, parameters);
            Call(drawing, "EndStroke");
            cursor = (Object)Get(drawing, "PixelUndoObject");
            var region = new RectInt(-256, 0, 512, 256);
            byte[] expected = Probe(drawing, region);
            test.True((int)Product("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null) > 0, "Undo starts while immutable sparse transfer is queued");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            test.Equal(0, document.layers.Count, "Immediate Undo removes the newly created Drawing");
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            test.Equal(0, logs.Count, "Destroyed native destinations do not invalidate queued sparse history");
            test.True((bool)Get(cursor, "HasLiveHistory"), "Native Redo records retain a temporarily destroyed source");
            Static("PaintHistoryStore", "RemoveUnusedHistories");
            Undo.PerformRedo();
            test.Equal(1, document.layers.Count, "Redo restores the Drawing");
            drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Call(drawing, "ReconcilePixelUndo");
            Exact(test, expected, Probe(drawing, region), "New layer Redo retains signed HDR and overflow pixels exactly");
            for (int i = 0; i < 3; i++)
            {
                Undo.PerformUndo(); test.Equal(0, document.layers.Count, "Repeated Undo removes the layer");
                Undo.PerformRedo(); drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour;
                Call(drawing, "ReconcilePixelUndo");
                Exact(test, expected, Probe(drawing, region), "Repeated native Redo " + i);
            }
            test.Equal(0, logs.Count, "No history runtime errors");
        }
        finally
        {
            Application.logMessageReceived -= logger;
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            if (original != null) Undo.ClearUndo(original);
            Undo.ClearUndo(document);
            if (drawing != null) Call(drawing, "ReleaseTransientResources");
            document.layers.Clear(); Object.DestroyImmediate(document);
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    });

    public static string RunRepaintAfterEmptyUndo() => TestContext.Run("Repaint an empty Drawing after Undo", test =>
    {
        foreach (bool pencil in new[] { false, true })
        {
            var document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 256;
            document.layers.Add(new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR });
            Call(document, "NormalizeModel");
            DrawingLayerBehaviour Drawing() => (DrawingLayerBehaviour)document.layers[0].Behaviour;
            using var cache = (IDisposable)Activator.CreateInstance(Product("EffectRenderCache"), true);
            var viewport = Activator.CreateInstance(Product("CanvasRenderRegion"), F, null,
                new object[] { new RectInt(0, 0, 256, 256), 1 }, null);
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            Object cursor = null;
            int strokeGroup = 0;
            try
            {
                Call(Drawing(), "InitializeCanvas", 256, 256);
                Call(document, "ResetUndoTrackingAfterLoad");
                test.True(!(bool)Get(Drawing(), "HasTiledSource"), "Empty layer starts with its original storage mode");
                var empty = Canvas(false);
                var left = new Vector2(.25f, .5f);
                var right = new Vector2(.75f, .5f);

                Begin(left, new Color(3f, -.25f, .1f, .75f));
                test.True(Canvas(true)[128 * 256 + 64].r > 1, "First HDR mark is visible while painting");
                Finish();
                var first = Canvas(false);
                test.True(first[128 * 256 + 64].r > 1, "First mark is visible after commit");

                Undo.PerformUndo();
                SameCanvas(empty, Canvas(false), "Undo restores a transparent canvas");
                Undo.PerformRedo();
                SameCanvas(first, Canvas(false), "Redo restores the first mark");
                Undo.PerformUndo();
                SameCanvas(empty, Canvas(false), "Second Undo returns to the untouched layer");

                Begin(right, new Color(-.25f, 4f, .1f, .75f));
                var pending = Canvas(true);
                test.True(pending[128 * 256 + 192].g > 1, "New mark is visible immediately after Undo");
                test.Equal(Color.clear, pending[128 * 256 + 64], "Abandoned mark stays gone");
                Finish();
                var committed = Canvas(false);
                SameCanvas(pending, committed, "Commit keeps the visible result");
                Undo.PerformUndo();
                SameCanvas(empty, Canvas(false), "Undo of the new branch restores empty pixels");
                Undo.PerformRedo();
                SameCanvas(committed, Canvas(false), "Redo of the new branch restores its pixels");

                void Begin(Vector2 point, Color color)
                {
                    const string label = "Owned repaint after empty Undo";
                    Undo.IncrementCurrentGroup();
                    strokeGroup = Undo.GetCurrentGroup();
                    Undo.RecordObject(document, label);
                    var drawing = Drawing();
                    var settings = Settings(color);
                    Set(settings, "pencilSize", 9);
                    Call(drawing, "PrepareBufferedStroke", 256, 256, label);
                    Call(drawing, "BeginStroke", point);
                    var parameters = Call(settings, pencil ? "GetPencilParameters" : "GetStrokeParameters", false, (Color?)null);
                    Call(drawing, "PaintPoint", point, 256, 256, parameters);
                }

                void Finish()
                {
                    var drawing = Drawing();
                    Call(drawing, "EndStroke");
                    cursor = (Object)Get(drawing, "PixelUndoObject");
                    Call(drawing, "FinishStrokeStorage");
                    Call(document, "MarkChanged");
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(strokeGroup);
                    Undo.IncrementCurrentGroup();
                }

                Color[] Canvas(bool painting)
                {
                    var target = (RenderTexture)Call(document, "RenderCanvasViewport", viewport, cache, painting, painting ? Drawing() : null);
                    Texture2D read = null;
                    try
                    {
                        read = (Texture2D)Static("HdrUtility", "ReadLinear", target, false);
                        return read.GetPixels();
                    }
                    finally
                    {
                        if (read != null) Object.DestroyImmediate(read);
                        Static("ProfiledRenderTexture", "ReleaseTemporary", target);
                    }
                }

                void SameCanvas(Color[] expected, Color[] actual, string label)
                    => test.True(System.Linq.Enumerable.SequenceEqual(expected, actual), (pencil ? "Pencil: " : "Brush: ") + label);
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                Call(Drawing(), "CancelStroke");
                Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
                if (cursor != null) Undo.ClearUndo(cursor);
                Undo.ClearUndo(document);
                Call(Drawing(), "ReleaseTransientResources");
                document.layers.Clear(); Object.DestroyImmediate(document);
            }
        }
    });

    static byte[] Probe(DrawingLayerBehaviour drawing, RectInt region)
    {
        Call(drawing, "EnsureTiledSource", 256, 256);
        var surface = (TiledPaintSurface)Get(drawing, "tiledSurface");
        var target = new RenderTexture(region.width, region.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        if (!target.Create()) throw new InvalidOperationException("Owned history probe target could not be created.");
        var read = new Texture2D(region.width, region.height, TextureFormat.RGBAHalf, false, true);
        var previous = RenderTexture.active;
        try
        {
            surface.CopyRegion(region, target);
            RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, region.width, region.height), 0, 0, false);
            return read.GetRawTextureData<byte>().ToArray();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(read); target.Release(); Object.DestroyImmediate(target); }
    }

    static void Exact(TestContext test, byte[] expected, byte[] actual, string label)
    {
        test.Equal(expected.Length, actual.Length, label + " length");
        int difference = 0; for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) difference++;
        test.Equal(0, difference, label + " raw Half bytes");
    }
}
