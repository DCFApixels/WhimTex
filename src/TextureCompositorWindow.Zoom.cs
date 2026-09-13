using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private PreviewViewport previewViewport = new PreviewViewport();
        private PreviewZoomManipulator previewZoomManipulator;
        private FloatField previewZoomPercent;
        private FloatField previewRotationField;
        private Button previewRotationReset;
        private float displayedPreviewScale = float.NaN;
        private float displayedPreviewRotation = float.NaN;
        private bool IsPreviewZoomEnabled => previewTool == PreviewTool.Zoom && compositor != null;

        private void BuildPreviewZoomTool()
        {
            previewZoomManipulator = new PreviewZoomManipulator(this);
            toolkitPreviewCanvas.AddManipulator(previewZoomManipulator);
            float lastPixelScale = toolkitPreviewCanvas.PixelScale;
            toolkitPreviewCanvas.ViewChanged += () =>
            {
                bool scaleChanged = lastPixelScale != toolkitPreviewCanvas.PixelScale;
                lastPixelScale = toolkitPreviewCanvas.PixelScale;
                RefreshPreviewZoomReadout();
                RefreshPreviewTransformTool();
                RefreshPreviewPointerCursor();
                if (scaleChanged && postFxSettings != null && postFxSettings.linkDistanceToZoom) postFxDirty = true;
            };
        }

        private void AddPreviewZoomSettings()
        {
            VisualElement row = SpriteEditorUI.CreateToolbar();
            row.AddToClassList("sprite-editor-zoom-settings");
            BindPreviewSettingsRow(row, PreviewTool.Zoom);
            previewZoomPercent = new FloatField("Zoom %")
            {
                isDelayed = true,
                tooltip = "Preview scale in percent. Zoom around the center of the view without changing its rotation."
            };
            displayedPreviewScale = float.NaN;
            displayedPreviewRotation = float.NaN;
            previewZoomPercent.AddToClassList("sprite-editor-zoom-percent");
            previewZoomPercent.AddToClassList("sprite-editor-view-field");
            previewZoomPercent.RegisterValueChangedCallback(evt =>
            {
                SetPreviewZoomPercent(evt.newValue);
                previewZoomPercent.SetValueWithoutNotify(toolkitPreviewCanvas.PixelScale * 100f);
            });
            toolkitHeaderBindings.Add(RefreshPreviewZoomReadout);
            row.Add(previewZoomPercent);
            row.Add(SpriteEditorUI.CreateButton("Fit", () => ChangePreviewZoom(true)));
            row.Add(SpriteEditorUI.CreateButton("100%", () => ChangePreviewZoom(false)));
            previewRotationField = new FloatField("Angle °")
            {
                isDelayed = true,
                tooltip = "View rotation in degrees. Enter an exact angle; no snapping is applied."
            };
            previewRotationField.AddToClassList("sprite-editor-view-field");
            previewRotationField.RegisterValueChangedCallback(evt =>
            {
                SetPreviewRotation(evt.newValue);
                previewRotationField.SetValueWithoutNotify(previewViewport.Rotation);
            });
            row.Add(previewRotationField);
            previewRotationReset = SpriteEditorUI.CreateButton("0°", () =>
            {
                SetPreviewRotation(0f);
                toolkitPreviewCanvas.Focus();
            });
            previewRotationReset.tooltip = "Reset view rotation to 0°.";
            row.Add(previewRotationReset);
            toolkitPreviewHeader.Add(row);
        }

        private void SetPreviewZoomPercent(float percent)
        {
            if (!HasPreviewLayers || percent <= 0f || float.IsNaN(percent) || float.IsInfinity(percent)) return;
            CancelPreviewZoomGesture();
            FinishPreviewTransform();
            FinishPaintingStroke();
            toolkitPreviewCanvas.ZoomAt(toolkitPreviewCanvas.contentRect.center, percent / 100f);
        }

        private void SetPreviewRotation(float degrees)
        {
            if (!HasPreviewLayers || float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            CancelPreviewZoomGesture();
            FinishPreviewTransform();
            FinishPaintingStroke();
            toolkitPreviewCanvas.SetViewRotation(degrees, snap: false);
        }

        private void RefreshPreviewZoomReadout()
        {
            if (previewZoomPercent == null || toolkitPreviewCanvas == null)
                return;
            float scale = toolkitPreviewCanvas.PixelScale;
            if (previewRotationField != null)
            {
                if (displayedPreviewRotation != previewViewport.Rotation)
                {
                    displayedPreviewRotation = previewViewport.Rotation;
                    previewRotationField.SetValueWithoutNotify(displayedPreviewRotation);
                }
                previewRotationField.SetEnabled(HasPreviewLayers);
            }
            previewRotationReset?.SetEnabled(HasPreviewLayers && previewViewport.Rotation != 0f);
            previewZoomPercent.SetEnabled(HasPreviewLayers);
            if (scale == displayedPreviewScale)
                return;
            displayedPreviewScale = scale;
            previewZoomPercent.SetValueWithoutNotify(scale * 100f);
        }

        private void ChangePreviewZoom(bool fit)
        {
            if (!HasPreviewLayers) return;
            CancelPreviewZoomGesture();
            FinishPreviewTransform();
            FinishPaintingStroke();
            if (fit) previewViewport.Reset();
            else toolkitPreviewCanvas.ZoomAt(toolkitPreviewCanvas.contentRect.center, 1f);
            toolkitPreviewCanvas.UpdateImageLayout();
            toolkitPreviewCanvas.Focus();
        }

        private void CancelPreviewZoomGesture()
        {
            previewGuideManipulator?.Cancel();
            shapePicker?.Cancel();
            marqueePicker?.Cancel();
            shapeManipulator?.Cancel();
            previewZoomManipulator?.Cancel();
        }

        private sealed class PreviewZoomManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private int pointerId = -1;
            private bool panning, rotating, zoomOut;
            private float freeRotation;
            private Vector2 start, current;
            private readonly VisualElement selection;
            internal bool IsDragging => pointerId >= 0;
            internal bool IsRotating => IsDragging && rotating;
            internal bool IsNavigating => IsDragging && panning;

            internal PreviewZoomManipulator(TextureCompositorWindow owner)
            {
                this.owner = owner;
                selection = new VisualElement { pickingMode = PickingMode.Ignore };
                selection.AddToClassList("sprite-editor-zoom-selection");
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
                if (captured >= 0) owner.RefreshPreviewPointerCursor();
            }

            private void OnCaptureOut(PointerCaptureOutEvent evt) { if (evt.pointerId == pointerId) Cancel(); }
            private void OnCancel(PointerCancelEvent evt) { if (evt.pointerId == pointerId) Cancel(); }
            private void OnDetach(DetachFromPanelEvent evt)
            {
                owner.ClearPreviewPointerCursor();
                Cancel();
            }
            private void OnGeometry(GeometryChangedEvent evt) => Cancel();

            private void OnDown(PointerDownEvent evt)
            {
                if (IsDragging)
                {
                    SpriteEditorUI.ConsumeEvent(evt);
                    return;
                }
                if (!owner.HasPreviewLayers || (evt.button != 2 && !(evt.button == 0 && owner.IsPreviewZoomEnabled)) ||
                    !target.contentRect.Contains(evt.localPosition)) return;
                owner.CancelPreviewEyedropper();
                owner.shapeManipulator?.Cancel();
                owner.FinishPreviewTransform();
                owner.FinishPaintingStroke();
                if (owner.areaSelectionManipulator?.RectangleDragging == true)
                    owner.areaSelectionManipulator.Cancel();
                owner.Focus();
                target.Focus();
                start = current = evt.localPosition;
                panning = evt.button == 2;
                rotating = panning && evt.shiftKey;
                freeRotation = owner.previewViewport.Rotation;
                zoomOut = evt.altKey;
                pointerId = evt.pointerId;
                target.CapturePointer(pointerId);
                owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                SpriteEditorUI.ConsumeEvent(evt);
            }

            private void OnWheel(WheelEvent evt)
            {
                Vector2 point = target.WorldToLocal(evt.mousePosition);
                if (!owner.HasPreviewLayers || !target.contentRect.Contains(point) || evt.delta.y == 0f ||
                    float.IsNaN(evt.delta.y) || float.IsInfinity(evt.delta.y)) return;
                SpriteEditorUI.ConsumeEvent(evt);
                if (IsDragging && !panning) Cancel();
                owner.shapeManipulator?.Cancel();
                owner.FinishPreviewTransform();
                owner.FinishPaintingStroke();
                SpritePreviewElement canvas = owner.toolkitPreviewCanvas;
                canvas.ZoomAt(point, PreviewViewport.WheelScale(canvas.PixelScale, evt.delta.y));
                if (IsNavigating) current = point;
                owner.UpdatePreviewCursor(point, evt.altKey);
            }

            private void OnMove(PointerMoveEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointerId) return;
                if ((evt.pressedButtons & (panning ? 4 : 1)) == 0)
                {
                    Cancel();
                    owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                    evt.StopImmediatePropagation();
                    return;
                }
                Vector2 point = evt.localPosition;
                if (rotating) RotateTo(point, evt.ctrlKey);
                else if (panning) owner.toolkitPreviewCanvas.Pan(point - current);
                current = point;
                owner.UpdatePreviewCursor(point, evt.altKey);
                selection.MarkDirtyRepaint();
                evt.StopImmediatePropagation();
            }

            private void OnUp(PointerUpEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointerId || evt.button != (panning ? 2 : 0)) return;
                if (rotating) RotateTo(evt.localPosition, evt.ctrlKey);
                else if (panning) owner.toolkitPreviewCanvas.Pan((Vector2)evt.localPosition - current);
                current = evt.localPosition;
                if (!panning)
                {
                    SpritePreviewElement canvas = owner.toolkitPreviewCanvas;
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
                owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                SpriteEditorUI.ConsumeEvent(evt);
            }

            private void RotateTo(Vector2 point, bool disableSnap)
            {
                Vector2 pivot = target.contentRect.center;
                Vector2 from = current - pivot, to = point - pivot;
                // Avoid an unstable angle when the pointer passes through the pivot.
                if (from.sqrMagnitude >= 144f && to.sqrMagnitude >= 144f)
                    freeRotation += Vector2.SignedAngle(from, to);
                float rotation = disableSnap ? freeRotation : owner.SnapPreviewGuideRotation(freeRotation, includeCanvasAxes: true);
                owner.toolkitPreviewCanvas.SetViewRotation(rotation, snap: false);
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
