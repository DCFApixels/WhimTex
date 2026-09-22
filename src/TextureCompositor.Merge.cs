using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        internal DrawingLayerBehaviour MergeLayers(List<Layer> requested, bool keepSources)
        {
            MergePlan plan = CreateMergePlan(requested);
            string undoName = keepSources ? "Merge Sprite Layers as Copy" : "Merge Sprite Layers";
            Texture2D texture = null;
            DrawingLayerBehaviour merged = null;
            int undoGroup = -1;
            try
            {
                VisitDrawingLayers(plan.roots, drawing => drawing.SyncSurfaceToTexture());
                texture = RasterizeMerge(plan);
                merged = DrawingLayerBehaviour.FromMergedTexture(texture);
                PlaceCanvasTransform(merged.Owner, plan.destination);
                foreach (Layer layer in plan.included)
                    if (layer.blendRange == LayerBlendRange.HDR)
                        merged.blendRange = LayerBlendRange.HDR;

                var inputs = new Dictionary<TargetedLayerBehaviour, Layer>();
                CaptureInputs(layers, inputs);
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                merged.MakeTexturePersistent(this);
                // Registering a new native object flushes pending object records in Unity.
                // Register the document afterwards so Redo captures the actual merged tree.
                Undo.RegisterCreatedObjectUndo(texture, undoName);
                Undo.RegisterCompleteObjectUndo(this, undoName);
                merged.layerName = keepSources ? AllocateDuplicateName(plan.roots[0]) : plan.roots[0].layerName;
                texture.name = merged.layerName;

                // Insert first so removing selected siblings cannot shift the intended location.
                plan.destination.Insert(plan.index, merged);
                if (!keepSources)
                    foreach (Layer layer in plan.roots)
                    {
                        if (!TryFindLayer(layer, out List<Layer> container, out _))
                            throw new InvalidOperationException("The merge selection changed unexpectedly.");
                        container.Remove(layer);
                        DestroyLayerAssets(layer);
                    }
                if (!keepSources) ReleaseLayerResources(plan.roots);

                foreach (var input in inputs)
                {
                    TargetedLayerBehaviour effect = input.Key;
                    if (!TryFindLayer(effect, out List<Layer> container, out int index)) continue;
                    Layer target = input.Value;
                    if (!keepSources && target != null && plan.removed.Contains(target)) target = merged;
                    if (effect.inputMode == EffectInputMode.Previous)
                    {
                        Layer adjacent = index + 1 < container.Count ? container[index + 1] : null;
                        if (adjacent == target) continue;
                        effect.inputMode = EffectInputMode.Specific;
                        effect.TargetLayerId = target?.Id;
                    }
                    else if (target == merged.Owner)
                        effect.TargetLayerId = merged.Id;
                }
                MarkChanged();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                return merged;
            }
            catch
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                merged?.ReleaseTransientResources();
                if (texture != null) DestroyImmediate(texture, true);
                throw;
            }
            finally
            {
                if (undoGroup >= 0) Undo.IncrementCurrentGroup();
            }
        }

        private sealed class MergePlan
        {
            internal readonly List<Layer> roots = new List<Layer>();
            internal readonly HashSet<Layer> included = new HashSet<Layer>();
            internal readonly HashSet<Layer> removed = new HashSet<Layer>();
            internal List<Layer> destination;
            internal int index;
        }

        private MergePlan CreateMergePlan(List<Layer> requested)
        {
            if (requested == null || requested.Count == 0)
                throw new InvalidOperationException("Select at least one layer or group to merge.");
            foreach (Layer layer in requested)
                if (layer == null || !TryFindLayer(layer, out _, out _))
                    throw new InvalidOperationException("A selected layer no longer belongs to this document.");
            var selected = new HashSet<Layer>(requested);
            var plan = new MergePlan();
            Collect(layers);
            TryFindLayer(plan.roots[0], out plan.destination, out _);
            foreach (Layer root in plan.roots)
                while (!TryFindLayerRecursive(plan.destination, root, out _, out _))
                {
                    if (!TryFindParentGroup(plan.destination, out _, out List<Layer> parent, out _))
                        throw new InvalidOperationException("Cannot find a common merge container.");
                    plan.destination = parent;
                }
            plan.index = plan.destination.Count;
            foreach (Layer root in plan.roots)
            {
                AddSubtree(root);
                Layer branch = root;
                TryFindLayer(root, out List<Layer> container, out _);
                while (!ReferenceEquals(container, plan.destination))
                {
                    if (!TryFindParentGroup(container, out Layer parent, out List<Layer> parentContainer, out _))
                        throw new InvalidOperationException("Cannot resolve the merge hierarchy.");
                    plan.included.Add(parent);
                    branch = parent;
                    container = parentContainer;
                }
                plan.index = Math.Min(plan.index, plan.destination.IndexOf(branch));
            }
            return plan;

            void Collect(List<Layer> source)
            {
                foreach (Layer layer in source)
                {
                    if (layer == null) continue;
                    if (selected.Contains(layer)) plan.roots.Add(layer);
                    else if (layer?.AsGroup() is Layer group) Collect(group.layers);
                }
            }
            void AddSubtree(Layer layer)
            {
                if (layer == null) return;
                plan.included.Add(layer);
                plan.removed.Add(layer);
                if (layer?.AsGroup() is Layer group)
                    foreach (Layer child in group.layers) AddSubtree(child);
            }
        }

        private Texture2D RasterizeMerge(MergePlan plan)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rendered = GetClearRenderTexture(width, height);
            using var originalFiles = UseOriginalFilePixels(plan.included);
            try
            {
                // Keep original containers and indices for Previous/SDF/Outline input resolution.
                CompositeLayers(plan.destination, ref rendered, width, height, 1f, new HashSet<Layer>(), plan.included);
                return HdrUtility.ReadLinear(rendered);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rendered);
            }
        }

        private void CaptureInputs(List<Layer> source, Dictionary<TargetedLayerBehaviour, Layer> inputs)
        {
            for (int i = 0; i < source.Count; i++)
            {
                Layer layer = source[i];
                if (layer?.Behaviour is TargetedLayerBehaviour effect)
                    inputs[effect] = effect.inputMode == EffectInputMode.Specific
                        ? FindLayer(effect.TargetLayerId) : i + 1 < source.Count ? source[i + 1] : null;
                if (layer?.AsGroup() is Layer group) CaptureInputs(group.layers, inputs);
            }
        }
    }
}
