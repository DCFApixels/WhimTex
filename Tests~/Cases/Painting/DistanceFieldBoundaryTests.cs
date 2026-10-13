// Explicit support: Framework/TestApi.cs and Cases/Painting/DistanceFieldTests.cs
// (DistanceFieldAnalyticFixture only; do not invoke that case's Run).
// Owned CPU buffers only. The parent registers/compiles/runs this case separately.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class DistanceFieldBoundaryTests
{
    const int Size = 64, BudgetMilliseconds = 30000;
    static readonly byte[] Thresholds = { 64, 96, 118, 128, 143, 160, 192 };
    static readonly DistanceMetric[] Modes = {
        DistanceMetric.EuclideanAntialiased, DistanceMetric.Sharp, DistanceMetric.Bevel,
        DistanceMetric.Diamond, DistanceMetric.Square, DistanceMetric.MTSDF
    };

    public static string Run() => TestContext.Run("Bounded six-mode intermediate-threshold and raster-boundary regression", t =>
    {
        var api = new OwnedApi(t);
        CheckHalfPlanes(t, api);
        CheckClosedShapes(t, api);
        CheckSmallPatches(t, api);
        CheckNearbyTinyContour(t, api);
        t.True(api.Calls <= 590, "At most 590 bounded CPU invocations; actual=" + api.Calls);
        api.CheckTime();
    });

    sealed class OwnedApi
    {
        readonly TestContext test;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly MethodInfo scalar, multi, build;
        public int Calls { get; private set; }

        public OwnedApi(TestContext test)
        {
            this.test = test;
            var assembly = typeof(SDFLayerBehaviour).Assembly;
            var utility = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility", true);
            var artistic = assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
            const BindingFlags entry = BindingFlags.Public | BindingFlags.Static;
            scalar = utility.GetMethod("ComputeArtisticDistance", entry, null, new[] {
                typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int),
                typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float) }, null);
            multi = utility.GetMethod("ComputeMtsdfDistance", entry, null, new[] {
                typeof(NativeArray<Color32>), typeof(NativeArray<float4>), typeof(int), typeof(int),
                typeof(byte), typeof(int) }, null);
            build = artistic.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static, null, new[] {
                typeof(NativeArray<Color32>), typeof(int), typeof(int), typeof(byte), typeof(int), typeof(DistanceMetric) }, null);
            test.True(scalar != null && multi != null && build != null, "Required package-owned SDF entries exist");
        }

        public void CheckTime() => test.True(clock.ElapsedMilliseconds < BudgetMilliseconds,
            "Case CPU budget is 30s (including job startup); elapsed=" + clock.ElapsedMilliseconds + "ms, calls=" + Calls);

        public float[] Scalar(Color32[] input, int width, int height, byte threshold, DistanceMetric mode, string label)
        {
            CheckTime(); Calls++;
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var output = new NativeArray<float>(input.Length, Allocator.TempJob);
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = float.NaN;
            scalar.Invoke(null, new object[] { pixels, output, width, height, threshold, 0, mode, 4f });
            var result = output.ToArray();
            int nonfinite = 0, signs = 0, firstSign = -1, changed = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if (!Finite(result[i])) nonfinite++;
                if (BadSign(input[i].a, threshold, result[i])) { signs++; if (firstSign < 0) firstSign = i; }
                if (!input[i].Equals(pixels[i])) changed++;
            }
            test.Equal(0, nonfinite, label + " writes every scalar sample finitely");
            test.Equal(0, signs, label + " follows source alpha > threshold, with equality outside/on boundary; pixel=" + firstSign +
                ", value=" + (firstSign < 0 ? 0 : result[firstSign]) + ", alpha=" + (firstSign < 0 ? 0 : input[firstSign].a));
            test.Equal(0, changed, label + " preserves source samples");
            return result;
        }

        public float4[] Multi(Color32[] input, int width, int height, byte threshold, float[] scalarValues, string label)
        {
            CheckTime(); Calls++;
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var output = new NativeArray<float4>(input.Length, Allocator.TempJob);
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = new float4(float.NaN);
            multi.Invoke(null, new object[] { pixels, output, width, height, threshold, 0 });
            var result = output.ToArray();
            int nonfinite = 0, signs = 0, changed = 0;
            double medianError = 0;
            for (int i = 0; i < result.Length; i++)
            {
                for (int c = 0; c < 4; c++) if (!Finite(result[i][c])) nonfinite++;
                float median = Median(result[i]);
                if (BadSign(input[i].a, threshold, median) || BadSign(input[i].a, threshold, result[i].w)) signs++;
                medianError = Math.Max(medianError, Math.Abs(median - scalarValues[i]));
                if (!input[i].Equals(pixels[i])) changed++;
            }
            // Individual RGB channels may have opposite signs at colored corners.
            test.Equal(0, nonfinite, label + " writes every RGBA component finitely");
            test.Equal(0, signs, label + " alpha and RGB median follow source signs");
            test.Equal(0, changed, label + " RGBA computation preserves input");
            test.Near(0, medianError, .0002, label + " scalar consumes RGB median");
            int interpolationErrors = 0, interpolated = 0;
            for (int y = 0; y + 1 < height; y++) for (int x = 0; x + 1 < width; x++)
            {
                float4 a = result[y * width + x], b = result[y * width + x + 1];
                float4 c = result[(y + 1) * width + x], d = result[(y + 1) * width + x + 1];
                float sign = a.w < 0 ? -1 : 1;
                // Away from the boundary, all four true distances certify the same
                // side. Individual RGB channels need not have that sign at a corner.
                if (math.min(math.min(sign * a.w, sign * b.w), math.min(sign * c.w, sign * d.w)) <= 1) continue;
                foreach (float2 phase in new[] { new float2(.25f), new float2(.5f), new float2(.75f, .25f) })
                {
                    float4 sample = math.lerp(math.lerp(a, b, phase.x), math.lerp(c, d, phase.x), phase.y);
                    if (Median(sample) * sign <= 0) interpolationErrors++;
                    interpolated++;
                }
            }
            if (width > 3) test.True(interpolated > 100, label + " tests fractional bilinear samples away from the contour");
            test.Equal(0, interpolationErrors, label + " RGB interpolation does not invent a contour away from alpha's boundary");
            return result;
        }

        public FeatureCopy[] Geometry(Color32[] input, int width, int height, byte threshold, DistanceMetric mode, string label)
        {
            CheckTime(); Calls++;
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var geometry = (IDisposable)build.Invoke(null, new object[] { pixels, width, height, threshold, 0, mode });
            // Nonpublic reflection is restricted to package-owned Build/Geometry/Feature.
            // NativeArray is inspected through its PUBLIC Length and Item properties.
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
            object array = geometry.GetType().GetField("features", fields).GetValue(geometry);
            Type arrayType = array.GetType(), featureType = arrayType.GetGenericArguments()[0];
            var item = arrayType.GetProperty("Item", fields);
            var a = featureType.GetField("a", fields); var b = featureType.GetField("b", fields);
            var previous = featureType.GetField("previousDirection", fields);
            var vertex = featureType.GetField("vertex", fields);
            var result = new FeatureCopy[(int)arrayType.GetProperty("Length", fields).GetValue(array)];
            int nonfinite = 0, changed = 0;
            for (int i = 0; i < result.Length; i++)
            {
                object feature = item.GetValue(array, new object[] { i });
                result[i] = new FeatureCopy { a = (float2)a.GetValue(feature), b = (float2)b.GetValue(feature),
                    previous = (float2)previous.GetValue(feature), vertex = (int)vertex.GetValue(feature) };
                if (!Finite(result[i].a.x) || !Finite(result[i].a.y) || !Finite(result[i].b.x) || !Finite(result[i].b.y)) nonfinite++;
            }
            for (int i = 0; i < input.Length; i++) if (!input[i].Equals(pixels[i])) changed++;
            test.True(result.Length > 0 && result.Length < 4096, label + " has bounded nonempty geometry");
            test.Equal(0, nonfinite, label + " geometry has finite endpoints");
            test.Equal(0, changed, label + " geometry construction preserves input");
            return result; // All native geometry has been disposed before returning.
        }
    }

    struct FeatureCopy { public float2 a, b, previous; public int vertex; }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    // Reconstructed corners may move a partially covered sample onto the zero set;
    // their documented subpixel error budget does not promise exact coverage recovery.
    // A wrong nonzero sign is forbidden. Weak-island fixtures below require strict interior distance.
    static bool BadSign(byte alpha, byte threshold, float value) => alpha > threshold ? value > 1e-5f : value < -1e-5f;
    static float Median(float4 f) => Math.Max(Math.Min(f.x, f.y), Math.Min(Math.Max(f.x, f.y), f.z));

    static void CheckHalfPlanes(TestContext t, OwnedApi api)
    {
        foreach (double angle in new[] { 0.0, 31.0 })
        {
            double nx = Math.Cos(angle * Math.PI / 180), ny = Math.Sin(angle * Math.PI / 180);
            double phase = angle == 0 ? .23 : -.61;
            double Distance(int x, int y) => nx * (x - 31.5) + ny * (y - 31.5) - phase;
            const double spread = 3;
            var input = DistanceFieldAnalyticFixture.Raster(Size, Size, Distance, 128, spread);
            var previous = new float[Modes.Length][];
            for (int ti = 0; ti < Thresholds.Length; ti++)
            {
                byte threshold = Thresholds[ti];
                Topology topology = AnalyzeTopology(input, Size, Size, threshold);
                t.True(topology.objects == 1 && topology.holes == 0, "Half-plane threshold sweep preserves one component without holes");
                var seeds = CrossingSeeds(input, Size, Size, threshold);
                for (int mi = 0; mi < Modes.Length; mi++)
                {
                    DistanceMetric mode = Modes[mi];
                    string label = $"Half-plane {mode}, angle={angle}, phase={phase}, threshold={threshold}";
                    var values = api.Scalar(input, Size, Size, threshold, mode, label);
                    double divisor = mode == DistanceMetric.Diamond ? Math.Max(Math.Abs(nx), Math.Abs(ny))
                        : mode == DistanceMetric.Square ? Math.Abs(nx) + Math.Abs(ny) : 1;
                    double maximum = 0; int count = 0;
                    for (int y = 10; y < Size - 10; y++) for (int x = 10; x < Size - 10; x++)
                    {
                        double expected = (Distance(x, y) + (threshold - 128) * spread / 127) / divisor;
                        if (Math.Abs(expected) > 6) continue;
                        maximum = Math.Max(maximum, Math.Abs(values[y * Size + x] - expected)); count++;
                    }
                    t.True(count > 400, label + " has a substantial interior analytic band");
                    t.Near(0, maximum, mode == DistanceMetric.EuclideanAntialiased ? .55 : .35, label + " dual-norm line error");
                    if (mode == DistanceMetric.EuclideanAntialiased) CheckSeedOracle(t, input, values, seeds, Size, Size, threshold, label);
                    if (mode == DistanceMetric.MTSDF)
                    {
                        var rgba = api.Multi(input, Size, Size, threshold, values, label);
                        double alphaError = 0;
                        for (int y = 10; y < Size - 10; y++) for (int x = 10; x < Size - 10; x++)
                        {
                            double expected = Distance(x, y) + (threshold - 128) * spread / 127;
                            if (Math.Abs(expected) <= 6) alphaError = Math.Max(alphaError, Math.Abs(rgba[y * Size + x].w - expected));
                        }
                        t.Near(0, alphaError, .35, label + " alpha measures the Euclidean line");
                    }
                    if (ti > 0)
                    {
                        // Compare only the same interior band: finite line endpoints at
                        // the raster border are not an infinite half-plane oracle.
                        double jump = 0;
                        for (int y = 10; y < Size - 10; y++) for (int x = 10; x < Size - 10; x++)
                            if (Math.Abs(Distance(x, y)) <= 5)
                                jump = Math.Max(jump, Math.Abs(values[y * Size + x] - previous[mi][y * Size + x]));
                        double displacement = (threshold - Thresholds[ti - 1]) * spread / (127 * divisor);
                        t.Near(0, jump, displacement + 1.1, label + " bounded response for unchanged half-plane topology");
                    }
                    previous[mi] = values;
                }
            }
        }
    }

    static void CheckClosedShapes(TestContext t, OwnedApi api)
    {
        var bevel = DistanceFieldAnalyticFixture.Rotate(new[] {
            new Vector2(-14, -11), new Vector2(14, -11), new Vector2(14, 8),
            new Vector2(11, 11), new Vector2(-11, 11), new Vector2(-14, 8)
        }, 27, new Vector2(32.61f, 31.13f));
        var thin = DistanceFieldAnalyticFixture.Rotate(DistanceFieldAnalyticFixture.Rectangle(-16, -.65f, 16, .65f),
            61, new Vector2(43.23f, 31.61f));
        var shapes = new[] {
            new[] { DistanceFieldAnalyticFixture.Rectangle(6.25f, 6.25f, 34.25f, 47.25f),
                DistanceFieldAnalyticFixture.Rectangle(14.25f, 16.25f, 26.25f, 36.25f),
                DistanceFieldAnalyticFixture.Rectangle(44.25f, 25.25f, 57.25f, 44.25f) },
            new[] { bevel },
            new[] { DistanceFieldAnalyticFixture.Rectangle(7.2f, 10.3f, 24.2f, 52.3f),
                DistanceFieldAnalyticFixture.Rectangle(14.4f, 17.2f, 16.0f, 44.8f), thin },
            new[] { Circle(32.23f, 31.61f, 15.1f) }
        };
        string[] names = { "hole and disjoint island", "resolved three-pixel bevel", "thin slit and rotated strip", "smooth circle" };
        for (int si = 0; si < shapes.Length; si++)
        {
            bool circle = si == 3, stable = si != 2;
            double spread = circle ? 2 : 1;
            double Distance(int x, int y) => circle ? Math.Sqrt((x - 32.23) * (x - 32.23) + (y - 31.61) * (y - 31.61)) - 15.1
                : DistanceFieldAnalyticFixture.Distance(shapes[si], x, y, 0);
            var input = DistanceFieldAnalyticFixture.Raster(Size, Size, Distance, 128, spread);
            var previous = new float[Modes.Length][];
            Topology previousTopology = default;
            for (int ti = 0; ti < Thresholds.Length; ti++)
            {
                byte threshold = Thresholds[ti];
                var seeds = CrossingSeeds(input, Size, Size, threshold);
                Topology topology = AnalyzeTopology(input, Size, Size, threshold);
                if (stable) t.True(topology.objects == (si == 0 ? 2 : 1) && topology.holes == (si == 0 ? 1 : 0),
                    names[si] + "/" + threshold + " retains its independently counted components and holes");
                // Circle level sets are circles of this radius. General polygon offset
                // level sets need not reproduce their ideal vector vertices exactly.
                var oracle = circle ? new[] { Circle(32.23f, 31.61f, (float)(15.1 + (128 - threshold) * spread / 127)) } : shapes[si];
                var euclidean = new double[Size * Size];
                var diamond = new double[Size * Size]; var square = new double[Size * Size];
                var seedDistances = new double[Size * Size];
                var cornerValues = new float[Modes.Length];
                var cornerProbe = DistanceFieldAnalyticFixture.Rotate(new[] { new Vector2(18, -15) }, 27, new Vector2(32.61f, 31.13f))[0];
                int cornerX = Mathf.RoundToInt(cornerProbe.x), cornerY = Mathf.RoundToInt(cornerProbe.y);
                for (int y = 0; y < Size; y += 3) for (int x = 0; x < Size; x += 3)
                {
                    int i = y * Size + x;
                    seedDistances[i] = SeedMagnitude(seeds, x, y);
                    euclidean[i] = DistanceFieldAnalyticFixture.Distance(oracle, x, y, 0);
                    diamond[i] = DistanceFieldAnalyticFixture.Distance(oracle, x, y, 1);
                    square[i] = DistanceFieldAnalyticFixture.Distance(oracle, x, y, 2);
                }
                for (int mi = 0; mi < Modes.Length; mi++)
                {
                    DistanceMetric mode = Modes[mi];
                    string label = $"{names[si]}/{mode}/threshold={threshold}";
                    var values = api.Scalar(input, Size, Size, threshold, mode, label);
                    if (mode == DistanceMetric.EuclideanAntialiased) CheckSeedOracle(t, input, values, seeds, Size, Size, threshold, label);
                    double error = 0; int errorIndex = -1, remote = 0, falseFields = 0, excessive = 0;
                    for (int y = 0; y < Size; y += 3) for (int x = 0; x < Size; x += 3)
                    {
                        int i = y * Size + x;
                        double rasterDistance = seedDistances[i], magnitude = Math.Abs(values[i]);
                        // Angles here are moderate. A 4x envelope accommodates artistic
                        // joins; a remote support line returning almost zero does not.
                        if (rasterDistance > 12 && input[i].a < threshold)
                        { remote++; if (magnitude < .2 * (rasterDistance - 1.5)) falseFields++; }
                        if (magnitude > 4 * (rasterDistance + 1.5)) excessive++;
                        if (!stable) continue; // Narrow details may change topology.
                        if (mode == DistanceMetric.Diamond || mode == DistanceMetric.Square)
                        {
                            double e = Math.Abs(values[i] - (mode == DistanceMetric.Diamond ? diamond[i] : square[i]));
                            if (e > error) { error = e; errorIndex = i; }
                        }
                        else if (circle && mode != DistanceMetric.EuclideanAntialiased)
                            error = Math.Max(error, Math.Abs(values[i] - euclidean[i]));
                    }
                    t.True(remote > 20, label + " samples remote exterior as well as the edge");
                    t.Equal(0, falseFields, label + " has no spurious remote field");
                    t.Equal(0, excessive, label + " has a bounded geometric magnitude");
                    if (stable && (circle || mode == DistanceMetric.Diamond || mode == DistanceMetric.Square))
                        t.Near(0, error, circle ? 1.1 : 1.7, label + " mode-appropriate analytic envelope; pixel=" + errorIndex +
                            ", actual=" + (errorIndex < 0 ? 0 : values[errorIndex]) + ", expected=" +
                            (errorIndex < 0 ? 0 : mode == DistanceMetric.Diamond ? diamond[errorIndex] : square[errorIndex]));
                    if (mode == DistanceMetric.MTSDF)
                    {
                        var rgba = api.Multi(input, Size, Size, threshold, values, label);
                        double alphaError = 0;
                        for (int y = 0; y < Size; y += 3) for (int x = 0; x < Size; x += 3)
                        {
                            int i = y * Size + x;
                            double expected = stable ? euclidean[i] : (input[i].a > threshold ? -1 : 1) * seedDistances[i];
                            alphaError = Math.Max(alphaError, Math.Abs(rgba[i].w - expected));
                        }
                        t.Near(0, alphaError, circle ? .85 : 1.6, label + " alpha contour-distance envelope");
                    }
                    if (stable && ti > 0)
                    {
                        t.True(topology.Same(previousTopology), label + " topology is unchanged before comparing threshold response");
                        double jump = 0; int jumpIndex = -1;
                        for (int i = 0; i < values.Length; i++)
                            if (Math.Abs(values[i] - previous[mi][i]) > jump) { jump = Math.Abs(values[i] - previous[mi][i]); jumpIndex = i; }
                        double displacement = (threshold - Thresholds[ti - 1]) * spread / 127;
                        t.Near(0, jump, 2.5 + 4 * displacement, label + " bounded numerical response on fixed coverage; pixel=" + jumpIndex +
                            ", current=" + (jumpIndex < 0 ? 0 : values[jumpIndex]) + ", previous=" + (jumpIndex < 0 ? 0 : previous[mi][jumpIndex]));
                    }
                    previous[mi] = values;
                    if (si == 0)
                    {
                        t.True(values[26 * Size + 20] > 4, label + " retains the hole");
                        t.True(values[34 * Size + 40] > 3, label + " retains the inter-island gap");
                        t.True(values[34 * Size + 51] < -4, label + " retains the remote island interior");
                        if (mode == DistanceMetric.MTSDF)
                            t.Near(7.75 + (threshold - 128) / 127.0, values[55 * Size + 42], .75,
                                label + " combines independent contour medians rather than switching by Euclidean color-channel proximity");
                    }
                    if (si == 1 && (mode == DistanceMetric.Sharp || mode == DistanceMetric.Bevel || mode == DistanceMetric.MTSDF))
                        CheckBevelGeometry(t, api.Geometry(input, Size, Size, threshold, mode, label), bevel, label);
                    if (si == 1)
                    {
                        cornerValues[mi] = values[cornerY * Size + cornerX];
                        double a = 27 * Math.PI / 180, px = cornerX - 32.61, py = cornerY - 31.13;
                        double offset = (128 - threshold) / 127.0;
                        double dx = px * Math.Cos(a) + py * Math.Sin(a) - 14 - offset;
                        double dy = px * Math.Sin(a) - py * Math.Cos(a) - 11 - offset;
                        t.True(dx > 3 && dy > 3, label + " hard-corner probe is beyond both incident edges");
                        // Only a local, well-supported right-angle fan has these join
                        // formulas. They are not Euclidean oracles for the whole image.
                        if (mode == DistanceMetric.Sharp || mode == DistanceMetric.MTSDF)
                            t.Near(Math.Max(dx, dy), cornerValues[mi], 1.0, label + " hard corner follows sharp incident support planes");
                        else if (mode == DistanceMetric.Bevel)
                            t.Near(dx + dy, cornerValues[mi], 1.4, label + " hard corner follows the bevel bisector");
                        else if (mode == DistanceMetric.EuclideanAntialiased)
                            t.Near(Math.Sqrt(dx * dx + dy * dy), cornerValues[mi], .85, label + " hard corner has bounded radial raster error");
                    }
                }
                if (si == 1)
                {
                    t.True(cornerValues[1] + .4 < cornerValues[0], "Sharp differs from Round at a supported rotated hard corner, threshold=" + threshold);
                    t.True(cornerValues[0] + .4 < cornerValues[2], "Bevel differs from Round at a supported rotated hard corner, threshold=" + threshold);
                }
                previousTopology = topology;
            }
        }
    }

    static Vector2[] Circle(float cx, float cy, float radius)
    {
        // 128 chords differ radially from the analytic circle by < .006 pixels.
        var result = new Vector2[128];
        for (int i = 0; i < result.Length; i++)
        { double a = 2 * Math.PI * i / result.Length; result[i] = new Vector2(cx + radius * (float)Math.Cos(a), cy + radius * (float)Math.Sin(a)); }
        return result;
    }

    static void CheckBevelGeometry(TestContext t, FeatureCopy[] features, Vector2[] polygon, string label)
    {
        double maximumDeviation = 0, nearestCut = double.PositiveInfinity;
        // The midpoint of the actual cut must have a nearby finite segment. Restoring
        // the absent square tip would miss it by 1.5 px, even before testing endpoints.
        var middle = (polygon[2] + polygon[3]) * .5f;
        foreach (var f in features)
        {
            maximumDeviation = Math.Max(maximumDeviation, Math.Abs(DistanceFieldAnalyticFixture.Distance(new[] { polygon }, f.a.x, f.a.y, 0)));
            maximumDeviation = Math.Max(maximumDeviation, Math.Abs(DistanceFieldAnalyticFixture.Distance(new[] { polygon }, f.b.x, f.b.y, 0)));
            if (f.vertex == 0) nearestCut = Math.Min(nearestCut, SegmentMagnitude(f.a, f.b, middle.x, middle.y));
        }
        t.Near(0, maximumDeviation, 1.3, label + " preserves resolved bevel endpoints within threshold/raster uncertainty");
        t.Near(0, nearestCut, 1.0, label + " retains the genuine bevel segment");
    }

    static void CheckSmallPatches(TestContext t, OwnedApi api)
    {
        foreach (byte threshold in Thresholds) for (int patch = 0; patch < 4; patch++)
        {
            byte high = (byte)(threshold + 32), low = (byte)(threshold - 32);
            var alpha = patch == 0 ? new[] { high, high, high, high, threshold, high, high, high, high }
                : patch == 1 ? new[] { threshold, low, threshold, high, threshold, high, threshold, low, threshold }
                : patch == 2 ? new byte[] { 0, 0, 0, 0, (byte)(threshold + 1), 0, 0, 0, 0 }
                : new[] { high, high, high, threshold, threshold, threshold, high, high, high };
            // Alternate a quarter-turn across thresholds. Only grid-preserving rotations
            // share an exact L1/L-infinity oracle; arbitrary rotations above use dual norms.
            var input = new Color32[9];
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                int i = threshold % 3 == 0 ? x * 3 + 2 - y : y * 3 + x;
                input[i] = new Color32(255, 255, 255, alpha[y * 3 + x]);
            }
            var seeds = CrossingSeeds(input, 3, 3, threshold);
            double radius = 1.0 / (threshold + 1);
            foreach (var mode in Modes)
            {
                string label = $"3x3 patch={patch}/{mode}/threshold={threshold}";
                var values = api.Scalar(input, 3, 3, threshold, mode, label);
                double maximum = 0;
                if (patch == 0)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        int dx = Math.Abs(i % 3 - 1), dy = Math.Abs(i / 3 - 1);
                        double expected = mode == DistanceMetric.Diamond ? dx + dy : mode == DistanceMetric.Square ? Math.Max(dx, dy) : Math.Sqrt(dx * dx + dy * dy);
                        maximum = Math.Max(maximum, Math.Abs(values[i] + expected));
                    }
                }
                else if (patch == 1)
                {
                    maximum = Math.Abs(values[4]);
                    double magnitude = mode == DistanceMetric.EuclideanAntialiased || mode == DistanceMetric.Diamond ? 1
                        : mode == DistanceMetric.Square ? .5 : 1 / Math.Sqrt(2);
                    foreach (int i in new[] { 1, 3, 5, 7 })
                        maximum = Math.Max(maximum, Math.Abs(values[i] - (input[i].a > threshold ? -magnitude : magnitude)));
                }
                else if (patch == 2)
                {
                    double expected = mode == DistanceMetric.EuclideanAntialiased || mode == DistanceMetric.Diamond ? -radius
                        : mode == DistanceMetric.Square ? -radius / 2 : -radius / Math.Sqrt(2);
                    maximum = Math.Abs(values[4] - expected);
                }
                else
                {
                    // Two coincident opposite branches enclose zero area. A filled
                    // region union must still retain this finite threshold line.
                    for (int i = 0; i < 9; i++)
                    {
                        double expected = -Math.Abs((threshold % 3 == 0 ? i % 3 : i / 3) - 1);
                        maximum = Math.Max(maximum, Math.Abs(values[i] - expected));
                    }
                }
                t.Near(0, maximum, .0002, label + " equality/degree-four/weak-island/coincident-line geometric oracle");
                if (mode == DistanceMetric.EuclideanAntialiased) CheckSeedOracle(t, input, values, seeds, 3, 3, threshold, label);
                if (mode == DistanceMetric.MTSDF)
                {
                    var rgba = api.Multi(input, 3, 3, threshold, values, label);
                    t.Near(patch == 2 ? -radius / Math.Sqrt(2) : 0, rgba[4].w, .0002, label + " alpha center oracle");
                    if (patch == 1) foreach (int i in new[] { 1, 3, 5, 7 })
                        t.Near(input[i].a > threshold ? -1 / Math.Sqrt(2) : 1 / Math.Sqrt(2), rgba[i].w, .0002, label + " alpha touching branch");
                    if (patch == 3)
                    {
                        double alphaError = 0;
                        for (int i = 0; i < 9; i++) alphaError = Math.Max(alphaError,
                            Math.Abs(rgba[i].w + Math.Abs((threshold % 3 == 0 ? i % 3 : i / 3) - 1)));
                        t.Near(0, alphaError, .0002, label + " alpha retains the entire zero-area line");
                    }
                }
            }
        }
    }

    static void CheckNearbyTinyContour(TestContext t, OwnedApi api)
    {
        const int width = 48, height = 48, cx = 32, cy = 32;
        var rectangle = new[] { DistanceFieldAnalyticFixture.Rectangle(10.25f, 10.25f, 31.25f, 31.25f) };
        foreach (byte threshold in Thresholds)
        {
            var input = DistanceFieldAnalyticFixture.Raster(width, height,
                (x, y) => DistanceFieldAnalyticFixture.Distance(rectangle, x, y, 0), 128, 1);
            input[cy * width + cx] = new Color32(255, 255, 255, (byte)(threshold + 1));
            var directions = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1) };
            var island = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                var d = directions[k]; int neighbor = input[(cy + d.y) * width + cx + d.x].a;
                t.True(neighbor < threshold, "Near-corner weak island has strictly negative cardinal neighbors");
                double radius = 1.0 / (threshold + 1 - neighbor);
                island[k] = new Vector2(cx + d.x * (float)radius, cy + d.y * (float)radius);
            }
            // Independent asymptotic-decider check: the positive diagonal rectangle
            // sample must not connect to the one-byte-high island through a saddle.
            for (int dx = -1; dx <= 1; dx += 2) for (int dy = -1; dy <= 1; dy += 2)
            {
                int diagonal = input[(cy + dy) * width + cx + dx].a - threshold;
                int horizontal = input[cy * width + cx + dx].a - threshold;
                int vertical = input[(cy + dy) * width + cx].a - threshold;
                t.True(diagonal <= 0 || diagonal < horizontal * vertical, "Tiny contour is bilinearly disjoint from the reconstructed corner");
            }
            var topology = AnalyzeTopology(input, width, height, threshold);
            t.Equal(2, topology.objects, "Near-corner fixture has two components, including the subpixel island");
            var seeds = CrossingSeeds(input, width, height, threshold);
            foreach (var mode in Modes)
            {
                string label = $"Near-duplicate contour/{mode}/threshold={threshold}";
                var values = api.Scalar(input, width, height, threshold, mode, label);
                int norm = mode == DistanceMetric.Diamond ? 1 : mode == DistanceMetric.Square ? 2 : 0;
                double expected = mode == DistanceMetric.EuclideanAntialiased ? -SeedMagnitude(seeds, cx, cy)
                    : DistanceFieldAnalyticFixture.Distance(new[] { island }, cx, cy, norm);
                t.Near(expected, values[cy * width + cx], .0002, label + " tiny contour keeps its own negative distance");
                t.Near(-9.75 + (threshold - 128) / 127.0, values[20 * width + 20], .6, label + " large component keeps its own interior");
                int remote = 0, falseFields = 0;
                for (int y = 0; y < height; y += 2) for (int x = 0; x < width; x += 2)
                {
                    double distance = SeedMagnitude(seeds, x, y);
                    if (distance <= 10 || input[y * width + x].a >= threshold) continue;
                    remote++; if (values[y * width + x] < .2 * (distance - 1.5)) falseFields++;
                }
                t.True(remote > 80, label + " inspects a remote exterior");
                t.Equal(0, falseFields, label + " tiny contour/corner interaction creates no remote field");
                if (mode == DistanceMetric.EuclideanAntialiased) CheckSeedOracle(t, input, values, seeds, width, height, threshold, label);
                if (mode == DistanceMetric.MTSDF) api.Multi(input, width, height, threshold, values, label);
                if (mode != DistanceMetric.Sharp && mode != DistanceMetric.Bevel && mode != DistanceMetric.MTSDF) continue;
                var features = api.Geometry(input, width, height, threshold, mode, label);
                double crossingError = 0, cornerError = double.PositiveInfinity;
                foreach (var crossing in island)
                {
                    double nearest = double.PositiveInfinity;
                    foreach (var f in features) nearest = Math.Min(nearest, math.distance(f.a, new float2(crossing.x, crossing.y)));
                    crossingError = Math.Max(crossingError, nearest);
                }
                float corner = 31.25f + (128 - threshold) / 127f;
                foreach (var f in features)
                {
                    if (f.vertex != 0 || math.distance(f.a, f.b) < 4) continue;
                    float2 next = math.normalizesafe(f.b - f.a);
                    // Both incident flanks must be axis-aligned. A tiny diamond's
                    // nearby endpoint is not evidence that the large corner was fitted.
                    bool axes = Math.Abs(f.previous.x * next.x) < .05 && Math.Abs(f.previous.y * next.y) < .05
                        && Math.Abs(math.dot(f.previous, next)) < .1;
                    if (axes) cornerError = Math.Min(cornerError, math.distance(f.a, new float2(corner, corner)));
                }
                t.Near(0, crossingError, .0002, label + " preserves all four distinct tiny-contour crossings");
                t.Near(0, cornerError, .75, label + " exercises a reconstructed large corner with two long straight flanks");
            }
        }
    }

    static Vector2[] CrossingSeeds(Color32[] input, int width, int height, byte threshold)
    {
        var result = new List<Vector2>();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x, a = input[i].a;
            if (x + 1 < width && (a > threshold) != (input[i + 1].a > threshold))
                result.Add(new Vector2((float)(x + (threshold - a) / (double)(input[i + 1].a - a)), y));
            if (y + 1 < height && (a > threshold) != (input[i + width].a > threshold))
                result.Add(new Vector2(x, (float)(y + (threshold - a) / (double)(input[i + width].a - a))));
        }
        return result.ToArray();
    }

    static double SeedMagnitude(Vector2[] seeds, int x, int y)
    {
        double best = double.PositiveInfinity;
        foreach (var seed in seeds)
        { double dx = x - (double)seed.x, dy = y - (double)seed.y; best = Math.Min(best, dx * dx + dy * dy); }
        return Math.Sqrt(best);
    }

    static void CheckSeedOracle(TestContext t, Color32[] input, float[] values, Vector2[] seeds,
        int width, int height, byte threshold, string label)
    {
        t.True(seeds.Length > 0 && seeds.Length <= 2 * input.Length, label + " has bounded independent crossing seeds");
        double maximum = 0; int count = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            // Entire tiny patches, every third pixel, and every partially covered
            // sample. This includes remote probes without quadratic full-image scans.
            if (width > 3 && (x % 3 != 0 || y % 3 != 0) && (input[i].a == 0 || input[i].a == 255)) continue;
            double expected = SeedMagnitude(seeds, x, y) * (input[i].a > threshold ? -1 : 1);
            maximum = Math.Max(maximum, Math.Abs(values[i] - expected)); count++;
        }
        t.True(count >= (width == 3 ? 9 : 250), label + " covers edge and exterior seed probes");
        t.Near(0, maximum, .0002, label + " Round measures crossing seeds, not segment Euclidean distance");
    }

    static double SegmentMagnitude(float2 a, float2 b, double x, double y)
    {
        double dx = b.x - (double)a.x, dy = b.y - (double)a.y;
        double u = dx * dx + dy * dy > 0 ? Math.Max(0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy))) : 0;
        double ex = x - a.x - u * dx, ey = y - a.y - u * dy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    struct Topology
    {
        public int objects, holes;
        public bool Same(Topology other) => objects == other.objects && holes == other.holes;
    }

    static Topology AnalyzeTopology(Color32[] input, int width, int height, byte threshold)
    {
        // Connectivity comes from source samples, not reconstructed production features.
        // Resolve checkerboard cells by the bilinear determinant; count background
        // components that never reach the border as holes.
        var parent = new int[input.Length]; var border = new bool[input.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Root(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }
        void Link(int a, int b)
        {
            int ra = Root(a), rb = Root(b);
            if (ra != rb) parent[rb] = ra;
        }
        bool Inside(int i) => input[i].a > threshold;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            if (x + 1 < width && Inside(i) == Inside(i + 1)) Link(i, i + 1);
            if (y + 1 < height && Inside(i) == Inside(i + width)) Link(i, i + width);
            if (x + 1 >= width || y + 1 >= height) continue;
            int a = i, b = i + 1, c = i + width + 1, d = i + width;
            if (Inside(a) != Inside(c) || Inside(b) != Inside(d) || Inside(a) == Inside(b)) continue;
            int determinant = (input[a].a - threshold) * (input[c].a - threshold) - (input[b].a - threshold) * (input[d].a - threshold);
            if (determinant >= 0) Link(a, c); else Link(b, d);
        }
        for (int i = 0; i < input.Length; i++)
            if (i % width == 0 || i % width == width - 1 || i / width == 0 || i / width == height - 1) border[Root(i)] = true;
        var result = new Topology();
        for (int i = 0; i < parent.Length; i++) if (Root(i) == i)
        { if (Inside(i)) result.objects++; else if (!border[i]) result.holes++; }
        return result;
    }
}
