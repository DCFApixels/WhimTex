using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private sealed class ProjectTextureDropManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private readonly bool prependToRoot;

            public ProjectTextureDropManipulator(TextureCompositorWindow owner, bool prependToRoot = false)
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

            private bool HasProjectTextures()
            {
                if (owner.compositor == null || DragAndDrop.GetGenericData(DraggedCompositorIdKey) != null)
                    return false;
                UnityEngine.Object[] objects = DragAndDrop.objectReferences;
                if (objects.Length == 0)
                    return false;
                foreach (UnityEngine.Object item in objects)
                    if (!(item is Texture2D) || !AssetDatabase.Contains(item))
                        return false;
                return true;
            }

            private void ResolveTarget(VisualElement element, Vector2 mousePosition,
                out List<Layer> container, out int index, out Layer group,
                out VisualElement indicator, out bool before, out int depth)
            {
                container = owner.compositor.layers;
                index = container.Count;
                group = null;
                indicator = owner.toolkitLayerHierarchyRoot;
                before = false;
                depth = 0;
                if (prependToRoot)
                {
                    index = 0;
                    before = true;
                    return;
                }
                for (VisualElement current = element; current != null && current != target; current = current.parent)
                {
                    if (current.userData is List<Layer> endContainer)
                    {
                        container = endContainer;
                        index = container.Count;
                        indicator = current;
                        break;
                    }
                    if (!(current.userData is string id))
                        continue;
                    Layer layer = owner.compositor.FindLayer(id);
                    if (layer == null || !owner.compositor.TryFindLayer(layer, out List<Layer> rowContainer, out int rowIndex))
                        continue;
                    container = rowContainer;
                    index = rowIndex;
                    indicator = current;
                    float fraction = current.WorldToLocal(mousePosition).y / Mathf.Max(1f, current.resolvedStyle.height);
                    before = fraction < 0.5f;
                    if (layer?.AsGroup() is Layer destination && fraction >= 0.25f && fraction <= 0.75f)
                    {
                        group = destination;
                        container = destination.layers;
                        index = 0;
                    }
                    else if (!before)
                        index++;
                    break;
                }
                foreach (LayerTreeEntry entry in owner.toolkitLayerTree)
                    if (ReferenceEquals(entry.Container, container))
                    {
                        depth = entry.Depth;
                        break;
                    }
            }

            private void OnDragUpdated(DragUpdatedEvent evt)
            {
                if (!HasProjectTextures())
                    return;
                ResolveTarget(evt.target as VisualElement, evt.mousePosition,
                    out _, out _, out Layer group, out VisualElement indicator, out bool before, out int depth);
                owner.ClearFooterDropIndicator();
                owner.SetToolkitDropIndicator(indicator, group != null, before, depth);
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopImmediatePropagation();
            }

            private void OnDragPerform(DragPerformEvent evt)
            {
                if (!HasProjectTextures())
                    return;
                ResolveTarget(evt.target as VisualElement, evt.mousePosition,
                    out List<Layer> container, out int index, out Layer group, out _, out _, out _);
                UnityEngine.Object[] textures = DragAndDrop.objectReferences;
                DragAndDrop.AcceptDrag();
                evt.StopImmediatePropagation();
                owner.ClearToolkitDropIndicator();
                owner.FinishPreviewTransform();
                owner.FinishPaintingStroke();
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                try
                {
                    owner.ExecuteModelChange("Add File Layers", () =>
                    {
                        owner.SelectOnlyLayer(null);
                        index = Mathf.Clamp(index, 0, container.Count);
                        foreach (Texture2D texture in textures)
                        {
                            FileLayerBehaviour layer = new FileLayerBehaviour();
                            layer.layerName = owner.compositor.AllocateLayerName(layer);
                            layer.AssignSourceTexture(texture, owner.compositor, initializeCanvas: true);
                            container.Insert(index++, layer);
                            owner.compositor.NormalizeModel();
                            owner.ActivateSelectedLayer(layer.Id);
                        }
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
                if (HasProjectTextures())
                    owner.ClearToolkitDropIndicator();
            }
        }
    }
}
