using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private bool HasFileTextureDrag()
        {
            if (activeDocument == null || DragAndDrop.GetGenericData(DraggedDocumentIdKey) != null) return false;
            return FileLayerSource.DragPaths() != null;
        }

        private sealed class FileTextureDropManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private readonly bool prependToRoot;

            public FileTextureDropManipulator(WhimTexWindow owner, bool prependToRoot = false)
            {
                this.owner = owner;
                this.prependToRoot = prependToRoot;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
                target.RegisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
                target.RegisterCallback<DragLeaveEvent>(OnDragLeave);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragLeaveEvent>(OnDragLeave);
            }

            private bool HasTextures()
                => owner.HasFileTextureDrag();

            private bool ResolveTarget(VisualElement element, Vector2 mousePosition,
                out List<Layer> container, out int index, out Layer group,
                out VisualElement indicator, out bool before, out int depth)
            {
                container = owner.activeDocument.layers;
                index = container.Count;
                group = null;
                indicator = owner.toolkitLayerHierarchyRoot;
                before = false;
                depth = 0;
                if (prependToRoot)
                {
                    index = 0;
                    before = true;
                    return true;
                }
                if (!owner.TryResolveLayerDropTarget(mousePosition, out var drop)) return false;
                container = drop.Container; index = drop.Index; group = drop.Expand;
                indicator = drop.Indicator; before = drop.Before; depth = drop.Depth;
                return true;
            }

            private void OnDragUpdated(DragUpdatedEvent evt)
            {
                if (!HasTextures())
                    return;
                if (!ResolveTarget(evt.target as VisualElement, evt.mousePosition,
                    out _, out _, out Layer group, out VisualElement indicator, out bool before, out int depth))
                { owner.ClearToolkitDropIndicator(); owner.layerDragAutoScroll?.Stop(); DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; return; }
                owner.ClearFooterDropIndicator();
                owner.SetToolkitDropIndicator(indicator, group != null, before, depth);
                if (!prependToRoot) owner.layerDragAutoScroll?.UpdatePointer(evt.mousePosition);
                else owner.layerDragAutoScroll?.Stop();
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopImmediatePropagation();
            }

            private void OnDragPerform(DragPerformEvent evt)
            {
                if (!HasTextures())
                    return;
                if (!ResolveTarget(evt.target as VisualElement, evt.mousePosition,
                    out List<Layer> container, out int index, out Layer group, out _, out _, out _))
                { owner.ClearToolkitDropIndicator(); return; }
                string[] paths = FileLayerSource.DragPaths();
                try { foreach (string path in paths) FileLayerSource.Prepare(owner.activeDocument, path); }
                catch (System.Exception error)
                {
                    owner.ClearToolkitDropIndicator();
                    evt.StopImmediatePropagation();
                    EditorUtility.DisplayDialog("Source Image", error.Message, "OK");
                    return;
                }
                DragAndDrop.AcceptDrag();
                evt.StopImmediatePropagation();
                owner.ClearToolkitDropIndicator();
                owner.FinishCanvasTransform();
                owner.FinishPaintingStroke();
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                try
                {
                    owner.ExecuteModelChange("Add File Layers", () =>
                    {
                        owner.SelectOnlyLayer(null);
                        index = Mathf.Clamp(index, 0, container.Count);
                        foreach (string path in paths)
                        {
                            FileLayerBehaviour layer = new FileLayerBehaviour();
                            layer.layerName = owner.activeDocument.AllocateLayerName(layer);
                            layer.AssignSourcePath(path, owner.activeDocument, initializeCanvas: true);
                            container.Insert(index++, layer);
                        }
                        owner.activeDocument.NormalizeModel();
                        owner.ActivateSelectedLayer(container[index - 1].Id);
                        owner.selectionAnchorId = owner.selectedLayerId;
                        if (group != null)
                            owner.groupExpansion[group.Id] = true;
                    });
                    if (prependToRoot)
                        owner.toolkitSettingsScroll.scrollOffset = Vector2.zero;
                }
                finally
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.IncrementCurrentGroup();
                    owner.ClearLayerDragData();
                }
            }

            private void OnDragLeave(DragLeaveEvent evt)
            {
                if (HasTextures())
                {
                    owner.ClearToolkitDropIndicator();
                    if (evt.target == target) owner.layerDragAutoScroll?.Stop();
                }
            }
        }
    }
}
