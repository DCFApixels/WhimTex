using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private TextureCompositor titleDocument;
        [NonSerialized] private string titleDocumentName;
        [NonSerialized] private string titleDocumentPath;
        [NonSerialized] private Texture2D sourceImage;
        [SerializeField] private string sourceImagePath;

        private void RestoreSourceImage()
        {
            if (sourceImage == null && !string.IsNullOrEmpty(sourceImagePath))
                sourceImage = AssetDatabase.LoadAssetAtPath<Texture2D>(sourceImagePath);
        }

        private void OnProjectChange() => RefreshDocumentTitle(true);

        private void RefreshDocumentTitle(bool force = false)
        {
            string documentName = compositor != null ? compositor.name : null;
            string path = TryGetDocumentFile(compositor, out string filePath) ? filePath : sourceImagePath;
            if (string.IsNullOrEmpty(path) && compositor != null)
                path = AssetDatabase.GetAssetPath(compositor);
            if (!force && titleDocument == compositor && titleDocumentName == documentName && titleDocumentPath == path) return;
            titleDocument = compositor;
            titleDocumentName = documentName;
            titleDocumentPath = path;
            string title = !string.IsNullOrEmpty(path) ? Path.GetFileNameWithoutExtension(path) : documentName;
            if (path != null && (path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)) && title.EndsWith(".whimtex", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(0, title.Length - ".whimtex".Length);
            if (string.IsNullOrWhiteSpace(title)) title = "Untitled";
            var content = WhimTexBranding.WindowTitle(title);
            content.tooltip = string.IsNullOrEmpty(path) ? "WhimTex — " + title : "WhimTex — " + path;
            titleContent = content;
        }
    }
}
