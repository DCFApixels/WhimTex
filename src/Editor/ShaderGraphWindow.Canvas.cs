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
            internal Direction direction;
            internal ShaderGraphValueType type;
            internal bool dynamic;
        }

        private sealed partial class Canvas : GraphView, IDisposable
        {
            internal readonly ShaderGraphWindow window;
            private readonly Dictionary<string, GraphNodeView> nodeViews = new Dictionary<string, GraphNodeView>();
            private readonly Dictionary<string, NodePreview> previews = new Dictionary<string, NodePreview>();
            private readonly List<NodePreview> previewQueue = new List<NodePreview>();
            private readonly ShaderGraphPreviewState previewState = new ShaderGraphPreviewState();
            private bool previewsInvalidated = true;
            private int previewCursor;
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
                SetupAnnotations();
                serializeGraphElements = CopyNodes;
                canPasteSerializedData = CanPasteNodes;
                unserializeAndPaste = PasteNodes;
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
                    if (HandleClipboardShortcut(e)) return;
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
                previewQueue.Clear();
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
                AnnotationMenu(e);
                SubGraphMenu(e);
                var edge = EdgeAt(e.target);
                if (edge != null)
                {
                    Vector2 point = e.mousePosition;
                    e.menu.AppendAction("Add Reroute", _ => InsertReroute(edge, point),
                        _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }
            }
            internal void Rebuild()
            {
                var selected = new HashSet<string>();
                foreach (var element in selection) if (element is IGraphItemView node) selected.Add(node.Id);
                if (selectAfterRebuild != null) { selected.Clear(); selected.Add(selectAfterRebuild); selectAfterRebuild = null; }
                rebuilding = true;
                try
                {
                    ClearAnnotations();
                    foreach (var preview in previews.Values) preview.RemoveFromHierarchy();
                    foreach (var id in previews.Keys.ToList())
                        if (!NodePreview.Supports(window, window.Graph?.FindNode(id)))
                        { previewQueue.Remove(previews[id]); previews[id].Dispose(); previews.Remove(id); }
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
                        if (selected.Contains(data.id) && !selection.Contains(view))
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
                    BuildAnnotations(selected);
                    RebuildBadges();
                    UpdateViewTransform(window.viewPosition, window.viewScale == Vector3.zero ? Vector3.one : window.viewScale);
                }
                finally { rebuilding = false; }
            }
            internal void HighlightParameter(string id)
            {
                foreach (var view in nodeViews.Values)
                    if (view is ParameterView parameter)
                        parameter.EnableInClassList("whimtex-shader-graph-parameter-highlight", id != null && parameter.ParameterId == id);
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
                RefreshAnnotations();
            }
            internal NodePreview GetPreview(ShaderGraphNode node)
            {
                if (!previews.TryGetValue(node.id, out var preview))
                { previews.Add(node.id, preview = new NodePreview(window, this, node.id)); previewQueue.Add(preview); }
                previewsInvalidated = true;
                preview.RefreshOutputs();
                return preview;
            }
            internal void InvalidatePreviews() { previewsInvalidated = true; foreach (var preview in previews.Values) preview.Invalidate(); }
            internal void TickPreviews()
            {
                if (previews.Count == 0) return;
                using var profile = WhimTexProfiler.Measure("shadergraph.preview.tick", window.document);
                if (previewsInvalidated || !ReferenceEquals(previewState.bindings.Graph, window.Graph))
                { previewState.Prepare(window.Graph); previewsInvalidated = false; }
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                int rendered = 0;
                for (int i = 0; i < previewQueue.Count; i++)
                {
                    if (previewCursor >= previewQueue.Count) previewCursor = 0;
                    var preview = previewQueue[previewCursor++];
                    if (preview.Tick(previewState)) rendered++;
                    // A native call cannot be interrupted; do not start another one after the budget.
                    if (rendered >= 8 || (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= 4) break;
                }
            }
            private GraphViewChange Changed(GraphViewChange change)
            {
                if (rebuilding) return change;
                if (!window.Editable)
                {
                    change.edgesToCreate?.Clear(); change.elementsToRemove?.Clear();
                    window.rebuildRequested = true; return change;
                }
                change.edgesToCreate?.RemoveAll(e => e.input == null || e.output == null || !e.input.enabledInHierarchy || !e.output.enabledInHierarchy);
                change.elementsToRemove?.RemoveAll(e => e is INodeView n && window.Graph.FindNode(n.Id)?.kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.SubGraphOutput);
                bool topology = change.edgesToCreate?.Count > 0 || change.elementsToRemove?.Any(e => e is INodeView || e is Edge) == true;
                bool annotations = change.elementsToRemove?.Any(e => e is NoteView || e is GroupView) == true;
                bool positions = change.movedElements?.Count > 0;
                if (!topology && !positions && !annotations) return change;
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
                        else if (element is NoteView note) graph.notes.RemoveAll(n => n.id == note.Id);
                        else if (element is GroupView group) graph.groups.RemoveAll(g => g.id == group.Id);
                    }
                    graph.edges.AddRange(bypass);
                    if (change.edgesToCreate != null) foreach (var edge in change.edgesToCreate)
                    {
                        if (edge.input?.userData is not PortData input || edge.output?.userData is not PortData output) continue;
                        graph.edges.RemoveAll(e => e.inputNode == input.nodeId && e.inputPort == input.name);
                        var data = new ShaderGraphEdge { inputNode = input.nodeId, inputPort = input.name, outputNode = output.nodeId, outputPort = output.name };
                        graph.edges.Add(data); edge.userData = data;
                    }
                    if (positions) StorePositions(graph);
                    if (annotations) graph.PruneGroups();
                }, structural: topology, rebuild: topology || annotations, layoutOnly: !topology);
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
                if (!startPort.enabledInHierarchy) return result;
                foreach (var port in ports)
                {
                    if (port.direction == startPort.direction || port.node == startPort.node || !port.enabledInHierarchy) continue;
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
                if (!window.ReadParameterDrag(out _)) return;
                window.HideParameterInsertion();
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation();
            }
            private void OnDragPerform(DragPerformEvent e)
            {
                if (!window.ReadParameterDrag(out var data)) return;
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
                        if (kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.SubGraph || ShaderGraphNodes.IsSubGraphInterface(kind)) continue;
                        string category = ShaderGraphNodes.Category(kind);
                        if (!categories.TryGetValue(category, out var list)) categories.Add(category, list = new List<ShaderGraphNodeKind>());
                        list.Add(kind);
                    }
                    string previousCategory = "";
                    foreach (var category in categories)
                    {
                        int level = AddSearchCategory(result, category.Key, ref previousCategory);
                        foreach (var kind in category.Value)
                            result.Add(new SearchTreeEntry(new GUIContent(ShaderGraphNodes.Name(kind))) { level = level + 1, userData = kind });
                    }
                    if (canvas.window.Graph.parameters.Count > 0)
                    {
                        result.Add(new SearchTreeGroupEntry(new GUIContent("Parameters"), 1));
                        foreach (var parameter in canvas.window.Graph.parameters)
                            result.Add(new SearchTreeEntry(new GUIContent(parameter.name)) { level = 2, userData = parameter.id });
                    }
                    AddFunctionSearchEntries(result, null);
                    var definitions = canvas.window.Graph.subGraphs.Where(d => !canvas.window.WouldRecurse(d.id)).ToList();
                    if (definitions.Count != 0)
                    {
                        result.Add(new SearchTreeGroupEntry(new GUIContent("Sub Graphs"), 1));
                        foreach (var definition in definitions) result.Add(new SearchTreeEntry(new GUIContent(definition.name)) { level = 2, userData = definition });
                    }
                    return result;
                }
                public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
                {
                    if (source != null) return entry.userData is ConnectedNodeChoice choice && canvas.CreateConnectedNode(sourceGraph, source, choice, dropPosition);
                    Vector2 local = context.screenMousePosition - canvas.window.position.position;
                    Vector2 point = canvas.window.rootVisualElement.ChangeCoordinatesTo(canvas.contentViewContainer, local);
                    if (entry.userData is ShaderGraphNodeKind kind) canvas.window.AddNode(kind, point);
                    else if (entry.userData is ShaderGraphSubGraph definition) canvas.window.AddSubGraph(definition, point);
                    else if (entry.userData is ShaderGraphFunctionCatalog.Entry function) canvas.window.AddFunctionNode(function, point);
                    else if (entry.userData is string parameter) canvas.window.AddNode(ShaderGraphNodeKind.Parameter, point, parameter);
                    else return false;
                    return true;
                }
            }
        }


        private static int AddSearchCategory(List<SearchTreeEntry> tree, string category, ref string previous)
        {
            var parts = category.Split('/'); var before = previous.Split('/');
            bool same = true;
            for (int i = 0; i < parts.Length; i++)
            {
                same &= i < before.Length && parts[i] == before[i];
                if (!same) tree.Add(new SearchTreeGroupEntry(new GUIContent(parts[i]), i + 1));
            }
            previous = category;
            return parts.Length;
        }

        private static bool Compatible(ShaderGraphValueType output, ShaderGraphValueType input, bool dynamic) =>
            dynamic ? ShaderGraphNodes.IsNumeric(output) : output == input || ShaderGraphNodes.IsNumeric(output) && ShaderGraphNodes.IsNumeric(input);

        private ShaderGraphValueType OutputType(ShaderGraphNode node, string port, HashSet<string> visiting = null)
        {
            visiting ??= new HashSet<string>();
            if (node == null || !visiting.Add(node.id)) return ShaderGraphValueType.Float;
            if (ShaderGraphNodes.TrySurfaceOutputType(node, port, out var surfaceType)) { visiting.Remove(node.id); return surfaceType; }
            if (ShaderGraphNodes.TryProceduralOutputType(node.kind, out var fixedType) || ShaderGraphNodes.TryColorOutputType(node.kind, out fixedType) || ShaderGraphNodes.TryExtendedOutputType(node.kind, out fixedType)) { visiting.Remove(node.id); return fixedType; }
            try
            {
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.SubGraph: return node.subGraph?.outputs.Find(p => p.id == port)?.type ?? ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.SubGraphInput: return node.interfacePort?.type ?? ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.CustomFunction: return Graph.FindFunction(node.functionId)?.OutputType(port) ?? ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.Reroute: return InputType(node, "In", visiting);
                    case ShaderGraphNodeKind.Parameter: return Graph.FindParameter(node.parameterId) is ShaderFXParameter p ? ShaderGraphNodes.ParameterType(p) : ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.Constant:
                    case ShaderGraphNodeKind.InstanceIndex:
                    case ShaderGraphNodeKind.Time: return ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.Combine: return node.valueType;
                    case ShaderGraphNodeKind.Swizzle: return ShaderGraphNodes.SwizzleType(node.swizzle);
                    case ShaderGraphNodeKind.UV: case ShaderGraphNodeKind.ImageSize: case ShaderGraphNodeKind.TextureSize: case ShaderGraphNodeKind.TransformUV: return ShaderGraphValueType.Float2;
                    case ShaderGraphNodeKind.SampleTexture: case ShaderGraphNodeKind.SampleGradient: case ShaderGraphNodeKind.Blend: case ShaderGraphNodeKind.Output: return ShaderGraphValueType.Color;
                    case ShaderGraphNodeKind.Split: case ShaderGraphNodeKind.Length: case ShaderGraphNodeKind.Distance: case ShaderGraphNodeKind.Dot:
                    case ShaderGraphNodeKind.SampleCurve: case ShaderGraphNodeKind.Noise: case ShaderGraphNodeKind.Shape: return ShaderGraphValueType.Float;
                    case ShaderGraphNodeKind.Compare: return ShaderGraphValueType.Boolean;
                }
                if (ShaderGraphNodes.IsFilter(node.kind)) return InputType(node, "In", visiting);
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
            var type = node.FindInput(name)?.type ?? (node.kind == ShaderGraphNodeKind.Reroute ? node.valueType : ShaderGraphValueType.Float);
            foreach (var edge in Graph.edges)
                if (edge.inputNode == node.id && edge.inputPort == name)
                { type = OutputType(Graph.FindNode(edge.outputNode), edge.outputPort, visiting); break; }
            return node.kind == ShaderGraphNodeKind.Swizzle ? ShaderGraphNodes.SwizzleInputType(node.swizzle, type) : type;
        }
    }
}
