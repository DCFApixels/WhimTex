using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class ShaderGraphInterfacePort
    {
        public string id = Guid.NewGuid().ToString("N"), name = "Value";
        public ShaderGraphValueType type;
        public Vector4 value;
        public bool uvDefault;
    }

    [Serializable]
    public sealed class ShaderGraphSubGraph
    {
        public string id = Guid.NewGuid().ToString("N"), name = "Sub Graph";
        public List<ShaderGraphInterfacePort> inputs = new List<ShaderGraphInterfacePort>();
        public List<ShaderGraphInterfacePort> outputs = new List<ShaderGraphInterfacePort>();
        public List<ShaderGraphNode> nodes = new List<ShaderGraphNode>();
        public List<ShaderGraphEdge> edges = new List<ShaderGraphEdge>();
        public List<ShaderGraphNote> notes = new List<ShaderGraphNote>();
        public List<ShaderGraphGroup> groups = new List<ShaderGraphGroup>();

        internal string[] OutputNames()
        {
            var names = new string[outputs.Count];
            for (int i = 0; i < names.Length; i++) names[i] = outputs[i].id;
            return names;
        }
    }

    public sealed partial class ShaderGraph
    {
        public ShaderGraphSubGraph FindSubGraph(string id)
        {
            if (subGraphs != null) foreach (var definition in subGraphs) if (definition != null && definition.id == id) return definition;
            return null;
        }
        internal ShaderGraph SubGraphView(ShaderGraphSubGraph definition) => new ShaderGraph
        {
            version = version, activeSubGraph = definition, nodes = definition.nodes, edges = definition.edges,
            notes = definition.notes, groups = definition.groups, parameters = parameters, functions = functions,
            sources = sources, subGraphs = subGraphs
        };

        internal IEnumerable<ShaderGraphNode> AllNodes()
        {
            if (nodes != null) foreach (var node in nodes) if (node != null) yield return node;
            if (subGraphs != null) foreach (var definition in subGraphs)
                if (definition != null && definition != activeSubGraph && definition.nodes != null)
                    foreach (var node in definition.nodes) if (node != null) yield return node;
        }

        void BindNodeDefinitions(List<ShaderGraphNode> list, ShaderGraphSubGraph owner)
        {
            if (list == null) return;
            foreach (var node in list)
            {
                if (node == null) continue;
                node.function = node.kind == ShaderGraphNodeKind.CustomFunction ? FindFunction(node.functionId) : null;
                node.subGraph = node.kind == ShaderGraphNodeKind.SubGraph ? FindSubGraph(node.subGraphId) : owner;
                if (!node.expandedInterface)
                    node.interfacePort = node.kind == ShaderGraphNodeKind.SubGraphInput ? owner?.inputs?.Find(p => p?.id == node.parameterId) : null;
            }
        }
    }

    internal static partial class ShaderGraphNodes
    {
        internal static bool IsSubGraphInterface(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.SubGraphInput or ShaderGraphNodeKind.SubGraphOutput;
        static IReadOnlyList<ShaderGraphPort> SubGraphInputs(ShaderGraphNode node)
        {
            if (node.kind == ShaderGraphNodeKind.SubGraphInput)
                return node.expandedInterface && node.interfacePort != null
                    ? new[] { new ShaderGraphPort("In", node.interfacePort.type, node.interfacePort.value, node.interfacePort.uvDefault) } : none;
            var list = node.kind == ShaderGraphNodeKind.SubGraph ? node.subGraph?.inputs
                : node.kind == ShaderGraphNodeKind.SubGraphOutput ? node.subGraph?.outputs : null;
            if (list == null) return null;
            var result = new ShaderGraphPort[list.Count];
            for (int i = 0; i < result.Length; i++) result[i] = new ShaderGraphPort(list[i].id, list[i].type, list[i].value, list[i].uvDefault);
            return result;
        }
        internal static string PortDisplayName(ShaderGraphNode node, string port, bool output)
        {
            if (node == null) return port;
            if (node.kind == ShaderGraphNodeKind.Output && !output && port == "AlphaScale") return "Alpha Scale";
            if (output && port == "Out" && node.kind is ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.SampleGradient) return "RGBA";
            if (output && node.kind == ShaderGraphNodeKind.CanvasPosition)
                return port == "AspectX" ? "Aspect X" : port == "AspectY" ? "Aspect Y" : port;
            if (output && node.kind == ShaderGraphNodeKind.CanvasData && port == "PixelScale") return "Pixel Scale";
            var list = node.kind == ShaderGraphNodeKind.SubGraph ? (output ? node.subGraph?.outputs : node.subGraph?.inputs)
                : node.kind == ShaderGraphNodeKind.SubGraphOutput ? node.subGraph?.outputs : null;
            return list?.Find(p => p?.id == port)?.name ?? port;
        }
        internal static Vector4 InputValue(ShaderGraphNode node, string port, Vector4 fallback)
        {
            var input = node?.FindInput(port);
            if (input != null) return input.value;
            var pins = node?.kind == ShaderGraphNodeKind.SubGraph ? node.subGraph?.inputs
                : node?.kind == ShaderGraphNodeKind.SubGraphOutput ? node.subGraph?.outputs : null;
            if (pins != null) foreach (var pin in pins) if (pin.id == port) return pin.value;
            return fallback;
        }
    }

    internal static class ShaderGraphExpansion
    {
        internal readonly struct Endpoint
        {
            internal readonly string node, port;
            internal Endpoint(string node, string port) { this.node = node; this.port = port; }
        }
        internal sealed class Result
        {
            internal ShaderGraph graph;
            internal readonly Dictionary<string, Endpoint> rootOutputs = new Dictionary<string, Endpoint>();
            internal Dictionary<string, string> rootNodes;
            internal Dictionary<ShaderGraphEdge, ShaderGraphEdge> rootEdges;
        }
        internal static Result Expand(ShaderGraph root, bool trackOrigins = false)
        {
            root.BindFunctions();
            var result = new Result { graph = new ShaderGraph { parameters = root.parameters, functions = root.functions, sources = root.sources } };
            if (trackOrigins) { result.rootNodes = new(); result.rootEdges = new(); }
            int serial = 0;
            var reserved = new HashSet<string>();
            foreach (var node in root.AllNodes()) reserved.Add(node.id);
            string Id() { string id; do { id = "expanded_" + ++serial; } while (!reserved.Add(id)); return id; }
            ShaderGraphNode Adapter(ShaderGraphNode origin, ShaderGraphInterfacePort pin, string owner = null)
            {
                var adapter = new ShaderGraphNode {
                    id = Id(), kind = ShaderGraphNodeKind.SubGraphInput, expandedInterface = true, interfacePort = pin,
                    uniformNodeId = origin.id, uniformPort = pin.id, position = origin.position
                };
                var value = origin.FindInput(pin.id);
                if (value != null || ShaderGraphNodes.Components(pin.type) > 0 && !pin.uvDefault)
                    adapter.inputs.Add(new ShaderGraphInput { port = "In", type = value?.type ?? pin.type, value = value?.value ?? pin.value,
                        resource = value?.resource, source = value?.source ?? ShaderGraphInputSource.Value });
                result.graph.nodes.Add(adapter);
                result.rootNodes?.Add(adapter.id, owner ?? origin.id);
                return adapter;
            }
            void Scope(List<ShaderGraphNode> scopeNodes, List<ShaderGraphEdge> scopeEdges, Dictionary<string, Endpoint> arguments,
                Dictionary<string, Endpoint> returns, bool top, int depth, string owner = null)
            {
                if (depth > 16) throw new FormatException("Sub Graph nesting exceeds 16 levels.");
                var outputs = top ? result.rootOutputs : new Dictionary<string, Endpoint>();
                var targets = new Dictionary<string, Endpoint>();
                var copies = new List<ShaderGraphNode>(); var nodeIds = new Dictionary<string, string>();
                var children = new List<(ShaderGraphSubGraph definition, Dictionary<string, Endpoint> inputs, Dictionary<string, Endpoint> outputs, string owner)>();
                foreach (var n in scopeNodes)
                {
                    if (n.kind == ShaderGraphNodeKind.SubGraphInput)
                    {
                        if (arguments != null && arguments.TryGetValue(n.parameterId, out var arg)) outputs.Add(n.id + ":Out", arg);
                        else if (top && root.activeSubGraph != null)
                        {
                            var adapter = Adapter(n, n.interfacePort);
                            outputs.Add(n.id + ":Out", new Endpoint(adapter.id, "Out"));
                        }
                        else throw new FormatException("Sub Graph input is missing its interface declaration.");
                    }
                    else if (n.kind == ShaderGraphNodeKind.SubGraphOutput)
                    {
                        if (returns != null) foreach (var pin in n.subGraph.outputs) targets.Add(n.id + ":" + pin.id, returns[pin.id]);
                    }
                    else if (n.kind == ShaderGraphNodeKind.SubGraph)
                    {
                        if (n.subGraph == null) throw new ShaderGraphException(n.id, "Sub Graph definition is missing.");
                        var args = new Dictionary<string, Endpoint>(); var outs = new Dictionary<string, Endpoint>();
                        foreach (var pin in n.subGraph.inputs)
                        {
                            var adapter = Adapter(n, pin, owner);
                            args.Add(pin.id, new Endpoint(adapter.id, "Out"));
                            targets.Add(n.id + ":" + pin.id, new Endpoint(adapter.id, "In"));
                        }
                        foreach (var pin in n.subGraph.outputs)
                        {
                            var adapter = Adapter(n.subGraph.nodes.Find(item => item.kind == ShaderGraphNodeKind.SubGraphOutput), pin, owner ?? n.id);
                            outs.Add(pin.id, new Endpoint(adapter.id, "In"));
                            outputs.Add(n.id + ":" + pin.id, new Endpoint(adapter.id, "Out"));
                        }
                        children.Add((n.subGraph, args, outs, owner ?? n.id));
                    }
                    else
                    {
                        var clone = n.CopyForCompilation(); clone.id = top ? n.id : Id(); clone.uniformNodeId = n.id;
                        copies.Add(clone); nodeIds.Add(n.id, clone.id);
                        result.graph.nodes.Add(clone);
                        result.rootNodes?.Add(clone.id, owner ?? n.id);
                        foreach (var port in ShaderGraphNodes.StoredOutputs(n)) outputs.Add(n.id + ":" + port, new Endpoint(clone.id, port));
                        foreach (var port in ShaderGraphNodes.StoredInputs(n)) targets.Add(n.id + ":" + port.name, new Endpoint(clone.id, port.name));
                    }
                    if (result.graph.nodes.Count > 4096) throw new FormatException("Expanded Shader graph exceeds 4096 nodes.");
                }
                ShaderGraphLoops.RemapPartners(copies, nodeIds);
                foreach (var edge in scopeEdges)
                {
                    if (!outputs.TryGetValue(edge.outputNode + ":" + edge.outputPort, out var from) || !targets.TryGetValue(edge.inputNode + ":" + edge.inputPort, out var to))
                        throw new FormatException("Sub Graph connection has a missing node or port.");
                    var expandedEdge = new ShaderGraphEdge { outputNode = from.node, outputPort = from.port, inputNode = to.node, inputPort = to.port };
                    result.graph.edges.Add(expandedEdge);
                    if (top) result.rootEdges?.Add(expandedEdge, edge);
                }
                foreach (var child in children) Scope(child.definition.nodes, child.definition.edges, child.inputs, child.outputs, false, depth + 1, child.owner);
            }
            Dictionary<string, Endpoint> topReturns = null;
            if (root.activeSubGraph != null)
            {
                topReturns = new Dictionary<string, Endpoint>();
                var terminal = root.nodes.Find(n => n.kind == ShaderGraphNodeKind.SubGraphOutput);
                foreach (var pin in root.activeSubGraph.outputs)
                {
                    var adapter = Adapter(terminal, pin);
                    topReturns.Add(pin.id, new Endpoint(adapter.id, "In"));
                }
                var output = ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, Vector2.zero);
                result.graph.nodes.Add(output);
                foreach (var pin in root.activeSubGraph.outputs)
                    if (ShaderGraphNodes.IsNumeric(pin.type))
                    {
                        result.graph.edges.Add(new ShaderGraphEdge { outputNode = topReturns[pin.id].node, inputNode = output.id, inputPort = "RGBA" });
                        break;
                    }
            }
            Scope(root.nodes, root.edges, null, topReturns, true, 0);
            return result;
        }
    }
}
