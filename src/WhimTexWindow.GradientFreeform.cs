using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private static void DrawFreeformPoint(Painter2D painter, Vector2 position, Color color, bool selected = false)
        {
            void Circle(float radius, Color fill)
            {
                painter.fillColor = fill; painter.BeginPath(); painter.Arc(position, radius, 0, 360); painter.ClosePath(); painter.Fill();
            }
            Circle(selected ? 10.6f : 9.1f, Color.black);
            if (selected) Circle(9.6f, new Color(.4f, .85f, 1));
            Circle(8.1f, Color.white);
            color = WhimTexColorInputs.StandardColor(color); color.a = 1;
            Circle(7.1f, color);
        }

        private sealed partial class GradientCanvasManipulator
        {
            private const float FreeformPointHitRadius = 12f;
            private bool IsFreeform => owner.gradientCanvasLayer.gradientType == GradientLayerBehaviour.GradientType.Freeform;
            private bool freeformGesture, influenceGesture;
            private FreeformGradient originalFreeform;
            private int lastFreeformSelection = -1;
            private Vector2 freeformPointerStart;
            private Vector2 FreeformView(Vector2 uv) => View(GradientCanvasGeometry.Point(uv, owner.gradientCanvasLayer.CanvasTransform, Size));
            private Vector2 FreeformUv(Vector2 view)
            {
                Vector2 pixel = Document(view);
                return owner.gradientCanvasLayer.CanvasTransform.Unmap(new Vector2(pixel.x / Size.x, pixel.y / Size.y), Size);
            }
            private static bool ValidFreeformUv(Vector2 uv) => float.IsFinite(uv.x) && float.IsFinite(uv.y) &&
                Mathf.Abs(uv.x) <= 10000 && Mathf.Abs(uv.y) <= 10000;
            private void ValidateFreeformSelection()
            {
                if (!IsFreeform || owner.gradientCanvasLayer.freeform == null) return;
                var gradient = owner.gradientCanvasLayer;
                int index = Mathf.Clamp(gradient.selectedFreeformPoint, 0, gradient.freeform.points.Count - 1);
                if (index != lastFreeformSelection)
                {
                    lastFreeformSelection = index;
                    owner.gradientCanvasOverlay?.MarkDirtyRepaint();
                }
            }
            private int HitFreeform(Vector2 view, out bool influence)
            {
                var gradient = owner.gradientCanvasLayer;
                var data = gradient.freeform;
                int active = Mathf.Clamp(gradient.selectedFreeformPoint, 0, data.points.Count - 1);
                int hit = -1;
                float nearest = FreeformPointHitRadius * FreeformPointHitRadius;
                for (int i = 0; i < data.points.Count; i++)
                {
                    float distance = (view - FreeformView(data.points[i].position)).sqrMagnitude;
                    if (distance <= nearest) { nearest = distance; hit = i; }
                }
                if ((view - FreeformView(data.points[active].position)).sqrMagnitude <= FreeformPointHitRadius * FreeformPointHitRadius) hit = active;
                var chosen = data.points[active];
                influence = hit < 0 && (view - FreeformView(chosen.position + Vector2.right * chosen.influence)).sqrMagnitude <= 64;
                return influence ? active : hit;
            }
            internal MouseCursor GetFreeformCursor(Vector2 view)
            {
                if (!owner.IsGradientCanvasEnabled || !IsFreeform || owner.toolkitCanvas.ImageRect.width <= 0 ||
                    !target.contentRect.Contains(view)) return MouseCursor.Arrow;
                if (freeformGesture && pointer >= 0) return MouseCursor.Pan;
                var data = owner.gradientCanvasLayer.freeform;
                if (data == null || data.points.Count == 0) return MouseCursor.Arrow;
                owner.activeDocument.RefreshTransformHierarchy();
                if (!ValidFreeformUv(FreeformUv(view))) return MouseCursor.Arrow;
                if (HitFreeform(view, out _) >= 0) return MouseCursor.Pan;
                return data.points.Count < FreeformGradient.MaxPoints && owner.toolkitCanvas.ImageRect.Contains(owner.toolkitCanvas.ToCanvas(view))
                    ? MouseCursor.ArrowPlus : MouseCursor.Arrow;
            }
            private void FreeformDown(PointerDownEvent e)
            {
                if (!target.contentRect.Contains(e.localPosition)) return;
                var gradient = owner.gradientCanvasLayer;
                var data = gradient.freeform;
                if (data == null || data.points.Count == 0) return;
                owner.activeDocument.RefreshTransformHierarchy();
                Vector2 uv = FreeformUv(e.localPosition);
                if (!ValidFreeformUv(uv)) return;
                int hit = HitFreeform(e.localPosition, out influenceGesture);
                var chosen = data.points[Mathf.Clamp(gradient.selectedFreeformPoint, 0, data.points.Count - 1)];
                if (hit < 0 && !owner.toolkitCanvas.ImageRect.Contains(owner.toolkitCanvas.ToCanvas(e.localPosition))) return;
                target.Focus();
                layer = owner.gradientCanvasLayer; selectionLayer = layer;
                midpointSelected = false; pendingRemoval = false;
                if (hit >= 0 && !influenceGesture && e.clickCount == 2)
                {
                    gradient.selectedFreeformPoint = hit;
                    OpenFreeformColor(hit); owner.gradientCanvasOverlay.MarkDirtyRepaint();
                    e.StopImmediatePropagation(); return;
                }
                if (hit < 0 && data.points.Count >= FreeformGradient.MaxPoints) { e.StopImmediatePropagation(); return; }
                originalFreeform = data.Clone(); CaptureGeometry();
                freeformPointerStart = uv;
                if (hit < 0)
                {
                    BeginUndo();
                    gradient.freeform = data = data.Clone();
                    hit = data.points.Count;
                    data.points.Add(new FreeformGradientPoint(uv, HdrUtility.Encode(data.Evaluate(uv)), chosen.influence));
                    owner.RequestTransformCanvas();
                }
                selected = hit; gradient.selectedFreeformPoint = hit;
                freeformGesture = true; pointer = e.pointerId;
                target.CapturePointer(pointer);
                owner.UpdateCanvasCursor(e.localPosition, e.altKey);
                owner.toolkitRefreshRequested = true; owner.gradientCanvasOverlay.MarkDirtyRepaint();
                e.StopImmediatePropagation();
            }
            private void FreeformMove(PointerMoveEvent e)
            {
                owner.UpdateCanvasCursor(e.localPosition, e.altKey);
                Vector2 uv = FreeformUv(e.localPosition);
                if (!ValidFreeformUv(uv) || selected < 0 || selected >= layer.freeform.points.Count) return;
                if ((uv - freeformPointerStart).sqrMagnitude < 1e-12f && undo < 0) return;
                if (undo < 0)
                {
                    BeginUndo();
                    layer.freeform = layer.freeform.Clone();
                }
                var point = layer.freeform.points[selected];
                if (influenceGesture)
                    point.influence = Mathf.Clamp(Vector2.Distance(uv, point.position), FreeformGradient.MinInfluence, FreeformGradient.MaxInfluence);
                else
                {
                    Vector2 initial = selected < originalFreeform.points.Count ? originalFreeform.points[selected].position : freeformPointerStart;
                    point.position = initial + uv - freeformPointerStart;
                    if (!ValidFreeformUv(point.position)) return;
                }
                layer.freeform.points[selected] = point;
                owner.RequestTransformCanvas(); owner.toolkitRefreshRequested = true;
                owner.gradientCanvasOverlay.MarkDirtyRepaint(); e.StopImmediatePropagation();
            }
            private bool DeleteFreeform(KeyDownEvent e)
            {
                var element = e.target as VisualElement;
                if (e.keyCode != KeyCode.Delete || !owner.IsGradientCanvasEnabled || pointer >= 0 ||
                    (element != target && (element == null || !target.Contains(element)))) return false;
                var gradient = owner.gradientCanvasLayer;
                if (gradient.freeform == null) return false;
                int index = Mathf.Clamp(gradient.selectedFreeformPoint, 0, gradient.freeform.points.Count - 1);
                if (gradient.freeform.points.Count > 1)
                {
                    BeginUndo(); var next = gradient.freeform.Clone(); next.points.RemoveAt(index); gradient.freeform = next;
                    gradient.selectedFreeformPoint = Mathf.Min(index, gradient.freeform.points.Count - 1); Commit();
                }
                e.StopImmediatePropagation(); return true;
            }
            private void OpenFreeformColor(int index)
            {
                var edited = owner.gradientCanvasLayer;
                var document = owner.activeDocument;
                var expected = edited.freeform;
                Color color = expected.points[index].color;
                bool Valid() => owner != null && owner.activeDocument == document && owner.gradientCanvasLayer == edited &&
                    owner.IsGradientCanvasAvailable && edited.gradientType == GradientLayerBehaviour.GradientType.Freeform &&
                    edited.freeform == expected && index < expected.points.Count &&
                    edited.selectedFreeformPoint == index;
                var picker = WhimTexColorPicker.Open(color, WhimTexColorInputs.Hdr || color.maxColorComponent > 1 ||
                    color.r < 0 || color.g < 0 || color.b < 0, true, WhimTexColorRange.Switchable, document, value =>
                {
                    if (!Valid() || expected.points[index].color.Equals(value)) return;
                    Undo.RegisterCompleteObjectUndo(document, "Change Freeform Color");
                    var next = expected.Clone(); var point = next.points[index]; point.color = value; next.points[index] = point;
                    edited.freeform = expected = next;
                    owner.applyingToolkitChange = true;
                    try { owner.CommitModelChange(); } finally { owner.applyingToolkitChange = false; }
                    owner.RequestCanvasRender(true); owner.toolkitRefreshRequested = true;
                    owner.gradientCanvasOverlay?.MarkDirtyRepaint();
                }, valid: Valid);
                picker.SetChannelSource(() => owner != null && owner.activeDocument == document ? owner.canvasChannels : -1);
            }
            private void DrawFreeform(Painter2D painter)
            {
                var gradient = owner.gradientCanvasLayer;
                var data = gradient.freeform;
                if (data == null || data.points.Count == 0) return;
                owner.activeDocument.RefreshTransformHierarchy();
                int active = Mathf.Clamp(gradient.selectedFreeformPoint, 0, data.points.Count - 1);
                var point = data.points[active];
                painter.lineWidth = 1;
                painter.strokeColor = new Color(.4f, .85f, 1, .6f);
                painter.BeginPath();
                for (int i = 0; i <= 64; i++)
                {
                    float angle = i * (Mathf.PI * 2 / 64);
                    Vector2 p = FreeformView(point.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * point.influence);
                    if (i == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
                Square(painter, FreeformView(point.position + Vector2.right * point.influence));
                for (int draw = 0; draw < data.points.Count; draw++)
                {
                    int i = draw == data.points.Count - 1 ? active : draw >= active ? draw + 1 : draw;
                    DrawFreeformPoint(painter, FreeformView(data.points[i].position), data.points[i].color, i == active);
                }
            }
        }
    }
}
