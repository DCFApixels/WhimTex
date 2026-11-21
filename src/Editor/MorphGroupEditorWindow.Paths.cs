using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        private sealed partial class EndpointPane
        {
            private int pathTangent;
            private Vector2 originalIn, originalOut;

            private struct PathHit
            {
                internal int index, knot, tangent, mark, span;
                internal float t, fraction, distance;
            }

            private float MarkFraction(Vector2 pair) => isFrom ? pair.x : pair.y;

            private PathHit ProjectPath(MorphGuidePath path, Vector2 local)
            {
                var hit = new PathHit { index = -1, knot = -1, mark = -1, span = -1, distance = float.PositiveInfinity };
                var knots = path.Side(isFrom);
                Rect rect = ImageRect;
                for (int span = 0; span < knots.Count - (path.closed ? 0 : 1); span++)
                {
                    var start = knots[span];
                    // Use the same parameterization as Evaluate, including zero-length Bézier handles.
                    Vector2 previous = PixelAt(start.position, rect);
                    const int steps = 32;
                    for (int i = 1; i <= steps; i++)
                    {
                        Vector2 next = PixelAt(path.Evaluate(isFrom, span, (float)i / steps), rect);
                        Vector2 axis = next - previous;
                        float u = Mathf.Clamp01(Vector2.Dot(local - previous, axis) / Mathf.Max(axis.sqrMagnitude, 1e-10f));
                        float distance = (local - previous - axis * u).sqrMagnitude;
                        if (distance < hit.distance)
                        {
                            hit.span = span;
                            hit.t = (i - 1 + u) / steps;
                            hit.distance = distance;
                        }
                        previous = next;
                    }
                }
                if (hit.span >= 0) hit.fraction = path.LengthAt(isFrom, hit.span, hit.t, editor.CanvasSize);
                return hit;
            }

            private PathHit HitPath(Vector2 local)
            {
                var best = new PathHit { index = -1, distance = float.PositiveInfinity };
                if (!editor.IsRaster || editor.transition == null) return best;
                var guides = editor.transition.guides;
                for (int i = 0; i < guides.Count; i++)
                {
                    var path = guides[i];
                    var candidate = ProjectPath(path, local);
                    var knots = path.Side(isFrom);
                    float nearest = 64;
                    for (int k = 0; k < knots.Count; k++)
                    {
                        var knot = knots[k];
                        Consider(knot.position, k, 0);
                        if (!editor.PathSelected || editor.selected != i || editor.EditingMark) continue;
                        if ((k > 0 || path.closed) && knot.inTangent != Vector2.zero) Consider(knot.position + knot.inTangent, k, -1);
                        if ((k + 1 < knots.Count || path.closed) && knot.outTangent != Vector2.zero) Consider(knot.position + knot.outTangent, k, 1);
                    }
                    for (int m = 0; m < path.marks.Count; m++)
                    {
                        var uv = path.AtLength(isFrom, MarkFraction(path.marks[m]), editor.CanvasSize);
                        float d = (LocalPixel(uv) - local).sqrMagnitude;
                        if (d <= Mathf.Min(36, nearest))
                        {
                            nearest = d;
                            candidate.knot = -1;
                            candidate.mark = m;
                        }
                    }
                    if (candidate.knot >= 0 || candidate.mark >= 0) candidate.distance = nearest;
                    else if (candidate.distance > 25) continue;
                    if (candidate.distance < best.distance || candidate.distance == best.distance && editor.PathSelected && editor.selected == i)
                    {
                        best = candidate;
                        best.index = i;
                    }

                    void Consider(Vector2 uv, int k, int tangent)
                    {
                        float d = (LocalPixel(uv) - local).sqrMagnitude;
                        if (d >= nearest) return;
                        nearest = d;
                        candidate.knot = k;
                        candidate.tangent = tangent;
                    }
                }
                return best;
            }

            private bool SelectPath(PointerDownEvent evt, Vector2 local, bool writable)
            {
                var hit = HitPath(local);
                if (hit.index < 0) return false;
                Surface.Focus();
                if (writable) editor.document.SetPlayback(false);
                editor.CancelGesture();
                editor.selection = GuideSelection.Path;
                editor.selected = hit.index;
                editor.selectedMark = hit.mark;
                var path = editor.SelectedPath;
                if (writable && hit.mark >= 0)
                {
                    markDrag = hit.mark;
                    markT = MarkFraction(path.marks[hit.mark]);
                    pointer = evt.pointerId;
                }
                else if (writable && hit.knot >= 0)
                {
                    dragged = hit.knot;
                    pathTangent = hit.tangent;
                    var knot = path.Side(isFrom)[dragged];
                    originalIn = knot.inTangent;
                    originalOut = knot.outTangent;
                    originalUv = dragUv = PathDragPosition();
                    pointerStart = local;
                    dragOffset = dragUv - UvAt(local);
                    undoRecorded = false;
                    nextDragUpdate = 0;
                    pointer = evt.pointerId;
                }
                else if (writable && evt.clickCount == 2 && hit.span >= 0 && hit.t > 0 && hit.t < 1)
                    editor.EditSelectedGuide("Insert Morph Guide Vertex", () => path.Split(isFrom, hit.span, hit.t));
                if (pointer >= 0)
                {
                    Surface.CapturePointer(pointer);
                    editor.a.RefreshMarkers();
                    editor.b.RefreshMarkers();
                }
                else editor.RefreshControls();
                evt.StopPropagation();
                return true;
            }

            private bool BuildPathMenu(ContextualMenuPopulateEvent evt, Vector2 local)
            {
                var hit = HitPath(local);
                if (hit.index < 0) return false;
                editor.selection = GuideSelection.Path;
                editor.selected = hit.index;
                editor.selectedMark = hit.mark;
                editor.RefreshControls();
                var path = editor.SelectedPath;
                bool Current() => editor.Writable && editor.SelectedPath == path && editor.pendingPane == null;
                var side = path.Side(isFrom);
                if (!path.closed && hit.knot >= 0 && hit.tangent == 0 && (hit.knot == 0 || hit.knot == side.Count - 1))
                    evt.menu.AppendAction("Continue Guide", _ => { if (Current()) editor.BeginExtension(this, hit.index, hit.knot == 0); });
                if (hit.knot >= 0 && hit.tangent == 0)
                {
                    bool smooth = !side[hit.knot].smooth;
                    evt.menu.AppendAction(smooth ? "Make Smooth" : "Make Corner", _ =>
                    {
                        if (Current()) editor.EditSelectedGuide("Change Morph Vertex Tangents",
                            () => SetKnotShape(path, hit.knot, smooth));
                    });
                }
                if (hit.mark < 0 && hit.fraction > 0 && hit.fraction < 1)
                    evt.menu.AppendAction("Add Match Mark", _ => { if (Current()) editor.PlaceMark(this, hit.index, hit.fraction, true); });
                if (path.closed)
                    evt.menu.AppendAction("Reverse Direction", _ =>
                    {
                        if (!Current()) return;
                        editor.EditSelectedGuide("Reverse Morph Contour", () =>
                        {
                            side.Reverse(1, side.Count - 1);
                            foreach (var knot in side) (knot.inTangent, knot.outTangent) = (knot.outTangent, knot.inTangent);
                            RemapSideMarks(path.marks, isFrom, 1, 0);
                        });
                    });
                evt.menu.AppendAction(hit.mark >= 0 ? "Remove Mark" : "Remove Guide",
                    _ => { if (Current() && editor.selectedMark == hit.mark) editor.RemoveSelected(); });
                return true;
            }

            private void SetKnotShape(MorphGuidePath path, int index, bool smooth)
            {
                var side = path.Side(isFrom);
                var knot = side[index];
                knot.smooth = smooth;
                knot.inTangent = knot.outTangent = Vector2.zero;
                if (!smooth) return;
                Vector2 size = editor.CanvasSize;
                Vector2 before = index > 0 || path.closed
                    ? Vector2.Scale(knot.position - side[(index + side.Count - 1) % side.Count].position, size) : Vector2.zero;
                Vector2 after = index + 1 < side.Count || path.closed
                    ? Vector2.Scale(side[(index + 1) % side.Count].position - knot.position, size) : Vector2.zero;
                Vector2 axis = (before.normalized + after.normalized).normalized;
                if (axis == Vector2.zero) axis = after.normalized;
                Vector2 direction = axis / size;
                knot.inTangent = -direction * (before.magnitude / 3);
                knot.outTangent = direction * (after.magnitude / 3);
            }

            private Vector2 PathDragPosition()
            {
                var knot = editor.SelectedPath.Side(isFrom)[dragged];
                return knot.position + (pathTangent == 0 ? Vector2.zero : pathTangent < 0 ? knot.inTangent : knot.outTangent);
            }

            private bool CanMovePathKnot()
            {
                if (pathTangent != 0) return true;
                var path = editor.SelectedPath;
                var side = path.Side(isFrom);
                return (!(dragged > 0 || path.closed) || side[(dragged + side.Count - 1) % side.Count].position != dragUv) &&
                    (!(dragged + 1 < side.Count || path.closed) || side[(dragged + 1) % side.Count].position != dragUv);
            }

            private void SetPathPosition()
            {
                var knot = editor.SelectedPath.Side(isFrom)[dragged];
                Vector2 offset = dragUv - knot.position;
                if (pathTangent == 0) knot.position = dragUv;
                else if (dragUv.Equals(originalUv)) { knot.inTangent = originalIn; knot.outTangent = originalOut; }
                else if (pathTangent < 0)
                {
                    knot.inTangent = offset;
                    if (knot.smooth) knot.outTangent = -offset.normalized * originalOut.magnitude;
                }
                else
                {
                    knot.outTangent = offset;
                    if (knot.smooth) knot.inTangent = -offset.normalized * originalIn.magnitude;
                }
            }

            private void DrawPaths(Painter2D painter, Rect rect)
            {
                if (!editor.IsRaster || editor.transition == null) return;
                var guides = editor.transition.guides;
                Vector2 Pixel(Vector2 at) => PixelAt(at, rect);
                for (int i = 0; i < guides.Count; i++)
                {
                    var path = guides[i];
                    var side = path.Side(isFrom);
                    if (side.Count < 2) continue;
                    bool selected = editor.PathSelected && editor.selected == i;
                    painter.BeginPath();
                    painter.MoveTo(Pixel(side[0].position));
                    for (int s = 0; s < side.Count - (path.closed ? 0 : 1); s++)
                    {
                        var a = side[s];
                        var b = side[(s + 1) % side.Count];
                        painter.BezierCurveTo(Pixel(a.position + a.outTangent), Pixel(b.position + b.inTangent), Pixel(b.position));
                    }
                    if (selected && !editor.EditingMark && path.radius > 0)
                        Stroke(2 * path.radius * Mathf.Min(rect.width, rect.height), new Color(1, .85f, .2f, .23f));
                    if (path.foreground && !editor.morph.classicLiao) Stroke(6, new Color(.2f,.95f,.45f));
                    Stroke(path.boundary ? 4 : 3, path.boundary ? new Color(1,.4f,.1f) : Color.black);
                    var color = selected ? Color.yellow : Color.cyan;
                    Stroke(1.5f, color);
                    for (int k = 0; k < side.Count; k++)
                    {
                        var knot = side[k];
                        var p = Pixel(knot.position);
                        if (selected && !editor.EditingMark)
                        {
                            if (k > 0 || path.closed) Handle(knot.inTangent);
                            if (k + 1 < side.Count || path.closed) Handle(knot.outTangent);
                        }
                        Dot(p, k == 0 && path.closed ? Color.white : color);
                        void Handle(Vector2 offset)
                        {
                            if (offset == Vector2.zero) return;
                            var tip = Pixel(knot.position + offset);
                            painter.BeginPath(); painter.MoveTo(p); painter.LineTo(tip); Stroke(1, Color.white); Dot(tip, Color.white);
                        }
                    }
                    for (int m = 0; m < path.marks.Count; m++)
                    {
                        float t = selected && markDrag == m ? markT : MarkFraction(path.marks[m]);
                        var center = Pixel(path.AtLength(isFrom, t, editor.CanvasSize));
                        var axis = Pixel(path.AtLength(isFrom, Mathf.Min(1,t+.002f), editor.CanvasSize)) -
                            Pixel(path.AtLength(isFrom, Mathf.Max(0,t-.002f), editor.CanvasSize));
                        var normal = new Vector2(-axis.y, axis.x).normalized * 5;
                        painter.BeginPath(); painter.MoveTo(center - normal); painter.LineTo(center + normal);
                        Stroke(4, Color.black); Stroke(2, selected && editor.selectedMark == m ? Color.yellow : Color.magenta);
                    }
                    if (selected && editor.pendingPane == this && editor.pendingEdge == i)
                        Dot(Pixel(path.AtLength(isFrom, editor.pendingParameter, editor.CanvasSize)), Color.white);
                }

                void Stroke(float width, Color color) { painter.lineWidth = width; painter.strokeColor = color; painter.Stroke(); }
                void Dot(Vector2 p, Color color)
                {
                    painter.BeginPath(); painter.Arc(p, 3, 0, 360); painter.fillColor = color; painter.Fill(); Stroke(1, Color.black);
                }
            }
        }
    }
}
