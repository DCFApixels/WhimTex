using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        public static string ProfileFile(string requestPath) => Respond(() => Profile(Parse(ReadRequestFile(requestPath))));
        public static string ProfileJson(string requestJson) => Respond(() => Profile(Parse(requestJson)));

        private static JObject Profile(JObject request)
        {
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            string action = Text(request, "action");
            if (action?.StartsWith("benchmark", StringComparison.Ordinal) == true) return ProfileBenchmark(request, action);
            if (action == "explain") return ProfileExplain(request);
            if (action == "baseline" || action == "clearBaseline" || action == "compare" || action == "summary")
                return ProfileAnalysis(request, action);
            switch (action)
            {
                case "start": Keys(request, "apiVersion", "action", "maxEvents", "durationSeconds", "documentId"); break;
                case "read": Keys(request, "apiVersion", "action", "captureId", "offset", "limit", "operationId", "layerId", "layer", "phase", "documentId", "tree", "collapsedIds", "groups"); break;
                case "export": Keys(request, "apiVersion", "action", "captureId", "outputPath"); break;
                case "status": case "stop": case "clear": Keys(request, "apiVersion", "action", "captureId"); break;
                default: throw new WhimTexApiException("invalid_request", "action must be start, stop, status, read, clear, export, baseline, clearBaseline, summary, compare, explain, benchmarkStart, benchmarkStatus, benchmarkCancel or benchmarkRead.");
            }
            WhimTexProfiler.Capture capture = WhimTexProfiler.Current;
            string id = Text(request, "captureId");
            if (id != null || action == "stop" || action == "clear" || action == "export")
                Require(capture != null && id == capture.id, "The capture ID is missing or no longer current. Read status first.", "capture_conflict");
            if (action == "start")
            {
                Require(capture == null || !capture.recording && capture.open == 0, "A capture is already active.", "capture_active");
                capture = WhimTexProfiler.Start(Int(request, "maxEvents", 32768, 256, 262144),
                    Number(request, "durationSeconds", 120, 1, 600), ProfileDocumentId(request));
            }
            if (action == "stop") WhimTexProfiler.Stop();
            if (action == "clear")
            {
                Require(!capture.recording && capture.open == 0, "Stop recording and wait for active scopes before clearing.", "capture_active");
                WhimTexProfiler.Clear(); capture = null;
            }
            JObject result = Success();
            result["capture"] = CaptureInfo(capture);
            result["baseline"] = CaptureInfo(WhimTexProfiler.Baseline);
            result["timing"] = "durationMs/selfMs are main-thread wall elapsed, including waits. FX cpuPrepareMs and cpuSubmitMs separate preparation from command submission. gpuMs is an asynchronous GPU marker interval, not CPU time; null unless gpuStatus is ready. CPU and GPU overlap: do not add them.";
            result["lifetime"] = "Automatically retained across script reload within this Editor session; active recording stops at reload. Export before closing the Editor. No document edits, forced renders or cache clears.";
            if (action == "read")
            {
                Require(capture != null, "No capture exists.", "capture_missing");
                int offset = Int(request, "offset", 0, 0, int.MaxValue);
                int limit = Int(request, "limit", 200, 1, 2000);
                int operation = Int(request, "operationId", 0, 0, int.MaxValue);
                string document = ProfileDocumentId(request);
                string layer = Text(request, "layerId"), phase = Text(request, "phase");
                bool tree = Bool(request, "tree");
                var collapsed = new HashSet<int>();
                if (request["collapsedIds"] != null)
                {
                    Require(tree, "collapsedIds requires tree:true.");
                    Require(request["collapsedIds"] is JArray ids && ids.Count <= capture.count, "collapsedIds must be a bounded array of event IDs.");
                    foreach (var token in (JArray)request["collapsedIds"])
                    {
                        Require(token.Type == JTokenType.Integer, "collapsedIds must contain integers.");
                        long eventId = (long)token;
                        Require(eventId > 0 && eventId <= capture.count, "collapsedIds contains an unknown event ID.");
                        collapsed.Add((int)eventId);
                    }
                }
                ReadCapture(capture, result, offset, limit, operation, document, layer, phase, Text(request, "layer"), tree, collapsed, ProfileGroups(request));
            }
            if (action == "export")
            {
                Require(!capture.recording && capture.open == 0, "Stop recording before export.", "capture_active");
                Require(WhimTexProfiler.PendingGpu(capture) == 0, "GPU timings are still pending. Refresh status after a few Editor frames.", "capture_gpu_pending");
                string path = Text(request, "outputPath");
                Require(!string.IsNullOrEmpty(path), "outputPath is required.");
                path = path.Replace('\\', '/');
                Require(path.StartsWith("Temp/WhimTex/", StringComparison.Ordinal) &&
                    string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase),
                    "Export must be project-relative Temp/WhimTex/*.json.", "invalid_path");
                ValidateSegments(path);
                string full = FullPath(path); RejectLinks(full);
                Require(!File.Exists(full), "Choose a new output file; reports are never overwritten.", "already_exists");
                ReadCapture(capture, result, 0, capture.count, 0, null, null, null);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                using (var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream))
                using (var json = new JsonTextWriter(writer)) result.WriteTo(json);
                result = Success(); result["capture"] = CaptureInfo(capture); result["baseline"] = CaptureInfo(WhimTexProfiler.Baseline); result["outputPath"] = full;
            }
            return result;
        }

        private static string ProfileDocumentId(JObject request)
        {
            if (request["documentId"]?.Type == JTokenType.Null) return null;
            string id = Text(request, "documentId");
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        private static JToken CaptureInfo(WhimTexProfiler.Capture capture)
        {
            if (capture == null) return JValue.CreateNull();
            long end = capture.recording ? Stopwatch.GetTimestamp() : capture.end;
            return new JObject {
                ["captureId"] = capture.id, ["recording"] = capture.recording, ["stopReason"] = capture.stopReason,
                ["capacity"] = capture.events.Length, ["eventCount"] = capture.count, ["droppedEvents"] = capture.dropped,
                ["elapsedMs"] = WhimTexProfiler.Milliseconds(end - capture.start), ["openSpans"] = capture.open,
                ["pendingGpuSamples"] = WhimTexProfiler.PendingGpu(capture),
                ["gpuAvailability"] = capture.recording ? WhimTexProfiler.GpuAvailability : "stopped",
                ["documentId"] = capture.documentId,
                ["truncated"] = capture.stopReason == "event_limit" || capture.stopReason == "document_limit",
                ["unityVersion"] = capture.unityVersion ?? UnityEngine.Application.unityVersion,
                ["allocationCountUnit"] = "allocations", ["allocationCountScope"] = "main_thread",
                ["memory"] = ProfileMemory(capture), ["causeTrackingLimited"] = capture.causeTrackingLimited
            };
        }

        [Flags]
        private enum ProfileNode : byte { Match = 1, Included = 2, Children = 4, Collapsed = 8, Hidden = 16 }

        private static WhimTexProfiler.Group ProfileGroups(JObject request)
        {
            if (request["groups"] == null) return WhimTexProfiler.Group.All;
            Require(request["groups"] is JArray array && array.Count <= WhimTexProfiler.Groups.Length,
                "groups must be an array of at most six group IDs; omit for all, use [] for none.");
            var selected = WhimTexProfiler.Group.None;
            foreach (var token in (JArray)request["groups"])
            {
                Require(token.Type == JTokenType.String, "groups must contain string IDs.");
                var found = WhimTexProfiler.Group.None;
                foreach (var group in WhimTexProfiler.Groups)
                    if (WhimTexProfiler.GroupId(group) == (string)token) found = group;
                Require(found != WhimTexProfiler.Group.None, "Unknown profiler group: " + token);
                selected |= found;
            }
            return selected;
        }

        private static bool ProfileMatches(WhimTexProfiler.Sample sample, long operation, string document,
            string layer, string phase, string layerSearch) =>
            (operation == 0 || sample.operationId == operation) && (document == null || sample.documentId == document) &&
            (layer == null || sample.layerId == layer) && (phase == null || sample.phase.IndexOf(phase, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (string.IsNullOrEmpty(layerSearch) || sample.layerId == layerSearch ||
                sample.layerName != null && sample.layerName.IndexOf(layerSearch, StringComparison.OrdinalIgnoreCase) >= 0);

        private static void ReadCapture(WhimTexProfiler.Capture capture, JObject result, int offset, int limit,
            long operation, string document, string layer, string phase, string layerSearch = null,
            bool tree = false, HashSet<int> collapsed = null, WhimTexProfiler.Group selectedGroups = WhimTexProfiler.Group.All)
        {
            var events = new JArray();
            var groups = new Dictionary<(string phase, string kind, string document, string layer, int fx, string name), JObject>();
            var groupCounts = new int[WhimTexProfiler.Groups.Length];
            var matches = new bool[capture.count];
            for (int i = 0; i < capture.count; i++)
            {
                var sample = capture.events[i];
                if (!ProfileMatches(sample, operation, document, layer, phase, layerSearch)) continue;
                var group = WhimTexProfiler.GroupFor(sample.phase, sample.kind);
                groupCounts[Array.IndexOf(WhimTexProfiler.Groups, group)]++;
                matches[i] = (selectedGroups & group) != 0;
            }
            int matched = 0, visible = 0, totalVisible = 0;
            ProfileNode[] nodes = null;
            if (tree)
            {
                nodes = new ProfileNode[capture.count];
                for (int i = 0; i < capture.count; i++)
                {
                    if (matches[i])
                        nodes[i] = ProfileNode.Match | ProfileNode.Included;
                    if (collapsed?.Contains(i + 1) == true) nodes[i] |= ProfileNode.Collapsed;
                }
                for (int i = capture.count - 1; i >= 0; i--)
                {
                    int parent = (int)capture.events[i].parentId - 1;
                    if (parent >= 0 && (nodes[i] & ProfileNode.Included) != 0)
                        nodes[parent] |= ProfileNode.Included | ProfileNode.Children;
                }
                for (int i = 0; i < capture.count; i++)
                {
                    int parent = (int)capture.events[i].parentId - 1;
                    if (parent >= 0 && (nodes[parent] & (ProfileNode.Hidden | ProfileNode.Collapsed)) != 0)
                        nodes[i] |= ProfileNode.Hidden;
                    if ((nodes[i] & (ProfileNode.Included | ProfileNode.Hidden)) == ProfileNode.Included) totalVisible++;
                }
                if (offset >= totalVisible) offset = totalVisible == 0 ? 0 : (totalVisible - 1) / limit * limit;
            }
            double rootMs = 0;
            for (int i = 0; i < capture.count; i++)
            {
                WhimTexProfiler.Sample sample = capture.events[i];
                bool isMatch = matches[i];
                bool isVisible = tree ? (nodes[i] & (ProfileNode.Included | ProfileNode.Hidden)) == ProfileNode.Included : isMatch;
                if (!isMatch && !isVisible) continue;
                double duration = sample.completed ? WhimTexProfiler.Milliseconds(sample.end - sample.start) : 0;
                double self = sample.kind == "span" ? Math.Max(0, duration - WhimTexProfiler.Milliseconds(sample.childTicks)) : 0;
                if (isMatch && sample.completed && sample.kind == "span" && sample.parentId == 0) rootMs += duration;
                if (isVisible && visible >= offset && visible - offset < limit)
                {
                    // IDs are sequential slots. Use the full capture, not the filtered page,
                    // so hierarchy and bar scale remain stable with missing ancestors.
                    var root = capture.events[(int)sample.operationId - 1];
                    events.Add(new JObject {
                        ["id"] = sample.id, ["parentId"] = sample.parentId, ["operationId"] = sample.operationId,
                        ["changeId"] = sample.changeId, ["documentId"] = sample.documentId, ["layerId"] = sample.layerId,
                        ["changeEventId"] = sample.changeEventId, ["changeReason"] = sample.changeReason,
                        ["changeOrigin"] = sample.changeOrigin, ["changedLayerId"] = sample.changedLayerId,
                        ["changedLayerName"] = sample.changedLayerName, ["resourceKey"] = sample.resourceKey,
                        ["causeEventId"] = sample.causeEventId, ["causeLayerId"] = sample.causeLayerId,
                        ["causeEventIds"] = sample.causeEventIds == null ? new JArray() : new JArray(sample.causeEventIds),
                        ["causeLayerName"] = sample.causeLayerName, ["causeProperty"] = sample.causeProperty, ["causeKind"] = sample.causeKind,
                        ["gcAllocationCount"] = ProfileAllocationMeasured(sample) ? new JValue(sample.gcAllocationCount) : JValue.CreateNull(),
                        ["gcSelfAllocationCount"] = ProfileAllocationMeasured(sample) ? new JValue(Math.Max(0, sample.gcAllocationCount - sample.gcChildAllocationCount)) : JValue.CreateNull(),
                        ["layerName"] = sample.layerName, ["depth"] = sample.depth,
                        ["hasChildren"] = tree && (nodes[i] & ProfileNode.Children) != 0,
                        ["collapsed"] = tree && (nodes[i] & ProfileNode.Collapsed) != 0,
                        ["contextOnly"] = !isMatch,
                        ["operationMs"] = root.completed && root.kind == "span" ?
                            new JValue(WhimTexProfiler.Milliseconds(root.end - root.start)) : JValue.CreateNull(),
                        ["fxIndex"] = sample.fxIndex, ["phase"] = sample.phase, ["kind"] = sample.kind,
                        ["fxName"] = sample.fxName,
                        ["cpuPrepareMs"] = sample.fxTiming && sample.completed ? new JValue(WhimTexProfiler.Milliseconds(sample.cpuPrepareTicks)) : JValue.CreateNull(),
                        ["cpuSubmitMs"] = sample.fxTiming && sample.completed ? new JValue(WhimTexProfiler.Milliseconds(sample.cpuSubmitTicks)) : JValue.CreateNull(),
                        ["gpuMs"] = sample.gpuStatus == "ready" ? new JValue(sample.gpuNanoseconds / 1000000.0) : JValue.CreateNull(),
                        ["gpuStatus"] = sample.gpuStatus ?? (sample.fxTiming ? "not_submitted" : "not_measured"),
                        ["gpuDetail"] = sample.gpuDetail,
                        ["group"] = WhimTexProfiler.GroupId(WhimTexProfiler.GroupFor(sample.phase, sample.kind)),
                        ["detail"] = sample.detail, ["startedMs"] = WhimTexProfiler.Milliseconds(sample.start - capture.start),
                        ["durationMs"] = sample.completed ? new JValue(duration) : JValue.CreateNull(),
                        ["selfMs"] = sample.completed && sample.kind == "span" ? new JValue(self) : JValue.CreateNull(),
                        ["completed"] = sample.completed, ["interrupted"] = sample.interrupted, ["value"] = sample.value
                    });
                }
                if (isVisible) visible++;
                if (!isMatch) continue;
                matched++;
                bool fxStage = sample.phase.StartsWith("fx.", StringComparison.Ordinal);
                var key = (sample.phase, sample.kind, fxStage ? sample.documentId : null,
                    fxStage ? sample.layerId : null, fxStage ? sample.fxIndex : -1, fxStage ? sample.fxName : null);
                if (!groups.TryGetValue(key, out JObject group))
                {
                    groups.Add(key, group = new JObject { ["phase"] = sample.phase, ["kind"] = sample.kind,
                        ["group"] = WhimTexProfiler.GroupId(WhimTexProfiler.GroupFor(sample.phase, sample.kind)),
                        ["documentId"] = key.Item3, ["layerId"] = key.Item4, ["layerName"] = fxStage ? sample.layerName : null,
                        ["fxIndex"] = key.Item5, ["fxName"] = key.Item6,
                        ["cpuPrepareMs"] = JValue.CreateNull(), ["cpuSubmitMs"] = JValue.CreateNull(),
                        ["gpuTotalMs"] = JValue.CreateNull(), ["gpuMaxMs"] = JValue.CreateNull(),
                        ["gpuCount"] = 0, ["gpuPendingCount"] = 0, ["gpuUnavailableCount"] = 0,
                        ["count"] = 0, ["completedCount"] = 0, ["totalMs"] = 0d, ["selfMs"] = 0d, ["maxMs"] = 0d });
                    ProfileAllocationMetrics(group, 0, 0, 0, 0);
                }
                group["count"] = (int)group["count"] + 1;
                if (sample.kind == "span")
                {
                    bool measured = ProfileAllocationMeasured(sample);
                    ProfileAllocationMetrics(group,
                        (group.Value<long?>("gcAllocationCount") ?? 0) + (measured ? sample.gcAllocationCount : 0),
                        (group.Value<long?>("gcSelfAllocationCount") ?? 0) + (measured ? Math.Max(0, sample.gcAllocationCount - sample.gcChildAllocationCount) : 0),
                        (int)group["measuredCalls"] + (measured ? 1 : 0), (int)group["eligibleCalls"] + 1);
                }
                if (sample.gpuStatus == "ready")
                {
                    double gpuMs = sample.gpuNanoseconds / 1000000.0;
                    group["gpuCount"] = (int)group["gpuCount"] + 1;
                    group["gpuTotalMs"] = (group.Value<double?>("gpuTotalMs") ?? 0) + gpuMs;
                    group["gpuMaxMs"] = Math.Max(group.Value<double?>("gpuMaxMs") ?? 0, gpuMs);
                }
                else if (sample.gpuStatus == "pending") group["gpuPendingCount"] = (int)group["gpuPendingCount"] + 1;
                else if (sample.fxTiming || sample.gpuStatus != null) group["gpuUnavailableCount"] = (int)group["gpuUnavailableCount"] + 1;
                if (!sample.completed) continue;
                if (sample.fxTiming)
                {
                    group["cpuPrepareMs"] = (group.Value<double?>("cpuPrepareMs") ?? 0) + WhimTexProfiler.Milliseconds(sample.cpuPrepareTicks);
                    group["cpuSubmitMs"] = (group.Value<double?>("cpuSubmitMs") ?? 0) + WhimTexProfiler.Milliseconds(sample.cpuSubmitTicks);
                }
                group["completedCount"] = (int)group["completedCount"] + 1;
                group["totalMs"] = (double)group["totalMs"] + duration;
                group["selfMs"] = (double)group["selfMs"] + self;
                group["maxMs"] = Math.Max((double)group["maxMs"], duration);
            }
            result["events"] = events; result["summary"] = new JArray(groups.Values);
            var catalog = new JArray();
            for (int i = 0; i < WhimTexProfiler.Groups.Length; i++)
            {
                var group = WhimTexProfiler.Groups[i];
                catalog.Add(new JObject { ["id"] = WhimTexProfiler.GroupId(group), ["name"] = WhimTexProfiler.GroupName(group),
                    ["count"] = groupCounts[i], ["selected"] = (selectedGroups & group) != 0 });
            }
            result["groups"] = catalog;
            result["totalMatched"] = matched; result["offset"] = offset;
            result["totalVisible"] = visible;
            result["nextOffset"] = (long)offset + events.Count < visible ? new JValue(offset + events.Count) : JValue.CreateNull();
            result["rootWallMs"] = rootMs;
            result["accounting"] = "Only root spans contribute to rootWallMs. Summary inclusive times overlap: do not sum rows. Stage managed allocations are main-thread counts, inclusive/self, including instrumentation; no stage byte amounts. Aggregates sum valid measured calls only: inspect measuredCalls, eligibleCalls and partial; eligibleCalls includes open/interrupted spans. Nested inclusive counts overlap. FX rows group by document/layer/index/name. GPU block intervals include input renders and may include GPU idle gaps; individual FX GPU intervals cover its blit and FinishStage, not input preparation. GPU aggregates include ready samples only: inspect gpuCount/pending/unavailable counts. CPU preparation includes dependency renders/waits, not pure worker CPU consumption. Open spans have null CPU timing. changeId links renders to the latest model change, not a completed UI gesture or GPU presentation.";
        }
    }
}
