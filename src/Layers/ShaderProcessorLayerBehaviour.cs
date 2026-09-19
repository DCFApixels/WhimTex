using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "ShaderProcessorLayerBehaviour")]
    [Serializable]
    public sealed class ShaderProcessorLayerBehaviour : LayerBehaviour
    {
        internal override void InitializeLayer(Layer layer)
        {
            layer.colorRange = LayerColorRange.HDR;
            layer.blendRange = LayerBlendRange.HDR;
        }

        public override string ToString() => "Shader Processor";
        internal override RenderTexture Render(in LayerRenderContext context) =>
            ApplyTransformAndModifiers(context.input, context);
    }
}
