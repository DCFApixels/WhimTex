using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private sealed class ProfileAggregate
        {
            internal WhimTexProfiler.Sample example;
            internal int count, completed, gpuCount, gpuUnconfirmed, gpuPending, gpuUnavailable;
            internal double total, self, max, prepare, submit, gpu;
            internal bool fx;
            internal long gcAllocationCount, gcSelfAllocationCount;
            internal int measuredCalls;
            internal void Add(WhimTexProfiler.Sample sample)
            {
                if (count++ == 0) example = sample;
                fx |= sample.fxTiming;
                if (WhimTexProfiler.HasGpuDuration(sample))
                { gpuCount++; if (sample.gpuStatus == "unconfirmed") gpuUnconfirmed++; gpu += sample.gpuNanoseconds / 1000000d; }
                else if (sample.gpuStatus == "pending") gpuPending++;
                else if (sample.fxTiming || sample.gpuStatus != null) gpuUnavailable++;
                if (!sample.completed) return;
                completed++;
                if (ProfileAllocationMeasured(sample))
                {
                    measuredCalls++; gcAllocationCount += sample.gcAllocationCount;
                    gcSelfAllocationCount += Math.Max(0, sample.gcAllocationCount - sample.gcChildAllocationCount);
                }
                double duration = WhimTexProfiler.Milliseconds(sample.end - sample.start);
                total += duration; max = Math.Max(max, duration);
                self += Math.Max(0, duration - WhimTexProfiler.Milliseconds(sample.childTicks));
                prepare += WhimTexProfiler.Milliseconds(sample.cpuPrepareTicks);
                submit += WhimTexProfiler.Milliseconds(sample.cpuSubmitTicks);
            }
            internal void AllocationMetrics(JObject row) =>
                ProfileAllocationMetrics(row, gcAllocationCount, gcSelfAllocationCount, measuredCalls, count);

            internal JObject Metrics()
            {
                var row = new JObject {
                    ["count"] = count, ["completedCount"] = completed,
                    ["totalMs"] = Optional(completed > 0, total), ["selfMs"] = Optional(completed > 0, self),
                    ["meanMs"] = Optional(completed > 0, completed == 0 ? 0 : total / completed), ["maxMs"] = Optional(completed > 0, max),
                    ["cpuPrepareMs"] = Optional(fx && completed > 0, prepare), ["cpuSubmitMs"] = Optional(fx && completed > 0, submit),
                    ["gpuMs"] = Optional(gpuCount > 0, gpu), ["gpuMeanMs"] = Optional(gpuCount > 0, gpuCount == 0 ? 0 : gpu / gpuCount),
                    ["gpuCount"] = gpuCount, ["gpuUnconfirmedCount"] = gpuUnconfirmed, ["gpuPendingCount"] = gpuPending, ["gpuUnavailableCount"] = gpuUnavailable,
                    ["eventId"] = example.id, ["layerName"] = example.layerName, ["fxName"] = example.fxName
                };
                AllocationMetrics(row);
                return row;
            }
        }

        private sealed class ProfileAnalysisData
        {
            internal readonly Dictionary<(string phase, string document, string layer, int fx), ProfileAggregate> stages = new();
            internal readonly Dictionary<(string phase, string reason), (int count, double occurrences, long eventId)> reasons = new();
            internal readonly List<WhimTexProfiler.Sample> roots = new();
            internal readonly HashSet<string> documents = new(StringComparer.Ordinal);
            internal readonly HashSet<long> matchedEventIds = new();
            internal int matched, spans, completed, interrupted, hits, misses, bypasses, releases, legacy;
            internal double rootMs;
            internal JObject Totals() => new JObject { ["rootWallMs"] = rootMs, ["rootCount"] = roots.Count,
                ["matchedEvents"] = matched, ["spanCount"] = spans, ["completedSpanCount"] = completed };
        }

        private static JToken Optional(bool available, double value) => available ? new JValue(value) : JValue.CreateNull();

        private static JObject ProfileAnalysis(JObject request, string action)
        {
            if (action == "baseline") Keys(request, "apiVersion", "action", "captureId");
            else if (action == "clearBaseline") Keys(request, "apiVersion", "action", "baselineId");
            else if (action == "summary") Keys(request, "apiVersion", "action", "captureId", "top", "operationId", "documentId", "layerId", "layer", "phase", "groups");
            else Keys(request, "apiVersion", "action", "captureId", "baselineId", "offset", "limit", "sort", "documentId", "layerId", "layer", "phase", "groups");
            var capture = WhimTexProfiler.Current;
            string captureId = Text(request, "captureId"), baselineId = Text(request, "baselineId");
            if (captureId != null || action == "baseline")
                Require(capture != null && capture.id == captureId, "The capture ID is missing or no longer current.", "capture_conflict");
            if (baselineId != null || action == "clearBaseline")
                Require(WhimTexProfiler.Baseline != null && WhimTexProfiler.Baseline.id == baselineId,
                    "The baseline ID is missing or no longer current.", "baseline_conflict");
            if (action == "clearBaseline") WhimTexProfiler.ClearBaseline();
            else
            {
                Require(capture != null, "No capture exists.", "capture_missing");
                if (action != "summary") RequireClosedProfile(capture);
                if (action == "baseline") WhimTexProfiler.PinBaseline();
            }
            var result = Success(); result["capture"] = CaptureInfo(capture); result["baseline"] = CaptureInfo(WhimTexProfiler.Baseline);
            if (action == "baseline" || action == "clearBaseline") return result;
            var current = AnalyzeProfile(capture, request);
            if (action == "summary")
                result["brief"] = BriefProfile(capture, current, Int(request, "top", 5, 1, 20));
            else
            {
                Require(WhimTexProfiler.Baseline != null, "Set a baseline from a stopped capture first.", "baseline_missing");
                var baseline = WhimTexProfiler.Baseline;
                var before = AnalyzeProfile(baseline, request);
                result["comparison"] = CompareProfiles(baseline, capture, before, current, request);
            }
            return result;
        }

        private static void RequireClosedProfile(WhimTexProfiler.Capture source)
        {
            Require(!source.recording && source.open == 0, "Stop recording and finish active scopes first.", "capture_active");
            Require(WhimTexProfiler.PendingGpu(source) == 0, "Wait for pending GPU timings first.", "capture_gpu_pending");
        }

        private static ProfileAnalysisData AnalyzeProfile(WhimTexProfiler.Capture source, JObject request)
        {
            var data = new ProfileAnalysisData();
            string document = ProfileDocumentId(request), layer = Text(request, "layerId"), phase = Text(request, "phase"), search = Text(request, "layer");
            int operation = Int(request, "operationId", 0, 0, int.MaxValue);
            var groups = ProfileGroups(request);
            for (int i = 0; i < source.count; i++)
            {
                var sample = source.events[i];
                if (!ProfileMatches(sample, operation, document, layer, phase, search) ||
                    (groups & WhimTexProfiler.GroupFor(sample.phase, sample.kind)) == 0) continue;
                data.matched++;
                data.matchedEventIds.Add(sample.id);
                if (sample.documentId != null) data.documents.Add(sample.documentId);
                if (sample.kind == "span")
                {
                    data.spans++; if (sample.completed) data.completed++;
                    if (sample.interrupted) data.interrupted++;
                    var key = (sample.phase, sample.documentId, sample.layerId, sample.fxIndex);
                    if (!data.stages.TryGetValue(key, out var stage)) data.stages.Add(key, stage = new ProfileAggregate());
                    stage.Add(sample);
                    if (sample.parentId == 0 && sample.completed)
                    {
                        data.roots.Add(sample);
                        data.rootMs += WhimTexProfiler.Milliseconds(sample.end - sample.start);
                    }
                }
                else if (sample.kind == "phase") data.legacy++;
                if (sample.phase == "effect.cache")
                {
                    if (sample.detail == "hit") data.hits++;
                    else if (sample.detail?.StartsWith("miss:", StringComparison.Ordinal) == true) data.misses++;
                    else if (sample.detail?.StartsWith("bypass:", StringComparison.Ordinal) == true) data.bypasses++;
                }
                if (sample.phase == "effect.cache.release") data.releases++;
                if (sample.kind == "reason")
                {
                    var key = (sample.phase, sample.detail);
                    data.reasons.TryGetValue(key, out var reason);
                    data.reasons[key] = (reason.count + 1, reason.occurrences + sample.value, reason.eventId == 0 ? sample.id : reason.eventId);
                }
            }
            return data;
        }

        private static List<string> ProfileWarnings(WhimTexProfiler.Capture source, ProfileAnalysisData data)
        {
            var warnings = new List<string>();
            if (source.recording) warnings.Add("Recording is still active; this is a partial snapshot.");
            if (source.open > 0 || data.spans != data.completed || data.interrupted > 0) warnings.Add("Unfinished/interrupted spans are excluded from timing aggregates.");
            if (source.dropped > 0 || source.stopReason == "event_limit" || source.stopReason == "document_limit") warnings.Add("Capture was truncated; totals are incomplete.");
            if (data.matched == 0) warnings.Add("No events match the filters.");
            if (data.legacy > 0) warnings.Add("Legacy post-hoc phase timings are excluded from this analysis.");
            if (data.stages.Values.Any(s => s.gpuPending > 0)) warnings.Add("GPU results are pending; GPU totals are partial.");
            if (data.stages.Values.Any(s => s.gpuUnavailable > 0)) warnings.Add("Some GPU timings are unavailable; null does not mean zero.");
            if (source.graphicsJobs == true || data.stages.Values.Any(s => s.gpuUnconfirmed > 0))
                warnings.Add(WhimTexProfiler.GpuUnconfirmedWarning);
            if (data.stages.Values.Any(s => s.measuredCalls < s.count)) warnings.Add("Stage allocation counts are partial or unavailable; inspect measuredCalls/eligibleCalls. Null does not mean zero.");
            if (data.roots.Count == 0 && data.spans > 0) warnings.Add("No completed roots match these filters; rootWallMs is not the selected child work total.");
            return warnings;
        }

        private static JObject ProfileStageIdentity(ProfileAggregate stage)
        {
            var sample = stage.example;
            return new JObject { ["phase"] = sample.phase, ["kind"] = "span",
                ["group"] = WhimTexProfiler.GroupId(WhimTexProfiler.GroupFor(sample.phase, sample.kind)),
                ["documentId"] = sample.documentId, ["layerId"] = sample.layerId, ["layerName"] = sample.layerName,
                ["fxIndex"] = sample.fxIndex, ["fxName"] = sample.fxName };
        }

        private static JObject BriefProfile(WhimTexProfiler.Capture source, ProfileAnalysisData data, int top)
        {
            var hotspots = new JArray();
            var stages = data.stages.Values.Where(s => s.completed > 0).OrderByDescending(s => s.self)
                .ThenBy(s => s.example.phase, StringComparer.Ordinal).ThenBy(s => s.example.id).ToList();
            foreach (var stage in stages.Take(top))
            {
                var row = ProfileStageIdentity(stage);
                row["eventId"] = stage.example.id; row["calls"] = stage.count; row["completedCalls"] = stage.completed;
                row["totalMs"] = stage.total; row["selfMs"] = stage.self; row["meanMs"] = stage.total / stage.completed; row["maxMs"] = stage.max;
                stage.AllocationMetrics(row);
                if (stage.fx)
                {
                    row["cpuPrepareMs"] = stage.prepare; row["cpuSubmitMs"] = stage.submit;
                    row["gpuMs"] = Optional(stage.gpuCount > 0, stage.gpu); row["gpuCount"] = stage.gpuCount;
                    row["gpuUnconfirmedCount"] = stage.gpuUnconfirmed;
                    row["gpuPendingCount"] = stage.gpuPending; row["gpuUnavailableCount"] = stage.gpuUnavailable;
                }
                hotspots.Add(row);
            }
            var operations = new JArray(data.roots.OrderByDescending(s => s.end - s.start).ThenBy(s => s.id).Take(top).Select(s =>
                new JObject { ["eventId"] = s.id, ["phase"] = s.phase, ["documentId"] = s.documentId, ["layerName"] = s.layerName,
                    ["durationMs"] = WhimTexProfiler.Milliseconds(s.end - s.start), ["changeReason"] = s.changeReason }));
            var reasons = new JArray(data.reasons.OrderByDescending(r => r.Value.count).ThenBy(r => r.Key.phase, StringComparer.Ordinal)
                .ThenBy(r => r.Key.reason, StringComparer.Ordinal).Take(top).Select(r => new JObject {
                    ["phase"] = r.Key.phase, ["reason"] = r.Key.reason, ["events"] = r.Value.count,
                    ["occurrences"] = r.Value.occurrences, ["eventId"] = r.Value.eventId }));
            var result = data.Totals();
            result["hotspots"] = hotspots; result["slowOperations"] = operations; result["reasons"] = reasons;
            var allocationStages = data.stages.Values.Where(s => s.measuredCalls > 0)
                .OrderByDescending(s => s.gcSelfAllocationCount).ThenBy(s => s.example.id).ToList();
            result["allocationHotspots"] = new JArray(allocationStages.Take(top).Select(s => {
                    var row = ProfileStageIdentity(s); row["eventId"] = s.example.id;
                    s.AllocationMetrics(row); return row;
                }));
            var gpuStages = data.stages.Values.Where(s => s.gpuCount > 0).OrderByDescending(s => s.gpu)
                .ThenBy(s => s.example.phase, StringComparer.Ordinal).ThenBy(s => s.example.id).ToList();
            result["gpuHotspots"] = new JArray(gpuStages.Take(top).Select(s => {
                var row = ProfileStageIdentity(s); row["eventId"] = s.example.id;
                row["gpuMs"] = s.gpu; row["gpuMeanMs"] = s.gpu / s.gpuCount; row["gpuCount"] = s.gpuCount;
                row["gpuUnconfirmedCount"] = s.gpuUnconfirmed;
                row["calls"] = s.count; row["partial"] = s.gpuCount != s.count; return row;
            }));
            result["cache"] = new JObject { ["hits"] = data.hits, ["misses"] = data.misses, ["bypasses"] = data.bypasses,
                ["releases"] = data.releases, ["missRate"] = Optional(data.hits + data.misses > 0, data.hits + data.misses == 0 ? 0 : (double)data.misses / (data.hits + data.misses)) };
            result["omitted"] = new JObject { ["hotspots"] = Math.Max(0, stages.Count - top), ["gpuHotspots"] = Math.Max(0, gpuStages.Count - top),
                ["allocationHotspots"] = Math.Max(0, allocationStages.Count - top),
                ["operations"] = Math.Max(0, data.roots.Count - top), ["reasons"] = Math.Max(0, data.reasons.Count - top) };
            result["warnings"] = new JArray(ProfileWarnings(source, data));
            result["memory"] = ProfileMemory(source);
            result["causes"] = ProfileCauseSummary(source, data, top);
            var asyncEvents = source.events.Take(source.count).Where(s => data.matchedEventIds.Contains(s.id) &&
                (s.kind == "worker" || s.kind == "latency")).ToArray();
            result["workerHotspots"] = new JArray(asyncEvents.Where(s => s.kind == "worker" && s.completed)
                .OrderByDescending(s => s.end - s.start).Take(top).Select(s => new JObject {
                    ["eventId"] = s.id, ["originId"] = s.originId, ["inputId"] = s.inputId,
                    ["phase"] = s.phase, ["threadId"] = s.threadId,
                    ["durationMs"] = WhimTexProfiler.Milliseconds(s.end - s.start),
                    ["queueMs"] = WhimTexProfiler.Milliseconds(s.queuedTicks), ["allocatedBytes"] = s.workerAllocatedBytes >= 0 ? new JValue(s.workerAllocatedBytes) : JValue.CreateNull(),
                    ["encodeMs"] = Optional(s.workerStagesMeasured, WhimTexProfiler.Milliseconds(s.workerEncodeTicks)),
                    ["writeMs"] = Optional(s.workerStagesMeasured, WhimTexProfiler.Milliseconds(s.workerWriteTicks)),
                    ["hashMs"] = Optional(s.workerStagesMeasured, WhimTexProfiler.Milliseconds(s.workerHashTicks))
                }));
            result["inputLatencies"] = new JArray(asyncEvents.Where(s => s.kind == "latency")
                .OrderByDescending(s => s.completed ? s.end - s.start : 0).Take(top).Select(s => new JObject {
                    ["eventId"] = s.id, ["phase"] = s.phase, ["status"] = s.asyncStatus,
                    ["durationMs"] = Optional(s.completed, WhimTexProfiler.Milliseconds(s.end - s.start)),
                    ["latestInputDurationMs"] = Optional(s.completed, WhimTexProfiler.Milliseconds(s.end - s.latestInputTicks)),
                    ["coalescedInputs"] = s.coalescedInputs, ["completionPollIntervalMs"] = s.completionPollIntervalMs
                }));
            result["asyncAccounting"] = "Worker wall time excludes queue time and overlaps main-thread waits. Worker/latency events are excluded from rootWallMs and CPU hotspots. Canvas latency starts at brush input or the first render request for other changes. GPU completion is observed by polling a fence; it is not physical presentation latency. Missing completion is not zero.";
            result["accounting"] = "Hotspots rank by self wall time; allocationHotspots rank by self managed allocation count on the main thread, including instrumentation. Stage counts are not bytes. Allocation aggregates sum valid measured calls only: inspect measuredCalls, eligibleCalls and partial; eligibleCalls includes open/interrupted spans. Nested inclusive counts/times and CPU/GPU overlap; do not sum them. Cache counts are lookup attempts, not renders. Reasons are last-change context, not a causal proof. Use eventId with read to inspect details.";
            return result;
        }

        private static readonly string[] ProfileComparisonMetrics = { "count", "totalMs", "selfMs", "meanMs", "maxMs", "cpuPrepareMs", "cpuSubmitMs", "gpuMs", "gpuMeanMs", "gcAllocationCount", "gcSelfAllocationCount" };

        private static JObject CompareProfiles(WhimTexProfiler.Capture baseline, WhimTexProfiler.Capture current,
            ProfileAnalysisData before, ProfileAnalysisData after, JObject request)
        {
            int offset = Int(request, "offset", 0, 0, int.MaxValue), limit = Int(request, "limit", 20, 1, 2000);
            string sort = Text(request, "sort", "selfMs");
            Require(new[] { "selfMs", "totalMs", "meanMs", "maxMs", "gpuMs", "count", "gcAllocationCount", "gcSelfAllocationCount" }.Contains(sort), "Unknown comparison sort metric.");
            var rows = new List<JObject>();
            foreach (var key in before.stages.Keys.Union(after.stages.Keys))
            {
                before.stages.TryGetValue(key, out var a); after.stages.TryGetValue(key, out var b);
                var row = ProfileStageIdentity(b ?? a); var x = a?.Metrics(); var y = b?.Metrics();
                row["status"] = a == null ? "added" : b == null ? "removed" : "matched";
                row["before"] = x; row["after"] = y;
                row["countChanged"] = (a?.count ?? 0) != (b?.count ?? 0);
                bool gpuComparable = a != null && b != null && a.gpuCount == a.count && b.gpuCount == b.count && a.count > 0 && b.count > 0;
                row["gpuComparable"] = gpuComparable;
                row["gpuUnconfirmed"] = (a?.gpuUnconfirmed ?? 0) + (b?.gpuUnconfirmed ?? 0) > 0;
                bool allocationComparable = a != null && b != null && a.count > 0 && b.count > 0 &&
                    a.measuredCalls == a.count && b.measuredCalls == b.count;
                row["allocationComparable"] = allocationComparable;
                var delta = new JObject(); var percent = new JObject();
                foreach (string metric in ProfileComparisonMetrics)
                {
                    if (metric == "gcAllocationCount" || metric == "gcSelfAllocationCount")
                    {
                        long? countBefore = x?.Value<long?>(metric), countAfter = y?.Value<long?>(metric);
                        bool countAvailable = allocationComparable && countBefore.HasValue && countAfter.HasValue;
                        long countDifference = countAvailable ? countAfter.Value - countBefore.Value : 0;
                        delta[metric] = countAvailable ? new JValue(countDifference) : JValue.CreateNull();
                        percent[metric] = Optional(countAvailable && countBefore != 0,
                            countAvailable && countBefore != 0 ? (double)countDifference / countBefore.Value * 100d : 0);
                        continue;
                    }
                    double? av = x?.Value<double?>(metric), bv = y?.Value<double?>(metric);
                    if (metric == "count") { av ??= 0; bv ??= 0; }
                    bool available = av.HasValue && bv.HasValue && (!metric.StartsWith("gpu", StringComparison.Ordinal) || gpuComparable);
                    double difference = available ? bv.Value - av.Value : 0;
                    delta[metric] = Optional(available, difference);
                    percent[metric] = Optional(available && av != 0, available && av != 0 ? difference / av.Value * 100d : 0);
                }
                row["delta"] = delta; row["percent"] = percent; rows.Add(row);
            }
            rows = rows.OrderByDescending(r => r["delta"].Value<decimal?>(sort) is decimal difference ? Math.Abs(difference) : -1m)
                .ThenBy(r => r.Value<string>("phase"), StringComparer.Ordinal)
                .ThenBy(r => r.Value<string>("documentId"), StringComparer.Ordinal)
                .ThenBy(r => r.Value<string>("layerId"), StringComparer.Ordinal).ThenBy(r => r.Value<int>("fxIndex")).ToList();
            var warnings = new List<string>();
            warnings.AddRange(ProfileWarnings(baseline, before).Select(w => "Baseline: " + w));
            warnings.AddRange(ProfileWarnings(current, after).Select(w => "Current: " + w));
            if (baseline.id == current.id) warnings.Add("Baseline and current refer to the same capture.");
            if (baseline.documentId != current.documentId) warnings.Add("Capture document filters differ.");
            if (!before.documents.SetEquals(after.documents)) warnings.Add("Recorded document identities differ; rows are not matched by display names.");
            if (baseline.unityVersion != current.unityVersion) warnings.Add("Recorded Unity versions differ or version metadata is missing.");
            if (rows.Any(r => (bool)r["countChanged"])) warnings.Add("Call counts differ: total-time and allocation deltas mix workload and per-call cost; inspect call counts and meanMs as well.");
            if (rows.Any(r => (string)r["status"] != "matched")) warnings.Add("Added/removed stages have no comparable timing or allocation delta; absence is not zero cost.");
            if (rows.Any(r => (string)r["status"] == "matched" && !(bool)r["allocationComparable"]))
                warnings.Add("Allocation deltas require measured counts for every eligible call in both captures; partial sums remain visible, but their deltas are unavailable.");
            if (rows.Any(r => (string)r["status"] == "matched" && r.Value<int>("fxIndex") >= 0 &&
                r["before"].Value<string>("fxName") != r["after"].Value<string>("fxName")))
                warnings.Add("FX names changed in matched slots; verify that these are still equivalent effects, not replacements or reordered slots.");
            warnings.Add("Observational comparison, not a controlled benchmark. Match input, quality, cache state and hardware. Inclusive stages and CPU/GPU overlap; do not sum rows.");
            return new JObject { ["rows"] = new JArray(rows.Skip(offset).Take(limit)), ["totalRows"] = rows.Count, ["offset"] = offset,
                ["nextOffset"] = (long)offset + limit < rows.Count ? new JValue(offset + limit) : JValue.CreateNull(),
                ["before"] = before.Totals(), ["after"] = after.Totals(), ["warnings"] = new JArray(warnings) };
        }
    }
}
