using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        [SerializeField] string editingSubGraphId;
        ShaderGraph subGraphView, subGraphRoot;
        ToolbarButton backToGraph;
        readonly List<Action> interfaceRefresh = new List<Action>();
        internal ShaderGraph RootGraph => Behaviour?.graph;
        ShaderGraph ResolveGraph()
        {
            var root = RootGraph;
            var definition = root?.FindSubGraph(editingSubGraphId);
            if (definition == null) { editingSubGraphId = null; return root; }
            if (subGraphRoot != root || subGraphView?.activeSubGraph != definition)
            { subGraphRoot = root; subGraphView = root.SubGraphView(definition); }
            return subGraphView;
        }
        void CommitGraphView()
        {
            var view = Graph; var definition = view.activeSubGraph;
            if (definition == null) return;
            definition.nodes = view.nodes; definition.edges = view.edges; definition.notes = view.notes; definition.groups = view.groups;
            RootGraph.parameters = view.parameters; RootGraph.functions = view.functions; RootGraph.sources = view.sources; RootGraph.subGraphs = view.subGraphs;
        }
        void OpenSubGraph(string id)
        {
            editingSubGraphId = id; subGraphView = null;
            viewPosition = Vector3.zero; viewScale = Vector3.one;
            CreateGUI();
        }
        void AddSubGraph(ShaderGraphSubGraph definition, Vector2 position)
        {
            if (definition == null || WouldRecurse(definition.id)) return;
            if (Graph.nodes.Count >= 512) { ShowError("A Shader graph supports up to 512 nodes."); return; }
            Edit("Add Sub Graph", graph =>
            {
                var node = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraph, subGraphId = definition.id, subGraph = definition, position = position };
                graph.nodes.Add(node);
            });
        }
        bool WouldRecurse(string id)
        {
            if (Graph?.activeSubGraph == null) return false;
            var visited = new HashSet<string>();
            bool Visit(string current)
            {
                if (current == editingSubGraphId) return true;
                if (!visited.Add(current)) return false;
                var definition = RootGraph.FindSubGraph(current);
                if (definition != null) foreach (var node in definition.nodes)
                    if (node.kind == ShaderGraphNodeKind.SubGraph && Visit(node.subGraphId)) return true;
                return false;
            }
            return Visit(id);
        }
        void RebuildSubGraphInterface()
        {
            interfaceRefresh.Clear();
            var definition = Graph?.activeSubGraph;
            if (definition == null) return;
            var name = new TextField("Sub Graph") { value = definition.name, isDelayed = true };
            name.RegisterValueChangedCallback(e => Edit("Rename Sub Graph", _ => definition.name = string.IsNullOrWhiteSpace(e.newValue) ? "Sub Graph" : e.newValue, false));
            declarations.Add(name);
            void Ports(string title, bool output)
            {
                var list = output ? definition.outputs : definition.inputs;
                var header = new Toolbar(); header.Add(new Label(title));
                var add = new ToolbarButton(() => Edit("Add Sub Graph Port", graph =>
                {
                    var pin = new ShaderGraphInterfacePort { name = "Value " + (list.Count + 1) }; list.Add(pin);
                    if (!output) graph.nodes.Add(new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphInput, parameterId = pin.id, position = Vector2.zero });
                })) { text = "+" };
                add.SetEnabled(list.Count < 64 && (output || Graph.nodes.Count < 512)); header.Add(add);
                declarations.Add(header);
                foreach (var pin in list)
                {
                    var row = new VisualElement();
                    var label = new TextField { value = pin.name, isDelayed = true };
                    label.RegisterValueChangedCallback(e => Edit("Rename Sub Graph Port", _ => pin.name = string.IsNullOrWhiteSpace(e.newValue) ? "Value" : e.newValue, false));
                    var type = new EnumField(pin.type);
                    type.RegisterValueChangedCallback(e => Edit("Change Sub Graph Port Type", _ =>
                    {
                        pin.type = (ShaderGraphValueType)e.newValue; pin.uvDefault = false;
                        foreach (var node in RootGraph.AllNodes())
                            if (node.kind == ShaderGraphNodeKind.SubGraph && node.subGraphId == definition.id || node.kind == ShaderGraphNodeKind.SubGraphOutput && node.subGraph == definition)
                                node.inputs.RemoveAll(i => i.port == pin.id);
                    }));
                    row.Add(label); row.Add(type);
                    AddInterfaceDefault(row, pin);
                    if (!output && pin.type == ShaderGraphValueType.Float2)
                    {
                        var uv = new Toggle("UV Default") { value = pin.uvDefault };
                        uv.RegisterValueChangedCallback(e => Edit("Change Sub Graph UV", _ => pin.uvDefault = e.newValue)); row.Add(uv);
                    }
                    row.AddManipulator(new ContextualMenuManipulator(e =>
                    {
                        if (!output) e.menu.AppendAction("Use in Graph", _ => AddNode(ShaderGraphNodeKind.SubGraphInput, canvas.contentViewContainer.WorldToLocal(canvas.worldBound.center), pin.id));
                        e.menu.AppendAction("Remove", _ => Edit("Remove Sub Graph Port", graph =>
                        {
                            list.Remove(pin);
                            var removed = new HashSet<string>(definition.nodes.Where(n => n.kind == ShaderGraphNodeKind.SubGraphInput && n.parameterId == pin.id).Select(n => n.id));
                            definition.nodes.RemoveAll(n => removed.Contains(n.id));
                            definition.edges.RemoveAll(edge => removed.Contains(edge.outputNode) || edge.inputPort == pin.id);
                            void Clear(ShaderGraph scope)
                            {
                                var instances = new HashSet<string>(scope.nodes.Where(n => n.kind == ShaderGraphNodeKind.SubGraph && n.subGraphId == definition.id).Select(n => n.id));
                                scope.edges.RemoveAll(edge => output ? instances.Contains(edge.outputNode) && edge.outputPort == pin.id : instances.Contains(edge.inputNode) && edge.inputPort == pin.id);
                                foreach (var node in scope.nodes) if (instances.Contains(node.id)) node.inputs.RemoveAll(i => i.port == pin.id);
                            }
                            Clear(RootGraph); foreach (var other in RootGraph.subGraphs) Clear(RootGraph.SubGraphView(other));
                        }), _ => output && list.Count <= 1 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                    }));
                    declarations.Add(row);
                }
            }
            Ports("Inputs", false); Ports("Outputs", true);
        }

        void AddInterfaceDefault(VisualElement row, ShaderGraphInterfacePort pin)
        {
            if (pin.uvDefault || ShaderGraphNodes.Components(pin.type) == 0) return;
            void Set(Vector4 value)
            {
                if (!ProjectiveMatrix.Finite(value.x) || !ProjectiveMatrix.Finite(value.y) || !ProjectiveMatrix.Finite(value.z) || !ProjectiveMatrix.Finite(value.w)) return;
                Edit("Change Sub Graph Default", _ => pin.value = value, structural: false, rebuild: false);
            }
            switch (pin.type)
            {
                case ShaderGraphValueType.Float:
                    var number = new FloatField("Default") { value = pin.value.x };
                    number.RegisterValueChangedCallback(e => Set(new Vector4(e.newValue, 0, 0, 0))); interfaceRefresh.Add(() => number.SetValueWithoutNotify(pin.value.x)); row.Add(number); break;
                case ShaderGraphValueType.Float2:
                    var pair = new Vector2Field("Default") { value = pin.value };
                    pair.RegisterValueChangedCallback(e => Set(new Vector4(e.newValue.x, e.newValue.y, 0, 0))); interfaceRefresh.Add(() => pair.SetValueWithoutNotify(pin.value)); row.Add(pair); break;
                case ShaderGraphValueType.Float3:
                    var triple = new Vector3Field("Default") { value = pin.value };
                    triple.RegisterValueChangedCallback(e => Set(e.newValue)); interfaceRefresh.Add(() => triple.SetValueWithoutNotify(pin.value)); row.Add(triple); break;
                case ShaderGraphValueType.Float4:
                    var vector = new Vector4Field("Default") { value = pin.value };
                    vector.RegisterValueChangedCallback(e => Set(e.newValue)); interfaceRefresh.Add(() => vector.SetValueWithoutNotify(pin.value)); row.Add(vector); break;
                case ShaderGraphValueType.Boolean:
                    var toggle = new Toggle("Default") { value = pin.value.x != 0 };
                    toggle.RegisterValueChangedCallback(e => Set(new Vector4(e.newValue ? 1 : 0, 0, 0, 0))); interfaceRefresh.Add(() => toggle.SetValueWithoutNotify(pin.value.x != 0)); row.Add(toggle); break;
                case ShaderGraphValueType.Color:
                    var color = new WhimTexColorField("Default") { value = HdrUtility.Encode(pin.value), hdr = true, showAlpha = true, Document = () => document };
                    color.RegisterValueChangedCallback(e => Set(HdrUtility.Decode(e.newValue))); interfaceRefresh.Add(() => color.SetValueWithoutNotify(HdrUtility.Encode(pin.value))); row.Add(color); break;
            }
        }

        void ExtractSubGraph(HashSet<string> selection)
        {
            var graph = Graph;
            if (!Editable || graph == null) return;
            if (graph.subGraphs.Count >= 128) { ShowError("A Shader graph supports up to 128 Sub Graph definitions."); return; }
            selection.RemoveWhere(id => graph.FindNode(id) == null || graph.FindNode(id).kind == ShaderGraphNodeKind.Output || ShaderGraphNodes.IsSubGraphInterface(graph.FindNode(id).kind));
            if (selection.Count == 0) return;
            var pending = new Stack<string>(); var visited = new HashSet<string>();
            foreach (var edge in graph.edges) if (selection.Contains(edge.outputNode) && !selection.Contains(edge.inputNode)) pending.Push(edge.inputNode);
            while (pending.Count != 0)
            {
                string id = pending.Pop();
                if (selection.Contains(id)) { ShowError("Include the intermediate nodes in the selection to avoid a cyclic Sub Graph connection."); return; }
                if (!visited.Add(id)) continue;
                foreach (var edge in graph.edges) if (edge.outputNode == id) pending.Push(edge.inputNode);
            }
            Edit("Convert Selection to Sub Graph", current =>
            {
                var definition = new ShaderGraphSubGraph();
                int suffix = 1; while (current.subGraphs.Exists(s => s.name == definition.name)) definition.name = "Sub Graph " + ++suffix;
                definition.nodes.AddRange(current.nodes.Where(n => selection.Contains(n.id)));
                float left = definition.nodes.Min(n => n.position.x), right = definition.nodes.Max(n => n.position.x), top = definition.nodes.Min(n => n.position.y);
                var instance = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraph, subGraphId = definition.id, subGraph = definition, position = new Vector2(left, top) };
                var terminal = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphOutput, position = new Vector2(right + 320, top) };
                var inputs = new Dictionary<string, ShaderGraphInterfacePort>(); var outputs = new Dictionary<string, ShaderGraphInterfacePort>();
                var replacement = new List<ShaderGraphEdge>();
                ShaderGraphInterfacePort Output(string nodeId, string port)
                {
                    string key = nodeId + ":" + port;
                    if (outputs.TryGetValue(key, out var found)) return found;
                    var from = current.FindNode(nodeId);
                    var pin = new ShaderGraphInterfacePort { name = ShaderGraphNodes.Name(from.kind) + " " + ShaderGraphNodes.PortDisplayName(from, port, true), type = OutputType(from, port) };
                    definition.outputs.Add(pin); outputs.Add(key, pin);
                    definition.edges.Add(new ShaderGraphEdge { outputNode = nodeId, outputPort = port, inputNode = terminal.id, inputPort = pin.id });
                    return pin;
                }
                foreach (var edge in current.edges)
                {
                    bool from = selection.Contains(edge.outputNode), to = selection.Contains(edge.inputNode);
                    if (from && to) { definition.edges.Add(edge); continue; }
                    if (!from && !to) { replacement.Add(edge); continue; }
                    if (from)
                    {
                        var pin = Output(edge.outputNode, edge.outputPort);
                        replacement.Add(new ShaderGraphEdge { outputNode = instance.id, outputPort = pin.id, inputNode = edge.inputNode, inputPort = edge.inputPort });
                    }
                    else
                    {
                        string key = edge.outputNode + ":" + edge.outputPort;
                        if (!inputs.TryGetValue(key, out var pin))
                        {
                            var source = current.FindNode(edge.outputNode);
                            pin = new ShaderGraphInterfacePort { name = source.kind == ShaderGraphNodeKind.Parameter ? current.FindParameter(source.parameterId)?.name ?? "Value" : ShaderGraphNodes.Name(source.kind) + " " + edge.outputPort, type = OutputType(source, edge.outputPort) };
                            inputs.Add(key, pin); definition.inputs.Add(pin);
                            var input = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphInput, parameterId = pin.id, position = new Vector2(left - 260, top + inputs.Count * 100) };
                            definition.nodes.Add(input);
                            replacement.Add(new ShaderGraphEdge { outputNode = edge.outputNode, outputPort = edge.outputPort, inputNode = instance.id, inputPort = pin.id });
                        }
                        var inputNode = definition.nodes.Find(n => n.kind == ShaderGraphNodeKind.SubGraphInput && n.parameterId == pin.id);
                        definition.edges.Add(new ShaderGraphEdge { outputNode = inputNode.id, inputNode = edge.inputNode, inputPort = edge.inputPort });
                    }
                }
                if (definition.outputs.Count == 0)
                {
                    var last = definition.nodes.Last(n => !ShaderGraphNodes.IsSubGraphInterface(n.kind));
                    foreach (var port in ShaderGraphNodes.Outputs(last)) Output(last.id, port);
                }
                definition.nodes.Add(terminal);
                if (definition.inputs.Count > 64 || definition.outputs.Count > 64 || definition.nodes.Count > 512)
                { ShowError("The selection exceeds the Sub Graph limit of 64 inputs, 64 outputs or 512 nodes. Select a smaller calculation."); return; }
                current.nodes.RemoveAll(n => selection.Contains(n.id)); current.nodes.Add(instance);
                current.edges = replacement; current.subGraphs.Add(definition);
                current.BindFunctions();
            });
        }

        private sealed partial class Canvas
        {
            void SubGraphMenu(ContextualMenuPopulateEvent e)
            {
                var ids = new HashSet<string>(selection.OfType<INodeView>().Select(n => n.Id));
                if (ids.Count != 0) e.menu.AppendAction("Convert Selection to Sub Graph", _ => window.ExtractSubGraph(ids),
                    _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }
    }
}
