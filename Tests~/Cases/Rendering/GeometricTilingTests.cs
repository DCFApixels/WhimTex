using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class GeometricTilingTests
{
    const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Run() => TestContext.Run("Geometric tiling defaults and explicit Clip", t =>
    {
        t.Equal(TransformTilingMode.Source, new Layer(new ShapeLayerBehaviour()).transform.tiling, "New Shape stores Source");
        t.Equal(TransformTilingMode.Source, new Layer(new VectorLayerBehaviour()).transform.tiling, "New Vector stores Source");
        t.Equal(TransformTilingMode.Source, new Layer(new BlurLayerBehaviour()).transform.tiling, "Other new layers store Source");
        foreach (LayerBehaviour behaviour in new LayerBehaviour[] { new ShapeLayerBehaviour(), new VectorLayerBehaviour(), new BlurLayerBehaviour() })
        {
            var layer = new Layer(behaviour); layer.transform.tiling = TransformTilingMode.Repeat;
            var value = layer.transform; value.Reset();
            t.Equal(TransformTilingMode.Source, value.tiling, "Reset restores Source");
            t.Equal(TransformTilingMode.Repeat, layer.transform.tiling, "Reading Reset defaults does not mutate the layer");
        }
        var drag = typeof(WhimTexWindow).GetMethod("ShapeDragTransform", Hidden);
        foreach (ShapeLayerBehaviour.ShapeKind kind in Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind)))
        {
            var placement = (TextureTransform)drag.Invoke(null, new object[] { new Vector2(20, 30), new Vector2(80, 70), new Vector2(128, 96), kind, false, 8f });
            t.Equal(TransformTilingMode.Source, placement.tiling, "Shape tool stores Source for " + kind);
        }
        foreach (string type in new[] { "ShapeLayerBehaviour", "VectorLayerBehaviour", "ColorFillLayerBehaviour" })
        foreach (string transform in new[] { "", ",\"transform\":{\"position\":{\"x\":3,\"y\":4}}", ",\"transform\":{\"tiling\":\"Clip\"}" })
        {
            using var read = WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"figure\",\"behaviour\":{\"$type\":\"" + type + "\"}" + transform + "}]}", false);
            var layer = read.Document.layers[0];
            var expected = transform.Contains("Clip") ? TransformTilingMode.Clip : TransformTilingMode.Source;
            t.Equal(expected, layer.transform.tiling, type + " omitted/partial/explicit JSON transform");
            foreach (TransformTilingMode mode in Enum.GetValues(typeof(TransformTilingMode)))
            {
                layer.transform = TextureTransform.Default; layer.transform.tiling = mode;
                foreach (WhimTexJsonWriteMode writing in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                {
                    using var restored = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(read.Document, new WhimTexJsonWriteOptions { Mode = writing }).Json, false);
                    t.Equal(mode, restored.Document.layers[0].transform.tiling, type + " retains " + mode + " through " + writing);
                }
            }
        }
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            doc.width = 128; doc.height = 96;
            var aliases = new Dictionary<string, Layer>();
            var jsonType = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.Linq.JObject", true);
            var parse = jsonType.GetMethod("Parse", new[] { typeof(string) });
            var apply = typeof(WhimTexApi).GetMethod("ApplyOperation", Hidden);
            void Operation(string json) => apply.Invoke(null, new object[] { doc, parse.Invoke(null, new object[] { json }), aliases, false, null, null });
            foreach (string type in new[] { "shape", "vector", "blur" })
            {
                Operation("{\"op\":\"add\",\"type\":\"" + type + "\",\"as\":\"" + type + "\"}");
                t.Equal(TransformTilingMode.Source, aliases[type].transform.tiling, "API add stores Source for " + type);
            }
            Operation("{\"op\":\"add\",\"type\":\"shape\",\"as\":\"clipped\",\"transform\":{\"tiling\":\"Clip\"}}");
            t.Equal(TransformTilingMode.Clip, aliases["clipped"].transform.tiling, "API retains explicit Clip");
            doc.layers.Clear(); aliases.Clear();
            var shape = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Rectangle, feather = 16 };
            var source = new Layer(shape); doc.layers.Add(source);
            Color[] Render()
            {
                var image = doc.ComposeCanvas();
                try { return image.GetPixels(); } finally { Object.DestroyImmediate(image); }
            }
            float Alpha(Color[] pixels, int x, int y = 48) => pixels[y * 128 + x].a;
            var unbounded = Render(); t.True(Alpha(unbounded, 28) > .01f, "Unbounded retains external Feather");
            source.transform.tiling = TransformTilingMode.Clip; var clipped = Render();
            t.Near(0, Alpha(clipped, 28), 0, "Clip removes external Feather");
            t.Near(Alpha(unbounded, 48), Alpha(clipped, 48), .0001, "Clip retains Feather inside frame");
            SavePreview(unbounded, clipped);
            shape.feather = 0; shape.stroke = true; shape.strokePosition = ShapeLayerBehaviour.StrokePosition.Outside; shape.strokeWidth = 12;
            t.Near(0, Alpha(Render(), 28), 0, "Clip removes external stroke");
            source.transform.tiling = TransformTilingMode.Unbounded; t.True(Alpha(Render(), 28) > .99f, "Unbounded retains external stroke");
            shape.stroke = false; shape.kind = ShapeLayerBehaviour.ShapeKind.Line; source.transform.scale = new Vector2(.5f, .125f);
            foreach (var cap in new[] { ShapeLayerBehaviour.LineCap.Round, ShapeLayerBehaviour.LineCap.Square })
            {
                shape.lineCap = cap; source.transform.tiling = TransformTilingMode.Unbounded;
                t.True(Alpha(Render(), 28) > .99f, "Unbounded retains body cap " + cap);
                source.transform.tiling = TransformTilingMode.Clip; t.Near(0, Alpha(Render(), 28), 0, "Clip cuts body cap " + cap);
            }
            shape.kind = ShapeLayerBehaviour.ShapeKind.Rectangle; source.transform.scale = new Vector2(.5f, .5f);
            source.transform.rotation = 27; shape.feather = 12;
            foreach (var edge in new[] { ShapeLayerBehaviour.EdgeMode.Antialiased, ShapeLayerBehaviour.EdgeMode.Step })
            {
                shape.edgeMode = edge; var pixels = Render(); source.transform.ToMatrix(128, 96).TryInverse(out var inverse);
                int outside = 0;
                for (int y = 0; y < 96; y++) for (int x = 0; x < 128; x++)
                {
                    var uv = inverse.Point(new Double2((x + .5) / 128, (y + .5) / 96));
                    if (uv.x < -.001 || uv.y < -.001 || uv.x > 1.001 || uv.y > 1.001)
                    { t.Near(0, Alpha(pixels, x, y), 0, "Rotated Clip excludes outside samples"); outside++; }
                }
                t.True(outside > 1000, "Exterior was checked for " + edge);
            }
            source.transform.rotation = 0; shape.edgeMode = ShapeLayerBehaviour.EdgeMode.Antialiased;
            var group = new Layer(new GroupLayerBehaviour()); group.children.Add(source); doc.layers[0] = group;
            t.Near(0, Alpha(Render(), 28), 0, "Group retains Clip"); doc.layers[0] = source;
            var target = new Layer(new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = source.Id, radius = 0, strength = 0 });
            doc.layers.Insert(0, target); source.enabled = false;
            t.Near(0, Alpha(Render(), 28), 0, "Target retains Clip"); doc.layers.Remove(target); source.enabled = true;
            t.True(shape.GetPreviewTexture(32)?.width == 32, "Shape thumbnail available");
            var export = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdPixels", Hidden).Invoke(doc, new object[] { source, false });
            try { t.Near(0, export.GetPixel(28, 48).a, 0, "Export retains Clip"); } finally { Object.DestroyImmediate(export); }
            shape.feather = 0; aliases.Add("source", source); Operation("{\"op\":\"convertToVector\",\"layer\":\"@source\"}");
            t.True(source.Behaviour is VectorLayerBehaviour, "Shape converted to Vector");
            t.Equal(TransformTilingMode.Clip, source.transform.tiling, "Conversion retains explicit Clip");
            t.Near(0, Alpha(Render(), 28), 0, "Converted silhouette retains crop");
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Caller render state preserved");
        }
        finally { Object.DestroyImmediate(doc); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });

    static void SavePreview(Color[] left, Color[] right)
    {
        var image = new Texture2D(256, 96, TextureFormat.RGBA32, false, true);
        try
        {
            for (int side = 0; side < 2; side++)
            {
                var source = side == 0 ? left : right; var display = new Color[source.Length];
                for (int p = 0; p < source.Length; p++)
                {
                    float checker = ((p % 128 / 8 + p / 128 / 8) & 1) == 0 ? .16f : .25f;
                    display[p] = Color.Lerp(new Color(checker, checker, checker), Color.white, source[p].a); display[p].a = 1;
                }
                image.SetPixels(side * 128, 0, 128, 96, display);
            }
            image.Apply(); Directory.CreateDirectory("Temp/WhimTex"); File.WriteAllBytes("Temp/WhimTex/shape-tiling-preview-" + System.Guid.NewGuid().ToString("N") + ".png", image.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(image); }
    }
}
