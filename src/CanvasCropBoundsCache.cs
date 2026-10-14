using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Canvas-space snapshots belong to one window/document and never enter saved model data.
    internal sealed class CanvasCropBoundsCache : IDisposable
    {
        private sealed class Entry
        {
            internal Layer layer;
            internal ulong stamp, generation;
            internal int width, height, snapshot;
            internal RectInt? bounds;
            internal bool ready, queued, dynamic;
        }

        private readonly WhimTexDocument document;
        private readonly Dictionary<Layer, Entry> entries = new Dictionary<Layer, Entry>();
        private readonly Queue<Entry> queue = new Queue<Entry>();
        private readonly HashSet<Layer> validation = new HashSet<Layer>();
        private readonly EffectRenderCache stamps = new EffectRenderCache { BudgetBytes = 0 };
        private readonly EffectRenderCache renders = new EffectRenderCache(snapshotShaders: true) { BudgetBytes = 8L * 1024 * 1024 };
        private WhimTexAlphaBounds detector;
        private Entry pending;
        private int snapshot;
        private double nextCheck, deferUntil;
        private bool updating, disposed;

        internal CanvasCropBoundsCache(WhimTexDocument document)
        {
            this.document = document;
            WhimTexDocument.Changed += Changed;
            WhimTexDocument.RenderResourcesChanged += Changed;
        }

        private void Changed(WhimTexDocument changed) { if (changed == document) nextCheck = 0d; }

        internal void BeginSnapshot()
        {
            unchecked { snapshot++; }
            if (snapshot == 0) snapshot = 1;
            foreach (Entry entry in entries.Values)
                if (entry.dynamic) Invalidate(entry);
            nextCheck = 0d;
        }

        private void BeginCheck(bool force = false)
        {
            if (disposed || document == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (!force && now < nextCheck) return;
            stamps.BeginFrame(document);
            renders.BeginFrame(document);
            nextCheck = now + .016d;
        }

        private bool GetKey(Layer layer, out ulong stamp, out bool dynamic)
        {
            stamp = 0; dynamic = false;
            if (disposed || document == null || layer == null ||
                document.width < 1 || document.height < 1 || document.width > SystemInfo.maxTextureSize ||
                document.height > SystemInfo.maxTextureSize || !document.TryFindLayer(layer, out _, out _)) return false;
            try
            {
                validation.Clear();
                if (!document.CanInspectCropLayerContent(layer, validation)) return false;
                stamp = stamps.Stamp(layer);
                if (stamp == 0)
                {
                    dynamic = true;
                    if (snapshot == 0) return false;
                    stamp = renders.Stamp(layer);
                }
                return stamp != 0;
            }
            catch (Exception) { stamp = 0; return false; }
        }

        internal RectInt? Get(Layer layer, bool deferUpdates = false)
        {
            if (disposed || layer == null) return null;
            BeginCheck();
            if (!entries.TryGetValue(layer, out Entry entry))
                entries.Add(layer, entry = new Entry { layer = layer });
            if (!GetKey(layer, out ulong stamp, out bool dynamic))
            {
                Invalidate(entry);
                entry.dynamic = dynamic;
                return null;
            }
            if (entry.stamp != stamp || entry.width != document.width || entry.height != document.height ||
                entry.dynamic != dynamic || (dynamic && entry.snapshot != snapshot))
            {
                Invalidate(entry);
                entry.stamp = stamp; entry.width = document.width; entry.height = document.height;
                entry.dynamic = dynamic; entry.snapshot = snapshot;
            }
            if (deferUpdates)
            {
                deferUntil = EditorApplication.timeSinceStartup + .15d;
                return entry.bounds;
            }
            if (!entry.ready && pending != entry && !entry.queued)
            {
                queue.Enqueue(entry); entry.queued = true; StartUpdate();
            }
            return entry.bounds;
        }

        private void Invalidate(Entry entry)
        {
            entry.bounds = null; entry.ready = false;
            unchecked { entry.generation++; }
            if (pending != entry) return;
            pending = null; detector?.Cancel();
        }

        private void StartUpdate()
        {
            if (updating || disposed) return;
            updating = true; EditorApplication.update += Update;
        }

        private void StopUpdate()
        {
            if (!updating) return;
            updating = false; EditorApplication.update -= Update;
        }

        private void Update()
        {
            if (disposed || document == null) { Dispose(); return; }
            if (queue.Count == 0) { StopUpdate(); return; }
            if (pending != null || detector?.isPending == true || EditorApplication.timeSinceStartup < deferUntil) return;
            BeginCheck(true);
            while (queue.Count > 0)
            {
                Entry entry = queue.Dequeue(); entry.queued = false;
                if (!entries.TryGetValue(entry.layer, out Entry current) || current != entry ||
                    !GetKey(entry.layer, out ulong stamp, out bool dynamic))
                { Invalidate(entry); continue; }
                if (entry.ready && entry.stamp == stamp && entry.width == document.width && entry.height == document.height &&
                    entry.dynamic == dynamic && (!dynamic || entry.snapshot == snapshot)) continue;
                Invalidate(entry);
                entry.stamp = stamp; entry.width = document.width; entry.height = document.height;
                entry.dynamic = dynamic; entry.snapshot = snapshot;
                Analyze(entry);
                break;
            }
            if (queue.Count == 0) StopUpdate();
        }

        private void Analyze(Entry entry)
        {
            RenderTexture rendered = null;
            try
            {
                rendered = document.RenderCropLayerContent(entry.layer, renders);
                if (rendered == null) { entry.ready = true; return; }
                detector ??= new WhimTexAlphaBounds();
                ulong generation = entry.generation;
                pending = entry;
                if (!detector.Begin(rendered, bounds => Complete(entry, generation, bounds)))
                {
                    if (pending == entry) pending = null;
                    entry.ready = true;
                }
            }
            catch (Exception)
            {
                if (pending == entry) pending = null;
                detector?.Cancel(); entry.bounds = null; entry.ready = true;
            }
            finally { if (rendered != null) RenderTexture.ReleaseTemporary(rendered); }
        }

        private void Complete(Entry entry, ulong generation, RectInt? bounds)
        {
            if (disposed || pending != entry || entry.generation != generation) return;
            pending = null;
            BeginCheck(true);
            if (!entries.TryGetValue(entry.layer, out Entry current) || current != entry ||
                !GetKey(entry.layer, out ulong stamp, out bool dynamic) || stamp != entry.stamp ||
                document.width != entry.width || document.height != entry.height || entry.dynamic != dynamic ||
                (dynamic && entry.snapshot != snapshot)) Invalidate(entry);
            else { entry.bounds = bounds; entry.ready = true; }
            if (queue.Count > 0) StartUpdate();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            WhimTexDocument.Changed -= Changed; WhimTexDocument.RenderResourcesChanged -= Changed;
            StopUpdate(); pending = null;
            detector?.Dispose(); detector = null;
            renders.Dispose(); stamps.Dispose(); entries.Clear(); queue.Clear(); validation.Clear();
        }
    }
}
