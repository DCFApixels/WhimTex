using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal enum ShaderGraphPanelGrouping { Tabs, Sections, Separate, SideBySide }
    internal enum ShaderGraphPanelKind { Parameters, Nodes, Inspector }

    [Serializable]
    internal sealed class ShaderGraphPanelLayoutNode
    {
        public string id = Guid.NewGuid().ToString("N");
        public ShaderGraphPanelGrouping grouping;
        public List<ShaderGraphPanelKind> tabs = new();
        public ShaderGraphPanelKind selected;
        public List<string> children = new();
        public List<float> weights = new();
        internal bool IsSplit => grouping == ShaderGraphPanelGrouping.Sections || grouping == ShaderGraphPanelGrouping.SideBySide;
    }

    [Serializable]
    internal sealed class ShaderGraphPanelHostLayout
    {
        public string root;
        public ShaderGraphPanelPlacement placement;
    }

    [Serializable]
    internal sealed class ShaderGraphWorkspaceLayout
    {
        // Child IDs keep the serialized layout flat, avoiding recursive inline serialization.
        public List<ShaderGraphPanelLayoutNode> treeNodes = new();
        public List<ShaderGraphPanelHostLayout> hosts = new();
        public ShaderGraphPanelPlacement preview = new(new Rect(0, 0, 256, 276), ShaderGraphPanelAnchor.Right | ShaderGraphPanelAnchor.Bottom);

        internal ShaderGraphPanelLayoutNode Find(string id) => treeNodes.Find(n => n != null && n.id == id);
        internal ShaderGraphPanelLayoutNode Tabs(IEnumerable<ShaderGraphPanelKind> kinds)
        {
            var node = new ShaderGraphPanelLayoutNode { tabs = new(kinds) };
            if (node.tabs.Count > 0) node.selected = node.tabs[0];
            treeNodes.Add(node); return node;
        }
        internal ShaderGraphPanelLayoutNode Split(ShaderGraphPanelGrouping grouping, params string[] children)
        {
            var node = new ShaderGraphPanelLayoutNode { grouping = grouping, children = new(children) };
            foreach (var child in children) node.weights.Add(1f / children.Length);
            treeNodes.Add(node); return node;
        }
        internal List<ShaderGraphPanelKind> Members(string id)
        {
            var result = new List<ShaderGraphPanelKind>();
            void Visit(string key)
            {
                var node = Find(key); if (node == null) return;
                if (node.IsSplit) { foreach (var child in node.children) Visit(child); }
                else result.AddRange(node.tabs);
            }
            Visit(id); return result;
        }
        internal void Replace(string oldId, string newId)
        {
            foreach (var host in hosts) if (host.root == oldId) host.root = newId;
            foreach (var node in treeNodes)
            {
                // A newly created split may already contain the replaced subtree.
                if (node.id == newId) continue;
                for (int i = 0; i < node.children.Count; i++) if (node.children[i] == oldId) node.children[i] = newId;
            }
        }
        internal void Reset(IEnumerable<ShaderGraphPanelKind> available = null)
        {
            treeNodes.Clear(); hosts.Clear();
            var kinds = new List<ShaderGraphPanelKind>(available ?? (ShaderGraphPanelKind[])Enum.GetValues(typeof(ShaderGraphPanelKind)));
            ShaderGraphPanelLayoutNode root;
            if (kinds.Count > 1 && kinds.Remove(ShaderGraphPanelKind.Nodes))
            {
                var upper = Tabs(kinds);
                if (kinds.Contains(ShaderGraphPanelKind.Parameters)) upper.selected = ShaderGraphPanelKind.Parameters;
                var lower = Tabs(new[] { ShaderGraphPanelKind.Nodes });
                root = Split(ShaderGraphPanelGrouping.Sections, upper.id, lower.id);
                root.weights[0] = .38f; root.weights[1] = .62f;
            }
            else root = Tabs(kinds);
            hosts.Add(new ShaderGraphPanelHostLayout { root = root.id,
                placement = new(new Rect(0, 0, 282, 560), ShaderGraphPanelAnchor.Left | ShaderGraphPanelAnchor.Top | ShaderGraphPanelAnchor.Bottom) });
        }
        internal void Validate(IEnumerable<ShaderGraphPanelKind> available = null)
        {
            var registered = new HashSet<ShaderGraphPanelKind>(available ?? (ShaderGraphPanelKind[])Enum.GetValues(typeof(ShaderGraphPanelKind)));
            treeNodes ??= new(); hosts ??= new();
            preview ??= new(new Rect(0, 0, 256, 276), ShaderGraphPanelAnchor.Right | ShaderGraphPanelAnchor.Bottom);
            if (hosts.Count == 0) { Reset(registered); return; }
            var nodeIds = new HashSet<string>();
            treeNodes.RemoveAll(n => n == null || string.IsNullOrEmpty(n.id) || !nodeIds.Add(n.id));
            var visited = new HashSet<string>(); var panels = new HashSet<ShaderGraphPanelKind>();
            string Visit(string id)
            {
                var node = Find(id);
                if (node == null || !visited.Add(node.id)) return null;
                node.tabs ??= new();
                node.children ??= new(); node.weights ??= new();
                if (node.IsSplit)
                {
                    var children = new List<string>(); var weights = new List<float>();
                    for (int i = 0; i < node.children.Count; i++)
                    {
                        string child = Visit(node.children[i]); if (child == null) continue;
                        children.Add(child);
                        float weight = i < node.weights.Count ? node.weights[i] : 1;
                        weights.Add(ProjectiveMatrix.Finite(weight) && weight > 0 ? weight : 1);
                    }
                    node.children = children; node.weights = weights; node.tabs.Clear();
                    if (children.Count < 2) return children.Count == 1 ? children[0] : null;
                    NormalizeWeights(node);
                }
                else
                {
                    node.grouping = ShaderGraphPanelGrouping.Tabs; node.children.Clear(); node.weights.Clear();
                    node.tabs.RemoveAll(p => !registered.Contains(p) || !panels.Add(p));
                    if (node.tabs.Count == 0) return null;
                    if (!node.tabs.Contains(node.selected)) node.selected = node.tabs[0];
                }
                return node.id;
            }
            hosts.RemoveAll(h => h == null);
            foreach (var host in hosts)
            {
                host.root = Visit(host.root);
                host.placement ??= new(new Rect(0, 0, 320, 400), ShaderGraphPanelAnchor.Top);
            }
            hosts.RemoveAll(h => h.root == null);
            if (hosts.Count == 0) { Reset(registered); return; }
            foreach (var kind in registered)
                if (!panels.Contains(kind)) hosts.Add(new ShaderGraphPanelHostLayout { root = Tabs(new[] { kind }).id,
                    placement = new(new Rect(0, 0, 320, 400), ShaderGraphPanelAnchor.Right | ShaderGraphPanelAnchor.Top) });
            var reachable = new HashSet<string>();
            void Keep(string id)
            { if (!reachable.Add(id)) return; var n = Find(id); if (n.IsSplit) foreach (var child in n.children) Keep(child); }
            foreach (var host in hosts) Keep(host.root);
            treeNodes.RemoveAll(n => !reachable.Contains(n.id));
        }
        internal static void NormalizeWeights(ShaderGraphPanelLayoutNode node)
        {
            double total = 0; foreach (float weight in node.weights) total += weight;
            for (int i = 0; i < node.weights.Count; i++) node.weights[i] = (float)(node.weights[i] / total);
        }
    }

    internal sealed class ShaderGraphPanelWorkspace
    {
        private sealed class Pane
        {
            internal ShaderGraphPanelLayoutNode node;
            internal ShaderGraphPanelHostLayout host;
            internal ShaderGraphFloatingPanel panel;
            internal VisualElement root, header;
        }
        private readonly VisualElement viewport;
        private readonly IReadOnlyDictionary<ShaderGraphPanelKind, VisualElement> contents;
        private readonly ShaderGraphWorkspaceLayout layout;
        private readonly Action save;
        private readonly Dictionary<ShaderGraphFloatingPanel, ShaderGraphPanelHostLayout> panels = new();
        private readonly List<Pane> panes = new();
        private readonly Dictionary<string, VisualElement> regions = new();
        private readonly List<VisualElement> dropHits = new();
        private readonly Label dropHint;
        private bool rebuilding;
        private string beforeDragLayout;
        internal bool NodesVisible
        {
            get
            {
                foreach (var node in layout.treeNodes)
                    if (!node.IsSplit && node.tabs.Contains(ShaderGraphPanelKind.Nodes)) return node.selected == ShaderGraphPanelKind.Nodes;
                return false;
            }
        }

        internal ShaderGraphPanelWorkspace(VisualElement viewport, IReadOnlyDictionary<ShaderGraphPanelKind, VisualElement> contents,
            ShaderGraphWorkspaceLayout layout, Action save)
        {
            this.viewport = viewport; this.contents = contents;
            this.layout = layout; this.save = save;
            foreach (var content in contents.Values) content.AddToClassList("whimtex-shader-graph-panel-content");
            dropHint = new Label { name = "graph-panel-drop-hint", pickingMode = PickingMode.Ignore };
            dropHint.AddToClassList("whimtex-shader-graph-panel-drop-hint");
            HideDropHint(); Rebuild(); viewport.Add(dropHint);
        }
        private Vector2 Minimum(string id)
        {
            var node = layout.Find(id);
            if (!node.IsSplit) return new Vector2(230, 80);
            return MinimumChildren(node, 0);
        }
        private Vector2 MinimumChildren(ShaderGraphPanelLayoutNode node, int start)
        {
            var result = Vector2.zero;
            for (int i = start; i < node.children.Count; i++)
            {
                var child = Minimum(node.children[i]);
                result = node.grouping == ShaderGraphPanelGrouping.SideBySide
                    ? new Vector2(result.x + child.x, Mathf.Max(result.y, child.y)) : new Vector2(Mathf.Max(result.x, child.x), result.y + child.y);
            }
            return result;
        }
        private void Rebuild()
        {
            rebuilding = true; layout.Validate(contents.Keys);
            foreach (var content in contents.Values) content.RemoveFromHierarchy();
            foreach (var panel in panels.Keys) panel.RemoveFromHierarchy();
            panels.Clear(); panes.Clear(); regions.Clear();
            foreach (var host in layout.hosts)
            {
                var members = layout.Members(host.root);
                var minimum = Minimum(host.root); minimum.x += 2; minimum.y = Mathf.Max(160, minimum.y + 2);
                var panel = new ShaderGraphFloatingPanel(host.placement, minimum)
                { name = members.Count == 1 ? "graph-" + members[0].ToString().ToLowerInvariant() + "-panel" : "graph-tools-group" };
                panel.AddToClassList("whimtex-shader-graph-tool-panel");
                panel.PlacementChanged += save; panel.Dragged += OnDrag; panel.DragEnded += OnDrop;
                panels.Add(panel, host); viewport.Add(panel);
                panel.Add(Build(host.root, host, panel)); panel.AddResizeHandles();
            }
            rebuilding = false;
        }
        private VisualElement Build(string id, ShaderGraphPanelHostLayout host, ShaderGraphFloatingPanel panel)
        {
            var node = layout.Find(id);
            var root = node.IsSplit ? BuildSplit(node, host, panel, 0) : BuildTabs(node, host, panel);
            regions.Add(id, root); root.userData = node; return root;
        }
        private VisualElement BuildTabs(ShaderGraphPanelLayoutNode node, ShaderGraphPanelHostLayout host, ShaderGraphFloatingPanel panel)
        {
            var tabs = new TabView { name = "graph-sidebar-tabs" };
            tabs.AddToClassList("whimtex-shader-graph-tabs");
            tabs.contentContainer.AddToClassList("whimtex-shader-graph-panel-content");
            foreach (var kind in node.tabs)
            {
                var tab = new Tab(kind.ToString()) { userData = kind };
                tab.AddToClassList("whimtex-shader-graph-tab");
                tab.contentContainer.AddToClassList("whimtex-shader-graph-panel-content");
                tab.tabHeader.AddToClassList("whimtex-shader-graph-panel-title");
                tab.Add(contents[kind]); tabs.Add(tab);
                if (kind == node.selected) tabs.activeTab = tab;
                panel.AddDragHandle(tab.tabHeader, detach: _ => DetachTab(kind), select: () => tabs.activeTab = tab);
            }
            tabs.activeTabChanged += (_, selected) =>
            { node.selected = (ShaderGraphPanelKind)selected.userData; if (!rebuilding) save(); };
            var bar = new VisualElement(); bar.AddToClassList("whimtex-shader-graph-panel-header");
            bar.Add(tabs.contentViewport);
            var grip = new VisualElement { name = "panel-header-grip" };
            grip.AddToClassList("whimtex-shader-graph-panel-header-grip");
            panel.AddDragHandle(grip); bar.Add(grip);
            var menu = new ToolbarMenu { name = "panel-menu" };
            menu.AddToClassList("whimtex-layer-menu-button");
            menu.EnableInClassList("whimtex-layer-menu-button--light", !UnityEditor.EditorGUIUtility.isProSkin);
            menu.AddToClassList("whimtex-shader-graph-panel-menu"); WhimTexUI.AddMenuDots(menu);
            AddMenu(menu.menu, node.id); bar.Add(menu);
            bar.RegisterCallback<GeometryChangedEvent>(e => menu.style.width = e.newRect.height);
            bar.AddManipulator(new ContextualMenuManipulator(e => { AddMenu(e.menu, node.id); e.StopPropagation(); }));
            tabs.hierarchy.Insert(0, bar);
            panes.Add(new Pane { node = node, host = host, panel = panel, root = tabs, header = bar });
            return tabs;
        }
        private VisualElement BuildSplit(ShaderGraphPanelLayoutNode node, ShaderGraphPanelHostLayout host, ShaderGraphFloatingPanel panel, int index)
        {
            if (index == node.children.Count - 1) return Build(node.children[index], host, panel);
            bool horizontal = node.grouping == ShaderGraphPanelGrouping.SideBySide;
            var split = new TwoPaneSplitView(0, 160,
                horizontal ? TwoPaneSplitViewOrientation.Horizontal : TwoPaneSplitViewOrientation.Vertical)
                { name = host.root == node.id && index == 0 ? "graph-panel-sections" : "graph-panel-sections-tail" };
            split.AddToClassList("whimtex-shader-graph-panel-content");
            var first = Build(node.children[index], host, panel); var second = BuildSplit(node, host, panel, index + 1);
            var minA = Minimum(node.children[index]); var minB = MinimumChildren(node, index + 1);
            float TailWeight() { float sum = 0; for (int i = index; i < node.weights.Count; i++) sum += node.weights[i]; return sum; }
            if (horizontal) { first.style.minWidth = minA.x; second.style.minWidth = minB.x; }
            else { first.style.minHeight = minA.y; second.style.minHeight = minB.y; }
            split.Add(first); split.Add(second);
            bool resizing = false;
            var applyRatio = split.schedule.Execute(() =>
            {
                float size = horizontal ? split.resolvedStyle.width : split.resolvedStyle.height;
                if (split.panel == null || resizing || size <= 0) return;
                float a = horizontal ? minA.x : minA.y, b = horizontal ? minB.x : minB.y;
                float factor = Mathf.Min(1, size / (a + b));
                split.fixedPaneInitialDimension = Mathf.Clamp(size * node.weights[index] / TailWeight(), a * factor, size - b * factor);
            });
            applyRatio.Pause(); split.RegisterCallback<GeometryChangedEvent>(_ => applyRatio.ExecuteLater(0));
            split.RegisterCallback<PointerDownEvent>(e =>
            {
                var hit = e.target as VisualElement;
                resizing = e.button == 0 && hit != null && hit != first && !first.Contains(hit) && hit != second && !second.Contains(hit);
            }, TrickleDown.TrickleDown);
            void FinishResize()
            {
                if (!resizing) return; resizing = false;
                split.schedule.Execute(() =>
                {
                    float size = horizontal ? split.resolvedStyle.width : split.resolvedStyle.height;
                    if (split.panel == null || size <= 0) return;
                    float ratio = Mathf.Clamp((horizontal ? first.resolvedStyle.width : first.resolvedStyle.height) / size, .01f, .99f);
                    float tail = TailWeight(), remaining = tail - node.weights[index];
                    node.weights[index] = ratio * tail;
                    for (int i = index + 1; i < node.weights.Count; i++) node.weights[i] *= (tail - node.weights[index]) / remaining;
                    save();
                });
            }
            split.RegisterCallback<PointerUpEvent>(_ => FinishResize(), TrickleDown.TrickleDown);
            split.RegisterCallback<PointerCaptureOutEvent>(_ => FinishResize());
            split.AddManipulator(new ContextualMenuManipulator(e =>
            {
                var hit = e.target as VisualElement;
                if (hit == null || hit == first || first.Contains(hit) || hit == second || second.Contains(hit)) return;
                AddMenu(e.menu, node.id); e.StopPropagation();
            }));
            return split;
        }
        private void AddMenu(DropdownMenu menu, string id)
        {
            var node = layout.Find(id);
            void Mode(string label, ShaderGraphPanelGrouping grouping) => menu.AppendAction(label, _ => Arrange(id, grouping),
                _ => node.grouping == grouping ? DropdownMenuAction.Status.Checked :
                    layout.Members(id).Count < 2 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            Mode("Combine as Tabs", ShaderGraphPanelGrouping.Tabs);
            Mode("Combine as Sections", ShaderGraphPanelGrouping.Sections);
            Mode("Combine Side by Side", ShaderGraphPanelGrouping.SideBySide);
            menu.AppendAction("Detach Group", _ => { DetachGroup(id); save(); },
                _ => layout.hosts.Exists(h => h.root == id) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.AppendAction("Separate Tabs", _ => Separate(id),
                _ => layout.Members(id).Count < 2 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.AppendSeparator(); menu.AppendAction("Reset Panel Layout", _ => Reset());
        }
        private string ArrangeMembers(List<ShaderGraphPanelKind> members, ShaderGraphPanelGrouping grouping)
        {
            if (grouping == ShaderGraphPanelGrouping.Tabs || members.Count == 1) return layout.Tabs(members).id;
            var children = new List<string>(); foreach (var member in members) children.Add(layout.Tabs(new[] { member }).id);
            return layout.Split(grouping, children.ToArray()).id;
        }
        private void Arrange(string id, ShaderGraphPanelGrouping grouping)
        {
            var members = layout.Members(id); if (members.Count < 2) return;
            layout.Replace(id, ArrangeMembers(members, grouping)); Rebuild(); save();
        }
        private ShaderGraphFloatingPanel PanelFor(ShaderGraphPanelKind kind)
        {
            foreach (var pair in panels) if (layout.Members(pair.Value.root).Contains(kind)) return pair.Key;
            throw new InvalidOperationException("Missing graph panel.");
        }
        private ShaderGraphPanelHostLayout HostFor(string id)
        {
            foreach (var host in layout.hosts)
            {
                bool Contains(string key)
                { if (key == id) return true; var node = layout.Find(key); return node.IsSplit && node.children.Exists(Contains); }
                if (Contains(host.root)) return host;
            }
            throw new InvalidOperationException("Missing graph panel group.");
        }
        private Rect RegionBounds(string id)
        {
            var rect = regions[id].worldBound; rect.position = viewport.WorldToLocal(rect.position); return rect;
        }
        private ShaderGraphFloatingPanel DetachTab(ShaderGraphPanelKind kind)
        {
            beforeDragLayout = JsonUtility.ToJson(layout);
            var pane = panes.Find(p => p.node.tabs.Contains(kind));
            if (pane.node.tabs.Count == 1) return DetachGroup(pane.node.id);
            Rect bounds = RegionBounds(pane.node.id); pane.node.tabs.Remove(kind);
            var node = layout.Tabs(new[] { kind });
            layout.hosts.Add(new ShaderGraphPanelHostLayout { root = node.id, placement = new(bounds, ShaderGraphPanelAnchor.None) });
            Rebuild(); return PanelFor(kind);
        }
        private ShaderGraphFloatingPanel DetachGroup(string id)
        {
            var host = HostFor(id); var members = layout.Members(id);
            if (host.root == id) return PanelFor(members[0]);
            Rect bounds = RegionBounds(id); layout.Replace(id, null);
            layout.hosts.Add(new ShaderGraphPanelHostLayout { root = id, placement = new(bounds, ShaderGraphPanelAnchor.None) });
            Rebuild(); return PanelFor(members[0]);
        }
        private void Separate(string id)
        {
            var members = layout.Members(id); Rect bounds = RegionBounds(id); layout.Replace(id, null);
            foreach (var member in members)
            {
                layout.hosts.Add(new ShaderGraphPanelHostLayout { root = layout.Tabs(new[] { member }).id,
                    placement = new(bounds, ShaderGraphPanelAnchor.None) });
                bounds.position += new Vector2(32, 32);
            }
            Rebuild(); save();
        }
        private Pane DropTarget(ShaderGraphFloatingPanel moving, Vector2 position,
            out ShaderGraphPanelGrouping grouping, out bool before, out Rect highlight)
        {
            grouping = ShaderGraphPanelGrouping.Tabs; before = false; highlight = default;
            dropHits.Clear(); viewport.panel.PickAll(position, dropHits);
            Pane other = null;
            foreach (var hit in dropHits)
            {
                var panel = hit as ShaderGraphFloatingPanel ?? hit.GetFirstAncestorOfType<ShaderGraphFloatingPanel>();
                if (panel == null || panel == moving) continue;
                if (!panels.ContainsKey(panel)) return null;
                other = panes.Find(p => p.panel == panel && p.root.worldBound.Contains(position)) ?? panes.Find(p => p.panel == panel);
                break;
            }
            if (other == null) return null;
            var boundary = BoundaryTarget(other.host.root, other, position, out var edgeGrouping, out bool edgeBefore, out var edgeHighlight);
            if (boundary != null)
            { grouping = edgeGrouping; before = edgeBefore; highlight = edgeHighlight; return boundary; }
            if (!other.root.worldBound.Contains(position)) return null;
            Rect bounds = other.root.worldBound;
            if (other.header.worldBound.Contains(position)) highlight = other.header.worldBound;
            else
            {
                float top = other.header.worldBound.yMax;
                float x = (position.x - bounds.x) / Mathf.Max(1, bounds.width);
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
        private Pane BoundaryTarget(string id, Pane pane, Vector2 position,
            out ShaderGraphPanelGrouping grouping, out bool before, out Rect highlight)
        {
            grouping = ShaderGraphPanelGrouping.Tabs; before = false; highlight = default;
            var node = layout.Find(id);
            if (!node.IsSplit) return null;
            var region = regions[id]; Rect bounds = region.worldBound;
            if (!bounds.Contains(position)) return null;
            float left = position.x - bounds.xMin, right = bounds.xMax - position.x;
            float top = position.y - bounds.yMin, bottom = bounds.yMax - position.y;
            // The outer edge addresses the whole subtree; farther inside, a drop addresses its leaf pane.
            float edge = ShaderGraphFloatingPanel.SnapDistance * .5f;
            if (Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom)) <= edge)
            {
                bool horizontal = Mathf.Min(left, right) < Mathf.Min(top, bottom);
                grouping = horizontal ? ShaderGraphPanelGrouping.SideBySide : ShaderGraphPanelGrouping.Sections;
                before = horizontal ? left < right : top < bottom;
                highlight = horizontal
                    ? new Rect(before ? bounds.x : bounds.center.x, bounds.y, bounds.width * .5f, bounds.height)
                    : new Rect(bounds.x, before ? bounds.y : bounds.center.y, bounds.width, bounds.height * .5f);
                return new Pane { node = node, host = pane.host, panel = pane.panel, root = region };
            }
            foreach (var child in node.children)
            {
                var target = BoundaryTarget(child, pane, position, out grouping, out before, out highlight);
                if (target != null) return target;
            }
            return null;
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
            string restore = beforeDragLayout; beforeDragLayout = null;
            if (cancelled)
            {
                if (restore != null) { JsonUtility.FromJsonOverwrite(restore, layout); Rebuild(); }
                return;
            }
            var other = DropTarget(moving, position, out var grouping, out bool before, out _);
            if (other == null || !panels.TryGetValue(moving, out var source)) return;
            if (grouping == ShaderGraphPanelGrouping.Tabs)
            {
                var members = layout.Members(source.root);
                other.node.tabs.AddRange(members); other.node.selected = members[0];
            }
            else
            {
                var parent = layout.treeNodes.Find(n => n.grouping == grouping && n.children.Contains(other.node.id));
                if (other.node.grouping == grouping)
                {
                    float newWeight = 1f / other.node.children.Count;
                    other.node.children.Insert(before ? 0 : other.node.children.Count, source.root);
                    other.node.weights.Insert(before ? 0 : other.node.weights.Count, newWeight);
                    ShaderGraphWorkspaceLayout.NormalizeWeights(other.node);
                }
                else if (parent != null)
                {
                    int index = parent.children.IndexOf(other.node.id);
                    parent.weights[index] *= .5f;
                    parent.children.Insert(before ? index : index + 1, source.root);
                    parent.weights.Insert(before ? index : index + 1, parent.weights[index]);
                }
                else
                {
                    var split = layout.Split(grouping, before ? source.root : other.node.id, before ? other.node.id : source.root);
                    layout.Replace(other.node.id, split.id);
                }
            }
            layout.hosts.Remove(source); Rebuild(); save();
        }
        private void HideDropHint() => dropHint.style.display = DisplayStyle.None;
        internal void Reset() { layout.Reset(contents.Keys); Rebuild(); save(); }
    }
}
