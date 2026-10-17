using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Owned transient documents only. Reflection reaches our own internal lifecycle/layout,
// never Unity internals. No font/project assets or user documents are changed.
public static class TextOutlineTests
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static WhimTexDocument Document()
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = 320; doc.height = 192;
        doc.outputPrecision = WhimTexOutputPrecision.Float32;
        return doc;
    }
    static string FontName()
    {
        var names = FontEngine.GetSystemFontNames();
        foreach (string preferred in new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica" })
            foreach (string name in names) if (name.StartsWith(preferred + " - ", StringComparison.OrdinalIgnoreCase)) return preferred;
        return (string)typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.SystemFontCatalog")
            .GetProperty("DefaultName", Hidden).GetValue(null);
    }
    static void Release(Layer layer) => typeof(Layer).GetMethod("ReleaseTransientResources", Hidden).Invoke(layer, null);
    static Color[] Pixels(WhimTexDocument doc)
    {
        var image = doc.ComposeCanvas();
        try { return image.GetPixels(); } finally { Object.DestroyImmediate(image); }
    }
    static double Difference(Color[] a, Color[] b)
    { double sum = 0; for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].a - b[i].a); return sum / a.Length; }
    static Rect Bounds(Color[] pixels, int width)
    {
        int left = width, right = -1, low = pixels.Length / width, high = -1;
        for (int i = 0; i < pixels.Length; i++) if (pixels[i].a > .5f)
        { left = Math.Min(left, i % width); right = Math.Max(right, i % width); low = Math.Min(low, i / width); high = Math.Max(high, i / width); }
        return Rect.MinMaxRect(left, low, right + 1, high + 1);
    }
    static TextLayerBehaviour Text() => new TextLayerBehaviour { text = "O B é\nText 123", fontFamily = FontName(),
        fontSize = 40, edgeMode = TextEdgeMode.Antialiased, color = new Color(.2f, .6f, .8f, 1) };

    public static string Layout() => TestContext.Run("Source font outlines, shared Text layout and atomic conversion", t =>
    {
        var beforeFace = FontEngine.GetFaceInfo();
        var doc = Document(); var text = Text(); var layer = new Layer(text); doc.layers.Add(layer);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            for (int scenario = 0; scenario < 9; scenario++)
            {
                text.layoutMode = scenario < 3 ? TextLayoutMode.Point : TextLayoutMode.Frame;
                text.layoutOffset = new Vector2(7.25f, -4.5f);
                text.frameSize = new Vector2(152, 106);
                text.spacing = new TextSpacing { character = .08f, word = .1f, line = .08f, paragraph = .2f };
                text.characterHorizontalScale = scenario == 2 ? 1.5f : 1;
                text.casing = scenario == 1 ? TextCasing.SmallCaps : TextCasing.Normal;
                text.overflow = scenario == 4 ? TextOverflowMode.Clip : scenario == 5 ? TextOverflowMode.Ellipsis : TextOverflowMode.None;
                text.autoSize = scenario == 6; text.maxFontSize = 55;
                text.justify = scenario == 7;
                text.fontStyle = scenario == 8 ? FontStyle.BoldAndItalic : FontStyle.Normal;
                string sourceJson = JsonUtility.ToJson(text);
                var transform = layer.transform;
                var original = Pixels(doc);
                string id = layer.Id;
                var converted = TextToVector.Convert(text, doc);
                t.Equal(sourceJson, JsonUtility.ToJson(text), "Conversion leaves source data unchanged " + scenario);
                t.True(ReferenceEquals(text, layer.Behaviour), "Conversion does not replace source before caller commits");
                t.Equal(transform, layer.transform, "Converter preserves transform");
                t.Equal(1, converted.shapes.Count, "One compound figure");
                var figure = converted.shapes[0];
                t.Equal(VectorFillRule.NonZero, figure.fillRule, "Font winding including holes");
                t.True(figure.contours.Count > 5, "Disconnected glyphs and counters retained");
                t.True(figure.contours.Exists(c => c.points.Exists(p => p.inTangent != Vector2.zero || p.outTangent != Vector2.zero)), "Original Bezier curves, not mask tracing");
                layer.SetBehaviour(converted); layer.transform.tiling = TransformTilingMode.Unbounded;
                try
                {
                    var actual = Pixels(doc);
                    var a = Bounds(original, doc.width); var b = Bounds(actual, doc.width);
                    t.True(Difference(original, actual) < .025, "Vector silhouette agrees with native unhinted raster " + scenario + ": " + Difference(original, actual));
                    t.Near(a.xMin, b.xMin, 3, "Layout left " + scenario); t.Near(a.yMin, b.yMin, 3, "Layout bottom " + scenario);
                    t.Near(a.xMax, b.xMax, 3, "Layout right " + scenario); t.Near(a.yMax, b.yMax, 3, "Layout top " + scenario);
                    t.Equal(id, layer.Id, "Replacement preserves layer identity");
                }
                finally { layer.SetBehaviour(text); layer.transform = transform; }
            }
            text.fontStyle = FontStyle.Normal; text.text = "O"; text.layoutMode = TextLayoutMode.Point;
            text.casing = TextCasing.Normal; text.fontSize = 100; text.layoutOffset = Vector2.zero; text.autoSize = false;
            var ring = TextToVector.Convert(text, doc);
            t.Equal(2, ring.shapes[0].contours.Count, "O keeps exterior and hole");
            var second = TextToVector.Convert(text, doc);
            ring.shapes[0].contours[0].points[0].position = new Vector2(12, 14);
            t.True(second.shapes[0].contours[0].points[0].position.x < 1, "Converted outputs do not share cached editable points");
            var frame = TextToVector.Convert(text, doc);
            text.layoutOffset = new Vector2(12, -9);
            var moved = TextToVector.Convert(text, doc);
            t.Near(12d / doc.width, moved.shapes[0].contours[0].points[0].position.x - frame.shapes[0].contours[0].points[0].position.x, 1e-6, "Layout offset X before transform");
            t.Near(-9d / doc.height, moved.shapes[0].contours[0].points[0].position.y - frame.shapes[0].contours[0].points[0].position.y, 1e-6, "Layout offset Y before transform");
            text.text = " \n\t";
            t.Equal(0, TextToVector.Convert(text, doc).shapes[0].contours.Count, "Whitespace has no contours");
            text.fontFamily = "WhimTex-Missing-Font-" + Guid.NewGuid().ToString("N");
            bool rejected = false; try { TextToVector.Convert(text, doc); } catch (NotSupportedException) { rejected = true; }
            t.True(rejected && ReferenceEquals(layer.Behaviour, text), "Missing font cannot partially replace Text or trace saved pixels");
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Caller render state retained");
        }
        finally
        {
            Object.DestroyImmediate(doc); RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (!string.IsNullOrEmpty(beforeFace.familyName)) FontEngine.LoadFontFace(beforeFace.familyName, beforeFace.styleName, beforeFace.pointSize);
            else FontEngine.UnloadFontFace();
        }
    });

    public static string Boolean() => TestContext.Run("Editable Text operands, Boolean composition and persistence", t =>
    {
        var beforeFace = FontEngine.GetFaceInfo(); var doc = Document();
        var text = Text(); text.text = "O B"; text.fontSize = 72;
        var operand = new Layer(text);
        operand.transform.positionF = new Vector2(10, -7); operand.transform.rotation = 12;
        var model = new BooleanGroupLayerBehaviour { referenceSize = new Vector2(doc.width, doc.height) };
        var group = new Layer(model); doc.layers.Add(group); group.children.Add(operand);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            model.Validate(); string settings = JsonUtility.ToJson(text); var transform = operand.transform;
            var live = Pixels(doc); var converted = TextToVector.Convert(text, doc);
            operand.SetBehaviour(converted);
            try { t.True(Difference(live, Pixels(doc)) < 1e-5, "Direct Text equals converted original outline in group"); }
            finally { operand.SetBehaviour(text); }
            t.Equal(settings, JsonUtility.ToJson(text), "Boolean rendering keeps Text settings");
            t.Equal(transform, operand.transform, "Boolean rendering keeps local transform");
            text.text = "TEX"; var edited = Pixels(doc);
            t.True(Difference(live, edited) > .01, "Editing live text invalidates Boolean geometry");
            text.text = "O B"; t.True(Difference(live, Pixels(doc)) < 1e-5, "Reverting text restores result");
            foreach (float pixelScale in new[] { .75f, 2f })
            {
                doc.pixelScale = pixelScale;
                var direct = Pixels(doc); var vector = TextToVector.Convert(text, doc); operand.SetBehaviour(vector);
                try { t.True(Difference(direct, Pixels(doc)) < 1e-5, "Text/Vector geometry uses document pixel scale " + pixelScale); }
                finally { operand.SetBehaviour(text); }
            }
            doc.pixelScale = 1;
            var nested = new Layer(new BooleanGroupLayerBehaviour { referenceSize = new Vector2(doc.width, doc.height) });
            group.children.Remove(operand); group.children.Add(nested); nested.children.Add(operand);
            nested.transform.scaleF = new Vector2(-.8f, .9f); nested.transform.rotation = -19;
            var nestedSource = Pixels(doc); var nestedVector = TextToVector.Convert(text, doc); operand.SetBehaviour(nestedVector);
            try { t.True(Difference(nestedSource, Pixels(doc)) < 1e-5, "Nested reflected/rotated parent preserves original glyph geometry"); }
            finally
            {
                operand.SetBehaviour(text); nested.children.Remove(operand); group.children.Remove(nested);
                group.children.Add(operand); Release(nested);
            }
            t.True(Difference(live, Pixels(doc)) < 1e-5, "Leaving nested parent restores source result");
            text.color = Color.red; operand.opacity = 0;
            t.True(Difference(live, Pixels(doc)) < 1e-5, "Group style ignores Text color/opacity");
            operand.opacity = 1;
            var basis = new Layer(new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Rectangle });
            basis.transform.scaleF = new Vector2(.9f, .8f); group.children.Add(basis);
            operand.booleanOperation = VectorBooleanOperation.Subtract;
            var subtracted = Pixels(doc);
            t.True(Difference(live, subtracted) > .1, "Text subtraction is not ordinary composition");
            foreach (VectorBooleanOperation op in Enum.GetValues(typeof(VectorBooleanOperation)))
            {
                operand.booleanOperation = op;
                var original = Pixels(doc); var vector = TextToVector.Convert(text, doc); operand.SetBehaviour(vector);
                try { t.True(Difference(original, Pixels(doc)) < 1e-5, "Text/Vector parity " + op); }
                finally { operand.SetBehaviour(text); }
            }
            group.children.Remove(basis); Release(basis);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string json = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var read = WhimTexDocumentJson.Read(json, false);
                var restored = read.Document.layers[0].children[0];
                t.True(restored.Behaviour is TextLayerBehaviour, "Text remains editable after JSON " + mode);
                t.Equal(text.text, ((TextLayerBehaviour)restored.Behaviour).text, "Saved source string");
                t.Equal(transform, restored.transform, "Saved local transform");
                t.True(Difference(Pixels(doc), Pixels(read.Document)) < .0001, "Restored font outlines " + mode);
            }
            operand.booleanOperation = VectorBooleanOperation.Combine; model.appearance = BooleanGroupAppearance.Layers;
            var combine = Pixels(doc);
            t.True(Array.Exists(combine, p => p.a > .9f && p.r > .9f && p.g < .1f), "Combine Layers retains text color");
            model.appearance = BooleanGroupAppearance.Group;
            var normal = Pixels(doc);
            group.enabled = false;
            var target = new Layer(new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = group.Id, radius = 0, strength = 0 });
            doc.layers.Insert(0, target);
            try { t.True(Difference(normal, Pixels(doc)) < .0001, "Hidden Boolean Text available to Target input"); }
            finally { doc.layers.Remove(target); Release(target); group.enabled = true; }
            var export = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdGroupContent", Hidden).Invoke(doc, new object[] { group });
            try { t.True(Difference(normal, export.GetPixels()) < .003, "Export retains text geometry"); }
            finally { Object.DestroyImmediate(export); }
            var thumbnail = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreviewFallback", Hidden).Invoke(doc, new object[] { group, 96 });
            t.True(thumbnail != null, "Boolean Text thumbnail"); RenderTexture.ReleaseTemporary(thumbnail);
            var image = doc.ComposeCanvas();
            try
            {
                Directory.CreateDirectory("Temp/WhimTex/text-outlines");
                var png = new Texture2D(doc.width, doc.height, TextureFormat.RGBA32, false, true);
                try { png.SetPixels(image.GetPixels()); png.Apply(); File.WriteAllBytes("Temp/WhimTex/text-outlines/boolean-text.png", png.EncodeToPNG()); }
                finally { Object.DestroyImmediate(png); }
            }
            finally { Object.DestroyImmediate(image); }
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Caller render state retained");
        }
        finally
        {
            Object.DestroyImmediate(doc); RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (!string.IsNullOrEmpty(beforeFace.familyName)) FontEngine.LoadFontFace(beforeFace.familyName, beforeFace.styleName, beforeFace.pointSize);
            else FontEngine.UnloadFontFace();
        }
    });
}
