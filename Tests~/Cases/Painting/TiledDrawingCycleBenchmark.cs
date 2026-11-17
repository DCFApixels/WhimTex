using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using Object = UnityEngine.Object;

// Frame-paced production model/viewport diagnostic. Never drives a user window.
public static class TiledDrawingCycleBenchmark
{
    const string Key = "WhimTex.Tests.RasterCycle";
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type Product(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static object Get(object obj, string name) => obj.GetType().GetField(name, F)?.GetValue(obj) ?? obj.GetType().GetProperty(name, F)?.GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, F).SetValue(obj, value);
    static object Static(string type, string name, params object[] args) => Product(type).GetMethod(name, F).Invoke(null, args);
    static T Stat<T>(string type, string name)
    { var owner = Product(type); var property = owner.GetProperty(name, F); return (T)(property != null ? property.GetValue(null) : owner.GetField(name, F).GetValue(null)); }
    static string Json(object value) => (string)Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    static double Ms(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;

    [Serializable] public sealed class Config
    {
        public string name, pattern = "dab";
        public int side = 8192, diameter = 81, strokes = 8, points = 1, channels = 15, burst = 1, alternateDiameter, offsetPixels, soakSeconds;
        public float spacing = .05f;
        public float outsideDistance = .75f;
        public bool pencilMetrics;
        public int motionPixels;
        public bool pencilStrip;
        public bool pencil, outside, outsideLeft, mask, pressureOpacity, stampBlend, denseBase, immediateUndo, profile, nativeView, erase;
        public double interval = 1d / 60;
    }
    [Serializable] public sealed class Frame
    {
        public int stroke, point, gc0, gc1, pendingPages, sourceTiles, storedTiles;
        public int stamps, vertices;
        public long indices, vertexPayloadBytes, indexPayloadBytes;
        public string phase;
        public string mode, pattern;
        public int diameter; public bool erase;
        public double atMs, intervalMs, beginMs, paintMs, endMs, renderMs, submitMs, gpuObservedMs, workMs, verificationMs;
        public long gpuPoolBytes, stagingBytes, writerBytes, capacityWaits, managedDelta;
        public long nativeActiveBytes, nativeIdleBytes;
    }
    [Serializable] public sealed class Stroke
    {
        public int index, changedTiles;
        public double beginMs, firstPaintMs, endMs, undoMs, redoMs;
        public int sourceTiles, storedTiles;
        public long stagingBytes, writerBytes;
    }
    [Serializable] public sealed class Report
    {
        public string status = "running", error, unity, device, backend, output, note, lastUndoSkippedReason, historyHome;
        public Config config;
        public double initializeMs, seedMs, prepareSourceMs, drainWallMs, explicitFlushMs, lastUndoMs, lastRedoMs, cleanupMs;
        public long originalBytes, peakPoolBytes, peakStagingBytes, peakWriterBytes, peakNativeBytes, historyBytes, capacityWaits, readbackBytes, driverBeforeBytes, driverPeakBytes;
        public long finalPoolBytes, finalStagingBytes, finalWriterBytes, finalNativeActiveBytes;
        public long historyFreeBeforeBytes, historyFreeAtFinishBytes, historyFreeAfterCleanupBytes;
        public int checks, asyncErrors, sourceTiles, storedTiles, gc0, gc1;
        public List<string> errors = new();
        public List<string> externalErrors = new();
        public bool allocationCounterResponds, canvasPlacementVerified;
        public List<Frame> frames = new();
        public List<Stroke> strokes = new();
        public List<object> profilePhases;
        public string pendingHash, committedHash, undoHash, redoHash;
    }
    static object[] State => AppDomain.CurrentDomain.GetData(Key) as object[];
    static string ReportPath(string runId, Config c) => Path.GetFullPath("Temp/WhimTex/raster-cycle-audit/" + runId +
        (c.soakSeconds > 0 ? (c.outside ? "-outside-" : "-inside-") + c.side : "") + ".json");
    public static string StartStability(string runId, bool outside, int seconds = 240, int side = 8192)
        => Start(runId, Json(new Config { name = "stability-" + (outside ? "outside" : "inside"), side = side,
            diameter = 2048, denseBase = true, mask = true, outside = outside, points = 32, soakSeconds = seconds }));
    public static string Start(string runId, string config)
    {
        if (State != null && (string)State[1] == "running") throw new InvalidOperationException("A cycle diagnostic is still running.");
        var c = JsonUtility.FromJson<Config>(config);
        if (c == null || (c.side != 2048 && c.side != 8192) || c.diameter < 1 || c.diameter > 4096 || c.strokes < 1 || c.strokes > 64 || c.points < 1 || c.points > 96)
            throw new ArgumentException("Bounded diagnostic configuration required.");
        if (!double.IsFinite(c.interval) || c.interval < 0 || c.interval > 1 || !float.IsFinite(c.spacing) || c.spacing < .001f || c.spacing > 1 || c.channels < 1 || c.channels > 15 || c.burst < 1 || c.burst > 4)
            throw new ArgumentException("Invalid diagnostic pacing or brush settings.");
        if (c.alternateDiameter < 0 || c.alternateDiameter > 4096 || Math.Abs((long)c.offsetPixels) > 256)
            throw new ArgumentException("Invalid alternating diameter or tile alignment.");
        if (!float.IsFinite(c.outsideDistance) || c.outsideDistance < 0 || c.outsideDistance > 2)
            throw new ArgumentException("Invalid outside distance.");
        if (c.motionPixels < 0 || c.motionPixels > 6500 || c.pencilStrip && (!c.pencil || c.motionPixels == 0 || c.diameter > 9 || c.points != 2))
            throw new ArgumentException("Invalid Pencil motion/strip configuration.");
        if (c.soakSeconds != 0 && (c.soakSeconds < 15 || c.soakSeconds > 240 || c.profile || c.pencil || c.immediateUndo))
            throw new ArgumentException("Stability duration must be 15..240 seconds with ordinary unprofiled Brush/Erase.");
        if (runId.Length > 100 || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid run ID.");
        if (File.Exists(ReportPath(runId, c))) throw new InvalidOperationException("Do not overwrite earlier evidence.");
        var state = new object[] { runId, "running", "Preparing", null, null };
        AppDomain.CurrentDomain.SetData(Key, state);
        var job = new Job(state, c, runId); state[3] = (Action)(() => job.Finish(new OperationCanceledException()));
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try { job.Prepare(); } catch (Exception e) { job.Finish(e); }
        finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        return Poll(runId);
    }
    public static string Poll(string runId)
    {
        var s = State;
        if (s == null || (string)s[0] != runId) return Json(new { status = "failed", checks = 0, message = "Missing cycle diagnostic", failures = new[] { "Run ID does not match." } });
        string status = (string)s[1]; var report = s.Length > 4 ? s[4] : null;
        return Json(new { runId, status, checks = report == null ? 0 : (int)Get(report, "checks"), message = (string)s[2],
            failures = status == "failed" ? new[] { report == null ? (string)s[2] : (string)Get(report, "error") } : Array.Empty<string>() });
    }
    public static string Cancel(string runId)
    {
        if (State != null && (string)State[0] == runId && (string)State[1] == "running") ((Action)State[3])();
        return Poll(runId);
    }

    sealed class Job
    {
        readonly object[] state; readonly Config c; readonly Report report; readonly string path;
        WhimTexDocument doc; DrawingLayerBehaviour drawing; Layer layer; object settings, parameters, cache, region;
        Texture2D mask, original, probeCpu; RenderTexture fence, probeGpu; Object cursor;
        object previousCapture; bool profiling, finished;
        PaintTileHistoryTests.DiskScope diskScope;
        readonly List<AsyncGPUReadbackRequest> ownedRequests = new();
        long started, last, drainStart, waitsAtStart, readsAtStart;
        int stroke, point, phase, currentGroup, gc0, gc1, requests;
        double next; Vector2 previousPoint; byte[] afterLast, beforeLastStrip;
        RectInt probeRect; Stroke row;
        bool Soak => c.soakSeconds > 0;
        string Pattern => Soak ? (stroke % 2 == 0 ? "long" : "spam") : c.pattern;
        int PointCount => Soak ? (Pattern == "long" ? 32 : 2) : c.points;
        int Diameter => Soak ? (stroke / 4 % 2 == 0 ? 2048 : 4096) : c.alternateDiameter > 0 && stroke % 2 != 0 ? c.alternateDiameter : c.diameter;
        bool Erase => Soak ? stroke / 2 % 2 != 0 : c.erase;
        int Mode => Soak ? stroke / 8 % 4 : 0;
        bool VerifyStroke => Soak && stroke % 16 == 15;
        void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert)
            {
                // Known Editor online-services failure, independently confirmed in
                // Editor.log. Keep its full evidence without misattributing it to paint.
                bool services = type == LogType.Exception && message.Contains("KeyNotFoundException") &&
                    message.Contains("'services-gateway'") && stack.Contains("UnityEditor.Connect.ServicesConfiguration.BuildPaths") &&
                    !stack.Contains("DCFApixels.WhimTex") && !stack.Contains(nameof(TiledDrawingCycleBenchmark));
                var log = services ? report.externalErrors : report.errors;
                if (log.Count < 32) log.Add(type + ": " + message + "\n" + stack);
            }
        }
        TiledPaintSurface Surface => (TiledPaintSurface)Get(drawing, "tiledSurface");
        Vector2 OutsideOffset => new Vector2(c.outside ? (c.outsideLeft ? -c.outsideDistance : c.outsideDistance) : 0, 0);

        internal Job(object[] state, Config c, string id)
        {
            this.state = state; this.c = c;
            path = ReportPath(id, c);
            report = new Report { config = c, output = path, unity = Application.unityVersion,
                device = SystemInfo.graphicsDeviceName, backend = SystemInfo.graphicsDeviceType.ToString(),
                note = "Production Prepare/Begin/PaintPoint/PaintSegment/EndStroke and RenderCanvasViewport, paced by EditorApplication.update. No mouse/UI dispatch or screen-presentation timing. GPU completion is observed asynchronously on a 1x1 output copy BEFORE EndStroke, so it measures paint/viewport readiness, not commit completion, and includes callback cadence. No forced history drain between strokes. Setup, final drain, Undo/Redo, and optional instrumented profile are separate. Reflection/harness costs are included in submission times. Driver memory is Editor-wide; pool/staging/writer figures are separate sampled maxima after each update, not true within-call peaks or total process memory. First interval includes a deliberate100ms startup delay." };
            state[4] = report;
        }
        void Check(bool value, string message) { report.checks++; if (!value) throw new InvalidOperationException(message); }
        internal void Prepare()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before editor painting diagnostics.");
            Check(!Stat<bool>("WhimTexProfiler", "Recording"), "An existing user profiler capture is active.");
            Check(Stat<int>("PaintTileTransfer", "PendingCount") == 0 && Stat<long>("PaintHistoryStore", "PendingBytes") == 0, "Finish existing user paint transfers before measuring.");
            foreach (var w in Resources.FindObjectsOfTypeAll<WhimTexWindow>()) Check(Get(w, "paintingLayer") == null, "A user stroke is active.");
            foreach (var history in Resources.FindObjectsOfTypeAll(Product("DrawingPixelHistory")))
            {
                bool retained = (bool)Get(history, "HasLiveHistory") || !string.IsNullOrEmpty((string)Get(history, "Revision"));
                if (Get(history, "undoRecords") is IEnumerable records)
                    foreach (var record in records)
                        if (record is Object nativeRecord && nativeRecord != null) { retained = true; break; }
                Check(!retained, "An existing paint Undo/Redo history is retained. This diagnostic cannot run while quota retirement could affect another document; it does not clear that history.");
            }
            if (Soak) diskScope = new PaintTileHistoryTests.DiskScope();
            report.historyHome = (string)Get(Stat<object>("PaintUndoDisk", "Current"), "Home");
            report.historyFreeBeforeBytes = HistoryFreeSpace();
            long requiredFree = c.side == 8192 ? 4L * 1024 * 1024 * 1024 : 768L * 1024 * 1024;
            Check(report.historyFreeBeforeBytes >= requiredFree,
                "Insufficient free space on the PaintUndo history volume for this diagnostic. Required: " + requiredFree + " bytes; available: " + report.historyFreeBeforeBytes + ". No document or stroke was created.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (Soak) Application.logMessageReceived += OnLog;
            doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
            doc.width = doc.height = c.side;
            drawing = new DrawingLayerBehaviour(); layer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            // Same visible canvas positions and footprint on both sides of the source frame.
            // An outside test translates the frame left and offsets source UV right.
            layer.transform.position = -OutsideOffset * c.side;
            doc.layers.Add(layer); Call(doc, "NormalizeModel");
            settings = Activator.CreateInstance(Product("PaintToolSettings"), true);
            Set(settings, "brushColor", new Color(.8f, 3.5f, -.3f, .6f));
            Set(settings, "brushSize", (float)c.diameter); Set(settings, "pencilSize", c.diameter);
            Set(settings, "brushSpacing", c.spacing); Set(settings, "brushHardness", .72f);
            var dynamics = Get(settings, "dynamics"); Set(dynamics, "writeChannels", c.channels);
            if (c.pressureOpacity) Set(Get(dynamics, "pressureOpacity"), "enabled", true);
            if (c.stampBlend) { Set(dynamics, "blend", DCFApixels.WhimTex.BlendMode.Multiply); Set(dynamics, "blendApplication", Enum.Parse(Product("BrushBlendApplication"), "Stamp")); }
            if (c.mask)
            {
                mask = new Texture2D(c.side, c.side, TextureFormat.R8, false, true) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var data = mask.GetRawTextureData<byte>();
                for (int i = 0; i < data.Length; i++) data[i] = (byte)(64 + (i % c.side) * 191 / (c.side - 1));
                mask.Apply(false, false);
            }
            ConfigureParameters(c.diameter);
            report.driverBeforeBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            long start = Stopwatch.GetTimestamp(); Call(drawing, "InitializeCanvas", c.side, c.side); report.initializeMs = Ms(start);
            original = (Texture2D)Get(drawing, "StoredTexture"); report.originalBytes = (long)c.side * c.side * 8;
            if (c.denseBase)
            {
                start = Stopwatch.GetTimestamp(); var data = original.GetRawTextureData<ulong>();
                for (int i = 0; i < data.Length; i++) data[i] = 0x3c00340034003400UL;
                original.Apply(false, false); Set(drawing, "tiledBaseEmpty", false); Call(drawing, "ReleasePaintSurface"); report.seedMs = Ms(start);
            }
            start = Stopwatch.GetTimestamp(); Call(drawing, "EnsureTiledSource", c.side, c.side); report.prepareSourceMs = Ms(start);
            Vector2 expected = Point(0, 0) - OutsideOffset;
            var transform = (TextureTransform)Get(layer, "PixelCanvasTransform");
            Check(Vector2.Distance(transform.Map(Point(0, 0), Vector2.one * c.side), expected) < .00001f,
                "Fixture source UV must map to the expected visible canvas position; Transform position is in pixels.");
            cache = Activator.CreateInstance(Product("EffectRenderCache"), true);
            int size = c.nativeView ? 1024 : c.side, step = c.nativeView ? 1 : c.side / 1024;
            var rect = c.nativeView ? new RectInt((c.side - size) / 2, (c.side - size) / 2, size, size) : new RectInt(0, 0, size, size);
            region = Activator.CreateInstance(Product("CanvasRenderRegion"), F, null, new object[] { rect, step }, null);
            fence = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave }; fence.Create();
            probeGpu = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave }; probeGpu.Create();
            probeCpu = new Texture2D(8, 8, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread(); var allocationProbe = new byte[1024];
            report.allocationCounterResponds = GC.GetAllocatedBytesForCurrentThread() > bytesBefore; GC.KeepAlive(allocationProbe);
            if (c.profile)
            {
                previousCapture = Stat<object>("WhimTexProfiler", "Current");
                Static("WhimTexProfiler", "StartConfigured", 262144, 180d, null, false, false); profiling = true;
            }
            waitsAtStart = Stat<long>("PaintTileTransfer", "CapacityWaits"); readsAtStart = Stat<long>("PaintTileTransfer", "ReadbackBytes");
            gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1);
            started = last = Stopwatch.GetTimestamp(); next = EditorApplication.timeSinceStartup + .1;
            EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Reload;
            state[2] = "Drawing " + c.name; EditorApplication.QueuePlayerLoopUpdate();
        }
        void Reload() => Finish(new InvalidOperationException("Script reload interrupted this diagnostic; no completed timing is claimed."));
        Vector2 Point(int i, int s)
        {
            float x = .5f, y = .5f;
            if (Pattern == "long") { float t = i / (float)Math.Max(1, PointCount - 1); x = .1f + .8f * t; y = .5f + .12f * Mathf.Sin(2 * Mathf.PI * t); }
            if (c.motionPixels > 0) { x = .1f + c.motionPixels * (i / (float)Math.Max(1, c.points - 1)) / c.side; y = .5f; }
            else if (Pattern == "spam" || Pattern == "spread")
            {
                x = Pattern == "spread" ? .08f + (s % 8) * .115f : .45f + (s % 3) * .025f;
                y = Pattern == "spread" ? .12f + (s / 8) * .14f : .48f + (s % 2) * .025f;
                x += i * .005f; y += i * .003f;
            }
            x += OutsideOffset.x;
            return new Vector2(x, y) + Vector2.one * (c.offsetPixels / (float)c.side);
        }
        void ConfigureParameters(int diameter)
        {
            Set(settings, "brushSize", (float)diameter); Set(settings, "pencilSize", diameter);
            if (Soak)
            {
                var dynamics = Get(settings, "dynamics");
                Set(dynamics, "writeChannels", Mode == 2 ? 5 : 15);
                Set(Get(dynamics, "pressureOpacity"), "enabled", Mode == 3);
                Set(dynamics, "blend", Mode == 3 ? DCFApixels.WhimTex.BlendMode.Multiply : DCFApixels.WhimTex.BlendMode.Normal);
                Set(dynamics, "blendApplication", Enum.Parse(Product("BrushBlendApplication"), Mode == 3 ? "Stamp" : "Stroke"));
            }
            parameters = Call(settings, c.pencil ? "GetPencilParameters" : "GetStrokeParameters", Erase, (Color?)null);
            if (mask != null && (!Soak || Mode == 1)) parameters = Call(parameters, "WithSelectionMask", mask);
            if (Soak ? Mode == 3 : c.pressureOpacity) parameters = Call(parameters, "WithPressure", .65f);
        }
        void Tick()
        {
            if (finished || EditorApplication.timeSinceStartup < next) return;
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { int limit = phase == 0 ? c.burst : 1; for (int i = 0; i < limit && !finished; i++) { Step(); if (phase != 0) break; } }
            catch (Exception e) { Finish(e); }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        void Step()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play Mode interrupted editor painting diagnostics.");
            if (Ms(started) > (Soak ? c.soakSeconds * 1000 + 60000 : 180000)) throw new TimeoutException("Cycle diagnostic exceeded its deadline.");
            next = EditorApplication.timeSinceStartup + c.interval;
            long begin = Stopwatch.GetTimestamp();
            var f = new Frame { stroke = stroke, point = point, atMs = Ms(started), intervalMs = Ms(last), phase = phase == 0 ? "input" : "drain" }; last = begin;
            f.diameter = Diameter; f.erase = Erase; f.pattern = Pattern;
            f.mode = Soak ? Mode switch { 0 => "plain", 1 => "mask", 2 => "channels", _ => "pressure-stamp" } : "configured";
            int beforeGc0 = GC.CollectionCount(0), beforeGc1 = GC.CollectionCount(1); long alloc = GC.GetAllocatedBytesForCurrentThread();
            if (phase == 0)
            {
                if (point == 0)
                {
                    if (c.pencilStrip && stroke == c.strokes - 1) beforeLastStrip = PencilStrip();
                    if (Soak || c.alternateDiameter > 0) ConfigureParameters(Diameter);
                    row = new Stroke { index = stroke }; report.strokes.Add(row);
                    var at = Point(0, stroke); probeRect = new RectInt(Mathf.FloorToInt(at.x * c.side) - 4, Mathf.FloorToInt(at.y * c.side) - 4, 8, 8);
                    if (VerifyStroke) { long verify = Stopwatch.GetTimestamp(); beforeLastStrip = Probe(); f.verificationMs += Ms(verify); }
                    long start = Stopwatch.GetTimestamp(); Undo.IncrementCurrentGroup(); currentGroup = Undo.GetCurrentGroup();
                    Call(drawing, "PrepareBufferedStroke", c.side, c.side, "Owned Raster cycle"); Call(drawing, "BeginStroke", Point(0, stroke));
                    f.beginMs = row.beginMs = Ms(start);
                }
                int limit = Pattern == "spam" || Pattern == "spread" ? PointCount : point + 1;
                long paint = Stopwatch.GetTimestamp();
                for (; point < limit; point++)
                {
                    var p = Point(point, stroke);
                    if (point == 0) Call(drawing, "PaintPoint", p, c.side, c.side, parameters);
                    else Call(drawing, "PaintSegment", previousPoint, p, c.side, c.side, false, parameters);
                    previousPoint = p;
                }
                f.paintMs = Ms(paint); if (f.point == 0) row.firstPaintMs = f.paintMs;
                if (c.pencilMetrics)
                {
                    f.stamps = ((ICollection)Get(drawing, "segmentStamps")).Count;
                    var renderer = typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer", F);
                    var mesh = (Mesh)renderer.GetField("brushMesh", F).GetValue(null);
                    f.vertices = mesh.vertexCount;
                    for (int s = 0; s < mesh.subMeshCount; s++) f.indices += mesh.GetIndexCount(s);
                    // Position + UV0..6: 25 float components; sequence context adds 4.
                    f.vertexPayloadBytes = (long)f.vertices * ((bool)renderer.GetField("meshSequence", F).GetValue(null) ? 116 : 100);
                    f.indexPayloadBytes = f.indices * 4;
                }
                long rendering = Stopwatch.GetTimestamp(); Render(); f.renderMs = Ms(rendering);
                long submission = Stopwatch.GetTimestamp(); SubmitCompletion(f); f.submitMs = Ms(submission);
                if (point == PointCount)
                {
                    if (c.pencilStrip && stroke == c.strokes - 1) report.pendingHash = Hash(PencilStrip());
                    long end = Stopwatch.GetTimestamp(); Call(drawing, "EndStroke"); f.endMs = row.endMs = Ms(end);
                    cursor = (Object)Get(drawing, "PixelUndoObject");
                    row.changedTiles = (int)Get(drawing, "LastPaintTileCount");
                    Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(currentGroup); currentGroup = Undo.GetCurrentGroup();
                    Call(doc, "MarkChanged");
                    row.sourceTiles = Surface.TileCount; row.storedTiles = ((ICollection)Get(drawing, "TiledPixels")).Count;
                    row.stagingBytes = Stat<long>("PaintTileTransfer", "StagingBytes"); row.writerBytes = Stat<long>("PaintHistoryStore", "PendingBytes");
                    if (c.immediateUndo) UndoRedo(row);
                    if (VerifyStroke) { long verify = Stopwatch.GetTimestamp(); UndoRedo(row); f.verificationMs += Ms(verify); }
                    stroke++; point = 0;
                    if (Soak ? Ms(started) >= c.soakSeconds * 1000 || stroke >= 512 : stroke == c.strokes)
                    { phase = 1; drainStart = Stopwatch.GetTimestamp(); }
                }
            }
            else if (Stat<int>("PaintTileTransfer", "PendingCount") == 0 && Stat<long>("PaintHistoryStore", "PendingBytes") == 0 && requests == 0)
            {
                report.drainWallMs = Ms(drainStart);
                long flush = Stopwatch.GetTimestamp(); Call(drawing, "FlushTiledStorage"); Static("PaintHistoryStore", "FlushWrites"); report.explicitFlushMs = Ms(flush);
                if (!c.immediateUndo)
                {
                    if (Undo.GetCurrentGroup() == currentGroup) UndoRedo(null);
                    else report.lastUndoSkippedReason = "Global Undo group changed between Editor updates; preserve unrelated actions. Immediate-Undo scenarios measure inside the same owned callback.";
                }
                Check(report.asyncErrors == 0, "GPU completion readback failed.");
                Check(report.errors.Count == 0, string.Join("\n", report.errors));
                Check((int)Get(drawing, "TiledSourceWidth") == c.side && (int)Get(drawing, "TiledSourceHeight") == c.side, "Source frame and UV changed.");
                report.sourceTiles = Surface.TileCount; report.storedTiles = ((ICollection)Get(drawing, "TiledPixels")).Count;
                report.finalPoolBytes = Surface.AllocatedBytes; report.finalStagingBytes = Stat<long>("PaintTileTransfer", "StagingBytes");
                report.finalWriterBytes = Stat<long>("PaintHistoryStore", "PendingBytes"); report.finalNativeActiveBytes = Stat<long>("PaintHistoryBuffers", "ActiveBytes");
                if (Soak) Check(report.finalStagingBytes == 0 && report.finalWriterBytes == 0 && report.finalNativeActiveBytes == 0, "Drained stroke retains transfer/writer buffers");
                VerifyVisiblePaint(); SavePreview(); Finish(null); return;
            }
            f.workMs = Ms(begin); f.gc0 = GC.CollectionCount(0) - beforeGc0; f.gc1 = GC.CollectionCount(1) - beforeGc1;
            f.managedDelta = GC.GetAllocatedBytesForCurrentThread() - alloc;
            f.pendingPages = Stat<int>("PaintTileTransfer", "PendingCount"); f.stagingBytes = Stat<long>("PaintTileTransfer", "StagingBytes");
            f.writerBytes = Stat<long>("PaintHistoryStore", "PendingBytes"); f.capacityWaits = Stat<long>("PaintTileTransfer", "CapacityWaits") - waitsAtStart;
            f.nativeActiveBytes = Stat<long>("PaintHistoryBuffers", "ActiveBytes"); f.nativeIdleBytes = Stat<long>("PaintHistoryBuffers", "IdleBytes");
            f.gpuPoolBytes = Surface.AllocatedBytes; f.sourceTiles = Surface.TileCount; f.storedTiles = ((ICollection)Get(drawing, "TiledPixels")).Count;
            if (Soak)
            {
                Check(f.gpuPoolBytes <= 2L * 1024 * 1024 * 1024, "GPU pool exceeded production cap");
                Check(f.stagingBytes <= Stat<long>("PaintTileTransfer", "StagingBudget"), "Staging exceeded production cap");
                Check(f.writerBytes <= Stat<long>("PaintHistoryStore", "PendingBudget"), "Writer queue exceeded production cap");
            }
            report.peakPoolBytes = Math.Max(report.peakPoolBytes, f.gpuPoolBytes); report.peakStagingBytes = Math.Max(report.peakStagingBytes, f.stagingBytes);
            report.peakWriterBytes = Math.Max(report.peakWriterBytes, f.writerBytes);
            report.peakNativeBytes = Math.Max(report.peakNativeBytes,
                f.nativeActiveBytes + f.nativeIdleBytes);
            report.driverPeakBytes = Math.Max(report.driverPeakBytes, UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver());
            report.frames.Add(f); state[2] = c.name + " stroke " + stroke + (Soak ? " at " + (int)(Ms(started) / 1000) + "s" : "/" + c.strokes) + " point " + point + " pending " + f.pendingPages;
            ownedRequests.RemoveAll(request => request.done);
            EditorApplication.QueuePlayerLoopUpdate();
        }
        RenderTexture Render()
        {
            var output = (RenderTexture)Call(doc, "RenderCanvasViewport", region, cache, Soak, Soak ? drawing : null);
            try { Graphics.Blit(output, fence); }
            finally { RenderTexture.ReleaseTemporary(output); }
            return fence;
        }
        void SubmitCompletion(Frame f)
        {
            long submitted = Stopwatch.GetTimestamp(); requests++;
            var pending = AsyncGPUReadback.Request(fence, 0, request =>
            { f.gpuObservedMs = Ms(submitted); if (request.hasError) report.asyncErrors++; requests--; });
            pending.forcePlayerLoopUpdate = true; ownedRequests.Add(pending);
            GL.Flush();
        }
        byte[] Probe()
        {
            Surface.CopyRegion(probeRect, probeGpu, true); RenderTexture.active = probeGpu;
            probeCpu.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false); return probeCpu.GetRawTextureData<byte>().ToArray();
        }
        void UndoRedo(Stroke timing)
        {
            Check(Undo.GetCurrentGroup() == currentGroup, "Another action changed the Undo stack during this diagnostic.");
            // Immediate Undo timing starts with its real pending GPU/history work.
            // Only the final, already-drained verification reads pixels beforehand.
            if (timing == null || Soak) afterLast = c.pencilStrip ? PencilStrip() : Probe();
            if (c.pencilStrip) report.committedHash = Hash(afterLast);
            string revision = (string)Get(cursor, "Revision");
            long start = Stopwatch.GetTimestamp(); Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo"); Call(drawing, "EnsureTiledSource", c.side, c.side);
            double undoMs = Ms(start); Check((string)Get(cursor, "Revision") != revision, "Undo changes the owned history revision");
            if (Soak && timing != null) Equal(beforeLastStrip, Probe(), "Stability stroke exact Undo probe");
            if (c.pencilStrip) { var restored = PencilStrip(); report.undoHash = Hash(restored); Equal(beforeLastStrip, restored, "Full Pencil strip Undo"); }
            start = Stopwatch.GetTimestamp(); Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo"); Call(drawing, "EnsureTiledSource", c.side, c.side);
            double redoMs = Ms(start); Check((string)Get(cursor, "Revision") == revision, "Redo restores the owned history revision");
            if (timing == null || Soak)
            {
                var restored = c.pencilStrip ? PencilStrip() : Probe();
                if (c.pencilStrip) report.redoHash = Hash(restored);
                Equal(afterLast, restored, "Last stroke Redo pixels");
            }
            if (timing != null) { timing.undoMs = undoMs; timing.redoMs = redoMs; } else { report.lastUndoMs = undoMs; report.lastRedoMs = redoMs; }
            currentGroup = Undo.GetCurrentGroup();
        }
        void Equal(byte[] a, byte[] b, string message)
        {
            bool equal = a.Length == b.Length; for (int i = 0; equal && i < a.Length; i++) equal &= a[i] == b[i]; Check(equal, message);
        }
        static string Hash(byte[] bytes)
        { using var sha = System.Security.Cryptography.SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        byte[] PencilStrip()
        {
            var start = Point(0, 0) * c.side;
            var rect = new RectInt(Mathf.FloorToInt(start.x) - 16, Mathf.FloorToInt(start.y) - 16, c.motionPixels + 34, 34);
            var rt = RenderTexture.GetTemporary(rect.width, rect.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var cpu = new Texture2D(rect.width, rect.height, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var previous = RenderTexture.active;
            try { Surface.CopyRegion(rect, rt, true); RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rect.width, rect.height), 0, 0, false); return cpu.GetRawTextureData<byte>().ToArray(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); RenderTexture.ReleaseTemporary(rt); }
        }
        long HistoryFreeSpace() => new DriveInfo(Path.GetPathRoot(report.historyHome)).AvailableFreeSpace;
        void VerifyVisiblePaint()
        {
            if (c.erase || Soak) return;
            var at = Point(0, 0) - OutsideOffset;
            var rect = new RectInt(Mathf.FloorToInt(at.x * c.side) - 4, Mathf.FloorToInt(at.y * c.side) - 4, 8, 8);
            var smallRegion = Activator.CreateInstance(Product("CanvasRenderRegion"), F, null, new object[] { rect, 1 }, null);
            var output = (RenderTexture)Call(doc, "RenderCanvasViewport", smallRegion, cache, false, null);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = output; probeCpu.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false);
                bool visible = false; foreach (var pixel in probeCpu.GetPixels()) visible |= pixel.a > .00001f;
                Check(visible, "The fixture paint is missing at its expected visible canvas location.");
                report.canvasPlacementVerified = true;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(output); }
        }
        object CompactProfile(object capture)
        {
            var source = (Array)Get(capture, "events"); int count = (int)Get(capture, "count");
            var events = Array.CreateInstance(source.GetType().GetElementType(), count); Array.Copy(source, events, count);
            return new { id = Get(capture, "id"), count, events, start = Get(capture, "start"), end = Get(capture, "end"),
                frequency = Stopwatch.Frequency, dropped = Get(capture, "dropped"), stopReason = Get(capture, "stopReason"),
                documentId = Get(capture, "documentId"), fixtureDocumentId = Static("WhimTexProfiler", "DocumentId", doc),
                unityVersion = Get(capture, "unityVersion"), renderingThreadingMode = Get(capture, "renderingThreadingMode"),
                graphicsJobs = Get(capture, "graphicsJobs"), memory = Get(capture, "memory") };
        }
        void SavePreview()
        {
            var output = (RenderTexture)Call(doc, "RenderCanvasViewport", region, cache, false, null);
            var small = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var cpu = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            try { Graphics.Blit(output, small); RenderTexture.active = small; cpu.ReadPixels(new Rect(0, 0, 256, 256), 0, 0, false); File.WriteAllBytes(Path.ChangeExtension(path, ".png"), cpu.EncodeToPNG()); }
            finally { Object.DestroyImmediate(cpu); RenderTexture.ReleaseTemporary(small); RenderTexture.ReleaseTemporary(output); }
        }
        internal void Finish(Exception error)
        {
            if (finished) return; finished = true;
            EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Reload;
            long cleanup = Stopwatch.GetTimestamp(); var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var cleanupErrors = new List<Exception>();
            void Cleanup(Action action) { try { action(); } catch (Exception e) { cleanupErrors.Add(e); } }
            try
            {
                report.gc0 = GC.CollectionCount(0) - gc0; report.gc1 = GC.CollectionCount(1) - gc1;
                report.capacityWaits = Stat<long>("PaintTileTransfer", "CapacityWaits") - waitsAtStart;
                report.readbackBytes = Stat<long>("PaintTileTransfer", "ReadbackBytes") - readsAtStart;
                if (cursor != null)
                    foreach (var step in (IEnumerable)Static("PaintHistoryStore", "Snapshot"))
                        if ((string)Get(step, "owner") == (string)Get(cursor, "Identity"))
                            foreach (var file in Directory.GetFiles((string)Static("PaintHistoryStore", "StepDirectory", (string)Get(step, "id")), "*", SearchOption.AllDirectories))
                                report.historyBytes += new FileInfo(file).Length;
                if (profiling)
                {
                    Static("WhimTexProfiler", "Stop", "raster_cycle_complete");
                    var capture = Stat<object>("WhimTexProfiler", "Current"); File.WriteAllText(Path.ChangeExtension(path, ".profile.json"), Json(CompactProfile(capture)));
                }
            }
            catch (Exception e) { error = error == null ? e : new AggregateException(error, e); }
            finally
            {
                if (profiling)
                {
                    Cleanup(() => Static("WhimTexProfiler", "Stop", "raster_cycle_cleanup"));
                    Cleanup(() => Static("WhimTexProfiler", "Clear"));
                    Cleanup(() => Product("WhimTexProfiler").GetField("capture", F).SetValue(null, previousCapture)); profiling = false;
                }
                foreach (var request in ownedRequests) if (!request.done) Cleanup(() => request.WaitForCompletion());
                if (report.historyHome != null) Cleanup(() => report.historyFreeAtFinishBytes = HistoryFreeSpace());
                if (drawing != null)
                {
                    var ownedSurface = Surface;
                    // Find the fixture cursor even when its first commit failed before
                    // Step could assign it. Never clear another document's history.
                    if (cursor == null) Cleanup(() => cursor = (Object)Get(drawing, "PixelUndoObject"));
                    string owner = cursor == null ? null : (string)Get(cursor, "Identity");
                    Cleanup(() => Call(drawing, "CancelStroke"));
                    if (cursor != null)
                    {
                        Cleanup(() => Static("PaintTileTransfer", "Drain")); Cleanup(() => Static("PaintHistoryStore", "FlushWrites"));
                        Cleanup(() => Static("PaintHistoryStore", "FinishCheckpointWork", cursor));
                    }
                    if (cursor != null) Cleanup(() => { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); });
                    if (owner != null)
                    {
                        // Destroying the cursor releases native Undo but ordinary disk
                        // cleanup is delayed. Retire only this fixture's files now.
                        Cleanup(() =>
                        {
                            foreach (var step in (IEnumerable)Static("PaintHistoryStore", "Snapshot"))
                                if ((string)Get(step, "owner") == owner) Static("PaintHistoryStore", "DeleteStep", step);
                            var disk = Stat<object>("PaintUndoDisk", "Current");
                            Call(disk, "DeleteOwned", Path.Combine((string)Get(disk, "Root"), "owners", owner));
                        });
                    }
                    if (original != null) Cleanup(() => Undo.ClearUndo(original));
                    Cleanup(() => Call(drawing, "ReleaseTransientResources"));
                    if (Soak && ownedSurface != null) Cleanup(() => Check(ownedSurface.AllocatedBytes == 0, "Surface retained GPU pages after disposal"));
                }
                if (settings != null) Cleanup(() => Call(settings, "ReleasePresetTip"));
                Cleanup(() => (cache as IDisposable)?.Dispose());
                if (doc != null) Cleanup(() => { Undo.ClearUndo(doc); doc.layers.Clear(); Object.DestroyImmediate(doc); });
                if (mask != null) Cleanup(() => Object.DestroyImmediate(mask));
                if (fence != null) Cleanup(() => Object.DestroyImmediate(fence));
                if (probeGpu != null) Cleanup(() => Object.DestroyImmediate(probeGpu));
                if (probeCpu != null) Cleanup(() => Object.DestroyImmediate(probeCpu));
                if (report.historyHome != null) Cleanup(() => report.historyFreeAfterCleanupBytes = HistoryFreeSpace());
                Cleanup(() => diskScope?.Dispose());
                if (Soak) Application.logMessageReceived -= OnLog;
                RenderTexture.active = active; GL.sRGBWrite = srgb;
            }
            if (cleanupErrors.Count > 0) { if (error != null) cleanupErrors.Insert(0, error); error = new AggregateException(cleanupErrors); }
            report.cleanupMs = Ms(cleanup); report.status = error == null ? "passed" : error is OperationCanceledException ? "cancelled" : "failed"; report.error = error?.ToString();
            state[1] = report.status; state[2] = Json(new { report.status, report.checks, report.error, report.output, frames = report.frames.Count });
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, Json(report)); }
            catch (Exception e) { report.status = "failed"; report.error = (report.error ?? "") + " Report write: " + e; state[1] = "failed"; state[2] = report.error; }
        }
    }
}
