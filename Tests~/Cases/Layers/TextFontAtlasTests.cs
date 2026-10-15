using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using WhimTex.Tests;

public static class TextFontAtlasTests
{
    const int Size = 512;
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly uint[] Characters = { 'H', 'W', 'i', 'g', '?' };
    sealed class Face { internal string path; internal int index; internal FaceInfo info; internal Glyph[] samples; }
    [Serializable] public sealed class Observation
    { public string path, family, style, checksum; public int index, extent, atlasCount, ink, partial; }
    [Serializable] sealed class Details
    { public bool collectionAvailable, arialAvailable; public string restoredFace; public string[] warnings; }

    static Glyph[] Samples()
    {
        var result = new Glyph[Characters.Length];
        for (int i = 0; i < result.Length; i++)
            if (!FontEngine.TryGetGlyphWithUnicodeValue(Characters[i], GlyphLoadFlags.LOAD_DEFAULT, out result[i])) return null;
        return result;
    }
    // Bound the environmental scan and require distinct glyphs to detect a stale first collection face.
    static List<Face> FindFaces()
    {
        var result = new List<Face>(); string[] paths = Font.GetPathsToOSFonts() ?? Array.Empty<string>();
        foreach (string path in paths.Where(p => Path.GetExtension(p).Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileNameWithoutExtension(p).StartsWith("Arial", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetFileName(p).Equals("arial.ttf", StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(12))
        {
            if (FontEngine.LoadFontFace(path, Size, 0) != FontEngineError.Success) continue;
            FaceInfo info = FontEngine.GetFaceInfo();
            if (info.familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase) && info.styleName.Equals("Regular", StringComparison.OrdinalIgnoreCase))
            { result.Add(new Face { path = path, index = 0, info = info }); break; }
        }
        if (result.Count == 0)
            foreach (string path in paths.Where(p => Path.GetExtension(p).Equals(".ttf", StringComparison.OrdinalIgnoreCase)).Take(16))
            {
                if (FontEngine.LoadFontFace(path, Size, 0) != FontEngineError.Success) continue;
                FaceInfo info = FontEngine.GetFaceInfo();
                if (info.styleName.Equals("Regular", StringComparison.OrdinalIgnoreCase) &&
                    Characters.All(c => FontEngine.TryGetGlyphWithUnicodeValue(c, GlyphLoadFlags.LOAD_DEFAULT, out _)))
                { result.Add(new Face { path = path, index = 0, info = info }); break; }
            }
        foreach (string path in paths.Where(p => Path.GetExtension(p).Equals(".ttc", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetFileName(p).Equals("msgothic.ttc", StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(8))
        {
            if (FontEngine.LoadFontFace(path, Size, 0) != FontEngineError.Success) continue;
            int count = Math.Min(4, FontEngine.GetFontFaces()?.Length ?? 0); var selected = new List<Face>();
            for (int index = 0; index < count && selected.Count < 2; index++)
            {
                if (FontEngine.LoadFontFace(path, Size, index) != FontEngineError.Success) continue;
                var samples = Samples(); if (samples == null) continue;
                selected.Add(new Face { path = path, index = index, info = FontEngine.GetFaceInfo(), samples = samples });
            }
            if (selected.Count == 2 && selected[1].index > 0 && selected[0].samples.Where((glyph, i) =>
                glyph.index != selected[1].samples[i].index || !glyph.metrics.Equals(selected[1].samples[i].metrics)).Any())
            { result.AddRange(selected); break; }
        }
        return result;
    }

    public static string Run()
    {
        FaceInfo initialFace = FontEngine.GetFaceInfo(); RenderTexture initialTarget = RenderTexture.active; bool initialSrgb = GL.sRGBWrite;
        var observations = new List<Observation>(); var warnings = new List<string>(); string restoration = "No initial public face";
        bool collection = false, arial = false;
        Application.LogCallback observe = (message, stack, kind) =>
        {
            if ((kind == LogType.Warning || kind == LogType.Error || kind == LogType.Exception) &&
                message.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (message.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)) warnings.Add(message);
        };
        string result = TestContext.Run("Large system-font atlases retain the selected TTF/TTC face", t =>
        {
            Application.logMessageReceived += observe;
            try
            {
                List<Face> faces = FindFaces(); collection = faces.Any(f => f.index > 0); arial = faces.Any(f => f.info.familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase));
                t.True(faces.Any(f => Path.GetExtension(f.path).Equals(".ttf", StringComparison.OrdinalIgnoreCase)), "Installed regular TTF is available; Arial is preferred where present.");
                Type fontType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.TextRasterFont", true);
                foreach (Face face in faces)
                {
                    t.Equal(FontEngineError.Success, FontEngine.LoadFontFace(face.path, Size, face.index), "Independent desired file face loads.");
                    var expected = new Dictionary<uint, Glyph>();
                    foreach (uint c in Characters)
                    { t.True(FontEngine.TryGetGlyphWithUnicodeValue(c, GlyphLoadFlags.LOAD_DEFAULT, out var glyph), "Desired face supplies the requested glyph " + c); expected[c] = glyph; }
                    object font = null; FontAsset asset = null; Material material = null; Texture2D[] atlases = Array.Empty<Texture2D>();
                    try
                    {
                        string name = face.info.familyName + " " + face.info.styleName;
                        font = Activator.CreateInstance(fontType, Flags, null, new object[] { name, FontStyle.Normal, Size, TextEdgeMode.HintedAA }, null);
                        asset = (FontAsset)fontType.GetField("Asset", Flags).GetValue(font);
                        fontType.GetMethod("Add", Flags).Invoke(font, new object[] { "H Wig?" });
                        material = asset.material; atlases = asset.atlasTextures.ToArray();
                        int extent = Mathf.Min(SystemInfo.maxTextureSize, Mathf.NextPowerOfTwo(Size * 2 + 12));
                        t.Equal(face.info.familyName, asset.faceInfo.familyName, "Atlas keeps selected collection family.");
                        t.Equal(face.info.styleName, asset.faceInfo.styleName, "Atlas keeps selected collection style.");
                        t.Near(Size, asset.faceInfo.pointSize, .001, "Atlas never silently falls back to a small sampling size.");
                        t.Equal(extent, asset.atlasWidth, "Large atlas width matches the requested extent.");
                        t.Equal(extent, asset.atlasHeight, "Large atlas height matches the requested extent.");
                        t.Equal(GlyphRenderMode.SMOOTH_HINTED, asset.atlasRenderMode, "Large atlas preserves the hinted bitmap rendering mode.");
                        t.Equal(HideFlags.HideAndDontSave, asset.hideFlags, "Owned font asset remains transient.");
                        uint checksum = 2166136261; int ink = 0, partial = 0;
                        foreach (uint c in Characters)
                        {
#pragma warning disable CS0618
                            t.True(asset.characterLookupTable.TryGetValue(c, out var character), "Atlas contains the actual glyph rather than a replacement " + c);
#pragma warning restore CS0618
                            Glyph actual = character.glyph, reference = expected[c];
                            t.Equal(reference.index, actual.index, "Glyph index matches the selected file face.");
                            t.Near(reference.metrics.horizontalAdvance, actual.metrics.horizontalAdvance, .01, "Glyph advance matches the independently loaded face.");
                            t.Near(reference.metrics.width, actual.metrics.width, .01, "Glyph width matches the independently loaded face.");
                            t.Near(reference.metrics.height, actual.metrics.height, .01, "Glyph height matches the independently loaded face.");
                            t.True((uint)actual.atlasIndex < atlases.Length, "Glyph uses a live owned atlas.");
                            Texture2D atlas = atlases[actual.atlasIndex]; GlyphRect rect = actual.glyphRect;
                            t.True(atlas != null && rect.width > 0 && rect.height > 0 && rect.x >= 0 && rect.y >= 0 &&
                                rect.x + rect.width <= atlas.width && rect.y + rect.height <= atlas.height, "Large glyph bitmap fits the atlas.");
                            t.Equal(HideFlags.HideAndDontSave, atlas.hideFlags, "Glyph atlas stays transient.");
                            var pixels = atlas.GetRawTextureData<byte>();
                            t.True(atlas.format == TextureFormat.Alpha8 || atlas.format == TextureFormat.R8, "Bitmap atlas exposes one-byte coverage through a public API.");
                            for (int y = rect.y; y < rect.y + rect.height; y++) for (int x = rect.x; x < rect.x + rect.width; x++)
                            {
                                byte a = pixels[y * atlas.width + x]; if (a > 0) ink++; if (a > 0 && a < 255) partial++;
                                unchecked { checksum = (checksum ^ a) * 16777619; }
                            }
                        }
                        t.True(ink > Size && partial > 0, "The large hinted-AA atlas contains visible ink and fractional contour pixels.");
                        int glyphCount = asset.glyphTable.Count;
                        fontType.GetMethod("Add", Flags).Invoke(font, new object[] { "H Wig?" });
                        t.Equal(glyphCount, asset.glyphTable.Count, "Repeated population reuses glyphs instead of appending duplicates.");
                        t.Equal(atlases.Length, asset.atlasTextures.Length, "Repeated population does not allocate another atlas.");
                        observations.Add(new Observation { path = face.path, index = face.index, family = face.info.familyName, style = face.info.styleName,
                            extent = extent, atlasCount = atlases.Length, ink = ink, partial = partial, checksum = checksum.ToString("x8") });
                    }
                    finally { if (font is IDisposable disposable) disposable.Dispose(); }
                    t.True(asset == null && material == null && atlases.All(a => a == null), "Production disposal destroys the owned font asset, material and every atlas.");
                }
            }
            finally
            {
                Application.logMessageReceived -= observe; RenderTexture.active = initialTarget; GL.sRGBWrite = initialSrgb;
                if (!string.IsNullOrEmpty(initialFace.familyName) && initialFace.pointSize > 0)
                {
                    var restored = FontEngine.LoadFontFace(initialFace.familyName, initialFace.styleName, Mathf.RoundToInt(initialFace.pointSize));
                    restoration = restored.ToString(); t.Equal(FontEngineError.Success, restored, "The initial font face is restored through the public family/style loader.");
                }
            }
            t.Equal(0, warnings.Count, "Font atlas creation/population emits no font-load warning: " + string.Join(" | ", warnings));
        });
        var report = JsonUtility.FromJson<TestResult>(result);
        if (report.status == "passed" && !collection)
        { report.status = "skipped"; report.message += "; no supported nonzero TTC face was found in the bounded scan, so collection-face preservation remains unverified"; }
        string details = JsonUtility.ToJson(new Details { collectionAvailable = collection, arialAvailable = arial, restoredFace = restoration, warnings = warnings.ToArray() });
        string facesJson = "[" + string.Join(",", observations.Select(face => JsonUtility.ToJson(face))) + "]";
        report.message += " " + details.Substring(0, details.Length - 1) + ",\"faces\":" + facesJson + "}";
        return report.ToJson();
    }
}
