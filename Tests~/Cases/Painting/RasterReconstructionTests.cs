// Entries: Accuracy and Boundaries. Explicit support: Framework/TestApi.cs only.
// Each body has a 45s budget, including fixture generation and synchronous jobs.
// Package-owned reflection only. CPU buffers only; no files, assets or windows.
// Inputs are independently supersampled polygon area, never production contours.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DCFApixels.WhimTex;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using WhimTex.Tests;

public static class RasterReconstructionTests
{
    const int BudgetMilliseconds = 45000, Samples = 32;
    const double GeometrySlack = 2.25, DefaultMiter = 4;
    static readonly DistanceMetric[] Modes = { DistanceMetric.CornerFit, DistanceMetric.Spring };

    public static string Accuracy() => TestContext.Run(
        "Inverse AA coverage: exact pixel areas, accepted cap recovery and selected low-resolution angular fields", t =>
    {
        var api = new CpuApi(t, 48);
        t.Equal(12, (int)DistanceMetric.CornerFit, "CornerFit identity");
        t.Equal(13, (int)DistanceMetric.Spring, "Spring identity");
        CheckPixelAreas(t, api);
        CheckAcceptedCap(t, api);
        double[] angles = { 10, 20, 30, 45, 60, 80 };
        int[] sizes = { 16, 32, 64 };
        double[] rotations = { 0, 17, 37 };
        float2[] phases = { new float2(0, 0), new float2(.25f, -.25f),
            new float2(.5f, .25f), new float2(-.25f, .5f) };
        // 18 principal inputs, not the Cartesian product of all parameters.
        for (int a = 0; a < angles.Length; a++) for (int s = 0; s < sizes.Length; s++)
        {
            int size = sizes[s];
            var rings = new[] { Triangle(size, angles[a], rotations[(a + s) % 3], phases[(a + s) % 4]) };
            var source = Raster(size, rings);
            string label = "triangle/" + angles[a] + "/" + size + "/rotation=" + rotations[(a + s) % 3]
                + "/phase=" + phases[(a + s) % 4];
            foreach (DistanceMetric mode in Modes)
                CheckField(t, api, source, rings, size, 128, mode, label);
            // The same source bytes at other thresholds: do not manufacture a
            // threshold-normalized input or claim coverage is a linear SDF ramp.
            if (a == 1 && s == 1 || a == 4 && s == 2)
                foreach (byte threshold in new byte[] { 77, 192 })
                    foreach (DistanceMetric mode in Modes)
                        CheckField(t, api, source, rings, size, threshold, mode, label);
        }
        CheckMiter(t, api);
        t.Equal(48, api.Calls, "36 main + 8 selected threshold + 4 miter field calls");
        t.True(api.NearProbes >= 300, "Selected matrix exercises substantial independently resolved side coverage");
        api.CheckTime("Accuracy complete");
    });

    public static string Boundaries() => TestContext.Run(
        "Raster reconstruction boundaries: holes, thin bars, islands, symmetry, channels and evidence-free fallback", t =>
    {
        var api = new CpuApi(t, 40);
        CheckFallback(t, api);
        CheckTopology(t, api);
        CheckSymmetry(t, api);
        CheckChannels(t, api);
        CheckAmbiguousCells(t, api);
        t.Equal(40, api.Calls, "6 topology + 6 symmetry + 10 channel + 18 ambiguous/uniform field calls");
        api.CheckTime("Boundaries complete");
    });

    public static string SpringContribution()
    {
        int differentPairs = 0, changedSamples = 0;
        double maximumDelta = 0;
        string result = TestContext.Run("Spring relaxation survives into the final field", t =>
        {
            var api = new CpuApi(t, 24);
            const int size = 64;
            foreach (double angle in new[] { 12.0, 30.0, 80.0 })
            foreach (double rotation in new[] { 0.0, 17.0 })
            foreach (byte threshold in new byte[] { 77, 128 })
            {
                var rings = new[] { Triangle(size, angle, rotation, new float2(.25f, -.25f)) };
                var source = Raster(size, rings);
                var corner = CheckField(t, api, source, rings, size, threshold, DistanceMetric.CornerFit, "corner contribution baseline");
                var spring = CheckField(t, api, source, rings, size, threshold, DistanceMetric.Spring, "spring contribution output");
                int changed = 0;
                for (int i = 0; i < corner.Length; i++)
                {
                    double delta = Math.Abs((double)corner[i] - spring[i]);
                    maximumDelta = Math.Max(maximumDelta, delta);
                    if (delta > .0001) changed++;
                }
                if (changed > 0) differentPairs++;
                changedSamples += changed;
            }
            t.True(differentPairs > 0 && changedSamples >= 16,
                "Spring must have a measurable final contribution on eligible raster inputs; pairs=" + differentPairs + ", samples=" + changedSamples);
            t.Equal(24, api.Calls, "12 independently rasterized pairs");
            api.CheckTime("Spring contribution complete");
        });
        var parsed = JsonUtility.FromJson<TestResult>(result);
        parsed.message += "; differing pairs=" + differentPairs + "/12; changed samples=" + changedSamples
            + "; max distance delta=" + maximumDelta.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return parsed.ToJson();
    }

    static void CheckPixelAreas(TestContext t, CpuApi api)
    {
        var square = Rectangle(-.5f, -.5f, .5f, .5f);
        AreaCase(t, api, square, new float2(0, 0), 1, "unit square");
        AreaCase(t, api, square, new float2(.25f, .25f), .5625, "square clipped on two sides");
        AreaCase(t, api, square, new float2(.5f, 0), .5, "half square");
        AreaCase(t, api, square, new float2(1, 0), 0, "touching boxes have zero area");
        AreaCase(t, api, square, new float2(2, 0), 0, "disjoint boxes");
        AreaCase(t, api, Rectangle(-.25f, -.25f, .25f, .25f), new float2(0, 0), .25, "contained small square");
        AreaCase(t, api, Rectangle(-2, -2, 2, 2), new float2(0, 0), 1, "containing square");
        var triangle = new[] { new float2(0, 0), new float2(1, 0), new float2(0, 1) };
        AreaCase(t, api, triangle, new float2(.5f, .5f), .5, "right triangle");
        AreaCase(t, api, triangle, new float2(0, 0), .25, "triangle clipped to lower quarter");
        AreaCase(t, api, triangle, new float2(.75f, .75f), .125, "triangle clipped to half-length legs");
        AreaCase(t, api, triangle, new float2(.25f, .25f), .4375, "triangle minus two 1/32 caps");
        AreaCase(t, api, triangle, new float2(1.5f, 1.5f), 0, "triangle outside box");
        var translated = new float2[triangle.Length];
        for (int i = 0; i < triangle.Length; i++) translated[i] = triangle[i] + new float2(19.25f, 11.5f);
        AreaCase(t, api, translated, new float2(20, 12.25f), .125, "translated clipped triangle");
    }

    static void AreaCase(TestContext t, CpuApi api, float2[] polygon, float2 center, double expected, string label)
    {
        var path = new List<float2>(polygon);
        t.Near(expected, api.PixelArea(path, center), .000002, label + " exact signed area");
        path.Reverse();
        t.Near(-expected, api.PixelArea(path, center), .000002, label + " reverse winding");
    }

    static float2[] CapTriangle() => new[] { new float2(4.25f, 5.25f),
        new float2(28.25f, 5.25f), new float2(16.25f, 25.25f) };

    static List<float2> CappedPath()
    {
        // Deliberately truncate the independently known triangle by .9px.
        // Two long, exact sides and a 1.08px cap supply an identifiable inverse
        // problem. This is a controlled raw-path fixture, not an extracted golden.
        return Densify(new[] { new float2(4.25f, 5.25f), new float2(28.25f, 5.25f),
            new float2(16.79f, 24.35f), new float2(15.71f, 24.35f) });
    }

    static void CheckAcceptedCap(TestContext t, CpuApi api)
    {
        var triangle = CapTriangle();
        var source = Raster(32, new[] { triangle });
        var raw = CappedPath();
        var verified = new HashSet<float2>();
        var refined = api.Refine(raw, source, 32, verified, "known AA cap");
        t.True(verified.Count > 0, "Known box-filtered AA cap must accept a corner; all-fallback is a failure");
        double before = DistanceToPath(raw, triangle[2]), after = DistanceToPath(refined, triangle[2]);
        t.Near(.9, before, .00001, "Fixture has a real independently specified missing tip");
        t.True(after < .3 && after < before * .4,
            "Accepted cap improves true tip geometry; before=" + before + ", after=" + after + ", accepted=" + verified.Count);
        double nearestVerified = double.MaxValue;
        foreach (float2 vertex in verified)
        {
            t.True(refined.Contains(vertex), "Verified vertex belongs to the returned contour");
            nearestVerified = Math.Min(nearestVerified, Length(vertex - triangle[2]));
        }
        t.True(nearestVerified < .3, "The accepted corner includes the missing tip, not only unrelated base vertices");
        var reconstructed = refined.ToArray();
        double oldError = CoverageError(source, Raster(32, new[] { raw.ToArray() }));
        double newError = CoverageError(source, Raster(32, new[] { reconstructed }));
        t.True(oldError > .01 && newError < oldError * .5,
            "Independent supersampled coverage improves; before=" + oldError + ", after=" + newError);
        t.True(SignedArea(reconstructed) > 0, "Recovery preserves orientation");
        // Sample both directions so dropping an edge cannot masquerade as an
        // accurate tip. Finite segments are the oracle, not production PixelArea.
        t.True(BoundaryDeviation(reconstructed, triangle) < .35 && BoundaryDeviation(triangle, reconstructed) < .35,
            "Recovered finite boundary is within .35px of the known triangle in both directions");
    }

    static void CheckMiter(TestContext t, CpuApi api)
    {
        const int size = 64;
        var polygon = new[] { new float2(11.25f, 14.25f), new float2(51.25f, 14.25f), new float2(31.25f, 34.25f) };
        var source = Raster(size, new[] { polygon });
        foreach (DistanceMetric mode in Modes)
        {
            var sharp = api.Field(source, size, 128, mode, "verified right-angle miter");
            var bevel = api.Field(source, size, 128, mode, "lower miter flat bevel", 1.1f);
            int changed = 0;
            for (int y = 37; y <= 41; y++) for (int x = 31; x <= 32; x++)
            {
                double dx = x - 31.25, dy = y - 34.25;
                double expectedSharp = (dy + Math.Abs(dx)) / Math.Sqrt(2);
                double expectedBevel = dy * Math.Sqrt(2);
                int id = y * size + x;
                t.Near(expectedSharp, sharp[id], .75, mode + " independent supported 90-degree sharp fan");
                t.Near(expectedBevel, bevel[id], 1, mode + " independent flat bevel plane");
                t.True(bevel[id] >= sharp[id], mode + " lowering miter cuts the exterior offset tip");
                if (bevel[id] - sharp[id] > .5) changed++;
            }
            t.True(changed >= 6, mode + " raster-to-field integration must use verified corners and honor miter; changed=" + changed);
        }
    }

    static void CheckFallback(TestContext t, CpuApi api)
    {
        var aa = Raster(32, new[] { CapTriangle() });
        for (int variant = 0; variant < 3; variant++)
        {
            var source = new Color32[aa.Length];
            for (int i = 0; i < source.Length; i++)
            {
                byte value = variant == 0 ? (byte)(aa[i].a > 128 ? 255 : 0)
                    : variant == 1 ? (byte)128 : (byte)(((i % 32 + i / 32) & 1) == 0 ? 77 : 192);
                source[i] = new Color32(value, value, value, value);
            }
            var raw = CappedPath();
            var verified = new HashSet<float2>();
            var result = api.Refine(raw, source, 32, verified, "unsupported cap/" + variant);
            t.Equal(0, verified.Count, "Binary/flat/ambiguous coverage cannot verify a sharp cap/" + variant);
            SamePath(t, raw, result, "Unsupported evidence keeps the original finite path/" + variant);
        }
    }

    static void CheckTopology(TestContext t, CpuApi api)
    {
        const int size = 64;
        // Even/odd area raster: a hollow frame, separate 2.5px bar and 1.5px
        // island. Winding is intentionally irrelevant to the independent input.
        var rings = new[] { Rectangle(8.25f, 8.25f, 35.25f, 48.25f),
            Rectangle(13.25f, 13.25f, 30.25f, 43.25f),
            Rectangle(44.25f, 10.25f, 46.75f, 47.25f),
            Rectangle(53.25f, 31.25f, 54.75f, 32.75f) };
        var source = Raster(size, rings);
        foreach (byte threshold in new byte[] { 77, 128, 192 }) foreach (DistanceMetric mode in Modes)
        {
            var field = CheckField(t, api, source, rings, size, threshold, mode, "frame/bar/island");
            t.True(field[28 * size + 22] > 7, mode + " hole stays open at threshold=" + threshold);
            t.True(field[28 * size + 10] < -1, mode + " frame interior is retained");
            t.True(field[28 * size + 45] < 0, mode + " thin bar interior is retained");
            t.True(field[32 * size + 54] < 0, mode + " small island is retained");
            t.True(field[32 * size + 50] > 1, mode + " gap between thin bar and island stays outside");
        }
    }

    static void CheckSymmetry(TestContext t, CpuApi api)
    {
        const int size = 32;
        var rings = new[] { Triangle(size, 60, 17, new float2(.25f, -.25f)) };
        var source = Raster(size, rings);
        foreach (DistanceMetric mode in Modes)
        {
            var original = CheckField(t, api, source, rings, size, 128, mode, "symmetry original");
            for (int transform = 0; transform < 2; transform++)
            {
                var transformed = new Color32[source.Length];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    transformed[TransformedIndex(x, y, size, transform)] = source[y * size + x];
                var geometry = new float2[rings[0].Length];
                for (int i = 0; i < geometry.Length; i++)
                {
                    float2 p = rings[0][i];
                    geometry[i] = transform == 0 ? new float2(size - 1 - p.x, p.y) : new float2(size - 1 - p.y, p.x);
                }
                var field = CheckField(t, api, transformed, new[] { geometry }, size, 128, mode, "symmetry/" + transform);
                int local = 0;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    Probe p = Oracle(rings, x, y);
                    double difference = Math.Abs(original[y * size + x] - field[TransformedIndex(x, y, size, transform)]);
                    if (p.sideSupport >= 2 && Math.Abs(p.signed) <= 2)
                    {
                        t.True(difference <= .65, mode + " near-side mirror/quarter-turn stability/" + transform
                            + " at " + x + "," + y + "; delta=" + difference);
                        local++;
                    }
                    // Angular distances amplify orientation uncertainty with
                    // travel. Do not demand a globally Euclidean or bitwise field.
                    t.True(difference <= 1.5 + .2 * Math.Abs(p.signed), mode + " bounded whole-field symmetry/" + transform
                        + " at " + x + "," + y + "; delta=" + difference);
                }
                t.True(local >= 20, "Symmetry comparison includes supported sides");
            }
        }
    }

    static int TransformedIndex(int x, int y, int size, int transform)
        => transform == 0 ? y * size + size - 1 - x : x * size + size - 1 - y;

    static void CheckChannels(TestContext t, CpuApi api)
    {
        const int size = 16;
        var gray = Raster(size, new[] { Triangle(size, 60, 17, new float2(.25f, .25f)) });
        foreach (DistanceMetric mode in Modes)
        {
            var baseline = api.Field(gray, size, 128, mode, "alpha channel baseline");
            for (int channel = 1; channel <= 4; channel++)
            {
                var source = new Color32[gray.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    byte v = gray[i].a;
                    // Contradictory unused components reveal an accidental alpha
                    // read in refinement. Luminance receives equal RGB components.
                    var p = new Color32((byte)(255 - v), (byte)(255 - v), (byte)(255 - v), (byte)(255 - v));
                    if (channel == 1 || channel == 4) p.r = v;
                    if (channel == 2 || channel == 4) p.g = v;
                    if (channel == 3 || channel == 4) p.b = v;
                    source[i] = p;
                }
                var field = api.Field(source, size, 128, mode, "selected channel=" + channel, 4, channel);
                for (int i = 0; i < field.Length; i++)
                    t.Near(baseline[i], field[i], .0001, mode + " selected source channel=" + channel + "/pixel=" + i);
            }
        }
    }

    static void CheckAmbiguousCells(TestContext t, CpuApi api)
    {
        // Independent finite segments for two alternating 2x2 saddles. Equal
        // +/-64 values cross at edge midpoints. Each pairing has two .5px
        // endpoint distances and two 1/sqrt(8) segment distances; the diagonal
        // receiving each distance is unspecified, so no tie policy is assumed.
        foreach (DistanceMetric mode in Modes)
        {
            foreach (bool invert in new[] { false, true })
            {
                var source = new Color32[4];
                for (int i = 0; i < 4; i++)
                {
                    bool inside = (i == 0 || i == 3) != invert;
                    byte a = (byte)(inside ? 192 : 64);
                    source[i] = new Color32(17, 31, 47, a);
                }
                var field = api.Field(source, 2, 128, mode, "ambiguous 2x2 saddle");
                double sum = 0;
                for (int i = 0; i < 4; i++)
                {
                    double d = Math.Abs(field[i]);
                    t.Equal(source[i].a > 128, field[i] < 0, mode + " saddle retains sample sign/" + i);
                    t.True(Math.Min(Math.Abs(d - .5), Math.Abs(d - 1 / Math.Sqrt(8))) < .00001,
                        mode + " saddle uses finite endpoint or segment distance/" + i + "; actual=" + field[i]);
                    sum += d;
                }
                t.Near(1 + 1 / Math.Sqrt(2), sum, .00001, mode + " both finite saddle branches remain");
                t.Near(field[0], field[3], .00001, mode + " first saddle diagonal symmetry");
                t.Near(field[1], field[2], .00001, mode + " second saddle diagonal symmetry");
            }
            foreach (byte value in new byte[] { 0, 128, 255 })
            foreach (byte threshold in new byte[] { 77, 192 })
            {
                var source = new Color32[16];
                for (int i = 0; i < source.Length; i++) source[i] = new Color32(13, 59, 101, value);
                var field = api.Field(source, 4, threshold, mode, "uniform source");
                foreach (float d in field)
                    t.True(value > threshold ? d < -1000 : d > 1000, mode + " uniform input has no invented boundary");
            }
            // Equality to threshold is not an inside sample, and must not
            // manufacture a contour when every sample equals the threshold.
            var equal = new Color32[16];
            for (int i = 0; i < equal.Length; i++) equal[i] = new Color32(0, 0, 0, 128);
            var equality = api.Field(equal, 4, 128, mode, "uniform exact threshold");
            foreach (float d in equality) t.True(d > 1000, mode + " exact-threshold uniform source stays empty");
        }
    }

    static float[] CheckField(TestContext t, CpuApi api, Color32[] source, float2[][] rings,
        int size, byte threshold, DistanceMetric mode, string name)
    {
        string label = name + "/" + mode + "/threshold=" + threshold;
        var field = api.Field(source, size, threshold, mode, label);
        int near = 0, remote = 0, outside = 0;
        double nearTolerance = threshold == 128 ? .8 : 1.1;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            Probe p = Oracle(rings, x, y);
            double d = Math.Abs(p.signed), actual = field[y * size + x];
            string position = label + " at " + x + "," + y;
            if (d > 1.25)
            {
                t.True(p.signed < 0 ? actual < 0 : actual > 0, position + " independent polygon sign; expected=" + p.signed + ", actual=" + actual);
                if (p.signed > 0) outside++;
            }
            if (p.sideSupport >= 2 && d <= 2)
            {
                t.Near(p.signed, actual, nearTolerance, position + " independent finite supported side");
                near++;
            }
            if (d > GeometrySlack + 1)
            {
                // At default miter=4 a sharp offset of radius r cannot reach
                // farther than 4*r from finite geometry, plus raster uncertainty.
                // A bevel may increase distance, so there is no Euclidean equality.
                t.True(Math.Abs(actual) + .0001 >= (d - GeometrySlack) / DefaultMiter,
                    position + " remote maximum-extension bound; finite=" + d + ", actual=" + actual);
                remote++;
            }
            // Verified corners have a 64x angular safety ceiling; this also
            // rejects finite empty-field sentinels on nonempty mixed rasters.
            t.True(Math.Abs(actual) <= 64 * (d + GeometrySlack) + .001,
                position + " bounded finite/bevel magnitude");
        }
        api.NearProbes += near;
        // A 10-degree triangle at 16px can have no independently resolved
        // two-pixel-wide side. It still receives all-domain sign and extension
        // checks; the larger cases and aggregate count require actual accuracy.
        t.True((near >= 4 || size == 16) && remote >= 8 && outside >= 8,
            label + " non-vacuous supported-side, remote and sign probes; counts=" + near + "/" + remote + "/" + outside);
        api.CheckTime(label);
        return field;
    }

    struct Probe { public double signed, sideSupport; }

    static Probe Oracle(float2[][] rings, double x, double y)
    {
        double nearest = double.MaxValue, support = 0;
        double footX = 0, footY = 0;
        int ownerRing = -1, ownerEdge = -1;
        bool inside = false;
        for (int r = 0; r < rings.Length; r++) for (int i = 0; i < rings[r].Length; i++)
        {
            var ring = rings[r];
            float2 a = ring[i], b = ring[(i + 1) % ring.Length];
            double dx = (double)b.x - a.x, dy = (double)b.y - a.y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double u = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / (length * length)));
            double rx = x - a.x - u * dx, ry = y - a.y - u * dy, distance = Math.Sqrt(rx * rx + ry * ry);
            if (distance < nearest)
            {
                nearest = distance; support = Math.Min(u, 1 - u) * length;
                footX = a.x + u * dx; footY = a.y + u * dy; ownerRing = r; ownerEdge = i;
            }
            if ((a.y > y) != (b.y > y) && x < a.x + (y - a.y) * dx / dy) inside = !inside;
        }
        // Finite endpoint support alone is insufficient on an acute subpixel
        // wedge. Measure clearance at the known boundary foot to every other
        // finite edge, including adjacent flanks and other components. This
        // geometric exclusion does not consult either raster or output field.
        for (int r = 0; r < rings.Length; r++) for (int i = 0; i < rings[r].Length; i++)
        {
            if (r == ownerRing && i == ownerEdge) continue;
            float2 a = rings[r][i], b = rings[r][(i + 1) % rings[r].Length];
            double dx = (double)b.x - a.x, dy = (double)b.y - a.y, length2 = dx * dx + dy * dy;
            double u = length2 == 0 ? 0 : Math.Max(0, Math.Min(1, ((footX - a.x) * dx + (footY - a.y) * dy) / length2));
            double rx = footX - a.x - u * dx, ry = footY - a.y - u * dy;
            support = Math.Min(support, Math.Sqrt(rx * rx + ry * ry));
        }
        return new Probe { signed = inside ? -nearest : nearest, sideSupport = support };
    }

    static Color32[] Raster(int size, float2[][] rings)
    {
        // Count regular 32x32 subpixel centers in each unit pixel box. Scanline
        // crossings integrate the independently specified finite polygons, with
        // even/odd fill for holes. No SDF ramp or production coverage helper.
        var counts = new int[size * size];
        var crossings = new List<double>();
        int extent = size * Samples;
        for (int sy = 0; sy < extent; sy++)
        {
            double y = (sy + .5) / Samples - .5;
            crossings.Clear();
            foreach (var ring in rings) for (int i = 0; i < ring.Length; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % ring.Length];
                if ((a.y > y) != (b.y > y)) crossings.Add(a.x + (y - a.y) * ((double)b.x - a.x) / ((double)b.y - a.y));
            }
            crossings.Sort();
            if ((crossings.Count & 1) != 0) throw new InvalidOperationException("Independent polygon scanline has odd crossings.");
            int row = sy / Samples * size;
            for (int k = 0; k < crossings.Count; k += 2)
            {
                int first = Math.Max(0, (int)Math.Ceiling((crossings[k] + .5) * Samples - .5));
                int end = Math.Min(extent, (int)Math.Ceiling((crossings[k + 1] + .5) * Samples - .5));
                for (int sx = first; sx < end; sx++) counts[row + sx / Samples]++;
            }
        }
        var pixels = new Color32[counts.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)Math.Round(counts[i] * 255.0 / (Samples * Samples));
            pixels[i] = new Color32(v, v, v, v);
        }
        return pixels;
    }

    static float2[] Triangle(int size, double angle, double rotation, float2 phase)
    {
        double half = angle * Math.PI / 360, length = size * .58;
        double x = length * Math.Sin(half), y = length * .5 - length * Math.Cos(half);
        var points = new[] { new float2((float)-x, (float)y), new float2((float)x, (float)y), new float2(0, (float)(length * .5)) };
        double c = Math.Cos(rotation * Math.PI / 180), s = Math.Sin(rotation * Math.PI / 180);
        for (int i = 0; i < points.Length; i++)
        {
            float2 p = points[i];
            points[i] = new float2((float)(p.x * c - p.y * s), (float)(p.x * s + p.y * c)) + new float2((size - 1) * .5f) + phase;
        }
        return points;
    }

    static float2[] Rectangle(float x0, float y0, float x1, float y1)
        => new[] { new float2(x0, y0), new float2(x1, y0), new float2(x1, y1), new float2(x0, y1) };

    static List<float2> Densify(float2[] polygon)
    {
        var result = new List<float2>();
        for (int i = 0; i < polygon.Length; i++)
        {
            float2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            int count = (int)Math.Ceiling(Length(b - a) / .5);
            for (int k = 0; k < count; k++) result.Add(a + (b - a) * (k / (float)count));
        }
        return result;
    }

    static double DistanceToPath(List<float2> path, float2 point)
        => Math.Abs(Oracle(new[] { path.ToArray() }, point.x, point.y).signed);

    static double BoundaryDeviation(float2[] from, float2[] to)
    {
        double result = 0;
        var rings = new[] { to };
        for (int i = 0; i < from.Length; i++)
        {
            float2 a = from[i], b = from[(i + 1) % from.Length];
            int samples = Math.Max(1, (int)Math.Ceiling(Length(b - a) * 4));
            for (int k = 0; k <= samples; k++)
            {
                float2 p = a + (b - a) * (k / (float)samples);
                result = Math.Max(result, Math.Abs(Oracle(rings, p.x, p.y).signed));
            }
        }
        return result;
    }

    static double Length(float2 p) => Math.Sqrt((double)p.x * p.x + (double)p.y * p.y);

    static double SignedArea(float2[] polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            float2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            area += (double)a.x * b.y - (double)a.y * b.x;
        }
        return area * .5;
    }

    static double CoverageError(Color32[] target, Color32[] candidate)
    {
        double error = 0;
        for (int i = 0; i < target.Length; i++) { double d = (target[i].a - candidate[i].a) / 255.0; error += d * d; }
        return error;
    }

    static void SamePath(TestContext t, List<float2> expected, List<float2> actual, string label)
    {
        t.True(actual != null, label + " non-null path");
        t.Equal(expected.Count, actual.Count, label + " point count");
        for (int i = 0; i < expected.Count; i++) t.Equal(expected[i], actual[i], label + " point=" + i);
    }

    sealed class CpuApi
    {
        readonly TestContext test;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly int maximumCalls;
        readonly MethodInfo compute, refine, pixelArea;
        public int Calls { get; private set; }
        public int NearProbes { get; set; }

        public CpuApi(TestContext t, int maximum)
        {
            test = t; maximumCalls = maximum;
            var assembly = typeof(SDFLayerBehaviour).Assembly;
            var utility = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility", true);
            var corner = assembly.GetType("DCFApixels.WhimTex.RasterCornerDistanceContour", true);
            compute = utility.GetMethod("ComputeArtisticDistance", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int), typeof(byte),
                    typeof(int), typeof(DistanceMetric), typeof(float), typeof(float) }, null);
            refine = corner.GetMethod("Refine", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(List<float2>), typeof(NativeArray<Color32>), typeof(int), typeof(int), typeof(byte),
                    typeof(int), typeof(HashSet<float2>) }, null);
            pixelArea = corner.GetMethod("PixelArea", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(List<float2>), typeof(float2) }, null);
            t.True(compute != null && compute.ReturnType == typeof(void), "Exact package ComputeArtisticDistance signature");
            t.True(refine != null && refine.ReturnType == typeof(List<float2>), "Exact package Refine signature");
            t.True(pixelArea != null && pixelArea.ReturnType == typeof(float), "Exact package PixelArea signature");
        }

        public void CheckTime(string label) => test.True(clock.ElapsedMilliseconds < BudgetMilliseconds,
            label + ": body budget 45s including fixture/oracle work; elapsed=" + clock.ElapsedMilliseconds + "ms");

        public float PixelArea(List<float2> path, float2 center)
        {
            CheckTime("PixelArea");
            return (float)pixelArea.Invoke(null, new object[] { path, center });
        }

        public List<float2> Refine(List<float2> raw, Color32[] source, int size, HashSet<float2> verified, string label)
        {
            CheckTime(label);
            var savedPath = new List<float2>(raw);
            using var input = new NativeArray<Color32>(source, Allocator.TempJob);
            var result = (List<float2>)refine.Invoke(null, new object[] { raw, input, size, size, (byte)128, 0, verified });
            SamePath(test, savedPath, raw, label + " raw path is borrowed read-only");
            CheckInput(source, input, label);
            test.True(result != null && result.Count >= 3 && result.Count <= raw.Count * 2, label + " bounded result");
            foreach (float2 p in result) test.True(float.IsFinite(p.x) && float.IsFinite(p.y), label + " finite point");
            CheckTime(label);
            return result;
        }

        public float[] Field(Color32[] source, int size, byte threshold, DistanceMetric mode, string label,
            float miter = 4, int channel = 0)
        {
            CheckTime(label);
            test.True(++Calls <= maximumCalls && size >= 2 && size <= 64, label + " bounded production calls and dimensions");
            using var input = new NativeArray<Color32>(source, Allocator.TempJob);
            using var output = new NativeArray<float>(source.Length, Allocator.TempJob);
            // Poison every output so a partial/no-op write cannot return a
            // plausible cleared zero field. The input remains byte-for-byte owned.
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = float.NaN;
            compute.Invoke(null, new object[] { input, output, size, size, threshold, channel, mode, miter, 0f });
            CheckInput(source, input, label);
            var result = output.ToArray();
            for (int i = 0; i < result.Length; i++) test.True(float.IsFinite(result[i]), label + " finite output/pixel=" + i);
            CheckTime(label);
            return result;
        }

        void CheckInput(Color32[] source, NativeArray<Color32> input, string label)
        {
            for (int i = 0; i < source.Length; i++) test.Equal(source[i], input[i], label + " input borrowed read-only/pixel=" + i);
        }
    }
}
