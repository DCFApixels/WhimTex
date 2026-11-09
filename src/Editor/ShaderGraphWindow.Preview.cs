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
        [Flags]
        private enum PreviewAnchor { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

        private sealed class LayerPreviewOverlay : VisualElement
        {
            private const float SnapDistance = 16f;
            private static readonly Vector2 MinimumSize = new Vector2(200, 100);
            private readonly ShaderGraphWindow window;
            private VisualElement viewport;
            private Rect bounds;

            internal LayerPreviewOverlay(ShaderGraphWindow window, LayerPreviewPanel preview)
            {
                this.window = window;
                name = "shader-layer-preview";
                usageHints = UsageHints.DynamicTransform;
                AddToClassList("whimtex-shader-graph-layer-preview");
                Add(preview);
                this.AddManipulator(new PreviewGesture(this, 0, preview.Header.Q("layer-preview-channels")));
                AddHandle("left", 1); AddHandle("right", 2);
                AddHandle("top", 4); AddHandle("bottom", 8);
                AddHandle("top-left", 5); AddHandle("top-right", 6);
                AddHandle("bottom-left", 9); AddHandle("bottom-right", 10);
                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    viewport = parent;
                    viewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometry);
                    Place();
                });
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    viewport?.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometry);
                    viewport = null;
                });
            }

            private void AddHandle(string side, int edges)
            {
                var handle = new VisualElement { name = "preview-resize-" + side };
                handle.AddToClassList("whimtex-shader-graph-preview-resize");
                handle.AddToClassList("whimtex-shader-graph-preview-resize--" + side);
                handle.AddManipulator(new PreviewGesture(this, edges, null));
                Add(handle);
            }

            private void OnViewportGeometry(GeometryChangedEvent evt) => Place();
            private Vector2 Available => viewport?.contentRect.size ?? Vector2.zero;
            private Vector2 Minimum => Vector2.Min(MinimumSize, Available);
            private PreviewAnchor Anchor => window.previewPositioned ? window.previewAnchor : PreviewAnchor.Right | PreviewAnchor.Bottom;
            private void Place()
            {
                Vector2 available = Available;
                if (!ProjectiveMatrix.Finite(available.x) || !ProjectiveMatrix.Finite(available.y) || available.x <= 0 || available.y <= 0) return;
                Rect next = window.previewBounds;
                next.size = Vector2.Min(Vector2.Max(next.size, Minimum), available);
                Vector2 room = available - next.size;
                PreviewAnchor anchor = Anchor;
                if ((anchor & PreviewAnchor.Left) != 0) next.x = 0;
                else if ((anchor & PreviewAnchor.Right) != 0) next.x = room.x;
                if ((anchor & PreviewAnchor.Top) != 0) next.y = 0;
                else if ((anchor & PreviewAnchor.Bottom) != 0) next.y = room.y;
                next.x = Mathf.Clamp(next.x, 0, available.x - next.width);
                next.y = Mathf.Clamp(next.y, 0, available.y - next.height);
                EnableInClassList("whimtex-shader-graph-preview--left", (anchor & PreviewAnchor.Left) != 0);
                EnableInClassList("whimtex-shader-graph-preview--right", (anchor & PreviewAnchor.Right) != 0);
                EnableInClassList("whimtex-shader-graph-preview--top", (anchor & PreviewAnchor.Top) != 0);
                EnableInClassList("whimtex-shader-graph-preview--bottom", (anchor & PreviewAnchor.Bottom) != 0);
                bounds = next;
                style.width = next.width; style.height = next.height;
                style.translate = new Translate(next.x, next.y);
            }

            private void Drag(Rect start, Vector2 delta, int edges)
            {
                Rect next = start;
                if (edges == 0)
                {
                    Vector2 position = start.position + delta, room = Available - next.size;
                    window.previewAnchor = SnapAxis(ref position.x, room.x, PreviewAnchor.Left, PreviewAnchor.Right)
                        | SnapAxis(ref position.y, room.y, PreviewAnchor.Top, PreviewAnchor.Bottom);
                    next.position = position;
                }
                else
                {
                    window.previewAnchor = Anchor & ~(PreviewAnchor)edges;
                    Vector2 min = Minimum, available = Available;
                    if ((edges & 1) != 0) next.xMin = Mathf.Clamp(start.xMin + delta.x, 0, start.xMax - min.x);
                    if ((edges & 2) != 0) next.xMax = Mathf.Clamp(start.xMax + delta.x, start.xMin + min.x, available.x);
                    if ((edges & 4) != 0) next.yMin = Mathf.Clamp(start.yMin + delta.y, 0, start.yMax - min.y);
                    if ((edges & 8) != 0) next.yMax = Mathf.Clamp(start.yMax + delta.y, start.yMin + min.y, available.y);
                }
                window.previewPositioned = true;
                window.previewBounds = next;
                Place();
                window.previewBounds = bounds;
            }

            private static PreviewAnchor SnapAxis(ref float position, float room, PreviewAnchor near, PreviewAnchor far)
            {
                room = Mathf.Max(0, room);
                position = Mathf.Clamp(position, 0, room);
                float nearDistance = position, farDistance = room - position;
                if (Mathf.Min(nearDistance, farDistance) > SnapDistance) return PreviewAnchor.None;
                if (nearDistance <= farDistance) { position = 0; return near; }
                position = room; return far;
            }

            private sealed class PreviewGesture : PointerManipulator
            {
                private readonly LayerPreviewOverlay overlay;
                private readonly int edges;
                private readonly VisualElement ignore;
                private int pointer = -1;
                private Vector2 origin;
                private Rect start;
                private bool dragging;
                internal PreviewGesture(LayerPreviewOverlay overlay, int edges, VisualElement ignore)
                { this.overlay = overlay; this.edges = edges; this.ignore = ignore; }
                protected override void RegisterCallbacksOnTarget()
                {
                    target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                    target.RegisterCallback<PointerMoveEvent>(Move);
                    target.RegisterCallback<PointerUpEvent>(Up);
                    target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                    target.RegisterCallback<DetachFromPanelEvent>(Detached);
                }
                protected override void UnregisterCallbacksFromTarget()
                {
                    Release();
                    target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                    target.UnregisterCallback<PointerMoveEvent>(Move);
                    target.UnregisterCallback<PointerUpEvent>(Up);
                    target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                    target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                }
                private void Down(PointerDownEvent e)
                {
                    if (e.button != 0 || pointer >= 0 || overlay.viewport == null) return;
                    var element = e.target as VisualElement;
                    if (ignore != null && (element == ignore || ignore.Contains(element))) return;
                    if (edges == 0 && element?.ClassListContains("whimtex-shader-graph-preview-resize") == true) return;
                    pointer = e.pointerId;
                    dragging = false;
                    origin = overlay.viewport.WorldToLocal(e.position); start = overlay.bounds;
                    overlay.BringToFront(); target.CapturePointer(pointer); e.StopPropagation();
                }
                private void Move(PointerMoveEvent e)
                {
                    if (e.pointerId != pointer || !target.HasPointerCapture(pointer)) return;
                    Vector2 delta = overlay.viewport.WorldToLocal(e.position) - origin;
                    dragging |= delta.sqrMagnitude >= 1;
                    if (dragging) overlay.Drag(start, delta, edges);
                    e.StopPropagation();
                }
                private void Up(PointerUpEvent e)
                {
                    if (e.pointerId != pointer || e.button != 0) return;
                    Release(); e.StopPropagation();
                }
                private void Lost(PointerCaptureOutEvent e) => pointer = -1;
                private void Detached(DetachFromPanelEvent e) => Release();
                private void Release()
                {
                    int id = pointer; pointer = -1;
                    if (id >= 0 && target.HasPointerCapture(id)) target.ReleasePointer(id);
                }
            }
        }

        private sealed class NodePreview : VisualElement, IDisposable
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
            private ShaderProgramRuntime program;
            private ShaderGraphPreviewVM svm;
            private bool svmAttempted;
            private int svmRevision;
            private Func<bool> svmWanted;
            private bool svmRendered;
            private VisualElement backendIndicator;
            private bool displayedSVM;
            private ulong svmCode, svmValues;
            private string svmPort;
            private ShaderProgramRuntime.CompilationRequest pending;
            private ulong programFingerprint;
            private string programPort;
            private double renderAt;
            private RenderTexture texture;
            private readonly string nodeId;
            private string compiledPort;
            private ulong fingerprint;
            private ulong renderedFrame;
            private bool dirty = true, attempted, disposed;
            private bool renderError;
            private int attemptedGeneration;
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
                    ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.Constant or ShaderGraphNodeKind.InstanceIndex or
                    ShaderGraphNodeKind.Time or ShaderGraphNodeKind.ForBegin or ShaderGraphNodeKind.CanvasData or
                    ShaderGraphNodeKind.TextureSize or ShaderGraphNodeKind.Split or ShaderGraphNodeKind.Compare) return false;
                foreach (string port in ShaderGraphNodes.Outputs(node))
                    if (ShaderGraphNodes.IsNumeric(window.OutputType(node, port))) return true;
                return false;
            }

            internal NodePreview(ShaderGraphWindow window, Canvas canvas, string nodeId)
            {
                this.window = window; this.canvas = canvas; this.nodeId = nodeId;
                RegisterCallback<GeometryChangedEvent>(_ => canvas.InvalidatePreviewPriorities());
                AddToClassList("whimtex-shader-graph-preview");
                userData = nodeId;
                content = new VisualElement(); Add(content);
                output = new DropdownField(new List<string> { "Out" }, 0);
                output.AddToClassList("whimtex-shader-graph-preview-output");
                string DisplayOutput(string port) => ShaderGraphNodes.PortDisplayName(window.Graph?.FindNode(nodeId), port, true);
                output.formatListItemCallback = DisplayOutput;
                output.formatSelectedValueCallback = DisplayOutput;
                output.RegisterValueChangedCallback(_ => { CancelPending(); canvas.PrioritizePreview(nodeId); Invalidate(); });
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
                surface.Add(output);
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
                menu.onClose += () => output.RemoveFromClassList("whimtex-shader-graph-preview-output--open");
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
            private void SetExpanded(bool value)
            {
                canvas.InvalidatePreviewPriorities();
                expanded = value;
                content.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                expand.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                EnableInClassList("whimtex-shader-graph-preview--collapsed", !value);
                GetFirstAncestorOfType<NodeView>()?.EnableInClassList("whimtex-shader-graph-node--preview", value);
                if (value) Invalidate();
                else { CancelPending(); loading.SetUpdating(false); }
            }
            void CancelPending()
            {
                if (svm?.IsHybridPending == true) { svm.Dispose(); svm = null; svmAttempted = false; }
                if (pending == null) return;
                // The shared executable cache owns native work that has already started.
                // Releasing this consumer cancels queued work without stopping another consumer.
                pending.Dispose(); pending = null; attempted = false;
            }
            internal void InvalidateCode(ulong next)
            {
                if (pending != null && fingerprint != next) CancelPending();
                Invalidate();
            }
            internal void Invalidate(bool imageChanged = true)
            {
                dirty |= imageChanged;
                loading.SetUpdating(CanDisplay());
            }
            internal void Suspend() { Release(); dirty = true; }
            internal void RefreshOutputs()
            {
                SyncExpanded();
                var node = window.Graph?.FindNode(nodeId);
                var choices = new List<string>();
                if (node != null) foreach (string port in ShaderGraphNodes.Outputs(node))
                    if (ShaderGraphNodes.IsNumeric(window.OutputType(node, port))) choices.Add(port);
                string previous = output.value;
                output.choices = choices;
                if (!choices.Contains(output.value)) output.SetValueWithoutNotify(choices.Count == 0 ? null : choices[0]);
                output.EnableInClassList("whimtex-hidden", choices.Count <= 1);
                if (output.value != previous) { CancelPending(); Invalidate(); }
            }
            ulong renderedValues;
            internal float CompilationPriority { get; private set; }
            internal void UpdatePriority()
            {
                CompilationPriority = canvas.PreviewPriority(nodeId);
                pending?.SetPreviewPriority(CompilationPriority);
                svm?.SetHybridPriority(CompilationPriority, svmWanted ??= WantsCompilation);
                if (!CanDisplay()) { loading.SetUpdating(false); CancelPending(); }
            }
            internal bool Tick(ShaderGraphPreviewState state)
            {
                if (disposed) return false;
                ulong next = Fingerprint(state);
                bool visible = CanDisplay();
                loading.SetUpdating(visible && (!renderError || attemptedGeneration != canvas.PreparationGeneration) &&
                    (dirty || texture == null || pending != null || programFingerprint != next || programPort != output.value));
                bool completed = false;
                try
                {
                    if (visible && window.svmPreviews && TickVM(state, next, out bool vmChanged)) return vmChanged;
                    if (!window.svmPreviews && svm != null) { svm.Dispose(); svm = null; svmAttempted = false; dirty = true; }
                    if (pending != null &&
                        (fingerprint != next || compiledPort != output.value || !visible))
                        CancelPending();
                    if (pending != null)
                    {
                        if (!pending.TryComplete(out var compiled)) return false;
                        pending.Dispose(); pending = null;
                        if (fingerprint == next && compiledPort == output.value)
                        {
                            program?.Dispose(); program = compiled;
                            programFingerprint = fingerprint; programPort = compiledPort;
                            dirty = completed = true;
                            SetMessages(program.Messages);
                        }
                        else { compiled.Dispose(); attempted = false; }
                    }
                    if (!visible) return false;
                    ulong values = state.ValueFingerprint(nodeId);
                    dirty |= values != renderedValues;
                    if (dirty) loading.SetUpdating(true);
                    // Returning to the retained output does not need a new preparation job.
                    if (pending == null && program != null && !renderError && programFingerprint == next && programPort == output.value)
                    { attempted = true; fingerprint = next; compiledPort = output.value; }
                    bool compile = !attempted || next != fingerprint || output.value != compiledPort ||
                        renderError && attemptedGeneration != canvas.PreparationGeneration;
                    if (compile)
                    {
                        var current = window.Graph?.FindNode(nodeId);
                        if (current == null) { Release(); return false; }
                        attempted = true; fingerprint = next; compiledPort = output.value;
                        attemptedGeneration = canvas.PreparationGeneration;
                        using var profile = WhimTexProfiler.Measure("shadergraph.preview.compile", window.document);
                        pending = new ShaderProgramRuntime.CompilationRequest(canvas.Preparation(), nodeId, output.value,
                            simplifiedLighting: !window.accurateLighting).WithLighting(window.document);
                        pending.SetPreviewPriority(CompilationPriority);
                        pending.SetPreviewDemand(WantsCompilation);
                        return true;
                    }
                    if (program == null || programFingerprint != next || programPort != output.value) { dirty = false; return false; }
                    int size = PreviewSize();
                    bool resize = texture == null || texture.width != size;
                    bool animated = program.UsesTime || window.Target.IsEffect && window.Target.Layer.UsesTime;
                    double now = EditorApplication.timeSinceStartup;
                    if (!dirty && !resize && (!animated || now < renderAt || renderedFrame == ShaderGraphClock.Frame)) return completed;
                    var previous = RenderTexture.active;
                    bool srgb = GL.sRGBWrite;
                    try
                    {
                        using var profile = WhimTexProfiler.Measure("shadergraph.preview.render", window.document);
                        if (resize)
                        {
                            image.image = null;
                            if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                            texture = CreateTexture(size);
                        }
                        var host = window.Target;
                        var context = new LayerRenderContext(window.document, null, size, size,
                            (float)Mathf.Max(window.document.width, window.document.height) / size, applyTransform: false, applyFx: false,
                            transformFxCoordinates: host.IsEffect && !host.Layer.IsGroup)
                            .WithShaderGraphPreviewTime(ShaderGraphClock.Seconds);
                        if (host.IsEffect) program.BindEffect(window.Graph, context, state.bindings, branchCurrent: true);
                        else
                        {
                            program.Bind(window.Graph, context, state.bindings, branchCurrent: true);
                            program.PrepareCoordinates(host.Layer, context);
                        }
                        using var input = host.IsEffect
                            ? new ShaderLayerInput(canvas.BorrowFXPreviewInput(context), context, owned: false)
                            : window.document.ResolveShaderInput(host.Behaviour, context);
                        if (host.IsEffect) host.Layer.BindShaderFXCoordinates(program.Material, context);
                        var inputTexture = input.Bind(program.Material, program.EmptyInput);
                        using var bindings = window.document.BindShaderTextureLayers(program.Parameters, host.Layer, context,
                            host.IsEffect ? input.Texture as RenderTexture : null);
                        bindings?.Apply(program.Material);
                        GL.sRGBWrite = false;
                        program.Render(inputTexture, texture, context);
                        image.image = texture;
                        SetDisplayedBackend(false);
                        image.MarkDirtyRepaint();
                        if (renderError) SetMessages(program.Messages);
                        renderError = false; dirty = false;
                        loading.SetUpdating(false);
                        renderedValues = values;
                        renderedFrame = ShaderGraphClock.Frame;
                        renderAt = now + (EditorWindow.focusedWindow == window ? 1d / 30 : .1d);
                    }
                    finally { program.Material.SetTexture("_MainTex", null); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
                    return true;
                }
                catch (Exception error)
                {
                    pending?.Dispose(); pending = null;
                    if (attemptedGeneration != canvas.PreparationGeneration)
                    { attempted = false; return true; }
                    renderError = true; dirty = false; renderedValues = state.ValueFingerprint(nodeId);
                    loading.SetUpdating(false);
                    image.image = null;
                    SetDisplayedBackend(false);
                    SetMessages((error as ShaderGraphException)?.messages ??
                        new[] { new ShaderGraphMessage(nodeId, error.Message, ShaderGraphMessageLevel.Error) });
                    return true;
                }
            }
            private bool TickVM(ShaderGraphPreviewState state, ulong next, out bool changed)
            {
                changed = false;
                ulong values = state.ValueFingerprint(nodeId);
                if (!svmAttempted || svmRevision != ShaderProgramSourceRevision.Current || svmCode != next || svmValues != values || svmPort != output.value)
                {
                    bool updated = svm != null && svmAttempted && svmRevision == ShaderProgramSourceRevision.Current &&
                        svmCode == next && svmPort == output.value && svm.TryUpdateValues(window.Graph, state.bindings);
                    svmAttempted = true; svmRevision = ShaderProgramSourceRevision.Current; svmCode = next; svmValues = values; svmPort = output.value;
                    if (!updated)
                    {
                        var prepared = ShaderGraphPreviewVM.BeginHybrid(window.Graph, nodeId, output.value, !window.accurateLighting, effect: window.Target.IsEffect);
                        if (svm != null && prepared != null) { svm.ReplaceCode(prepared); prepared.Dispose(); }
                        else { svm?.Dispose(); svm = prepared; }
                    }
                    dirty = true; svmRendered = false;
                    loading.SetUpdating(true);
                }
                if (svm == null) return false;
                if (svm.HasBoundaries && window.Target.IsEffect) { svm.Dispose(); svm = null; return false; }
                svm.ConfigureHybrid(window.document, window.Target.Layer, effect:window.Target.IsEffect);
                svm.SetHybridPriority(CompilationPriority, svmWanted ??= WantsCompilation);
                if (!svm.Ready)
                {
                    // A supported program waiting for its shared executor does not
                    // also need a per-node native shader compilation.
                    if (!svm.Failed) return true;
                    svm.Dispose(); svm = null; return false;
                }
                pending?.Dispose(); pending = null;
                program?.Dispose(); program = null; attempted = false;
                dirty |= !svmRendered;
                int size = PreviewSize();
                bool resize = texture == null || texture.width != size;
                double now = EditorApplication.timeSinceStartup;
                bool animated=svm.UsesTime || window.Target.IsEffect && window.Target.Layer.UsesTime;
                if (!dirty && !resize && (!animated || now < renderAt || renderedFrame == ShaderGraphClock.Frame))
                { loading.SetUpdating(false); return true; }
                if (resize)
                {
                    image.image = null;
                    if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                    texture = CreateTexture(size);
                }
                var vmContext = new LayerRenderContext(window.document, null, size, size,
                    (float)Mathf.Max(window.document.width, window.document.height) / size, applyTransform: false, applyFx: false,
                    transformFxCoordinates: window.Target.IsEffect && !window.Target.Layer.IsGroup)
                    .WithShaderGraphPreviewTime(ShaderGraphClock.Seconds);
                bool rendered=window.Target.IsEffect
                    ? svm.RenderEffectPreview(texture,vmContext,svm.NeedsInput?canvas.BorrowFXPreviewInput(vmContext):null)
                    : svm.RenderBoundaries(texture,vmContext);
                if (!rendered) return true;
                image.image = texture; image.MarkDirtyRepaint();
                SetDisplayedBackend(true);
                SetMessages(Array.Empty<ShaderGraphMessage>());
                programFingerprint = next; programPort = output.value;
                renderedValues = values; renderedFrame = ShaderGraphClock.Frame;
                dirty = renderError = false;
                loading.SetUpdating(false);
                svmRendered = true;
                renderAt = now + (EditorWindow.focusedWindow == window ? 1d / 30 : .1d);
                changed = true;
                return true;
            }
            private int PreviewSize()
            {
                float pixels = Mathf.Max(surface.worldBound.width, surface.worldBound.height) * EditorGUIUtility.pixelsPerPoint;
                int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(pixels)), 16, Size);
                // Avoid reallocating repeatedly while zoom oscillates around a bucket boundary.
                if (texture != null && size < texture.width && pixels > texture.width * .4f) return texture.width;
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
            private static RenderTexture CreateTexture(int size)
            {
                var result = ProfiledRenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                result.filterMode = FilterMode.Bilinear; result.wrapMode = TextureWrapMode.Clamp;
                return result;
            }
            private void Release()
            {
                SetDisplayedBackend(false);
                loading.SetUpdating(false);
                svm?.Dispose(); svm = null; svmAttempted = svmRendered = false;
                image.image = null; pending?.Dispose(); pending = null; program?.Dispose(); program = null;
                if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                texture = null; attempted = false;
            }
            public void Dispose() { if (disposed) return; disposed = true; Release(); }
        }
    }
}
