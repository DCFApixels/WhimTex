using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    // Shared by the node previews for one graph revision, including animated frames.
    internal sealed class ShaderGraphBindingState
    {
        internal readonly Dictionary<string, ShaderGraphNode> nodes = new Dictionary<string, ShaderGraphNode>();
        internal readonly Dictionary<string, ShaderGraphSubGraph> definitions = new Dictionary<string, ShaderGraphSubGraph>();
        internal ShaderGraph Graph { get; private set; }
        internal ulong Fingerprint { get; private set; }

        internal void Prepare(ShaderGraph graph)
        {
            Graph = graph;
            Fingerprint = ShaderGraphCompiler.CodeFingerprint(graph);
            nodes.Clear(); definitions.Clear();
            if (graph?.nodes != null) foreach (var node in graph.nodes) if (node?.id != null) nodes[node.id] = node;
            if (graph?.subGraphs != null) foreach (var definition in graph.subGraphs)
                if (definition?.nodes != null) foreach (var node in definition.nodes) if (node?.id != null)
                { nodes[node.id] = node; definitions[node.id] = definition; }
        }
    }
}
