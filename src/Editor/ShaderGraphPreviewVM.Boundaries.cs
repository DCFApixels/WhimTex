using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        ShaderGraphPreparation.Job hybridPreparation;
        string hybridPreparedSource;
        ShaderSourceAnalysis hybridPreparedAnalysis;
        internal bool IsHybridPending
        {
            get
            {
                if (hybridPreparation != null || hybridRequest != null) return true;
                if (!HasBoundaries) return false;
                if (hybridRuntime == null || !executorReady) return true;
                if (lightingSurfaces != null)
                    foreach (var surface in lightingSurfaces)
                        if (surface != null && !surface.executorReady) return true;
                return false;
            }
        }
        ShaderGraph hybridGraph;
        internal ShaderGraphCompilation hybridCompilation;
        ShaderGraphBufferPass[] hybridInputs;
        ShaderProgramRuntime.CompilationRequest hybridRequest;
        ShaderProgramRuntime hybridRuntime;
        WhimTexDocument hybridDocument;
        Layer hybridLayer;
        bool hybridEffect, hybridMain;
        int hybridLightingVariant = -1;
        float hybridRank;
        Func<bool> hybridWanted;
        internal void SetHybridPriority(float rank, Func<bool> wanted)
        { hybridRank = rank; hybridWanted = wanted; hybridRequest?.SetPreviewPriority(rank); hybridRequest?.SetPreviewDemand(wanted);
            if (executorPriority != null) { executorPriority.previewRank = rank; executorPriority.wanted = wanted; }
            if (lightingSurfaces != null) foreach (var surface in lightingSurfaces) surface?.SetHybridPriority(rank, wanted);
            if (hybridPreparation != null) { hybridPreparation.priority.previewRank = rank; hybridPreparation.priority.wanted = wanted; } }
        internal bool HasBoundaries => hybridInputs != null;

        internal static bool TryCreateHybrid(ShaderGraph graph, string node, string port, bool simplifiedLighting,
            out ShaderGraphPreviewVM vm)
        {
            if (TryCreatePreview(graph, node, port, simplifiedLighting, out vm)) return true;
            try
            {
                var prepared = ShaderGraphCompiler.Prepare(graph);
                var compiled = prepared.Compile(node, port, simplifiedLighting);
                return TryCreateFromCompilation(graph, prepared, node, port, compiled, out vm);
            }
            catch (Exception error) when (error is NotSupportedException || error is ShaderGraphException || error is FormatException)
            { LastFallbackReason = error.Message; vm = null; return false; }
        }

        internal static ShaderGraphPreviewVM BeginHybrid(ShaderGraph graph, string node, string port, bool simplifiedLighting, bool main = false, bool effect = false)
        {
            // Nested graphs share the native compiler's validated expansion on the CPU worker.
            if (!NeedsExpansion(graph))
            {
                if (TryCreatePreview(graph, node, port, simplifiedLighting, out var pure)) { pure.hybridMain = main; return pure; }
                if (effect) return null;
            }
            var job = ShaderGraphPreparation.Enqueue(ShaderGraphPreparation.Capture(graph, effect), node, port,
                "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", new ShaderGraphPreparation.Priority(main), simplifiedLighting);
            job.hybrid = true;
            return new ShaderGraphPreviewVM { hybridPreparation = job, hybridMain = main };
        }

        internal static bool TryCreateFromCompilation(ShaderGraph graph, ShaderGraphCompiler.Prepared prepared, string node, string port,
            ShaderGraphCompilation compiled, out ShaderGraphPreviewVM vm)
        {
            vm = null;
            try
            {
                // The native compiler validates the graph and determines legal boundaries.
                // Unsupported arbitrary HLSL is never implicitly baked into an image.
                var boundaries = new Dictionary<string, ShaderGraphBufferPass>();
                foreach (var pass in compiled.buffers)
                    if (!pass.causticsHeight && !pass.lightingInputs) boundaries[pass.nodeId + (pass.causticsColor ? ":Color" : "")] = pass;
                if (boundaries.Count == 0) return false;
                prepared.ResolvePreview(ref node, ref port);
                var builder = new Builder(prepared.Graph, boundaries, compiled.simplifiedLighting);
                var value = node == null ? builder.Output() : builder.Emit(node, port);
                value = builder.Cast(value, ShaderGraphValueType.Color);
                if (builder.resources.Count == 0) return false;
                var bundle = new ShaderGraphCompilation {
                    source = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ProceduralUv.cginc\"\n" +
                        ShaderGraphCompiler.CanvasCoordinatesSource + "float4 _GraphBakeSize, _GraphLightingViewport;\n" +
                        "float4 ApplyFX(PinnedUV uv, float4 color) { return 0; }",
                    bindingsOnly = true, usesCanvasPosition = compiled.usesCanvasPosition, uniformNames = new HashSet<string>()
                };
                // The carrier binds native producers only. VM-only uniforms must not create
                // new carrier shaders for every downstream preview or numeric edit.
                var parameterIds = new HashSet<string>(); var propertyNames = new HashSet<string>();
                var uniformNodes = new HashSet<ShaderGraphNode>();
                foreach (var pass in compiled.buffers)
                {
                    bundle.buffers.Add(pass);
                    var program = pass.program;
                    bundle.contextDependencies |= program.contextDependencies;
                    bundle.bufferExternalCode |= program.bufferExternalCode;
                    foreach (var parameter in program.parameters)
                        if (parameterIds.Add(parameter.id)) bundle.parameters.Add(parameter);
                    foreach (var parameter in program.shaderParameters)
                        if (propertyNames.Add(parameter.name)) bundle.shaderParameters.Add(parameter);
                    foreach (var uniform in program.uniformNodes)
                        if (uniformNodes.Add(uniform)) bundle.uniformNodes.Add(uniform);
                    if (program.uniformNames != null) bundle.uniformNames.UnionWith(program.uniformNames);
                    foreach (var pair in program.inputNames) bundle.inputNames.TryAdd(pair.Key, pair.Value);
                }
                foreach (var pass in bundle.buffers)
                    if (!pass.lightingInputs && propertyNames.Add(pass.textureName))
                        bundle.shaderParameters.Add(new ShaderParameter { id = pass.textureName, name = pass.textureName, type = ShaderParameterType.Texture2D });
                vm = new ShaderGraphPreviewVM(builder, value.register) {
                    hybridGraph = graph, hybridCompilation = bundle, hybridInputs = builder.resources.ToArray()
                };
                vm.PrepareLightingSurfaces(prepared.Graph, boundaries);
                LastFallbackReason = null;
                return true;
            }
            catch (Exception error) when (error is NotSupportedException || error is ShaderGraphException || error is FormatException)
            { LastFallbackReason = error.Message; vm = null; return false; }
        }

        bool CompleteHybridPreparation()
        {
            if (hybridPreparation == null) return true;
            var job = hybridPreparation;
            if (!job.complete) return false;
            hybridPreparation = null;
            if (job.error != null || job.vm == null || job.session.revision != ShaderProgramSourceRevision.Current)
            { LastFallbackReason = job.error?.Message ?? "No supported continuation"; Failed = true; job.Dispose(); return false; }
            var prepared = job.vm;
            ReplaceCode(prepared);
            hybridPreparedSource = job.source; hybridPreparedAnalysis = job.analysis;
            prepared.Dispose(); job.Dispose();
            return true;
        }

        internal void ConfigureHybrid(WhimTexDocument document, Layer layer, bool effect = false, bool main = false)
        {
            if (hybridDocument != document || hybridLayer != layer || hybridEffect != effect || hybridMain != main ||
                document != null && hybridLightingVariant != document.LightingVariant) dependenciesReady = false;
            if (executorPriority != null && executorPriority.main != main) ReleaseExecutor();
            hybridDocument = document; hybridLayer = layer; hybridEffect = effect; hybridMain = main;
        }

        bool UpdateHybridPreparation()
        {
            if (!HasBoundaries) return true;
            if (hybridDocument == null || hybridLayer == null) return false;
            try
            {
                if (hybridLightingVariant != hybridDocument.LightingVariant)
                {
                    hybridRequest?.Dispose(); hybridRequest = null;
                    hybridRuntime?.Dispose(); hybridRuntime = null;
                    hybridLightingVariant = hybridDocument.LightingVariant;
                }
                bool surfacesReady = UpdateLightingPreparation();
                if (hybridRuntime != null) return surfacesReady;
                hybridRequest ??= new ShaderProgramRuntime.CompilationRequest(hybridGraph, hybridCompilation,
                    ShaderGraphCompiler.CodeFingerprint(hybridGraph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",
                    false, hybridEffect, hybridMain, hybridPreparedSource, hybridPreparedAnalysis).WithLighting(hybridDocument);
                hybridRequest.SetPreviewPriority(hybridRank); hybridRequest.SetPreviewDemand(hybridWanted);
                if (!hybridRequest.TryComplete(out var runtime)) return false;
                hybridRuntime = runtime; UsesTime |= runtime.UsesTime; hybridRequest.Dispose(); hybridRequest = null;
                return surfacesReady;
            }
            catch (Exception error)
            {
                LastFallbackReason = error.Message; Failed = true;
                hybridRequest?.Dispose(); hybridRequest = null;
                return false;
            }
        }

        void PrepareImageContext(in LayerRenderContext context)
        {
            material.SetVector("_CanvasSize", new Vector4(context.activeDocument.width, context.activeDocument.height,
                1f / context.activeDocument.width, 1f / context.activeDocument.height));
            material.SetFloat("_PixelScale", context.pixelScale);
            BindTransforms(new Vector2(context.activeDocument.width,context.activeDocument.height));
            if ((noiseFamilies & 128) != 0) BlueNoiseTextures.EnsureShaderGlobals();
        }
        internal bool RenderBoundaries(RenderTexture target, in LayerRenderContext context)
        {
            if (!Ready) return false;
            PrepareImageContext(context);
            if (!HasImages) return Render(target,context.shaderContext.Time);
            using var input=NeedsInput && hybridLayer?.Behaviour is ShaderLayerBehaviour shader
                ? context.activeDocument.ResolveShaderInput(shader,context):default;
            if (!HasBoundaries) return RenderImages(target,context,input);
            var runtime = hybridRuntime;
            if (hybridEffect) return false; // FX input ownership remains with the native preview path.
            runtime.Bind(hybridGraph, context, branchCurrent: hybridValuesCurrent);
            runtime.PrepareCoordinates(hybridLayer, context);
            runtime.PrepareCoordinatesFor(material, hybridLayer, context);
            material.SetVector("_VMViewport", runtime.Material.GetVector("_WhimTex_Viewport"));
            var texture = input.Bind(runtime.Material, runtime.EmptyInput);
            using var bindings = context.activeDocument.BindShaderTextureLayers(runtime.Parameters, hybridLayer, context, null);
            bindings?.Apply(runtime.Material);
            try
            {
                runtime.RenderPrepared(texture, target, context, drawFinal: false);
                for (int i = 0; i < hybridInputs.Length; i++)
                {
                    var pixels = runtime.BufferTexture(hybridInputs[i].textureName);
                    SetImage(i,pixels);
                }
                return RenderImages(target,context,input);
            }
            finally { runtime.Material.SetTexture("_MainTex", null); }
        }

        void ReleaseHybrid()
        {
            dependenciesReady = false;
            if (lightingSurfaces != null) foreach (var surface in lightingSurfaces) surface?.Dispose();
            lightingSurfaces = null;
            hybridPreparation?.Dispose(); hybridPreparation = null;
            hybridRequest?.Dispose(); hybridRequest = null;
            hybridRuntime?.Dispose(); hybridRuntime = null;
        }

        sealed partial class Builder
        {
            readonly IReadOnlyDictionary<string, ShaderGraphBufferPass> boundaries;
            internal readonly List<ShaderGraphBufferPass> resources = new();
            Value Boundary(ShaderGraphNode node, ShaderGraphBufferPass pass)
            {
                if (pass.screenSpace && depth != 0) { Unsupported("Lighting inside a sampled branch"); return default; }
                int slot = resources.IndexOf(pass);
                if (slot < 0) { slot = resources.Count; if (slot+images.Count >= 8) Unsupported("Preview image input limit"); resources.Add(pass); }
                int uv = pass.screenSpace ? -1 : Input(node, "UV").register;
                return Add(new Instruction { op = 41, a = slot, b = uv, c = (int)node.wrap,
                    d = node.kind == ShaderGraphNodeKind.Buffer ? (int)node.filter : 1, e = pass.screenSpace ? 1 : 0,
                    f = pass.causticsColor ? 1 : 0, g = pass.causticsColor && pass.causticsSpectrum == null ? 1 : 0 },
                    node.kind == ShaderGraphNodeKind.Caustics ? (pass.causticsColor ? ShaderGraphValueType.Color : ShaderGraphValueType.Float) : pass.type);
            }
        }
    }
}
