using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum ShaderGraphPanelGrouping { Tabs = 0, Sections = 1, SideBySide = 3 }
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

}
