using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class ProfilerFollowupTests
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type P = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static object Call(string name, params object[] args) => P.GetMethod(name, S).Invoke(null, args);
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static object Invoke(object o, string name, params object[] a) => o.GetType().GetMethod(name, F).Invoke(o, a);
    static object Current => P.GetProperty("Current", S).GetValue(null);
    static object Context => Activator.CreateInstance(P.GetNestedType("WorkContext", BindingFlags.NonPublic));
    static IDisposable Span(string name, WhimTexDocument document = null) => (IDisposable)Call("Measure", name, document, null, -1, null);
    [Serializable] public sealed class Row
    {
        public string phase, kind, asyncStatus;
        public long id, originId, inputId, workerAllocatedBytes;
        public int threadId, coalescedInputs;
        public double durationMs, selfMs, queueMs, latestInputDurationMs;
        public bool completed, interrupted, workerAllocationAvailable;
    }
    [Serializable] public sealed class Report { public bool success; public Row[] events; public double rootWallMs; }
    static Report Read()
    {
        string json = WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"read\",\"limit\":2000}");
        foreach (string name in new[] { "workerAllocatedBytes", "queueMs", "durationMs", "selfMs", "latestInputDurationMs" })
            json = json.Replace("\"" + name + "\":null", "\"" + name + "\":0");
        var convert = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var result = (Report)convert.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
            .Invoke(null, new object[] { json, typeof(Report) });
        if (result.events == null) throw new Exception(json);
        return result;
    }
    static Row Find(Report report, string phase) => Array.Find(report.events, x => x.phase == phase);
    static void RequireIdle()
    {
        var current = Current;
        if (current != null && ((bool)Get(current, "recording") || (int)Get(current, "open") != 0))
            throw new Exception("Stop the existing profiler capture before this owned test.");
    }
    static void Restore(object previous)
    {
        Call("Stop", "owned_test"); Call("PollAsync"); Call("Clear");
        P.GetField("capture", S).SetValue(null, previous);
    }
    public static string Run() => TestContext.Run("Worker, latency, memory and bounded profiler capture", t =>
    {
        RequireIdle(); var previous = Current;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        var pool = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.PaintHistoryBuffers", true);
        object[] buffer = { pool.GetMethod("Rent", S).Invoke(null, new object[] { 4096 }) };
        var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32); target.Create();
        var oldTarget = RenderTexture.active;
        try
        {
            var owner = Call("StartConfigured", 1024, 120d, null, false, false);
            t.True((long)Get(Get(owner, "memory"), "paintNativeActiveBytes") >= 4096, "Existing native pool observed at capture start");
            int main = Thread.CurrentThread.ManagedThreadId;
            Call("ActionInput", doc, "owned.input");
            using (Span("test.main_wait", doc))
            {
                var a = Call("QueueWorker", "storage.test.a", Context);
                var b = Call("QueueWorker", "storage.test.b", Context);
                Task Work(object ticket) => Task.Run(() =>
                {
                    using var work = (IDisposable)Invoke(ticket, "Start");
                    var bytes = new byte[8192]; Thread.Sleep(5); GC.KeepAlive(bytes);
                });
                Task.WaitAll(Work(a), Work(b));
            }
            Call("PollAsync");
            Call("ActionInput", doc, "owned.input");
            Call("CanvasRequested", doc, "render.request");
            Call("CanvasReady", doc);
            Call("Stop", "owned_test");
            var r = Read(); t.True(r.success, "Read API succeeds");
            var mainRow = Find(r, "test.main_wait");
            foreach (var phase in new[] { "storage.test.a", "storage.test.b" })
            {
                var row = Find(r, phase);
                t.True(row != null && row.completed && row.kind == "worker", "Worker published: " + phase);
                t.True(row.threadId != main && row.originId == mainRow.id, "Worker thread and originating span");
                t.True(row.inputId == Find(r, "input.canvas_ready").id, "Worker retains input link");
                t.True((!row.workerAllocationAvailable || row.workerAllocatedBytes >= 8192) && row.durationMs >= 4 && row.queueMs >= 0,
                    "Worker duration/allocation traffic: " + row.durationMs + " ms, " + row.workerAllocatedBytes + " bytes, queue " + row.queueMs);
            }
            t.Near(mainRow.durationMs, r.rootWallMs, .001, "Worker and latency time excluded from root wall");
            t.Near(mainRow.durationMs, mainRow.selfMs, .001, "Worker does not subtract from main self time");
            var input = Find(r, "input.canvas_ready");
            t.Equal(2, input.coalescedInputs, "Render request does not count as another input");
            t.True(input.completed && input.durationMs >= input.latestInputDurationMs, "First/latest coalesced input");
            t.True(Find(r, "input.gpu_observed") == null, "CPU-only mode submits no fence");
            var summary = WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"summary\"}");
            t.True(summary.Contains("\"workerHotspots\"") && summary.Contains("\"inputLatencies\""), "Separate agent summaries");
            using (var stream = new MemoryStream())
            {
                Call("WriteSnapshot", stream, owner); stream.Position = 0;
                var restored = Call("ReadSnapshot", stream);
                t.Equal((int)Get(owner, "count"), (int)Get(restored, "count"), "Worker/latency hierarchy survives snapshot validation");
                t.True(!(bool)Get(restored, "collectGpu"), "Capture settings survive snapshot");
            }
            Call("Clear");

            owner = Call("StartConfigured", 1024, 120d, null, true, false);
            Call("ActionInput", doc, "owned.input.gpu");
            RenderTexture.active = target; GL.Clear(false, true, Color.gray);
            Call("CanvasReady", doc);
            // Tiny owned readback completes preceding commands; never reads the user's canvas.
            var readback = AsyncGPUReadback.Request(target); readback.WaitForCompletion(); Call("PollAsync");
            r = Read(); var gpu = Find(r, "input.gpu_observed");
            t.True(gpu != null, "GPU completion or explicit unavailable result");
            t.True(gpu.asyncStatus == "ready_observed" || gpu.asyncStatus == "unavailable", "Public fence result, no made-up timing");
            t.True(gpu.asyncStatus != "unavailable" || !gpu.completed, "Unavailable latency is incomplete");
            Call("Stop", "owned_test"); Call("Clear");

            // Event limit and stop-without-canvas do not leave a permanently open capture.
            owner = Call("StartConfigured", 256, 120d, null, false, false);
            Call("ActionInput", doc, "never.rendered");
            for (int i = 0; i < 300; i++) using (Span("test.bounded")) { }
            t.True(!(bool)Get(owner, "recording"), "Event cap stops capture");
            t.Equal(0, (int)Get(owner, "open"), "Unrendered input closed at cap");
            t.True((int)Get(owner, "count") <= 256, "Capture remains bounded");
            Call("Clear");
            owner = Call("StartConfigured", 1024, 120d, null, false, false);
            var queued = new System.Collections.Generic.List<object>();
            for (int i = 0; i < 300; i++)
            {
                var ticket = Call("QueueWorker", "storage.limit", Context);
                if (ticket != null) queued.Add(ticket);
            }
            t.Equal(256, queued.Count, "Pending worker limit");
            foreach (var ticket in queued) Invoke(ticket, "Cancel");
            Call("PollAsync");
            t.Equal(0, (int)Get(owner, "open"), "Post-stop worker completion/cancellation drains capture");
        }
        finally
        {
            Restore(previous); pool.GetMethod("Return", S).Invoke(null, buffer);
            RenderTexture.active = oldTarget; Object.DestroyImmediate(target); Object.DestroyImmediate(doc);
        }
    });

    public static string Overhead() => TestContext.Run("Profiler short-operation overhead", t =>
    {
        RequireIdle(); var previous = Current;
        const int loops = 3000;
        var method = P.GetMethod("Measure", S);
        object[] args = { "test.short_operation", null, null, -1, null };
        var lines = new System.Collections.Generic.List<string>();
        try
        {
            // Same reflected entry/boxing in every mode; report medians, not a claim
            // about zero-cost production instrumentation. Warm up before each series.
            foreach (string mode in new[] { "off", "cpu", "allocations", "gpu_allocations" })
            {
                double[] samples = new double[5];
                for (int repeat = 0; repeat < 5; repeat++)
                {
                    if (mode != "off") Call("StartConfigured", 16384, 120d, null, mode == "gpu_allocations", mode != "cpu");
                    for (int i = 0; i < 128; i++) ((IDisposable)method.Invoke(null, args)).Dispose();
                    var clock = Stopwatch.StartNew();
                    double sum = 0;
                    for (int i = 0; i < loops; i++)
                    {
                        using var scope = (IDisposable)method.Invoke(null, args);
                        sum += Math.Sqrt(i + 1);
                    }
                    samples[repeat] = clock.Elapsed.TotalMilliseconds * 1000 / loops;
                    t.True(sum > 1000, "Identical workload executed");
                    if (mode != "off") { Call("Stop", "overhead"); Call("Clear"); }
                }
                Array.Sort(samples);
                lines.Add(mode + ": medianUsPerScope=" + samples[2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
            lines.Add("Includes the same reflected entry and boxed scope in all modes. GPU configuration cost only; this synchronous microbenchmark does not wait for GPU marker activation.");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/profiler-followup"));
            Directory.CreateDirectory(path); File.WriteAllLines(Path.Combine(path, "overhead.txt"), lines);
        }
        finally { Restore(previous); }
    });

    public static async Task<string> GpuOverhead()
    {
        RequireIdle(); var previous = Current; var t = new TestContext();
        var texture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf); texture.Create();
        var oldTarget = RenderTexture.active;
        var lines = new System.Collections.Generic.List<string>();
        try
        {
            foreach (string mode in new[] { "off", "cpu", "allocations", "gpu_allocations" })
            {
                object owner = null;
                if (mode != "off") owner = Call("StartConfigured", 8192, 40d, null, mode == "gpu_allocations", mode != "cpu");
                if (mode == "gpu_allocations")
                {
                    var warmup = Stopwatch.StartNew();
                    string availability;
                    do
                    {
                        Graphics.Blit(Texture2D.whiteTexture, texture); GL.Flush();
                        await Task.Delay(30);
                        availability = (string)P.GetProperty("GpuAvailability", S).GetValue(null);
                    } while (availability == "warming_up" && warmup.Elapsed.TotalSeconds < 11);
                    lines.Add("gpuAvailability=" + availability);
                    if (availability != "ready" && availability != "unconfirmed")
                        return TestContext.Result("skipped", t.Checks, "Live GPU markers unavailable: " + availability).ToJson();
                }
                double[] times = new double[30];
                for (int i = 0; i < times.Length; i++)
                {
                    var clock = Stopwatch.StartNew();
                    object scope = Call("Measure", "test.gpu_batch", null, null, -1, null);
                    using ((IDisposable)scope)
                    using ((IDisposable)Invoke(scope, "Gpu"))
                        for (int draw = 0; draw < 8; draw++) Graphics.Blit(Texture2D.whiteTexture, texture);
                    times[i] = clock.Elapsed.TotalMilliseconds;
                    GL.Flush(); await Task.Delay(30);
                }
                Array.Sort(times);
                lines.Add(mode + ": medianCpuSubmitMs=" + times[15].ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                t.True(times[15] > 0, "Identical live GPU submission workload");
                if (owner != null)
                {
                    Call("Stop", "gpu_overhead");
                    var drain = Stopwatch.StartNew();
                    while ((int)Call("PendingGpu", owner) > 0 && drain.Elapsed.TotalSeconds < 11) await Task.Delay(30);
                    string capture = WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"read\",\"phase\":\"test.gpu_batch\",\"limit\":2000}");
                    if (mode == "gpu_allocations")
                    {
                        t.True(capture.Contains("\"gpuStatus\":\"ready\"") || capture.Contains("\"gpuStatus\":\"unconfirmed\""),
                            "Real GPU marker results delivered");
                        lines.Add("GPU markers actually activated; native status/confidence retained in gpu-capture.json.");
                        Write("gpu-capture.json", capture);
                    }
                    Call("Clear");
                }
            }
            return TestContext.Result("passed", t.Checks, "Live GPU collection overhead compared").ToJson();
        }
        catch (Exception error) { return TestContext.Result("failed", t.Checks, "GPU overhead", error.ToString()).ToJson(); }
        finally
        {
            Write("gpu-overhead.txt", string.Join("\n", lines));
            Restore(previous); RenderTexture.active = oldTarget; Object.DestroyImmediate(texture);
        }
        static void Write(string name, string text)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/profiler-followup"));
            Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), text);
        }
    }
}
