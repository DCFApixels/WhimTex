// Package-owned reflection, bounded CPU-only geometry tests. Support: TestApi.
using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using WhimTex.Tests;

public static class ReconstructionSafetyTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static Assembly Assembly => typeof(SDFLayerBehaviour).Assembly;

    public static string Run() => TestContext.Run("Reconstruction: composite AA, geometric signs and continuous local endpoint influence", t =>
    {
        Composite(t);
        Signs(t);
        Continuity(t);
    });

    static List<float2> Rectangle(float left, float right) => new List<float2> {
        new float2(left, .5f), new float2(right, .5f), new float2(right, 3.5f), new float2(left, 3.5f) };

    static void Composite(TestContext t)
    {
        var original = Rectangle(1.05f, 1.45f);
        var candidate = Rectangle(1, 1.45f);
        var other = Rectangle(.55f, .95f);
        var type = Assembly.GetType("DCFApixels.WhimTex.RasterCornerDistanceContour", true);
        var method = type.GetMethod("CompositeArea", Static);
        t.True(method != null, "Corner fitting retains composite coverage integration");
        float Coverage(List<float2> path, List<List<float2>> loops) => (float)method.Invoke(null,
            new object[] { path, loops, new float2(1, 2), 0f });
        t.Near(.4, Coverage(original, null), .000001, "One isolated component covers .4 of the pixel");
        t.Near(.8, Coverage(original, new List<List<float2>> { original, other }), .000001,
            "Corner fitting accounts for both components sharing an AA pixel");
        t.Near(.85, Coverage(candidate, new List<List<float2>> { candidate, other }), .000001,
            "Composite integration exposes candidate growth even without an intersection");
    }

    static void Signs(TestContext t)
    {
        var original = new List<float2> { new float2(4, 5), new float2(28, 5), new float2(16.6f, 24.3f), new float2(15.4f, 24.3f) };
        var fitted = new List<float2> { new float2(4, 5), new float2(28, 5), new float2(16, 25.25f) };
        var type = Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var method = type.GetMethod("ReconstructedSignChanges", Static);
        t.True(method != null, "Geometric membership update exists");
        var mask = (byte[])method.Invoke(null, new object[] { new List<List<float2>> { original }, new List<List<float2>> { fitted }, 32, 32 });
        t.Equal((byte)1, mask[25 * 32 + 16], "Recovered low-coverage tip changes pixel-centre membership");
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            t.Equal(Contains(original, x, y) != Contains(fitted, x, y), mask[y * 32 + x] != 0, "Independent crossing membership " + x + "/" + y);
    }

    static bool Contains(List<float2> path, double x, double y)
    {
        bool inside = false;
        for (int i = 0; i < path.Count; i++)
        {
            float2 a = path[i], b = path[(i + 1) % path.Count];
            if ((a.y > y) != (b.y > y) && x < a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
        }
        return inside;
    }

    static void Continuity(TestContext t)
    {
        var type = Assembly.GetType("DCFApixels.WhimTex.ArtisticDistanceField", true);
        var featureType = type.GetNestedType("Feature", BindingFlags.NonPublic);
        var nodeType = type.GetNestedType("Node", BindingFlags.NonPublic);
        var evaluate = type.GetMethod("EvaluateReconstructed", Static);
        t.True(featureType != null && nodeType != null && evaluate != null, "Exact package kernel types");
        var features = Array.CreateInstance(featureType, 3);
        float2 incoming = math.normalize(new float2(10, 5)), outgoing = math.normalize(new float2(-10, 5));
        void Feature(int index, float2 a, float2 b, float2 previous, float2 next, int flags)
        {
            object f = Activator.CreateInstance(featureType);
            featureType.GetField("a").SetValue(f, a); featureType.GetField("b").SetValue(f, b);
            featureType.GetField("previousDirection").SetValue(f, previous); featureType.GetField("nextDirection").SetValue(f, next);
            featureType.GetField("color").SetValue(f, flags); features.SetValue(f, index);
        }
        // Two incident edges with a verified convex tip; an unverified competing
        // wall in the same BVH exercises the former nearest-owner switch.
        Feature(0, new float2(-10, -5), new float2(0, 0), incoming, outgoing, 16);
        Feature(1, new float2(0, 0), new float2(-10, 5), incoming, outgoing, 32);
        Feature(2, new float2(10, -10), new float2(10, 10), new float2(0, 1), new float2(0, 1), 48);
        object node = Activator.CreateInstance(nodeType);
        nodeType.GetField("minimum").SetValue(node, new float2(-10, -10));
        nodeType.GetField("maximum").SetValue(node, new float2(10, 10));
        nodeType.GetField("count").SetValue(node, 3);
        var nodes = Array.CreateInstance(nodeType, 1); nodes.SetValue(node, 0);
        object nativeFeatures = null, nativeNodes = null;
        using var roots = new NativeArray<int>(new[] { 0 }, Allocator.TempJob);
        try
        {
            nativeFeatures = Activator.CreateInstance(typeof(NativeArray<>).MakeGenericType(featureType), new object[] { features, Allocator.TempJob });
            nativeNodes = Activator.CreateInstance(typeof(NativeArray<>).MakeGenericType(nodeType), new object[] { nodes, Allocator.TempJob });
            float Sample(float x, float y, float miter = 4) => (float)evaluate.Invoke(null, new object[] {
                new float2(x, y), nativeFeatures, nativeNodes, roots, miter, false });
            t.Near(5 / Math.Sqrt(5), Sample(5, 0), .00002, "Verified corner contributes true sharp character at finite-owner tie");
            foreach (float miter in new[] { 1.1f, 4f })
                foreach (float x in new[] { 4.5f, 5f, 5.25f, 5.75f, 6f })
                    t.Near(Sample(x - .00001f, 0, miter), Sample(x + .00001f, 0, miter), .001,
                        "No value jump at ownership/locality transitions; x=" + x + ", miter=" + miter);
            t.Near(4, Sample(6, 0), .00001, "Remote corner loses influence when a competing boundary is nearer by more than 1.5px");
            for (int i = 0; i < 64; i++)
            {
                float x = 4 + i * .05f, value = Sample(x, 0, 64);
                t.True(float.IsFinite(value) && value > 0, "No extra zero-distance ray in gap");
            }
        }
        finally
        {
            if (nativeNodes is IDisposable a) a.Dispose();
            if (nativeFeatures is IDisposable b) b.Dispose();
        }
    }
}
