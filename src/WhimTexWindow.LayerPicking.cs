using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private void AddLayerPickSettings(VisualElement row)
        {
            var threshold = new FloatField("Alpha ≥ %")
            {
                tooltip = "Minimum visible alpha for picking a layer. Default: 10%. Shift toggles selection; Ctrl selects inside groups. Shared with User Settings."
            };
            threshold.AddToClassList("whimtex-view-field");
            threshold.AddToClassList("whimtex-zoom-percent");
            toolkitHeaderBindings.Track(threshold, () => WhimTexUserSettings.LayerPickAlphaThreshold * 100f);
            threshold.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.LayerPickAlphaThreshold = evt.newValue * .01f;
                threshold.SetValueWithoutNotify(WhimTexUserSettings.LayerPickAlphaThreshold * 100f);
            });
            row.Add(threshold);
        }

        private bool HandleLayerPickPointerDown(PointerDownEvent evt)
        {
            if (canvasTool != CanvasTool.None || !HasCanvasLayers || evt.button != 0 || evt.altKey ||
                evt.pressedButtons != 1 || evt.target != toolkitCanvas ||
                !toolkitCanvas.contentRect.Contains(evt.localPosition) ||
                (canvasZoomManipulator?.IsDragging ?? false) || (canvasGuideManipulator?.IsDragging ?? false)) return false;
            WhimTexUI.ConsumeEvent(evt);
            Focus();
            toolkitCanvas.Focus();
            Rect image = toolkitCanvas.ImageRect;
            if (image.width <= 0f || image.height <= 0f) return true;
            Vector2 point = toolkitCanvas.ToCanvas(evt.localPosition);
            Vector2 uv = new Vector2((point.x - image.x) / image.width, 1f - (point.y - image.y) / image.height);
            if (tiledCanvas) uv = new Vector2(Mathf.Repeat(uv.x, 1f), Mathf.Repeat(uv.y, 1f));
            try
            {
                Layer hit = activeDocument.PickLayerAtPixel(Mathf.FloorToInt(uv.x * activeDocument.width), Mathf.FloorToInt(uv.y * activeDocument.height),
                    WhimTexUserSettings.LayerPickAlphaThreshold, evt.ctrlKey || evt.commandKey,
                    !evt.shiftKey && selectedLayerIds.Count == 1 ? GetSelectedLayer() : null);
                if (evt.shiftKey)
                {
                    if (hit == null) return true;
                    ResetOpacityEntry();
                    if (IsLayerSelected(hit.Id))
                    {
                        selectedLayerIds.Remove(hit.Id);
                        selectedLayerId = selectedLayerIds.Count > 0 ? selectedLayerIds[selectedLayerIds.Count - 1] : null;
                    }
                    else ActivateSelectedLayer(hit.Id);
                    selectionAnchorId = selectedLayerId;
                }
                else SelectOnlyLayer(hit?.Id);
                if (hit != null && activeDocument.TryFindLayer(hit, out var container, out _))
                    while (activeDocument.TryFindParentGroup(container, out Layer parent, out var ancestors, out _))
                    {
                        groupExpansion[parent.Id] = true;
                        container = ancestors;
                    }
                RefreshToolkitInterface();
                if (hit != null)
                    for (int i = 0; i < toolkitLayerHierarchyRoot.childCount; i++)
                        if (toolkitLayerHierarchyRoot[i].userData as string == hit.Id)
                        {
                            toolkitSettingsScroll.ScrollTo(toolkitLayerHierarchyRoot[i]);
                            break;
                        }
            }
            catch (Exception exception) { ShowNotification(new GUIContent("Cannot select layer: " + exception.Message)); }
            return true;
        }
    }
}
