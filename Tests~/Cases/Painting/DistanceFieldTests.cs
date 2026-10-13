// Support: Tests~/Framework/TestApi.cs. CPU jobs only; the parent owns Unity execution.
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class DistanceFieldTests
{
    const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    static readonly Type Utility = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility", true);
    static readonly DistanceMetric[] Modes = {
        DistanceMetric.EuclideanAntialiased, DistanceMetric.Sharp, DistanceMetric.Bevel,
        DistanceMetric.Diamond, DistanceMetric.Square, DistanceMetric.MTSDF
    };

    public static string Run() => TestContext.Run("Analytic raster distance fields and six artistic modes", t =>
    {
        CheckContract(t);
        CheckContinuousEdges(t);
        CheckPolygonGeometry(t);
        CheckCornerJoins(t);
        CheckAcuteJoinFan(t);
        CheckTopology(t);
        CheckDisjointJoinIsolation(t);
        CheckThresholdPoint(t);
        CheckWeakClosedContour(t);
        CheckTouchingBranchPairing(t);
        CheckAsymmetricTouchingBranches(t);
        CheckCoincidentTouchingBranches(t);
        CheckChannels(t);
        CheckDegenerateSizes(t);
        CheckMtsdf(t);
        CheckMtsdfStarEndpointDomains(t);
        CheckFittedStarCorners(t);
        CheckCornerFitGuards(t);
        CheckContourNormalization(t);
    });

    static void CheckContract(TestContext t)
    {
        t.Equal(6, Enum.GetNames(typeof(DistanceMetric)).Length, "Exactly the six artistic metrics are exposed");
        for (int i = 0; i < Modes.Length; i++)
            t.Equal(i + 4, (int)Modes[i], "Current enum declaration: " + Modes[i]);
        var label = typeof(DistanceMetric).GetField(nameof(DistanceMetric.EuclideanAntialiased))
            .GetCustomAttribute<InspectorNameAttribute>();
        t.True(label != null && label.displayName == "Round AA", "Round AA is the displayed Euclidean label");
        t.True(SignedMethod() != null, "The original seven-argument scalar entry remains available");
        t.True(ArtisticMethod() != null, "Artistic scalar entry accepts a miter limit after the metric");
    }

    static MethodInfo SignedMethod() => Utility.GetMethod("ComputeSignedDistance", PublicStatic, null,
        new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int),
            typeof(byte), typeof(int), typeof(DistanceMetric) }, null);

    static MethodInfo ArtisticMethod() => Utility.GetMethod("ComputeArtisticDistance", PublicStatic, null,
        new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int),
            typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float) }, null);

    static float[] Compute(TestContext t, Color32[] input, int w, int h, DistanceMetric mode,
        byte threshold = 128, int channel = 0, float? miter = null)
    {
        using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
        using var distances = new NativeArray<float>(input.Length, Allocator.TempJob);
        var writable = distances;
        for (int i = 0; i < input.Length; i++) writable[i] = float.NaN;
        var method = miter.HasValue ? ArtisticMethod() : SignedMethod();
        t.True(method != null, "Required scalar method for " + mode);
        object[] args = miter.HasValue
            ? new object[] { pixels, distances, w, h, threshold, channel, mode, miter.Value }
            : new object[] { pixels, distances, w, h, threshold, channel, mode };
        method.Invoke(null, args);
        var result = distances.ToArray();
        int nonfinite = -1, changed = -1;
        for (int i = 0; i < input.Length; i++)
        {
            if (!Finite(result[i])) nonfinite = i;
            if (!input[i].Equals(pixels[i])) changed = i;
        }
        t.Equal(-1, nonfinite, mode + " writes finite distances throughout its output");
        t.Equal(-1, changed, mode + " preserves borrowed input");
        return result;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    static void CheckContinuousEdges(TestContext t)
    {
        const int w = 65, h = 53;
        // Independent byte samples of a known continuous half-plane. The line equation
        // and dual metric norm supply the oracle, including subpixel translations.
        foreach (double angle in new[] { 0.0, 17.0, 31.0, 53.0, 79.0, 90.0 })
        foreach (double shift in new[] { -.37, .23 })
        foreach (double spread in new[] { 1.0, 4.0, 12.0 })
        {
            double nx = Math.Cos(angle * Math.PI / 180), ny = Math.Sin(angle * Math.PI / 180);
            double D(int x, int y) => nx * (x - 32) + ny * (y - 26) - shift;
            var input = DistanceFieldAnalyticFixture.Raster(w, h, (x, y) => D(x, y), 128, spread);
            foreach (var mode in Modes)
            {
                var values = Compute(t, input, w, h, mode);
                double divisor = mode == DistanceMetric.Diamond ? Math.Max(Math.Abs(nx), Math.Abs(ny))
                    : mode == DistanceMetric.Square ? Math.Abs(nx) + Math.Abs(ny) : 1;
                int samples = 0;
                for (int y = 10; y < h - 10; y++) for (int x = 10; x < w - 10; x++)
                {
                    if (Math.Abs(D(x, y)) > 6) continue;
                    // Round retains the sampled AA contour: allow subpixel error near
                    // crossings. Segment modes must keep the continuous line within .3 px.
                    t.Near(D(x, y) / divisor, values[y * w + x], mode == DistanceMetric.EuclideanAntialiased ? .55 : .3,
                        $"Continuous {mode} edge angle={angle} shift={shift} spread={spread} at {x},{y}");
                    samples++;
                }
                t.True(samples > 100, "Continuous edge has a substantial analytic sample region");
            }
        }
    }

    static void CheckPolygonGeometry(TestContext t)
    {
        const int w = 65, h = 65;
        foreach (float angle in new[] { 17f, 31f, 53f })
        {
            var rings = new[] { DistanceFieldAnalyticFixture.Rotate(new[] {
                new Vector2(-13.2f, -7.8f), new Vector2(5.1f, -12.3f),
                new Vector2(14.4f, -1.7f), new Vector2(7.6f, 11.8f), new Vector2(-10.7f, 9.3f)
            }, angle, new Vector2(32.2f, 31.7f)) };
            var input = DistanceFieldAnalyticFixture.Raster(w, h,
                (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0));
            foreach (var mode in new[] { DistanceMetric.EuclideanAntialiased, DistanceMetric.Diamond, DistanceMetric.Square })
            {
                int norm = mode == DistanceMetric.Diamond ? 1 : mode == DistanceMetric.Square ? 2 : 0;
                var values = Compute(t, input, w, h, mode);
                for (int y = 2; y < h - 2; y++) for (int x = 2; x < w - 2; x++)
                    t.Near(DistanceFieldAnalyticFixture.Distance(rings, x, y, norm), values[y * w + x], .65,
                        $"{mode} rotated polygon {angle} at {x},{y}");
            }
        }
        var rect = new[] { DistanceFieldAnalyticFixture.Rectangle(20.25f, 18.25f, 40.25f, 38.25f) };
        var mask = DistanceFieldAnalyticFixture.Raster(w, h, (x, y) => DistanceFieldAnalyticFixture.Distance(rect, x, y, 0));
        t.Near(9.5, Compute(t, mask, w, h, DistanceMetric.Diamond)[43 * w + 45], .65, "Diamond L1 corner geometry");
        t.Near(4.75, Compute(t, mask, w, h, DistanceMetric.Square)[43 * w + 45], .65, "Square L-infinity corner geometry");
    }

    static void CheckCornerJoins(TestContext t)
    {
        const int w = 65, h = 65;
        foreach (float angle in new[] { 0f, 27f, 62f })
        {
            var center = new Vector2(32.25f, 31.75f);
            var rings = new[] { DistanceFieldAnalyticFixture.Rotate(
                DistanceFieldAnalyticFixture.Rectangle(-12.4f, -8.6f, 12.4f, 8.6f), angle, center) };
            var input = DistanceFieldAnalyticFixture.Raster(w, h, (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0));
            var point = DistanceFieldAnalyticFixture.Rotate(new[] { new Vector2(16.4f, 12.6f) }, angle, center)[0];
            int x = Mathf.RoundToInt(point.x), y = Mathf.RoundToInt(point.y), index = y * w + x;
            double a = angle * Math.PI / 180, px = x - center.x, py = y - center.y;
            double dx = px * Math.Cos(a) + py * Math.Sin(a) - 12.4;
            double dy = -px * Math.Sin(a) + py * Math.Cos(a) - 8.6;
            t.True(dx > 3 && dy > 3, "Corner probe lies beyond both incident edges");
            var round = Compute(t, input, w, h, DistanceMetric.EuclideanAntialiased, miter: 16);
            var sharp = Compute(t, input, w, h, DistanceMetric.Sharp, miter: 16);
            var bevel = Compute(t, input, w, h, DistanceMetric.Bevel, miter: 16);
            t.Near(Math.Sqrt(dx * dx + dy * dy), round[index], .65, "Round vertex radius " + angle);
            t.Near(Math.Max(dx, dy), sharp[index], .8, "Sharp rotated incident edge extensions " + angle);
            t.Near(dx + dy, bevel[index], 1, "Bevel cuts the rotated miter corner " + angle);
            t.True(sharp[index] + .4 < round[index], "Sharp corner differs from Round " + angle);
            t.True(round[index] + .4 < bevel[index], "Bevel corner differs from Round " + angle);
            var limited = Compute(t, input, w, h, DistanceMetric.Sharp, miter: 1);
            t.Near(bevel[index], limited[index], .8, "Miter limit below sqrt(2) falls back to Bevel " + angle);
            t.True(limited[index] > sharp[index] + .4, "Miter limit changes sharp join " + angle);
        }
    }

    static void CheckAcuteJoinFan(TestContext t)
    {
        const int w = 65, h = 65;
        var a = new Vector2(16.25f, 12.25f);
        var b = new Vector2(48.25f, 20.25f);
        var c = new Vector2(28.25f, 55.25f);
        var rings = new[] { new[] { a, b, c } };
        var input = DistanceFieldAnalyticFixture.Raster(w, h,
            (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0));
        var sharp = Compute(t, input, w, h, DistanceMetric.Sharp, miter: 16);
        var bevel = Compute(t, input, w, h, DistanceMetric.Bevel, miter: 16);
        var mtsdf = Compute(t, input, w, h, DistanceMetric.MTSDF);

        // CCW incident directions C->A and A->B define A's outward vertex fan.
        // Compute signed support planes and their bisector directly from the input triangle.
        double previousLength = Math.Sqrt((a.x - c.x) * (a.x - c.x) + (a.y - c.y) * (a.y - c.y));
        double nextLength = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
        double previousX = (a.x - c.x) / previousLength, previousY = (a.y - c.y) / previousLength;
        double nextX = (b.x - a.x) / nextLength, nextY = (b.y - a.y) / nextLength;
        double normalPreviousX = previousY, normalPreviousY = -previousX;
        double normalNextX = nextY, normalNextY = -nextX;
        double bisectorX = normalPreviousX + normalNextX, bisectorY = normalPreviousY + normalNextY;
        double bisectorLength = Math.Sqrt(bisectorX * bisectorX + bisectorY * bisectorY);
        bisectorX /= bisectorLength; bisectorY /= bisectorLength;
        double cosHalf = normalPreviousX * bisectorX + normalPreviousY * bisectorY;
        t.True(cosHalf > 0 && 1 / cosHalf < 16, "Acute triangle's sharp join stays below the supplied miter limit");

        int samples = 0, oneNegativePlaneSamples = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            double rx = x - (double)a.x, ry = y - (double)a.y;
            double lengthSquared = rx * rx + ry * ry;
            if (lengthSquared <= 4 || lengthSquared >= 64) continue;
            if (rx * previousX + ry * previousY < 0 || rx * nextX + ry * nextY > 0) continue;
            double s1 = rx * normalPreviousX + ry * normalPreviousY;
            double s2 = rx * normalNextX + ry * normalNextY;
            double expectedSharp = Math.Max(s1, s2);
            double expectedBevel = (rx * bisectorX + ry * bisectorY) / cosHalf;
            int index = y * w + x;
            t.True(input[index].a < 128 && expectedSharp > 0 && expectedBevel > 0,
                $"Acute vertex fan lies outside the triangle at {x},{y}");
            t.True(Finite(sharp[index]) && sharp[index] > 0 && Finite(bevel[index]) && bevel[index] > 0,
                $"Acute vertex fan has finite positive distances at {x},{y}");
            t.Near(expectedSharp, sharp[index], .65, $"Acute Sharp fan uses signed incident planes at {x},{y}");
            t.Near(expectedBevel, bevel[index], .65, $"Acute Bevel fan uses its signed bisector at {x},{y}");
            t.Near(expectedSharp, mtsdf[index], .65, $"Acute MTSDF fan retains its sharp signed support planes at {x},{y}");
            if ((s1 < 0) != (s2 < 0)) oneNegativePlaneSamples++;
            samples++;
        }
        t.True(samples > 20, "Acute join fan has a bounded analytic sample region");
        t.True(oneNegativePlaneSamples > 20, "Acute join fan includes more than twenty points behind exactly one support plane");
    }

    static void CheckTopology(TestContext t)
    {
        const int w = 55, h = 49;
        var cases = new[] {
            new[] { new[] { new Vector2(8.2f, 7.3f), new Vector2(39.4f, 7.3f), new Vector2(39.4f, 16.6f),
                new Vector2(22.7f, 16.6f), new Vector2(22.7f, 38.8f), new Vector2(8.2f, 38.8f) } },
            new[] { DistanceFieldAnalyticFixture.Rectangle(6.25f, 5.25f, 46.25f, 42.25f),
                DistanceFieldAnalyticFixture.Rectangle(18.25f, 15.25f, 33.25f, 31.25f) },
            new[] { DistanceFieldAnalyticFixture.Rectangle(6.25f, 8.25f, 19.25f, 33.25f),
                DistanceFieldAnalyticFixture.Rectangle(31.25f, 12.25f, 46.25f, 39.25f) }
        };
        for (int c = 0; c < cases.Length; c++)
        {
            var rings = cases[c];
            var input = DistanceFieldAnalyticFixture.Raster(w, h, (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0));
            var roundSeeds = RoundCrossingSeeds(input, w, h, 128);
            t.True(roundSeeds.Length > 0, "Round topology fixture has threshold-crossing seeds");
            foreach (var mode in Modes)
            {
                var values = Compute(t, input, w, h, mode);
                bool hasInside = false, hasOutside = false;
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                {
                    double expected = DistanceFieldAnalyticFixture.Distance(rings, x, y, 0);
                    if (Math.Abs(expected) > .8)
                    {
                        t.True((values[y * w + x] < 0) == (expected < 0), $"Topology {c}/{mode} sign at {x},{y}");
                        hasInside |= expected < 0; hasOutside |= expected > 0;
                    }
                    if (mode == DistanceMetric.EuclideanAntialiased)
                    {
                        // The preserved Round AA contract measures nearest horizontal/vertical
                        // threshold-crossing points, including near concave and convex corners.
                        // It does not measure distance to connected contour segments.
                        double seeded = RoundSeedDistance(roundSeeds, x, y, input[y * w + x].a > 128);
                        t.Near(seeded, values[y * w + x], .0001, $"Topology {c}/{mode} crossing-seed distance at {x},{y}");
                    }
                }
                t.True(hasInside && hasOutside, "Topology exercises both sides of its contours");
                int probeX = c == 0 ? 30 : 25, probeY = c == 0 ? 28 : c == 1 ? 22 : 24;
                t.True(values[probeY * w + probeX] > 2, "Hole, notch or inter-component gap remains empty: " + c + "/" + mode);
            }
        }
    }

    static Vector2[] RoundCrossingSeeds(Color32[] input, int w, int h, byte threshold)
    {
        // Independent oracle: enumerate each adjacent sample pair once, linearly interpolate
        // its threshold crossing, then retain points. No production jobs or transforms run here.
        var points = new List<Vector2>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int index = y * w + x, a = input[index].a;
            if (x + 1 < w)
            {
                int b = input[index + 1].a;
                if ((a > threshold) != (b > threshold))
                    points.Add(new Vector2((float)(x + (threshold - a) / (double)(b - a)), y));
            }
            if (y + 1 < h)
            {
                int b = input[index + w].a;
                if ((a > threshold) != (b > threshold))
                    points.Add(new Vector2(x, (float)(y + (threshold - a) / (double)(b - a))));
            }
        }
        return points.ToArray();
    }

    static double RoundSeedDistance(Vector2[] seeds, int x, int y, bool inside)
    {
        double bestSquared = double.PositiveInfinity;
        foreach (var point in seeds)
        {
            double dx = x - (double)point.x, dy = y - (double)point.y;
            bestSquared = Math.Min(bestSquared, dx * dx + dy * dy);
        }
        double distance = Math.Sqrt(bestSquared);
        return inside ? -distance : distance;
    }

    static void CheckDisjointJoinIsolation(TestContext t)
    {
        const int w = 53, h = 53;
        var rings = new[] {
            DistanceFieldAnalyticFixture.Rectangle(2, 2, 20, 20),
            new[] { new Vector2(13, 30), new Vector2(30, 13), new Vector2(47, 30), new Vector2(30, 47) }
        };
        var input = DistanceFieldAnalyticFixture.Raster(w, h,
            (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0));
        // Diamond B's sides are |x-30|+|y-30|=17. Its center is 17/sqrt(2)
        // from each side; square A's outward miter is not an inside candidate for B.
        foreach (var mode in new[] { DistanceMetric.Sharp, DistanceMetric.Bevel })
        {
            var values = Compute(t, input, w, h, mode);
            t.Near(-17 / Math.Sqrt(2), values[30 * w + 30], .05,
                "Disjoint " + mode + " keeps diamond B's own inside distance instead of square A's -10 miter");
            t.Near(-9, values[11 * w + 11], .05, "Disjoint " + mode + " keeps square A's own inside distance");
            t.Near(1 / Math.Sqrt(2), values[21 * w + 21], .05, "Disjoint " + mode + " keeps the separating gap outside");
        }
    }

    static void CheckThresholdPoint(TestContext t)
    {
        var input = AlphaMask(255, 255, 255, 255, 128, 255, 255, 255, 255);
        // The threshold contour is exactly the isolated point (1,1), not an empty mask.
        foreach (var mode in Modes)
        {
            var values = Compute(t, input, 3, 3, mode);
            t.Near(0, values[4], .0001, "Exact threshold point stays on the boundary: " + mode);
            for (int i = 0; i < input.Length; i++) if (i != 4)
            {
                int dx = Math.Abs(i % 3 - 1), dy = Math.Abs(i / 3 - 1);
                double expected = mode == DistanceMetric.Diamond ? dx + dy
                    : mode == DistanceMetric.Square ? Math.Max(dx, dy) : Math.Sqrt(dx * dx + dy * dy);
                t.True(values[i] < 0, "Threshold-point neighbors remain strictly inside: " + mode + "/" + i);
                t.Near(-expected, values[i], .0001, "Finite point-contour distance: " + mode + "/" + i);
            }
        }
        var multi = ComputeMulti(t, input, 3, 3);
        for (int i = 0; i < input.Length; i++)
        {
            int dx = i % 3 - 1, dy = i / 3 - 1;
            t.Near(-Math.Sqrt(dx * dx + dy * dy), multi[i].w, .0001,
                "MTSDF alpha keeps the threshold point and finite negative neighboring distances at " + i);
        }
    }

    static void CheckWeakClosedContour(TestContext t)
    {
        var input = AlphaMask(0, 0, 0, 0, 160, 0, 0, 0, 0);
        // Linear interpolation puts each axial crossing 32/160=.2 pixels from (1,1).
        // The four crossings form a nonzero-area diamond, even though it is subpixel sized.
        const double radius = 32.0 / 160;
        foreach (var mode in Modes)
        {
            var values = Compute(t, input, 3, 3, mode);
            double expected = mode == DistanceMetric.EuclideanAntialiased || mode == DistanceMetric.Diamond ? -radius
                : mode == DistanceMetric.Square ? -radius / 2 : -radius / Math.Sqrt(2);
            t.True(values[4] < 0, "Weak closed contour is not collapsed to zero: " + mode);
            t.Near(expected, values[4], .0001, "Weak contour retains its analytic inside distance: " + mode);
            for (int i = 0; i < input.Length; i++) if (i != 4)
                t.True(values[i] > 0, "Weak-contour neighboring samples stay outside: " + mode + "/" + i);
        }
        var multi = ComputeMulti(t, input, 3, 3);
        t.True(multi[4].w < 0, "MTSDF alpha preserves the weak contour's negative interior");
        t.Near(-radius / Math.Sqrt(2), multi[4].w, .0001, "MTSDF alpha measures the tiny diamond's true inside distance");
        var rings = new[] { new[] {
            new Vector2(.8f, 1), new Vector2(1, .8f), new Vector2(1.2f, 1), new Vector2(1, 1.2f)
        } };
        for (int i = 0; i < input.Length; i++)
            t.Near(DistanceFieldAnalyticFixture.Distance(rings, i % 3, i / 3, 0), multi[i].w, .0001,
                "MTSDF alpha retains finite tiny-contour geometry at " + i);
    }

    static void CheckTouchingBranchPairing(TestContext t)
    {
        var input = AlphaMask(128, 96, 128, 160, 128, 160, 128, 96, 128);
        foreach (var mode in Modes)
        {
            var values = Compute(t, input, 3, 3, mode);
            t.Near(0, values[4], .0001, "Degree-four touching contour retains its center: " + mode);
            double magnitude = mode == DistanceMetric.EuclideanAntialiased || mode == DistanceMetric.Diamond ? 1
                : mode == DistanceMetric.Square ? .5 : 1 / Math.Sqrt(2);
            foreach (int i in new[] { 1, 3, 5, 7 })
                t.Near(input[i].a > 128 ? -magnitude : magnitude, values[i], .0001,
                    "Degree-four branch has finite signed neighboring geometry: " + mode + "/" + i);
        }

        // Inspect package-owned geometry because both pairings have the same union of finite
        // segments. Numeric join directions distinguish the positive left/right wedges.
        var artistic = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var build = artistic.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(NativeArray<Color32>), typeof(int), typeof(int), typeof(byte), typeof(int), typeof(DistanceMetric) }, null);
        t.True(build != null, "Package contour builder is available for explicit branch inspection");
        using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
        foreach (var mode in new[] { DistanceMetric.Sharp, DistanceMetric.Bevel })
        {
            using var geometry = (IDisposable)build.Invoke(null, new object[] { pixels, 3, 3, (byte)128, 0, mode });
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
            object features = geometry.GetType().GetField("features", fields).GetValue(geometry);
            Type arrayType = features.GetType(), featureType = arrayType.GetGenericArguments()[0];
            var item = arrayType.GetProperty("Item", fields);
            int count = (int)arrayType.GetProperty("Length", fields).GetValue(features);
            int leftPairs = 0, rightPairs = 0;
            for (int i = 0; i < count; i++)
            {
                object feature = item.GetValue(features, new object[] { i });
                if ((int)featureType.GetField("vertex", fields).GetValue(feature) != 1) continue;
                var point = (float2)featureType.GetField("a", fields).GetValue(feature);
                if (math.lengthsq(point - new float2(1, 1)) > 1e-10f) continue;
                var previous = (float2)featureType.GetField("previousDirection", fields).GetValue(feature);
                var next = (float2)featureType.GetField("nextDirection", fields).GetValue(feature);
                float2 incomingRay = -previous;
                t.True(incomingRay.x * next.x > 0, "Touching join pairs SW with NW or SE with NE: " + mode);
                t.True(incomingRay.y * next.y < 0, "Touching join connects lower and upper rays on the same side: " + mode);
                t.Near(1 / Math.Sqrt(2), Math.Abs(incomingRay.x), .0001, "Incoming branch diagonal X: " + mode);
                t.Near(1 / Math.Sqrt(2), Math.Abs(incomingRay.y), .0001, "Incoming branch diagonal Y: " + mode);
                t.Near(1 / Math.Sqrt(2), Math.Abs(next.x), .0001, "Outgoing branch diagonal X: " + mode);
                t.Near(1 / Math.Sqrt(2), Math.Abs(next.y), .0001, "Outgoing branch diagonal Y: " + mode);
                if (incomingRay.x < 0) leftPairs++; else rightPairs++;
            }
            t.Equal(1, leftPairs, "Exactly one SW-to-NW center join: " + mode);
            t.Equal(1, rightPairs, "Exactly one SE-to-NE center join: " + mode);
        }
        var multi = ComputeMulti(t, input, 3, 3);
        t.Near(0, multi[4].w, .0001, "MTSDF alpha retains the degree-four center");
        foreach (int i in new[] { 1, 3, 5, 7 })
            t.Near(input[i].a > 128 ? -1 / Math.Sqrt(2) : 1 / Math.Sqrt(2), multi[i].w, .0001,
                "MTSDF alpha retains finite touching-branch distances at " + i);
    }

    static void CheckAsymmetricTouchingBranches(TestContext t)
    {
        var source = AlphaMask(255, 1, 255, 129, 128, 129, 0, 1, 0);
        // Threshold interpolation gives the lower rays (+/- .5,-1) and upper rays
        // (+/-1,1/129) from (1,1). The positive incident grid samples are east/west
        // even though sampling .25 pixels along the wedge bisector is below threshold.
        foreach (bool transpose in new[] { false, true })
        foreach (bool reflectX in new[] { false, true })
        foreach (bool reflectY in new[] { false, true })
        {
            var input = TransformTouchingPatch(source, transpose, reflectX, reflectY);
            float2 axis = TransformTouchingDirection(new float2(1, 0), transpose, reflectX, reflectY);
            float2 eastLower = TransformTouchingDirection(new float2(.5f, -1), transpose, reflectX, reflectY);
            float2 eastUpper = TransformTouchingDirection(new float2(1, 1f / 129), transpose, reflectX, reflectY);
            float2 westLower = TransformTouchingDirection(new float2(-.5f, -1), transpose, reflectX, reflectY);
            float2 westUpper = TransformTouchingDirection(new float2(-1, 1f / 129), transpose, reflectX, reflectY);
            string variant = $"transpose={transpose}, reflectX={reflectX}, reflectY={reflectY}";
            foreach (var mode in new[] { DistanceMetric.Sharp, DistanceMetric.Bevel })
            {
                var features = ReadTouchingFeatures(t, input, mode);
                int openEnds = 0, joins = 0, eastPairs = 0, westPairs = 0;
                foreach (var feature in features)
                {
                    if (!AtTouchingCenter(feature.a)) continue;
                    if (feature.vertex == 2 || feature.vertex == 3) { openEnds++; continue; }
                    if (feature.vertex != 1) continue;
                    float2 incoming = -feature.previous, outgoing = feature.next;
                    t.True(math.dot(incoming, axis) * math.dot(outgoing, axis) > 0,
                        "Asymmetric touching rays stay around the same positive cardinal sample: " + mode + "/" + variant);
                    if (math.dot(incoming, axis) > 0)
                    {
                        t.Near(0, TouchingPairError(incoming, outgoing, eastLower, eastUpper), 1e-8,
                            "Asymmetric east positive wedge pairs its two interpolated rays: " + mode + "/" + variant);
                        eastPairs++;
                    }
                    else
                    {
                        t.Near(0, TouchingPairError(incoming, outgoing, westLower, westUpper), 1e-8,
                            "Asymmetric west positive wedge pairs its two interpolated rays: " + mode + "/" + variant);
                        westPairs++;
                    }
                    joins++;
                }
                t.Equal(0, openEnds, "Asymmetric center has no open-end feature 2/3: " + mode + "/" + variant);
                t.Equal(2, joins, "Asymmetric center has exactly two paired joins: " + mode + "/" + variant);
                t.Equal(1, eastPairs, "Asymmetric positive east sector retains its pair: " + mode + "/" + variant);
                t.Equal(1, westPairs, "Asymmetric positive west sector retains its pair: " + mode + "/" + variant);
            }
        }
    }

    static void CheckCoincidentTouchingBranches(TestContext t)
    {
        var source = AlphaMask(160, 160, 160, 128, 128, 128, 160, 160, 160);
        // The two contours coincide along the middle row. North and south are positive,
        // so both sectors pair east with west; an E->E or W->W zero-span pairing is invalid.
        foreach (bool transpose in new[] { false, true })
        foreach (bool reflectX in new[] { false, true })
        foreach (bool reflectY in new[] { false, true })
        {
            var input = TransformTouchingPatch(source, transpose, reflectX, reflectY);
            float2 east = TransformTouchingDirection(new float2(1, 0), transpose, reflectX, reflectY);
            float2 west = -east;
            int positiveSectors = 0;
            foreach (int i in new[] { 1, 3, 5, 7 }) if (input[i].a > 128) positiveSectors++;
            t.Equal(2, positiveSectors, "Coincident fixture has exactly two positive incident cardinal sectors");
            string variant = $"transpose={transpose}, reflectX={reflectX}, reflectY={reflectY}";
            foreach (var mode in new[] { DistanceMetric.Sharp, DistanceMetric.Bevel })
            {
                var features = ReadTouchingFeatures(t, input, mode);
                int openEnds = 0, joins = 0;
                foreach (var feature in features)
                {
                    if (feature.vertex == 0)
                        t.True(math.lengthsq(feature.b - feature.a) > 1e-10f,
                            "Coincident fixture has no zero-length contour segment: " + mode + "/" + variant);
                    if (!AtTouchingCenter(feature.a)) continue;
                    if (feature.vertex == 2 || feature.vertex == 3) { openEnds++; continue; }
                    if (feature.vertex != 1) continue;
                    float2 incoming = -feature.previous, outgoing = feature.next;
                    t.Near(0, TouchingPairError(incoming, outgoing, east, west), 1e-8,
                        "Coincident center pairs E with W rather than E with E or W with W: " + mode + "/" + variant);
                    t.Near(-1, math.dot(incoming, outgoing), .0001,
                        "Coincident center rays are opposite, not a degenerate closed join: " + mode + "/" + variant);
                    joins++;
                }
                t.Equal(0, openEnds, "Coincident center has no open-end feature 2/3: " + mode + "/" + variant);
                t.Equal(positiveSectors, joins,
                    "Two coincident E-W joins retain the positive north/south sectors: " + mode + "/" + variant);
            }
        }
    }

    struct TouchingFeature
    {
        public float2 a, b, previous, next;
        public int vertex;
    }

    static TouchingFeature[] ReadTouchingFeatures(TestContext t, Color32[] input, DistanceMetric mode)
    {
        // Only the package-owned builder/features are nonpublic. NativeArray access uses
        // its public Length/Item properties, and geometry is disposed after the copied snapshot.
        var artistic = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var build = artistic.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(NativeArray<Color32>), typeof(int), typeof(int), typeof(byte), typeof(int), typeof(DistanceMetric) }, null);
        t.True(build != null, "Package builder is available for touching-branch regression inspection");
        using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
        using var geometry = (IDisposable)build.Invoke(null, new object[] { pixels, 3, 3, (byte)128, 0, mode });
        const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
        object features = geometry.GetType().GetField("features", fields).GetValue(geometry);
        Type arrayType = features.GetType(), featureType = arrayType.GetGenericArguments()[0];
        int count = (int)arrayType.GetProperty("Length", fields).GetValue(features);
        var item = arrayType.GetProperty("Item", fields);
        var result = new TouchingFeature[count];
        for (int i = 0; i < count; i++)
        {
            object feature = item.GetValue(features, new object[] { i });
            result[i] = new TouchingFeature {
                a = (float2)featureType.GetField("a", fields).GetValue(feature),
                b = (float2)featureType.GetField("b", fields).GetValue(feature),
                previous = (float2)featureType.GetField("previousDirection", fields).GetValue(feature),
                next = (float2)featureType.GetField("nextDirection", fields).GetValue(feature),
                vertex = (int)featureType.GetField("vertex", fields).GetValue(feature)
            };
            t.True(float.IsFinite(result[i].a.x) && float.IsFinite(result[i].a.y)
                && float.IsFinite(result[i].b.x) && float.IsFinite(result[i].b.y), "Touching contour has finite feature endpoints");
        }
        for (int i = 0; i < input.Length; i++) t.Equal(input[i], pixels[i], "Touching geometry inspection preserves input");
        return result;
    }

    static Color32[] TransformTouchingPatch(Color32[] source, bool transpose, bool reflectX, bool reflectY)
    {
        var result = new Color32[9];
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
        {
            int tx = transpose ? y : x, ty = transpose ? x : y;
            if (reflectX) tx = 2 - tx;
            if (reflectY) ty = 2 - ty;
            result[ty * 3 + tx] = source[y * 3 + x];
        }
        return result;
    }

    static float2 TransformTouchingDirection(float2 direction, bool transpose, bool reflectX, bool reflectY)
    {
        double x = transpose ? direction.y : direction.x, y = transpose ? direction.x : direction.y;
        if (reflectX) x = -x;
        if (reflectY) y = -y;
        double length = Math.Sqrt(x * x + y * y);
        return new float2((float)(x / length), (float)(y / length));
    }

    static bool AtTouchingCenter(float2 point) => math.lengthsq(point - new float2(1, 1)) <= 1e-10f;

    static double TouchingPairError(float2 incoming, float2 outgoing, float2 first, float2 second)
        => Math.Min(math.lengthsq(incoming - first) + math.lengthsq(outgoing - second),
            math.lengthsq(incoming - second) + math.lengthsq(outgoing - first));

    static Color32[] AlphaMask(params byte[] alpha)
    {
        var input = new Color32[alpha.Length];
        for (int i = 0; i < input.Length; i++) input[i] = new Color32(0, 0, 0, alpha[i]);
        return input;
    }

    static float4[] ComputeMulti(TestContext t, Color32[] input, int w, int h)
    {
        var method = Utility.GetMethod("ComputeMtsdfDistance", PublicStatic, null, new[] {
            typeof(NativeArray<Color32>), typeof(NativeArray<float4>), typeof(int), typeof(int), typeof(byte), typeof(int)
        }, null);
        t.True(method != null, "MTSDF API is available for contour regressions");
        using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
        using var output = new NativeArray<float4>(input.Length, Allocator.TempJob);
        var writable = output;
        for (int i = 0; i < output.Length; i++) writable[i] = new float4(float.NaN);
        method.Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0 });
        var result = output.ToArray();
        for (int i = 0; i < result.Length; i++)
        {
            for (int c = 0; c < 4; c++) t.True(Finite(result[i][c]), "MTSDF contour regression writes finite channel " + c + " at " + i);
            t.Equal(input[i], pixels[i], "MTSDF contour regression preserves input at " + i);
        }
        return result;
    }

    static void CheckChannels(TestContext t)
    {
        const int w = 19, h = 7;
        foreach (var mode in Modes) for (int channel = 0; channel < 5; channel++)
        {
            var input = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                byte v = (byte)Math.Max(0, Math.Min(255, 128 + (9 - x) * 24));
                // Other channels deliberately describe a different uniform silhouette.
                input[y * w + x] = channel == 0 ? new Color32(255, 255, 255, v)
                    : channel == 1 ? new Color32(v, 255, 255, 255)
                    : channel == 2 ? new Color32(255, v, 255, 255)
                    : channel == 3 ? new Color32(255, 255, v, 255) : new Color32(v, v, v, 255);
            }
            var values = Compute(t, input, w, h, mode, channel: channel);
            for (int x = 2; x < w - 2; x++)
                t.Near(x - 9, values[3 * w + x], .08, "Selected channel: " + mode + "/" + channel + "/" + x);
        }
    }

    static void CheckDegenerateSizes(TestContext t)
    {
        foreach (var size in new[] { new Vector2Int(1, 1), new Vector2Int(1, 13), new Vector2Int(13, 1), new Vector2Int(2, 2) })
        foreach (var mode in Modes) foreach (byte alpha in new byte[] { 0, 32, 224, 255 })
        {
            var input = new Color32[size.x * size.y];
            for (int i = 0; i < input.Length; i++) input[i] = new Color32(0, 0, 0, alpha);
            foreach (float value in Compute(t, input, size.x, size.y, mode))
                t.True(alpha < 128 ? value > 0 : value < 0, $"No-contour {size}/{mode}/{alpha} finite nonzero signed sentinel");
        }
        // The agreed 1D fallback is Round, so every artistic selection has this same line oracle.
        foreach (bool vertical in new[] { false, true }) foreach (byte threshold in new byte[] { 64, 128, 192 })
        foreach (var mode in Modes)
        {
            var input = new Color32[13];
            for (int i = 0; i < input.Length; i++)
                input[i] = new Color32(0, 0, 0, (byte)Math.Max(0, Math.Min(255, threshold + (6 - i) * 16)));
            var values = Compute(t, input, vertical ? 1 : 13, vertical ? 13 : 1, mode, threshold);
            for (int i = 0; i < input.Length; i++)
                t.Near(i - 6, values[i], .01, "1D subpixel boundary " + mode + "/" + threshold + "/" + vertical);
        }
        foreach (byte threshold in new byte[] { 0, 255 }) foreach (var mode in Modes)
            Compute(t, new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 128), new Color32(0, 0, 0, 255) }, 3, 1, mode, threshold);
    }

    static void CheckMtsdfStarEndpointDomains(TestContext t)
    {
        const int w = 512, h = 512;
        foreach (float rotation in new[] { 0f, 23f })
        {
            var star = new Vector2[10];
            for (int i = 0; i < star.Length; i++)
            {
                double angle = Math.PI * (.5 + i * .2);
                double radius = i % 2 == 0 ? 95 : 47.5;
                star[i] = new Vector2((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
            }
            var rings = new[] { DistanceFieldAnalyticFixture.Rotate(star, rotation, new Vector2(150.2f, 296.7f)) };
            // Keep the same coverage while varying the contour threshold, as in live editing.
            var input = DistanceFieldAnalyticFixture.Raster(w, h,
                (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0), 128, 1);
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var output = new NativeArray<float4>(input.Length, Allocator.TempJob);
            var method = Utility.GetMethod("ComputeMtsdfDistance", PublicStatic);
            foreach (byte threshold in new byte[] { 64, 127, 128, 129, 143, 192, 224 })
            {
                method.Invoke(null, new object[] { pixels, output, w, h, threshold, 0 });
                int farCount = 0, falseFields = 0, badSigns = 0, nonFinite = 0, firstBad = -1;
                for (int i = 0; i < output.Length; i++)
                {
                    float4 field = output[i];
                    float median = math.max(math.min(field.x, field.y), math.min(math.max(field.x, field.y), field.z));
                    for (int c = 0; c < 4; c++) if (!Finite(field[c])) nonFinite++;
                    if (Math.Abs(field.w) > 1 && (median < 0) != (input[i].a > threshold)) badSigns++;
                    if (field.w <= 100) continue;
                    farCount++;
                    // This five-point star has bounded, nonzero corner angles. Its valid
                    // miter fields cannot approach zero along a remote supporting line.
                    if (median < field.w * .25f)
                    { falseFields++; if (firstBad < 0) firstBad = i; }
                }
                string label = $"Star rotation={rotation}, threshold={threshold}";
                t.True(farCount > 100000, label + " exercises a large exterior, not only the edge band");
                t.Equal(0, nonFinite, label + " has finite RGBA distances throughout");
                t.Equal(0, badSigns, label + " preserves the selected silhouette");
                t.Equal(0, falseFields, label + " has no remote false field; first pixel=" + firstBad);
            }
        }
    }

    static void CheckMtsdf(TestContext t)
    {
        var method = Utility.GetMethod("ComputeMtsdfDistance", PublicStatic, null, new[] {
            typeof(NativeArray<Color32>), typeof(NativeArray<float4>), typeof(int), typeof(int), typeof(byte), typeof(int)
        }, null);
        t.True(method != null, "MTSDF exposes float4 data through the confirmed package API");
        const int w = 49, h = 49;
        var rings = new[] { DistanceFieldAnalyticFixture.Rotate(new[] {
            new Vector2(-13, -10), new Vector2(14, -6), new Vector2(3, 15)
        }, 23, new Vector2(24.2f, 23.7f)) };
        var input = DistanceFieldAnalyticFixture.Raster(w, h, (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0));
        using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
        using var output = new NativeArray<float4>(w * h, Allocator.TempJob);
        var writable = output;
        for (int i = 0; i < output.Length; i++) writable[i] = new float4(float.NaN);
        method.Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0 });
        var scalar = Compute(t, input, w, h, DistanceMetric.MTSDF);
        int coloredSamples = 0, cornerSamples = 0;
        for (int y = 2; y < h - 2; y++) for (int x = 2; x < w - 2; x++)
        {
            int index = y * w + x; var value = output[index];
            for (int c = 0; c < 4; c++) t.True(Finite(value[c]), "MTSDF finite channel " + c + " at " + index);
            double expected = DistanceFieldAnalyticFixture.Distance(rings, x, y, 0);
            t.Near(expected, value.w, .65, "MTSDF alpha true Euclidean contour distance at " + index);
            float median = Math.Max(Math.Min(value.x, value.y), Math.Min(Math.Max(value.x, value.y), value.z));
            t.Near(median, scalar[index], .0001, "Scalar MTSDF consumes the stored RGB median at " + index);
            if (Math.Abs(expected) > .8) t.True((median < 0) == (expected < 0), "MTSDF RGB median silhouette at " + index);
            if (Math.Abs(expected) < 5 && Math.Max(value.x, Math.Max(value.y, value.z)) - Math.Min(value.x, Math.Min(value.y, value.z)) > .2f)
                coloredSamples++;
            if (expected > 1 && expected < 8 && value.w - Math.Abs(median) > .4f) cornerSamples++;
            t.Equal(input[index], pixels[index], "MTSDF preserves source coverage");
        }
        t.True(coloredSamples > 10, "MTSDF RGB contains edge-colored distances rather than scalar copies");
        t.True(cornerSamples > 2, "MTSDF stores a sharp RGB median separately from alpha true distance");
        foreach (byte alpha in new byte[] { 0, 255 })
        {
            for (int i = 0; i < pixels.Length; i++) { var p = pixels; p[i] = new Color32(0, 0, 0, alpha); }
            method.Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0 });
            for (int i = 0; i < output.Length; i++) for (int c = 0; c < 4; c++)
                t.True(Finite(output[i][c]) && (alpha == 0 ? output[i][c] > 0 : output[i][c] < 0), "MTSDF no-contour signed channels");
        }
    }

    static void CheckFittedStarCorners(TestContext t)
    {
        const int size = 128;
        int isolatedTips = 0;
        var artistic = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var build = artistic.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (float angle in new[] { 0f, 17f, 37f, 90f })
        foreach (float shift in new[] { .13f, .61f })
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            { double a = Math.PI * (.5 + i * .2); double r = i % 2 == 0 ? 40 : 20; points[i] = new Vector2((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a))); }
            points = DistanceFieldAnalyticFixture.Rotate(points, angle, new Vector2(64 + shift, 64 - shift));
            var input = DistanceFieldAnalyticFixture.Raster(size, size,
                (x, y) => DistanceFieldAnalyticFixture.Distance(new[] { points }, x, y, 0), 128, 1);
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            foreach (byte threshold in new byte[] { 128, 143, 192, 224, 225, 226, 227, 228, 229, 240 })
            {
                using var geometry = (IDisposable)build.Invoke(null, new object[] { pixels, size, size, threshold, 0, DistanceMetric.MTSDF });
                object array = geometry.GetType().GetField("features").GetValue(geometry);
                int count = (int)array.GetType().GetProperty("Length").GetValue(array);
                var item = array.GetType().GetProperty("Item");
                for (int tip = 0; tip < 10; tip += 2)
                {
                    Vector2 incoming = (points[tip] - points[(tip + 9) % 10]).normalized;
                    Vector2 outgoing = (points[(tip + 1) % 10] - points[tip]).normalized;
                    Vector2 n1 = new Vector2(incoming.y, -incoming.x), n2 = new Vector2(outgoing.y, -outgoing.x);
                    Vector2 bisector = (n1 + n2).normalized;
                    // The raster's linear coverage has a known threshold displacement.
                    // Intersect the analytically shifted supporting lines, not the output.
                    Vector2 expected = points[tip] + ((128 - threshold) / 127f / Vector2.Dot(n1, bisector)) * bisector;
                    float best = float.MaxValue; float2 actual = default, previous = default, next = default;
                    for (int i = 0; i < count; i++)
                    {
                        object f = item.GetValue(array, new object[] { i });
                        var p = (float2)f.GetType().GetField("a").GetValue(f);
                        float d = math.distance(p, new float2(expected.x, expected.y));
                        if (d >= best) continue;
                        best = d; actual = p;
                        previous = (float2)f.GetType().GetField("previousDirection").GetValue(f);
                        next = math.normalizesafe((float2)f.GetType().GetField("b").GetValue(f) - p);
                    }
                    string label = $"Fitted star tip {tip}; angle={angle}, shift={shift}, threshold={threshold}";
                    // Error is bounded normal to each support. An acute tip amplifies
                    // that uncertainty along its bisector; a fixed Euclidean vertex
                    // tolerance would incorrectly reject an otherwise reliable fit.
                    Vector2 displacement = new Vector2(actual.x, actual.y) - expected;
                    t.True(Math.Abs(Vector2.Dot(displacement, n1)) <= .6 && Math.Abs(Vector2.Dot(displacement, n2)) <= .6,
                        label + " respects both supporting lines' raster uncertainty; got " + actual + ", expected " + expected);
                    t.True(best <= 2, label + " has bounded tip displacement");
                    if (IsIsolatedThresholdPixel(input, size, expected, threshold, out int2 island))
                    {
                        // Independent bilinear topology: one positive sample, negative
                        // cardinal neighbors and disconnected diagonal cells form a tiny
                        // component. Its four crossings must remain represented unless
                        // reconstruction of the neighboring long flanks covers them.
                        foreach (int2 direction in new[] { new int2(1, 0), new int2(0, 1), new int2(-1, 0), new int2(0, -1) })
                        {
                            double center = input[island.y * size + island.x].a;
                            double neighbor = input[(island.y + direction.y) * size + island.x + direction.x].a;
                            float2 crossing = new float2(island.x, island.y) + new float2(direction.x, direction.y) * (float)((center - threshold) / (center - neighbor));
                            double nearest = double.MaxValue, boundary = double.MaxValue;
                            int winding = 0;
                            for (int i = 0; i < count; i++)
                            {
                                object f = item.GetValue(array, new object[] { i });
                                float2 a = (float2)f.GetType().GetField("a").GetValue(f), b = (float2)f.GetType().GetField("b").GetValue(f);
                                nearest = Math.Min(nearest, math.distance(crossing, a));
                                if ((int)f.GetType().GetField("vertex").GetValue(f) != 0) continue;
                                float2 d = b - a;
                                boundary = Math.Min(boundary, math.distance(crossing, a + math.saturate(math.dot(crossing - a, d) / math.lengthsq(d)) * d));
                                double cross = (b.x - (double)a.x) * (crossing.y - a.y) - (b.y - (double)a.y) * (crossing.x - a.x);
                                if (a.y <= crossing.y && b.y > crossing.y && cross > 0) winding++;
                                if (a.y > crossing.y && b.y <= crossing.y && cross < 0) winding--;
                            }
                            t.True(nearest < .0001 || winding != 0 || boundary <= .0501,
                                label + " retains a crossing or covers it in the normalized region within the cleanup budget; crossing=" + crossing +
                                ", nearest=" + nearest + ", boundary=" + boundary + ", winding=" + winding);
                        }
                        isolatedTips++;
                        continue;
                    }
                    t.True(math.distance(previous, new float2(incoming.x, incoming.y)) < .07f,
                        label + " retains the incoming flank rather than a diagonal cap");
                    t.True(math.distance(next, new float2(outgoing.x, outgoing.y)) < .07f,
                        label + " retains the outgoing flank rather than a diagonal cap");
                }
            }
            for (int i = 0; i < input.Length; i++) t.Equal(input[i], pixels[i], "Corner fitting preserves source samples");
        }
        t.True(isolatedTips > 0, "The threshold sweep explicitly exercises disconnected subpixel tips");
    }

    static bool IsIsolatedThresholdPixel(Color32[] input, int size, Vector2 expected, byte threshold, out int2 point)
    {
        int cx = Mathf.RoundToInt(expected.x), cy = Mathf.RoundToInt(expected.y);
        for (int y = cy - 1; y <= cy + 1; y++) for (int x = cx - 1; x <= cx + 1; x++)
        {
            double center = input[y * size + x].a - threshold;
            if (center <= 0) continue;
            bool isolated = true;
            foreach (int2 d in new[] { new int2(1, 0), new int2(0, 1), new int2(-1, 0), new int2(0, -1) })
                isolated &= input[(y + d.y) * size + x + d.x].a <= threshold;
            if (!isolated) continue;
            foreach (int dx in new[] { -1, 1 }) foreach (int dy in new[] { -1, 1 })
            {
                double diagonal = input[(y + dy) * size + x + dx].a - threshold;
                double horizontal = input[y * size + x + dx].a - threshold;
                double vertical = input[(y + dy) * size + x].a - threshold;
                if (diagonal > 0 && center * diagonal >= horizontal * vertical) isolated = false;
            }
            if (isolated) { point = new int2(x, y); return true; }
        }
        point = default; return false;
    }

    static void CheckCornerFitGuards(TestContext t)
    {
        var artistic = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var recover = artistic.GetMethod("RecoverCorners", BindingFlags.Static | BindingFlags.NonPublic);
        t.True(recover != null, "The constrained contour fitter is available for independent geometric guards");
        foreach (float radius in new[] { 2f, 6f, 26f, 90f })
        {
            var circle = new List<float2>();
            for (int i = 0; i < 360; i++)
            { double a = i * Math.PI / 180; circle.Add(new float2(100.3f + radius * (float)Math.Cos(a), 99.7f + radius * (float)Math.Sin(a))); }
            var result = (List<float2>)recover.Invoke(null, new object[] { circle });
            t.True(ReferenceEquals(circle, result), "A smooth circle is not classified as a hard corner; radius=" + radius);
        }
        var polygon = new[] { new float2(-30, -25), new float2(30, -25), new float2(30, 22),
            new float2(27, 25), new float2(-27, 25), new float2(-30, 22) };
        foreach (int seam in new[] { 0, 41, 93 }) foreach (bool reverse in new[] { false, true })
        {
            var raw = new List<float2>();
            for (int e = 0; e < polygon.Length; e++)
            {
                float2 a = polygon[e], b = polygon[(e + 1) % polygon.Length];
                int steps = (int)Math.Ceiling(math.distance(a, b) * 2);
                for (int i = 0; i < steps; i++) raw.Add(math.lerp(a, b, i / (float)steps));
            }
            var shifted = new List<float2>();
            for (int i = 0; i < raw.Count; i++) shifted.Add(raw[(i + seam) % raw.Count]);
            if (reverse) shifted.Reverse();
            var result = (List<float2>)recover.Invoke(null, new object[] { shifted });
            foreach (float2 p in result)
            {
                double distance = double.MaxValue;
                for (int e = 0; e < polygon.Length; e++)
                {
                    float2 a = polygon[e], d = polygon[(e + 1) % polygon.Length] - a;
                    distance = Math.Min(distance, math.distance(p, a + math.saturate(math.dot(p - a, d) / math.lengthsq(d)) * d));
                }
                t.True(distance <= .6, "Resolved three-pixel bevel is not replaced by its missing ideal tip; seam=" + seam + ", reverse=" + reverse);
            }
        }
    }

    static void CheckContourNormalization(TestContext t)
    {
        var artistic = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var normalize = artistic.GetMethod("NormalizeContours", BindingFlags.Static | BindingFlags.NonPublic);
        List<float2> Path(params float[] xy)
        { var p = new List<float2>(); for (int i = 0; i < xy.Length; i += 2) p.Add(new float2(xy[i], xy[i + 1])); return p; }
        var paths = new List<List<float2>> {
            Path(0,0, 10,0, 10,10, 0,10), Path(9,2, 11,2, 11,8, 9,8),
            Path(1,1, 2,1, 2,2, 1,2), // Redundant foreground loop inside the filled region.
            Path(3,3, 3,7, 7,7, 7,3), // Oppositely oriented hole.
            Path(14.995f,5, 15,4.995f, 15.005f,5, 15,5.005f) // Genuine small island.
        };
        var result = (List<List<float2>>)normalize.Invoke(null, new object[] { paths });
        t.Equal(3, result.Count, "Union removes overlapping/internal loops but retains a hole and genuine .01px island");
        var rings = new Vector2[result.Count][];
        for (int c = 0; c < result.Count; c++)
        {
            rings[c] = new Vector2[result[c].Count];
            for (int i = 0; i < result[c].Count; i++) rings[c][i] = new Vector2(result[c][i].x, result[c][i].y);
        }
        t.Near(-1, DistanceFieldAnalyticFixture.Distance(rings, 10, 5, 0), .0001,
            "The former intersection seam is filled, not a spurious zero-distance boundary");
        t.Near(2, DistanceFieldAnalyticFixture.Distance(rings, 5, 5, 0), .0001, "Union retains the hole boundary");
        t.Near(-.005 / Math.Sqrt(2), DistanceFieldAnalyticFixture.Distance(rings, 15, 5, 0), .0001,
            "Union precision preserves a genuinely distinct weak island");
        foreach (var path in paths) t.True(path.Count >= 4, "Normalization does not mutate its borrowed contour inputs");

        var guard = artistic.GetMethod("CleanupPreservesTopology", BindingFlags.Static | BindingFlags.NonPublic);
        var original = new List<List<float2>> {
            Path(0,0, 10,0, 10,10, 0,10), Path(4,4, 4,6, 6,6, 6,4)
        };
        var crossing = new List<List<float2>> {
            Path(0,0, 10,10, 0,10), original[1]
        };
        t.True(!(bool)guard.Invoke(null, new object[] { original, crossing }),
            "Cleanup rejects a replacement chord crossing a hole, even if its own winding/area is valid");
        t.True((bool)guard.Invoke(null, new object[] { original, original }), "Unchanged boundaries pass topology guard");
        var capped = new List<List<float2>> {
            Path(-1,0, -1,-10, 1,-10, 1,0, 0,.04f),
            Path(-.005f,.015f, -.005f,.025f, .005f,.025f, .005f,.015f)
        };
        var lostContainment = new List<List<float2>> { Path(-1,0, -1,-10, 1,-10, 1,0), capped[1] };
        t.True(!(bool)guard.Invoke(null, new object[] { capped, lostContainment }),
            "A chord cannot sweep over an entire tiny hole even without edge intersections");
        var kept = (List<List<float2>>)normalize.Invoke(null, new object[] { capped });
        bool tipPresent = false;
        foreach (var path in kept) foreach (float2 p in path) tipPresent |= math.distance(p, new float2(0, .04f)) < .0001f;
        t.True(tipPresent, "Normalization retains the shallow cap that contains the tiny hole");
    }
}

// Independent input/oracle shared explicitly by the layer case's supportFiles.
// Pixel centers are integer coordinates. Rings use even/odd filling.
static class DistanceFieldAnalyticFixture
{
    public static Vector2[] Rectangle(float left, float bottom, float right, float top) => new[] {
        new Vector2(left, bottom), new Vector2(right, bottom), new Vector2(right, top), new Vector2(left, top)
    };

    public static Vector2[] Rotate(Vector2[] points, float angle, Vector2 center)
    {
        var result = new Vector2[points.Length];
        double a = angle * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        for (int i = 0; i < points.Length; i++)
            result[i] = new Vector2((float)(points[i].x * c - points[i].y * s) + center.x,
                (float)(points[i].x * s + points[i].y * c) + center.y);
        return result;
    }

    public static Color32[] Raster(int w, int h, Func<int, int, double> signedDistance, byte threshold = 128, double spread = 2)
    {
        var result = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            byte alpha = (byte)Math.Max(0, Math.Min(255, Math.Round(threshold - signedDistance(x, y) * 127 / spread)));
            result[y * w + x] = new Color32(255, 255, 255, alpha);
        }
        return result;
    }

    // norm 0 = Euclidean, 1 = L1 (Diamond), 2 = L-infinity (Square).
    public static double Distance(Vector2[][] rings, double x, double y, int norm)
    {
        bool inside = false;
        double best = double.PositiveInfinity;
        foreach (var ring in rings) for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Length];
            if ((a.y > y) != (b.y > y) && x < a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
            best = Math.Min(best, SegmentDistance(a, b, x, y, norm));
        }
        return inside ? -best : best;
    }

    static double SegmentDistance(Vector2 a, Vector2 b, double x, double y, int norm)
    {
        double dx = b.x - a.x, dy = b.y - a.y, qx = x - a.x, qy = y - a.y;
        double best = double.PositiveInfinity;
        void Probe(double parameter)
        {
            double u = Math.Max(0, Math.Min(1, parameter));
            double ex = Math.Abs(qx - u * dx), ey = Math.Abs(qy - u * dy);
            double d = norm == 1 ? ex + ey : norm == 2 ? Math.Max(ex, ey) : Math.Sqrt(ex * ex + ey * ey);
            best = Math.Min(best, d);
        }
        Probe(0); Probe(1);
        if (norm == 0 && dx * dx + dy * dy > 0) Probe((qx * dx + qy * dy) / (dx * dx + dy * dy));
        if (norm != 0)
        {
            // Piecewise-linear norms attain minima at endpoints or a kink.
            if (dx != 0) Probe(qx / dx);
            if (dy != 0) Probe(qy / dy);
            if (norm == 2 && dx != dy) Probe((qx - qy) / (dx - dy));
            if (norm == 2 && dx != -dy) Probe((qx + qy) / (dx + dy));
        }
        return best;
    }
}
