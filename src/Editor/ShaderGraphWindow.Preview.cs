using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    // All graph previews sample the same time within an Editor update.
    [InitializeOnLoad]
    internal static class ShaderGraphClock
    {
        internal static ulong Frame { get; private set; } = 1;
        internal static float Seconds { get; private set; } = (float)EditorApplication.timeSinceStartup;
        static ShaderGraphClock() => EditorApplication.update += Tick;
        static void Tick()
        {
            Seconds = (float)EditorApplication.timeSinceStartup;
            Frame++;
        }
    }

    public sealed partial class ShaderGraphWindow
    {
        private sealed class LayerPreviewOverlay : ShaderGraphFloatingPanel
        {
            internal LayerPreviewOverlay(ShaderGraphWindow window, LayerPreviewPanel preview)
                : base(window.panelLayout.preview, new Vector2(200, 100))
            {
                name = "shader-layer-preview";
                AddToClassList("whimtex-shader-graph-layer-preview");
                Add(preview);
                AddDragHandle(this, preview.HeaderControls);
                AddResizeHandles();
                PlacementChanged += window.SavePanelLayout;
            }
        }

        private sealed partial class NodePreview : VisualElement, IDisposable
        {
            private const int Size = 128;
            private readonly ShaderGraphWindow window;
            private readonly Canvas canvas;
            private readonly DropdownField output;
            private readonly VisualElement surface;
            private readonly Image image;
            private readonly PreviewLoadingIndicator loading;
            private readonly VisualElement content;
            private readonly Button expand;
            private readonly NodePreviewSource source;
            private readonly string nodeId;
            private VisualElement backendIndicator;
            private bool displayedSVM;
            private uint sourceRevision, messagesRevision;
            private bool expanded = true;
            internal bool Expanded => expanded;
            internal ulong Fingerprint(ShaderGraphPreviewState state) => state.PreviewFingerprint(nodeId, window.accurateLighting);
            internal void BindBackendIndicator(VisualElement indicator)
            {
                backendIndicator = indicator;
                backendIndicator.EnableInClassList("whimtex-hidden", !displayedSVM);
            }
            private void SetDisplayedBackend(bool svm)
            {
                if (displayedSVM == svm) return;
                displayedSVM = svm;
                backendIndicator?.EnableInClassList("whimtex-hidden", !svm);
            }

            internal static bool Supports(ShaderGraphWindow window, ShaderGraphNode node)
            {
                if (node == null || ShaderGraphNodes.IsSubGraphInterface(node.kind) || node.kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or
                    ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.Constant or ShaderGraphNodeKind.Value or ShaderGraphNodeKind.InstanceIndex or
                    ShaderGraphNodeKind.Time or ShaderGraphNodeKind.ForBegin or ShaderGraphNodeKind.ScatterInstance or ShaderGraphNodeKind.CanvasData or
                    ShaderGraphNodeKind.TextureSize or ShaderGraphNodeKind.Split or ShaderGraphNodeKind.Compare or ShaderGraphNodeKind.MatrixDeterminant) return false;
                foreach (string port in ShaderGraphNodes.Outputs(node))
                    if (ShaderGraphNodes.IsNumeric(window.OutputType(node, port))) return true;
                return false;
            }

            internal NodePreview(ShaderGraphWindow window, Canvas canvas, string nodeId)
            {
                this.window = window; this.canvas = canvas; this.nodeId = nodeId;
                source = new NodePreviewSource(window, canvas, nodeId, WantsCompilation);
                RegisterCallback<GeometryChangedEvent>(_ => canvas.InvalidatePreviewPriorities());
                AddToClassList("whimtex-shader-graph-preview");
                userData = nodeId;
                content = new VisualElement(); Add(content);
                output = new DropdownField(new List<string> { "Out" }, 0);
                output.AddToClassList("whimtex-shader-graph-preview-output");
                string DisplayOutput(string port) => ShaderGraphNodes.PortDisplayName(window.Graph?.FindNode(nodeId), port, true);
                output.formatListItemCallback = DisplayOutput;
                output.formatSelectedValueCallback = DisplayOutput;
                output.RegisterValueChangedCallback(e => ChangeOutput(e.newValue));
                output.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    e.StopImmediatePropagation(); ShowOutputMenu();
                }, TrickleDown.TrickleDown);
                output.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode is not (KeyCode.Space or KeyCode.Return or KeyCode.KeypadEnter or KeyCode.UpArrow or KeyCode.DownArrow)) return;
                    e.StopImmediatePropagation(); ShowOutputMenu();
                }, TrickleDown.TrickleDown);
                output.RegisterCallback<NavigationSubmitEvent>(e =>
                { e.StopImmediatePropagation(); ShowOutputMenu(); }, TrickleDown.TrickleDown);
                surface = new VisualElement(); surface.AddToClassList("whimtex-shader-graph-node-preview");
                surface.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
                image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("whimtex-shader-graph-node-preview-image"); surface.Add(image); content.Add(surface);
                BuildViewControls();
                var collapse = new Button(() => ChangeExpanded(false)) { name = "collapse-preview" };
                collapse.AddToClassList("whimtex-shader-graph-preview-collapse"); collapse.Add(Chevron(true)); surface.Add(collapse);
                loading = new PreviewLoadingIndicator(image); surface.Insert(1, loading);
                expand = new Button(() => ChangeExpanded(true)) { name = "expand-preview" };
                expand.AddToClassList("whimtex-shader-graph-preview-expand"); expand.Add(Chevron(false)); Add(expand);
                SetExpanded(window.Graph?.FindNode(nodeId)?.previewExpanded ?? true);
            }
            private void ShowOutputMenu()
            {
                if (output.panel == null || !output.enabledInHierarchy || output.choices.Count <= 1 ||
                    output.ClassListContains("whimtex-shader-graph-preview-output--open")) return;
                var input = output.Q(className: DropdownField.inputUssClassName);
                Rect anchor = input.worldBound;
                float menuScale = Mathf.Max(.7f, anchor.height / input.resolvedStyle.height);
                var menu = new GenericDropdownMenu();
                foreach (string port in output.choices)
                    menu.AddItem(output.formatListItemCallback(port), output.value == port, () =>
                    {
                        if (output.panel != null && output.choices.Contains(port)) output.value = port;
                    });
                output.AddToClassList("whimtex-shader-graph-preview-output--open");
                viewControls.AddToClassList("whimtex-node-preview-controls--open");
                menu.onClose += () =>
                {
                    output.RemoveFromClassList("whimtex-shader-graph-preview-output--open");
                    viewControls.RemoveFromClassList("whimtex-node-preview-controls--open");
                };
                var popup = menu.contentContainer;
                while (!popup.ClassListContains(GenericDropdownMenu.containerOuterUssClassName)) popup = popup.parent;
                popup.name = "node-preview-output-menu";
                popup.style.minWidth = anchor.width / menuScale;
                popup.style.transformOrigin = new TransformOrigin(0, 0, 0);
                popup.style.scale = new Scale(new Vector3(menuScale, menuScale, 1));
                popup.style.opacity = 0;
                void PositionMenu(GeometryChangedEvent e)
                {
                    if (e.newRect.width <= 0 || e.newRect.height <= 0 || e.newRect.size == e.oldRect.size) return;
                    popup.schedule.Execute(() =>
                    {
                        if (output.panel == null || popup.panel == null) return;
                        anchor = input.worldBound;
                        Vector2 corner = popup.parent.WorldToLocal(new Vector2(surface.worldBound.xMax, anchor.yMin));
                        float availableHeight = Mathf.Max(0, corner.y);
                        float height = Mathf.Min(popup.resolvedStyle.height * menuScale, availableHeight);
                        // Keep the native placement anchor in sync; changing styles alone is reset by menu layout.
                        menu.DropDown(new Rect(surface.worldBound.xMax - popup.resolvedStyle.width * menuScale, anchor.yMin - height, 0, 0),
                            output, DropdownMenuSizeMode.Content);
                        popup.style.maxHeight = availableHeight / menuScale;
                        popup.style.opacity = 1;
                    });
                }
                popup.RegisterCallback<GeometryChangedEvent>(PositionMenu);
                output.Focus();
                menu.DropDown(new Rect(surface.worldBound.xMax, anchor.yMin, 0, 0), output, DropdownMenuSizeMode.Content);
            }
            private static VisualElement Chevron(bool up)
            {
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("whimtex-shader-graph-preview-chevron");
                icon.generateVisualContent += context =>
                {
                    Vector2 center = icon.contentRect.center;
                    var painter = context.painter2D;
                    painter.lineWidth = 2; painter.lineCap = LineCap.Round; painter.lineJoin = LineJoin.Round;
                    painter.strokeColor = icon.resolvedStyle.color;
                    float direction = up ? -1 : 1;
                    painter.BeginPath(); painter.MoveTo(center + new Vector2(-4, -2 * direction));
                    painter.LineTo(center + new Vector2(0, 2 * direction));
                    painter.LineTo(center + new Vector2(4, -2 * direction)); painter.Stroke();
                };
                return icon;
            }
            private void ChangeExpanded(bool value)
            {
                if (window.Graph?.FindNode(nodeId) == null) return;
                window.Edit("Toggle Node Preview", graph => graph.FindNode(nodeId).previewExpanded = value,
                    structural: false, rebuild: false, layoutOnly: true);
                SyncExpanded();
            }
            internal void SyncExpanded()
            {
                bool value = window.Graph?.FindNode(nodeId)?.previewExpanded ?? true;
                if (expanded != value) SetExpanded(value);
            }
            internal void SyncPresentation()
            {
                SyncExpanded(); SyncPreviewMode(); SyncPreviewOutput();
            }
            private void ChangeOutput(string port)
            {
                if (window.Graph?.FindNode(nodeId) == null) return;
                window.Edit("Change Node Preview Output", graph => graph.FindNode(nodeId).previewOutput = port,
                    structural: false, rebuild: false, layoutOnly: true);
                SyncPreviewOutput();
                InvalidateOutput(); canvas.PrioritizePreview(nodeId);
            }
            private void SyncPreviewOutput()
            {
                string selected = window.Graph?.FindNode(nodeId)?.previewOutput;
                if (selected == null || !output.choices.Contains(selected))
                    selected = output.choices.Count == 0 ? null : output.choices[0];
                if (output.value == selected) return;
                output.SetValueWithoutNotify(selected);
                InvalidateOutput(); canvas.PrioritizePreview(nodeId);
            }
            private void SetExpanded(bool value)
            {
                canvas.InvalidatePreviewPriorities();
                expanded = value;
                content.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                expand.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                EnableInClassList("whimtex-shader-graph-preview--collapsed", !value);
                GetFirstAncestorOfType<NodeView>()?.EnableInClassList("whimtex-shader-graph-node--preview", value);
                if (value) Invalidate();
                else { CancelPending(); ReleaseHeight(); loading.SetUpdating(false); }
            }
            void CancelPending() => source.CancelPending();
            internal void InvalidateCode(ulong next) { source.InvalidateCode(next); SyncSource(CanDisplay()); }
            private void InvalidateOutput() { source.InvalidateOutput(); SyncSource(CanDisplay()); }
            internal void Invalidate(bool imageChanged = true)
            { source.Invalidate(imageChanged); loading.SetUpdating(CanDisplay()); }
            internal void Suspend()
            {
                ReleaseHeight(); image.image = null;
                source.Suspend(); SyncSource(false);
            }
            internal void RefreshOutputs()
            {
                var node = window.Graph?.FindNode(nodeId);
                var choices = new List<string>();
                if (node != null) foreach (string port in ShaderGraphNodes.Outputs(node))
                    if (ShaderGraphNodes.IsNumeric(window.OutputType(node, port))) choices.Add(port);
                output.choices = choices;
                output.EnableInClassList("whimtex-hidden", choices.Count <= 1);
                SyncPresentation();
            }
            internal float CompilationPriority => source.CompilationPriority;
            internal void UpdatePriority()
            {
                bool visible = CanDisplay();
                source.UpdatePriority(canvas.PreviewPriority(nodeId), visible);
                if (!visible) { loading.SetUpdating(false); ReleaseHeight(); }
            }
            private void SyncSource(bool visible)
            {
                if (sourceRevision != source.ImageRevision)
                {
                    sourceRevision = source.ImageRevision;
                    canvas.SetMessages("presentation:" + nodeId, null);
                    if (source.HasImage) PreviewSourceChanged();
                    else { image.image = null; ReleaseHeight(); }
                }
                if (messagesRevision != source.MessagesRevision)
                {
                    messagesRevision = source.MessagesRevision; SetMessages(source.Messages);
                    canvas.SetMessages("presentation:" + nodeId, null);
                }
                SetDisplayedBackend(source.DisplayedSVM);
                loading.SetUpdating(source.IsUpdating && visible);
            }
            internal bool Tick(ShaderGraphPreviewState state)
            {
                bool visible = CanDisplay();
                bool changed = source.Tick(state, output.value, visible ? PreviewSize() : 0, visible);
                SyncSource(visible);
                if (presentationDirty && source.HasImage && visible)
                {
                    try { PresentPreview(); }
                    catch (Exception error)
                    {
                        ReleaseHeight(); viewState.heightMode = false; UpdateViewControls();
                        image.image = source.Texture; presentationDirty = false;
                        canvas.SetMessages("presentation:" + nodeId,
                            new[] { new ShaderGraphMessage(nodeId, "Height preview: " + error.Message, ShaderGraphMessageLevel.Warning) });
                    }
                    changed = true;
                }
                return changed;
            }
            private int PreviewSize()
            {
                float pixels = Mathf.Max(surface.worldBound.width, surface.worldBound.height) * EditorGUIUtility.pixelsPerPoint;
                int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(pixels)), 16, Size);
                // Avoid reallocating repeatedly while zoom oscillates around a bucket boundary.
                if (source.Texture != null && size < source.Texture.width && pixels > source.Texture.width * .4f) return source.Texture.width;
                return size;
            }
            private void SetMessages(IReadOnlyList<ShaderGraphMessage> messages)
            {
                // A preview-only device/render failure still belongs to the preview's node.
                List<ShaderGraphMessage> local = null;
                for (int i = 0; i < messages.Count; i++) if (messages[i].nodeId == null)
                {
                    local ??= new List<ShaderGraphMessage>(messages);
                    local[i] = new ShaderGraphMessage(nodeId, messages[i].text, messages[i].level);
                }
                canvas.SetMessages("preview:" + nodeId, local ?? messages);
            }
            private bool CanDisplay()
            {
                if (!expanded || panel == null || surface.contentRect.width < 1) return false;
                Rect bounds = surface.worldBound;
                for (VisualElement element = this; element != null; element = element.parent)
                {
                    if (element.resolvedStyle.display == DisplayStyle.None || element.resolvedStyle.visibility == Visibility.Hidden) return false;
                    if (element is ScrollView scroll && !scroll.contentViewport.worldBound.Overlaps(bounds)) return false;
                }
                return canvas.worldBound.Overlaps(bounds) && window.rootVisualElement.worldBound.Overlaps(bounds);
            }
            private bool WantsCompilation() => canvas.PreviewStateIsCurrent && CanDisplay();
            public void Dispose()
            {
                ReleaseHeight(); image.image = null;
                source.Dispose(); SyncSource(false);
            }
        }
    }
}
