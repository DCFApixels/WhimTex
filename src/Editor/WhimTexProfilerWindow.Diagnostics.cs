using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class WhimTexProfilerWindow
    {
        [SerializeField] private bool diagnosticsExpanded;
        private Foldout diagnosticsPanel;
        private ScrollView diagnosticsScroll;
        private Label memoryManaged;
        private Label memoryOverview;
        private Label memoryTraffic;
        private Label memoryNative;
        private Label memoryProcess;
        private Label memoryCoverage;
        private Label causeCoverage;
        private Label detailDiagnostics;
        private Button explainCause;
        private ScrollView causeScroll;
        private Label causeChain;
        private Label causeEvidence;
        [SerializeField] private string benchmarkMode = "both";
        [SerializeField] private int benchmarkRepetitions = 7;
        [SerializeField] private int benchmarkWarmups = 1;
        [SerializeField] private int benchmarkMaxSize = 512;
        private string benchmarkId;
        private bool benchmarkRunning;
        private bool benchmarkPollingFailed;
        private double nextBenchmarkPoll;
        private JObject benchmarkResponse;
        private VisualElement benchmarkSettings;
        private Button benchmarkRun;
        private Button benchmarkCancel;
        private Label benchmarkValidation;
        private Label benchmarkStatus;
        private Label benchmarkCold;
        private Label benchmarkWarm;
        private Label benchmarkMetadata;
        private Label benchmarkError;
        private Label benchmarkMethod;

        private void CreateDiagnosticsPanel(VisualElement parent)
        {
            diagnosticsPanel = new Foldout { name = "diagnostics-panel", text = "Diagnostics & benchmarks", value = diagnosticsExpanded };
            diagnosticsPanel.AddToClassList("whimtex-profiler-diagnostics");
            diagnosticsPanel.RegisterValueChangedCallback(e => { if (e.target == diagnosticsPanel) diagnosticsExpanded = e.newValue; });
            parent.Add(diagnosticsPanel);
            diagnosticsScroll = new ScrollView(ScrollViewMode.Vertical) { name = "diagnostics-scroll", viewDataKey = "diagnostics-scroll" };
            diagnosticsScroll.AddToClassList("whimtex-profiler-diagnostics-scroll");
            diagnosticsPanel.Add(diagnosticsScroll);
            var title = DiagnosticLabel(diagnosticsScroll, "memory-title", "Memory · whole capture");
            title.AddToClassList("whimtex-profiler-diagnostics-title");
            title.tooltip = "These values describe the whole retained current capture. Display filters do not change them. Managed allocated bytes cover all Editor threads, regardless of the capture's document filter.";
            memoryManaged = DiagnosticLabel(diagnosticsScroll, "memory-managed");
            memoryManaged.tooltip = "Total managed bytes allocated during the capture across all Editor threads, including unrelated Editor work and instrumentation. Document and display filters do not limit this total. Allocation traffic, not live heap. An unavailable or limited recorder shows —.";
            memoryOverview = DiagnosticLabel(diagnosticsScroll, "memory-overview");
            memoryOverview.tooltip = "Texture figures estimate observed checkout payload, not driver or pool memory.";
            memoryTraffic = DiagnosticLabel(diagnosticsScroll, "memory-traffic");
            memoryTraffic.tooltip = "First observed bytes belong to pre-existing textures seen during this capture; they are not new acquisitions. Released temporary textures may remain in Unity's pool.";
            memoryNative = DiagnosticLabel(diagnosticsScroll, "memory-native");
            memoryNative.tooltip = "Tracked native cache payload, separate from texture payload estimates.";
            memoryProcess = DiagnosticLabel(diagnosticsScroll, "memory-process");
            memoryProcess.tooltip = "Sampled Unity-wide allocation/reservation, not document memory or OS resident size.";
            memoryCoverage = DiagnosticLabel(diagnosticsScroll, "memory-coverage");
            memoryCoverage.AddToClassList("whimtex-profiler-diagnostics-hint");
            causeCoverage = DiagnosticLabel(diagnosticsScroll, "cause-coverage");
            causeCoverage.AddToClassList("whimtex-profiler-diagnostics-hint");
            ApplyMemory(null);
            CreateBenchmarkControls(diagnosticsScroll);
        }

        private static Label DiagnosticLabel(VisualElement parent, string name, string text = "")
        {
            var label = new Label(text) { name = name, enableRichText = false };
            label.AddToClassList("whimtex-profiler-diagnostics-text");
            parent.Add(label);
            return label;
        }

        private void ApplyMemory(JObject capture)
        {
            if (memoryOverview == null) return;
            var memory = capture?["memory"] as JObject;
            bool limited = memory?.Value<bool?>("trackingLimited") == true;
            bool editorAllocationsLimited = memory?.Value<bool?>("editorManagedAllocationLimited") == true;
            bool allocationCountsLimited = memory?.Value<bool?>("allocationCountLimited") == true;
            bool causesLimited = capture?.Value<bool?>("causeTrackingLimited") == true;
            causeCoverage.text = causesLimited ? "Cause evidence is partial: tracking limit reached." : "";
            causeCoverage.EnableInClassList("whimtex-profiler-hidden", !causesLimited);
            causeCoverage.EnableInClassList("whimtex-profiler-diagnostics-warning", causesLimited);
            memoryManaged.EnableInClassList("whimtex-profiler-diagnostics-warning", editorAllocationsLimited);
            memoryCoverage.EnableInClassList("whimtex-profiler-diagnostics-warning", limited || editorAllocationsLimited || allocationCountsLimited);
            memoryCoverage.tooltip = memory?.Value<string>("coverage") ?? "Memory coverage unavailable for this capture.";
            if (memory == null)
            {
                memoryManaged.text = "";
                memoryOverview.text = capture == null ? "No retained capture." : "Memory data unavailable for this capture.";
                memoryTraffic.text = "";
                memoryNative.text = "";
                memoryProcess.text = "";
                memoryCoverage.text = "Whole capture · filters do not apply · — unavailable";
                return;
            }
            bool editorAllocationsAvailable = memory.Value<bool?>("editorManagedAllocationAvailable") == true && !editorAllocationsLimited;
            memoryManaged.text = "Managed allocated · all Editor threads " +
                (editorAllocationsAvailable ? FormatBytes(memory, "editorManagedAllocatedBytes") : "—") +
                (editorAllocationsLimited ? " · recorder limit reached" : "");
            memoryOverview.text = "Tracked textures " + FormatBytes(memory, "trackedTextureBytes") + " · peak " + FormatBytes(memory, "peakTrackedTextureBytes");
            memoryTraffic.text = "Acquired " + FormatBytes(memory, "acquiredBytes") + " (" + DiagnosticCount(memory, "acquireCount") + ")" +
                " · released " + FormatBytes(memory, "releasedBytes") + " (" + DiagnosticCount(memory, "releaseCount") + ")" +
                " · first observed " + FormatBytes(memory, "observedBytes");
            memoryNative.text = "Native cache payload " + FormatBytes(memory, "nativeCacheBytes") + " · peak " + FormatBytes(memory, "peakNativeCacheBytes");
            memoryProcess.text = "Unity allocated " + FormatBytes(memory, "processUsedBytes") + " · reserved " + FormatBytes(memory, "processReservedBytes") +
                " · peak allocated " + FormatBytes(memory, "peakProcessUsedBytes");
            memoryCoverage.text = "Whole capture · filters do not apply · texture/native payload estimates · — unavailable" +
                (limited ? " · Tracking limit reached" : "") +
                (allocationCountsLimited ? " · Stage allocation counts limited; affected scopes unavailable" : "");
        }

        private static string DiagnosticCount(JObject data, string key)
        {
            long? value = data?.Value<long?>(key);
            return value.HasValue && value.Value >= 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "—";
        }

        private static string FormatBytes(JObject data, string key)
        {
            double? bytes = data?.Value<double?>(key);
            if (!bytes.HasValue || double.IsNaN(bytes.Value) || double.IsInfinity(bytes.Value) || bytes.Value < 0) return "—";
            double value = bytes.Value;
            string unit = "B";
            if (value >= 1073741824) { value /= 1073741824; unit = "GiB"; }
            else if (value >= 1048576) { value /= 1048576; unit = "MiB"; }
            else if (value >= 1024) { value /= 1024; unit = "KiB"; }
            return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
        }

        private void ApplyEventDiagnostics(JObject item)
        {
            if (detailDiagnostics == null) return;
            ResetCauseExplanation(item);
            detailDiagnostics.EnableInClassList("whimtex-profiler-hidden", item == null);
            detailDiagnostics.text = item == null ? "" : EventDiagnosticsText(item);
            detailDiagnostics.tooltip = item == null ? "" :
                "Main-thread managed allocation counts in this completed scope, inclusive and self, including instrumentation. Counts are not bytes; nested inclusive counts overlap. Unavailable or limited scopes show —.\n" +
                "Cause fields describe the recorded link; missing attribution is not proof that no dependency exists.\n" +
                "Cause layer ID: " + (item.Value<string>("causeLayerId") ?? "—");
        }

        private static string EventDiagnosticsText(JObject item)
        {
            string text = "Managed allocation count · main thread · inclusive " + DiagnosticCount(item, "gcAllocationCount") +
                " · self " + DiagnosticCount(item, "gcSelfAllocationCount");
            long? cause = item.Value<long?>("causeEventId");
            string layer = item.Value<string>("causeLayerName");
            string property = item.Value<string>("causeProperty");
            string kind = item.Value<string>("causeKind");
            bool hasCause = cause > 0 || !string.IsNullOrEmpty(layer) || !string.IsNullOrEmpty(item.Value<string>("causeLayerId")) ||
                !string.IsNullOrEmpty(property) || !string.IsNullOrEmpty(kind);
            if (!hasCause) return text + "\nRecorded cause: unavailable";
            text += "\nRecorded cause: " + (string.IsNullOrEmpty(kind) ? "link" : kind);
            if (!string.IsNullOrEmpty(layer)) text += " · " + layer;
            else if (!string.IsNullOrEmpty(item.Value<string>("causeLayerId"))) text += " · Unnamed layer";
            if (!string.IsNullOrEmpty(property)) text += " · " + property;
            if (cause > 0) text += " · event " + cause.Value.ToString(CultureInfo.InvariantCulture);
            return text;
        }

        private void CreateCauseControls(VisualElement parent)
        {
            var actions = Row(parent);
            explainCause = AddButton(actions, "Explain cause", ExplainCause);
            explainCause.name = "explain-cause";
            explainCause.tooltip = "Read up to 64 recorded cause nodes for this event. This does not change the capture or filters.";
            causeEvidence = DiagnosticLabel(parent, "cause-evidence");
            causeEvidence.AddToClassList("whimtex-profiler-diagnostics-hint");
            causeScroll = new ScrollView(ScrollViewMode.Vertical) { name = "cause-scroll" };
            causeScroll.AddToClassList("whimtex-profiler-cause-scroll");
            parent.Add(causeScroll);
            causeChain = DiagnosticLabel(causeScroll, "cause-chain");
            ResetCauseExplanation(null);
        }

        private void ResetCauseExplanation(JObject item)
        {
            explainCause?.SetEnabled(!string.IsNullOrEmpty(captureId) && item?.Value<long?>("id") > 0);
            if (causeChain == null) return;
            causeChain.text = "";
            causeEvidence.text = "";
            causeEvidence.tooltip = "";
            causeEvidence.RemoveFromClassList("whimtex-profiler-diagnostics-warning");
            causeScroll.AddToClassList("whimtex-profiler-hidden");
            causeEvidence.AddToClassList("whimtex-profiler-hidden");
        }

        private JObject CauseRequest()
        {
            var request = Request("explain", true);
            request["eventId"] = selectedEvent?.Value<long?>("id");
            request["limit"] = 64;
            return request;
        }

        private void ExplainCause()
        {
            if (string.IsNullOrEmpty(captureId) || !(selectedEvent?.Value<long?>("id") > 0)) return;
            if (Call(CauseRequest(), out var response)) ApplyCauseExplanation(response);
        }

        private void ApplyCauseExplanation(JObject response)
        {
            var text = new StringBuilder();
            int count = 0;
            bool linked = false;
            if (response?["nodes"] is JArray nodes)
                foreach (var node in nodes)
                {
                    if (!(node is JObject item)) continue;
                    if (count++ >= 64) break;
                    if (text.Length > 0) text.Append('\n');
                    text.Append('#').Append(DiagnosticCount(item, "eventId")).Append(' ').Append(item.Value<string>("phase") ?? "Event");
                    foreach (string key in new[] { "layerName", "property", "kind" })
                    {
                        string value = item.Value<string>(key);
                        if (!string.IsNullOrEmpty(value)) text.Append(" · ").Append(value);
                    }
                    if (item["causes"] is JArray causes && causes.Count > 0)
                    {
                        linked = true;
                        text.Append(" ← ");
                        for (int i = 0; i < causes.Count; i++)
                        {
                            if (i != 0) text.Append(", ");
                            text.Append('#').Append(causes[i].ToString());
                        }
                    }
                }
            causeChain.text = text.Length == 0 ? "No recorded cause nodes." : text.ToString();
            bool truncated = response?.Value<bool?>("truncated") == true || count > 64;
            bool trackingLimited = response?.Value<bool?>("trackingLimited") == true;
            causeEvidence.text = "Observed links only; not a complete causal proof" + (linked ? "." : " · no linked cause recorded.") +
                (truncated ? " Node limit reached; result truncated." : "") +
                (trackingLimited ? " Tracking limit reached; evidence is partial." : "");
            causeEvidence.EnableInClassList("whimtex-profiler-diagnostics-warning", truncated || trackingLimited);
            causeEvidence.tooltip = response?.Value<string>("evidence") ?? "Missing attribution does not prove that no dependency exists.";
            causeScroll.RemoveFromClassList("whimtex-profiler-hidden");
            causeEvidence.RemoveFromClassList("whimtex-profiler-hidden");
            causeScroll.scrollOffset = Vector2.zero;
        }

        private void CreateBenchmarkControls(VisualElement parent)
        {
            DiagnosticLabel(parent, "benchmark-title", "Benchmark · separate document snapshot").AddToClassList("whimtex-profiler-diagnostics-title");
            var source = DiagnosticLabel(parent, "benchmark-source", "Source: Document ID in Capture settings. Current capture and baseline are retained.");
            source.AddToClassList("whimtex-profiler-diagnostics-hint");
            benchmarkSettings = Row(parent, true);
            var modes = new List<string> { "Both", "Cold", "Warm" };
            var mode = new DropdownField("Mode", modes, benchmarkMode == "cold" ? 1 : benchmarkMode == "warm" ? 2 : 0) { name = "benchmark-mode" };
            mode.RegisterValueChangedCallback(e => { benchmarkMode = e.newValue.ToLowerInvariant(); UpdateBenchmarkControls(); });
            AddField(benchmarkSettings, mode);
            AddBenchmarkInteger("Repetitions", "benchmark-repetitions", benchmarkRepetitions, "Measured samples per mode: 1–30.", value => benchmarkRepetitions = value);
            AddBenchmarkInteger("Warmups", "benchmark-warmups", benchmarkWarmups, "Preparatory warm renders: 0–5; excluded from statistics. Zero includes first-use work in the warm series.", value => benchmarkWarmups = value);
            AddBenchmarkInteger("Max size", "benchmark-max-size", benchmarkMaxSize, "Maximum render dimension: 1–2048 pixels. Default 512.", value => benchmarkMaxSize = value);
            var actions = Row(parent, true);
            benchmarkRun = AddButton(actions, "Run", RunBenchmark);
            benchmarkRun.name = "benchmark-run";
            benchmarkCancel = AddButton(actions, "Cancel", CancelBenchmark);
            benchmarkCancel.name = "benchmark-cancel";
            benchmarkCancel.tooltip = "Cancel between operations; a synchronous render/readback cannot be interrupted. Keep partial statistics.";
            AddButton(actions, "Refresh benchmark", RefreshBenchmark).name = "benchmark-refresh";
            benchmarkValidation = DiagnosticLabel(parent, "benchmark-validation");
            benchmarkValidation.AddToClassList("whimtex-profiler-diagnostics-hint");
            benchmarkStatus = DiagnosticLabel(parent, "benchmark-status");
            benchmarkCold = DiagnosticLabel(parent, "benchmark-cold");
            benchmarkWarm = DiagnosticLabel(parent, "benchmark-warm");
            benchmarkMetadata = DiagnosticLabel(parent, "benchmark-metadata");
            benchmarkMetadata.AddToClassList("whimtex-profiler-diagnostics-hint");
            benchmarkError = DiagnosticLabel(parent, "benchmark-error");
            benchmarkError.AddToClassList("whimtex-profiler-diagnostics-error");
            var note = DiagnosticLabel(parent, "benchmark-note", "CPU wall including GPU readback, not GPU time. Cold uses fresh document caches; it does not restart Unity or JIT.");
            note.AddToClassList("whimtex-profiler-diagnostics-hint");
            var method = new Foldout { name = "benchmark-method", text = "Method & limitations", value = false };
            parent.Add(method);
            benchmarkMethod = DiagnosticLabel(method, "benchmark-method-text");
            ApplyBenchmark(benchmarkResponse);
        }

        private void AddBenchmarkInteger(string label, string name, int value, string tooltip, Action<int> assign)
        {
            var field = new IntegerField(label) { name = name, value = value, isDelayed = true, tooltip = tooltip };
            field.RegisterValueChangedCallback(e => { assign(e.newValue); UpdateBenchmarkControls(); });
            AddField(benchmarkSettings, field);
        }

        private string BenchmarkValidation()
        {
            if (string.IsNullOrWhiteSpace(documentId)) return "Set a source Document ID in Capture settings before running.";
            if (benchmarkMode != "cold" && benchmarkMode != "warm" && benchmarkMode != "both") return "Choose Cold, Warm or Both.";
            if (benchmarkRepetitions < 1 || benchmarkRepetitions > 30) return "Repetitions must be 1–30.";
            if (benchmarkWarmups < 0 || benchmarkWarmups > 5) return "Warmups must be 0–5.";
            if (benchmarkMaxSize < 1 || benchmarkMaxSize > 2048) return "Max size must be 1–2048 pixels.";
            return "";
        }

        private void UpdateBenchmarkControls()
        {
            if (benchmarkRun == null || benchmarkValidation == null) return;
            string validation = BenchmarkValidation();
            benchmarkRun.SetEnabled(!benchmarkRunning && validation.Length == 0);
            benchmarkRun.tooltip = benchmarkRunning ? "A benchmark is running." : validation.Length > 0 ? validation :
                "Run a separate snapshot. Replaces only the previous benchmark result, never the current capture or baseline.";
            benchmarkCancel.SetEnabled(benchmarkRunning && !string.IsNullOrEmpty(benchmarkId));
            benchmarkSettings.SetEnabled(!benchmarkRunning);
            benchmarkValidation.text = validation;
            benchmarkValidation.EnableInClassList("whimtex-profiler-hidden", validation.Length == 0 || benchmarkRunning);
        }

        private JObject BenchmarkRequest(string action)
        {
            var request = Request(action);
            if (action == "benchmarkStart")
            {
                request["documentId"] = documentId;
                request["mode"] = benchmarkMode;
                request["repetitions"] = benchmarkRepetitions;
                request["warmups"] = benchmarkWarmups;
                request["maxSize"] = benchmarkMaxSize;
            }
            else if (!string.IsNullOrEmpty(benchmarkId)) request["benchmarkId"] = benchmarkId;
            return request;
        }

        private void RunBenchmark()
        {
            if (benchmarkRunning || BenchmarkValidation().Length != 0) { UpdateBenchmarkControls(); return; }
            SendBenchmark(BenchmarkRequest("benchmarkStart"));
        }

        private void CancelBenchmark()
        {
            if (benchmarkRunning && !string.IsNullOrEmpty(benchmarkId)) SendBenchmark(BenchmarkRequest("benchmarkCancel"));
        }

        private void RefreshBenchmark() => SendBenchmark(Request("benchmarkStatus"));

        private void PollBenchmark()
        {
            if (!benchmarkRunning || benchmarkPollingFailed || EditorApplication.timeSinceStartup < nextBenchmarkPoll) return;
            if (!SendBenchmark(BenchmarkRequest("benchmarkStatus"))) benchmarkPollingFailed = true;
        }

        private bool SendBenchmark(JObject request)
        {
            try
            {
                var response = JObject.Parse(WhimTexApi.ProfileJson(request.ToString(Formatting.None)));
                if (response.Value<int?>("apiVersion") != 1) throw new InvalidOperationException("Unsupported profiler API version.");
                if (response.Value<bool?>("success") != true)
                    throw new InvalidOperationException((response.Value<string>("errorCode") ?? "benchmark_failed") + ": " +
                        (response.Value<string>("error") ?? "The benchmark request failed."));
                ApplyBenchmark(response);
                return true;
            }
            catch (Exception error)
            {
                benchmarkError.text = error.Message + " Use Refresh benchmark to check the latest run.";
                benchmarkError.RemoveFromClassList("whimtex-profiler-hidden");
                return false;
            }
        }

        private void ApplyBenchmark(JObject response)
        {
            benchmarkResponse = response;
            var benchmark = response?["benchmark"] as JObject;
            benchmarkId = benchmark?.Value<string>("benchmarkId");
            benchmarkRunning = benchmark?.Value<bool?>("running") == true;
            benchmarkPollingFailed = false;
            nextBenchmarkPoll = EditorApplication.timeSinceStartup + 0.5;
            string state = benchmark?.Value<string>("state");
            benchmarkStatus.text = benchmark == null ? "No retained benchmark. Run explicitly to measure a snapshot." :
                (state ?? "Unknown") + " · " + (benchmark.Value<string>("phase") ?? "—") + " · measured " +
                DiagnosticCount(benchmark, "completedSamples") + "/" + DiagnosticCount(benchmark, "totalSamples") +
                " · warmups " + DiagnosticCount(benchmark, "completedWarmups") + "/" + DiagnosticCount(benchmark, "warmups") +
                (benchmark.Value<bool?>("partial") == true ? " · partial results" : "");
            benchmarkStatus.tooltip = benchmark == null ? "Only the latest benchmark result is retained." :
                "Benchmark " + benchmarkId + "\nDocument " + benchmark.Value<string>("documentId") +
                "\nElapsed " + Milliseconds(benchmark, "elapsedMs") + " ms · stop reason: " + (benchmark.Value<string>("stopReason") ?? "—");
            benchmarkCold.text = BenchmarkSeries("Cold", benchmark?["cold"] as JObject);
            benchmarkWarm.text = BenchmarkSeries("Warm", benchmark?["warm"] as JObject);
            var metadata = benchmark?["metadata"] as JObject;
            benchmarkMetadata.text = metadata == null ? "" : (metadata.Value<string>("documentName") ?? "Document snapshot") + " · " +
                DiagnosticCount(metadata, "width") + " × " + DiagnosticCount(metadata, "height") + " · " + (metadata.Value<string>("quality") ?? "");
            benchmarkMetadata.tooltip = metadata == null ? "" : "Source " + DiagnosticCount(metadata, "sourceWidth") + " × " + DiagnosticCount(metadata, "sourceHeight") +
                "\nContent " + metadata.Value<string>("contentHash") + "\nUnity " + metadata.Value<string>("unityVersion") +
                "\n" + metadata.Value<string>("graphicsDevice") + " · " + metadata.Value<string>("graphicsApi") + "\n" + metadata.Value<string>("processor");
            benchmarkError.text = benchmark?.Value<string>("error") ?? (state == "failed" ? "Benchmark failed; no error details were returned." : "");
            benchmarkError.EnableInClassList("whimtex-profiler-hidden", string.IsNullOrEmpty(benchmarkError.text));
            var method = new StringBuilder("Cold does not restart Unity or JIT. Warmups are excluded from statistics. Only the latest run is retained; no archive is created.");
            if (metadata != null)
            {
                foreach (string key in new[] { "timing", "coldDefinition", "warmDefinition", "statistics" })
                    if (!string.IsNullOrEmpty(metadata.Value<string>(key))) method.Append('\n').Append(metadata.Value<string>(key));
                if (metadata["caveats"] is JArray caveats)
                    foreach (var caveat in caveats) if (caveat.Type == JTokenType.String) method.Append('\n').Append(caveat.Value<string>());
            }
            benchmarkMethod.text = method.ToString();
            UpdateBenchmarkControls();
        }

        private static string BenchmarkSeries(string name, JObject series)
        {
            var stats = series?["statistics"] as JObject;
            if (stats == null) return name + " · no measured samples";
            return name + " · n " + DiagnosticCount(stats, "count") + " · median " + Milliseconds(stats, "medianMs") +
                " ms · p95 " + Milliseconds(stats, "p95Ms") + " ms · range " + Milliseconds(stats, "minMs") + "–" + Milliseconds(stats, "maxMs") + " ms";
        }
    }
}
