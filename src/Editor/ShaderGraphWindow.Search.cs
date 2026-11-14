using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        static bool MatchesReplacement(object choice, ShaderGraphNode node) => node != null && (choice switch
        {
            ShaderGraphFunctionCatalog.Entry entry => node.function?.inline == false &&
                entry.path == node.function.sourcePath && entry.definition.Signature() == node.function.Signature(),
            ShaderGraphNodeKind kind => kind == node.kind && (kind != ShaderGraphNodeKind.CustomFunction || node.function?.inline != false),
            ShaderGraphSubGraph definition => node.subGraphId == definition.id,
            string parameter => node.parameterId == parameter && node.kind == ShaderGraphNodeKind.Parameter,
            _ => false
        });

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
                        var item = new WhimTexSearchDropdown.Item(entry.content.text, entry is SearchTreeGroupEntry, entry)
                        { icon = entry.content.image as Texture2D, highlighted = MatchesReplacement(entry.userData, replacement) };
                        if (entry.userData is ShaderGraphNodeKind.FWidth or ConnectedNodeChoice { kind: ShaderGraphNodeKind.FWidth })
                            item.searchAlias = "DDXY";
                        parents[parents.Count - 1].children.Add(item);
                        if (item.highlighted) path = parents.Skip(1).Select(parent => parent.text).ToArray();
                        if (item.group) parents.Add(item);
                    }
                    var graph = canvas.window.Graph;
                    bool svmFilter = canvas.window.svmNodesOnly;
                    return WhimTexSearchDropdown.Show(screenPosition, root,
                        item => OnSelectEntry((SearchTreeEntry)item.data, context), path, owner: this,
                        valid: () => canvas?.panel != null && canvas.window.Editable && canvas.window.svmNodesOnly == svmFilter && ReferenceEquals(canvas.window.Graph, graph));
                }
            }
        }
    }
}
