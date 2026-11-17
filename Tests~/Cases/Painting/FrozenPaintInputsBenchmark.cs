using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Diagnostic only: production methods and public Unity APIs, no source instrumentation.
public static class FrozenPaintInputsBenchmark
{
    const int Side = 8192;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Product(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static object Get(object value, string name) => value.GetType().GetField(name, F)?.GetValue(value) ?? value.GetType().GetProperty(name, F)?.GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static double Ms(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    static readonly Dictionary<GraphicsFormat, RenderTexture> probes = new();
    static byte[] Read(Texture texture, int x = 0, int y = 0)
    {
        // Some backends allocate a full source-sized staging resource even for
        // a one-pixel request. Always submit readback from a real 1x1 texture.
        if (!probes.TryGetValue(texture.graphicsFormat, out var probe))
        {
            probe = new RenderTexture(new RenderTextureDescriptor(1, 1)
                { graphicsFormat = texture.graphicsFormat, depthBufferBits = 0, msaaSamples = 1, useMipMap = false })
                { hideFlags = HideFlags.HideAndDontSave, name = "Owned frozen-input probe 1x1" };
            if (!probe.Create()) { Object.DestroyImmediate(probe); throw new InvalidOperationException("Probe creation failed"); }
            probes.Add(texture.graphicsFormat, probe);
        }
        Graphics.CopyTexture(texture, 0, 0, x, y, 1, 1, probe, 0, 0, 0, 0);
        var read = AsyncGPUReadback.Request(probe, 0);
        read.WaitForCompletion();
        if (read.hasError) throw new InvalidOperationException("Owned GPU probe failed");
        return read.GetData<byte>().ToArray();
    }
    static void Equal(TestContext t, byte[] a, byte[] b, string label)
    {
        t.Equal(a.Length, b.Length, label + " length");
        for (int i = 0; i < a.Length; i++) t.Equal(a[i], b[i], label + " byte " + i);
    }
    static void Preflight(TestContext t)
    {
        t.True(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlaying, "Idle graphical editor");
        t.True(SystemInfo.maxTextureSize >= Side && SystemInfo.supportsAsyncGPUReadback, "8K readback supported");
        t.Equal(0, (int)Product("PaintTileTransfer").GetProperty("PendingCount", S).GetValue(null), "No pending user readbacks");
        t.True(!(bool)Product("PaintHistoryStore").GetMethod("HasActiveStroke", S).Invoke(null, null), "No active history stroke");
    }
    [Serializable] public sealed class Sample
    {
        public string mode, path;
        public int cycle, order;
        public double snapshotSubmitMs, snapshotReadyMs, paintSubmitMs, paintReadyMs,
            controlMs, cancelMs, createSubmitMs, createReadyMs, copySubmitMs, copyReadyMs;
        public long frozenBytes, allocatedDelta, poolBytes, idlePoolBytes;
    }
    [Serializable] public sealed class Report
    {
        public string result, unity, gpu, backend, fixtureHash, sourceHash;
        public int diameter, batch;
        public bool outside;
        public List<Sample> samples = new();
        public Dictionary<string, string> centerPixels = new();
    }
    static string Hash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static Report NewReport() => new Report { unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
        backend = SystemInfo.graphicsDeviceType.ToString(),
        fixtureHash = Hash("Packages/com.dcfapixels.whimtex/Tests~/Cases/Painting/FrozenPaintInputsBenchmark.cs"),
        sourceHash = Hash("Packages/com.dcfapixels.whimtex/src/Layers/TiledPaint/TiledPaintSurface.cs") };
    static string Save(Report report, string name)
    {
        string folder = "Temp/WhimTex/frozen-inputs"; Directory.CreateDirectory(folder);
        var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        File.WriteAllText(folder + "/" + name + "-" + Guid.NewGuid().ToString("N") + ".json",
            (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report }));
        return report.result;
    }
    sealed class Fixture : IDisposable
    {
        public readonly WhimTexDocument doc;
        public readonly DrawingLayerBehaviour drawing;
        public readonly Texture2D original, mask;
        public readonly RenderTexture fence;
        public readonly TiledPaintSurface surface;
        public readonly Vector2 at;
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        public Fixture(bool outside)
        {
            try
            {
                doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
                doc.width = doc.height = Side;
                drawing = new DrawingLayerBehaviour();
                var layer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                layer.transform.position = outside ? new Vector2(6144, 0) : Vector2.zero;
                doc.layers.Add(layer); Call(doc, "NormalizeModel"); Call(drawing, "InitializeCanvas", Side, Side);
                original = (Texture2D)Get(drawing, "StoredTexture");
                var data = original.GetRawTextureData<half4>(); var color = new half4(new float4(1.5f, -.25f, .5f, .75f));
                for (int i = 0; i < data.Length; i++) data[i] = color;
                data[0] = new half4(new float4(4f, -.5f, 2f, 0f)); // Hidden RGB survives the exact snapshot.
                original.Apply(false, false); Set(drawing, "tiledBaseEmpty", false);
                mask = new Texture2D(Side, Side, TextureFormat.R8, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var bytes = mask.GetRawTextureData<byte>();
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(32 + (i % Side) * 223 / (Side - 1));
                mask.Apply(false, false);
                Call(drawing, "EnsureTiledSource", Side, Side);
                surface = (TiledPaintSurface)Get(drawing, "tiledSurface");
                at = new Vector2(outside ? -.25f : .5f, .5f);
                fence = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { hideFlags = HideFlags.HideAndDontSave };
                if (!fence.Create()) throw new InvalidOperationException("Fence creation failed");
                PendingPixel(); Read(original); Read(mask); Read(fence);
            }
            catch { Dispose(); throw; }
        }
        public object Parameters(int diameter, int mode)
        {
            var settings = Activator.CreateInstance(Product("PaintToolSettings"), true);
            Set(settings, "brushSize", (float)diameter); Set(settings, "brushHardness", .6f);
            Set(settings, "brushColor", new Color(3f, -.5f, 2f, .65f));
            var dynamics = Get(settings, "dynamics");
            Set(dynamics, "writeChannels", (mode & 2) != 0 ? 9 : 15);
            var parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
            return (mode & 1) != 0 ? Call(parameters, "WithSelectionMask", mask) : parameters;
        }
        public byte[] PendingPixel()
        {
            surface.CopyRegion(new RectInt(Mathf.RoundToInt(at.x * Side), Side / 2, 1, 1), fence, true);
            return Read(fence);
        }
        public Texture Frozen(string name)
        {
            object value = Get(surface, name);
            return value == null ? null : (Texture)Get(value, "Texture");
        }
        public void Dispose()
        {
            if (drawing != null) { Call(drawing, "CancelStroke"); Call(drawing, "ReleaseTransientResources"); }
            if (doc != null) { doc.layers.Clear(); Object.DestroyImmediate(doc); }
            if (mask != null) Object.DestroyImmediate(mask);
            if (fence != null) Object.DestroyImmediate(fence);
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    }
    static readonly string[] Modes = { "none", "mask", "channels", "both" };
    const string JobKey = "WhimTex.Tests.FrozenInputs";
    static object[] State => AppDomain.CurrentDomain.GetData(JobKey) as object[];
    public static string Poll(string runId)
    {
        if (State == null || (string)State[0] != runId) return TestContext.Result("failed", 0, "Missing frozen-input diagnostic").ToJson();
        return (string)State[1];
    }
    public static string Cancel(string runId)
    {
        if (State != null && (string)State[0] == runId && State[2] is Action cancel) cancel();
        return Poll(runId);
    }
    static string Start(string runId, Report report, Func<TestContext, IEnumerator> sequence, string name)
    {
        if (State != null && State[2] != null) throw new InvalidOperationException("A frozen-input diagnostic is still active");
        var test = new TestContext();
        var state = new object[] { runId, TestContext.Result("running", 0, name).ToJson(), null };
        AppDomain.CurrentDomain.SetData(JobKey, state);
        IEnumerator iterator = null; EditorApplication.CallbackFunction tick = null; AssemblyReloadEvents.AssemblyReloadCallback reload = null;
        bool finished = false;
        void Finish(Exception error)
        {
            if (finished) return; finished = true;
            EditorApplication.update -= tick; AssemblyReloadEvents.beforeAssemblyReload -= reload;
            try { (iterator as IDisposable)?.Dispose(); } catch (Exception cleanup) { error = cleanup; }
            foreach (var probe in probes.Values) if (probe != null) Object.DestroyImmediate(probe);
            probes.Clear();
            report.result = TestContext.Result(error is OperationCanceledException ? "cancelled" : error == null ? "passed" : "failed",
                test.Checks, name, error == null ? Array.Empty<string>() : new[] { error.ToString() }).ToJson();
            try { Save(report, name); } finally { state[1] = report.result; state[2] = null; }
        }
        reload = () => Finish(new OperationCanceledException("Editor reload"));
        tick = () =>
        {
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { if (!iterator.MoveNext()) Finish(null); }
            catch (Exception error) { Finish(error); }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        };
        state[2] = (Action)(() => Finish(new OperationCanceledException("Requested cancellation")));
        try
        {
            Preflight(test); iterator = sequence(test);
            EditorApplication.update += tick; AssemblyReloadEvents.beforeAssemblyReload += reload;
        }
        catch (Exception error) { Finish(error); }
        return Poll(runId);
    }
    public static string StartRun(string runId, int diameter, bool outside, int batch, int repeats = 8)
    {
        if ((diameter != 2048 && diameter != 4096) || batch < 0 || batch > 3 || repeats < 2 || repeats > 16)
            throw new ArgumentOutOfRangeException();
        var report = NewReport(); report.diameter = diameter; report.outside = outside; report.batch = batch;
        return Start(runId, report, Sequence, "matrix-" + diameter + "-" + outside + "-" + batch);
        IEnumerator Sequence(TestContext test)
        {
            using var f = new Fixture(outside);
            var transform = (TextureTransform)Get(f.doc.layers[0], "PixelCanvasTransform");
            test.True(Vector2.Distance(transform.Map(f.at, Vector2.one * Side), Vector2.one * .5f) < .00001f, "Same visible canvas location");
            var expected = new Dictionary<int, byte[]>(); var initial = f.PendingPixel();
            for (int cycle = -1; cycle < repeats; cycle++)
            for (int order = 0; order < 4; order++)
            {
                int mode = (order + cycle + batch + 5) % 4;
                var parameters = f.Parameters(diameter, mode);
                for (int pair = 0; pair < 2; pair++)
                {
                    bool split = ((cycle + order + batch + pair + 4) & 1) == 0;
                    var sample = new Sample { cycle = cycle, order = order * 2 + pair, mode = Modes[mode], path = split ? "split" : "natural" };
                    Call(f.drawing, "BeginStroke", f.at);
                    long start = Stopwatch.GetTimestamp(); Read(f.fence); sample.controlMs = Ms(start);
                    long before = f.surface.AllocatedBytes;
                    if (split)
                    {
                        start = Stopwatch.GetTimestamp(); Call(f.drawing, "EnsureTiledStroke", parameters, Side, Side);
                        sample.snapshotSubmitMs = Ms(start);
                        Read(f.Frozen("originalStraight") ?? f.Frozen("selection") ?? f.fence);
                        sample.snapshotReadyMs = Ms(start); sample.allocatedDelta = f.surface.AllocatedBytes - before;
                    }
                    start = Stopwatch.GetTimestamp(); Call(f.drawing, "PaintPoint", f.at, Side, Side, parameters);
                    sample.paintSubmitMs = Ms(start); byte[] pixel = f.PendingPixel(); sample.paintReadyMs = Ms(start);
                    sample.poolBytes = f.surface.AllocatedBytes;
                    test.True(BitConverter.ToString(pixel) != BitConverter.ToString(initial), "Actual stamp changes the visible source pixel");
                    test.True(sample.poolBytes <= 2L * 1024 * 1024 * 1024, "Production GPU cap retained");
                    long frozenBytes = ((mode & 1) != 0 ? 64L : 0) * 1024 * 1024 + ((mode & 2) != 0 ? 512L : 0) * 1024 * 1024;
                    foreach (string name in new[] { "selection", "originalStraight" })
                    {
                        Texture input = f.Frozen(name);
                        if (input != null) sample.frozenBytes += (long)input.width * input.height * GraphicsFormatUtility.GetBlockSize(input.graphicsFormat);
                    }
                    test.Equal(frozenBytes, sample.frozenBytes, "Exact full-size frozen charge");
                    test.True((f.Frozen("selection") != null) == ((mode & 1) != 0), "Mask capture mode");
                    test.True((f.Frozen("originalStraight") != null) == ((mode & 2) != 0), "Original capture mode");
                    if (expected.TryGetValue(mode, out var value)) Equal(test, value, pixel, "Stable actual stamp across split/natural order");
                    else { expected[mode] = pixel; report.centerPixels[Modes[mode]] = BitConverter.ToString(pixel); }
                    if (cycle == 0 && split)
                    {
                        foreach (int coordinate in new[] { 0, Side / 2, Side - 1 })
                        {
                            if ((mode & 1) != 0) Equal(test, Read(f.mask, coordinate, coordinate), Read(f.Frozen("selection"), coordinate, coordinate), "Exact mask snapshot");
                            if ((mode & 2) != 0) Equal(test, Read(f.original, coordinate, coordinate), Read(f.Frozen("originalStraight"), coordinate, coordinate), "Exact signed/HDR/hidden RGB snapshot");
                        }
                    }
                    start = Stopwatch.GetTimestamp(); Call(f.drawing, "CancelStroke"); sample.cancelMs = Ms(start);
                    sample.idlePoolBytes = f.surface.AllocatedBytes;
                    test.True(!f.surface.Pending, "Canceled owned stroke only");
                    Equal(test, initial, f.PendingPixel(), "Source remains unchanged after cancellation");
                    report.samples.Add(sample);
                    yield return null; // Let Editor retire temporary textures/readbacks between stamps.
                }
            }
            test.Equal(Side, (int)Get(f.drawing, "TiledSourceWidth"), "Fixed source UV width");
            test.Equal(Side, (int)Get(f.drawing, "TiledSourceHeight"), "Fixed source UV height");
            test.True(report.centerPixels["none"] != report.centerPixels["mask"], "Soft mask affects the pending result");
            test.True(report.centerPixels["none"] != report.centerPixels["channels"], "Channel protection affects the pending result");
            test.True(report.centerPixels["channels"] != report.centerPixels["both"], "Mask also affects the protected-channel result");
        }
    }

    // Diagnostic decomposition of the same public allocation/copy operations.
    // Fencing between Create and Copy changes scheduling: do not sum these times
    // as a prediction of the uninterrupted production capture above.
    public static string StartBreakdown(string runId)
    {
        var report = NewReport();
        return Start(runId, report, Sequence, "breakdown");
        IEnumerator Sequence(TestContext test)
        {
            using var f = new Fixture(false);
            for (int cycle = -1; cycle < 12; cycle++)
            for (int order = 0; order < 2; order++)
            {
                bool original = ((cycle + order + 2) & 1) == 0;
                Texture input = original ? f.original : f.mask;
                var row = new Sample { cycle = cycle, order = order, mode = original ? "original" : "mask", path = "decomposition" };
                long start = Stopwatch.GetTimestamp(); Read(f.fence); row.controlMs = Ms(start);
                RenderTexture copy = null;
                try
                {
                    start = Stopwatch.GetTimestamp();
                    copy = new RenderTexture(new RenderTextureDescriptor(input.width, input.height)
                        { graphicsFormat = input.graphicsFormat, depthBufferBits = 0, msaaSamples = 1, useMipMap = false })
                        { hideFlags = HideFlags.HideAndDontSave, filterMode = input.filterMode, wrapModeU = input.wrapModeU, wrapModeV = input.wrapModeV };
                    if (!copy.Create()) throw new InvalidOperationException("Owned copy allocation failed");
                    row.createSubmitMs = Ms(start); Read(copy); row.createReadyMs = Ms(start);
                    start = Stopwatch.GetTimestamp(); Graphics.CopyTexture(input, 0, 0, copy, 0, 0);
                    row.copySubmitMs = Ms(start); var pixel = Read(copy); row.copyReadyMs = Ms(start);
                    Equal(test, Read(input), pixel, "Exact decomposed copy");
                    report.samples.Add(row);
                }
                finally { if (copy != null) Object.DestroyImmediate(copy); }
                yield return null;
            }
        }
    }
}
