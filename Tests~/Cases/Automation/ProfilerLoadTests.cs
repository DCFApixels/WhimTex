using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

public static class ProfilerLoadTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type P = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly Type W = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true);
    static readonly string Root = Path.GetFullPath("Temp/WhimTex/profiler-load");
    static object Call(string method, params object[] args) => P.GetMethod(method, F).Invoke(null, args);
    static object Get(object target, string name) => target.GetType().GetField(name, F).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, F).SetValue(target, value);
    static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, F).Invoke(target, args);
    static object Current => P.GetProperty("Current", F).GetValue(null);
    static IDisposable Span(string phase) => (IDisposable)Call("Measure", phase, null, null, -1, null);
    static void Idle()
    {
        var c = Current;
        if (c != null && ((bool)Get(c, "recording") || (int)Get(c, "open") != 0))
            throw new InvalidOperationException("An existing capture is active.");
    }
    static void Clear()
    {
        Call("Stop", "owned_test"); Call("PollAsync"); Call("Clear");
    }
    static double Median(double[] a) { Array.Sort(a); return a[a.Length / 2]; }
    static object Seed(int count)
    {
        var c = Call("StartConfigured", count, 600d, null, false, false);
        var a = (Array)Get(c, "events"); var type = a.GetType().GetElementType();
        long start = (long)Get(c, "start");
        var ids = type.GetField("id", F); var parent = type.GetField("parentId", F);
        var op = type.GetField("operationId", F); var depth = type.GetField("depth", F);
        for (int k = 0; k < 8; k++)
        {
            var s = Activator.CreateInstance(type);
            Set(s, "kind", "span"); Set(s, "phase", k % 4 == 1 ? "fx.render" : "layer.render." + k);
            Set(s, "documentId", "owned"); Set(s, "layerId", "layer-" + k);
            Set(s, "layerName", "Fixture " + k); Set(s, "fxIndex", k % 4 == 1 ? k : -1);
            Set(s, "fxName", k % 4 == 1 ? "Fixture FX" : null);
            Set(s, "start", start); Set(s, "end", start + 10000L);
            Set(s, "childTicks", k % 4 == 0 ? 3000L : 0L); Set(s, "completed", true);
            Set(s, "fxTiming", k % 4 == 1); Set(s, "cpuPrepareTicks", 100L); Set(s, "cpuSubmitTicks", 200L);
            Set(s, "allocationCountMeasured", k % 3 == 0); Set(s, "gcAllocationCount", 9L); Set(s, "gcChildAllocationCount", 3L);
            Set(s, "gpuStatus", k % 3 == 0 ? "ready" : k % 3 == 1 ? "pending" : "unavailable");
            Set(s, "gpuNanoseconds", 3000L);
            for (int i = k; i < count; i += 8)
            {
                ids.SetValue(s, i + 1L); parent.SetValue(s, i % 4 == 0 ? 0L : i / 4 * 4 + 1L);
                op.SetValue(s, i / 4 * 4 + 1L); depth.SetValue(s, i % 4 == 0 ? 0 : 1);
                a.SetValue(s, i);
            }
        }
        Set(c, "count", count); Call("Stop", "owned_fixture");
        return c;
    }
    static readonly string[] Queries = {
        "{\"apiVersion\":1,\"action\":\"read\",\"tree\":true,\"limit\":200}",
        "{\"apiVersion\":1,\"action\":\"read\",\"tree\":true,\"limit\":20,\"phase\":\"fx\",\"groups\":[\"rendering\"],\"collapsedIds\":[1,9]}",
        "{\"apiVersion\":1,\"action\":\"read\",\"tree\":true,\"limit\":200,\"offset\":1000,\"layer\":\"Fixture 1\"}",
        "{\"apiVersion\":1,\"action\":\"read\",\"operationId\":9,\"limit\":200}"
    };
    public static string Bench(string label) => TestContext.Run("Profiler retained capture read/UI cost", t =>
    {
        Idle(); var previous = Current; EditorWindow window = null;
        Directory.CreateDirectory(Root); var lines = new List<string>();
        try
        {
            Clear(); window = (EditorWindow)ScriptableObject.CreateInstance(W);
            Invoke(window, "CreateGUI"); ((IVisualElementScheduledItem)Get(window, "automaticRefresh")).Pause();
            foreach (int count in new[] { 32768, 262144 })
            {
                var capture = Seed(count);
                for (int q = 0; q < Queries.Length; q++)
                {
                    string actual = WhimTexApi.ProfileJson(Queries[q]);
                    File.WriteAllText(Path.Combine(Root, count + "-" + label + "-" + q + ".json"), actual);
                    if (label == "after")
                    {
                        var jsonType = Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true);
                        var tokenType = Type.GetType("Newtonsoft.Json.Linq.JToken, Newtonsoft.Json", true);
                        object a = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] {
                            File.ReadAllText(Path.Combine(Root, count + "-before-" + q + ".json")) });
                        object b = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { actual });
                        var index = jsonType.GetProperty("Item", new[] { typeof(string) });
                        foreach (string key in new[] { "events", "summary", "groups", "totalMatched", "totalVisible", "offset", "nextOffset", "rootWallMs" })
                            t.True((bool)tokenType.GetMethod("DeepEquals", new[] { tokenType, tokenType }).Invoke(null,
                                new[] { index.GetValue(a, new object[] { key }), index.GetValue(b, new object[] { key }) }),
                                "Pre-change parity " + count + "/" + q + "/" + key);
                    }
                }
                Invoke(window, "RefreshCapture");
                foreach (string mode in new[] { "read", "ui" })
                {
                    double[] times = new double[3]; int gc = GC.CollectionCount(0);
                    for (int i = 0; i < times.Length; i++)
                    {
                        var watch = Stopwatch.StartNew();
                        if (mode == "read") WhimTexApi.ProfileJson(Queries[0]);
                        else Invoke(window, "ReadPageCore", 0, true);
                        times[i] = watch.Elapsed.TotalMilliseconds;
                    }
                    lines.Add(count + " " + mode + " medianMs=" + Median(times).ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                        " gen0=" + (GC.CollectionCount(0) - gc));
                }
                t.Equal(200, ((IList)Get(window, "events")).Count, "Bounded UI page");
                t.Equal(count, (int)Get(capture, "count"), "Reading does not alter capture");
                if (label == "after")
                {
                    double maximum = 0; int slices = 0;
                    var timer = Stopwatch.StartNew(); Invoke(window, "BeginAutomaticRead", 0);
                    maximum = timer.Elapsed.TotalMilliseconds;
                    while (Get(window, "pendingRead") != null)
                    {
                        timer.Restart(); Invoke(window, "AdvancePendingRead");
                        maximum = Math.Max(maximum, timer.Elapsed.TotalMilliseconds); slices++;
                    }
                    lines.Add(count + " automaticSlices=" + slices + " maxSliceMs=" + maximum);
                    t.True(slices > 1, "Large automatic reads are spread across updates");
                    t.True(maximum < 100, "Automatic read avoids the previous multi-hundred-ms pause");
                }
                Clear();
            }
        }
        finally
        {
            if (window != null) Object.DestroyImmediate(window);
            Clear(); P.GetField("capture", F).SetValue(null, previous);
            File.WriteAllLines(Path.Combine(Root, label + ".txt"), lines);
        }
    });

    public static string Start(string id) => AsyncD.Start(id, LongRun);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    public static string Lifecycle() => TestContext.Run("Profiler deferred window lifecycle", t =>
    {
        Idle(); var previous = Current; EditorWindow window = null;
        try
        {
            Clear(); var capture = Seed(32768);
            window = (EditorWindow)ScriptableObject.CreateInstance(W);
            Invoke(window, "CreateGUI");
            ((IVisualElementScheduledItem)Get(window, "automaticRefresh")).Pause();
            Invoke(window, "BeginAutomaticRead", 0);
            t.True(Get(window, "pendingRead") != null, "Read continues across updates");
            object response = Get(window, "pendingResponse");
            while (Get(window, "pendingRead") != null) Invoke(window, "AdvancePendingRead");
            var jsonType = Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true);
            var tokenType = Type.GetType("Newtonsoft.Json.Linq.JToken, Newtonsoft.Json", true);
            // Match the public wire representation (for example float versus
            // double JValues); this also exercises the same serialization.
            response = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null,
                new object[] { response.ToString() });
            object synchronous = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null,
                new object[] { WhimTexApi.ProfileJson(Invoke(window, "PageRequest", 0).ToString()) });
            var index = jsonType.GetProperty("Item", new[] { typeof(string) });
            foreach (string key in new[] { "events", "summary", "groups", "totalMatched", "totalVisible", "offset", "nextOffset", "rootWallMs" })
                t.True((bool)tokenType.GetMethod("DeepEquals", new[] { tokenType, tokenType }).Invoke(null,
                    new[] { index.GetValue(response, new object[] { key }), index.GetValue(synchronous, new object[] { key }) }),
                    "Deferred/synchronous parity " + key);

            Invoke(window, "BeginAutomaticRead", 0);
            Set(window, "phaseFilter", "fx");
            Invoke(window, "ReadPage", 0);
            t.True(Get(window, "pendingRead") == null, "Filter cancels pending read");
            t.True(((Label)Get(window, "eventCount")).text.StartsWith("8192 matches"),
                "New filter wins over stale results");

            Invoke(window, "BeginAutomaticRead", 0);
            Invoke(window, "CaptureAction", "clear");
            t.True(Get(window, "pendingRead") == null && ((IList)Get(window, "events")).Count == 0,
                "Clear releases pending read and rows immediately");

            capture = Seed(32768); Invoke(window, "RefreshCapture");
            Invoke(window, "BeginAutomaticRead", 0);
            int oldRevision = (int)Get(capture, "gpuRevision");
            Set(capture, "gpuRevision", oldRevision + 1);
            while (Get(window, "pendingRead") != null) Invoke(window, "AdvancePendingRead");
            t.Equal(oldRevision, (int)Get(window, "loadedGpuRevision"), "Late GPU results remain detectable");
            Set(window, "nextAutomaticStatus", 0d); Set(window, "nextAutomaticRead", 0d);
            Invoke(window, "AutoRefresh");
            while (Get(window, "pendingRead") != null) Invoke(window, "AdvancePendingRead");
            t.Equal(oldRevision + 1, (int)Get(window, "loadedGpuRevision"), "Next read picks up late GPU results");

            Invoke(window, "BeginAutomaticRead", 0);
            Clear(); var replacement = Seed(512);
            Set(window, "nextAutomaticStatus", 0d); Set(window, "nextAutomaticRead", 0d);
            Invoke(window, "AutoRefresh");
            while (Get(window, "pendingRead") != null) Invoke(window, "AdvancePendingRead");
            t.Equal((string)Get(replacement, "id"), (string)Get(window, "captureId"),
                "Replacement capture cannot receive stale results");
            t.Equal(512, (int)Get(window, "loadedCount"), "Replacement count retained");

            Clear(); Seed(32768); Invoke(window, "RefreshCapture"); Invoke(window, "BeginAutomaticRead", 0);
            Invoke(window, "OnDisable");
            t.True(Get(window, "pendingRead") == null && Get(window, "pendingResponse") == null,
                "Closing window releases pending work");
        }
        finally
        {
            if (window != null) Object.DestroyImmediate(window);
            Clear(); P.GetField("capture", F).SetValue(null, previous);
        }
    });

    static async Task LongRun(TestContext t, CancellationToken token)
    {
        Idle(); var previous = Current; EditorWindow window = null;
        Directory.CreateDirectory(Root);
        var lines = new List<string>();
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 512;
        var drawing = new DrawingLayerBehaviour();
        var pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        pixels.SetPixels32(new Color32[512 * 512]); pixels.Apply();
        Invoke(drawing, "AdoptStoredTexture", pixels); doc.layers.Add(drawing); Invoke(doc, "NormalizeModel");
        var settings = Activator.CreateInstance(P.Assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true), true);
        Set(settings, "brushSize", 90f); Set(settings, "brushColor", new Color(.3f, .8f, .5f, 1));
        var parameters = Invoke(settings, "GetStrokeParameters", false, (Color?)null);
        var render = doc.GetType().GetMethod("RenderCanvasAtSize", F);
        IVisualElementScheduledItem refresh = null;
        var workers = new List<Task>();
        try
        {
            Clear();
            foreach (string mode in new[] { "off", "record", "ui" })
            {
                object capture = mode == "off" ? null : Call("StartConfigured", 262144, 600d, null, true, true);
                var updates = new List<double>(); var batches = new List<double>(); var uiReads = new List<double>();
                var watch = Stopwatch.StartNew(); double lastUpdate = watch.Elapsed.TotalMilliseconds;
                void Heartbeat()
                {
                    double now = watch.Elapsed.TotalMilliseconds; updates.Add(now - lastUpdate); lastUpdate = now;
                }
                EditorApplication.update += Heartbeat;
                if (mode == "ui")
                {
                    window = (EditorWindow)ScriptableObject.CreateInstance(W);
                    window.titleContent = new GUIContent("WhimTex Profiler · owned load test");
                    window.ShowUtility();
                    await Task.Delay(150, token);
                    ((IVisualElementScheduledItem)Get(window, "automaticRefresh")).Pause();
                    refresh = window.rootVisualElement.schedule.Execute(() =>
                    {
                        var clock = Stopwatch.StartNew(); Invoke(window, "AutoRefresh");
                        uiReads.Add(clock.Elapsed.TotalMilliseconds);
                    }).Every(16);
                }
                int n = 0, lastMemorySample = -1;
                try
                {
                    while (watch.Elapsed.TotalSeconds < 60)
                    {
                        token.ThrowIfCancellationRequested();
                        var clock = Stopwatch.StartNew();
                        using (Span("test.paint_frame"))
                        {
                            Invoke(drawing, "PrepareBufferedStroke", 512, 512, "Owned profiler load");
                            var point = new Vector2(.5f + .35f * Mathf.Sin(n * .13f), .5f + .35f * Mathf.Cos(n * .21f));
                            Invoke(drawing, "BeginStroke", point);
                            Invoke(drawing, "PaintPoint", point, 512, 512, parameters);
                            var image = (RenderTexture)render.Invoke(doc, new object[] { 512, 512 });
                            t.True(image != null && image.IsCreated(), "Owned brush frame renders");
                            Invoke(drawing, "CancelStroke");
                            // Dense ordinary spans stress retained history independently
                            // of GPU workload; identical calls also run with capture off.
                            for (int i = 0; i < 256; i++) using (Span("test.frame_detail")) { }
                        }
                        batches.Add(clock.Elapsed.TotalMilliseconds); n++;
                        int seconds = (int)watch.Elapsed.TotalSeconds / 10;
                        if (seconds != lastMemorySample)
                        {
                            lines.Add(mode + " second=" + seconds * 10 + " events=" + (capture == null ? 0 : Get(capture, "count")) +
                                " managedBytes=" + GC.GetTotalMemory(false));
                            File.WriteAllLines(Path.Combine(Root, "long-progress.txt"), lines);
                            lastMemorySample = seconds;
                        }
                        await Task.Delay(50, token);
                    }
                }
                finally { EditorApplication.update -= Heartbeat; refresh?.Pause(); refresh = null; }
                Call("Stop", "owned_long_run");
                for (int wait = 0; wait < 150 && (int)Call("PendingGpu", Current) != 0; wait++) await Task.Delay(100, token);
                if (capture != null)
                {
                    t.Equal(0, (int)Get(Current, "open"), mode + " closes all scopes");
                    t.True((int)Get(capture, "count") <= 262144, "Capture is bounded");
                }
                if (window != null)
                {
                    Invoke(window, "RefreshCapture");
                    t.True(((IList)Get(window, "events")).Count <= 200, "UI page stays bounded during long run");
                    t.True(window.rootVisualElement.Q(className: "whimtex-profiler-data-row") != null, "Actual UI rows realized");
                    window.Close(); window = null;
                }
                double[] batchTimes = batches.ToArray(), updateTimes = updates.ToArray(), readTimes = uiReads.ToArray();
                Array.Sort(batchTimes); Array.Sort(updateTimes); Array.Sort(readTimes);
                lines.Add(mode + " batches=" + n + " medianBatchMs=" + Median(batchTimes) +
                    " p95BatchMs=" + batchTimes[(int)(batchTimes.Length * .95)] +
                    " p95EditorUpdateGapMs=" + updateTimes[(int)(updateTimes.Length * .95)] +
                    " uiMedianMs=" + (readTimes.Length == 0 ? 0 : Median(readTimes)) +
                    " uiMaxMs=" + (readTimes.Length == 0 ? 0 : readTimes[readTimes.Length - 1]));
                Clear();
            }
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var owner = Call("StartConfigured", 256, 600d, null, false, false);
                var context = Activator.CreateInstance(P.GetNestedType("WorkContext", BindingFlags.NonPublic));
                var ticket = Call("QueueWorker", "storage.owned_late", context);
                for (int i = 0; i < 270; i++) using (Span("test.limit")) { }
                t.Equal("event_limit", (string)Get(owner, "stopReason"), "Stops at event limit");
                t.Equal(256, (int)Get(owner, "count"), "No growing retained event buffer");
                string id = (string)Get(owner, "id");
                t.True(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"clear\",\"captureId\":\"" + id + "\"}")
                    .Contains("capture_active"), "Cannot clear while worker is pending");
                var worker = Task.Run(() => { using var scope = (IDisposable)Invoke(ticket, "Start"); Thread.Sleep(30); });
                workers.Add(worker); await worker; Call("PollAsync");
                t.Equal(0, (int)Get(owner, "open"), "Late completion closes stopped capture");
                Clear();
                owner = Call("StartConfigured", 256, 1d, null, false, false);
                await Task.Delay(1100, token);
                t.Equal("time_limit", (string)Get(Current, "stopReason"), "Real deadline stops recording");
                Clear();
                using (Span("test.after_clear")) { }
                t.True(Current == null, "Clear leaves no retained capture");
            }
        }
        finally
        {
            refresh?.Pause();
            await Task.WhenAll(workers);
            AsyncD.CleanupOwned(
                () => { if (window != null) window.Close(); },
                () => {
                    Invoke(drawing, "CancelStroke");
                    var cursor = (Object)drawing.GetType().GetProperty("PixelUndoObject", F).GetValue(drawing);
                    if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
                    Invoke(drawing, "ReleaseTransientResources"); doc.layers.Clear(); Object.DestroyImmediate(doc);
                },
                () => { Clear(); P.GetField("capture", F).SetValue(null, previous); },
                () => File.WriteAllLines(Path.Combine(Root, "long.txt"), lines));
        }
    }
}
