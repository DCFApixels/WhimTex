using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphPreviewState
    {
        internal readonly ShaderGraphBindingState bindings = new ShaderGraphBindingState();
        readonly Dictionary<string, List<ShaderGraphEdge>> incoming = new Dictionary<string, List<ShaderGraphEdge>>();
        readonly Dictionary<string, ulong> fingerprints = new Dictionary<string, ulong>();
        readonly HashSet<string> visiting = new HashSet<string>();
        readonly ShaderGraph scratch = new ShaderGraph();
        ulong context;

        internal void Prepare(ShaderGraph graph)
        {
            bindings.Prepare(graph);
            fingerprints.Clear(); visiting.Clear();
            foreach (var edges in incoming.Values) edges.Clear();
            foreach (var edge in graph.edges)
            {
                if (edge?.inputNode == null) continue;
                if (!incoming.TryGetValue(edge.inputNode, out var edges)) incoming.Add(edge.inputNode, edges = new List<ShaderGraphEdge>());
                edges.Add(edge);
            }
            scratch.version = graph.version;
            scratch.nodes.Clear();
            scratch.parameters = graph.parameters; scratch.functions = graph.functions;
            scratch.sources = graph.sources; scratch.subGraphs = graph.subGraphs;
            context = ShaderGraphCompiler.CodeFingerprint(scratch);
            scratch.parameters = null; scratch.functions = null; scratch.sources = null; scratch.subGraphs = null;
            // Interface views also depend on the containing definition and its exposed pins.
            if (graph.activeSubGraph != null) context = RenderFingerprint.Mix(context, bindings.Fingerprint);
        }

        internal ulong Fingerprint(string nodeId, int depth = 0)
        {
            if (nodeId == null || !bindings.nodes.TryGetValue(nodeId, out var node)) return context;
            if (fingerprints.TryGetValue(nodeId, out var cached)) return cached;
            if (depth > 256 || !visiting.Add(nodeId)) return bindings.Fingerprint;
            scratch.nodes.Clear(); scratch.nodes.Add(node);
            ulong hash = RenderFingerprint.Mix(context, ShaderGraphCompiler.CodeFingerprint(scratch));
            if (incoming.TryGetValue(nodeId, out var edges)) foreach (var edge in edges)
            {
                hash = RenderFingerprint.Mix(hash, Fingerprint(edge.outputNode, depth + 1));
                Add(edge.inputPort); Add(edge.outputPort);
            }
            visiting.Remove(nodeId);
            fingerprints[nodeId] = hash;
            return hash;
            void Add(string text)
            {
                if (text != null) foreach (char c in text) hash = RenderFingerprint.Mix(hash, c);
                hash = RenderFingerprint.Mix(hash, 255);
            }
        }
    }
}
