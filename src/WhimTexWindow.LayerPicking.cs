using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private WhimTexDocument lastLayerPickDocument;
        private Layer lastPickedLayer;
        private Vector2 lastLayerPickPosition;
        private bool lastLayerPickInsideGroups;

        private Layer GetLayerPickContinuation(Vector2 position, bool insideGroups, bool shift)
        {
            return !shift && lastLayerPickDocument == activeDocument && lastLayerPickInsideGroups == insideGroups &&
                selectedLayerIds.Count == 1 && GetSelectedLayer() == lastPickedLayer &&
                (position - lastLayerPickPosition).sqrMagnitude <= 16f ? lastPickedLayer : null;
        }

        private void AddLayerPickSettings(VisualElement row)
        {
            var threshold = new FloatField("Alpha ≥ %")
            {
                tooltip = "Minimum alpha for pixel picking and layer/group opacity. Vector geometry can also be picked independently of fill alpha. Default: 10%. Shift toggles selection; Ctrl selects inside groups. Shared with User Settings."
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
            if (canvasGuideManipulator?.WantsPointer(evt.localPosition, evt.ctrlKey, evt.altKey) == true) return false;
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
                bool insideGroups = evt.ctrlKey || evt.commandKey;
                Layer continuation = GetLayerPickContinuation(evt.localPosition, insideGroups, evt.shiftKey);
                Vector2 geometryPoint = tiledCanvas ? toolkitCanvas.ToView(new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height)) : evt.localPosition;
                Layer hit = activeDocument.PickLayerAtPixelWithGeometry(Mathf.FloorToInt(uv.x * activeDocument.width), Mathf.FloorToInt(uv.y * activeDocument.height),
                    WhimTexUserSettings.LayerPickAlphaThreshold, insideGroups,
                    layer => layer.Behaviour is VectorLayerBehaviour vector && PickVectorGeometry(vector, geometryPoint,
                        p => VectorLayerPointToView(layer, p)) != null,
                    continuation, cycle: true);
                lastLayerPickDocument = activeDocument;
                lastPickedLayer = evt.shiftKey ? null : hit;
                lastLayerPickPosition = evt.localPosition;
                lastLayerPickInsideGroups = insideGroups;
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

        private Vector2 VectorLayerPointToView(Layer layer, Vector2 local)
        {
            Rect image = toolkitCanvas.ImageRect;
            Vector2 p = layer.RenderTransform.Map(local, new Vector2(activeDocument.width, activeDocument.height));
            return toolkitCanvas.ToView(new Vector2(image.x + p.x * image.width, image.yMax - p.y * image.height));
        }

        private static VectorShape PickVectorGeometry(VectorLayerBehaviour vector, Vector2 view, Func<Vector2, Vector2> toView)
        {
            for (int s = vector.shapes.Count - 1; s >= 0; s--)
            {
                var shape = vector.shapes[s];
                if (!shape.visible) continue;
                int crossings = 0, winding = 0;
                foreach (var contour in shape.contours)
                {
                    int segments = contour.points.Count - (contour.closed ? 0 : 1);
                    for (int i = 0; i < segments; i++)
                    {
                        var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count];
                        Vector2 previous = toView(a.position);
                        for (int j = 1; j <= 24; j++)
                        {
                            Vector2 current = toView(VectorGeometry.EvaluateCubic(a.position, a.position + a.outTangent,
                                b.position + b.inTangent, b.position, j / 24f));
                            if (VectorGeometry.DistanceSquared(view, previous, current, out _) < 36f) return shape;
                            if (contour.closed && (previous.y > view.y) != (current.y > view.y) &&
                                view.x < (current.x - previous.x) * (view.y - previous.y) / (current.y - previous.y) + previous.x)
                            { crossings++; winding += current.y > previous.y ? 1 : -1; }
                            previous = current;
                        }
                    }
                }
                if (shape.fillMode != VectorFillMode.None &&
                    (shape.fillRule == VectorFillRule.EvenOdd ? (crossings & 1) != 0 : winding != 0)) return shape;
            }
            return null;
        }

        private void DrawSelectedVectorContours(Painter2D painter)
        {
            if (activeDocument == null || toolkitCanvas.ImageRect.width <= 0f) return;
            painter.lineWidth = 1.2f;
            painter.strokeColor = new Color(.25f, .8f, 1f);
            foreach (string id in selectedLayerIds)
            {
                var layer = activeDocument.FindLayer(id);
                if (layer?.Behaviour is not VectorLayerBehaviour vector || !layer.enabled) continue;
                foreach (var shape in vector.shapes)
                {
                    if (!shape.visible) continue;
                    foreach (var contour in shape.contours)
                    {
                        if (contour.points.Count == 0) continue;
                        painter.BeginPath(); painter.MoveTo(VectorLayerPointToView(layer, contour.points[0].position));
                        for (int i = 0; i < contour.points.Count - (contour.closed ? 0 : 1); i++)
                        {
                            var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count];
                            painter.BezierCurveTo(VectorLayerPointToView(layer, a.position + a.outTangent),
                                VectorLayerPointToView(layer, b.position + b.inTangent), VectorLayerPointToView(layer, b.position));
                        }
                        if (contour.closed) painter.ClosePath();
                        painter.Stroke();
                    }
                }
            }
        }
    }
}
