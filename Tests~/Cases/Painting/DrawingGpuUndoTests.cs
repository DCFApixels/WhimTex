using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    [Serializable] public sealed class GpuUndoSample
    {
        public string action;
        public int cycle, tiles, retained;
        public double undoMs, rebuildMs, readyMs;
        public long gpuBytes;
    }
    [Serializable] public sealed class GpuUndoReport
    {
        public string label, unity, device, fixtureHash, historyHash, surfaceHash;
        public int diameter; public bool inside;
        public string[] stateHashes;
        public List<GpuUndoSample> samples = new();
    }
    static string GpuHash(byte[] data)
    { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    static string GpuFileHash(string path) => GpuHash(File.ReadAllBytes("Packages/com.dcfapixels.whimtex/" + path));
    static void GpuPreflight(TestContext test)
    {
        test.Equal(0, (int)Product("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null), "No user transfers");
        test.True(!(bool)Static("PaintHistoryStore", "HasActiveStroke"), "No user stroke");
        foreach (var history in Resources.FindObjectsOfTypeAll(Product("DrawingPixelHistory")))
            test.True(!(bool)Get(history, "HasLiveHistory") && string.IsNullOrEmpty((string)Get(history, "Revision")), "Preserve existing user history");
        test.True(new DriveInfo(Path.GetPathRoot(Application.dataPath)).AvailableFreeSpace > 4L * 1024 * 1024 * 1024, "At least 4 GiB for owned 8K history");
    }
    public static string RunGpuUndo8K(int diameter, bool inside, string label, bool incremental) => TestContext.Run("Partial GPU Undo 8K " + diameter + " " + inside, test =>
    {
        if (diameter != 2048 && diameter != 4096) throw new ArgumentOutOfRangeException(nameof(diameter));
        if (label != "before" && label != "after") throw new ArgumentException(nameof(label));
        GpuPreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        const int side = 8192;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = side;
        var drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        document.layers.Add(drawing); Call(document, "NormalizeModel");
        document.layers[0].transform.position = inside ? Vector2.zero : new Vector2(6144, 0);
        var mask = new Texture2D(side, side, TextureFormat.R8, false, true) { hideFlags = HideFlags.HideAndDontSave };
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; Object cursor = null;
        var report = new GpuUndoReport { label = label, diameter = diameter, inside = inside,
            unity = Application.unityVersion, device = SystemInfo.graphicsDeviceType.ToString(),
            fixtureHash = GpuFileHash("Tests~/Cases/Painting/DrawingGpuUndoTests.cs"),
            historyHash = GpuFileHash("src/Layers/DrawingLayerBehaviour.TiledHistory.cs"),
            surfaceHash = GpuFileHash("src/Layers/TiledPaint/TiledPaintSurface.cs") };
        try
        {
            var data = mask.GetRawTextureData<byte>();
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(32 + (long)(i % side) * 223 / (side - 1));
            mask.Apply(false, false);
            Call(drawing, "InitializeCanvas", side, side); Call(drawing, "EnsureTiledSource", side, side);
            var centers = new[] { new Vector2(.25f, .28f), new Vector2(.72f, .73f), new Vector2(.47f, .50f) };
            var states = new List<Dictionary<Vector2Int, string>>();
            for (int i = 0; i < centers.Length; i++)
            {
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                var settings = Settings(new Color(-.4f, 2.5f + i, .3f, .65f)); Set(settings, "brushSize", (float)diameter);
                var parameters = Call(Call(settings, "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", mask);
                var p = centers[i] - (inside ? Vector2.zero : new Vector2(.75f, 0));
                Call(drawing, "PrepareBufferedStroke", side, side, "Owned GPU Undo benchmark");
                Call(drawing, "BeginStroke", p); Call(drawing, "PaintPoint", p, side, side, parameters);
                Call(drawing, "PaintSegment", p, p + new Vector2(.025f, .0125f), side, side, false, parameters);
                Call(drawing, "EndStroke"); Call(drawing, "FlushTiledStorage"); Static("PaintHistoryStore", "FlushWrites");
                cursor = (Object)Get(drawing, "PixelUndoObject");
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                states.Add(GpuStorageHashes(drawing));
            }
            report.stateHashes = states.ConvertAll(GpuStateHash).ToArray();
            GpuCheckTiles(test, drawing, states[2]);
            Call(document, "ResetUndoTrackingAfterLoad");
            for (int cycle = 0; cycle < 4; cycle++)
            {
                Measure(false, states[1], cycle); Measure(true, states[2], cycle);
            }
            void Measure(bool redo, Dictionary<Vector2Int, string> expected, int cycle)
            {
                var surface = (TiledPaintSurface)Get(drawing, "tiledSurface");
                var addresses = GpuAddresses(surface);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (redo) Undo.PerformRedo(); else Undo.PerformUndo();
                Call(drawing, "ReconcilePixelUndo"); double undo = watch.Elapsed.TotalMilliseconds;
                Call(drawing, "EnsureTiledSource", side, side); double rebuilt = watch.Elapsed.TotalMilliseconds;
                surface = (TiledPaintSurface)Get(drawing, "tiledSurface");
                using (var snapshot = surface.AcquireSnapshot())
                {
                    foreach (var tile in snapshot.Tiles) { Probe(drawing, new RectInt(tile.Key * 256, Vector2Int.one)); break; }
                }
                double ready = watch.Elapsed.TotalMilliseconds;
                int retained = 0; foreach (var pair in GpuAddresses(surface))
                    if (addresses.TryGetValue(pair.Key, out var address) && address == pair.Value) retained++;
                if (incremental) test.True(retained > 0, "Undo preserves untouched GPU slots");
                test.True(surface.AllocatedBytes <= 2L * 1024 * 1024 * 1024, "Unchanged 2 GiB GPU budget");
                report.samples.Add(new GpuUndoSample { action = redo ? "redo" : "undo", cycle = cycle,
                    undoMs = undo, rebuildMs = rebuilt - undo, readyMs = ready, tiles = surface.TileCount,
                    retained = retained, gpuBytes = surface.AllocatedBytes });
                test.Equal(GpuStateHash(expected), GpuStateHash(GpuStorageHashes(drawing)), "Exact CPU Half history");
                // Full GPU-byte checks are outside the timed region.
                if (cycle == 3) GpuCheckTiles(test, drawing, expected);
            }
        }
        finally
        {
            string folder = "Temp/WhimTex/gpu-undo"; Directory.CreateDirectory(folder);
            var json = (string)System.Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
                .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            File.WriteAllText(Path.Combine(folder, label + "-" + diameter + "-" + inside + "-" + Guid.NewGuid().ToString("N") + ".json"), json);
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Undo.ClearUndo(document); Call(drawing, "ReleaseTransientResources"); document.layers.Clear();
            Object.DestroyImmediate(document); Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    });
    static Dictionary<Vector2Int, string> GpuStorageHashes(DrawingLayerBehaviour drawing)
    {
        var result = new Dictionary<Vector2Int, string>();
        foreach (var tile in (IEnumerable)Get(drawing, "TiledPixels"))
            result.Add((Vector2Int)Get(tile, "key"), GpuHash(((Texture2D)Get(tile, "texture")).GetRawTextureData<byte>().ToArray()));
        return result;
    }
    static string GpuStateHash(Dictionary<Vector2Int, string> state)
    {
        var keys = new List<Vector2Int>(state.Keys); keys.Sort((a,b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        var text = new System.Text.StringBuilder(); foreach (var key in keys) text.Append(key.x).Append(',').Append(key.y).Append(':').Append(state[key]).Append(';');
        return GpuHash(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
    }
    static Dictionary<Vector2Int, (Texture, RectInt)> GpuAddresses(TiledPaintSurface surface)
    {
        var result = new Dictionary<Vector2Int, (Texture, RectInt)>();
        using var snapshot = surface.AcquireSnapshot();
        foreach (var tile in snapshot.Tiles) result.Add(tile.Key, (tile.Texture, tile.PhysicalRect));
        return result;
    }
    static void GpuCheckTiles(TestContext test, DrawingLayerBehaviour drawing, Dictionary<Vector2Int, string> expected)
    {
        var surface = (TiledPaintSurface)Get(drawing, "tiledSurface");
        test.Equal(expected.Count, surface.TileCount, "No stale or missing GPU tiles");
        var target = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var read = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true); var previous = RenderTexture.active;
        try
        {
            target.Create(); using var snapshot = surface.AcquireSnapshot();
            foreach (var pair in expected)
            {
                test.True(snapshot.TryGetTile(pair.Key, out var tile), "GPU tile exists " + pair.Key);
                var r = tile.PhysicalRect; RenderTexture.active = null;
                Graphics.CopyTexture(tile.Texture, 0, 0, r.x, r.y, 256, 256, target, 0, 0, 0, 0);
                RenderTexture.active = target; read.ReadPixels(new Rect(0,0,256,256), 0, 0, false);
                test.Equal(pair.Value, GpuHash(read.GetRawTextureData<byte>().ToArray()), "Exact GPU Half " + pair.Key);
            }
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(target); Object.DestroyImmediate(read); }
    }
}
