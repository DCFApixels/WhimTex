// Entry: Run. Support: Tests~/Framework/TestApi.cs.
// Opt-in catalog entry: distance-spring-contour-v1.
// Reflection accesses only the package-owned entry, never Unity internals.
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class SpringDistanceContourTests
{
    const int Size = 32;

    public static string Run() => TestContext.Run("Automatic bounded spring contour adaptation", t =>
    {
        var type = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.SpringDistanceContour", true);
        var method = type.GetMethod("Refine", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(List<float2>), typeof(NativeArray<Color32>), typeof(int), typeof(int), typeof(byte), typeof(int) }, null);
        t.True(method != null, "Expected package-owned integration signature");
        List<float2> Refine(List<float2> path, int channel = 0, bool soft = false)
        {
            using var pixels = new NativeArray<Color32>(Size * Size, Allocator.Temp);
            var writable = pixels;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                byte v = Inside(path, new float2(x, y)) ? (byte)(soft ? 200 : 255) : (byte)(soft ? 40 : 0);
                writable[y * Size + x] = channel == 0 ? new Color32(19, 83, 227, v) :
                    channel == 1 ? new Color32(v, 83, 227, 255) :
                    channel == 2 ? new Color32(19, v, 227, 255) :
                    channel == 3 ? new Color32(19, 83, v, 255) : new Color32(v, v, v, 255);
            }
            var saved = path.ToArray();
            var savedPixels = pixels.ToArray();
            var result = (List<float2>)method.Invoke(null, new object[] {
                path, pixels, Size, Size, (byte)(soft ? 120 : 128), channel });
            t.Equal(saved.Length, path.Count, "Borrowed list size");
            for (int i = 0; i < path.Count; i++)
                t.True(math.all(saved[i] == path[i]), "Borrowed contour is unchanged");
            for (int i = 0; i < pixels.Length; i++)
                t.True(savedPixels[i].Equals(pixels[i]), "Borrowed source pixels are unchanged");
            return result;
        }

        var rectangle = Subdivide(Path(4.5f,4.5f, 20.5f,4.5f, 20.5f,20.5f, 4.5f,20.5f));
        var squareResult = Refine(rectangle);
        Geometry(t, rectangle, squareResult);
        foreach (int corner in new[] { 0, 16, 32, 48 })
            t.True(math.all(rectangle[corner] == squareResult[corner]), "Multi-pixel supported corner stays exactly fixed");

        var ripple = new List<float2>(rectangle);
        for (int i = 4; i <= 12; i++) ripple[i] += new float2(0, i % 2 == 0 ? .22f : -.22f);
        var relaxed = Refine(ripple);
        Geometry(t, ripple, relaxed);
        double before = 0, after = 0;
        for (int i = 4; i <= 12; i++)
        {
            before += Math.Pow(ripple[i].y - 4.5, 2);
            after += Math.Pow(relaxed[i].y - 4.5, 2);
        }
        t.True(after < before * .95, "Spring forces measurably reduce raster-scale oscillation");
        t.True(!ReferenceEquals(ripple, relaxed), "Eligible contour is actually refined");

        // Rotation of the start index and winding reversal must not change the
        // geometric answer (within float accumulation error).
        foreach (int shift in new[] { 0, 7, 31 })
        foreach (bool reverse in new[] { false, true })
        {
            var rotated = new List<float2>();
            for (int i = 0; i < ripple.Count; i++)
                rotated.Add(ripple[(shift + (reverse ? ripple.Count - i : i)) % ripple.Count]);
            var result = Refine(rotated);
            Geometry(t, rotated, result);
            for (int i = 0; i < result.Count; i++)
            {
                int source = (shift + (reverse ? ripple.Count - i : i)) % ripple.Count;
                t.True(math.distance(result[i], relaxed[source]) < .002f, "Cyclic/winding-independent relaxation");
            }
        }
        for (int channel = 1; channel <= 4; channel++)
        {
            var result = Refine(ripple, channel);
            for (int i = 0; i < result.Count; i++)
                t.True(math.distance(result[i], relaxed[i]) < .002f, "Selected source channel is honored: " + channel);
        }
        Geometry(t, ripple, Refine(ripple, 0, true));

        // A half-pixel neck, a narrow open slit, and a tiny isolated component.
        // Independently compare every nonadjacent edge pair, including pairs
        // separated by many contour indices; area alone would miss a collapsed neck.
        foreach (var raw in new[] {
            Path(4.5f,4.5f, 10.5f,4.5f, 10.5f,12.25f, 18.5f,12.25f,
                 18.5f,4.5f, 24.5f,4.5f, 24.5f,20.5f, 18.5f,20.5f,
                 18.5f,12.75f, 10.5f,12.75f, 10.5f,20.5f, 4.5f,20.5f),
            Path(4.5f,4.5f, 20.5f,4.5f, 20.5f,20.5f, 13,20.5f,
                 13,8.5f, 12.5f,8.5f, 12.5f,20.5f, 4.5f,20.5f),
            Path(8.5f,9, 9,8.5f, 9.5f,9, 9,9.5f) })
        {
            var source = Subdivide(raw);
            Geometry(t, source, Refine(source));
        }

        foreach (var invalid in new[] {
            Path(5,5, 10,5, 10,5, 10,10, 5,10), // Repeated adjacent vertex.
            Path(5,5, 6,5, 7,5),               // Zero area.
            Path(5,5, 15,15, 5,15, 14,5),     // Nonzero signed area, self-crossing.
            Path(5,5, 15,5, 15,15, 5,15, 5,5) })
            t.True(ReferenceEquals(invalid, Refine(invalid)), "Ambiguous/degenerate polygon is an exact fallback");
    });

    static void Geometry(TestContext t, List<float2> original, List<float2> result)
    {
        t.Equal(original.Count, result.Count, "No vertex insertion, deletion or resampling");
        double ratio = Area(result) / Area(original);
        t.True(ratio >= .94999 && ratio <= 1.05001, "Winding and bounded area drift");
        for (int i = 0; i < original.Count; i++)
        {
            int next = (i + 1) % original.Count;
            t.True(math.all(math.isfinite(result[i])), "Every result coordinate is finite");
            t.True(math.distance(original[i], result[i]) <= .45001f, "Subpixel displacement bound");
            t.True(math.distance(result[i], result[next]) >= .5999f * math.distance(original[i], original[next]),
                "No collapsed edge");
            for (int j = i + 2; j < original.Count; j++)
            {
                int jNext = (j + 1) % original.Count;
                if (jNext == i) continue;
                double oldGap = Gap(original[i], original[next], original[j], original[jNext]);
                double newGap = Gap(result[i], result[next], result[j], result[jNext]);
                t.True(oldGap > 0, "Fixture is simple");
                t.True(newGap > 0 && newGap + 1e-5 >= .5999 * oldGap,
                    "No new intersections; at least 60% of each original local separation survives");
            }
        }
    }

    static List<float2> Path(params float[] xy)
    {
        var result = new List<float2>();
        for (int i = 0; i < xy.Length; i += 2) result.Add(new float2(xy[i], xy[i + 1]));
        return result;
    }

    static List<float2> Subdivide(List<float2> corners)
    {
        var result = new List<float2>();
        for (int i = 0; i < corners.Count; i++)
        {
            float2 a = corners[i], b = corners[(i + 1) % corners.Count];
            int count = Math.Max(1, (int)Math.Ceiling(math.distance(a, b)));
            for (int k = 0; k < count; k++) result.Add(math.lerp(a, b, (float)k / count));
        }
        return result;
    }

    static bool Inside(List<float2> path, float2 p)
    {
        bool inside = false;
        for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
        {
            float2 a = path[i], b = path[j];
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < ((double)b.x - a.x) * (p.y - a.y) / ((double)b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    static double Area(List<float2> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            float2 a = points[i], b = points[(i + 1) % points.Count];
            area += (double)a.x * b.y - (double)a.y * b.x;
        }
        return .5 * area;
    }

    static double Gap(float2 a, float2 b, float2 c, float2 d)
    {
        double abC = Orient(a, b, c), abD = Orient(a, b, d), cdA = Orient(c, d, a), cdB = Orient(c, d, b);
        bool overlap = Math.Max(Math.Min(a.x, b.x), Math.Min(c.x, d.x)) <= Math.Min(Math.Max(a.x, b.x), Math.Max(c.x, d.x)) &&
            Math.Max(Math.Min(a.y, b.y), Math.Min(c.y, d.y)) <= Math.Min(Math.Max(a.y, b.y), Math.Max(c.y, d.y));
        if (overlap && abC * abD <= 0 && cdA * cdB <= 0) return 0;
        return Math.Min(Math.Min(PointGap(a, c, d), PointGap(b, c, d)), Math.Min(PointGap(c, a, b), PointGap(d, a, b)));
    }

    static double Orient(float2 a, float2 b, float2 c) =>
        ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

    static double PointGap(float2 p, float2 a, float2 b)
    {
        double x = (double)b.x - a.x, y = (double)b.y - a.y;
        double px = (double)p.x - a.x, py = (double)p.y - a.y;
        double t = Math.Max(0, Math.Min(1, (px * x + py * y) / (x * x + y * y)));
        return Math.Sqrt(Math.Pow(px - t * x, 2) + Math.Pow(py - t * y, 2));
    }
}
