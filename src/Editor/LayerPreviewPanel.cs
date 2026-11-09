using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    /// <summary>Shared layer preview. View state belongs to the window, not the document.</summary>
    internal sealed partial class LayerPreviewPanel : VisualElement, IDisposable
    {
        [Serializable]
        internal sealed class ViewState : HeightPreviewSettings
        {
            public float height = 220f;
            public bool collapsed;
            public int channelMask = 15;
        }

        private const float HeaderHeight = VerticalRevealHandle.HeaderHeight;
        private const int PreviewSize = 256;
        private const float MaximumHeight = 256f;
        private readonly ViewState state;
        private readonly VisualElement header;
        private readonly bool docked;
        private readonly bool animateShaderGraph;
        private bool AnimatedGraph => animateShaderGraph && boundLayer != null && boundLayer.UsesTime;
        internal VisualElement Header => header;
        private readonly VisualElement surface;
        private readonly Image image;
        private readonly PreviewLoadingIndicator loading;
        private readonly VisualElement channels;
        private readonly Button[] channelButtons = new Button[4];
        private WhimTexDocument document;
        private Layer boundLayer;
        private LayerBehaviour boundBehaviour;
        private string layerId;
        private RenderTexture source, channelTexture;
        private Material channelMaterial;
        private bool dirty = true, attached, disposed, renderFailed;
        private ulong renderedFrame;
        private ulong documentTimeRevision;
        private VisualElement layoutParent;
        internal Func<ShaderGraphPreviewVM> PreviewOverride;
        internal Func<bool> PreviewPending;
        internal Action<bool> PreviewBackendChanged;
        internal bool UsesSVM { get; private set; }
        private void SetPreviewBackend(bool svm)
        {
            if (UsesSVM == svm) return;
            UsesSVM = svm;
            PreviewBackendChanged?.Invoke(svm);
        }
        private ShaderGraphPreviewVM renderedVM;

        internal LayerPreviewPanel(ViewState state, bool docked = true, bool animateShaderGraph = false)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.docked = docked;
            this.animateShaderGraph = animateShaderGraph;
            name = "layer-preview";
            AddToClassList("whimtex-output-preview-footer");
            if (docked)
                header = new VerticalRevealHandle("Layer Preview", () => resolvedStyle.height, Resize,
                    element => disposed || HeaderControls.Contains(element))
                    { name = "layer-preview-resizer", tooltip = "Drag up to reveal the layer preview; drag down to hide it." };
            else
            {
                header = new UnityEditor.UIElements.Toolbar { name = "layer-preview-header" };
                header.Add(new Label("Layer Preview") { pickingMode = PickingMode.Ignore });
                header.Add(new UnityEditor.UIElements.ToolbarSpacer { flex = true, pickingMode = PickingMode.Ignore });
            }
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
            BuildPreviewControls();
            HeaderControls.Add(channels);
            header.Add(HeaderControls);
            Add(header);
            surface = new VisualElement { name = "layer-preview-surface" };
            surface.AddToClassList("whimtex-output-preview-surface");
            image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("whimtex-output-preview-image");
            surface.Add(image);
            if (animateShaderGraph) { loading = new PreviewLoadingIndicator(image); surface.Add(loading); }
            surface.generateVisualContent += DrawCheckerboard;
            surface.RegisterCallback<GeometryChangedEvent>(_ => surface.MarkDirtyRepaint());
            Add(surface);
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);
            UpdateLayout();
        }

        internal void Bind(WhimTexDocument owner, Layer layer)
        {
            if (disposed || (document == owner && ReferenceEquals(boundLayer, layer) && ReferenceEquals(boundBehaviour, layer?.Behaviour))) return;
            if (!ReferenceEquals(document, null)) document.LayerPreviewRendered -= OnLayerPreviewRendered;
            ReleaseTextures();
            document = owner;
            SetPreviewBackend(false);
            if (attached && document != null) document.LayerPreviewRendered += OnLayerPreviewRendered;
            boundLayer = layer;
            boundBehaviour = layer?.Behaviour;
            layerId = layer?.Id;
            renderedFrame = 0;
            channels.SetEnabled(owner != null && layer != null && layer.Behaviour != null && !(layer.Behaviour is PendingLayerBehaviour));
            RequestLayerPreview();
            UpdateLayout();
        }

        internal void RequestLayerPreview()
        {
            if (disposed) return;
            // Tick coalesces edits into one render per Editor update, without a timer.
            dirty = true; renderFailed = false;
            loading?.SetUpdating(CanDisplay());
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            if (disposed || attached) return;
            attached = true;
            layoutParent = parent;
            layoutParent?.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            WhimTexDocument.Changed += OnChanged;
            WhimTexDocument.RenderResourcesChanged += OnChanged;
            if (document != null) document.LayerPreviewRendered += OnLayerPreviewRendered;
            EditorApplication.update += Tick;
            RequestLayerPreview();
            UpdateLayout();
        }

        private void OnDetach(DetachFromPanelEvent evt) => Stop();
        private void OnGeometry(GeometryChangedEvent evt) => UpdateLayout();
        private void OnChanged(WhimTexDocument changed) { if (changed == document) RequestLayerPreview(); }

        private bool CanDisplay()
        {
            if (disposed || !attached || docked && state.collapsed || resolvedStyle.height <= HeaderHeight) return false;
            for (VisualElement ancestor = this; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        private void OnLayerPreviewRendered(Layer layer, RenderTexture pixels)
        {
            if (!ReferenceEquals(layer, boundLayer) || !CanDisplay() || renderFailed || AnimatedGraph) return;
            try
            {
                if (PreviewOverride?.Invoke()?.Ready == true) return;
                if (state.heightMode) { RequestLayerPreview(); return; }
                AcceptCachedLayerPreview(pixels);
            }
            catch (Exception ex) { FailPreview(ex); }
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
            var copy = ProfiledRenderTexture.GetTemporary(descriptor);
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                copy.filterMode = FilterMode.Bilinear;
                copy.wrapMode = TextureWrapMode.Clamp;
                GL.sRGBWrite = copy.sRGB;
                Graphics.Blit(pixels, copy);
            }
            catch { ProfiledRenderTexture.ReleaseTemporary(copy); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            ReleaseTextures();
            source = copy;
            renderedVM = null;
            SetPreviewBackend(false);
            dirty = false;
            loading?.SetUpdating(PreviewPending?.Invoke() == true);
            renderedFrame = ShaderGraphClock.Frame;
            UpdateChannels();
            surface.tooltip = string.Empty;
        }

        private void Tick()
        {
            if (disposed || !attached) return;
            if (!animateShaderGraph && document != null && documentTimeRevision != document.TimeRevision)
            {
                documentTimeRevision = document.TimeRevision;
                if (document.LayerPreviewUsesTime(boundLayer))
                {
                    RequestLayerPreview();
                }
            }
            Layer layer = document != null && !string.IsNullOrEmpty(layerId) ? document.FindLayer(layerId) : null;
            if (!ReferenceEquals(layer, boundLayer) || !ReferenceEquals(layer?.Behaviour, boundBehaviour)) Bind(document, layer);
            if (layer == null || layer.Behaviour == null || layer.Behaviour is PendingLayerBehaviour)
            {
                SetPreviewBackend(false);
                loading?.SetUpdating(false);
                if (source != null) ReleaseTextures();
                return;
            }
            if (!CanDisplay()) { loading?.SetUpdating(false); return; }
            if (renderFailed) { loading?.SetUpdating(false); return; }
            try
            {
                var vm = PreviewOverride?.Invoke();
                if (vm != null && !vm.Ready) vm = null;
                if (!ReferenceEquals(vm, renderedVM)) dirty = true;
                bool animated = AnimatedGraph || vm?.UsesTime == true;
                bool pending = vm == null && PreviewPending?.Invoke() == true;
                loading?.SetUpdating(dirty || pending);
                if (renderedFrame == ShaderGraphClock.Frame || !animated && !dirty) return;
                if (state.heightMode && HeightPreviewRenderer.Supported)
                {
                    dirty = false;
                    renderedFrame = ShaderGraphClock.Frame;
                    ReleaseSource();
                    using (vm != null ? vm.UseFor(boundBehaviour as ShaderLayerBehaviour) : default(ShaderGraphPreviewVM.Scope))
                        source = document.RenderHeightPreviewInput(layer, PreviewSize, vm != null || animated ? ShaderGraphClock.Seconds : null);
                    renderedVM = vm;
                    SetPreviewBackend(vm != null && source != null);
                    RebuildHeight();
                    loading?.SetUpdating(pending);
                    surface.tooltip = source == null ? "Preview unavailable" : string.Empty;
                    return;
                }
                if (vm == null && !animated && document.TryGetCachedLayerPreview(layer, out var cached)) { AcceptCachedLayerPreview(cached); return; }
                // Only wait for an already queued full-canvas render. Viewport renders
                // cannot supply a complete layer, and an absent/closed canvas must not stall us.
                if (vm == null && !animated && document.HasPendingLayerPreview(layer)) return;
                dirty = false;
                renderedFrame = ShaderGraphClock.Frame;
                ReleaseTextures();
                using (vm != null ? vm.UseFor(boundBehaviour as ShaderLayerBehaviour) : default(ShaderGraphPreviewVM.Scope))
                    source = vm != null || animated ? document.RenderShaderGraphPreview(layer, PreviewSize, ShaderGraphClock.Seconds) :
                        document.RenderLayerPreviewFallback(layer, PreviewSize);
                renderedVM = vm;
                SetPreviewBackend(vm != null && source != null);
                loading?.SetUpdating(pending);
                UpdateChannels();
                surface.tooltip = source == null ? "Preview unavailable" : string.Empty;
                UpdateLayout();
            }
            catch (Exception ex) { FailPreview(ex); }
        }

        private void FailPreview(Exception error)
        {
            dirty = false; renderFailed = true;
            SetPreviewBackend(false);
            loading?.SetUpdating(false);
            ReleaseTextures();
            surface.tooltip = "Preview unavailable: " + error.Message;
            Debug.LogException(error);
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
            if (!CanDisplay()) loading?.SetUpdating(false);
            if (disposed || !docked) return;
            float maximum = MaxHeight();
            float minimum = Mathf.Min(VerticalRevealHandle.MinimumExpandedHeight, maximum);
            style.height = state.collapsed ? HeaderHeight : Mathf.Clamp(state.height, minimum, maximum);
            bool hidden = state.collapsed || maximum <= HeaderHeight;
            surface.EnableInClassList("whimtex-output-preview-surface--hidden", hidden);
            if (hidden) { ReleaseTextures(); dirty = true; }
        }

        private void Resize(float requestedHeight)
        {
            float maximum = MaxHeight();
            bool wasCollapsed = state.collapsed;
            state.collapsed = VerticalRevealHandle.ShouldCollapse(requestedHeight, maximum);
            if (!state.collapsed) state.height = Mathf.Clamp(requestedHeight, Mathf.Min(VerticalRevealHandle.MinimumExpandedHeight, maximum), maximum);
            UpdateLayout();
            if (wasCollapsed && !state.collapsed) RequestLayerPreview();
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
            if (state.heightMode && source != null && heightRenderer?.Geometry != null)
            {
                image.image = heightRenderer.Display(source, state.channelMask, state.heightTexture);
                surface.MarkDirtyRepaint();
                return;
            }
            image.image = null;
            if (channelTexture != null) ProfiledRenderTexture.ReleaseTemporary(channelTexture);
            channelTexture = null;
            if (source == null) return;
            if (channelMaterial == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/DisplayChannels.shader");
                if (shader == null || !shader.isSupported) { image.image = source; return; }
                channelMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            channelTexture = ProfiledRenderTexture.GetTemporary(source.descriptor);
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
            heightRenderer?.Dispose();
            ReleaseSource();
        }

        private void ReleaseSource()
        {
            image.image = null;
            if (channelTexture != null) ProfiledRenderTexture.ReleaseTemporary(channelTexture);
            if (source != null) ProfiledRenderTexture.ReleaseTemporary(source);
            channelTexture = source = null;
            surface.MarkDirtyRepaint();
        }

        private void Stop()
        {
            heightSettings?.editorWindow?.Close();
            heightSettings = null;
            SetPreviewBackend(false);
            loading?.SetUpdating(false);
            (header as VerticalRevealHandle)?.Release();
            attached = false;
            EditorApplication.update -= Tick;
            WhimTexDocument.Changed -= OnChanged;
            WhimTexDocument.RenderResourcesChanged -= OnChanged;
            if (!ReferenceEquals(document, null)) document.LayerPreviewRendered -= OnLayerPreviewRendered;
            layoutParent?.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            layoutParent = null;
            ReleaseTextures();
            if (channelMaterial != null) UnityEngine.Object.DestroyImmediate(channelMaterial);
            channelMaterial = null;
        }

        public void Dispose() { if (disposed) return; Stop(); disposed = true; }

    }
}
