using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        internal static event Action<TextureCompositor> MiniPreviewRequested;
        internal event Action<Layer, RenderTexture> MiniPreviewRendered;
        [NonSerialized] private bool publishingMiniPreview;
        [NonSerialized] private EffectRenderCache lastMiniPreviewCache;
        [NonSerialized] private int lastMiniPreviewSize;
        [NonSerialized] private bool lastMiniPreviewInteractive;
        [NonSerialized] private DrawingLayerBehaviour lastMiniPreviewPainting;

        internal void RequestMiniPreviewRefresh() => MiniPreviewRequested?.Invoke(this);

        internal RenderTexture RenderMiniPreviewFallback(Layer layer, int maxSize)
        {
            if (layer == null || !layer.IsGroup) return RenderLayerPreview(layer, maxSize);
            if (!layer.enabled || !TryFindLayer(layer, out var container, out int index)) return null;
            RefreshTransformHierarchy();
            GetPreviewDimensions(maxSize, out int w, out int h, out float scale);
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

        private void PublishMiniPreview(Layer layer, RenderTexture pixels)
        {
            if (!publishingMiniPreview || pixels == null || layer == null || layer.clippingMask) return;
            MiniPreviewRendered?.Invoke(layer, pixels);
        }

        // Borrowed cache texture: callers must copy it before another render can evict it.
        internal bool TryGetCachedMiniPreview(Layer layer, out RenderTexture pixels)
        {
            pixels = null;
            if (lastMiniPreviewCache == null || lastMiniPreviewSize <= 0 || layer == null ||
                !layer.enabled || layer.clippingMask || layer.IsGroup && layer.IsPassThrough ||
                !TryFindLayer(layer, out _, out _)) return false;
            lastMiniPreviewCache.BeginFrame(this, lastMiniPreviewPainting);
            ulong stamp = lastMiniPreviewCache.Stamp(layer);
            if (stamp == 0) return false;
            GetPreviewDimensions(lastMiniPreviewSize, out int w, out int h, out float scale);
            string kind = layer.IsGroup ? "group-composite" : layer.Behaviour is ShaderProcessorLayerBehaviour ? "processor" : "effect";
            string key = layer.Id + "/" + kind + "/debug";
            if (lastMiniPreviewCache.TryGet(key, stamp, w, h, scale, lastMiniPreviewInteractive, true, out pixels, out _, out _)) return true;
            return lastMiniPreviewInteractive && lastMiniPreviewCache.TryGet(key, stamp, w, h, scale, false, true, out pixels, out _, out _);
        }

        private void ForgetMiniPreviewCache()
        {
            lastMiniPreviewCache = null;
            lastMiniPreviewPainting = null;
            lastMiniPreviewSize = 0;
        }
    }
}
