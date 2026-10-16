// Support: Tests~/Framework/TestApi.cs. Graphics-enabled Editor; transient objects only.
// Reflection below inspects WhimTex-owned cache state, never Unity internals.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorPaintCacheTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Width = 96, Height = 80;
    static readonly VectorFillMode[] GradientModes = {
        VectorFillMode.LinearGradient, VectorFillMode.RadialGradient,
        VectorFillMode.SquareGradient, VectorFillMode.DiamondGradient, VectorFillMode.CircularGradient
    };

    public static string Run() => TestContext.Run("Vector GPU paint preserves coverage and Boolean geometry caches", t =>
    {
        PaintEdits(t);
        LostPaintStorage(t);
        SharedEdgeAlpha(t);
        CoverageInvalidation(t);
        BooleanEdits(t);
        CombineOrder(t);
    });

    static void PaintEdits(TestContext t)
    {
        foreach (var edge in new[] { VectorEdgeMode.Antialiased, VectorEdgeMode.Step })
        {
            using var fixture = new Fixture();
            var model = Vector(Rect(.2f, .2f, .8f, .8f));
            model.edgeMode = edge;
            var shape = model.shapes[0];
            shape.fillMode = shape.strokeMode = VectorFillMode.LinearGradient;
            shape.gradient = Ramp(); shape.strokeGradient = Ramp(true);
            fixture.Add(model);
            fixture.Render(t);
            var cache = new CacheStamp(model);
            t.True(cache.Count > 0 && cache.Identity != null, "First render builds coverage " + edge);

            void Check(string label)
            {
                var pixels = fixture.Render(t);
                cache.Unchanged(t, model, edge + " " + label);
                // These probes are wholly within fill-only / stroke-only regions.
                Near(t, Paint(shape, false, 48, 40), pixels[40 * Width + 48], label + " fill");
                Near(t, Paint(shape, true, 17, 40), pixels[40 * Width + 17], label + " stroke");
                t.Near(0, pixels[0].a, .0001, label + " exterior stays transparent");
            }

            Check("unchanged render");
            shape.gradientStart = new Vector2(.31f, .27f); Check("fill start");
            shape.gradientEnd = new Vector2(.79f, .61f); Check("fill end");
            shape.strokeGradientStart = new Vector2(.05f, .17f); Check("stroke start");
            shape.strokeGradientEnd = new Vector2(.63f, .83f); Check("stroke end");
            foreach (var mode in GradientModes) { shape.fillMode = mode; Check("fill mode " + mode); }
            foreach (var mode in GradientModes) { shape.strokeMode = mode; Check("stroke mode " + mode); }
            shape.gradientRepetitions = 2.25f; Check("fill circular repetitions");
            shape.strokeGradientRepetitions = 1.75f; Check("stroke circular repetitions");
            foreach (var wrap in new[] { WhimTexGradientWrapMode.Repeat, WhimTexGradientWrapMode.Mirror })
            {
                shape.gradient.WrapMode = wrap; Check("fill wrap " + wrap);
                shape.strokeGradient.WrapMode = wrap; Check("stroke wrap " + wrap);
            }
            foreach (var mode in new[] { WhimTexGradientMode.Fixed, WhimTexGradientMode.Perceptual })
            {
                shape.gradient.Mode = mode; Check("fill interpolation " + mode);
                shape.strokeGradient.Mode = mode; Check("stroke interpolation " + mode);
            }
            shape.gradient = Ramp(true); Check("replace fill palette");
            shape.strokeGradient = Ramp(); Check("replace stroke palette");
            SetRamp(shape.gradient, Color.yellow, Color.cyan, .8f, .6f); Check("edit fill color keys in place");
            SetRamp(shape.strokeGradient, Color.magenta, Color.green, .8f, .6f); Check("edit stroke color keys in place");
            SetRamp(shape.gradient, Color.yellow, Color.cyan, .15f, .35f); Check("edit fill alpha keys");
            SetRamp(shape.strokeGradient, Color.magenta, Color.green, .25f, .45f); Check("edit stroke alpha keys");
            shape.gradient.SetMidpoint(false, 0, .3f); Check("fill midpoint");
            shape.strokeGradient.SetMidpoint(true, 0, .7f); Check("stroke alpha midpoint");
            shape.gradient.ColorSpace = ColorSpace.Gamma; Check("fill color space");
            shape.strokeGradient.ColorSpace = ColorSpace.Gamma; Check("stroke color space");
            shape.fillMode = VectorFillMode.Solid; shape.fillColor = new Color(1, 0, 0, .3f); Check("solid fill");
            shape.strokeMode = VectorFillMode.Solid; shape.strokeColor = new Color(0, 0, 1, .4f); Check("solid stroke");
            shape.fillColor.a = .65f; Check("solid fill alpha");
            shape.strokeColor.a = .2f; Check("solid stroke alpha");
        }
    }

    static void LostPaintStorage(TestContext t)
    {
        using var fixture = new Fixture();
        var model = Vector(Rect(.203f, .207f, .797f, .793f));
        model.shapes[0].fillMode = VectorFillMode.CircularGradient;
        model.shapes[0].gradient = Ramp();
        fixture.Add(model);
        var expected = fixture.Render(t);
        var cache = new CacheStamp(model);
        var target = (RenderTexture)typeof(VectorLayerBehaviour).GetField("painted", Hidden).GetValue(model);
        t.True(target != null && target.IsCreated(), "Owned paint target is created");
        target.Release();
        t.True(!target.IsCreated(), "Simulated paint storage loss");
        Same(t, expected, fixture.Render(t), "Lost paint storage is recreated with identical pixels");
        t.True(target.IsCreated(), "Repaint recreates the owned target");
        cache.Unchanged(t, model, "Paint storage recovery reuses coverage");
    }

    static void SharedEdgeAlpha(TestContext t)
    {
        using var fixture = new Fixture();
        var model = Vector(Rect(.203f, .2f, .8f, .8f));
        var shape = model.shapes[0];
        shape.strokePosition = VectorStrokePosition.Inside;
        shape.fillColor = new Color(1, 0, 0, .5f); shape.strokeColor = new Color(0, 0, 1, .5f);
        fixture.Add(model); fixture.Render(t);
        var cache = new CacheStamp(model);
        foreach (float alpha in new[] { .5f, .2f, .8f })
        {
            shape.strokeColor.a = alpha;
            var pixels = fixture.Render(t);
            cache.Unchanged(t, model, "coincident fill/stroke AA alpha edit");
            var expected = Over(shape.strokeColor, shape.fillColor);
            // Exact area of an axis-aligned rectangle within pixel [19,20] x [40,41].
            expected.a *= 20 - .203f * Width;
            Near(t, expected, pixels[40 * Width + 19], "shared boundary uses joint coverage");
        }
    }

    static void CoverageInvalidation(TestContext t)
    {
        void Check(string label, Action<Fixture, Layer, VectorLayerBehaviour> change,
            Action<VectorLayerBehaviour> prepare = null)
        {
            using var fixture = new Fixture();
            // Fractional edges make the AA -> Step change observable without a golden.
            var model = Vector(Rect(.203f, .207f, .797f, .793f));
            prepare?.Invoke(model);
            var layer = fixture.Add(model);
            var before = fixture.Render(t);
            var cache = new CacheStamp(model);
            change(fixture, layer, model);
            var after = fixture.Render(t);
            t.Equal(cache.Count + 1, Count(model, "CoverageBuildCount"), label + " rebuilds coverage once");
            Different(t, before, after, label + " changes actual image");
            var changed = new CacheStamp(model);
            Same(t, after, fixture.Render(t), label + " repeated render");
            changed.Unchanged(t, model, label + " settles after rebuild");
        }

        Check("anchor geometry", (f, l, m) => m.shapes[0].contours[0].points[0].position += new Vector2(.09f, .08f));
        Check("cubic tangent", (f, l, m) => m.shapes[0].contours[0].points[0].outTangent = new Vector2(.12f, -.13f));
        Check("stroke width", (f, l, m) => m.shapes[0].strokeWidth = 14);
        Check("dash pattern", (f, l, m) => m.shapes[0].dashPattern = new List<float> { 9, 7 });
        Check("dash offset", (f, l, m) => m.shapes[0].dashOffset = 5,
            m => m.shapes[0].dashPattern = new List<float> { 9, 7 });
        Check("relative dash units", (f, l, m) => m.shapes[0].dashPatternRelative = true,
            m => m.shapes[0].dashPattern = new List<float> { 2, 1 });
        Check("stroke position", (f, l, m) => m.shapes[0].strokePosition = VectorStrokePosition.Outside);
        Check("translation", (f, l, m) => l.transform.positionF = new Vector2(7, -3));
        Check("rotation", (f, l, m) => l.transform.rotationF = 17);
        Check("scale", (f, l, m) => l.transform.scaleF = new Vector2(.8f, 1.1f));
        Check("canvas width", (f, l, m) => f.Document.width = 112);
        Check("canvas height", (f, l, m) => f.Document.height = 92);
        Check("reference size", (f, l, m) => m.referenceSize = new Vector2(Width * 2, Height * 2));
        Check("edge mode", (f, l, m) => m.edgeMode = VectorEdgeMode.Step);
    }

    static void BooleanEdits(TestContext t)
    {
        using var fixture = new Fixture();
        var model = new BooleanGroupLayerBehaviour { referenceSize = new Vector2(Width, Height) };
        var group = fixture.Add(model);
        var bottomModel = Vector(Rect(.15f, .2f, .6f, .8f));
        var topModel = Vector(Rect(.4f, .1f, .85f, .7f));
        var bottom = Operand(bottomModel); var top = Operand(topModel);
        group.children.Add(top); group.children.Add(bottom);
        model.style.fillMode = VectorFillMode.LinearGradient;
        model.style.gradient = Ramp(); model.style.stroke = true; model.style.strokeWidth = 4;
        model.style.strokeMode = VectorFillMode.LinearGradient; model.style.strokeGradient = Ramp(true);
        fixture.Render(t);
        var result = Result(model);
        var cache = new CacheStamp(result);
        int builds = Count(model, "GeometryBuildCount");
        t.True(builds > 0, "Boolean result is prepared");

        void Prepared(string label)
        {
            t.Equal(builds, Count(model, "GeometryBuildCount"), label + " retains Boolean geometry count");
            t.True(ReferenceEquals(result, Result(model)), label + " retains prepared result identity");
        }
        void Repaint(string label)
        {
            var pixels = fixture.Render(t);
            Prepared(label); cache.Unchanged(t, result, label);
            Near(t, Paint(model.style, false, 48, 40), pixels[40 * Width + 48], label + " rendered group fill");
            Near(t, Paint(model.style, true, 13, 40), pixels[40 * Width + 13], label + " rendered group stroke");
        }

        model.style.gradientStart = new Vector2(.2f, .1f); Repaint("Boolean fill anchor");
        model.style.gradientEnd = new Vector2(.7f, .9f); Repaint("Boolean fill end");
        model.style.strokeGradientStart = new Vector2(.03f, .22f); Repaint("Boolean stroke anchor");
        model.style.fillMode = VectorFillMode.RadialGradient; Repaint("Boolean fill mode");
        model.style.strokeMode = VectorFillMode.DiamondGradient; Repaint("Boolean stroke mode");
        model.style.gradient = Ramp(true); Repaint("Boolean fill palette");
        SetRamp(model.style.strokeGradient, Color.green, Color.blue, .2f, .6f); Repaint("Boolean stroke palette/alpha");
        model.style.fillMode = VectorFillMode.Solid; model.style.fillColor = new Color(1, 0, 0, .25f);
        Repaint("Boolean solid alpha");

        var baseline = fixture.Render(t);
        void Ignored(string label, Action edit)
        {
            edit(); Same(t, baseline, fixture.Render(t), label);
            Prepared(label); cache.Unchanged(t, result, label);
        }
        Ignored("child fill mode", () => bottomModel.shapes[0].fillMode = VectorFillMode.None);
        Ignored("child fill palette", () => topModel.shapes[0].gradient = Ramp(true));
        Ignored("child fill anchors", () => topModel.shapes[0].gradientEnd = new Vector2(.4f, .7f));
        Ignored("child solid color/alpha", () => topModel.shapes[0].fillColor = new Color(0, 1, 0, .1f));
        Ignored("child stroke geometry", () => {
            bottomModel.shapes[0].strokeWidth = 21;
            bottomModel.shapes[0].dashPattern = new List<float> { 3, 2 };
            bottomModel.shapes[0].strokePosition = VectorStrokePosition.Outside;
        });
        Ignored("child stroke paint", () => {
            bottomModel.shapes[0].strokeMode = VectorFillMode.CircularGradient;
            bottomModel.shapes[0].strokeGradient = Ramp(true);
            bottomModel.shapes[0].strokeGradientEnd = new Vector2(.3f, .8f);
        });
        Ignored("child edge mode", () => topModel.edgeMode = VectorEdgeMode.Step);
        Ignored("child opacity/blend/clipping", () => {
            bottom.opacity = 0; bottom.blendMode = BlendMode.Multiply; bottom.clippingMask = true;
        });

        model.style.strokeWidth = 10;
        Different(t, baseline, fixture.Render(t), "group stroke width changes paint geometry");
        Prepared("group stroke width");
        t.Equal(cache.Count + 1, Count(result, "CoverageBuildCount"), "group stroke rebuilds coverage only");
        var before = fixture.Render(t);
        topModel.shapes[0].contours[0].points[1].position = new Vector2(.96f, .1f);
        Different(t, before, fixture.Render(t), "actual child geometry changes Boolean image");
        t.Equal(builds + 1, Count(model, "GeometryBuildCount"), "actual child geometry rebuilds Boolean result");
        builds = Count(model, "GeometryBuildCount");
        before = fixture.Render(t);
        top.transform.positionF = new Vector2(-9, 5);
        Different(t, before, fixture.Render(t), "child transform changes Boolean image");
        t.Equal(builds + 1, Count(model, "GeometryBuildCount"), "child transform rebuilds Boolean result");
        builds = Count(model, "GeometryBuildCount");
        before = fixture.Render(t); top.booleanOperation = VectorBooleanOperation.Subtract;
        Different(t, before, fixture.Render(t), "operation changes Boolean image");
        t.Equal(builds + 1, Count(model, "GeometryBuildCount"), "operation rebuilds Boolean result");
    }

    static void CombineOrder(TestContext t)
    {
        using var fixture = new Fixture();
        var model = new BooleanGroupLayerBehaviour { referenceSize = new Vector2(Width, Height) };
        var group = fixture.Add(model);
        var top = Operand(Vector(Rect(.4f, .1f, .85f, .7f)));
        top.booleanOperation = VectorBooleanOperation.Combine;
        var bottom = Operand(Vector(Rect(.15f, .2f, .6f, .8f)));
        group.children.Add(top); group.children.Add(bottom);
        model.style.fillColor = new Color(1, 0, 0, .5f);
        model.style.stroke = true; model.style.strokeWidth = 6;
        model.style.strokeColor = new Color(0, 0, 1, .5f);
        fixture.Render(t);
        var result = Result(model); var cache = new CacheStamp(result);
        int builds = Count(model, "GeometryBuildCount");
        t.Equal(2, result.shapes.Count, "Combine prepares two separate figures");

        void Check(string label)
        {
            var pixels = fixture.Render(t);
            cache.Unchanged(t, result, label);
            t.True(ReferenceEquals(result, Result(model)), label + " keeps Combine result");
            t.Equal(builds, Count(model, "GeometryBuildCount"), label + " keeps Combine geometry");
            Color fill = model.style.fillColor, stroke = model.style.strokeColor;
            Near(t, fill, pixels[30 * Width + 20], label + " lone fill");
            Near(t, Over(fill, fill), pixels[30 * Width + 48], label + " two translucent fills");
            Near(t, Over(stroke, Over(fill, fill)), pixels[30 * Width + 40], label + " top stroke over both fills");
            // The top fill must cover the bottom stroke. A global fills-then-strokes pass fails here.
            Near(t, Over(fill, Over(stroke, fill)), pixels[30 * Width + 56], label + " top fill over bottom stroke");
            t.Near(0, pixels[0].a, .0001, label + " transparent background");
        }
        Check("Combine baseline");
        model.style.fillColor.a = .25f; model.style.strokeColor.a = .6f;
        Check("Combine alpha edit");
        model.style.fillColor.a = .7f; model.style.strokeColor.a = .15f;
        Check("Combine second alpha edit");
    }

    static Layer Operand(VectorLayerBehaviour model)
    {
        var layer = new Layer(model);
        // Fixed source frame: ComposeCanvas must not rebase fixture coordinates with FitFrame.
        layer.transform.tiling = TransformTilingMode.Clip;
        return layer;
    }

    static VectorLayerBehaviour Vector(VectorContour contour)
    {
        var shape = new VectorShape { id = "figure", fillColor = Color.red, stroke = true,
            strokeColor = Color.blue, strokeWidth = 8 };
        shape.contours.Add(contour);
        return new VectorLayerBehaviour { referenceSize = new Vector2(Width, Height), shapes = new List<VectorShape> { shape } };
    }

    static VectorContour Rect(float x0, float y0, float x1, float y1)
    {
        var contour = new VectorContour { id = "contour" };
        var points = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
        for (int i = 0; i < points.Length; i++) contour.points.Add(new VectorPoint(points[i]) { id = "point-" + i });
        return contour;
    }

    static WhimTexGradient Ramp(bool reverse = false)
    {
        var gradient = new WhimTexGradient { Mode = WhimTexGradientMode.Classic, ColorSpace = ColorSpace.Linear };
        SetRamp(gradient, reverse ? Color.blue : Color.red, reverse ? Color.yellow : Color.green, .8f, .6f);
        return gradient;
    }
    static void SetRamp(WhimTexGradient gradient, Color a, Color b, float alphaA, float alphaB) => gradient.SetKeys(
        new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
        new[] { new GradientAlphaKey(alphaA, 0), new GradientAlphaKey(alphaB, 1) });

    // Independent coordinates and CPU palette evaluation; no GPU-paint or rasterizer helper is called.
    static Color Paint(VectorShape shape, bool stroke, int x, int y)
    {
        var mode = stroke ? shape.strokeMode : shape.fillMode;
        if (mode == VectorFillMode.Solid) return Decode(stroke ? shape.strokeColor : shape.fillColor);
        var start = stroke ? shape.strokeGradientStart : shape.gradientStart;
        var end = stroke ? shape.strokeGradientEnd : shape.gradientEnd;
        double ax = (end.x - start.x) * Width, ay = (end.y - start.y) * Height;
        double dx = x + .5 - start.x * Width, dy = y + .5 - start.y * Height;
        double length2 = ax * ax + ay * ay, q = 0;
        if (length2 > 1e-20)
        {
            double along = (dx * ax + dy * ay) / length2, across = (dy * ax - dx * ay) / length2;
            switch (mode)
            {
                case VectorFillMode.LinearGradient: q = along; break;
                case VectorFillMode.RadialGradient: q = Math.Sqrt((dx * dx + dy * dy) / length2); break;
                case VectorFillMode.SquareGradient: q = Math.Max(Math.Abs(along), Math.Abs(across)); break;
                case VectorFillMode.DiamondGradient: q = Math.Abs(along) + Math.Abs(across); break;
                case VectorFillMode.CircularGradient:
                    double turn = Math.Atan2(across, along) / (Math.PI * 2);
                    q = (turn - Math.Floor(turn)) * (stroke ? shape.strokeGradientRepetitions : shape.gradientRepetitions); break;
            }
        }
        var gradient = stroke ? shape.strokeGradient : shape.gradient;
        var color = gradient.Evaluate((float)q);
        return gradient.ColorSpace == ColorSpace.Gamma ? Decode(color) : color;
    }

    static Color Decode(Color color) => new Color(Mathf.GammaToLinearSpace(color.r),
        Mathf.GammaToLinearSpace(color.g), Mathf.GammaToLinearSpace(color.b), color.a);

    // Arguments are foreground, background; colors are straight-alpha linear RGB.
    static Color Over(Color front, Color back)
    {
        float alpha = front.a + back.a * (1 - front.a);
        if (alpha == 0) return Color.clear;
        var color = (front * front.a + back * (back.a * (1 - front.a))) / alpha;
        color.a = alpha; return color;
    }

    static void Near(TestContext t, Color expected, Color actual, string label)
    { for (int c = 0; c < 4; c++) t.Near(expected[c], actual[c], .004, label + " channel " + c); }
    static void Same(TestContext t, Color[] expected, Color[] actual, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " size");
        double max = 0;
        for (int i = 0; i < expected.Length; i++) for (int c = 0; c < 4; c++)
        {
            double difference = Math.Abs(expected[i][c] - actual[i][c]);
            if (double.IsNaN(difference) || double.IsInfinity(difference)) { max = double.NaN; break; }
            max = Math.Max(max, difference);
        }
        t.Near(0, max, .00001, label + " all pixels");
    }
    static void Different(TestContext t, Color[] before, Color[] after, string label)
    {
        if (before.Length != after.Length) { t.True(true, label + " dimensions"); return; }
        int changed = 0;
        for (int i = 0; i < before.Length; i++)
            if (Math.Abs(before[i].r - after[i].r) + Math.Abs(before[i].g - after[i].g) +
                Math.Abs(before[i].b - after[i].b) + Math.Abs(before[i].a - after[i].a) > .01) changed++;
        t.True(changed > 4, label + ": changed pixels=" + changed);
    }

    static int Count(object model, string name)
    {
        var property = model.GetType().GetProperty(name, Hidden);
        if (property != null) return (int)property.GetValue(model);
        var field = model.GetType().GetField(name, Hidden);
        if (field != null) return (int)field.GetValue(model);
        throw new InvalidOperationException("Missing WhimTex cache diagnostic: " + model.GetType().Name + "." + name);
    }
    static VectorLayerBehaviour Result(BooleanGroupLayerBehaviour model) =>
        (VectorLayerBehaviour)typeof(BooleanGroupLayerBehaviour).GetField("result", Hidden).GetValue(model);

    readonly struct CacheStamp
    {
        internal readonly object Identity;
        internal readonly int Count;
        readonly object[] entries;
        readonly Texture2D[] textures;
        internal CacheStamp(VectorLayerBehaviour model)
        {
            var field = typeof(VectorLayerBehaviour).GetField("coverage", Hidden);
            if (field == null) throw new InvalidOperationException("Missing VectorLayerBehaviour.coverage cache field.");
            Identity = field.GetValue(model); Count = VectorPaintCacheTests.Count(model, "CoverageBuildCount");
            var list = Identity as System.Collections.IList;
            if (list == null || list.Count == 0) throw new InvalidOperationException("GPU coverage was not prepared; this case requires the supported GPU paint path.");
            entries = new object[list.Count]; textures = new Texture2D[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                entries[i] = list[i];
                textures[i] = (Texture2D)entries[i].GetType().GetField("texture", Hidden).GetValue(entries[i]);
            }
        }
        internal void Unchanged(TestContext t, VectorLayerBehaviour model, string label)
        {
            var current = new CacheStamp(model);
            t.Equal(Count, current.Count, label + " does not rebuild coverage");
            t.True(Identity != null && ReferenceEquals(Identity, current.Identity), label + " retains coverage identity");
            t.Equal(entries.Length, current.entries.Length, label + " coverage entry count");
            for (int i = 0; i < entries.Length; i++)
            {
                t.True(ReferenceEquals(entries[i], current.entries[i]), label + " coverage entry " + i);
                t.True(textures[i] != null && ReferenceEquals(textures[i], current.textures[i]), label + " coverage texture " + i);
            }
        }
    }

    sealed class Fixture : IDisposable
    {
        internal readonly WhimTexDocument Document;
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        internal Fixture()
        {
            Document = ScriptableObject.CreateInstance<WhimTexDocument>();
            Document.hideFlags = HideFlags.HideAndDontSave;
            Document.width = Width; Document.height = Height;
            Document.outputPrecision = WhimTexOutputPrecision.Float32;
        }
        internal Layer Add(LayerBehaviour behaviour)
        {
            var layer = behaviour is VectorLayerBehaviour vector ? Operand(vector) : new Layer(behaviour);
            layer.colorRange = LayerColorRange.HDR; layer.blendRange = LayerBlendRange.HDR;
            Document.layers.Add(layer); return layer;
        }
        internal Color[] Render(TestContext t)
        {
            Texture2D image = null;
            try
            {
                image = Document.ComposeCanvas();
                t.Equal(Document.width, image.width, "Rendered width");
                t.Equal(Document.height, image.height, "Rendered height");
                t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Render restores caller state");
                return image.GetPixels();
            }
            finally { if (image != null) Object.DestroyImmediate(image); }
        }
        public void Dispose()
        {
            try { Object.DestroyImmediate(Document); }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
    }
}
