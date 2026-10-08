using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Menu entries and asset opening for documents stored in the carrier image format.
    ///
    /// TIFF files keep their native importer. Live Update temporarily enables Read/Write
    /// through the document session when it is needed.
    /// </summary>
    public sealed partial class TextureCompositorWindow
    {
        private void BindDocumentFile(string path)
        {
            ClearSourceImage();
            if (WhimTexDocumentService.PathOf(compositor) != path) WhimTexDocumentService.Bind(compositor, path);
            WhimTexDocumentService.Attach(this, compositor);
            RefreshDocumentTitle(true);
        }

        private void ClearDocumentFile()
        {
            WhimTexDocumentService.Detach(this);
            ClearSourceImage();
        }

        private void BindSourceImage(Texture2D texture)
        {
            sourceImage = texture;
            sourceImagePath = texture == null ? null : AssetDatabase.GetAssetPath(texture);
            RefreshDocumentTitle(true);
            toolkitDocumentField?.SetValueWithoutNotify(texture != null ? (UnityEngine.Object)texture : compositor);
        }

        private void ClearSourceImage()
        {
            sourceImage = null;
            sourceImagePath = null;
        }

        private static TextureCompositorWindow WindowFor(TextureCompositor document)
        {
            if (document == null) return null;
            if (focusedWindow is TextureCompositorWindow focused && focused.compositor == document) return focused;
            foreach (TextureCompositorWindow window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window != null && window.compositor == document) return window;
            return null;
        }

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
            if (document == null) return false;
            path = WhimTexDocumentService.PathOf(document);
            return !string.IsNullOrEmpty(path);
        }

        [MenuItem("Assets/WhimTex/Save Document As WhimTex File…", true)]
        private static bool ValidateSaveDocumentAsFile() => ActiveDocument() != null;

        [MenuItem("Assets/WhimTex/Save Document As WhimTex File…")]
        private static void SaveDocumentAsFile() => SaveDocumentAs(ActiveDocument());

        /// <summary>Saves into the document's own file, or asks for one when the document has none yet.</summary>
        private bool SaveDocument()
        {
            if (compositor == null) return true;
            if (TrySaveLinkedImage()) return true;
            if (TryGetDocumentFile(compositor, out string path))
            {
                if (WhimTexDocumentJson.IsJsonPath(path))
                    return SaveJsonToPath(path, compositor.JsonWriteMode);
                return SaveDocumentTo(compositor, path);
            }
            return SaveDocumentAs(compositor);
        }

        private void SaveDocumentAs() => SaveDocumentAs(compositor);

        private static bool SaveDocumentAs(TextureCompositor document)
        {
            if (document == null) return false;
            string suggested = TryGetDocumentFile(document, out string currentPath) ? Path.GetFileNameWithoutExtension(currentPath)
                : (string.IsNullOrEmpty(document.name) ? "WhimTex Document" : document.name);
            string path = EditorUtility.SaveFilePanelInProject("Save WhimTex Document", suggested, "tiff",
                "The document is stored as a TIFF image, so Unity imports it as a texture with full import settings.");
            if (string.IsNullOrEmpty(path)) return false;
            return SaveDocumentTo(document, path);
        }

        /// <summary>Writing pauses Live Update without toggling Read/Write around each save.</summary>
        private static bool SaveDocumentTo(TextureCompositor document, string path)
        {
            if (document == null || string.IsNullOrEmpty(path)) return false;
            using var operation = new WhimTexDocumentOperation("Save WhimTex document");
            try
            {
                TextureCompositorWindow owner = WindowFor(document);
                owner?.PrepareDocumentSave();
                if (!ConfirmIncompleteDocumentSave(document, ref path, out bool allowDataLoss)) return false;
                bool wasLive = WhimTexDocumentSession.IsLiveFor(document);
                string written = WhimTexDocumentFile.Save(document, path, deferImport: !wasLive, allowDataLoss: allowDataLoss);
                if (owner != null)
                {
                    owner.BindDocumentFile(written);
                    // The document is not an asset, so its dirty state lives on the window and has to be
                    // cleared here: otherwise the title keeps its asterisk and Save stays enabled.
                    owner.temporaryDocumentDirty = false;
                    owner.UpdateUnsavedChangesState();
                    owner.RefreshDocumentLoadWarning();
                }
                var image = AssetDatabase.LoadAssetAtPath<Texture2D>(written);
                if (image != null)
                {
                    Selection.activeObject = image;
                    EditorGUIUtility.PingObject(image);
                }
                return true;
            }
            catch (System.OperationCanceledException) { return false; }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("WhimTex", error.Message, "OK");
                return false;
            }
        }

        private static bool ConfirmIncompleteDocumentSave(TextureCompositor document, ref string path, out bool allowDataLoss)
        {
            allowDataLoss = false;
            if (string.IsNullOrEmpty(document.documentLoadWarning)) return true;
            int choice = EditorUtility.DisplayDialogComplex("Save an incompletely loaded document?",
                "Some document data could not be read:\n\n" + document.documentLoadWarning +
                "\n\nSaving will keep your edits and the data this version could read, but may permanently discard unread fields, layer types or asset references. " +
                "Save a copy to keep the original file for recovery, or cancel and reopen after restoring the required version or assets.",
                "Save a Copy…", "Cancel", "Save Anyway");
            if (choice == 1) return false;
            if (choice == 0)
            {
                string copyPath = EditorUtility.SaveFilePanelInProject("Save recovered document copy",
                    Path.GetFileNameWithoutExtension(path) + "_Recovered", Path.GetExtension(path).TrimStart('.'),
                    "The copy contains only the data that was loaded. Keep the original file to recover unread data later.");
                if (string.IsNullOrEmpty(copyPath)) return false;
                string sourcePath = WhimTexDocumentService.PathOf(document);
                if (string.Equals(Path.GetFullPath(copyPath), Path.GetFullPath(path), System.StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrEmpty(sourcePath) && string.Equals(Path.GetFullPath(copyPath), Path.GetFullPath(sourcePath), System.StringComparison.OrdinalIgnoreCase))
                {
                    EditorUtility.DisplayDialog("Choose a different file", "A recovery copy must not overwrite the original or selected destination. Choose Save Anyway if you intend to overwrite.", "OK");
                    return false;
                }
                path = copyPath;
            }
            else if (choice != 2) return false;
            allowDataLoss = true;
            return true;
        }

        private void OpenDocumentOutputSettings()
        {
            if (compositor == null) return;
            if (WhimTexDocumentJson.IsJsonPath(WhimTexDocumentService.PathOf(compositor)))
            {
                EditorUtility.DisplayDialog("JSON document", "JSON has no texture importer. Use Export or Save As TIFF to create an image asset with import settings.", "OK");
                return;
            }
            if (!TryGetDocumentFile(compositor, out string path))
            {
                if (!SaveDocument()) return;
                if (!TryGetDocumentFile(compositor, out path)) return;
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
            {
                Selection.activeObject = texture;
                EditorGUIUtility.PingObject(texture);
            }
        }

        [MenuItem("Assets/WhimTex/Toggle Live Update", true)]
        private static bool ValidateToggleLiveUpdate() => ActiveDocument() != null;

        [MenuItem("Assets/WhimTex/Toggle Live Update")]
        private static void ToggleLiveUpdate() => ToggleLiveUpdate(ActiveDocument());

        private static void ToggleLiveUpdate(TextureCompositor document)
        {
            if (WhimTexDocumentSession.IsLiveFor(document))
            {
                WhimTexDocumentSession.Stop("menu");
                RefreshLiveUpdateButtons();
                return;
            }
            if (document == null) return;
            if (!TryGetDocumentFile(document, out string path))
            {
                EditorUtility.DisplayDialog("WhimTex",
                    "Save the document as a WhimTex file first: Live Update edits the imported image of that file.", "OK");
                return;
            }
            if (!WhimTexDocumentSession.Start(document, path))
                EditorUtility.DisplayDialog("WhimTex", WhimTexDocumentSession.Status, "OK");
            RefreshLiveUpdateButtons();
        }

        /// <summary>The footer control lives on every window that shows the document.</summary>
        private static void RefreshLiveUpdateButtons()
        {
            foreach (TextureCompositorWindow window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window != null) window.RefreshLiveOutputButton();
        }

        [OnOpenAsset]
        private static bool OpenWhimTexDocument(
#if UNITY_6000_4_OR_NEWER
            EntityId entityId,
#else
            int entityId,
#endif
            int line)
        {
            var target =
#if UNITY_6000_4_OR_NEWER
                UnityObjectID.FromEntityId(entityId).Resolve();
#else
                UnityObjectID.FromInstanceId(entityId).Resolve();
#endif
            string path = target == null ? null : AssetDatabase.GetAssetPath(target);
            if (OpenWhimTexDocumentPath(path)) return true;
            if (WhimTexUserSettings.ImageOpening != ImageOpenMode.AllSupportedImages ||
                !IsSupportedImagePath(path)) return false;
            var texture = target as Texture2D ?? (target as Sprite)?.texture;
            return texture != null && OpenImageDocument(texture);
        }

        internal static bool IsSupportedImagePath(string path)
        {
            switch (Path.GetExtension(path ?? string.Empty).ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".bmp":
                case ".exr": case ".tif": case ".tiff": return true;
                default: return false;
            }
        }

        private static bool OpenImageDocument(Texture2D texture)
        {
            TextureCompositor document = null;
            TextureCompositorWindow window = null;
            try
            {
                document = CreateInstance<TextureCompositor>();
                document.hideFlags = HideFlags.HideAndDontSave;
                document.name = texture.name;
                document.width = texture.width;
                document.height = texture.height;
                var file = new FileLayerBehaviour();
                file.AssignSourceTexture(texture, document);
                LayerBehaviour layer = file;
                if (WhimTexUserSettings.ImageLayer == ImageOpenLayer.Drawing)
                {
                    if (TextureCompositor.TryDecodeOriginalFileTexture(texture, out Texture2D decoded))
                    {
                        document.width = decoded.width;
                        document.height = decoded.height;
                        var drawing = DrawingLayerBehaviour.FromMergedTexture(decoded);
                        drawing.StoredTexture.filterMode = texture.filterMode;
                        drawing.StoredTexture.wrapModeU = texture.wrapModeU;
                        drawing.StoredTexture.wrapModeV = texture.wrapModeV;
                        layer = drawing;
                    }
                    else
                    {
                        var previous = RenderTexture.active;
                        var surface = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                            RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                        try
                        {
                            Graphics.Blit(texture, surface);
                            var drawing = DrawingLayerBehaviour.FromMergedTexture(HdrUtility.ReadLinear(surface));
                            drawing.StoredTexture.filterMode = texture.filterMode;
                            drawing.StoredTexture.wrapModeU = texture.wrapModeU;
                            drawing.StoredTexture.wrapModeV = texture.wrapModeV;
                            layer = drawing;
                        }
                        finally
                        {
                            RenderTexture.active = previous;
                            RenderTexture.ReleaseTemporary(surface);
                        }
                    }
                }
                layer.layerName = texture.name;
                document.layers.Add(layer);
                document.NormalizeModel();
                window = CreateWindow<TextureCompositorWindow>("WhimTex", typeof(TextureCompositorWindow));
                window.SetCompositor(document);
                window.BindSourceImage(texture);
                window.temporaryDocumentDirty = true;
                window.ActivateSelectedLayer(layer.Id);
                window.UpdateUnsavedChangesState();
                window.Show();
                window.Repaint();
                return true;
            }
            catch (System.Exception exception)
            {
                if (window != null) window.Close();
                else if (document != null) DestroyImmediate(document);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("WhimTex", "Could not open the image: " + exception.Message, "OK");
                return true;
            }
        }

        internal static bool OpenWhimTexDocumentPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !WhimTexDocumentFile.IsDocument(path)) return false;
            // Unity's asset-open callback reports whether the request was handled, even on failure.
            try
            {
                if (!TryOpenWhimTexDocumentPath(path, out string error)) EditorUtility.DisplayDialog("WhimTex", error, "OK");
            }
            catch (System.OperationCanceledException) { }
            return true;
        }

        // Automation needs actual success and must not block on a modal error dialog.
        internal static bool TryOpenWhimTexDocumentPath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !WhimTexDocumentFile.IsDocument(path))
            { error = "Not a WhimTex document: " + path; return false; }
            foreach (TextureCompositorWindow existing in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (existing != null && TryGetDocumentFile(existing.compositor, out string openPath) &&
                    string.Equals(openPath, path, System.StringComparison.OrdinalIgnoreCase))
                {
                    existing.Show();
                    existing.Focus();
                    return true;
                }
            using var operation = new WhimTexDocumentOperation("Open WhimTex document");
            if (!WhimTexDocumentFile.TryLoad(path, out var document, out error)) return false;
            var window = CreateWindow<TextureCompositorWindow>("WhimTex", typeof(TextureCompositorWindow));
            window.SetCompositor(document);
            window.BindDocumentFile(path);
            window.Show();
            window.Repaint();
            return true;
        }
    }
}
