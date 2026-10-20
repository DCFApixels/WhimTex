using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class WhimTexProfilerWindow : EditorWindow
    {
        private const int PageSize = 200;
        private const string StyleGuid = "e0ebef3cf2954231a875d8744227ce42";
        private enum SummaryColumn { Count, Total, Self, Max, CpuPrepare, Gpu, Allocations, SelfAllocations }
        [SerializeField] private int maxEvents = 32768;
        [SerializeField] private double durationSeconds = 120;
        [NonSerialized] private string documentId = "";
        [SerializeField] private string layerFilter = "";
        [SerializeField] private string phaseFilter = "";
        [SerializeField] private WhimTexProfiler.Group selectedGroups = WhimTexProfiler.Group.All;
        [SerializeField] private float summaryHeight = 180;
        [SerializeField] private float detailsHeight = 170;
        [SerializeField] private SummaryColumn summarySort = SummaryColumn.Total;
        [SerializeField] private bool summaryDescending = true;

        private readonly List<JObject> events = new List<JObject>(PageSize);
        private readonly List<JObject> summary = new List<JObject>();
        private readonly Dictionary<SummaryColumn, Button> summaryHeaders = new Dictionary<SummaryColumn, Button>();
        private readonly HashSet<int> collapsedEvents = new HashSet<int>();
        private readonly Dictionary<WhimTexProfiler.Group, Toggle> groupToggles = new Dictionary<WhimTexProfiler.Group, Toggle>();
        private Foldout groupFilters;
        private WhimTexProfiler.Group appliedGroups;
        private string captureId;
        private bool recording;
        private int openSpans;
        private int pendingGpu;
        private int offset;
        private int? nextOffset;
        private long? operationId;
        private string appliedLayer;
        private string appliedPhase;
        private JObject selectedEvent;
        private ListView eventList;
        private ListView summaryList;
        private Label status;
        private Label gpuEnvironment;
        private HelpBox gpuWarning;
        private Label page;
        private Label operation;
        private Label rootWall;
        private Label detailTitle;
        private Label detailInfo;
        private Label eventCount;
        private double summaryScale;
        private TextField detail;
        private HelpBox message;
        private VisualElement captureSettings;
        private Button start;
        private Button stop;
        private Button clear;
        private Button export;
        private Button previous;
        private Button next;
        private Button inspectOperation;
        private Button allOperations;
        private IVisualElementScheduledItem automaticRefresh;
        private double nextAutomaticRead;
        private int loadedCount = -1, loadedOpen, loadedGpuRevision;
        private bool loadedRecording;
        private string gpuAvailability;

        [MenuItem("Window/Analysis/WhimTex Profiler")]
        public static void Open()
        {
            GetWindow<WhimTexProfilerWindow>("WhimTex Profiler").Show();
        }

        public void CreateGUI()
        {
            automaticRefresh?.Pause();
            minSize = new Vector2(680, 620);
            var root = rootVisualElement;
            root.Clear();
            summaryHeaders.Clear();
            groupToggles.Clear();
            root.AddToClassList("whimtex-profiler");
            root.EnableInClassList("whimtex-profiler--light", !EditorGUIUtility.isProSkin);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(StyleGuid));
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);

            var actions = Row(root, true);
            actions.AddToClassList("whimtex-profiler-toolbar");
            start = AddButton(actions, "Record", StartCapture);
            start.AddToClassList("whimtex-profiler-record");
            start.tooltip = "Replaces the stopped current capture. The baseline is retained separately. Export the current capture first if you need to keep it.";
            stop = AddButton(actions, "Stop", () => CaptureAction("stop"));
            AddButton(actions, "Refresh", RefreshCapture);
            var spacer = new VisualElement();
            spacer.AddToClassList("whimtex-profiler-spacer");
            actions.Add(spacer);
            export = AddButton(actions, "Export Temp JSON", ExportCapture);
            export.tooltip = "After stopping and completing open spans/GPU queries, write the entire capture without filters to a new Temp/WhimTex JSON file.";
            clear = AddButton(actions, "Clear", () => CaptureAction("clear"));
            clear.tooltip = "Discard the stopped current capture only. The baseline, documents and render caches are unchanged.";

            var settings = new Foldout { text = "Capture settings", value = false, viewDataKey = "capture-settings" };
            settings.AddToClassList("whimtex-profiler-settings");
            root.Add(settings);
            captureSettings = Row(settings, true);
            var capacity = new IntegerField("Max events") { value = maxEvents, isDelayed = true };
            capacity.tooltip = "256–262144 events; default 32768. Recording stops when full.";
            capacity.RegisterValueChangedCallback(e => maxEvents = e.newValue);
            AddField(captureSettings, capacity);
            var duration = new DoubleField("Seconds") { value = durationSeconds, isDelayed = true };
            duration.tooltip = "1–600 seconds; default 120. Recording stops at the deadline.";
            duration.RegisterValueChangedCallback(e => durationSeconds = e.newValue);
            AddField(captureSettings, duration);
            var document = new TextField("Document ID") { name = "document-id", value = documentId, isDelayed = true };
            document.tooltip = "Paste the opaque profilerDocumentId string returned by API discovery unchanged. Leave empty to record all documents; a benchmark requires one document.";
            document.RegisterValueChangedCallback(e => { documentId = e.newValue; UpdateBenchmarkControls(); });
            AddField(captureSettings, document);

            status = new Label();
            status.AddToClassList("whimtex-profiler-status");
            root.Add(status);
            gpuEnvironment = new Label { name = "gpu-environment" };
            gpuEnvironment.AddToClassList("whimtex-profiler-gpu-environment");
            root.Add(gpuEnvironment);
            gpuWarning = new HelpBox(WhimTexProfiler.GpuUnconfirmedWarning, HelpBoxMessageType.Warning) { name = "gpu-warning" };
            root.Add(gpuWarning);
            CreateBaselineControls(root);
            CreateDiagnosticsPanel(root);
            var filters = Row(root, true);
            var layer = new TextField("Layer") { value = layerFilter, isDelayed = true };
            layer.tooltip = "Captured layer name (contains, case-insensitive) or exact layer ID. Enter or leave the field to apply.";
            layer.RegisterValueChangedCallback(e => { layerFilter = e.newValue; ReadPage(0); });
            AddField(filters, layer);
            var phase = new TextField("Phase") { value = phaseFilter, isDelayed = true };
            phase.tooltip = "Case-insensitive phase text filter. Enter or leave the field to apply.";
            phase.RegisterValueChangedCallback(e => { phaseFilter = e.newValue; ReadPage(0); });
            AddField(filters, phase);

            groupFilters = new Foldout { text = "Groups", value = true, viewDataKey = "group-filters" };
            groupFilters.AddToClassList("whimtex-profiler-settings");
            root.Add(groupFilters);
            var groupActions = Row(groupFilters, true);
            AddButton(groupActions, "All", () => SetGroups(WhimTexProfiler.Group.All)).name = "groups-all";
            AddButton(groupActions, "None", () => SetGroups(WhimTexProfiler.Group.None)).name = "groups-none";
            var groupHint = new Label("Toggle to show/hide · None, then one checkbox to isolate · Current capture counts before group filtering");
            groupHint.AddToClassList("whimtex-profiler-group-hint");
            groupActions.Add(groupHint);
            var groupOptions = Row(groupFilters, true);
            foreach (var group in WhimTexProfiler.Groups)
            {
                string id = WhimTexProfiler.GroupId(group);
                var option = Row(groupOptions);
                option.AddToClassList("whimtex-profiler-group-option");
                SetTone(option, id);
                var toggle = new Toggle { text = WhimTexProfiler.GroupName(group), name = "group-" + id };
                toggle.AddToClassList("whimtex-profiler-group-toggle");
                toggle.tooltip = "Show or hide this group in Summary, Comparison and Events. Timings and retained captures are unchanged.";
                toggle.RegisterValueChangedCallback(e => SetGroups(e.newValue ? selectedGroups | group : selectedGroups & ~group));
                option.Add(toggle);
                groupToggles.Add(group, toggle);
            }
            UpdateGroupControls();

            var legend = Row(root, true);
            legend.AddToClassList("whimtex-profiler-legend");
            AddLegend(legend, "Cache hit", "hit");
            AddLegend(legend, "Cache miss", "miss");
            AddLegend(legend, "Error", "error");

            var outer = Split("summary-split", 0, Mathf.Max(110, summaryHeight));
            root.Add(outer);
            var summaryPane = Pane(outer, "summary-pane");
            summaryPane.RegisterCallback<GeometryChangedEvent>(e => { if (e.newRect.height >= 110) summaryHeight = e.newRect.height; });
            rootWall = Heading(summaryPane, "Summary", "All matching events · click a column to sort");
            rootWall.tooltip = "Sum of completed root spans that match the filters. Inclusive summary rows overlap; do not sum them.";
            var summaryScroll = new ScrollView(ScrollViewMode.Horizontal) { name = "summary-content" };
            summaryContent = summaryScroll;
            summaryContent.AddToClassList("whimtex-profiler-summary-content");
            summaryScroll.contentContainer.AddToClassList("whimtex-profiler-summary-table");
            summaryPane.Add(summaryContent);
            var summaryHeader = Columns(summaryContent);
            Cell(summaryHeader, "Phase / kind", "phase");
            AddSummaryHeader(summaryHeader, SummaryColumn.Count, "count");
            AddSummaryHeader(summaryHeader, SummaryColumn.Total, "duration");
            AddSummaryHeader(summaryHeader, SummaryColumn.Self, "number");
            AddSummaryHeader(summaryHeader, SummaryColumn.Max, "number");
            AddSummaryHeader(summaryHeader, SummaryColumn.CpuPrepare, "number");
            AddSummaryHeader(summaryHeader, SummaryColumn.Gpu, "number");
            AddSummaryHeader(summaryHeader, SummaryColumn.Allocations, "allocations");
            AddSummaryHeader(summaryHeader, SummaryColumn.SelfAllocations, "self-allocations");
            summaryList = MakeList(summary, MakeSummaryRow, BindSummary, SelectionType.None);
            summaryList.AddToClassList("whimtex-profiler-summary");
            summaryContent.Add(summaryList);
            AlignColumns(summaryHeader, summaryList);
            CreateComparisonView(summaryPane);

            var lower = Split("details-split", 1, Mathf.Max(105, detailsHeight));
            lower.AddToClassList("whimtex-profiler-lower");
            outer.Add(lower);
            var eventPane = Pane(lower, "events-pane");
            eventCount = Heading(eventPane, "Events", "Event tree · bars = share of operation");
            AddButton(eventCount.parent, "Expand all", ExpandAllBranches);
            AddButton(eventCount.parent, "Collapse all", CollapseAllBranches).tooltip = "Collapse every branch across all pages, leaving only root events.";
            var eventHeader = Columns(eventPane);
            Cell(eventHeader, "Phase", "phase");
            Cell(eventHeader, "Layer", "layer");
            Cell(eventHeader, "Kind", "kind");
            Cell(eventHeader, "Wall ms", "duration");
            Cell(eventHeader, "Prep ms", "number");
            Cell(eventHeader, "GPU ms", "number");
            eventList = MakeList(events, MakeEventRow, BindEvent, SelectionType.Single);
            eventList.AddToClassList("whimtex-profiler-events");
            eventList.selectionChanged += SelectEvent;
            eventList.RegisterCallback<KeyDownEvent>(OnTreeKey, TrickleDown.TrickleDown);
            eventPane.Add(eventList);
            AlignColumns(eventHeader, eventList);
            var paging = Row(eventPane);
            paging.AddToClassList("whimtex-profiler-paging");
            previous = AddButton(paging, "Previous", () => ReadPage(Math.Max(0, offset - PageSize)));
            page = new Label();
            page.AddToClassList("whimtex-profiler-page");
            paging.Add(page);
            next = AddButton(paging, "Next", () => ReadPage(nextOffset ?? offset));

            var detailsPane = Pane(lower, "details-pane");
            detailsPane.RegisterCallback<GeometryChangedEvent>(e => { if (e.newRect.height >= 105) detailsHeight = e.newRect.height; });
            Heading(detailsPane, "Event details", "Select a row to inspect timings and context");
            var selection = Row(detailsPane, true);
            operation = new Label("Selected operation / event");
            operation.AddToClassList("whimtex-profiler-page");
            selection.Add(operation);
            inspectOperation = AddButton(selection, "Only this operation", InspectOperation);
            allOperations = AddButton(selection, "All operations", () => { operationId = null; ReadPage(0); });
            var detailsScroll = new ScrollView(ScrollViewMode.Vertical) { viewDataKey = "details-scroll" };
            detailsScroll.AddToClassList("whimtex-profiler-details-scroll");
            detailsPane.Add(detailsScroll);
            detailTitle = new Label { enableRichText = false };
            detailTitle.AddToClassList("whimtex-profiler-detail-title");
            detailsScroll.Add(detailTitle);
            detailInfo = new Label { enableRichText = false };
            detailInfo.AddToClassList("whimtex-profiler-detail-info");
            detailsScroll.Add(detailInfo);
            detailDiagnostics = new Label { name = "event-diagnostics", enableRichText = false };
            detailDiagnostics.AddToClassList("whimtex-profiler-detail-info");
            detailsScroll.Add(detailDiagnostics);
            CreateCauseControls(detailsScroll);
            var raw = new Foldout { text = "Raw event data / IDs", value = false, viewDataKey = "raw-event" };
            detailsScroll.Add(raw);
            detail = new TextField
            {
                multiline = true, isReadOnly = true, selectAllOnFocus = false,
                selectAllOnMouseUp = false, verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            detail.AddToClassList("whimtex-profiler-detail");
            raw.Add(detail);
            var note = new Label("GPU results update automatically · Prep = CPU wall · GPU = device interval · … pending · — unavailable · * partial GPU total");
            note.tooltip = "GPU results arrive several frames later. FX block GPU includes input renders; an individual FX includes its blit and output finalization. GPU intervals can include idle gaps. CPU preparation includes dependency renders/waits. CPU/GPU and nested totals overlap: do not sum them.";
            note.AddToClassList("whimtex-profiler-status");
            root.Add(note);
            message = new HelpBox("", HelpBoxMessageType.Info);
            message.AddToClassList("whimtex-profiler-hidden");
            root.Add(message);
            captureId = null;
            recording = false;
            openSpans = 0;
            pendingGpu = 0;
            operationId = null;
            ResetRows();
            UpdateButtons();
            RefreshCapture();
            RefreshBenchmark();
            automaticRefresh = root.schedule.Execute(AutoRefresh).Every(250);
        }

        private void OnDisable()
        {
            automaticRefresh?.Pause();
            automaticRefresh = null;
        }

        private void AutoRefresh()
        {
            PollBenchmark();
            var live = WhimTexProfiler.Current;
            bool changedCapture = live?.id != captureId;
            bool changedRows = changedCapture || live != null &&
                (loadedCount != live.count || loadedOpen != live.open || loadedGpuRevision != live.gpuRevision || loadedRecording != live.recording);
            string availability = live?.recording == true ? WhimTexProfiler.GpuAvailability : "stopped";
            if (!changedRows && live?.recording != true && gpuAvailability == availability &&
                EditorApplication.timeSinceStartup < nextBaselinePoll) return;
            nextBaselinePoll = EditorApplication.timeSinceStartup + 1;
            if (!Call(Request("status"), out var response, quiet: true)) return;
            ApplyCapture(response);
            if (changedCapture)
            {
                operationId = null;
                ResetRows();
            }
            if (changedRows && !string.IsNullOrEmpty(captureId) && EditorApplication.timeSinceStartup >= nextAutomaticRead)
                ReadPageCore(offset, preserveView: true);
            else RefreshComparison();
        }

        private void StartCapture()
        {
            if (maxEvents < 256 || maxEvents > 262144 || double.IsNaN(durationSeconds) ||
                double.IsInfinity(durationSeconds) || durationSeconds < 1 || durationSeconds > 600)
            {
                ShowMessage("Max events must be 256–262144; seconds must be 1–600.", true);
                return;
            }
            var request = Request("start");
            request["maxEvents"] = maxEvents;
            request["durationSeconds"] = durationSeconds;
            if (!string.IsNullOrWhiteSpace(documentId)) request["documentId"] = documentId;
            if (!Call(request, out var response)) return;
            ApplyCapture(response);
            operationId = null;
            ResetRows();
            ReadPage(0);
        }

        private void CaptureAction(string action)
        {
            if (string.IsNullOrEmpty(captureId)) return;
            if (!Call(Request(action, true), out var response)) return;
            ApplyCapture(response);
            if (action == "clear")
            {
                captureId = null;
                recording = false;
                openSpans = 0;
                operationId = null;
                ResetRows();
                status.text = "Capture cleared. Recording is off.";
                status.tooltip = "";
                status.RemoveFromClassList("whimtex-profiler-recording");
                UpdateButtons();
                return;
            }
            ReadPage(offset);
        }

        private void RefreshCapture()
        {
            InvalidateComparison();
            string previousCapture = captureId;
            if (!Call(Request("status"), out var response)) return;
            ApplyCapture(response);
            if (captureId != previousCapture)
            {
                operationId = null;
                ResetRows();
            }
            if (!string.IsNullOrEmpty(captureId)) ReadPage(offset);
            else ResetRows();
        }

        private void ReadPage(int requestedOffset) => ReadPageCore(requestedOffset, preserveView: false);

        private void ReadPageCore(int requestedOffset, bool preserveView)
        {
            if (string.IsNullOrEmpty(captureId))
            {
                RefreshComparison();
                return;
            }
            string layer = (layerFilter ?? "").Trim();
            string phase = (phaseFilter ?? "").Trim();
            if (layer != appliedLayer || phase != appliedPhase || selectedGroups != appliedGroups)
            {
                requestedOffset = 0;
                collapsedEvents.Clear();
                InvalidateComparison();
            }
            var request = Request("read", true);
            request["offset"] = requestedOffset;
            request["limit"] = PageSize;
            request["tree"] = true;
            request["collapsedIds"] = new JArray(collapsedEvents);
            var chosen = new JArray();
            foreach (var group in WhimTexProfiler.Groups)
                if ((selectedGroups & group) != 0) chosen.Add(WhimTexProfiler.GroupId(group));
            request["groups"] = chosen;
            if (layer.Length > 0) request["layer"] = layer;
            if (phase.Length > 0) request["phase"] = phase;
            if (operationId.HasValue) request["operationId"] = operationId.Value;
            if (!Call(request, out var response, quiet: preserveView)) return;
            long? selectedId = preserveView ? selectedEvent?.Value<long?>("id") : null;
            var eventScroll = eventList.Q<ScrollView>();
            var summaryScroll = summaryList.Q<ScrollView>();
            Vector2 eventPosition = eventScroll?.scrollOffset ?? Vector2.zero;
            Vector2 summaryPosition = summaryScroll?.scrollOffset ?? Vector2.zero;
            ApplyCapture(response);
            var live = WhimTexProfiler.Current;
            loadedCount = response["capture"].Value<int>("eventCount");
            loadedOpen = openSpans;
            loadedRecording = recording;
            loadedGpuRevision = live?.id == captureId ? live.gpuRevision : -1;
            nextAutomaticRead = EditorApplication.timeSinceStartup + .5;
            appliedLayer = layer;
            appliedPhase = phase;
            appliedGroups = selectedGroups;
            UpdateGroupControls(response["groups"] as JArray);
            offset = response.Value<int?>("offset") ?? requestedOffset;
            nextOffset = response.Value<int?>("nextOffset");
            eventList.ClearSelection();
            events.Clear();
            if (response["events"] is JArray rows)
                for (int i = 0; i < rows.Count && i < PageSize; i++)
                    if (rows[i] is JObject item) events.Add(item);
            summary.Clear();
            if (response["summary"] is JArray groups)
                foreach (JToken group in groups)
                    if (group is JObject item) summary.Add(item);
            SortSummary();
            eventList.RefreshItems();
            rootWall.text = "All matches · root wall " + Milliseconds(response, "rootWallMs") + " ms · totals overlap";
            long total = response.Value<long?>("totalMatched") ?? events.Count;
            long visible = response.Value<long?>("totalVisible") ?? total;
            eventCount.text = operationId.HasValue ? "Operation " + operationId.Value + " · " + total + " matches" :
                total + " matches · bars = share of operation";
            page.text = events.Count == 0 ? selectedGroups == WhimTexProfiler.Group.None ? "No groups selected" : "No matching events" :
                $"{offset + 1}–{offset + events.Count} of {visible} visible rows";
            page.tooltip = "Visible rows include filter ancestors and exclude collapsed descendants. Summary still includes all matching events.";
            ClearDetail();
            if (preserveView)
            {
                int selectedIndex = selectedId.HasValue ? events.FindIndex(item => item.Value<long>("id") == selectedId.Value) : -1;
                if (selectedIndex >= 0)
                {
                    eventList.SetSelectionWithoutNotify(new[] { selectedIndex });
                    SelectEvent(new[] { events[selectedIndex] });
                }
                if (eventScroll != null) eventScroll.scrollOffset = eventPosition;
                if (summaryScroll != null) summaryScroll.scrollOffset = summaryPosition;
            }
            UpdateButtons();
            RefreshComparison();
        }

        private void ExportCapture()
        {
            if (string.IsNullOrEmpty(captureId)) return;
            var request = Request("export", true);
            string path = "Temp/WhimTex/profile-" + Guid.NewGuid().ToString("N") + ".json";
            request["outputPath"] = path;
            if (Call(request, out var response))
                ShowMessage("Exported all retained events: " + (response.Value<string>("outputPath") ?? path), false);
        }

        private void SetGroups(WhimTexProfiler.Group groups)
        {
            selectedGroups = groups & WhimTexProfiler.Group.All;
            UpdateGroupControls();
            ReadPage(0);
        }

        private void UpdateGroupControls(JArray counts = null)
        {
            int enabled = 0;
            foreach (var pair in groupToggles)
            {
                bool selected = (selectedGroups & pair.Key) != 0;
                if (selected) enabled++;
                pair.Value.SetValueWithoutNotify(selected);
                int count = 0;
                if (counts != null)
                    foreach (var item in counts)
                        if (item.Value<string>("id") == WhimTexProfiler.GroupId(pair.Key)) count = item.Value<int>("count");
                pair.Value.text = WhimTexProfiler.GroupName(pair.Key) + (counts != null ? " (" + count + ")" : "");
                pair.Value.parent.EnableInClassList("whimtex-profiler-group-off", !selected);
            }
            groupFilters.text = "Groups · " + enabled + "/" + WhimTexProfiler.Groups.Length;
        }

        private static string SummaryTitle(SummaryColumn column) => column switch
        {
            SummaryColumn.Count => "Count", SummaryColumn.Self => "Self ms",
            SummaryColumn.Max => "Max ms", SummaryColumn.CpuPrepare => "Prep ms",
            SummaryColumn.Gpu => "GPU ms", SummaryColumn.Allocations => "Allocations",
            SummaryColumn.SelfAllocations => "Self allocations", _ => "Total ms"
        };

        private void AddSummaryHeader(VisualElement parent, SummaryColumn column, string widthClass)
        {
            var button = new Button(() => SetSummarySort(column)) { name = "summary-sort-" + column };
            button.AddToClassList("whimtex-profiler-cell");
            button.AddToClassList("whimtex-profiler-" + widthClass);
            button.AddToClassList("whimtex-profiler-sort-header");
            summaryHeaders.Add(column, button);
            parent.Add(button);
        }

        private void SetSummarySort(SummaryColumn column)
        {
            summaryDescending = summarySort != column || !summaryDescending;
            summarySort = column;
            SortSummary();
        }

        private static double? SummaryValue(JObject item, SummaryColumn column)
        {
            if (column == SummaryColumn.Count) return item.Value<double?>("count");
            if (column == SummaryColumn.Gpu) return item.Value<double?>("gpuTotalMs");
            if (column == SummaryColumn.CpuPrepare) return item.Value<double?>("cpuPrepareMs");
            if (column == SummaryColumn.Allocations) return item.Value<double?>("gcAllocationCount");
            if (column == SummaryColumn.SelfAllocations) return item.Value<double?>("gcSelfAllocationCount");
            if (item.Value<int?>("completedCount") <= 0 || !IsTimed(item) ||
                column == SummaryColumn.Self && item.Value<string>("kind") != "span") return null;
            string key = column == SummaryColumn.Self ? "selfMs" : column == SummaryColumn.Max ? "maxMs" : "totalMs";
            return item.Value<double?>(key);
        }

        private void SortSummary()
        {
            summary.Sort((a, b) =>
            {
                double? av = SummaryValue(a, summarySort), bv = SummaryValue(b, summarySort);
                if (av.HasValue != bv.HasValue) return av.HasValue ? -1 : 1;
                int order = av.HasValue ? av.Value.CompareTo(bv.Value) : 0;
                if (order != 0) return summaryDescending ? -order : order;
                order = string.Compare(a.Value<string>("phase"), b.Value<string>("phase"), StringComparison.Ordinal);
                if (order != 0) return order;
                order = string.Compare(a.Value<string>("kind"), b.Value<string>("kind"), StringComparison.Ordinal);
                if (order != 0) return order;
                order = string.Compare(a.Value<string>("documentId"), b.Value<string>("documentId"), StringComparison.Ordinal);
                if (order != 0) return order;
                order = string.Compare(a.Value<string>("layerId"), b.Value<string>("layerId"), StringComparison.Ordinal);
                if (order != 0) return order;
                order = (a.Value<int?>("fxIndex") ?? -1).CompareTo(b.Value<int?>("fxIndex") ?? -1);
                return order != 0 ? order : string.Compare(a.Value<string>("fxName"), b.Value<string>("fxName"), StringComparison.Ordinal);
            });
            summaryScale = 0;
            foreach (var item in summary) summaryScale = Math.Max(summaryScale, SummaryValue(item, SummaryColumn.Total) ?? 0);
            foreach (var pair in summaryHeaders)
            {
                bool active = pair.Key == summarySort;
                pair.Value.text = SummaryTitle(pair.Key) + (active ? summaryDescending ? " ▼" : " ▲" : "");
                pair.Value.EnableInClassList("whimtex-profiler-sort-active", active);
                pair.Value.tooltip = "Sort by " + SummaryTitle(pair.Key) + (active && summaryDescending ? ": lowest first." : ": highest first.") +
                    " Click again to reverse. Unavailable values stay last." +
                    (pair.Key == SummaryColumn.Allocations || pair.Key == SummaryColumn.SelfAllocations ?
                        "\nMain-thread managed allocation count, not bytes. " +
                        (pair.Key == SummaryColumn.Allocations ? "Includes nested stages." : "Excludes nested stages.") +
                        " * = partial measurement; sorted by the measured count, without extrapolation." : "");
            }
            summaryList.RefreshItems();
        }

        private JObject Request(string action, bool includeCapture = false)
        {
            var request = new JObject { ["apiVersion"] = 1, ["action"] = action };
            if (includeCapture) request["captureId"] = captureId;
            return request;
        }

        private bool Call(JObject request, out JObject response, bool quiet = false)
        {
            response = null;
            try
            {
                response = JObject.Parse(WhimTexApi.ProfileJson(request.ToString(Formatting.None)));
                if (response.Value<int?>("apiVersion") != 1)
                    throw new InvalidOperationException("Unsupported profiler API version.");
                ApplyBaseline(response);
                if (response.Value<bool?>("success") != true)
                {
                    ShowMessage((response.Value<string>("errorCode") ?? "profile_failed") + ": " +
                        (response.Value<string>("error") ?? "The request failed.") + " Press Refresh to check the current capture.", true);
                    return false;
                }
                if (!quiet) message.AddToClassList("whimtex-profiler-hidden");
                return true;
            }
            catch (Exception error)
            {
                ShowMessage(error.Message, true);
                return false;
            }
        }

        private void ApplyCapture(JObject response)
        {
            var capture = response["capture"] as JObject;
            if (captureId != capture?.Value<string>("captureId") || recording != (capture?.Value<bool?>("recording") == true) ||
                openSpans != (capture?.Value<int?>("openSpans") ?? 0) || pendingGpu != (capture?.Value<int?>("pendingGpuSamples") ?? 0))
                InvalidateComparison();
            captureId = capture?.Value<string>("captureId");
            recording = capture?.Value<bool?>("recording") == true;
            openSpans = capture?.Value<int?>("openSpans") ?? 0;
            pendingGpu = capture?.Value<int?>("pendingGpuSamples") ?? 0;
            gpuAvailability = capture?.Value<string>("gpuAvailability") ?? "stopped";
            ApplyGpuEnvironment(capture, response["baseline"] as JObject);
            if (string.IsNullOrEmpty(captureId))
            {
                status.text = "No retained capture. Recording is off.";
                status.tooltip = "";
            }
            else
            {
                string state = recording ? "Recording" : "Stopped (" + (capture.Value<string>("stopReason") ?? "—") + ")";
                string document = capture.Value<string>("documentId");
                status.text = $"{state} · {capture["eventCount"]}/{capture["capacity"]} events · " +
                    $"{Milliseconds(capture, "elapsedMs")} ms · {capture["droppedEvents"]} dropped · {capture["openSpans"]} open · GPU {capture["gpuAvailability"]}, {pendingGpu} pending";
                status.tooltip = $"Capture {captureId}\nDocument {(string.IsNullOrEmpty(document) ? "all" : document)}\nActive status updates every 250 ms; idle baseline status every second. Changed results update at most twice per second. Refresh updates immediately.";
            }
            status.EnableInClassList("whimtex-profiler-recording", recording);
            ApplyMemory(capture);
            UpdateButtons();
        }

        private void ApplyGpuEnvironment(JObject capture, JObject baseline)
        {
            var threading = SystemInfo.renderingThreadingMode;
            bool? active = WhimTexProfiler.GraphicsJobsActive(threading);
            bool configured = PlayerSettings.graphicsJobs;
            gpuEnvironment.text = "Graphics Jobs: " + (active.HasValue ? active.Value ? "On (active)" : "Off (active)" : "Unknown") +
                " · " + threading + (active.HasValue && active.Value != configured
                    ? " · Configured " + (configured ? "On" : "Off") + " (restart required)" : "");
            gpuEnvironment.tooltip = "Actual running render device; changing Player Settings takes effect after restarting Unity.\n" +
                "Capture: " + (capture?.Value<string>("renderingThreadingMode") ?? "not recorded") +
                "\nBaseline: " + (baseline?.Value<string>("renderingThreadingMode") ?? "not recorded");
            bool capturedJobs = capture?.Value<bool?>("graphicsJobs") == true;
            bool baselineJobs = baseline?.Value<bool?>("graphicsJobs") == true;
            gpuWarning.text = WhimTexProfiler.GpuUnconfirmedWarning +
                (capturedJobs || baselineJobs ? " Recorded with Graphics Jobs: " +
                    (capturedJobs && baselineJobs ? "current capture and baseline." : capturedJobs ? "current capture." : "baseline.") : "");
            gpuWarning.EnableInClassList("whimtex-profiler-hidden", active != true && !capturedJobs && !baselineJobs);
        }

        private static void MarkUnconfirmedGpu(Label cell, bool unconfirmed)
        {
            cell.EnableInClassList("whimtex-profiler-gpu-unconfirmed", unconfirmed);
            if (unconfirmed && !cell.tooltip.Contains(WhimTexProfiler.GpuUnconfirmedWarning))
                cell.tooltip += "\n" + WhimTexProfiler.GpuUnconfirmedWarning;
        }

        private void SelectEvent(IEnumerable<object> selection)
        {
            ClearDetail();
            foreach (object item in selection)
            {
                selectedEvent = item as JObject;
                if (selectedEvent == null) continue;
                operation.text = $"Operation {selectedEvent["operationId"]} / event {selectedEvent["id"]}";
                detailTitle.text = selectedEvent.Value<string>("phase") + "  ·  " + LayerName(selectedEvent);
                string fx = selectedEvent.Value<int?>("fxIndex") >= 0 ? selectedEvent["fxIndex"].ToString() : "—";
                detailInfo.text = $"Wall {Milliseconds(selectedEvent, "durationMs")} ms   ·   Self {Milliseconds(selectedEvent, "selfMs")} ms   ·   " +
                    $"Start +{Milliseconds(selectedEvent, "startedMs")} ms   ·   Depth {selectedEvent["depth"]}\n" +
                    $"CPU prepare {Milliseconds(selectedEvent, "cpuPrepareMs")} ms   ·   CPU submit {Milliseconds(selectedEvent, "cpuSubmitMs")} ms   ·   GPU {Milliseconds(selectedEvent, "gpuMs")} ms ({selectedEvent["gpuStatus"]})\n" +
                    $"{Kind(selectedEvent)}   ·   Group {selectedEvent["group"]}   ·   Change {selectedEvent["changeId"]}   ·   FX {fx}\n" +
                    (selectedEvent.Value<long?>("changeEventId") > 0
                        ? $"Latest change: {selectedEvent["changeReason"]} · {selectedEvent["changeOrigin"]} · {selectedEvent["changedLayerName"]} (event {selectedEvent["changeEventId"]})\n" : "") +
                    (selectedEvent.Value<string>("resourceKey") is string key ? "Cache entry: " + key + "\n" : "") +
                    (selectedEvent.Value<string>("kind") == "reason" ? "Occurrences: " + selectedEvent["value"] + "\n" : "") +
                    (selectedEvent.Value<string>("detail") ?? "");
                detail.SetValueWithoutNotify(selectedEvent.ToString(Formatting.Indented));
                ApplyEventDiagnostics(selectedEvent);
                inspectOperation.SetEnabled(selectedEvent.Value<long?>("operationId") > 0);
                break;
            }
        }

        private void InspectOperation()
        {
            long? selectedOperation = selectedEvent?.Value<long?>("operationId");
            if (!selectedOperation.HasValue || selectedOperation.Value <= 0) return;
            operationId = selectedOperation;
            ReadPage(0);
        }

        private void ClearDetail()
        {
            selectedEvent = null;
            operation.text = operationId.HasValue ? "Operation " + operationId.Value + " — select an event" : "Selected operation / event";
            detailTitle.text = "No event selected";
            detailInfo.text = "Select a row above. Layer names are captured at recording time; IDs remain in raw data and tooltips.";
            detail.SetValueWithoutNotify("Select an event to inspect its operation, parent, change, layer, FX and timing fields.");
            inspectOperation.SetEnabled(false);
            ApplyEventDiagnostics(null);
        }

        private void ResetRows()
        {
            InvalidateComparison();
            loadedCount = -1;
            collapsedEvents.Clear();
            offset = 0;
            nextOffset = null;
            appliedLayer = null;
            appliedPhase = null;
            UpdateGroupControls();
            eventList.ClearSelection();
            events.Clear();
            summary.Clear();
            eventList.RefreshItems();
            SortSummary();
            page.text = "No events loaded";
            rootWall.text = "All matching events · click a column to sort";
            eventCount.text = "Event tree · bars = share of operation";
            ClearDetail();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasCapture = !string.IsNullOrEmpty(captureId);
            bool finished = !recording && openSpans == 0;
            start.SetEnabled(finished);
            captureSettings.SetEnabled(!recording);
            stop.SetEnabled(hasCapture && recording);
            clear.SetEnabled(hasCapture && finished);
            export.SetEnabled(hasCapture && finished && pendingGpu == 0);
            previous.SetEnabled(hasCapture && offset > 0);
            next.SetEnabled(hasCapture && nextOffset.HasValue);
            allOperations.SetEnabled(hasCapture && operationId.HasValue);
            UpdateComparisonControls();
        }

        private void ShowMessage(string text, bool error)
        {
            message.text = text;
            message.messageType = error ? HelpBoxMessageType.Error : HelpBoxMessageType.Info;
            message.RemoveFromClassList("whimtex-profiler-hidden");
        }

        private static ListView MakeList(List<JObject> items, Func<VisualElement> make, Action<VisualElement, int> bind, SelectionType selection)
        {
            var list = new ListView(items, 26, make, bind)
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = selection, reorderable = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly
            };
            list.AddToClassList("whimtex-profiler-list");
            return list;
        }

        private VisualElement MakeEventRow()
        {
            var row = Row(null);
            row.AddToClassList("whimtex-profiler-data-row");
            var phase = Row(row);
            phase.AddToClassList("whimtex-profiler-phase");
            for (int i = 0; i < 8; i++)
            {
                var indent = new VisualElement { name = "indent-" + i, pickingMode = PickingMode.Ignore };
                indent.AddToClassList("whimtex-profiler-indent");
                phase.Add(indent);
            }
            var fold = new Button { name = "branch-toggle", focusable = false };
            fold.AddToClassList("whimtex-profiler-branch-toggle");
            fold.clicked += () =>
            {
                if (fold.userData is int id) SetBranchCollapsed(id, !collapsedEvents.Contains(id));
            };
            phase.Add(fold);
            Cell(phase, "", "phase-name");
            Cell(row, "", "layer");
            Cell(row, "", "kind");
            DurationCell(row);
            Cell(row, "", "number").name = "prepare";
            Cell(row, "", "number").name = "gpu";
            return row;
        }

        private void BindEvent(VisualElement row, int index)
        {
            JObject item = events[index];
            SetTone(row, Category(item));
            int depth = item.Value<int?>("depth") ?? 0;
            for (int i = 0; i < 8; i++) row.Q("indent-" + i).EnableInClassList("whimtex-profiler-hidden", i >= depth);
            bool hasChildren = item.Value<bool?>("hasChildren") == true;
            bool folded = item.Value<bool?>("collapsed") == true;
            var fold = row.Q<Button>("branch-toggle");
            fold.userData = item.Value<int>("id");
            fold.text = folded ? "▸" : "▾";
            fold.tooltip = folded ? "Expand stage (Right arrow)" : "Collapse stage (Left arrow)";
            fold.SetEnabled(hasChildren);
            fold.EnableInClassList("whimtex-profiler-leaf-toggle", !hasChildren);
            SetCell(row, "phase-name", (depth > 8 ? "… " : "") + FxTitle(item, false));
            row.EnableInClassList("whimtex-profiler-context-row", item.Value<bool?>("contextOnly") == true);
            SetCell(row, "layer", LayerName(item));
            row.Q<Label>("layer").tooltip = LayerName(item) + "\nID: " + (item.Value<string>("layerId") ?? "—");
            SetCell(row, "kind", Kind(item));
            bool timed = IsTimed(item);
            SetDuration(row, timed ? item.Value<double?>("durationMs") : null,
                item.Value<double?>("operationMs"), "Share of complete operation wall time; inclusive spans overlap.");
            SetCell(row, "prepare", Milliseconds(item, "cpuPrepareMs"));
            SetCell(row, "gpu", item.Value<string>("gpuStatus") == "pending" ? "…" : Milliseconds(item, "gpuMs"));
            row.Q<Label>("gpu").tooltip = "GPU: " + item.Value<string>("gpuStatus") + ". Delayed results update automatically.\n" + (item.Value<string>("gpuDetail") ?? "");
            MarkUnconfirmedGpu(row.Q<Label>("gpu"), item.Value<string>("gpuStatus") == "unconfirmed");
            row.EnableInClassList("whimtex-profiler-root-event", depth == 0);
            row.tooltip = $"Event {item["id"]} · operation {item["operationId"]} · depth {depth}\n" +
                "Group: " + item.Value<string>("group") + "\n" +
                (item.Value<bool?>("contextOnly") == true ? "Ancestor shown for filter context; not included in the matching summary.\n" : "") +
                (item.Value<string>("detail") ?? "");
        }

        private void ExpandAllBranches()
        {
            collapsedEvents.Clear();
            ReadPage(0);
        }

        private void CollapseAllBranches()
        {
            RefreshCapture();
            var capture = WhimTexProfiler.Current;
            if (capture == null || capture.id != captureId) return;
            collapsedEvents.Clear();
            for (int i = 0; i < capture.count; i++)
            {
                int parent = (int)capture.events[i].parentId;
                if (parent > 0) collapsedEvents.Add(parent);
            }
            ReadPage(0);
        }

        private void SetBranchCollapsed(int id, bool collapsed)
        {
            if (!events.Exists(item => item.Value<int>("id") == id && item.Value<bool?>("hasChildren") == true)) return;
            if (collapsed) collapsedEvents.Add(id); else collapsedEvents.Remove(id);
            ReadPage(offset);
            int index = events.FindIndex(item => item.Value<int>("id") == id);
            if (index >= 0) eventList.SetSelection(index);
        }

        private void OnTreeKey(KeyDownEvent evt)
        {
            if (selectedEvent?.Value<bool?>("hasChildren") != true ||
                evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow) return;
            SetBranchCollapsed(selectedEvent.Value<int>("id"), evt.keyCode == KeyCode.LeftArrow);
            evt.StopImmediatePropagation();
        }

        private static VisualElement MakeSummaryRow()
        {
            var row = Row(null);
            row.AddToClassList("whimtex-profiler-data-row");
            Cell(row, "", "phase");
            Cell(row, "", "count");
            DurationCell(row);
            Cell(row, "", "number").name = "self";
            Cell(row, "", "number").name = "max";
            Cell(row, "", "number").name = "prepare";
            Cell(row, "", "number").name = "gpu";
            Cell(row, "", "allocations");
            Cell(row, "", "self-allocations");
            return row;
        }

        private void BindSummary(VisualElement row, int index)
        {
            JObject item = summary[index];
            SetTone(row, Category(item));
            SetCell(row, "phase", FxTitle(item, true) + " / " + item["kind"]);
            SetCell(row, "count", item["count"]?.ToString());
            SetDuration(row, IsTimed(item) && item.Value<int?>("completedCount") > 0 ? item.Value<double?>("totalMs") : null,
                summaryScale, "Relative to the largest total across all matching summary rows (not just this page). Rows overlap.");
            SetCell(row, "self", item.Value<string>("kind") == "span" && item.Value<int?>("completedCount") > 0 ? Milliseconds(item, "selfMs") : "—");
            SetCell(row, "max", IsTimed(item) && item.Value<int?>("completedCount") > 0 ? Milliseconds(item, "maxMs") : "—");
            SetCell(row, "prepare", Milliseconds(item, "cpuPrepareMs"));
            bool partialGpu = (item.Value<int?>("gpuPendingCount") ?? 0) + (item.Value<int?>("gpuUnavailableCount") ?? 0) > 0;
            SetCell(row, "gpu", item.Value<double?>("gpuTotalMs").HasValue ? Milliseconds(item, "gpuTotalMs") + (partialGpu ? "*" : "") :
                item.Value<int?>("gpuPendingCount") > 0 ? "…" : "—");
            row.Q<Label>("gpu").tooltip = $"GPU: {item["gpuCount"]} measured ({item.Value<int?>("gpuUnconfirmedCount") ?? 0} unconfirmed), {item["gpuPendingCount"]} pending, {item["gpuUnavailableCount"]} unavailable. Max {Milliseconds(item, "gpuMaxMs")} ms.";
            MarkUnconfirmedGpu(row.Q<Label>("gpu"), item.Value<int?>("gpuUnconfirmedCount") > 0);
            BindSummaryAllocations(row, item, "allocations", "gcAllocationCount", "Includes nested stages.");
            BindSummaryAllocations(row, item, "self-allocations", "gcSelfAllocationCount", "Excludes nested stages.");
            row.tooltip = $"Group: {item["group"]}. {item["completedCount"]} completed of {item["count"]}. Timings include completed events only; rows may overlap.";
        }

        private static void BindSummaryAllocations(VisualElement row, JObject item, string name, string key, string scope)
        {
            long? value = item.Value<long?>(key);
            bool partial = item.Value<bool?>("partial") == true;
            SetCell(row, name, value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + (partial ? "*" : "") : "—");
            row.Q<Label>(name).tooltip = "Main-thread managed allocation count, not bytes. " + scope +
                $"\nMeasured calls: {item.Value<int?>("measuredCalls") ?? 0} / {item.Value<int?>("eligibleCalls") ?? 0}." +
                (value.HasValue ? partial ? " Partial sum; unmeasured calls are excluded, not estimated." : " Sum across measured calls." :
                    " Allocation count unavailable; not zero.");
        }

        private static string Milliseconds(JObject item, string key)
        {
            double? value = item.Value<double?>(key);
            return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "—";
        }

        private static string FxTitle(JObject item, bool includeLayer)
        {
            string phase = item.Value<string>("phase");
            string name = item.Value<string>("fxName");
            int index = item.Value<int?>("fxIndex") ?? -1;
            if (index >= 0 && !string.IsNullOrEmpty(name)) phase += " · " + (index + 1) + ": " + name;
            if (includeLayer && !string.IsNullOrEmpty(item.Value<string>("layerId"))) phase += " · " + LayerName(item);
            return phase;
        }

        private static void SetCell(VisualElement row, string name, string text)
        {
            var cell = row.Q<Label>(name);
            cell.text = text ?? "";
            cell.tooltip = cell.text;
        }

        private static VisualElement Row(VisualElement parent, bool controls = false)
        {
            var row = new VisualElement();
            row.AddToClassList("whimtex-profiler-row");
            if (controls) row.AddToClassList("whimtex-profiler-controls");
            parent?.Add(row);
            return row;
        }

        private static Label Cell(VisualElement row, string text, string column)
        {
            var cell = new Label(text) { name = column, enableRichText = false };
            cell.AddToClassList("whimtex-profiler-cell");
            cell.AddToClassList("whimtex-profiler-" + column);
            row.Add(cell);
            return cell;
        }

        private static Label Heading(VisualElement parent, string text, string subtitle)
        {
            var row = Row(parent);
            row.AddToClassList("whimtex-profiler-heading");
            var title = new Label(text);
            title.AddToClassList("whimtex-profiler-heading-title");
            row.Add(title);
            var hint = new Label(subtitle);
            hint.AddToClassList("whimtex-profiler-heading-hint");
            row.Add(hint);
            return hint;
        }

        private static TwoPaneSplitView Split(string name, int fixedPane, float size)
        {
            var split = new TwoPaneSplitView(fixedPane, size, TwoPaneSplitViewOrientation.Vertical) { name = name };
            split.AddToClassList("whimtex-profiler-split");
            WhimTexUI.StyleSplitView(split);
            return split;
        }

        private static VisualElement Pane(VisualElement parent, string name)
        {
            var pane = new VisualElement { name = name };
            pane.AddToClassList("whimtex-profiler-pane");
            parent.Add(pane);
            return pane;
        }

        private static VisualElement Columns(VisualElement parent)
        {
            var holder = Row(parent);
            holder.AddToClassList("whimtex-profiler-table-header");
            var row = Row(holder);
            row.AddToClassList("whimtex-profiler-columns");
            return row;
        }

        private static void AlignColumns(VisualElement header, ListView list)
        {
            var scroll = list.Q<ScrollView>();
            var gutter = new VisualElement { pickingMode = PickingMode.Ignore };
            gutter.AddToClassList("whimtex-profiler-scrollbar-gutter");
            header.parent.Add(gutter);
            scroll.verticalScroller.AddToClassList("whimtex-profiler-table-scroller");
            void Sync() => gutter.EnableInClassList("whimtex-profiler-hidden",
                scroll.verticalScroller.resolvedStyle.display == DisplayStyle.None);
            scroll.verticalScroller.RegisterCallback<GeometryChangedEvent>(_ => Sync());
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Sync());
            list.RegisterCallback<AttachToPanelEvent>(_ => Sync());
            Sync();
        }

        private static void AddLegend(VisualElement parent, string text, string category)
        {
            var label = new Label(text);
            label.AddToClassList("whimtex-profiler-legend-item");
            SetTone(label, category);
            parent.Add(label);
        }

        private static void SetTone(VisualElement row, string category)
        {
            if (row.userData is string previous) row.RemoveFromClassList("whimtex-profiler-tone-" + previous);
            row.userData = category;
            row.AddToClassList("whimtex-profiler-tone-" + category);
        }

        private static string Category(JObject item)
        {
            string phase = item.Value<string>("phase") ?? "";
            string kind = item.Value<string>("kind") ?? "";
            string info = item.Value<string>("detail") ?? "";
            if (kind == "error") return "error";
            if (kind == "cache") return info.StartsWith("hit", StringComparison.Ordinal) ? "hit" :
                info.StartsWith("miss", StringComparison.Ordinal) ? "miss" : "other";
            return item.Value<string>("group") ?? WhimTexProfiler.GroupId(WhimTexProfiler.GroupFor(phase, kind));
        }

        private static string Kind(JObject item)
        {
            if (item.Value<bool?>("interrupted") == true) return "Interrupted";
            if (item.Value<bool?>("completed") == false) return "Open";
            string kind = item.Value<string>("kind");
            string info = item.Value<string>("detail") ?? "";
            if (kind == "cache") return info.StartsWith("hit", StringComparison.Ordinal) ? "Hit" :
                info.StartsWith("miss", StringComparison.Ordinal) ? "Miss" : "Cache";
            return kind == "span" ? "Span" : kind == "phase" ? "Phase" : kind == "change" ? "Change" : kind ?? "—";
        }

        private static string LayerName(JObject item) => string.IsNullOrEmpty(item.Value<string>("layerId")) ? "—" :
            string.IsNullOrEmpty(item.Value<string>("layerName")) ? "Unnamed layer" : item.Value<string>("layerName");

        private static bool IsTimed(JObject item) => item.Value<string>("kind") == "span" || item.Value<string>("kind") == "phase";

        private static void DurationCell(VisualElement row)
        {
            var cell = Row(row);
            cell.AddToClassList("whimtex-profiler-duration");
            var bar = new ProgressBar { name = "duration-bar", lowValue = 0, highValue = 100, title = "", pickingMode = PickingMode.Ignore };
            bar.AddToClassList("whimtex-profiler-bar");
            cell.Add(bar);
            Cell(cell, "", "duration-value");
        }

        private static void SetDuration(VisualElement row, double? value, double? scale, string explanation)
        {
            var bar = row.Q<ProgressBar>("duration-bar");
            bool known = value.HasValue && scale.HasValue && scale.Value > 0;
            double ratio = known ? value.Value / scale.Value : 0;
            bar.value = (float)(Math.Max(0, Math.Min(1, ratio)) * 100);
            bar.EnableInClassList("whimtex-profiler-bar-unavailable", !known);
            SetCell(row, "duration-value", value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "—");
            bar.parent.tooltip = explanation + (known ? "\n" + (ratio * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "\nNo completed timing / operation denominator.");
        }

        private static void AddField(VisualElement parent, VisualElement field)
        {
            field.AddToClassList("whimtex-profiler-field");
            parent.Add(field);
        }

        private static Button AddButton(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text };
            parent.Add(button);
            return button;
        }
    }
}
