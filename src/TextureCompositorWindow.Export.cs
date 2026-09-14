using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private enum TextureExportFormat { Png, Jpeg, Tga, Exr, Asset }

        private void ShowExportMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("PNG (.png)"), false, () => ExportTexture(TextureExportFormat.Png));
            menu.AddItem(new GUIContent("JPEG (.jpg, white background)"), false, () => ExportTexture(TextureExportFormat.Jpeg));
            menu.AddItem(new GUIContent("TGA (.tga)"), false, () => ExportTexture(TextureExportFormat.Tga));
            menu.AddItem(new GUIContent("OpenEXR (.exr)"), false, () => ExportTexture(TextureExportFormat.Exr));
            menu.AddItem(new GUIContent("Layered PSD (.psd)"), false, ExportPsd);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Unity Texture2D (.asset)"), false, () => ExportTexture(TextureExportFormat.Asset));
            menu.ShowAsContext();
        }

        private void ExportPsd()
        {
            if (compositor == null) return;
            FinishPreviewTransform();
            FinishPaintingStroke();
            string path = EditorUtility.SaveFilePanel("Export layered PSD", Application.dataPath, "sprite", "psd");
            if (string.IsNullOrEmpty(path)) return;
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
                    Debug.Log("PSD export: " + path + "\n" + summary + "\n" + string.Join("\n", report.notes));
                    summary += "\n\nSome settings were rasterized or approximated. Details are in the Console.";
                }
                EditorUtility.DisplayDialog("PSD exported", summary, "OK");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("PSD export failed", exception.Message, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private void ExportTexture(TextureExportFormat format)
        {
            if (compositor == null)
                return;
            FinishPreviewTransform();
            FinishPaintingStroke();
            string extension = GetExportExtension(format);
            string path = format == TextureExportFormat.Asset
                ? EditorUtility.SaveFilePanelInProject("Export Unity Texture2D", "sprite", "asset",
                    "Save the flattened texture, without the layer tree.")
                : EditorUtility.SaveFilePanel("Export Sprite " + extension.ToUpperInvariant(), Application.dataPath, "sprite", extension);
            if (string.IsNullOrEmpty(path))
                return;

            Texture2D texture = null;
            try
            {
                string selectedExtension = Path.GetExtension(path);
                if (!string.Equals(selectedExtension, "." + extension, StringComparison.OrdinalIgnoreCase) &&
                    !(format == TextureExportFormat.Jpeg && string.Equals(selectedExtension, ".jpeg", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Choose a file with the ." + extension + " extension for this export format.");

                if (format == TextureExportFormat.Asset && !CanExportTextureAsset(path))
                    return;
                texture = compositor.Compose();
                if (format == TextureExportFormat.Asset)
                {
                    SaveExportedTextureAsset(texture, path);
                }
                else
                {
                    byte[] bytes = EncodeExportTexture(texture, format);
                    if (bytes == null || bytes.Length == 0)
                        throw new InvalidOperationException("Unity returned no image data for this format.");
                    File.WriteAllBytes(path, bytes);
                    ImportExportedTextureIfNeeded(path, format != TextureExportFormat.Exr);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Sprite export failed", exception.Message, "OK");
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
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static byte[] EncodeExportTexture(Texture2D texture, TextureExportFormat format)
        {
            if (format == TextureExportFormat.Exr) return texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            Texture2D ldr = HdrUtility.ToLdr(texture, format == TextureExportFormat.Jpeg);
            try
            {
                switch (format)
                {
                    case TextureExportFormat.Png: return ldr.EncodeToPNG();
                    case TextureExportFormat.Tga: return ldr.EncodeToTGA();
                    case TextureExportFormat.Jpeg: return ldr.EncodeToJPG(95);
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
