using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class MorphPathKnot
    {
        // Handles are offsets from position in endpoint UV coordinates.
        public Vector2 position, inTangent, outTangent;
        public bool smooth;

        internal bool IsFinite => VectorGeometry.Finite(position) && VectorGeometry.Finite(inTangent) &&
            VectorGeometry.Finite(outTangent) && VectorGeometry.Finite(position + inTangent) &&
            VectorGeometry.Finite(position + outTangent);
    }

    [Serializable]
    public sealed class MorphGuidePath
    {
        public List<MorphPathKnot> from = new(), to = new();
        public bool closed, boundary, foreground;
        public float radius;
        public MorphGuideCorrection correction;
        // Paired fractions of whole-side pixel arc length. The first knot is the seam on each closed side.
        public List<Vector2> marks = new();

        [NonSerialized] private ArcTable fromTable, toTable, compiledFrom, compiledTo;
        [NonSerialized] private List<MorphSegment> pieces;
        [NonSerialized] private Vector2[] compiledMarks;

        internal List<MorphPathKnot> Side(bool fromSide) => fromSide ? from : to;

        internal Vector2 Evaluate(bool fromSide, int span, float t)
        {
            var side = Side(fromSide);
            var a = side[span];
            var b = side[Next(side, span)];
            return VectorGeometry.EvaluateCubic(a.position, a.position + a.outTangent,
                b.position + b.inTangent, b.position, t);
        }

        private int Next(List<MorphPathKnot> side, int span) =>
            closed && span + 1 == side.Count ? 0 : span + 1;

        internal bool Split(bool fromSide, int span, float t)
        {
            var side = Side(fromSide);
            if (side == null || side.Count < 2 || side.Count >= 1_000_000 ||
                span < 0 || span >= side.Count - (closed ? 0 : 1) || !(t > 0 && t < 1)) return false;
            var a = side[span];
            var b = side[Next(side, span)];
            if (a?.IsFinite != true || b?.IsFinite != true || a.position.Equals(b.position)) return false;
            var p = Vector2.LerpUnclamped(a.position, a.position + a.outTangent, t);
            var q = Vector2.LerpUnclamped(a.position + a.outTangent, b.position + b.inTangent, t);
            var r = Vector2.LerpUnclamped(b.position + b.inTangent, b.position, t);
            var left = Vector2.LerpUnclamped(p, q, t);
            var right = Vector2.LerpUnclamped(q, r, t);
            var joint = Vector2.LerpUnclamped(left, right, t);
            var knot = new MorphPathKnot { position = joint, inTangent = left - joint, outTangent = right - joint,
                smooth = !left.Equals(joint) && !right.Equals(joint) };
            var outgoing = p - a.position;
            var incoming = r - b.position;
            if (!knot.IsFinite || joint.Equals(a.position) || joint.Equals(b.position) ||
                !VectorGeometry.Finite(outgoing) || !VectorGeometry.Finite(incoming) ||
                !VectorGeometry.Finite(a.position + outgoing) || !VectorGeometry.Finite(b.position + incoming)) return false;
            side.Insert(span + 1, knot);
            a.outTangent = outgoing;
            b.inTangent = incoming;
            return true;
        }

        internal Vector2 AtLength(bool fromSide, float fraction, Vector2 size) =>
            Table(fromSide, size).At(Mathf.Clamp01(fraction));

        internal float LengthAt(bool fromSide, int span, float t, Vector2 size)
        {
            var table = Table(fromSide, size);
            if (span < 0 || span >= table.Spans) throw new ArgumentOutOfRangeException(nameof(span));
            return (float)table.Fraction(span + (double)Mathf.Clamp01(t));
        }

        internal void Validate()
        {
            ValidateSide(from, closed);
            ValidateSide(to, closed);
            if (!MorphTransition.ValidRadius(radius)) throw new InvalidOperationException("Invalid morph guide path radius.");
            if (foreground && !closed) throw new InvalidOperationException("A foreground contour must close on both images.");
            correction.Validate();
            ValidateMarks();
        }

        private void ValidateMarks()
        {
            if (marks == null) throw new InvalidOperationException("Missing morph guide path marks.");
            var previous = Vector2.zero;
            foreach (var mark in marks)
            {
                if (!MorphSegment.InteriorAfter(mark, previous))
                    throw new InvalidOperationException("Morph guide path marks must be interior and ordered in both images.");
                previous = mark;
            }
        }

        private static void ValidateSide(List<MorphPathKnot> side, bool closed)
        {
            if (side == null || side.Count < 2) throw new InvalidOperationException("Each morph guide path needs at least two knots per side.");
            MorphPathKnot previous = closed ? side[side.Count - 1] : null;
            foreach (var knot in side)
            {
                if (knot?.IsFinite != true || previous != null && previous.position.Equals(knot.position))
                    throw new InvalidOperationException("Morph guide path spans require finite controls and distinct consecutive knots.");
                previous = knot;
            }
        }

        private ArcTable Table(bool fromSide, Vector2 size)
        {
            var side = Side(fromSide);
            var cached = fromSide ? fromTable : toTable;
            if (cached != null && cached.Matches(side, closed, size)) return cached;
            ValidateSide(side, closed);
            VectorGeometry.ValidateReferenceSize(size);
            var next = new ArcTable(side, closed, size);
            if (fromSide) fromTable = next;
            else toTable = next;
            return next;
        }

        // Borrowed chords for both the overlay and the ordinary raster renderer.
        internal void AppendLinear(Vector2 size, List<MorphSegment> output)
        {
            var a = Table(true, size);
            var b = Table(false, size);
            bool reuse = a == compiledFrom && b == compiledTo && marks != null && compiledMarks?.Length == marks.Count;
            for (int i = 0; reuse && i < marks.Count; i++) reuse = marks[i].Equals(compiledMarks[i]);
            if (!reuse)
            {
                ValidateMarks();
                pieces ??= new();
                compiledFrom = null;
                pieces.Clear();
                int cornerA = 0, cornerB = 0;
                var previous = Vector2.zero;
                for (int i = 0; i <= marks.Count; i++)
                {
                    var next = i < marks.Count ? marks[i] : Vector2.one;
                    AppendInterval(a, b, size, previous, next, ref cornerA, ref cornerB);
                    previous = next;
                }
                if (pieces.Count > 0) pieces[0].chordCount = pieces.Count;
                compiledMarks = marks.Count == 0 ? Array.Empty<Vector2>() : marks.ToArray();
                compiledFrom = a;
                compiledTo = b;
            }
            foreach (var piece in pieces)
            {
                piece.radius = radius;
                piece.boundary = boundary;
                piece.foreground = foreground;
                piece.correction = correction;
                output.Add(piece);
            }
        }

        private void AppendInterval(ArcTable a, ArcTable b, Vector2 size, Vector2 lo, Vector2 hi,
            ref int cornerA, ref int cornerB)
        {
            double rangeA = (double)hi.x - lo.x, rangeB = (double)hi.y - lo.y;
            double start = 0;
            while (start < 1)
            {
                double x = cornerA < a.corners.Count ? (a.corners[cornerA] - lo.x) / rangeA : double.PositiveInfinity;
                double y = cornerB < b.corners.Count ? (b.corners[cornerB] - lo.y) / rangeB : double.PositiveInfinity;
                double end = Math.Min(1, Math.Min(x, y));
                if (x <= end) cornerA++;
                if (y <= end) cornerB++;
                if (end <= start) continue;
                Flatten(start, end, Pair(start), Pair(end));
                start = end;
            }

            Vector4 Pair(double u)
            {
                var p = a.At(lo.x + u * rangeA);
                var q = b.At(lo.y + u * rangeB);
                return new Vector4(p.x, p.y, q.x, q.y);
            }

            void Flatten(double left, double right, Vector4 p, Vector4 q)
            {
                // At fraction t of a polyline of length L, the squared distance to the
                // corresponding chord point is <= t(1-t)(L²-chord²). Bisect in whole-path
                // arc length, so adding a smooth knot does not introduce a raster breakpoint.
                double step = right - left;
                if (NeedsSplit(true, a.Length * rangeA * step) || NeedsSplit(false, b.Length * rangeB * step))
                {
                    double mid = (left + right) * .5;
                    if (mid <= left || mid >= right) throw VectorGeometry.Limit("morph guide arc-length precision");
                    var m = Pair(mid);
                    Flatten(left, mid, p, m);
                    Flatten(mid, right, m, q);
                    return;
                }
                // A split stored in float UVs can move an otherwise identical point by a few ULPs.
                // Only quantize the solver upload, after both the length mapping and flatness test.
                p = RasterPair(p, size);
                q = RasterPair(q, size);
                if (p.Equals(q)) return;
                if (pieces.Count >= VectorGeometry.MaxFlattenedPoints) throw VectorGeometry.Limit("morph guide chord count");
                pieces.Add(new MorphSegment { pathChord = true, fromStart = MorphSegment.Side(p, true), fromEnd = MorphSegment.Side(q, true),
                    toStart = MorphSegment.Side(p, false), toEnd = MorphSegment.Side(q, false) });

                bool NeedsSplit(bool source, double length)
                {
                    var v = MorphSegment.Side(p, source);
                    var w = MorphSegment.Side(q, source);
                    double dx = ((double)w.x - v.x) * size.x, dy = ((double)w.y - v.y) * size.y;
                    const double tolerance = .08; // Maximum chord error in raster pixels.
                    return length * length - dx * dx - dy * dy > 4 * tolerance * tolerance;
                }
            }
        }

        private static Vector4 RasterPair(Vector4 pair, Vector2 size)
        {
            static float Snap(float uv, double pixels)
            {
                double grid = 1024 * pixels;
                return (float)(Math.Round(uv * grid) / grid);
            }
            return new Vector4(Snap(pair.x, size.x), Snap(pair.y, size.y), Snap(pair.z, size.x), Snap(pair.w, size.y));
        }

        // The table brackets the inverse arc length. Both the length and the returned UV
        // are evaluated on the authored cubic, independently of the bracket boundaries.
        private sealed class ArcTable
        {
            private readonly List<(int span, double t, double length)> samples = new();
            private readonly Cubic[] curves;
            private readonly (Vector2 position, Vector2 incoming, Vector2 outgoing)[] geometry;
            private readonly bool closed;
            private readonly Vector2 size;
            internal readonly List<double> corners = new();
            internal int Spans => geometry.Length - (closed ? 0 : 1);
            internal double Length => samples[samples.Count - 1].length;

            internal bool Matches(List<MorphPathKnot> knots, bool loop, Vector2 pixels)
            {
                if (knots?.Count != geometry.Length || loop != closed || !pixels.Equals(size)) return false;
                for (int i = 0; i < knots.Count; i++)
                {
                    var k = knots[i];
                    if (k == null || !geometry[i].Equals((k.position, k.inTangent, k.outTangent))) return false;
                }
                return true;
            }

            internal ArcTable(List<MorphPathKnot> knots, bool closed, Vector2 size)
            {
                this.closed = closed;
                this.size = size;
                geometry = new (Vector2, Vector2, Vector2)[knots.Count];
                curves = new Cubic[Spans];
                samples.Add((0, 0, 0));
                // A cubic has up to two extrema on each axis, plus both endpoints.
                var cuts = new double[6];
                for (int i = 0; i < knots.Count; i++)
                {
                    var k = knots[i];
                    geometry[i] = (k.position, k.inTangent, k.outTangent);
                    if (i >= Spans) continue;
                    var next = knots[(i + 1) % knots.Count];
                    if (i > 0 && IsCorner(knots[i - 1], k, next)) corners.Add(Length);
                    var curve = curves[i] = new Cubic(k, next, size);
                    int count = 1;
                    cuts[0] = 0;
                    curve.x.AddExtrema(cuts, ref count);
                    curve.y.AddExtrema(cuts, ref count);
                    cuts[count++] = 1;
                    Array.Sort(cuts, 0, count);
                    for (int j = 1; j < count; j++)
                    {
                        double lo = cuts[j - 1], hi = cuts[j];
                        if (lo < hi) Sample(i, lo, hi, curve.Arc(lo, hi), 0);
                    }
                }
                if (!(Length > 0) || double.IsInfinity(Length))
                    throw new InvalidOperationException("Morph guide path must have finite positive pixel length.");
                for (int i = 0; i < corners.Count; i++) corners[i] /= Length;
            }

            private static bool IsCorner(MorphPathKnot prev, MorphPathKnot k, MorphPathKnot next)
            {
                // A zero handle takes its one-sided direction from the next distinct control.
                var incoming = -k.inTangent;
                if (incoming.Equals(Vector2.zero)) incoming = k.position - (prev.position + prev.outTangent);
                if (incoming.Equals(Vector2.zero)) incoming = k.position - prev.position;
                var outgoing = k.outTangent;
                if (outgoing.Equals(Vector2.zero)) outgoing = next.position + next.inTangent - k.position;
                if (outgoing.Equals(Vector2.zero)) outgoing = next.position - k.position;
                double dot = (double)incoming.x * outgoing.x + (double)incoming.y * outgoing.y;
                double cross = (double)incoming.x * outgoing.y - (double)incoming.y * outgoing.x;
                return dot <= 0 || Math.Abs(cross) > .0001 * dot;
            }

            private readonly struct Axis
            {
                private readonly double origin, linear, quadratic, cubic;

                internal Axis(double p0, double p1, double p2, double p3, double pixels)
                {
                    origin = p0 * pixels;
                    double a = (p1 - p0) * pixels, b = (p2 - p1) * pixels, c = (p3 - p2) * pixels;
                    linear = 3 * a;
                    quadratic = 3 * (b - a);
                    cubic = a - 2 * b + c;
                }

                internal double Position(double t) => origin + t * (linear + t * (quadratic + t * cubic));
                internal double Velocity(double t) => linear + t * (2 * quadratic + t * 3 * cubic);

                // Isolate reversals so a short return near a cusp cannot fall between quadrature sites.
                internal void AddExtrema(double[] cuts, ref int count)
                {
                    double a = 3 * cubic, b = 2 * quadratic;
                    if (a == 0)
                    {
                        if (b != 0) Add(-linear / b, cuts, ref count);
                        return;
                    }
                    double discriminant = b * b - 4 * a * linear;
                    if (discriminant < 0) return;
                    double root = Math.Sqrt(discriminant);
                    double q = -.5 * (b + (b < 0 ? -root : root));
                    if (q == 0) return;
                    Add(q / a, cuts, ref count);
                    Add(linear / q, cuts, ref count);

                    static void Add(double t, double[] values, ref int n)
                    {
                        if (t > 0 && t < 1) values[n++] = t;
                    }
                }
            }

            private readonly struct Cubic
            {
                internal readonly Axis x, y;
                internal Cubic(MorphPathKnot a, MorphPathKnot b, Vector2 size)
                {
                    var p = a.position + a.outTangent;
                    var q = b.position + b.inTangent;
                    x = new Axis(a.position.x, p.x, q.x, b.position.x, size.x);
                    y = new Axis(a.position.y, p.y, q.y, b.position.y, size.y);
                }
                internal double Speed(double t)
                {
                    double dx = x.Velocity(t), dy = y.Velocity(t);
                    return Math.Sqrt(dx * dx + dy * dy);
                }

                // Five-point Gauss-Legendre integral of |dP/dt| in canvas pixels.
                internal double Arc(double lo, double hi)
                {
                    double mid = .5 * (lo + hi), h = .5 * (hi - lo);
                    double s = h * .5384693101056831, t = h * .9061798459386640;
                    return h * (.5688888888888889 * Speed(mid)
                        + .4786286704993665 * (Speed(mid - s) + Speed(mid + s))
                        + .2369268850561891 * (Speed(mid - t) + Speed(mid + t)));
                }
            }

            private void Sample(int span, double lo, double hi, double length, int depth)
            {
                var curve = curves[span];
                double mid = .5 * (lo + hi), left = curve.Arc(lo, mid), right = curve.Arc(mid, hi);
                if (Math.Abs(left + right - length) > Math.Max(1e-9, 1e-12 * length))
                {
                    if (depth >= 24) throw VectorGeometry.Limit("morph guide subdivision depth");
                    Sample(span, lo, mid, left, depth + 1);
                    Sample(span, mid, hi, right, depth + 1);
                    return;
                }
                if (samples.Count >= VectorGeometry.MaxFlattenedPoints) throw VectorGeometry.Limit("morph guide length table");
                samples.Add((span, hi, Length + length));
            }

            internal double Fraction(double parameter)
            {
                int i = Upper(parameter, false);
                var p = samples[i - 1]; var q = samples[i];
                if (parameter == q.span + q.t) return q.length / Length;
                double start = p.span == q.span ? p.t : 0;
                return (p.length + curves[q.span].Arc(start, parameter - q.span)) / Length;
            }

            internal Vector2 At(double fraction)
            {
                if (fraction <= 0) return geometry[0].position;
                if (fraction >= 1) return geometry[closed ? 0 : geometry.Length - 1].position;
                double length = fraction * Length;
                int i = Upper(length, true);
                var p = samples[i - 1]; var q = samples[i];
                var curve = curves[q.span];
                double start = p.span == q.span ? p.t : 0;
                double lo = start, hi = q.t, t = hi;
                if (length < q.length)
                {
                    double target = length - p.length;
                    t = lo + (hi - lo) * (target / (q.length - p.length));
                    // Safeguarded Newton iteration also handles a zero-speed endpoint.
                    for (int j = 0; j < 48; j++)
                    {
                        double error = curve.Arc(start, t) - target;
                        if (Math.Abs(error) <= 1e-9) break;
                        if (error < 0) lo = t; else hi = t;
                        double next = t - error / curve.Speed(t);
                        t = next > lo && next < hi ? next : .5 * (lo + hi);
                    }
                }
                return new Vector2((float)(curve.x.Position(t) / size.x), (float)(curve.y.Position(t) / size.y));
            }

            private int Upper(double value, bool byLength)
            {
                int lo = 1, hi = samples.Count - 1;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    var sample = samples[mid];
                    if ((byLength ? sample.length : sample.span + sample.t) < value) lo = mid + 1;
                    else hi = mid;
                }
                return lo;
            }
        }
    }
}
