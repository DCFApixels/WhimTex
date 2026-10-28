using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private Dictionary<string, ExternalFileEntry> externalFileTextures;
        [NonSerialized] private double nextExternalFilePoll;

        internal Texture2D ResolveExternalFileTexture(string path, bool forceCheck = false)
        {
            if (string.IsNullOrEmpty(path)) return null;
            externalFileTextures ??= new Dictionary<string, ExternalFileEntry>(StringComparer.Ordinal);
            if (!externalFileTextures.TryGetValue(path, out var entry)) externalFileTextures.Add(path, entry = new ExternalFileEntry());
            entry.Refresh(path, forceCheck);
            return entry.texture;
        }

        internal string ExternalFileError(string path) => path != null && externalFileTextures != null &&
            externalFileTextures.TryGetValue(path, out var entry) ? entry.error : null;

        private void PollExternalFiles()
        {
            if (externalFileTextures == null || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < nextExternalFilePoll) return;
            nextExternalFilePoll = EditorApplication.timeSinceStartup + 1;
            bool changed = false;
            foreach (var item in externalFileTextures) changed |= item.Value.Refresh(item.Key, false);
            if (changed) RenderResourcesChanged?.Invoke(this);
        }

        internal void RefreshExternalFiles()
        {
            if (externalFileTextures == null) return;
            foreach (var item in externalFileTextures) item.Value.Refresh(item.Key, true);
        }

        private void PruneExternalFileTextures()
        {
            if (externalFileTextures == null || externalFileTextures.Count == 0) return;
            var used = new HashSet<string>(StringComparer.Ordinal);
            void Collect(List<Layer> items)
            {
                if (items == null) return;
                foreach (var layer in items)
                {
                    if (layer?.Behaviour is FileLayerBehaviour file && file.HasExternalSource) used.Add(file.externalSourcePath);
                    if (layer?.IsGroup == true) Collect(layer.children);
                }
            }
            Collect(layers);
            var removed = new List<string>();
            foreach (var item in externalFileTextures)
                if (!used.Contains(item.Key)) { item.Value.Dispose(); removed.Add(item.Key); }
            foreach (string path in removed) externalFileTextures.Remove(path);
        }

        private void ReleaseExternalFileTextures()
        {
            if (externalFileTextures == null) return;
            foreach (var entry in externalFileTextures.Values) entry.Dispose();
            externalFileTextures = null;
        }

        private sealed class ExternalFileEntry : IDisposable
        {
            internal Texture2D texture;
            internal string error;
            private long length = -2, written;
            private double nextCheck;

            internal bool Refresh(string path, bool force)
            {
                double now = EditorApplication.timeSinceStartup;
                if (!force && now < nextCheck) return false;
                nextCheck = now + 1;
                try
                {
                    var info = new FileInfo(path);
                    long size = info.Exists ? info.Length : -1, stamp = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
                    if (size == length && stamp == written && (!force || error == null)) return false;
                    length = size; written = stamp;
                    Dispose();
                    if (!info.Exists) throw new FileNotFoundException("Source image is missing: " + path);
                    texture = FileLayerSource.Decode(path);
                    error = null;
                }
                catch (Exception exception) { Dispose(); error = exception.Message; }
                return true;
            }

            public void Dispose()
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
            }
        }
    }
}
