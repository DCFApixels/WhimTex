using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime : IDisposable
    {
        readonly ShaderExecutableCache.Entry executable;
        Shader shader => executable.shader;
        readonly int sourceRevision = ShaderProgramSourceRevision.Current;
        bool disposed;
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
        string compilationPath;
        internal ShaderSourceAnalysis Analysis { get; private set; }
        internal ShaderLocalityReason[] LocalityReasons => Analysis.LocalityReasons;
        internal readonly Material Material;
        internal IReadOnlyList<ShaderGraphMessage> Messages { get; private set; }
        internal IReadOnlyList<ShaderDiagnostic> DiagnosticMessages { get; private set; }
        internal string Source => compilation.source;
        internal bool UsesTime => compilation.usesTime;
        internal bool UsesLighting => compilation.usesLighting;
        internal bool UsesInstanceIndex => compilation.usesInstanceIndex;
        internal IReadOnlyList<ShaderParameter> Parameters => compilation.parameters;
        internal List<ShaderParameter> CodeParameters { get; private set; }
        internal ShaderGraph WorkingGraph { get; private set; }
        ShaderGraphInput[] workingInputs;
        ShaderGraphInterfacePort[] workingPorts;
        ShaderGraphFunctionPort[] workingFunctionPorts;
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
        ShaderProgramRuntime(ShaderExecutableCache.Entry executable, Material material, ShaderGraphCompilation compilation, ulong fingerprint)
        {
            this.executable = executable; executable.Retain();
            Material = material; this.compilation = compilation;
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
        internal static ShaderProgramRuntime CompileDraft(ShaderProgram program, ShaderProgramMode mode, string sourcePath, bool effectContext, ShaderProgramRuntime current = null)
            => CreateProgram(program, mode, sourcePath, effectContext, ShaderProgramVersion.Draft, current);
        internal static ShaderProgramRuntime RestoreWorking(ShaderProgram program, ShaderProgramMode mode, string sourcePath, bool effectContext)
            => CreateProgram(program, mode, sourcePath, effectContext, ShaderProgramVersion.Working);
        private static ShaderProgramRuntime CreateProgram(ShaderProgram program, ShaderProgramMode mode, string sourcePath,
            bool effectContext, ShaderProgramVersion version, ShaderProgramRuntime current = null)
        {
            using var request = BeginProgram(program, mode, sourcePath, effectContext, version, current, synchronous: true);
            if (!request.TryComplete(out var result)) throw new InvalidOperationException("Shader compilation did not finish.");
            return result;
        }
        internal static CompilationRequest BeginProgram(ShaderProgram program, ShaderProgramMode mode, string sourcePath,
            bool effectContext, ShaderProgramVersion version, ShaderProgramRuntime current = null, bool synchronous = false)
        {
            bool working = version == ShaderProgramVersion.Working;
            var graph = mode == ShaderProgramMode.Graph ? (working && program.workingGraph != null ? program.workingGraph : program.draftGraph) : null;
            // An asynchronous request owns a stable snapshot; later edits must not alter its executable or backup.
            if (graph != null && !synchronous) graph = ShaderProgramStorage.CopyWorkingGraph(graph);
            var compilation = graph != null ? ShaderGraphCompiler.Compile(graph, effectContext: effectContext) :
                new ShaderGraphCompilation { source = program.CodeSource(working) };
            List<ShaderParameter> parameters = null;
            if (graph == null)
            {
                parameters = program.PrepareCodeParameters(compilation.source, effectContext, working ? program.WorkingCodeParameters : null);
                foreach (var parameter in parameters) compilation.parameters.Add(parameter.Copy());
                compilation.shaderParameters.AddRange(compilation.parameters);
            }
            ulong fingerprint = graph == null ? 0 : ShaderGraphCompiler.CodeFingerprint(graph);
            bool canReuse = graph != null && current != null && !current.disposed && current.shader != null &&
                current.sourceRevision == ShaderProgramSourceRevision.Current && current.compilationPath == sourcePath;
            string shaderSource;
            ShaderSourceAnalysis analysis;
            if (canReuse && !compilation.bufferExternalCode && !current.compilation.bufferExternalCode &&
                compilation.ExecutableSource == current.compilation.ExecutableSource && SameDeclarations(compilation.shaderParameters, current.compilation.shaderParameters))
            { shaderSource = current.ShaderSource; analysis = current.Analysis; }
            else shaderSource = ShaderExecutableCache.Prepare(compilation, graph != null, sourcePath, effectContext, out analysis);
            return new CompilationRequest(graph, compilation, fingerprint, sourcePath, synchronous, effectContext,
                collectProgramDiagnostics: true, preparedSource: shaderSource, preparedAnalysis: analysis)
                { codeParameters = parameters, retainGraph = graph != null, ownsGraph = graph != null && !synchronous };
        }
        internal void RefreshWorkingGraph(ShaderGraph graph) => RetainGraph(ShaderProgramStorage.CopyWorkingGraph(graph));
        internal void RefreshCodeValues(IReadOnlyList<ShaderParameter> values)
        {
            Retain(compilation.parameters);
            if (CodeParameters != null) Retain(CodeParameters);
            void Retain(IReadOnlyList<ShaderParameter> declarations)
            {
                foreach (var binding in parameterPlan.Prepare(values, declarations))
                    if (!ReferenceEquals(binding.value, binding.declaration)) ShaderMetadata.CopyValue(binding.value, binding.declaration);
            }
        }
        static bool SameDeclarations(IReadOnlyList<ShaderParameter> left, IReadOnlyList<ShaderParameter> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (left[i].id != right[i].id || left[i].name != right[i].name || left[i].type != right[i].type || left[i].valueType != right[i].valueType) return false;
            return true;
        }
        private void RetainGraph(ShaderGraph saved)
        {
            WorkingGraph = saved;
            if (saved == null) return;
            // The accepted draft may have newer values and controls than the queued candidate.
            // Retain those objects so subsequent binds and saved fallback share the same values.
            for (int i = 0; i < compilation.parameters.Count; i++)
            {
                var parameter = compilation.parameters[i];
                var latest = saved.parameters.Find(p => p.id == parameter.id);
                if (latest == null) continue;
                compilation.parameters[i] = latest;
                int declaration = compilation.shaderParameters.IndexOf(parameter);
                if (declaration >= 0) compilation.shaderParameters[declaration] = latest;
            }
            saved.BindFunctions();
            bindingState.Prepare(saved);
            foreach (var resource in resourceInputs)
                if (bindingState.nodes.TryGetValue(resource.node, out var owner) && owner.FindInput(resource.port) is ShaderGraphInput input)
                {
                    ShaderMetadata.CopyValue(input.resource ?? resource.defaultValue, resource.declaration);
                    input.resource = resource.declaration;
                }
            workingInputs = new ShaderGraphInput[uniforms.Count];
            workingPorts = new ShaderGraphInterfacePort[uniforms.Count];
            workingFunctionPorts = new ShaderGraphFunctionPort[uniforms.Count];
            for (int i = 0; i < uniforms.Count; i++)
            {
                var uniform = uniforms[i];
                if (!bindingState.nodes.TryGetValue(uniform.node, out var node)) continue;
                workingInputs[i] = node.FindInput(uniform.port);
                if (workingInputs[i] != null)
                    uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, workingInputs[i].value);
                else
                {
                    if (node.kind == ShaderGraphNodeKind.CustomFunction)
                        workingFunctionPorts[i] = node.function?.ports.Find(p => p.name == uniform.port && p.direction != ShaderGraphFunctionDirection.Output);
                    ShaderGraphSubGraph definition;
                    if (node.kind == ShaderGraphNodeKind.SubGraph) definition = saved.FindSubGraph(node.subGraphId);
                    else bindingState.definitions.TryGetValue(node.id, out definition);
                    workingPorts[i] = definition?.inputs.Find(p => p.id == uniform.port) ?? definition?.outputs.Find(p => p.id == uniform.port);
                    if (workingPorts[i] != null) uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, workingPorts[i].value);
                    else if (workingFunctionPorts[i] != null) uniforms[i] = new Uniform(uniform.property, uniform.node, uniform.port, workingFunctionPorts[i].value);
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
            internal static int ActiveCount => ShaderExecutableCache.ActiveCount;
            internal bool HasStarted => executable?.started == true;
            readonly ShaderGraph graph;
            readonly ShaderGraphCompilation compilation;
            readonly ulong fingerprint;
            readonly bool collectProgramDiagnostics;
            readonly string sourcePath;
            ShaderExecutableCache.Entry executable;
            Shader shader => executable.shader;
            Material material;
            internal List<ShaderParameter> codeParameters;
            internal bool retainGraph;
            internal bool ownsGraph;
            readonly bool effectContext;
            string shaderSource;
            ShaderSourceAnalysis analysis;

            internal CompilationRequest(ShaderGraph graph, string previewNode = null, string previewPort = "Out", bool synchronous = false, bool effectContext = false)
                : this(graph, ShaderGraphCompiler.Compile(graph, previewNode, previewPort, effectContext),
                    ShaderGraphCompiler.CodeFingerprint(graph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", synchronous, effectContext) { }
            internal CompilationRequest(ShaderGraph graph, ShaderGraphCompilation compilation, ulong fingerprint, string sourcePath, bool synchronous, bool effectContext = false, bool collectProgramDiagnostics = false,
                string preparedSource = null, ShaderSourceAnalysis preparedAnalysis = default)
            {
                this.collectProgramDiagnostics = collectProgramDiagnostics;
                this.effectContext = effectContext;
                this.sourcePath = sourcePath;
                this.graph = graph; this.fingerprint = fingerprint; this.compilation = compilation;
                try
                {
                    if (preparedSource != null) { shaderSource = preparedSource; analysis = preparedAnalysis; }
                    else shaderSource = ShaderExecutableCache.Prepare(compilation, graph != null, sourcePath, effectContext, out analysis);
                    compilation.contextDependencies = analysis.ContextDependencies;
                    executable = ShaderExecutableCache.Acquire(shaderSource, synchronous, collectProgramDiagnostics);
                }
                catch { Dispose(); throw; }
            }
            internal bool TryComplete(out ShaderProgramRuntime result)
            {
                result = null;
                if (executable == null || !ShaderExecutableCache.IsReady(executable)) return false;
                material ??= new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
                result = new ShaderProgramRuntime(executable, material, compilation, fingerprint)
                    { Messages = messages.AsReadOnly(), DiagnosticMessages = diagnostics.AsReadOnly(), ShaderSource = shaderSource, compilationPath = sourcePath, Analysis = analysis };
                try
                {
                    result.ThrowCompilerErrors();
                    result.CodeParameters = codeParameters;
                    if (retainGraph)
                    { if (ownsGraph) result.RetainGraph(graph); else result.RefreshWorkingGraph(graph); }
                    else if (graph == null && effectContext) result.parameterPlan = new ShaderParameterBindings.Plan(byId: false);
                }
                catch
                {
                    result.Dispose(); result = null;
                    material = null;
                    throw;
                }
                material = null;
                Dispose();
                return true;
            }
            public void Dispose()
            {
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                material = null;
                executable?.Release(); executable = null;
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
                    else if (workingFunctionPorts?[i] != null) workingFunctionPorts[i].value = value;
                }
            }
            ShaderParameterBindings.BindContext(Material, context, effectContext);
            if (UsesLighting) context.activeDocument.BindLighting(Material);
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
            if (UsesLighting) context.activeDocument.BindLighting(Material);
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
            if (node.kind == ShaderGraphNodeKind.CustomFunction && node.function?.ports != null)
                foreach (var pin in node.function.ports)
                    if (pin.name == port && pin.direction != ShaderGraphFunctionDirection.Output) return pin.value;
            if (definition != null)
            {
                foreach (var pin in definition.inputs) if (pin.id == port) return pin.value;
                foreach (var pin in definition.outputs) if (pin.id == port) return pin.value;
            }
            return fallback;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleaseBuffers();
            bindings.Dispose();
            if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
            executable.Release();
        }
    }
}
