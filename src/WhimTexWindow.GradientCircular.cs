using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed partial class GradientCanvasManipulator
        {
            private const int CircularCenter = -4, CircularRotate = -5,
                CircularStart = -6, CircularEnd = -7, CircularJoined = -8;
            private bool circularGesture;
            private Vector2 circularPointerPosition;
            private float originalCircularAngle, originalCircularRepetitions, lastCircularAngle, circularDelta;
            private bool IsCircular => owner.gradientCanvasLayer.IsValid && owner.gradientCanvasLayer.gradientType == GradientLayerBehaviour.GradientType.Circular;
            private float CircularSpan => CircularGradientGeometry.Span(owner.gradientCanvasLayer.circularRepetitions);
            private bool CircularClosed => Mathf.Abs(CircularSpan - 360f) < .001f;

            private Vector2 CircularCenterPoint => View(GradientCanvasGeometry.Point(
                owner.gradientCanvasLayer.CircularCenter, owner.gradientCanvasLayer.CanvasTransform, Size));

            private Vector2 CircularPoint(float time) => CircularPointAtAngle(
                owner.gradientCanvasLayer.circularStartAngle + CircularSpan * time);

            private Vector2 CircularPointAtAngle(float angle)
            {
                Vector2 uv = owner.gradientCanvasLayer.CircularUv(angle);
                return View(GradientCanvasGeometry.Point(uv, owner.gradientCanvasLayer.CanvasTransform, Size));
            }

            private float CircularAngle(Vector2 view)
            {
                Vector2 pixel = Document(view);
                Vector2 uv = owner.gradientCanvasLayer.CanvasTransform.Unmap(
                    new Vector2(pixel.x / Size.x, pixel.y / Size.y), Size);
                return owner.gradientCanvasLayer.CircularAngle(uv);
            }

            private float CircularProjection(Vector2 p)
            {
                float angle = CircularAngle(p) - owner.gradientCanvasLayer.circularStartAngle;
                if (pointer >= 0 && handle >= 0)
                {
                    var keys = originalGradient.ColorKeys;
                    float reference = midpointSelected
                        ? Mathf.Lerp(keys[handle].time, keys[handle + 1].time, originalGradient.GetMidpoint(false, handle))
                        : keys[handle].time;
                    return CircularGradientGeometry.Project(angle, CircularSpan, reference);
                }
                return Mathf.Clamp01(Mathf.Repeat(angle, 360f) / CircularSpan);
            }

            private bool CircularVisible(float time) => CircularSpan * time <= 360.001f;

            private int CircularEndpointKey(bool first)
            {
                var keys = owner.gradientCanvasLayer.gradient.ColorKeys;
                for (int i = 0; i < keys.Length; i++)
                    if (Mathf.Abs(keys[i].time - (first ? 0f : 1f)) < .00001f) return i;
                return -1;
            }

            private bool CircularFirstHalf(Vector2 p)
            {
                Vector2 seam = CircularPoint(0);
                Vector2 tangent = CircularPointAtAngle(owner.gradientCanvasLayer.circularStartAngle + .1f) - seam;
                return Vector2.Dot(p - seam, tangent) >= 0f;
            }

            private void CircularDown(PointerDownEvent e)
            {
                owner.activeDocument.RefreshTransformHierarchy();
                Vector2 p = e.localPosition, center = CircularCenterPoint, first = CircularPoint(0);
                if (!float.IsFinite(CircularAngle(p))) return;
                var g = owner.gradientCanvasLayer.gradient;
                var keys = g.ColorKeys;
                int hit = -1, mid = -1, endpointKey = -1;
                bool firstEndpoint = true;
                if ((p - center).sqrMagnitude <= 81) hit = CircularCenter;
                else if ((p - first).sqrMagnitude <= 121)
                {
                    hit = CircularClosed ? CircularJoined : CircularStart;
                    firstEndpoint = !CircularClosed || CircularFirstHalf(p);
                    endpointKey = CircularEndpointKey(firstEndpoint);
                }
                else if (CircularSpan < 360f && (p - CircularPoint(1)).sqrMagnitude <= 121)
                {
                    hit = CircularEnd; firstEndpoint = false; endpointKey = CircularEndpointKey(false);
                }
                if (hit == -1)
                {
                    float nearest = 100;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (!CircularVisible(keys[i].time)) continue;
                        float distance = (p - CircularPoint(keys[i].time)).sqrMagnitude;
                        if (distance <= nearest) { nearest = distance; hit = i; }
                    }
                }
                if (hit == -1 && g.Mode != WhimTexGradientMode.Fixed)
                    for (int i = 0; i < keys.Length - 1; i++)
                    {
                        float t = Mathf.Lerp(keys[i].time, keys[i + 1].time, g.GetMidpoint(false, i));
                        if (CircularVisible(t) && (p - CircularPoint(t)).sqrMagnitude <= 64) { mid = i; break; }
                    }
                if (hit == -1 && mid < 0)
                {
                    Vector2 ray = first - center;
                    float along = Vector2.Dot(p - center, ray) / Mathf.Max(.0001f, ray.sqrMagnitude);
                    if (along > 0 && along < 1 && (p - center - along * ray).sqrMagnitude <= 36)
                        hit = CircularRotate;
                }
                float time = CircularProjection(p);
                if (hit == -1 && mid < 0 && (p - CircularPoint(time)).sqrMagnitude > 36) { SelectTextCharacter(e); return; }
                owner.Focus(); target.Focus();
                midpointSelected = mid >= 0;
                if (midpointSelected) hit = mid;
                selectionLayer = owner.gradientCanvasLayer;
                selected = hit >= 0 ? hit : endpointKey;
                if (e.clickCount == 2 && !midpointSelected && (hit >= 0 || hit <= CircularStart))
                {
                    if (selected < 0 && keys.Length < 64)
                    {
                        BeginUndo();
                        float endpoint = firstEndpoint ? 0 : 1;
                        Array.Resize(ref keys, keys.Length + 1);
                        keys[keys.Length - 1] = new GradientColorKey(g.Evaluate(endpoint), endpoint);
                        Array.Sort(keys, (x, y) => x.time.CompareTo(y.time));
                        var next = g.Clone(); next.SetKeys(keys, g.AlphaKeys); selectionLayer.gradient = next;
                        selected = Array.FindIndex(keys, k => k.time == endpoint);
                        Commit();
                    }
                    if (selected >= 0) OpenColor(selected);
                    owner.gradientCanvasOverlay.MarkDirtyRepaint();
                    e.StopImmediatePropagation(); return;
                }
                if (hit == -1)
                {
                    if (keys.Length >= 64) return;
                    foreach (var key in keys) if (Mathf.Abs(key.time - time) < .00001f) return;
                    BeginUndo();
                    Array.Resize(ref keys, keys.Length + 1);
                    keys[keys.Length - 1] = new GradientColorKey(g.Evaluate(time), time);
                    Array.Sort(keys, (x, y) => x.time.CompareTo(y.time));
                    var next = g.Clone(); next.SetKeys(keys, g.AlphaKeys); selectionLayer.gradient = next;
                    selected = Array.FindIndex(keys, k => k.time == time);
                    Commit(); e.StopImmediatePropagation(); return;
                }
                layer = owner.gradientCanvasLayer;
                originalGradient = g.Clone(); CaptureGeometry();
                originalCircularAngle = layer.circularStartAngle;
                originalCircularRepetitions = layer.circularRepetitions;
                lastCircularAngle = CircularAngle(p); circularDelta = 0; circularPointerPosition = p;
                pointerStart = Document(p); handle = hit; pendingRemoval = false; circularGesture = true;
                pointer = e.pointerId; target.CapturePointer(pointer);
                owner.gradientCanvasOverlay.MarkDirtyRepaint(); e.StopImmediatePropagation();
            }

            private void CircularControlChanged(bool control)
            {
                Validate();
                if (pointer < 0 || !circularGesture || handle > CircularStart ||
                    CircularGradientGeometry.Span(originalCircularRepetitions) > 360.001f) return;
                BeginUndo();
                CircularMove(circularPointerPosition, control);
                owner.RequestTransformCanvas(); owner.gradientCanvasOverlay.MarkDirtyRepaint();
            }

            private void CircularMove(Vector2 p, bool control)
            {
                circularPointerPosition = p;
                if (handle == CircularCenter)
                {
                    layer.MoveEndpoint(owner.activeDocument, Size, Vector2.zero, Vector2.right, true,
                        Document(p) - pointerStart, originalTransform, originalFillStart, originalFillEnd);
                    return;
                }
                float angle = CircularAngle(p);
                if (!float.IsFinite(angle)) return;
                circularDelta += Mathf.DeltaAngle(lastCircularAngle, angle); lastCircularAngle = angle;
                // Rebuild fill direction from the gesture snapshot, avoiding cumulative radius drift.
                if (layer.IsFill) { layer.FillStart = originalFillStart; layer.FillEnd = originalFillEnd; }
                if (handle == CircularRotate || CircularGradientGeometry.Span(originalCircularRepetitions) > 360.001f)
                {
                    layer.circularStartAngle = Mathf.Repeat(originalCircularAngle + circularDelta, 360f);
                    return;
                }
                if (handle == CircularJoined)
                {
                    if (Mathf.Abs(circularDelta) < .05f) return;
                    handle = circularDelta > 0 ? CircularStart : CircularEnd;
                }
                float radius = Vector2.Distance(CircularPoint(0), CircularCenterPoint);
                float snap = Mathf.Clamp(6f / Mathf.Max(radius, 1f) * Mathf.Rad2Deg, .1f, 3f);
                CircularGradientGeometry.MoveBoundary(originalCircularAngle,
                    CircularGradientGeometry.Span(originalCircularRepetitions), circularDelta,
                    handle == CircularStart, snap, out float nextAngle, out float nextSpan, control);
                layer.circularStartAngle = nextAngle;
                // Store exact integer repetitions, not a round-trip division approximation.
                layer.circularRepetitions = control ? Mathf.Round(360f / nextSpan) : 360f / nextSpan;
            }

            private Color CircularKeyColor(float time)
            {
                var gradient = owner.gradientCanvasLayer.gradient;
                int index = time == 0 ? CircularEndpointKey(true) : time == 1 ? CircularEndpointKey(false) : -1;
                Color color = index >= 0 ? gradient.ColorKeys[index].color : gradient.Evaluate(time);
                if (gradient.ColorSpace == ColorSpace.Linear) color = HdrUtility.Encode(color);
                color /= Mathf.Max(1, Mathf.Max(color.r, Mathf.Max(color.g, color.b))); color.a = 1;
                return color;
            }

            private static void CircularDisc(Painter2D p, Vector2 point, Color color, bool highlighted)
            {
                p.fillColor = Color.black;
                p.BeginPath(); p.Arc(point, highlighted ? 10.6f : 9.1f, 0, 360); p.Fill();
                if (highlighted)
                {
                    p.fillColor = new Color(.4f, .85f, 1);
                    p.BeginPath(); p.Arc(point, 9.6f, 0, 360); p.Fill();
                }
                p.fillColor = Color.white;
                p.BeginPath(); p.Arc(point, 8.1f, 0, 360); p.Fill();
                p.fillColor = color;
                p.BeginPath(); p.Arc(point, 7.1f, 0, 360); p.Fill();
            }

            private void DrawCircular(Painter2D p)
            {
                owner.activeDocument.RefreshTransformHierarchy();
                Vector2 center = CircularCenterPoint, first = CircularPoint(0);
                if (!float.IsFinite(first.x) || !float.IsFinite(first.y)) return;
                float span = Mathf.Min(360, CircularSpan);
                int segments = Mathf.Max(8, Mathf.CeilToInt(span / 3f));
                if (!CircularClosed && span < 360f)
                {
                    int remainingSegments = Mathf.Max(1, Mathf.CeilToInt((360f - span) / 3f));
                    for (int pass = 0; pass < 2; pass++)
                    {
                        p.lineWidth = pass == 0 ? 3 : 1;
                        p.strokeColor = pass == 0 ? new Color(0, 0, 0, .12f) : new Color(.4f, .85f, 1, .3f);
                        p.BeginPath(); p.MoveTo(CircularPoint(1));
                        for (int i = 1; i <= remainingSegments; i++)
                            p.LineTo(i == remainingSegments ? first : CircularPointAtAngle(
                                owner.gradientCanvasLayer.circularStartAngle + span + (360f - span) * i / remainingSegments));
                        p.Stroke();
                    }
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    p.lineWidth = pass == 0 ? 4 : 2;
                    p.strokeColor = pass == 0 ? new Color(0, 0, 0, .3f) : new Color(.4f, .85f, 1, .7f);
                    p.BeginPath(); p.MoveTo(center); p.LineTo(first);
                    for (int i = 1; i <= segments; i++)
                        p.LineTo(CircularPointAtAngle(owner.gradientCanvasLayer.circularStartAngle + span * i / segments));
                    if (!CircularClosed && CircularSpan <= 360) p.LineTo(center);
                    p.Stroke();
                }
                Square(p, center);
                var g = owner.gradientCanvasLayer.gradient;
                var keys = g.ColorKeys;
                if (g.Mode != WhimTexGradientMode.Fixed)
                    for (int i = 0; i < keys.Length - 1; i++)
                    {
                        float t = Mathf.Lerp(keys[i].time, keys[i + 1].time, g.GetMidpoint(false, i));
                        if (CircularVisible(t)) Diamond(p, CircularPoint(t), midpointSelected && selected == i && selectionLayer == owner.gradientCanvasLayer);
                    }
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].time <= .00001f || keys[i].time >= .99999f || !CircularVisible(keys[i].time) || pendingRemoval && selected == i) continue;
                    Color color = keys[i].color;
                    if (g.ColorSpace == ColorSpace.Linear) color = HdrUtility.Encode(color);
                    color /= Mathf.Max(1, Mathf.Max(color.r, Mathf.Max(color.g, color.b))); color.a = 1;
                    CircularDisc(p, CircularPoint(keys[i].time), color,
                        !midpointSelected && selected == i && selectionLayer == owner.gradientCanvasLayer);
                }
                bool firstSelected = !midpointSelected && selected >= 0 && selected == CircularEndpointKey(true) && selectionLayer == owner.gradientCanvasLayer;
                bool endSelected = !midpointSelected && selected >= 0 && selected == CircularEndpointKey(false) && selectionLayer == owner.gradientCanvasLayer;
                CircularDisc(p, first, CircularKeyColor(0), firstSelected || CircularClosed && endSelected);
                if (CircularClosed)
                {
                    // Each half faces its side of the seam, including mirrored/projective transforms.
                    Vector2 tangent = (CircularPointAtAngle(owner.gradientCanvasLayer.circularStartAngle + .1f) - first).normalized;
                    float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
                    p.fillColor = CircularKeyColor(1);
                    p.BeginPath(); p.MoveTo(first); p.Arc(first, 7.1f, angle + 90, angle + 270); p.ClosePath(); p.Fill();
                    Vector2 dividing = new Vector2(-tangent.y, tangent.x) * 7.1f;
                    p.strokeColor = new Color(.15f, .15f, .15f); p.lineWidth = 1;
                    p.BeginPath(); p.MoveTo(first - dividing); p.LineTo(first + dividing); p.Stroke();
                }
                else if (CircularSpan < 360) CircularDisc(p, CircularPoint(1), CircularKeyColor(1), endSelected);
            }
        }
    }

    internal static class CircularGradientGeometry
    {
        internal static float Span(float repetitions) => 360f / Mathf.Max(.0001f, repetitions);

        internal static float Project(float relativeAngle, float span, float referenceTime) =>
            Mathf.Clamp01((referenceTime * span + Mathf.DeltaAngle(referenceTime * span, relativeAngle)) / span);

        internal static void MoveBoundary(float start, float span, float delta, bool first, float snap,
            out float nextStart, out float nextSpan, bool snapRepetitions = false)
        {
            nextSpan = Mathf.Clamp(span + (first ? -delta : delta), 1f, 360f);
            if (360f - nextSpan <= snap) nextSpan = 360f;
            if (snapRepetitions) nextSpan = 360f / Mathf.Max(1f, Mathf.Round(360f / nextSpan));
            nextStart = Mathf.Repeat(first ? start + span - nextSpan : start, 360f);
        }
    }
}
