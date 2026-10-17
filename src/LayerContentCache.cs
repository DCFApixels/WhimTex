using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum LayerContentState { Unknown, Empty, Present }

    internal sealed class LayerContentCache : IDisposable
    {
        private sealed class Entry
        {
            internal Layer layer;
            internal ulong stamp, generation;
            internal int width, height;
            internal LayerContentState state;
            internal bool ready, queued, deferred, coverage;
        }

        private readonly WhimTexDocument document;
        private readonly Dictionary<Layer, Entry> entries = new Dictionary<Layer, Entry>();
        private readonly Queue<Entry> queue = new Queue<Entry>();
        private readonly List<Layer> removed = new List<Layer>();
        private readonly EffectRenderCache renders = new EffectRenderCache { BudgetBytes = 8L * 1024 * 1024 };
        private readonly HashSet<Layer> validation = new HashSet<Layer>();
        private WhimTexAlphaPresence detector;
        private Entry pending;
        private double nextCheck;
        private bool updating, disposed, analyzing;

        internal LayerContentCache(WhimTexDocument document)
        {
            this.document = document;
            WhimTexDocument.Changed += OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged += OnDocumentChanged;
        }

        private void OnDocumentChanged(WhimTexDocument changed)
        {
            if (changed != document || disposed) return;
            nextCheck = 0d;
        }

        private void BeginCheck(bool force = false)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!force && now < nextCheck) return;
            renders.BeginFrame(document);
            nextCheck = now + .016d;
        }

        private bool GetKey(Layer layer, out ulong stamp, out int width, out int height)
        {
            stamp = 0; width = height = 0;
            if (disposed || document == null || !document.TryFindLayer(layer, out _, out _)) return false;
            try
            {
                validation.Clear();
                if (!document.CanInspectLayerContent(layer, validation)) return false;
                document.GetLayerContentSize(layer, out width, out height);
                if (width <= 0 || height <= 0 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize) return false;
                stamp = renders.Stamp(layer);
                return stamp != 0;
            }
            catch (Exception) { stamp = 0; return false; }
        }

        internal LayerContentState Get(Layer layer, bool deferUpdates)
        {
            if (disposed || layer == null) return LayerContentState.Unknown;
            BeginCheck();
            if (!entries.TryGetValue(layer, out Entry entry))
                entries.Add(layer, entry = new Entry { layer = layer });
            entry.deferred = deferUpdates;
            if (!GetKey(layer, out ulong stamp, out int width, out int height))
            {
                Invalidate(entry);
                return LayerContentState.Unknown;
            }
            if (entry.stamp != stamp || entry.width != width || entry.height != height)
            {
                Invalidate(entry);
                entry.stamp = stamp; entry.width = width; entry.height = height;
            }
            if (deferUpdates)
            {
                if (!entry.ready) Invalidate(entry);
                return entry.state;
            }
            LayerContentState coverageState = LayerContentState.Unknown;
            entry.coverage = layer.Behaviour is DrawingLayerBehaviour drawing &&
                drawing.TryGetAlphaContentState(width, height, out coverageState);
            if (entry.coverage)
            {
                entry.state = coverageState;
                if (pending == entry) { pending = null; detector?.Cancel(); }
                entry.ready = entry.state != LayerContentState.Unknown;
                return entry.state;
            }
            if (!entry.ready && pending != entry && !entry.queued)
            {
                queue.Enqueue(entry);
                entry.queued = true;
                StartUpdate();
            }
            return entry.state;
        }

        private void Invalidate(Entry entry)
        {
            entry.state = LayerContentState.Unknown;
            entry.ready = false;
            unchecked { entry.generation++; }
            if (pending != entry) return;
            pending = null;
            detector?.Cancel();
        }

        internal void RefreshStructure()
        {
            if (disposed) return;
            BeginCheck(true);
            removed.Clear();
            foreach (var pair in entries)
                if (!document.TryFindLayer(pair.Key, out _, out _)) removed.Add(pair.Key);
            foreach (Layer layer in removed)
            {
                Invalidate(entries[layer]);
                entries.Remove(layer);
            }
            removed.Clear();
        }

        private void StartUpdate()
        {
            if (updating || disposed) return;
            EditorApplication.update += Update;
            updating = true;
        }

        private void StopUpdate()
        {
            if (!updating) return;
            EditorApplication.update -= Update;
            updating = false;
        }

        private void Update()
        {
            if (disposed || document == null) { Dispose(); return; }
            if (queue.Count == 0) { StopUpdate(); return; }
            if (pending != null || detector?.isPending == true) return;
            BeginCheck(true);
            while (queue.Count > 0)
            {
                Entry entry = queue.Dequeue();
                entry.queued = false;
                if (entry.deferred || entry.coverage) continue;
                if (!entries.TryGetValue(entry.layer, out Entry current) || current != entry ||
                    !GetKey(entry.layer, out ulong stamp, out int width, out int height))
                { Invalidate(entry); continue; }
                if (entry.ready && entry.stamp == stamp && entry.width == width && entry.height == height) continue;
                Invalidate(entry);
                entry.stamp = stamp; entry.width = width; entry.height = height;
                Analyze(entry);
                break;
            }
            if (queue.Count == 0) StopUpdate();
        }

        private void Analyze(Entry entry)
        {
            using var profile = WhimTexProfiler.Measure("layer.content_check", document, entry.layer);
            using var gpu = profile.Gpu();
            RenderTexture rendered = null;
            try
            {
                analyzing = true;
                rendered = document.RenderLayerContent(entry.layer, entry.width, entry.height, renders);
                if (rendered == null) { entry.ready = true; return; }
                Reduce(entry, rendered);
            }
            catch (Exception)
            {
                if (pending == entry) pending = null;
                detector?.Cancel();
                entry.state = LayerContentState.Unknown;
                entry.ready = true;
            }
            finally
            {
                analyzing = false;
                if (rendered != null) ProfiledRenderTexture.ReleaseTemporary(rendered);
            }
        }

        private void Reduce(Entry entry, RenderTexture rendered)
        {
            detector ??= new WhimTexAlphaPresence();
            ulong generation = entry.generation;
            pending = entry;
            if (!detector.Begin(rendered, empty => Complete(entry, generation, empty)))
            {
                if (pending == entry) pending = null;
                entry.ready = true;
            }
        }

        // Borrow the exact full-size, pre-clipping result while its owner still holds it.
        // Reduction retains only its own final pixel, never the full render texture.
        internal void AcceptRendered(Layer layer, RenderTexture rendered)
        {
            if (disposed || analyzing || rendered == null || pending != null || detector?.isPending == true ||
                !entries.TryGetValue(layer, out var entry) || entry.deferred ||
                layer.Behaviour is DrawingLayerBehaviour drawing && drawing.CanUseAlphaCoverage) return;
            BeginCheck(true);
            if (!GetKey(layer, out ulong stamp, out int width, out int height) ||
                rendered.width != width || rendered.height != height) return;
            if (entry.ready && entry.stamp == stamp && entry.width == width && entry.height == height) return;
            Invalidate(entry);
            entry.coverage = false;
            entry.stamp = stamp; entry.width = width; entry.height = height;
            using var profile = WhimTexProfiler.Measure("layer.content_check.reuse", document, layer);
            using var gpu = profile.Gpu();
            Reduce(entry, rendered);
        }

        private void Complete(Entry entry, ulong generation, bool? empty)
        {
            if (disposed || pending != entry || entry.generation != generation) return;
            pending = null;
            BeginCheck(true);
            if (!entries.TryGetValue(entry.layer, out Entry current) || current != entry ||
                !GetKey(entry.layer, out ulong stamp, out int width, out int height) ||
                stamp != entry.stamp || width != entry.width || height != entry.height)
            {
                Invalidate(entry);
            }
            else
            {
                entry.state = empty.HasValue ? empty.Value ? LayerContentState.Empty : LayerContentState.Present : LayerContentState.Unknown;
                entry.ready = true;
            }
            if (queue.Count > 0) StartUpdate();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            WhimTexDocument.Changed -= OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= OnDocumentChanged;
            StopUpdate();
            pending = null;
            detector?.Dispose(); detector = null;
            renders.Dispose(); entries.Clear(); queue.Clear(); removed.Clear(); validation.Clear();
        }
    }
}
