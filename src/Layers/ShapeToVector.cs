using System;
using System.Collections.Generic;
using UnityEngine;
using static DCFApixels.WhimTex.ShapeLayerBehaviour;

namespace DCFApixels.WhimTex
{
    /// <summary>Geometric conversion; the caller replaces the behaviour on the existing layer.</summary>
    public static class ShapeToVector
    {
        /// <summary>
        /// Returns independent editable geometry without replacing the source or changing its layer.
        /// Arcs use cubic approximations. Unsupported appearance is reported before replacement.
        /// </summary>
        public static VectorLayerBehaviour Convert(ShapeLayerBehaviour source, WhimTexDocument document)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.width <= 0 || document.height <= 0) throw new ArgumentException("The document must have a positive size.", nameof(document));
            if (!Enum.IsDefined(typeof(ShapeKind), source.kind)) throw Unsupported("unknown Shape kind");
            if (source.edgeMode != EdgeMode.Step && Limit(source.feather, 0, 8192, 0) > 0)
                throw Unsupported("Feather is not supported by Vector; set Feather to zero first");
            bool stroked = source.stroke && source.strokeMode != VectorFillMode.None && Limit(source.strokeWidth, 0, 8192, 2) > 0;
            bool translucentStroke = source.strokeColor.a < 1;
            if (FigureFill.IsPaletteGradient(source.strokeMode))
            {
                translucentStroke = source.strokeGradient != null && !source.strokeGradient.HasOpaqueAlpha;
            }
            if (source.strokeMode == VectorFillMode.Freeform)
            {
                translucentStroke = false;
                if (source.strokeFreeform != null) foreach (var point in source.strokeFreeform.points)
                    if (point.color.a < 1) { translucentStroke = true; break; }
            }
            if (stroked && source.fill && source.fillMode != VectorFillMode.None && source.strokePosition != StrokePosition.Outside && translucentStroke)
                throw Unsupported("overlapping translucent Stroke replaces Shape fill, but Vector blends over it");

            Vector2 half = source.GeometryHalfSize(document);
            float scaleX = 2 * half.x / document.width, scaleY = 2 * half.y / document.height;
            if (!float.IsFinite(half.x) || !float.IsFinite(half.y) || scaleX < 1e-7f || scaleY < 1e-7f)
                throw Unsupported("degenerate layer transform");
            if (stroked && (!UniformTransform(source, document) || Mathf.Abs(scaleX - scaleY) > Mathf.Max(scaleX, scaleY) * 1e-5f))
                throw Unsupported("canvas-pixel Stroke under a nonuniform or projective transform cannot be represented by a single Vector stroke width");

            var result = new VectorLayerBehaviour
            {
                referenceSize = new Vector2(document.width, document.height),
                edgeMode = source.edgeMode == EdgeMode.Step ? VectorEdgeMode.Step : VectorEdgeMode.Antialiased
            };
            var shape = new VectorShape
            {
                name = source.kind.ToString(), fillMode = source.fill ? source.fillMode : VectorFillMode.None,
                fillColor = source.fillColor, stroke = source.stroke, strokeColor = source.strokeColor,
                gradient = source.gradient?.Clone(), gradientStart = source.gradientStart, gradientEnd = source.gradientEnd,
                gradientRepetitions = source.gradientRepetitions,
                freeform = source.freeform?.Clone(), strokeFreeform = source.strokeFreeform?.Clone(),
                strokeMode = source.strokeMode, strokeGradient = source.strokeGradient?.Clone(),
                strokeGradientStart = source.strokeGradientStart, strokeGradientEnd = source.strokeGradientEnd,
                strokeGradientRepetitions = source.strokeGradientRepetitions,
                strokeWidth = Limit(source.strokeWidth, 0, 8192, 2) * document.GetPixelScale() / scaleX,
                strokePosition = source.strokePosition == StrokePosition.Inside ? VectorStrokePosition.Inside :
                    source.strokePosition == StrokePosition.Outside ? VectorStrokePosition.Outside : VectorStrokePosition.Center,
                join = source.strokeJoin, cap = source.strokeCap, miterLimit = source.miterLimit,
                dashPattern = source.dashPattern == null ? new List<float>() : new List<float>(source.dashPattern),
                dashPatternRelative = true, dashOffset = source.dashOffset * document.GetPixelScale() / scaleX,
                fillRule = VectorFillRule.EvenOdd
            };
            result.shapes.Add(shape);
            shape.contours = GeometryContours(source, half, document.GetPixelScale());
            try { result.Validate(); }
            catch (InvalidOperationException error) { throw Unsupported(error.Message); }
            return result;
        }

        internal static List<VectorContour> GeometryContours(ShapeLayerBehaviour source, Vector2 half, float pixelScale)
        {
            var shape = new VectorShape();
            var path = new Path(half);
            switch (source.kind)
            {
                case ShapeKind.Ellipse:
                    path.Arc(Vector2.zero, half, 0, Mathf.PI * 2);
                    shape.contours.Add(path.Close());
                    break;
                case ShapeKind.Line:
                    LineBody(path, half, source.lineCap);
                    shape.contours.Add(path.Close());
                    break;
                case ShapeKind.Arc:
                    ArcBody(shape, half, source, pixelScale);
                    break;
                default:
                    // EnsureContour normalizes corners. Work on a copy to keep conversion atomic
                    // even when the source has not been normalized by its inspector yet.
                    var geometry = CopyGeometrySettings(source);
                    var segments = new List<Vector4>();
                    var curves = new List<Vector4>();
                    geometry.Contour.CopyGeometry(geometry, half, segments, curves);
                    for (int i = 0; i < segments.Count; i++)
                    {
                        Vector4 s = segments[i], c = curves[i];
                        if (c.z == 0) path.Line(new Vector2(s.x, s.y), new Vector2(s.z, s.w));
                        else path.Arc(new Vector2(s.x, s.y), new Vector2(s.z, s.w), c.x, c.y);
                    }
                    if (path.Count > 0) shape.contours.Add(path.Close());
                    break;
            }
            return shape.contours;
        }

        private static NotSupportedException Unsupported(string reason) => new NotSupportedException("Shape to Vector: " + reason + ".");

        private static bool UniformTransform(ShapeLayerBehaviour source, WhimTexDocument document)
        {
            ProjectiveMatrix m = source.Owner.RenderTransform.ToMatrix(document.width, document.height);
            if (!m.IsFinite || Math.Abs(m.m20) > 1e-10 || Math.Abs(m.m21) > 1e-10 || Math.Abs(m.m22) < 1e-10) return false;
            double ax = m.m00, ay = m.m10 * document.height / document.width;
            double bx = m.m01 * document.width / document.height, by = m.m11;
            double a = ax * ax + ay * ay, b = bx * bx + by * by;
            return Math.Abs(a - b) <= Math.Max(a, b) * 1e-5 && Math.Abs(ax * bx + ay * by) <= Math.Sqrt(a * b) * 1e-5;
        }

        private static ShapeLayerBehaviour CopyGeometrySettings(ShapeLayerBehaviour source) => new ShapeLayerBehaviour
        {
            kind = source.kind, sides = source.sides, innerRadius = source.innerRadius,
            startAngle = source.startAngle, sweepAngle = source.sweepAngle,
            rectangleCorners = source.rectangleCorners == null ? null : (Corner[])source.rectangleCorners.Clone(),
            polygonCorners = source.polygonCorners == null ? null : (Corner[])source.polygonCorners.Clone(),
            outerCorner = source.outerCorner, innerCorner = source.innerCorner
        };

        private static void LineBody(Path path, Vector2 half, LineCap cap)
        {
            float x = half.x + (cap == LineCap.Square ? half.y : 0), y = half.y;
            path.Line(new Vector2(-x, -y), new Vector2(x, -y));
            if (cap == LineCap.Round) path.Arc(new Vector2(x, 0), Vector2.one * y, -Mathf.PI * .5f, Mathf.PI);
            else path.Line(new Vector2(x, -y), new Vector2(x, y));
            path.Line(new Vector2(x, y), new Vector2(-x, y));
            if (cap == LineCap.Round) path.Arc(new Vector2(-x, 0), Vector2.one * y, Mathf.PI * .5f, Mathf.PI);
            else path.Line(new Vector2(-x, y), new Vector2(-x, -y));
        }

        private static void ArcBody(VectorShape shape, Vector2 axes, ShapeLayerBehaviour source, float pixelScale)
        {
            float sweep = Limit(source.sweepAngle, 0, 360, 90) * Mathf.Deg2Rad;
            float radius = Limit(source.arcThickness, 0, 8192, 8) * pixelScale * .5f;
            if (sweep < 1e-5f || radius <= 0) return;
            float start = Limit(source.startAngle, -360000, 360000, 0) * Mathf.Deg2Rad;
            bool full = sweep >= Mathf.PI * 2 - 1e-5f;
            // An inward offset past the minimum radius of curvature develops cusps and
            // overlapping regions; those require a separate geometric union operation.
            float minor = Mathf.Min(axes.x, axes.y), major = Mathf.Max(axes.x, axes.y);
            if (radius >= minor * minor / major)
                throw Unsupported("Arc Thickness reaches an offset cusp; this thick arc needs geometric union before conversion");
            if (!full && sweep > Mathf.PI && source.lineCap != LineCap.Butt &&
                Vector2.Distance(Ellipse(axes, start), Ellipse(axes, start + sweep)) <=
                radius * (source.lineCap == LineCap.Square ? 2.8284272f : 2))
                throw Unsupported("overlapping Arc end caps need geometric union before conversion");
            var path = new Path(axes);
            OffsetArc(path, axes, radius, start, sweep);
            if (full)
            {
                shape.contours.Add(path.Close());
                path = new Path(axes);
                OffsetArc(path, axes, -radius, start + sweep, -sweep);
                shape.contours.Add(path.Close());
                return;
            }
            ArcCap(path, axes, radius, start + sweep, source.lineCap, false);
            OffsetArc(path, axes, -radius, start + sweep, -sweep);
            ArcCap(path, axes, radius, start, source.lineCap, true);
            shape.contours.Add(path.Close());
        }

        private static Vector2 Ellipse(Vector2 axes, float t) => new Vector2(axes.x * Mathf.Cos(t), axes.y * Mathf.Sin(t));
        private static Vector2 Normal(Vector2 axes, float t) => new Vector2(Mathf.Cos(t) / axes.x, Mathf.Sin(t) / axes.y).normalized;
        private static void ArcCap(Path path, Vector2 axes, float radius, float t, LineCap cap, bool atStart)
        {
            Vector2 center = Ellipse(axes, t), normal = Normal(axes, t);
            Vector2 a = center + normal * (atStart ? -radius : radius), b = center + normal * (atStart ? radius : -radius);
            if (cap == LineCap.Round)
                path.Arc(center, Vector2.one * radius, Mathf.Atan2(a.y - center.y, a.x - center.x), Mathf.PI);
            else if (cap == LineCap.Square)
            {
                Vector2 extension = new Vector2(-normal.y, normal.x) * (atStart ? -radius : radius);
                path.Line(a, a + extension); path.Line(a + extension, b + extension); path.Line(b + extension, b);
            }
            else path.Line(a, b);
        }

        private static void OffsetPoint(Vector2 axes, float radius, float t, out Vector2 p, out Vector2 derivative)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            Vector2 v = new Vector2(c / axes.x, s / axes.y), dv = new Vector2(-s / axes.x, c / axes.y);
            float length = v.magnitude;
            Vector2 n = v / length;
            p = new Vector2(axes.x * c, axes.y * s) + radius * n;
            derivative = new Vector2(-axes.x * s, axes.y * c) + radius * (dv - n * Vector2.Dot(n, dv)) / length;
        }

        private static void OffsetArc(Path path, Vector2 axes, float radius, float start, float sweep)
        {
            int pieces = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep) / (Mathf.PI / 8)));
            for (int i = 0; i < pieces; i++) OffsetPiece(path, axes, radius, start + sweep * i / pieces, start + sweep * (i + 1) / pieces, 0);
        }

        private static void OffsetPiece(Path path, Vector2 axes, float radius, float start, float end, int depth)
        {
            OffsetPoint(axes, radius, start, out var a, out var da);
            OffsetPoint(axes, radius, end, out var b, out var db);
            Vector2 c = a + da * ((end - start) / 3), d = b - db * ((end - start) / 3);
            bool split = false;
            for (int i = 1; i < 4; i++)
            {
                float t = i * .25f, u = 1 - t;
                OffsetPoint(axes, radius, Mathf.Lerp(start, end, t), out var expected, out _);
                Vector2 actual = u * u * u * a + 3 * u * u * t * c + 3 * u * t * t * d + t * t * t * b;
                split |= (expected - actual).sqrMagnitude > .0001f;
            }
            if (split)
            {
                if (depth >= 16) throw Unsupported("elliptic arc approximation exceeds the 0.01 pixel sampling tolerance");
                float mid = (start + end) * .5f;
                OffsetPiece(path, axes, radius, start, mid, depth + 1);
                OffsetPiece(path, axes, radius, mid, end, depth + 1);
            }
            else path.Cubic(a, c, d, b);
        }

        private sealed class Path
        {
            private readonly Vector2 half;
            private readonly VectorContour contour = new VectorContour();
            internal int Count => contour.points.Count;
            internal Path(Vector2 half) { this.half = half; }
            private Vector2 Position(Vector2 p) => new Vector2(.5f + p.x / (2 * half.x), .5f + p.y / (2 * half.y));
            private Vector2 Offset(Vector2 p) => new Vector2(p.x / (2 * half.x), p.y / (2 * half.y));
            internal void Line(Vector2 a, Vector2 b) => Cubic(a, a, b, b);
            internal void Cubic(Vector2 a, Vector2 c, Vector2 d, Vector2 b)
            {
                if (contour.points.Count == 0) contour.points.Add(new VectorPoint { position = Position(a) });
                var previous = contour.points[contour.points.Count - 1];
                previous.outTangent = Offset(c - a);
                contour.points.Add(new VectorPoint { position = Position(b), inTangent = Offset(d - b) });
            }
            internal void Arc(Vector2 center, Vector2 axes, float start, float sweep)
            {
                int pieces = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep) / (Mathf.PI / 8)));
                for (int i = 0; i < pieces; i++)
                {
                    float a = start + sweep * i / pieces, b = start + sweep * (i + 1) / pieces;
                    float k = 4f / 3f * Mathf.Tan((b - a) * .25f);
                    Vector2 p = center + Ellipse(axes, a), q = center + Ellipse(axes, b);
                    Cubic(p, p + new Vector2(-axes.x * Mathf.Sin(a), axes.y * Mathf.Cos(a)) * k,
                        q - new Vector2(-axes.x * Mathf.Sin(b), axes.y * Mathf.Cos(b)) * k, q);
                }
            }
            internal VectorContour Close()
            {
                var points = contour.points;
                if (points.Count > 1 && (points[0].position - points[points.Count - 1].position).sqrMagnitude < 1e-10f)
                {
                    points[0].inTangent = points[points.Count - 1].inTangent;
                    points.RemoveAt(points.Count - 1);
                }
                contour.closed = true;
                return contour;
            }
        }
    }
}
