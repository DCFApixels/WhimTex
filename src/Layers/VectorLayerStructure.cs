using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static class VectorLayerStructure
    {
        // Keep the old shell (including ID, FX and transform) as the common group.
        public static void Normalize(List<Layer> layers, int width, int height)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer?.Behaviour is VectorLayerBehaviour vector)
                {
                    if (vector.shapes?.Count > 1)
                    {
                        vector.Validate();
                        var children = new List<Layer>();
                        for (int i = vector.shapes.Count - 1; i >= 0; i--)
                        {
                            var shape = vector.shapes[i];
                            var child = new Layer(new VectorLayerBehaviour { referenceSize = vector.referenceSize,
                                edgeMode = vector.edgeMode, feather = vector.feather, featherPosition = vector.featherPosition,
                                shapes = new List<VectorShape> { shape },
                                brushDraft = vector.brushDraft?.shapeId == shape.id ? vector.brushDraft : null })
                                { layerName = shape.name, colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR, filterMode = layer.filterMode };
                            child.transform = TextureTransform.Default;
                            child.transform.tiling = layer.transform.tiling;
                            children.Add(child);
                        }
                        layer.SetBehaviour(new GroupLayerBehaviour());
                        layer.compositing = GroupCompositing.Isolated;
                        layer.children = children;
                    }
                    else FitFrame(layer, width, height);
                }
                if (layer?.IsGroup == true) Normalize(layer.children, width, height);
            }
        }

        /// <summary>Rebase geometry into its exact cubic bounds without moving its rendered contents.</summary>
        public static void FitFrame(Layer layer, int width, int height)
        {
            if (layer?.Behaviour is not VectorLayerBehaviour vector || vector.shapes?.Count != 1) return;
            // Clipped/repeating sources have a meaningful image frame: do not change that crop.
            if (layer.EffectiveTiling != TransformTilingMode.Unbounded) return;
            Rect bounds = Bounds(vector.shapes[0]);
            if (!VectorGeometry.Finite(bounds.min) || !VectorGeometry.Finite(bounds.max)) return;
            var size = bounds.size;
            size.x = Mathf.Max(size.x, 1f / Mathf.Max(1, vector.referenceSize.x));
            size.y = Mathf.Max(size.y, 1f / Mathf.Max(1, vector.referenceSize.y));
            if (bounds.min.sqrMagnitude < 1e-12f && (size - Vector2.one).sqrMagnitude < 1e-12f) return;
            Vector2 nextReference = Vector2.Scale(vector.referenceSize, size);
            VectorGeometry.ValidateReferenceSize(nextReference);
            var next = layer.transform;
            if (!next.TrySetMatrix(layer.transform.ToMatrix(width, height) *
                ProjectiveMatrix.Translate(bounds.xMin, bounds.yMin) * ProjectiveMatrix.Scale(size.x, size.y)))
                throw new InvalidOperationException("Cannot fit vector frame through a singular transform.");
            Vector2 Map(Vector2 point) => new((point.x - bounds.xMin) / size.x, (point.y - bounds.yMin) / size.y);
            foreach (var contour in vector.shapes[0].contours) foreach (var point in contour.points)
            {
                point.position = Map(point.position);
                point.inTangent = new Vector2(point.inTangent.x / size.x, point.inTangent.y / size.y);
                point.outTangent = new Vector2(point.outTangent.x / size.x, point.outTangent.y / size.y);
            }
            var shape = vector.shapes[0];
            shape.gradientStart = Map(shape.gradientStart); shape.gradientEnd = Map(shape.gradientEnd);
            shape.strokeGradientStart = Map(shape.strokeGradientStart); shape.strokeGradientEnd = Map(shape.strokeGradientEnd);
            if (vector.brushDraft != null)
            {
                var r = vector.brushDraft.frame;
                vector.brushDraft.frame = new Rect(Map(r.min), new Vector2(r.width / size.x, r.height / size.y));
            }
            vector.referenceSize = nextReference; layer.transform = next;
        }

        public static Rect Bounds(VectorShape shape)
        {
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            void Include(Vector2 p) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            foreach (var contour in shape.contours)
            {
                foreach (var p in contour.points) Include(p.position);
                int count = contour.points.Count;
                for (int i = 0; i < count - (contour.closed ? 0 : 1); i++)
                {
                    var a = contour.points[i]; var b = contour.points[(i + 1) % count];
                    Vector2 p = a.position, q = p + a.outTangent, r = b.position + b.inTangent, s = b.position;
                    for (int axis = 0; axis < 2; axis++)
                    {
                        double x = -p[axis] + 3d * q[axis] - 3d * r[axis] + s[axis];
                        double y = 2d * (p[axis] - 2d * q[axis] + r[axis]), z = q[axis] - p[axis];
                        void Root(double t) { if (t > 0 && t < 1) Include(VectorGeometry.EvaluateCubic(p, q, r, s, (float)t)); }
                        if (Math.Abs(x) < 1e-12) { if (Math.Abs(y) > 1e-12) Root(-z / y); }
                        else { double d = y * y - 4 * x * z; if (d >= 0) { Root((-y - Math.Sqrt(d)) / (2 * x)); Root((-y + Math.Sqrt(d)) / (2 * x)); } }
                    }
                }
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
