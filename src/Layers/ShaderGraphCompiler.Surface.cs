using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesSurface, usesBlackbody;
        Value EmitSurface(ShaderGraphNode n, string port)
        {
            if (n.kind == ShaderGraphNodeKind.SlopeBlur) return EmitSlopeBlur(n);
            if (n.kind == ShaderGraphNodeKind.BilateralBlur) return EmitBilateralBlur(n);
            string V(string name) => Input(n, name).text;
            ShaderGraphNodes.TrySurfaceOutputType(n, port, out var type);
            if (n.kind == ShaderGraphNodeKind.Blackbody)
            {
                usesBlackbody = true;
                return new Value(type, $"GraphBlackbody({V("Temperature")})");
            }
            usesSurface = usesProcedural = true;
            switch (n.kind)
            {
                case ShaderGraphNodeKind.HeightBlend:
                    var blend = ProceduralShared(n, ShaderGraphValueType.Float2, () => $"GraphHeightBlend({V("Bottom")},{V("Top")},{V("Offset")},{V("Smoothness")},{V("Opacity")})");
                    return new Value(type, blend.text + (port == "Height" ? ".x" : ".y"));
                case ShaderGraphNodeKind.PerspectiveUV:
                    var perspective = ProceduralShared(n, ShaderGraphValueType.Float3, () => $"GraphPerspectiveUV({V("UV")},{V("BottomLeft")},{V("BottomRight")},{V("TopRight")},{V("TopLeft")})");
                    return new Value(type, perspective.text + (port == "Mask" ? ".z" : ".xy"));
                case ShaderGraphNodeKind.RotateVector:
                    return new Value(type, n.option == 0 ? $"GraphRotatePoint({V("In")},{V("Angle")})" : $"GraphRotateVector({V("In")},{V("Axis")},{V("Angle")})");
                case ShaderGraphNodeKind.TileUV:
                {
                    string key = n.id + ":$tile";
                    if (!values.TryGetValue(key, out var tile))
                    {
                        string arguments = $"{V("UV")},{V("Count")},{V("Offset")},{(n.option == 1 ? V("RowOffset") : "0")},{V("RandomRotation")},{V("RandomScale")},{V("Seed")}";
                        string name = "gt" + NodeCodeName(n);
                        body.Append("GraphTileData ").Append(name).Append(" = GraphTileUV(").Append(arguments).AppendLine(");");
                        tile = new Value(ShaderGraphValueType.Float4, name); values.Add(key, tile);
                    }
                    return new Value(type, tile.text + "." + port.ToLowerInvariant());
                }
                default: throw new ShaderGraphException(n.id, "Unknown surface node.");
            }
        }

        Value EmitSlopeBlur(ShaderGraphNode node)
        {
            var source = SamplingFunction(node, "In");
            var direct = sampledEvaluation ? source : SamplingFunction(node, "In", false);
            var slope = SamplingFunction(node, "Slope");
            string type = Type(source.type), name = "GraphSlopeBlur" + sampleFunctionIndex++;
            bool color = source.type == ShaderGraphValueType.Color, average = node.option == 0;
            samplingCode.Append(type).Append(' ').Append(name).Append("(float2 uv,float strength,float samples").Append(LoopParameters).AppendLine(") {")
                .Append(type).Append(" original=").Append(Call(direct, "uv")).AppendLine(";")
                .AppendLine("int steps=(int)clamp(round(samples),1,64); float2 p=uv; bool moved=false;")
                .Append(type).AppendLine(" result=original;");
            if (average && color) AppendPremultiply(samplingCode, "result");
            samplingCode.AppendLine("[loop] for(int i=0;i<steps;i++) {")
                .Append("float2 slope=float2(").Append(Call(slope, "p+float2(_CanvasSize.z,0)")).Append('-').Append(Call(slope, "p-float2(_CanvasSize.z,0)")).Append(',')
                .Append(Call(slope, "p+float2(0,_CanvasSize.w)")).Append('-').Append(Call(slope, "p-float2(0,_CanvasSize.w)")).AppendLine(")*0.5;")
                .AppendLine("float2 next=p-slope*(strength/steps); moved=moved||any(next!=p); p=next;")
                .Append(type).Append(" value=").Append(Call(source, "p")).AppendLine(";");
            if (average && color) AppendPremultiply(samplingCode, "value");
            samplingCode.AppendLine(average ? "result+=value;" : node.option == 1 ? "result=min(result,value);" : "result=max(result,value);")
                .AppendLine("}");
            if (average)
            {
                samplingCode.AppendLine("result/=(steps+1);");
                if (color) AppendUnpremultiply(samplingCode, "result");
            }
            samplingCode.AppendLine("return !moved || abs(strength)<=1e-7 ? original : result;\n}");
            return new Value(source.type, $"{name}({Input(node,"UV").text},{Input(node,"Strength").text},{Input(node,"Samples").text}{LoopArguments})");
        }

        Value EmitBilateralBlur(ShaderGraphNode node)
        {
            var source = SamplingFunction(node, "In");
            var direct = sampledEvaluation ? source : SamplingFunction(node, "In", false);
            var guide = node.option == 1 ? SamplingFunction(node, "Guide") : source;
            bool color = source.type == ShaderGraphValueType.Color, guideColor = guide.type == ShaderGraphValueType.Color;
            string type = Type(source.type), guideType = Type(guide.type), name = "GraphBilateral" + sampleFunctionIndex++;
            samplingCode.Append(type).Append(' ').Append(name).Append("(float2 uv,float radius,float tolerance").Append(LoopParameters).AppendLine(") {")
                .Append(type).Append(" original=").Append(Call(direct, "uv")).AppendLine(";")
                .Append(guideType).Append(" guideCenter=").Append(Call(guide, "uv")).AppendLine(";");
            if (guideColor) AppendPremultiply(samplingCode, "guideCenter");
            samplingCode.AppendLine("radius=abs(radius); tolerance=max(tolerance,0); int extent=(int)max(1,ceil(radius));")
                .Append(type).AppendLine(" sum=0; float total=0;")
                .AppendLine("[loop] for(int y=-extent;y<=extent;y++) [loop] for(int x=-extent;x<=extent;x++) {")
                .AppendLine("float2 offset=float2(x,y)/extent; float2 p=uv+offset*radius*_CanvasSize.zw;")
                .Append(type).Append(" value=").Append(Call(source, "p")).AppendLine(";")
                .Append(guideType).Append(" guideValue=").Append(node.option == 1 ? Call(guide, "p") : "value").AppendLine(";");
            if (guideColor) AppendPremultiply(samplingCode, "guideValue");
            samplingCode.Append(guideType).AppendLine(" difference=guideValue-guideCenter;")
                .Append("float distance2=dot(difference,difference)/").Append(ShaderGraphNodes.Components(guide.type)).AppendLine(".0;")
                .AppendLine("float rangeWeight=tolerance==0 ? (distance2==0 ? 1 : 0) : exp(-0.5*distance2/max(tolerance*tolerance,1e-30));")
                .AppendLine("float weight=exp(-2*dot(offset,offset))*rangeWeight;");
            if (color) AppendPremultiply(samplingCode, "value");
            samplingCode.AppendLine("sum+=value*weight; total+=weight;\n}\nsum/=max(total,1e-20);");
            if (color) AppendUnpremultiply(samplingCode, "sum");
            samplingCode.AppendLine("return radius<=1e-7 ? original : sum;\n}");
            return new Value(source.type, $"{name}({Input(node,"UV").text},{Input(node,"Radius").text},{Input(node,"Tolerance").text}{LoopArguments})");
        }

        static string SurfaceHelpers => ShaderGraphSharedSource.Read("Surface");
    }
}
