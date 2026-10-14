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
                if (!layer.IsGroup && !(layer.Behaviour is ShaderProcessorLayerBehaviour))
                    return RenderStandaloneUncached(container, index, width, height, 1f, stack,
                        applyTransform: true, includeDisabled: true, applyClipping: false);
                stack.Add(layer);
                RenderTexture content = GetClearRenderTexture(width, height);
                try
                {
                    CompositeLayers(layer.IsGroup ? layer.children : container, ref content, width, height, 1f, stack,
                        firstIndex: layer.IsGroup ? 0 : index + 1);
                    if (layer.IsGroup)
                        layer.ApplyFx(ref content, new LayerRenderContext(this, null, width, height, 1f, false, true));
                    else
                    {
                        RenderTexture input = content;
                        content = null;
                        try { content = layer.Render(new LayerRenderContext(this, input, width, height, 1f, true, true)); }
                        finally { RenderTexture.ReleaseTemporary(input); }
                    }
                    content = FinishStage(content, layer.colorRange == LayerColorRange.Standard, layer.channelMapping);
                    RenderTexture result = content;
                    content = null;
                    return result;
                }
                finally { if (content != null) RenderTexture.ReleaseTemporary(content); }
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
