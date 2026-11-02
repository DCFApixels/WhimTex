using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphProgram : IDisposable
    {
        readonly Shader shader;
        readonly ShaderGraphCompilation compilation;
        readonly ShaderParameterBindings bindings = new ShaderParameterBindings();
        readonly List<Uniform> uniforms = new List<Uniform>();
        readonly ShaderGraphBindingState bindingState = new ShaderGraphBindingState();
        readonly ulong fingerprint;
        internal Shader Shader => shader;
        internal ShaderGraphCompilation Compilation => compilation;
        internal ulong Fingerprint => fingerprint;
        internal string ShaderSource { get; private set; }
        internal ShaderFXLocalityReason[] LocalityReasons { get; private set; }
        internal bool OwnsShader = true;
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
        internal static ShaderGraphProgram Create(ShaderGraph graph, string previewNode = null, string previewPort = "Out", bool effectContext = false)
        {
            using var request = new CompilationRequest(graph, previewNode, previewPort, synchronous: true, effectContext: effectContext);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Graph shader compilation did not finish.");
            return result;
        }
        internal Texture EmptyInput => bindings.Transparent;
        internal static ShaderGraphProgram CreateFX(ShaderProgram program, string sourcePath)
        {
            var graph = program.mode == ShaderLayerMode.Graph ? program.graph : null;
            var compilation = graph != null ? ShaderGraphCompiler.Compile(graph, effectContext: true) : new ShaderGraphCompilation { source = program.code };
            if (graph == null)
            {
                foreach (var parameter in program.codeParameters) compilation.parameters.Add(parameter.Copy());
                compilation.shaderParameters.AddRange(compilation.parameters);
            }
            using var request = new CompilationRequest(graph, compilation, graph == null ? 0 : ShaderGraphCompiler.CodeFingerprint(graph), sourcePath, true, true, validateCompilation: false);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Shader compilation did not finish.");
            return result;
        }
        internal static ShaderGraphProgram Attach(Shader shader, ShaderGraphCompilation compilation, ulong fingerprint)
            => new ShaderGraphProgram(shader, new Material(shader) { hideFlags = HideFlags.HideAndDontSave }, compilation, fingerprint) { OwnsShader = false };
        internal static ShaderGraphProgram CreateCode(string source, List<ShaderFXParameter> parameters, string sourcePath, bool effectContext = false)
        {
            var compilation = new ShaderGraphCompilation { source = source };
            foreach (var parameter in parameters) compilation.parameters.Add(parameter.Copy());
            compilation.shaderParameters.AddRange(compilation.parameters);
            using var request = new CompilationRequest(null, compilation, 0, sourcePath, true, effectContext);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Shader compilation did not finish.");
            return result;
        }
        internal static void ValidateInactiveCode(ShaderProgram program, string sourcePath, bool effectContext)
        {
            if (program.mode != ShaderLayerMode.Graph || string.IsNullOrEmpty(program.compiledCode)) return;
            // A preset may retain an invalid draft, but must not replace a working executable
            // with unverified code merely because another mode is currently selected.
            var parameters = ShaderFXMetadata.Parse(program.compiledCode, false, out _);
            ShaderFXMetadata.PreserveValues(parameters, program.codeParameters);
            using var compiled = CreateCode(program.compiledCode, parameters, sourcePath, effectContext);
        }
        // Explicitly precompile the pass: Blit itself would force synchronous compilation.
        internal sealed class CompilationRequest : IDisposable
        {
            internal static int ActiveCount { get; private set; }
            readonly ShaderGraph graph;
            readonly ShaderGraphCompilation compilation;
            readonly ulong fingerprint;
            readonly bool validateCompilation;
            Shader shader;
            Material material;
            string shaderSource;
            ShaderFXLocalityReason[] localityReasons;
            bool counted;
            internal CompilationRequest(ShaderGraph graph, string previewNode = null, string previewPort = "Out", bool synchronous = false, bool effectContext = false)
                : this(graph, ShaderGraphCompiler.Compile(graph, previewNode, previewPort, effectContext),
                    ShaderGraphCompiler.CodeFingerprint(graph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", synchronous, effectContext) { }
            internal CompilationRequest(ShaderGraph graph, ShaderGraphCompilation compilation, ulong fingerprint, string sourcePath, bool synchronous, bool effectContext = false, bool validateCompilation = true)
            {
                this.validateCompilation = validateCompilation;
                this.graph = graph; this.fingerprint = fingerprint; this.compilation = compilation;
                try
                {
                    shaderSource = ShaderFXSourceBuilder.BuildProgram(compilation.source, compilation.shaderParameters, sourcePath,
                        graph == null && !effectContext, graph == null ? ShaderFXSourceBuilder.MaximumCharacters : ShaderGraphSources.MaximumArchiveBytes + 1024 * 1024, out localityReasons);
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
                if (validateCompilation && (!shader.isSupported || material.passCount == 0 || errors.Length > 0))
                {
                    string message = errors.Length > 0 ? errors.ToString() : "Graph shader is not supported by this graphics device.";
                    if (errors.Length == 0) messages.Add(new ShaderGraphMessage(null, message, ShaderGraphMessageLevel.Error));
                    throw new ShaderGraphException(errorNode, message, messages);
                }
                result = new ShaderGraphProgram(shader, material, compilation, fingerprint)
                    { Messages = messages, ShaderSource = shaderSource, LocalityReasons = localityReasons };
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
        internal void BindEffect(ShaderGraph graph, in LayerRenderContext context, ShaderGraphBindingState prepared = null, bool branchCurrent = false)
        {
            Bind(graph, context, prepared, branchCurrent);
            Material.SetInt("_GraphCanvasCoordinates", 0);
            Material.SetInt("_UnboundedUv", 0);
            ProjectiveMatrix.Identity.SetShader(Material, "_UvRow");
            ProjectiveMatrix.Identity.SetShader(Material, "_GraphCanvasRow");
            var document = context.activeDocument;
            float width = document.RenderingViewport ? Mathf.Ceil(document.width / context.scaleMultiplier) : context.width;
            float height = document.RenderingViewport ? Mathf.Ceil(document.height / context.scaleMultiplier) : context.height;
            Material.SetVector("_InputSize", new Vector4(width, height, 1f / width, 1f / height));
            var viewport = document.ViewportToCanvas;
            Material.SetVector("_WhimTex_Viewport", document.RenderingViewport
                ? new Vector4((float)viewport.m02, (float)viewport.m12, (float)viewport.m00, (float)viewport.m11) : new Vector4(0, 0, 1, 1));
            if (document.RenderingViewport) ShaderTextureView.ForContext(null, context).BindMetadata(Material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
            else ShaderTextureView.BindFull(Material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
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
            if (OwnsShader && shader != null) UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
