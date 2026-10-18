using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Frame-paced owned strokes, with optional owned windows. No OS input or user-document writes.
// ui-post modes commit only the owned fixture and clear only its Undo objects on cleanup.
// Poll crosses ephemeral assemblies using BCL delegates/strings only.
public static class BrushLiveTrace
{
    const string Key = "WhimTex.Tests.BrushLiveTrace";
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static Type T(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static object Static(string type, string name, params object[] args) => T(type).GetMethod(name, S).Invoke(null, args);
    static object Property(string type, string name) => T(type).GetProperty(name, S).GetValue(null);
    static object New(string name) => Activator.CreateInstance(T(name), true);
    static string Json(object o) => (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { o });
    static object[] State => AppDomain.CurrentDomain.GetData(Key) as object[];

    public static string Start(string runId, int side, int diameter, string mode, bool showWindow)
    {
        if (State != null) return TestContext.Result("failed", 0, "A diagnostic already owns this session", "Poll/cleanup the prior run first").ToJson();
        var state = new object[] { runId, TestContext.Result("running", 0, "Preparing owned stroke").ToJson(), null };
        AppDomain.CurrentDomain.SetData(Key, state);
        var run = new Run(state, side, diameter, mode, showWindow);
        state[2] = (Action)(() => run.Finish("cancelled", null));
        try { run.Prepare(); }
        catch (Exception error) { run.Finish("failed", error); }
        return (string)state[1];
    }
    public static string Poll(string runId) => State != null && (string)State[0] == runId
        ? (string)State[1] : TestContext.Result("failed", 0, "Missing run", "Diagnostic state was lost").ToJson();
    public static string Cancel(string runId)
    {
        if (State != null && (string)State[0] == runId) ((Action)State[2])();
        return TestContext.Result("cancelled", 0, "Owned diagnostic cancelled").ToJson();
    }
    public static string Cleanup(string runId) => TestContext.Run("Owned diagnostic cleanup", t =>
    {
        t.True(State == null || (string)State[0] == runId, "Do not clean another run");
        if (State == null) return;
        ((Action)State[2])(); AppDomain.CurrentDomain.SetData(Key, null);
    });

    [Serializable] sealed class Frame
    {
        public int index, stamps, gc0, stroke, pendingPages;
        public long at;
        public double intervalMs, paintMs, renderMs, updateMs, dirtyPixels;
        public float x, y;
        public bool rendered;
    }
    sealed class Run
    {
        const int Steps = 360;
        readonly object[] state;
        readonly int side, diameter;
        readonly string mode;
        readonly bool showWindow;
        readonly List<Frame> frames = new List<Frame>(Steps);
        readonly List<object> memorySamples = new List<object>();
        readonly List<object> editorFrames = new List<object>();
        readonly List<object> historySamples = new List<object>();
        readonly List<object> historyEvents = new List<object>();
        bool Post => mode.StartsWith("ui-post", StringComparison.Ordinal);
        bool Native => mode == "ui-native" || mode.EndsWith("-native", StringComparison.Ordinal);
        object historyStep;
        Process process;
        double nextHistorySample;
        long firstCommitAt;
        int stroke;
        bool nativeEnabled, oldProfileEditor, oldProfileCpu;
        int nextNativeFrame;
        readonly TestContext test = new TestContext();
        WhimTexDocument doc;
        DrawingLayerBehaviour layer;
        Texture2D pixels;
        RenderTexture preview, conversion;
        object cache, parameters, capture;
        MethodInfo segment, render;
        object[] segmentArgs, renderArgs;
        string report;
        double started, next, last, nextRender, finishedAt;
        int step, phase;
        bool finished, profiling;
        BrushLiveTraceWindow window;
        WhimTexWindow ui;
        CommandBuffer markers;
        ProfilerMarker paintMarker, renderMarker;
        ProfilerRecorder paintGpu, renderGpu;
        readonly List<ProfilerRecorderSample> sampleBuffer = new List<ProfilerRecorderSample>(1024);
        readonly List<object> paintSamples = new List<object>(), renderSamples = new List<object>();
        readonly Dictionary<string, ProfilerRecorder> native = new Dictionary<string, ProfilerRecorder>();
        readonly Dictionary<string, List<double>> nativeSamples = new Dictionary<string, List<double>>();
        public Run(object[] state, int side, int diameter, string mode, bool showWindow)
        { this.state = state; this.side = side; this.diameter = diameter; this.mode = mode; this.showWindow = showWindow; }
        public void Prepare()
        {
            test.True(side >= 512 && side <= 8192 && diameter >= 16 && diameter <= 2048, "Bounded workload");
            test.True(Post || mode == "full" || mode == "stamps" || mode == "convert" || mode == "idle" || mode == "unprofiled" || mode == "ui" || mode == "ui-unprofiled" || mode == "ui-direct" || mode == "ui-raw" || mode == "ui-warm" || mode == "ui-convert" || mode == "ui-stamps" || mode == "ui-hidden" || mode == "ui-copy" || mode == "ui-clear" || mode == "ui-current" || mode == "ui-nouav" || mode == "ui-native", "Known control");
            test.True(side < 8192 || SystemInfo.graphicsMemorySize >= 7000, "8K fixture memory guard");
            test.True(!(bool)Property("WhimTexProfiler", "Recording"), "Do not interrupt user recording");
            test.True((int)Property("PaintTileTransfer", "PendingCount") == 0, "No preceding history work contaminates in-stroke capture");
            test.True((long)Property("PaintHistoryStore", "PendingBytes") == 0, "No preceding history writer");
            foreach (var existing in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                test.True(Get(existing, "paintingLayer") == null, "No active user stroke");
            test.True(!File.Exists(Path.Combine(Application.dataPath, "../Temp/WhimTex/HangDiagnostics/vector-brush.enabled")), "Verbose hang trace must not contaminate timing");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/brush-live-trace"));
            Directory.CreateDirectory(directory);
            report = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + side + "-" + diameter + "-" + mode);
            if (Native)
            {
                test.True(!ProfilerDriver.enabled && !ProfilerDriver.deepProfiling, "Do not interrupt native CPU profiling");
                if (ProfilerDriver.lastFrameIndex >= 0)
                    test.True(ProfilerDriver.SaveProfile(report + "-previous-native.raw"), "Preserve existing stopped native capture");
            }
            var oldCapture = Property("WhimTexProfiler", "Current");
            if (oldCapture != null) File.WriteAllText(report + "-previous-profile.json", Json(new { frequency = Stopwatch.Frequency, capture = oldCapture }));
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
                doc.width = doc.height = side;
                layer = new DrawingLayerBehaviour();
                pixels = new Texture2D(side, side, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                var raw = pixels.GetRawTextureData<Color32>();
                for (int i = 0; i < raw.Length; i++) raw[i] = default;
                pixels.Apply(false, false); // One full upload; subsequent samples never read back the 8K surface.
                Call(layer, "AdoptStoredTexture", pixels); doc.layers.Add(layer); Call(doc, "NormalizeModel");
                var surface = (RenderTexture)Call(layer, "EnsurePaintSurface", side, side);
                if (mode == "ui-nouav")
                {
                    Call(layer, "ReleasePaintSurface");
                    surface = new RenderTexture(side, side, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    Set(layer, "paintSurface", surface);
                    test.True(surface.Create(), "Owned non-UAV control created");
                    RenderTexture.active = surface; GL.Clear(false, true, Color.clear); RenderTexture.active = previous;
                }
                test.True(surface.format == RenderTextureFormat.ARGBHalf, "Production Half surface retained");
                var settings = New("PaintToolSettings"); var dynamics = Get(settings, "dynamics");
                Set(settings, "brushSize", (float)diameter); Set(settings, "brushSpacing", .15f);
                Set(settings, "brushHardness", .65f); Set(settings, "brushColor", new Color(.8f, .5f, .2f, 1f));
                Set(dynamics, "opacity", 1f); Set(dynamics, "flow", .35f);
                if (mode == "ui-current" || Post)
                {
                    // Explicit snapshot of the reported ordinary brush, independent of saved preferences.
                    Set(settings, "brushHardness", 0f); Set(settings, "brushSpacing", .225f);
                    Set(dynamics, "flow", 1f); Set(dynamics, "pressure", false);
                }
                parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
                cache = New("EffectRenderCache");
                segment = layer.GetType().GetMethod("PaintSegment", F);
                render = doc.GetType().GetMethod("RenderCanvasWithCache", F);
                segmentArgs = new object[] { Point(0), Point(0), side, side, false, parameters };
                renderArgs = new object[] { 512, cache, true, layer };
                if (mode.EndsWith("convert", StringComparison.Ordinal) || mode == "ui-copy") conversion = RenderTexture.GetTemporary(side, side, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                if (Post)
                {
                    test.True((bool)Static("PaintTileTransfer", "SupportsPixels", pixels), "Owned fixture uses tile history");
                    Undo.IncrementCurrentGroup();
                    Call(layer, "PrepareBufferedStroke", side, side, "Owned post-stroke diagnostic");
                    process = Process.GetCurrentProcess();
                }
                Call(layer, "BeginStroke", Point(0));
                if (mode != "idle") Call(layer, "PaintPoint", Point(0), side, side, parameters);
                Render(); // Warm shaders/allocation separately from movement samples.
                if (mode.StartsWith("ui", StringComparison.Ordinal))
                {
                    ui = ScriptableObject.CreateInstance<WhimTexWindow>();
                    Call(ui, "SetDocument", doc); Call(ui, "SelectOnlyLayer", layer.Id);
                    Set(ui, "canvasTool", Enum.Parse(Get(ui, "canvasTool").GetType(), "Brush"));
                    Call(Get(ui, "paintSettings"), "ReleasePresetTip"); Set(ui, "paintSettings", settings);
                    ui.ShowUtility(); ui.position = new Rect(120, 90, 950, 750); ui.CreateGUI();
                    // Isolation control on this fixture only: keep an explicitly disposed
                    // detector so Get returns Unknown and does not recreate background work.
                    if (mode.Contains("-noempty"))
                    {
                        var content = Get(doc, "layerContent") as IDisposable;
                        test.True(content != null, "Owned content detector exists"); content.Dispose();
                    }
                    if (mode == "ui-hidden") ui.rootVisualElement.style.display = DisplayStyle.None;
                    Set(ui, "paintingLayer", layer); Set(ui, "paintingPressure", 1f);
                    Set(ui, "lastPaintingUv", Point(0)); Set(ui, "hasLastPaintingUv", true);
                    Call(ui, "BeginPaintingInput", Point(0));
                    // The diagnostic schedules the ordinary canvas update at the same 30 Hz ceiling.
                    // Suppress duplicate automatic requests, without skipping other window updates.
                    Set(ui, "nextPaintingCanvasAt", double.MaxValue); Set(ui, "canvasRequested", false);
                    ui.rootVisualElement.schedule.Execute(() => { ui.rootVisualElement.MarkDirtyRepaint(); ui.Repaint(); }).Every(8);
                    if (mode == "ui-clear")
                    {
                        RenderTexture.active = surface; GL.Clear(false, true, Color.clear);
                        RenderTexture.active = previous;
                    }
                }
                else if (showWindow)
                {
                    window = ScriptableObject.CreateInstance<BrushLiveTraceWindow>();
                    window.titleContent = new GUIContent("Brush timing · " + side + " / " + diameter);
                    window.minSize = new Vector2(300, 300); window.position = new Rect(140, 100, 540, 560);
                    window.ShowUtility(); window.SetPreview(preview);
                }
                if (!mode.EndsWith("unprofiled", StringComparison.Ordinal) && SystemInfo.supportsGpuRecorder)
                {
                    string identity = Guid.NewGuid().ToString("N");
                    paintMarker = new ProfilerMarker(ProfilerCategory.Render, "BrushTrace.Paint." + identity, MarkerFlags.SampleGPU);
                    renderMarker = new ProfilerMarker(ProfilerCategory.Render, "BrushTrace.Canvas." + identity, MarkerFlags.SampleGPU);
                    paintGpu = new ProfilerRecorder(paintMarker, 1024, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.GpuRecorder);
                    renderGpu = new ProfilerRecorder(renderMarker, 1024, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.GpuRecorder);
                    paintGpu.Start(); renderGpu.Start(); markers = new CommandBuffer();
                }
                if (!mode.EndsWith("unprofiled", StringComparison.Ordinal))
                {
                    var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
                    int Priority(ProfilerRecorderHandle handle)
                    {
                        string name = ProfilerRecorderHandle.GetDescription(handle).Name;
                        return name.Contains("Wait") || name.Contains("Present") || name.StartsWith("Gfx.") || name == "EditorLoop" ? 0 : 1;
                    }
                    handles.Sort((a, b) => Priority(a).CompareTo(Priority(b)));
                    foreach (var handle in handles)
                    {
                        var description = ProfilerRecorderHandle.GetDescription(handle); string name = description.Name;
                        if (description.UnitType.ToString() != "TimeNanoseconds" || name.StartsWith("WhimTex") || name.StartsWith("BrushTrace")) continue;
                        if (!(Priority(handle) == 0 || name.Contains("UIElements") || name.Contains("Render") || name.Contains("Mesh") || name.Contains("Texture") || name.StartsWith("GC.") || name == "GUI.Repaint")) continue;
                        if (native.ContainsKey(name) || native.Count >= 128) continue;
                        var recorder = ProfilerRecorder.StartNew(description.Category, name, 64);
                        if (recorder.Valid) { native.Add(name, recorder); nativeSamples.Add(name, new List<double>()); }
                        else recorder.Dispose();
                    }
                }
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            started = EditorApplication.timeSinceStartup; next = started + (mode == "ui-warm" ? 15 : 1);
            SampleMemory(-1);
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        static Vector2 Point(int i)
        {
            double a = 2 * Math.PI * i / Steps;
            return new Vector2(.5f + .46f * (float)Math.Sin(3 * a), .5f + .46f * (float)Math.Sin(2 * a + Math.PI / 4));
        }
        void BeforeReload() => Finish("failed", new InvalidOperationException("Reload interrupted diagnostic"));
        void Render()
        {
            if (mode == "ui-copy") { Graphics.Blit((Texture)Get(layer, "paintSurface"), conversion); return; }
            if (mode == "ui-convert") { Call(layer, "BlitStraightSurface", conversion, 1f); return; }
            if (mode == "ui-stamps") return;
            if (ui != null && mode != "ui-direct")
            {
                Call(ui, "UpdateCanvasRender"); Set(ui, "canvasRequested", false); return;
            }
            if (preview != null) { RenderTexture.ReleaseTemporary(preview); preview = null; }
            if (mode == "stamps") return;
            if (mode == "convert") Call(layer, "BlitStraightSurface", conversion, 1f);
            else preview = (RenderTexture)render.Invoke(doc, renderArgs);
            if (window != null) window.SetPreview(preview);
        }
        void Marker(ProfilerMarker marker, bool begin)
        {
            if (markers == null) return;
            markers.Clear(); if (begin) markers.BeginSample(marker); else markers.EndSample(marker);
            Graphics.ExecuteCommandBuffer(markers);
        }
        void Update()
        {
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                CollectEditorFrames();
                CollectGpu(paintGpu, paintSamples); CollectGpu(renderGpu, renderSamples);
                foreach (var pair in native)
                {
                    pair.Value.CopyTo(sampleBuffer, true);
                    foreach (var sample in sampleBuffer) if (sample.Count > 0) nativeSamples[pair.Key].Add(sample.Value / 1e6);
                }
                // Keep the isolated workload ticking like an actively repainting canvas;
                // otherwise an idle unfocused Editor throttles update to about 10 Hz.
                EditorApplication.QueuePlayerLoopUpdate();
                if (showWindow && window == null && ui == null) { Finish("cancelled", null); return; }
                if (window != null) window.Repaint();
                if (ui != null) ui.Repaint();
                double now = EditorApplication.timeSinceStartup;
                if (Post && now >= nextHistorySample)
                {
                    SampleHistory(); nextHistorySample = now + .05;
                }
                if (now - started > 90) throw new TimeoutException("Bounded continuous-stroke diagnostic exceeded 90 seconds");
                if (now < next) return;
                if (phase == 0)
                {
                    if (Native)
                    {
                        oldProfileEditor = ProfilerDriver.profileEditor;
                        oldProfileCpu = ProfilerDriver.IsAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU);
                        nextNativeFrame = ProfilerDriver.lastFrameIndex + 1;
                        nativeEnabled = true;
                        ProfilerDriver.profileEditor = true;
                        ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU, true);
                        ProfilerDriver.enabled = true;
                    }
                    if (!mode.EndsWith("unprofiled", StringComparison.Ordinal))
                    { capture = Static("WhimTexProfiler", "Start", 65536, 85d, null); profiling = true; }
                    phase = 1; next = now + .5; return;
                }
                if (phase == 2)
                {
                    if (capture != null && (int)Static("WhimTexProfiler", "PendingGpu", capture) > 0 && now - finishedAt < 12) return;
                    Save(); Finish("passed", null); return;
                }
                if (phase == 3)
                {
                    if (mode.Contains("-wait") && HistoryBusy()) return;
                    long begin = Stopwatch.GetTimestamp();
                    Undo.IncrementCurrentGroup();
                    Call(layer, "PrepareBufferedStroke", side, side, "Owned second diagnostic stroke");
                    Call(layer, "BeginStroke", Point(0));
                    Call(layer, "PaintPoint", Point(0), side, side, parameters);
                    Set(ui, "paintingLayer", layer); Set(ui, "paintingPressure", 1f);
                    Set(ui, "lastPaintingUv", Point(0)); Set(ui, "hasLastPaintingUv", true);
                    Call(ui, "BeginPaintingInput", Point(0));
                    Set(ui, "nextPaintingCanvasAt", double.MaxValue); Set(ui, "canvasRequested", false);
                    historyEvents.Add(new { kind = "second_begin", begin, end = Stopwatch.GetTimestamp(), pendingPages = Property("PaintTileTransfer", "PendingCount") });
                    stroke = 1; step = 0; phase = 1; last = 0; nextRender = 0;
                    next = now + 1d / 60d; return;
                }
                if (phase == 4)
                {
                    if (HistoryBusy()) return;
                    StopCapture(now); return;
                }
                var f = new Frame { at = Stopwatch.GetTimestamp(), index = step, stroke = stroke, pendingPages = (int)Property("PaintTileTransfer", "PendingCount"), intervalMs = last == 0 ? 0 : (now - last) * 1000 };
                if (step % 30 == 0) SampleMemory(step);
                last = now; next = now + 1d / 60d;
                long start = Stopwatch.GetTimestamp(); int gc = GC.CollectionCount(0);
                Vector2 to = Point(step + 1); f.x = to.x; f.y = to.y;
                if (mode != "idle")
                {
                    segmentArgs[0] = Point(step); segmentArgs[1] = to;
                    Marker(paintMarker, true);
                    try
                    {
                        if (ui != null && mode != "ui-raw")
                        {
                            ulong beforePaint = (ulong)Get(layer, "paintSurfaceRevision");
                            bool hadRequest = (bool)Get(ui, "canvasRequested");
                            object canvas = Get(ui, "toolkitCanvas"); Rect r = (Rect)Get(canvas, "ImageRect");
                            Call(ui, "UpdateCanvasCursor", new Vector2(r.x + to.x * r.width, r.y + (1f - to.y) * r.height), false);
                            Call(ui, "PaintFilteredLayerPoint", to, true, false);
                            if (!hadRequest && beforePaint == (ulong)Get(layer, "paintSurfaceRevision"))
                                test.True(!(bool)Get(ui, "canvasRequested"), "No-stamp input preserves spacing without requesting another composite");
                        }
                        else segment.Invoke(layer, segmentArgs);
                    }
                    finally { Marker(paintMarker, false); }
                    f.stamps = ((System.Collections.ICollection)Get(layer, "segmentStamps")).Count;
                }
                long painted = Stopwatch.GetTimestamp();
                f.paintMs = (painted - start) * 1000d / Stopwatch.Frequency;
                if (now >= nextRender)
                {
                    f.rendered = mode != "stamps" && mode != "ui-stamps"; Marker(renderMarker, true);
                    try { Render(); } finally { Marker(renderMarker, false); }
                    nextRender = now + 1d / 30d;
                }
                long rendered = Stopwatch.GetTimestamp();
                f.renderMs = (rendered - painted) * 1000d / Stopwatch.Frequency;
                RectInt bounds = (RectInt)Get(layer, "brushDirtyBounds"); f.dirtyPixels = (double)bounds.width * bounds.height;
                f.gc0 = GC.CollectionCount(0) - gc;
                f.updateMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                frames.Add(f); step++;
                if (step < Steps) return;
                test.Equal(Steps * (stroke + 1), frames.Count, "Every path point submitted in order");
                test.True(mode == "idle" || f.dirtyPixels >= side * (double)side * .80, "Stroke spans almost the entire canvas");
                if (Post && stroke == 0)
                {
                    firstCommitAt = Stopwatch.GetTimestamp();
                    SampleHistory();
                    Call(ui, "FinishPaintingStroke");
                    historyEvents.Add(new { kind = "commit", begin = firstCommitAt, end = Stopwatch.GetTimestamp(), tiles = Get(layer, "LastPaintTileCount"), pixels = Get(layer, "LastPaintTilePixels") });
                    historyStep = Static("PaintHistoryStore", "Get", (string)Get(Get(layer, "PixelUndoObject"), "Revision"));
                    SampleHistory(); phase = 3; next = EditorApplication.timeSinceStartup + .1; return;
                }
                if (Post) { phase = 4; next = now + .05; return; }
                test.Equal(0, (int)Property("PaintTileTransfer", "PendingCount"), "No Undo transfer entered measured workload");
                StopCapture(now);
            }
            catch (Exception error) { Finish("failed", error); }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
        bool HistoryBusy() => (int)Property("PaintTileTransfer", "PendingCount") != 0 || (long)Property("PaintHistoryStore", "PendingBytes") != 0;
        void StopCapture(double now)
        {
            if (profiling) { Static("WhimTexProfiler", "Stop", "live_trace_complete"); profiling = false; }
            phase = 2; finishedAt = now; next = now + .5;
        }
        void SampleHistory()
        {
            process?.Refresh();
            historySamples.Add(new { at = Stopwatch.GetTimestamp(), phase, stroke, step,
                pendingPages = Property("PaintTileTransfer", "PendingCount"), stagingBytes = Property("PaintTileTransfer", "StagingBytes"),
                pendingWriteBytes = Property("PaintHistoryStore", "PendingBytes"), readbackBytes = Property("PaintTileTransfer", "ReadbackBytes"),
                transferredBytes = Property("PaintTileTransfer", "TransferredBytes"), capacityWaits = Property("PaintTileTransfer", "CapacityWaits"),
                cpuMs = process?.TotalProcessorTime.TotalMilliseconds, workingSet = process?.WorkingSet64 > 0 ? (long?)process.WorkingSet64 : null,
                managedBytes = GC.GetTotalMemory(false), gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2) });
        }
        void Save()
        {
            if (nativeEnabled) { CollectEditorFrames(); ProfilerDriver.SaveProfile(report + "-native.raw"); }
            CollectGpu(paintGpu, paintSamples); CollectGpu(renderGpu, renderSamples);
            object composite = Get(layer, "brushCompositeParameters");
            var parameterState = new Dictionary<string, object>();
            foreach (var field in composite.GetType().GetFields(F))
            {
                var value = field.GetValue(composite);
                parameterState[field.Name] = value is Color color ? new[] { color.r, color.g, color.b, color.a }
                    : value is Texture texture ? texture.GetType().Name + " " + texture.width + "x" + texture.height
                    : field.Name == "Dynamics" ? JsonUtility.ToJson(value) : value;
            }
            var surfaceState = new List<object>();
            foreach (string name in new[] { "paintSurface", "advancedStroke", "brushPreviewSurface" })
            {
                var texture = Get(layer, name) as RenderTexture;
                if (texture != null) surfaceState.Add(new { name, texture.width, texture.height, format = texture.format.ToString(), filter = texture.filterMode.ToString(), texture.anisoLevel });
            }
            if (Get(layer, "advancedStroke") is object strokeBuffer)
                surfaceState.Add(new { name = "strokePages", bytes = Get(strokeBuffer, "Bytes"), touchedBytes = Get(strokeBuffer, "TouchedBytes"),
                    pooledBytes = Property("PaintStrokeBuffer", "PooledBytes") });
            var largeTextures = new List<object>();
            foreach (var texture in Resources.FindObjectsOfTypeAll<RenderTexture>())
                if (texture.width >= 4096 || texture.height >= 4096)
                    largeTextures.Add(new { texture.width, texture.height, format = texture.format.ToString(), texture.enableRandomWrite, created = texture.IsCreated() });
            var historyWorkers = new List<object>();
            if (historyStep != null)
                foreach (var page in (Array)Get(historyStep, "payload"))
                {
                    var task = (System.Threading.Tasks.Task)Get(page, "write");
                    test.True(task.IsCompletedSuccessfully, "Owned history worker completed");
                    var metrics = new Dictionary<string, object>();
                    foreach (string field in new[] { "bytes", "queuedAt", "startedAt", "finishedAt", "sampleTicks", "encodeTicks", "hashTicks", "fileBytes", "compressed" }) metrics[field] = Get(page, field);
                    historyWorkers.Add(metrics);
                }
            File.WriteAllText(report + ".json", Json(new { side, diameter, mode, showWindow, steps = Steps, inputHz = 60, previewHz = 30, previewSize = 512,
                frequency = Stopwatch.Frequency, firstCommitAt, historySamples, historyEvents, historyWorkers,
                unity = Application.unityVersion, device = SystemInfo.graphicsDeviceName, backend = SystemInfo.graphicsDeviceType.ToString(),
                parameterState, surfaceState, largeTextures, memorySamples, graphicsDriverBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver(), layer.colorRange, layer.blendRange,
                frames, editorFrames, paintGpuFrames = paintSamples, renderGpuFrames = renderSamples, nativeCpuFrames = nativeSamples,
                note = "Frame-paced production brush and document render; excludes OS event dispatch. ui-post modes include first-stroke commit, background tile history and second-stroke start; second stroke is cancelled after capture. Other modes exclude commit/Undo. Worker timings include compression and stream I/O wall time, not pure disk or CPU time. GPU records are per-frame intervals with invocation counts and may include waits. No per-sample fence." }));
            if (capture != null) File.WriteAllText(report + "-profile.json", Json(new { frequency = Stopwatch.Frequency, capture }));
            // A bounded final preview, after timing has stopped, verifies the actual drawn figure.
            if (preview == null || ui != null)
            {
                if (preview != null) RenderTexture.ReleaseTemporary(preview);
                preview = (RenderTexture)render.Invoke(doc, renderArgs);
            }
            var image = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = preview; image.ReadPixels(new Rect(0, 0, preview.width, preview.height), 0, 0, false);
                File.WriteAllBytes(report + ".png", image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
            test.True(File.Exists(report + ".json"), "Timing report saved");
        }
        void CollectGpu(ProfilerRecorder recorder, List<object> values)
        {
            if (!recorder.Valid) return;
            recorder.CopyTo(sampleBuffer, true);
            foreach (var s in sampleBuffer) if (s.Count > 0) values.Add(new { ms = s.Value / 1e6, count = s.Count });
        }
        void SampleMemory(int index)
        {
            int count = 0; long bytes = 0;
            foreach (var texture in Resources.FindObjectsOfTypeAll<RenderTexture>())
                if (texture.width >= 4096 || texture.height >= 4096)
                { count++; bytes += (long)texture.width * texture.height * (texture.format == RenderTextureFormat.ARGBFloat ? 16 : 8); }
            memorySamples.Add(new { index, graphicsBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver(), largeRtCount = count, largeRtLogicalBytes = bytes });
        }
        void CollectEditorFrames()
        {
            if (!nativeEnabled) return;
            int end = ProfilerDriver.lastFrameIndex;
            for (int frame = Math.Max(nextNativeFrame, ProfilerDriver.firstFrameIndex); frame <= end; frame++)
                for (int thread = 0; thread < 128; thread++)
                {
                    using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid) break;
                    if (data.threadName != "Main Thread" && data.threadName != "Render Thread") continue;
                    var samples = new List<object>();
                    for (int i = 0; i < data.sampleCount && samples.Count < 80; i++)
                    {
                        double ms = data.GetSampleTimeMs(i);
                        if (ms >= 1) samples.Add(new { name = data.GetSampleName(i), ms });
                    }
                    // Retain slow frames through both strokes instead of filling the budget
                    // with the Editor's frequent sub-8ms idle ticks before the first commit.
                    if (samples.Count > 0 && (!Post || data.frameTimeMs >= 8) && editorFrames.Count < 4000)
                        editorFrames.Add(new { frame, collectedAt = Stopwatch.GetTimestamp(), stroke, step, thread = data.threadName, totalMs = data.frameTimeMs, samples });
                }
            nextNativeFrame = end + 1;
        }
        public void Finish(string status, Exception error)
        {
            if (finished) return; finished = true;
            EditorApplication.update -= Update; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                if (nativeEnabled)
                {
                    ProfilerDriver.enabled = false; ProfilerDriver.profileEditor = oldProfileEditor;
                    ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU, oldProfileCpu); nativeEnabled = false;
                }
                if (profiling) { Static("WhimTexProfiler", "Stop", "live_trace_interrupted"); profiling = false; }
                if (Post && layer != null)
                {
                    Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
                    var cursor = (Object)Get(layer, "PixelUndoObject");
                    if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
                    if (pixels != null) Undo.ClearUndo(pixels);
                    if (doc != null) Undo.ClearUndo(doc);
                }
                process?.Dispose();
                if (window != null) { window.SetPreview(null); window.Close(); window = null; }
                if (ui != null)
                {
                    Set(ui, "paintingLayer", null); Set(ui, "temporaryDocumentDirty", false);
                    // The window owns its document; clear only the test-created objects on close.
                    Call(layer, "CancelStroke"); Call(layer, "ReleaseTransientResources");
                    doc.layers.Clear(); Call(ui, "UpdateUnsavedChangesState"); ui.Close(); ui = null;
                }
                paintGpu.Dispose(); renderGpu.Dispose(); markers?.Release();
                foreach (var recorder in native.Values) recorder.Dispose(); native.Clear();
                if (preview != null) RenderTexture.ReleaseTemporary(preview);
                if (conversion != null) RenderTexture.ReleaseTemporary(conversion);
                (cache as IDisposable)?.Dispose();
                if (layer != null) { Call(layer, "CancelStroke"); Call(layer, "ReleaseTransientResources"); }
                if (doc != null) { doc.layers.Clear(); Object.DestroyImmediate(doc); }
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
            catch (Exception cleanup) { error = error == null ? cleanup : new AggregateException(error, cleanup); status = "failed"; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            state[1] = TestContext.Result(status, test.Checks, report ?? "Diagnostic preparation", error == null ? Array.Empty<string>() : new[] { error.ToString() }).ToJson();
        }
    }
}

public sealed class BrushLiveTraceWindow : EditorWindow
{
    Image image;
    public void CreateGUI()
    {
        rootVisualElement.style.backgroundColor = new Color(.16f, .16f, .16f);
        rootVisualElement.Add(new Label("Owned diagnostic canvas · closes after the measurement"));
        image = new Image { scaleMode = ScaleMode.ScaleToFit }; image.style.flexGrow = 1;
        rootVisualElement.Add(image);
        rootVisualElement.schedule.Execute(() => { image.MarkDirtyRepaint(); Repaint(); }).Every(8);
    }
    public void SetPreview(Texture texture) { if (image != null) image.image = texture; }
}
