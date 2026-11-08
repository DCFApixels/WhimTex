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
        internal bool IsHybridPending => hybridPreparation != null || HasBoundaries;
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
            if (hybridPreparation != null) { hybridPreparation.priority.previewRank = rank; hybridPreparation.priority.wanted = wanted; } }
        internal bool HasBoundaries => hybridInputs != null;

        internal static bool TryCreateHybrid(ShaderGraph graph, string node, string port, bool simplifiedLighting,
            out ShaderGraphPreviewVM vm)
        {
            if (TryCreatePreview(graph, node, port, simplifiedLighting, out vm)) return true;
            try
            {
                var compiled = ShaderGraphCompiler.Prepare(graph).Compile(node, port, simplifiedLighting);
                return TryCreateFromCompilation(graph, node, port, compiled, out vm);
            }
            catch (Exception error) when (error is NotSupportedException || error is ShaderGraphException || error is FormatException)
            { LastFallbackReason = error.Message; vm = null; return false; }
        }

        internal static ShaderGraphPreviewVM BeginHybrid(ShaderGraph graph, string node, string port, bool simplifiedLighting, bool main = false)
        {
            if (TryCreatePreview(graph, node, port, simplifiedLighting, out var pure)) return pure;
            var job = ShaderGraphPreparation.Enqueue(ShaderGraphPreparation.Capture(graph, false), node, port,
                "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", new ShaderGraphPreparation.Priority(main), simplifiedLighting);
            job.hybrid = true;
            return new ShaderGraphPreviewVM { hybridPreparation = job };
        }

        internal static bool TryCreateFromCompilation(ShaderGraph graph, string node, string port,
            ShaderGraphCompilation compiled, out ShaderGraphPreviewVM vm)
        {
            vm = null;
            try
            {
                // The native compiler validates the graph and determines legal boundaries.
                // Unsupported arbitrary HLSL is never implicitly baked into an image.
                var boundaries = new Dictionary<string, ShaderGraphBufferPass>();
                foreach (var pass in compiled.buffers)
                    if (!pass.causticsHeight && !pass.lightingInputs) boundaries[pass.nodeId] = pass;
                if (boundaries.Count == 0) return false;
                var builder = new Builder(graph, boundaries, compiled.simplifiedLighting);
                var value = node == null ? builder.Output() : builder.Emit(node, port);
                value = builder.Cast(value, ShaderGraphValueType.Color);
                if (builder.resources.Count == 0) return false;
                var bundle = new ShaderGraphCompilation {
                    source = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ProceduralUv.cginc\"\n" +
                        ShaderGraphCompiler.CanvasCoordinatesSource + "float4 _GraphBakeSize, _GraphLightingViewport;\n" +
                        "float4 ApplyFX(PinnedUV uv, float4 color) { return 0; }",
                    contextDependencies = compiled.contextDependencies, usesCanvasPosition = compiled.usesCanvasPosition,
                    bufferExternalCode = compiled.bufferExternalCode, uniformNames = compiled.uniformNames
                };
                bundle.parameters.AddRange(compiled.parameters); bundle.shaderParameters.AddRange(compiled.shaderParameters);
                bundle.uniformNodes.AddRange(compiled.uniformNodes); bundle.buffers.AddRange(compiled.buffers);
                foreach (var pair in compiled.inputNames) bundle.inputNames.Add(pair.Key, pair.Value);
                vm = new ShaderGraphPreviewVM(builder, value.register) {
                    hybridGraph = graph, hybridCompilation = bundle, hybridInputs = builder.resources.ToArray()
                };
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
        { hybridDocument = document; hybridLayer = layer; hybridEffect = effect; hybridMain = main; }

        bool HybridReady()
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
                if (hybridRuntime != null) return true;
                hybridRequest ??= new ShaderProgramRuntime.CompilationRequest(hybridGraph, hybridCompilation,
                    ShaderGraphCompiler.CodeFingerprint(hybridGraph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",
                    false, hybridEffect, hybridMain, hybridPreparedSource, hybridPreparedAnalysis).WithLighting(hybridDocument);
                hybridRequest.SetPreviewPriority(hybridRank); hybridRequest.SetPreviewDemand(hybridWanted);
                if (!hybridRequest.TryComplete(out var runtime)) return false;
                hybridRuntime = runtime; UsesTime |= runtime.UsesTime; hybridRequest.Dispose(); hybridRequest = null;
                return true;
            }
            catch (Exception error)
            {
                LastFallbackReason = error.Message; Failed = true;
                hybridRequest?.Dispose(); hybridRequest = null;
                return false;
            }
        }

        internal bool RenderBoundaries(RenderTexture target, in LayerRenderContext context)
        {
            if (!Ready) return false;
            material.SetVector("_CanvasSize", new Vector4(context.activeDocument.width, context.activeDocument.height,
                1f / context.activeDocument.width, 1f / context.activeDocument.height));
            material.SetFloat("_PixelScale", context.pixelScale);
            if (!HasBoundaries) return Render(target, context.shaderContext.Time);
            if (!Ready) return false;
            var runtime = hybridRuntime;
            if (hybridEffect) return false; // FX input ownership remains with the native preview path.
            runtime.Bind(hybridGraph, context);
            runtime.PrepareCoordinates(hybridLayer, context);
            runtime.PrepareCoordinatesFor(material, hybridLayer, context);
            material.SetVector("_VMViewport", runtime.Material.GetVector("_WhimTex_Viewport"));
            using var input = context.activeDocument.ResolveShaderInput(hybridLayer.Behaviour as ShaderLayerBehaviour, context);
            var texture = input.Bind(runtime.Material, runtime.EmptyInput);
            using var bindings = context.activeDocument.BindShaderTextureLayers(runtime.Parameters, hybridLayer, context, null);
            bindings?.Apply(runtime.Material);
            try
            {
                runtime.RenderPrepared(texture, target, context, drawFinal: false);
                for (int i = 0; i < hybridInputs.Length; i++)
                {
                    var pixels = runtime.BufferTexture(hybridInputs[i].textureName);
                    material.SetTexture("_VMImage" + i, pixels);
                    material.SetVector("_VMImageSize" + i, new Vector4(pixels.width, pixels.height, 0, 0));
                }
                return Render(target, context.shaderContext.Time);
            }
            finally { runtime.Material.SetTexture("_MainTex", null); }
        }

        void ReleaseHybrid()
        {
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
                if (slot < 0) { slot = resources.Count; if (slot >= 8) Unsupported("Preview image input limit"); resources.Add(pass); }
                int uv = pass.screenSpace ? -1 : Input(node, "UV").register;
                return Add(new Instruction { op = 41, a = slot, b = uv, c = (int)node.wrap,
                    d = node.kind == ShaderGraphNodeKind.Buffer ? (int)node.filter : 1, e = pass.screenSpace ? 1 : 0 },
                    node.kind is ShaderGraphNodeKind.Caustics or ShaderGraphNodeKind.CausticsNoise ? ShaderGraphValueType.Float : pass.type);
            }
        }
    }
}
