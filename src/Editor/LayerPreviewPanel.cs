using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    /// <summary>Shared, bottom-docked layer preview. View state belongs to the window, not the document.</summary>
    internal sealed class LayerPreviewPanel : VisualElement, IDisposable
    {
        [Serializable]
        internal sealed class ViewState
        {
            public float height = 220f;
            public bool collapsed;
            public int channelMask = 15;
        }

        private const float HeaderHeight = 20f;
        private const int PreviewSize = 256;
        private const float MaximumHeight = 256f;
        private readonly ViewState state;
        private readonly VisualElement header, surface;
        private readonly Image image;
        private readonly VisualElement channels;
        private readonly Button[] channelButtons = new Button[4];
        private readonly ResizeManipulator resize;
        private TextureCompositor document;
        private Layer boundLayer;
        private LayerBehaviour boundBehaviour;
        private string layerId;
        private RenderTexture source, channelTexture;
        private Material channelMaterial;
        private bool dirty = true, attached, disposed;
        private bool awaitingCanvasRender;
        private double renderAt;
        private VisualElement layoutParent;

        internal LayerPreviewPanel(ViewState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            name = "layer-preview";
            AddToClassList("whimtex-output-preview-footer");
            header = new VisualElement { name = "layer-preview-resizer", tooltip = "Drag up to reveal the layer preview; drag down to hide it." };
            header.AddToClassList("whimtex-output-preview-title");
            header.Add(new Label("Layer Preview") { pickingMode = PickingMode.Ignore });
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList("whimtex-output-preview-grip");
            header.Add(grip);
            state.channelMask &= 15;
            channels = new VisualElement { name = "layer-preview-channels" };
            channels.AddToClassList("whimtex-layer-preview-channels");
            string[] labels = { "R", "G", "B", "A" };
            for (int i = 0; i < labels.Length; i++)
            {
                int bit = 1 << i;
                var button = new Button(() => ToggleChannel(bit))
                {
                    name = "layer-preview-channel-" + labels[i].ToLowerInvariant(), text = labels[i],
                    tooltip = i == 3
                        ? "Alpha: off ignores transparency; enable only A to view alpha in grayscale. Layer preview only; does not affect painting or output."
                        : labels[i] + " channel: toggle in the layer preview. A single RGB channel is shown in grayscale; A controls transparency. Does not affect painting or output."
                };
                button.AddToClassList("whimtex-channel-button");
                button.AddToClassList("whimtex-layer-preview-channel");
                button.EnableInClassList("whimtex-channel-button--enabled", (state.channelMask & bit) != 0);
                channelButtons[i] = button;
                channels.Add(button);
            }
            channels.AddManipulator(new ChannelDragManipulator(channelButtons, () => state.channelMask, ToggleChannel));
            channels.SetEnabled(false);
            header.Add(channels);
            resize = new ResizeManipulator(this);
            header.AddManipulator(resize);
            Add(header);
            surface = new VisualElement { name = "layer-preview-surface" };
            surface.AddToClassList("whimtex-output-preview-surface");
            image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("whimtex-output-preview-image");
            surface.Add(image);
            surface.generateVisualContent += DrawCheckerboard;
            surface.RegisterCallback<GeometryChangedEvent>(_ => surface.MarkDirtyRepaint());
            Add(surface);
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);
            UpdateLayout();
        }

        internal void Bind(TextureCompositor owner, Layer layer)
        {
            if (disposed || (document == owner && ReferenceEquals(boundLayer, layer) && ReferenceEquals(boundBehaviour, layer?.Behaviour))) return;
            if (!ReferenceEquals(document, null)) document.LayerPreviewRendered -= OnLayerPreviewRendered;
            ReleaseTextures();
            document = owner;
            if (attached && document != null) document.LayerPreviewRendered += OnLayerPreviewRendered;
            boundLayer = layer;
            boundBehaviour = layer?.Behaviour;
            layerId = layer?.Id;
            channels.SetEnabled(owner != null && layer != null && layer.Behaviour != null && !(layer.Behaviour is PendingLayerBehaviour));
            RequestLayerPreview(true);
            UpdateLayout();
        }

        internal void RequestLayerPreview(bool immediate = false)
        {
            if (disposed) return;
            // Coalesce edits without indefinitely postponing a preview during continuous dragging.
            double next = EditorApplication.timeSinceStartup + (immediate ? 0d : .12d);
            renderAt = dirty ? Math.Min(renderAt, next) : next;
            dirty = true;
            awaitingCanvasRender = false;
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            if (disposed || attached) return;
            attached = true;
            layoutParent = parent;
            layoutParent?.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            TextureCompositor.Changed += OnChanged;
            TextureCompositor.RenderResourcesChanged += OnChanged;
            if (document != null) document.LayerPreviewRendered += OnLayerPreviewRendered;
            EditorApplication.update += Tick;
            RequestLayerPreview(true);
            UpdateLayout();
        }

        private void OnDetach(DetachFromPanelEvent evt) => Stop();
        private void OnGeometry(GeometryChangedEvent evt) => UpdateLayout();
        private void OnChanged(TextureCompositor changed) { if (changed == document) RequestLayerPreview(); }

        private bool CanDisplay()
        {
            if (disposed || !attached || state.collapsed || resolvedStyle.height <= HeaderHeight) return false;
            for (VisualElement ancestor = this; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        private void OnLayerPreviewRendered(Layer layer, RenderTexture pixels)
        {
            if (!ReferenceEquals(layer, boundLayer) || !CanDisplay()) return;
            try { AcceptCachedLayerPreview(pixels); }
            catch (Exception ex) { surface.tooltip = "Layer preview unavailable: " + ex.Message; Debug.LogException(ex); }
        }

        private void AcceptCachedLayerPreview(RenderTexture pixels)
        {
            float scale = Mathf.Min(1f, (float)PreviewSize / Mathf.Max(pixels.width, pixels.height));
            var descriptor = pixels.descriptor;
            descriptor.width = Mathf.Max(1, Mathf.RoundToInt(pixels.width * scale));
            descriptor.height = Mathf.Max(1, Mathf.RoundToInt(pixels.height * scale));
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = descriptor.autoGenerateMips = false;
            var copy = RenderTexture.GetTemporary(descriptor);
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                copy.filterMode = FilterMode.Bilinear;
                copy.wrapMode = TextureWrapMode.Clamp;
                GL.sRGBWrite = copy.sRGB;
                Graphics.Blit(pixels, copy);
            }
            catch { RenderTexture.ReleaseTemporary(copy); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            ReleaseTextures();
            source = copy;
            dirty = awaitingCanvasRender = false;
            UpdateChannels();
            surface.tooltip = string.Empty;
        }

        private void Tick()
        {
            if (disposed || !attached) return;
            Layer layer = document != null && !string.IsNullOrEmpty(layerId) ? document.FindLayer(layerId) : null;
            if (!ReferenceEquals(layer, boundLayer) || !ReferenceEquals(layer?.Behaviour, boundBehaviour)) Bind(document, layer);
            if (layer == null || layer.Behaviour == null || layer.Behaviour is PendingLayerBehaviour)
            {
                if (source != null) ReleaseTextures();
                return;
            }
            if (!CanDisplay()) return;
            if (!dirty || EditorApplication.timeSinceStartup < renderAt) return;
            try
            {
                if (document.TryGetCachedLayerPreview(layer, out var cached)) { AcceptCachedLayerPreview(cached); return; }
                if (!awaitingCanvasRender)
                {
                    awaitingCanvasRender = true;
                    renderAt = EditorApplication.timeSinceStartup + .12d;
                    document.RequestLayerPreviewRefresh();
                    return;
                }
                dirty = awaitingCanvasRender = false;
                ReleaseTextures();
                source = document.RenderLayerPreviewFallback(layer, PreviewSize);
                UpdateChannels();
                surface.tooltip = source == null ? "Preview unavailable" : string.Empty;
                UpdateLayout();
            }
            catch (Exception ex)
            {
                dirty = awaitingCanvasRender = false;
                ReleaseTextures();
                surface.tooltip = "Preview unavailable: " + ex.Message;
                Debug.LogException(ex);
            }
        }

        private float MaxHeight()
        {
            float maximum = MaximumHeight;
            float availableHeight = parent?.contentRect.height ?? float.NaN;
            if (!float.IsNaN(availableHeight) && availableHeight > 0) maximum = Mathf.Min(maximum, Mathf.Max(HeaderHeight, availableHeight - 80f));
            return maximum;
        }

        private void UpdateLayout()
        {
            if (disposed) return;
            float maximum = MaxHeight();
            float minimum = Mathf.Min(60f, maximum);
            style.height = state.collapsed ? HeaderHeight : Mathf.Clamp(state.height, minimum, maximum);
            bool hidden = state.collapsed || maximum <= HeaderHeight;
            surface.EnableInClassList("whimtex-output-preview-surface--hidden", hidden);
            if (hidden) { ReleaseTextures(); dirty = true; }
        }

        private void Resize(float requestedHeight)
        {
            float maximum = MaxHeight();
            float threshold = Mathf.Min(34f, HeaderHeight + (maximum - HeaderHeight) * .5f);
            bool wasCollapsed = state.collapsed;
            state.collapsed = requestedHeight <= threshold;
            if (!state.collapsed) state.height = Mathf.Clamp(requestedHeight, Mathf.Min(60f, maximum), maximum);
            UpdateLayout();
            if (wasCollapsed && !state.collapsed) RequestLayerPreview(true);
        }

        private void ToggleChannel(int bit)
        {
            state.channelMask = (state.channelMask ^ bit) & 15;
            for (int i = 0; i < channelButtons.Length; i++)
                channelButtons[i].EnableInClassList("whimtex-channel-button--enabled", (state.channelMask & (1 << i)) != 0);
            UpdateChannels();
        }

        private void UpdateChannels()
        {
            image.image = null;
            if (channelTexture != null) RenderTexture.ReleaseTemporary(channelTexture);
            channelTexture = null;
            if (source == null) return;
            if (channelMaterial == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/DisplayChannels.shader");
                if (shader == null || !shader.isSupported) { image.image = source; return; }
                channelMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            channelTexture = RenderTexture.GetTemporary(source.descriptor);
            channelTexture.filterMode = source.filterMode;
            channelTexture.wrapMode = TextureWrapMode.Clamp;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                channelMaterial.SetVector("_Channels", new Vector4(
                    (state.channelMask & 1) != 0 ? 1 : 0, (state.channelMask & 2) != 0 ? 1 : 0,
                    (state.channelMask & 4) != 0 ? 1 : 0, (state.channelMask & 8) != 0 ? 1 : 0));
                channelMaterial.SetFloat("_Exposure", 1);
                channelMaterial.SetFloat("_Debug", 0);
                GL.sRGBWrite = channelTexture.sRGB;
                Graphics.Blit(source, channelTexture, channelMaterial);
                image.image = channelTexture;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            surface.MarkDirtyRepaint();
        }

        private void DrawCheckerboard(MeshGenerationContext context)
        {
            Texture texture = image.image;
            Rect area = surface.contentRect;
            if (texture == null || area.width <= 0 || area.height <= 0) return;
            float scale = Mathf.Min(area.width / texture.width, area.height / texture.height);
            Vector2 size = new Vector2(texture.width, texture.height) * scale;
            Vector2 origin = area.center - size * .5f;
            var painter = context.painter2D;
            for (int y = 0; y < Mathf.CeilToInt(size.y / 16f); y++)
            for (int x = 0; x < Mathf.CeilToInt(size.x / 16f); x++)
            {
                float shade = ((x + y) & 1) == 0 ? .28f : .42f;
                painter.fillColor = new Color(shade, shade, shade, 1);
                float left = origin.x + x * 16, top = origin.y + y * 16;
                float right = Mathf.Min(left + 16, origin.x + size.x), bottom = Mathf.Min(top + 16, origin.y + size.y);
                painter.BeginPath(); painter.MoveTo(new Vector2(left, top)); painter.LineTo(new Vector2(right, top));
                painter.LineTo(new Vector2(right, bottom)); painter.LineTo(new Vector2(left, bottom)); painter.ClosePath(); painter.Fill();
            }
        }

        private void ReleaseTextures()
        {
            image.image = null;
            if (channelTexture != null) RenderTexture.ReleaseTemporary(channelTexture);
            if (source != null) RenderTexture.ReleaseTemporary(source);
            channelTexture = source = null;
            surface.MarkDirtyRepaint();
        }

        private void Stop()
        {
            resize.Release();
            attached = false;
            EditorApplication.update -= Tick;
            TextureCompositor.Changed -= OnChanged;
            TextureCompositor.RenderResourcesChanged -= OnChanged;
            if (!ReferenceEquals(document, null)) document.LayerPreviewRendered -= OnLayerPreviewRendered;
            layoutParent?.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            layoutParent = null;
            ReleaseTextures();
            if (channelMaterial != null) UnityEngine.Object.DestroyImmediate(channelMaterial);
            channelMaterial = null;
        }

        public void Dispose() { if (disposed) return; Stop(); disposed = true; }

        private sealed class ResizeManipulator : PointerManipulator
        {
            private readonly LayerPreviewPanel owner;
            private int pointer = -1;
            private float startY, startHeight;
            internal ResizeManipulator(LayerPreviewPanel owner) { this.owner = owner; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Release();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            private void Down(PointerDownEvent evt)
            {
                if (owner.disposed || evt.button != 0 || pointer >= 0) return;
                var element = evt.target as VisualElement;
                if (element == owner.channels || owner.channels.Contains(element)) return;
                pointer = evt.pointerId; startY = evt.position.y; startHeight = owner.resolvedStyle.height;
                target.CapturePointer(pointer); evt.StopPropagation();
            }
            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer || !target.HasPointerCapture(pointer)) return;
                owner.Resize(startHeight + startY - evt.position.y); evt.StopPropagation();
            }
            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                Release(); evt.StopPropagation();
            }
            private void Lost(PointerCaptureOutEvent evt) { pointer = -1; }
            internal void Release()
            {
                int id = pointer; pointer = -1;
                if (id >= 0 && target != null && target.HasPointerCapture(id)) target.ReleasePointer(id);
            }
        }
    }
}
