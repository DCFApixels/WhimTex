using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private const string DraggedWindowKey = "DCFApixels.WhimTex.DraggedWindow";

        private bool IsCrossWindowLayerDrag()
        {
            var source = DragAndDrop.GetGenericData(DraggedCompositorIdKey) as TextureCompositor;
            if (source == null || compositor == null) return false;
            var window = DragAndDrop.GetGenericData(DraggedWindowKey) as TextureCompositorWindow;
            return window != null ? window != this : source != compositor;
        }

        private bool TryGetCrossWindowLayers(out TextureCompositor source, out List<Layer> roots)
        {
            source = DragAndDrop.GetGenericData(DraggedCompositorIdKey) as TextureCompositor;
            roots = DragAndDrop.GetGenericData(DraggedLayersKey) as List<Layer>;
            if (!IsCrossWindowLayerDrag() || roots == null || roots.Count == 0) return false;
            foreach (Layer layer in roots)
                if (layer == null || !source.TryFindLayer(layer, out _, out _) || WhimTexApi.ContainsReservation(layer)) return false;
            return true;
        }

        private void ResolveCrossWindowLayerDrop(Vector2 point, out List<Layer> destination, out int index,
            out Layer expand, out VisualElement indicator, out bool before, out int depth)
        {
            destination = compositor.layers; index = 0; expand = null;
            indicator = null; before = true; depth = 0;
            if (toolkitSettingsScroll?.panel == null || !toolkitSettingsScroll.contentViewport.worldBound.Contains(point)) return;
            VisualElement picked = rootVisualElement.panel.Pick(point);
            for (var element = picked; element != null && element != toolkitSettingsScroll; element = element.parent)
            {
                if (element.ClassListContains("whimtex-layer-row") && element.userData is string id)
                {
                    Layer layer = compositor.FindLayer(id);
                    if (layer == null || !compositor.TryFindLayer(layer, out destination, out index)) return;
                    foreach (var entry in toolkitLayerTree) if (entry.Layer == layer) { depth = entry.Depth; break; }
                    float y = element.WorldToLocal(point).y, height = Mathf.Max(1, element.resolvedStyle.height);
                    before = y < height * .5f;
                    if (layer.IsGroup && y >= height * .25f && y <= height * .75f)
                    { expand = layer; destination = layer.layers; index = 0; }
                    else if (!before) index++;
                    indicator = element;
                    return;
                }
                if (element.userData is List<Layer> container)
                { destination = container; index = container.Count; indicator = element; return; }
            }
            if (IsLayerListEndDropArea(point))
            { index = destination.Count; indicator = toolkitLayerEndDropZone; }
        }

        private bool UpdateCrossWindowLayerDrop(Vector2 point)
        {
            ClearToolkitDropIndicator();
            ClearFooterDropIndicator();
            bool valid = TryGetCrossWindowLayers(out _, out _);
            if (valid)
            {
                ResolveCrossWindowLayerDrop(point, out _, out _, out Layer expand, out var indicator, out bool before, out int depth);
                if (indicator != null) SetToolkitDropIndicator(indicator, expand != null, before, depth);
            }
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            return valid;
        }

        private void OnCrossWindowLayerDragUpdated(DragUpdatedEvent evt)
        {
            if (!IsCrossWindowLayerDrag()) return;
            if (UpdateCrossWindowLayerDrop(evt.mousePosition)) layerDragAutoScroll?.UpdatePointer(evt.mousePosition);
            else layerDragAutoScroll?.Stop();
            evt.StopImmediatePropagation();
        }

        private void OnCrossWindowLayerDragPerform(DragPerformEvent evt)
        {
            if (!IsCrossWindowLayerDrag()) return;
            evt.StopImmediatePropagation();
            TextureCompositor snapshot = null;
            var sourceWindow = DragAndDrop.GetGenericData(DraggedWindowKey) as TextureCompositorWindow;
            try
            {
                if (!TryGetCrossWindowLayers(out var source, out var roots)) return;
                ResolveCrossWindowLayerDrop(evt.mousePosition, out var destination, out int index, out var expand, out _, out _, out _);
                sourceWindow?.FinishCanvasTransform();
                sourceWindow?.FinishPaintingStroke();
                FinishCanvasTransform(); FinishPaintingStroke();
                snapshot = source.CaptureLayerClipboard(roots);
                DragAndDrop.AcceptDrag();
                PasteCopiedLayersAt(snapshot, destination, index, expand);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Cannot copy layers: " + exception.Message));
            }
            finally
            {
                if (snapshot != null) DestroyImmediate(snapshot);
                sourceWindow?.ClearLayerDragData();
                ClearToolkitDropIndicator(); ClearLayerDragData();
            }
        }

        private void OnCrossWindowLayerDragLeave(DragLeaveEvent evt)
        {
            if (evt.target != rootVisualElement) return;
            ClearToolkitDropIndicator(); ClearFooterDropIndicator(); ClearLayerDragGhost();
            layerDragAutoScroll?.Stop();
        }
    }
}
