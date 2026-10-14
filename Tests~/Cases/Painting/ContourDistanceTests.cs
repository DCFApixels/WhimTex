using System;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class ContourDistanceTests
{
    static readonly MethodInfo ComputeMethod = typeof(SDFLayerBehaviour).Assembly
        .GetType("DCFApixels.WhimTex.DistanceFieldUtility", true)
        .GetMethod("ComputeArtisticDistance", BindingFlags.Public | BindingFlags.Static);

    public static float[] Field(TestContext t, Color32[] source, int w, int h,
        DistanceMetric mode = DistanceMetric.Contour, byte threshold = 128, int channel = 0,
        float miter = 4, float merge = 0)
    {
        using var input = new NativeArray<Color32>(source, Allocator.TempJob);
        using var output = new NativeArray<float>(source.Length, Allocator.TempJob);
        ComputeMethod.Invoke(null, new object[] { input, output, w, h, threshold, channel, mode, miter, merge });
        var result = output.ToArray();
        for (int i = 0; i < source.Length; i++)
        {
            t.True(float.IsFinite(result[i]), "Finite field " + mode);
            t.Equal(source[i], input[i], "Input pixels are borrowed read-only");
        }
        return result;
    }

    static Color32[] Raster(int w, int h, Func<int, int, double> distance, byte threshold = 128, double spread = 2)
    {
        var result = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            byte a = (byte)Math.Max(0, Math.Min(255, Math.Round(threshold - distance(x, y) * 127 / spread)));
            result[y * w + x] = new Color32(a, a, a, a);
        }
        return result;
    }

    public static string Run() => TestContext.Run("Contour AA: finite raw segments, soft ramps and raster boundaries", t =>
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        t.Equal(11, (int)DistanceMetric.Contour, "New mode has its own identity");
        CheckLines(t);
        CheckCurves(t);
        CheckCells(t);
        CheckContacts(t);
        CheckChannelsAndInvariance(t);
        t.True(clock.Elapsed.TotalSeconds < 30, "Bounded contour kernel suite");
    });

    static void CheckLines(TestContext t)
    {
        const int w = 49, h = 45;
        double roundError = 0, contourError = 0;
        foreach (double angle in new[] { 0.0, 17, 45, 79, 90 })
        foreach (byte threshold in new byte[] { 64, 128, 192 })
        foreach (double spread in new[] { 2.0, 5.0 })
        {
            double nx = Math.Cos(angle * Math.PI / 180), ny = Math.Sin(angle * Math.PI / 180);
            double D(int x, int y) => nx * (x - 24) + ny * (y - 22) - .31;
            var input = Raster(w, h, D, threshold, spread);
            var contour = Field(t, input, w, h, threshold: threshold);
            var round = Field(t, input, w, h, DistanceMetric.EuclideanAntialiased, threshold);
            for (int y = 10; y < h - 10; y++) for (int x = 10; x < w - 10; x++)
            {
                double expected = D(x, y);
                if (Math.Abs(expected) > 6) continue;
                t.Near(expected, contour[y * w + x], .035, "Quantized soft half-plane " + angle + "/" + spread + "/" + threshold);
                if (Math.Abs(expected) < 1 && angle != 0 && angle != 90)
                {
                    contourError += Math.Abs(contour[y * w + x] - expected);
                    roundError += Math.Abs(round[y * w + x] - expected);
                }
            }
        }
        t.True(roundError > 1 && contourError < roundError * .4,
            "On linear diagonal ramps segment distance improves the independent near-edge error; not a universal ranking");
    }

    static void CheckCurves(TestContext t)
    {
        const int size = 49;
        foreach (double phase in new[] { .0, .37 })
        foreach (bool ring in new[] { false, true })
        foreach (double spread in new[] { 1.0, 3.0 })
        {
            double D(int x, int y)
            {
                double dx = x - 24 - phase, dy = y - 24 + phase, radius = Math.Sqrt(dx * dx + dy * dy);
                return ring ? Math.Abs(radius - 13) - 1.3 : radius - 13;
            }
            var source = Raster(size, size, D, spread: spread);
            var result = Field(t, source, size, size);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                t.Near(D(x, y), result[y * size + x], .22, "Circle/thin ring keeps finite curved boundary");
                if (Math.Abs(D(x, y)) > .1)
                    t.Equal(D(x, y) < 0, result[y * size + x] < 0, "Circle/ring signs and holes");
            }
            if (ring) t.True(result[24 * size + 24] > 10, "Hole is retained, not filled");
        }
    }

    static double Segment(double x, double y, Vector2 a, Vector2 b)
    {
        double dx = b.x - a.x, dy = b.y - a.y, rx = x - a.x, ry = y - a.y;
        double length = dx * dx + dy * dy;
        double u = length == 0 ? 0 : Math.Max(0, Math.Min(1, (rx * dx + ry * dy) / length));
        return Math.Sqrt((rx - u * dx) * (rx - u * dx) + (ry - u * dy) * (ry - u * dy));
    }

    static void CheckCells(TestContext t)
    {
        // Explicit marching-square edge-pair table, independent of the production
        // extractor and BVH. Equal +/-64 samples put crossings at edge midpoints.
        int[][] pairs = { new int[0], new[] { 0,3 }, new[] { 0,1 }, new[] { 1,3 },
            new[] { 1,2 }, new[] { 0,1,2,3 }, new[] { 0,2 }, new[] { 2,3 },
            new[] { 2,3 }, new[] { 0,2 }, new[] { 0,1,2,3 }, new[] { 1,2 },
            new[] { 1,3 }, new[] { 0,1 }, new[] { 0,3 }, new int[0] };
        var edges = new[] { new Vector2(.5f,0), new Vector2(1,.5f), new Vector2(.5f,1), new Vector2(0,.5f) };
        int[] bits = { 1,2,8,4 };
        for (int mask = 0; mask < 16; mask++)
        {
            var source = new Color32[4];
            for (int i = 0; i < 4; i++) source[i] = new Color32(0, 0, 0, (byte)((mask & bits[i]) != 0 ? 192 : 64));
            var result = Field(t, source, 2, 2);
            for (int i = 0; i < 4; i++)
            {
                double expected = 1e10;
                for (int k = 0; k < pairs[mask].Length; k += 2)
                    expected = Math.Min(expected, Segment(i % 2, i / 2, edges[pairs[mask][k]], edges[pairs[mask][k + 1]]));
                if ((mask & bits[i]) != 0) expected = -expected;
                t.Near(expected, result[i], mask == 0 || mask == 15 ? 10 : .00001, "All 16 cells, finite endpoints " + mask + "/" + i);
            }
        }
        // Unequal diagonal saddle: determinant is negative and must switch pairing.
        var saddle = new[] { new Color32(0,0,0,160), new Color32(0,0,0,64), new Color32(0,0,0,64), new Color32(0,0,0,160) };
        var values = Field(t, saddle, 2, 2);
        t.Near(-1 / (3 * Math.Sqrt(2)), values[0], .00001, "Asymmetric saddle uses its bilinear determinant");
        t.Near(-1 / (3 * Math.Sqrt(2)), values[3], .00001, "Both diagonal islands retained");
    }

    static void CheckContacts(TestContext t)
    {
        var source = new Color32[9];
        for (int i = 0; i < 9; i++) source[i] = new Color32(0,0,0,255);
        source[4] = new Color32(0,0,0,128);
        var point = Field(t, source, 3, 3);
        for (int i = 0; i < 9; i++) t.Near(-Math.Sqrt((i % 3 - 1) * (i % 3 - 1) + (i / 3 - 1) * (i / 3 - 1)), point[i], .00001, "Exact threshold point retained");
        for (int i = 0; i < 9; i++) source[i] = new Color32(0,0,0,0);
        source[4] = new Color32(0,0,0,160);
        var weak = Field(t, source, 3, 3);
        t.Near(-.2 / Math.Sqrt(2), weak[4], .00001, "Subpixel island is not simplified away");
        for (int i = 0; i < 9; i++) if (i != 4) t.True(weak[i] > 0, "Weak island has no extended rays");
        foreach (byte threshold in new byte[] { 0, 1, 128, 254, 255 })
        foreach (byte alpha in new byte[] { 0, 128, 255 })
        {
            for (int i = 0; i < 9; i++) source[i] = new Color32(0,0,0,alpha);
            var uniform = Field(t, source, 3, 3, threshold: threshold);
            foreach (float d in uniform) t.True(alpha > threshold ? d < -1000 : d > 1000, "Uniform mask has no invented boundary");
        }
        foreach (int w in new[] { 1, 9 })
        {
            var line = Raster(w, 10 - w, (x,y) => x + y - 3.3);
            var result = Field(t, line, w, 10 - w);
            for (int i = 0; i < line.Length; i++) t.Near(i - 3.3, result[i], .02, "One-dimensional source measures interpolated crossings");
        }
    }

    static void CheckChannelsAndInvariance(TestContext t)
    {
        const int size = 33;
        var source = Raster(size, size, (x,y) => Math.Sqrt((x-16.3)*(x-16.3)+(y-15.7)*(y-15.7))-8.1);
        var baseline = Field(t, source, size, size);
        var rotated = new Color32[source.Length];
        for (int y=0;y<size;y++) for (int x=0;x<size;x++) rotated[x*size+size-1-y]=source[y*size+x];
        var turns = Field(t, rotated, size, size);
        var ignored = Field(t, source, size, size, miter: 64, merge: 2);
        for (int y=0;y<size;y++) for (int x=0;x<size;x++)
        {
            int i=y*size+x;
            t.Near(baseline[i], turns[x*size+size-1-y], .00001, "Quarter-turn raster stability");
            t.Near(baseline[i], ignored[i], 0, "Miter/merge never enter raw contour construction");
        }
        for (int channel=1;channel<5;channel++)
        {
            var result=Field(t, source, size, size, channel: channel);
            for (int i=0;i<result.Length;i++) t.Near(baseline[i],result[i],.0001,"Selected channel preserves gray threshold geometry");
        }
    }
}
