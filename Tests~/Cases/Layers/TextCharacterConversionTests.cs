using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class TextCharacterConversionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Product = typeof(Layer).Assembly;
    static object Call(object instance, string name, params object[] args) =>
        Array.Find((instance as Type ?? instance.GetType()).GetMethods(F), m => m.Name == name && m.GetParameters().Length == args.Length)
            .Invoke(instance is Type ? null : instance, args);
    static object Json(string text) => Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true)
        .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
    static void Convert(WhimTexDocument doc, Layer layer) => Call(typeof(WhimTexApi), "ApplyOperation", doc,
        Json("{\"op\":\"convertToVector\",\"layer\":\"" + layer.Id + "\"}"), new Dictionary<string, Layer>(), false, null, null);
    static Color[] Pixels(WhimTexDocument doc)
    {
        var image = doc.ComposeCanvas(); try { return image.GetPixels(); } finally { Object.DestroyImmediate(image); }
    }
    static void Same(TestContext t, Color[] expected, Color[] actual, float tolerance, string label)
    {
        float delta = 0;
        for (int i = 0; i < expected.Length; i++) for (int k = 0; k < 4; k++) delta = Mathf.Max(delta, Mathf.Abs(expected[i][k] - actual[i][k]));
        t.Near(0, delta, tolerance, label);
    }
    static double VisibleError(Color[] a, Color[] b)
    {
        double error = 0;
        for (int i = 0; i < a.Length; i++)
        { error += Math.Abs(a[i].a - b[i].a); for (int k = 0; k < 3; k++) error += Math.Abs(a[i][k] * a[i].a - b[i][k] * b[i].a); }
        return error / (a.Length * 4);
    }
    static WhimTexDocument Document()
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 192; doc.height = 128; doc.outputPrecision = WhimTexOutputPrecision.Float32; return doc;
    }
    static TextLayerBehaviour Source() => new TextLayerBehaviour
    {
        text = "HO", fontFamily = Family(), fontSize = 54, edgeMode = TextEdgeMode.Antialiased,
        gradientScope = TextGradientScope.Character, fillMode = VectorFillMode.LinearGradient,
        gradientStart = new Vector2(.15f, .2f), gradientEnd = new Vector2(.8f, .7f),
        spacing = new TextSpacing { character = .12f }
    };
    static string Family()
    {
        var names = Font.GetOSInstalledFontNames();
        foreach (var preferred in new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica" })
            if (Array.Exists(names, n => n == preferred)) return preferred;
        throw new NotSupportedException("A supported installed static outline font is required.");
    }
    public static string Run() => TestContext.Run("Character gradient conversion, independent glyph layers and shared FX", t =>
    {
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var sheet = new Texture2D(384, 640, TextureFormat.RGBA32, false, true);
        try
        {
            int row = 0;
            foreach (var mode in new[] { VectorFillMode.LinearGradient, VectorFillMode.RadialGradient,
                VectorFillMode.SquareGradient, VectorFillMode.DiamondGradient, VectorFillMode.CircularGradient })
            {
                var doc = Document(); var source = Source(); source.fillMode = mode;
                var fx = ScriptableObject.CreateInstance<ShaderFX>(); fx.hideFlags = HideFlags.HideAndDontSave;
                typeof(ShaderFX).GetField("code", F).SetValue(fx, "float4 ApplyFX(float2 uv, float4 color) { float2 p = LayerToLocal(uv); return color * float4(.3 + .6 * saturate(p.x), .3 + .6 * saturate(p.y), .6, .75); }");
                try
                {
                    t.True((bool)Call(fx, "ApplyCore", false), "Owned tint FX compiles");
                    source.gradient.ColorSpace = ColorSpace.Linear;
                    source.gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.blue, 1) },
                        new[] { new GradientAlphaKey(.65f, 0), new GradientAlphaKey(1, 1) });
                    var layer = new Layer(source) { layerName = "Character source", colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                    layer.transform.scaleF = new Vector2(.85f, 1.1f); layer.transform.rotation = 11;
                    layer.transform.positionF = new Vector2(4, -3); layer.fx.Add(fx); doc.layers.Add(layer);
                    var original = Pixels(doc); string id = layer.Id; var transform = layer.transform; string data = JsonUtility.ToJson(source);
                    var raw = TextToVector.Convert(source, doc);
                    t.Equal(2, raw.shapes.Count, "One independently styled figure per visible glyph");
                    t.Equal(data, JsonUtility.ToJson(source), "Detached conversion does not change source data");
                    var staged = (Layer)Call(typeof(TextToVector), "PrepareLayer", source, doc);
                    t.True(ReferenceEquals(source, layer.Behaviour), "Preparation is nonmutating");
                    t.True(staged.Behaviour is GroupLayerBehaviour, "Ordinary isolated group prepared");
                    for (int i = 0; i < raw.shapes.Count; i++)
                    {
                        var child = staged.children[raw.shapes.Count - 1 - i]; var vector = (VectorLayerBehaviour)child.Behaviour; var figure = vector.shapes[0];
                        var world = staged.transform.ToMatrix(192, 128) * child.transform.ToMatrix(192, 128);
                        foreach (bool end in new[] { false, true })
                        {
                            var a = transform.Map(end ? raw.shapes[i].gradientEnd : raw.shapes[i].gradientStart, new Vector2(192, 128));
                            var b = world.Point(end ? figure.gradientEnd : figure.gradientStart);
                            t.Near(a.x, b.x, .000002, "Gradient X survives frame fitting"); t.Near(a.y, b.y, .000002, "Gradient Y survives frame fitting");
                        }
                        t.True(!ReferenceEquals(source.gradient, figure.gradient) && source.gradient.Equals(figure.gradient), "Independent equal glyph palette");
                        t.Equal(0, child.fx.Count, "FX not duplicated onto letters");
                    }
                    Call(staged, "ReleaseTransientResources");
                    Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc, "Owned Character conversion");
                    Convert(doc, layer); Call(doc, "NormalizeModel");
                    t.True(layer.Behaviour is GroupLayerBehaviour && layer.compositing == GroupCompositing.Isolated, "API creates isolated group");
                    t.Equal(id, layer.Id, "Group retains source ID"); t.Equal(transform, layer.transform, "Group retains exact transform");
                    t.True(ReferenceEquals(fx, layer.fx[0]) && layer.fx.Count == 1, "Group retains original FX object once");
                    var converted = Pixels(doc);
                    t.True(VisibleError(original, converted) < .025, "Original outlines preserve layout and paint, allowing raster AA differences " + mode);
                    float colorError = 0; int interior = 0;
                    for (int p = 0; p < original.Length; p++)
                        if (original[p].a > .6f && converted[p].a > .6f && Mathf.Abs(original[p].a - converted[p].a) < .005f)
                        {
                            interior++;
                            for (int k = 0; k < 3; k++) colorError = Mathf.Max(colorError, Mathf.Abs(original[p][k] - converted[p][k]));
                        }
                    t.True(interior > 50, "Stable interiors available for paint/FX comparison");
                    t.Near(0, colorError, .015, "Local-space FX and Character color retained away from coverage edges");
                    AddPreview(sheet, original, converted, row++);
                    var moved = layer.children[0]; var beforeMove = moved.transform;
                    t.True(moved.transform.TrySetMatrix(ProjectiveMatrix.Translate(6d / 192, 0) * beforeMove.ToMatrix(192, 128)),
                        "Translate fitted letter matrix");
                    t.True(VisibleError(converted, Pixels(doc)) > .0001, "A converted letter moves independently");
                    moved.transform = beforeMove;
                    Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                    layer = doc.layers[0]; t.True(layer.Behaviour is TextLayerBehaviour, "Undo restores editable Text");
                    t.Equal(transform, layer.transform, "Undo restores transform"); t.True(ReferenceEquals(fx, layer.fx[0]), "Undo retains FX identity");
                    Same(t, original, Pixels(doc), .00001f, "Undo restores appearance");
                    Undo.PerformRedo(); layer = doc.layers[0];
                    t.Equal(2, layer.children.Count, "Redo restores glyph layers");
                    t.True(ReferenceEquals(fx, layer.fx[0]), "Redo retains FX identity"); Same(t, converted, Pixels(doc), .00001f, "Redo restores appearance");
                    Undo.ClearUndo(doc); Undo.IncrementCurrentGroup();
                    if (mode == VectorFillMode.LinearGradient)
                    {
                        foreach (WhimTexJsonWriteMode writing in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                        {
                            using var reopened = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = writing }).Json);
                            t.Equal(2, reopened.Document.layers[0].children.Count, "JSON retains letter layers " + writing);
                            Same(t, converted, Pixels(reopened.Document), .0001f, "JSON retains converted paint and group FX " + writing);
                        }
                        var containerType = Product.GetType("DCFApixels.WhimTex.WhimTexDocumentContainer", true);
                        using var container = (IDisposable)Activator.CreateInstance(containerType, true);
                        var serializer = Product.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
                        var bytes = (byte[])Call(serializer, "Serialize", doc, container);
                        var result = Call(serializer, "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
                        var restored = (WhimTexDocument)result.GetType().GetProperty("Model", F).GetValue(result);
                        var restoredFx = restored.layers[0].fx[0];
                        try
                        {
                            t.True((bool)Call(restoredFx, "ApplyCore", false), "Deserialized owned FX compiles");
                            Same(t, converted, Pixels(restored), .0001f, "Native model retains glyph layers and common FX");
                        }
                        finally { Object.DestroyImmediate(restored); if (restoredFx != null && restoredFx != fx) Object.DestroyImmediate(restoredFx); }
                    }
                }
                finally { Undo.ClearUndo(doc); Object.DestroyImmediate(doc); if (fx != null) Object.DestroyImmediate(fx); }
            }
            EdgeCases(t);
            sheet.Apply(); Directory.CreateDirectory("Temp/WhimTex"); File.WriteAllBytes("Temp/WhimTex/text-character-conversion.png", sheet.EncodeToPNG());
            t.True(RenderTexture.active == active && GL.sRGBWrite == srgb, "Caller render state preserved");
        }
        finally { Object.DestroyImmediate(sheet); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });

    static void EdgeCases(TestContext t)
    {
        foreach (string content in new[] { "H", " \n\t", "OO O\nOO O" })
        {
            var doc = Document(); var source = Source(); source.text = content;
            var layer = new Layer(source); doc.layers.Add(layer);
            try
            {
                source.layoutMode = TextLayoutMode.Frame; source.frameSize = new Vector2(82, 39); source.overflow = TextOverflowMode.Clip;
                var before = Pixels(doc); Convert(doc, layer); var after = Pixels(doc);
                t.True(layer.Behaviour is GroupLayerBehaviour, "Single/empty/clipped Character still has a common group");
                t.True(VisibleError(before, after) < .025, "Clipped letter geometry retains appearance");
                if (content.Trim().Length == 0) t.Equal(0, layer.children.Count, "Whitespace creates no empty letter layers");
            }
            finally { Object.DestroyImmediate(doc); }
        }
        var nested = Document(); var text = Source(); var operand = new Layer(text);
        var outer = new Layer(new BooleanGroupLayerBehaviour { referenceSize = new Vector2(192, 128) }); outer.children.Add(operand); nested.layers.Add(outer);
        try
        {
            var before = Pixels(nested); Convert(nested, operand); Call(nested, "NormalizeModel");
            t.True(operand.Behaviour is BooleanGroupLayerBehaviour b && b.appearance == BooleanGroupAppearance.Layers,
                "Nested conversion remains a valid Boolean operand with retained letter styles");
            t.True(VisibleError(before, Pixels(nested)) < .0001, "Outer Boolean result retains geometry");
        }
        finally { Object.DestroyImmediate(nested); }
        var limited = Document(); var limitedText = Source(); var limitedLayer = new Layer(limitedText); limited.layers.Add(limitedLayer);
        try
        {
            for (int i = 0; i < 1022; i++) limited.layers.Add(new Layer(new ColorFillLayerBehaviour()));
            bool rejected = false; try { Convert(limited, limitedLayer); } catch (TargetInvocationException) { rejected = true; }
            t.True(rejected && ReferenceEquals(limitedText, limitedLayer.Behaviour) && (limitedLayer.children?.Count ?? 0) == 0,
                "Layer limit failure leaves source untouched");
        }
        finally { Object.DestroyImmediate(limited); }
    }
    static void AddPreview(Texture2D sheet, Color[] before, Color[] after, int row)
    {
        for (int side = 0; side < 2; side++)
        {
            var input = side == 0 ? before : after; var pixels = new Color[input.Length];
            for (int i = 0; i < input.Length; i++) pixels[i] = Color.Lerp(new Color(.12f, .12f, .12f), input[i], input[i].a);
            sheet.SetPixels(side * 192, (4 - row) * 128, 192, 128, pixels);
        }
    }
}
