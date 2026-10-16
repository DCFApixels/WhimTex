using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>Data-only cubic editing and bounded flattening. No Unity objects are accessed.</summary>
    public static class VectorGeometry
    {
        public const int MaxShapes = 4096, MaxContours = 8192, MaxPoints = 65536;
        public const int MaxFlattenedPoints = 262144;
        public const float MaxCoordinate = 10000000;

        public static Vector2 EvaluateCubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        /// <summary>Splits one cubic exactly; existing IDs survive and the inserted point gets a new ID.</summary>
        public static VectorPoint SplitSegment(VectorContour contour, int segmentIndex, float t)
        {
            if (contour?.points == null) throw new ArgumentNullException(nameof(contour));
            int count = contour.points.Count;
            if (segmentIndex < 0 || segmentIndex >= count - (contour.closed ? 0 : 1) || count < 2)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            if (!Finite(t) || t <= 0 || t >= 1) throw new ArgumentOutOfRangeException(nameof(t));
            if (count >= MaxPoints) throw Limit("anchor count");
            var a = contour.points[segmentIndex];
            var b = contour.points[(segmentIndex + 1) % count];
            ValidatePoint(a); ValidatePoint(b);
            Vector2 p0 = a.position, p1 = p0 + a.outTangent, p3 = b.position, p2 = p3 + b.inTangent;
            Vector2 q0 = Vector2.LerpUnclamped(p0, p1, t), q1 = Vector2.LerpUnclamped(p1, p2, t), q2 = Vector2.LerpUnclamped(p2, p3, t);
            Vector2 r0 = Vector2.LerpUnclamped(q0, q1, t), r1 = Vector2.LerpUnclamped(q1, q2, t);
            var inserted = new VectorPoint(Vector2.LerpUnclamped(r0, r1, t)) { tangentMode = VectorTangentMode.Aligned };
            inserted.inTangent = r0 - inserted.position; inserted.outTangent = r1 - inserted.position;
            ValidatePoint(inserted);
            if (!Finite(q0 - p0) || !Finite(q2 - p3)) throw Limit("split coordinate range");
            a.outTangent = q0 - p0; b.inTangent = q2 - p3;
            // Splitting changes only the handle on this segment. Preserve opposite handles.
            if (a.tangentMode == VectorTangentMode.Mirrored) a.tangentMode = VectorTangentMode.Aligned;
            if (b.tangentMode == VectorTangentMode.Mirrored) b.tangentMode = VectorTangentMode.Aligned;
            contour.points.Insert(segmentIndex + 1, inserted);
            return inserted;
        }

        /// <summary>Replaces output with reference-pixel coordinates; closed output has no repeated last point.</summary>
        public static void FlattenContour(VectorContour contour, Vector2 referenceSize, float tolerance, List<Vector2> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            ValidateReferenceSize(referenceSize);
            if (!Finite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            if (contour?.points == null) throw new ArgumentNullException(nameof(contour));
            if (contour.points.Count > MaxPoints) throw Limit("anchor count");
            output.Clear();
            foreach (var p in contour.points) ValidatePoint(p);
            if (contour.points.Count == 0) return;
            Add(output, Vector2.Scale(contour.points[0].position, referenceSize));
            int segments = contour.points.Count - (contour.closed ? 0 : 1);
            for (int i = 0; i < segments; i++)
            {
                var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count];
                Flatten(Vector2.Scale(a.position, referenceSize), Vector2.Scale(a.position + a.outTangent, referenceSize),
                    Vector2.Scale(b.position + b.inTangent, referenceSize), Vector2.Scale(b.position, referenceSize),
                    tolerance * tolerance, 0, output);
            }
            if (contour.closed && output.Count > 1 && output[0] == output[output.Count - 1]) output.RemoveAt(output.Count - 1);
        }

        private static void Flatten(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float toleranceSquared, int depth, List<Vector2> output)
        {
            // Distance to the segment (not its infinite line) also detects collinear reversals/cusps.
            if (Mathf.Max(DistanceSquared(b, a, d, out _), DistanceSquared(c, a, d, out _)) <= toleranceSquared)
            { Add(output, d); return; }
            if (depth >= 24) throw Limit("cubic subdivision depth");
            Vector2 ab = (a + b) * .5f, bc = (b + c) * .5f, cd = (c + d) * .5f;
            Vector2 abc = (ab + bc) * .5f, bcd = (bc + cd) * .5f, mid = (abc + bcd) * .5f;
            Flatten(a, ab, abc, mid, toleranceSquared, depth + 1, output);
            Flatten(mid, bcd, cd, d, toleranceSquared, depth + 1, output);
        }

        /// <summary>Returns a closest cubic parameter within tolerance pixels, suitable for insertion handles.</summary>
        public static bool TryHitSegment(VectorContour contour, Vector2 point, Vector2 referenceSize,
            float tolerance, out int segmentIndex, out float t, out float distance)
        {
            ValidateReferenceSize(referenceSize);
            if (!Finite(point) || !Finite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            segmentIndex = -1; t = 0; distance = float.PositiveInfinity;
            if (contour?.points == null || contour.points.Count < 2) return false;
            if (contour.points.Count > MaxPoints) throw Limit("anchor count");
            foreach (var p in contour.points) ValidatePoint(p);
            point = Vector2.Scale(point, referenceSize);
            float best = tolerance * tolerance;
            int budget = MaxFlattenedPoints;
            for (int i = 0; i < contour.points.Count - (contour.closed ? 0 : 1); i++)
            {
                var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count];
                float candidate = 0; bool hit = Hit(point, Vector2.Scale(a.position, referenceSize),
                    Vector2.Scale(a.position + a.outTangent, referenceSize), Vector2.Scale(b.position + b.inTangent, referenceSize),
                    Vector2.Scale(b.position, referenceSize), 0, 1, 0, ref best, ref candidate, ref budget);
                if (hit) { segmentIndex = i; t = candidate; }
            }
            if (segmentIndex < 0) return false;
            distance = Mathf.Sqrt(best); return true;
        }

        private static bool Hit(Vector2 p, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t0, float t1,
            int depth, ref float best, ref float t, ref int budget)
        {
            if (--budget < 0) throw Limit("hit-test subdivision count");
            float xmin = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            float xmax = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float ymin = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            float ymax = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            var closest = new Vector2(Mathf.Clamp(p.x, xmin, xmax), Mathf.Clamp(p.y, ymin, ymax));
            if ((closest - p).sqrMagnitude > best) return false;
            // A straight cubic can have nonlinear speed. Bound parameterization error as well as
            // geometric flatness, otherwise inserting on a zero-handle segment lands at the wrong t.
            float error = Mathf.Max((b - Vector2.LerpUnclamped(a, d, 1f / 3)).sqrMagnitude,
                (c - Vector2.LerpUnclamped(a, d, 2f / 3)).sqrMagnitude);
            if (error < .0001f)
            {
                float squared = DistanceSquared(p, a, d, out float along);
                if (squared > best) return false;
                best = squared; t = Mathf.Lerp(t0, t1, along); return true;
            }
            if (depth >= 24) throw Limit("hit-test subdivision depth");
            Vector2 ab = (a + b) * .5f, bc = (b + c) * .5f, cd = (c + d) * .5f;
            Vector2 abc = (ab + bc) * .5f, bcd = (bc + cd) * .5f, mid = (abc + bcd) * .5f;
            float tm = (t0 + t1) * .5f;
            bool left = Hit(p, a, ab, abc, mid, t0, tm, depth + 1, ref best, ref t, ref budget);
            bool right = Hit(p, mid, bcd, cd, d, tm, t1, depth + 1, ref best, ref t, ref budget);
            return left || right;
        }

        internal static float DistanceSquared(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a; float length = ab.sqrMagnitude;
            t = length > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / length) : 0;
            return (p - (a + t * ab)).sqrMagnitude;
        }
        private static void Add(List<Vector2> output, Vector2 p)
        {
            if (!Finite(p) || Math.Abs(p.x) > MaxCoordinate || Math.Abs(p.y) > MaxCoordinate) throw Limit("coordinate range");
            if (output.Count >= MaxFlattenedPoints) throw Limit("flattened point count");
            if (output.Count == 0 || output[output.Count - 1] != p) output.Add(p);
        }
        internal static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        internal static bool Finite(Vector2 p) => Finite(p.x) && Finite(p.y);
        internal static void ValidatePoint(VectorPoint p)
        {
            if (p == null || !Finite(p.position) || !Finite(p.inTangent) || !Finite(p.outTangent) ||
                !Finite(p.position + p.inTangent) || !Finite(p.position + p.outTangent))
                throw new InvalidOperationException("Vector point and tangent coordinates must be finite.");
        }
        internal static void ValidateReferenceSize(Vector2 size)
        {
            if (!Finite(size) || size.x <= 0 || size.y <= 0 || size.x > MaxCoordinate || size.y > MaxCoordinate)
                throw new InvalidOperationException("Vector referenceSize must contain finite positive pixel dimensions.");
        }
        internal static InvalidOperationException Limit(string resource) => new InvalidOperationException(
            "Vector " + resource + " exceeds the supported resource limit. Geometry was not truncated; simplify the source or reduce output size.");
    }
}
