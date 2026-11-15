using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPanelWorkspace : IDisposable
    {
        private readonly VisualElement viewport;
        private readonly IReadOnlyDictionary<ShaderGraphPanelKind, VisualElement> contents;
        private readonly ShaderGraphWorkspaceLayout layout;
        private readonly Action save;
        private readonly Dictionary<ShaderGraphFloatingPanel, ShaderGraphPanelHostLayout> panels = new();
        private readonly List<Pane> panes = new();
        private readonly Dictionary<string, VisualElement> regions = new();
        private readonly Dictionary<string, Pane> views = new();
        private readonly Dictionary<ShaderGraphPanelKind, Tab> tabsByKind = new();
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
            InitializePersistence();
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
        private void AddMenu(DropdownMenu menu, string id)
        {
            void Mode(string label, ShaderGraphPanelGrouping grouping) => menu.AppendAction(label, _ => Arrange(id, grouping),
                _ => layout.Find(id)?.grouping == grouping ? DropdownMenuAction.Status.Checked :
                    layout.Members(id).Count < 2 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            Mode("Combine as Tabs", ShaderGraphPanelGrouping.Tabs);
            Mode("Combine as Sections", ShaderGraphPanelGrouping.Sections);
            Mode("Combine Side by Side", ShaderGraphPanelGrouping.SideBySide);
            menu.AppendAction("Detach Group", _ => { DetachGroup(id); RequestSave(); },
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
            layout.Replace(id, ArrangeMembers(members, grouping)); Rebuild(); RequestSave();
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
            Rebuild(); RequestSave();
        }
        private void OnDrop(ShaderGraphFloatingPanel moving, Vector2 position, bool cancelled)
        {
            if (disposed) return;
            var other = DropTarget(moving, position, out var grouping, out bool before, out _);
            int tabIndex = other == null ? 0 : TabDropIndex(other, position);
            HideDropHint();
            string restore = beforeDragLayout; beforeDragLayout = null;
            if (cancelled)
            {
                if (restore != null) { JsonUtility.FromJsonOverwrite(restore, layout); Rebuild(); }
                return;
            }
            if (other == null || !panels.TryGetValue(moving, out var source)) return;
            if (grouping == ShaderGraphPanelGrouping.Tabs)
            {
                var members = layout.Members(source.root);
                other.node.tabs.InsertRange(tabIndex, members); other.node.selected = members[0];
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
            layout.hosts.Remove(source); Rebuild(); RequestSave();
        }

        internal void Reset() { layout.Reset(contents.Keys); Rebuild(); RequestSave(); }
    }
}
