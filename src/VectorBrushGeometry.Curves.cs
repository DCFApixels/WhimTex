using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class VectorBrushGeometry
    {
        // Least-squares cubic fitting with chord parameters, Newton refinement and
        // subdivision (Schneider, Graphics Gems, 1990). This implementation also bounds
        // error BETWEEN samples and checks the compound contour after fitting.
        static List<VectorContour> FitContours(List<List<Vector2>> source, float tolerance,
            Vector2 step, Vector2 referenceSize, string shapeId)
        {
            int work = MaxFitWork, topologyWork = MaxTopologyWork;
            for (int attempt = 0; attempt < 9; attempt++, tolerance *= .5f)
            {
                var contours = new List<VectorContour>(source.Count);
                var flattened = new List<List<Vector2>>(source.Count);
                int total = 0, flatTotal = 0;
                for (int c = 0; c < source.Count; c++)
                {
                    var fitter = new BrushCurveFit(source[c], 1.5f * Mathf.Min(step.x, step.y), ref work);
                    List<BrushCubic> curves = fitter.Fit(tolerance, ref work);
                    total += curves.Count;
                    if (total > MaxOutputPoints) throw Complexity("Vector brush exceeds 8192 editable points. Increase tolerance or reduce source detail.");
                    var contour = new VectorContour { id = shapeId + ":c" + c, closed = true };
                    for (int p = 0; p < curves.Count; p++)
                    {
                        BrushCubic next = curves[p], previous = curves[(p + curves.Count - 1) % curves.Count];
                        Vector2 incoming = previous.c - next.a, outgoing = next.b - next.a;
                        bool aligned = incoming.sqrMagnitude > 0 && outgoing.sqrMagnitude > 0
                            && Vector2.Dot(incoming.normalized, outgoing.normalized) < -.9999f;
                        contour.points.Add(new VectorPoint
                        {
                            id = contour.id + ":p" + p, position = Normalize(next.a),
                            inTangent = Normalize(incoming), outTangent = Normalize(outgoing),
                            tangentMode = aligned ? VectorTangentMode.Aligned : VectorTangentMode.Corner
                        });
                    }
                    contours.Add(contour);
                    var polyline = new List<Vector2>();
                    VectorGeometry.FlattenContour(contour, referenceSize,
                        Mathf.Min(tolerance * .0625f, Mathf.Min(step.x, step.y) * .015625f), polyline);
                    flatTotal += polyline.Count;
                    if (flatTotal > MaxTracePoints) throw Complexity("Vector brush curve validation exceeds 131072 samples.");
                    flattened.Add(polyline);
                }
                // Check the CURVES, not only their anchors. Tighten from the original
                // trace when smoothing would cross a neighbor or change hole nesting.
                if (TopologyMatches(source, flattened, ref topologyWork)) return contours;
            }
            throw Complexity("Vector brush curve fitting could not preserve contour topology. Reduce source detail.");

            Vector2 Normalize(Vector2 p) => new Vector2(p.x / referenceSize.x, p.y / referenceSize.y);
        }

        struct BrushCubic
        {
            public Vector2 a, b, c, d;
            public Vector2 At(float t) => VectorGeometry.EvaluateCubic(a, b, c, d, t);
            public Vector2 Derivative(float t)
            {
                float s = 1 - t;
                return 3 * (s * s * (b - a) + 2 * s * t * (c - b) + t * t * (d - c));
            }
            public Vector2 SecondDerivative(float t) => 6 * ((1 - t) * (c - 2 * b + a) + t * (d - 2 * c + b));
        }

        sealed class BrushCurveFit
        {
            readonly Vector2[] points, tangents;
            readonly float[] parameters;
            readonly List<int> breaks = new List<int>();
            readonly int count;

            public BrushCurveFit(List<Vector2> loop, float support, ref int work)
            {
                count = loop.Count;
                points = new Vector2[count + 1]; tangents = new Vector2[count + 1];
                parameters = new float[count + 1];
                loop.CopyTo(points); points[count] = points[0];
                // Stable extrema prevent a degenerate closed fit and keep the seam
                // on the source. A cyclic tangent is shared by both sides of the seam.
                int left = 0, right = 0, bottom = 0, top = 0;
                float perimeter = 0;
                for (int i = 0; i < count; i++)
                {
                    if (points[i].x < points[left].x) left = i;
                    if (points[i].x > points[right].x) right = i;
                    if (points[i].y < points[bottom].y) bottom = i;
                    if (points[i].y > points[top].y) top = i;
                    perimeter += Vector2.Distance(points[i], points[i + 1]);
                }
                support = Mathf.Min(support, perimeter / 16);
                for (int i = 0; i < count; i++)
                {
                    Vector2 before = Along(i, -1, support, ref work), after = Along(i, 1, support, ref work);
                    tangents[i] = (after - before).normalized;
                    if (tangents[i].sqrMagnitude == 0) tangents[i] = (points[i + 1] - points[i]).normalized;
                }
                tangents[count] = tangents[0];
                breaks.Add(0); AddBreak(left); AddBreak(right); AddBreak(bottom); AddBreak(top);
                if (breaks.Count < 3) { AddBreak(count / 3); AddBreak(2 * count / 3); }
                breaks.Add(count); breaks.Sort();
            }

            void AddBreak(int value) { if (!breaks.Contains(value)) breaks.Add(value); }

            Vector2 Along(int start, int direction, float distance, ref int work)
            {
                Vector2 a = points[start];
                for (int k = 1; k <= count; k++)
                {
                    Tick(ref work);
                    Vector2 b = points[(start + direction * k + count) % count];
                    float length = Vector2.Distance(a, b);
                    if (length >= distance && length > 0) return Vector2.LerpUnclamped(a, b, distance / length);
                    distance -= length; a = b;
                }
                return a;
            }

            public List<BrushCubic> Fit(float tolerance, ref int work)
            {
                var output = new List<BrushCubic>();
                var pending = new Stack<Vector2Int>();
                for (int i = breaks.Count - 2; i >= 0; i--) pending.Push(new Vector2Int(breaks[i], breaks[i + 1]));
                double threshold = (double)tolerance * tolerance;
                while (pending.Count > 0)
                {
                    Vector2Int range = pending.Pop();
                    int first = range.x, last = range.y;
                    float length = Parameterize(first, last, ref work);
                    BrushCubic curve = Generate(first, last, length, ref work);
                    double error = Error(curve, first, last, out int split, ref work);
                    for (int iteration = 0; error > threshold && iteration < 4 && last - first > 1; iteration++)
                    {
                        if (!Refine(curve, first, last, ref work)) break;
                        BrushCubic next = Generate(first, last, length, ref work);
                        double nextError = Error(next, first, last, out int nextSplit, ref work);
                        curve = next; error = nextError; split = nextSplit;
                    }
                    if (last - first == 1)
                    {
                        // No hidden truncation: if even the shortest curved piece cannot
                        // meet the tolerance, keep this one original segment exactly.
                        if (error > threshold)
                        { curve.b = Vector2.Lerp(curve.a, curve.d, 1f / 3); curve.c = Vector2.Lerp(curve.a, curve.d, 2f / 3); }
                    }
                    else if (error > threshold)
                    {
                        split = Mathf.Clamp(split, first + 1, last - 1);
                        pending.Push(new Vector2Int(split, last)); pending.Push(new Vector2Int(first, split));
                        continue;
                    }
                    if (output.Count >= MaxOutputPoints) throw Complexity("Vector brush exceeds 8192 editable points. Increase tolerance or reduce source detail.");
                    output.Add(curve);
                }
                return output;
            }

            float Parameterize(int first, int last, ref int work)
            {
                parameters[first] = 0;
                for (int i = first + 1; i <= last; i++)
                { Tick(ref work); parameters[i] = parameters[i - 1] + Vector2.Distance(points[i], points[i - 1]); }
                float length = parameters[last];
                if (!(length > 0)) throw Complexity("Vector brush contains a zero-length boundary.");
                for (int i = first + 1; i < last; i++) parameters[i] /= length;
                parameters[last] = 1;
                return length;
            }

            BrushCubic Generate(int first, int last, float length, ref int work)
            {
                Vector2 a = points[first], d = points[last], chord = d - a;
                Vector2 forward = tangents[first], backward = -tangents[last];
                double aa = 0, ab = 0, bb = 0, ar = 0, br = 0;
                for (int i = first + 1; i < last; i++)
                {
                    Tick(ref work);
                    double t = parameters[i], s = 1 - t, b1 = 3 * s * s * t, b2 = 3 * s * t * t;
                    double ax = forward.x * b1, ay = forward.y * b1, bx = backward.x * b2, by = backward.y * b2;
                    double rx = (double)points[i].x - a.x - (b2 + t * t * t) * chord.x;
                    double ry = (double)points[i].y - a.y - (b2 + t * t * t) * chord.y;
                    aa += ax * ax + ay * ay; ab += ax * bx + ay * by; bb += bx * bx + by * by;
                    ar += ax * rx + ay * ry; br += bx * rx + by * ry;
                }
                double determinant = aa * bb - ab * ab;
                double left = length / 3.0, right = left;
                if (determinant > 1e-12 * aa * bb)
                { left = (ar * bb - br * ab) / determinant; right = (br * aa - ar * ab) / determinant; }
                // Negative or ill-conditioned handles cannot represent a regular span.
                if (!(left > length * 1e-6 && right > length * 1e-6 && left <= length * 2 && right <= length * 2))
                    left = right = chord.magnitude / 3.0;
                return new BrushCubic { a = a, b = a + (float)left * forward, c = d + (float)right * backward, d = d };
            }

            double Error(BrushCubic curve, int first, int last, out int split, ref int work)
            {
                double worst = 0;
                split = (first + last) / 2;
                Vector2 a = curve.a, da = curve.Derivative(0);
                for (int i = first; i < last; i++)
                {
                    Tick(ref work);
                    float t = parameters[i + 1], span = (t - parameters[i]) / 3;
                    Vector2 d = curve.At(t), dd = curve.Derivative(t);
                    // Exact cubic subspan minus its corresponding source line is a
                    // cubic too. Its control hull bounds the entire error, in both
                    // directions, rather than checking isolated sample positions.
                    double error = Math.Max(Squared(a, points[i]), Squared(d, points[i + 1]));
                    error = Math.Max(error, Squared(a + span * da, Vector2.Lerp(points[i], points[i + 1], 1f / 3)));
                    error = Math.Max(error, Squared(d - span * dd, Vector2.Lerp(points[i], points[i + 1], 2f / 3)));
                    if (error > worst) { worst = error; split = i + 1; }
                    a = d; da = dd;
                }
                return worst;
            }

            bool Refine(BrushCubic curve, int first, int last, ref int work)
            {
                bool changed = false;
                for (int i = first + 1; i < last; i++)
                {
                    Tick(ref work);
                    float t = parameters[i];
                    Vector2 delta = curve.At(t) - points[i], derivative = curve.Derivative(t);
                    double divisor = Vector2.Dot(derivative, derivative) + Vector2.Dot(delta, curve.SecondDerivative(t));
                    if (Math.Abs(divisor) < 1e-12) continue;
                    double next = t - Vector2.Dot(delta, derivative) / divisor;
                    if (!(next > parameters[i - 1] && next < parameters[i + 1])) continue;
                    float value = (float)next;
                    if (value <= parameters[i - 1] || value >= parameters[i + 1]) continue;
                    changed |= Math.Abs(value - t) > 1e-6f; parameters[i] = value;
                }
                return changed;
            }

            static void Tick(ref int work) => Spend(ref work, "Vector brush curve-fitting work limit exceeded. Reduce source detail.");
        }
    }
}
