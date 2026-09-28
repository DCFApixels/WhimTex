using System;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Applies the document's own texture settings during its first import, so saving needs one import
    /// instead of two. The sRGB flag decides how Unity reads the 8-bit samples of the carrier, and the
    /// marker in the .meta makes document detection free afterwards. Later imports leave the settings
    /// alone; an Inspector encoding change queues a saved-composite rewrite after import.
    /// </summary>
    internal sealed class WhimTexDocumentPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            WhimTexDocumentSession.AfterImport(importedAssets);
            WhimTexOutputEncoding.AfterImport(importedAssets);
        }

        private void OnPreprocessTexture()
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(WhimTexTiffCarrier.Extension, StringComparison.OrdinalIgnoreCase) &&
                !assetPath.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)) return;
            if (!(assetImporter is TextureImporter importer)) return;
            WhimTexDocumentSession.BeforeImport(assetPath, importer);
            if (!string.IsNullOrEmpty(importer.userData) &&
                importer.userData.Contains(WhimTexTiffCarrier.MetaMarker)) return;
            if (!WhimTexTiffCarrier.TryReadCarrierFlags(assetPath, out bool srgb, out bool isDocument, out _) ||
                !isDocument) return;
            importer.userData = string.IsNullOrEmpty(importer.userData) ? WhimTexTiffCarrier.MetaMarker : importer.userData + "\n" + WhimTexTiffCarrier.MetaMarker;
            importer.sRGBTexture = srgb;
        }
    }
}
