// Explicit support: Framework/TestApi.cs only. No fixtures, assets, windows or files.
// Package-owned reflection binds Extract and the cell type; Unity APIs are public.
// This checks extraction equivalence, not final distance fields or render goldens.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DCFApixels.WhimTex;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using WhimTex.Tests;

public static class DistanceContourExtractionTests
{
    const int BudgetMilliseconds = 30000, MaximumCalls = 4000;
    static readonly byte[] Thresholds = { 0, 64, 96, 118, 128, 143, 160, 192, 228, 255 };
    static readonly int[] ChunkSizes = { 1, 7, 32 };

    public static string Run() => TestContext.Run("Bit-exact ordered Jobs contour extraction against the original managed scan", t =>
    {
        var api = new OwnedApi(t);
        var fixtures = Fixtures();
        foreach (var fixture in fixtures)
        {
            using var pixels = new NativeArray<Color32>(fixture.pixels, Allocator.TempJob);
            foreach (byte threshold in Thresholds) for (int channel = 0; channel < 5; channel++)
            {
                var expected = OriginalScan(fixture, threshold, channel);
                string label = fixture.name + "/channel=" + channel + "/threshold=" + threshold;
                if (fixture.height == 1)
                {
                    // There are no cell rows. Build skips Extract: zero rowCount is
                    // deliberately outside the production method's valid contract.
                    t.Equal(0, expected.Length, label + " has no cell rows; no Extract call");
                    continue;
                }
                api.CheckRange(pixels, fixture, threshold, channel, expected, 0, expected.Length, label);
                if (fixture.name == "Chunked random 7x9")
                    foreach (int chunk in ChunkSizes)
                        api.CheckChunks(pixels, fixture, threshold, channel, expected, chunk, label);
                if (fixture.name == "Random 64x64")
                {
                    api.CheckChunks(pixels, fixture, threshold, channel, expected, 7, label);
                    api.CheckChunks(pixels, fixture, threshold, channel, expected, 32, label);
                    if (threshold == 128)
                        api.CheckChunks(pixels, fixture, threshold, channel, expected, 1, label);
                    foreach (var range in new[] { new int2(1, 1), new int2(6, 2), new int2(7, 7),
                        new int2(31, 2), new int2(32, 1), new int2(62, 1) })
                        api.CheckRange(pixels, fixture, threshold, channel, expected, range.x, range.y,
                            label + "/subrange=" + range.x + "+" + range.y);
                }
            }
            CheckInput(t, fixture, pixels);
        }
        // Exhaust all corner sign masks, including both ambiguous checkerboards,
        // using nearest byte neighbors on either side of each requested threshold.
        // At 0/255 the unrepresentable side naturally collapses to equality.
        foreach (byte threshold in Thresholds) for (int mask = 0; mask < 16; mask++)
        {
            byte low = threshold == 0 ? (byte)0 : (byte)(threshold - 1);
            byte high = threshold == 255 ? (byte)255 : (byte)(threshold + 1);
            Color32 Corner(int bit) => Gray((mask & (1 << bit)) == 0 ? low : high);
            var fixture = new Fixture("Corner sign mask=" + mask, 2, 2,
                new[] { Corner(0), Corner(1), Corner(3), Corner(2) });
            using var pixels = new NativeArray<Color32>(fixture.pixels, Allocator.TempJob);
            for (int channel = 0; channel < 5; channel++)
            {
                var expected = OriginalScan(fixture, threshold, channel);
                api.CheckRange(pixels, fixture, threshold, channel, expected, 0, 1,
                    fixture.name + "/channel=" + channel + "/threshold=" + threshold);
            }
            CheckInput(t, fixture, pixels);
        }
        t.True(api.Calls > 2000 && api.Calls <= MaximumCalls,
            "Nonempty bounded matrix of extraction calls; actual=" + api.Calls);
        t.True(api.Cells > 100000, "Substantial ordered, four-component bit comparisons; cells=" + api.Cells);
        api.CheckTime();
    });

    struct CellCopy
    {
        public int x, y;
        public float4 values;
    }

    sealed class Fixture
    {
        public readonly string name;
        public readonly int width, height;
        public readonly Color32[] pixels;
        public Fixture(string name, int width, int height, Color32[] pixels)
        { this.name = name; this.width = width; this.height = height; this.pixels = pixels; }
    }

    delegate CellCopy[][] CopyStream(NativeStream stream, int rowCount);

    sealed class OwnedApi
    {
        readonly TestContext test;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly MethodInfo extract;
        readonly CopyStream copy;
        public int Calls { get; private set; }
        public int Cells { get; private set; }

        public OwnedApi(TestContext test)
        {
            this.test = test;
            var assembly = typeof(SDFLayerBehaviour).Assembly;
            var api = assembly.GetType("DCFApixels.WhimTex.DistanceContourCells", false);
            var cell = assembly.GetType("DCFApixels.WhimTex.DistanceContourCell", false);
            test.True(api != null && cell != null && cell.IsValueType, "Package contour extraction types exist");
            extract = api.GetMethod("Extract", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(NativeArray<Color32>), typeof(int), typeof(int), typeof(byte),
                    typeof(int), typeof(int), typeof(int) }, null);
            test.True(extract != null && extract.ReturnType == typeof(NativeStream), "Exact package Extract signature exists");
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
            test.True(cell.GetField("x", fields)?.FieldType == typeof(int)
                && cell.GetField("y", fields)?.FieldType == typeof(int)
                && cell.GetField("values", fields)?.FieldType == typeof(float4), "Cell public field contract exists");
            var bridge = typeof(DistanceContourExtractionTests).GetMethod(nameof(CopyRows), BindingFlags.Static | BindingFlags.NonPublic);
            copy = (CopyStream)Delegate.CreateDelegate(typeof(CopyStream), bridge.MakeGenericMethod(cell));
        }

        public void CheckTime() => test.True(clock.ElapsedMilliseconds < BudgetMilliseconds,
            "30s wall-clock safety budget including job startup; elapsed=" + clock.ElapsedMilliseconds + "ms, calls=" + Calls);

        public int CheckRange(NativeArray<Color32> pixels, Fixture fixture, byte threshold, int channel,
            CellCopy[][] expected, int firstRow, int rowCount, string label)
        {
            CheckTime();
            test.True(++Calls <= MaximumCalls, "Bounded extraction invocation count");
            var stream = default(NativeStream);
            try
            {
                stream = (NativeStream)extract.Invoke(null, new object[] { pixels, fixture.width, fixture.height,
                    threshold, channel, firstRow, rowCount });
                test.True(stream.IsCreated, label + " returns an owned stream");
                test.Equal(rowCount, stream.ForEachCount, label + " stream indexes are local rows");
                var actual = copy(stream, rowCount);
                int count = 0;
                for (int local = 0; local < rowCount; local++)
                {
                    var wanted = expected[firstRow + local];
                    var got = actual[local];
                    string rowLabel = label + "/firstRow=" + firstRow + "/localRow=" + local;
                    test.Equal(wanted.Length, got.Length, rowLabel + " active cell count");
                    int firstOrder = -1, firstValue = -1;
                    for (int i = 0; i < wanted.Length; i++)
                    {
                        if ((wanted[i].x != got[i].x || wanted[i].y != got[i].y) && firstOrder < 0) firstOrder = i;
                        if (!math.all(math.asuint(wanted[i].values) == math.asuint(got[i].values)) && firstValue < 0) firstValue = i;
                    }
                    test.Equal(-1, firstOrder, rowLabel + " absolute x/y and increasing-x sequence" +
                        (firstOrder < 0 ? "" : "; expected=" + Coordinates(wanted[firstOrder]) + ", got=" + Coordinates(got[firstOrder])));
                    test.Equal(-1, firstValue, rowLabel + " a/b/c/d bit patterns" +
                        (firstValue < 0 ? "" : "; cell=" + Coordinates(wanted[firstValue]) + ", expected=" +
                            Bits(wanted[firstValue].values) + ", got=" + Bits(got[firstValue].values)));
                    count += got.Length;
                }
                test.Equal(count, stream.Count(), label + " total stream count has no unread/extra records");
                Cells += count;
                return count;
            }
            finally { if (stream.IsCreated) stream.Dispose(); }
        }

        public void CheckChunks(NativeArray<Color32> pixels, Fixture fixture, byte threshold, int channel,
            CellCopy[][] expected, int chunk, string label)
        {
            int observed = 0, wanted = 0;
            foreach (var row in expected) wanted += row.Length;
            for (int first = 0; first < expected.Length; first += chunk)
                observed += CheckRange(pixels, fixture, threshold, channel, expected, first,
                    Math.Min(chunk, expected.Length - first), label + "/chunk=" + chunk);
            // Each chunk was compared to its absolute rows, so concatenation cannot
            // duplicate/omit/reorder rows even though its foreach indexes restart at 0.
            test.Equal(wanted, observed, label + "/chunk=" + chunk + " ordered concatenation matches the full scan");
        }
    }

    static class CellFields<T> where T : unmanaged
    {
        public static readonly FieldInfo X = typeof(T).GetField("x", BindingFlags.Public | BindingFlags.Instance);
        public static readonly FieldInfo Y = typeof(T).GetField("y", BindingFlags.Public | BindingFlags.Instance);
        public static readonly FieldInfo Values = typeof(T).GetField("values", BindingFlags.Public | BindingFlags.Instance);
    }

    static CellCopy[][] CopyRows<T>(NativeStream stream, int rowCount) where T : unmanaged
    {
        // Close this test-owned generic once over the internal package cell type.
        // Reader stays a typed mutable local: boxing it for separate reflected
        // Begin/Read/End calls would lose the cursor when a fresh box is made.
        var reader = stream.AsReader();
        var result = new CellCopy[rowCount][];
        for (int row = 0; row < rowCount; row++)
        {
            int count = reader.BeginForEachIndex(row);
            var cells = new CellCopy[count];
            for (int i = 0; i < count; i++)
            {
                T value = reader.Read<T>();
                object boxedCell = value;
                cells[i] = new CellCopy { x = (int)CellFields<T>.X.GetValue(boxedCell),
                    y = (int)CellFields<T>.Y.GetValue(boxedCell), values = (float4)CellFields<T>.Values.GetValue(boxedCell) };
            }
            if (reader.RemainingItemCount != 0) throw new InvalidOperationException("Reader retained unread row records.");
            reader.EndForEachIndex();
            result[row] = cells;
        }
        return result;
    }

    // Frozen copy of the original managed Value expression. Do not replace this
    // with DistanceFieldSource.Value, a dot product, double math or fused operations:
    // association/rounding of luminance and subtraction are part of the oracle.
    static float OriginalValue(Color32 color, int sourceChannel)
    {
        float value;
        switch (sourceChannel)
        {
            case 1: value = color.r; break;
            case 2: value = color.g; break;
            case 3: value = color.b; break;
            case 4: value = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b; break;
            default: value = color.a; break;
        }
        return value;
    }

    static CellCopy[][] OriginalScan(Fixture fixture, byte threshold, int channel)
    {
        int width = fixture.width;
        var rows = new CellCopy[Math.Max(0, fixture.height - 1)][];
        for (int y = 0; y + 1 < fixture.height; y++)
        {
            var active = new List<CellCopy>();
            for (int x = 0; x + 1 < width; x++)
            {
                float a = OriginalValue(fixture.pixels[y * width + x], channel) - threshold;
                float b = OriginalValue(fixture.pixels[y * width + x + 1], channel) - threshold;
                float c = OriginalValue(fixture.pixels[(y + 1) * width + x + 1], channel) - threshold;
                float d = OriginalValue(fixture.pixels[(y + 1) * width + x], channel) - threshold;
                // Independent old crossing test, not the job's all-equal predicate.
                int crossings = 0;
                if ((a > 0) != (b > 0)) crossings++;
                if ((b > 0) != (c > 0)) crossings++;
                if ((c > 0) != (d > 0)) crossings++;
                if ((d > 0) != (a > 0)) crossings++;
                if (crossings != 0) active.Add(new CellCopy { x = x, y = y, values = new float4(a, b, c, d) });
            }
            rows[y] = active.ToArray();
        }
        return rows;
    }

    static void CheckInput(TestContext test, Fixture fixture, NativeArray<Color32> pixels)
    {
        int firstChanged = -1;
        for (int i = 0; i < fixture.pixels.Length; i++)
            if (!fixture.pixels[i].Equals(pixels[i])) { firstChanged = i; break; }
        test.Equal(-1, firstChanged, fixture.name + " extraction preserves every input byte");
    }

    static string Coordinates(CellCopy cell) => cell.x + "," + cell.y;
    static string Bits(float4 values)
    {
        uint4 bits = math.asuint(values);
        return bits.x.ToString("X8") + "/" + bits.y.ToString("X8") + "/" + bits.z.ToString("X8") + "/" + bits.w.ToString("X8");
    }

    static Color32 Gray(byte value) => new Color32(value, value, value, value);

    static List<Fixture> Fixtures()
    {
        var result = new List<Fixture> {
            Solid("Single transparent pixel", 1, 1, Gray(0)),
            Solid("Single opaque pixel", 1, 1, Gray(255)),
            Solid("Single equality pixel", 1, 1, Gray(128)),
            Random("Single column 1x64", 1, 64, 0x7DCE129Bu),
            Random("Single row 64x1", 64, 1, 0xE9123A87u),
            Solid("Empty transparent source", 17, 9, Gray(0)),
            Solid("Solid opaque source", 17, 9, Gray(255)),
            Solid("Solid equality source", 17, 9, Gray(118)),
            Solid("Solid colored source", 17, 9, new Color32(71, 193, 246, 160)),
            Random("Random 2x2", 2, 2, 0xAECB921Fu),
            Random("Random 3x9", 3, 9, 0x19AB398Du),
            Random("Random 8x7", 8, 7, 0x3819AD2Bu),
            Random("Random 32x33", 32, 33, 0x9A011FA3u),
            Random("Random 64x64", 64, 64, 0x62EC4301u),
            Random("Chunked random 7x9", 7, 9, 0x71CA42DDu)
        };
        byte[] palette = { 0, 1, 63, 64, 65, 95, 96, 97, 117, 118, 119, 127, 128, 129,
            142, 143, 144, 159, 160, 161, 191, 192, 193, 227, 228, 229, 254, 255 };
        var ties = new Color32[17 * 18];
        for (int y = 0; y < 18; y++) for (int x = 0; x < 17; x++)
            ties[y * 17 + x] = new Color32(palette[(x + y) % palette.Length], palette[(3 * x + y) % palette.Length],
                palette[(x + 5 * y) % palette.Length], palette[(7 * x + 3 * y) % palette.Length]);
        result.Add(new Fixture("Threshold ties and channel palette", 17, 18, ties));
        var equality = new Color32[9];
        for (int i = 0; i < equality.Length; i++) equality[i] = Gray(i == 4 ? (byte)128 : i % 2 == 0 ? (byte)255 : (byte)0);
        result.Add(new Fixture("Equality degree-four checkerboard", 3, 3, equality));
        foreach (string shape in new[] { "Soft star", "Soft ring", "Soft diagonals" })
            result.Add(SoftShape(shape));
        return result;
    }

    static Fixture Solid(string name, int width, int height, Color32 value)
    {
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = value;
        return new Fixture(name, width, height, pixels);
    }

    static Fixture Random(string name, int width, int height, uint seed)
    {
        // Fixed xorshift32, independent of System.Random implementation/platform.
        byte Next()
        { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (byte)(seed >> 24); }
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(Next(), Next(), Next(), Next());
        return new Fixture(name, width, height, pixels);
    }

    static Fixture SoftShape(string name)
    {
        const int size = 64;
        var polygon = new double2[10];
        for (int i = 0; i < polygon.Length; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / 5, radius = i % 2 == 0 ? 24 : 10;
            polygon[i] = new double2(31.37 + Math.Cos(angle) * radius, 30.63 + Math.Sin(angle) * radius);
        }
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double dx = x - 31.37, dy = y - 30.63;
            double distance = name == "Soft ring" ? Math.Abs(Math.Sqrt(dx * dx + dy * dy) - 18) - 3.2
                : name == "Soft diagonals" ? Math.Min(Math.Abs(dx + .73 * dy) / Math.Sqrt(1 + .73 * .73),
                    Math.Abs(dx - .81 * dy) / Math.Sqrt(1 + .81 * .81)) - 2.3
                : PolygonDistance(polygon, x, y);
            byte coverage = (byte)Math.Round(Math.Max(0, Math.Min(255, 128 - 72 * distance)));
            pixels[y * size + x] = Gray(coverage);
        }
        return new Fixture(name, size, size, pixels);
    }

    static double PolygonDistance(double2[] polygon, double x, double y)
    {
        double best = double.PositiveInfinity;
        bool inside = false;
        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
            double dx = b.x - a.x, dy = b.y - a.y;
            double u = Math.Max(0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy)));
            double ex = x - a.x - u * dx, ey = y - a.y - u * dy;
            best = Math.Min(best, ex * ex + ey * ey);
            if ((a.y > y) != (b.y > y) && x < a.x + (y - a.y) * dx / dy) inside = !inside;
        }
        return Math.Sqrt(best) * (inside ? -1 : 1);
    }
}
