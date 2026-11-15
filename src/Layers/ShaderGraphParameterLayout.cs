using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class ShaderGraphParameterItem
    {
        public string id = Guid.NewGuid().ToString("N");
        public string parameterId;
        public ShaderContainerKind kind;
        public int containerId;
        public string title;
        public bool hideTitle;
        public string headerParameterId;
        [SerializeReference] public List<ShaderGraphParameterItem> children = new();
        public bool IsParameter => !string.IsNullOrEmpty(parameterId);
    }

    internal static class ShaderGraphParameterLayout
    {
        internal static bool CanHideTitle(ShaderContainerKind kind) => kind is ShaderContainerKind.Group or ShaderContainerKind.Tabs;
        internal static void SetTitleVisible(ShaderGraphParameterItem item, bool visible)
        {
            if (item == null || item.IsParameter || !CanHideTitle(item.kind))
                throw new FormatException("Only Group and Tabs support hiding their title.");
            if (!visible && !string.IsNullOrEmpty(item.headerParameterId))
                throw new FormatException("A linked header requires a visible title.");
            item.hideTitle = !visible;
            if (visible && string.IsNullOrWhiteSpace(item.title)) item.title = item.kind.ToString();
        }
        internal static IEnumerable<ShaderGraphParameterItem> All(ShaderGraph graph)
        {
            var visited = new HashSet<ShaderGraphParameterItem>();
            var pending = new Stack<ShaderGraphParameterItem>();
            if (graph.parameterLayout != null)
                for (int i = graph.parameterLayout.Count - 1; i >= 0; i--) pending.Push(graph.parameterLayout[i]);
            while (pending.Count != 0)
            {
                var item = pending.Pop();
                if (item == null || !visited.Add(item)) throw new FormatException("Invalid or cyclic parameter layout.");
                if (visited.Count > 1024) throw new FormatException("Parameter layout exceeds 1024 items.");
                yield return item;
                if (item.children != null) for (int i = item.children.Count - 1; i >= 0; i--) pending.Push(item.children[i]);
            }
        }
        internal static ShaderGraphParameterItem Find(ShaderGraph graph, string id)
        { foreach (var item in All(graph)) if (item.id == id) return item; return null; }
        internal static ShaderGraphParameterItem FindParameterItem(ShaderGraph graph, string id)
        { foreach (var item in All(graph)) if (item.parameterId == id) return item; return null; }
        internal static List<ShaderGraphParameterItem> Roots(ShaderGraph graph)
        {
            var roots = new List<ShaderGraphParameterItem>(graph.parameterLayout ?? new());
            var used = new HashSet<string>();
            foreach (var item in All(graph)) if (item.IsParameter) used.Add(item.parameterId);
            foreach (var p in graph.parameters) if (p != null && !used.Contains(p.id))
                roots.Add(new ShaderGraphParameterItem { id = p.id, parameterId = p.id });
            return roots;
        }
        internal static void Normalize(ShaderGraph graph)
        {
            graph.parameterLayout = Roots(graph);
            var assigned = new HashSet<int>();
            foreach (var item in All(graph)) if (!item.IsParameter && item.containerId > 0) assigned.Add(item.containerId);
            int next = 1;
            foreach (var item in All(graph)) if (!item.IsParameter && item.containerId <= 0)
            { while (assigned.Contains(next)) next++; item.containerId = next; assigned.Add(next); }
        }
        internal static bool CanUseHeader(ShaderParameter p, ShaderContainerKind kind)
        {
            if (p == null || kind == ShaderContainerKind.Tabs || p.controls.Count > 1) return false;
            var control = p.controls.Count == 0 ? ShaderGraphParameters.DefaultControl(p) : p.controls[0];
            if (control == null || control.hidden || !string.IsNullOrEmpty(control.visibleIfParameter) || control.enableIf?.Length > 0) return false;
            if (kind == ShaderContainerKind.Tab) return control.type == ShaderParameterType.Toggle && p.Components == 1;
            return control.type is ShaderParameterType.Float or ShaderParameterType.Toggle or ShaderParameterType.Enum or ShaderParameterType.Seed or
                ShaderParameterType.Color or ShaderParameterType.Vector2 or ShaderParameterType.Vector3 or ShaderParameterType.Vector;
        }
        internal static void Validate(ShaderGraph graph)
        {
            var ids = new HashSet<string>(); var parameters = new HashSet<string>(); var containers = new HashSet<int>();
            foreach (var item in All(graph))
            {
                if (string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id) || item.children == null)
                    throw new FormatException("Invalid or duplicate parameter layout ID.");
                if (item.IsParameter)
                {
                    if (item.id != item.parameterId || graph.FindParameter(item.parameterId) == null || !parameters.Add(item.parameterId) || item.children.Count != 0 || item.hideTitle)
                        throw new FormatException("Invalid or repeated parameter layout reference.");
                }
                else if (!Guid.TryParseExact(item.id, "N", out var guid) || guid == Guid.Empty || graph.FindParameter(item.id) != null ||
                    item.containerId <= 0 || !containers.Add(item.containerId))
                    throw new FormatException("Invalid or duplicate parameter container ID.");
            }
            void Visit(List<ShaderGraphParameterItem> items, ShaderGraphParameterItem parent, bool inTabs, ShaderContainerKind? activeGroup, int depth)
            {
                if (depth > 16) throw new FormatException("Parameter layout supports at most 16 container levels.");
                var tabTitles = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in items)
                {
                    if (item.IsParameter)
                    {
                        if (parent?.kind == ShaderContainerKind.Tabs) throw new FormatException("Place parameters inside a Tab, not directly inside Tabs.");
                        continue;
                    }
                    if (!Enum.IsDefined(typeof(ShaderContainerKind), item.kind)) throw new FormatException("Unknown parameter container kind.");
                    if (item.hideTitle && !CanHideTitle(item.kind)) throw new FormatException("Only Group and Tabs support hiding their title.");
                    if ((item.kind == ShaderContainerKind.Tab) != (parent?.kind == ShaderContainerKind.Tabs))
                        throw new FormatException("Tabs contains only Tab pages; a Tab must be directly inside Tabs.");
                    if (item.kind == ShaderContainerKind.Tabs && inTabs) throw new FormatException("Nested Tabs containers are not supported.");
                    if (item.kind == ShaderContainerKind.Group && activeGroup == ShaderContainerKind.Group)
                        throw new FormatException("Nested Group containers require an intervening Foldout.");
                    if (item.kind is ShaderContainerKind.Foldout or ShaderContainerKind.Tab && string.IsNullOrWhiteSpace(item.title))
                        throw new FormatException("Foldout and Tab require a title.");
                    if (item.title?.IndexOfAny(new[] { ';', '\r', '\n', ')' }) >= 0) throw new FormatException("Invalid parameter container title.");
                    if (item.kind == ShaderContainerKind.Tab && !tabTitles.Add(item.title.Trim())) throw new FormatException("Tab titles must be unique within Tabs.");
                    if (!string.IsNullOrEmpty(item.headerParameterId))
                    {
                        var p = graph.FindParameter(item.headerParameterId);
                        if (!CanUseHeader(p, item.kind) || !item.children.Exists(c => c.parameterId == p.id))
                            throw new FormatException("A header requires a supported unconditional direct child; a Tab requires a scalar Toggle.");
                        if (item.hideTitle || string.IsNullOrWhiteSpace(item.title)) throw new FormatException("A container with a header parameter requires a visible title.");
                        if (graph.parameters.Exists(other => other.id != p.id && other.name == p.name))
                            throw new FormatException("A header parameter requires a unique parameter name.");
                    }
                    Visit(item.children, item, inTabs || item.kind == ShaderContainerKind.Tabs,
                        item.kind is ShaderContainerKind.Group or ShaderContainerKind.Foldout ? item.kind : activeGroup, depth + 1);
                }
            }
            Visit(graph.parameterLayout ?? new(), null, false, null, 0);
        }
        private static List<ShaderGraphParameterItem> ParentList(ShaderGraph graph, string id)
        {
            if (graph.parameterLayout.Exists(i => i.id == id)) return graph.parameterLayout;
            foreach (var parent in All(graph)) if (parent.children.Exists(i => i.id == id)) return parent.children;
            throw new FormatException("Parameter layout item is missing.");
        }
        internal static void Move(ShaderGraph graph, string id, string parentId, int index)
        {
            Normalize(graph);
            var item = Find(graph, id) ?? throw new FormatException("Parameter layout item is missing.");
            var parent = string.IsNullOrEmpty(parentId) ? null : Find(graph, parentId) ?? throw new FormatException("Parameter container is missing.");
            if (parent?.IsParameter == true) throw new FormatException("A parameter cannot contain other items.");
            for (var ancestor = parent; ancestor != null; ancestor = FindParent(graph, ancestor.id))
                if (ancestor == item) throw new FormatException("A container cannot be moved inside itself.");
            var from = ParentList(graph, id); var to = parent?.children ?? graph.parameterLayout;
            int original = from.IndexOf(item); index = Math.Clamp(index, 0, to.Count);
            if (from == to && index > original) index--;
            from.RemoveAt(original); to.Insert(index, item);
            try { Validate(graph); }
            catch { to.RemoveAt(index); from.Insert(original, item); throw; }
        }
        internal static ShaderGraphParameterItem AddContainer(ShaderGraph graph, ShaderContainerKind kind, string title, string parentId = null)
        {
            Normalize(graph);
            var parent = string.IsNullOrEmpty(parentId) ? null : Find(graph, parentId) ?? throw new FormatException("Parameter container is missing.");
            if (parent?.IsParameter == true) throw new FormatException("A parameter cannot contain other items.");
            var item = new ShaderGraphParameterItem { kind = kind, title = title };
            if (kind == ShaderContainerKind.Tabs) item.children.Add(new ShaderGraphParameterItem { kind = ShaderContainerKind.Tab, title = "Tab" });
            var list = parent?.children ?? graph.parameterLayout; list.Add(item);
            try { Normalize(graph); Validate(graph); }
            catch { (parent?.children ?? graph.parameterLayout).Remove(item); throw; }
            return item;
        }
        internal static void Ungroup(ShaderGraph graph, string id)
        {
            Normalize(graph);
            var item = Find(graph, id) ?? throw new FormatException("Parameter container is missing.");
            if (item.IsParameter) throw new FormatException("Only containers can be ungrouped.");
            var from = ParentList(graph, id); int original = from.IndexOf(item);
            var list = from; int index = original;
            if (item.kind == ShaderContainerKind.Tab)
            {
                var tabs = FindParent(graph, id); list = ParentList(graph, tabs.id); index = list.IndexOf(tabs) + 1;
            }
            var children = new List<ShaderGraphParameterItem>();
            if (item.kind == ShaderContainerKind.Tabs) foreach (var tab in item.children) children.AddRange(tab.children);
            else children.AddRange(item.children);
            from.RemoveAt(original); list.InsertRange(index, children);
            try { Validate(graph); }
            catch { list.RemoveRange(index, children.Count); from.Insert(original, item); throw; }
        }
        internal static ShaderGraphParameterItem FindParent(ShaderGraph graph, string id)
        { foreach (var item in All(graph)) if (item.children.Exists(c => c.id == id)) return item; return null; }
        internal static void RemoveParameter(ShaderGraph graph, string id)
        {
            var items = new List<ShaderGraphParameterItem>(All(graph));
            graph.parameterLayout?.RemoveAll(i => i.parameterId == id);
            foreach (var item in items)
            { item.children.RemoveAll(i => i.parameterId == id); if (item.headerParameterId == id) item.headerParameterId = null; }
        }
        // Node clipboard carries only the ancestors of copied declarations. Paste remaps
        // new declarations and their containers, leaving reused destination parameters in place.
        internal static List<ShaderGraphParameterItem> CopySubset(List<ShaderGraphParameterItem> roots, HashSet<string> selected,
            Dictionary<string, string> parameterIds = null)
        {
            var result = new List<ShaderGraphParameterItem>();
            foreach (var item in roots)
            {
                if (item.IsParameter)
                {
                    if (selected.Contains(item.parameterId))
                    {
                        string id = parameterIds == null ? item.parameterId : parameterIds[item.parameterId];
                        result.Add(new ShaderGraphParameterItem { id = id, parameterId = id });
                    }
                    continue;
                }
                var children = CopySubset(item.children, selected, parameterIds);
                if (children.Count == 0) continue;
                string header = !string.IsNullOrEmpty(item.headerParameterId) && selected.Contains(item.headerParameterId)
                    ? parameterIds == null ? item.headerParameterId : parameterIds[item.headerParameterId] : null;
                result.Add(new ShaderGraphParameterItem { id = parameterIds == null ? item.id : Guid.NewGuid().ToString("N"),
                    containerId = parameterIds == null ? item.containerId : 0, kind = item.kind, title = item.title, hideTitle = item.hideTitle, headerParameterId = header, children = children });
            }
            return result;
        }
        internal static ulong Fingerprint(ShaderGraph graph)
        {
            ulong hash = 14695981039346656037UL;
            void Text(string text) { if (text != null) foreach (char c in text) hash = RenderFingerprint.Mix(hash, c); hash = RenderFingerprint.Mix(hash, 255); }
            foreach (var item in All(graph))
            { Text(item.id); Text(item.parameterId); Text(item.title); Text(item.headerParameterId); hash = RenderFingerprint.Mix(hash, item.hideTitle ? 1UL : 0UL); hash = RenderFingerprint.Mix(hash, (ulong)item.kind); hash = RenderFingerprint.Mix(hash, (ulong)item.containerId); hash = RenderFingerprint.Mix(hash, (ulong)item.children.Count); }
            return hash;
        }
        internal static void Project(ShaderGraph graph, List<ShaderParameter> displayed)
        {
            var byId = new Dictionary<string, ShaderParameter>(); foreach (var p in displayed) byId[p.id] = p;
            var names = new Dictionary<string, string>(); foreach (var p in displayed) names[p.id] = p.name;
            int ordinal = 0; var path = new List<ShaderLayoutContainer>();
            void Visit(List<ShaderGraphParameterItem> items)
            {
                foreach (var item in items)
                {
                    if (item.IsParameter)
                    {
                        if (!byId.Remove(item.parameterId, out var p)) continue;
                        int controlIndex = 0;
                        foreach (var control in p.controls)
                        {
                            control.containers = path.ToArray(); control.order = ordinal * 256 + controlIndex++;
                            control.inGroup = false; control.groupTitle = control.groupHeaderParameter = null;
                            for (int i = path.Count - 1; i >= 0; i--) if (path[i].kind is ShaderContainerKind.Group or ShaderContainerKind.Foldout)
                            { control.inGroup = true; control.groupId = path[i].id; control.groupTitle = path[i].title; control.groupHeaderParameter = path[i].headerParameter; break; }
                        }
                        ordinal++; continue;
                    }
                    string header = item.headerParameterId != null && names.TryGetValue(item.headerParameterId, out var name) ? name : null;
                    path.Add(new ShaderLayoutContainer { id = item.containerId, kind = item.kind, title = item.hideTitle ? null : item.title?.Trim(), headerParameter = header });
                    Visit(item.children); path.RemoveAt(path.Count - 1);
                }
            }
            Visit(Roots(graph));
        }
    }
}
