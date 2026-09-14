using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        internal Texture2D RenderAreaSelectionSource(Layer layer)
        {
            if (layer == null) return Compose();
            return RasterizeMerge(CreateMergePlan(new List<Layer> { layer }));
        }

        internal Texture2D RenderAreaSelectionAlphaSource(Layer layer)
        {
            if (layer == null || !TryFindLayer(layer, out var container, out int index)) return null;
            RenderTexture rendered = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var stack = new HashSet<Layer>();
                if (layer?.AsGroup() is Layer group)
                {
                    stack.Add(group);
                    rendered = GetClearRenderTexture(width, height);
                    CompositeLayers(group.layers, ref rendered, width, height, 1f, stack);
                    rendered = FinishStage(rendered, group.colorRange == LayerColorRange.Standard, group.swizzle);
                    if (group.clippingMask) ApplyClippingCoverage(ref rendered, container, index, width, height, 1f, stack);
                }
                else rendered = RenderStandalone(container, index, width, height, 1f, stack, includeDisabled: true);
                if (rendered == null) rendered = GetClearRenderTexture(width, height);
                return HdrUtility.ReadLinear(rendered);
            }
            finally
            {
                RenderTexture.active = previous;
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            }
        }
    }
}
