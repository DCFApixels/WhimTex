using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = ProfilerAnalysisTests.Json;

public static class ProfilerAllocationTests
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type P = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly FieldInfo Capture = P.GetField("capture", S);
    static readonly MethodInfo Measure = P.GetMethod("Measure", S);
    static object Call(string name, params object[] args) => P.GetMethod(name, S).Invoke(null, args);
    static void Set(object o, string name, object v) => o.GetType().GetField(name, I).SetValue(o, v);
    static object Get(object o, string name) => o.GetType().GetField(name, I).GetValue(o);
    static Json Api(string action) => Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"" + action + "\"}"));
    static IDisposable Span(string phase) => (IDisposable)Measure.Invoke(null, new object[] { phase, null, null, -1, null });
    static Json Event(string phase) => Api("read")["events"].Last(x => (string)x["phase"] == phase);
    static long Total(object capture) => (long)Get(Get(capture, "memory"), "editorManagedAllocatedBytes");
    static void Allocate(int count, int size) { for (int i = 0; i < count; i++) GC.KeepAlive(new byte[size]); }
    static Task Frames(int count)
    {
        var completion = new TaskCompletionSource<bool>();
        EditorApplication.CallbackFunction tick = null;
        tick = () => { if (--count > 0) return; EditorApplication.update -= tick; completion.SetResult(true); };
        EditorApplication.update += tick;
        return completion.Task;
    }

    public static async Task<string> Run()
    {
        var status = Api("status")["capture"];
        if (status.Type != "Null" && ((bool)status["recording"] || (int)status["openSpans"] != 0 || (int)status["pendingGpuSamples"] != 0))
            return TestContext.Result("skipped", 0, "Needs idle profiler; user capture untouched.").ToJson();
        object saved = Capture.GetValue(null);
        var t = new TestContext();
        string failure = null;
        var signal = new AutoResetEvent(false); var done = new AutoResetEvent(false);
        int work = 0; bool quit = false, workerStarted = false;
        var worker = new Thread(() => { while (true) { signal.WaitOne(); if (quit) return; Allocate(work, 8192); done.Set(); } });
        worker.IsBackground = true;
        try
        {
            object capture = Activator.CreateInstance(P.GetNestedType("Capture", BindingFlags.NonPublic), true);
            Set(capture, "id", Guid.NewGuid().ToString("N")); Set(capture, "start", Stopwatch.GetTimestamp());
            Set(capture, "deadline", long.MaxValue); Set(capture, "recording", true);
            Set(capture, "events", Array.CreateInstance(P.GetNestedType("Sample", BindingFlags.NonPublic), 4096));
            Call("ResetMemory"); Capture.SetValue(null, capture); Call("PrepareAllocations", capture);
            worker.Start(); workerStarted = true;
            work = 2; signal.Set(); t.True(done.WaitOne(5000), "Warm worker finished");
            Allocate(1, 16);
            using (Span("alloc.warm")) { }
            using (Span("alloc.empty")) { }
            using (Span("alloc.one")) Allocate(1, 8192);
            using (Span("alloc.parent"))
            {
                Allocate(1, 32);
                using (Span("alloc.child")) Allocate(3, 64);
            }
            var empty = Event("alloc.empty"); var one = Event("alloc.one");
            t.Near((double)empty["gcAllocationCount"] + 1, (double)one["gcAllocationCount"], 0, "One array adds one allocation, regardless of bytes");
            var parent = Event("alloc.parent"); var child = Event("alloc.child");
            t.True((double)child["gcAllocationCount"] >= 3, "Child counts all three arrays");
            t.Near((double)parent["gcAllocationCount"] - (double)child["gcAllocationCount"], (double)parent["gcSelfAllocationCount"], 0, "Self subtracts nested count once");
            work = 0;
            using (Span("alloc.worker_empty")) { signal.Set(); if (!done.WaitOne(5000)) throw new TimeoutException(); }
            Call("SampleEditorAllocations", capture); long beforeWorker = Total(capture);
            work = 128;
            using (Span("alloc.worker_busy")) { signal.Set(); if (!done.WaitOne(5000)) throw new TimeoutException(); }
            Call("SampleEditorAllocations", capture);
            t.True(Total(capture) - beforeWorker >= 128 * 8192, "Global bytes include worker allocations");
            t.Near((double)Event("alloc.worker_empty")["gcAllocationCount"], (double)Event("alloc.worker_busy")["gcAllocationCount"], 0, "Worker allocations excluded from stage count");

            long previous = Total(capture);
            for (int frame = 0; frame < 4; frame++)
            {
                Allocate(2, 4096);
                await Frames(2);
                Call("SampleEditorAllocations", capture);
                long next = Total(capture);
                t.True(next >= previous + 8192, "Allocation total survives frame reset " + frame);
                previous = next;
            }
            t.True(!(bool)Get(Get(capture, "memory"), "editorManagedAllocationLimited"), "Ordinary frames do not lose byte samples");
            using (Span("alloc.overflow_parent"))
            using (Span("alloc.overflow")) Allocate(17000, 0);
            t.Equal("Null", Event("alloc.overflow")["gcAllocationCount"].Type, "Overflow is unavailable, not a truncated zero/count");
            t.Equal("Null", Event("alloc.overflow_parent")["gcSelfAllocationCount"].Type, "Overflow invalidates parent self count");
            using (Span("alloc.recovered")) Allocate(1, 16);
            t.True((double)Event("alloc.recovered")["gcAllocationCount"] >= 1, "Following scopes recover after drain");
            var memory = Api("status")["capture"]["memory"];
            t.True((bool)memory["allocationCountLimited"], "Capture reports incomplete count coverage");
            t.Equal("all_editor_threads", (string)memory["editorManagedAllocationScope"], "Global byte scope explicit");
            var hotspot = Api("summary")["brief"]["allocationHotspots"].First();
            t.True(hotspot["gcSelfAllocationCount"] != null && hotspot["gcAllocatedBytes"] == null, "Agent allocation hotspots use counts");

            Call("Stop", "allocation_test"); long stopped = Total(capture);
            Allocate(2, 8192); Call("SampleEditorAllocations", capture);
            t.Near(stopped, Total(capture), 0, "Stopped total frozen");
            t.True(P.GetField("allocationOwner", S).GetValue(null) == null, "Stop releases native recorder ownership");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            quit = true; signal.Set();
            if (workerStarted && !worker.Join(5000))
                failure = (failure ?? "") + " Worker failed to finish.";
            signal.Dispose(); done.Dispose();
            Call("Stop", "allocation_test_cleanup"); Call("ResetMemory"); Call("ResetCauses");
            Capture.SetValue(null, saved); P.GetField("current", S).SetValue(null, -1); P.GetField("suppressed", S).SetValue(null, 0);
        }
        return TestContext.Result(failure == null ? "passed" : "failed", t.Checks,
            "Managed counts, worker exclusion, frame byte totals, overflow and lifecycle", failure == null ? Array.Empty<string>() : new[] { failure }).ToJson();
    }
}
