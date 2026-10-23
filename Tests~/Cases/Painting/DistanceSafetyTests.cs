// Opt-in entries: Run (35 calls), RunAngles (106), RunThinStroke (60).
// Explicit support: Tests~/Framework/TestApi.cs and
// Tests~/Cases/Painting/DistanceFieldTests.cs (DistanceFieldAnalyticFixture only).
// Each body has a 30s budget, including CPU raster/oracle work and job startup.
// CPU distance buffers; Run also reads one authenticated PNG and owns its decode
// texture. No assets, windows, documents, files written, or vector production path.
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

public static class DistanceSafetyTests
{
    const double OwnerUncertaintyLimit = 4, StraightSupport = 2, BorderBand = 1.5;
    const int BodyMilliseconds = 30000, MaximumSize = 512;
    const string FifthSource = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/DistanceRaster/star-13-inner-0364-phase3-known-failure.png";
    const string FifthHash = "98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC";
    static readonly byte[] Thresholds = { 96, 128, 160 };
    static readonly double[] CommonAngles = { 30, 45, 60, 75, 90 };
    static readonly double[] Subcells = { .25, .5, .75 };
    static readonly Vector2[] Phases = {
        new Vector2(.13f, -.13f), new Vector2(.61f, -.37f), new Vector2(-.23f, .41f)
    };

    public static string Run() => TestContext.Run(
        "Conservative raster distance safety: fifth source, finite owners, absent rays, tiny detail and curves", t =>
    {
        var api = new CpuApi(t, 35);
        CheckCaptured(t, api);
        CheckStars(t, api);
        CheckTinyDetail(t, api);
        CheckCurves(t, api);
        t.Equal(35, api.Calls, "6 captured + 18 star + 1 cap equality + 6 tiny-detail + 4 curve calls");
        api.CheckTime();
    });

    public static string RunAngles() => TestContext.Run(
        "Guarded raster angles: finite ownership at 10 through 90 degrees and requested bevels", t =>
    {
        var api = new CpuApi(t, 106);
        foreach (double degrees in new[] { 10.0, 15, 20, 30, 45, 60, 75, 90 })
        for (int phase = 0; phase < 2; phase++)
        {
            const int size = 96;
            Vector2[] polygon = Wedge(degrees, 34, 48, 60, phase);
            var source = Raster(size, new[] { polygon });
            foreach (byte threshold in Thresholds)
            {
                string label = $"wedge/{degrees}/phase={phase}/threshold={threshold}";
                var oracle = new FiniteContour(source, size, threshold);
                var rings = new[] { Offset(polygon, (128 - threshold) / 127.0) };
                var sharp = api.Scalar(source, size, threshold, DistanceMetric.Sharp, 64, label);
                var multi = api.Multi(source, size, threshold, label);
                CheckEnvelopes(t, api, rings, sharp, multi, size, label);
                CheckBilinear(t, api, oracle, multi, size, label, rings);
                CheckSides(t, rings[0], sharp, multi, size, label);
                if (degrees >= 30) CheckTip(t, rings[0], sharp, multi, size, label);
            }
            if (phase == 0 && degrees >= 30)
            {
                string label = $"requested lower Sharp limit/{degrees}";
                var limited = api.Scalar(source, size, 128, DistanceMetric.Sharp, 1.1f, label);
                var bevel = api.Scalar(source, size, 128, DistanceMetric.Bevel, 64, label);
                var tip = Tip(polygon, 2);
                int count = 0;
                // Keep these local bevel probes before the triangle's opposite
                // finite edge competes with the bevel. The independent bisector
                // formula is not an oracle for an entire multi-edge offset.
                foreach (double along in new[] { 6.0, 10 })
                {
                    P q = Integer(tip.vertex + tip.bisector * along);
                    int index = Index(q, size);
                    double expected = Dot(q - tip.vertex, tip.bisector) / tip.cosine;
                    t.Near(expected, limited[index], .85 + .04 * along, label + " bevel fan");
                    // Bevel retains the earlier contour fitter; Sharp uses
                    // adaptive local fits. Their fields are no longer identical.
                    t.Near(expected, bevel[index], .85 + .04 * along, label + " explicit Bevel fan");
                    t.True(limited[index] > along + 1, label + " lower limit has a visible bevel effect");
                    count++;
                }
                t.Equal(2, count, label + " exercises two non-border distances");
            }
        }
        t.Equal(106, api.Calls, "8 angles x 2 phases x 3 thresholds x 2 modes + 5 lower-limit pairs");
        api.CheckTime();
    });

    public static string RunThinStroke() => TestContext.Run(
        "Common-angle raster strokes and thin bars retain finite distances, holes and bilinear RGB signs", t =>
    {
        var api = new CpuApi(t, 60);
        foreach (double degrees in CommonAngles)
        for (int phase = 0; phase < 2; phase++)
        {
            const int size = 128;
            // A 4px triangular stroke has independently inset/outset finite
            // boundaries. The separate 3px bar supplies thin straight support.
            Vector2[] centerline = Wedge(degrees, 34, 83, 96, phase);
            var outer = Offset(centerline, 2);
            var inner = Offset(centerline, -2);
            Array.Reverse(inner);
            var bar = DistanceFieldAnalyticFixture.Rotate(
                DistanceFieldAnalyticFixture.Rectangle(-16, -1.5f, 16, 1.5f),
                (float)degrees, new Vector2(27, 27) + Phases[phase]);
            var originalRings = new[] { outer, inner, bar };
            var source = Raster(size, originalRings);
            foreach (byte threshold in Thresholds)
            {
                string label = $"thin/stroke/{degrees}/phase={phase}/threshold={threshold}";
                double shift = (128 - threshold) / 127.0;
                var rings = new[] { Offset(outer, shift), Offset(inner, shift), Offset(bar, shift) };
                var oracle = new FiniteContour(source, size, threshold);
                var sharp = api.Scalar(source, size, threshold, DistanceMetric.Sharp, 64, label);
                var multi = api.Multi(source, size, threshold, label);
                CheckEnvelopes(t, api, rings, sharp, multi, size, label);
                CheckBilinear(t, api, oracle, multi, size, label, rings);
                // Straight distances around the bar must not become round seed
                // distances or be swallowed by a distant stroke's angular fan.
                int probes = 0;
                for (int y = 8; y < 47; y++) for (int x = 8; x < 47; x++)
                {
                    P q = new P(x, y);
                    double expected = SignedPolygon(rings, q);
                    double barDistance = SignedPolygon(new[] { rings[2] }, q);
                    if (Math.Abs(expected - barDistance) > .001 || Math.Abs(expected) > 4 || Math.Abs(expected) < .7) continue;
                    Owner owner = Nearest(rings, q);
                    if (owner.parameter <= .15 || owner.parameter >= .85) continue;
                    int index = y * size + x;
                    t.Near(expected, sharp[index], .85, label + " thin supported side/Sharp");
                    t.Near(expected, Median(multi[index]), .85, label + " thin supported side/RGB");
                    t.Near(expected, multi[index].w, .85, label + " thin supported side/alpha");
                    probes++;
                }
                t.True(probes >= 40, label + " substantial thin side coverage; count=" + probes);
                P hole = (new P(rings[1][0]) + new P(rings[1][1]) + new P(rings[1][2])) * (1.0 / 3);
                int holeIndex = Index(Integer(hole), size);
                t.True(SignedPolygon(new[] { rings[0] }, Integer(hole)) < -2
                    && SignedPolygon(new[] { rings[1] }, Integer(hole)) < -2,
                    label + " hole probe is inside both finite triangle boundaries");
                t.True(SignedPolygon(rings, Integer(hole)) > 2, label + " independent stroke hole remains open");
                t.True(sharp[holeIndex] > 0 && Median(multi[holeIndex]) > 0 && multi[holeIndex].w > 0,
                    label + " retains the real hole in scalar, RGB and alpha");
            }
        }
        t.Equal(60, api.Calls, "5 common angles x 2 phases x 3 thresholds x 2 modes");
        api.CheckTime();
    });

    static void CheckCaptured(TestContext t, CpuApi api)
    {
        byte[] bytes = File.ReadAllBytes(FifthSource);
        using (var sha = SHA256.Create())
            t.Equal(FifthHash, BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""), "Authenticate fifth immutable input before decoding");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            t.True(ImageConversion.LoadImage(texture, bytes, false), "Decode authenticated fifth source");
            t.Equal(512, texture.width, "Fifth source width");
            t.Equal(512, texture.height, "Fifth source height");
            var source = texture.GetPixels32();
            var saved = (Color32[])source.Clone();
            var polygon = Star(13, 89.7093963623, .364, 245.040496826, 251.790664673, 0);
            P known = new P(20, 238);
            // This independently computed old unrestricted angular envelope is
            // a diagnostic floor, not the new oracle or an output golden.
            double oldIdeal = UnrestrictedAngularOutside(polygon, known);
            t.Near(58.078659, oldIdeal, .001, "Known query is tied to the authenticated source's analytic geometry");
            t.True(6.626863 < oldIdeal - 40, "The documented old false ray is separated from any allowed envelope");
            foreach (byte threshold in Thresholds)
            {
                string label = "fifth source/threshold=" + threshold;
                var oracle = new FiniteContour(source, 512, threshold);
                var rings = new[] { Offset(polygon, (128 - threshold) / 127.0) };
                var sharp = api.Scalar(source, 512, threshold, DistanceMetric.Sharp, 34.49f, label);
                var multi = api.Multi(source, 512, threshold, label);
                CheckEnvelopes(t, api, rings, sharp, multi, 512, label, 7);
                CheckAngularRings(t, rings, sharp, multi, 512, new P(245.040496826, 251.790664673), 89.7093963623, label);
                CheckBilinear(t, api, oracle, multi, 512, label, rings);
                if (threshold == 128)
                {
                    int index = Index(known, 512);
                    CheckProbe(t, rings, sharp, multi, known, 512, label + " known q(20,238)");
                    t.True(sharp[index] >= oldIdeal - 4 && Median(multi[index]) >= oldIdeal - 4,
                        label + " known absent ray cannot return the old 6.626px field; Sharp=" + sharp[index] + ", RGB=" + Median(multi[index]));
                    t.Near(oracle.Distance(known), multi[index].w, 4, label + " independent nearest finite raster alpha at known query");
                }
            }
            CheckSource(t, saved, source, "Fifth decoded source stays immutable");
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    static void CheckStars(TestContext t, CpuApi api)
    {
        for (int phase = 0; phase < 3; phase++)
        {
            const int size = 128;
            double rotation = phase * 17;
            var center = new P(64 + Phases[phase].x, 64 + Phases[phase].y);
            var polygon = Star(13, 37, .364, center.x, center.y, rotation);
            var source = Raster(size, new[] { polygon });
            foreach (byte threshold in Thresholds)
            {
                string label = $"star/phase={phase}/rotation={rotation}/threshold={threshold}";
                var rings = new[] { Offset(polygon, (128 - threshold) / 127.0) };
                var oracle = new FiniteContour(source, size, threshold);
                var sharp = api.Scalar(source, size, threshold, DistanceMetric.Sharp, 64, label);
                var multi = api.Multi(source, size, threshold, label);
                CheckEnvelopes(t, api, rings, sharp, multi, size, label);
                CheckAngularRings(t, rings, sharp, multi, size, center, 37, label);
                CheckBilinear(t, api, oracle, multi, size, label, rings);
                if (phase == 0 && threshold == 128)
                {
                    var capped = api.Scalar(source, size, threshold, DistanceMetric.Sharp, 4, label);
                    int different = 0;
                    for (int i = 0; i < sharp.Length; i++) if (sharp[i] != capped[i]) different++;
                    t.True(different > 0, "Guarded Sharp honors requested miter limits 4 and 64 instead of silently capping both at four");
                }
            }
        }
    }

    static void CheckTinyDetail(TestContext t, CpuApi api)
    {
        const int size = 64;
        // Include a disconnected one-sample island, a two-sample island, and
        // short edges in the same large contour. Whole-contour perimeter alone
        // cannot establish two pixels of straight support on both sides.
        var polygon = new[] { new Vector2(16.25f, 16.25f), new Vector2(45.25f, 16.25f),
            new Vector2(45.25f, 45.25f), new Vector2(32.25f, 45.25f),
            new Vector2(32.25f, 46.0f), new Vector2(31.5f, 45.25f), new Vector2(16.25f, 45.25f) };
        var source = Raster(size, new[] { polygon });
        source[8 * size + 8] = new Color32(17, 31, 43, 193);
        source[54 * size + 53] = new Color32(73, 91, 117, 211);
        source[54 * size + 54] = new Color32(127, 133, 139, 177);
        // A subpixel hole and adjacent AA perturbation remain source data.
        source[25 * size + 25] = new Color32(151, 157, 163, 95);
        source[44 * size + 31] = new Color32(167, 173, 179, 191);
        foreach (byte threshold in Thresholds)
        {
            string label = "short support/islands/threshold=" + threshold;
            var oracle = new FiniteContour(source, size, threshold);
            double amplification = SupportedAmplification(new[] { polygon });
            var sharp = api.Scalar(source, size, threshold, DistanceMetric.Sharp, 64, label);
            var multi = api.Multi(source, size, threshold, label);
            CheckBilinear(t, api, oracle, multi, size, label);
            int probes = 0;
            for (int y = 2; y < size - 2; y += 2) for (int x = 2; x < size - 2; x += 2)
            {
                P q = new P(x, y);
                double expected = oracle.Signed(q);
                if (Math.Abs(expected) < 4) continue;
                int index = y * size + x;
                double finite = Math.Abs(expected), lowSharp = finite, lowMulti = finite;
                // The large contour still has genuine supported right-angle
                // corners. Permit those fans, while giving neither island nor
                // either short side of the notch any angular support.
                for (int vertex = 0; vertex < polygon.Length; vertex++)
                {
                    Corner corner = Tip(polygon, vertex);
                    P r = q - corner.vertex;
                    if (corner.left < StraightSupport || corner.right < StraightSupport
                        || Dot(r, corner.incoming) < 0 || Dot(r, corner.outgoing) > 0
                        || (expected < 0) != (Cross(corner.incoming, corner.outgoing) < 0)) continue;
                    double angular = expected < 0 ? Math.Max(-Dot(r, corner.n1), -Dot(r, corner.n2))
                        : Math.Max(Dot(r, corner.n1), Dot(r, corner.n2));
                    if (Math.Abs(r.Length - Nearest(new[] { polygon }, q).distance) <= 1.2) lowSharp = Math.Min(lowSharp, angular);
                    lowMulti = Math.Min(lowMulti, angular);
                }
                t.True((sharp[index] < 0) == (expected < 0) && (Median(multi[index]) < 0) == (expected < 0),
                    label + " independent raw finite sign at " + q);
                t.True(Math.Abs(sharp[index]) >= lowSharp - 1.2, label + " no distant short-support Sharp ray at " + q);
                t.True(Math.Abs(Median(multi[index])) >= Math.Max(lowMulti - 1.2, finite / amplification - 1.2),
                    label + " no distant short-support RGB ray at " + q);
                t.Near(expected, multi[index].w, .85, label + " retained finite alpha at " + q);
                probes++;
            }
            t.True(probes >= 300, label + " far field covers islands and large-contour short edges");
            foreach (int index in new[] { 8 * size + 8, 54 * size + 53, 54 * size + 54 })
                t.True(sharp[index] < 0 && Median(multi[index]) < 0 && multi[index].w < 0,
                    label + " retains each real tiny inside sample rather than deleting noise");
            int hole = 25 * size + 25;
            t.True(sharp[hole] > 0 && Median(multi[hole]) > 0 && multi[hole].w > 0, label + " retains the real subpixel hole");
        }
    }

    static void CheckCurves(TestContext t, CpuApi api)
    {
        for (int phase = 0; phase < 2; phase++)
        {
            const int size = 96;
            double cx = 48 + Phases[phase].x, cy = 48 + Phases[phase].y;
            var source = DistanceFieldAnalyticFixture.Raster(size, size, (x, y) =>
                Math.Abs(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - 25) - 2, 128, 1);
            var oracle = new FiniteContour(source, size, 128);
            string label = "curved 4px annulus/phase=" + phase;
            var sharp = api.Scalar(source, size, 128, DistanceMetric.Sharp, 64, label);
            var multi = api.Multi(source, size, 128, label);
            CheckBilinear(t, api, oracle, multi, size, label);
            for (int y = 3; y < size - 3; y += 3) for (int x = 3; x < size - 3; x += 3)
            {
                P q = new P(x, y);
                double expected = oracle.Signed(q);
                int index = y * size + x;
                t.Near(expected, sharp[index], .9, label + " smooth finite Sharp at " + q);
                t.Near(expected, Median(multi[index]), .9, label + " smooth finite RGB at " + q);
                t.Near(expected, multi[index].w, .85, label + " smooth finite alpha at " + q);
            }
        }
    }

    static void CheckEnvelopes(TestContext t, CpuApi api, Vector2[][] rings, float[] sharp, float4[] multi,
        int size, string label, int step = 4)
    {
        int count = 0;
        for (int y = 2; y < size - 2; y += step)
        {
            api.CheckTime();
            for (int x = 2; x < size - 2; x += step)
            {
                if (Math.Abs(SignedPolygon(rings, new P(x, y))) <= BorderBand) continue;
                CheckProbe(t, rings, sharp, multi, new P(x, y), size, label);
                count++;
            }
        }
        t.True(count >= 300, label + " finite-owner envelopes cover interior and far exterior; count=" + count);
    }

    static void CheckAngularRings(TestContext t, Vector2[][] rings, float[] sharp, float4[] multi,
        int size, P center, double radius, string label)
    {
        int count = 0;
        foreach (double offset in new[] { 12.0, 24 })
        for (int angle = 0; angle < 360; angle += 2)
        {
            double radians = angle * Math.PI / 180;
            P q = Integer(center + new P(Math.Cos(radians), Math.Sin(radians)) * (radius + offset));
            if (q.x < 2 || q.y < 2 || q.x >= size - 2 || q.y >= size - 2) continue;
            CheckProbe(t, rings, sharp, multi, q, size, label + "/angular=" + angle);
            count++;
        }
        t.True(count >= 250, label + " checks between rays as well as intended tip directions");
    }

    static void CheckProbe(TestContext t, Vector2[][] rings, float[] sharp, float4[] multi, P q, int size, string label)
    {
        Owner owner = Nearest(rings, q);
        bool inside = SignedPolygon(rings, q) < 0;
        double lowSharp = owner.distance, lowMulti = owner.distance, high = owner.distance;
        double uncertainty = .85;
        double maximumAmplification = SupportedAmplification(rings);
        foreach (Vector2[] ring in rings)
        {
        // Independent boundaries combine their already-resolved artistic fields.
        // A farther rectangle can legitimately reach a point first after a sharp
        // offset. Finite ownership is local to that boundary, not to other islands.
        double contourDistance = Nearest(new[] { ring }, q).distance;
        for (int vertex = 0; vertex < ring.Length; vertex++)
        {
            Corner corner = Tip(ring, vertex);
            P r = q - corner.vertex;
            double length = r.Length;
            double amplification = 1 / corner.cosine;
            // Fitting may change which of two almost equidistant finite
            // endpoints owns a sample. Admit only that geometric uncertainty,
            // never a remote endpoint selected for its small angular value.
            // Keep the existing positional uncertainty budget. This bound
            // does not disqualify an otherwise supported acute corner.
            double ownerSlack = .85 * (1 + Math.Min(amplification, OwnerUncertaintyLimit)) + .04 * owner.distance;
            bool owns = length <= contourDistance + ownerSlack;
            bool fan = Dot(r, corner.incoming) >= 0 && Dot(r, corner.outgoing) <= 0
                && inside == (Cross(corner.incoming, corner.outgoing) < 0);
            if (!fan) continue;
            bool supported = corner.left >= StraightSupport && corner.right >= StraightSupport;
            if (supported)
            {
                double angular = inside ? Math.Max(-Dot(r, corner.n1), -Dot(r, corner.n2))
                    : Math.Max(Dot(r, corner.n1), Dot(r, corner.n2));
                // Sharp's angular value must belong to the nearest finite
                // polygon endpoint. RGB may combine other supported colors,
                // but never unsupported angular continuations. The adaptive
                // contract has no fixed minimum corner angle in either mode.
                if (owns) lowSharp = Math.Min(lowSharp, angular);
                lowMulti = Math.Min(lowMulti, angular);
            }
            if (owns)
            {
                // Extreme corners may round OR bevel. Bevel is permitted to
                // increase distance; it cannot invent a small distant ray.
                high = Math.Max(high, Math.Abs(Dot(r, corner.bisector)) / corner.cosine);
                uncertainty = Math.Max(uncertainty, .85 * amplification);
            }
        }
        }
        double budget = uncertainty + .04 * owner.distance;
        int index = Index(q, size);
        double s = sharp[index], m = Median(multi[index]);
        t.True((s < 0) == inside && (m < 0) == inside, label + " independent finite polygon sign at " + q);
        t.True(Math.Abs(s) >= Math.Max(0, lowSharp - budget) && Math.Abs(s) <= high + budget,
            label + " Sharp nearest finite-owner envelope at " + q + "; allowed=" + lowSharp + ".." + high + ", slack=" + budget + ", got=" + s);
        t.True(Math.Abs(m) >= Math.Max(0, lowMulti - budget) && Math.Abs(m) <= high + budget,
            label + " RGB allowed angular envelope at " + q + "; allowed=" + lowMulti + ".." + high + ", slack=" + budget + ", got=" + m);
        t.True(Math.Abs(m) >= Math.Max(0, owner.distance / maximumAmplification - budget),
            label + " RGB amplification is bounded by the independent supported corner angles at " + q);
        t.Near(inside ? -owner.distance : owner.distance, multi[index].w, budget, label + " alpha remains finite contour distance at " + q);
    }

    static void CheckSides(TestContext t, Vector2[] polygon, float[] sharp, float4[] multi, int size, string label)
    {
        Corner tip = Tip(polygon, 2);
        int count = 0;
        for (int side = 0; side < 2; side++)
        foreach (double along in new[] { 14.0, 22 })
        foreach (double offset in new[] { -1.5, 1.5, 3.0 })
        {
            P direction = side == 0 ? tip.incoming * -1 : tip.outgoing;
            P normal = side == 0 ? tip.n1 : tip.n2;
            P q = Integer(tip.vertex + direction * along + normal * offset);
            double expected = SignedPolygon(new[] { polygon }, q);
            if (Math.Abs(expected - Dot(q - tip.vertex, normal)) > .01 || Math.Abs(expected) < .7) continue;
            int index = Index(q, size);
            t.Near(expected, sharp[index], .85, label + " supported flank/Sharp");
            t.Near(expected, Median(multi[index]), .85, label + " supported flank/RGB");
            count++;
        }
        t.True(count >= 8, label + " independent probes exercise both finite flanks");
    }

    static void CheckTip(TestContext t, Vector2[] polygon, float[] sharp, float4[] multi, int size, string label)
    {
        Corner tip = Tip(polygon, 2);
        t.True(tip.cosine > 0 && tip.left > StraightSupport && tip.right > StraightSupport,
            label + " independently supported common-angle corner is eligible");
        int colored = 0;
        foreach (double along in new[] { 10.0, 16 })
        {
            P q = Integer(tip.vertex + tip.bisector * along);
            int index = Index(q, size);
            double expected = Math.Max(Dot(q - tip.vertex, tip.n1), Dot(q - tip.vertex, tip.n2));
            double budget = .85 + .04 * (along + 2);
            t.Near(expected, sharp[index], budget, label + " supported Sharp tip remains sharp at " + q);
            t.Near(expected, Median(multi[index]), budget, label + " supported RGB tip remains sharp at " + q);
            if (multi[index].w - Math.Abs(Median(multi[index])) > 1) colored++;
        }
        t.Equal(2, colored, label + " RGB corner character is distinct from finite Euclidean alpha");
    }

    static void CheckBilinear(TestContext t, CpuApi api, FiniteContour contour, float4[] field, int size, string label,
        Vector2[][] intended = null)
    {
        int checkedSamples = 0, insideSamples = 0;
        var analytic = intended == null ? null : new FiniteContour(intended, size);
        foreach (double fy in Subcells)
        for (int y = 2; y < size - 3; y++)
        {
            api.CheckTime();
            double[] crossings = contour.Crossings(y + fy);
            double[] analyticCrossings = analytic?.Crossings(y + fy);
            for (int x = 2; x < size - 3; x++)
            foreach (double fx in Subcells)
            {
                P q = new P(x + fx, y + fy);
                // Both references are independent of production: raw raster and
                // the analytic shape used to produce it. A recovered subpixel tip
                // may lie beyond the truncated raster tip, especially at 10 degrees.
                // Exclude their near-border bands, never a band derived from output.
                double signed = contour.SignedNear(q, crossings, BorderBand);
                if (Math.Abs(signed) <= BorderBand) continue;
                if (analytic != null && Math.Abs(analytic.SignedNear(q, analyticCrossings, BorderBand)) <= BorderBand) continue;
                int index = y * size + x;
                float4 a = field[index], b = field[index + 1], c = field[index + size], d = field[index + size + 1];
                // Interpolate each full RGB component BEFORE the nonlinear
                // median. Interpolating per-pixel medians misses color ghosts.
                double r = Bilinear(a.x, b.x, c.x, d.x, fx, fy);
                double g = Bilinear(a.y, b.y, c.y, d.y, fx, fy);
                double blue = Bilinear(a.z, b.z, c.z, d.z, fx, fy);
                double alpha = Bilinear(a.w, b.w, c.w, d.w, fx, fy);
                double median = Median(r, g, blue);
                bool inside = signed < 0;
                if (!Finite(median) || (median < 0) != inside || median == 0 || !Finite(alpha) || (alpha < 0) != inside || alpha == 0)
                    t.True(false, label + " full RGB bilinear sign disagrees with signed finite contour at " + q
                        + "; oracle=" + signed + ", RGB=" + r + "," + g + "," + blue + ", median=" + median + ", alpha=" + alpha);
                checkedSamples++;
                if (inside) insideSamples++;
            }
        }
        t.True(checkedSamples >= (size - 5) * (size - 5) * 5, label + " full domain, nine subcells, substantial non-border coverage; checked=" + checkedSamples);
        t.True(insideSamples >= 8, label + " bilinear checks include real interior and exterior; interior=" + insideSamples);
    }

    // Independent finite raster contour: cell crossing interpolation and the
    // bilinear saddle determinant, followed only by finite segment distance and
    // even/odd ray crossings. No extraction/fitting/color data from production.
    sealed class FiniteContour
    {
        const int BinSize = 8;
        readonly Segment[] segments;
        readonly List<int>[] bins;
        readonly int columns;
        public FiniteContour(Color32[] source, int size, byte threshold) : this(Extract(source, size, threshold), size) { }
        public FiniteContour(Vector2[][] rings, int size) : this(Segments(rings), size) { }
        static Segment[] Segments(Vector2[][] rings)
        {
            var list = new List<Segment>();
            foreach (var ring in rings) for (int i = 0; i < ring.Length; i++)
                list.Add(new Segment(new P(ring[i]), new P(ring[(i + 1) % ring.Length])));
            return list.ToArray();
        }
        static Segment[] Extract(Color32[] source, int size, byte threshold)
        {
            for (int i = 0; i < size; i++)
                if (source[i].a > threshold || source[(size - 1) * size + i].a > threshold
                    || source[i * size].a > threshold || source[i * size + size - 1].a > threshold)
                    throw new InvalidOperationException("Independent contour requires a closed raster inside the image border.");
            var list = new List<Segment>();
            var points = new P[4];
            for (int y = 0; y < size - 1; y++) for (int x = 0; x < size - 1; x++)
            {
                double a = source[y * size + x].a - threshold, b = source[y * size + x + 1].a - threshold;
                double c = source[(y + 1) * size + x + 1].a - threshold, d = source[(y + 1) * size + x].a - threshold;
                int count = 0;
                if ((a > 0) != (b > 0)) points[count++] = new P(x + a / (a - b), y);
                if ((b > 0) != (c > 0)) points[count++] = new P(x + 1, y + b / (b - c));
                if ((c > 0) != (d > 0)) points[count++] = new P(x + d / (d - c), y + 1);
                if ((d > 0) != (a > 0)) points[count++] = new P(x, y + a / (a - d));
                if (count == 2) list.Add(new Segment(points[0], points[1]));
                else if (count == 4)
                {
                    if (a * c - b * d >= 0)
                    { list.Add(new Segment(points[0], points[1])); list.Add(new Segment(points[2], points[3])); }
                    else
                    { list.Add(new Segment(points[0], points[3])); list.Add(new Segment(points[1], points[2])); }
                }
            }
            return list.ToArray();
        }
        FiniteContour(Segment[] segments, int size)
        {
            this.segments = segments;
            if (segments.Length == 0 || segments.Length > 8192) throw new InvalidOperationException("Independent contour segment count is out of bounds: " + segments.Length);
            columns = (size + BinSize - 1) / BinSize;
            bins = new List<int>[columns * columns];
            for (int i = 0; i < segments.Length; i++)
            {
                Segment s = segments[i];
                int x0 = Bin(Math.Min(s.a.x, s.b.x)), x1 = Bin(Math.Max(s.a.x, s.b.x));
                int y0 = Bin(Math.Min(s.a.y, s.b.y)), y1 = Bin(Math.Max(s.a.y, s.b.y));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    int bin = y * columns + x;
                    if (bins[bin] == null) bins[bin] = new List<int>();
                    bins[bin].Add(i);
                }
            }
        }
        int Bin(double coordinate) => Math.Max(0, Math.Min(columns - 1, (int)Math.Floor(coordinate / BinSize)));
        public double[] Crossings(double y)
        {
            var result = new List<double>();
            foreach (Segment s in segments)
                if ((s.a.y > y) != (s.b.y > y)) result.Add(s.a.x + (y - s.a.y) * (s.b.x - s.a.x) / (s.b.y - s.a.y));
            if ((result.Count & 1) != 0) throw new InvalidOperationException("Independent finite contour has an unpaired ray crossing at y=" + y);
            result.Sort();
            return result.ToArray();
        }
        static bool Inside(double x, double[] crossings)
        {
            int low = 0, high = crossings.Length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (crossings[middle] <= x) low = middle + 1; else high = middle;
            }
            return ((crossings.Length - low) & 1) != 0;
        }
        public double Distance(P q)
        {
            double best = double.PositiveInfinity;
            foreach (Segment s in segments) best = Math.Min(best, SegmentDistance(q, s.a, s.b));
            return best;
        }
        public double Signed(P q) => (Inside(q.x, Crossings(q.y)) ? -1 : 1) * Distance(q);
        public double SignedNear(P q, double[] crossings, double radius)
        {
            double best = double.PositiveInfinity;
            // Query every bin intersecting the radius square: a segment closer
            // than radius cannot be missed. Far distances are truncated to a
            // fixed finite magnitude; their exact sign still uses all contours.
            for (int y = Bin(q.y - radius); y <= Bin(q.y + radius); y++)
            for (int x = Bin(q.x - radius); x <= Bin(q.x + radius); x++)
            {
                List<int> ids = bins[y * columns + x];
                if (ids == null) continue;
                foreach (int id in ids) best = Math.Min(best, SegmentDistance(q, segments[id].a, segments[id].b));
            }
            best = Math.Min(best, radius + 1);
            return (Inside(q.x, crossings) ? -1 : 1) * best;
        }
    }

    sealed class CpuApi
    {
        readonly TestContext test;
        readonly MethodInfo scalar, multi;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly int maximumCalls;
        public int Calls { get; private set; }
        public CpuApi(TestContext test, int maximumCalls)
        {
            this.test = test; this.maximumCalls = maximumCalls;
            Type utility = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility", true);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            scalar = utility.GetMethod("ComputeArtisticDistance", flags, null, new[] {
                typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int),
                typeof(byte), typeof(int), typeof(DistanceMetric), typeof(float), typeof(float), typeof(SharpDistanceMode) }, null);
            multi = utility.GetMethod("ComputeMtsdfDistance", flags, null, new[] {
                typeof(NativeArray<Color32>), typeof(NativeArray<float4>), typeof(int), typeof(int), typeof(byte), typeof(int), typeof(float), typeof(SharpDistanceMode) }, null);
            test.True(scalar != null && multi != null, "Use public package-owned methods and enum; no Unity internal reflection");
        }
        public void CheckTime()
        {
            if (clock.ElapsedMilliseconds >= BodyMilliseconds)
                test.True(false, "Body exceeds 30s including raster/oracle work and production calls; elapsed=" + clock.ElapsedMilliseconds + "ms");
        }
        void Begin(Color32[] source, int size)
        {
            CheckTime();
            test.True(size >= 2 && size <= MaximumSize && source.Length == size * size, "Bounded square raster dimensions");
            test.True(++Calls <= maximumCalls, "Bounded production call count: " + Calls);
        }
        public float[] Scalar(Color32[] source, int size, byte threshold, DistanceMetric mode, float limit, string label)
        {
            Begin(source, size);
            var saved = (Color32[])source.Clone();
            using var pixels = new NativeArray<Color32>(source, Allocator.TempJob);
            using var output = new NativeArray<float>(source.Length, Allocator.TempJob);
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = float.NaN;
            scalar.Invoke(null, new object[] { pixels, output, size, size, threshold, 0, mode, limit, 0f, SharpDistanceMode.GuardedSharp });
            var result = output.ToArray();
            int invalid = 0;
            foreach (float value in result) if (!Finite(value)) invalid++;
            test.Equal(0, invalid, label + "/" + mode + " writes all finite scalar samples");
            Borrowed(saved, source, pixels, label);
            CheckTime();
            return result;
        }
        public float4[] Multi(Color32[] source, int size, byte threshold, string label)
        {
            Begin(source, size);
            var saved = (Color32[])source.Clone();
            using var pixels = new NativeArray<Color32>(source, Allocator.TempJob);
            using var output = new NativeArray<float4>(source.Length, Allocator.TempJob);
            var writable = output;
            for (int i = 0; i < output.Length; i++) writable[i] = new float4(float.NaN);
            multi.Invoke(null, new object[] { pixels, output, size, size, threshold, 0, 0f, SharpDistanceMode.GuardedSharp });
            var result = output.ToArray();
            int invalid = 0;
            foreach (float4 value in result) for (int channel = 0; channel < 4; channel++) if (!Finite(value[channel])) invalid++;
            test.Equal(0, invalid, label + " writes every RGB and true-distance alpha component finitely");
            Borrowed(saved, source, pixels, label);
            CheckTime();
            return result;
        }
        void Borrowed(Color32[] saved, Color32[] source, NativeArray<Color32> pixels, string label)
        {
            CheckSource(test, saved, source, label + " managed source remains immutable");
            int changed = 0;
            for (int i = 0; i < saved.Length; i++) if (!saved[i].Equals(pixels[i])) changed++;
            test.Equal(0, changed, label + " borrowed NativeArray RGBA, including alpha, remains immutable");
        }
    }

    static Color32[] Raster(int size, Vector2[][] rings) => DistanceFieldAnalyticFixture.Raster(size, size,
        (x, y) => SignedPolygon(rings, new P(x, y)), 128, 1);
    static Vector2[] Wedge(double degrees, double length, float cx, float cy, int phase)
    {
        double half = degrees * Math.PI / 360;
        return DistanceFieldAnalyticFixture.Rotate(new[] {
            new Vector2((float)(-length * Math.Sin(half)), (float)(-length * Math.Cos(half))),
            new Vector2((float)(length * Math.Sin(half)), (float)(-length * Math.Cos(half))), Vector2.zero },
            phase == 0 ? 0 : 37, new Vector2(cx, cy) + Phases[phase]);
    }
    static Vector2[] Star(int rays, double radius, double ratio, double cx, double cy, double rotation)
    {
        var result = new Vector2[rays * 2];
        for (int i = 0; i < result.Length; i++)
        {
            double angle = Math.PI * (.5 + i / (double)rays + rotation / 180), r = (i & 1) == 0 ? radius : radius * ratio;
            result[i] = new Vector2((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));
        }
        return result;
    }
    static Vector2[] Offset(Vector2[] ring, double shift)
    {
        var result = new Vector2[ring.Length];
        for (int i = 0; i < ring.Length; i++)
        {
            Corner corner = Tip(ring, i);
            P p = corner.vertex + corner.bisector * (shift / corner.cosine);
            result[i] = new Vector2((float)p.x, (float)p.y);
        }
        return result;
    }
    static Corner Tip(Vector2[] ring, int index)
    {
        P v = new P(ring[index]), left = v - new P(ring[(index + ring.Length - 1) % ring.Length]);
        P right = new P(ring[(index + 1) % ring.Length]) - v;
        P incoming = left.Unit, outgoing = right.Unit;
        P n1 = new P(incoming.y, -incoming.x), n2 = new P(outgoing.y, -outgoing.x), bisector = (n1 + n2).Unit;
        return new Corner { vertex = v, incoming = incoming, outgoing = outgoing, n1 = n1, n2 = n2,
            bisector = bisector, cosine = Math.Max(.000001, Dot(n1, bisector)), left = left.Length, right = right.Length };
    }
    static double SupportedAmplification(Vector2[][] rings)
    {
        double result = 1;
        foreach (Vector2[] ring in rings) for (int i = 0; i < ring.Length; i++)
        {
            Corner corner = Tip(ring, i);
            if (corner.left >= StraightSupport && corner.right >= StraightSupport)
                result = Math.Max(result, 1 / corner.cosine);
        }
        return result;
    }
    static Owner Nearest(Vector2[][] rings, P q)
    {
        Owner best = new Owner { distance = double.PositiveInfinity };
        foreach (Vector2[] ring in rings) for (int i = 0; i < ring.Length; i++)
        {
            P a = new P(ring[i]), b = new P(ring[(i + 1) % ring.Length]), d = b - a;
            double u = Math.Max(0, Math.Min(1, Dot(q - a, d) / Dot(d, d))), distance = (q - a - d * u).Length;
            if (distance < best.distance) best = new Owner { distance = distance, parameter = u };
        }
        return best;
    }
    static double SignedPolygon(Vector2[][] rings, P q)
    {
        bool inside = false;
        foreach (Vector2[] ring in rings) for (int i = 0; i < ring.Length; i++)
        {
            P a = new P(ring[i]), b = new P(ring[(i + 1) % ring.Length]);
            if ((a.y > q.y) != (b.y > q.y) && q.x < a.x + (q.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
        }
        return (inside ? -1 : 1) * Nearest(rings, q).distance;
    }
    static double UnrestrictedAngularOutside(Vector2[] polygon, P q)
    {
        double best = Nearest(new[] { polygon }, q).distance;
        for (int i = 0; i < polygon.Length; i++)
        {
            Corner corner = Tip(polygon, i);
            P r = q - corner.vertex;
            if (Cross(corner.incoming, corner.outgoing) <= 0 || Dot(r, corner.incoming) < 0 || Dot(r, corner.outgoing) > 0) continue;
            best = Math.Min(best, Math.Max(Dot(r, corner.n1), Dot(r, corner.n2)));
        }
        return best;
    }
    static double SegmentDistance(P q, P a, P b)
    {
        P d = b - a;
        double length = Dot(d, d), u = length > 0 ? Math.Max(0, Math.Min(1, Dot(q - a, d) / length)) : 0;
        return (q - a - d * u).Length;
    }
    static void CheckSource(TestContext t, Color32[] expected, Color32[] actual, string label)
    {
        int changed = 0;
        for (int i = 0; i < expected.Length; i++) if (!expected[i].Equals(actual[i])) changed++;
        t.Equal(0, changed, label);
    }
    static P Integer(P p) => new P(Math.Round(p.x), Math.Round(p.y));
    static int Index(P q, int size)
    {
        int x = (int)q.x, y = (int)q.y;
        if (x < 0 || y < 0 || x >= size || y >= size) throw new InvalidOperationException("Probe outside bounded raster: " + q);
        return y * size + x;
    }
    static double Bilinear(double a, double b, double c, double d, double x, double y)
        => (a * (1 - x) + b * x) * (1 - y) + (c * (1 - x) + d * x) * y;
    static double Median(float4 value) => Median(value.x, value.y, value.z);
    static double Median(double r, double g, double b) => Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b));
    static double Dot(P a, P b) => a.x * b.x + a.y * b.y;
    static double Cross(P a, P b) => a.x * b.y - a.y * b.x;
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    struct Corner { public P vertex, incoming, outgoing, n1, n2, bisector; public double cosine, left, right; }
    struct Owner { public double distance, parameter; }
    struct Segment { public readonly P a, b; public Segment(P a, P b) { this.a = a; this.b = b; } }
    readonly struct P
    {
        public readonly double x, y;
        public P(double x, double y) { this.x = x; this.y = y; }
        public P(Vector2 p) { x = p.x; y = p.y; }
        public double Length => Math.Sqrt(x * x + y * y);
        public P Unit => this * (1 / Length);
        public static P operator +(P a, P b) => new P(a.x + b.x, a.y + b.y);
        public static P operator -(P a, P b) => new P(a.x - b.x, a.y - b.y);
        public static P operator *(P a, double b) => new P(a.x * b, a.y * b);
        public override string ToString() => "(" + x + "," + y + ")";
    }
}
