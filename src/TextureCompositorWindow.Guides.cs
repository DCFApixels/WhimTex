using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        [Serializable]
        private struct PreviewGuide
        {
            public Vector2 normal;
            public float position;
        }

        [SerializeField] private List<PreviewGuide> previewGuides = new List<PreviewGuide>();
        [SerializeField] private TextureCompositor previewGuidesDocument;
        private PreviewGuideManipulator previewGuideManipulator;
        private VisualElement previewGuideOverlay;

        private bool CanMovePreviewGuides => previewTool == PreviewTool.None ||
            previewTool == PreviewTool.Transform || previewTool == PreviewTool.Zoom;

        private void BuildPreviewGuides()
        {
            previewGuides ??= new List<PreviewGuide>();
            if (previewGuidesDocument != compositor) ClearPreviewGuides();
            previewGuidesDocument = compositor;
            previewGuideOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            previewGuideOverlay.AddToClassList("sprite-editor-guides-overlay");
            previewGuideOverlay.AddToClassList("sprite-editor-preview-surface");
            previewGuideManipulator = new PreviewGuideManipulator(this);
            toolkitPreviewCanvas.AddManipulator(previewGuideManipulator);
            previewGuideOverlay.generateVisualContent += previewGuideManipulator.Draw;
            toolkitPreviewCanvas.Add(previewGuideOverlay);
            AddGuideRail(true);
            AddGuideRail(false);
            toolkitPreviewCanvas.ViewChanged += previewGuideOverlay.MarkDirtyRepaint;
        }

        private void AddGuideRail(bool vertical)
        {
            var rail = new VisualElement
            {
                tooltip = vertical ? "Drag out a vertical guide. Right-click for guide settings."
                    : "Drag out a horizontal guide. Right-click for guide settings."
            };
            rail.AddToClassList("sprite-editor-guide-rail");
            rail.AddToClassList(vertical ? "sprite-editor-guide-rail--left" : "sprite-editor-guide-rail--top");
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList("sprite-editor-guide-grip");
            rail.Add(grip);
            previewGuideOverlay.Add(rail);
        }

        private void ClearPreviewGuides()
        {
            previewGuideManipulator?.Cancel();
            previewGuides?.Clear();
            previewGuideUndo.Clear();
            previewGuideRedo.Clear();
            selectedPreviewGuide = -1;
            previewGuidesRevision++;
            previewGuideOverlay?.MarkDirtyRepaint();
            RefreshPreviewPointerCursor();
        }

        private sealed class PreviewGuideManipulator : PointerManipulator
        {
            private const float RailSize = 8f;
            private const float GrabDistance = 4f;
            private readonly TextureCompositorWindow owner;
            private int pointer = -1, movingIndex = -1, hovered = -1;
            private PreviewGuide pending;
            private float grabOffset;
            private bool discard;
            private bool controlHeld;
            private Vector2 lastPoint;
            private Vector2 startPoint;
            private bool moved;
            internal bool IsDragging => pointer >= 0;
            private bool CanContinueDrag => Ready && (movingIndex < 0 ||
                (owner.CanMovePreviewGuides && !owner.previewGuidesLocked && !owner.previewGuidesHidden));

            internal PreviewGuideManipulator(TextureCompositorWindow owner) { this.owner = owner; }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerLeaveEvent>(Leave);
                target.RegisterCallback<PointerEnterEvent>(Enter);
                target.RegisterCallback<KeyDownEvent>(ModifierDown);
                target.RegisterCallback<KeyUpEvent>(ModifierUp);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted);
                target.RegisterCallback<DetachFromPanelEvent>(Detached);
                target.RegisterCallback<GeometryChangedEvent>(Geometry);
                target.RegisterCallback<WheelEvent>(Wheel, TrickleDown.TrickleDown);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerLeaveEvent>(Leave);
                target.UnregisterCallback<PointerEnterEvent>(Enter);
                target.UnregisterCallback<KeyDownEvent>(ModifierDown);
                target.UnregisterCallback<KeyUpEvent>(ModifierUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<GeometryChangedEvent>(Geometry);
                target.UnregisterCallback<WheelEvent>(Wheel, TrickleDown.TrickleDown);
            }

            private bool Ready => owner.HasPreviewLayers && owner.previewGuidesDocument == owner.compositor &&
                owner.toolkitPreviewCanvas.PixelScale > 0f;

            private int RailAt(Vector2 point)
            {
                Rect rect = target.contentRect;
                if (!rect.Contains(point)) return -1;
                if (point.y < rect.yMin + RailSize) return 1;
                if (point.x < rect.xMin + RailSize) return 0;
                return -1;
            }

            private bool CanGrab(bool control, bool alt) => Ready && !control && !alt &&
                owner.paintingLayer == null && !(owner.previewZoomManipulator?.IsDragging ?? false) &&
                !(owner.previewTransformManipulator?.IsDragging ?? false) &&
                !(owner.shapeManipulator?.IsDragging ?? false) &&
                !(owner.areaSelectionManipulator?.HasGesture ?? false);

            private float PositionAt(Vector2 point, Vector2 normal)
            {
                SpritePreviewElement canvas = owner.toolkitPreviewCanvas;
                point = canvas.ToCanvas(point);
                return Vector2.Dot((point - canvas.ImageRect.position) / canvas.PixelScale, normal);
            }

            private int Hit(Vector2 point)
            {
                if (!owner.CanMovePreviewGuides || owner.previewGuidesHidden || owner.previewGuidesLocked ||
                    !target.contentRect.Contains(point)) return -1;
                int result = -1;
                float best = GrabDistance;
                for (int i = owner.previewGuides.Count - 1; i >= 0; i--)
                {
                    PreviewGuide guide = owner.previewGuides[i];
                    float distance = Mathf.Abs(PositionAt(point, guide.normal) - guide.position) * owner.toolkitPreviewCanvas.PixelScale;
                    if (distance <= best) { best = distance; result = i; }
                }
                return result;
            }

            internal bool WantsCursor(Vector2 point, bool alt) => IsDragging ||
                (CanGrab(controlHeld, alt) && (RailAt(point) >= 0 || Hit(point) >= 0));

            private void Down(PointerDownEvent evt)
            {
                controlHeld = evt.ctrlKey;
                if (IsDragging) { SpriteEditorUI.ConsumeEvent(evt); return; }
                if (evt.button != 0 && evt.button != 1) return;
                if (!CanGrab(evt.ctrlKey, evt.altKey)) { owner.selectedPreviewGuide = -1; return; }
                Vector2 point = evt.localPosition;
                int rail = RailAt(point);
                int hit = rail < 0 ? Hit(point) : -1;
                owner.selectedPreviewGuide = hit;
                if (rail < 0 && hit < 0) return;
                owner.CancelPreviewEyedropper();
                owner.Focus();
                target.Focus();
                if (evt.button == 1)
                {
                    owner.ShowPreviewGuideMenu(hit);
                    SpriteEditorUI.ConsumeEvent(evt);
                    return;
                }
                if (hit >= 0 && evt.clickCount > 1)
                {
                    PreviewGuideSettingsWindow.Open(owner, hit);
                    SpriteEditorUI.ConsumeEvent(evt);
                    return;
                }
                if (rail >= 0 && owner.previewGuides.Count >= MaxPreviewGuides)
                {
                    owner.ShowNotification(new GUIContent("Guide limit reached (256)."));
                    SpriteEditorUI.ConsumeEvent(evt);
                    return;
                }
                owner.previewGuidesHidden = false;
                movingIndex = hit;
                pending = hit >= 0 ? owner.previewGuides[hit] : new PreviewGuide
                {
                    normal = owner.previewViewport.ToCanvasDelta(rail == 0 ? Vector2.right : Vector2.up).normalized
                };
                grabOffset = hit >= 0 ? pending.position - PositionAt(point, pending.normal) : 0f;
                pointer = evt.pointerId;
                startPoint = point;
                moved = false;
                target.CapturePointer(pointer);
                Update(point);
                owner.previewGuideOverlay.MarkDirtyRepaint();
                owner.UpdatePreviewCursor(point, false);
                SpriteEditorUI.ConsumeEvent(evt);
            }

            private void Update(Vector2 point)
            {
                lastPoint = point;
                moved |= (point - startPoint).sqrMagnitude >= 9f;
                if (movingIndex >= 0 && !moved) return;
                pending.position = PositionAt(point, pending.normal) + grabOffset;
                if (!controlHeld) pending.position = owner.SnapPreviewGuidePosition(pending, movingIndex);
                discard = !target.contentRect.Contains(point) || RailAt(point) >= 0 ||
                    float.IsNaN(pending.position) || float.IsInfinity(pending.position);
                owner.previewGuideOverlay.MarkDirtyRepaint();
            }

            private void Move(PointerMoveEvent evt)
            {
                controlHeld = evt.ctrlKey;
                if (IsDragging)
                {
                    if (evt.pointerId != pointer) return;
                    if (!CanContinueDrag || (evt.pressedButtons & 1) == 0) Cancel();
                    else Update(evt.localPosition);
                    owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                    evt.StopImmediatePropagation();
                    return;
                }
                int next = evt.pressedButtons == 0 && CanGrab(evt.ctrlKey, evt.altKey) ? Hit(evt.localPosition) : -1;
                if (next != hovered) { hovered = next; owner.previewGuideOverlay.MarkDirtyRepaint(); }
            }

            private void Up(PointerUpEvent evt)
            {
                controlHeld = evt.ctrlKey;
                if (!IsDragging || evt.pointerId != pointer || evt.button != 0) return;
                if (CanContinueDrag)
                {
                    Update(evt.localPosition);
                    if (movingIndex >= 0 && movingIndex < owner.previewGuides.Count)
                    {
                        if (discard)
                        {
                            owner.RememberPreviewGuides();
                            owner.previewGuides.RemoveAt(movingIndex);
                            owner.selectedPreviewGuide = -1;
                        }
                        else if (pending.position != owner.previewGuides[movingIndex].position)
                        {
                            owner.RememberPreviewGuides();
                            owner.previewGuides[movingIndex] = pending;
                        }
                    }
                    else if (!discard && owner.previewGuides.Count < MaxPreviewGuides)
                    {
                        owner.RememberPreviewGuides();
                        owner.previewGuides.Add(pending);
                        owner.selectedPreviewGuide = owner.previewGuides.Count - 1;
                    }
                }
                Cancel();
                owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                SpriteEditorUI.ConsumeEvent(evt);
            }

            internal void Cancel()
            {
                int captured = pointer;
                pointer = -1;
                movingIndex = hovered = -1;
                discard = false;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                owner.previewGuideOverlay?.MarkDirtyRepaint();
                if (captured >= 0) owner.RefreshPreviewPointerCursor();
            }

            private void Leave(PointerLeaveEvent evt)
            {
                if (hovered < 0) return;
                hovered = -1;
                owner.previewGuideOverlay.MarkDirtyRepaint();
            }
            private void Enter(PointerEnterEvent evt) => UpdateControl(evt.ctrlKey);
            private void ModifierDown(KeyDownEvent evt) => UpdateControl(evt.ctrlKey);
            private void ModifierUp(KeyUpEvent evt) => UpdateControl(evt.ctrlKey);
            private void UpdateControl(bool held)
            {
                if (controlHeld == held) return;
                controlHeld = held;
                hovered = -1;
                if (IsDragging) Update(lastPoint);
                owner.previewGuideOverlay?.MarkDirtyRepaint();
                owner.RefreshPreviewPointerCursor();
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent evt) => Cancel();
            private void Detached(DetachFromPanelEvent evt) => Cancel();
            private void Geometry(GeometryChangedEvent evt) => Cancel();
            private void Wheel(WheelEvent evt) { if (IsDragging) Cancel(); }

            internal void Draw(MeshGenerationContext context)
            {
                if (!Ready || owner.previewGuidesHidden) return;
                Rect bounds = target.contentRect;
                Painter2D painter = context.painter2D;
                for (int i = 0; i < owner.previewGuides.Count; i++)
                    if (!IsDragging || i != movingIndex)
                        DrawGuide(painter, owner.previewGuides[i], bounds, owner.CanMovePreviewGuides && !owner.previewGuidesLocked &&
                            (i == hovered || i == owner.selectedPreviewGuide), false);
                if (IsDragging) DrawGuide(painter, pending, bounds, true, discard);
            }

            private void DrawGuide(Painter2D painter, PreviewGuide guide, Rect bounds, bool highlight, bool deleting)
            {
                SpritePreviewElement canvas = owner.toolkitPreviewCanvas;
                Vector2 point = canvas.ToView(canvas.ImageRect.position + guide.normal * (guide.position * canvas.PixelScale));
                Vector2 direction = owner.previewViewport.ToViewDelta(new Vector2(-guide.normal.y, guide.normal.x));
                if (!ClipLine(bounds, point, direction, out Vector2 a, out Vector2 b)) return;
                bool aligned = Mathf.Min(Mathf.Abs(direction.x), Mathf.Abs(direction.y)) <= .0001f;
                Color lineColor = highlight ? SpriteEditorUserSettings.GuideActiveColor :
                    aligned ? SpriteEditorUserSettings.GuideAlignedColor : SpriteEditorUserSettings.GuideAngledColor;
                lineColor.a = highlight ? 1f : aligned ? .8f : .55f;
                if (deleting) lineColor = new Color(1f, .35f, .25f, .9f);
                for (int pass = 0; pass < 2; pass++)
                {
                    painter.lineWidth = pass == 0 ? 3f : 1f;
                    painter.strokeColor = pass == 0 ? new Color(0f, 0f, 0f, aligned || highlight ? .35f : .25f) : lineColor;
                    painter.BeginPath();
                    painter.MoveTo(a); painter.LineTo(b);
                    painter.Stroke();
                }
            }

            private static bool ClipLine(Rect bounds, Vector2 point, Vector2 direction, out Vector2 a, out Vector2 b)
            {
                a = b = default;
                if (bounds.width <= 0f || bounds.height <= 0f || direction.sqrMagnitude < .5f ||
                    float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y)) return false;
                float first = float.NegativeInfinity, last = float.PositiveInfinity;
                bool Axis(float origin, float delta, float min, float max)
                {
                    if (Mathf.Abs(delta) < .000001f) return origin >= min && origin <= max;
                    float t0 = (min - origin) / delta, t1 = (max - origin) / delta;
                    first = Mathf.Max(first, Mathf.Min(t0, t1));
                    last = Mathf.Min(last, Mathf.Max(t0, t1));
                    return first <= last;
                }
                if (!Axis(point.x, direction.x, bounds.xMin, bounds.xMax) ||
                    !Axis(point.y, direction.y, bounds.yMin, bounds.yMax)) return false;
                a = point + direction * first;
                b = point + direction * last;
                return true;
            }
        }
    }
}
