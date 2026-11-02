using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    // Read-only bridge for existing documents and Editor reload snapshots.
    // NormalizeModel replaces it with an ordinary Shader before rendering/saving.
    internal sealed class ShaderProcessorLayerBehaviour : LayerBehaviour
    {
        internal override RenderTexture Render(in LayerRenderContext context) =>
            throw new InvalidOperationException("Normalize the document before rendering a retired Shader Processor.");
    }
}
