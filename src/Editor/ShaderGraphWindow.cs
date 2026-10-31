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
        [SerializeField] private Vector3 viewPosition;
        [SerializeField] private Vector3 viewScale = Vector3.one;
        [SerializeField] private LayerPreviewPanel.ViewState previewState = new LayerPreviewPanel.ViewState();
        [SerializeField] private float codeHeight = 220f;
        [SerializeField] private bool codeCollapsed = true;
        private Canvas canvas;
        private VisualElement declarations;
        private ShaderGraphParameterViewSource parameterSource;
        private ShaderFXParameterView parameterValues;
        private LayerPreviewPanel preview;
        private HelpBox diagnostics;
        private Label status;
        private VisualElement body;
        private TextField generatedSource;
        private VisualElement codePanel;
        private int codeCursor, codeSelection;
        private IVisualElementScheduledItem codeSelectionCapture;
        private ShaderGraph boundGraph;
        private ulong boundLayout;
        private bool applying, refreshRequested, rebuildRequested, compileRequested, ready;

        internal ShaderLayerBehaviour Behaviour => document != null ? document.FindLayer(layerId)?.Behaviour as ShaderLayerBehaviour : null;
        internal ShaderGraph Graph => Behaviour?.graph;
        internal bool Editable => Behaviour != null && !WhimTexApi.IsLayerContentLocked(document, Behaviour.Owner);

        public static void Open(ShaderLayerBehaviour layer, WhimTexDocument document)
        {
            if (layer == null || document == null) return;
            foreach (var existing in Resources.FindObjectsOfTypeAll<ShaderGraphWindow>())
                if (existing.document == document && existing.layerId == layer.Id) { existing.Show(); existing.Focus(); return; }
            var window = CreateInstance<ShaderGraphWindow>();
            window.document = document; window.layerId = layer.Id;
            window.titleContent = WhimTexBranding.WindowTitle("Shader Graph — " + layer.layerName);
            window.minSize = new Vector2(680, 380);
            window.Show();
        }

        private void OnEnable()
        {
            WhimTexDocument.Changed += OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged += OnResourcesChanged;
            WhimTexApi.LiveEditLocksChanged += OnLocksChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            refreshRequested = rebuildRequested = true;
        }
        private void OnDisable()
        {
            WhimTexDocument.Changed -= OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= OnResourcesChanged;
            WhimTexApi.LiveEditLocksChanged -= OnLocksChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            preview?.Dispose(); preview = null;
            canvas?.Dispose(); canvas = null;
            ready = false;
        }
        public void CreateGUI()
        {
            preview?.Dispose(); canvas?.Dispose();
            rootVisualElement.Clear();
            WhimTexUI.ApplyWindowStyles(rootVisualElement);
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/ShaderGraphWindow.uss");
            if (styles != null) rootVisualElement.styleSheets.Add(styles);
            rootVisualElement.AddToClassList("whimtex-shader-graph");
            rootVisualElement.EnableInClassList("whimtex-shader-graph--light", !EditorGUIUtility.isProSkin);

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(() => canvas?.ShowSearch(position.position + position.size * .5f)) { text = "Add Node" });
            toolbar.Add(new ToolbarButton(() => Compile()) { text = "Compile" });
            toolbar.Add(new ToolbarButton(() => canvas?.FrameAll()) { text = "Frame All" });
            toolbar.Add(new ToolbarButton(() => canvas?.FrameSelection()) { text = "Frame Selection" });
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
            // One scroll surface contains both declarations and their native value controls.
            scroll.Add(declarations);
            parameterSource = new ShaderGraphParameterViewSource(document, layerId);
            parameterValues = new ShaderFXParameterView(parameterSource);
            var values = new Foldout { text = "Values", value = true };
            values.Add(parameterValues); scroll.Add(values);
            sidebar.Add(scroll);
            previewState ??= new LayerPreviewPanel.ViewState();
            preview = new LayerPreviewPanel(previewState); sidebar.Add(preview);
            body.Add(sidebar);
            canvas = new Canvas(this); body.Add(canvas);
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
            if (compileRequested) Compile();
            canvas?.TickPreviews();
        }
        private void OnDocumentChanged(WhimTexDocument changed)
        {
            if (changed != document || applying) return;
            refreshRequested = true;
            canvas?.InvalidatePreviews();
            preview?.RequestLayerPreview();
        }
        private void OnResourcesChanged(WhimTexDocument changed)
        {
            if (changed != document) return;
            refreshRequested = true; preview?.RequestLayerPreview();
            canvas?.InvalidatePreviews();
        }
        private void OnLocksChanged() { if (ready) body.SetEnabled(Editable); }
        private void OnUndoRedo()
        {
            refreshRequested = rebuildRequested = compileRequested = true;
            canvas?.InvalidatePreviews();
            // Undo may replace the serialized behaviour; always resolve it from the owning document.
            preview?.RequestLayerPreview(true);
        }
        private void RefreshInterface()
        {
            if (!ready) return;
            refreshRequested = false;
            var behaviour = Behaviour;
            if (behaviour == null)
            {
                body.SetEnabled(false); status.text = "Layer unavailable";
                diagnostics.text = "The Shader layer no longer exists in this document.";
                diagnostics.RemoveFromClassList("whimtex-hidden"); return;
            }
            body.SetEnabled(Editable);
            titleContent = WhimTexBranding.WindowTitle("Shader Graph — " + behaviour.layerName);
            ulong layout = LayoutFingerprint(Graph);
            if (rebuildRequested || !ReferenceEquals(boundGraph, Graph) || boundLayout != layout)
            {
                boundGraph = Graph; boundLayout = layout; rebuildRequested = false;
                RebuildParameters(); canvas.Rebuild();
            }
            else canvas.RefreshValues();
            parameterSource.RefreshParameters(); parameterValues.Refresh();
            preview.Bind(document, behaviour.Owner);
            RefreshDiagnostics();
        }

        internal void Edit(string name, Action<ShaderGraph> change, bool structural = true, bool rebuild = true, bool layoutOnly = false)
        {
            if (!Editable || Graph == null) return;
            Undo.RecordObject(document, name);
            applying = true;
            try
            {
                change(Graph);
                if (structural) Behaviour.InvalidateGraph();
                if (layoutOnly)
                {
                    EditorUtility.SetDirty(document);
                    if (document.documentBinding != null) document.documentBinding.dirty = true;
                }
                else document.MarkChanged();
            }
            finally { applying = false; }
            if (structural) compileRequested = true;
            rebuildRequested |= rebuild;
            refreshRequested = true;
            if (!layoutOnly) preview?.RequestLayerPreview(true);
            if (!layoutOnly) canvas?.InvalidatePreviews();
        }
        internal void EditNode(string id, string name, Action<ShaderGraphNode> edit, bool structural = true, bool rebuild = true)
        {
            if (Graph?.FindNode(id) == null) return;
            Edit(name, graph => edit(graph.FindNode(id)), structural, rebuild);
        }
        private void Compile()
        {
            compileRequested = false;
            if (Behaviour == null) return;
            try { Behaviour.CompileGraph(document); }
            catch (Exception error) { ShowError(error.Message); return; }
            RefreshDiagnostics(); preview?.RequestLayerPreview(true);
        }
        private void RefreshDiagnostics()
        {
            bool error = Behaviour?.HasCompileError == true;
            string message = error ? Behaviour.Diagnostics : string.Empty;
            diagnostics.text = message;
            diagnostics.EnableInClassList("whimtex-hidden", message.Length == 0);
            status.text = error ? "Compile error" : compileRequested ? "Compile pending" : Behaviour?.Diagnostics ?? "Fragment · RGBA";
            RefreshGeneratedSource();
            canvas.ClearErrors();
            if (error) canvas.ShowError(Behaviour.ErrorNodeId, message);
        }
        internal void ShowError(string message)
        {
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
                generatedSource = new ShaderFXCodeField { name = "generatedHLSL" };
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
            string source = Behaviour?.GeneratedSource ?? string.Empty;
            if (generatedSource.value != source) generatedSource.SetValueWithoutNotify(source);
        }
        internal void AddNode(ShaderGraphNodeKind kind, Vector2 position, string parameterId = null)
        {
            if (!Editable || Graph == null) return;
            if (Graph.nodes.Count >= 512) { ShowError("A Shader graph supports up to 512 nodes."); return; }
            if (kind == ShaderGraphNodeKind.Output && Graph.nodes.Exists(n => n.kind == kind)) return;
            if (kind == ShaderGraphNodeKind.Parameter && parameterId == null)
            {
                if (Graph.parameters.Count == 0) { ShowError("Add a parameter first."); return; }
                parameterId = Graph.parameters[0].id;
            }
            Edit("Add Shader Node", graph =>
            {
                var node = ShaderGraphNodes.Create(kind, position);
                node.parameterId = parameterId; graph.nodes.Add(node);
            });
        }
        private static ulong LayoutFingerprint(ShaderGraph graph)
        {
            ulong hash = ShaderGraphCompiler.CodeFingerprint(graph);
            if (graph?.parameters != null) foreach (var p in graph.parameters)
            {
                if (p == null) continue;
                unchecked
                {
                    if (p.name != null) foreach (char c in p.name) hash = (hash ^ c) * 1099511628211UL;
                    hash = (hash ^ (p.hasMinimum ? 1UL : 0UL) ^ (p.hasMaximum ? 2UL : 0UL)) * 1099511628211UL;
                }
            }
            return hash;
        }
    }
}
