using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    /// <summary>Editable shader source shared by layer generators, FX and presets.</summary>
    [Serializable]
    public sealed class ShaderProgram
    {
        public ShaderLayerMode mode;
        public ShaderGraph graph = ShaderGraph.CreateDefault();
        public string code = ShaderLayerBehaviour.DefaultCode;
        public string compiledCode = ShaderLayerBehaviour.DefaultCode;
        public List<ShaderFXParameter> codeParameters = new List<ShaderFXParameter>();
        public string presetId;
        public int presetVersion;

        internal IReadOnlyList<ShaderFXParameter> ActiveParameters => mode == ShaderLayerMode.Graph ? graph?.parameters : codeParameters;

        internal static ShaderProgram CreateEffect()
        {
            var result = new ShaderProgram { mode = ShaderLayerMode.Code,
                code = "// @param color(float4) _Tint = (1, 1, 1, 1)\n\nfloat4 ApplyFX(float4 color)\n{\n    return color * _Tint;\n}\n",
                compiledCode = null };
            result.graph.parameters.Clear();
            result.graph.nodes.Clear(); result.graph.edges.Clear();
            var input = ShaderGraphNodes.Create(ShaderGraphNodeKind.SampleInput, new UnityEngine.Vector2(40, 80));
            var output = ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, new UnityEngine.Vector2(360, 80));
            result.graph.nodes.Add(input); result.graph.nodes.Add(output);
            result.graph.edges.Add(new ShaderGraphEdge { outputNode = input.id, inputNode = output.id, inputPort = "RGBA" });
            return result;
        }
    }
}
