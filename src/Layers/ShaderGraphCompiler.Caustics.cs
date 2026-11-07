using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        Value EmitCaustics(ShaderGraphNode node)
        {
            var pass = RequireBuffer(node);
            return new Value(ShaderGraphValueType.Float, $"GraphSample({pass.textureName},{Input(node, "UV").text},{(int)node.wrap},1).r");
        }

        Value CausticsRays(ShaderGraphNode node)
        {
            usesSampleFootprint = true;
            var heightPass = RequireBuffer(node, height: true);
            string Sample(string uv) => $"GraphSample({heightPass.textureName},{uv},{(int)node.wrap},1).r";
            string V(string name) => Input(node, name).text;
            string depth = V("Depth"), ior = V("IOR"), light = V("LightDirection"),
                softness = V("Softness"), intensity = V("Intensity");
            body.AppendLine("float2 delta=_GraphBakeSize.zw;")
                .Append("float hx=(").Append(Sample("graphUV+float2(delta.x,0)")).Append('-').Append(Sample("graphUV-float2(delta.x,0)")).AppendLine(")/(2*delta.x);")
                .Append("float hy=(").Append(Sample("graphUV+float2(0,delta.y)")).Append('-').Append(Sample("graphUV-float2(0,delta.y)")).AppendLine(")/(2*delta.y);")
                .AppendLine("float3 normal=normalize(float3(-float2(hx,hy),1));")
                .Append("float3 light=").Append(light).AppendLine("; light=dot(light,light)>1e-12 ? normalize(light) : float3(0,0,-1);")
                .Append("float eta=1/max(").Append(ior).AppendLine(",1);")
                .AppendLine("float3 ray=refract(light,normal,eta);")
                .AppendLine("float3 flatRay=refract(light,float3(0,0,1),eta);")
                .AppendLine("bool valid=light.z<0 && dot(light,normal)<0 && ray.z<-1e-4;")
                .Append("float2 displacement=max(").Append(depth).AppendLine(",0)*(ray.xy/max(-ray.z,1e-4)-flatRay.xy/max(-flatRay.z,1e-4));")
                .Append("float4 causticRays=float4(valid ? displacement : 0,valid ? max(").Append(intensity).Append(",0) : 0,max(").Append(softness).AppendLine(",0));");
            return new Value(ShaderGraphValueType.Float4, "causticRays");
        }

        Value CausticsHeight(ShaderGraphNode node)
        {
            string V(string name) => Input(node, name).text;
            string strength = V("Strength");
            if (node.kind == ShaderGraphNodeKind.Caustics) return new Value(ShaderGraphValueType.Float, $"({V("Height")}*{strength})");
            string position = V("Position"), scale = V("Scale"), period = node.noisePeriodic ? V("Period") : "float2(0,0)", seed = V("Seed"), phase = V("Phase");
            body.AppendLine("WTNoiseSettings water=WTNoiseDefaults();")
                .Append("water.scale=float3(").Append(scale).Append(",1); water.period=float3(").Append(period).AppendLine(",0);")
                .Append("water.seed=(int)(").Append(seed).AppendLine(")+_GraphNoiseSeed;")
                .AppendLine("water.fractal.mode=WT_NOISE_FBM; water.fractal.octaves=2; water.fractal.gain=0.2;");
            if (node.noiseFiltering)
                body.Append("water.dpdx=float3(ddx(").Append(position).Append("),0); water.dpdy=float3(ddy(").Append(position).AppendLine("),0);");
            body.Append("WTNoiseFlowSettings flow=WTNoiseFlowDefaults(); flow.phase=").Append(phase).AppendLine(";");
            return new Value(ShaderGraphValueType.Float, "WTNoiseFlow(" + position + ",water,flow)*" + strength);
        }
    }
}
