using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static JObject ShaderSnapshot(ShaderLayerBehaviour shader, WhimTexDocument document)
            => new JObject { ["graph"] = WhimTexDocumentJson.ShaderGraphSnapshot(shader.graph, document) };

        private static void SetShader(ShaderLayerBehaviour shader, JObject settings)
        {
            Keys(settings, "graph");
            if (settings["graph"] == null) return;
            // A complete replacement keeps IDs and wiring atomic; validation precedes mutation.
            // Function definitions and their plain-text source archive use the same codec;
            // loading a graph never refreshes its embedded backups from sourcePath.
            var graph = WhimTexDocumentJson.ReadShaderGraph(Obj(settings["graph"], "shader.graph"));
            shader.graph = graph;
            shader.InvalidateGraph();
        }

        private static JArray ShaderNodeCatalog()
        {
            var result = new JArray();
            foreach (var kind in ShaderGraphNodes.Kinds)
            {
                var node = ShaderGraphNodes.Create(kind, default);
                var inputs = new JArray();
                foreach (var port in ShaderGraphNodes.Inputs(node))
                    inputs.Add(new JObject
                    {
                        ["port"] = port.name,
                        ["type"] = port.type?.ToString(),
                        ["passthrough"] = kind == ShaderGraphNodeKind.Reroute,
                        ["defaultUV"] = port.uvDefault,
                        ["value"] = new JArray(port.value.x, port.value.y, port.value.z, port.value.w)
                    });
                var entry = new JObject { ["kind"] = kind.ToString(), ["inputs"] = inputs, ["outputs"] = new JArray(ShaderGraphNodes.Outputs(node)) };
                if (kind == ShaderGraphNodeKind.CustomFunction)
                {
                    entry["dynamicInputs"] = true;
                    entry["dynamicOutputs"] = true;
                    entry["description"] = "Ports shown are the default inline template. Node functionId references graph.functions; actual ports and defaults come from that definition. draft and optional draftPorts retain uncompiled edits. File-backed functions reference sourceId in graph.sources, including all non-engine include bindings and plain source code. Complete graph replacement preserves embedded backups without reading sourcePath.";
                }
                result.Add(entry);
            }
            return result;
        }
    }
}
