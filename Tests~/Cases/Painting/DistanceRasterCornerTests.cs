// Explicit support: Tests~/Framework/TestApi.cs and
// Tests~/Cases/Painting/DistanceFieldTests.cs (DistanceFieldAnalyticFixture only).
// Do not invoke the support case's Run. CPU buffers only; no assets or windows.
// Analytic polygons construct raster inputs/oracles, not a vector production path.
// This case does not distinguish genuine subpixel cuts from identical raster caps.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class DistanceRasterCornerTests
{
    const int BudgetMilliseconds = 30000, MaximumCalls = 112, Rays = 13;
    const double InnerRatio = .368, Spread = 1, NormalBudget = .6, DirectionTolerance = .04;
    const double FloatSlack = .0001, CurveBudget = .85, SharpMiterLimit = 16;
    static readonly byte[] Thresholds = { 77, 96, 128, 160, 192 };
    static readonly float[] Rotations = { 0, 17, 37 };
    static readonly Vector2[] Phases = {
        new Vector2(.13f, -.13f), new Vector2(.61f, -.37f), new Vector2(-.23f, .41f)
    };

    public static string Run() => TestContext.Run(
        "Independent acute thirteen-ray raster corners, resolvable cuts and smooth/detail guards", t =>
    {
        var api = new OwnedApi(t);
        CheckStars(t, api);
        CheckResolvedAcuteCuts(t, api);
        CheckSmoothRasters(t, api);
        CheckShortRawDetail(t, api);
        t.Equal(MaximumCalls, api.Calls, "30 star x 3, 12 cut builds, 8 curve builds, 2 short-contour calls");
        api.CheckTime();
    });

    public static string RunAngles() => TestContext.Run(
        "Independent raster wedge sweep from ten-degree tips through ordinary acute and right angles, all six modes", t =>
    {
        const int calls = 8 * 2 * 3 * 6 * 2;
        var api = new OwnedApi(t, calls);
        int probes = 0;
        foreach (double degrees in new[] { 10.0, 15, 20, 30, 45, 60, 75, 90 })
        for (int variant = 0; variant < 2; variant++)
        {
            double half = degrees * Math.PI / 360;
            var polygon = new[] { new Vector2((float)(-40*Math.Sin(half)), (float)(-40*Math.Cos(half))),
                new Vector2((float)(40*Math.Sin(half)), (float)(-40*Math.Cos(half))), Vector2.zero };
            polygon = DistanceFieldAnalyticFixture.Rotate(polygon, variant == 0 ? 0 : 37,
                new Vector2(64.23f, 64.61f));
            var rings = new[] { polygon };
            var input = DistanceFieldAnalyticFixture.Raster(128, 128,
                (x,y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0), 128, Spread);
            foreach (byte threshold in new byte[] { 96, 128, 160 })
            foreach (DistanceMetric mode in new[] { DistanceMetric.EuclideanAntialiased, DistanceMetric.Sharp,
                DistanceMetric.Bevel, DistanceMetric.Diamond, DistanceMetric.Square, DistanceMetric.MTSDF })
            {
                string label = $"Angle={degrees}/rotation={variant*37}/threshold={threshold}/{mode}";
                var geometry = api.Geometry(input, 128, threshold, mode, label);
                var field = api.Scalar(input, 128, threshold, mode, label);
                Tip tip = ExpectedTip(polygon, 2, threshold);
                CheckTip(t, geometry, tip, label);
                int local = 0;
                foreach (D2 direction in new[] { -1*tip.incoming, tip.outgoing })
                {
                    D2 normal = direction.x == -tip.incoming.x && direction.y == -tip.incoming.y ? tip.n1 : tip.n2;
                    double dual = mode == DistanceMetric.Diamond ? Math.Max(Math.Abs(normal.x),Math.Abs(normal.y))
                        : mode == DistanceMetric.Square ? Math.Abs(normal.x)+Math.Abs(normal.y) : 1;
                    foreach (double along in new[] { 12.0, 20, 28 })
                    foreach (double offset in new[] { -3.0, -1.5, 1.5, 3 })
                    {
                        D2 q = tip.original + along*direction + offset*normal;
                        int x = (int)Math.Round(q.x), y = (int)Math.Round(q.y);
                        q = new D2(x,y);
                        double plane = Dot(q-tip.original,normal);
                        // Skip probes whose nearest boundary is another side or
                        // endpoint. This checks supported sides, not join style.
                        double exact = DistanceFieldAnalyticFixture.Distance(rings,x,y,0);
                        if (Math.Abs(exact-plane) > .01 || Math.Abs(plane-tip.shift) < .7) continue;
                        t.True(x > 0 && y > 0 && x < 127 && y < 127, label + " interior probe domain");
                        double expected = (plane-tip.shift)/dual;
                        double budget = mode == DistanceMetric.EuclideanAntialiased ? 1.1 : CurveBudget/dual+FloatSlack;
                        t.Near(expected,field[y*128+x],budget,label+$" supported side at {x},{y}, expected={expected}");
                        local++; probes++;
                    }
                }
                t.True(local >= 12, label + " exercises both sides and finite inner/outer distances; probes="+local);
                if (mode == DistanceMetric.Sharp || mode == DistanceMetric.MTSDF)
                {
                    foreach (double distance in new[] { 1.5, 2.5 })
                    {
                        D2 q=tip.point+tip.bisector*(distance/tip.cosHalf);
                        int x=(int)Math.Round(q.x),y=(int)Math.Round(q.y);q=new D2(x,y);
                        // GuardedSharp retains finite ownership, but no longer
                        // rounds every tip whose amplification exceeds four.
                        double expected=Math.Max(Dot(q-tip.original,tip.n1)-tip.shift,Dot(q-tip.original,tip.n2)-tip.shift);
                        double budget=NormalBudget+DirectionTolerance*((q-tip.point).Length+tip.vertexBudget)+FloatSlack;
                        t.True(x>0&&y>0&&x<127&&y<127,label+" tip probe domain");
                        if (degrees >= 30)
                            t.Near(expected,field[y*128+x],budget,label+" independent sharp tip fan/"+distance);
                        else
                        {
                            // Acute corners used to be forced finite by the fixed
                            // angular cap. They may now be certified sharp or keep
                            // the finite fallback; lifting that cap does not certify
                            // every raster fit. Keep the same independent error budget.
                            double actual = field[y*128+x], finite = (q-tip.point).Length;
                            double finiteBudget = budget + tip.vertexBudget - NormalBudget;
                            t.True(actual >= expected-budget && actual <= finite+finiteBudget,
                                label+" acute sharp/finite envelope/"+distance+"; allowed="+expected+".."+finite+", slack="+budget+"/"+finiteBudget+", got="+actual);
                        }
                    }
                }
            }
        }
        t.Equal(calls,api.Calls,"Bounded angle matrix production calls");
        t.True(probes > 5000,"Angle matrix checks substantial independent side coverage; probes="+probes);
        api.CheckTime();
    });

    public static string RunCaptured() => TestContext.Run(
        "Authenticated original raster inputs, independent sharp-ray envelopes at moderate thresholds", t =>
    {
        var api = new OwnedApi(t,52);
        var names = new[] { "star-13-inner-0368.png", "star-13-inner-0413.png", "star-13-inner-0364.png", "star-13-inner-0364-phase2.png", "star-13-inner-0364-phase3-known-failure.png" };
        var hashes = new[] { "CB725C3A9F28D6B9511D98BFF2861730E4A899E8899F3347B779B51270F3045A",
            "745E4920F392354A6713CD30E326AD759128726C899E99D799C4D0FE727F5797",
            "EE0BDE8C29364AED0D13FEB5A545F8032F9A1B188130AA5B02C7C7C53BE131AD",
            "F52290C1515C7688EBF704D1FAB9DCA2DB8044D949673AFEEE34A2EB9A499C95",
            "98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC" };
        var centers = new[] { new D2(248.203460693,255.948944092), new D2(246.610931396,251.967605591),new D2(259.329071045,250.861679077),new D2(253.799438477,254.179458618),new D2(245.040496826,251.790664673) };
        var ratios = new[] { .368, .413, .364, .364, .364 };
        const double radius = 89.7093963623;
        for (int source=0;source<names.Length;source++)
        {
            byte[] bytes=File.ReadAllBytes("Packages/com.dcfapixels.whimtex/Tests~/Fixtures/DistanceRaster/"+names[source]);
            using var sha=SHA256.Create();
            t.Equal(hashes[source],BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""),"Original source hash/"+names[source]);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                t.True(ImageConversion.LoadImage(texture,bytes,false),"Decode authenticated source");
                t.Equal(512,texture.width,"Source width");t.Equal(512,texture.height,"Source height");
                var input=texture.GetPixels32();
                var polygon=new D2[2*Rays];
                for(int k=0;k<polygon.Length;k++)
                {
                    double angle=Math.PI*(.5+k/(double)Rays),r=k%2==0?radius:radius*ratios[source];
                    polygon[k]=centers[source]+new D2(Math.Cos(angle),Math.Sin(angle))*r;
                }
                double delta=Math.PI/Rays, sine=ratios[source]*Math.Sin(delta), cosine=1-ratios[source]*Math.Cos(delta);
                double cosHalf=sine/Math.Sqrt(sine*sine+cosine*cosine);
                foreach (byte threshold in Thresholds)
                foreach (DistanceMetric mode in new[] { DistanceMetric.Sharp,DistanceMetric.MTSDF })
                {
                    var field=api.Scalar(input,512,threshold,mode,names[source]+"/"+threshold+"/"+mode,64);
                    if(source==3&&threshold==128)
                    {
                        var geometry=api.Geometry(input,512,threshold,mode,"Retained tiny hole and supported concave join");
                        D2 v=polygon[3],incoming=(v-polygon[2]).Unit,outgoing=(polygon[4]-v).Unit;
                        // Independent threshold crossings of the retained 126-alpha
                        // hole, not fitted coordinates or output-derived tolerances.
                        var holes=new[]{new D2(232-2.0/(162-126),279),new D2(232+2.0/(255-126),279),
                            new D2(232,279+2.0/(194-126)),new D2(232,279-2.0/(255-126))};
                        bool CrossesHole(D2 direction)
                        {
                            D2 normal=new D2(direction.y,-direction.x);
                            double min=double.PositiveInfinity,max=double.NegativeInfinity;
                            foreach(D2 h in holes){double side=Dot(h-v,normal);min=Math.Min(min,side);max=Math.Max(max,side);}
                            return min < -FloatSlack && max > FloatSlack;
                        }
                        bool obstructed=CrossesHole(incoming)||CrossesHole(outgoing);
                        Feature left=default,right=default;double dl=double.PositiveInfinity,dr=double.PositiveInfinity;
                        foreach(Feature f in geometry)
                        {
                            // Adaptive fitting inserts local incident segments;
                            // choose a connected pair, not two independently nearest
                            // endpoints which may belong to the retained raster flanks.
                            if(f.vertex!=0||(f.b-f.a).Length<=FloatSlack)continue;
                            D2 direction=(f.b-f.a).Unit;
                            if((direction-incoming).Length>=DirectionTolerance||(f.b-v).Length>=dl)continue;
                            foreach(Feature g in geometry)
                            {
                                if(g.vertex!=0||(g.b-g.a).Length<=FloatSlack||(f.b-g.a).Length>FloatSlack)continue;
                                if(((g.b-g.a).Unit-outgoing).Length>=DirectionTolerance)continue;
                                left=f;right=g;dl=(f.b-v).Length;dr=(g.a-v).Length;
                            }
                        }
                        bool shared=Finite(dl)&&Finite(dr);
                        if(!shared&&obstructed)
                        {
                            // The adaptive contract permits a finite cap when the
                            // ideal walls would cut a real hole. It no longer promises
                            // the former constrained-wall reconstruction in this case.
                            foreach(Feature f in geometry)
                            {
                                if(f.vertex!=0||(f.b-f.a).Length<=FloatSlack)continue;
                                D2 direction=(f.b-f.a).Unit;
                                if((direction-incoming).Length<DirectionTolerance&&(f.b-v).Length<dl){left=f;dl=(f.b-v).Length;}
                                if((direction-outgoing).Length<DirectionTolerance&&(f.a-v).Length<dr){right=f;dr=(f.a-v).Length;}
                            }
                        }
                        t.True(Finite(dl)&&Finite(dr),mode+" concave corner has independently supported sides");
                        t.True(shared||obstructed,mode+" shared concave join or independently obstructing hole");
                        double joinCosine=Math.Sqrt((1+Dot(incoming,outgoing))*.5);
                        t.Near(0,shared?(left.b-v).Length:NearestGeometryDistance(v,geometry),NormalBudget/joinCosine+FloatSlack,
                            mode+" join or topology-preserving cap stays in its normal uncertainty");
                        // Exact source-crossing interpolation, not fitted/golden
                        // coordinates. The 126-alpha pixel remains a real tiny hole.
                        foreach(D2 h in holes)t.Near(0,NearestGeometryDistance(h,geometry),.00005,mode+" does not erase the original subpixel hole");
                        t.True(input[279*512+232].a==126&&field[279*512+232]>0,mode+" keeps the original hole sample exterior");
                    }
                    foreach(double offset in new[] {30.0,60})
                    {
                        for(int ray=0;ray<Rays;ray++)
                        {
                            double angle=Math.PI*.5+2*Math.PI*ray/Rays;
                            D2 q=centers[source]+new D2(Math.Cos(angle),Math.Sin(angle))*(radius+offset);
                            int x=(int)Math.Round(q.x),y=(int)Math.Round(q.y);
                            t.True(x>0&&y>0&&x<511&&y<511,"Captured ray probe domain");
                            // The adaptive contract permits a supported sharp
                            // join or finite fallback, including at acute tips.
                            // Both endpoints of this envelope are independent
                            // of production fitting and measured output.
                            D2 probe=new D2(x,y);
                            double actual=field[y*512+x],low=AnalyticSharpOutside(probe,polygon),high=AnalyticFiniteOutside(probe,polygon);
                            // Original raster cap/AA uncertainty, integer probe
                            // rounding and propagation of the declared .04 flank
                            // direction bound. No production value enters this
                            // independent geometry envelope or its tolerance.
                            double budget=NormalBudget/cosHalf+Math.Sqrt(.5)+DirectionTolerance*(offset+8)+Math.Sqrt(.5);
                            t.True(actual>=low-budget&&actual<=high+budget,
                                $"{names[source]}/{mode}/threshold={threshold}/ray={ray}/offset={offset}; allowed={low}..{high}, slack={budget}, got={actual}");
                        }
                        // Between-ray probes catch unrelated pseudo/miter rays,
                        // which a symmetry check on intended tips alone misses.
                        for(int phase=0;phase<180;phase++)
                        {
                            double angle=2*Math.PI*phase/180;
                            D2 q=centers[source]+new D2(Math.Cos(angle),Math.Sin(angle))*(radius+offset);
                            int x=(int)Math.Round(q.x),y=(int)Math.Round(q.y);q=new D2(x,y);
                            double low=AnalyticSharpOutside(q,polygon),high=AnalyticFiniteOutside(q,polygon);
                            double budget=NormalBudget/cosHalf+2*Math.Sqrt(.5)+DirectionTolerance*(offset+32);
                            double actual=field[y*512+x];
                            t.True(actual>=low-budget&&actual<=high+budget,
                                $"{names[source]}/{mode}/threshold={threshold}/angular={phase}/offset={offset}; allowed={low}..{high}, slack={budget}, got={actual}");
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        t.Equal(52,api.Calls,"Five sources x five thresholds x two modes, plus two concave geometry checks");api.CheckTime();
    });

    static double AnalyticFiniteOutside(D2 point,D2[] polygon)
    {
        double best=double.PositiveInfinity;
        for(int i=0;i<polygon.Length;i++)best=Math.Min(best,SegmentDistance(point,polygon[i],polygon[(i+1)%polygon.Length]));
        return best;
    }

    static double AnalyticSharpOutside(D2 point,D2[] polygon)
    {
        // Pure double-precision ideal polygon, never production geometry or an
        // output golden. All probes are exterior and genuine miter ratios < 16.
        double best=double.PositiveInfinity;
        for(int i=0;i<polygon.Length;i++)
        {
            D2 a=polygon[i],b=polygon[(i+1)%polygon.Length],d=b-a,r=point-a;
            double u=Dot(r,d)/Dot(d,d);
            if(u>=0&&u<=1) best=Math.Min(best,(r-d*u).Length);
            D2 incoming=(a-polygon[(i+polygon.Length-1)%polygon.Length]).Unit,outgoing=d.Unit;
            if(incoming.x*outgoing.y-incoming.y*outgoing.x<=0||Dot(r,incoming)<0||Dot(r,outgoing)>0) continue;
            D2 n1=new D2(incoming.y,-incoming.x),n2=new D2(outgoing.y,-outgoing.x);
            best=Math.Min(best,Math.Max(Dot(r,n1),Dot(r,n2)));
        }
        return best;
    }

    static void CheckStars(TestContext t, OwnedApi api)
    {
        foreach (float radius in new[] { 40f, 89f })
        for (int variant = 0; variant < Rotations.Length; variant++)
        {
            api.CheckTime();
            int size = radius < 50 ? 128 : 256;
            Vector2 center = new Vector2(size * .5f, size * .5f) + Phases[variant];
            var polygon = DistanceFieldAnalyticFixture.Rotate(Star(radius), Rotations[variant], center);
            var rings = new[] { polygon };
            var input = DistanceFieldAnalyticFixture.Raster(size, size,
                (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0), 128, Spread);
            var original = (Color32[])input.Clone();
            foreach (byte threshold in Thresholds)
            {
                string label = $"13 rays/r={radius}/rotation={Rotations[variant]}/phase={Phases[variant]}/threshold={threshold}";
                // Corner reconstruction is shared. MTSDF exposes finite edges without
                // Sharp's additional point-join features obscuring vertex selection.
                var geometry = api.Geometry(input, size, threshold, DistanceMetric.MTSDF, label);
                var sharp = api.Sharp(input, size, threshold, label);
                var multi = api.Multi(input, size, threshold, label);
                var tips = new Tip[Rays];
                for (int ray = 0; ray < Rays; ray++)
                {
                    Tip tip = tips[ray] = ExpectedTip(polygon, 2 * ray, threshold);
                    string tipLabel = label + "/tip=" + ray;
                    t.True(tip.cosHalf > 0 && 1 / tip.cosHalf < SharpMiterLimit,
                        tipLabel + " analytic acute join is below the explicit Sharp miter limit");
                    CheckTip(t, geometry, tip, tipLabel);
                }
                CheckRayFields(t, input, sharp, multi, size, threshold, tips, label);
                CheckSource(t, original, input, label + " managed raster remains immutable");
            }
        }
    }

    static Vector2[] Star(float radius)
    {
        var points = new Vector2[2 * Rays];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = Math.PI * (.5 + i / (double)Rays);
            double r = i % 2 == 0 ? radius : radius * InnerRatio;
            points[i] = new Vector2((float)(r * Math.Cos(angle)), (float)(r * Math.Sin(angle)));
        }
        return points;
    }

    struct Tip
    {
        public D2 original, point, incoming, outgoing, n1, n2, bisector;
        public double shift, cosHalf, vertexBudget;
    }

    static Tip ExpectedTip(Vector2[] polygon, int index, byte threshold)
    {
        int n = polygon.Length;
        D2 point = new D2(polygon[index]);
        D2 incoming = (point - new D2(polygon[(index + n - 1) % n])).Unit;
        D2 outgoing = (new D2(polygon[(index + 1) % n]) - point).Unit;
        D2 n1 = new D2(incoming.y, -incoming.x), n2 = new D2(outgoing.y, -outgoing.x);
        D2 bisector = (n1 + n2).Unit;
        double cosHalf = Dot(n1, bisector), shift = (128 - threshold) * Spread / 127;
        // Intersection of two independently known threshold-shifted support lines.
        // No recovered point, production fit, or rendered field enters this oracle.
        return new Tip { original = point, point = point + bisector * (shift / cosHalf),
            incoming = incoming, outgoing = outgoing, n1 = n1, n2 = n2,
            bisector = bisector, shift = shift, cosHalf = cosHalf,
            vertexBudget = (NormalBudget + FloatSlack) / cosHalf };
    }

    static void CheckTip(TestContext t, Feature[] geometry, Tip tip, string label)
    {
        Feature Flank(D2 direction, string name, bool isIncoming)
        {
            // Adaptive patches split a wall into short edges and tiny connectors.
            // Observe the same eight-pixel support span geometrically instead of
            // requiring that span to be stored as one uninterrupted straight edge.
            (D2 point, double error) Observe(double along)
            {
                D2 sample = tip.point + direction * (isIncoming ? -along : along);
                D2 point = default; double nearest = double.PositiveInfinity;
                foreach (Feature feature in geometry)
                {
                    if (feature.vertex != 0) continue;
                    D2 delta = feature.b-feature.a;
                    double length = Dot(delta,delta);
                    if (length <= FloatSlack*FloatSlack) continue;
                    D2 projected = feature.a + delta * Math.Max(0,Math.Min(1,Dot(sample-feature.a,delta)/length));
                    double error = (sample-projected).Length;
                    if (error < nearest) { nearest = error; point = projected; }
                }
                return (point,nearest);
            }
            double nearDistance = 2 + tip.vertexBudget, farDistance = nearDistance + 8;
            var near = Observe(nearDistance); var far = Observe(farDistance);
            t.True(near.error <= NormalBudget + DirectionTolerance*nearDistance + FloatSlack &&
                far.error <= NormalBudget + DirectionTolerance*farDistance + FloatSlack,
                label + " has the local supported " + name + " flank; endpoint errors=" + near.error + ", " + far.error);
            return isIncoming ? new Feature { a=far.point,b=near.point } : new Feature { a=near.point,b=far.point };
        }
        // Region union preserves genuine AA-tip islands and can split either
        // flank into short fragments. Observe its supporting lines over a fixed
        // span, not a random nearest vertex. Their intersection is independently
        // checked; full-field probes below catch a truncated/incorrect fan.
        Feature incoming = Flank(tip.incoming, "incoming", true), outgoing = Flank(tip.outgoing, "outgoing", false);
        D2 a = (incoming.b-incoming.a).Unit, b = (outgoing.b-outgoing.a).Unit;
        double cross = a.x*b.y-a.y*b.x;
        t.True(Math.Abs(cross) > .05, label + " supported sides have a finite, conditioned intersection");
        D2 gap = outgoing.a-incoming.a;
        D2 point = incoming.a + a*((gap.x*b.y-gap.y*b.x)/cross), delta = point-tip.point;
        t.Near(0, Dot(delta, tip.n1), NormalBudget + FloatSlack,
            label + " incoming supporting-line normal uncertainty; point=" + point + ", expected=" + tip.point);
        t.Near(0, Dot(delta, tip.n2), NormalBudget + FloatSlack,
            label + " outgoing supporting-line normal uncertainty");
        t.Near(0, delta.Length, tip.vertexBudget, label + " angle-aware vertex uncertainty, not a fixed 2px bound");
        // .04 in unit-direction chord distance is about 2.3 degrees, tighter than
        // the older five-ray .07 gate; otherwise an acute ray can change character.
        t.Near(0, (a - tip.incoming).Length, DirectionTolerance,
            label + " incoming edge is the supported flank, not a cap");
        t.Near(0, (b - tip.outgoing).Length, DirectionTolerance,
            label + " outgoing edge is the supported flank, not a cap");
    }

    static void CheckRayFields(TestContext t, Color32[] input, float[] sharp, float4[] multi,
        int size, byte threshold, Tip[] tips, string label)
    {
        foreach (double idealDistance in new[] { 1.5, 2.5 })
        {
            double minSharp = double.PositiveInfinity, maxSharp = double.NegativeInfinity;
            double minMulti = double.PositiveInfinity, maxMulti = double.NegativeInfinity, maxBudget = 0;
            for (int ray = 0; ray < tips.Length; ray++)
            {
                Tip tip = tips[ray];
                D2 continuous = tip.point + tip.bisector * (idealDistance / tip.cosHalf);
                int x = (int)Math.Round(continuous.x), y = (int)Math.Round(continuous.y);
                string probeLabel = label + $"/ray={ray}/outer-distance={idealDistance}/pixel={x},{y}";
                t.True(x > 0 && y > 0 && x + 1 < size && y + 1 < size,
                    probeLabel + " independent probe is inside the raster");
                D2 probe = new D2(x, y), r = probe - tip.point;
                t.True(Dot(r, tip.incoming) >= 0 && Dot(r, tip.outgoing) <= 0,
                    probeLabel + " probe belongs to the analytic acute corner fan");
                int index = y * size + x;
                t.True(input[index].a <= threshold, probeLabel + " probe is exterior in source coverage");
                double expected = Math.Max(Dot(probe - tip.original, tip.n1) - tip.shift,
                    Dot(probe - tip.original, tip.n2) - tip.shift);
                t.True(expected > 0, probeLabel + " independently shifted support planes are exterior");
                // Propagate the asserted normal vertex and angular errors to this
                // particular integer probe. Rounding is already in the oracle.
                double budget = NormalBudget + DirectionTolerance * (r.Length + tip.vertexBudget) + FloatSlack;
                double scalar = Median(multi[index]);
                bool requiresSharp = tip.cosHalf >= .25;
                if (requiresSharp)
                {
                    t.Near(expected, sharp[index], budget, probeLabel + " Sharp finite fan support distance");
                    t.Near(expected, scalar, budget, probeLabel + " MTSDF median finite fan support distance");
                }
                else
                {
                    // The former angle-cap domain now admits either a supported
                    // sharp fit or finite fallback, not mandatory extrapolation.
                    // Finite distance is sensitive to the full vertex displacement,
                    // unlike distance to the supporting planes. Retain the original
                    // angle-aware vertex budget for this fallback side of the bound.
                    double finiteBudget = budget + tip.vertexBudget - NormalBudget;
                    t.True(sharp[index] >= expected-budget && sharp[index] <= r.Length+finiteBudget,
                        probeLabel + " Sharp acute sharp/finite envelope");
                    t.True(scalar >= expected-budget && scalar <= r.Length+finiteBudget,
                        probeLabel + " MTSDF acute sharp/finite envelope");
                }
                double a = sharp[index] - expected, b = scalar - expected;
                if (requiresSharp)
                {
                    minSharp = Math.Min(minSharp, a); maxSharp = Math.Max(maxSharp, a);
                    minMulti = Math.Min(minMulti, b); maxMulti = Math.Max(maxMulti, b);
                }
                maxBudget = Math.Max(maxBudget, budget);
            }
            // Equal analytic rays need not hit identical pixel phases. Compare
            // residuals after correcting each ray's independent integer-plane oracle.
            if (Finite(minSharp))
            {
                t.Near(0, maxSharp - minSharp, 2 * maxBudget, label + "/Sharp corrected equal-ray symmetry/" + idealDistance);
                t.Near(0, maxMulti - minMulti, 2 * maxBudget, label + "/MTSDF corrected equal-ray symmetry/" + idealDistance);
            }
        }
    }

    static void CheckResolvedAcuteCuts(TestContext t, OwnedApi api)
    {
        // 30-degree tip, a 7px-deep cut with a 3.75px-wide actual third edge.
        // Its missing apex is within an 8px extrapolation ceiling, but the facet's
        // transverse evidence exceeds .6px. This is not a subpixel-cut fixture.
        double slope = Math.Tan(Math.PI / 12), depth = 7, apexY = 20, baseY = -30;
        float baseX = (float)((apexY - baseY) * slope), cutX = (float)(depth * slope);
        var triangle = new[] { new Vector2(-baseX, (float)baseY), new Vector2(baseX, (float)baseY), new Vector2(0, (float)apexY) };
        var cut = new[] { triangle[0], triangle[1], new Vector2(cutX, (float)(apexY - depth)), new Vector2(-cutX, (float)(apexY - depth)) };
        t.True(2 * cutX > 3.5, "Acute cut has a separately resolvable third edge wider than three pixels");
        foreach (float angle in new[] { 0f, 27f })
        {
            Vector2 center = new Vector2(64.23f, 63.61f);
            var polygon = DistanceFieldAnalyticFixture.Rotate(cut, angle, center);
            var uncut = DistanceFieldAnalyticFixture.Rotate(triangle, angle, center);
            var rings = new[] { polygon };
            var input = DistanceFieldAnalyticFixture.Raster(128, 128,
                (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0), 128, Spread);
            D2 capDirection = (new D2(polygon[3]) - new D2(polygon[2])).Unit;
            D2 capNormal = new D2(capDirection.y, -capDirection.x);
            foreach (byte threshold in new byte[] { 96, 128, 160 })
            foreach (DistanceMetric mode in new[] { DistanceMetric.Sharp, DistanceMetric.MTSDF })
            {
                string label = $"Resolvable acute cut/{mode}/rotation={angle}/threshold={threshold}";
                var geometry = api.Geometry(input, 128, threshold, mode, label);
                Tip absent = ExpectedTip(uncut, 2, threshold);
                D2 middle = (new D2(polygon[2]) + new D2(polygon[3])) * .5 + capNormal * absent.shift;
                Feature nearest = default;
                double capError = double.PositiveInfinity;
                foreach (Feature feature in geometry)
                {
                    if (feature.vertex != 0) continue;
                    double distance = SegmentDistance(middle, feature.a, feature.b);
                    if (distance < capError) { capError = distance; nearest = feature; }
                }
                t.Near(0, capError, CurveBudget, label + " actual threshold-shifted cut has a nearby finite edge");
                t.True(Math.Abs(Dot((nearest.b - nearest.a).Unit, capDirection)) >= .98,
                    label + " preserved third edge follows the cut direction");
                double cutDistance = Dot(absent.point - middle, capNormal);
                t.True(cutDistance > 6, label + " the deliberately absent acute apex is well resolved");
                t.True(NearestGeometryDistance(absent.point, geometry) >= cutDistance - CurveBudget,
                    label + " reconstruction does not reintroduce the deliberately absent apex");
            }
        }
    }

    static void CheckSmoothRasters(TestContext t, OwnedApi api)
    {
        foreach (double radius in new[] { 16.0, 26.0 })
        {
            var input = DistanceFieldAnalyticFixture.Raster(64, 64,
                (x, y) => Math.Sqrt((x - 32.23) * (x - 32.23) + (y - 31.61) * (y - 31.61)) - radius, 128, Spread);
            CheckCurve(t, api, input, 64, 128, "Smooth raster circle/r=" + radius);
        }
        foreach (float angle in new[] { 0f, 31f })
        {
            // 17:1 genuinely thin smooth shape, represented through raster input.
            // 192 independent chords have <.004px ellipse sag; no production path
            // receives these chords. Assertions use the raster isocontour itself.
            var ellipse = new Vector2[192];
            for (int i = 0; i < ellipse.Length; i++)
            {
                double a = 2 * Math.PI * i / ellipse.Length;
                ellipse[i] = new Vector2((float)(26 * Math.Cos(a)), (float)(1.5 * Math.Sin(a)));
            }
            ellipse = DistanceFieldAnalyticFixture.Rotate(ellipse, angle, new Vector2(32.23f, 31.61f));
            var rings = new[] { ellipse };
            var input = DistanceFieldAnalyticFixture.Raster(64, 64,
                (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0), 128, Spread);
            foreach (byte threshold in new byte[] { 96, 128, 160 })
                CheckCurve(t, api, input, 64, threshold, $"Thin smooth raster ellipse/rotation={angle}/threshold={threshold}");
        }
    }

    static void CheckCurve(TestContext t, OwnedApi api, Color32[] input, int size, byte threshold, string label)
    {
        var raw = RasterSegments(input, size, threshold);
        var geometry = api.Geometry(input, size, threshold, DistanceMetric.MTSDF, label);
        t.True(raw.Count > 10 && raw.Count < 1024, label + " bounded nonempty independently extracted isocontour");
        double forward = 0, backward = 0;
        foreach (Feature feature in geometry)
        {
            forward = Math.Max(forward, NearestRawDistance(feature.a, raw));
            forward = Math.Max(forward, NearestRawDistance(feature.b, raw));
        }
        foreach (Segment segment in raw)
        {
            backward = Math.Max(backward, NearestGeometryDistance(segment.a, geometry));
            backward = Math.Max(backward, NearestGeometryDistance(segment.b, geometry));
        }
        // .6 corner-fit uncertainty + .25 initial RDP budget, not an 8px tip
        // allowance. Check both added spikes and deleted smooth/thin details.
        t.Near(0, forward, CurveBudget + FloatSlack, label + " has no extra sharp endpoint beyond the raw raster contour");
        t.Near(0, backward, CurveBudget + FloatSlack, label + " retains raw smooth/thin contour detail");
    }

    static void CheckShortRawDetail(TestContext t, OwnedApi api)
    {
        // Unit guard on the extracted-contour stage, not a vector source route.
        // A .75px notch must survive the documented short-perimeter early return.
        var points = new List<float2> { new float2(0, 0), new float2(6, 0), new float2(6, 4),
            new float2(4, 4), new float2(4, 3.25f), new float2(3.25f, 3.25f), new float2(3.25f, 4), new float2(0, 4) };
        foreach (bool reverse in new[] { false, true })
        {
            var input = new List<float2>(points);
            if (reverse) input.Reverse();
            var original = input.ToArray();
            var result = api.Recover(input);
            t.True(ReferenceEquals(input, result), "Short contour keeps its original notch without fitting/resampling; reverse=" + reverse);
            t.Equal(original.Length, result.Count, "Short contour vertex count is unchanged");
            for (int i = 0; i < original.Length; i++)
                t.True(math.all(math.asuint(original[i]) == math.asuint(input[i])) &&
                    math.all(math.asuint(original[i]) == math.asuint(result[i])), "Short contour preserves borrowed vertex bits at " + i);
        }
    }

    struct Segment { public D2 a, b; public Segment(D2 a, D2 b) { this.a = a; this.b = b; } }

    static List<Segment> RasterSegments(Color32[] input, int size, byte threshold)
    {
        // Independent cell-level interpolation of the known byte raster. A bag of
        // finite segments is sufficient for distance envelopes; no production
        // extraction, welding, simplification, or fitted vertices are consulted.
        var result = new List<Segment>();
        var crossings = new D2[4];
        for (int y = 0; y + 1 < size; y++) for (int x = 0; x + 1 < size; x++)
        {
            double a = input[y * size + x].a - threshold, b = input[y * size + x + 1].a - threshold;
            double c = input[(y + 1) * size + x + 1].a - threshold, d = input[(y + 1) * size + x].a - threshold;
            int n = 0;
            if ((a > 0) != (b > 0)) crossings[n++] = new D2(x + a / (a - b), y);
            if ((b > 0) != (c > 0)) crossings[n++] = new D2(x + 1, y + b / (b - c));
            if ((c > 0) != (d > 0)) crossings[n++] = new D2(x + d / (d - c), y + 1);
            if ((d > 0) != (a > 0)) crossings[n++] = new D2(x, y + a / (a - d));
            if (n == 2) result.Add(new Segment(crossings[0], crossings[1]));
            else if (n == 4)
            {
                if (a * c - b * d >= 0)
                { result.Add(new Segment(crossings[0], crossings[1])); result.Add(new Segment(crossings[2], crossings[3])); }
                else
                { result.Add(new Segment(crossings[0], crossings[3])); result.Add(new Segment(crossings[1], crossings[2])); }
            }
        }
        return result;
    }

    static double NearestRawDistance(D2 point, List<Segment> segments)
    {
        double best = double.PositiveInfinity;
        foreach (Segment segment in segments) best = Math.Min(best, SegmentDistance(point, segment.a, segment.b));
        return best;
    }

    static double NearestGeometryDistance(D2 point, Feature[] geometry)
    {
        double best = double.PositiveInfinity;
        foreach (Feature feature in geometry)
            if (feature.vertex == 0) best = Math.Min(best, SegmentDistance(point, feature.a, feature.b));
        return best;
    }

    static double SegmentDistance(D2 point, D2 a, D2 b)
    {
        D2 d = b - a;
        double length = Dot(d, d), u = length > 0 ? Math.Max(0, Math.Min(1, Dot(point - a, d) / length)) : 0;
        return (point - a - d * u).Length;
    }

    struct Feature { public D2 a, b, previous; public int vertex; }

    sealed class OwnedApi
    {
        readonly TestContext test;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly MethodInfo build, scalar, multi, recover;
        readonly int maximumCalls;
        public int Calls { get; private set; }
        public OwnedApi(TestContext test, int maximumCalls = MaximumCalls)
        {
            this.test = test;
            this.maximumCalls = maximumCalls;
            var assembly = typeof(SDFLayerBehaviour).Assembly;
            var artistic = assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
            var utility = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility", true);
            const BindingFlags entry = BindingFlags.Public | BindingFlags.Static;
            build = artistic.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(NativeArray<Color32>), typeof(int), typeof(int), typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float), typeof(SharpDistanceMode) }, null);
            recover = artistic.GetMethod("RecoverCorners", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(List<float2>) }, null);
            scalar = utility.GetMethod("ComputeArtisticDistance", entry, null,
                new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int),
                    typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float), typeof(float), typeof(SharpDistanceMode) }, null);
            multi = utility.GetMethod("ComputeMtsdfDistance", entry, null,
                new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float4>), typeof(int), typeof(int), typeof(byte), typeof(int), typeof(float), typeof(SharpDistanceMode) }, null);
            test.True(build != null && scalar != null && multi != null && recover != null,
                "Required package-owned entries exist; no Unity internal reflection");
        }

        public void CheckTime() => test.True(clock.ElapsedMilliseconds < BudgetMilliseconds,
            "Entire body, raster generation and job startup included, stays below 30s; elapsed=" + clock.ElapsedMilliseconds + "ms");
        void Begin() { CheckTime(); test.True(++Calls <= maximumCalls, "Bounded production invocation count; calls=" + Calls); }

        public Feature[] Geometry(Color32[] input, int size, byte threshold, DistanceMetric metric, string label)
        {
            Begin();
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var geometry = (IDisposable)build.Invoke(null, new object[] { pixels, size, size, threshold, 0, metric, 0f, SharpDistanceMode.GuardedSharp });
            // Private members belong to this package. NativeArray is read only
            // through its public Length/Item API, never reflected Unity internals.
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
            object array = geometry.GetType().GetField("features", fields).GetValue(geometry);
            Type type = array.GetType(), featureType = type.GetGenericArguments()[0];
            var item = type.GetProperty("Item", fields);
            var a = featureType.GetField("a", fields); var b = featureType.GetField("b", fields);
            var previous = featureType.GetField("previousDirection", fields); var vertex = featureType.GetField("vertex", fields);
            var result = new Feature[(int)type.GetProperty("Length", fields).GetValue(array)];
            test.True(result.Length > 0 && result.Length < 4096, label + " bounded nonempty feature buffer");
            int invalid = 0;
            for (int i = 0; i < result.Length; i++)
            {
                object value = item.GetValue(array, new object[] { i });
                result[i] = new Feature { a = new D2((float2)a.GetValue(value)), b = new D2((float2)b.GetValue(value)),
                    previous = new D2((float2)previous.GetValue(value)), vertex = (int)vertex.GetValue(value) };
                if (!result[i].a.IsFinite || !result[i].b.IsFinite || !result[i].previous.IsFinite) invalid++;
            }
            test.Equal(0, invalid, label + " finite feature geometry");
            CheckBorrowed(input, pixels, label); CheckTime();
            return result;
        }

        public float[] Sharp(Color32[] input, int size, byte threshold, string label)
            => Scalar(input,size,threshold,DistanceMetric.Sharp,label);

        public float[] Scalar(Color32[] input, int size, byte threshold, DistanceMetric mode, string label, float miterLimit = (float)SharpMiterLimit)
        {
            Begin();
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var output = new NativeArray<float>(input.Length, Allocator.TempJob);
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = float.NaN;
            scalar.Invoke(null, new object[] { pixels, output, size, size, threshold, 0, mode, miterLimit, 0f, SharpDistanceMode.GuardedSharp });
            var result = output.ToArray();
            int invalid = 0;
            for (int i = 0; i < result.Length; i++) if (!Finite(result[i])) invalid++;
            test.Equal(0, invalid, label + " writes every scalar sample finitely");
            CheckBorrowed(input, pixels, label); CheckTime();
            return result;
        }

        public float4[] Multi(Color32[] input, int size, byte threshold, string label)
        {
            Begin();
            using var pixels = new NativeArray<Color32>(input, Allocator.TempJob);
            using var output = new NativeArray<float4>(input.Length, Allocator.TempJob);
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = new float4(float.NaN);
            multi.Invoke(null, new object[] { pixels, output, size, size, threshold, 0, 0f, SharpDistanceMode.GuardedSharp });
            var result = output.ToArray();
            int invalid = 0;
            for (int i = 0; i < result.Length; i++) for (int c = 0; c < 4; c++) if (!Finite(result[i][c])) invalid++;
            test.Equal(0, invalid, label + " MTSDF writes all RGBA components finitely");
            CheckBorrowed(input, pixels, label); CheckTime();
            return result;
        }

        public List<float2> Recover(List<float2> input)
        {
            Begin();
            var result = (List<float2>)recover.Invoke(null, new object[] { input });
            CheckTime(); return result;
        }

        void CheckBorrowed(Color32[] input, NativeArray<Color32> pixels, string label)
        {
            int changed = 0;
            for (int i = 0; i < input.Length; i++) if (!input[i].Equals(pixels[i])) changed++;
            test.Equal(0, changed, label + " production preserves borrowed source pixels");
        }
    }

    static void CheckSource(TestContext test, Color32[] expected, Color32[] actual, string label)
    {
        int changed = 0;
        for (int i = 0; i < expected.Length; i++) if (!expected[i].Equals(actual[i])) changed++;
        test.Equal(0, changed, label);
    }

    struct D2
    {
        public readonly double x, y;
        public D2(double x, double y) { this.x = x; this.y = y; }
        public D2(Vector2 v) { x = v.x; y = v.y; }
        public D2(float2 v) { x = v.x; y = v.y; }
        public double Length => Math.Sqrt(x * x + y * y);
        public D2 Unit { get { double n = Length; return n > 0 ? this * (1 / n) : new D2(); } }
        public bool IsFinite => Finite(x) && Finite(y);
        public static D2 operator +(D2 a, D2 b) => new D2(a.x + b.x, a.y + b.y);
        public static D2 operator -(D2 a, D2 b) => new D2(a.x - b.x, a.y - b.y);
        public static D2 operator *(D2 a, double b) => new D2(a.x * b, a.y * b);
        public static D2 operator *(double b, D2 a) => a*b;
        public override string ToString() => "(" + x + ", " + y + ")";
    }
    static double Dot(D2 a, D2 b) => a.x * b.x + a.y * b.y;
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static double Median(float4 v) => Math.Max(Math.Min(v.x, v.y), Math.Min(Math.Max(v.x, v.y), v.z));
}
