using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorUnionAppearanceTests
{
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Assembly Product = typeof(VectorLayerBehaviour).Assembly;
    static readonly MethodInfo Render = Product.GetType("DCFApixels.WhimTex.VectorRasterizer", true).GetMethod("Render", Hidden);
    const string Folder = "Temp/WhimTex/vector-union-appearance";

    public static string Fill() => Check(0);
    public static string Stroke() => Check(1);
    public static string Dashes() => Check(2);
    static string Check(int mode) => TestContext.Run("Union appearance: " + mode, t =>
    {
        string folder = Folder + "-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        using var suppression = (IDisposable)Product.GetType("DCFApixels.WhimTex.WhimTexProfiler", true)
            .GetMethod("SuppressInstrumentation", Hidden).Invoke(null, null);
        int cases = 0;
        for (int fixture = 0; fixture < 12; fixture++)
        {
            var input = Input(fixture);
            // Exercise both a single resolved operand and mixed fill rules in Combine.
            foreach (bool compound in new[] { false, true })
            {
                var inputs = compound ? new[] { input, Input((fixture + 3) % 12) } : new[] { input };
                var rules = compound ? new[] { VectorFillRule.NonZero, VectorFillRule.EvenOdd } : new[] { VectorFillRule.NonZero };
                var reference = VectorClipperWrapperTests.Reference(inputs, rules);
                var actual = VectorClipperWrapperTests.Current(inputs, rules);
                for (int variant = 0; variant < (mode == 0 ? 2 : 9); variant++)
                {
                    var a = Draw(reference, mode, variant, fixture);
                    Texture2D b = null;
                    try
                    {
                        b = Draw(actual, mode, variant, fixture);
                        double delta = Difference(a, b);
                        t.Near(0, delta, 0, "pixels fixture=" + fixture + " compound=" + compound + " variant=" + variant);
                        if (!compound && fixture == 5 && variant == 0)
                        {
                            File.WriteAllBytes(folder + "/" + mode + "-reference.png", EncodePng(a));
                            File.WriteAllBytes(folder + "/" + mode + "-current.png", EncodePng(b));
                        }
                        cases++;
                    }
                    finally { Object.DestroyImmediate(a); if (b != null) Object.DestroyImmediate(b); }
                }
            }
        }
        t.Equal(mode == 0 ? 48 : 216, cases, "appearance matrix count");
        Counterexample(t, mode, folder);
        FixedPointIsUsed(t);
    });

    static void FixedPointIsUsed(TestContext t)
    {
        var stable = VectorClipperWrapperTests.Resolve(Input(0));
        var timing = Product.GetType("DCFApixels.WhimTex.VectorGeometryTiming", true);
        using var session = (IDisposable)timing.GetMethod("Begin", Hidden).Invoke(null, null);
        VectorClipperWrapperTests.Current(new[] { stable }, new[] { VectorFillRule.NonZero });
        var calls = (Dictionary<string, long>)session.GetType().GetField("calls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        t.Equal(1L, calls["vector.clip.execute"], "proven fixed point skips the second union");
    }

    static void Counterexample(TestContext t, int mode, string folder)
    {
        var split = Input(5);
        var joined = VectorClipperWrapperTests.Reference(new[] { split }, new[] { VectorFillRule.NonZero });
        var a = Draw(split, mode, 0, 5);
        Texture2D b = null;
        try
        {
            b = Draw(joined, mode, 0, 5);
            double delta = Difference(a, b);
            if (mode == 0) t.Near(0, delta, 0, "touching partition preserves fill");
            else t.True(delta > 0, "blind union removal changes centered " + (mode == 1 ? "stroke" : "dashes"));
            File.WriteAllBytes(folder + "/" + mode + "-unsafe-skip.png", EncodePng(a));
        }
        finally { Object.DestroyImmediate(a); if (b != null) Object.DestroyImmediate(b); }
    }

    static Paths64 Input(int i)
    {
        Path64 Rect(long x, long y, long w, long h) => new Path64 {
            new Point64(x,y), new Point64(x+w,y), new Point64(x+w,y+h), new Point64(x,y+h) };
        Paths64 raw;
        switch (i)
        {
            case 0: raw = new Paths64 { Rect(-20,-18,40,36) }; break;
            case 1: raw = new Paths64 { Rect(-22,-22,44,44), Rect(-10,-10,20,20) }; break;
            case 2: raw = new Paths64 { Rect(-22,-20,22,40), Rect(0,-20,22,40) }; break;
            case 3: raw = new Paths64 { Rect(-22,-22,22,22), Rect(0,0,22,22) }; break;
            case 4: raw = new Paths64 { Rect(-18,-18,36,36), Rect(-18,-18,36,36) }; break;
            case 5: return VectorClipperWrapperTests.Resolve(VectorClipperWrapperTests.Fixture(0),
                VectorClipperWrapperTests.Fixture(1), ClipType.Union, FillRule.EvenOdd);
            case 6: raw = new Paths64 { new Path64 { new Point64(-24,-20), new Point64(24,20),
                new Point64(-24,20), new Point64(24,-20) } }; break;
            case 7:
                var star = new Path64();
                for (int p = 0; p < 26; p++) { double angle = p * Math.PI / 13, r = p % 2 == 0 ? 25 : 6;
                    star.Add(new Point64(Math.Cos(angle)*r, Math.Sin(angle)*r)); }
                raw = new Paths64 { star }; break;
            default: raw = VectorClipperWrapperTests.Fixture(i + 1); break;
        }
        return VectorClipperWrapperTests.Resolve(raw, rule: i == 4 ? FillRule.NonZero : FillRule.EvenOdd);
    }

    static Texture2D Draw(Paths64 paths, int mode, int variant, int fixture)
    {
        var shape = new VectorShape { fillMode = mode == 0 ? VectorFillMode.Solid : VectorFillMode.None,
            fillColor = new Color(.2f,.7f,.4f,.63f), stroke = mode != 0, strokeMode = VectorFillMode.Solid,
            strokeColor = new Color(.8f,.2f,.5f,.57f), strokeWidth = new[] { 1f, 3f, 7f }[fixture % 3],
            strokePosition = (VectorStrokePosition)(variant % 3), join = (VectorStrokeJoin)(variant / 3),
            cap = (VectorStrokeCap)((variant + fixture) % 3), fillRule = VectorFillRule.NonZero,
            dashPatternRelative = variant % 2 == 0, dashOffset = -.7f };
        // Counterexample is specifically the raw centerline, not its offset trajectory.
        if (fixture == 5 && variant == 0) shape.strokePosition = VectorStrokePosition.Center;
        if (mode == 2) shape.dashPattern = fixture % 3 == 0 ? new List<float> { .05f, 1.5f } :
            fixture % 3 == 1 ? new List<float> { 2, 1, .5f } : new List<float> { 2, 1 };
        foreach (var path in paths)
        {
            var contour = new VectorContour();
            foreach (var p in path) contour.points.Add(new VectorPoint(new Vector2((p.X + 32.25f) / 64, (p.Y + 31.75f) / 64)));
            shape.contours.Add(contour);
        }
        var model = new VectorLayerBehaviour { referenceSize = Vector2.one * 64,
            shapes = new List<VectorShape> { shape }, edgeMode = fixture % 2 == 0 ? VectorEdgeMode.Antialiased : VectorEdgeMode.Step };
        var image = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true) { hideFlags = HideFlags.HideAndDontSave };
        try { Render.Invoke(null, new object[] { model, image, ProjectiveMatrix.Identity, variant % 2 == 1 }); image.Apply(false,false); return image; }
        catch { Object.DestroyImmediate(image); throw; }
    }


    static byte[] EncodePng(Texture2D texture)
    {
        var encoder = Product.GetType("DCFApixels.WhimTex.WhimTexRasterEncoder", true);
        var format = Enum.Parse(Product.GetType("DCFApixels.WhimTex.RasterImageFormat", true), "Png");
        return (byte[])encoder.GetMethod("Encode", Hidden)
            .Invoke(null, new object[] { texture, format, 95, Texture2D.EXRFlags.CompressZIP });
    }

    static double Difference(Texture2D a, Texture2D b)
    {
        var x = a.GetRawTextureData<float>(); var y = b.GetRawTextureData<float>(); double max = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double delta = Math.Abs(x[i] - y[i]);
            if (!double.IsFinite(delta)) throw new Exception("Nonfinite pixel");
            max = Math.Max(max, delta);
        }
        return max;
    }
}
