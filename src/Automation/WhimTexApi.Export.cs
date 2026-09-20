using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        /// <summary>Exports a flattened document to a diagnostic image in Temp/WhimTex.</summary>
        public static string Export(string assetPath, string outputPath, int maxSize = 0, bool overwrite = false)
        {
            return Respond(() =>
            {
                string path = DocumentPath(assetPath);
                string extension = Path.GetExtension(outputPath ?? string.Empty).ToLowerInvariant();
                Require(extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".tga" || extension == ".exr",
                    "outputPath must end in .png, .jpg, .jpeg, .tga or .exr.", "invalid_path");
                Require(maxSize >= 0 && maxSize <= 4096, "maxSize must be 0..4096.");
                outputPath = outputPath.Replace('\\', '/');
                Require(outputPath.StartsWith("Temp/WhimTex/", StringComparison.Ordinal),
                    "Export output must be project-relative Temp/WhimTex/*.", "invalid_path");
                ValidateSegments(outputPath);
                string full = FullPath(outputPath);
                RejectLinks(full);
                Require(overwrite || !File.Exists(full), "Export exists; set overwrite=true or choose a new path.", "already_exists");
                RequireGraphics();
                TextureCompositor document = Load(path);
                Texture2D image = null;
                Texture2D encoded = null;
                try
                {
                    Require(!TextureCompositorWindow.IsDocumentBusyForApi(document), "Finish the current paint/transform gesture first.", "document_busy");
                    image = maxSize == 0 ? document.Compose() : document.ComposePreview(maxSize);
                    Require(image != null, "The document produced no export image.", "render_failed");
                    byte[] bytes;
                    if (extension == ".exr") bytes = image.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
                    else
                    {
                        encoded = HdrUtility.ToLdr(image, extension == ".jpg" || extension == ".jpeg");
                        bytes = extension == ".png" ? encoded.EncodeToPNG() :
                            extension == ".tga" ? encoded.EncodeToTGA() : encoded.EncodeToJPG(95);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    using (var stream = new FileStream(full, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                        stream.Write(bytes, 0, bytes.Length);
                    var result = Success();
                    result["assetPath"] = path;
                    result["outputPath"] = full;
                    result["format"] = extension.TrimStart('.').ToUpperInvariant();
                    result["width"] = image.width;
                    result["height"] = image.height;
                    result["bytes"] = bytes.Length;
                    result["revision"] = Revision(document);
                    return result;
                }
                finally
                {
                    if (encoded != null) Object.DestroyImmediate(encoded);
                    if (image != null) Object.DestroyImmediate(image);
                    ReleaseTransientDocument(document);
                }
            });
        }
    }
}
