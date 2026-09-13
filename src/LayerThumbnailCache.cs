using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    internal sealed class LayerThumbnailCache : IDisposable
    {
        private sealed class Entry
        {
            internal Texture2D texture;
            internal ulong stamp;
            internal int size, width, height;
            internal bool ready;
        }

        private readonly Dictionary<Layer, Entry> entries = new Dictionary<Layer, Entry>();
        private readonly HashSet<Layer> alive = new HashSet<Layer>();
        private readonly List<Layer> removed = new List<Layer>();
        private readonly EffectRenderCache renders = new EffectRenderCache(snapshotShaders: true) { BudgetBytes = 8L * 1024 * 1024 };
        private readonly TextureCompositor document;
        private double nextCheck, deferUntil;
        internal int RenderCount { get; private set; }

        internal LayerThumbnailCache(TextureCompositor document) => this.document = document;

        internal void Invalidate() => nextCheck = 0;

        internal void RefreshStructure() => BeginCheck();

        private void BeginCheck()
        {
            renders.BeginFrame(document);
            alive.Clear();
            Visit(document.layers);
            removed.Clear();
            foreach (var pair in entries)
                if (!alive.Contains(pair.Key) || !(pair.Key.Behaviour is TargetedLayerBehaviour ||
                    pair.Key.Behaviour is ShaderProcessorLayerBehaviour)) removed.Add(pair.Key);
            foreach (Layer layer in removed)
            {
                Destroy(entries[layer]);
                entries.Remove(layer);
            }
            removed.Clear();
            nextCheck = EditorApplication.timeSinceStartup + .2d;
        }

        private void Visit(List<Layer> layers)
        {
            if (layers == null) return;
            foreach (Layer layer in layers)
                if (layer != null && alive.Add(layer) && layer.IsGroup) Visit(layer.children);
        }

        internal Texture2D Get(Layer layer, int size, bool deferUpdates = false)
        {
            if (layer?.Behaviour == null) return null;
            entries.TryGetValue(layer, out Entry entry);
            double now = EditorApplication.timeSinceStartup;
            if (deferUpdates) deferUntil = now + .2d;
            if (now < deferUntil) return entry?.texture;
            if (now >= nextCheck) BeginCheck();
            if (!alive.Contains(layer)) return null;
            size = Mathf.Clamp(size, 1, 128);
            ulong stamp = renders.Stamp(layer);
            if (entry != null && entry.ready && entry.stamp == stamp && entry.size == size &&
                entry.width == document.width && entry.height == document.height) return entry.texture;
            if (entry == null) entries[layer] = entry = new Entry();
            entry.stamp = stamp; entry.size = size;
            entry.width = document.width; entry.height = document.height; entry.ready = true;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture rendered = null, reduced = null;
            try
            {
                GL.sRGBWrite = false;
                RenderCount++;
                rendered = document.RenderThumbnailLayer(layer, Mathf.Max(64, size * 2), renders);
                Texture2D next = null;
                if (rendered != null)
                {
                    float scale = (float)size / Mathf.Max(rendered.width, rendered.height);
                    reduced = RenderTexture.GetTemporary(Mathf.Max(1, Mathf.RoundToInt(rendered.width * scale)),
                        Mathf.Max(1, Mathf.RoundToInt(rendered.height * scale)), 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    Graphics.Blit(rendered, reduced);
                    next = TextureCompositor.CopyToTexture2D(reduced);
                }
                Destroy(entry);
                entry.texture = next;
            }
            catch (Exception exception)
            {
                // Keep the last good image, and retry only after the source/settings change.
                Debug.LogWarning("WhimTex layer thumbnail: " + exception.Message);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (reduced != null) RenderTexture.ReleaseTemporary(reduced);
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            }
            return entry.texture;
        }

        private static void Destroy(Entry entry)
        {
            if (entry.texture != null) UnityEngine.Object.DestroyImmediate(entry.texture);
            entry.texture = null;
        }

        public void Dispose()
        {
            foreach (Entry entry in entries.Values) Destroy(entry);
            entries.Clear(); alive.Clear(); removed.Clear(); renders.Dispose();
            nextCheck = deferUntil = 0;
        }
    }
}
