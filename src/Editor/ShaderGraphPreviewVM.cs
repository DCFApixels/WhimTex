using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Editor-only prototype. Unsupported programs always keep the native HLSL path.
    internal sealed partial class ShaderGraphPreviewVM : IDisposable
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
        bool extended, volume, noise, periodicNoise, basisDerivatives, ready, usesCanvasPosition;
        ShaderExecutableCache.Entry executor;
        ShaderGraphPreparation.Priority executorPriority;
        int noiseFamilies, executionFeatures;
        int shaderRevision = -1;
        const string ExtendedKeyword = "WT_VM_EXTENDED";
        const string VolumeKeyword = "WT_VM_VOLUME";
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
            renderContext = active.HasBoundaries
                ? active.hybridRuntime.PrepareCoordinatesFor(active.material, owner.Owner, context)
                : ShaderProgramRuntime.PrepareGraphCoordinates(active.material, owner.Owner, context, active.usesCanvasPosition);
            return active.RenderBoundaries(target, context);
        }

        ShaderGraphPreviewVM() { instructions = Array.Empty<Instruction>(); }

        ShaderGraphPreviewVM(Builder builder, int result)
        { instructions = builder.code.ToArray(); UsesTime = builder.usesTime; extended = builder.extended; volume = builder.volume; noise = builder.noise;
            noiseFamilies = builder.noiseFamilies; executionFeatures = builder.executionFeatures; palettes = builder.palettes.ToArray(); transforms = builder.transforms.ToArray();
            imageParameters=builder.images.ToArray();inputSlot=builder.inputSlot;usesCanvasPosition = builder.usesCanvasPosition; this.result = result;
            foreach (var op in instructions)
                if (op.op is 20 or 21 or 22 or 25 or 26 or 27 or 28)
                {
                    var period = instructions[op.n];
                    periodicNoise |= period.op != 0 || period.value.x > 0 || op.m >= 2 && period.value.y > 0 || op.m == 3 && period.value.z > 0;
                    basisDerivatives |= op.op == 20 && op.j != 0;
                }
        }

        internal void ReplaceCode(ShaderGraphPreviewVM prepared)
        {
            bool imagesChanged = HasImages != prepared.HasImages;
            imageParameters=prepared.imageParameters;inputSlot=prepared.inputSlot;
            bool palettesChanged = (palettes.Length == 0) != (prepared.palettes.Length == 0) || (transforms.Length == 0) != (prepared.transforms.Length == 0);
            palettes = prepared.palettes;
            transforms = prepared.transforms;
            usesCanvasPosition = prepared.usesCanvasPosition;
            Failed = false;
            ReleaseHybrid(); lightingSurfaces = prepared.lightingSurfaces; prepared.lightingSurfaces = null;
            hybridPreparation = prepared.hybridPreparation; prepared.hybridPreparation = null;
            hybridPreparedSource = prepared.hybridPreparedSource; hybridPreparedAnalysis = prepared.hybridPreparedAnalysis;
            hybridGraph = prepared.hybridGraph; hybridCompilation = prepared.hybridCompilation;
            hybridInputs = prepared.hybridInputs; hybridLightingVariant = -1;
            instructions = prepared.instructions; UsesTime = prepared.UsesTime; result = prepared.result;
            if (extended != prepared.extended || volume != prepared.volume || noise != prepared.noise || periodicNoise != prepared.periodicNoise || basisDerivatives != prepared.basisDerivatives || noiseFamilies != prepared.noiseFamilies || executionFeatures != prepared.executionFeatures || imagesChanged || palettesChanged)
            {
                extended = prepared.extended;
                volume = prepared.volume; noise = prepared.noise; periodicNoise = prepared.periodicNoise;
                basisDerivatives = prepared.basisDerivatives;
                noiseFamilies = prepared.noiseFamilies;
                executionFeatures = prepared.executionFeatures;
                ReleaseExecutor();
                if (material != null) SetVariant();
            }
            if (material != null)
            {
                material.SetInt("_GraphCanvasCoordinates", 0);
                material.SetVector("_VMViewport", new Vector4(0, 0, 1, 1));
                if (instructions.Length != 0) Upload();
            }
        }
        void SetVariant()
        {
            bool imageMath = HasImages && !noise && !extended;
            if (HasImages && !imageMath) material.EnableKeyword("WT_VM_IMAGES"); else material.DisableKeyword("WT_VM_IMAGES");
            if (imageMath) material.EnableKeyword("WT_VM_IMAGE_MATH"); else material.DisableKeyword("WT_VM_IMAGE_MATH");
            if (extended && !volume) material.EnableKeyword(ExtendedKeyword);
            else material.DisableKeyword(ExtendedKeyword);
            if (volume) material.EnableKeyword(VolumeKeyword);
            else material.DisableKeyword(VolumeKeyword);
            void Keyword(string name, bool enabled) { if (enabled) material.EnableKeyword(name); else material.DisableKeyword(name); }
            Keyword("WT_VM_SURFACE", surfaceResults != null);
            Keyword("WT_VM_PERIODIC", periodicNoise);
            Keyword("WT_VM_BASIS_DERIVATIVES", basisDerivatives);
            Keyword("WT_VM_BASIS", (noiseFamilies & 1) != 0);
            Keyword("WT_VM_GABOR", (noiseFamilies & 2) != 0);
            Keyword("WT_VM_CELLULAR", (noiseFamilies & 4) != 0);
            Keyword("WT_VM_SHARD", (noiseFamilies & 8) != 0);
            Keyword("WT_VM_SPOTS", (noiseFamilies & 16) != 0);
            Keyword("WT_VM_TRIANGLE", (noiseFamilies & 32) != 0);
            Keyword("WT_VM_WARP", (noiseFamilies & 64) != 0);
            Keyword("WT_VM_GRAIN", (noiseFamilies & 128) != 0);
            Keyword("WT_VM_PALETTES", palettes.Length != 0);
            Keyword("WT_VM_TRANSFORMS", transforms.Length != 0);
            Keyword("WT_VM_SAMPLING", (executionFeatures & 1) != 0);
            Keyword("WT_VM_FRACTAL", (executionFeatures & 2) != 0);
            Keyword("WT_VM_BLEND", (executionFeatures & 4) != 0);
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
            UploadSurface();
        }

        internal static bool TryCreate(ShaderGraph graph, string nodeId, string port, out ShaderGraphPreviewVM vm)
            => TryCreatePreview(graph, nodeId, port, false, out vm);

        internal static bool TryCreatePreview(ShaderGraph graph, string nodeId, string port, bool simplifiedLighting, out ShaderGraphPreviewVM vm)
        {
            try
            {
                return NeedsExpansion(graph)
                    ? TryCreatePrepared(ShaderGraphCompiler.Prepare(graph), nodeId, port, simplifiedLighting, out vm)
                    : TryCreateInstructions(graph, nodeId, port, simplifiedLighting, out vm);
            }
            catch (Exception error) when (error is NotSupportedException || error is ShaderGraphException || error is FormatException)
            { LastFallbackReason = error.Message; vm = null; return false; }
        }

        static bool NeedsExpansion(ShaderGraph graph) => graph.activeSubGraph != null || graph.subGraphs.Count != 0;

        internal static bool TryCreatePrepared(ShaderGraphCompiler.Prepared prepared, string node, string port,
            bool simplifiedLighting, out ShaderGraphPreviewVM vm)
        {
            prepared.ResolvePreview(ref node, ref port);
            return TryCreateInstructions(prepared.Graph, node, port, simplifiedLighting, out vm);
        }

        static bool TryCreateInstructions(ShaderGraph graph, string nodeId, string port, bool simplifiedLighting, out ShaderGraphPreviewVM vm)
        {
            vm = null;
            try
            {
                var builder = new Builder(graph, simplifiedLighting: nodeId != null && simplifiedLighting);
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

        internal static bool SupportsSubGraph(ShaderGraph graph, ShaderGraphSubGraph definition, string port = null)
        {
            var node = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraph, subGraphId = definition.id };
            var candidate = new ShaderGraph { parameters = graph.parameters, subGraphs = graph.subGraphs,
                functions = graph.functions, sources = graph.sources };
            candidate.nodes.Add(node);
            candidate.nodes.Add(ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, default));
            try
            {
                var prepared = ShaderGraphCompiler.Prepare(candidate);
                foreach (var output in definition.outputs)
                {
                    if (port != null && output.id != port) continue;
                    string id = node.id, mappedPort = output.id;
                    prepared.ResolvePreview(ref id, ref mappedPort);
                    if (SupportsCandidate(prepared.Graph, prepared.Graph.FindNode(id), mappedPort)) return true;
                }
            }
            catch (Exception error) when (error is NotSupportedException || error is ShaderGraphException || error is FormatException) { }
            return false;
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
                var builder = new Builder(graph, simplifiedLighting: true);
                var value = builder.Emit(node.id, port);
                // Logical ports can drive Select without having an image preview themselves.
                // Palette parameters likewise feed a sampling node rather than an image card.
                if (value.type is not (ShaderGraphValueType.Boolean or ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve or ShaderGraphValueType.Transform2D))
                    builder.Cast(value, ShaderGraphValueType.Color);
                return true;
            }
            catch (NotSupportedException) { return false; }
        }

        internal bool Ready
        {
            get
            {
                if (!CompleteHybridPreparation()) return false;
                if (instructions.Length == 0) { Failed = true; return false; }
                int revision = ShaderProgramSourceRevision.Current;
                if (shaderRevision != revision)
                {
                    shaderRevision = revision;
                    ReleaseExecutor(); Failed = false;
                }
                if (Failed) return false;
                if (ready) return HybridReady();
                if (!SystemInfo.supportsComputeShaders) { Failed = true; return false; }
                if (material == null)
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/GraphPreviewVM.shader");
                    if (shader == null || ShaderUtil.ShaderHasError(shader)) { Failed = true; return false; }
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    SetVariant();
                    material.SetVector("_VMViewport", new Vector4(0, 0, 1, 1));
                    Upload();
                }
                try
                {
                    executorPriority ??= new ShaderGraphPreparation.Priority(hybridMain) { previewRank = hybridRank, wanted = hybridWanted };
                    executor ??= ShaderExecutableCache.AcquireVariant(material, executorPriority);
                    ready = ShaderExecutableCache.IsReady(executor);
                }
                catch (Exception error) { LastFallbackReason = error.Message; Failed = true; return false; }
                if (ShaderUtil.ShaderHasError(material.shader)) { Failed = true; return false; }
                if (ready && !material.shader.isSupported) { ready = false; Failed = true; return false; }
                // Queue dependencies without first waiting for this executor to compile.
                bool dependenciesReady = HybridReady();
                return ready && dependenciesReady;
            }
        }

        internal bool Render(RenderTexture target, float time = 0)
        {
            if (!Ready) return false;
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                material.SetFloat("_VMTime", time);
                BindPalettes();
                GL.sRGBWrite = false;
                if (surfaceResults != null) { Graphics.SetRenderTarget(surfaceTargets, surfaceDepth); GL.Clear(false, true, Color.clear); Graphics.Blit(Texture2D.blackTexture, material, 0); }
                else Graphics.Blit(Texture2D.blackTexture, target, material, 0);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            return true;
        }
        void ReleaseExecutor()
        {
            if (executor != null) { executor.requests.Remove(executorPriority); executor.Release(); executor = null; }
            executorPriority = null; ready = false;
        }
        public void Dispose()
        { ReleaseExecutor(); ReleaseHybrid(); ReleasePalettes(); emptyInput?.Dispose();emptyInput=null;buffer?.Dispose(); buffer = null; if (material != null) UnityEngine.Object.DestroyImmediate(material); material = null; }

        internal readonly struct Value
        {
            internal readonly int register;
            internal readonly ShaderGraphValueType type;
            internal Value(int register, ShaderGraphValueType type) { this.register = register; this.type = type; }
        }
        sealed partial class Builder
        {
            internal bool usesCanvasPosition;
            readonly ShaderGraph graph;
            readonly Dictionary<string, ShaderGraphNode> nodes = new();
            readonly Dictionary<string, ShaderGraphEdge> incoming = new();
            Dictionary<string, Value> values = new();
            readonly Dictionary<(int, int, int, int, ShaderGraphValueType), Value> literals = new();
            readonly HashSet<string> active = new();
            internal readonly List<Instruction> code = new();
            internal bool usesTime;
            internal bool extended;
            internal bool volume, noise;
            internal int noiseFamilies, executionFeatures;
            readonly bool simplifiedLighting;
            bool varyingIterations, rootDerivatives;
            int depth, work, multiplier = 1;
            internal Builder(ShaderGraph graph, IReadOnlyDictionary<string, ShaderGraphBufferPass> boundaries = null, bool simplifiedLighting = false)
            {
                this.boundaries = boundaries;
                this.simplifiedLighting = simplifiedLighting;
                this.graph = graph;
                if (graph == null || graph.version != 1 || graph.nodes == null || graph.edges == null || graph.parameters == null) Unsupported("Graph version or structure");
                foreach (var node in graph.nodes)
                    if (node == null || node.inputs == null || string.IsNullOrEmpty(node.id) || !nodes.TryAdd(node.id, node)) Unsupported("Node ID or inputs");
                foreach (var edge in graph.edges)
                    if (edge == null || edge.inputNode == null || edge.outputNode == null || !nodes.ContainsKey(edge.inputNode) || !nodes.ContainsKey(edge.outputNode) ||
                        !incoming.TryAdd(edge.inputNode + ":" + edge.inputPort, edge)) Unsupported("Graph connection");
                var connections = new ShaderGraphConnections();
                connections.Prepare(nodes, graph);
                connections.RemoveUnboundInputs(incoming);
            }
            static void Unsupported(string reason) => throw new NotSupportedException(reason);
            internal Value Add(Instruction op, ShaderGraphValueType type)
            {
                if (code.Count >= MaxInstructions) Unsupported("Preview instruction limit: " + code.Count);
                if ((work += multiplier) > 4096) Unsupported("Preview replay work limit: " + work);
                noise |= op.op is >= 20 and <= 31;
                extended |= op.op == 40;
                if (op.op is 32 or 33 or 34 or 47) executionFeatures |= 1;
                if (op.op is 30 or 31) executionFeatures |= 2;
                if (op.op == 40 && op.o == (int)ShaderGraphNodeKind.Blend) executionFeatures |= 4;
                code.Add(op); return new Value(code.Count - 1, type);
            }
            Value Literal(Vector4 value, ShaderGraphValueType type)
            {
                if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z) || !float.IsFinite(value.w)) Unsupported("Nonfinite value");
                var key = (BitConverter.SingleToInt32Bits(value.x), BitConverter.SingleToInt32Bits(value.y),
                    BitConverter.SingleToInt32Bits(value.z), BitConverter.SingleToInt32Bits(value.w), type);
                if (literals.TryGetValue(key, out var cached)) return cached;
                var result = Add(new Instruction { op = 0, value = value }, type);
                literals.Add(key, result);
                return result;
            }
            Value Number(float value) => Literal(new Vector4(value, 0, 0, 0), ShaderGraphValueType.Float);
            internal Value Cast(Value value, ShaderGraphValueType type)
            {
                if (value.type == type) return value;
                if (!ShaderGraphNodes.IsNumeric(value.type) || !ShaderGraphNodes.IsNumeric(type)) Unsupported("Resource or boolean value");
                // Both types share all four components; preserve the semantic type without copying the register.
                if (value.type == ShaderGraphValueType.Float4 && type == ShaderGraphValueType.Color ||
                    value.type == ShaderGraphValueType.Color && type == ShaderGraphValueType.Float4)
                    return new Value(value.register, type);
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
                        if (node.kind == ShaderGraphNodeKind.Swizzle) type = ShaderGraphNodes.SwizzleInputType(node.swizzle, type);
                        if (ShaderGraphInput.IsResource(type)) return Palette(input?.resource, type);
                        if (!ShaderGraphNodes.IsNumeric(type) && type != ShaderGraphValueType.Boolean) { Unsupported("Resource input"); return default; }
                        if (port.canvasSizeDefault && input == null)
                        { extended = true; value = Add(new Instruction { op=5 }, ShaderGraphValueType.Float2); }
                        else if (port.uvDefault && (input == null || input.source != ShaderGraphInputSource.Value))
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
                if (boundaries != null && boundaries.TryGetValue(id, out var boundary))
                { result = Boundary(node, boundary); active.Remove(key); values.Add(key, result); return result; }
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.Parameter:
                    {
                        var parameter = graph.FindParameter(node.parameterId);
                        if (parameter == null) Unsupported("Missing parameter");
                        var type = ShaderGraphNodes.ParameterType(parameter);
                        if (ShaderGraphInput.IsResource(type)) { result = Palette(parameter, type); break; }
                        if (!ShaderGraphNodes.IsNumeric(type) && type != ShaderGraphValueType.Boolean) Unsupported("Resource parameter");
                        Vector4 data = Vector4.zero;
                        for (int component = 0; component < parameter.Components; component++) data[component] = (float)parameter.GetNumber(component);
                        bool typed = ShaderNumericValue.NeedsTypedBinding(parameter);
                        if (type == ShaderGraphValueType.Color && !parameter.IsInteger) data = HdrUtility.Decode((Color)data);
                        if (parameter.type == ShaderParameterType.Toggle) data.x = parameter.BoolValue ? 1 : 0;
                        if (parameter.type == ShaderParameterType.Normal && !typed) data = ShaderParameter.NormalizeNormal(data);
                        result = Literal(data, type);
                        if (parameter.UsesPixelScale)
                        {
                            extended = true;
                            var scale = Cast(Add(new Instruction { op = 5, i = 2 }, ShaderGraphValueType.Float), type);
                            result = Add(new Instruction { op = 10, a = result.register, b = scale.register,
                                c = (int)ShaderGraphNodeKind.Multiply, d = ShaderGraphNodes.Components(type) }, type);
                        }
                        break;
                    }
                    case ShaderGraphNodeKind.UV: result = Add(new Instruction { op = 2, b = 2 }, ShaderGraphValueType.Float2); break;
                    case ShaderGraphNodeKind.CanvasPosition:
                        extended = usesCanvasPosition = true;
                        result = Add(new Instruction { op=6,i=port=="AspectX"?1:port=="AspectY"?2:0 },ShaderGraphValueType.Float2); break;
                    case ShaderGraphNodeKind.CanvasData:
                        extended = true;
                        result = Add(new Instruction { op=5,i=port=="Size"?0:port=="Texel"?1:2 },
                            port=="PixelScale"?ShaderGraphValueType.Float:ShaderGraphValueType.Float2); break;
                    case ShaderGraphNodeKind.SubGraphInput:
                        if (!node.expandedInterface) Unsupported("Unexpanded Sub Graph input");
                        result = V("In"); break;
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
                    case ShaderGraphNodeKind.Modulo: case ShaderGraphNodeKind.Atan2:
                    {
                        var a = V(node.kind == ShaderGraphNodeKind.Step ? "Edge" : node.kind == ShaderGraphNodeKind.Atan2 ? "Y" : "A");
                        var b = V(node.kind == ShaderGraphNodeKind.Step ? "In" : node.kind == ShaderGraphNodeKind.Atan2 ? "X" : "B");
                        var type = Common(a, b); a = Cast(a, type); b = Cast(b, type);
                        result = Add(new Instruction { op = 10, a = a.register, b = b.register, c = kind, d = ShaderGraphNodes.Components(type) },
                            node.kind is ShaderGraphNodeKind.Dot or ShaderGraphNodeKind.Distance ? ShaderGraphValueType.Float : type); break;
                    }
                    case ShaderGraphNodeKind.Abs: case ShaderGraphNodeKind.OneMinus: case ShaderGraphNodeKind.Negate:
                    case ShaderGraphNodeKind.SquareRoot: case ShaderGraphNodeKind.Floor: case ShaderGraphNodeKind.Ceil:
                    case ShaderGraphNodeKind.Round: case ShaderGraphNodeKind.Fraction: case ShaderGraphNodeKind.Sine:
                    case ShaderGraphNodeKind.Cosine: case ShaderGraphNodeKind.Length: case ShaderGraphNodeKind.Normalize:
                    case ShaderGraphNodeKind.Sign: case ShaderGraphNodeKind.Saturate: case ShaderGraphNodeKind.Asin:
                    case ShaderGraphNodeKind.Acos: case ShaderGraphNodeKind.Exp: case ShaderGraphNodeKind.Log:
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
                    case ShaderGraphNodeKind.PerlinNoise: case ShaderGraphNodeKind.ValueNoise: case ShaderGraphNodeKind.ValueCubicNoise:
                    case ShaderGraphNodeKind.OpenSimplex2Noise: case ShaderGraphNodeKind.OpenSimplex2SNoise: case ShaderGraphNodeKind.FlowNoise:
                    case ShaderGraphNodeKind.GaborNoise: case ShaderGraphNodeKind.CellularNoise: case ShaderGraphNodeKind.ShardNoise:
                    {
                        int dimensions = ShaderGraphNodes.NoiseDimensions(node);
                        bool threeD = dimensions == 3;
                        if (dimensions < 1 || dimensions > 3 || node.fractal != ShaderGraphFractal.None)
                            Unsupported("Noise settings outside the SVM prototype");
                        var op = new Instruction { op = node.kind == ShaderGraphNodeKind.ShardNoise ? 25 : node.kind == ShaderGraphNodeKind.GaborNoise ? 21 : node.kind == ShaderGraphNodeKind.CellularNoise ? 22 : 20,
                            a = R("Position"), b = R("Scale"), c = R("Seed"), m = dimensions,
                            n = node.noisePeriodic ? R("Period") : Number(0).register };
                        var type = ShaderGraphValueType.Float;
                        NoiseFootprint(node, ref op);
                        noiseFamilies |= op.op == 20 ? 1 : op.op == 21 ? 2 : op.op == 22 ? 4 : 8;
                        if (op.op == 25) { op.d = R("Sharpness"); extended = true; }
                        if (op.op != 22 && op.op != 25)
                        {
                            op.j = port switch { "Value" => 0, "Gradient" => 1, "Curl" => 2,
                                "Phase" when op.op == 21 => 3, "Intensity" when op.op == 21 => 4, "Phasor" when op.op == 21 => 5,
                                _ => throw new NotSupportedException("Noise output") };
                            if (op.j is 1 or 2) type = ShaderGraphNodes.NoisePositionType(node);
                        }
                        if (op.op == 20)
                        {
                            op.o = node.kind switch { ShaderGraphNodeKind.OpenSimplex2Noise => 0, ShaderGraphNodeKind.OpenSimplex2SNoise => 1,
                                ShaderGraphNodeKind.FlowNoise => 1, ShaderGraphNodeKind.PerlinNoise => 3, ShaderGraphNodeKind.ValueCubicNoise => 4, _ => 5 };
                            op.i = node.kind == ShaderGraphNodeKind.FlowNoise ? 1 : 0;
                            op.d = op.i != 0 ? R("Phase") : Number(0).register;
                        }
                        if (op.op == 21)
                        {
                            op.d = R("Frequency"); op.e = R("Bandwidth"); op.f = R("Anisotropy"); op.g = R("Direction");
                            op.i = node.option; op.k = port == "Phasor" ? R("PhaseOffset") : Number(0).register;
                            op.l = port == "Phasor" && node.option == 2 ? R("Width") : Number(.5f).register;
                        }
                        if (op.op == 22)
                        {
                            if (!Enum.IsDefined(typeof(ShaderGraphNoiseDistribution), node.noiseDistribution) ||
                                !Enum.IsDefined(typeof(ShaderGraphNoiseMetric), node.noiseMetric) || !Enum.IsDefined(typeof(ShaderGraphNoiseOutput), node.noiseOutput)) Unsupported("Cellular options");
                            op.d = R("Jitter"); op.e = R("Evolution"); op.f = R("Smoothness");
                            op.g = (int)node.noiseDistribution; op.h = (int)node.noiseMetric; op.i = (int)node.noiseOutput;
                            op.j = port switch { "Value" => 0, "F1" => 1, "F2" => 2, "CellValue" => 3, "ToPoint" => 4, _ => throw new NotSupportedException("Cell output") };
                            if (op.j == 4) type = ShaderGraphNodes.NoisePositionType(node);
                        }
                        extended |= dimensions != 2 || node.noisePeriodic || op.op == 20 && (op.o != 3 || op.j != 0) || op.op == 21 && op.j != 0;
                        volume |= threeD;
                        result = Add(op, type); break;
                    }
                    case ShaderGraphNodeKind.Fractal:
                    {
                        if (++depth > 2 || depth + samplingDepth > 4 || Array.IndexOf(ShaderGraphNodes.FractalModeValues, node.fractal) < 0) Unsupported("Fractal nesting or mode");
                        bool cascade = ShaderGraphNodes.IsMusgraveFractal(node.fractal);
                        var op = new Instruction { op = 30, a = R("Octaves"), b = R("Lacunarity"), c = R("Gain"),
                            d = cascade ? Number(0).register : R("WeightedStrength"), e = node.fractal == ShaderGraphFractal.PingPong ? R("PingPongStrength") : Number(2).register,
                            f = R("SeedStep"), g = cascade && node.fractal != ShaderGraphFractal.Multifractal ? R("Bias") : Number(1).register,
                            h = node.fractal is ShaderGraphFractal.HybridMultifractal or ShaderGraphFractal.RidgedMultifractal ? R("Feedback") : Number(2).register,
                            i = cascade ? 1 : (int)node.fractal, j = cascade ? (int)node.fractal - (int)ShaderGraphFractal.Multifractal : -1, k = node.noiseFiltering ? 1 : 0 };
                        int begin = Add(op, ShaderGraphValueType.Float).register;
                        // Constants are uploaded instructions, so their real iteration count
                        // is known. Keep the worst-case bound for a varying Octaves input.
                        int repeats = code[op.a].op == 0 ? Mathf.Clamp((int)code[op.a].value.x, 1, 8) : 8;
                        if (code[op.a].op != 0)
                        {
                            varyingIterations = true;
                            if (rootDerivatives) Unsupported("Root derivatives after varying iteration counts");
                        }
                        var outer = values; values = new Dictionary<string, Value>(); multiplier *= repeats;
                        int previousResources = resources.Count;
                        var value = V("In");
                        if (resources.Count != previousResources && (incoming.ContainsKey(node.id + ":SeedStep") || (node.FindInput("SeedStep")?.value.x ?? 1) != 0))
                            Unsupported("A buffered source requires an unconnected Seed Step of zero");
                        result = Add(new Instruction { op = 31, a = value.register, b = begin }, value.type);
                        multiplier /= repeats; values = outer; depth--;
                        break;
                    }
                    default: result = EmitSimple(node, port); break;
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
