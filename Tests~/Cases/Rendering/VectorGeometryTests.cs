// Support: Tests~/Framework/TestApi.cs. Not registered or executed by this change.
using System;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class VectorGeometryTests
{
    public static string Run() => TestContext.Run("VectorGeometryTests", context =>
    {
        var a = new VectorPoint(new Vector2(.1f, .2f)) { outTangent = new Vector2(.1f, .6f) };
        var b = new VectorPoint(new Vector2(.9f, .4f)) { inTangent = new Vector2(-.2f, .4f) };
        var contour = new VectorContour { closed = false, points = new List<VectorPoint> { a, b } };
        string aid = a.id, bid = b.id;
        Vector2 p0 = a.position, p1 = a.position + a.outTangent, p2 = b.position + b.inTangent, p3 = b.position;
        var inserted = VectorGeometry.SplitSegment(contour, 0, .3f);
        context.Equal(aid, a.id, "split keeps first ID"); context.Equal(bid, b.id, "split keeps last ID");
        context.True(inserted.id != aid && inserted.id != bid, "inserted ID is new");
        for (int i = 0; i <= 40; i++)
        {
            float t = i / 40f;
            Vector2 expected = VectorGeometry.EvaluateCubic(p0, p1, p2, p3, t);
            var left = t <= .3f ? a : inserted; var right = t <= .3f ? inserted : b;
            float local = t <= .3f ? t / .3f : (t - .3f) / .7f;
            Vector2 actual = VectorGeometry.EvaluateCubic(left.position, left.position + left.outTangent,
                right.position + right.inTangent, right.position, local);
            context.True(Vector2.Distance(expected, actual) < 2e-6f, "split preserves entire cubic at " + t);
        }
        var flat = new List<Vector2>();
        VectorGeometry.FlattenContour(contour, new Vector2(256, 128), .1f, flat);
        context.True(flat.Count > 4, "curve is adaptively flattened");
        context.True(Vector2.Distance(flat[0], Vector2.Scale(a.position, new Vector2(256, 128))) < 1e-5f, "flatten emits reference pixels");
        Vector2 query = VectorGeometry.EvaluateCubic(p0, p1, p2, p3, .72f);
        context.True(VectorGeometry.TryHitSegment(contour, query, new Vector2(256, 128), .1f,
            out int segment, out float along, out float distance), "cubic insertion hit");
        context.Equal(1, segment, "hit identifies split segment");
        context.Near((.72f - .3f) / .7f, along, .002, "hit returns cubic parameter");
        context.True(distance < .03f, "hit distance in reference pixels");

        var straight = new VectorContour { closed = false, points = new List<VectorPoint>
            { new VectorPoint(Vector2.zero), new VectorPoint(Vector2.right) } };
        context.True(VectorGeometry.TryHitSegment(straight, new Vector2(.2f, 0), Vector2.one * 100, .1f,
            out _, out float straightT, out _), "straight cubic hit");
        VectorPoint straightSplit = VectorGeometry.SplitSegment(straight, 0, straightT);
        context.Near(.2, straightSplit.position.x, .0002, "hit parameter accounts for nonlinear straight-cubic speed");

        var reversal = new VectorContour { closed = false, points = new List<VectorPoint>
        { new VectorPoint(Vector2.zero) { outTangent = Vector2.right * 2 }, new VectorPoint(Vector2.right) { inTangent = Vector2.left * 2 } } };
        VectorGeometry.FlattenContour(reversal, Vector2.one * 100, .01f, flat);
        context.True(flat.Count > 2, "collinear reversing handles are not collapsed to endpoint chord");

        var closed = new VectorContour { points = new List<VectorPoint> { new VectorPoint(Vector2.zero), new VectorPoint(Vector2.right), new VectorPoint(Vector2.one) } };
        var seam = VectorGeometry.SplitSegment(closed, 2, .5f);
        context.True(ReferenceEquals(closed.points[3], seam), "closing segment split inserts at end");
        context.Near(.5, seam.position.x, 1e-6, "closing split x");
        var shape = new VectorShape { contours = new List<VectorContour> { contour } };
        var clone = shape.Clone(); clone.contours[0].points[0].position = Vector2.zero;
        clone.dashPattern.Add(5);
        context.Equal(shape.id, clone.id, "snapshot clone preserves stable shape ID");
        context.True(a.position != Vector2.zero && shape.dashPattern.Count == 0, "snapshot has independent lists and points");
        var duplicate = shape.Clone(false);
        context.True(shape.id != duplicate.id && shape.contours[0].points[0].id != duplicate.contours[0].points[0].id, "duplicate clone gets new IDs");

        var model = new VectorLayerBehaviour(); model.shapes.Add(shape); model.Validate();
        int count = contour.points.Count;
        bool failed = false; shape.dashPattern.Add(0);
        try { model.Validate(); } catch (InvalidOperationException) { failed = true; }
        context.True(failed, "zero dash has an explicit diagnostic");
        context.Equal(count, contour.points.Count, "invalid style never deletes anchors");
        shape.dashPattern.Clear(); b.id = a.id; failed = false;
        try { model.Validate(); } catch (InvalidOperationException) { failed = true; }
        context.True(failed, "duplicate IDs rejected without repair");
        b.id = bid; model.Validate();
    });
}
