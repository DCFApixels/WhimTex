using System;
using System.IO;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        /// <summary>Validates a staged TIFF and recovers it to a new project asset without deleting the source.</summary>
        public static string Recover(string sourcePath, string destinationPath)
        {
            return Respond(() =>
            {
                string source = StagedPath(sourcePath);
                string destination = TiffPath(destinationPath);
                string fullDestination = FullPath(destination);
                Require(File.Exists(source), "The staged TIFF does not exist.", "document_not_found");
                Require(!File.Exists(fullDestination) && !File.Exists(fullDestination + ".meta") &&
                    UnityEditor.AssetDatabase.LoadMainAssetAtPath(destination) == null,
                    "Recovery needs a new destination; existing files are never overwritten.", "already_exists");
                EnsureAssetFolder(destination);
                WhimTexDocumentRecovery.RecoverTo(source, destination);
                var recovered = Load(destination);
                try
                {
                    var result = Success();
                    result["sourcePath"] = sourcePath;
                    result["destinationPath"] = destination;
                    result["guid"] = UnityEditor.AssetDatabase.AssetPathToGUID(destination);
                    result["recovered"] = true;
                    result["document"] = Snapshot(recovered, destination);
                    return result;
                }
                finally { ReleaseTransientDocument(recovered); }
            });
        }

        private static string StagedPath(string path)
        {
            Require(!string.IsNullOrWhiteSpace(path), "sourcePath is required.");
            path = path.Replace('\\', '/');
            string relative;
            if (Path.IsPathRooted(path))
            {
                string full = Path.GetFullPath(path);
                relative = Path.GetRelativePath(ProjectRoot, full).Replace('\\', '/');
            }
            else relative = path;
            Require(relative.StartsWith("Assets/", StringComparison.Ordinal) &&
                relative.EndsWith(".whimtex-tmp", StringComparison.OrdinalIgnoreCase),
                "sourcePath must be an Assets/*.whimtex-tmp staging file.", "invalid_path");
            ValidateSegments(relative);
            string fullPath = FullPath(relative);
            RejectLinks(fullPath);
            return fullPath;
        }
    }
}
