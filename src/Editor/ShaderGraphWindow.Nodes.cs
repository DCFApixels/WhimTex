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
        private interface IGraphItemView { string Id { get; } }
        private interface INodeView : IGraphItemView
        {
            void RefreshValues();
        }

        private sealed class ParameterView : TokenNode, INodeView
        {
            public string Id { get; }
            internal string ParameterId { get; private set; }
            private readonly ShaderGraphWindow window;
            internal ParameterView(ShaderGraphWindow window, Canvas canvas, ShaderGraphNode node)
                : base(null, NodeView.CreatePort(node.id, "Out", Direction.Output, window.OutputType(node, "Out"), false))
            {
                this.window = window; Id = node.id; viewDataKey = Id;
                AddToClassList("whimtex-shader-graph-node");
                AddToClassList("whimtex-shader-graph-parameter-node");
                mainContainer.style.overflow = StyleKeyword.Null;
                this.Q("selection-border")?.SendToBack();
                capabilities |= Capabilities.Copiable | Capabilities.Groupable;
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
                ParameterId = node.parameterId;
                output.portName = NodeView.PortLabel(window.Graph.FindParameter(node.parameterId)?.name ?? "Missing Parameter", ((PortData)output.userData).type);
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
                capabilities = (capabilities | Capabilities.Copiable | Capabilities.Groupable) & ~Capabilities.Collapsible;
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

        private sealed partial class NodeView : UnityEditor.Experimental.GraphView.Node, INodeView
        {
            public string Id { get; }
            internal readonly ShaderGraphWindow window;
            private readonly List<Action> refresh = new List<Action>();
            internal ShaderGraphNode Data => window.Graph?.FindNode(Id);
            internal NodeView(ShaderGraphWindow window, Canvas canvas, ShaderGraphNode node)
            {
                this.window = window; Id = node.id;
                title = node.kind == ShaderGraphNodeKind.Parameter ? window.Graph.FindParameter(node.parameterId)?.name ?? "Missing Parameter" : ShaderGraphNodes.Name(node.kind);
                if (node.kind == ShaderGraphNodeKind.SubGraph) title = node.subGraph?.name ?? "Missing Sub Graph";
                if (node.kind == ShaderGraphNodeKind.SubGraphInput) title = node.interfacePort?.name ?? "Missing Input";
                if (node.kind == ShaderGraphNodeKind.SubGraph)
                {
                    this.AddManipulator(new ContextualMenuManipulator(e => e.menu.AppendAction("Edit Sub Graph", _ => window.OpenSubGraph(Data.subGraphId))));
                    titleContainer.RegisterCallback<MouseDownEvent>(e => { if (e.button == 0 && e.clickCount == 2) { window.OpenSubGraph(Data.subGraphId); e.StopPropagation(); } });
                }
                viewDataKey = Id;
                AddToClassList("whimtex-shader-graph-node");
                // Clear the base Node's inline clipping so the external input fields can use USS overflow.
                mainContainer.style.overflow = StyleKeyword.Null;
                capabilities |= Capabilities.Copiable | Capabilities.Groupable;
                this.Q("selection-border")?.SendToBack();
                if (node.kind is ShaderGraphNodeKind.Output or ShaderGraphNodeKind.SubGraphOutput)
                {
                    capabilities &= ~(Capabilities.Deletable | Capabilities.Collapsible | Capabilities.Copiable);
                    AddToClassList("whimtex-shader-graph-output");
                }
                SetPosition(new Rect(node.position, Vector2.zero));
                BuildOptions(node);
                foreach (var input in ShaderGraphNodes.Inputs(node))
                {
                    if (node.kind == ShaderGraphNodeKind.Combine && "XYZW".IndexOf(input.name, StringComparison.Ordinal) >= ShaderGraphNodes.Components(node.valueType)) continue;
                    var type = input.type ?? window.InputType(node, input.name);
                    var port = MakePort(input.name, Direction.Input, type, !input.type.HasValue);
                    if (ShaderGraphNodes.IsOptionalCurve(node, input.name)) port.tooltip = "Optional curve. Unconnected uses a linear curve.";
                    var row = new VisualElement(); row.AddToClassList("whimtex-shader-graph-port-row");
                    row.Add(port); inputContainer.Add(row);
                    canvas.RegisterPort(port, true);
                    if (!window.Graph.edges.Exists(e => e.inputNode == Id && e.inputPort == input.name)) BuildInput(node, input, row, port);
                    string activeTooltip = port.tooltip;
                    refresh.Add(() =>
                    {
                        string reason = ShaderGraphNodes.InputInactiveReason(Data, input.name);
                        row.SetEnabled(reason == null);
                        port.tooltip = reason ?? activeTooltip;
                        row.tooltip = reason;
                    });
                }
                foreach (string output in ShaderGraphNodes.Outputs(node))
                {
                    var type = window.OutputType(node, output);
                    var port = MakePort(output, Direction.Output, type, false);
                    if (node.kind == ShaderGraphNodeKind.Split)
                    {
                        string activeTooltip = port.tooltip;
                        refresh.Add(() =>
                        {
                            bool active = "XYZW".IndexOf(output, StringComparison.Ordinal) < ShaderGraphNodes.Components(window.InputType(Data, "In"));
                            port.SetEnabled(active);
                            port.tooltip = active ? activeTooltip : "This component is not present in the input.";
                        });
                    }
                    canvas.RegisterPort(port, false); outputContainer.Add(port);
                }
                if (NodePreview.Supports(window, node))
                {
                    var preview = canvas.GetPreview(node);
                    EnableInClassList("whimtex-shader-graph-node--preview", preview.Expanded);
                    extensionContainer.Add(preview);
                    refresh.Add(preview.SyncExpanded);
                }
                if (node.kind == ShaderGraphNodeKind.CustomFunction) BuildFunctionEditor();
                if (node.kind == ShaderGraphNodeKind.Output)
                {
                    AddToClassList("whimtex-shader-graph-output--terminal");
                    titleContainer.BringToFront();
                    var destination = new Label("Layer result") { pickingMode = PickingMode.Ignore };
                    destination.AddToClassList("whimtex-shader-graph-output-destination");
                    titleContainer.Add(destination);
                }
                RefreshExpandedState(); RefreshPorts();
                RefreshValues();
            }
            private Port MakePort(string name, Direction direction, ShaderGraphValueType type, bool dynamic)
            {
                var port = CreatePort(Id, name, direction, type, dynamic);
                port.portName = PortLabel(ShaderGraphNodes.PortDisplayName(Data, name, direction == Direction.Output), type);
                if (Data?.kind == ShaderGraphNodeKind.Output && name == "AlphaScale")
                    port.tooltip = "Multiplies the RGBA input's alpha. Default 1 preserves it; RGB is unchanged.";
                if (Data?.kind == ShaderGraphNodeKind.Swizzle && direction == Direction.Input)
                    port.tooltip = "Numeric or Color. Width is the maximum required by the source and mask; _ inserts zero.";
                var definition = Data?.function?.ports.Find(p => p.name == name);
                if (!string.IsNullOrEmpty(definition?.label)) port.portName = PortLabel(definition.label, type);
                return port;
            }
            internal static Port CreatePort(string id, string name, Direction direction, ShaderGraphValueType type, bool dynamic)
            {
                var port = new GraphPort(direction, PortType(type));
                port.portName = PortLabel(name, type);
                port.userData = new PortData { nodeId = id, name = name, direction = direction, type = type, dynamic = dynamic };
                port.AddToClassList("whimtex-shader-graph-port");
                if (!ShaderGraphNodes.IsNumeric(type))
                    port.AddToClassList(type == ShaderGraphValueType.Boolean ? "whimtex-shader-graph-port--boolean" : "whimtex-shader-graph-port--resource");
                port.portColor = PortColor(type);
                port.tooltip = dynamic ? "Numeric or Color. Scalars expand to the vector width; missing vector components are zero." : TypeName(type);
                return port;
            }
            internal static string PortLabel(string name, ShaderGraphValueType type)
            {
                if (string.IsNullOrEmpty(name)) return string.Empty;
                string suffix = type switch
                {
                    ShaderGraphValueType.Float => "1", ShaderGraphValueType.Float2 => "2",
                    ShaderGraphValueType.Float3 => "3", ShaderGraphValueType.Float4 => "4",
                    ShaderGraphValueType.Texture => "T2", ShaderGraphValueType.Gradient => "G",
                    ShaderGraphValueType.Curve => "C", ShaderGraphValueType.Color => "col",
                    ShaderGraphValueType.Boolean => "bool", _ => type.ToString()
                };
                return name + "(" + suffix + ")";
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
                var modes = ShaderGraphNodes.ModeNames(node.kind);
                if (modes != null) Choice(ShaderGraphNodes.ModeLabel(node.kind), modes, node.option, value =>
                    window.Edit("Change Shader Mode", graph => ShaderGraphNodes.ChangeMode(graph, graph.FindNode(Id), value)));
                if (node.kind == ShaderGraphNodeKind.ConvertColor)
                {
                    const string spaces = "HSV and HSL use sRGB-encoded RGB and hue in turns. HSV preserves HDR Value but excludes negative RGB; HSL is limited to 0–1. Linear RGB, sRGB and Oklab retain HDR and negative values.";
                    Choice("From", ShaderGraphNodes.ColorSpaceNames, (int)node.inputColorSpace, value => Change(n => n.inputColorSpace = (ShaderGraphColorSpace)value), spaces);
                    Choice("To", ShaderGraphNodes.ColorSpaceNames, (int)node.outputColorSpace, value => Change(n => n.outputColorSpace = (ShaderGraphColorSpace)value), spaces);
                }
                if (ShaderGraphNodes.UsesHeightTexture(node.kind))
                    Choice("Source", new[] { "Texture", "Value" }, (int)node.heightSource, value => window.Edit("Change Height Source", graph =>
                    {
                        var changed = graph.FindNode(Id); changed.heightSource = (ShaderGraphHeightSource)value;
                        changed.inputs.RemoveAll(p => p.port == "Height");
                        graph.edges.RemoveAll(e => e.inputNode == Id && e.inputPort == "Height");
                    }), "Value evaluates the connected branch at neighboring UV coordinates. Radius uses output pixels; Texture uses source texels.");
                if (ShaderGraphNodes.UsesChannel(node.kind) && (!ShaderGraphNodes.UsesHeightTexture(node.kind) || node.heightSource == ShaderGraphHeightSource.Texture))
                    Choice("Channel", ShaderGraphNodes.ChannelNames, (int)node.channel, value => Change(n => n.channel = (ShaderGraphChannel)value));
                if (ShaderGraphNodes.UsesHeightTexture(node.kind))
                    Choice("Derivative", ShaderGraphNodes.DerivativeNames, (int)node.derivative, value => Change(n => n.derivative = (ShaderGraphDerivative)value));
                if (ShaderGraphNodes.UsesNormalEncoding(node.kind))
                    Choice(node.kind == ShaderGraphNodeKind.HeightToNormal ? "Output Encoding" : "Normal Encoding", ShaderGraphNodes.NormalEncodingNames,
                        (int)node.normalEncoding, value => Change(n => ShaderGraphNodes.ChangeNormalEncoding(n, (ShaderGraphNormalEncoding)value)));
                if (ShaderGraphNodes.IsNoise(node.kind)) BuildNoiseOptions(node);
                if (node.kind == ShaderGraphNodeKind.Buffer)
                {
                    var resolution = new EnumField("Resolution", node.bufferResolution);
                    resolution.RegisterValueChangedCallback(e => Change(n => n.bufferResolution = (ShaderGraphBufferResolution)e.newValue));
                    AddOption(resolution);
                    if (node.bufferResolution == ShaderGraphBufferResolution.Custom)
                    {
                        var size = new Vector2IntField("Size") { value = node.bufferSize };
                        foreach (var component in size.Query<IntegerField>().ToList())
                        {
                            component.isDelayed = true;
                            // Native Vector2IntField row: singleLineHeight (18 px in Unity 6000.7).
                            component.style.minHeight = EditorGUIUtility.singleLineHeight;
                        }
                        size.RegisterValueChangedCallback(e => Change(n => n.bufferSize = new Vector2Int(Mathf.Max(1, e.newValue.x), Mathf.Max(1, e.newValue.y)), rebuild: false));
                        refresh.Add(() => size.SetValueWithoutNotify(Data.bufferSize)); AddOption(size);
                    }
                }
                if (node.kind == ShaderGraphNodeKind.Fractal)
                {
                    Choice("Mode", ShaderGraphNodes.FractalModes, (int)node.fractal - 1,
                        value => Change(n => n.fractal = (ShaderGraphFractal)(value + 1)));
                    BuildNoiseFiltering(node);
                }
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.Constant:
                        var constant = new EnumField(node.constant);
                        constant.RegisterValueChangedCallback(e => Change(n => n.constant = (ShaderGraphConstant)e.newValue));
                        AddOption(constant); break;
                    case ShaderGraphNodeKind.Swizzle:
                        var mask = new TextField("Mask") { value = node.swizzle, isDelayed = true };
                        mask.tooltip = "1–4 components: xyzw or rgba. _ inserts zero. Example: _x_y produces (0, x, 0, y).";
                        mask.RegisterValueChangedCallback(e => Change(n => n.swizzle = e.newValue));
                        AddOption(mask); break;
                    case ShaderGraphNodeKind.Combine:
                        TypeChoice("Type", node.valueType, false, true, type => window.Edit("Change Combine Width", graph =>
                        {
                            graph.FindNode(Id).valueType = type;
                            graph.edges.RemoveAll(e => e.inputNode == Id && "XYZW".IndexOf(e.inputPort, StringComparison.Ordinal) >= ShaderGraphNodes.Components(type));
                        })); break;

                    case ShaderGraphNodeKind.Compare:
                        Choice("Compare", new[] { "Less", "Less or Equal", "Greater", "Greater or Equal", "Equal", "Not Equal" }, node.option, value => Change(n => n.option = value)); break;
                    case ShaderGraphNodeKind.Blend:
                        var blend = new EnumField("Mode", (BlendMode)node.option);
                        blend.RegisterValueChangedCallback(e => Change(n => n.option = (int)(BlendMode)e.newValue)); AddOption(blend);
                        var hdr = new Toggle("HDR") { value = node.hdr };
                        hdr.RegisterValueChangedCallback(e => Change(n => n.hdr = e.newValue)); AddOption(hdr); break;
                    case ShaderGraphNodeKind.HeightToNormal:
                    case ShaderGraphNodeKind.HeightToDistortion:
                    case ShaderGraphNodeKind.SampleTexture:
                    case ShaderGraphNodeKind.SampleInput:
                    case ShaderGraphNodeKind.Buffer:
                        if (ShaderGraphNodes.UsesHeightTexture(node.kind) && node.heightSource == ShaderGraphHeightSource.Value) break;
                        var wrap = new EnumField("Wrap", node.wrap);
                        wrap.RegisterValueChangedCallback(e => Change(n => n.wrap = (ShaderGraphWrap)e.newValue)); AddOption(wrap);
                        var filter = new EnumField("Filter", node.filter);
                        filter.RegisterValueChangedCallback(e => Change(n => n.filter = (ShaderGraphFilter)e.newValue)); AddOption(filter); break;
                }
            }
            private void BuildNoiseOptions(ShaderGraphNode node)
            {
                bool grain = ShaderGraphNodes.IsGrainNoise(node.kind);
                Choice("Dimensions", grain ? new[] { "1D", "2D" } : new[] { "1D", "2D", "3D" }, (int)node.noiseDimensions,
                    value => window.Edit("Change Noise Dimensions", graph =>
                        ShaderGraphNodes.ChangeNoiseDimensions(graph, graph.FindNode(Id), (ShaderGraphNoiseDimensions)value)));
                if (grain)
                {
                    var invert = new Toggle("Invert") { value = node.noiseInverted };
                    invert.RegisterValueChangedCallback(e => Change(n => n.noiseInverted = e.newValue)); AddOption(invert);
                    return;
                }
                if (ShaderGraphNodes.IsWarpNoise(node.kind))
                    Choice("Fractal", ShaderGraphNodes.WarpFractals, node.fractal == ShaderGraphFractal.None ? 0 : (int)node.fractal - 3,
                        value => Change(n => n.fractal = value == 0 ? ShaderGraphFractal.None : (ShaderGraphFractal)(value + 3)));
                BuildNoiseFiltering(node);
                if (node.kind is ShaderGraphNodeKind.CellularNoise or ShaderGraphNodeKind.VoronoiEdgeNoise or ShaderGraphNodeKind.VoronoiseNoise)
                    Choice("Distribution", new[] { "Uniform", "Fixed Radius" }, (int)node.noiseDistribution,
                        value => Change(n => n.noiseDistribution = (ShaderGraphNoiseDistribution)value));
                if (node.kind == ShaderGraphNodeKind.CellularNoise)
                {
                    Choice("Metric", new[] { "Euclidean", "Euclidean Squared", "Manhattan", "Hybrid" }, (int)node.noiseMetric,
                        value => Change(n => n.noiseMetric = (ShaderGraphNoiseMetric)value));
                    Choice("Output", new[] { "Cell Value", "F1", "F2", "Add", "Subtract", "Multiply", "Divide", "Smooth F1" }, (int)node.noiseOutput,
                        value => Change(n => n.noiseOutput = (ShaderGraphNoiseOutput)value));
                }
            }
            private void BuildNoiseFiltering(ShaderGraphNode node)
            {
                var filtering = new Toggle("Filtering") { value = node.noiseFiltering };
                filtering.RegisterValueChangedCallback(e => Change(n => n.noiseFiltering = e.newValue)); AddOption(filtering);
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
                if (input.uvDefault || input.canvasSizeDefault)
                {
                    bool useDefault = node.FindInput(name) == null;
                    var mode = new DropdownField(new List<string> { input.canvasSizeDefault ? "Canvas" : "UV", "Custom" }, useDefault ? 0 : 1);
                    mode.tooltip = input.canvasSizeDefault
                        ? "Canvas uses the canvas size in pixels. Custom sets a constant value while the input remains unconnected."
                        : "UV uses this pixel's coordinates. Custom sets a constant value while the input remains unconnected.";
                    mode.AddToClassList("whimtex-shader-graph-uv-mode");
                    mode.RegisterValueChangedCallback(e => Change(n =>
                    {
                        n.inputs.RemoveAll(i => i.port == name);
                        if (e.newValue == "Custom") n.inputs.Add(new ShaderGraphInput
                        {
                            port = name,
                            type = input.canvasSizeDefault ? ShaderGraphValueType.Float2 : input.type ?? ShaderGraphValueType.Float2
                        });
                    }));
                    AddOption(mode, fields);
                    if (useDefault) return;
                }
                var value = node.FindInput(name);
                ShaderGraphValueType type = value?.type ?? input.type ?? ShaderGraphValueType.Float;
                if (node.kind == ShaderGraphNodeKind.Swizzle) type = window.InputType(node, name);
                if (node.kind == ShaderGraphNodeKind.ConvertColor && node.inputColorSpace != ShaderGraphColorSpace.LinearRGB)
                    type = ShaderGraphValueType.Float4;
                var definition = node.function?.ports.Find(p => p.name == name);
                if (type == ShaderGraphValueType.Float && definition?.hasRange == true)
                {
                    var slider = new Slider(definition.minimum, definition.maximum) { showInputField = true, value = value?.value.x ?? input.value.x };
                    fields.style.flexGrow = 1; slider.style.flexGrow = 1; slider.style.minWidth = 0;
                    slider.RegisterValueChangedCallback(e => Change(n =>
                    {
                        var item = n.FindInput(name);
                        if (item == null) n.inputs.Add(item = new ShaderGraphInput { port = name, type = type });
                        item.value.x = e.newValue;
                    }, structural: Data.FindInput(name) == null, rebuild: false));
                    refresh.Add(() => slider.SetValueWithoutNotify(Data.FindInput(name)?.value.x ?? input.value.x));
                    AddOption(slider, fields); return;
                }
                if (!input.type.HasValue)
                {
                    fields.tooltip = node.kind == ShaderGraphNodeKind.Swizzle
                        ? "Unconnected value. The mask sets the minimum width; right-click to choose a wider type."
                        : "Unconnected value. Right-click to choose its numeric type.";
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
                ValueField(null, type, () => ShaderGraphNodes.InputValue(Data, name, input.value),
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
                if (!element.ClassListContains(BaseCompositeField<Vector2, FloatField, float>.fieldUssClassName))
                    element.AddToClassList("whimtex-shader-graph-node-field");
                element.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode is KeyCode.Backspace or KeyCode.Delete or KeyCode.Space or KeyCode.LeftArrow or KeyCode.RightArrow or KeyCode.UpArrow or KeyCode.DownArrow)
                        e.StopPropagation();
                });
                (target ?? extensionContainer).Add(element);
            }
            private void Choice(string label, string[] options, int selected, Action<int> changed, string tooltip = null)
            {
                var field = new DropdownField(label, new List<string>(options), Mathf.Clamp(selected, 0, options.Length - 1)) { tooltip = tooltip };
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
                        var number = new FloatField(label ?? "X") { value = read().x };
                        number.AddToClassList(BaseCompositeField<Vector2, FloatField, float>.fieldUssClassName);
                        number.AddToClassList(BaseCompositeField<Vector2, FloatField, float>.firstFieldVariantUssClassName);
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
