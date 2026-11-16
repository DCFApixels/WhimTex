using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesImageFilters;
        static string ImageFilterHelpers => ShaderGraphSharedSource.Read("ImageFilters");
        Value EmitImageFilter(ShaderGraphNode node, string port)
        {
            usesProcedural = true;
            if (node.kind == ShaderGraphNodeKind.Truchet)
            {
                string Field() => $"GraphTruchetDistance({Input(node, "UV").text},{Input(node, "Scale").text},{Input(node, "Width").text},{Input(node, "Seed").text},{node.option})";
                var distance = ProceduralShared(node, ShaderGraphValueType.Float, Field);
                if (port == "Distance") return distance;
                string width = sampledEvaluation ? "," + SampledWidth(node, ShaderGraphValueType.Float, Field) : "";
                return new Value(ShaderGraphValueType.Float, $"({Input(node, "Width").text}>0 ? GraphDistanceMask({distance.text},{Input(node, "Feather").text}{width}) : 0)");
            }
            if (CanBufferKuwahara(node)) return EmitKuwaharaBuffer(node);
            if (node.kind == ShaderGraphNodeKind.Kuwahara && node.option == 1) return EmitAnisotropicKuwahara(node);
            usesImageFilters = usesColor = true;
            if (node.kind == ShaderGraphNodeKind.PaletteQuantize) return EmitPaletteQuantize(node);
            int mode = ShaderGraphNodes.ImageFilterMode(node.kind);
            string key = node.id + ":$imageFilter";
            if (!values.TryGetValue(key, out var result))
            {
                var source = SamplingFunction(node, ShaderGraphNodes.ImageFilterSource(mode));
                string uv = Input(node, "UV").text, p = "0", q = "0";
                string V(string name) => Input(node, name).text;
                if (mode == 0) p = $"float4({V("Radius")},{V("Strength")},0,0)";
                if (mode == 1) { p = $"float4({V("Radius")},{V("HeightScale")},{V("Strength")},{V("Bias")})"; q = $"float4({V("Directions")},{V("Steps")},0,0)"; }
                if (mode == 2) { p = $"float4({V("Distance")},{V("HeightScale")},{V("Bias")},{V("Softness")})"; q = $"float4({V("LightDirection")},{V("Steps")})"; }
                if (mode == 3) p = $"float4({Math.Clamp(node.option, 0, 2) + 2},0,0,0)";
                if (mode == 4) p = $"float4({V("BlendWidth")},0,0,0)";
                if (mode == 5) p = $"float4({V("Radius")},{V("Strength")},0,0)";
                if (mode == 6) p = $"float4({V("Radius")},{V("Strength")},0,0)";
                if (mode == 7 || mode == 9) p = $"float4({V("Radius")},0,{ShaderGraphNodes.Components(source.type)},0)";
                if (mode == 8) p = $"float4({V("Strength")},{V("Steps")},0,0)";
                usesSampleFootprint = true;
                string id = "imageFilter" + NodeCodeName(node), config = id + "Config", state = id + "State", index = id + "Index";
                if (mode == 9)
                {
                    body.Append("float4 imageMedian").Append(NodeCodeName(node)).AppendLine("[9];");
                    for (int i = 0; i < 9; i++) body.Append("imageMedian").Append(NodeCodeName(node)).Append('[').Append(i).AppendLine("]=0;");
                }
                body.Append("GraphImageFilter ").Append(config).Append("=GraphFilterSetup(").Append(mode)
                    .Append(',').Append(uv).Append(",_CanvasSize.zw,float4(_GraphSampleDx,_GraphSampleDy),").Append(p).Append(',').Append(q).AppendLine(");")
                    .Append("GraphImageFilterState ").Append(state).Append(';').Append(state).Append(".index=0;")
                    .Append(state).Append(".sum=0;").Append(state).Append(".height=0;").Append(state).Append(".best=0;")
                    .Append(state).Append(".auxiliary=0;").Append(state).AppendLine(".variance=0;")
                    .Append("[loop] for(int ").Append(index).Append("=0;").Append(index).Append("<GraphFilterSamples(").Append(config).Append(");").Append(index).AppendLine("++) {");
                var sampled = new Value(source.type, Call(source, "GraphFilterNextUV(" + config + "," + state + ")"));
                if (mode == 9)
                {
                    string array = "imageMedian" + NodeCodeName(node), element = array + "[" + index + "]";
                    body.Append(element).Append('=').Append(Cast(sampled, ShaderGraphValueType.Float4, node).text).AppendLine(";");
                    body.Append(state).AppendLine(".index++;\n}").Append("float4 ").Append(id).Append("=GraphMedian9(");
                    for (int i = 0; i < 9; i++) { if (i != 0) body.Append(','); body.Append(array).Append('[').Append(i).Append(']'); }
                    body.AppendLine(");");
                }
                else body.Append("GraphFilterAccumulate(").Append(config).Append(',').Append(state).Append(',')
                    .Append(Cast(sampled, ShaderGraphValueType.Float4, node).text).AppendLine(");\n}")
                    .Append("float4 ").Append(id).Append("=GraphFilterResult(").Append(config).Append(',').Append(state).AppendLine(");");
                result = new Value(mode < 3 || mode == 5 ? ShaderGraphValueType.Float4 : source.type, id);
                values[key] = result;
            }
            if (mode < 3 || mode == 5) return new Value(ShaderGraphValueType.Float, result.text + (port == "Concave" ? ".y" : port == "Curvature" ? ".z" : ".x"));
            return new Value(result.type, result.text + (ShaderGraphNodes.Components(result.type) == 4 ? "" : "." + "xyzw".Substring(0, ShaderGraphNodes.Components(result.type))));
        }
        Value EmitPaletteQuantize(ShaderGraphNode node)
        {
            string color = Input(node, "Color").text, palette = Input(node, "Palette").text;
            string count = node.paletteKeys ? palette + "_KeyCount()" : "GraphFilterCount(" + Input(node, "Colors").text + ",64)";
            string id = "palette" + NodeCodeName(node), index = id + "Index", candidate = id + "Candidate", distance = id + "Distance";
            body.Append("float4 ").Append(id).Append('=').Append(color).AppendLine(";")
                .Append("float3 ").Append(id).Append("Coordinates=GraphColorCoordinates((").Append(color).Append(").rgb,").Append(node.option).AppendLine(");")
                .Append("float ").Append(distance).AppendLine("=3.402823e38;")
                .Append("int ").Append(id).Append("Count=").Append(count).AppendLine(";")
                .Append("[loop] for(int ").Append(index).Append("=0;").Append(index).Append('<').Append(id).Append("Count;").Append(index).AppendLine("++) {")
                .Append("float3 ").Append(candidate).Append('=').Append(palette)
                .Append(node.paletteKeys ? "_Key(" + index + ")" : "_Sample((float)" + index + "/max(" + id + "Count-1,1))").AppendLine(".rgb;")
                .Append("float ").Append(id).Append("D=GraphColorDistanceSquared(").Append(id).Append("Coordinates,GraphColorCoordinates(").Append(candidate).Append(',').Append(node.option).AppendLine("));")
                .Append("if(").Append(id).Append('D').Append('<').Append(distance).Append(") {").Append(distance).Append('=').Append(id).Append('D').Append(';')
                .Append(id).Append(".rgb=").Append(candidate).AppendLine(";}\n}");
            return new Value(ShaderGraphValueType.Float4, id);
        }
    }
}
