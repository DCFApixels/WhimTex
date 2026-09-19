using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Menu entries and asset opening for documents stored in the carrier image format.
    ///
    /// This is an addition, not a replacement: the legacy <c>.asset</c> path keeps working, and a
    /// document switches to the new format only when it is explicitly saved as one. Live Update works
    /// on the carrier image, so it needs Read/Write enabled in that image's .meta and a document that
    /// already has a file.
    /// </summary>
    public sealed partial class TextureCompositorWindow
    {
        private static readonly Dictionary<TextureCompositor, string> DocumentFiles = new Dictionary<TextureCompositor, string>();

        private static TextureCompositor ActiveDocument()
        {
            if (focusedWindow is TextureCompositorWindow focused && focused.compositor != null) return focused.compositor;
            foreach (TextureCompositorWindow window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window != null && window.compositor != null) return window.compositor;
            return null;
        }

        private static bool TryGetDocumentFile(TextureCompositor document, out string path)
        {
            path = null;
            return document != null && DocumentFiles.TryGetValue(document, out path) &&
                   !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        [MenuItem("Assets/WhimTex/Save Document As WhimTex File…", true)]
        private static bool ValidateSaveDocumentAsFile() => ActiveDocument() != null;

        [MenuItem("Assets/WhimTex/Save Document As WhimTex File…")]
        private static void SaveDocumentAsFile()
        {
            TextureCompositor document = ActiveDocument();
            if (document == null) return;
            string suggested = (string.IsNullOrEmpty(document.name) ? "WhimTex Document" : document.name) + ".whimtex";
            string path = EditorUtility.SaveFilePanelInProject("Save WhimTex Document", suggested, "tiff",
                "The document is stored as a TIFF image, so Unity imports it as a texture with full import settings.");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                bool wasLive = WhimTexDocumentSession.IsLive;
                WhimTexDocumentSession.Stop("document save");
                string written = WhimTexDocumentFile.Save(document, path);
                DocumentFiles[document] = written;
                if (wasLive) WhimTexDocumentSession.Start(document, written);
                var image = AssetDatabase.LoadAssetAtPath<Texture2D>(written);
                if (image != null)
                {
                    Selection.activeObject = image;
                    EditorGUIUtility.PingObject(image);
                }
                Debug.Log("WhimTex: document saved to " + written);
            }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("WhimTex", error.Message, "OK");
            }
        }

        [MenuItem("Assets/WhimTex/Toggle Live Update", true)]
        private static bool ValidateToggleLiveUpdate() => ActiveDocument() != null;

        [MenuItem("Assets/WhimTex/Toggle Live Update")]
        private static void ToggleLiveUpdate()
        {
            if (WhimTexDocumentSession.IsLive)
            {
                WhimTexDocumentSession.Stop("menu");
                return;
            }
            TextureCompositor document = ActiveDocument();
            if (document == null) return;
            if (!TryGetDocumentFile(document, out string path))
            {
                EditorUtility.DisplayDialog("WhimTex",
                    "Save the document as a WhimTex file first: Live Update edits the imported image of that file.", "OK");
                return;
            }
            if (!EnsureReadable(path)) return;
            if (!WhimTexDocumentSession.Start(document, path))
                EditorUtility.DisplayDialog("WhimTex", WhimTexDocumentSession.Status, "OK");
        }

        /// <summary>
        /// Live Update needs a CPU-accessible texture. The flag lives in the .meta, so enabling it is
        /// reported rather than hidden: it is a change to the imported asset, not to the document.
        /// </summary>
        private static bool EnsureReadable(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return true;
            if (importer.isReadable) return true;
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("WhimTex: Read/Write enabled on " + path + " for Live Update. " +
                      "It is stored in the .meta and can be turned off in the texture inspector.");
            return true;
        }

        [OnOpenAsset]
        private static bool OpenWhimTexDocument(EntityId entityId, int line)
        {
            var target = EditorUtility.EntityIdToObject(entityId);
            string path = target == null ? null : AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path) || !WhimTexDocumentFile.IsDocument(path)) return false;
            if (!WhimTexDocumentFile.TryLoad(path, out TextureCompositor document, out string error))
            {
                EditorUtility.DisplayDialog("WhimTex", error, "OK");
                return true;
            }
            var window = CreateWindow<TextureCompositorWindow>("WhimTex", typeof(TextureCompositorWindow));
            window.SetCompositor(document);
            DocumentFiles[document] = path;
            window.Show();
            window.Repaint();
            return true;
        }
    }
}
