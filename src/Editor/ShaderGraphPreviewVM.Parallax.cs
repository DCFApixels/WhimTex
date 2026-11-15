using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        sealed partial class Builder
        {
            Value EmitParallax(ShaderGraphNode node)
            {
                extended = true;
                bool occlusion = node.kind == ShaderGraphNodeKind.ParallaxOcclusionMapping;
                bool texture = node.heightSource == ShaderGraphHeightSource.Texture;
                var start = new Instruction { op = 49, a = Input(node, "UV").register,
                    b = Input(node, "ViewDirection").register, c = Input(node, "Amplitude").register,
                    d = occlusion ? Input(node, "Steps").register : Number(1).register,
                    i = occlusion ? 1 : 0, j = texture ? 1 : 0, k = (int)node.channel };
                int image = texture ? Input(node, "Height").register : 0;
                if (depth + ++samplingDepth > 4) Unsupported("Preview sampling nesting limit");
                // The ray search is divergent. Its source uses finite sample derivatives;
                // hardware derivatives elsewhere keep the native fallback.
                varyingIterations = true;
                if (rootDerivatives) Unsupported("Root derivatives around parallax sampling");
                executionFeatures |= 8;
                int samples = occlusion ? 68 : 1;
                sampledEvaluationDepth++;
                int begin = Add(start, ShaderGraphValueType.Float2).register;
                var outer = values; values = new Dictionary<string, Value>();
                multiplier *= samples;
                Value source;
                if (texture)
                {
                    var uv = Add(new Instruction { op = 2, b = 2 }, ShaderGraphValueType.Float2);
                    source = Add(new Instruction { op = 41, a = image, b = uv.register,
                        c = (int)node.wrap, d = (int)node.filter }, ShaderGraphValueType.Color);
                }
                else source = Input(node, "Height");
                var result = Add(new Instruction { op = 50, a = source.register, b = begin }, ShaderGraphValueType.Float2);
                multiplier /= samples; values = outer;
                sampledEvaluationDepth--; samplingDepth--;
                return result;
            }
        }
    }
}
