using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphPreviewState
    {
        internal readonly ShaderGraphBindingState bindings = new();
        readonly Dictionary<string, List<ShaderGraphEdge>> incoming = new();
        readonly HashSet<(string node, string port)> connected = new();
        readonly Dictionary<string, (ulong code, ulong values)> stamps = new();
        readonly Dictionary<string, (ulong code, ulong values)> local = new();
        readonly Dictionary<string, List<string>> outgoing = new();
        readonly HashSet<string> changed = new();
        readonly HashSet<string> directlyChanged = new();
        readonly Stack<string> affected = new();
        readonly HashSet<string> visiting = new();
        readonly Stack<(string id, bool finish)> pending = new();
        readonly ShaderGraph scratch = new();
        readonly HashSet<string> parameters = new(), functions = new(), sources = new(), definitions = new();
        readonly Dictionary<string, ShaderGraphSource> sourceIndex = new();
        readonly List<string> removed = new();
        ulong valueHash;

        internal void Prepare(ShaderGraph graph)
        {
            bool same = ReferenceEquals(bindings.Graph, graph);
            graph?.BindFunctions();
            bindings.Prepare(graph);
            if (!same) { stamps.Clear(); local.Clear(); }
            visiting.Clear(); pending.Clear(); sourceIndex.Clear(); connected.Clear(); changed.Clear(); directlyChanged.Clear();
            foreach (var list in incoming.Values) list.Clear();
            removed.Clear();
            foreach (var pair in incoming) if (!bindings.nodes.ContainsKey(pair.Key)) removed.Add(pair.Key);
            foreach (string id in removed) incoming.Remove(id);
            if (graph == null) return;
            foreach (var source in graph.sources) if (source != null) sourceIndex[source.id] = source;
            AddEdges(graph.edges);
            foreach (var definition in graph.subGraphs)
                if (definition != null && definition != graph.activeSubGraph) AddEdges(definition.edges);
            foreach (var list in outgoing.Values) list.Clear();
            foreach (var pair in bindings.nodes)
            {
                var node = pair.Value;
                PrepareLocal(node);
                ulong code = LocalCode(node), values = valueHash;
                if (node.kind == ShaderGraphNodeKind.ForEnd) Link(node.loopPartnerId, node.id);
                if (incoming.TryGetValue(node.id, out var edges)) foreach (var edge in edges)
                { code = Text(Text(Text(code, edge.outputNode), edge.outputPort), edge.inputPort); Link(edge.outputNode, node.id); }
                if (!local.TryGetValue(node.id, out var old) || old.code != code || old.values != values) changed.Add(node.id);
                local[node.id] = (code, values);
            }
            removed.Clear();
            foreach (var id in local.Keys) if (!bindings.nodes.ContainsKey(id)) removed.Add(id);
            foreach (var id in removed) { local.Remove(id); stamps.Remove(id); outgoing.Remove(id); }
            PropagateChanges();
        }
        internal bool PrepareNode(ShaderGraph graph, string id)
        {
            if (!ReferenceEquals(bindings.Graph, graph) || graph.activeSubGraph != null || id == null || !bindings.nodes.TryGetValue(id, out var node) ||
                !ReferenceEquals(node, graph.FindNode(id))) return false;
            bindings.RefreshFingerprint(); changed.Clear();
            PrepareLocal(node);
            ulong code = LocalCode(node), values = valueHash;
            if (incoming.TryGetValue(id, out var edges)) foreach (var edge in edges)
                code = Text(Text(Text(code, edge.outputNode), edge.outputPort), edge.inputPort);
            if (!local.TryGetValue(id, out var old) || old.code != code || old.values != values) changed.Add(id);
            local[id] = (code, values);
            PropagateChanges();
            return true;
        }
        void PropagateChanges()
        {
            directlyChanged.Clear(); directlyChanged.UnionWith(changed);
            foreach (var id in changed) affected.Push(id);
            while (affected.Count > 0)
            {
                string id = affected.Pop(); stamps.Remove(id);
                if (outgoing.TryGetValue(id, out var children)) foreach (string child in children)
                    if (changed.Add(child)) affected.Push(child);
            }
        }
        void Link(string source, string target)
        {
            if (source == null) return;
            if (!outgoing.TryGetValue(source, out var list)) outgoing.Add(source, list = new());
            list.Add(target);
        }
        ulong LocalCode(ShaderGraphNode node)
        {
            ulong code = RenderFingerprint.Mix(ShaderGraphCompiler.CodeFingerprint(scratch), (ulong)ShaderProgramSourceRevision.Current);
            if (node.interfacePort is ShaderGraphInterfacePort pin)
            { code = RenderFingerprint.Mix(code, (ulong)pin.type); code = RenderFingerprint.Mix(code, pin.uvDefault ? 1UL : 0UL); Value(pin.value); }
            return code;
        }
        internal bool IsChanged(string id) => changed.Contains(id);
        internal bool IsDirectlyChanged(string id) => directlyChanged.Contains(id);
        void AddEdges(List<ShaderGraphEdge> edges)
        {
            if (edges == null) return;
            foreach (var edge in edges)
            {
                if (edge?.inputNode == null || !bindings.nodes.TryGetValue(edge.inputNode, out var target) ||
                    !bindings.nodes.TryGetValue(edge.outputNode ?? "", out var sourceNode) ||
                    !ShaderGraphNodes.InputVisible(target, edge.inputPort) ||
                    !ShaderGraphNodes.OutputVisible(sourceNode, edge.outputPort)) continue;
                // Match ValidateStructure/BranchContainsBuffer, which also traverse visible
                // inactive inputs. Their topology can change cycles and Fractal legality.
                connected.Add((edge.inputNode, edge.inputPort));
                if (!incoming.TryGetValue(edge.inputNode, out var list)) incoming.Add(edge.inputNode, list = new());
                list.Add(edge);
            }
        }
        // Code and bound values propagate separately: uniform edits need rendering, not compilation.
        internal ulong Fingerprint(string nodeId) { Ensure(nodeId); return nodeId != null && stamps.TryGetValue(nodeId, out var stamp) ? stamp.code : 0; }
        internal ulong ValueFingerprint(string nodeId) { Ensure(nodeId); return nodeId != null && stamps.TryGetValue(nodeId, out var stamp) ? stamp.values : 0; }
        void Ensure(string nodeId)
        {
            if (nodeId == null || stamps.ContainsKey(nodeId) || !bindings.nodes.ContainsKey(nodeId)) return;
            pending.Push((nodeId, false));
            while (pending.Count != 0)
            {
                var next = pending.Pop();
                if (next.id == null || stamps.ContainsKey(next.id) || !bindings.nodes.TryGetValue(next.id, out var node)) continue;
                if (!next.finish)
                {
                    if (!visiting.Add(next.id)) { stamps[next.id] = (bindings.Fingerprint, bindings.Fingerprint); continue; }
                    pending.Push((next.id, true));
                    if (node.kind == ShaderGraphNodeKind.ForEnd && node.loopPartnerId != null) pending.Push((node.loopPartnerId, false));
                    if (incoming.TryGetValue(next.id, out var list)) foreach (var edge in list) pending.Push((edge.outputNode, false));
                    continue;
                }
                var own = local[next.id];
                ulong code = own.code;
                ulong values = RenderFingerprint.Mix(code, own.values);
                void Dependency(string id, bool evaluates = true)
                {
                    if (id == null || !stamps.TryGetValue(id, out var upstream)) return;
                    code = RenderFingerprint.Mix(code, upstream.code);
                    if (evaluates) values = RenderFingerprint.Mix(values, upstream.values);
                }
                if (node.kind == ShaderGraphNodeKind.ForEnd) Dependency(node.loopPartnerId);
                if (incoming.TryGetValue(next.id, out var edges)) foreach (var edge in edges)
                {
                    bool evaluates = ShaderGraphParameters.EvaluatesInput(node, edge.inputPort);
                    Dependency(edge.outputNode, evaluates);
                    code = Text(Text(code, edge.inputPort), edge.outputPort);
                    if (evaluates) values = Text(Text(values, edge.inputPort), edge.outputPort);
                }
                stamps[next.id] = (code, values); visiting.Remove(next.id);
            }
            visiting.Clear();
        }
        void PrepareLocal(ShaderGraphNode node)
        {
            scratch.version = bindings.Graph.version;
            scratch.nodes.Clear(); scratch.nodes.Add(node);
            scratch.parameters.Clear(); scratch.functions.Clear(); scratch.sources.Clear(); scratch.subGraphs.Clear();
            parameters.Clear(); functions.Clear(); sources.Clear(); definitions.Clear();
            valueHash = 14695981039346656037UL;
            Collect(node);
        }
        void Collect(ShaderGraphNode node)
        {
            foreach (var port in ShaderGraphNodes.Inputs(node))
                if (ShaderGraphParameters.EvaluatesInput(node, port.name) && !connected.Contains((node.id, port.name)))
                    InputValue(node, port);
            if (node.kind == ShaderGraphNodeKind.Parameter && node.parameterId != null && parameters.Add(node.parameterId) &&
                bindings.Graph.FindParameter(node.parameterId) is ShaderParameter parameter)
            { scratch.parameters.Add(parameter); valueHash = ShaderFX.HashParameter(valueHash, parameter); }
            if (node.kind == ShaderGraphNodeKind.CustomFunction && node.functionId != null && functions.Add(node.functionId) &&
                bindings.Graph.FindFunction(node.functionId) is ShaderGraphFunction function)
            {
                scratch.functions.Add(function); Source(function.sourceId);
            }
            if (node.kind == ShaderGraphNodeKind.SubGraph && node.subGraphId != null && definitions.Add(node.subGraphId) &&
                bindings.Graph.FindSubGraph(node.subGraphId) is ShaderGraphSubGraph definition)
            {
                scratch.subGraphs.Add(definition);
                foreach (var child in definition.nodes) if (child != null) Collect(child);
            }
        }
        void InputValue(ShaderGraphNode node, ShaderGraphPort port)
        {
            var input = node.FindInput(port.name);
            var value = input?.value ?? port.value;
            // UV defaults read no literal components except Z for a float3 position.
            if (port.uvDefault && (input == null || input.source != ShaderGraphInputSource.Value))
            {
                if (port.type == ShaderGraphValueType.Float3)
                    valueHash = RenderFingerprint.Float(Text(valueHash, port.name), value.z);
                return;
            }
            if (port.canvasSizeDefault && input == null) return;
            var type = input?.type ?? port.type ?? ShaderGraphValueType.Float;
            if (node.kind == ShaderGraphNodeKind.Expression) type = node.valueType;
            if (node.kind == ShaderGraphNodeKind.Swizzle) type = ShaderGraphNodes.SwizzleInputType(node.swizzle, type);
            valueHash = Text(valueHash, port.name);
            if (ShaderGraphInput.IsResource(type))
                valueHash = ShaderFX.HashParameter(valueHash, input?.resource);
            else for (int i = 0; i < ShaderGraphNodes.Components(type); i++)
                valueHash = RenderFingerprint.Float(valueHash, value[i]);
        }
        void Source(string id)
        {
            if (id == null || !sources.Add(id) || !sourceIndex.TryGetValue(id, out var source)) return;
            scratch.sources.Add(source);
            if (source.includes != null) foreach (var include in source.includes) if (include != null) Source(include.sourceId);
        }
        void Value(UnityEngine.Vector4 value)
        { for (int i = 0; i < 4; i++) valueHash = RenderFingerprint.Float(valueHash, value[i]); }
        static ulong Text(ulong hash, string text)
        {
            if (text != null) foreach (char c in text) hash = RenderFingerprint.Mix(hash, c);
            return RenderFingerprint.Mix(hash, 255);
        }
    }
}
