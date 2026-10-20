using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class ProfilerAnalysisTests
{
    // Avoid ambiguous Newtonsoft references in the ephemeral Pipeline assembly.
    public sealed class Json : IEnumerable<Json>
    {
        static readonly Type ObjectType = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.Linq.JObject", true);
        readonly object token;
        Json(object value) { token = value; }
        public static Json Parse(string text) => new Json(ObjectType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text }));
        public Json this[string name] { get { var v = token.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(token, new object[] { name }); return v == null ? null : new Json(v); } }
        public string Type => token.GetType().GetProperty("Type").GetValue(token).ToString();
        public override string ToString() => token.ToString();
        public static explicit operator string(Json v) => v == null || v.Type == "Null" ? null : v.ToString();
        public static explicit operator double(Json v) => double.Parse(v.ToString(), CultureInfo.InvariantCulture);
        public static explicit operator int(Json v) => int.Parse(v.ToString(), CultureInfo.InvariantCulture);
        public static explicit operator bool(Json v) => bool.Parse(v.ToString());
        public IEnumerator<Json> GetEnumerator() { foreach (object v in (IEnumerable)token) yield return new Json(v); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    const string BaselineKey = "WhimTex.Profiler.BaselineSnapshot";
    static readonly Type Profiler = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly Type CaptureType = Profiler.GetNestedType("Capture", BindingFlags.NonPublic);
    static readonly Type SampleType = Profiler.GetNestedType("Sample", BindingFlags.NonPublic);
    static readonly FieldInfo Current = Profiler.GetField("capture", Static);
    static readonly FieldInfo Baseline = Profiler.GetField("<Baseline>k__BackingField", Static);
    static object Get(object o, string f) => o.GetType().GetField(f, Instance).GetValue(o);
    static void Set(object o, string f, object v) => o.GetType().GetField(f, Instance).SetValue(o, v);
    static object Invoke(string name, params object[] args) => Profiler.GetMethod(name, Static).Invoke(null, args);
    static Json Call(string action, string fields = "") => Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"" + action + "\"" + fields + "}"));
    static long Ticks(double ms) => (long)Math.Round(ms * Stopwatch.Frequency / 1000d);
    static object Sample(int id, string phase, double duration, string layer = "a", int parent = 0, string name = "Layer", string kind = "span", string detail = null, bool complete = true, string gpu = null)
    {
        object s = Activator.CreateInstance(SampleType);
        Set(s, "id", (long)id); Set(s, "parentId", (long)parent); Set(s, "operationId", (long)(parent == 0 ? id : parent));
        Set(s, "depth", parent == 0 ? 0 : 1); Set(s, "phase", phase); Set(s, "kind", kind); Set(s, "detail", detail);
        Set(s, "documentId", "doc-a"); Set(s, "layerId", layer); Set(s, "layerName", name); Set(s, "fxIndex", gpu == null ? -1 : 0);
        Set(s, "start", Ticks(1)); Set(s, "end", Ticks(1 + duration)); Set(s, "completed", complete); Set(s, "interrupted", !complete);
        Set(s, "fxTiming", gpu != null); Set(s, "fxName", gpu == null ? null : "FX"); Set(s, "gpuStatus", gpu);
        if (gpu == "ready" || gpu == "unconfirmed") Set(s, "gpuNanoseconds", 2000000L);
        return s;
    }
    static object Capture(params object[] samples)
    {
        object c = Activator.CreateInstance(CaptureType, true); var a = Array.CreateInstance(SampleType, 256);
        for (int i = 0; i < samples.Length; i++) a.SetValue(samples[i], i);
        Set(c, "events", a); Set(c, "count", samples.Length); Set(c, "id", Guid.NewGuid().ToString("N"));
        Set(c, "start", 0L); Set(c, "end", Ticks(100)); Set(c, "stopReason", "manual"); Set(c, "unityVersion", UnityEngine.Application.unityVersion);
        return c;
    }
    static Json Row(Json comparison, string phase, string layer = null) => comparison["rows"].Single(x => (string)x["phase"] == phase && (layer == null || (string)x["layerId"] == layer));

    static object AllocationSample(int id, string phase, long count, long children = 0, bool measured = true)
    {
        var sample = Sample(id, phase, 1);
        Set(sample, "allocationCountMeasured", measured); Set(sample, "gcAllocationCount", count); Set(sample, "gcChildAllocationCount", children);
        return sample;
    }

    static void CheckAllocationComparison(TestContext t)
    {
        object savedCurrent = Current.GetValue(null), savedBaseline = Baseline.GetValue(null);
        try
        {
            const long large = 9007199254740993L;
            Baseline.SetValue(null, Capture(
                AllocationSample(1, "matched", 40, 10), AllocationSample(2, "decrease", 50, 20),
                AllocationSample(3, "zero", 0), AllocationSample(4, "missing", 20, measured: false),
                AllocationSample(5, "partial", 10), AllocationSample(6, "partial", 10, measured: false),
                AllocationSample(7, "removed", 20), AllocationSample(8, "large", large),
                AllocationSample(9, "unchanged", 0), AllocationSample(10, "tie-b", 5), AllocationSample(11, "tie-a", 5)));
            Current.SetValue(null, Capture(
                AllocationSample(1, "matched", 52, 45), AllocationSample(2, "decrease", 20, 10),
                AllocationSample(3, "zero", 3, 1), AllocationSample(4, "missing", 1),
                AllocationSample(5, "partial", 20), AllocationSample(6, "partial", 20, measured: false),
                AllocationSample(7, "added", 20), AllocationSample(8, "large", large + 1),
                AllocationSample(9, "unchanged", 0), AllocationSample(10, "tie-b", 7), AllocationSample(11, "tie-a", 7)));
            var comparison = Call("compare", ",\"sort\":\"gcAllocationCount\"")["comparison"];
            var matched = Row(comparison, "matched");
            t.True((bool)matched["allocationComparable"], "Complete counts can be compared");
            t.Equal("12", matched["delta"]["gcAllocationCount"].ToString(), "Inclusive delta is current minus baseline");
            t.Equal("-23", matched["delta"]["gcSelfAllocationCount"].ToString(), "Self delta excludes each capture's children");
            t.Equal("Integer", matched["delta"]["gcAllocationCount"].Type, "Allocation deltas retain integer JSON type");
            t.Near(30, (double)matched["percent"]["gcAllocationCount"], 1e-9, "Allocation percent uses baseline");
            t.Near(-23d / 30 * 100, (double)matched["percent"]["gcSelfAllocationCount"], 1e-9, "Self percent uses self baseline");
            t.Equal("-30", Row(comparison, "decrease")["delta"]["gcAllocationCount"].ToString(), "Negative allocation delta");
            foreach (string phase in new[] { "missing", "partial", "added", "removed" })
            {
                var row = Row(comparison, phase);
                t.True(!(bool)row["allocationComparable"], phase + " is not comparable");
                foreach (string key in new[] { "gcAllocationCount", "gcSelfAllocationCount" })
                {
                    t.Equal("Null", row["delta"][key].Type, phase + " delta is unavailable, not zero");
                    t.Equal("Null", row["percent"][key].Type, phase + " percent is unavailable");
                }
            }
            var partial = Row(comparison, "partial");
            t.True((bool)partial["before"]["partial"] && (bool)partial["after"]["partial"], "Both partial sums retain coverage flags");
            t.Equal(1, (int)partial["after"]["measuredCalls"], "Measured call count exposed");
            t.Equal(2, (int)partial["after"]["eligibleCalls"], "Eligible call count exposed");
            t.Equal("20", partial["after"]["gcAllocationCount"].ToString(), "Measured subset stays visible without delta");
            t.Equal("3", Row(comparison, "zero")["delta"]["gcAllocationCount"].ToString(), "Zero baseline allows absolute delta");
            t.Equal("Null", Row(comparison, "zero")["percent"]["gcAllocationCount"].Type, "Zero baseline never divides by zero");
            t.Equal("0", Row(comparison, "unchanged")["delta"]["gcAllocationCount"].ToString(), "Measured unchanged zero stays zero");
            t.Equal(large.ToString(CultureInfo.InvariantCulture), Row(comparison, "large")["before"]["gcAllocationCount"].ToString(), "Large count retains precision");
            t.Equal("1", Row(comparison, "large")["delta"]["gcAllocationCount"].ToString(), "Long delta avoids floating-point cancellation");
            t.Equal("decrease,matched,zero,tie-a,tie-b,large,unchanged,added,missing,partial,removed",
                string.Join(",", comparison["rows"].Select(x => (string)x["phase"])), "Inclusive absolute-delta order, stable ties and unavailable last");
            var self = Call("compare", ",\"sort\":\"gcSelfAllocationCount\"")["comparison"];
            t.Equal("matched", (string)self["rows"].First()["phase"], "Self counts sort independently from inclusive");
            t.Equal("matched", (string)Call("compare", ",\"sort\":\"gcAllocationCount\",\"offset\":1,\"limit\":1")["comparison"]["rows"].Single()["phase"], "Allocation sorting precedes paging");
            t.Equal(1, Call("compare", ",\"sort\":\"gcSelfAllocationCount\",\"phase\":\"matched\"")["comparison"]["rows"].Count(), "Allocation comparison respects filters");
        }
        finally { Current.SetValue(null, savedCurrent); Baseline.SetValue(null, savedBaseline); }
    }

    static void CheckUnconfirmedGpu(TestContext t)
    {
        object savedCurrent = Current.GetValue(null), savedBaseline = Baseline.GetValue(null);
        try
        {
            var jobs = Capture(Sample(1, "fx.render", 2, gpu: "unconfirmed"));
            Set(jobs, "graphicsJobs", (bool?)true); Set(jobs, "renderingThreadingMode", "NativeGraphicsJobsSplitThreading");
            Current.SetValue(null, jobs);
            var read = Call("read");
            t.True((bool)read["capture"]["graphicsJobs"], "Stopped capture records actual Graphics Jobs independently of the current Editor");
            t.Equal("NativeGraphicsJobsSplitThreading", (string)read["capture"]["renderingThreadingMode"], "Actual threading mode exposed");
            t.True(((string)read["capture"]["gpuWarning"]).Contains("restart Unity"), "Capture carries timing warning");
            t.Near(2, (double)read["events"].Single()["gpuMs"], 0, "Unconfirmed event retains native duration");
            t.Equal("unconfirmed", (string)read["events"].Single()["gpuStatus"], "Event confidence explicit");
            t.Near(2, (double)read["summary"].Single()["gpuTotalMs"], 0, "Unconfirmed duration participates in Summary");
            t.Equal(1, (int)read["summary"].Single()["gpuUnconfirmedCount"], "Unconfirmed subset count exposed");
            t.Equal(0, (int)read["summary"].Single()["gpuUnavailableCount"], "Unconfirmed is not unavailable");
            var brief = Call("summary")["brief"];
            t.Equal(1, (int)brief["gpuHotspots"].Single()["gpuUnconfirmedCount"], "Agent GPU hotspots retain confidence");
            t.True(brief["warnings"].Any(x => x.ToString().Contains("Graphics Jobs")), "Compact summary warning");
            Invoke("PinBaseline");
            t.Equal((bool?)true, (bool?)Get(Baseline.GetValue(null), "graphicsJobs"), "Baseline preserves device context");
            Current.SetValue(null, Capture(Sample(1, "fx.render", 2, gpu: "ready")));
            var comparison = Call("compare")["comparison"];
            var row = Row(comparison, "fx.render");
            t.True((bool)row["gpuComparable"] && (bool)row["gpuUnconfirmed"], "Numeric comparison remains available but explicitly unconfirmed");
            t.Near(0, (double)row["delta"]["gpuMs"], 0, "Unconfirmed delta is not silently discarded");
            t.True(comparison["warnings"].Any(x => x.ToString().Contains("Baseline:") && x.ToString().Contains("Graphics Jobs")), "Historical baseline warning survives a supported current capture");
            using var stream = new System.IO.MemoryStream();
            Invoke("WriteSnapshot", stream, jobs); stream.Position = 0;
            object restored = Invoke("ReadSnapshot", stream);
            t.Equal((bool?)true, (bool?)Get(restored, "graphicsJobs"), "Reload preserves capture confidence context");
            t.Equal("NativeGraphicsJobsSplitThreading", (string)Get(restored, "renderingThreadingMode"), "Reload preserves actual mode");
            t.Equal("unconfirmed", (string)Get(((Array)Get(restored, "events")).GetValue(0), "gpuStatus"), "Reload preserves per-event confidence");
        }
        finally { Current.SetValue(null, savedCurrent); Baseline.SetValue(null, savedBaseline); }
    }

    public static string Run()
    {
        var status = Call("status")["capture"];
        if (status.Type != "Null" && ((bool)status["recording"] || (int)status["openSpans"] != 0 || (int)status["pendingGpuSamples"] != 0))
            return TestContext.Result("skipped", 0, "Requires an idle profiler; user recording is untouched.").ToJson();
        return TestContext.Run("Profiler comparison and compact agent analysis; retained state preserved", t =>
        {
            object savedCurrent = Current.GetValue(null), savedBaseline = Baseline.GetValue(null);
            string savedToken = SessionState.GetString(BaselineKey, "");
            SessionState.EraseString(BaselineKey); // Never delete a user's retained snapshot through fixture controls.
            try
            {
                var root = Sample(1, "document.render", 10, null); Set(root, "childTicks", Ticks(6));
                var first = Capture(root, Sample(2, "layer.render", 6, parent: 1), Sample(3, "fx.render", 4, gpu: "ready"),
                    Sample(4, "removed", 2), Sample(5, "zero", 0));
                Current.SetValue(null, first); Baseline.SetValue(null, null);
                string beforeId = (string)Get(first, "id");
                t.True(!(bool)Call("baseline")["success"], "Pin requires explicit current capture identity");
                t.True(!(bool)Call("baseline", ",\"captureId\":\"stale\"")["success"], "Stale capture cannot be pinned");
                t.True((bool)Call("baseline", ",\"captureId\":\"" + beforeId + "\"")["success"], "Pin stopped capture");
                object frozen = Baseline.GetValue(null);
                t.True(!ReferenceEquals(first, frozen) && !ReferenceEquals(Get(first, "events"), Get(frozen, "events")), "Baseline is an independent snapshot");
                var modified = ((Array)Get(first, "events")).GetValue(0); Set(modified, "end", Ticks(90)); ((Array)Get(first, "events")).SetValue(modified, 0);
                t.Near(Ticks(11), (long)Get(((Array)Get(frozen, "events")).GetValue(0), "end"), 0, "Later mutation cannot alter baseline");

                var nextRoot = Sample(1, "document.render", 12, null); Set(nextRoot, "childTicks", Ticks(9));
                var second = Capture(nextRoot, Sample(2, "layer.render", 4, parent: 1, name: "Renamed"), Sample(3, "layer.render", 5, parent: 1, name: "Renamed"),
                    Sample(4, "fx.render", 2, gpu: "ready"), Sample(5, "added", 1), Sample(6, "zero", 1),
                    Sample(7, "layer.render", 3, "b", name: "Renamed"), Sample(8, "effect.cache", 0, kind: "cache", detail: "hit"),
                    Sample(9, "effect.cache", 0, kind: "cache", detail: "miss:empty"), Sample(10, "cache.reason", 0, kind: "reason", detail: "empty"));
                var reason = ((Array)Get(second, "events")).GetValue(9); Set(reason, "value", 4d); ((Array)Get(second, "events")).SetValue(reason, 9);
                Current.SetValue(null, second);
                var comparison = Call("compare")["comparison"];
                var layer = Row(comparison, "layer.render", "a");
                t.Equal("matched", (string)layer["status"], "Renaming does not change identity");
                t.Near(3, (double)layer["delta"]["totalMs"], 1e-6, "Total cost increased with workload");
                t.Near(-1.5, (double)layer["delta"]["meanMs"], 1e-6, "Mean cost separately shows improvement");
                t.Near(50, (double)layer["percent"]["totalMs"], 1e-6, "Percent uses baseline denominator");
                t.True((bool)layer["countChanged"], "Workload difference is explicit");
                t.Equal("added", (string)Row(comparison, "layer.render", "b")["status"], "Equal display names never merge separate layers");
                t.Equal("removed", (string)Row(comparison, "removed")["status"], "Removed stage identified");
                t.Equal("Null", Row(comparison, "removed")["delta"]["totalMs"].Type, "Missing timing is not zero");
                t.Equal("Null", Row(comparison, "zero")["percent"]["totalMs"].Type, "Zero baseline percent is undefined");
                t.True((bool)Row(comparison, "fx.render")["gpuComparable"], "Fully measured GPU rows comparable");
                t.Near(0, (double)Row(comparison, "fx.render")["delta"]["gpuMs"], 0, "GPU delta independent of CPU change");
                t.Near(10 + 4 + 2, (double)comparison["before"]["rootWallMs"], 1e-6, "Roots exclude nested child total");
                t.True(comparison["warnings"].Any(x => x.ToString().Contains("Call counts differ")), "Workload warning included");
                var page = Call("compare", ",\"limit\":2")["comparison"];
                t.Equal(2, page["rows"].Count(), "Comparison page bounded"); t.Equal(2, (int)page["nextOffset"], "Next offset");
                t.True(comparison["rows"].Take(2).Select(x=>x.ToString()).SequenceEqual(page["rows"].Select(x=>x.ToString())), "Stable comparison paging");
                t.True(!(bool)Call("compare", ",\"operationId\":1")["success"], "Capture-local operation cannot filter two captures");
                t.True(!(bool)Call("compare", ",\"baselineId\":\"stale\"")["success"], "Stale baseline rejected");
                t.True(!(bool)Call("compare", ",\"sort\":\"unknown\"")["success"], "Unknown metric rejected");
                t.True(!(bool)Call("compare", ",\"limit\":2001")["success"], "Page size bounded");
                t.Equal(1, Call("compare", ",\"phase\":\"layer.render\",\"layerId\":\"a\"")["comparison"]["rows"].Count(), "Same filters apply to both captures");
                t.Equal(0, Call("compare", ",\"groups\":[]")["comparison"]["rows"].Count(), "No groups means no stages");

                var briefEnvelope = Call("summary", ",\"top\":1"); var brief = briefEnvelope["brief"];
                t.True(briefEnvelope["events"] == null && briefEnvelope["summary"] == null, "Compact action does not dump raw events or full legacy summary");
                t.Equal(1, brief["hotspots"].Count(), "Hotspots capped"); t.Equal(1, brief["slowOperations"].Count(), "Slow operations capped");
                t.True((int)brief["omitted"]["hotspots"] > 0, "Omitted stages counted");
                t.Equal("layer.render", (string)brief["hotspots"].First()["phase"], "Hotspots rank by self wall time, not inclusive root");
                t.Equal(1, brief["gpuHotspots"].Count(), "GPU costs have their own bounded list");
                t.Equal(1, (int)brief["cache"]["hits"], "Cache hits counted"); t.Equal(1, (int)brief["cache"]["misses"], "Reasons do not double-count misses");
                t.Near(.5, (double)brief["cache"]["missRate"], 0, "Cache rate denominator is attempts");
                t.Near(4, (double)brief["reasons"].First()["occurrences"], 0, "Occurrences preserved separately from reason events");
                t.Equal(1, (int)brief["reasons"].First()["events"], "One batched reason event");
                t.True(!(bool)Call("summary", ",\"top\":21")["success"], "Compact top limit enforced");
                t.Equal(0, Call("summary", ",\"phase\":\"nothing\"")["brief"]["hotspots"].Count(), "Empty filtered summary");

                var fx = ((Array)Get(second, "events")).GetValue(3); Set(fx, "gpuStatus", "unavailable"); ((Array)Get(second, "events")).SetValue(fx, 3);
                var noGpu = Row(Call("compare")["comparison"], "fx.render");
                t.True(!(bool)noGpu["gpuComparable"] && noGpu["delta"]["gpuMs"].Type == "Null", "Unavailable GPU never becomes false speedup");
                Set(second, "recording", true); Set(second, "deadline", long.MaxValue);
                t.True(!(bool)Call("compare")["success"], "Active comparison refused");
                t.True(Call("summary")["brief"]["warnings"].Any(x=>x.ToString().Contains("active")), "Live brief explicitly partial");
                Set(second, "recording", false); Set(second, "dropped", 2);
                t.True(Call("summary")["brief"]["warnings"].Any(x=>x.ToString().Contains("truncated")), "Truncation visible");

                var mixed = Capture(Sample(1, "fx.render", 2, gpu: "ready"), Sample(2, "fx.render", 3, gpu: "unavailable"),
                    Sample(3, "unfinished", 500, complete: false), Sample(4, "legacy", 900, kind: "phase"));
                var measured = ((Array)Get(mixed, "events")).GetValue(0);
                Set(measured, "allocationCountMeasured", true); Set(measured, "gcAllocationCount", 5L); Set(measured, "gcChildAllocationCount", 2L);
                ((Array)Get(mixed, "events")).SetValue(measured, 0);
                Current.SetValue(null, mixed);
                var mixedBrief = Call("summary")["brief"];
                var allocation = mixedBrief["allocationHotspots"].Single();
                t.Near(5, (double)allocation["gcAllocationCount"], 0, "Allocation hotspot counts measured calls only");
                t.Near(3, (double)allocation["gcSelfAllocationCount"], 0, "Allocation hotspot ranks self count, not bytes");
                t.True((bool)allocation["partial"] && (int)allocation["measuredCalls"] == 1 && (int)allocation["eligibleCalls"] == 2,
                    "Missing allocation samples are explicitly partial");
                t.True(allocation["gcAllocatedBytes"] == null, "No per-stage byte totals in compact analysis");
                t.Near(5, (double)mixedBrief["rootWallMs"], 1e-6, "Interrupted and legacy durations do not inflate roots");
                t.True(mixedBrief["hotspots"].All(x => (string)x["phase"] == "fx.render"), "Incomplete/legacy stages are not hotspots");
                t.True((bool)mixedBrief["gpuHotspots"].First()["partial"], "Partial GPU total explicitly marked");
                var mixedRow = Row(Call("compare")["comparison"], "fx.render");
                t.Near(2, (double)mixedRow["after"]["gpuMs"], 0, "Measured subset retained");
                t.Equal("Null", mixedRow["delta"]["gpuMs"].Type, "Partially measured row has no GPU delta");
                t.Equal("Null", mixedBrief["cache"]["missRate"].Type, "No lookups is not a zero miss rate");
                Set(mixed, "open", 1);
                t.True(!(bool)Call("compare")["success"], "Stopped capture with unfinished active scope cannot compare");
                Set(mixed, "open", 0);
                Current.SetValue(null, second);

                CheckAllocationComparison(t);
                CheckUnconfirmedGpu(t);

                // Exercise the actual baseline save/restore functions with an isolated token and owned Temp file.
                Invoke("SaveBaselineForReload"); string ownToken = SessionState.GetString(BaselineKey, "");
                t.True(ownToken.Length > 0, "Baseline snapshot written");
                Baseline.SetValue(null, null); Invoke("RestoreBaselineAfterReload");
                t.Equal(beforeId, (string)Get(Baseline.GetValue(null), "id"), "Baseline ID restored by reload path");
                t.Equal(UnityEngine.Application.unityVersion, (string)Get(Baseline.GetValue(null), "unityVersion"), "Recorded Unity version preserved");
                t.True(!System.IO.File.Exists((string)Invoke("SnapshotPath", ownToken)), "Consumed owned snapshot removed");
                t.Equal("", SessionState.GetString(BaselineKey, ""), "Owned reload token consumed");
                Current.SetValue(null, null);
                t.True(Call("status")["baseline"].Type != "Null", "Baseline remains available without current capture");
                t.True(!(bool)Call("clearBaseline", ",\"baselineId\":\"stale\"")["success"], "Clear baseline checks identity");
                t.True((bool)Call("clearBaseline", ",\"baselineId\":\"" + beforeId + "\"")["success"], "Baseline independently clearable");
                t.True(Baseline.GetValue(null) == null, "Clear releases baseline reference");
            }
            finally
            {
                Invoke("DiscardBaselineBackup"); Current.SetValue(null, savedCurrent); Baseline.SetValue(null, savedBaseline);
                if (savedToken.Length > 0) SessionState.SetString(BaselineKey, savedToken); else SessionState.EraseString(BaselineKey);
            }
            t.True(ReferenceEquals(savedCurrent, Current.GetValue(null)) && ReferenceEquals(savedBaseline, Baseline.GetValue(null)), "User current and baseline references restored");
            t.Equal(savedToken, SessionState.GetString(BaselineKey, ""), "User reload token preserved");
        });
    }
}
