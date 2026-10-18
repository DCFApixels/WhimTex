using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed partial class VectorManipulator : PointerManipulator
        {
            readonly WhimTexWindow owner;
            readonly VisualElement overlay = new() { pickingMode = PickingMode.Ignore };
            readonly HashSet<string> selectedPoints = new();
            WhimTexDocument document;
            Layer layer;
            Layer insertionAnchor;
            bool creatingLayer;
            VectorLayerBehaviour behaviour;
            VectorShape candidate, original;
            string originalJson;
            VectorContour pen;
            VectorPoint activePoint;
            int pointer = -1, pointerButton, tangent;
            bool moved, marquee;
            Vector2 dimensions, start, startView, lastView;
            ProjectiveMatrix preparedTransform;
            CanvasTool gestureTool;
            internal bool IsDragging => pointer >= 0 || pen != null;
            internal void RepaintOutline() => overlay.MarkDirtyRepaint();
            internal VectorManipulator(WhimTexWindow owner)
            {
                this.owner = owner;
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
                owner.toolkitCanvas.ViewChanged -= overlay.MarkDirtyRepaint;
                overlay.RemoveFromHierarchy();
            }
            bool Valid => document != null && owner.activeDocument == document && IsVectorTool(owner.canvasTool) &&
                layer?.Behaviour == behaviour && dimensions == new Vector2(document.width, document.height) &&
                layer.RenderTransform.ToMatrix(document.width, document.height).Equals(preparedTransform) &&
                (creatingLayer ? document.FindLayer(layer.Id) == null && owner.GetSelectedLayer() == insertionAnchor :
                    ReferenceEquals(document.FindLayer(layer.Id), layer) && owner.GetSelectedLayer() == layer) &&
                !WhimTexApi.IsLayerContentLocked(document, layer);
            internal void Refresh()
            {
                if (owner.canvasTool != CanvasTool.VectorBrush || owner.activeDocument != brushLineDocument || owner.GetSelectedLayer() != brushLineLayer)
                { brushLineDocument = null; brushLineLayer = null; }
                if ((candidate != null || pointer >= 0) && !Valid) Cancel();
                overlay.MarkDirtyRepaint();
            }
            Vector2 Local(Vector2 view, bool noSnap = false)
            {
                var canvas = owner.toolkitCanvas;
                Rect image = canvas.ImageRect;
                Vector2 p = canvas.ToCanvas(view);
                Vector2 pixels = new((p.x - image.x) / image.width * dimensions.x, (image.yMax - p.y) / image.height * dimensions.y);
                if (!noSnap) pixels = owner.SnapCanvasGuidePoint(pixels);
                return layer.RenderTransform.Unmap(new Vector2(pixels.x / dimensions.x, pixels.y / dimensions.y), dimensions);
            }
            Vector2 View(Vector2 local)
            {
                var canvas = owner.toolkitCanvas; Rect image = canvas.ImageRect;
                Vector2 p = layer.RenderTransform.Map(local, dimensions);
                return canvas.ToView(new Vector2(image.x + p.x * image.width, image.yMax - p.y * image.height));
            }
            static bool Finite(Vector2 p) => float.IsFinite(p.x) && float.IsFinite(p.y);
            bool Prepare(bool create)
            {
                document = owner.activeDocument;
                if (document == null) return false;
                dimensions = new Vector2(document.width, document.height);
                layer = owner.GetSelectedLayer();
                insertionAnchor = layer;
                bool editMask = owner.canvasTool == CanvasTool.VectorBrush && owner.IsEditingVectorMask(layer);
                creatingLayer = layer?.Behaviour is not VectorLayerBehaviour || create && !editMask && ((VectorLayerBehaviour)layer.Behaviour).shapes.Count > 0;
                if (creatingLayer)
                {
                    if (!create) return false;
                    layer = new Layer(new VectorLayerBehaviour { referenceSize = dimensions });
                }
                behaviour = (VectorLayerBehaviour)layer.Behaviour;
                preparedTransform = layer.RenderTransform.ToMatrix(document.width, document.height);
                return !WhimTexApi.IsLayerContentLocked(document, layer);
            }
            void Down(PointerDownEvent evt)
            {
                if (owner.IsVectorMaskRebuilding && IsVectorTool(owner.canvasTool) && (evt.button == 0 || evt.button == 1))
                {
                    owner.ShowNotification(new GUIContent("Rebuilding the vector mask. Wait for completion or press Escape to cancel."));
                    WhimTexUI.ConsumeEvent(evt); return;
                }
                bool brushTool = owner.canvasTool == CanvasTool.VectorBrush;
                if (!IsVectorTool(owner.canvasTool) || evt.button != 0 && !(brushTool && evt.button == 1) || evt.altKey && owner.canvasTool != CanvasTool.VectorNodes ||
                    pointer >= 0 || owner.activeDocument == null || !target.contentRect.Contains(evt.localPosition) || owner.toolkitCanvas.PixelScale <= 0) return;
                if (brushTool && (owner.GetSelectedLayer() is Layer selected && WhimTexApi.IsLayerContentLocked(owner.activeDocument, selected) ||
                    evt.button == 1 && !owner.IsEditingVectorMask(owner.GetSelectedLayer())))
                { WhimTexUI.ConsumeEvent(evt); return; }
                bool drawing = owner.canvasTool == CanvasTool.VectorPen || owner.canvasTool == CanvasTool.VectorBrush;
                if (pen == null && !Prepare(drawing) || pen != null && !Valid) return;
                Vector2 p = Local(evt.localPosition, evt.ctrlKey);
                if (!Finite(p)) return;
                owner.Focus(); target.Focus();
                start = p; startView = lastView = evt.localPosition; moved = false; tangent = 0; marquee = false;
                gestureTool = owner.canvasTool;
                if (gestureTool == CanvasTool.VectorBrush)
                {
                    if (!BeginBrush(p, evt.shiftKey, evt.localPosition, evt.button == 1)) { WhimTexUI.ConsumeEvent(evt); return; }
                }
                else if (gestureTool == CanvasTool.VectorPen)
                {
                    if (pen != null && pen.points.Count >= 3 && Vector2.Distance(View(pen.points[0].position), evt.localPosition) < 8)
                    { FinishPen(true); WhimTexUI.ConsumeEvent(evt); return; }
                    if (pen == null)
                    {
                        candidate = new VectorShape { name = "Path", fillColor = WhimTexColorInputs.DisplayColor(owner.vectorFillColor), stroke = true, strokeWidth = 2 };
                        pen = new VectorContour(); candidate.contours.Add(pen); selectedPoints.Clear();
                    }
                    if (pen.points.Count >= 4096) { owner.ShowNotification(new GUIContent("Finish this path before adding more points.")); return; }
                    activePoint = new VectorPoint { position = p };
                    pen.points.Add(activePoint); selectedPoints.Clear(); selectedPoints.Add(activePoint.id);
                }
                else
                {
                    original = owner.SelectedVectorShape(behaviour);
                    VectorPoint hit = null;
                    if (gestureTool == CanvasTool.VectorNodes && original != null) hit = HitPoint(original, evt.localPosition, out tangent);
                    if (hit == null)
                    {
                        VectorShape picked = Pick(evt.localPosition);
                        if (picked != null && (original == null || picked.id != original.id)) selectedPoints.Clear();
                        if (picked != null) { original = picked; owner.SelectVectorShape(picked); }
                    }
                    if (original == null) { WhimTexUI.ConsumeEvent(evt); return; }
                    originalJson = JsonUtility.ToJson(original); candidate = CopyVectorShape(original);
                    if (hit != null)
                    {
                        if (evt.altKey && tangent == 0)
                        {
                            foreach (var contour in candidate.contours) contour.points.RemoveAll(point => point.id == hit.id);
                            FinishEdit("Delete Vector Point"); WhimTexUI.ConsumeEvent(evt); return;
                        }
                        if (!evt.shiftKey && !selectedPoints.Contains(hit.id)) selectedPoints.Clear();
                        if (evt.shiftKey && selectedPoints.Contains(hit.id)) selectedPoints.Remove(hit.id); else selectedPoints.Add(hit.id);
                        activePoint = FindPoint(candidate, hit.id);
                    }
                    else if (gestureTool == CanvasTool.VectorNodes)
                    {
                        if (evt.clickCount == 2 && InsertAt(candidate, evt.localPosition))
                        { FinishEdit("Insert Vector Point"); WhimTexUI.ConsumeEvent(evt); return; }
                        marquee = true;
                        if (!evt.shiftKey) selectedPoints.Clear();
                    }
                }
                pointer = evt.pointerId; pointerButton = evt.button; target.CapturePointer(pointer);
                if (gestureTool == CanvasTool.VectorBrush) owner.UpdateCanvasCursor(evt.localPosition, evt.altKey);
                overlay.MarkDirtyRepaint(); WhimTexUI.ConsumeEvent(evt);
            }
            void Move(PointerMoveEvent evt)
            {
                if (pointer < 0 || evt.pointerId != pointer) return;
                if (!Valid || !target.HasPointerCapture(pointer) || owner.canvasTool != gestureTool || (evt.pressedButtons & (1 << pointerButton)) == 0 ||
                    gestureTool == CanvasTool.VectorBrush && !ValidBrushSource) { Cancel(); return; }
                Update(evt.localPosition, evt.shiftKey, evt.ctrlKey, evt.altKey);
                WhimTexUI.ConsumeEvent(evt);
            }
            void Update(Vector2 view, bool shift, bool control, bool alt)
            {
                Vector2 p = gestureTool == CanvasTool.VectorBrush ? BrushPosition(view, shift, control) : Local(view, control);
                if (!Finite(p)) return;
                lastView = view; moved |= (view - startView).sqrMagnitude > 4;
                if (gestureTool == CanvasTool.VectorBrush)
                {
                    UpdateBrush(p);
                    // This manipulator consumes captured pointer events before the canvas listener.
                    owner.UpdateCanvasCursor(view, alt);
                }
                else if (gestureTool == CanvasTool.VectorPen)
                {
                    if (moved) { activePoint.outTangent = p - activePoint.position; activePoint.inTangent = -activePoint.outTangent; activePoint.tangentMode = VectorTangentMode.Mirrored; }
                }
                else if (!marquee && candidate != null)
                {
                    Vector2 delta = p - start;
                    if (shift && tangent == 0)
                    { if (Mathf.Abs(delta.x * dimensions.x) > Mathf.Abs(delta.y * dimensions.y)) delta.y = 0; else delta.x = 0; }
                    string pointId = activePoint?.id;
                    candidate = CopyVectorShape(original);
                    if (tangent != 0 && pointId != null)
                    {
                        activePoint = FindPoint(candidate, pointId);
                        Vector2 value = p - activePoint.position;
                        if (alt) activePoint.tangentMode = VectorTangentMode.Corner;
                        if (tangent < 0) activePoint.inTangent = value; else activePoint.outTangent = value;
                        if (activePoint.tangentMode != VectorTangentMode.Corner)
                        {
                            Vector2 other = tangent < 0 ? activePoint.outTangent : activePoint.inTangent;
                            other = activePoint.tangentMode == VectorTangentMode.Mirrored ? -value : -value.normalized * other.magnitude;
                            if (tangent < 0) activePoint.outTangent = other; else activePoint.inTangent = other;
                        }
                    }
                    else foreach (var contour in candidate.contours) foreach (var point in contour.points)
                        if (selectedPoints.Contains(point.id)) point.position += delta;
                }
                overlay.MarkDirtyRepaint();
            }
            void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || pointer < 0 || evt.button != pointerButton) return;
                if (!Valid || !target.HasPointerCapture(pointer) || gestureTool == CanvasTool.VectorBrush && !ValidBrushSource) { Cancel(); return; }
                Update(evt.localPosition, evt.shiftKey, evt.ctrlKey, evt.altKey);
                if (pointer < 0) return;
                ReleasePointer();
                if (gestureTool == CanvasTool.VectorBrush) FinishBrush();
                else if (gestureTool != CanvasTool.VectorPen)
                {
                    if (marquee)
                    {
                        Rect rect = Rect.MinMaxRect(Mathf.Min(startView.x, lastView.x), Mathf.Min(startView.y, lastView.y), Mathf.Max(startView.x, lastView.x), Mathf.Max(startView.y, lastView.y));
                        foreach (var contour in original.contours) foreach (var point in contour.points) if (rect.Contains(View(point.position))) selectedPoints.Add(point.id);
                        candidate = null; marquee = false;
                    }
                    else if (moved) FinishEdit("Edit Vector Geometry");
                    else candidate = null;
                }
                overlay.MarkDirtyRepaint(); WhimTexUI.ConsumeEvent(evt);
            }
            void FinishEdit(string action)
            {
                if (Valid && candidate != null && original != null && JsonUtility.ToJson(original) == originalJson && behaviour.shapes.Contains(original))
                    owner.CommitVectorShape(layer, candidate, action);
                candidate = null; original = null; activePoint = null;
            }
            internal void FinishPen(bool closed)
            {
                if (pen == null || !Valid || pen.points.Count < (closed ? 3 : 2)) return;
                ReleasePointer(); pen.closed = closed;
                if (!closed) candidate.fillMode = VectorFillMode.None;
                owner.CommitVectorShape(layer, candidate, "Create Vector Path");
                pen = null; candidate = null; activePoint = null; selectedPoints.Clear(); overlay.MarkDirtyRepaint();
            }
            internal void SetTangentMode(VectorTangentMode mode)
            {
                if (!Prepare(false)) return;
                gestureTool = CanvasTool.VectorNodes;
                original = owner.SelectedVectorShape(behaviour); if (original == null) return;
                originalJson = JsonUtility.ToJson(original); candidate = CopyVectorShape(original);
                foreach (var contour in candidate.contours) for (int i = 0; i < contour.points.Count; i++)
                {
                    var point = contour.points[i]; if (!selectedPoints.Contains(point.id)) continue;
                    point.tangentMode = mode;
                    if (mode == VectorTangentMode.Corner) continue;
                    Vector2 direction = point.outTangent;
                    if (direction.sqrMagnitude < 1e-12f)
                    {
                        var prev = contour.points[(i + contour.points.Count - 1) % contour.points.Count];
                        var next = contour.points[(i + 1) % contour.points.Count];
                        direction = (next.position - prev.position) / 6;
                    }
                    point.outTangent = direction;
                    point.inTangent = mode == VectorTangentMode.Mirrored ? -direction : -direction.normalized * Mathf.Max(point.inTangent.magnitude, direction.magnitude);
                }
                FinishEdit("Change Vector Tangents"); overlay.MarkDirtyRepaint();
            }
            internal bool HandleKey(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Escape && owner.IsVectorMaskRebuilding)
                { Cancel(); WhimTexUI.ConsumeEvent(evt); return true; }
                if (!IsVectorTool(owner.canvasTool) || IsTextInputTarget(evt.target as VisualElement)) return false;
                if ((evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift) && SetBrushShift(true))
                    owner.RefreshCanvasPointerCursor();
                else if (evt.keyCode == KeyCode.Escape) Cancel();
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                { if (owner.canvasTool == CanvasTool.VectorBrush) owner.FinishVectorMaskEditing(); else FinishPen(false); }
                else if (evt.keyCode == KeyCode.Backspace && pen != null)
                { if (pen.points.Count > 0) pen.points.RemoveAt(pen.points.Count - 1); overlay.MarkDirtyRepaint(); }
                else if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && Prepare(false))
                {
                    gestureTool = owner.canvasTool;
                    original = owner.SelectedVectorShape(behaviour); if (original == null) return false;
                    if (owner.canvasTool == CanvasTool.VectorNodes && selectedPoints.Count > 0)
                    {
                        originalJson = JsonUtility.ToJson(original); candidate = CopyVectorShape(original);
                        foreach (var contour in candidate.contours) contour.points.RemoveAll(p => selectedPoints.Contains(p.id));
                        FinishEdit("Delete Vector Points"); selectedPoints.Clear();
                    }
                    else
                    {
                        owner.DeleteLayers(new List<Layer> { layer });
                        owner.SelectVectorShape(null);
                    }
                }
                else return false;
                WhimTexUI.ConsumeEvent(evt); return true;
            }
            void ReleasePointer() { int id = pointer; pointer = -1; if (id >= 0 && target.HasPointerCapture(id)) target.ReleasePointer(id); }
            internal void Cancel()
            {
                owner.CancelVectorMaskRebuild();
                ReleasePointer(); CancelBrush(); candidate = original = null; pen = null; activePoint = null; marquee = false;
                document = null; layer = null; behaviour = null; overlay.MarkDirtyRepaint();
            }
            void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer && !target.HasPointerCapture(pointer)) Cancel(); }
            void Interrupted(PointerCancelEvent evt) => Cancel();
            void Detached(DetachFromPanelEvent evt) => Cancel();
            static VectorPoint FindPoint(VectorShape shape, string id)
            { foreach (var contour in shape.contours) foreach (var point in contour.points) if (point.id == id) return point; return null; }
            static Vector2 Cubic(VectorPoint a, VectorPoint b, float t)
            {
                float u = 1 - t;
                return u * u * u * a.position + 3 * u * u * t * (a.position + a.outTangent) + 3 * u * t * t * (b.position + b.inTangent) + t * t * t * b.position;
            }
            VectorPoint HitPoint(VectorShape shape, Vector2 view, out int handle)
            {
                handle = 0;
                foreach (var contour in shape.contours) foreach (var point in contour.points)
                    if (selectedPoints.Contains(point.id))
                    {
                        if (point.inTangent.sqrMagnitude > 1e-12f && Vector2.Distance(View(point.position + point.inTangent), view) < 7) { handle = -1; return point; }
                        if (point.outTangent.sqrMagnitude > 1e-12f && Vector2.Distance(View(point.position + point.outTangent), view) < 7) { handle = 1; return point; }
                    }
                foreach (var contour in shape.contours) foreach (var point in contour.points) if (Vector2.Distance(View(point.position), view) < 7) return point;
                return null;
            }
            VectorShape Pick(Vector2 view) => PickVectorGeometry(behaviour, view, View);
            static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b, out float t)
            { Vector2 d = b - a; t = d.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude) : 0; return Vector2.Distance(p, a + d * t); }
            bool InsertAt(VectorShape shape, Vector2 view)
            {
                float best = 7, time = 0; VectorContour found = null; int edge = -1;
                foreach (var contour in shape.contours)
                    for (int i = 0; i < (contour.closed ? contour.points.Count : contour.points.Count - 1); i++)
                    {
                        var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count]; Vector2 previous = View(a.position);
                        for (int j = 1; j <= 32; j++)
                        {
                            Vector2 next = View(Cubic(a, b, j / 32f)); float distance = EdgeDistance(view, previous, next, out float along);
                            if (distance < best) { best = distance; found = contour; edge = i; time = (j - 1 + along) / 32; }
                            previous = next;
                        }
                    }
                if (found == null || time <= 1e-5f || time >= 1 - 1e-5f) return false;
                var inserted = VectorGeometry.SplitSegment(found, edge, time);
                selectedPoints.Clear(); selectedPoints.Add(inserted.id); return true;
            }
            void Draw(MeshGenerationContext context)
            {
                if (owner.canvasTool == CanvasTool.None || owner.canvasTool == CanvasTool.Transform)
                {
                    owner.DrawSelectedVectorContours(context.painter2D);
                    return;
                }
                if (!IsVectorTool(owner.canvasTool) || owner.activeDocument == null) return;
                if (candidate == null && brushBuilder == null && !Prepare(false)) return;
                if (!Valid) return;
                var p = context.painter2D; p.lineWidth = 1.2f;
                var selected = owner.SelectedVectorShape(behaviour);
                foreach (var shape in behaviour.shapes)
                    if (shape.visible && shape.id != candidate?.id) DrawShape(p, shape, shape.id == selected?.id);
                if (candidate != null) DrawShape(p, candidate, true);
                DrawBrush(context);
                if (marquee)
                {
                    p.strokeColor = Color.cyan; p.BeginPath(); p.MoveTo(startView); p.LineTo(new Vector2(lastView.x, startView.y)); p.LineTo(lastView); p.LineTo(new Vector2(startView.x, lastView.y)); p.ClosePath(); p.Stroke();
                }
            }
            void DrawShape(Painter2D p, VectorShape shape, bool selected)
            {
                p.strokeColor = selected ? new Color(.25f, .8f, 1) : new Color(.4f, .65f, .8f, .45f);
                foreach (var contour in shape.contours)
                {
                    if (contour.points.Count == 0) continue;
                    p.BeginPath(); p.MoveTo(View(contour.points[0].position));
                    for (int i = 0; i < (contour.closed ? contour.points.Count : contour.points.Count - 1); i++)
                    { var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count]; p.BezierCurveTo(View(a.position + a.outTangent), View(b.position + b.inTangent), View(b.position)); }
                    if (contour.closed) p.ClosePath(); p.Stroke();
                    if (!selected || owner.canvasTool == CanvasTool.VectorBrush) continue;
                    foreach (var point in contour.points)
                    {
                        Vector2 pos = View(point.position); bool marked = selectedPoints.Contains(point.id);
                        if (marked)
                            foreach (Vector2 handle in new[] { point.inTangent, point.outTangent })
                            {
                                if (handle.sqrMagnitude < 1e-12f) continue;
                                Vector2 h = View(point.position + handle); p.BeginPath(); p.MoveTo(pos); p.LineTo(h); p.Stroke(); Dot(p, h, 3, Color.white);
                            }
                        Dot(p, pos, 3.5f, marked ? Color.cyan : Color.white);
                    }
                }
            }
            static void Dot(Painter2D p, Vector2 pos, float radius, Color color)
            { if (!Finite(pos)) return; p.fillColor = color; p.BeginPath(); p.Arc(pos, radius, 0, 360); p.Fill(); }
        }
    }
}
