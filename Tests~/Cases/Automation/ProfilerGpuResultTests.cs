using System;
using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using UnityEngine.Rendering;

// Synthetic timestamps exercise package-owned bookkeeping without native GPU resources
// or replacing the retained capture. Live timestamp delivery is tested separately.
public static class ProfilerGpuResultTests
{
    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Profiler = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static object New(string type) => Activator.CreateInstance(Profiler.GetNestedType(type, F), true);
    static object Get(object obj, string field) => obj.GetType().GetField(field, F).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, F).SetValue(obj, value);
    static void Accept(object slot, long value, long count) => Profiler.GetMethod("AcceptGpuResult", F).Invoke(null, new[] { slot, (object)value, count });

    public static string Run() => TestContext.Run("GPU timestamp validation and immutable completion", t =>
    {
        object retained = Profiler.GetField("capture", F).GetValue(null);
        var reason = Profiler.GetMethod("GpuUnsupportedReason", F);
        foreach (RenderingThreadingMode mode in Enum.GetValues(typeof(RenderingThreadingMode)))
        {
            bool jobs = mode == RenderingThreadingMode.LegacyJobified || mode == RenderingThreadingMode.NativeGraphicsJobs ||
                mode == RenderingThreadingMode.NativeGraphicsJobsWithoutRenderThread || mode == RenderingThreadingMode.NativeGraphicsJobsSplitThreading;
            t.Equal(jobs ? "unsupported_graphics_jobs" : null, (string)reason.Invoke(null, new object[] { true, mode }), "Actual rendering threading mode support: " + mode);
            t.Equal((bool?)jobs, (bool?)Profiler.GetMethod("GraphicsJobsActive", F).Invoke(null, new object[] { mode }), "Actual Graphics Jobs classification: " + mode);
            t.Equal("unsupported", (string)reason.Invoke(null, new object[] { false, mode }), "Missing device counters: " + mode);
        }
        t.Equal("unsupported_threading", (string)reason.Invoke(null, new object[] { true, (RenderingThreadingMode)int.MaxValue }), "Unknown future modes fail closed");
        foreach (bool unconfirmed in new[] { false, true })
        foreach (var pair in new[] { (2500L, 1L), (0L, 1L), (-1L, 1L), (2500L, 2L), (2500L, 0L) })
        {
            object capture = New("Capture"), slot = New("GpuSlot");
            var events = Array.CreateInstance(Profiler.GetNestedType("Sample", F), 1);
            Set(capture, "events", events); Set(slot, "owner", capture);
            Set(slot, "sample", 0); Set(slot, "busy", true);
            Set(slot, "unconfirmed", unconfirmed);
            bool valid = pair.Item1 > 0 && pair.Item2 == 1;
            Accept(slot, pair.Item1, pair.Item2);
            var row = events.GetValue(0);
            t.Equal(valid ? unconfirmed ? "unconfirmed" : "ready" : "ambiguous", (string)Get(row, "gpuStatus"), "Native timestamp validity and confidence");
            t.Equal(valid, (bool)Profiler.GetMethod("HasGpuDuration", F).Invoke(null, new[] { row }), "Measured interval remains available with either confidence");
            if (valid && unconfirmed) t.True(((string)Get(row, "gpuDetail")).Contains("restart Unity"), "Unconfirmed duration carries the supported-comparison recommendation");
            t.Equal(valid ? pair.Item1 : 0L, (long)Get(row, "gpuNanoseconds"), "Unavailable result has no duration");
            t.Equal(!valid, (bool)Get(slot, "retired"), "Invalid marker identity is not recycled");
            t.True(!(bool)Get(slot, "busy") && Get(slot, "owner") == null, "Completed query releases capture ownership");
            t.Equal(1, (int)Get(capture, "gpuRevision"), "Completion invalidates the GPU view once");
            if (!valid) t.True(!string.IsNullOrEmpty((string)Get(row, "gpuDetail")), "Invalid native result has a diagnostic");
            Accept(slot, 9999, 1);
            Profiler.GetMethod("InvalidateGpuReadback", F).Invoke(null, new[] { slot });
            t.Equal(1, (int)Get(capture, "gpuRevision"), "Repeated completion is harmless");
            t.Equal(valid ? pair.Item1 : 0L, (long)Get(events.GetValue(0), "gpuNanoseconds"), "Completed duration is immutable");
        }
        foreach (bool ended in new[] { false, true })
        {
            object capture = New("Capture"), slot = New("GpuSlot");
            var events = Array.CreateInstance(Profiler.GetNestedType("Sample", F), 1);
            Set(capture, "events", events); Set(slot, "owner", capture); Set(slot, "sample", 0);
            Set(slot, "busy", true); Set(slot, "ended", ended);
            Profiler.GetMethod("InvalidateGpuReadback", F).Invoke(null, new[] { slot });
            t.True((bool)Get(slot, "invalidatedByReadback"), "Readback affects the pending query");
            t.Equal(!ended, (bool)Get(slot, "busy"), "Open scopes retain ownership until EndSample can be submitted");
            Accept(slot, 123456, 1);
            t.Equal("invalidated_by_readback", (string)Get(events.GetValue(0), "gpuStatus"), "A positive late timestamp cannot repair invalidation");
            t.Equal(0L, (long)Get(events.GetValue(0), "gpuNanoseconds"), "No fabricated readback duration");
            t.True((bool)Get(slot, "retired") && Get(slot, "owner") == null, "Invalidated marker cannot be reassigned");
        }
        string source = Path.Combine(Directory.GetCurrentDirectory(), "Packages/com.dcfapixels.whimtex/src");
        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
            foreach (Match call in Regex.Matches(File.ReadAllText(file), @"\b(\w+)\.ReadPixels\s*\("))
            {
                string receiver = call.Groups[1].Value;
                t.True(receiver == "WhimTexProfiler" || receiver == "HdrUtility" ||
                    (Path.GetFileName(file) == "WhimTexProfiler.Gpu.cs" && receiver == "destination"),
                    "ReadPixels routes through invalidation: " + file);
            }
        t.True(ReferenceEquals(retained, Profiler.GetField("capture", F).GetValue(null)), "Retained capture unchanged");
    });
}
