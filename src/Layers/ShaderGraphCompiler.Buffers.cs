using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphBufferPass
    {
        internal string nodeId, textureName;
        internal ShaderGraphCompilation program;
        internal string preparedSource;
        internal ShaderSourceAnalysis analysis;
        internal ShaderGraphValueType type;
        internal ShaderGraphBufferResolution resolution;
        internal Vector2Int size;
        internal bool caustics, causticsHeight;
        internal bool lightingInputs, screenSpace;
        internal ShaderGraphBufferPass lightingSource;
        internal ShaderGraphWrap wrap;
    }

    internal sealed partial class ShaderGraphCompiler
    {
        sealed class BufferPrograms
        {
            internal readonly Dictionary<string, ShaderGraphBufferPass> byNode = new();
            internal readonly List<ShaderGraphBufferPass> ordered = new();
        }
        BufferPrograms bufferPrograms;

        void AppendGraphEntry(StringBuilder source, string statements)
        {
            source.AppendLine("float4 ApplyFX(PinnedUV uv, float4 color) {")
                .AppendLine("float2 graphUV = float2(uv.x, uv.y); _GraphScreenUV=graphUV;");
            if (!result.isBufferProgram)
                source.AppendLine(result.usesCanvasPosition ? "graphUV = GraphSourceUv(graphUV);" : "graphUV = ProceduralSourceUv(graphUV);");
            else if (result.usesCanvasPosition)
                source.AppendLine("_GraphRawUV=graphUV; _GraphSourceUV=graphUV; _GraphUvDirection=1; _GraphCanvasUV=GraphLocalToCanvas(graphUV);");
            if (usesSampleFootprint)
                source.AppendLine(result.usesCanvasPosition
                    ? "_GraphSampleDx=ddx(_GraphRawUV)*_GraphUvDirection; _GraphSampleDy=ddy(_GraphRawUV)*_GraphUvDirection;"
                    : "_GraphSampleDx=ddx(graphUV); _GraphSampleDy=ddy(graphUV);");
            source.Append(statements).AppendLine("}");
        }

        ShaderGraphBufferPass RequireBuffer(ShaderGraphNode node, bool height = false)
        {
            string key = node.id + (height ? ":height" : ":value");
            if (!bufferPrograms.byNode.TryGetValue(key, out var pass))
            {
                var compiler = new ShaderGraphCompiler(this);
                var program = compiler.Generate(null, "Out", node, height);
                pass = new ShaderGraphBufferPass { nodeId = node.id,
                    textureName = BufferBindingName(height ? "_GraphCausticsHeight" : "_GraphBuffer", node.id),
                    program = program, type = program.bufferOutputType, resolution = node.bufferResolution,
                    size = node.bufferSize, caustics = !height && node.kind != ShaderGraphNodeKind.Buffer,
                    causticsHeight = height, wrap = node.wrap };
                bufferPrograms.byNode.Add(key, pass);
                bufferPrograms.ordered.Add(pass);
            }
            if (inputParameters.Find(p => p.name == pass.textureName) == null)
            {
                inputDeclarations.Append("// @param hidden texture2D ").AppendLine(pass.textureName);
                inputParameters.Add(new ShaderParameter { id = pass.textureName, name = pass.textureName, type = ShaderParameterType.Texture2D });
            }
            return pass;
        }

        Value EmitBuffer(ShaderGraphNode node)
        {
            var pass = RequireBuffer(node);
            string uv = Input(node, "UV").text;
            return new Value(pass.type, $"GraphSample({pass.textureName},{uv},{(int)node.wrap},{(int)node.filter})" + Swizzle(pass.type));
        }

        Value BufferValue(ShaderGraphNode node, bool height)
            => node.kind == ShaderGraphNodeKind.Buffer ? Input(node, "In") : height ? CausticsHeight(node) : CausticsRays(node);

        void MergeBufferBindings()
        {
            if (bufferPrograms == null) return;
            result.buffers.AddRange(bufferPrograms.ordered);
            // The owner binds one complete property set, also used for external texture-layer
            // resolution. Producers copy those values using the same physical property names.
            var inputs = new HashSet<string>();
            foreach (var parameter in inputParameters) inputs.Add(parameter.name);
            foreach (var pass in result.buffers)
            {
                var program = pass.program;
                foreach (var parameter in program.parameters)
                    if (usedParameters.Add(parameter.id)) result.parameters.Add(parameter);
                foreach (var parameter in program.shaderParameters)
                    if (!program.parameters.Contains(parameter) && inputs.Add(parameter.name)) inputParameters.Add(parameter);
                foreach (var node in program.uniformNodes) if (uniformNodes.Add(node)) result.uniformNodes.Add(node);
                declarations.UnionWith(program.uniformNames);
                foreach (var pair in program.inputNames) result.inputNames.TryAdd(pair.Key, pair.Value);
                result.usesCanvasPosition |= program.usesCanvasPosition;
                result.bufferExternalCode |= program.bufferExternalCode;
            }
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
            string name = "gf" + NodeCodeName(node), type = Type(source.type);
            string prefix = RegisterFractalTemplate(node.fractal, source.type);
            var replacements = FractalReplacements(prefix, type);
            replacements["WTFractalGraphResult"] = name;
            replacements["WTFractalGraphMode"] = (cascade ? 1 : (int)node.fractal).ToString();
            replacements["WTFractalGraphOctaves"] = octaves;
            replacements["WTFractalGraphLacunarity"] = lacunarity;
            replacements["WTFractalGraphGain"] = gain;
            replacements["WTFractalGraphWeighted"] = weighted;
            replacements["WTFractalGraphPingPong"] = pingPong;
            replacements["WTFractalGraphSeedStep"] = seedStep;
            replacements["WTFractalGraphSource"] = Call(source, "graphUV*frequency");
            replacements["WTFractalGraphCascade"] = ((int)node.fractal - (int)ShaderGraphFractal.Multifractal).ToString();
            replacements["WTFractalGraphBias"] = bias;
            replacements["WTFractalGraphFeedback"] = feedback;
            var features = new List<string> { cascade ? "Cascade" : "Mean" };
            if (node.noiseFiltering) features.Add("Filtering");
            body.Append(ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseFractal.hlsl")
                .Generate(new[] { "GraphEvaluation" }, features, replacements));
            return new Value(source.type, name);
        }

        static string BufferRGBA(Value value) => value.type switch
        {
            ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => value.text,
            ShaderGraphValueType.Float3 => "float4(" + value.text + ",0)",
            ShaderGraphValueType.Float2 => "float4(" + value.text + ",0,0)",
            _ => "float4(" + value.text + ",0,0,0)"
        };
    }
}
