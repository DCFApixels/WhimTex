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
            private readonly VisualElement content;
            private readonly Button expand;
            private ShaderGraphProgram program;
            private ShaderGraphProgram.CompilationRequest pending;
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
            private bool expanded = true;
            internal bool Expanded => expanded;

            internal static bool Supports(ShaderGraphWindow window, ShaderGraphNode node)
            {
                if (node == null || ShaderGraphNodes.IsSubGraphInterface(node.kind) || node.kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or
                    ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.Constant or ShaderGraphNodeKind.InstanceIndex or
                    ShaderGraphNodeKind.Time or ShaderGraphNodeKind.CanvasData or
                    ShaderGraphNodeKind.TextureSize or ShaderGraphNodeKind.Split or ShaderGraphNodeKind.Compare) return false;
                foreach (string port in ShaderGraphNodes.Outputs(node))
                    if (ShaderGraphNodes.IsNumeric(window.OutputType(node, port))) return true;
                return false;
            }

            internal NodePreview(ShaderGraphWindow window, Canvas canvas, string nodeId)
            {
                this.window = window; this.canvas = canvas; this.nodeId = nodeId;
                AddToClassList("whimtex-shader-graph-preview");
                userData = nodeId;
                content = new VisualElement(); Add(content);
                output = new DropdownField("Output", new List<string> { "Out" }, 0);
                output.AddToClassList("whimtex-shader-graph-node-field");
                string DisplayOutput(string port) => ShaderGraphNodes.PortDisplayName(window.Graph?.FindNode(nodeId), port, true);
                output.formatListItemCallback = DisplayOutput;
                output.formatSelectedValueCallback = DisplayOutput;
                output.RegisterValueChangedCallback(_ => dirty = true); content.Add(output);
                surface = new VisualElement(); surface.AddToClassList("whimtex-shader-graph-node-preview");
                surface.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
                image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("whimtex-shader-graph-node-preview-image"); surface.Add(image); content.Add(surface);
                var collapse = new Button(() => ChangeExpanded(false)) { name = "collapse-preview" };
                collapse.AddToClassList("whimtex-shader-graph-preview-collapse"); collapse.Add(Chevron(true)); surface.Add(collapse);
                expand = new Button(() => ChangeExpanded(true)) { name = "expand-preview" };
                expand.AddToClassList("whimtex-shader-graph-preview-expand"); expand.Add(Chevron(false)); Add(expand);
                SetExpanded(window.Graph?.FindNode(nodeId)?.previewExpanded ?? true);
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
                expanded = value;
                content.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                expand.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                EnableInClassList("whimtex-shader-graph-preview--collapsed", !value);
                GetFirstAncestorOfType<NodeView>()?.EnableInClassList("whimtex-shader-graph-node--preview", value);
                if (value) dirty = true;
            }
            internal void Invalidate() => dirty = true;
            internal void RefreshOutputs()
            {
                SyncExpanded();
                var node = window.Graph?.FindNode(nodeId);
                var choices = new List<string>();
                if (node != null) foreach (string port in ShaderGraphNodes.Outputs(node))
                    if (ShaderGraphNodes.IsNumeric(window.OutputType(node, port))) choices.Add(port);
                output.choices = choices;
                if (!choices.Contains(output.value)) output.SetValueWithoutNotify(choices.Count == 0 ? null : choices[0]);
                output.EnableInClassList("whimtex-hidden", choices.Count <= 1);
                dirty = true;
            }
            internal bool Tick(ShaderGraphPreviewState state)
            {
                if (disposed) return false;
                ulong next = state.Fingerprint(nodeId);
                bool completed = false;
                try
                {
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
                    if (!CanDisplay()) return false;
                    bool compile = !attempted || next != fingerprint || output.value != compiledPort;
                    if (compile)
                    {
                        if (ShaderGraphProgram.CompilationRequest.ActiveCount >= 2) return false;
                        var current = window.Graph?.FindNode(nodeId);
                        if (current == null) { Release(); return false; }
                        attempted = true; fingerprint = next; compiledPort = output.value;
                        using var profile = WhimTexProfiler.Measure("shadergraph.preview.compile", window.document);
                        pending = new ShaderGraphProgram.CompilationRequest(window.Graph, nodeId, output.value);
                        return true;
                    }
                    if (program == null || programFingerprint != next || programPort != output.value) { dirty = false; return false; }
                    int size = PreviewSize();
                    bool resize = texture == null || texture.width != size;
                    bool animated = program.UsesTime;
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
                        var context = new LayerRenderContext(window.document, null, size, size,
                            (float)Mathf.Max(window.document.width, window.document.height) / size, applyTransform: false, applyFx: false)
                            .WithShaderGraphPreviewTime(ShaderGraphClock.Seconds);
                        program.Bind(window.Graph, context, state.bindings, branchCurrent: true);
                        program.PrepareCoordinates(window.Behaviour.Owner, context);
                        using var input = window.document.ResolveShaderInput(window.Behaviour, context);
                        var inputTexture = input.Bind(program.Material, program.EmptyInput);
                        using var bindings = window.document.BindShaderTextureLayers(program.Parameters, window.Behaviour.Owner, context, null);
                        bindings?.Apply(program.Material);
                        GL.sRGBWrite = false;
                        Graphics.Blit(inputTexture, texture, program.Material, 0);
                        image.image = texture;
                        image.MarkDirtyRepaint();
                        if (renderError) SetMessages(program.Messages);
                        renderError = false; dirty = false;
                        renderedFrame = ShaderGraphClock.Frame;
                        renderAt = now + (EditorWindow.focusedWindow == window ? 1d / 30 : .1d);
                    }
                    finally { program.Material.SetTexture("_MainTex", null); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
                    return true;
                }
                catch (Exception error)
                {
                    pending?.Dispose(); pending = null;
                    renderError = true; dirty = false;
                    image.image = null;
                    SetMessages((error as ShaderGraphException)?.messages ??
                        new[] { new ShaderGraphMessage(nodeId, error.Message, ShaderGraphMessageLevel.Error) });
                    return true;
                }
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
            private static RenderTexture CreateTexture(int size)
            {
                var result = ProfiledRenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                result.filterMode = FilterMode.Bilinear; result.wrapMode = TextureWrapMode.Clamp;
                return result;
            }
            private void Release()
            {
                image.image = null; pending?.Dispose(); pending = null; program?.Dispose(); program = null;
                if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                texture = null; attempted = false;
            }
            public void Dispose() { if (disposed) return; disposed = true; Release(); }
        }
    }
}
