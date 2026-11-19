using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

// Managed geometry and one owned transient document. Reflection calls WhimTex internals only.
public static class MorphVectorGeometryTests
{
    private static readonly Vector2 Size = new Vector2(240, 120);
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Type Geometry = typeof(VectorShape).Assembly.GetType("DCFApixels.WhimTex.MorphVectorGeometry", true);

    private static object Prepare(Layer a, Layer b, params MorphAnchor[] pins) =>
        Geometry.GetMethod("Prepare", Internal).Invoke(null, new object[] { a, b, Size, pins });
    private static VectorLayerBehaviour Sample(object plan, float t) =>
        (VectorLayerBehaviour)plan.GetType().GetMethod("Sample", Internal).Invoke(plan, new object[] { t });
    private static VectorLayerBehaviour Build(Layer a, Layer b, float t) =>
        (VectorLayerBehaviour)Geometry.GetMethod("Build", Internal).Invoke(null, new object[] { a, b, Size, null, t });

    private static Layer LayerOf(params VectorContour[] paths)
    {
        var vector = new VectorLayerBehaviour { referenceSize = Size };
        vector.shapes.Add(new VectorShape { contours = new List<VectorContour>(paths) });
        return new Layer(vector);
    }

    private static VectorContour Square(float x, float y, float width, bool reverse = false)
    {
        var path = new VectorContour();
        foreach (Vector2 offset in new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up })
            path.points.Add(new VectorPoint(new Vector2(x,y) + offset*width));
        if (reverse) path.points.Reverse();
        return path;
    }

    private static void Near(TestContext t, Vector2 expected, Vector2 actual, string label) =>
        t.True(Vector2.Distance(expected, actual) < 2e-5f, label + " expected " + expected + " got " + actual);

    private static Vector2 Evaluate(VectorContour path, int s, float t)
    {
        var a = path.points[s]; var b = path.points[s+1];
        return VectorGeometry.EvaluateCubic(a.position, a.position+a.outTangent, b.position+b.inTangent, b.position, t);
    }

    public static string Run() => TestContext.Run("Morph vector geometry, local frame, pins and prepared snapshots", context =>
    {
        AnchoredCubic(context);
        Compound(context);
        Gradient(context);
        LocalFrame(context);
        Fallback(context);
    });

    private static void AnchoredCubic(TestContext check)
    {
        var path = new VectorContour { closed = false };
        path.points.Add(new VectorPoint(new Vector2(.1f,.2f)) { outTangent = new Vector2(.03f,.6f) });
        path.points.Add(new VectorPoint(new Vector2(.8f,.3f)) { inTangent = new Vector2(-.2f,.45f) });
        var reversed = path.Clone(false);
        reversed.points.Reverse();
        foreach (var p in reversed.points) (p.inTangent,p.outTangent) = (p.outTangent,p.inTangent);

        var a = LayerOf(path); var b = LayerOf(reversed);
        b.transform.positionF = new Vector2(24, 12);
        var shift = new Vector2(.1f,.1f);
        var sa = ((VectorLayerBehaviour)a.Behaviour).shapes[0]; var sb = ((VectorLayerBehaviour)b.Behaviour).shapes[0];
        a.opacity = 0; b.opacity = .1f;
        sa.stroke = sb.stroke = true; sa.strokeWidth = 2; sb.strokeWidth = 6;
        sa.fillColor = new Color(2,0,0,.2f); sb.fillColor = new Color(0,1,0,.8f);
        const float ua = .27f, ub = .68f, t = .35f;
        var middle = new MorphAnchor { from = Evaluate(path,0,ua), to = Evaluate(path,0,ub)+shift };
        var plan = Prepare(a,b, middle,
            new MorphAnchor { from = path.points[0].position, to = path.points[0].position+shift },
            new MorphAnchor { from = path.points[1].position, to = path.points[1].position+shift });
        check.True(plan != null, "Reversed open path honors all three anchors");
        middle.from = Vector2.one;
        b.transform.positionF = Vector2.zero; sb.strokeWidth = 99;
        var sample = Sample(plan,t); sample.Validate();
        var shape = sample.shapes[0]; var contour = shape.contours[0];
        check.True(!contour.closed && contour.points.Count == 3, "One interior pin creates two open cubic segments");
        check.Near(Mathf.Lerp(2,6,t), shape.strokeWidth, 1e-5, "Snapshot owns stroke width");
        check.Near(Mathf.Lerp(.2f,.8f,t), shape.fillColor.a, 1e-6, "Only paint alpha is interpolated");
        check.Near(Mathf.Lerp(2,0,t), shape.fillColor.r, 1e-6, "HDR fill interpolates");
        for (int s = 0; s < 2; s++)
            for (int i = 0; i <= 20; i++)
            {
                float v = i/20f;
                float x = s == 0 ? ua*v : Mathf.Lerp(ua,1,v);
                float y = s == 0 ? ub*v : Mathf.Lerp(ub,1,v);
                Near(check, Vector2.Lerp(Evaluate(path,0,x), Evaluate(path,0,y)+shift,t), Evaluate(contour,s,v),
                    "Subcurve controls preserve the full cubic");
            }
        contour.points[1].position = Vector2.zero;
        Near(check, Vector2.Lerp(Evaluate(path,0,ua), Evaluate(path,0,ub)+shift,t),
            Sample(plan,t).shapes[0].contours[0].points[1].position, "Repeated samples are independent");
        check.Equal(2, path.points.Count, "Sampling leaves original cubic intact");

        // Same cubic already split at a different authored boundary.
        var split = path.Clone(false);
        VectorGeometry.SplitSegment(split,0,.41f);
        var equal = Build(LayerOf(path), LayerOf(split), .5f).shapes[0].contours[0];
        check.Equal(3, equal.points.Count, "Different authored point counts share the split");
        for (int s = 0; s < 2; s++)
            for (int i = 0; i <= 8; i++)
            {
                // Compare to the locus: arc-length correspondence is numerically estimated.
                var p = Evaluate(equal,s,i/8f);
                check.True(VectorGeometry.TryHitSegment(path,p,Size,.03f,out _,out _,out _), "Identical curves retain their locus");
            }
    }

    private static void Compound(TestContext check)
    {
        var shell = Square(0,0,1); var left = Square(.1f,.25f,.16f,true); var right = Square(.74f,.25f,.16f,true);
        var open = new VectorContour { closed = false, points = new List<VectorPoint> {
            new VectorPoint(new Vector2(0,1.2f)), new VectorPoint(new Vector2(.8f,1.2f)) } };
        var a = LayerOf(shell,left,right,open);
        var b = LayerOf(right.Clone(false),open.Clone(false),shell.Clone(false),left.Clone(false));
        var natural = Build(a,b,.35f).shapes[0];
        check.Equal(4, natural.contours.Count, "Reordered compound keeps all contours");
        Near(check,left.points[0].position,natural.contours[1].points[0].position,"Hole stays in place");
        check.True(!natural.contours[3].closed,"An open stroke stays open");
        var crossing = new MorphAnchor { from = left.points[0].position, to = right.points[0].position };
        var pinned = Sample(Prepare(a,b,crossing),.5f).shapes[0];
        Near(check, (crossing.from+crossing.to)*.5f,pinned.contours[1].points[0].position,"Anchor overrides nearest hole assignment");
        var missing = Build(LayerOf(shell),LayerOf(shell.Clone(false),left.Clone(false)),.5f).shapes[0];
        check.Equal(2,missing.contours.Count,"A new hole grows independently");
        check.Near(.08,Vector2.Distance(missing.contours[1].points[0].position,missing.contours[1].points[1].position),1e-5,
            "Unmatched hole grows from zero size");
    }

    private static void Gradient(TestContext check)
    {
        var a = LayerOf(Square(0,0,1)); var b = LayerOf(Square(0,0,1));
        var left = ((VectorLayerBehaviour)a.Behaviour).shapes[0];
        var right = ((VectorLayerBehaviour)b.Behaviour).shapes[0];
        left.fillMode = right.fillMode = VectorFillMode.LinearGradient;
        left.gradientStart = right.gradientStart = new Vector2(.2f,.2f);
        left.gradientEnd = right.gradientEnd = new Vector2(.7f,.8f);
        void Constant(WhimTexGradient ramp, Color c)
        {
            ramp.SetKeys(new[] { new GradientColorKey(c,0), new GradientColorKey(c,1) },
                new[] { new GradientAlphaKey(c.a,0), new GradientAlphaKey(c.a,1) });
            ramp.WrapMode = WhimTexGradientWrapMode.Repeat;
        }
        Constant(left.gradient,new Color(1,0,0,.25f)); Constant(right.gradient,new Color(0,0,1,.75f));
        var m = ProjectiveMatrix.Identity; m.m01 = .4; m.m00 = 1.5;
        a.transform.TrySetMatrix(m);
        var plan = Prepare(a,b);
        var start = Sample(plan,0).shapes[0];
        Vector2 local = new Vector2(.33f,.58f), axis = Vector2.Scale(left.gradientEnd-left.gradientStart, Size);
        float expected = Vector2.Dot(Vector2.Scale(local-left.gradientStart, Size),axis)/axis.sqrMagnitude;
        axis = Vector2.Scale(start.gradientEnd-start.gradientStart,Size);
        float actual = Vector2.Dot(Vector2.Scale(a.transform.Map(local,Size)-start.gradientStart,Size),axis)/axis.sqrMagnitude;
        check.Near(expected,actual,1e-5,"Shear preserves the linear gradient coordinate");
        var middle = Sample(plan,.5f).shapes[0];
        Color color = middle.gradient.Evaluate(.25f);
        check.Near(.5,color.r,1e-5,"Palette red interpolates"); check.Near(.5,color.b,1e-5,"Palette blue interpolates");
        check.Near(.5,color.a,1e-5,"Palette alpha interpolates");
        check.Equal(WhimTexGradientWrapMode.Repeat,Sample(plan,0).shapes[0].gradient.WrapMode,"Ramp sampling preserves snapshot wrap");
    }

    private static void LocalFrame(TestContext check)
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        try
        {
            doc.width = (int)Size.x; doc.height = (int)Size.y;
            var parent = new Layer(new GroupLayerBehaviour());
            var primitive = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Rectangle };
            var a = new Layer(primitive); var b = LayerOf(Square(0,0,1));
            parent.transform.positionF = new Vector2(90,30); parent.transform.rotationF = 41;
            a.transform.positionF = new Vector2(-17,4); a.transform.rotationF = 27;
            a.transform.scaleF = new Vector2(.4f,.65f);
            b.transform.scaleF = new Vector2(-.3f,.7f);
            parent.children.Add(a); parent.children.Add(b); doc.layers.Add(parent);
            typeof(WhimTexDocument).GetMethod("RefreshTransformHierarchy", Internal).Invoke(doc,null);
            var plan = Prepare(a,b);
            check.True(plan != null, "Shape to mirrored Vector under an active parent");
            var start = Sample(plan,0).shapes[0].contours[0];
            foreach (var p in Square(0,0,1).points)
            {
                var local = a.transform.Map(p.position,Size);
                check.True(start.points.Exists(v => Vector2.Distance(local,v.position) < 2e-5f),"Shape uses child local transform");
            }
            Near(check,b.transform.Map(Vector2.zero,Size),Sample(plan,1).shapes[0].contours[0].points[0].position,
                "Vector endpoint excludes parent transform");
        }
        finally { UnityEngine.Object.DestroyImmediate(doc); }
    }

    private static void Fallback(TestContext check)
    {
        var path = Square(0,0,1); var a = LayerOf(path); var b = LayerOf(path.Clone(false));
        check.True(Prepare(a,b,new MorphAnchor { from = Vector2.zero,to = Vector2.one },
            new MorphAnchor { from = Vector2.zero,to = Vector2.right }) == null, "Conflicting landmark returns raster fallback");
        var m = ProjectiveMatrix.Identity; m.m20 = .25;
        b.transform.TrySetMatrix(m);
        check.True(Build(a,b,.5f) == null, "Perspective cubic requests raster fallback");
    }
}
