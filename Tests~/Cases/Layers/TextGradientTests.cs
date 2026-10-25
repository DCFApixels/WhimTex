using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class TextGradientTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Product = typeof(Layer).Assembly;
    static object Call(object instance, string method, params object[] args) =>
        Array.Find((instance as Type ?? instance.GetType()).GetMethods(F), m => m.Name == method && m.GetParameters().Length == args.Length)
            .Invoke(instance is Type ? null : instance, args);
    static object Read(object instance, string name) => instance.GetType().GetField(name, F).GetValue(instance);
    static object Json(string text) => Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true)
        .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
    static Color[] Pixels(WhimTexDocument doc)
    {
        var image = doc.ComposeCanvas();
        try { return image.GetPixels(); } finally { Object.DestroyImmediate(image); }
    }
    public static string Run() => TestContext.Run("Text/Character gradient paint, coverage, handles and persistence", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 192; doc.height = 128;
        var text = new TextLayerBehaviour { text = "HH", fontSize = 54, spacing = new TextSpacing { character = .2f } };
        var layer = new Layer(text); doc.layers.Add(layer);
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var gradient = new WhimTexGradient { ColorMode = WhimTexGradientColorMode.Linear, ColorSpace = ColorSpace.Linear };
        gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.blue, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        var previews = new List<Color[]>();
        try
        {
            t.True((bool)typeof(TextLayerBehaviour).GetProperty("FontAvailable", F).GetValue(text), "Installed default font is available");
            foreach (TextEdgeMode edges in Enum.GetValues(typeof(TextEdgeMode)))
            {
                text.edgeMode = edges; text.fillMode = VectorFillMode.Solid; text.color = Color.white;
                var baseline = Pixels(doc);
                int ink = 0; foreach (var c in baseline) if (c.a > .9f) ink++;
                t.True(ink > 100, "Glyphs actually render in " + edges);
                text.gradient = gradient.Clone();
                foreach (TextGradientScope scope in Enum.GetValues(typeof(TextGradientScope)))
                foreach (var mode in new[] { VectorFillMode.LinearGradient, VectorFillMode.RadialGradient, VectorFillMode.SquareGradient,
                    VectorFillMode.AngularGradient })
                {
                    text.fillMode = mode; text.gradientScope = scope;
                    text.gradientStart = new Vector2(.2f, .3f); text.gradientEnd = new Vector2(.8f, .7f);
                    var painted = Pixels(doc); float maxAlpha = 0, maxColor = 0; int checkedPixels = 0;
                    var frames = scope == TextGradientScope.Text
                        ? new[] { (Rect)Call(text, "GetGradientFrame", 192, 128, 0) }
                        : new[] { (Rect)Call(text, "GetGradientFrame", 192, 128, 0), (Rect)Call(text, "GetGradientFrame", 192, 128, 1) };
                    for (int i = 0; i < painted.Length; i++)
                    {
                        maxAlpha = Mathf.Max(maxAlpha, Mathf.Abs(painted[i].a - baseline[i].a));
                        if (baseline[i].a < .999f) continue;
                        var local = new Vector2(i % 192 + .5f - 96, i / 192 + .5f - 64);
                        int character = scope == TextGradientScope.Text ? 0 : frames[1].Contains(local) ? 1 : 0;
                        Rect frame = frames[character];
                        if (!frame.Contains(local)) continue;
                        float time = Coordinate(mode, local, frame, text.gradientStart, text.gradientEnd);
                        if (mode == VectorFillMode.AngularGradient && Mathf.Min(time, 1 - time) < .00005f) continue;
                        var expected = gradient.Evaluate(time);
                        maxColor = Mathf.Max(maxColor, Mathf.Abs(expected.r - painted[i].r), Mathf.Abs(expected.b - painted[i].b));
                        checkedPixels++;
                    }
                    t.True(checkedPixels > 100, "Color coordinates tested " + scope + "/" + mode + "/" + edges);
                    t.Near(0, maxAlpha, edges == TextEdgeMode.Pixel ? .00001 : .0041, "Opaque gradient preserves coverage " + scope + "/" + mode + "/" + edges);
                    t.Near(0, maxColor, .006, "Gradient follows its local frame " + scope + "/" + mode + "/" + edges);
                    if (edges == TextEdgeMode.HintedAA && mode == VectorFillMode.LinearGradient) previews.Add(painted);
                }
            }
            text.text = "Текст АБ\nText 123"; text.fontSize = 24; text.fontStyle = FontStyle.BoldAndItalic;
            text.layoutMode = TextLayoutMode.Frame; text.frameSize = new Vector2(95, 35); text.overflow = TextOverflowMode.Clip;
            foreach (TextEdgeMode edges in Enum.GetValues(typeof(TextEdgeMode)))
            {
                text.edgeMode = edges; text.fillMode = VectorFillMode.Solid; var reference = Pixels(doc);
                foreach (TextGradientScope scope in Enum.GetValues(typeof(TextGradientScope)))
                {
                    text.fillMode = VectorFillMode.LinearGradient; text.gradientScope = scope; var painted = Pixels(doc);
                    float error = 0;
                    for (int i = 0; i < painted.Length; i++) error = Mathf.Max(error, Mathf.Abs(reference[i].a - painted[i].a));
                    t.Near(0, error, .00001, "Frame clipping and styled multiline Cyrillic keep coverage " + scope + "/" + edges);
                }
            }
            text.text = "HH"; text.fontSize = 54; text.fontStyle = FontStyle.Normal;
            text.layoutMode = TextLayoutMode.Point; text.overflow = TextOverflowMode.None;
            text.edgeMode = TextEdgeMode.HintedAA; text.fillMode = VectorFillMode.LinearGradient;
            text.gradientStart = Vector2.zero; text.gradientEnd = Vector2.right;
            text.gradientScope = TextGradientScope.Character; Pixels(doc);
            var renderer = Read(text, "renderer"); int builds = (int)renderer.GetType().GetProperty("MaskBuildCount", F).GetValue(renderer);
            text.gradientStart = new Vector2(.1f, .1f); text.gradient = gradient.Clone(); text.gradientRepetitions = 2; Pixels(doc);
            t.Equal(builds, (int)renderer.GetType().GetProperty("MaskBuildCount", F).GetValue(renderer), "Paint edits do not rebuild glyph masks");
            Call(renderer, "GetMask", text, 192, 128, .5f);
            int reducedBuilds = (int)renderer.GetType().GetProperty("MaskBuildCount", F).GetValue(renderer);
            Call(text, "GetGradientFrame", 192, 128, 0);
            t.Equal(reducedBuilds, (int)renderer.GetType().GetProperty("MaskBuildCount", F).GetValue(renderer), "Reading handles does not force full-resolution mask rebuilding");
            Call(renderer, "GetMask", text, 192, 128, 1f);

            var targetType = Product.GetType("DCFApixels.WhimTex.GradientCanvasTarget", true);
            var target = Activator.CreateInstance(targetType, F, null, new object[] { layer, new Vector2(192, 128), false, 1 }, null);
            var transform = (TextureTransform)targetType.GetProperty("CanvasTransform", F).GetValue(target);
            var oldTransform = layer.transform; var start = text.gradientStart; var end = text.gradientEnd;
            Rect second = (Rect)Call(text, "GetGradientFrame", 192, 128, 1);
            var mapped = transform.Map(start, new Vector2(192, 128));
            t.Near(.5 + (second.x + start.x * second.width) / 192, mapped.x, .00001, "Handles use the selected character frame");
            Call(target, "MoveEndpoint", doc, new Vector2(192, 128), Vector2.zero, Vector2.zero, true,
                new Vector2(3, 0), transform, start, end);
            t.Near(start.x + 3 / second.width, text.gradientStart.x, .00001, "Dragging handle edits shared relative anchors");
            t.Equal(oldTransform, layer.transform, "Gradient handles leave layer transform unchanged");
            text.gradientStart = Vector2.zero; text.gradientEnd = Vector2.right;
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                using var reopened = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json, false);
                var copy = (TextLayerBehaviour)reopened.Document.layers[0].Behaviour;
                t.Equal(text.gradientScope, copy.gradientScope, "JSON retains scope " + mode);
                t.Equal(text.fillMode, copy.fillMode, "JSON retains fill " + mode);
                Same(t, Pixels(doc), Pixels(reopened.Document), .00001f, "JSON retains painted output " + mode);
            }
            Call(typeof(WhimTexApi), "SetText", text, Json("{\"gradientScope\":\"Text\",\"fillMode\":\"RadialGradient\",\"gradientStart\":[0.5,0.5],\"gradientEnd\":[1,0.5]}"));
            t.Equal(TextGradientScope.Text, text.gradientScope, "API accepts Text scope");
            t.Equal(VectorFillMode.RadialGradient, text.fillMode, "API accepts common fill mode");
            var targetPixels = Pixels(doc);
            var group = new Layer(new GroupLayerBehaviour()); doc.layers[0] = group; group.children.Add(layer);
            Same(t, targetPixels, Pixels(doc), .00001f, "Group composition preserves gradient text");
            doc.layers[0] = layer;
            var consumer = new Layer(new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = layer.Id, radius = 0, strength = 0 });
            doc.layers.Insert(0, consumer); layer.enabled = false;
            Same(t, targetPixels, Pixels(doc), .00001f, "Target input preserves gradient text");
            Call(consumer, "ReleaseTransientResources"); doc.layers.Remove(consumer); layer.enabled = true;
            var export = (Texture2D)Call(doc, "RenderPsdPixels", layer, false);
            try
            {
                var encoded = new Color[targetPixels.Length];
                for (int i = 0; i < encoded.Length; i++) { encoded[i] = targetPixels[i].gamma; encoded[i].a = targetPixels[i].a; }
                var exported = export.GetPixels();
                for (int i = 0; i < encoded.Length; i++)
                {
                    encoded[i].r *= encoded[i].a; encoded[i].g *= encoded[i].a; encoded[i].b *= encoded[i].a;
                    exported[i].r *= exported[i].a; exported[i].g *= exported[i].a; exported[i].b *= exported[i].a;
                }
                Same(t, encoded, exported, .004f, "Encoded export preserves visible gradient paint");
            }
            finally { Object.DestroyImmediate(export); }
            t.True(Call(doc, "GetLayerThumbnail", layer, 64, false) != null, "Gradient text thumbnail is available");

            layer.colorRange = LayerColorRange.HDR; layer.blendRange = LayerBlendRange.HDR;
            doc.outputPrecision = WhimTexOutputPrecision.Float32;
            text.gradient.SetKeys(new[] { new GradientColorKey(new Color(1.5f, .1f, -.3f), 0), new GradientColorKey(new Color(3, .3f, 1.8f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            bool hdr = false; foreach (var c in Pixels(doc)) hdr |= c.a > .9f && c.r > 1;
            t.True(hdr, "HDR fallback test actually renders values above one");
            foreach (TextGradientScope scope in Enum.GetValues(typeof(TextGradientScope)))
            {
                text.gradientScope = scope; var before = Pixels(doc); Call(text, "PrepareSavedAppearance", 192, 128);
                var catalog = Product.GetType("DCFApixels.WhimTex.SystemFontCatalog", true);
                var available = catalog.GetField("available", F); var saved = available.GetValue(null);
                try
                {
                    available.SetValue(null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    Same(t, before, Pixels(doc), .0001f, "Missing-font snapshot preserves " + scope + " paint");
                }
                finally { available.SetValue(null, saved); }
            }
            var nativeType = Product.GetType("DCFApixels.WhimTex.WhimTexDocumentContainer", true);
            using (var container = (IDisposable)Activator.CreateInstance(nativeType, true))
            {
                var serializer = Product.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
                var bytes = (byte[])Call(serializer, "Serialize", doc, container);
                var result = Call(serializer, "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
                var copy = (WhimTexDocument)result.GetType().GetProperty("Model", F).GetValue(result);
                try { Same(t, Pixels(doc), Pixels(copy), .00001f, "Native model roundtrip retains Character fill"); }
                finally { Object.DestroyImmediate(copy); }
            }
            var undoStart = text.gradientStart;
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc, "Owned Text Gradient test");
            text.gradientScope = TextGradientScope.Text; text.gradientStart = new Vector2(.35f, .2f);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
            text = (TextLayerBehaviour)doc.layers[0].Behaviour;
            t.Equal(TextGradientScope.Character, text.gradientScope, "Undo restores Character scope");
            t.Equal(undoStart, text.gradientStart, "Undo restores shared anchor");
            Undo.PerformRedo(); text = (TextLayerBehaviour)doc.layers[0].Behaviour;
            t.Equal(TextGradientScope.Text, text.gradientScope, "Redo restores Text scope");
            t.Equal(new Vector2(.35f, .2f), text.gradientStart, "Redo restores shared anchor");
            Undo.ClearUndo(doc); Undo.IncrementCurrentGroup();
            text.fillMode = VectorFillMode.None;
            foreach (var pixel in Pixels(doc)) t.Near(0, pixel.a, 0, "None has no ink");
            SavePreview(previews);
            t.True(RenderTexture.active == active && GL.sRGBWrite == srgb, "Render state preserved");
        }
        finally { Undo.ClearUndo(doc); Object.DestroyImmediate(doc); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });

    static float Coordinate(VectorFillMode mode, Vector2 point, Rect frame, Vector2 start, Vector2 end)
    {
        var axis = Vector2.Scale(end - start, frame.size); var d = point - frame.min - Vector2.Scale(start, frame.size);
        float squared = axis.sqrMagnitude, along = Vector2.Dot(d, axis) / squared, across = (d.y * axis.x - d.x * axis.y) / squared;
        if (mode == VectorFillMode.LinearGradient) return along;
        if (mode == VectorFillMode.RadialGradient) return d.magnitude / Mathf.Sqrt(squared);
        if (mode == VectorFillMode.SquareGradient) return Mathf.Max(Mathf.Abs(along), Mathf.Abs(across));
        return Mathf.Repeat(Mathf.Atan2(across, along) / (2 * Mathf.PI), 1);
    }
    static void Same(TestContext t, Color[] a, Color[] b, float tolerance, string message)
    {
        t.Equal(a.Length, b.Length, message + " dimensions"); float delta = 0;
        for (int i = 0; i < a.Length; i++) for (int k = 0; k < 4; k++) delta = Mathf.Max(delta, Mathf.Abs(a[i][k] - b[i][k]));
        t.Near(0, delta, tolerance, message);
    }
    static void SavePreview(List<Color[]> rows)
    {
        var image = new Texture2D(192, 128 * rows.Count, TextureFormat.RGBA32, false, true);
        try
        {
            for (int row = 0; row < rows.Count; row++)
            {
                var output = new Color[192 * 128];
                for (int i = 0; i < output.Length; i++) output[i] = Color.Lerp(new Color(.12f, .12f, .12f), rows[row][i], rows[row][i].a);
                image.SetPixels(0, (rows.Count - row - 1) * 128, 192, 128, output);
            }
            image.Apply(); Directory.CreateDirectory("Temp/WhimTex"); File.WriteAllBytes("Temp/WhimTex/text-gradient-preview-" + System.Guid.NewGuid().ToString("N") + ".png", image.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(image); }
    }
}
