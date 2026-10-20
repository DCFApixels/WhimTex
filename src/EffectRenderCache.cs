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
        private WhimTexDocument document;
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

        internal void BeginFrame(WhimTexDocument owner, DrawingLayerBehaviour painting = null)
        {
            using var profile = WhimTexProfiler.Measure("cache.prepare", owner);
            if (document != owner) { Dispose(); document = owner; }
            WhimTexProfiler.BeginCauseFrame();
            if (WhimTexProfiler.Recording)
                foreach (Entry entry in entries.Values)
                {
                    WhimTexProfiler.TrackTexture(entry.pixels, "effect cache", existing: true);
                    WhimTexProfiler.TrackTexture(entry.errors, "effect cache diagnostics", existing: true);
                }
            owner.RefreshTransformHierarchy();
            liveDrawing = painting;
            frame++;
            stamps.Clear(); visiting.Clear(); colorSources.Clear(); requiredEntries.Clear();
            VisitVisible(owner.layers);
            ReleaseUnusedDistancePreparation(owner.layers);
            visiting.Clear();
            unused.Clear();
            foreach (Entry entry in entries.Values)
                if (!IsRequired(entry.key)) unused.Add(entry);
            foreach (Entry entry in unused) Remove(entry, "unused");
            unused.Clear();
            TrimToBudget(0);
        }

        private void VisitVisible(List<Layer> layers)
        {
            if (layers == null) return;
            foreach (Layer layer in layers)
                if (layer != null && layer.enabled) VisitRequired(layer);
        }

        private void ReleaseUnusedDistancePreparation(List<Layer> layers)
        {
            if (layers == null) return;
            foreach (Layer layer in layers)
            {
                if (layer == null) continue;
                if (!requiredEntries.Contains(layer.Id + "/effect"))
                {
                    if (layer.Behaviour is SDFLayerBehaviour sdf) sdf.ReleaseDistancePreparation();
                    if (layer.Behaviour is OutlineLayerBehaviour outline) outline.ReleaseDistancePreparation();
                }
                if (layer.IsGroup) ReleaseUnusedDistancePreparation(layer.layers);
            }
        }

        private void VisitRequired(Layer layer)
        {
            if (layer == null || !visiting.Add(layer)) return;
            RequireEntry(layer, "viewport-source");
            if (layer?.AsGroup() is Layer group)
            {
                VisitVisible(group.layers);
                if (!group.IsPassThrough && CanCacheLayer(layer))
                    RequireEntry(layer, "group-composite");
            }
            if (layer?.Behaviour is TargetedLayerBehaviour effect)
            {
                RequireEntry(layer, "effect");
                if (effect is MakeSeamlessLayerBehaviour seamless && seamless.EffectiveMode == MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting)
                    RequireEntry(layer, "quilting");
                if (effect.inputMode == EffectInputMode.AllBelow)
                {
                    if (document.TryFindLayer(layer, out var below, out int effectIndex))
                        for (int i = effectIndex + 1; i < below.Count; i++)
                            if (below[i]?.enabled == true) VisitRequired(below[i]);
                }
                else
                {
                    Layer input = Input(effect);
                    if (input?.IsGroup == true) RequireEntry(input, "group");
                    if (effect.RequiresColorInput) colorSources.Add(input);
                    VisitRequired(input);
                }
            }
            else if (layer?.Behaviour is ShaderProcessorLayerBehaviour &&
                document.TryFindLayer(layer, out var processorContainer, out int processorIndex))
            {
                RequireEntry(layer, "processor");
                RequireEntry(layer, "effect");
                for (int i = processorIndex + 1; i < processorContainer.Count; i++)
                    VisitRequired(processorContainer[i]);
            }
            else if (CanCacheLayer(layer))
                RequireEntry(layer, "effect");
            if (layer.fx != null)
                foreach (var fxEntry in layer.fx)
                    if (fxEntry is ShaderFX fx && fx.Active)
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

        private bool IsRequired(string key)
        {
            if (requiredEntries.Contains(key)) return true;
            int variant = key.IndexOf("/viewport", StringComparison.Ordinal);
            // viewport-source is a render kind, not a viewport cache suffix.
            if (variant >= 0 && key.IndexOf("/viewport-source", StringComparison.Ordinal) == variant)
                variant = key.IndexOf("/viewport", variant + 1, StringComparison.Ordinal);
            int prefix = key.IndexOf("/prefix/", StringComparison.Ordinal);
            if (prefix >= 0 && (variant < 0 || prefix < variant)) variant = prefix;
            return variant >= 0 && requiredEntries.Contains(key.Substring(0, variant));
        }

        private void RequireEntry(Layer layer, string kind)
        {
            requiredEntries.Add(layer.Id + "/" + kind);
            requiredEntries.Add(layer.Id + "/" + kind + "/debug");
        }

        private Layer Input(TargetedLayerBehaviour effect)
        {
            if (effect.inputMode == EffectInputMode.AllBelow) return null;
            if (effect.inputMode == EffectInputMode.Specific) return document.FindLayer(effect.TargetLayerId);
            if(!document.TryFindLayer(effect,out var list,out int index)) return null;
            index=WhimTexDocument.NextContentLayer(list,index);
            return index<list.Count ? list[index] : null;
        }

        internal static bool CanCacheLayer(Layer layer)
        {
            if (layer?.Behaviour is TargetedLayerBehaviour || layer?.Behaviour is ShaderProcessorLayerBehaviour)
                return true;
            if (layer?.fx == null) return false;
            bool found = false;
            foreach (var fxEntry in layer.fx)
            {
                if (!(fxEntry is ShaderFX fx) || !fx.Active) continue;
                found = true;
                if (fx.UsesUnityTimeInputs) return false;
            }
            return found;
        }

        private ulong InputStamp(TargetedLayerBehaviour effect)
        {
            if (effect.inputMode != EffectInputMode.AllBelow) return DependencyStamp(effect.Owner, Input(effect), "target input");
            ulong hash = 14695981039346656037UL;
            if (!document.TryFindLayer(effect, out var below, out int index)) return 0;
            for (int i = index + 1; i < below.Count; i++)
            {
                if (below[i]?.enabled != true || below[i].Behaviour is PendingLayerBehaviour) continue;
                ulong dependency = DependencyStamp(effect.Owner, below[i], "all-below input");
                if (dependency == 0) return 0;
                hash = Mix(hash, dependency);
            }
            return hash == 0 ? 1 : hash;
        }

        private static bool CanCacheFx(ShaderFX fxEntry)
        {
            return fxEntry is ShaderFX fx && (!fx.Active || !fx.UsesUnityTimeInputs);
        }

        internal ulong Stamp(Layer layer)
        {
            if (layer == null) return 1;
            if (stamps.TryGetValue(layer, out ulong ready)) return ready;
            // Live stroke pixels change every input batch. Avoid serializing/hashing
            // their entire Drawing state and retaining a single-frame FX cache.
            if (layer.Behaviour is DrawingLayerBehaviour live &&
                (live == liveDrawing || live.HasBufferedStroke)) return stamps[layer] = 0;
            if (!visiting.Add(layer)) return 0;
            try
            {
                // Unity time inputs bypass reusable caching.
                // Thumbnail snapshots may explicitly capture their current result.
                if (!snapshotShaders && layer.fx != null)
                    foreach (var fxEntry in layer.fx)
                        if (fxEntry != null && !CanCacheFx(fxEntry)) return stamps[layer] = 0;
                ulong dependencies = 14695981039346656037UL;
                if (layer?.AsGroup() is Layer group && group.layers != null)
                    foreach (Layer child in group.layers)
                    {
                        if (child?.enabled != true || child.Behaviour is PendingLayerBehaviour) continue;
                        ulong dependency = DependencyStamp(layer, child, "group child");
                        if (dependency == 0) return stamps[layer] = 0;
                        dependencies = Mix(dependencies, dependency);
                    }
                if (layer?.Behaviour is TargetedLayerBehaviour effect)
                {
                    ulong dependency = InputStamp(effect);
                    if (dependency == 0) return stamps[layer] = 0;
                    dependencies = Mix(dependencies, dependency);
                }
                if (layer?.Behaviour is ShaderProcessorLayerBehaviour &&
                    document.TryFindLayer(layer, out var container, out int index))
                    for (int i = index + 1; i < container.Count; i++)
                    {
                        if (container[i]?.enabled != true || container[i].Behaviour is PendingLayerBehaviour) continue;
                        ulong dependency = DependencyStamp(layer, container[i], "processor input");
                        if (dependency == 0) return stamps[layer] = 0;
                        dependencies = Mix(dependencies, dependency);
                    }
                if (layer.clippingMask)
                {
                    ulong dependency = DependencyStamp(layer, document.GetClippingBase(layer), "clipping base");
                    if (dependency == 0) return stamps[layer] = 0;
                    dependencies = Mix(dependencies, dependency);
                }
                ulong hash = Mix(Mix(14695981039346656037UL, layer.transformCache?.version ?? 0), dependencies);
                string settings = JsonUtility.ToJson(layer);
                WhimTexProfiler.ObserveSettings(document, layer, settings);
                foreach (char c in settings) hash = Mix(hash, c);
                if (layer.Behaviour is TextLayerBehaviour text)
                {
                    hash = Mix(hash, unchecked((uint)SystemFontCatalog.Revision));
                    hash = Mix(hash, text.FontAvailable ? 1UL : 0UL);
                }
                if (layer.fx != null)
                    foreach (var fxEntry in layer.fx)
                    {
                        if (fxEntry == null) continue;
                        hash = Mix(hash, unchecked((ulong)UnityEditor.EditorUtility.GetDirtyCount(fxEntry)));
                        if (fxEntry is ShaderFX shaderFX)
                        {
                            hash = Mix(hash, shaderFX.RenderCacheStamp());
                            foreach (var parameter in shaderFX.Parameters)
                                if (parameter?.textureValue != null) hash = MixTexture(hash, parameter.textureValue);
                            foreach (var parameter in shaderFX.TextureLayerParameters())
                            {
                                ulong dependency = DependencyStamp(layer, document.FindLayer(parameter.textureLayerId), "FX texture input");
                                if (dependency == 0) return stamps[layer] = 0;
                                hash = Mix(hash, dependency);
                            }
                        }
                    }
                Texture texture = layer.SamplingSource;
                bool paintSource = layer.Behaviour is DrawingLayerBehaviour sourceDrawing && sourceDrawing.HasPaintSurface;
                if (texture != null)
                {
                    hash = Mix(hash, unchecked((ulong)texture.GetHashCode()));
                    // CPU Undo/storage pages catch up asynchronously. Rendering already reads
                    // the completed Half surface, whose revision tracks actual image changes.
                    if (!paintSource) hash = Mix(hash, texture.updateCount);
                    hash = Mix(hash, (ulong)texture.width);
                    hash = Mix(hash, (ulong)texture.height);
                    hash = Mix(hash, (ulong)texture.graphicsFormat);
                    if (!paintSource) hash = Mix(hash, unchecked((ulong)UnityEditor.EditorUtility.GetDirtyCount(texture)));
                    hash = Mix(hash, (ulong)texture.filterMode);
                    hash = Mix(hash, (ulong)texture.wrapModeU);
                    hash = Mix(hash, (ulong)texture.wrapModeV);
                }
                if (layer.Behaviour is DrawingLayerBehaviour drawing)
                    hash = Mix(hash, drawing.PaintSurfaceRevision);
                return stamps[layer] = hash == 0 ? 1 : hash;
            }
            finally { visiting.Remove(layer); }
        }

        // The raw quilting result does not depend on downstream Poisson, FX, transforms,
        // opacity or channelMapping. Input dependency stamps still cover all upstream changes.
        internal ulong QuiltingStamp(MakeSeamlessLayerBehaviour layer, WhimTexDocument owner)
        {
            // Direct thumbnail/export callers may not have begun a cache frame.
            if(!ReferenceEquals(document,owner)) return 0;
            ulong hash=InputStamp(layer);
            if(hash==0) return 0;
            hash=Mix(hash,(ulong)layer.quiltingEdges);
            hash=Mix(hash,unchecked((uint)layer.quiltingWidth.GetHashCode()));
            hash=Mix(hash,unchecked((uint)layer.quiltingAlongSearch.GetHashCode()));
            hash=Mix(hash,unchecked((uint)layer.quiltingFeather.GetHashCode()));
            hash=Mix(hash,layer.quiltingContrastCompensation?1UL:0UL);
            hash=Mix(hash,unchecked((uint)layer.quiltingContrast.GetHashCode()));
            hash=Mix(hash,(ulong)layer.quiltingQuality);
            hash=Mix(hash,unchecked((uint)layer.quiltingSeed));
            hash=Mix(hash,(ulong)layer.quiltingChannels);
            hash=Mix(hash,(ulong)((layer.processRed?1:0)|(layer.processGreen?2:0)|(layer.processBlue?4:0)|(layer.processAlpha?8:0)));
            return hash==0 ? 1 : hash;
        }

        private static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

        private ulong DependencyStamp(Layer consumer, Layer input, string relation)
        {
            ulong stamp = Stamp(input);
            WhimTexProfiler.DependencyCause(document, consumer, input, relation);
            return stamp;
        }

        private static ulong MixTexture(ulong hash, Texture texture)
        {
            if (texture == null) return Mix(hash, 0);
            hash = Mix(hash, unchecked((ulong)texture.GetHashCode()));
            hash = Mix(hash, texture.updateCount);
            hash = Mix(hash, (ulong)texture.width);
            hash = Mix(hash, (ulong)texture.height);
            hash = Mix(hash, (ulong)texture.graphicsFormat);
            hash = Mix(hash, unchecked((ulong)UnityEditor.EditorUtility.GetDirtyCount(texture)));
            hash = Mix(hash, (ulong)texture.filterMode);
            hash = Mix(hash, (ulong)texture.wrapModeU);
            hash = Mix(hash, (ulong)texture.wrapModeV);
            return hash;
        }

        internal bool TryGet(string key, ulong stamp, int width, int height, float scale, bool interactive,
            bool requireColor, out RenderTexture pixels, out RenderTexture errors, out bool alphaOnly)
        {
            pixels = errors = null; alphaOnly = false;
            string miss = !entries.TryGetValue(key, out Entry entry) ? "miss:not_retained" :
                entry.stamp != stamp ? "miss:content_or_settings" : entry.interactive != interactive ? "miss:quality" :
                entry.scale != scale ? "miss:scale" : entry.pixels == null || !entry.pixels.IsCreated() ||
                entry.errors != null && !entry.errors.IsCreated() ? "miss:resource_lost" :
                entry.pixels.width != width || entry.pixels.height != height ? "miss:dimensions" :
                requireColor && entry.alphaOnly ? "miss:color_required" : null;
            if (miss != null)
            {
                WhimTexProfiler.Event("effect.cache", "cache", miss);
                ProfileCacheReason(key, miss);
                Misses++; return false;
            }
            entry.used = ++clock;
            pixels = entry.pixels; errors = entry.errors; alphaOnly = entry.alphaOnly;
            Hits++;
            WhimTexProfiler.Event("effect.cache", "cache", "hit", entry.bytes);
            return true;
        }

        internal void Store(string key, ulong stamp, float scale, bool interactive, bool alphaOnly,
            RenderTexture pixels, RenderTexture errors)
            => StoreCore(key,stamp,scale,interactive,alphaOnly,pixels,errors,false);

        internal void StoreFullPrecision(string key, ulong stamp, float scale, bool interactive, RenderTexture pixels)
            => StoreCore(key,stamp,scale,interactive,false,pixels,null,true);

        private void StoreCore(string key, ulong stamp, float scale, bool interactive, bool alphaOnly,
            RenderTexture pixels, RenderTexture errors, bool fullPrecision)
        {
            if (pixels == null || stamp == 0) return;
            using var profile = WhimTexProfiler.Measure("cache.store");
            if (entries.TryGetValue(key, out Entry old)) Remove(old);
            RenderTextureFormat format = fullPrecision ? RenderTextureFormat.ARGBFloat : alphaOnly && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBHalf;
            RenderTextureFormat errorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            long area = (long)pixels.width * pixels.height;
            long size = area * (format == RenderTextureFormat.ARGBFloat ? 16 : format == RenderTextureFormat.RFloat ? 4 : 8) +
                (errors == null ? 0 : area * (errorFormat == RenderTextureFormat.R8 ? 1 : 4));
            if (size > BudgetBytes) { WhimTexProfiler.Event("effect.cache", "cache", "bypass:budget", size); return; }
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
                WhimTexProfiler.Event("effect.cache", "cache", "stored", size);
                WhimTexProfiler.Event("effect.cache.bytes", "counter", value: Bytes);
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
                Remove(oldest, "budget");
            }
        }

        private static RenderTexture Allocate(int width, int height, RenderTextureFormat format)
        {
            var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!texture.Create()) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidOperationException("Cannot allocate effect cache texture."); }
            WhimTexProfiler.TrackTexture(texture, "effect cache");
            return texture;
        }

        private void Remove(Entry entry, string reason = "replaced")
        {
            ProfileCacheReason(entry.key, reason, "cache.release");
            WhimTexProfiler.Event("effect.cache.release", "cache", reason, entry.bytes);
            entries.Remove(entry.key); Bytes -= entry.bytes; Destroy(entry);
        }
        private static void Destroy(Entry entry)
        {
            WhimTexProfiler.ReleaseTexture(entry.pixels);
            WhimTexProfiler.ReleaseTexture(entry.errors);
            if (entry.pixels != null) UnityEngine.Object.DestroyImmediate(entry.pixels);
            if (entry.errors != null) UnityEngine.Object.DestroyImmediate(entry.errors);
        }
        public void Dispose()
        {
            if (entries.Count > 0)
                WhimTexProfiler.Reason("cache.invalidate", "cache.disposed", document, count: entries.Count);
            foreach (Entry entry in entries.Values) Destroy(entry);
            entries.Clear(); stamps.Clear(); visiting.Clear(); colorSources.Clear(); requiredEntries.Clear(); unused.Clear();
            Bytes = 0; document = null; liveDrawing = null;
        }

        private void ProfileCacheReason(string key, string reason, string phase = "cache.miss")
        {
            if (!WhimTexProfiler.Recording) return;
            int separator = key.IndexOf('/');
            Layer layer = document?.FindLayer(separator < 0 ? key : key.Substring(0, separator));
            WhimTexProfiler.Reason(phase, reason, document, layer, key);
        }
    }
}
