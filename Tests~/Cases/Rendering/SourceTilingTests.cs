using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class SourceTilingTests
{
    const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Run() => TestContext.Run("Intrinsic Source tiling and cache independence", t =>
    {
        foreach (var behaviour in new LayerBehaviour[] { new ShapeLayerBehaviour(), new VectorLayerBehaviour(),
            new BooleanGroupLayerBehaviour(), new NoiseLayerBehaviour(), new GradientLayerBehaviour(), new ColorFillLayerBehaviour(),
            new DrawingLayerBehaviour(), new TextLayerBehaviour(), new GroupLayerBehaviour(), new BlurLayerBehaviour(), new FileLayerBehaviour() })
        {
            var layer = new Layer(behaviour);
            t.Equal(TransformTilingMode.Source, layer.transform.tiling, behaviour.GetType().Name + " stores Source");
            var expected = behaviour is FileLayerBehaviour ? TransformTilingMode.Source :
                behaviour is ShapeLayerBehaviour || behaviour is VectorLayerBehaviour || behaviour is BooleanGroupLayerBehaviour ||
                behaviour is NoiseLayerBehaviour || behaviour is GradientLayerBehaviour || behaviour is ColorFillLayerBehaviour
                    ? TransformTilingMode.Unbounded : TransformTilingMode.Clip;
            t.Equal(expected, layer.EffectiveTiling, "Intrinsic policy " + behaviour.GetType().Name);
            foreach (TransformTilingMode mode in Enum.GetValues(typeof(TransformTilingMode)))
            {
                layer.transform.tiling = mode;
                t.Equal(mode == TransformTilingMode.Source ? expected : mode, layer.EffectiveTiling, "Explicit mode has priority");
                t.Equal(mode, layer.transform.tiling, "Resolving never mutates stored mode");
            }
        }
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 64;
        var source = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var ownedLayers = new System.Collections.Generic.List<Layer>();
        Color[] Render()
        {
            var image = doc.ComposeCanvas();
            try { return image.GetPixels(); } finally { Object.DestroyImmediate(image); }
        }
        try
        {
            var drawing = new Layer(new DrawingLayerBehaviour()); doc.layers.Add(drawing);
            ownedLayers.Add(drawing);
            var paintTransform = (TextureTransform)typeof(WhimTexDocument).GetMethod("GetPaintTransform", Hidden).Invoke(doc, new object[] { drawing });
            t.Equal(TransformTilingMode.Clip, paintTransform.tiling, "Drawing paint guards receive resolved Clip");
            t.Equal(TransformTilingMode.Source, drawing.transform.tiling, "Painting does not materialize Source");
            doc.layers.Clear();
            foreach (var generator in new LayerBehaviour[] { new ShapeLayerBehaviour { feather = 12 },
                new ColorFillLayerBehaviour(), new GradientLayerBehaviour(), new NoiseLayerBehaviour() })
            {
                var layer = new Layer(generator); layer.transform.scale = Vector2.one * .5f; doc.layers.Add(layer);
                ownedLayers.Add(layer);
                var intrinsic = Render(); layer.transform.tiling = TransformTilingMode.Unbounded;
                Same(t, intrinsic, Render(), "Source matches explicit Unbounded for " + generator.GetType().Name);
                doc.layers.Clear();
            }
            var vector = new VectorLayerBehaviour { referenceSize = Vector2.one * 64, feather = 10 };
            var shape = new VectorShape(); var contour = new VectorContour();
            foreach (var point in new[] { new Vector2(.1f, .1f), new Vector2(.9f, .1f), new Vector2(.9f, .9f), new Vector2(.1f, .9f) })
                contour.points.Add(new VectorPoint(point));
            shape.contours.Add(contour); vector.shapes.Add(shape);
            var vectorLayer = new Layer(vector); vectorLayer.transform.scale = Vector2.one * .5f; doc.layers.Add(vectorLayer);
            ownedLayers.Add(vectorLayer);
            var contextType = typeof(Layer).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            var context = Activator.CreateInstance(contextType, new object[] { doc, null, 64, 64, 1f, true, true, null });
            var render = typeof(Layer).GetMethod("Render", Hidden);
            Color[] Direct()
            {
                var target = (RenderTexture)render.Invoke(vectorLayer, new[] { context });
                var image = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
                var active = RenderTexture.active;
                try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); return image.GetPixels(); }
                finally { RenderTexture.active = active; Object.DestroyImmediate(image); RenderTexture.ReleaseTemporary(target); }
            }
            var original = Direct();
            var painted = (RenderTexture)typeof(VectorLayerBehaviour).GetField("painted", Hidden).GetValue(vector);
            t.True(painted != null, "GPU paint cache exists for cache-metadata test");
            foreach (var wrap in new[] { TextureWrapMode.Repeat, TextureWrapMode.Mirror, TextureWrapMode.Clamp })
            {
                painted.wrapMode = wrap;
                Same(t, original, Direct(), "Cached paint wrap cannot change Source " + wrap);
            }
            typeof(VectorLayerBehaviour).GetMethod("ReleaseTransientResources", Hidden).Invoke(vector, null);
            Same(t, original, Direct(), "Rebuilt geometry retains Source output");
            t.Equal(TransformTilingMode.Source, vectorLayer.transform.tiling, "Warm/rebuilt rendering retains Source");
            doc.layers.Clear();
            typeof(VectorLayerBehaviour).GetMethod("ReleaseTransientResources", Hidden).Invoke(vector, null);
            var colors = new Color[16];
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) colors[y * 4 + x] = new Color(x % 2, y % 2, 0, 1);
            source.SetPixels(colors); source.Apply(false, false);
            var file = new Layer(new FileLayerBehaviour { sourceTexture = source });
            file.transform.scale = Vector2.one * .5f; doc.layers.Add(file);
            source.wrapMode = TextureWrapMode.Repeat; var repeat = Render();
            file.transform.tiling = TransformTilingMode.Repeat; Same(t, repeat, Render(), "File Source follows original Repeat");
            file.transform.tiling = TransformTilingMode.Clamp; var clamp = Render();
            file.transform.tiling = TransformTilingMode.Source;
            source.wrapModeU = TextureWrapMode.Repeat; source.wrapModeV = TextureWrapMode.Clamp;
            var mixed = Render();
            t.Near(repeat[32 * 64 + 8].r, mixed[32 * 64 + 8].r, .0001, "File honors source U wrap independently");
            t.Near(clamp[8 * 64 + 32].g, mixed[8 * 64 + 32].g, .0001, "File honors source V wrap independently");
            t.True(Math.Abs(repeat[8 * 64 + 32].g - clamp[8 * 64 + 32].g) > .5f, "Wrap comparison actually distinguishes modes");
            source.wrapModeV = TextureWrapMode.Repeat;
            Same(t, repeat, Render(), "Changing intrinsic wraps invalidates reusable source output");
            t.Equal(TransformTilingMode.Source, file.transform.tiling, "File still stores Source");
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Caller render state preserved");
        }
        finally
        {
            foreach (var layer in ownedLayers) typeof(Layer).GetMethod("ReleaseTransientResources", Hidden).Invoke(layer, null);
            Object.DestroyImmediate(doc); Object.DestroyImmediate(source); RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    });

    static void Same(TestContext t, Color[] expected, Color[] actual, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " dimensions"); float error = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            var a = expected[i]; var b = actual[i];
            error = Mathf.Max(error, Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));
        }
        t.Near(0, error, .0001, label);
    }
}
