using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        internal Texture2D RenderPsdGroupContent(Layer group)
        {
            RenderTexture rendered = GetClearRenderTexture(width, height);
            RenderTexture previous = RenderTexture.active;
            try
            {
                CompositeLayers(group.layers, ref rendered, width, height, 1f, new HashSet<Layer>());
                rendered = FinishStage(rendered, group.colorRange == LayerColorRange.Standard, group.swizzle);
                return CopyToTexture2D(rendered, uploadToGpu: false);
            }
            finally
            {
                RenderTexture.active = previous;
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            }
        }

        internal Texture2D RenderPsdCoverage(Layer layer)
        {
            RenderTexture rendered = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var fill = new ColorFillLayerBehaviour { color = Color.white, transform = layer.transform, filterMode = layer.filterMode };
                rendered = fill.Render(new LayerRenderContext(this, null, width, height, 1f));
                return CopyToTexture2D(rendered, uploadToGpu: false);
            }
            finally
            {
                RenderTexture.active = previous;
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            }
        }

        internal Texture2D RenderPsdPixels(Layer layer, bool effectInput = false)
        {
            RenderTexture rendered = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (layer == null)
                    rendered = RenderComposite(width, height, 1f);
                else if (TryFindLayer(layer, out List<Layer> container, out int index))
                {
                    var stack = new HashSet<Layer>();
                    if (effectInput && layer?.Behaviour is TargetedLayerBehaviour effect)
                    {
                        stack.Add(layer);
                        rendered = RenderEffectInput(effect, container, index, width, height, 1f, stack);
                    }
                    else
                        rendered = RenderStandalone(container, index, width, height, 1f, stack,
                            applyTransform: true, applyModifiers: true, includeDisabled: true, applyClipping: false);
                }
                return rendered == null ? null : CopyToTexture2D(rendered, uploadToGpu: false);
            }
            finally
            {
                RenderTexture.active = previous;
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            }
        }
    }
}
