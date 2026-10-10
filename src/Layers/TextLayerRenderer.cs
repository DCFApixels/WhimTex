using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class TextLayerRenderer : IDisposable
    {
        private Font font;
        private string family, key;
        private int revision = -1;
        private Texture2D mask;
        private float cachedScale;
        private Mesh mesh;
        private readonly TextGenerator generator = new TextGenerator();
        internal Rect Bounds { get; private set; }
        internal Rect SourceRect { get; private set; }
        internal int FontSize { get; private set; }

        internal Rect GetBounds(TextLayerBehaviour layer, int width, int height)
        {
            if (mask == null || key != layer.LayoutKey(width, height) || family != layer.ResolvedFont || revision != SystemFontCatalog.Revision)
                GetMask(layer, width, height, 1);
            return Bounds;
        }

        internal Texture2D GetMask(TextLayerBehaviour layer, int width, int height, float scale)
        {
            width = Mathf.Max(1, width); height = Mathf.Max(1, height);
            if (font == null || family != layer.ResolvedFont || revision != SystemFontCatalog.Revision)
            {
                if (font != null) UnityEngine.Object.DestroyImmediate(font);
                family = layer.ResolvedFont; revision = SystemFontCatalog.Revision;
                font = Font.CreateDynamicFontFromOSFont(family, 64);
                font.hideFlags = HideFlags.HideAndDontSave;
                key = null;
            }
            string layoutKey = layer.LayoutKey(width, height);
            if (mask != null && key == layoutKey && cachedScale == scale) return mask;
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
                font.RequestCharactersInTexture(glyphText, full, layer.fontStyle);
                if (smallCaps && small != full) font.RequestCharactersInTexture(glyphText, small, layer.fontStyle);
            }
            bool WordSpace(char character) => character == ' ' || character == '\u00a0';
            float GlyphAdvance(string value)
            {
                float result = 0;
                foreach (char character in value)
                    if (font.GetCharacterInfo(smallCaps ? char.ToUpperInvariant(character) : character, out var info,
                        smallCaps && char.IsLower(character) ? smallSize : size, layer.fontStyle))
                        result += WordSpace(character) ? Mathf.Max(0, info.advance * horizontalScale + wordGap) : info.advance * horizontalScale;
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
                lineHeight = font.lineHeight * size / (float)font.fontSize;
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
                Request(size, smallSize);
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
                font.RequestCharactersInTexture("\u2026", size, layer.fontStyle);
                lines = TextLayout.Ellipsize(lines, lineOffsets, lineHeight, layer.frameSize, Measure);
                MeasureLines();
                glyphText += "\u2026";
            }
            FontSize = size;
            int horizontal = (int)layer.LayoutAlignment % 3, vertical = (int)layer.LayoutAlignment / 3;
            float extent = framed ? layer.frameSize.x : widest;
            float left = framed ? -layer.frameSize.x * .5f : -extent * horizontal * .5f;
            float top = framed ? layer.frameSize.y * .5f - (layer.frameSize.y - contentHeight) * vertical * .5f : contentHeight * vertical * .5f;
            Bounds = framed ? new Rect(-layer.frameSize * .5f, layer.frameSize) : new Rect(left, top - contentHeight, Mathf.Max(1, widest), contentHeight);
            SourceRect = Bounds;
            if (!framed || layer.overflow == TextOverflowMode.None)
            {
                float padding = size * .25f + 2;
                float horizontalPadding = size * .25f * horizontalScale + 2;
                float contentWidth = layer.CanJustify && layer.justify ? Mathf.Max(widest, extent) : widest;
                float contentLeft = left + (extent - contentWidth) * horizontal * .5f;
                SourceRect = Rect.MinMaxRect(Mathf.Min(Bounds.xMin, contentLeft) - horizontalPadding,
                    Mathf.Min(Bounds.yMin, top - contentHeight) - padding, Mathf.Max(Bounds.xMax, contentLeft + contentWidth) + horizontalPadding,
                    Mathf.Max(Bounds.yMax, top) + padding);
            }
            float renderScale = Mathf.Min(scale, Mathf.Min(SystemInfo.maxTextureSize, 8192f) / Mathf.Max(SourceRect.width, SourceRect.height),
                Mathf.Sqrt(16 * 1024 * 1024f / (SourceRect.width * SourceRect.height)));
            int outputWidth = Mathf.Max(1, Mathf.CeilToInt(SourceRect.width * renderScale));
            int outputHeight = Mathf.Max(1, Mathf.CeilToInt(SourceRect.height * renderScale));
            int renderSize = Mathf.Clamp(Mathf.RoundToInt(size * renderScale), 1, 2048);
            float actualScale = renderSize / (float)size;
            int renderSmallSize = Mathf.Clamp(Mathf.RoundToInt(smallSize * actualScale), 1, 2048);
            Request(renderSize, renderSmallSize);
            var settings = new TextGenerationSettings
            {
                font = font, fontSize = renderSize, fontStyle = layer.fontStyle, color = Color.white, scaleFactor = 1,
                lineSpacing = 1, textAnchor = TextAnchor.UpperLeft, generationExtents = Vector2.zero, pivot = Vector2.zero,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow, richText = false, updateBounds = false, generateOutOfBounds = true
            };
            var positions = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
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
                    settings.fontSize = small ? renderSmallSize : renderSize;
                    generator.Invalidate();
                    if (!generator.Populate(smallCaps ? run.ToUpperInvariant() : run, settings))
                        throw new InvalidOperationException("The system font could not generate this text.");
                    float baseline = small ? font.ascent * (renderSize - renderSmallSize) / (float)font.fontSize / actualScale : 0;
                    var vertices = generator.verts;
                    for (int q = 0; q + 3 < vertices.Count; q += 4)
                    {
                        float center = runX + (vertices[q].position.x + vertices[q + 2].position.x) * .5f * horizontalScale / actualScale;
                        int before = 0; if (justified) foreach (float gap in gaps) if (center > gap) before++;
                        float extra = justified ? before * (extent - lineWidth) / gaps.Count : 0;
                        int v = positions.Count;
                        for (int n = 0; n < 4; n++)
                        {
                            Vector3 p = vertices[q + n].position / actualScale;
                            p.x *= horizontalScale;
                            p.x += offsetX + runX + extra; p.y += offsetY - baseline;
                            p.x = (p.x - SourceRect.center.x) / SourceRect.width * outputWidth;
                            p.y = (p.y - SourceRect.center.y) / SourceRect.height * outputHeight;
                            positions.Add(p); uv.Add(vertices[q + n].uv0);
                        }
                        triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                        triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
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
            mesh ??= new Mesh { name = "Text Glyphs", hideFlags = HideFlags.HideAndDontSave };
            mesh.Clear(); mesh.SetVertices(positions); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            var target = RenderTexture.GetTemporary(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                RenderTexture.active = target; GL.sRGBWrite = false; GL.Clear(false, true, Color.clear);
                Material material = WhimTexMaterials.Text;
                material.SetTexture("_MainTex", font.material.mainTexture);
                material.SetVector("_TextCanvasSize", new Vector4(outputWidth, outputHeight, 0, 0));
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(-outputWidth * .5f, outputWidth * .5f, -outputHeight * .5f, outputHeight * .5f);
                    if (!material.SetPass(0)) throw new InvalidOperationException("Text glyph shader is unavailable.");
                    if (positions.Count > 0) Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                }
                finally { GL.PopMatrix(); }
                if (mask == null || mask.width != outputWidth || mask.height != outputHeight)
                {
                    if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
                    mask = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false, true)
                    { name = "Text Mask", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                }
                mask.ReadPixels(new Rect(0, 0, outputWidth, outputHeight), 0, 0, false); mask.Apply(false, false);
                key = layoutKey; cachedScale = scale;
                return mask;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(target); }
        }

        public void Dispose()
        {
            ((IDisposable)generator).Dispose();
            if (font != null) UnityEngine.Object.DestroyImmediate(font);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
            font = null; mesh = null; mask = null;
        }
    }
}
