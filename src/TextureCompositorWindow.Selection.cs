using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private List<string> selectedLayerIds = new List<string>();
        [SerializeField] private string selectionAnchorId;
        private const string DraggedLayersKey = "DCFApixels.SpriteEditor.DraggedLayers";
        [System.NonSerialized] private VisualElement footerDropTarget;

        private bool IsLayerSelected(string id) => id != null && selectedLayerIds.Contains(id);

        private bool HandleLayerNavigationKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.UpArrow && evt.keyCode != KeyCode.DownArrow)
                return false;
            if (evt.ctrlKey || evt.commandKey || evt.altKey || evt.shiftKey || compositor == null)
                return false;
            if (IsLayerNavigationInput(evt.target as VisualElement) ||
                IsLayerNavigationInput(rootVisualElement.panel?.focusController?.focusedElement as VisualElement))
                return false;
            if (activeLayerDrag != null || paintingLayer != null ||
                (previewTransformManipulator != null && previewTransformManipulator.IsDragging) ||
                (previewZoomManipulator != null && previewZoomManipulator.IsDragging))
                return false;

            SpriteEditorUI.ConsumeEvent(evt);
            int index = FindAdjacentLayerIndex(evt.keyCode == KeyCode.UpArrow ? -1 : 1);
            if (index < 0) return true;
            FinishPreviewTransform();
            FinishPaintingStroke();
            LayerTreeEntry entry = toolkitLayerTree[index];
            SelectOnlyLayer(entry.Layer.Id);
            rootVisualElement.Focus();
            RefreshToolkitInterface();
            ScrollView scroll = toolkitLayerHierarchyRoot?.GetFirstAncestorOfType<ScrollView>();
            int rowIndex = 0;
            for (int i = 0; i < index; i++)
                if (toolkitLayerTree[i].Index >= 0) rowIndex++;
            if (scroll != null && rowIndex < toolkitLayerHierarchyRoot.childCount)
                scroll.ScrollTo(toolkitLayerHierarchyRoot[rowIndex]);
            return true;
        }

        private int FindAdjacentLayerIndex(int direction)
        {
            int current = -1;
            for (int i = 0; i < toolkitLayerTree.Count; i++)
            {
                var entry = toolkitLayerTree[i];
                if (entry.Layer != null && entry.Layer.Id == selectedLayerId)
                {
                    current = i;
                    break;
                }
            }
            int next = current < 0 ? (direction > 0 ? 0 : toolkitLayerTree.Count - 1) : current + direction;
            for (int i = next; i >= 0 && i < toolkitLayerTree.Count; i += direction)
                if (toolkitLayerTree[i].Index >= 0 && toolkitLayerTree[i].Layer != null) return i;
            return -1;
        }

        private static bool IsLayerNavigationInput(VisualElement element)
        {
            if (IsTextInputTarget(element)) return true;
            for (VisualElement current = element; current != null; current = current.parent)
                if (current.ClassListContains("unity-base-field") || current is IMGUIContainer)
                    return true;
            return false;
        }

        private void SelectOnlyLayer(string id)
        {
            ResetOpacityEntry();
            selectedLayerIds.Clear();
            if (id != null)
                selectedLayerIds.Add(id);
            selectedLayerId = id;
            selectionAnchorId = id;
        }

        private void NormalizeLayerSelection()
        {
            selectedLayerIds ??= new List<string>();
            selectedLayerIds.RemoveAll(id => compositor == null || compositor.FindLayer(id) == null);
            if (compositor != null && compositor.FindLayer(selectedLayerId) != null)
            {
                if (!selectedLayerIds.Contains(selectedLayerId))
                    selectedLayerIds.Add(selectedLayerId);
            }
            else
                selectedLayerId = selectedLayerIds.Count > 0 ? selectedLayerIds[selectedLayerIds.Count - 1] : null;
            if (compositor == null || compositor.FindLayer(selectionAnchorId) == null)
                selectionAnchorId = selectedLayerId;
        }

        private void ActivateSelectedLayer(string id)
        {
            ResetOpacityEntry();
            selectedLayerIds.Remove(id);
            selectedLayerIds.Add(id);
            selectedLayerId = id;
        }

        private void SelectLayerFromPointer(Layer layer, PointerDownEvent evt, bool preserveSelection = false)
        {
            FinishPreviewTransform();
            FinishPaintingStroke();
            bool additive = evt.ctrlKey || evt.commandKey;
            if (evt.shiftKey)
            {
                string anchor = selectionAnchorId ?? selectedLayerId ?? layer.Id;
                int from = toolkitLayerTree.FindIndex(entry => entry.Layer?.Id == anchor);
                int to = toolkitLayerTree.FindIndex(entry => entry.Layer == layer);
                if (!additive)
                    selectedLayerIds.Clear();
                if (from >= 0 && to >= 0)
                {
                    for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
                    {
                        string id = toolkitLayerTree[i].Layer?.Id;
                        if (id != null && !selectedLayerIds.Contains(id))
                            selectedLayerIds.Add(id);
                    }
                }
                ActivateSelectedLayer(layer.Id);
                selectionAnchorId = anchor;
            }
            else if (additive)
            {
                if (IsLayerSelected(layer.Id))
                {
                    selectedLayerIds.Remove(layer.Id);
                    selectedLayerId = selectedLayerIds.Count > 0 ? selectedLayerIds[selectedLayerIds.Count - 1] : null;
                }
                else
                    ActivateSelectedLayer(layer.Id);
                selectionAnchorId = layer.Id;
            }
            else if (preserveSelection && IsLayerSelected(layer.Id))
                ActivateSelectedLayer(layer.Id);
            else
                SelectOnlyLayer(layer.Id);
            RefreshToolkitInterface();
        }

        private void ApplyLayerSelectionStyle(VisualElement row, string id)
        {
            row.AddToClassList("sprite-editor-layer-row");
            row.EnableInClassList("sprite-editor-layer-row--light", !EditorGUIUtility.isProSkin);
            row.EnableInClassList("sprite-editor-layer-row--selected", IsLayerSelected(id));
            row.EnableInClassList("sprite-editor-layer-row--active", id == selectedLayerId);
        }

        private List<Layer> GetSelectedRoots()
        {
            List<Layer> result = new List<Layer>();
            CollectSelectedRoots(compositor.layers, result);
            return result;
        }

        private List<Layer> GetSelectedParameterLayers(Layer source)
        {
            var result = new List<Layer>();
            if (compositor == null || source == null || !compositor.TryFindLayer(source, out _, out _)) return result;
            if (!IsLayerSelected(source.Id)) result.Add(source);
            else
                foreach (string id in selectedLayerIds)
                {
                    Layer selected = compositor.FindLayer(id);
                    if (selected != null) result.Add(selected);
                }
            return result;
        }

        private void ApplySelectedParameter(Layer source, string undoName, System.Action<Layer> change)
        {
            List<Layer> targets = GetSelectedParameterLayers(source);
            if (targets.Count == 0) return;
            FinishPreviewTransform();
            FinishPaintingStroke();
            ApplyToolkitChange(undoName, () =>
            {
                foreach (Layer layer in targets)
                    if (!(layer?.Behaviour is PendingLayerBehaviour) && !SpriteEditorApi.IsLayerContentLocked(compositor, layer)) change(layer);
            });
        }

        private void ApplySelectedOpacity(Layer source, float value)
        {
            float opacity = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
            ApplySelectedParameter(source, "Change Selected Layers Opacity", layer => layer.opacity = opacity);
        }

        private void ApplySelectedBlend(Layer source, BlendMode mode, bool passThrough = false)
        {
            ApplySelectedParameter(source, "Change Selected Layers Blend", layer =>
            {
                if (layer?.AsGroup() is Layer group)
                    group.compositing = passThrough ? GroupCompositing.PassThrough : GroupCompositing.Isolated;
                if (!passThrough) layer.blendMode = mode;
            });
        }

        private void CollectSelectedRoots(List<Layer> container, List<Layer> result)
        {
            foreach (Layer layer in container)
            {
                if (layer == null)
                    continue;
                if (IsLayerSelected(layer.Id))
                    result.Add(layer);
                else if (layer?.AsGroup() is Layer group)
                    CollectSelectedRoots(group.layers, result);
            }
        }

        private static bool ContainerContainsLayer(List<Layer> container, Layer target)
        {
            foreach (Layer layer in container)
                if (layer == target || layer?.AsGroup() is Layer group && ContainerContainsLayer(group.layers, target))
                    return true;
            return false;
        }

        private void DeleteSelectedLayers()
        {
            DeleteLayers(GetSelectedRoots());
        }

        private void DuplicateLayers(List<Layer> layers)
        {
            if (layers.Exists(SpriteEditorApi.ContainsReservation))
            { ShowNotification(new GUIContent("Finish or cancel generation before duplicating these layers.")); return; }
            FinishPreviewTransform();
            FinishPaintingStroke();
            Layer active = GetSelectedLayer();
            applyingToolkitChange = true;
            try
            {
                Dictionary<Layer, Layer> copies = compositor.DuplicateLayers(layers);
                if (copies.Count == 0)
                    return;
                SelectOnlyLayer(null);
                foreach (Layer source in layers)
                    if (copies.TryGetValue(source, out Layer copy))
                        ActivateSelectedLayer(copy.Id);
                foreach (KeyValuePair<Layer, Layer> pair in copies)
                    if (pair.Key?.AsGroup() is Layer group)
                        groupExpansion[pair.Value.Id] = GetGroupExpanded(group);
                if (active != null && copies.TryGetValue(active, out Layer activeCopy))
                    ActivateSelectedLayer(activeCopy.Id);
                selectionAnchorId = selectedLayerId;
                temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
                lineAnchorLayer = null;
                RequestPreview();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Cannot Duplicate Layers", exception.Message, "OK");
            }
            finally
            {
                applyingToolkitChange = false;
                RefreshToolkitInterface();
            }
        }

        private void PasteCopiedLayers(TextureCompositor snapshot)
        {
            applyingToolkitChange = true;
            try
            {
                Dictionary<Layer, Layer> copies = compositor.PasteLayers(snapshot);
                SelectOnlyLayer(null);
                foreach (Layer source in snapshot.layers)
                    if (copies.TryGetValue(source, out Layer copy)) ActivateSelectedLayer(copy.Id);
                foreach (Layer copy in copies.Values)
                    if (copy.IsGroup) groupExpansion[copy.Id] = true;
                selectionAnchorId = selectedLayerId;
                temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
                lineAnchorLayer = null;
                RequestPreview();
            }
            finally
            {
                applyingToolkitChange = false;
                RefreshToolkitInterface();
            }
        }

        private void DeleteLayers(List<Layer> layers)
        {
            FinishPreviewTransform();
            FinishPaintingStroke();
            if (layers.Count == 0)
                return;
            ExecuteContextChange("Delete Sprite Layers", () =>
            {
                foreach (Layer layer in layers)
                {
                    if (!compositor.TryFindLayer(layer, out List<Layer> container, out _))
                        continue;
                    compositor.DestroyLayerAssets(layer);
                    container.Remove(layer);
                    layer.ReleaseTransientResources();
                }
                SelectOnlyLayer(null);
            });
        }

        private void MergeSelectedLayers(List<Layer> layers, bool keepSources)
        {
            if (layers.Exists(SpriteEditorApi.ContainsReservation))
            { ShowNotification(new GUIContent("Finish or cancel generation before merging these layers.")); return; }
            FinishPreviewTransform();
            FinishPaintingStroke();
            if (compositor == null || layers.Count == 0) return;
            applyingToolkitChange = true;
            try
            {
                DrawingLayerBehaviour merged = compositor.MergeLayers(layers, keepSources);
                SelectOnlyLayer(merged.Id);
                temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
                lineAnchorLayer = null;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Cannot Merge Layers", exception.Message, "OK");
            }
            finally
            {
                applyingToolkitChange = false;
                RequestPreview(true);
                RefreshToolkitInterface(forceValues: true);
            }
        }

        private List<Layer> GetDraggedRoots() => GetDraggedLayer() != null
            ? DragAndDrop.GetGenericData(DraggedLayersKey) as List<Layer> : null;

        private void ClearFooterDropIndicator()
        {
            footerDropTarget?.RemoveFromClassList("sprite-editor-layer-action--drop-target");
            footerDropTarget = null;
        }

        private enum LayerFooterDropAction { Group, Delete, Duplicate, MergeCopy }

        private sealed class LayerFooterDropManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private readonly LayerFooterDropAction action;

            public LayerFooterDropManipulator(TextureCompositorWindow owner, LayerFooterDropAction action)
            {
                this.owner = owner;
                this.action = action;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
                target.RegisterCallback<DragPerformEvent>(OnDragPerform);
                target.RegisterCallback<DragLeaveEvent>(OnDragLeave);
                target.RegisterCallback<DragExitedEvent>(OnDragExited);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                ClearHighlight();
                target.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
                target.UnregisterCallback<DragPerformEvent>(OnDragPerform);
                target.UnregisterCallback<DragLeaveEvent>(OnDragLeave);
                target.UnregisterCallback<DragExitedEvent>(OnDragExited);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            private bool TryGetLayers(out List<Layer> layers)
            {
                layers = owner.GetDraggedRoots();
                if (!target.enabledInHierarchy || layers == null || layers.Count == 0)
                    return false;
                foreach (Layer layer in layers)
                    if (!owner.compositor.TryFindLayer(layer, out _, out _))
                        return false;
                return true;
            }

            private void OnDragUpdated(DragUpdatedEvent evt)
            {
                owner.ClearToolkitDropIndicator();
                bool valid = TryGetLayers(out _);
                DragAndDrop.visualMode = !valid ? DragAndDropVisualMode.Rejected :
                    action == LayerFooterDropAction.Duplicate || action == LayerFooterDropAction.MergeCopy
                        ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Move;
                owner.ClearFooterDropIndicator();
                if (valid)
                {
                    owner.footerDropTarget = target;
                    target.AddToClassList("sprite-editor-layer-action--drop-target");
                }
                evt.StopImmediatePropagation();
            }

            private void OnDragPerform(DragPerformEvent evt)
            {
                evt.StopImmediatePropagation();
                if (!TryGetLayers(out List<Layer> layers))
                {
                    ClearHighlight();
                    return;
                }
                DragAndDrop.AcceptDrag();
                owner.FinishPreviewTransform();
                owner.FinishPaintingStroke();
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                try
                {
                    if (action == LayerFooterDropAction.Duplicate)
                        owner.DuplicateLayers(layers);
                    else if (action == LayerFooterDropAction.MergeCopy)
                        owner.MergeSelectedLayers(layers, keepSources: true);
                    else if (action == LayerFooterDropAction.Delete)
                        owner.DeleteLayers(layers);
                    else
                        owner.GroupLayers(layers);
                }
                finally
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.IncrementCurrentGroup();
                    owner.ClearLayerDragData();
                    owner.ClearToolkitDropIndicator();
                }
            }

            private void ClearHighlight()
            {
                if (owner.footerDropTarget == target)
                    owner.ClearFooterDropIndicator();
            }

            private void OnDragLeave(DragLeaveEvent evt) => ClearHighlight();
            private void OnDragExited(DragExitedEvent evt) => ClearHighlight();
            private void OnDetach(DetachFromPanelEvent evt) => ClearHighlight();
        }

        private bool CanDropLayers(List<Layer> layers, List<Layer> destination)
        {
            if (destination == null || layers.Count == 0)
                return false;
            foreach (Layer layer in layers)
                if (!compositor.TryFindLayer(layer, out _, out _) ||
                    layer?.AsGroup() is Layer group && ContainsLayerContainer(group, destination))
                    return false;
            return true;
        }

        private void PerformSelectedLayersDrop(List<Layer> layers, List<Layer> destination, int index, Layer expand)
        {
            if (!CanDropLayers(layers, destination))
                return;
            FinishPreviewTransform();
            FinishPaintingStroke();
            index = Mathf.Clamp(index, 0, destination.Count);
            int insertion = index;
            foreach (Layer layer in layers)
                if (compositor.TryFindLayer(layer, out List<Layer> source, out int sourceIndex) &&
                    ReferenceEquals(source, destination) && sourceIndex < index)
                    insertion--;
            ExecuteModelChange("Move Sprite Layers", () =>
            {
                foreach (Layer layer in layers)
                    if (compositor.TryFindLayer(layer, out List<Layer> source, out _))
                        source.Remove(layer);
                destination.InsertRange(insertion, layers);
                if (expand != null)
                    groupExpansion[expand.Id] = true;
            });
        }
    }
}
