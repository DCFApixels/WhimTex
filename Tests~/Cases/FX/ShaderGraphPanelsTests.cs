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
            var shader = new ShaderLayerBehaviour(); document.layers.Add(shader);
            Call(document, "NormalizeModel");
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>();
            window.name = "WhimTex Panel Test " + Guid.NewGuid().ToString("N");
            window.titleContent = new GUIContent("Shader Graph Panel Test");
            Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            Set(window, "panelLayout", Activator.CreateInstance(Type("ShaderGraphWorkspaceLayout")));
            window.ShowUtility(); window.position = new Rect(70, 70, 1080, 760); window.CreateGUI();
            await Layout(); await Layout();
            var root = window.rootVisualElement;
            var viewport = root.Q(className: "whimtex-shader-graph-area");
            var canvas = Get(window, "canvas"); var preview = Get(window, "preview");
            string graphBefore = JsonUtility.ToJson(shader.draftGraph);
            var workspace = Get(window, "panelWorkspace"); var layout = Get(window, "panelLayout");
            var groupingType = Type("ShaderGraphPanelGrouping");
            var group = root.Q("graph-tools-group");
            Check(group != null && group.resolvedStyle.width >= 230, "Default grouped panel");
            Near(group.worldBound.xMin, viewport.worldBound.xMin, "Left docking flush");
            var tabs = root.Q<TabView>();
            var nodeTab = tabs.Query<Tab>().ToList().Single(t => t.label == "Nodes");
            var header = group.Q(className: "whimtex-shader-graph-panel-header");
            var menu = header.Q<UnityEditor.UIElements.ToolbarMenu>("panel-menu");
            float tabHeight = nodeTab.tabHeader.resolvedStyle.height;
            Near(header.worldBound.yMin, group.worldBound.yMin + group.resolvedStyle.borderTopWidth, "Tabs are the only group heading");
            Near(header.resolvedStyle.height, tabHeight, "Single header row");
            Near(menu.worldBound.center.y, nodeTab.tabHeader.worldBound.center.y, "Menu aligned with tabs");
            Check(menu.Query(className: "whimtex-layer-menu-dot").ToList().Count == 3 && menu.Q(className: "unity-toolbar-menu__arrow").resolvedStyle.display == DisplayStyle.None, "Menu has only ellipsis");
            Check(menu.menu.MenuItems().OfType<DropdownMenuAction>().Count() == 5, "All layout actions retained");
            Check(header.Q("panel-header-grip").resolvedStyle.width > 0, "Header has a whole-panel drag area");
            Check(menu.worldBound.xMax <= group.worldBound.xMax, "Menu fits compact header");
            Near(menu.resolvedStyle.width, tabHeight, "Menu has a full square hit area");
            var hit = viewport.panel.Pick(menu.worldBound.center);
            Check(hit == menu || menu.Contains(hit), "Resize handles do not cover menu center");
            var parameters = root.Q("graph-parameters-content"); var palette = root.Q("node-palette");
            tabs.activeTab = nodeTab; await Layout();
            Check((bool)Get(layout, "nodesSelected"), "Selected tab retained");
            Check(nodeTab.tabHeader.resolvedStyle.backgroundColor == group.resolvedStyle.backgroundColor, "Active tab joins content background");
            Near(nodeTab.tabHeader.resolvedStyle.borderBottomWidth, 0, "Active tab has no lower divider");
            Check(menu.resolvedStyle.backgroundColor.a == 0, "Menu has no idle background");
            var search = root.Q<UnityEditor.UIElements.ToolbarSearchField>(); search.value = "Perlin";
            await Layout();
            Check(palette.worldBound.width > 200 && palette.worldBound.height > 100, "Palette fills tab: " + palette.worldBound + "; tabs=" + tabs.worldBound + "; group=" + group.worldBound + "; parents=" + string.Join("; ", Ancestors(palette)));

            await Drag(nodeTab.tabHeader, viewport, viewport.LocalToWorld(new Vector2(600, 80)));
            Check(Get(layout, "grouping").ToString() == "Separate", "Tab drag detaches");
            Check(ReferenceEquals(root.Q("node-palette"), palette) && ReferenceEquals(root.Q("graph-parameters-content"), parameters), "Content retained during detach");
            Check(root.Q<UnityEditor.UIElements.ToolbarSearchField>().value == "Perlin", "Search retained");
            var nodes = root.Q("graph-nodes-panel"); var param = root.Q("graph-parameters-panel");
            Check(nodes != null && param != null, "Both detached panels present");
            foreach (var single in new[] { nodes, param })
            {
                var title = single.Q(className: "whimtex-shader-graph-panel-title");
                Near(title.resolvedStyle.height, tabHeight, "Single-panel header matches grouped tabs");
                Near(single.Q("panel-menu").worldBound.center.y, title.worldBound.center.y, "Single-panel menu aligned");
            }
            using (var imageToken = new CancellationTokenSource(5000))
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/GraphPanels/separate.png", imageToken.Token);
            await Drag(nodes.Q(className: "whimtex-shader-graph-panel-title"), viewport,
                param.Q(className: "whimtex-shader-graph-panel-header").worldBound.center);
            Check(Get(layout, "grouping").ToString() == "Tabs", "Header drop merges tabs");
            Check((bool)Get(layout, "nodesSelected"), "Dropped tab selected");

            var movingGroup = root.Q("graph-tools-group");
            var grip = movingGroup.Q("panel-header-grip");
            Vector2 groupBefore = movingGroup.worldBound.position;
            await Drag(grip, viewport, grip.worldBound.center + new Vector2(80, 30));
            Check(Get(layout, "grouping").ToString() == "Tabs", "Blank header drag keeps tabs together");
            Near(movingGroup.worldBound.x, groupBefore.x + 80, "Blank header moves group");
            Call(workspace, "Separate"); await Layout();
            nodes = root.Q("graph-nodes-panel"); param = root.Q("graph-parameters-panel");
            await Drag(nodes.Q(className: "whimtex-shader-graph-panel-title"), viewport,
                new Vector2(param.worldBound.center.x, param.worldBound.yMax - 60));
            Check(Get(layout, "grouping").ToString() == "Sections", "Body drop merges sections");
            Check(!((bool)Get(layout, "nodesFirst")), "Lower section order");
            Check(parameters.worldBound.yMax <= palette.worldBound.yMin + 1, "Sections do not overlap");
            Check(parameters.worldBound.height >= 80 && palette.worldBound.height >= 80, "Both sections usable");
            var firstSection = root.Q<TwoPaneSplitView>().fixedPane;
            await Drag(firstSection.Q(className: "whimtex-shader-graph-panel-title"), viewport, viewport.LocalToWorld(new Vector2(650, 70)));
            Check(Get(layout, "grouping").ToString() == "Separate", "Section can detach");

            var layerPreview = root.Q("shader-layer-preview");
            Check(layerPreview.GetType().BaseType == Type("ShaderGraphFloatingPanel"), "Preview shares panel placement implementation");
            foreach (string panelName in new[] { "graph-parameters-panel", "shader-layer-preview" })
            {
                var panel = root.Q(panelName);
                var handle = panelName == "shader-layer-preview" ? panel.Q<Image>() as VisualElement : panel.Q(className: "whimtex-shader-graph-panel-title");
                handle ??= panel.Q("layer-preview-header");
                await Drag(handle, viewport, viewport.worldBound.max + new Vector2(500, 500));
                Near(panel.worldBound.xMax, viewport.worldBound.xMax, panelName + " right flush");
                Near(panel.worldBound.yMax, viewport.worldBound.yMax, panelName + " bottom flush");
                float oldWidth = panel.worldBound.width;
                await Drag(panel.Q("panel-resize-left"), viewport, panel.Q("panel-resize-left").worldBound.center - new Vector2(35, 0));
                Check(panel.worldBound.width > oldWidth + 30, panelName + " resizes: " + oldWidth + " -> " + panel.worldBound.width + "; anchor=" + Get(Get(layout, "preview"), "anchor"));
                Near(panel.worldBound.xMax, viewport.worldBound.xMax, panelName + " retains right anchor during resize");
                Vector2 before = panel.worldBound.position;
                Pointer(handle, EventType.MouseDown, handle.worldBound.center);
                Pointer(viewport, EventType.MouseDrag, handle.worldBound.center - new Vector2(90, 90));
                using (var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape })) viewport.SendEvent(escape);
                await Layout(); Near(panel.worldBound.x, before.x, "Escape cancels move");
                Check(!viewport.HasPointerCapture(PointerId.mousePointerId), "Capture released after Escape");
            }
            window.position = new Rect(70, 70, 760, 520); await Layout();
            Near(layerPreview.worldBound.xMax, viewport.worldBound.xMax, "Preview follows viewport resize");
            Near(layerPreview.worldBound.yMax, viewport.worldBound.yMax, "Preview bottom follows viewport resize");
            Call(workspace, "Combine", Enum.Parse(groupingType, "Sections"), false); await Layout();
            var toolGroup = root.Q("graph-tools-group");
            await Drag(toolGroup.Q("panel-resize-top"), viewport, new Vector2(toolGroup.worldBound.center.x, viewport.worldBound.yMin));
            await Drag(toolGroup.Q("panel-resize-bottom"), viewport, new Vector2(toolGroup.worldBound.center.x, viewport.worldBound.yMax));
            var anchored = Get(Get(layout, "group"), "anchor").ToString();
            Check(anchored.Contains("Top") && anchored.Contains("Bottom"), "Resize docks both vertical edges: " + anchored + "; group=" + toolGroup.worldBound + "; viewport=" + viewport.worldBound);
            Near(toolGroup.worldBound.yMin, viewport.worldBound.yMin, "Stretched panel top flush");
            Near(toolGroup.worldBound.yMax, viewport.worldBound.yMax, "Stretched panel bottom flush");
            var split = root.Q<TwoPaneSplitView>();
            float ratio = .3f;
            Set(layout, "sectionRatio", ratio);
            window.position = new Rect(70, 70, 950, 720); await Layout(); await Layout();
            Near(toolGroup.worldBound.height, viewport.worldBound.height, "Stretched height tracks growing viewport");
            Near(split.fixedPane.resolvedStyle.height, split.resolvedStyle.height * ratio, "Sections grow proportionally");
            window.position = new Rect(70, 70, 760, 520); await Layout(); await Layout();
            Near(split.fixedPane.resolvedStyle.height, split.resolvedStyle.height * ratio, "Sections shrink proportionally");
            // Native divider dragging updates the persisted ratio rather than locking a pixel height.
            var divider = split.Q(className: "unity-two-pane-split-view__dragline-anchor");
            Check(divider != null, "Native section divider");
            Near(divider.worldBound.yMin, split.fixedPane.worldBound.yMax, "Vertical divider tracks the pane boundary");
            Vector2 dividerStart = divider.worldBound.center, dividerEnd = dividerStart + new Vector2(0, 35);
            Pointer(divider, EventType.MouseDown, dividerStart);
            Pointer(divider, EventType.MouseDrag, dividerEnd);
            await Layout(); Pointer(divider, EventType.MouseUp, dividerEnd); await Layout();
            Check((float)Get(layout, "sectionRatio") > ratio + .04f, "Divider updates saved proportion");
            ratio = (float)Get(layout, "sectionRatio");
            window.position = new Rect(70, 70, 950, 720); await Layout(); await Layout();
            Near(split.fixedPane.resolvedStyle.height, split.resolvedStyle.height * ratio, "User section proportion follows viewport");
            // Side placement uses the same grouping and resize pipeline on the other axis.
            Call(workspace, "Separate"); await Layout();
            nodes = root.Q("graph-nodes-panel"); param = root.Q("graph-parameters-panel");
            await Drag(nodes.Q(className: "whimtex-shader-graph-panel-title"), viewport,
                new Vector2(param.worldBound.xMin + 30, param.worldBound.center.y));
            Check(Get(layout, "grouping").ToString() == "SideBySide", "Side drop joins horizontal sections");
            Check((bool)Get(layout, "nodesFirst"), "Left section order");
            toolGroup = root.Q("graph-tools-group"); split = root.Q<TwoPaneSplitView>();
            Check(split.orientation == TwoPaneSplitViewOrientation.Horizontal, "Native horizontal split");
            await Drag(toolGroup.Q("panel-resize-left"), viewport, new Vector2(viewport.worldBound.xMin, toolGroup.worldBound.center.y));
            await Drag(toolGroup.Q("panel-resize-right"), viewport, new Vector2(viewport.worldBound.xMax, toolGroup.worldBound.center.y));
            anchored = Get(Get(layout, "group"), "anchor").ToString();
            Check(anchored.Contains("Left") && anchored.Contains("Right"), "Resize docks both horizontal edges: " + anchored + "; group=" + toolGroup.worldBound + "; viewport=" + viewport.worldBound);
            Near(toolGroup.worldBound.xMin, viewport.worldBound.xMin, "Stretched panel left flush");
            Near(toolGroup.worldBound.xMax, viewport.worldBound.xMax, "Stretched panel right flush");
            Set(layout, "sectionRatio", .4f);
            window.position = new Rect(70, 70, 1080, 720); await Layout(); await Layout();
            Near(toolGroup.worldBound.width, viewport.worldBound.width, "Horizontal stretch tracks viewport");
            Near(split.fixedPane.resolvedStyle.width, split.resolvedStyle.width * .4f, "Horizontal sections grow proportionally");
            Check(palette.worldBound.xMax <= parameters.worldBound.xMin + 1, "Horizontal sections do not overlap");
            divider = split.Q(className: "unity-two-pane-split-view__dragline-anchor");
            dividerStart = divider.worldBound.center; dividerEnd = dividerStart + new Vector2(50, 0);
            Pointer(divider, EventType.MouseDown, dividerStart); Pointer(divider, EventType.MouseDrag, dividerEnd);
            await Layout(); Pointer(divider, EventType.MouseUp, dividerEnd); await Layout();
            Check((float)Get(layout, "sectionRatio") > .44f, "Horizontal divider saves ratio");
            ratio = (float)Get(layout, "sectionRatio");
            window.position = new Rect(70, 70, 760, 520); await Layout(); await Layout();
            Near(split.fixedPane.resolvedStyle.width, split.resolvedStyle.width * ratio, "Horizontal ratio follows shrinking viewport");
            Near(divider.worldBound.xMin, split.fixedPane.worldBound.xMax, "Horizontal divider tracks the pane boundary");
            foreach (var content in new[] { parameters, palette })
                Check(content.worldBound.xMin >= viewport.worldBound.xMin && content.worldBound.xMax <= viewport.worldBound.xMax + 1, "Content fits narrow viewport");
            Check(ReferenceEquals(Get(window, "canvas"), canvas) && ReferenceEquals(Get(window, "preview"), preview), "Rearranging never recreates canvas or preview");
            Check(JsonUtility.ToJson(shader.draftGraph) == graphBefore, "Workspace operations leave graph untouched");
            Call(window, "SavePanelLayout");
            var restored = Type("WhimTexUserSettings").GetMethod("LoadGraphPanelLayout", F).Invoke(null, null);
            Check(JsonUtility.ToJson(layout) == JsonUtility.ToJson(restored), "Workspace preference roundtrip");
            window.CreateGUI(); await Layout();
            Check(root.Q<TwoPaneSplitView>() != null && root.Q("node-palette") != null, "Layout restores when UI rebuilds");
            // A new window content instance exercises the ordinary node and parameter drop handlers.
            canvas = Get(window, "canvas"); palette = root.Q("node-palette");
            Call(palette, "RefreshIfNeeded");
            Check(!(bool)Call(canvas, "IsEmptyDrop", palette.worldBound.center), "Connections cannot create nodes over a floating panel");
            int nodeCount = shader.draftGraph.nodes.Count;
            var dragData = Activator.CreateInstance(typeof(ShaderGraphWindow).GetNestedType("NodePaletteDragData", F));
            Set(dragData, "palette", palette); Set(dragData, "graph", shader.draftGraph);
            Set(dragData, "choice", ShaderGraphNodeKind.Add); Set(dragData, "svmOnly", false);
            object previousDrag = DragAndDrop.GetGenericData("WhimTex.ShaderGraph.NodePalette");
            try
            {
                DragAndDrop.SetGenericData("WhimTex.ShaderGraph.NodePalette", dragData);
                using var perform = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = new Vector2(400, 200) });
                perform.target = (VisualElement)canvas; ((VisualElement)canvas).SendEvent(perform);
                Check(shader.draftGraph.nodes.Count == nodeCount + 1, "Palette drop still creates a node after grouping");
            }
            finally { DragAndDrop.SetGenericData("WhimTex.ShaderGraph.NodePalette", previousDrag); }
            var parameterDrag = Activator.CreateInstance(typeof(ShaderGraphWindow).GetNestedType("ParameterDragData", F));
            Set(parameterDrag, "window", window); Set(parameterDrag, "id", shader.draftGraph.parameters[0].id);
            previousDrag = DragAndDrop.GetGenericData("WhimTex.ShaderGraph.Parameter");
            try
            {
                DragAndDrop.SetGenericData("WhimTex.ShaderGraph.Parameter", parameterDrag);
                using var perform = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = new Vector2(440, 250) });
                perform.target = (VisualElement)canvas; ((VisualElement)canvas).SendEvent(perform);
                Check(shader.draftGraph.nodes.Count == nodeCount + 2, "Parameter drop still creates a node after grouping");
            }
            finally { DragAndDrop.SetGenericData("WhimTex.ShaderGraph.Parameter", previousDrag); }
            await Layout();

            using (var imageToken = new CancellationTokenSource(5000))
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/GraphPanels/columns.png", imageToken.Token);
            workspace = Get(window, "panelWorkspace");
            Call(workspace, "Combine", Enum.Parse(groupingType, "Sections"), false); await Layout();
            var groupPlacement = Get(layout, "group");
            Set(groupPlacement, "anchor", Enum.Parse(Type("ShaderGraphPanelAnchor"), "Left, Top, Bottom"));
            Call(root.Q("graph-tools-group"), "Place"); await Layout();
            string directory = "Temp/WhimTex/GraphPanels"; System.IO.Directory.CreateDirectory(directory);
            using var cancellation = new CancellationTokenSource(5000);
            await WhimTex.Tests.UnityA.UnityACapture.Capture(window, directory + "/sections.png", cancellation.Token);
            Call(Get(window, "panelWorkspace"), "Combine", Enum.Parse(groupingType, "Tabs"), false); await Layout();
            await WhimTex.Tests.UnityA.UnityACapture.Capture(window, directory + "/tabs.png", cancellation.Token);
            return new { status = "passed", checks, message = "Panel gestures, shared preview placement, retained content, layout persistence and graph isolation." };
        }
        catch (Exception e)
        {
            if (window != null)
            {
                using var token = new CancellationTokenSource(3000);
                try { await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/GraphPanels/failed.png", token.Token); } catch { }
            }
            return new { status = "failed", checks, message = e.ToString() + "\n" + string.Join("\n", trace.Skip(Math.Max(0, trace.Count - 16))) };
        }
        finally
        {
            if (window != null) window.Close();
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            if (hadPref) EditorPrefs.SetString(Pref, originalPref); else EditorPrefs.DeleteKey(Pref);
        }
    }
}
