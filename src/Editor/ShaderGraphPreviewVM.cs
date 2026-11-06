using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Editor-only prototype. Unsupported programs always keep the native HLSL path.
    internal sealed class ShaderGraphPreviewVM : IDisposable
    {
        internal const int MaxInstructions = 128;
        [StructLayout(LayoutKind.Sequential)]
        internal struct Instruction
        {
            public int op, a, b, c, d, e, f, g, h, i, j, k, l, m, n, o;
            public Vector4 value;
        }
        internal Instruction[] instructions { get; private set; }
        internal bool UsesTime { get; private set; }
        int result;
        GraphicsBuffer buffer;
        Material material;
        internal bool Failed { get; private set; }
        internal Material Material => material;
        internal int InstructionCount => instructions.Length;
        internal static string LastFallbackReason { get; private set; }
        static readonly Dictionary<(ShaderGraphNodeKind, string), bool> defaultNodeSupport = new();
        static ShaderGraphPreviewVM active;
        static ShaderLayerBehaviour activeOwner;

        internal readonly struct Scope : IDisposable
        {
            readonly ShaderGraphPreviewVM previous;
            readonly ShaderLayerBehaviour owner;
            readonly bool initialized;
            internal Scope(ShaderGraphPreviewVM value, ShaderLayerBehaviour target)
            { previous = active; owner = activeOwner; initialized = true; active = value; activeOwner = target; }
            public void Dispose() { if (initialized) { active = previous; activeOwner = owner; } }
        }
        internal Scope UseFor(ShaderLayerBehaviour owner) => new Scope(this, owner);
        internal static bool TryRender(ShaderLayerBehaviour owner, in LayerRenderContext context, RenderTexture target,
            out LayerRenderContext renderContext)
        {
            renderContext = context;
            if (!context.shaderGraphPreview || owner.mode != ShaderProgramMode.Graph || !ReferenceEquals(owner, activeOwner) || active == null || !active.Ready)
                return false;
            renderContext = ProceduralUv.Prepare(active.material, owner.Owner, context);
            return active.Render(target, context.shaderContext.Time);
        }

        ShaderGraphPreviewVM(Builder builder, int result)
        { instructions = builder.code.ToArray(); UsesTime = builder.usesTime; this.result = result; }

        internal void ReplaceCode(ShaderGraphPreviewVM prepared)
        {
            instructions = prepared.instructions; UsesTime = prepared.UsesTime; result = prepared.result;
            if (material != null) Upload();
        }
        void Upload()
        {
            if (buffer == null || buffer.count < instructions.Length)
            {
                buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(instructions.Length), Marshal.SizeOf<Instruction>());
                material.SetBuffer("_VMCode", buffer);
            }
            buffer.SetData(instructions, 0, 0, instructions.Length);
            material.SetInt("_VMCount", instructions.Length);
            material.SetInt("_VMResult", result);
        }

        internal static bool TryCreate(ShaderGraph graph, string nodeId, string port, out ShaderGraphPreviewVM vm)
        {
            vm = null;
            try
            {
                var builder = new Builder(graph);
                var value = nodeId == null ? builder.Output() : builder.Emit(nodeId, port);
                value = builder.Cast(value, ShaderGraphValueType.Color);
                vm = new ShaderGraphPreviewVM(builder, value.register);
                LastFallbackReason = null;
                return true;
            }
            catch (NotSupportedException error) { LastFallbackReason = error.Message; return false; }
        }

        // Ask the actual instruction builder, without shaders/GPU allocation or a second
        // handwritten capability list. Cache immutable creation defaults, not live parameters.
        internal static bool SupportsDefaultNode(ShaderGraphNodeKind kind, string port = null)
        {
            var key = (kind, port);
            if (!defaultNodeSupport.TryGetValue(key, out bool supported))
            {
                var node = ShaderGraphNodes.Create(kind, default);
                supported = SupportsCandidate(new ShaderGraph { nodes = new List<ShaderGraphNode> { node } }, node, port);
                defaultNodeSupport.Add(key, supported);
            }
            return supported;
        }

        internal static bool SupportsParameter(ShaderParameter parameter)
        {
            if (parameter == null) return false;
            var node = new ShaderGraphNode { kind = ShaderGraphNodeKind.Parameter, parameterId = parameter.id };
            return SupportsCandidate(new ShaderGraph { nodes = new List<ShaderGraphNode> { node },
                parameters = new List<ShaderParameter> { parameter } }, node, "Out");
        }

        static bool SupportsCandidate(ShaderGraph graph, ShaderGraphNode node, string port)
        {
            if (port == null)
            {
                var outputs = ShaderGraphNodes.Outputs(node);
                if (outputs.Length == 0) return false;
                port = outputs[0];
            }
            try
            {
                var builder = new Builder(graph);
                builder.Cast(builder.Emit(node.id, port), ShaderGraphValueType.Color);
                return true;
            }
            catch (NotSupportedException) { return false; }
        }

        internal bool Ready
        {
            get
            {
                if (!SystemInfo.supportsComputeShaders) { Failed = true; return false; }
                if (material == null)
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/GraphPreviewVM.shader");
                    if (shader == null || ShaderUtil.ShaderHasError(shader)) { Failed = true; return false; }
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    Upload();
                    bool asynchronous = ShaderUtil.allowAsyncCompilation;
                    try
                    {
                        ShaderUtil.allowAsyncCompilation = true;
                        if (!ShaderUtil.IsPassCompiled(material, 0)) ShaderUtil.CompilePass(material, 0, false);
                    }
                    finally { ShaderUtil.allowAsyncCompilation = asynchronous; }
                }
                if (ShaderUtil.ShaderHasError(material.shader)) { Failed = true; return false; }
                bool ready = ShaderUtil.IsPassCompiled(material, 0);
                if (ready && !material.shader.isSupported) { Failed = true; return false; }
                return ready;
            }
        }

        internal bool Render(RenderTexture target, float time = 0)
        {
            if (!Ready) return false;
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                material.SetFloat("_VMTime", time);
                GL.sRGBWrite = false;
                Graphics.Blit(Texture2D.blackTexture, target, material, 0);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            return true;
        }
        public void Dispose()
        { buffer?.Dispose(); buffer = null; if (material != null) UnityEngine.Object.DestroyImmediate(material); material = null; }

        internal readonly struct Value
        {
            internal readonly int register;
            internal readonly ShaderGraphValueType type;
            internal Value(int register, ShaderGraphValueType type) { this.register = register; this.type = type; }
        }
        sealed class Builder
        {
            readonly ShaderGraph graph;
            readonly Dictionary<string, ShaderGraphNode> nodes = new();
            readonly Dictionary<string, ShaderGraphEdge> incoming = new();
            Dictionary<string, Value> values = new();
            readonly HashSet<string> active = new();
            internal readonly List<Instruction> code = new();
            internal bool usesTime;
            int depth, work, multiplier = 1;
            internal Builder(ShaderGraph graph)
            {
                this.graph = graph;
                if (graph == null || graph.version != 1 || graph.nodes == null || graph.edges == null || graph.parameters == null) Unsupported("Graph version or structure");
                foreach (var node in graph.nodes)
                    if (node == null || node.inputs == null || string.IsNullOrEmpty(node.id) || !nodes.TryAdd(node.id, node)) Unsupported("Node ID or inputs");
                foreach (var edge in graph.edges)
                    if (edge == null || edge.inputNode == null || edge.outputNode == null || !nodes.ContainsKey(edge.inputNode) || !nodes.ContainsKey(edge.outputNode) ||
                        !incoming.TryAdd(edge.inputNode + ":" + edge.inputPort, edge)) Unsupported("Graph connection");
            }
            static void Unsupported(string reason) => throw new NotSupportedException(reason);
            internal Value Add(Instruction op, ShaderGraphValueType type)
            {
                if (code.Count >= MaxInstructions || (work += multiplier) > 4096) Unsupported("Preview instruction limit");
                code.Add(op); return new Value(code.Count - 1, type);
            }
            Value Literal(Vector4 value, ShaderGraphValueType type)
            {
                if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z) || !float.IsFinite(value.w)) Unsupported("Nonfinite value");
                return Add(new Instruction { op = 0, value = value }, type);
            }
            Value Number(float value) => Literal(new Vector4(value, 0, 0, 0), ShaderGraphValueType.Float);
            internal Value Cast(Value value, ShaderGraphValueType type)
            {
                if (!ShaderGraphNodes.IsNumeric(value.type) || !ShaderGraphNodes.IsNumeric(type)) Unsupported("Resource or boolean value");
                if (value.type == type) return value;
                return Add(new Instruction { op = 1, a = value.register, b = ShaderGraphNodes.Components(value.type),
                    c = ShaderGraphNodes.Components(type), d = type == ShaderGraphValueType.Color ? 1 : 0 }, type);
            }
            Value Input(ShaderGraphNode node, string name)
            {
                foreach (var port in ShaderGraphNodes.Inputs(node))
                {
                    if (port.name != name) continue;
                    Value value;
                    if (incoming.TryGetValue(node.id + ":" + name, out var edge)) value = Emit(edge.outputNode, edge.outputPort);
                    else
                    {
                        var input = node.FindInput(name);
                        var type = input?.type ?? port.type ?? ShaderGraphValueType.Float;
                        if (!ShaderGraphNodes.IsNumeric(type)) { Unsupported("Non-numeric input"); return default; }
                        if (port.canvasSizeDefault) { Unsupported("Canvas input"); return default; }
                        if (port.uvDefault && (input == null || input.source != ShaderGraphInputSource.Value))
                        {
                            value = Add(new Instruction { op = 2, a = input?.source == ShaderGraphInputSource.UVY ? 1 : 0,
                                b = ShaderGraphNodes.Components(port.type ?? ShaderGraphValueType.Float2), value = input?.value ?? port.value }, port.type ?? ShaderGraphValueType.Float2);
                        }
                        else value = Literal(input?.value ?? port.value, type);
                    }
                    return port.type.HasValue ? Cast(value, port.type.Value) : value;
                }
                Unsupported("Input " + name); return default;
            }
            internal Value Output()
            {
                ShaderGraphNode output = null;
                foreach (var n in nodes.Values) if (n.kind == ShaderGraphNodeKind.Output)
                { if (output != null) Unsupported("Multiple outputs"); output = n; }
                if (output == null) Unsupported("Missing output");
                var color = Input(output, "RGBA"); var alpha = Input(output, "AlphaScale");
                return Add(new Instruction { op = 4, a = color.register, b = alpha.register }, ShaderGraphValueType.Color);
            }
            internal Value Emit(string id, string port)
            {
                string key = id + ":" + port;
                if (values.TryGetValue(key, out var result)) return result;
                if (!active.Add(key)) { Unsupported("Graph cycle"); return default; }
                if (!nodes.TryGetValue(id, out var node) || !ShaderGraphNodes.OutputVisible(node, port)) { Unsupported("Output port"); return default; }
                Value V(string name) => Input(node, name);
                int R(string name) => V(name).register;
                int kind = (int)node.kind;
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.Parameter:
                    {
                        var parameter = graph.FindParameter(node.parameterId);
                        if (parameter == null || parameter.IsInteger || parameter.pixelUnits || parameter.type == ShaderParameterType.Normal)
                            Unsupported("Parameter settings outside the SVM prototype");
                        var type = ShaderGraphNodes.ParameterType(parameter);
                        if (!ShaderGraphNodes.IsNumeric(type)) Unsupported("Resource parameter");
                        Vector4 data = Vector4.zero;
                        for (int component = 0; component < parameter.Components; component++) data[component] = (float)parameter.GetNumber(component);
                        if (type == ShaderGraphValueType.Color) data = HdrUtility.Decode((Color)data);
                        result = Literal(data, type); break;
                    }
                    case ShaderGraphNodeKind.UV: result = Add(new Instruction { op = 2, b = 2 }, ShaderGraphValueType.Float2); break;
                    case ShaderGraphNodeKind.Reroute: result = V("In"); break;
                    case ShaderGraphNodeKind.Constant:
                        result = Number(node.constant switch { ShaderGraphConstant.PI => Mathf.PI, ShaderGraphConstant.TAU => 2 * Mathf.PI,
                            ShaderGraphConstant.PHI => 1.618033988749895f, ShaderGraphConstant.E => 2.718281828459045f,
                            ShaderGraphConstant.SQRT2 => 1.414213562373095f, _ => throw new NotSupportedException("Constant") }); break;
                    case ShaderGraphNodeKind.Time: case ShaderGraphNodeKind.InstanceIndex:
                        usesTime |= node.kind == ShaderGraphNodeKind.Time;
                        result = Add(new Instruction { op = 3, a = R("Start"), b = R("Scale"), c = node.kind == ShaderGraphNodeKind.Time ? 1 : 0 }, ShaderGraphValueType.Float); break;
                    case ShaderGraphNodeKind.Add: case ShaderGraphNodeKind.Subtract: case ShaderGraphNodeKind.Multiply:
                    case ShaderGraphNodeKind.Divide: case ShaderGraphNodeKind.Min: case ShaderGraphNodeKind.Max:
                    case ShaderGraphNodeKind.Power: case ShaderGraphNodeKind.Step: case ShaderGraphNodeKind.Dot: case ShaderGraphNodeKind.Distance:
                    {
                        var a = V(node.kind == ShaderGraphNodeKind.Step ? "Edge" : "A"); var b = V(node.kind == ShaderGraphNodeKind.Step ? "In" : "B");
                        var type = Common(a, b); a = Cast(a, type); b = Cast(b, type);
                        result = Add(new Instruction { op = 10, a = a.register, b = b.register, c = kind, d = ShaderGraphNodes.Components(type) },
                            node.kind is ShaderGraphNodeKind.Dot or ShaderGraphNodeKind.Distance ? ShaderGraphValueType.Float : type); break;
                    }
                    case ShaderGraphNodeKind.Abs: case ShaderGraphNodeKind.OneMinus: case ShaderGraphNodeKind.Negate:
                    case ShaderGraphNodeKind.SquareRoot: case ShaderGraphNodeKind.Floor: case ShaderGraphNodeKind.Ceil:
                    case ShaderGraphNodeKind.Round: case ShaderGraphNodeKind.Fraction: case ShaderGraphNodeKind.Sine:
                    case ShaderGraphNodeKind.Cosine: case ShaderGraphNodeKind.Length: case ShaderGraphNodeKind.Normalize:
                    {
                        var a = V("In"); result = Add(new Instruction { op = 11, a = a.register, c = kind, d = ShaderGraphNodes.Components(a.type) },
                            node.kind == ShaderGraphNodeKind.Length ? ShaderGraphValueType.Float : ShaderGraphNodes.NumericType(a.type)); break;
                    }
                    case ShaderGraphNodeKind.Lerp: case ShaderGraphNodeKind.Clamp: case ShaderGraphNodeKind.Smoothstep: case ShaderGraphNodeKind.Unlerp:
                    {
                        bool limits = node.kind is ShaderGraphNodeKind.Clamp or ShaderGraphNodeKind.Smoothstep;
                        var a = V(limits ? "In" : "A"); var b = V(limits ? "Min" : "B"); var c = V(limits ? "Max" : "T");
                        var type = node.kind == ShaderGraphNodeKind.Lerp ? Common(a, b) : Common(a, b, c);
                        result = Add(new Instruction { op = 12, a = Cast(a, type).register, b = Cast(b, type).register,
                            c = Cast(c, type).register, d = kind }, type); break;
                    }
                    case ShaderGraphNodeKind.Combine:
                    {
                        int count = ShaderGraphNodes.Components(node.valueType);
                        if (count < 1) Unsupported("Combine type");
                        int zero = Number(0).register;
                        result = Add(new Instruction { op = 13, a = R("X"), b = count > 1 ? R("Y") : zero,
                            c = count > 2 ? R("Z") : zero, d = count > 3 ? R("W") : zero }, node.valueType); break;
                    }
                    case ShaderGraphNodeKind.Split:
                    {
                        var a = V("In"); int component = "XYZW".IndexOf(port, StringComparison.Ordinal);
                        if (component < 0 || component >= ShaderGraphNodes.Components(a.type)) Unsupported("Split component");
                        result = Add(new Instruction { op = 14, a = a.register, b = component }, ShaderGraphValueType.Float); break;
                    }
                    case ShaderGraphNodeKind.PerlinNoise: case ShaderGraphNodeKind.GaborNoise: case ShaderGraphNodeKind.CellularNoise:
                    {
                        if (node.noiseDimensions != ShaderGraphNoiseDimensions.TwoD || node.noiseFiltering || node.noisePeriodic || node.fractal != ShaderGraphFractal.None)
                            Unsupported("Noise settings outside the SVM prototype");
                        if (node.kind != ShaderGraphNodeKind.CellularNoise && port != "Value" || node.kind == ShaderGraphNodeKind.GaborNoise && node.option != 0)
                            Unsupported("Noise output outside the SVM prototype");
                        var op = new Instruction { op = node.kind == ShaderGraphNodeKind.PerlinNoise ? 20 : node.kind == ShaderGraphNodeKind.GaborNoise ? 21 : 22,
                            a = R("Position"), b = R("Scale"), c = R("Seed") };
                        var type = ShaderGraphValueType.Float;
                        if (op.op == 21) { op.d = R("Frequency"); op.e = R("Bandwidth"); op.f = R("Anisotropy"); op.g = R("Direction"); }
                        if (op.op == 22)
                        {
                            if (!Enum.IsDefined(typeof(ShaderGraphNoiseDistribution), node.noiseDistribution) ||
                                !Enum.IsDefined(typeof(ShaderGraphNoiseMetric), node.noiseMetric) || !Enum.IsDefined(typeof(ShaderGraphNoiseOutput), node.noiseOutput)) Unsupported("Cellular options");
                            op.d = R("Jitter"); op.e = R("Evolution"); op.f = R("Smoothness");
                            op.g = (int)node.noiseDistribution; op.h = (int)node.noiseMetric; op.i = (int)node.noiseOutput;
                            op.j = port switch { "Value" => 0, "F1" => 1, "F2" => 2, "CellValue" => 3, "ToPoint" => 4, _ => throw new NotSupportedException("Cell output") };
                            if (op.j == 4) type = ShaderGraphValueType.Float2;
                        }
                        result = Add(op, type); break;
                    }
                    case ShaderGraphNodeKind.Fractal:
                    {
                        if (++depth > 2 || Array.IndexOf(ShaderGraphNodes.FractalModeValues, node.fractal) < 0) Unsupported("Fractal nesting or mode");
                        bool cascade = ShaderGraphNodes.IsMusgraveFractal(node.fractal);
                        var op = new Instruction { op = 30, a = R("Octaves"), b = R("Lacunarity"), c = R("Gain"),
                            d = cascade ? Number(0).register : R("WeightedStrength"), e = node.fractal == ShaderGraphFractal.PingPong ? R("PingPongStrength") : Number(2).register,
                            f = R("SeedStep"), g = cascade && node.fractal != ShaderGraphFractal.Multifractal ? R("Bias") : Number(1).register,
                            h = node.fractal is ShaderGraphFractal.HybridMultifractal or ShaderGraphFractal.RidgedMultifractal ? R("Feedback") : Number(2).register,
                            i = cascade ? 1 : (int)node.fractal, j = cascade ? (int)node.fractal - (int)ShaderGraphFractal.Multifractal : -1, k = node.noiseFiltering ? 1 : 0 };
                        int begin = Add(op, ShaderGraphValueType.Float).register;
                        var outer = values; values = new Dictionary<string, Value>(); multiplier *= 8;
                        var value = V("In");
                        result = Add(new Instruction { op = 31, a = value.register, b = begin }, value.type);
                        multiplier /= 8; values = outer; depth--;
                        break;
                    }
                    default: Unsupported("Node " + node.kind); return default;
                }
                active.Remove(key); values.Add(key, result); return result;
            }
            static ShaderGraphValueType Common(params Value[] inputs)
            {
                var type = ShaderGraphValueType.Float;
                foreach (var input in inputs)
                {
                    if (!ShaderGraphNodes.IsNumeric(input.type)) Unsupported("Numeric value required");
                    var numeric = ShaderGraphNodes.NumericType(input.type); if (numeric > type) type = numeric;
                }
                return type;
            }
        }
    }
}
