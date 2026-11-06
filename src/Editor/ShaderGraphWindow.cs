using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow : EditorWindow
    {
        [SerializeField] private WhimTexDocument document;
        [SerializeField] private string layerId;
        [SerializeField] private ShaderFX effect;
        [SerializeField] private bool effectTarget;
        [SerializeField] private Vector3 viewPosition;
        [SerializeField] private Vector3 viewScale = Vector3.one;
        [SerializeField] private LayerPreviewPanel.ViewState previewState = new LayerPreviewPanel.ViewState();
        [SerializeField] private Rect previewBounds = new Rect(0, 0, 256, 276);
        [SerializeField] private bool previewPositioned;
        [SerializeField] private PreviewAnchor previewAnchor;
        [SerializeField] private float codeHeight = 220f;
        [SerializeField] private bool codeCollapsed = true;
        private Canvas canvas;
        private VisualElement declarations;
        private ShaderGraphParameterViewSource parameterSource;
        private ShaderParameterView parameterValues;
        private LayerPreviewPanel preview;
        private HelpBox diagnostics;
        private Label status;
        private ToolbarButton compileButton;
        private VisualElement body;
        private TextField generatedSource;
        private VisualElement codePanel;
        private int codeCursor, codeSelection;
        private IVisualElementScheduledItem codeSelectionCapture;
        private ulong boundLayout;
        private ShaderGraphEditorTarget target;
        private ShaderGraph boundGraph;
        private ShaderGraphSubGraph boundSubGraph;
        private ShaderProgramMode boundMode;
        private bool applying, refreshRequested, rebuildRequested, compileRequested, ready;

        internal ShaderGraphEditorTarget Target => target ??= new ShaderGraphEditorTarget(document, layerId, effect, effectTarget);
        internal ShaderGraph Graph => ResolveGraph();
        internal bool SourceEditable => !Target.IsReadOnly && !Target.IsLinked;
        internal bool Editable => Target.GraphActive && SourceEditable;

        public static void Open(ShaderLayerBehaviour layer, WhimTexDocument document)
        {
            if (layer == null || document == null) return;
            OpenTarget(document, layer.Owner, null, false);
        }

        public static void Open(ShaderFX effect, Layer layer, WhimTexDocument document)
        {
            if (effect == null || layer == null || document == null || layer.fx?.Contains(effect) != true ||
                !ReferenceEquals(document.FindLayer(layer.Id), layer)) return;
            OpenTarget(document, layer, effect, true);
        }

        private static void OpenTarget(WhimTexDocument document, Layer layer, ShaderFX effect, bool effectTarget)
        {
            if (layer == null) return;
            foreach (var existing in Resources.FindObjectsOfTypeAll<ShaderGraphWindow>())
                if (existing.document == document && existing.layerId == layer.Id && existing.effectTarget == effectTarget &&
                    (!effectTarget || existing.effect == effect)) { existing.Show(); existing.Focus(); return; }
            var window = CreateInstance<ShaderGraphWindow>();
            window.document = document; window.layerId = layer.Id; window.effect = effect; window.effectTarget = effectTarget;
            window.titleContent = WhimTexBranding.WindowTitle("Shader Graph — " + window.Target.Title);
            window.minSize = new Vector2(680, 380);
            window.Show();
        }

        private void OnEnable()
        {
            WhimTexDocument.Changed += OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged += OnResourcesChanged;
            ShaderFX.Changed += OnEffectChanged;
            WhimTexApi.LiveEditLocksChanged += OnLocksChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            refreshRequested = rebuildRequested = true;
        }
        private void OnDisable()
        {
            WhimTexDocument.Changed -= OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= OnResourcesChanged;
            ShaderFX.Changed -= OnEffectChanged;
            WhimTexApi.LiveEditLocksChanged -= OnLocksChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            preview?.Dispose(); preview = null;
            canvas?.Dispose(); canvas = null;
            parameterSource?.ReleasePreview();
            ready = false;
        }
        public void CreateGUI()
        {
            preview?.Dispose(); canvas?.Dispose(); parameterSource?.ReleasePreview();
            rootVisualElement.Clear();
            WhimTexUI.ApplyWindowStyles(rootVisualElement);
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/ShaderGraphWindow.uss");
            if (styles != null) rootVisualElement.styleSheets.Add(styles);
            rootVisualElement.AddToClassList("whimtex-shader-graph");
            rootVisualElement.EnableInClassList("whimtex-shader-graph--light", !EditorGUIUtility.isProSkin);

            var toolbar = new Toolbar();
            backToGraph = new ToolbarButton(() => OpenSubGraph(null)) { text = "Back to Graph" };
            backToGraph.style.display = Graph?.activeSubGraph != null ? DisplayStyle.Flex : DisplayStyle.None;
            toolbar.Add(backToGraph);
            compileButton = new ToolbarButton(() => Compile()) { text = "Compile" };
            toolbar.Add(compileButton);
            toolbar.Add(new ToolbarButton(() => canvas?.FrameAll()) { text = "Frame All" });
            toolbar.Add(new ToolbarButton(() => canvas?.FrameSelection()) { text = "Frame Selection" });
            toolbar.Add(new ToolbarButton(RefreshFunctionCatalog) { text = "Refresh HLSL" });
            status = new Label(); status.AddToClassList("whimtex-shader-graph-status"); toolbar.Add(status);
            rootVisualElement.Add(toolbar);

            body = new TwoPaneSplitView(0, 280, TwoPaneSplitViewOrientation.Horizontal);
            body.AddToClassList("whimtex-shader-graph-body");
            var sidebar = new VisualElement(); sidebar.AddToClassList("whimtex-shader-graph-sidebar");
            var heading = new Toolbar(); heading.Add(new Label("Parameters"));
            var addParameter = new ToolbarMenu { text = "+" };
            foreach (var type in ParameterTypes)
            {
                var captured = type;
                addParameter.menu.AppendAction(ParameterTypeName(type), _ => AddParameter(captured), _ => Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
            heading.Add(addParameter); sidebar.Add(heading);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("whimtex-shader-graph-properties");
            declarations = new VisualElement();
            declarations.AddToClassList("whimtex-shader-graph-declarations");
            declarations.RegisterCallback<DragUpdatedEvent>(OnParameterDragUpdated, TrickleDown.TrickleDown);
            declarations.RegisterCallback<DragPerformEvent>(OnParameterDragPerform, TrickleDown.TrickleDown);
            declarations.RegisterCallback<DragLeaveEvent>(_ => HideParameterInsertion());
            declarations.RegisterCallback<DragExitedEvent>(_ => HideParameterInsertion());
            // One scroll surface contains both declarations and their native value controls.
            scroll.Add(declarations);
            parameterSource = new ShaderGraphParameterViewSource(Target, graphOnly: true);
            parameterValues = new ShaderParameterView(parameterSource);
            var values = new Foldout { text = "Values", value = true };
            values.Add(parameterValues); scroll.Add(values);
            sidebar.Add(scroll);
            previewState ??= new LayerPreviewPanel.ViewState();
            body.Add(sidebar);
            var graphArea = new VisualElement(); graphArea.AddToClassList("whimtex-shader-graph-area");
            canvas = new Canvas(this); graphArea.Add(canvas);
            preview = new LayerPreviewPanel(previewState, docked: false, animateShaderGraph: true);
            graphArea.Add(new LayerPreviewOverlay(this, preview));
            body.Add(graphArea);
            rootVisualElement.Add(body);
            diagnostics = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            diagnostics.AddToClassList("whimtex-hidden"); rootVisualElement.Add(diagnostics);
            generatedSource = null;
            codePanel = new VisualElement { name = "generated-hlsl-panel" };
            codePanel.AddToClassList("whimtex-output-preview-footer");
            var codeHandle = new VerticalRevealHandle("Generated HLSL", () => codePanel.resolvedStyle.height, ResizeGeneratedCode)
            { name = "generated-hlsl-resizer", tooltip = "Drag up to reveal generated code; drag down to hide it." };
            codePanel.Add(codeHandle);
            rootVisualElement.Add(codePanel);
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnCodePanelGeometry);
            UpdateCodePanelLayout();
            ready = true; rebuildRequested = refreshRequested = compileRequested = true;
            RefreshInterface();
        }

        private void Update()
        {
            if (!ready) return;
            if (refreshRequested || rebuildRequested) RefreshInterface();
            if (compileRequested) CompileRequested();
            if (Target.GraphActive && Graph != null) canvas?.TickPreviews();
        }
        private void OnDocumentChanged(WhimTexDocument changed)
        {
            if (changed != document || applying) return;
            subGraphView = null;
            typesInvalidated = true;
            refreshRequested = true;
            canvas?.InvalidatePreviews(external: !effectTarget || WhimTexDocument.ShaderNotificationSource != effect);
            preview?.RequestLayerPreview();
        }
        private void OnResourcesChanged(WhimTexDocument changed)
        {
            if (changed != document) return;
            refreshRequested = true; preview?.RequestLayerPreview();
            bool ownShader = !effectTarget && ReferenceEquals(WhimTexDocument.ShaderLayerNotificationSource, Target.Behaviour);
            canvas?.InvalidatePreviews(external: !ownShader && (!effectTarget || WhimTexDocument.ShaderNotificationSource != effect));
        }
        private void OnEffectChanged(ShaderFX changed)
        {
            if (!effectTarget || changed != effect || applying) return;
            subGraphView = null;
            typesInvalidated = true;
            refreshRequested = true;
            canvas?.InvalidatePreviews(external: false); preview?.RequestLayerPreview();
        }
        private void OnLocksChanged()
        {
            if (!ready) return;
            body.SetEnabled(Editable); compileButton.SetEnabled(Editable);
            refreshRequested = true;
        }
        private void OnUndoRedo()
        {
            subGraphView = null;
            typesInvalidated = true;
            if (!string.IsNullOrEmpty(editingSubGraphId)) rebuildRequested = true;
            refreshRequested = compileRequested = true;
            canvas?.InvalidatePreviews();
            // Undo may replace the serialized behaviour; always resolve it from the owning document.
            preview?.RequestLayerPreview(true);
        }
        private void RefreshInterface()
        {
            if (!ready) return;
            refreshRequested = false;
            var host = Target;
            compileButton.SetEnabled(Editable);
            if (!host.Available || Graph == null)
            {
                body.SetEnabled(false); status.text = effectTarget ? "FX unavailable" : "Layer unavailable";
                diagnostics.messageType = HelpBoxMessageType.Error;
                diagnostics.text = effectTarget ? "The FX no longer exists on this layer." : "The Shader layer no longer exists in this document.";
                boundGraph = null; rebuildRequested = compileRequested = false;
                declarations.Clear(); parameterSource.RefreshParameters(); parameterValues.Refresh(); canvas.Rebuild();
                preview.Bind(document, null); RefreshGeneratedSource();
                diagnostics.RemoveFromClassList("whimtex-hidden"); return;
            }
            body.SetEnabled(Editable);
            titleContent = WhimTexBranding.WindowTitle("Shader Graph — " + host.Title);
            if (!ReferenceEquals(boundGraph, RootGraph) || !ReferenceEquals(boundSubGraph, Graph.activeSubGraph) || boundMode != host.Program.mode)
            {
                boundGraph = RootGraph; boundSubGraph = Graph.activeSubGraph; boundMode = host.Program.mode;
                rebuildRequested = true; compileRequested = host.GraphActive;
                if (!host.GraphActive) canvas.SuspendPreviews();
                canvas.InvalidatePreviews();
            }
            Graph.BindFunctions();
            typesInvalidated = true;
            ulong layout = LayoutFingerprint(Graph);
            if (rebuildRequested || boundLayout != layout)
            {
                boundLayout = layout; rebuildRequested = false;
                RebuildParameters(); canvas.Rebuild();
            }
            else canvas.RefreshValues();
            foreach (var update in declarationRefresh) update();
            parameterSource.RefreshParameters(); parameterValues.Refresh();
            preview.Bind(document, host.Layer);
            RefreshDiagnostics();
        }

        internal void Edit(string name, Action<ShaderGraph> change, bool structural = true, bool rebuild = true, bool layoutOnly = false)
        {
            if (!Editable || Graph == null) return;
            Target.RecordUndo(name);
            applying = true;
            try
            {
                change(Graph);
                typesInvalidated = true;
                CommitGraphView();
                if (structural) { RootGraph.PruneFunctions(); Graph.PruneGroups(); Target.InvalidateGraph(); }
                // A first inline edit materializes a default input and changes the code
                // fingerprint, but its field/ports already exist. Honor the caller's
                // no-rebuild intent so an open picker keeps its source control.
                if (!rebuild) boundLayout = LayoutFingerprint(Graph);
                Target.MarkChanged(layoutOnly);
            }
            finally { applying = false; }
            if (structural) compileRequested = true;
            rebuildRequested |= rebuild;
            refreshRequested = true;
            if (!layoutOnly) preview?.RequestLayerPreview(true);
            if (!layoutOnly) canvas?.InvalidatePreviews(external: false);
        }
        internal void EditNode(string id, string name, Action<ShaderGraphNode> edit, bool structural = true, bool rebuild = true)
        {
            if (Graph?.FindNode(id) == null) return;
            Edit(name, graph => edit(graph.FindNode(id)), structural, rebuild);
        }
        private void Compile() => CompileCurrentGraph(false);
        private void CompileRequested() => CompileCurrentGraph(true);
        private void CompileCurrentGraph(bool requested)
        {
            compileRequested = false;
            if (!Editable) return;
            try { if (requested) Target.EnsureGraphCompiled(); else Target.CompileGraph(); }
            catch (Exception error) { ShowError(error.Message); return; }
            RefreshDiagnostics(); preview?.RequestLayerPreview(true);
        }
        private void RefreshDiagnostics()
        {
            var host = Target;
            bool error = host.HasCompileError;
            var global = new System.Text.StringBuilder();
            var level = ShaderGraphMessageLevel.Info;
            foreach (var item in host.Messages)
            {
                if (item.nodeId != null && Graph?.FindNode(item.nodeId) != null) continue;
                if (global.Length > 0) global.AppendLine();
                global.Append(item.text);
                if (item.level > level) level = item.level;
            }
            string message = global.ToString();
            diagnostics.messageType = level == ShaderGraphMessageLevel.Error ? HelpBoxMessageType.Error :
                level == ShaderGraphMessageLevel.Warning ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
            diagnostics.text = message;
            diagnostics.EnableInClassList("whimtex-hidden", message.Length == 0);
            status.text = !host.GraphActive ? "Code mode · Graph retained" : error ? "Compile error" :
                host.IsLinked ? "Linked preset · Read-only" :
                compileRequested || host.IsCompiling ? "Compile pending" :
                host.DiagnosticSeverity == ShaderGraphMessageLevel.Warning ? "Compiled with warnings." :
                host.Messages.Count > 0 ? "Compiled with messages." : host.Diagnostics ?? "Fragment · RGBA";
            RefreshGeneratedSource();
            canvas.SetMessages("compile", host.Messages);
        }
        internal void ShowError(string message)
        {
            diagnostics.messageType = HelpBoxMessageType.Error;
            diagnostics.text = message; diagnostics.RemoveFromClassList("whimtex-hidden"); status.text = "Graph error";
        }
        private float MaxCodeHeight() => Mathf.Max(VerticalRevealHandle.HeaderHeight, rootVisualElement.contentRect.height - 80f);
        private void OnCodePanelGeometry(GeometryChangedEvent evt) => UpdateCodePanelLayout();
        private void QueueCodeSelection() => codeSelectionCapture.ExecuteLater(0);
        private void RememberCodeSelection()
        {
            var focused = generatedSource?.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null || focused != generatedSource && !generatedSource.Contains(focused)) return;
            codeCursor = generatedSource.textSelection.cursorIndex;
            codeSelection = generatedSource.textSelection.selectIndex;
        }
        private void ResizeGeneratedCode(float requestedHeight)
        {
            float maximum = MaxCodeHeight();
            codeCollapsed = VerticalRevealHandle.ShouldCollapse(requestedHeight, maximum);
            if (!codeCollapsed) codeHeight = Mathf.Clamp(requestedHeight, Mathf.Min(VerticalRevealHandle.MinimumExpandedHeight, maximum), maximum);
            UpdateCodePanelLayout();
        }
        private void UpdateCodePanelLayout()
        {
            if (codePanel == null) return;
            float maximum = MaxCodeHeight();
            bool hidden = codeCollapsed || maximum <= VerticalRevealHandle.HeaderHeight;
            codePanel.style.height = hidden ? VerticalRevealHandle.HeaderHeight : Mathf.Clamp(codeHeight, Mathf.Min(VerticalRevealHandle.MinimumExpandedHeight, maximum), maximum);
            if (hidden) { generatedSource?.RemoveFromHierarchy(); return; }
            if (generatedSource == null)
            {
                generatedSource = new ShaderCodeField { name = "generatedHLSL" };
                generatedSource.AddToClassList("whimtex-shader-graph-source");
                codeSelectionCapture = generatedSource.schedule.Execute(RememberCodeSelection);
                codeSelectionCapture.Pause();
                generatedSource.textSelection.OnCursorIndexChange += QueueCodeSelection;
                generatedSource.textSelection.OnSelectIndexChange += QueueCodeSelection;
            }
            bool reattached = generatedSource.parent != codePanel;
            string previous = generatedSource.value;
            if (reattached) codePanel.Add(generatedSource);
            RefreshGeneratedSource();
            if (reattached && generatedSource.value == previous && codeCursor != codeSelection)
            {
                int cursor = codeCursor, selection = codeSelection;
                generatedSource.schedule.Execute(() =>
                {
                    if (generatedSource.panel != null && generatedSource.value == previous)
                    {
                        generatedSource.Focus();
                        generatedSource.textSelection.SelectRange(Mathf.Clamp(cursor, 0, previous.Length), Mathf.Clamp(selection, 0, previous.Length));
                    }
                });
            }
        }
        private void RefreshGeneratedSource()
        {
            if (generatedSource?.panel == null) return;
            string source = Target.GeneratedSource ?? string.Empty;
            if (generatedSource.value != source) generatedSource.SetValueWithoutNotify(source);
        }
        internal void AddNode(ShaderGraphNodeKind kind, Vector2 position, string parameterId = null)
        {
            if (!Editable || Graph == null) return;
            if (Graph.nodes.Count + (ShaderGraphLoops.IsEndpoint(kind) ? 2 : 1) > 512 ||
                ShaderGraphLoops.IsEndpoint(kind) && Graph.edges.Count >= 4096) { ShowError("The graph has no room for this node or loop pair."); return; }
            if (kind == ShaderGraphNodeKind.Output && Graph.nodes.Exists(n => n.kind == kind)) return;
            if (kind == ShaderGraphNodeKind.Parameter && parameterId == null)
            {
                if (Graph.parameters.Count == 0) { ShowError("Add a parameter first."); return; }
                parameterId = Graph.parameters[0].id;
            }
            Edit("Add Shader Node", graph =>
            {
                var node = ShaderGraphNodes.Create(kind, position);
                if (node.function != null) graph.functions.Add(node.function);
                node.parameterId = parameterId; graph.nodes.Add(node);
                ShaderGraphLoops.AddPartner(graph, node);
            });
        }
        private static ulong LayoutFingerprint(ShaderGraph graph)
        {
            ulong hash = ShaderGraphCompiler.CodeFingerprint(graph);
            void Text(string value) { unchecked { if (value != null) foreach (char c in value) hash = (hash ^ c) * 1099511628211UL; hash = (hash ^ 255UL) * 1099511628211UL; } }
            if (graph?.functions != null) foreach (var f in graph.functions)
            {
                if (f == null) continue;
                Text(f.name);
                if (f.ports != null) foreach (var p in f.ports)
                {
                    if (p == null) continue;
                    Text(p.label);
                    unchecked { hash = (hash ^ (p.hasRange ? 1UL : 0UL) ^ (uint)p.minimum.GetHashCode()) * 1099511628211UL; hash = (hash ^ (uint)p.maximum.GetHashCode()) * 1099511628211UL; }
                }
            }
            if (graph?.parameters != null) foreach (var p in graph.parameters)
            {
                if (p == null) continue;
                unchecked
                {
                    hash = (hash ^ ShaderGraphParameters.MetadataFingerprint(p)) * 1099511628211UL;
                    hash = (hash ^ (p.hasMinimum ? 1UL : 0UL) ^ (p.hasMaximum ? 2UL : 0UL)) * 1099511628211UL;
                }
            }
            if (graph?.subGraphs != null) foreach (var definition in graph.subGraphs)
            {
                Text(definition.id); Text(definition.name);
                foreach (var pin in definition.inputs) Text(pin.name);
                foreach (var pin in definition.outputs) Text(pin.name);
            }
            if (graph?.notes != null) foreach (var note in graph.notes) Text(note.id);
            if (graph?.groups != null) foreach (var group in graph.groups)
            {
                Text(group.id);
                foreach (var id in group.elements) Text(id);
            }
            return hash;
        }
    }
}
