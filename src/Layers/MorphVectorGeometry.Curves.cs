using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class MorphVectorGeometry
    {
        private const double KnotEpsilon = 1e-12;
        private static int Mod(int n, int period) => (n % period + period) % period;
        private static double Wrap(double arc) => arc - Math.Floor(arc);

        private readonly struct Cubic
        {
            internal readonly Vector2 a, b, c, d;
            internal Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            { this.a = a; this.b = b; this.c = c; this.d = d; }
            internal Vector2 At(float t) => t <= 0 ? a : t >= 1 ? d : Blossom(t, t, t);

            // Polar form of de Casteljau. Also works for a reversed subinterval.
            private Vector2 Blossom(float u, float v, float w)
            {
                var ab = Vector2.LerpUnclamped(a,b,u); var bc = Vector2.LerpUnclamped(b,c,u);
                var cd = Vector2.LerpUnclamped(c,d,u);
                return Vector2.LerpUnclamped(Vector2.LerpUnclamped(ab,bc,v), Vector2.LerpUnclamped(bc,cd,v), w);
            }

            internal Cubic Slice(float u, float v) => u == 0 && v == 1 ? this :
                new Cubic(At(u), Blossom(u,u,v), Blossom(u,v,v), At(v));

            // The bounded WhimTex curve hit test locates the nearest branch. Refine
            // its parameter on the actual cubic, especially for zero-handle line segments.
            internal float Refine(Vector2 uv, Vector2 size, float t)
            {
                for (int i = 0; i < 5; i++)
                {
                    float u = 1-t;
                    Vector2 error = Vector2.Scale(At(t)-uv, size);
                    Vector2 velocity = Vector2.Scale(3*(u*u*(b-a) + 2*u*t*(c-b) + t*t*(d-c)), size);
                    Vector2 acceleration = Vector2.Scale(6*(u*(c-2*b+a) + t*(d-2*c+b)), size);
                    float slope = velocity.sqrMagnitude + Vector2.Dot(error, acceleration);
                    if (slope <= 0) break;
                    float next = Mathf.Clamp01(t-Vector2.Dot(error, velocity)/slope);
                    if (t == next || Vector2.Scale(At(next)-uv, size).sqrMagnitude > error.sqrMagnitude) break;
                    t = next;
                }
                return t;
            }
        }

        internal sealed class ContourPair
        {
            internal string id;
            internal readonly VectorContour from;
            private readonly VectorContour to;
            private readonly bool reverseSource;
            internal readonly bool reverseTarget;

            internal ContourPair(VectorContour from, VectorContour to, bool reverseSource, bool reverseTarget)
            { this.from = from; this.to = to; this.reverseSource = reverseSource; this.reverseTarget = reverseTarget; }

            internal VectorContour Sample(float t, VectorFillRule fillRule)
            {
                var result = new VectorContour { id = id, closed = from.closed, points = new List<VectorPoint>(from.points.Count) };
                for (int i = 0; i < from.points.Count; i++)
                {
                    var a = from.points[i]; var b = to.points[i];
                    result.points.Add(new VectorPoint(Vector2.LerpUnclamped(a.position, b.position, t))
                    {
                        id = a.id, inTangent = Vector2.LerpUnclamped(a.inTangent, b.inTangent, t),
                        outTangent = Vector2.LerpUnclamped(a.outTangent, b.outTangent, t)
                    });
                }
                if (fillRule == VectorFillRule.NonZero && result.closed && (t < .5f ? reverseSource : reverseTarget)) Reverse(result);
                return result;
            }
        }

        private static ContourPair Correspond(Outline a, Outline b, Vector2 size, bool dashed)
        {
            bool sameWinding = !a.Closed || Math.Sign(a.area) == Math.Sign(b.area);
            bool hasPins = a.pins?.Count > 0;
            bool movableSeam = a.Closed && !dashed;
            double startA = movableSeam && hasPins ? a.pins[0].a : 0;
            List<Pin> best = null;
            int direction = 1;
            double startB = 0, bestCost = double.PositiveInfinity;
            for (int dir = 1; dir >= -1; dir -= 2)
            {
                if (dashed && (dir < 0 || !sameWinding)) continue;
                if (a.Closed && !hasPins && dir != (sameWinding ? 1 : -1)) continue;
                double origin = !movableSeam ? (dir > 0 ? 0 : 1) : hasPins ? a.pins[0].b : 0;
                if (movableSeam && !hasPins) origin = FindSeam(a, b, size, dir);
                var knots = LandmarkKnots(a, dir, startA, origin);
                if (knots == null) continue;
                // A closed pair has a natural winding unless ordered anchors require the reverse.
                double cost = AlignmentCost(a, b, size, dir, startA, origin, knots);
                if (a.Closed && dir != (sameWinding ? 1 : -1)) cost += 1e30;
                if (cost >= bestCost) continue;
                best = knots; bestCost = cost; direction = dir; startB = origin;
            }
            if (best == null) return null;

            // Pair corresponding hard corners before distributing the remaining cubic
            // boundaries by arc length. E.g. preserve all four corners of changing rectangles.
            var av = Vertices(a, startA, 1, size);
            var bv = Vertices(b, startB, direction, size);
            var knotsWithCorners = new List<Pin> { best[0] };
            int ia = 0, ib = 0;
            for (int k = 1; k < best.Count; k++)
            {
                var left = best[k-1]; var right = best[k];
                var ac = Corners(av, ref ia, left.a, right.a);
                var bc = Corners(bv, ref ib, left.b, right.b);
                if (ac.Count == bc.Count)
                    for (int i = 0; i < ac.Count; i++) knotsWithCorners.Add(new Pin(ac[i], bc[i]));
                knotsWithCorners.Add(right);
            }
            var cuts = new List<Pin>(knotsWithCorners);
            AddVertexCuts(av, knotsWithCorners, true, cuts);
            AddVertexCuts(bv, knotsWithCorners, false, cuts);
            cuts.Sort((x,y) => x.a.CompareTo(y.a));

            var ca = new VectorContour { closed = a.Closed };
            var cb = new VectorContour { closed = b.Closed };
            Pin previous = cuts[0];
            for (int i = 1; i < cuts.Count; i++)
            {
                var next = cuts[i];
                if (next.a - previous.a <= KnotEpsilon) continue;
                if (ca.points.Count >= VectorGeometry.MaxPoints) throw VectorGeometry.Limit("morph subdivision count");
                Append(ca, Slice(a, startA + previous.a, startA + next.a));
                Append(cb, Slice(b, startB + direction * previous.b, startB + direction * next.b));
                previous = next;
            }
            if (a.Closed) { Close(ca); Close(cb); }
            return new ContourPair(ca, cb, false, direction < 0);
        }

        private static List<Pin> LandmarkKnots(Outline path, int direction, double startA, double startB)
        {
            var knots = new List<Pin> { new Pin(0,0), new Pin(1,1) };
            if (path.pins != null) foreach (var pin in path.pins)
            {
                double x = pin.a - startA, y = direction * (pin.b - startB);
                if (path.Closed) { x = Wrap(x); y = Wrap(y); }
                knots.Add(new Pin(x,y));
            }
            knots.Sort((x,y) => x.a.CompareTo(y.a));
            var ordered = new List<Pin>();
            foreach (var pin in knots)
            {
                if (ordered.Count > 0)
                {
                    var last = ordered[ordered.Count-1];
                    if (Math.Abs(pin.a-last.a) <= KnotEpsilon && Math.Abs(pin.b-last.b) <= KnotEpsilon) continue;
                    if (pin.a <= last.a + KnotEpsilon || pin.b <= last.b + KnotEpsilon) return null;
                }
                ordered.Add(pin);
            }
            return ordered;
        }

        private static double FindSeam(Outline a, Outline b, Vector2 size, int dir)
        {
            var ends = new List<Pin> { new Pin(0,0), new Pin(1,1) };
            double phase = 0, best = double.PositiveInfinity;
            void Try(double value)
            {
                double cost = AlignmentCost(a,b,size,dir,0,value,ends);
                if (cost < best) { best = cost; phase = value; }
            }
            if (IsCorner(a, 0, size))
            {
                for (int i = 0; i < b.Segments; i++)
                    if (IsCorner(b, i, size)) Try(b.Arc(i, 0));
                if (!double.IsPositiveInfinity(best)) return phase;
            }
            for (int i = 0; i < 32; i++) Try(i / 32d);
            int stride = Math.Max(1, b.Segments / 128);
            for (int i = 0; i < b.Segments; i += stride) Try(b.Arc(i,0));
            for (double step = 1d/64; step > 1e-7; step *= .5)
            { double center = phase; Try(center-step); Try(center+step); }
            // Prefer an authored seam when the sampled arc metric differs by subpixel noise.
            double nearest = Math.Round(b.Parameter(phase));
            double vertex = b.Arc(Mod((int)nearest, b.Segments), 0);
            double gap = Math.Abs(Wrap(phase)-vertex);
            return Math.Min(gap, 1-gap)*b.length <= .05 ? vertex : Wrap(phase);
        }

        private static double AlignmentCost(Outline a, Outline b, Vector2 size, int dir, double startA,
            double startB, List<Pin> knots)
        {
            double cost = 0;
            int k = 1;
            for (int i = 0; i <= 24; i++)
            {
                double x = i / 24d;
                while (k+1 < knots.Count && knots[k].a < x) k++;
                var left = knots[k-1]; var right = knots[k];
                double y = left.b + (right.b-left.b)*(x-left.a)/(right.a-left.a);
                Vector2 delta = Vector2.Scale(a.At(startA+x) - b.At(startB+dir*y), size) -
                    (a.bounds.center-b.bounds.center);
                cost += delta.sqrMagnitude;
            }
            return cost;
        }

        private readonly struct Vertex
        {
            internal readonly double arc;
            internal readonly bool corner;
            internal Vertex(double arc, bool corner) { this.arc = arc; this.corner = corner; }
        }

        private static List<Vertex> Vertices(Outline path, double start, int dir, Vector2 size)
        {
            var vertices = new List<Vertex>();
            for (int i = path.Closed ? 0 : 1; i < path.Segments; i++)
            {
                double arc = dir * (path.Arc(i,0)-start);
                if (path.Closed) arc = Wrap(arc);
                if (arc <= KnotEpsilon || arc >= 1-KnotEpsilon) continue;
                vertices.Add(new Vertex(arc, IsCorner(path, i, size)));
            }
            vertices.Sort((x,y) => x.arc.CompareTo(y.arc));
            return vertices;
        }

        private static bool IsCorner(Outline path, int i, Vector2 size)
        {
            var before = path.Curve(Mod(i-1,path.Segments)); var after = path.Curve(i);
            Vector2 incoming = before.d-before.c, outgoing = after.b-after.a;
            if (incoming.Equals(Vector2.zero)) incoming = before.d-before.b;
            if (outgoing.Equals(Vector2.zero)) outgoing = after.c-after.a;
            if (incoming.Equals(Vector2.zero)) incoming = before.d-before.a;
            if (outgoing.Equals(Vector2.zero)) outgoing = after.d-after.a;
            return Vector2.Dot(Vector2.Scale(incoming,size).normalized, Vector2.Scale(outgoing,size).normalized) < .98f;
        }

        private static List<double> Corners(List<Vertex> vertices, ref int index, double min, double max)
        {
            var result = new List<double>();
            while (index < vertices.Count && vertices[index].arc < max-KnotEpsilon)
            {
                var v = vertices[index++];
                if (v.corner && v.arc > min+KnotEpsilon && (result.Count == 0 || v.arc > result[result.Count-1]+KnotEpsilon))
                    result.Add(v.arc);
            }
            return result;
        }

        private static void AddVertexCuts(List<Vertex> vertices, List<Pin> knots, bool source, List<Pin> cuts)
        {
            int k = 1;
            foreach (var vertex in vertices)
            {
                double at = vertex.arc;
                while (k+1 < knots.Count && (source ? knots[k].a : knots[k].b) < at) k++;
                var lo = knots[k-1]; var hi = knots[k];
                double start = source ? lo.a : lo.b, end = source ? hi.a : hi.b;
                if (at <= start+KnotEpsilon || at >= end-KnotEpsilon) continue;
                double u = (at-start)/(end-start);
                cuts.Add(source ? new Pin(at, lo.b+(hi.b-lo.b)*u) : new Pin(lo.a+(hi.a-lo.a)*u, at));
            }
        }

        private static Cubic Slice(Outline path, double start, double end)
        {
            int segment = (int)Math.Floor(path.Parameter((start+end)*.5));
            float u = Mathf.Clamp01((float)(path.Parameter(start)-segment));
            float v = Mathf.Clamp01((float)(path.Parameter(end)-segment));
            return path.Curve(Mod(segment,path.Segments)).Slice(u,v);
        }

        private static void Append(VectorContour contour, Cubic curve)
        {
            if (contour.points.Count == 0) contour.points.Add(new VectorPoint(curve.a));
            contour.points[contour.points.Count-1].outTangent = curve.b-curve.a;
            contour.points.Add(new VectorPoint(curve.d) { inTangent = curve.c-curve.d });
        }

        private static void Close(VectorContour contour)
        {
            int last = contour.points.Count-1;
            contour.points[0].inTangent = contour.points[last].inTangent;
            contour.points.RemoveAt(last);
        }
    }
}
