using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        internal static string GetLinkedTexturePath(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw LinkedTextureError("The linked output texture is missing. Assign another texture or clear the field.");
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase))
                throw LinkedTextureError("Choose an image inside Assets, outside StreamingAssets and Packages.");
            string fullPath = Path.GetFullPath(path);
            string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw LinkedTextureError("The linked output texture must be inside Assets.");
            // Do not follow a file or folder junction when overwriting an explicitly linked asset.
            for (var item = new FileInfo(fullPath) as FileSystemInfo; item != null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw LinkedTextureError("Linked output cannot overwrite a symbolic link or junction.");
                if (string.Equals(item.FullName.TrimEnd(Path.DirectorySeparatorChar), assetsRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
            }
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".tga" && extension != ".jpg" && extension != ".jpeg" && extension != ".exr")
                throw LinkedTextureError("Linked output supports PNG, TGA, JPG and EXR images only.");
            if (!(AssetImporter.GetAtPath(path) is TextureImporter) || !(AssetDatabase.LoadMainAssetAtPath(path) is Texture2D))
                throw LinkedTextureError("Choose an image imported by Unity's TextureImporter, not a subasset or compositor.");
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw LinkedTextureError("The linked output file is read-only. Make it writable before saving.");
            return path;
        }

        private static OutputSettingsError LinkedTextureError(string message) => new OutputSettingsError(message, "linkedTextureGuid");

        private static byte[] EncodeLinkedTexture(RenderTexture composite, string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            Texture2D linear = HdrUtility.ReadLinear(composite);
            Texture2D ldr = null;
            try
            {
                if (extension == ".exr") return linear.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool srgb = importer.sRGBTexture && importer.textureType != TextureImporterType.NormalMap;
                bool jpeg = extension == ".jpg" || extension == ".jpeg";
                ldr = new Texture2D(linear.width, linear.height, TextureFormat.RGBA32, false, !srgb)
                { hideFlags = HideFlags.HideAndDontSave };
                using var pixels = HdrUtility.ReadPixels(linear, Unity.Collections.Allocator.Temp);
                var bytes = ldr.GetRawTextureData<Color32>();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color color = HdrUtility.Saturate(HdrUtility.Safe(pixels[i]));
                    if (srgb) color = HdrUtility.Encode(color);
                    if (jpeg) { color = Color.Lerp(Color.white, color, color.a); color.a = 1; }
                    bytes[i] = color;
                }
                ldr.Apply(false, false);
                return extension == ".png" ? ldr.EncodeToPNG() : extension == ".tga" ? ldr.EncodeToTGA() : ldr.EncodeToJPG(95);
            }
            finally
            {
                DestroyImmediate(linear);
                if (ldr != null) DestroyImmediate(ldr);
            }
        }

        private static void WriteLinkedTexture(string path, byte[] bytes)
        {
            if (bytes.Length == 0) throw LinkedTextureError("Unity returned no data for the linked output image.");
            string temporary = path + ".whimtex-" + Guid.NewGuid().ToString("N") + "~";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Replace(temporary, path, null);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception error)
            {
                throw new IOException("The document was saved, but its linked output could not be updated: " + path + ". " + error.Message, error);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
