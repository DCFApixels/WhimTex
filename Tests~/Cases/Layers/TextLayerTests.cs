using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class TextLayerTests
{
    // Reflection is restricted to package-owned types. All Unity APIs are public.
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Product = typeof(WhimTexDocument).Assembly;
    static Type Type(string name) => Product.GetType("DCFApixels.WhimTex." + name, true);
    static object Json(string value) => System.Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true)
        .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { value });
    static object Node(object value, params object[] keys)
    {
        foreach (object key in keys) value = value.GetType().GetProperty("Item", new[] { key.GetType() }).GetValue(value, new[] { key });
        return value;
    }
    static object Call(object owner, string name, params object[] args) => Array.Find((owner as Type ?? owner.GetType()).GetMethods(Flags),
        method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(owner is Type ? null : owner, args);
    static void NewId(Layer layer) => Call(layer, "AssignNewId");
    static Color[] Pixels(WhimTexDocument document)
    {
        Texture2D texture = document.ComposeCanvas();
        try { return texture.GetPixels(); }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    static Rect Bounds(Color[] pixels, int width)
    {
        int left = width, right = -1, bottom = pixels.Length / width, top = -1;
        for (int i = 0; i < pixels.Length; i++) if (pixels[i].a > .05f)
        { int x = i % width, y = i / width; left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y); }
        return right < left ? Rect.zero : Rect.MinMaxRect(left, bottom, right + 1, top + 1);
    }
    static void EqualPixels(TestContext t, Color[] expected, Color[] actual, string message)
    {
        t.Equal(expected.Length, actual.Length, message + " dimensions");
        float delta = 0;
        for (int i = 0; i < expected.Length; i++)
            for (int channel = 0; channel < 4; channel++) delta = Mathf.Max(delta, Mathf.Abs(expected[i][channel] - actual[i][channel]));
        t.Near(0, delta, .00001, message + " RGBA");
    }
    static WhimTexDocument Document()
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = 384; document.height = 192;
        return document;
    }
    public static string Run() => TestContext.Run("Text layer rendering, system fonts and persistence", t =>
    {
        RenderTexture original = RenderTexture.active; bool originalSrgb = GL.sRGBWrite;
        var document = Document(); var restored = new List<WhimTexDocument>();
        var catalog = Type("SystemFontCatalog");
        var names = (IReadOnlyList<string>)catalog.GetProperty("Names", Flags).GetValue(null);
        var availableField = catalog.GetField("available", Flags);
        object available = availableField.GetValue(null);
        var ownedLayers = new List<Layer>();
        RenderTexture sentinel = RenderTexture.GetTemporary(3, 3);
        int warnings = 0;
        Application.LogCallback observe = (message, stack, kind) => { if (kind == LogType.Warning && message.StartsWith("WhimTex Text:")) warnings++; };
        Application.logMessageReceived += observe;
        try
        {
            t.True(names.Count > 0, "OS font catalog is available without imported project fonts");
            string family = (string)catalog.GetProperty("DefaultName", Flags).GetValue(null);
            CheckHorizontalScale(t, family, restored);
            var text = new TextLayerBehaviour { text = "WhimTex", fontFamily = family, fontSize = 32,
                layoutMode = TextLayoutMode.Frame, frameSize = new Vector2(384,192), wrapping = TextWrapping.Manual, overflow = TextOverflowMode.Clip };
            t.Equal(TextOverflowMode.None, new TextLayerBehaviour().overflow, "New text does not clip overflow by default");
            var layer = new Layer(text); NewId(layer); ownedLayers.Add(layer); document.layers.Add(layer);
            RenderTexture.active = sentinel; GL.sRGBWrite = true;
            Color[] initial = Pixels(document);
            t.True(ReferenceEquals(sentinel, RenderTexture.active), "Renderer restores caller render target");
            t.True(GL.sRGBWrite, "Renderer restores caller sRGB flag");
            t.True(initial.Count(c => c.a > .05f) > 300, "Text produces visible glyphs");
            Rect initialBounds = Bounds(initial, document.width);
            t.Near(document.width / 2.0, initialBounds.center.x, 5, "Text starts horizontally centered");
            t.Near(document.height / 2.0, initialBounds.center.y, 12, "Text starts vertically centered");
            EqualPixels(t, initial, Pixels(document), "Repeated render cache");
            var preview = (Texture2D)Call(document, "ComposeCanvas", 96);
            try
            {
                Rect previewBounds = Bounds(preview.GetPixels(), preview.width);
                t.Near(initialBounds.center.x / 4, previewBounds.center.x, 2, "Scaled preview retains text placement");
                t.Near(initialBounds.width / 4, previewBounds.width, 3, "Scaled preview retains text size");
                var renderer = text.GetType().GetField("renderer", Flags).GetValue(text);
                var mask = (Texture2D)renderer.GetType().GetField("mask", Flags).GetValue(renderer);
                t.True(mask.width <= 96 && mask.height <= 48, "Scaled preview rasterizes at its own resolution rather than upscaling glyphs");
                Call(text, "GetLayoutBounds", document.width, document.height);
                t.True(ReferenceEquals(mask, renderer.GetType().GetField("mask", Flags).GetValue(renderer)), "Overlay bounds reuse the preview layout without rerasterizing full-size glyphs");
            }
            finally { UnityEngine.Object.DestroyImmediate(preview); }
            text.fontSize = 64; Rect large = Bounds(Pixels(document), document.width);
            t.True(large.width > initialBounds.width * 1.8f && large.height > initialBounds.height * 1.8f, "Size increases actual glyph resolution");
            text.fontSize = 32; text.alignment = TextAnchor.MiddleLeft; Rect left = Bounds(Pixels(document), document.width);
            text.alignment = TextAnchor.MiddleRight; Rect right = Bounds(Pixels(document), document.width);
            t.True(right.center.x - left.center.x > 170, "Left and right alignment use the layer frame: " + left + " -> " + right);
            text.alignment = TextAnchor.MiddleCenter; text.text = "A\nB"; text.spacing = default;
            Rect tight = Bounds(Pixels(document), document.width);
            text.spacing.line = 1; Rect loose = Bounds(Pixels(document), document.width);
            t.Near(32, loose.height - tight.height, 1, "One em of Line spacing adds the current font size to baseline separation");
            if (family == "Arial" || family == "DejaVu Sans" || family == "Liberation Sans")
            {
                text.text = "Привет"; t.True(Pixels(document).Count(c => c.a > .05f) > 200, "Cyrillic text is visible");
            }
            text.text = "A A A A A A A A A A A A A A A A A"; text.spacing = default; text.wrapping = TextWrapping.Manual;
            Rect unwrapped = Bounds(Pixels(document), document.width);
            text.wrapping = TextWrapping.Words; Rect wrapped = Bounds(Pixels(document), document.width);
            t.True(wrapped.height > unwrapped.height, "Word Wrap produces additional lines");
            text.frameSize = new Vector2(95,150); text.fontSize = 24; text.alignment = TextAnchor.UpperLeft;
            text.text = "A B C D E F G H"; text.justify = false;
            Rect TopLine(Color[] p) => Bounds(p.Select((c,i) => i / document.width >= 135 ? c : Color.clear).ToArray(), document.width);
            Rect ragged = TopLine(Pixels(document));
            text.justify = true; Rect justified = TopLine(Pixels(document));
            t.True(justified.width > ragged.width + 5, "Justification expands spaces in wrapped lines, without resizing glyphs: " + ragged + " -> " + justified);
            t.True(Pixels(document).Where((c,i) => Math.Abs(i % document.width - document.width * .5f) > 49).All(c => c.a == 0), "Frame clips horizontal overflow");
            text.text = "A B\nC D"; text.wrapping = TextWrapping.Manual; text.justify = false;
            Color[] paragraphs = Pixels(document); text.justify = true;
            EqualPixels(t, paragraphs, Pixels(document), "Explicit paragraph ends are never justified");
            text.frameSize = new Vector2(384,192); text.fontSize = 32; text.text = "AAA"; text.spacing = default;
            Rect normalSpacing = Bounds(Pixels(document), document.width);
            text.spacing.character = .25f; Rect tracked = Bounds(Pixels(document), document.width);
            t.Near(16, tracked.width - normalSpacing.width, 1, "Character spacing adds em gaps only between characters");
            text.spacing.character = -.1f; Rect condensed = Bounds(Pixels(document), document.width);
            t.True(condensed.width < normalSpacing.width, "Negative Character spacing brings glyphs closer together");
            text.spacing = default; text.text = "AA AA"; Rect regularWords = Bounds(Pixels(document), document.width);
            text.spacing.word = .5f; Rect spacedWords = Bounds(Pixels(document), document.width);
            t.Near(16, spacedWords.width - regularWords.width, 1, "Word spacing adds em distance at a space");
            text.text = "AAA"; Color[] withoutSpaces = Pixels(document); text.spacing.word = 0;
            EqualPixels(t, withoutSpaces, Pixels(document), "Word spacing has no effect without spaces");
            text.text = "A\nB"; Rect regularParagraphs = Bounds(Pixels(document), document.width);
            text.spacing.paragraph = .5f; Rect spacedParagraphs = Bounds(Pixels(document), document.width);
            t.Near(16, spacedParagraphs.height - regularParagraphs.height, 1, "Paragraph spacing applies at explicit paragraph breaks");
            text.text = "A A A A A A A A"; text.frameSize = new Vector2(90,180); text.wrapping = TextWrapping.Words;
            text.spacing = default; Color[] automaticLines = Pixels(document); text.spacing.paragraph = 1;
            EqualPixels(t, automaticLines, Pixels(document), "Paragraph spacing does not affect automatic wrapping within a paragraph");
            text.spacing = default; text.text = "AAAAAA"; text.fontSize = 24; text.frameSize = new Vector2(100,180); text.wrapping = TextWrapping.Characters;
            Rect regularWrap = Bounds(Pixels(document), document.width); text.spacing.character = .5f;
            t.True(Bounds(Pixels(document), document.width).height > regularWrap.height, "Character spacing participates in line wrapping");
            var layoutLines = (System.Collections.IList)Call(Type("TextLayout"), "Build", "A\u0301B", TextWrapping.Characters, 20f,
                (Func<string,float>)(s => System.Globalization.StringInfo.ParseCombiningCharacters(s).Length * 10), 4f);
            t.Equal(2, layoutLines.Count, "Tracking wraps text elements without splitting combining characters");
            text.frameSize = new Vector2(384,192); text.fontSize = 48; text.text = "Aa Aa"; text.wrapping = TextWrapping.Manual;
            text.spacing = default; text.casing = TextCasing.SmallCaps; Rect plainCaps = Bounds(Pixels(document), document.width);
            text.spacing.character = .25f; text.spacing.word = .5f; Rect spacedCaps = Bounds(Pixels(document), document.width);
            t.Near(72, spacedCaps.width - plainCaps.width, 1.5, "Character and Word spacing use the full em size with Small Caps");
            using (var proof = new OwnedTexture(document.ComposeCanvas()))
                System.IO.File.WriteAllBytes("Temp/WhimTex/text-spacing-proof-" + Guid.NewGuid().ToString("N") + ".png", proof.Value.EncodeToPNG());
            text.spacing = default;
            text.frameSize = new Vector2(384,192); text.fontSize = 48; text.text = "MiXeD Привет";
            text.casing = TextCasing.Uppercase;
            Color[] upper = Pixels(document);
            t.Equal("MiXeD Привет", text.text, "Uppercase rendering preserves editable source text");
            text.casing = TextCasing.Normal; text.text = "MIXED ПРИВЕТ";
            EqualPixels(t, upper, Pixels(document), "Uppercase matches explicitly capitalized text");
            text.text = "MiXeD Привет"; text.casing = TextCasing.Lowercase;
            Color[] lower = Pixels(document); text.casing = TextCasing.Normal; text.text = "mixed привет";
            EqualPixels(t, lower, Pixels(document), "Lowercase matches explicitly lowercased text");
            text.text = "A"; Rect capital = Bounds(Pixels(document), document.width);
            text.text = "a"; text.casing = TextCasing.SmallCaps; Rect smallCapital = Bounds(Pixels(document), document.width);
            t.True(smallCapital.width < capital.width * .85f && smallCapital.height < capital.height * .85f && smallCapital.height > capital.height * .6f,
                "Small Caps draws lowercase as smaller uppercase glyphs: " + capital + " -> " + smallCapital);
            t.Near(capital.yMin, smallCapital.yMin, 2.5, "Small and full capitals share a baseline");
            text.text = "A"; Rect unchangedCapital = Bounds(Pixels(document), document.width);
            t.Equal(capital, unchangedCapital, "Small Caps leaves originally uppercase glyphs at full size");
            text.casing = TextCasing.Normal;
            text.autoSize = true; text.fontSize = 8; text.maxFontSize = 64; text.text = "Fit all of this text"; text.frameSize = new Vector2(160,40);
            float fitted = (float)Call(text, "GetFontSize", document.width, document.height);
            t.True(fitted >= 8 && fitted < 64, "Auto Size fits manual text within the configured minimum and maximum");
            t.Equal(8f, text.fontSize, "Auto Size does not overwrite the stored minimum size");
            t.Equal(64f, text.maxFontSize, "Auto Size does not overwrite the stored maximum size");
            t.True(Pixels(document).Any(c => c.a > .05f), "Auto-sized text produces visible glyphs");
            text.frameSize = new Vector2(384,192); text.text = "A";
            t.Equal(64f, (float)Call(text, "GetFontSize", document.width, document.height), "A larger frame allows the full maximum size");
            text.text = "A A"; text.frameSize = new Vector2(120,100);
            float plainFit = (float)Call(text, "GetFontSize", document.width, document.height);
            text.spacing.character = .3f; text.spacing.word = .5f;
            t.True((float)Call(text, "GetFontSize", document.width, document.height) < plainFit, "Auto Size accounts for Character and Word spacing");
            text.text = "A\nA"; text.spacing = default;
            float plainParagraphFit = (float)Call(text, "GetFontSize", document.width, document.height);
            text.spacing.line = .25f; text.spacing.paragraph = .5f;
            t.True((float)Call(text, "GetFontSize", document.width, document.height) < plainParagraphFit, "Auto Size accounts for Line and Paragraph spacing");
            text.spacing = default;
            text.text = "A B C D E F G H I J K L"; text.frameSize = new Vector2(120,70); text.wrapping = TextWrapping.Words;
            float wrappedSize = (float)Call(text, "GetFontSize", document.width, document.height);
            t.True(wrappedSize > 1 && wrappedSize < 64, "Auto Size accounts for wrapped line count and frame height");
            text.frameSize.y = 30;
            t.True((float)Call(text, "GetFontSize", document.width, document.height) < wrappedSize, "Reducing frame height recomputes the fitted size");
            text.frameSize = Vector2.one;
            t.Equal(8f, (float)Call(text, "GetFontSize", document.width, document.height), "Auto Size stops at Min Size even when the frame cannot hold the text");
            text.frameSize = new Vector2(384,192); text.text = "A"; text.fontSize = 24; text.maxFontSize = 24;
            t.Equal(24f, (float)Call(text, "GetFontSize", document.width, document.height), "Equal bounds produce a fixed auto-sized font");
            text.maxFontSize = 48;
            t.Equal(48f, (float)Call(text, "GetFontSize", document.width, document.height), "Changing Max Size invalidates the layout cache");
            text.fontSize = 12.25f; text.maxFontSize = 18.75f;
            t.Equal(18f, (float)Call(text, "GetFontSize", document.width, document.height), "Auto Size uses whole pixels inside fractional bounds");
            text.frameSize = Vector2.one;
            t.Equal(13f, (float)Call(text, "GetFontSize", document.width, document.height), "Fractional Min Size rounds inward to the next whole pixel");
            text.maxFontSize = 64; text.fontSize = 64;
            text.layoutMode = TextLayoutMode.Point; text.text = "Long text without any frame"; text.wrapping = TextWrapping.Words;
            t.Equal(64f, (float)Call(text, "GetFontSize", document.width, document.height), "Point text ignores Auto Size");
            text.autoSize = false;
            Color[] fixedSize = Pixels(document); text.maxFontSize = 128;
            EqualPixels(t, fixedSize, Pixels(document), "Fixed-size text ignores Max Size");
            Color[] point = Pixels(document); text.frameSize = new Vector2(1,1); text.wrapping = TextWrapping.Characters;
            EqualPixels(t, point, Pixels(document), "Point text ignores frame width and automatic wrapping");
            text.text = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; text.fontSize = 24;
            var pointTransform = TextureTransform.Default; pointTransform.position = new Vector2(-180, 40); layer.transform = pointTransform;
            t.True(Bounds(Pixels(document), document.width).xMax > 350, "Long point text is not cropped to a canvas-sized source before Transform");
            text.layoutMode = TextLayoutMode.Frame; text.frameSize = new Vector2(384,192); text.fontSize = 32;
            text.alignment = TextAnchor.MiddleCenter; text.justify = false; layer.transform = TextureTransform.Default;
            text.text = "WhimTex"; text.wrapping = TextWrapping.Manual; text.color = new Color(.6f, .2f, .8f, .7f);
            var tinted = Pixels(document); float maxAlpha = tinted.Max(c => c.a);
            t.Near(.7, maxAlpha, .01, "Text color alpha is applied once");
            var thumbnail = (Texture2D)Call(document, "GetLayerThumbnail", layer, 96, false);
            t.True(thumbnail != null && thumbnail.GetPixels().Any(c => c.a > .1f), "Document thumbnail renders text");
            Color sample = thumbnail.GetPixels().First(c => c.a > .1f);
            t.True(sample.b > sample.g && sample.r > sample.g, "Thumbnail includes text tint, not the raw coverage mask");
            text.color = Color.white;
            var transform = layer.transform; transform.position.x += 20; layer.transform = transform;
            Rect moved = Bounds(Pixels(document), document.width);
            t.Near(20, moved.center.x - initialBounds.center.x, 1.1, "Text participates in ordinary layer transform");
            layer.transform = TextureTransform.Default;
            text.autoSize = true;
            text.casing = TextCasing.SmallCaps;
            text.spacing = new TextSpacing { character = .1f, word = .2f, line = .3f, paragraph = .4f };
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var saved = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode });
                object behaviour = Node(Json(saved.Json), "layers", 0, "behaviour");
                t.Equal("TextLayerBehaviour", Node(behaviour, "$type").ToString(), "Text type serializes " + mode);
                t.True(!string.IsNullOrEmpty(Node(behaviour, "fallbackPng")?.ToString()), "Portable appearance stored " + mode);
                t.True(!saved.Json.Contains("$asset") && !saved.Json.Contains("C:\\"), "No project font reference or machine path " + mode);
                using var read = WhimTexDocumentJson.Read(saved.Json, false);
                var reopenedText = (TextLayerBehaviour)read.Document.layers[0].Behaviour;
                t.Equal(text.text, reopenedText.text, "Editable text roundtrip " + mode);
                t.Equal(text.fontFamily, reopenedText.fontFamily, "Font identity roundtrip " + mode);
                t.Equal(text.layoutMode, reopenedText.layoutMode, "Layout mode roundtrip " + mode);
                t.Equal(text.frameSize, reopenedText.frameSize, "Frame size roundtrip " + mode);
                t.Equal(text.wrapping, reopenedText.wrapping, "Wrapping rule roundtrip " + mode);
                t.Equal(text.justify, reopenedText.justify, "Justification roundtrip " + mode);
                t.Equal(text.autoSize, reopenedText.autoSize, "Auto Size roundtrip " + mode);
                t.Equal(text.fontSize, reopenedText.fontSize, "Min Size roundtrip " + mode);
                t.Equal(text.maxFontSize, reopenedText.maxFontSize, "Max Size roundtrip " + mode);
                t.Equal(text.casing, reopenedText.casing, "Casing roundtrip " + mode);
                t.Equal(text.spacing, reopenedText.spacing, "All em spacing fields roundtrip " + mode);
                t.Equal(text.characterHorizontalScale, reopenedText.characterHorizontalScale, "Character width roundtrip " + mode);
                EqualPixels(t, Pixels(document), Pixels(read.Document), "JSON render roundtrip " + mode);
            }
            object container = Activator.CreateInstance(Type("WhimTexDocumentContainer"), true);
            try
            {
                byte[] data = (byte[])Call(Type("WhimTexDocumentSerializer"), "Serialize", document, container);
                object decoded = Call(Type("WhimTexDocumentSerializer"), "Deserialize", data, container, typeof(WhimTexDocument), null, false);
                var reopened = (WhimTexDocument)decoded.GetType().GetProperty("Model", Flags).GetValue(decoded); restored.Add(reopened);
                t.Equal(text.maxFontSize, ((TextLayerBehaviour)reopened.layers[0].Behaviour).maxFontSize, "TIFF stores Max Size");
                EqualPixels(t, Pixels(document), Pixels(reopened), "TIFF model payload roundtrip");
            }
            finally { ((IDisposable)container).Dispose(); }
            var pointBackup = new TextLayerBehaviour { text = "Saved bounds", fontFamily = family, fontSize = 24 };
            Call(pointBackup, "PrepareSavedAppearance", document.width, document.height);
            Rect savedBounds = (Rect)pointBackup.GetType().GetField("fallbackRect", Flags).GetValue(pointBackup);
            ownedLayers.Add(new Layer(pointBackup));
            Color[] beforeMissing = Pixels(document);
            availableField.SetValue(null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            EqualPixels(t, beforeMissing, Pixels(document), "Missing font uses saved appearance");
            Pixels(document); t.Equal(1, warnings, "Missing font warning is not repeated per render");
            t.Equal(savedBounds, (Rect)Call(pointBackup, "GetLayoutBounds", document.width, document.height),
                "Missing-font Point bounds use the retained appearance instead of the entire canvas");
            t.True((string)text.GetType().GetProperty("Notice", Flags).GetValue(text) != null, "Missing font notice is available to UI/API");
            var missingSaved = WhimTexDocumentJson.Write(document);
            t.True(missingSaved.Warnings.Count > 0, "Missing font appears in JSON write diagnostics");
            using (var reopenedMissing = WhimTexDocumentJson.Read(missingSaved.Json, false))
            {
                t.True(reopenedMissing.Warnings.Count > 0, "Missing font reported when opening JSON, before saving");
                EqualPixels(t, beforeMissing, Pixels(reopenedMissing.Document), "Saving with a missing font retains fallback and editable source");
            }
            text.text = "Changed";
            t.True(Pixels(document).All(c => c.a == 0), "Changed layout does not reuse a stale appearance");
            availableField.SetValue(null, available); text.text = "WhimTex";
            EqualPixels(t, beforeMissing, Pixels(document), "Font return restores native rendering");
            // Current API accepts text-only settings without exposing the encoded fallback.
            var apiDocument = Document(); restored.Add(apiDocument);
            var apiLayer = (Layer)Call(typeof(WhimTexApi), "ApplyOperation", apiDocument,
                Json("{\"op\":\"add\",\"type\":\"text\",\"settings\":{\"text\":{\"text\":\"Created by API\",\"fontSize\":28}}}"),
                new Dictionary<string, Layer>(), false);
            t.Equal("Created by API", ((TextLayerBehaviour)apiLayer.Behaviour).text, "Agent add routes nested Text settings through the layer registry");
            t.True(Pixels(apiDocument).Any(c => c.a > .1f), "Agent-created text renders without project font assets");
            var apiText = (TextLayerBehaviour)apiLayer.Behaviour;
            Call(typeof(WhimTexApi), "SetText", apiText, (object)Json("{\"layoutMode\":\"Frame\",\"autoSize\":true,\"fontSize\":12,\"maxFontSize\":48}"));
            t.Equal(12f, apiText.fontSize, "Agent can set Auto Size minimum");
            t.Equal(48f, apiText.maxFontSize, "Agent can set Auto Size maximum");
            t.Equal("48", Node(Call(typeof(WhimTexApi), "TextSnapshot", apiText), "maxFontSize").ToString(), "Agent snapshot exposes Max Size");
            Call(typeof(WhimTexApi), "SetText", apiText, (object)Json("{\"alignment\":\"LowerRight\",\"justify\":true,\"wrapping\":\"Words\"}"));
            t.True(apiText.justify && apiText.alignment == TextAnchor.LowerLeft, "Agent justification keeps paragraph endings left-aligned and preserves vertical alignment");
            Call(typeof(WhimTexApi), "SetText", apiText, (object)Json("{\"wrapping\":\"Manual\"}"));
            t.True(!apiText.justify && apiText.alignment == TextAnchor.LowerLeft, "Agent Manual wrapping uses the same Justify fallback as Properties");
            Call(typeof(WhimTexApi), "SetText", apiText, (object)Json("{\"layoutMode\":\"Point\",\"alignment\":\"MiddleRight\",\"justify\":true}"));
            t.True(!apiText.justify && apiText.alignment == TextAnchor.MiddleLeft, "Agent Point text falls back from Justify without changing vertical alignment");
            foreach (string limits in new[] { "\"fontSize\":40,\"maxFontSize\":20", "\"fontSize\":12.25,\"maxFontSize\":12.75" })
            {
                var invalidRange = new TextLayerBehaviour { layoutMode = TextLayoutMode.Frame, autoSize = true };
                try { Call(typeof(WhimTexApi), "SetText", invalidRange, (object)Json("{" + limits + "}")); t.True(false, "Invalid Auto Size range must be rejected"); }
                catch (TargetInvocationException error) { t.True(error.InnerException.Message.Contains("Auto Size"), "Invalid Auto Size range gets a clear diagnostic"); }
            }
            Call(typeof(WhimTexApi), "SetText", text, (object)Json("{\"text\":\"API\",\"fontSize\":28,\"autoSize\":false,\"casing\":\"Uppercase\",\"spacing\":{\"character\":0.2,\"line\":0.5}}"));
            t.Equal("API", text.text, "Agent text settings update"); t.Equal(28f, text.fontSize, "Agent text size update");
            object snapshot = Call(typeof(WhimTexApi), "TextSnapshot", text);
            t.Equal("API", Node(snapshot, "text").ToString(), "Agent snapshot reports editable text");
            t.True(!text.autoSize && Node(snapshot, "autoSize").ToString().Equals("False", StringComparison.OrdinalIgnoreCase), "Agent settings and snapshot expose Auto Size");
            t.Equal("Uppercase", Node(snapshot, "casing").ToString(), "Agent settings and snapshot expose casing");
            t.Equal(.2f, text.spacing.character, "Agent can patch one horizontal spacing field");
            t.Equal(.2f, text.spacing.word, "Partial spacing patch preserves Word spacing");
            t.Equal(.4f, text.spacing.paragraph, "Partial spacing patch preserves Paragraph spacing");
            t.Near(.5, double.Parse(Node(snapshot, "spacing", "line").ToString(), System.Globalization.CultureInfo.InvariantCulture), .00001, "Agent snapshot reports spacing in em");
            try { Call(typeof(WhimTexApi), "SetText", text, (object)Json("{\"lineSpacing\":2}")); t.True(false, "Retired line-spacing multiplier must not be interpreted as em"); }
            catch (TargetInvocationException error) { t.True(error.InnerException.Message.Contains("lineSpacing"), "Retired multiplier gets a clear unsupported-field diagnostic"); }
            t.True(Node(snapshot, "fallbackPng") == null, "Agent settings do not carry the raster fallback");
            CheckOverflow(t, family, restored);
            var copy = JsonUtility.FromJson<Layer>(JsonUtility.ToJson(layer)); NewId(copy);
            var copyDocument = Document(); restored.Add(copyDocument); copyDocument.layers.Add(copy);
            EqualPixels(t, Pixels(document), Pixels(copyDocument), "Layer duplication preserves text data independently");
            ((TextLayerBehaviour)copy.Behaviour).text = "Copy";
            t.Equal("API", text.text, "Editing duplicate does not change original text");
            // A text source is a normal source for groups, clipping and targeted effects.
            document.layers.Clear(); var group = new Layer(new GroupLayerBehaviour()); NewId(group);
            group.children.Add(layer); document.layers.Add(group);
            t.True(Pixels(document).Any(c => c.a > .1f), "Grouped text renders");
            document.layers.Clear(); document.layers.Add(layer); layer.clippingMask = true;
            var clip = new Layer(new ColorFillLayerBehaviour { color = new Color(1, 1, 1, .5f) }); NewId(clip); document.layers.Add(clip);
            t.True(Pixels(document).Max(c => c.a) <= .76f, "Text participates in clipping");
            layer.clippingMask = false; document.layers.Remove(clip);
            var outline = new Layer(new OutlineLayerBehaviour { outlineWidth = 2 }); NewId(outline); document.layers.Insert(0, outline);
            ownedLayers.Add(outline); ownedLayers.Add(clip); ownedLayers.Add(group);
            t.True(Pixels(document).Any(c => c.a > .1f), "Effect layer can sample text as Previous input");
            document.layers.Remove(outline);
            text.spacing.character = float.NaN;
            try { Pixels(document); t.True(false, "Non-finite spacing must be rejected"); }
            catch (ArgumentException) { t.True(true, "Non-finite spacing rejected"); }
            text.spacing.character = .2f;
            text.fontSize = float.NaN;
            try { Pixels(document); t.True(false, "Invalid size must be rejected"); }
            catch (ArgumentException) { t.True(true, "Invalid size rejected"); }
            text.fontSize = 42; text.text = "WhimTex\nПривет"; text.color = new Color(.2f, .7f, 1);
            using (var proof = new OwnedTexture(document.ComposeCanvas()))
                System.IO.File.WriteAllBytes("Temp/WhimTex/text-layer-proof-" + Guid.NewGuid().ToString("N") + ".png", proof.Value.EncodeToPNG());
            text.text = "";
            t.True(Pixels(document).All(c => c.a == 0), "Empty text produces a transparent layer");
        }
        finally
        {
            availableField.SetValue(null, available); Application.logMessageReceived -= observe;
            RenderTexture.active = original; GL.sRGBWrite = originalSrgb;
            document.layers.ForEach(layer => { if (layer?.Behaviour is TextLayerBehaviour text) text.fontSize = 32; });
            foreach (var item in restored) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(document);
            foreach (var layer in ownedLayers) Call(layer, "ReleaseTransientResources");
            RenderTexture.ReleaseTemporary(sentinel);
        }
    });

    static void CheckOverflow(TestContext t, string family, List<WhimTexDocument> owned)
    {
        Func<string, float> measure = s => System.Globalization.StringInfo.ParseCombiningCharacters(s).Length * 10;
        object lines = Call(Type("TextLayout"), "Build", "A\u0301BC", TextWrapping.Manual, 20f, measure, 0f);
        var shortened = (System.Collections.IList)Call(Type("TextLayout"), "Ellipsize", lines, new float[] { 0 }, 10f, new Vector2(20, 10), measure);
        string Content(object line) => (string)line.GetType().GetField("text", Flags).GetValue(line);
        t.Equal("A\u0301\u2026", Content(shortened[0]), "Ellipsis preserves combining-character boundaries and reserves suffix width");
        lines = Call(Type("TextLayout"), "Build", "AB\nC\nD", TextWrapping.Manual, 30f, measure, 0f);
        shortened = (System.Collections.IList)Call(Type("TextLayout"), "Ellipsize", lines, new float[] { 0, 10, 20 }, 10f, new Vector2(30, 20), measure);
        t.True(shortened.Count == 2 && Content(shortened[0]) == "AB" && Content(shortened[1]) == "C\u2026", "Height overflow marks the last visible line with ellipsis");
        t.True((bool)shortened[1].GetType().GetField("paragraphEnd", Flags).GetValue(shortened[1]), "Truncated last line is not justified");
        shortened = (System.Collections.IList)Call(Type("TextLayout"), "Ellipsize", lines, new float[] { 0, 10, 20 }, 10f, new Vector2(5, 20), measure);
        t.True(shortened.Cast<object>().All(line => Content(line) == ""), "A frame narrower than ellipsis shows no partial suffix");
        shortened = (System.Collections.IList)Call(Type("TextLayout"), "Ellipsize", lines, new float[] { 0, 10, 20 }, 10f, new Vector2(30, 9), measure);
        t.True(shortened.Count == 1 && Content(shortened[0]) == "", "A frame shorter than one line does not show clipped text in Ellipsis mode");

        var document = Document(); owned.Add(document);
        var text = new TextLayerBehaviour { text = "ABCDEFGHIJKLMN\nABCDEFGHIJKLMN\nThird line", fontFamily = family, fontSize = 24,
            layoutMode = TextLayoutMode.Frame, wrapping = TextWrapping.Manual, frameSize = new Vector2(100, 65), alignment = TextAnchor.UpperLeft };
        var layer = new Layer(text); NewId(layer); document.layers.Add(layer);
        Color[] clipped = null;
        foreach (var mode in new[] { TextOverflowMode.Clip, TextOverflowMode.None, TextOverflowMode.Ellipsis })
        {
            Call(typeof(WhimTexApi), "SetText", text, Json("{\"overflow\":\"" + mode + "\"}"));
            t.Equal(mode.ToString(), Node(Call(typeof(WhimTexApi), "TextSnapshot", text), "overflow").ToString(), "Agent overflow setting and snapshot agree " + mode);
            var pixels = Pixels(document); Rect ink = Bounds(pixels, document.width);
            if (mode == TextOverflowMode.None) t.True(ink.width > 110 && ink.height > 65, "None retains text beyond both frame axes");
            else t.True(ink.width <= 100 && ink.height <= 65 && ink.width > 1, mode + " keeps visible text inside the frame");
            if (mode == TextOverflowMode.Clip) clipped = pixels;
            if (mode == TextOverflowMode.Ellipsis) t.True(pixels.Where((c, i) => Mathf.Abs(c.a - clipped[i].a) > .01f).Any(), "Ellipsis draws shortened content, not the clipped result");
            t.Equal("ABCDEFGHIJKLMN\nABCDEFGHIJKLMN\nThird line", text.text, "Overflow never overwrites the editable source " + mode);
            var saved = WhimTexDocumentJson.Write(document);
            using var reopened = WhimTexDocumentJson.Read(saved.Json, false);
            t.Equal(mode, ((TextLayerBehaviour)reopened.Document.layers[0].Behaviour).overflow, "Overflow survives JSON roundtrip " + mode);
            EqualPixels(t, pixels, Pixels(reopened.Document), "Overflow render roundtrip " + mode);
            object container = Activator.CreateInstance(Type("WhimTexDocumentContainer"), true);
            try
            {
                byte[] data = (byte[])Call(Type("WhimTexDocumentSerializer"), "Serialize", document, container);
                object decoded = Call(Type("WhimTexDocumentSerializer"), "Deserialize", data, container, typeof(WhimTexDocument), null, false);
                var restored = (WhimTexDocument)decoded.GetType().GetProperty("Model", Flags).GetValue(decoded); owned.Add(restored);
                t.Equal(mode, ((TextLayerBehaviour)restored.layers[0].Behaviour).overflow, "Overflow survives TIFF model payload " + mode);
                EqualPixels(t, pixels, Pixels(restored), "TIFF overflow render roundtrip " + mode);
            }
            finally { ((IDisposable)container).Dispose(); }
            using var proof = new OwnedTexture(document.ComposeCanvas());
            System.IO.File.WriteAllBytes("Temp/WhimTex/text-overflow-" + mode + "-" + Guid.NewGuid().ToString("N") + ".png", proof.Value.EncodeToPNG());
        }
        text.autoSize = true; text.fontSize = text.maxFontSize = 24;
        t.Equal(24f, (float)Call(text, "GetFontSize", document.width, document.height), "Ellipsis applies after Auto Size reaches its minimum, without changing the limits");
        t.True(Bounds(Pixels(document), document.width).height <= 65, "Auto Size with Ellipsis retains the frame constraint");
        text.layoutMode = TextLayoutMode.Point; text.autoSize = false;
        Color[] point = Pixels(document); text.overflow = TextOverflowMode.Clip;
        EqualPixels(t, point, Pixels(document), "Point text ignores frame overflow settings");
    }
    static void CheckHorizontalScale(TestContext t, string family, List<WhimTexDocument> owned)
    {
        var document = Document(); owned.Add(document);
        var text = new TextLayerBehaviour { text = "H H", fontFamily = family, fontSize = 32, alignment = TextAnchor.UpperLeft };
        var layer = new Layer(text); NewId(layer); document.layers.Add(layer);
        t.Equal(1f, text.characterHorizontalScale, "Character horizontal scale defaults to a neutral multiplier");
        Rect normal = Bounds(Pixels(document), document.width);
        float normalWidth = ((Rect)Call(text, "GetLayoutBounds", document.width, document.height)).width;
        foreach (float scale in new[] { .5f, 2f })
        {
            text.characterHorizontalScale = scale;
            Rect ink = Bounds(Pixels(document), document.width);
            t.Near(normal.width * scale, ink.width, 2, "Horizontal scale changes actual glyph widths " + scale);
            t.Near(normal.height, ink.height, 1, "Horizontal scale preserves glyph height " + scale);
            text.spacing = new TextSpacing { character = .2f, word = .25f };
            t.Near(normalWidth * scale + 32 * (.4f + .25f),
                ((Rect)Call(text, "GetLayoutBounds", document.width, document.height)).width, .01,
                "Added em spacing stays independent of character scaling " + scale);
            text.spacing = default;
        }
        text.characterHorizontalScale = 1; text.text = "AAAAAA";
        float lineWidth = ((Rect)Call(text, "GetLayoutBounds", document.width, document.height)).width;
        text.layoutMode = TextLayoutMode.Frame; text.frameSize = new Vector2(lineWidth * .75f, 160);
        text.wrapping = TextWrapping.Characters; text.overflow = TextOverflowMode.Clip;
        float wrappedHeight = Bounds(Pixels(document), document.width).height;
        text.characterHorizontalScale = .5f;
        t.True(Bounds(Pixels(document), document.width).height < wrappedHeight, "Character horizontal scale participates in wrapping");
        text.wrapping = TextWrapping.Manual; text.autoSize = true; text.fontSize = 8; text.maxFontSize = 64;
        text.frameSize = new Vector2(lineWidth * .9f, 128); text.characterHorizontalScale = 1;
        float fitted = (float)Call(text, "GetFontSize", document.width, document.height);
        text.characterHorizontalScale = .5f;
        t.True((float)Call(text, "GetFontSize", document.width, document.height) > fitted, "Narrower glyphs allow a larger Auto Size");
        text.characterHorizontalScale = 2;
        t.True((float)Call(text, "GetFontSize", document.width, document.height) < fitted, "Wider glyphs reduce Auto Size");
        text.autoSize = false; text.fontSize = 24; text.overflow = TextOverflowMode.Ellipsis;
        text.frameSize = new Vector2(100, 65); text.text = "ABCDEFGHIJKLMN\nABCDEFGHIJKLMN\nThird line";
        Rect ellipsis = Bounds(Pixels(document), document.width);
        t.True(ellipsis.width <= 100 && ellipsis.height <= 65 && ellipsis.width > 1, "Ellipsis accounts for scaled glyphs and suffix width");
        t.Equal("ABCDEFGHIJKLMN\nABCDEFGHIJKLMN\nThird line", text.text, "Character scaling and Ellipsis preserve source text");
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            var saved = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode });
            using var reopened = WhimTexDocumentJson.Read(saved.Json, false);
            t.Equal(2f, ((TextLayerBehaviour)reopened.Document.layers[0].Behaviour).characterHorizontalScale, "Non-neutral character scale persists in JSON " + mode);
            EqualPixels(t, Pixels(document), Pixels(reopened.Document), "Scaled glyph JSON roundtrip " + mode);
        }
        object container = Activator.CreateInstance(Type("WhimTexDocumentContainer"), true);
        try
        {
            byte[] data = (byte[])Call(Type("WhimTexDocumentSerializer"), "Serialize", document, container);
            object decoded = Call(Type("WhimTexDocumentSerializer"), "Deserialize", data, container, typeof(WhimTexDocument), null, false);
            var reopened = (WhimTexDocument)decoded.GetType().GetProperty("Model", Flags).GetValue(decoded); owned.Add(reopened);
            t.Equal(2f, ((TextLayerBehaviour)reopened.layers[0].Behaviour).characterHorizontalScale, "Character horizontal scale persists in TIFF payload");
            EqualPixels(t, Pixels(document), Pixels(reopened), "Scaled glyph TIFF roundtrip");
        }
        finally { ((IDisposable)container).Dispose(); }
        Call(typeof(WhimTexApi), "SetText", text, Json("{\"characterHorizontalScale\":0.75}"));
        t.Equal(.75f, text.characterHorizontalScale, "API accepts character horizontal scale as a multiplier");
        t.Near(.75, double.Parse(Node(Call(typeof(WhimTexApi), "TextSnapshot", text), "characterHorizontalScale").ToString(),
            System.Globalization.CultureInfo.InvariantCulture), 0, "API snapshot reports character horizontal scale");
        foreach (float invalid in new[] { 0f, -.5f, 11f, float.NaN })
        {
            text.characterHorizontalScale = invalid;
            try { Call(text, "Validate"); t.True(false, "Invalid character scale must be rejected"); }
            catch (TargetInvocationException error) { t.True(error.InnerException is ArgumentException, "Character scale rejects non-finite and out-of-range values"); }
        }
        Call(text, "SetCharacterHorizontalScale", float.NaN); t.Equal(1f, text.characterHorizontalScale, "UI normalizes non-finite character scale to neutral");
        Call(text, "SetCharacterHorizontalScale", 0f); t.Equal(.01f, text.characterHorizontalScale, "UI keeps character width positive");
        Call(text, "SetCharacterHorizontalScale", 20f); t.Equal(10f, text.characterHorizontalScale, "UI caps excessive character width");
        text.characterHorizontalScale = 1; text.layoutMode = TextLayoutMode.Point; text.fontSize = 32; text.text = "WhimTex";
        foreach (float scale in new[] { .5f, 1f, 2f })
        {
            text.characterHorizontalScale = scale;
            using var proof = new OwnedTexture(document.ComposeCanvas());
            System.IO.File.WriteAllBytes("Temp/WhimTex/text-horizontal-scale-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".png", proof.Value.EncodeToPNG());
        }
    }
    sealed class OwnedTexture : IDisposable
    {
        public readonly Texture2D Value;
        public OwnedTexture(Texture2D value) { Value = value; }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(Value); }
    }
}
