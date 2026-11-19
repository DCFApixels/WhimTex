using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class MorphVectorGeometry
    {
        private readonly struct Pin
        {
            internal readonly double a, b;
            internal Pin(double a, double b) { this.a = a; this.b = b; }
        }

        private sealed class Outline
        {
            internal readonly VectorContour contour;
            internal readonly List<Vector2> polygon = new List<Vector2>(); // group pixels, for nesting only
            internal readonly Rect bounds;
            internal readonly double area, length;
            internal readonly double[] lengths;
            internal int parent = -1, mate = -1;
            internal bool reversed;
            internal List<Pin> pins;
            internal bool Closed => contour.closed;
            internal int Segments => contour.points.Count - (Closed ? 0 : 1);
            internal const int Samples = 16;

            internal Outline(VectorContour contour, Vector2 size)
            {
                this.contour = contour;
                VectorGeometry.FlattenContour(contour, size, .05f, polygon);
                Vector2 min = polygon[0], max = min;
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 p = polygon[i], q = polygon[(i + 1) % polygon.Count];
                    min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                    area += ((double)p.x * q.y - (double)p.y * q.x) * .5;
                }
                bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                lengths = new double[Segments * Samples + 1];
                var previous = contour.points[0].position;
                for (int s = 0; s < Segments; s++)
                {
                    var curve = Curve(s);
                    for (int j = 1; j <= Samples; j++)
                    {
                        Vector2 next = curve.At(j / (float)Samples);
                        length += Vector2.Scale(next - previous, size).magnitude;
                        lengths[s * Samples + j] = length;
                        previous = next;
                    }
                }
            }

            internal Cubic Curve(int i)
            {
                var p = contour.points[i]; var q = contour.points[(i + 1) % contour.points.Count];
                return new Cubic(p.position, p.position + p.outTangent, q.position + q.inTangent, q.position);
            }

            internal bool Contains(Outline other)
            {
                if (bounds.xMin > other.bounds.xMin || bounds.yMin > other.bounds.yMin ||
                    bounds.xMax < other.bounds.xMax || bounds.yMax < other.bounds.yMax) return false;
                Vector2 p = other.polygon[0];
                bool inside = false;
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                {
                    var a = polygon[i]; var b = polygon[j];
                    if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (b.x-a.x) * (double)(p.y-a.y) / (b.y-a.y))
                        inside = !inside;
                }
                return inside;
            }

            internal double Arc(int segment, float t)
            {
                double index = (segment + (double)t) * Samples;
                int i = Math.Min((int)index, lengths.Length - 2);
                return (lengths[i] + (lengths[i+1] - lengths[i]) * (index-i)) / length;
            }

            // Return an unwrapped segment parameter, including backwards travel across the seam.
            internal double Parameter(double arc)
            {
                double lap = Math.Floor(arc), local = (arc - lap) * length;
                int i = Array.BinarySearch(lengths, local);
                if (i >= 0) return lap * Segments + i / (double)Samples;
                i = ~i - 1;
                double blend = (local - lengths[i]) / (lengths[i+1] - lengths[i]);
                return lap * Segments + (i + blend) / Samples;
            }

            internal Vector2 At(double arc)
            {
                if (!Closed && arc <= 0) return contour.points[0].position;
                if (!Closed && arc >= 1) return contour.points[contour.points.Count-1].position;
                double param = Parameter(arc);
                int s = (int)Math.Floor(param);
                return Curve(Mod(s, Segments)).At((float)(param - s));
            }
        }

        private static List<Outline> Outlines(VectorShape shape, Vector2 size)
        {
            if (shape.contours.Count > MaxMorphContours) throw VectorGeometry.Limit("morph contour count");
            var paths = new List<Outline>();
            int polygonPoints = 0;
            foreach (var contour in shape.contours)
            {
                if (contour.points.Count == 0 || (!contour.closed && contour.points.Count == 1)) continue;
                var path = new Outline(contour, size);
                if ((polygonPoints += path.polygon.Count) > VectorGeometry.MaxFlattenedPoints) throw VectorGeometry.Limit("morph polygon points");
                if (path.length > 0) paths.Add(path);
            }
            for (int i = 0; i < paths.Count; i++)
            {
                var child = paths[i];
                if (!child.Closed) continue;
                double smallest = double.PositiveInfinity;
                for (int j = 0; j < paths.Count; j++)
                {
                    var outer = paths[j];
                    double area = Math.Abs(outer.area);
                    if (outer.Closed && area > Math.Abs(child.area) && area < smallest && outer.Contains(child))
                    { child.parent = j; smallest = area; }
                }
            }
            return paths;
        }

        private static int Project(List<Outline> paths, Vector2 uv, Vector2 size, out double arc)
        {
            arc = 0;
            if (!VectorGeometry.Finite(uv)) return -1;
            int hit = -1;
            float best = float.MaxValue;
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (VectorGeometry.TryHitSegment(path.contour, uv, size, best,
                    out int s, out float t, out float distance) && distance < best)
                {
                    hit = i; best = distance;
                    arc = path.Arc(s, path.Curve(s).Refine(uv, size, t));
                }
            }
            return hit;
        }

        private static List<ContourPair> Match(VectorShape source, VectorShape target, Vector2 size,
            IReadOnlyList<MorphAnchor> anchors)
        {
            var a = Outlines(source, size); var b = Outlines(target, size);
            if (anchors != null) foreach (var anchor in anchors)
            {
                if (anchor == null) return null;
                int i = Project(a, anchor.from, size, out double x), j = Project(b, anchor.to, size, out double y);
                if (i < 0 || j < 0) return null;
                (a[i].pins ??= new List<Pin>()).Add(new Pin(x, y));
                // A landmark on a hole also identifies its containing figure(s).
                while (i >= 0 && j >= 0)
                {
                    if (a[i].Closed != b[j].Closed || (a[i].mate >= 0 && a[i].mate != j) ||
                        (b[j].mate >= 0 && b[j].mate != i)) return null;
                    a[i].mate = j; b[j].mate = i;
                    i = a[i].parent; j = b[j].parent;
                }
                if (i != j) return null;
            }

            var parents = new Queue<(int a, int b)>();
            parents.Enqueue((-1, -1));
            while (parents.Count > 0)
            {
                var p = parents.Dequeue();
                PairChildren(a, b, p.a, p.b, true, size);
                if (p.a < 0) PairChildren(a, b, -1, -1, false, size);
                for (int i = 0; i < a.Count; i++)
                    if (a[i].Closed && a[i].parent == p.a && a[i].mate >= 0)
                        parents.Enqueue((i, a[i].mate));
            }

            bool dashed = HasDashes(source) || HasDashes(target);
            var result = new List<ContourPair>();
            int points = 0;
            foreach (var path in a)
            {
                if (path.mate < 0) continue;
                var other = b[path.mate];
                var pair = Correspond(path, other, size, dashed);
                if (pair == null) return null;
                path.reversed = other.reversed = pair.reverseTarget;
                Add(pair);
            }
            // Unpaired islands/holes/open paths grow from or shrink to their own centre.
            // Preserve relative winding when the paired containing ring changed direction.
            void Unmatched(List<Outline> paths, bool missingSource)
            {
                foreach (var path in paths)
                {
                    if (path.mate >= 0) continue;
                    var contour = path.contour.Clone();
                    Vector2 center = path.bounds.center;
                    center = new Vector2(center.x / size.x, center.y / size.y);
                    foreach (var point in contour.points)
                    {
                        point.position = center;
                        point.inTangent = point.outTangent = Vector2.zero;
                    }
                    int parent = path.parent;
                    while (parent >= 0 && paths[parent].mate < 0) parent = paths[parent].parent;
                    bool reverse = parent >= 0 && paths[parent].reversed;
                    Add(missingSource ? new ContourPair(contour, path.contour, reverse, false) :
                        new ContourPair(path.contour.Clone(), contour, false, reverse));
                }
            }
            void Add(ContourPair pair)
            {
                if ((points += pair.from.points.Count) > VectorGeometry.MaxPoints) throw VectorGeometry.Limit("morph output points");
                pair.id = "c" + result.Count;
                for (int i = 0; i < pair.from.points.Count; i++) pair.from.points[i].id = pair.id + "." + i;
                result.Add(pair);
            }
            Unmatched(a, false);
            Unmatched(b, true);
            return result;
        }

        private static bool HasDashes(VectorShape s) =>
            s.visible && s.stroke && s.strokeMode != VectorFillMode.None && s.strokeWidth > 0 && s.dashPattern?.Count > 0;

        private static void Reverse(VectorContour contour)
        {
            contour.points.Reverse();
            foreach (var p in contour.points) (p.inTangent, p.outTangent) = (p.outTangent, p.inTangent);
        }

        private static void PairChildren(List<Outline> a, List<Outline> b, int parentA, int parentB, bool closed, Vector2 size)
        {
            List<int> Available(List<Outline> paths, int parent)
            {
                var indices = new List<int>();
                for (int i = 0; i < paths.Count; i++)
                    if (paths[i].parent == parent && paths[i].Closed == closed && paths[i].mate < 0) indices.Add(i);
                return indices;
            }
            var left = Available(a, parentA); var right = Available(b, parentB);
            if (left.Count == 0 || right.Count == 0) return;
            bool swap = left.Count > right.Count;
            var rows = swap ? right : left; var cols = swap ? left : right;
            var costs = new double[rows.Count, cols.Count];
            Vector2 offset = parentA < 0 ? Vector2.zero : b[parentB].bounds.center - a[parentA].bounds.center;
            double diagonal = size.magnitude;
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < cols.Count; c++)
                {
                    var x = a[swap ? cols[c] : rows[r]]; var y = b[swap ? rows[r] : cols[c]];
                    costs[r,c] = x.contour.id == y.contour.id ? 0 :
                        1 + (x.bounds.center + offset - y.bounds.center).magnitude / diagonal +
                        (x.bounds.size - y.bounds.size).magnitude / diagonal + Math.Abs(Math.Log(x.length/y.length)) * .25;
                }
            var assignments = Assign(costs);
            for (int c = 1; c < assignments.Length; c++)
            {
                if (assignments[c] == 0) continue;
                int row = rows[assignments[c]-1], col = cols[c-1];
                int i = swap ? col : row, j = swap ? row : col;
                a[i].mate = j; b[j].mate = i;
            }
        }

        // Rectangular Hungarian assignment. Rows are the smaller sibling set.
        private static int[] Assign(double[,] costs)
        {
            int rows = costs.GetLength(0), cols = costs.GetLength(1);
            var u = new double[rows+1]; var v = new double[cols+1];
            var match = new int[cols+1]; var prev = new int[cols+1];
            for (int r = 1; r <= rows; r++)
            {
                match[0] = r;
                int col = 0;
                var used = new bool[cols+1]; var min = new double[cols+1];
                Array.Fill(min, double.PositiveInfinity);
                do
                {
                    used[col] = true;
                    int row = match[col], next = 0;
                    double delta = double.PositiveInfinity;
                    for (int c = 1; c <= cols; c++)
                    {
                        if (used[c]) continue;
                        double cost = costs[row-1,c-1] - u[row] - v[c];
                        if (cost < min[c]) { min[c] = cost; prev[c] = col; }
                        if (min[c] < delta) { delta = min[c]; next = c; }
                    }
                    for (int c = 0; c <= cols; c++)
                        if (used[c]) { u[match[c]] += delta; v[c] -= delta; } else min[c] -= delta;
                    col = next;
                } while (match[col] != 0);
                do { int next = prev[col]; match[col] = match[next]; col = next; } while (col != 0);
            }
            return match;
        }
    }
}
