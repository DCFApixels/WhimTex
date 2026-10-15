using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using WhimTex.Tests;

public static class TextEdgeModeTests
{
    // Private reflection is restricted to package-owned data, never Unity internals.
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static object Read(object obj, string field) => obj.GetType().GetField(field, Flags).GetValue(obj);
    static object Call(object obj, string method, params object[] args) => Array.Find((obj as Type ?? obj.GetType()).GetMethods(Flags),
        m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(obj is Type ? null : obj, args);
    static Color32[] Pixels(WhimTexDocument doc)
    {
        var image = doc.ComposeCanvas();
        try { return image.GetPixels32(); }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    static int Partial(Color32[] pixels) => pixels.Count(p => p.a > 0 && p.a < 255);
    static void Same(TestContext t, Color32[] a, Color32[] b, string label) => t.True(a.SequenceEqual(b), label);
    static object Json(string json) => Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true).GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
    static void CheckLargeGlyph(TestContext t, WhimTexDocument doc, TextLayerBehaviour text, int size,
        FontStyle style, string[] systemFaces, List<UnityEngine.Object> resources)
    {
        text.fontSize = size; text.fontStyle = style; text.text = "H";
        string label = text.edgeMode + "/" + size + "/" + style;
        t.True(Pixels(doc).Any(p => p.a > 0), "Large glyph has visible ink " + label);
        var font = Read(Read(text, "renderer"), "font");
        var asset = (FontAsset)Read(font, "Asset");
        t.Near(size, asset.faceInfo.pointSize, .001, "Font face retains the requested raster size " + label);
#pragma warning disable CS0618
        t.True(asset.characterLookupTable.TryGetValue('H', out var character), "Requested H is present rather than a replacement glyph " + label);
#pragma warning restore CS0618
        var glyph = character.glyph;
        t.True(glyph.metrics.height > size * .25f && glyph.metrics.height < size * 2f &&
            glyph.metrics.width > size * .15f && glyph.metrics.width < size * 2f &&
            glyph.metrics.horizontalAdvance > size * .15f && glyph.metrics.horizontalAdvance < size * 2f,
            "H metrics scale with the requested size, without a small-font fallback " + label);
        t.True((uint)glyph.atlasIndex < asset.atlasTextures.Length, "H uses a live atlas " + label);
        var atlas = asset.atlasTextures[glyph.atlasIndex];
        var rect = glyph.glyphRect;
        t.True(atlas != null && rect.width > 0 && rect.height > 0 && rect.x >= 0 && rect.y >= 0 &&
            rect.x + rect.width <= atlas.width && rect.y + rect.height <= atlas.height,
            "The large H bitmap fits its atlas " + label);
        bool Bold(string face) => face.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
        bool Italic(string face) => face.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
            face.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
        bool bold = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
        bool italic = style == FontStyle.Italic || style == FontStyle.BoldAndItalic;
        bool matchingFace = Array.Exists(systemFaces, entry =>
        {
            int split = entry.LastIndexOf(" - ", StringComparison.Ordinal);
            if (split < 1 || !entry.Substring(0, split).Equals(asset.faceInfo.familyName, StringComparison.OrdinalIgnoreCase)) return false;
            string candidate = entry.Substring(split + 3);
            return Bold(candidate) == bold && Italic(candidate) == italic;
        });
        if (matchingFace)
        {
            t.Equal(bold, Bold(asset.faceInfo.styleName), "Installed face has the requested Bold style " + label);
            t.Equal(italic, Italic(asset.faceInfo.styleName), "Installed face has the requested Italic style " + label);
        }
        else
        {
            t.Equal(bold, Bold(asset.faceInfo.styleName) || (int)Read(font, "SyntheticBold") > 0,
                "Unavailable Bold face retains the requested appearance " + label);
            t.Equal(italic, Italic(asset.faceInfo.styleName) || (bool)Read(font, "SyntheticItalic"),
                "Unavailable Italic face retains the requested appearance " + label);
        }
        resources.Add(asset); resources.Add(asset.material); resources.AddRange(asset.atlasTextures);
    }
    static void CheckOffset(TestContext t, WhimTexDocument doc, TextLayerBehaviour text, Layer layer)
    {
        int oldHeight = doc.height; string oldText = text.text; float oldSize = text.fontSize;
        Vector2 oldFrame = text.frameSize; var transform = layer.transform;
        try
        {
            doc.height = 256; text.fontSize = 22; text.frameSize = new Vector2(160, 70);
            text.text = "Offset text\nSecond line\nThird line\nFourth line";
            foreach (TextLayoutMode layout in Enum.GetValues(typeof(TextLayoutMode)))
            foreach (TextOverflowMode overflow in Enum.GetValues(typeof(TextOverflowMode)))
            {
                text.layoutMode = layout; text.overflow = overflow; text.layoutOffset = Vector2.zero;
                Color32[] baseline = Pixels(doc);
                text.layoutOffset = new Vector2(11, -7);
                Color32[] shifted = Pixels(doc);
                int errors = 0;
                for (int y = 0; y < doc.height; y++)
                for (int x = 0; x < doc.width; x++)
                {
                    int sx = x - 11, sy = y + 7;
                    byte expected = sx >= 0 && sx < doc.width && sy >= 0 && sy < doc.height ? baseline[sy * doc.width + sx].a : (byte)0;
                    if (Math.Abs(shifted[y * doc.width + x].a - expected) > 1) errors++;
                }
                t.Equal(0, errors, "Local integer offset shifts coverage without distortion " + text.edgeMode + "/" + layout + "/" + overflow);
                t.True(layer.transform.Equals(transform), "Rendering offset does not edit Transform");
            }
            foreach (TextLayoutMode layout in Enum.GetValues(typeof(TextLayoutMode)))
            {
                text.layoutMode = layout; text.overflow = TextOverflowMode.None;
                text.layoutOffset = new Vector2(.37f, -.28f);
                text.frameSize = new Vector2(160.7f, 70.3f);
                var fractional = Pixels(doc); var renderer = Read(text, "renderer");
                var mask = (Texture2D)Read(renderer, "mask");
                if (text.edgeMode == TextEdgeMode.Pixel) t.Equal(0, Partial(fractional), "Fractional local layout retains Pixel binary coverage " + layout);
                if (text.edgeMode == TextEdgeMode.HintedAA)
                {
                    var allowed = new HashSet<byte>(mask.GetPixels32().Select(p => p.a)); allowed.Add(0);
                    t.True(fractional.All(p => allowed.Any(a => Math.Abs(a - p.a) <= 1)), "Fractional offset/size add no post-raster bilinear softening " + layout);
                }
            }
        }
        finally
        {
            doc.height = oldHeight; text.text = oldText; text.fontSize = oldSize; text.frameSize = oldFrame;
            text.layoutMode = TextLayoutMode.Point; text.overflow = TextOverflowMode.None; text.layoutOffset = Vector2.zero;
        }
    }
    public static string Run() => TestContext.Run("Three native text edge modes", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        var text = new TextLayerBehaviour { text = "Text 123 ЧЧ", fontSize = 17 };
        var layer = new Layer(text); Call(layer, "AssignNewId"); doc.layers.Add(layer);
        var resources = new List<UnityEngine.Object>();
        RenderTexture active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Texture2D sheet = null;
        var fontMessages = new List<string>();
        Application.LogCallback capture = (message, stack, kind) =>
        {
            if ((kind == LogType.Warning || kind == LogType.Error || kind == LogType.Exception) &&
                message.IndexOf("font face", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (message.IndexOf("Unable to load", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("Unable to find", StringComparison.OrdinalIgnoreCase) >= 0))
                fontMessages.Add(message);
        };
        Application.logMessageReceived += capture;
        try
        {
            t.Equal(TextEdgeMode.HintedAA, text.edgeMode, "New text defaults to Hinted AA");
            var catalog = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.SystemFontCatalog");
            string[] systemFaces = FontEngine.GetSystemFontNames() ?? Array.Empty<string>();
            t.True(!string.IsNullOrEmpty(text.fontFamily), "A real installed system font is available");
            var originals = new Dictionary<TextEdgeMode, Color32[]>();
            sheet = new Texture2D(512, 288, TextureFormat.RGBA32, false, true);
            foreach (TextEdgeMode mode in Enum.GetValues(typeof(TextEdgeMode)))
            {
                Call(typeof(WhimTexApi), "SetText", text, Json("{\"edgeMode\":\"" + mode + "\"}"));
                t.Equal(mode, text.edgeMode, "API accepts " + mode);
                foreach (int width in new[] { 512, 513 })
                foreach (int size in new[] { 13, 17, 32, 64 })
                {
                    doc.width = width; doc.height = 96; text.fontSize = size;
                    Color32[] pixels = Pixels(doc);
                    t.True(pixels.Any(p => p.a > 128), mode + " has visible ink at " + size + "/" + width);
                    if (mode == TextEdgeMode.Pixel) t.Equal(0, Partial(pixels), "Pixel has binary alpha through the document transform " + size + "/" + width);
                    else t.True(Partial(pixels) > 0, mode + " has fractional coverage");
                    var renderer = Read(text, "renderer");
                    var rect = (Rect)renderer.GetType().GetProperty("SourceRect", Flags).GetValue(renderer);
                    t.Near(Math.Round(rect.xMin + width * .5), rect.xMin + width * .5, .0001, "Mask origin aligns to canvas X");
                    t.Near(Math.Round(rect.yMin + doc.height * .5), rect.yMin + doc.height * .5, .0001, "Mask origin aligns to canvas Y");
                    var mask = (Texture2D)Read(renderer, "mask");
                    if (size == 17 && width == 512)
                    {
                        originals[mode] = pixels;
                        Same(t, pixels, Pixels(doc), "Repeated render uses the same appearance");
                        var values = pixels.Select(p => new Color32((byte)(255 - p.a), (byte)(255 - p.a), (byte)(255 - p.a), 255)).ToArray();
                        sheet.SetPixels32(0, (2 - (int)mode) * 96, 512, 96, values);
                        var native = (FontAsset)Read(Read(renderer, "font"), "Asset");
                        resources.Add(native); resources.Add(native.material); resources.AddRange(native.atlasTextures);
                        t.True(native.atlasRenderMode.ToString() == (mode == TextEdgeMode.Pixel ? "RASTER_HINTED" : mode == TextEdgeMode.Antialiased ? "SMOOTH" : "SMOOTH_HINTED"), "Mode selects the real font rasterizer");
                    }
                    if (mode == TextEdgeMode.HintedAA)
                    {
                        // At identity and 1:1 scale, the canvas must not introduce new intermediate alpha values.
                        var allowed = new HashSet<byte>(mask.GetPixels32().Select(p => p.a)); allowed.Add(0);
                        t.True(pixels.All(p => allowed.Any(a => Math.Abs(a - p.a) <= 1)), "Centered mask is not bilinearly softened again");
                    }
                }
                doc.width = 512; text.fontSize = 32;
                foreach (FontStyle style in Enum.GetValues(typeof(FontStyle)))
                {
                    text.fontStyle = style;
                    t.True(Pixels(doc).Any(p => p.a > 0), mode + " supports " + style);
                }
                text.fontStyle = FontStyle.Normal;
                CheckOffset(t, doc, text, layer);
                Call(typeof(WhimTexApi), "SetText", text, Json("{\"layoutOffset\":[11,-7]}"));
                t.Equal(new Vector2(11, -7), text.layoutOffset, "API accepts local layout offset");
                foreach (var writeMode in new[] { WhimTexJsonWriteMode.Full, WhimTexJsonWriteMode.Compact })
                {
                    var saved = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = writeMode });
                    using var reopened = WhimTexDocumentJson.Read(saved.Json, false);
                    t.Equal(mode, ((TextLayerBehaviour)reopened.Document.layers[0].Behaviour).edgeMode, "JSON retains mode " + mode);
                    t.Equal(text.layoutOffset, ((TextLayerBehaviour)reopened.Document.layers[0].Behaviour).layoutOffset, "JSON retains layout offset " + writeMode);
                    Same(t, Pixels(doc), Pixels(reopened.Document), "JSON retains pixels " + mode);
                }
                var assembly = typeof(WhimTexDocument).Assembly;
                object container = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentContainer"), true);
                WhimTexDocument restored = null;
                try
                {
                    var serializer = assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
                    byte[] data = (byte[])Call(serializer, "Serialize", doc, container);
                    object decoded = Call(serializer, "Deserialize", data, container, typeof(WhimTexDocument), null, false);
                    restored = (WhimTexDocument)decoded.GetType().GetProperty("Model", Flags).GetValue(decoded);
                    t.Equal(mode, ((TextLayerBehaviour)restored.layers[0].Behaviour).edgeMode, "TIFF model retains mode " + mode);
                    t.Equal(text.layoutOffset, ((TextLayerBehaviour)restored.layers[0].Behaviour).layoutOffset, "TIFF model retains layout offset");
                    Same(t, Pixels(doc), Pixels(restored), "TIFF model retains pixels " + mode);
                }
                finally
                {
                    if (restored != null) UnityEngine.Object.DestroyImmediate(restored);
                    ((IDisposable)container).Dispose();
                }
                var outline = new Layer(new OutlineLayerBehaviour { outlineWidth = 2 }); Call(outline, "AssignNewId");
                doc.layers.Insert(0, outline);
                try
                {
                    var previous = Pixels(doc);
                    var effect = (OutlineLayerBehaviour)outline.Behaviour;
                    effect.inputMode = EffectInputMode.Specific; effect.TargetLayerId = layer.Id;
                    Same(t, previous, Pixels(doc), "Outline Previous and Specific text input agree " + mode);
                    t.True(previous.Any(p => p.a > 0), "Outline receives visible text " + mode);
                }
                finally { doc.layers.Remove(outline); Call(outline, "ReleaseTransientResources"); }
                // A fractionally translated, rotated Pixel layer remains binary with Source filtering.
                var transform = TextureTransform.Default; transform.position = new Vector2(.4f, -.3f); transform.rotation = 9;
                layer.transform = transform;
                if (mode == TextEdgeMode.Pixel) t.Equal(0, Partial(Pixels(doc)), "Pixel default filter remains nearest under transform");
                layer.transform = TextureTransform.Default;
                var grouped = new Layer(new GroupLayerBehaviour()); Call(grouped, "AssignNewId");
                doc.layers.Clear(); grouped.children.Add(layer); doc.layers.Add(grouped);
                t.True(Pixels(doc).Any(p => p.a > 0), mode + " works inside a group");
                grouped.children.Clear(); doc.layers.Clear(); doc.layers.Add(layer); Call(grouped, "ReleaseTransientResources");
                foreach (int size in new[] { 256, 257, 512, 2048 })
                    CheckLargeGlyph(t, doc, text, size, FontStyle.Normal, systemFaces, resources);
                foreach (FontStyle style in new[] { FontStyle.Bold, FontStyle.Italic, FontStyle.BoldAndItalic })
                    CheckLargeGlyph(t, doc, text, 512, style, systemFaces, resources);
                text.text = "Text 123 ЧЧ"; text.fontSize = 17;
                text.fontStyle = FontStyle.Normal;
                text.layoutOffset = Vector2.zero;
            }
            t.True(!originals[TextEdgeMode.Antialiased].SequenceEqual(originals[TextEdgeMode.HintedAA]), "Hinted AA and unhinted AA are distinct native rasters");
            text.layoutOffset = new Vector2(7.3f, -4.2f);
            Call(text, "SetLayoutMode", TextLayoutMode.Frame); Call(text, "SetLayoutMode", TextLayoutMode.Point);
            t.Equal(new Vector2(7.3f, -4.2f), text.layoutOffset, "Point/Frame switches retain local position");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1000001f, -1000001f })
            {
                text.layoutOffset = new Vector2(invalid, 0);
                try { Call(text, "Validate"); t.True(false, "Invalid layout offset must fail"); }
                catch (TargetInvocationException) { t.True(true, "Nonfinite or out-of-range layout offset is rejected"); }
            }
            text.layoutOffset = Vector2.zero;
            try { Call(typeof(WhimTexApi), "SetText", text, Json("{\"layoutOffset\":[1000001,0]}")); t.True(false, "Out-of-range API layout offset must fail"); }
            catch (TargetInvocationException) { t.True(true, "API rejects out-of-range layout offset"); }
            text.layoutOffset = Vector2.zero;
            text.edgeMode = TextEdgeMode.Pixel;
            var availableField = catalog.GetField("available", Flags); object available = availableField.GetValue(null);
            Call(text, "PrepareSavedAppearance", doc.width, doc.height);
            var before = Pixels(doc);
            try
            {
                availableField.SetValue(null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                Same(t, before, Pixels(doc), "Missing font keeps Pixel appearance");
                text.edgeMode = TextEdgeMode.HintedAA;
                t.True(Pixels(doc).All(p => p.a == 0), "Switching mode cannot reuse a stale missing-font mask");
            }
            finally { availableField.SetValue(null, available); }
            Call(typeof(WhimTexApi), "SetText", text, Json("{\"edgeMode\":\"HintedAA\"}"));
            try { Call(typeof(WhimTexApi), "SetText", text, Json("{\"edgeMode\":\"Invalid\"}")); t.True(false, "Unknown edge mode must fail"); }
            catch (TargetInvocationException) { t.True(true, "Unknown edge mode is rejected"); }
            sheet.Apply();
            string directory = "Temp/WhimTex/text-edges"; Directory.CreateDirectory(directory);
            File.WriteAllBytes(directory + "/modes-" + Guid.NewGuid().ToString("N") + ".png", sheet.EncodeToPNG());
            t.True(RenderTexture.active == active && GL.sRGBWrite == srgb, "All edge modes restore caller render state");
            Call(layer, "ReleaseTransientResources");
            t.True(resources.All(obj => obj == null), "Changing settings and disposing releases owned atlases, materials and fonts");
            t.Equal(0, fontMessages.Count, "Native text rasterization emits no font-face load/find warnings or errors: " + string.Join("; ", fontMessages));
        }
        finally
        {
            Application.logMessageReceived -= capture;
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    });
}
