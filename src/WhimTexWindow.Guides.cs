using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [Serializable]
        private struct CanvasGuide
        {
            public Vector2 normal;
            public float position;
        }

        [SerializeField] private List<CanvasGuide> canvasGuides = new List<CanvasGuide>();
        [NonSerialized] private WhimTexDocument canvasGuidesDocument;
        private CanvasGuideManipulator canvasGuideManipulator;
        private VisualElement canvasGuideOverlay;
        private VisualElement canvasGuideTopRail;
        private VisualElement canvasGuideLeftRail;

        private bool CanMoveCanvasGuides => canvasTool == CanvasTool.None ||
            canvasTool == CanvasTool.Transform || canvasTool == CanvasTool.Zoom || IsTemporaryCanvasTool(canvasTool);

        private bool CanvasToolWantsPointer(Vector2 point) => IsCanvasZoomEnabled || canvasTool == CanvasTool.None ||
            canvasTransformManipulator?.WantsPointer(point) == true || pointManipulator?.WantsPointer(point) == true ||
            normalManipulator?.WantsPointer(point) == true;

        private void BuildCanvasGuides()
        {
            canvasGuides ??= new List<CanvasGuide>();
            canvasGuidesDocument = activeDocument;
            canvasGuideOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            canvasGuideOverlay.AddToClassList("whimtex-guides-overlay");
            canvasGuideOverlay.AddToClassList("whimtex-canvas-surface");
            canvasGuideManipulator = new CanvasGuideManipulator(this);
            toolkitCanvas.RegisterCallback<PointerDownEvent>(canvasGuideManipulator.RailDown, TrickleDown.TrickleDown);
            canvasGuideOverlay.generateVisualContent += canvasGuideManipulator.Draw;
            toolkitCanvas.AddBelowToolOverlays(canvasGuideOverlay);
            AddGuideRail(true);
            AddGuideRail(false);
            toolkitCanvasViewHeader.RegisterCallback<GeometryChangedEvent>(UpdateCanvasGuideRails);
            toolkitCanvas.RegisterCallback<GeometryChangedEvent>(UpdateCanvasGuideRails);
            toolkitCanvas.ViewChanged += canvasGuideOverlay.MarkDirtyRepaint;
        }

        private void UpdateCanvasGuideRails(GeometryChangedEvent evt)
        {
            float top = Mathf.Max(0f, toolkitCanvasViewHeader.worldBound.yMax - toolkitCanvas.worldBound.yMin);
            if (float.IsNaN(top) || float.IsInfinity(top)) return;
            canvasGuideTopRail.style.top = top;
            canvasGuideLeftRail.style.top = top + canvasGuideTopRail.resolvedStyle.height;
        }

        private void AddGuideRail(bool vertical)
        {
            var rail = new VisualElement
            {
                tooltip = vertical ? "Drag out a vertical guide. Right-click for guide settings."
                    : "Drag out a horizontal guide. Right-click for guide settings."
            };
            rail.AddToClassList("whimtex-guide-rail");
            rail.AddToClassList(vertical ? "whimtex-guide-rail--left" : "whimtex-guide-rail--top");
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList("whimtex-guide-grip");
            rail.Add(grip);
            canvasGuideOverlay.Add(rail);
            if (vertical) canvasGuideLeftRail = rail;
            else canvasGuideTopRail = rail;
        }

        private void ClearCanvasGuides()
        {
            canvasGuideManipulator?.Cancel();
            canvasGuides?.Clear();
            canvasGuideUndo.Clear();
            canvasGuideRedo.Clear();
            selectedCanvasGuide = -1;
            canvasGuidesRevision++;
            canvasGuideOverlay?.MarkDirtyRepaint();
            RefreshCanvasPointerCursor();
        }

        private sealed class CanvasGuideManipulator : PointerManipulator
        {
            private const float GrabDistance = 4f;
            private readonly WhimTexWindow owner;
            private int pointer = -1, movingIndex = -1, hovered = -1;
            private CanvasGuide pending;
            private float grabOffset;
            private bool discard;
            private bool controlHeld;
            private Vector2 lastPoint;
            private Vector2 startPoint;
            private bool moved;
            internal bool IsDragging => pointer >= 0;
            private bool CanContinueDrag => Ready && (movingIndex < 0 ||
                (owner.CanMoveCanvasGuides && !owner.canvasGuidesLocked && !owner.canvasGuidesHidden));

            internal CanvasGuideManipulator(WhimTexWindow owner) { this.owner = owner; }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
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
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerDownEvent>(RailDown, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
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

            private bool Ready => owner.HasCanvasLayers && owner.canvasGuidesDocument == owner.activeDocument &&
                owner.toolkitCanvas.PixelScale > 0f;

            private int RailAt(Vector2 point)
            {
                Rect rect = target.contentRect;
                if (!rect.Contains(point)) return -1;
                Vector2 worldPoint = target.LocalToWorld(point);
                if (owner.canvasGuideTopRail.worldBound.Contains(worldPoint)) return 1;
                if (owner.canvasGuideLeftRail.worldBound.Contains(worldPoint)) return 0;
                return -1;
            }

            private bool CanGrab(bool control, bool alt) => Ready && !control && !alt &&
                owner.paintingLayer == null && !(owner.canvasZoomManipulator?.IsDragging ?? false) &&
                !(owner.canvasTransformManipulator?.IsDragging ?? false) &&
                !(owner.pointManipulator?.IsDragging ?? false) &&
                !(owner.normalManipulator?.IsDragging ?? false) &&
                !(owner.shapeManipulator?.IsDragging ?? false) &&
                !(owner.areaSelectionManipulator?.HasGesture ?? false);

            private float PositionAt(Vector2 point, Vector2 normal)
            {
                CanvasElement canvas = owner.toolkitCanvas;
                point = canvas.ToCanvas(point);
                return Vector2.Dot((point - canvas.ImageRect.position) / canvas.PixelScale, normal);
            }

            private int Hit(Vector2 point, bool toolPriority = true)
            {
                if (!owner.CanMoveCanvasGuides || owner.canvasGuidesHidden || owner.canvasGuidesLocked ||
                    !target.contentRect.Contains(point) ||
                    toolPriority && owner.CanvasToolWantsPointer(point)) return -1;
                int result = -1;
                float best = GrabDistance;
                for (int i = owner.canvasGuides.Count - 1; i >= 0; i--)
                {
                    CanvasGuide guide = owner.canvasGuides[i];
                    float distance = Mathf.Abs(PositionAt(point, guide.normal) - guide.position) * owner.toolkitCanvas.PixelScale;
                    if (distance <= best) { best = distance; result = i; }
                }
                return result;
            }

            internal bool WantsCursor(Vector2 point, bool alt) => IsDragging ||
                (CanGrab(controlHeld, alt) && (RailAt(point) >= 0 || Hit(point) >= 0));

            internal void RailDown(PointerDownEvent evt)
            {
                if (RailAt(evt.localPosition) >= 0) Down(evt);
                else if (!IsDragging && evt.button == 0 && owner.selectedCanvasGuide >= 0)
                {
                    owner.selectedCanvasGuide = -1;
                    owner.canvasGuideOverlay.MarkDirtyRepaint();
                }
            }

            private void Down(PointerDownEvent evt)
            {
                controlHeld = evt.ctrlKey;
                if (IsDragging) { WhimTexUI.ConsumeEvent(evt); return; }
                if (evt.button != 0 && evt.button != 1) return;
                if (!CanGrab(evt.ctrlKey, evt.altKey)) { owner.selectedCanvasGuide = -1; return; }
                Vector2 point = evt.localPosition;
                int rail = RailAt(point);
                int hit = rail < 0 ? Hit(point, evt.button == 0) : -1;
                owner.selectedCanvasGuide = hit;
                if (rail < 0 && hit < 0) return;
                owner.CancelCanvasEyedropper();
                owner.Focus();
                target.Focus();
                if (evt.button == 1)
                {
                    owner.ShowCanvasGuideMenu(hit);
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (hit >= 0 && evt.clickCount > 1)
                {
                    CanvasGuideSettingsWindow.Open(owner, hit);
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (rail >= 0 && owner.canvasGuides.Count >= MaxCanvasGuides)
                {
                    owner.ShowNotification(new GUIContent("Guide limit reached (256)."));
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                owner.SetCanvasGuidesHidden(false);
                movingIndex = hit;
                pending = hit >= 0 ? owner.canvasGuides[hit] : new CanvasGuide
                {
                    normal = owner.canvasViewport.ToCanvasDelta(rail == 0 ? Vector2.right : Vector2.up).normalized
                };
                grabOffset = hit >= 0 ? pending.position - PositionAt(point, pending.normal) : 0f;
                pointer = evt.pointerId;
                startPoint = point;
                moved = false;
                target.CapturePointer(pointer);
                Update(point);
                owner.canvasGuideOverlay.MarkDirtyRepaint();
                owner.UpdateCanvasCursor(point, false);
                WhimTexUI.ConsumeEvent(evt);
            }

            private void Update(Vector2 point)
            {
                lastPoint = point;
                moved |= (point - startPoint).sqrMagnitude >= 9f;
                if (movingIndex >= 0 && !moved) return;
                pending.position = PositionAt(point, pending.normal) + grabOffset;
                if (!controlHeld) pending.position = owner.SnapCanvasGuidePosition(pending, movingIndex);
                discard = !target.contentRect.Contains(point) || RailAt(point) >= 0 ||
                    float.IsNaN(pending.position) || float.IsInfinity(pending.position);
                owner.canvasGuideOverlay.MarkDirtyRepaint();
            }

            private void Move(PointerMoveEvent evt)
            {
                controlHeld = evt.ctrlKey;
                if (IsDragging)
                {
                    if (evt.pointerId != pointer) return;
                    if (!CanContinueDrag || (evt.pressedButtons & 1) == 0) Cancel();
                    else Update(evt.localPosition);
                    owner.UpdateCanvasCursor(evt.localPosition, evt.altKey);
                    evt.StopImmediatePropagation();
                    return;
                }
                int next = evt.pressedButtons == 0 && CanGrab(evt.ctrlKey, evt.altKey) ? Hit(evt.localPosition) : -1;
                if (next != hovered) { hovered = next; owner.canvasGuideOverlay.MarkDirtyRepaint(); }
            }

            private void Up(PointerUpEvent evt)
            {
                controlHeld = evt.ctrlKey;
                if (!IsDragging || evt.pointerId != pointer || evt.button != 0) return;
                if (CanContinueDrag)
                {
                    Update(evt.localPosition);
                    if (movingIndex >= 0 && movingIndex < owner.canvasGuides.Count)
                    {
                        if (discard)
                        {
                            owner.RememberCanvasGuides();
                            owner.canvasGuides.RemoveAt(movingIndex);
                            owner.selectedCanvasGuide = -1;
                        }
                        else if (pending.position != owner.canvasGuides[movingIndex].position)
                        {
                            owner.RememberCanvasGuides();
                            owner.canvasGuides[movingIndex] = pending;
                        }
                    }
                    else if (!discard && owner.canvasGuides.Count < MaxCanvasGuides)
                    {
                        owner.RememberCanvasGuides();
                        owner.canvasGuides.Add(pending);
                        owner.selectedCanvasGuide = owner.canvasGuides.Count - 1;
                    }
                }
                Cancel();
                owner.UpdateCanvasCursor(evt.localPosition, evt.altKey);
                WhimTexUI.ConsumeEvent(evt);
            }

            internal void Cancel()
            {
                int captured = pointer;
                pointer = -1;
                movingIndex = hovered = -1;
                discard = false;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                owner.canvasGuideOverlay?.MarkDirtyRepaint();
                if (captured >= 0) owner.RefreshCanvasPointerCursor();
            }

            private void Leave(PointerLeaveEvent evt)
            {
                if (hovered < 0) return;
                hovered = -1;
                owner.canvasGuideOverlay.MarkDirtyRepaint();
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
                owner.canvasGuideOverlay?.MarkDirtyRepaint();
                owner.RefreshCanvasPointerCursor();
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent evt) => Cancel();
            private void Detached(DetachFromPanelEvent evt) => Cancel();
            private void Geometry(GeometryChangedEvent evt) => Cancel();
            private void Wheel(WheelEvent evt) { if (IsDragging) Cancel(); }

            internal void Draw(MeshGenerationContext context)
            {
                if (!Ready || owner.canvasGuidesHidden) return;
                Rect bounds = target.contentRect;
                Painter2D painter = context.painter2D;
                for (int i = 0; i < owner.canvasGuides.Count; i++)
                    if (!IsDragging || i != movingIndex)
                        DrawGuide(painter, owner.canvasGuides[i], bounds, owner.CanMoveCanvasGuides && !owner.canvasGuidesLocked &&
                            (i == hovered || i == owner.selectedCanvasGuide), false);
                if (IsDragging) DrawGuide(painter, pending, bounds, true, discard);
            }

            private void DrawGuide(Painter2D painter, CanvasGuide guide, Rect bounds, bool highlight, bool deleting)
            {
                CanvasElement canvas = owner.toolkitCanvas;
                Vector2 point = canvas.ToView(canvas.ImageRect.position + guide.normal * (guide.position * canvas.PixelScale));
                Vector2 direction = owner.canvasViewport.ToViewDelta(new Vector2(-guide.normal.y, guide.normal.x));
                if (!ClipLine(bounds, point, direction, out Vector2 a, out Vector2 b)) return;
                bool aligned = Mathf.Min(Mathf.Abs(direction.x), Mathf.Abs(direction.y)) <= .0001f;
                Color lineColor = highlight ? WhimTexUserSettings.GuideActiveColor :
                    aligned ? WhimTexUserSettings.GuideAlignedColor : WhimTexUserSettings.GuideAngledColor;
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
