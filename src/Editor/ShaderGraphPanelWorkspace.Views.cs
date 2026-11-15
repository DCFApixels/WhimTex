using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPanelWorkspace
    {
        private sealed class Pane
        {
            internal ShaderGraphPanelLayoutNode node;
            internal ShaderGraphPanelHostLayout host;
            internal ShaderGraphFloatingPanel panel;
            internal VisualElement root, header;
            internal ShaderGraphPanelGrouping grouping;
            internal readonly List<VisualElement> children = new();
            internal readonly List<SplitBinding> splits = new();
            internal Rect bounds, headerBounds;
            internal void ReleaseSplits() { foreach (var split in splits) split.Dispose(); splits.Clear(); }
        }

        // The model owns structure; stable IDs retain the corresponding view and its local state.
        private void Rebuild()
        {
            ClearTabPreview();
            rebuilding = true;
            var focus = viewport.focusController?.focusedElement as VisualElement;
            try
            {
                layout.Validate(contents.Keys);
                panes.Clear(); regions.Clear();
                var live = new HashSet<string>();
                foreach (var host in layout.hosts)
                {
                    ShaderGraphFloatingPanel panel = null;
                    foreach (var pair in panels) if (ReferenceEquals(pair.Value, host)) { panel = pair.Key; break; }
                    var minimum = Minimum(host.root); minimum.x += 2; minimum.y = Mathf.Max(160, minimum.y + 2);
                    if (panel == null)
                    {
                        panel = new ShaderGraphFloatingPanel(host.placement, minimum);
                        panel.AddToClassList("whimtex-shader-graph-tool-panel");
                        panel.PlacementChanged += RequestSave;
                        panel.GestureStarted += BeginGesture; panel.GestureEnded += EndGesture;
                        panel.Dragged += OnDrag; panel.DragEnded += OnDrop;
                        panels.Add(panel, host); viewport.Add(panel); panel.AddResizeHandles();
                    }
                    else panel.SetMinimum(minimum);
                    var members = layout.Members(host.root);
                    panel.name = members.Count == 1 ? "graph-" + members[0].ToString().ToLowerInvariant() + "-panel" : "graph-tools-group";
                    var content = Build(host.root, host, panel, live);
                    if (content.parent != panel) { ResetSizing(content); panel.Insert(0, content); }
                    panel.Place();
                }
                var stale = new List<string>();
                foreach (var pair in views) if (!live.Contains(pair.Key)) stale.Add(pair.Key);
                foreach (string id in stale)
                { views[id].ReleaseSplits(); views[id].root.RemoveFromHierarchy(); views.Remove(id); }
                var removed = new List<ShaderGraphFloatingPanel>();
                foreach (var pair in panels) if (!layout.hosts.Contains(pair.Value)) removed.Add(pair.Key);
                foreach (var panel in removed) { panel.RemoveFromHierarchy(); panels.Remove(panel); }
                foreach (var pair in panels)
                {
                    var root = regions[pair.Value.root];
                    for (int i = pair.Key.childCount - 1; i >= 0; i--)
                    {
                        var child = pair.Key[i];
                        if (child != root && !child.ClassListContains("whimtex-shader-graph-panel-resize")) child.RemoveFromHierarchy();
                    }
                }
                geometryDirty = true;
                if (focus?.panel == viewport.panel && viewport.Contains(focus) && focus.enabledInHierarchy &&
                    viewport.focusController.focusedElement != focus) focus.Focus();
            }
            finally { rebuilding = false; }
        }
        private static void ResetSizing(VisualElement element)
        {
            // Native splitters assign inline dimensions to their children. Those do not belong to a new parent.
            element.style.width = StyleKeyword.Null; element.style.height = StyleKeyword.Null;
            element.style.minWidth = StyleKeyword.Null; element.style.minHeight = StyleKeyword.Null;
            element.style.flexGrow = StyleKeyword.Null; element.style.flexShrink = StyleKeyword.Null;
            element.style.flexBasis = StyleKeyword.Null; element.style.left = StyleKeyword.Null; element.style.top = StyleKeyword.Null;
        }
        private VisualElement Build(string id, ShaderGraphPanelHostLayout host, ShaderGraphFloatingPanel panel, HashSet<string> live)
        {
            var node = layout.Find(id); live.Add(id);
            if (!views.TryGetValue(id, out var pane)) views.Add(id, pane = new Pane());
            pane.node = node; pane.host = host; pane.panel = panel;
            if (node.IsSplit)
            {
                var children = new List<VisualElement>(node.children.Count);
                foreach (var child in node.children) children.Add(Build(child, host, panel, live));
                bool changed = pane.root == null || pane.grouping != node.grouping || children.Count != pane.children.Count;
                if (!changed) for (int i = 0; i < children.Count; i++) if (children[i] != pane.children[i]) { changed = true; break; }
                if (changed)
                {
                    pane.ReleaseSplits();
                    var previous = pane.root;
                    pane.children.Clear(); pane.children.AddRange(children);
                    pane.root = BuildSplit(pane, 0);
                    previous?.RemoveFromHierarchy();
                }
                foreach (var split in pane.splits) split.Refresh();
            }
            else
            {
                if (pane.root is not TabView) { pane.ReleaseSplits(); pane.root?.RemoveFromHierarchy(); BuildTabs(pane); }
                var tabs = (TabView)pane.root;
                for (int i = tabs.childCount - 1; i >= 0; i--)
                    if (tabs[i] is Tab tab && !node.tabs.Contains((ShaderGraphPanelKind)tab.userData)) tab.RemoveFromHierarchy();
                for (int i = 0; i < node.tabs.Count; i++)
                {
                    var kind = node.tabs[i];
                    if (!tabsByKind.TryGetValue(kind, out var tab))
                    {
                        tab = new Tab(kind.ToString()) { userData = kind }; tabsByKind.Add(kind, tab);
                        tab.AddToClassList("whimtex-shader-graph-tab"); tab.contentContainer.AddToClassList("whimtex-shader-graph-panel-content");
                        tab.tabHeader.AddToClassList("whimtex-shader-graph-panel-title"); tab.Add(contents[kind]);
                        var retained = tab;
                        panel.AddDragHandle(tab.tabHeader, detach: _ => DetachTab(kind),
                            select: () => { var owner = retained.GetFirstAncestorOfType<TabView>(); if (owner != null) owner.activeTab = retained; },
                            tabDrag: (position, start) => DragTab(kind, position, start), tabDrop: (position, cancelled) => DropTab(kind, position, cancelled));
                    }
                    int current = tabs.IndexOf(tab);
                    if (current >= 0) MoveTab(tabs, current, i);
                    else tabs.Insert(i, tab);
                }
                tabs.activeTab = tabsByKind[node.selected]; panes.Add(pane);
            }
            pane.grouping = node.grouping;
            pane.root.userData = node; regions.Add(id, pane.root); return pane.root;
        }
        private static void MoveTab(TabView tabs, int from, int to)
        {
            if (from == to) return;
            // Use the public operation so content, headers, selection and native tab indexes stay in sync.
            // Our shared gesture owns dragging; native drag handles are enabled only for this operation.
            bool reorderable = tabs.reorderable;
            try { tabs.reorderable = true; tabs.ReorderTab(from, to); }
            finally { tabs.reorderable = reorderable; }
        }
        private void BuildTabs(Pane pane)
        {
            var tabs = new TabView { name = "graph-sidebar-tabs" }; pane.root = tabs;
            tabs.AddToClassList("whimtex-shader-graph-tabs"); tabs.contentContainer.AddToClassList("whimtex-shader-graph-panel-content");
            tabs.activeTabChanged += (_, selected) =>
            {
                if (rebuilding || selected == null) return;
                pane.node.selected = (ShaderGraphPanelKind)selected.userData; RequestSave();
            };
            var bar = new VisualElement(); pane.header = bar; bar.AddToClassList("whimtex-shader-graph-panel-header"); bar.Add(tabs.contentViewport);
            var grip = new VisualElement { name = "panel-header-grip" }; grip.AddToClassList("whimtex-shader-graph-panel-header-grip");
            pane.panel.AddDragHandle(grip); bar.Add(grip);
            var menu = new ToolbarMenu { name = "panel-menu" }; menu.AddToClassList("whimtex-layer-menu-button");
            menu.EnableInClassList("whimtex-layer-menu-button--light", !UnityEditor.EditorGUIUtility.isProSkin);
            menu.AddToClassList("whimtex-shader-graph-panel-menu"); WhimTexUI.AddMenuDots(menu); AddMenu(menu.menu, pane.node.id); bar.Add(menu);
            bar.RegisterCallback<GeometryChangedEvent>(e => { if (e.newRect.height != e.oldRect.height) menu.style.width = e.newRect.height; });
            bar.AddManipulator(new ContextualMenuManipulator(e => { AddMenu(e.menu, pane.node.id); e.StopPropagation(); }));
            tabs.hierarchy.Insert(0, bar);
            tabs.RegisterCallback<GeometryChangedEvent>(_ => geometryDirty = true);
        }
        private VisualElement BuildSplit(Pane pane, int index)
        {
            if (index == pane.children.Count - 1) return pane.children[index];
            var first = pane.children[index]; var second = BuildSplit(pane, index + 1);
            ResetSizing(first); ResetSizing(second);
            var binding = new SplitBinding(this, pane, index, first, second); pane.splits.Add(binding);
            return binding.view;
        }

        private sealed class SplitBinding : IDisposable
        {
            internal readonly TwoPaneSplitView view;
            readonly ShaderGraphPanelWorkspace workspace;
            readonly Pane pane;
            readonly int index;
            readonly bool horizontal;
            readonly VisualElement first, second;
            readonly IVisualElementScheduledItem apply, commit;
            float minA, minB;
            bool resizing, commitPending, disposed;
            internal SplitBinding(ShaderGraphPanelWorkspace workspace, Pane pane, int index, VisualElement first, VisualElement second)
            {
                this.workspace = workspace; this.pane = pane; this.index = index; this.first = first; this.second = second;
                horizontal = pane.node.grouping == ShaderGraphPanelGrouping.SideBySide;
                view = new TwoPaneSplitView(0, 160, horizontal ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical)
                { name = pane.host.root == pane.node.id && index == 0 ? "graph-panel-sections" : "graph-panel-sections-tail" };
                view.AddToClassList("whimtex-shader-graph-panel-content"); view.Add(first); view.Add(second);
                apply = view.schedule.Execute(Apply); apply.Pause(); commit = view.schedule.Execute(Commit); commit.Pause();
                view.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    workspace.geometryDirty = true;
                    if ((horizontal ? e.newRect.width != e.oldRect.width : e.newRect.height != e.oldRect.height) && !resizing)
                        apply.ExecuteLater(0);
                });
                view.RegisterCallback<PointerDownEvent>(e =>
                {
                    var hit = e.target as VisualElement;
                    if (e.button != 0 || hit == null || hit == first || first.Contains(hit) || hit == second || second.Contains(hit)) return;
                    resizing = true; apply.Pause(); workspace.BeginGesture();
                }, TrickleDown.TrickleDown);
                view.RegisterCallback<PointerUpEvent>(_ => FinishResize(), TrickleDown.TrickleDown);
                view.RegisterCallback<PointerCaptureOutEvent>(_ => FinishResize());
                view.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    var hit = e.target as VisualElement;
                    if (hit == null || hit == first || first.Contains(hit) || hit == second || second.Contains(hit)) return;
                    workspace.AddMenu(e.menu, pane.node.id); e.StopPropagation();
                }));
            }
            float Size => horizontal ? view.resolvedStyle.width : view.resolvedStyle.height;
            float TailWeight() { float sum = 0; for (int i = index; i < pane.node.weights.Count; i++) sum += pane.node.weights[i]; return sum; }
            internal void Refresh()
            {
                view.name = pane.host.root == pane.node.id && index == 0 ? "graph-panel-sections" : "graph-panel-sections-tail";
                var a = workspace.Minimum(pane.node.children[index]); var b = workspace.MinimumChildren(pane.node, index + 1);
                minA = horizontal ? a.x : a.y; minB = horizontal ? b.x : b.y;
                if (horizontal) { first.style.minWidth = minA; second.style.minWidth = minB; }
                else { first.style.minHeight = minA; second.style.minHeight = minB; }
                apply.ExecuteLater(0);
            }
            void Apply()
            {
                float size = Size;
                if (disposed || view.panel == null || resizing || !ProjectiveMatrix.Finite(size) || size <= 0) return;
                float factor = Mathf.Min(1, size / (minA + minB));
                float dimension = Mathf.Clamp(size * pane.node.weights[index] / TailWeight(), minA * factor, size - minB * factor);
                if (Mathf.Abs(view.fixedPaneInitialDimension - dimension) > .01f) view.fixedPaneInitialDimension = dimension;
            }
            void FinishResize()
            { if (!resizing || commitPending || disposed) return; commitPending = true; commit.ExecuteLater(0); }
            void Commit()
            {
                if (disposed) return;
                try
                {
                    float size = Size; if (view.panel == null || size <= 0) return;
                    float ratio = Mathf.Clamp((horizontal ? first.resolvedStyle.width : first.resolvedStyle.height) / size, .01f, .99f);
                    float tail = TailWeight(), remaining = tail - pane.node.weights[index];
                    pane.node.weights[index] = ratio * tail;
                    for (int i = index + 1; i < pane.node.weights.Count; i++) pane.node.weights[i] *= (tail - pane.node.weights[index]) / remaining;
                    workspace.RequestSave();
                }
                finally { resizing = commitPending = false; workspace.EndGesture(); }
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true; apply.Pause(); commit.Pause();
                if (resizing) { resizing = commitPending = false; workspace.EndGesture(); }
            }
        }
    }
}
