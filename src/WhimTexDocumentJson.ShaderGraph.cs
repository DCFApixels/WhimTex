using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentJson
    {
        internal static void ValidateShaderGraphStorage(object value)
        {
            if (value is ShaderLayerBehaviour shader)
            {
                if (shader.graph == null) throw new WhimTexDocumentException("Shader graph data is missing.");
                shader.graph.Validate();
            }
            else if (value is ShaderGraph graph) graph.Validate();
        }

        internal static JObject ShaderGraphSnapshot(ShaderGraph graph, WhimTexDocument document)
            => new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(graph);

        internal static ShaderGraph ReadShaderGraph(JObject data)
            => (ShaderGraph)new Reader().Value(data, typeof(ShaderGraph));
    }
}
