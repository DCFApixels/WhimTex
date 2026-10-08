using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
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
            ApplyTransformAndFx(context.input, context);
    }
}
