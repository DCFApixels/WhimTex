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
            int sampledEvaluationDepth;
            Value SampleSpan(ShaderGraphNode node, string input, Instruction start, int samples)
                => SampleSpan(() => Input(node, input), start, samples);

            Value SampleSpan(Func<Value> expression, Instruction start, int samples, bool direct = false)
            {
                if (depth + ++samplingDepth > 4) Unsupported("Preview sampling nesting limit");
                if (!direct) sampledEvaluationDepth++;
                int begin = Add(start, ShaderGraphValueType.Float4).register;
                var outer = values; values = new Dictionary<string, Value>(); multiplier *= samples;
                var source = expression();
                var result = Add(new Instruction { op = 33, a = source.register, b = begin }, ShaderGraphValueType.Float4);
                multiplier /= samples; values = outer; samplingDepth--;
                if (!direct) sampledEvaluationDepth--;
                return result;
            }

            void NoiseFootprint(ShaderGraphNode node, ref Instruction op)
            {
                if (!node.noiseFiltering) return;
                extended = true;
                if (depth + sampledEvaluationDepth == 0)
                {
                    if (varyingIterations) Unsupported("Root derivatives after varying iteration counts");
                    rootDerivatives = true; op.value.x = 1; return;
                }
                var position = code[op.a];
                if (position.op == 2)
                {
                    // A direct UV read has no upstream work to replay four times.
                    // Keep the sampled +/- half-pixel subtraction in the executor.
                    op.value = new Vector4(3, position.a, position.b, 0); return;
                }
                // Match the native sampled-function footprint, including arbitrary
                // transformations of Position and the current Fractal octave scale.
                var sample = SampleSpan(node, "Position", new Instruction { op = 32, i = -1 }, 4);
                op.value.x = 2; op.value.y = code[sample.register].b; op.value.z = sample.register;
            }

            Value HeightSlope(ShaderGraphNode node)
            {
                extended = true;
                int uv = Input(node, "UV").register, radius = Input(node, "Radius").register;
                var sample = node.heightSource == ShaderGraphHeightSource.Texture
                    ? Add(new Instruction {op=47,a=Input(node,"Height").register,b=uv,c=radius,i=(int)node.channel,
                        j=(int)node.derivative,k=(int)node.wrap,l=(int)node.filter},ShaderGraphValueType.Float2)
                    : SampleSpan(node, "Height", new Instruction {
                    op = 32, a = uv, b = radius, i = (int)node.derivative
                }, node.derivative == ShaderGraphDerivative.CentralDifference ? 4 : 8);
                return Add(new Instruction { op = 34, a = sample.register, b = Input(node, "Strength").register,
                    c = Input(node, "FlipX").register, d = Input(node, "FlipY").register,
                    i = node.kind == ShaderGraphNodeKind.HeightToNormal ? 0 : 1,
                    j = (int)node.normalEncoding, k = node.option },
                    node.kind == ShaderGraphNodeKind.HeightToNormal ? ShaderGraphValueType.Float3 : ShaderGraphValueType.Float2);
            }

            Value EdgeDetection(ShaderGraphNode node)
            {
                extended = true;
                int uv=Input(node,"UV").register, radius=Input(node,"Radius").register;
                Value field=default;
                var edge=SampleSpan(()=>field=Input(node,"In"),new Instruction {op=32,a=uv,b=radius,i=1,j=1},8);
                if(!ShaderGraphNodes.IsNumeric(field.type)) Unsupported("Edge input type");
                bool color=field.type==ShaderGraphValueType.Color;
                // The center retains direct derivatives at root scope, matching the native
                // filter's alpha. Numeric outputs do not need another evaluation.
                var center=color?SampleSpan(nodeInput,new Instruction {op=32,a=uv,i=3},1,
                    direct:depth+sampledEvaluationDepth==0):Number(0);
                return Add(new Instruction {op=35,a=edge.register,b=center.register,c=Input(node,"Strength").register,d=radius,
                    i=color?1:0},field.type);
                Value nodeInput()=>Input(node,"In");
            }
        }
    }
}
