using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using WhimTex.Tests;

public sealed class HsvMasterTimingWindow : EditorWindow
{
    public Action repaint;
    void OnGUI() { if (Event.current.type == EventType.Repaint) { var draw = repaint; repaint = null; draw?.Invoke(); } }
}

public static class HsvMasterTiming
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const int Passes = 64, Pairs = 16, Warmups = 2, PrepareCalls = 128;
    static readonly Type Profiler = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly FieldInfo CaptureField = Profiler.GetField("capture", F), CurrentField = Profiler.GetField("current", F);
    static object Field(object target, string name) => target.GetType().GetField(name, F).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, F).SetValue(target, value);
    static string Key(string id) => "WhimTex.Tests.HsvMasterTiming." + Guid.Parse(id).ToString("D");
    static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    delegate Material Prepare<T>(in T context);
    static Func<Material> BindPrepare<T>(ShaderFX effect, object boxed)
    {
        var prepare = (Prepare<T>)typeof(ShaderFX).GetMethod("GetMaterial", F).CreateDelegate(typeof(Prepare<T>), effect);
        T context = (T)boxed;
        return () => prepare(in context);
    }

    [Serializable] public sealed class ApiResult { public bool success; public string error; public CaptureStatus capture; }
    [Serializable] public sealed class CaptureStatus { public bool recording; public int openSpans, pendingGpuSamples; }
    [Serializable] public sealed class Row
    {
        public string effect, mode, gpuStatus;
        public int size, pair, eventIndex, passes = Passes;
        public bool warmup;
        public double prepareUs, gpuMs;
        public double referenceGpuMs;
        public long referenceCount;
        public string referenceStatus;
        public int poolSlot = -1, poolGeneration;
        public string[] nativeSamples;
        public List<string> referenceHistory = new List<string>();
        public string outputFormat, workload, legacyStatus;
        public double wallBatchMs, preDrainMs, legacyGpuMs;
        public int legacyCount;
        public List<string> legacyHistory = new List<string>();
    }
    [Serializable] public sealed class Report
    {
        public string device, backend, unity, capturePath, note, actualThreading;
        public string probe;
        public int pairs = Pairs, passes = Passes, warmups = Warmups, prepareCalls = PrepareCalls;
        public float hue = 35, saturation = .75f, lightness = 20;
        public double maximumPixelDifference;
        public List<Row> samples = new List<Row>();
    }

    public static string Start(string id) => StartCore(id, null);
    public static string StartAudit(string id, string method) => StartCore(id, method);
    public static string StartProbe(string id, string profile) => StartCore(id, "probe-" + profile);
    static string StartCore(string id, string method)
    {
        if (method != null && method != "split" && method != "single" && method != "same" && method != "repaint" &&
            method != "probe-formats" && method != "probe-scaling" && method != "probe-formats-single" && method != "probe-observe" && method != "probe-sync")
            return TestContext.Result("failed", 0, "Unknown GPU audit submission method").ToJson();
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null) return TestContext.Result("failed", 0, "Duplicate HSV timing fixture").ToJson();
        var status = JsonUtility.FromJson<ApiResult>(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"status\"}"));
        if (!status.success) return TestContext.Result("failed", 0, "Cannot inspect profiler status").ToJson();
        if (status.capture != null && (status.capture.recording || status.capture.openSpans != 0 || status.capture.pendingGpuSamples != 0))
            return TestContext.Result("skipped", 0, "Requires idle profiler; current capture retained").ToJson();
        if (!SystemInfo.supportsGpuRecorder) return TestContext.Result("skipped", 0, "Native GPU timestamps unavailable").ToJson();
        string unsupported = (string)Profiler.GetProperty("GpuSupportStatus", F)?.GetValue(null);
        if (unsupported != null) return TestContext.Result("skipped", 0, unsupported + ": restart with a supported actual rendering threading mode before timing").ToJson();
        var fixture = new Fixture(id, method);
        AppDomain.CurrentDomain.SetData(Key(id), (Func<string>)fixture.Poll);
        AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", (Action)fixture.Dispose);
        fixture.Begin(); return fixture.Poll();
    }
    public static string Poll(string id) => AppDomain.CurrentDomain.GetData(Key(id)) is Func<string> poll
        ? poll() : TestContext.Result("skipped", 0, "No HSV timing fixture was started").ToJson();
    public static string Cleanup(string id) => TestContext.Run("Release HSV timing fixture", t =>
    {
        (AppDomain.CurrentDomain.GetData(Key(id) + ".cleanup") as Action)?.Invoke();
        AppDomain.CurrentDomain.SetData(Key(id), null); AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", null);
        t.True(true, "Only owned resources released");
    });

    sealed class Fixture : IDisposable
    {
        readonly string id;
        readonly object original = CaptureField.GetValue(null);
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly TestContext checks = new TestContext();
        readonly Report report = new Report();
        readonly string audit;
        readonly List<ProfilerRecorder> references = new List<ProfilerRecorder>();
        readonly List<UnityEngine.Profiling.Recorder> legacy = new List<UnityEngine.Profiling.Recorder>();
        readonly string probe;
        readonly bool singleBatch;
        CommandBuffer auditCommands;
        readonly List<ProfilerRecorderSample> referenceDrain = new List<ProfilerRecorderSample>(8);
        int settle;
        bool scheduled;
        readonly EditorWindow previousFocus = EditorWindow.focusedWindow;
        WhimTexDocument document;
        ShaderFX[] effects;
        Func<Material>[] prepare;
        Material[] materials;
        Texture2D input, pixels;
        RenderTexture target;
        HsvMasterTimingWindow window;
        object capture;
        int drawPasses = Passes;
        RenderTextureFormat outputFormat = RenderTextureFormat.ARGBFloat;
        string workload;
        int configuration, pair = -Warmups;
        double deadline;
        string result;
        bool disposed;
        public Fixture(string id, string audit)
        {
            this.id = id; this.audit = audit; singleBatch = audit == "single" || audit?.EndsWith("-single") == true;
            probe = audit?.StartsWith("probe-") == true ? audit.Substring(6).Replace("-single", "") : null;
        }
        int Configurations => probe == "formats" ? 7 : probe == "scaling" || probe == "observe" ? 5 : 6;
        T Own<T>(T value) where T : UnityEngine.Object { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        void Activate(Action body)
        {
            checks.True(ReferenceEquals(original, CaptureField.GetValue(null)) && (int)CurrentField.GetValue(null) == -1,
                "Retained user capture and active scopes stay untouched");
            CaptureField.SetValue(null, capture);
            try { body(); }
            finally { CaptureField.SetValue(null, original); CurrentField.SetValue(null, -1); }
        }
        ShaderFX Catalog(string path)
        {
            var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
            object entry = null;
            foreach (var candidate in (IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
                if ((string)Field(candidate, "path") == path) entry = candidate;
            checks.True(entry != null, "Built-in catalog entry exists: " + path);
            return Own((ShaderFX)typeof(ShaderFX).GetMethod("FromCatalog", F).Invoke(null, new[] { (object)document, entry }));
        }
        void Value(ShaderFX effect, string name, float value)
        {
            var parameters = (List<ShaderFXParameter>)Field(effect, "parameters");
            var parameter = parameters.Find(p => p.name == name);
            checks.True(parameter != null, "Expected parameter " + name); parameter.floatValue = value;
        }
        public void Begin()
        {
            try
            {
                deadline = EditorApplication.timeSinceStartup + 50;
                document = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
                const string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/HueSaturation.hlsl";
                effects = new[] { Catalog(path), Catalog(path) };
                foreach (var fx in effects)
                {
                    for (int i = 1; i <= 4; i++) Value(fx, "_Range" + i + "Enabled", 0);
                    Value(fx, "_Output", 0); Value(fx, "_Opacity", 1);
                }
                // Empty enabled coverage forces the range path without changing correction.
                Value(effects[0], "_Range1Enabled", 1); Value(effects[0], "_Range1Feather", 0);
                ((List<ShaderFXParameter>)Field(effects[0], "parameters")).Find(p => p.name == "_Range1").vectorValue = Vector4.zero;
                input = Own(new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp });
                var colors = new Color[256 * 256];
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                {
                    var color = Color.HSVToRGB(x / 256f, y / 255f, .15f + (y % 47) / 20f, true).linear;
                    color.a = .25f + .75f * ((x + y) % 37) / 36f; colors[y * 256 + x] = color;
                }
                input.SetPixels(colors); input.Apply(false, false);
                pixels = Own(new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true));
                capture = Activator.CreateInstance(Profiler.GetNestedType("Capture", F), true);
                long now = Stopwatch.GetTimestamp();
                Set(capture, "id", Guid.NewGuid().ToString("N")); Set(capture, "start", now);
                Set(capture, "deadline", now + 180 * Stopwatch.Frequency); Set(capture, "recording", true);
                Set(capture, "events", Array.CreateInstance(Profiler.GetNestedType("Sample", F), 1024));
                Profiler.GetMethod("PrepareGpuCapture", F).Invoke(null, new[] { capture });
                report.device = SystemInfo.graphicsDeviceName; report.backend = SystemInfo.graphicsDeviceType.ToString(); report.unity = Application.unityVersion;
                report.actualThreading = SystemInfo.renderingThreadingMode.ToString();
                report.probe = probe;
                report.note = "Paired AB/BA batches, 64 independent blits from the same mixed-color/HDR input, ARGBFloat output. Native WhimTex GPU marker intervals normalized per pass include submission gaps; not pure shader execution or FPS. CPU preparation is warmed GetMaterial wall time over 128 direct-delegate calls; no reflective call per iteration. Compilation, texture upload, parity readback, setup/cleanup and warmup samples excluded from reported statistics. No inspector preview rendering. The full range path uses one enabled empty interval; the Master fast path disables all ranges. Both use identical Hue/Saturation/Lightness settings, Output Result and Opacity 1.";
                if (audit != null) report.note += " Audit: fresh, single-use reference GPU markers; submission method=" + audit + ". Not a performance comparison with the ordinary path.";
                if (probe != null) report.note += " Probe: pre-drained GPU queue and synchronous one-pixel readback after the measured batch provide independent CPU wall timing including submission/readback overhead. Legacy Recorder observes the fresh marker separately. Per-row passes/format/size override baseline settings; zero-pass values are empty-interval controls, not per-pass costs.";
                window = Own(ScriptableObject.CreateInstance<HsvMasterTimingWindow>());
                window.titleContent = new GUIContent("HSV timing"); window.position = new Rect(100, 100, 280, 55);
                window.rootVisualElement.Add(new UnityEngine.UIElements.Label("Comparing Hue & Saturation range path and Master fast path…"));
                window.ShowUtility();
                Setup(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Fail(error); }
        }
        void Setup()
        {
            int size = 512 << (configuration % 3); bool neutral = configuration >= 3;
            if (probe != null)
            {
                neutral = false;
                if (probe == "formats")
                {
                    size = new[] { 1024, 1536, 2048, 2048, 2048, 2896, 4096 }[configuration];
                    outputFormat = new[] { RenderTextureFormat.ARGBFloat, RenderTextureFormat.ARGBFloat,
                        RenderTextureFormat.ARGBFloat, RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGB32,
                        RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGB32 }[configuration];
                }
                else { size = 2048; drawPasses = probe == "sync" ? 64 : new[] { 0, 1, 8, 64, 128 }[configuration]; }
                workload = probe + "/" + configuration;
            }
            document.width = document.height = size;
            foreach (var fx in effects)
            {
                Value(fx, "_MasterHue", neutral ? 0 : 35); Value(fx, "_MasterSaturation", neutral ? 1 : .75f);
                Value(fx, "_MasterLightness", neutral ? 0 : 20);
            }
            var contextType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            object context = Activator.CreateInstance(contextType, document, null, size, size, 1f, true, true, null);
            prepare = new Func<Material>[2]; materials = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                prepare[i] = (Func<Material>)typeof(HsvMasterTiming).GetMethod("BindPrepare", F).MakeGenericMethod(contextType).Invoke(null, new[] { (object)effects[i], context });
                for (int n = 0; n < 16; n++) materials[i] = prepare[i]();
                checks.True(materials[i] != null, "Compiled material is available");
            }
            if (audit == "same") materials[1] = materials[0];
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var small = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(input, small, materials[0]); RenderTexture.active = small;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false); var expected = pixels.GetPixels();
                Graphics.Blit(input, small, materials[1]); RenderTexture.active = small;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false); var actual = pixels.GetPixels();
                for (int n = 0; n < expected.Length; n++) for (int c = 0; c < 4; c++)
                {
                    double delta = Math.Abs(actual[n][c] - expected[n][c]);
                    report.maximumPixelDifference = Math.Max(report.maximumPixelDifference, delta);
                    checks.True(delta <= Math.Max(.0005, Math.Abs(expected[n][c]) * .003), "Matching range/Master output");
                }
            }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(small); }
            checks.True(SystemInfo.SupportsRenderTextureFormat(outputFormat), "Probe output format supported");
            target = new RenderTexture(size, size, 0, outputFormat, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
            checks.True(target.Create(), "Owned output created");
            if (probe != null) { Graphics.Blit(input, target, materials[0]); Drain(); }
        }
        void Tick()
        {
            if (result != null || disposed) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("HSV timing deadline");
                window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
                string availability = (string)Profiler.GetProperty("GpuAvailability", F).GetValue(null);
                if (availability == "warming_up") return;
                checks.Equal("ready", availability, "Native GPU timing available");
                if ((int)Profiler.GetMethod("PendingGpu", F).Invoke(null, new[] { capture }) > 0) return;
                if (!PollReferences()) return;
                ReadPooledResults();
                if (scheduled) return;
                if (pair >= Pairs)
                {
                    if (audit != null && configuration == Configurations - 1 && ++settle < 20) return;
                    target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null;
                    if (++configuration >= Configurations) { Finish(); return; }
                    pair = -Warmups; Setup();
                }
                if (audit == "repaint") { scheduled = true; window.repaint = DrawPair; return; }
                DrawPair();
            }
            catch (Exception error) { Fail(error); }
        }
        void DrawPair()
        {
            try
            {
                scheduled = false;
                var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = false;
                    int first = (pair & 1) == 0 ? 0 : 1;
                    Batch(first); Batch(1 - first); pair++;
                }
                finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
            }
            catch (Exception error) { Fail(error); }
        }
        void Batch(int effect)
        {
            long start = Stopwatch.GetTimestamp();
            for (int n = 0; n < PrepareCalls; n++) prepare[effect]();
            double prepareUs = (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency / PrepareCalls;
            var row = new Row { effect = effect == 0 ? "Hue & Saturation range path" : "Hue & Saturation Master", mode = configuration >= 3 ? "neutral" : "active",
                size = document.width, pair = pair, warmup = pair < 0, prepareUs = prepareUs };
            row.passes = drawPasses; row.outputFormat = outputFormat.ToString(); row.workload = workload;
            if (probe != null) row.mode = "active";
            ProfilerMarker referenceMarker = default;
            bool fresh = audit != null && (probe != "sync" || configuration != 1);
            if (fresh)
            {
                string name = "WhimTex.Tests.GpuAudit." + id + "." + audit + "." + report.samples.Count;
                referenceMarker = new ProfilerMarker(ProfilerCategory.Render, name, MarkerFlags.SampleGPU);
                references.Add(new ProfilerRecorder(referenceMarker, probe == "observe" ? 512 : 8,
                    ProfilerRecorderOptions.Default | ProfilerRecorderOptions.GpuRecorder | ProfilerRecorderOptions.StartImmediately));
                row.referenceStatus = "pending";
                if (probe != null)
                {
                    var recorder = UnityEngine.Profiling.Recorder.Get(name);
                    row.legacyStatus = recorder.isValid ? "pending" : "unsupported";
                    if (recorder.isValid) recorder.enabled = true;
                    legacy.Add(recorder);
                }
            }
            else if (audit != null) { references.Add(default); legacy.Add(null); row.referenceStatus = "not_measured"; }
            if (probe != null && (probe != "sync" || configuration == 1 || configuration == 2 || configuration == 3 || configuration == 5))
            { start = Stopwatch.GetTimestamp(); Drain(); row.preDrainMs = Elapsed(start); }
            long wallStart = Stopwatch.GetTimestamp();
            Activate(() =>
            {
                row.eventIndex = (int)Field(capture, "count");
                var scope = Profiler.GetMethod("Measure", F).Invoke(null, new object[] { "fx.render", document, null, effect, row.effect + "/" + row.mode + "/" + row.size });
                try
                {
                    using (var gpu = (IDisposable)Invoke(scope, "Gpu"))
                    {
                        if (singleBatch || (probe == "sync" && configuration == 5))
                        {
                            auditCommands ??= new CommandBuffer(); auditCommands.Clear();
                            auditCommands.BeginSample(referenceMarker);
                            for (int n = 0; n < drawPasses; n++) auditCommands.Blit(input, target, materials[effect]);
                            auditCommands.EndSample(referenceMarker); Graphics.ExecuteCommandBuffer(auditCommands);
                        }
                        else
                        {
                            if (fresh) Profiler.GetMethod("SubmitGpuMarker", F).Invoke(null, new object[] { referenceMarker, true });
                            for (int n = 0; n < drawPasses; n++) Graphics.Blit(input, target, materials[effect]);
                            if (fresh) Profiler.GetMethod("SubmitGpuMarker", F).Invoke(null, new object[] { referenceMarker, false });
                        }
                    }
                }
                finally { ((IDisposable)scope).Dispose(); }
                var slots = (IList)Profiler.GetField("gpuSlots", F).GetValue(null);
                for (int n = 0; n < slots.Count; n++)
                    if (ReferenceEquals(Field(slots[n], "owner"), capture) && (int)Field(slots[n], "sample") == row.eventIndex)
                    { row.poolSlot = n; row.poolGeneration = (int)Field(slots[n], "generation"); break; }
            });
            if (probe != null)
            {
                if (probe != "sync" || configuration == 1 || configuration == 2 || configuration == 4 || configuration == 5) Drain();
                row.wallBatchMs = Elapsed(wallStart);
            }
            report.samples.Add(row);
        }
        static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        void Drain()
        {
            if (probe == "sync" && configuration == 2)
            {
                var request = AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1);
                request.WaitForCompletion(); checks.True(!request.hasError, "Owned asynchronous readback completion"); return;
            }
            var previous = RenderTexture.active;
            try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); }
            finally { RenderTexture.active = previous; }
        }
        bool PollReferences()
        {
            bool ready = true;
            for (int n = 0; n < references.Count; n++)
            {
                var row = report.samples[n];
                if (probe != null && legacy[n] != null && legacy[n].isValid && legacy[n].gpuSampleBlockCount != 0)
                {
                    long value = legacy[n].gpuElapsedNanoseconds; int count = legacy[n].gpuSampleBlockCount;
                    string raw = value + "/" + count;
                    if (row.legacyHistory.Count == 0 || row.legacyHistory[row.legacyHistory.Count - 1] != raw) row.legacyHistory.Add(raw);
                    if (row.legacyStatus == "pending")
                    { row.legacyGpuMs = value / 1e6 / Math.Max(1, row.passes); row.legacyCount = count; row.legacyStatus = value > 0 && count == 1 ? "ready" : "ambiguous"; }
                }
                var recorder = references[n];
                if (!recorder.Valid) continue;
                recorder.CopyTo(referenceDrain, probe != "observe");
                foreach (var sample in referenceDrain)
                {
                    if (sample.Count == 0) continue;
                    string raw = sample.Value + "/" + sample.Count;
                    if (probe == "observe" && row.referenceHistory.Contains(raw)) continue;
                    row.referenceHistory.Add(raw);
                    if (row.referenceStatus != "pending") continue;
                    row.referenceCount = sample.Count;
                    row.referenceGpuMs = sample.Value / 1e6 / Math.Max(1, row.passes);
                    row.referenceStatus = sample.Count == 1 && sample.Value > 0 ? "ready" : "ambiguous";
                }
                if (row.referenceHistory.Count > 1) row.referenceStatus = "ambiguous";
                ready &= row.referenceStatus != "pending";
            }
            return ready;
        }
        void ReadPooledResults()
        {
            if (audit == null) return;
            var events = (Array)Field(capture, "events");
            var slots = (IList)Profiler.GetField("gpuSlots", F).GetValue(null);
            foreach (var row in report.samples)
            {
                if (row.nativeSamples != null || row.poolSlot < 0) continue;
                var slot = slots[row.poolSlot];
                if ((int)Field(slot, "generation") != row.poolGeneration) continue;
                if ((string)Field(events.GetValue(row.eventIndex), "gpuStatus") == "pending") continue;
                var values = (IEnumerable)Field(slot, "samples");
                var raw = new List<string>();
                foreach (ProfilerRecorderSample sample in values) raw.Add(sample.Value + "/" + sample.Count);
                row.nativeSamples = raw.ToArray();
            }
        }
        void Finish()
        {
            var samples = (Array)Field(capture, "events");
            foreach (var row in report.samples)
            {
                object sample = samples.GetValue(row.eventIndex);
                row.gpuStatus = (string)Field(sample, "gpuStatus");
                if (audit == null) checks.Equal("ready", row.gpuStatus, "Every measured GPU batch completed");
                row.gpuMs = (long)Field(sample, "gpuNanoseconds") / 1e6 / Math.Max(1, row.passes);
                if (audit == null) checks.True(row.gpuMs > 0, "Nonzero native GPU interval");
            }
            Set(capture, "recording", false); Set(capture, "end", Stopwatch.GetTimestamp()); Set(capture, "stopReason", "benchmark_completed");
            string folder = "Temp/WhimTex/hsv-master-timing/" + id + (audit == null ? "" : "-" + audit);
            report.capturePath = folder + "/capture.json";
            Activate(() => checks.True(JsonUtility.FromJson<ApiResult>(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"export\",\"captureId\":\"" + (string)Field(capture, "id") + "\",\"outputPath\":\"" + report.capturePath + "\"}")).success,
                "Raw intervals exported through the existing profiler API"));
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), folder, "timing.json");
            checks.Equal((Pairs + Warmups) * Configurations * 2, report.samples.Count, "All paired samples retained");
            var serializer = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
            string json = (string)serializer.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            checks.True(json.Contains("\"samples\":[{") && json.Contains("\"prepareUs\":"), "Raw timing arrays are serialized");
            using (var file = new StreamWriter(new FileStream(full, FileMode.CreateNew, FileAccess.Write))) file.Write(json);
            result = TestContext.Result("passed", checks.Checks, "HSV/Master paired timing: " + folder + "/timing.json").ToJson(); Dispose();
        }
        void Fail(Exception error)
        {
            result = TestContext.Result("failed", checks.Checks, "HSV/Master timing", error.ToString()).ToJson(); Dispose();
        }
        public string Poll() => result ?? TestContext.Result("running", 0, "HSV/Master timing, configuration " + configuration + "/6, pair " + pair + "/" + Pairs).ToJson();
        public void Dispose()
        {
            if (disposed) return;
            EditorApplication.update -= Tick;
            if (window != null) window.repaint = null;
            if (capture != null)
            {
                Set(capture, "recording", false);
                int pending = (int)Profiler.GetMethod("PendingGpu", F).Invoke(null, new[] { capture });
                if (pending != 0)
                {
                    string recovery = "Owned GPU cleanup unfinished: " + pending +
                        " samples still pending. Retain ownership and retry Cleanup after profiler polling drains them; do not start another timing run.";
                    result = TestContext.Result("failed", checks.Checks, recovery, "Prior result: " + result).ToJson();
                    throw new InvalidOperationException(recovery);
                }
            }
            var errors = new List<Exception>();
            void ReleaseOwned(Action release)
            {
                try { release(); }
                catch (Exception error) { errors.Add(error); }
            }
            ReleaseOwned(() =>
            {
                if (window == null) return;
                try { if (window.rootVisualElement.panel != null) window.Close(); }
                finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
            });
            ReleaseOwned(() =>
            {
                if (target == null) return;
                try { target.Release(); }
                finally { UnityEngine.Object.DestroyImmediate(target); }
                target = null;
            });
            for (int n = references.Count - 1; n >= 0; n--)
            {
                int index = n;
                ReleaseOwned(() => { references[index].Dispose(); references.RemoveAt(index); });
            }
            for (int n = legacy.Count - 1; n >= 0; n--)
            {
                int index = n;
                ReleaseOwned(() =>
                {
                    var recorder = legacy[index];
                    if (recorder != null && recorder.isValid) recorder.enabled = false;
                    legacy.RemoveAt(index);
                });
            }
            ReleaseOwned(() => { auditCommands?.Release(); auditCommands = null; });
            for (int n = owned.Count - 1; n >= 0; n--)
            {
                var resource = owned[n];
                ReleaseOwned(() => { if (resource != null) UnityEngine.Object.DestroyImmediate(resource); });
            }
            ReleaseOwned(() => { if (previousFocus != null) previousFocus.Focus(); });
            if (errors.Count != 0)
            {
                var failure = new AggregateException("Owned HSV timing cleanup unfinished; retry Cleanup before another run", errors);
                result = TestContext.Result("failed", checks.Checks, "Cleanup requires recovery; prior result: " + result, failure.ToString()).ToJson();
                throw failure;
            }
            disposed = true;
        }
    }
}
