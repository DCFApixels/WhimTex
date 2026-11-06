using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Only executable shaders are shared. Materials, bindings and buffer images belong to runtimes.
    [InitializeOnLoad]
    internal static class ShaderExecutableCache
    {
        internal sealed class Entry
        {
            internal readonly string source;
            internal readonly int revision;
            internal Shader shader;
            internal Material compilerMaterial;
            internal Exception error;
            internal int owners;
            internal bool cached = true, started, ready, priority;
            internal long used;
            internal readonly List<ShaderGraphPreparation.Priority> requests = new();
            internal float Rank
            {
                get
                {
                    float rank = float.PositiveInfinity;
                    foreach (var request in requests) rank = Math.Min(rank, request.Rank);
                    return requests.Count == 0 ? (priority ? -1 : float.MaxValue) : rank;
                }
            }
            internal Entry(string source, bool priority)
            { this.source = source; this.priority = priority; revision = ShaderProgramSourceRevision.Current; }
            internal void Retain() { owners++; used = ++clock; }
            internal void Release()
            {
                if (--owners != 0) return;
                if (!started || ready && (!cached || error != null || shader == null || ShaderUtil.ShaderHasError(shader))) Remove(this);
                Trim();
            }
        }
        const int MaximumEntries = 48;
        const long MaximumSourceBytes = 24 * 1024 * 1024;
        static readonly Dictionary<string, Entry> cache = new(StringComparer.Ordinal);
        static readonly List<Entry> entries = new();
        sealed class Prepared
        {
            internal string source;
            internal ShaderSourceAnalysis analysis;
            internal long used;
            internal long bytes;
        }
        static readonly Dictionary<(string code, string declarations, string path, bool effect), Prepared> prepared = new();
        static readonly object preparedLock = new();
        static long preparedClock;
        static int preparedRevision;
        static long preparedBytes;
        internal static string Prepare(ShaderGraphCompilation compilation, bool graph, string path, bool effect,
            out ShaderSourceAnalysis analysis)
        {
            int sourceRevision = ShaderProgramSourceRevision.Current;
            // Code and external function dependencies must still be resolved on each requested build.
            bool cacheable = graph && !compilation.bufferExternalCode;
            var signature = new StringBuilder();
            if (cacheable) foreach (var p in compilation.shaderParameters)
                signature.Append(p.ShaderId).Append('\0').Append(p.name).Append('\0').Append((int)p.type).Append(':').Append(p.ValueType).Append('\0');
            var key = (compilation.ExecutableSource, signature.ToString(), path, effect);
            lock (preparedLock)
            {
                if (preparedRevision != sourceRevision)
                { prepared.Clear(); preparedBytes = 0; preparedRevision = sourceRevision; }
                if (cacheable && prepared.TryGetValue(key, out var hit))
                { hit.used = ++preparedClock; analysis = hit.analysis; return hit.source; }
            }
            string compilerPath = cacheable ? "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl" : path;
            string source = ShaderSourceBuilder.BuildProgram(compilation.ExecutableSource, compilation.shaderParameters, compilerPath,
                !graph && !effect, graph ? ShaderGraphSources.MaximumArchiveBytes + 1024 * 1024 : ShaderSourceBuilder.MaximumCharacters,
                out analysis, noiseLibrary: graph ? ShaderGraphNoiseLibrary.ForGeneratedSource(compilation.ExecutableSource) : null);
            long bytes = 2L * (compilation.ExecutableSource.Length + key.Item2.Length + (path?.Length ?? 0) + source.Length);
            lock (preparedLock)
            if (cacheable && bytes <= MaximumSourceBytes && sourceRevision == ShaderProgramSourceRevision.Current && preparedRevision == sourceRevision)
            {
                if (prepared.TryGetValue(key, out var existing))
                { existing.used = ++preparedClock; analysis = existing.analysis; return existing.source; }
                while (prepared.Count > 0 && (prepared.Count >= MaximumEntries || preparedBytes + bytes > MaximumSourceBytes))
                {
                    var oldest = key; long age = long.MaxValue;
                    foreach (var pair in prepared) if (pair.Value.used < age) { age = pair.Value.used; oldest = pair.Key; }
                    preparedBytes -= prepared[oldest].bytes; prepared.Remove(oldest);
                }
                prepared[key] = new Prepared { source = source, analysis = analysis, used = ++preparedClock, bytes = bytes };
                preparedBytes += bytes;
            }
            return source;
        }
        static int revision;
        static long clock, sourceBytes;
        internal static int ActiveCount { get; private set; }
        internal static long NativeCompilations { get; private set; }
        internal static long CacheHits { get; private set; }
        static ShaderExecutableCache()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }
        internal static Entry Acquire(string source, bool synchronous, bool priority, ShaderGraphPreparation.Priority request = null)
        {
            WhimTexHueGradient.Ensure();
            BlueNoiseTextures.EnsureShaderGlobals();
            if (revision != ShaderProgramSourceRevision.Current)
            {
                revision = ShaderProgramSourceRevision.Current;
                cache.Clear(); sourceBytes = 0;
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    var old = entries[i]; old.cached = false;
                    if (old.owners == 0 && (!old.started || old.ready)) Remove(old);
                }
            }
            if (cache.TryGetValue(source, out var entry)) CacheHits++;
            else
            {
                entry = new Entry(source, priority);
                cache.Add(source, entry); entries.Add(entry); sourceBytes += source.Length * 2L;
            }
            entry.priority |= priority;
            if (request != null) entry.requests.Add(request);
            entry.Retain();
            try
            {
                if (synchronous) Start(entry, true);
                Trim(); return entry;
            }
            catch { if (request != null) entry.requests.Remove(request); entry.Release(); throw; }
        }
        internal static bool IsReady(Entry entry)
        {
            if (entry.error != null) throw entry.error;
            if (!entry.started) return false;
            if (!entry.ready && (entry.shader == null || entry.compilerMaterial.passCount == 0 ||
                ShaderUtil.ShaderHasError(entry.shader) || ShaderUtil.IsPassCompiled(entry.compilerMaterial, 0)))
            {
                entry.ready = true; ActiveCount--;
                if (entry.compilerMaterial != null) UnityEngine.Object.DestroyImmediate(entry.compilerMaterial);
                entry.compilerMaterial = null;
            }
            return entry.ready;
        }
        static void Start(Entry entry, bool synchronous)
        {
            if (entry.ready) return;
            using var profile = WhimTexProfiler.Measure("shader.compile.native", detail: synchronous ? "explicit" : "queued");
            bool previous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = !synchronous;
                if (!entry.started)
                {
                    entry.shader = ShaderUtil.CreateShaderAsset(entry.source, false);
                    if (entry.shader == null) throw new InvalidOperationException("Unity could not create the graph shader.");
                    entry.shader.hideFlags = HideFlags.HideAndDontSave;
                    entry.compilerMaterial = new Material(entry.shader) { hideFlags = HideFlags.HideAndDontSave };
                    entry.started = true; ActiveCount++; NativeCompilations++;
                }
                if (entry.compilerMaterial.passCount > 0) ShaderUtil.CompilePass(entry.compilerMaterial, 0, synchronous);
                IsReady(entry);
            }
            catch (Exception error)
            {
                entry.error = error;
                if (entry.started && !entry.ready) ActiveCount--;
                entry.ready = true;
                if (entry.compilerMaterial != null) UnityEngine.Object.DestroyImmediate(entry.compilerMaterial);
                entry.compilerMaterial = null;
            }
            finally { ShaderUtil.allowAsyncCompilation = previous; }
        }
        static void Update()
        {
            // Prepare main results before choosing among their requests and node previews.
            ShaderProgramUpdate.Update();
            ShaderGraphPreparation.Update();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.started && !entry.ready) IsReady(entry);
                if (entry.owners == 0 && entry.ready && !entry.cached) Remove(entry);
            }
            if (ActiveCount < 2)
            {
                Entry next = null;
                foreach (var entry in entries)
                    if (!entry.started && entry.owners > 0 &&
                        (entry.Rank < 0 || !ShaderGraphPreparation.HasPendingMain) &&
                        (next == null || entry.Rank < next.Rank)) next = entry;
                // One submission per editor update also bounds front-end work on the main thread.
                if (next != null) Start(next, false);
            }
            Trim();
        }
        static void Trim()
        {
            while (cache.Count > MaximumEntries || sourceBytes > MaximumSourceBytes)
            {
                Entry oldest = null;
                foreach (var entry in entries)
                    if (entry.cached && entry.ready && entry.owners == 0 && (oldest == null || entry.used < oldest.used)) oldest = entry;
                if (oldest == null) break;
                Remove(oldest);
            }
        }
        static void Remove(Entry entry)
        {
            if (entry.cached && cache.TryGetValue(entry.source, out var cached) && ReferenceEquals(cached, entry))
            { cache.Remove(entry.source); sourceBytes -= entry.source.Length * 2L; }
            entry.cached = false;
            entries.Remove(entry);
            if (entry.compilerMaterial != null) UnityEngine.Object.DestroyImmediate(entry.compilerMaterial);
            if (entry.shader != null) UnityEngine.Object.DestroyImmediate(entry.shader);
            entry.compilerMaterial = null; entry.shader = null;
        }
        static void Clear()
        {
            ShaderProgramUpdate.Clear();
            ShaderGraphPreparation.Clear();
            for (int i = entries.Count - 1; i >= 0; i--) Remove(entries[i]);
            cache.Clear(); sourceBytes = 0; ActiveCount = 0;
            lock (preparedLock) { prepared.Clear(); preparedBytes = 0; }
        }
        internal static void Initialize() { }
    }
}
