using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;
using WhimTex.Tests;

public static class VectorClipperWrapperTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Type Stroke = typeof(VectorLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.StrokeGeometry", true);
    public static Paths64 Resolve(Paths64 a, Paths64 b = null, ClipType op = ClipType.Union, FillRule rule = FillRule.NonZero)
    {
        var engine = new Clipper64(); engine.AddSubject(a); if (b != null) engine.AddClip(b);
        var output = new Paths64(); if (!engine.Execute(op, rule, output)) throw new Exception("Reference Clipper failed");
        return output;
    }
    public static Paths64 Reference(Paths64[] inputs, VectorFillRule[] rules)
    {
        var result = new Paths64();
        for (int i = 0; i < inputs.Length; i++)
        {
            var filled = Resolve(inputs[i], rule: rules[i] == VectorFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
            result = Resolve(result, filled);
        }
        return result;
    }
    public static Paths64 Current(Paths64[] inputs, VectorFillRule[] rules)
    {
        var type = typeof(BooleanGroupLayerBehaviour).GetNestedType("GeometryPart", BindingFlags.NonPublic);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
        for (int i = 0; i < inputs.Length; i++)
            list.Add(constructor.Invoke(new object[] { inputs[i], rules[i] }));
        return (Paths64)typeof(BooleanGroupLayerBehaviour).GetMethod("FilledRegion", Static).Invoke(null, new object[] { list });
    }
    // Preserve orientation and every vertex; only rotate the seam and sort independent paths.
    static string Canonical(Paths64 paths)
    {
        var strings = new List<string>();
        foreach (var path in paths)
        {
            string smallest = null;
            for (int first = 0; first < path.Count; first++)
            {
                var text = new StringBuilder();
                for (int k = 0; k < path.Count; k++) { var p = path[(first + k) % path.Count]; text.Append(p.X).Append(',').Append(p.Y).Append(';'); }
                string s = text.ToString(); if (smallest == null || string.CompareOrdinal(s, smallest) < 0) smallest = s;
            }
            strings.Add(smallest ?? "");
        }
        strings.Sort(StringComparer.Ordinal); return string.Join("|", strings);
    }
    public static Paths64 Fixture(int seed)
    {
        var random = new System.Random(seed * 173 + 17); var result = new Paths64();
        if (seed % 17 == 0) return result;
        for (int p = 0; p < 1 + seed % 3; p++)
        {
            var path = new Path64();
            for (int i = 0; i < 6 + seed % 5; i++)
                path.Add(new Point64(random.Next(-25, 26), random.Next(-25, 26)));
            if (seed % 4 == 0) path.Reverse(); result.Add(path);
        }
        return result;
    }
    public static string Regions() => TestContext.Run("Boolean normalization preserves exact oriented contours", t =>
    {
        int cases = 0;
        for (int seed = 0; seed < 100; seed++)
        foreach (var operation in new[] { ClipType.Union, ClipType.Difference, ClipType.Intersection, ClipType.Xor })
        {
            var raw = Fixture(seed); var other = Fixture(seed + 1);
            var result = Resolve(raw, other, operation, seed % 2 == 0 ? FillRule.EvenOdd : FillRule.NonZero);
            foreach (bool compound in new[] { false, true })
            {
                var inputs = compound ? new[] { result, other } : new[] { result };
                var rules = compound ? new[] { VectorFillRule.NonZero, VectorFillRule.EvenOdd } : new[] { VectorFillRule.NonZero };
                string frozen = Canonical(result) + "/" + Canonical(other);
                t.Equal(Canonical(Reference(inputs, rules)), Canonical(Current(inputs, rules)),
                    "resolved contour parity seed=" + seed + " op=" + operation + " compound=" + compound);
                t.Equal(frozen, Canonical(result) + "/" + Canonical(other), "inputs remain unchanged"); cases++;
            }
            var rawInputs = new[] { raw, other };
            var rawRules = new[] { VectorFillRule.EvenOdd, VectorFillRule.NonZero };
            t.Equal(Canonical(Reference(rawInputs, rawRules)), Canonical(Current(rawInputs, rawRules)), "raw fill rules " + seed);
        }
        t.Equal(800, cases, "resolved/compound matrix");
        var touching = Resolve(Fixture(0), Fixture(1), ClipType.Union, FillRule.EvenOdd);
        t.True(Canonical(touching) != Canonical(Reference(new[] { touching }, new[] { VectorFillRule.NonZero })),
            "Retained counterexample: Clipper output cannot be assumed stable under another union");
    });

    static long ReferenceBudget(Paths64 subject, Paths64 clip)
    {
        var edges = new List<(long left, long right)>();
        foreach (var paths in new[] { subject, clip })
        {
            if (paths == null) continue;
            foreach (var path in paths)
            {
                if (path.Count < 3) continue;
                if (edges.Count + path.Count > 1048576) return -1;
                var previous = path[path.Count - 1];
                foreach (var p in path) { edges.Add((Math.Min(previous.X, p.X), Math.Max(previous.X, p.X))); previous = p; }
            }
        }
        edges.Sort((a, b) => a.left.CompareTo(b.left)); var active = new List<long>(); long visits = 0;
        foreach (var edge in edges)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (++visits > 4000000) return -1;
                if (active[i] < edge.left) { active[i] = active[active.Count - 1]; active.RemoveAt(active.Count - 1); }
            }
            active.Add(edge.right);
        }
        return visits;
    }
    public static string Budgets() => TestContext.Run("Intersection budget retains exact acceptance", t =>
    {
        var method = Stroke.GetMethod("CheckIntersectionBudget", Static);
        void Check(Paths64 a, Paths64 b, string label)
        {
            long actual;
            try { actual = (long)method.Invoke(null, new object[] { a, b }); }
            catch (TargetInvocationException e)
            {
                if (!(e.InnerException is InvalidOperationException) || !e.InnerException.Message.Contains("polygon")) throw;
                actual = -1;
            }
            t.Equal(ReferenceBudget(a, b), actual, label);
        }
        for (int seed = 0; seed < 150; seed++) Check(Fixture(seed), Fixture(seed + 31), "mixed endpoints " + seed);
        foreach (int count in new[] { 2827, 2828, 2829, 3000 })
        {
            var path = new Path64(); for (int i = 0; i < count; i++) path.Add(new Point64(i % 2, i));
            Check(new Paths64 { path }, null, "dense workload boundary " + count);
        }
        Check(new Paths64(), new Paths64(), "empty");
        Check(new Paths64 { new Path64 { new Point64(0, 0), new Point64(1, 1) } }, null, "ignored degenerate");
    });
}
