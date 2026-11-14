using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal enum ShaderGraphPanelGrouping { Tabs, Sections, Separate, SideBySide }

    [Serializable]
    internal sealed class ShaderGraphWorkspaceLayout
    {
        public ShaderGraphPanelGrouping grouping;
        public bool nodesFirst, nodesSelected;
        public float sectionRatio = .5f;
        public ShaderGraphPanelPlacement group = new(new Rect(0, 0, 280, 560), ShaderGraphPanelAnchor.Left | ShaderGraphPanelAnchor.Top);
        public ShaderGraphPanelPlacement parameters = new(new Rect(0, 0, 280, 400), ShaderGraphPanelAnchor.Left | ShaderGraphPanelAnchor.Top);
        public ShaderGraphPanelPlacement nodes = new(new Rect(300, 0, 280, 400), ShaderGraphPanelAnchor.Top);
        public ShaderGraphPanelPlacement preview = new(new Rect(0, 0, 256, 276), ShaderGraphPanelAnchor.Right | ShaderGraphPanelAnchor.Bottom);

        internal void Validate()
        {
            var defaults = new ShaderGraphWorkspaceLayout();
            group ??= defaults.group; parameters ??= defaults.parameters; nodes ??= defaults.nodes; preview ??= defaults.preview;
            if (!Enum.IsDefined(typeof(ShaderGraphPanelGrouping), grouping)) grouping = ShaderGraphPanelGrouping.Tabs;
            if (!ProjectiveMatrix.Finite(sectionRatio)) sectionRatio = .5f;
            sectionRatio = Mathf.Clamp(sectionRatio, .05f, .95f);
        }
    }

    internal sealed class ShaderGraphPanelWorkspace
    {
        private readonly VisualElement viewport, parameters, nodes;
        private readonly ShaderGraphWorkspaceLayout layout;
        private readonly Action save;
        private ShaderGraphFloatingPanel groupPanel, parametersPanel, nodesPanel;
        private VisualElement parametersHeader, nodesHeader;
        private readonly Label dropHint;
        private readonly List<VisualElement> dropHits = new();
        private bool rebuilding;
        internal bool NodesVisible => layout.grouping != ShaderGraphPanelGrouping.Tabs || layout.nodesSelected;

        internal ShaderGraphPanelWorkspace(VisualElement viewport, VisualElement parameters, VisualElement nodes,
            ShaderGraphWorkspaceLayout layout, Action save)
        {
            this.viewport = viewport; this.parameters = parameters; this.nodes = nodes;
            this.layout = layout; this.save = save;
            parameters.AddToClassList("whimtex-shader-graph-panel-content");
            nodes.AddToClassList("whimtex-shader-graph-panel-content");
            dropHint = new Label { name = "graph-panel-drop-hint", pickingMode = PickingMode.Ignore };
            dropHint.AddToClassList("whimtex-shader-graph-panel-drop-hint");
            HideDropHint(); Rebuild(); viewport.Add(dropHint);
        }

        private ShaderGraphFloatingPanel CreatePanel(string name, ShaderGraphPanelPlacement placement, int panelId)
        {
            var panel = new ShaderGraphFloatingPanel(placement,
                new Vector2(panelId == -1 && layout.grouping == ShaderGraphPanelGrouping.SideBySide ? 462 : 230,
                    panelId == -1 && layout.grouping == ShaderGraphPanelGrouping.Sections ? 240 : 160)) { name = name };
            panel.AddToClassList("whimtex-shader-graph-tool-panel");
            panel.PlacementChanged += save;
            panel.Dragged += OnDrag;
            panel.DragEnded += OnDrop;
            viewport.Add(panel);
            return panel;
        }

        private static TabView CreateTabs()
        {
            var tabs = new TabView();
            tabs.AddToClassList("whimtex-shader-graph-tabs");
            tabs.contentContainer.AddToClassList("whimtex-shader-graph-panel-content");
            return tabs;
        }

        private Tab AddTab(TabView tabs, bool isNodes)
        {
            var tab = new Tab(isNodes ? "Nodes" : "Parameters");
            tab.AddToClassList("whimtex-shader-graph-tab");
            tab.contentContainer.AddToClassList("whimtex-shader-graph-panel-content");
            tab.tabHeader.AddToClassList("whimtex-shader-graph-panel-title");
            tab.Add(isNodes ? nodes : parameters);
            tabs.Add(tab);
            return tab;
        }

        private VisualElement AddHeader(TabView tabs, ShaderGraphFloatingPanel panel, int panelId)
        {
            // Keep the native tab viewport and its overflow navigation; the menu sits outside it.
            var bar = new VisualElement(); bar.AddToClassList("whimtex-shader-graph-panel-header");
            bar.Add(tabs.contentViewport);
            var grip = new VisualElement { name = "panel-header-grip" };
            grip.AddToClassList("whimtex-shader-graph-panel-header-grip");
            panel.AddDragHandle(grip);
            bar.Add(grip);
            var menu = new ToolbarMenu { name = "panel-menu" };
            menu.AddToClassList("whimtex-layer-menu-button");
            menu.EnableInClassList("whimtex-layer-menu-button--light", !UnityEditor.EditorGUIUtility.isProSkin);
            menu.AddToClassList("whimtex-shader-graph-panel-menu");
            WhimTexUI.AddMenuDots(menu);
            AddMenu(menu.menu, panelId); bar.Add(menu);
            // Square hit area sized from the native Tab header, including the current Editor theme.
            bar.RegisterCallback<GeometryChangedEvent>(e => menu.style.width = e.newRect.height);
            bar.AddManipulator(new ContextualMenuManipulator(e => AddMenu(e.menu, panelId)));
            tabs.hierarchy.Insert(0, bar);
            return bar;
        }

        private TabView BuildSingle(ShaderGraphFloatingPanel panel, bool isNodes, bool detachable,
            out VisualElement header)
        {
            var tabs = CreateTabs(); var tab = AddTab(tabs, isNodes);
            header = AddHeader(tabs, panel, isNodes ? 1 : 0);
            panel.AddDragHandle(tab.tabHeader, detach: detachable ? position => Detach(isNodes, position) : null);
            return tabs;
        }

        private void AddMenu(DropdownMenu menu, int panelId)
        {
            menu.AppendAction("Combine as Tabs", _ => Combine(ShaderGraphPanelGrouping.Tabs, panelId < 0 ? layout.nodesFirst : panelId == 1),
                _ => layout.grouping == ShaderGraphPanelGrouping.Tabs ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.AppendAction("Combine as Sections", _ => Combine(ShaderGraphPanelGrouping.Sections, panelId < 0 ? layout.nodesFirst : panelId == 1),
                _ => layout.grouping == ShaderGraphPanelGrouping.Sections ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.AppendAction("Combine Side by Side", _ => Combine(ShaderGraphPanelGrouping.SideBySide, panelId < 0 ? layout.nodesFirst : panelId == 1),
                _ => layout.grouping == ShaderGraphPanelGrouping.SideBySide ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.AppendAction("Separate Panels", _ => Separate(),
                _ => layout.grouping == ShaderGraphPanelGrouping.Separate ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.AppendSeparator();
            menu.AppendAction("Reset Panel Layout", _ => Reset());
        }

        private void Rebuild()
        {
            rebuilding = true;
            parameters.RemoveFromHierarchy(); nodes.RemoveFromHierarchy();
            groupPanel?.RemoveFromHierarchy(); parametersPanel?.RemoveFromHierarchy(); nodesPanel?.RemoveFromHierarchy();
            groupPanel = parametersPanel = nodesPanel = null;
            if (layout.grouping == ShaderGraphPanelGrouping.Separate)
            {
                parametersPanel = CreatePanel("graph-parameters-panel", layout.parameters, 0);
                parametersPanel.Add(BuildSingle(parametersPanel, false, false, out parametersHeader)); parametersPanel.AddResizeHandles();
                nodesPanel = CreatePanel("graph-nodes-panel", layout.nodes, 1);
                nodesPanel.Add(BuildSingle(nodesPanel, true, false, out nodesHeader)); nodesPanel.AddResizeHandles();
            }
            else
            {
                groupPanel = CreatePanel("graph-tools-group", layout.group, -1);
                if (layout.grouping == ShaderGraphPanelGrouping.Tabs) BuildTabs();
                else BuildSections();
                groupPanel.AddResizeHandles();
            }
            rebuilding = false;
        }

        private void BuildTabs()
        {
            var tabs = CreateTabs(); tabs.name = "graph-sidebar-tabs";
            var first = AddTab(tabs, layout.nodesFirst); var second = AddTab(tabs, !layout.nodesFirst);
            var parameterTab = layout.nodesFirst ? second : first;
            var nodeTab = layout.nodesFirst ? first : second;
            AddHeader(tabs, groupPanel, -1);
            tabs.activeTab = layout.nodesSelected ? nodeTab : parameterTab;
            tabs.activeTabChanged += (_, selected) =>
            { layout.nodesSelected = selected == nodeTab; if (!rebuilding) save(); };
            groupPanel.AddDragHandle(parameterTab.tabHeader, detach: position => Detach(false, position), select: () => tabs.activeTab = parameterTab);
            groupPanel.AddDragHandle(nodeTab.tabHeader, detach: position => Detach(true, position), select: () => tabs.activeTab = nodeTab);
            groupPanel.Add(tabs);
        }

        private void BuildSections()
        {
            bool horizontal = layout.grouping == ShaderGraphPanelGrouping.SideBySide;
            float dimension = horizontal ? layout.group.bounds.width : layout.group.bounds.height;
            var split = new TwoPaneSplitView(0, dimension * layout.sectionRatio,
                horizontal ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical) { name = "graph-panel-sections" };
            split.AddToClassList("whimtex-shader-graph-panel-content");
            var first = Section(layout.nodesFirst); var second = Section(!layout.nodesFirst);
            split.Add(first); split.Add(second);
            bool resizing = false;
            var applyRatio = split.schedule.Execute(() =>
            {
                float size = horizontal ? split.resolvedStyle.width : split.resolvedStyle.height;
                if (resizing || size <= 0) return;
                float min = Mathf.Min(horizontal ? 230 : 80, size * .5f);
                split.fixedPaneInitialDimension = Mathf.Clamp(size * layout.sectionRatio, min, size - min);
            });
            applyRatio.Pause();
            // Let the native splitter finish its geometry update before changing its dimension.
            split.RegisterCallback<GeometryChangedEvent>(_ => applyRatio.ExecuteLater(0));
            split.RegisterCallback<PointerDownEvent>(e =>
            {
                var hit = e.target as VisualElement;
                resizing = e.button == 0 && hit != null && hit != first && !first.Contains(hit) && hit != second && !second.Contains(hit);
            }, TrickleDown.TrickleDown);
            void FinishResize()
            {
                if (!resizing) return;
                resizing = false;
                split.schedule.Execute(() =>
                {
                    float size = horizontal ? split.resolvedStyle.width : split.resolvedStyle.height;
                    if (split.panel == null || size <= 0) return;
                    layout.sectionRatio = Mathf.Clamp((horizontal ? first.resolvedStyle.width : first.resolvedStyle.height) / size, .05f, .95f);
                    save();
                });
            }
            split.RegisterCallback<PointerUpEvent>(_ => FinishResize(), TrickleDown.TrickleDown);
            split.RegisterCallback<PointerCaptureOutEvent>(_ => FinishResize());
            groupPanel.Add(split);
        }

        private VisualElement Section(bool isNodes)
        {
            var section = new VisualElement(); section.AddToClassList("whimtex-shader-graph-panel-section");
            section.EnableInClassList("whimtex-shader-graph-panel-section--horizontal", layout.grouping == ShaderGraphPanelGrouping.SideBySide);
            section.Add(BuildSingle(groupPanel, isNodes, true, out _));
            return section;
        }

        internal void Combine(ShaderGraphPanelGrouping grouping, bool nodesFirst)
        {
            if (layout.grouping == ShaderGraphPanelGrouping.Separate)
            {
                var source = nodesFirst ? nodesPanel : parametersPanel;
                layout.group.bounds = source.Bounds; layout.group.anchor = source.Placement.anchor;
            }
            layout.grouping = grouping; layout.nodesFirst = nodesFirst;
            Rebuild(); save();
        }

        internal void Separate()
        {
            if (layout.grouping == ShaderGraphPanelGrouping.Separate) return;
            Rect bounds = groupPanel.Bounds;
            layout.parameters.bounds = bounds; layout.parameters.anchor = groupPanel.Placement.anchor;
            bounds.position += new Vector2(bounds.width + 12, 0);
            layout.nodes.bounds = bounds; layout.nodes.anchor = ShaderGraphPanelAnchor.None;
            layout.grouping = ShaderGraphPanelGrouping.Separate;
            Rebuild(); save();
        }

        private ShaderGraphFloatingPanel Detach(bool isNodes, Vector2 position)
        {
            Rect original = groupPanel.Bounds;
            var anchor = groupPanel.Placement.anchor;
            layout.parameters.bounds = layout.nodes.bounds = original;
            layout.parameters.anchor = layout.nodes.anchor = anchor;
            var moving = isNodes ? layout.nodes : layout.parameters;
            moving.bounds.position = viewport.WorldToLocal(position) - new Vector2(40, 12);
            moving.anchor = ShaderGraphPanelAnchor.None;
            layout.grouping = ShaderGraphPanelGrouping.Separate;
            Rebuild();
            return isNodes ? nodesPanel : parametersPanel;
        }

        private ShaderGraphFloatingPanel DropTarget(ShaderGraphFloatingPanel moving, Vector2 position,
            out ShaderGraphPanelGrouping grouping, out bool before, out Rect highlight)
        {
            grouping = ShaderGraphPanelGrouping.Tabs; before = false; highlight = default;
            if (layout.grouping != ShaderGraphPanelGrouping.Separate) return null;
            var other = moving == parametersPanel ? nodesPanel : moving == nodesPanel ? parametersPanel : null;
            if (other == null || !other.worldBound.Contains(position)) return null;
            dropHits.Clear(); viewport.panel.PickAll(position, dropHits);
            foreach (var hit in dropHits)
            {
                var panel = hit as ShaderGraphFloatingPanel ?? hit.GetFirstAncestorOfType<ShaderGraphFloatingPanel>();
                if (panel == null || panel == moving) continue;
                if (panel != other) return null;
                break;
            }
            var header = other == nodesPanel ? nodesHeader : parametersHeader;
            Rect bounds = other.worldBound;
            if (position.y < header.worldBound.yMax)
                highlight = header.worldBound;
            else
            {
                float top = header.worldBound.yMax;
                float x = (position.x - bounds.x) / bounds.width;
                float y = (position.y - top) / Mathf.Max(1, bounds.yMax - top);
                bool horizontal = Mathf.Min(x, 1 - x) < Mathf.Min(y, 1 - y);
                grouping = horizontal ? ShaderGraphPanelGrouping.SideBySide : ShaderGraphPanelGrouping.Sections;
                before = horizontal ? x < .5f : y < .5f;
                highlight = horizontal
                    ? new Rect(before ? bounds.x : bounds.center.x, top, bounds.width * .5f, bounds.yMax - top)
                    : new Rect(bounds.x, before ? top : (top + bounds.yMax) * .5f, bounds.width, (bounds.yMax - top) * .5f);
            }
            return other;
        }

        private void OnDrag(ShaderGraphFloatingPanel moving, Vector2 position)
        {
            if (DropTarget(moving, position, out var grouping, out bool before, out var rect) == null)
            { HideDropHint(); return; }
            dropHint.text = grouping == ShaderGraphPanelGrouping.Tabs ? "Tabs" :
                grouping == ShaderGraphPanelGrouping.SideBySide ? (before ? "Left" : "Right") : (before ? "Above" : "Below");
            dropHint.style.display = DisplayStyle.Flex;
            dropHint.style.left = viewport.WorldToLocal(rect.position).x; dropHint.style.top = viewport.WorldToLocal(rect.position).y;
            dropHint.style.width = rect.width; dropHint.style.height = rect.height; dropHint.BringToFront();
        }

        private void OnDrop(ShaderGraphFloatingPanel moving, Vector2 position, bool cancelled)
        {
            HideDropHint();
            if (cancelled) return;
            var other = DropTarget(moving, position, out var grouping, out bool before, out _);
            if (other == null) return;
            bool movingNodes = moving == nodesPanel;
            layout.group.bounds = other.Bounds; layout.group.anchor = other.Placement.anchor;
            layout.grouping = grouping; layout.nodesFirst = before ? movingNodes : !movingNodes;
            layout.nodesSelected = movingNodes;
            Rebuild(); save();
        }

        private void HideDropHint() => dropHint.style.display = DisplayStyle.None;
        internal void Reset()
        {
            var defaults = new ShaderGraphWorkspaceLayout();
            layout.group = defaults.group; layout.parameters = defaults.parameters; layout.nodes = defaults.nodes;
            layout.grouping = ShaderGraphPanelGrouping.Tabs; layout.nodesFirst = layout.nodesSelected = false;
            layout.sectionRatio = defaults.sectionRatio;
            Rebuild(); save();
        }
    }
}
