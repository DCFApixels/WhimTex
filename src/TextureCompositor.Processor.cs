using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        private void CompositeProcessor(ShaderProcessorLayerBehaviour layer, ref RenderTexture accumulator,
            int width, int height, float scale, HashSet<Layer> stack)
        {
            if (!stack.Add(layer)) return;
            RenderTexture processed = null;
            try
            {
                RenderTexture processorInput = accumulator;
                processed = CachedEffectRender(layer, "processor", width, height, scale, false, () =>
                {
                    RenderTexture result = layer.Render(new LayerRenderContext(this, processorInput, width, height, scale));
                    try
                    {
                        result = FinishStage(result, layer.colorRange == LayerColorRange.Standard, layer.swizzle);
                        return result;
                    }
                    catch
                    {
                        if (result != null) RenderTexture.ReleaseTemporary(result);
                        throw;
                    }
                });
                if (processed != null)
                    BlendInto(ref accumulator, processed, layer.blendMode == BlendMode.Normal ? (BlendMode)101 : layer.blendMode,
                        layer.opacity, layer.blendRange);
            }
            finally
            {
                if (processed != null) RenderTexture.ReleaseTemporary(processed);
                stack.Remove(layer);
            }
        }
    }
}
