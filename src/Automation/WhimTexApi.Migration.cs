using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        /// <summary>Copies a readable legacy .asset document into a new TIFF without mutating the source.</summary>
        public static string Migrate(string sourcePath, string destinationPath, bool overwrite = false)
        {
            return Respond(() =>
            {
                string source = AssetPath(sourcePath, ".asset");
                string destination = TiffPath(destinationPath);
                Require(!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase),
                    "Source and destination must be different.", "invalid_path");
                string fullDestination = FullPath(destination);
                Require(overwrite || (!File.Exists(fullDestination) && !File.Exists(fullDestination + ".meta") &&
                    AssetDatabase.LoadMainAssetAtPath(destination) == null),
                    "Destination exists; set overwrite=true or choose a new TIFF path.", "already_exists");
                EnsureAssetFolder(destination);
                TextureCompositor legacy = Load(source);
                TextureCompositor inspection = null;
                try
                {
                    string saved = WhimTexDocumentFile.Save(legacy, destination);
                    inspection = Load(saved);
                    var result = Success();
                    result["sourcePath"] = source;
                    result["destinationPath"] = saved;
                    result["guid"] = AssetDatabase.AssetPathToGUID(saved);
                    result["document"] = Snapshot(inspection, saved);
                    return result;
                }
                finally
                {
                    // The legacy object belongs to AssetDatabase; the inspection copy does not.
                    ReleaseTransientDocument(legacy);
                    ReleaseTransientDocument(inspection);
                }
            });
        }
    }
}
