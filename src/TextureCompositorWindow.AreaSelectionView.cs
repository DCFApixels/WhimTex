using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        private sealed class AreaSelectionManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            internal readonly List<Vector2> Vertices = new List<Vector2>();
            internal Vector2 Start, Current;
            private int pointer = -1;
            private SelectionCombine combine;
            private bool wrap;
            private bool shiftStartsCombine;
            private Vector2 pointerPosition;
            internal bool EllipseDragging { get; private set; }
            internal bool RectangleDragging => pointer >= 0;
            internal bool HasGesture => RectangleDragging || Vertices.Count > 0;
            internal AreaSelectionManipulator(TextureCompositorWindow owner) { this.owner = owner; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted);
                target.RegisterCallback<DetachFromPanelEvent>(Detached);
                target.RegisterCallback<KeyDownEvent>(KeyDown);
                target.RegisterCallback<KeyUpEvent>(KeyUp);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<KeyDownEvent>(KeyDown);
                target.UnregisterCallback<KeyUpEvent>(KeyUp);
            }
            private Vector2 CanvasPoint(Vector2 point, bool disableSnap)
            {
                Rect image = owner.toolkitPreviewCanvas.ImageRect;
                point = owner.toolkitPreviewCanvas.ToCanvas(point);
                Vector2 documentPoint = new Vector2((point.x - image.x) / Mathf.Max(.0001f, image.width) * owner.compositor.width,
                    (1f - (point.y - image.y) / Mathf.Max(.0001f, image.height)) * owner.compositor.height);
                return disableSnap ? documentPoint : owner.SnapPreviewGuidePoint(documentPoint, owner.previewTool == PreviewTool.RectangleSelect);
            }
            private static Vector2 ConstrainMarquee(Vector2 start, Vector2 end)
            {
                Vector2 delta = end - start;
                float size = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                return start + new Vector2(delta.x < 0f ? -size : size, delta.y < 0f ? -size : size);
            }
            private void UpdateCurrent(Vector2 position, bool shift, bool control)
            {
                pointerPosition = position;
                if (!shift) shiftStartsCombine = false;
                Current = CanvasPoint(position, control);
                if (RectangleDragging && shift && !shiftStartsCombine)
                    Current = ConstrainMarquee(Start, Current);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private void KeyDown(KeyDownEvent evt)
            {
                if (!RectangleDragging || (evt.keyCode != KeyCode.LeftShift && evt.keyCode != KeyCode.RightShift)) return;
                UpdateCurrent(pointerPosition, true, evt.ctrlKey);
                SpriteEditorUI.ConsumeEvent(evt);
            }
            private void KeyUp(KeyUpEvent evt)
            {
                if (!RectangleDragging || (evt.keyCode != KeyCode.LeftShift && evt.keyCode != KeyCode.RightShift)) return;
                UpdateCurrent(pointerPosition, evt.shiftKey, evt.ctrlKey);
                SpriteEditorUI.ConsumeEvent(evt);
            }
            private void Down(PointerDownEvent evt)
            {
                if (!owner.IsAreaSelectionTool || !owner.HasPreviewLayers ||
                    !target.contentRect.Contains(evt.localPosition) || (evt.button != 0 && evt.button != 1)) return;
                SpriteEditorUI.ConsumeEvent(evt);
                owner.Focus(); target.Focus();
                if (evt.button == 1) { RemoveVertex(); return; }
                if (!HasGesture)
                {
                    combine = evt.shiftKey && evt.altKey ? SelectionCombine.Intersect : evt.shiftKey ? SelectionCombine.Add :
                        evt.altKey ? SelectionCombine.Subtract : owner.areaSelectionMode;
                    wrap = owner.tiledPreview;
                    owner.FinishPaintingStroke(); owner.FinishPreviewTransform();
                    owner.GetAreaSelection();
                }
                Current = CanvasPoint(evt.localPosition, evt.ctrlKey);
                if (owner.previewTool == PreviewTool.RectangleSelect)
                {
                    shiftStartsCombine = evt.shiftKey;
                    pointerPosition = evt.localPosition;
                    Start = Current; pointer = evt.pointerId;
                    EllipseDragging = owner.marqueeShape == MarqueeShape.Ellipse;
                    target.CapturePointer(pointer);
                }
                else if (Vertices.Count >= 3 && (evt.clickCount > 1 ||
                    (Current - Vertices[0]).magnitude * owner.toolkitPreviewCanvas.PixelScale <= 6f)) CompletePolygon();
                else if (Vertices.Count < 256 && (Vertices.Count == 0 || (Current - Vertices[Vertices.Count - 1]).sqrMagnitude > .0001f))
                    Vertices.Add(Current);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private void Move(PointerMoveEvent evt)
            {
                if (!owner.IsAreaSelectionTool || !HasGesture || owner.compositor == null ||
                    (owner.previewZoomManipulator?.IsNavigating ?? false)) return;
                if (RectangleDragging && (evt.pointerId != pointer || (evt.pressedButtons & 1) == 0)) { Cancel(); return; }
                UpdateCurrent(evt.localPosition, evt.shiftKey, evt.ctrlKey);
                evt.StopImmediatePropagation();
            }
            private void Up(PointerUpEvent evt)
            {
                if (evt.button != 0 || pointer != evt.pointerId) return;
                UpdateCurrent(evt.localPosition, evt.shiftKey, evt.ctrlKey);
                Vector2 a = Start, b = Current;
                var operation = combine; bool tiled = wrap, ellipse = EllipseDragging;
                bool click = (a - b).magnitude * owner.toolkitPreviewCanvas.PixelScale < 3f;
                Cancel();
                if (!click) owner.ChangeAreaSelection(s =>
                {
                    if (ellipse) s.Ellipse(a, b, operation, tiled);
                    else s.Rectangle(a, b, operation, tiled);
                });
                else if (operation == SelectionCombine.Replace) owner.ChangeAreaSelection(s => s.Clear());
                SpriteEditorUI.ConsumeEvent(evt);
            }
            internal void CompletePolygon()
            {
                if (Vertices.Count < 3) return;
                Vector2[] points = Vertices.ToArray();
                var operation = combine; bool tiled = wrap;
                Cancel();
                owner.ChangeAreaSelection(s => s.Polygon(points, operation, tiled));
            }
            internal void RemoveVertex()
            {
                if (Vertices.Count > 0) Vertices.RemoveAt(Vertices.Count - 1);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            internal void Cancel()
            {
                int captured = pointer; pointer = -1;
                Vertices.Clear();
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent evt) => Cancel();
            private void Detached(DetachFromPanelEvent evt) => Cancel();
        }

        private sealed class AreaSelectionOverlay : VisualElement
        {
            private readonly TextureCompositorWindow owner;
            private readonly List<Vector4> edges = new List<Vector4>();
            private readonly List<Vector4> visibleEdges = new List<Vector4>();
            private CanvasSelection cached;
            private int revision = -1;
            internal AreaSelectionOverlay(TextureCompositorWindow owner)
            {
                this.owner = owner;
                pickingMode = PickingMode.Ignore;
                AddToClassList("sprite-editor-area-overlay");
                generateVisualContent += Draw;
            }
            internal void Invalidate()
            {
                CanvasSelection selection = owner.areaSelection;
                if (selection == cached && (selection == null || selection.Revision == revision)) return;
                if (selection != cached || (selection != null && selection.Revision != revision))
                {
                    cached = selection; revision = selection?.Revision ?? -1;
                    edges.Clear();
                    if (selection != null && selection.Active && selection.Bounds.width > 0)
                        for (int step = 1; step <= Mathf.Max(selection.Width, selection.Height); step *= 2)
                        {
                            edges.Clear();
                            if (BuildEdges(selection, step)) break;
                        }
                }
                MarkDirtyRepaint();
            }
            private bool BuildEdges(CanvasSelection s, int step)
            {
                int width = (s.Width + step - 1) / step, height = (s.Height + step - 1) / step;
                byte threshold = 1;
                for (int i = 0; i < s.Coverage.Length; i++) if (s.Coverage[i] >= 128) { threshold = 128; break; }
                bool Filled(int x, int y) => x >= 0 && y >= 0 && x < width && y < height &&
                    s.Coverage[Mathf.Min(s.Height - 1, y * step + step / 2) * s.Width + Mathf.Min(s.Width - 1, x * step + step / 2)] >= threshold;
                // Coalesce collinear pixel edges. Simple shapes stay exact even at high resolution;
                // noisy masks use a bounded contour LOD without changing their painting coverage.
                for (int y = 0; y <= height; y++)
                {
                    int start = -1;
                    for (int x = 0; x <= width; x++)
                    {
                        bool edge = x < width && Filled(x, y - 1) != Filled(x, y);
                        if (edge && start < 0) start = x;
                        if (!edge && start >= 0)
                        {
                            edges.Add(new Vector4(start * step, Mathf.Min(y * step, s.Height), Mathf.Min(x * step, s.Width), Mathf.Min(y * step, s.Height)));
                            start = -1;
                            if (edges.Count > 4096) return false;
                        }
                    }
                }
                for (int x = 0; x <= width; x++)
                {
                    int start = -1;
                    for (int y = 0; y <= height; y++)
                    {
                        bool edge = y < height && Filled(x - 1, y) != Filled(x, y);
                        if (edge && start < 0) start = y;
                        if (!edge && start >= 0)
                        {
                            edges.Add(new Vector4(Mathf.Min(x * step, s.Width), start * step, Mathf.Min(x * step, s.Width), Mathf.Min(y * step, s.Height)));
                            start = -1;
                            if (edges.Count > 4096) return false;
                        }
                    }
                }
                return true;
            }
            private Vector2 PreviewPoint(Vector2 canvas, Rect image) => owner.toolkitPreviewCanvas.ToView(new Vector2(
                image.x + canvas.x * image.width / owner.compositor.width,
                image.yMax - canvas.y * image.height / owner.compositor.height));
            private void AddVisible(Vector2 a, Vector2 b)
            {
                Rect bounds = contentRect;
                float first = 0f, last = 1f;
                Vector2 d = b - a;
                bool Clip(float p, float q)
                {
                    if (Mathf.Abs(p) < .000001f) return q >= 0f;
                    float t = q / p;
                    if (p < 0f) first = Mathf.Max(first, t); else last = Mathf.Min(last, t);
                    return first <= last;
                }
                if (!Clip(-d.x, a.x - bounds.xMin) || !Clip(d.x, bounds.xMax - a.x) ||
                    !Clip(-d.y, a.y - bounds.yMin) || !Clip(d.y, bounds.yMax - a.y)) return;
                Vector2 from = a + d * first, to = a + d * last;
                if (visibleEdges.Count < 16384) visibleEdges.Add(new Vector4(from.x, from.y, to.x, to.y));
            }
            private void Draw(MeshGenerationContext context)
            {
                if (owner.compositor == null || contentRect.width < 1f || contentRect.height < 1f) return;
                Rect image = owner.toolkitPreviewCanvas.ImageRect;
                if (image.width <= .0001f || image.height <= .0001f) return;
                visibleEdges.Clear();
                int left = 0, right = 0, top = 0, bottom = 0;
                if (owner.tiledPreview)
                {
                    Rect bounds = owner.toolkitPreviewCanvas.VisibleCanvasBounds;
                    left = Mathf.FloorToInt((bounds.xMin - image.xMax) / image.width) + 1;
                    right = Mathf.CeilToInt((bounds.xMax - image.xMin) / image.width) - 1;
                    top = Mathf.FloorToInt((bounds.yMin - image.yMax) / image.height) + 1;
                    bottom = Mathf.CeilToInt((bounds.yMax - image.yMin) / image.height) - 1;
                }
                int skip = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt((right - left + 1f) * (bottom - top + 1f) / 256f)));
                for (int y = top; y <= bottom && visibleEdges.Count < 16384; y += skip)
                for (int x = left; x <= right && visibleEdges.Count < 16384; x += skip)
                {
                    Rect tile = new Rect(image.position + new Vector2(x * image.width, y * image.height), image.size);
                    foreach (Vector4 e in edges)
                    {
                        if (visibleEdges.Count >= 16384) break;
                        AddVisible(PreviewPoint(new Vector2(e.x, e.y), tile), PreviewPoint(new Vector2(e.z, e.w), tile));
                    }
                }
                var gesture = owner.areaSelectionManipulator;
                if (gesture != null && gesture.RectangleDragging && gesture.EllipseDragging)
                {
                    Vector2 center = (gesture.Start + gesture.Current) * .5f;
                    Vector2 radius = (gesture.Current - gesture.Start) * .5f;
                    float viewRadius = Mathf.Max(Mathf.Abs(radius.x), Mathf.Abs(radius.y)) * owner.toolkitPreviewCanvas.PixelScale;
                    int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.PI * Mathf.Sqrt(viewRadius)), 24, 512);
                    Vector2 previous = PreviewPoint(center + new Vector2(radius.x, 0f), image);
                    for (int i = 1; i <= segments; i++)
                    {
                        float angle = i * Mathf.PI * 2f / segments;
                        Vector2 next = PreviewPoint(center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y), image);
                        AddVisible(previous, next);
                        previous = next;
                    }
                }
                else if (gesture != null && gesture.RectangleDragging)
                {
                    Vector2 a = PreviewPoint(gesture.Start, image), b = PreviewPoint(gesture.Current, image);
                    Vector2 c = PreviewPoint(new Vector2(gesture.Current.x, gesture.Start.y), image);
                    Vector2 d = PreviewPoint(new Vector2(gesture.Start.x, gesture.Current.y), image);
                    AddVisible(a, c); AddVisible(c, b);
                    AddVisible(b, d); AddVisible(d, a);
                }
                else if (gesture != null && gesture.Vertices.Count > 0)
                {
                    for (int i = 1; i < gesture.Vertices.Count; i++)
                        AddVisible(PreviewPoint(gesture.Vertices[i - 1], image), PreviewPoint(gesture.Vertices[i], image));
                    AddVisible(PreviewPoint(gesture.Vertices[gesture.Vertices.Count - 1], image), PreviewPoint(gesture.Current, image));
                    AddVisible(PreviewPoint(gesture.Current, image), PreviewPoint(gesture.Vertices[0], image));
                }
                Painter2D painter = context.painter2D;
                painter.lineWidth = 1.5f; painter.lineCap = LineCap.Butt; painter.strokeColor = Color.black;
                painter.BeginPath();
                foreach (Vector4 e in visibleEdges) { painter.MoveTo(new Vector2(e.x, e.y)); painter.LineTo(new Vector2(e.z, e.w)); }
                painter.Stroke();
                painter.lineWidth = 1f; painter.strokeColor = Color.white;
                painter.BeginPath();
                int budget = 16384;
                float dashPhase = 0f;
                foreach (Vector4 e in visibleEdges)
                {
                    Vector2 a = new Vector2(e.x, e.y), d = new Vector2(e.z - e.x, e.w - e.y);
                    float length = d.magnitude;
                    if (length < .0001f) continue;
                    for (float t = -dashPhase; t < length && budget > 0; t += 8f, budget--)
                    {
                        if (t + 4f <= 0f) continue;
                        painter.MoveTo(a + d * (Mathf.Max(0f, t) / length));
                        painter.LineTo(a + d * (Mathf.Min(t + 4f, length) / length));
                    }
                    dashPhase = (dashPhase + length) % 8f;
                }
                painter.Stroke();
            }
        }
    }
}
