using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Isolates panel interaction from shader preparation. Uses production panels and native fields.
public sealed class ShaderGraphPanelBenchmarkWindow : EditorWindow { public void CreateGUI() { } }
public static class ShaderGraphPanelBenchmark
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type Type(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    static void Pointer(VisualElement receiver, EventType type, Vector2 position)
    {
        var source = new Event { type = type, mousePosition = position, button = 0 };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(source); e.target = receiver; receiver.SendEvent(e); }
        if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(source); e.target = receiver; receiver.SendEvent(e); }
        if (type == EventType.MouseUp) { using var e = PointerUpEvent.GetPooled(source); e.target = receiver; receiver.SendEvent(e); }
    }
    sealed class Probe : ImmediateModeElement
    {
        internal int detaches, saves;
        internal EditorWindow owner;
        TaskCompletionSource<long> pending;
        internal Probe()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = 0; style.top = 0; style.width = 2; style.height = 2;
        }
        protected override void ImmediateRepaint()
        {
            var completion = pending; pending = null; long timestamp = Stopwatch.GetTimestamp();
            completion?.TrySetResult(timestamp);
        }
        internal async Task<long> Paint()
        {
            pending = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously); var task = pending.Task; MarkDirtyRepaint(); owner.Repaint();
            if (await Task.WhenAny(task, Task.Delay(4000)) != task) throw new Exception("Owned UI paint not received: " + worldBound + " parent=" + parent?.worldBound + " root=" + owner.rootVisualElement.worldBound + " attached=" + (panel != null) + " window=" + owner.position);
            return await task;
        }
    }
    [Serializable] public sealed class Sample
    { public string action; public double cpuMs, repaintMs; public long allocatedBytes; public int detaches, saves; }
    [Serializable] public sealed class Report
    { public string unity, utc, fixture; public Sample[] samples; }
    public static async Task<object> Run()
    {
        ShaderGraphPanelBenchmarkWindow window = null; object workspace = null;
        string pref = "WhimTex.PanelBenchmark." + Guid.NewGuid().ToString("N");
        var samples = new List<Sample>();
        try
        {
            window = ScriptableObject.CreateInstance<ShaderGraphPanelBenchmarkWindow>();
            window.titleContent = new GUIContent("Panel interaction benchmark");
            window.ShowUtility(); window.position = new Rect(50, 50, 1280, 800);
            var root = window.rootVisualElement; root.AddToClassList("whimtex-shader-graph");
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/ShaderGraphWindow.uss"));
            var viewport = new VisualElement(); viewport.style.flexGrow = 1; root.Add(viewport);
            // A busy canvas behind the panels; no shader work or document changes.
            for (int i = 0; i < 300; i++)
            {
                var card = new VisualElement(); card.style.position = Position.Absolute;
                card.style.left = (i % 20) * 63; card.style.top = (i / 20) * 50;
                card.style.width = 60; card.style.height = 45; card.Add(new Label("Node " + i));
                for (int p = 0; p < 4; p++) card.Add(new Label("Port " + p)); viewport.Add(card);
            }
            var kind = Type("ShaderGraphPanelKind");
            var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(kind, typeof(VisualElement)));
            var probe = new Probe { owner = window };
            for (int i = 0; i < 3; i++)
            {
                var scroll = new ScrollView();
                for (int p = 0; p < 48; p++) scroll.Add(new Vector3Field("Parameter " + p));
                scroll.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == scroll) probe.detaches++; });
                map.Add(Enum.ToObject(kind, i), scroll);
            }
            var layout = Activator.CreateInstance(Type("ShaderGraphWorkspaceLayout"));
            Action save = () => { probe.saves++; EditorPrefs.SetString(pref, JsonUtility.ToJson(layout)); };
            workspace = Activator.CreateInstance(Type("ShaderGraphPanelWorkspace"), F, null,
                new object[] { viewport, map, layout, save }, null);
            root.Add(probe); await Task.Delay(150); await probe.Paint(); await probe.Paint();
            Tab Tab(string label) => viewport.Query<Tab>().ToList().Single(t => t.label == label);
            VisualElement Panel(string name) => viewport.Q(name);
            async Task Measure(string name, Action action, bool record)
            {
                int d = probe.detaches, s = probe.saves; long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp(); action(); long end = Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                long painted = await probe.Paint(); await probe.Paint();
                if (record) samples.Add(new Sample { action = name, cpuMs = Milliseconds(end - start),
                    repaintMs = Milliseconds(painted - start), allocatedBytes = allocated,
                    detaches = probe.detaches - d, saves = probe.saves - s });
            }
            async Task Reset()
            {
                Call(workspace, "Reset");
                var host = ((IList)Get(layout, "hosts"))[0];
                var node = Call(layout, "Find", Get(host, "root"));
                Call(workspace, "Arrange", Get(node, "id"), Enum.Parse(Type("ShaderGraphPanelGrouping"), "Tabs"));
                var placement = Get(((IList)Get(layout, "hosts"))[0], "placement");
                Set(placement, "anchor", Enum.ToObject(Type("ShaderGraphPanelAnchor"), 5));
                Set(placement, "bounds", new Rect(0, 0, 320, 650));
                Call(workspace, "Rebuild"); await probe.Paint(); await probe.Paint();
            }
            for (int round = 0; round < 14; round++)
            {
                bool record = round >= 2; await Reset();
                var tab = Tab("Inspector");
                await Measure("select", () =>
                {
                    Pointer(tab.tabHeader, EventType.MouseDown, tab.tabHeader.worldBound.center);
                    Pointer(viewport, EventType.MouseUp, tab.tabHeader.worldBound.center);
                }, record);
                var to = viewport.LocalToWorld(new Vector2(700, 80));
                await Measure("detach", () =>
                {
                    Pointer(tab.tabHeader, EventType.MouseDown, tab.tabHeader.worldBound.center);
                    Pointer(viewport, EventType.MouseDrag, to); Pointer(viewport, EventType.MouseUp, to);
                }, record);
                var floating = Panel("graph-inspector-panel"); var grip = floating.Q("panel-header-grip");
                Vector2 start = grip.worldBound.center;
                Pointer(grip, EventType.MouseDown, start);
                for (int move = 0; move < 8; move++)
                {
                    Vector2 point = start + new Vector2(10 + move * 2, 5 + move);
                    await Measure("move", () => Pointer(viewport, EventType.MouseDrag, point), record);
                }
                Pointer(viewport, EventType.MouseUp, start + new Vector2(24, 12)); await probe.Paint();
                var resize = floating.Q("panel-resize-bottom-right"); start = resize.worldBound.center;
                Pointer(resize, EventType.MouseDown, start);
                for (int move = 0; move < 4; move++)
                {
                    Vector2 point = start + new Vector2(5 + move * 2, -5 - move * 2);
                    await Measure("resize", () => Pointer(viewport, EventType.MouseDrag, point), record);
                }
                Pointer(viewport, EventType.MouseUp, start + new Vector2(11, -11)); await probe.Paint();
                var target = Panel("graph-tools-group").Q(className: "whimtex-shader-graph-panel-header").worldBound.center;
                tab = Tab("Inspector");
                await Measure("dock", () =>
                {
                    Pointer(tab.tabHeader, EventType.MouseDown, tab.tabHeader.worldBound.center);
                    Pointer(viewport, EventType.MouseDrag, target); Pointer(viewport, EventType.MouseUp, target);
                }, record);
                if (((IList)Get(layout, "hosts")).Count != 1) throw new Exception("Dock did not merge");
            }
            string directory = "Temp/WhimTex/GraphPanels"; System.IO.Directory.CreateDirectory(directory);
            string path = directory + "/benchmark-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
            string metadata = JsonUtility.ToJson(new Report { unity = Application.unityVersion,
                utc = DateTime.UtcNow.ToString("O"), fixture = "3 production panels, 48 native Vector3 fields each, 300 background cards; no shader work" });
            System.IO.File.WriteAllText(path, metadata.Substring(0, metadata.Length - 1) + ",\"samples\":[" +
                string.Join(",", samples.Select(s => JsonUtility.ToJson(s))) + "]}");
            var statistics = samples.GroupBy(s => s.action).Select(g =>
            {
                double Quantile(Func<Sample, double> read, double q) { var a = g.Select(read).OrderBy(v => v).ToArray(); return a[(int)((a.Length - 1) * q)]; }
                return new { action = g.Key, count = g.Count(), cpuMedian = Quantile(s => s.cpuMs, .5), cpuP95 = Quantile(s => s.cpuMs, .95),
                    repaintMedian = Quantile(s => s.repaintMs, .5), repaintP95 = Quantile(s => s.repaintMs, .95),
                    bytesMedian = Quantile(s => s.allocatedBytes, .5), detaches = g.Sum(s => s.detaches), saves = g.Sum(s => s.saves) };
            }).ToArray();
            return new { status = "passed", checks = samples.Count, failures = Array.Empty<string>(), message = path, statistics };
        }
        finally
        {
            (workspace as IDisposable)?.Dispose();
            if (window != null) window.Close(); EditorPrefs.DeleteKey(pref);
        }
    }
}
