using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private CanvasViewport canvasViewport = new CanvasViewport();
        private CanvasZoomManipulator canvasZoomManipulator;
        private FloatField canvasZoomPercent;
        private FloatField canvasRotationField;
        private Button canvasRotationReset;
        private float displayedCanvasScale = float.NaN;
        private float displayedCanvasRotation = float.NaN;
        private bool IsCanvasZoomEnabled => canvasTool == CanvasTool.Zoom && activeDocument != null;

        private void BuildCanvasZoomTool()
        {
            canvasZoomManipulator = new CanvasZoomManipulator(this);
            toolkitCanvas.AddManipulator(canvasZoomManipulator);
            float lastPixelScale = toolkitCanvas.PixelScale;
            toolkitCanvas.ViewChanged += () =>
            {
                bool scaleChanged = lastPixelScale != toolkitCanvas.PixelScale;
                lastPixelScale = toolkitCanvas.PixelScale;
                RefreshCanvasZoomReadout();
                RefreshCanvasTransformTool();
                RefreshCanvasPointerCursor();
                if (scaleChanged && postFxSettings != null && postFxSettings.linkDistanceToZoom) postFxDirty = true;
            };
        }

        private void AddCanvasZoomSettings()
        {
            VisualElement row = CreateCanvasSettingsRow();
            row.AddToClassList("whimtex-zoom-settings");
            BindCanvasSettingsRow(row, CanvasTool.Zoom);
            canvasZoomPercent = new FloatField("Zoom %")
            {
                isDelayed = true,
                tooltip = "Canvas zoom in percent. Zoom around the center of the view without changing its rotation."
            };
            displayedCanvasScale = float.NaN;
            displayedCanvasRotation = float.NaN;
            canvasZoomPercent.AddToClassList("whimtex-zoom-percent");
            canvasZoomPercent.AddToClassList("whimtex-view-field");
            canvasZoomPercent.RegisterValueChangedCallback(evt =>
            {
                SetCanvasZoomPercent(evt.newValue);
                canvasZoomPercent.SetValueWithoutNotify(toolkitCanvas.PixelScale * 100f);
            });
            toolkitHeaderBindings.Add(RefreshCanvasZoomReadout);
            row.Add(canvasZoomPercent);
            row.Add(WhimTexUI.CreateButton("Fit", () => ChangeCanvasZoom(true)));
            row.Add(WhimTexUI.CreateButton("100%", () => ChangeCanvasZoom(false)));
            canvasRotationField = new FloatField("Angle °")
            {
                isDelayed = true,
                tooltip = "View rotation in degrees. Enter an exact angle; no snapping is applied."
            };
            canvasRotationField.AddToClassList("whimtex-view-field");
            canvasRotationField.RegisterValueChangedCallback(evt =>
            {
                SetCanvasRotation(evt.newValue);
                canvasRotationField.SetValueWithoutNotify(canvasViewport.Rotation);
            });
            row.Add(canvasRotationField);
            canvasRotationReset = WhimTexUI.CreateButton("0°", () =>
            {
                SetCanvasRotation(0f);
                toolkitCanvas.Focus();
            });
            canvasRotationReset.tooltip = "Reset view rotation to 0°.";
            row.Add(canvasRotationReset);
            toolkitCanvasViewHeader.Add(row);
        }

        private void SetCanvasZoomPercent(float percent)
        {
            if (!HasCanvasLayers || percent <= 0f || float.IsNaN(percent) || float.IsInfinity(percent)) return;
            CancelCanvasZoomGesture();
            FinishCanvasTransform();
            FinishPaintingStroke();
            toolkitCanvas.ZoomAt(toolkitCanvas.contentRect.center, percent / 100f);
        }

        private void SetCanvasRotation(float degrees)
        {
            if (!HasCanvasLayers || float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            CancelCanvasZoomGesture();
            FinishCanvasTransform();
            FinishPaintingStroke();
            toolkitCanvas.SetViewRotation(degrees, snap: false);
        }

        private void RefreshCanvasZoomReadout()
        {
            if (canvasZoomPercent == null || toolkitCanvas == null)
                return;
            float scale = toolkitCanvas.PixelScale;
            if (canvasRotationField != null)
            {
                if (displayedCanvasRotation != canvasViewport.Rotation)
                {
                    displayedCanvasRotation = canvasViewport.Rotation;
                    canvasRotationField.SetValueWithoutNotify(displayedCanvasRotation);
                }
                canvasRotationField.SetEnabled(HasCanvasLayers);
            }
            canvasRotationReset?.SetEnabled(HasCanvasLayers && canvasViewport.Rotation != 0f);
            canvasZoomPercent.SetEnabled(HasCanvasLayers);
            if (scale == displayedCanvasScale)
                return;
            displayedCanvasScale = scale;
            canvasZoomPercent.SetValueWithoutNotify(scale * 100f);
        }

        private void ChangeCanvasZoom(bool fit)
        {
            if (!HasCanvasLayers) return;
            CancelCanvasZoomGesture();
            FinishCanvasTransform();
            FinishPaintingStroke();
            if (fit) canvasViewport.Reset();
            else toolkitCanvas.ZoomAt(toolkitCanvas.contentRect.center, 1f);
            toolkitCanvas.UpdateImageLayout();
            toolkitCanvas.Focus();
        }

        private void CancelCanvasZoomGesture()
        {
            canvasGuideManipulator?.Cancel();
            shapePicker?.Cancel();
            marqueePicker?.Cancel();
            shapeManipulator?.Cancel();
            canvasZoomManipulator?.Cancel();
        }

        private sealed class CanvasZoomManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private int pointerId = -1;
            private bool panning, rotating, zoomOut;
            private float freeRotation;
            private Vector2 start, current;
            private readonly VisualElement selection;
            internal bool IsDragging => pointerId >= 0;
            internal bool IsRotating => IsDragging && rotating;
            internal bool IsNavigating => IsDragging && panning;

            internal CanvasZoomManipulator(WhimTexWindow owner)
            {
                this.owner = owner;
                selection = new VisualElement { pickingMode = PickingMode.Ignore };
                selection.AddToClassList("whimtex-zoom-selection");
                selection.generateVisualContent += DrawSelection;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.Add(selection);
                target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                target.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.RegisterCallback<PointerCancelEvent>(OnCancel);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
                target.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                target.UnregisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.UnregisterCallback<PointerCancelEvent>(OnCancel);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                target.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                selection.RemoveFromHierarchy();
            }

            internal void Cancel()
            {
                int captured = pointerId;
                pointerId = -1;
                if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                selection.MarkDirtyRepaint();
                if (captured >= 0) owner.RefreshCanvasPointerCursor();
            }

            private void OnCaptureOut(PointerCaptureOutEvent evt) { if (evt.pointerId == pointerId) Cancel(); }
            private void OnCancel(PointerCancelEvent evt) { if (evt.pointerId == pointerId) Cancel(); }
            private void OnDetach(DetachFromPanelEvent evt)
            {
                owner.ClearCanvasPointerCursor();
                Cancel();
            }
            private void OnGeometry(GeometryChangedEvent evt) => Cancel();

            private void OnDown(PointerDownEvent evt)
            {
                if (IsDragging)
                {
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (!owner.HasCanvasLayers || (evt.button != 2 && !(evt.button == 0 && owner.IsCanvasZoomEnabled)) ||
                    !target.contentRect.Contains(evt.localPosition)) return;
                owner.CancelCanvasEyedropper();
                owner.canvasGuideManipulator?.Cancel();
                owner.shapeManipulator?.Cancel();
                owner.FinishCanvasTransform();
                owner.FinishPaintingStroke();
                if (owner.areaSelectionManipulator?.RectangleDragging == true)
                    owner.areaSelectionManipulator.Cancel();
                owner.Focus();
                target.Focus();
                start = current = evt.localPosition;
                panning = evt.button == 2;
                rotating = panning && evt.shiftKey;
                freeRotation = owner.canvasViewport.Rotation;
                zoomOut = evt.altKey;
                pointerId = evt.pointerId;
                target.CapturePointer(pointerId);
                owner.UpdateCanvasCursor(evt.localPosition, evt.altKey);
                WhimTexUI.ConsumeEvent(evt);
            }

            private void OnWheel(WheelEvent evt)
            {
                Vector2 point = target.WorldToLocal(evt.mousePosition);
                if (!owner.HasCanvasLayers || !target.contentRect.Contains(point) || evt.delta.y == 0f ||
                    float.IsNaN(evt.delta.y) || float.IsInfinity(evt.delta.y)) return;
                WhimTexUI.ConsumeEvent(evt);
                owner.canvasGuideManipulator?.Cancel();
                if (IsDragging && !panning) Cancel();
                owner.shapeManipulator?.Cancel();
                owner.FinishCanvasTransform();
                owner.FinishPaintingStroke();
                CanvasElement canvas = owner.toolkitCanvas;
                canvas.ZoomAt(point, CanvasViewport.WheelScale(canvas.PixelScale, evt.delta.y));
                if (IsNavigating) current = point;
                owner.UpdateCanvasCursor(point, evt.altKey);
            }

            private void OnMove(PointerMoveEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointerId) return;
                if ((evt.pressedButtons & (panning ? 4 : 1)) == 0)
                {
                    Cancel();
                    owner.UpdateCanvasCursor(evt.localPosition, evt.altKey);
                    evt.StopImmediatePropagation();
                    return;
                }
                Vector2 point = evt.localPosition;
                if (rotating) RotateTo(point, evt.ctrlKey);
                else if (panning) owner.toolkitCanvas.Pan(point - current);
                current = point;
                owner.UpdateCanvasCursor(point, evt.altKey);
                selection.MarkDirtyRepaint();
                evt.StopImmediatePropagation();
            }

            private void OnUp(PointerUpEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointerId || evt.button != (panning ? 2 : 0)) return;
                if (rotating) RotateTo(evt.localPosition, evt.ctrlKey);
                else if (panning) owner.toolkitCanvas.Pan((Vector2)evt.localPosition - current);
                current = evt.localPosition;
                if (!panning)
                {
                    CanvasElement canvas = owner.toolkitCanvas;
                    Rect region = SelectionRect();
                    if (zoomOut || evt.altKey)
                    {
                        if ((current - start).sqrMagnitude < 16f)
                            canvas.ZoomAt(start, canvas.PixelScale * 0.5f);
                    }
                    else if (region.width >= 4f && region.height >= 4f)
                        canvas.Frame(region);
                    else if ((current - start).sqrMagnitude < 16f)
                        canvas.ZoomAt(start, canvas.PixelScale * 2f);
                }
                Cancel();
                owner.UpdateCanvasCursor(evt.localPosition, evt.altKey);
                WhimTexUI.ConsumeEvent(evt);
            }

            private void RotateTo(Vector2 point, bool disableSnap)
            {
                Vector2 pivot = target.contentRect.center;
                Vector2 from = current - pivot, to = point - pivot;
                // Avoid an unstable angle when the pointer passes through the pivot.
                if (from.sqrMagnitude >= 144f && to.sqrMagnitude >= 144f)
                    freeRotation += Vector2.SignedAngle(from, to);
                float rotation = disableSnap ? freeRotation : owner.SnapCanvasGuideRotation(freeRotation, includeCanvasAxes: true);
                owner.toolkitCanvas.SetViewRotation(rotation, snap: false);
            }

            private Rect SelectionRect()
            {
                Rect bounds = target.contentRect;
                float xMin = Mathf.Max(Mathf.Min(start.x, current.x), bounds.xMin);
                float yMin = Mathf.Max(Mathf.Min(start.y, current.y), bounds.yMin);
                float xMax = Mathf.Min(Mathf.Max(start.x, current.x), bounds.xMax);
                float yMax = Mathf.Min(Mathf.Max(start.y, current.y), bounds.yMax);
                return new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
            }

            private void DrawSelection(MeshGenerationContext context)
            {
                if (!IsDragging || panning || zoomOut) return;
                Rect rect = SelectionRect();
                if (rect.width < 4f || rect.height < 4f) return;
                Painter2D painter = context.painter2D;
                painter.fillColor = new Color(0.2f, 0.65f, 1f, 0.12f);
                painter.strokeColor = new Color(0.3f, 0.75f, 1f, 1f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(rect.min);
                painter.LineTo(new Vector2(rect.xMax, rect.yMin));
                painter.LineTo(rect.max);
                painter.LineTo(new Vector2(rect.xMin, rect.yMax));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }
        }
    }
}
