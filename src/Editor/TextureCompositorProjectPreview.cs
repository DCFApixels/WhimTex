using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_4_OR_NEWER
using ProjectItemId = UnityEngine.EntityId;
#else
using ProjectItemId = System.Int32;
#endif

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class TextureCompositorProjectPreview
    {
        private static readonly Dictionary<ProjectItemId, Texture2D> Outputs = new Dictionary<ProjectItemId, Texture2D>();

        static TextureCompositorProjectPreview()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.projectWindowItemByEntityIdOnGUI += DrawProjectIcon;
#else
            EditorApplication.projectWindowItemInstanceOnGUI += DrawProjectIcon;
#endif
            EditorApplication.projectChanged += ClearCache;
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
            EditorApplication.quitting += Unregister;
        }

        private static void DrawProjectIcon(ProjectItemId itemId, Rect selectionRect)
        {
            if (Event.current.type != EventType.Repaint || selectionRect.height > 20f ||
                selectionRect.height < 1f || selectionRect.width < 16f)
                return;

            if (!Outputs.TryGetValue(itemId, out Texture2D output))
            {
                if (Outputs.Count >= 2048)
                    Outputs.Clear();
                if (AssetDatabase.IsMainAsset(itemId))
                {
                    string path = AssetDatabase.GetAssetPath(itemId);
                    if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
                        TextureCompositor document = TextureCompositor.FindDocument(main);
                        if (document != null)
                            output = main as Texture2D ?? document.OutputTexture;
                    }
                }
                Outputs[itemId] = output;
            }
            if (output == null)
                return;

            float size = Mathf.Min(16f, selectionRect.height);
            Rect icon = new Rect(selectionRect.x, selectionRect.y + (selectionRect.height - size) * 0.5f, size, size);
            Color previous = GUI.color;
            try
            {
                GUI.color = Color.white;
                EditorGUI.DrawRect(icon, EditorGUIUtility.isProSkin
                    ? new Color(0.18f, 0.18f, 0.18f, 1f)
                    : new Color(0.75f, 0.75f, 0.75f, 1f));
                EditorGUI.DrawTextureTransparent(icon, output, ScaleMode.ScaleToFit);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        internal static void ClearCache()
        {
            Outputs.Clear();
            EditorApplication.RepaintProjectWindow();
        }

        private static void Unregister()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.projectWindowItemByEntityIdOnGUI -= DrawProjectIcon;
#else
            EditorApplication.projectWindowItemInstanceOnGUI -= DrawProjectIcon;
#endif
            EditorApplication.projectChanged -= ClearCache;
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
            EditorApplication.quitting -= Unregister;
            Outputs.Clear();
        }
    }
}
