using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Immutable rendered passes shared only within one document. Active runtimes own
    // leases; unused entries are retained under a bounded memory/entry budget.
    internal sealed class ShaderGraphBufferCache : IDisposable
    {
        internal readonly struct Key : IEquatable<Key>
        {
            readonly ShaderExecutableCache.Entry program, surface;
            readonly ulong stamp;
            internal readonly int width, height;
            readonly ShaderGraphWrap wrap;
            readonly bool caustics;
            internal Key(ShaderExecutableCache.Entry program, ShaderExecutableCache.Entry surface, ulong stamp,
                int width, int height, ShaderGraphWrap wrap, bool caustics)
            { this.program = program; this.surface = surface; this.stamp = stamp; this.width = width; this.height = height; this.wrap = wrap; this.caustics = caustics; }
            public bool Equals(Key other) => ReferenceEquals(program, other.program) && ReferenceEquals(surface, other.surface) &&
                stamp == other.stamp && width == other.width && height == other.height && wrap == other.wrap && caustics == other.caustics;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => HashCode.Combine(program, surface, stamp, width, height, wrap, caustics);
        }
        internal sealed class Entry
        {
            internal Key key;
            internal readonly ShaderGraphBufferCache owner;
            internal RenderTexture texture;
            internal bool valid, cached = true;
            internal int owners;
            internal long used;
            internal Entry(ShaderGraphBufferCache owner, Key key)
            { this.owner = owner; this.key = key; texture = CreateTexture(key.width, key.height); }
            internal void Release()
            {
                if (--owners != 0) return;
                if (!cached) Destroy(); else owner.Trim();
            }
            internal void Destroy()
            {
                if (texture != null) { WhimTexProfiler.ReleaseTexture(texture); UnityEngine.Object.DestroyImmediate(texture); }
                texture = null; valid = false;
            }
        }
        const int MaximumEntries = 48;
        const long MaximumBytes = 64L * 1024 * 1024;
        readonly Dictionary<Key, Entry> entries = new();
        long bytes, clock;
        internal Entry Acquire(Key key)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                // Recycle an unleased image before allocating. Animated keys change each
                // frame; cache sharing must not turn them into per-frame GPU allocations.
                foreach (var candidate in entries.Values)
                    if (candidate.owners == 0 && candidate.key.width == key.width && candidate.key.height == key.height &&
                        (entry == null || candidate.used < entry.used)) entry = candidate;
                if (entry != null)
                { entries.Remove(entry.key); entry.key = key; entry.valid = false; }
                else { entry = new Entry(this, key); bytes += 16L * key.width * key.height; }
                entries.Add(key, entry);
            }
            if (entry.texture == null || !entry.texture.IsCreated())
            { entry.Destroy(); entry.texture = CreateTexture(key.width, key.height); }
            entry.owners++; entry.used = ++clock; Trim(); return entry;
        }
        void Trim()
        {
            while (entries.Count > MaximumEntries || bytes > MaximumBytes)
            {
                Entry oldest = null;
                foreach (var entry in entries.Values)
                    if (entry.owners == 0 && (oldest == null || entry.used < oldest.used)) oldest = entry;
                if (oldest == null) break;
                entries.Remove(oldest.key); bytes -= 16L * oldest.key.width * oldest.key.height;
                oldest.cached = false; oldest.Destroy();
            }
        }
        internal static RenderTexture CreateTexture(int width, int height)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                hideFlags = HideFlags.HideAndDontSave, name = "Shader Graph Buffer",
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false
            };
            if (!texture.Create()) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidOperationException("Cannot allocate Shader Graph Buffer."); }
            WhimTexProfiler.TrackTexture(texture, "shader graph buffer"); return texture;
        }
        public void Dispose()
        {
            foreach (var entry in entries.Values) { entry.cached = false; if (entry.owners == 0) entry.Destroy(); }
            entries.Clear(); bytes = 0;
        }
    }

    public partial class WhimTexDocument
    {
        [NonSerialized] ShaderGraphBufferCache shaderBuffers;
        internal ShaderGraphBufferCache ShaderBuffers => shaderBuffers ??= new ShaderGraphBufferCache();
        void ReleaseShaderBuffers() { shaderBuffers?.Dispose(); shaderBuffers = null; }
    }
}
