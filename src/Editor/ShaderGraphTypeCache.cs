using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphTypeCache
    {
        // Reuse topology and resolved outputs until the graph changes, including shared branches.
        internal ShaderGraph Graph { get; private set; }
        internal readonly Dictionary<string, ShaderGraphNode> nodes = new();
        readonly Dictionary<(string, string), ShaderGraphEdge> incoming = new();
        readonly ShaderGraphConnections connections = new();
        readonly HashSet<(string, string)> connectedOutputs = new();
        readonly Dictionary<(string, string), ShaderGraphValueType> outputs = new();
        readonly HashSet<(string, string)> visiting = new();
        readonly Stack<(ShaderGraphNode node, string port, bool finish)> pending = new();

        internal void Prepare(ShaderGraph graph)
        {
            Graph = graph;
            nodes.Clear(); incoming.Clear(); connectedOutputs.Clear(); outputs.Clear(); visiting.Clear(); pending.Clear();
            if (graph == null) return;
            foreach (var node in graph.nodes) if (node?.id != null) nodes[node.id] = node;
            foreach (var edge in graph.edges)
                if (edge != null)
                {
                    incoming.TryAdd((edge.inputNode, edge.inputPort), edge);
                    connectedOutputs.Add((edge.outputNode, edge.outputPort));
                }
            connections.Prepare(nodes, graph);
        }
        internal ShaderGraphNode Node(string id) => id != null && nodes.TryGetValue(id, out var node) ? node : null;
        internal bool HasInput(string id, string port) => incoming.ContainsKey((id, port));
        internal bool HasOutput(string id, string port) => connectedOutputs.Contains((id, port));
        ShaderGraphEdge Source(ShaderGraphNode node, string port) =>
            incoming.TryGetValue((node.id, port), out var edge) && !connections.IsUnbound(edge.outputNode) &&
                ShaderGraphNodes.OutputVisible(Node(edge.outputNode), edge.outputPort) ? edge : null;

        internal ShaderGraphValueType Input(ShaderGraphNode node, string port)
        {
            if (node == null) return ShaderGraphValueType.Float;
            if (node.kind == ShaderGraphNodeKind.Expression) return node.valueType;
            var type = node.FindInput(port)?.type ?? (node.kind == ShaderGraphNodeKind.Reroute ? node.valueType : ShaderGraphValueType.Float);
            if (Source(node, port) is ShaderGraphEdge edge) type = Output(Node(edge.outputNode), edge.outputPort);
            return node.kind == ShaderGraphNodeKind.Swizzle ? ShaderGraphNodes.SwizzleInputType(node.swizzle, type) : type;
        }

        internal ShaderGraphValueType Output(ShaderGraphNode node, string port)
        {
            if (node == null) return ShaderGraphValueType.Float;
            var key = (node.id, port);
            if (outputs.TryGetValue(key, out var value)) return value;
            pending.Push((node, port, false));
            while (pending.Count != 0)
            {
                var next = pending.Pop(); var current = (next.node.id, next.port);
                if (outputs.ContainsKey(current)) continue;
                if (Fixed(next.node, next.port, out value)) { outputs[current] = value; continue; }
                if (next.finish)
                {
                    outputs[current] = Evaluate(next.node); visiting.Remove(current); continue;
                }
                if (!visiting.Add(current)) { outputs[current] = ShaderGraphValueType.Float; continue; }
                pending.Push((next.node, next.port, true));
                if (ShaderGraphConnections.ImplicitDependency(next.node) is string implicitInput) Push(Node(implicitInput), "Value");
                else if (next.node.kind == ShaderGraphNodeKind.ForBegin) PushInput(next.node, "Initial");
                else if (next.node.kind == ShaderGraphNodeKind.Scatter) PushInput(next.node, "Source");
                else if (next.node.kind == ShaderGraphNodeKind.Reroute || ShaderGraphNodes.IsFilter(next.node.kind)) PushInput(next.node, "In");
                else foreach (var input in ShaderGraphNodes.Inputs(next.node)) if (!input.type.HasValue) PushInput(next.node, input.name);
            }
            visiting.Clear();
            return outputs[key];
        }
        void Push(ShaderGraphNode node, string port)
        { if (node != null && !outputs.ContainsKey((node.id, port))) pending.Push((node, port, false)); }
        void PushInput(ShaderGraphNode node, string port)
        { if (Source(node, port) is ShaderGraphEdge edge) Push(Node(edge.outputNode), edge.outputPort); }
        ShaderGraphValueType ReadyInput(ShaderGraphNode node, string port)
        {
            if (Source(node, port) is ShaderGraphEdge edge && outputs.TryGetValue((edge.outputNode, edge.outputPort), out var type)) return type;
            return node.FindInput(port)?.type ?? (node.kind == ShaderGraphNodeKind.Reroute ? node.valueType : ShaderGraphValueType.Float);
        }
        ShaderGraphValueType Evaluate(ShaderGraphNode node)
        {
            if (node.kind == ShaderGraphNodeKind.ForEnd)
                return node.loopPartnerId != null && outputs.TryGetValue((node.loopPartnerId, "Value"), out var type) ? type : ShaderGraphValueType.Float;
            if (node.kind == ShaderGraphNodeKind.ForBegin) return ReadyInput(node, "Initial");
            if (node.kind == ShaderGraphNodeKind.Scatter) return ReadyInput(node, "Source");
            if (node.kind == ShaderGraphNodeKind.Reroute || ShaderGraphNodes.IsFilter(node.kind)) return ReadyInput(node, "In");
            var result = ShaderGraphValueType.Float;
            foreach (var input in ShaderGraphNodes.Inputs(node))
                if (!input.type.HasValue)
                {
                    var type = ShaderGraphNodes.NumericType(ReadyInput(node, input.name));
                    if (ShaderGraphNodes.IsNumeric(type) && type > result) result = type;
                }
            return result;
        }
        bool Fixed(ShaderGraphNode node, string port, out ShaderGraphValueType type)
        {
            if (ShaderGraphNodes.TryNoiseOutputType(node, port, out type) || ShaderGraphNodes.TrySurfaceOutputType(node, port, out type) ||
                ShaderGraphNodes.TryProceduralOutputType(node.kind, out type) || ShaderGraphNodes.TryColorOutputType(node.kind, out type) ||
                ShaderGraphNodes.TryExtendedOutputType(node.kind, out type)) return true;
            type = ShaderGraphValueType.Float;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Scatter: type = ShaderGraphValueType.Color; return node.option == 2;
                case ShaderGraphNodeKind.ScatterInstance: type = port is "UV" or "Center" ? ShaderGraphValueType.Float2 : ShaderGraphValueType.Float; return true;
                case ShaderGraphNodeKind.ForBegin: return port == "Index";
                case ShaderGraphNodeKind.SubGraph: type = node.subGraph?.outputs.Find(p => p.id == port)?.type ?? type; return true;
                case ShaderGraphNodeKind.SubGraphInput: type = node.interfacePort?.type ?? type; return true;
                case ShaderGraphNodeKind.CustomFunction: type = Graph.FindFunction(node.functionId)?.OutputType(port) ?? type; return true;
                case ShaderGraphNodeKind.Parameter: type = Graph.FindParameter(node.parameterId) is ShaderParameter p ? ShaderGraphNodes.ParameterType(p) : type; return true;
                case ShaderGraphNodeKind.Constant: case ShaderGraphNodeKind.InstanceIndex: case ShaderGraphNodeKind.Time: return true;
                case ShaderGraphNodeKind.Combine: case ShaderGraphNodeKind.Expression: type = node.valueType; return true;
                case ShaderGraphNodeKind.Swizzle: type = ShaderGraphNodes.SwizzleType(node.swizzle); return true;
                case ShaderGraphNodeKind.CanvasData: type = port == "PixelScale" ? type : ShaderGraphValueType.Float2; return true;
                case ShaderGraphNodeKind.UV: case ShaderGraphNodeKind.CanvasPosition: case ShaderGraphNodeKind.TextureSize:
                case ShaderGraphNodeKind.TransformUV: case ShaderGraphNodeKind.ApplyTransform2D: type = ShaderGraphValueType.Float2; return true;
                case ShaderGraphNodeKind.SampleTexture: case ShaderGraphNodeKind.SampleGradient: type = port == "Out" ? ShaderGraphValueType.Color : type; return true;
                case ShaderGraphNodeKind.SampleInput: case ShaderGraphNodeKind.Blend: case ShaderGraphNodeKind.Output: type = ShaderGraphValueType.Color; return true;
                case ShaderGraphNodeKind.Split: case ShaderGraphNodeKind.Length: case ShaderGraphNodeKind.Distance: case ShaderGraphNodeKind.Dot:
                case ShaderGraphNodeKind.SampleCurve: case ShaderGraphNodeKind.Shape: return true;
                case ShaderGraphNodeKind.Compare: type = ShaderGraphValueType.Boolean; return true;
                default: return false;
            }
        }
    }
}
