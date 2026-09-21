using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_4_OR_NEWER
using ProjectItemId = UnityEngine.EntityId;
#endif

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class TextureCompositorProjectPreview
    {
        // Legacy fallback for the small Project-window icons of old .asset documents.
        // TIFF documents use Unity's native TextureImporter previews and do not need this
        // callback. Keep the implementation available for compatibility, but leave it
        // disabled while the legacy icon path is not required.
        // Keep the compatibility path available without making the compiler treat its
        // guarded body as unreachable code. TIFF documents use Unity's native preview.
        private static readonly bool EnableLegacyAssetProjectIcons = false;

        private static readonly Dictionary<UnityObjectID, Texture2D> Outputs = new Dictionary<UnityObjectID, Texture2D>();

        static TextureCompositorProjectPreview()
        {
            if (EnableLegacyAssetProjectIcons)
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
        }

        private static void DrawProjectIcon(
#if UNITY_6000_4_OR_NEWER
            ProjectItemId itemId,
#else
            int itemId,
#endif
            Rect selectionRect)
        {
            UnityObjectID objectId =
#if UNITY_6000_4_OR_NEWER
                UnityObjectID.FromEntityId(itemId);
#else
                UnityObjectID.FromInstanceId(itemId);
#endif
            if (Event.current.type != EventType.Repaint || selectionRect.height > 20f ||
                selectionRect.height < 1f || selectionRect.width < 16f)
                return;

            if (!Outputs.TryGetValue(objectId, out Texture2D output))
            {
                if (Outputs.Count >= 2048)
                    Outputs.Clear();
                UnityEngine.Object mainAsset = objectId.Resolve();
                if (mainAsset != null && AssetDatabase.IsMainAsset(mainAsset))
                {
                    string path = AssetDatabase.GetAssetPath(mainAsset);
                    if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
                        TextureCompositor document = TextureCompositor.FindDocument(main);
                        if (document != null)
                            output = main as Texture2D ?? document.OutputTexture;
                    }
                }
                Outputs[objectId] = output;
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
            if (!EnableLegacyAssetProjectIcons)
                return;

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
