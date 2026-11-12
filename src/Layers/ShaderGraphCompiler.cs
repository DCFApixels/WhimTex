using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphCompilation
    {
        internal string source;
        internal string executableSource;
        internal string causticsSpectrum;
        internal string ExecutableSource => executableSource ?? source;
        internal ShaderGraphCompiler.GeneratedSource generatedSource;
        internal int parameterCount;
        internal bool usesCanvasPosition;
        internal bool bufferExternalCode;
        internal bool isBufferProgram;
        internal bool lightingProducer;
        internal bool simplifiedLighting;
        internal bool bindingsOnly;
        internal ShaderGraphValueType bufferOutputType;
        internal ShaderContextDependencies contextDependencies;
        internal bool usesTime => (contextDependencies & ShaderContextDependencies.Time) != 0;
        internal bool usesInstanceIndex => (contextDependencies & ShaderContextDependencies.InstanceIndex) != 0;
        internal bool usesLighting => (contextDependencies & ShaderContextDependencies.Lighting) != 0;
        internal readonly List<ShaderParameter> parameters = new List<ShaderParameter>();
        internal readonly List<ShaderParameter> shaderParameters = new List<ShaderParameter>();
        internal readonly Dictionary<string, ShaderGraphValueType> outputTypes = new Dictionary<string, ShaderGraphValueType>();
        internal readonly List<ShaderGraphNode> uniformNodes = new List<ShaderGraphNode>();
        internal readonly List<ShaderGraphBufferPass> buffers = new List<ShaderGraphBufferPass>();
        internal HashSet<string> uniformNames;
        internal readonly Dictionary<string, string> inputNames = new();
        internal readonly Dictionary<string, string> diagnosticNodes = new();
        internal readonly List<ShaderGraphMessage> messages = new();
    }

    internal enum ShaderGraphMessageLevel { Info, Warning, Error }
    internal readonly struct ShaderGraphMessage
    {
        internal readonly string nodeId, text;
        internal readonly ShaderGraphMessageLevel level;
        internal ShaderGraphMessage(string nodeId, string text, ShaderGraphMessageLevel level)
        { this.nodeId = nodeId; this.text = text; this.level = level; }
    }
    internal sealed class ShaderGraphException : InvalidOperationException
    {
        internal readonly string nodeId;
        internal readonly IReadOnlyList<ShaderGraphMessage> messages;
        internal readonly IReadOnlyList<ShaderDiagnostic> diagnostics;
        internal ShaderGraphException(string nodeId, string message, IReadOnlyList<ShaderGraphMessage> messages = null,
            IReadOnlyList<ShaderDiagnostic> diagnostics = null) : base(message)
        {
            this.nodeId = nodeId; this.diagnostics = diagnostics;
            this.messages = messages ?? new[] { new ShaderGraphMessage(nodeId, message, ShaderGraphMessageLevel.Error) };
        }
    }

    internal sealed partial class ShaderGraphCompiler
    {
        // A compiler-issued snapshot, never inferred from a source comment or the Graph flag.
        // Only the reusable helper block is eligible; all semantic checks still run.
        internal sealed class GeneratedSource
        {
            readonly string source;
            readonly int helperStart, helperLength;
            internal GeneratedSource(string source, int helperStart, int helperLength)
            { this.source = source; this.helperStart = helperStart; this.helperLength = helperLength; }
            internal ShaderSourceDocument CreateDocument(string code, string expanded)
                => ReferenceEquals(code, source)
                    ? new ShaderSourceDocument(expanded, ShaderSourceDocument.GeneratedFragment.Get(source.Substring(helperStart, helperLength)))
                    : new ShaderSourceDocument(expanded);
        }

        readonly ShaderGraph graph;
        // Sixteen emissions per maximum expanded node (4096), twice the expanded
        // edge limit (32768). Scope-local caches must not reset this work budget.
        internal const int MaximumNodeEmissions = 65536;
        // Match ShaderSourceBuilder.BuildGraphProgram's graph-specific character limit,
        // not MaximumCharacters (the smaller limit for ordinary authored Code).
        internal const int MaximumGeneratedCharacters = ShaderGraphSources.MaximumArchiveBytes + 1024 * 1024;
        sealed class GenerationBudget
        {
            readonly Func<bool> isCancelled;
            readonly Dictionary<string, string> origins;
            int emissions;
            internal GenerationBudget(Func<bool> isCancelled, Dictionary<string, string> origins)
            { this.isCancelled = isCancelled; this.origins = origins; }
            internal void CheckCancellation()
            {
                if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            }
            internal void Emit(ShaderGraphNode node)
            {
                if (++emissions <= MaximumNodeEmissions) return;
                Fail(node.id,
                    "Shader graph generation exceeds " + MaximumNodeEmissions +
                    " node emissions across loop, sampled and buffer scopes. Reduce repeated nested branches.");
            }
            internal void CheckSource(ShaderGraphNode node, long characters)
            {
                CheckCancellation();
                if (characters > MaximumGeneratedCharacters)
                    Fail(node.id, "Generated Shader graph source exceeds " + MaximumGeneratedCharacters +
                        " characters. Reduce repeated nested branches.");
            }
            void Fail(string origin, string message)
            {
                if (origins != null && origins.TryGetValue(origin, out string mapped)) origin = mapped;
                throw new ShaderGraphException(origin, message);
            }
        }
        readonly GenerationBudget generationBudget;
        long suspendedBodyCharacters;
        void CheckGeneratedSize(ShaderGraphNode node, long additionalCharacters = 0)
        {
            // Count suspended sampled bodies until they move into samplingCode. Authoring
            // parameter comments are removed from the executable and do not consume its cap.
            generationBudget.CheckSource(node, suspendedBodyCharacters + body.Length +
                samplingCode.Length + functionCode.Length + additionalCharacters);
        }
        bool simplifiedPreviewLighting;
        bool separateLighting;
        readonly HashSet<string> globalLightingNodes = new();
        readonly ShaderGraphCompilation result = new ShaderGraphCompilation();
        readonly Dictionary<string, ShaderGraphNode> nodes = new Dictionary<string, ShaderGraphNode>();
        readonly Dictionary<string, ShaderGraphEdge> incoming = new Dictionary<string, ShaderGraphEdge>();
        Dictionary<string, Value> values = new Dictionary<string, Value>();
        readonly HashSet<string> visiting = new HashSet<string>();
        readonly HashSet<string> declarations = new HashSet<string>();
        readonly HashSet<ShaderGraphNode> uniformNodes = new HashSet<ShaderGraphNode>();
        readonly StringBuilder inputDeclarations = new StringBuilder();
        readonly List<ShaderParameter> inputParameters = new List<ShaderParameter>();
        StringBuilder body = new StringBuilder();
        readonly HashSet<string> usedParameters = new HashSet<string>();
        readonly struct Value
        {
            internal readonly ShaderGraphValueType type;
            internal readonly string text;
            internal readonly string arguments;
            internal Value(ShaderGraphValueType type, string text, string arguments = null) { this.type = type; this.text = text; this.arguments = arguments; }
        }
        ShaderGraphCompiler(ShaderGraph graph) { this.graph = graph; }
        ShaderGraphCompiler(ShaderGraphCompiler validated, GenerationBudget generationBudget = null)
        { graph = validated.graph; nodes = validated.nodes; incoming = validated.incoming; bufferPrograms = validated.bufferPrograms;
            this.generationBudget = generationBudget ?? validated.generationBudget;
            simplifiedPreviewLighting = validated.simplifiedPreviewLighting; separateLighting = validated.separateLighting; }
        internal static void Validate(ShaderGraph graph, bool effectContext = false) => Compile(graph, effectContext: effectContext);
        internal static void ValidateStorage(ShaderGraph graph, bool effectContext = false)
            => new ShaderGraphCompiler(graph).ValidateStructure(effectContext: effectContext);
        internal static ShaderGraphCompilation Compile(ShaderGraph graph, string previewNode = null, string previewPort = "Out", bool effectContext = false)
            => Prepare(graph, effectContext).Compile(previewNode, previewPort);

        // Validation, subgraph expansion and adjacency are shared by all outputs of one snapshot.
        internal sealed partial class Prepared
        {
            readonly ShaderGraphCompiler validated;
            readonly ShaderGraphExpansion.Result expansion;
            internal Prepared(ShaderGraphCompiler validated, ShaderGraphExpansion.Result expansion)
            { this.validated = validated; this.expansion = expansion; }
            internal ShaderGraph Graph => validated.graph;
            internal void ResolvePreview(ref string node, ref string port)
            {
                if (node != null && expansion != null && expansion.rootOutputs.TryGetValue(node + ":" + port, out var endpoint))
                { node = endpoint.node; port = endpoint.port; }
            }
            internal ShaderGraphCompilation Compile(string previewNode = null, string previewPort = "Out", bool simplifiedLighting = false)
                => CompilePolicy(previewNode, previewPort, simplifiedLighting, true);
            internal ShaderGraphCompilation CompileCancellable(string previewNode, string previewPort, bool simplifiedLighting, Func<bool> isCancelled)
                => CompilePolicy(previewNode, previewPort, simplifiedLighting, true, isCancelled);
            // Kept as the deterministic reference and device fallback; no mutable global policy.
            internal ShaderGraphCompilation CompileInlineLighting()
                => CompilePolicy(null, "Out", false, false);
            ShaderGraphCompiler CreateCompiler(bool simplifiedLighting = false, bool splitLighting = false, Func<bool> isCancelled = null)
            {
                // Prepared is reusable. Only producer copies inherit this invocation's budget.
                var budget = new GenerationBudget(isCancelled, expansion?.rootNodes);
                budget.CheckCancellation();
                var compiler = new ShaderGraphCompiler(validated, budget) { simplifiedPreviewLighting = simplifiedLighting,
                    separateLighting = splitLighting && (ShaderPreparationEnvironment.Current?.supportsLightingTargets ?? (SystemInfo.supportedRenderTargetCount >= 4 && (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0)) };
                if (compiler.separateLighting || validated.graph.nodes.Exists(n => ShaderGraphNodes.IsBuffered(n.kind)))
                    compiler.bufferPrograms = new BufferPrograms();
                return compiler;
            }
            ShaderGraphCompilation CompilePolicy(string previewNode, string previewPort, bool simplifiedLighting, bool splitLighting, Func<bool> isCancelled = null)
            {
                // A preview policy must never affect the actual graph output. Buffer producers
                // inherit the policy from their owning node preview through the compiler copy.
                bool nodePreview = previewNode != null;
                ResolvePreview(ref previewNode, ref previewPort);
                var compiler = CreateCompiler(nodePreview && simplifiedLighting, splitLighting, isCancelled);
                var result = compiler.Generate(previewNode, previewPort);
                result.simplifiedLighting = compiler.simplifiedPreviewLighting;
                if (expansion?.rootNodes != null)
                {
                    void MapMessages(ShaderGraphCompilation part)
                    {
                        for (int i = 0; i < part.messages.Count; i++)
                        {
                            var message = part.messages[i];
                            if (expansion.rootNodes.TryGetValue(message.nodeId, out string origin))
                                part.messages[i] = new ShaderGraphMessage(origin, message.text, message.level);
                        }
                    }
                    MapMessages(result);
                    foreach (var pass in result.buffers) MapMessages(pass.program);
                }
                return result;
            }
        }
        internal static Prepared Prepare(ShaderGraph graph, bool effectContext = false)
        {
            var compiler = new ShaderGraphCompiler(graph);
            compiler.ValidateStructure(effectContext: effectContext);
            ShaderGraphExpansion.Result expansion = null;
            if (graph.subGraphs.Count != 0 || graph.activeSubGraph != null)
            {
                expansion = ShaderGraphExpansion.Expand(graph, trackOrigins: true);
                graph = expansion.graph;
                compiler = new ShaderGraphCompiler(graph);
                compiler.ValidateStructure(false, true, effectContext);
            }
            return new Prepared(compiler, expansion);
        }
        ShaderGraphCompilation Generate(string previewNode, string previewPort, ShaderGraphNode bufferNode = null, bool height = false, ShaderGraphNode lightingNode = null, bool causticsColor = false)
        {
            var compiler = this;
            var output = graph.nodes.Find(n => n.kind == ShaderGraphNodeKind.Output);
            var sourceNode = bufferNode ?? lightingNode ?? (previewNode == null ? output : nodes[previewNode]);
            compiler.result.isBufferProgram = bufferNode != null;
            compiler.previewLoopInputs = bufferNode == null && previewNode != null;
            compiler.bufferEvaluation = bufferNode != null;
            // Buffer evaluation starts outside iterative scopes at local UV, with seed zero.
            compiler.sampledEvaluation = bufferNode != null && bufferNode.kind != ShaderGraphNodeKind.Buffer;
            compiler.result.lightingProducer = lightingNode != null;
            Value color = lightingNode != null ? compiler.LightingInputs(lightingNode) : bufferNode != null ? compiler.BufferValue(bufferNode, height, causticsColor) :
                previewNode == null ? compiler.Input(output, "RGBA") : compiler.Emit(previewNode, previewPort);
            compiler.result.bufferOutputType = color.type;
            if (ShaderGraphNodes.Components(color.type) == 0) throw new ShaderGraphException(previewNode, "Resource outputs cannot be displayed as an image.");
            string rgba = color.type switch
            {
                ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => color.text,
                ShaderGraphValueType.Float3 => "float4(" + color.text + ", 1)",
                ShaderGraphValueType.Float2 => "float4(" + color.text + ", 0, 1)",
                _ => "float4((" + color.text + ").xxx, 1)"
            };
            if (bufferNode == null && previewNode == null && lightingNode == null)
            {
                Value alphaScale = compiler.Input(output, "AlphaScale");
                compiler.body.Append("float4 graphOutput = ").Append(rgba).AppendLine(";");
                compiler.body.Append("graphOutput.a *= ").Append(alphaScale.text).AppendLine(";");
                rgba = "graphOutput";
            }
            if (bufferNode != null) rgba = BufferRGBA(color);
            compiler.body.Append("return ").Append(rgba).AppendLine(";");
            if (bufferNode == null && lightingNode == null) compiler.MergeBufferBindings();
            if (globalLightingNodes.Count > 1)
                foreach (string id in globalLightingNodes)
                    result.messages.Add(new ShaderGraphMessage(id,
                        "Global lighting is evaluated more than once in this pass. Where suitable, mix surface inputs with a mask before one Normal Lighting node. Mixing already-lit results still evaluates both; mixing inputs can change the result.",
                        ShaderGraphMessageLevel.Info));
            compiler.CheckGeneratedSize(sourceNode);
            string statements = compiler.body.ToString();
            string helpers = compiler.BuiltInSource(statements, out bool blend);
            var source = new StringBuilder("// @whimtex-effect Shader Graph\n");
            foreach (var parameter in compiler.result.parameters)
                AppendParameter(source, parameter);
            int inputStart = source.Length;
            source.Append(compiler.inputDeclarations).AppendLine()
                .AppendLine("static int _GraphNoiseSeed = 0;")
                .AppendLine("static float2 _GraphScreenUV; float4 _GraphLightingViewport;")
                .Append(compiler.result.lightingProducer ? "static float4 GraphLighting0, GraphLighting1, GraphLighting2, GraphLighting3;\n" : "")
                .Append(compiler.bufferPrograms != null ? "float4 _GraphBakeSize;\n" : "")
                .Append(compiler.causticsColorPass ? "int _GraphCausticsBand;\n" : "")
                .Append(compiler.causticsColorPass || compiler.usesCausticsGamut ? "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCausticsSpectrum.hlsl\"\n" : "")
                .AppendLine("#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ProceduralUv.cginc\"")
                .Append(compiler.result.usesCanvasPosition ? CanvasCoordinatesSource : "")
                .Append(blend ? "static float _HdrBlend = 1.0;\n#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ColorBlend.cginc\"\n" : "")
                .Append(compiler.usesSampleFootprint ? "static float2 _GraphSampleDx, _GraphSampleDy;\n" : "");
            if (compiler.usesScatter) source.AppendLine(ScatterSource);
            int helperStart = source.Length;
            int executableStart = inputStart + compiler.inputDeclarations.Length;
            string noiseTemplates = compiler.NoiseTemplateSource();
            compiler.generationBudget.CheckSource(sourceNode, (long)source.Length - executableStart +
                noiseTemplates.Length + helpers.Length + compiler.functionCode.Length + compiler.samplingCode.Length + statements.Length);
            source.Append(noiseTemplates).Append(helpers);
            int helperLength = source.Length - helperStart;
            source.Append(compiler.functionCode);
            compiler.AppendGraphEntry(compiler.samplingCode, statements);
            compiler.generationBudget.CheckSource(sourceNode, (long)source.Length - executableStart + compiler.samplingCode.Length);
            compiler.AppendIndentedGeneratedCode(source, compiler.samplingCode.ToString(), sourceNode, executableStart);
            compiler.result.source = source.ToString();
            // Authoring directives belong to the displayed/exported source. Native declarations
            // come from shaderParameters, so control labels, ranges and defaults do not change HLSL.
            source.Remove(0, inputStart + compiler.inputDeclarations.Length);
            compiler.result.executableSource = source.ToString();
            compiler.result.parameterCount = compiler.result.parameters.Count + compiler.declarations.Count;
            compiler.result.shaderParameters.AddRange(compiler.result.parameters);
            compiler.result.shaderParameters.AddRange(compiler.inputParameters);
            compiler.result.uniformNames = compiler.declarations;
            compiler.result.bufferExternalCode |= compiler.functionCode.Length != 0;
            // Keep every Expression/Custom Function graph on the ordinary path, including
            // disconnected nodes. Structure, functions and subgraphs were validated above.
            bool compilerOwned = !compiler.result.bufferExternalCode;
            foreach (var node in graph.nodes)
                if (node.kind is ShaderGraphNodeKind.Expression or ShaderGraphNodeKind.CustomFunction)
                { compilerOwned = false; break; }
            if (compilerOwned && helperLength >= 256)
                compiler.result.generatedSource = new GeneratedSource(compiler.result.executableSource,
                    helperStart - inputStart - compiler.inputDeclarations.Length, helperLength);
            return compiler.result;
        }

        static void AppendParameter(StringBuilder source, ShaderParameter parameter, bool hidden = false)
        {
            var controls = parameter.controls.Count == 0
                ? new List<ShaderParameterControl> { ShaderGraphParameters.DefaultControl(parameter) } : parameter.controls;
            foreach (var control in controls)
            {
                bool original = control.hidden; control.hidden |= hidden;
                try { source.AppendLine(ShaderParameterDeclarations.Control(parameter, control, includeDefault: false)); }
                finally { control.hidden = original; }
            }
        }

        void ValidateStructure(bool includeDefinitions = true, bool expanded = false, bool effectContext = false)
        {
            if (graph == null || graph.version != 1 || graph.nodes == null || graph.edges == null || graph.parameters == null)
                throw new FormatException("Unsupported or incomplete Shader graph.");
            if (graph.nodes.Count > (expanded ? 4096 : 512) || graph.edges.Count > (expanded ? 32768 : 4096) || graph.parameters.Count > 256)
                throw new FormatException("Shader graph exceeds the supported limits (512 nodes, 4096 edges, 256 parameters).");
            if (includeDefinitions || expanded) ValidateFunctions();
            if (includeDefinitions) ValidateSubGraphs(effectContext);
            var ids = new HashSet<string>();
            foreach (var p in graph.parameters)
            {
                if (p == null || !Identifier(p.id) || !ids.Add(p.id)) throw new FormatException("Invalid or duplicate Shader parameter ID.");
                ShaderGraphNodes.ParameterType(p);
                if (p.type == ShaderParameterType.Texture2D)
                {
                    if (!Enum.IsDefined(typeof(ShaderTextureSource), p.textureSource))
                        throw new FormatException("Unknown Shader graph texture source.");
                    if (!effectContext && p.textureSource == ShaderTextureSource.Self)
                        throw new FormatException("A Shader layer cannot sample its own output. Choose a texture or another layer.");
                }
                for (int i = 0; i < p.Components; i++)
                    ShaderNumericValue.Check(p.ValueType, p.GetNumber(i));
            }
            int outputs = 0;
            foreach (var n in graph.nodes)
            {
                if (n == null || !Identifier(n.id) || !nodes.TryAdd(n.id, n))
                    throw new FormatException("Invalid or duplicate Shader node.");
                if (!Enum.IsDefined(typeof(ShaderGraphNodeKind), n.kind))
                    throw new FormatException("Unsupported Shader node kind: " + (int)n.kind + ". Removed node types must be recreated.");
                if (n.kind == (graph.activeSubGraph == null ? ShaderGraphNodeKind.Output : ShaderGraphNodeKind.SubGraphOutput)) outputs++;
                if (n.kind == ShaderGraphNodeKind.SubGraph && n.subGraph == null) Error(n, "Sub Graph definition is missing.");
                if (n.kind == ShaderGraphNodeKind.SubGraphInput && n.interfacePort == null) Error(n, "Sub Graph input declaration is missing.");
                if (!expanded && (graph.activeSubGraph == null && ShaderGraphNodes.IsSubGraphInterface(n.kind) || graph.activeSubGraph != null && n.kind == ShaderGraphNodeKind.Output)) Error(n, "Output or interface node belongs to a different graph scope.");
                if (n.kind == ShaderGraphNodeKind.CustomFunction && n.function == null) Error(n, "Function definition is missing.");
                if (n.inputs == null || !ProjectiveMatrix.Finite(n.position.x) || !ProjectiveMatrix.Finite(n.position.y))
                    Error(n, "Invalid node data.");
                if (!ProjectiveMatrix.Finite(n.codeSize.x) || !ProjectiveMatrix.Finite(n.codeSize.y) || n.codeSize.x < 0 || n.codeSize.y < 0)
                    Error(n, "Invalid function editor size.");
                if (!Enum.IsDefined(typeof(ShaderGraphValueType), n.valueType) || !Enum.IsDefined(typeof(ShaderGraphWrap), n.wrap) || !Enum.IsDefined(typeof(ShaderGraphFilter), n.filter)) Error(n, "Unknown node option.");
                if (n.kind == ShaderGraphNodeKind.Parameter && !string.IsNullOrEmpty(n.parameterId) && graph.FindParameter(n.parameterId) == null) Error(n, "Parameter is missing.");
                if (n.kind == ShaderGraphNodeKind.Constant && !Enum.IsDefined(typeof(ShaderGraphConstant), n.constant)) Error(n, "Unknown mathematical constant.");
                if (n.kind == ShaderGraphNodeKind.Reroute && n.inputs.Count != 0) Error(n, "Reroute takes its value from its connection.");
                if (n.kind == ShaderGraphNodeKind.Combine && (n.valueType < ShaderGraphValueType.Float2 || n.valueType > ShaderGraphValueType.Float4)) Error(n, "Combine requires float2, float3 or float4.");
                if (n.kind == ShaderGraphNodeKind.Expression && (n.valueType < ShaderGraphValueType.Float || n.valueType > ShaderGraphValueType.Float4 || n.expression == null || n.expression.Length > ShaderGraphExpression.MaximumLength))
                    Error(n, "Expression requires Float–Float4 and at most 4096 characters.");
                var modes = ShaderGraphNodes.ModeNames(n.kind);
                if (modes != null && (n.option < 0 || n.option >= modes.Length) || n.kind == ShaderGraphNodeKind.Compare && (n.option < 0 || n.option > 5)) Error(n, "Unknown node mode.");
                if (!Enum.IsDefined(typeof(ShaderGraphFractal), n.fractal) ||
                    ShaderGraphNodes.IsNoise(n.kind) && !ShaderGraphNodes.IsWarpNoise(n.kind) && n.fractal > ShaderGraphFractal.PingPong ||
                    ShaderGraphNodes.IsWarpNoise(n.kind) && n.fractal is not (ShaderGraphFractal.None or ShaderGraphFractal.Progressive or ShaderGraphFractal.Independent)) Error(n, "Unknown fractal mode.");
                if (n.kind == ShaderGraphNodeKind.Blend && !Enum.IsDefined(typeof(BlendMode), n.option)) Error(n, "Unknown blend mode.");
                ValidateColorOptions(n);
                ValidateNoiseOptions(n);
                if (ShaderGraphNodes.IsBuffered(n.kind) && (!Enum.IsDefined(typeof(ShaderGraphBufferResolution), n.bufferResolution) ||
                    n.bufferSize.x < 1 || n.bufferSize.y < 1)) Error(n, "Buffer resolution must be positive.");
                if (n.kind == ShaderGraphNodeKind.Fractal && Array.IndexOf(ShaderGraphNodes.FractalModeValues, n.fractal) < 0)
                    Error(n, "Choose a supported Fractal mode.");
                var inputNames = new HashSet<string>();
                foreach (var input in n.inputs)
                {
                    if (input == null || !inputNames.Add(input.port ?? "") || !Finite(input.value) || !Enum.IsDefined(typeof(ShaderGraphValueType), input.type) || !HasInput(n, input.port)) Error(n, n.kind == ShaderGraphNodeKind.CausticsNoise && input != null && !HasInput(n, input.port)
                        ? "Unsupported Caustics Noise input. The former buffered node is no longer supported; recreate it as procedural Caustics Noise or use Caustics for height projection."
                        : "Invalid or duplicate input value.");
                    else if (ShaderGraphInput.IsResource(input.type))
                    {
                        foreach (var port in ShaderGraphNodes.StoredInputs(n))
                            if (port.name == input.port && port.type != input.type) Error(n, "Resource input type does not match its port.");
                        if (input.resource != null && (ShaderGraphNodes.ParameterType(input.resource) != input.type ||
                            input.type == ShaderGraphValueType.Texture && input.resource.textureSource != ShaderTextureSource.Texture))
                            Error(n, "Inline resources must match their port type; textures use an assigned texture asset.");
                    }
                    else if (input.resource != null) Error(n, "Numeric inputs cannot contain a resource.");
                    if (!Enum.IsDefined(typeof(ShaderGraphInputSource), input.source)) Error(n, "Unknown input source.");
                    if (input.source != ShaderGraphInputSource.Value)
                        foreach (var port in ShaderGraphNodes.StoredInputs(n))
                            if (port.name == input.port && (!port.uvDefault || port.type != input.type))
                                Error(n, "UV input source requires a UV-enabled port with a matching type.");
                }
            }
            if (outputs != 1) throw new FormatException(graph.activeSubGraph == null
                ? "A Shader graph requires exactly one Output node." : "A Sub Graph definition requires exactly one SubGraphOutput node.");
            var connectedInputs = new HashSet<string>();
            foreach (var edge in graph.edges)
            {
                if (edge == null || !nodes.TryGetValue(edge.outputNode ?? "", out var from) || !nodes.TryGetValue(edge.inputNode ?? "", out var to) ||
                    Array.IndexOf(ShaderGraphNodes.StoredOutputs(from), edge.outputPort) < 0 || !HasInput(to, edge.inputPort)) throw new FormatException("Shader graph connection has a missing node or port.");
                string key = edge.inputNode + ":" + edge.inputPort;
                if (!connectedInputs.Add(key)) Error(to, "An input can have only one connection.");
                if (ShaderGraphNodes.InputVisible(to, edge.inputPort) && ShaderGraphNodes.OutputVisible(from, edge.outputPort))
                    incoming.Add(key, edge);
            }
            ValidateLoops();
            var complete = new HashSet<string>();
            void Visit(ShaderGraphNode node)
            {
                if (complete.Contains(node.id)) return;
                if (!visiting.Add(node.id)) Error(node, "Cyclic connections are not allowed.");
                foreach (string dependency in ShaderGraphConnections.Dependencies(node, incoming)) Visit(nodes[dependency]);
                visiting.Remove(node.id); complete.Add(node.id);
            }
            foreach (var node in graph.nodes) Visit(node);
            ValidateAnnotations();
            var connections = new ShaderGraphConnections();
            connections.Prepare(nodes, graph);
            connections.RemoveUnboundInputs(incoming);
        }
        void ValidateAnnotations()
        {
            if (graph.notes == null || graph.groups == null || graph.notes.Count > 512 || graph.groups.Count > 512)
                throw new FormatException("Invalid Shader graph notes or groups (maximum 512 each).");
            var elements = new HashSet<string>(nodes.Keys);
            foreach (var note in graph.notes)
            {
                if (note == null || !Identifier(note.id) || !elements.Add(note.id) || note.title == null || note.contents == null ||
                    !ProjectiveMatrix.Finite(note.position.x) || !ProjectiveMatrix.Finite(note.position.y) ||
                    !ProjectiveMatrix.Finite(note.size.x) || !ProjectiveMatrix.Finite(note.size.y) || note.size.x <= 0 || note.size.y <= 0 ||
                    note.theme < 0 || note.theme > 1 || note.fontSize < 0 || note.fontSize > 3 ||
                    !ProjectiveMatrix.Finite(note.hueShift) || note.hueShift < -180 || note.hueShift > 180)
                    throw new FormatException("Invalid Shader graph note.");
            }
            var ids = new HashSet<string>(elements); var grouped = new HashSet<string>();
            foreach (var group in graph.groups)
            {
                if (group == null || !Identifier(group.id) || !ids.Add(group.id) || group.title == null || group.elements == null ||
                    !ProjectiveMatrix.Finite(group.position.x) || !ProjectiveMatrix.Finite(group.position.y))
                    throw new FormatException("Invalid Shader graph group.");
                foreach (var id in group.elements)
                    if (!elements.Contains(id ?? "") || !grouped.Add(id)) throw new FormatException("A node or note may belong to only one group; nested groups are not supported.");
            }
        }
        static bool Identifier(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_')) return false;
            return true;
        }
        static bool Finite(Vector4 v) => ProjectiveMatrix.Finite(v.x) && ProjectiveMatrix.Finite(v.y) && ProjectiveMatrix.Finite(v.z) && ProjectiveMatrix.Finite(v.w);
        static bool HasInput(ShaderGraphNode n, string name) { foreach (var p in ShaderGraphNodes.StoredInputs(n)) if (p.name == name) return true; return false; }
        static void Error(ShaderGraphNode n, string message) => throw new ShaderGraphException(n.id, ShaderGraphNodes.Name(n.kind) + ": " + message);
        static string Type(ShaderGraphValueType t) => t == ShaderGraphValueType.Boolean ? "bool" : "float" + (t == ShaderGraphValueType.Float ? "" : ShaderGraphNodes.Components(t).ToString());
        // Logical binding key, not an HLSL identifier. Validated node IDs cannot contain ':'.
        internal static string InputName(ShaderGraphNode n, string port) => "_GraphI" + (n.uniformNodeId ?? n.id) + ":" + (n.uniformPort ?? port);
        static string Swizzle(ShaderGraphValueType type) => type switch { ShaderGraphValueType.Float2 => ".xy", ShaderGraphValueType.Float3 => ".xyz", ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => "", _ => ".x" };
        Value Uniform(string name, ShaderGraphValueType type, Vector4 value)
        {
            if (declarations.Add(name))
            {
                inputParameters.Add(new ShaderParameter { name = name, id = name, type = ShaderParameterType.Vector, vectorValue = value });
                inputDeclarations.Append("// @param hidden float4 ").Append(name).Append(" = (");
                for (int i = 0; i < 4; i++)
                {
                    if (i > 0) inputDeclarations.Append(", ");
                    inputDeclarations.Append(value[i].ToString("R", CultureInfo.InvariantCulture));
                }
                inputDeclarations.AppendLine(")");
            }
            return new Value(type, type == ShaderGraphValueType.Boolean ? "(" + name + ".x != 0)" : name + Swizzle(type));
        }
        Value Cast(Value value, ShaderGraphValueType type, ShaderGraphNode node)
        {
            if (value.type == type) return value;
            if (ShaderGraphNodes.IsNumeric(value.type) && ShaderGraphNodes.IsNumeric(type))
            {
                return OptimizeNumericCast(value, type);
            }
            Error(node, "Cannot connect " + value.type + " to " + type + ".");
            return default;
        }
        Value Input(ShaderGraphNode node, string name)
        {
            foreach (var port in ShaderGraphNodes.Inputs(node))
            {
                if (port.name != name) continue;
                Value value;
                if (incoming.TryGetValue(node.id + ":" + name, out var edge))
                { TraceInput(edge); value = Emit(edge.outputNode, edge.outputPort); }
                else if (port.canvasSizeDefault && node.FindInput(name) == null) value = new Value(ShaderGraphValueType.Float2, "_CanvasSize.xy");
                else if (port.uvDefault && (node.FindInput(name) == null || node.FindInput(name).source != ShaderGraphInputSource.Value))
                {
                    if (port.type == ShaderGraphValueType.Float3)
                    {
                        var literal = Uniform(InputSourceName(node, name), ShaderGraphValueType.Float4, node.FindInput(name)?.value ?? port.value);
                        value = new Value(ShaderGraphValueType.Float3, "float3(graphUV, " + literal.text + ".z)");
                        if (uniformNodes.Add(node)) result.uniformNodes.Add(node);
                    }
                    else
                        value = port.type == ShaderGraphValueType.Float
                            ? new Value(ShaderGraphValueType.Float, node.FindInput(name)?.source == ShaderGraphInputSource.UVY ? "graphUV.y" : "graphUV.x")
                            : new Value(ShaderGraphValueType.Float2, "graphUV");
                }
                else
                {
                    var type = node.FindInput(name)?.type ?? port.type ?? ShaderGraphValueType.Float;
                    if (node.kind == ShaderGraphNodeKind.Expression) type = node.valueType;
                    if (node.kind == ShaderGraphNodeKind.Swizzle) type = ShaderGraphNodes.SwizzleInputType(node.swizzle, type);
                    if (ShaderGraphInput.IsResource(type))
                    {
                        string uniform = InputSourceName(node, name);
                        if (declarations.Add(uniform))
                        {
                            var resource = node.FindInput(name)?.resource?.CopyDeclaration() ?? ShaderGraphNodes.DefaultResource(node, name, type);
                            resource.name = uniform; resource.id = uniform.TrimStart('_');
                            inputParameters.Add(resource);
                            AppendParameter(inputDeclarations, resource, hidden: true);
                        }
                        value = new Value(type, uniform);
                    }
                    else value = Uniform(InputSourceName(node, name), type, node.FindInput(name)?.value ?? port.value);
                    if (uniformNodes.Add(node)) result.uniformNodes.Add(node);
                }
                return port.type.HasValue ? Cast(value, port.type.Value, node) : value;
            }
            Error(node, "Unknown input " + name); return default;
        }
        ShaderGraphValueType Common(ShaderGraphNode node, params Value[] inputs)
        {
            var type = ShaderGraphValueType.Float;
            foreach (var input in inputs)
            {
                if (!ShaderGraphNodes.IsNumeric(input.type)) Error(node, "Numeric input required.");
                var numeric = ShaderGraphNodes.NumericType(input.type);
                if (numeric > type) type = numeric;
            }
            return type;
        }
        Value Emit(string id, string port)
        {
            CheckGeneratedSize(nodes[id]);
            if (activeTrace != null) TraceNode(nodes[id]);
            string key = id + ":" + port;
            if (values.TryGetValue(key, out var cached)) return cached;
            var n = nodes[id];
            generationBudget.Emit(n);
            Value V(string name) => Input(n, name);
            // All channel outputs reuse the cached full-color sample.
            Value SampleChannel() => new Value(ShaderGraphValueType.Float, Emit(id, "Out").text + "." + port.ToLowerInvariant());
            Value resultValue;
            Value Op(string function, string a = "A", string b = "B", bool infix = false)
            {
                var x = V(a); var y = V(b); var type = Common(n, x, y);
                return new Value(type, infix ? "(" + Cast(x, type, n).text + function + Cast(y, type, n).text + ")" : function + "(" + Cast(x, type, n).text + "," + Cast(y, type, n).text + ")");
            }
            BeginOptimizedEmission(n);
            switch (n.kind)
            {
                case ShaderGraphNodeKind.Scatter: resultValue = EmitScatter(n); break;
                case ShaderGraphNodeKind.ScatterInstance: resultValue = EmitScatterInstance(n, port); break;
                case ShaderGraphNodeKind.ForBegin: resultValue = EmitForBegin(n, port); break;
                case ShaderGraphNodeKind.ForEnd: resultValue = EmitForEnd(n); break;
                case ShaderGraphNodeKind.Buffer: resultValue = EmitBuffer(n); break;
                case ShaderGraphNodeKind.Caustics: resultValue = EmitCaustics(n, port); break;
                case ShaderGraphNodeKind.CausticsNoise: resultValue = EmitCausticsNoise(n, port); break;
                case ShaderGraphNodeKind.Fractal: resultValue = EmitFractal(n); break;
                case ShaderGraphNodeKind.CustomFunction:
                    var functionValue = CompleteOpaqueEmission(EmitFunction(n, port));
                    CheckGeneratedSize(n); return functionValue;
                case ShaderGraphNodeKind.SubGraphInput: resultValue = Input(n, "In"); break;
                case ShaderGraphNodeKind.Reroute:
                    if (!incoming.TryGetValue(n.id + ":In", out var source))
                    {
                        resultValue = new Value(n.valueType, Type(n.valueType) + "(0)");
                        break;
                    }
                    TraceInput(source);
                    resultValue = Emit(source.outputNode, source.outputPort);
                    values.Add(key, resultValue); result.outputTypes[key] = resultValue.type;
                    return resultValue;
                case ShaderGraphNodeKind.ApplyTransform2D:
                    var transform = V("Transform");
                    resultValue = new Value(ShaderGraphValueType.Float2, transform.text + (port == "Local" ? "_ToLocal(" : "_ToInput(") + V("UV").text + ")"); break;
                case ShaderGraphNodeKind.Parameter:
                    var p = graph.FindParameter(n.parameterId);
                    if (p == null) Error(n, "Select a parameter.");
                    string name = ParameterSourceName(p);
                    if (usedParameters.Add(p.id)) { var declaration = p.CopyDeclaration(); if (declaration.controls.Count == 0) declaration.controls.Add(ShaderGraphParameters.DefaultControl(p)); foreach (var c in declaration.controls) if (string.IsNullOrWhiteSpace(c.label)) c.label = p.name; declaration.name = name; declaration.shaderId = name.TrimStart('_'); result.parameters.Add(declaration); }
                    resultValue = new Value(ShaderGraphNodes.ParameterType(p), name); break;
                case ShaderGraphNodeKind.Constant:
                    resultValue = new Value(ShaderGraphValueType.Float, n.constant switch
                    {
                        ShaderGraphConstant.PI => "3.14159265358979323846",
                        ShaderGraphConstant.TAU => "6.28318530717958647693",
                        ShaderGraphConstant.PHI => "1.61803398874989484820",
                        ShaderGraphConstant.E => "2.71828182845904523536",
                        ShaderGraphConstant.SQRT2 => "1.41421356237309504880",
                        _ => throw new ShaderGraphException(n.id, "Unknown mathematical constant.")
                    }); break;
                case ShaderGraphNodeKind.Swizzle: resultValue = EmitSwizzle(n); break;
                case ShaderGraphNodeKind.Expression: resultValue = EmitExpression(n); break;
                case ShaderGraphNodeKind.InstanceIndex:
                    result.contextDependencies |= ShaderContextDependencies.InstanceIndex;
                    resultValue = new Value(ShaderGraphValueType.Float, $"({V("Start").text} + (float)_WhimTexInstanceIndex * {V("Scale").text})"); break;
                case ShaderGraphNodeKind.Time:
                    result.contextDependencies |= ShaderContextDependencies.Time;
                    resultValue = new Value(ShaderGraphValueType.Float, $"({V("Start").text} + _WhimTexTime * {V("Scale").text})"); break;
                case ShaderGraphNodeKind.UV: resultValue = new Value(ShaderGraphValueType.Float2, "graphUV"); break;
                case ShaderGraphNodeKind.CanvasPosition:
                    result.usesCanvasPosition = true;
                    resultValue = new Value(ShaderGraphValueType.Float2, "GraphCanvasPosition(graphUV)" + (port == "AspectX" ? " * float2(_CanvasSize.x * _CanvasSize.w, 1)" :
                        port == "AspectY" ? " * float2(1, _CanvasSize.y * _CanvasSize.z)" : "")); break;
                case ShaderGraphNodeKind.CanvasData:
                    resultValue = port == "PixelScale" ? new Value(ShaderGraphValueType.Float, "_PixelScale") :
                        new Value(ShaderGraphValueType.Float2, "_CanvasSize" + (port == "Size" ? ".xy" : ".zw")); break;
                case ShaderGraphNodeKind.TextureSize: resultValue = new Value(ShaderGraphValueType.Float2, V("Texture").text + "_TexelSize" + (port == "Size" ? ".zw" : ".xy")); break;
                case ShaderGraphNodeKind.SampleTexture:
                    resultValue = port == "Out" ? new Value(ShaderGraphValueType.Color, $"GraphSample({V("Texture").text}, {V("UV").text}, {(int)n.wrap}, {(int)n.filter})") : SampleChannel(); break;
                case ShaderGraphNodeKind.SampleInput: resultValue = new Value(ShaderGraphValueType.Color, $"GraphSample(_MainTex, {V("UV").text}, {(int)n.wrap}, {(int)n.filter})"); break;
                case ShaderGraphNodeKind.SampleGradient:
                    resultValue = port == "Out" ? new Value(ShaderGraphValueType.Color, V("Gradient").text + "_Sample(" + V("T").text + ")") : SampleChannel(); break;
                case ShaderGraphNodeKind.SampleCurve: resultValue = new Value(ShaderGraphValueType.Float, V("Curve").text + "_Sample(" + V("T").text + ")"); break;
                case ShaderGraphNodeKind.Split: resultValue = EmitOptimizedSplit(n, port); break;
                case ShaderGraphNodeKind.Combine: resultValue = EmitOptimizedCombine(n); break;
                case ShaderGraphNodeKind.Add: resultValue = Op("+", infix: true); break;
                case ShaderGraphNodeKind.Subtract: resultValue = Op("-", infix: true); break;
                case ShaderGraphNodeKind.Multiply: resultValue = Op("*", infix: true); break;
                case ShaderGraphNodeKind.Divide: resultValue = Op("/", infix: true); break;
                case ShaderGraphNodeKind.Min: resultValue = Op("min"); break;
                case ShaderGraphNodeKind.Max: resultValue = Op("max"); break;
                case ShaderGraphNodeKind.Power: resultValue = Op("pow"); break;
                case ShaderGraphNodeKind.Step: resultValue = Op("step", "Edge", "In"); break;
                case ShaderGraphNodeKind.Distance: case ShaderGraphNodeKind.Dot:
                    var pair = Op(n.kind == ShaderGraphNodeKind.Distance ? "distance" : "dot"); resultValue = new Value(ShaderGraphValueType.Float, pair.text); break;
                case ShaderGraphNodeKind.Abs: case ShaderGraphNodeKind.OneMinus: case ShaderGraphNodeKind.Negate:
                case ShaderGraphNodeKind.SquareRoot: case ShaderGraphNodeKind.Floor: case ShaderGraphNodeKind.Ceil:
                case ShaderGraphNodeKind.Round: case ShaderGraphNodeKind.Fraction: case ShaderGraphNodeKind.Sine:
                case ShaderGraphNodeKind.Cosine: case ShaderGraphNodeKind.Length: case ShaderGraphNodeKind.Normalize:
                    var unary = V("In"); Common(n, unary);
                    string fn = n.kind switch { ShaderGraphNodeKind.OneMinus => "1-", ShaderGraphNodeKind.Negate => "-", ShaderGraphNodeKind.SquareRoot => "sqrt", ShaderGraphNodeKind.Fraction => "frac", ShaderGraphNodeKind.Sine => "sin", ShaderGraphNodeKind.Cosine => "cos", _ => n.kind.ToString().ToLowerInvariant() };
                    resultValue = new Value(n.kind == ShaderGraphNodeKind.Length ? ShaderGraphValueType.Float : ShaderGraphNodes.NumericType(unary.type),
                        n.kind == ShaderGraphNodeKind.OneMinus || n.kind == ShaderGraphNodeKind.Negate ? "(" + fn + unary.text + ")" : fn + "(" + unary.text + ")"); break;
                case ShaderGraphNodeKind.Clamp: case ShaderGraphNodeKind.Smoothstep:
                    var v = V("In"); var lo = V("Min"); var hi = V("Max"); var width = Common(n, v, lo, hi);
                    string vs = Cast(v, width, n).text, ls = Cast(lo, width, n).text, hs = Cast(hi, width, n).text;
                    resultValue = new Value(width, n.kind == ShaderGraphNodeKind.Clamp ? $"clamp({vs},{ls},{hs})" : $"smoothstep({ls},{hs},{vs})"); break;
                case ShaderGraphNodeKind.Lerp: case ShaderGraphNodeKind.Select:
                    bool select = n.kind == ShaderGraphNodeKind.Select;
                    var a = V(select ? "True" : "A"); var b = V(select ? "False" : "B"); var common = Common(n, a, b);
                    string aa = Cast(a, common, n).text, bb = Cast(b, common, n).text;
                    resultValue = new Value(common, select ? $"({V("Condition").text} ? {aa} : {bb})" : $"lerp({aa},{bb},{V("T").text})"); break;
                case ShaderGraphNodeKind.Unlerp:
                    var unlerpA = V("A"); var unlerpB = V("B"); var unlerpT = V("T"); var unlerpType = Common(n, unlerpA, unlerpB, unlerpT);
                    string ua = Cast(unlerpA, unlerpType, n).text, ub = Cast(unlerpB, unlerpType, n).text, ut = Cast(unlerpT, unlerpType, n).text;
                    resultValue = new Value(unlerpType, $"(({ut} - {ua}) / ({ub} - {ua}))"); break;
                case ShaderGraphNodeKind.Remap:
                    var remap = V("In"); Common(n, remap);
                    resultValue = new Value(ShaderGraphNodes.NumericType(remap.type), $"({V("OutMin").text} + ({remap.text} - {V("InMin").text}) / ({V("InMax").text} - {V("InMin").text}) * ({V("OutMax").text} - {V("OutMin").text}))"); break;
                case ShaderGraphNodeKind.RangeMask:
                    var rangeInput = V("In"); var rangeType = Common(n, rangeInput);
                    resultValue = Cast(new Value(ShaderGraphValueType.Float4,
                        $"GraphRangeMask({Cast(rangeInput, ShaderGraphValueType.Float4, n).text},{V("Center").text},{V("Width").text},{V("Feather").text},{n.option})"), rangeType, n); break;
                case ShaderGraphNodeKind.Compare:
                    string compare = new[] { "<", "<=", ">", ">=", "==", "!=" }[n.option];
                    resultValue = new Value(ShaderGraphValueType.Boolean, $"({V("A").text} {compare} {V("B").text})"); break;
                case ShaderGraphNodeKind.TransformUV: resultValue = new Value(ShaderGraphValueType.Float2, $"GraphTransform({V("UV").text},{V("Offset").text},{V("Scale").text},{V("Rotation").text},{V("Center").text})"); break;
                case ShaderGraphNodeKind.Shape:
                case ShaderGraphNodeKind.Random:
                case ShaderGraphNodeKind.Checkerboard:
                case ShaderGraphNodeKind.Stripes:
                case ShaderGraphNodeKind.Grid:
                case ShaderGraphNodeKind.Rings:
                case ShaderGraphNodeKind.GradientFactor:
                case ShaderGraphNodeKind.CombineDistance:
                case ShaderGraphNodeKind.PolarCoordinates:
                case ShaderGraphNodeKind.PixelateUV:
                case ShaderGraphNodeKind.Repeat:
                case ShaderGraphNodeKind.Mirror:
                case ShaderGraphNodeKind.Kaleidoscope:
                case ShaderGraphNodeKind.VectorDisplacement:
                case ShaderGraphNodeKind.Twirl:
                case ShaderGraphNodeKind.BulgePinch:
                case ShaderGraphNodeKind.Wave:
                case ShaderGraphNodeKind.Ripple: resultValue = EmitProcedural(n, port); break;
                case ShaderGraphNodeKind.Blend: resultValue = new Value(ShaderGraphValueType.Color, $"GraphBlend({V("Background").text},{V("Foreground").text},{V("Opacity").text},{n.option},{(n.hdr ? 1 : 0)})"); break;
                default:
                    if (ShaderGraphNodes.IsNoise(n.kind)) { resultValue = EmitNoise(n, port); break; }
                    if (ShaderGraphNodes.SurfaceCategory(n.kind) != null) { resultValue = EmitSurface(n, port); break; }
                    if (ShaderGraphNodes.IsExtended(n.kind)) { resultValue = EmitExtended(n); break; }
                    if (ShaderGraphNodes.IsFilter(n.kind)) { resultValue = EmitFilter(n); break; }
                    if (ShaderGraphNodes.TryColorOutputType(n.kind, out _)) { resultValue = EmitColor(n); break; }
                    Error(n, "This node has no value output."); return default;
            }
            resultValue = MaterializeOptimizedValue(n, port, resultValue);
            CheckGeneratedSize(n);
            values.Add(key, resultValue); result.outputTypes[key] = resultValue.type;
            return resultValue;
        }

        Value EmitSwizzle(ShaderGraphNode node) => EmitOptimizedSwizzle(node);

        internal static ulong CodeFingerprint(ShaderGraph graph)
        {
            ulong hash = 14695981039346656037UL;
            void Add(string value) { if (value != null) foreach (char c in value) { hash ^= c; hash *= 1099511628211UL; } hash ^= 255; hash *= 1099511628211UL; }
            void Number(int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }
            if (graph == null) return 0;
            Number(graph.version);
            if (graph.parameters != null) foreach (var p in graph.parameters) { if (p == null) { Number(-1); continue; } Add(p.id); Number((int)p.type); Add(p.ValueType); Add(p.name); }
            void AddNodes(List<ShaderGraphNode> nodeList)
            {
            if (nodeList != null) foreach (var n in nodeList)
            {
                if (n == null) { Number(-1); continue; }
                Add(n.id); Number((int)n.kind); Number((int)n.valueType);
                if (n.kind is ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.SubGraphInput) Add(n.parameterId);
                if (n.kind == ShaderGraphNodeKind.CustomFunction) Add(n.functionId);
                if (n.kind == ShaderGraphNodeKind.SubGraph) Add(n.subGraphId);
                if (ShaderGraphNodes.ModeNames(n.kind) != null || n.kind is ShaderGraphNodeKind.Blend or ShaderGraphNodeKind.Compare) Number(n.option);
                if (n.kind == ShaderGraphNodeKind.Blend) Number(n.hdr ? 1 : 0);
                // Keep enum validation and buffer runtime descriptors in this stamp as well
                // as HLSL: it decides whether the owner's working snapshot is refreshed.
                Number((int)n.wrap); Number((int)n.filter);
                if (ShaderGraphNodes.HasNoisePeriod(n.kind)) Number(n.noisePeriodic ? 1 : 0);
                if (n.kind == ShaderGraphNodeKind.Scatter) Number(n.scatterPeriodic ? 1 : 0);
                if (n.kind == ShaderGraphNodeKind.CausticsNoise) Number(n.noiseFiltering ? 1 : 0);
                if (ShaderGraphNodes.HasCausticsSpectrum(n.kind))
                {
                    Number(n.causticsWhiteBalance ? 1 : 0);
                    Number(n.FindInput("Spectrum")?.resource == null ? 0 : 1);
                }
                if (ShaderGraphNodes.IsNoise(n.kind))
                {
                    Number((int)n.fractal); Number((int)n.noiseDimensions); Number((int)n.noiseDistribution);
                    Number((int)n.noiseMetric); Number((int)n.noiseOutput);
                    if (ShaderGraphNodes.IsGrainNoise(n.kind)) Number(n.noiseInverted ? 1 : 0);
                    else Number(n.noiseFiltering ? 1 : 0);
                }
                if (ShaderGraphNodes.IsBuffered(n.kind)) { Number((int)n.bufferResolution); Number(n.bufferSize.x); Number(n.bufferSize.y); }
                if (n.kind == ShaderGraphNodeKind.Fractal)
                {
                    Number((int)n.fractal); Number(n.noiseFiltering ? 1 : 0);
                    // A literal zero changes whether an upstream buffer is admissible.
                    Number((n.FindInput("SeedStep")?.value.x ?? 1) == 0 ? 0 : 1);
                }
                if (n.kind == ShaderGraphNodeKind.Constant) Number((int)n.constant);
                if (n.kind == ShaderGraphNodeKind.Swizzle) Add(n.swizzle);
                if (n.kind == ShaderGraphNodeKind.Expression) Add(n.expression);
                if (ShaderGraphLoops.IsEndpoint(n.kind)) Add(n.loopPartnerId);
                if (n.kind == ShaderGraphNodeKind.ConvertColor) { Number((int)n.inputColorSpace); Number((int)n.outputColorSpace); }
                if (ShaderGraphNodes.UsesChannel(n.kind)) Number((int)n.channel);
                if (ShaderGraphNodes.UsesHeightTexture(n.kind)) { Number((int)n.derivative); Number((int)n.heightSource); }
                if (ShaderGraphNodes.UsesNormalEncoding(n.kind)) Number((int)n.normalEncoding);
                if (n.inputs != null) foreach (var p in n.inputs)
                {
                    if (p == null) { Number(-1); continue; }
                    Add(p.port);
                    // Only plain, valid numeric literals can discard their inactive type.
                    // UV sources/resources still carry storage constraints even when hidden;
                    // active types remain significant for casts, Swizzle and dynamic outputs.
                    // Definition-backed layouts may not be bound yet at owner stamp time.
                    bool inactiveLiteral = n.kind is not (ShaderGraphNodeKind.CustomFunction or ShaderGraphNodeKind.SubGraph) &&
                        !ShaderGraphNodes.IsSubGraphInterface(n.kind) && !ShaderGraphParameters.EvaluatesInput(n, p.port) &&
                        p.source == ShaderGraphInputSource.Value && p.resource == null &&
                        ((uint)p.type <= (uint)ShaderGraphValueType.Boolean || p.type == ShaderGraphValueType.Color);
                    Number(inactiveLiteral ? -2 : (int)p.type);
                    if (!inactiveLiteral) Number((int)p.source);
                    Number(Finite(p.value) ? 1 : 0);
                }
            }
            }
            // Retain stored topology: hidden edges still validate endpoints/duplicates;
            // visible inactive edges also participate in cycle and buffer checks.
            void AddEdges(List<ShaderGraphEdge> edgeList)
            { if (edgeList != null) foreach (var e in edgeList) { if (e == null) { Number(-1); continue; } Add(e.outputNode); Add(e.outputPort); Add(e.inputNode); Add(e.inputPort); } }
            AddNodes(graph.nodes); AddEdges(graph.edges);
            if (graph.subGraphs != null) foreach (var definition in graph.subGraphs)
            {
                if (definition == null) { Number(-1); continue; }
                Add(definition.id); AddNodes(definition.nodes); AddEdges(definition.edges);
                void Pins(List<ShaderGraphInterfacePort> pins)
                { if (pins != null) foreach (var pin in pins) { if (pin == null) { Number(-1); continue; } Add(pin.id); Number((int)pin.type); Number(pin.uvDefault ? 1 : 0); } }
                Pins(definition.inputs); Pins(definition.outputs);
            }
            if (graph.functions != null) foreach (var f in graph.functions)
            {
                if (f == null) { Number(-1); continue; }
                Add(f.id); Number(f.inline ? 1 : 0); Number(f.fullSource ? 1 : 0); Add(f.sourceId); if (f.inline) hash = RenderFingerprint.Mix(hash, f.BodyFingerprint); Add(f.entryPoint); Add(f.returnType); Add(f.returnName); Number((int)f.returnValueType);
                if (f.ports != null) foreach (var p in f.ports) { if (p == null) { Number(-1); continue; } Add(p.name); Number((int)p.type); Number((int)p.direction); Number(p.uvDefault ? 1 : 0); }
            }
            if (graph.sources != null) foreach (var source in graph.sources)
            {
                if (source == null) { Number(-1); continue; }
                Add(source.id); hash = RenderFingerprint.Mix(hash, source.CodeFingerprint);
                if (source.includes != null) foreach (var include in source.includes)
                { if (include == null) { Number(-1); continue; } Add(include.requested); Add(include.sourceId); }
            }
            return hash;
        }

        internal const string CanvasCoordinatesSource = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/GraphCoordinates.cginc\"\n";

        static string Helpers => ShaderGraphSharedSource.Read("Basic") + @"
float4 GraphFetch(WhimTexTexture tex, float2 pixel, int wrap)
{
    float2 size = max(tex.texelSize.zw,1);
    if (wrap == 3 && (any(pixel < 0) || any(pixel >= size))) return 0;
    if (wrap == 1) pixel -= floor(pixel/size)*size;
    else if (wrap == 2) { pixel -= floor(pixel/(size*2))*(size*2); pixel = min(pixel,size*2-1-pixel); }
    else pixel = clamp(pixel,0,size-1);
    return SampleTextureLod(tex,(pixel+0.5)/size,0);
}
float4 GraphSample(WhimTexTexture tex, float2 uv, int wrap, int filter)
{
    float2 p = uv*max(tex.texelSize.zw,1)-0.5;
    if (filter == 0) return GraphFetch(tex,floor(p+0.5),wrap);
    float2 i = floor(p), f = frac(p);
    return lerp(lerp(GraphFetch(tex,i,wrap),GraphFetch(tex,i+float2(1,0),wrap),f.x),
        lerp(GraphFetch(tex,i+float2(0,1),wrap),GraphFetch(tex,i+1,wrap),f.x),f.y);
}
" + ShaderGraphSharedSource.Read("Blend");
    }
}
