using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class MorphVectorGeometry
    {
        // The editable Vector layer contract is one styled compound figure. Bound the
        // assignment problem separately from Vector's much larger individual path limit.
        private const int MaxMorphContours = 256;

        /// <summary>
        /// Builds independent cubic paths in normalized, y-up group UV using only each
        /// child's local transform. The caller owns layer opacity, compositing and FX.
        /// Returns null for unsupported geometry, paint, limits or inconsistent anchors
        /// so the caller can use its raster fallback. Finite t is clamped to [0,1].
        /// </summary>
        internal static VectorLayerBehaviour Build(Layer a, Layer b, Vector2 canvasSize,
            IReadOnlyList<MorphAnchor> anchors, float t)
            => Prepare(a, b, canvasSize, anchors)?.Sample(t);

        internal static Prepared Prepare(Layer a, Layer b, Vector2 canvasSize, IReadOnlyList<MorphAnchor> anchors)
        {
            try
            {
                VectorGeometry.ValidateReferenceSize(canvasSize);
                var from = Read(a, canvasSize);
                var to = Read(b, canvasSize);
                if (from == null || to == null) return null;
                var paths = Match(from.shapes[0], to.shapes[0], canvasSize, anchors);
                return paths == null ? null : new Prepared(from, to, paths);
            }
            catch (NotSupportedException) { return null; } // Includes ShapeToVector offset cusps.
            catch (InvalidOperationException) { return null; } // Vector validation and geometry budgets.
            catch (ArgumentException) { return null; } // Invalid authored gradient/coordinate data.
        }

        internal sealed class Prepared
        {
            private readonly VectorLayerBehaviour from, to;
            private readonly List<ContourPair> paths;
            internal Prepared(VectorLayerBehaviour from, VectorLayerBehaviour to, List<ContourPair> paths)
            { this.from = from; this.to = to; this.paths = paths; }

            internal VectorLayerBehaviour Sample(float t)
            {
                if (!VectorGeometry.Finite(t)) return null;
                t = Mathf.Clamp01(t);
                var nearest = t < .5f ? from : to;
                var model = new VectorLayerBehaviour { referenceSize = nearest.referenceSize, edgeMode = nearest.edgeMode,
                    featherPosition = nearest.featherPosition, feather = Mathf.Lerp(from.feather, to.feather, t) };
                if (t == 0 || t == 1) { model.shapes.Add(nearest.shapes[0].Clone()); return model; }
                try
                {
                    var shape = BlendStyle(from.shapes[0], to.shapes[0], t);
                    if (shape == null) return null;
                    shape.id = "morph";
                    foreach (var path in paths) shape.contours.Add(path.Sample(t, shape.fillRule));
                    model.shapes.Add(shape);
                    model.Validate();
                    return model;
                }
                catch (ArgumentException) { return null; }
                catch (InvalidOperationException) { return null; }
            }
        }

        private static VectorLayerBehaviour Read(Layer layer, Vector2 size)
        {
            if (layer == null || layer.EffectiveTiling != TransformTilingMode.Unbounded) return null;
            var matrix = layer.transform.ToMatrix(size.x, size.y);
            // A perspective image of a polynomial cubic is generally a rational cubic.
            if (!matrix.IsFinite || matrix.m20 != 0 || matrix.m21 != 0 || !matrix.TryInverse(out _)) return null;
            matrix = matrix.Divide(matrix.m22);
            var model = new VectorLayerBehaviour { referenceSize = size };
            VectorShape shape;
            Vector2 reference;
            if (layer.Behaviour is VectorLayerBehaviour vector)
            {
                vector.Validate();
                if (vector.PreparedContours != null || vector.shapes.Count > 1) return null;
                shape = vector.shapes.Count == 0 ? new VectorShape() : vector.shapes[0].Clone();
                reference = vector.referenceSize;
                double scale = PixelScale(matrix, reference, size, out bool uniform);
                if (!uniform && ((shape.stroke && shape.strokeMode != VectorFillMode.None && shape.strokeWidth > 0) ||
                    (vector.edgeMode != VectorEdgeMode.Step && vector.feather > 0))) return null;
                shape.strokeWidth *= (float)scale;
                shape.dashOffset *= (float)scale;
                if (!shape.dashPatternRelative && shape.dashPattern != null)
                    for (int i = 0; i < shape.dashPattern.Count; i++) shape.dashPattern[i] *= (float)scale;
                model.edgeMode = vector.edgeMode;
                model.feather = vector.feather * (float)scale;
                model.featherPosition = vector.featherPosition;
            }
            else if (layer.Behaviour is ShapeLayerBehaviour primitive)
            {
                if (!Enum.IsDefined(typeof(ShapeLayerBehaviour.ShapeKind), primitive.kind)) return null;
                reference = size;
                layer.transform.GetDisplay(size, out _, out var scale, out _);
                Vector2 half = Vector2.Scale(size, new Vector2((float)Math.Abs(scale.x), (float)Math.Abs(scale.y))) * .5f;
                if (half.x < 1e-5f || half.y < 1e-5f) return null;
                float pixels = layer.GetPixelScale();
                var stroke = StrokeStyle.From(primitive, pixels);
                PixelScale(matrix, 2 * half, size, out bool uniformShapeMetric);
                if (!uniformShapeMetric && ((primitive.stroke && primitive.strokeMode != VectorFillMode.None && stroke.strokeWidth > 0) ||
                    (primitive.edgeMode != ShapeLayerBehaviour.EdgeMode.Step && primitive.feather > 0))) return null;
                shape = new VectorShape
                {
                    name = primitive.kind.ToString(), contours = ShapeToVector.GeometryContours(primitive, half, pixels),
                    fillMode = primitive.fill ? primitive.fillMode : VectorFillMode.None, fillColor = primitive.fillColor,
                    gradient = primitive.gradient?.Clone(), freeform = primitive.freeform?.Clone(),
                    gradientStart = primitive.gradientStart, gradientEnd = primitive.gradientEnd,
                    gradientRepetitions = primitive.gradientRepetitions,
                    stroke = primitive.stroke, strokeMode = primitive.strokeMode, strokeColor = primitive.strokeColor,
                    strokeGradient = primitive.strokeGradient?.Clone(), strokeFreeform = primitive.strokeFreeform?.Clone(),
                    strokeGradientStart = primitive.strokeGradientStart, strokeGradientEnd = primitive.strokeGradientEnd,
                    strokeGradientRepetitions = primitive.strokeGradientRepetitions, strokeWidth = stroke.strokeWidth,
                    strokePosition = stroke.strokePosition, join = stroke.join, cap = stroke.cap, miterLimit = stroke.miterLimit,
                    dashPatternRelative = true, dashOffset = stroke.dashOffset,
                    dashPattern = stroke.dashPattern == null ? null : new List<float>(stroke.dashPattern)
                };
                if (shape.stroke && shape.strokeWidth > 0 && shape.strokeMode != VectorFillMode.None &&
                    shape.fillMode != VectorFillMode.None && shape.strokePosition != VectorStrokePosition.Outside)
                {
                    bool translucent = shape.strokeMode == VectorFillMode.Freeform
                        ? shape.strokeFreeform?.points == null || shape.strokeFreeform.points.Exists(p => p.color.a < 1)
                        : FigureFill.IsPaletteGradient(shape.strokeMode) ? shape.strokeGradient?.HasOpaqueAlpha != true :
                        shape.strokeColor.a < 1;
                    if (translucent) return null; // Shape replaces overlapping fill. Vector composites ink over it.
                }
                model.edgeMode = (VectorEdgeMode)primitive.edgeMode;
                model.feather = ShapeLayerBehaviour.Limit(primitive.feather, 0, 8192, 0) * pixels;
                model.featherPosition = (VectorFeatherPosition)primitive.featherPosition;
            }
            else return null;

            if (!RebasePaint(shape, matrix, reference, size)) return null;
            foreach (var contour in shape.contours)
                foreach (var point in contour.points)
                {
                    point.position = matrix.Point(point.position);
                    point.inTangent = Direction(matrix, point.inTangent);
                    point.outTangent = Direction(matrix, point.outTangent);
                }
            model.shapes.Add(shape);
            model.Validate();
            return model;
        }

        private static Vector2 Direction(ProjectiveMatrix m, Vector2 v) => new Vector2(
            (float)(m.m00 * v.x + m.m01 * v.y), (float)(m.m10 * v.x + m.m11 * v.y));

        // Jacobian from source-reference pixels to group pixels.
        private static double PixelScale(ProjectiveMatrix m, Vector2 reference, Vector2 size, out bool uniform)
        {
            double x = m.m00 * size.x / reference.x, y = m.m10 * size.y / reference.x;
            double u = m.m01 * size.x / reference.y, v = m.m11 * size.y / reference.y;
            double a = x * x + y * y, b = u * u + v * v;
            uniform = Math.Abs(a - b) <= 1e-5 * Math.Max(a, b) && Math.Abs(x*u + y*v) <= 1e-5 * Math.Sqrt(a*b);
            return Math.Sqrt(Math.Abs(x*v - y*u));
        }
    }
}
