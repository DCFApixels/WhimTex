using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using GraphNodeView = UnityEditor.Experimental.GraphView.Node;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class GraphPort : Port
        {
            static readonly ConnectionListener listener = new ConnectionListener();
            internal GraphPort(Direction direction, Type type)
                : base(Orientation.Horizontal, direction, direction == Direction.Input ? Capacity.Single : Capacity.Multi, type)
            {
                m_EdgeConnector = new EdgeConnector<Edge>(listener);
                this.AddManipulator(m_EdgeConnector);
            }
        }

        private sealed class ConnectionListener : IEdgeConnectorListener
        {
            public void OnDropOutsidePort(Edge edge, Vector2 position)
            {
                var dragged = edge.output?.edgeConnector.edgeDragHelper.draggedPort ?? edge.input?.edgeConnector.edgeDragHelper.draggedPort;
                if (dragged?.direction != Direction.Output || dragged.userData is not PortData source) return;
                var canvas = dragged.GetFirstAncestorOfType<Canvas>();
                if (canvas != null && canvas.IsEmptyDrop(position)) canvas.ShowConnectedSearch(source, position);
            }

            public void OnDrop(GraphView graphView, Edge edge)
            {
                if (graphView is not Canvas canvas || edge.input == null || edge.output == null) return;
                var change = canvas.graphViewChanged(new GraphViewChange { edgesToCreate = new List<Edge> { edge } });
                if (change.edgesToCreate == null) return;
                foreach (var accepted in change.edgesToCreate)
                {
                    // The model replaces a single input atomically; update the visible ports too.
                    foreach (var old in accepted.input.connections.ToArray())
                    {
                        if (old == accepted) continue;
                        old.input?.Disconnect(old); old.output?.Disconnect(old); canvas.RemoveElement(old);
                    }
                    if (accepted.parent == null) canvas.AddElement(accepted);
                    accepted.input.Connect(accepted); accepted.output.Connect(accepted);
                }
            }
        }

        private readonly struct ConnectedNodeChoice
        {
            internal readonly ShaderGraphNodeKind kind;
            internal readonly string input;
            internal ConnectedNodeChoice(ShaderGraphNodeKind kind, string input) { this.kind = kind; this.input = input; }
        }

        private sealed partial class Canvas
        {
            internal bool IsEmptyDrop(Vector2 position)
            {
                if (panel == null || !worldBound.Contains(position)) return false;
                var hits = new List<VisualElement>();
                panel.PickAll(position, hits);
                foreach (var hit in hits)
                {
                    if (hit is GraphNodeView || hit.GetFirstAncestorOfType<GraphNodeView>() != null) return false;
                    var edge = EdgeAt(hit);
                    if (edge?.userData is ShaderGraphEdge) return false;
                }
                return true;
            }

            internal void ShowConnectedSearch(PortData source, Vector2 panelPosition)
            {
                if (!window.Editable || !CanAddConnection()) return;
                if (search == null) search = ScriptableObject.CreateInstance<NodeSearch>();
                search.canvas = this; search.sourceGraph = window.Graph;
                search.source = source;
                search.dropPosition = contentViewContainer.WorldToLocal(panelPosition);
                Vector2 screen = window.position.position + window.rootVisualElement.WorldToLocal(panelPosition);
                const float width = 240f; // SearchWindow's native default width; its anchor is the horizontal center.
                SearchWindow.Open(new SearchWindowContext(screen + Vector2.right * (width * 0.5f), width), search);
            }

            bool CanAddConnection() => window.Graph != null && window.Graph.nodes.Count < 512 && window.Graph.edges.Count < 4096;

            bool CurrentSource(ShaderGraph graph, PortData source, out ShaderGraphValueType type)
            {
                type = default;
                if (!ReferenceEquals(graph, window.Graph) || source == null || !window.Editable) return false;
                var node = graph.FindNode(source.nodeId);
                if (node == null || Array.IndexOf(ShaderGraphNodes.Outputs(node), source.name) < 0) return false;
                if (!outputPorts.TryGetValue(source.nodeId + ":" + source.name, out var port) || !port.enabledSelf) return false;
                type = window.OutputType(node, source.name);
                return true;
            }

            internal List<SearchTreeEntry> ConnectedSearchTree(ShaderGraph graph, PortData source)
            {
                var result = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Create Connected Node"), 0) };
                if (!CanAddConnection() || !CurrentSource(graph, source, out var type)) return result;
                var categories = new SortedDictionary<string, List<SearchTreeEntry>>();
                foreach (var kind in ShaderGraphNodes.Kinds)
                {
                    if (kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Reroute) continue;
                    var node = ShaderGraphNodes.Create(kind, default);
                    foreach (var input in ShaderGraphNodes.Inputs(node))
                    {
                        if (!Compatible(type, input.type ?? ShaderGraphValueType.Float, !input.type.HasValue)) continue;
                        string category = ShaderGraphNodes.Category(kind);
                        if (!categories.TryGetValue(category, out var list)) categories.Add(category, list = new List<SearchTreeEntry>());
                        list.Add(new SearchTreeEntry(new GUIContent(ShaderGraphNodes.Name(kind) + "(" + input.name + ")"))
                            { level = 2, userData = new ConnectedNodeChoice(kind, input.name) });
                    }
                }
                foreach (var category in categories)
                {
                    result.Add(new SearchTreeGroupEntry(new GUIContent(category.Key), 1));
                    result.AddRange(category.Value);
                }
                return result;
            }

            internal bool CreateConnectedNode(ShaderGraph graph, PortData source, ConnectedNodeChoice choice, Vector2 position)
            {
                if (!CanAddConnection() || !CurrentSource(graph, source, out var type)) return false;
                if (choice.kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Reroute) return false;
                var node = ShaderGraphNodes.Create(choice.kind, position);
                foreach (var input in ShaderGraphNodes.Inputs(node))
                {
                    if (input.name != choice.input || !Compatible(type, input.type ?? ShaderGraphValueType.Float, !input.type.HasValue)) continue;
                    window.Edit("Create Connected Shader Node", current =>
                    {
                        current.nodes.Add(node);
                        current.edges.Add(new ShaderGraphEdge { outputNode = source.nodeId, outputPort = source.name, inputNode = node.id, inputPort = input.name });
                        selectAfterRebuild = node.id;
                    });
                    return true;
                }
                return false;
            }
        }
    }
}
