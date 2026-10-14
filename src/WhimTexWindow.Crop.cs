using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private enum CanvasCropMode { Scale, Resize }
        [SerializeField] private CanvasCropMode canvasCropMode = CanvasCropMode.Resize;
        [SerializeField] private bool canvasCropAutoPixelScale = true;
        private Button canvasCropButton;
        private CropManipulator cropManipulator;
        private CanvasChangeSelectionUndo canvasChangeSelectionUndo;
        private int canvasChangeSelectionRevision, observedCanvasChangeSelectionRevision;

        private void SetCanvasCropMode(CanvasCropMode mode)
        {
            if (mode != CanvasCropMode.Scale && mode != CanvasCropMode.Resize) return;
            if (canvasCropMode != mode)
            {
                cropManipulator?.Reset();
                canvasCropMode = mode;
                ResetCanvasCropFrame();
            }
            RefreshCanvasToolToolbar();
            toolkitHeaderBindings.Refresh();
            UpdateToolkitCanvasPresentation();
            toolkitCanvas?.Focus();
        }

        private void BuildCanvasCropTool()
        {
            cropManipulator?.Reset();
            cropManipulator = new CropManipulator(this);
            toolkitCanvas.AddManipulator(cropManipulator);
        }

        private RectInt GetCanvasCropFrame() => cropManipulator?.Frame ??
            new RectInt(0, 0, activeDocument != null ? activeDocument.width : 1,
                activeDocument != null ? activeDocument.height : 1);

        private float GetCanvasCropPixelScale() => cropManipulator?.PixelScale ?? activeDocument?.GetPixelScale() ?? 1f;

        private void ResetCanvasCropFrame()
        {
            cropManipulator?.Reset();
            toolkitHeaderBindings.Refresh();
        }

        private void RefreshCanvasCropTool()
        {
            cropManipulator?.Refresh();
            if (canvasTool == CanvasTool.Crop) RefreshCanvasCropSnapTargets();
        }

        private void AddCanvasCropSettings()
        {
            var row = CreateCanvasSettingsRow();
            BindCanvasSettingsRow(row, CanvasTool.Crop);
            var mode = new EnumField(canvasCropMode) { name = "canvasCropMode", tooltip =
                "Scale stretches the entire image. Resize changes its canvas boundary without stretching the content." };
            TwoChoiceDropdown.Attach(mode);
            mode.AddToClassList("whimtex-shape-kind");
            toolkitHeaderBindings.Track(mode, () => (Enum)canvasCropMode);
            mode.RegisterValueChangedCallback(evt => SetCanvasCropMode((CanvasCropMode)evt.newValue));
            row.Add(mode);
            IntegerField Pixels(string label, string name, Func<RectInt, int> get, Func<RectInt, int, RectInt> set)
            {
                var field = new IntegerField(label) { name = name, isDelayed = true };
                field.AddToClassList("whimtex-view-field");
                toolkitHeaderBindings.Track(field, () => get(GetCanvasCropFrame()));
                field.RegisterValueChangedCallback(evt =>
                {
                    cropManipulator?.SetFrame(set(GetCanvasCropFrame(), evt.newValue));
                    field.SetValueWithoutNotify(get(GetCanvasCropFrame()));
                });
                row.Add(field);
                return field;
            }
            var x = Pixels("X", "cropX", r => r.x, (r, v) => new RectInt(v, r.y, r.width, r.height));
            x.tooltip =
                "Left edge in canvas pixels. Negative values expand beyond the canvas.";
            var y = Pixels("Y", "cropY", r => activeDocument != null ? activeDocument.height - r.yMax : 0,
                (r, v) => new RectInt(r.x, activeDocument != null ? activeDocument.height - v - r.height : r.y, r.width, r.height));
            y.tooltip =
                "Top edge in canvas pixels. Negative values expand above the canvas.";
            Pixels("W", "cropWidth", r => r.width, (r, v) => new RectInt(r.x, r.y,
                Mathf.Clamp(v, 1, Mathf.Min(WhimTexDocument.MaximumCanvasFrameDimension,
                    (int)(WhimTexDocument.MaximumCanvasFramePixels / Mathf.Max(1, r.height)))), r.height)).tooltip =
                "New canvas width in pixels.";
            Pixels("H", "cropHeight", r => r.height, (r, v) =>
            {
                int height = Mathf.Clamp(v, 1, Mathf.Min(WhimTexDocument.MaximumCanvasFrameDimension,
                    (int)(WhimTexDocument.MaximumCanvasFramePixels / Mathf.Max(1, r.width))));
                return new RectInt(r.x, r.yMax - height, r.width, height);
            }).tooltip =
                "New canvas height in pixels.";
            var pixelScale = new FloatField("Pixel Scale")
            {
                name = "cropPixelScale", isDelayed = true,
                tooltip = "Canvas pixels per logical pixel. Scale pixel-sized strokes, text, brushes and effect distances."
            };
            pixelScale.AddToClassList("whimtex-view-field");
            toolkitHeaderBindings.Track(pixelScale, GetCanvasCropPixelScale);
            pixelScale.RegisterValueChangedCallback(evt =>
            {
                cropManipulator?.SetPixelScale(evt.newValue);
                pixelScale.SetValueWithoutNotify(GetCanvasCropPixelScale());
            });
            row.Add(pixelScale);
            var autoPixelScale = new Toggle("Auto")
            {
                name = "cropAutoPixelScale",
                tooltip = "Multiply the applied Pixel Scale by the smaller width or height scale factor."
            };
            toolkitHeaderBindings.Track(autoPixelScale, () => canvasCropAutoPixelScale);
            autoPixelScale.RegisterValueChangedCallback(evt =>
            {
                cropManipulator?.CancelGesture();
                float current = GetCanvasCropPixelScale();
                canvasCropAutoPixelScale = evt.newValue;
                if (!evt.newValue) cropManipulator?.SetPixelScale(current);
                toolkitHeaderBindings.Refresh();
            });
            row.Add(autoPixelScale);
            var apply = WhimTexUI.CreateToolbarButton("Apply", ApplyCanvasCrop);
            var reset = WhimTexUI.CreateToolbarButton("Reset", ResetCanvasCropFrame);
            apply.name = "cropApply"; reset.name = "cropReset";
            row.Add(apply); row.Add(reset);
            var actions = new VisualElement { name = "canvasCropActions" };
            actions.AddToClassList("whimtex-transform-actions");
            var reflection = CreateCanvasTransformActionGroup("canvasCropReflectionGroup", separated: true);
            var rotation = CreateCanvasTransformActionGroup("canvasCropRotationGroup", separated: true);
            AddCanvasCropOrientationAction(reflection, CanvasOrientation.FlipHorizontal, LayerActionIcon.Kind.FlipHorizontal,
                "Flip Canvas Horizontal", "Reflect the entire canvas horizontally.");
            AddCanvasCropOrientationAction(reflection, CanvasOrientation.FlipVertical, LayerActionIcon.Kind.FlipVertical,
                "Flip Canvas Vertical", "Reflect the entire canvas vertically.");
            AddCanvasCropOrientationAction(rotation, CanvasOrientation.RotateLeft, LayerActionIcon.Kind.RotateLeft,
                "Rotate Canvas Left 90°", "Rotate the entire canvas counterclockwise. Swap width and height.");
            AddCanvasCropOrientationAction(rotation, CanvasOrientation.RotateRight, LayerActionIcon.Kind.RotateRight,
                "Rotate Canvas Right 90°", "Rotate the entire canvas clockwise. Swap width and height.");
            actions.Add(reflection); actions.Add(rotation); row.Add(actions);
            toolkitHeaderBindings.Add(() =>
            {
                bool scale = canvasCropMode == CanvasCropMode.Scale;
                x.EnableInClassList("whimtex-hidden", scale);
                y.EnableInClassList("whimtex-hidden", scale);
                autoPixelScale.EnableInClassList("whimtex-hidden", !scale);
                pixelScale.SetEnabled(activeDocument != null && !(scale && canvasCropAutoPixelScale) && cropManipulator?.IsDragging != true);
                autoPixelScale.SetEnabled(activeDocument != null && cropManipulator?.IsDragging != true);
                RectInt frame = GetCanvasCropFrame();
                bool changed = activeDocument != null && (GetCanvasCropPixelScale() != activeDocument.GetPixelScale() || (scale
                    ? frame.width != activeDocument.width || frame.height != activeDocument.height
                    : frame != new RectInt(0, 0, activeDocument.width, activeDocument.height)));
                apply.SetEnabled(changed && cropManipulator?.IsDragging != true);
                reset.SetEnabled(changed || cropManipulator?.IsDragging == true);
            });
            toolkitCanvasViewHeader.Add(row);
        }

        private void AddCanvasCropOrientationAction(VisualElement group, CanvasOrientation orientation,
            LayerActionIcon.Kind icon, string undoName, string tooltip)
        {
            var button = WhimTexUI.CreateButton(string.Empty, () => ApplyCanvasCropOrientation(orientation, undoName));
            SetCanvasTransformActionIcon(button, orientation.ToString(), icon);
            button.name = "canvasCrop" + orientation;
            button.tooltip = undoName + "\n" + tooltip;
            toolkitHeaderBindings.Add(() => button.SetEnabled(activeDocument != null && canvasTool == CanvasTool.Crop &&
                cropManipulator?.IsDragging != true));
            group.Add(button);
        }

        private void ApplyCanvasCrop()
        {
            var document = activeDocument;
            if (document == null || cropManipulator?.IsDragging == true) return;
            if (canvasCropMode == CanvasCropMode.Scale) { ApplyCanvasScale(); return; }
            RectInt frame = GetCanvasCropFrame();
            float pixelScale = GetCanvasCropPixelScale();
            if (frame == new RectInt(0, 0, document.width, document.height) && pixelScale == document.GetPixelScale()) return;
            FinishPaintingStroke(); FinishCanvasTransform();
            areaSelectionManipulator?.Cancel(); canvasGuideManipulator?.Cancel();
            byte[] coverage = null;
            if (areaSelectionOwner == document && areaSelection?.Active == true &&
                areaSelection.Width == document.width && areaSelection.Height == document.height)
            {
                coverage = new byte[frame.width * frame.height];
                int x0 = Mathf.Max(0, frame.x), y0 = Mathf.Max(0, frame.y);
                int x1 = Mathf.Min(document.width, frame.xMax), y1 = Mathf.Min(document.height, frame.yMax);
                for (int y = y0; y < y1; y++)
                    if (x1 > x0) Array.Copy(areaSelection.Coverage, y * document.width + x0,
                        coverage, (y - frame.y) * frame.width + x0 - frame.x, x1 - x0);
            }
            Vector2 topLeft = new Vector2(frame.x, document.height - frame.yMax);
            CommitCanvasChange("Crop Canvas", coverage, null, () =>
            {
                if (!document.ApplyCanvasFrame(frame, pixelScale)) return false;
                for (int i = 0; i < canvasGuides.Count; i++)
                {
                    CanvasGuide guide = canvasGuides[i];
                    guide.position -= Vector2.Dot(guide.normal, topLeft);
                    canvasGuides[i] = guide;
                }
                return true;
            });
        }

        private void ApplyCanvasScale()
        {
            var document = activeDocument;
            if (document == null || cropManipulator?.IsDragging == true) return;
            RectInt frame = GetCanvasCropFrame();
            int width = document.width, height = document.height;
            float pixelScale = GetCanvasCropPixelScale();
            if (frame.width == width && frame.height == height && pixelScale == document.GetPixelScale()) return;
            FinishPaintingStroke(); FinishCanvasTransform();
            areaSelectionManipulator?.Cancel(); canvasGuideManipulator?.Cancel();
            byte[] coverage = null;
            if (areaSelectionOwner == document && areaSelection?.Active == true &&
                areaSelection.Width == width && areaSelection.Height == height)
            {
                coverage = new byte[frame.width * frame.height];
                for (int y = 0; y < frame.height; y++)
                {
                    int sourceY = (int)((2L * y + 1) * height / (2L * frame.height));
                    for (int x = 0; x < frame.width; x++)
                    {
                        int sourceX = (int)((2L * x + 1) * width / (2L * frame.width));
                        coverage[y * frame.width + x] = areaSelection.Coverage[sourceY * width + sourceX];
                    }
                }
            }
            float sx = frame.width / (float)width, sy = frame.height / (float)height;
            CommitCanvasChange("Scale Canvas", coverage, null, () =>
            {
                if (!document.ApplyCanvasScale(frame.width, frame.height, pixelScale)) return false;
                for (int i = 0; i < canvasGuides.Count; i++)
                {
                    CanvasGuide guide = canvasGuides[i];
                    Vector2 normal = new Vector2(guide.normal.x / sx, guide.normal.y / sy);
                    float length = normal.magnitude;
                    guide.normal = normal / length;
                    guide.position /= length;
                    canvasGuides[i] = guide;
                }
                return true;
            });
        }

        private void ApplyCanvasCropOrientation(CanvasOrientation orientation, string undoName)
        {
            var document = activeDocument;
            if (document == null || canvasTool != CanvasTool.Crop || cropManipulator?.IsDragging == true) return;
            FinishPaintingStroke(); FinishCanvasTransform();
            areaSelectionManipulator?.Cancel(); canvasGuideManipulator?.Cancel();
            int width = document.width, height = document.height;
            RectInt frame = GetCanvasCropFrame(), nextFrame;
            switch (orientation)
            {
                case CanvasOrientation.FlipHorizontal: nextFrame = new RectInt(width - frame.xMax, frame.y, frame.width, frame.height); break;
                case CanvasOrientation.FlipVertical: nextFrame = new RectInt(frame.x, height - frame.yMax, frame.width, frame.height); break;
                case CanvasOrientation.RotateLeft: nextFrame = new RectInt(height - frame.yMax, frame.x, frame.height, frame.width); break;
                case CanvasOrientation.RotateRight: nextFrame = new RectInt(frame.y, width - frame.xMax, frame.height, frame.width); break;
                default: return;
            }
            byte[] coverage = null;
            if (areaSelectionOwner == document && areaSelection?.Active == true &&
                areaSelection.Width == width && areaSelection.Height == height)
            {
                coverage = new byte[areaSelection.Coverage.Length];
                int nextWidth = orientation == CanvasOrientation.RotateLeft || orientation == CanvasOrientation.RotateRight ? height : width;
                for (int y = 0, i = 0; y < height; y++)
                    for (int x = 0; x < width; x++, i++)
                    {
                        int nx, ny;
                        switch (orientation)
                        {
                            case CanvasOrientation.FlipHorizontal: nx = width - 1 - x; ny = y; break;
                            case CanvasOrientation.FlipVertical: nx = x; ny = height - 1 - y; break;
                            case CanvasOrientation.RotateLeft: nx = height - 1 - y; ny = x; break;
                            default: nx = y; ny = width - 1 - x; break;
                        }
                        coverage[ny * nextWidth + nx] = areaSelection.Coverage[i];
                    }
            }
            CommitCanvasChange(undoName, coverage, nextFrame, () =>
            {
                if (!document.ApplyCanvasOrientation(orientation)) return false;
                for (int i = 0; i < canvasGuides.Count; i++)
                {
                    CanvasGuide guide = canvasGuides[i];
                    Vector2 normal = guide.normal;
                    switch (orientation)
                    {
                        case CanvasOrientation.FlipHorizontal: guide.normal = new Vector2(-normal.x, normal.y); guide.position -= normal.x * width; break;
                        case CanvasOrientation.FlipVertical: guide.normal = new Vector2(normal.x, -normal.y); guide.position -= normal.y * height; break;
                        case CanvasOrientation.RotateLeft: guide.normal = new Vector2(normal.y, -normal.x); guide.position -= normal.x * width; break;
                        case CanvasOrientation.RotateRight: guide.normal = new Vector2(-normal.y, normal.x); guide.position -= normal.y * height; break;
                    }
                    canvasGuides[i] = guide;
                }
                return true;
            });
        }

        private void CommitCanvasChange(string undoName, byte[] coverage, RectInt? nextFrame, Func<bool> apply)
        {
            var document = activeDocument;
            CaptureCanvasChangeSelection();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { document, this, canvasChangeSelectionUndo }, undoName);
                if (!apply()) return;
                canvasGuidesRevision++;
                canvasGuideUndo.Clear(); canvasGuideRedo.Clear();
                selectedCanvasGuide = -1;
                ResetAreaSelection();
                if (coverage != null)
                {
                    var selection = GetAreaSelection();
                    selection.Set(coverage, SelectionCombine.Replace);
                }
                CaptureCanvasChangeSelection();
                DisposeCanvasCropSnapTargets();
                temporaryDocumentDirty = true;
                document.MarkChanged();
                if (nextFrame.HasValue) cropManipulator?.SetFrameAfterCanvasChange(nextFrame.Value);
                else ResetCanvasCropFrame();
                RefreshCanvasGuides();
                RequestCanvasRender(true);
                RefreshToolkitInterface(forceValues: true);
                toolkitCanvas?.Focus();
            }
            catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                Undo.IncrementCurrentGroup();
            }
        }

        private void CaptureCanvasChangeSelection()
        {
            if (canvasChangeSelectionUndo == null)
            {
                canvasChangeSelectionUndo = CreateInstance<CanvasChangeSelectionUndo>();
                canvasChangeSelectionUndo.hideFlags = HideFlags.HideAndDontSave;
            }
            canvasChangeSelectionUndo.document = activeDocument;
            canvasChangeSelectionUndo.width = activeDocument.width;
            canvasChangeSelectionUndo.height = activeDocument.height;
            canvasChangeSelectionUndo.coverage = areaSelectionOwner == activeDocument && areaSelection?.Active == true &&
                areaSelection.Width == activeDocument.width && areaSelection.Height == activeDocument.height
                ? (byte[])areaSelection.Coverage.Clone() : null;
            canvasChangeSelectionUndo.revision = ++canvasChangeSelectionRevision;
            observedCanvasChangeSelectionRevision = canvasChangeSelectionUndo.revision;
        }

        private void RestoreCanvasChangeSelectionUndo()
        {
            var state = canvasChangeSelectionUndo;
            if (state == null || state.revision == observedCanvasChangeSelectionRevision) return;
            observedCanvasChangeSelectionRevision = state.revision;
            ResetAreaSelection();
            if (state.document == activeDocument && state.width == activeDocument.width && state.height == activeDocument.height &&
                state.coverage != null)
                GetAreaSelection().Set((byte[])state.coverage.Clone(), SelectionCombine.Replace);
            canvasGuideManipulator?.Cancel();
            canvasGuideUndo.Clear(); canvasGuideRedo.Clear();
            canvasGuidesRevision++;
            selectedCanvasGuide = -1;
            RefreshCanvasGuides();
        }

        private void DisposeCanvasChangeSelectionUndo()
        {
            if (canvasChangeSelectionUndo == null) return;
            Undo.ClearUndo(canvasChangeSelectionUndo);
            DestroyImmediate(canvasChangeSelectionUndo);
            canvasChangeSelectionUndo = null;
        }

        private bool HandleCanvasCropKey(KeyDownEvent evt)
        {
            if (canvasTool != CanvasTool.Crop || evt.ctrlKey || evt.commandKey || evt.altKey) return false;
            if (evt.keyCode == KeyCode.Escape)
            {
                if (cropManipulator?.IsDragging == true) cropManipulator.CancelGesture();
                else ResetCanvasCropFrame();
                toolkitHeaderBindings.Refresh();
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) ApplyCanvasCrop();
            else
            {
                int distance = evt.shiftKey ? 10 : 1;
                RectInt frame = GetCanvasCropFrame();
                if (canvasCropMode == CanvasCropMode.Scale)
                {
                    switch (evt.keyCode)
                    {
                        case KeyCode.LeftArrow: frame.width -= distance; break;
                        case KeyCode.RightArrow: frame.width += distance; break;
                        case KeyCode.UpArrow: frame.y -= distance; frame.height += distance; break;
                        case KeyCode.DownArrow: frame.y += distance; frame.height -= distance; break;
                        default: return false;
                    }
                }
                else
                switch (evt.keyCode)
                {
                    case KeyCode.LeftArrow: frame.x -= distance; break;
                    case KeyCode.RightArrow: frame.x += distance; break;
                    case KeyCode.UpArrow: frame.y += distance; break;
                    case KeyCode.DownArrow: frame.y -= distance; break;
                    default: return false;
                }
                if (cropManipulator?.IsDragging != true) cropManipulator?.SetFrame(frame);
            }
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private sealed class CropManipulator : PointerManipulator
        {
            private const int MoveHandle = 8, CreateHandle = 9;
            private readonly WhimTexWindow owner;
            private readonly VisualElement overlay;
            private IVisualElementScheduledItem gestureCheck;
            private WhimTexDocument document;
            private Vector2Int dimensions;
            private float appliedPixelScale = 1f, manualPixelScale = 1f;
            private RectInt frame, original;
            private Vector2 start, startView;
            private int pointer = -1, handle = -1;
            internal bool IsDragging => pointer >= 0;
            internal RectInt Frame { get { EnsureFrame(); return frame; } }
            internal float PixelScale
            {
                get
                {
                    EnsureFrame();
                    if (owner.canvasCropMode != CanvasCropMode.Scale || !owner.canvasCropAutoPixelScale) return manualPixelScale;
                    return appliedPixelScale * Mathf.Min(frame.width / (float)dimensions.x, frame.height / (float)dimensions.y);
                }
            }

            internal CropManipulator(WhimTexWindow owner)
            {
                this.owner = owner;
                overlay = new VisualElement { name = "canvasCropOverlay", pickingMode = PickingMode.Ignore };
                overlay.AddToClassList("whimtex-area-overlay");
                overlay.generateVisualContent += Draw;
            }
            protected override void RegisterCallbacksOnTarget()
            {
                target.Add(overlay);
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted);
                target.RegisterCallback<DetachFromPanelEvent>(Detached);
                owner.toolkitCanvas.ViewChanged += overlay.MarkDirtyRepaint;
                gestureCheck = target.schedule.Execute(CheckGesture).Every(16);
                gestureCheck.Pause();
                Refresh();
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Reset();
                gestureCheck?.Pause();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                owner.toolkitCanvas.ViewChanged -= overlay.MarkDirtyRepaint;
                overlay.RemoveFromHierarchy();
            }
            private bool Valid => document != null && document == owner.activeDocument &&
                dimensions.x == document.width && dimensions.y == document.height && appliedPixelScale == document.GetPixelScale();
            private void EnsureFrame()
            {
                if (Valid) return;
                CancelGesture();
                document = owner.activeDocument;
                dimensions = document != null ? new Vector2Int(document.width, document.height) : Vector2Int.one;
                manualPixelScale = appliedPixelScale = document?.GetPixelScale() ?? 1f;
                frame = new RectInt(0, 0, dimensions.x, dimensions.y);
            }
            internal void Refresh()
            {
                EnsureFrame();
                RefreshPreview();
                overlay.EnableInClassList("whimtex-hidden", owner.canvasTool != CanvasTool.Crop || document == null);
                overlay.MarkDirtyRepaint();
            }
            internal void Reset()
            {
                CancelGesture(); document = null;
                EnsureFrame(); Refresh();
            }
            internal void SetFrame(RectInt value)
            {
                CancelGesture(); EnsureFrame();
                int maximum = WhimTexDocument.MaximumCanvasFrameDimension;
                int width = Mathf.Clamp(value.width, 1, maximum);
                int height = Mathf.Clamp(value.height, 1, maximum);
                height = Mathf.Min(height, (int)(WhimTexDocument.MaximumCanvasFramePixels / width));
                frame = new RectInt(Mathf.Clamp(value.x, -maximum, maximum),
                    Mathf.Clamp(value.y, -maximum, maximum), width, height);
                RefreshPreview();
                overlay.MarkDirtyRepaint();
                owner.toolkitHeaderBindings.Refresh();
            }
            internal void SetFrameAfterCanvasChange(RectInt value)
            {
                float pendingPixelScale = manualPixelScale;
                CancelGesture(); EnsureFrame();
                manualPixelScale = pendingPixelScale;
                frame = value;
                Refresh();
                owner.toolkitHeaderBindings.Refresh();
            }
            internal void SetPixelScale(float value)
            {
                if (!float.IsFinite(value) || value <= 0f || value > WhimTexDocument.MaximumPixelScale) return;
                CancelGesture(); EnsureFrame();
                manualPixelScale = value;
                owner.toolkitHeaderBindings.Refresh();
            }
            internal void CancelGesture()
            {
                if (!IsDragging) return;
                frame = original;
                ReleasePointer();
                RefreshPreview();
                overlay.MarkDirtyRepaint();
                if (Valid) owner.toolkitHeaderBindings.Refresh();
            }
            private void CheckGesture()
            {
                if (IsDragging && (!Valid || owner.canvasTool != CanvasTool.Crop ||
                    target.panel == null || !target.HasPointerCapture(pointer))) CancelGesture();
            }
            private void ReleasePointer()
            {
                gestureCheck?.Pause();
                int captured = pointer; pointer = -1; handle = -1;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
            }
            private Vector2 CanvasPoint(Vector2 view)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                Vector2 point = owner.toolkitCanvas.ToCanvas(view);
                return new Vector2((point.x - image.x) / image.width * dimensions.x,
                    (image.yMax - point.y) / image.height * dimensions.y);
            }
            private Vector2 ViewPoint(Vector2 point)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                return owner.toolkitCanvas.ToView(new Vector2(image.x + point.x / dimensions.x * image.width,
                    image.yMax - point.y / dimensions.y * image.height));
            }
            private static Vector2 Anchor(RectInt rect, int index) => new Vector2(
                index == 0 || index == 6 || index == 7 ? rect.xMin : index == 2 || index == 3 || index == 4 ? rect.xMax : rect.x + rect.width * .5f,
                index <= 2 ? rect.yMin : index >= 4 && index <= 6 ? rect.yMax : rect.y + rect.height * .5f);
            private Vector2 Anchor(int index) => Anchor(frame, index);
            private int Hit(Vector2 view)
            {
                int closest = -1;
                float distance = 49f;
                for (int i = 0; i < 8; i++)
                {
                    float next = (ViewPoint(Anchor(i)) - view).sqrMagnitude;
                    if (next <= distance) { distance = next; closest = i; }
                }
                return closest >= 0 ? closest : frame.Contains(Vector2Int.FloorToInt(CanvasPoint(view))) ? MoveHandle : CreateHandle;
            }
            internal MouseCursor GetCursor(Vector2 view)
            {
                if (!Valid || owner.toolkitCanvas.PixelScale <= 0f || owner.toolkitCanvas.ImageRect.width <= 0f ||
                    owner.toolkitCanvas.ImageRect.height <= 0f || !target.contentRect.Contains(view)) return MouseCursor.Arrow;
                int hovered = Hit(view);
                if (owner.canvasCropMode == CanvasCropMode.Scale && hovered >= MoveHandle) return MouseCursor.Arrow;
                return hovered == MoveHandle ? MouseCursor.MoveArrow : hovered < MoveHandle ? MouseCursor.ScaleArrow : MouseCursor.Arrow;
            }
            private void Down(PointerDownEvent evt)
            {
                if (IsDragging || owner.canvasTool != CanvasTool.Crop || owner.activeDocument == null || evt.button != 0 ||
                    evt.altKey || owner.canvasZoomManipulator?.IsNavigating == true ||
                    !target.contentRect.Contains(evt.localPosition) || owner.toolkitCanvas.PixelScale <= 0f) return;
                if (owner.toolkitCanvas.ImageRect.width <= 0f || owner.toolkitCanvas.ImageRect.height <= 0f) return;
                EnsureFrame();
                int hit = Hit(evt.localPosition);
                if (owner.canvasCropMode == CanvasCropMode.Scale && hit >= MoveHandle) return;
                owner.FinishPaintingStroke(); owner.FinishCanvasTransform();
                owner.Focus(); target.Focus(); EnsureFrame();
                owner.BeginCanvasCropSnapSnapshot();
                original = frame;
                startView = evt.localPosition; start = CanvasPoint(startView); handle = hit;
                if (handle == CreateHandle && !(evt.ctrlKey || evt.commandKey))
                    start = owner.SnapCanvasCropPoint(start, Vector2.zero, true);
                if (handle == CreateHandle) start = new Vector2(Mathf.Round(start.x), Mathf.Round(start.y));
                pointer = evt.pointerId; target.CapturePointer(pointer);
                gestureCheck?.Resume();
                owner.toolkitHeaderBindings.Refresh();
                WhimTexUI.ConsumeEvent(evt);
            }
            private void Update(Vector2 view, bool shift, bool disableSnap)
            {
                Vector2 point = CanvasPoint(view);
                if (handle == MoveHandle)
                {
                    Vector2 delta = point - start;
                    if (shift) delta = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? new Vector2(delta.x, 0) : new Vector2(0, delta.y);
                    Rect proposed = new Rect(original.x + delta.x, original.y + delta.y, original.width, original.height);
                    if (!disableSnap) delta += owner.SnapCanvasCropMove(proposed, !shift || delta.x != 0f, !shift || delta.y != 0f);
                    frame = new RectInt(original.x + Mathf.RoundToInt(delta.x), original.y + Mathf.RoundToInt(delta.y), original.width, original.height);
                }
                else
                {
                    if (handle != CreateHandle) point = Anchor(original, handle) + point - start;
                    bool x = handle == CreateHandle || handle != 1 && handle != 5;
                    bool y = handle == CreateHandle || handle != 3 && handle != 7;
                    Vector2 fixedPoint = handle == CreateHandle ? start : new Vector2(
                        handle == 0 || handle == 6 || handle == 7 ? original.xMax : original.xMin,
                        handle <= 2 ? original.yMax : original.yMin);
                    Vector2 direction = x && y ? point - fixedPoint : x ? Vector2.right : Vector2.up;
                    if (!x) point.x = original.x + original.width * .5f;
                    if (!y) point.y = original.y + original.height * .5f;
                    float ratio = handle == CreateHandle ? dimensions.x / (float)dimensions.y : original.width / (float)original.height;
                    if (shift && x && y)
                    {
                        Vector2 delta = point - fixedPoint;
                        float height = Mathf.Max(Mathf.Abs(delta.y), Mathf.Abs(delta.x) / ratio);
                        direction = new Vector2(delta.x < 0 ? -ratio : ratio, delta.y < 0 ? -1 : 1);
                        point = fixedPoint + direction * height;
                    }
                    if (!disableSnap) point = owner.SnapCanvasCropPoint(point, direction, x && y && !shift);
                    if (!x) point.x = original.xMax;
                    if (!y) point.y = original.yMax;
                    int x0 = Mathf.RoundToInt(x ? Mathf.Min(fixedPoint.x, point.x) : original.xMin);
                    int y0 = Mathf.RoundToInt(y ? Mathf.Min(fixedPoint.y, point.y) : original.yMin);
                    int width = Mathf.Max(1, Mathf.RoundToInt(x ? Mathf.Abs(point.x - fixedPoint.x) : original.width));
                    int heightPixels = Mathf.Max(1, Mathf.RoundToInt(y ? Mathf.Abs(point.y - fixedPoint.y) : original.height));
                    if (shift && x && y) heightPixels = Mathf.Max(1, Mathf.RoundToInt(width / ratio));
                    int maximum = WhimTexDocument.MaximumCanvasFrameDimension;
                    width = Mathf.Min(width, maximum); heightPixels = Mathf.Min(heightPixels, maximum,
                        (int)(WhimTexDocument.MaximumCanvasFramePixels / width));
                    if (x) x0 = Mathf.RoundToInt(fixedPoint.x) - (point.x < fixedPoint.x ? width : 0);
                    if (y) y0 = Mathf.RoundToInt(fixedPoint.y) - (point.y < fixedPoint.y ? heightPixels : 0);
                    frame = new RectInt(x0, y0, width, heightPixels);
                }
                RefreshPreview();
                overlay.MarkDirtyRepaint(); owner.toolkitHeaderBindings.Refresh();
            }
            private void Move(PointerMoveEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointer) return;
                if (!Valid || (evt.pressedButtons & 1) == 0) CancelGesture();
                else if (((Vector2)evt.localPosition - startView).sqrMagnitude >= 9f)
                    Update(evt.localPosition, evt.shiftKey, evt.ctrlKey || evt.commandKey);
                WhimTexUI.ConsumeEvent(evt);
            }
            private void Up(PointerUpEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointer || evt.button != 0) return;
                if (!Valid || handle == CreateHandle && ((Vector2)evt.localPosition - startView).sqrMagnitude < 9f) CancelGesture();
                else
                {
                    if (((Vector2)evt.localPosition - startView).sqrMagnitude >= 9f)
                        Update(evt.localPosition, evt.shiftKey, evt.ctrlKey || evt.commandKey);
                    else frame = original;
                    ReleasePointer();
                }
                RefreshPreview();
                owner.toolkitHeaderBindings.Refresh();
                WhimTexUI.ConsumeEvent(evt);
            }
            private void Lost(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId == pointer && !target.HasPointerCapture(pointer)) CancelGesture();
            }
            private void Interrupted(PointerCancelEvent evt) => CancelGesture();
            private void Detached(DetachFromPanelEvent evt) => Reset();
            private void RefreshPreview()
            {
                owner.toolkitCanvas?.SetCanvasFramePreview(owner.canvasTool == CanvasTool.Crop &&
                    owner.canvasCropMode == CanvasCropMode.Scale && Valid ? frame : (RectInt?)null);
            }
            private void Draw(MeshGenerationContext context)
            {
                if (owner.canvasTool != CanvasTool.Crop || !Valid || owner.toolkitCanvas.PixelScale <= 0f) return;
                var painter = context.painter2D;
                Vector2 a = ViewPoint(new Vector2(frame.xMin, frame.yMin)), b = ViewPoint(new Vector2(frame.xMax, frame.yMin));
                Vector2 c = ViewPoint(new Vector2(frame.xMax, frame.yMax)), d = ViewPoint(new Vector2(frame.xMin, frame.yMax));
                painter.fillColor = new Color(0, 0, 0, .4f);
                painter.BeginPath();
                painter.MoveTo(Vector2.zero); painter.LineTo(new Vector2(target.contentRect.width, 0));
                painter.LineTo(target.contentRect.size); painter.LineTo(new Vector2(0, target.contentRect.height)); painter.ClosePath();
                painter.MoveTo(a); painter.LineTo(b); painter.LineTo(c); painter.LineTo(d); painter.ClosePath();
                painter.Fill(FillRule.OddEven);
                painter.lineWidth = 1f;
                painter.strokeColor = new Color(0, 0, 0, .25f);
                for (int i = 1; i < 3; i++)
                {
                    float t = i / 3f;
                    painter.BeginPath(); painter.MoveTo(Vector2.Lerp(a, b, t)); painter.LineTo(Vector2.Lerp(d, c, t)); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(Vector2.Lerp(a, d, t)); painter.LineTo(Vector2.Lerp(b, c, t)); painter.Stroke();
                }
                painter.strokeColor = new Color(0, 0, 0, .85f); painter.lineWidth = 3f;
                painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.LineTo(c); painter.LineTo(d); painter.ClosePath(); painter.Stroke();
                painter.strokeColor = new Color(.9f, .92f, .95f, 1f); painter.lineWidth = 1f; painter.Stroke();
                painter.fillColor = new Color(.92f, .94f, .98f); painter.strokeColor = new Color(0, 0, 0, .9f);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 p = ViewPoint(Anchor(i));
                    float ppi = EditorGUIUtility.pixelsPerPoint;
                    Vector2 pixels = overlay.LocalToWorld(p) * ppi;
                    p = overlay.WorldToLocal(new Vector2(Mathf.Floor(pixels.x) + .5f,
                        Mathf.Floor(pixels.y) + .5f) / ppi);
                    painter.BeginPath(); painter.MoveTo(p + new Vector2(-3, -3)); painter.LineTo(p + new Vector2(3, -3));
                    painter.LineTo(p + new Vector2(3, 3)); painter.LineTo(p + new Vector2(-3, 3)); painter.ClosePath();
                    painter.Fill(); painter.Stroke();
                }
            }
        }
    }

    internal sealed class CanvasChangeSelectionUndo : ScriptableObject
    {
        [SerializeField] internal WhimTexDocument document;
        [SerializeField] internal int width, height, revision;
        [SerializeField] internal byte[] coverage;
    }
}
