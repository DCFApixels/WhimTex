using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using AsyncGPUReadback = UnityEngine.Rendering.AsyncGPUReadback;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = ProfilerTests.Json;

public static class ProfilerFxTests
{
    public sealed class FrameWindow : EditorWindow { }
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Profiler = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly FieldInfo CaptureField = Profiler.GetField("capture", Hidden);
    static string Key(string id) => "WhimTex.Tests.ProfilerFx." + id;
    static object Field(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    static Json Call(string action) => Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"" + action + "\"" + (action == "read" ? ",\"limit\":2000" : "") + "}"));

    public static string Start(string id)
    {
        string unsupported = (string)Profiler.GetProperty("GpuSupportStatus", Hidden)?.GetValue(null);
        if (unsupported != null) return TestContext.Result("skipped", 0, "Live GPU delivery requires a supported actual threading mode: " + unsupported).ToJson();
        var status = Call("status")["capture"];
        if (status.Type != "Null" && ((bool)status["recording"] || (int)status["openSpans"] != 0 || (int)status["pendingGpuSamples"] != 0))
            return TestContext.Result("skipped", 0, "Requires an idle profiler; retained stopped capture is preserved.").ToJson();
        var fixture = new Fixture(id);
        AppDomain.CurrentDomain.SetData(Key(id) + ".poll", (Func<string>)fixture.Poll);
        AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", (Action)fixture.Dispose);
        fixture.Begin();
        return fixture.Poll();
    }
    public static string Poll(string id) => ((Func<string>)AppDomain.CurrentDomain.GetData(Key(id) + ".poll"))();
    public static string Cancel(string id) => Cleanup(id);
    public static string Cleanup(string id) => TestContext.Run("Release owned FX profiler fixture", t =>
    {
        (AppDomain.CurrentDomain.GetData(Key(id) + ".cleanup") as Action)?.Invoke();
        AppDomain.CurrentDomain.SetData(Key(id) + ".poll", null);
        AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", null);
        t.True(true, "Owned queries/documents/windows released; user capture untouched");
    });

    sealed class Fixture : IDisposable
    {
        readonly TestContext t = new TestContext();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly object original = CaptureField.GetValue(null);
        readonly string originalStatus = Call("status").ToString();
        readonly string originalEvents;
        readonly string id;
        object capture;
        WhimTexDocument document;
        Layer layer;
        Color[] baseline;
        string result;
        Json first;
        int batch;
        double deadline;
        bool disposed;
        EditorWindow frameWindow;
        readonly EditorWindow previousFocus = EditorWindow.focusedWindow;
        public Fixture(string id)
        {
            this.id = id;
            originalEvents = original == null ? null : Call("read")["events"].ToString();
        }
        T Own<T>(T value) where T : UnityEngine.Object { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        void Activate(Action body)
        {
            t.True(ReferenceEquals(original, CaptureField.GetValue(null)), "Never overwrite a newly started user capture");
            t.Equal(-1, (int)Profiler.GetField("current", Hidden).GetValue(null), "No active user scope");
            CaptureField.SetValue(null, capture);
            try { body(); }
            finally { CaptureField.SetValue(null, original); Profiler.GetField("current", Hidden).SetValue(null, -1); }
        }
        Json Read()
        {
            Json value = null;
            Activate(() => value = Call("read"));
            t.True((bool)value["success"], "Own capture can be read through production API");
            return value;
        }
        ShaderFX Fx(string name, string code)
        {
            var method = typeof(ShaderFX).GetMethod("CreateAgentDraft", Hidden, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            var value = Own((ShaderFX)method.Invoke(null, new object[] { document, code, new List<ShaderFXParameter>() }));
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", Instance).Invoke(value, null);
            value.name = name;
            t.True(typeof(ShaderFX).GetField("compiledShader", Instance).GetValue(value) != null, "Owned shader compiles: " + name);
            return value;
        }
        Color[] Render()
        {
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            Texture2D result = null;
            RenderTexture target = null;
            try
            {
                typeof(WhimTexDocument).GetMethod("NormalizeModel", Instance).Invoke(document, null);
                target = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderCanvas", Instance).Invoke(document, new object[] { 128 });
                var readback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAHalf);
                readback.WaitForCompletion(); t.True(!readback.hasError, "Owned parity readback succeeds without ReadPixels");
                result = new Texture2D(target.width, target.height, TextureFormat.RGBAHalf, false, true);
                result.LoadRawTextureData(readback.GetData<byte>());
                return result.GetPixels();
            }
            finally
            {
                if (result != null) UnityEngine.Object.DestroyImmediate(result);
                if (target != null)
                {
                    Profiler.GetMethod("ReleaseTexture", Hidden).Invoke(null, new object[] { target });
                    RenderTexture.ReleaseTemporary(target);
                }
                RenderTexture.active = active; GL.sRGBWrite = srgb;
            }
        }
        public void Begin()
        {
            try
            {
                document = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
                document.width = document.height = 128;
                layer = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.UV };
                layer.layerName = "FX child";
                var tint = Fx("Tint", "float4 ApplyFX(float2 uv,float4 color){color.rgb *= .7; return color;}");
                var disabled = Fx("Disabled", "float4 ApplyFX(float2 uv,float4 color){return 0;}"); disabled.Active = false;
                layer.fx.Add(tint); layer.fx.Add(disabled);
                layer.fx.Add(Fx("Second HLSL FX", "float4 ApplyFX(float2 uv,float4 color){color.rgb *= .7; return color;}"));
                Layer dependency = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.UV };
                dependency.layerName = "FX input"; dependency.enabled = false;
                dependency.fx.Add(Fx("Input tint", "float4 ApplyFX(float2 uv,float4 color){return color * .8;}"));
                Layer group = new GroupLayerBehaviour(); group.layerName = "FX group"; group.children.Add(layer);
                var target = Fx("Sample input", "// @param texture2D _Map\nfloat4 ApplyFX(float2 uv,float4 color){return color * tex2D(_Map,uv);}");
                group.fx.Add(target); document.layers.Add(group); document.layers.Add(dependency);
                typeof(WhimTexDocument).GetMethod("NormalizeModel", Instance).Invoke(document, null);
                var parameter = ((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Instance).GetValue(target))[0];
                parameter.textureSource = ShaderFXTextureSource.Layer; parameter.textureLayerId = dependency.Id;
                baseline = Render();
                var type = Profiler.GetNestedType("Capture", BindingFlags.NonPublic);
                capture = Activator.CreateInstance(type, true);
                long now = Stopwatch.GetTimestamp();
                Set(capture, "id", Guid.NewGuid().ToString("N")); Set(capture, "start", now);
                Set(capture, "deadline", now + 60 * Stopwatch.Frequency);
                Set(capture, "events", Array.CreateInstance(Profiler.GetNestedType("Sample", BindingFlags.NonPublic), 4096));
                Set(capture, "recording", true);
                Profiler.GetMethod("PrepareGpuCapture", Hidden).Invoke(null, new[] { capture });
                frameWindow = Own(ScriptableObject.CreateInstance<FrameWindow>());
                frameWindow.titleContent = new GUIContent("FX timing check");
                frameWindow.position = new Rect(100, 100, 260, 75);
                frameWindow.rootVisualElement.Add(new Label("Waiting for native GPU timestamps…"));
                frameWindow.ShowUtility();
                EditorApplication.update += Tick;
                deadline = EditorApplication.timeSinceStartup + 22;
            }
            catch (Exception e) { Fail(e); }
        }
        void RunBatch()
        {
            Set(capture, "recording", true);
            Activate(() =>
            {
                ExerciseEncode();
                t.True(Render().SequenceEqual(baseline), "Composite/group/Target pixels exactly equal with profiling on/off");
            });
            Set(capture, "end", Stopwatch.GetTimestamp()); Set(capture, "stopReason", "test");
            t.Equal(0, (int)Field(capture, "open"), "CPU scopes close including dependencies");
            batch++;
            if (batch == 3) CheckStopWithPendingGpu();
        }
        void ExerciseEncode()
        {
            // ComposeCanvas uses linear readback, so explicitly exercise the distinct
            // encoded-copy path instead of expecting it in every canvas composition.
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var source = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
            Texture2D encoded = null;
            try
            {
                Graphics.Blit(Texture2D.whiteTexture, source);
                encoded = (Texture2D)typeof(WhimTexDocument).GetMethod("CopyToTexture2D", Hidden)
                    .Invoke(null, new object[] { source, false });
                t.True(encoded != null && encoded.width == 128, "Encoded-copy GPU path actually executed");
            }
            finally
            {
                if (encoded != null) UnityEngine.Object.DestroyImmediate(encoded);
                RenderTexture.active = active; GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(source);
            }
        }
        void CheckStopWithPendingGpu()
        {
            Activate(() =>
            {
                string identity = (string)Field(capture, "id");
                var stopped = Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"stop\",\"captureId\":\"" + identity + "\"}"));
                t.True((bool)stopped["success"] && !(bool)stopped["capture"]["recording"], "Stop closes CPU recording without cancelling submitted GPU work");
                if ((int)stopped["capture"]["pendingGpuSamples"] == 0) return;
                var exported = Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"export\",\"captureId\":\"" + identity + "\",\"outputPath\":\"Temp/WhimTex/profiler-fx-pending-" + id + ".json\"}"));
                t.True(!(bool)exported["success"], "Export refuses incomplete GPU results");
                t.Equal("capture_gpu_pending", (string)exported["errorCode"], "Explicit pending GPU export diagnostic");
            });
            using var stream = new MemoryStream();
            Profiler.GetMethod("WriteSnapshot", Hidden).Invoke(null, new[] { (object)stream, capture });
            stream.Position = 0;
            object retained = capture;
            capture = Profiler.GetMethod("ReadSnapshot", Hidden).Invoke(null, new object[] { stream });
            try
            {
                var snapshot = Read();
                t.True(snapshot["events"].All(e => (string)e["gpuStatus"] != "pending"), "Reload snapshot never leaves orphaned pending GPU queries");
                foreach (var row in snapshot["events"].Where(e => (string)e["gpuStatus"] == "interrupted"))
                    t.Equal("Null", row["gpuMs"].Type, "Interrupted GPU samples have no fabricated duration");
            }
            finally { capture = retained; }
        }
        void Tick()
        {
            if (result != null || disposed) return;
            try
            {
                frameWindow.Repaint();
                if (batch == 0)
                {
                    EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
                    string availability = (string)Profiler.GetProperty("GpuAvailability", Hidden).GetValue(null);
                    if (availability == "warming_up") return;
                    t.Equal(SystemInfo.supportsGpuRecorder ? "ready" : "unsupported", availability, "Native GPU activation");
                    RunBatch();
                    return;
                }
                int pending = (int)Profiler.GetMethod("PendingGpu", Hidden).Invoke(null, new[] { capture });
                if (pending > 0)
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new Exception("GPU queries did not drain before fixture deadline");
                    // Advance ordinary Editor frames for delayed public GPU counters, without blocking/readback waits.
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                    return;
                }
                var report = Read();
                File.WriteAllText("Temp/WhimTex/profiler-fx-" + id + ".json", report.ToString());
                Check(report);
                if (batch < 3)
                {
                    if (first == null) first = report;
                    RunBatch();
                    return;
                }
                Set(capture, "recording", false); Set(capture, "end", Stopwatch.GetTimestamp());
                t.Equal(first["events"].ToString(), Json.FromObject(report["events"].Take(first["events"].Count).Select(e => e.Raw).ToArray()).ToString(),
                    "Reused GPU slots never change completed earlier invocation timings");
                CheckUI();
                CheckPersistence(report);
                File.WriteAllText("Temp/WhimTex/profiler-fx-" + id + ".json", report.ToString());
                int gpu = report["events"].Count(e => (string)e["gpuStatus"] == "ready");
                int unavailable = report["events"].Count(e => (string)e["gpuStatus"] != "ready" && (string)e["gpuStatus"] != "not_measured");
                t.True(ReferenceEquals(original, CaptureField.GetValue(null)), "Original capture reference retained");
                t.Equal(originalStatus, Call("status").ToString(), "Original capture metadata retained");
                if (originalEvents != null) t.Equal(originalEvents, Call("read")["events"].ToString(), "Original user event data untouched");
                result = TestContext.Result(gpu == 0 ? "skipped" : "passed", t.Checks,
                    "FX CPU/GPU capture: " + gpu + " ready, " + unavailable + " unavailable; supportsGpuRecorder=" + SystemInfo.supportsGpuRecorder +
                    "; " + SystemInfo.graphicsDeviceType + "; report Temp/WhimTex/profiler-fx-" + id + ".json").ToJson();
            }
            catch (Exception e) { Fail(e); }
        }
        void Check(Json report)
        {
            var rows = report["events"].ToArray();
            foreach (string phase in new[] { "render.blend", "layer.generate", "layer.transform", "transfer.encode" })
            {
                var measured = rows.Where(e => (string)e["phase"] == phase).ToArray();
                t.True(measured.Length > 0, "Actual non-FX GPU boundary: " + phase);
                foreach (var sample in measured)
                {
                    string state = (string)sample["gpuStatus"];
                    t.True(state != "pending" && state != "not_measured", "Non-FX measurement reaches terminal state: " + phase);
                    t.Equal(state == "ready", sample["gpuMs"].Type != "Null", "No fake non-FX GPU values");
                    if (phase == "transfer.encode") t.Equal("invalidated_by_readback", state, "Production encoded readback invalidates its pending interval");
                    else if (SystemInfo.supportsGpuRecorder) t.True(state == "ready" || state == "invalidated_by_readback",
                        "Actual device measurement or explicit external readback invalidation for " + phase);
                }
            }
            var effects = rows.Where(e => (string)e["phase"] == "fx.render").ToArray();
            foreach (var child in rows.Where(e => (string)e["gpuStatus"] == "ready" && (long)e["parentId"] != 0))
            {
                var parent = rows.FirstOrDefault(e => (long)e["id"] == (long)child["parentId"]);
                if (parent != null && (string)parent["gpuStatus"] == "ready")
                    t.True((double)child["gpuMs"] <= (double)parent["gpuMs"] + .001, "Nested GPU interval fits its parent within timestamp resolution");
            }
            t.True(effects.Length >= 4 * batch, "Individual FX recorded in child, group and input dependency");
            t.True(effects.All(e => (string)e["fxName"] != "Disabled"), "Inactive FX never measured");
            t.True(effects.Any(e => (string)e["fxName"] == "Second HLSL FX"), "Each HLSL FX measured independently");
            t.True(effects.Any(e => (string)e["fxName"] == "Input tint" && (string)e["layerName"] == "FX input"), "Dependency FX has its own name/layer");
            foreach (var effect in rows.Where(e => (string)e["phase"] == "fx.render" || (string)e["phase"] == "fx.block"))
            {
                var children = rows.Where(e => (long)e["parentId"] == (long)effect["id"]).ToArray();
                bool block = (string)effect["phase"] == "fx.block";
                double prep = block ? children.Where(e => (string)e["phase"] == "fx.render").Sum(e => (double)e["cpuPrepareMs"]) :
                    children.Where(e => (string)e["phase"] == "fx.prepare").Sum(e => (double)e["durationMs"]);
                t.Near(prep, (double)effect["cpuPrepareMs"], .000001, "CPU preparation counted exactly once");
                t.True((double)effect["cpuPrepareMs"] + (double)effect["cpuSubmitMs"] <= (double)effect["durationMs"] + .000001, "CPU preparation/submission fit in inclusive wall interval");
                string state = (string)effect["gpuStatus"];
                t.True(state != "pending", "GPU sample reaches a terminal state");
                t.Equal(state == "ready", effect["gpuMs"].Type != "Null", "Unavailable GPU is null, not zero or CPU elapsed");
                if (state == "ready") t.True((double)effect["gpuMs"] > 0, "Actual nonzero GPU timing");
                if (SystemInfo.supportsGpuRecorder) t.True(state == "ready" || state == "invalidated_by_readback",
                    "Supported Editor returns timing or honest external readback invalidation: " + effect["gpuDetail"]);
            }
            var summary = report["summary"].Where(e => (string)e["phase"] == "fx.render").ToArray();
            t.True(summary.Length >= 4, "Summary does not merge different effect slots/layers");
            foreach (var row in summary)
            {
                var matching = effects.Where(e => (string)e["layerId"] == (string)row["layerId"] && (int)e["fxIndex"] == (int)row["fxIndex"]).ToArray();
                t.Equal(matching.Length, (int)row["count"], "Per-effect call count");
                t.Near(matching.Sum(e => (double)e["cpuPrepareMs"]), (double)row["cpuPrepareMs"], .000001, "Per-effect CPU aggregate");
                if ((int)row["gpuCount"] > 0) t.Near(matching.Where(e => (string)e["gpuStatus"] == "ready").Sum(e => (double)e["gpuMs"]), (double)row["gpuTotalMs"], .000001, "Per-effect GPU aggregate");
            }
        }
        void CheckUI()
        {
            Activate(() =>
            {
                var type = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow");
                var window = Own((EditorWindow)ScriptableObject.CreateInstance(type));
                type.GetMethod("CreateGUI").Invoke(window, null);
                var eventList = (ListView)Field(window, "eventList");
                t.True(window.rootVisualElement.Q<Button>("summary-sort-CpuPrepare") != null && window.rootVisualElement.Q<Button>("summary-sort-Gpu") != null,
                    "CPU/GPU summary columns can be sorted");
                for (int i = 0; i < eventList.itemsSource.Count; i++)
                {
                    var data = Json.Parse(eventList.itemsSource[i].ToString());
                    if ((string)data["phase"] != "fx.render") continue;
                    var row = eventList.makeItem(); eventList.bindItem(row, i);
                    t.True(row.Q<Label>("phase-name").text.Contains((string)data["fxName"]), "Event label includes effect name");
                    t.True(row.Q<Label>("prepare").text != "—", "CPU preparation displayed");
                    t.Equal((string)data["gpuStatus"] == "ready", row.Q<Label>("gpu").text != "—", "GPU availability displayed honestly");
                }
                CheckAutoRefresh(window, type);
            });
        }
        void CheckAutoRefresh(EditorWindow window, Type type)
        {
            void Invoke(string name, params object[] args) => type.GetMethod(name, Instance).Invoke(window, args);
            void Auto(bool allowRead = true)
            {
                Set(window, "nextAutomaticRead", allowRead ? 0d : double.MaxValue);
                Invoke("AutoRefresh");
            }
            var scheduled = (IVisualElementScheduledItem)Field(window, "automaticRefresh");
            t.True(scheduled != null && scheduled.isActive, "Automatic refresh is scheduled by CreateGUI");
            Set(window, "phaseFilter", "fx");
            Invoke("ReadPage", 0);
            var list = (ListView)Field(window, "eventList");
            int index = Enumerable.Range(0, list.itemsSource.Count).First(i =>
                (string)Json.Parse(list.itemsSource[i].ToString())["phase"] == "fx.render");
            int eventId = (int)Json.Parse(list.itemsSource[index].ToString())["id"];
            Invoke("SetBranchCollapsed", eventId, true);
            object sort = Enum.Parse(type.GetNestedType("SummaryColumn", BindingFlags.NonPublic), "Gpu");
            Invoke("SetSummarySort", sort);
            var samples = (Array)Field(capture, "events");
            object saved = samples.GetValue(eventId - 1);
            object revision = Field(capture, "gpuRevision"), recording = Field(capture, "recording");
            var availability = Profiler.GetField("gpuAvailability", Hidden);
            object oldAvailability = availability.GetValue(null);
            try
            {
                object pending = samples.GetValue(eventId - 1);
                Set(pending, "gpuStatus", "pending"); Set(pending, "gpuNanoseconds", 0L);
                samples.SetValue(pending, eventId - 1);
                Invoke("ReadPage", 0);
                index = Enumerable.Range(0, list.itemsSource.Count).First(i =>
                    (int)Json.Parse(list.itemsSource[i].ToString())["id"] == eventId);
                list.SetSelection(index);
                Vector2 scroll = list.Q<ScrollView>().scrollOffset;
                var before = Field(window, "selectedEvent");
                object completed = samples.GetValue(eventId - 1);
                Set(completed, "gpuNanoseconds", 1234567L); samples.SetValue(completed, eventId - 1);
                var slot = Activator.CreateInstance(Profiler.GetNestedType("GpuSlot", BindingFlags.NonPublic), true);
                Set(slot, "owner", capture); Set(slot, "sample", eventId - 1);
                Profiler.GetMethod("CompleteGpu", Hidden).Invoke(null, new[] { slot, "ready", (object)false });
                t.Equal((int)revision + 1, (int)Field(capture, "gpuRevision"), "GPU completion invalidates the displayed snapshot");
                Auto(false);
                t.True(ReferenceEquals(before, Field(window, "selectedEvent")), "Automatic table refresh is throttled");
                Auto();
                var selected = Json.Parse(Field(window, "selectedEvent").ToString());
                t.Equal(eventId, (int)selected["id"], "Automatic refresh preserves selected event by ID");
                t.Equal("ready", (string)selected["gpuStatus"], "Delayed GPU completion reaches UI without Refresh");
                t.Near(1.234567, (double)selected["gpuMs"], .000001, "Selected event gets actual updated GPU value");
                t.True(((Label)Field(window, "detailInfo")).text.Contains("1.235"), "Details update with the selected GPU result");
                t.True(((HashSet<int>)Field(window, "collapsedEvents")).Contains(eventId), "Automatic refresh preserves folded branches");
                t.Equal("fx", (string)Field(window, "appliedPhase"), "Automatic refresh preserves filters");
                t.Equal(sort, Field(window, "summarySort"), "Automatic refresh preserves GPU sorting");
                t.Equal(scroll, list.Q<ScrollView>().scrollOffset, "Automatic refresh preserves scroll position");
                var row = list.makeItem(); list.bindItem(row, list.selectedIndex);
                t.Equal("1.235", row.Q<Label>("gpu").text, "GPU cell no longer shows pending");
                object idleRow = list.itemsSource[0]; Auto();
                t.True(ReferenceEquals(idleRow, list.itemsSource[0]), "Unchanged stopped capture does not rebuild tables");
                Set(capture, "recording", true); availability.SetValue(null, "warming_up"); Auto(false);
                t.True(((Label)Field(window, "status")).text.Contains("GPU warming_up"), "Automatic status displays GPU warmup");
                availability.SetValue(null, "ready"); Auto(false);
                t.True(((Label)Field(window, "status")).text.Contains("GPU ready"), "GPU ready updates independently of table throttle");
            }
            finally
            {
                samples.SetValue(saved, eventId - 1); Set(capture, "gpuRevision", revision);
                Set(capture, "recording", recording); availability.SetValue(null, oldAvailability);
            }
            type.GetMethod("CreateGUI").Invoke(window, null);
            t.True(!scheduled.isActive, "Rebuilding the window pauses the old scheduled callback");
            scheduled = (IVisualElementScheduledItem)Field(window, "automaticRefresh");
            Invoke("OnDisable");
            t.True(!scheduled.isActive, "Disabling the window stops its automatic refresh");
        }
        void CheckPersistence(Json before)
        {
            using var stream = new MemoryStream();
            Profiler.GetMethod("WriteSnapshot", Hidden).Invoke(null, new[] { (object)stream, capture });
            stream.Position = 0;
            object retained = capture;
            capture = Profiler.GetMethod("ReadSnapshot", Hidden).Invoke(null, new object[] { stream });
            try { t.Equal(before["events"].ToString(), Read()["events"].ToString(), "Completed CPU/GPU fields survive reload snapshot"); }
            finally { capture = retained; }
        }
        void Fail(Exception e) => result = TestContext.Result("failed", t.Checks, "FX profiler checks", e.ToString()).ToJson();
        public string Poll() => result ?? TestContext.Result("running", t.Checks, "Waiting for GPU results, batch " + batch).ToJson();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; EditorApplication.update -= Tick;
            if (capture != null) Set(capture, "recording", false);
            // Cancel only this fixture's queries. Do not touch any native/user capture scopes.
            var slots = (System.Collections.IEnumerable)Profiler.GetField("gpuSlots", Hidden).GetValue(null);
            foreach (object slot in slots)
                if (ReferenceEquals(Field(slot, "owner"), capture))
                    Profiler.GetMethod("CompleteGpu", Hidden).Invoke(null, new[] { slot, "interrupted", (object)true });
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (previousFocus != null) previousFocus.Focus();
        }
    }
}
