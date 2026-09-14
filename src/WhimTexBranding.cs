using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexBranding
    {
        private static Texture2D icon;
        private static Texture2D previewBackdrop;

        internal static Texture2D PreviewBackdrop
        {
            get
            {
                if (previewBackdrop == null)
                    previewBackdrop = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        AssetDatabase.GUIDToAssetPath("a693463e898e46f089f96a27a27c7451"));
                return previewBackdrop;
            }
        }

        internal static Texture2D Icon
        {
            get
            {
                if (icon == null)
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        AssetDatabase.GUIDToAssetPath("fd19ec41578c43f8b0439d8ae8b4d49e"));
                return icon;
            }
        }

        internal static GUIContent WindowTitle(string title) => new GUIContent(title, Icon);
    }
}
