using System;
using System.Collections.Generic;
using System.IO;
using Typography.OpenFont;
using Typography.OpenFont.CFF;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal readonly struct TextGlyphPlacement
    {
        internal readonly string path;
        internal readonly int faceIndex;
        internal readonly uint glyphIndex;
        internal readonly Vector2 origin;
        internal readonly float size, horizontalScale;
        internal readonly bool italic;
        internal TextGlyphPlacement(string path, int faceIndex, uint glyphIndex, Vector2 origin,
            float size, float horizontalScale, bool italic)
        {
            this.path = path; this.faceIndex = faceIndex; this.glyphIndex = glyphIndex;
            this.origin = origin; this.size = size; this.horizontalScale = horizontalScale; this.italic = italic;
        }
    }

    // One build owns its parsed faces. The resulting curves, not whole font files/atlases,
    // are retained by the Text layer. Typography implements both composite TTF and CFF.
    internal sealed class FontOutlineReader
    {
        private readonly Dictionary<(string, int), Typeface> faces = new Dictionary<(string, int), Typeface>();
        private readonly Dictionary<(string, int, uint), List<VectorContour>> glyphs = new Dictionary<(string, int, uint), List<VectorContour>>();
        private readonly CffEvaluationEngine cff = new CffEvaluationEngine();

        private Typeface Face(string path, int index)
        {
            if (faces.TryGetValue((path, index), out var face)) return face;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length < 12 || file.Length > 128L * 1024 * 1024)
                    throw new NotSupportedException("Font file is missing or exceeds the 128 MiB reader budget.");
                string extension = file.Extension.ToLowerInvariant();
                if (extension != ".ttf" && extension != ".otf" && extension != ".ttc" && extension != ".otc")
                    throw new NotSupportedException("Only TrueType/OpenType outline files and collections are supported.");
                var reader = new OpenFontReader();
                PreviewFontInfo preview;
                using (var stream = File.OpenRead(path)) preview = reader.ReadPreview(stream);
                int offset = 0;
                if (preview.IsFontCollection)
                {
                    if (index < 0 || index >= preview.MemberCount) throw new NotSupportedException("Font collection face is unavailable.");
                    offset = preview.GetMember(index).ActualStreamOffset;
                }
                else if (index != 0) throw new NotSupportedException("Named variable font instances are not supported by the outline reader.");
                using (var stream = File.OpenRead(path)) face = reader.Read(stream, offset);
                if (face == null || face.UnitsPerEm == 0 || face.GlyphCount == 0)
                    throw new NotSupportedException("The selected font has no supported outline data.");
                if (face.ContainsTable("fvar") || face.ContainsTable("CFF2"))
                    throw new NotSupportedException("Variable font outlines are not supported; choose a static font face.");
                if (face.ContainsTable("COLR") || face.ContainsTable("SVG ") || face.ContainsTable("sbix") ||
                    (!face.ContainsTable("glyf") && !face.ContainsTable("CFF ")))
                    throw new NotSupportedException("Color/bitmap font outlines are not supported; choose a monochrome outline font.");
                faces.Add((path, index), face);
                return face;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                throw new NotSupportedException("Text outlines: cannot read '" + Path.GetFileName(path) + "' face " + index + ": " + error.Message, error);
            }
        }

        internal List<VectorContour> Read(TextGlyphPlacement placement, Vector2 reference)
        {
            var face = Face(placement.path, placement.faceIndex);
            var key = (placement.path, placement.faceIndex, placement.glyphIndex);
            if (!glyphs.TryGetValue(key, out var original))
            {
                if (placement.glyphIndex > ushort.MaxValue || placement.glyphIndex >= face.GlyphCount)
                    throw new NotSupportedException("Text outlines: the raster glyph does not belong to the selected font face.");
                var glyph = face.GetGlyph((ushort)placement.glyphIndex);
                var writer = new CurveWriter();
                if (glyph.IsCffGlyph) cff.Run(writer, glyph.GetCff1GlyphData());
                else writer.Read(glyph.GlyphPoints, glyph.EndPoints);
                writer.CloseContour();
                original = writer.contours;
                if (original.Count == 0)
                    throw new NotSupportedException("Text outlines: a visible glyph has no outline (bitmap/color-only glyphs are unsupported).");
                glyphs.Add(key, original);
            }
            float factor = placement.size / face.UnitsPerEm;
            Vector2 Offset(Vector2 p) => new Vector2((p.x + (placement.italic ? .2f * p.y : 0)) * factor * placement.horizontalScale / reference.x,
                p.y * factor / reference.y);
            var origin = new Vector2(.5f + placement.origin.x / reference.x, .5f + placement.origin.y / reference.y);
            var result = new List<VectorContour>(original.Count);
            foreach (var source in original)
            {
                var contour = new VectorContour();
                foreach (var p in source.points)
                    contour.points.Add(new VectorPoint(origin + Offset(p.position))
                    { inTangent = Offset(p.inTangent), outTangent = Offset(p.outTangent), tangentMode = VectorTangentMode.Corner });
                result.Add(contour);
            }
            return result;
        }

        private sealed class CurveWriter : IGlyphTranslator
        {
            internal readonly List<VectorContour> contours = new List<VectorContour>();
            private VectorContour current;
            private int count;
            public void BeginRead(int contourCount) { }
            public void EndRead() => CloseContour();
            public void MoveTo(float x, float y)
            {
                CloseContour();
                if (contours.Count >= VectorGeometry.MaxContours) throw VectorGeometry.Limit("font contour count");
                current = new VectorContour();
                Add(new Vector2(x, y));
            }
            private void Add(Vector2 point, Vector2 incoming = default)
            {
                if (++count > VectorGeometry.MaxPoints) throw VectorGeometry.Limit("font anchor count");
                if (!VectorGeometry.Finite(point) || !VectorGeometry.Finite(incoming)) throw VectorGeometry.Limit("font coordinate range");
                if (current == null) throw new InvalidOperationException("Font curve starts without MoveTo.");
                current.points.Add(new VectorPoint(point) { inTangent = incoming });
            }
            public void LineTo(float x, float y) => Add(new Vector2(x, y));
            public void Curve3(float x1, float y1, float x2, float y2)
            {
                var a = current.points[current.points.Count - 1];
                var control = new Vector2(x1, y1); var end = new Vector2(x2, y2);
                // Exact degree elevation: quadratic font curves remain exact cubic curves.
                a.outTangent = (control - a.position) * (2f / 3);
                Add(end, (control - end) * (2f / 3));
            }
            public void Curve4(float x1, float y1, float x2, float y2, float x3, float y3)
            {
                var a = current.points[current.points.Count - 1];
                a.outTangent = new Vector2(x1, y1) - a.position;
                var end = new Vector2(x3, y3);
                Add(end, new Vector2(x2, y2) - end);
            }
            public void CloseContour()
            {
                if (current == null) return;
                var points = current.points;
                if (points.Count > 1 && points[0].position == points[points.Count - 1].position)
                {
                    points[0].inTangent = points[points.Count - 1].inTangent;
                    points.RemoveAt(points.Count - 1);
                }
                if (points.Count >= 2) contours.Add(current);
                current = null;
            }
        }
    }
}
