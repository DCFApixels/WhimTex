using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // A reservation has identity and placement, but never contributes pixels.
    [Serializable]
    public sealed class PendingLayerBehaviour : LayerBehaviour
    {
        [SerializeField] internal string jobId;
        internal override RenderTexture Render(in LayerRenderContext context) => null;
    }
}
