using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphBufferPass
    {
        internal string nodeId, textureName, function;
        internal ShaderGraphValueType type;
        internal ShaderGraphBufferResolution resolution;
        internal Vector2Int size;
    }

    internal sealed partial class ShaderGraphCompiler
    {
        readonly Dictionary<string, ShaderGraphBufferPass> bufferPasses = new();

        void AppendGraphEntry(StringBuilder source, string statements)
        {
            bool buffered = result.buffers.Count != 0;
            if (buffered) source.AppendLine("float4 GraphBufferedOutput(float2 graphUV) {").Append(statements).AppendLine("}");
            source.AppendLine("float4 ApplyFX(PinnedUV uv, float4 color) {")
                .AppendLine("float2 graphUV = float2(uv.x, uv.y);");
            if (buffered) source.AppendLine("if (_GraphBufferPass == 0) {");
            source.AppendLine(result.usesCanvasPosition ? "graphUV = GraphSourceUv(graphUV);" : "graphUV = ProceduralSourceUv(graphUV);");
            if (buffered)
            {
                source.AppendLine("} else {");
                if (result.usesCanvasPosition)
                    source.AppendLine("_GraphRawUV=graphUV; _GraphSourceUV=graphUV; _GraphUvDirection=1; _GraphCanvasUV=GraphLocalToCanvas(graphUV);");
                source.AppendLine("}");
            }
            if (usesSampleFootprint)
                source.AppendLine(result.usesCanvasPosition
                    ? "_GraphSampleDx=ddx(_GraphRawUV)*_GraphUvDirection; _GraphSampleDy=ddy(_GraphRawUV)*_GraphUvDirection;"
                    : "_GraphSampleDx=ddx(graphUV); _GraphSampleDy=ddy(graphUV);");
            if (buffered)
            {
                source.AppendLine("float4 graphResult=0;")
                    .AppendLine("if (_GraphBufferPass == 0) graphResult=GraphBufferedOutput(graphUV);");
                for (int i = 0; i < result.buffers.Count; i++)
                    source.Append("else if (_GraphBufferPass == ").Append(i + 1).Append(") graphResult=").Append(BufferRGBA(result.buffers[i])).AppendLine(";");
                source.AppendLine("return graphResult;");
            }
            else source.Append(statements);
            source.AppendLine("}");
        }

        Value EmitBuffer(ShaderGraphNode node)
        {
            if (!bufferPasses.TryGetValue(node.id, out var pass))
            {
                // Recursion registers dependencies first. The stored value is always
                // evaluated over the full local 0..1 domain, independently of its readers.
                var source = SamplingFunction(node, "In", sampled: false);
                pass = new ShaderGraphBufferPass { nodeId = node.id, textureName = "_GraphBuffer" + node.id,
                    function = source.text, type = source.type, resolution = node.bufferResolution, size = node.bufferSize };
                bufferPasses.Add(node.id, pass);
                result.buffers.Add(pass);
                inputDeclarations.Append("// @param hidden texture2D ").AppendLine(pass.textureName);
                inputParameters.Add(new ShaderParameter { id = pass.textureName, name = pass.textureName, type = ShaderParameterType.Texture2D });
            }
            string uv = Input(node, "UV").text;
            return new Value(pass.type, $"GraphSample({pass.textureName},{uv},{(int)node.wrap},{(int)node.filter})" + Swizzle(pass.type));
        }

        bool BranchContainsBuffer(ShaderGraphNode node)
        {
            var visited = new HashSet<string>();
            bool Visit(string id)
            {
                if (!visited.Add(id)) return false;
                if (nodes[id].kind == ShaderGraphNodeKind.Buffer) return true;
                foreach (var port in ShaderGraphNodes.Inputs(nodes[id]))
                    if (incoming.TryGetValue(id + ":" + port.name, out var edge) && Visit(edge.outputNode)) return true;
                return false;
            }
            return incoming.TryGetValue(node.id + ":In", out var input) && Visit(input.outputNode);
        }

        Value EmitFractal(ShaderGraphNode node)
        {
            bool buffered = BranchContainsBuffer(node);
            if (buffered && (incoming.ContainsKey(node.id + ":SeedStep") || (node.FindInput("SeedStep")?.value.x ?? 1) != 0))
                Error(node, "A buffered source cannot change Seed per octave. Set an unconnected Seed Step to 0, or place Buffer after Fractal.");
            usesSampleFootprint = true;
            var source = SamplingFunction(node, "In");
            string octaves = Input(node, "Octaves").text, lacunarity = Input(node, "Lacunarity").text,
                gain = Input(node, "Gain").text, weighted = Input(node, "WeightedStrength").text,
                seedStep = buffered ? "0" : Input(node, "SeedStep").text;
            string pingPong = node.fractal == ShaderGraphFractal.PingPong ? Input(node, "PingPongStrength").text : "2";
            string name = "gf" + node.id, type = Type(source.type);
            body.Append(type).Append(' ').Append(name).AppendLine(";")
                .AppendLine("{")
                .Append("int count=clamp((int)(").Append(octaves).AppendLine("),1,8);")
                .Append("float lacunarity=clamp(").Append(lacunarity).AppendLine(",0.01,16);")
                .Append("float gain=saturate(").Append(gain).Append("), weighted=saturate(").Append(weighted).AppendLine(");")
                .Append("int seedStep=(int)(").Append(seedStep).AppendLine("), originalSeed=_GraphNoiseSeed;")
                .AppendLine("float2 originalDx=_GraphSampleDx, originalDy=_GraphSampleDy;")
                .Append(type).AppendLine(" sum=0, total=0, amplitude=1;")
                .AppendLine("float frequency=1;")
                .AppendLine("[loop] for(int octave=0;octave<count;octave++) {")
                .AppendLine("_GraphNoiseSeed=asint(asuint(originalSeed)+asuint(seedStep)*(uint)octave);")
                .AppendLine("_GraphSampleDx=originalDx*frequency; _GraphSampleDy=originalDy*frequency;")
                .Append(type).Append(" value=").Append(source.text).AppendLine("(graphUV*frequency);");
            if (node.fractal == ShaderGraphFractal.Ridged) body.AppendLine("value=1-abs(value*2-1);");
            else if (node.fractal == ShaderGraphFractal.PingPong)
                body.Append("value=1-abs(frac(value*max(").Append(pingPong).AppendLine(",0))*2-1);");
            body.Append("float visibility=").Append(node.noiseFiltering
                    ? "1-smoothstep(0.25,1.0,WTNoiseInternalFootprint(float3(_GraphSampleDx,0),float3(_GraphSampleDy,0)))" : "1").AppendLine(";")
                .AppendLine("// Preserve a constant field even when every octave is subpixel.")
                .AppendLine("if(octave==0) visibility=max(visibility,0.000001);")
                .AppendLine("sum+=value*amplitude*visibility; total+=amplitude*visibility;")
                .AppendLine("amplitude*=gain*lerp(1,saturate(value),weighted); frequency*=lacunarity;")
                .AppendLine("}")
                .Append(name).AppendLine("=sum/max(total,0.0000001);")
                .AppendLine("_GraphNoiseSeed=originalSeed; _GraphSampleDx=originalDx; _GraphSampleDy=originalDy;")
                .AppendLine("}");
            return new Value(source.type, name);
        }

        static string BufferRGBA(ShaderGraphBufferPass pass) => pass.type switch
        {
            ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => pass.function + "(graphUV)",
            ShaderGraphValueType.Float3 => "float4(" + pass.function + "(graphUV),0)",
            ShaderGraphValueType.Float2 => "float4(" + pass.function + "(graphUV),0,0)",
            _ => "float4(" + pass.function + "(graphUV),0,0,0)"
        };
    }
}
