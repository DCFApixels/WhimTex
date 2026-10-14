using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;

// Owned transient documents and textures; no imports, assets, windows or global Undo.
public static class PixelScaleTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object target, string name, params object[] args) =>
        (target is Type type ? type : target.GetType()).GetMethod(name, Flags).Invoke(target is Type ? null : target, args);
    static FieldInfo LayerField(string name) => typeof(Layer).GetField(name, Flags);
    static WhimTexDocument Document(UnityAScope scope, int width = 128, int height = 96, float pixelScale = 1)
    {
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = width; document.height = height; document.pixelScale = pixelScale;
        return document;
    }
    static void Normalize(WhimTexDocument document) => Call(document, "NormalizeModel");
    static Color[] Pixels(WhimTexDocument document)
    {
        Texture2D texture = document.ComposeCanvas();
        try { return texture.GetPixels(); }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    static void Same(TestContext test, Color[] expected, Color[] actual, string message, double tolerance = .001)
    {
        test.Equal(expected.Length, actual.Length, message + " dimensions");
        double maximum = 0, maximumAlpha = 0, maximumVisible = 0;
        for (int i = 0; i < expected.Length; i++) for (int channel = 0; channel < 4; channel++)
        {
            maximum = Math.Max(maximum, Math.Abs(expected[i][channel] - actual[i][channel]));
            if (channel == 3) maximumAlpha = Math.Max(maximumAlpha, Math.Abs(expected[i].a - actual[i].a));
            else maximumVisible = Math.Max(maximumVisible, Math.Abs(expected[i][channel] * expected[i].a - actual[i][channel] * actual[i].a));
        }
        test.Near(0, maximum, tolerance, message + "; maximum alpha=" + maximumAlpha + ", visible RGB=" + maximumVisible);
    }
    static void HasPixels(TestContext test, Color[] pixels, string message)
    {
        int visible = 0;
        foreach (Color pixel in pixels) if (pixel.a > .01) visible++;
        test.True(visible > 0 && visible < pixels.Length, message);
    }
    static void NearRect(TestContext test, Rect expected, Rect actual, string message)
    {
        test.Near(expected.x, actual.x, .0001, message + " X"); test.Near(expected.y, actual.y, .0001, message + " Y");
        test.Near(expected.width, actual.width, .0001, message + " width"); test.Near(expected.height, actual.height, .0001, message + " height");
    }
    static void Model(TestContext test, UnityAScope scope)
    {
        var document = Document(scope, 64, 48);
        test.Equal(1f, document.pixelScale, "New document pixel scale defaults to one");
        Layer layer = new NoiseLayerBehaviour(); document.layers.Add(layer); Normalize(document);
        var cacheField = LayerField("transformCache"); object beforeCache = cacheField.GetValue(layer);
        ulong beforeVersion = (ulong)beforeCache.GetType().GetField("version", Flags).GetValue(beforeCache);
        test.True(document.ApplyPixelScale(2.5f), "Pixel-scale-only change applies without changing dimensions");
        test.Equal(64, document.width, "Pixel-scale-only width"); test.Equal(48, document.height, "Pixel-scale-only height");
        test.Equal(2.5f, (float)Call(layer, "GetPixelScale"), "Layer obtains the owning document scale");
        object afterCache = cacheField.GetValue(layer);
        test.True((ulong)afterCache.GetType().GetField("version", Flags).GetValue(afterCache) > beforeVersion,
            "Pixel scale changes the render cache stamp even when the matrix and dimensions are unchanged");
        beforeVersion = (ulong)afterCache.GetType().GetField("version", Flags).GetValue(afterCache);
        TextureTransform matrix = layer.transform;
        document.pixelScale = 3; Call(document, "RefreshTransformHierarchy");
        test.Equal(matrix, layer.transform, "Refreshing a public pixelScale field leaves the layer matrix unchanged");
        test.True((ulong)afterCache.GetType().GetField("version", Flags).GetValue(afterCache) > beforeVersion,
            "A pixel-scale-only field change invalidates caches independently of transform or source-reference mutations");
        document.ApplyPixelScale(2.5f);
        test.Equal(1f, (float)LayerField("sourcePixelScale").GetValue(layer), "First style change freezes the original source pixel scale");
        string snapshot = JsonUtility.ToJson(document);
        test.True(!document.ApplyPixelScale(2.5f), "Identical pixel scale is a no-op");
        test.Equal(snapshot, JsonUtility.ToJson(document), "No-op retains every serialized field");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 16385f })
        {
            bool rejected = false;
            try { document.ApplyCanvasScale(96, 72, invalid); } catch (WhimTexDocumentException) { rejected = true; }
            test.True(rejected, "Scale rejects invalid pixel scale " + invalid);
            test.Equal(snapshot, JsonUtility.ToJson(document), "Invalid scale cannot partially change dimensions, transforms or source references");
            rejected = false;
            try { document.ApplyCanvasFrame(new RectInt(5, 3, 45, 32), invalid); } catch (WhimTexDocumentException) { rejected = true; }
            test.True(rejected, "Crop rejects invalid pixel scale " + invalid);
            test.Equal(snapshot, JsonUtility.ToJson(document), "Invalid Crop is atomic");
        }
        var empty = Document(scope, 1, 1);
        test.True(empty.ApplyPixelScale(float.Epsilon), "Every finite positive scale is accepted without an arbitrary lower cutoff");
        test.True(empty.ApplyPixelScale(16384), "The resource-bound maximum is inclusive");
        test.Equal(1f, (float)Call(new Layer(new ShapeLayerBehaviour()), "GetPixelScale"), "Standalone layers use one");

        Type contextType = typeof(Layer).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext");
        object context = Activator.CreateInstance(contextType, Flags, null,
            new object[] { document, null, 32, 24, 2f, true, true, null }, null);
        test.Equal(2.5f, (float)contextType.GetField("pixelScale", Flags).GetValue(context), "Render context captures effective document pixel scale");
        test.Equal(10f, (float)Call(context, "CanvasPixels", 4f), "Logical to canvas pixels is applied once");
        test.Equal(5f, (float)Call(context, "RenderPixels", 4f), "Preview downsampling follows logical-to-canvas conversion");

        var legacyReference = Document(scope, 64, 48, 3);
        Layer oldLayer = new GradientLayerBehaviour(); legacyReference.layers.Add(oldLayer);
        LayerField("sourceCanvasSize").SetValue(oldLayer, new Vector2Int(80, 60)); Normalize(legacyReference);
        legacyReference.ApplyCanvasFrame(new RectInt(1, 2, 50, 40));
        test.Equal(3f, (float)LayerField("sourcePixelScale").GetValue(oldLayer), "Legacy Crop reference freezes the current scale when its source scale is omitted");
        test.Equal(new Vector2Int(80, 60), (Vector2Int)LayerField("sourceCanvasSize").GetValue(oldLayer), "Existing source size is retained");
    }
    static TextLayerBehaviour Text(TextLayoutMode mode, TextEdgeMode edge, float factor = 1) => new TextLayerBehaviour
    {
        text = "Crop\n123", fontSize = 13 * factor, layoutOffset = new Vector2(3, -2) * factor,
        frameSize = new Vector2(48, 30) * factor, layoutMode = mode, edgeMode = edge,
        overflow = TextOverflowMode.Clip, wrapping = TextWrapping.Manual,
        spacing = new TextSpacing { character = .1f, word = .1f, line = .15f }, color = Color.white
    };
    static void TextPixels(TestContext test, UnityAScope scope)
    {
        foreach (TextLayoutMode mode in Enum.GetValues(typeof(TextLayoutMode)))
        foreach (TextEdgeMode edge in Enum.GetValues(typeof(TextEdgeMode)))
        {
            var actual = Document(scope, pixelScale: 2); var expected = Document(scope);
            TextLayerBehaviour settings = Text(mode, edge), doubled = Text(mode, edge, 2);
            Layer a = settings, b = doubled; a.filterMode = b.filterMode = LayerFilterMode.Point;
            actual.layers.Add(a); expected.layers.Add(b); Normalize(actual); Normalize(expected);
            string source = JsonUtility.ToJson(settings);
            Color[] reference = Pixels(expected), scaled = Pixels(actual);
            HasPixels(test, scaled, "Text is visible and bounded " + mode + " " + edge);
            Same(test, reference, scaled, "P2 Text matches doubled font/frame/offset at P1 " + mode + " " + edge, .012);
            NearRect(test, (Rect)Call(doubled, "GetLayoutBounds", 128, 96), (Rect)Call(settings, "GetLayoutBounds", 128, 96),
                "Physical text handles match the effective typography " + mode + " " + edge);
            test.Equal(13f, (float)Call(settings, "GetFontSize", 128, 96), "Font Size remains in authored logical pixels");
            actual.ApplyPixelScale(1); Color[] unscaled = Pixels(actual);
            actual.ApplyPixelScale(2); Same(test, scaled, Pixels(actual), "P-only Text cache roundtrip " + mode + " " + edge, .012);
            test.Equal(source, JsonUtility.ToJson(settings), "Changing P retains editable Text settings " + mode + " " + edge);
            actual.ApplyCanvasScale(256, 288, 2);
            var resizedReference = Document(scope, 256, 288, 2); Layer resizedText = Text(mode, edge);
            resizedText.filterMode = LayerFilterMode.Point; resizedReference.layers.Add(resizedText); Normalize(resizedReference);
            Same(test, Pixels(resizedReference), Pixels(actual), "Nonuniform Scale preserves scalar typography without a second stretch " + mode + " " + edge, .012);
            actual.ApplyCanvasScale(128, 96, 1); Same(test, unscaled, Pixels(actual), "Inverse nonuniform dimensions and P restore Text " + mode + " " + edge, .012);
        }
        // Model a saved document whose font became unavailable, without changing the global font catalog.
        var saved = Document(scope, pixelScale: 2); var fallback = Text(TextLayoutMode.Point, TextEdgeMode.Pixel);
        saved.layers.Add(fallback); Normalize(saved); Call(fallback, "PrepareSavedAppearance", 128, 96);
        Color[] pixels = Pixels(saved);
        fallback.fontFamily = "WhimTexMissingPixelScaleFont_" + Guid.NewGuid().ToString("N");
        typeof(TextLayerBehaviour).GetField("fallbackFont", Flags).SetValue(fallback, fallback.fontFamily);
        typeof(TextLayerBehaviour).GetField("fallbackKey", Flags).SetValue(fallback, Call(fallback, "LayoutKey", 128, 96));
        test.True((bool)typeof(TextLayerBehaviour).GetProperty("HasSavedAppearance", Flags).GetValue(fallback), "Owned unavailable-font fixture retains its matching saved mask");
        Same(test, pixels, Pixels(saved), "Unavailable font retains the P2 saved raster", .012);
        Rect bounds = (Rect)Call(fallback, "GetLayoutBounds", 128, 96);
        saved.ApplyPixelScale(1);
        test.True((bool)typeof(TextLayerBehaviour).GetProperty("HasSavedAppearance", Flags).GetValue(fallback), "P change does not discard a missing-font fallback");
        NearRect(test, new Rect(bounds.position * .5f, bounds.size * .5f), (Rect)Call(fallback, "GetLayoutBounds", 128, 96), "Missing-font handles use the retained scale reference");
        HasPixels(test, Pixels(saved), "Missing-font fallback remains visible after changing P");
        saved.ApplyPixelScale(2); Same(test, pixels, Pixels(saved), "Missing-font scale roundtrip", .012);
    }
    static ShapeLayerBehaviour Shape(int style, float factor)
    {
        var shape = new ShapeLayerBehaviour { kind = style == 2 ? ShapeLayerBehaviour.ShapeKind.Arc : ShapeLayerBehaviour.ShapeKind.Rectangle,
            fill = style != 0, stroke = style == 0, strokeWidth = 2 * factor, arcThickness = 3 * factor,
            feather = style == 1 ? 2 * factor : 0, fillColor = new Color(.4f, .7f, .2f, 1), strokeColor = Color.white,
            edgeMode = style == 1 ? ShapeLayerBehaviour.EdgeMode.Antialiased : ShapeLayerBehaviour.EdgeMode.Step };
        return shape;
    }
    static void Sources(TestContext test, UnityAScope scope)
    {
        for (int style = 0; style < 3; style++)
        {
            var actual = Document(scope, 80, 60, 2); var expected = Document(scope, 80, 60);
            ShapeLayerBehaviour settings = Shape(style, 1); Layer a = settings, b = Shape(style, 2);
            a.transform.scale = b.transform.scale = new Double2(.6, .6);
            actual.layers.Add(a); expected.layers.Add(b); Normalize(actual); Normalize(expected);
            string source = JsonUtility.ToJson(settings);
            HasPixels(test, Pixels(actual), "Shape style has bounded visible coverage " + style);
            Same(test, Pixels(expected), Pixels(actual), "Shape physical stroke/feather/arc equivalent at P2 " + style);
            test.Equal(source, JsonUtility.ToJson(settings), "Shape logical settings retained " + style);
        }
        foreach (bool seamless in new[] { false, true })
        {
            var actual = Document(scope, 64, 48, 2); var expected = Document(scope, 64, 48);
            ColorFillLayerBehaviour settings = Pattern(1, seamless); actual.layers.Add(settings); expected.layers.Add(Pattern(2, seamless));
            Normalize(actual); Normalize(expected);
            Same(test, Pixels(expected), Pixels(actual), "Pattern cell/offset lengths match doubled logical values at P1, seamless=" + seamless);
            actual.ApplyCanvasScale(128, 144, 2);
            var resized = Document(scope, 128, 144, 2); resized.layers.Add(Pattern(1, seamless)); Normalize(resized);
            Same(test, Pixels(resized), Pixels(actual), "Pattern nonuniform Scale applies the physical cell size once, seamless=" + seamless);
        }
        var grain = Document(scope, 64, 48, 2); var grainReference = Document(scope, 64, 48);
        grain.layers.Add(new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 2, seed = 431 });
        grainReference.layers.Add(new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 4, seed = 431 });
        Normalize(grain); Normalize(grainReference);
        Same(test, Pixels(grainReference), Pixels(grain), "Grain physical cell size matches the doubled logical setting at P1");
        grain.ApplyCanvasScale(128, 144, 2);
        var resizedGrain = Document(scope, 128, 144, 2);
        resizedGrain.layers.Add(new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 2, seed = 431 }); Normalize(resizedGrain);
        Same(test, Pixels(resizedGrain), Pixels(grain), "Nonuniform Scale does not multiply Grain size twice");
    }
    static ColorFillLayerBehaviour Pattern(float factor, bool seamless)
    {
        var fill = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.Pattern };
        fill.pattern.shape = FillPatternSettings.Shape.Squares; fill.pattern.size = 7 * factor; fill.pattern.sizeY = 5 * factor;
        fill.pattern.linkSize = false; fill.pattern.offset = new Vector2(2, -1) * factor;
        fill.pattern.seamless = seamless; fill.pattern.gap = .15f; fill.pattern.roundness = .3f;
        return fill;
    }
    static void RotatedSourceMetrics(TestContext test, UnityAScope scope)
    {
        var turned = Document(scope, 64, 48); ColorFillLayerBehaviour pattern = Pattern(1, false);
        turned.layers.Add(pattern); Normalize(turned);
        turned.ApplyCanvasOrientation(CanvasOrientation.RotateLeft);
        turned.ApplyCanvasScale(96, 192, 2);
        Vector2 metric = (Vector2)Call(pattern.Owner, "GetPixelSourceCanvasSize", 96, 192);
        test.Near(192, metric.x, .00001, "Quarter-turned source X follows the new canvas Y scale");
        test.Near(96, metric.y, .00001, "Quarter-turned source Y follows the new canvas X scale");
        var reference = Document(scope, 96, 192, 2); ColorFillLayerBehaviour rotatedPattern = Pattern(1, false);
        rotatedPattern.pattern.rotation = 90; reference.layers.Add(rotatedPattern); Normalize(reference);
        Same(test, Pixels(reference), Pixels(turned), "Quarter turn then nonuniform Scale retains the physical Pattern cell sizes");

        var noise = Document(scope, 64, 48); Layer grain = new NoiseLayerBehaviour
            { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 2, seed = 761 };
        noise.layers.Add(grain); Normalize(noise); noise.ApplyCanvasOrientation(CanvasOrientation.RotateLeft); noise.ApplyCanvasScale(96, 192, 2);
        var expectedNoise = Document(scope, 96, 192); Layer expectedGrain = new NoiseLayerBehaviour
            { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 4, seed = 761 };
        expectedNoise.layers.Add(expectedGrain); expectedGrain.transform = grain.transform;
        LayerField("sourceCanvasSize").SetValue(expectedGrain, new Vector2Int(192, 96)); Normalize(expectedNoise);
        Same(test, Pixels(expectedNoise), Pixels(noise), "Quarter-turned Grain axes do not use unswapped canvas ratios");

        var nested = Document(scope, 64, 48); Layer group = new GroupLayerBehaviour(), inner = new GroupLayerBehaviour();
        ColorFillLayerBehaviour fill = Pattern(1, false); CoordinateCells(fill); Layer source = fill;
        nested.layers.Add(group); group.children.Add(inner); inner.children.Add(source);
        group.transform.rotation = 21; inner.transform.rotation = 13; Normalize(nested);
        nested.ApplyCanvasScale(128, 144, 2);
        double angle = 34 * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        double ratioX = Math.Sqrt(4 * c * c + 9 * s * s), ratioY = Math.Sqrt(4 * s * s + 9 * c * c);
        Vector2 accumulated = (Vector2)LayerField("sourceCanvasScale").GetValue(source);
        test.Near(ratioX, accumulated.x, .000001, "Nested source X follows its physical 34-degree composite axis");
        test.Near(ratioY, accumulated.y, .000001, "Nested source Y follows its physical perpendicular axis");
        var nestedReference = Document(scope, 128, 144); Layer refGroup = new GroupLayerBehaviour(), refInner = new GroupLayerBehaviour();
        ColorFillLayerBehaviour adjusted = Pattern(1, false);
        CoordinateCells(adjusted);
        adjusted.pattern.size = (float)(28 / ratioX); adjusted.pattern.sizeY = (float)(30 / ratioY);
        adjusted.pattern.offset = new Vector2((float)(8 / ratioX), (float)(-6 / ratioY));
        nestedReference.layers.Add(refGroup); refGroup.children.Add(refInner); refInner.children.Add(adjusted);
        refGroup.transform = group.transform; refInner.transform = inner.transform; Normalize(nestedReference);
        Same(test, Pixels(nestedReference), Pixels(nested), "Nested rotated Pattern matches independent physical axis lengths");
        nested.ApplyCanvasScale(64, 48, 1);
        accumulated = (Vector2)LayerField("sourceCanvasScale").GetValue(source);
        test.Near(1, accumulated.x, .000001, "Inverse nested resize restores source metric X");
        test.Near(1, accumulated.y, .000001, "Inverse nested resize restores source metric Y");
    }
    static void CoordinateCells(ColorFillLayerBehaviour fill)
    {
        // A constant distance ramp and deterministic random palette expose cell coordinates
        // independently of the rounded contour's source-space aspect ratio.
        fill.pattern.gap = 0; fill.pattern.roundness = 0; fill.pattern.cellColor = FillPatternSettings.CellColor.Random;
        fill.pattern.seed = 251;
        fill.pattern.gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
    }
    static void ReferencesAndPersistence(TestContext test, UnityAScope scope)
    {
        var document = Document(scope, 64, 48, 2.5f);
        Layer group = new GroupLayerBehaviour(), noise = new NoiseLayerBehaviour(); group.children.Add(noise); document.layers.Add(group); Normalize(document);
        document.ApplyCanvasFrame(new RectInt(7, 5, 45, 32));
        test.Equal(2.5f, document.pixelScale, "Crop retains P unless explicitly changed");
        document.ApplyCanvasScale(46, 37, 1.25f);
        Vector2 metric = (Vector2)Call(noise, "GetPixelSourceCanvasSize", 46, 37);
        test.Near(64d * 46 / 45, metric.x, .00001, "Fractional source metric X survives cropped Scale");
        test.Near(48d * 37 / 32, metric.y, .00001, "Nonuniform source metric Y survives cropped Scale");
        test.Equal(2.5f, (float)LayerField("sourcePixelScale").GetValue(noise), "Nested source reference keeps its original P");
        Vector2 scale = (Vector2)LayerField("sourceCanvasScale").GetValue(noise);
        document.ApplyCanvasOrientation(CanvasOrientation.RotateLeft);
        test.Equal(scale, (Vector2)LayerField("sourceCanvasScale").GetValue(noise), "Orientation retains the source's local metric axes");
        test.Equal(1.25f, document.pixelScale, "Orientation retains P");
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            string json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
            using var reopened = WhimTexDocumentJson.Read(json, false);
            test.Equal(1.25f, reopened.Document.pixelScale, "Pixel scale JSON roundtrip " + mode);
            Layer source = reopened.Document.layers[0].children[0];
            test.Equal(new Vector2Int(64, 48), (Vector2Int)LayerField("sourceCanvasSize").GetValue(source), "Source canvas JSON roundtrip " + mode);
            test.Equal(2.5f, (float)LayerField("sourcePixelScale").GetValue(source), "Source P JSON roundtrip " + mode);
            test.Equal(scale, (Vector2)LayerField("sourceCanvasScale").GetValue(source), "Fractional source metric JSON roundtrip " + mode);
        }
        const string legacy = "{\"format\":\"WhimTex\",\"version\":2,\"document\":{\"width\":64,\"height\":48},\"layers\":[]}";
        // Use the package's actual format name while omitting every new field.
        string exemplar = WhimTexDocumentJson.Write(Document(scope)).Json;
        int formatStart = exemplar.IndexOf("\"format\"", StringComparison.Ordinal);
        int valueStart = exemplar.IndexOf('"', exemplar.IndexOf(':', formatStart) + 1) + 1;
        string format = exemplar.Substring(valueStart, exemplar.IndexOf('"', valueStart) - valueStart);
        using var oldDocument = WhimTexDocumentJson.Read(legacy.Replace("\"WhimTex\"", "\"" + format + "\""), false);
        test.Equal(1f, oldDocument.Document.pixelScale, "Legacy omitted document pixelScale reads as one");
        var corrupted = Document(scope, 64, 48); Layer valid = new GradientLayerBehaviour(), invalid = new NoiseLayerBehaviour();
        corrupted.layers.Add(valid); corrupted.layers.Add(invalid); LayerField("sourceCanvasScale").SetValue(invalid, new Vector2(0, 1));
        string snapshot = JsonUtility.ToJson(corrupted); bool rejected = false;
        try { corrupted.ApplyCanvasScale(96, 72, 2); } catch (WhimTexDocumentException) { rejected = true; }
        test.True(rejected, "Invalid source metric rejects the whole operation");
        test.Equal(snapshot, JsonUtility.ToJson(corrupted), "A later invalid layer cannot leave earlier layers changed");
    }
    static void BinaryPersistence(TestContext test, UnityAScope scope)
    {
        var document = Document(scope, 64, 48, 2);
        var text = Text(TextLayoutMode.Point, TextEdgeMode.Pixel); Layer textLayer = text;
        Layer group = new GroupLayerBehaviour(), grain = new NoiseLayerBehaviour
            { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, seed = 819, grainSize = 3 };
        group.children.Add(grain); document.layers.Add(textLayer); document.layers.Add(group);
        var texture = scope.OwnObject(new Texture2D(7, 5, TextureFormat.RGBA32, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        var samples = new Color32[35];
        for (int i = 0; i < samples.Length; i++) samples[i] = new Color32((byte)(i * 7), (byte)(255 - i * 5), (byte)(19 + i * 3),
            (byte)(i % 4 == 0 ? 0 : 190));
        texture.SetPixels32(samples); texture.Apply(false, false);
        var drawing = new DrawingLayerBehaviour(); Layer raster = drawing; raster.filterMode = LayerFilterMode.Point;
        typeof(DrawingLayerBehaviour).GetField("pixels", Flags).SetValue(drawing, texture);
        group.children.Add(raster); Normalize(document);
        Call(text, "PrepareSavedAppearance", 64, 48);
        text.fontFamily = "WhimTexMissingBinaryPixelScaleFont_" + Guid.NewGuid().ToString("N");
        typeof(TextLayerBehaviour).GetField("fallbackFont", Flags).SetValue(text, text.fontFamily);
        typeof(TextLayerBehaviour).GetField("fallbackKey", Flags).SetValue(text, Call(text, "LayoutKey", 64, 48));
        document.ApplyCanvasFrame(new RectInt(-3, 2, 61, 47), 1.25f);
        document.ApplyCanvasScale(63, 51, 1.25f);
        Color[] expected = Pixels(document);
        Vector2 metric = (Vector2)LayerField("sourceCanvasScale").GetValue(grain);
        using var container = new WhimTexDocumentContainer();
        Type serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
        byte[] bytes = (byte[])Call(serializer, "Serialize", document, container);
        test.True(bytes.Length > 0, "TIFF binary model payload is nonempty");
        object result = Call(serializer, "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
        var reopened = scope.OwnObject((WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result)); Normalize(reopened);
        test.Equal(1.25f, reopened.pixelScale, "TIFF binary payload retains document P");
        test.Equal(63, reopened.width, "TIFF binary payload retains scaled width"); test.Equal(51, reopened.height, "TIFF binary payload retains scaled height");
        Layer restoredGrain = reopened.layers[1].children[0];
        test.Equal(new Vector2Int(64, 48), (Vector2Int)LayerField("sourceCanvasSize").GetValue(restoredGrain), "TIFF binary payload retains original source dimensions");
        test.Equal(2f, (float)LayerField("sourcePixelScale").GetValue(restoredGrain), "TIFF binary payload retains original source P");
        test.Equal(metric, (Vector2)LayerField("sourceCanvasScale").GetValue(restoredGrain), "TIFF binary payload retains fractional source metrics");
        var restoredText = (TextLayerBehaviour)reopened.layers[0].Behaviour;
        test.Equal(2f, (float)typeof(TextLayerBehaviour).GetField("fallbackPixelScale", Flags).GetValue(restoredText), "TIFF binary payload retains missing-font bitmap P independently of current document P");
        test.True((bool)typeof(TextLayerBehaviour).GetProperty("HasSavedAppearance", Flags).GetValue(restoredText), "TIFF binary payload retains a usable missing-font bitmap");
        var restoredDrawing = (DrawingLayerBehaviour)reopened.layers[1].children[1].Behaviour;
        var restoredPixels = (Texture2D)typeof(DrawingLayerBehaviour).GetField("pixels", Flags).GetValue(restoredDrawing);
        Color32[] actual = restoredPixels.GetPixels32(); test.Equal(samples.Length, actual.Length, "TIFF embedded Drawing source dimensions");
        for (int i = 0; i < samples.Length; i++) test.Equal(samples[i], actual[i], "TIFF retains original texels, including RGB below zero alpha, at " + i);
        Same(test, expected, Pixels(reopened), "TIFF binary payload restores P-dependent composition and retained font fallback", .012);
    }
    public static string Run() => TestContext.Run("Document pixel scale, physical source lengths, atomic canvas changes and persistence", test =>
        UnityAScope.RunOwned(scope =>
        {
            Model(test, scope); TextPixels(test, scope); Sources(test, scope); RotatedSourceMetrics(test, scope); ReferencesAndPersistence(test, scope); BinaryPersistence(test, scope);
        }));
}
