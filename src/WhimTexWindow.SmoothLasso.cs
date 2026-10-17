using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed partial class AreaSelectionManipulator
        {
            readonly List<bool> polygonCorners = new List<bool>();
            readonly List<Vector2> polygonCurve = new List<Vector2>();
            readonly List<int> polygonCurveSegments = new List<int>();
            bool polygonCurveDirty = true;
            int polygonCurveNodeCount = -1;
            string polygonCurveError;
            internal int SmoothPolygonPreviewCount { get; private set; }
            internal int SelectedPolygonPoint { get; private set; } = -1;
            internal bool HasSelectedPolygonPoint => SelectedPolygonPoint >= 0 && SelectedPolygonPoint < Vertices.Count;
            internal bool SelectedPointIsCorner => HasSelectedPolygonPoint && IsPolygonCorner(SelectedPolygonPoint);
            internal bool IsPolygonCorner(int index) => index >= 0 && index < polygonCorners.Count && polygonCorners[index];

            void InvalidatePolygonCurve() => polygonCurveDirty = true;

            internal void RefreshPolygonCurve()
            {
                InvalidatePolygonCurve();
                if (owner.activeDocument != null && Vertices.Count > 0) SetCurrent(pointerPosition, pointerControl);
                owner.areaSelectionOverlay?.MarkDirtyRepaint();
                owner.RefreshCanvasPointerCursor();
            }

            void SyncPolygonCorners()
            {
                while (polygonCorners.Count < Vertices.Count) polygonCorners.Add(false);
                if (polygonCorners.Count > Vertices.Count) polygonCorners.RemoveRange(Vertices.Count, polygonCorners.Count - Vertices.Count);
            }

            void SelectPolygonPoint(int index)
            {
                SelectedPolygonPoint = index;
                owner.toolkitHeaderBindings.Refresh();
            }

            void InsertPolygonPoint(int index, Vector2 point)
            {
                SyncPolygonCorners();
                Vertices.Insert(index, point);
                polygonCorners.Insert(index, false);
                InvalidatePolygonCurve();
                SelectPolygonPoint(index);
            }

            void RemovePolygonPoint(int index)
            {
                SyncPolygonCorners();
                Vertices.RemoveAt(index);
                polygonCorners.RemoveAt(index);
                int selected = SelectedPolygonPoint > index ? SelectedPolygonPoint - 1 : SelectedPolygonPoint;
                InvalidatePolygonCurve();
                SelectPolygonPoint(Math.Min(selected, Vertices.Count - 1));
            }

            internal void SetSelectedPointCorner(bool corner)
            {
                if (!owner.IsSmoothLasso || !HasSelectedPolygonPoint) return;
                SyncPolygonCorners();
                polygonCorners[SelectedPolygonPoint] = corner;
                RefreshPolygonCurve();
                owner.toolkitHeaderBindings.Refresh();
            }

            internal void TogglePolygonCorner()
            {
                if (!owner.IsSmoothLasso || Vertices.Count == 0) return;
                SelectPolygonPoint(HoverVertex >= 0 ? HoverVertex : HasSelectedPolygonPoint ? SelectedPolygonPoint : Vertices.Count - 1);
                SetSelectedPointCorner(!SelectedPointIsCorner);
            }

            void ResetPolygonCurve()
            {
                polygonCorners.Clear(); polygonCurve.Clear(); polygonCurveSegments.Clear();
                SmoothPolygonPreviewCount = 0;
                SelectedPolygonPoint = -1;
                polygonCurveError = null; polygonCurveNodeCount = -1; polygonCurveDirty = true;
            }

            internal bool TryGetSmoothPolygon(out IReadOnlyList<Vector2> outline)
            {
                if (polygonCurveDirty || polygonCurveNodeCount != Vertices.Count)
                {
                    polygonCurveDirty = false; polygonCurveNodeCount = Vertices.Count;
                    polygonCurveError = null;
                    SmoothPolygonPreviewCount = 0;
                    try
                    {
                        SmoothLassoContour.Build(Vertices, polygonCorners, polygonCurve, polygonCurveSegments);
                        SmoothPolygonPreviewCount = polygonCurve.Count;
                        if (Vertices.Count >= 3)
                            for (int i = 0; i < polygonCurveSegments.Count; i++)
                                if (polygonCurveSegments[i] == Vertices.Count - 1)
                                {
                                    SmoothPolygonPreviewCount = i + 1;
                                    break;
                                }
                    }
                    catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
                    {
                        polygonCurve.Clear(); polygonCurveSegments.Clear(); polygonCurveError = error.Message;
                    }
                }
                outline = polygonCurve;
                return polygonCurveError == null;
            }

            int HitSmoothSegment(Vector2 point, float scale, out Vector2 projected)
            {
                projected = point;
                if (!TryGetSmoothPolygon(out IReadOnlyList<Vector2> outline)) return -1;
                int segment = -1;
                float nearest = SegmentHitRadius * SegmentHitRadius;
                for (int i = 1; i < SmoothPolygonPreviewCount; i++)
                {
                    Vector2 a = outline[i - 1], delta = outline[i] - a;
                    float length = delta.sqrMagnitude;
                    if (length < 1e-12f) continue;
                    Vector2 p = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / length);
                    float distance = (point - p).sqrMagnitude * scale * scale;
                    if (distance > nearest) continue;
                    nearest = distance; projected = p; segment = polygonCurveSegments[i - 1];
                }
                return segment;
            }
        }
    }

    internal static class SmoothLassoContour
    {
        internal const int MaximumPoints = 16384;
        const double ToleranceSquared = .0625 * .0625;

        internal static void Build(IReadOnlyList<Vector2> nodes, IReadOnlyList<bool> corners, List<Vector2> outline, List<int> segments)
        {
            outline.Clear(); segments.Clear();
            int count = nodes.Count;
            if (count > 256) throw new InvalidOperationException("Polygonal Lasso supports at most 256 points.");
            for (int i = 0; i < count; i++)
                if (!VectorGeometry.Finite(nodes[i])) throw new ArgumentException("Lasso points must be finite.");
            if (count == 0) return;
            outline.Add(nodes[0]);
            bool closed = count >= 3;
            int edgeCount = closed ? count : count - 1;
            for (int i = 0; i < edgeCount; i++)
            {
                int next = (i + 1) % count;
                Double2 a = nodes[i], d = nodes[next], chord = d - a;
                Double2 before = i > 0 ? (Double2)nodes[i - 1] : closed ? (Double2)nodes[count - 1] : a - chord;
                Double2 after = i + 2 < count ? (Double2)nodes[i + 2] : closed ? (Double2)nodes[(i + 2) % count] : d + chord;
                double left = KnotSpan(a - before), middle = KnotSpan(chord), right = KnotSpan(after - d);
                if (middle <= 1e-12) { Add(d, i, outline, segments); continue; }
                bool cornerA = i < corners.Count && corners[i], cornerD = next < corners.Count && corners[next];
                Double2 tangentA = cornerA || left <= 1e-12 ? chord :
                    ((a - before) * (1 / left) - (d - before) * (1 / (left + middle)) + chord * (1 / middle)) * middle;
                Double2 tangentD = cornerD || right <= 1e-12 ? chord :
                    (chord * (1 / middle) - (after - a) * (1 / (middle + right)) + (after - d) * (1 / right)) * middle;
                Flatten(a, a + tangentA * (1d / 3), d - tangentD * (1d / 3), d, i, 0, outline, segments);
            }
        }

        static double KnotSpan(Double2 delta) => Math.Sqrt(Math.Sqrt(delta.x * delta.x + delta.y * delta.y));

        static void Flatten(Double2 a, Double2 b, Double2 c, Double2 d, int segment, int depth, List<Vector2> outline, List<int> segments)
        {
            if (Math.Max(DistanceSquared(b, a, d), DistanceSquared(c, a, d)) <= ToleranceSquared)
            { Add(d, segment, outline, segments); return; }
            if (depth >= 24) throw new InvalidOperationException("The smoothed lasso cannot be represented accurately. Move overlapping points apart.");
            Double2 ab = (a + b) * .5, bc = (b + c) * .5, cd = (c + d) * .5;
            Double2 abc = (ab + bc) * .5, bcd = (bc + cd) * .5, mid = (abc + bcd) * .5;
            Flatten(a, ab, abc, mid, segment, depth + 1, outline, segments);
            Flatten(mid, bcd, cd, d, segment, depth + 1, outline, segments);
        }

        static double DistanceSquared(Double2 point, Double2 a, Double2 b)
        {
            Double2 delta = b - a, offset = point - a;
            double length = delta.x * delta.x + delta.y * delta.y;
            double t = length > 0 ? Math.Max(0, Math.Min(1, (offset.x * delta.x + offset.y * delta.y) / length)) : 0;
            Double2 distance = offset - delta * t;
            return distance.x * distance.x + distance.y * distance.y;
        }

        static void Add(Double2 point, int segment, List<Vector2> outline, List<int> segments)
        {
            Vector2 value = point;
            if (!VectorGeometry.Finite(value)) throw new ArgumentException("Lasso points must be finite.");
            if (outline[outline.Count - 1].Equals(value)) return;
            if (outline.Count >= MaximumPoints + 1) throw new InvalidOperationException("The smoothed lasso is too detailed. Use fewer points or a smaller outline.");
            outline.Add(value); segments.Add(segment);
        }
    }
}
