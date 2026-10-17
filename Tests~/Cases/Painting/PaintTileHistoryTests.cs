using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Detached, owned fixtures; no windows or input. Native Undo is scoped to owned objects.
public static class PaintTileHistoryTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static Type Type(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    static object Static(string type, string name, params object[] args) => Type(type).GetMethod(name, S).Invoke(null, args);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object New(string name) => Activator.CreateInstance(Type(name), true);
    static IDisposable Phase(string name) => (IDisposable)Static("WhimTexProfiler", "Measure", name, null, null, -1, null);
    // The Editor contains two public JsonConvert types; select the package assembly explicitly.
    static string Json(object value) => (string)System.Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    public static string RunCodecs() => TestContext.Run("Portable journal codec comparison", t =>
    {
        var records = new System.Collections.Generic.List<string>();
        foreach (string pattern in new[] { "zero", "smooth", "noise" })
        {
            byte[] source = new byte[8 * 1024 * 1024]; var random = new System.Random(937);
            if (pattern == "noise") random.NextBytes(source);
            if (pattern == "smooth") for (int i = 0; i < source.Length; i += 4)
            { source[i] = (byte)(i / 4096); source[i + 1] = (byte)(i / 16); source[i + 3] = 127; }
            foreach (string codec in new[] { "gzip", "deflate" })
            {
                using var packed = new MemoryStream(); var clock = Stopwatch.StartNew();
                using (Stream writer = Codec(codec, packed, true)) writer.Write(source, 0, source.Length);
                double encode = clock.Elapsed.TotalMilliseconds; packed.Position = 0; byte[] decoded = new byte[source.Length]; clock.Restart();
                using (Stream reader = Codec(codec, packed, false))
                { int at = 0, count; while ((count = reader.Read(decoded, at, decoded.Length - at)) > 0) at += count; t.Equal(source.Length, at, "Decoded length"); }
                double decode = clock.Elapsed.TotalMilliseconds; Exact(t, source, decoded, pattern + " " + codec);
                records.Add(pattern + " " + codec + " encode=" + encode + " decode=" + decode + " bytes=" + packed.Length);
            }
        }
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history")); Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "codecs.txt"), records);
    });
    static Stream Codec(string codec, Stream stream, bool write)
    {
        if (write)
        {
            var level = System.IO.Compression.CompressionLevel.Fastest;
            if (codec == "gzip") return new System.IO.Compression.GZipStream(stream, level, true);
            return new System.IO.Compression.DeflateStream(stream, level, true);
        }
        var mode = System.IO.Compression.CompressionMode.Decompress;
        if (codec == "gzip") return new System.IO.Compression.GZipStream(stream, mode, true);
        return new System.IO.Compression.DeflateStream(stream, mode, true);
    }
    sealed class Fixture : IDisposable
    {
        internal readonly WhimTexDocument document;
        internal readonly DrawingLayerBehaviour layer;
        internal readonly Texture2D pixels;
        internal readonly RenderTexture surface;
        internal readonly object settings, dynamics;
        internal readonly int size;
        internal Fixture(int size, bool hdr, bool textured = false)
        {
            this.size = size;
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = size;
            layer = new DrawingLayerBehaviour { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard,
                blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard };
            pixels = new Texture2D(size, size, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr) { hideFlags = HideFlags.HideAndDontSave };
            var raw = pixels.GetRawTextureData<ulong>(); ulong noise = 937;
            for (int i = 0; i < raw.Length; i++)
            {
                if (textured) { noise ^= noise << 13; noise ^= noise >> 7; noise ^= noise << 17; }
                raw[i] = textured ? (noise & 0x00FFFFFF00FFFFFFUL) | 0xFF000000FF000000UL : 0;
            }
            pixels.Apply(false, false); // Exactly one full-size upload per large invocation.
            Call(layer, "AdoptStoredTexture", pixels); document.layers.Add(layer); Call(document, "NormalizeModel");
            surface = (RenderTexture)Call(layer, "EnsurePaintSurface", size, size);
            settings = New("PaintToolSettings"); dynamics = Get(settings, "dynamics");
            Set(settings, "brushColor", hdr ? new Color(-.4f, 2f, .8f, 1f) : new Color(.7f, .3f, .1f, 1));
            Set(settings, "brushSize", 32f); Set(settings, "brushHardness", .3f); Set(settings, "brushSpacing", .25f);
            Set(dynamics, "flow", .13f); Set(dynamics, "opacity", .6f);
        }
        internal object Parameters(Texture mask, bool erase = false)
        {
            object p = Call(settings, "GetStrokeParameters", erase, (Color?)null);
            return mask != null ? Call(p, "WithSelectionMask", mask) : p;
        }
        internal void Stroke(Vector2[] points, Texture mask, bool history = true, bool erase = false, bool drain = true)
        {
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            using (Phase("test.paint.prepare"))
            if (history) Call(layer, "PrepareBufferedStroke", size, size, "Owned pixel history test");
            using (Phase("test.paint.draw"))
            {
            Call(layer, "BeginStroke", points[0]); object p = Parameters(mask, erase);
            Call(layer, "PaintPoint", points[0], size, size, p);
            for (int i = 1; i < points.Length; i++) Call(layer, "PaintSegment", points[i - 1], points[i], size, size, false, p);
            }
            using (Phase("test.paint.end"))
            Call(layer, "EndStroke");
            if (!history || drain) Call(layer, "SyncSurfaceToTexture");
            using (Phase("test.paint.finalize"))
            { Call(document, "MarkChanged"); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); }
        }
        public void Dispose()
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            var cursor = (Object)Get(layer, "PixelUndoObject");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Undo.ClearUndo(pixels); Undo.ClearUndo(document);
            if (document.layers.Count > 0 && document.layers[0].Behaviour != layer) Call(document.layers[0].Behaviour, "ReleaseTransientResources");
            Call(layer, "ReleaseTransientResources"); document.layers.Clear(); Object.DestroyImmediate(document);
        }
    }
    static byte[] Probe(RenderTexture source, RectInt rect)
    {
        var previous = RenderTexture.active;
        var image = new Texture2D(rect.width, rect.height, TextureFormat.RGBAHalf, false, true);
        try { RenderTexture.active = source; image.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0, false); return image.GetRawTextureData<byte>().ToArray(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    static void Exact(TestContext t, byte[] a, byte[] b, string label)
    {
        t.Equal(a.Length, b.Length, label + " length");
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) throw new Exception(label + " mismatch at " + i + ": " + a[i] + "/" + b[i]);
        t.True(true, label);
    }
    static Texture2D Mask()
    {
        var result = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
        var colors = new Color32[1024]; for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(64, 0, 0, 255);
        result.SetPixels32(colors); result.Apply(false, false); return result;
    }
    static int restoredBlocks;
    static T Transfer<T>(string name) => (T)Type("PaintTileTransfer").GetProperty(name, S).GetValue(null);
    public static string RunQueued() => TestContext.Run("Queued strokes preserve exact revision order and bounded staging", t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; var mask = Mask();
        try
        {
            foreach (bool hdr in new[] { false, true })
            {
                using var f = new Fixture(1536, hdr);
                Set(f.settings, "brushSize", 1536f);
                var rect = new RectInt(0, 0, 1536, 1536);
                var halves = new byte[7][]; var stored = new byte[7][];
                halves[0] = Probe(f.surface, rect); stored[0] = f.pixels.GetRawTextureData<byte>().ToArray();
                long waits = Transfer<long>("CapacityWaits"), transferred = Transfer<long>("TransferredBytes");
                for (int i = 1; i <= 6; i++)
                {
                    Set(f.dynamics, "flow", i % 2 == 0 ? .0001f : .13f);
                    f.Stroke(new[] { new Vector2(.5f, .5f) }, mask, erase: i == 3, drain: false);
                    if (i <= 3)
                    {
                        t.Equal(waits, Transfer<long>("CapacityWaits"), "First three wide strokes never wait for queue capacity");
                        t.Equal(transferred, Transfer<long>("TransferredBytes"), "Stroke completion does not copy CPU history");
                    }
                    t.True(Transfer<long>("StagingBytes") <= 256L * 1024 * 1024, "GPU staging remains within budget");
                    halves[i] = Probe(f.surface, rect);
                    var request = UnityEngine.Rendering.AsyncGPUReadback.Request(f.pixels, 0); request.WaitForCompletion();
                    t.True(!request.hasError, "Independent GPU reference for queued revision");
                    stored[i] = request.GetData<byte>().ToArray();
                }
                t.True(Transfer<long>("CapacityWaits") > waits, "Sustained overload exercises backpressure");
                t.True(Transfer<int>("PendingCount") > 2, "Several immutable pages remain pending before immediate Undo");
                for (int i = 5; i >= 0; i--)
                {
                    Undo.PerformUndo();
                    Exact(t, stored[i], f.pixels.GetRawTextureData<byte>().ToArray(), "Queued Undo storage revision " + i);
                    Exact(t, halves[i], Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Queued Undo exact Half revision " + i);
                }
                for (int i = 1; i <= 6; i++)
                {
                    Undo.PerformRedo();
                    Exact(t, stored[i], f.pixels.GetRawTextureData<byte>().ToArray(), "Queued Redo storage revision " + i);
                    Exact(t, halves[i], Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Queued Redo exact Half revision " + i);
                }
                t.Equal(0L, Transfer<long>("StagingBytes"), "Synchronization releases all staged pages");
            }
        }
        finally { Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
    public static string RunRaw() => TestContext.Run("Uncompressed journal keeps exact patterned SDR/HDR pixels", t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; var mask = Mask();
        try
        {
            foreach (bool hdr in new[] { false, true })
            {
                using var f = new Fixture(513, hdr); using var reference = new Fixture(513, hdr);
                var pattern = new Color[513 * 513]; var random = new System.Random(931);
                for (int i = 0; i < pattern.Length; i++) pattern[i] = new Color(
                    (float)random.NextDouble() * (hdr ? 4 : 1) - (hdr ? 1 : 0),
                    (float)random.NextDouble() * (hdr ? 4 : 1), (float)random.NextDouble(), .5f + .5f * (float)random.NextDouble());
                foreach (var fixture in new[] { f, reference })
                {
                    fixture.pixels.SetPixels(pattern); fixture.pixels.Apply(false, false); Call(fixture.layer, "InvalidatePaintSurface");
                    Call(fixture.layer, "EnsurePaintSurface", 513, 513); Set(fixture.settings, "brushSize", 400f);
                }
                var rect = new RectInt(0, 0, 513, 513);
                var before = Probe((RenderTexture)Get(f.layer, "paintSurface"), rect); var stored = f.pixels.GetRawTextureData<byte>().ToArray();
                var points = new[] { new Vector2(.4f, .4f), new Vector2(.9f, .9f) };
                f.Stroke(points, mask); reference.Stroke(points, mask, false);
                var after = Probe((RenderTexture)Get(f.layer, "paintSurface"), rect); var afterStored = f.pixels.GetRawTextureData<byte>().ToArray();
                Exact(t, Probe((RenderTexture)Get(reference.layer, "paintSurface"), rect), after, "Raw history does not change compute result HDR=" + hdr);
                Static("PaintHistoryStore", "FlushWrites");
                string revision = (string)Get(Get(f.layer, "PixelUndoObject"), "Revision");
                string root = (string)Type("PaintHistoryStore").GetProperty("Root", S).GetValue(null);
                using (var file = File.OpenRead(Path.Combine(root, revision + "-0.bin")))
                using (var reader = new BinaryReader(file)) t.Equal(0x57545034, reader.ReadInt32(), "Detailed source chooses raw journal page");
                RejectDamagedPage(t, f);
                Undo.PerformUndo(); Exact(t, stored, f.pixels.GetRawTextureData<byte>().ToArray(), "Raw Undo stored pixels");
                Exact(t, before, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Raw Undo Half pixels");
                Undo.PerformRedo(); Exact(t, afterStored, f.pixels.GetRawTextureData<byte>().ToArray(), "Raw Redo stored pixels");
                Exact(t, after, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Raw Redo Half pixels");
            }
        }
        finally { Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
    static void CountRestoredBlock(object block, bool forward) { restoredBlocks++; }
    static void RejectDamagedPage(TestContext t, Fixture fixture)
    {
        using var profile = Phase("test.paint.integrity");
        var cursor = Get(fixture.layer, "PixelUndoObject"); string revision = (string)Get(cursor, "Revision");
        var step = Static("PaintHistoryStore", "Get", revision);
        string root = (string)Type("PaintHistoryStore").GetProperty("Root", S).GetValue(null);
        string file = Path.Combine(root, revision + "-" + ((int)Get(step, "pages") - 1) + ".bin");
        byte[] original = File.ReadAllBytes(file);
        var restore = cursor.GetType().GetMethod("Restore", F);
        var callback = Delegate.CreateDelegate(restore.GetParameters()[0].ParameterType,
            typeof(PaintTileHistoryTests).GetMethod("CountRestoredBlock", S));
        try
        {
            for (int damage = 0; damage < 2; damage++)
            {
                var damaged = (byte[])original.Clone();
                if (damage == 0) damaged[damaged.Length - 1] ^= 0x80;
                else damaged[0] ^= 7; // Swap the valid raw/Deflate markers without changing the payload checksum.
                File.WriteAllBytes(file, damaged); Set(cursor, "revision", (string)Get(step, "parent")); restoredBlocks = 0;
                bool rejected = false;
                try { restore.Invoke(cursor, new object[] { callback }); }
                catch (TargetInvocationException error) when (error.InnerException is IOException || error.InnerException is InvalidDataException) { rejected = true; }
                t.True(rejected, "Damaged final journal page or encoding rejected");
                t.Equal(0, restoredBlocks, "No block applied before all journal pages pass integrity checks");
            }
        }
        finally { File.WriteAllBytes(file, original); Set(cursor, "revision", revision); }
    }
    public static string RunSmall() => TestContext.Run("Block coverage, exact Half Undo, partial storage, disk journal", t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; var mask = Mask();
        try
        {
            foreach (bool hdr in new[] { false, true })
            {
                using var f = new Fixture(768, hdr); using var reference = new Fixture(768, hdr);
                var rect = new RectInt(0, 0, 768, 768);
                var before = Probe(f.surface, rect); var beforeStored = f.pixels.GetRawTextureData<byte>().ToArray();
                var points = new[] { new Vector2(.08f, .08f), new Vector2(.08f, .9f), new Vector2(.9f, .9f) };
                f.Stroke(points, mask); reference.Stroke(points, mask, false);
                Exact(t, Probe(reference.surface, rect), Probe(f.surface, rect), "Partial compute equals ordinary compute, HDR=" + hdr);
                var after = Probe(f.surface, rect); var afterStored = f.pixels.GetRawTextureData<byte>().ToArray();
                Exact(t, reference.pixels.GetRawTextureData<byte>().ToArray(), afterStored, "Partial straight conversion equals full conversion");
                var gpuStored = UnityEngine.Rendering.AsyncGPUReadback.Request(f.pixels, 0);
                gpuStored.WaitForCompletion(); t.True(!gpuStored.hasError, "Stored GPU texture readback");
                Exact(t, afterStored, gpuStored.GetData<byte>().ToArray(), "Stored CPU and GPU data agree");
                t.True((int)Get(f.layer, "LastPaintTileCount") < 9, "L-shaped stroke does not fill its bounding box");
                Static("PaintHistoryStore", "FlushWrites");
                RejectDamagedPage(t, f);
                // Discard the journal's RAM metadata too: restore must read its files.
                ((System.Collections.IDictionary)Type("PaintHistoryStore").GetField("steps", S).GetValue(null)).Clear();
                Undo.PerformUndo();
                Exact(t, beforeStored, f.pixels.GetRawTextureData<byte>().ToArray(), "Native Undo restores stored pixels");
                Exact(t, before, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Native Undo restores exact Half surface");
                Undo.PerformRedo();
                using var edge = new Fixture(518, hdr);
                var pattern = new Color[518 * 518];
                for (int y = 0; y < 518; y++) for (int x = 0; x < 518; x++)
                    pattern[y * 518 + x] = new Color(hdr ? -.31f : .31f, hdr ? 1.7f : .7f, x / 517f, x % 5 == 0 ? 0f : .001f + (y % 100) * .009f);
                edge.pixels.SetPixels(pattern); edge.pixels.Apply(false, false); Call(edge.layer, "InvalidatePaintSurface");
                var edgeRect = new RectInt(0, 0, 518, 518);
                var oldHalf = Probe((RenderTexture)Call(edge.layer, "EnsurePaintSurface", 518, 518), edgeRect);
                var oldBytes = edge.pixels.GetRawTextureData<byte>().ToArray();
                edge.Stroke(new[] { new Vector2(.48f, .48f), new Vector2(.99f, .99f) }, mask, erase: true);
                var newHalf = Probe((RenderTexture)Get(edge.layer, "paintSurface"), edgeRect);
                var newBytes = edge.pixels.GetRawTextureData<byte>().ToArray();
                int untouched = 0, bpp = hdr ? 8 : 4;
                for (int pixel = 0; pixel < pattern.Length; pixel++)
                {
                    bool same = true; for (int channel = 0; channel < 8; channel++) same &= oldHalf[pixel * 8 + channel] == newHalf[pixel * 8 + channel];
                    if (!same) continue;
                    untouched++;
                    for (int channel = 0; channel < bpp; channel++) t.Equal(oldBytes[pixel * bpp + channel], newBytes[pixel * bpp + channel], "Untouched stored texel including hidden RGB");
                }
                t.True(untouched > 100000, "Untouched checks cover partial tiles and nonuniform source");
                Undo.PerformUndo(); Exact(t, oldBytes, edge.pixels.GetRawTextureData<byte>().ToArray(), "Patterned partial-edge eraser Undo");
                Exact(t, afterStored, f.pixels.GetRawTextureData<byte>().ToArray(), "Native Redo restores stored pixels");
                Exact(t, after, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Native Redo restores exact Half surface");
                Set(f.dynamics, "flow", .0001f); Set(reference.dynamics, "flow", .0001f);
                var shortPaths = new[] {
                    new[] { new Vector2(.4f, .4f) },
                    new[] { new Vector2(.31f, .4f), new Vector2(.45f, .4f) },
                    new[] { new Vector2(.31f, .4f), new Vector2(.68f, .4f) } };
                for (int i = 0; i < 12; i++)
                {
                    long readBytes = Transfer<long>("ReadbackBytes");
                    f.Stroke(shortPaths[i % 3], mask, drain: false);
                    int tiles = i % 3 + 1;
                    t.Equal(tiles, (int)Get(f.layer, "LastPaintTileCount"), "Compact single-row tile coverage");
                    t.Equal((long)tiles * 256 * 256 * (hdr ? 24 : 20), Transfer<long>("ReadbackBytes") - readBytes,
                        "Small pages transfer only occupied columns");
                }
                for (int i = 0; i < 12; i++) reference.Stroke(shortPaths[i % 3], mask, false);
                Call(f.layer, "SyncPendingSurfaceToTexture");
                Exact(t, Probe(reference.surface, rect), Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Queued low-flow strokes retain Half precision");
                t.Equal(0, (int)Type("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null), "Explicit boundary drains pending reads");
                var precise = Probe((RenderTexture)Get(f.layer, "paintSurface"), rect);
                Call(f.layer, "InvalidatePaintSurface");
                Exact(t, precise, Probe((RenderTexture)Call(f.layer, "EnsurePaintSurface", 768, 768), rect), "Surface rebuild retains journal Half precision");
                // Native pixel edits and custom brush history share the Ctrl+Z sequence.
                byte[] nativeBefore = f.pixels.GetRawTextureData<byte>().ToArray();
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(f.pixels, "Owned native pixel edit");
                f.pixels.SetPixel(600, 100, new Color(.2f, .6f, .9f, .8f)); f.pixels.Apply(false, false);
                EditorUtility.SetDirty(f.pixels); Call(f.layer, "InvalidatePaintSurface"); Call(f.document, "MarkChanged"); Undo.IncrementCurrentGroup();
                var nativeAfter = f.pixels.GetRawTextureData<byte>().ToArray();
                f.Stroke(new[] { new Vector2(.8f, .2f) }, mask);
                var brushAfter = f.pixels.GetRawTextureData<byte>().ToArray();
                Undo.PerformUndo(); Exact(t, nativeAfter, f.pixels.GetRawTextureData<byte>().ToArray(), "Brush Undo after native edit");
                Undo.PerformUndo(); Exact(t, nativeBefore, f.pixels.GetRawTextureData<byte>().ToArray(), "Native edit Undo after brush Undo");
                Undo.PerformRedo(); Exact(t, nativeAfter, f.pixels.GetRawTextureData<byte>().ToArray(), "Native edit Redo");
                Undo.PerformRedo(); Exact(t, brushAfter, f.pixels.GetRawTextureData<byte>().ToArray(), "Brush Redo after native edit");
                Undo.PerformUndo();
                f.Stroke(new[] { new Vector2(.2f, .8f) }, mask);
                var branch = f.pixels.GetRawTextureData<byte>().ToArray();
                Undo.PerformUndo(); Exact(t, nativeAfter, f.pixels.GetRawTextureData<byte>().ToArray(), "New history branch Undo");
                Undo.PerformRedo(); Exact(t, branch, f.pixels.GetRawTextureData<byte>().ToArray(), "New history branch Redo");
                f.Stroke(new[] { new Vector2(.7f, .7f) }, mask, drain: false);
                string save = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history/roundtrip-" + Guid.NewGuid().ToString("N") + ".tiff"));
                Directory.CreateDirectory(Path.GetDirectoryName(save));
                Call(f.document, "SyncDrawingLayerTextures"); // The production Save boundary, without creating an imported asset.
                using (var container = (IDisposable)New("WhimTexDocumentContainer"))
                {
                    var model = (byte[])Static("WhimTexDocumentSerializer", "Serialize", f.document, container);
                    Call(container, "Set", "document", model, System.IO.Compression.CompressionLevel.Fastest);
                    File.WriteAllBytes(save, (byte[])Static("WhimTexTiffCarrier", "Write", container, f.pixels, (bool?)null));
                }
                using (var container = (IDisposable)Static("WhimTexTiffCarrier", "OpenContainer", save))
                {
                    var model = (byte[])Call(container, "Get", "document");
                    var loaded = (WhimTexDocument)Get(Static("WhimTexDocumentSerializer", "Deserialize", model, container, typeof(WhimTexDocument), save, false), "Model");
                    try { Exact(t, f.pixels.GetRawTextureData<byte>().ToArray(), ((Texture2D)Get(loaded.layers[0].Behaviour, "StoredTexture")).GetRawTextureData<byte>().ToArray(), "Save boundary and TIFF roundtrip preserve pending pages"); }
                    finally { Object.DestroyImmediate(loaded); }
                }
                // A complete model snapshot can replace the managed Drawing instance.
                Undo.IncrementCurrentGroup(); int modelGroup = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(f.document, "Owned model and pixels");
                var modelBefore = f.pixels.GetRawTextureData<byte>().ToArray();
                f.Stroke(new[] { new Vector2(.3f, .7f) }, mask);
                f.document.layers[0].layerName = "Owned renamed drawing"; EditorUtility.SetDirty(f.document);
                Undo.CollapseUndoOperations(modelGroup); Undo.IncrementCurrentGroup();
                Undo.PerformUndo(); Exact(t, modelBefore, f.pixels.GetRawTextureData<byte>().ToArray(), "Document snapshot plus pixel cursor Undo");
                Undo.PerformRedo();
            }
        }
        finally { Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
    [Serializable] public sealed class Report { public string gpu, api, mode; public bool hdr; public int size, tiles, rawPages, compressedPages; public long changedPixels, bytes, actualReadbackBytes, journalBytes, queuedBytes, capacityWaits; public double commitMs, drainMs, undoMs, redoMs; public double[] repeatCommitMs, repeatDrainMs; }
    const string ReloadKey = "WhimTex.Tests.PaintHistory.Reload";
    const string AsyncKey = "WhimTex.Tests.PaintHistory.Async";
    public static string PrepareAsync() => TestContext.Run("Queue owned readback for Editor updates", t =>
    {
        t.True(SessionState.GetString(AsyncKey, "").Length == 0, "No previous asynchronous fixture remains");
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var f = new Fixture(1536, true); bool keep = false;
        try
        {
            string token = Guid.NewGuid().ToString("N"); f.document.name = "WhimTex async history test " + token;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history", token)); Directory.CreateDirectory(directory);
            Set(f.settings, "brushSize", 1536f);
            for (int i = 0; i < 3; i++) f.Stroke(new[] { new Vector2(.5f, .5f) }, null, drain: false);
            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(f.pixels, 0); request.WaitForCompletion();
            t.True(!request.hasError, "Owned GPU storage readback");
            File.WriteAllBytes(Path.Combine(directory, "expected.bin"), request.GetData<byte>().ToArray());
            t.True((int)Type("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null) > 0, "CPU transfer awaits Editor updates");
            SessionState.SetString(AsyncKey, token); keep = true;
        }
        finally { if (!keep) f.Dispose(); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
    public static string VerifyAsync() => TestContext.Run("Editor updates complete owned readback without explicit drain", t =>
    {
        string token = SessionState.GetString(AsyncKey, ""); t.True(Guid.TryParseExact(token, "N", out _), "Prepared asynchronous fixture");
        WhimTexDocument doc = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexDocument>()) if (candidate.name == "WhimTex async history test " + token) doc = candidate;
        t.True(doc != null, "Owned asynchronous document remains");
        var layer = (DrawingLayerBehaviour)doc.layers[0].Behaviour; var pixels = (Texture2D)Get(layer, "StoredTexture");
        var cursor = (Object)Get(layer, "PixelUndoObject");
        try
        {
            t.Equal(0, (int)Type("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null), "Editor drained transfers without a CPU consumer");
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history", token, "expected.bin"));
            Exact(t, File.ReadAllBytes(file), pixels.GetRawTextureData<byte>().ToArray(), "Asynchronous stored CPU pixels equal GPU bytes");
        }
        finally
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            Undo.ClearUndo(doc); Undo.ClearUndo(pixels); if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Object.DestroyImmediate(doc); SessionState.EraseString(AsyncKey);
        }
    });
    public static string PrepareReload() => TestContext.Run("Prepare owned paint history for a real script reload", t =>
    {
        t.True(SessionState.GetString(ReloadKey, "").Length == 0, "No previous fixture remains");
        var f = new Fixture(768, false); bool keep = false;
        try
        {
            string token = Guid.NewGuid().ToString("N"); f.document.name = "WhimTex paint history test " + token;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history", token)); Directory.CreateDirectory(directory);
            var rect = new RectInt(0, 0, 768, 768);
            File.WriteAllBytes(Path.Combine(directory, "before-half.bin"), Probe(f.surface, rect));
            File.WriteAllBytes(Path.Combine(directory, "before-stored.bin"), f.pixels.GetRawTextureData<byte>().ToArray());
            Set(f.dynamics, "flow", .0001f); f.Stroke(new[] { new Vector2(.35f, .35f), new Vector2(.7f, .7f) }, null, drain: false);
            File.WriteAllBytes(Path.Combine(directory, "after-half.bin"), Probe(f.surface, rect));
            Call(f.layer, "SyncPendingSurfaceToTexture");
            File.WriteAllBytes(Path.Combine(directory, "after-stored.bin"), f.pixels.GetRawTextureData<byte>().ToArray());
            Undo.SetCurrentGroupName("WhimTex reload test " + token);
            SessionState.SetString(ReloadKey, token); keep = true;
            t.True((Object)Get(f.layer, "PixelUndoObject") != null, "Owned cursor survives with the document");
        }
        finally { if (!keep) f.Dispose(); }
    });
    public static string VerifyReload() => TestContext.Run("Paint history after a real script reload", t =>
    {
        string token = SessionState.GetString(ReloadKey, ""); t.True(Guid.TryParseExact(token, "N", out _), "Prepared fixture token");
        WhimTexDocument doc = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexDocument>()) if (candidate.name == "WhimTex paint history test " + token) doc = candidate;
        t.True(doc != null, "Owned document survives script reload");
        var layer = (DrawingLayerBehaviour)doc.layers[0].Behaviour; var pixels = (Texture2D)Get(layer, "StoredTexture");
        var cursor = (Object)Get(layer, "PixelUndoObject");
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history", token));
        var rect = new RectInt(0, 0, 768, 768);
        try
        {
            Exact(t, File.ReadAllBytes(Path.Combine(directory, "after-stored.bin")), pixels.GetRawTextureData<byte>().ToArray(), "Stored pixels survive reload");
            Exact(t, File.ReadAllBytes(Path.Combine(directory, "after-half.bin")), Probe((RenderTexture)Call(layer, "EnsurePaintSurface", 768, 768), rect), "Half precision rebuilt from disk after reload");
            t.Equal("WhimTex reload test " + token, Undo.GetCurrentGroupName(), "No other action has changed the Undo group");
            Undo.PerformUndo();
            Exact(t, File.ReadAllBytes(Path.Combine(directory, "before-stored.bin")), pixels.GetRawTextureData<byte>().ToArray(), "Undo stored pixels after reload");
            Exact(t, File.ReadAllBytes(Path.Combine(directory, "before-half.bin")), Probe((RenderTexture)Get(layer, "paintSurface"), rect), "Undo Half after reload");
            Undo.PerformRedo();
            Exact(t, File.ReadAllBytes(Path.Combine(directory, "after-half.bin")), Probe((RenderTexture)Get(layer, "paintSurface"), rect), "Redo Half after reload");
        }
        finally
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            Undo.ClearUndo(doc); Undo.ClearUndo(pixels); if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Object.DestroyImmediate(doc); SessionState.EraseString(ReloadKey);
        }
    });
    public static string RunLarge(string mode, bool hdr) => TestContext.Run("8K block history " + mode + " HDR=" + hdr, t =>
    {
        t.True(SystemInfo.graphicsMemorySize >= 7000, "8K test requires at least 7 GB reported VRAM");
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; var mask = Mask();
        bool profiling = false;
        try
        {
            bool textured = mode == "large-textured";
            t.True(!textured || !hdr, "Textured performance input is RGBA32");
            using var f = new Fixture(8192, hdr, textured);
            Set(f.settings, "brushSize", mode.StartsWith("large") ? 2048f : 32f);
            float edge = 40f / 8192;
            Vector2[] points = mode == "frame" ? new[] { new Vector2(edge, edge), new Vector2(edge, 1-edge),
                new Vector2(1-edge, 1-edge), new Vector2(1-edge, edge), new Vector2(edge, edge) }
                : new[] { new Vector2(.5f, .5f), new Vector2(.52f, .51f) };
            var probe = mode == "frame" ? new RectInt(0, 0, 256, 256) : new RectInt(3968, 3968, 256, 256);
            var before = Probe(f.surface, probe);
            var report = new Report { gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), mode = mode, hdr = hdr, size = 8192 };
            long bytesBefore = (long)Type("PaintTileTransfer").GetProperty("TransferredBytes", S).GetValue(null);
            long readbackBefore = (long)Type("PaintTileTransfer").GetProperty("ReadbackBytes", S).GetValue(null);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history")); Directory.CreateDirectory(directory);
            string reportPath = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + mode + "-" + hdr);
            t.True(!(bool)Type("WhimTexProfiler").GetProperty("Recording", S).GetValue(null), "No user profiler capture is recording");
            var previousCapture = Type("WhimTexProfiler").GetProperty("Current", S).GetValue(null);
            if (previousCapture != null) File.WriteAllText(reportPath + "-previous-profile.json", Json(previousCapture));
            Static("WhimTexProfiler", "Start", 32768, 60.0, null); profiling = true;
            long waitsBefore = Transfer<long>("CapacityWaits");
            var clock = Stopwatch.StartNew(); f.Stroke(points, mask, drain: false); report.commitMs = clock.Elapsed.TotalMilliseconds;
            report.queuedBytes = Transfer<long>("StagingBytes"); report.capacityWaits = Transfer<long>("CapacityWaits") - waitsBefore;
            t.Equal(0L, report.capacityWaits, "A single measured stroke fits the asynchronous staging budget");
            t.Equal(bytesBefore, Transfer<long>("TransferredBytes"), "Completion leaves CPU extraction to the queue");
            clock.Restart(); using (Phase("test.paint.drain")) Call(f.layer, "SyncPendingSurfaceToTexture"); report.drainMs = clock.Elapsed.TotalMilliseconds;
            report.tiles = (int)Get(f.layer, "LastPaintTileCount"); report.changedPixels = (long)Get(f.layer, "LastPaintTilePixels");
            report.bytes = (long)Type("PaintTileTransfer").GetProperty("TransferredBytes", S).GetValue(null) - bytesBefore;
            report.actualReadbackBytes = (long)Type("PaintTileTransfer").GetProperty("ReadbackBytes", S).GetValue(null) - readbackBefore;
            var sample = mode == "frame" ? f.pixels.GetPixel(40, 40) : f.pixels.GetPixel(4096, 4096);
            if (textured) t.Near(1, sample.a, 0, "Opaque textured source stays opaque");
            else t.True(sample.a > 0 && sample.a <= .152, "Mask and stroke opacity bound coverage");
            if (hdr) t.True(sample.r < 0 && sample.g > 1, "Signed HDR survives partial synchronization");
            var after = Probe(f.surface, probe);
            t.True(report.tiles > 0 && report.tiles < 1024, "Coverage is smaller than the full canvas");
            if (mode == "frame") t.Equal(124, report.tiles, "Perimeter covers only the outer tile ring");
            Static("PaintHistoryStore", "FlushWrites");
            string revision = (string)Get(Get(f.layer, "PixelUndoObject"), "Revision");
            string journalRoot = (string)Type("PaintHistoryStore").GetProperty("Root", S).GetValue(null);
            int pages = (int)Get(Static("PaintHistoryStore", "Get", revision), "pages");
            for (int i = 0; i < pages; i++)
            {
                using var file = File.OpenRead(Path.Combine(journalRoot, revision + "-" + i + ".bin"));
                report.journalBytes += file.Length;
                using var reader = new BinaryReader(file);
                if (reader.ReadInt32() == 0x57545034) report.rawPages++; else report.compressedPages++;
            }
            RejectDamagedPage(t, f);
            clock.Restart(); using (Phase("test.paint.undo")) Undo.PerformUndo(); report.undoMs = clock.Elapsed.TotalMilliseconds;
            Exact(t, before, Probe((RenderTexture)Get(f.layer, "paintSurface"), probe), "8K exact Half Undo probe");
            clock.Restart(); using (Phase("test.paint.redo")) Undo.PerformRedo(); report.redoMs = clock.Elapsed.TotalMilliseconds;
            Exact(t, after, Probe((RenderTexture)Get(f.layer, "paintSurface"), probe), "8K exact Half Redo probe");
            if (mode == "small")
            {
                report.repeatCommitMs = new double[3]; report.repeatDrainMs = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    clock.Restart(); f.Stroke(points, mask, drain: false); report.repeatCommitMs[i] = clock.Elapsed.TotalMilliseconds;
                    clock.Restart(); Call(f.layer, "SyncPendingSurfaceToTexture"); report.repeatDrainMs[i] = clock.Elapsed.TotalMilliseconds;
                }
            }
            Static("WhimTexProfiler", "Stop", "test_complete"); profiling = false;
            File.WriteAllText(reportPath + ".json", JsonUtility.ToJson(report, true));
            File.WriteAllText(reportPath + "-profile.json", Json(new { frequency = Stopwatch.Frequency, capture = Type("WhimTexProfiler").GetProperty("Current", S).GetValue(null) }));
        }
        finally { if (profiling) Static("WhimTexProfiler", "Stop", "test_failed"); Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
}
