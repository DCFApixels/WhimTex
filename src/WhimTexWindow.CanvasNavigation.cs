using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class CanvasNavigationMetrics
    {
        internal static float MajorStep(float pixelsPerUnit)
        {
            if (!(pixelsPerUnit > 0f) || float.IsInfinity(pixelsPerUnit)) return 1f;
            double required = 72d / pixelsPerUnit;
            double decade = Math.Pow(10d, Math.Floor(Math.Log10(required)));
            double fraction = required / decade;
            return (float)(decade * (fraction <= 1d ? 1d : fraction <= 2d ? 2d : fraction <= 5d ? 5d : 10d));
        }

        internal static Rect PresentedBounds(CanvasViewport viewport, Rect view, Rect image)
        {
            Vector2 a = viewport.ToView(view, image.min);
            Vector2 b = viewport.ToView(view, new Vector2(image.xMax, image.yMin));
            Vector2 c = viewport.ToView(view, image.max);
            Vector2 d = viewport.ToView(view, new Vector2(image.xMin, image.yMax));
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        internal static Vector3 ScrollRange(float viewMin, float viewLength, float imageMin, float imageLength)
        {
            float padding = imageLength > viewLength ? viewLength * .25f : (viewLength - imageLength) * .5f;
            float min = Mathf.Min(viewMin, imageMin - padding);
            float max = Mathf.Max(viewMin + viewLength, imageMin + imageLength + padding);
            return new Vector3(Mathf.Max(0f, max - min - viewLength), viewMin - min, viewLength);
        }

        internal static Vector2 ScrollThumb(Vector3 range, float length)
        {
            if (!(length > 0f) || float.IsInfinity(length)) return Vector2.zero;
            float size = range.z > 0f
                ? Mathf.Min(length, Mathf.Max(24f, length * range.z / Mathf.Max(1f, range.x + range.z))) : length;
            float offset = range.x > .01f ? Mathf.Clamp01(range.y / range.x) * (length - size) : 0f;
            return new Vector2(offset, size);
        }

        internal static float ScrollDragScale(Vector3 range, float length)
        {
            if (!(length > 0f) || float.IsInfinity(length) || !(range.z > 0f)) return 0f;
            float travel = length - ScrollThumb(range, length).y;
            return range.x > .01f && travel > .01f ? range.x / travel : (range.x + range.z) / length;
        }
    }

    public sealed partial class WhimTexWindow
    {
        private VisualElement canvasNavigation;
        private CanvasScrollBar canvasHorizontalScrollBar, canvasVerticalScrollBar;

        private VisualElement BuildCanvasNavigation(VisualElement workspace)
        {
            canvasNavigation = new VisualElement { name = "canvasNavigation" };
            canvasNavigation.AddToClassList("whimtex-canvas-navigation");
            canvasNavigation.EnableInClassList("whimtex-canvas-navigation--light", !EditorGUIUtility.isProSkin);
            var top = NavigationRow();
            canvasGuideCorner = NavigationCorner();
            canvasGuideCorner.name = "canvasGuideCorner";
            canvasGuideCorner.pickingMode = PickingMode.Position;
            canvasGuideCorner.tooltip = "Drag out a vertical and horizontal guide together. Right-click for guide settings.";
            canvasGuideCorner.RegisterCallback<PointerDownEvent>(canvasGuideManipulator.RulerDown);
            top.Add(canvasGuideCorner);
            top.Add(canvasGuideTopRail);
            top.Add(NavigationCorner());
            canvasNavigation.Add(top);
            var middle = NavigationRow();
            middle.AddToClassList("whimtex-canvas-navigation-body");
            middle.Add(canvasGuideLeftRail);
            middle.Add(workspace);
            canvasVerticalScrollBar = new CanvasScrollBar(toolkitCanvas, true) { name = "canvasVerticalScrollBar" };
            middle.Add(canvasVerticalScrollBar);
            canvasNavigation.Add(middle);
            var bottom = NavigationRow();
            bottom.Add(NavigationCorner());
            canvasHorizontalScrollBar = new CanvasScrollBar(toolkitCanvas, false) { name = "canvasHorizontalScrollBar" };
            bottom.Add(canvasHorizontalScrollBar);
            bottom.Add(NavigationCorner());
            canvasNavigation.Add(bottom);
            toolkitCanvas.ViewChanged += RefreshCanvasNavigation;
            toolkitCanvas.RegisterCallback<PointerMoveEvent>(evt =>
            {
                Vector2 point = toolkitCanvas.WorldToLocal(evt.position);
                bool visible = toolkitCanvas.contentRect.Contains(point) && HasCanvasLayers;
                ((CanvasRuler)canvasGuideTopRail).SetPointer(visible ? point.x : float.NaN);
                ((CanvasRuler)canvasGuideLeftRail).SetPointer(visible ? point.y : float.NaN);
            }, TrickleDown.TrickleDown);
            toolkitCanvas.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                ((CanvasRuler)canvasGuideTopRail).SetPointer(float.NaN);
                ((CanvasRuler)canvasGuideLeftRail).SetPointer(float.NaN);
            });
            toolkitCanvasViewHeader.RegisterCallback<GeometryChangedEvent>(_ => UpdateCanvasNavigationInset());
            canvasNavigation.RegisterCallback<GeometryChangedEvent>(_ => UpdateCanvasNavigationInset());
            return canvasNavigation;
        }

        private static VisualElement NavigationRow()
        {
            var row = new VisualElement();
            row.AddToClassList("whimtex-canvas-navigation-row");
            return row;
        }

        private static VisualElement NavigationCorner()
        {
            var corner = new VisualElement { pickingMode = PickingMode.Ignore };
            corner.AddToClassList("whimtex-canvas-navigation-corner");
            return corner;
        }

        private void UpdateCanvasNavigationInset()
        {
            if (canvasNavigation?.parent == null || toolkitCanvasViewHeader == null) return;
            float inset = Mathf.Max(0f, toolkitCanvasViewHeader.worldBound.yMax -
                (canvasNavigation.worldBound.yMin - canvasNavigation.resolvedStyle.marginTop));
            if (!float.IsNaN(inset) && !float.IsInfinity(inset) &&
                Mathf.Abs(canvasNavigation.resolvedStyle.marginTop - inset) > .1f)
                canvasNavigation.style.marginTop = inset;
        }

        private void RefreshCanvasNavigation()
        {
            ((CanvasRuler)canvasGuideTopRail).Refresh();
            ((CanvasRuler)canvasGuideLeftRail).Refresh();
            Rect view = toolkitCanvas.contentRect;
            Rect image = CanvasNavigationMetrics.PresentedBounds(canvasViewport, view, toolkitCanvas.ImageRect);
            canvasHorizontalScrollBar.SetRange(HasCanvasLayers
                ? CanvasNavigationMetrics.ScrollRange(view.xMin, view.width, image.xMin, image.width) : Vector3.zero);
            canvasVerticalScrollBar.SetRange(HasCanvasLayers
                ? CanvasNavigationMetrics.ScrollRange(view.yMin, view.height, image.yMin, image.height) : Vector3.zero);
        }

        private sealed class CanvasRuler : VisualElement
        {
            private readonly CanvasElement canvas;
            private readonly bool vertical;
            private readonly List<Label> labels = new List<Label>();
            private float origin, scale, major = 1f, pointer = float.NaN;

            internal CanvasRuler(CanvasElement canvas, bool vertical)
            {
                this.canvas = canvas;
                this.vertical = vertical;
                AddToClassList("whimtex-canvas-ruler");
                AddToClassList(vertical ? "whimtex-canvas-ruler--vertical" : "whimtex-canvas-ruler--horizontal");
                RegisterCallback<GeometryChangedEvent>(_ => Refresh());
                generateVisualContent += Draw;
            }

            internal void SetPointer(float value)
            {
                if (pointer.Equals(value)) return;
                pointer = value;
                MarkDirtyRepaint();
            }

            internal void Refresh()
            {
                Vector2 zero = canvas.ToView(canvas.ImageRect.position);
                origin = vertical ? zero.y : zero.x;
                scale = canvas.PixelScale;
                major = CanvasNavigationMetrics.MajorStep(scale);
                float length = vertical ? contentRect.height : contentRect.width;
                int used = 0;
                if (canvas.IsCanvasVisible && scale > 0f && length > 0f && !float.IsInfinity(origin))
                {
                    double first = Math.Ceiling(-origin / (double)(major * scale));
                    for (int i = 0; i < 256; i++)
                    {
                        double value = (first + i) * major;
                        float position = origin + (float)(value * scale);
                        if (position > length) break;
                        if (position < 0f) continue;
                        if (used == labels.Count)
                        {
                            var label = new Label { pickingMode = PickingMode.Ignore };
                            label.AddToClassList("whimtex-canvas-ruler-label");
                            if (vertical) label.AddToClassList("whimtex-canvas-ruler-label--vertical");
                            labels.Add(label);
                            Add(label);
                        }
                        Label text = labels[used++];
                        text.style.display = DisplayStyle.Flex;
                        text.text = value.ToString("0.###", CultureInfo.InvariantCulture);
                        text.style.width = Mathf.Max(1f, major * scale - 6f);
                        if (!vertical) text.style.left = position + 3f;
                        text.style.top = vertical ? position + 3f : 0f;
                    }
                }
                for (int i = used; i < labels.Count; i++) labels[i].style.display = DisplayStyle.None;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                if (!canvas.IsCanvasVisible || !(scale > 0f)) return;
                float length = vertical ? contentRect.height : contentRect.width;
                float thickness = vertical ? contentRect.width : contentRect.height;
                if (length < 1f || thickness < 1f) return;
                int divisions = major >= 10f && major * scale / 10f >= 5f ? 10 :
                    major >= 5f && major * scale / 5f >= 5f ? 5 : major >= 2f ? 2 : 1;
                float step = major / divisions;
                double first = Math.Floor(-origin / (double)(step * scale));
                var painter = context.painter2D;
                painter.strokeColor = resolvedStyle.color;
                painter.lineWidth = 1f;
                painter.BeginPath();
                for (int i = 0; i < 4096; i++)
                {
                    double index = first + i;
                    float position = origin + (float)(index * step * scale);
                    if (position > length) break;
                    if (position < 0f) continue;
                    bool primary = Math.Abs(index % divisions) < .001;
                    bool half = divisions == 10 && Math.Abs(index % divisions) == 5;
                    float size = primary ? thickness * .48f : half ? 3f : 2f;
                    position = Mathf.Round(position) + .5f;
                    painter.MoveTo(vertical ? new Vector2(thickness - size, position) : new Vector2(position, thickness - size));
                    painter.LineTo(vertical ? new Vector2(thickness, position) : new Vector2(position, thickness));
                }
                painter.Stroke();
                if (float.IsNaN(pointer)) return;
                painter.strokeColor = EditorGUIUtility.isProSkin ? new Color(.4f, .8f, .9f) : new Color(.05f, .35f, .45f);
                painter.BeginPath();
                painter.MoveTo(vertical ? new Vector2(0f, pointer) : new Vector2(pointer, 0f));
                painter.LineTo(vertical ? new Vector2(thickness, pointer) : new Vector2(pointer, thickness));
                painter.Stroke();
            }
        }

        private sealed class CanvasScrollBar : VisualElement
        {
            private readonly CanvasElement canvas;
            private readonly bool vertical;
            private readonly VisualElement track;
            private readonly VisualElement thumb;
            private readonly RepeatButton backward, forward;
            private const float ScrollStep = 24f;
            private Vector3 range;
            private int pointer = -1;
            private float start, dragOffset, dragScale;

            internal CanvasScrollBar(CanvasElement canvas, bool vertical)
            {
                this.canvas = canvas;
                this.vertical = vertical;
                tooltip = vertical ? "Scroll Canvas View vertically" : "Scroll Canvas View horizontally";
                AddToClassList("whimtex-canvas-scrollbar");
                AddToClassList(vertical ? "whimtex-canvas-scrollbar--vertical" : "whimtex-canvas-scrollbar--horizontal");
                backward = CreateArrow(-1);
                forward = CreateArrow(1);
                Add(backward);
                track = new VisualElement { name = "canvasScrollTrack" };
                track.AddToClassList("whimtex-canvas-scrollbar-track");
                Add(track);
                thumb = new VisualElement { pickingMode = PickingMode.Ignore };
                thumb.AddToClassList("whimtex-canvas-scrollbar-thumb");
                track.Add(thumb);
                Add(forward);
                track.RegisterCallback<GeometryChangedEvent>(_ => { EndDrag(); RefreshThumb(); });
                track.RegisterCallback<PointerDownEvent>(Down);
                track.RegisterCallback<PointerMoveEvent>(Move);
                track.RegisterCallback<PointerUpEvent>(Up);
                track.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
                track.RegisterCallback<PointerCancelEvent>(_ => EndDrag());
                RegisterCallback<DetachFromPanelEvent>(_ => EndDrag());
                RegisterCallback<WheelEvent>(Wheel);
            }

            private RepeatButton CreateArrow(int direction)
            {
                var button = new RepeatButton(() =>
                {
                    EndDrag();
                    Pan(-direction * ScrollStep);
                }, 350, 50)
                {
                    name = direction < 0 ? "canvasScrollBackward" : "canvasScrollForward",
                    focusable = false,
                    tooltip = vertical ? direction < 0 ? "Scroll up (hold to repeat)" : "Scroll down (hold to repeat)"
                        : direction < 0 ? "Scroll left (hold to repeat)" : "Scroll right (hold to repeat)"
                };
                button.AddToClassList("whimtex-canvas-scrollbar-arrow");
                button.Add(new CanvasScrollArrow(vertical, direction));
                return button;
            }

            private float Length => vertical ? track.contentRect.height : track.contentRect.width;
            private float Coordinate(Vector2 point) => vertical ? point.y : point.x;
            private bool CanScroll => range.z > 0f && Length > 0f;

            internal void SetRange(Vector3 value)
            {
                range = value;
                if (!CanScroll && pointer >= 0) EndDrag();
                RefreshThumb();
            }

            private void RefreshThumb()
            {
                if (!(Length > 0f) || float.IsInfinity(Length)) return;
                Vector2 geometry = CanvasNavigationMetrics.ScrollThumb(range, Length);
                EnableInClassList("whimtex-canvas-scrollbar--disabled", !CanScroll);
                backward.SetEnabled(CanScroll);
                forward.SetEnabled(CanScroll);
                if (vertical) { thumb.style.top = geometry.x; thumb.style.height = geometry.y; }
                else { thumb.style.left = geometry.x; thumb.style.width = geometry.y; }
            }

            private void Pan(float delta)
            {
                if (CanScroll && Mathf.Abs(delta) > .001f && !float.IsInfinity(delta))
                    canvas.Pan(vertical ? new Vector2(0f, delta) : new Vector2(delta, 0f));
            }

            private void Down(PointerDownEvent evt)
            {
                if (evt.button != 0 || pointer >= 0) return;
                WhimTexUI.ConsumeEvent(evt);
                if (!CanScroll) return;
                float position = Coordinate(evt.localPosition);
                Vector2 geometry = CanvasNavigationMetrics.ScrollThumb(range, Length);
                if (position < geometry.x || position > geometry.x + geometry.y)
                {
                    Pan((position < geometry.x ? 1f : -1f) * range.z * .9f);
                    return;
                }
                pointer = evt.pointerId;
                start = position;
                dragOffset = 0f;
                dragScale = CanvasNavigationMetrics.ScrollDragScale(range, Length);
                track.CapturePointer(pointer);
                AddToClassList("whimtex-canvas-scrollbar--dragging");
            }

            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer) return;
                if ((evt.pressedButtons & 1) == 0) { EndDrag(); return; }
                float next = (Coordinate(evt.localPosition) - start) * dragScale;
                float delta = dragOffset - next;
                dragOffset = next;
                Pan(delta);
                RefreshThumb();
                WhimTexUI.ConsumeEvent(evt);
            }

            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                EndDrag();
                WhimTexUI.ConsumeEvent(evt);
            }

            private void EndDrag()
            {
                int captured = pointer;
                pointer = -1;
                if (captured >= 0 && track.HasPointerCapture(captured)) track.ReleasePointer(captured);
                RemoveFromClassList("whimtex-canvas-scrollbar--dragging");
                RefreshThumb();
            }

            private void Wheel(WheelEvent evt)
            {
                if (pointer >= 0) EndDrag();
                float delta = vertical ? evt.delta.y : Mathf.Abs(evt.delta.x) > .01f ? evt.delta.x : evt.delta.y;
                Pan(-delta * ScrollStep);
                WhimTexUI.ConsumeEvent(evt);
            }
        }

        private sealed class CanvasScrollArrow : VisualElement
        {
            internal CanvasScrollArrow(bool vertical, int direction)
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-canvas-scrollbar-arrow-icon");
                generateVisualContent += context =>
                {
                    if (contentRect.width < 1f || contentRect.height < 1f) return;
                    Vector2 center = contentRect.center;
                    Vector2 axis = vertical ? Vector2.up * direction : Vector2.right * direction;
                    Vector2 side = new Vector2(-axis.y, axis.x);
                    var painter = context.painter2D;
                    painter.fillColor = resolvedStyle.color;
                    painter.BeginPath();
                    painter.MoveTo(center + axis * 3f);
                    painter.LineTo(center - axis * 2f + side * 3f);
                    painter.LineTo(center - axis * 2f - side * 3f);
                    painter.ClosePath();
                    painter.Fill();
                };
            }
        }
    }
}
