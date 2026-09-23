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
        private static UnityEngine.Object snapshot;
        private static ShaderFX ownedEffect;
        private static WeakReference<TextureCompositor> sourceDocument;
        private static string marker;
        private static uint imageRevision;

        static ShaderFXClipboard()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        internal static UnityEngine.Object Current
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

        internal static void Copy(TextureCompositor document, UnityEngine.Object value)
        {
            ShaderFX nextOwned = value is ShaderFX effect ? effect.CloneForClipboard() : null;
            UnityEngine.Object next = nextOwned != null ? nextOwned : value is Material ? value : null;
            if (next == null)
            {
                if (nextOwned != null) UnityEngine.Object.DestroyImmediate(nextOwned);
                return;
            }

            string nextMarker = "WhimTex FX: " + Guid.NewGuid().ToString("N");
            try
            {
                GUIUtility.systemCopyBuffer = nextMarker;
            }
            catch
            {
                if (nextOwned != null) UnityEngine.Object.DestroyImmediate(nextOwned);
                throw;
            }

            Clear();
            snapshot = next;
            ownedEffect = nextOwned;
            sourceDocument = document != null ? new WeakReference<TextureCompositor>(document) : null;
            marker = nextMarker;
            imageRevision = ImageClipboard.Revision;
            Changed?.Invoke();
        }

        internal static UnityEngine.Object CreatePasteValue(TextureCompositor destination)
        {
            UnityEngine.Object value = Current;
            if (value is not ShaderFX source)
                return value;

            ShaderFX copy = source.CloneForDocument(destination);
            copy.DetachCatalog();
            bool sameDocument = sourceDocument != null && sourceDocument.TryGetTarget(out TextureCompositor sourceOwner) &&
                sourceOwner != null && sourceOwner == destination;
            if (!sameDocument)
                copy.RemapTextureLayers(new Dictionary<string, string>(), clearExternal: true);
            return copy;
        }

        internal static void Clear()
        {
            if (ownedEffect != null) UnityEngine.Object.DestroyImmediate(ownedEffect);
            snapshot = null;
            ownedEffect = null;
            sourceDocument = null;
            marker = null;
            Changed?.Invoke();
        }
    }
}
