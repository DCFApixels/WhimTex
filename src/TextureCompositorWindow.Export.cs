using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        internal enum TextureExportFormat { Png, Jpeg, Tga, Exr, Asset, Psd, Json }

        private void ShowExportWindow()
        {
            if (compositor != null) WhimTexExportWindow.Open(this, compositor);
        }

        internal bool ExportDocumentFromDialog(TextureCompositor source, WhimTexExportOptions options)
        {
            CheckExportSource(source);
            options.Validate();
            PrepareDocumentSave();
            string extension = GetExportExtension(options.format);
            string filename = string.IsNullOrEmpty(source.name) ? "WhimTex Document" : source.name;
            string path = options.format == TextureExportFormat.Asset || options.format == TextureExportFormat.Json
                ? EditorUtility.SaveFilePanelInProject("Export WhimTex", filename, extension,
                    options.format == TextureExportFormat.Json ? "Save editable settings without Drawing pixels." : "Save the flattened texture without layers.")
                : EditorUtility.SaveFilePanel("Export WhimTex", Application.dataPath, filename, extension);
            return ExportDocumentToPath(source, options, path);
        }

        private void CheckExportSource(TextureCompositor source)
        {
            if (source == null || compositor != source)
                throw new InvalidOperationException("The source document is no longer open here. Reopen Export from the document you want to export.");
        }

        internal bool ExportDocumentToPath(TextureCompositor source, WhimTexExportOptions options, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            CheckExportSource(source);
            options.Validate();
            PrepareDocumentSave();
            string extension = GetExportExtension(options.format);
            string selectedExtension = Path.GetExtension(path);
            bool correctExtension = options.format == TextureExportFormat.Json ? WhimTexDocumentJson.IsJsonPath(path) :
                string.Equals(selectedExtension, "." + extension, StringComparison.OrdinalIgnoreCase) ||
                options.format == TextureExportFormat.Jpeg && string.Equals(selectedExtension, ".jpeg", StringComparison.OrdinalIgnoreCase);
            if (!correctExtension) throw new InvalidOperationException("Choose a file with the ." + extension + " extension for this export format.");
            if (options.format == TextureExportFormat.Json)
            {
                var jsonOptions = new WhimTexJsonWriteOptions { Mode = options.jsonMode, AllowDrawingOmission = true };
                var written = WhimTexDocumentJson.Write(source, jsonOptions);
                if (!ConfirmJsonDrawingOmission(written, true)) return false;
                using var operation = new WhimTexDocumentOperation("Export WhimTex JSON");
                WhimTexDocumentFile.ExportJson(source, path, jsonOptions);
                return true;
            }
            if (options.format == TextureExportFormat.Psd) return ExportPsdToPath(path);
            return ExportTextureToPath(options, path);
        }

        private bool ExportPsdToPath(string path)
        {
            try
            {
                PsdExportReport report = WhimTexPsdExporter.Export(compositor, path, overwrite: true,
                    progress: (label, amount) =>
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Export layered PSD", label, amount))
                            throw new OperationCanceledException();
                    });
                EditorUtility.ClearProgressBar();
                ImportExportedTextureIfNeeded(path, true);
                string summary = $"Exported {report.layerCount} layers and {report.groupCount} groups.\n" +
                    $"Editable fills: {report.editableFillCount}. Editable outlines: {report.editableOutlineCount}.";
                if (report.notes.Count > 0)
                {
                    summary += "\n" + string.Join("\n", report.notes);
                }
                Debug.Log("WhimTex PSD export: " + path + "\n" + summary);
                return true;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private bool ExportTextureToPath(WhimTexExportOptions options, string path)
        {
            TextureExportFormat format = options.format;
            Texture2D texture = null;
            try
            {
                if (format == TextureExportFormat.Asset && !CanExportTextureAsset(path))
                    return false;
                texture = compositor.Compose();
                if (format == TextureExportFormat.Asset)
                {
                    SaveExportedTextureAsset(texture, path);
                }
                else
                {
                    byte[] bytes = EncodeExportTextureWithOptions(texture, format, options.jpegQuality, options.ExrFlags);
                    if (bytes == null || bytes.Length == 0)
                        throw new InvalidOperationException("Unity returned no image data for this format.");
                    File.WriteAllBytes(path, bytes);
                    ImportExportedTextureIfNeeded(path, format != TextureExportFormat.Exr);
                }
                return true;
            }
            finally
            {
                if (texture != null && !AssetDatabase.Contains(texture))
                    DestroyImmediate(texture);
            }
        }

        private bool TrySaveLinkedImage()
        {
            if (compositor == null || sourceImage == null || compositor.layers == null || compositor.layers.Count != 1 ||
                string.IsNullOrEmpty(sourceImagePath))
                return false;

            TextureExportFormat format;
            switch (Path.GetExtension(sourceImagePath).ToLowerInvariant())
            {
                case ".png": format = TextureExportFormat.Png; break;
                case ".jpg": case ".jpeg": format = TextureExportFormat.Jpeg; break;
                case ".tga": format = TextureExportFormat.Tga; break;
                case ".exr": format = TextureExportFormat.Exr; break;
                case ".asset": format = TextureExportFormat.Asset; break;
                default: return false;
            }

            FinishPreviewTransform();
            FinishPaintingStroke();
            Texture2D texture = null;
            try
            {
                texture = compositor.Compose();
                if (format == TextureExportFormat.Asset)
                {
                    SaveExportedTextureAsset(texture, sourceImagePath);
                }
                else
                {
                    byte[] bytes = EncodeExportTexture(texture, format);
                    if (bytes == null || bytes.Length == 0)
                        throw new InvalidOperationException("Unity returned no image data for this format.");
                    string outputPath = Path.IsPathFullyQualified(sourceImagePath)
                        ? sourceImagePath
                        : Path.Combine(Directory.GetParent(Application.dataPath).FullName, sourceImagePath);
                    File.WriteAllBytes(outputPath, bytes);
                    if (sourceImagePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        AssetDatabase.ImportAsset(sourceImagePath, ImportAssetOptions.ForceSynchronousImport);
                }
                sourceImage = AssetDatabase.LoadAssetAtPath<Texture2D>(sourceImagePath) ?? sourceImage;
                temporaryDocumentDirty = false;
                UpdateUnsavedChangesState();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("WhimTex save failed", exception.Message, "OK");
                return false;
            }
            finally
            {
                if (texture != null && !AssetDatabase.Contains(texture))
                    DestroyImmediate(texture);
            }
        }

        private static string GetExportExtension(TextureExportFormat format)
        {
            switch (format)
            {
                case TextureExportFormat.Png: return "png";
                case TextureExportFormat.Jpeg: return "jpg";
                case TextureExportFormat.Tga: return "tga";
                case TextureExportFormat.Exr: return "exr";
                case TextureExportFormat.Asset: return "asset";
                case TextureExportFormat.Psd: return "psd";
                case TextureExportFormat.Json: return WhimTexDocumentJson.Extension.TrimStart('.');
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static byte[] EncodeExportTexture(Texture2D texture, TextureExportFormat format)
            => EncodeExportTextureWithOptions(texture, format, 95, Texture2D.EXRFlags.CompressZIP);

        private static byte[] EncodeExportTextureWithOptions(Texture2D texture, TextureExportFormat format, int jpegQuality, Texture2D.EXRFlags exrFlags)
        {
            if (format == TextureExportFormat.Exr) return texture.EncodeToEXR(exrFlags);
            Texture2D ldr = HdrUtility.ToLdr(texture, format == TextureExportFormat.Jpeg);
            try
            {
                switch (format)
                {
                    case TextureExportFormat.Png: return ldr.EncodeToPNG();
                    case TextureExportFormat.Tga: return ldr.EncodeToTGA();
                    case TextureExportFormat.Jpeg: return ldr.EncodeToJPG(jpegQuality);
                    default: throw new ArgumentOutOfRangeException(nameof(format));
                }
            }
            finally { DestroyImmediate(ldr); }
        }

        private static bool CanExportTextureAsset(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the texture inside Assets, outside StreamingAssets.");

            string assetsRoot = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, path.Substring("Assets/".Length))).Replace('\\', '/');
            if (!fullPath.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(assetsRoot + "/StreamingAssets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the texture inside Assets, outside StreamingAssets.");

            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing == null && !File.Exists(fullPath))
                return true;
            if (!(existing is Texture2D) || AssetDatabase.LoadAllAssetsAtPath(path).Length != 1)
                throw new InvalidOperationException("This path belongs to another asset or contains sub-assets. Choose a different file to avoid losing data.");
            return EditorUtility.DisplayDialog("Replace Texture Asset?",
                "Replace the pixels in " + path + "? Existing references to this texture will be preserved.", "Replace", "Cancel");
        }

        private static void SaveExportedTextureAsset(Texture2D texture, string path)
        {
            texture.name = Path.GetFileNameWithoutExtension(path);
            texture.hideFlags = HideFlags.None;
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(texture, existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(texture, path);
                AssetDatabase.SaveAssetIfDirty(texture);
                Selection.activeObject = texture;
                EditorGUIUtility.PingObject(texture);
            }
        }
    }
}
