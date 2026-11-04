using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime : IDisposable
    {
        readonly Shader shader;
        readonly ShaderGraphCompilation compilation;
        readonly ShaderParameterBindings bindings = new ShaderParameterBindings();
        ShaderParameterBindings.Plan parameterPlan = new ShaderParameterBindings.Plan(byId: true);
        readonly List<Uniform> uniforms = new List<Uniform>();
        readonly List<ResourceInput> resourceInputs = new();
        readonly ShaderParameterBindings.Binding[] resourceBindings;
        readonly ShaderGraphBindingState bindingState = new ShaderGraphBindingState();
        readonly ulong fingerprint;
        internal Shader Shader => shader;
        internal ShaderGraphCompilation Compilation => compilation;
        internal ulong Fingerprint => fingerprint;
        internal string ShaderSource { get; private set; }
        internal ShaderSourceAnalysis Analysis { get; private set; }
        internal ShaderLocalityReason[] LocalityReasons => Analysis.LocalityReasons;
        internal readonly Material Material;
        internal IReadOnlyList<ShaderGraphMessage> Messages { get; private set; }
        internal IReadOnlyList<ShaderDiagnostic> DiagnosticMessages { get; private set; }
        internal string Source => compilation.source;
        internal bool UsesTime => compilation.usesTime;
        internal bool UsesInstanceIndex => compilation.usesInstanceIndex;
        internal IReadOnlyList<ShaderParameter> Parameters => compilation.parameters;
        internal List<ShaderParameter> CodeParameters { get; private set; }
        internal ShaderGraph WorkingGraph { get; private set; }
        ShaderGraphInput[] workingInputs;
        ShaderGraphInterfacePort[] workingPorts;
        readonly struct Uniform
        {
            internal readonly int property;
            internal readonly string node, port;
            internal readonly Vector4 fallback;
            internal Uniform(int property, string node, string port, Vector4 fallback)
            { this.property = property; this.node = node; this.port = port; this.fallback = fallback; }
        }
        readonly struct ResourceInput
        {
            internal readonly string node, port;
            internal readonly ShaderParameter declaration, defaultValue;
            internal ResourceInput(string node, string port, ShaderParameter declaration)
            {
                this.node = node; this.port = port; this.declaration = declaration;
                defaultValue = ShaderGraphInput.DefaultResource(ShaderGraphNodes.ParameterType(declaration));
            }
        }
        ShaderProgramRuntime(Shader shader, Material material, ShaderGraphCompilation compilation, ulong fingerprint)
        {
            this.shader = shader; Material = material; this.compilation = compilation;
            this.fingerprint = fingerprint;
            var added = new HashSet<int>();
            foreach (var n in compilation.uniformNodes)
            {
                foreach (var port in ShaderGraphNodes.Inputs(n))
                    {
                        string name = ShaderGraphCompiler.InputName(n, port.name);
                        if (!compilation.uniformNames.Contains(name)) continue;
                        int property = Shader.PropertyToID(name);
                        if (!added.Add(property)) continue;
                        if (port.type.HasValue && ShaderGraphInput.IsResource(port.type.Value))
                            resourceInputs.Add(new ResourceInput(n.uniformNodeId ?? n.id, n.uniformPort ?? port.name,
                                compilation.shaderParameters.Find(p => p.name == name)));
                        else uniforms.Add(new Uniform(property, n.uniformNodeId ?? n.id, n.uniformPort ?? port.name, n.FindInput(port.name)?.value ?? port.value));
                    }
            }
            resourceBindings = new ShaderParameterBindings.Binding[resourceInputs.Count];
        }
        internal static ShaderProgramRuntime Create(ShaderGraph graph, string previewNode = null, string previewPort = "Out", bool effectContext = false)
        {
            using var request = new CompilationRequest(graph, previewNode, previewPort, synchronous: true, effectContext: effectContext);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Graph shader compilation did not finish.");
            return result;
        }
        internal Texture EmptyInput => bindings.Transparent;
        internal static ShaderProgramRuntime CompileDraft(ShaderProgram program, ShaderProgramMode mode, string sourcePath, bool effectContext)
            => CreateProgram(program, mode, sourcePath, effectContext, ShaderProgramVersion.Draft);
        internal static ShaderProgramRuntime RestoreWorking(ShaderProgram program, ShaderProgramMode mode, string sourcePath, bool effectContext)
            => CreateProgram(program, mode, sourcePath, effectContext, ShaderProgramVersion.Working);
        private static ShaderProgramRuntime CreateProgram(ShaderProgram program, ShaderProgramMode mode, string sourcePath,
            bool effectContext, ShaderProgramVersion version)
        {
            bool working = version == ShaderProgramVersion.Working;
            var graph = mode == ShaderProgramMode.Graph ? (working && program.workingGraph != null ? program.workingGraph : program.draftGraph) : null;
            var compilation = graph != null ? ShaderGraphCompiler.Compile(graph, effectContext: effectContext) :
                new ShaderGraphCompilation { source = program.CodeSource(working) };
            List<ShaderParameter> parameters = null;
            if (graph == null)
            {
                parameters = program.PrepareCodeParameters(compilation.source, effectContext, working ? program.WorkingCodeParameters : null);
                foreach (var parameter in parameters) compilation.parameters.Add(parameter.Copy());
                compilation.shaderParameters.AddRange(compilation.parameters);
            }
            using var request = new CompilationRequest(graph, compilation, graph == null ? 0 : ShaderGraphCompiler.CodeFingerprint(graph),
                sourcePath, true, effectContext, collectProgramDiagnostics: true);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Shader compilation did not finish.");
            try
            {
                result.CodeParameters = parameters;
                if (graph != null) result.RetainGraph(ShaderProgramStorage.CopyWorkingGraph(graph));
                else if (effectContext) result.parameterPlan = new ShaderParameterBindings.Plan(byId: false);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        private void RetainGraph(ShaderGraph saved)
        {
            WorkingGraph = saved;
            if (saved == null) return;
            WhimTexDocumentJson.PreserveShaderCodeParameterAssets(saved.parameters, compilation.parameters);
            foreach (var parameter in compilation.parameters)
            {
                int index = saved.parameters.FindIndex(p => p.id == parameter.id);
                if (index >= 0) saved.parameters[index] = parameter;
            }
            bindingState.Prepare(saved);
            foreach (var resource in resourceInputs)
                if (bindingState.nodes.TryGetValue(resource.node, out var owner) && owner.FindInput(resource.port) is ShaderGraphInput input)
                    input.resource = resource.declaration;
            workingInputs = new ShaderGraphInput[uniforms.Count];
            workingPorts = new ShaderGraphInterfacePort[uniforms.Count];
            for (int i = 0; i < uniforms.Count; i++)
            {
                var uniform = uniforms[i];
                if (!bindingState.nodes.TryGetValue(uniform.node, out var node)) continue;
                workingInputs[i] = node.FindInput(uniform.port);
                if (workingInputs[i] != null)
                    uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, workingInputs[i].value);
                else
                {
                    ShaderGraphSubGraph definition;
                    if (node.kind == ShaderGraphNodeKind.SubGraph) definition = saved.FindSubGraph(node.subGraphId);
                    else bindingState.definitions.TryGetValue(node.id, out definition);
                    workingPorts[i] = definition?.inputs.Find(p => p.id == uniform.port) ?? definition?.outputs.Find(p => p.id == uniform.port);
                    if (workingPorts[i] != null) uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, workingPorts[i].value);
                }
            }
        }
        internal static void ValidateInactiveProgram(ShaderProgram program, string sourcePath, bool effectContext)
        {
            var inactive = program.mode == ShaderProgramMode.Graph ? ShaderProgramMode.Code : ShaderProgramMode.Graph;
            if (!program.HasWorkingProgram(inactive)) return;
            // A preset may retain an invalid draft, but must not replace a working executable
            // with unverified code merely because another mode is currently selected.
            using var compiled = RestoreWorking(program, inactive, sourcePath, effectContext);
        }
        internal void ThrowCompilerErrors()
        {
            if (!ShaderDiagnostics.HasErrors(DiagnosticMessages)) return;
            string node = null;
            foreach (var message in Messages)
                if (message.level == ShaderGraphMessageLevel.Error)
                { node = message.nodeId; break; }
            throw new ShaderGraphException(node, ShaderDiagnostics.Format(DiagnosticMessages), Messages, DiagnosticMessages);
        }
        // Explicitly precompile the pass: Blit itself would force synchronous compilation.
        internal sealed class CompilationRequest : IDisposable
        {
            internal static int ActiveCount { get; private set; }
            readonly ShaderGraph graph;
            readonly ShaderGraphCompilation compilation;
            readonly ulong fingerprint;
            readonly bool collectProgramDiagnostics;
            readonly string sourcePath;
            Shader shader;
            Material material;
            string shaderSource;
            ShaderSourceAnalysis analysis;
            bool counted;
            internal CompilationRequest(ShaderGraph graph, string previewNode = null, string previewPort = "Out", bool synchronous = false, bool effectContext = false)
                : this(graph, ShaderGraphCompiler.Compile(graph, previewNode, previewPort, effectContext),
                    ShaderGraphCompiler.CodeFingerprint(graph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", synchronous, effectContext) { }
            internal CompilationRequest(ShaderGraph graph, ShaderGraphCompilation compilation, ulong fingerprint, string sourcePath, bool synchronous, bool effectContext = false, bool collectProgramDiagnostics = false)
            {
                this.collectProgramDiagnostics = collectProgramDiagnostics;
                this.sourcePath = sourcePath;
                this.graph = graph; this.fingerprint = fingerprint; this.compilation = compilation;
                try
                {
                    shaderSource = ShaderSourceBuilder.BuildProgram(compilation.source, compilation.shaderParameters, sourcePath,
                        graph == null && !effectContext, graph == null ? ShaderSourceBuilder.MaximumCharacters : ShaderGraphSources.MaximumArchiveBytes + 1024 * 1024, out analysis);
                    compilation.contextDependencies = analysis.ContextDependencies;
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
            internal bool TryComplete(out ShaderProgramRuntime result)
            {
                result = null;
                if (shader == null) return false;
                if (material.passCount > 0 && !ShaderUtil.IsPassCompiled(material, 0) && !ShaderUtil.ShaderHasError(shader)) return false;
                var messages = new List<ShaderGraphMessage>();
                var diagnostics = collectProgramDiagnostics
                    ? ShaderDiagnostics.CollectProgram(shader, analysis, compilation.source, compilation.parameters, sourcePath,
                        shader.isSupported && material.passCount > 0)
                    : ShaderDiagnostics.ReadCompiler(shader);
                if (!collectProgramDiagnostics && (!shader.isSupported || material.passCount == 0) && !ShaderDiagnostics.HasErrors(diagnostics))
                    diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "The shader is not supported on this graphics device."));
                foreach (var message in diagnostics)
                {
                    var level = message.Severity == ShaderDiagnosticSeverity.Error ? ShaderGraphMessageLevel.Error :
                        message.Severity == ShaderDiagnosticSeverity.Warning ? ShaderGraphMessageLevel.Warning : ShaderGraphMessageLevel.Info;
                    string nodeId = graph?.FindNode(message.File)?.id;
                    string text = nodeId == null ? message.ToString() : message.Message + (message.Line > 0 ? " (line " + message.Line + ")" : "");
                    messages.Add(new ShaderGraphMessage(nodeId, text, level));
                }
                result = new ShaderProgramRuntime(shader, material, compilation, fingerprint)
                    { Messages = messages.AsReadOnly(), DiagnosticMessages = diagnostics.AsReadOnly(), ShaderSource = shaderSource, Analysis = analysis };
                result.ThrowCompilerErrors();
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
        internal void Bind(ShaderGraph graph, in LayerRenderContext context, ShaderGraphBindingState prepared = null, bool branchCurrent = false, bool effectContext = false)
        {
            if (prepared == null) { bindingState.Prepare(graph); prepared = bindingState; }
            bindings.Apply(Material, parameterPlan.Prepare(prepared.Graph?.parameters, compilation.parameters), context, retainValues: true);
            bool current = branchCurrent || prepared.Fingerprint == fingerprint;
            for (int i = 0; i < resourceInputs.Count; i++)
            {
                var resource = resourceInputs[i];
                ShaderParameter value = null;
                if (current && prepared.nodes.TryGetValue(resource.node, out var owner)) value = owner.FindInput(resource.port)?.resource ?? resource.defaultValue;
                if (value == null || value.type != resource.declaration.type) value = resource.declaration;
                resourceBindings[i] = new ShaderParameterBindings.Binding(value, resource.declaration);
            }
            bindings.Apply(Material, resourceBindings, context, retainValues: true);
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
                if (current)
                {
                    uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, value);
                    if (workingInputs?[i] != null) workingInputs[i].value = value;
                    else if (workingPorts?[i] != null) workingPorts[i].value = value;
                }
            }
            ShaderParameterBindings.BindContext(Material, context, effectContext);
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
            Bind(graph, context, prepared, branchCurrent, effectContext: true);
            Material.SetInt("_GraphCanvasCoordinates", 0);
            Material.SetInt("_UnboundedUv", 0);
            ProjectiveMatrix.Identity.SetShader(Material, "_UvRow");
            ProjectiveMatrix.Identity.SetShader(Material, "_GraphCanvasRow");
        }
        internal ShaderParameterBindings.Binding[] PrepareParameterPlan(IReadOnlyList<ShaderParameter> parameters)
            => parameterPlan.Prepare(parameters, compilation.parameters);
        internal ShaderParameterBindings.Binding[] TextureBindings => parameterPlan.Textures;
        internal void BindCode(IReadOnlyList<ShaderParameter> parameters, in LayerRenderContext context, bool effectContext = false)
        {
            bindings.Apply(Material, parameterPlan.Prepare(parameters, compilation.parameters), context, retainValues: true);
            ShaderParameterBindings.BindContext(Material, context, effectContext);
            if (effectContext) return;
            ShaderTextureView.BindFull(Material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
            // ApplyFX already receives the procedural layer's local coordinates.
            ProjectiveMatrix.Identity.SetShader(Material, "_WhimTex_LayerToLocalRow");
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
            ReleaseBuffers();
            bindings.Dispose();
            if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
            if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
