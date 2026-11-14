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
            private readonly Dictionary<(int width, int height, float scale), RenderTexture> fxPreviewInputs = new();
            private DCFApixels.WhimTex.Layer fxPreviewLayer;
            private ShaderFX fxPreviewEffect;
            private ulong fxPreviewFrame;
            private bool fxInputsInvalidated = true;
            private bool previewsInvalidated = true;
            internal bool PreviewStateIsCurrent => !previewsInvalidated;
            private bool previewPrioritiesInvalidated = true;
            private readonly HashSet<string> editedPreviewNodes = new();
            private Vector2? previewEditPosition;
            private string previewPriorityScope;
            private ShaderGraphPreparation.Session preparationSession;
            private ShaderGraph preparationGraph;
            internal int PreparationGeneration { get; private set; }
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
                viewDataKey = "whimtex-shader-graph-" + window.Target.Identity;
                AddToClassList("whimtex-shader-graph-canvas");
                focusable = true;
                SetupZoom(.08f, 3f);
                this.AddManipulator(new ContentDragger());
                this.AddManipulator(new SelectionDragger());
                SetupBranchClicks();
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
                    InvalidatePreviewPriorities();
                    if (rebuilding) return;
                    window.viewPosition = contentViewContainer.resolvedStyle.translate;
                    Vector3 scale = contentViewContainer.resolvedStyle.scale.value;
                    window.viewScale = new Vector3(scale.x, scale.y, 1f);
                };
                RegisterCallback<GeometryChangedEvent>(_ => InvalidatePreviewPriorities());
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
                InvalidatePreparation();
                ResetBranchTraces();
                ReleaseFXPreviewInputs();
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
                search.replacement = null;
                search.Open(screenPosition);
            }
            public override void BuildContextualMenu(ContextualMenuPopulateEvent e)
            {
                base.BuildContextualMenu(e);
                AnnotationMenu(e);
                SubGraphMenu(e);
                ReplacementMenu(e);
                var edge = EdgeAt(e.target);
                if (edge != null)
                {
                    Vector2 point = e.mousePosition;
                    e.menu.AppendAction("Add Reroute", _ => InsertReroute(edge, point),
                        _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }
            }
            internal void Rebuild() => ReconcileNodes();
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
                ResetBranchTraces(refreshHover: true);
                RefreshAnnotations();
            }
            internal NodePreview GetPreview(ShaderGraphNode node)
            {
                if (!previews.TryGetValue(node.id, out var preview))
                { previews.Add(node.id, preview = new NodePreview(window, this, node.id)); previewQueue.Add(preview); }
                previewsInvalidated |= !ReferenceEquals(previewState.bindings.Graph, window.Graph) ||
                    !previewState.bindings.nodes.TryGetValue(node.id, out var bound) || !ReferenceEquals(bound, node);
                InvalidatePreviewPriorities();
                preview.RefreshOutputs();
                return preview;
            }
            internal void InvalidatePreviews(bool external = true)
            {
                window.outputVMDirty = true;
                InvalidatePreparation();
                previewsInvalidated = fxInputsInvalidated = true;
                if (external) foreach (var preview in previews.Values) preview.Invalidate();
            }
            internal ulong? OutputStamp()
            {
                // Subgraph interfaces can affect root consumers outside this view.
                if (window.Graph?.activeSubGraph != null) return null;
                if (previewsInvalidated || !ReferenceEquals(previewState.bindings.Graph, window.Graph)) return null;
                var output = window.Graph?.nodes.Find(n => n.kind == ShaderGraphNodeKind.Output);
                return output == null ? null : previewState.Fingerprint(output.id);
            }
            internal bool TryGetOutputState(out ulong code, out ulong values, out ShaderGraphBindingState bindings)
            {
                code = values = 0; bindings = null;
                if (window.Graph?.activeSubGraph != null) return false;
                PreparePreviewState();
                var output = window.Graph?.nodes.Find(n => n.kind == ShaderGraphNodeKind.Output);
                if (output == null) return false;
                code = previewState.Fingerprint(output.id); values = previewState.ValueFingerprint(output.id);
                bindings = previewState.bindings; return true;
            }
            internal bool ApplyGraphEdit(ulong? previousOutput, string changedNode)
            {
                window.outputVMDirty = true;
                InvalidatePreparation(); fxInputsInvalidated = true;
                if (previewsInvalidated || !previewState.PrepareNode(window.Graph, changedNode)) previewState.Prepare(window.Graph);
                previewsInvalidated = false;
                RememberPreviewEdit(window.editedNode);
                foreach (var pair in previews) if (previewState.IsChanged(pair.Key)) pair.Value.InvalidateCode(pair.Value.Fingerprint(previewState));
                RefreshDiagnosticSources();
                UpdatePreviewPriorities();
                return previousOutput.HasValue && OutputStamp() == previousOutput;
            }
            internal void SetOutputPreviewBackend(bool svm)
            {
                foreach (var view in nodeViews.Values)
                    if (view is NodeView node && node.Data?.kind == ShaderGraphNodeKind.Output)
                        node.SetOutputPreviewBackend(svm);
            }
            internal void InvalidatePreparation()
            {
                InvalidatePreviewPriorities();
                ShaderGraphPreparation.Invalidate(preparationGraph);
                ShaderGraphPreparation.Invalidate(window.Graph);
                preparationSession = null; preparationGraph = null;
                PreparationGeneration++;
            }
            internal ShaderGraphPreparation.Session Preparation()
            {
                var graph = window.Graph;
                bool effect = window.Target.IsEffect;
                if (preparationSession == null || !ReferenceEquals(graph, preparationGraph) ||
                    preparationSession.effect != effect || preparationSession.revision != ShaderProgramSourceRevision.Current)
                {
                    preparationSession = ShaderGraphPreparation.Capture(graph, effect);
                    preparationGraph = graph;
                }
                return preparationSession;
            }
            internal void InvalidatePreviewImages()
            {
                fxInputsInvalidated = true;
                foreach (var preview in previews.Values) preview.Invalidate();
            }
            internal void InvalidatePreviewLighting()
            {
                foreach (var pair in previews)
                    if (previewState.UsesGlobalLighting(pair.Key)) pair.Value.InvalidateCode(pair.Value.Fingerprint(previewState));
            }
            internal void InvalidatePreviewPriorities() => previewPrioritiesInvalidated = true;
            void CheckPreviewPriorityScope()
            {
                if (previewPriorityScope == window.editingSubGraphId) return;
                previewPriorityScope = window.editingSubGraphId;
                editedPreviewNodes.Clear(); previewEditPosition = null;
                InvalidatePreviewPriorities();
            }
            void RememberPreviewEdit(string editedNode)
            {
                CheckPreviewPriorityScope();
                Vector2 sum = Vector2.zero;
                int count = 0;
                foreach (var node in window.Graph.nodes)
                {
                    if (node == null || (editedNode != null ? node.id != editedNode : !previewState.IsDirectlyChanged(node.id))) continue;
                    if (count++ == 0) editedPreviewNodes.Clear();
                    editedPreviewNodes.Add(node.id);
                    sum += nodeViews.TryGetValue(node.id, out var view) ? view.GetPosition().center : node.position;
                }
                if (count == 0) return;
                // Keep the edit location in graph coordinates; panning and zooming must not move it.
                previewEditPosition = sum / count;
                InvalidatePreviewPriorities();
            }
            internal void PrioritizePreview(string nodeId)
            {
                RememberPreviewEdit(nodeId);
                UpdatePreviewPriorities();
            }
            internal float PreviewPriority(string id)
            {
                if (editedPreviewNodes.Contains(id)) return 0;
                Vector2 position = nodeViews.TryGetValue(id, out var view) ? view.GetPosition().center : Vector2.zero;
                return 1 + (position - (previewEditPosition ?? CenterPosition)).sqrMagnitude;
            }
            void UpdatePreviewPriorities()
            {
                CheckPreviewPriorityScope();
                if (!previewPrioritiesInvalidated) return;
                previewPrioritiesInvalidated = false;
                foreach (var preview in previewQueue) preview.UpdatePriority();
                previewQueue.Sort((a, b) => a.CompilationPriority.CompareTo(b.CompilationPriority));
                previewCursor = 0;
            }
            internal RenderTexture BorrowFXPreviewInput(in LayerRenderContext context)
            {
                var host = window.Target;
                var layer = host.Layer;
                var effect = host.Effect;
                if (fxInputsInvalidated || fxPreviewFrame != ShaderGraphClock.Frame ||
                    !ReferenceEquals(fxPreviewLayer, layer) || fxPreviewEffect != effect)
                {
                    ReleaseFXPreviewInputs();
                    fxPreviewFrame = ShaderGraphClock.Frame; fxPreviewLayer = layer; fxPreviewEffect = effect;
                    fxInputsInvalidated = false;
                }
                var key = (context.width, context.height, context.scaleMultiplier);
                if (!fxPreviewInputs.TryGetValue(key, out var input))
                {
                    input = window.document.RenderShaderFXInput(layer, effect, context.width, context.height,
                        context.scaleMultiplier, ShaderGraphClock.Seconds);
                    fxPreviewInputs.Add(key, input);
                }
                return input;
            }
            private void ReleaseFXPreviewInputs()
            {
                foreach (var input in fxPreviewInputs.Values) if (input != null) ProfiledRenderTexture.ReleaseTemporary(input);
                fxPreviewInputs.Clear();
                fxPreviewLayer = null; fxPreviewEffect = null; fxPreviewFrame = 0;
                fxInputsInvalidated = true;
            }
            internal void SuspendPreviews()
            {
                InvalidatePreparation();
                ReleaseFXPreviewInputs();
                foreach (var pair in previews)
                {
                    pair.Value.Suspend();
                    SetMessages("preview:" + pair.Key, null);
                }
            }
            internal void TickPreviews()
            {
                if (previews.Count == 0) return;
                using var profile = WhimTexProfiler.Measure("shadergraph.preview.tick", window.document);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                PreparePreviewState();
                UpdatePreviewPriorities();
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
            void PreparePreviewState()
            {
                if (!previewsInvalidated && ReferenceEquals(previewState.bindings.Graph, window.Graph)) return;
                previewState.Prepare(window.Graph); previewsInvalidated = false;
                foreach (var pair in previews) if (previewState.IsChanged(pair.Key)) pair.Value.InvalidateCode(pair.Value.Fingerprint(previewState));
                RefreshDiagnosticSources();
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
                    ShaderGraphLoops.IncludePartners(graph, removed);
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
                    graph.nodes.RemoveAll(n => removed.Contains(n.id));
                    graph.edges.RemoveAll(e => removed.Contains(e.inputNode) || removed.Contains(e.outputNode));
                    graph.PruneGroups();
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
                var kind = window.Graph.FindNode(input.nodeId)?.kind;
                if (kind != ShaderGraphNodeKind.Reroute)
                    return Compatible(type, input.type, input.dynamic, kind);
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
                    if (window.Graph.FindNode(id) is ShaderGraphNode node && node.kind == ShaderGraphNodeKind.ForBegin)
                        stack.Push(node.loopPartnerId);
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

            private sealed partial class NodeSearch : ScriptableObject, ISearchWindowProvider
            {
                internal Canvas canvas;
                internal PortData source;
                internal ShaderGraph sourceGraph;
                internal Vector2 dropPosition;
                internal ShaderGraphNode replacement;
                public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
                {
                    if (source != null) return canvas.ConnectedSearchTree(sourceGraph, source);
                    var window = canvas.window;
                    return CreateNodeSearchTree(window.Graph, replacement != null, window.svmNodesOnly, window.WouldRecurse);
                }
                public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
                {
                    if (source != null) return entry.userData is ConnectedNodeChoice choice && canvas.CreateConnectedNode(sourceGraph, source, choice, dropPosition);
                    if (replacement != null) return canvas.window.ReplaceNode(sourceGraph, replacement, entry.userData);
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

        private static List<SearchTreeEntry> CreateNodeSearchTree(ShaderGraph graph, bool replacing, bool svmOnly, Func<string, bool> wouldRecurse = null)
        {
            var tree = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent(replacing ? "Replace Node" : "Create Node"), 0) };
            var categories = new SortedDictionary<string, List<ShaderGraphNodeKind>>();
            foreach (var kind in ShaderGraphNodes.Kinds)
            {
                if (kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.SubGraph ||
                    ShaderGraphNodes.IsSubGraphInterface(kind) || replacing && ShaderGraphLoops.IsEndpoint(kind)) continue;
                if (svmOnly && !ShaderGraphPreviewVM.SupportsDefaultNode(kind)) continue;
                string category = ShaderGraphNodes.Category(kind);
                if (!categories.TryGetValue(category, out var list)) categories.Add(category, list = new List<ShaderGraphNodeKind>());
                list.Add(kind);
            }
            string previousCategory = "";
            foreach (var category in categories)
            {
                int level = AddSearchCategory(tree, category.Key, ref previousCategory);
                foreach (var kind in category.Value)
                    tree.Add(new SearchTreeEntry(new GUIContent(ShaderGraphNodes.Name(kind))) { level = level + 1, userData = kind });
            }
            bool parametersHeading = false;
            foreach (var parameter in graph.parameters)
            {
                if (svmOnly && !ShaderGraphPreviewVM.SupportsParameter(parameter)) continue;
                if (!parametersHeading) { tree.Add(new SearchTreeGroupEntry(new GUIContent("Parameters"), 1)); parametersHeading = true; }
                tree.Add(new SearchTreeEntry(new GUIContent(parameter.name)) { level = 2, userData = parameter.id });
            }
            if (!svmOnly) AddFunctionSearchEntries(tree, null);
            var definitions = graph.subGraphs.Where(d => wouldRecurse?.Invoke(d.id) != true &&
                (!svmOnly || ShaderGraphPreviewVM.SupportsSubGraph(graph, d))).ToList();
            if (definitions.Count != 0)
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent("Sub Graphs"), 1));
                foreach (var definition in definitions) tree.Add(new SearchTreeEntry(new GUIContent(definition.name)) { level = 2, userData = definition });
            }
            return tree;
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

        private static bool Compatible(ShaderGraphValueType output, ShaderGraphValueType input, bool dynamic,
            ShaderGraphNodeKind? kind = null)
        {
            bool matrix = ShaderGraphNodes.MatrixSize(output) != 0;
            if (kind == ShaderGraphNodeKind.Multiply && matrix) return true;
            if (kind.HasValue && ShaderGraphNodes.HasDynamicMatrixInput(kind.Value)) return matrix;
            return dynamic ? ShaderGraphNodes.IsNumeric(output) :
                output == input || ShaderGraphNodes.IsNumeric(output) && ShaderGraphNodes.IsNumeric(input);
        }

        readonly ShaderGraphTypeCache typeCache = new();
        bool typesInvalidated = true;
        private ShaderGraphTypeCache Types
        {
            get
            {
                var graph = Graph;
                if (typesInvalidated || !ReferenceEquals(typeCache.Graph, graph))
                { typeCache.Prepare(graph); typesInvalidated = false; }
                return typeCache;
            }
        }
        private ShaderGraphValueType OutputType(ShaderGraphNode node, string port) => Types.Output(node, port);
        private ShaderGraphValueType InputType(ShaderGraphNode node, string port) => Types.Input(node, port);
        private ShaderGraphValueType PortInputType(ShaderGraphNode node, ShaderGraphPort port) =>
            ShaderGraphNodes.HasDynamicMatrixInput(node.kind) ? InputType(node, port.name) : port.type ?? InputType(node, port.name);
        private static ShaderGraphValueType OutputType(ShaderGraph graph, ShaderGraphNode node, string port)
        { var types = new ShaderGraphTypeCache(); types.Prepare(graph); return types.Output(node, port); }
        private static ShaderGraphValueType InputType(ShaderGraph graph, ShaderGraphNode node, string port)
        { var types = new ShaderGraphTypeCache(); types.Prepare(graph); return types.Input(node, port); }
    }
}
