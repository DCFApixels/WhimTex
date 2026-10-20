using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        static JToken ProfileMemory(WhimTexProfiler.Capture capture)
        {
            WhimTexProfiler.SampleEditorAllocations(capture);
            var m = capture?.memory;
            if (m == null) return JValue.CreateNull();
            return new JObject {
                ["editorManagedAllocatedBytes"] = m.editorManagedAllocationAvailable && !m.editorManagedAllocationLimited ? new JValue(m.editorManagedAllocatedBytes) : JValue.CreateNull(),
                ["editorManagedAllocationAvailable"] = m.editorManagedAllocationAvailable,
                ["editorManagedAllocationLimited"] = m.editorManagedAllocationLimited,
                ["editorManagedAllocationScope"] = "all_editor_threads",
                ["editorManagedAllocationUnit"] = "bytes",
                ["allocationCountLimited"] = m.allocationCountLimited,
                ["trackedTextureBytes"] = m.trackedTextureBytes, ["peakTrackedTextureBytes"] = m.peakTrackedTextureBytes,
                ["acquiredBytes"] = m.acquiredBytes, ["releasedBytes"] = m.releasedBytes, ["observedBytes"] = m.observedBytes,
                ["acquireCount"] = m.acquireCount, ["releaseCount"] = m.releaseCount,
                ["processUsedBytes"] = m.processUsedBytes, ["processReservedBytes"] = m.processReservedBytes,
                ["peakProcessUsedBytes"] = m.peakProcessUsedBytes,
                ["nativeCacheBytes"] = m.nativeCacheBytes, ["peakNativeCacheBytes"] = m.peakNativeCacheBytes,
                ["paintBuffers"] = new JObject {
                    ["nativeActiveBytes"] = m.paintNativeActiveBytes, ["nativeIdleBytes"] = m.paintNativeIdleBytes,
                    ["writerPendingBytes"] = m.paintWriterPendingBytes, ["gpuStagingBytes"] = m.paintGpuStagingBytes,
                    ["atlasActiveBytes"] = m.paintAtlasActiveBytes, ["atlasPooledBytes"] = m.paintAtlasPooledBytes,
                    ["peakNativeActiveBytes"] = m.peakPaintNativeActiveBytes, ["peakGpuPayloadBytes"] = m.peakPaintGpuPayloadBytes,
                    ["scope"] = "all_whimtex_documents", ["sampleIntervalMs"] = 100,
                    ["accounting"] = "Sampled existing pools, including allocations before recording. Writer pending bytes overlap native active bytes; GPU payload overlaps tracked textures. Do not sum these views. Payload is not physical VRAM or OS resident memory; peaks may miss short-lived allocations."
                },
                ["trackingLimited"] = m.trackingLimited,
                ["coverage"] = "Managed allocated bytes cover the capture interval across all Editor threads, including unrelated Editor work and instrumentation. Neither document nor display filters limit this total; it is allocation traffic, not live heap or per-stage bytes. Bounded Unity ProfilerRecorder overflow makes the affected measurement unavailable, not zero. Stage allocation counts cover main-thread scopes, inclusive/self, including instrumentation; inspect measuredCalls, eligibleCalls and partial on aggregates. Texture payload estimates cover observed WhimTex RT checkouts, effect caches, drawing surfaces and field uploads; excludes driver overhead, unobserved pre-existing resources and the Unity pool after return. Native cache payload separate. Resource totals describe the whole retained capture, not display filters. Process figures are sampled Unity-wide allocation/reservation, not document memory or OS resident size."
            };
        }

        private static bool ProfileAllocationMeasured(WhimTexProfiler.Sample sample) =>
            sample.kind == "span" && sample.completed && !sample.interrupted && sample.allocationCountMeasured;

        private static void ProfileAllocationMetrics(JObject row, long inclusive, long self, int measuredCalls, int eligibleCalls)
        {
            row["gcAllocationCount"] = measuredCalls > 0 ? new JValue(inclusive) : JValue.CreateNull();
            row["gcSelfAllocationCount"] = measuredCalls > 0 ? new JValue(self) : JValue.CreateNull();
            row["measuredCalls"] = measuredCalls;
            row["eligibleCalls"] = eligibleCalls;
            row["partial"] = measuredCalls < eligibleCalls;
        }

        static JArray ProfileCauseSummary(WhimTexProfiler.Capture source, ProfileAnalysisData data, int top)
        {
            var result = new JArray();
            for (int i = source.count - 1; i >= 0 && result.Count < top; i--)
            {
                var s = source.events[i];
                if (s.phase != "cache.miss" || s.causeEventId == 0) continue;
                if (!data.matchedEventIds.Contains(s.id)) continue;
                result.Add(new JObject { ["eventId"] = s.id, ["causeEventId"] = s.causeEventId,
                    ["layerId"] = s.layerId, ["layerName"] = s.layerName,
                    ["sourceLayerId"] = s.causeLayerId, ["sourceLayerName"] = s.causeLayerName,
                    ["property"] = s.causeProperty, ["kind"] = s.causeKind });
            }
            return result;
        }

        static JObject ProfileExplain(JObject request)
        {
            Keys(request, "apiVersion", "action", "captureId", "eventId", "limit");
            var capture = WhimTexProfiler.Current;
            Require(capture != null && Text(request, "captureId") == capture.id, "Read status and supply the current captureId.", "capture_conflict");
            Require(request["eventId"] != null && capture.count > 0, "eventId is required and must identify a retained event.");
            int id = Int(request, "eventId", 0, 1, capture.count), limit = Int(request, "limit", 64, 1, 256);
            var pending = new Queue<long>(); var visited = new HashSet<long>(); var nodes = new JArray();
            pending.Enqueue(id);
            while (pending.Count > 0 && nodes.Count < limit)
            {
                long next = pending.Dequeue();
                if (next < 1 || next > capture.count || !visited.Add(next)) continue;
                var s = capture.events[(int)next - 1];
                var links = s.causeEventIds ?? (s.causeEventId > 0 ? new[] { s.causeEventId } : Array.Empty<long>());
                nodes.Add(new JObject { ["eventId"] = s.id, ["phase"] = s.phase, ["layerId"] = s.layerId,
                    ["layerName"] = s.layerName, ["property"] = s.causeProperty, ["kind"] = s.causeKind,
                    ["detail"] = s.detail, ["causes"] = new JArray(links) });
                foreach (long link in links) if (link < next) pending.Enqueue(link);
            }
            var result = Success(); result["capture"] = CaptureInfo(capture); result["nodes"] = nodes;
            result["truncated"] = pending.Count != 0;
            result["trackingLimited"] = capture.causeTrackingLimited;
            result["evidence"] = "Observed property differences and render-cache dependency edges, not a complete causal proof. First observation has no before state. Up to 32 changes/edges per layer, 1024 layers and 2 Mi characters of input snapshots (not a heap-byte limit). Untracked edits or external dependencies remain unattributed.";
            return result;
        }
    }
}
