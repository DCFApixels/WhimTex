using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed partial class Canvas
        {
            static void CollectSubGraphs(ShaderGraph fragment, ShaderGraph source, HashSet<string> parameters)
            {
                var used = new HashSet<string>();
                void Collect(List<ShaderGraphNode> nodes)
                {
                    foreach (var node in nodes)
                    {
                        if (node.kind == ShaderGraphNodeKind.Parameter) parameters.Add(node.parameterId);
                        if (node.kind != ShaderGraphNodeKind.SubGraph || !used.Add(node.subGraphId)) continue;
                        var definition = source.FindSubGraph(node.subGraphId);
                        if (definition == null) throw new FormatException("Sub Graph definition is missing.");
                        fragment.subGraphs.Add(definition); Collect(definition.nodes);
                    }
                }
                Collect(fragment.nodes);
            }

            static Dictionary<string, string> ImportSubGraphs(ShaderGraph fragment, ShaderGraph target, bool sameGraph, Dictionary<string, string> parameters)
            {
                var definitions = new Dictionary<string, string>(); var imported = new List<ShaderGraphSubGraph>();
                if (fragment.subGraphs == null) throw new FormatException("Sub Graph clipboard table is missing.");
                foreach (var definition in fragment.subGraphs)
                {
                    if (definition == null || string.IsNullOrEmpty(definition.id) || definitions.ContainsKey(definition.id)) throw new FormatException("Invalid Sub Graph clipboard definition.");
                    var existing = sameGraph ? target.FindSubGraph(definition.id) : null;
                    if (existing != null) { definitions.Add(definition.id, existing.id); continue; }
                    string old = definition.id; definition.id = Guid.NewGuid().ToString("N"); definitions.Add(old, definition.id);
                    target.subGraphs.Add(definition); imported.Add(definition);
                }
                var functions = new Dictionary<string, string>();
                foreach (var definition in imported)
                {
                    var ids = new Dictionary<string, string>();
                    foreach (var node in definition.nodes)
                    {
                        string old = node.id; node.id = Guid.NewGuid().ToString("N"); ids.Add(old, node.id);
                        if (node.kind == ShaderGraphNodeKind.Parameter)
                        {
                            if (!parameters.TryGetValue(node.parameterId ?? "", out string id)) throw new FormatException("Sub Graph clipboard parameter is missing.");
                            node.parameterId = id;
                        }
                        if (node.kind == ShaderGraphNodeKind.SubGraph)
                        {
                            if (!definitions.TryGetValue(node.subGraphId ?? "", out string id)) throw new FormatException("Nested Sub Graph is missing.");
                            node.subGraphId = id;
                        }
                        if (node.kind == ShaderGraphNodeKind.CustomFunction)
                        {
                            if (!functions.TryGetValue(node.functionId ?? "", out string id))
                            {
                                var source = fragment.FindFunction(node.functionId);
                                if (source == null) throw new FormatException("Sub Graph function is missing.");
                                var copy = CopyFunctionData(source); copy.id = Guid.NewGuid().ToString("N");
                                functions.Add(node.functionId, copy.id); target.functions.Add(copy); id = copy.id;
                            }
                            node.functionId = id;
                        }
                    }
                    ShaderGraphLoops.RemapPartners(definition.nodes, ids);
                    foreach (var edge in definition.edges)
                    { edge.inputNode = ids[edge.inputNode]; edge.outputNode = ids[edge.outputNode]; }
                    foreach (var note in definition.notes)
                    { string old = note.id; note.id = Guid.NewGuid().ToString("N"); ids.Add(old, note.id); }
                    foreach (var group in definition.groups)
                    {
                        group.id = Guid.NewGuid().ToString("N");
                        for (int i = 0; i < group.elements.Count; i++) group.elements[i] = ids[group.elements[i]];
                    }
                }
                return definitions;
            }
        }
    }
}
