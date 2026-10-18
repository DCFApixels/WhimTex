using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class TextLayerRenderer : IDisposable
    {
        private TextRasterFont font, smallFont;
        private FontStyle fontStyle;
        private TextEdgeMode edgeMode;
        private string family, key;
        private int revision = -1;
        private Texture2D mask;
        private float cachedScale;
        private float cachedPixelScale;
        private Vector2 cachedOrigin;
        private List<Batch> cachedBatches;
        private readonly List<Rect> characterFrames = new List<Rect>();
        private Rect inkBounds, paintPixels, clipPixels;
        private bool clipGlyphs;
        private WhimTexGradientPalette palette;
        internal int MaskBuildCount { get; private set; }
        private sealed class Batch
        {
            internal TextRasterFont font;
            internal Texture2D atlas;
            internal readonly List<Vector3> positions = new List<Vector3>();
            internal readonly List<Vector2> uv = new List<Vector2>();
            internal readonly List<Vector4> paintUv = new List<Vector4>();
            internal readonly List<int> triangles = new List<int>();
            internal Mesh mesh;
        }
        internal Rect Bounds { get; private set; }
        internal Rect SourceRect { get; private set; }
        internal int FontSize { get; private set; }
        private readonly List<Vector3> pointLines = new List<Vector3>();
        internal IReadOnlyList<Vector3> PointLines => pointLines;
        private static Vector2 GridOrigin(TextLayerBehaviour layer, int width, int height) =>
            (Vector2)layer.Owner.GetSourceCanvasSize(width, height) * .5f;

        internal Rect GetBounds(TextLayerBehaviour layer, int width, int height)
        {
            if (mask == null || key != layer.LayoutKey(width, height) || family != layer.ResolvedFont ||
                revision != SystemFontCatalog.Revision || cachedOrigin != GridOrigin(layer, width, height))
                GetMask(layer, width, height, 1);
            else if (cachedPixelScale != layer.Owner.GetPixelScale())
                GetMask(layer, width, height, 1);
            return Bounds;
        }

        internal Texture2D GetMask(TextLayerBehaviour layer, int width, int height, float scale)
            => Build(layer, width, height, scale, null);

        // Uses exactly the raster renderer's layout and selected glyph IDs. Only its output
        // changes: placements instead of atlas quads. No mask/readback or tracing is involved.
        internal List<TextGlyphPlacement> GetGlyphLayout(TextLayerBehaviour layer, int width, int height)
        {
            var glyphs = new List<TextGlyphPlacement>();
            Build(layer, width, height, 1, glyphs);
            return glyphs;
        }

        private Texture2D Build(TextLayerBehaviour layer, int width, int height, float scale, List<TextGlyphPlacement> placements)
        {
            width = Mathf.Max(1, width); height = Mathf.Max(1, height);
            Vector2 origin = GridOrigin(layer, width, height);
            float pixelScale = layer.Owner.GetPixelScale();
            if (family != layer.ResolvedFont || revision != SystemFontCatalog.Revision || fontStyle != layer.fontStyle || edgeMode != layer.edgeMode)
            {
                font?.Dispose(); smallFont?.Dispose(); font = smallFont = null;
                family = layer.ResolvedFont; revision = SystemFontCatalog.Revision;
                fontStyle = layer.fontStyle; edgeMode = layer.edgeMode;
                key = null;
            }
            string layoutKey = layer.LayoutKey(width, height);
            if (placements == null && mask != null && key == layoutKey && cachedScale == scale && cachedOrigin == origin && cachedPixelScale == pixelScale) return mask;
            int size = Mathf.Clamp(Mathf.RoundToInt(layer.fontSize), 1, 2048);
            string text = (layer.text ?? "").Replace("\t", "    ");
            if (layer.casing == TextCasing.Lowercase) text = text.ToLowerInvariant();
            else if (layer.casing == TextCasing.Uppercase) text = text.ToUpperInvariant();
            bool smallCaps = layer.casing == TextCasing.SmallCaps;
            float horizontalScale = layer.characterHorizontalScale;
            int smallSize = 1;
            float characterGap = 0, wordGap = 0;
            string glyphText = smallCaps ? text.ToUpperInvariant() : text;
            void Request(int full, int small)
            {
                if (font == null || font.Size != full) { font?.Dispose(); font = new TextRasterFont(family, fontStyle, full, edgeMode); }
                font.Add(glyphText);
                if (smallCaps && small != full)
                {
                    if (smallFont == null || smallFont.Size != small) { smallFont?.Dispose(); smallFont = new TextRasterFont(family, fontStyle, small, edgeMode); }
                    smallFont.Add(glyphText);
                }
                else { smallFont?.Dispose(); smallFont = null; }
            }
            int RasterSize(float value) => Mathf.RoundToInt(Mathf.Clamp(value, 1, 2048));
            bool WordSpace(char character) => character == ' ' || character == '\u00a0';
            float GlyphAdvance(string value)
            {
                float result = 0;
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    bool small = smallCaps && char.IsLower(character);
                    uint unicode = smallCaps ? char.ToUpperInvariant(character) : character;
                    if (char.IsHighSurrogate(character) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        unicode = (uint)char.ConvertToUtf32(character, value[++i]);
                    var face = small ? smallFont ?? font : font;
                    var glyph = face.Glyph(unicode);
                    if (glyph == null) continue;
                    float advance = glyph.metrics.horizontalAdvance * (small ? smallSize : size) / face.Size * horizontalScale;
                    result += WordSpace(character) ? Mathf.Max(0, advance + wordGap) : advance;
                }
                return result;
            }
            float Measure(string value)
            {
                if (characterGap == 0) return GlyphAdvance(value);
                int[] elements = StringInfo.ParseCombiningCharacters(value);
                float result = 0;
                for (int i = 0; i < elements.Length; i++)
                {
                    int end = i + 1 < elements.Length ? elements[i + 1] : value.Length;
                    float advance = GlyphAdvance(value.Substring(elements[i], end - elements[i]));
                    result += i + 1 < elements.Length ? Mathf.Max(0, advance + characterGap) : advance;
                }
                return result;
            }
            bool framed = layer.layoutMode == TextLayoutMode.Frame;
            List<TextLayout.Line> lines = null;
            var lineOffsets = new List<float>();
            float widest = 0, lineHeight = 0, contentHeight = 0;
            void MeasureLines()
            {
                widest = 0;
                foreach (var line in lines) widest = Mathf.Max(widest, Measure(line.text));
                lineHeight = font.Asset.faceInfo.lineHeight * size / font.Size;
                lineOffsets.Clear(); lineOffsets.Add(0);
                float lineStep = Mathf.Max(1, lineHeight + layer.spacing.line * size);
                for (int i = 1; i < lines.Count; i++)
                    lineOffsets.Add(lineOffsets[i - 1] + Mathf.Max(1, lineStep + (lines[i - 1].paragraphEnd ? layer.spacing.paragraph * size : 0)));
                contentHeight = lineHeight + lineOffsets[lines.Count - 1];
            }
            void Layout(int candidate)
            {
                size = candidate;
                characterGap = layer.spacing.character * size; wordGap = layer.spacing.word * size;
                smallSize = Mathf.Max(1, Mathf.RoundToInt(size * .75f));
                Request(RasterSize(size * pixelScale), RasterSize(smallSize * pixelScale));
                lines = TextLayout.Build(text, framed ? layer.wrapping : TextWrapping.Manual, layer.frameSize.x, Measure, characterGap);
                MeasureLines();
            }
            if (framed && layer.autoSize)
            {
                if (key == layoutKey) Layout(FontSize);
                else
                {
                    int low = Mathf.CeilToInt(layer.fontSize), high = Mathf.FloorToInt(layer.maxFontSize), best = low;
                    while (low <= high)
                    {
                        int candidate = low + (high - low) / 2;
                        Layout(candidate);
                        if (widest <= layer.frameSize.x && contentHeight <= layer.frameSize.y)
                        { best = candidate; low = candidate + 1; }
                        else high = candidate - 1;
                    }
                    Layout(best);
                }
            }
            else Layout(size);
            if (framed && layer.overflow == TextOverflowMode.Ellipsis)
            {
                font.Add("\u2026");
                lines = TextLayout.Ellipsize(lines, lineOffsets, lineHeight, layer.frameSize, Measure);
                MeasureLines();
                glyphText += "\u2026";
            }
            FontSize = size;
            int horizontal = (int)layer.LayoutAlignment % 3, vertical = (int)layer.LayoutAlignment / 3;
            float extent = framed ? layer.frameSize.x : widest;
            float left = layer.layoutOffset.x + (framed ? -layer.frameSize.x * .5f : -extent * horizontal * .5f);
            float top = layer.layoutOffset.y + (framed ? layer.frameSize.y * .5f - (layer.frameSize.y - contentHeight) * vertical * .5f : contentHeight * vertical * .5f);
            Bounds = framed ? new Rect(layer.layoutOffset - layer.frameSize * .5f, layer.frameSize) : new Rect(left, top - contentHeight, Mathf.Max(1, widest), contentHeight);
            pointLines.Clear();
            if (!framed)
            {
                float ascent = font.Asset.faceInfo.ascentLine * size / font.Size;
                for (int i = 0; i < lines.Count; i++)
                {
                    float lineWidth = Measure(lines[i].text);
                    float lineLeft = left + (extent - lineWidth) * horizontal * .5f;
                    pointLines.Add(new Vector3(lineLeft, lineLeft + lineWidth, top - lineOffsets[i] - ascent));
                }
            }
            SourceRect = Bounds;
            if (!framed || layer.overflow == TextOverflowMode.None)
            {
                // The extra raster margin is physical pixels, independent of authored font scale.
                float rasterPadding = 2 / Mathf.Max(pixelScale, 1e-12f);
                float padding = size * .25f + rasterPadding;
                float horizontalPadding = size * .25f * horizontalScale + rasterPadding;
                float contentWidth = layer.CanJustify && layer.justify ? Mathf.Max(widest, extent) : widest;
                float contentLeft = left + (extent - contentWidth) * horizontal * .5f;
                SourceRect = Rect.MinMaxRect(Mathf.Min(Bounds.xMin, contentLeft) - horizontalPadding,
                    Mathf.Min(Bounds.yMin, top - contentHeight) - padding, Mathf.Max(Bounds.xMax, contentLeft + contentWidth) + horizontalPadding,
                    Mathf.Max(Bounds.yMax, top) + padding);
            }
            float renderScale = Mathf.Min(scale * pixelScale, (Mathf.Min(SystemInfo.maxTextureSize, 8192f) - 2) / Mathf.Max(SourceRect.width, SourceRect.height),
                Mathf.Sqrt((16 * 1024 * 1024f - 32768) / (SourceRect.width * SourceRect.height)));
            renderScale = Mathf.Max(renderScale, 1e-12f);
            // Retain the source pixel-grid phase when Crop changes canvas parity.
            Vector2 logicalOrigin = pixelScale < 1e-12f ? Vector2.zero : origin / pixelScale;
            SourceRect = Rect.MinMaxRect(Mathf.Floor((SourceRect.xMin + logicalOrigin.x) * renderScale) / renderScale - logicalOrigin.x,
                Mathf.Floor((SourceRect.yMin + logicalOrigin.y) * renderScale) / renderScale - logicalOrigin.y,
                Mathf.Ceil((SourceRect.xMax + logicalOrigin.x) * renderScale) / renderScale - logicalOrigin.x,
                Mathf.Ceil((SourceRect.yMax + logicalOrigin.y) * renderScale) / renderScale - logicalOrigin.y);
            int outputWidth = Mathf.Max(1, Mathf.RoundToInt(SourceRect.width * renderScale));
            int outputHeight = Mathf.Max(1, Mathf.RoundToInt(SourceRect.height * renderScale));
            // Rasterize expanded glyphs at their widest axis; mesh coordinates still use logical font units.
            int renderSize = RasterSize(size * renderScale * Mathf.Max(1, horizontalScale));
            float actualScale = renderSize / (float)size;
            int renderSmallSize = Mathf.Clamp(Mathf.RoundToInt(smallSize * actualScale), 1, 2048);
            Request(renderSize, renderSmallSize);
            var batches = new List<Batch>();
            if (placements == null) { ReleaseMeshes(); characterFrames.Clear(); MaskBuildCount++; }
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                int[] elements = StringInfo.ParseCombiningCharacters(line.text);
                float lineWidth = Measure(line.text);
                var gaps = new List<float>();
                if (layer.CanJustify && layer.justify && !line.paragraphEnd)
                {
                    float cursor = 0;
                    for (int i = 0; i < elements.Length; i++)
                    {
                        int start = elements[i], end = i + 1 < elements.Length ? elements[i + 1] : line.text.Length;
                        float advance = GlyphAdvance(line.text.Substring(start, end - start));
                        if (i > 0 && i < elements.Length - 1 && line.text[start] == ' ' && line.text[start - 1] != ' ')
                            gaps.Add(cursor + advance * .5f);
                        cursor += i + 1 < elements.Length ? Mathf.Max(0, advance + characterGap) : advance;
                    }
                }
                bool justified = gaps.Count > 0 && lineWidth < extent;
                float offsetX = left + (justified ? 0 : (extent - lineWidth) * horizontal * .5f);
                float offsetY = top - lineOffsets[lineIndex];
                void Append(string run, bool small, float runX)
                {
                    var face = small ? smallFont ?? font : font;
                    float baseline = offsetY - font.Asset.faceInfo.ascentLine / actualScale;
                    float cursor = runX;
                    for (int q = 0; q < run.Length; q++)
                    {
                        char character = run[q];
                        uint unicode = smallCaps ? char.ToUpperInvariant(character) : character;
                        if (char.IsHighSurrogate(character) && q + 1 < run.Length && char.IsLowSurrogate(run[q + 1]))
                            unicode = (uint)char.ConvertToUtf32(character, run[++q]);
                        var glyph = face.Glyph(unicode);
                        if (glyph == null) continue;
                        var metrics = glyph.metrics; var rect = glyph.glyphRect;
                        float glyphX = cursor;
                        float advance = metrics.horizontalAdvance * horizontalScale / actualScale;
                        cursor += WordSpace(character) ? Mathf.Max(0, advance + wordGap) : advance;
                        if (rect.width == 0 || rect.height == 0) continue;
                        float center = glyphX + (metrics.horizontalBearingX + metrics.width * .5f) * horizontalScale / actualScale;
                        int before = 0; if (justified) foreach (float gap in gaps) if (center > gap) before++;
                        float extra = justified ? before * (extent - lineWidth) / gaps.Count : 0;
                        if (placements != null)
                        {
                            if (face.SyntheticBold != 0)
                                throw new NotSupportedException("Text outlines: synthetic Bold has no original contour. Choose an installed bold font face.");
                            var file = face.SourceFile();
                            float px = (offsetX + glyphX + extra + metrics.horizontalBearingX * horizontalScale / actualScale - SourceRect.xMin) * renderScale;
                            float py = (baseline + (metrics.horizontalBearingY - metrics.height) / actualScale - SourceRect.yMin) * renderScale;
                            if (edgeMode != TextEdgeMode.Antialiased) { px = Mathf.Round(px); py = Mathf.Round(py); }
                            float gw = metrics.width * horizontalScale / actualScale * renderScale;
                            float gh = metrics.height / actualScale * renderScale;
                            float shearLow = face.SyntheticItalic ? .2f * (py - (baseline - SourceRect.yMin) * renderScale) * horizontalScale : 0;
                            float shearHigh = face.SyntheticItalic ? .2f * (py + gh - (baseline - SourceRect.yMin) * renderScale) * horizontalScale : 0;
                            var paintFrame = new Rect((SourceRect.xMin + (px + Mathf.Min(shearLow, shearHigh)) / renderScale) * pixelScale,
                                (SourceRect.yMin + py / renderScale) * pixelScale,
                                (gw + Mathf.Abs(shearHigh - shearLow)) / renderScale * pixelScale, gh / renderScale * pixelScale);
                            placements.Add(new TextGlyphPlacement(file.path, file.index, glyph.index,
                                new Vector2(offsetX + glyphX + extra, baseline) * pixelScale,
                                face.Size / actualScale * pixelScale, horizontalScale, face.SyntheticItalic, paintFrame, unicode));
                            continue;
                        }
                        var atlas = face.Asset.atlasTextures[glyph.atlasIndex];
                        var batch = batches.Find(item => item.atlas == atlas);
                        if (batch == null) { batch = new Batch { font = face, atlas = atlas }; batches.Add(batch); }
                        float padding = face.SyntheticBold;
                        float x0 = (offsetX + glyphX + extra + (metrics.horizontalBearingX - padding) * horizontalScale / actualScale - SourceRect.xMin) * renderScale;
                        float y0 = (baseline + (metrics.horizontalBearingY - metrics.height - padding) / actualScale - SourceRect.yMin) * renderScale;
                        if (edgeMode != TextEdgeMode.Antialiased) { x0 = Mathf.Round(x0); y0 = Mathf.Round(y0); }
                        float x1 = x0 + (metrics.width + padding * 2) * horizontalScale / actualScale * renderScale;
                        float y1 = y0 + (metrics.height + padding * 2) / actualScale * renderScale;
                        float u0 = (rect.x - padding) / atlas.width, u1 = (rect.x + rect.width + padding) / atlas.width;
                        float v0 = (rect.y - padding) / atlas.height, v1 = (rect.y + rect.height + padding) / atlas.height;
                        int v = batch.positions.Count;
                        for (int n = 0; n < 4; n++)
                        {
                            float px = n == 0 || n == 3 ? x0 : x1;
                            float py = n < 2 ? y1 : y0;
                            if (face.SyntheticItalic) px += .2f * (py - (baseline - SourceRect.yMin) * renderScale) * horizontalScale;
                            batch.positions.Add(new Vector3(px - outputWidth * .5f, py - outputHeight * .5f, 0));
                            batch.uv.Add(new Vector2(n == 0 || n == 3 ? u0 : u1, n < 2 ? v1 : v0));
                        }
                        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
                        for (int n = 0; n < 4; n++)
                        {
                            var p = batch.positions[v + n];
                            minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
                            maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
                        }
                        var glyphSize = new Vector2(Mathf.Max(.0001f, maxX - minX), Mathf.Max(.0001f, maxY - minY));
                        for (int n = 0; n < 4; n++)
                        {
                            var p = batch.positions[v + n];
                            batch.paintUv.Add(new Vector4((p.x - minX) / glyphSize.x, (p.y - minY) / glyphSize.y, glyphSize.x, glyphSize.y));
                        }
                        var frame = new Rect((minX + outputWidth * .5f) / renderScale * pixelScale + SourceRect.xMin * pixelScale,
                            (minY + outputHeight * .5f) / renderScale * pixelScale + SourceRect.yMin * pixelScale,
                            glyphSize.x / renderScale * pixelScale, glyphSize.y / renderScale * pixelScale);
                        inkBounds = characterFrames.Count == 0 ? frame : Rect.MinMaxRect(Mathf.Min(inkBounds.xMin, frame.xMin),
                            Mathf.Min(inkBounds.yMin, frame.yMin), Mathf.Max(inkBounds.xMax, frame.xMax), Mathf.Max(inkBounds.yMax, frame.yMax));
                        characterFrames.Add(frame);
                        batch.triangles.Add(v); batch.triangles.Add(v + 1); batch.triangles.Add(v + 2);
                        batch.triangles.Add(v); batch.triangles.Add(v + 2); batch.triangles.Add(v + 3);
                    }
                }
                if (!smallCaps && characterGap == 0 && wordGap == 0) Append(line.text, false, 0);
                else
                {
                    float runX = 0;
                    for (int start = 0; start < elements.Length;)
                    {
                        bool small = smallCaps && char.IsLower(line.text[elements[start]]);
                        bool space = WordSpace(line.text[elements[start]]); int end = start + 1;
                        while (characterGap == 0 && end < elements.Length && WordSpace(line.text[elements[end]]) == space &&
                            (smallCaps && char.IsLower(line.text[elements[end]])) == small) end++;
                        int finish = end < elements.Length ? elements[end] : line.text.Length;
                        string run = line.text.Substring(elements[start], finish - elements[start]);
                        if (!space) Append(run, small, runX);
                        float advance = Measure(run);
                        runX += characterGap != 0 && end < elements.Length ? Mathf.Max(0, advance + characterGap) : advance;
                        start = end;
                    }
                }
            }
            if (placements != null)
            {
                Bounds = new Rect(Bounds.position * pixelScale, Bounds.size * pixelScale);
                SourceRect = new Rect(SourceRect.position * pixelScale, SourceRect.size * pixelScale);
                key = null;
                return null;
            }
            cachedBatches = batches;
            if (characterFrames.Count == 0) inkBounds = new Rect(Bounds.position * pixelScale, Bounds.size * pixelScale);
            paintPixels = new Rect((inkBounds.xMin / pixelScale - SourceRect.xMin) * renderScale,
                (inkBounds.yMin / pixelScale - SourceRect.yMin) * renderScale,
                Mathf.Max(.0001f, inkBounds.width / pixelScale * renderScale), Mathf.Max(.0001f, inkBounds.height / pixelScale * renderScale));
            clipGlyphs = framed && layer.overflow != TextOverflowMode.None;
            clipPixels = Rect.MinMaxRect((Bounds.xMin - SourceRect.center.x) * renderScale,
                (Bounds.yMin - SourceRect.center.y) * renderScale, (Bounds.xMax - SourceRect.center.x) * renderScale,
                (Bounds.yMax - SourceRect.center.y) * renderScale);
            foreach (var batch in batches)
            {
                batch.mesh = new Mesh { name = "Text Glyphs", hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                batch.mesh.SetVertices(batch.positions); batch.mesh.SetUVs(0, batch.uv);
                batch.mesh.SetUVs(1, batch.paintUv); batch.mesh.SetTriangles(batch.triangles, 0);
            }
            var target = ProfiledRenderTexture.GetTemporary(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                RenderTexture.active = target; GL.sRGBWrite = false; GL.Clear(false, true, Color.clear);
                Material material = WhimTexMaterials.Text;
                material.SetVector("_TextCanvasSize", new Vector4(outputWidth, outputHeight, 0, 0));
                material.SetInt("_TextPixel", edgeMode == TextEdgeMode.Pixel ? 1 : 0);
                material.SetInt("_TextClip", framed && layer.overflow != TextOverflowMode.None ? 1 : 0);
                material.SetVector("_TextClipRect", new Vector4((Bounds.xMin - SourceRect.center.x) * renderScale,
                    (Bounds.yMin - SourceRect.center.y) * renderScale, (Bounds.xMax - SourceRect.center.x) * renderScale,
                    (Bounds.yMax - SourceRect.center.y) * renderScale));
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(-outputWidth * .5f, outputWidth * .5f, -outputHeight * .5f, outputHeight * .5f);
                    foreach (var batch in batches)
                    {
                        material.SetTexture("_MainTex", batch.atlas);
                        material.SetVector("_TextBoldOffset", new Vector4(batch.font.SyntheticBold / (float)batch.atlas.width,
                            batch.font.SyntheticBold / (float)batch.atlas.height, 0, 0));
                        if (!material.SetPass(0)) throw new InvalidOperationException("Text glyph shader is unavailable.");
                        Graphics.DrawMeshNow(batch.mesh, Matrix4x4.identity);
                    }
                }
                finally { GL.PopMatrix(); }
                if (mask == null || mask.width != outputWidth || mask.height != outputHeight)
                {
                    if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
                    mask = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false, true)
                    { name = "Text Mask", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                }
                mask.ReadPixels(new Rect(0, 0, outputWidth, outputHeight), 0, 0, false); mask.Apply(false, false);
                mask.filterMode = edgeMode == TextEdgeMode.Pixel ? FilterMode.Point : FilterMode.Bilinear;
                Bounds = new Rect(Bounds.position * pixelScale, Bounds.size * pixelScale);
                SourceRect = new Rect(SourceRect.position * pixelScale, SourceRect.size * pixelScale);
                for (int i = 0; i < pointLines.Count; i++) pointLines[i] *= pixelScale;
                key = layoutKey; cachedScale = scale; cachedOrigin = origin; cachedPixelScale = pixelScale;
                return mask;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; ProfiledRenderTexture.ReleaseTemporary(target); }
        }

        public void Dispose()
        {
            ReleaseMeshes(); palette?.Dispose(); palette = null;
            font?.Dispose(); smallFont?.Dispose();
            if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
            font = smallFont = null; mask = null;
        }

        private void ReleaseMeshes()
        {
            if (cachedBatches != null) foreach (var batch in cachedBatches)
                if (batch.mesh != null) UnityEngine.Object.DestroyImmediate(batch.mesh);
            cachedBatches = null;
        }

        internal Rect GradientFrame(int character) => character < 0 || characterFrames.Count == 0 ? inkBounds :
            characterFrames[Mathf.Clamp(character, 0, characterFrames.Count - 1)];
        internal int FindCharacter(Vector2 local)
        {
            for (int i = characterFrames.Count - 1; i >= 0; i--) if (characterFrames[i].Contains(local)) return i;
            return -1;
        }

        internal void Paint(TextLayerBehaviour layer, RenderTexture output)
        {
            var material = WhimTexMaterials.Text;
            if (!FigureFill.IsGradient(layer.fillMode))
            {
                material.SetColor("_TextColor", layer.fillMode == VectorFillMode.None ? Color.clear : HdrUtility.Decode(layer.color));
                Graphics.Blit(mask, output, material, 1); return;
            }
            (palette ??= new WhimTexGradientPalette()).Bind(material, layer.gradient);
            material.SetInt("_TextFillMode", (int)layer.fillMode);
            material.SetVector("_TextAnchors", new Vector4(layer.gradientStart.x, layer.gradientStart.y, layer.gradientEnd.x, layer.gradientEnd.y));
            material.SetFloat("_TextRepetitions", layer.gradientRepetitions);
            material.SetVector("_TextCanvasSize", new Vector4(output.width, output.height, 0, 0));
            material.SetVector("_TextPaintRect", new Vector4(paintPixels.x, paintPixels.y, paintPixels.width, paintPixels.height));
            if (layer.gradientScope == TextGradientScope.Text) { Graphics.Blit(mask, output, material, 2); return; }
            bool opaque = true;
            foreach (var key in layer.gradient.AlphaKeys) opaque &= key.alpha == 1;
            material.SetInt("_TextOpaqueGradient", opaque ? 1 : 0);
            material.SetTexture("_TextCoverage", mask);
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var temporary = ProfiledRenderTexture.GetTemporary(output.width, output.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                RenderTexture.active = temporary; GL.sRGBWrite = false; GL.Clear(false, true, Color.clear);
                material.SetInt("_TextPixel", layer.edgeMode == TextEdgeMode.Pixel ? 1 : 0);
                material.SetInt("_TextClip", clipGlyphs ? 1 : 0);
                material.SetVector("_TextClipRect", new Vector4(clipPixels.xMin, clipPixels.yMin, clipPixels.xMax, clipPixels.yMax));
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(-output.width * .5f, output.width * .5f, -output.height * .5f, output.height * .5f);
                    foreach (var batch in cachedBatches)
                    {
                        material.SetTexture("_MainTex", batch.atlas);
                        material.SetVector("_TextBoldOffset", new Vector4(batch.font.SyntheticBold / (float)batch.atlas.width,
                            batch.font.SyntheticBold / (float)batch.atlas.height, 0, 0));
                        if (!material.SetPass(3)) throw new InvalidOperationException("Text gradient shader is unavailable.");
                        Graphics.DrawMeshNow(batch.mesh, Matrix4x4.identity);
                    }
                }
                finally { GL.PopMatrix(); }
                Graphics.Blit(temporary, output, material, 4);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; ProfiledRenderTexture.ReleaseTemporary(temporary); }
        }
    }
}
