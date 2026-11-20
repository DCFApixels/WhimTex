using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        private class PreviewPane : VisualElement
        {
            protected readonly MorphGroupEditorWindow editor;
            protected readonly VisualElement frame;
            protected readonly Image image;
            internal readonly VisualElement Surface;
            protected readonly Label caption;
            private readonly Label error;
            internal RenderTexture Texture { get; private set; }
            internal ulong Revision { get; private set; }
            protected bool failed;

            internal PreviewPane(MorphGroupEditorWindow editor, string name, string title)
            {
                this.editor = editor;
                AddToClassList("whimtex-morph-pane");
                caption = new Label(title);
                caption.AddToClassList("whimtex-morph-caption");
                Add(caption);
                Surface = new VisualElement { name = name };
                Surface.AddToClassList("whimtex-morph-surface");
                Add(Surface);
                frame = new VisualElement { pickingMode = PickingMode.Ignore };
                frame.AddToClassList("whimtex-morph-frame");
                frame.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
                Surface.Add(frame);
                image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                image.StretchToParentSize();
                frame.Add(image);
                error = new Label { pickingMode = PickingMode.Ignore };
                error.AddToClassList("whimtex-morph-error");
                Surface.Add(error);
                SetVisible(error, false);
                Surface.RegisterCallback<GeometryChangedEvent>(_ => FitFrame());
                RegisterCallback<DetachFromPanelEvent>(_ => Release());
            }

            protected void FitFrame()
            {
                var doc = editor.document;
                Rect area = Surface.contentRect;
                if (doc == null || !(area.width > 0 && area.height > 0)) return;
                // Align all previews with the 10-point marker radius at the UV boundaries.
                float scale = Mathf.Min(Mathf.Max(1, area.width - 20) / doc.width, Mathf.Max(1, area.height - 20) / doc.height);
                var size = new Vector2(doc.width, doc.height) * scale;
                Rect desired = new Rect(area.center - size * .5f, size);
                frame.style.left = desired.x;
                frame.style.top = desired.y;
                frame.style.width = size.x;
                frame.style.height = size.y;
            }

            internal void ShowTexture(RenderTexture next)
            {
                if (next == null) throw new InvalidOperationException("No preview image was returned.");
                image.image = next;
                if (Texture != null) ProfiledRenderTexture.ReleaseTemporary(Texture);
                Texture = next;
                Revision++;
                failed = false;
                SetVisible(frame, true);
                SetVisible(error, false);
            }

            internal void ShowFailure(Exception ex)
            {
                Release();
                failed = true;
                error.text = "Preview unavailable. " + ex.Message;
                SetVisible(error, true);
            }

            internal virtual void Release()
            {
                image.image = null;
                if (Texture != null) ProfiledRenderTexture.ReleaseTemporary(Texture);
                Texture = null;
                failed = false;
                SetVisible(frame, false);
                SetVisible(error, false);
            }
        }

        private sealed class EndpointPane : PreviewPane
        {
            private readonly bool isFrom;
            private readonly List<Label> markers = new();
            private readonly VisualElement lines, pointRadius;
            private bool drawing, tangentDrag;
            private int markDrag = -1;
            private float markT;
            private Vector2 strokeStart;
            private Rect ImageRect => Surface.WorldToLocal(image.worldBound);
            private bool undoRecorded;
            private int pointer = -1, dragged = -1;
            private Vector2 dragOffset, dragUv, originalUv, pointerStart;
            private double nextDragUpdate;

            internal EndpointPane(MorphGroupEditorWindow editor, bool isFrom)
                : base(editor, isFrom ? "morphPreviewA" : "morphPreviewB", isFrom ? "A" : "B")
            {
                this.isFrom = isFrom;
                lines = new VisualElement { pickingMode = PickingMode.Ignore };
                lines.StretchToParentSize();
                frame.Add(lines);
                lines.generateVisualContent += DrawSegments;
                pointRadius = new VisualElement { name = "morphPointRadiusPreview", pickingMode = PickingMode.Ignore };
                pointRadius.AddToClassList("whimtex-morph-point-radius");
                frame.Add(pointRadius);
                Surface.focusable = true;
                Surface.RegisterCallback<PointerDownEvent>(Down);
                Surface.RegisterCallback<PointerMoveEvent>(Move);
                Surface.RegisterCallback<PointerUpEvent>(Up);
                Surface.RegisterCallback<PointerCaptureOutEvent>(evt =>
                {
                    if (pointer != evt.pointerId) return;
                    PublishDrag(true);
                    drawing = tangentDrag = false;
                    markDrag = -1;
                    dragged = pointer = -1;
                    RefreshMarkers();
                });
                Surface.RegisterCallback<PointerCancelEvent>(evt =>
                {
                    if (pointer == evt.pointerId) { RollbackDrag(); CancelDrag(); RefreshMarkers(); }
                });
            }

            internal void Bind(Layer layer)
            {
                caption.text = layer == null ? (isFrom ? "A" : "B") : $"{(isFrom ? "A" : "B")} Р’В· {layer.layerName}";
                FitFrame();
            }

            internal void Render(WhimTexDocument doc, Layer group, Layer child, bool invalidated)
            {
                if (child == null) { Release(); return; }
                if (failed && !invalidated) return;
                // Fitting the retained image on resize does not re-render either endpoint.
                float scale = Mathf.Min(1f, 512f / Mathf.Max(doc.width, doc.height));
                int w = Mathf.Max(1, Mathf.RoundToInt(doc.width * scale));
                int h = Mathf.Max(1, Mathf.RoundToInt(doc.height * scale));
                try
                {
                    ShowTexture(doc.RenderMorphEndpoint(group, child, w, h));
                }
                catch (Exception ex) { ShowFailure(ex); }
            }

            internal override void Release()
            {
                CancelDrag();
                base.Release();
            }

            private Vector2 UvAt(Vector2 local)
            {
                Rect rect = ImageRect;
                return new Vector2((local.x - rect.xMin) / rect.width, (rect.yMax - local.y) / rect.height);
            }

            private Vector2 AnchorUv(int index)
            {
                if (editor.SegmentTool)
                {
                    var line = editor.transition.segments[index / 2];
                    return MorphSegment.Side(editor.transition.Position(line, (index & 1) != 0), isFrom);
                }
                var point = editor.transition.anchors[index];
                return isFrom ? point.from : point.to;
            }

            internal void RefreshMarkers()
            {
                lines.MarkDirtyRepaint();
                int count = editor.transition?.anchors.Count ?? 0;
                int visible = count + (!editor.SegmentTool && editor.pendingPane == this ? 1 : 0);
                while (markers.Count < visible)
                {
                    var label = new Label { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("whimtex-morph-marker");
                    frame.Insert(frame.IndexOf(pointRadius), label);
                    markers.Add(label);
                }
                for (int i = 0; i < markers.Count; i++)
                {
                    var marker = markers[i];
                    SetVisible(marker, i < visible);
                    if (i >= visible) continue;
                    bool pending = i == count;
                    Vector2 uv = pending ? editor.pendingUv : !editor.SegmentTool && i == dragged ? dragUv :
                        isFrom ? editor.transition.anchors[i].from : editor.transition.anchors[i].to;
                    marker.text = pending ? "+" : (i + 1).ToString();
                    marker.EnableInClassList("whimtex-morph-marker--selected", !pending && !editor.SegmentTool && i == editor.selected);
                    marker.EnableInClassList("whimtex-morph-marker--pending", pending);
                    marker.style.opacity = editor.SegmentTool ? .35f : 1f;
                    PositionMarker(marker, uv);
                }
                var point = editor.SelectedPoint;
                bool showRadius = point != null && point.radius > 0;
                SetVisible(pointRadius, showRadius);
                if (showRadius)
                {
                    var doc = editor.document;
                    float diameter = 2 * point.radius * Mathf.Min(doc.width, doc.height);
                    pointRadius.style.width = Length.Percent(100 * diameter / doc.width);
                    pointRadius.style.height = Length.Percent(100 * diameter / doc.height);
                    PositionMarker(pointRadius, dragged == editor.selected ? dragUv : isFrom ? point.from : point.to);
                }
            }

            private Vector2 OnEdge(MorphSegment edge, float t) => edge.Evaluate(editor.transition.Position(edge, false),
                editor.transition.Position(edge, true), isFrom, t);

            private static Vector2 PixelAt(Vector2 uv, Rect rect) =>
                new Vector2(rect.xMin + uv.x * rect.width, rect.yMax - uv.y * rect.height);

            private Vector2 LocalPixel(Vector2 uv) => PixelAt(uv, ImageRect);

            private Vector2 ControlUv(int index) => AnchorUv(index) +
                editor.transition.segments[index / 2].Offset(isFrom, (index & 1) != 0);

            private void DrawSegments(MeshGenerationContext ctx)
            {
                if (!editor.IsRaster) return;
                var painter = ctx.painter2D;
                Rect rect = lines.contentRect;
                float shortAxis = Mathf.Min(rect.width, rect.height);
                if (!(shortAxis > 0)) return;
                var influence = new Color(1f, .85f, .2f, .23f);
                Vector2 Pixel(Vector2 uv) => PixelAt(uv, rect);
                void Draw(Vector2 start, Vector2 end, Color color, bool handles, MorphSegment edge = null, bool selected = false)
                {
                    var p = Pixel(start);
                    var q = Pixel(end);
                    painter.BeginPath();
                    painter.MoveTo(p);
                    if (edge?.bezier == true)
                        painter.BezierCurveTo(Pixel(start + edge.Offset(isFrom, false)), Pixel(end + edge.Offset(isFrom, true)), q);
                    else painter.LineTo(q);
                    if (selected && edge.radius > 0 && !editor.MarkTool)
                    {
                        painter.lineCap = LineCap.Round;
                        painter.lineWidth = 2 * edge.radius * shortAxis;
                        painter.strokeColor = influence;
                        painter.Stroke();
                        painter.lineCap = LineCap.Butt;
                    }
                    bool boundary = edge?.boundary == true;
                    painter.lineWidth = boundary ? 4 : 3;
                    painter.strokeColor = boundary ? new Color(1f, .4f, .1f) : Color.black;
                    painter.Stroke();
                    painter.lineWidth = 1.5f;
                    painter.strokeColor = color;
                    painter.Stroke();
                    if (!handles) return;
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.Arc(p, 3, 0, 360);
                    painter.Fill();
                    painter.BeginPath();
                    painter.Arc(q, 3, 0, 360);
                    painter.Stroke();
                }
                var segments = editor.transition?.segments;
                for (int i = 0; i < (segments?.Count ?? 0); i++)
                {
                    var line = segments[i];
                    Vector2 start = AnchorPosition(false), end = AnchorPosition(true);
                    Vector2 AnchorPosition(bool last) => MorphSegment.Side(editor.transition.Position(line, last), isFrom);
                    if (editor.SegmentTool && !tangentDrag && dragged == i * 2) start = dragUv;
                    if (editor.SegmentTool && !tangentDrag && dragged == i * 2 + 1) end = dragUv;
                    bool selected = editor.SegmentTool && editor.selected == i;
                    Draw(start, end, selected ? Color.yellow : Color.cyan, editor.SegmentTool && !editor.MarkTool, line, selected);
                    if (selected && line.bezier && !editor.MarkTool)
                    {
                        for (int side = 0; side < 2; side++)
                        {
                            Vector2 knot = Pixel(side == 0 ? start : end);
                            Vector2 handle = Pixel(ControlUv(i * 2 + side));
                            painter.BeginPath(); painter.MoveTo(knot); painter.LineTo(handle);
                            painter.lineWidth = 1; painter.strokeColor = Color.white; painter.Stroke();
                            Dot(handle, Color.white);
                        }
                    }
                    for (int m = 0; m < (line.marks?.Count ?? 0); m++)
                    {
                        float t = markDrag == m && selected ? markT : isFrom ? line.marks[m].x : line.marks[m].y;
                        Dot(Pixel(OnEdge(line, t)), selected && editor.selectedMark == m ? Color.yellow : Color.magenta);
                    }
                    if (editor.MarkTool && editor.pendingPane == this && i == editor.pendingEdge)
                        Dot(Pixel(OnEdge(line, editor.pendingParameter)), Color.white);
                }
                if (!editor.SegmentTool) return;
                if (drawing) Draw(strokeStart, dragUv, Color.yellow, true);
                else if (!editor.MarkTool && editor.pendingPane == this) Draw(editor.pendingUv, editor.pendingEnd, Color.yellow, true);
                void Dot(Vector2 position, Color color)
                {
                    painter.BeginPath(); painter.Arc(position, 3, 0, 360);
                    painter.fillColor = color; painter.Fill();
                    painter.strokeColor = Color.black; painter.lineWidth = 1; painter.Stroke();
                }
            }

            private static void PositionMarker(VisualElement marker, Vector2 uv)
            {
                marker.style.left = Length.Percent(100f * uv.x);
                marker.style.top = Length.Percent(100f * (1f - uv.y));
            }

            private int Hit(Vector2 local)
            {
                Rect imageRect = ImageRect;
                int best = -1;
                float distance = 10f * 10f;
                int count = editor.SegmentTool ? 2 * (editor.transition?.segments.Count ?? 0) : editor.transition?.anchors.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    float squared = (local - PixelAt(AnchorUv(i), imageRect)).sqrMagnitude;
                    int guide = editor.SegmentTool ? i / 2 : i;
                    if (squared < distance || squared == distance && (best < 0 || guide == editor.selected))
                    { best = i; distance = squared; }
                }
                return best;
            }

            private float NearestParameter(MorphSegment edge, Vector2 local, out float nearest)
            {
                int steps = edge.bezier ? 32 : 1;
                Vector2 previous = LocalPixel(OnEdge(edge, 0));
                float best = 0;
                nearest = float.PositiveInfinity;
                for (int i = 1; i <= steps; i++)
                {
                    Vector2 next = LocalPixel(OnEdge(edge, (float)i / steps));
                    Vector2 axis = next - previous;
                    float u = Mathf.Clamp01(Vector2.Dot(local - previous, axis) / Mathf.Max(axis.sqrMagnitude, 1e-8f));
                    float d = (local - previous - u * axis).sqrMagnitude;
                    if (d < nearest) { nearest = d; best = (i - 1 + u) / steps; }
                    previous = next;
                }
                return best;
            }

            private int HitSegmentBody(Vector2 local, out float parameter)
            {
                int best = -1;
                float nearest = 25;
                parameter = 0;
                for (int i = 0; i < (editor.transition?.segments.Count ?? 0); i++)
                {
                    float t = NearestParameter(editor.transition.segments[i], local, out float distance);
                    if (distance < nearest || distance == nearest && i == editor.selected)
                    { best = i; nearest = distance; parameter = t; }
                }
                return best;
            }

            private void MarkDown(Vector2 local, int button, int pointerId)
            {
                int index = HitSegmentBody(local, out float t);
                if (index < 0) return;
                Surface.Focus();
                var edge = editor.transition.segments[index];
                if (editor.pendingPane == null)
                {
                    float nearest = 36;
                    int hit = -1;
                    for (int i = 0; i < (edge.marks?.Count ?? 0); i++)
                    {
                        float at = isFrom ? edge.marks[i].x : edge.marks[i].y;
                        float d = (LocalPixel(OnEdge(edge, at)) - local).sqrMagnitude;
                        if (d < nearest) { nearest = d; hit = i; }
                    }
                    if (hit >= 0)
                    {
                        editor.selected = index;
                        editor.selectedMark = hit;
                        if (button == 1) editor.RemoveSelected();
                        else
                        {
                            editor.document.SetPlayback(false);
                            markDrag = hit;
                            markT = isFrom ? edge.marks[hit].x : edge.marks[hit].y;
                            pointer = pointerId;
                            Surface.CapturePointer(pointer);
                        }
                        editor.RefreshControls();
                        return;
                    }
                }
                if (button == 0) editor.PlaceMark(this, index, t);
            }

            private void MoveMark(Vector2 local)
            {
                var edge = editor.transition.segments[editor.selected];
                var marks = edge.marks;
                float At(int i) => isFrom ? marks[i].x : marks[i].y;
                float lo = markDrag == 0 ? 0 : At(markDrag - 1);
                float hi = markDrag + 1 == marks.Count ? 1 : At(markDrag + 1);
                float t = NearestParameter(edge, local, out _);
                if (t > lo && t < hi) markT = t;
                lines.MarkDirtyRepaint();
            }

            private void Down(PointerDownEvent evt)
            {
                Rect imageRect = ImageRect;
                if (pointer >= 0 || !editor.Writable || Texture == null || evt.button != 0 && evt.button != 1 ||
                    imageRect.width <= 0 || imageRect.height <= 0) return;
                Vector2 local = evt.localPosition;
                if (editor.MarkTool)
                {
                    MarkDown(local, evt.button, evt.pointerId);
                    evt.StopPropagation();
                    return;
                }
                int hit = Hit(local);
                bool tangent = false;
                if (editor.SegmentTool && editor.pendingPane == null && !evt.shiftKey && evt.button == 0 &&
                    editor.selected >= 0 && editor.transition?.segments[editor.selected].bezier == true)
                {
                    float nearest = hit >= 0 ? (local - LocalPixel(AnchorUv(hit))).sqrMagnitude : 64;
                    for (int side = 0; side < 2; side++)
                    {
                        int handle = 2 * editor.selected + side;
                        float distance = (local - LocalPixel(ControlUv(handle))).sqrMagnitude;
                        if (distance < nearest) { nearest = distance; hit = handle; tangent = true; }
                    }
                }
                bool completing = editor.pendingPane != null && editor.pendingPane != this;
                if (editor.SegmentTool && hit < 0 && !completing && !evt.shiftKey)
                {
                    int body = HitSegmentBody(local, out _);
                    if (body >= 0)
                    {
                        Surface.Focus();
                        editor.CancelGesture();
                        editor.selected = body;
                        if (evt.button == 1) editor.RemoveSelected();
                        editor.RefreshControls();
                        evt.StopPropagation();
                        return;
                    }
                }
                if (hit < 0 && (evt.button != 0 || !imageRect.Contains(local))) return;
                Surface.Focus();
                if (editor.SegmentTool && evt.button == 0 && (hit < 0 || completing || evt.shiftKey))
                {
                    editor.document.SetPlayback(false);
                    drawing = true;
                    strokeStart = dragUv = SnapEnd(local);
                    pointerStart = local;
                    pointer = evt.pointerId;
                    Surface.CapturePointer(pointer);
                    lines.MarkDirtyRepaint();
                }
                else if (hit < 0 || editor.pendingPane != null && editor.pendingPane != this && evt.button == 0)
                {
                    // A second endpoint may coincide with one of the existing controls.
                    editor.Place(this, HitUv(local));
                }
                else
                {
                    editor.document.SetPlayback(false);
                    editor.CancelGesture();
                    editor.selected = editor.SegmentTool ? hit / 2 : hit;
                    editor.lastEnd = (hit & 1) != 0;
                    if (evt.button == 1) editor.RemoveSelected();
                    else
                    {
                        dragged = hit;
                        tangentDrag = tangent;
                        originalUv = dragUv = tangent ? ControlUv(hit) : AnchorUv(hit);
                        pointerStart = local;
                        undoRecorded = false;
                        nextDragUpdate = 0;
                        dragOffset = dragUv - UvAt(local);
                        pointer = evt.pointerId;
                        Surface.CapturePointer(pointer);
                    }
                    editor.RefreshControls();
                }
                evt.StopPropagation();
            }

            private Vector2 SnapEnd(Vector2 local)
            {
                int hit = Hit(local);
                return hit >= 0 ? AnchorUv(hit) : HitUv(local);
            }

            private Vector2 HitUv(Vector2 local)
            {
                Vector2 uv = UvAt(local);
                return new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            }

            private Vector2 DragPosition(Vector2 local)
            {
                if (local.Equals(pointerStart)) return originalUv;
                Vector2 uv = UvAt(local) + dragOffset;
                return new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            }

            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer) return;
                if (markDrag >= 0) { MoveMark(evt.localPosition); evt.StopPropagation(); return; }
                if (drawing)
                {
                    dragUv = SnapEnd(evt.localPosition);
                    lines.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;
                }
                if (dragged < 0) return;
                dragUv = DragPosition(evt.localPosition);
                if (editor.SegmentTool) lines.MarkDirtyRepaint();
                else
                {
                    PositionMarker(markers[dragged], dragUv);
                    PositionMarker(pointRadius, dragUv);
                }
                PublishDrag();
                evt.StopPropagation();
            }

            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                if (markDrag >= 0)
                {
                    int index = editor.selected, mark = markDrag;
                    var edge = editor.transition.segments[index];
                    MoveMark(evt.localPosition);
                    Vector2 old = edge.marks[mark], next = isFrom ? new Vector2(markT,old.y) : new Vector2(old.x,markT);
                    CancelDrag();
                    if (!next.Equals(old))
                    {
                        editor.Edit("Move Morph Correspondence Mark", () => edge.marks[mark] = next);
                        editor.selected = index;
                        editor.selectedMark = mark;
                    }
                    editor.RefreshControls();
                    evt.StopPropagation();
                    return;
                }
                if (drawing)
                {
                    var end = SnapEnd(evt.localPosition);
                    CancelDrag();
                    if ((pointerStart - (Vector2)evt.localPosition).sqrMagnitude >= 9) editor.PlaceSegment(this, strokeStart, end);
                    lines.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;
                }
                if (dragged < 0) return;
                dragUv = DragPosition(evt.localPosition);
                PublishDrag(true);
                CancelDrag();
                editor.RefreshControls();
                evt.StopPropagation();
            }

            internal void PublishDrag(bool immediate = false)
            {
                if (dragged < 0 || !editor.Writable || (tangentDrag ? ControlUv(dragged) : AnchorUv(dragged)).Equals(dragUv)) return;
                bool end = (dragged & 1) != 0;
                if (editor.SegmentTool && !tangentDrag && !editor.transition.CanMove(editor.transition.segments[dragged / 2], end, isFrom, dragUv)) return;
                double now = EditorApplication.timeSinceStartup;
                if (!immediate && now < nextDragUpdate) return;
                nextDragUpdate = now + 1d / 20d;
                if (!undoRecorded)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.RegisterCompleteObjectUndo(editor.document, editor.SegmentTool ? "Move Morph Segment End" : "Move Morph Anchor");
                    undoRecorded = true;
                }
                // The canvas receives ordinary document changes. Only this pane keeps its capture.
                editor.publishingDrag = true;
                try
                {
                    if (editor.SegmentTool)
                    {
                        var line = editor.transition.segments[dragged / 2];
                        if (tangentDrag)
                        {
                            Vector2 offset = dragUv - AnchorUv(dragged);
                            editor.transition.SetTangent(line, end, isFrom, offset);
                        }
                        else
                        {
                            if (line.startNode == 0 || line.endNode == 0) editor.transition.BindNodes();
                            var node = editor.transition.nodes[(end ? line.endNode : line.startNode) - 1];
                            if (isFrom) node.from = dragUv;
                            else node.to = dragUv;
                        }
                    }
                    else
                    {
                        var point = editor.transition.anchors[dragged];
                        if (isFrom) point.from = dragUv;
                        else point.to = dragUv;
                    }
                    editor.document.MarkChanged();
                }
                finally { editor.publishingDrag = false; }
            }

            internal void RollbackDrag()
            {
                if (dragged < 0 || !undoRecorded) return;
                dragUv = originalUv;
                PublishDrag(true);
            }

            internal void CancelDrag()
            {
                drawing = tangentDrag = false;
                markDrag = -1;
                int id = pointer;
                dragged = pointer = -1;
                if (id >= 0 && Surface.HasPointerCapture(id)) Surface.ReleasePointer(id);
            }
        }
    }
}
