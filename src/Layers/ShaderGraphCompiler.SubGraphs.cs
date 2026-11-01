using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        void ValidateSubGraphs()
        {
            if (graph.subGraphs == null || graph.subGraphs.Count > 128) throw new FormatException("Invalid Sub Graph table (maximum 128 definitions).");
            var ids = new HashSet<string>();
            var nodeIds = new HashSet<string>();
            if (graph.activeSubGraph == null) foreach (var node in graph.nodes) if (node?.id != null) nodeIds.Add(node.id);
            foreach (var definition in graph.subGraphs)
            {
                if (definition == null || !Identifier(definition.id) || !ids.Add(definition.id) || string.IsNullOrWhiteSpace(definition.name) ||
                    definition.inputs == null || definition.outputs == null || definition.inputs.Count > 64 || definition.outputs.Count == 0 || definition.outputs.Count > 64 ||
                    definition.nodes == null || definition.edges == null) throw new FormatException("Invalid Sub Graph definition.");
                var pins = new HashSet<string>();
                void ValidatePins(List<ShaderGraphInterfacePort> list)
                {
                    foreach (var pin in list)
                        if (pin == null || !Identifier(pin.id) || !pins.Add(pin.id) || string.IsNullOrWhiteSpace(pin.name) ||
                            !Enum.IsDefined(typeof(ShaderGraphValueType), pin.type) || !Finite(pin.value) || pin.uvDefault && pin.type != ShaderGraphValueType.Float2)
                            throw new FormatException("Invalid Sub Graph port: " + definition.name);
                }
                ValidatePins(definition.inputs); ValidatePins(definition.outputs);
                foreach (var node in definition.nodes)
                    if (node == null || !nodeIds.Add(node.id)) throw new FormatException("Sub Graph node IDs must be unique across the document graph.");
                new ShaderGraphCompiler(graph.SubGraphView(definition)).ValidateStructure(false);
            }
            var active = new HashSet<string>(); var depths = new Dictionary<string, int>();
            int Depth(ShaderGraphSubGraph definition)
            {
                if (depths.TryGetValue(definition.id, out int depth)) return depth;
                if (!active.Add(definition.id)) throw new FormatException("Recursive Sub Graph calls are not allowed: " + definition.name);
                depth = 1;
                foreach (var node in definition.nodes)
                    if (node.kind == ShaderGraphNodeKind.SubGraph) depth = Math.Max(depth, 1 + Depth(graph.FindSubGraph(node.subGraphId)));
                if (depth > 16) throw new FormatException("Sub Graph nesting exceeds 16 levels.");
                active.Remove(definition.id); depths.Add(definition.id, depth);
                return depth;
            }
            foreach (var definition in graph.subGraphs) Depth(definition);
        }
    }
}
