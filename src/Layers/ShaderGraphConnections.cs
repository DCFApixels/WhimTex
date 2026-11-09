using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphConnections
    {
        internal static string ImplicitDependency(ShaderGraphNode node) => node.kind == ShaderGraphNodeKind.ForEnd ? node.loopPartnerId : null;

        // Structural dependencies include visible inactive inputs. Callers supply their
        // validated connection map; open reroutes are removed after cycle validation.
        internal static DependencyEnumerable Dependencies(ShaderGraphNode node, IReadOnlyDictionary<string, ShaderGraphEdge> incoming)
            => new DependencyEnumerable(node, incoming);

        internal readonly struct DependencyEnumerable
        {
            readonly ShaderGraphNode node;
            readonly IReadOnlyDictionary<string, ShaderGraphEdge> incoming;
            internal DependencyEnumerable(ShaderGraphNode node, IReadOnlyDictionary<string, ShaderGraphEdge> incoming)
            { this.node = node; this.incoming = incoming; }
            public Enumerator GetEnumerator() => new Enumerator(node, incoming);
            internal struct Enumerator
            {
                readonly ShaderGraphNode node;
                readonly IReadOnlyList<ShaderGraphPort> ports;
                readonly IReadOnlyDictionary<string, ShaderGraphEdge> incoming;
                int index;
                public string Current { get; private set; }
                internal Enumerator(ShaderGraphNode node, IReadOnlyDictionary<string, ShaderGraphEdge> incoming)
                { this.node = node; this.incoming = incoming; ports = ShaderGraphNodes.Inputs(node); index = -1; Current = null; }
                public bool MoveNext()
                {
                    if (index < 0)
                    {
                        index = 0;
                        Current = ImplicitDependency(node);
                        if (Current != null) return true;
                    }
                    while (index < ports.Count)
                        if (incoming.TryGetValue(node.id + ":" + ports[index++].name, out var edge))
                        { Current = edge.outputNode; return true; }
                    return false;
                }
            }
        }

        enum State { Visiting, Bound, Unbound }
        readonly Dictionary<string, string> sources = new();
        readonly Dictionary<string, State> routes = new();
        readonly List<string> path = new();

        internal bool IsUnbound(string nodeId) => nodeId != null && routes.TryGetValue(nodeId, out var state) && state == State.Unbound;

        internal void Prepare(IReadOnlyDictionary<string, ShaderGraphNode> nodes, ShaderGraph graph)
        {
            sources.Clear(); routes.Clear(); path.Clear();
            void AddEdges(List<ShaderGraphEdge> edges)
            {
                if (edges == null) return;
                foreach (var edge in edges)
                    if (edge?.inputNode != null && edge.outputNode != null && edge.inputPort == "In" &&
                        nodes.TryGetValue(edge.inputNode, out var target) && target.kind == ShaderGraphNodeKind.Reroute &&
                        nodes.TryGetValue(edge.outputNode, out var source) && ShaderGraphNodes.OutputVisible(source, edge.outputPort))
                        sources.TryAdd(target.id, source.id);
            }
            AddEdges(graph?.edges);
            if (graph?.subGraphs != null) foreach (var definition in graph.subGraphs)
                if (definition != null && definition != graph.activeSubGraph) AddEdges(definition.edges);
            foreach (var node in nodes.Values)
            {
                if (node.kind != ShaderGraphNodeKind.Reroute || routes.ContainsKey(node.id)) continue;
                path.Clear();
                var current = node;
                var result = State.Bound;
                while (current.kind == ShaderGraphNodeKind.Reroute)
                {
                    if (routes.TryGetValue(current.id, out var state))
                    {
                        // Cycles remain connected for the ordinary cycle validator.
                        result = state == State.Unbound ? State.Unbound : State.Bound;
                        break;
                    }
                    routes.Add(current.id, State.Visiting); path.Add(current.id);
                    if (!sources.TryGetValue(current.id, out var source)) { result = State.Unbound; break; }
                    current = nodes[source];
                }
                foreach (string id in path) routes[id] = result;
            }
            path.Clear();
        }

        internal void RemoveUnboundInputs(Dictionary<string, ShaderGraphEdge> incoming)
        {
            path.Clear();
            foreach (var pair in incoming) if (IsUnbound(pair.Value.outputNode)) path.Add(pair.Key);
            foreach (string key in path) incoming.Remove(key);
            path.Clear();
        }
    }
}
