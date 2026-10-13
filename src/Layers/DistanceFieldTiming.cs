using System;
using System.Diagnostics;

namespace DCFApixels.WhimTex
{
    // Opt-in main-thread diagnostics. Disabled in normal rendering; no per-pixel instrumentation.
    internal static class DistanceFieldTiming
    {
        internal static Action<string, double> receiver;
        internal static long Start() => receiver == null ? 0 : Stopwatch.GetTimestamp();
        internal static void Report(string phase, ref long start)
        {
            if (start == 0) return;
            long end = Stopwatch.GetTimestamp();
            receiver?.Invoke(phase, (end - start) * 1000.0 / Stopwatch.Frequency);
            start = end;
        }
        internal static Scope Measure(string phase) => new Scope(phase);
        internal readonly struct Scope : IDisposable
        {
            readonly string phase;
            readonly long start;
            readonly Action<string, double> callback;
            internal Scope(string phase)
            {
                this.phase = phase;
                callback = receiver;
                start = callback == null ? 0 : Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                if (callback != null)
                    callback(phase, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }
        }
    }
}
