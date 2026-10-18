using System;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    /// <summary>Original font curves in the existing Text layout. Never traces a raster mask.</summary>
    public static class TextToVector
    {
        /// <summary>Returns independent figures. Character gradients produce one figure per visible glyph; normalize them into child layers.</summary>
        public static VectorLayerBehaviour Convert(TextLayerBehaviour source, WhimTexDocument document)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (FigureFill.IsGradient(source.fillMode) && source.gradientScope == TextGradientScope.Character)
                return Build(source, document, characterPaint: true);
            var geometry = source.GetVectorGeometry(document);
            var result = new VectorLayerBehaviour { referenceSize = geometry.referenceSize, edgeMode = geometry.edgeMode };
            foreach (var figure in geometry.shapes)
            {
                var copy = figure.Clone(false); copy.fillMode = source.fillMode; copy.fillColor = source.color;
                copy.gradient = source.gradient.Clone(); copy.gradientRepetitions = source.gradientRepetitions;
                if (FigureFill.IsGradient(source.fillMode))
                {
                    Rect frame = source.GetGradientFrame(document.width, document.height);
                    Vector2 Anchor(Vector2 uv) => new Vector2(.5f + (frame.x + uv.x * frame.width) / document.width,
                        .5f + (frame.y + uv.y * frame.height) / document.height);
                    copy.gradientStart = Anchor(source.gradientStart); copy.gradientEnd = Anchor(source.gradientEnd);
                }
                result.shapes.Add(copy);
            }
            result.Validate();
            return result;
        }

        internal static Layer PrepareLayer(TextLayerBehaviour source, WhimTexDocument document)
        {
            var geometry = Convert(source, document);
            bool characters = FigureFill.IsGradient(source.fillMode) && source.gradientScope == TextGradientScope.Character;
            if (!characters)
            {
                var vector = new Layer(geometry) { transform = source.Owner.transform };
                VectorLayerStructure.FitFrame(vector, document.width, document.height);
                return vector;
            }
            int Count(List<Layer> layers, int depth = 0)
            {
                if (depth > 64) throw new InvalidOperationException("Layer nesting exceeds 64 levels.");
                int count = 0;
                foreach (var layer in layers)
                {
                    count++;
                    if (ReferenceEquals(layer, source.Owner) && depth >= 64 && geometry.shapes.Count > 0)
                        throw new InvalidOperationException("Character conversion would exceed the layer nesting limit.");
                    if (layer?.IsGroup == true) count += Count(layer.children, depth + 1);
                    if (count + geometry.shapes.Count > 1024)
                        throw new InvalidOperationException("Character conversion would exceed the 1024-layer document limit.");
                }
                return count;
            }
            Count(document.layers);
            bool booleanOperand = document.IsBooleanOperand(source.Owner);
            LayerBehaviour behaviour = booleanOperand ? new BooleanGroupLayerBehaviour
            {
                referenceSize = geometry.referenceSize, edgeMode = geometry.edgeMode, appearance = BooleanGroupAppearance.Layers
            } : new GroupLayerBehaviour();
            var group = new Layer(behaviour) { transform = source.Owner.transform, compositing = GroupCompositing.Isolated,
                groupFxInLayerSpace = true };
            for (int i = geometry.shapes.Count - 1; i >= 0; i--)
            {
                var figure = geometry.shapes[i];
                var child = new Layer(new VectorLayerBehaviour { referenceSize = geometry.referenceSize,
                    edgeMode = geometry.edgeMode, shapes = new List<VectorShape> { figure } })
                {
                    layerName = figure.name, colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR,
                    filterMode = source.Owner.filterMode, booleanOperation = VectorBooleanOperation.Combine
                };
                child.transform.tiling = source.Owner.transform.tiling;
                VectorLayerStructure.FitFrame(child, document.width, document.height);
                group.children.Add(child);
            }
            return group;
        }

        internal static void ApplyPrepared(Layer target, Layer prepared)
        {
            target.SetBehaviour(prepared.DetachBehaviour());
            target.transform = prepared.transform;
            if (target.IsGroup)
            {
                target.children = prepared.children; prepared.children = null;
                target.compositing = GroupCompositing.Isolated;
                target.groupFxInLayerSpace = prepared.groupFxInLayerSpace;
            }
        }

        internal static VectorLayerBehaviour Build(TextLayerBehaviour source, WhimTexDocument document, bool characterPaint = false)
        {
            source.Validate();
            if (!source.FontAvailable)
                throw new NotSupportedException("Text outlines require the original installed font '" + source.ResolvedFont + "'. A saved raster appearance cannot supply vector geometry.");
            if (document.width <= 0 || document.height <= 0) throw new ArgumentException("The document must have a positive size.");
            using var measure = WhimTexProfiler.Measure("geometry.text_outlines", document, source.Owner);
            using var layout = new TextLayerRenderer();
            var placements = layout.GetGlyphLayout(source, document.width, document.height);
            var reference = new Vector2(document.width, document.height);
            var reader = new FontOutlineReader();
            var figure = new VectorShape { name = "Text", fillColor = source.color, fillRule = VectorFillRule.NonZero };
            var figures = new List<VectorShape>();
            bool clip = source.layoutMode == TextLayoutMode.Frame && source.overflow != TextOverflowMode.None;
            Rect bounds = layout.Bounds;
            Rect normalizedClip = new Rect(new Vector2(.5f + bounds.xMin / reference.x, .5f + bounds.yMin / reference.y),
                new Vector2(bounds.width / reference.x, bounds.height / reference.y));
            int count = 0;
            using var build = StrokeGeometry.BeginBuild();
            foreach (var placement in placements)
            {
                var contours = reader.Read(placement, reference);
                // Entirely contained glyphs retain exact Beziers. Only glyphs crossing a
                // clipping frame need the vector pipeline's bounded polygon intersection.
                if (clip && !Contained(contours, normalizedClip)) contours = Clip(contours, normalizedClip, reference);
                if (characterPaint)
                {
                    if (contours.Count == 0) continue;
                    figure = new VectorShape { name = char.ConvertFromUtf32((int)placement.unicode), fillRule = VectorFillRule.NonZero,
                        fillMode = source.fillMode, fillColor = source.color, gradient = source.gradient.Clone(),
                        gradientRepetitions = source.gradientRepetitions };
                    Vector2 Anchor(Vector2 uv) => new Vector2(.5f + (placement.paintFrame.x + uv.x * placement.paintFrame.width) / reference.x,
                        .5f + (placement.paintFrame.y + uv.y * placement.paintFrame.height) / reference.y);
                    figure.gradientStart = Anchor(source.gradientStart); figure.gradientEnd = Anchor(source.gradientEnd);
                    figures.Add(figure);
                }
                foreach (var contour in contours)
                {
                    if ((count += contour.points.Count) > VectorGeometry.MaxPoints || figure.contours.Count >= VectorGeometry.MaxContours)
                        throw VectorGeometry.Limit("text outline count");
                    figure.contours.Add(contour);
                }
            }
            var result = new VectorLayerBehaviour { referenceSize = reference,
                edgeMode = source.edgeMode == TextEdgeMode.Pixel ? VectorEdgeMode.Step : VectorEdgeMode.Antialiased };
            if (characterPaint) result.shapes.AddRange(figures); else result.shapes.Add(figure);
            result.Validate();
            return result;
        }

        private static bool Contained(List<VectorContour> contours, Rect bounds)
        {
            bool Inside(Vector2 p) => p.x >= bounds.xMin && p.x <= bounds.xMax && p.y >= bounds.yMin && p.y <= bounds.yMax;
            foreach (var contour in contours) foreach (var p in contour.points)
                if (!Inside(p.position) || !Inside(p.position + p.inTangent) || !Inside(p.position + p.outTangent)) return false;
            return true;
        }

        private static List<VectorContour> Clip(List<VectorContour> contours, Rect bounds, Vector2 reference)
        {
            var input = new Paths64();
            foreach (var path in VectorContourPreparation.Flatten(contours, reference, .01f))
                if (path.Count >= 3) input.Add(StrokeGeometry.ToPath(path));
            var rectangle = StrokeGeometry.ToPath(new List<Vector2>
            {
                Vector2.Scale(bounds.min, reference), new Vector2(bounds.xMax * reference.x, bounds.yMin * reference.y),
                Vector2.Scale(bounds.max, reference), new Vector2(bounds.xMin * reference.x, bounds.yMax * reference.y)
            });
            var clipped = StrokeGeometry.Resolve(input, new Paths64 { rectangle }, ClipType.Intersection);
            var result = new List<VectorContour>();
            foreach (var path in clipped)
            {
                var contour = new VectorContour();
                foreach (var p in path) contour.points.Add(new VectorPoint(new Vector2(
                    (float)(p.X / StrokeGeometry.Precision / reference.x), (float)(p.Y / StrokeGeometry.Precision / reference.y))));
                result.Add(contour);
            }
            return result;
        }
    }
}
