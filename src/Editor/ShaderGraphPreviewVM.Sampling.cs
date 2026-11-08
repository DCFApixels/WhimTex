using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        // Replay one instruction span at neighboring coordinates. The span is emitted
        // once, just like a Fractal body, and never materialized as an implicit buffer.
        sealed partial class Builder
        {
            int samplingDepth;
            Value SampleSpan(ShaderGraphNode node, string input, Instruction start, int samples)
                => SampleSpan(() => Input(node, input), start, samples);

            Value SampleSpan(Func<Value> expression, Instruction start, int samples)
            {
                if (depth + ++samplingDepth > 4) Unsupported("Preview sampling nesting limit");
                int begin = Add(start, ShaderGraphValueType.Float4).register;
                var outer = values; values = new Dictionary<string, Value>(); multiplier *= samples;
                var source = expression();
                var result = Add(new Instruction { op = 33, a = source.register, b = begin }, ShaderGraphValueType.Float4);
                multiplier /= samples; values = outer; samplingDepth--;
                return result;
            }

            void NoiseFootprint(ShaderGraphNode node, ref Instruction op)
            {
                if (!node.noiseFiltering) return;
                extended = true;
                if (depth + samplingDepth == 0)
                {
                    if (varyingIterations) Unsupported("Root derivatives after varying iteration counts");
                    rootDerivatives = true; op.value.x = 1; return;
                }
                // Match the native sampled-function footprint, including arbitrary
                // transformations of Position and the current Fractal octave scale.
                var sample = SampleSpan(node, "Position", new Instruction { op = 32, i = -1 }, 4);
                op.value.x = 2; op.value.y = code[sample.register].b; op.value.z = sample.register;
            }

            Value HeightSlope(ShaderGraphNode node)
            {
                if (node.heightSource != ShaderGraphHeightSource.Value) Unsupported("Height texture input");
                extended = true;
                int uv = Input(node, "UV").register, radius = Input(node, "Radius").register;
                var sample = SampleSpan(node, "Height", new Instruction {
                    op = 32, a = uv, b = radius, i = (int)node.derivative
                }, node.derivative == ShaderGraphDerivative.CentralDifference ? 4 : 8);
                return Add(new Instruction { op = 34, a = sample.register, b = Input(node, "Strength").register,
                    c = Input(node, "FlipX").register, d = Input(node, "FlipY").register,
                    i = node.kind == ShaderGraphNodeKind.HeightToNormal ? 0 : 1,
                    j = (int)node.normalEncoding, k = node.option },
                    node.kind == ShaderGraphNodeKind.HeightToNormal ? ShaderGraphValueType.Float3 : ShaderGraphValueType.Float2);
            }
        }
    }
}
