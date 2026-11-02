using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        // Borrowed by the bounds reducer, then released by its caller. Own outer
        // opacity/visibility/clipping do not hide content that can be recovered.
        internal RenderTexture RenderCropLayerContent(Layer layer, EffectRenderCache cache)
        {
            if (!TryFindLayer(layer, out var container, out int index)) return null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite, oldErrors = collectingErrors, oldQuality = interactiveEffects,
                oldPublishing = publishingLayerPreview;
            EffectRenderCache previousCache = effectCache;
            try
            {
                GL.sRGBWrite = false;
                collectingErrors = interactiveEffects = publishingLayerPreview = false;
                effectCache = cache;
                RefreshTransformHierarchy();
                var stack = new HashSet<Layer>();
                if (!layer.IsGroup || layer.Behaviour is BooleanGroupLayerBehaviour)
                    return RenderStandaloneUncached(container, index, width, height, 1f, stack,
                        applyTransform: true, includeDisabled: true, applyClipping: false);
                stack.Add(layer);
                RenderTexture content = GetClearRenderTexture(width, height);
                try
                {
                    CompositeLayers(layer.children, ref content, width, height, 1f, stack,
                        firstIndex: 0);
                    layer.ApplyFx(ref content, new LayerRenderContext(this, null, width, height, 1f, false, true));
                    content = FinishStage(content, layer.colorRange == LayerColorRange.Standard, layer.channelMapping);
                    RenderTexture result = content;
                    content = null;
                    return result;
                }
                finally { if (content != null) ProfiledRenderTexture.ReleaseTemporary(content); }
            }
            finally
            {
                effectCache = previousCache;
                collectingErrors = oldErrors; interactiveEffects = oldQuality; publishingLayerPreview = oldPublishing;
                GL.sRGBWrite = srgb;
                RenderTexture.active = previous;
            }
        }
    }
}
