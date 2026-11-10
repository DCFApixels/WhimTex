using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        int scatterDepth;
        bool scatterCenterControls;
        bool usesScatter;

        // Runtime context is scoped by the generated scatter function. Sampling functions
        // share it, while nested scatter calls save and restore their parent's instance.
        const string ScatterSource = @"
struct GraphScatterContext { float index; float random; float2 center; };
static GraphScatterContext _GraphScatter = { 0, 0.5, float2(0.5,0.5) };
uint GraphScatterHash(uint x) {
    x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16);
}
float GraphScatterRandom(uint x) { return (GraphScatterHash(x) >> 8) * (1.0/16777216.0); }
";

        Value EmitScatterInstance(ShaderGraphNode node, string port)
        {
            if (scatterDepth == 0 && (!previewLoopInputs || bufferEvaluation || result.lightingProducer))
                Error(node, "Scatter Instance is available only inside Scatter's Source or center-sampled inputs. A buffer cannot store a live instance: place it before the instance-dependent branch or after Scatter.");
            usesScatter = true;
            return port switch {
                "UV" => new Value(ShaderGraphValueType.Float2, scatterCenterControls ? "float2(0.5,0.5)" : "graphUV"),
                "Center" => new Value(ShaderGraphValueType.Float2, "_GraphScatter.center"),
                "Index" => new Value(ShaderGraphValueType.Float, "_GraphScatter.index"),
                _ => new Value(ShaderGraphValueType.Float, "_GraphScatter.random")
            };
        }

        Value EmitScatter(ShaderGraphNode node)
        {
            usesScatter = usesSampleFootprint = true;
            string V(string port) => Input(node, port).text;
            // Global placement settings are evaluated in the enclosing scope, before
            // entering the new instance. Only maps and Source see the new instance.
            string uv = V("UV"), count = V("Count"), size = V("Size"), sizeRandom = V("SizeRandom"),
                rotation = V("Rotation"), rotationRandom = V("RotationRandom"), jitter = V("Jitter"), seed = V("Seed");
            string period = node.scatterPeriodic ? V("Period") : "float2(1,1)";
            string sampleDx, sampleDy;
            if (sampledEvaluation)
            {
                var field = SamplingFunction(node, "UV");
                sampleDx = $"({Call(field, "graphUV+_GraphSampleDx*0.5")}-{Call(field, "graphUV-_GraphSampleDx*0.5")})";
                sampleDy = $"({Call(field, "graphUV+_GraphSampleDy*0.5")}-{Call(field, "graphUV-_GraphSampleDy*0.5")})";
            }
            else { sampleDx = "ddx(" + uv + ")"; sampleDy = "ddy(" + uv + ")"; }
            Value source, controls;
            bool previousCenterControls = scatterCenterControls;
            scatterDepth++;
            try
            {
                scatterCenterControls = false;
                source = SamplingFunction(node, "Source");
                scatterCenterControls = true;
                controls = SamplingExpression(node.id + ":scatterControls", () =>
                    new Value(ShaderGraphValueType.Float4, $"float4({V("Density")},{V("Mask")},{V("ScaleMap")},{V("RotationMap")})"), node);
            }
            finally { scatterDepth--; scatterCenterControls = previousCenterControls; }
            bool alpha = node.option == 2;
            var resultType = alpha ? ShaderGraphValueType.Color : source.type;
            string name = "GraphScatter" + sampleFunctionIndex++, type = Type(resultType);
            var code = new StringBuilder();
            code.Append(type).Append(' ').Append(name)
                .Append("(float2 graphUV,float2 count,float2 size,float sizeRandom,float rotation,float rotationRandom,float jitter,float seed,float2 period,float2 sampleDx,float2 sampleDy")
                .Append(LoopParameters).AppendLine(") {")
                .Append("count=clamp(floor(abs(count)+0.5),1,").Append(ShaderGraphNodes.ScatterMaximumCount).AppendLine(");")
                .Append("size=clamp(abs(size),0,").Append(ShaderGraphNodes.ScatterMaximumSize).AppendLine(");")
                .AppendLine("sizeRandom=saturate(sizeRandom); jitter=saturate(jitter); period=max(abs(period),0.0001);")
                .AppendLine("float2 cellSize=period/count;")
                .AppendLine(node.scatterPeriodic ? "float2 p=frac(graphUV/period)*count;" : "float2 p=graphUV*count;")
                // The bound uses the largest rotated rectangle. ScaleMap and random
                // size only shrink it, so no unseen neighbor can reach the pixel.
                .AppendLine("float radius=0.5*(length(size)+jitter);")
                .AppendLine("int2 first=(int2)ceil(p-radius-0.5), last=(int2)floor(p+radius-0.5);")
                .AppendLine("uint seedBits=(uint)(int)clamp(round(seed),-16777216.0,16777216.0);")
                .AppendLine("GraphScatterContext saved=_GraphScatter;")
                .AppendLine("float2 savedDx=_GraphSampleDx, savedDy=_GraphSampleDy;")
                .Append(type).AppendLine(" sum=0;")
                .AppendLine("[loop] for(int scatterY=first.y;scatterY<=last.y;scatterY++) {")
                .AppendLine("[loop] for(int scatterX=first.x;scatterX<=last.x;scatterX++) {")
                .AppendLine("float2 cell=float2(scatterX,scatterY);");
            if (node.scatterPeriodic) code.AppendLine("cell=cell-floor(cell/count)*count;");
            code.AppendLine("uint hash=GraphScatterHash((uint)(int)cell.x ^ GraphScatterHash((uint)(int)cell.y ^ seedBits));")
                .AppendLine("float2 offset=(float2(GraphScatterRandom(hash^0x68bc21ebu),GraphScatterRandom(hash^0x02e5be93u))-0.5)*jitter;")
                .AppendLine("float2 center=float2(scatterX,scatterY)+0.5+offset;")
                .AppendLine("_GraphScatter.index=(float)(hash & 0xffffffu); _GraphScatter.random=GraphScatterRandom(hash^0x967a889bu);")
                .AppendLine("_GraphScatter.center=(cell+0.5+offset)*cellSize;");
            if (node.scatterPeriodic) code.AppendLine("_GraphScatter.center=frac(_GraphScatter.center/period)*period;");
            code.AppendLine("_GraphSampleDx=sampleDx; _GraphSampleDy=sampleDy;")
                .Append("float4 controls=").Append(Call(controls, "_GraphScatter.center")).AppendLine(";")
                .AppendLine("float opacity=saturate(controls.y);")
                .AppendLine("if(opacity<=0 || GraphScatterRandom(hash^0xb5297a4du)>=saturate(controls.x)) continue;")
                .AppendLine("float2 extent=size*saturate(controls.z)*(1-sizeRandom*GraphScatterRandom(hash^0x1b56c4e9u));")
                .AppendLine("if(any(extent<=0.000001)) continue;")
                .AppendLine("float s,c; sincos(radians(rotation+controls.w+(2*GraphScatterRandom(hash^0x9e3779b9u)-1)*rotationRandom),s,c);")
                .AppendLine("float2 delta=p-center;")
                .AppendLine("float2 localUV=float2(c*delta.x+s*delta.y,-s*delta.x+c*delta.y)/extent+0.5;")
                .AppendLine("if(any(localUV<0) || any(localUV>=1)) continue;")
                .AppendLine("float2 dx=sampleDx/cellSize, dy=sampleDy/cellSize;")
                .AppendLine("_GraphSampleDx=float2(c*dx.x+s*dx.y,-s*dx.x+c*dx.y)/extent;")
                .AppendLine("_GraphSampleDy=float2(c*dy.x+s*dy.y,-s*dy.x+c*dy.y)/extent;");
            string sample = Call(source, "localUV");
            if (alpha) sample = Cast(new Value(source.type, sample), ShaderGraphValueType.Color, node).text;
            code.Append(type).Append(" value=").Append(sample).AppendLine(";");
            if (alpha)
                code.AppendLine("float a=saturate(value.a)*opacity; sum.rgb=value.rgb*a+sum.rgb*(1-a); sum.a=a+sum.a*(1-a);");
            else code.AppendLine(node.option == 1 ? "sum+=value*opacity;" : "sum=max(sum,value*opacity);");
            code.AppendLine("}}")
                .AppendLine("_GraphScatter=saved; _GraphSampleDx=savedDx; _GraphSampleDy=savedDy;");
            if (alpha) AppendUnpremultiply(code, "sum");
            code.AppendLine("return sum;\n}");
            CheckGeneratedSize(node, code.Length);
            samplingCode.Append(code);
            return new Value(resultType, $"{name}({uv},{count},{size},{sizeRandom},{rotation},{rotationRandom},{jitter},{seed},{period},{sampleDx},{sampleDy}{LoopArguments})");
        }
    }
}
