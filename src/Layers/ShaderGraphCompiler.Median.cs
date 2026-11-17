using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesMedian;
        internal static bool MedianLargeRadius(ShaderGraphNode node)
            => Math.Floor(node.FindInput("Radius")?.value.x ?? 1) > 2;
        internal static int MedianSampleBudget(ShaderGraphNode node)
            => Math.Floor(node.FindInput("Radius")?.value.x ?? 1) > 1 ? 25 : 9;

        Value EmitMedian(ShaderGraphNode node)
        {
            bool connectedRadius = incoming.ContainsKey(node.id + ":Radius");
            bool prepared = bufferPrograms != null && supportsImageCopies &&
                !sampledEvaluation && !bufferEvaluation && samplingDepth == 0 && loopFrames.Count == 0 && scatterDepth == 0 &&
                (node.FindInput("UV") == null || node.FindInput("UV").source != ShaderGraphInputSource.Value) &&
                !incoming.ContainsKey(node.id + ":UV") &&
                (connectedRadius ? PixelUniformInput(node, "Radius") : MedianLargeRadius(node));
            if (prepared) return EmitMedianBuffer(node);
            usesMedian = usesSampleFootprint = true;
            var source = SamplingFunction(node, "In");
            string name = "GraphMedianField" + sampleFunctionIndex++;
            string sample = BufferRGBA(new Value(source.type, Call(source, "sampleUV")));
            bool variable = incoming.ContainsKey(node.id + ":Radius");
            samplingCode.Append("float4 ").Append(name).Append("(float2 center, float requested").Append(LoopParameters).AppendLine(") {")
                .AppendLine("uint radius=(uint)max(floor(GraphFilterFinite(requested)),0); float2 texel=_CanvasSize.zw;");
            if (!variable && !MedianLargeRadius(node)) samplingCode.Append("GRAPH_MEDIAN_SMALL(").Append(sample).AppendLine(");");
            else samplingCode.Append("GRAPH_MEDIAN_DENSE(").Append(sample).AppendLine(");");
            samplingCode.AppendLine("}");
            return new Value(source.type, name + "(" + Input(node, "UV").text + "," + Input(node, "Radius").text + LoopArguments + ")" + Swizzle(source.type));
        }

        Value EmitMedianBuffer(ShaderGraphNode node)
        {
            string key = node.id + ":median";
            if (!bufferPrograms.byNode.TryGetValue(key, out var pass))
            {
                bool connected = incoming.ContainsKey(node.id + ":Radius");
                string radius = connected ? BufferBindingName("_GraphMedianRadius", node.id) : null;
                if (connected)
                    bufferPrograms.ordered.Add(new ShaderGraphBufferPass {
                        nodeId = node.id, textureName = BufferBindingName("_GraphMedianScalar", node.id),
                        program = new ShaderGraphCompiler(this).Generate(null, "Out", node, bufferPort: "Radius"),
                        scalarBinding = radius, type = ShaderGraphValueType.Float,
                        resolution = ShaderGraphBufferResolution.Custom, size = UnityEngine.Vector2Int.one });
                var program = new ShaderGraphCompiler(this).Generate(null, "Out", node);
                radius ??= program.inputNames[InputName(node, "Radius")];
                var input = new ShaderGraphBufferPass { nodeId = node.id, textureName = BufferBindingName("_GraphMedianInput", node.id),
                    program = program, type = program.bufferOutputType, imageInput = true };
                bufferPrograms.ordered.Add(input);
                pass = new ShaderGraphBufferPass { nodeId = node.id, textureName = BufferBindingName("_GraphMedian", node.id),
                    program = new ShaderGraphCompilation {
                        source = (connected ? "// @param hidden float4 " + radius + " (0,0,0,0)\n" : "") + "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/GraphMedianImage.hlsl\"\n" +
                            "float4 ApplyFX(PinnedUV uv,float4 color) { return GraphMedianImage(float2(uv.x,uv.y)); }",
                        uniformNames = new HashSet<string>() },
                    type = input.type, imageSource = input, median = true, imageRadius = radius,
                    resolution = ShaderGraphBufferResolution.Canvas, wrap = ShaderGraphWrap.Clamp };
                if (connected)
                {
                    pass.program.shaderParameters.Add(new ShaderParameter { id = radius, name = radius, type = ShaderParameterType.Vector });
                    pass.program.uniformNames.Add(radius);
                }
                bufferPrograms.byNode.Add(key, pass); bufferPrograms.ordered.Add(pass);
            }
            if (inputParameters.Find(p => p.name == pass.textureName) == null)
            {
                inputDeclarations.Append("// @param hidden texture2D ").AppendLine(pass.textureName);
                inputParameters.Add(new ShaderParameter { id = pass.textureName, name = pass.textureName, type = ShaderParameterType.Texture2D });
            }
            return new Value(pass.type, $"GraphSample({pass.textureName},{Input(node, "UV").text},0,1)" + Swizzle(pass.type));
        }
    }
}
