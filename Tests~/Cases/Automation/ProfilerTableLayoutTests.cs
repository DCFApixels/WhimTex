using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class ProfilerTableLayoutTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type WindowType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true);
    static object Get(object target, string field) => WindowType.GetField(field, Flags).GetValue(target);
    static void Invoke(object target, string method, params object[] args) => WindowType.GetMethod(method, Flags).Invoke(target, args);
    static object Raw(string text) => WindowType.GetMethod("ComparisonRequest", Flags).ReturnType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
    public static string Start(string id) => AsyncD.Start(id, Run);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static void Fill(EditorWindow window, string field, int count, bool comparison = false)
    {
        var list = (ListView)Get(window, field); list.itemsSource.Clear();
        for (int i = 0; i < count; i++)
        {
            string data = "{\"id\":" + (i + 1) + ",\"phase\":\"layer.render.long.phase.name\",\"kind\":\"span\",\"group\":\"rendering\",\"layerId\":\"test-layer\",\"layerName\":\"Long named fixture layer\",\"depth\":" + (i % 4) + ",\"count\":1,\"completedCount\":1,\"totalMs\":2,\"selfMs\":1,\"maxMs\":2,\"durationMs\":2,\"operationMs\":3,\"fxIndex\":-1,\"gcAllocationCount\":123456,\"gcSelfAllocationCount\":3456,\"measuredCalls\":1,\"eligibleCalls\":1,\"partial\":false";
            if (comparison) data += ",\"status\":\"matched\",\"countChanged\":false,\"before\":{\"count\":1,\"selfMs\":1},\"after\":{\"count\":1,\"selfMs\":2},\"delta\":{\"selfMs\":1},\"percent\":{\"selfMs\":100}";
            list.itemsSource.Add(Raw(data + "}"));
        }
        list.RefreshItems();
    }

    static void Check(TestContext t, VisualElement header, ListView list, string context, bool overflow)
    {
        var row = list.Q<VisualElement>(className: "whimtex-profiler-data-row");
        t.True(row != null && row.worldBound.width > 0, context + " has realized rows");
        t.Equal(header.childCount, row.childCount, context + " same column count");
        var scroll = list.Q<ScrollView>();
        bool shown = scroll.verticalScroller.resolvedStyle.display != DisplayStyle.None;
        t.Equal(overflow, shown, context + " expected vertical scrollbar visibility");
        for (int i = 0; i < row.childCount; i++)
        {
            t.Near(header[i].worldBound.xMin, row[i].worldBound.xMin, .6, context + " column " + i + " start");
            t.Near(header[i].worldBound.width, row[i].worldBound.width, .6, context + " column " + i + " width");
        }
    }

    static void CheckAllocations(TestContext t, EditorWindow window)
    {
        var list = (ListView)Get(window, "summaryList");
        var rows = list.itemsSource; rows.Clear();
        var names = new Dictionary<object, string>();
        void Add(string name, string kind, string allocations)
        {
            var value = Raw("{\"phase\":\"" + name + "\",\"kind\":\"" + kind + "\",\"count\":2,\"completedCount\":2,\"totalMs\":30,\"selfMs\":1,\"maxMs\":15" + allocations + "}");
            rows.Add(value); names.Add(value, name);
        }
        Add("zeta", "span", ",\"gcAllocationCount\":20,\"gcSelfAllocationCount\":3,\"measuredCalls\":2,\"eligibleCalls\":2,\"partial\":false");
        Add("alpha", "span", ",\"gcAllocationCount\":20,\"gcSelfAllocationCount\":7,\"measuredCalls\":2,\"eligibleCalls\":2,\"partial\":false");
        Add("partial", "span", ",\"gcAllocationCount\":10,\"gcSelfAllocationCount\":8,\"measuredCalls\":1,\"eligibleCalls\":2,\"partial\":true");
        Add("zero", "span", ",\"gcAllocationCount\":0,\"gcSelfAllocationCount\":0,\"measuredCalls\":2,\"eligibleCalls\":2,\"partial\":false");
        Add("missing", "span", ",\"gcAllocationCount\":null,\"gcSelfAllocationCount\":null,\"measuredCalls\":0,\"eligibleCalls\":2,\"partial\":true");
        Add("legacy", "span", "");
        Add("cache", "cache", "");
        Add("phase", "phase", "");
        void Submit(string column, string expected, string arrow)
        {
            var header = window.rootVisualElement.Q<Button>("summary-sort-" + column);
            t.True(header != null, "Allocation header exists: " + column);
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = header; header.SendEvent(e); }
            t.Equal(expected, string.Join(",", rows.Cast<object>().Select(x => names[x])), column + " order, ties, zero and unavailable values");
            t.True(header.text.EndsWith(arrow), "Header indicates sort direction");
            t.True(header.tooltip.Contains("count, not bytes"), "Header distinguishes count from bytes");
            t.Equal(1, window.rootVisualElement.Query<Button>(className: "whimtex-profiler-sort-active").ToList().Count, "Only one active header");
            t.Near(30, (double)Get(window, "summaryScale"), 0, "Allocation sorting does not rescale time bars");
        }
        Submit("Allocations", "alpha,zeta,partial,zero,cache,legacy,missing,phase", "▼");
        Submit("Allocations", "zero,partial,alpha,zeta,cache,legacy,missing,phase", "▲");
        Submit("SelfAllocations", "partial,alpha,zeta,zero,cache,legacy,missing,phase", "▼");
        Submit("SelfAllocations", "zero,zeta,alpha,partial,cache,legacy,missing,phase", "▲");
        var recycled = list.makeItem();
        void Bind(string name) => list.bindItem(recycled, rows.Cast<object>().ToList().FindIndex(x => names[x] == name));
        Bind("partial");
        t.Equal("10*", recycled.Q<Label>("allocations").text, "Partial inclusive count");
        t.Equal("8*", recycled.Q<Label>("self-allocations").text, "Partial self count");
        t.True(recycled.Q<Label>("allocations").tooltip.Contains("1 / 2"), "Partial measurement coverage tooltip");
        Bind("zero");
        t.Equal("0", recycled.Q<Label>("allocations").text, "Measured zero is not unavailable");
        t.True(!recycled.Q<Label>("allocations").tooltip.Contains("Partial"), "Recycled tooltip clears partial state");
        foreach (var name in new[] { "missing", "legacy", "cache", "phase" })
        {
            Bind(name);
            t.Equal("—", recycled.Q<Label>("allocations").text, name + " inclusive unavailable");
            t.Equal("—", recycled.Q<Label>("self-allocations").text, name + " self unavailable");
        }
        var restored = (EditorWindow)ScriptableObject.CreateInstance(WindowType);
        try
        {
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(window), restored);
            Invoke(restored, "CreateGUI"); Invoke(restored, "OnDisable");
            t.Equal("Self allocations ▲", restored.rootVisualElement.Q<Button>("summary-sort-SelfAllocations").text, "Allocation sort survives serialized UI recreation");
        }
        finally { UnityEngine.Object.DestroyImmediate(restored); }
    }

    static async Task CheckDeltaColors(TestContext t, EditorWindow window, CancellationToken token)
    {
        var root = window.rootVisualElement;
        var list = (ListView)Get(window, "comparisonList");
        string[] keys = { "selfMs", "totalMs", "meanMs", "maxMs", "gpuMs", "count", "gcAllocationCount", "gcSelfAllocationCount" };
        var recycled = list.makeItem();
        root.Add(recycled);
        try
        {
            foreach (bool light in new[] { false, true })
            {
                root.EnableInClassList("whimtex-profiler--light", light);
                for (int metric = 0; metric < keys.Length; metric++)
                {
                    WindowType.GetField("comparisonSort", Flags).SetValue(window, metric);
                    string key = keys[metric];
                    async Task CheckValue(string value, string percent, bool comparable, int deltaTone, int percentTone)
                    {
                        string comparison = comparable ? "true" : "false";
                        list.itemsSource.Clear();
                        list.itemsSource.Add(Raw("{\"phase\":\"layer.render\",\"kind\":\"span\",\"group\":\"rendering\",\"status\":\"matched\",\"allocationComparable\":" + comparison + ",\"gpuComparable\":" + comparison +
                            ",\"before\":{\"count\":1,\"" + key + "\":10},\"after\":{\"count\":1,\"" + key + "\":12},\"delta\":{\"" + key + "\":" + value + "},\"percent\":{\"" + key + "\":" + percent + "}}"));
                        list.RefreshItems(); list.bindItem(recycled, 0);
                        await AsyncD.Tick(root, token);
                        string context = (light ? "light/" : "dark/") + key + "/" + value + "/" + comparable;
                        var neutral = recycled.Q<Label>("comparison-before").resolvedStyle.color;
                        void CheckCell(string name, int tone)
                        {
                            var cell = recycled.Q<Label>(name);
                            t.Equal(tone < 0, cell.ClassListContains("whimtex-profiler-delta-decrease"), context + "/" + name + " decrease class");
                            t.Equal(tone > 0, cell.ClassListContains("whimtex-profiler-delta-increase"), context + "/" + name + " increase class");
                            Color color = cell.resolvedStyle.color;
                            if (tone < 0) t.True(color.g > color.r, context + " renders green");
                            else if (tone > 0) t.True(color.r > color.g, context + " renders red");
                            else t.Equal(neutral, color, context + " renders neutral");
                            if (tone != 0) t.True(cell.text.StartsWith(tone < 0 ? "-" : "+"), context + " keeps numeric sign");
                        }
                        CheckCell("comparison-delta", deltaTone);
                        CheckCell("comparison-percent", percentTone);
                    }
                    foreach (string value in new[] { "-2", "2", "0", "null", "0.0001", "-0.0001" })
                    {
                        int tone = key == "count" ? 0 : value == "-2" ? -1 : value == "2" ? 1 : 0;
                        await CheckValue(value, value, true, tone, tone);
                    }
                    await CheckValue("2", "null", true, key == "count" ? 0 : 1, 0);
                    if (key == "gpuMs" || key.StartsWith("gc"))
                        await CheckValue("-2", "-2", false, 0, 0);
                }
            }
        }
        finally { recycled.RemoveFromHierarchy(); }
    }

    static async Task Run(TestContext t, CancellationToken token)
    {
        var window = (EditorWindow)ScriptableObject.CreateInstance(WindowType);
        try
        {
            window.titleContent = new GUIContent("Profiler column alignment check"); window.ShowUtility(); Invoke(window, "CreateGUI");
            Invoke(window, "OnDisable"); // Own synthetic UI rows only; never replace or modify the user's capture.
            var root = window.rootVisualElement;
            CheckAllocations(t, window);
            foreach (bool light in new[] { false, true })
            foreach (int width in new[] { 680, 1000 })
            foreach (bool comparison in new[] { false, true })
            foreach (int count in new[] { 1, 60 })
            {
                token.ThrowIfCancellationRequested();
                root.EnableInClassList("whimtex-profiler--light", light);
                window.position = new Rect(100, 80, width, 800);
                Invoke(window, "SetComparisonView", comparison);
                Fill(window, "summaryList", count); Fill(window, "eventList", count); Fill(window, "comparisonList", count, true);
                window.Repaint(); await AsyncD.Tick(root, token); await AsyncD.Tick(root, token);
                string context = (light ? "light" : "dark") + "/" + width + "/" + count;
                var list = (ListView)Get(window, comparison ? "comparisonList" : "summaryList");
                var header = root.Q(comparison ? "comparison-content" : "summary-content").Q(className: "whimtex-profiler-columns");
                Check(t, header, list, context + (comparison ? "/comparison" : "/summary"), count > 1);
                Check(t, root.Q("events-pane").Q(className: "whimtex-profiler-columns"), (ListView)Get(window, "eventList"), context + "/events", count > 1);
                if (count > 1)
                {
                    list.ScrollToItem(count - 1); await AsyncD.Tick(root, token);
                    Check(t, header, list, context + "/scrolled", true);
                }
                if (!comparison)
                {
                    t.Equal(9, header.childCount, "Summary has both allocation columns");
                    var horizontal = root.Q<ScrollView>("summary-content");
                    t.Equal(width == 680, horizontal.horizontalScroller.resolvedStyle.display != DisplayStyle.None, context + " horizontal scrollbar only when needed");
                    horizontal.horizontalScroller.value = horizontal.horizontalScroller.highValue;
                    await AsyncD.Tick(root, token);
                    Check(t, header, list, context + "/horizontal-scroll", count > 1);
                    t.True(header[8].worldBound.xMax <= horizontal.contentViewport.worldBound.xMax + .6f, context + " last column reachable");
                    horizontal.scrollOffset = Vector2.zero;
                }
            }
            await CheckDeltaColors(t, window, token);
        }
        finally { AsyncD.CleanupOwned(window.Close); }
    }
}
