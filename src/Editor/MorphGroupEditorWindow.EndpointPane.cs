using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        private partial class PreviewPane : VisualElement
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
                frame = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
                frame.AddToClassList("whimtex-morph-frame");
                frame.generateVisualContent += WhimTexCheckerboard.DrawPreview;
                Surface.Add(frame);
                image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                image.StretchToParentSize();
                frame.Add(image);
                error = new Label { pickingMode = PickingMode.Ignore };
                error.AddToClassList("whimtex-morph-error");
                Surface.Add(error);
                SetVisible(error, false);
                AddNavigation();
                Surface.RegisterCallback<GeometryChangedEvent>(_ => FitFrame());
                RegisterCallback<DetachFromPanelEvent>(_ => Release());
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
                EndPan();
                image.image = null;
                if (Texture != null) ProfiledRenderTexture.ReleaseTemporary(Texture);
                Texture = null;
                failed = false;
                SetVisible(frame, false);
                SetVisible(error, false);
            }
        }

        private sealed partial class EndpointPane : PreviewPane
        {
            private readonly bool isFrom;
            private readonly List<Label> markers = new();
            private readonly VisualElement lines, pointRadius;
            protected override bool EditingImage => pointer >= 0;
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
                Surface.AddManipulator(new ContextualMenuManipulator(BuildGuideMenu));
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
                caption.text = layer == null ? (isFrom ? "A" : "B") : $"{(isFrom ? "A" : "B")} · {layer.layerName}";
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

            private Vector2 AnchorUv(int index, bool edge)
            {
                if (edge)
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
                while (markers.Count < count)
                {
                    var label = new Label { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("whimtex-morph-marker");
                    frame.Insert(frame.IndexOf(pointRadius), label);
                    markers.Add(label);
                }
                for (int i = 0; i < markers.Count; i++)
                {
                    var marker = markers[i];
                    SetVisible(marker, i < count);
                    if (i >= count) continue;
                    Vector2 uv = editor.selection == GuideSelection.Point && i == dragged ? dragUv :
                        isFrom ? editor.transition.anchors[i].from : editor.transition.anchors[i].to;
                    marker.text = (i + 1).ToString();
                    marker.EnableInClassList("whimtex-morph-marker--selected", editor.selection == GuideSelection.Point && i == editor.selected);
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

            private Vector2 ControlUv(int index) => AnchorUv(index, true) +
                editor.transition.segments[index / 2].Offset(isFrom, (index & 1) != 0);

            private void DrawSegments(MeshGenerationContext ctx)
            {
                if (!editor.IsRaster && editor.firstKnots.Count == 0 && !drawing) return;
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
                    if (selected && edge.radius > 0 && !editor.EditingMark)
                    {
                        painter.lineCap = LineCap.Round;
                        painter.lineWidth = 2 * edge.radius * shortAxis;
                        painter.strokeColor = influence;
                        painter.Stroke();
                        painter.lineCap = LineCap.Butt;
                    }
                    bool boundary = edge?.boundary == true;
                    bool foreground = edge?.foreground == true && !editor.morph.classicLiao;
                    painter.lineWidth = foreground ? 6 : boundary ? 4 : 3;
                    painter.strokeColor = foreground ? new Color(.2f, .95f, .45f) : boundary ? new Color(1f, .4f, .1f) : Color.black;
                    painter.Stroke();
                    if (foreground && boundary)
                    {
                        painter.lineWidth = 4;
                        painter.strokeColor = new Color(1f, .4f, .1f);
                        painter.Stroke();
                    }
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
                var segments = editor.IsRaster ? editor.transition?.segments : null;
                for (int i = 0; i < (segments?.Count ?? 0); i++)
                {
                    var line = segments[i];
                    Vector2 start = AnchorPosition(false), end = AnchorPosition(true);
                    Vector2 AnchorPosition(bool last) => MorphSegment.Side(editor.transition.Position(line, last), isFrom);
                    if (editor.EdgeSelected && !tangentDrag && dragged == i * 2) start = dragUv;
                    if (editor.EdgeSelected && !tangentDrag && dragged == i * 2 + 1) end = dragUv;
                    bool selected = editor.EdgeSelected && editor.selected == i;
                    Draw(start, end, selected ? Color.yellow : Color.cyan, true, line, selected);
                    if (selected && line.bezier && !editor.EditingMark)
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
                        Vector2 at = Pixel(OnEdge(line, t));
                        Vector2 along = Pixel(OnEdge(line, Mathf.Min(1, t + .002f))) - Pixel(OnEdge(line, Mathf.Max(0, t - .002f)));
                        var across = new Vector2(-along.y, along.x).normalized * 5;
                        painter.BeginPath(); painter.MoveTo(at - across); painter.LineTo(at + across);
                        painter.lineWidth = 4; painter.strokeColor = Color.black; painter.Stroke();
                        painter.lineWidth = 2; painter.strokeColor = selected && editor.selectedMark == m ? Color.yellow : Color.magenta; painter.Stroke();
                    }
                    if (editor.EdgeSelected && editor.EditingMark && editor.pendingPane == this && i == editor.pendingEdge)
                        Dot(Pixel(OnEdge(line, editor.pendingParameter)), Color.white);
                }
                DrawPaths(painter, rect);
                bool first = editor.pendingPane == this;
                var knots = first ? editor.firstKnots : editor.matchedKnots;
                for (int i = 0; i < knots.Count; i++)
                {
                    var knot = knots[i];
                    if (i > 0) DraftEdge(knots[i - 1], knot);
                    if (i == 1)
                    {
                        var start = knots[0];
                        Vector2 p = Pixel(start.uv), c = Pixel(start.uv + start.tangent);
                        Vector2 q = Pixel(knot.uv), d = Pixel(knot.uv - knot.tangent);
                        Vector2 mid = (p + 3 * c + 3 * d + q) / 8;
                        Vector2 along = (q + d - c - p).normalized;
                        Vector2 cross = new Vector2(-along.y, along.x) * 3;
                        painter.BeginPath();
                        painter.MoveTo(mid - 5 * along + cross); painter.LineTo(mid); painter.LineTo(mid - 5 * along - cross);
                        painter.lineWidth = 2; painter.strokeColor = Color.white; painter.Stroke();
                    }
                    Dot(Pixel(knot.uv), i == 0 ? Color.white : Color.yellow);
                    if (i == 0)
                    {
                        painter.BeginPath(); painter.Arc(Pixel(knot.uv), 6, 0, 360);
                        painter.lineWidth = 2; painter.strokeColor = Color.yellow; painter.Stroke();
                    }
                }
                if (editor.closedDraft && knots.Count >= 3) DraftEdge(knots[^1], knots[0]);

                if (drawing)
                {
                    var knot = new GuideKnot(strokeStart, dragUv - strokeStart);
                    if (knots.Count > 0) DraftEdge(knots[^1], knot);
                    Dot(Pixel(strokeStart), Color.yellow);
                    if (knot.tangent != Vector2.zero)
                    {
                        painter.BeginPath(); painter.MoveTo(Pixel(strokeStart - knot.tangent)); painter.LineTo(Pixel(dragUv));
                        painter.lineWidth = 1; painter.strokeColor = Color.white; painter.Stroke();
                        Dot(Pixel(dragUv), Color.white);
                    }
                }

                void DraftEdge(GuideKnot start, GuideKnot end)
                {
                    painter.BeginPath();
                    painter.MoveTo(Pixel(start.uv));
                    painter.BezierCurveTo(Pixel(start.uv + start.tangent), Pixel(end.uv - end.tangent), Pixel(end.uv));
                    painter.lineWidth = 2; painter.strokeColor = Color.yellow; painter.Stroke();
                }
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

            private int Hit(Vector2 local, bool edge)
            {
                Rect imageRect = ImageRect;
                int best = -1;
                float distance = 10f * 10f;
                int count = edge ? (editor.IsRaster ? 2 * (editor.transition?.segments.Count ?? 0) : 0) : editor.transition?.anchors.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    float squared = (local - PixelAt(AnchorUv(i, edge), imageRect)).sqrMagnitude;
                    int guide = edge ? i / 2 : i;
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

            private int HitMark(int index, Vector2 local)
            {
                if (index < 0) return -1;
                var edge = editor.transition.segments[index];
                float nearest = 36;
                int best = -1;
                for (int i = 0; i < (edge.marks?.Count ?? 0); i++)
                {
                    float t = isFrom ? edge.marks[i].x : edge.marks[i].y;
                    float d = (LocalPixel(OnEdge(edge, t)) - local).sqrMagnitude;
                    if (d < nearest) { nearest = d; best = i; }
                }
                return best;
            }

            private void BuildGuideMenu(ContextualMenuPopulateEvent evt)
            {
                if (!editor.Writable || Texture == null || editor.pendingPane != null) return;
                Vector2 local = Surface.WorldToLocal(evt.mousePosition);
                if (BuildPathMenu(evt, local)) return;
                int handle = Hit(local, true);
                int index = editor.IsRaster ? HitSegmentBody(local, out _) : -1;
                int point = Hit(local, false);
                if (handle >= 0) index = handle / 2;
                else if (point >= 0) index = -1;
                if (index < 0 && point < 0) return;
                int mark = HitMark(index, local);
                var pair = editor.transition;
                editor.selection = index >= 0 ? GuideSelection.Segment : GuideSelection.Point;
                editor.selected = index >= 0 ? index : point;
                editor.selectedMark = mark;
                editor.lastEnd = handle >= 0 && (handle & 1) != 0;
                editor.RefreshControls();
                int chosen = editor.selected;
                bool Current() => editor.Writable && editor.transition == pair && editor.selected == chosen &&
                    editor.selection == (index >= 0 ? GuideSelection.Segment : GuideSelection.Point) && editor.pendingPane == null;
                if (index >= 0)
                {
                    bool end = editor.lastEnd;
                    if (handle >= 0 && pair.CanContinueGuide(index, end))
                        evt.menu.AppendAction("Continue Guide", _ => { if (Current()) editor.ContinueGuide(this, index, end); });
                    float t = NearestParameter(pair.segments[index], local, out _);
                    if (mark < 0 && t > 0 && t < 1)
                        evt.menu.AppendAction("Add Match Mark", _ => { if (Current()) editor.PlaceMark(this, index, t); });
                }
                evt.menu.AppendAction(mark >= 0 ? "Remove Mark" : index >= 0 ? "Remove Segment" : "Remove Pair",
                    _ => { if (Current() && editor.selectedMark == mark) editor.RemoveSelected(); });
            }

            private void MoveMark(Vector2 local)
            {
                var marks = editor.SelectedMarks;
                float At(int i) => isFrom ? marks[i].x : marks[i].y;
                float lo = markDrag == 0 ? 0 : At(markDrag - 1);
                float hi = markDrag + 1 == marks.Count ? 1 : At(markDrag + 1);
                float t = editor.PathSelected ? ProjectPath(editor.SelectedPath, local).fraction :
                    NearestParameter(editor.SelectedEdge, local, out _);
                if (t > lo && t < hi) markT = t;
                lines.MarkDirtyRepaint();
            }

            private void Down(PointerDownEvent evt)
            {
                Rect imageRect = ImageRect;
                if (pointer >= 0 || editor.modelDirty || Texture == null || evt.button != 0 ||
                    imageRect.width <= 0 || imageRect.height <= 0) return;
                Vector2 local = evt.localPosition;
                bool writable = editor.Writable;
                if (editor.pendingEdge >= 0)
                {
                    if (writable && editor.pendingPane != this)
                    {
                        if (editor.PathSelected)
                        {
                            var projection = ProjectPath(editor.SelectedPath, local);
                            if (projection.distance <= 36) editor.PlaceMark(this, editor.pendingEdge, projection.fraction, true);
                        }
                        else
                        {
                            var edge = editor.transition.segments[editor.pendingEdge];
                            float t = NearestParameter(edge, local, out float distance);
                            if (distance <= 36) editor.PlaceMark(this, editor.pendingEdge, t);
                        }
                    }

                    evt.StopPropagation();
                    return;
                }
                if (editor.pendingPane == null && SelectPath(evt, local, writable)) return;
                int node = Hit(local, true);
                int point = Hit(local, false);
                float parameter = 0;
                int body = editor.IsRaster ? HitSegmentBody(local, out parameter) : -1;
                int mark = HitMark(body, local);
                bool edgeHandle = node >= 0 && (point < 0 ||
                    (local - LocalPixel(AnchorUv(node, true))).sqrMagnitude <= (local - LocalPixel(AnchorUv(point, false))).sqrMagnitude);
                int hit = edgeHandle ? node : point;
                bool tangent = false;
                if (editor.pendingPane == null && editor.SelectedEdge?.bezier == true)
                {
                    float nearest = hit >= 0 ? (local - LocalPixel(AnchorUv(hit, edgeHandle))).sqrMagnitude : 64;
                    for (int side = 0; side < 2; side++)
                    {
                        int handle = 2 * editor.selected + side;
                        float distance = (local - LocalPixel(ControlUv(handle))).sqrMagnitude;
                        if (distance < nearest) { nearest = distance; hit = handle; tangent = edgeHandle = true; }
                    }
                }
                if (hit < 0 && body < 0 && !imageRect.Contains(local)) return;
                Surface.Focus();
                if (writable) editor.document.SetPlayback(false);
                if (editor.pendingPane != null || hit < 0 && body < 0)
                {
                    if (!writable || editor.extendingPath >= 0 && editor.pendingPane != this || editor.pendingPane == this && (editor.closedDraft || editor.matchingKnots)) return;
                    drawing = true;
                    strokeStart = dragUv = hit >= 0 && editor.pendingPane != null ? AnchorUv(hit, edgeHandle) : HitUv(local);
                    pointerStart = local;
                    pointer = evt.pointerId;
                    Surface.CapturePointer(pointer);
                    lines.MarkDirtyRepaint();
                }
                else
                {
                    editor.CancelGesture();
                    if (mark >= 0 && !tangent && !edgeHandle && evt.clickCount != 2)
                    {
                        editor.selection = GuideSelection.Segment;
                        editor.selected = body;
                        editor.selectedMark = mark;
                        if (writable)
                        {
                            markDrag = mark;
                            var at = editor.transition.segments[body].marks[mark];
                            markT = isFrom ? at.x : at.y;
                            pointer = evt.pointerId;
                            Surface.CapturePointer(pointer);
                        }
                    }
                    else if (hit < 0)
                    {
                        editor.selection = GuideSelection.Segment;
                        editor.selected = body;
                        var imageSize = new Vector2(editor.document.width, editor.document.height);
                        if (writable && evt.clickCount == 2 && editor.transition.CanSplitGuide(body, isFrom, parameter, imageSize))
                        {
                            var pair = editor.transition;
                            editor.Edit("Insert Morph Guide Vertex", () => pair.SplitGuide(body, isFrom, parameter, imageSize));
                            editor.selected = body;
                            editor.lastEnd = true;
                        }
                    }
                    else
                    {
                        editor.selection = edgeHandle ? GuideSelection.Segment : GuideSelection.Point;
                        editor.selected = edgeHandle ? hit / 2 : hit;
                        editor.lastEnd = edgeHandle && (hit & 1) != 0;
                        if (writable)
                        {
                            dragged = hit;
                            tangentDrag = tangent;
                            originalUv = dragUv = tangent ? ControlUv(hit) : AnchorUv(hit, edgeHandle);
                            pointerStart = local;
                            undoRecorded = false;
                            nextDragUpdate = 0;
                            dragOffset = dragUv - UvAt(local);
                            pointer = evt.pointerId;
                            Surface.CapturePointer(pointer);
                        }
                    }
                    // Showing the selected guide's fields can move both images. Keep their
                    // coordinate systems stationary until the captured gesture finishes.
                    if (pointer < 0) editor.RefreshControls();
                    else { editor.a.RefreshMarkers(); editor.b.RefreshMarkers(); }
                }
                evt.StopPropagation();
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
                if (editor.PathSelected && pathTangent != 0) return uv;
                return new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            }

            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer) return;
                if (markDrag >= 0) { MoveMark(evt.localPosition); evt.StopPropagation(); return; }
                if (drawing)
                {
                    dragUv = strokeStart + (UvAt(evt.localPosition) - UvAt(pointerStart));
                    lines.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;
                }
                if (dragged < 0) return;
                dragUv = DragPosition(evt.localPosition);
                if (editor.EdgeSelected || editor.PathSelected) lines.MarkDirtyRepaint();
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
                    var marks = editor.SelectedMarks;
                    MoveMark(evt.localPosition);
                    Vector2 old = marks[mark], next = isFrom ? new Vector2(markT,old.y) : new Vector2(old.x,markT);
                    CancelDrag();
                    if (!next.Equals(old))
                    {
                        editor.Edit("Move Morph Correspondence Mark", () => marks[mark] = next);
                        editor.selected = index;
                        editor.selectedMark = mark;
                    }
                    editor.RefreshControls();
                    evt.StopPropagation();
                    return;
                }
                if (drawing)
                {
                    Vector2 at = strokeStart;
                    Vector2 tangent = editor.IsRaster && (pointerStart - (Vector2)evt.localPosition).sqrMagnitude >= 9
                        ? UvAt(evt.localPosition) - UvAt(pointerStart) : Vector2.zero;
                    var knots = this == editor.pendingPane ? editor.firstKnots : editor.matchedKnots;
                    bool close = editor.extendingPath < 0 && knots.Count >= 3 &&
                        (pointerStart - LocalPixel(knots[0].uv)).sqrMagnitude <= 36;
                    CancelDrag();
                    editor.PlaceKnot(this, at, tangent, close);
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
                if (dragged < 0 || !editor.Writable) return;
                Vector2 current = editor.PathSelected ? PathDragPosition() :
                    tangentDrag ? ControlUv(dragged) : AnchorUv(dragged, editor.EdgeSelected);
                if (current.Equals(dragUv) || editor.PathSelected && !CanMovePathKnot()) return;
                bool end = (dragged & 1) != 0;
                if (editor.EdgeSelected && !tangentDrag && !editor.transition.CanMove(editor.transition.segments[dragged / 2], end, isFrom, dragUv)) return;
                double now = EditorApplication.timeSinceStartup;
                if (!immediate && now < nextDragUpdate) return;
                nextDragUpdate = now + 1d / 20d;
                if (!undoRecorded)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.RegisterCompleteObjectUndo(editor.document, editor.PathSelected ? "Move Morph Guide Vertex" : editor.EdgeSelected ? "Move Morph Segment End" : "Move Morph Anchor");
                    undoRecorded = true;
                }
                // The canvas receives ordinary document changes. Only this pane keeps its capture.
                editor.publishingDrag = true;
                try
                {
                    if (editor.PathSelected) SetPathPosition();
                    else if (editor.EdgeSelected)
                    {
                        var line = editor.transition.segments[dragged / 2];
                        if (tangentDrag)
                        {
                            Vector2 offset = dragUv - AnchorUv(dragged, editor.EdgeSelected);
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
