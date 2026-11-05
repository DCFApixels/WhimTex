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
        internal bool caustics, causticsHeight;
        internal ShaderGraphWrap wrap;
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
                var source = OutsideLoop(() => SamplingFunction(node, "In", sampled: false));
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
                if (ShaderGraphNodes.IsBuffered(nodes[id].kind)) return true;
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
            bool cascade = ShaderGraphNodes.IsMusgraveFractal(node.fractal);
            string octaves = Input(node, "Octaves").text, lacunarity = Input(node, "Lacunarity").text,
                gain = Input(node, "Gain").text, weighted = cascade ? "0" : Input(node, "WeightedStrength").text,
                seedStep = buffered ? "0" : Input(node, "SeedStep").text;
            string pingPong = node.fractal == ShaderGraphFractal.PingPong ? Input(node, "PingPongStrength").text : "2";
            string bias = cascade && node.fractal != ShaderGraphFractal.Multifractal ? Input(node, "Bias").text : "1";
            string feedback = node.fractal is ShaderGraphFractal.HybridMultifractal or ShaderGraphFractal.RidgedMultifractal
                ? Input(node, "Feedback").text : "2";
            string name = "gf" + node.id, type = Type(source.type);
            body.Append(type).Append(' ').Append(name).AppendLine(";")
                .AppendLine("{")
                .Append("WTNoiseFractal f; f.mode=").Append(cascade ? 1 : (int)node.fractal).Append("; f.octaves=(int)(").Append(octaves).AppendLine(");")
                .Append("f.lacunarity=").Append(lacunarity).Append("; f.gain=").Append(gain).AppendLine(";")
                .Append("f.weightedStrength=").Append(weighted).Append("; f.pingPongStrength=").Append(pingPong).AppendLine(";")
                .Append("int seedStep=(int)(").Append(seedStep).AppendLine("), originalSeed=_GraphNoiseSeed;")
                .AppendLine("float2 originalDx=_GraphSampleDx, originalDy=_GraphSampleDy;")
                .Append(type).AppendLine(cascade ? " sum=0, weight=1; float amplitude=1;" : " sum=0, total=0, amplitude=1;")
                .AppendLine("float frequency=1;")
                .AppendLine("[loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++) {")
                .AppendLine("_GraphNoiseSeed=WTNoiseInternalSeed(originalSeed,octave,seedStep);")
                .AppendLine("_GraphSampleDx=originalDx*frequency; _GraphSampleDy=originalDy*frequency;")
                .Append(type).Append(" value=").Append(Call(source, "graphUV*frequency")).AppendLine(";");
            body.Append("float visibility=").Append(node.noiseFiltering
                    ? "WTFractalInternalVisibility(float3(_GraphSampleDx,0),float3(_GraphSampleDy,0))" : "1").AppendLine(";");
            if (cascade)
                body.Append("WTFractalInternalCascade(value,").Append((int)node.fractal - (int)ShaderGraphFractal.Multifractal)
                    .Append(",f.gain,").Append(bias).Append(',').Append(feedback).AppendLine(",visibility,octave,sum,weight,amplitude);");
            else body.AppendLine("WTFractalInternalAccumulate(value,f,visibility,octave,sum,total,amplitude);");
            body.AppendLine("frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);")
                .AppendLine("}")
                .Append(name).AppendLine(cascade ? "=sum;" : "=WTFractalInternalResolve(sum,total);")
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
