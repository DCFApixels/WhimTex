using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;
using Shape = DCFApixels.WhimTex.ShapeLayerBehaviour;

// Owned transient documents/textures; only diagnostic PNGs under project Temp.
public static class ShapeStrokeTests
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    public static string Run() => TestContext.Run("Shared Shape strokes, Feather and pixel metrics", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 128;
        var shape = new Shape { fill = false, stroke = true, strokeWidth = 8, strokeJoin = VectorStrokeJoin.Miter,
            strokePosition = Shape.StrokePosition.Center, strokeColor = Color.white, edgeMode = Shape.EdgeMode.Step };
        var layer = new Layer(shape); doc.layers.Add(layer);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var sentinel = new RenderTexture(8, 8, 0) { hideFlags = HideFlags.HideAndDontSave }; sentinel.Create();
        Color[] Render()
        {
            var image = doc.ComposeCanvas();
            try
            {
                var pixels = image.GetPixels();
                foreach (var p in pixels) t.True(float.IsFinite(p.a + p.r + p.g + p.b) && p.a >= 0 && p.a <= 1.0001f, "Finite bounded pixels");
                return pixels;
            }
            finally { Object.DestroyImmediate(image); }
        }
        float At(Color[] pixels, int x, int y) => pixels[y * 128 + x].a;
        try
        {
            foreach (VectorStrokeJoin join in Enum.GetValues(typeof(VectorStrokeJoin)))
            {
                shape.strokeJoin = join; var pixels = Render();
                t.Near(join == VectorStrokeJoin.Miter ? 1 : 0, At(pixels, 28, 28), .001, "Join outer tip " + join);
                t.Near(join == VectorStrokeJoin.Bevel ? 0 : 1, At(pixels, 29, 29), .001, "Join corner cut " + join);
            }
            shape.strokeJoin = VectorStrokeJoin.Miter; shape.miterLimit = 1;
            t.Near(0, At(Render(), 29, 29), .001, "Miter limit bevel fallback"); shape.miterLimit = 4;
            foreach (var pos in new[] { Shape.StrokePosition.Inside, Shape.StrokePosition.Center, Shape.StrokePosition.Outside })
            {
                shape.strokePosition = pos; var pixels = Render();
                t.Near(pos == Shape.StrokePosition.Inside ? 0 : 1, At(pixels, 98, 64), .001, "Outside half " + pos);
                t.Near(pos == Shape.StrokePosition.Outside ? 0 : 1, At(pixels, 93, 64), .001, "Inside half " + pos);
            }
            shape.strokePosition = Shape.StrokePosition.Center;
            shape.strokeWidth = 4; shape.dashPattern = new List<float> { 2, 1 };
            var dashed = Render();
            t.Near(1, At(dashed, 36, 95), .001, "Dash starts at Shape contour start");
            t.Near(0, At(dashed, 42, 95), .001, "Width-relative gap");
            shape.strokeWidth = 8; dashed = Render();
            t.Near(1, At(dashed, 42, 95), .001, "Width doubles dash length");
            t.Near(0, At(dashed, 52, 95), .001, "Width doubles gap length");
            shape.strokeWidth = 4; shape.dashOffset = -4;
            t.Near(0, At(Render(), 34, 95), .001, "Negative offset measured in pixels");
            shape.dashOffset = 0;
            foreach (VectorStrokeCap cap in Enum.GetValues(typeof(VectorStrokeCap)))
            {
                shape.strokeCap = cap;
                t.Near(cap == VectorStrokeCap.Butt ? 0 : 1, At(Render(), 41, 95), .001, "Dash cap extent " + cap);
            }
            // A single long on interval exercises the styled path while retaining the analytic
            // straight-edge oracle. Feather must soften both boundaries, including inside strokes.
            shape.strokeWidth = 12; shape.dashPattern = new List<float> { 1000, 1 }; shape.strokeCap = VectorStrokeCap.Butt;
            shape.strokePosition = Shape.StrokePosition.Inside; shape.edgeMode = Shape.EdgeMode.Antialiased; shape.feather = 16;
            foreach (Shape.FeatherPosition pos in Enum.GetValues(typeof(Shape.FeatherPosition)))
            {
                shape.featherPosition = pos; var pixels = Render();
                for (int x = 70; x < 120; x++)
                {
                    float d = x + .5f - 96;
                    t.Near(Mathf.Min(Coverage(d,16,pos), Coverage(-d-12,16,pos)), At(pixels,x,64), .002, "Styled Feather " + pos);
                }
            }
            shape.featherPosition = Shape.FeatherPosition.Centered;
            foreach (Vector2 scale in new[] { new Vector2(.75f, .25f), new Vector2(.375f, .75f) })
            {
                layer.transform.scaleF = scale; var pixels = Render(); float right = 64 + scale.x * 64;
                for (int x = Mathf.CeilToInt(right - 15); x < Math.Min(128,right + 12); x++)
                {
                    float d = x + .5f - right;
                    t.Near(Mathf.Min(Coverage(d,16,shape.featherPosition), Coverage(-d-12,16,shape.featherPosition)), At(pixels,x,64), .002, "Shape width and Feather stay in document pixels " + scale);
                }
            }
            // Compare the unchanged analytic path and styled path along straight flanks.
            // Perspective intentionally retains the existing local metric, not a new
            // constant-screen-width rule. Corners are excluded because joins may differ.
            var rotated = TextureTransform.Default; rotated.scaleF = new Vector2(.65f,.45f); rotated.rotation = 27;
            var reflected = TextureTransform.Default; reflected.scaleF = new Vector2(-.65f,.5f); reflected.rotation = -18;
            var perspective = TextureTransform.Default;
            t.True(perspective.TrySetMatrix(new ProjectiveMatrix { m00=.6,m01=.08,m02=.15,m10=.03,m11=.65,m12=.15,m20=.2,m21=-.12,m22=1 }), "Valid projective test transform");
            shape.feather = 4;
            foreach (var transform in new[] { rotated, reflected, perspective })
            foreach (float pixelScale in new[] { 1f, 1.5f })
            {
                doc.pixelScale = pixelScale; layer.transform = transform;
                shape.strokeJoin = VectorStrokeJoin.Round; shape.dashPattern.Clear(); var analytic = Render();
                shape.strokeJoin = VectorStrokeJoin.Miter; var styled = Render();
                int samples = 0;
                for (int y=0;y<128;y++) for (int x=0;x<128;x++)
                {
                    Vector2 uv = transform.Unmap(new Vector2((x+.5f)/128,(y+.5f)/128),Vector2.one*128);
                    if (Mathf.Abs(uv.x-.5f) >= .15f && Mathf.Abs(uv.y-.5f) >= .15f) continue;
                    t.Near(analytic[y*128+x].a,styled[y*128+x].a,.002,"Transformed width/Feather preserve analytic metric"); samples++;
                }
                t.True(samples>500,"Enough transformed flank samples");
            }
            doc.pixelScale = 1; layer.transform = TextureTransform.Default; layer.transform.scaleF = Vector2.one * .5f;
            shape.feather = 0; shape.edgeMode = Shape.EdgeMode.Step;
            shape.fill = true; shape.fillColor = Color.red; shape.strokeColor = new Color(0,0,1,.5f);
            var translucent = Render(); t.Near(.5, At(translucent,93,64), .001, "Shape stroke still replaces fill overlap");
            t.Near(1, At(translucent,64,64), .001, "Uncovered fill retained");
            shape.fill = false; shape.strokeColor = Color.white; shape.strokeWidth = 4;
            shape.dashPattern = new List<float> { 3, 2 }; shape.edgeMode = Shape.EdgeMode.Antialiased;
            foreach (Shape.ShapeKind kind in Enum.GetValues(typeof(Shape.ShapeKind)))
            {
                shape.kind = kind; shape.startAngle = 17; shape.sweepAngle = 239;
                foreach (VectorStrokeJoin join in Enum.GetValues(typeof(VectorStrokeJoin)))
                {
                    shape.strokeJoin = join; var pixels = Render(); double sum = 0; foreach (var p in pixels) sum += p.a;
                    t.True(sum > 20, "Visible styled stroke " + kind + "/" + join);
                }
            }
            shape.kind = Shape.ShapeKind.Arc; shape.sweepAngle = 360; shape.arcThickness = 14;
            t.Near(0, At(Render(),64,64), .001, "Styled full ring retains hole");
            shape.arcThickness = 96; Render(); shape.sweepAngle = 359; Render();
            shape.arcThickness = 0; t.Near(0, Sum(Render()), .001, "Zero arc body is empty");
            shape.arcThickness = 12; shape.sweepAngle = 0; t.Near(0, Sum(Render()), .001, "Zero arc sweep is empty");
            shape.kind = Shape.ShapeKind.Rectangle; shape.strokeJoin = VectorStrokeJoin.Miter; shape.strokePosition = Shape.StrokePosition.Center;
            RenderTexture.active = sentinel; GL.sRGBWrite = true;
            var baseline = Render(); t.True(RenderTexture.active == sentinel && GL.sRGBWrite, "Caller render state retained");
            var thumb = shape.GetPreviewTexture(64); t.True(thumb != null && Sum(thumb.GetPixels()) > 0, "Styled thumbnail");
            Equal(t,baseline,Render(),"Thumbnail does not contaminate full-size geometry cache");
            var export = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdPixels",Hidden).Invoke(doc,new object[]{layer,false});
            try { Equal(t,baseline,export.GetPixels(),"Layered export retains strokes"); } finally { Object.DestroyImmediate(export); }
            var group = new Layer(new GroupLayerBehaviour()); group.children.Add(layer); doc.layers.Clear(); doc.layers.Add(group);
            Equal(t,baseline,Render(),"Group retains styled strokes");
            var clip = new Layer(new ColorFillLayerBehaviour { color = Color.red }) { clippingMask = true }; group.children.Insert(0,clip);
            var clipped = Render(); for(int i=0;i<baseline.Length;i++) t.Near(baseline[i].a,clipped[i].a,.001,"Clipping retains styled alpha");
            group.children.Remove(clip);
            var target = new Layer(new BlurLayerBehaviour { radius=0, strength=0, inputMode=EffectInputMode.Specific, TargetLayerId=group.Id });
            doc.layers.Insert(0,target); group.enabled=false;
            Equal(t,baseline,Render(),"Hidden group Target retains styled strokes");
            doc.layers.Remove(target); group.enabled=true;
            var cached = (IDisposable)typeof(Shape).GetField("strokeCache",Hidden).GetValue(shape);
            var texture = (Texture2D)cached.GetType().GetField("tree",Hidden).GetValue(cached);
            t.True(texture != null, "Styled geometry has an owned texture");
            layer.SetBehaviour(new Shape()); t.True(texture == null,"Replacing Shape releases owned stroke texture");
            t.True(!UnityEditor.ShaderUtil.ShaderHasError(Shader.Find("Hidden/WhimTex/Shape")), "Shape shader compiled");
        }
        finally { RenderTexture.active=previous; GL.sRGBWrite=srgb; Object.DestroyImmediate(doc); sentinel.Release(); Object.DestroyImmediate(sentinel); }
    });

    static double Sum(Color[] pixels) { double sum=0; foreach(var p in pixels) sum+=p.a; return sum; }
    static void Equal(TestContext t, Color[] a, Color[] b, string label)
    { t.Equal(a.Length,b.Length,label+" size"); for(int i=0;i<a.Length;i++) t.True((a[i]-b[i]).maxColorComponent < .002f && (b[i]-a[i]).maxColorComponent < .002f && Mathf.Abs(a[i].a-b[i].a)<.002f,label); }
    static float Coverage(float d,float width,Shape.FeatherPosition position)
    { float shift=position==Shape.FeatherPosition.Inside?0:position==Shape.FeatherPosition.Outside?1:.5f; float t=Mathf.Clamp01((d+width*(1-shift))/width); return 1-t*t*(3-2*t); }
}
