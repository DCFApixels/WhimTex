using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class LightHandleGeometryTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Run() => TestContext.Run("Light contours match the cone/range intersection across view transforms", t =>
    {
        var assembly = typeof(WhimTexWindow).Assembly;
        var viewportType = assembly.GetType("DCFApixels.WhimTex.CanvasViewport");
        var coordinatesType = assembly.GetType("DCFApixels.WhimTex.CanvasCoordinates");
        var build = typeof(WhimTexWindow).GetMethod("BuildLightSpotContour", F);
        var points = new List<Vector2>();
        int cases = 0, joins = 0;
        foreach (float height in new[] { 0f, 100f, -100f, 199.9f, 210f })
        foreach (float tilt in new[] { 0f, 20f, 60f, 89f, 110f, 160f, 180f })
        foreach (float angle in new[] { 1f, 30f, 95f, 179f })
        {
            float zoom = new[] { .125f, 1f, 4f }[cases % 3];
            var viewport = Activator.CreateInstance(viewportType, true);
            viewportType.GetMethod("SetRotation", F).Invoke(viewport, new object[] { (cases % 3) * 33f, false });
            object coordinates = Activator.CreateInstance(coordinatesType, F, null, new object[]
                { viewport, new Rect(0, 0, 800, 600), new Rect(-173.25f, 21.125f, 8192 * zoom, 8192 * zoom), new Vector2(8192, 8192) }, null);
            var toView = (Func<Vector2, Vector2>)Delegate.CreateDelegate(typeof(Func<Vector2, Vector2>), coordinates, coordinatesType.GetMethod("DocumentToView", F));
            var toDocument = (Func<Vector2, Vector2>)Delegate.CreateDelegate(typeof(Func<Vector2, Vector2>), coordinates, coordinatesType.GetMethod("ViewToDocument", F));
            var light = new WhimTexLight { type = WhimTexLightType.Spot, position = new Vector3(4096, 4096, height), range = 200, angle = angle,
                direction = new Vector3(Mathf.Sin(tilt * Mathf.Deg2Rad) * .8f, Mathf.Sin(tilt * Mathf.Deg2Rad) * .6f, -Mathf.Cos(tilt * Mathf.Deg2Rad)).normalized };
            string label = "height=" + height + ", tilt=" + tilt + ", angle=" + angle + ", zoom=" + zoom;
            bool closed = (bool)build.Invoke(null, new object[] { points, light, coordinates, angle * .5f, true, .25f });
            t.True(points.Count == 0 || closed && points.Count >= 3, "Outer boundary is closed: " + label);
            t.True(points.Count <= 8194, "Curve subdivision remains bounded: " + label);
            float limit = Mathf.Cos(angle * .5f * Mathf.Deg2Rad);
            foreach (var point in points)
            {
                t.True(float.IsFinite(point.x) && float.IsFinite(point.y), "Finite projected point: " + label);
                Vector2 pixel = toDocument(point);
                Vector3 offset = new Vector3(pixel.x - light.position.x, pixel.y - light.position.y, -height);
                float distance = offset.magnitude;
                float coneError = Mathf.Abs(Vector3.Dot(offset, light.direction) - distance * limit);
                float rangeError = Mathf.Abs(distance - light.range);
                t.True(Mathf.Min(coneError, rangeError) * zoom < .04f, "Vertex lies on a true cone/range boundary: " + label);
                if (coneError * zoom < .04f && rangeError * zoom < .04f) joins++;
            }
            for (int y = 0; y < 21; y++)
            for (int x = 0; x < 21; x++)
            {
                Vector2 offset = new Vector2((x - 10) * 19.7f + .11f, (y - 10) * 19.7f + .37f);
                Vector3 ray = new Vector3(offset.x, offset.y, -height);
                bool expected = ray.magnitude <= light.range && Vector3.Dot(ray.normalized, light.direction) >= limit;
                Vector2 point = toView((Vector2)light.position + offset);
                bool actual = Contains(points, point);
                t.True(actual == expected || Distance(points, point) <= .4f, "Coverage agrees with the lighting equation: " + label + ", point=" + offset);
            }
            var outer = new List<Vector2>(points);
            bool innerClosed = (bool)build.Invoke(null, new object[] { points, light, coordinates, angle * .35f, false, .25f });
            if (!innerClosed && points.Count > 1 && height != 0)
            {
                t.True(Distance(outer, points[0]) <= .4f && Distance(outer, points[points.Count - 1]) <= .4f,
                    "Clipped soft boundary meets the outer range contour: " + label);
            }
            cases++;
        }
        t.True(cases == 140 && joins > 20, "Cases include both exact joining points and degenerate orientations");
    });
    static bool Contains(List<Vector2> points, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            Vector2 a = points[i], b = points[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }
    static float Distance(List<Vector2> points, Vector2 p)
    {
        float result = float.PositiveInfinity;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i], delta = points[(i + 1) % points.Count] - a;
            float along = delta.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude) : 0;
            result = Mathf.Min(result, Vector2.Distance(p, a + delta * along));
        }
        return result;
    }
}
