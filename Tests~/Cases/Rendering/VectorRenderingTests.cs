// Support: Tests~/Framework/TestApi.cs. Not registered or executed by this change.
// Uses transient documents/textures only. A graphics-enabled Editor is required.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorRenderingTests
{
    public static string Run() => TestContext.Run("VectorRenderingTests", context =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = 96; document.height = 64;
        var vector = new VectorLayerBehaviour { referenceSize = new Vector2(96, 64) };
        var layer = new Layer(vector); document.layers.Add(layer);
        // This rasterizer fixture replaces its absolute 0..1 source geometry repeatedly.
        // Keep a meaningful clip frame; automatic geometry-fit frames have their own test.
        layer.transform.tiling = TransformTilingMode.Clip;
        RenderTexture previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var sentinel = new RenderTexture(8, 8, 0) { hideFlags = HideFlags.HideAndDontSave };
        sentinel.Create();
        ShaderFX fxEffect = null;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Render(Action<Texture2D> inspect)
        {
            Texture2D result = document.ComposeCanvas();
            try { inspect(result); }
            finally { Object.DestroyImmediate(result); }
        }
        void Set(VectorShape shape) { vector.shapes.Clear(); vector.shapes.Add(shape); }
        try
        {
            var shape = new VectorShape();
            shape.contours.Add(Rect(.1f, .1f, .9f, .9f)); shape.contours.Add(Rect(.35f, .35f, .65f, .65f));
            Set(shape);
            Render(t => { context.Near(0, t.GetPixel(48, 32).a, .001, "EvenOdd compound hole"); context.Near(1, t.GetPixel(20, 32).a, .001, "compound shell"); });
            shape.fillRule = VectorFillRule.NonZero;
            Render(t => context.Near(1, t.GetPixel(48, 32).a, .001, "same-winding NonZero interior"));
            shape.contours[1].points.Reverse();
            Render(t => context.Near(0, t.GetPixel(48, 32).a, .001, "opposite-winding NonZero hole"));

            shape = new VectorShape(); shape.contours.Add(Path(true, new Vector2(.1f,.1f), new Vector2(.9f,.9f), new Vector2(.1f,.9f), new Vector2(.9f,.1f))); Set(shape);
            Render(t => { context.Near(1, t.GetPixel(48, 12).a, .001, "self intersection lower lobe"); context.Near(1, t.GetPixel(48, 52).a, .001, "self intersection upper lobe"); context.Near(0, t.GetPixel(12, 32).a, .001, "self intersection outside"); });

            shape = new VectorShape { fillMode = VectorFillMode.None, stroke = true, strokeWidth = 8 };
            shape.contours.Add(Rect(.25f, .25f, .75f, .75f)); Set(shape); vector.edgeMode = VectorEdgeMode.Step;
            shape.strokePosition = VectorStrokePosition.Inside;
            Render(t => { context.Near(1, t.GetPixel(25, 32).a, .001, "inside stroke inside edge"); context.Near(0, t.GetPixel(22, 32).a, .001, "inside stroke excludes outside"); });
            shape.strokePosition = VectorStrokePosition.Outside;
            Render(t => { context.Near(1, t.GetPixel(22, 32).a, .001, "outside stroke outside edge"); context.Near(0, t.GetPixel(25, 32).a, .001, "outside stroke excludes interior"); });
            shape.strokePosition = VectorStrokePosition.Center;
            Render(t => { context.Near(1, t.GetPixel(22, 32).a, .001, "center stroke outer half"); context.Near(1, t.GetPixel(25, 32).a, .001, "center stroke inner half"); });
            shape.join = VectorStrokeJoin.Miter;
            Render(t => context.Near(1, t.GetPixel(20, 12).a, .001, "miter reaches square outer corner"));
            shape.join = VectorStrokeJoin.Round;
            Render(t => { context.Near(1, t.GetPixel(21, 13).a, .001, "round join arc interior"); context.Near(0, t.GetPixel(20, 12).a, .001, "round join removes square tip"); });
            shape.join = VectorStrokeJoin.Bevel;
            Render(t => context.Near(0, t.GetPixel(21, 13).a, .001, "bevel cuts corner on straight chord"));
            shape.join = VectorStrokeJoin.Miter; shape.miterLimit = 1;
            Render(t => context.Near(0, t.GetPixel(21, 13).a, .001, "miter limit falls back to bevel"));
            shape.miterLimit = 4;

            shape.contours.Clear(); shape.contours.Add(Path(false, new Vector2(.25f,.5f), new Vector2(.75f,.5f)));
            shape.strokePosition = VectorStrokePosition.Inside; shape.cap = VectorStrokeCap.Butt;
            Render(t => { context.Near(0, t.GetPixel(22, 32).a, .001, "butt cap terminates at anchor"); context.Near(1, t.GetPixel(40, 29).a, .001, "open inside setting remains centered"); });
            shape.cap = VectorStrokeCap.Square;
            Render(t => context.Near(1, t.GetPixel(21, 32).a, .001, "square cap extends by half-width"));
            shape.cap = VectorStrokeCap.Round;
            Render(t => { context.Near(1, t.GetPixel(21, 32).a, .001, "round cap tip"); context.Near(0, t.GetPixel(20, 28).a, .001, "round cap excludes square corner"); });
            shape.cap = VectorStrokeCap.Butt; shape.dashPattern.AddRange(new[] { 8f, 8f });
            Render(t => { context.Near(1, t.GetPixel(26, 32).a, .001, "dash on"); context.Near(0, t.GetPixel(34, 32).a, .001, "dash off"); });
            shape.dashOffset = 8;
            Render(t => { context.Near(0, t.GetPixel(26, 32).a, .001, "dash offset off"); context.Near(1, t.GetPixel(34, 32).a, .001, "dash offset on"); });

            shape.dashPatternRelative = true; shape.dashPattern = new List<float> { 2, 1 };
            shape.strokeWidth = 4; shape.dashOffset = 0;
            Render(t => { context.Near(1, t.GetPixel(28, 32).a, .001, "relative dash at width 4"); context.Near(0, t.GetPixel(34, 32).a, .001, "relative gap at width 4"); });
            shape.strokeWidth = 8;
            Render(t => { context.Near(1, t.GetPixel(34, 32).a, .001, "doubling width doubles dash length"); context.Near(0, t.GetPixel(44, 32).a, .001, "doubling width doubles gap length"); });
            context.True(shape.dashPattern[0] == 2 && shape.dashPattern[1] == 1, "Rendering preserves multipliers");
            shape.strokeWidth = 4; shape.dashOffset = -4;
            Render(t => { context.Near(0, t.GetPixel(26, 32).a, .001, "negative offset remains pixels"); context.Near(1, t.GetPixel(30, 32).a, .001, "negative offset wraps relative period"); });
            shape.strokeWidth = 0;
            Render(t => { foreach (var pixel in t.GetPixels()) context.Near(0, pixel.a, .001, "zero-width dashes are empty"); });
            shape.strokeWidth = 4; shape.dashOffset = 0;
            shape.contours.Clear(); shape.contours.Add(Rect(.25f, .25f, .75f, .75f));
            foreach (var position in new[] { VectorStrokePosition.Center, VectorStrokePosition.Inside, VectorStrokePosition.Outside })
            {
                shape.strokePosition = position;
                int shift = position == VectorStrokePosition.Center ? 0 : position == VectorStrokePosition.Inside ? 2 : -2;
                int y = 16 + shift;
                Render(t => { context.Near(1, t.GetPixel(28 + shift, y).a, .001, "relative dash length on shifted trajectory " + position); context.Near(0, t.GetPixel(34 + shift, y).a, .001, "relative gap length on shifted trajectory " + position); });
            }
            shape.strokeWidth = 8; shape.dashPatternRelative = false;

            // A dash spanning the closed seam must have a join there, not two capped ends.
            shape.dashPattern.Clear(); shape.dashPattern.AddRange(new[] { 60f, 20f }); shape.dashOffset = 10;
            shape.contours.Clear(); shape.contours.Add(Rect(.25f, .25f, .75f, .75f));
            shape.strokePosition = VectorStrokePosition.Center; shape.join = VectorStrokeJoin.Miter;
            Render(t => context.Near(1, t.GetPixel(21, 13).a, .001, "closed dash seam keeps miter corner"));
            shape.dashPattern.Clear(); shape.dashPattern.AddRange(new[] { 70f, 20f }); shape.dashOffset = 0;
            Render(t => context.Near(1, t.GetPixel(21, 13).a, .001, "dash ending exactly at seam joins first dash"));

            // Stroke union is sampled once at a crossing, including when the stroke is translucent.
            shape.dashPattern.Clear(); shape.dashOffset = 0; shape.strokeColor = new Color(0, 0, 1, .5f);
            shape.contours.Clear(); shape.contours.Add(Path(false, new Vector2(.2f,.2f), new Vector2(.8f,.8f)));
            shape.contours.Add(Path(false, new Vector2(.2f,.8f), new Vector2(.8f,.2f)));
            Render(t => context.Near(.5, t.GetPixel(48, 32).a, .015, "stroke intersections have no repeated alpha"));

            shape = new VectorShape { fillColor = new Color(1, 0, 0, .5f), stroke = true, strokeWidth = 8,
                strokeColor = new Color(0, 0, 1, .5f), strokePosition = VectorStrokePosition.Inside };
            shape.contours.Add(Rect(.25f, .25f, .75f, .75f)); Set(shape);
            Render(t => { context.Near(.75, t.GetPixel(25, 32).a, .015, "stroke is source-over fill"); context.Near(.5, t.GetPixel(48, 32).a, .015, "interior retains fill alpha"); });

            shape.stroke = false; shape.fillColor = Color.white;
            shape.fillMode = VectorFillMode.LinearGradient;
            shape.gradient = new WhimTexGradient { ColorMode = WhimTexGradientColorMode.Linear, ColorSpace = ColorSpace.Linear };
            shape.gradientStart = new Vector2(.25f,.5f); shape.gradientEnd = new Vector2(.75f,.5f);
            Render(t => context.True(t.GetPixel(68, 32).r > t.GetPixel(28, 32).r + .6f, "linear gradient uses shape-local anchors"));
            shape.fillMode = VectorFillMode.RadialGradient; shape.gradientStart = new Vector2(.5f,.5f);
            Render(t => context.True(t.GetPixel(68, 32).r > t.GetPixel(48, 32).r + .6f, "radial gradient uses reference-pixel radius"));

            shape.fillMode = VectorFillMode.Solid;
            shape.contours[0].points[0].position += new Vector2(.037f,.017f);
            vector.edgeMode = VectorEdgeMode.Antialiased;
            Render(t => { bool fractional = false; foreach (var c in t.GetPixels()) fractional |= c.a > .01f && c.a < .99f; context.True(fractional, "AA yields partial edge coverage"); });
            vector.edgeMode = VectorEdgeMode.Step;
            layer.transform.rotation = 17;
            Render(t => { foreach (var c in t.GetPixels()) context.True(c.a < .001f || c.a > .999f, "Step is binary on transformed canvas grid"); });
            layer.transform.rotation = 0;
            layer.transform.position = new Vector2(12, 0);
            Render(t => { context.Near(1, t.GetPixel(78, 32).a, .001, "translation applied once"); context.Near(0, t.GetPixel(26, 32).a, .001, "old position vacated"); });
            layer.transform.position = Vector2.zero;

            // Direct edits and undo-like restoration must invalidate content caches without a revision token.
            shape.fillColor = Color.red; Render(t => context.Near(1, t.GetPixel(48, 32).r, .001, "direct style edit"));
            shape.fillColor = Color.blue; Render(t => context.Near(1, t.GetPixel(48, 32).b, .001, "second direct style edit"));
            shape.fillColor = Color.red; Render(t => context.Near(1, t.GetPixel(48, 32).r, .001, "restored style invalidates cache"));
            var thumbnail = vector.GetPreviewTexture(32);
            context.True(thumbnail != null && thumbnail.width == 32 && thumbnail.GetPixel(16,16).a > .99f, "ordinary thumbnail path");
            Render(t => context.Near(1, t.GetPixel(48, 32).r, .001, "full raster survives thumbnail resize"));

            RenderTexture.active = sentinel; GL.sRGBWrite = true;
            Render(t => context.Near(1, t.GetPixel(48,32).a, .001, "render with sentinel target"));
            context.True(RenderTexture.active == sentinel && GL.sRGBWrite, "caller target and sRGB state restored");
            var exported = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdPixels", flags).Invoke(document, new object[] { layer, false });
            try { context.True(exported.GetPixel(48,32).a > .99f, "ordinary layered export source"); }
            finally { Object.DestroyImmediate(exported); }

            var group = new Layer(new GroupLayerBehaviour()); group.children.Add(layer);
            document.layers.Clear(); document.layers.Add(group); group.transform.position = new Vector2(12,0);
            Render(t => { context.Near(1, t.GetPixel(78,32).a, .001, "group transform reaches vector source"); context.Near(0, t.GetPixel(26,32).a, .001, "group transform not doubled"); });
            group.transform.position = Vector2.zero;

            var clipping = new VectorLayerBehaviour { referenceSize = vector.referenceSize };
            var clipShape = new VectorShape { fillColor = Color.blue }; clipShape.contours.Add(Rect(0,0,1,1)); clipping.shapes.Add(clipShape);
            var clipLayer = new Layer(clipping) { clippingMask = true };
            group.children.Insert(0, clipLayer);
            Render(t => { context.Near(1, t.GetPixel(48,32).b, .001, "vector clipping uses underlying coverage"); context.Near(0, t.GetPixel(2,2).a, .001, "clipping removes outside source"); });

            fxEffect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, "float4 ApplyFX(PinnedUV _pinnedUv, float4 color){ float2 uv = _pinnedUv;return float4(color.bbb,color.a);}", new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fxEffect, null);
            clipLayer.fx.Add(fxEffect);
            Render(t => { Color c = t.GetPixel(48,32); context.True(c.r > .99f && c.g > .99f && c.b > .99f, "vector FX runs before ordinary clipping"); });
            clipLayer.fx.Clear();

            var target = new BlurLayerBehaviour { radius = 0, strength = 0, inputMode = EffectInputMode.Specific, TargetLayerId = group.Id };
            var targetLayer = new Layer(target);
            document.layers.Insert(0, targetLayer);
            group.enabled = false; group.transform.position = new Vector2(12, 0);
            Render(t => { context.Near(1, t.GetPixel(78,32).b, .001, "hidden group supplies Target color input"); context.Near(0, t.GetPixel(26,32).a, .001, "Target group transform is applied once"); });
            target.TargetLayerId = layer.Id;
            Render(t => { context.Near(1, t.GetPixel(78,32).r, .001, "Target can resolve vector leaf inside hidden group"); context.Near(0, t.GetPixel(26,32).a, .001, "Target leaf retains parent transform"); });
            targetLayer.enabled = false;
            Render(t => context.Near(0, t.GetPixel(78,32).a, .001, "hidden Target source never leaks into composition"));

            // Probe every target/sRGB combination, including a source-validation exception.
            targetLayer.enabled = true;
            foreach (bool useSentinel in new[] { false, true }) foreach (bool writeSrgb in new[] { false, true })
            {
                RenderTexture expected = useSentinel ? sentinel : null;
                RenderTexture.active = expected; GL.sRGBWrite = writeSrgb;
                Render(t => context.True(t.GetPixel(78,32).a > .99f, "Target pixels during state probe"));
                context.True(RenderTexture.active == expected && GL.sRGBWrite == writeSrgb, "Target restores caller render state");
                shape.strokeWidth = -1;
                bool rejected = false;
                try { Render(_ => { }); } catch (InvalidOperationException) { rejected = true; }
                finally { shape.strokeWidth = 8; }
                context.True(rejected, "invalid Vector source has a diagnostic");
                context.True(RenderTexture.active == expected && GL.sRGBWrite == writeSrgb, "failed raster restores caller render state");
            }
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            Object.DestroyImmediate(document); sentinel.Release(); Object.DestroyImmediate(sentinel);
            if (fxEffect != null) Object.DestroyImmediate(fxEffect);
        }
    });

    private static VectorContour Rect(float left, float bottom, float right, float top) => Path(true,
        new Vector2(left,bottom), new Vector2(right,bottom), new Vector2(right,top), new Vector2(left,top));
    private static VectorContour Path(bool closed, params Vector2[] points)
    {
        var contour = new VectorContour { closed = closed };
        foreach (var p in points) contour.points.Add(new VectorPoint(p));
        return contour;
    }
}
