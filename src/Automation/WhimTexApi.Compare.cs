using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        /// <summary>Compares two documents without modifying either one. Rendering is optional.</summary>
        public static string Compare(string leftPath, string rightPath, bool render = false, int maxSize = 1024)
        {
            return Respond(() =>
            {
                string left = DocumentPath(leftPath);
                string right = DocumentPath(rightPath);
                Require(!string.Equals(left, right, StringComparison.OrdinalIgnoreCase) || FileExists(left),
                    "At least one comparison path must be different or point to an existing document.", "invalid_path");
                Require(!render || maxSize >= 1 && maxSize <= 4096, "maxSize must be 1..4096.");
                TextureCompositor leftDocument = null, rightDocument = null;
                Texture2D leftPreview = null, rightPreview = null;
                try
                {
                    leftDocument = Load(left);
                    rightDocument = Load(right);
                    Require(!TextureCompositorWindow.IsDocumentBusyForApi(leftDocument) &&
                        !TextureCompositorWindow.IsDocumentBusyForApi(rightDocument),
                        "Finish the current paint/transform gesture first.", "document_busy");
                    var result = Success();
                    result["leftPath"] = left;
                    result["rightPath"] = right;
                    result["leftRevision"] = Revision(leftDocument);
                    result["rightRevision"] = Revision(rightDocument);
                    result["modelEqual"] = string.Equals((string)result["leftRevision"], (string)result["rightRevision"], StringComparison.Ordinal);
                    result["renderRequested"] = render;
                    bool storageEqual = StorageEqual(left, right, out JObject storage);
                    result["storageEqual"] = (bool)storage["available"] ? storageEqual : (JToken)JValue.CreateNull();
                    result["storage"] = storage;
                    if (render)
                    {
                        RequireGraphics();
                        leftPreview = leftDocument.ComposePreview(maxSize);
                        rightPreview = rightDocument.ComposePreview(maxSize);
                        Require(leftPreview != null && rightPreview != null, "One of the documents produced no preview.", "render_failed");
                        string leftRender = TextureHash(leftPreview);
                        string rightRender = TextureHash(rightPreview);
                        result["leftRenderHash"] = leftRender;
                        result["rightRenderHash"] = rightRender;
                        result["renderEqual"] = leftPreview.width == rightPreview.width && leftPreview.height == rightPreview.height &&
                            string.Equals(leftRender, rightRender, StringComparison.Ordinal);
                        result["renderWidth"] = leftPreview.width;
                        result["renderHeight"] = leftPreview.height;
                    }
                    return result;
                }
                finally
                {
                    if (leftPreview != null) Object.DestroyImmediate(leftPreview);
                    if (rightPreview != null) Object.DestroyImmediate(rightPreview);
                    ReleaseTransientDocument(leftDocument);
                    ReleaseTransientDocument(rightDocument);
                }
            });
        }

        private static bool FileExists(string path) => System.IO.File.Exists(FullPath(path));

        private static bool StorageEqual(string left, string right, out JObject details)
        {
            details = new JObject();
            if (!IsTiffPath(left) || !IsTiffPath(right))
            {
                details["available"] = false;
                return false;
            }
            var a = WhimTexDocumentFile.InspectStorage(left);
            var b = WhimTexDocumentFile.InspectStorage(right);
            var leftBlocks = new JObject();
            var rightBlocks = new JObject();
            for (int i = 0; i < a.names.Length; i++) leftBlocks[a.names[i]] = a.sizes[i];
            for (int i = 0; i < b.names.Length; i++) rightBlocks[b.names[i]] = b.sizes[i];
            details["available"] = true;
            details["left"] = leftBlocks;
            details["right"] = rightBlocks;
            return JObject.DeepEquals(leftBlocks, rightBlocks);
        }

        private static string TextureHash(Texture2D texture)
        {
            Require(texture.isReadable, "The comparison preview is not readable.", "render_failed");
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(texture.GetRawTextureData())).Replace("-", "").ToLowerInvariant();
        }
    }
}
