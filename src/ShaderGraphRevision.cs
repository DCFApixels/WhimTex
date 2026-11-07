using System;
using System.Runtime.CompilerServices;

namespace DCFApixels.WhimTex
{
    // Only editor-owned, observed graphs retain a stamp across calls. Unobserved
    // mutable graphs (including public API callers and compiler snapshots) are read directly.
    internal static class ShaderGraphRevision
    {
        sealed class State { internal int readers; internal bool dirty = true; internal ulong fingerprint; }
        static readonly ConditionalWeakTable<ShaderGraph, State> states = new();
        internal static void Observe(ShaderGraph graph)
        { if (graph != null) { var state = states.GetOrCreateValue(graph); state.readers++; state.dirty = true; } }
        internal static void Release(ShaderGraph graph)
        { if (graph != null && states.TryGetValue(graph, out var state) && --state.readers <= 0) states.Remove(graph); }
        internal static void Invalidate(ShaderGraph graph)
        { if (graph != null && states.TryGetValue(graph, out var state)) state.dirty = true; }
        // Public graph fields remain mutable. One observer poll detects callers which
        // did not send a document notification, instead of hashing at every consumer.
        internal static bool Poll(ShaderGraph graph)
        {
            if (graph == null || !states.TryGetValue(graph, out var state)) return false;
            ulong next = ShaderGraphCompiler.CodeFingerprint(graph);
            bool changed = !state.dirty && state.fingerprint != next;
            state.fingerprint = next; state.dirty = false;
            return changed;
        }
        internal static ulong Fingerprint(ShaderGraph graph)
        {
            if (graph == null || !states.TryGetValue(graph, out var state) || state.readers == 0)
                return ShaderGraphCompiler.CodeFingerprint(graph);
            if (state.dirty) { state.fingerprint = ShaderGraphCompiler.CodeFingerprint(graph); state.dirty = false; }
            return state.fingerprint;
        }
    }
}
