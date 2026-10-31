using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private interface INodeView
        {
            string Id { get; }
            void RefreshValues();
        }

        private sealed class ParameterView : TokenNode, INodeView
        {
            public string Id { get; }
            private readonly ShaderGraphWindow window;
            internal ParameterView(ShaderGraphWindow window, Canvas canvas, ShaderGraphNode node)
                : base(null, NodeView.CreatePort(node.id, "Out", Direction.Output, window.OutputType(node, "Out"), false))
            {
                this.window = window; Id = node.id; viewDataKey = Id;
                AddToClassList("whimtex-shader-graph-parameter-node");
                capabilities &= ~(Capabilities.Copiable | Capabilities.Groupable);
                this.Q("title-label")?.RemoveFromHierarchy();
                this.Q("icon")?.RemoveFromHierarchy();
                canvas.RegisterPort(output, false);
                this.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    foreach (var parameter in window.Graph.parameters)
                    {
                        string id = parameter.id;
                        e.menu.AppendAction("Parameter/" + parameter.name,
                            _ => window.EditNode(Id, "Change Shader Parameter", n => n.parameterId = id),
                            _ => !window.Editable ? DropdownMenuAction.Status.Disabled : window.Graph.FindNode(Id)?.parameterId == id ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                    }
                }));
                SetPosition(new Rect(node.position, Vector2.zero));
                RefreshValues();
            }
            public void RefreshValues()
            {
                var node = window.Graph?.FindNode(Id);
                if (node == null) return;
                output.portName = window.Graph.FindParameter(node.parameterId)?.name ?? "Missing Parameter";
                if ((GetPosition().position - node.position).sqrMagnitude > .01f) SetPosition(new Rect(node.position, GetPosition().size));
            }
        }

        private sealed class RerouteView : UnityEditor.Experimental.GraphView.Node, INodeView
        {
            internal static readonly Vector2 Size = new Vector2(48, 24);
            public string Id { get; }
            private readonly ShaderGraphWindow window;
            internal RerouteView(ShaderGraphWindow window, Canvas canvas, ShaderGraphNode node)
            {
                this.window = window; Id = node.id; viewDataKey = Id; title = string.Empty;
                AddToClassList("whimtex-shader-graph-reroute");
                capabilities &= ~(Capabilities.Copiable | Capabilities.Groupable | Capabilities.Collapsible);
                titleContainer.RemoveFromHierarchy(); extensionContainer.RemoveFromHierarchy();
                foreach (var divider in mainContainer.Query<VisualElement>("divider").ToList()) divider.RemoveFromHierarchy();
                var type = window.OutputType(node, "Out");
                var input = NodeView.CreatePort(Id, "In", Direction.Input, type, false);
                var output = NodeView.CreatePort(Id, "Out", Direction.Output, type, false);
                input.Q("type")?.RemoveFromHierarchy(); output.Q("type")?.RemoveFromHierarchy();
                inputContainer.Add(input); outputContainer.Add(output);
                canvas.RegisterPort(input, true); canvas.RegisterPort(output, false);
                SetPosition(new Rect(node.position, Size));
                RefreshExpandedState(); RefreshPorts();
            }
            public void RefreshValues()
            {
                var node = window.Graph?.FindNode(Id);
                if (node != null && (GetPosition().position - node.position).sqrMagnitude > .01f) SetPosition(new Rect(node.position, Size));
            }
        }

        private sealed class NodeView : UnityEditor.Experimental.GraphView.Node, INodeView
        {
            public string Id { get; }
            private readonly ShaderGraphWindow window;
            private readonly List<Action> refresh = new List<Action>();
            private ShaderGraphNode Data => window.Graph?.FindNode(Id);
            internal NodeView(ShaderGraphWindow window, Canvas canvas, ShaderGraphNode node)
            {
                this.window = window; Id = node.id;
                title = node.kind == ShaderGraphNodeKind.Parameter ? window.Graph.FindParameter(node.parameterId)?.name ?? "Missing Parameter" : ShaderGraphNodes.Name(node.kind);
                viewDataKey = Id;
                AddToClassList("whimtex-shader-graph-node");
                // Clear the base Node's inline clipping so the external input fields can use USS overflow.
                mainContainer.style.overflow = StyleKeyword.Null;
                capabilities &= ~(Capabilities.Copiable | Capabilities.Groupable);
                if (node.kind == ShaderGraphNodeKind.Output)
                {
                    capabilities &= ~(Capabilities.Deletable | Capabilities.Collapsible);
                    AddToClassList("whimtex-shader-graph-output");
                }
                SetPosition(new Rect(node.position, Vector2.zero));
                BuildOptions(node);
                foreach (var input in ShaderGraphNodes.Inputs(node))
                {
                    if (node.kind == ShaderGraphNodeKind.Combine && "XYZW".IndexOf(input.name, StringComparison.Ordinal) >= ShaderGraphNodes.Components(node.valueType)) continue;
                    var type = input.type ?? window.InputType(node, input.name);
                    var port = MakePort(input.name, Direction.Input, type, !input.type.HasValue);
                    var row = new VisualElement(); row.AddToClassList("whimtex-shader-graph-port-row");
                    row.Add(port); inputContainer.Add(row);
                    canvas.RegisterPort(port, true);
                    if (!window.Graph.edges.Exists(e => e.inputNode == Id && e.inputPort == input.name)) BuildInput(node, input, row, port);
                }
                foreach (string output in ShaderGraphNodes.Outputs(node))
                {
                    var type = window.OutputType(node, output);
                    var port = MakePort(output, Direction.Output, type, false);
                    if (node.kind == ShaderGraphNodeKind.Split && "XYZW".IndexOf(output, StringComparison.Ordinal) >= ShaderGraphNodes.Components(window.InputType(node, "In")))
                    {
                        port.SetEnabled(false); port.tooltip = "This component is not present in the input.";
                    }
                    canvas.RegisterPort(port, false); outputContainer.Add(port);
                }
                if (node.kind != ShaderGraphNodeKind.Output) extensionContainer.Add(canvas.GetPreview(node));
                RefreshExpandedState(); RefreshPorts();
                RefreshValues();
            }
            private Port MakePort(string name, Direction direction, ShaderGraphValueType type, bool dynamic)
                => CreatePort(Id, name, direction, type, dynamic);
            internal static Port CreatePort(string id, string name, Direction direction, ShaderGraphValueType type, bool dynamic)
            {
                var port = new GraphPort(direction, PortType(type));
                port.portName = name;
                port.userData = new PortData { nodeId = id, name = name, type = type, dynamic = dynamic };
                port.AddToClassList("whimtex-shader-graph-port");
                if (!ShaderGraphNodes.IsNumeric(type))
                    port.AddToClassList(type == ShaderGraphValueType.Boolean ? "whimtex-shader-graph-port--boolean" : "whimtex-shader-graph-port--resource");
                port.portColor = PortColor(type);
                port.tooltip = dynamic ? "Numeric or Color. Scalars expand to the vector width; missing vector components are zero." : TypeName(type);
                return port;
            }
            private static Type PortType(ShaderGraphValueType type) => type switch
            {
                ShaderGraphValueType.Float2 => typeof(Vector2), ShaderGraphValueType.Float3 => typeof(Vector3),
                ShaderGraphValueType.Float4 => typeof(Vector4), ShaderGraphValueType.Color => typeof(Color), ShaderGraphValueType.Boolean => typeof(bool),
                ShaderGraphValueType.Texture => typeof(Texture2D), ShaderGraphValueType.Gradient => typeof(WhimTexGradient),
                ShaderGraphValueType.Curve => typeof(AnimationCurve), _ => typeof(float)
            };
            private static Color PortColor(ShaderGraphValueType type) => type switch
            {
                ShaderGraphValueType.Float => new Color32(132, 228, 231, 255), ShaderGraphValueType.Float2 => new Color32(154, 239, 146, 255),
                ShaderGraphValueType.Float3 => new Color32(246, 255, 154, 255), ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => new Color32(251, 203, 244, 255),
                ShaderGraphValueType.Boolean => new Color32(148, 129, 230, 255), ShaderGraphValueType.Texture => new Color32(255, 139, 139, 255),
                ShaderGraphValueType.Gradient => new Color(.5f, .8f, .9f), _ => new Color(.85f, .65f, .55f)
            };
            private static string TypeName(ShaderGraphValueType type) => type switch
            {
                ShaderGraphValueType.Boolean => "bool", ShaderGraphValueType.Float => "float",
                ShaderGraphValueType.Float2 => "float2", ShaderGraphValueType.Float3 => "float3", ShaderGraphValueType.Float4 => "float4",
                _ => type.ToString()
            };

            private void BuildOptions(ShaderGraphNode node)
            {
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.Constant:
                        TypeChoice("Type", node.valueType, true, false, type => Change(n =>
                        {
                            if (type == ShaderGraphValueType.Color && ShaderGraphNodes.Components(n.valueType) < 4) n.value.w = 1;
                            n.valueType = type;
                        }));
                        ValueField("Value", node.valueType, () => Data.value,
                            value => Change(n => n.value = value, structural: false, rebuild: false)); break;
                    case ShaderGraphNodeKind.Combine:
                        TypeChoice("Type", node.valueType, false, true, type => window.Edit("Change Combine Width", graph =>
                        {
                            graph.FindNode(Id).valueType = type;
                            graph.edges.RemoveAll(e => e.inputNode == Id && "XYZW".IndexOf(e.inputPort, StringComparison.Ordinal) >= ShaderGraphNodes.Components(type));
                        })); break;
                    case ShaderGraphNodeKind.Noise:
                        Choice("Type", new[] { "OpenSimplex2", "OpenSimplex2S", "Cellular", "Perlin", "ValueCubic", "Value" }, node.option, value => Change(n => n.option = value)); break;
                    case ShaderGraphNodeKind.Shape:
                        Choice("Shape", new[] { "Ellipse", "Rectangle", "Rounded Rectangle" }, node.option, value => Change(n => n.option = value)); break;
                    case ShaderGraphNodeKind.Compare:
                        Choice("Compare", new[] { "Less", "Less or Equal", "Greater", "Greater or Equal", "Equal", "Not Equal" }, node.option, value => Change(n => n.option = value)); break;
                    case ShaderGraphNodeKind.Blend:
                        var blend = new EnumField("Mode", (BlendMode)node.option);
                        blend.RegisterValueChangedCallback(e => Change(n => n.option = (int)(BlendMode)e.newValue)); AddOption(blend);
                        var hdr = new Toggle("HDR") { value = node.hdr };
                        hdr.RegisterValueChangedCallback(e => Change(n => n.hdr = e.newValue)); AddOption(hdr); break;
                    case ShaderGraphNodeKind.SampleTexture:
                        var wrap = new EnumField("Wrap", node.wrap);
                        wrap.RegisterValueChangedCallback(e => Change(n => n.wrap = (ShaderGraphWrap)e.newValue)); AddOption(wrap);
                        var filter = new EnumField("Filter", node.filter);
                        filter.RegisterValueChangedCallback(e => Change(n => n.filter = (ShaderGraphFilter)e.newValue)); AddOption(filter); break;
                }
            }
            private void BuildInput(ShaderGraphNode node, ShaderGraphPort input, VisualElement row, Port port)
            {
                if (input.type is ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve) return;
                string name = input.name;
                var external = new VisualElement { pickingMode = PickingMode.Ignore, userData = name };
                external.AddToClassList("whimtex-shader-graph-port-value");
                var stub = new VisualElement { pickingMode = PickingMode.Ignore };
                stub.AddToClassList("whimtex-shader-graph-value-stub");
                var tint = port.portColor; tint.a = .45f; stub.style.backgroundColor = tint; external.Add(stub);
                var fields = new VisualElement(); fields.AddToClassList("whimtex-shader-graph-value-fields"); external.Add(fields);
                row.Insert(0, external);
                if (input.uvDefault)
                {
                    bool useUV = node.FindInput(name) == null;
                    var uv = new DropdownField(new List<string> { "UV", "Custom" }, useUV ? 0 : 1);
                    uv.tooltip = "UV uses this pixel's coordinates. Custom sets a constant value while the input remains unconnected.";
                    uv.AddToClassList("whimtex-shader-graph-uv-mode");
                    uv.RegisterValueChangedCallback(e => Change(n =>
                    {
                        n.inputs.RemoveAll(i => i.port == name);
                        if (e.newValue == "Custom") n.inputs.Add(new ShaderGraphInput { port = name, type = ShaderGraphValueType.Float2 });
                    }));
                    AddOption(uv, fields);
                    if (useUV) return;
                }
                var value = node.FindInput(name);
                ShaderGraphValueType type = value?.type ?? input.type ?? ShaderGraphValueType.Float;
                if (!input.type.HasValue)
                {
                    fields.tooltip = "Unconnected value. Right-click to choose its numeric type.";
                    fields.AddManipulator(new ContextualMenuManipulator(e =>
                    {
                        foreach (var next in new[] { ShaderGraphValueType.Float, ShaderGraphValueType.Float2, ShaderGraphValueType.Float3, ShaderGraphValueType.Float4, ShaderGraphValueType.Color })
                        {
                            var chosen = next;
                            e.menu.AppendAction(TypeName(chosen), _ => Change(n =>
                            {
                                var item = n.FindInput(name);
                                if (item == null) n.inputs.Add(item = new ShaderGraphInput { port = name, value = input.value });
                                if (chosen == ShaderGraphValueType.Color && ShaderGraphNodes.Components(item.type) < 4) item.value.w = 1;
                                item.type = chosen;
                            }), _ => chosen == type ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                        }
                        e.StopPropagation();
                    }));
                }
                ValueField(null, type, () => Data.FindInput(name)?.value ?? input.value,
                    next => Change(n =>
                    {
                        var item = n.FindInput(name);
                        if (item == null) n.inputs.Add(item = new ShaderGraphInput { port = name, type = type });
                        item.value = next;
                    }, structural: Data.FindInput(name) == null, rebuild: false), fields);
            }
            private void Change(Action<ShaderGraphNode> edit, bool structural = true, bool rebuild = true) =>
                window.EditNode(Id, "Change Shader " + title, edit, structural, rebuild);
            private void AddOption(VisualElement element, VisualElement target = null)
            {
                element.AddToClassList("whimtex-shader-graph-node-field");
                element.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode is KeyCode.Backspace or KeyCode.Delete or KeyCode.Space or KeyCode.LeftArrow or KeyCode.RightArrow or KeyCode.UpArrow or KeyCode.DownArrow)
                        e.StopPropagation();
                });
                (target ?? extensionContainer).Add(element);
            }
            private void Choice(string label, string[] options, int selected, Action<int> changed)
            {
                var field = new DropdownField(label, new List<string>(options), Mathf.Clamp(selected, 0, options.Length - 1));
                field.RegisterValueChangedCallback(e => { int i = Array.IndexOf(options, e.newValue); if (i >= 0) changed(i); });
                AddOption(field);
            }
            private void TypeChoice(string label, ShaderGraphValueType selected, bool boolean, bool combine, Action<ShaderGraphValueType> changed)
            {
                var options = new List<ShaderGraphValueType>(); var labels = new List<string>();
                for (var type = combine ? ShaderGraphValueType.Float2 : ShaderGraphValueType.Float; type <= (boolean ? ShaderGraphValueType.Boolean : ShaderGraphValueType.Float4); type++)
                { options.Add(type); labels.Add(TypeName(type)); }
                if (!combine) { options.Add(ShaderGraphValueType.Color); labels.Add(TypeName(ShaderGraphValueType.Color)); }
                var field = new DropdownField(label, labels, Math.Max(0, options.IndexOf(selected)));
                field.RegisterValueChangedCallback(e => { int i = labels.IndexOf(e.newValue); if (i >= 0) changed(options[i]); });
                AddOption(field);
            }
            private void ValueField(string label, ShaderGraphValueType type, Func<Vector4> read, Action<Vector4> write, VisualElement target = null)
            {
                void Apply(Vector4 value)
                {
                    if (ProjectiveMatrix.Finite(value.x) && ProjectiveMatrix.Finite(value.y) && ProjectiveMatrix.Finite(value.z) && ProjectiveMatrix.Finite(value.w)) write(value);
                }
                switch (type)
                {
                    case ShaderGraphValueType.Float:
                        var number = new FloatField(label) { value = read().x };
                        number.RegisterValueChangedCallback(e => { var value = read(); value.x = e.newValue; Apply(value); });
                        refresh.Add(() => number.SetValueWithoutNotify(read().x)); AddOption(number, target); break;
                    case ShaderGraphValueType.Float2:
                        var v2 = new Vector2Field(label) { value = read() };
                        v2.RegisterValueChangedCallback(e => { var value = read(); value.x = e.newValue.x; value.y = e.newValue.y; Apply(value); });
                        refresh.Add(() => v2.SetValueWithoutNotify(read())); AddOption(v2, target); break;
                    case ShaderGraphValueType.Float3:
                        var v3 = new Vector3Field(label) { value = read() };
                        v3.RegisterValueChangedCallback(e => { var value = read(); value.x = e.newValue.x; value.y = e.newValue.y; value.z = e.newValue.z; Apply(value); });
                        refresh.Add(() => v3.SetValueWithoutNotify(read())); AddOption(v3, target); break;
                    case ShaderGraphValueType.Float4:
                        var v4 = new Vector4Field(label) { value = read() };
                        v4.RegisterValueChangedCallback(e => Apply(e.newValue));
                        refresh.Add(() => v4.SetValueWithoutNotify(read())); AddOption(v4, target); break;
                    case ShaderGraphValueType.Color:
                        var color = new WhimTexColorField(label) { value = HdrUtility.Encode(read()), hdr = true, showAlpha = true, Document = () => window.document };
                        color.RegisterValueChangedCallback(e => Apply(HdrUtility.Decode(e.newValue)));
                        refresh.Add(() => color.SetValueWithoutNotify(HdrUtility.Encode(read()))); AddOption(color, target); break;
                    case ShaderGraphValueType.Boolean:
                        var toggle = new Toggle(label) { value = read().x != 0 };
                        toggle.RegisterValueChangedCallback(e => Apply(new Vector4(e.newValue ? 1 : 0, 0, 0, 0)));
                        refresh.Add(() => toggle.SetValueWithoutNotify(read().x != 0)); AddOption(toggle, target); break;
                }
            }
            public void RefreshValues()
            {
                if (Data == null) return;
                foreach (var update in refresh) update();
                if ((GetPosition().position - Data.position).sqrMagnitude > .01f) SetPosition(new Rect(Data.position, GetPosition().size));
            }
            public override void CollectElements(HashSet<GraphElement> collectedElementSet, Func<GraphElement, bool> conditionFunc)
            {
                base.CollectElements(collectedElementSet, conditionFunc);
                foreach (var port in inputContainer.Query<Port>().ToList())
                    foreach (var edge in port.connections)
                        if ((edge.capabilities & Capabilities.Deletable) != 0) collectedElementSet.Add(edge);
            }
        }
    }
}
