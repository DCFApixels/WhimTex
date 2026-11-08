using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesColor;
        void ValidateColorOptions(ShaderGraphNode node)
        {
            if (node.kind == ShaderGraphNodeKind.ConvertColor &&
                (!Enum.IsDefined(typeof(ShaderGraphColorSpace), node.inputColorSpace) || !Enum.IsDefined(typeof(ShaderGraphColorSpace), node.outputColorSpace)))
                Error(node, "Unknown color space.");
            if (ShaderGraphNodes.UsesChannel(node.kind) && !Enum.IsDefined(typeof(ShaderGraphChannel), node.channel)) Error(node, "Unknown source channel.");
            if (ShaderGraphNodes.UsesHeightTexture(node.kind) && !Enum.IsDefined(typeof(ShaderGraphDerivative), node.derivative)) Error(node, "Unknown height derivative.");
            if (ShaderGraphNodes.UsesHeightTexture(node.kind) && !Enum.IsDefined(typeof(ShaderGraphHeightSource), node.heightSource)) Error(node, "Unknown height source.");
            if (ShaderGraphNodes.UsesNormalEncoding(node.kind) && !Enum.IsDefined(typeof(ShaderGraphNormalEncoding), node.normalEncoding)) Error(node, "Unknown normal encoding.");
        }

        Value EmitColor(ShaderGraphNode node)
        {
            usesColor = true;
            string V(string name) => Input(node, name).text;
            ShaderGraphNodes.TryColorOutputType(node.kind, out var type);
            string text;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Hue: text = $"GraphHue({V("Color")},{V("Degrees")})"; break;
                case ShaderGraphNodeKind.Saturation: text = $"GraphSaturation({V("Color")},{V("Amount")})"; break;
                case ShaderGraphNodeKind.Exposure: text = $"float4({V("Color")}.rgb*exp2({V("Stops")}),{V("Color")}.a)"; break;
                case ShaderGraphNodeKind.Lightness: text = $"GraphLightness({V("Color")},{V("Amount")})"; break;
                case ShaderGraphNodeKind.Levels: text = $"GraphLevels({V("Color")},{V("InputRange")},{V("Gamma")},{V("OutputRange")},{V("Clamp")},{node.option})"; break;
                case ShaderGraphNodeKind.ColorBalance: text = $"GraphColorBalance({V("Color")},{V("Shadows")},{V("Midtones")},{V("Highlights")},{V("ShadowRange")},{V("HighlightRange")},{V("PreserveLuminance")})"; break;
                case ShaderGraphNodeKind.ChannelMixer: text = $"float4(dot({V("Color")}.rgb,{V("Red")}),dot({V("Color")}.rgb,{V("Green")}),dot({V("Color")}.rgb,{V("Blue")}),{V("Color")}.a)"; break;
                case ShaderGraphNodeKind.WhiteBalance: text = $"GraphWhiteBalance({V("Color")},{V("Temperature")},{V("Tint")})"; break;
                case ShaderGraphNodeKind.Posterize: text = $"GraphPosterize({V("Color")},{V("Levels")})"; break;
                case ShaderGraphNodeKind.ConvertColor: text = $"GraphConvertColor({V("Color")},{(int)node.inputColorSpace},{(int)node.outputColorSpace})"; break;
                case ShaderGraphNodeKind.Luminance: text = $"GraphLuminance({V("Color")}.rgb)"; break;
                case ShaderGraphNodeKind.GradientMap:
                    string mapped = "gm" + NodeCodeName(node);
                    string color = V("Color"), gradient = V("Gradient");
                    body.Append("float4 ").Append(mapped).Append(" = ").Append(gradient).Append("_Sample(GraphChannel(")
                        .Append(color).Append(',').Append((int)node.channel).AppendLine("));");
                    text = $"float4({mapped}.rgb,{(node.option == 0 ? color + ".a" : node.option == 1 ? mapped + ".a*" + color + ".a" : mapped + ".a")})"; break;
                case ShaderGraphNodeKind.ColorCurves: text = EmitColorCurves(node, V("Color")); break;
                case ShaderGraphNodeKind.ColorMask: text = $"GraphColorMask({V("Color")}.rgb,{V("KeyColor")}.rgb,{V("Tolerance")},{V("Softness")},{node.option})"; break;
                case ShaderGraphNodeKind.HueRange: text = $"GraphHueRange({V("Color")}.rgb,{V("Range")},{V("Feather")},{V("SaturationRange")})"; break;
                case ShaderGraphNodeKind.MixColor: text = $"GraphMixColor({V("A")},{V("B")},{V("T")},{node.option})"; break;
                case ShaderGraphNodeKind.SetAlpha: text = $"float4({V("Color")}.rgb,{V("Alpha")}{(node.option == 1 ? "*" + V("Color") + ".a" : "")})"; break;
                case ShaderGraphNodeKind.HeightToNormal:
                case ShaderGraphNodeKind.HeightToDistortion:
                    string slope = node.heightSource == ShaderGraphHeightSource.Value ? EmitHeightFieldSlope(node)
                        : $"GraphHeightSlope({V("Height")},{V("UV")},{V("Radius")},{(int)node.channel},{(int)node.derivative},{(int)node.wrap},{(int)node.filter})";
                    slope += $"*{V("Strength")}*float2({V("FlipX")}? -1.0:1.0,{V("FlipY")}? -1.0:1.0)";
                    text = node.kind == ShaderGraphNodeKind.HeightToNormal
                        ? $"GraphEncodeNormal(GraphUnitNormal(float3(-({slope}),1)),{(int)node.normalEncoding})"
                        : $"(({slope}){(node.option == 1 ? "*0.5+0.5" : "")})"; break;
                case ShaderGraphNodeKind.NormalBlend: text = $"GraphNormalBlend({V("Base")},{V("Detail")},{V("Strength")},{node.option},{(int)node.normalEncoding})"; break;
                case ShaderGraphNodeKind.NormalToDistortion:
                    text = $"((GraphDecodeNormal({V("Normal")},{(int)node.normalEncoding}).xy*{V("Strength")}*float2({V("FlipX")}? -1.0:1.0,{V("FlipY")}? -1.0:1.0)){(node.option == 1 ? "*0.5+0.5" : "")})"; break;
                default: throw new ShaderGraphException(node.id, "Unknown color node.");
            }
            return new Value(type, text);
        }

        string EmitColorCurves(ShaderGraphNode node, string color)
        {
            string variable = "gc" + NodeCodeName(node);
            body.Append("float4 ").Append(variable).Append(" = ").Append(color).AppendLine(";");
            bool Assigned(string port) => incoming.ContainsKey(node.id + ":" + port) || node.FindInput(port) != null;
            string Curve(string port, string value)
            {
                if (!Assigned(port)) return value;
                string curve = Input(node, port).text;
                return $"({value}+{curve}_Sample(saturate({value}))-saturate({value}))";
            }
            if (Assigned("Master"))
            {
                if (node.option == 0)
                    body.Append(variable).Append(".rgb = float3(").Append(Curve("Master", variable + ".r")).Append(',')
                        .Append(Curve("Master", variable + ".g")).Append(',').Append(Curve("Master", variable + ".b")).AppendLine(");");
                else
                {
                    string luma = variable + "_luma";
                    body.Append("float ").Append(luma).Append(" = GraphLuminance(").Append(variable).AppendLine(".rgb);");
                    body.Append(variable).Append(".rgb = GraphMapLuminance(").Append(variable).Append(".rgb,").Append(luma).Append(',').Append(Curve("Master", luma)).AppendLine(");");
                }
            }
            body.Append(variable).Append(" = float4(").Append(Curve("Red", variable + ".r")).Append(',')
                .Append(Curve("Green", variable + ".g")).Append(',').Append(Curve("Blue", variable + ".b")).Append(',')
                .Append(Curve("Alpha", variable + ".a")).AppendLine(");");
            return variable;
        }

        static string ColorHelpers => ShaderGraphSharedSource.Read("Color") + ShaderGraphSharedSource.Read("Height")
            .Replace("WT_GRAPH_HEIGHT_TEXTURE", "WhimTexTexture")
            .Replace("WT_GRAPH_HEIGHT_SIZE(tex)", "max(tex.texelSize.zw,1)")
            .Replace("WT_GRAPH_HEIGHT_SAMPLE", "GraphSample");
    }
}
