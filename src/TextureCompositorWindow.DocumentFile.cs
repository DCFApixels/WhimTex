using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Menu entries and asset opening for documents stored in the carrier image format.
    ///
    /// TIFF files keep their native importer. Legacy .asset files remain readable for migration only;
    /// the window never writes them back. Live Update temporarily enables Read/Write through the
    /// document session when it is needed.
    /// </summary>
    public sealed partial class TextureCompositorWindow
    {
        /// <summary>Compatibility with existing serialized windows; new identity lives on the document.</summary>
        [SerializeField] private string documentFilePath;
        [SerializeField] private string documentFileGuid;
        [SerializeField] private TextureCompositor documentFileOwner;

        private void BindDocumentFile(string path)
        {
            ClearSourceImage();
            if (WhimTexDocumentService.PathOf(compositor) != path) WhimTexDocumentService.Bind(compositor, path);
            WhimTexDocumentService.Attach(this, compositor);
            documentFileOwner = compositor;
            documentFilePath = path;
            documentFileGuid = AssetDatabase.AssetPathToGUID(path);
            RefreshDocumentTitle(true);
        }

        private void ClearDocumentFile()
        {
            WhimTexDocumentService.Detach(this);
            ClearSourceImage();
            documentFileOwner = null;
            documentFileGuid = null;
            documentFilePath = null;
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
            if (!string.IsNullOrEmpty(path)) return true;
            TextureCompositorWindow owner = WindowFor(document);
            path = null;
            // Upgrade a window serialized before bindings stored their owner/GUID. The imported
            // output is proof of ownership; a stale window path alone must never be trusted.
            if (owner != null && owner.documentFileOwner == null && string.IsNullOrEmpty(owner.documentFileGuid) &&
                !string.IsNullOrEmpty(owner.documentFilePath) && document.OutputTexture != null &&
                AssetDatabase.GetAssetPath(document.OutputTexture) == owner.documentFilePath)
                owner.BindDocumentFile(owner.documentFilePath);
            if (owner == null || owner.documentFileOwner != document) return false;
            path = AssetDatabase.GUIDToAssetPath(owner.documentFileGuid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            owner.documentFilePath = path;
            owner.BindDocumentFile(path);
            return true;
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
                // Legacy ScriptableObject documents remain openable for migration, but are
                // permanently read-only. Never let Ctrl+S overwrite the .asset; route it to
                // the explicit TIFF Save As flow instead.
                if (IsLegacyAssetPath(path))
                    return SaveDocumentAs(compositor);
                return SaveDocumentTo(compositor, path);
            }
            if (WhimTexLegacyMigration.IsLegacyAsset(compositor))
                return SaveDocumentAs(compositor);
            return SaveDocumentAs(compositor);
        }

        private void SaveDocumentAs() => SaveDocumentAs(compositor);

        internal static bool SaveDocumentAsTiff(TextureCompositor document) => SaveDocumentAs(document);

        internal static bool IsLegacyAssetPath(string path) =>
            !string.IsNullOrEmpty(path) && string.Equals(Path.GetExtension(path), ".asset", System.StringComparison.OrdinalIgnoreCase);

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
            if (IsLegacyAssetPath(path))
            {
                EditorUtility.DisplayDialog("Legacy WhimTex asset is read-only",
                    "Legacy .asset documents can no longer be saved in place. Use Save As to create a TIFF document.", "OK");
                return false;
            }
            TextureCompositor copy = null;
            using var operation = new WhimTexDocumentOperation("Save WhimTex document");
            try
            {
                TextureCompositorWindow owner = WindowFor(document);
                owner?.PrepareDocumentSave();
                if (AssetDatabase.Contains(document))
                    copy = WhimTexDocumentFile.CreateEditableCopy(document);
                var target = copy != null ? copy : document;
                bool wasLive = WhimTexDocumentSession.IsLiveFor(document);
                string written = WhimTexDocumentFile.Save(target, path, deferImport: !wasLive);
                if (owner != null)
                {
                    if (copy != null)
                    {
                        WhimTexApi.TransferLiveDocument(owner.agentSessionId, document, copy);
                        owner.agentSessionDocument = copy;
                        owner.SetCompositor(copy);
                        copy = null; // window now owns the working document
                    }
                    owner.BindDocumentFile(written);
                    // The document is not an asset, so its dirty state lives on the window and has to be
                    // cleared here: otherwise the title keeps its asterisk and Save stays enabled.
                    owner.temporaryDocumentDirty = false;
                    owner.UpdateUnsavedChangesState();
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
            finally { if (copy != null) DestroyImmediate(copy); }
        }

        private void OpenDocumentOutputSettings()
        {
            if (compositor == null) return;
            if (AssetDatabase.Contains(compositor)) { WhimTexOutputSettingsWindow.Open(compositor); return; }
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
            if (TextureCompositor.FindDocument(target) != null) return false;
            var texture = target as Texture2D ?? (target as Sprite)?.texture;
            return texture != null && OpenImageDocument(texture);
        }

        internal static bool IsSupportedImagePath(string path)
        {
            switch (Path.GetExtension(path ?? string.Empty).ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".bmp":
                case ".exr": case ".tif": case ".tiff": case ".asset": return true;
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

        private static bool OpenWhimTexDocumentPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !WhimTexDocumentFile.IsDocument(path)) return false;
            foreach (TextureCompositorWindow existing in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (existing != null && TryGetDocumentFile(existing.compositor, out string openPath) &&
                    string.Equals(openPath, path, System.StringComparison.OrdinalIgnoreCase))
                {
                    existing.Show();
                    existing.Focus();
                    return true;
                }
            using var operation = new WhimTexDocumentOperation("Open WhimTex document");
            TextureCompositor document;
            try
            {
                if (!WhimTexDocumentFile.TryLoad(path, out document, out string error))
                {
                    EditorUtility.DisplayDialog("WhimTex", error, "OK");
                    return true;
                }
            }
            catch (System.OperationCanceledException) { return true; }
            var window = CreateWindow<TextureCompositorWindow>("WhimTex", typeof(TextureCompositorWindow));
            window.SetCompositor(document);
            window.BindDocumentFile(path);
            window.Show();
            window.Repaint();
            return true;
        }
    }
}
