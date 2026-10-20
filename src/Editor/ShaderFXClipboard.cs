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
        private static ShaderFX snapshot;
        private static WeakReference<WhimTexDocument> sourceDocument;
        private static string marker;
        private static uint imageRevision;

        static ShaderFXClipboard()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        internal static ShaderFX Current
        {
            get
            {
                if (snapshot == null)
                {
                    if (!ReferenceEquals(snapshot, null)) Clear();
                    return null;
                }
                if (imageRevision != ImageClipboard.Revision || GUIUtility.systemCopyBuffer != marker)
                    Clear();
                return snapshot;
            }
        }

        internal static void Copy(WhimTexDocument document, ShaderFX value)
        {
            if (value == null) return;
            ShaderFX next = value.CloneForClipboard();

            string nextMarker = "WhimTex FX: " + Guid.NewGuid().ToString("N");
            try
            {
                GUIUtility.systemCopyBuffer = nextMarker;
            }
            catch
            {
                if (next != null) UnityEngine.Object.DestroyImmediate(next);
                throw;
            }

            Clear();
            snapshot = next;
            sourceDocument = document != null ? new WeakReference<WhimTexDocument>(document) : null;
            marker = nextMarker;
            imageRevision = ImageClipboard.Revision;
            Changed?.Invoke();
        }

        internal static ShaderFX CreatePasteValue(WhimTexDocument destination)
        {
            ShaderFX source = Current;
            if (source == null) return null;

            ShaderFX copy = source.CloneForDocument(destination);
            copy.DetachCatalog();
            bool sameDocument = sourceDocument != null && sourceDocument.TryGetTarget(out WhimTexDocument sourceOwner) &&
                sourceOwner != null && sourceOwner == destination;
            if (!sameDocument)
                copy.RemapTextureLayers(new Dictionary<string, string>(), clearExternal: true);
            return copy;
        }

        internal static void Clear()
        {
            if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
            snapshot = null;
            sourceDocument = null;
            marker = null;
            Changed?.Invoke();
        }
    }
}
