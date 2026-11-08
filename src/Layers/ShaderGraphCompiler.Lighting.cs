using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        Value LightingInputs(ShaderGraphNode node)
        {
            result.usesCanvasPosition = true; usesColor = true;
            string V(string name) => Input(node, name).text;
            string color = V("Color"), normal = V("Normal"), ambient = V("Ambient"), uv = V("UV"),
                height = V("Height"), roughness = V("Roughness"), specular = V("SpecularColor"), intensity = V("SpecularIntensity");
            body.AppendLine($"GraphLighting0={color};")
                .AppendLine($"GraphLighting1=float4(GraphDecodeNormal({normal},{(int)node.normalEncoding}),{ambient});")
                .AppendLine($"GraphLighting2=float4(GraphCanvasPosition({uv})*_CanvasSize.xy,{height},{roughness});")
                .AppendLine($"GraphLighting3=float4(({specular}).rgb,{intensity}*({specular}).a);");
            return new Value(ShaderGraphValueType.Color, "GraphLighting0");
        }

        Value EmitSeparateLighting(ShaderGraphNode node)
        {
            string key = node.id + ":lighting";
            if (!bufferPrograms.byNode.TryGetValue(key, out var pass))
            {
                var producer = new ShaderGraphCompiler(this) { separateLighting = false };
                var inputs = new ShaderGraphBufferPass { nodeId = node.id,
                    textureName = BufferBindingName("_GraphLightingInputs", node.id),
                    program = producer.Generate(null, "Out", lightingNode: node),
                    type = ShaderGraphValueType.Color, resolution = ShaderGraphBufferResolution.Canvas,
                    lightingInputs = true, screenSpace = true };
                bufferPrograms.ordered.Add(inputs);
                pass = new ShaderGraphBufferPass { nodeId = node.id,
                    textureName = BufferBindingName("_GraphLighting", node.id),
                    program = LightingEvaluation(), type = ShaderGraphValueType.Color,
                    resolution = ShaderGraphBufferResolution.Canvas, screenSpace = true, lightingSource = inputs };
                bufferPrograms.byNode.Add(key, pass);
                bufferPrograms.ordered.Add(pass);
            }
            if (inputParameters.Find(p => p.name == pass.textureName) == null)
            {
                inputDeclarations.Append("// @param hidden texture2D ").AppendLine(pass.textureName);
                inputParameters.Add(new ShaderParameter { id = pass.textureName, name = pass.textureName, type = ShaderParameterType.Texture2D });
            }
            return new Value(ShaderGraphValueType.Color, $"tex2Dlod({pass.textureName},float4((_GraphScreenUV-_GraphLightingViewport.xy)/_GraphLightingViewport.zw,0,0))");
        }

        // Shared exact evaluator. Surface preparation carries the procedural graph;
        // this small program retains real light types, cookies, area lights and HDR.
        static ShaderGraphCompilation LightingEvaluation() => new ShaderGraphCompilation
        {
            source = @"Texture2D<float4> _GraphLightData0, _GraphLightData1, _GraphLightData2, _GraphLightData3;
float4 _GraphLightSize;
float4 ApplyFX(PinnedUV uv, float4 color) {
    int3 pixel=int3(int2(float2(uv.x,uv.y)*_GraphLightSize.xy),0);
    float4 c=_GraphLightData0.Load(pixel), n=_GraphLightData1.Load(pixel);
    float4 p=_GraphLightData2.Load(pixel), s=_GraphLightData3.Load(pixel);
    return WTGlobalLighting(p.xyz,c,n.xyz,n.w,s.xyz,s.w,p.w);
}",
            contextDependencies = ShaderContextDependencies.Lighting,
            uniformNames = new System.Collections.Generic.HashSet<string>()
        };
    }
}
