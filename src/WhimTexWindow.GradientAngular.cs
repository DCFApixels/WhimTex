using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed partial class GradientCanvasManipulator
        {
            private const int AngularCenter = -4, AngularRotate = -5,
                AngularStart = -6, AngularEnd = -7, AngularJoined = -8;
            private bool angularGesture;
            private Vector2 angularPointerPosition;
            private float originalAngularAngle, originalAngularRepetitions, lastAngularAngle, angularDelta;
            private bool IsAngular => owner.gradientCanvasLayer.IsValid && owner.gradientCanvasLayer.gradientType == GradientLayerBehaviour.GradientType.Angular;
            private float AngularSpan => AngularGradientGeometry.Span(owner.gradientCanvasLayer.circularRepetitions);
            private bool AngularClosed => Mathf.Abs(AngularSpan - 360f) < .001f;

            private Vector2 AngularCenterPoint => View(GradientCanvasGeometry.Point(
                owner.gradientCanvasLayer.AngularCenter, owner.gradientCanvasLayer.CanvasTransform, Size));

            private Vector2 AngularPoint(float time) => AngularPointAtAngle(
                owner.gradientCanvasLayer.circularStartAngle + AngularSpan * time);

            private Vector2 AngularPointAtAngle(float angle)
            {
                Vector2 uv = owner.gradientCanvasLayer.AngularUv(angle);
                return View(GradientCanvasGeometry.Point(uv, owner.gradientCanvasLayer.CanvasTransform, Size));
            }

            private float AngularAngle(Vector2 view)
            {
                Vector2 pixel = Document(view);
                Vector2 uv = owner.gradientCanvasLayer.CanvasTransform.Unmap(
                    new Vector2(pixel.x / Size.x, pixel.y / Size.y), Size);
                return owner.gradientCanvasLayer.AngularAngle(uv);
            }

            private float AngularProjection(Vector2 p)
            {
                float angle = AngularAngle(p) - owner.gradientCanvasLayer.circularStartAngle;
                if (pointer >= 0 && handle >= 0)
                {
                    var keys = originalGradient.ColorKeys;
                    float reference = midpointSelected
                        ? Mathf.Lerp(keys[handle].time, keys[handle + 1].time, originalGradient.GetMidpoint(false, handle))
                        : keys[handle].time;
                    return AngularGradientGeometry.Project(angle, AngularSpan, reference);
                }
                return Mathf.Clamp01(Mathf.Repeat(angle, 360f) / AngularSpan);
            }

            private bool AngularVisible(float time) => AngularSpan * time <= 360.001f;

            private int AngularEndpointKey(bool first)
            {
                var keys = GradientHitKeys(owner.gradientCanvasLayer.gradient);
                for (int i = 0; i < keys.Length; i++)
                    if (Mathf.Abs(keys[i].time - (first ? 0f : 1f)) < .00001f) return i;
                return -1;
            }

            private bool AngularFirstHalf(Vector2 p)
            {
                Vector2 seam = AngularPoint(0);
                Vector2 tangent = AngularPointAtAngle(owner.gradientCanvasLayer.circularStartAngle + .1f) - seam;
                return Vector2.Dot(p - seam, tangent) >= 0f;
            }

            private bool HitAngular(Vector2 p, out int hit, out int mid, out float time, out int endpointKey, out bool firstEndpoint)
            {
                hit = mid = endpointKey = -1; time = 0; firstEndpoint = true;
                owner.activeDocument.RefreshTransformHierarchy();
                Vector2 center = AngularCenterPoint, first = AngularPoint(0);
                if (!float.IsFinite(AngularAngle(p))) return false;
                var g = owner.gradientCanvasLayer.gradient;
                var keys = GradientHitKeys(g);
                if ((p - center).sqrMagnitude <= 81) hit = AngularCenter;
                else if ((p - first).sqrMagnitude <= 121)
                {
                    hit = AngularClosed ? AngularJoined : AngularStart;
                    firstEndpoint = !AngularClosed || AngularFirstHalf(p);
                    endpointKey = AngularEndpointKey(firstEndpoint);
                }
                else if (AngularSpan < 360f && (p - AngularPoint(1)).sqrMagnitude <= 121)
                {
                    hit = AngularEnd; firstEndpoint = false; endpointKey = AngularEndpointKey(false);
                }
                if (hit == -1)
                {
                    float nearest = 100;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (!AngularVisible(keys[i].time)) continue;
                        float distance = (p - AngularPoint(keys[i].time)).sqrMagnitude;
                        if (distance <= nearest) { nearest = distance; hit = i; }
                    }
                }
                if (hit == -1 && g.Mode != WhimTexGradientMode.Fixed)
                    for (int i = 0; i < keys.Length - 1; i++)
                    {
                        float t = Mathf.Lerp(keys[i].time, keys[i + 1].time, g.GetMidpoint(false, i));
                        if (AngularVisible(t) && (p - AngularPoint(t)).sqrMagnitude <= 64) { mid = i; break; }
                    }
                if (hit == -1 && mid < 0)
                {
                    Vector2 ray = first - center;
                    float along = Vector2.Dot(p - center, ray) / Mathf.Max(.0001f, ray.sqrMagnitude);
                    if (along > 0 && along < 1 && (p - center - along * ray).sqrMagnitude <= 36)
                        hit = AngularRotate;
                }
                time = AngularProjection(p);
                return hit != -1 || mid >= 0 || (p - AngularPoint(time)).sqrMagnitude <= 36;
            }
            private void AngularDown(PointerDownEvent e)
            {
                Vector2 p = e.localPosition;
                if (!HitAngular(p, out int hit, out int mid, out float time, out int endpointKey, out bool firstEndpoint))
                { SelectTextCharacter(e); return; }
                var g = owner.gradientCanvasLayer.gradient;
                var keys = g.ColorKeys;
                owner.Focus(); target.Focus();
                midpointSelected = mid >= 0;
                if (midpointSelected) hit = mid;
                selectionLayer = owner.gradientCanvasLayer;
                selected = hit >= 0 ? hit : endpointKey;
                if (e.clickCount == 2 && !midpointSelected && (hit >= 0 || hit <= AngularStart))
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
                    if (!CanAddGradientKey(g, time)) return;
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
                originalAngularAngle = layer.circularStartAngle;
                originalAngularRepetitions = layer.circularRepetitions;
                lastAngularAngle = AngularAngle(p); angularDelta = 0; angularPointerPosition = p;
                pointerStart = Document(p); handle = hit; pendingRemoval = false; angularGesture = true;
                pointer = e.pointerId; target.CapturePointer(pointer);
                owner.UpdateCanvasCursor(e.localPosition, e.altKey);
                owner.gradientCanvasOverlay.MarkDirtyRepaint(); e.StopImmediatePropagation();
            }

            private void AngularControlChanged(bool control)
            {
                Validate();
                if (pointer < 0 || !angularGesture || handle > AngularStart ||
                    AngularGradientGeometry.Span(originalAngularRepetitions) > 360.001f) return;
                BeginUndo();
                AngularMove(angularPointerPosition, control);
                owner.RequestTransformCanvas(); owner.gradientCanvasOverlay.MarkDirtyRepaint();
            }

            private void AngularMove(Vector2 p, bool control)
            {
                angularPointerPosition = p;
                if (handle == AngularCenter)
                {
                    layer.MoveEndpoint(owner.activeDocument, Size, Vector2.zero, Vector2.right, true,
                        Document(p) - pointerStart, originalTransform, originalFillStart, originalFillEnd);
                    return;
                }
                float angle = AngularAngle(p);
                if (!float.IsFinite(angle)) return;
                angularDelta += Mathf.DeltaAngle(lastAngularAngle, angle); lastAngularAngle = angle;
                // Rebuild fill direction from the gesture snapshot, avoiding cumulative radius drift.
                if (layer.IsFill) { layer.FillStart = originalFillStart; layer.FillEnd = originalFillEnd; }
                if (handle == AngularRotate || AngularGradientGeometry.Span(originalAngularRepetitions) > 360.001f)
                {
                    layer.circularStartAngle = Mathf.Repeat(originalAngularAngle + angularDelta, 360f);
                    return;
                }
                if (handle == AngularJoined)
                {
                    if (Mathf.Abs(angularDelta) < .05f) return;
                    handle = angularDelta > 0 ? AngularStart : AngularEnd;
                }
                float radius = Vector2.Distance(AngularPoint(0), AngularCenterPoint);
                float snap = Mathf.Clamp(6f / Mathf.Max(radius, 1f) * Mathf.Rad2Deg, .1f, 3f);
                AngularGradientGeometry.MoveBoundary(originalAngularAngle,
                    AngularGradientGeometry.Span(originalAngularRepetitions), angularDelta,
                    handle == AngularStart, snap, out float nextAngle, out float nextSpan, control);
                layer.circularStartAngle = nextAngle;
                // Store exact integer repetitions, not a round-trip division approximation.
                layer.circularRepetitions = control ? Mathf.Round(360f / nextSpan) : 360f / nextSpan;
            }

            private Color AngularKeyColor(float time)
            {
                var gradient = owner.gradientCanvasLayer.gradient;
                int index = time == 0 ? AngularEndpointKey(true) : time == 1 ? AngularEndpointKey(false) : -1;
                Color color = index >= 0 ? gradient.ColorKeys[index].color : gradient.Evaluate(time);
                if (gradient.ColorSpace == ColorSpace.Linear) color = HdrUtility.Encode(color);
                color /= Mathf.Max(1, Mathf.Max(color.r, Mathf.Max(color.g, color.b))); color.a = 1;
                return color;
            }

            private static void AngularDisc(Painter2D p, Vector2 point, Color color, bool highlighted)
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

            private void DrawAngular(Painter2D p)
            {
                owner.activeDocument.RefreshTransformHierarchy();
                Vector2 center = AngularCenterPoint, first = AngularPoint(0);
                if (!float.IsFinite(first.x) || !float.IsFinite(first.y)) return;
                float span = Mathf.Min(360, AngularSpan);
                int segments = Mathf.Max(8, Mathf.CeilToInt(span / 3f));
                if (!AngularClosed && span < 360f)
                {
                    int remainingSegments = Mathf.Max(1, Mathf.CeilToInt((360f - span) / 3f));
                    for (int pass = 0; pass < 2; pass++)
                    {
                        p.lineWidth = pass == 0 ? 3 : 1;
                        p.strokeColor = pass == 0 ? new Color(0, 0, 0, .12f) : new Color(.4f, .85f, 1, .3f);
                        p.BeginPath(); p.MoveTo(AngularPoint(1));
                        for (int i = 1; i <= remainingSegments; i++)
                            p.LineTo(i == remainingSegments ? first : AngularPointAtAngle(
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
                        p.LineTo(AngularPointAtAngle(owner.gradientCanvasLayer.circularStartAngle + span * i / segments));
                    if (!AngularClosed && AngularSpan <= 360) p.LineTo(center);
                    p.Stroke();
                }
                Square(p, center);
                var g = owner.gradientCanvasLayer.gradient;
                var keys = g.ColorKeys;
                if (g.Mode != WhimTexGradientMode.Fixed)
                    for (int i = 0; i < keys.Length - 1; i++)
                    {
                        float t = Mathf.Lerp(keys[i].time, keys[i + 1].time, g.GetMidpoint(false, i));
                        if (AngularVisible(t)) Diamond(p, AngularPoint(t), midpointSelected && selected == i && selectionLayer == owner.gradientCanvasLayer);
                    }
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].time <= .00001f || keys[i].time >= .99999f || !AngularVisible(keys[i].time) || pendingRemoval && selected == i) continue;
                    Color color = keys[i].color;
                    if (g.ColorSpace == ColorSpace.Linear) color = HdrUtility.Encode(color);
                    color /= Mathf.Max(1, Mathf.Max(color.r, Mathf.Max(color.g, color.b))); color.a = 1;
                    AngularDisc(p, AngularPoint(keys[i].time), color,
                        !midpointSelected && selected == i && selectionLayer == owner.gradientCanvasLayer);
                }
                bool firstSelected = !midpointSelected && selected >= 0 && selected == AngularEndpointKey(true) && selectionLayer == owner.gradientCanvasLayer;
                bool endSelected = !midpointSelected && selected >= 0 && selected == AngularEndpointKey(false) && selectionLayer == owner.gradientCanvasLayer;
                AngularDisc(p, first, AngularKeyColor(0), firstSelected || AngularClosed && endSelected);
                if (AngularClosed)
                {
                    // Each half faces its side of the seam, including mirrored/projective transforms.
                    Vector2 tangent = (AngularPointAtAngle(owner.gradientCanvasLayer.circularStartAngle + .1f) - first).normalized;
                    float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
                    p.fillColor = AngularKeyColor(1);
                    p.BeginPath(); p.MoveTo(first); p.Arc(first, 7.1f, angle + 90, angle + 270); p.ClosePath(); p.Fill();
                    Vector2 dividing = new Vector2(-tangent.y, tangent.x) * 7.1f;
                    p.strokeColor = new Color(.15f, .15f, .15f); p.lineWidth = 1;
                    p.BeginPath(); p.MoveTo(first - dividing); p.LineTo(first + dividing); p.Stroke();
                }
                else if (AngularSpan < 360) AngularDisc(p, AngularPoint(1), AngularKeyColor(1), endSelected);
            }
        }
    }

    internal static class AngularGradientGeometry
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
