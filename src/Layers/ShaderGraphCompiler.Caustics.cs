using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        Value EmitCaustics(ShaderGraphNode node)
        {
            if (!bufferPasses.TryGetValue(node.id, out var pass))
            {
                var previousTrace = activeTrace;
                var previousBody = body; var previousValues = values; bool previousSampled = sampledEvaluation;
                activeTrace = null;
                body = new StringBuilder(); values = new Dictionary<string, Value>(); sampledEvaluation = true;
                string function = "GraphCaustics" + node.id;
                try
                {
                    usesSampleFootprint = true;
                    var height = SamplingExpression(node.id + ":causticsHeight", () => CausticsHeight(node), node);
                    var heightPass = new ShaderGraphBufferPass { nodeId = node.id, textureName = "_GraphCausticsHeight" + node.id,
                        function = height.text, type = ShaderGraphValueType.Float, resolution = node.bufferResolution, size = node.bufferSize, causticsHeight = true };
                    AddCausticsPass(heightPass);
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
                        .Append("return float4(valid ? displacement : 0,valid ? max(").Append(intensity).Append(",0) : 0,max(").Append(softness).AppendLine(",0));");
                    samplingCode.Append("float4 ").Append(function).AppendLine("(float2 graphUV) {").Append(body).AppendLine("}");
                }
                finally { body = previousBody; values = previousValues; sampledEvaluation = previousSampled; activeTrace = previousTrace; }
                pass = new ShaderGraphBufferPass { nodeId = node.id, textureName = "_GraphBuffer" + node.id, function = function,
                    type = ShaderGraphValueType.Float4, resolution = node.bufferResolution, size = node.bufferSize, caustics = true, wrap = node.wrap };
                bufferPasses.Add(node.id, pass); AddCausticsPass(pass);
            }
            return new Value(ShaderGraphValueType.Float, $"GraphSample({pass.textureName},{Input(node, "UV").text},{(int)node.wrap},1).r");
        }

        void AddCausticsPass(ShaderGraphBufferPass pass)
        {
            result.buffers.Add(pass);
            inputDeclarations.Append("// @param hidden texture2D ").AppendLine(pass.textureName);
            inputParameters.Add(new ShaderParameter { id = pass.textureName, name = pass.textureName, type = ShaderParameterType.Texture2D });
        }

        Value CausticsHeight(ShaderGraphNode node)
        {
            string V(string name) => Input(node, name).text;
            string strength = V("Strength");
            if (node.kind == ShaderGraphNodeKind.Caustics) return new Value(ShaderGraphValueType.Float, $"({V("Height")}*{strength})");
            string position = V("Position"), scale = V("Scale"), period = V("Period"), seed = V("Seed"), phase = V("Phase");
            body.AppendLine("WTNoiseSettings water=WTNoiseDefaults();")
                .Append("water.scale=float3(").Append(scale).Append(",1); water.period=float3(").Append(period).AppendLine(",0);")
                .Append("water.seed=(int)(").Append(seed).AppendLine(")+_GraphNoiseSeed;")
                .AppendLine("water.fractal.mode=WT_NOISE_FBM; water.fractal.octaves=2; water.fractal.gain=0.2;")
                .Append("water.dpdx=float3(ddx(").Append(position).Append("),0); water.dpdy=float3(ddy(").Append(position).AppendLine("),0);")
                .Append("WTNoiseFlowSettings flow=WTNoiseFlowDefaults(); flow.phase=").Append(phase).AppendLine(";");
            return new Value(ShaderGraphValueType.Float, "WTNoiseFlow(" + position + ",water,flow)*" + strength);
        }
    }
}
