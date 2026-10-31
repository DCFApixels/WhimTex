using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class ShaderFXClipboard
    {
        internal static event Action Changed;
        private static List<ShaderFX> snapshot;
        private static WeakReference<WhimTexDocument> sourceDocument;
        private static string marker;
        private static uint imageRevision;

        static ShaderFXClipboard()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        internal static IReadOnlyList<ShaderFX> Current
        {
            get
            {
                if (snapshot == null) return null;
                if (snapshot.Exists(value => value == null) || imageRevision != ImageClipboard.Revision || GUIUtility.systemCopyBuffer != marker)
                    Clear();
                return snapshot;
            }
        }

        internal static void Copy(WhimTexDocument document, ShaderFX value)
        {
            if (value == null) return;
            CopyAll(document, new[] { value });
        }

        internal static string PasteLabel
        {
            get
            {
                int count = Current?.Count ?? 0;
                return count > 1 ? $"Paste {count} FX As New" : "Paste FX As New";
            }
        }

        internal static void CopyAll(WhimTexDocument document, IReadOnlyList<ShaderFX> values)
        {
            List<ShaderFX> next = CreateSnapshot(values);
            if (next.Count == 0) return;

            string nextMarker = "WhimTex FX: " + Guid.NewGuid().ToString("N");
            try
            {
                GUIUtility.systemCopyBuffer = nextMarker;
            }
            catch
            {
                DestroyValues(next);
                throw;
            }

            Clear();
            snapshot = next;
            sourceDocument = document != null ? new WeakReference<WhimTexDocument>(document) : null;
            marker = nextMarker;
            imageRevision = ImageClipboard.Revision;
            Changed?.Invoke();
        }

        private static List<ShaderFX> CreateSnapshot(IReadOnlyList<ShaderFX> values)
        {
            var copies = new List<ShaderFX>();
            try
            {
                if (values != null)
                    foreach (ShaderFX value in values)
                        if (value != null) copies.Add(value.CloneForClipboard());
                return copies;
            }
            catch
            {
                DestroyValues(copies);
                throw;
            }
        }

        internal static List<ShaderFX> CreatePasteValues(WhimTexDocument destination)
        {
            IReadOnlyList<ShaderFX> sources = Current;
            if (sources == null) return null;

            bool sameDocument = sourceDocument != null && sourceDocument.TryGetTarget(out WhimTexDocument sourceOwner) &&
                sourceOwner != null && sourceOwner == destination;
            var copies = new List<ShaderFX>(sources.Count);
            try
            {
                foreach (ShaderFX source in sources)
                {
                    ShaderFX copy = source.CloneForDocument(destination);
                    copies.Add(copy);
                    copy.DetachCatalog();
                    if (!sameDocument)
                        copy.RemapTextureLayers(new Dictionary<string, string>(), clearExternal: true);
                }
                return copies;
            }
            catch
            {
                DestroyValues(copies);
                throw;
            }
        }

        private static void DestroyValues(List<ShaderFX> values)
        {
            if (values == null) return;
            foreach (ShaderFX value in values)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
        }

        internal static void Clear()
        {
            DestroyValues(snapshot);
            snapshot = null;
            sourceDocument = null;
            marker = null;
            Changed?.Invoke();
        }
    }
}
