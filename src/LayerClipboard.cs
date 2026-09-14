using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class LayerClipboard
    {
        private static TextureCompositor snapshot;
        private static string marker;
        private static uint revision;

        static LayerClipboard()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        internal static TextureCompositor Current
        {
            get
            {
                if (snapshot != null && (revision != ImageClipboard.Revision || GUIUtility.systemCopyBuffer != marker))
                    Clear();
                return snapshot;
            }
        }

        internal static void Copy(TextureCompositor source, List<Layer> roots)
        {
            if (roots.Count == 0) throw new InvalidOperationException("Select a layer to copy.");
            TextureCompositor next = source.CaptureLayerClipboard(roots);
            try
            {
                string nextMarker = "WhimTex layers: " + Guid.NewGuid().ToString("N");
                GUIUtility.systemCopyBuffer = nextMarker;
                Clear();
                snapshot = next;
                marker = nextMarker;
                revision = ImageClipboard.Revision;
            }
            catch { UnityEngine.Object.DestroyImmediate(next); throw; }
        }

        internal static void Clear()
        {
            if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
            snapshot = null;
            marker = null;
        }
    }
}
