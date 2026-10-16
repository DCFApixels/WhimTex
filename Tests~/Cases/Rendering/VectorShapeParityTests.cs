// Support: Tests~/Framework/TestApi.cs only. Opt-in graphics-enabled Editor test.
// No asset writes, imports, scene changes, Undo operations or frozen-image regeneration.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;
using Kind = DCFApixels.WhimTex.ShapeLayerBehaviour.ShapeKind;
using Corner = DCFApixels.WhimTex.ShapeLayerBehaviour.Corner;
using CornerStyle = DCFApixels.WhimTex.ShapeLayerBehaviour.CornerStyle;
using StrokePosition = DCFApixels.WhimTex.ShapeLayerBehaviour.StrokePosition;
using LineCap = DCFApixels.WhimTex.ShapeLayerBehaviour.LineCap;

public static class VectorShapeParityTests
{
    private sealed class Case
    {
        internal string name;
        internal int width = 128, height = 96;
        internal Kind kind;
        internal bool stroke, fill = true;
        internal Vector2 scale = Vector2.one * .55f;
        internal float pixelScale = 1;
        internal TransformTilingMode tiling = TransformTilingMode.Clip;
        internal Action<ShapeLayerBehaviour> configure;
    }

    public static string Run()
    {
        List<Case> cases = Cases();
        return TestContext.Run("Shape/Vector pixel parity: " + cases.Count + " cases", context =>
        {
            var failures = new List<string>();
            // Complete the matrix even when one fixture fails, so an early cap/arc failure
            // cannot hide the results for the other Shape kinds.
            foreach (Case item in cases)
            {
                try { CompareCase(context, item); }
                catch (Exception error) { failures.Add(item.name + ": " + error.Message); }
            }
            context.True(failures.Count == 0, string.Join("\n", failures));
        });
    }

    private static List<Case> Cases()
    {
        var result = new List<Case>();
        foreach (int width in new[] { 96, 128 })
        foreach (Kind kind in Enum.GetValues(typeof(Kind)))
        foreach (bool stroke in new[] { false, true })
            result.Add(new Case { name = kind + "/" + width + "x96/" + (stroke ? "fill+outline" : "fill"),
                width = width, kind = kind, stroke = stroke,
                // Uniform scaling is required for conversion of canvas-pixel Shape strokes.
                // A smaller Line frame leaves its round caps fully visible on the canvas.
                scale = Vector2.one * (kind == Kind.Line ? .34f : .55f) });

        foreach (bool stroke in new[] { false, true })
        {
            result.Add(new Case { name = "rounded-rectangle/" + stroke, kind = Kind.Rectangle, stroke = stroke,
                configure = s => s.rectangleCorners = new[] { new Corner(.3f), new Corner(.12f, CornerStyle.Bevel), new Corner(.22f), new Corner(.08f) } });
            result.Add(new Case { name = "rounded-star/" + stroke, kind = Kind.Star, stroke = stroke,
                tiling = TransformTilingMode.Unbounded,
                configure = s => { s.sides = 6; s.innerRadius = .48f; s.outerCorner = new Corner(.16f); s.innerCorner = new Corner(.1f); } });
        }
        result.Add(new Case { name = "beveled-star/outline", kind = Kind.Star, stroke = true,
            configure = s => { s.outerCorner = new Corner(.2f, CornerStyle.Bevel); s.innerCorner = new Corner(.14f, CornerStyle.Bevel); } });
        result.Add(new Case { name = "mixed-polygon-corners/outline", kind = Kind.Polygon, stroke = true,
            configure = s => { s.sides = 7; s.polygonCorners = new Corner[7]; for (int i = 0; i < 7; i++) s.polygonCorners[i] = new Corner(.12f, (i & 1) == 0 ? CornerStyle.Round : CornerStyle.Bevel); } });
        foreach (LineCap cap in Enum.GetValues(typeof(LineCap)))
        {
            LineCap selected = cap;
            result.Add(new Case { name = "arc/" + cap + "/outside-outline", kind = Kind.Arc, stroke = true,
                configure = s => { s.lineCap = selected; s.startAngle = 27; s.sweepAngle = 227; s.arcThickness = 8; s.strokePosition = StrokePosition.Outside; } });
            result.Add(new Case { name = "line/" + cap + "/outline", kind = Kind.Line, stroke = true,
                scale = Vector2.one * .34f, configure = s => s.lineCap = selected });
        }
        result.Add(new Case { name = "full-arc-ring/inside-outline", kind = Kind.Arc, stroke = true,
            configure = s => { s.sweepAngle = 360; s.arcThickness = 10; s.strokePosition = StrokePosition.Inside; s.strokeWidth = 2; } });
        result.Add(new Case { name = "rounded-reflex-sector/outline", kind = Kind.Sector, stroke = true,
            configure = s => { s.sweepAngle = 245; s.outerCorner = new Corner(.15f); s.innerCorner = new Corner(.1f); } });
        result.Add(new Case { name = "beveled-convex-sector/fill", kind = Kind.Sector,
            configure = s => { s.sweepAngle = 125; s.outerCorner = new Corner(.2f, CornerStyle.Bevel); s.innerCorner = new Corner(.12f, CornerStyle.Bevel); } });
        foreach (StrokePosition position in new[] { StrokePosition.Inside, StrokePosition.Outside })
        {
            StrokePosition selected = position;
            result.Add(new Case { name = "rectangle/" + position + "/hollow-outline", kind = Kind.Rectangle,
                stroke = true, fill = false, configure = s => s.strokePosition = selected });
        }
        result.Add(new Case { name = "ellipse/pixel-scale-1.25/outline", kind = Kind.Ellipse, stroke = true, pixelScale = 1.25f });
        result.Add(new Case { name = "arc/nonuniform/fill-only", kind = Kind.Arc, scale = new Vector2(.64f, .4f) });
        result.Add(new Case { name = "rectangle/step/outline", kind = Kind.Rectangle, stroke = true,
            configure = s => s.edgeMode = ShapeLayerBehaviour.EdgeMode.Step });
        result.Add(new Case { name = "arc/step/outline", kind = Kind.Arc, stroke = true,
            configure = s => s.edgeMode = ShapeLayerBehaviour.EdgeMode.Step });
        foreach (Kind kind in Enum.GetValues(typeof(Kind)))
        foreach (VectorStrokeJoin join in Enum.GetValues(typeof(VectorStrokeJoin)))
        {
            var selected = join;
            result.Add(new Case { name = kind + "/styled/" + join, kind = kind, stroke = true, fill = false,
                scale = Vector2.one * (kind == Kind.Line ? .34f : .55f),
                configure = s => { s.strokeJoin = selected; s.strokeCap = VectorStrokeCap.Round;
                    s.dashPattern = new List<float> { 3, 2 }; s.dashOffset = 1.25f; } });
        }
        foreach (Kind kind in new[] { Kind.Rectangle, Kind.Star })
        foreach (StrokePosition position in new[] { StrokePosition.Inside, StrokePosition.Outside })
        foreach (VectorStrokeJoin join in Enum.GetValues(typeof(VectorStrokeJoin)))
        foreach (float dash in new[] { .01f, 1.5f })
        {
            var selectedPosition = position; var selectedJoin = join; float selectedDash = dash;
            result.Add(new Case { name = kind + "/offset/" + position + "/" + join + "/dash=" + dash,
                kind = kind, stroke = true, fill = false,
                configure = s => { s.strokePosition = selectedPosition; s.strokeJoin = selectedJoin;
                    s.strokeCap = VectorStrokeCap.Round; s.dashPattern = new List<float> { selectedDash, 2 }; } });
        }
        // Nonuniform/projective stroked Shapes are explicitly unsupported by the converter,
        // and are covered by its rejection tests instead of being forced into a parity fixture.
        return result;
    }

    private static void CompareCase(TestContext context, Case item)
    {
        RenderTexture previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        WhimTexDocument document = null;
        Layer layer = null;
        ShapeLayerBehaviour source = null;
        Texture2D before = null, after = null;
        TextureTransform originalTransform = default;
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = item.width; document.height = item.height; document.pixelScale = item.pixelScale;
            source = new ShapeLayerBehaviour { kind = item.kind, fill = item.fill, stroke = item.stroke,
                fillColor = Color.red, strokeColor = Color.blue, strokeWidth = 3.5f,
                strokePosition = StrokePosition.Center, lineCap = LineCap.Round,
                arcThickness = 8, startAngle = 17, sweepAngle = 215 };
            item.configure?.Invoke(source);
            layer = new Layer(source);
            layer.transform.scaleF = item.scale;
            layer.transform.positionF = new Vector2(2.25f, -1.75f);
            layer.transform.rotationF = 11;
            layer.transform.tiling = item.tiling;
            originalTransform = layer.transform;
            document.layers.Add(layer);

            before = document.ComposeCanvas();
            string id = layer.Id;
            context.True(RenderTexture.active == previous && GL.sRGBWrite == previousSrgb, item.name + " source restores render state");
            // Capture after rendering: Shape may normalize its corner arrays on first use.
            string sourceSnapshot = JsonUtility.ToJson(source);
            VectorLayerBehaviour converted = ShapeToVector.Convert(source, document);
            context.True(ReferenceEquals(layer.Behaviour, source), item.name + " conversion preparation keeps source attached");
            context.Equal(sourceSnapshot, JsonUtility.ToJson(source), item.name + " conversion preparation preserves source data");
            layer.SetBehaviour(converted);
            // Match the actual main/UI/API conversion operation. Shape Clip is analytically
            // unbounded, so its protruding bodies and strokes require Vector Unbounded.
            if (layer.transform.tiling == TransformTilingMode.Clip) layer.transform.tiling = TransformTilingMode.Unbounded;
            TextureTransform expectedTransform = originalTransform;
            if (expectedTransform.tiling == TransformTilingMode.Clip) expectedTransform.tiling = TransformTilingMode.Unbounded;
            context.Equal(id, layer.Id, item.name + " replacement keeps layer ID");
            context.Equal(expectedTransform, layer.transform, item.name + " only Clip-to-Unbounded transform adaptation");
            after = document.ComposeCanvas();
            context.True(RenderTexture.active == previous && GL.sRGBWrite == previousSrgb, item.name + " converted render restores state");
            context.Equal(before.width, after.width, item.name + " matching width");
            context.Equal(before.height, after.height, item.name + " matching height");
            Color[] a = before.GetPixels(), b = after.GetPixels();
            context.True(Finite(a) && Finite(b), item.name + " finite normalized render pixels");
            CompareCoverage(context, a, b, item.width, item.height, 0, item.name + "/alpha");
            // Pure primary colors separate the fill/stroke geometry without demanding identical
            // AA color bytes. Premultiplied blue is stroke coverage; red is visible fill coverage.
            if (item.stroke) CompareCoverage(context, a, b, item.width, item.height, 2, item.name + "/stroke");
            if (item.fill && item.stroke) CompareCoverage(context, a, b, item.width, item.height, 1, item.name + "/fill");
        }
        finally
        {
            try
            {
                if (layer != null && source != null)
                {
                    layer.SetBehaviour(source); // Detaches and releases the converted raster cache.
                    layer.transform = originalTransform;
                }
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
                if (before != null) Object.DestroyImmediate(before);
                if (after != null) Object.DestroyImmediate(after);
                if (document != null) Object.DestroyImmediate(document);
            }
        }
    }

    private static bool Finite(Color[] pixels)
    {
        foreach (Color c in pixels)
            if (float.IsNaN(c.r + c.g + c.b + c.a) || float.IsInfinity(c.r + c.g + c.b + c.a) || c.a < -.001f || c.a > 1.001f) return false;
        return true;
    }

    private static float Coverage(Color color, int channel) => channel == 0 ? color.a : color.a * (channel == 1 ? color.r : color.b);

    private static void CompareCoverage(TestContext context, Color[] a, Color[] b, int width, int height, int channel, string label)
    {
        double areaA = 0, areaB = 0, l1 = 0, ax = 0, ay = 0, bx = 0, by = 0;
        int perimeter = 0, unsupportedA = 0, unsupportedB = 0;
        int aminX = width, aminY = height, amaxX = -1, amaxY = -1;
        int bminX = width, bminY = height, bmaxX = -1, bmaxY = -1;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            float ca = Coverage(a[i], channel), cb = Coverage(b[i], channel);
            areaA += ca; areaB += cb; l1 += Math.Abs(ca - cb);
            ax += ca * (x + .5); ay += ca * (y + .5); bx += cb * (x + .5); by += cb * (y + .5);
            if (ca >= .5f)
            {
                aminX = Math.Min(aminX,x); aminY = Math.Min(aminY,y); amaxX = Math.Max(amaxX,x); amaxY = Math.Max(amaxY,y);
                if (x == 0 || Coverage(a[i-1],channel) < .5f) perimeter++;
                if (x == width-1 || Coverage(a[i+1],channel) < .5f) perimeter++;
                if (y == 0 || Coverage(a[i-width],channel) < .5f) perimeter++;
                if (y == height-1 || Coverage(a[i+width],channel) < .5f) perimeter++;
            }
            if (cb >= .5f) { bminX = Math.Min(bminX,x); bminY = Math.Min(bminY,y); bmaxX = Math.Max(bmaxX,x); bmaxY = Math.Max(bmaxY,y); }
            if (ca >= .8f && !Nearby(b,width,height,x,y,channel)) unsupportedA++;
            if (cb >= .8f && !Nearby(a,width,height,x,y,channel)) unsupportedB++;
        }
        string stats = string.Format(CultureInfo.InvariantCulture,
            "{0}: area {1:F2}/{2:F2}, L1 {3:F2}, perimeter {4}, unmatched strong pixels {5}/{6}",
            label, areaA, areaB, l1, perimeter, unsupportedA, unsupportedB);
        context.True(areaA > 4 && areaB > 4 && amaxX >= 0 && bmaxX >= 0, stats + "; both masks must contain geometry");
        // The tolerance scales with geometric boundary length, not the full transparent canvas.
        // It permits different analytic/scanline AA kernels while rejecting lost caps, holes,
        // shifted bodies, missing outlines and large changes of covered area.
        context.True(Math.Abs(areaA-areaB) <= Math.Max(4, perimeter * .4), stats + "; covered-area tolerance");
        context.True(l1 <= Math.Max(8, perimeter * .65), stats + "; boundary coverage tolerance");
        context.True(unsupportedA == 0 && unsupportedB == 0, stats + "; bidirectional two-pixel support");
        context.True(Math.Abs(aminX-bminX) <= 1 && Math.Abs(amaxX-bmaxX) <= 1 && Math.Abs(aminY-bminY) <= 1 && Math.Abs(amaxY-bmaxY) <= 1,
            stats + "; half-coverage bounds differ by at most one pixel");
        context.Near(ax/areaA, bx/areaB, .65, stats + "; coverage centroid x");
        context.Near(ay/areaA, by/areaB, .65, stats + "; coverage centroid y");
    }

    private static bool Nearby(Color[] pixels, int width, int height, int x, int y, int channel)
    {
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
        {
            if (dx*dx + dy*dy > 4) continue;
            int sx = x+dx, sy = y+dy;
            if (sx >= 0 && sx < width && sy >= 0 && sy < height && Coverage(pixels[sy*width+sx],channel) >= .2f) return true;
        }
        return false;
    }
}
