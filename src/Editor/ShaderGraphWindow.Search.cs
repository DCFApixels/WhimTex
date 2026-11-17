using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private static void SortSearchCategories(List<SearchTreeEntry> tree)
        {
            var groups = new List<(int start, int count, float order, string name)>();
            for (int start = 1; start < tree.Count;)
            {
                int end = start + 1;
                while (end < tree.Count && tree[end].level > 1) end++;
                string name = tree[start].content.text;
                var categoryColor = ShaderGraphCatalogIcon.ForCategory(name).color;
                Color.RGBToHSV(categoryColor, out float hue, out float saturation, out _);
                float order = name switch { "Procedural" => -1, "Sub Graphs" => 2, "HLSL" => 3, _ => saturation == 0 ? 1 : hue };
                if (categoryColor == ShaderGraphNodeColors.Category("Input")) order = -2;
                groups.Add((start, end - start, order, name));
                start = end;
            }
            groups.Sort((a, b) =>
            {
                int color = a.order.CompareTo(b.order);
                return color != 0 ? color : StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name);
            });
            var original = tree.ToArray();
            int destination = 1;
            foreach (var group in groups)
                for (int i = 0; i < group.count; i++) tree[destination++] = original[group.start + i];
        }

        private bool CreateNodeChoice(object choice, Vector2 position)
        {
            if (!Editable || Graph == null) return false;
            switch (choice)
            {
                case ShaderGraphNodeKind kind: AddNode(kind, position); break;
                case string parameter when Graph.FindParameter(parameter) != null:
                    AddNode(ShaderGraphNodeKind.Parameter, position, parameter); break;
                case ShaderGraphSubGraph definition when Graph.FindSubGraph(definition.id) == definition && !WouldRecurse(definition.id):
                    AddSubGraph(definition, position); break;
                case ShaderGraphFunctionCatalog.Entry function when ShaderGraphFunctionCatalog.Entries.Contains(function):
                    AddFunctionNode(function, position); break;
                default: return false;
            }
            return true;
        }

        static bool MatchesReplacement(object choice, ShaderGraphNode node) => node != null && (choice switch
        {
            ShaderGraphFunctionCatalog.Entry entry => node.function?.inline == false &&
                entry.path == node.function.sourcePath && entry.definition.Signature() == node.function.Signature(),
            ShaderGraphNodeKind kind => kind == node.kind && (kind != ShaderGraphNodeKind.CustomFunction || node.function?.inline != false),
            ShaderGraphSubGraph definition => node.subGraphId == definition.id,
            string parameter => node.parameterId == parameter && node.kind == ShaderGraphNodeKind.Parameter,
            _ => false
        });

        private static ShaderGraphCatalogIcon.Appearance CatalogAppearance(SearchTreeEntry entry, string path)
        {
            if (entry is SearchTreeGroupEntry) return ShaderGraphCatalogIcon.ForCategory(path);
            var kind = entry.userData switch
            {
                ShaderGraphNodeKind value => value,
                ConnectedNodeChoice value => value.kind,
                ShaderGraphFunctionCatalog.Entry => ShaderGraphNodeKind.CustomFunction,
                ShaderGraphSubGraph => ShaderGraphNodeKind.SubGraph,
                string => ShaderGraphNodeKind.Parameter,
                _ => ShaderGraphNodeKind.Reroute
            };
            return ShaderGraphCatalogIcon.ForNode(kind);
        }

        private sealed partial class Canvas
        {
            private sealed partial class NodeSearch
            {
                internal WhimTexSearchDropdown Open(Vector2 screenPosition)
                {
                    var context = new SearchWindowContext(screenPosition);
                    var tree = CreateSearchTree(context);
                    if (tree == null || tree.Count == 0) return null;
                    var root = new WhimTexSearchDropdown.Item(tree[0].content.text, true);
                    var parents = new List<WhimTexSearchDropdown.Item> { root };
                    string[] path = null;
                    for (int i = 1; i < tree.Count; i++)
                    {
                        var entry = tree[i];
                        while (parents.Count > entry.level) parents.RemoveAt(parents.Count - 1);
                        string categoryPath = string.Join("/", parents.Skip(1).Select(parent => parent.text).Append(entry.content.text));
                        var appearance = CatalogAppearance(entry, categoryPath);
                        var item = new WhimTexSearchDropdown.Item(entry.content.text, entry is SearchTreeGroupEntry, entry)
                        { makeIcon = () => { var visual = new ShaderGraphCatalogPreview(); visual.Bind(appearance); return visual; }, accentColor = appearance.group ? null : appearance.color,
                            highlighted = MatchesReplacement(entry.userData, replacement) };
                        if (entry.userData is ShaderGraphNodeKind kind) item.searchAlias = ShaderGraphNodes.SearchAliases(kind);
                        else if (entry.userData is ConnectedNodeChoice choice) item.searchAlias = ShaderGraphNodes.SearchAliases(choice.kind);
                        parents[parents.Count - 1].children.Add(item);
                        if (item.highlighted) path = parents.Skip(1).Select(parent => parent.text).ToArray();
                        if (item.group) parents.Add(item);
                    }
                    var graph = canvas.window.Graph;
                    bool svmFilter = canvas.window.svmNodesOnly;
                    return WhimTexSearchDropdown.Show(screenPosition, root,
                        item => OnSelectEntry((SearchTreeEntry)item.data, context), path, owner: this,
                        makeOptions: ShaderGraphCatalogThumbnails.CreateSelector,
                        rowHeight: item => ShaderGraphCatalogThumbnails.Height(CatalogAppearance((SearchTreeEntry)item.data, item.text).kind, 20),
                        valid: () => canvas?.panel != null && canvas.window.Editable && canvas.window.svmNodesOnly == svmFilter && ReferenceEquals(canvas.window.Graph, graph));
                }
            }
        }
    }
}
