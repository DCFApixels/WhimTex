using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class WhimTexProfilerWindow
    {
        private static readonly string[] ComparisonKeys = { "selfMs", "totalMs", "meanMs", "maxMs", "gpuMs", "count", "gcAllocationCount", "gcSelfAllocationCount" };
        private static readonly List<string> ComparisonTitles = new List<string> { "Self ms", "Total ms", "Mean ms", "Max ms", "GPU ms", "Count", "Allocations", "Self allocations" };
        [SerializeField] private bool showComparison;
        [SerializeField] private int comparisonSort;
        private readonly List<JObject> comparisonRows = new List<JObject>(PageSize);
        private string baselineId;
        private string comparisonQuery;
        private string comparisonError;
        private JObject comparisonInfo;
        private int comparisonOffset;
        private int? comparisonNextOffset;
        private double nextBaselinePoll;
        private VisualElement summaryContent;
        private VisualElement comparisonContent;
        private ScrollView comparisonNotices;
        private ListView comparisonList;
        private Label baselineStatus;
        private Label comparisonStats;
        private Label comparisonNotice;
        private Label comparisonPage;
        private Label comparisonDeltaHeader;
        private Button setBaseline;
        private Button clearBaseline;
        private Button comparisonPrevious;
        private Button comparisonNext;
        private Toggle comparisonToggle;
        private DropdownField comparisonMetric;

        private bool CurrentCaptureReady => !string.IsNullOrEmpty(captureId) && !recording && openSpans == 0 && pendingGpu == 0;
        private bool ComparisonReady => CurrentCaptureReady && !string.IsNullOrEmpty(baselineId);

        private void CreateBaselineControls(VisualElement parent)
        {
            var row = Row(parent, true);
            row.AddToClassList("whimtex-profiler-baseline");
            setBaseline = AddButton(row, "Set baseline", SetBaseline);
            setBaseline.name = "set-baseline";
            clearBaseline = AddButton(row, "Clear baseline", ClearBaseline);
            clearBaseline.name = "clear-baseline";
            clearBaseline.tooltip = "Discard the separate baseline. The current capture is retained.";
            baselineStatus = new Label("No baseline") { name = "baseline-status", enableRichText = false };
            baselineStatus.AddToClassList("whimtex-profiler-baseline-info");
            row.Add(baselineStatus);
        }

        private void CreateComparisonView(VisualElement pane)
        {
            comparisonSort = Mathf.Clamp(comparisonSort, 0, ComparisonKeys.Length - 1);
            comparisonToggle = new Toggle { text = "Compare baseline", name = "comparison-toggle" };
            comparisonToggle.AddToClassList("whimtex-profiler-comparison-toggle");
            comparisonToggle.RegisterValueChangedCallback(e => SetComparisonView(e.newValue));
            rootWall.parent.Add(comparisonToggle);
            comparisonContent = new VisualElement { name = "comparison-content" };
            comparisonContent.AddToClassList("whimtex-profiler-summary-content");
            pane.Add(comparisonContent);
            var controls = Row(comparisonContent);
            controls.AddToClassList("whimtex-profiler-comparison-controls");
            comparisonMetric = new DropdownField("Metric", ComparisonTitles, comparisonSort) { name = "comparison-sort" };
            comparisonMetric.AddToClassList("whimtex-profiler-comparison-sort");
            comparisonMetric.tooltip = "Show this metric and sort all matching rows by absolute delta, largest first. Delta = current minus baseline. Allocations include nested stages; Self allocations exclude them. Both are main-thread managed allocation counts, not bytes.";
            comparisonMetric.RegisterValueChangedCallback(e => SetComparisonSort(ComparisonTitles.IndexOf(e.newValue)));
            controls.Add(comparisonMetric);
            comparisonStats = new Label { name = "comparison-stats", enableRichText = false };
            comparisonStats.AddToClassList("whimtex-profiler-comparison-stats");
            controls.Add(comparisonStats);
            comparisonNotices = new ScrollView(ScrollViewMode.Vertical) { name = "comparison-notices" };
            comparisonNotices.AddToClassList("whimtex-profiler-comparison-notices");
            comparisonNotice = new Label { name = "comparison-warnings", enableRichText = false };
            comparisonNotice.AddToClassList("whimtex-profiler-comparison-notice");
            comparisonNotices.Add(comparisonNotice);
            comparisonContent.Add(comparisonNotices);
            var header = Columns(comparisonContent);
            Cell(header, "Phase / layer / FX", "phase");
            Cell(header, "State", "comparison-state");
            Cell(header, "Calls (B → C)", "comparison-count").tooltip = "Baseline → current call count. ≠ marks a changed count.";
            Cell(header, "Baseline", "number");
            Cell(header, "Current", "number");
            comparisonDeltaHeader = Cell(header, "Δ ms", "number");
            Cell(header, "Δ %", "number").tooltip = "Unavailable when the baseline is zero or either measurement is missing or incomplete.";
            comparisonList = MakeList(comparisonRows, MakeComparisonRow, BindComparison, SelectionType.None);
            comparisonList.name = "comparison-list";
            comparisonContent.Add(comparisonList);
            AlignColumns(header, comparisonList);
            var paging = Row(comparisonContent);
            paging.AddToClassList("whimtex-profiler-paging");
            comparisonPrevious = AddButton(paging, "Previous", () => ReadComparisonPage(Math.Max(0, comparisonOffset - PageSize)));
            comparisonPrevious.name = "comparison-previous";
            comparisonPage = new Label { name = "comparison-page" };
            comparisonPage.AddToClassList("whimtex-profiler-page");
            comparisonPage.tooltip = "200 rows per page; all matching rows are available through paging. Hover a row for other timings, allocation counts and measurement coverage.";
            paging.Add(comparisonPage);
            comparisonNext = AddButton(paging, "Next", () => ReadComparisonPage(comparisonNextOffset ?? comparisonOffset));
            comparisonNext.name = "comparison-next";
            ApplyComparisonVisibility();
        }

        private void SetComparisonView(bool enabled)
        {
            showComparison = enabled;
            ApplyComparisonVisibility();
            RefreshComparison();
        }

        private void ApplyComparisonVisibility()
        {
            comparisonToggle.SetValueWithoutNotify(showComparison);
            summaryContent.EnableInClassList("whimtex-profiler-hidden", showComparison);
            comparisonContent.EnableInClassList("whimtex-profiler-hidden", !showComparison);
            rootWall.EnableInClassList("whimtex-profiler-hidden", showComparison);
            summaryContent.parent.EnableInClassList("whimtex-profiler-comparison-open", showComparison);
            if (rootWall.parent[0] is Label title) title.text = showComparison ? "Comparison" : "Summary";
        }

        private void SetBaseline()
        {
            if (!CurrentCaptureReady) return;
            if (!Call(Request("baseline", true), out var response)) return;
            ApplyCapture(response);
            InvalidateComparison();
            RefreshComparison();
        }

        private void ClearBaseline()
        {
            if (string.IsNullOrEmpty(baselineId)) return;
            var request = Request("clearBaseline");
            request["baselineId"] = baselineId;
            if (!Call(request, out var response)) return;
            ApplyCapture(response);
            InvalidateComparison();
            RefreshComparison();
        }

        private void ApplyBaseline(JObject response)
        {
            if (response["baseline"] == null) return;
            var baseline = response["baseline"] as JObject;
            string id = baseline?.Value<string>("captureId");
            if (baselineId != id)
            {
                baselineId = id;
                InvalidateComparison();
            }
            if (baselineStatus == null) return;
            bool truncated = baseline?.Value<bool?>("truncated") == true || baseline?.Value<int?>("droppedEvents") > 0;
            baselineStatus.text = string.IsNullOrEmpty(id) ? "No baseline" :
                $"Baseline {ShortCaptureId(id)} · stopped · {baseline["eventCount"]} events · {Milliseconds(baseline, "elapsedMs")} ms" +
                (truncated ? " · truncated" : "");
            baselineStatus.tooltip = string.IsNullOrEmpty(id) ? "Set a baseline from a stopped capture, then record another capture to compare." :
                $"Baseline capture {id}\nStopped: {baseline["stopReason"]} · {baseline["droppedEvents"]} dropped\n" +
                "Record and Clear affect only the current capture. The baseline is retained separately across script reloads in this Editor session.";
            UpdateComparisonControls();
        }

        private static string ShortCaptureId(string id) => id.Length <= 8 ? id : id.Substring(0, 8);

        private void SetComparisonSort(int index)
        {
            if (index < 0 || index >= ComparisonKeys.Length || index == comparisonSort) return;
            comparisonSort = index;
            comparisonMetric.SetValueWithoutNotify(ComparisonTitles[index]);
            InvalidateComparison();
            RefreshComparison();
        }

        private JObject ComparisonRequest(int requestedOffset)
        {
            var request = Request("compare", true);
            request["baselineId"] = baselineId;
            request["offset"] = requestedOffset;
            request["limit"] = PageSize;
            request["sort"] = ComparisonKeys[comparisonSort];
            var groups = new JArray();
            foreach (var group in WhimTexProfiler.Groups)
                if ((selectedGroups & group) != 0) groups.Add(WhimTexProfiler.GroupId(group));
            request["groups"] = groups;
            if (!string.IsNullOrWhiteSpace(layerFilter)) request["layer"] = layerFilter.Trim();
            if (!string.IsNullOrWhiteSpace(phaseFilter)) request["phase"] = phaseFilter.Trim();
            return request;
        }

        private void RefreshComparison()
        {
            UpdateComparisonControls();
            if (!showComparison || !ComparisonReady) return;
            if (comparisonQuery != ComparisonRequest(0).ToString(Formatting.None)) ReadComparisonPage(0);
        }

        private void ReadComparisonPage(int requestedOffset)
        {
            if (!ComparisonReady) return;
            string query = ComparisonRequest(0).ToString(Formatting.None);
            InvalidateComparison();
            comparisonQuery = query;
            if (!Call(ComparisonRequest(requestedOffset), out var response))
            {
                comparisonError = "Comparison unavailable. Press Refresh to retry.";
                UpdateComparisonControls();
                return;
            }
            ApplyCapture(response);
            comparisonInfo = response["comparison"] as JObject;
            comparisonQuery = query;
            if (comparisonInfo == null)
            {
                comparisonError = "No comparison was returned. Press Refresh to retry.";
                UpdateComparisonControls();
                return;
            }
            comparisonOffset = comparisonInfo.Value<int?>("offset") ?? requestedOffset;
            comparisonNextOffset = comparisonInfo.Value<int?>("nextOffset");
            if (comparisonInfo["rows"] is JArray rows)
                for (int i = 0; i < rows.Count && i < PageSize; i++)
                    if (rows[i] is JObject row) comparisonRows.Add(row);
            comparisonList.RefreshItems();
            var scroll = comparisonList.Q<ScrollView>();
            if (scroll != null) scroll.scrollOffset = Vector2.zero;
            UpdateComparisonControls();
        }

        private void InvalidateComparison()
        {
            comparisonQuery = null;
            comparisonInfo = null;
            comparisonError = null;
            comparisonOffset = 0;
            comparisonNextOffset = null;
            comparisonRows.Clear();
            comparisonList?.RefreshItems();
            UpdateComparisonControls();
        }

        private void UpdateComparisonControls()
        {
            bool hasBaseline = !string.IsNullOrEmpty(baselineId);
            setBaseline?.SetEnabled(CurrentCaptureReady);
            clearBaseline?.SetEnabled(hasBaseline);
            if (setBaseline != null) setBaseline.tooltip = CurrentCaptureReady ?
                "Use the entire stopped current capture as the baseline, replacing any previous baseline. Filters do not affect it." :
                "Requires a stopped current capture with zero open spans and no pending GPU samples.";
            if (comparisonList == null) return;
            comparisonMetric.SetEnabled(ComparisonReady);
            comparisonPrevious.SetEnabled(ComparisonReady && comparisonOffset > 0);
            comparisonNext.SetEnabled(ComparisonReady && comparisonNextOffset.HasValue);
            string key = ComparisonKeys[comparisonSort];
            comparisonDeltaHeader.text = key == "count" ? "Δ calls" : IsAllocationMetric(key) ? "Δ alloc" : "Δ ms";
            string unavailable = !hasBaseline ? "Set a baseline from a stopped capture." :
                string.IsNullOrEmpty(captureId) ? "Record and stop a capture to compare." :
                recording ? "Stop recording to compare." : openSpans > 0 ? "Waiting for open spans to finish." :
                pendingGpu > 0 ? "Waiting for GPU samples to complete." : comparisonError ?? "No comparison loaded.";
            comparisonStats.text = unavailable;
            comparisonStats.tooltip = unavailable;
            comparisonPage.text = unavailable;
            if (ComparisonReady && comparisonInfo != null)
            {
                var before = comparisonInfo["before"] as JObject;
                var after = comparisonInfo["after"] as JObject;
                comparisonStats.text = $"Root wall {ComparisonNumber(before, "rootWallMs")} → {ComparisonNumber(after, "rootWallMs")} ms · Δ = current − baseline";
                comparisonStats.tooltip = $"Baseline → current: {before?["rootCount"]} → {after?["rootCount"]} roots; " +
                    $"{before?["matchedEvents"]} → {after?["matchedEvents"]} matching events.\n" +
                    "Root totals include only matching completed roots. Nested totals and CPU/GPU overlap; do not sum them. Call counts and workloads can differ.";
                long total = comparisonInfo.Value<long?>("totalRows") ?? comparisonRows.Count;
                comparisonPage.text = comparisonRows.Count == 0 ? selectedGroups == WhimTexProfiler.Group.None ? "No groups selected" : "No matching spans" :
                    $"{comparisonOffset + 1}–{comparisonOffset + comparisonRows.Count} of {total} rows · |Δ| {ComparisonTitles[comparisonSort]}, largest first";
            }
            var notices = new List<string>();
            if (operationId.HasValue) notices.Add("Operation filter ignored in Comparison; operation IDs belong to one capture. Events remain filtered.");
            if (comparisonInfo?["warnings"] is JArray warnings)
                foreach (var warning in warnings)
                    if (warning.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)warning)) notices.Add((string)warning);
            comparisonNotice.text = string.Join("\n", notices);
            comparisonNotice.tooltip = comparisonNotice.text;
            comparisonNotices.EnableInClassList("whimtex-profiler-hidden", notices.Count == 0);
        }

        private static VisualElement MakeComparisonRow()
        {
            var row = Row(null);
            row.AddToClassList("whimtex-profiler-data-row");
            Cell(row, "", "phase");
            Cell(row, "", "comparison-state");
            Cell(row, "", "comparison-count");
            Cell(row, "", "number").name = "comparison-before";
            Cell(row, "", "number").name = "comparison-after";
            Cell(row, "", "number").name = "comparison-delta";
            Cell(row, "", "number").name = "comparison-percent";
            return row;
        }

        private void BindComparison(VisualElement row, int index)
        {
            JObject item = comparisonRows[index];
            var before = item["before"] as JObject;
            var after = item["after"] as JObject;
            bool countChanged = item.Value<bool?>("countChanged") == true;
            string state = item.Value<string>("status");
            string key = ComparisonKeys[comparisonSort];
            bool gpu = key == "gpuMs";
            bool allocations = IsAllocationMetric(key);
            bool comparable = allocations ? item.Value<bool?>("allocationComparable") == true : !gpu || item.Value<bool?>("gpuComparable") == true;
            SetTone(row, Category(item));
            SetCell(row, "phase", FxTitle(item, true));
            SetCell(row, "comparison-state", state == "added" ? "Added" : state == "removed" ? "Removed" : "Matched");
            SetCell(row, "comparison-count", (countChanged ? "≠ " : "") + ComparisonNumber(before, "count") + " → " + ComparisonNumber(after, "count"));
            var counts = row.Q<Label>("comparison-count");
            counts.EnableInClassList("whimtex-profiler-count-changed", countChanged);
            counts.tooltip = (countChanged ? "Call count changed. " : "Call count unchanged. ") + "Baseline → current; totals can reflect different amounts of work.";
            SetCell(row, "comparison-before", ComparisonNumber(before, key) + (gpu && PartialGpu(before) || allocations && PartialAllocations(before, key) ? "*" : ""));
            SetCell(row, "comparison-after", ComparisonNumber(after, key) + (gpu && PartialGpu(after) || allocations && PartialAllocations(after, key) ? "*" : ""));
            SetCell(row, "comparison-delta", comparable ? ComparisonNumber(item["delta"] as JObject, key, true) : "—");
            string percent = comparable ? ComparisonNumber(item["percent"] as JObject, key, true) : "—";
            SetCell(row, "comparison-percent", percent == "—" ? percent : percent + "%");
            bool colorDelta = comparable && key != "count";
            SetComparisonDeltaTone(row.Q<Label>("comparison-delta"), colorDelta ? (item["delta"] as JObject)?.Value<decimal?>(key) : null);
            SetComparisonDeltaTone(row.Q<Label>("comparison-percent"), colorDelta ? (item["percent"] as JObject)?.Value<decimal?>(key) : null);
            row.Q<Label>("comparison-state").tooltip = state == "added" ? "Present only in the current capture." :
                state == "removed" ? "Present only in the baseline capture." : "The same phase, document, layer and FX slot occur in both captures.";
            row.Q<Label>("comparison-percent").tooltip = "(Current − baseline) / baseline × 100. Unavailable for zero or missing baseline values, or incomplete GPU/allocation measurements.";
            row.Q<Label>("comparison-delta").tooltip = allocations ? comparable ?
                "Current minus baseline managed allocation count on the main thread, not bytes. Compare call counts and workload as well." :
                "Allocation delta requires measured counts for every eligible call in both captures; missing or partial measurements are not zero." : gpu && !comparable ?
                "GPU delta requires complete GPU samples in both captures; missing or partial measurements are not zero." : "Current minus baseline. A timing change alone does not establish a performance change.";
            row.tooltip = ComparisonTooltip(item);
            row.Q<Label>("phase").tooltip = row.tooltip;
            if (gpu)
            {
                row.Q<Label>("comparison-before").tooltip = GpuSampleText(before);
                row.Q<Label>("comparison-after").tooltip = GpuSampleText(after);
            }
            else if (allocations)
            {
                string scope = key == "gcAllocationCount" ? "Includes nested stages." : "Excludes nested stages.";
                row.Q<Label>("comparison-before").tooltip = AllocationSampleText(before) + " " + scope;
                row.Q<Label>("comparison-after").tooltip = AllocationSampleText(after) + " " + scope;
            }
        }

        private static bool IsAllocationMetric(string key) => key == "gcAllocationCount" || key == "gcSelfAllocationCount";

        private static void SetComparisonDeltaTone(Label cell, decimal? value)
        {
            bool visibleChange = cell.text != "0" && cell.text != "0%";
            cell.EnableInClassList("whimtex-profiler-delta-decrease", visibleChange && value < 0);
            cell.EnableInClassList("whimtex-profiler-delta-increase", visibleChange && value > 0);
        }

        private static bool PartialAllocations(JObject metrics, string key) => metrics?.Value<long?>(key) != null && metrics.Value<bool?>("partial") == true;

        private static bool PartialGpu(JObject metrics) => metrics?.Value<double?>("gpuMs") != null &&
            (metrics.Value<int?>("gpuCount") ?? 0) < (metrics.Value<int?>("count") ?? 0);

        private static string ComparisonNumber(JObject metrics, string key, bool signed = false)
        {
            decimal? value = metrics?.Value<decimal?>(key);
            return value.HasValue ? value.Value.ToString(signed ? "+0.###;-0.###;0" : "0.###", CultureInfo.InvariantCulture) : "—";
        }

        private static string GpuSampleText(JObject metrics) => metrics == null ? "No matching stage." :
            $"GPU: {metrics["gpuCount"]} ready, {metrics["gpuPendingCount"]} pending, {metrics["gpuUnavailableCount"]} unavailable. * marks a partial total; unavailable is not zero.";

        private static string AllocationSampleText(JObject metrics) => metrics == null ? "No matching stage." :
            $"Main-thread managed allocation count, not bytes. Measured calls: {metrics.Value<int?>("measuredCalls") ?? 0} / {metrics.Value<int?>("eligibleCalls") ?? 0}. * marks a partial sum; unavailable is not zero.";

        private static string ComparisonTooltip(JObject item)
        {
            var before = item["before"] as JObject;
            var after = item["after"] as JObject;
            string text = FxTitle(item, true) + "\nBaseline → current · " + item.Value<string>("status") +
                (item.Value<bool?>("countChanged") == true ? " · call count changed" : "") +
                "\nCalls " + ComparisonNumber(before, "count") + " → " + ComparisonNumber(after, "count");
            string[] keys = { "totalMs", "selfMs", "meanMs", "maxMs", "cpuPrepareMs", "cpuSubmitMs", "gpuMs", "gpuMeanMs" };
            string[] names = { "Total", "Self", "Mean", "Max", "CPU prepare", "CPU submit", "GPU", "GPU mean" };
            for (int i = 0; i < keys.Length; i++) text += "\n" + names[i] + " " + ComparisonNumber(before, keys[i]) + " → " + ComparisonNumber(after, keys[i]) + " ms";
            text += "\nBaseline " + GpuSampleText(before) + "\nCurrent " + GpuSampleText(after);
            foreach (string key in new[] { "gcAllocationCount", "gcSelfAllocationCount" })
                text += "\n" + (key == "gcAllocationCount" ? "Allocations " : "Self allocations ") +
                    ComparisonNumber(before, key) + (PartialAllocations(before, key) ? "*" : "") + " → " +
                    ComparisonNumber(after, key) + (PartialAllocations(after, key) ? "*" : "");
            text += "\nBaseline " + AllocationSampleText(before) + "\nCurrent " + AllocationSampleText(after);
            if (before?.Value<string>("layerName") != after?.Value<string>("layerName"))
                text += "\nLayer " + (before?.Value<string>("layerName") ?? "—") + " → " + (after?.Value<string>("layerName") ?? "—");
            if (before?.Value<string>("fxName") != after?.Value<string>("fxName"))
                text += "\nFX " + (before?.Value<string>("fxName") ?? "—") + " → " + (after?.Value<string>("fxName") ?? "—");
            return text;
        }
    }
}
