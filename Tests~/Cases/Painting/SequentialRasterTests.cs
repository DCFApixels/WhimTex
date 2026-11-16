using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;
using BlendMode = DCFApixels.WhimTex.BlendMode;

// Reuses the production history fixture helpers. Oracles are hashes of every raw
// Half tile, not regenerated expected images or full-size managed pixel copies.
public static partial class TiledDrawingHistoryTests
{
    [Serializable] public sealed class SequentialConfig
    {
        public string id, mode = "pressure";
        public int width = 8192, height = 8192, diameter = 2048, segments = 16, channels = 15, subdivisions = 1;
        public float spacing = .005f;
        public bool outside, mask = true, varyPressure = true, edge;
    }
    [Serializable] public sealed class SequentialHash { public string name, sha256; public int x, y; }
    [Serializable] public sealed class SequentialVersion { public List<SequentialHash> tiles = new(); }
    [Serializable] public sealed class SequentialInput
    {
        public int stroke, index;
        public float pressure;
        public double paintCpuMs, completionMs;
        public string completedPixel;
    }
    [Serializable] public sealed class SequentialReport
    {
        public int version = 1;
        public string config, unity, device, backend, colorSpace, createdUtc, baselineSha256, historyHome;
        public List<SequentialHash> sources = new();
        public List<SequentialVersion> versions = new();
        public List<SequentialInput> inputs = new();
        public List<SequentialHash> listCapacities = new();
        public double setupMs, hashMs, undoRedoMs, cleanupMs;
        public double[] endStrokeMs = new double[2], drainMs = new double[2];
        public long originalBytes, maskBytes, peakPageBytes, peakStagingBytes, peakNativeBytes, peakWriterBytes, historyFreeBeforeBytes;
        public long peakMeshRuntimeBytes, peakRendererTextureBytes, peakCommandBufferBytes, peakManagedBytes, peakDriverBytes;
        public string timing = "Paint CPU submission and a separate synchronous 1px pending-output completion per input; includes reflection. Setup, hashing, commit/drain and native Undo/Redo are outside input latency. Memory maxima are sampled after inputs/commit/drain, not instantaneous driver allocation peaks. Mesh runtime size and command bytes use public Unity APIs; list capacities are observed on package-owned fields.";
    }
    public static string CaptureSequential(string runId, string caseJson) => SequentialRun(runId, caseJson, true);
    public static string VerifySequential(string runId, string caseJson) => SequentialRun(runId, caseJson, false);

    static string SequentialRun(string runId, string caseJson, bool capture) => TestContext.Run(
        (capture ? "Capture immutable" : "Verify frozen") + " sequential Raster pixels", test =>
    {
        var config = JsonUtility.FromJson<SequentialConfig>(caseJson);
        if (config == null || !SequentialName(runId) || !SequentialName(config.id) ||
            (config.mode != "pressure" && config.mode != "stampMultiply" && config.mode != "both") ||
            config.width < 257 || config.height < 257 || config.width > 8192 || config.height > 8192 ||
            config.diameter < 1 || config.diameter > 4096 || config.segments < 2 || config.segments > 32 ||
            config.channels < 1 || config.channels > 15 || config.subdivisions < 1 || config.subdivisions > 2 ||
            !float.IsFinite(config.spacing) || config.spacing < .005f || config.spacing > .1f)
            throw new ArgumentException("Use a named bounded sequential case: pressure/stampMultiply/both, 257..8192 source, diameter1..4096, segments2..32, subdivisions1..2.");
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/sequential-raster", runId));
        string baselinePath = Path.Combine(directory, config.id + ".baseline.json");
        if (capture && File.Exists(baselinePath)) throw new IOException("Frozen baseline already exists; capture never overwrites it: " + baselinePath);
        if (!capture && !File.Exists(baselinePath)) throw new FileNotFoundException("Capture this case before changing production code.", baselinePath);
        var report = new SequentialReport { config = JsonUtility.ToJson(config), unity = Application.unityVersion,
            device = SystemInfo.graphicsDeviceName, backend = SystemInfo.graphicsDeviceType.ToString(),
            colorSpace = QualitySettings.activeColorSpace.ToString(), createdUtc = DateTime.UtcNow.ToString("O"), sources = SequentialSources() };
        var expected = capture ? null : SequentialReadReport(File.ReadAllText(baselinePath));
        if (expected != null)
        {
            test.Equal(1, expected.version, "Oracle schema version"); test.Equal(expected.config, report.config, "Exact captured input configuration");
            test.Equal(expected.unity, report.unity, "Same Unity version"); test.Equal(expected.device, report.device, "Same GPU");
            test.Equal(expected.backend, report.backend, "Same graphics backend"); test.Equal(expected.colorSpace, report.colorSpace, "Same project color space");
            foreach (var source in expected.sources)
                if (source.name.StartsWith("Tests~/", StringComparison.Ordinal))
                    test.Equal(source.sha256, report.sources.Find(x => x.name == source.name)?.sha256, "Unchanged oracle fixture " + source.name);
            report.baselineSha256 = SequentialFileHash(baselinePath);
        }
        test.True(SystemInfo.supportsAsyncGPUReadback, "Actual GPU completion is available");
        test.True(!SequentialStat<bool>("WhimTexProfiler", "Recording"), "Finish the existing user profiler capture first");
        test.Equal(0, SequentialStat<int>("PaintTileTransfer", "PendingCount"), "Finish prior user transfers first");
        test.Equal(0L, SequentialStat<long>("PaintHistoryStore", "PendingBytes"), "Finish prior user writers first");
        test.True(!(bool)Static("PaintHistoryStore", "HasActiveStroke"), "Finish the active paint gesture first");
        test.True(!SequentialStat<bool>("PaintHistoryStore", "MaintenanceBusy"), "Finish existing paint checkpoint work first");
        foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
            test.True(Get(window, "paintingLayer") == null, "No user window is in a paint gesture");
        foreach (var history in Resources.FindObjectsOfTypeAll(Product("DrawingPixelHistory")))
        {
            bool retained = (bool)Get(history, "HasLiveHistory") || !string.IsNullOrEmpty((string)Get(history, "Revision"));
            if (Get(history, "undoRecords") is IEnumerable records)
                foreach (var record in records)
                    if (record is Object nativeRecord && nativeRecord != null) { retained = true; break; }
            test.True(!retained, "Existing paint Undo/Redo history blocks this diagnostic; no user history is cleared");
        }
        string tempHistory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-undo-lifecycle"));
        report.historyFreeBeforeBytes = new DriveInfo(Path.GetPathRoot(tempHistory)).AvailableFreeSpace;
        long requiredFree = Math.Max(config.width, config.height) == 8192 ? 4L * 1024 * 1024 * 1024 : 768L * 1024 * 1024;
        test.True(report.historyFreeBeforeBytes >= requiredFree, "Insufficient free space on the isolated Temp history volume; required " + requiredFree + " bytes");
        using var disk = new PaintTileHistoryTests.DiskScope();
        report.historyHome = disk.home;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        document.width = config.width; document.height = config.height;
        var drawing = new DrawingLayerBehaviour();
        var layer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var offset = config.outside ? new Vector2(.75f, 0f) : Vector2.zero;
        layer.transform.position = new Vector2(-offset.x * config.width, -offset.y * config.height);
        Texture2D mask = null, original = null;
        Object cursor = null;
        var fence = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
        if (!fence.Create()) throw new InvalidOperationException("Cannot create owned completion target.");
        var hashBuffer = new byte[256 * 256 * 8];
        long setup = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            document.layers.Add(layer); Call(document, "NormalizeModel");
            Call(drawing, "InitializeCanvas", config.width, config.height);
            original = (Texture2D)Get(drawing, "StoredTexture"); report.originalBytes = (long)config.width * config.height * 8;
            Call(drawing, "EnsureTiledStorage", config.width, config.height);
            SequentialSeed(drawing, config, offset);
            Call(drawing, "EnsureTiledSource", config.width, config.height);
            var point0 = SequentialPoint(config, 0, 0) + offset;
            test.True(Vector2.Distance(((TextureTransform)Get(layer, "PixelCanvasTransform")).Map(point0,
                new Vector2(config.width, config.height)), point0 - offset) < .00001f, "Outside fixture maps to the same visible canvas geometry");
            if (config.mask)
            {
                mask = new Texture2D(config.width, config.height, TextureFormat.R8, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var raw = mask.GetRawTextureData<byte>();
                for (int y = 0; y < config.height; y++) for (int x = 0; x < config.width; x++)
                    raw[y * config.width + x] = (byte)(32 + ((x / 31 + y / 47) % 8) * 31);
                mask.Apply(false, false); report.maskBytes = raw.Length;
            }
            report.setupMs = SequentialMs(setup);
            report.versions.Add(SequentialVersionOf(drawing, hashBuffer, report));
            for (int stroke = 0; stroke < 2; stroke++)
            {
                var settings = Settings(stroke == 0 ? new Color(-.4f, 4f, .3f, .65f) : new Color(3f, -.2f, 1.2f, .45f));
                Set(settings, "brushSize", (float)config.diameter); Set(settings, "brushSpacing", config.spacing); Set(settings, "brushHardness", .72f);
                var dynamics = Get(settings, "dynamics"); Set(dynamics, "writeChannels", config.channels);
                Set(dynamics, "flow", .71f); Set(dynamics, "opacity", .77f);
                Set(Get(dynamics, "pressureFlow"), "enabled", false);
                if (config.mode != "stampMultiply") Set(Get(dynamics, "pressureOpacity"), "enabled", true);
                if (config.mode != "pressure")
                { Set(dynamics, "blend", BlendMode.Multiply); Set(dynamics, "blendApplication", Enum.Parse(Product("BrushBlendApplication"), "Stamp")); }
                var parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
                if (mask != null) parameters = Call(parameters, "WithSelectionMask", mask);
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Call(drawing, "PrepareBufferedStroke", config.width, config.height, "Owned sequential Raster capture");
                var last = SequentialPoint(config, 0, stroke) + offset;
                Call(drawing, "BeginStroke", last);
                int count = config.segments * config.subdivisions;
                for (int i = 0; i <= count; i++)
                {
                    float u = i / (float)count;
                    var point = SequentialPoint(config, u, stroke) + offset;
                    float pressure = config.varyPressure ? .21f + .79f * ((i + stroke * 3) % 7) / 6f : .65f;
                    var p = Call(parameters, "WithPressure", pressure);
                    var input = new SequentialInput { stroke = stroke, index = i, pressure = pressure };
                    long at = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (i == 0) Call(drawing, "PaintPoint", point, config.width, config.height, p);
                    else Call(drawing, "PaintSegment", last, point, config.width, config.height, false, p);
                    input.paintCpuMs = SequentialMs(at); last = point;
                    at = System.Diagnostics.Stopwatch.GetTimestamp();
                    report.peakCommandBufferBytes = Math.Max(report.peakCommandBufferBytes, SequentialCommandBytes(drawing));
                    ((TiledPaintSurface)Get(drawing, "tiledSurface")).CopyRegion(new RectInt(Mathf.FloorToInt(point.x * config.width), Mathf.FloorToInt(point.y * config.height), 1, 1), fence, true);
                    var request = AsyncGPUReadback.Request(fence, 0); request.WaitForCompletion();
                    input.completionMs = SequentialMs(at);
                    test.True(!request.hasError, "Pending output completed without GPU readback error");
                    input.completedPixel = BitConverter.ToString(request.GetData<byte>().ToArray()); report.inputs.Add(input);
                    SequentialMemory(drawing, report);
                }
                long commit = System.Diagnostics.Stopwatch.GetTimestamp(); Call(drawing, "EndStroke"); report.endStrokeMs[stroke] = SequentialMs(commit);
                SequentialMemory(drawing, report);
                long drain = System.Diagnostics.Stopwatch.GetTimestamp(); Call(drawing, "FlushTiledStorage"); Static("PaintHistoryStore", "FlushWrites"); report.drainMs[stroke] = SequentialMs(drain);
                cursor = (Object)Get(drawing, "PixelUndoObject");
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                report.versions.Add(SequentialVersionOf(drawing, hashBuffer, report));
                test.True(!SequentialSame(report.versions[stroke], report.versions[stroke + 1]), "Overlapping stroke changes actual stored Half pixels");
                SequentialMemory(drawing, report);
            }
            long replay = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 1; i >= 0; i--)
            { Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); SequentialEqual(test, report.versions[i], SequentialVersionOf(drawing, hashBuffer, report), "Exact native Undo " + i); }
            for (int i = 1; i <= 2; i++)
            { Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); SequentialEqual(test, report.versions[i], SequentialVersionOf(drawing, hashBuffer, report), "Exact native Redo " + i); }
            report.undoRedoMs = SequentialMs(replay);
            var first = report.versions[2].tiles[report.versions[2].tiles.Count / 2];
            test.Equal(first.sha256, SequentialHashBytes(Probe(drawing, new RectInt(first.x * 256, first.y * 256, 256, 256))), "Recreated GPU tile equals exact durable Half data");
            test.Equal(config.width, (int)Get(drawing, "TiledSourceWidth"), "Original source UV width remains fixed");
            test.Equal(config.height, (int)Get(drawing, "TiledSourceHeight"), "Original source UV height remains fixed");
            if (expected != null)
            {
                test.Equal(expected.versions.Count, report.versions.Count, "Captured version count");
                for (int i = 0; i < report.versions.Count; i++) SequentialEqual(test, expected.versions[i], report.versions[i], "Frozen before/after version " + i);
                test.Equal(expected.inputs.Count, report.inputs.Count, "Same sequential input count");
                for (int i = 0; i < report.inputs.Count; i++) test.Equal(expected.inputs[i].completedPixel, report.inputs[i].completedPixel, "Frozen pending output after input " + i);
            }
            var finalSources = SequentialSources();
            foreach (var source in report.sources) test.Equal(source.sha256, finalSources.Find(x => x.name == source.name)?.sha256, "Source remained stable during the run " + source.name);
        }
        finally
        {
            long cleanup = System.Diagnostics.Stopwatch.GetTimestamp();
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            cursor ??= (Object)Get(drawing, "PixelUndoObject");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            if (original != null) Undo.ClearUndo(original);
            Undo.ClearUndo(document); Call(drawing, "ReleaseTransientResources"); document.layers.Clear();
            Object.DestroyImmediate(document); Object.DestroyImmediate(mask); fence.Release(); Object.DestroyImmediate(fence);
            RenderTexture.active = previous; GL.sRGBWrite = srgb; report.cleanupMs = SequentialMs(cleanup);
        }
        Directory.CreateDirectory(directory);
        string output = capture ? baselinePath : Path.Combine(directory, config.id + ".verify-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
        string serialized = SequentialWriteReport(report);
        SequentialCheckRoundtrip(test, report, SequentialReadReport(serialized));
        using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(serialized);
        test.True(File.Exists(output), "Completed report saved without overwriting the frozen baseline");
    });

    // Select the package's serializer assembly explicitly; the Editor may also
    // contain another public JsonConvert type in a diagnostic dependency.
    static Type SequentialJson => Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
    static string SequentialWriteReport(SequentialReport value) => (string)SequentialJson
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { value });
    static SequentialReport SequentialReadReport(string json) => (SequentialReport)SequentialJson
        .GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
        .Invoke(null, new object[] { json, typeof(SequentialReport) });
    static void SequentialCheckRoundtrip(TestContext test, SequentialReport expected, SequentialReport actual)
    {
        test.True(actual != null, "Report roundtrip returns a complete object");
        test.Equal(expected.version, actual.version, "Report roundtrip version");
        test.Equal(expected.config, actual.config, "Report roundtrip exact input configuration");
        test.Equal(expected.createdUtc, actual.createdUtc, "Report roundtrip timestamp");
        test.Equal(expected.baselineSha256, actual.baselineSha256, "Report roundtrip baseline identity");
        test.True(expected.sources.Count > 0 && expected.versions.Count == 3 && expected.inputs.Count > 0,
            "Oracle contains source hashes, three complete versions and actual input samples");
        test.Equal(expected.sources.Count, actual.sources.Count, "Report roundtrip source count");
        for (int i = 0; i < expected.sources.Count; i++)
        {
            test.Equal(expected.sources[i].name, actual.sources[i].name, "Report roundtrip source name");
            test.Equal(expected.sources[i].sha256, actual.sources[i].sha256, "Report roundtrip source hash");
        }
        test.Equal(expected.versions.Count, actual.versions.Count, "Report roundtrip version count");
        for (int i = 0; i < expected.versions.Count; i++)
        {
            test.True(expected.versions[i].tiles.Count > 0, "Oracle version contains actual Half tile hashes");
            SequentialEqual(test, expected.versions[i], actual.versions[i], "Report roundtrip version " + i);
        }
        test.Equal(expected.inputs.Count, actual.inputs.Count, "Report roundtrip input count");
        for (int i = 0; i < expected.inputs.Count; i++)
        {
            var a = expected.inputs[i]; var b = actual.inputs[i];
            test.Equal(a.stroke, b.stroke, "Report roundtrip input stroke");
            test.Equal(a.index, b.index, "Report roundtrip input order");
            test.Equal(a.pressure, b.pressure, "Report roundtrip input pressure");
            test.Equal(a.completedPixel, b.completedPixel, "Report roundtrip exact completed pixel");
            test.Equal(a.paintCpuMs, b.paintCpuMs, "Report roundtrip CPU timing");
            test.Equal(a.completionMs, b.completionMs, "Report roundtrip completion timing");
        }
        test.Equal(expected.listCapacities.Count, actual.listCapacities.Count, "Report roundtrip capacity count");
        for (int i = 0; i < expected.listCapacities.Count; i++)
        {
            test.Equal(expected.listCapacities[i].name, actual.listCapacities[i].name, "Report roundtrip capacity name");
            test.Equal(expected.listCapacities[i].x, actual.listCapacities[i].x, "Report roundtrip capacity value");
        }
    }

    static bool SequentialName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 80) return false;
        foreach (char c in value) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
        return true;
    }
    static double SequentialMs(long at) => (System.Diagnostics.Stopwatch.GetTimestamp() - at) * 1000d / System.Diagnostics.Stopwatch.Frequency;
    static T SequentialStat<T>(string type, string name) => (T)(Product(type).GetProperty(name, S)?.GetValue(null) ?? Product(type).GetField(name, S).GetValue(null));
    static Vector2 SequentialPoint(SequentialConfig c, float u, int stroke)
    {
        if (c.edge) return new Vector2(.93f + .13f * u, .94f + .08f * u + stroke * .025f);
        return new Vector2(stroke == 0 ? .42f + .16f * u : .58f - .16f * u, .47f + .035f * stroke + .025f * Mathf.Sin(u * Mathf.PI * 4));
    }
    static void SequentialSeed(DrawingLayerBehaviour drawing, SequentialConfig c, Vector2 offset)
    {
        Vector2 min = c.edge ? new Vector2(.9f, .9f) : new Vector2(.38f, .40f);
        Vector2 max = c.edge ? new Vector2(1.1f, 1.1f) : new Vector2(.62f, .58f);
        int x0 = Mathf.FloorToInt(((min.x + offset.x) * c.width - c.diameter * .5f) / 256), x1 = Mathf.FloorToInt(((max.x + offset.x) * c.width + c.diameter * .5f) / 256);
        int y0 = Mathf.FloorToInt(((min.y + offset.y) * c.height - c.diameter * .5f) / 256), y1 = Mathf.FloorToInt(((max.y + offset.y) * c.height + c.diameter * .5f) / 256);
        var data = new Unity.Mathematics.half4[256 * 256];
        for (int ty = y0; ty <= y1; ty++) for (int tx = x0; tx <= x1; tx++)
        {
            var tile = (Texture2D)Get(Call(drawing, "GetOrCreateTiledPixel", new Vector2Int(tx, ty)), "texture");
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                int cx = tx * 256 + x - Mathf.RoundToInt(offset.x * c.width), cy = ty * 256 + y;
                float p = ((cx & 31) + (cy & 15)) / 64f;
                data[y * 256 + x] = new Unity.Mathematics.half4(new Unity.Mathematics.float4(-.125f - p * .125f, 1.5f + p, .375f + p * .25f, .75f));
            }
            tile.GetRawTextureData<Unity.Mathematics.half4>().CopyFrom(data); tile.Apply(false, false);
        }
    }
    static SequentialVersion SequentialVersionOf(DrawingLayerBehaviour drawing, byte[] buffer, SequentialReport report)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); Call(drawing, "FlushTiledStorage");
        var result = new SequentialVersion();
        using var hash = SHA256.Create();
        foreach (var tile in (IEnumerable)Get(drawing, "TiledPixels"))
        {
            var key = (Vector2Int)Get(tile, "key"); var texture = (Texture2D)Get(tile, "texture");
            texture.GetRawTextureData<byte>().CopyTo(buffer);
            result.tiles.Add(new SequentialHash { x = key.x, y = key.y, sha256 = BitConverter.ToString(hash.ComputeHash(buffer)) });
        }
        result.tiles.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        report.hashMs += SequentialMs(started); return result;
    }
    static bool SequentialSame(SequentialVersion a, SequentialVersion b)
    {
        if (a.tiles.Count != b.tiles.Count) return false;
        for (int i = 0; i < a.tiles.Count; i++)
            if (a.tiles[i].x != b.tiles[i].x || a.tiles[i].y != b.tiles[i].y || a.tiles[i].sha256 != b.tiles[i].sha256) return false;
        return true;
    }
    static void SequentialEqual(TestContext test, SequentialVersion a, SequentialVersion b, string label)
    {
        test.Equal(a.tiles.Count, b.tiles.Count, label + " tile count");
        for (int i = 0; i < a.tiles.Count; i++)
        { test.Equal(a.tiles[i].x, b.tiles[i].x, label + " x"); test.Equal(a.tiles[i].y, b.tiles[i].y, label + " y"); test.Equal(a.tiles[i].sha256, b.tiles[i].sha256, label + " raw Half tile " + a.tiles[i].x + "," + a.tiles[i].y); }
    }
    static string SequentialHashBytes(byte[] bytes) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)); }
    static string SequentialFileHash(string path) { using var hash = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(stream)); }
    static List<SequentialHash> SequentialSources()
    {
        const string root = "Packages/com.dcfapixels.whimtex/";
        var paths = new List<string>(Directory.GetFiles(root + "src/Layers", "DrawingLayerBehaviour*.cs"));
        paths.AddRange(Directory.GetFiles(root + "src/Layers/TiledPaint", "*.cs"));
        paths.AddRange(new[] { root + "src/Shaders/PaintBrush.shader", root + "src/Shaders/HdrColor.cginc", root + "src/Shaders/ColorBlend.cginc", root + "src/Shaders/TiledPaintCommit.shader", root + "src/Shaders/TiledPaintCommit.compute", root + "src/BrushDynamics.cs", root + "src/PaintStrokeParameters.cs",
            root + "Tests~/Cases/Painting/SequentialRasterTests.cs", root + "Tests~/Cases/Painting/TiledDrawingHistoryTests.cs", root + "Tests~/Cases/Painting/PaintTileHistoryTests.cs", root + "Tests~/Cases/Painting/PaintUndoLifecycleTests.cs", root + "Tests~/Framework/TestApi.cs" });
        paths.Sort(StringComparer.Ordinal); var result = new List<SequentialHash>();
        foreach (string path in paths) result.Add(new SequentialHash { name = path.Substring(root.Length).Replace('\\', '/'), sha256 = SequentialFileHash(path) });
        return result;
    }
    static void SequentialMemory(DrawingLayerBehaviour drawing, SequentialReport report)
    {
        report.peakPageBytes = Math.Max(report.peakPageBytes, ((TiledPaintSurface)Get(drawing, "tiledSurface")).AllocatedBytes);
        report.peakStagingBytes = Math.Max(report.peakStagingBytes, SequentialStat<long>("PaintTileTransfer", "StagingBytes"));
        report.peakNativeBytes = Math.Max(report.peakNativeBytes, SequentialStat<long>("PaintHistoryBuffers", "ActiveBytes"));
        report.peakWriterBytes = Math.Max(report.peakWriterBytes, SequentialStat<long>("PaintHistoryStore", "PendingBytes"));
        report.peakManagedBytes = Math.Max(report.peakManagedBytes, GC.GetTotalMemory(false));
        report.peakDriverBytes = Math.Max(report.peakDriverBytes, Profiler.GetAllocatedMemoryForGraphicsDriver());
        var renderer = typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer", BindingFlags.NonPublic);
        long textures = 0, commands = 0;
        foreach (var field in renderer.GetFields(S))
        {
            object value = field.GetValue(null);
            if (value is Mesh mesh) report.peakMeshRuntimeBytes = Math.Max(report.peakMeshRuntimeBytes, Profiler.GetRuntimeMemorySizeLong(mesh));
            if (value is RenderTexture texture) textures += Profiler.GetRuntimeMemorySizeLong(texture);
            if (value is CommandBuffer buffer) commands += buffer.sizeInBytes;
            SequentialCapacity(report, field.Name, value);
            if (field.Name == "nativeRasterBatch" && value != null)
                foreach (var child in value.GetType().GetFields(F)) SequentialCapacity(report, field.Name + "." + child.Name, child.GetValue(value));
        }
        foreach (var field in typeof(TiledPaintSurface).GetFields(F))
            if (field.GetValue(Get(drawing, "tiledSurface")) is CommandBuffer buffer) commands += buffer.sizeInBytes;
        report.peakRendererTextureBytes = Math.Max(report.peakRendererTextureBytes, textures);
        report.peakCommandBufferBytes = Math.Max(report.peakCommandBufferBytes, commands);
    }
    static void SequentialCapacity(SequentialReport report, string name, object value)
    {
        if (value == null || !value.GetType().IsGenericType || value.GetType().GetGenericTypeDefinition() != typeof(List<>)) return;
        int capacity = (int)value.GetType().GetProperty("Capacity").GetValue(value);
        var old = report.listCapacities.Find(x => x.name == name);
        if (old == null) report.listCapacities.Add(new SequentialHash { name = name, x = capacity }); else old.x = Math.Max(old.x, capacity);
    }
    static long SequentialCommandBytes(DrawingLayerBehaviour drawing)
    {
        long result = 0;
        var renderer = typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer", BindingFlags.NonPublic);
        foreach (var field in renderer.GetFields(S)) if (field.GetValue(null) is CommandBuffer buffer) result += buffer.sizeInBytes;
        var surface = Get(drawing, "tiledSurface");
        foreach (var field in typeof(TiledPaintSurface).GetFields(F)) if (field.GetValue(surface) is CommandBuffer buffer) result += buffer.sizeInBytes;
        return result;
    }
}
