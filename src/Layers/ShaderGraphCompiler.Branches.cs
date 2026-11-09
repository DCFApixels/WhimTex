using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphBranchTrace
    {
        internal readonly HashSet<string> nodes = new();
        internal readonly HashSet<string> buffers = new();
        internal readonly HashSet<ShaderGraphEdge> edges = new();
        internal readonly HashSet<string> functions = new();
    }

    internal sealed partial class ShaderGraphCompiler
    {
        ShaderGraphBranchTrace activeTrace;

        internal static ShaderGraphBranchTrace TraceInputBranch(ShaderGraph graph, string nodeId, string port, bool effectContext)
        {
            var source = graph.edges.Find(e => e.inputNode == nodeId && e.inputPort == port);
            return source == null ? new ShaderGraphBranchTrace() : Prepare(graph, effectContext).TraceInputBranch(source);
        }

        internal sealed partial class Prepared
        {
            internal ShaderGraphBranchTrace TraceInputBranch(ShaderGraphEdge source)
            {
                if (expansion != null)
                    foreach (var pair in expansion.rootEdges)
                        if (ReferenceEquals(pair.Value, source)) { source = pair.Key; break; }
                var trace = new ShaderGraphBranchTrace();
                var compiler = CreateCompiler();
                compiler.activeTrace = trace;
                compiler.previewLoopInputs = true;
                compiler.TraceInput(source);
                compiler.Emit(source.outputNode, source.outputPort);
                if (expansion == null) return trace;
                var mapped = new ShaderGraphBranchTrace();
                foreach (string id in trace.nodes) if (expansion.rootNodes.TryGetValue(id, out var root)) mapped.nodes.Add(root);
                foreach (string id in trace.buffers) if (expansion.rootNodes.TryGetValue(id, out var root)) mapped.buffers.Add(root);
                foreach (var edge in trace.edges) if (expansion.rootEdges.TryGetValue(edge, out var root)) mapped.edges.Add(root);
                return mapped;
            }
        }

        void TraceInput(ShaderGraphEdge edge)
        {
            activeTrace?.edges.Add(edge);
        }

        void TraceNode(ShaderGraphNode node)
        {
            if (activeTrace == null) return;
            activeTrace.nodes.Add(node.id);
            if (ShaderGraphNodes.IsBuffered(node.kind)) activeTrace.buffers.Add(node.id);
        }
    }
}
