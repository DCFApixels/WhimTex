using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        internal bool CanGenerateRegionalProcessor(Layer layer) =>
            layer.Behaviour is ShaderProcessorLayerBehaviour &&
            (layer.EffectiveTiling == TransformTilingMode.Clip || layer.EffectiveTiling == TransformTilingMode.Unbounded) &&
            layer.transform.ToMatrix(width, height).Equals(ProjectiveMatrix.Identity);

        // A processor in a pass-through group also consumes the group's external
        // backdrop. Do not depend on the whole parent (which contains this processor).
        internal IEnumerable<Layer> ProcessorBackdropLayers(Layer processor, bool includeDisabled = false)
        {
            Layer current = processor;
            while (TryFindLayer(current, out var container, out int index))
            {
                for (int i = index + 1; i < container.Count; i++)
                    if (container[i] != null && (includeDisabled || container[i].enabled) &&
                        container[i].Behaviour is not PendingLayerBehaviour)
                        yield return container[i];
                if (!TryFindParentGroup(container, out Layer parent, out _, out _) ||
                    !parent.IsPassThrough || IsGroupIsolatedByClipping(parent)) yield break;
                current = parent;
            }
        }

        private void CompositeProcessor(ShaderProcessorLayerBehaviour layer, ref RenderTexture accumulator,
            int width, int height, float scale, HashSet<Layer> stack)
        {
            if (renderingViewport && ViewportFullPrefix(layer.Owner) >= 0 &&
                TryFindLayer(layer, out var container, out int index))
            {
                var region = RenderStandalone(container, index, width, height, scale, stack);
                try
                {
                    if (region != null) BlendInto(ref accumulator, region,
                        layer.blendMode == BlendMode.Normal ? (BlendMode)101 : layer.blendMode, layer.opacity, layer.blendRange);
                }
                finally { if (region != null) ProfiledRenderTexture.ReleaseTemporary(region); }
                return;
            }
            if (!stack.Add(layer)) return;
            RenderTexture processed = null;
            try
            {
                // The accumulator is already the correct regional backdrop, including
                // pass-through parents. Local processors can use it directly.
                RenderTexture processorInput = accumulator;
                processed = CachedEffectRender(layer, "processor", width, height, scale, false, () =>
                {
                    RenderTexture result = layer.Render(new LayerRenderContext(this, processorInput, width, height, scale));
                    try
                    {
                        result = FinishStage(result, layer.colorRange == LayerColorRange.Standard, layer.channelMapping);
                        return result;
                    }
                    catch
                    {
                        if (result != null) ProfiledRenderTexture.ReleaseTemporary(result);
                        throw;
                    }
                });
                if (processed != null)
                {
                    PublishLayerPreview(layer.Owner, processed);
                    BlendInto(ref accumulator, processed, layer.blendMode == BlendMode.Normal ? (BlendMode)101 : layer.blendMode,
                        layer.opacity, layer.blendRange);
                }
            }
            finally
            {
                if (processed != null) ProfiledRenderTexture.ReleaseTemporary(processed);
                stack.Remove(layer);
            }
        }
    }
}
