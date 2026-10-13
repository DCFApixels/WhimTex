using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private const string DraggedWindowKey = "DCFApixels.WhimTex.DraggedWindow";

        private bool IsCrossWindowLayerDrag()
        {
            var source = DragAndDrop.GetGenericData(DraggedDocumentIdKey) as WhimTexDocument;
            if (source == null || activeDocument == null) return false;
            var window = DragAndDrop.GetGenericData(DraggedWindowKey) as WhimTexWindow;
            return window != null ? window != this : source != activeDocument;
        }

        private bool TryGetCrossWindowLayers(out WhimTexDocument source, out List<Layer> roots)
        {
            source = DragAndDrop.GetGenericData(DraggedDocumentIdKey) as WhimTexDocument;
            roots = DragAndDrop.GetGenericData(DraggedLayersKey) as List<Layer>;
            if (!IsCrossWindowLayerDrag() || roots == null || roots.Count == 0) return false;
            foreach (Layer layer in roots)
                if (layer == null || !source.TryFindLayer(layer, out _, out _) || WhimTexApi.ContainsReservation(layer)) return false;
            return true;
        }

        private bool ResolveCrossWindowLayerDrop(Vector2 point, out List<Layer> destination, out int index,
            out Layer expand, out VisualElement indicator, out bool before, out int depth)
        {
            destination = null; index = -1; expand = null;
            indicator = null; before = true; depth = 0;
            if (!TryResolveLayerDropTarget(point, out var drop))
            {
                if (!rootVisualElement.worldBound.Contains(point) ||
                    toolkitSettingsScroll?.contentViewport.worldBound.Contains(point) == true) return false;
                destination = activeDocument.layers; index = 0;
                return true;
            }
            destination = drop.Container; index = drop.Index; expand = drop.Expand;
            indicator = drop.Indicator; before = drop.Before; depth = drop.Depth;
            return true;
        }

        private bool UpdateCrossWindowLayerDrop(Vector2 point)
        {
            ClearToolkitDropIndicator();
            ClearFooterDropIndicator();
            bool valid = TryGetCrossWindowLayers(out _, out _) &&
                ResolveCrossWindowLayerDrop(point, out _, out _, out _, out _, out _, out _);
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
            WhimTexDocument snapshot = null;
            var sourceWindow = DragAndDrop.GetGenericData(DraggedWindowKey) as WhimTexWindow;
            try
            {
                if (!TryGetCrossWindowLayers(out var source, out var roots)) return;
                if (!ResolveCrossWindowLayerDrop(evt.mousePosition, out var destination, out int index, out var expand, out _, out _, out _)) return;
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
