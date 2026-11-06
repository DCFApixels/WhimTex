using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using GraphNodeView = UnityEditor.Experimental.GraphView.Node;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed partial class Canvas
        {
            readonly ShaderGraph viewScratch = new();
            readonly ShaderGraphSubGraph interfaceScratch = new();
            readonly Dictionary<string, (ShaderGraphNode node, ShaderGraphFunction function, ShaderGraphSubGraph definition, ulong layout)> viewStamps = new();
            readonly Dictionary<(string, string, string, string), Edge> edgeViews = new();
            readonly HashSet<string> retainedNodes = new();
            readonly HashSet<(string, string, string, string)> retainedEdges = new();
            readonly List<string> staleNodes = new();
            readonly List<(string, string, string, string)> staleEdges = new();

            ulong ViewStamp(ShaderGraphNode node)
            {
                viewScratch.nodes.Clear(); viewScratch.nodes.Add(node);
                viewScratch.parameters.Clear(); viewScratch.functions.Clear(); viewScratch.subGraphs.Clear();
                if (window.Graph.FindParameter(node.parameterId) is ShaderParameter parameter) viewScratch.parameters.Add(parameter);
                if (node.function != null) viewScratch.functions.Add(node.function);
                if (node.subGraph != null && (node.kind == ShaderGraphNodeKind.SubGraph || ShaderGraphNodes.IsSubGraphInterface(node.kind)))
                {
                    interfaceScratch.id = node.subGraph.id; interfaceScratch.name = node.subGraph.name;
                    interfaceScratch.inputs = node.subGraph.inputs; interfaceScratch.outputs = node.subGraph.outputs;
                    viewScratch.subGraphs.Add(interfaceScratch);
                }
                ulong stamp = LayoutFingerprint(viewScratch);
                foreach (var input in ShaderGraphNodes.StoredInputs(node))
                {
                    bool connected = window.Types.HasInput(node.id, input.name);
                    bool visible = ShaderGraphNodes.InputVisible(node, input.name) || connected;
                    stamp = RenderFingerprint.Mix(stamp, visible ? 1UL : 0UL);
                    if (!visible) continue;
                    stamp = RenderFingerprint.Mix(stamp, connected ? 1UL : 0UL);
                    stamp = RenderFingerprint.Mix(stamp, (ulong)(input.type ?? window.InputType(node, input.name)));
                }
                foreach (string output in ShaderGraphNodes.StoredOutputs(node))
                {
                    bool visible = ShaderGraphNodes.OutputVisible(node, output) || window.Types.HasOutput(node.id, output);
                    stamp = RenderFingerprint.Mix(stamp, visible ? 1UL : 0UL);
                    if (visible) stamp = RenderFingerprint.Mix(stamp, (ulong)window.OutputType(node, output));
                }
                return stamp;
            }

            void ReconcileNodes()
            {
                window.typesInvalidated = true;
                var graph = window.Graph;
                ResetBranchTraces();
                var selected = new HashSet<string>();
                foreach (var item in selection) if (item is IGraphItemView view) selected.Add(view.Id);
                if (selectAfterRebuild != null)
                {
                    foreach (var item in selection) if (item is GraphElement element) { element.selected = false; element.OnUnselected(); }
                    selection.Clear(); selected.Clear(); selected.Add(selectAfterRebuild); selectAfterRebuild = null;
                }
                rebuilding = true;
                try
                {
                    ClearAnnotations();
                    foreach (var edge in edges.ToList())
                        if (edge.userData is not ShaderGraphEdge data ||
                            !edgeViews.TryGetValue((data.outputNode, data.outputPort, data.inputNode, data.inputPort), out var owned) || !ReferenceEquals(owned, edge))
                            RemoveViewEdge(edge);
                    retainedNodes.Clear(); staleNodes.Clear();
                    if (graph != null) foreach (var node in graph.nodes)
                    {
                        if (node == null) continue;
                        retainedNodes.Add(node.id);
                        ulong stamp = ViewStamp(node);
                        if (viewStamps.TryGetValue(node.id, out var previous) &&
                            (!ReferenceEquals(previous.node, node) || !ReferenceEquals(previous.function, node.function) ||
                             !ReferenceEquals(previous.definition, node.subGraph) || previous.layout != stamp)) staleNodes.Add(node.id);
                    }
                    foreach (string id in nodeViews.Keys) if (!retainedNodes.Contains(id)) staleNodes.Add(id);
                    foreach (string id in staleNodes)
                    {
                        if (previews.TryGetValue(id, out var preview)) preview.RemoveFromHierarchy();
                        var view = nodeViews[id];
                        foreach (var port in view.Query<Port>().ToList())
                            if (port.userData is PortData data) (data.direction == Direction.Input ? inputPorts : outputPorts).Remove(id + ":" + data.name);
                        selection.Remove(view); RemoveElement(view); nodeViews.Remove(id); viewStamps.Remove(id);
                    }
                    staleNodes.Clear();
                    foreach (var pair in previews)
                        if (!NodePreview.Supports(window, window.Types.Node(pair.Key))) staleNodes.Add(pair.Key);
                    foreach (string id in staleNodes)
                    { var preview = previews[id]; previewQueue.Remove(preview); preview.Dispose(); previews.Remove(id); }

                    if (graph != null) foreach (var node in graph.nodes)
                    {
                        if (node == null) continue;
                        if (!nodeViews.TryGetValue(node.id, out var view))
                        {
                            view = node.kind switch
                            {
                                ShaderGraphNodeKind.Parameter => new ParameterView(window, this, node),
                                ShaderGraphNodeKind.Reroute => new RerouteView(window, this, node),
                                _ => new NodeView(window, this, node)
                            };
                            nodeViews.Add(node.id, view); AddElement(view);
                            viewStamps[node.id] = (node, node.function, node.subGraph, ViewStamp(node));
                            RestoreSelection(view, selected);
                        }
                        else ((INodeView)view).RefreshValues();
                    }
                    retainedEdges.Clear(); staleEdges.Clear();
                    if (graph != null) foreach (var data in graph.edges)
                    {
                        if (data == null || !inputPorts.TryGetValue(data.inputNode + ":" + data.inputPort, out var input) ||
                            !outputPorts.TryGetValue(data.outputNode + ":" + data.outputPort, out var output)) continue;
                        var key = (data.outputNode, data.outputPort, data.inputNode, data.inputPort);
                        retainedEdges.Add(key);
                        if (edgeViews.TryGetValue(key, out var edge) && (edge.input != input || edge.output != output || edge.parent == null))
                        { RemoveViewEdge(edge); edgeViews.Remove(key); edge = null; }
                        if (edge == null)
                        { edge = output.ConnectTo<GraphEdge>(input); edgeViews.Add(key, edge); AddElement(edge); }
                        edge.userData = data;
                    }
                    foreach (var pair in edgeViews) if (!retainedEdges.Contains(pair.Key)) staleEdges.Add(pair.Key);
                    foreach (var key in staleEdges) { RemoveViewEdge(edgeViews[key]); edgeViews.Remove(key); }
                    if (graph != null) BuildAnnotations(selected);
                    RebuildBadges();
                    UpdateViewTransform(window.viewPosition, window.viewScale == Vector3.zero ? Vector3.one : window.viewScale);
                }
                finally { rebuilding = false; }
            }
            void RemoveViewEdge(Edge edge)
            { edge.input?.Disconnect(edge); edge.output?.Disconnect(edge); selection.Remove(edge); RemoveElement(edge); }
        }
    }
}
