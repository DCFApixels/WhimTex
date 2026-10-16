using System;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;
using HashCode = System.HashCode;

namespace DCFApixels.WhimTex
{
    public enum VectorBooleanOperation { Union, Subtract, Intersect, Xor, Combine }
    public enum BooleanGroupAppearance { Group, Layers }

    /// <summary>Editable operands resolve a Boolean region or retain separate figures in Combine mode.</summary>
    [Serializable]
    public sealed class BooleanGroupLayerBehaviour : LayerBehaviour
    {
        public VectorShape style = new VectorShape { name = "Boolean Result" };
        public Vector2 referenceSize = Vector2.one * 256;
        public VectorEdgeMode edgeMode;
        public BooleanGroupAppearance appearance;
        [NonSerialized] private VectorLayerBehaviour result;
        [NonSerialized] private int preparedHash;
        [NonSerialized] private bool prepared;
        internal int GeometryBuildCount { get; private set; }
        internal override bool IsGroup => true;
        internal override void InitializeLayer(Layer layer)
        {
            layer.compositing = GroupCompositing.Isolated;
            layer.transform.tiling = TransformTilingMode.Unbounded;
        }

        public void Validate()
        {
            if ((uint)appearance > (uint)BooleanGroupAppearance.Layers) throw new InvalidOperationException("Unknown Boolean Group appearance.");
            if (style == null) throw new InvalidOperationException("Boolean Group requires a result style.");
            if (style.contours == null || style.contours.Count != 0)
                throw new InvalidOperationException("Boolean Group style cannot contain geometry; use child layers.");
            new VectorLayerBehaviour { referenceSize = referenceSize, edgeMode = edgeMode,
                shapes = new List<VectorShape> { style } }.Validate();
            ValidateOperands(BoundOwner?.children, new HashSet<Layer>(), 0);
        }

        internal static bool IsOperand(Layer layer) => layer?.Behaviour is ShapeLayerBehaviour or VectorLayerBehaviour or BooleanGroupLayerBehaviour;

        internal static void ValidateOperands(List<Layer> layers, HashSet<Layer> visited, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("Boolean Group nesting exceeds 64 levels.");
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (!IsOperand(layer)) throw new InvalidOperationException("Boolean Group accepts only Shape, Vector and Boolean Group layers.");
                if (!visited.Add(layer)) throw new InvalidOperationException("Boolean Group contains a cycle or shared operand.");
                if ((uint)layer.booleanOperation > (uint)VectorBooleanOperation.Combine) throw new InvalidOperationException("Unknown Boolean operation.");
                if (layer.Behaviour is VectorLayerBehaviour vector)
                {
                    vector.Validate();
                    foreach (var shape in vector.shapes) foreach (var contour in shape.contours)
                        if (!contour.closed && contour.points.Count > 1)
                            throw new InvalidOperationException("Boolean operands require closed contours. Close the open path first.");
                }
                if (layer.Behaviour is BooleanGroupLayerBehaviour) ValidateOperands(layer.children, visited, depth + 1);
            }
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            using var geometryBuild = StrokeGeometry.BeginBuild();
            if (appearance == BooleanGroupAppearance.Layers && ReadMode(Owner) == VectorBooleanOperation.Combine)
            {
                Validate();
                result?.ReleaseTransientResources(); result = null; prepared = false;
                return context.activeDocument.RenderCombinedLayerStyles(Owner, context);
            }
            var hash = new HashCode();
            hash.Add(context.width); hash.Add(context.height); hash.Add(context.activeDocument.width); hash.Add(context.activeDocument.height);
            hash.Add(context.activeDocument.GetPixelScale()); hash.Add(context.applyTransform);
            HashMatrix(Owner.RenderTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height), ref hash);
            hash.Add(referenceSize); hash.Add(edgeMode); hash.Add(appearance);
            HashOperands(Owner.layers, context.activeDocument.width, context.activeDocument.height, ref hash, 0);
            int key = hash.ToHashCode();
            if (!prepared || result == null || key != preparedHash)
            {
                Validate();
                using var measure = WhimTexProfiler.Measure("vector.boolean", context.activeDocument, Owner);
                GeometryBuildCount++;
                ProjectiveMatrix output = ProjectiveMatrix.Scale(context.width, context.height) *
                    (context.applyTransform ? Owner.RenderTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height) : ProjectiveMatrix.Identity);
                int count = 0;
                var parts = Combine(Owner.layers, ProjectiveMatrix.Identity, output, context.activeDocument, ref count);
                var figures = new List<VectorShape>();
                var paths = new List<Paths64>(parts.Count);
                foreach (var part in parts)
                {
                    var figure = style.Clone(false);
                    figure.visible = true; figure.fillRule = part.fillRule;
                    figure.contours.Clear();
                    paths.Add(part.paths);
                    figures.Add(figure);
                }
                var next = new VectorLayerBehaviour { referenceSize = referenceSize, edgeMode = edgeMode,
                    shapes = figures };
                next.SetPreparedContours(paths);
                result?.ReleaseTransientResources();
                result = next;
                preparedHash = key; prepared = true;
            }
            if (style == null || style.contours == null || style.contours.Count != 0)
                throw new InvalidOperationException("Boolean Group requires a style without geometry.");
            foreach (var figure in result.shapes) figure.CopyAppearanceFrom(style);
            return result.RenderWithOwner(new LayerRenderContext(context.activeDocument, context.input,
                context.width, context.height, context.scaleMultiplier, context.applyTransform, context.applyFx,
                transformFxCoordinates: false), Owner);
        }

        private readonly struct GeometryPart
        {
            internal readonly Paths64 paths;
            internal readonly VectorFillRule fillRule;
            internal GeometryPart(Paths64 paths, VectorFillRule fillRule) { this.paths = paths; this.fillRule = fillRule; }
        }

        private static Paths64 FilledRegion(List<GeometryPart> parts)
        {
            using var measure = VectorGeometryTiming.Measure("vector.boolean.filled_region");
            var region = new Paths64();
            foreach (var part in parts)
            {
                var filled = StrokeGeometry.Resolve(part.paths, rule: part.fillRule == VectorFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
                // Do not assume every Clipper result is already normalized: touching
                // contours may still merge. Reuse only a demonstrated NonZero fixed point,
                // including path order, winding and seam (important for centered dashes).
                region = region.Count == 0 && part.fillRule == VectorFillRule.NonZero && SamePaths(part.paths, filled)
                    ? filled : StrokeGeometry.Resolve(region, filled, ClipType.Union);
            }
            return region;
        }

        private static bool SamePaths(Paths64 a, Paths64 b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Count != b[i].Count) return false;
                for (int j = 0; j < a[i].Count; j++) if (a[i][j] != b[i][j]) return false;
            }
            return true;
        }

        private List<GeometryPart> Combine(List<Layer> children, ProjectiveMatrix parent, ProjectiveMatrix output,
            WhimTexDocument document, ref int count)
        {
            var combined = new List<GeometryPart>(); bool first = true;
            if (children == null) return combined;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i]; if (!child.enabled) continue;
                var matrix = parent * child.transform.ToMatrix(document.width, document.height);
                List<GeometryPart> operand;
                if (child.Behaviour is BooleanGroupLayerBehaviour)
                    operand = Combine(child.children, matrix, output, document, ref count);
                else
                {
                    var figures = child.Behaviour is VectorLayerBehaviour vector ? vector.shapes :
                        new List<VectorShape> { new VectorShape { contours = ShapeToVector.GeometryContours(
                            (ShapeLayerBehaviour)child.Behaviour, ((ShapeLayerBehaviour)child.Behaviour).GeometryHalfSize(document), document.GetPixelScale()) } };
                    Vector2 reference = child.Behaviour is VectorLayerBehaviour v ? v.referenceSize : new Vector2(document.width, document.height);
                    operand = new List<GeometryPart>();
                    foreach (var figure in figures)
                    {
                        if (!figure.visible) continue;
                        var input = new Paths64();
                        float tolerance = VectorRasterizer.FlattenTolerance(figure, reference,
                            output * matrix * ProjectiveMatrix.Scale(1d / reference.x, 1d / reference.y), false, includeStroke: false);
                        // Open paths never contribute to Boolean fills; do not subdivide
                        // them or consume their point budget during operand preparation.
                        var closedContours = new List<VectorContour>();
                        foreach (var contour in figure.contours)
                            if (contour.closed) closedContours.Add(contour);
                        var paths = VectorContourPreparation.Flatten(closedContours, reference, tolerance);
                        foreach (var points in paths)
                        {
                            if ((count += points.Count) > VectorGeometry.MaxFlattenedPoints) throw VectorGeometry.Limit("Boolean flattened point count");
                            for (int p = 0; p < points.Count; p++)
                            {
                                var q = matrix.Point(new Double2(points[p].x / reference.x, points[p].y / reference.y));
                                points[p] = new Vector2((float)(q.x * referenceSize.x), (float)(q.y * referenceSize.y));
                            }
                            if (points.Count >= 3) input.Add(StrokeGeometry.ToPath(points));
                        }
                        operand.Add(new GeometryPart(input, figure.fillRule));
                    }
                }
                if (first) { combined = operand; first = false; }
                else if (child.booleanOperation == VectorBooleanOperation.Combine) combined.AddRange(operand);
                else combined = new List<GeometryPart> { new GeometryPart(StrokeGeometry.Resolve(FilledRegion(combined), FilledRegion(operand), child.booleanOperation switch
                {
                    VectorBooleanOperation.Subtract => ClipType.Difference,
                    VectorBooleanOperation.Intersect => ClipType.Intersection,
                    VectorBooleanOperation.Xor => ClipType.Xor,
                    _ => ClipType.Union
                }), VectorFillRule.NonZero) };
            }
            return combined;
        }

        internal static VectorBooleanOperation ReadMode(Layer layer)
        {
            if (layer.children.Count == 0) return (VectorBooleanOperation)(-1);
            var mode = layer.children[0].booleanOperation;
            int basis = -1;
            for (int i = layer.children.Count - 1; i >= 0; i--)
                if (layer.children[i].enabled) { basis = i; break; }
            bool found = false;
            for (int i = 0; i < layer.children.Count; i++)
            {
                if (i == basis) continue;
                if (!found) { mode = layer.children[i].booleanOperation; found = true; }
                else if (mode != layer.children[i].booleanOperation) return (VectorBooleanOperation)(-1);
            }
            return mode;
        }

        private static void HashMatrix(ProjectiveMatrix matrix, ref HashCode hash)
        {
            hash.Add(matrix.m00); hash.Add(matrix.m01); hash.Add(matrix.m02);
            hash.Add(matrix.m10); hash.Add(matrix.m11); hash.Add(matrix.m12);
            hash.Add(matrix.m20); hash.Add(matrix.m21); hash.Add(matrix.m22);
        }

        private static void HashOperands(List<Layer> layers, int width, int height, ref HashCode hash, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("Boolean Group nesting exceeds 64 levels.");
            hash.Add(layers?.Count ?? 0);
            if (layers == null) return;
            foreach (var child in layers)
            {
                hash.Add(child?.enabled); if (child == null) continue;
                HashMatrix(child.transform.ToMatrix(width, height), ref hash); hash.Add(child.booleanOperation);
                if (child.Behaviour is VectorLayerBehaviour vector) hash.Add(vector.ComputeGeometryHash(booleanOperand: true));
                else if (child.Behaviour is ShapeLayerBehaviour shape)
                {
                    hash.Add(shape.kind); hash.Add(shape.sides); hash.Add(shape.innerRadius);
                    hash.Add(shape.startAngle); hash.Add(shape.sweepAngle); hash.Add(shape.arcThickness); hash.Add(shape.lineCap);
                    shape.AddCornerHash(ref hash);
                }
                else if (child.Behaviour is BooleanGroupLayerBehaviour) HashOperands(child.children, width, height, ref hash, depth + 1);
                else hash.Add(-1);
            }
        }

        internal override void ReleaseTransientResources()
        { result?.ReleaseTransientResources(); result = null; prepared = false; }
    }
}
