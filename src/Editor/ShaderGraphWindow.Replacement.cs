using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using GraphNodeView = UnityEditor.Experimental.GraphView.Node;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        static bool CanReplace(ShaderGraphNode node) => node != null &&
            node.kind != ShaderGraphNodeKind.Output && !ShaderGraphNodes.IsSubGraphInterface(node.kind) && !ShaderGraphLoops.IsEndpoint(node.kind);

        private sealed partial class Canvas
        {
            void ReplacementMenu(ContextualMenuPopulateEvent evt)
            {
                var target = evt.target as VisualElement;
                var view = (target as GraphNodeView ?? target?.GetFirstAncestorOfType<GraphNodeView>()) as INodeView;
                var node = window.Graph?.FindNode(view?.Id);
                if (!CanReplace(node)) return;
                Vector2 screen = window.position.position + window.rootVisualElement.WorldToLocal(evt.mousePosition);
                evt.menu.InsertAction(0, "Replace Node", _ => ShowReplacementSearch(node, screen),
                    _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }

            void ShowReplacementSearch(ShaderGraphNode node, Vector2 screen)
            {
                if (!window.Editable || !CanReplace(node)) return;
                if (search == null) search = ScriptableObject.CreateInstance<NodeSearch>();
                search.canvas = this; search.source = null;
                search.sourceGraph = window.Graph; search.replacement = node;
                search.Open(screen);
            }
        }

        bool ReplaceNode(ShaderGraph expectedGraph, ShaderGraphNode previous, object choice)
        {
            if (!Editable || !ReferenceEquals(expectedGraph, Graph) || !CanReplace(previous) ||
                !ReferenceEquals(Graph.FindNode(previous.id), previous)) return false;
            var graph = Graph;
            var staged = new ShaderGraph { nodes = new List<ShaderGraphNode>(graph.nodes), edges = new List<ShaderGraphEdge>(),
                parameters = graph.parameters, functions = new List<ShaderGraphFunction>(graph.functions), sources = new List<ShaderGraphSource>(graph.sources) };
            ShaderGraphNode next;
            try
            {
                switch (choice)
                {
                    case ShaderGraphNodeKind kind when CanReplace(new ShaderGraphNode { kind = kind }) &&
                        kind is not (ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.SubGraph):
                        next = ShaderGraphNodes.Create(kind, previous.position); break;
                    case string parameter when graph.FindParameter(parameter) != null:
                        next = ShaderGraphNodes.Create(ShaderGraphNodeKind.Parameter, previous.position); next.parameterId = parameter; break;
                    case ShaderGraphSubGraph definition when graph.FindSubGraph(definition.id) == definition && !WouldRecurse(definition.id):
                        next = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraph, subGraphId = definition.id, subGraph = definition }; break;
                    case ShaderGraphFunctionCatalog.Entry entry:
                        var function = entry.Import(staged);
                        CheckFunction(function, staged.sources, Target.IsEffect);
                        next = ShaderGraphNodes.Create(ShaderGraphNodeKind.CustomFunction, previous.position);
                        ShaderGraphNodes.SetFunction(next, function); break;
                    default: return false;
                }
            }
            catch (Exception error) { ShowError(error.Message); return false; }
            next.id = previous.id; next.position = previous.position; next.previewExpanded = previous.previewExpanded;
            next.previewHeight = previous.previewHeight; next.previewOutput = previous.previewOutput;
            if (next.function != null && !staged.functions.Contains(next.function)) staged.functions.Add(next.function);
            staged.nodes[staged.nodes.IndexOf(previous)] = next;

            string Name(ShaderGraphNode node, string port, bool output) => node.kind == ShaderGraphNodeKind.SubGraph
                ? (output ? node.subGraph?.outputs : node.subGraph?.inputs)?.Find(p => p.id == port)?.name ?? port : port;
            var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var oldInputs = ShaderGraphNodes.StoredInputs(previous);
            var newInputs = ShaderGraphNodes.StoredInputs(next);
            foreach (var oldPort in oldInputs)
            {
                string name = Name(previous, oldPort.name, false);
                var matches = newInputs.Where(p => Name(next, p.name, false) == name).ToArray();
                if (matches.Length != 1 || oldInputs.Count(p => Name(previous, p.name, false) == name) != 1) continue;
                var port = matches[0];
                var oldType = oldPort.type ?? InputType(graph, previous, oldPort.name);
                if (port.type.HasValue ? port.type.Value != oldType : !ShaderGraphNodes.IsNumeric(oldType)) continue;
                inputs.Add(oldPort.name, port.name);
                var value = previous.FindInput(oldPort.name);
                if (value == null || value.source != ShaderGraphInputSource.Value && (!port.uvDefault || port.type != value.type)) continue;
                next.inputs.RemoveAll(p => p.port == port.name);
                next.inputs.Add(new ShaderGraphInput { port = port.name, type = value.type, value = value.value,
                    source = value.source, resource = value.resource?.Copy() });
            }
            foreach (var edge in graph.edges)
            {
                string input = edge.inputPort;
                if (edge.inputNode == previous.id && !inputs.TryGetValue(input, out input)) continue;
                staged.edges.Add(new ShaderGraphEdge { outputNode = edge.outputNode, outputPort = edge.outputPort, inputNode = edge.inputNode, inputPort = input });
            }
            // Dynamic ports resolve against the retained incoming edges, not just the
            // node's default scalar width. A numeric conversion is not an exact match.
            foreach (var oldPort in oldInputs)
                if (inputs.TryGetValue(oldPort.name, out string port) &&
                    (oldPort.type ?? InputType(graph, previous, oldPort.name)) !=
                    (newInputs.First(p => p.name == port).type ?? InputType(staged, next, port)))
                    staged.edges.RemoveAll(e => e.inputNode == next.id && e.inputPort == port);
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var oldOutputs = ShaderGraphNodes.StoredOutputs(previous);
            var newOutputs = ShaderGraphNodes.StoredOutputs(next);
            foreach (string oldPort in oldOutputs)
            {
                string name = Name(previous, oldPort, true);
                var matches = newOutputs.Where(p => Name(next, p, true) == name).ToArray();
                if (matches.Length == 1 && oldOutputs.Count(p => Name(previous, p, true) == name) == 1 &&
                    OutputType(graph, previous, oldPort) == OutputType(staged, next, matches[0])) outputs.Add(oldPort, matches[0]);
            }
            staged.edges.RemoveAll(e => e.outputNode == previous.id && !outputs.ContainsKey(e.outputPort));
            foreach (var edge in staged.edges)
                if (edge.outputNode == previous.id) edge.outputPort = outputs[edge.outputPort];
            Edit("Replace Shader Node", current =>
            {
                current.nodes[current.nodes.IndexOf(previous)] = next;
                current.edges = staged.edges; current.functions = staged.functions; current.sources = staged.sources;
            });
            return true;
        }
    }
}
