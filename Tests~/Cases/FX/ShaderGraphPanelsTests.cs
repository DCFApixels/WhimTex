using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ShaderGraphPanelsTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Pref = "DCFApixels.WhimTex.ShaderGraph.PanelLayout";
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static Type Type(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static System.Collections.Generic.IEnumerable<string> Ancestors(VisualElement e)
    { for (; e != null; e = e.parent) yield return e.GetType().Name + ":" + e.name + ":" + string.Join(",", e.GetClasses()) + ":" + e.worldBound + ":grow=" + e.resolvedStyle.flexGrow + ":content=" + e.contentContainer.name; }
    static readonly System.Collections.Generic.List<string> trace = new();
    static Task Layout() => Task.Delay(120);
    static void Pointer(VisualElement receiver, EventType type, Vector2 position)
    {
        var source = new Event { type = type, mousePosition = position, button = 0 };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(source); e.target = receiver; receiver.SendEvent(e); }
        if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(source); e.target = receiver; receiver.SendEvent(e); }
        if (type == EventType.MouseUp) { using var e = PointerUpEvent.GetPooled(source); e.target = receiver; receiver.SendEvent(e); }
    }
    static async Task Drag(VisualElement handle, VisualElement viewport, Vector2 to)
    {
        trace.Add("down " + handle.name + " at " + handle.worldBound.center + " to " + to);
        Pointer(handle, EventType.MouseDown, handle.worldBound.center);
        trace.Add("captured=" + viewport.HasPointerCapture(PointerId.mousePointerId));
        Pointer(viewport, EventType.MouseDrag, to);
        trace.Add("moved captured=" + viewport.HasPointerCapture(PointerId.mousePointerId));
        await Layout();
        Pointer(viewport, EventType.MouseUp, to);
        await Layout();
    }

    static System.Collections.IList Hosts(object layout) => (System.Collections.IList)Get(layout, "hosts");
    static object Node(object layout, string id) => Call(layout, "Find", id);
    static object RootNode(object layout) => Node(layout, (string)Get(Hosts(layout)[0], "root"));
    static string ChildId(object node, int index) => (string)((System.Collections.IList)Get(node, "children"))[index];
    static object Child(object layout, object node, int index) => Node(layout, ChildId(node, index));
    static float Weight(object node, int index) => (float)((System.Collections.IList)Get(node, "weights"))[index];
    static void Weights(object node, params float[] values) => Set(node, "weights", new System.Collections.Generic.List<float>(values));
    static object TabNode(object layout, string name) => ((System.Collections.IList)Get(layout, "treeNodes")).Cast<object>()
        .Single(n => ((System.Collections.IList)Get(n, "tabs")).Cast<object>().Any(k => k.ToString() == name));
    static Tab TabFor(VisualElement root, string name) => root.Query<Tab>().ToList().Single(t => t.label == name);
    static async Task TabbedLayout(object workspace, object layout)
    {
        Call(workspace, "Reset");
        Call(workspace, "Arrange", Get(RootNode(layout), "id"), Enum.Parse(Type("ShaderGraphPanelGrouping"), "Tabs"));
        var placement = Get(Hosts(layout)[0], "placement");
        Set(placement, "anchor", Enum.Parse(Type("ShaderGraphPanelAnchor"), "Left, Top"));
        Set(placement, "bounds", new Rect(0, 0, 320, 560));
        Call(workspace, "Rebuild"); await Layout();
    }
    static VisualElement Region(VisualElement root, object node) => root.Query<VisualElement>().ToList().Single(e => ReferenceEquals(e.userData, node));
    static VisualElement Divider(TwoPaneSplitView split) => split.Query(className: "unity-two-pane-split-view__dragline-anchor").ToList()
        .Single(e => e.GetFirstAncestorOfType<TwoPaneSplitView>() == split);

    public static async Task<object> Run()
    {
        bool hadPref = EditorPrefs.HasKey(Pref); string originalPref = EditorPrefs.GetString(Pref);
        ShaderGraphWindow window = null; WhimTexDocument document = null;
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        void Near(float a, float b, string label) => Check(Mathf.Abs(a - b) < 1.1f, label + ": " + a + " / " + b);
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 32;
            var shader = new ShaderLayerBehaviour(); document.layers.Add(shader); Call(document, "NormalizeModel");
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>();
            window.name = "WhimTex Panel Test " + Guid.NewGuid().ToString("N"); window.titleContent = new GUIContent("Shader Graph Panel Test");
            Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            Set(window, "panelLayout", Activator.CreateInstance(Type("ShaderGraphWorkspaceLayout")));
            window.ShowUtility(); window.position = new Rect(70, 70, 1080, 760); window.CreateGUI(); await Layout(); await Layout();
            var root = window.rootVisualElement; var viewport = root.Q(className: "whimtex-shader-graph-area");
            var canvas = Get(window, "canvas"); var preview = Get(window, "preview");
            var parameters = root.Q("graph-parameters-content"); var palette = root.Q("node-palette"); var inspector = root.Q("graph-inspector-content");
            string graphBefore = JsonUtility.ToJson(shader.draftGraph);
            var workspace = Get(window, "panelWorkspace"); var layout = Get(window, "panelLayout");
            var groupingType = Type("ShaderGraphPanelGrouping");
            var defaultRoot = RootNode(layout);
            Check(Hosts(layout).Count == 1 && Get(defaultRoot, "grouping").ToString() == "Sections", "Default is one vertically split host");
            var defaultUpper = Child(layout, defaultRoot, 0); var defaultLower = Child(layout, defaultRoot, 1);
            Check(string.Join(",", ((System.Collections.IList)Get(defaultUpper, "tabs")).Cast<object>()) == "Parameters,Inspector", "Default upper tabs and order");
            Check(Get(defaultUpper, "selected").ToString() == "Parameters" && Get(defaultLower, "selected").ToString() == "Nodes", "Default selected tabs");
            Check(Mathf.Abs(Weight(defaultRoot, 0) - .38f) < .00001f, "Default upper weight");
            var defaultHost = root.Q("graph-tools-group"); var defaultSplit = (TwoPaneSplitView)Region(root, defaultRoot);
            Near(defaultHost.worldBound.xMin, viewport.worldBound.xMin, "Default flush left");
            Near(defaultHost.worldBound.height, viewport.worldBound.height, "Default stretches to graph height");
            Near(defaultSplit.fixedPane.resolvedStyle.height, defaultSplit.resolvedStyle.height * .38f, "Default upper height");
            window.position = new Rect(70, 70, 1080, 680); await Layout(); await Layout();
            Near(defaultHost.worldBound.height, viewport.worldBound.height, "Default follows window resize");
            Near(defaultSplit.fixedPane.resolvedStyle.height, defaultSplit.resolvedStyle.height * .38f, "Default proportion follows resize");
            System.IO.Directory.CreateDirectory("Temp/WhimTex/GraphPanels");
            using (var capture = new CancellationTokenSource(5000))
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/GraphPanels/default-layout.png", capture.Token);
            window.position = new Rect(70, 70, 1080, 760); await Layout();
            await TabbedLayout(workspace, layout);
            Check(!parameters.Query<Foldout>().ToList().Any(f => f.text == "Values") && inspector.Contains((VisualElement)Get(window, "parameterValues")), "Inspector owns values");
            var group = root.Q("graph-tools-group"); var header = group.Q(className: "whimtex-shader-graph-panel-header");
            var menu = header.Q<UnityEditor.UIElements.ToolbarMenu>("panel-menu");
            var tabs = root.Q<TabView>(); var nodeTab = TabFor(root, "Nodes"); float tabHeight = nodeTab.tabHeader.resolvedStyle.height;
            Near(header.worldBound.yMin, group.worldBound.yMin + group.resolvedStyle.borderTopWidth, "Single header row");
            Near(menu.worldBound.center.y, nodeTab.tabHeader.worldBound.center.y, "Menu/tab alignment");
            Near(menu.resolvedStyle.width, tabHeight, "Native square menu");
            Check(menu.Query(className: "whimtex-layer-menu-dot").ToList().Count == 3, "Ellipsis retained");
            Check(menu.menu.MenuItems().OfType<DropdownMenuAction>().Count() == 6, "Scoped layout actions");
            Check(header.Q("panel-header-grip").resolvedStyle.width > 0, "Group drag grip");
            tabs.activeTab = nodeTab; await Layout();
            Check((bool)workspace.GetType().GetProperty("NodesVisible", F).GetValue(workspace), "Selected Nodes visible");
            Near(nodeTab.tabHeader.resolvedStyle.borderBottomWidth, 0, "Active tab joins content");
            root.Q<UnityEditor.UIElements.ToolbarSearchField>().value = "Perlin";

            // Two tabs above one section, built entirely by normal pointer gestures.
            await Drag(TabFor(root, "Inspector").tabHeader, viewport, viewport.LocalToWorld(new Vector2(700, 80)));
            Check(Hosts(layout).Count == 2 && ((System.Collections.IList)Get(TabNode(layout, "Nodes"), "tabs")).Count == 2, "Detach only Inspector");
            group = root.Q("graph-tools-group");
            await Drag(TabFor(root, "Inspector").tabHeader, viewport, new Vector2(group.worldBound.center.x, group.worldBound.yMax - 30));
            var outer = RootNode(layout);
            Check(Get(outer, "grouping").ToString() == "Sections", "Vertical outer split");
            Check(((System.Collections.IList)Get(Child(layout, outer, 0), "tabs")).Count == 2, "Upper tabs remain grouped");
            Check(parameters.GetFirstAncestorOfType<TabView>() == palette.GetFirstAncestorOfType<TabView>(), "Upper panels share TabView");
            Check(inspector.GetFirstAncestorOfType<TabView>() != palette.GetFirstAncestorOfType<TabView>(), "Lower independent TabView");
            Check(Region(root, TabNode(layout, "Nodes")).worldBound.yMax <= inspector.worldBound.yMin + 1, "No overlapping sections");
            parameters.GetFirstAncestorOfType<TabView>().activeTab = TabFor(root, "Parameters"); await Layout();
            Check(!(bool)workspace.GetType().GetProperty("NodesVisible", F).GetValue(workspace), "Nodes visibility follows its own stack");
            var directory = "Temp/WhimTex/GraphPanels"; System.IO.Directory.CreateDirectory(directory);
            using (var capture = new CancellationTokenSource(5000))
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, directory + "/tabs-above-inspector.png", capture.Token);

            // Reparent one tab into the lower group without flattening the upper section.
            await Drag(TabFor(root, "Nodes").tabHeader, viewport, viewport.LocalToWorld(new Vector2(710, 70)));
            await Drag(TabFor(root, "Nodes").tabHeader, viewport,
                inspector.GetFirstAncestorOfType<TabView>().Q(className: "whimtex-shader-graph-panel-header").worldBound.center);
            Check(((System.Collections.IList)Get(TabNode(layout, "Parameters"), "tabs")).Count == 1, "Upper section stays separate");
            Check(ReferenceEquals(TabNode(layout, "Nodes"), TabNode(layout, "Inspector")), "Lower tabs grouped independently");
            string bottomId = (string)Get(TabNode(layout, "Nodes"), "id");
            string topId = (string)Get(TabNode(layout, "Parameters"), "id");
            Call(workspace, "Arrange", bottomId, Enum.Parse(groupingType, "SideBySide")); await Layout();
            outer = RootNode(layout);
            Check(ChildId(outer, 0) == topId && Get(Child(layout, outer, 1), "grouping").ToString() == "SideBySide", "Scoped menu changes lower section only");
            Call(workspace, "Arrange", ChildId(outer, 1), Enum.Parse(groupingType, "Tabs")); await Layout();
            bottomId = (string)Get(TabNode(layout, "Nodes"), "id");
            Call(workspace, "DetachGroup", bottomId); await Layout();
            Check(Hosts(layout).Count == 2 && ReferenceEquals(TabNode(layout, "Nodes"), TabNode(layout, "Inspector")), "Detach entire tab group");
            // Moving the grouped host alongside Parameters keeps its inner tabs.
            var bottomGrip = Region(root, TabNode(layout, "Nodes")).Q("panel-header-grip");
            var paramHost = root.Q("graph-parameters-panel");
            await Drag(bottomGrip, viewport, new Vector2(paramHost.worldBound.xMax - 30, paramHost.worldBound.center.y));
            outer = RootNode(layout);
            Check(Get(outer, "grouping").ToString() == "SideBySide", "Horizontal split preserves tab group");
            Check(ReferenceEquals(TabNode(layout, "Nodes"), TabNode(layout, "Inspector")), "Inner tabs survive docking");

            // Requested full-width lower section: first put Parameters and Nodes side by side.
            await TabbedLayout(workspace, layout);
            await Drag(TabFor(root, "Inspector").tabHeader, viewport, viewport.LocalToWorld(new Vector2(700, 70)));
            string pair = (string)Get(TabNode(layout, "Parameters"), "id");
            Call(workspace, "Arrange", pair, Enum.Parse(groupingType, "SideBySide")); await Layout();
            group = root.Q("graph-tools-group");
            await Drag(TabFor(root, "Inspector").tabHeader, viewport, new Vector2(group.worldBound.center.x, group.worldBound.yMax - 3));
            outer = RootNode(layout);
            Check(Get(outer, "grouping").ToString() == "Sections", "Outer bottom-edge drop splits entire host vertically");
            var upper = Child(layout, outer, 0);
            Check(Get(upper, "grouping").ToString() == "SideBySide", "Upper horizontal split retained");
            Check((string)Get(Child(layout, outer, 1), "id") == (string)Get(TabNode(layout, "Inspector"), "id"), "Inspector is lower sibling of both columns");
            var upperRegion = Region(root, upper); var lowerRegion = Region(root, TabNode(layout, "Inspector"));
            Near(upperRegion.worldBound.xMin, lowerRegion.worldBound.xMin, "Full-width section left aligned");
            Near(upperRegion.worldBound.width, lowerRegion.worldBound.width, "Full-width section spans both columns");
            Check(parameters.worldBound.xMax <= palette.worldBound.xMin + 1, "Upper columns do not overlap");
            using (var capture = new CancellationTokenSource(5000))
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, directory + "/columns-above-inspector.png", capture.Token);

            // Resize and dock the mixed hierarchy on both axes; each splitter keeps its own ratio.
            group = root.Q("graph-tools-group");
            await Drag(group.Q("panel-resize-top"), viewport, new Vector2(group.worldBound.center.x, viewport.worldBound.yMin));
            await Drag(group.Q("panel-resize-bottom"), viewport, new Vector2(group.worldBound.center.x, viewport.worldBound.yMax));
            await Drag(group.Q("panel-resize-left"), viewport, new Vector2(viewport.worldBound.xMin, group.worldBound.center.y));
            await Drag(group.Q("panel-resize-right"), viewport, new Vector2(viewport.worldBound.xMax, group.worldBound.center.y));
            Weights(outer, .55f, .45f); Weights(upper, .4f, .6f);
            window.position = new Rect(70, 70, 980, 700); await Layout(); await Layout();
            Near(group.worldBound.width, viewport.worldBound.width, "Mixed host stretches horizontally");
            Near(group.worldBound.height, viewport.worldBound.height, "Mixed host stretches vertically");
            var vertical = (TwoPaneSplitView)Region(root, outer); var horizontal = (TwoPaneSplitView)Region(root, upper);
            Near(vertical.fixedPane.resolvedStyle.height, vertical.resolvedStyle.height * .55f, "Vertical ratio retained");
            Near(horizontal.fixedPane.resolvedStyle.width, horizontal.resolvedStyle.width * .4f, "Horizontal ratio retained");
            var divider = Divider(horizontal); Vector2 start = divider.worldBound.center, end = start + new Vector2(30, 0);
            Pointer(divider, EventType.MouseDown, start); Pointer(divider, EventType.MouseDrag, end); await Layout();
            Pointer(divider, EventType.MouseUp, end); await Layout();
            Check(Weight(upper, 0) > .42f, "Native nested divider saves its ratio");
            Near(Weight(outer, 0), .55f, "Nested divider leaves outer ratio unchanged");
            string saved = JsonUtility.ToJson(layout); Call(window, "SavePanelLayout");
            var restored = Type("WhimTexUserSettings").GetMethod("LoadGraphPanelLayout", F).Invoke(null, null);
            Check(JsonUtility.ToJson(restored) == saved, "Nested preference roundtrip");
            Check(ReferenceEquals(Get(window, "canvas"), canvas) && ReferenceEquals(Get(window, "preview"), preview), "Docking retains canvas and preview");
            Check(ReferenceEquals(root.Q("graph-parameters-content"), parameters) && ReferenceEquals(root.Q("node-palette"), palette), "Content retained during docking");
            Check(root.Q<UnityEditor.UIElements.ToolbarSearchField>().value == "Perlin", "Search preserved");
            Check(JsonUtility.ToJson(shader.draftGraph) == graphBefore, "Graph remains untouched");
            window.CreateGUI(); await Layout();
            viewport = root.Q(className: "whimtex-shader-graph-area");
            Check(root.Query<TwoPaneSplitView>().ToList().Count == 2 && JsonUtility.ToJson(layout) == saved, "Nested hierarchy restored on recreation");

            // Cancelling a detached tab restores the original tree and releases capture.
            workspace = Get(window, "panelWorkspace");
            var tabHandle = TabFor(root, "Inspector").tabHeader; start = tabHandle.worldBound.center;
            Pointer(tabHandle, EventType.MouseDown, start); Pointer(viewport, EventType.MouseDrag, start + new Vector2(90, -40));
            using (var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape })) viewport.SendEvent(escape);
            await Layout();
            Check(!viewport.HasPointerCapture(PointerId.mousePointerId), "Escape releases capture");
            Check(JsonUtility.ToJson(layout) == saved, "Escape restores grouping as well as placement");
            await TabbedLayout(workspace, layout);
            await CheckInspector(window, document, Check, Near);
            await CheckAdditionalPanels(window, Check, Near);
            return new { status = "passed", checks, failures = Array.Empty<string>(), message = "Nested tabs/splits, independent reparenting, resize, persistence, cancellation and Inspector parity." };
        }
        catch (Exception e)
        {
            if (window != null)
            {
                using var token = new CancellationTokenSource(3000);
                try { await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/GraphPanels/failed.png", token.Token); } catch { }
            }
            return new { status = "failed", checks, message = "Graph panel regression", failures = new[] { e.ToString() + "\n" + string.Join("\n", trace.Skip(Math.Max(0, trace.Count - 16))) } };
        }
        finally
        {
            if (window != null) window.Close();
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            if (hadPref) EditorPrefs.SetString(Pref, originalPref); else EditorPrefs.DeleteKey(Pref);
        }
    }
    static async Task CheckAdditionalPanels(ShaderGraphWindow window, Action<bool, string> check, Action<float, float, string> near)
    {
        // Register future panel IDs only in this fixture; production has no special handling for them.
        var kindType = Type("ShaderGraphPanelKind");
        var mapType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(kindType, typeof(VisualElement));
        var map = (System.Collections.IDictionary)Activator.CreateInstance(mapType);
        var kinds = Array.CreateInstance(kindType, 6);
        for (int i = 0; i < 6; i++)
        {
            var kind = Enum.ToObject(kindType, i); kinds.SetValue(kind, i);
            map.Add(kind, new Label("Registered panel " + i));
        }
        var viewport = new VisualElement { name = "additional-panel-test" };
        viewport.style.position = Position.Absolute; viewport.style.left = 0; viewport.style.top = 40;
        viewport.style.width = 1500; viewport.style.height = 640;
        window.position = new Rect(50, 50, 1540, 760); window.rootVisualElement.Add(viewport);
        try
        {
            var layout = Activator.CreateInstance(Type("ShaderGraphWorkspaceLayout"));
            var workspace = Activator.CreateInstance(Type("ShaderGraphPanelWorkspace"), F, null,
                new object[] { viewport, map, layout, (Action)(() => { }) }, null);
            await Layout(); await Layout();
            Call(workspace, "Arrange", Get(RootNode(layout), "id"), Enum.Parse(Type("ShaderGraphPanelGrouping"), "Tabs")); await Layout();
            check(((System.Collections.IList)Get(RootNode(layout), "tabs")).Count == 6, "All registered panels can become tabs");
            foreach (string mode in new[] { "Sections", "SideBySide" })
            {
                Call(workspace, "Arrange", Get(RootNode(layout), "id"), Enum.Parse(Type("ShaderGraphPanelGrouping"), mode));
                await Layout(); await Layout();
                var node = RootNode(layout);
                check(((System.Collections.IList)Get(node, "children")).Count == 6, "Six direct children in " + mode);
                check(viewport.Query<TabView>().ToList().Count == 6, "Six rendered panes in " + mode);
                float sum = ((System.Collections.IList)Get(node, "weights")).Cast<float>().Sum(); near(sum, 1, "Normalized pane weights");
                for (int i = 1; i < 6; i++)
                {
                    var a = Region(viewport, Child(layout, node, i - 1)).worldBound;
                    var b = Region(viewport, Child(layout, node, i)).worldBound;
                    check(mode == "Sections" ? a.yMax <= b.yMin + 1 : a.xMax <= b.xMin + 1, "Future panes do not overlap");
                }
            }
            Call(workspace, "Arrange", Get(RootNode(layout), "id"), Enum.Parse(Type("ShaderGraphPanelGrouping"), "Sections")); await Layout();
            // Moving the sixth pane between existing siblings keeps an n-way group, not a chain of model pairs.
            var first = Region(viewport, Child(layout, RootNode(layout), 0));
            await Drag(TabFor(viewport, "5").tabHeader, viewport, new Vector2(first.worldBound.center.x, first.worldBound.yMax - 20));
            check(Hosts(layout).Count == 1 && ((System.Collections.IList)Get(RootNode(layout), "children")).Count == 6, "Reordering uses an arbitrary-length group");
            var restored = JsonUtility.FromJson(JsonUtility.ToJson(layout), Type("ShaderGraphWorkspaceLayout"));
            Call(restored, "Validate", kinds);
            check(JsonUtility.ToJson(restored) == JsonUtility.ToJson(layout), "Future panels and weights roundtrip");
            // Malformed preference links cannot duplicate panels or recurse forever.
            var damaged = RootNode(restored); ((System.Collections.IList)Get(damaged, "children"))[0] = Get(damaged, "id");
            Call(restored, "Validate", kinds);
            var all = Hosts(restored).Cast<object>().SelectMany(h => ((System.Collections.IList)Call(restored, "Members", Get(h, "root"))).Cast<object>()).ToArray();
            check(all.Length == 6 && all.Distinct().Count() == 6, "Damaged layout recovers each registered panel once");
        }
        finally { viewport.RemoveFromHierarchy(); }
    }

    static async Task CheckInspector(ShaderGraphWindow window, WhimTexDocument document,
        Action<bool, string> check, Action<float, float, string> near)
    {
        var root = window.rootVisualElement;
        var viewport = root.Q(className: "whimtex-shader-graph-area");
        var workspace = Get(window, "panelWorkspace"); var layout = Get(window, "panelLayout");
        var shader = (ShaderLayerBehaviour)document.layers[0].Behaviour;
        shader.draftGraph.edges.Clear();
        shader.draftGraph.nodes.RemoveAll(n => n.kind != ShaderGraphNodeKind.Output);
        shader.draftGraph.parameters.Clear();
        foreach (var kind in (ShaderParameterType[])Enum.GetValues(typeof(ShaderParameterType)))
        {
            var p = new ShaderParameter { name = kind.ToString(), type = kind, vectorValue = new Vector4(1, 2, 3, 4) };
            if (kind == ShaderParameterType.Enum)
                p.controls.Add(new ShaderParameterControl { label = "Enum", type = kind, optionNames = new[] { "First", "Second" }, optionValues = new double[] { 0, 1 } });
            shader.draftGraph.parameters.Add(p);
        }
        Set(window, "rebuildRequested", true); Call(window, "RefreshInterface");
        var tabs = root.Q<TabView>("graph-sidebar-tabs");
        tabs.activeTab = tabs.Query<Tab>().ToList().Single(t => t.label == "Inspector"); await Layout();
        var inspector = root.Q("graph-inspector-content");
        var values = root.Q("graph-inspector-parameters");
        check(!values.GetClasses().Contains("whimtex-shader-graph-properties"), "Inspector retains native field label rules");
        check(Get(TabNode(layout, "Inspector"), "selected").ToString() == "Inspector", "Inspector selection persists");
        check(root.Q("graph-parameters-content").Q<Vector4Field>() == null, "No editable values remain in Parameters");

        // Build the actual Layer Settings fields alongside Inspector, with the same content width.
        var reference = new VisualElement();
        reference.style.position = Position.Absolute; reference.style.left = 370; reference.style.top = 70;
        reference.style.backgroundColor = root.Q("graph-tools-group").resolvedStyle.backgroundColor;
        reference.AddToClassList("whimtex-inspector-section-content"); root.Add(reference);
        var bindings = Activator.CreateInstance(Type("WhimTexUI").GetNestedType("ValueBindings", F));
        Action<string, Action> apply = (_, change) => change();
        Type("ShaderLayerEditorWindow").GetMethod("BuildFields", F).Invoke(null, new object[] { reference, shader, document, apply, bindings });
        var expected = reference.Children().Single(e => e.GetType().Name == "ShaderParameterView");
        foreach (var child in reference.Children().Where(e => e != expected)) child.style.display = DisplayStyle.None;
        try
        {
            foreach (float width in new[] { 360f, 280f, 230f })
            {
                var group = root.Q("graph-tools-group");
                var placement = Get(Hosts(layout)[0], "placement");
                Rect bounds = (Rect)Get(placement, "bounds"); bounds.width = width; Set(placement, "bounds", bounds);
                Call(group, "Place"); await Layout();
                reference.style.width = values.contentRect.width + reference.resolvedStyle.paddingLeft + reference.resolvedStyle.paddingRight;
                await Layout(); await Layout();
                var actualFields = values.Query(className: "unity-base-field").ToList();
                var referenceFields = expected.Query(className: "unity-base-field").ToList();
                check(actualFields.Count == referenceFields.Count && actualFields.Count > 15, "All parameter kinds use Layer Settings controls");
                for (int i = 0; i < actualFields.Count; i++)
                {
                    var a = actualFields[i]; var b = referenceFields[i];
                    check(a.GetType() == b.GetType(), "Field type parity " + i);
                    near(a.resolvedStyle.height, b.resolvedStyle.height, "Field height parity " + i);
                    bool component = a.ClassListContains("unity-composite-field__field");
                    if (!component) near(a.resolvedStyle.width, b.resolvedStyle.width, "Field width parity " + i);
                    var al = a.Q<Label>(className: "unity-base-field__label");
                    var bl = b.Q<Label>(className: "unity-base-field__label");
                    if (al != null && bl != null)
                    {
                        near(al.resolvedStyle.width, component ? bl.resolvedStyle.width : bl.resolvedStyle.width * (88f / 120f),
                            "Inspector label column / native component label " + i);
                        if (al.resolvedStyle.display != DisplayStyle.None && al.worldBound.height > 0)
                            check(al.worldBound.xMax <= values.worldBound.xMax + 1, "Labels fit Inspector at " + width + ": " + al.text);
                    }
                }
                foreach (var composite in values.Query(className: "unity-composite-field__input").ToList())
                {
                    var components = composite.Query(className: "unity-composite-field__field").ToList();
                    if (composite.worldBound.height <= 0 || components.Count == 0) continue;
                    foreach (var component in components)
                        near(component.worldBound.y, components[0].worldBound.y, "Components stay on one row at " + width);
                }
                using var token = new CancellationTokenSource(5000);
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/GraphPanels/inspector-" + width + ".png", token.Token);
            }
            var scalar = values.Query<FloatField>().ToList().First(f => f.label == "Float");
            Undo.IncrementCurrentGroup();
            scalar.value = .375f; await Layout();
            near(shader.draftGraph.parameters.Single(p => p.name == "Float").floatValue, .375f, "Inspector edits graph parameter");
            Call(expected, "Refresh");
            near(expected.Query<FloatField>().ToList().First(f => f.label == "Float").value, .375f, "Layer Settings reads Inspector edits");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Layout(); Call(window, "RefreshInterface");
            near(((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph.parameters.Single(p => p.name == "Float").floatValue, 1, "Inspector value Undo");
            Undo.PerformRedo(); await Layout(); Call(window, "RefreshInterface");
            shader = (ShaderLayerBehaviour)document.layers[0].Behaviour;
            near(shader.draftGraph.parameters.Single(p => p.name == "Float").floatValue, .375f, "Inspector value Redo");
            scalar = values.Query<FloatField>().ToList().First(f => f.label == "Float");
            shader.program.presetId = Guid.NewGuid().ToString("N"); shader.program.presetVersion = 1;
            Call(window, "RefreshInterface");
            check(!root.Q("graph-parameters-content").enabledInHierarchy && scalar.enabledInHierarchy, "Preset declarations are read-only but Inspector values are editable");
            scalar.value = .625f;
            near(shader.draftGraph.parameters.Single(p => p.name == "Float").floatValue, .625f, "Preset Inspector changes instance value");
            shader.program.presetId = null; Call(window, "RefreshInterface");
            // Detaching just Inspector keeps Parameters and Nodes together and retains picker/field instances.
            tabs = root.Q<TabView>("graph-sidebar-tabs");
            await Drag(tabs.activeTab.tabHeader, viewport, viewport.LocalToWorld(new Vector2(720, 70)));
            check(root.Q("graph-inspector-panel") != null && root.Q("graph-tools-group") != null, "Inspector detaches without splitting remaining tabs");
            check(((System.Collections.IList)Get(TabNode(layout, "Nodes"), "tabs")).Count == 2, "Remaining group has two members");
            check(ReferenceEquals(values, root.Q("graph-inspector-parameters")) && values.Contains(scalar), "Inspector controls survive detach");
            await Drag(root.Q("graph-inspector-panel").Q(className: "whimtex-shader-graph-panel-title"), viewport,
                root.Q("graph-tools-group").Q(className: "whimtex-shader-graph-panel-header").worldBound.center);
            check(((System.Collections.IList)Get(TabNode(layout, "Inspector"), "tabs")).Count == 3, "Inspector merges back into tabs");
            Call(window, "SavePanelLayout"); window.CreateGUI(); await Layout();
            check(Get(TabNode(layout, "Inspector"), "selected").ToString() == "Inspector" && root.Q("graph-inspector-parameters") != null, "Inspector restores on UI recreation");
        }
        finally { reference.RemoveFromHierarchy(); }
    }
}
