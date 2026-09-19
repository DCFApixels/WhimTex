using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // A reservation has identity and placement, but never contributes pixels.
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "PendingLayerBehaviour")]
    [Serializable]
    public sealed class PendingLayerBehaviour : LayerBehaviour
    {
        [SerializeField] internal string jobId;
        internal override RenderTexture Render(in LayerRenderContext context) => null;
    }
}
