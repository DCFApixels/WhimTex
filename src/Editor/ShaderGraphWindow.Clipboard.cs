using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        [Serializable]
        private sealed class NodeClipboardData
        {
            public string document, layer, target;
            public ShaderGraph graph;
            public Rect bounds;
            public bool hasBounds;
        }

        private sealed partial class Canvas
        {
            // Native object references survive script reload, but not an Editor restart.
            private static readonly string ClipboardPrefix = GetClipboardPrefix();

            private static string GetClipboardPrefix()
            {
                const string key = "WhimTex.ShaderGraph.ClipboardSession";
                string session = SessionState.GetString(key, "");
                if (string.IsNullOrEmpty(session))
                {
                    session = Guid.NewGuid().ToString("N");
                    SessionState.SetString(key, session);
                }
                return "WhimTex Shader Nodes v2 " + session + "\n";
            }

            protected override bool canCopySelection => selection.Any(item => item is IGraphItemView && item is GraphElement element &&
                (element.capabilities & Capabilities.Copiable) != 0);
            protected override bool canCutSelection => window.Editable && canCopySelection;
            protected override bool canDuplicateSelection => window.Editable && canCopySelection;
            protected override bool canDeleteSelection => window.Editable && base.canDeleteSelection;

            private bool HandleClipboardShortcut(KeyDownEvent e)
            {
                if (!e.actionKey || e.altKey || e.shiftKey || panel?.GetCapturingElement(PointerId.mousePointerId) != null) return false;
                switch (e.keyCode)
                {
                    case KeyCode.C when canCopySelection: CopySelectionCallback(); break;
                    case KeyCode.V when canPaste: PasteCallback(); break;
                    case KeyCode.X when canCutSelection: CutSelectionCallback(); break;
                    case KeyCode.D when canDuplicateSelection: DuplicateSelectionCallback(); break;
                    default: return false;
                }
                e.StopImmediatePropagation();
                e.imguiEvent?.Use();
                return true;
            }

            private string CopyNodes(IEnumerable<GraphElement> elements)
            {
                if (window.Graph == null) return string.Empty;
                var ids = new HashSet<string>();
                foreach (var element in elements)
                    if (element is IGraphItemView view && (element.capabilities & Capabilities.Copiable) != 0 &&
                        window.Graph.FindNode(view.Id)?.kind != ShaderGraphNodeKind.Output && !ShaderGraphNodes.IsSubGraphInterface(window.Graph.FindNode(view.Id)?.kind ?? ShaderGraphNodeKind.Output)) ids.Add(view.Id);
                if (ids.Count == 0) return string.Empty;
                foreach (var group in window.Graph.groups) if (ids.Contains(group.id))
                    foreach (string id in group.elements) if (window.Graph.FindNode(id)?.kind != ShaderGraphNodeKind.Output) ids.Add(id);
                ids.RemoveWhere(id => ShaderGraphNodes.IsSubGraphInterface(window.Graph.FindNode(id)?.kind ?? ShaderGraphNodeKind.Output));
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
                CollectSubGraphs(fragment, window.Graph, parameters);
                foreach (var parameter in window.Graph.parameters)
                    if (parameters.Contains(parameter.id)) fragment.parameters.Add(parameter);
                foreach (var note in window.Graph.notes) if (ids.Contains(note.id)) fragment.notes.Add(note);
                foreach (var group in window.Graph.groups) if (ids.Contains(group.id))
                    fragment.groups.Add(new ShaderGraphGroup { id = group.id, title = group.title, position = group.position,
                        elements = group.elements.Where(id => ids.Contains(id)).ToList() });
                fragment.functions.AddRange(window.Graph.functions);
                fragment.sources.AddRange(window.Graph.sources);
                fragment.PruneFunctions();
                bool hasBounds = TryClipboardBounds(fragment, out Rect bounds);
                return ClipboardPrefix + JsonUtility.ToJson(new NodeClipboardData
                {
                    document = UnityObjectID.FromObject(window.document).ToString(), layer = window.layerId, target = window.Target.Identity,
                    graph = fragment, bounds = bounds, hasBounds = hasBounds
                });
            }

            private bool TryClipboardBounds(ShaderGraph fragment, out Rect bounds)
            {
                Rect combined = default;
                bool any = false;
                bool Include(string id)
                {
                    var view = ItemView(id);
                    if (view == null) return false;
                    Rect rect = view.GetPosition();
                    if (!ValidClipboardBounds(rect)) return false;
                    combined = any ? Rect.MinMaxRect(Mathf.Min(combined.xMin, rect.xMin), Mathf.Min(combined.yMin, rect.yMin),
                        Mathf.Max(combined.xMax, rect.xMax), Mathf.Max(combined.yMax, rect.yMax)) : rect;
                    any = true;
                    return true;
                }
                bounds = default;
                foreach (var node in fragment.nodes) if (!Include(node.id)) return false;
                foreach (var note in fragment.notes) if (!Include(note.id)) return false;
                foreach (var group in fragment.groups) if (!Include(group.id)) return false;
                bounds = combined;
                return any;
            }

            private static bool ValidClipboardBounds(Rect rect) => rect.width > 0 && rect.height > 0 &&
                ProjectiveMatrix.Finite(rect.xMin) && ProjectiveMatrix.Finite(rect.yMin) &&
                ProjectiveMatrix.Finite(rect.xMax) && ProjectiveMatrix.Finite(rect.yMax);

            private static Rect ClipboardBounds(NodeClipboardData copied)
            {
                if (copied.hasBounds && ValidClipboardBounds(copied.bounds)) return copied.bounds;
                // A fragment already on the clipboard can recover its bounds from its still-open source.
                foreach (var source in Resources.FindObjectsOfTypeAll<ShaderGraphWindow>())
                    if (source.document != null && source.Target.Identity == copied.target &&
                        UnityObjectID.FromObject(source.document).ToString() == copied.document &&
                        source.canvas != null && source.canvas.TryClipboardBounds(copied.graph, out Rect bounds)) return bounds;
                throw new FormatException("Copy the nodes again after their layout is ready.");
            }

            private bool CanPasteNodes(string text) => window.Editable && text != null && text.StartsWith(ClipboardPrefix, StringComparison.Ordinal);

            private void PasteNodes(string operation, string text)
            {
                if (!CanPasteNodes(text)) return;
                try
                {
                    var copied = JsonUtility.FromJson<NodeClipboardData>(text.Substring(ClipboardPrefix.Length));
                    var fragment = copied?.graph;
                    if (fragment == null || fragment.version != 1 || fragment.nodes == null || fragment.notes == null || fragment.groups == null ||
                        fragment.nodes.Count + fragment.notes.Count + fragment.groups.Count == 0 || fragment.notes.Count > 512 || fragment.groups.Count > 512 ||
                        fragment.nodes.Count > 512 || fragment.edges == null || fragment.edges.Count > 4096 ||
                        fragment.parameters == null || fragment.parameters.Count > 256 || fragment.functions == null || fragment.sources == null)
                        throw new FormatException("The Shader node clipboard is incomplete or no longer available.");
                    if (fragment.nodes.Exists(n => n == null || n.kind == ShaderGraphNodeKind.Output || ShaderGraphNodes.IsSubGraphInterface(n.kind)))
                        throw new FormatException("Output cannot be pasted; a graph has one Output node.");

                    var current = window.Graph;
                    var next = new ShaderGraph
                    {
                        version = current.version,
                        nodes = new List<ShaderGraphNode>(current.nodes), edges = new List<ShaderGraphEdge>(current.edges),
                        parameters = new List<ShaderFXParameter>(current.parameters), functions = new List<ShaderGraphFunction>(current.functions),
                        sources = new List<ShaderGraphSource>(current.sources),
                        subGraphs = new List<ShaderGraphSubGraph>(current.subGraphs), activeSubGraph = current.activeSubGraph,
                        notes = new List<ShaderGraphNote>(current.notes), groups = new List<ShaderGraphGroup>(current.groups)
                    };
                    bool sameDocument = copied.document == UnityObjectID.FromObject(window.document).ToString();
                    bool sameGraph = sameDocument && copied.target == window.Target.Identity;
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
                            (!sameDocument || !window.document.IsUsableStoredShaderTexture(window.Target.Layer, parameter.textureLayerId)))
                        { parameter.textureLayerId = null; parameter.textureSource = ShaderFXTextureSource.None; }
                        if (parameter.textureSource == ShaderFXTextureSource.Self && !window.Target.IsEffect)
                            parameter.textureSource = ShaderFXTextureSource.None;
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

                    Vector2 offset = CenterPosition - ClipboardBounds(copied).center;
                    if (!ProjectiveMatrix.Finite(offset.x) || !ProjectiveMatrix.Finite(offset.y))
                        throw new InvalidOperationException("The graph view layout is not ready for paste.");
                    var definitionIds = ImportSubGraphs(fragment, next, sameGraph, parameterIds);
                    var nodeIds = new Dictionary<string, string>();
                    foreach (var node in fragment.nodes)
                    {
                        if (string.IsNullOrEmpty(node.id) || nodeIds.ContainsKey(node.id)) throw new FormatException("Duplicate or missing Shader clipboard node ID.");
                        string oldId = node.id; node.id = Guid.NewGuid().ToString("N"); nodeIds.Add(oldId, node.id);
                        node.position += offset;
                        if (node.kind == ShaderGraphNodeKind.SubGraph)
                        {
                            if (!definitionIds.TryGetValue(node.subGraphId ?? "", out string definitionId)) throw new FormatException("Shader clipboard Sub Graph is missing.");
                            node.subGraphId = definitionId;
                        }
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
                    foreach (var note in fragment.notes)
                    {
                        if (note == null || string.IsNullOrEmpty(note.id) || nodeIds.ContainsKey(note.id)) throw new FormatException("Invalid Shader clipboard note ID.");
                        string oldId = note.id; note.id = Guid.NewGuid().ToString("N"); nodeIds.Add(oldId, note.id);
                        note.position += offset; next.notes.Add(note);
                    }
                    foreach (var group in fragment.groups)
                    {
                        if (group == null || string.IsNullOrEmpty(group.id) || nodeIds.ContainsKey(group.id) || group.elements == null)
                            throw new FormatException("Invalid Shader clipboard group.");
                        for (int i = 0; i < group.elements.Count; i++)
                        {
                            if (!nodeIds.TryGetValue(group.elements[i], out var id)) throw new FormatException("Shader clipboard group leaves the copied selection.");
                            group.elements[i] = id;
                        }
                        string oldId = group.id; group.id = Guid.NewGuid().ToString("N"); nodeIds.Add(oldId, group.id);
                        group.position += offset; next.groups.Add(group);
                    }
                    foreach (var edge in fragment.edges)
                    {
                        if (edge == null || !nodeIds.TryGetValue(edge.inputNode ?? "", out var input) || !nodeIds.TryGetValue(edge.outputNode ?? "", out var output))
                            throw new FormatException("Shader clipboard connection leaves the copied selection.");
                        edge.inputNode = input; edge.outputNode = output; next.edges.Add(edge);
                    }
                    if (next.activeSubGraph is ShaderGraphSubGraph definition)
                    {
                        var candidate = new ShaderGraphSubGraph
                        {
                            id = definition.id, name = definition.name, inputs = definition.inputs, outputs = definition.outputs,
                            nodes = next.nodes, edges = next.edges, notes = next.notes, groups = next.groups
                        };
                        int index = next.subGraphs.FindIndex(item => item.id == definition.id);
                        next.subGraphs[index] = candidate; next.activeSubGraph = candidate;
                    }
                    ShaderGraphCompiler.ValidateStorage(next, effectContext: window.Target.IsEffect);
                    // Keep the live definition referenced by sidebar callbacks after validation.
                    if (next.activeSubGraph != null)
                        next.subGraphs[next.subGraphs.FindIndex(item => item.id == current.activeSubGraph.id)] = current.activeSubGraph;
                    window.Edit(operation == "Duplicate" ? "Duplicate Shader Nodes" : "Paste Shader Nodes", graph =>
                    {
                        graph.parameters = next.parameters; graph.nodes = next.nodes; graph.edges = next.edges;
                        graph.functions = next.functions; graph.sources = next.sources;
                        graph.subGraphs = next.subGraphs;
                        graph.notes = next.notes; graph.groups = next.groups;
                    }, structural: fragment.nodes.Count > 0, rebuild: false, layoutOnly: fragment.nodes.Count == 0);
                    // Native selection state must change in the same Undo group as the paste.
                    Rebuild();
                    ClearSelection();
                    foreach (var node in fragment.nodes) AddToSelection(nodeViews[node.id]);
                    foreach (var note in fragment.notes) AddToSelection(noteViews[note.id]);
                    foreach (var group in fragment.groups) AddToSelection(groupViews[group.id]);
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
