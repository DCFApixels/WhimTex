// Explicit support: Framework/TestApi.cs only. In-memory public APIs; no assets,
// windows, files, jobs, rendering or fixtures. Entry: VectorBrushTests.Run().
using System;
using System.Collections.Generic;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class VectorBrushTests
{
    public static string Run() => TestContext.Run("Vector brush source ownership, raster preview, holes and bounded reversible offsets", t =>
    {
        Builder(t);
        Rebuild(t);
        Hole(t);
        ReferenceMetric(t);
        BordersAndSaddles(t);
        SmoothCurves(t);
        NarrowFeatures(t);
        Validation(t);
    });

    static void Builder(TestContext t)
    {
        var brush = new VectorBrushBuilder();
        brush.Begin(64, 64);
        t.Equal(new RectInt(0, 0, 64, 64), brush.ConsumeDirtyRect(), "Beginning clears the preview texture");
        t.True(brush.AddPoint(new Vector2(10.2f, 12.3f), 3.75f), "First dab changes coverage");
        RectInt dirty = brush.ConsumeDirtyRect();
        t.True(dirty.width < 12 && dirty.height < 12 && dirty.Contains(new Vector2Int(10, 12)), "Small dab has a local dirty region");
        int soft = 0;
        foreach (byte value in brush.Coverage) if (value > 0 && value < 255) soft++;
        t.True(soft > 0, "Preview contains actual antialiased coverage");
        t.True(brush.AddPoint(new Vector2(54.2f, 12.3f), 3.75f), "Sparse pointer samples paint a connected stroke");
        for (int x = 11; x < 54; x++) t.Equal((byte)255, brush.Coverage[12 * 64 + x], "No gap along the swept stroke");
        var draft = brush.Finish();
        byte[] original = (byte[])draft.coverage.Clone();
        t.True(!brush.IsDrawing, "Finish ends mutable drawing");
        Throws<InvalidOperationException>(t, () => brush.AddPoint(Vector2.zero, 2), "Finished mask cannot be painted again");
        brush.Begin(64, 64);
        brush.AddPoint(new Vector2(40, 40), 12);
        t.True(!ReferenceEquals(draft.coverage, brush.Coverage), "Next stroke owns a different buffer");
        EqualBytes(t, original, draft.coverage, "Starting the next stroke preserves retained source");
        VectorBrushDraft clone = draft.DeepClone();
        t.Equal(draft.shapeId, clone.shapeId, "Snapshot retains shape identity");
        clone.coverage[0] = 255;
        t.Equal((byte)0, draft.coverage[0], "Cloned source does not alias Undo data");

        brush.Begin(16, 16);
        brush.ConsumeDirtyRect();
        brush.AddPoint(new Vector2(-100, 8), 2);
        brush.AddPoint(new Vector2(100, 8), 2);
        for (int x = 0; x < 16; x++) t.True(brush.Coverage[8 * 16 + x] > 127, "Clipped off-frame gesture still crosses the mask");
        brush.Begin(1, 1);
        for (int i = 0; i < VectorBrushBuilder.MaxSamples; i++) brush.AddPoint(Vector2.zero, 0);
        t.True(brush.IsFull, "Sample limit is observable");
        t.True(!brush.AddPoint(Vector2.zero, 1), "Sample overflow does not extend the stroke silently");
        t.Equal(VectorBrushBuilder.MaxSamples, brush.SampleCount, "Sample count is bounded");
    }

    static void Rebuild(TestContext t)
    {
        VectorBrushDraft draft = Disc(64, 64, new Vector2(32, 32), 16);
        byte[] source = (byte[])draft.coverage.Clone();
        Vector2 size = new Vector2(64, 64);
        draft.tolerance = .25f;
        VectorShape precise = VectorBrushGeometry.Rebuild(draft, size);
        t.Equal(draft.shapeId, precise.id, "Rebuilt shape uses the retained ID");
        t.Equal(1, precise.contours.Count, "A single dab becomes one contour");
        CheckContours(t, precise);
        VectorBrushDraft reloaded = JsonUtility.FromJson<VectorBrushDraft>(JsonUtility.ToJson(draft));
        reloaded.Validate();
        EqualBytes(t, source, reloaded.coverage, "Serializable draft keeps original coverage across an in-memory roundtrip");
        t.Equal(draft.shapeId, reloaded.shapeId, "Serializable draft keeps shape identity");
        SameGeometry(t, precise, VectorBrushGeometry.Rebuild(reloaded, size));
        t.True(Contains(precise, new Vector2(.5f, .5f)), "Dab center remains filled");
        Rect bounds = Bounds(precise);
        draft.tolerance = 4;
        VectorShape coarse = VectorBrushGeometry.Rebuild(draft, size);
        t.True(PointCount(coarse) <= PointCount(precise), "Coarse tolerance does not require more anchors for a disc");
        draft.tolerance = .25f; draft.expansion = 3;
        Rect expanded = Bounds(VectorBrushGeometry.Rebuild(draft, size));
        t.Near(bounds.xMin - 3f / 64, expanded.xMin, .6f / 64, "Positive signed-distance offset expands by reference pixels");
        draft.expansion = -3;
        Rect contracted = Bounds(VectorBrushGeometry.Rebuild(draft, size));
        t.Near(bounds.xMin + 3f / 64, contracted.xMin, .6f / 64, "Negative signed-distance offset contracts by reference pixels");
        draft.expansion = -100;
        VectorShape empty = VectorBrushGeometry.Rebuild(draft, size);
        t.Equal(0, empty.contours.Count, "Large contraction can erase the derived geometry");
        t.Equal(draft.shapeId, empty.id, "Empty result keeps the editable draft identity");
        draft.expansion = 0;
        VectorShape restored = VectorBrushGeometry.Rebuild(draft, size);
        SameGeometry(t, precise, restored);
        EqualBytes(t, source, draft.coverage, "Rebuilding never mutates original coverage");
    }

    static void Hole(TestContext t)
    {
        var draft = new VectorBrushDraft { width = 64, height = 64, coverage = new byte[4096], tolerance = .5f };
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float radius = new Vector2(x + .5f - 32, y + .5f - 32).magnitude;
            draft.coverage[y * 64 + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(.5f - Mathf.Abs(radius - 17) + 5) * 255);
        }
        var shape = VectorBrushGeometry.Rebuild(draft, new Vector2(64, 64));
        t.Equal(2, shape.contours.Count, "Ring retains an outer boundary and a hole");
        t.Equal(VectorFillRule.EvenOdd, shape.fillRule, "Compound fill uses even-odd semantics");
        t.True(!Contains(shape, new Vector2(.5f, .5f)), "The ring center is empty");
        t.True(Contains(shape, new Vector2(.5f, 49f / 64)), "The ring body is filled");
        draft.expansion = 2;
        shape = VectorBrushGeometry.Rebuild(draft, new Vector2(64, 64));
        t.Equal(2, shape.contours.Count, "A small expansion preserves the hole");
        t.True(!Contains(shape, new Vector2(.5f, .5f)), "Expanded ring still has an empty center");
        draft.expansion = 14;
        shape = VectorBrushGeometry.Rebuild(draft, new Vector2(64, 64));
        t.Equal(1, shape.contours.Count, "Distance expansion can close a hole");
        t.True(Contains(shape, new Vector2(.5f, .5f)), "Closed hole becomes filled");
    }

    static void ReferenceMetric(TestContext t)
    {
        var draft = new VectorBrushDraft { width = 64, height = 32, coverage = new byte[2048], tolerance = .25f };
        for (int y = 8; y < 24; y++) for (int x = 16; x < 48; x++) draft.coverage[y * 64 + x] = 255;
        Vector2 size = new Vector2(128, 128);
        Rect original = Bounds(VectorBrushGeometry.Rebuild(draft, size));
        t.Near(.25, original.xMin, .26 / 128, "X pixel-center convention within cubic fitting tolerance");
        t.Near(.25, original.yMin, .26 / 128, "Y pixel-center convention within cubic fitting tolerance");
        draft.expansion = 4;
        Rect expanded = Bounds(VectorBrushGeometry.Rebuild(draft, size));
        t.Near(4, (original.xMin - expanded.xMin) * 128, .3, "X expansion uses physical reference metric");
        t.Near(4, (original.yMin - expanded.yMin) * 128, .3, "Y expansion uses physical reference metric");
        draft.expansion = -4;
        Rect inset = Bounds(VectorBrushGeometry.Rebuild(draft, size));
        t.Near(4, (inset.xMin - original.xMin) * 128, .3, "X contraction uses physical reference metric");
        t.Near(4, (inset.yMin - original.yMin) * 128, .3, "Y contraction uses physical reference metric");
    }

    static void BordersAndSaddles(TestContext t)
    {
        var full = new VectorBrushDraft { width = 16, height = 16, coverage = new byte[256], tolerance = .25f };
        for (int i = 0; i < full.coverage.Length; i++) full.coverage[i] = 255;
        VectorShape shape = VectorBrushGeometry.Rebuild(full, new Vector2(16, 16));
        t.Equal(1, shape.contours.Count, "Full mask closes at its exterior halo");
        Rect bounds = Bounds(shape);
        t.Near(0, bounds.xMin, .26 / 16, "Full-mask left boundary within cubic fitting tolerance");
        t.Near(1, bounds.yMax, .26 / 16, "Full-mask top boundary within cubic fitting tolerance");
        full.expansion = 2;
        bounds = Bounds(VectorBrushGeometry.Rebuild(full, new Vector2(16, 16)));
        t.True(bounds.xMin < 0 && bounds.xMax > 1 && bounds.yMin < 0 && bounds.yMax > 1, "Expansion is not clipped to the old mask frame");
        var diagonal = new VectorBrushDraft { width = 2, height = 2, coverage = new byte[] { 255, 0, 0, 255 }, tolerance = .25f };
        shape = VectorBrushGeometry.Rebuild(diagonal, new Vector2(2, 2));
        CheckContours(t, shape);
        t.True(Contains(shape, new Vector2(.25f, .25f)) && Contains(shape, new Vector2(.75f, .75f)), "Both diagonal foreground pixels survive the saddle");
        SameGeometry(t, shape, VectorBrushGeometry.Rebuild(diagonal, new Vector2(2, 2)));
        var empty = new VectorBrushDraft { width = 1, height = 1, coverage = new byte[1], expansion = 256 };
        t.Equal(0, VectorBrushGeometry.Rebuild(empty, Vector2.one).contours.Count, "Empty source remains empty under expansion");
    }

    static void Validation(TestContext t)
    {
        var brush = new VectorBrushBuilder();
        Throws<ArgumentOutOfRangeException>(t, () => brush.Begin(1025, 1), "Oversize masks are rejected");
        Throws<ArgumentOutOfRangeException>(t, () => brush.Begin(int.MaxValue, int.MaxValue), "Dimensions cannot overflow allocation checks");
        brush.Begin(16, 16);
        Throws<ArgumentOutOfRangeException>(t, () => brush.AddPoint(new Vector2(float.NaN, 0), 2), "Nonfinite sample rejected");
        Throws<ArgumentOutOfRangeException>(t, () => brush.AddPoint(Vector2.zero, -1), "Negative radius rejected");
        var draft = Disc(16, 16, new Vector2(8, 8), 4);
        draft.tolerance = float.NaN;
        Throws<ArgumentOutOfRangeException>(t, () => draft.Validate(), "Nonfinite tolerance rejected");
        draft.tolerance = 0;
        Throws<ArgumentOutOfRangeException>(t, () => draft.Validate(), "Unbounded tiny-segment tolerance rejected");
        draft.tolerance = 1; draft.expansion = 257;
        Throws<ArgumentOutOfRangeException>(t, () => draft.Validate(), "Excessive expansion rejected");
        draft.expansion = 0;
        Throws<ArgumentOutOfRangeException>(t, () => VectorBrushGeometry.Rebuild(draft, new Vector2(float.PositiveInfinity, 16)), "Nonfinite reference metric rejected");
        draft.expansion = 256;
        Throws<InvalidOperationException>(t, () => VectorBrushGeometry.Rebuild(draft, Vector2.one), "Expanded field allocation is bounded");
        draft.expansion = 0; draft.coverage = new byte[1];
        Throws<ArgumentException>(t, () => draft.Validate(), "Incorrect serialized mask length rejected");
    }

    static void SmoothCurves(TestContext t)
    {
        var brush = new VectorBrushBuilder();
        brush.Begin(128, 96);
        for (int i = 0; i <= 48; i++)
            brush.AddPoint(new Vector2(16 + i * 2, 48 + 16 * Mathf.Sin(i * .13f)), 7);
        VectorBrushDraft draft = brush.Finish();
        byte[] mask = (byte[])draft.coverage.Clone();
        Vector2 size = new Vector2(128, 96);
        draft.tolerance = .25f;
        VectorShape fine = VectorBrushGeometry.Rebuild(draft, size);
        draft.tolerance = 4;
        VectorShape coarse = VectorBrushGeometry.Rebuild(draft, size);
        t.True(PointCount(coarse) < PointCount(fine), "Tolerance changes the cubic anchor density on a curved stroke");
        t.True(PointCount(fine) < 100, "Smooth stroke has editable cubics, not one anchor per boundary pixel");
        draft.tolerance = .25f;
        SameGeometry(t, fine, VectorBrushGeometry.Rebuild(draft, size));
        EqualBytes(t, mask, draft.coverage, "Fine/coarse/fine rebuilds preserve the source mask");
        SameGeometry(t, fine, JsonUtility.FromJson<VectorShape>(JsonUtility.ToJson(fine)));

        foreach (float radius in new[] { 3.5f, 12f, 31f })
        {
            var disc = Disc(80, 80, new Vector2(40.3f, 39.7f), radius);
            disc.tolerance = .25f;
            VectorShape shape = VectorBrushGeometry.Rebuild(disc, new Vector2(80, 80));
            int curved = 0;
            foreach (VectorPoint p in shape.contours[0].points)
            {
                if (p.inTangent.sqrMagnitude > 0 && p.outTangent.sqrMagnitude > 0) curved++;
                t.Equal(VectorTangentMode.Aligned, p.tangentMode, "Round dab joins are smooth, including the closed seam");
                t.Near(-1, Vector2.Dot(p.inTangent.normalized, p.outTangent.normalized), .0002, "Closed dab tangents agree");
            }
            t.True(curved >= 3, "Dab contains actual cubic control handles");
            foreach (Vector2 p in Flatten(shape.contours[0]))
            {
                float distance = Vector2.Distance(p * 80, new Vector2(40.3f, 39.7f));
                t.Near(radius, distance, .65, "Fitted curve follows the AA disc, including between trace samples");
            }
        }
    }

    static void NarrowFeatures(TestContext t)
    {
        // High requested tolerance must not merge close islands or remove a thin hole.
        var draft = new VectorBrushDraft { width = 64, height = 64, coverage = new byte[4096], tolerance = 16 };
        for (int y = 8; y < 56; y++) for (int x = 8; x < 56; x++)
            if (x < 30 || x >= 32) draft.coverage[y * 64 + x] = 255;
        VectorShape shape = VectorBrushGeometry.Rebuild(draft, new Vector2(64, 64));
        t.Equal(2, shape.contours.Count, "Close islands remain separate at coarse tolerance");
        t.True(!Contains(shape, new Vector2(31f / 64, .5f)), "Gap between islands stays open");
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float r = new Vector2(x + .5f - 32, y + .5f - 32).magnitude;
            draft.coverage[y * 64 + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(2 - Mathf.Abs(r - 17)) * 255);
        }
        shape = VectorBrushGeometry.Rebuild(draft, new Vector2(64, 64));
        t.Equal(2, shape.contours.Count, "Thin ring keeps both curves at coarse tolerance");
        t.True(!Contains(shape, new Vector2(.5f, .5f)), "Thin ring hole is not filled by smoothing");
    }

    static VectorBrushDraft Disc(int width, int height, Vector2 center, float radius)
    {
        var brush = new VectorBrushBuilder();
        brush.Begin(width, height); brush.AddPoint(center, radius); return brush.Finish();
    }

    static void Throws<T>(TestContext t, Action operation, string label) where T : Exception
    {
        bool threw = false;
        try { operation(); } catch (T) { threw = true; }
        t.True(threw, label);
    }

    static void EqualBytes(TestContext t, byte[] expected, byte[] actual, string label)
    {
        bool equal = expected.Length == actual.Length;
        if (equal) for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) { equal = false; break; }
        t.True(equal, label);
    }

    static int PointCount(VectorShape shape) { int count = 0; foreach (var contour in shape.contours) count += contour.points.Count; return count; }

    static void CheckContours(TestContext t, VectorShape shape)
    {
        t.True(PointCount(shape) <= VectorBrushGeometry.MaxOutputPoints, "Output points are bounded");
        foreach (var contour in shape.contours)
        {
            t.True(contour.closed && contour.points.Count >= 3, "Editable contour is closed and nondegenerate");
            for (int i = 0; i < contour.points.Count; i++)
            {
                VectorPoint point = contour.points[i], next = contour.points[(i + 1) % contour.points.Count];
                t.True(!float.IsNaN(point.position.x) && !float.IsNaN(point.position.y), "Finite coordinates");
                t.True((point.position - next.position).sqrMagnitude > 0, "No duplicate edge endpoints");
            }
        }
    }

    static void SameGeometry(TestContext t, VectorShape expected, VectorShape actual)
    {
        t.Equal(expected.id, actual.id, "Repeated rebuild retains shape identity");
        t.Equal(expected.contours.Count, actual.contours.Count, "Repeated rebuild retains contour count");
        for (int c = 0; c < expected.contours.Count; c++)
        {
            t.Equal(expected.contours[c].points.Count, actual.contours[c].points.Count, "Repeated rebuild retains point count");
            for (int p = 0; p < expected.contours[c].points.Count; p++)
            {
                t.Equal(expected.contours[c].points[p].position, actual.contours[c].points[p].position, "Reversing controls reconstructs the original geometry exactly");
                t.Equal(expected.contours[c].points[p].inTangent, actual.contours[c].points[p].inTangent, "Rebuild restores incoming cubic handles exactly");
                t.Equal(expected.contours[c].points[p].outTangent, actual.contours[c].points[p].outTangent, "Rebuild restores outgoing cubic handles exactly");
                t.Equal(expected.contours[c].points[p].tangentMode, actual.contours[c].points[p].tangentMode, "Rebuild restores tangent mode");
            }
        }
    }

    static Rect Bounds(VectorShape shape)
    {
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (var contour in shape.contours) foreach (var point in Flatten(contour))
        { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static bool Contains(VectorShape shape, Vector2 point)
    {
        bool inside = false;
        foreach (var contour in shape.contours)
        {
            List<Vector2> points = Flatten(contour);
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                Vector2 a = points[j], b = points[i];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (double)(b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
        }
        return inside;
    }

    static List<Vector2> Flatten(VectorContour contour)
    {
        var result = new List<Vector2>();
        VectorGeometry.FlattenContour(contour, Vector2.one, .0001f, result);
        return result;
    }
}
