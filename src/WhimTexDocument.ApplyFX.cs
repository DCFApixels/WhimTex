using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        internal string ApplyFXUnavailable(Layer layer, int lastIndex)
        {
            if (layer?.Behaviour == null || !TryFindLayer(layer, out _, out _))
                return "The layer no longer belongs to this document.";
            if (lastIndex < 0 || lastIndex >= layer.fx.Count)
                return "There are no effects to apply.";
            if (WhimTexApi.IsLayerContentLocked(this, layer) || WhimTexApi.ContainsReservation(layer))
                return "Finish or cancel generation before applying effects.";
            for (int i = 0; i <= lastIndex; i++)
                if (layer.fx[i] is ShaderFX fx && WhimTexApi.IsShaderFXContentLocked(fx))
                    return "An effect is being edited by an agent.";
            return null;
        }

        internal void ApplyLayerFX(Layer layer, int lastIndex)
        {
            string unavailable = ApplyFXUnavailable(layer, lastIndex);
            if (unavailable != null) throw new InvalidOperationException(unavailable);
            RefreshTransformHierarchy();
            if (!layer.CanvasTransform.ToMatrix(width, height).TryInverse(out var canvasToLayer))
                throw new InvalidOperationException("Cannot apply effects through a singular layer transform.");

            Texture2D texture = null;
            DrawingLayerBehaviour prepared = null;
            int undoGroup = -1;
            bool registered = false;
            const string undoName = "Apply Layer FX";
            try
            {
                VisitDrawingLayers(new List<Layer> { layer }, drawing => drawing.SyncPendingSurfaceToTexture());
                texture = RasterizeFXPrefix(layer, lastIndex + 1);
                prepared = DrawingLayerBehaviour.FromRasterizedLayer(layer, texture, false, preserveGroupBlend: true);
                prepared.transform = layer.transform;
                prepared.channelMapping = layer.channelMapping;
                prepared.colorRange = layer.colorRange;
                prepared.fx = layer.fx.GetRange(lastIndex + 1, layer.fx.Count - lastIndex - 1);
                prepared.SetBakedPixelFrame(canvasToLayer);
                if (layer.IsGroup) prepared.SetBakedFxFrame(layer.CanvasTransform.ToMatrix(width, height));
                if (layer.Behaviour is ShaderProcessorLayerBehaviour)
                    prepared.SetProcessorSnapshot(layer.blendMode == BlendMode.Normal);

                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                Undo.RegisterCreatedObjectUndo(texture, undoName);
                registered = true;
                Undo.RegisterCompleteObjectUndo(this, undoName);
                DestroyLayerAssets(layer);
                if (layer.IsGroup) ReleaseLayerResources(layer.layers);
                layer.AdoptContent(prepared);
                MarkChanged();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                prepared?.ReleaseTransientResources();
                if (!registered && texture != null) DestroyImmediate(texture);
                throw;
            }
            finally { if (undoGroup >= 0) Undo.IncrementCurrentGroup(); }
        }

        internal Texture2D RasterizeFXPrefix(Layer layer, int count) => RasterizeFXPrefixCore(layer, count, true, 0);

        [NonSerialized] private bool capturingInspectorFX;
        internal bool CapturingInspectorFX => capturingInspectorFX;

        internal void CaptureInspectorFXPrefix(Layer layer, int count, bool nativeResolution = false)
            => RasterizeFXPrefixCore(layer, count, false, nativeResolution ? Mathf.Max(width, height) : 512);

        private Texture2D RasterizeFXPrefixCore(Layer layer, int count, bool readback, int previewSize)
        {
            int renderWidth = width, renderHeight = height;
            if (previewSize > 0)
            {
                float reduction = Mathf.Min(1f, previewSize / (float)Mathf.Max(width, height));
                renderWidth = Mathf.Max(1, Mathf.RoundToInt(width * reduction));
                renderHeight = Mathf.Max(1, Mathf.RoundToInt(height * reduction));
            }
            float renderScale = width / (float)renderWidth;
            RefreshTransformHierarchy();
            TryFindLayer(layer, out var container, out int index);
            RenderTexture previous = RenderTexture.active;
            RenderTexture rendered = null;
            var oldCache = effectCache;
            bool oldQuality = interactiveEffects;
            bool oldViewport = renderingViewport, oldCapture = capturingInspectorFX;
            bool oldDiagnostics = collectingErrors, oldPublishing = publishingLayerPreview, oldSrgb = GL.sRGBWrite;
            var oldLimitLayer = viewportFxLimitLayer;
            int oldLimitCount = viewportFxLimitCount;
            using var originalFiles = UseOriginalFilePixels(new[] { layer });
            try
            {
                effectCache = null;
                interactiveEffects = false;
                renderingViewport = false;
                viewportFxLimitLayer = null;
                capturingInspectorFX = previewSize > 0;
                publishingLayerPreview = false;
                collectingErrors = false;
                GL.sRGBWrite = false;
                var context = new LayerRenderContext(this, null, renderWidth, renderHeight, renderScale, transformFxCoordinates: !layer.IsGroup);
                for (int i = 0; i < count; i++)
                    if (layer.fx[i] is ShaderFX fx && fx.Active && fx.GetMaterial(context) == null)
                        throw new InvalidOperationException($"Apply the code for '{fx.name}' successfully before baking it.");

                if (layer.Behaviour is ShaderProcessorLayerBehaviour)
                {
                    RenderTexture input = RenderBackdropBefore(layer, new HashSet<Layer> { layer }, renderWidth, renderHeight, renderScale);
                    try { rendered = layer.Render(new LayerRenderContext(this, input, renderWidth, renderHeight, renderScale, applyFx: false)); }
                    finally { ProfiledRenderTexture.ReleaseTemporary(input); }
                }
                else if (layer.IsGroup)
                {
                    rendered = GetClearRenderTexture(renderWidth, renderHeight);
                    RenderGroupGeometry(layer, ref rendered, renderWidth, renderHeight, renderScale, new HashSet<Layer> { layer });
                }
                else
                    rendered = RenderStandaloneUncached(container, index, renderWidth, renderHeight, renderScale, new HashSet<Layer>(),
                        applyFx: false, includeDisabled: true, applyClipping: false, finishLayer: false);
                if (rendered == null) rendered = GetClearRenderTexture(renderWidth, renderHeight);
                layer.ApplyFx(ref rendered, context, layer.ResolveFilterMode(), count);
                return readback ? HdrUtility.ReadLinear(rendered) : null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rendered != null) ProfiledRenderTexture.ReleaseTemporary(rendered);
                effectCache = oldCache;
                interactiveEffects = oldQuality;
                renderingViewport = oldViewport;
                capturingInspectorFX = oldCapture;
                collectingErrors = oldDiagnostics;
                publishingLayerPreview = oldPublishing;
                viewportFxLimitLayer = oldLimitLayer;
                viewportFxLimitCount = oldLimitCount;
                GL.sRGBWrite = oldSrgb;
            }
        }

        // Reconstruct the accumulator at this exact stack position. Pass-through groups
        // inherit their external backdrop; isolated/clipped groups start transparent.
        private RenderTexture RenderBackdropBefore(Layer layer, HashSet<Layer> stack, int w, int h, float scale)
        {
            TryFindLayer(layer, out var container, out int index);
            Layer parent = layer.transformCache?.parent;
            RenderTexture input = parent != null && parent.IsPassThrough && !IsGroupIsolatedByClipping(parent)
                ? RenderBackdropBefore(parent, stack, w, h, scale) : GetClearRenderTexture(w, h);
            bool added = parent != null && stack.Add(parent);
            try
            {
                CompositeLayers(container, ref input, w, h, scale, stack, firstIndex: index + 1);
                return input;
            }
            catch { ProfiledRenderTexture.ReleaseTemporary(input); throw; }
            finally { if (added) stack.Remove(parent); }
        }
    }
}
