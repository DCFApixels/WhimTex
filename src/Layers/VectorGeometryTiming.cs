using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DCFApixels.WhimTex
{
    // A scoped, opt-in benchmark sink. It never replaces a user's profiler capture.
    // Normal editor work records only the existing profiler scopes, with no sink allocation.
    internal static class VectorGeometryTiming
    {
        [ThreadStatic] private static Session current;
        internal sealed class Session : IDisposable
        {
            internal readonly Dictionary<string, long> ticks = new(), calls = new();
            private readonly Session previous;
            internal Session() { previous = current; current = this; }
            public void Dispose() { current = previous; }
        }
        internal static Session Begin() => new();
        internal static Scope Measure(string name) => new(name);
        internal readonly struct Scope : IDisposable
        {
            private readonly WhimTexProfiler.Scope profiler;
            private readonly Session owner;
            private readonly string name;
            private readonly long start;
            internal Scope(string name)
            {
                profiler = WhimTexProfiler.Measure(name);
                owner = current; this.name = name;
                start = owner == null ? 0 : Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                if (owner != null)
                {
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    owner.ticks.TryGetValue(name, out long ticks);
                    owner.calls.TryGetValue(name, out long calls);
                    owner.ticks[name] = ticks + elapsed; owner.calls[name] = calls + 1;
                }
                profiler.Dispose();
            }
        }
    }
}
