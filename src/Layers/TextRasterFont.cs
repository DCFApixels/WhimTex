using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace DCFApixels.WhimTex
{
    internal sealed class TextRasterFont : IDisposable
    {
        internal readonly FontAsset Asset;
        internal readonly int Size;
        internal readonly bool SyntheticItalic;
        internal readonly int SyntheticBold;
        private readonly FilterMode filter;
        private static readonly Dictionary<string, (string path, int index)> files = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
        private static int filesRevision = -1;
        internal TextRasterFont(string name, FontStyle style, int size, TextEdgeMode edges)
        {
            Size = size;
            filter = edges == TextEdgeMode.Pixel ? FilterMode.Point : FilterMode.Bilinear;
            string family = name, baseStyle = "Regular";
            string[] faces = FontEngine.GetSystemFontNames() ?? Array.Empty<string>();
            bool exactFamily = Array.Exists(faces, entry => entry.StartsWith(name + " - ", StringComparison.OrdinalIgnoreCase));
            if (!exactFamily)
                foreach (string entry in faces)
                {
                    int split = entry.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (split > 0 && (entry.Substring(0, split) + " " + entry.Substring(split + 3)).Equals(name, StringComparison.OrdinalIgnoreCase))
                    { family = entry.Substring(0, split); baseStyle = entry.Substring(split + 3); break; }
                }
            bool Bold(string value) => value.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
            bool Italic(string value) => value.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
            bool bold = style == FontStyle.Bold || style == FontStyle.BoldAndItalic || Bold(baseStyle);
            bool italic = style == FontStyle.Italic || style == FontStyle.BoldAndItalic || Italic(baseStyle);
            string selected = baseStyle; int best = int.MinValue;
            foreach (string entry in faces)
            {
                int split = entry.LastIndexOf(" - ", StringComparison.Ordinal);
                if (split < 1 || !entry.Substring(0, split).Equals(family, StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = entry.Substring(split + 3);
                int score = (Bold(candidate) == bold ? 100 : 0) + (Italic(candidate) == italic ? 100 : 0) +
                    (candidate.Equals(baseStyle, StringComparison.OrdinalIgnoreCase) ? 4 : 0) +
                    (candidate.Equals("Regular", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
                if (score > best) { best = score; selected = candidate; }
            }
            SyntheticItalic = italic && !Italic(selected);
            SyntheticBold = bold && !Bold(selected) ? Mathf.Max(1, Mathf.RoundToInt(size / 32f)) : 0;
            var mode = edges == TextEdgeMode.Pixel ? GlyphRenderMode.RASTER_HINTED :
                edges == TextEdgeMode.Antialiased ? GlyphRenderMode.SMOOTH : GlyphRenderMode.SMOOTH_HINTED;
            int padding = Mathf.Max(2, SyntheticBold + 1);
            // Family-based creation uses a fixed atlas. Large glyphs need an explicitly sized atlas.
            if (size > 256 && TryFindFile(family, selected, out var file))
            {
                int extent = Mathf.Min(SystemInfo.maxTextureSize, Mathf.NextPowerOfTwo(size * 2 + padding * 2 + 8));
                Asset = CreateLargeAsset(file, size, padding, mode, extent);
            }
            else Asset = FontAsset.CreateFontAsset(family, selected, size, padding, mode);
            if (Asset == null) throw new InvalidOperationException("The system font could not create a text raster: " + name);
            Asset.hideFlags = HideFlags.HideAndDontSave;
            Asset.isMultiAtlasTexturesEnabled = true;
            HideResources();
        }
        internal void Add(string text)
        {
            Asset.TryAddCharacters(text + "\uFFFD?");
            HideResources();
        }
        private static FontAsset CreateLargeAsset((string path, int index) file, int size, int padding,
            GlyphRenderMode mode, int extent)
        {
            if (FontEngine.LoadFontFace(file.path, size, file.index) != FontEngineError.Success) return null;
            var face = FontEngine.GetFaceInfo();
            Font source = null;
            FontAsset asset = null;
            try
            {
                // The file-path factory initializes Dynamic tables before assigning its source path.
                // A temporary Font lets the public factory initialize DynamicOS with a valid face.
                source = new Font(file.path) { hideFlags = HideFlags.HideAndDontSave };
                if (FontEngine.LoadFontFace(source, size) != FontEngineError.Success) return null;
                asset = FontAsset.CreateFontAsset(source, size, padding, mode, extent, extent, AtlasPopulationMode.DynamicOS);
                if (asset == null) return null;
                if (asset.atlasWidth != extent || asset.atlasHeight != extent || asset.atlasRenderMode != mode ||
                    asset.faceInfo.pointSize != size)
                    throw new InvalidOperationException("The system font could not create the requested raster atlas: " + face.familyName);
                asset.hideFlags = HideFlags.HideAndDontSave;
                foreach (var texture in asset.atlasTextures)
                    if (texture != null) texture.hideFlags = HideFlags.HideAndDontSave;
                if (asset.material != null) asset.material.hideFlags = HideFlags.HideAndDontSave;
                // The Font overload starts with face zero. Preserve the selected face of a collection.
                if (file.index != 0 || asset.faceInfo.familyName != face.familyName ||
                    asset.faceInfo.styleName != face.styleName)
                {
                    asset.faceInfo = face;
                    asset.ClearFontAssetData(true);
                    string name = face.familyName + " - " + face.styleName;
                    asset.name = name + " Raster";
                    if (asset.material != null) asset.material.name = name + " Material";
                    foreach (var texture in asset.atlasTextures)
                        if (texture != null) texture.name = name + " Atlas";
                }
                return asset;
            }
            catch { Release(asset); throw; }
            finally { if (source != null) UnityEngine.Object.DestroyImmediate(source); }
        }
        private static bool TryFindFile(string family, string style, out (string path, int index) found)
        {
            if (filesRevision != SystemFontCatalog.Revision) { files.Clear(); filesRevision = SystemFontCatalog.Revision; }
            string key = family + "\0" + style;
            if (files.TryGetValue(key, out found)) return found.path != null;
            foreach (string path in Font.GetPathsToOSFonts() ?? Array.Empty<string>())
            {
                if (FontEngine.LoadFontFace(path, 16) != FontEngineError.Success) continue;
                int count = Mathf.Max(1, FontEngine.GetFontFaces()?.Length ?? 0);
                for (int index = 0; index < count; index++)
                {
                    if (index > 0 && FontEngine.LoadFontFace(path, 16f, index) != FontEngineError.Success) continue;
                    var info = FontEngine.GetFaceInfo();
                    files[info.familyName + "\0" + info.styleName] = (path, index);
                    if (files.TryGetValue(key, out found)) return true;
                }
            }
            files[key] = found = default;
            return false;
        }
        // The public bitmap layout API still exposes glyph metrics through this table.
#pragma warning disable CS0618
        internal Glyph Glyph(uint unicode)
        {
            if (Asset.characterLookupTable.TryGetValue(unicode, out var character) ||
                Asset.characterLookupTable.TryGetValue(0xFFFD, out character) ||
                Asset.characterLookupTable.TryGetValue('?', out character)) return character.glyph;
            return null;
        }
#pragma warning restore CS0618
        private void HideResources()
        {
            foreach (var texture in Asset.atlasTextures)
                if (texture != null) { texture.hideFlags = HideFlags.HideAndDontSave; texture.filterMode = filter; }
            if (Asset.material != null) Asset.material.hideFlags = HideFlags.HideAndDontSave;
        }
        private static void Release(FontAsset asset)
        {
            if (asset == null) return;
            foreach (var texture in asset.atlasTextures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            if (asset.material != null) UnityEngine.Object.DestroyImmediate(asset.material);
            UnityEngine.Object.DestroyImmediate(asset);
        }
        public void Dispose() => Release(Asset);
    }
}
