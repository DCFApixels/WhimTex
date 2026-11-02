using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        internal static event Action<WhimTexDocument> LayerPreviewRequested;
        internal event Action<Layer, RenderTexture> LayerPreviewRendered;
        [NonSerialized] private bool publishingLayerPreview;
        [NonSerialized] private EffectRenderCache lastLayerPreviewCache;
        [NonSerialized] private int lastLayerPreviewSize;
        [NonSerialized] private bool lastLayerPreviewInteractive;
        [NonSerialized] private DrawingLayerBehaviour lastLayerPreviewPainting;

        internal void RequestLayerPreviewRefresh() => LayerPreviewRequested?.Invoke(this);

        internal RenderTexture RenderShaderFXInput(Layer layer, ShaderFX effect, int outputWidth, int outputHeight,
            float scale = 1f, float? previewTime = null)
        {
            if (layer == null || effect == null) return null;
            int count = layer.fx?.IndexOf(effect) ?? -1;
            if (count < 0) return null;
            return RenderShaderFXPreview(layer, count, outputWidth, outputHeight, scale, previewTime);
        }

        private RenderTexture RenderShaderFXPreview(Layer layer, int count, int outputWidth, int outputHeight,
            float scale, float? previewTime, bool finishLayer = false, bool includeDisabled = true)
        {
            if (layer?.Behaviour == null || !includeDisabled && !layer.enabled ||
                !TryFindLayer(layer, out var container, out int index)) return null;
            RefreshTransformHierarchy();
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite, oldViewport = renderingViewport, oldQuality = interactiveEffects, oldPublishing = publishingLayerPreview;
            bool oldCollectingErrors = collectingErrors;
            var oldCache = effectCache; var oldLimit = viewportFxLimitLayer;
            RenderTexture result = null;
            try
            {
                renderingViewport = interactiveEffects = publishingLayerPreview = false;
                collectingErrors = false;
                effectCache = null; viewportFxLimitLayer = null; GL.sRGBWrite = false;
                var context = new LayerRenderContext(this, null, outputWidth, outputHeight, scale,
                    applyTransform: !layer.IsGroup,
                    transformFxCoordinates: !layer.IsGroup).WithShaderGraphPreviewTime(previewTime);
                var stack = new HashSet<Layer>();
                if (layer.IsGroup)
                {
                    stack.Add(layer);
                    result = GetClearRenderTexture(outputWidth, outputHeight);
                    // Children keep their normal evaluation time; only this group's FX animate.
                    RenderGroupGeometry(layer, ref result, outputWidth, outputHeight, scale, stack);
                }
                else result = RenderStandaloneUncached(container, index, outputWidth, outputHeight, scale, stack,
                    applyFx: false, includeDisabled: includeDisabled, applyClipping: false, finishLayer: false, shaderGraphPreviewTime: previewTime);
                result ??= GetClearRenderTexture(outputWidth, outputHeight);
                layer.ApplyFx(ref result, context, layer.IsGroup ? FilterMode.Bilinear : layer.ResolveFilterMode(), count);
                if (finishLayer)
                {
                    result = FinishStage(result, layer.colorRange == LayerColorRange.Standard, layer.channelMapping);
                    if (layer.clippingMask) ApplyClippingCoverage(ref result, container, index, outputWidth, outputHeight, scale, stack);
                }
                var output = result; result = null; return output;
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (result != null) ProfiledRenderTexture.ReleaseTemporary(result);
                renderingViewport = oldViewport; interactiveEffects = oldQuality; publishingLayerPreview = oldPublishing;
                collectingErrors = oldCollectingErrors;
                effectCache = oldCache; viewportFxLimitLayer = oldLimit;
            }
        }

        internal RenderTexture RenderLayerPreviewFallback(Layer layer, int maxSize)
        {
            if (layer == null || !layer.IsGroup) return RenderLayerPreview(layer, maxSize);
            if (!layer.enabled || !TryFindLayer(layer, out var container, out int index)) return null;
            RefreshTransformHierarchy();
            GetCanvasRenderSize(maxSize, out int w, out int h, out float scale);
            var previous = RenderTexture.active;
            var stack = new HashSet<Layer>();
            RenderTexture result = null;
            try
            {
                result = RenderClippingSource(container, index, w, h, scale, stack);
                if (layer.clippingMask && result != null) ApplyClippingCoverage(ref result, container, index, w, h, scale, stack);
                return result;
            }
            catch { if (result != null) ProfiledRenderTexture.ReleaseTemporary(result); throw; }
            finally { RenderTexture.active = previous; }
        }

        private void PublishLayerPreview(Layer layer, RenderTexture pixels)
        {
            if (!publishingLayerPreview || pixels == null || layer == null || layer.clippingMask) return;
            LayerPreviewRendered?.Invoke(layer, pixels);
        }

        // Borrowed cache texture: callers must copy it before another render can evict it.
        internal bool TryGetCachedLayerPreview(Layer layer, out RenderTexture pixels)
        {
            pixels = null;
            if (lastLayerPreviewCache == null || lastLayerPreviewSize <= 0 || layer == null ||
                !layer.enabled || layer.clippingMask || layer.IsGroup && layer.IsPassThrough ||
                !TryFindLayer(layer, out _, out _)) return false;
            lastLayerPreviewCache.BeginFrame(this, lastLayerPreviewPainting);
            ulong stamp = lastLayerPreviewCache.Stamp(layer);
            if (stamp == 0) return false;
            GetCanvasRenderSize(lastLayerPreviewSize, out int w, out int h, out float scale);
            string kind = layer.IsGroup ? "group-composite" : "effect";
            string key = layer.Id + "/" + kind + "/debug";
            if (lastLayerPreviewCache.TryGet(key, stamp, w, h, scale, lastLayerPreviewInteractive, true, out pixels, out _, out _)) return true;
            return lastLayerPreviewInteractive && lastLayerPreviewCache.TryGet(key, stamp, w, h, scale, false, true, out pixels, out _, out _);
        }

        private void ForgetLayerPreviewCache()
        {
            lastLayerPreviewCache = null;
            lastLayerPreviewPainting = null;
            lastLayerPreviewSize = 0;
        }
    }
}
