using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private void ExecuteContextChange(string undoName, Action action)
        {
            FinishPreviewTransform();
            FinishPaintingStroke();
            if (compositor == null) return;
            var previousSelection = new List<string>(selectedLayerIds);
            string previousActive = selectedLayerId;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RegisterCompleteObjectUndo(compositor, undoName);
            applyingToolkitChange = true;
            try
            {
                action();
                NormalizeLayerSelection();
                CommitModelChange();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                selectedLayerIds = previousSelection;
                selectedLayerId = previousActive;
                NormalizeLayerSelection();
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Cannot Change Layers", exception.Message, "OK");
            }
            finally
            {
                Undo.IncrementCurrentGroup();
                applyingToolkitChange = false;
                RequestPreview(true);
                RefreshToolkitInterface(forceValues: true);
            }
        }

        private void SelectContextLayers(List<Layer> layers, string preferredActive)
        {
            SelectOnlyLayer(null);
            foreach (Layer layer in layers)
                if (layer != null && !selectedLayerIds.Contains(layer.Id)) selectedLayerIds.Add(layer.Id);
            selectedLayerId = selectedLayerIds.Contains(preferredActive)
                ? preferredActive : selectedLayerIds.Count > 0 ? selectedLayerIds[selectedLayerIds.Count - 1] : null;
            selectionAnchorId = selectedLayerId;
        }

        private void MoveContextLayers(List<Layer> roots, int direction)
        {
            ExecuteContextChange("Reorder Sprite Layers", () =>
                LayerSelectionOperations.Move(compositor, roots, direction, true));
        }

        private void ApplyContextChannelPreset(List<Layer> targets)
        {
            if (!LayerSelectionOperations.CanApplyChannelPreset(targets.Count)) return;
            if (targets.Exists(target => WhimTexApi.IsLayerContentLocked(compositor, target))) return;
            ExecuteContextChange("Assign Channels", () =>
                LayerSelectionOperations.ApplyChannelPreset(compositor, targets));
        }

        private void MoveContextLayersAcrossGroups(List<Layer> roots, bool into)
        {
            ExecuteContextChange(into ? "Move Layers Into Groups" : "Move Layers Out Of Groups", () =>
            {
                var moves = LayerSelectionOperations.PlanGroupMoves(compositor, roots, into);
                LayerSelectionOperations.ApplyGroupMoves(moves, into);
                if (into)
                    foreach (var move in moves) groupExpansion[move.group.Id] = true;
            });
        }

        private void AddInsideContextGroups(List<Layer> targets, Func<Layer> create)
        {
            ExecuteContextChange("Add Layers Inside Groups", () =>
            {
                var created = new List<Layer>();
                string preferredActive = null;
                foreach (Layer target in targets)
                {
                    if (!(target?.AsGroup() is Layer group) || !compositor.TryFindLayer(group, out _, out _)) continue;
                    Layer layer = create();
                    layer.AssignNewId();
                    layer.layerName = compositor.AllocateLayerName(layer);
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing)
                    {
                        try
                        {
                            drawing.InitializeCanvas(compositor.width, compositor.height);
                            drawing.InvalidatePaintSurface();
                            drawing.MakeTexturePersistent(compositor);
                            Undo.RegisterCreatedObjectUndo(drawing.StoredTexture, "Add Layers Inside Groups");
                        }
                        catch
                        {
                            if (drawing.StoredTexture != null) DestroyImmediate(drawing.StoredTexture, true);
                            drawing.InvalidatePaintSurface();
                            throw;
                        }
                    }
                    group.layers.Insert(0, layer);
                    if (layer?.Behaviour is ShaderProcessorLayerBehaviour) compositor.AddEmbeddedShaderFX(layer);
                    created.Add(layer);
                    groupExpansion[group.Id] = true;
                    if (layer?.IsGroup == true) groupExpansion[layer.Id] = true;
                    if (group.Id == selectedLayerId) preferredActive = layer.Id;
                }
                compositor.NormalizeModel();
                SelectContextLayers(created, preferredActive);
            });
        }

        private void UngroupContextLayers(List<Layer> targets)
        {
            ExecuteContextChange("Ungroup Sprite Layers", () =>
            {
                string activeId = selectedLayerId;
                List<Layer> result = LayerSelectionOperations.Ungroup(compositor, targets);
                SelectContextLayers(result, activeId);
            });
        }
    }
}
