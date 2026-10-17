using System;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    /// <summary>Original font curves in the existing Text layout. Never traces a raster mask.</summary>
    public static class TextToVector
    {
        /// <summary>Returns independent geometry; the caller replaces only the layer behaviour.</summary>
        public static VectorLayerBehaviour Convert(TextLayerBehaviour source, WhimTexDocument document)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (document == null) throw new ArgumentNullException(nameof(document));
            var geometry = source.GetVectorGeometry(document);
            var result = new VectorLayerBehaviour { referenceSize = geometry.referenceSize, edgeMode = geometry.edgeMode };
            foreach (var figure in geometry.shapes) result.shapes.Add(figure.Clone(false));
            result.Validate();
            return result;
        }

        internal static VectorLayerBehaviour Build(TextLayerBehaviour source, WhimTexDocument document)
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
                foreach (var contour in contours)
                {
                    if ((count += contour.points.Count) > VectorGeometry.MaxPoints || figure.contours.Count >= VectorGeometry.MaxContours)
                        throw VectorGeometry.Limit("text outline count");
                    figure.contours.Add(contour);
                }
            }
            var result = new VectorLayerBehaviour { referenceSize = reference,
                edgeMode = source.edgeMode == TextEdgeMode.Pixel ? VectorEdgeMode.Step : VectorEdgeMode.Antialiased };
            result.shapes.Add(figure);
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
