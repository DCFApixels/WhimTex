using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        int[] surfaceResults;
        int surfaceNormalEncoding;
        RenderBuffer[] surfaceTargets;
        RenderBuffer surfaceDepth;
        ShaderGraphBufferPass[] surfaceInputs;
        ShaderGraphPreviewVM[] lightingSurfaces;

        static ShaderGraphPreviewVM TryLightingSurface(ShaderGraph graph, ShaderGraphNode node,
            IReadOnlyDictionary<string, ShaderGraphBufferPass> boundaries)
        {
            try
            {
                var builder = new Builder(graph, boundaries);
                var outputs = builder.LightingSurface(node);
                return new ShaderGraphPreviewVM(builder, outputs[0]) {
                    surfaceResults = outputs, surfaceNormalEncoding = (int)node.normalEncoding,
                    surfaceInputs = builder.resources.ToArray()
                };
            }
            catch (Exception error) when (error is NotSupportedException || error is ShaderGraphException || error is FormatException)
            { return null; }
        }

        void PrepareLightingSurfaces(ShaderGraph graph, IReadOnlyDictionary<string, ShaderGraphBufferPass> boundaries)
        {
            bool hasLighting = false;
            foreach (var pass in hybridCompilation.buffers) hasLighting |= pass.lightingInputs;
            if (!hasLighting) return;
            var surfaces = new List<ShaderGraphPreviewVM>();
            var copies = new Dictionary<ShaderGraphBufferPass, ShaderGraphBufferPass>();
            // Never attach mutable preview state to a shared native compilation.
            for (int i = 0; i < hybridCompilation.buffers.Count; i++)
            {
                var original = hybridCompilation.buffers[i];
                var copy = original.Copy(); copies.Add(original, copy);
                hybridCompilation.buffers[i] = copy;
                if (!copy.lightingInputs) continue;
                copy.previewSurface = TryLightingSurface(graph, graph.FindNode(copy.nodeId), boundaries);
                if (copy.previewSurface != null) surfaces.Add(copy.previewSurface);
            }
            foreach (var pass in hybridCompilation.buffers)
                if (pass.lightingSource != null) pass.lightingSource = copies[pass.lightingSource];
            lightingSurfaces = surfaces.ToArray();
        }

        bool LightingSurfacesReady()
        {
            bool complete = true;
            if (lightingSurfaces == null) return true;
            for (int i = 0; i < lightingSurfaces.Length; i++)
            {
                var surface = lightingSurfaces[i];
                if (surface == null) continue;
                surface.ConfigureHybrid(hybridDocument, hybridLayer);
                if (surface.Ready) continue;
                if (!surface.Failed) { complete = false; continue; }
                // A device/compiler failure uses the original validated native producer.
                foreach (var pass in hybridCompilation.buffers)
                    if (ReferenceEquals(pass.previewSurface, surface)) pass.previewSurface = null;
                surface.Dispose(); lightingSurfaces[i] = null;
                hybridRequest?.Dispose(); hybridRequest = null;
                hybridRuntime?.Dispose(); hybridRuntime = null;
            }
            return complete;
        }

        internal void RenderLightingSurface(ShaderProgramRuntime runtime, Texture input, RenderBuffer[] colors,
            RenderBuffer depth, Vector4 viewport, in LayerRenderContext context)
        {
            if (!Ready) throw new InvalidOperationException("Lighting surface executor is not ready.");
            runtime.PrepareCoordinatesFor(material, hybridLayer, context);
            PrepareImageContext(context);
            material.SetVector("_VMViewport", viewport);
            material.SetVector("_VMSurfaceViewport", runtime.Material.GetVector("_GraphLightingViewport"));
            material.SetVector("_InputSize", runtime.Material.GetVector("_InputSize"));
            for (int i = 0; i < surfaceInputs.Length; i++) SetImage(i, runtime.BufferTexture(surfaceInputs[i].textureName));
            surfaceTargets = colors; surfaceDepth = depth;
            using var borrowed = new ShaderLayerInput(input);
            try { RenderImages(null, context, borrowed, runtime.Material); }
            finally { surfaceTargets = null; }
        }

        void UploadSurface()
        {
            if (surfaceResults == null) return;
            material.SetVector("_VMSurface0", new Vector4(surfaceResults[0], surfaceResults[1], surfaceResults[2], surfaceResults[3]));
            material.SetVector("_VMSurface1", new Vector4(surfaceResults[4], surfaceResults[5], surfaceResults[6], surfaceResults[7]));
            material.SetInt("_VMSurfaceNormalEncoding", surfaceNormalEncoding);
        }

        sealed partial class Builder
        {
            internal int[] LightingSurface(ShaderGraphNode node)
            {
                extended = usesCanvasPosition = true;
                return new[] { Input(node,"Color").register, Input(node,"Normal").register,
                    Input(node,"Ambient").register, Input(node,"UV").register,
                    Input(node,"Height").register, Input(node,"Roughness").register,
                    Input(node,"SpecularColor").register, Input(node,"SpecularIntensity").register };
            }
        }
    }
}
