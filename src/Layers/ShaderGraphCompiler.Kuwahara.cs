using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool supportsImageCopies;
        internal static bool KuwaharaLargeRadius(ShaderGraphNode node)
            => Math.Floor(node.FindInput("Radius")?.value.x ?? 2) > 5;

        bool CanBufferKuwahara(ShaderGraphNode node)
            => node.kind == ShaderGraphNodeKind.Kuwahara && bufferPrograms != null && supportsImageCopies &&
                !sampledEvaluation && !bufferEvaluation && samplingDepth == 0 && loopFrames.Count == 0 && scatterDepth == 0 &&
                (node.FindInput("UV") == null || node.FindInput("UV").source != ShaderGraphInputSource.Value) &&
                (node.option == 1 || KuwaharaLargeRadius(node)) && !incoming.ContainsKey(node.id + ":Radius") && !incoming.ContainsKey(node.id + ":UV") &&
                (node.option == 0 || (!incoming.ContainsKey(node.id + ":Sharpness") && !incoming.ContainsKey(node.id + ":Uniformity") && !incoming.ContainsKey(node.id + ":Eccentricity")));

        Value KuwaharaOrBufferInput(ShaderGraphNode node)
        {
            // Keep the radius binding in the producer's dependency set, including hybrid previews.
            if (node.kind == ShaderGraphNodeKind.Kuwahara)
            {
                Input(node, "Radius");
                if (node.option == 1) { Input(node, "Sharpness"); Input(node, "Uniformity"); Input(node, "Eccentricity"); }
            }
            return Input(node, "In");
        }

        Value EmitKuwaharaBuffer(ShaderGraphNode node)
        {
            string key = node.id + ":kuwahara";
            if (!bufferPrograms.byNode.TryGetValue(key, out var pass))
            {
                var producer = new ShaderGraphCompiler(this);
                var program = producer.Generate(null, "Out", node);
                string filter = node.option == 1 ? "GraphKuwaharaAnisotropicImage" : "GraphKuwahara";
                var input = new ShaderGraphBufferPass { nodeId = node.id,
                    textureName = BufferBindingName("_GraphKuwaharaInput", node.id), program = program,
                    type = program.bufferOutputType, kuwaharaInput = true };
                bufferPrograms.ordered.Add(input);
                pass = new ShaderGraphBufferPass { nodeId = node.id,
                    textureName = BufferBindingName("_GraphKuwahara", node.id),
                    program = new ShaderGraphCompilation {
                        source = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/" + filter + ".hlsl\"\n" +
                            "float4 ApplyFX(PinnedUV uv, float4 color) { return " + filter + "(float2(uv.x,uv.y)); }",
                        uniformNames = new System.Collections.Generic.HashSet<string>() },
                    type = input.type, kuwaharaSource = input, kuwaharaHighPrecision = node.kuwaharaHighPrecision,
                    kuwaharaRadius = program.inputNames[InputName(node, "Radius")],
                    kuwaharaAnisotropic = node.option == 1,
                    kuwaharaSharpness = node.option == 1 ? program.inputNames[InputName(node, "Sharpness")] : null,
                    kuwaharaUniformity = node.option == 1 ? program.inputNames[InputName(node, "Uniformity")] : null,
                    kuwaharaEccentricity = node.option == 1 ? program.inputNames[InputName(node, "Eccentricity")] : null,
                    resolution = ShaderGraphBufferResolution.Canvas, wrap = ShaderGraphWrap.Clamp };
                bufferPrograms.byNode.Add(key, pass);
                bufferPrograms.ordered.Add(pass);
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
