using System;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexUserSettings
    {
        const string GraphCatalogThumbnailsKey = "DCFApixels.WhimTex.ShaderGraph.CatalogThumbnails";
        internal static event Action CatalogDisplayChanged;
        internal static bool GraphCatalogThumbnails
        {
            get => EditorPrefs.GetBool(GraphCatalogThumbnailsKey, true);
            set
            {
                if (GraphCatalogThumbnails == value) return;
                EditorPrefs.SetBool(GraphCatalogThumbnailsKey, value);
                CatalogDisplayChanged?.Invoke();
            }
        }
    }
}
