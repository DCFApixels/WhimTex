using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderLayerMode { Graph, Code }
    public enum ShaderLayerInputMode { None, Texture, Layer, CompositeBelow }

    [Serializable]
    public sealed class ShaderLayerBehaviour : LayerBehaviour
    {
        public const string DefaultCode = "// @whimtex-effect Shader\n\nfloat4 ApplyFX(PinnedUV uv, float4 color)\n{\n    return float4(1, 1, 1, 1);\n}\n";
        public ShaderLayerMode mode;
        public ShaderLayerInputMode inputMode;
        public Texture2D inputTexture;
        public string inputLayerId;
        public ShaderGraph graph = ShaderGraph.CreateDefault();
        public string code = DefaultCode;
        public string compiledCode = DefaultCode;
        public List<ShaderFXParameter> codeParameters = new List<ShaderFXParameter>();
        sealed class CompilationState
        {
            internal ShaderGraphProgram program;
            internal ulong fingerprint;
            internal bool attempted, failed, draftFailed;
            internal string diagnostics, errorNodeId;
            internal IReadOnlyList<ShaderGraphMessage> messages = Array.Empty<ShaderGraphMessage>();
        }
        [NonSerialized] CompilationState graphState, codeState;
        [NonSerialized] RenderFingerprint.TextStamp codeFingerprint;
        CompilationState State(ShaderLayerMode selected) => selected == ShaderLayerMode.Code
            ? codeState ??= new CompilationState() : graphState ??= new CompilationState();
        CompilationState CurrentState => State(mode);
        ShaderGraphProgram Program => CurrentState.program;
        internal IReadOnlyList<ShaderFXParameter> ActiveParameters => mode == ShaderLayerMode.Code ? codeParameters : graph?.parameters;
        internal string Diagnostics => CurrentState.diagnostics ?? "Not compiled yet.";
        internal string GeneratedSource => graphState?.program?.Source ?? "";
        public override bool UsesTime => Program?.UsesTime == true;
        public override bool UsesInstanceIndex => Program?.UsesInstanceIndex == true;
        internal bool HasCompileError => CurrentState.failed;
        internal string ErrorNodeId => CurrentState.errorNodeId;
        internal IReadOnlyList<ShaderGraphMessage> Messages => CurrentState.messages;
        internal string CodeDiagnostics => codeState?.diagnostics ?? "Not compiled yet.";
        public override TransformTilingMode SourceTiling => TransformTilingMode.Unbounded;
        public override string ToString() => "Shader";
        internal override void InitializeLayer(Layer layer)
        {
            layer.colorRange = LayerColorRange.HDR;
            layer.blendRange = LayerBlendRange.HDR;
        }
        internal void InvalidateGraph() => CurrentState.attempted = false;
        internal string CodeSourcePath(WhimTexDocument document)
        {
            string path = WhimTexDocumentService.PathOf(document);
            return string.IsNullOrEmpty(path) ? "Assets/Untitled.hlsl" : path;
        }
        ulong Fingerprint(ShaderLayerMode selected)
        {
            if (selected == ShaderLayerMode.Graph) return ShaderGraphCompiler.CodeFingerprint(graph);
            ulong hash = codeFingerprint.Get(compiledCode);
            foreach (var parameter in codeParameters)
                if (parameter != null) hash = RenderFingerprint.Mix(hash, parameter.idFingerprint.Get(parameter.id));
            return hash;
        }
        internal bool CompileGraph(WhimTexDocument document) => Compile(document, false);
        internal bool CompileCode(WhimTexDocument document) => Compile(document, true);
        bool Compile(WhimTexDocument document, bool applyCode)
        {
            var selected = applyCode ? ShaderLayerMode.Code : mode;
            var state = State(selected);
            // A failed draft must not suppress restoration of the saved working code.
            if (!applyCode) { state.fingerprint = Fingerprint(selected); state.attempted = true; }
            try
            {
                ShaderGraphProgram compiled;
                if (selected == ShaderLayerMode.Code)
                {
                    string source = applyCode ? code : compiledCode;
                    var parameters = ShaderFXMetadata.Parse(source, false, out _);
                    ShaderFXMetadata.PreserveValues(parameters, codeParameters);
                    foreach (var parameter in parameters)
                        if (parameter.type == ShaderFXParameterType.Texture2D && parameter.textureSource == ShaderFXTextureSource.Self)
                            throw new InvalidOperationException("Self is not available for Shader layers. Choose Texture, Layer or None.");
                    compiled = ShaderGraphProgram.CreateCode(source, parameters, CodeSourcePath(document));
                    if (applyCode)
                    {
                        WhimTexDocumentJson.PreserveShaderCodeParameterAssets(codeParameters, parameters);
                        compiledCode = source; codeParameters = parameters;
                    }
                }
                else compiled = ShaderGraphProgram.Create(graph);
                state.program?.Dispose(); state.program = compiled;
                state.fingerprint = Fingerprint(selected); state.attempted = true;
                if (applyCode) state.draftFailed = false;
                if (!state.draftFailed)
                {
                    state.failed = false; state.errorNodeId = null;
                    state.messages = compiled.Messages;
                    state.diagnostics = "Compiled successfully.";
                }
                return true;
            }
            catch (Exception error)
            {
                state.failed = true; state.draftFailed = applyCode;
                state.errorNodeId = (error as ShaderGraphException)?.nodeId;
                state.messages = (error as ShaderGraphException)?.messages ?? new[] { new ShaderGraphMessage(null, error.Message, ShaderGraphMessageLevel.Error) };
                state.diagnostics = error.Message + (state.program == null ? "" : "\nShowing the last successfully compiled shader in this mode.");
                return false;
            }
        }
        internal override RenderTexture Render(in LayerRenderContext context)
        {
            var state = CurrentState;
            ulong fingerprint = Fingerprint(mode);
            if (!state.attempted || state.fingerprint != fingerprint) Compile(context.activeDocument, false);
            var program = Program;
            var source = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            source.filterMode = FilterMode.Bilinear; source.wrapMode = TextureWrapMode.Clamp;
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                if (program == null)
                {
                    RenderTexture.active = source; GL.Clear(false, true, Color.clear);
                    return ApplyTransformAndFx(source, context);
                }
                if (mode == ShaderLayerMode.Code) program.BindCode(codeParameters, context);
                else program.Bind(graph, context);
                var renderContext = program.PrepareCoordinates(Owner, context);
                using var input = context.activeDocument.ResolveShaderInput(this, context);
                var inputTexture = input.Bind(program.Material, program.EmptyInput);
                using var textures = context.activeDocument.BindShaderTextureLayers(program.Parameters, Owner, context, null);
                textures?.Apply(program.Material);
                GL.sRGBWrite = false;
                Graphics.Blit(inputTexture, source, program.Material, 0);
                return ApplyTransformAndFx(source, renderContext);
            }
            finally
            {
                program?.Material.SetTexture("_MainTex", null);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                ProfiledRenderTexture.ReleaseTemporary(source);
            }
        }
        internal override void ReleaseTransientResources()
        {
            graphState?.program?.Dispose(); graphState = null;
            codeState?.program?.Dispose(); codeState = null;
        }
    }

    internal sealed partial class ShaderGraphProgram : IDisposable
    {
        readonly Shader shader;
        readonly ShaderGraphCompilation compilation;
        readonly ShaderParameterBindings bindings = new ShaderParameterBindings();
        readonly List<Uniform> uniforms = new List<Uniform>();
        readonly ShaderGraphBindingState bindingState = new ShaderGraphBindingState();
        readonly ulong fingerprint;
        internal readonly Material Material;
        internal IReadOnlyList<ShaderGraphMessage> Messages { get; private set; }
        internal string Source => compilation.source;
        internal bool UsesTime => compilation.usesTime;
        internal bool UsesInstanceIndex => compilation.usesInstanceIndex;
        internal IReadOnlyList<ShaderFXParameter> Parameters => compilation.parameters;
        readonly struct Uniform
        {
            internal readonly int property;
            internal readonly string node, port;
            internal readonly Vector4 fallback;
            internal Uniform(int property, string node, string port, Vector4 fallback)
            { this.property = property; this.node = node; this.port = port; this.fallback = fallback; }
        }
        ShaderGraphProgram(Shader shader, Material material, ShaderGraphCompilation compilation, ulong fingerprint)
        {
            this.shader = shader; Material = material; this.compilation = compilation;
            this.fingerprint = fingerprint;
            var added = new HashSet<int>();
            foreach (var n in compilation.uniformNodes)
            {
                foreach (var port in ShaderGraphNodes.Inputs(n))
                    if (port.type is not (ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve))
                    {
                        string name = ShaderGraphCompiler.InputName(n, port.name);
                        if (!compilation.uniformNames.Contains(name)) continue;
                        int property = Shader.PropertyToID(name);
                        if (added.Add(property)) uniforms.Add(new Uniform(property, n.uniformNodeId ?? n.id, n.uniformPort ?? port.name, n.FindInput(port.name)?.value ?? port.value));
                    }
            }
        }
        internal static ShaderGraphProgram Create(ShaderGraph graph, string previewNode = null, string previewPort = "Out")
        {
            using var request = new CompilationRequest(graph, previewNode, previewPort, synchronous: true);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Graph shader compilation did not finish.");
            return result;
        }
        internal Texture EmptyInput => bindings.Transparent;
        internal static ShaderGraphProgram CreateCode(string source, List<ShaderFXParameter> parameters, string sourcePath)
        {
            var compilation = new ShaderGraphCompilation { source = source };
            foreach (var parameter in parameters) compilation.parameters.Add(parameter.Copy());
            compilation.shaderParameters.AddRange(compilation.parameters);
            using var request = new CompilationRequest(null, compilation, 0, sourcePath, true);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Shader compilation did not finish.");
            return result;
        }
        // Explicitly precompile the pass: Blit itself would force synchronous compilation.
        internal sealed class CompilationRequest : IDisposable
        {
            internal static int ActiveCount { get; private set; }
            readonly ShaderGraph graph;
            readonly ShaderGraphCompilation compilation;
            readonly ulong fingerprint;
            Shader shader;
            Material material;
            bool counted;
            internal CompilationRequest(ShaderGraph graph, string previewNode = null, string previewPort = "Out", bool synchronous = false)
                : this(graph, ShaderGraphCompiler.Compile(graph, previewNode, previewPort),
                    ShaderGraphCompiler.CodeFingerprint(graph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", synchronous) { }
            internal CompilationRequest(ShaderGraph graph, ShaderGraphCompilation compilation, ulong fingerprint, string sourcePath, bool synchronous)
            {
                this.graph = graph; this.fingerprint = fingerprint; this.compilation = compilation;
                try
                {
                    string shaderSource = graph == null
                        ? ShaderFXSourceBuilder.BuildLayer(compilation.source, compilation.shaderParameters, sourcePath)
                        : ShaderFXSourceBuilder.BuildGenerated(compilation.source, compilation.shaderParameters,
                            "Hidden/WhimTex/ShaderGraph", sourcePath, ShaderGraphSources.MaximumArchiveBytes + 1024 * 1024);
                    compilation.contextDependencies = ShaderContextUsage.ReadMarker(shaderSource);
                    shader = ShaderUtil.CreateShaderAsset(shaderSource, false);
                    if (shader == null) throw new InvalidOperationException("Unity could not create the graph shader.");
                    shader.hideFlags = HideFlags.HideAndDontSave;
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    bool previous = ShaderUtil.allowAsyncCompilation;
                    try
                    {
                        ShaderUtil.allowAsyncCompilation = !synchronous;
                        if (material.passCount > 0) ShaderUtil.CompilePass(material, 0, synchronous);
                    }
                    finally { ShaderUtil.allowAsyncCompilation = previous; }
                    ActiveCount++; counted = true;
                }
                catch { Dispose(); throw; }
            }
            internal bool TryComplete(out ShaderGraphProgram result)
            {
                result = null;
                if (shader == null) return false;
                if (material.passCount > 0 && !ShaderUtil.IsPassCompiled(material, 0) && !ShaderUtil.ShaderHasError(shader)) return false;
                var errors = new StringBuilder();
                var messages = new List<ShaderGraphMessage>();
                string errorNode = null;
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                {
                    var level = message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error ? ShaderGraphMessageLevel.Error :
                        message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning ? ShaderGraphMessageLevel.Warning : ShaderGraphMessageLevel.Info;
                    string nodeId = graph?.FindNode(message.file)?.id;
                    string text = message.message + " (line " + message.line + ")";
                    messages.Add(new ShaderGraphMessage(nodeId, text, level));
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    {
                        errors.AppendLine(text);
                        errorNode ??= nodeId;
                    }
                }
                if (!shader.isSupported || material.passCount == 0 || errors.Length > 0)
                {
                    string message = errors.Length > 0 ? errors.ToString() : "Graph shader is not supported by this graphics device.";
                    if (errors.Length == 0) messages.Add(new ShaderGraphMessage(null, message, ShaderGraphMessageLevel.Error));
                    throw new ShaderGraphException(errorNode, message, messages);
                }
                result = new ShaderGraphProgram(shader, material, compilation, fingerprint) { Messages = messages };
                material = null; shader = null;
                Dispose();
                return true;
            }
            public void Dispose()
            {
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
                material = null; shader = null;
                if (counted) { counted = false; ActiveCount--; }
            }
        }
        internal void Bind(ShaderGraph graph, in LayerRenderContext context, ShaderGraphBindingState prepared = null, bool branchCurrent = false)
        {
            if (prepared == null) { bindingState.Prepare(graph); prepared = bindingState; }
            var dimensions = new Vector2(context.activeDocument.width, context.activeDocument.height);
            BindParameters(prepared.parameters, context, dimensions);
            bool current = branchCurrent || prepared.Fingerprint == fingerprint;
            for (int i = 0; i < uniforms.Count; i++)
            {
                var uniform = uniforms[i];
                ShaderGraphNode node = null;
                if (current) prepared.nodes.TryGetValue(uniform.node, out node);
                ShaderGraphSubGraph definition = null;
                if (node?.kind == ShaderGraphNodeKind.SubGraph) definition = graph.FindSubGraph(node.subGraphId);
                else if (node != null) prepared.definitions.TryGetValue(node.id, out definition);
                Vector4 value = UniformValue(node, uniform.port, uniform.fallback, definition);
                Material.SetVector(uniform.property, value);
                if (current) uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, value);
            }
            BindGlobals(context, dimensions);
        }
        internal LayerRenderContext PrepareCoordinates(Layer layer, in LayerRenderContext context)
        {
            if (!compilation.usesCanvasPosition) return ProceduralUv.Prepare(Material, layer, context);
            bool transformed = context.applyTransform;
            Material.SetInt("_GraphCanvasCoordinates", transformed ? 1 : 0);
            Material.SetInt("_GraphTiling", (int)layer.EffectiveTiling);
            Material.SetInt("_UnboundedUv", transformed ? 1 : 0);
            if (transformed) layer.SetRenderInverse(Material, "_UvRow", context);
            var forward = transformed ? layer.RenderTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height) : ProjectiveMatrix.Identity;
            forward.SetShader(Material, "_GraphCanvasRow");
            return ProceduralUv.WithoutTransform(context);
        }
        void BindGlobals(in LayerRenderContext context, Vector2 dimensions)
        {
            Material.SetVector("_InputSize", new Vector4(context.width, context.height, 1f / context.width, 1f / context.height));
            Material.SetVector("_CanvasSize", new Vector4(dimensions.x, dimensions.y, 1f / dimensions.x, 1f / dimensions.y));
            Material.SetFloat("_RenderScale", context.scaleMultiplier);
            Material.SetFloat("_PixelScale", context.pixelScale);
            context.shaderContext.Bind(Material);
        }
        internal void BindCode(IReadOnlyList<ShaderFXParameter> parameters, in LayerRenderContext context)
        {
            var values = bindingState.parameters;
            values.Clear();
            foreach (var parameter in parameters) if (parameter?.id != null) values[parameter.id] = parameter;
            var dimensions = new Vector2(context.activeDocument.width, context.activeDocument.height);
            BindParameters(values, context, dimensions);
            BindGlobals(context, dimensions);
            ShaderTextureView.BindFull(Material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
            // ApplyFX already receives the procedural layer's local coordinates.
            ProjectiveMatrix.Identity.SetShader(Material, "_WhimTex_LayerToLocalRow");
        }
        void BindParameters(Dictionary<string, ShaderFXParameter> values, in LayerRenderContext context, Vector2 dimensions)
        {
            foreach (var declaration in compilation.parameters)
            {
                values.TryGetValue(declaration.id, out var value);
                if (value == null || value.type != declaration.type || value.ValueType != declaration.ValueType) value = declaration;
                bindings.Apply(Material, value, declaration, dimensions, context.pixelScale, textureViews: true);
                if (!ReferenceEquals(value, declaration))
                {
                    // Keep the last bound values if a later draft removes or changes a declaration.
                    declaration.floatValue = value.floatValue; declaration.vectorValue = value.vectorValue;
                    declaration.colorValue = value.colorValue; declaration.pixelUnits = value.pixelUnits;
                    declaration.transformValue = value.transformValue;
                    declaration.gradientValue = value.gradientValue; declaration.curveValue = value.curveValue;
                    declaration.integerValues = value.integerValues;
                    declaration.textureSource = value.textureSource;
                    declaration.textureLayerId = value.textureLayerId;
                    declaration.textureValue = value.textureValue;
                }
            }
        }
        static Vector4 UniformValue(ShaderGraphNode node, string port, Vector4 fallback, ShaderGraphSubGraph definition)
        {
            if (node == null) return fallback;
            var input = node.FindInput(port);
            if (input != null) return input.value;
            if (definition != null)
            {
                foreach (var pin in definition.inputs) if (pin.id == port) return pin.value;
                foreach (var pin in definition.outputs) if (pin.id == port) return pin.value;
            }
            return fallback;
        }
        public void Dispose()
        {
            bindings.Dispose();
            if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
            if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
