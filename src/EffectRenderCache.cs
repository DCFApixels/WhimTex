using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Window-owned derived data. Never serialized, registered with Undo, or saved in documents.
    internal sealed class EffectRenderCache : IDisposable
    {
        internal const long DefaultBudget = 256L * 1024 * 1024;
        private sealed class Entry
        {
            internal string key;
            internal ulong stamp;
            internal float scale;
            internal bool interactive, alphaOnly;
            internal RenderTexture pixels, errors;
            internal long bytes, used;
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly Dictionary<Layer, ulong> stamps = new Dictionary<Layer, ulong>();
        private readonly HashSet<Layer> visiting = new HashSet<Layer>();
        private readonly HashSet<Layer> colorSources = new HashSet<Layer>();
        private readonly HashSet<string> requiredEntries = new HashSet<string>();
        private readonly List<Entry> unused = new List<Entry>();
        private TextureCompositor document;
        private DrawingLayerBehaviour liveDrawing;
        private readonly bool snapshotShaders;
        private long clock, frame;
        internal long BudgetBytes { get; set; } = DefaultBudget;
        internal long Bytes { get; private set; }
        internal int Count => entries.Count;
        internal int Hits { get; private set; }
        internal int Misses { get; private set; }

        internal EffectRenderCache() : this(false) { }
        internal EffectRenderCache(bool snapshotShaders) => this.snapshotShaders = snapshotShaders;

        internal void BeginFrame(TextureCompositor owner, DrawingLayerBehaviour painting = null)
        {
            if (document != owner) { Dispose(); document = owner; }
            owner.RefreshTransformHierarchy();
            liveDrawing = painting;
            frame++;
            stamps.Clear(); visiting.Clear(); colorSources.Clear(); requiredEntries.Clear();
            VisitVisible(owner.layers);
            visiting.Clear();
            unused.Clear();
            foreach (Entry entry in entries.Values)
                if (!requiredEntries.Contains(entry.key)) unused.Add(entry);
            foreach (Entry entry in unused) Remove(entry);
            unused.Clear();
            TrimToBudget(0);
        }

        private void VisitVisible(List<Layer> layers)
        {
            if (layers == null) return;
            foreach (Layer layer in layers)
                if (layer != null && layer.enabled) VisitRequired(layer);
        }

        private void VisitRequired(Layer layer)
        {
            if (layer == null || !visiting.Add(layer)) return;
            if (layer?.AsGroup() is Layer group) VisitVisible(group.layers);
            if (layer?.Behaviour is TargetedLayerBehaviour effect)
            {
                RequireEntry(layer, "effect");
                Layer input = Input(effect);
                if (input?.IsGroup == true) RequireEntry(input, "group");
                if (effect.RequiresColorInput) colorSources.Add(input);
                VisitRequired(input);
            }
            if (layer.modifiers != null)
                foreach (var modifier in layer.modifiers)
                    if (modifier is ShaderFX fx)
                        foreach (var parameter in fx.TextureLayerParameters())
                        {
                            Layer input = document.FindLayer(parameter.textureLayerId);
                            if (input == null) continue;
                            RequireEntry(input, "fx-input");
                            colorSources.Add(input);
                            VisitRequired(input);
                        }
            if (layer.clippingMask) VisitRequired(document.GetClippingBase(layer));
        }

        internal bool NeedsColor(Layer group) => colorSources.Contains(group);

        private void RequireEntry(Layer layer, string kind)
        {
            requiredEntries.Add(layer.Id + "/" + kind);
            requiredEntries.Add(layer.Id + "/" + kind + "/debug");
        }

        private Layer Input(TargetedLayerBehaviour effect)
        {
            if (effect.inputMode == EffectInputMode.Specific) return document.FindLayer(effect.TargetLayerId);
            return document.TryFindLayer(effect, out var list, out int index) && index + 1 < list.Count ? list[index + 1] : null;
        }

        internal ulong Stamp(Layer layer)
        {
            if (layer == null) return 1;
            if (!snapshotShaders && layer?.Behaviour is ShaderProcessorLayerBehaviour) return 0;
            if (stamps.TryGetValue(layer, out ulong ready)) return ready;
            if (!visiting.Add(layer)) return 0;
            try
            {
                // Arbitrary material/code FX may depend on time or external resources. Do not memoize them.
                if (!snapshotShaders && layer.modifiers != null)
                    foreach (var modifier in layer.modifiers) if (modifier != null) return stamps[layer] = 0;
                ulong hash = Mix(14695981039346656037UL, layer.transformCache?.version ?? 0);
                string settings = JsonUtility.ToJson(layer);
                foreach (char c in settings) hash = Mix(hash, c);
                if (snapshotShaders && layer.modifiers != null)
                    foreach (var modifier in layer.modifiers)
                    {
                        if (modifier == null) continue;
                        hash = Mix(hash, unchecked((ulong)UnityEditor.EditorUtility.GetDirtyCount(modifier)));
                        if (modifier is ShaderFX shaderFX)
                        {
                            foreach (char c in JsonUtility.ToJson(modifier)) hash = Mix(hash, c);
                            foreach (var parameter in shaderFX.Parameters)
                                if (parameter?.textureValue != null) hash = Mix(hash, parameter.textureValue.updateCount);
                            foreach (var parameter in shaderFX.TextureLayerParameters())
                            {
                                ulong dependency = Stamp(document.FindLayer(parameter.textureLayerId));
                                if (dependency == 0) return stamps[layer] = 0;
                                hash = Mix(hash, dependency);
                            }
                        }
                        if (modifier is Material material)
                        {
                            hash = Mix(hash, unchecked((ulong)(material.shader != null ? UnityEditor.EditorUtility.GetDirtyCount(material.shader) : 0)));
                            foreach (string property in material.GetTexturePropertyNames())
                            {
                                Texture input = material.GetTexture(property);
                                if (input != null)
                                {
                                    hash = Mix(hash, unchecked((ulong)input.GetHashCode()));
                                    hash = Mix(hash, input.updateCount);
                                }
                            }
                        }
                    }
                Texture texture = layer.SamplingSource;
                if (texture != null)
                {
                    hash = Mix(hash, unchecked((ulong)texture.GetHashCode()));
                    hash = Mix(hash, texture.updateCount);
                    hash = Mix(hash, (ulong)texture.width);
                    hash = Mix(hash, (ulong)texture.height);
                    hash = Mix(hash, (ulong)texture.graphicsFormat);
                    hash = Mix(hash, unchecked((ulong)UnityEditor.EditorUtility.GetDirtyCount(texture)));
                    hash = Mix(hash, (ulong)texture.filterMode);
                    hash = Mix(hash, (ulong)texture.wrapModeU);
                    hash = Mix(hash, (ulong)texture.wrapModeV);
                }
                if (ReferenceEquals(layer, liveDrawing?.Owner)) hash = Mix(hash, unchecked((ulong)frame));
                if (layer?.AsGroup() is Layer group && group.layers != null)
                    foreach (Layer child in group.layers)
                    {
                        ulong dependency = Stamp(child);
                        if (dependency == 0) return stamps[layer] = 0;
                        hash = Mix(hash, dependency);
                    }
                if (layer?.Behaviour is TargetedLayerBehaviour effect)
                {
                    ulong dependency = Stamp(Input(effect));
                    if (dependency == 0) return stamps[layer] = 0;
                    hash = Mix(hash, dependency);
                }
                if (snapshotShaders && layer?.Behaviour is ShaderProcessorLayerBehaviour &&
                    document.TryFindLayer(layer, out var container, out int index))
                    for (int i = index + 1; i < container.Count; i++)
                    {
                        ulong dependency = Stamp(container[i]);
                        if (dependency == 0) return stamps[layer] = 0;
                        hash = Mix(hash, dependency);
                    }
                if (layer.clippingMask)
                {
                    ulong dependency = Stamp(document.GetClippingBase(layer));
                    if (dependency == 0) return stamps[layer] = 0;
                    hash = Mix(hash, dependency);
                }
                return stamps[layer] = hash == 0 ? 1 : hash;
            }
            finally { visiting.Remove(layer); }
        }

        private static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

        internal bool TryGet(string key, ulong stamp, int width, int height, float scale, bool interactive,
            bool requireColor, out RenderTexture pixels, out RenderTexture errors, out bool alphaOnly)
        {
            pixels = errors = null; alphaOnly = false;
            if (!entries.TryGetValue(key, out Entry entry) || entry.stamp != stamp || entry.interactive != interactive ||
                entry.scale != scale || entry.pixels == null || !entry.pixels.IsCreated() ||
                entry.errors != null && !entry.errors.IsCreated() ||
                entry.pixels.width != width || entry.pixels.height != height || requireColor && entry.alphaOnly)
            { Misses++; return false; }
            entry.used = ++clock;
            pixels = entry.pixels; errors = entry.errors; alphaOnly = entry.alphaOnly;
            Hits++;
            return true;
        }

        internal void Store(string key, ulong stamp, float scale, bool interactive, bool alphaOnly,
            RenderTexture pixels, RenderTexture errors)
        {
            if (pixels == null || stamp == 0) return;
            if (entries.TryGetValue(key, out Entry old)) Remove(old);
            RenderTextureFormat format = alphaOnly && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBHalf;
            RenderTextureFormat errorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            long area = (long)pixels.width * pixels.height;
            long size = area * (format == RenderTextureFormat.RFloat ? 4 : 8) +
                (errors == null ? 0 : area * (errorFormat == RenderTextureFormat.R8 ? 1 : 4));
            if (size > BudgetBytes) return;
            TrimToBudget(size);
            var entry = new Entry { key = key, stamp = stamp, scale = scale, interactive = interactive,
                alphaOnly = alphaOnly, bytes = size, used = ++clock };
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                entry.pixels = Allocate(pixels.width, pixels.height, format);
                entry.pixels.filterMode = pixels.filterMode;
                if (alphaOnly) Graphics.Blit(pixels, entry.pixels, WhimTexMaterials.EffectCache, 0);
                else Graphics.Blit(pixels, entry.pixels);
                if (errors != null)
                {
                    entry.errors = Allocate(pixels.width, pixels.height, errorFormat);
                    entry.errors.filterMode = FilterMode.Point;
                    Graphics.Blit(errors, entry.errors);
                }
                entries.Add(key, entry);
                Bytes += size;
            }
            catch { Destroy(entry); throw; }
            finally { GL.sRGBWrite = srgb; RenderTexture.active = previous; }
        }

        private void TrimToBudget(long additionalBytes)
        {
            while (Bytes + additionalBytes > Math.Max(0, BudgetBytes) && entries.Count > 0)
            {
                Entry oldest = null;
                foreach (Entry candidate in entries.Values)
                    if (oldest == null || candidate.used < oldest.used) oldest = candidate;
                Remove(oldest);
            }
        }

        private static RenderTexture Allocate(int width, int height, RenderTextureFormat format)
        {
            var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!texture.Create()) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidOperationException("Cannot allocate effect cache texture."); }
            return texture;
        }

        private void Remove(Entry entry)
        {
            entries.Remove(entry.key); Bytes -= entry.bytes; Destroy(entry);
        }
        private static void Destroy(Entry entry)
        {
            if (entry.pixels != null) UnityEngine.Object.DestroyImmediate(entry.pixels);
            if (entry.errors != null) UnityEngine.Object.DestroyImmediate(entry.errors);
        }
        public void Dispose()
        {
            foreach (Entry entry in entries.Values) Destroy(entry);
            entries.Clear(); stamps.Clear(); visiting.Clear(); colorSources.Clear(); requiredEntries.Clear(); unused.Clear();
            Bytes = 0; document = null; liveDrawing = null;
        }
    }
}
