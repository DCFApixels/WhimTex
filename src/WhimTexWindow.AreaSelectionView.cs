using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed class AreaSelectionManipulator : PointerManipulator
        {
            private const float VertexHitRadius = 6f;
            private const float SegmentHitRadius = 3f;
            private const float VertexDragThreshold = 4f;
            private const int MaximumFreehandPoints = 4096;
            private enum PolygonPress { Append, Vertex, Insert, Delete, Close, Complete }
            private readonly WhimTexWindow owner;
            internal readonly List<Vector2> Vertices = new List<Vector2>();
            internal readonly List<Vector2> FreehandPoints = new List<Vector2>();
            internal Vector2 FreehandCurrent;
            private int freehandPointer = -1;
            private float freehandScale;
            private bool[] freehandKeep;
            private readonly List<int> freehandRanges = new List<int>();
            internal Vector2 Start, Current;
            private int pointer = -1;
            private SelectionCombine combine;
            private bool wrap;
            private bool antialias;
            private bool shiftStartsCombine;
            private Vector2 pointerPosition;
            private bool pointerControl;
            private bool pointerAlt;
            private bool pointerShift;
            private int polygonPointer = -1;
            private int pressedVertex = -1, pressedSegment = -1;
            private int createdVertex = -1;
            private PolygonPress polygonPress;
            private Vector2 pressPosition, pressPoint, vertexOriginal, vertexGrabOffset;
            private bool vertexDragging, createdPointMoved, closeArmed, protectingTail;
            private Vector2 lastAppendPosition;
            private bool lastAppendCanComplete, bypassPolygonPress;
            internal bool ClosingPolygon { get; private set; }
            internal int HoverVertex { get; private set; } = -1;
            internal int InsertSegment { get; private set; } = -1;
            internal Vector2 InsertPoint { get; private set; }
            internal bool DeletingVertex { get; private set; }
            internal bool ShowPolygonTail { get; private set; } = true;
            internal bool WantsMoveCursor => HoverVertex >= 0 && !DeletingVertex && !ClosingPolygon;
            internal bool EllipseDragging { get; private set; }
            internal bool RectangleDragging => pointer >= 0;
            internal bool IsFreehandDrawing => freehandPointer >= 0;
            internal bool HasGesture => RectangleDragging || IsFreehandDrawing || polygonPointer >= 0 || Vertices.Count > 0;
            internal AreaSelectionManipulator(WhimTexWindow owner) { this.owner = owner; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted);
                target.RegisterCallback<PointerLeaveEvent>(Leave);
                target.RegisterCallback<BlurEvent>(Blurred);
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
                target.UnregisterCallback<PointerLeaveEvent>(Leave);
                target.UnregisterCallback<BlurEvent>(Blurred);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<KeyDownEvent>(KeyDown);
                target.UnregisterCallback<KeyUpEvent>(KeyUp);
            }
            private Vector2 CanvasPoint(Vector2 point, bool disableSnap)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                point = owner.toolkitCanvas.ToCanvas(point);
                Vector2 documentPoint = new Vector2((point.x - image.x) / Mathf.Max(.0001f, image.width) * owner.activeDocument.width,
                    (1f - (point.y - image.y) / Mathf.Max(.0001f, image.height)) * owner.activeDocument.height);
                return disableSnap ? documentPoint : owner.SnapCanvasGuidePoint(documentPoint, owner.canvasTool == CanvasTool.RectangleSelect);
            }
            private static Vector2 ConstrainMarquee(Vector2 start, Vector2 end)
            {
                Vector2 delta = end - start;
                float size = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                return start + new Vector2(delta.x < 0f ? -size : size, delta.y < 0f ? -size : size);
            }
            private void UpdateCurrent(Vector2 position, bool shift, bool control)
            {
                SetCurrent(position, control);
                if (!shift) shiftStartsCombine = false;
                if (RectangleDragging && shift && !shiftStartsCombine)
                    Current = ConstrainMarquee(Start, Current);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private void SetCurrent(Vector2 position, bool control)
            {
                pointerPosition = position;
                pointerControl = control;
                ClearPolygonHover();
                if (owner.IsFreehandLasso) return;
                Current = CanvasPoint(position, control);
                if (owner.canvasTool != CanvasTool.PolygonSelect) return;
                if (pointerShift && Vertices.Count > 0)
                    Current = ConstrainPolygonLine(Vertices[Vertices.Count - 1], Current);
                if (polygonPointer >= 0)
                {
                    if (vertexDragging)
                    {
                        Vector2 point;
                        if (createdVertex >= 0 && polygonPress == PolygonPress.Insert && !createdPointMoved)
                            point = pressPoint;
                        else
                        {
                            point = CanvasPoint(position, true) + vertexGrabOffset;
                            if (!control) point = owner.SnapCanvasGuidePoint(point, false);
                        }
                        if (pointerShift && createdVertex > 0 && polygonPress == PolygonPress.Append)
                            point = ConstrainPolygonLine(Vertices[createdVertex - 1], point);
                        else if (pointerShift && createdVertex < 0)
                            point = ConstrainPolygonLine(vertexOriginal, point);
                        Vertices[pressedVertex] = point;
                        HoverVertex = pressedVertex;
                        ShowPolygonTail = false;
                    }
                    else
                    {
                        Current = pressPoint;
                        HoverVertex = pressedVertex;
                        DeletingVertex = polygonPress == PolygonPress.Delete;
                        ClosingPolygon = polygonPress == PolygonPress.Close;
                        InsertSegment = polygonPress == PolygonPress.Insert ? pressedSegment : -1;
                        InsertPoint = pressPoint;
                        ShowPolygonTail = polygonPress == PolygonPress.Append || ClosingPolygon || polygonPress == PolygonPress.Complete;
                    }
                    return;
                }
                if (control || Vertices.Count == 0 || !target.contentRect.Contains(position)) return;
                Vector2 raw = CanvasPoint(position, true);
                float scale = owner.toolkitCanvas.PixelScale;
                protectingTail = (raw - Vertices[Vertices.Count - 1]).sqrMagnitude * scale * scale <= VertexHitRadius * VertexHitRadius;
                HoverVertex = HitVertex(raw, scale);
                if (HoverVertex >= 0)
                {
                    DeletingVertex = pointerAlt;
                    ClosingPolygon = !pointerAlt && !protectingTail && closeArmed && Vertices.Count >= 3 && HoverVertex == 0;
                    if (ClosingPolygon) Current = Vertices[0];
                    ShowPolygonTail = !DeletingVertex && (protectingTail || ClosingPolygon);
                }
                else if (!protectingTail && Vertices.Count < 256)
                {
                    InsertSegment = HitSegment(raw, scale, out Vector2 point);
                    InsertPoint = point;
                    if (InsertSegment >= 0) ShowPolygonTail = false;
                }
            }
            private static Vector2 ConstrainPolygonLine(Vector2 start, Vector2 end)
            {
                Vector2 delta = end - start;
                float x = Mathf.Abs(delta.x), y = Mathf.Abs(delta.y);
                const float tangent = .41421356f;
                if (y <= x * tangent) return new Vector2(end.x, start.y);
                if (x <= y * tangent) return new Vector2(start.x, end.y);
                float diagonal = (x + y) * .5f;
                return start + new Vector2(Mathf.Sign(delta.x) * diagonal, Mathf.Sign(delta.y) * diagonal);
            }
            private void ClearPolygonHover()
            {
                HoverVertex = InsertSegment = -1;
                ClosingPolygon = DeletingVertex = protectingTail = false;
                ShowPolygonTail = true;
            }
            private int HitVertex(Vector2 point, float scale)
            {
                int index = -1;
                float nearest = VertexHitRadius * VertexHitRadius;
                for (int i = 0; i < Vertices.Count; i++)
                {
                    float distance = (point - Vertices[i]).sqrMagnitude * scale * scale;
                    if (distance > nearest) continue;
                    nearest = distance; index = i;
                }
                return index;
            }
            private int HitSegment(Vector2 point, float scale, out Vector2 projected)
            {
                int index = -1;
                projected = point;
                float nearest = SegmentHitRadius * SegmentHitRadius;
                // Only committed segments participate; the rubber-band endpoint is not an insertion target.
                for (int i = 0; i + 1 < Vertices.Count; i++)
                {
                    Vector2 a = Vertices[i], delta = Vertices[i + 1] - a;
                    float length = delta.sqrMagnitude;
                    if (length < .0001f) continue;
                    Vector2 p = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / length);
                    float distance = (point - p).sqrMagnitude * scale * scale;
                    if (distance > nearest) continue;
                    nearest = distance; projected = p; index = i;
                }
                return index;
            }
            private void UpdateCloseArming(bool reset = false)
            {
                if (reset || Vertices.Count < 3) closeArmed = false;
                if (Vertices.Count < 3) return;
                float scale = owner.toolkitCanvas.PixelScale;
                for (int i = 1; i < Vertices.Count && !closeArmed; i++)
                    closeArmed = (Vertices[i] - Vertices[0]).sqrMagnitude * scale * scale > VertexHitRadius * VertexHitRadius;
            }
            private void UpdatePolygonDrag(Vector2 position)
            {
                if (polygonPointer >= 0 && createdVertex >= 0 && (position - pressPosition).sqrMagnitude > .0001f)
                    createdPointMoved = true;
                if (polygonPointer < 0 || pressedVertex < 0 || vertexDragging ||
                    polygonPress == PolygonPress.Delete || polygonPress == PolygonPress.Insert) return;
                vertexDragging = (position - pressPosition).sqrMagnitude >= VertexDragThreshold * VertexDragThreshold;
            }
            private void EndPolygonPress(bool restore)
            {
                int captured = polygonPointer;
                polygonPointer = -1;
                if (restore && createdVertex >= 0 && createdVertex < Vertices.Count)
                    Vertices.RemoveAt(createdVertex);
                else if (restore && vertexDragging && pressedVertex >= 0 && pressedVertex < Vertices.Count)
                    Vertices[pressedVertex] = vertexOriginal;
                vertexDragging = false;
                createdVertex = -1;
                pressedVertex = pressedSegment = -1;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
            }
            private void AbortPolygonPress()
            {
                EndPolygonPress(true);
                lastAppendCanComplete = false;
                UpdateCloseArming(true);
                if (owner.activeDocument != null) SetCurrent(pointerPosition, pointerControl);
                else ClearPolygonHover();
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
            {
                Vector2 delta = b - a;
                float length = delta.sqrMagnitude;
                Vector2 projected = length > 1e-12f ? a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / length) : a;
                return (point - projected).sqrMagnitude;
            }
            private bool CompactFreehand()
            {
                int count = FreehandPoints.Count;
                if (freehandKeep == null) freehandKeep = new bool[MaximumFreehandPoints];
                System.Array.Clear(freehandKeep, 0, count);
                freehandKeep[0] = freehandKeep[count - 1] = true;
                freehandRanges.Clear();
                freehandRanges.Add(0); freehandRanges.Add(count - 1);
                float tolerance = .5f / Mathf.Max(.0001f, freehandScale);
                while (freehandRanges.Count > 0)
                {
                    int end = freehandRanges[freehandRanges.Count - 1]; freehandRanges.RemoveAt(freehandRanges.Count - 1);
                    int start = freehandRanges[freehandRanges.Count - 1]; freehandRanges.RemoveAt(freehandRanges.Count - 1);
                    int farthest = -1;
                    float distance = tolerance * tolerance;
                    for (int i = start + 1; i < end; i++)
                    {
                        float d = DistanceToSegmentSquared(FreehandPoints[i], FreehandPoints[start], FreehandPoints[end]);
                        if (d <= distance) continue;
                        distance = d; farthest = i;
                    }
                    if (farthest < 0) continue;
                    freehandKeep[farthest] = true;
                    freehandRanges.Add(start); freehandRanges.Add(farthest);
                    freehandRanges.Add(farthest); freehandRanges.Add(end);
                }
                int retained = 0;
                for (int i = 0; i < count; i++)
                    if (freehandKeep[i]) FreehandPoints[retained++] = FreehandPoints[i];
                FreehandPoints.RemoveRange(retained, count - retained);
                return retained < MaximumFreehandPoints * 3 / 4;
            }
            private bool TraceFreehand(Vector2 position, bool final)
            {
                Vector2 point = CanvasPoint(position, true);
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y))
                { Cancel(); return false; }
                pointerPosition = position;
                FreehandCurrent = point;
                int count = FreehandPoints.Count;
                if (count > 0)
                {
                    float distance = (point - FreehandPoints[count - 1]).sqrMagnitude;
                    float viewDistance = distance * freehandScale * freehandScale;
                    if (viewDistance < .0001f || (!final && viewDistance < .75f * .75f)) return true;
                }
                if (count >= MaximumFreehandPoints && !CompactFreehand())
                {
                    Cancel();
                    owner.ShowNotification(new GUIContent("Lasso outline is too detailed. Draw a shorter outline or zoom in."));
                    return false;
                }
                FreehandPoints.Add(point);
                return true;
            }
            private bool HasFreehandArea()
            {
                if (FreehandPoints.Count < 3) return false;
                Vector2 a = FreehandPoints[0], b = a;
                float farthest = 0f;
                for (int i = 1; i < FreehandPoints.Count; i++)
                {
                    float distance = (FreehandPoints[i] - a).sqrMagnitude;
                    if (distance <= farthest) continue;
                    farthest = distance; b = FreehandPoints[i];
                }
                if (farthest * freehandScale * freehandScale < 9f) return false;
                Vector2 delta = b - a;
                for (int i = 1; i < FreehandPoints.Count; i++)
                {
                    Vector2 d = FreehandPoints[i] - a;
                    float cross = delta.x * d.y - delta.y * d.x;
                    if (cross * cross / farthest * freehandScale * freehandScale >= .25f) return true;
                }
                return false;
            }
            private void CompleteFreehand()
            {
                if (!IsFreehandDrawing || !target.HasPointerCapture(freehandPointer)) { Cancel(); return; }
                bool valid = HasFreehandArea();
                Vector2[] points = valid ? FreehandPoints.ToArray() : null;
                SelectionCombine operation = combine; bool tiled = wrap, smooth = antialias;
                Cancel();
                if (valid) owner.ChangeAreaSelection(s => s.Freehand(points, operation, tiled, smooth));
            }
            private void KeyDown(KeyDownEvent evt)
            {
                if (HasGesture && owner.canvasTool == CanvasTool.PolygonSelect &&
                    (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift))
                {
                    pointerShift = true;
                    UpdateCurrent(pointerPosition, true, evt.ctrlKey);
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (HasGesture && (evt.keyCode == KeyCode.LeftControl || evt.keyCode == KeyCode.RightControl ||
                    evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt))
                {
                    pointerAlt = evt.altKey;
                    UpdateCurrent(pointerPosition, evt.shiftKey,
                        evt.keyCode == KeyCode.LeftControl || evt.keyCode == KeyCode.RightControl || evt.ctrlKey);
                    owner.RefreshCanvasPointerCursor();
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (!RectangleDragging || (evt.keyCode != KeyCode.LeftShift && evt.keyCode != KeyCode.RightShift)) return;
                UpdateCurrent(pointerPosition, true, evt.ctrlKey);
                WhimTexUI.ConsumeEvent(evt);
            }
            private void KeyUp(KeyUpEvent evt)
            {
                if (HasGesture && owner.canvasTool == CanvasTool.PolygonSelect &&
                    (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift))
                {
                    pointerShift = evt.shiftKey;
                    UpdateCurrent(pointerPosition, evt.shiftKey, evt.ctrlKey);
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (HasGesture && (evt.keyCode == KeyCode.LeftControl || evt.keyCode == KeyCode.RightControl ||
                    evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt))
                {
                    pointerAlt = evt.altKey;
                    UpdateCurrent(pointerPosition, evt.shiftKey, evt.ctrlKey);
                    owner.RefreshCanvasPointerCursor();
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (!RectangleDragging || (evt.keyCode != KeyCode.LeftShift && evt.keyCode != KeyCode.RightShift)) return;
                UpdateCurrent(pointerPosition, evt.shiftKey, evt.ctrlKey);
                WhimTexUI.ConsumeEvent(evt);
            }
            private void Down(PointerDownEvent evt)
            {
                if (!owner.IsAreaSelectionTool || !owner.HasCanvasLayers ||
                    !target.contentRect.Contains(evt.localPosition) || (evt.button != 0 && evt.button != 1)) return;
                WhimTexUI.ConsumeEvent(evt);
                owner.Focus(); target.Focus();
                if (evt.button == 1) { if (owner.IsFreehandLasso) Cancel(); else RemoveVertex(); return; }
                if (RectangleDragging || polygonPointer >= 0 || IsFreehandDrawing) return;
                if (owner.IsUvSelectionTool)
                {
                    owner.SelectUvIsland(evt.localPosition, evt.shiftKey && evt.altKey ? SelectionCombine.Intersect :
                        evt.shiftKey ? SelectionCombine.Add : evt.altKey ? SelectionCombine.Subtract : owner.areaSelectionMode);
                    return;
                }
                if (!HasGesture)
                {
                    combine = evt.shiftKey && evt.altKey ? SelectionCombine.Intersect : evt.shiftKey ? SelectionCombine.Add :
                        evt.altKey ? SelectionCombine.Subtract : owner.areaSelectionMode;
                    wrap = owner.tiledCanvas;
                    antialias = owner.selectionEdgeMode == SelectionEdgeMode.Antialiased;
                    owner.FinishPaintingStroke(); owner.FinishCanvasTransform();
                    owner.GetAreaSelection();
                }
                pointerAlt = evt.altKey;
                pointerShift = evt.shiftKey;
                if (owner.IsFreehandLasso)
                {
                    ClearPolygonHover();
                    FreehandPoints.Clear();
                    freehandScale = owner.toolkitCanvas.PixelScale;
                    freehandPointer = evt.pointerId;
                    target.CapturePointer(freehandPointer);
                    TraceFreehand(evt.localPosition, true);
                    owner.areaSelectionOverlay?.MarkDirtyRepaint();
                    return;
                }
                SetCurrent(evt.localPosition, evt.ctrlKey);
                if (owner.canvasTool == CanvasTool.RectangleSelect)
                {
                    shiftStartsCombine = evt.shiftKey;
                    pointerPosition = evt.localPosition;
                    Start = Current; pointer = evt.pointerId;
                    EllipseDragging = owner.marqueeShape == MarqueeShape.Ellipse;
                    target.CapturePointer(pointer);
                }
                else
                {
                    pressPosition = evt.localPosition;
                    pressPoint = InsertSegment >= 0 ? InsertPoint : Current;
                    pressedVertex = HoverVertex;
                    pressedSegment = InsertSegment;
                    bypassPolygonPress = evt.ctrlKey;
                    bool repeatAppend = !evt.ctrlKey && evt.clickCount > 1 && Vertices.Count >= 3 &&
                        lastAppendCanComplete && HoverVertex == Vertices.Count - 1 &&
                        (pressPosition - lastAppendPosition).sqrMagnitude <= .25f * .25f;
                    polygonPress = DeletingVertex ? PolygonPress.Delete : ClosingPolygon ? PolygonPress.Close :
                        InsertSegment >= 0 ? PolygonPress.Insert : repeatAppend ? PolygonPress.Complete :
                        HoverVertex >= 0 && !protectingTail ? PolygonPress.Vertex : PolygonPress.Append;
                    lastAppendCanComplete = false;
                    createdVertex = -1;
                    createdPointMoved = false;
                    if (polygonPress == PolygonPress.Insert && Vertices.Count < 256)
                    {
                        createdVertex = pressedSegment + 1;
                        Vertices.Insert(createdVertex, pressPoint);
                    }
                    else if (polygonPress == PolygonPress.Append)
                    {
                        pressedVertex = -1;
                        if (Vertices.Count < 256 && (Vertices.Count == 0 ||
                            (pressPoint - Vertices[Vertices.Count - 1]).sqrMagnitude > .0001f))
                        {
                            createdVertex = Vertices.Count;
                            Vertices.Add(pressPoint);
                        }
                        else if (Vertices.Count > 0 && HoverVertex == Vertices.Count - 1 &&
                            (pressPoint - Vertices[Vertices.Count - 1]).sqrMagnitude <= .0001f && !evt.ctrlKey)
                            pressedVertex = HoverVertex;
                    }
                    if (createdVertex >= 0)
                    {
                        pressedVertex = createdVertex;
                        vertexDragging = true;
                        UpdateCloseArming();
                    }
                    if (pressedVertex >= 0)
                    {
                        vertexOriginal = Vertices[pressedVertex];
                        vertexGrabOffset = createdVertex >= 0 && polygonPress == PolygonPress.Append
                            ? Vector2.zero : vertexOriginal - CanvasPoint(evt.localPosition, true);
                    }
                    polygonPointer = evt.pointerId;
                    target.CapturePointer(polygonPointer);
                    SetCurrent(evt.localPosition, evt.ctrlKey);
                }
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private void Move(PointerMoveEvent evt)
            {
                if (owner.IsUvSelectionTool && evt.pressedButtons == 0)
                    owner.SetUvHovered(owner.PickUvIsland(evt.localPosition));
                if (!owner.IsAreaSelectionTool || !HasGesture || owner.activeDocument == null ||
                    (owner.canvasZoomManipulator?.IsNavigating ?? false)) return;
                if (IsFreehandDrawing)
                {
                    if (evt.pointerId != freehandPointer) return;
                    if ((evt.pressedButtons & 1) == 0 || !target.HasPointerCapture(freehandPointer)) Cancel();
                    else TraceFreehand(evt.localPosition, false);
                    owner.areaSelectionOverlay?.MarkDirtyRepaint();
                    evt.StopImmediatePropagation();
                    return;
                }
                if (RectangleDragging && (evt.pointerId != pointer || (evt.pressedButtons & 1) == 0)) { Cancel(); return; }
                if (polygonPointer >= 0)
                {
                    if (evt.pointerId != polygonPointer) return;
                    if ((evt.pressedButtons & 1) == 0 || !target.HasPointerCapture(polygonPointer)) { AbortPolygonPress(); return; }
                    UpdatePolygonDrag(evt.localPosition);
                }
                pointerAlt = evt.altKey;
                pointerShift = evt.shiftKey;
                UpdateCurrent(evt.localPosition, evt.shiftKey, evt.ctrlKey);
                owner.RefreshCanvasPointerCursor();
                evt.StopImmediatePropagation();
            }
            private void Up(PointerUpEvent evt)
            {
                if (evt.button == 0 && evt.pointerId == freehandPointer)
                {
                    if (target.HasPointerCapture(freehandPointer) && TraceFreehand(evt.localPosition, true)) CompleteFreehand();
                    else Cancel();
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (evt.button == 0 && polygonPointer == evt.pointerId)
                {
                    if (!target.HasPointerCapture(polygonPointer))
                    {
                        AbortPolygonPress();
                        WhimTexUI.ConsumeEvent(evt);
                        return;
                    }
                    UpdatePolygonDrag(evt.localPosition);
                    pointerShift = evt.shiftKey;
                    SetCurrent(evt.localPosition, evt.ctrlKey);
                    PolygonPress action = polygonPress;
                    int vertex = pressedVertex;
                    bool dragged = vertexDragging;
                    bool created = createdVertex >= 0;
                    Vector2 released = evt.localPosition;
                    bool polygonClick = (released - pressPosition).sqrMagnitude < VertexDragThreshold * VertexDragThreshold;
                    EndPolygonPress(false);
                    if (created)
                    {
                        UpdateCloseArming(true);
                        if (action == PolygonPress.Append)
                        {
                            lastAppendPosition = released;
                            lastAppendCanComplete = !bypassPolygonPress;
                        }
                    }
                    else if (dragged) UpdateCloseArming(true);
                    else if (polygonClick && (action == PolygonPress.Close || action == PolygonPress.Complete)) CompletePolygon();
                    else if (action == PolygonPress.Delete && polygonClick && vertex >= 0 && vertex < Vertices.Count)
                    {
                        Vertices.RemoveAt(vertex);
                        UpdateCloseArming(true);
                    }
                    pointerAlt = evt.altKey;
                    SetCurrent(evt.localPosition, evt.ctrlKey);
                    owner.areaSelectionOverlay?.MarkDirtyRepaint();
                    owner.RefreshCanvasPointerCursor();
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (evt.button != 0 || pointer != evt.pointerId) return;
                UpdateCurrent(evt.localPosition, evt.shiftKey, evt.ctrlKey);
                Vector2 a = Start, b = Current;
                var operation = combine; bool tiled = wrap, ellipse = EllipseDragging, smooth = antialias;
                bool click = (a - b).magnitude * owner.toolkitCanvas.PixelScale < 3f;
                Cancel();
                if (!click) owner.ChangeAreaSelection(s =>
                {
                    if (ellipse) s.Ellipse(a, b, operation, tiled, smooth);
                    else s.Rectangle(a, b, operation, tiled, smooth);
                });
                else if (operation == SelectionCombine.Replace) owner.ChangeAreaSelection(s => s.Clear());
                WhimTexUI.ConsumeEvent(evt);
            }
            internal void CompletePolygon()
            {
                if (IsFreehandDrawing)
                {
                    if (TraceFreehand(pointerPosition, true)) CompleteFreehand();
                    return;
                }
                if (polygonPointer >= 0 && !target.HasPointerCapture(polygonPointer)) AbortPolygonPress();
                else EndPolygonPress(false);
                lastAppendCanComplete = false;
                if (Vertices.Count < 3) return;
                Vector2[] points = Vertices.ToArray();
                var operation = combine; bool tiled = wrap, smooth = antialias;
                Cancel();
                owner.ChangeAreaSelection(s => s.Polygon(points, operation, tiled, smooth));
            }
            internal void RemoveVertex()
            {
                if (IsFreehandDrawing) { Cancel(); return; }
                bool creating = createdVertex >= 0;
                EndPolygonPress(true);
                lastAppendCanComplete = false;
                if (!creating && Vertices.Count > 0) Vertices.RemoveAt(Vertices.Count - 1);
                UpdateCloseArming(true);
                if (owner.activeDocument != null) SetCurrent(pointerPosition, pointerControl);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            internal void Cancel()
            {
                int captured = pointer; pointer = -1;
                int traced = freehandPointer; freehandPointer = -1;
                FreehandPoints.Clear();
                EndPolygonPress(false);
                ClearPolygonHover();
                closeArmed = false;
                lastAppendCanComplete = false;
                Vertices.Clear();
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                if (traced >= 0 && target != null && target.HasPointerCapture(traced)) target.ReleasePointer(traced);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private void Lost(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId == pointer) Cancel();
                else if (evt.pointerId == freehandPointer) Cancel();
                else if (evt.pointerId == polygonPointer) AbortPolygonPress();
            }
            private void Interrupted(PointerCancelEvent evt)
            {
                if (IsFreehandDrawing) Cancel();
                else if (polygonPointer >= 0) AbortPolygonPress();
                else if (RectangleDragging) Cancel();
            }
            private void Leave(PointerLeaveEvent evt)
            {
                if (polygonPointer >= 0 || IsFreehandDrawing) return;
                ClearPolygonHover();
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
            }
            private void Blurred(BlurEvent evt)
            {
                if (IsFreehandDrawing) Cancel();
                else if (polygonPointer >= 0) AbortPolygonPress();
            }
            private void Detached(DetachFromPanelEvent evt) => Cancel();
        }

        private sealed class AreaSelectionOverlay : VisualElement
        {
            private readonly WhimTexWindow owner;
            private readonly List<Vector4> edges = new List<Vector4>();
            private readonly List<Vector4> visibleEdges = new List<Vector4>();
            private CanvasSelection cached;
            private int revision = -1;
            internal AreaSelectionOverlay(WhimTexWindow owner)
            {
                this.owner = owner;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-area-overlay");
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
            private Vector2 CanvasViewPoint(Vector2 canvas, Rect image) => owner.toolkitCanvas.ToView(new Vector2(
                image.x + canvas.x * image.width / owner.activeDocument.width,
                image.yMax - canvas.y * image.height / owner.activeDocument.height));
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
                if (owner.activeDocument == null || contentRect.width < 1f || contentRect.height < 1f) return;
                Rect image = owner.toolkitCanvas.DisplayedImageRect;
                if (image.width <= .0001f || image.height <= .0001f) return;
                visibleEdges.Clear();
                int left = 0, right = 0, top = 0, bottom = 0;
                if (owner.tiledCanvas)
                {
                    Rect bounds = owner.toolkitCanvas.VisibleCanvasBounds;
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
                        AddVisible(CanvasViewPoint(new Vector2(e.x, e.y), tile), CanvasViewPoint(new Vector2(e.z, e.w), tile));
                    }
                }
                var gesture = owner.areaSelectionManipulator;
                if (gesture != null && gesture.RectangleDragging && gesture.EllipseDragging)
                {
                    Vector2 center = (gesture.Start + gesture.Current) * .5f;
                    Vector2 radius = (gesture.Current - gesture.Start) * .5f;
                    float viewRadius = Mathf.Max(Mathf.Abs(radius.x), Mathf.Abs(radius.y)) * owner.toolkitCanvas.PixelScale;
                    int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.PI * Mathf.Sqrt(viewRadius)), 24, 512);
                    Vector2 previous = CanvasViewPoint(center + new Vector2(radius.x, 0f), image);
                    for (int i = 1; i <= segments; i++)
                    {
                        float angle = i * Mathf.PI * 2f / segments;
                        Vector2 next = CanvasViewPoint(center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y), image);
                        AddVisible(previous, next);
                        previous = next;
                    }
                }
                else if (gesture != null && gesture.RectangleDragging)
                {
                    Vector2 a = CanvasViewPoint(gesture.Start, image), b = CanvasViewPoint(gesture.Current, image);
                    Vector2 c = CanvasViewPoint(new Vector2(gesture.Current.x, gesture.Start.y), image);
                    Vector2 d = CanvasViewPoint(new Vector2(gesture.Start.x, gesture.Current.y), image);
                    AddVisible(a, c); AddVisible(c, b);
                    AddVisible(b, d); AddVisible(d, a);
                }
                else if (gesture != null && gesture.IsFreehandDrawing && gesture.FreehandPoints.Count > 0)
                {
                    for (int i = 1; i < gesture.FreehandPoints.Count; i++)
                        AddVisible(CanvasViewPoint(gesture.FreehandPoints[i - 1], image), CanvasViewPoint(gesture.FreehandPoints[i], image));
                    AddVisible(CanvasViewPoint(gesture.FreehandPoints[gesture.FreehandPoints.Count - 1], image),
                        CanvasViewPoint(gesture.FreehandCurrent, image));
                }
                else if (gesture != null && gesture.Vertices.Count > 0)
                {
                    for (int i = 1; i < gesture.Vertices.Count; i++)
                        AddVisible(CanvasViewPoint(gesture.Vertices[i - 1], image), CanvasViewPoint(gesture.Vertices[i], image));
                    if (gesture.ShowPolygonTail)
                        AddVisible(CanvasViewPoint(gesture.Vertices[gesture.Vertices.Count - 1], image), CanvasViewPoint(gesture.Current, image));
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
                if (gesture != null && gesture.Vertices.Count > 0)
                {
                    float ppi = Mathf.Max(.0001f, EditorGUIUtility.pixelsPerPoint);
                    float radius = Mathf.Max(2f, Mathf.Round(2f * ppi)) / ppi;
                    for (int i = 0; i < gesture.Vertices.Count; i++)
                    {
                        bool closing = i == 0 && gesture.ClosingPolygon;
                        bool hover = i == gesture.HoverVertex;
                        bool deleting = hover && gesture.DeletingVertex;
                        Color color = deleting ? new Color(1f, .25f, .25f) :
                            closing || hover ? new Color(.3f, .8f, 1f) : Color.white;
                        DrawPolygonMarker(painter, CanvasViewPoint(gesture.Vertices[i], image),
                            radius + (closing || hover ? 1f / ppi : 0f), color, deleting ? -1 : 0, ppi);
                    }
                    if (gesture.InsertSegment >= 0)
                        DrawPolygonMarker(painter, CanvasViewPoint(gesture.InsertPoint, image),
                            radius + 2f / ppi, new Color(.3f, .8f, 1f), 1, ppi);
                }
            }
            private void DrawPolygonMarker(Painter2D painter, Vector2 position, float radius, Color color, int symbol, float ppi)
            {
                Vector2 pixels = this.LocalToWorld(position) * ppi;
                Vector2 p = this.WorldToLocal(new Vector2(Mathf.Floor(pixels.x) + .5f, Mathf.Floor(pixels.y) + .5f) / ppi);
                if (!contentRect.Overlaps(new Rect(p - Vector2.one * radius, Vector2.one * (radius * 2f)))) return;
                painter.lineWidth = 1f / ppi;
                painter.strokeColor = Color.black;
                painter.fillColor = color;
                painter.BeginPath();
                painter.MoveTo(p + new Vector2(-radius, -radius));
                painter.LineTo(p + new Vector2(radius, -radius));
                painter.LineTo(p + new Vector2(radius, radius));
                painter.LineTo(p + new Vector2(-radius, radius));
                painter.ClosePath();
                painter.Fill(); painter.Stroke();
                if (symbol == 0) return;
                float extent = radius - 1f / ppi;
                painter.strokeColor = Color.white;
                painter.BeginPath();
                painter.MoveTo(p - new Vector2(extent, 0f)); painter.LineTo(p + new Vector2(extent, 0f));
                if (symbol > 0)
                {
                    painter.MoveTo(p - new Vector2(0f, extent)); painter.LineTo(p + new Vector2(0f, extent));
                }
                painter.Stroke();
            }
        }
    }
}
