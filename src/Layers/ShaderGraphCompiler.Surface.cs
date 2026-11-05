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
                        string name = "gt" + n.id;
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
            if (average && color) samplingCode.AppendLine("result.rgb*=result.a;");
            samplingCode.AppendLine("[loop] for(int i=0;i<steps;i++) {")
                .Append("float2 slope=float2(").Append(Call(slope, "p+float2(_CanvasSize.z,0)")).Append('-').Append(Call(slope, "p-float2(_CanvasSize.z,0)")).Append(',')
                .Append(Call(slope, "p+float2(0,_CanvasSize.w)")).Append('-').Append(Call(slope, "p-float2(0,_CanvasSize.w)")).AppendLine(")*0.5;")
                .AppendLine("float2 next=p-slope*(strength/steps); moved=moved||any(next!=p); p=next;")
                .Append(type).Append(" value=").Append(Call(source, "p")).AppendLine(";");
            if (average && color) samplingCode.AppendLine("value.rgb*=value.a;");
            samplingCode.AppendLine(average ? "result+=value;" : node.option == 1 ? "result=min(result,value);" : "result=max(result,value);")
                .AppendLine("}");
            if (average)
            {
                samplingCode.AppendLine("result/=(steps+1);");
                if (color) samplingCode.AppendLine("result.rgb=result.a==0 ? 0 : result.rgb/(result.a==0 ? 1 : result.a);");
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
            if (guideColor) samplingCode.AppendLine("guideCenter.rgb*=guideCenter.a;");
            samplingCode.AppendLine("radius=abs(radius); tolerance=max(tolerance,0); int extent=(int)max(1,ceil(radius));")
                .Append(type).AppendLine(" sum=0; float total=0;")
                .AppendLine("[loop] for(int y=-extent;y<=extent;y++) [loop] for(int x=-extent;x<=extent;x++) {")
                .AppendLine("float2 offset=float2(x,y)/extent; float2 p=uv+offset*radius*_CanvasSize.zw;")
                .Append(type).Append(" value=").Append(Call(source, "p")).AppendLine(";")
                .Append(guideType).Append(" guideValue=").Append(node.option == 1 ? Call(guide, "p") : "value").AppendLine(";");
            if (guideColor) samplingCode.AppendLine("guideValue.rgb*=guideValue.a;");
            samplingCode.Append(guideType).AppendLine(" difference=guideValue-guideCenter;")
                .Append("float distance2=dot(difference,difference)/").Append(ShaderGraphNodes.Components(guide.type)).AppendLine(".0;")
                .AppendLine("float rangeWeight=tolerance==0 ? (distance2==0 ? 1 : 0) : exp(-0.5*distance2/max(tolerance*tolerance,1e-30));")
                .AppendLine("float weight=exp(-2*dot(offset,offset))*rangeWeight;");
            if (color) samplingCode.AppendLine("value.rgb*=value.a;");
            samplingCode.AppendLine("sum+=value*weight; total+=weight;\n}\nsum/=max(total,1e-20);");
            if (color) samplingCode.AppendLine("sum.rgb=sum.a==0 ? 0 : sum.rgb/(sum.a==0 ? 1 : sum.a);");
            samplingCode.AppendLine("return radius<=1e-7 ? original : sum;\n}");
            return new Value(source.type, $"{name}({Input(node,"UV").text},{Input(node,"Radius").text},{Input(node,"Tolerance").text}{LoopArguments})");
        }

        const string SurfaceHelpers = @"
float2 GraphHeightBlend(float bottom,float top,float offset,float smoothness,float opacity)
{
    top+=offset; smoothness=max(smoothness,0);
    float highest=max(bottom,top), a=max(top-highest+smoothness,0), b=max(bottom-highest+smoothness,0);
    float mask=smoothness<=1e-7 ? (top==bottom ? 0.5 : (top>bottom ? 1 : 0)) : a/max(a+b,1e-20);
    mask*=saturate(opacity);
    return float2(lerp(bottom,top,mask),mask);
}
struct GraphTileData { float2 uv,cell,center; float random,mask; };
GraphTileData GraphTileUV(float2 uv,float2 count,float2 offset,float rowOffset,float rotation,float scaleRandom,float seed)
{
    GraphTileData o;
    count=max(round(abs(count)),1);
    float2 p=uv*count+offset; float shift=frac(floor(p.y)*0.5)*2*rowOffset;
    p.x+=shift; o.cell=floor(p);
    uint h=GraphHashPoint(o.cell,seed);
    o.random=GraphHashUnit(h);
    float angle=(GraphHashUnit(GraphHash(h^0x9e3779b9u))*2-1)*rotation;
    float scale=lerp(1,0.5+GraphHashUnit(GraphHash(h^0x68bc21ebu)),saturate(scaleRandom));
    o.uv=GraphRotatePoint(frac(p)-0.5,-angle)/scale+0.5;
    o.center=(o.cell+0.5-float2(shift,0)-offset)/count;
    o.mask=all(o.uv>=0) && all(o.uv<=1) ? 1 : 0;
    return o;
}
float GraphCross2(float2 a,float2 b) { return a.x*b.y-a.y*b.x; }
float3 GraphPerspectiveUV(float2 p,float2 q0,float2 q1,float2 q2,float2 q3)
{
    // Normalize the solve to the quad's own extent, including small translated quads.
    float size=max(max(length(q1-q0),length(q2-q0)),length(q3-q0));
    if(size<=1e-20) return 0;
    p=(p-q0)/size; q1=(q1-q0)/size; q2=(q2-q0)/size; q3=(q3-q0)/size; q0=0;
    float4 turns=float4(GraphCross2(q1,q2-q1),GraphCross2(q2-q1,q3-q2),GraphCross2(q3-q2,-q3),GraphCross2(-q3,q1));
    if(!(all(turns>1e-7)||all(turns< -1e-7))) return 0;
    float2 c=q2-q1-q3, d1=q1-q2, d2=q3-q2;
    float det=GraphCross2(d1,d2);
    if(abs(det)<=1e-8) return 0;
    float g=GraphCross2(c,d2)/det, h=GraphCross2(d1,c)/det;
    float2 a=q1*(1+g)-p*g, b=q3*(1+h)-p*h;
    det=GraphCross2(a,b);
    if(abs(det)<=1e-8) return 0;
    float2 result=float2(GraphCross2(p,b),GraphCross2(a,p))/det;
    return float3(result,all(result>=0)&&all(result<=1) ? 1 : 0);
}
float3 GraphRotateVector(float3 value,float3 axis,float angle)
{
    float size=max(max(abs(axis.x),abs(axis.y)),abs(axis.z));
    if(size==0) return value;
    axis/=size; axis*=rsqrt(dot(axis,axis));
    float s,c; sincos(radians(angle),s,c);
    return value*c+cross(axis,value)*s+axis*dot(axis,value)*(1-c);
}
";
    }
}
