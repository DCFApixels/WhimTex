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
        private sealed class PortData
        {
            internal string nodeId, name;
            internal ShaderGraphValueType type;
            internal bool dynamic;
        }

        private sealed partial class Canvas : GraphView, IDisposable
        {
            private readonly ShaderGraphWindow window;
            private readonly Dictionary<string, GraphNodeView> nodeViews = new Dictionary<string, GraphNodeView>();
            private readonly Dictionary<string, NodePreview> previews = new Dictionary<string, NodePreview>();
            private readonly Dictionary<string, Port> inputPorts = new Dictionary<string, Port>();
            private readonly Dictionary<string, Port> outputPorts = new Dictionary<string, Port>();
            private NodeSearch search;
            private string selectAfterRebuild;
            private bool rebuilding;
            internal Vector2 CenterPosition => contentViewContainer.WorldToLocal(worldBound.center);
            internal Canvas(ShaderGraphWindow window)
            {
                this.window = window;
                viewDataKey = "whimtex-shader-graph-" + window.layerId;
                AddToClassList("whimtex-shader-graph-canvas");
                focusable = true;
                SetupZoom(.08f, 3f);
                this.AddManipulator(new ContentDragger());
                this.AddManipulator(new SelectionDragger());
                portSnapper = new ConnectedPortSnapper(this);
                this.AddManipulator(portSnapper);
                this.AddManipulator(new RectangleSelector());
                var grid = new GridBackground(); Insert(0, grid); grid.StretchToParentSize();
                graphViewChanged = Changed;
                nodeCreationRequest = context => ShowSearch(context.screenMousePosition);
                viewTransformChanged = _ =>
                {
                    if (rebuilding) return;
                    window.viewPosition = viewTransform.position;
                    window.viewScale = viewTransform.scale;
                };
                RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
                RegisterCallback<DragPerformEvent>(OnDragPerform);
                RegisterCallback<MouseDownEvent>(e =>
                {
                    if (e.button != 0 || IsFieldControl(e.target as VisualElement)) return;
                    Focus();
                    if (e.clickCount != 2) return;
                    var edge = EdgeAt(e.target);
                    if (edge == null || !window.Editable) return;
                    InsertReroute(edge, e.mousePosition);
                    e.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                RegisterCallback<KeyDownEvent>(e =>
                {
                    if (IsFieldControl(e.target as VisualElement) || IsFieldControl(panel?.focusController?.focusedElement as VisualElement)) return;
                    if (e.keyCode == KeyCode.Delete)
                    {
                        if (window.Editable)
                            DeleteElements(selection.OfType<GraphElement>().Where(item => (item.capabilities & Capabilities.Deletable) != 0).ToArray());
                        e.StopImmediatePropagation(); return;
                    }
                    if (e.keyCode != KeyCode.Space) return;
                    ShowSearch(window.position.position + window.rootVisualElement.WorldToLocal(worldBound.center));
                    e.StopPropagation();
                }, TrickleDown.TrickleDown);
            }
            public void Dispose()
            {
                if (portSnapper != null) this.RemoveManipulator(portSnapper);
                foreach (var preview in previews.Values) preview.Dispose();
                previews.Clear();
                if (search != null) UnityEngine.Object.DestroyImmediate(search);
                search = null;
            }
            internal void ShowSearch(Vector2 screenPosition)
            {
                if (!window.Editable) return;
                if (search == null) search = ScriptableObject.CreateInstance<NodeSearch>();
                search.canvas = this;
                search.source = null;
                SearchWindow.Open(new SearchWindowContext(screenPosition), search);
            }
            public override void BuildContextualMenu(ContextualMenuPopulateEvent e)
            {
                base.BuildContextualMenu(e);
                var edge = EdgeAt(e.target);
                if (edge != null)
                {
                    Vector2 point = e.mousePosition;
                    e.menu.AppendAction("Add Reroute", _ => InsertReroute(edge, point),
                        _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    return;
                }
                if (e.target is not GraphNodeView && e.target is not Edge && e.target is not Port)
                {
                    Vector2 graphPosition = contentViewContainer.WorldToLocal(e.mousePosition);
                    foreach (var kind in ShaderGraphNodes.Kinds)
                    {
                        if (kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Reroute) continue;
                        var captured = kind;
                        e.menu.AppendAction("Add/" + ShaderGraphNodes.Category(kind) + "/" + ShaderGraphNodes.Name(kind),
                            _ => window.AddNode(captured, graphPosition), _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    }
                    if (window.Graph != null) foreach (var parameter in window.Graph.parameters)
                    {
                        string id = parameter.id;
                        e.menu.AppendAction("Add/Parameters/" + parameter.name,
                            _ => window.AddNode(ShaderGraphNodeKind.Parameter, graphPosition, id));
                    }
                }
            }
            internal void Rebuild()
            {
                var selected = new HashSet<string>();
                foreach (var element in selection) if (element is INodeView node) selected.Add(node.Id);
                if (selectAfterRebuild != null) { selected.Clear(); selected.Add(selectAfterRebuild); selectAfterRebuild = null; }
                rebuilding = true;
                try
                {
                    foreach (var preview in previews.Values) preview.RemoveFromHierarchy();
                    foreach (var id in previews.Keys.ToList())
                        if (window.Graph?.FindNode(id) is not ShaderGraphNode node || node.kind is ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.Output)
                        { previews[id].Dispose(); previews.Remove(id); }
                    foreach (var edge in edges.ToList()) { edge.input?.Disconnect(edge); edge.output?.Disconnect(edge); RemoveElement(edge); }
                    foreach (var node in nodes.ToList()) RemoveElement(node);
                    selection.Clear();
                    nodeViews.Clear(); inputPorts.Clear(); outputPorts.Clear();
                    if (window.Graph == null) return;
                    foreach (var data in window.Graph.nodes)
                    {
                        if (data == null) continue;
                        GraphNodeView view = data.kind switch
                        {
                            ShaderGraphNodeKind.Parameter => new ParameterView(window, this, data),
                            ShaderGraphNodeKind.Reroute => new RerouteView(window, this, data),
                            _ => new NodeView(window, this, data)
                        };
                        nodeViews[data.id] = view; AddElement(view);
                        if (selected.Contains(data.id))
                        {
                            // Rebuilding after Undo must not register a new selection Undo and clear Redo.
                            selection.Add(view); view.selected = true; view.OnSelected();
                            view.RegisterCallback<DetachFromPanelEvent>(_ => selection.Remove(view));
                        }
                    }
                    foreach (var data in window.Graph.edges)
                    {
                        if (data == null || !inputPorts.TryGetValue(data.inputNode + ":" + data.inputPort, out var input) ||
                            !outputPorts.TryGetValue(data.outputNode + ":" + data.outputPort, out var output)) continue;
                        var edge = output.ConnectTo(input); edge.userData = data; AddElement(edge);
                    }
                    UpdateViewTransform(window.viewPosition, window.viewScale == Vector3.zero ? Vector3.one : window.viewScale);
                }
                finally { rebuilding = false; }
            }
            internal void RegisterPort(Port port, bool input)
            {
                var data = (PortData)port.userData;
                (input ? inputPorts : outputPorts)[data.nodeId + ":" + data.name] = port;
            }
            private static Edge EdgeAt(IEventHandler target) => target is Edge edge ? edge : (target as VisualElement)?.GetFirstAncestorOfType<Edge>();
            private void InsertReroute(Edge edge, Vector2 panelPosition)
            {
                if (!window.Editable || edge.userData is not ShaderGraphEdge source || !window.Graph.edges.Contains(source)) return;
                if (window.Graph.nodes.Count >= 512 || window.Graph.edges.Count >= 4096)
                { window.ShowError("A Shader graph supports up to 512 nodes and 4096 edges."); return; }
                Vector2 position = contentViewContainer.WorldToLocal(panelPosition) - RerouteView.Size * .5f;
                window.Edit("Insert Shader Reroute", graph =>
                {
                    var node = ShaderGraphNodes.Create(ShaderGraphNodeKind.Reroute, position);
                    node.valueType = window.OutputType(graph.FindNode(source.outputNode), source.outputPort);
                    graph.nodes.Add(node); graph.edges.Remove(source);
                    graph.edges.Add(new ShaderGraphEdge { outputNode = source.outputNode, outputPort = source.outputPort, inputNode = node.id, inputPort = "In" });
                    graph.edges.Add(new ShaderGraphEdge { outputNode = node.id, inputNode = source.inputNode, inputPort = source.inputPort });
                    selectAfterRebuild = node.id;
                });
            }
            internal void RefreshValues()
            {
                foreach (INodeView view in nodeViews.Values) view.RefreshValues();
            }
            internal NodePreview GetPreview(ShaderGraphNode node)
            {
                if (!previews.TryGetValue(node.id, out var preview)) previews.Add(node.id, preview = new NodePreview(window, this, node.id));
                preview.RefreshOutputs();
                return preview;
            }
            internal void InvalidatePreviews() { foreach (var preview in previews.Values) preview.Invalidate(); }
            internal void TickPreviews()
            {
                ulong fingerprint = ShaderGraphCompiler.CodeFingerprint(window.Graph);
                bool compiled = false;
                foreach (var preview in previews.Values) compiled |= preview.Tick(fingerprint, !compiled);
            }
            internal void ClearErrors()
            {
                foreach (var view in nodeViews.Values) { view.RemoveFromClassList("whimtex-shader-graph-node--error"); view.tooltip = string.Empty; }
            }
            internal void ShowError(string id, string message)
            {
                if (id == null || !nodeViews.TryGetValue(id, out var view)) return;
                view.AddToClassList("whimtex-shader-graph-node--error"); view.tooltip = message;
            }
            private GraphViewChange Changed(GraphViewChange change)
            {
                if (rebuilding) return change;
                if (!window.Editable)
                {
                    change.edgesToCreate?.Clear(); change.elementsToRemove?.Clear();
                    window.rebuildRequested = true; return change;
                }
                change.elementsToRemove?.RemoveAll(e => e is INodeView n && window.Graph.FindNode(n.Id)?.kind == ShaderGraphNodeKind.Output);
                bool topology = change.edgesToCreate?.Count > 0 || change.elementsToRemove?.Count > 0;
                bool positions = change.movedElements?.Count > 0;
                if (!topology && !positions) return change;
                window.Edit(topology ? "Edit Shader Connections" : "Move Shader Nodes", graph =>
                {
                    var removed = new HashSet<string>();
                    if (change.elementsToRemove != null) foreach (var element in change.elementsToRemove)
                        if (element is INodeView node) removed.Add(node.Id);
                    var bypass = BypassReroutes(graph, removed);
                    if (change.elementsToRemove != null) foreach (var element in change.elementsToRemove)
                    {
                        if (element is INodeView node)
                        {
                            graph.nodes.RemoveAll(n => n.id == node.Id);
                            graph.edges.RemoveAll(e => e.inputNode == node.Id || e.outputNode == node.Id);
                        }
                        else if (element is Edge edge) RemoveEdge(graph, edge);
                    }
                    graph.edges.AddRange(bypass);
                    if (change.edgesToCreate != null) foreach (var edge in change.edgesToCreate)
                    {
                        if (edge.input?.userData is not PortData input || edge.output?.userData is not PortData output) continue;
                        graph.edges.RemoveAll(e => e.inputNode == input.nodeId && e.inputPort == input.name);
                        var data = new ShaderGraphEdge { inputNode = input.nodeId, inputPort = input.name, outputNode = output.nodeId, outputPort = output.name };
                        graph.edges.Add(data); edge.userData = data;
                    }
                    if (change.movedElements != null) foreach (var element in change.movedElements)
                        if (element is GraphNodeView view && view is INodeView data && graph.FindNode(data.Id) is ShaderGraphNode node) node.position = view.GetPosition().position;
                }, structural: topology, rebuild: topology, layoutOnly: !topology);
                return change;
            }
            private static List<ShaderGraphEdge> BypassReroutes(ShaderGraph graph, HashSet<string> removed)
            {
                var result = new List<ShaderGraphEdge>();
                foreach (var edge in graph.edges)
                {
                    if (removed.Contains(edge.inputNode) || !removed.Contains(edge.outputNode)) continue;
                    var source = edge;
                    int remaining = graph.nodes.Count;
                    while (source != null && removed.Contains(source.outputNode) && remaining-- > 0)
                    {
                        if (graph.FindNode(source.outputNode)?.kind != ShaderGraphNodeKind.Reroute) { source = null; break; }
                        string id = source.outputNode;
                        source = graph.edges.Find(e => e.inputNode == id && e.inputPort == "In");
                    }
                    if (source != null && !removed.Contains(source.outputNode))
                        result.Add(new ShaderGraphEdge { outputNode = source.outputNode, outputPort = source.outputPort, inputNode = edge.inputNode, inputPort = edge.inputPort });
                }
                return result;
            }
            private static void RemoveEdge(ShaderGraph graph, Edge edge)
            {
                if (edge.input?.userData is not PortData input || edge.output?.userData is not PortData output) return;
                graph.edges.RemoveAll(e => e.inputNode == input.nodeId && e.inputPort == input.name && e.outputNode == output.nodeId && e.outputPort == output.name);
            }
            public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
            {
                var result = new List<Port>();
                foreach (var port in ports)
                {
                    if (port.direction == startPort.direction || port.node == startPort.node || !port.enabledSelf) continue;
                    var input = (PortData)(port.direction == Direction.Input ? port : startPort).userData;
                    var output = (PortData)(port.direction == Direction.Output ? port : startPort).userData;
                    if (!Accepts(output.type, input) || WouldCycle(output.nodeId, input.nodeId)) continue;
                    result.Add(port);
                }
                return result;
            }
            private bool Accepts(ShaderGraphValueType type, PortData input, HashSet<string> visited = null)
            {
                if (window.Graph.FindNode(input.nodeId)?.kind != ShaderGraphNodeKind.Reroute)
                    return Compatible(type, input.type, input.dynamic);
                visited ??= new HashSet<string>();
                if (!visited.Add(input.nodeId)) return true;
                foreach (var edge in window.Graph.edges)
                    if (edge.outputNode == input.nodeId && inputPorts.TryGetValue(edge.inputNode + ":" + edge.inputPort, out var port) &&
                        !Accepts(type, (PortData)port.userData, visited)) return false;
                return true;
            }
            private bool WouldCycle(string output, string input)
            {
                var stack = new Stack<string>(); var visited = new HashSet<string>(); stack.Push(input);
                while (stack.Count > 0)
                {
                    string id = stack.Pop(); if (id == output) return true;
                    if (!visited.Add(id)) continue;
                    foreach (var edge in window.Graph.edges) if (edge.outputNode == id) stack.Push(edge.inputNode);
                }
                return false;
            }
            private void OnDragUpdated(DragUpdatedEvent e)
            {
                if (DragAndDrop.GetGenericData(ParameterDragKey) is not ParameterDragData data || data.window != window || !window.Editable) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation();
            }
            private void OnDragPerform(DragPerformEvent e)
            {
                if (DragAndDrop.GetGenericData(ParameterDragKey) is not ParameterDragData data || data.window != window || !window.Editable) return;
                DragAndDrop.AcceptDrag();
                window.AddNode(ShaderGraphNodeKind.Parameter, contentViewContainer.WorldToLocal(e.mousePosition), data.id);
                DragAndDrop.SetGenericData(ParameterDragKey, null); e.StopPropagation();
            }

            private sealed class NodeSearch : ScriptableObject, ISearchWindowProvider
            {
                internal Canvas canvas;
                internal PortData source;
                internal ShaderGraph sourceGraph;
                internal Vector2 dropPosition;
                public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
                {
                    if (source != null) return canvas.ConnectedSearchTree(sourceGraph, source);
                    var result = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Create Node"), 0) };
                    var categories = new SortedDictionary<string, List<ShaderGraphNodeKind>>();
                    foreach (var kind in ShaderGraphNodes.Kinds)
                    {
                        if (kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Reroute) continue;
                        string category = ShaderGraphNodes.Category(kind);
                        if (!categories.TryGetValue(category, out var list)) categories.Add(category, list = new List<ShaderGraphNodeKind>());
                        list.Add(kind);
                    }
                    foreach (var category in categories)
                    {
                        result.Add(new SearchTreeGroupEntry(new GUIContent(category.Key), 1));
                        foreach (var kind in category.Value)
                            result.Add(new SearchTreeEntry(new GUIContent(ShaderGraphNodes.Name(kind))) { level = 2, userData = kind });
                    }
                    if (canvas.window.Graph.parameters.Count > 0)
                    {
                        result.Add(new SearchTreeGroupEntry(new GUIContent("Parameters"), 1));
                        foreach (var parameter in canvas.window.Graph.parameters)
                            result.Add(new SearchTreeEntry(new GUIContent(parameter.name)) { level = 2, userData = parameter.id });
                    }
                    return result;
                }
                public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
                {
                    if (source != null) return entry.userData is ConnectedNodeChoice choice && canvas.CreateConnectedNode(sourceGraph, source, choice, dropPosition);
                    Vector2 local = context.screenMousePosition - canvas.window.position.position;
                    Vector2 point = canvas.window.rootVisualElement.ChangeCoordinatesTo(canvas.contentViewContainer, local);
                    if (entry.userData is ShaderGraphNodeKind kind) canvas.window.AddNode(kind, point);
                    else if (entry.userData is string parameter) canvas.window.AddNode(ShaderGraphNodeKind.Parameter, point, parameter);
                    else return false;
                    return true;
                }
            }
        }

        private static bool Compatible(ShaderGraphValueType output, ShaderGraphValueType input, bool dynamic) =>
            dynamic ? ShaderGraphNodes.IsNumeric(output) : output == input || ShaderGraphNodes.IsNumeric(output) && ShaderGraphNodes.IsNumeric(input);

        private ShaderGraphValueType OutputType(ShaderGraphNode node, string port, HashSet<string> visiting = null)
        {
            visiting ??= new HashSet<string>();
            if (node == null || !visiting.Add(node.id)) return ShaderGraphValueType.Float;
            try
            {
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.Reroute: return InputType(node, "In", visiting);
                    case ShaderGraphNodeKind.Parameter: return Graph.FindParameter(node.parameterId) is ShaderFXParameter p ? ShaderGraphNodes.ParameterType(p) : ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.Constant: case ShaderGraphNodeKind.Combine: return node.valueType;
                    case ShaderGraphNodeKind.UV: case ShaderGraphNodeKind.ImageSize: case ShaderGraphNodeKind.TextureSize: case ShaderGraphNodeKind.TransformUV: return ShaderGraphValueType.Float2;
                    case ShaderGraphNodeKind.SampleTexture: case ShaderGraphNodeKind.SampleGradient: case ShaderGraphNodeKind.Blend: case ShaderGraphNodeKind.Output: return ShaderGraphValueType.Color;
                    case ShaderGraphNodeKind.Split: case ShaderGraphNodeKind.Length: case ShaderGraphNodeKind.Distance: case ShaderGraphNodeKind.Dot:
                    case ShaderGraphNodeKind.SampleCurve: case ShaderGraphNodeKind.Noise: case ShaderGraphNodeKind.Shape: return ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.Compare: return ShaderGraphValueType.Boolean;
                }
                ShaderGraphValueType result = ShaderGraphValueType.Float;
                foreach (var input in ShaderGraphNodes.Inputs(node))
                {
                    if (input.type.HasValue) continue;
                    var type = ShaderGraphNodes.NumericType(InputType(node, input.name, visiting));
                    if (ShaderGraphNodes.IsNumeric(type) && type > result) result = type;
                }
                return result;
            }
            finally { visiting.Remove(node.id); }
        }
        private ShaderGraphValueType InputType(ShaderGraphNode node, string name, HashSet<string> visiting = null)
        {
            foreach (var edge in Graph.edges)
                if (edge.inputNode == node.id && edge.inputPort == name) return OutputType(Graph.FindNode(edge.outputNode), edge.outputPort, visiting);
            return node.FindInput(name)?.type ?? (node.kind == ShaderGraphNodeKind.Reroute ? node.valueType : ShaderGraphValueType.Float);
        }
    }
}
