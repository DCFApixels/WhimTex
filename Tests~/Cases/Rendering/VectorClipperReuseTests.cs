using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;
using WhimTex.Tests;

// Support: Framework/TestApi.cs. Detached polygon data only; no documents, assets or windows.
public static class VectorClipperReuseTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type Stroke = typeof(VectorLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.StrokeGeometry", true);
    static readonly MethodInfo ResolveMethod = Stroke.GetMethod("Resolve", Static);
    static readonly MethodInfo BeginMethod = Stroke.GetMethod("BeginBuild", Static);
    static readonly FieldInfo CurrentField = Stroke.GetField("currentBuild", Static);
    static object Current => CurrentField.GetValue(null);
    static FieldInfo Field(object state, string name) => state.GetType().GetField(name, Instance);
    static Clipper64 Engine(object state) => (Clipper64)Field(state, "engine").GetValue(state);
    static bool Busy(object state) => (bool)Field(state, "inUse").GetValue(state);
    static object Invoke(MethodInfo method, params object[] arguments)
    {
        try { return method.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static IDisposable Begin() => (IDisposable)Invoke(BeginMethod);
    static Paths64 Resolve(Paths64 a, Paths64 b = null, ClipType operation = ClipType.Union, FillRule rule = FillRule.NonZero)
        => (Paths64)Invoke(ResolveMethod, a, b, operation, rule);
    static Paths64 Fresh(Paths64 a, Paths64 b, ClipType operation, FillRule rule)
    {
        if (a.Count == 0 && (b == null || b.Count == 0)) return new Paths64();
        var engine = new Clipper64();
        try
        {
            engine.AddSubject(a); if (b != null) engine.AddClip(b);
            var result = new Paths64();
            if (!engine.Execute(operation, rule, result)) throw new InvalidOperationException("Reference Clipper failed.");
            return result;
        }
        finally { engine.Clear(); }
    }
    // Deliberately retain path order, winding and starting vertex: centered dash phase depends on them.
    static string Exact(Paths64 paths)
    {
        var text = new StringBuilder().Append(paths.Count).Append(':');
        foreach (var path in paths)
        {
            text.Append('[').Append(path.Count).Append(':');
            foreach (var point in path) text.Append(point.X).Append(',').Append(point.Y).Append(';');
            text.Append(']');
        }
        return text.ToString();
    }
    static Path64 Rectangle(long x, long y, long size) => new Path64
    {
        new Point64(x, y), new Point64(x + size, y),
        new Point64(x + size, y + size), new Point64(x, y + size)
    };
    static Paths64 Fixture(int seed)
    {
        if (seed == 0) return new Paths64();
        if (seed == 1) return new Paths64 { new Path64(), new Path64 { new Point64(4, 4), new Point64(4, 4) } };
        if (seed == 2)
        {
            var hole = Rectangle(4, 4, 8); hole.Reverse();
            return new Paths64 { Rectangle(0, 0, 16), hole };
        }
        if (seed == 3) return new Paths64 { Rectangle(0, 0, 16), Rectangle(16, 0, 16) };
        if (seed == 4) return new Paths64 { new Path64
        {
            new Point64(-20, -20), new Point64(20, 20), new Point64(-20, 20), new Point64(20, -20)
        } };
        if (seed == 5) return new Paths64 { Rectangle(-40000000000L, 40000000000L, 4096) };
        var random = new Random(seed * 173 + 17);
        var result = new Paths64();
        for (int p = 0; p < 1 + seed % 3; p++)
        {
            var path = new Path64();
            for (int i = 0; i < 6 + seed % 7; i++) path.Add(new Point64(random.Next(-128, 129), random.Next(-128, 129)));
            if ((seed & 1) != 0) path.Reverse();
            result.Add(path);
        }
        return result;
    }
    static string Failure(Action action)
    {
        try { action(); return null; }
        catch (InvalidOperationException error) { return error.Message; }
    }

    public static string Run() => TestContext.Run("Scoped Clipper reuse: exact output, ownership, nesting and cleanup", t =>
    {
        t.True(Current == null, "Test starts outside any geometry build");
        var retained = new List<(Paths64 paths, string frozen)>();
        object owner;
        Clipper64 pooled;
        using (Begin())
        {
            owner = Current;
            t.True(owner != null && Engine(owner) == null, "BeginBuild is lazy");
            using (Begin())
            {
                t.True(ReferenceEquals(owner, Current), "Nested build borrows its outer owner");
                t.Equal(0, Resolve(new Paths64()).Count, "Empty resolve stays empty");
                t.True(Engine(owner) == null && !Busy(owner), "Empty resolve does not create or lease an engine afterward");
            }
            t.True(ReferenceEquals(owner, Current), "Nested dispose preserves outer state");
            var first = Resolve(Fixture(2)); pooled = Engine(owner);
            retained.Add((first, Exact(first)));
            t.True(pooled != null && pooled.GetBounds().IsEmpty() && !Busy(owner), "Resolve clears its inputs and returns the engine");

            for (int seed = 0; seed < 24; seed++)
            foreach (var operation in new[] { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor })
            foreach (var rule in new[] { FillRule.EvenOdd, FillRule.NonZero, FillRule.Positive, FillRule.Negative })
            {
                var a = Fixture(seed); var b = seed % 3 == 0 ? null : Fixture((seed + 1) % 24);
                string aBefore = Exact(a), bBefore = b == null ? null : Exact(b);
                Paths64 actual;
                using (Begin()) actual = Resolve(a, b, operation, rule);
                string label = seed + " " + operation + " " + rule;
                t.Equal(Exact(Fresh(a, b, operation, rule)), Exact(actual), "Exact fresh-engine parity " + label);
                t.Equal(aBefore, Exact(a), "Subject remains unchanged " + label);
                t.Equal(bBefore, b == null ? null : Exact(b), "Clip remains unchanged " + label);
                t.True(ReferenceEquals(pooled, Engine(owner)) && !Busy(owner) && pooled.GetBounds().IsEmpty(), "One cleared engine per build " + label);
                retained.Add((actual, Exact(actual)));
            }
            var emptyA = Resolve(new Paths64(), new Paths64()); var emptyB = Resolve(new Paths64());
            t.True(!ReferenceEquals(emptyA, emptyB) && emptyA.Count == 0 && emptyB.Count == 0, "Empty outputs are independently owned");
            t.Equal(Exact(Fresh(first, Fixture(3), ClipType.Xor, FillRule.NonZero)),
                Exact(Resolve(first, Fixture(3), ClipType.Xor)), "Previous output can become an input");

            // Clipper has no callback in this vendored build. Model an in-flight lease
            // with pending input to verify a reentrant call cannot clear or execute it.
            var pending = Fixture(2);
            pooled.AddSubject(pending);
            Field(owner, "inUse").SetValue(owner, true);
            try
            {
                using (Begin())
                    t.Equal(Exact(Fresh(Fixture(4), Fixture(3), ClipType.Union, FillRule.EvenOdd)),
                        Exact(Resolve(Fixture(4), Fixture(3), ClipType.Union, FillRule.EvenOdd)), "Reentrant lease uses an isolated engine");
                t.True(Busy(owner) && ReferenceEquals(pooled, Engine(owner)), "Reentrant resolve preserves outer lease");
                var pendingResult = new Paths64();
                t.True(pooled.Execute(ClipType.Union, FillRule.NonZero, pendingResult), "Outer pending operation still executes");
                t.Equal(Exact(Fresh(pending, null, ClipType.Union, FillRule.NonZero)), Exact(pendingResult), "Reentrant call preserves outer inputs");
            }
            finally { pooled.Clear(); Field(owner, "inUse").SetValue(owner, false); }

            // Force the existing workload rejection after acquiring a previously used
            // scoped engine, proving finally cleanup on an exception as well as success.
            var dense = new Path64(); for (int i = 0; i < 3000; i++) dense.Add(new Point64(i % 2, i));
            pooled.AddSubject(pending);
            string failure = Failure(() => Resolve(new Paths64 { dense }));
            t.True(failure != null && failure.Contains("polygon intersection workload"), "Budget rejection stays explicit");
            t.True(!Busy(owner) && pooled.GetBounds().IsEmpty(), "Failure clears inputs and releases the lease");
            t.True(Failure(() => Resolve(pending, operation: ClipType.NoClip)) != null, "NoClip retains fresh-engine failure after prior successes");
            t.True(ReferenceEquals(pooled, Engine(owner)) && !Busy(owner), "Failed operation does not replace or strand the scoped engine");
            t.Equal("nested sentinel", Failure(() => { using (Begin()) throw new InvalidOperationException("nested sentinel"); }), "Nested exception unwinds");
            t.True(ReferenceEquals(owner, Current), "Nested exception preserves outer owner");
            t.Equal(Exact(Fresh(pending, null, ClipType.Union, FillRule.NonZero)), Exact(Resolve(pending)), "Build can continue after failures");
        }
        t.True(Current == null && Engine(owner) == null, "Outer dispose drops the thread-local engine reference");
        foreach (var saved in retained) t.Equal(saved.frozen, Exact(saved.paths), "Previous outputs survive reuse and scope disposal");
        using (Begin())
        {
            var next = Current;
            t.True(!ReferenceEquals(owner, next) && Engine(next) == null, "Next build starts without retained storage");
            Resolve(Fixture(3));
            t.True(!ReferenceEquals(pooled, Engine(next)), "Engines are not retained across builds");
            ((IDisposable)owner).Dispose();
            t.True(ReferenceEquals(next, Current), "Repeated old-owner disposal cannot detach a newer build");
        }
        object throwingOwner = null;
        t.Equal("outer sentinel", Failure(() =>
        {
            using (Begin())
            {
                throwingOwner = Current; Resolve(Fixture(3));
                throw new InvalidOperationException("outer sentinel");
            }
        }), "Outer exception unwinds");
        t.True(Current == null && Engine(throwingOwner) == null, "Outer exception releases scoped storage");
        t.Equal(Exact(Fresh(Fixture(2), null, ClipType.Union, FillRule.NonZero)), Exact(Resolve(Fixture(2))), "Unscoped resolve retains fresh-engine output");
        t.True(Failure(() => Resolve(Fixture(2), operation: ClipType.NoClip)) != null, "Unscoped NoClip still fails");
        t.True(Current == null, "Unscoped calls do not create retained build state");
    });
}
