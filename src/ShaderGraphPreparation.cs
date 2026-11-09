using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DCFApixels.WhimTex
{
    // One CPU worker owns prepared graphs. Unity shader/material creation stays in the editor update.
    internal static class ShaderGraphPreparation
    {
        internal sealed class Priority
        {
            internal readonly bool main;
            internal float previewRank;
            // Evaluated only by the main-thread queues, never by the CPU worker.
            internal Func<bool> wanted;
            internal bool IsWanted => wanted == null || wanted();
            internal Priority(bool main) { this.main = main; }
            internal float Rank => main ? -1 : previewRank;
        }
        sealed class Sessions { internal Session layer, effect; }
        internal sealed class Session
        {
            internal ShaderGraph graph;
            internal ulong fingerprint;
            internal ulong snapshotFingerprint;
            internal int revision;
            internal int requests;
            internal bool effect;
            internal ShaderPreparationEnvironment environment;
            internal ShaderGraphCompiler.Prepared prepared;
            internal Exception error;
        }
        internal sealed class Job : IDisposable
        {
            internal Session session;
            internal Priority priority;
            internal string node, port, path;
            internal ShaderGraphCompilation compilation;
            internal string source;
            internal ShaderSourceAnalysis analysis;
            internal Exception error;
            internal volatile bool complete, cancelled;
            internal bool started;
            internal int pendingMain;
            internal bool simplifiedLighting, hybrid;
            internal ShaderGraphPreviewVM vm;
            internal void Run()
            {
                ShaderPreparationEnvironment.Current = session.environment;
                try
                {
                    if (cancelled) return;
                    if (session.error != null) throw session.error;
                    if (session.prepared == null)
                    {
                        try { session.prepared = ShaderGraphCompiler.Prepare(session.graph, session.effect); }
                        catch (Exception e) { session.error = e; throw; }
                    }
                    if (cancelled) return;
                    if (hybrid)
                    {
                        if (ShaderGraphPreviewVM.TryCreatePrepared(session.prepared, node, port, simplifiedLighting, out vm)) return;
                        // FX cards support pure SVM; native producers still use their existing FX path.
                        if (session.effect) return;
                    }
                    compilation = session.prepared.CompileCancellable(node, port, simplifiedLighting, IsCancelled);
                    if (cancelled) return;
                    if (hybrid)
                    {
                        if (!ShaderGraphPreviewVM.TryCreateFromCompilation(session.graph, session.prepared, node, port, compilation, out vm)) return;
                        compilation = vm.hybridCompilation;
                    }
                    source = ShaderExecutableCache.Prepare(compilation, true, path, session.effect, out analysis);
                }
                catch (OperationCanceledException) when (cancelled) { }
                catch (Exception e) { error = e; }
                finally { ShaderPreparationEnvironment.Current = null; complete = true; ReleaseMain(); }
            }
            bool IsCancelled() => cancelled;
            void ReleaseMain()
            {
                if (System.Threading.Interlocked.Exchange(ref pendingMain, 0) != 0)
                    System.Threading.Interlocked.Decrement(ref mainRequests);
            }
            public void Dispose()
            {
                if (cancelled) return;
                cancelled = true;
                session.requests--;
                ReleaseMain();
            }
        }
        static ConditionalWeakTable<ShaderGraph, Sessions> sessions = new();
        static readonly List<Job> pending = new();
        static Job running;
        static ShaderPreparationEnvironment environment;
        static int environmentRevision = -1;
        static int mainRequests;
        internal static bool HasPendingMain => System.Threading.Volatile.Read(ref mainRequests) != 0 || ShaderProgramUpdate.HasUnprepared;
        internal static Job Enqueue(ShaderGraph graph, string node, string port, string path, bool effect, Priority priority)
            => Enqueue(Capture(graph, effect), node, port, path, priority);

        // General callers validate live data. The editor can explicitly retain this immutable
        // snapshot until its next edit, so a batch of previews does not rescan the graph per node.
        internal static Session Capture(ShaderGraph graph, bool effect)
        {
            ShaderExecutableCache.Initialize();
            int revision = ShaderProgramSourceRevision.Current;
            if (environment == null || environmentRevision != revision)
            { environment = new ShaderPreparationEnvironment(); environmentRevision = revision; }
            ulong fingerprint = ShaderGraphCompiler.CodeFingerprint(graph);
            ulong snapshotFingerprint = SnapshotFingerprint(graph, fingerprint);
            var slot = sessions.GetOrCreateValue(graph);
            var session = effect ? slot.effect : slot.layer;
            if (session == null || session.requests == 0 || session.snapshotFingerprint != snapshotFingerprint || session.revision != revision)
            {
                var copy = ShaderProgramStorage.CopyWorkingGraph(graph);
                if (graph.activeSubGraph != null) copy = copy.SubGraphView(copy.FindSubGraph(graph.activeSubGraph.id));
                session = new Session { graph = copy, fingerprint = fingerprint, snapshotFingerprint = snapshotFingerprint, revision = revision, effect = effect, environment = environment };
                if (effect) slot.effect = session; else slot.layer = session;
            }
            return session;
        }
        internal static Job Enqueue(Session session, string node, string port, string path, Priority priority, bool simplifiedLighting = false)
        {
            if (session.revision != ShaderProgramSourceRevision.Current)
                throw new InvalidOperationException("The shader preparation snapshot is no longer current.");
            var job = new Job { session = session, node = node, port = port, path = path, priority = priority, simplifiedLighting = simplifiedLighting };
            session.requests++;
            if (priority.main) { job.pendingMain = 1; System.Threading.Interlocked.Increment(ref mainRequests); }
            pending.Add(job);
            return job;
        }
        // Values and authoring metadata can change without changing executable code.
        // Existing jobs keep their immutable snapshot; subsequent requests capture the current data.
        internal static void Invalidate(ShaderGraph graph) { if (graph != null) sessions.Remove(graph); }
        static ulong SnapshotFingerprint(ShaderGraph graph, ulong hash)
        {
            // Executable stamps deliberately omit uniforms and control metadata. Validation and
            // authoring declarations still consume them, so a pending batch must distinguish edits.
            void Text(string text)
            { if (text != null) foreach (char c in text) hash = RenderFingerprint.Mix(hash, c); hash = RenderFingerprint.Mix(hash, 255); }
            void Value(UnityEngine.Vector4 value)
            { for (int i = 0; i < 4; i++) hash = RenderFingerprint.Float(hash, value[i]); }
            void Parameter(ShaderParameter parameter)
            {
                hash = ShaderFX.HashParameter(hash, parameter);
                if (parameter?.controls != null) hash = RenderFingerprint.Mix(hash, ShaderGraphParameters.MetadataFingerprint(parameter));
            }
            if (graph.parameters != null) foreach (var parameter in graph.parameters) Parameter(parameter);
            foreach (var node in graph.AllNodes())
            {
                Value(node.position); Value(node.codeSize);
                if (node.inputs != null) foreach (var input in node.inputs)
                    if (input != null) { Value(input.value); Parameter(input.resource); }
            }
            if (graph.subGraphs != null) foreach (var definition in graph.subGraphs)
            {
                if (definition == null) continue;
                Text(definition.name);
                void Pins(List<ShaderGraphInterfacePort> pins)
                { if (pins != null) foreach (var pin in pins) if (pin != null) { Text(pin.name); Value(pin.value); } }
                Pins(definition.inputs); Pins(definition.outputs);
            }
            if (graph.functions != null) foreach (var function in graph.functions)
            {
                if (function == null) continue;
                Text(function.name);
                if (function.ports != null) foreach (var port in function.ports)
                    if (port != null)
                    {
                        Value(port.value); Text(port.label);
                        hash = RenderFingerprint.Mix(hash, port.hasRange ? 1UL : 0);
                        hash = RenderFingerprint.Float(hash, port.minimum); hash = RenderFingerprint.Float(hash, port.maximum);
                    }
            }
            return hash;
        }
        internal static void Update()
        {
            if (running != null && !running.complete) return;
            running = null;
            Job next = null;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var job = pending[i];
                if (job.cancelled || job.session.revision != ShaderProgramSourceRevision.Current)
                { job.Dispose(); pending.RemoveAt(i); continue; }
                if (!job.priority.IsWanted) continue;
                if (next == null || job.priority.Rank <= next.priority.Rank) next = job;
            }
            if (next == null) return;
            if (!next.priority.main && ShaderProgramUpdate.HasUnprepared) return;
            pending.Remove(next); running = next; next.started = true;
            _ = Task.Run(next.Run);
        }
        internal static void Clear()
        {
            foreach (var job in pending) job.Dispose();
            pending.Clear(); running?.Dispose();
            sessions = new ConditionalWeakTable<ShaderGraph, Sessions>();
            environment = null;
        }
    }
}
