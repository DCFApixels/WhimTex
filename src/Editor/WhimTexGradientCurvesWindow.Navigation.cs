using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientCurvesWindow
    {
        // View coordinates need more precision than float keys at deep zoom levels.
        [Serializable]
        private struct CurveView
        {
            public double x, y, width, height;
            internal double xMin => x;
            internal double yMin => y;
            internal double yMax => y + height;
            internal CurveView(double x, double y, double width, double height)
            { this.x = x; this.y = y; this.width = width; this.height = height; }
        }

        private const double CurveRangeStep = .25;
        private CurveScrollBar horizontalScroll, verticalScroll;
        private WhimTexGradient rangeGradient;
        private uint rangeRevision;
        private int rangeChannels = -1;
        private double curveMinimum, curveMaximum = 1;

        private void BuildNavigation(VisualElement graph)
        {
            horizontalScroll = new CurveScrollBar(false, (start, span) => ChangeViewAxis(false, start, span))
                { name = "gradientHorizontalScroll" };
            verticalScroll = new CurveScrollBar(true, (start, span) => ChangeViewAxis(true, start, span))
                { name = "gradientVerticalScroll" };
            graph.Add(horizontalScroll); graph.Add(verticalScroll);
        }

        private static bool IsValidViewAxis(double start, double span, double pixels)
        {
            if (!double.IsFinite(start) || !double.IsFinite(span) || !(span > 0)) return false;
            double end = start + span, pixel = span / Math.Max(1, pixels);
            return double.IsFinite(end) && end > start && pixel > 0 && start + pixel > start && end - pixel < end;
        }

        private void ChangeViewAxis(bool vertical, double start, double span)
        {
            if (pointerDown || !IsValidViewAxis(start, span, vertical ? plot.contentRect.height : plot.contentRect.width)) return;
            if (vertical) { view.y = start; view.height = span; }
            else { view.x = start; view.width = span; }
            UpdateAxes(); plot?.MarkDirtyRepaint();
        }

        private void UpdateCurveRange()
        {
            if (ReferenceEquals(rangeGradient, gradient) && rangeChannels == visibleChannels &&
                (gradient == null || rangeRevision == gradient.Revision)) return;
            rangeGradient = gradient; rangeChannels = visibleChannels;
            rangeRevision = gradient != null ? gradient.Revision : 0;
            double minimum = 0, maximum = 1;
            if (gradient != null)
                for (int c = 0; c < 4; c++) if ((visibleChannels & (1 << c)) != 0)
                {
                    for (int i = 0; i <= 256; i++)
                    {
                        float value = gradient.EvaluateCustomChannel(c, i / 256f);
                        if (!float.IsFinite(value)) continue;
                        minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value);
                    }
                    foreach (var key in keys) if (key.channel == c)
                    { minimum = Math.Min(minimum, key.value); maximum = Math.Max(maximum, key.value); }
                }
            curveMinimum = CurveRangeBoundary(minimum, false);
            curveMaximum = CurveRangeBoundary(maximum, true);
        }

        private static double CurveRangeBoundary(double value, bool upper)
        {
            double units = value / CurveRangeStep;
            double nearest = Math.Round(units) * CurveRangeStep;
            if (Math.Abs(value - nearest) <= 1e-7) return nearest;
            return (upper ? Math.Ceiling(units) : Math.Floor(units)) * CurveRangeStep;
        }

        private void UpdateNavigation()
        {
            UpdateCurveRange();
            horizontalScroll?.SetRange(view.x, view.width, 0, 1);
            verticalScroll?.SetRange(view.y, view.height, curveMinimum, curveMaximum);
        }

        private sealed class CurveScrollBar : VisualElement
        {
            private readonly bool vertical;
            private readonly Action<double, double> changed;
            private readonly VisualElement thumb, lowGrip, highGrip;
            private readonly ScrollDrag drag;
            private double start, span = 1, domainMin = -1, domainSpan = 3;
            private float thumbStart, thumbLength;
            private float Length => vertical ? contentRect.height : contentRect.width;

            internal CurveScrollBar(bool vertical, Action<double, double> changed)
            {
                this.vertical = vertical; this.changed = changed; focusable = true;
                AddToClassList("whimtex-curve-scroll");
                AddToClassList(vertical ? "whimtex-curve-scroll-y" : "whimtex-curve-scroll-x");
                thumb = new VisualElement { name = "viewRange" };
                thumb.AddToClassList("whimtex-curve-scroll-thumb"); Add(thumb);
                lowGrip = Grip("rangeStart", "whimtex-curve-scroll-low");
                highGrip = Grip("rangeEnd", "whimtex-curve-scroll-high");
                thumb.Add(lowGrip); thumb.Add(highGrip);
                drag = new ScrollDrag(this); this.AddManipulator(drag);
                RegisterCallback<GeometryChangedEvent>(_ => { FinishDrag(); RefreshThumb(); });
                RegisterCallback<DetachFromPanelEvent>(_ => FinishDrag());
            }

            private static VisualElement Grip(string name, string side)
            {
                var grip = new VisualElement { name = name };
                grip.AddToClassList("whimtex-curve-scroll-grip"); grip.AddToClassList(side);
                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("whimtex-curve-scroll-grip-mark"); grip.Add(mark);
                return grip;
            }

            internal void FinishDrag() => drag.Finish(false);

            internal void SetRange(double minimum, double size, double contentMin, double contentMax)
            {
                start = minimum; span = size;
                // The fixed normalized navigation range is [-1, 2], independent of pan history.
                double baseSpan = contentMax - contentMin;
                domainMin = contentMin - baseSpan; domainSpan = 3 * baseSpan;
                RefreshThumb();
            }

            private void RefreshThumb()
            {
                float length = Length;
                if (!(length > 0) || !float.IsFinite(length)) return;
                double size = Math.Min(1, Math.Max(.002, span / domainSpan));
                double offset = vertical ? (domainMin + domainSpan - start - span) / domainSpan : (start - domainMin) / domainSpan;
                thumbLength = (float)(size * length);
                thumbStart = (float)(Math.Min(1 - size, Math.Max(0, offset)) * length);
                if (vertical) { thumb.style.top = thumbStart; thumb.style.height = thumbLength; }
                else { thumb.style.left = thumbStart; thumb.style.width = thumbLength; }
            }

            private float Coordinate(Vector3 position)
            {
                Vector2 point = this.WorldToLocal(position);
                return vertical ? point.y : point.x;
            }

            private sealed class ScrollDrag : PointerManipulator
            {
                private readonly CurveScrollBar bar;
                private int pointer = -1, edge;
                private float origin;
                private double originalStart, originalSpan, unitsPerPixel;
                internal bool Active => pointer >= 0;

                internal ScrollDrag(CurveScrollBar bar) { this.bar = bar; }
                protected override void RegisterCallbacksOnTarget()
                {
                    target.RegisterCallback<PointerDownEvent>(Down);
                    target.RegisterCallback<PointerMoveEvent>(Move);
                    target.RegisterCallback<PointerUpEvent>(Up);
                    target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                    target.RegisterCallback<PointerCancelEvent>(Cancel);
                    target.RegisterCallback<KeyDownEvent>(Key);
                }
                protected override void UnregisterCallbacksFromTarget()
                {
                    Finish(false);
                    target.UnregisterCallback<PointerDownEvent>(Down);
                    target.UnregisterCallback<PointerMoveEvent>(Move);
                    target.UnregisterCallback<PointerUpEvent>(Up);
                    target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                    target.UnregisterCallback<PointerCancelEvent>(Cancel);
                    target.UnregisterCallback<KeyDownEvent>(Key);
                }
                private void Down(PointerDownEvent e)
                {
                    if (e.button != 0 || Active || !(bar.Length > 0)) return;
                    target.Focus(); origin = bar.Coordinate(e.position);
                    edge = ReferenceEquals(e.target, bar.lowGrip) ? -1 : ReferenceEquals(e.target, bar.highGrip) ? 1 : 0;
                    if (ReferenceEquals(e.target, bar))
                    {
                        double fraction = origin / bar.Length;
                        if (bar.vertical) fraction = 1 - fraction;
                        bar.changed(bar.domainMin + fraction * bar.domainSpan - bar.span * .5, bar.span);
                        e.StopPropagation(); return;
                    }
                    originalStart = bar.start; originalSpan = bar.span;
                    unitsPerPixel = bar.domainSpan / bar.Length * (bar.vertical ? -1 : 1);
                    pointer = e.pointerId; target.CapturePointer(pointer);
                    target.AddToClassList("whimtex-curve-scroll-dragging");
                    e.StopPropagation();
                }
                private void Move(PointerMoveEvent e)
                {
                    if (e.pointerId != pointer) return;
                    if ((e.pressedButtons & 1) == 0) { Finish(false); return; }
                    double delta = (bar.Coordinate(e.position) - origin) * unitsPerPixel;
                    if (edge == 0) bar.changed(originalStart + delta, originalSpan);
                    else
                    {
                        bar.changed(originalStart + (edge < 0 ? delta : 0), originalSpan + edge * delta);
                    }
                    e.StopPropagation();
                }
                private void Up(PointerUpEvent e)
                {
                    if (e.pointerId != pointer || e.button != 0) return;
                    Finish(false); e.StopPropagation();
                }
                private void Lost(PointerCaptureOutEvent e) { if (e.pointerId == pointer) Finish(false); }
                private void Cancel(PointerCancelEvent e) { if (e.pointerId == pointer) Finish(true); }
                private void Key(KeyDownEvent e)
                {
                    if (!Active || e.keyCode != KeyCode.Escape) return;
                    Finish(true); e.StopPropagation();
                }
                internal void Finish(bool cancel)
                {
                    if (!Active) return;
                    if (cancel) bar.changed(originalStart, originalSpan);
                    int captured = pointer; pointer = -1;
                    if (target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                    target.RemoveFromClassList("whimtex-curve-scroll-dragging");
                    bar.RefreshThumb();
                }
            }
        }
    }
}
