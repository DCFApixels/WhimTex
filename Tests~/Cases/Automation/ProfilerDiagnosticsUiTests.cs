using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = ProfilerAnalysisTests.Json;

// Support: Cases/Automation/ProfilerAnalysisTests.cs, Framework/TestApi.cs.
public static class ProfilerDiagnosticsUiTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Hidden = "whimtex-profiler-hidden";
    const string ReloadKey = "WhimTex.Profiler.ReloadSnapshot";
    const string BaselineKey = "WhimTex.Profiler.BaselineSnapshot";
    const string BenchmarkKey = "WhimTex.Profiler.BenchmarkResult";
    static readonly Assembly Package = typeof(WhimTexDocument).Assembly;
    static readonly Type WindowType = Package.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true);
    static readonly Type Profiler = Package.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly FieldInfo Current = Profiler.GetField("capture", Static);
    static readonly FieldInfo Baseline = Profiler.GetField("<Baseline>k__BackingField", Static);
    static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    static object Invoke(EditorWindow window, string method, params object[] args) => WindowType.GetMethod(method, Instance).Invoke(window, args);
    static object Raw(string text) => WindowType.GetMethod("ApplyMemory", Instance).GetParameters()[0].ParameterType
        .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
    static Label Label(EditorWindow window, string name) => window.rootVisualElement.Q<Label>(name);
    static void Memory(EditorWindow window, string json) => Invoke(window, "ApplyMemory", json == null ? null : Raw(json));
    static void Restored<T>(TestContext t, EditorWindow window, string field, T expected, string serialized)
    {
        var actual = (T)Get(window, field);
        t.Equal(expected, actual, field + " round-trips through EditorJsonUtility" +
            (Equals(expected, actual) ? "" : "\nSaved Editor JSON:\n" + serialized));
    }

    public static string Run()
    {
        object savedCurrent = Current.GetValue(null), savedBaseline = Baseline.GetValue(null);
        if (savedCurrent != null && ((bool)Get(savedCurrent, "recording") || (int)Get(savedCurrent, "open") != 0 ||
            (int)Profiler.GetMethod("PendingGpu", Static).Invoke(null, new[] { savedCurrent }) != 0))
            return TestContext.Result("skipped", 0, "Requires an idle profiler; user capture and baseline are untouched.").ToJson();
        return TestContext.Run("Profiler diagnostics UI: memory, causes, bounded controls and retained state", t =>
        {
            string absent = "absent-" + Guid.NewGuid().ToString("N");
            string reload = SessionState.GetString(ReloadKey, absent), baseline = SessionState.GetString(BaselineKey, absent);
            string benchmarkToken = SessionState.GetString(BenchmarkKey, absent);
            var focus = EditorWindow.focusedWindow;
            EditorWindow window = null;
            try
            {
                window = (EditorWindow)ScriptableObject.CreateInstance(WindowType);
                window.titleContent = new GUIContent("Profiler diagnostics UI check");
                window.ShowUtility();
                Invoke(window, "CreateGUI");
                t.True(window.rootVisualElement.panel != null, "Owned utility window attaches controls to a real event-dispatch panel");
                CheckLifecycle(t, window);
                CheckMemory(t, window);
                CheckCauses(t, window);
                CheckBenchmarks(t, window);
                CheckPresentationState(t, window);
            }
            finally
            {
                try { if (window != null) window.Close(); }
                finally { if (focus != null && EditorWindow.focusedWindow != focus) focus.Focus(); }
            }
            t.True(ReferenceEquals(savedCurrent, Current.GetValue(null)), "Current capture reference untouched");
            t.True(ReferenceEquals(savedBaseline, Baseline.GetValue(null)), "Baseline reference untouched");
            t.Equal(reload, SessionState.GetString(ReloadKey, absent), "Current snapshot token untouched, including absence");
            t.Equal(baseline, SessionState.GetString(BaselineKey, absent), "Baseline snapshot token untouched, including absence");
            t.Equal(benchmarkToken, SessionState.GetString(BenchmarkKey, absent), "Benchmark snapshot token untouched, including absence");
            if (focus != null) t.Equal(focus, EditorWindow.focusedWindow, "Original focused window preserved");
        });
    }

    static void CheckLifecycle(TestContext t, EditorWindow window)
    {
        var root = window.rootVisualElement;
        t.True(!root.Q<Foldout>("diagnostics-panel").value, "Diagnostics initially collapsed");
        root.Q<Foldout>("diagnostics-panel").value = true;
        t.True((bool)Get(window, "diagnosticsExpanded"), "Foldout updates serialized view state");
        root.Q<Foldout>("benchmark-method").value = true;
        root.Q<Foldout>("benchmark-method").value = false;
        t.True((bool)Get(window, "diagnosticsExpanded"), "Nested method foldout cannot overwrite parent expanded state");
        string saved = EditorJsonUtility.ToJson(window);
        root.Q<Foldout>("diagnostics-panel").value = false;
        t.True(!(bool)Get(window, "diagnosticsExpanded"), "Expanded state deliberately changed before deserialization");
        EditorJsonUtility.FromJsonOverwrite(saved, window);
        Restored(t, window, "diagnosticsExpanded", true, saved);
        object oldSchedule = Get(window, "automaticRefresh");
        Invoke(window, "CreateGUI");
        t.True(root.panel != null, "Repeated CreateGUI keeps the event-dispatch panel attached");
        t.True(root.Q<Foldout>("diagnostics-panel").value, "Recreated UI restores expanded state");
        t.Equal(1, root.Query<Foldout>("diagnostics-panel").ToList().Count, "CreateGUI twice creates one diagnostics panel");
        t.Equal(1, root.Query<Button>("explain-cause").ToList().Count, "CreateGUI twice creates one explain action");
        t.True(!ReferenceEquals(oldSchedule, Get(window, "automaticRefresh")), "Recreation replaces the refresh item");
        t.True(root.Q<ScrollView>("diagnostics-scroll").ClassListContains("whimtex-profiler-diagnostics-scroll"), "Panel uses bounded USS scroll container");
        t.True(root.Q<ScrollView>("cause-scroll").ClassListContains("whimtex-profiler-cause-scroll"), "Cause result has its own bounded USS container");
        t.Equal(1, root.Query<Button>("benchmark-run").ToList().Count, "Recreation creates one explicit Run control");
        t.Equal(1, root.Query<Button>("benchmark-cancel").ToList().Count, "Recreation creates one explicit Cancel control");
    }

    static void CheckMemory(TestContext t, EditorWindow window)
    {
        Memory(window, null);
        t.Equal("No retained capture.", Label(window, "memory-overview").text, "Missing capture explicit");
        Memory(window, "{\"captureId\":\"old\",\"memory\":null}");
        t.True(Label(window, "memory-overview").text.Contains("unavailable"), "Null memory supported");
        Memory(window, "{}");
        t.True(Label(window, "memory-overview").text.Contains("unavailable"), "Absent memory supported");
        Memory(window, "{\"causeTrackingLimited\":true,\"memory\":null}");
        t.True(Label(window, "cause-coverage").text.Contains("partial") && !Label(window, "cause-coverage").ClassListContains(Hidden), "Cause tracking limit visible independently of memory availability");
        Memory(window, "{\"memory\":{\"editorManagedAllocatedBytes\":0,\"editorManagedAllocationAvailable\":true," +
            "\"editorManagedAllocationLimited\":false,\"editorManagedAllocationScope\":\"all_editor_threads\",\"allocationCountLimited\":false," +
            "\"trackedTextureBytes\":1024,\"peakTrackedTextureBytes\":1048576," +
            "\"acquiredBytes\":2048,\"releasedBytes\":512,\"observedBytes\":4096,\"acquireCount\":2,\"releaseCount\":1," +
            "\"nativeCacheBytes\":8192,\"peakNativeCacheBytes\":16384,\"processUsedBytes\":1073741824," +
            "\"processReservedBytes\":2147483648,\"peakProcessUsedBytes\":1610612736,\"trackingLimited\":true,\"coverage\":\"Measured coverage\"}}");
        string overview = Label(window, "memory-overview").text;
        t.Equal("Managed allocated · all Editor threads 0 B", Label(window, "memory-managed").text, "Measured zero bytes explicitly covers all Editor threads on its own line");
        t.True(!overview.Contains("Managed") && overview.Contains("1 KiB") && overview.Contains("1 MiB"), "Texture payload uses a separate line with binary byte units");
        string managedTooltip = Label(window, "memory-managed").tooltip;
        t.True(managedTooltip.Contains("unrelated Editor work and instrumentation") && managedTooltip.Contains("Document and display filters") &&
            managedTooltip.Contains("not live heap"), "Editor allocation coverage and exclusions explicit");
        t.True(Label(window, "memory-traffic").text.Contains("Acquired 2 KiB (2)") &&
            Label(window, "memory-traffic").text.Contains("first observed 4 KiB"), "Observed resources are separate from acquisitions");
        t.True(Label(window, "memory-native").text.Contains("8 KiB") && Label(window, "memory-native").text.Contains("16 KiB"), "Native cache and peak displayed");
        t.True(Label(window, "memory-process").text.Contains("Unity allocated 1 GiB") &&
            Label(window, "memory-process").text.Contains("peak allocated 1.5 GiB"), "Unity-wide memory and sampled peak labeled");
        t.True(Label(window, "memory-process").tooltip.Contains("not document memory or OS resident"), "Process scope caveat explicit");
        t.True(Label(window, "memory-coverage").text.Contains("filters do not apply"), "Whole-capture scope remains visible");
        t.True(Label(window, "memory-coverage").text.Contains("Tracking limit reached"), "Tracking limit visible");
        t.Equal("Measured coverage", Label(window, "memory-coverage").tooltip, "Full backend coverage is available without a large paragraph");
        Memory(window, "{\"memory\":{\"editorManagedAllocatedBytes\":null,\"editorManagedAllocationAvailable\":false,\"trackedTextureBytes\":0,\"peakTrackedTextureBytes\":-1}}");
        t.Equal("Managed allocated · all Editor threads —", Label(window, "memory-managed").text, "Unavailable Editor bytes never imply zero");
        t.True(Label(window, "memory-overview").text.Contains("Tracked textures 0 B") &&
            Label(window, "memory-overview").text.Contains("peak —"), "Unavailable and invalid measurements never imply zero");
        t.True(!Label(window, "memory-coverage").ClassListContains("whimtex-profiler-diagnostics-warning"), "Old warning styling reset");
        Memory(window, "{\"memory\":{\"editorManagedAllocatedBytes\":4096,\"editorManagedAllocationAvailable\":true,\"editorManagedAllocationLimited\":true}}");
        t.Equal("Managed allocated · all Editor threads — · recorder limit reached", Label(window, "memory-managed").text,
            "Limited Editor bytes are unavailable even when a partial numeric total is supplied");
        t.True(Label(window, "memory-managed").ClassListContains("whimtex-profiler-diagnostics-warning"), "Editor recorder limit highlighted");
        Memory(window, "{\"memory\":{\"editorManagedAllocatedBytes\":4096,\"editorManagedAllocationAvailable\":true,\"allocationCountLimited\":true}}");
        t.Equal("Managed allocated · all Editor threads 4 KiB", Label(window, "memory-managed").text, "Stage count limit does not hide independently measured Editor bytes");
        t.True(Label(window, "memory-coverage").text.Contains("Stage allocation counts limited; affected scopes unavailable"), "Stage count recorder limit explicit");
        t.True(Label(window, "memory-coverage").ClassListContains("whimtex-profiler-diagnostics-warning") &&
            !Label(window, "memory-managed").ClassListContains("whimtex-profiler-diagnostics-warning"), "Stage and Editor recorder warnings remain independent");
        Memory(window, "{\"memory\":{\"editorManagedAllocatedBytes\":4096,\"editorManagedAllocationAvailable\":false}}");
        t.Equal("Managed allocated · all Editor threads —", Label(window, "memory-managed").text, "Unavailable flag wins over a numeric Editor byte value");
        Memory(window, "{\"memory\":{\"editorManagedAllocatedBytes\":-1,\"editorManagedAllocationAvailable\":true}}");
        t.Equal("Managed allocated · all Editor threads —", Label(window, "memory-managed").text, "Negative Editor bytes are unavailable");
        Memory(window, null);
        t.Equal("", Label(window, "memory-managed").text, "Missing capture clears stale Editor allocation total");
        t.True(!Label(window, "memory-coverage").ClassListContains("whimtex-profiler-diagnostics-warning") &&
            !Label(window, "memory-managed").ClassListContains("whimtex-profiler-diagnostics-warning"), "Missing capture clears allocation warnings");
        t.True(Label(window, "cause-coverage").ClassListContains(Hidden), "Missing capture clears stale cause limit");
        t.Equal("", Label(window, "memory-native").text, "Missing capture clears stale native totals");
        t.Equal("", Label(window, "memory-process").text, "Missing capture clears stale process totals");
    }

    static void CheckCauses(TestContext t, EditorWindow window)
    {
        Set(window, "captureId", "diagnostics-ui-capture");
        var item = Raw("{\"id\":12,\"gcAllocationCount\":2048,\"gcSelfAllocationCount\":512,\"causeEventId\":7," +
            "\"causeEventIds\":[7,8],\"causeLayerId\":\"opaque-layer\",\"causeLayerName\":\"Paint input\",\"causeProperty\":\"opacity\",\"causeKind\":\"dependency\"}");
        Set(window, "selectedEvent", item);
        Invoke(window, "ApplyEventDiagnostics", item);
        string detail = Label(window, "event-diagnostics").text;
        t.True(detail.StartsWith("Managed allocation count · main thread · inclusive 2048 · self 512\n", StringComparison.Ordinal),
            "Event inclusive/self allocations use integer counts with main-thread scope, not byte formatting");
        t.True(Label(window, "event-diagnostics").tooltip.Contains("including instrumentation") &&
            Label(window, "event-diagnostics").tooltip.Contains("Counts are not bytes"), "Event count coverage explicit");
        t.True(detail.Contains("Paint input") && detail.Contains("opacity") && detail.Contains("event 7"), "Primary cause, known name and property displayed");
        t.True(!detail.Contains("opaque-layer"), "Opaque IDs stay out of the visible cause label");
        t.True(Label(window, "event-diagnostics").tooltip.Contains("opaque-layer"), "Cause ID retained in tooltip");
        t.True(window.rootVisualElement.Q<Button>("explain-cause").enabledSelf, "Selected captured event can be explained");
        var request = Json.Parse(Invoke(window, "CauseRequest").ToString());
        t.Equal("explain", (string)request["action"], "Explain action explicit");
        t.Equal("diagnostics-ui-capture", (string)request["captureId"], "Explain binds to capture identity");
        t.Equal(12, (int)request["eventId"], "Explain binds to selected event");
        t.Equal(64, (int)request["limit"], "Explain request bounded");
        Invoke(window, "ApplyCauseExplanation", Raw("{\"nodes\":[{\"eventId\":12,\"phase\":\"cache.miss\",\"layerName\":\"Output\",\"causes\":[7,8]}," +
            "{\"eventId\":7,\"phase\":\"edit.observed_change\",\"layerName\":\"Paint input\",\"property\":\"opacity\",\"kind\":\"observed\",\"causes\":[]}]," +
            "\"truncated\":true,\"trackingLimited\":true,\"evidence\":\"Observed edges only\"}"));
        t.True(Label(window, "cause-chain").text.Contains("#7, #8"), "Multiple incoming causes remain visible");
        t.True(Label(window, "cause-chain").text.Contains("Paint input · opacity"), "Cause explanation uses names and properties");
        t.True(Label(window, "cause-evidence").text.Contains("truncated"), "Truncated graph visible");
        t.True(Label(window, "cause-evidence").text.Contains("Tracking limit reached; evidence is partial"), "Limited causal evidence visible");
        t.True(Label(window, "cause-evidence").ClassListContains("whimtex-profiler-diagnostics-warning"), "Limited cause evidence uses existing warning styling");
        t.Equal("Observed edges only", Label(window, "cause-evidence").tooltip, "Backend evidence caveat retained");
        Invoke(window, "ApplyCauseExplanation", Raw("{\"nodes\":[],\"truncated\":false,\"trackingLimited\":true}"));
        t.True(Label(window, "cause-evidence").text.Contains("Tracking limit reached") && !Label(window, "cause-evidence").text.Contains("truncated"),
            "Evidence tracking limit is distinct from result node truncation");
        Invoke(window, "ApplyCauseExplanation", Raw("{\"nodes\":null,\"truncated\":false}"));
        t.Equal("No recorded cause nodes.", Label(window, "cause-chain").text, "Null graph clears old nodes");
        t.True(!Label(window, "cause-evidence").text.Contains("truncated"), "Stale truncation cleared");
        t.True(!Label(window, "cause-evidence").text.Contains("Tracking limit reached") &&
            !Label(window, "cause-evidence").ClassListContains("whimtex-profiler-diagnostics-warning"), "Missing tracking flag clears stale evidence warning");
        Invoke(window, "ApplyEventDiagnostics", Raw("{\"id\":13,\"gcAllocationCount\":null,\"gcSelfAllocationCount\":null}"));
        t.True(Label(window, "event-diagnostics").text.Contains("Recorded cause: unavailable"), "Missing cause never implies no dependency");
        t.True(Label(window, "event-diagnostics").text.Contains("self —"), "Unavailable self allocation explicit");
        t.True(window.rootVisualElement.Q("cause-scroll").ClassListContains(Hidden), "New selection hides old cause explanation");
        t.Equal("", Label(window, "cause-chain").text, "Old explanation text cleared on selection change");
        Invoke(window, "ApplyEventDiagnostics", Raw("{\"id\":14,\"gcAllocationCount\":0,\"gcSelfAllocationCount\":0}"));
        t.True(Label(window, "event-diagnostics").text.Contains("inclusive 0 · self 0"), "Measured zero allocation counts remain zero");
        Invoke(window, "ApplyEventDiagnostics", Raw("{\"id\":15,\"gcAllocationCount\":4294967296,\"gcSelfAllocationCount\":2147483648}"));
        t.True(Label(window, "event-diagnostics").text.Contains("inclusive 4294967296 · self 2147483648"), "Stage counts preserve long values without byte scaling");
        Invoke(window, "ApplyEventDiagnostics", Raw("{\"id\":16}"));
        t.True(Label(window, "event-diagnostics").text.Contains("inclusive — · self —"), "Missing count fields cannot be mistaken for measured zero");
        Invoke(window, "ClearDetail");
        t.True(Label(window, "event-diagnostics").ClassListContains(Hidden), "Clearing selection hides diagnostic details");
        t.Equal("", Label(window, "event-diagnostics").tooltip, "Clearing selection resets tooltip");
        t.True(!window.rootVisualElement.Q<Button>("explain-cause").enabledSelf, "No selection disables explain");
    }

    static void CheckPresentationState(TestContext t, EditorWindow window)
    {
        Set(window, "offset", 200); Set(window, "operationId", (long?)23);
        Set(window, "layerFilter", "Paint"); Set(window, "phaseFilter", "cache");
        Set(window, "summaryHeight", 220f); Set(window, "detailsHeight", 160f);
        Set(window, "showComparison", true);
        object groups = Get(window, "selectedGroups"), baseline = Get(window, "baselineId");
        var events = (IList)Get(window, "events");
        var list = (ListView)Get(window, "eventList");
        events.Clear(); events.Add(Raw("{\"id\":1}")); events.Add(Raw("{\"id\":2}"));
        list.SetSelectionWithoutNotify(new[] { 1 });
        var scroll = window.rootVisualElement.Q<ScrollView>("diagnostics-scroll");
        scroll.verticalScroller.highValue = 500;
        scroll.scrollOffset = new Vector2(0, 42);
        var position = scroll.scrollOffset;
        Memory(window, "{\"memory\":{\"editorManagedAllocatedBytes\":1024,\"editorManagedAllocationAvailable\":true}}");
        Invoke(window, "ApplyBenchmark", Raw("{\"benchmark\":{\"benchmarkId\":\"state-test\",\"state\":\"running\",\"running\":true,\"phase\":\"cold\"}}"));
        t.Equal(200, (int)Get(window, "offset"), "Memory updates retain event page");
        t.Equal((long?)23, (long?)Get(window, "operationId"), "Memory updates retain operation filter");
        t.Equal("Paint", (string)Get(window, "layerFilter"), "Layer filter retained");
        t.Equal("cache", (string)Get(window, "phaseFilter"), "Phase filter retained");
        t.Equal(groups, Get(window, "selectedGroups"), "Group filter retained");
        t.Equal(baseline, Get(window, "baselineId"), "Baseline identity retained");
        t.True((bool)Get(window, "showComparison"), "Comparison view retained");
        t.Equal(220f, (float)Get(window, "summaryHeight"), "Summary pane size retained");
        t.Equal(160f, (float)Get(window, "detailsHeight"), "Details pane size retained");
        t.Equal(1, list.selectedIndex, "Event selection retained");
        t.Equal(2, events.Count, "Event rows retained");
        t.Equal(position, scroll.scrollOffset, "Diagnostics refresh preserves scroll position");
    }

    static void CheckBenchmarks(TestContext t, EditorWindow window)
    {
        var root = window.rootVisualElement;
        Invoke(window, "ApplyBenchmark", Raw("{\"benchmark\":null}"));
        root.Q<TextField>("document-id").value = "";
        t.True(!root.Q<Button>("benchmark-run").enabledSelf, "Run requires source document ID");
        t.True(!root.Q<Button>("benchmark-cancel").enabledSelf, "No run disables Cancel");
        t.True(Label(window, "benchmark-validation").text.Contains("Document ID"), "Missing source identifies existing source field");
        root.Q<TextField>("document-id").value = "diagnostics-benchmark-document";
        t.True(root.Q<Button>("benchmark-run").enabledSelf, "Source field enables manual Run");
        var request = Json.Parse(Invoke(window, "BenchmarkRequest", "benchmarkStart").ToString());
        t.Equal("benchmarkStart", (string)request["action"], "Explicit benchmarkStart request");
        t.Equal(1, (int)request["apiVersion"], "Shared API version");
        t.Equal("diagnostics-benchmark-document", (string)request["documentId"], "Uses existing source field unchanged");
        t.Equal("both", (string)request["mode"], "Default both modes");
        t.Equal(7, (int)request["repetitions"], "Default seven measured samples");
        t.Equal(1, (int)request["warmups"], "Default one warmup");
        t.Equal(512, (int)request["maxSize"], "Default size 512");
        t.True(request["captureId"] == null && request["baselineId"] == null && request["operationId"] == null && request["groups"] == null,
            "Benchmark does not inherit capture IDs or display filters");
        foreach (var bounds in new[] { ("benchmark-repetitions", 0, 31, 30), ("benchmark-warmups", -1, 6, 0), ("benchmark-max-size", 0, 2049, 2048) })
        {
            var field = root.Q<IntegerField>(bounds.Item1);
            field.value = bounds.Item2;
            t.True(!root.Q<Button>("benchmark-run").enabledSelf, bounds.Item1 + " lower bound enforced");
            field.value = bounds.Item3;
            t.True(!root.Q<Button>("benchmark-run").enabledSelf, bounds.Item1 + " upper bound enforced");
            field.value = bounds.Item4;
            t.True(root.Q<Button>("benchmark-run").enabledSelf, bounds.Item1 + " inclusive allowed bound accepted");
        }
        root.Q<DropdownField>("benchmark-mode").value = "Warm";
        request = Json.Parse(Invoke(window, "BenchmarkRequest", "benchmarkStart").ToString());
        t.Equal("warm", (string)request["mode"], "Mode is lower-case API value");
        string serialized = EditorJsonUtility.ToJson(window);
        root.Q<DropdownField>("benchmark-mode").value = "Cold";
        root.Q<IntegerField>("benchmark-repetitions").value = 1;
        root.Q<IntegerField>("benchmark-warmups").value = 1;
        root.Q<IntegerField>("benchmark-max-size").value = 64;
        t.Equal("cold", (string)Get(window, "benchmarkMode"), "Mode deliberately changed before deserialization");
        t.Equal(1, (int)Get(window, "benchmarkRepetitions"), "Repetitions deliberately changed before deserialization");
        t.Equal(1, (int)Get(window, "benchmarkWarmups"), "Warmups deliberately changed before deserialization");
        t.Equal(64, (int)Get(window, "benchmarkMaxSize"), "Max size deliberately changed before deserialization");
        EditorJsonUtility.FromJsonOverwrite(serialized, window);
        Restored(t, window, "benchmarkMode", "warm", serialized);
        Restored(t, window, "benchmarkRepetitions", 30, serialized);
        Restored(t, window, "benchmarkWarmups", 0, serialized);
        Restored(t, window, "benchmarkMaxSize", 2048, serialized);
        Invoke(window, "CreateGUI");
        t.True(root.panel != null, "Benchmark serialization recreation retains attached panel");
        t.Equal("Warm", root.Q<DropdownField>("benchmark-mode").value, "Rebuilt mode control reads restored setting");
        t.Equal(30, root.Q<IntegerField>("benchmark-repetitions").value, "Rebuilt repetitions control reads restored setting");
        t.Equal(0, root.Q<IntegerField>("benchmark-warmups").value, "Rebuilt warmups control reads restored setting");
        t.Equal(2048, root.Q<IntegerField>("benchmark-max-size").value, "Rebuilt max size control reads restored setting");
        Invoke(window, "ApplyBenchmark", Raw("{\"benchmark\":{\"benchmarkId\":\"bench-ui\",\"state\":\"running\",\"running\":true,\"phase\":\"warmup\"," +
            "\"completedSamples\":7,\"totalSamples\":14,\"completedWarmups\":0,\"warmups\":1,\"cold\":{\"statistics\":{\"count\":7,\"medianMs\":2,\"p95Ms\":3,\"minMs\":1,\"maxMs\":4}},\"warm\":{\"statistics\":null}}}"));
        t.True(!root.Q<Button>("benchmark-run").enabledSelf && root.Q<Button>("benchmark-cancel").enabledSelf, "Active run gates Run and enables explicit Cancel");
        t.True(!root.Q<IntegerField>("benchmark-repetitions").parent.enabledSelf, "Running settings disabled");
        t.True(Label(window, "benchmark-status").text.Contains("warmup") && Label(window, "benchmark-status").text.Contains("7/14"), "Progress exposes phase and measured sample count");
        t.True(Label(window, "benchmark-cold").text.Contains("median 2 ms") && Label(window, "benchmark-cold").text.Contains("p95 3 ms") &&
            Label(window, "benchmark-cold").text.Contains("range 1–4 ms"), "Compact CPU wall statistics shown");
        t.True(Label(window, "benchmark-warm").text.Contains("no measured samples"), "Unmeasured mode is not zero");
        foreach (string action in new[] { "benchmarkStatus", "benchmarkRead", "benchmarkCancel" })
        {
            request = Json.Parse(Invoke(window, "BenchmarkRequest", action).ToString());
            t.Equal("bench-ui", (string)request["benchmarkId"], action + " retains run identity");
            t.True(request["captureId"] == null && request["documentId"] == null, action + " includes no capture or source mutation");
        }
        foreach (string state in new[] { "cancelled", "timedOut", "interrupted", "failed" })
        {
            Invoke(window, "ApplyBenchmark", Raw("{\"success\":true,\"benchmark\":{\"benchmarkId\":\"bench-ui\",\"state\":\"" + state +
                "\",\"running\":false,\"partial\":true,\"error\":\"stopped detail\",\"warm\":{\"statistics\":{\"count\":1,\"medianMs\":0,\"p95Ms\":null,\"minMs\":0,\"maxMs\":0}}}}"));
            t.True(Label(window, "benchmark-status").text.Contains(state) && Label(window, "benchmark-status").text.Contains("partial"), state + " keeps partial results explicit");
            t.True(!root.Q<Button>("benchmark-cancel").enabledSelf && root.Q<Button>("benchmark-run").enabledSelf, state + " ends active controls");
            t.Equal("stopped detail", Label(window, "benchmark-error").text, state + " execution error shown despite successful API response");
        }
        t.True(Label(window, "benchmark-warm").text.Contains("median 0 ms") && Label(window, "benchmark-warm").text.Contains("p95 — ms"), "Null timing is not zero in benchmark results");
        Invoke(window, "ApplyBenchmark", Raw("{\"benchmark\":{\"benchmarkId\":\"done\",\"state\":\"completed\",\"running\":false," +
            "\"metadata\":{\"documentName\":\"Paint study\",\"width\":512,\"height\":256,\"timing\":\"Readback boundary\",\"caveats\":[\"Environment uncontrolled\"]}}}"));
        t.True(Label(window, "benchmark-error").ClassListContains(Hidden), "Successful result clears stale error");
        t.True(Label(window, "benchmark-metadata").text.Contains("Paint study") && Label(window, "benchmark-metadata").text.Contains("512 × 256"), "Result name and dimensions visible");
        t.True(Label(window, "benchmark-method-text").text.Contains("Readback boundary") && Label(window, "benchmark-method-text").text.Contains("Environment uncontrolled"), "Backend method and caveats retained");
        t.True(Label(window, "benchmark-note").text.Contains("not GPU time") && Label(window, "benchmark-note").text.Contains("does not restart Unity or JIT"), "Timing and cold limitations visible");
        Invoke(window, "ApplyBenchmark", Raw("{}"));
        t.Equal("", Label(window, "benchmark-metadata").text, "Missing benchmark safely resets stale metadata");
        t.True(Get(window, "benchmarkId") == null && !(bool)Get(window, "benchmarkRunning"), "Missing benchmark stops polling state");
        t.True(!Label(window, "benchmark-method-text").text.Contains("Environment uncontrolled"), "Missing benchmark clears old caveats");
    }
}
