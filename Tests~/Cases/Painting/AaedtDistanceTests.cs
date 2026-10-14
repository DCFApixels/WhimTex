// Support: Tests~/Framework/TestApi.cs only. CPU NativeArrays; parent owns Unity execution.
// Independent geometric/coverage oracles, no production-derived goldens. At most 160
// kernel calls on images <=65x65, with a cooperative 30-second body budget.
using System;
using System.Diagnostics;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class AaedtDistanceTests
{
    const int MaximumCalls = 160;
    const double BudgetSeconds = 30;
    static readonly MethodInfo ComputeMethod = typeof(SDFLayerBehaviour).Assembly
        .GetType("DCFApixels.WhimTex.DistanceFieldUtility", true)
        .GetMethod("ComputeArtisticDistance", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int),
                typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float), typeof(float) }, null);

    public static string Run() => TestContext.Run("AAEDT + 8SED: bounded independent coverage and geometry regressions", t =>
    {
        var run = new CpuRun(t);
        t.Equal(10, (int)DistanceMetric.AAEDT, "AAEDT has its assigned serialized value");
        t.Equal(10, Enum.GetNames(typeof(DistanceMetric)).Length, "Ten selectable distance modes");
        var label = typeof(DistanceMetric).GetField(nameof(DistanceMetric.AAEDT))
            .GetCustomAttribute<InspectorNameAttribute>();
        t.True(label != null && label.displayName == "AAEDT + 8SED", "AAEDT inspector label");
        t.True(ComputeMethod != null, "Nine-argument artistic distance entry exists");
        CheckAxisEdges(t, run);
        CheckDiagonalCoverage(t, run);
        CheckThresholdNormalization(t, run);
        CheckChannels(t, run);
        CheckDegenerateAndExtreme(t, run);
        CheckGeometry(t, run);
        run.CheckBudget();
        t.True(run.Calls <= MaximumCalls, "Fixed kernel-call bound");
    });

    sealed class CpuRun
    {
        readonly TestContext t;
        readonly Stopwatch clock = Stopwatch.StartNew();
        public int Calls { get; private set; }
        public CpuRun(TestContext context) { t = context; }
        public void CheckBudget() => t.True(clock.Elapsed.TotalSeconds < BudgetSeconds,
            "AAEDT CPU body stays below 30 seconds; elapsed=" + clock.Elapsed.TotalSeconds);

        public float[] Compute(Color32[] input, int width, int height, byte threshold = 128,
            int channel = 0, float miter = 4, float merge = 0)
        {
            CheckBudget();
            t.True(++Calls <= MaximumCalls, "AAEDT call budget");
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var output = new NativeArray<float>(input.Length, Allocator.TempJob);
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = float.NaN;
            ComputeMethod.Invoke(null, new object[] { pixels, output, width, height, threshold, channel,
                DistanceMetric.AAEDT, miter, merge });
            var values = output.ToArray();
            int nonfinite = -1, mutated = -1;
            for (int i = 0; i < input.Length; i++)
            {
                if (!float.IsFinite(values[i])) nonfinite = i;
                if (!input[i].Equals(pixels[i])) mutated = i;
            }
            t.Equal(-1, nonfinite, "AAEDT overwrites every NaN output with a finite distance");
            t.Equal(-1, mutated, "AAEDT preserves every borrowed input channel");
            CheckBudget();
            return values;
        }
    }

    static void CheckAxisEdges(TestContext t, CpuRun run)
    {
        foreach (bool vertical in new[] { false, true })
        foreach (int direction in new[] { -1, 1 })
        foreach (double shift in new[] { -.31, 0, .23 })
        {
            const int w = 37, h = 31;
            double edge = (vertical ? 15 : 18) + shift;
            var input = Raster(w, h, (x, y) => EncodeCoverage(.5 - direction * ((vertical ? y : x) - edge), 128));
            var values = run.Compute(input, w, h);
            double worst = 0;
            for (int y = 2; y < h - 2; y++) for (int x = 2; x < w - 2; x++)
                worst = Math.Max(worst, Math.Abs(values[y * w + x] - direction * ((vertical ? y : x) - edge)));
            t.Near(0, worst, .006, "Analytic AA axis edge, both phases and scan directions: " + vertical + "/" + direction + "/" + shift);
        }
        foreach (bool vertical in new[] { false, true }) foreach (int direction in new[] { -1, 1 })
        {
            const int w = 29, h = 27;
            int firstOutside = vertical ? 13 : 14;
            var input = Raster(w, h, (x, y) => (byte)(direction * ((vertical ? y : x) - firstOutside + .5) < 0 ? 255 : 0));
            var values = run.Compute(input, w, h);
            double worst = 0;
            for (int i = 0; i < values.Length; i++)
                worst = Math.Max(worst, Math.Abs(values[i] - direction * ((vertical ? i / w : i % w) - firstOutside + .5)));
            t.Near(0, worst, .006, "Binary edge lies half a pixel between centers: " + vertical + "/" + direction);
        }
    }

    static void CheckDiagonalCoverage(TestContext t, CpuRun run)
    {
        // Exact area of a unit square cut by x+y=s: a triangle on either side
        // of the diagonal. This tests gradient-aware correction independently.
        const int size = 37;
        foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
        {
            double Distance(double x, double y) => (sx * (x - 18) + sy * (y - 18)) / Math.Sqrt(2) - .21;
            var input = Raster(size, size, (x, y) =>
            {
                double s = -Distance(x, y) * Math.Sqrt(2);
                double area = s <= -1 ? 0 : s >= 1 ? 1 : s <= 0 ? .5 * (s + 1) * (s + 1) : 1 - .5 * (1 - s) * (1 - s);
                return EncodeCoverage(area, 128);
            });
            var values = run.Compute(input, size, size);
            double worst = 0;
            int samples = 0;
            for (int y = 5; y < size - 5; y++) for (int x = 5; x < size - 5; x++)
            {
                double expected = Distance(x, y);
                if (Math.Abs(expected) > 7) continue;
                worst = Math.Max(worst, Math.Abs(values[y * size + x] - expected));
                samples++;
            }
            t.True(samples > 200, "Diagonal oracle covers a substantial band");
            t.Near(0, worst, .12, "Diagonal area coverage and eight-neighbor Euclidean propagation: " + sx + "/" + sy);
        }
    }

    static void CheckThresholdNormalization(TestContext t, CpuRun run)
    {
        foreach (byte threshold in new byte[] { 1, 77, 128, 192, 254 })
        foreach (byte sample in new byte[] { 1, 64, threshold, 192, 254 })
        {
            const int w = 25, h = 7, edge = 12;
            var input = Raster(w, h, (x, y) => x < edge ? (byte)255 : x == edge ? sample : (byte)0);
            var values = run.Compute(input, w, h, threshold);
            // One partially covered column: inversion of the specified piecewise
            // normalization gives its geometric edge, with exact 0/1 endpoints.
            double coverage = sample <= threshold ? .5 * sample / threshold
                : .5 + .5 * (sample - threshold) / (255 - threshold);
            for (int x = 0; x < w; x++)
                t.Near(x - edge + .5 - coverage, values[3 * w + x], .002,
                    "Threshold-normalized planar edge: t=" + threshold + ", byte=" + sample + ", x=" + x);
            if (sample == threshold) t.Near(0, values[3 * w + edge], .00001, "Byte threshold maps exactly to half coverage");
        }
    }

    static void CheckChannels(TestContext t, CpuRun run)
    {
        const int w = 29, h = 9;
        for (int channel = 0; channel < 5; channel++)
        {
            var input = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                byte a = EncodeCoverage(.5 - (x - 14.23), 128);
                input[y * w + x] = channel == 0 ? new Color32(0, 255, 0, a)
                    : channel == 1 ? new Color32(a, 0, 255, 0)
                    : channel == 2 ? new Color32(255, a, 0, 0)
                    : channel == 3 ? new Color32(0, 255, a, 0) : new Color32(a, a, a, 0);
            }
            var values = run.Compute(input, w, h, channel: channel);
            for (int x = 0; x < w; x++) t.Near(x - 14.23, values[4 * w + x], .006, "Selected source channel " + channel + "/" + x);
        }
        var colored = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            colored[y * w + x] = x < 14 ? new Color32(255, 255, 255, 0)
                : x == 14 ? new Color32(255, 64, 0, 255) : new Color32(0, 0, 0, 255);
        var luminance = run.Compute(colored, w, h, channel: 4);
        double centerCoverage = .5 * (.2126 * 255 + .7152 * 64) / 128;
        for (int x = 0; x < w; x++) t.Near(x - 14 + .5 - centerCoverage, luminance[4 * w + x], .002,
            "Luminance uses RGB weights and ignores opposite alpha at " + x);
    }

    static void CheckDegenerateAndExtreme(TestContext t, CpuRun run)
    {
        foreach (var size in new[] { new Vector2Int(1, 1), new Vector2Int(1, 17), new Vector2Int(17, 1), new Vector2Int(2, 2) })
        foreach (byte alpha in new byte[] { 0, 32, 128, 224, 255 })
        {
            var input = Raster(size.x, size.y, (x, y) => alpha);
            var values = run.Compute(input, size.x, size.y);
            foreach (float value in values) t.Equal(alpha > 128 ? -1e10f : 1e10f, value,
                "One-class mask has a finite signed sentinel: " + size + "/" + alpha);
        }
        foreach (bool vertical in new[] { false, true })
        foreach (byte threshold in new byte[] { 77, 128, 192 })
        {
            int w = vertical ? 1 : 17, h = vertical ? 17 : 1;
            var input = Raster(w, h, (x, y) => EncodeCoverage(.5 - (x + y - 8.23), threshold));
            var values = run.Compute(input, w, h, threshold);
            for (int i = 0; i < values.Length; i++) t.Near(i - 8.23, values[i], .008, "AAEDT 1D coverage distance: " + vertical + "/" + threshold);
        }
        foreach (byte threshold in new byte[] { 0, 255 })
        foreach (byte alpha in new byte[] { 0, 1, 128, 255 })
        {
            var values = run.Compute(Raster(3, 2, (x, y) => alpha), 3, 2, threshold);
            foreach (float value in values) t.Equal(alpha > threshold ? -1e10f : 1e10f, value,
                "Extreme threshold hard classification: " + threshold + "/" + alpha);
        }
        foreach (bool vertical in new[] { false, true })
        {
            int w = vertical ? 1 : 17, h = vertical ? 17 : 1;
            var input = Raster(w, h, (x, y) => (byte)(x + y < 8 ? 1 : 0));
            var values = run.Compute(input, w, h, 0);
            for (int i = 0; i < values.Length; i++) t.Near(i - 7.5, values[i], .006, "Threshold zero treats byte one as fully covered");
            foreach (float value in run.Compute(input, w, h, 255)) t.Equal(1e10f, value, "Threshold 255 is wholly outside");
        }
    }

    static void CheckGeometry(TestContext t, CpuRun run)
    {
        const int size = 65;
        for (int shape = 0; shape < 4; shape++)
        {
            Vector2[] polygon = shape < 2 ? null : Star(shape == 2 ? 5 : 13, shape == 2 ? .5 : .364);
            double Distance(double x, double y)
            {
                if (polygon != null) return PolygonDistance(polygon, x, y);
                double r = Math.Sqrt((x - 31.23) * (x - 31.23) + (y - 32.37) * (y - 32.37));
                return shape == 0 ? r - 19.4 : Math.Max(r - 21.4, 9.2 - r);
            }
            // Area samples are independent input, never a transform-derived mask.
            var input = Raster(size, size, (x, y) =>
            {
                int inside = 0;
                for (int sy = 0; sy < 6; sy++) for (int sx = 0; sx < 6; sx++)
                    if (Distance(x + (sx + .5) / 6 - .5, y + (sy + .5) / 6 - .5) < 0) inside++;
                return (byte)Math.Round(255.0 * inside / 36);
            });
            foreach (byte threshold in new byte[] { 77, 128, 192 })
            {
                var values = run.Compute(input, size, size, threshold);
                Same(t, values, run.Compute(input, size, size, threshold), 0, "Identical AAEDT input is deterministic");
                double worst = 0;
                int badSigns = 0, farSamples = 0, insideSamples = 0;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    double expected = Distance(x, y);
                    float actual = values[y * size + x];
                    worst = Math.Max(worst, Math.Abs(actual - expected));
                    // Boundary exclusions depend on the analytic geometry only.
                    if (Math.Abs(expected) > 1 && (actual < 0) != (expected < 0)) badSigns++;
                    if (expected > 8) farSamples++;
                    if (expected < -2) insideSamples++;
                }
                t.Equal(0, badSigns, "Analytic circle/ring/star sign: " + shape + "/" + threshold);
                t.True(farSamples > 500 && insideSamples > 50, "Safety oracle covers far exterior and actual interior");
                t.Near(0, worst, shape < 2 ? 1 : 3, "Full-domain finite-distance safety bound: " + shape + "/" + threshold);
                if (shape == 3 && threshold == 128)
                {
                    foreach (float miter in new[] { 1f, 64f }) foreach (float merge in new[] { 0f, 1f, 2f })
                        Same(t, values, run.Compute(input, size, size, threshold, miter: miter, merge: merge), 0,
                            "AAEDT ignores valid miter/merge settings");
                    var mirrored = new Color32[input.Length];
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) mirrored[x * size + size - 1 - y] = input[y * size + x];
                    var rotated = run.Compute(mirrored, size, size);
                    double error = 0;
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                        error = Math.Max(error, Math.Abs(values[y * size + x] - rotated[x * size + size - 1 - y]));
                    t.Near(0, error, .02, "Converged eight-neighbor propagation is stable under quarter turns");
                }
            }
        }
    }

    static void Same(TestContext t, float[] expected, float[] actual, double tolerance, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " length");
        double error = 0;
        for (int i = 0; i < expected.Length; i++) error = Math.Max(error, Math.Abs(expected[i] - actual[i]));
        t.Near(0, error, tolerance, label);
    }

    static Color32[] Raster(int width, int height, Func<int, int, byte> sample)
    {
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            pixels[y * width + x] = new Color32(17, 91, 203, sample(x, y));
        return pixels;
    }

    static byte EncodeCoverage(double coverage, byte threshold)
    {
        coverage = Math.Max(0, Math.Min(1, coverage));
        return (byte)Math.Round(coverage <= .5 ? 2 * threshold * coverage
            : threshold + 2 * (255 - threshold) * (coverage - .5));
    }

    static Vector2[] Star(int tips, double inner)
    {
        var points = new Vector2[tips * 2];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = .31 + i * Math.PI / tips, radius = 23.7 * (i % 2 == 0 ? 1 : inner);
            points[i] = new Vector2((float)(31.23 + radius * Math.Cos(angle)), (float)(32.37 + radius * Math.Sin(angle)));
        }
        return points;
    }

    static double PolygonDistance(Vector2[] points, double x, double y)
    {
        bool inside = false;
        double best = double.PositiveInfinity;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            var a = points[j]; var b = points[i];
            if ((a.y > y) != (b.y > y) && x < a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
            double dx = b.x - (double)a.x, dy = b.y - (double)a.y;
            double u = Math.Max(0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy)));
            double ex = x - a.x - u * dx, ey = y - a.y - u * dy;
            best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
        }
        return inside ? -best : best;
    }
}
