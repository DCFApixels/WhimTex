using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorFeatherTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const int W = 128, H = 96;
    public static string Run() => TestContext.Run("Vector Feather geometry, AA, paint, consumers and render state", t =>
    {
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        var vector = new VectorLayerBehaviour { referenceSize = new Vector2(W, H) };
        var layer = new Layer(vector); layer.transform.tiling = TransformTilingMode.Clip;
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = W; doc.height = H; doc.layers.Add(layer);
        var shape = new VectorShape { fillColor = Color.white };
        shape.contours.Add(Rect(.25f, .25f, .75f, .75f)); vector.shapes.Add(shape);
        var previews = new List<Color[]>();
        Color[] Render()
        {
            var image = doc.ComposeCanvas();
            try { return image.GetPixels(); }
            finally { Object.DestroyImmediate(image); }
        }
        try
        {
            Color[] sharp = Render();
            foreach (VectorFeatherPosition position in Enum.GetValues(typeof(VectorFeatherPosition)))
            {
                vector.featherPosition = position;
                Same(t, sharp, Render(), 0, "Zero Feather preserves the original path " + position);
            }
            vector.feather = 8;
            foreach (VectorFeatherPosition position in Enum.GetValues(typeof(VectorFeatherPosition)))
            {
                vector.featherPosition = position;
                var pixels = Render(); previews.Add(pixels);
                for (int x = 23; x <= 41; x++)
                {
                    double outside = position == VectorFeatherPosition.Inside ? 0 : position == VectorFeatherPosition.Outside ? 1 : .5;
                    double expected = 0;
                    for (int sample = 0; sample < 256; sample++)
                    {
                        double d = 32 - (x + (sample + .5) / 256);
                        double a = Math.Clamp((d + 8 * (1 - outside)) / 8, 0, 1);
                        expected += (1 - a * a * (3 - 2 * a)) / 256;
                    }
                    t.Near(expected, pixels[48 * W + x].a, .002, "Independent integrated straight-edge profile " + position + " at " + x);
                }
                t.Near(1, pixels[48 * W + 64].a, .0001, "Feather retains deep interior");
                t.Near(0, pixels[0].a, .0001, "Feather retains distant transparency");
            }
            vector.featherPosition = VectorFeatherPosition.Centered;
            vector.feather = .00001f;
            Same(t, sharp, Render(), .00003f, "Subpixel Feather converges continuously to original AA");
            vector.edgeMode = VectorEdgeMode.Step; vector.feather = 0; var step = Render();
            vector.feather = 20; vector.featherPosition = VectorFeatherPosition.Outside;
            Same(t, step, Render(), 0, "Step ignores retained Feather");
            vector.edgeMode = VectorEdgeMode.Antialiased; vector.feather = 8; vector.featherPosition = VectorFeatherPosition.Centered;
            shape.contours.Add(Rect(.4f, .35f, .6f, .65f));
            var hole = Render(); previews.Add(hole);
            t.Near(0, hole[48 * W + 64].a, .0001, "Compound hole remains empty away from its boundary");
            t.True(hole[48 * W + 50].a > 0 && hole[48 * W + 50].a < 1, "Feather follows the inner boundary");
            shape.fillRule = VectorFillRule.NonZero;
            t.Near(1, Render()[48 * W + 64].a, .0001, "Feather follows resolved nonzero topology");
            shape.fillRule = VectorFillRule.EvenOdd; shape.contours.RemoveAt(1);

            shape.fillMode = VectorFillMode.LinearGradient;
            shape.gradient = new WhimTexGradient();
            var gradient = Render(); previews.Add(gradient);
            int builds = (int)typeof(VectorLayerBehaviour).GetProperty("CoverageBuildCount", Hidden).GetValue(vector);
            shape.gradientStart = new Vector2(.2f, .1f); Render();
            t.Equal(builds, (int)typeof(VectorLayerBehaviour).GetProperty("CoverageBuildCount", Hidden).GetValue(vector), "Gradient manipulation reuses Feather coverage");
            vector.featherPosition = VectorFeatherPosition.Outside; Render();
            t.Equal(builds + 1, (int)typeof(VectorLayerBehaviour).GetProperty("CoverageBuildCount", Hidden).GetValue(vector), "Feather position invalidates coverage");
            shape.fillMode = VectorFillMode.Solid; shape.fillColor = new Color(.2f, .4f, .8f, .5f);
            shape.stroke = true; shape.strokeColor = new Color(1, .2f, .1f, .5f); shape.strokeWidth = 8;
            shape.strokePosition = VectorStrokePosition.Inside; vector.feather = 4; vector.featherPosition = VectorFeatherPosition.Centered;
            var overlap = Render(); previews.Add(overlap);
            t.Near(.75, overlap[48 * W + 36].a, .003, "Translucent fill and stroke retain source-over semantics");
            CompareCpu(overlap);

            shape.fillMode = VectorFillMode.None; shape.strokeColor = Color.white; shape.strokeWidth = 8;
            shape.cap = VectorStrokeCap.Round; shape.dashPattern = new List<float> { 8, 16 };
            shape.contours.Clear(); shape.contours.Add(new VectorContour { closed = false, points = new List<VectorPoint>
                { new(new Vector2(.25f, .5f)), new(new Vector2(.75f, .5f)) } });
            vector.feather = 3;
            var dashed = Render(); previews.Add(dashed);
            t.True(dashed[48 * W + 29].a > 0 && dashed[48 * W + 29].a < 1, "Feather follows a round dash cap outside the endpoint");
            t.Near(0, dashed[48 * W + 48].a, .001, "Separated dash gaps remain transparent");
            CompareCpu(dashed);

            shape.stroke = false; shape.fillMode = VectorFillMode.Solid; shape.fillColor = Color.white;
            shape.contours.Clear(); shape.contours.Add(Rect(.25f, .25f, .3f, .75f));
            layer.transform.position = new Vector2(-48, 0); vector.feather = 16; vector.featherPosition = VectorFeatherPosition.Outside;
            var offCanvas = Render();
            t.True(offCanvas[48 * W].a > 0, "Off-canvas geometry can feather into the canvas");
            layer.transform.position = Vector2.zero;
            shape.contours.Clear(); shape.contours.Add(Rect(-.1f, .25f, .3f, .75f));
            t.Near(1, Render()[48 * W].a, .001, "Source-frame clipping does not create a soft artificial boundary");
            shape.contours.Clear(); shape.contours.Add(Rect(.25f, .25f, .75f, .75f));
            vector.feather = 8; vector.featherPosition = VectorFeatherPosition.Centered;
            var transform = layer.transform; transform.scale = new Vector2(.75f, 1.25f); transform.rotation = 21; layer.transform = transform;
            var transformed = Render(); previews.Add(transformed); CompareCpu(transformed);

            transform = TextureTransform.Default; transform.tiling = TransformTilingMode.Clip; layer.transform = transform;
            var perspective = ProjectiveMatrix.Identity; perspective.m20 = -.5;
            t.True(transform.TrySetMatrix(perspective), "Perspective fixture is valid inside the clipped source frame");
            layer.transform = transform; vector.feather = 400; vector.featherPosition = VectorFeatherPosition.Outside;
            t.True(Render()[48 * W + 64].a > 0, "Clipped Feather ignores projective horizons outside the source frame");
            transform = TextureTransform.Default; transform.tiling = TransformTilingMode.Clip; layer.transform = transform;
            vector.feather = 8;
            vector.featherPosition = VectorFeatherPosition.Outside;
            var beforeGroup = Render();
            var group = new Layer(new GroupLayerBehaviour()); group.children.Add(layer); doc.layers[0] = group;
            Same(t, beforeGroup, Render(), .0001f, "Group consumer preserves soft coverage");
            doc.layers[0] = layer;
            var thumbnail = vector.GetPreviewTexture(48);
            t.True(thumbnail != null && thumbnail.width > 0 && thumbnail.height > 0, "Feathered thumbnail renders");
            var output = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderCanvasAtSize", Hidden).Invoke(doc, new object[] { W, H });
            try { t.True(output != null && output.width == W && output.height == H, "Export-sized render consumes Feather"); }
            finally { RenderTexture.ReleaseTemporary(output); }
            var mask = new Layer(new ColorFillLayerBehaviour { color = new Color(0, 0, 0, .5f) });
            layer.clippingMask = true; doc.layers.Add(mask);
            var clipped = Render();
            t.Near(.5, clipped[48 * W + 29].a, .002, "Clipping preserves the base alpha");
            t.True(clipped[48 * W + 29].r > .01f && clipped[48 * W + 29].r < .99f, "Feather coverage blends color within the clipping chain");
            t.Near(0, clipped[0].r, .002, "Distant clipping base retains its color");
            t.Near(1, clipped[48 * W + 64].r, .002, "Opaque Feather interior replaces the clipping base color");
            layer.clippingMask = false; doc.layers.Remove(mask);
            var outline = new Layer(new OutlineLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = layer.Id, outlineWidth = 2 });
            doc.layers.Insert(0, outline);
            t.True(Render()[48 * W + 29].a >= beforeGroup[48 * W + 29].a, "Target consumer accepts feathered Vector");
            doc.layers.Remove(outline);
            layer.transform.tiling = TransformTilingMode.Unbounded;
            VectorLayerStructure.FitFrame(layer, W, H);
            t.Equal(8f, vector.feather, "Automatic geometry frame retains the reference-pixel Feather width");
            Same(t, beforeGroup, Render(), .001f, "Fitting the vector frame preserves Feather appearance", true);
            vector.shapes.Add(shape.Clone(false));
            VectorLayerStructure.Normalize(doc.layers, W, H);
            t.Equal(2, layer.children.Count, "Multiple figures normalize into separate child layers");
            foreach (var child in layer.children)
            {
                var part = (VectorLayerBehaviour)child.Behaviour;
                t.Equal(vector.feather, part.feather, "Normalized figures retain Feather width");
                t.Equal(vector.featherPosition, part.featherPosition, "Normalized figures retain Feather position");
            }
            SavePreview(previews);
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Render state remains unchanged");
        }
        finally { Object.DestroyImmediate(doc); RenderTexture.active = previous; GL.sRGBWrite = srgb; }

        void CompareCpu(Color[] expected)
        {
            var cpu = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
            try
            {
                var matrix = layer.transform.ToMatrix(W, H);
                var type = typeof(VectorLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.VectorRasterizer");
                type.GetMethod("Render", Hidden).Invoke(null, new object[] { vector, cpu, matrix, true });
                Same(t, expected, cpu.GetPixels(), .002f, "GPU paint and bounded CPU fallback agree", true);
            }
            finally { Object.DestroyImmediate(cpu); }
        }
    });

    public static string RunUndo() => TestContext.Run("Vector Feather owned-document Undo/Redo", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var vector = new VectorLayerBehaviour { referenceSize = new Vector2(W, H) };
            var shape = new VectorShape(); shape.contours.Add(Rect(.25f, .25f, .75f, .75f)); vector.shapes.Add(shape);
            var layer = new Layer(vector); layer.transform.tiling = TransformTilingMode.Clip;
            doc.width = W; doc.height = H; doc.layers.Add(layer);
            Color[] Render()
            {
                var image = doc.ComposeCanvas();
                try { return image.GetPixels(); } finally { Object.DestroyImmediate(image); }
            }
            var before = Render();
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(doc, "Vector Feather owned test");
            vector.feather = 9; vector.featherPosition = VectorFeatherPosition.Outside;
            var after = Render();
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
            vector = (VectorLayerBehaviour)doc.layers[0].Behaviour;
            t.Equal(0f, vector.feather, "Undo restores Feather width");
            t.Equal(VectorFeatherPosition.Centered, vector.featherPosition, "Undo restores Feather position");
            Same(t, before, Render(), .0001f, "Undo restores original coverage");
            Undo.PerformRedo(); vector = (VectorLayerBehaviour)doc.layers[0].Behaviour;
            t.Equal(9f, vector.feather, "Redo restores Feather width");
            t.Equal(VectorFeatherPosition.Outside, vector.featherPosition, "Redo restores Feather position");
            Same(t, after, Render(), .0001f, "Redo restores soft coverage");
        }
        finally { Undo.ClearUndo(doc); Undo.IncrementCurrentGroup(); Object.DestroyImmediate(doc); }
    });

    static VectorContour Rect(float x0, float y0, float x1, float y1)
    {
        var contour = new VectorContour();
        foreach (var p in new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) })
            contour.points.Add(new VectorPoint(p));
        return contour;
    }
    static void Same(TestContext t, Color[] a, Color[] b, float tolerance, string label, bool premultiplied = false)
    {
        t.Equal(a.Length, b.Length, label + " dimensions");
        float error = 0; int worst = 0;
        for (int i = 0; i < a.Length; i++)
        {
            Color left = a[i], right = b[i];
            if (premultiplied) { left.r *= left.a; left.g *= left.a; left.b *= left.a; right.r *= right.a; right.g *= right.a; right.b *= right.a; }
            float delta = Mathf.Max(Mathf.Abs(left.r - right.r), Mathf.Abs(left.g - right.g), Mathf.Abs(left.b - right.b), Mathf.Abs(left.a - right.a));
            if (delta > error) { error = delta; worst = i; }
        }
        t.Near(0, error, tolerance, label + " at " + worst + ": " + a[worst].ToString("G9") + " / " + b[worst].ToString("G9"));
    }
    static void SavePreview(List<Color[]> images)
    {
        var sheet = new Texture2D(W * 4, H * 2, TextureFormat.RGBA32, false, true);
        try
        {
            for (int i = 0; i < images.Count; i++)
            {
                var pixels = images[i]; var display = new Color[pixels.Length];
                for (int p = 0; p < pixels.Length; p++)
                {
                    float checker = ((p % W / 8 + p / W / 8) & 1) == 0 ? .16f : .25f;
                    Color c = pixels[p]; display[p] = new Color(c.r * c.a + checker * (1 - c.a), c.g * c.a + checker * (1 - c.a), c.b * c.a + checker * (1 - c.a), 1);
                }
                sheet.SetPixels(i % 4 * W, (1 - i / 4) * H, W, H, display);
            }
            sheet.Apply(); Directory.CreateDirectory("Temp/WhimTex"); File.WriteAllBytes("Temp/WhimTex/vector-feather-preview.png", sheet.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(sheet); }
    }
}
