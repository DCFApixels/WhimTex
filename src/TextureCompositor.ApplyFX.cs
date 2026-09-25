using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        internal string ApplyFXUnavailable(Layer layer, int lastIndex)
        {
            if (layer?.Behaviour == null || !TryFindLayer(layer, out _, out _))
                return "The layer no longer belongs to this document.";
            if (lastIndex < 0 || lastIndex >= layer.modifiers.Count)
                return "There are no effects to apply.";
            if (WhimTexApi.IsLayerContentLocked(this, layer) || WhimTexApi.ContainsReservation(layer))
                return "Finish or cancel generation before applying effects.";
            for (int i = 0; i <= lastIndex; i++)
                if (layer.modifiers[i] is ShaderFX fx && WhimTexApi.IsShaderFXContentLocked(fx))
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
                VisitDrawingLayers(new List<Layer> { layer }, drawing => drawing.SyncSurfaceToTexture());
                texture = RasterizeFXPrefix(layer, lastIndex + 1);
                prepared = DrawingLayerBehaviour.FromRasterizedLayer(layer, texture, false, preserveGroupBlend: true);
                prepared.transform = layer.transform;
                prepared.swizzle = layer.swizzle;
                prepared.colorRange = layer.colorRange;
                prepared.modifiers = layer.modifiers.GetRange(lastIndex + 1, layer.modifiers.Count - lastIndex - 1);
                prepared.SetBakedPixelFrame(canvasToLayer);
                if (layer.IsGroup) prepared.SetBakedFxFrame(layer.CanvasTransform.ToMatrix(width, height));
                if (layer.Behaviour is ShaderProcessorLayerBehaviour)
                    prepared.SetProcessorSnapshot(layer.blendMode == BlendMode.Normal);

                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                prepared.MakeTexturePersistent(this);
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

        private Texture2D RasterizeFXPrefix(Layer layer, int count)
        {
            TryFindLayer(layer, out var container, out int index);
            RenderTexture previous = RenderTexture.active;
            RenderTexture rendered = null;
            var oldCache = effectCache;
            bool oldQuality = interactiveEffects;
            using var originalFiles = UseOriginalFilePixels(new[] { layer });
            try
            {
                effectCache = null;
                interactiveEffects = false;
                var context = new LayerRenderContext(this, null, width, height, 1f, transformFxCoordinates: !layer.IsGroup);
                for (int i = 0; i < count; i++)
                    if (layer.modifiers[i] is ShaderFX fx && fx.Active && fx.GetMaterial(context) == null)
                        throw new InvalidOperationException($"Apply the code for '{fx.name}' successfully before baking it.");

                if (layer.Behaviour is ShaderProcessorLayerBehaviour)
                {
                    RenderTexture input = RenderBackdropBefore(layer, new HashSet<Layer> { layer });
                    try { rendered = layer.Render(new LayerRenderContext(this, input, width, height, 1f, applyModifiers: false)); }
                    finally { RenderTexture.ReleaseTemporary(input); }
                }
                else if (layer.IsGroup)
                {
                    rendered = GetClearRenderTexture(width, height);
                    CompositeLayers(layer.layers, ref rendered, width, height, 1f, new HashSet<Layer> { layer });
                }
                else
                    rendered = RenderStandaloneUncached(container, index, width, height, 1f, new HashSet<Layer>(),
                        applyModifiers: false, includeDisabled: true, applyClipping: false, finishLayer: false);
                if (rendered == null) rendered = GetClearRenderTexture(width, height);
                layer.ApplyModifiers(ref rendered, context, layer.ResolveFilterMode(), count);
                return HdrUtility.ReadLinear(rendered);
            }
            finally
            {
                RenderTexture.active = previous;
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
                effectCache = oldCache;
                interactiveEffects = oldQuality;
            }
        }

        // Reconstruct the accumulator at this exact stack position. Pass-through groups
        // inherit their external backdrop; isolated/clipped groups start transparent.
        private RenderTexture RenderBackdropBefore(Layer layer, HashSet<Layer> stack)
        {
            TryFindLayer(layer, out var container, out int index);
            Layer parent = layer.transformCache?.parent;
            RenderTexture input = parent != null && parent.IsPassThrough && !IsGroupIsolatedByClipping(parent)
                ? RenderBackdropBefore(parent, stack) : GetClearRenderTexture(width, height);
            bool added = parent != null && stack.Add(parent);
            try
            {
                CompositeLayers(container, ref input, width, height, 1f, stack, firstIndex: index + 1);
                return input;
            }
            catch { RenderTexture.ReleaseTemporary(input); throw; }
            finally { if (added) stack.Remove(parent); }
        }
    }
}
