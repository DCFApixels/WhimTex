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
            catch { if (result != null) RenderTexture.ReleaseTemporary(result); throw; }
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
            string kind = layer.IsGroup ? "group-composite" : layer.Behaviour is ShaderProcessorLayerBehaviour ? "processor" : "effect";
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
