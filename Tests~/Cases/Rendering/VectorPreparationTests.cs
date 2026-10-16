// Support: Tests~/Framework/TestApi.cs. Owned transient data/textures only.
// Reflection targets WhimTex types exclusively; no Unity internal APIs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorPreparationTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int Width = 73, Height = 59;

    public static string Flatten() => TestContext.Run("Vector preparation matches independent scalar flattening", t =>
    {
        var method = Method("VectorContourPreparation", "Flatten", typeof(List<VectorContour>), typeof(Vector2), typeof(float));
        var contours = FlattenInputs();
        var before = new List<VectorContour>();
        foreach (var contour in contours) before.Add(contour.Clone());
        var references = new[] { Vector2.one, new Vector2(257, 73), new Vector2(1024, 511), new Vector2(.003f, 2) };
        var tolerances = new[] { .001f, .015625f, .125f, .25f, 4f, 64f };
        foreach (var reference in references)
        foreach (float tolerance in tolerances)
        {
            var expected = new List<List<Vector2>>();
            foreach (var contour in contours) expected.Add(LegacyFlatten(contour, reference, tolerance));
            string label = "reference=" + reference + " tolerance=" + tolerance;
            var actual = Prepared(method, contours, reference, tolerance);
            SamePaths(t, expected, actual, label);
            SamePaths(t, expected, Prepared(method, contours, reference, tolerance), label + " repeated batch");
        }
        // Small and empty batches also exercise dispatch boundaries and preserve empty contour slots.
        SamePaths(t, new List<List<Vector2>>(), Prepared(method, new List<VectorContour>(), Vector2.one, .25f), "empty batch");
        for (int count = 1; count <= 4; count++)
        {
            var batch = contours.GetRange(0, count);
            var expected = new List<List<Vector2>>();
            foreach (var contour in batch) expected.Add(LegacyFlatten(contour, new Vector2(256, 128), .01f));
            SamePaths(t, expected, Prepared(method, batch, new Vector2(256, 128), .01f), "small batch " + count);
        }
        // A reversed traversal changes output order but not the required scalar semantics.
        var reversed = new List<VectorContour>();
        foreach (var contour in contours) reversed.Add(Reverse(contour));
        var reverseExpected = new List<List<Vector2>>();
        foreach (var contour in reversed) reverseExpected.Add(LegacyFlatten(contour, new Vector2(197, 83), .03125f));
        SamePaths(t, reverseExpected, Prepared(method, reversed, new Vector2(197, 83), .03125f), "reversed traversals");
        SameSource(t, before, contours);

        // Guard the fixtures themselves against becoming trivial straight-line comparisons.
        t.True(LegacyFlatten(contours[3], Vector2.one * 100, .01f).Count > 2, "collinear reversal fixture subdivides");
        t.True(LegacyFlatten(contours[4], Vector2.one * 100, .01f).Count > 8, "coincident-endpoint cusp fixture subdivides");
        t.Equal(1, LegacyFlatten(contours[1], Vector2.one, .01f).Count, "single closed anchor remains one point");
    });

    public static string CoverageAA() => Coverage(VectorEdgeMode.Antialiased);
    public static string CoverageStep() => Coverage(VectorEdgeMode.Step);

    public static string CoverageBudgets()
    {
        if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            return TestContext.Result("skipped", 0, "Vector coverage budget checks require RGBAFloat texture support.").ToJson();
        return TestContext.Run("Vector coverage prechecks global work before dispatch", t =>
        {
            const long limit = 100000000;
            var edgeType = OwnType("VectorCoveragePreparation+Edge");
            var coverageType = OwnType("VectorRasterizer+Coverage");
            var exceededType = OwnType("VectorRasterizer+CoverageBudgetExceeded");
            var render = Method("VectorCoveragePreparation", "Render", edgeType.MakeArrayType(), edgeType.MakeArrayType(),
                coverageType, typeof(bool), typeof(long).MakeByRefType());
            var constructor = coverageType.GetConstructor(Instance, null,
                new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) }, null);
            t.True(constructor != null, "WhimTex coverage constructor exists");
            var empty = Array.CreateInstance(edgeType, 0);

            void Reject(Array fill, Array stroke, bool step, int width, int height, long priorWork, string label)
            {
                object coverage = constructor.Invoke(new object[] { 0, 4, 7, width, height });
                using var owned = (IDisposable)coverage;
                var texture = (Texture2D)Field(coverage, "texture");
                // Public API tripwire: a regressed precheck must fail at output access, before
                // scheduling the oversized synthetic workload. This texture is owned by this case.
                texture.Apply(false, true);
                t.True(!texture.isReadable, label + " unreadable output tripwire armed");
                object[] arguments = { fill, stroke, coverage, step, priorWork };
                Exception failure = null;
                try { render.Invoke(null, arguments); }
                catch (TargetInvocationException error) { failure = error.InnerException ?? error; }
                t.True(failure != null && failure.GetType() == exceededType,
                    label + " must request scalar fallback before output access/dispatch; got " + (failure?.GetType().FullName ?? "success"));
                t.Equal(priorWork, (long)arguments[4], label + " preserves caller work for scalar fallback");
            }

            // The 1x1024 mask is 16 KiB. 12,210 full-height edges imply >100M AA crossings;
            // eight bands' scratch estimate stays below 128 MiB, so scratch rejection cannot
            // accidentally satisfy this work-precheck test. No heavy scan can reach dispatch.
            var oversized = SyntheticEdges(edgeType, 12210, -1000, 2000);
            Reject(oversized, empty, false, 1, 1024, 0, "oversized fill AA work");
            var half = SyntheticEdges(edgeType, 6105, -1000, 2000);
            Reject(half, half, false, 1, 1024, 0, "combined fill/stroke AA work");

            // Crop y=[7,10): fractional endpoints conservatively cover three rows, not two.
            var fractional = SyntheticEdges(edgeType, 2, 7.25, 9.75);
            Reject(fractional, empty, false, 2, 3, limit - 53, "AA previous work plus fractional rows");
            Reject(empty, fractional, true, 2, 3, limit - 11, "Step previous work plus fractional stroke rows");
            Reject(empty, empty, false, 2, 3, limit - 5, "pixel work alone exceeds remaining budget");

            // Exact-boundary positives perform only six pixels and two edges. The large edge
            // y range must be clipped to the crop; an overconservative unbounded estimate fails.
            var spanning = SyntheticEdges(edgeType, 2, -1000, 2000);
            foreach (bool step in new[] { false, true })
            {
                object coverage = constructor.Invoke(new object[] { 0, 4, 7, 2, 3 });
                using var owned = (IDisposable)coverage;
                long priorWork = limit - (6 + 2 * 3 * (step ? 1 : 8));
                object[] arguments = { spanning, empty, coverage, step, priorWork };
                Invoke(render, arguments);
                t.Equal(limit, (long)arguments[4], "exact global work limit is accepted " + (step ? "Step" : "AA"));
                var pixels = ((Texture2D)Field(coverage, "texture")).GetPixels();
                t.Equal(6, pixels.Length, "bounded budget fixture size");
                foreach (var pixel in pixels)
                {
                    t.Near(step ? 1 : .75, pixel.r, 0, "boundary fixture exact fill");
                    t.True(pixel.g == 0 && pixel.b == 0 && pixel.a == 0, "boundary fixture has no stroke/intersection");
                }
            }
        });
    }

    static Array SyntheticEdges(Type type, int count, double y0, double y1)
    {
        var edges = Array.CreateInstance(type, count);
        object left = Activator.CreateInstance(type), right = Activator.CreateInstance(type);
        foreach (object edge in new[] { left, right })
        {
            type.GetField("y0", Instance).SetValue(edge, y0);
            type.GetField("y1", Instance).SetValue(edge, y1);
            type.GetField("slope", Instance).SetValue(edge, 0d);
        }
        type.GetField("x0", Instance).SetValue(left, 4.25d);
        type.GetField("x0", Instance).SetValue(right, 5.75d);
        type.GetField("winding", Instance).SetValue(left, 1);
        type.GetField("winding", Instance).SetValue(right, -1);
        for (int i = 0; i < count; i++) edges.SetValue(i % 2 == 0 ? left : right, i);
        return edges;
    }

    static string Coverage(VectorEdgeMode edge)
    {
        var shader = Shader.Find("Hidden/WhimTex/VectorPaint");
        if (shader == null || !shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ||
            !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            return TestContext.Result("skipped", 0, "Vector coverage parity requires the supported VectorPaint shader and float textures/targets.").ToJson();
        return TestContext.Run("Vector " + edge + " coverage and GPU paint match scalar scan conversion", t =>
        {
            var prepare = Method("VectorRasterizer", "Prepare", typeof(VectorLayerBehaviour), typeof(int), typeof(int), typeof(ProjectiveMatrix), typeof(bool));
            var scalar = Method("VectorRasterizer", "Render", typeof(VectorLayerBehaviour), typeof(Texture2D), typeof(ProjectiveMatrix), typeof(bool));
            var paint = OwnType("VectorPaintRenderer").GetMethod("Render", Static);
            t.True(paint != null, "WhimTex GPU paint entry exists");
            var transforms = new[] {
                ProjectiveMatrix.Identity,
                ProjectiveMatrix.Translate(.375 / Width, -.3125 / Height),
                ProjectiveMatrix.Translate(.5, .5) * ProjectiveMatrix.Rotate(27) * ProjectiveMatrix.Scale(.81, 1.07) * ProjectiveMatrix.Translate(-.5, -.5),
                ProjectiveMatrix.Translate(1, 0) * ProjectiveMatrix.Scale(-1, 1),
                new ProjectiveMatrix { m00 = .93, m01 = .11, m02 = -.06, m10 = -.07, m11 = 1.02, m12 = .04, m20 = .24, m21 = -.13, m22 = 1 }
            };
            var cases = CoverageInputs();
            int covered = 0, partial = 0, sharedEdges = 0;
            for (int caseIndex = 0; caseIndex < cases.Count; caseIndex++)
            for (int matrixIndex = 0; matrixIndex < transforms.Length; matrixIndex++)
            foreach (bool clip in new[] { false, true })
            {
                var model = cases[caseIndex]; model.edgeMode = edge;
                CompareCoverage(t, prepare, scalar, paint, model, transforms[matrixIndex], clip, Width, Height,
                    edge + " fixture=" + caseIndex + " transform=" + matrixIndex + " clip=" + clip,
                    ref covered, ref partial, ref sharedEdges);
            }
            // One-pixel rows/columns and a collapsed transform exercise empty/cropped worker ranges.
            foreach (var size in new[] { new Vector2Int(1, 1), new Vector2Int(1, 19), new Vector2Int(23, 1),
                new Vector2Int(65, 33), new Vector2Int(17, 97) })
                CompareCoverage(t, prepare, scalar, paint, cases[0], ProjectiveMatrix.Identity, true, size.x, size.y,
                    edge + " thin raster " + size, ref covered, ref partial, ref sharedEdges);
            CompareCoverage(t, prepare, scalar, paint, cases[0], ProjectiveMatrix.Scale(0, 1), false, Width, Height,
                edge + " collapsed transform", ref covered, ref partial, ref sharedEdges);
            t.True(covered > 100, "matrix includes visible geometry");
            t.True(sharedEdges > 100, "matrix includes actual fill/stroke intersections");
            if (edge == VectorEdgeMode.Antialiased) t.True(partial > 100, "AA matrix includes partial coverage pixels");
            else t.Equal(0, partial, "Step masks contain only zero/one coverage");
        });
    }

    static void CompareCoverage(TestContext t, MethodInfo prepare, MethodInfo scalar, MethodInfo paint,
        VectorLayerBehaviour model, ProjectiveMatrix matrix, bool clip, int width, int height, string label,
        ref int covered, ref int partial, ref int sharedEdges)
    {
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        IList entries = null;
        Texture2D oracleTexture = null, readback = null;
        RenderTexture target = null;
        try
        {
            oracleTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            entries = (IList)Invoke(prepare, model, width, height, matrix, clip);
            t.True(entries != null, label + " prepared list");
            var byShape = new Dictionary<int, Color[]>();
            foreach (object entry in entries)
            {
                int shapeIndex = (int)Field(entry, "shapeIndex");
                int x0 = (int)Field(entry, "x"), y0 = (int)Field(entry, "y");
                var texture = (Texture2D)Field(entry, "texture");
                t.True(shapeIndex >= 0 && shapeIndex < model.shapes.Count && !byShape.ContainsKey(shapeIndex), label + " distinct shape index");
                t.True(x0 >= 0 && y0 >= 0 && x0 + texture.width <= width && y0 + texture.height <= height, label + " cropped bounds");
                var crop = texture.GetPixels();
                var full = new Color[width * height];
                for (int y = 0; y < texture.height; y++)
                    Array.Copy(crop, y * texture.width, full, (y + y0) * width + x0, texture.width);
                byShape.Add(shapeIndex, full);
            }
            for (int shapeIndex = 0; shapeIndex < model.shapes.Count; shapeIndex++)
            {
                // Keep fill/stroke geometry gates unchanged. Only alpha changes, so each legacy
                // render isolates a mask while still exercising the original scalar scan loop.
                var shape = model.shapes[shapeIndex].Clone();
                var isolated = new VectorLayerBehaviour { referenceSize = model.referenceSize, edgeMode = model.edgeMode,
                    shapes = new List<VectorShape> { shape } };
                shape.fillColor = new Color(1, 0, 0, 1); shape.strokeColor = new Color(0, 0, 1, 0);
                var fill = ScalarPixels(scalar, isolated, oracleTexture, matrix, clip);
                shape.fillColor.a = 0; shape.strokeColor.a = 1;
                var stroke = ScalarPixels(scalar, isolated, oracleTexture, matrix, clip);
                shape.fillColor.a = 1;
                var union = ScalarPixels(scalar, isolated, oracleTexture, matrix, clip);
                if (!byShape.TryGetValue(shapeIndex, out var actual)) actual = new Color[width * height];
                double fillError = 0, strokeError = 0, overlapError = 0;
                for (int i = 0; i < actual.Length; i++)
                {
                    var mask = actual[i];
                    if (!Finite(mask.r) || !Finite(mask.g) || !Finite(mask.b) || mask.r < 0 || mask.r > 1 ||
                        mask.g < 0 || mask.g > 1 || mask.b < 0 || mask.b > Math.Min(mask.r, mask.g) + 2e-7f || mask.a != 0)
                        throw new InvalidOperationException(label + " invalid coverage at shape=" + shapeIndex + " pixel=" + i + ": " + mask);
                    // F and S are directly the legacy alpha, so these two comparisons are exact.
                    // Recovering I = F + S - union incurs only the final float subtraction error.
                    fillError = Math.Max(fillError, Math.Abs((double)fill[i].a - mask.r));
                    strokeError = Math.Max(strokeError, Math.Abs((double)stroke[i].a - mask.g));
                    overlapError = Math.Max(overlapError, Math.Abs((double)fill[i].a + stroke[i].a - union[i].a - mask.b));
                    if (mask.r > 0 || mask.g > 0) covered++;
                    if (mask.r > 0 && mask.r < 1 || mask.g > 0 && mask.g < 1 || mask.b > 0 && mask.b < 1) partial++;
                    if (mask.b > 0) sharedEdges++;
                }
                t.Near(0, fillError, 0, label + " exact fill coverage shape=" + shapeIndex);
                t.Near(0, strokeError, 0, label + " exact stroke coverage shape=" + shapeIndex);
                t.Near(0, overlapError, 2e-7, label + " intersection coverage shape=" + shapeIndex);
            }
            var expected = ScalarPixels(scalar, model, oracleTexture, matrix, clip);
            target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            t.True(target.Create(), label + " GPU target allocation");
            Invoke(paint, model, entries, matrix, target);
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, label + " GPU paint restores caller state");
            readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            readback.Apply(false, false);
            SamePixels(t, expected, readback.GetPixels(), label + " translucent GPU paint");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (entries != null) foreach (IDisposable entry in entries) entry.Dispose();
            if (target != null) Object.DestroyImmediate(target);
            if (readback != null) Object.DestroyImmediate(readback);
            if (oracleTexture != null) Object.DestroyImmediate(oracleTexture);
        }
    }

    static Color[] ScalarPixels(MethodInfo scalar, VectorLayerBehaviour model, Texture2D target, ProjectiveMatrix matrix, bool clip)
    {
        Invoke(scalar, model, target, matrix, clip);
        // Render writes readable CPU storage. No upload/readback or production mask helper is used.
        return target.GetPixels();
    }

    static void SamePixels(TestContext t, Color[] expected, Color[] actual, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " dimensions");
        double max = 0; int pixel = -1, channel = -1;
        for (int i = 0; i < expected.Length; i++)
        for (int c = 0; c < 4; c++)
        {
            double difference = Math.Abs((double)expected[i][c] - actual[i][c]);
            if (double.IsNaN(difference) || double.IsInfinity(difference))
                throw new InvalidOperationException(label + " nonfinite pixel=" + i + " channel=" + c);
            if (difference > max) { max = difference; pixel = i; channel = c; }
        }
        // Float paint math may round differently on GPU; mask equality above has no such allowance.
        t.Near(0, max, 2e-5, label + " all RGBA pixels; largest difference pixel=" + pixel + " channel=" + channel);
    }

    static List<VectorLayerBehaviour> CoverageInputs()
    {
        var result = new List<VectorLayerBehaviour>();
        for (int style = 0; style < 3; style++)
        {
            var shape = PaintedShape();
            shape.join = (VectorStrokeJoin)style; shape.cap = (VectorStrokeCap)style;
            shape.strokePosition = (VectorStrokePosition)style;
            shape.fillRule = style == 1 ? VectorFillRule.NonZero : VectorFillRule.EvenOdd;
            shape.contours.Add(CurvedLoop());
            var hole = Polygon(true, new Vector2(.34f, .33f), new Vector2(.67f, .34f), new Vector2(.63f, .64f), new Vector2(.32f, .66f));
            shape.contours.Add(style == 1 ? Reverse(hole) : hole);
            if (style > 0) { shape.dashPattern = new List<float> { 1.3f, .6f, .25f }; shape.dashPatternRelative = true; shape.dashOffset = -2.375f; }
            result.Add(Model(shape));
        }
        var open = PaintedShape(); open.fillMode = VectorFillMode.None; open.cap = VectorStrokeCap.Round; open.join = VectorStrokeJoin.Round;
        open.contours.Add(Cubic(new Vector2(-.15f, .3f), new Vector2(1.5f, .3f), new Vector2(-.5f, .3f), new Vector2(1.15f, .3f)));
        open.contours.Add(Cubic(new Vector2(.15f, .65f), new Vector2(.95f, -.25f), new Vector2(-.2f, 1.3f), new Vector2(.85f, .65f)));
        open.dashPattern = new List<float> { 4.5f, 2.25f }; result.Add(Model(open));

        var contacts = PaintedShape(); contacts.strokeWidth = .7f;
        contacts.contours.Add(Polygon(true, new Vector2(-.2f, .2f), new Vector2(.5f, .2f), new Vector2(.5f, .8f), new Vector2(-.2f, .8f)));
        contacts.contours.Add(Polygon(true, new Vector2(.5f, .2f), new Vector2(1.2f, .2f), new Vector2(1.2f, .8f), new Vector2(.5f, .8f)));
        contacts.contours.Add(Polygon(true, new Vector2(.1f, .5f), new Vector2(.9f, .5f), new Vector2(.5f, .5005f)));
        result.Add(Model(contacts));

        var many = PaintedShape(); many.stroke = false;
        for (int y = 0; y < 8; y++) for (int x = 0; x < 12; x++)
        {
            float x0 = .02f + x * .08f, y0 = .02f + y * .12f;
            many.contours.Add(Polygon(true, new Vector2(x0, y0), new Vector2(x0 + .053f, y0),
                new Vector2(x0 + .053f, y0 + .073f), new Vector2(x0, y0 + .073f)));
        }
        result.Add(Model(many));

        var lower = PaintedShape(); lower.contours.Add(CurvedLoop());
        var upper = PaintedShape(); upper.fillColor = new Color(0, 1, 0, .3125f); upper.strokeColor = new Color(1, 0, 1, .75f);
        upper.contours.Add(Polygon(true, new Vector2(.4f, .05f), new Vector2(.9f, .3f), new Vector2(.7f, .9f), new Vector2(.25f, .7f)));
        result.Add(Model(lower, upper));

        var transparent = PaintedShape(); transparent.fillColor.a = transparent.strokeColor.a = 0;
        transparent.contours.Add(CurvedLoop()); result.Add(Model(transparent));
        var empty = PaintedShape(); empty.visible = false; empty.contours.Add(CurvedLoop()); result.Add(Model(empty));
        return result;
    }

    static VectorShape PaintedShape() => new VectorShape { fillMode = VectorFillMode.Solid, strokeMode = VectorFillMode.Solid,
        fillColor = new Color(1, 0, 0, .375f), strokeColor = new Color(0, 0, 1, .625f), stroke = true, strokeWidth = 5.25f };
    static VectorLayerBehaviour Model(params VectorShape[] shapes) => new VectorLayerBehaviour {
        referenceSize = new Vector2(Width, Height), shapes = new List<VectorShape>(shapes) };

    static List<VectorContour> FlattenInputs()
    {
        var result = new List<VectorContour> {
            new VectorContour(),
            Polygon(true, new Vector2(.375f, -.25f)),
            Polygon(false, Vector2.zero, Vector2.right),
            Cubic(Vector2.zero, Vector2.right * 2, Vector2.left, Vector2.right),
            Cubic(new Vector2(.5f, .5f), new Vector2(1.7f, -.8f), new Vector2(-.7f, -.8f), new Vector2(.5f, .5f)),
            Cubic(Vector2.zero, new Vector2(1, 1), new Vector2(-1, 1), new Vector2(0, .000001f)),
            CurvedLoop(),
            Polygon(true, Vector2.zero, Vector2.right, Vector2.one, Vector2.zero),
            Polygon(false, Vector2.zero, new Vector2(.000004f, 0), new Vector2(.00002f, 0), Vector2.right),
            Polygon(true, Vector2.zero, Vector2.right, Vector2.one, new Vector2(.000004f, 0)),
            Polygon(false, Vector2.one, Vector2.one, Vector2.one),
            new VectorContour { closed = true, points = new List<VectorPoint> {
                new VectorPoint(new Vector2(.5f, .5f)) { inTangent = new Vector2(-.4f, .3f), outTangent = new Vector2(.4f, .3f) } } },
            new VectorContour { closed = false },
            Polygon(false, new Vector2(-.125f, .875f)),
            Polygon(true, Vector2.one, Vector2.one, Vector2.one),
            Polygon(true, Vector2.zero, Vector2.right, Vector2.one, new Vector2(.000012f, 0)),
            // Flatness exactly on and immediately either side of the .125 tolerance boundary.
            Cubic(Vector2.zero, new Vector2(.25f, .125f), new Vector2(.75f, .125f), Vector2.right),
            Cubic(Vector2.zero, new Vector2(.25f, .125f - 1f / 67108864), new Vector2(.75f, .125f - 1f / 67108864), Vector2.right),
            Cubic(Vector2.zero, new Vector2(.25f, .125f + 1f / 67108864), new Vector2(.75f, .125f + 1f / 67108864), Vector2.right)
        };
        uint seed = 0x51c0ffee;
        for (int i = 0; i < 52; i++)
        {
            var contour = new VectorContour { closed = i % 3 != 0 };
            for (int p = 0; p < 2 + i % 7; p++)
            {
                var point = new VectorPoint(new Vector2(Next(ref seed) * 1.8f - .4f, Next(ref seed) * 1.8f - .4f)) {
                    inTangent = new Vector2(Next(ref seed) * 2 - 1, Next(ref seed) * 2 - 1),
                    outTangent = new Vector2(Next(ref seed) * 2 - 1, Next(ref seed) * 2 - 1),
                    tangentMode = (VectorTangentMode)(p % 3)
                };
                if (i % 4 == 0) point.inTangent = -point.outTangent;
                if (i % 7 == 0) point.position.y = point.inTangent.y = point.outTangent.y = 0;
                contour.points.Add(point);
            }
            result.Add(contour);
        }
        return result;
    }

    static float Next(ref uint seed) { seed = unchecked(seed * 1664525u + 1013904223u); return (seed >> 8) / 16777216f; }

    static VectorContour CurvedLoop()
    {
        var contour = Polygon(true, new Vector2(.12f, .18f), new Vector2(.88f, .2f), new Vector2(.85f, .86f), new Vector2(.16f, .83f));
        contour.points[0].outTangent = new Vector2(.35f, -.35f); contour.points[1].inTangent = new Vector2(-.3f, .18f);
        contour.points[1].outTangent = new Vector2(.3f, .18f); contour.points[2].inTangent = new Vector2(-.16f, -.21f);
        contour.points[2].outTangent = new Vector2(-.08f, .3f); contour.points[3].inTangent = new Vector2(.3f, -.14f);
        // The closing segment is curved and must be emitted before removing its repeated endpoint.
        contour.points[3].outTangent = new Vector2(-.3f, -.1f); contour.points[0].inTangent = new Vector2(.2f, .17f);
        return contour;
    }

    static VectorContour Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var contour = Polygon(false, a, d);
        contour.points[0].outTangent = b - a; contour.points[1].inTangent = c - d;
        return contour;
    }
    static VectorContour Polygon(bool closed, params Vector2[] points)
    {
        var contour = new VectorContour { closed = closed };
        foreach (var point in points) contour.points.Add(new VectorPoint(point));
        return contour;
    }
    static VectorContour Reverse(VectorContour source)
    {
        var result = source.Clone(); result.points.Reverse();
        foreach (var point in result.points) { var incoming = point.inTangent; point.inTangent = point.outTangent; point.outTangent = incoming; }
        return result;
    }

    static List<List<Vector2>> Prepared(MethodInfo method, List<VectorContour> contours, Vector2 reference, float tolerance)
        => (List<List<Vector2>>)Invoke(method, contours, reference, tolerance);

    static void SamePaths(TestContext t, List<List<Vector2>> expected, List<List<Vector2>> actual, string label)
    {
        t.True(actual != null, label + " nonnull batch");
        t.Equal(expected.Count, actual.Count, label + " contour count/order");
        for (int c = 0; c < expected.Count; c++)
        {
            t.True(actual[c] != null, label + " nonnull contour=" + c);
            t.Equal(expected[c].Count, actual[c].Count, label + " point count contour=" + c);
            int mismatch = -1;
            for (int p = 0; p < expected[c].Count; p++)
                if (expected[c][p].x != actual[c][p].x || expected[c][p].y != actual[c][p].y) { mismatch = p; break; }
            // Do not use Vector2 == here: its approximate equality would hide arithmetic regressions.
            t.True(mismatch < 0, label + " exact coordinates/order contour=" + c + " first mismatch=" + mismatch +
                (mismatch < 0 ? "" : " expected=" + expected[c][mismatch].ToString("R") + " actual=" + actual[c][mismatch].ToString("R")));
        }
    }
    static void SameSource(TestContext t, List<VectorContour> before, List<VectorContour> after)
    {
        t.Equal(before.Count, after.Count, "source contour count unchanged");
        for (int c = 0; c < before.Count; c++)
        {
            t.Equal(before[c].id, after[c].id, "source contour ID unchanged");
            t.Equal(before[c].closed, after[c].closed, "source closure unchanged");
            t.Equal(before[c].points.Count, after[c].points.Count, "source anchor count unchanged");
            for (int p = 0; p < before[c].points.Count; p++)
            {
                var a = before[c].points[p]; var b = after[c].points[p];
                t.Equal(a.id, b.id, "source anchor ID unchanged");
                t.True(a.position.Equals(b.position) && a.inTangent.Equals(b.inTangent) && a.outTangent.Equals(b.outTangent) &&
                    a.tangentMode == b.tangentMode, "source coordinates/handles unchanged contour=" + c + " anchor=" + p);
            }
        }
    }

    // Frozen scalar oracle copied from VectorGeometry.FlattenContour on 2026-10-16 before
    // the Jobs/Burst preparation change. It intentionally does not call any production
    // flattening, distance, validation or deduplication helper. Keep operation order,
    // finite-segment distance, depth/point limits and Vector2 approximate seam semantics.
    static List<Vector2> LegacyFlatten(VectorContour contour, Vector2 reference, float tolerance)
    {
        if (!Finite(reference) || reference.x <= 0 || reference.y <= 0 || reference.x > 10000000 || reference.y > 10000000)
            throw new InvalidOperationException("Invalid reference dimensions in scalar oracle.");
        if (!Finite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (contour?.points == null) throw new ArgumentNullException(nameof(contour));
        if (contour.points.Count > 65536) throw new InvalidOperationException("Scalar oracle anchor limit.");
        var output = new List<Vector2>();
        foreach (var point in contour.points)
            if (point == null || !Finite(point.position) || !Finite(point.inTangent) || !Finite(point.outTangent) ||
                !Finite(point.position + point.inTangent) || !Finite(point.position + point.outTangent))
                throw new InvalidOperationException("Invalid scalar oracle point.");
        if (contour.points.Count == 0) return output;
        LegacyAdd(output, Vector2.Scale(contour.points[0].position, reference));
        int segments = contour.points.Count - (contour.closed ? 0 : 1);
        for (int i = 0; i < segments; i++)
        {
            var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count];
            LegacySubdivide(Vector2.Scale(a.position, reference), Vector2.Scale(a.position + a.outTangent, reference),
                Vector2.Scale(b.position + b.inTangent, reference), Vector2.Scale(b.position, reference), tolerance * tolerance, 0, output);
        }
        if (contour.closed && output.Count > 1 && output[0] == output[output.Count - 1]) output.RemoveAt(output.Count - 1);
        return output;
    }
    static void LegacySubdivide(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float toleranceSquared, int depth, List<Vector2> output)
    {
        if (Mathf.Max(LegacyDistanceSquared(b, a, d), LegacyDistanceSquared(c, a, d)) <= toleranceSquared)
        { LegacyAdd(output, d); return; }
        if (depth >= 24) throw new InvalidOperationException("Scalar oracle cubic subdivision depth limit.");
        Vector2 ab = (a + b) * .5f, bc = (b + c) * .5f, cd = (c + d) * .5f;
        Vector2 abc = (ab + bc) * .5f, bcd = (bc + cd) * .5f, mid = (abc + bcd) * .5f;
        LegacySubdivide(a, ab, abc, mid, toleranceSquared, depth + 1, output);
        LegacySubdivide(mid, bcd, cd, d, toleranceSquared, depth + 1, output);
    }
    static float LegacyDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a; float length = ab.sqrMagnitude;
        float along = length > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / length) : 0;
        return (p - (a + along * ab)).sqrMagnitude;
    }
    static void LegacyAdd(List<Vector2> output, Vector2 point)
    {
        if (!Finite(point) || Math.Abs(point.x) > 10000000 || Math.Abs(point.y) > 10000000)
            throw new InvalidOperationException("Scalar oracle coordinate range limit.");
        if (output.Count >= 262144) throw new InvalidOperationException("Scalar oracle flattened point limit.");
        if (output.Count == 0 || output[output.Count - 1] != point) output.Add(point);
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);

    static Type OwnType(string name) => typeof(VectorShape).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static MethodInfo Method(string type, string name, params Type[] parameters)
    {
        var method = OwnType(type).GetMethod(name, Static, null, parameters, null);
        if (method == null) throw new InvalidOperationException("Missing WhimTex test contract: " + type + "." + name);
        return method;
    }
    static object Field(object instance, string name)
    {
        var field = instance.GetType().GetField(name, Instance);
        if (field == null) throw new InvalidOperationException("Missing WhimTex coverage field: " + name);
        return field.GetValue(instance);
    }
    static object Invoke(MethodInfo method, params object[] arguments)
    {
        try { return method.Invoke(null, arguments); }
        catch (TargetInvocationException error) { throw new InvalidOperationException(method.DeclaringType.Name + "." + method.Name + " failed.", error.InnerException ?? error); }
    }
}
