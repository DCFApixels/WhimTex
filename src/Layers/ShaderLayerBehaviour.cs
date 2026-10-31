using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class ShaderLayerBehaviour : LayerBehaviour
    {
        public ShaderGraph graph = ShaderGraph.CreateDefault();
        [NonSerialized] ShaderGraphProgram program;
        [NonSerialized] ulong attemptedFingerprint;
        [NonSerialized] bool attempted;
        [NonSerialized] string diagnostics;
        internal string Diagnostics => diagnostics ?? "Not compiled yet.";
        internal string GeneratedSource => program?.Source ?? "";
        internal bool HasCompileError { get; private set; }
        internal string ErrorNodeId { get; private set; }
        public override TransformTilingMode SourceTiling => TransformTilingMode.Unbounded;
        public override string ToString() => "Shader";
        internal override void InitializeLayer(Layer layer)
        {
            layer.colorRange = LayerColorRange.HDR;
            layer.blendRange = LayerBlendRange.HDR;
        }
        internal void InvalidateGraph() => attempted = false;
        internal bool CompileGraph(WhimTexDocument document)
        {
            ulong fingerprint = ShaderGraphCompiler.CodeFingerprint(graph);
            if (attempted && attemptedFingerprint == fingerprint && program != null && !HasCompileError) return true;
            attemptedFingerprint = fingerprint;
            attempted = true;
            try
            {
                var compiled = ShaderGraphProgram.Create(graph);
                program?.Dispose(); program = compiled;
                HasCompileError = false; ErrorNodeId = null;
                diagnostics = "Compiled successfully.";
                return true;
            }
            catch (Exception error)
            {
                HasCompileError = true;
                ErrorNodeId = (error as ShaderGraphException)?.nodeId;
                diagnostics = error.Message + (program == null ? "" : "\nShowing the last successfully compiled graph.");
                return false;
            }
        }
        internal override RenderTexture Render(in LayerRenderContext context)
        {
            ulong fingerprint = ShaderGraphCompiler.CodeFingerprint(graph);
            if (!attempted || attemptedFingerprint != fingerprint) CompileGraph(context.activeDocument);
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
                program.Bind(graph, context);
                var renderContext = ProceduralUv.Prepare(program.Material, Owner, context);
                using var textures = context.activeDocument.BindShaderTextureLayers(program.Parameters, Owner, context, null);
                textures?.Apply(program.Material);
                GL.sRGBWrite = false;
                Graphics.Blit(null, source, program.Material, 0);
                return ApplyTransformAndFx(source, renderContext);
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                ProfiledRenderTexture.ReleaseTemporary(source);
            }
        }
        internal override void ReleaseTransientResources()
        {
            program?.Dispose(); program = null; attempted = false;
        }
    }

    internal sealed class ShaderGraphProgram : IDisposable
    {
        readonly Shader shader;
        readonly ShaderGraphCompilation compilation;
        readonly ShaderParameterBindings bindings = new ShaderParameterBindings();
        readonly List<Uniform> uniforms = new List<Uniform>();
        readonly Dictionary<string, ShaderGraphNode> liveNodes = new Dictionary<string, ShaderGraphNode>();
        readonly Dictionary<string, ShaderFXParameter> liveParameters = new Dictionary<string, ShaderFXParameter>();
        readonly ulong fingerprint;
        internal readonly Material Material;
        internal string Source => compilation.source;
        internal IReadOnlyList<ShaderFXParameter> Parameters => compilation.parameters;
        readonly struct Uniform
        {
            internal readonly int property;
            internal readonly string node, port;
            internal readonly Vector4 fallback;
            internal Uniform(int property, string node, string port, Vector4 fallback)
            { this.property = property; this.node = node; this.port = port; this.fallback = fallback; }
        }
        ShaderGraphProgram(Shader shader, Material material, ShaderGraphCompilation compilation, ShaderGraph graph)
        {
            this.shader = shader; Material = material; this.compilation = compilation;
            fingerprint = ShaderGraphCompiler.CodeFingerprint(graph);
            foreach (var n in graph.nodes)
            {
                if (n.kind == ShaderGraphNodeKind.Constant)
                    uniforms.Add(new Uniform(Shader.PropertyToID(ShaderGraphCompiler.ConstantName(n)), n.id, null, n.value));
                foreach (var port in ShaderGraphNodes.Inputs(n))
                    if (port.type is not (ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve))
                        uniforms.Add(new Uniform(Shader.PropertyToID(ShaderGraphCompiler.InputName(n, port.name)), n.id, port.name, n.FindInput(port.name)?.value ?? port.value));
            }
        }
        internal static ShaderGraphProgram Create(ShaderGraph graph, string previewNode = null, string previewPort = "Out")
        {
            var compilation = ShaderGraphCompiler.Compile(graph, previewNode, previewPort);
            Shader shader = null; Material material = null;
            try
            {
                shader = ShaderUtil.CreateShaderAsset(compilation.source, true);
                if (shader == null) throw new InvalidOperationException("Unity could not create the graph shader.");
                shader.hideFlags = HideFlags.HideAndDontSave;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                if (material.passCount > 0) ShaderUtil.CompilePass(material, 0, true);
                var errors = new StringBuilder();
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        errors.AppendLine(message.message + " (line " + message.line + ")");
                if (!shader.isSupported || material.passCount == 0 || errors.Length > 0)
                    throw new InvalidOperationException(errors.Length > 0 ? errors.ToString() : "Graph shader is not supported by this graphics device.");
                return new ShaderGraphProgram(shader, material, compilation, graph);
            }
            catch
            {
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
                throw;
            }
        }
        internal void Bind(ShaderGraph graph, in LayerRenderContext context)
        {
            liveNodes.Clear(); liveParameters.Clear();
            if (graph?.nodes != null) foreach (var node in graph.nodes) if (node?.id != null) liveNodes[node.id] = node;
            if (graph?.parameters != null) foreach (var parameter in graph.parameters) if (parameter?.id != null) liveParameters[parameter.id] = parameter;
            var dimensions = new Vector2(context.activeDocument.width, context.activeDocument.height);
            foreach (var declaration in compilation.parameters)
            {
                liveParameters.TryGetValue(declaration.id, out var value);
                if (value == null || value.type != declaration.type || value.ValueType != declaration.ValueType) value = declaration;
                bindings.Apply(Material, value, declaration, dimensions, context.pixelScale, textureViews: true);
                if (!ReferenceEquals(value, declaration))
                {
                    // Keep the last bound values if a later draft removes or changes a declaration.
                    declaration.floatValue = value.floatValue; declaration.vectorValue = value.vectorValue;
                    declaration.colorValue = value.colorValue; declaration.pixelUnits = value.pixelUnits;
                    declaration.gradientValue = value.gradientValue; declaration.curveValue = value.curveValue;
                    declaration.integerValues = value.integerValues;
                    declaration.textureSource = value.textureSource;
                    declaration.textureLayerId = value.textureLayerId;
                    declaration.textureValue = value.textureValue;
                }
            }
            bool current = ShaderGraphCompiler.CodeFingerprint(graph) == fingerprint;
            for (int i = 0; i < uniforms.Count; i++)
            {
                var uniform = uniforms[i];
                ShaderGraphNode node = null;
                if (current) liveNodes.TryGetValue(uniform.node, out node);
                Vector4 value = node == null ? uniform.fallback : uniform.port == null ? node.value : node.FindInput(uniform.port)?.value ?? uniform.fallback;
                Material.SetVector(uniform.property, value);
                if (current) uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, value);
            }
            Material.SetVector("_GraphImageSize", new Vector4(context.width, context.height, 1f / context.width, 1f / context.height));
        }
        public void Dispose()
        {
            bindings.Dispose();
            if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
            if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
