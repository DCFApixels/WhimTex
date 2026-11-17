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
                    return Procedural(node, port);
                if (node.kind == ShaderGraphNodeKind.PaletteQuantize)
                {
                    var color = Input(node, "Color"); var palette = Input(node, "Palette"); var count = node.paletteKeys ? Number(0) : Input(node, "Colors");
                    var parameter = palettes[palette.register];
                    return Add(new Instruction { op = 53, a = color.register, b = palette.register, c = count.register,
                        e = node.option, f = node.paletteKeys ? 1 : 0, d = (int)(parameter.gradientValue?.WrapMode ?? WhimTexGradientWrapMode.Clamp) }, ShaderGraphValueType.Color);
                }
                if (node.kind == ShaderGraphNodeKind.Kuwahara && node.option == 1)
                    Unsupported("Anisotropic Kuwahara uses a compiled image boundary");
                if (node.kind == ShaderGraphNodeKind.Kuwahara && depth == 0 && samplingDepth == 0 && sampledEvaluationDepth == 0 &&
                    (node.FindInput("UV") == null || node.FindInput("UV").source != ShaderGraphInputSource.Value) &&
                    ShaderGraphCompiler.KuwaharaLargeRadius(node) && !incoming.ContainsKey(node.id + ":Radius") && !incoming.ContainsKey(node.id + ":UV"))
                    Unsupported("Kuwahara image preparation requires a compiled boundary");
                if (node.kind == ShaderGraphNodeKind.Median && (incoming.ContainsKey(node.id + ":Radius") || ShaderGraphCompiler.MedianLargeRadius(node)))
                    Unsupported("Dense Median requires an image boundary or native branch evaluation");
                executionFeatures |= 16;
                varyingIterations = true;
                if (rootDerivatives) Unsupported("Root derivatives around sampled image filters");
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
                    if (mode == 4) start.b = R("BlendWidth");
                    if (mode == 5) { start.b = R("Radius"); start.c = R("Strength"); samples = 4; }
                    if (mode == 6) { start.b = R("Radius"); start.c = R("Strength"); samples = 9; }
                    if (mode == 7 || mode == 9) { start.b = R("Radius"); samples = mode == 9 ? ShaderGraphCompiler.MedianSampleBudget(node) : 9;
                        if (mode == 7)
                        {
                            if (incoming.ContainsKey(node.id + ":Radius")) Unsupported("Dynamic Kuwahara radius requires native evaluation");
                            double side = Math.Floor(Math.Max(0, node.FindInput("Radius")?.value.x ?? 2)) + 1;
                            if (side > 32) Unsupported("Kuwahara sampling work budget");
                            samples = (int)(4 * side * side);
                        } }
                    if (mode == 8) { start.b = R("Strength"); start.c = R("Steps"); samples = 64; }
                    if (mode == 9)
                    {
                        start.k = code.Count;
                        for (int i = 0; i < samples; i++) Add(new Instruction { op = 0 }, ShaderGraphValueType.Float4);
                    }
                    if (depth + ++samplingDepth > 4) Unsupported("Preview sampling nesting limit");
                    int begin = Add(start, ShaderGraphValueType.Float4).register;
                    var outer = values; values = new Dictionary<string, Value>();
                    multiplier *= samples; sampledEvaluationDepth++;
                    var source = Input(node, ShaderGraphNodes.ImageFilterSource(mode));
                    start.l = ShaderGraphNodes.Components(source.type);
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
