using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = ProfilerAnalysisTests.Json;

// Support: Cases/Automation/ProfilerAnalysisTests.cs, Framework/TestApi.cs.
public static class ProfilerComparisonUiTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string ReloadKey = "WhimTex.Profiler.ReloadSnapshot";
    const string BaselineKey = "WhimTex.Profiler.BaselineSnapshot";
    const string HiddenClass = "whimtex-profiler-hidden";
    static readonly Assembly Package = typeof(WhimTexDocument).Assembly;
    static readonly Type Profiler = Package.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly Type WindowType = Package.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true);
    static readonly Type CaptureType = Profiler.GetNestedType("Capture", BindingFlags.NonPublic);
    static readonly Type SampleType = Profiler.GetNestedType("Sample", BindingFlags.NonPublic);
    static readonly FieldInfo Current = Profiler.GetField("capture", Static);
    static readonly FieldInfo Baseline = Profiler.GetField("<Baseline>k__BackingField", Static);
    static object Get(object target, string field) => target.GetType().GetField(field, Instance).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Instance).SetValue(target, value);
    static object Invoke(EditorWindow window, string method, params object[] args) => WindowType.GetMethod(method, Instance).Invoke(window, args);
    static Json Read(object value) => Json.Parse(value.ToString());
    static Json Request(EditorWindow window, int offset = 0) => Read(Invoke(window, "ComparisonRequest", offset));
    static ListView Comparisons(EditorWindow window) => window.rootVisualElement.Q<ListView>("comparison-list");
    static Json[] Rows(ListView list) => list.itemsSource.Cast<object>().Select(Read).ToArray();
    static long Ticks(double milliseconds) => (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000d);

    // Resolve the production method's JSON type without adding a second Newtonsoft reference.
    static object Raw(string text) => WindowType.GetMethod("ComparisonRequest", Instance).ReturnType
        .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });

    static void Submit(EditorWindow window, string name)
    {
        var button = window.rootVisualElement.Q<Button>(name);
        if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("Unavailable test control: " + name);
        using var evt = NavigationSubmitEvent.GetPooled();
        evt.target = button;
        button.SendEvent(evt);
    }

    static object Sample(int id, string phase, double ms, string layer = "a", string name = "Layer A")
    {
        object sample = Activator.CreateInstance(SampleType);
        Set(sample, "id", (long)id); Set(sample, "operationId", (long)id);
        Set(sample, "phase", phase); Set(sample, "kind", "span");
        Set(sample, "documentId", "comparison-ui-document"); Set(sample, "layerId", layer); Set(sample, "layerName", name);
        Set(sample, "fxIndex", -1); Set(sample, "completed", true);
        Set(sample, "start", Ticks(1)); Set(sample, "end", Ticks(1 + ms));
        return sample;
    }

    static object Capture(params object[] samples)
    {
        object capture = Activator.CreateInstance(CaptureType, true);
        var events = Array.CreateInstance(SampleType, Math.Max(256, samples.Length));
        for (int i = 0; i < samples.Length; i++) events.SetValue(samples[i], i);
        Set(capture, "events", events); Set(capture, "count", samples.Length);
        Set(capture, "id", Guid.NewGuid().ToString("N")); Set(capture, "stopReason", "manual");
        Set(capture, "start", 0L); Set(capture, "end", Ticks(1000)); Set(capture, "deadline", long.MaxValue);
        Set(capture, "unityVersion", Application.unityVersion);
        return capture;
    }

    static object SmallCapture(double multiplier) => Capture(
        Sample(1, "layer.render", 4 * multiplier), Sample(2, "layer.prepare", 2 * multiplier),
        Sample(3, "resource.prepare", multiplier, "b", "Layer B"), Sample(4, "ui.layout", multiplier, "b", "Layer B"));

    static object PagedCapture(bool after)
    {
        var samples = new object[205];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Sample(i + 1, "layer.page." + i.ToString("D3", CultureInfo.InvariantCulture), after ? 206 - i : 1, "page", "Page layer");
        return Capture(samples);
    }

    static void SelectGroups(EditorWindow window, params string[] ids)
    {
        Submit(window, "groups-none");
        foreach (string id in ids) window.rootVisualElement.Q<Toggle>("group-" + id).value = true;
    }

    static void Refresh(EditorWindow window) => Invoke(window, "RefreshCapture");

    public static string Run()
    {
        object savedCurrent = Current.GetValue(null), savedBaseline = Baseline.GetValue(null);
        if (savedCurrent != null && ((bool)Get(savedCurrent, "recording") || (int)Get(savedCurrent, "open") != 0 ||
            (int)Profiler.GetMethod("PendingGpu", Static).Invoke(null, new[] { savedCurrent }) != 0))
            return TestContext.Result("skipped", 0, "Requires an idle profiler; user capture and baseline are untouched.").ToJson();

        return TestContext.Run("Profiler comparison controls, filters, paging, recycled rows and serialized view", t =>
        {
            string absent = "absent-" + Guid.NewGuid().ToString("N");
            string savedReload = SessionState.GetString(ReloadKey, absent), savedBaselineToken = SessionState.GetString(BaselineKey, absent);
            EditorWindow window = null;
            var previousFocus = EditorWindow.focusedWindow;
            try
            {
                // Pin/clear may discard a reload backup. Keep the user's tokens and files out of their reach.
                SessionState.EraseString(ReloadKey); SessionState.EraseString(BaselineKey);
                Current.SetValue(null, null); Baseline.SetValue(null, null);
                window = (EditorWindow)ScriptableObject.CreateInstance(WindowType);
                window.titleContent = new GUIContent("Profiler comparison UI check");
                window.ShowUtility();
                Invoke(window, "CreateGUI");
                t.True(window.rootVisualElement.panel != null, "Owned window supports real button and field events");
                CheckControls(t, window);
                CheckRequestsAndFilters(t, window);
                CheckPaging(t, window);
                CheckSerialization(t, window);
                CheckRecycledRows(t, window);
                CheckAllocationRows(t, window);
                t.Equal("", SessionState.GetString(ReloadKey, ""), "UI checks do not create a current reload snapshot");
                t.Equal("", SessionState.GetString(BaselineKey, ""), "UI checks do not create a baseline reload snapshot");
            }
            finally
            {
                try { if (window != null) window.Close(); }
                finally
                {
                    Current.SetValue(null, savedCurrent); Baseline.SetValue(null, savedBaseline);
                    if (savedReload == absent) SessionState.EraseString(ReloadKey); else SessionState.SetString(ReloadKey, savedReload);
                    if (savedBaselineToken == absent) SessionState.EraseString(BaselineKey); else SessionState.SetString(BaselineKey, savedBaselineToken);
                    if (previousFocus != null) previousFocus.Focus();
                }
            }
            t.True(ReferenceEquals(savedCurrent, Current.GetValue(null)), "Original current capture reference restored");
            t.True(ReferenceEquals(savedBaseline, Baseline.GetValue(null)), "Original baseline reference restored");
            t.Equal(savedReload, SessionState.GetString(ReloadKey, absent), "Current SessionState restored, including absent versus empty");
            t.Equal(savedBaselineToken, SessionState.GetString(BaselineKey, absent), "Baseline SessionState restored, including absent versus empty");
        });
    }

    static void CheckControls(TestContext t, EditorWindow window)
    {
        var root = window.rootVisualElement;
        t.True(!root.Q<Button>("set-baseline").enabledSelf && !root.Q<Button>("clear-baseline").enabledSelf, "No capture or baseline disables both actions");
        t.True(!((bool)Get(window, "showComparison")) && !root.Q("summary-content").ClassListContains(HiddenClass), "Summary remains the default view");
        t.Equal(CollectionVirtualizationMethod.FixedHeight, Comparisons(window).virtualizationMethod, "Comparison uses fixed-height virtualization");
        object first = SmallCapture(1);
        Current.SetValue(null, first); Refresh(window);
        t.True(root.Q<Button>("set-baseline").enabledSelf, "Stopped capture can be pinned");
        foreach (string blocker in new[] { "recording", "openSpans", "pendingGpu" })
        {
            Set(window, blocker, blocker == "recording" ? (object)true : 1);
            Invoke(window, "UpdateButtons");
            t.True(!root.Q<Button>("set-baseline").enabledSelf, blocker + " disables pinning");
            Invoke(window, "SetBaseline");
            t.True(Baseline.GetValue(null) == null, blocker + " also guards the pin action");
            Refresh(window);
        }
        Submit(window, "set-baseline");
        object frozen = Baseline.GetValue(null);
        string baselineId = (string)Get(first, "id");
        t.True(frozen != null && !ReferenceEquals(first, frozen), "Set baseline control pins a separate snapshot");
        t.Equal(baselineId, (string)Get(window, "baselineId"), "UI reads baseline.captureId as baseline identity");
        t.True(root.Q<Label>("baseline-status").text.Contains(baselineId.Substring(0, 8)), "Baseline identity is visible");
        Current.SetValue(null, SmallCapture(2)); Refresh(window);
        t.True(ReferenceEquals(frozen, Baseline.GetValue(null)), "Replacing current capture retains baseline");
        root.Q<Toggle>("comparison-toggle").value = true;
        t.Equal(4, Comparisons(window).itemsSource.Count, "Toggle loads comparison through shared API");
        foreach (string blocker in new[] { "recording", "openSpans", "pendingGpu" })
        {
            Set(window, blocker, blocker == "recording" ? (object)true : 1);
            Invoke(window, "InvalidateComparison"); Invoke(window, "RefreshComparison");
            t.True(!root.Q<DropdownField>("comparison-sort").enabledSelf, blocker + " disables comparison controls");
            t.Equal(0, Comparisons(window).itemsSource.Count, blocker + " prevents a comparison read");
            Refresh(window);
        }
        Invoke(window, "ResetRows");
        t.True(ReferenceEquals(frozen, Baseline.GetValue(null)), "Resetting current UI rows retains baseline");
        Current.SetValue(null, null); Refresh(window);
        t.True(!root.Q<Button>("set-baseline").enabledSelf && root.Q<Button>("clear-baseline").enabledSelf, "Baseline remains independently clearable without current capture");
        t.Equal(baselineId, (string)Get(window, "baselineId"), "No-current status still displays baseline metadata");
        Submit(window, "clear-baseline");
        t.True(Baseline.GetValue(null) == null && Get(window, "baselineId") == null, "Clear baseline control uses the retained capture ID");
        t.True(!root.Q<Button>("clear-baseline").enabledSelf, "Cleared baseline disables clear action");
        Current.SetValue(null, first); Refresh(window); Submit(window, "set-baseline");
        Current.SetValue(null, SmallCapture(2)); Refresh(window);
        var oldSchedule = (IVisualElementScheduledItem)Get(window, "automaticRefresh");
        var oldList = Comparisons(window);
        t.True(oldSchedule.isActive, "Attached window has an active refresh schedule before rebuild");
        Invoke(window, "CreateGUI");
        t.True(!oldSchedule.isActive, "Second CreateGUI pauses the prior active schedule");
        t.True(!ReferenceEquals(oldSchedule, Get(window, "automaticRefresh")), "Second CreateGUI creates a fresh schedule");
        t.True(!ReferenceEquals(oldList, Comparisons(window)), "Second CreateGUI replaces the old comparison table");
        t.Equal(1, root.Query<Button>(name: "set-baseline").ToList().Count, "Second CreateGUI does not duplicate baseline controls");
        t.Equal(1, root.Query<ListView>(name: "comparison-list").ToList().Count, "Second CreateGUI does not duplicate the comparison table");
        t.Equal(4, Comparisons(window).itemsSource.Count, "Second CreateGUI reloads a usable comparison");
    }

    static void CheckRequestsAndFilters(TestContext t, EditorWindow window)
    {
        var root = window.rootVisualElement;
        var request = Request(window, 200);
        t.Equal("compare", (string)request["action"], "Comparison action");
        t.Equal((string)Get(Current.GetValue(null), "id"), (string)request["captureId"], "Request pins current identity");
        t.Equal((string)Get(Baseline.GetValue(null), "id"), (string)request["baselineId"], "Request pins baseline identity");
        t.Equal(200, (int)request["offset"], "Comparison offset forwarded");
        t.Equal(200, (int)request["limit"], "Comparison request bounded to one UI page");
        string[] titles = { "Self ms", "Total ms", "Mean ms", "Max ms", "GPU ms", "Count", "Allocations", "Self allocations" };
        string[] keys = { "selfMs", "totalMs", "meanMs", "maxMs", "gpuMs", "count", "gcAllocationCount", "gcSelfAllocationCount" };
        object summarySort = Get(window, "summarySort");
        for (int i = 0; i < titles.Length; i++)
        {
            root.Q<DropdownField>("comparison-sort").value = titles[i];
            t.Equal(keys[i], (string)Request(window)["sort"], "Metric maps to API sort: " + titles[i]);
            t.Equal(i >= 6 ? "Δ alloc" : i == 5 ? "Δ calls" : "Δ ms", ((Label)Get(window, "comparisonDeltaHeader")).text, "Delta header has the selected unit");
        }
        t.Equal(summarySort, Get(window, "summarySort"), "Comparison sorting leaves Summary sorting intact");
        Set(window, "layerFilter", "  Layer A  "); Set(window, "phaseFilter", "  layer.  ");
        Set(window, "operationId", (long?)1);
        SelectGroups(window, "rendering");
        request = Request(window);
        t.Equal("Layer A", (string)request["layer"], "Layer filter trimmed");
        t.Equal("layer.", (string)request["phase"], "Phase filter trimmed");
        t.True(request["groups"].Select(x => (string)x).SequenceEqual(new[] { "rendering" }), "Selected groups forwarded");
        t.True(request["operationId"] == null, "Capture-local operation filter excluded from comparison request");
        t.Equal(2, Comparisons(window).itemsSource.Count, "Both matching operations remain in comparison");
        t.True(Rows(Comparisons(window)).All(x => (string)x["group"] == "rendering" && (string)x["layerName"] == "Layer A"), "Layer, phase and group filters apply to comparison");
        var events = (ListView)Get(window, "eventList");
        t.Equal(1, events.itemsSource.Count, "Events retain their operation filter");
        t.Equal(1, (int)Rows(events)[0]["operationId"], "Event operation is unchanged");
        t.True(root.Q<Label>("comparison-warnings").text.Contains("Operation filter ignored"), "Ignored filter warning is visible");
        t.True(!root.Q("comparison-notices").ClassListContains(HiddenClass), "Warning container is shown");
        Submit(window, "groups-none");
        t.Equal(0, Request(window)["groups"].Count(), "None is an explicit empty groups array");
        t.Equal(0, Comparisons(window).itemsSource.Count, "None clears comparison");
        t.Equal("No groups selected", root.Q<Label>("comparison-page").text, "Empty group selection explained");
        Set(window, "operationId", null); Set(window, "layerFilter", ""); Set(window, "phaseFilter", "");
        root.Q<Toggle>("group-resources").value = true;
        t.Equal(1, Comparisons(window).itemsSource.Count, "One checkbox recovers from None");
        t.Equal("resources", (string)Rows(Comparisons(window))[0]["group"], "Resource group is honored");
        t.True(!root.Q<Label>("comparison-warnings").text.Contains("Operation filter ignored"), "Obsolete operation warning removed");
        t.True(Request(window)["layer"] == null && Request(window)["phase"] == null, "Empty text filters omitted");
        Submit(window, "groups-all");
    }

    static void CheckPaging(TestContext t, EditorWindow window)
    {
        Current.SetValue(null, PagedCapture(false)); Refresh(window); Submit(window, "set-baseline");
        Current.SetValue(null, PagedCapture(true)); Refresh(window);
        var root = window.rootVisualElement;
        root.Q<DropdownField>("comparison-sort").value = "Self ms";
        var list = Comparisons(window);
        t.Equal(200, list.itemsSource.Count, "First comparison page is bounded");
        t.True(!root.Q<Button>("comparison-previous").enabledSelf && root.Q<Button>("comparison-next").enabledSelf, "First-page controls");
        var first = Rows(list).Select(x => (string)x["phase"]).ToArray();
        Submit(window, "comparison-next");
        t.Equal(200, (int)Get(window, "comparisonOffset"), "Next control uses API nextOffset");
        t.Equal(5, list.itemsSource.Count, "Final page contains remainder");
        t.True(root.Q<Button>("comparison-previous").enabledSelf && !root.Q<Button>("comparison-next").enabledSelf, "Final-page controls");
        var all = first.Concat(Rows(list).Select(x => (string)x["phase"])).ToArray();
        t.Equal(205, all.Distinct().Count(), "Pages cover every stage without duplicates");
        t.True(all.SequenceEqual(Enumerable.Range(0, 205).Select(i => "layer.page." + i.ToString("D3", CultureInfo.InvariantCulture))), "Absolute-delta ordering is stable across pages");
        Submit(window, "comparison-previous");
        t.True(first.SequenceEqual(Rows(list).Select(x => (string)x["phase"])), "Previous restores the first page");
        Submit(window, "comparison-next"); SelectGroups(window, "rendering");
        t.Equal(0, (int)Get(window, "comparisonOffset"), "Group change resets comparison paging");
        Submit(window, "comparison-next"); root.Q<DropdownField>("comparison-sort").value = "Mean ms";
        t.Equal(0, (int)Get(window, "comparisonOffset"), "Metric change resets comparison paging");
    }

    static void CheckSerialization(TestContext t, EditorWindow window)
    {
        window.rootVisualElement.Q<DropdownField>("comparison-sort").value = "Self allocations";
        Set(window, "layerFilter", "Page layer"); Set(window, "phaseFilter", "layer.page"); Refresh(window);
        Set(window, "summaryHeight", 237f); Set(window, "detailsHeight", 156f);
        string saved = EditorJsonUtility.ToJson(window);
        var restored = (EditorWindow)ScriptableObject.CreateInstance(WindowType);
        try
        {
            EditorJsonUtility.FromJsonOverwrite(saved, restored); Invoke(restored, "CreateGUI");
            var root = restored.rootVisualElement;
            t.True(root.Q<Toggle>("comparison-toggle").value, "Comparison view survives serialization");
            t.Equal("Self allocations", root.Q<DropdownField>("comparison-sort").value, "Allocation metric survives serialization");
            t.True(root.Q<Toggle>("group-rendering").value && !root.Q<Toggle>("group-resources").value, "Groups survive serialization");
            t.Equal("Page layer", (string)Request(restored)["layer"], "Layer filter survives serialization");
            t.Equal("layer.page", (string)Request(restored)["phase"], "Phase filter survives serialization");
            t.Equal(237f, (float)Get(restored, "summaryHeight"), "Shared pane height survives serialization");
            t.Equal((string)Get(Baseline.GetValue(null), "id"), (string)Get(restored, "baselineId"), "Baseline rediscovered through status");
            t.Equal(200, Comparisons(restored).itemsSource.Count, "Reconstructed view loads a usable comparison");
            var events = (ListView)Get(restored, "eventList");
            object firstEvent = events.itemsSource[0];
            Invoke(restored, "SetComparisonView", false);
            t.True(!root.Q("summary-content").ClassListContains(HiddenClass) && root.Q("comparison-content").ClassListContains(HiddenClass), "Switching back displays Summary");
            t.True(((ListView)Get(restored, "summaryList")).itemsSource.Count > 0, "Current Summary remains populated");
            t.True(ReferenceEquals(firstEvent, events.itemsSource[0]), "View toggle does not rebuild Events");
            Invoke(restored, "SetComparisonView", true);
            t.True(root.Q("summary-content").ClassListContains(HiddenClass) && !root.Q("comparison-content").ClassListContains(HiddenClass), "Comparison can be restored without rebuilding the window");
            Invoke(restored, "OnDisable");
            t.True(Get(restored, "automaticRefresh") == null, "Disabling the reconstructed window releases its refresh schedule");
        }
        finally { UnityEngine.Object.DestroyImmediate(restored); }
    }

    static void CheckRecycledRows(TestContext t, EditorWindow window)
    {
        var list = Comparisons(window);
        const string partial = @"{""phase"":""fx.render"",""kind"":""span"",""group"":""rendering"",""layerId"":""a"",""layerName"":""Named layer"",""fxIndex"":0,""fxName"":""Named FX"",""status"":""matched"",""countChanged"":true,""gpuComparable"":false,
            ""before"":{""count"":2,""selfMs"":4,""gpuMs"":3,""gpuCount"":1,""gpuPendingCount"":0,""gpuUnavailableCount"":1},
            ""after"":{""count"":3,""selfMs"":7,""gpuMs"":6,""gpuCount"":3,""gpuPendingCount"":0,""gpuUnavailableCount"":0},
            ""delta"":{""selfMs"":3,""gpuMs"":null},""percent"":{""selfMs"":75,""gpuMs"":null}}";
        list.itemsSource.Clear();
        list.itemsSource.Add(Raw(partial));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.added"",""kind"":""span"",""group"":""rendering"",""status"":""added"",""countChanged"":true,""before"":null,""after"":{""count"":1,""selfMs"":2},""delta"":{""selfMs"":null},""percent"":{""selfMs"":null}}"));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.removed"",""kind"":""span"",""group"":""rendering"",""status"":""removed"",""countChanged"":true,""before"":{""count"":1,""selfMs"":3},""after"":null,""delta"":{""selfMs"":null},""percent"":{""selfMs"":null}}"));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.zero"",""kind"":""span"",""group"":""rendering"",""status"":""matched"",""countChanged"":false,""before"":{""count"":1,""selfMs"":0},""after"":{""count"":1,""selfMs"":0},""delta"":{""selfMs"":0},""percent"":{""selfMs"":null}}"));
        var row = list.makeItem();
        Set(window, "comparisonSort", 4); list.bindItem(row, 0);
        t.Equal("3*", row.Q<Label>("comparison-before").text, "Partial GPU total is marked");
        t.Equal("—", row.Q<Label>("comparison-delta").text, "Incomparable GPU delta stays unavailable");
        t.Equal("—", row.Q<Label>("comparison-percent").text, "Incomparable GPU percent stays unavailable");
        t.True(row.Q<Label>("comparison-before").tooltip.Contains("GPU:"), "GPU metric supplies sample tooltip");
        t.True(row.Q<Label>("phase").text.Contains("Named layer") && row.Q<Label>("phase").text.Contains("Named FX"), "Known layer and FX names are displayed");
        Set(window, "comparisonSort", 0); list.bindItem(row, 0);
        foreach (string cell in new[] { "comparison-before", "comparison-after" })
            t.Equal(row.Q<Label>(cell).text, row.Q<Label>(cell).tooltip, "GPU-to-CPU reuse resets tooltip: " + cell);
        t.Equal("+3", row.Q<Label>("comparison-delta").text, "Signed CPU delta");
        t.Equal("+75%", row.Q<Label>("comparison-percent").text, "Signed percent");
        t.True(row.Q<Label>("comparison-count").text.StartsWith("≠ "), "Changed count is explicit");
        list.bindItem(row, 1);
        t.Equal("Added", row.Q<Label>("comparison-state").text, "Added row state");
        t.Equal("—", row.Q<Label>("comparison-before").text, "Missing baseline is not zero");
        t.Equal("2", row.Q<Label>("comparison-after").text, "Added row current timing remains visible");
        t.Equal("—", row.Q<Label>("comparison-delta").text, "Added row has no timing delta");
        list.bindItem(row, 2);
        t.Equal("Removed", row.Q<Label>("comparison-state").text, "Removed row state");
        t.Equal("3", row.Q<Label>("comparison-before").text, "Removed row baseline remains visible");
        t.Equal("—", row.Q<Label>("comparison-after").text, "Missing current timing is not zero");
        t.Equal("—", row.Q<Label>("comparison-delta").text, "Removed row is not presented as a negative timing delta");
        list.bindItem(row, 3);
        t.Equal("Matched", row.Q<Label>("comparison-state").text, "Recycled row resets presence state");
        t.Equal("0", row.Q<Label>("comparison-delta").text, "Actual zero delta remains zero");
        t.Equal("—", row.Q<Label>("comparison-percent").text, "Zero-baseline percent remains undefined");
        t.True(!row.Q<Label>("comparison-count").ClassListContains("whimtex-profiler-count-changed"), "Recycled row removes changed-count styling");
        t.True(!row.Q<Label>("comparison-count").text.Contains("≠"), "Recycled row removes changed-count marker");
        Refresh(window);
    }

    static void CheckAllocationRows(TestContext t, EditorWindow window)
    {
        var list = Comparisons(window); list.itemsSource.Clear();
        list.itemsSource.Add(Raw(@"{""phase"":""layer.render"",""kind"":""span"",""status"":""matched"",""countChanged"":true,""allocationComparable"":true,
            ""before"":{""count"":2,""gcAllocationCount"":100,""gcSelfAllocationCount"":20,""measuredCalls"":2,""eligibleCalls"":2,""partial"":false},
            ""after"":{""count"":3,""gcAllocationCount"":80,""gcSelfAllocationCount"":30,""measuredCalls"":3,""eligibleCalls"":3,""partial"":false},
            ""delta"":{""gcAllocationCount"":-20,""gcSelfAllocationCount"":10},""percent"":{""gcAllocationCount"":-20,""gcSelfAllocationCount"":50}}"));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.partial"",""kind"":""span"",""status"":""matched"",""allocationComparable"":false,
            ""before"":{""count"":2,""gcAllocationCount"":5,""gcSelfAllocationCount"":3,""measuredCalls"":1,""eligibleCalls"":2,""partial"":true},
            ""after"":{""count"":2,""gcAllocationCount"":20,""gcSelfAllocationCount"":10,""measuredCalls"":2,""eligibleCalls"":2,""partial"":false},
            ""delta"":{""gcAllocationCount"":15,""gcSelfAllocationCount"":7},""percent"":{""gcAllocationCount"":300,""gcSelfAllocationCount"":233}}"));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.zero"",""kind"":""span"",""status"":""matched"",""allocationComparable"":true,
            ""before"":{""gcAllocationCount"":0,""gcSelfAllocationCount"":0,""measuredCalls"":1,""eligibleCalls"":1,""partial"":false},
            ""after"":{""gcAllocationCount"":0,""gcSelfAllocationCount"":0,""measuredCalls"":1,""eligibleCalls"":1,""partial"":false},
            ""delta"":{""gcAllocationCount"":0,""gcSelfAllocationCount"":0},""percent"":{""gcAllocationCount"":null,""gcSelfAllocationCount"":null}}"));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.added"",""kind"":""span"",""status"":""added"",""allocationComparable"":false,
            ""before"":null,""after"":{""gcAllocationCount"":7,""gcSelfAllocationCount"":4,""measuredCalls"":1,""eligibleCalls"":1,""partial"":false},""delta"":{},""percent"":{}}"));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.missing"",""kind"":""span"",""status"":""matched"",""before"":{},""after"":{},""delta"":{},""percent"":{}}"));
        list.itemsSource.Add(Raw(@"{""phase"":""layer.large"",""kind"":""span"",""status"":""matched"",""allocationComparable"":true,
            ""before"":{""gcAllocationCount"":9007199254740993},""after"":{""gcAllocationCount"":9007199254740994},""delta"":{""gcAllocationCount"":1},""percent"":{}}"));
        var row = list.makeItem();
        foreach (int metric in new[] { 6, 7 })
        {
            Set(window, "comparisonSort", metric); list.bindItem(row, 0);
            t.Equal(metric == 6 ? "100" : "20", row.Q<Label>("comparison-before").text, "Baseline allocation count");
            t.Equal(metric == 6 ? "80" : "30", row.Q<Label>("comparison-after").text, "Current allocation count");
            t.Equal(metric == 6 ? "-20" : "+10", row.Q<Label>("comparison-delta").text, "Signed allocation delta");
            t.Equal(metric == 6 ? "-20%" : "+50%", row.Q<Label>("comparison-percent").text, "Signed allocation percentage");
            t.True(row.Q<Label>("comparison-before").tooltip.Contains("count, not bytes") && row.Q<Label>("comparison-before").tooltip.Contains("2 / 2"), "Count unit and coverage in tooltip");
            t.True(row.Q<Label>("comparison-before").tooltip.Contains(metric == 6 ? "Includes nested" : "Excludes nested"), "Nested allocation scope explained");
            t.True(row.tooltip.Contains("Allocations 100 → 80") && row.tooltip.Contains("Self allocations 20 → 30"), "Row tooltip exposes both counts");
            list.bindItem(row, 1);
            t.Equal(metric == 6 ? "5*" : "3*", row.Q<Label>("comparison-before").text, "Partial allocation marked");
            t.True(row.Q<Label>("comparison-before").tooltip.Contains("1 / 2"), "Partial allocation coverage");
            t.Equal("—", row.Q<Label>("comparison-delta").text, "Incomparable allocation delta suppressed");
            t.Equal("—", row.Q<Label>("comparison-percent").text, "Incomparable percentage suppressed");
            list.bindItem(row, 2);
            t.Equal("0", row.Q<Label>("comparison-delta").text, "Measured zero delta preserved after reuse");
            t.Equal("0", row.Q<Label>("comparison-before").text, "Partial marker cleared after reuse");
            t.Equal("—", row.Q<Label>("comparison-percent").text, "Zero denominator is undefined");
            list.bindItem(row, 3);
            t.Equal("—", row.Q<Label>("comparison-before").text, "Added allocation baseline is not zero");
            t.Equal("—", row.Q<Label>("comparison-delta").text, "Added allocation has no delta");
            list.bindItem(row, 4);
            t.Equal("—", row.Q<Label>("comparison-before").text, "Unavailable count without partial marker");
            t.Equal("—", row.Q<Label>("comparison-after").text, "Missing metrics stay unavailable");
        }
        Set(window, "comparisonSort", 6); list.bindItem(row, 5);
        t.Equal("9007199254740993", row.Q<Label>("comparison-before").text, "Large count is formatted without floating-point loss");
        Set(window, "comparisonSort", 0); list.bindItem(row, 0);
        t.Equal(row.Q<Label>("comparison-before").text, row.Q<Label>("comparison-before").tooltip, "Switching back to time resets allocation tooltip");
        Refresh(window);
    }
}
