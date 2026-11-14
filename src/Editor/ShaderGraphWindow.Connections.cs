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
            bool hovered;
            internal GraphPort(Direction direction, Type type)
                : base(Orientation.Horizontal, direction, direction == Direction.Input ? Capacity.Single : Capacity.Multi, type)
            {
                m_EdgeConnector = new EdgeConnector<GraphEdge>(listener);
                this.AddManipulator(m_EdgeConnector);
                RegisterCallback<PointerEnterEvent>(e => SetHovered(ContainsPoint(this.WorldToLocal(e.position))));
                RegisterCallback<PointerMoveEvent>(e => SetHovered(ContainsPoint(this.WorldToLocal(e.position))));
                RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
                RegisterCallback<DetachFromPanelEvent>(_ => SetHovered(false));
                m_ConnectorBox.generateVisualContent += DrawHoverOutline;
            }
            public override void Connect(Edge edge)
            {
                base.Connect(edge);
                m_ConnectorBox.MarkDirtyRepaint();
            }
            public override void Disconnect(Edge edge)
            {
                base.Disconnect(edge);
                m_ConnectorBox.MarkDirtyRepaint();
            }
            public override void DisconnectAll()
            {
                base.DisconnectAll();
                m_ConnectorBox.MarkDirtyRepaint();
            }
            void SetHovered(bool value)
            {
                if (hovered == value) return;
                hovered = value;
                m_ConnectorBox.MarkDirtyRepaint();
            }
            void DrawHoverOutline(MeshGenerationContext context)
            {
                if (!hovered || !connected) return;
                var rect = new Rect(Vector2.zero, m_ConnectorBox.layout.size);
                if (rect.width <= 0 || rect.height <= 0) return;
                var painter = context.painter2D;
                painter.strokeColor = m_ConnectorBox.resolvedStyle.borderLeftColor;
                // Half the stroke overlaps the native border, half extends outside it.
                painter.lineWidth = 2;
                painter.BeginPath();
                if (m_ConnectorBox.resolvedStyle.borderTopLeftRadius > 0)
                    painter.Arc(rect.center, Mathf.Min(rect.width, rect.height) * .5f, 0, 360);
                else
                {
                    painter.MoveTo(rect.min);
                    painter.LineTo(new Vector2(rect.xMax, rect.yMin));
                    painter.LineTo(rect.max);
                    painter.LineTo(new Vector2(rect.xMin, rect.yMax));
                }
                painter.ClosePath();
                painter.Stroke();
            }
            public override bool ContainsPoint(Vector2 localPoint)
            {
                if (base.ContainsPoint(localPoint)) return true;
                var connector = m_ConnectorBox.layout;
                if (connector.xMin >= 0 && connector.xMax <= layout.width)
                    return m_ConnectorBox.ContainsPoint(this.ChangeCoordinatesTo(m_ConnectorBox, localPoint));
                var hit = m_ConnectorBox.ChangeCoordinatesTo(this, new Rect(Vector2.zero, connector.size));
                hit.xMin -= 4; hit.xMax += 4;
                hit.yMin = 0; hit.yMax = layout.height;
                return hit.Contains(localPoint);
            }
        }

        private sealed class ConnectionListener : IEdgeConnectorListener
        {
            public void OnDropOutsidePort(Edge edge, Vector2 position)
            {
                var dragged = edge.output?.edgeConnector.edgeDragHelper.draggedPort ?? edge.input?.edgeConnector.edgeDragHelper.draggedPort;
                if (dragged == null || !dragged.enabledInHierarchy || dragged.userData is not PortData source) return;
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
            internal readonly string port, parameterId;
            internal readonly ShaderGraphFunctionCatalog.Entry function;
            internal readonly ShaderGraphSubGraph subGraph;
            internal readonly bool heightValue;
            internal readonly ShaderGraphValueType valueType;
            internal ConnectedNodeChoice(ShaderGraphNodeKind kind, string port, ShaderGraphFunctionCatalog.Entry function = null,
                string parameterId = null, ShaderGraphSubGraph subGraph = null, bool heightValue = false, ShaderGraphValueType valueType = ShaderGraphValueType.Float)
            { this.kind = kind; this.port = port; this.function = function; this.parameterId = parameterId; this.subGraph = subGraph; this.heightValue = heightValue; this.valueType = valueType; }
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
                    if (hit is LayerPreviewOverlay || hit.GetFirstAncestorOfType<LayerPreviewOverlay>() != null) return false;
                    if (hit is GraphNodeView || hit.GetFirstAncestorOfType<GraphNodeView>() != null) return false;
                    var edge = EdgeAt(hit);
                    if (edge?.userData is ShaderGraphEdge) return false;
                }
                return true;
            }

            internal void ShowConnectedSearch(PortData source, Vector2 panelPosition)
            {
                if (!window.Editable || !CanAddConnection(source)) return;
                if (search == null) search = ScriptableObject.CreateInstance<NodeSearch>();
                search.canvas = this; search.sourceGraph = window.Graph;
                search.source = source;
                search.replacement = null;
                search.dropPosition = contentViewContainer.WorldToLocal(panelPosition);
                Vector2 screen = window.position.position + window.rootVisualElement.WorldToLocal(panelPosition);
                const float width = WhimTexSearchDropdown.DefaultWidth; // Anchor is the horizontal center.
                search.Open(screen + Vector2.right * (width * 0.5f));
            }

            bool CanAddConnection(PortData source) => window.Graph != null && window.Graph.nodes.Count < 512 &&
                (window.Graph.edges.Count < 4096 || source?.direction == Direction.Input &&
                    window.Graph.edges.Exists(e => e.inputNode == source.nodeId && e.inputPort == source.name));

            bool CurrentSource(ShaderGraph graph, PortData source, out PortData current)
            {
                current = null;
                if (!ReferenceEquals(graph, window.Graph) || source == null || !window.Editable) return false;
                var node = graph.FindNode(source.nodeId);
                if (node == null) return false;
                if (source.direction == Direction.Output)
                {
                    if (Array.IndexOf(ShaderGraphNodes.Outputs(node), source.name) < 0) return false;
                }
                else if (!ShaderGraphNodes.Inputs(node).Any(p => p.name == source.name) ||
                    ShaderGraphNodes.InputInactiveReason(node, source.name) != null) return false;
                var ports = source.direction == Direction.Input ? inputPorts : outputPorts;
                if (!ports.TryGetValue(source.nodeId + ":" + source.name, out var port) || !port.enabledInHierarchy) return false;
                current = (PortData)port.userData;
                return true;
            }

            IEnumerable<string> ConnectingPorts(ShaderGraphNode node, PortData source)
            {
                if (source.direction == Direction.Output)
                {
                    var type = window.OutputType(window.Graph.FindNode(source.nodeId), source.name);
                    foreach (var input in ShaderGraphNodes.Inputs(node))
                        if (!NodeView.IsOptionInput(node, input.name) && ShaderGraphNodes.InputInactiveReason(node, input.name) == null &&
                            Compatible(type, input.type ?? ShaderGraphValueType.Float, !input.type.HasValue, node.kind)) yield return input.name;
                }
                else foreach (string output in ShaderGraphNodes.Outputs(node))
                {
                    if (node.kind == ShaderGraphNodeKind.Split && "XYZW".IndexOf(output, StringComparison.Ordinal) >=
                        ShaderGraphNodes.Components(window.InputType(node, "In"))) continue;
                    if (Accepts(node.function?.OutputType(output) ?? window.OutputType(node, output), source)) yield return output;
                }
            }

            internal List<SearchTreeEntry> ConnectedSearchTree(ShaderGraph graph, PortData source)
            {
                var result = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Create Connected Node"), 0) };
                if (!CanAddConnection(source) || !CurrentSource(graph, source, out var current)) return result;
                var categories = new SortedDictionary<string, List<SearchTreeEntry>>();
                var sourceNode = graph.FindNode(current.nodeId);
                if (window.svmNodesOnly && current.direction == Direction.Output &&
                    ShaderGraphNodes.MatrixSize(window.OutputType(sourceNode, current.name)) != 0) return result;
                if (current.direction == Direction.Input)
                {
                    var type = ValueTypeForDrop(current);
                    if ((!window.svmNodesOnly || ShaderGraphNodes.MatrixSize(type) == 0) && Accepts(type, current))
                    {
                        string category = ShaderGraphNodes.Category(ShaderGraphNodeKind.Value);
                        categories.Add(category, new List<SearchTreeEntry>
                        {
                            new SearchTreeEntry(new GUIContent(NodeView.PortLabel("Value", type)))
                            { userData = new ConnectedNodeChoice(ShaderGraphNodeKind.Value, "Out", valueType: type) }
                        });
                    }
                }
                foreach (var kind in ShaderGraphNodes.Kinds)
                {
                    if (kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Value or ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.SubGraph || ShaderGraphNodes.IsSubGraphInterface(kind)) continue;
                    if (window.svmNodesOnly && !ShaderGraphPreviewVM.SupportsDefaultNode(kind)) continue;
                    var node = ShaderGraphNodes.Create(kind, default);
                    if (kind == ShaderGraphNodeKind.MatrixTranspose && current.direction == Direction.Input)
                        node.valueType = ValueTypeForDrop(current);
                    bool heightValue = ShaderGraphNodes.UsesHeightTexture(kind) && current.direction == Direction.Output && ShaderGraphNodes.IsNumeric(current.type);
                    if (heightValue) node.heightSource = ShaderGraphHeightSource.Value;
                    foreach (string port in ConnectingPorts(node, current))
                    {
                        if (window.svmNodesOnly && current.direction == Direction.Input && !ShaderGraphPreviewVM.SupportsDefaultNode(kind, port)) continue;
                        string category = ShaderGraphNodes.Category(kind);
                        if (!categories.TryGetValue(category, out var list)) categories.Add(category, list = new List<SearchTreeEntry>());
                        list.Add(new SearchTreeEntry(new GUIContent(ShaderGraphNodes.Name(kind) + "(" + port + ")"))
                            { level = 2, userData = new ConnectedNodeChoice(kind, port, heightValue: heightValue) });
                    }
                }
                if (current.direction == Direction.Input)
                    foreach (var parameter in graph.parameters)
                    {
                        if (!Accepts(ShaderGraphNodes.ParameterType(parameter), current)) continue;
                        if (window.svmNodesOnly && !ShaderGraphPreviewVM.SupportsParameter(parameter)) continue;
                        if (!categories.TryGetValue("Parameters", out var list)) categories.Add("Parameters", list = new List<SearchTreeEntry>());
                        list.Add(new SearchTreeEntry(new GUIContent(parameter.name + "(Out)"))
                            { userData = new ConnectedNodeChoice(ShaderGraphNodeKind.Parameter, "Out", parameterId: parameter.id) });
                    }
                string previousCategory = "";
                foreach (var category in categories)
                {
                    int level = AddSearchCategory(result, category.Key, ref previousCategory);
                    foreach (var entry in category.Value) { entry.level = level + 1; result.Add(entry); }
                }
                if (!window.svmNodesOnly)
                {
                    if (current.direction == Direction.Output) AddFunctionSearchEntries(result, window.OutputType(graph.FindNode(current.nodeId), current.name));
                    else AddFunctionSearchEntries(result, null, type => Accepts(type, current));
                }
                bool subHeading = false;
                foreach (var definition in graph.subGraphs)
                {
                    if (window.WouldRecurse(definition.id)) continue;
                    var node = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraph, subGraphId = definition.id, subGraph = definition };
                    foreach (string port in ConnectingPorts(node, current))
                    {
                        if (window.svmNodesOnly && !ShaderGraphPreviewVM.SupportsSubGraph(graph, definition, current.direction == Direction.Input ? port : null)) continue;
                        if (!subHeading) { result.Add(new SearchTreeGroupEntry(new GUIContent("Sub Graphs"), 1)); subHeading = true; }
                        result.Add(new SearchTreeEntry(new GUIContent(definition.name + "(" + ShaderGraphNodes.PortDisplayName(node, port, current.direction == Direction.Input) + ")"))
                            { level = 2, userData = new ConnectedNodeChoice(ShaderGraphNodeKind.SubGraph, port, subGraph: definition) });
                    }
                }
                return result;
            }

            internal bool CreateConnectedNode(ShaderGraph graph, PortData source, ConnectedNodeChoice choice, Vector2 position)
            {
                if (!CanAddConnection(source) || !CurrentSource(graph, source, out var currentPort)) return false;
                if (choice.kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Reroute) return false;
                if (choice.kind == ShaderGraphNodeKind.Parameter &&
                    (currentPort.direction != Direction.Input || graph.FindParameter(choice.parameterId) == null)) return false;
                var node = ShaderGraphNodes.Create(choice.kind, position);
                if (choice.kind == ShaderGraphNodeKind.Value)
                {
                    if (currentPort.direction != Direction.Input || ValueTypeForDrop(currentPort) != choice.valueType) return false;
                    SetValueFromPort(node, choice.valueType);
                }
                if (choice.kind == ShaderGraphNodeKind.MatrixTranspose && currentPort.direction == Direction.Input)
                    node.valueType = ValueTypeForDrop(currentPort);
                if (ShaderGraphLoops.IsEndpoint(node.kind) && (graph.nodes.Count > 510 || graph.edges.Count > 4094)) return false;
                node.parameterId = choice.parameterId;
                if (choice.subGraph != null)
                {
                    if (window.WouldRecurse(choice.subGraph.id)) return false;
                    node.subGraphId = choice.subGraph.id; node.subGraph = graph.FindSubGraph(choice.subGraph.id);
                }
                if (choice.heightValue) node.heightSource = ShaderGraphHeightSource.Value;
                ShaderGraph staged = null;
                if (choice.function != null)
                {
                    try
                    {
                        staged = new ShaderGraph { sources = new List<ShaderGraphSource>(graph.sources) };
                        var function = choice.function.Import(staged);
                        CheckFunction(function, staged.sources, window.Target.IsEffect);
                        ShaderGraphNodes.SetFunction(node, function);
                    }
                    catch (Exception error) { window.ShowError(error.Message); return false; }
                }
                foreach (string port in ConnectingPorts(node, currentPort))
                {
                    if (port != choice.port) continue;
                    window.Edit("Create Connected Shader Node", current =>
                    {
                        current.nodes.Add(node);
                        ShaderGraphLoops.AddPartner(current, node);
                        if (node.function != null) current.functions.Add(node.function);
                        if (staged != null) current.sources = staged.sources;
                        if (currentPort.direction == Direction.Input)
                        {
                            current.edges.RemoveAll(e => e.inputNode == currentPort.nodeId && e.inputPort == currentPort.name);
                            current.edges.Add(new ShaderGraphEdge { outputNode = node.id, outputPort = port, inputNode = currentPort.nodeId, inputPort = currentPort.name });
                        }
                        else
                        {
                            current.edges.RemoveAll(e => e.inputNode == node.id && e.inputPort == port);
                            current.edges.Add(new ShaderGraphEdge { outputNode = currentPort.nodeId, outputPort = currentPort.name, inputNode = node.id, inputPort = port });
                        }
                        selectAfterRebuild = node.id;
                    });
                    return true;
                }
                return false;
            }

            internal void PruneValueConnections(ShaderGraph graph, ShaderGraphNode value)
            {
                var type = ShaderGraphNodes.ValueOutputType(value);
                graph.edges.RemoveAll(edge => edge.outputNode == value.id &&
                    inputPorts.TryGetValue(edge.inputNode + ":" + edge.inputPort, out var port) &&
                    !Accepts(type, (PortData)port.userData));
            }

            ShaderGraphValueType ValueTypeForDrop(PortData input)
            {
                if (window.Graph.FindNode(input.nodeId)?.kind != ShaderGraphNodeKind.Reroute ||
                    Accepts(input.type, input)) return input.type;
                foreach (ShaderGraphValueType type in Enum.GetValues(typeof(ShaderGraphValueType)))
                    if (Accepts(type, input)) return type;
                return input.type;
            }

            static void SetValueFromPort(ShaderGraphNode node, ShaderGraphValueType type)
            {
                if (ShaderGraphNodes.MatrixSize(type) != 0)
                {
                    node.valueType = type;
                    node.literal = null;
                    return;
                }
                if (type == ShaderGraphValueType.Float) return;
                ShaderGraphNodes.SetValueKind(node, type switch
                {
                    ShaderGraphValueType.Float2 => ShaderParameterType.Vector2,
                    ShaderGraphValueType.Float3 => ShaderParameterType.Vector3,
                    ShaderGraphValueType.Float4 => ShaderParameterType.Vector,
                    ShaderGraphValueType.Boolean => ShaderParameterType.Toggle,
                    ShaderGraphValueType.Texture => ShaderParameterType.Texture2D,
                    ShaderGraphValueType.Gradient => ShaderParameterType.Gradient,
                    ShaderGraphValueType.Curve => ShaderParameterType.Curve,
                    ShaderGraphValueType.Color => ShaderParameterType.Color,
                    ShaderGraphValueType.Transform2D => ShaderParameterType.Transform2D,
                    _ => throw new ArgumentOutOfRangeException(nameof(type))
                });
            }
        }
    }
}
