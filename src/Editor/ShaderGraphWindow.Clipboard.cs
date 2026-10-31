using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        [Serializable]
        private sealed class NodeClipboardData
        {
            public string document, layer;
            public ShaderGraph graph;
        }

        private sealed partial class Canvas
        {
            // Unity object references in this clipboard belong to the current Editor domain.
            private static readonly string ClipboardPrefix = "WhimTex Shader Nodes v1 " + Guid.NewGuid().ToString("N") + "\n";
            private string lastPastedNodes;
            private int pasteCount;

            protected override bool canCopySelection => selection.Any(item => item is INodeView && item is GraphElement element &&
                (element.capabilities & Capabilities.Copiable) != 0);
            protected override bool canCutSelection => window.Editable && canCopySelection;
            protected override bool canDuplicateSelection => window.Editable && canCopySelection;
            protected override bool canDeleteSelection => window.Editable && base.canDeleteSelection;

            private string CopyNodes(IEnumerable<GraphElement> elements)
            {
                if (window.Graph == null) return string.Empty;
                var ids = new HashSet<string>();
                foreach (var element in elements)
                    if (element is INodeView view && (element.capabilities & Capabilities.Copiable) != 0 &&
                        window.Graph.FindNode(view.Id)?.kind != ShaderGraphNodeKind.Output) ids.Add(view.Id);
                if (ids.Count == 0) return string.Empty;
                var fragment = new ShaderGraph();
                var parameters = new HashSet<string>();
                foreach (var node in window.Graph.nodes)
                    if (ids.Contains(node.id))
                    {
                        fragment.nodes.Add(node);
                        if (node.kind == ShaderGraphNodeKind.Parameter) parameters.Add(node.parameterId);
                    }
                foreach (var edge in window.Graph.edges)
                    if (ids.Contains(edge.inputNode) && ids.Contains(edge.outputNode)) fragment.edges.Add(edge);
                foreach (var parameter in window.Graph.parameters)
                    if (parameters.Contains(parameter.id)) fragment.parameters.Add(parameter);
                fragment.functions.AddRange(window.Graph.functions);
                fragment.sources.AddRange(window.Graph.sources);
                fragment.PruneFunctions();
                return ClipboardPrefix + JsonUtility.ToJson(new NodeClipboardData
                {
                    document = UnityObjectID.FromObject(window.document).ToString(), layer = window.layerId,
                    graph = fragment
                });
            }

            private bool CanPasteNodes(string text) => window.Editable && text != null && text.StartsWith(ClipboardPrefix, StringComparison.Ordinal);

            private void PasteNodes(string operation, string text)
            {
                if (!CanPasteNodes(text)) return;
                try
                {
                    var copied = JsonUtility.FromJson<NodeClipboardData>(text.Substring(ClipboardPrefix.Length));
                    var fragment = copied?.graph;
                    if (fragment == null || fragment.version != 1 || fragment.nodes == null || fragment.nodes.Count == 0 ||
                        fragment.nodes.Count > 512 || fragment.edges == null || fragment.edges.Count > 4096 ||
                        fragment.parameters == null || fragment.parameters.Count > 256 || fragment.functions == null || fragment.sources == null)
                        throw new FormatException("The Shader node clipboard is incomplete or no longer available.");
                    if (fragment.nodes.Exists(n => n == null || n.kind == ShaderGraphNodeKind.Output))
                        throw new FormatException("Output cannot be pasted; a graph has one Output node.");

                    var current = window.Graph;
                    var next = new ShaderGraph
                    {
                        version = current.version,
                        nodes = new List<ShaderGraphNode>(current.nodes), edges = new List<ShaderGraphEdge>(current.edges),
                        parameters = new List<ShaderFXParameter>(current.parameters), functions = new List<ShaderGraphFunction>(current.functions),
                        sources = new List<ShaderGraphSource>(current.sources)
                    };
                    bool sameDocument = copied.document == UnityObjectID.FromObject(window.document).ToString();
                    bool sameGraph = sameDocument && copied.layer == window.layerId;
                    var parameterIds = new Dictionary<string, string>();
                    foreach (var parameter in fragment.parameters)
                    {
                        if (parameter == null || string.IsNullOrEmpty(parameter.id) || parameterIds.ContainsKey(parameter.id))
                            throw new FormatException("Invalid Shader clipboard parameter.");
                        var existing = sameGraph ? current.FindParameter(parameter.id) : null;
                        if (existing != null && existing.type == parameter.type && existing.ValueType == parameter.ValueType)
                        { parameterIds.Add(parameter.id, existing.id); continue; }
                        string oldId = parameter.id;
                        parameter.id = Guid.NewGuid().ToString("N");
                        string name = parameter.name; int suffix = 1;
                        while (next.parameters.Exists(p => p.name == parameter.name)) parameter.name = name + " " + ++suffix;
                        if (parameter.type == ShaderFXParameterType.Texture2D && parameter.textureSource == ShaderFXTextureSource.Layer &&
                            (!sameDocument || !window.document.IsUsableShaderTexture(window.Behaviour.Owner, parameter.textureLayerId)))
                        { parameter.textureLayerId = null; parameter.textureSource = ShaderFXTextureSource.None; }
                        next.parameters.Add(parameter); parameterIds.Add(oldId, parameter.id);
                    }

                    var sourceIds = new Dictionary<string, ShaderGraphSource>();
                    foreach (var source in next.sources) sourceIds.Add(source.id, source);
                    foreach (var source in fragment.sources)
                    {
                        if (source == null || string.IsNullOrEmpty(source.id)) throw new FormatException("Invalid Shader clipboard source.");
                        if (sourceIds.TryGetValue(source.id, out var existing))
                        {
                            if (!SameSource(existing, source)) throw new FormatException("Shader include backups have conflicting IDs: " + source.id);
                        }
                        else { next.sources.Add(source); sourceIds.Add(source.id, source); }
                    }

                    int repeat = text == lastPastedNodes ? pasteCount + 1 : 1;
                    Vector2 min = fragment.nodes[0].position, max = min;
                    foreach (var node in fragment.nodes) { min = Vector2.Min(min, node.position); max = Vector2.Max(max, node.position); }
                    Vector2 offset = sameGraph ? new Vector2(30, 30) * repeat : CenterPosition - (min + max) * .5f + new Vector2(30, 30) * (repeat - 1);
                    var nodeIds = new Dictionary<string, string>();
                    foreach (var node in fragment.nodes)
                    {
                        if (string.IsNullOrEmpty(node.id) || nodeIds.ContainsKey(node.id)) throw new FormatException("Duplicate or missing Shader clipboard node ID.");
                        string oldId = node.id; node.id = Guid.NewGuid().ToString("N"); nodeIds.Add(oldId, node.id);
                        node.position += offset;
                        if (node.kind == ShaderGraphNodeKind.Parameter)
                        {
                            if (!parameterIds.TryGetValue(node.parameterId ?? "", out var id)) throw new FormatException("Shader clipboard parameter is missing.");
                            node.parameterId = id;
                        }
                        if (node.kind == ShaderGraphNodeKind.CustomFunction)
                        {
                            var source = fragment.FindFunction(node.functionId);
                            if (source == null) throw new FormatException("Shader clipboard function is missing.");
                            var function = CopyFunctionData(source);
                            function.id = Guid.NewGuid().ToString("N"); node.functionId = function.id;
                            next.functions.Add(function);
                        }
                        next.nodes.Add(node);
                    }
                    foreach (var edge in fragment.edges)
                    {
                        if (edge == null || !nodeIds.TryGetValue(edge.inputNode ?? "", out var input) || !nodeIds.TryGetValue(edge.outputNode ?? "", out var output))
                            throw new FormatException("Shader clipboard connection leaves the copied selection.");
                        edge.inputNode = input; edge.outputNode = output; next.edges.Add(edge);
                    }
                    next.Validate();
                    window.Edit(operation == "Duplicate" ? "Duplicate Shader Nodes" : "Paste Shader Nodes", graph =>
                    {
                        graph.parameters = next.parameters; graph.nodes = next.nodes; graph.edges = next.edges;
                        graph.functions = next.functions; graph.sources = next.sources;
                    }, rebuild: false);
                    // Native selection state must change in the same Undo group as the paste.
                    Rebuild();
                    ClearSelection();
                    foreach (var node in fragment.nodes) AddToSelection(nodeViews[node.id]);
                    lastPastedNodes = text; pasteCount = repeat;
                }
                catch (Exception error) { window.ShowError("Cannot paste Shader nodes: " + error.Message); }
            }

            private static bool SameSource(ShaderGraphSource a, ShaderGraphSource b)
            {
                if (a.code != b.code || a.includes == null || b.includes == null || a.includes.Count != b.includes.Count) return false;
                for (int i = 0; i < a.includes.Count; i++)
                {
                    var x = a.includes[i]; var y = b.includes[i];
                    if (x == null || y == null || x.requested != y.requested || x.sourceId != y.sourceId) return false;
                }
                return true;
            }
        }
    }
}
