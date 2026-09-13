using System;
using System.IO;
using UnityEditor;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private TextureCompositor titleDocument;
        [NonSerialized] private string titleDocumentName;

        private void OnProjectChange() => RefreshDocumentTitle(true);

        private void RefreshDocumentTitle(bool force = false)
        {
            string documentName = compositor != null ? compositor.name : null;
            if (!force && titleDocument == compositor && titleDocumentName == documentName) return;
            titleDocument = compositor;
            titleDocumentName = documentName;
            string path = compositor != null ? AssetDatabase.GetAssetPath(compositor) : null;
            string title = !string.IsNullOrEmpty(path) ? Path.GetFileNameWithoutExtension(path) : documentName;
            if (string.IsNullOrWhiteSpace(title)) title = "Untitled";
            var content = SpriteEditorBranding.WindowTitle(title);
            content.tooltip = string.IsNullOrEmpty(path) ? "WhimTex — " + title : "WhimTex — " + path;
            titleContent = content;
        }
    }
}
