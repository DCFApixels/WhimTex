// Support: Tests~/Framework/TestApi.cs. Opt-in visual diagnostics.
// Captures actual ComposeCanvas pixels, not a geometry illustration or generated reference image.
using System;
using System.IO;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorRenderingPreview
{
    public static string CaptureBrush()
    {
        string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "WhimTex", "vector-brush-curves-preview-" + System.Guid.NewGuid().ToString("N") + ".png");
        return TestContext.Run("Actual Bezier brush preview: " + path, context =>
        {
            var document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = 640; document.height = 360;
            var vector = new VectorLayerBehaviour { referenceSize = new Vector2(640, 360) };
            document.layers.Add(new Layer(vector));
            Texture2D rendered = null, png = null;
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                var background = new VectorShape { fillColor = new Color(.025f, .035f, .05f, 1) };
                background.contours.Add(Rect(0, 0, 1, 1)); vector.shapes.Add(background);
                var builder = new VectorBrushBuilder(); builder.Begin(128, 128);
                for (int i = 0; i <= 64; i++)
                    builder.AddPoint(new Vector2(64 + 29 * Mathf.Sin(i * .095f), 17 + i * 1.45f), 8);
                var draft = builder.Finish();
                int fineCount = 0, coarseCount = 0;
                for (int side = 0; side < 2; side++)
                {
                    draft.shapeId = Guid.NewGuid().ToString("N");
                    draft.tolerance = side == 0 ? .25f : 4;
                    VectorShape shape = VectorBrushGeometry.Rebuild(draft, new Vector2(128, 128));
                    shape.fillColor = side == 0 ? new Color(.03f, .55f, .6f) : new Color(.55f, .16f, .35f);
                    shape.stroke = true; shape.strokeWidth = 1; shape.strokeColor = Color.white;
                    foreach (var contour in shape.contours) foreach (var point in contour.points)
                    {
                        point.position = Vector2.Scale(point.position, new Vector2(.45f, .9f)) + new Vector2(.025f + side * .5f, .05f);
                        point.inTangent = Vector2.Scale(point.inTangent, new Vector2(.45f, .9f));
                        point.outTangent = Vector2.Scale(point.outTangent, new Vector2(.45f, .9f));
                        if (side == 0) fineCount++; else coarseCount++;
                    }
                    vector.shapes.Add(shape);
                    foreach (var contour in shape.contours) foreach (var point in contour.points)
                    {
                        var handles = new VectorShape { fillMode = VectorFillMode.None, stroke = true,
                            strokeWidth = .7f, strokeColor = new Color(1, .7f, .2f, .7f) };
                        var line = new VectorContour { closed = false };
                        line.points.Add(new VectorPoint(point.position + point.inTangent));
                        line.points.Add(new VectorPoint(point.position));
                        line.points.Add(new VectorPoint(point.position + point.outTangent)); handles.contours.Add(line);
                        vector.shapes.Add(handles);
                        var anchor = new VectorShape { fillColor = new Color(1, .7f, .2f) };
                        anchor.contours.Add(Ellipse(point.position, new Vector2(2f / 640, 2f / 360)));
                        vector.shapes.Add(anchor);
                    }
                }
                context.True(coarseCount < fineCount, "Coarse preview uses fewer cubic anchors: " + fineCount + " / " + coarseCount);
                vector.Validate(); rendered = document.ComposeCanvas();
                context.Equal(640, rendered.width, "Brush preview width");
                png = new Texture2D(640, 360, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                Color[] pixels = rendered.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color c = pixels[i];
                    context.True(!float.IsNaN(c.r + c.g + c.b + c.a) && !float.IsInfinity(c.r + c.g + c.b + c.a), "Finite brush pixels");
                    pixels[i] = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), c.a);
                }
                png.SetPixels(pixels); png.Apply(false, false);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, png.EncodeToPNG());
                context.True(File.Exists(path), "Actual brush render written");
                context.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Render state preserved");
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (rendered != null) Object.DestroyImmediate(rendered);
                if (png != null) Object.DestroyImmediate(png);
                Object.DestroyImmediate(document);
            }
        });
    }

    public static string Capture()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Temp", "WhimTex", "vector-render-preview-" + System.Guid.NewGuid().ToString("N") + ".png");
        return TestContext.Run("Actual Vector preview: " + path, context =>
        {
            var document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = 512; document.height = 320;
            var vector = new VectorLayerBehaviour { referenceSize = new Vector2(512, 320) };
            document.layers.Add(new Layer(vector));
            Texture2D linear = null, png = null;
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                var background = new VectorShape { fillColor = new Color(.09f, .11f, .16f, 1) };
                background.contours.Add(Rect(0,0,1,1)); vector.shapes.Add(background);
                var donut = new VectorShape { fillMode = VectorFillMode.LinearGradient,
                    gradientStart = new Vector2(.08f,.5f), gradientEnd = new Vector2(.42f,.9f),
                    stroke = true, strokeColor = new Color(.8f,.95f,1,1), strokeWidth = 3 };
                donut.gradient.SetKeys(new[] { new GradientColorKey(new Color(.1f,.8f,1),0), new GradientColorKey(new Color(.65f,.3f,1),1) },
                    new[] { new GradientAlphaKey(1,0), new GradientAlphaKey(1,1) });
                donut.contours.Add(Ellipse(new Vector2(.25f,.7f), new Vector2(.165f,.24f)));
                donut.contours.Add(Ellipse(new Vector2(.25f,.7f), new Vector2(.075f,.11f)));
                vector.shapes.Add(donut);

                var radial = new VectorShape { fillMode = VectorFillMode.RadialGradient,
                    gradientStart = new Vector2(.73f,.76f), gradientEnd = new Vector2(.91f,.76f) };
                radial.gradient.SetKeys(new[] { new GradientColorKey(new Color(1,.95f,.3f),0), new GradientColorKey(new Color(1,.15f,.4f),1) },
                    new[] { new GradientAlphaKey(1,0), new GradientAlphaKey(.25f,1) });
                radial.contours.Add(Ellipse(new Vector2(.75f,.7f), new Vector2(.165f,.24f))); vector.shapes.Add(radial);
                var curve = new VectorShape { fillMode = VectorFillMode.None, stroke = true, strokeWidth = 7,
                    strokeColor = Color.white, cap = VectorStrokeCap.Round, join = VectorStrokeJoin.Round, dashOffset = 3 };
                curve.dashPattern.AddRange(new[] { 16f, 10f, 3f, 10f });
                var open = new VectorContour { closed = false };
                open.points.Add(new VectorPoint(new Vector2(.54f,.59f)) { outTangent = new Vector2(.08f,.4f) });
                open.points.Add(new VectorPoint(new Vector2(.96f,.77f)) { inTangent = new Vector2(-.16f,-.32f) });
                curve.contours.Add(open); vector.shapes.Add(curve);

                for (int i = 0; i < 3; i++)
                {
                    var outline = new VectorShape { fillMode = VectorFillMode.None, stroke = true, strokeWidth = 14,
                        strokeColor = i == 0 ? new Color(.25f,.8f,1) : i == 1 ? new Color(.55f,1,.6f) : new Color(1,.55f,.4f),
                        join = (VectorStrokeJoin)i, strokePosition = (VectorStrokePosition)i };
                    float left = .09f + i * .315f;
                    outline.contours.Add(Rect(left,.12f,left + .19f,.31f)); vector.shapes.Add(outline);
                }
                vector.Validate(); linear = document.ComposeCanvas();
                context.Equal(512, linear.width, "preview width"); context.Equal(320, linear.height, "preview height");
                context.True(linear.GetPixel(128,224).r < .05f && linear.GetPixel(128,224).b < .05f, "compound hole reveals backdrop");
                context.True(linear.GetPixel(60,224).b > .2f, "gradient shell rendered");
                context.True(linear.GetPixel(374,243).r > .5f, "radial gradient rendered");
                png = new Texture2D(512,320,TextureFormat.RGBA32,false) { hideFlags = HideFlags.HideAndDontSave };
                Color[] colors = linear.GetPixels();
                for (int i = 0; i < colors.Length; i++)
                {
                    Color c = colors[i];
                    context.True(!float.IsNaN(c.r + c.g + c.b + c.a) && !float.IsInfinity(c.r + c.g + c.b + c.a), "finite preview pixels");
                    colors[i] = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), c.a);
                }
                png.SetPixels(colors); png.Apply(false, false);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllBytes(path, png.EncodeToPNG());
                context.True(File.Exists(path) && new FileInfo(path).Length > 100, "actual pixel PNG saved");
                context.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "preview capture preserves caller render state");
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (linear != null) Object.DestroyImmediate(linear);
                if (png != null) Object.DestroyImmediate(png);
                Object.DestroyImmediate(document);
            }
        });
    }

    private static VectorContour Rect(float x0, float y0, float x1, float y1)
    {
        var c = new VectorContour();
        c.points.Add(new VectorPoint(new Vector2(x0,y0))); c.points.Add(new VectorPoint(new Vector2(x1,y0)));
        c.points.Add(new VectorPoint(new Vector2(x1,y1))); c.points.Add(new VectorPoint(new Vector2(x0,y1))); return c;
    }
    private static VectorContour Ellipse(Vector2 center, Vector2 radius)
    {
        const float k = .55228475f;
        var c = new VectorContour();
        c.points.Add(new VectorPoint(center + new Vector2(radius.x,0)) { inTangent = new Vector2(0,-radius.y*k), outTangent = new Vector2(0,radius.y*k) });
        c.points.Add(new VectorPoint(center + new Vector2(0,radius.y)) { inTangent = new Vector2(radius.x*k,0), outTangent = new Vector2(-radius.x*k,0) });
        c.points.Add(new VectorPoint(center - new Vector2(radius.x,0)) { inTangent = new Vector2(0,radius.y*k), outTangent = new Vector2(0,-radius.y*k) });
        c.points.Add(new VectorPoint(center - new Vector2(0,radius.y)) { inTangent = new Vector2(-radius.x*k,0), outTangent = new Vector2(radius.x*k,0) });
        return c;
    }
}
