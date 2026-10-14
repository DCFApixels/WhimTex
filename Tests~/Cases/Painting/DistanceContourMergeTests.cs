// Entries: Run (bounded geometry), RunFields (42 CPU calls, maximum 128x128).
// Support: Tests~/Framework/TestApi.cs; RunFields also requires
// Tests~/Cases/Painting/DistanceFieldTests.cs for DistanceFieldAnalyticFixture.
// Both entries need that support file when the runner compiles this whole source.
// No assets, documents, windows, output goldens or Unity internal reflection.
// Captured source/UI/save coverage belongs to the separate layer tests.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class DistanceContourMergeTests
{
    const int BudgetMilliseconds = 30000;
    const double Cap = 4, Support = 2;

    public static string Run() => TestContext.Run("Opt-in contour midpoint welding and topology guards", t =>
    {
        var clock = Stopwatch.StartNew();
        var type = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var method = type.GetMethod("MergeCloseVertices", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(List<List<float2>>), typeof(float) }, null);
        t.True(method != null, "Package-owned merge entry exists");
        int calls = 0;
        List<List<float2>> Merge(List<List<float2>> source, float distance)
        {
            CheckTime(clock);
            t.True(++calls <= 100, "At most 100 geometry calls");
            var saved = new List<float2[]>(source.Count);
            var references = source.ToArray();
            foreach (var path in source) saved.Add(path.ToArray());
            var result = (List<List<float2>>)method.Invoke(null, new object[] { source, distance });
            t.Equal(saved.Count, source.Count, "Borrowed contour list count");
            for (int c = 0; c < saved.Count; c++)
            {
                t.True(ReferenceEquals(references[c], source[c]), "Borrowed path reference");
                t.Equal(saved[c].Length, source[c].Count, "Borrowed vertex count");
                for (int i = 0; i < saved[c].Length; i++)
                    t.True(math.all(math.asuint(saved[c][i]) == math.asuint(source[c][i])), "Borrowed vertex bits");
            }
            CheckTime(clock);
            return result;
        }

        var shortEdge = Path(0,0, 1,0, 10,0, 10,10, 0,10);
        var contours = new List<List<float2>> { shortEdge };
        t.True(ReferenceEquals(contours, Merge(contours, 0)), "Zero is reference passthrough");
        t.True(ReferenceEquals(contours, Merge(contours, .999f)), "Edge just above threshold is unchanged");
        foreach (float distance in new[] { 1f, 2f })
        {
            var result = Merge(contours, distance);
            t.True(!ReferenceEquals(contours, result), "Opt-in really changes eligible geometry at " + distance);
            SameCycle(t, Path(.5f,0, 10,0, 10,10, 0,10), result[0], "Exact midpoint at " + distance);
        }
        var two = new List<List<float2>> { Path(0,0, 2,0, 10,0, 10,10, 0,10) };
        t.True(ReferenceEquals(two, Merge(two, 1)), "Merge 1 cannot weld a two-pixel edge");
        t.True(ReferenceEquals(two, Merge(two, 1.999f)), "Two-pixel threshold has no extra epsilon");
        SameCycle(t, Path(1,0, 10,0, 10,10, 0,10), Merge(two, 2)[0], "Inclusive two-pixel threshold");

        // Equal-length disjoint edges plus a closing seam. Test every start and
        // both orientations against independently declared midpoint coordinates.
        var pairs = Path(0,0, 1,0, 10,0, 10,10, 9,10, 0,10);
        var expected = Path(.5f,0, 10,0, 9.5f,10, 0,10);
        for (int seam = 0; seam < pairs.Count; seam++)
        foreach (bool reverse in new[] { false, true })
        {
            var input = RotatePath(pairs, seam, reverse);
            var wanted = new List<float2>(expected);
            if (reverse) wanted.Reverse();
            var result = Merge(new List<List<float2>> { input }, 1)[0];
            SameCycle(t, wanted, result, "Seam/reversal disjoint midpoint selection");
            t.True(Area(input) * Area(result) > 0, "Accepted weld preserves winding");
            t.True(Math.Abs(Area(result)) >= .8 * Math.Abs(Area(input)), "Accepted weld retains at least 80% area");
        }
        // The shortest .5 edge wins over its .75 and 1 neighbours. The new
        // (1,0) remains within threshold of (0,0), but cannot merge again.
        var chain = Path(0,0, .75f,0, 1.25f,0, 2.25f,0, 3,0, 10,0, 10,10, 0,10);
        var chainExpected = Path(0,0, 1,0, 2.625f,0, 10,0, 10,10, 0,10);
        foreach (bool reverse in new[] { false, true })
        {
            var wanted = new List<float2>(chainExpected);
            if (reverse) wanted.Reverse();
            SameCycle(t, wanted, Merge(new List<List<float2>> { RotatePath(chain, 2, reverse) }, 1)[0],
                "Shortest disjoint pairs, each original point once, no cascading");
        }
        var minimum = new List<List<float2>> { Path(0,0, .125f,0, 1,0, 0,1) };
        var triangle = Merge(minimum, 2);
        SameCycle(t, Path(.0625f,0, 1,0, 0,1), triangle[0], "Stop at three points even with more eligible edges");
        t.True(ReferenceEquals(triangle, Merge(triangle, 2)), "A triangle cannot collapse");
        var tiny = new List<List<float2>> { Path(0,0, 1,0, 1,1, 0,1) };
        t.True(ReferenceEquals(tiny, Merge(tiny, 2)), "50% area-loss candidate is rejected");
        var degenerate = new List<List<float2>> { Path(0,0, .5f,0, 1,0, 2,0) };
        t.True(ReferenceEquals(degenerate, Merge(degenerate, 2)), "Zero-area candidate has no valid winding");

        var neighbours = new List<List<float2>> {
            Path(0,0, 10,0, 10,10, 0,10), Path(10.25f,0, 20.25f,0, 20.25f,10, 10.25f,10)
        };
        t.True(ReferenceEquals(neighbours, Merge(neighbours, 2)), "Nearby vertices on different loops never weld");
        var safeHole = Path(3,3, 3,7, 7,7, 7,3);
        var island = Path(20,0, 20.25f,0, 20.25f,.25f, 20,.25f);
        var mixed = Merge(new List<List<float2>> { shortEdge, safeHole, island }, 2);
        t.Equal(3, mixed.Count, "Independent island and hole counts survive a successful outer weld");
        SameCycle(t, expected: Path(.5f,0, 10,0, 10,10, 0,10), actual: mixed[0], label: "Outer still changes");
        t.True(ReferenceEquals(safeHole, mixed[1]) && ReferenceEquals(island, mixed[2]), "Hole and tiny island retain their original loops");
        t.True(Inside(mixed[0], new float2(5,5)) && Inside(mixed[1], new float2(5,5)), "Hole stays contained");
        t.True(!Inside(mixed[0], island[0]), "Separate island stays outside");

        var chamfer = Path(0,0, 10,0, 10,10, 1,10, 0,9);
        var chamferExpected = Path(0,0, 10,0, 10,10, .5f,9.5f);
        SameCycle(t, chamferExpected, Merge(new List<List<float2>> { chamfer }, 2)[0], "Topology fixtures have an eligible safe weld in isolation");
        var obstacles = new[] {
            Path(.08f,7, .1f,7.2f, .12f,7), // Entire hole swept over without any final crossing.
            Path(.125f,4.5f, .125f,5, .25f,4.75f), // Exact contact with x=y/19.
            Path(.125f,4.5f, .125f,5, .5f,4.75f) // New edge crosses the hole.
        };
        for (int i = 0; i < obstacles.Length; i++)
        {
            if (i == 0) foreach (float2 point in obstacles[i])
                t.True(Inside(chamfer, point) && !Inside(chamferExpected, point), "Independent swept-hole containment precondition");
            var batch = new List<List<float2>> { chamfer, obstacles[i], Path(30,0, 31,0, 40,0, 40,10, 30,10) };
            t.True(ReferenceEquals(batch, Merge(batch, 2)), "Conflict rejects the WHOLE batch, including a distant eligible loop: " + i);
        }
        var crossing = new List<List<float2>> {
            Path(0,0, 10,0, 10,3, .2f,3, .2f,6, 10,6, 10,10, 1,10, 0,9)
        };
        t.True(ReferenceEquals(crossing, Merge(crossing, 2)), "Moved closing edge cannot cross a nonadjacent edge of its own loop");
        var touching = new List<List<float2>> { Path(0,0, 1,0, 4,0, 4,4, 0,4, 0,0, -4,0, -4,-4, 0,-4) };
        t.True(ReferenceEquals(touching, Merge(touching, 2)), "Original self-touching loop with repeated vertex is skipped");
        foreach (float invalid in new[] { -.01f, 2.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try { Merge(contours, invalid); }
            catch (TargetInvocationException error) { rejected = error.InnerException is ArgumentOutOfRangeException; }
            t.True(rejected, "Invalid merge distance is rejected: " + invalid);
        }
        CheckTime(clock);
    });

    public static string RunFields()
    {
        var changes = new[] { new Changes(), new Changes() };
        string result = TestContext.Run("Bounded merge 0/1/2 raster safety; 42 calls; no output-golden claim", t =>
        {
            var api = new FieldApi(t);
            foreach (double angle in new[] { 30.0, 45, 60, 75, 90 })
            {
                double half = angle * Math.PI / 360;
                var polygon = DistanceFieldAnalyticFixture.Rotate(new[] {
                    new Vector2((float)(-34*Math.Sin(half)), (float)(-34*Math.Cos(half))),
                    new Vector2((float)(34*Math.Sin(half)), (float)(-34*Math.Cos(half))), Vector2.zero
                }, 17, new Vector2(48.23f,60.61f));
                Sweep(t, api, new[] { polygon }, 96, "angle=" + angle, changes, true, false);
            }
            var star = new Vector2[26];
            for (int i = 0; i < star.Length; i++)
            {
                double a = Math.PI*(.5+i/13.0), r = (i & 1) == 0 ? 37 : 37*.364;
                star[i] = new Vector2((float)(64.13+r*Math.Cos(a)), (float)(63.87+r*Math.Sin(a)));
            }
            Sweep(t, api, new[] { star }, 128, "13-ray star", changes, false, false);
            var detail = new[] {
                new[] { new Vector2(20,20),new Vector2(76,20),new Vector2(76,64),new Vector2(51,64),
                    new Vector2(51,64.75f),new Vector2(50.25f,64),new Vector2(20,64) },
                DistanceFieldAnalyticFixture.Rectangle(36,32,56,48),
                DistanceFieldAnalyticFixture.Rectangle(18,80,78,83),
                new[] { new Vector2(7.6f,8),new Vector2(8,7.6f),new Vector2(8.4f,8),new Vector2(8,8.4f) },
                new[] { new Vector2(63.6f,35),new Vector2(64,34.6f),new Vector2(64.4f,35),new Vector2(64,35.4f) }
            };
            Sweep(t, api, detail, 96, "thin bar, short notch, islands and holes", changes, false, true);
            t.Equal(42, api.Calls, "Seven rasters x three merge distances x Sharp/MTSDF");
            api.CheckTime();
        });
        // Facts are reported even if zero: welding may reject a complete batch.
        // Output differences are measurements, never a requested improvement.
        var report = JsonUtility.FromJson<TestResult>(result);
        report.message += "; " + changes[0].Describe(1) + "; " + changes[1].Describe(2);
        return report.ToJson();
    }

    static void Sweep(TestContext t, FieldApi api, Vector2[][] rings, int size, string name,
        Changes[] changes, bool commonTip, bool detail)
    {
        var source = DistanceFieldAnalyticFixture.Raster(size, size,
            (x,y) => DistanceFieldAnalyticFixture.Distance(rings,x,y,0), 128, 1);
        // Independent reference samples are built once and shared by all three
        // merge settings. Neither sample selection nor budgets use output alpha.
        var probes = new List<Probe>();
        for (int y = 2; y < size-3; y += 4)
        {
            api.CheckTime();
            for (int x = 2; x < size-3; x += 4)
            {
                probes.Add(Oracle(rings, x, y));
                probes.Add(Oracle(rings, x+.25, y+.75));
            }
        }
        float[] baseline = null;
        float4[] baselineMulti = null;
        for (int merge = 0; merge <= 2; merge++)
        {
            string label = name + "/merge=" + merge;
            var sharp = api.Scalar(source, size, merge);
            var multi = api.Multi(source, size, merge);
            int sampled = 0, inside = 0, far = 0;
            foreach (var p in probes)
            {
                double budget = p.uncertainty + merge*.5 + .04*Math.Abs(p.signed);
                if (Math.Abs(p.signed) <= 1.5+merge*.5) continue;
                double s = SampleScalar(sharp,size,p.x,p.y);
                float4 rgba = SampleMulti(multi,size,p.x,p.y);
                double m = Median(rgba), finite = Math.Abs(p.signed);
                t.True((s < 0) == (p.signed < 0) && (m < 0) == (p.signed < 0), label + " independent contour sign");
                t.True(Math.Abs(s) >= Math.Max(0,p.low-budget) && Math.Abs(m) >= Math.Max(0,p.low-budget),
                    label + " no unsupported long ray at " + p.x + "," + p.y + "; floor=" + (p.low-budget) + ", Sharp=" + s + ", RGB=" + m);
                t.True(Math.Abs(s) >= Math.Max(0,finite/Cap-budget) && Math.Abs(m) >= Math.Max(0,finite/Cap-budget), label + " amplification cap four");
                t.True(Math.Abs(s) <= Cap*(finite+budget) && Math.Abs(m) <= Cap*(finite+budget), label + " bounded finite upper envelope");
                t.Near(p.signed, rgba.w, budget+1, label + " finite alpha envelope");
                sampled++; if (p.signed < 0) inside++; if (p.signed > 12) far++;
            }
            t.True(sampled > 250 && inside >= 4 && far > 100, label + " non-vacuous interior, exterior and RGB subcell coverage");
            if (commonTip) CheckTip(t,rings[0],sharp,multi,size,merge,label);
            if (detail)
            {
                foreach (int i in new[] { 8*size+8, 81*size+48, 25*size+28 })
                    t.True(sharp[i] < 0 && Median(multi[i]) < 0 && multi[i].w < 0, label + " retains island, thin bar and large interior");
                foreach (int i in new[] { 40*size+46, 35*size+64, 72*size+48 })
                    t.True(sharp[i] > 0 && Median(multi[i]) > 0 && multi[i].w > 0, label + " retains large/tiny holes and inter-component gap");
            }
            if (merge == 0) { baseline = sharp; baselineMulti = multi; }
            else changes[merge-1].Add(baseline,baselineMulti,sharp,multi);
            api.CheckTime();
        }
    }

    // Conservative analytic envelope: only true input corners with >=2px
    // support on each side and amplification <=4 may lower finite distance.
    // This is deliberately independent of fitted/merged production geometry.
    static Probe Oracle(Vector2[][] rings, double x, double y)
    {
        double signed = DistanceFieldAnalyticFixture.Distance(rings,x,y,0), low = Math.Abs(signed), uncertainty = .85;
        foreach (var ring in rings) for (int i = 0; i < ring.Length; i++)
        {
            Vector2 v = ring[i], a = v-ring[(i+ring.Length-1)%ring.Length], b = ring[(i+1)%ring.Length]-v;
            double left = a.magnitude, right = b.magnitude;
            a.Normalize(); b.Normalize();
            double cosine = Math.Sqrt(Math.Max(.000001,(1+Vector2.Dot(a,b))*.5));
            double rx = x-v.x, ry = y-v.y;
            // Raster truncation can move acute tips farther than the normal
            // edge error. This fixed geometric uncertainty never depends on output.
            uncertainty = Math.Max(uncertainty,.85/cosine);
            if (left < Support || right < Support || 1/cosine > Cap) continue;
            if (rx*a.x+ry*a.y < 0 || rx*b.x+ry*b.y > 0) continue;
            if ((signed < 0) != (a.x*b.y-a.y*b.x < 0)) continue;
            double sign = signed < 0 ? -1 : 1;
            low = Math.Min(low,Math.Max(sign*(rx*a.y-ry*a.x),sign*(rx*b.y-ry*b.x)));
        }
        return new Probe { x=x,y=y,signed=signed,low=Math.Max(0,low),uncertainty=uncertainty };
    }

    static void CheckTip(TestContext t, Vector2[] ring, float[] sharp, float4[] multi, int size, int merge, string label)
    {
        Vector2 v = ring[2], a = (v-ring[1]).normalized, b = (ring[0]-v).normalized;
        Vector2 n1 = new Vector2(a.y,-a.x), n2 = new Vector2(b.y,-b.x), bisector = (n1+n2).normalized;
        t.True(1/Vector2.Dot(n1,bisector) <= Cap && (v-ring[1]).magnitude >= Support && (ring[0]-v).magnitude >= Support,
            label + " independently supported common angle");
        foreach (float along in new[] { 10f,16f })
        {
            Vector2 q = v+bisector*along;
            int x = Mathf.RoundToInt(q.x), y = Mathf.RoundToInt(q.y);
            t.True(x >= 0 && y >= 0 && x < size && y < size, label + " tip probe bounds");
            Vector2 delta = new Vector2(x,y)-v;
            double expected = Math.Max(Vector2.Dot(delta,n1),Vector2.Dot(delta,n2));
            double budget = .85+.04*(along+2)+merge*.5;
            t.Near(expected,sharp[y*size+x],budget,label + " supported Sharp tip");
            t.Near(expected,Median(multi[y*size+x]),budget,label + " supported RGB tip");
        }
    }

    sealed class FieldApi
    {
        readonly TestContext test;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly MethodInfo scalar, multi;
        public int Calls { get; private set; }
        public FieldApi(TestContext test)
        {
            this.test = test;
            var type = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility",true);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            scalar = type.GetMethod("ComputeArtisticDistance",flags,null,new[] {
                typeof(NativeArray<Color32>),typeof(NativeArray<float>),typeof(int),typeof(int),
                typeof(byte),typeof(int),typeof(DistanceMetric),typeof(float),typeof(float) },null);
            multi = type.GetMethod("ComputeMtsdfDistance",flags,null,new[] {
                typeof(NativeArray<Color32>),typeof(NativeArray<float4>),typeof(int),typeof(int),typeof(byte),typeof(int),typeof(float) },null);
            test.True(scalar != null && multi != null,"Package scalar and MTSDF merge entries exist");
        }
        public void CheckTime() => DistanceContourMergeTests.CheckTime(clock);
        void Begin(Color32[] source,int size,float merge)
        {
            CheckTime();
            test.True(size >= 2 && size <= 128 && source.Length == size*size,"Bounded raster dimensions");
            test.True(merge >= 0 && merge <= 2 && ++Calls <= 42,"Bounded merge and production call count");
        }
        public float[] Scalar(Color32[] source,int size,float merge)
        {
            Begin(source,size,merge);
            using var pixels = new NativeArray<Color32>(source,Allocator.TempJob);
            using var output = new NativeArray<float>(source.Length,Allocator.TempJob);
            var writable = output;
            for (int i=0;i<output.Length;i++) writable[i]=float.NaN;
            scalar.Invoke(null,new object[] { pixels,output,size,size,(byte)128,0,DistanceMetric.Sharp,64f,merge });
            var result = output.ToArray();
            int invalid=0;
            foreach (float value in result) if (!float.IsFinite(value)) invalid++;
            test.Equal(0,invalid,"Every scalar output is finite");
            Borrowed(source,pixels); CheckTime(); return result;
        }
        public float4[] Multi(Color32[] source,int size,float merge)
        {
            Begin(source,size,merge);
            using var pixels = new NativeArray<Color32>(source,Allocator.TempJob);
            using var output = new NativeArray<float4>(source.Length,Allocator.TempJob);
            var writable = output;
            for (int i=0;i<output.Length;i++) writable[i]=new float4(float.NaN);
            multi.Invoke(null,new object[] { pixels,output,size,size,(byte)128,0,merge });
            var result = output.ToArray();
            int invalid=0;
            foreach (float4 value in result) for (int c=0;c<4;c++) if (!float.IsFinite(value[c])) invalid++;
            test.Equal(0,invalid,"Every MTSDF component is finite");
            Borrowed(source,pixels); CheckTime(); return result;
        }
        void Borrowed(Color32[] source,NativeArray<Color32> pixels)
        {
            int changed=0;
            for (int i=0;i<source.Length;i++) if (!source[i].Equals(pixels[i])) changed++;
            test.Equal(0,changed,"Borrowed source RGBA remains unchanged");
        }
    }

    sealed class Changes
    {
        int sharpPixels, medianPixels, rgbaPixels, alphaPixels, comparisons;
        double sharpMaximum, medianMaximum, rgbaMaximum, alphaMaximum;
        public void Add(float[] before,float4[] beforeMulti,float[] after,float4[] afterMulti)
        {
            comparisons++;
            for (int i=0;i<before.Length;i++)
            {
                double s=Math.Abs(after[i]-before[i]), m=Math.Abs(Median(afterMulti[i])-Median(beforeMulti[i]));
                double a=Math.Abs(afterMulti[i].w-beforeMulti[i].w), rgba=0;
                for (int c=0;c<4;c++) rgba=Math.Max(rgba,Math.Abs(afterMulti[i][c]-beforeMulti[i][c]));
                if (s>0) sharpPixels++; if (m>0) medianPixels++; if (a>0) alphaPixels++; if (rgba>0) rgbaPixels++;
                sharpMaximum=Math.Max(sharpMaximum,s); medianMaximum=Math.Max(medianMaximum,m);
                alphaMaximum=Math.Max(alphaMaximum,a); rgbaMaximum=Math.Max(rgbaMaximum,rgba);
            }
        }
        public string Describe(int merge) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "merge={0}, completed comparisons={1}, changed pixels Sharp/RGB-median/RGBA/alpha={2}/{3}/{4}/{5}, max delta px={6:G6}/{7:G6}/{8:G6}/{9:G6}",
            merge,comparisons,sharpPixels,medianPixels,rgbaPixels,alphaPixels,sharpMaximum,medianMaximum,rgbaMaximum,alphaMaximum);
    }

    struct Probe { public double x,y,signed,low,uncertainty; }
    static void CheckTime(Stopwatch clock)
    {
        if (clock.ElapsedMilliseconds >= BudgetMilliseconds)
            throw new InvalidOperationException("Contour merge test body exceeded 30s, including startup and oracle work.");
    }
    static List<float2> Path(params float[] xy)
    {
        var result = new List<float2>();
        for (int i=0;i<xy.Length;i+=2) result.Add(new float2(xy[i],xy[i+1]));
        return result;
    }
    static List<float2> RotatePath(List<float2> path,int seam,bool reverse)
    {
        var result = new List<float2>();
        for (int i=0;i<path.Count;i++) result.Add(path[(i+seam)%path.Count]);
        if (reverse) result.Reverse();
        return result;
    }
    static void SameCycle(TestContext t,List<float2> expected,List<float2> actual,string label)
    {
        t.Equal(expected.Count,actual.Count,label+" count");
        int start=-1;
        for (int i=0;i<actual.Count;i++) if (math.distancesq(expected[0],actual[i])<1e-10f) { start=i; break; }
        t.True(start>=0,label+" contains expected midpoint");
        for (int i=0;i<expected.Count;i++)
            t.Near(0,math.distance(expected[i],actual[(start+i)%actual.Count]),.00001,label+" cyclic vertex "+i);
    }
    static double Area(List<float2> path)
    {
        double area=0;
        for (int i=0;i<path.Count;i++) { float2 a=path[i],b=path[(i+1)%path.Count]; area+=(double)a.x*b.y-(double)a.y*b.x; }
        return area*.5;
    }
    static bool Inside(List<float2> path,float2 p)
    {
        bool inside=false;
        for (int i=0;i<path.Count;i++)
        {
            float2 a=path[i],b=path[(i+1)%path.Count];
            if ((a.y>p.y)!=(b.y>p.y) && p.x<a.x+(p.y-(double)a.y)*(b.x-a.x)/(b.y-a.y)) inside=!inside;
        }
        return inside;
    }
    static double SampleScalar(float[] field,int size,double x,double y)
    {
        int ix=(int)x,iy=(int)y,i=iy*size+ix;
        double fx=x-ix,fy=y-iy;
        return (field[i]*(1-fx)+field[i+1]*fx)*(1-fy)+(field[i+size]*(1-fx)+field[i+size+1]*fx)*fy;
    }
    static float4 SampleMulti(float4[] field,int size,double x,double y)
    {
        int ix=(int)x,iy=(int)y,i=iy*size+ix;
        // Interpolate all RGB components before taking the nonlinear median.
        return math.lerp(math.lerp(field[i],field[i+1],(float)(x-ix)),
            math.lerp(field[i+size],field[i+size+1],(float)(x-ix)),(float)(y-iy));
    }
    static double Median(float4 f) => Math.Max(Math.Min(f.x,f.y),Math.Min(Math.Max(f.x,f.y),f.z));
}
