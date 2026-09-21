using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// One-way migration from the legacy ScriptableObject compositor asset to a native WhimTex TIFF.
    /// The legacy asset remains untouched and is still readable after the migration.
    /// </summary>
    internal static class WhimTexLegacyMigration
    {
        private const string MenuPath = "Assets/WhimTex/Migrate Legacy .asset to TIFF...";

        [MenuItem(MenuPath, true)]
        private static bool ValidateMenu()
        {
            return ResolveLegacyDocument(Selection.activeObject) != null;
        }

        [MenuItem(MenuPath)]
        private static void MigrateMenu()
        {
            TextureCompositor source = ResolveLegacyDocument(Selection.activeObject);
            if (source == null)
                return;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string suggested = Path.GetFileNameWithoutExtension(sourcePath);
            string destination = EditorUtility.SaveFilePanelInProject(
                "Migrate WhimTex legacy asset",
                string.IsNullOrEmpty(suggested) ? "WhimTex Document" : suggested,
                "tiff",
                "Create a native WhimTex TIFF and keep the legacy .asset unchanged.");
            if (string.IsNullOrEmpty(destination))
                return;

            if (!TryMigrate(source, destination, out string error))
            {
                Debug.LogError("WhimTex legacy migration failed: " + error, source);
                EditorUtility.DisplayDialog("WhimTex migration failed", error, "OK");
                return;
            }

            Texture2D result = AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
            if (result != null)
            {
                Selection.activeObject = result;
                EditorGUIUtility.PingObject(result);
            }
#if WHIMTEX_DEBUG
            Debug.Log("WhimTex: migrated legacy asset to " + destination, result);
#endif
        }

        internal static bool TryMigrate(TextureCompositor source, string destination, out string error)
        {
            error = null;
            if (source == null)
            {
                error = "The legacy document is missing.";
                return false;
            }
            if (!IsLegacyAsset(source))
            {
                error = "The selected object is not a legacy WhimTex .asset document.";
                return false;
            }
            if (string.IsNullOrEmpty(destination))
            {
                error = "The destination path is empty.";
                return false;
            }
            if (!string.Equals(Path.GetExtension(destination), ".tiff", StringComparison.OrdinalIgnoreCase))
            {
                error = "The migration destination must use the .tiff extension.";
                return false;
            }
            if (File.Exists(Path.GetFullPath(destination)))
            {
                error = "The destination file already exists. Choose a new path.";
                return false;
            }

            TextureCompositor copy = null;
            try
            {
                // CreateEditableCopy validates missing types/references and snapshots Drawing pixels
                // without changing the serialized legacy asset. Save then uses the normal TIFF writer.
                copy = WhimTexDocumentFile.CreateEditableCopy(source);
                WhimTexDocumentFile.Save(copy, destination, deferImport: false);
                if (!WhimTexDocumentFile.IsDocument(destination) ||
                    AssetDatabase.LoadAssetAtPath<Texture2D>(destination) == null)
                {
                    error = "The TIFF was written but could not be imported as a WhimTex document.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (copy != null)
                    UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        internal static bool IsLegacyAsset(TextureCompositor document)
        {
            if (document == null || !AssetDatabase.Contains(document))
                return false;
            string path = AssetDatabase.GetAssetPath(document);
            return string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase);
        }

        private static TextureCompositor ResolveLegacyDocument(UnityEngine.Object selected)
        {
            TextureCompositor document = TextureCompositor.FindDocument(selected);
            return IsLegacyAsset(document) ? document : null;
        }
    }
}
