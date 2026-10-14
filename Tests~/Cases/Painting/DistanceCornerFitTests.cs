// Explicit support: Framework/TestApi.cs only. No assets, windows or external fixtures.
// Package-owned reflection invokes RecoverCorners and DistanceContourFit.Compute.
// Fit arithmetic remains bit-exact; the adaptive acceptance policy is checked
// against independent geometry, not frozen to the obsolete two-pixel limit.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DCFApixels.WhimTex;
using Unity.Mathematics;
using WhimTex.Tests;

public static class DistanceCornerFitTests
{
    const int BudgetMilliseconds = 30000, MaximumFixtures = 160;
    const int MaximumVertices = 1024, MaximumSamples = 2048, MaximumTotalSamples = 100000;
    const int MaximumFitWindowSlots = 6000;

    public static object Run() => TestContext.Run(
        "Frozen Fit-window arithmetic and constrained recovery on independent contour fixtures", t =>
    {
        var clock = Stopwatch.StartNew();
        var type = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", false);
        t.True(type != null, "Package-owned ArtisticDistanceField exists");
        var recover = type.GetMethod("RecoverCorners", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(List<float2>) }, null);
        t.True(recover != null && recover.ReturnType == typeof(List<float2>),
            "Exact package-owned RecoverCorners(List<float2>) signature exists");

        CheckFitWindows(t, clock);
        var fixtures = Fixtures(t);
        t.True(fixtures.Count > 100 && fixtures.Count <= MaximumFixtures, "Bounded nonempty fixture matrix; count=" + fixtures.Count);
        int totalSamples = 0, changedContours = 0, unchangedContours = 0, comparedVertices = 0;
        foreach (var fixture in fixtures)
        {
            CheckTime(t, clock, fixture.name);
            t.True(fixture.points.Count >= 3 && fixture.points.Count <= MaximumVertices,
                fixture.name + " bounded input vertex count");
            CheckFinite(t, fixture.points, fixture.name + " input");
            float perimeter = 0;
            for (int i = 0; i < fixture.points.Count; i++)
                perimeter += math.distance(fixture.points[i], fixture.points[(i + 1) % fixture.points.Count]);
            t.True(Finite(perimeter) && perimeter >= 0 && perimeter <= MaximumSamples * .5f,
                fixture.name + " bounded finite perimeter");
            int samples = perimeter < 32 ? 0 : (int)Math.Ceiling(perimeter / .5f);
            totalSamples += samples;
            t.True(samples <= MaximumSamples && totalSamples <= MaximumTotalSamples,
                fixture.name + " bounded per-contour and aggregate arc-length samples; total=" + totalSamples);

            var productionInput = new List<float2>(fixture.points);
            var actual = (List<float2>)recover.Invoke(null, new object[] { productionInput });
            CheckTime(t, clock, fixture.name + " production");
            t.True(actual != null, fixture.name + " production returns a list");
            CheckFinite(t, actual, fixture.name + " production output");
            CheckOrderedBits(t, fixture.points, productionInput, fixture.name + " production preserves input");
            if (FirstMismatch(fixture.points, actual) < 0) unchangedContours++;
            else changedContours++;
            if (perimeter < 32 || fixture.name.StartsWith("Circle/") || fixture.name.StartsWith("Small circle/"))
                CheckOrderedBits(t, fixture.points, actual, fixture.name + " unchanged smooth/short geometry");
            double Deviation(List<float2> points)
            {
                double largest = 0;
                foreach (float2 p in points)
                {
                    double nearest = double.MaxValue;
                    for (int i = 0; i < fixture.points.Count; i++)
                    {
                        float2 a = fixture.points[i], b = fixture.points[(i + 1) % fixture.points.Count], d = b - a;
                        float length = math.lengthsq(d);
                        float u = length > 1e-12f ? math.saturate(math.dot(p - a, d) / length) : 0;
                        nearest = Math.Min(nearest, math.distance(p, a + u * d));
                    }
                    largest = Math.Max(largest, nearest);
                }
                return largest;
            }
            // The old policy already extrapolated some thin curved caps. Retain
            // its frozen upper bound on these exact non-raster outlines rather
            // than silently permitting additional displacement in this fix.
            var frozenInput = new List<float2>(fixture.points);
            double referenceDeviation = Deviation(FrozenRecoverCorners(frozenInput));
            CheckOrderedBits(t, fixture.points, frozenInput, fixture.name + " frozen baseline preserves input");
            double largestDeviation = Deviation(actual);
            t.True(largestDeviation <= Math.Max(.61, referenceDeviation + .01),
                fixture.name + " does not increase established boundary uncertainty; deviation=" + largestDeviation + ", baseline=" + referenceDeviation);
            comparedVertices += actual.Count;
        }
        t.True(changedContours > 0 && unchangedContours > 0,
            "Fixtures exercise both recovered and unchanged contours; recovered=" + changedContours
                + ", unchanged=" + unchangedContours);
        t.True(comparedVertices > 0, "Compared ordered finite output coordinates; vertices=" + comparedVertices);
        CheckTime(t, clock, "complete body");
    });

    static void CheckTime(TestContext test, Stopwatch clock, string label) => test.True(
        clock.ElapsedMilliseconds < BudgetMilliseconds,
        label + " body budget including fixture generation, managed reference and production startup; elapsed="
            + clock.ElapsedMilliseconds + "ms (limit 30s)");

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    static void CheckFinite(TestContext test, List<float2> points, string label)
    {
        int first = -1;
        for (int i = 0; i < points.Count; i++)
            if (!Finite(points[i].x) || !Finite(points[i].y)) { first = i; break; }
        test.Equal(-1, first, label + " every float2 component is finite");
    }

    static int FirstMismatch(List<float2> expected, List<float2> actual)
    {
        if (expected.Count != actual.Count) return Math.Min(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
            if (!math.all(math.asuint(expected[i]) == math.asuint(actual[i]))) return i;
        return -1;
    }

    static void CheckOrderedBits(TestContext test, List<float2> expected, List<float2> actual, string label)
    {
        test.Equal(expected.Count, actual.Count, label + " vertex count");
        int first = FirstMismatch(expected, actual);
        test.Equal(-1, first, label + " ordered x/y float bits" + (first < 0 ? "" :
            "; vertex=" + first + ", expected=" + Bits(expected[first]) + ", actual=" + Bits(actual[first])));
    }

    static string Bits(float2 value)
    {
        uint2 bits = math.asuint(value);
        return bits.x.ToString("X8") + "/" + bits.y.ToString("X8");
    }

    struct LineCopy
    {
        public float2 center, direction;
        public float error;
        public bool valid;
    }

    sealed class OwnedFitApi
    {
        readonly MethodInfo compute;
        readonly FieldInfo center, direction, error, valid;

        public OwnedFitApi(TestContext test)
        {
            var type = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.DistanceContourFit", false);
            test.True(type != null, "Package-owned DistanceContourFit exists");
            compute = type.GetMethod("Compute", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(float2[]), typeof(int), typeof(bool[]) }, null);
            test.True(compute != null && compute.ReturnType.IsArray && compute.ReturnType.GetArrayRank() == 1,
                "Exact package-owned Compute(float2[], int, bool[]) returns a one-dimensional array");
            var line = compute.ReturnType.GetElementType();
            test.True(line != null && line.IsValueType, "Package fit result element is a value type");
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
            center = line.GetField("center", fields);
            direction = line.GetField("direction", fields);
            error = line.GetField("error", fields);
            valid = line.GetField("valid", fields);
            test.True(center?.FieldType == typeof(float2) && direction?.FieldType == typeof(float2)
                && error?.FieldType == typeof(float) && valid?.FieldType == typeof(bool),
                "Package fit line exposes public float2 center/direction, float error and bool valid");
        }

        public Array Compute(float2[] samples, int flank, bool[] required) =>
            (Array)compute.Invoke(null, new object[] { samples, flank, required });

        public LineCopy ReadRequired(Array lines, int index)
        {
            object line = lines.GetValue(index);
            return new LineCopy { center = (float2)center.GetValue(line), direction = (float2)direction.GetValue(line),
                error = (float)error.GetValue(line), valid = (bool)valid.GetValue(line) };
        }
    }

    sealed class FitFixture
    {
        public readonly string name;
        public readonly float2[] samples;
        public readonly int probeFlank;
        public readonly bool maximumProbe, above;
        public FitFixture(string name, float2[] samples, int probeFlank = 0, bool maximumProbe = false, bool above = false)
        { this.name = name; this.samples = samples; this.probeFlank = probeFlank; this.maximumProbe = maximumProbe; this.above = above; }
    }

    static void CheckFitWindows(TestContext test, Stopwatch clock)
    {
        var api = new OwnedFitApi(test);
        var fixtures = FitFixtures();
        test.Equal(12, fixtures.Count, "Bounded direct Fit fixture matrix");
        int slots = 0, compared = 0, calls = 0, accepted = 0, rejected = 0;
        foreach (var fixture in fixtures)
        {
            CheckTime(test, clock, fixture.name + " Fit fixture");
            test.True(fixture.samples.Length >= 18 && fixture.samples.Length <= 48,
                fixture.name + " bounded cyclic sample buffer");
            CheckFinite(test, new List<float2>(fixture.samples), fixture.name + " Fit samples");
            if (fixture.probeFlank != 0)
            {
                bool valid = FrozenFit(fixture.samples, 0, fixture.probeFlank, out FittedLine probe);
                test.Equal(!fixture.above, valid, fixture.name + " frozen probe straddles the intended validity threshold");
                if (fixture.maximumProbe)
                {
                    // Symmetric center spike(s): max residual targets .35 while
                    // the mean square stays comfortably below its separate .025 gate.
                    int count = fixture.probeFlank + 1, spikes = count % 2 == 0 ? 2 : 1;
                    test.Near(.35 * .35 * spikes / (count - spikes), probe.error, .00002,
                        fixture.name + " frozen maximum-residual probe's mean square");
                    test.True(probe.error < .02f, fixture.name + " validity isolates the maximum-residual gate");
                }
                else test.Near(.025, probe.error, .00002, fixture.name + " frozen mean-square probe is near .025");
            }
            foreach (int flank in new[] { 16, 17, 32, 34 }) for (int mask = 0; mask < 3; mask++)
            {
                string label = fixture.name + "/flank=" + flank + "/mask=" + (mask == 0 ? "all" : mask == 1 ? "partial" : "none");
                CheckTime(test, clock, label);
                // Count every output position, even unrequired/all-false entries,
                // so both base and extended windows/returned slots stay below 6k.
                slots += fixture.samples.Length;
                test.True(slots <= MaximumFitWindowSlots, "Direct Fit output slot budget; slots=" + slots);
                var required = new bool[fixture.samples.Length];
                for (int i = 0; i < required.Length; i++) required[i] = mask == 0 || mask == 1 && i % 3 != 1;
                var input = (float2[])fixture.samples.Clone();
                var requiredInput = (bool[])required.Clone();
                var actual = api.Compute(input, flank, requiredInput);
                calls++;
                CheckTime(test, clock, label + " production");
                test.True(actual != null, label + " returns a line array");
                test.Equal(fixture.samples.Length, actual.Length, label + " output count and cyclic start indexing");
                CheckOrderedBits(test, new List<float2>(fixture.samples), new List<float2>(input), label + " preserves samples");
                int firstMaskChange = -1;
                for (int i = 0; i < required.Length; i++)
                    if (required[i] != requiredInput[i]) { firstMaskChange = i; break; }
                test.Equal(-1, firstMaskChange, label + " preserves required mask");
                for (int first = 0; first < required.Length; first++)
                {
                    // Unrequired entries have no Fit contract. In particular, the
                    // all-false case checks array shape/inputs without reading lines.
                    if (!required[first]) continue;
                    bool wantedValid = FrozenFit(fixture.samples, first, first + flank, out FittedLine wanted);
                    LineCopy got = api.ReadRequired(actual, first);
                    string window = label + "/cyclicStart=" + first;
                    test.True(Finite(wanted.center.x) && Finite(wanted.center.y) && Finite(wanted.direction.x)
                        && Finite(wanted.direction.y) && Finite(wanted.error) && Finite(got.center.x)
                        && Finite(got.center.y) && Finite(got.direction.x) && Finite(got.direction.y) && Finite(got.error),
                        window + " finite frozen and production fields, including invalid required windows");
                    test.True(math.all(math.asuint(wanted.center) == math.asuint(got.center)),
                        window + " center bits; expected=" + Bits(wanted.center) + ", actual=" + Bits(got.center));
                    test.True(math.all(math.asuint(wanted.direction) == math.asuint(got.direction)),
                        window + " direction bits; expected=" + Bits(wanted.direction) + ", actual=" + Bits(got.direction));
                    test.Equal(math.asuint(wanted.error), math.asuint(got.error), window + " mean-square error bits");
                    test.Equal(wantedValid, got.valid, window + " frozen validity");
                    compared++;
                    if (wantedValid) accepted++; else rejected++;
                }
            }
        }
        test.Equal(144, calls, "12 Fit sample buffers x four base/extended flanks x three required masks");
        test.Equal(5376, slots, "Direct Fit slots include every cyclic start and all-false arrays; <=6000");
        test.Equal(2976, compared, "Direct Fit compares required base/extended windows, including all cyclic starts");
        test.True(accepted > 0 && rejected > 0, "Frozen direct Fit oracle exercises valid and invalid windows");
        CheckTime(test, clock, "complete direct Fit scope");
    }

    static List<FitFixture> FitFixtures()
    {
        var star = SampleContour(Star(5, 32, 7), 48);
        var result = new List<FitFixture> {
            new FitFixture("Fit acute star", star),
            new FitFixture("Fit acute star high offset", Transform(new List<float2>(star), .371,
                new float2(65536.25f, -32768.5f)).ToArray()),
            new FitFixture("Fit ellipse high offset", Transform(new List<float2>(SampleContour(Ellipse(64, 31, 9), 48)),
                .733, new float2(-131072.5f, 65536.25f)).ToArray()),
            new FitFixture("Fit intended bevels", SampleContour(BeveledRectangle(30, 18, .375f), 48))
        };
        foreach (int flank in new[] { 16, 17 }) foreach (bool maximum in new[] { false, true })
            foreach (bool above in new[] { false, true })
                result.Add(new FitFixture("Fit " + (maximum ? "maximum .35" : "mean-square .025")
                    + (above ? " just above" : " just below") + "/probeFlank=" + flank,
                    ThresholdSamples(flank, maximum, above), flank, maximum, above));
        return result;
    }

    static float2[] SampleContour(List<float2> polygon, int count)
    {
        var arc = new float[polygon.Count + 1];
        float perimeter = 0;
        for (int i = 0; i < polygon.Count; i++)
            arc[i + 1] = perimeter += math.distance(polygon[i], polygon[(i + 1) % polygon.Count]);
        var result = new float2[count];
        float step = perimeter / count;
        int segment = 0;
        for (int i = 0; i < count; i++)
        {
            float s = i * step;
            while (segment + 1 < polygon.Count && arc[segment + 1] < s) segment++;
            result[i] = math.lerp(polygon[segment], polygon[(segment + 1) % polygon.Count],
                (s - arc[segment]) / (arc[segment + 1] - arc[segment]));
        }
        return result;
    }

    static float2[] ThresholdSamples(int flank, bool maximum, bool above)
    {
        int count = flank + 1, spikes = count % 2 == 0 ? 2 : 1;
        float factor = above ? 1.0002f : .9998f;
        float amplitude = (maximum ? .35f * count / (count - spikes) : (float)Math.Sqrt(.025 * count / 16)) * factor;
        var result = new float2[32];
        for (int i = 0; i < result.Length; i++)
        {
            int pair = Math.Min(i, flank - i);
            float y = i > flank ? 0 : maximum ? (pair == flank / 2 ? amplitude : 0)
                : pair < 4 ? amplitude : pair < 8 ? -amplitude : 0;
            result[i] = new float2((i - flank * .5f) * .5f, y);
        }
        return result;
    }

    // Standalone copy of the ef26001 local Fit body. At retains the old cyclic
    // addressing; no production statistics, trig, residuals or result populate it.
    static bool FrozenFit(float2[] samples, int first, int last, out FittedLine line)
    {
        int n = samples.Length;
        float2 At(int i) => samples[(i % n + n) % n];
        line = default;
        double x = 0, y = 0;
        int count = last - first + 1;
        float2 origin = At(first);
        for (int k = first; k <= last; k++) { float2 p = At(k) - origin; x += p.x; y += p.y; }
        float2 center = new float2((float)(x / count), (float)(y / count));
        double xx = 0, xy = 0, yy = 0;
        for (int k = first; k <= last; k++)
        {
            float2 p = At(k) - origin - center;
            xx += (double)p.x * p.x; xy += (double)p.x * p.y; yy += (double)p.y * p.y;
        }
        double angle = .5 * Math.Atan2(2 * xy, xx - yy);
        float2 direction = new float2((float)Math.Cos(angle), (float)Math.Sin(angle));
        if (math.dot(direction, At(last) - At(first)) < 0) direction = -direction;
        float2 normal = new float2(-direction.y, direction.x);
        float error = 0;
        double squares = 0;
        for (int k = first; k <= last; k++)
        {
            float e = math.abs(math.dot(At(k) - origin - center, normal));
            error = math.max(error, e); squares += e * e;
        }
        line = new FittedLine { center = origin + center, direction = direction, error = (float)(squares / count) };
        return error <= .35f && squares / count <= .025;
    }

    sealed class Fixture
    {
        public readonly string name;
        public readonly List<float2> points;
        public Fixture(string name, List<float2> points) { this.name = name; this.points = points; }
    }

    static List<Fixture> Fixtures(TestContext test)
    {
        var result = new List<Fixture>();
        foreach (bool acute in new[] { false, true })
        {
            var star = Densify(Star(5, acute ? 40 : 32, acute ? 6.5f : 13), .875f);
            string name = acute ? "Acute five-point star" : "Five-point star";
            AddVariants(result, name, star);
            AddVariants(result, name + " rotated/subpixel translated", Transform(star, .371, new float2(.375f, -.625f)));
            AddVariants(result, name + " rotated/large translated", Transform(star, 1.117, new float2(4096.25f, -2048.5f)));
        }
        AddVariants(result, "Circle", Ellipse(128, 22, 22));
        AddVariants(result, "Small circle", Transform(Ellipse(64, 5.25f, 5.25f), .29, new float2(-.375f, .625f)));
        AddVariants(result, "Ellipse rotated/translated", Transform(Ellipse(128, 31, 9), .733, new float2(127.375f, -63.625f)));
        AddVariants(result, "Thin ellipse", Transform(Ellipse(96, 26, 1.5f), 1.037, new float2(-17.25f, 29.75f)));

        AddVariants(result, "Intended wide rectangle bevels", Densify(BeveledRectangle(30, 18, 5), .75f));
        AddVariants(result, "Intended short rectangle bevels", Transform(
            Densify(BeveledRectangle(30, 18, .375f), .75f), .413, new float2(-.25f, .375f)));
        AddVariants(result, "Intended star tip bevels", Densify(Bevel(Star(5, 32, 13), 2), .875f));

        AddPair(result, "Short rectangle below support perimeter", Rectangle(6, 4));
        AddPair(result, "Just below 32 perimeter", Rectangle(8, 7.9375f));
        AddPair(result, "Exactly 32 perimeter", Rectangle(8, 8));
        AddPair(result, "Just above 32 perimeter", Rectangle(8, 8.0625f));
        AddPair(result, "Thin rectangle", Densify(Rectangle(40, .015625f), .875f));
        AddPair(result, "Thin acute triangle", Densify(new List<float2> {
            new float2(-38, 0), new float2(38, .03125f), new float2(-37.875f, .0625f) }, .875f));
        AddPair(result, "Short duplicate contour (defined early return)", new List<float2> {
            new float2(0, 0), new float2(0, 0), new float2(.125f, 0),
            new float2(.125f, 0), new float2(.125f, .0625f), new float2(0, .0625f) });
        AddPair(result, "All coincident vertices (defined early return)", new List<float2> {
            new float2(.375f, -.625f), new float2(.375f, -.625f), new float2(.375f, -.625f) });

        var duplicates = Densify(Rectangle(24, 16), .875f);
        duplicates.Insert(duplicates.Count / 3, duplicates[duplicates.Count / 3]);
        duplicates.Insert(duplicates.Count / 2, duplicates[duplicates.Count / 2]);
        duplicates.Insert(duplicates.Count / 2, duplicates[duplicates.Count / 2]);
        // A long contour's initial zero-length edge makes the old s=0 lerp undefined.
        // Keep duplicate plateaus away from the first edge in both traversal orders.
        AddPair(result, "Long contour with internal duplicate plateaus", duplicates);
        var near = Densify(BeveledRectangle(24, 16, .015625f), .875f);
        near.Insert(1, near[0] + new float2(.0000152587890625f, .00000762939453125f));
        AddPair(result, "Near-degenerate tiny first edge and bevels", near);

        uint state = 0x716E3A29u;
        for (int i = 0; i < 12; i++)
        {
            string name = "Seeded simple closed polygon " + i;
            var polygon = RandomPolygon(6 + i % 9, ref state);
            test.True(IsSimple(polygon), name + " independent skeleton is simple and has nonzero area");
            var points = Transform(Densify(polygon, .875f), .137 * i,
                i % 3 == 0 ? new float2(1024.25f, -512.5f) : new float2(.125f * i, -.375f * i));
            // Vary the seam independently of the random polygon's first angular ray.
            AddPair(result, name, Reindex(points, (i * 17 + 1) % points.Count, false));
        }
        return result;
    }

    static void AddVariants(List<Fixture> fixtures, string name, List<float2> points)
    {
        foreach (int start in new[] { 0, 1, points.Count / 3, points.Count - 1 })
        {
            fixtures.Add(new Fixture(name + "/seam=" + start + "/forward", Reindex(points, start, false)));
            fixtures.Add(new Fixture(name + "/seam=" + start + "/reverse", Reindex(points, start, true)));
        }
    }

    static void AddPair(List<Fixture> fixtures, string name, List<float2> points)
    {
        fixtures.Add(new Fixture(name + "/forward", Reindex(points, 0, false)));
        fixtures.Add(new Fixture(name + "/reverse", Reindex(points, 0, true)));
    }

    static List<float2> Reindex(List<float2> points, int start, bool reverse)
    {
        var result = new List<float2>(points.Count);
        for (int i = 0; i < points.Count; i++)
            result.Add(points[(start + (reverse ? -i : i) + points.Count) % points.Count]);
        return result;
    }

    static List<float2> Transform(List<float2> points, double angle, float2 offset)
    {
        var result = new List<float2>(points.Count);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        foreach (float2 p in points)
            result.Add(new float2((float)(p.x * cos - p.y * sin) + offset.x,
                (float)(p.x * sin + p.y * cos) + offset.y));
        return result;
    }

    static List<float2> Star(int tips, float outer, float inner)
    {
        var result = new List<float2>(2 * tips);
        for (int i = 0; i < 2 * tips; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / tips;
            float radius = i % 2 == 0 ? outer : inner;
            result.Add(new float2((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius)));
        }
        return result;
    }

    static List<float2> Ellipse(int count, float xRadius, float yRadius)
    {
        var result = new List<float2>(count);
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            result.Add(new float2((float)(Math.Cos(angle) * xRadius), (float)(Math.Sin(angle) * yRadius)));
        }
        return result;
    }

    static List<float2> Rectangle(float width, float height) => new List<float2> {
        new float2(0, 0), new float2(width, 0), new float2(width, height), new float2(0, height) };

    static List<float2> BeveledRectangle(float width, float height, float cut) => new List<float2> {
        new float2(cut, 0), new float2(width - cut, 0), new float2(width, cut),
        new float2(width, height - cut), new float2(width - cut, height), new float2(cut, height),
        new float2(0, height - cut), new float2(0, cut) };

    static List<float2> Bevel(List<float2> polygon, float cut)
    {
        var result = new List<float2>(2 * polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            float2 p = polygon[i];
            result.Add(p + cut * math.normalizesafe(polygon[(i + polygon.Count - 1) % polygon.Count] - p));
            result.Add(p + cut * math.normalizesafe(polygon[(i + 1) % polygon.Count] - p));
        }
        return result;
    }

    static List<float2> Densify(List<float2> polygon, float maximumStep)
    {
        var result = new List<float2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            float2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            int count = Math.Max(1, (int)Math.Ceiling(math.distance(a, b) / maximumStep));
            for (int k = 0; k < count; k++) result.Add(k == 0 ? a : math.lerp(a, b, (float)k / count));
        }
        return result;
    }

    static float Next01(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return (state >> 8) * (1f / 16777216f);
    }

    static List<float2> RandomPolygon(int count, ref uint state)
    {
        var result = new List<float2>(count);
        // Positive radii and increasing angles with every adjacent gap < pi keep
        // edges in disjoint angular sectors. Validate the actual float skeleton too.
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * (i + .2 * Next01(ref state)) / count;
            double radius = 12 + 24 * Next01(ref state);
            result.Add(new float2((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius)));
        }
        return result;
    }

    static double Turn(float2 a, float2 b, float2 c) =>
        ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

    static bool OnSegment(float2 p, float2 a, float2 b) =>
        p.x >= Math.Min(a.x, b.x) && p.x <= Math.Max(a.x, b.x)
            && p.y >= Math.Min(a.y, b.y) && p.y <= Math.Max(a.y, b.y);

    static bool Intersects(float2 a, float2 b, float2 c, float2 d)
    {
        double abc = Turn(a, b, c), abd = Turn(a, b, d), cda = Turn(c, d, a), cdb = Turn(c, d, b);
        return ((abc > 0 && abd < 0 || abc < 0 && abd > 0) && (cda > 0 && cdb < 0 || cda < 0 && cdb > 0))
            || abc == 0 && OnSegment(c, a, b) || abd == 0 && OnSegment(d, a, b)
            || cda == 0 && OnSegment(a, c, d) || cdb == 0 && OnSegment(b, c, d);
    }

    static bool IsSimple(List<float2> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            float2 a = points[i], b = points[(i + 1) % points.Count];
            if (!Finite(a.x) || !Finite(a.y) || math.all(a == b)) return false;
            area += (double)a.x * b.y - (double)a.y * b.x;
            for (int j = i + 1; j < points.Count; j++)
            {
                if (j == i + 1 || i == 0 && j == points.Count - 1) continue;
                if (Intersects(a, b, points[j], points[(j + 1) % points.Count])) return false;
            }
        }
        return area != 0;
    }

    // Frozen from src/Layers/ArtisticDistanceField.cs at
    // ef260010da7e64a1b827cf0189c4e2ba75329915. Only the method name and the
    // package timing scope are changed/omitted; all arithmetic, local helpers,
    // short-circuiting, proposal order, tie-breaking and reconstruction are retained.
    // Do not replace this oracle with calls into production or newly captured output.
    struct FittedLine { public float2 center, direction; public float error; }
    struct FittedCorner
    {
        public int index, gap;
        public float2 point;
        public FittedLine left, right;
        public float score;
    }

    // Fit the raw isocontour, before RDP chooses individual edges. Arc-length
    // weighting and orthogonal least squares avoid grid-axis and short-edge bias.
    // The budgets describe raster uncertainty, not a promise to recover vectors.
    static List<float2> FrozenRecoverCorners(List<float2> raw)
    {
        const float spacing = .5f, support = 8f, normalBudget = .6f;
        float perimeter = 0;
        var arc = new float[raw.Count + 1];
        for (int i = 0; i < raw.Count; i++) arc[i + 1] = perimeter += math.distance(raw[i], raw[(i + 1) % raw.Count]);
        if (perimeter < support * 4) return raw;
        int n = (int)Math.Ceiling(perimeter / spacing);
        float step = perimeter / n;
        var samples = new float2[n];
        int segment = 0;
        for (int i = 0; i < n; i++)
        {
            float s = i * step;
            while (segment + 1 < raw.Count && arc[segment + 1] < s) segment++;
            samples[i] = math.lerp(raw[segment], raw[(segment + 1) % raw.Count],
                (s - arc[segment]) / (arc[segment + 1] - arc[segment]));
        }
        float2 At(int i) => samples[(i % n + n) % n];
        bool Fit(int first, int last, out FittedLine line)
        {
            line = default;
            double x = 0, y = 0;
            int count = last - first + 1;
            float2 origin = At(first);
            for (int k = first; k <= last; k++) { float2 p = At(k) - origin; x += p.x; y += p.y; }
            float2 center = new float2((float)(x / count), (float)(y / count));
            double xx = 0, xy = 0, yy = 0;
            for (int k = first; k <= last; k++)
            {
                float2 p = At(k) - origin - center;
                xx += (double)p.x * p.x; xy += (double)p.x * p.y; yy += (double)p.y * p.y;
            }
            double angle = .5 * Math.Atan2(2 * xy, xx - yy);
            float2 direction = new float2((float)Math.Cos(angle), (float)Math.Sin(angle));
            if (math.dot(direction, At(last) - At(first)) < 0) direction = -direction;
            float2 normal = new float2(-direction.y, direction.x);
            float error = 0;
            double squares = 0;
            for (int k = first; k <= last; k++)
            {
                float e = math.abs(math.dot(At(k) - origin - center, normal));
                error = math.max(error, e); squares += e * e;
            }
            line = new FittedLine { center = origin + center, direction = direction, error = (float)(squares / count) };
            return error <= .35f && squares / count <= .025;
        }
        float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        float2 Project(float2 p, FittedLine line) => line.center + math.dot(p - line.center, line.direction) * line.direction;
        int flank = (int)Math.Ceiling(support / step);
        var proposals = new List<FittedCorner>();
        for (int i = 0; i < n; i++)
        {
            // Cheap curvature gate before repeated fits. A smooth arc's two
            // nearby chords cannot establish a hard turn supported by both flanks.
            float2 incoming = math.normalizesafe(At(i) - At(i - flank));
            float2 outgoing = math.normalizesafe(At(i + flank) - At(i));
            if (math.dot(incoming, outgoing) > .8f) continue;
            FittedCorner best = default;
            best.score = float.MaxValue;
            for (int gap = 1; gap * step <= 4; gap++)
            {
                if (!Fit(i - gap - flank, i - gap, out FittedLine left) || !Fit(i + gap, i + gap + flank, out FittedLine right)) continue;
                float dot = math.dot(left.direction, right.direction), cross = Cross(left.direction, right.direction);
                if (dot > .7071068f || math.abs(cross) < .05f) continue;
                float2 point = left.center + Cross(right.center - left.center, right.direction) / cross * left.direction;
                if (math.dot(point - At(i - gap), left.direction) < 0 || math.dot(point - At(i + gap), right.direction) > 0) continue;
                float2 a = Project(At(i - gap - flank), left), b = Project(At(i + gap + flank), right);
                float error = 0, tipDistance = float.MaxValue;
                for (int k = i - gap - flank; k <= i + gap + flank; k++)
                {
                    error = math.max(error, math.min(SquaredSegmentDistance(At(k), a, point), SquaredSegmentDistance(At(k), point, b)));
                    if (k >= i - gap && k < i + gap)
                        tipDistance = math.min(tipDistance, SquaredSegmentDistance(point, At(k), At(k + 1)));
                }
                float cosine = math.sqrt(math.max(.0001f, (1 + dot) * .5f));
                float tipLimit = math.min(2, normalBudget / cosine);
                if (error > normalBudget * normalBudget || tipDistance > tipLimit * tipLimit) continue;
                float score = left.error + right.error + .001f * gap + .01f * error;
                if (score < best.score) best = new FittedCorner { index = i, gap = gap, point = point, left = left, right = right, score = score };
            }
            if (best.score < float.MaxValue) proposals.Add(best);
        }
        proposals.Sort((a, b) => { int order = a.score.CompareTo(b.score); return order != 0 ? order : a.index.CompareTo(b.index); });
        var selected = new List<FittedCorner>();
        var occupied = new bool[n];
        foreach (FittedCorner candidate in proposals)
        {
            bool overlap = false;
            for (int k = -candidate.gap; k <= candidate.gap; k++) overlap |= occupied[(candidate.index + k + n) % n];
            if (overlap) continue;
            selected.Add(candidate);
            for (int k = -candidate.gap - 2; k <= candidate.gap + 2; k++) occupied[(candidate.index + k + n) % n] = true;
        }
        if (selected.Count == 0) return raw;
        selected.Sort((a, b) => a.index.CompareTo(b.index));
        var result = new List<float2>();
        for (int c = 0; c < selected.Count; c++)
        {
            FittedCorner current = selected[c], next = selected[(c + 1) % selected.Count], previous = selected[(c + selected.Count - 1) % selected.Count];
            int before = (current.index - previous.index + n) % n, after = (next.index - current.index + n) % n;
            if (selected.Count == 1) before = after = n;
            int start = current.index - Math.Min(current.gap + flank, before / 2);
            int end = current.index + Math.Min(current.gap + flank, after / 2);
            result.Add(Project(At(start), current.left)); result.Add(current.point); result.Add(Project(At(end), current.right));
            int nextStart = next.index + (c + 1 == selected.Count ? n : 0) - Math.Min(next.gap + flank, after / 2);
            // Preserve unsampled details outside fitted regions. Resampling is
            // only a weighting device; it must not replace the remaining contour.
            float from = end * step, to = nextStart * step;
            int cycle = (int)Math.Floor(from / perimeter);
            int index = Array.BinarySearch(arc, from - cycle * perimeter);
            index = index < 0 ? ~index : index + 1;
            if (index >= raw.Count) { index = 0; cycle++; }
            while (arc[index] + cycle * perimeter < to)
            {
                result.Add(raw[index]);
                if (++index == raw.Count) { index = 0; cycle++; }
            }
        }
        return result;
    }

    static float SquaredSegmentDistance(float2 p, float2 a, float2 b)
    {
        float2 delta = b - a;
        float length = math.lengthsq(delta);
        float t = length > 1e-12f ? math.saturate(math.dot(p - a, delta) / length) : 0;
        return math.lengthsq(p - a - t * delta);
    }
}
