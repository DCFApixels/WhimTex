using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        sealed partial class Builder
        {
            Value ImageFilter(ShaderGraphNode node, string port)
            {
                extended = true;
                if (node.kind == ShaderGraphNodeKind.Truchet)
                {
                    var uv = Input(node, "UV"); var scale = Input(node, "Scale"); var width = Input(node, "Width"); var seed = Input(node, "Seed");
                    return Add(new Instruction { op = 40, o = (int)ShaderGraphNodeKind.Truchet, a = uv.register, b = scale.register, c = width.register, d = seed.register, i = node.option }, ShaderGraphValueType.Float);
                }
                executionFeatures |= 16;
                varyingIterations = true;
                if (rootDerivatives) Unsupported("Root derivatives around sampled image filters");
                if (node.kind == ShaderGraphNodeKind.PaletteQuantize)
                {
                    var color = Input(node, "Color"); var palette = Input(node, "Palette"); var count = Input(node, "Colors");
                    var parameter = palettes[palette.register];
                    return Add(new Instruction { op = 53, a = color.register, b = palette.register, c = count.register,
                        e = node.option, d = (int)(parameter.gradientValue?.WrapMode ?? WhimTexGradientWrapMode.Clamp) }, ShaderGraphValueType.Color);
                }
                int mode = ShaderGraphNodes.ImageFilterMode(node.kind);
                string key = node.id + ":$imageFilter";
                if (!values.TryGetValue(key, out var result))
                {
                    int zero = Number(0).register;
                    int R(string name) => Input(node, name).register;
                    var start = new Instruction { op = 51, a = R("UV"), b = zero, c = zero, d = zero, e = zero, f = zero, g = zero, i = mode };
                    int samples = 4;
                    if (mode == 0) { start.b = R("Radius"); start.c = R("Strength"); samples = 5; }
                    if (mode == 1) { start.b = R("Radius"); start.c = R("HeightScale"); start.d = R("Strength"); start.e = R("Bias"); start.f = R("Directions"); start.g = R("Steps"); samples = 261; }
                    if (mode == 2) { start.b = R("Distance"); start.c = R("HeightScale"); start.d = R("Bias"); start.e = R("Softness"); start.f = R("LightDirection"); start.g = R("Steps"); samples = 65; }
                    if (mode == 3) { int side = Math.Clamp(node.option, 0, 2) + 2; start.b = Number(side).register; samples = side * side; }
                    if (mode == 5) { start.b = R("Radius"); start.c = R("Strength"); start.d = Number((int)node.normalEncoding).register; samples = 4; }
                    if (mode == 6) { start.b = R("Radius"); start.c = R("Strength"); samples = 9; }
                    if (mode == 7 || mode == 9) { start.b = R("Radius"); samples = mode == 7 ? 36 : 9; }
                    if (mode == 8) { start.b = R("Strength"); start.c = R("Steps"); samples = 64; }
                    if (mode == 9)
                    {
                        start.k = code.Count;
                        for (int i = 0; i < 9; i++) Add(new Instruction { op = 0 }, ShaderGraphValueType.Float4);
                    }
                    if (depth + ++samplingDepth > 4) Unsupported("Preview sampling nesting limit");
                    int begin = Add(start, ShaderGraphValueType.Float4).register;
                    var outer = values; values = new Dictionary<string, Value>();
                    multiplier *= samples; sampledEvaluationDepth++;
                    var source = Input(node, ShaderGraphNodes.ImageFilterSource(mode));
                    start.l = ShaderGraphNodes.Components(source.type);
                    start.j = source.type == ShaderGraphValueType.Color ? 1 : 0;
                    code[begin] = start;
                    result = Add(new Instruction { op = 52, a = source.register, b = begin }, mode < 3 || mode == 5 ? ShaderGraphValueType.Float4 : source.type);
                    sampledEvaluationDepth--; multiplier /= samples; samplingDepth--; values = outer;
                    values[key] = result;
                }
                return mode < 3 || mode == 5 ? Component(result, port == "Concave" ? 1 : port == "Curvature" ? 2 : 0) : result;
            }
        }
    }
}
