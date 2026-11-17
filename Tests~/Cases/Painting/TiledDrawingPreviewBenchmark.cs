using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Object = UnityEngine.Object;

// Owned production Drawing/viewport calls, no user window and no history commits.
public static class TiledDrawingPreviewBenchmark
{
    const string Key = "WhimTex.Tests.FilledPreview", ProbeKey = "WhimTex.Tests.PreviewProbe";
    const string Root = "Temp/WhimTex/filled-preview/", Source = "Packages/com.dcfapixels.whimtex/src/Layers/TiledPaint/TiledPaintSurface.Render.cs";
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type Product(string n) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + n, true);
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static object Get(object o, string n) => o.GetType().GetField(n, F)?.GetValue(o) ?? o.GetType().GetProperty(n, F)?.GetValue(o);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    static T Stat<T>(string t, string n) { var type = Product(t); return (T)(type.GetProperty(n, F)?.GetValue(null) ?? type.GetField(n, F)?.GetValue(null)); }
    static string Json(object o) => (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true).GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { o });
    static double Ms(long t) => (Stopwatch.GetTimestamp() - t) * 1000d / Stopwatch.Frequency;
    static string Hash(byte[] bytes) { using var h = SHA256.Create(); return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", ""); }
    [Serializable] public sealed class Row
    {
        public string workload, view, gpuStatus, hash;
        public int frame, gc0, calls, sourceTiles, candidates, visible, draws, passes = 1;
        public double paintMs, viewportMs, renderMs, validateMs, configureMs, collectMs, cullMs, bindMs, recordMs, executeMs, cleanupMs, gpuMs, syncMs;
        public long poolBytes, gpuNanoseconds, gpuCount;
    }
    [Serializable] public sealed class Report
    {
        public string id, mode, status = "running", error, sourceHash, fixtureHash, unity, backend, device, threading;
        public bool gpuSupported; public int checks; public double setupMs, elapsedMs;
        public List<Row> rows = new();
    }
    public static string Main()
    {
        var args = File.ReadAllLines(Root + "request.txt"); return Start(args[0], args[1]);
    }
    public static string Start(string id, string mode)
    {
        if (mode != "plain" && mode != "stages" && mode != "gpu" && mode != "sync") throw new ArgumentException("Unknown measurement mode");
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Length > 90) throw new ArgumentException("Invalid ID");
        if (File.Exists(Root + id + ".json")) throw new InvalidOperationException("Do not overwrite earlier evidence");
        var old = AppDomain.CurrentDomain.GetData(Key) as object[];
        if (old != null && old[1] != null) throw new InvalidOperationException("Preview measurement is active");
        var state = new object[] { "", null }; AppDomain.CurrentDomain.SetData(Key, state);
        var job = new Job(id, mode, state); state[1] = (Action)(() => job.Finish(new OperationCanceledException()));
        try { job.Prepare(); } catch (Exception e) { job.Finish(e); }
        return Poll();
    }
    public static string Poll() => (AppDomain.CurrentDomain.GetData(Key) as object[])?[0] as string ?? "No measurement";
    public static string Cancel() { ((AppDomain.CurrentDomain.GetData(Key) as object[])?[1] as Action)?.Invoke(); return Poll(); }
    sealed class Job
    {
        readonly Report report; readonly object[] state; readonly long started = Stopwatch.GetTimestamp();
        WhimTexDocument doc; DrawingLayerBehaviour drawing; Layer layer; object settings, parameters;
        readonly object[] caches = new object[2], regions = new object[2];
        Texture2D original, pixel, reader; RenderTexture control; CommandBuffer controls;
        readonly List<ProfilerRecorder> recorders = new(); readonly List<Row> gpuRows = new();
        readonly List<ProfilerRecorderSample> gpuSamples = new();
        int workload, frame = -4, drainTicks; double drainingAt; bool finished;
        Vector2 previous;
        TiledPaintSurface Surface => (TiledPaintSurface)Get(drawing, "tiledSurface");
        internal Job(string id, string mode, object[] state)
        {
            this.state = state;
            report = new Report { id = id, mode = mode, sourceHash = Hash(File.ReadAllBytes(Source)),
                fixtureHash = Hash(File.ReadAllBytes("Packages/com.dcfapixels.whimtex/Tests~/Cases/Painting/TiledDrawingPreviewBenchmark.cs")),
                unity = Application.unityVersion, backend = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName,
                threading = SystemInfo.renderingThreadingMode.ToString(), gpuSupported = SystemInfo.supportsGpuRecorder };
        }
        void Check(bool ok, string why) { report.checks++; if (!ok) throw new InvalidOperationException(why); }
        internal void Prepare()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before measuring editor painting");
            Check(!Stat<bool>("WhimTexProfiler", "Recording"), "Finish active profiler capture first");
            Check(Stat<int>("PaintTileTransfer", "PendingCount") == 0 && Stat<long>("PaintHistoryStore", "PendingBytes") == 0, "Paint transfer queue is busy");
            foreach (var w in Resources.FindObjectsOfTypeAll<WhimTexWindow>()) Check(Get(w, "paintingLayer") == null, "User is painting");
            if (report.mode is "stages" or "gpu") Check(File.ReadAllText(Source).Contains("WhimTex.Tests.PreviewProbe"), "Apply reviewed timing-only patch first");
            Directory.CreateDirectory(Root);
            doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 8192;
            drawing = new DrawingLayerBehaviour(); layer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            doc.layers.Add(layer); Call(doc, "NormalizeModel");
            Call(drawing, "InitializeCanvas", 8192, 8192); original = (Texture2D)Get(drawing, "StoredTexture");
            var pixels = original.GetRawTextureData<ulong>();
            // Exact signed Half values: R=2, G=.25, B=-.25, A=1; every source tile occupied.
            for (int i = 0; i < pixels.Length; i++) pixels[i] = 0x3c00b40034004000UL;
            original.Apply(false, false); Set(drawing, "tiledBaseEmpty", false); Call(drawing, "ReleasePaintSurface");
            Call(drawing, "EnsureTiledSource", 8192, 8192); Check(Surface.TileCount == 1024, "Expected 1024 occupied tiles");
            settings = Activator.CreateInstance(Product("PaintToolSettings"), true);
            Set(settings, "brushColor", new Color(.8f, 3.5f, -.3f, .6f)); Set(settings, "brushSpacing", .05f); Set(settings, "brushHardness", .72f);
            for (int i = 0; i < 2; i++)
            {
                caches[i] = Activator.CreateInstance(Product("EffectRenderCache"), true);
                regions[i] = Activator.CreateInstance(Product("CanvasRenderRegion"), F, null,
                    new object[] { i == 0 ? new RectInt(0, 0, 8192, 8192) : new RectInt(3584, 3584, 1024, 1024), i == 0 ? 8 : 1 }, null);
            }
            pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            reader = new Texture2D(1024, 1024, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var output = Render(0); Fence(output); RenderTexture.ReleaseTemporary(output);
            report.setupMs = Ms(started);
            if (report.mode == "gpu")
            {
                Check(report.gpuSupported && report.threading == "MultiThreaded", "GPU timing unsupported in this running device mode");
                control = new RenderTexture(2048, 2048, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave }; control.Create();
                controls = new CommandBuffer();
            }
            BeginCase(); EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Reload;
            Publish(); EditorApplication.QueuePlayerLoopUpdate();
        }
        void BeginCase()
        {
            if (workload == 0) return;
            int size = workload == 1 ? 81 : workload == 4 ? 1024 : 2048;
            Set(settings, "brushSize", (float)size);
            var dynamics = Get(settings, "dynamics");
            Set(dynamics, "blend", workload == 4 ? DCFApixels.WhimTex.BlendMode.Multiply : DCFApixels.WhimTex.BlendMode.Normal);
            Set(dynamics, "blendApplication", Enum.Parse(Product("BrushBlendApplication"), workload == 4 ? "Stamp" : "Stroke"));
            parameters = Call(settings, "GetStrokeParameters", workload == 3, (Color?)null);
            Call(drawing, "PrepareBufferedStroke", 8192, 8192, "Owned preview diagnostic"); Call(drawing, "BeginStroke", new Vector2(.46f, .5f));
        }
        string Work => new[] { "idle", "brush81", "brush2048", "erase2048", "multiply1024" }[workload];
        void Tick()
        {
            if (finished) return;
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play Mode interrupted preview measurement");
                if (Ms(started) > 180000) throw new TimeoutException("Preview diagnostic timed out");
                if (workload == 5)
                {
                    PollGpu(); drainTicks++;
                    if (report.mode != "gpu" || (EditorApplication.timeSinceStartup - drainingAt > 3 && drainTicks > 20)) { Finish(null); return; }
                }
                else
                {
                    double paint = 0;
                    if (workload > 0)
                    {
                        var point = new Vector2(.46f + (frame + 4) * (640f / 27 / 8192), .5f);
                        long start = Stopwatch.GetTimestamp();
                        if (frame == -4) Call(drawing, "PaintPoint", point, 8192, 8192, parameters);
                        else Call(drawing, "PaintSegment", previous, point, 8192, 8192, false, parameters);
                        paint = Ms(start); previous = point;
                    }
                    for (int k = 0; k < 2; k++) Measure((k + (frame & 1)) & 1, paint);
                    PollGpu();
                    if (++frame == 24)
                    {
                        Call(drawing, "CancelStroke"); workload++; frame = -4;
                        if (workload < 5) BeginCase(); else { drainingAt = EditorApplication.timeSinceStartup; if (report.mode == "gpu") Controls(); }
                    }
                }
                Publish(); EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception e) { Finish(e); }
            finally { AppDomain.CurrentDomain.SetData(ProbeKey, null); RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        RenderTexture Render(int view) => (RenderTexture)Call(doc, "RenderCanvasViewport", regions[view], caches[view], true, workload > 0 ? drawing : null);
        void Measure(int view, double paint)
        {
            var row = new Row { workload = Work, view = view == 0 ? "full" : "native", frame = frame, paintMs = paint, sourceTiles = Surface.TileCount,
                poolBytes = Surface.AllocatedBytes, gpuStatus = "not_measured" };
            long[] ticks = new long[10]; int[] counts = new int[4];
            object marker = report.mode == "gpu" ? (object)NewGpu(row) : null;
            if (report.mode is "stages" or "gpu") AppDomain.CurrentDomain.SetData(ProbeKey, new object[] { ticks, counts, marker });
            if (report.mode == "sync") { var warm = Render(view); Fence(warm); RenderTexture.ReleaseTemporary(warm); }
            int gc = GC.CollectionCount(0); long start = Stopwatch.GetTimestamp(); var output = Render(view); row.viewportMs = Ms(start); row.gc0 = GC.CollectionCount(0) - gc;
            AppDomain.CurrentDomain.SetData(ProbeKey, null);
            try
            {
                Check(output.width == 1024 && output.height == 1024, "View output resolution differs");
                if (report.mode == "sync") { long sync = Stopwatch.GetTimestamp(); Fence(output); row.syncMs = Ms(sync); }
                double scale = 1000d / Stopwatch.Frequency;
                row.validateMs = ticks[0] * scale; row.configureMs = ticks[1] * scale; row.collectMs = ticks[2] * scale;
                row.cullMs = ticks[3] * scale; row.bindMs = ticks[4] * scale; row.recordMs = ticks[5] * scale;
                row.executeMs = ticks[6] * scale; row.cleanupMs = ticks[7] * scale; row.renderMs = ticks[8] * scale;
                row.calls = counts[0]; row.candidates = counts[1]; row.visible = counts[2]; row.draws = counts[3];
                if (report.mode == "stages") Check(row.calls == 1 && row.draws > 0, "Expected one native view render; got a cached result");
                if (frame == 23 && report.mode != "gpu")
                {
                    RenderTexture.active = output; reader.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0, false);
                    var bytes = reader.GetRawTextureData<byte>(); row.hash = Hash(bytes.ToArray());
                    var p = reader.GetPixel(0, 0);
                    if (workload == 0) Check(p.r > 1 && p.b < 0, "Unpainted preview lost signed HDR");
                }
                report.rows.Add(row);
            }
            finally { RenderTexture.ReleaseTemporary(output); }
        }
        ProfilerMarker NewGpu(Row row)
        {
            var marker = new ProfilerMarker(ProfilerCategory.Render, "WhimTex.Tests.FilledPreview." + report.id + "." + gpuRows.Count, MarkerFlags.SampleGPU);
            var recorder = new ProfilerRecorder(marker, 8, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.GpuRecorder | ProfilerRecorderOptions.StartImmediately);
            recorders.Add(recorder); gpuRows.Add(row); row.gpuStatus = recorder.Valid ? "pending" : "unsupported"; return marker;
        }
        void Controls()
        {
            for (int repeat = 0; repeat < 4; repeat++) foreach (int count in new[] { 1, 16, 64 })
            {
                var row = new Row { workload = "gpu-control", view = "2048", frame = repeat, passes = count };
                var marker = NewGpu(row); controls.Clear(); controls.BeginSample(marker);
                for (int i = 0; i < count; i++) controls.Blit(Texture2D.whiteTexture, control);
                controls.EndSample(marker); Graphics.ExecuteCommandBuffer(controls); report.rows.Add(row);
            }
        }
        void PollGpu()
        {
            for (int i = 0; i < recorders.Count; i++)
            {
                var recorder = recorders[i]; if (!recorder.Valid) continue;
                recorder.CopyTo(gpuSamples, true);
                foreach (var sample in gpuSamples)
                {
                    if (sample.Count == 0) continue;
                    var row = gpuRows[i]; row.gpuCount += sample.Count; row.gpuNanoseconds += sample.Value;
                    row.gpuMs = row.gpuNanoseconds / 1e6;
                    row.gpuStatus = row.gpuCount == 1 && row.gpuNanoseconds > 0 ? "ready" : "ambiguous";
                }
            }
        }
        void Fence(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            try { RenderTexture.active = texture; pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); }
            finally { RenderTexture.active = previous; }
        }
        void Reload() => Finish(new InvalidOperationException("Interrupted by script reload"));
        void Publish() => state[0] = Json(new { report.id, report.status, workload, frame, rows = report.rows.Count, report.checks,
            message = report.id + ": " + report.status + ", workload " + workload + ", frame " + frame,
            failures = report.status == "failed" ? new[] { report.error } : Array.Empty<string>() });
        internal void Finish(Exception error)
        {
            if (finished) return; finished = true;
            EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Reload; AppDomain.CurrentDomain.SetData(ProbeKey, null);
            var errors = new List<Exception>(); if (error != null) errors.Add(error);
            void Clean(Action a) { try { a(); } catch (Exception e) { errors.Add(e); } }
            Clean(PollGpu); foreach (var r in recorders) Clean(() => r.Dispose());
            if (drawing != null) { Clean(() => Call(drawing, "CancelStroke")); Clean(() => Call(drawing, "ReleaseTransientResources")); }
            if (settings != null) Clean(() => Call(settings, "ReleasePresetTip"));
            foreach (var cache in caches) Clean(() => (cache as IDisposable)?.Dispose());
            Clean(() => controls?.Dispose());
            foreach (var o in new Object[] { control, reader, pixel, original }) if (o != null) Clean(() => Object.DestroyImmediate(o));
            if (doc != null) Clean(() => { Undo.ClearUndo(doc); doc.layers.Clear(); Object.DestroyImmediate(doc); });
            report.elapsedMs = Ms(started); report.status = errors.Count == 0 ? "passed" : errors.Count == 1 && errors[0] is OperationCanceledException ? "cancelled" : "failed";
            report.error = errors.Count == 0 ? null : new AggregateException(errors).ToString();
            foreach (var row in report.rows) if (row.gpuStatus == "pending") row.gpuStatus = "missing";
            state[1] = null; Publish(); Directory.CreateDirectory(Root); File.WriteAllText(Root + report.id + ".json", Json(report));
        }
    }
}
