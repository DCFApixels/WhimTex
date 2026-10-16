using System;
using System.Diagnostics;

namespace DCFApixels.WhimTex
{
    // Opt-in main-thread diagnostics. Disabled in normal rendering; no per-pixel instrumentation.
    internal static class DistanceFieldTiming
    {
        internal static Action<string, double> receiver;
        // Reserve the event on entry; disposal fills that same slot after its children.
        internal static Scope Measure(string phase) => new Scope(phase);
        internal readonly struct Scope : IDisposable
        {
            readonly string phase;
            readonly long start;
            readonly Action<string, double> callback;
            readonly WhimTexProfiler.Scope profiling;
            internal Scope(string phase)
            {
                this.phase = phase;
                profiling = WhimTexProfiler.Measure(phase);
                callback = receiver;
                start = callback == null ? 0 : Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                try
                {
                    if (callback != null)
                        callback(phase, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                }
                finally { profiling.Dispose(); }
            }
        }
    }
}
