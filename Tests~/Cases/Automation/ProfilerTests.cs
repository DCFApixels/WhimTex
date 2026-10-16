using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using JObject = ProfilerTests.Json;
using JArray = ProfilerTests.Json;
using JToken = ProfilerTests.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class ProfilerTests
{
    // Pipeline references two assemblies carrying Newtonsoft names. Use the public
    // API from the canonical assembly, without changing production references.
    public sealed class Json : IEnumerable<Json>
    {
        static readonly Assembly JsonAssembly = Assembly.Load("Newtonsoft.Json");
        static readonly Type ObjectType = JsonAssembly.GetType("Newtonsoft.Json.Linq.JObject", true);
        static readonly Type TokenType = JsonAssembly.GetType("Newtonsoft.Json.Linq.JToken", true);
        readonly object token;
        public Json() { token = Activator.CreateInstance(ObjectType); }
        Json(object value) { token = value; }
        public static Json Parse(string json) => new Json(ObjectType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json }));
        public static Json FromObject(object value) => new Json(TokenType.GetMethod("FromObject", new[] { typeof(object) }).Invoke(null, new[] { value }));
        public Json this[string name]
        {
            get { object value = token.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(token, new object[] { name }); return value == null ? null : new Json(value); }
            set => token.GetType().GetProperty("Item", new[] { typeof(string) }).SetValue(token, value.token, new object[] { name });
        }
        public string Type => token.GetType().GetProperty("Type").GetValue(token).ToString();
        public int Count => (int)token.GetType().GetProperty("Count").GetValue(token);
        public override string ToString() => token.ToString();
        internal object Raw => token;
        public static implicit operator Json(string value) => FromObject(value);
        public static implicit operator Json(int value) => FromObject(value);
        public static explicit operator string(Json value) => value == null || value.Type == "Null" ? null : value.ToString();
        public static explicit operator int(Json value) => int.Parse(value.ToString(), CultureInfo.InvariantCulture);
        public static explicit operator long(Json value) => long.Parse(value.ToString(), CultureInfo.InvariantCulture);
        public static explicit operator double(Json value) => double.Parse(value.ToString(), CultureInfo.InvariantCulture);
        public static explicit operator bool(Json value) => bool.Parse(value.ToString());
        public IEnumerator<Json> GetEnumerator() { foreach (object value in (IEnumerable)token) yield return new Json(value); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    static class JTokenType { internal const string Null = "Null"; }
    static readonly BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type Profiler = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static JObject Call(string action, params object[] fields)
    {
        var request = new JObject { ["apiVersion"] = 1, ["action"] = action };
        for (int i = 0; i < fields.Length; i += 2) request[(string)fields[i]] = JToken.FromObject(fields[i + 1]);
        return JObject.Parse(WhimTexApi.ProfileJson(request.ToString()));
    }
    static IDisposable Span(string phase, WhimTexDocument document = null, Layer layer = null, int fxIndex = -1) => (IDisposable)Profiler.GetMethod("Measure", Hidden)
        .Invoke(null, new object[] { phase, document, layer, fxIndex, null });
    static IDisposable DistanceSpan(string phase) => (IDisposable)typeof(WhimTexDocument).Assembly
        .GetType("DCFApixels.WhimTex.DistanceFieldTiming", true).GetMethod("Measure", Hidden).Invoke(null, new object[] { phase });
    static void Event(string detail) => Profiler.GetMethod("Event", Hidden).Invoke(null, new object[] { "test.cache", "cache", detail, 0d });
    static JArray Events(JObject report) => (JArray)report["events"];
    static string DocumentId(WhimTexDocument doc) => (string)Profiler.GetMethod("DocumentId", Hidden).Invoke(null, new object[] { doc });
    sealed class OwnedDocument : IDisposable
    {
        public WhimTexDocument Document { get; } = ScriptableObject.CreateInstance<WhimTexDocument>();
        public OwnedDocument(int size) { Document.width = Document.height = size; Document.hideFlags = HideFlags.HideAndDontSave; }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(Document);
    }

    static void CheckSummarySorting(TestContext t, Type windowType)
    {
        var previousFocus = EditorWindow.focusedWindow;
        var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
        try
        {
            window.ShowUtility();
            windowType.GetMethod("CreateGUI").Invoke(window, null);
            t.True(window.rootVisualElement.panel != null, "Owned window has a panel for real button events");
            var list = window.rootVisualElement.Query<ListView>().ToList()[0];
            var rows = list.itemsSource;
            foreach (string data in new[] {
                "{\"phase\":\"zeta\",\"kind\":\"span\",\"count\":2,\"completedCount\":2,\"totalMs\":30,\"selfMs\":1,\"maxMs\":25}",
                "{\"phase\":\"alpha\",\"kind\":\"span\",\"count\":3,\"completedCount\":3,\"totalMs\":30,\"selfMs\":2,\"maxMs\":20}",
                "{\"phase\":\"beta\",\"kind\":\"span\",\"count\":10,\"completedCount\":10,\"totalMs\":10,\"selfMs\":8,\"maxMs\":7}",
                "{\"phase\":\"pending\",\"kind\":\"span\",\"count\":4,\"completedCount\":0,\"totalMs\":0,\"selfMs\":0,\"maxMs\":0}",
                "{\"phase\":\"cache\",\"kind\":\"cache\",\"count\":6,\"completedCount\":6,\"totalMs\":0,\"selfMs\":0,\"maxMs\":0}",
                "{\"phase\":\"zero\",\"kind\":\"span\",\"count\":1,\"completedCount\":1,\"totalMs\":0,\"selfMs\":0,\"maxMs\":0}" })
                rows.Add(Json.Parse(data).Raw);
            string Order() => string.Join(",", rows.Cast<object>().Select(x => (string)Json.Parse(x.ToString())["phase"]));
            void Submit(string column)
            {
                var header = window.rootVisualElement.Q<Button>("summary-sort-" + column);
                t.True(header != null, "Sortable header exists: " + column);
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = header; header.SendEvent(e); }
            }
            void Check(string expected, string column, string arrow)
            {
                t.Equal(expected, Order(), column + " " + arrow + " numeric order, ties and missing times");
                t.True(window.rootVisualElement.Q<Button>("summary-sort-" + column).text.EndsWith(arrow), "Active direction is visible");
                t.Equal(1, window.rootVisualElement.Query<Button>(className: "whimtex-profiler-sort-active").ToList().Count,
                    "Only one header is active");
                t.Near(30, (double)windowType.GetField("summaryScale", Instance).GetValue(window), 0, "Bar scale is maximum total, not first row");
                var row = list.makeItem();
                int beta = rows.Cast<object>().ToList().FindIndex(x => (string)Json.Parse(x.ToString())["phase"] == "beta");
                list.bindItem(row, beta);
                t.Near(100d / 3, row.Q<ProgressBar>("duration-bar").value, 1e-4, "Sorting does not change duration bars");
            }
            windowType.GetMethod("SortSummary", Instance).Invoke(window, null);
            Check("alpha,zeta,beta,zero,cache,pending", "Total", "▼");
            Submit("Total"); Check("zero,beta,alpha,zeta,cache,pending", "Total", "▲");
            Submit("Total"); Check("alpha,zeta,beta,zero,cache,pending", "Total", "▼");
            Submit("Self"); Check("beta,alpha,zeta,zero,cache,pending", "Self", "▼");
            Submit("Self"); Check("zero,zeta,alpha,beta,cache,pending", "Self", "▲");
            Submit("Max"); Check("zeta,alpha,beta,zero,cache,pending", "Max", "▼");
            Submit("Max"); Check("zero,beta,alpha,zeta,cache,pending", "Max", "▲");
            Submit("Count"); Check("beta,cache,pending,alpha,zeta,zero", "Count", "▼");
            Submit("Count"); Check("zero,zeta,alpha,pending,cache,beta", "Count", "▲");
            var saved = EditorJsonUtility.ToJson(window);
            var restored = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            try
            {
                EditorJsonUtility.FromJsonOverwrite(saved, restored);
                windowType.GetMethod("CreateGUI").Invoke(restored, null);
                t.Equal("Count ▲", restored.rootVisualElement.Q<Button>("summary-sort-Count").text,
                    "Serialized sort column and direction survive rebuilding the window");
            }
            finally { UnityEngine.Object.DestroyImmediate(restored); }
            windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
            t.Equal(0, rows.Count, "Refresh with no capture clears fixture rows");
            t.Equal("Count ▲", window.rootVisualElement.Q<Button>("summary-sort-Count").text, "Refresh preserves sort choice");
        }
        finally
        {
            window.Close();
            if (previousFocus != null) previousFocus.Focus();
        }
    }

    public static string ReadOnlyGroups()
    {
        var capture = Call("status")["capture"];
        if (capture.Type == "Null" || (bool)capture["recording"] || (int)capture["openSpans"] != 0 || (int)capture["eventCount"] > 2000)
            return TestContext.Result("skipped", 0, "Requires a stopped retained capture of at most 2000 events; leaves it untouched.").ToJson();
        return TestContext.Run("Read-only profiler groups / current capture preserved", t =>
            CheckGroups(t, typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true), true));
    }

    static void CheckGroups(TestContext t, Type windowType, bool useExisting = false)
    {
        string id = (string)Call(useExisting ? "status" : "start")["capture"]["captureId"];
        var previousFocus = EditorWindow.focusedWindow;
        EditorWindow window = null;
        try
        {
            var representatives = new Dictionary<string, string> {
                ["ui.layout"] = "ui", ["input.pointer"] = "ui", ["handle.move"] = "ui",
                ["brush.stroke"] = "editing", ["undo.restore"] = "editing", ["edit.parameter"] = "editing",
                ["layer.render"] = "rendering", ["fx.render"] = "rendering", ["render.text"] = "rendering",
                ["render.sdf"] = "rendering", ["contour.traceFit"] = "rendering", ["mtsdf.correct"] = "rendering",
                ["cache.prepare"] = "resources", ["input.readback"] = "resources", ["output.upload"] = "resources",
                ["document.compose_readback"] = "resources", ["reconstruction.match"] = "resources",
                ["save.document"] = "storage", ["load.document"] = "storage", ["export.png"] = "storage",
                ["unknown.operation"] = "diagnostics" };
            foreach (var pair in representatives)
            {
                object group = Profiler.GetMethod("GroupFor", Hidden).Invoke(null, new object[] { pair.Key, "span" });
                t.Equal(pair.Value, (string)Profiler.GetMethod("GroupId", Hidden).Invoke(null, new[] { group }), "Classifies phase independently of capture: " + pair.Key);
                if (!useExisting) using (Span(pair.Key)) { }
            }
            if (!useExisting)
            {
                using (Span("document.render"))
                {
                    using (Span("cache.store")) using (Span("layer.render")) { }
                    for (int i = 0; i < 230; i++) Event("hit");
                }
                Call("stop", "captureId", id);
            }
            var full = Call("read", "limit", 2000);
            var fullEvents = Events(full).ToArray();
            var fullEventJson = fullEvents.ToDictionary(e => (long)e["id"], e => e.ToString());
            foreach (var pair in representatives)
                t.True(fullEvents.Where(e => (string)e["phase"] == pair.Key).All(e => (string)e["group"] == pair.Value), "Semantic group: " + pair.Key);
            var groupIds = new[] { "ui", "editing", "rendering", "resources", "storage", "diagnostics" };
            t.True(full["groups"].Select(g => (string)g["id"]).SequenceEqual(groupIds), "Six stable general groups, not algorithm-specific");
            foreach (var group in groupIds)
            {
                var solo = Call("read", "groups", new[] { group }, "limit", 2000);
                var expected = fullEvents.Where(e => (string)e["group"] == group).ToArray();
                t.Equal(expected.Length, (int)solo["totalMatched"], "Only one group: " + group);
                t.True(Events(solo).Select(e => (long)e["id"]).SequenceEqual(expected.Select(e => (long)e["id"])), "Filtering preserves start order: " + group);
                t.True(solo["summary"].All(e => (string)e["group"] == group), "Summary respects groups: " + group);
                t.Equal(expected.Length, (int)solo["groups"].First(g => (string)g["id"] == group)["count"], "Counts ignore own selection");
                foreach (var item in Events(solo))
                    t.Equal(fullEventJson[(long)item["id"]], item.ToString(), "Filter leaves event and timing unchanged");
            }
            var both = Call("read", "groups", new[] { "rendering", "resources" }, "limit", 2);
            t.Equal(fullEvents.Count(e => (string)e["group"] == "rendering" || (string)e["group"] == "resources"),
                (int)both["totalMatched"], "Multiple groups use OR, before pagination");
            t.Equal(2, Events(both).Count, "Groups preserve pagination");
            var pageIds = new List<long>(); int pageOffset = 0;
            do
            {
                var data = Call("read", "groups", new[] { "resources" }, "offset", pageOffset, "limit", 73);
                pageIds.AddRange(Events(data).Select(e => (long)e["id"]));
                pageOffset = data["nextOffset"].Type == "Null" ? -1 : (int)data["nextOffset"];
            } while (pageOffset >= 0);
            t.True(pageIds.SequenceEqual(fullEvents.Where(e => (string)e["group"] == "resources").Select(e => (long)e["id"])), "Pages contain every matching group event once");
            var filtered = Call("read", "groups", new[] { "rendering" }, "phase", "layer.render", "tree", true);
            t.Equal(fullEvents.Count(e => (string)e["phase"] == "layer.render" && (string)e["group"] == "rendering"), (int)filtered["totalMatched"], "Phase and groups combine with AND");
            if (!useExisting) t.True(Events(filtered).Any(e => (string)e["group"] == "resources" && (bool)e["contextOnly"]), "Hidden-group ancestor remains muted context");
            t.True(filtered["summary"].All(e => (string)e["phase"] == "layer.render"), "Context never contributes to summary");
            int rootId = (int)fullEvents.First(e => (string)e["phase"] == "document.render")["id"];
            var folded = Call("read", "groups", new[] { "rendering" }, "phase", "layer.render", "tree", true, "collapsedIds", new[] { rootId });
            t.Equal(filtered["summary"].ToString(), folded["summary"].ToString(), "Groups and folding do not alter aggregate times");
            var none = Call("read", "groups", new string[0], "tree", true);
            t.Equal(0, Events(none).Count, "No groups means empty tree, not all");
            t.Equal(0, none["summary"].Count, "No groups means empty summary");
            t.Equal(fullEvents.Length, none["groups"].Sum(g => (int)g["count"]), "Counts remain available to re-enable hidden groups");
            foreach (object bad in new object[] { "rendering", new[] { "sdf" }, new object[] { 1 }, new object[] { null } })
                t.True(!(bool)Call("read", "groups", bad)["success"], "Invalid groups rejected");
            t.True(!(bool)Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"read\",\"groups\":null}"))["success"], "Null is not an all-groups alias");

            window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            window.ShowUtility(); windowType.GetMethod("CreateGUI").Invoke(window, null);
            var root = window.rootVisualElement;
            var lists = root.Query<ListView>().ToList();
            void Submit(string name)
            {
                var button = root.Q<Button>(name);
                using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e);
            }
            t.Equal(6, root.Query<Toggle>(className: "whimtex-profiler-group-toggle").ToList().Count, "Six visible group toggles");
            windowType.GetMethod("ReadPage", Instance).Invoke(window, new object[] { 200 });
            windowType.GetMethod("CollapseAllBranches", Instance).Invoke(window, null);
            Submit("groups-none");
            root.Q<Toggle>("group-resources").value = true;
            t.Equal(0, (int)windowType.GetField("offset", Instance).GetValue(window), "Group change resets pagination");
            t.Equal(Events(Call("read", "groups", new[] { "resources" }, "tree", true)).Count, lists[1].itemsSource.Count,
                "Group change unfolds matches instead of leaving them hidden");
            t.True(lists[0].itemsSource.Cast<object>().All(e => (string)Json.Parse(e.ToString())["group"] == "resources"), "Single selection filters summary");
            t.True(root.Q<Toggle>("group-resources").value && !root.Q<Toggle>("group-rendering").value, "None then one checkbox isolates a group");
            root.Q<Toggle>("group-rendering").value = true;
            root.Q<Toggle>("group-resources").value = false;
            t.True(lists[0].itemsSource.Cast<object>().All(e => (string)Json.Parse(e.ToString())["group"] == "rendering"), "Toggles combine then hide groups");
            var saved = EditorJsonUtility.ToJson(window);
            var restored = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            try
            {
                EditorJsonUtility.FromJsonOverwrite(saved, restored); windowType.GetMethod("CreateGUI").Invoke(restored, null);
                t.True(restored.rootVisualElement.Q<Toggle>("group-rendering").value && !restored.rootVisualElement.Q<Toggle>("group-resources").value,
                    "Group selection survives serialized window reconstruction");
            }
            finally { UnityEngine.Object.DestroyImmediate(restored); }
            windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
            t.True(!root.Q<Toggle>("group-resources").value, "Refresh preserves selection");
            Submit("groups-none"); t.True(lists.All(l => l.itemsSource.Count == 0), "None clears both views");
            root.Q<Toggle>("group-ui").value = true;
            t.Equal(Call("read", "groups", new[] { "ui" })["summary"].Count, lists[0].itemsSource.Count, "One checkbox works from no selection, including empty groups");
            t.True(!root.Query<Button>().ToList().Any(b => b.name != null && b.name.StartsWith("group-only-")), "No redundant Only buttons");
            Submit("groups-all"); t.True(root.Query<Toggle>(className: "whimtex-profiler-group-toggle").ToList().All(toggle => toggle.value), "All restores every checkbox");
            t.Equal(full["events"].ToString(), Call("read", "limit", 2000)["events"].ToString(), "UI filtering never modifies capture");
            t.Equal(full["capture"].ToString(), Call("status")["capture"].ToString(), "Capture identity, counts and state unchanged");
        }
        finally
        {
            if (window != null) window.Close();
            if (!useExisting) { Call("stop", "captureId", id); Call("clear", "captureId", id); }
            if (previousFocus != null) previousFocus.Focus();
        }
    }

    public static string Run()
    {
        if (Call("status")["capture"]?.Type != JTokenType.Null)
            return TestContext.Result("skipped", 0, "A user capture exists; leave it untouched.").ToJson();
        return TestContext.Run("Bounded profiling / render parity / agent control", t =>
        {
            string id = null;
            using var build = new OwnedDocument(48);
            var document = build.Document;
            var texture = new Texture2D(48, 48, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[48 * 48];
            for (int y = 0; y < 48; y++) for (int x = 0; x < 48; x++)
                pixels[y * 48 + x] = new Color32(255, 255, 255, (byte)(x > 10 && x < 34 && y > 11 && y < 35 ? 255 : 0));
            texture.SetPixels32(pixels); texture.Apply();
            var file = new FileLayerBehaviour { sourceTexture = texture };
            var sdf = new SDFLayerBehaviour { inputMode = EffectInputMode.Specific, metric = DistanceMetric.Sharp };
            document.layers.Add(sdf); document.layers.Add(file);
            var normalize = typeof(WhimTexDocument).GetMethod("NormalizeModel", Instance);
            normalize.Invoke(document, null); sdf.TargetLayerId = file.Id;
            file.layerName = "Source alpha"; sdf.layerName = "Outline SDF";
            var cacheType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache", true);
            object cache = Activator.CreateInstance(cacheType, true);
            var render = typeof(WhimTexDocument).GetMethod("RenderCanvasWithCache", Instance);
            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            Color[] Render()
            {
                var rt = (RenderTexture)render.Invoke(document, new object[] { 48, cache, false, null });
                var read = new Texture2D(48, 48, TextureFormat.RGBAFloat, false, true);
                var active = RenderTexture.active;
                try { RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, 48, 48), 0, 0, false); return read.GetPixels(); }
                finally { RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(read); RenderTexture.ReleaseTemporary(rt); }
            }
            try
            {
                Color[] baseline = Render();
                t.True(Call("status")["capture"].Type == JTokenType.Null, "Disabled instrumentation creates no capture");
                t.True(!(bool)Call("start", "maxEvents", 255)["success"], "Reject undersized buffer");
                var start = Call("start", "maxEvents", 8192, "durationSeconds", 120, "documentId", DocumentId(document));
                t.True((bool)start["success"], "Capture starts without Unity Profiler"); id = (string)start["capture"]["captureId"];
                t.True(!(bool)Call("start")["success"], "Cannot replace active capture");
                t.True(!(bool)Call("stop", "captureId", "stale")["success"], "Stale caller cannot stop capture");
                t.True(!(bool)Call("clear", "captureId", id)["success"], "Cannot clear active capture");
                using (Span("test.root", document))
                {
                    using (Span("test.child")) { Event("hit"); }
                    using (Span("test.fx", document, sdf.Owner, 2))
                        using (Span("test.dependency", document, file.Owner)) Event("input");
                    try { using (Span("test.throw")) throw new InvalidOperationException("owned"); } catch (InvalidOperationException) { }
                    var live = Call("read", "captureId", id);
                    t.True(Events(live).Any(e => (bool)e["completed"] == false && e["durationMs"].Type == JTokenType.Null), "Open spans have no invented duration");
                    long reservedId;
                    int reservedIndex;
                    using (DistanceSpan("test.reserved"))
                    {
                        var pending = Events(Call("read", "captureId", id));
                        reservedIndex = pending.Count - 1;
                        reservedId = (long)pending.Last()["id"];
                        t.True((string)pending.Last()["phase"] == "test.reserved" && !(bool)pending.Last()["completed"] &&
                            pending.Last()["durationMs"].Type == JTokenType.Null, "Distance stage reserves an untimed row before doing work");
                        using (DistanceSpan("test.reserved.child")) { }
                        var stillOpen = Events(Call("read", "captureId", id)).ToArray();
                        t.Equal(reservedId, (long)stillOpen[reservedIndex]["id"], "Child completion does not move its pending parent");
                        t.Equal(reservedId, (long)stillOpen[reservedIndex + 1]["parentId"], "Work belongs to the reserved stage");
                        t.True(!(bool)stillOpen[reservedIndex]["completed"], "Parent remains pending while child is complete");
                    }
                    var filled = Events(Call("read", "captureId", id)).ToArray();
                    t.Equal(reservedId, (long)filled[reservedIndex]["id"], "Completion fills the original slot, not a new event");
                    t.True((bool)filled[reservedIndex]["completed"] && filled[reservedIndex]["durationMs"].Type != JTokenType.Null,
                        "Reserved stage receives its duration on completion");
                }
                ((IDisposable)cache).Dispose();
                typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(document, null);
                Color[] profiled = Render(); Render();
                t.True(profiled.SequenceEqual(baseline), "Instrumentation preserves rendered pixels exactly");
                sdf.threshold = 143; Render();
                sdf.metric = DistanceMetric.MTSDF; sdf.sharpMode = SharpDistanceMode.GuardedSharp; Render();
                var outline = new OutlineLayerBehaviour { inputMode = EffectInputMode.Specific, metric = DistanceMetric.MTSDF,
                    sharpMode = SharpDistanceMode.GuardedSharp };
                document.layers.Insert(0, outline); normalize.Invoke(document, null); outline.TargetLayerId = file.Id;
                Render();
                var stop = Call("stop", "captureId", id); t.True((bool)stop["success"], "Stop succeeds");
                file.layerName = "Renamed after recording";
                int retained = (int)stop["capture"]["eventCount"];
                using (Span("test.after_stop", document)) Event("unused");
                var report = Call("read", "captureId", id, "limit", 2000);
                var events = Events(report);
                t.Equal(retained, events.Count, "Stop prevents further recording");
                t.Equal(0, (int)report["capture"]["openSpans"], "All scopes drained");
                t.True(events.Any(e => (string)e["phase"] == "render.sdf"), "SDF phases included");
                t.True(events.Any(e => (string)e["phase"] == "layer.render" && (string)e["layerId"] == sdf.Id), "Stable layer identity");
                t.True(events.Any(e => (string)e["kind"] == "cache" && (string)e["detail"] == "hit"), "Cache hits visible");
                t.True(events.Any(e => (string)e["detail"] == "miss:content_or_settings"), "Cache invalidation reason visible");
                t.True(events.Any(e => (string)e["phase"] == "test.dependency" && (int)e["fxIndex"] == -1 && (string)e["layerId"] == file.Id), "Dependency layer does not inherit the consuming FX index");
                t.True(events.Where(e => (string)e["layerId"] == file.Id).All(e => (string)e["layerName"] == "Source alpha"), "Layer name snapshot survives rename and is inherited by child events");
                t.True(events.Where(e => (string)e["layerId"] == sdf.Id).All(e => (string)e["layerName"] == "Outline SDF"), "Dependency name does not leak to other layers");
                t.True(events.Where(e => (string)e["phase"] == "document.canvas_update").All(e => (long)e["changeId"] > 0), "Change linked to subsequent renders");
                var byId = events.ToDictionary(e => (long)e["id"]);
                var ordered = events.ToArray();
                t.True(ordered.Zip(ordered.Skip(1), (a, b) => (long)a["id"] < (long)b["id"] &&
                    (double)a["startedMs"] <= (double)b["startedMs"]).All(x => x), "Real SDF/Outline events are registered in start order");
                t.True(events.All(e => (string)e["kind"] != "phase"), "No post-hoc diagnostic intervals in new captures");
                foreach (string phase in new[] { "contour.extract", "contour.traceFit", "contour.normalizeFeatures",
                    "geometry.bvhUpload", "distance.jobs", "mtsdf.correct", "output.setup", "output.upload", "output.submit" })
                    t.True(events.Any(e => (string)e["phase"] == phase && (string)e["kind"] == "span"), "Previously delayed stage is a reserved span: " + phase);
                foreach (var pair in new[] { new[] { "contour.scanJobs", "contour.extract" },
                    new[] { "geometry.cornerParameters", "geometry.bvhUpload" } })
                {
                    var children = events.Where(e => (string)e["phase"] == pair[0]).ToArray();
                    t.True(children.Length > 0 && children.All(e => (string)byId[(long)e["parentId"]]["phase"] == pair[1]),
                        "Nested work is under its reserved stage: " + pair[0]);
                }
                bool UnderStage(Json sample, string phase)
                {
                    for (long parent = (long)sample["parentId"]; parent > 0; parent = (long)byId[parent]["parentId"])
                        if ((string)byId[parent]["phase"] == phase) return true;
                    return false;
                }
                var normalization = events.Where(e => (string)e["phase"] == "contour.normalize").ToArray();
                t.True(normalization.Length > 0 && normalization.All(e => UnderStage(e, "contour.normalizeFeatures")),
                    "Both direct normalization and normalization inside adaptive sharp remain under the reserved stage");
                foreach (var sample in events.Where(e => (string)e["kind"] == "span"))
                {
                    long parent = (long)sample["parentId"];
                    t.True(parent == 0 || byId.ContainsKey(parent), "Parent retained");
                    t.Equal(parent == 0 ? 0 : (int)byId[parent]["depth"] + 1, (int)sample["depth"], "Absolute nesting depth retained");
                    t.Near((double)byId[(long)sample["operationId"]]["durationMs"], (double)sample["operationMs"], 1e-6, "Bar denominator is complete operation");
                    t.True((double)sample["selfMs"] >= 0 && (double)sample["selfMs"] <= (double)sample["durationMs"] + 1e-6, "Nonnegative self <= inclusive");
                    double childrenMs = events.Where(e => (string)e["kind"] == "span" && (long)e["parentId"] == (long)sample["id"])
                        .Sum(e => (double)e["durationMs"]);
                    t.Near((double)sample["durationMs"], (double)sample["selfMs"] + childrenMs, 1e-6, "Reserved stages subtract children exactly once");
                    if (parent > 0) t.Equal((long)byId[parent]["operationId"], (long)sample["operationId"], "Operation propagation");
                }
                double roots = events.Where(e => (string)e["kind"] == "span" && (long)e["parentId"] == 0).Sum(e => (double)e["durationMs"]);
                t.Near(roots, (double)report["rootWallMs"], 1e-6, "Root time never double-counts nested scopes");
                var page = Call("read", "captureId", id, "limit", 3);
                t.Equal(3, Events(page).Count, "Pagination bounded");
                t.Equal(3, (int)page["nextOffset"], "Next offset");
                t.Equal(retained, (int)page["totalMatched"], "Summary covers all matching events");
                var filtered = Call("read", "captureId", id, "layerId", sdf.Id, "phase", "sdf");
                t.True(Events(filtered).Count > 0 && Events(filtered).All(e => (string)e["layerId"] == sdf.Id && ((string)e["phase"]).Contains("sdf")), "Layer and phase filters");
                var named = Events(Call("read", "captureId", id, "layer", "SOURCE AL", "limit", 1));
                t.Equal(1, named.Count, "Case insensitive layer name search supports pagination");
                var nameMatch = named.First(); var original = byId[(long)nameMatch["id"]];
                t.Equal((int)original["depth"], (int)nameMatch["depth"], "Filtered page keeps original indent without ancestors");
                t.Near((double)original["operationMs"], (double)nameMatch["operationMs"], 1e-6, "Filtered page keeps operation bar scale");
                t.True(Events(Call("read", "captureId", id, "layer", file.Id)).All(e => (string)e["layerId"] == file.Id), "Name filter also accepts exact layer ID");
                t.True(!(bool)Call("export", "captureId", id, "outputPath", "Temp/WhimTex/../escape.json")["success"], "Reject escaping export");
                string output = "Temp/WhimTex/profiler-test-" + Guid.NewGuid().ToString("N") + ".json";
                var exported = Call("export", "captureId", id, "outputPath", output);
                t.True((bool)exported["success"], "Export complete capture");
                t.Equal(retained, ((JArray)JObject.Parse(File.ReadAllText((string)exported["outputPath"]))["events"]).Count, "Export is not paginated");
                t.True(!(bool)Call("export", "captureId", id, "outputPath", output)["success"], "Reports never overwritten");
                t.True(!(bool)Call("read", "captureId", id, "limit", 2001)["success"], "Page limit enforced");
                t.True(!(bool)Call("read", "captureId", id, "unexpected", true)["success"], "Unknown fields rejected");
                var saveReload = Profiler.GetMethod("SaveForReload", Hidden);
                var restoreReload = Profiler.GetMethod("RestoreAfterReload", Hidden);
                var currentField = Profiler.GetField("capture", Hidden);
                saveReload.Invoke(null, null);
                currentField.SetValue(null, null);
                restoreReload.Invoke(null, null);
                var reloaded = Call("read", "captureId", id, "limit", 2000);
                t.Equal(report["events"].ToString(), reloaded["events"].ToString(), "Reload snapshot preserves every completed event exactly");
                t.Equal(report["summary"].ToString(), reloaded["summary"].ToString(), "Reload snapshot preserves summary exactly");
                t.Equal(report["capture"].ToString(), reloaded["capture"].ToString(), "Stopped capture metadata and ID survive reload");
                t.True(SessionState.GetString("WhimTex.Profiler.ReloadSnapshot", "").Length == 0, "Restored snapshot is consumed");
                using (var invalid = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"version\":999}")))
                {
                    bool rejected = false;
                    try { Profiler.GetMethod("ReadSnapshot", Hidden).Invoke(null, new object[] { invalid }); }
                    catch (TargetInvocationException error) { rejected = error.InnerException is InvalidDataException; }
                    t.True(rejected, "Unsupported snapshot is rejected without replacing the capture");
                }
                saveReload.Invoke(null, null);
                Call("clear", "captureId", id); id = null;
                restoreReload.Invoke(null, null);
                t.True(Call("status")["capture"].Type == JTokenType.Null, "Clear prevents resurrection of a saved capture");

                id = (string)Call("start")["capture"]["captureId"];
                var interrupted = Span("test.interrupted");
                try { saveReload.Invoke(null, null); }
                finally { interrupted.Dispose(); }
                currentField.SetValue(null, null);
                restoreReload.Invoke(null, null);
                var interruptedReport = Call("read", "captureId", id);
                t.Equal("assembly_reload", (string)interruptedReport["capture"]["stopReason"], "Active capture stops at reload");
                t.Equal(0, (int)interruptedReport["capture"]["openSpans"], "Interrupted scopes cannot keep restored capture locked");
                var interruptedEvent = Events(interruptedReport).Single();
                t.True((bool)interruptedEvent["interrupted"] && !(bool)interruptedEvent["completed"] && interruptedEvent["durationMs"].Type == JTokenType.Null, "Interrupted event remains untimed, not fabricated as complete");
                Call("clear", "captureId", id); id = null;

                id = (string)Call("start")["capture"]["captureId"];
                using (Span("tree.root"))
                {
                    using (Span("tree.large")) for (int i = 0; i < 230; i++) Event("hit");
                    using (Span("tree.sibling"))
                    {
                        using (Span("tree.match")) Event("hit");
                        for (int i = 0; i < 25; i++) Event("hit");
                    }
                }
                using (Span("tree.other")) for (int i = 0; i < 30; i++) Event("hit");
                Call("stop", "captureId", id);
                var fullTree = Call("read", "captureId", id, "tree", true, "limit", 1000);
                var fullEvents = Events(fullTree);
                int largeId = (int)fullEvents.First(e => (string)e["phase"] == "tree.large")["id"];
                int siblingId = (int)fullEvents.First(e => (string)e["phase"] == "tree.sibling")["id"];
                var collapsedTree = Call("read", "captureId", id, "tree", true, "collapsedIds", new[] { largeId }, "limit", 1000);
                t.Equal(fullEvents.Count - 230, Events(collapsedTree).Count, "Collapse removes descendants beyond the first page");
                t.Equal((int)fullTree["totalMatched"], (int)collapsedTree["totalMatched"], "Fold state never changes match count");
                t.Equal(fullTree["summary"].ToString(), collapsedTree["summary"].ToString(), "Fold state never changes summary timings or counts");
                t.True(Events(collapsedTree).Any(e => (string)e["phase"] == "tree.other") && Events(collapsedTree).Any(e => (string)e["phase"] == "tree.sibling"), "Unrelated roots and sibling branches stay visible");
                var tinyPage = Call("read", "captureId", id, "tree", true, "limit", 1);
                t.True((bool)Events(tinyPage).First()["hasChildren"], "A parent is expandable when its children are on another page");
                var afterCollapse = Call("read", "captureId", id, "tree", true, "collapsedIds", new[] { largeId }, "offset", 200);
                t.Equal(0, (int)afterCollapse["offset"], "Stale page offset clamps after collapse");
                var searchTree = Call("read", "captureId", id, "tree", true, "phase", "tree.match");
                t.Equal(1, (int)searchTree["totalMatched"], "Only the match contributes to filtered totals");
                t.Equal(3, (int)searchTree["totalVisible"], "Filtered tree includes the match and both ancestors");
                t.Equal(2, Events(searchTree).Count(e => (bool)e["contextOnly"]), "Ancestors are marked as context, not additional matches");
                var collapsedSearch = Call("read", "captureId", id, "tree", true, "phase", "tree.match", "collapsedIds", new[] { siblingId });
                t.Equal(2, Events(collapsedSearch).Count, "Filtered ancestor can collapse its matching descendant");
                t.Equal(searchTree["summary"].ToString(), collapsedSearch["summary"].ToString(), "Filtered collapse does not change statistics");
                var restored = Call("read", "captureId", id, "tree", true, "collapsedIds", new int[0], "limit", 1000);
                t.True(fullEvents.Select(e => (int)e["id"]).SequenceEqual(Events(restored).Select(e => (int)e["id"])), "Expanding restores every row in recorded order");
                t.True(!(bool)Call("read", "captureId", id, "collapsedIds", new[] { largeId })["success"], "Collapse is opt-in tree behavior");
                t.True(!(bool)Call("read", "captureId", id, "tree", true, "collapsedIds", new[] { 0 })["success"], "Invalid collapsed ID rejected");
                t.True(!(bool)Call("read", "captureId", id, "tree", true, "collapsedIds", new[] { .5 })["success"], "Noninteger collapsed ID rejected");
                Call("clear", "captureId", id); id = null;

                using (var other = new OwnedDocument(8))
                {
                    var s = Call("start", "documentId", DocumentId(document)); id = (string)s["capture"]["captureId"];
                    using (Span("test.filtered", other.Document)) using (Span("test.nested")) Event("hidden");
                    using (Span("test.visible", document)) Event("shown");
                    Call("stop", "captureId", id);
                    var data = Call("read", "captureId", id);
                    t.True(Events(data).All(e => (string)e["documentId"] == DocumentId(document)), "Document filter also suppresses nested unlabelled phases");
                    t.Equal(2, Events(data).Count, "Filtered work omitted");
                    Call("clear", "captureId", id); id = null;
                }
                id = (string)Call("start", "maxEvents", 256)["capture"]["captureId"];
                using (Span("test.capacity")) for (int i = 0; i < 300; i++) Event("hit");
                var capped = Call("read", "captureId", id, "limit", 1000);
                t.Equal(256, Events(capped).Count, "Buffer never grows past bound");
                t.Equal("event_limit", (string)capped["capture"]["stopReason"], "Auto-stop on capacity");
                t.Equal(0, (int)capped["capture"]["openSpans"], "Capacity stop still closes root");
                Call("clear", "captureId", id); id = null;
                id = (string)Call("start")["capture"]["captureId"];
                object activeCapture = Profiler.GetProperty("Current", Hidden).GetValue(null);
                activeCapture.GetType().GetField("deadline", Instance).SetValue(activeCapture, 0L);
                t.Equal("time_limit", (string)Call("status")["capture"]["stopReason"], "Deadline stops idle recording");
                Call("clear", "captureId", id); id = null;

                var windowType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true);
                var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
                try
                {
                    // Build only our unattached UI tree; do not show, focus or borrow a user's window.
                    windowType.GetMethod("CreateGUI").Invoke(window, null);
                    t.Equal(2, window.rootVisualElement.Query<ListView>().ToList().Count, "Window contains virtualized events and summary");
                    t.True(window.rootVisualElement.styleSheets.count > 0, "Profiler style sheet resolves");
                    t.Equal(2, window.rootVisualElement.Query<TwoPaneSplitView>().ToList().Count, "Three independently sized panes use two draggable splits");
                    t.True(window.rootVisualElement.Query<TwoPaneSplitView>().ToList().All(s => s.orientation == TwoPaneSplitViewOrientation.Vertical && s.childCount == 2), "Vertical split hierarchy has two panes per divider");
                    CheckSummarySorting(t, windowType);
                    CheckGroups(t, windowType);
                    windowType.GetMethod("StartCapture", Instance).Invoke(window, null);
                    id = (string)Call("status")["capture"]["captureId"];
                    t.True(id != null, "Window starts capture through shared API");
                    using (Span("test.ui", document, sdf.Owner))
                    {
                        using (Span("render.sdf")) { Event("hit"); Event("miss:content"); }
                    }
                    Call("stop", "captureId", id);
                    windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
                    var lists = window.rootVisualElement.Query<ListView>().ToList();
                    t.True(lists.All(list => list.itemsSource.Count > 0 && list.virtualizationMethod == CollectionVirtualizationMethod.FixedHeight), "Refresh populates both bounded virtualized lists");
                    lists[1].SetSelection(0);
                    var details = window.rootVisualElement.Q<TextField>(className: "whimtex-profiler-detail");
                    t.True(details.value.Contains("operationId"), "Selecting event displays its structured detail");
                    var row = lists[1].makeItem();
                    lists[1].bindItem(row, 0);
                    t.Equal("Outline SDF", row.Q<Label>("layer").text, "Rows show names instead of IDs");
                    t.Near(100, row.Q<ProgressBar>("duration-bar").value, 1e-5, "Root duration bar is full operation");
                    lists[1].bindItem(row, 1);
                    t.True(row.ClassListContains("whimtex-profiler-tone-rendering") && !row.Q("indent-0").ClassListContains("whimtex-profiler-hidden"), "SDF uses general rendering color with depth visible");
                    lists[1].bindItem(row, 2);
                    t.True(row.Q<Button>("branch-toggle").ClassListContains("whimtex-profiler-leaf-toggle"), "Leaf has no expandable arrow");
                    t.True(row.ClassListContains("whimtex-profiler-tone-hit") && !row.ClassListContains("whimtex-profiler-tone-rendering"), "Recycled row resets previous category color");
                    t.True(row.Q<ProgressBar>("duration-bar").ClassListContains("whimtex-profiler-bar-unavailable"), "Instant cache events do not masquerade as timed spans");
                    lists[1].bindItem(row, 3);
                    t.Equal("Miss", row.Q<Label>("kind").text, "Cache miss has text as well as color");
                    lists[1].bindItem(row, 0);
                    t.True(!row.Q<Button>("branch-toggle").ClassListContains("whimtex-profiler-leaf-toggle"), "Recycled parent restores its arrow");
                    t.True(row.Q("indent-0").ClassListContains("whimtex-profiler-hidden") && !row.Q<ProgressBar>("duration-bar").ClassListContains("whimtex-profiler-bar-unavailable"), "Recycled root restores indentation and duration visibility");
                    var toggleBranch = windowType.GetMethod("SetBranchCollapsed", Instance);
                    toggleBranch.Invoke(window, new object[] { 2, true });
                    t.Equal(2, lists[1].itemsSource.Count, "Window hides collapsed descendants");
                    t.True(details.value.Contains("render.sdf"), "Collapsed stage stays selected");
                    windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
                    t.Equal(2, lists[1].itemsSource.Count, "Refresh preserves collapse state");
                    toggleBranch.Invoke(window, new object[] { 1, true });
                    t.Equal(1, lists[1].itemsSource.Count, "Collapsing root hides the whole branch");
                    toggleBranch.Invoke(window, new object[] { 1, false });
                    t.Equal(2, lists[1].itemsSource.Count, "Expanding parent preserves a collapsed child");
                    toggleBranch.Invoke(window, new object[] { 2, false });
                    t.Equal(4, lists[1].itemsSource.Count, "Expanding child restores leaf events");
                    toggleBranch.Invoke(window, new object[] { 1, true });
                    windowType.GetField("phaseFilter", Instance).SetValue(window, "render.sdf");
                    windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
                    t.Equal(2, lists[1].itemsSource.Count, "New search unfolds its matching path");
                    lists[1].bindItem(row, 0);
                    t.True(row.ClassListContains("whimtex-profiler-context-row"), "Search ancestor has contextual styling");
                    var collapseAll = windowType.GetMethod("CollapseAllBranches", Instance);
                    collapseAll.Invoke(window, null);
                    t.Equal(1, lists[1].itemsSource.Count, "Collapse all leaves only the filtered root");
                    windowType.GetField("phaseFilter", Instance).SetValue(window, "");
                    windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
                    Call("clear", "captureId", id); id = null;
                    id = (string)Call("start")["capture"]["captureId"];
                    using (Span("bulk.first")) for (int i = 0; i < 230; i++) Event("hit");
                    using (Span("bulk.second")) using (Span("bulk.child")) Event("hit");
                    Call("stop", "captureId", id);
                    windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
                    windowType.GetMethod("ReadPage", Instance).Invoke(window, new object[] { 200 });
                    var summaryBeforeFold = Call("read", "captureId", id)["summary"].ToString();
                    collapseAll.Invoke(window, null);
                    t.Equal(2, lists[1].itemsSource.Count, "Collapse all reaches roots on other pages");
                    t.Equal(0, (int)windowType.GetField("offset", Instance).GetValue(window), "Collapse all returns to the first page");
                    t.True(lists[1].itemsSource.Cast<object>().All(e => (int)Json.Parse(e.ToString())["parentId"] == 0), "Only roots remain visible");
                    t.Equal(summaryBeforeFold, Call("read", "captureId", id)["summary"].ToString(), "Collapse all leaves statistics unchanged");
                    windowType.GetMethod("RefreshCapture", Instance).Invoke(window, null);
                    t.Equal(2, lists[1].itemsSource.Count, "Refresh retains bulk collapse");
                    toggleBranch.Invoke(window, new object[] { 232, false });
                    t.Equal(3, lists[1].itemsSource.Count, "Opening one root leaves its nested branch collapsed");
                    var buttons = window.rootVisualElement.Query<Button>().ToList();
                    var expandAll = buttons.First(b => b.text == "Expand all");
                    var collapseAllButton = buttons.First(b => b.text == "Collapse all");
                    t.True(expandAll.parent == collapseAllButton.parent && expandAll.parent.IndexOf(collapseAllButton) == expandAll.parent.IndexOf(expandAll) + 1, "Collapse all is beside Expand all");
                    windowType.GetMethod("ExpandAllBranches", Instance).Invoke(window, null);
                    t.Equal(200, lists[1].itemsSource.Count, "Expand all restores the complete first page");
                    windowType.GetMethod("CaptureAction", Instance).Invoke(window, new object[] { "clear" }); id = null;
                    t.True(Call("status")["capture"].Type == JTokenType.Null, "Window clears capture through shared API");
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
            }
            finally
            {
                if (id != null) { Call("stop", "captureId", id); Call("clear", "captureId", id); }
                ((IDisposable)cache).Dispose();
                UnityEngine.Object.DestroyImmediate(texture);
                RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            }
        });
    }
}
