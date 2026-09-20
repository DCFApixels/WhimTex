using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string FullPath(string relative) => Path.GetFullPath(Path.Combine(ProjectRoot, relative));

        private static string AssetPath(string path, string extension)
        {
            Require(!string.IsNullOrWhiteSpace(path), "assetPath is required.");
            path = path.Replace('\\', '/');
            Require(path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase),
                "Destinations must be inside Assets, outside StreamingAssets.", "invalid_path");
            Require(string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase), "Expected " + extension + " destination.", "invalid_path");
            ValidateSegments(path);
            RejectLinks(FullPath(path));
            return path;
        }

        private static string DocumentPath(string path)
        {
            Require(!string.IsNullOrWhiteSpace(path), "assetPath is required.");
            path = path.Replace('\\', '/');
            Require(path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase),
                "Destinations must be inside Assets, outside StreamingAssets.", "invalid_path");
            string extension = Path.GetExtension(path);
            Require(string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, WhimTexTiffCarrier.Extension, StringComparison.OrdinalIgnoreCase),
                "Expected a WhimTex .asset or .tiff destination.", "invalid_path");
            ValidateSegments(path);
            RejectLinks(FullPath(path));
            return path;
        }

        private static bool IsTiffPath(string path) =>
            string.Equals(Path.GetExtension(path), WhimTexTiffCarrier.Extension, StringComparison.OrdinalIgnoreCase);

        private static string TiffPath(string path)
        {
            path = DocumentPath(path);
            Require(IsTiffPath(path), "Expected a .tiff destination.", "invalid_path");
            return path;
        }

        private static void ReleaseTransientDocument(TextureCompositor document)
        {
            if (document != null && !AssetDatabase.Contains(document))
                Object.DestroyImmediate(document);
        }

        private static string ReadAssetPath(string path)
        {
            Require(!string.IsNullOrWhiteSpace(path), "A texture asset path is required.");
            path = path.Replace('\\', '/');
            Require(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal),
                "Texture source must be an imported Assets/ or Packages/ path.");
            ValidateSegments(path);
            return path;
        }

        private static void ValidateSegments(string path)
        {
            foreach (string part in path.Split('/'))
                Require(part.Length > 0 && part != "." && part != ".." && !part.EndsWith(".") && !part.EndsWith(" ") &&
                    part.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|', '\0' }) < 0,
                    "Invalid path segment: " + part, "invalid_path");
        }

        private static void RejectLinks(string fullPath)
        {
            string root = Path.GetFullPath(ProjectRoot);
            string current = fullPath;
            while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                Require(current != null && current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Path escapes the project.", "invalid_path");
                if (File.Exists(current) || Directory.Exists(current))
                    Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Writing through symlinks/junctions is not supported.", "invalid_path");
                current = Path.GetDirectoryName(current);
            }
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string folder = parts[0];
            for (int i = 1; i < parts.Length - 1; i++)
            {
                string next = folder + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(folder, parts[i])), "Could not create asset folder: " + next, "io_error");
                folder = next;
            }
        }

        private static TextureCompositor Load(string path)
        {
            if (IsTiffPath(path))
            {
                Require(WhimTexDocumentFile.IsDocument(path), "No WhimTex TIFF document at " + path, "document_not_found");
                return WhimTexDocumentFile.Load(path);
            }
            TextureCompositor document = TextureCompositor.FindDocument(AssetDatabase.LoadMainAssetAtPath(path));
            Require(document != null, "No WhimTex document at " + path, "document_not_found");
            return document;
        }

        public static string ImportImage(string sourcePath, string assetPath)
        {
            return Respond(() =>
            {
                Require(!string.IsNullOrEmpty(sourcePath) && Path.IsPathRooted(sourcePath), "sourcePath must be an absolute local image path.");
                string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                Require(extension == ".png" || extension == ".jpg" || extension == ".jpeg", "ImportImage supports PNG and JPEG.");
                string destination = AssetPath(assetPath, extension);
                Require(!File.Exists(FullPath(destination)) && !File.Exists(FullPath(destination) + ".meta") && AssetDatabase.LoadMainAssetAtPath(destination) == null,
                    "Destination exists; reuse its imported asset path or choose a new path.", "already_exists");
                var input = new FileInfo(sourcePath);
                Require(input.Exists && input.Length > 0 && input.Length <= 64 * 1024 * 1024, "Image must exist and be at most 64 MiB.");
                EnsureAssetFolder(destination);
                File.Copy(input.FullName, FullPath(destination), false);
                try
                {
                    AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(destination) as TextureImporter;
                    Require(importer != null, "Unity did not recognize the image.", "import_failed");
                    importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                    Require(sourceWidth > 0 && sourceHeight > 0 && (long)sourceWidth * sourceHeight <= MaxCanvasPixels,
                        "Imported image exceeds 16,777,216 pixels.", "resource_limit");
                    importer.textureType = TextureImporterType.Default;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = 16384;
                    importer.mipmapEnabled = false;
                    importer.isReadable = false;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.SaveAndReimport();
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
                    Require(texture != null, "Texture import failed.", "import_failed");
                    JObject result = Success();
                    result["assetPath"] = destination;
                    result["guid"] = AssetDatabase.AssetPathToGUID(destination);
                    result["width"] = texture.width;
                    result["height"] = texture.height;
                    return result;
                }
                catch (Exception exception)
                {
                    JObject result = Failure(exception);
                    result["assetPath"] = destination;
                    result["fileCreated"] = true;
                    result["recovery"] = "The copied file was retained. Inspect its import error; do not retry with overwrite.";
                    return result;
                }
            });
        }

        public static string Render(string assetPath, string outputPath, int maxSize = 1024, bool overwrite = false)
        {
            return Respond(() =>
            {
                string path = DocumentPath(assetPath);
                TextureCompositor document = Load(path);
                Texture2D preview = null;
                try
                {
                    Require(!TextureCompositorWindow.IsDocumentBusyForApi(document), "Finish the current paint/transform gesture first.", "document_busy");
                    Require(maxSize >= 1 && maxSize <= 4096, "maxSize must be 1..4096.");
                    Require(!string.IsNullOrEmpty(outputPath), "outputPath is required.");
                    outputPath = outputPath.Replace('\\', '/');
                    Require(outputPath.StartsWith("Temp/WhimTex/", StringComparison.Ordinal), "Preview output must be project-relative Temp/WhimTex/*.png.", "invalid_path");
                    ValidateSegments(outputPath);
                    Require(string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase), "Preview output must be PNG.");
                    string full = FullPath(outputPath);
                    RejectLinks(full);
                    Require(overwrite || !File.Exists(full), "Preview exists; choose a new path or explicitly set overwrite=true.", "already_exists");
                    RequireGraphics();
                    preview = document.ComposePreview(maxSize);
                    Require(preview != null, "No preview was generated.", "render_failed");
                    byte[] bytes;
                    Texture2D encoded = HdrUtility.ToLdr(preview);
                    try { bytes = encoded.EncodeToPNG(); }
                    finally { Object.DestroyImmediate(encoded); }
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    using (var stream = new FileStream(full, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                        stream.Write(bytes, 0, bytes.Length);
                    JObject result = Success();
                    result["outputPath"] = full;
                    result["width"] = preview.width;
                    result["height"] = preview.height;
                    result["revision"] = Revision(document);
                    return result;
                }
                finally
                {
                    if (preview != null) Object.DestroyImmediate(preview);
                    ReleaseTransientDocument(document);
                }
            });
        }
    }
}
