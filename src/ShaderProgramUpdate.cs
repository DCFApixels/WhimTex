using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    // Coalesces automatic owner updates; explicit Compile/export can still await a complete result.
    internal sealed class ShaderProgramUpdate : IDisposable
    {
        static readonly List<ShaderProgramUpdate> jobs = new();
        static int synchronousScopes;
        internal static bool Synchronous => synchronousScopes != 0;
        internal static bool HasUnprepared
        {
            get { foreach (var job in jobs) if (job.IsPending && job.request == null) return true; return false; }
        }
        internal readonly struct CompleteScope : IDisposable
        {
            readonly bool enabled;
            internal CompleteScope(bool enabled) { this.enabled = enabled; if (enabled) synchronousScopes++; }
            public void Dispose() { if (enabled) synchronousScopes--; }
        }
        internal static CompleteScope CompleteResults() => new CompleteScope(true);
        readonly Func<ShaderProgramRuntime.CompilationRequest> prepare;
        readonly Func<bool> valid;
        readonly Action<ShaderProgramRuntime> accept;
        readonly Action<Exception> fail;
        readonly ulong fingerprint;
        readonly int revision;
        ShaderProgramRuntime.CompilationRequest request;
        internal bool IsPending { get; private set; } = true;
        internal bool Matches(ulong stamp) => IsPending && fingerprint == stamp && revision == ShaderProgramSourceRevision.Current;
        internal ShaderProgramUpdate(ulong fingerprint, Func<ShaderProgramRuntime.CompilationRequest> prepare,
            Func<bool> valid, Action<ShaderProgramRuntime> accept, Action<Exception> fail)
        {
            this.fingerprint = fingerprint; this.prepare = prepare; this.valid = valid; this.accept = accept; this.fail = fail;
            revision = ShaderProgramSourceRevision.Current;
            jobs.Add(this); ShaderExecutableCache.Initialize();
        }
        internal static void Update()
        {
            bool prepared = false;
            // Callbacks may schedule another job, so only visit jobs that existed at entry.
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                var job = jobs[i];
                if (!job.IsPending) { jobs.RemoveAt(i); continue; }
                ShaderProgramRuntime result = null;
                try
                {
                    if (job.revision != ShaderProgramSourceRevision.Current || !job.valid()) { job.Dispose(); continue; }
                    if (job.request == null)
                    {
                        if (prepared) continue;
                        prepared = true; job.request = job.prepare();
                    }
                    if (!job.request.TryComplete(out result)) continue;
                    job.Dispose();
                    if (job.valid()) { job.accept(result); result = null; }
                }
                catch (Exception error)
                {
                    job.Dispose();
                    if (job.valid()) job.fail(error);
                }
                finally { result?.Dispose(); }
            }
        }
        public void Dispose()
        {
            IsPending = false;
            request?.Dispose(); request = null;
        }
        internal static void Clear()
        {
            foreach (var job in jobs) job.Dispose();
            jobs.Clear();
        }
    }
}
