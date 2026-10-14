using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;

// Owned in-memory documents and textures only. No imports, assets, windows or global Undo.
public static class CanvasFrameTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly FieldInfo SourceSize = typeof(Layer).GetField("sourceCanvasSize", Flags);
    static object Call(object target, string name, params object[] args) =>
        (target is Type type ? type : target.GetType()).GetMethod(name, Flags).Invoke(target is Type ? null : target, args);
    static WhimTexDocument Document(UnityAScope scope, int width = 64, int height = 48)
    {
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.width = width; document.height = height;
        document.hideFlags = HideFlags.HideAndDontSave;
        return document;
    }
    static void Normalize(WhimTexDocument document) => Call(document, "NormalizeModel");
    static ProjectiveMatrix World(WhimTexDocument document, Layer layer) =>
        ((TextureTransform)Call(document, "GetCanvasTransform", layer)).ToMatrix(document.width, document.height);
    static Color[] Pixels(WhimTexDocument document)
    {
        Texture2D image = document.ComposeCanvas();
        try { return image.GetPixels(); }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    static void Same(TestContext test, Color[] expected, Color[] actual, double tolerance, string message)
    {
        test.Equal(expected.Length, actual.Length, message + " dimensions");
        double maximum = 0;
        for (int i = 0; i < expected.Length; i++)
            for (int channel = 0; channel < 4; channel++)
                maximum = Math.Max(maximum, Math.Abs(expected[i][channel] - actual[i][channel]));
        test.Near(0, maximum, tolerance, message);
    }
    static void CropPixels(TestContext test, Color[] before, int width, int height, Color[] after, RectInt frame, double tolerance, string message)
    {
        test.Equal(frame.width * frame.height, after.Length, message + " dimensions");
        double maximum = 0;
        double maximumAlpha = 0, maximumVisible = 0;
        int worstX = 0, worstY = 0;
        Color worstBefore = default, worstAfter = default;
        int compared = 0;
        for (int y = 0; y < frame.height; y++)
            for (int x = 0; x < frame.width; x++)
            {
                int sx = x + frame.x, sy = y + frame.y;
                if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                compared++;
                Color a = before[sy * width + sx], b = after[y * frame.width + x];
                maximumAlpha = Math.Max(maximumAlpha, Math.Abs(a.a - b.a));
                for (int channel = 0; channel < 4; channel++)
                {
                    double error = Math.Abs(a[channel] - b[channel]);
                    if (error > maximum)
                    {
                        maximum = error; worstX = x; worstY = y; worstBefore = a; worstAfter = b;
                    }
                    if (channel < 3) maximumVisible = Math.Max(maximumVisible, Math.Abs(a[channel] * a.a - b[channel] * b.a));
                }
            }
        test.True(compared > 0, message + " overlaps the original canvas");
        test.Near(0, maximum, tolerance, message + " retained pixels; maximum alpha=" + maximumAlpha +
            ", visible RGB=" + maximumVisible + ", worst=" + worstX + "," + worstY +
            ", before=" + worstBefore.ToString("F6") + ", after=" + worstAfter.ToString("F6"));
    }
    static void ShiftedPoint(TestContext test, ProjectiveMatrix before, ProjectiveMatrix after, Double2 source,
        int oldWidth, int oldHeight, RectInt frame, string message)
    {
        Double2 a = before.Point(source), b = after.Point(source);
        test.Near(a.x * oldWidth - frame.x, b.x * frame.width, 1e-9, message + " X");
        test.Near(a.y * oldHeight - frame.y, b.y * frame.height, 1e-9, message + " Y");
    }

    static void LiveShapeAntialiasing(TestContext test, Color[] before, int width, int height, Color[] after, RectInt frame, string message)
    {
        test.Equal(frame.width * frame.height, after.Length, message + " dimensions");
        double maxAlpha = 0, maxVisible = 0, outsideBandError = 0, alphaError = 0, expectedArea = 0, actualArea = 0;
        int compared = 0, bandPixels = 0;
        bool EdgeBand(int x, int y)
        {
            float center = before[y * width + x].a;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                float neighbor = before[ny * width + nx].a;
                if (neighbor > 0 && neighbor < 1 || (neighbor >= .5f) != (center >= .5f)) return true;
            }
            return false;
        }
        for (int y = 0; y < frame.height; y++) for (int x = 0; x < frame.width; x++)
        {
            int sx = x + frame.x, sy = y + frame.y;
            if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
            compared++;
            Color a = before[sy * width + sx], b = after[y * frame.width + x];
            bool band = EdgeBand(sx, sy); if (band) bandPixels++;
            double error = Math.Abs(a.a - b.a);
            maxAlpha = Math.Max(maxAlpha, error); alphaError += error;
            expectedArea += a.a; actualArea += b.a;
            if (!band) outsideBandError = Math.Max(outsideBandError, error);
            for (int channel = 0; channel < 3; channel++)
            {
                double visibleError = Math.Abs(a[channel] * a.a - b[channel] * b.a);
                maxVisible = Math.Max(maxVisible, visibleError);
                if (!band) outsideBandError = Math.Max(outsideBandError, visibleError);
            }
        }
        // Derivative-based AA is reevaluated on the resized viewport's 2x2 quad grid.
        // Only its existing edge band may change; geometry, solid pixels and total
        // coverage must stay stable. Unassociated RGB at alpha zero is not visible.
        test.True(compared > 0 && bandPixels > 0 && bandPixels < compared, message + " has a bounded AA edge band");
        test.Near(0, outsideBandError, .0005, message + " interior and exterior are unchanged");
        test.Near(0, maxAlpha, .05, message + " maximum edge-only alpha change");
        test.Near(0, maxVisible, .031, message + " maximum edge-only visible-color change");
        test.Near(0, alphaError / compared, .001, message + " mean absolute coverage change");
        test.Near(expectedArea, actualArea, .2, message + " subpixel coverage-area conservation");
    }

    static void Geometry(TestContext test, UnityAScope scope)
    {
        var document = Document(scope);
        Layer group = new GroupLayerBehaviour(), nested = new GroupLayerBehaviour(), leaf = new ShapeLayerBehaviour();
        Layer text = new TextLayerBehaviour { text = "Frame", fontSize = 13, layoutOffset = new Vector2(4, -2) };
        document.layers.Add(group); group.children.Add(nested); nested.children.Add(leaf); nested.children.Add(text);
        group.transform.position = new Double2(7, -4); group.transform.rotation = 17;
        group.transform.scale = new Double2(.8, 1.1);
        nested.transform.pivot = new Double2(.3, .7); nested.transform.rotation = -9;
        nested.transform.scale = new Double2(.9, .7); leaf.transform.position = new Double2(-3, 2);
        text.transform.pivot = new Double2(.2, .8); text.transform.rotation = 11;
        ProjectiveMatrix perspective = nested.transform.ToMatrix(64, 48); perspective.m20 = .11; perspective.m21 = -.07;
        test.True(nested.transform.TrySetMatrix(perspective), "Perspective source group is valid");
        Normalize(document);
        ProjectiveMatrix oldLeaf = World(document, leaf), oldText = World(document, text);
        Double2 oldTextPivot = oldText.Point(text.transform.pivot);
        string textSettings = JsonUtility.ToJson(text.Behaviour);
        var frame = new RectInt(-9, 5, 91, 37);
        test.True(document.ApplyCanvasFrame(frame), "Mixed crop/extension applies");
        ProjectiveMatrix newLeaf = World(document, leaf), newText = World(document, text);
        foreach (var point in new[] { new Double2(0, 0), new Double2(1, 0), new Double2(1, 1), new Double2(0, 1), new Double2(.35, .61) })
            ShiftedPoint(test, oldLeaf, newLeaf, point, 64, 48, frame, "Nested projective source");
        foreach (var pixel in new[] { new Double2(0, 0), new Double2(7, 13), new Double2(-18, 4) })
        {
            Double2 a = oldText.Point(new Double2(.5 + pixel.x / 64, .5 + pixel.y / 48));
            Double2 b = newText.Point(new Double2(.5 + pixel.x / 91, .5 + pixel.y / 37));
            test.Near(a.x * 64 - frame.x, b.x * 91, 1e-9, "Fixed text-pixel X");
            test.Near(a.y * 48 - frame.y, b.y * 37, 1e-9, "Fixed text-pixel Y");
        }
        Double2 newTextPivot = newText.Point(text.transform.pivot);
        test.Near(oldTextPivot.x * 64 - frame.x, newTextPivot.x * 91, 1e-9, "Text custom pivot X");
        test.Near(oldTextPivot.y * 48 - frame.y, newTextPivot.y * 37, 1e-9, "Text custom pivot Y");
        test.Equal(textSettings, JsonUtility.ToJson(text.Behaviour), "Crop retains editable Text layout settings");
        test.True(ReferenceEquals(document.layers[0], group) && ReferenceEquals(group.children[0], nested) &&
            ReferenceEquals(nested.children[0], leaf), "Crop retains hierarchy and layer identities");
        test.Equal(new Vector2Int(64, 48), (Vector2Int)SourceSize.GetValue(leaf), "Original source reference is frozen");
        test.True(document.ApplyCanvasFrame(new RectInt(9, -5, 64, 48)), "Inverse frame reveals the original canvas");
        ProjectiveMatrix restored = World(document, leaf);
        foreach (var point in new[] { new Double2(0, 0), new Double2(1, 1), new Double2(.3, .7) })
        {
            Double2 a = oldLeaf.Point(point), b = restored.Point(point);
            test.Near(a.x, b.x, 1e-12, "Repeated frame X"); test.Near(a.y, b.y, 1e-12, "Repeated frame Y");
        }
        test.Equal(new Vector2Int(64, 48), (Vector2Int)SourceSize.GetValue(leaf), "Repeated frame keeps source reference");
    }

    static void Operators(TestContext test, UnityAScope scope)
    {
        foreach (Layer effect in new Layer[] { new BlurLayerBehaviour(), new ShaderProcessorLayerBehaviour() })
        {
            var document = Document(scope); document.layers.Add(effect);
            effect.transform.position = new Double2(3, -2); effect.transform.rotation = 8;
            effect.transform.scale = new Double2(.9, 1.1); effect.transform.pivot = new Double2(.25, .6);
            ProjectiveMatrix before = effect.transform.ToMatrix(64, 48);
            Double2 pivot = before.Point(effect.transform.pivot);
            var frame = new RectInt(7, -4, 83, 39);
            document.ApplyCanvasFrame(frame);
            ProjectiveMatrix after = effect.transform.ToMatrix(83, 39);
            foreach (var pixel in new[] { new Double2(10, 17), new Double2(37, 5), new Double2(50, 43) })
            {
                Double2 a = before.Point(new Double2(pixel.x / 64, pixel.y / 48));
                Double2 b = after.Point(new Double2((pixel.x - frame.x) / 83, (pixel.y - frame.y) / 39));
                test.Near(a.x * 64 - frame.x, b.x * 83, 1e-10, "Input operator X is not transformed twice");
                test.Near(a.y * 48 - frame.y, b.y * 39, 1e-10, "Input operator Y is not transformed twice");
            }
            Double2 nextPivot = after.Point(effect.transform.pivot);
            test.Near(pivot.x * 64 - frame.x, nextPivot.x * 83, 1e-10, "Input pivot X");
            test.Near(pivot.y * 48 - frame.y, nextPivot.y * 39, 1e-10, "Input pivot Y");
        }
    }

    static void SourceRenders(TestContext test, UnityAScope scope)
    {
        var shape = new ShapeLayerBehaviour { edgeMode = ShapeLayerBehaviour.EdgeMode.Step, fillColor = new Color(.6f, .3f, .9f, 1),
            stroke = true, strokeWidth = 3 };
        var pattern = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.Pattern };
        pattern.pattern.size = 9; pattern.pattern.sizeY = 7; pattern.pattern.linkSize = false; pattern.pattern.rotation = 13;
        pattern.pattern.gap = .22f; pattern.pattern.offset = new Vector2(2, -3);
        var sources = new LayerBehaviour[]
        {
            shape, new GradientLayerBehaviour(), pattern,
            new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.Perlin, fractal = NoiseLayerBehaviour.FractalType.None },
            new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 3 },
            new TextLayerBehaviour { text = "Crop", fontSize = 15, layoutOffset = new Vector2(4, -3), edgeMode = TextEdgeMode.Pixel }
        };
        foreach (LayerBehaviour source in sources)
        {
            var document = Document(scope); Layer layer = source; document.layers.Add(layer);
            Normalize(document);
            Color[] before = Pixels(document);
            var frame = new RectInt(7, 5, 45, 32);
            document.ApplyCanvasFrame(frame);
            CropPixels(test, before, 64, 48, Pixels(document), frame, .012, source.GetType().Name);
            document.ApplyCanvasFrame(new RectInt(-7, -5, 64, 48));
            Same(test, before, Pixels(document), .012, "Revealed " + source.GetType().Name);
        }

        // Raster content beyond a cropped boundary remains in its owned original texture.
        var rasterDocument = Document(scope);
        Texture2D texture = scope.OwnObject(new Texture2D(64, 48, TextureFormat.RGBA32, false, true));
        texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
        var colors = new Color32[64 * 48];
        for (int y = 0; y < 48; y++) for (int x = 0; x < 64; x++)
            colors[y * 64 + x] = new Color32((byte)(x * 4), (byte)(y * 5), 71, (byte)(x < 5 || x > 58 ? 255 : 150));
        texture.SetPixels32(colors); texture.Apply(false, false);
        var drawing = new DrawingLayerBehaviour(); Layer raster = drawing;
        typeof(DrawingLayerBehaviour).GetField("pixels", Flags).SetValue(drawing, texture);
        raster.filterMode = LayerFilterMode.Point; rasterDocument.layers.Add(raster); Normalize(rasterDocument);
        Color[] original = Pixels(rasterDocument); var rasterFrame = new RectInt(11, 8, 30, 24);
        rasterDocument.ApplyCanvasFrame(rasterFrame);
        test.True(ReferenceEquals(texture, Call(drawing, "get_StoredTexture")), "Drawing retains its owned source texture");
        test.Equal(64, texture.width, "Drawing source width is retained"); test.Equal(48, texture.height, "Drawing source height is retained");
        CropPixels(test, original, 64, 48, Pixels(rasterDocument), rasterFrame, .012, "Drawing crop");
        rasterDocument.ApplyCanvasFrame(new RectInt(-11, -8, 64, 48));
        Same(test, original, Pixels(rasterDocument), .012, "Drawing outside pixels reappear");

        var emptyDocument = Document(scope); var empty = new DrawingLayerBehaviour(); emptyDocument.layers.Add(empty);
        emptyDocument.ApplyCanvasFrame(new RectInt(-8, -4, 80, 60));
        Call(empty, "InitializeCanvas", emptyDocument.width, emptyDocument.height);
        var emptyPixels = (Texture2D)Call(empty, "get_StoredTexture");
        test.Equal(64, emptyPixels.width, "Uninitialized Drawing retains its reference source width");
        test.Equal(48, emptyPixels.height, "Uninitialized Drawing retains its reference source height");
        test.True(Array.TrueForAll(emptyPixels.GetPixels32(), value => value.a == 0), "Empty Drawing stays transparent");
    }

    static void NestedOperators(TestContext test, UnityAScope scope)
    {
        Type contextType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
        Shader shader = Shader.Find("Hidden/WhimTex/Transform");
        test.True(shader != null, "Nested operator probe uses the actual transform shader");
        foreach (Layer effect in new Layer[] { new BlurLayerBehaviour(), new ShaderProcessorLayerBehaviour() })
        {
            var document = Document(scope);
            Layer group = new GroupLayerBehaviour(), inner = new GroupLayerBehaviour();
            document.layers.Add(group); group.children.Add(inner); inner.children.Add(effect);
            group.transform.position = new Double2(5, -3); group.transform.rotation = 17;
            group.transform.scale = new Double2(.8, 1.1);
            inner.transform.position = new Double2(-2, 1); inner.transform.rotation = -9;
            inner.transform.scale = new Double2(.9, .7);
            ProjectiveMatrix perspective = inner.transform.ToMatrix(64, 48);
            perspective.m20 = .05; perspective.m21 = -.03;
            test.True(inner.transform.TrySetMatrix(perspective), "Nested operator parent perspective is valid");
            effect.transform.position = new Double2(3, -2); effect.transform.rotation = 8;
            effect.transform.scale = new Double2(.9, 1.1); effect.transform.pivot = new Double2(.25, .6);
            Normalize(document);
            ProjectiveMatrix beforeWorld = World(document, effect);
            var renderProperty = typeof(Layer).GetProperty("RenderTransform", Flags);
            ProjectiveMatrix before = ((TextureTransform)renderProperty.GetValue(effect)).ToMatrix(64, 48);
            test.True(before.TryInverse(out var beforeInverse), "Original nested input operator is invertible");
            Double2 localPivot = effect.transform.pivot;
            Double2 oldPivot = beforeWorld.Point(localPivot);
            var frame = new RectInt(7, -4, 83, 39);
            document.ApplyCanvasFrame(frame);
            ProjectiveMatrix afterWorld = World(document, effect);
            ProjectiveMatrix after = ((TextureTransform)renderProperty.GetValue(effect)).ToMatrix(83, 39);
            test.Equal(localPivot, effect.transform.pivot, "Nested input pivot remains in its preserved parent-local basis");
            Double2 newPivot = afterWorld.Point(effect.transform.pivot);
            test.Near(oldPivot.x * 64 - frame.x, newPivot.x * 83, 1e-9, "Nested input world pivot X");
            test.Near(oldPivot.y * 48 - frame.y, newPivot.y * 39, 1e-9, "Nested input world pivot Y");

            var material = scope.OwnObject(new Material(shader) { hideFlags = HideFlags.HideAndDontSave });
            object context = Activator.CreateInstance(contextType, new object[] { document, null, 83, 39, 1f, true, true, null });
            Call(effect, "SetRenderInverse", material, "_TransformRow", context);
            Vector4 row0 = material.GetVector("_TransformRow0"), row1 = material.GetVector("_TransformRow1"), row2 = material.GetVector("_TransformRow2");
            var gpuInverse = new ProjectiveMatrix { m00 = row0.x, m01 = row0.y, m02 = row0.z,
                m10 = row1.x, m11 = row1.y, m12 = row1.z, m20 = row2.x, m21 = row2.y, m22 = row2.z };
            foreach (var pixel in new[] { new Double2(10, 17), new Double2(37, 5), new Double2(50, 43) })
            {
                var oldPoint = new Double2(pixel.x / 64, pixel.y / 48);
                var newPoint = new Double2((pixel.x - frame.x) / 83, (pixel.y - frame.y) / 39);
                Double2 a = before.Point(oldPoint), b = after.Point(newPoint);
                test.Near(a.x * 64 - frame.x, b.x * 83, 1e-9, "Nested render operator X is conjugated once");
                test.Near(a.y * 48 - frame.y, b.y * 39, 1e-9, "Nested render operator Y is conjugated once");
                Double2 inverseA = beforeInverse.Point(oldPoint), inverseB = gpuInverse.Point(newPoint);
                test.Near(inverseA.x * 64 - frame.x, inverseB.x * 83, .00002, "Nested GPU input inverse X");
                test.Near(inverseA.y * 48 - frame.y, inverseB.y * 39, .00002, "Nested GPU input inverse Y");
            }
        }
    }

    static void Persistence(TestContext test, UnityAScope scope)
    {
        var document = Document(scope); Layer layer = new NoiseLayerBehaviour(); document.layers.Add(layer); Normalize(document);
        document.ApplyCanvasFrame(new RectInt(4, 7, 53, 33)); Color[] expected = Pixels(document);
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            string json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
            using var read = WhimTexDocumentJson.Read(json, false);
            test.Equal(new Vector2Int(64, 48), (Vector2Int)SourceSize.GetValue(read.Document.layers[0]), "JSON retains source frame " + mode);
            Same(test, expected, Pixels(read.Document), .012, "JSON cropped appearance " + mode);
        }
        using var container = new WhimTexDocumentContainer();
        Type serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
        byte[] data = (byte[])Call(serializer, "Serialize", document, container);
        object result = Call(serializer, "Deserialize", data, container, typeof(WhimTexDocument), null, false);
        var reopened = scope.OwnObject((WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result));
        Normalize(reopened);
        test.Equal(new Vector2Int(64, 48), (Vector2Int)SourceSize.GetValue(reopened.layers[0]), "TIFF payload retains source frame");
        Same(test, expected, Pixels(reopened), .012, "TIFF cropped appearance");
        string invalid = "{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"bad\",\"sourceCanvasSize\":[0,48],\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\"}}]}";
        bool rejected = false;
        try { using var read = WhimTexDocumentJson.Read(invalid, false); }
        catch (WhimTexDocumentException) { rejected = true; }
        test.True(rejected, "JSON rejects a half-defined source reference");
    }

    static void TextGrid(TestContext test, UnityAScope scope)
    {
        foreach (TextEdgeMode edge in Enum.GetValues(typeof(TextEdgeMode)))
        foreach (TextLayoutMode layout in Enum.GetValues(typeof(TextLayoutMode)))
        {
            var document = Document(scope);
            var text = new TextLayerBehaviour
            {
                text = layout == TextLayoutMode.Point ? "Crop" : "Crop\nframe",
                fontSize = 11, edgeMode = edge, layoutMode = layout,
                frameSize = new Vector2(39, 30), layoutOffset = new Vector2(3, -2)
            };
            Layer layer = text; document.layers.Add(layer); Normalize(document);
            Color[] original = Pixels(document);
            foreach (var frame in new[] { new RectInt(7, 5, 45, 32), new RectInt(-4, -3, 73, 55) })
            {
                document.ApplyCanvasFrame(frame);
                CropPixels(test, original, 64, 48, Pixels(document), frame, .012,
                    "Text grid parity " + edge + "/" + layout + "/" + frame.size);
                document.ApplyCanvasFrame(new RectInt(-frame.x, -frame.y, 64, 48));
                Same(test, original, Pixels(document), .012, "Text grid restored " + edge + "/" + layout);
            }
            string json = WhimTexDocumentJson.Write(document).Json;
            using var reopened = WhimTexDocumentJson.Read(json, false);
            Same(test, original, Pixels(reopened.Document), .012, "Text reference grid JSON " + edge + "/" + layout);
        }
    }

    static void GroupsAndFx(TestContext test, UnityAScope scope)
    {
        foreach (GroupCompositing compositing in Enum.GetValues(typeof(GroupCompositing)))
        foreach (ShapeLayerBehaviour.EdgeMode edge in Enum.GetValues(typeof(ShapeLayerBehaviour.EdgeMode)))
        {
            var document = Document(scope);
            Layer group = new GroupLayerBehaviour(), shape = new ShapeLayerBehaviour
            {
                kind = ShapeLayerBehaviour.ShapeKind.Ellipse, fillColor = new Color(.8f, .2f, .4f, 1), edgeMode = edge
            };
            document.layers.Add(group); group.children.Add(shape); group.compositing = compositing;
            group.transform.position = new Double2(4, -3); group.transform.rotation = 16;
            shape.transform.scale = new Double2(.5, .4); Normalize(document);
            Color[] before = Pixels(document); ProjectiveMatrix oldWorld = World(document, shape);
            var frame = new RectInt(5, 3, 51, 38);
            document.ApplyCanvasFrame(frame);
            ProjectiveMatrix newWorld = World(document, shape);
            foreach (var point in new[] { new Double2(0, 0), new Double2(1, 1), new Double2(.5, .5) })
                ShiftedPoint(test, oldWorld, newWorld, point, 64, 48, frame, "Group geometry " + compositing + "/" + edge);
            Color[] after = Pixels(document);
            if (edge == ShapeLayerBehaviour.EdgeMode.Step)
                CropPixels(test, before, 64, 48, after, frame, .0005, "Group Step " + compositing);
            else LiveShapeAntialiasing(test, before, 64, 48, after, frame, "Group AA " + compositing);
        }

        var fxDocument = Document(scope); Layer fill = new GradientLayerBehaviour(); fxDocument.layers.Add(fill); Normalize(fxDocument);
        var effect = scope.OwnObject(ScriptableObject.CreateInstance<ShaderFX>());
        typeof(ShaderFX).GetField("code", Flags).SetValue(effect,
            "float4 ApplyFX(float2 uv, float4 color) { float2 p = LayerToLocal(uv); return color * float4(p.x, 1-p.y, .7, 1); }");
        test.True((bool)Call(effect, "ApplyCore", false), "Owned local-coordinate FX compiles without a document edit or global Undo group");
        fill.fx.Add(effect);
        Color[] fxBefore = Pixels(fxDocument); var fxFrame = new RectInt(6, 8, 49, 31);
        fxDocument.ApplyCanvasFrame(fxFrame);
        test.True(ReferenceEquals(effect, fill.fx[0]), "Frame retains editable FX identity");
        CropPixels(test, fxBefore, 64, 48, Pixels(fxDocument), fxFrame, .012, "Layer-local FX coordinates");
    }

    static void Validation(TestContext test, UnityAScope scope)
    {
        var document = Document(scope); Layer first = new GradientLayerBehaviour(), invalid = new ShapeLayerBehaviour();
        document.layers.Add(first); document.layers.Add(invalid); invalid.transform.scale = new Double2(0, 1);
        string before = JsonUtility.ToJson(document);
        bool rejected = false;
        try { document.ApplyCanvasFrame(new RectInt(1, 2, 30, 20)); }
        catch (InvalidOperationException) { rejected = true; }
        test.True(rejected, "Singular transform is rejected");
        test.Equal(before, JsonUtility.ToJson(document), "A failed transform leaves all model data unchanged");
        invalid.transform = TextureTransform.Default;
        foreach (var frame in new[] { new RectInt(0, 0, 0, 20), new RectInt(0, 0, -2, 20), new RectInt(0, 0, 16385, 1), new RectInt(0, 0, 4097, 4096) })
        {
            rejected = false; before = JsonUtility.ToJson(document);
            try { document.ApplyCanvasFrame(frame); } catch (ArgumentOutOfRangeException) { rejected = true; }
            test.True(rejected, "Invalid dimensions are rejected " + frame);
            test.Equal(before, JsonUtility.ToJson(document), "Rejected dimensions leave model unchanged");
        }
        before = JsonUtility.ToJson(document);
        test.True(!document.ApplyCanvasFrame(new RectInt(0, 0, 64, 48)), "Identical frame returns false");
        test.Equal(before, JsonUtility.ToJson(document), "No-op does not freeze or rewrite transforms");
        Layer group = new GroupLayerBehaviour(); document.layers.Clear(); document.layers.Add(group); group.children.Add(group);
        try
        {
            rejected = false;
            try { document.ApplyCanvasFrame(new RectInt(0, 0, 65, 48)); } catch (InvalidOperationException) { rejected = true; }
            test.True(rejected, "Cyclic hierarchy is rejected before mutation or refresh");
            test.Equal(64, document.width, "Cyclic hierarchy does not change dimensions");
        }
        finally { group.children.Clear(); }
        var empty = Document(scope, 1, 1);
        test.True(empty.ApplyCanvasFrame(new RectInt(-2, -3, 16384, 1)), "Thin canvas accepts the existing maximum dimension");
        test.Equal(16384, empty.width, "Maximum dimension is retained exactly");
        test.True(empty.ApplyCanvasFrame(new RectInt(0, 0, 1, 1)), "Canvas can be reduced to one pixel");
    }

    static Double2 OrientedUv(Double2 point, CanvasOrientation orientation) => orientation switch
    {
        CanvasOrientation.FlipHorizontal => new Double2(1 - point.x, point.y),
        CanvasOrientation.FlipVertical => new Double2(point.x, 1 - point.y),
        CanvasOrientation.RotateLeft => new Double2(1 - point.y, point.x),
        CanvasOrientation.RotateRight => new Double2(point.y, 1 - point.x),
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };
    static bool QuarterTurn(CanvasOrientation orientation) =>
        orientation == CanvasOrientation.RotateLeft || orientation == CanvasOrientation.RotateRight;
    static CanvasOrientation InverseOrientation(CanvasOrientation orientation) => orientation switch
    {
        CanvasOrientation.RotateLeft => CanvasOrientation.RotateRight,
        CanvasOrientation.RotateRight => CanvasOrientation.RotateLeft,
        _ => orientation
    };
    static Color[] OrientedPixels(Color[] original, int width, int height, CanvasOrientation orientation)
    {
        int nextWidth = QuarterTurn(orientation) ? height : width;
        var expected = new Color[original.Length];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int nx = x, ny = y;
            switch (orientation)
            {
                case CanvasOrientation.FlipHorizontal: nx = width - 1 - x; break;
                case CanvasOrientation.FlipVertical: ny = height - 1 - y; break;
                case CanvasOrientation.RotateLeft: nx = height - 1 - y; ny = x; break;
                case CanvasOrientation.RotateRight: nx = y; ny = width - 1 - x; break;
            }
            expected[ny * nextWidth + nx] = original[y * width + x];
        }
        return expected;
    }
    static void CanvasOrientationPixels(TestContext test, UnityAScope scope)
    {
        foreach (CanvasOrientation orientation in Enum.GetValues(typeof(CanvasOrientation)))
        {
            var document = Document(scope, 7, 5);
            var texture = scope.OwnObject(new Texture2D(7, 5, TextureFormat.RGBA32, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave });
            var samples = new Color32[35];
            for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++)
                samples[y * 7 + x] = new Color32((byte)(19 + x * 29 + y * 7), (byte)(7 + y * 43 + x * 5),
                    (byte)(23 + x * 11 + y * 17), (byte)((x + y * 2) % 3 == 0 ? 255 : 190));
            texture.SetPixels32(samples); texture.Apply(false, false);
            var drawing = new DrawingLayerBehaviour(); Layer layer = drawing;
            typeof(DrawingLayerBehaviour).GetField("pixels", Flags).SetValue(drawing, texture);
            layer.filterMode = LayerFilterMode.Point; document.layers.Add(layer); Normalize(document);
            Color[] original = Pixels(document);
            test.True(document.ApplyCanvasOrientation(orientation), "Whole canvas orientation applies " + orientation);
            test.Equal(QuarterTurn(orientation) ? 5 : 7, document.width, "Orientation width " + orientation);
            test.Equal(QuarterTurn(orientation) ? 7 : 5, document.height, "Orientation height " + orientation);
            Same(test, OrientedPixels(original, 7, 5, orientation), Pixels(document), .001,
                "Asymmetric raster pixel placement " + orientation);
            test.True(ReferenceEquals(texture, Call(drawing, "get_StoredTexture")), "Orientation retains raster source " + orientation);
            test.True(Array.TrueForAll(texture.GetPixels32(), value => value.a > 0), "Orientation does not truncate source pixels");
            test.Equal(new Vector2Int(7, 5), (Vector2Int)SourceSize.GetValue(layer), "Orientation freezes original source dimensions");
            using var container = new WhimTexDocumentContainer();
            Type serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
            byte[] payload = (byte[])Call(serializer, "Serialize", document, container);
            object result = Call(serializer, "Deserialize", payload, container, typeof(WhimTexDocument), null, false);
            var reopened = scope.OwnObject((WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result));
            Normalize(reopened);
            Same(test, Pixels(document), Pixels(reopened), .001, "TIFF orientation roundtrip " + orientation);
            document.ApplyCanvasOrientation(InverseOrientation(orientation));
            Same(test, original, Pixels(document), .001, "Inverse orientation restores every raster pixel " + orientation);
            test.Equal(7, document.width, "Inverse restores width"); test.Equal(5, document.height, "Inverse restores height");
        }
        foreach (CanvasOrientation orientation in Enum.GetValues(typeof(CanvasOrientation)))
        foreach (TextEdgeMode edge in Enum.GetValues(typeof(TextEdgeMode)))
        {
            var document = Document(scope, 63, 45);
            Layer text = new TextLayerBehaviour { text = "Turn", fontSize = 11, layoutOffset = new Vector2(3, -2), edgeMode = edge };
            document.layers.Add(text); Normalize(document); Color[] original = Pixels(document);
            document.ApplyCanvasOrientation(orientation);
            Same(test, OrientedPixels(original, 63, 45, orientation), Pixels(document), .012,
                "Text source grid orientation " + orientation + "/" + edge);
            document.ApplyCanvasOrientation(InverseOrientation(orientation));
            Same(test, original, Pixels(document), .012, "Text inverse orientation " + orientation + "/" + edge);
        }
        foreach (CanvasOrientation orientation in Enum.GetValues(typeof(CanvasOrientation)))
        {
            var document = Document(scope, 31, 23);
            Layer noise = new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 2, seed = 113 };
            document.layers.Add(noise); Normalize(document); Color[] original = Pixels(document);
            document.ApplyCanvasOrientation(orientation);
            Same(test, OrientedPixels(original, 31, 23, orientation), Pixels(document), .001,
                "Noise source pixel scale orientation " + orientation);
            using var reopened = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false);
            Same(test, Pixels(document), Pixels(reopened.Document), .001, "JSON orientation roundtrip " + orientation);
            for (int turn = 0; turn < 4; turn++) document.ApplyCanvasOrientation(CanvasOrientation.RotateLeft);
            Same(test, Pixels(reopened.Document), Pixels(document), .001, "Four quarter turns preserve appearance " + orientation);
        }
    }
    static void CanvasOrientationGeometry(TestContext test, UnityAScope scope)
    {
        foreach (CanvasOrientation orientation in Enum.GetValues(typeof(CanvasOrientation)))
        {
            var document = Document(scope);
            Layer group = new GroupLayerBehaviour(), inner = new GroupLayerBehaviour(), shape = new ShapeLayerBehaviour();
            Layer text = new TextLayerBehaviour { text = "Frame", fontSize = 13, layoutOffset = new Vector2(4, -2) };
            Layer rootInput = new BlurLayerBehaviour(), nestedInput = new ShaderProcessorLayerBehaviour();
            document.layers.Add(rootInput); document.layers.Add(group);
            group.children.Add(inner); inner.children.Add(shape); inner.children.Add(text); inner.children.Add(nestedInput);
            group.transform.position = new Double2(5, -3); group.transform.rotation = 17;
            group.transform.scale = new Double2(.8, 1.1); inner.transform.rotation = -9;
            ProjectiveMatrix perspective = inner.transform.ToMatrix(64, 48); perspective.m20 = .06; perspective.m21 = -.04;
            test.True(inner.transform.TrySetMatrix(perspective), "Orientation perspective parent is valid");
            rootInput.transform.position = new Double2(2, -1); rootInput.transform.rotation = 12;
            nestedInput.transform.position = new Double2(-2, 3); nestedInput.transform.scale = new Double2(.9, 1.1);
            text.transform.pivot = new Double2(.2, .7); text.transform.rotation = 11;
            Normalize(document);
            ProjectiveMatrix shapeBefore = World(document, shape), textBefore = World(document, text);
            var renderProperty = typeof(Layer).GetProperty("RenderTransform", Flags);
            ProjectiveMatrix rootBefore = ((TextureTransform)renderProperty.GetValue(rootInput)).ToMatrix(64, 48);
            ProjectiveMatrix nestedBefore = ((TextureTransform)renderProperty.GetValue(nestedInput)).ToMatrix(64, 48);
            Double2 textPivot = textBefore.Point(text.transform.pivot);
            string textSettings = JsonUtility.ToJson(text.Behaviour);
            document.ApplyCanvasOrientation(orientation);
            ProjectiveMatrix shapeAfter = World(document, shape), textAfter = World(document, text);
            foreach (var source in new[] { new Double2(0, 0), new Double2(1, 1), new Double2(.3, .7) })
            {
                Double2 expected = OrientedUv(shapeBefore.Point(source), orientation), actual = shapeAfter.Point(source);
                test.Near(expected.x, actual.x, 1e-12, "Nested shape oriented X");
                test.Near(expected.y, actual.y, 1e-12, "Nested shape oriented Y");
                foreach (var input in new[] { (layer: rootInput, before: rootBefore), (layer: nestedInput, before: nestedBefore) })
                {
                    var after = ((TextureTransform)renderProperty.GetValue(input.layer)).ToMatrix(document.width, document.height);
                    expected = OrientedUv(input.before.Point(source), orientation);
                    actual = after.Point(OrientedUv(source, orientation));
                    test.Near(expected.x, actual.x, 1e-12, "Input operator oriented X");
                    test.Near(expected.y, actual.y, 1e-12, "Input operator oriented Y");
                }
            }
            foreach (var pixel in new[] { new Double2(0, 0), new Double2(13, -7), new Double2(-5, 11) })
            {
                Double2 expected = OrientedUv(textBefore.Point(new Double2(.5 + pixel.x / 64, .5 + pixel.y / 48)), orientation);
                Double2 actual = textAfter.Point(new Double2(.5 + pixel.x / document.width, .5 + pixel.y / document.height));
                test.Near(expected.x, actual.x, 1e-12, "Fixed local Text pixel orientation X");
                test.Near(expected.y, actual.y, 1e-12, "Fixed local Text pixel orientation Y");
            }
            Double2 expectedPivot = OrientedUv(textPivot, orientation), actualPivot = textAfter.Point(text.transform.pivot);
            test.Near(expectedPivot.x, actualPivot.x, 1e-12, "Text custom pivot orientation X");
            test.Near(expectedPivot.y, actualPivot.y, 1e-12, "Text custom pivot orientation Y");
            test.Equal(textSettings, JsonUtility.ToJson(text.Behaviour), "Orientation keeps editable Text settings");
            test.True(ReferenceEquals(document.layers[1], group) && ReferenceEquals(group.children[0], inner) &&
                ReferenceEquals(inner.children[0], shape), "Orientation retains hierarchy and identities");
        }
        var invalid = Document(scope); Layer first = new GradientLayerBehaviour(), broken = new ShapeLayerBehaviour();
        invalid.layers.Add(first); invalid.layers.Add(broken); broken.transform.scale = new Double2(0, 1);
        string snapshot = JsonUtility.ToJson(invalid); bool rejected = false;
        try { invalid.ApplyCanvasOrientation(CanvasOrientation.RotateLeft); } catch (InvalidOperationException) { rejected = true; }
        test.True(rejected, "Orientation rejects a singular transform atomically");
        test.Equal(snapshot, JsonUtility.ToJson(invalid), "Rejected orientation leaves all model data unchanged");
        rejected = false;
        try { invalid.ApplyCanvasOrientation((CanvasOrientation)999); } catch (ArgumentOutOfRangeException) { rejected = true; }
        test.True(rejected, "Undefined orientation enum is rejected");
        test.Equal(snapshot, JsonUtility.ToJson(invalid), "Invalid enum leaves model unchanged");
    }

    static Color[] RepeatedPixels(Color[] original, int width, int height, int scaleX, int scaleY)
    {
        int nextWidth = width * scaleX, nextHeight = height * scaleY;
        var result = new Color[nextWidth * nextHeight];
        for (int y = 0; y < nextHeight; y++) for (int x = 0; x < nextWidth; x++)
            result[y * nextWidth + x] = original[(y / scaleY) * width + x / scaleX];
        return result;
    }
    static void CanvasScalePixels(TestContext test, UnityAScope scope)
    {
        var document = Document(scope, 7, 5);
        var texture = scope.OwnObject(new Texture2D(7, 5, TextureFormat.RGBA32, false, true)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave });
        var samples = new Color32[35];
        for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++)
            samples[y * 7 + x] = new Color32((byte)(19 + x * 29 + y * 7), (byte)(7 + y * 43 + x * 5),
                (byte)(23 + x * 11 + y * 17), (byte)((x + y * 2) % 3 == 0 ? 255 : 190));
        texture.SetPixels32(samples); texture.Apply(false, false);
        var drawing = new DrawingLayerBehaviour(); Layer raster = drawing;
        typeof(DrawingLayerBehaviour).GetField("pixels", Flags).SetValue(drawing, texture);
        raster.filterMode = LayerFilterMode.Point; document.layers.Add(raster); Normalize(document);
        Color[] original = Pixels(document);
        test.True(document.ApplyCanvasScale(21, 10), "Nonuniform whole-canvas Scale applies");
        Same(test, RepeatedPixels(original, 7, 5, 3, 2), Pixels(document), .001, "Asymmetric raster stretches over the entire canvas");
        test.True(ReferenceEquals(texture, Call(drawing, "get_StoredTexture")), "Scale retains the original owned Drawing texture");
        test.Equal(7, texture.width, "Scale does not resample source width"); test.Equal(5, texture.height, "Scale does not resample source height");
        test.Equal(new Vector2Int(7, 5), (Vector2Int)SourceSize.GetValue(raster), "Scale retains source reference dimensions");
        document.ApplyCanvasScale(7, 5);
        Same(test, original, Pixels(document), .001, "Inverse Scale restores every Drawing pixel");

        foreach (TextEdgeMode edge in Enum.GetValues(typeof(TextEdgeMode)))
        {
            var textDocument = Document(scope);
            Layer text = new TextLayerBehaviour { text = "Stretch", fontSize = 11, layoutOffset = new Vector2(3, -2), edgeMode = edge };
            text.filterMode = LayerFilterMode.Point; textDocument.layers.Add(text); Normalize(textDocument);
            Color[] before = Pixels(textDocument); string settings = JsonUtility.ToJson(text.Behaviour);
            textDocument.ApplyCanvasScale(128, 96);
            var reference = Document(scope, 128, 96);
            Layer referenceText = new TextLayerBehaviour { text = "Stretch", fontSize = 11, layoutOffset = new Vector2(3, -2), edgeMode = edge };
            referenceText.filterMode = LayerFilterMode.Point; reference.layers.Add(referenceText); Normalize(reference);
            Same(test, Pixels(reference), Pixels(textDocument), .001, "Scale retains fixed physical Text sizes when pixelScale is unchanged " + edge);
            textDocument.ApplyPixelScale(2); reference.ApplyPixelScale(2);
            Same(test, Pixels(reference), Pixels(textDocument), .001, "Text pixelScale is applied once after Scale " + edge);
            test.Equal(settings, JsonUtility.ToJson(text.Behaviour), "Scale does not rewrite Text typography " + edge);
            textDocument.ApplyCanvasScale(64, 48, 1);
            Same(test, before, Pixels(textDocument), .001, "Text inverse Scale " + edge);
        }
        var pattern = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.Pattern };
        pattern.pattern.size = 9; pattern.pattern.sizeY = 7; pattern.pattern.linkSize = false;
        pattern.pattern.offset = new Vector2(2, -3); pattern.pattern.gap = .22f;
        foreach (LayerBehaviour source in new LayerBehaviour[]
        {
            new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainSize = 2, seed = 113 }, pattern,
            new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Ellipse, edgeMode = ShapeLayerBehaviour.EdgeMode.Step }
        })
        {
            var procedural = Document(scope, 31, 23); Layer layer = source; procedural.layers.Add(layer);
            layer.transform.tiling = TransformTilingMode.Repeat; Normalize(procedural);
            Color[] before = Pixels(procedural); procedural.ApplyCanvasScale(93, 69, 3);
            Color[] after = Pixels(procedural);
            // Odd integer factors expose identical source sample centers while allowing
            // the procedural source to evaluate its additional high-resolution samples.
            double maximum = 0;
            for (int y = 0; y < 23; y++) for (int x = 0; x < 31; x++)
            {
                Color a = before[y * 31 + x], b = after[(y * 3 + 1) * 93 + x * 3 + 1];
                for (int channel = 0; channel < 4; channel++) maximum = Math.Max(maximum, Math.Abs(a[channel] - b[channel]));
            }
            test.Near(0, maximum, .001, "Tiled source centers are preserved during Scale " + source.GetType().Name);
            procedural.ApplyCanvasScale(31, 23, 1);
            Same(test, before, Pixels(procedural), .001, "Procedural inverse Scale " + source.GetType().Name);
        }
    }
    static void CanvasScaleGeometry(TestContext test, UnityAScope scope)
    {
        var document = Document(scope);
        Layer group = new GroupLayerBehaviour(), inner = new GroupLayerBehaviour(), shape = new ShapeLayerBehaviour();
        Layer text = new TextLayerBehaviour { text = "Scale", fontSize = 13, layoutOffset = new Vector2(4, -2) };
        Layer rootInput = new BlurLayerBehaviour(), nestedInput = new ShaderProcessorLayerBehaviour();
        document.layers.Add(rootInput); document.layers.Add(group); group.children.Add(inner);
        inner.children.Add(shape); inner.children.Add(text); inner.children.Add(nestedInput);
        group.transform.position = new Double2(5, -3); group.transform.rotation = 17; group.transform.scale = new Double2(.8, 1.1);
        inner.transform.rotation = -9;
        ProjectiveMatrix perspective = inner.transform.ToMatrix(64, 48); perspective.m20 = .06; perspective.m21 = -.04;
        test.True(inner.transform.TrySetMatrix(perspective), "Scale perspective parent is valid");
        rootInput.transform.position = new Double2(2, -1); rootInput.transform.rotation = 12;
        nestedInput.transform.position = new Double2(-2, 3); nestedInput.transform.scale = new Double2(.9, 1.1);
        text.transform.pivot = new Double2(.2, .7); text.transform.rotation = 11; Normalize(document);
        ProjectiveMatrix shapeBefore = World(document, shape), textBefore = World(document, text);
        var renderProperty = typeof(Layer).GetProperty("RenderTransform", Flags);
        ProjectiveMatrix rootBefore = ((TextureTransform)renderProperty.GetValue(rootInput)).ToMatrix(64, 48);
        ProjectiveMatrix nestedBefore = ((TextureTransform)renderProperty.GetValue(nestedInput)).ToMatrix(64, 48);
        Double2 oldPivot = textBefore.Point(text.transform.pivot);
        document.ApplyCanvasScale(96, 120);
        ProjectiveMatrix shapeAfter = World(document, shape), textAfter = World(document, text);
        foreach (var source in new[] { new Double2(0, 0), new Double2(1, 1), new Double2(.3, .7) })
        {
            Double2 a = shapeBefore.Point(source), b = shapeAfter.Point(source);
            test.Near(a.x * 64 * 1.5, b.x * 96, 1e-10, "Nested shape Scale X");
            test.Near(a.y * 48 * 2.5, b.y * 120, 1e-10, "Nested shape Scale Y");
            foreach (var input in new[] { (layer: rootInput, before: rootBefore), (layer: nestedInput, before: nestedBefore) })
            {
                var after = ((TextureTransform)renderProperty.GetValue(input.layer)).ToMatrix(96, 120);
                a = input.before.Point(source); b = after.Point(source);
                test.Near(a.x, b.x, 1e-12, "Scale retains normalized input operator X");
                test.Near(a.y, b.y, 1e-12, "Scale retains normalized input operator Y");
            }
        }
        foreach (var pixel in new[] { new Double2(0, 0), new Double2(13, -7), new Double2(-5, 11) })
        {
            Double2 source = new Double2(.5 + pixel.x / 64, .5 + pixel.y / 48);
            Double2 a = textBefore.Point(source), b = textAfter.Point(source);
            test.Near(a.x, b.x, 1e-12, "Scale retains Text normalized layer geometry X");
            test.Near(a.y, b.y, 1e-12, "Scale retains Text normalized layer geometry Y");
        }
        Double2 newPivot = textAfter.Point(text.transform.pivot);
        test.Near(oldPivot.x, newPivot.x, 1e-12, "Scale retains Text normalized pivot X");
        test.Near(oldPivot.y, newPivot.y, 1e-12, "Scale retains Text normalized pivot Y");
        test.True(ReferenceEquals(document.layers[1], group) && ReferenceEquals(group.children[0], inner) &&
            ReferenceEquals(inner.children[0], shape), "Scale retains hierarchy and identities");
        string snapshot = JsonUtility.ToJson(document);
        test.True(!document.ApplyCanvasScale(96, 120), "Identical Scale returns false");
        test.Equal(snapshot, JsonUtility.ToJson(document), "No-op Scale preserves all data");
        foreach (var size in new[] { new Vector2Int(0, 20), new Vector2Int(-2, 20), new Vector2Int(16385, 1), new Vector2Int(4097, 4096) })
        {
            bool rejected = false;
            try { document.ApplyCanvasScale(size.x, size.y); } catch (ArgumentOutOfRangeException) { rejected = true; }
            test.True(rejected, "Scale rejects invalid dimensions " + size);
            test.Equal(snapshot, JsonUtility.ToJson(document), "Invalid Scale is atomic");
        }
        var empty = Document(scope, 1, 1);
        test.True(empty.ApplyCanvasScale(16384, 1), "Empty Scale supports the existing maximum dimension");
        test.True(empty.ApplyCanvasScale(1, 1), "Empty Scale supports one pixel");
    }

    public static string Run() => TestContext.Run("Nondestructive canvas frame, sources, transforms and persistence", test =>
        UnityAScope.RunOwned(scope =>
        {
            Geometry(test, scope); Operators(test, scope); NestedOperators(test, scope);
            SourceRenders(test, scope); TextGrid(test, scope); GroupsAndFx(test, scope);
            Persistence(test, scope); Validation(test, scope);
            CanvasOrientationPixels(test, scope); CanvasOrientationGeometry(test, scope);
            CanvasScalePixels(test, scope); CanvasScaleGeometry(test, scope);
        }));
}
