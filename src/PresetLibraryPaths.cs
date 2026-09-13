using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DCFApixels.SpriteEditor
{
    internal static class PresetLibraryPaths
    {
        private static string[] projectPaths;
        private static readonly StringComparison PathComparison = Application.platform == RuntimePlatform.WindowsEditor
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        static PresetLibraryPaths() => EditorApplication.projectChanged += () => projectPaths = null;

        internal static bool IsInside(string path, string folder) => Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);

        internal static string PhysicalPath(string path)
        {
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = PackageInfo.FindForAssetPath(path);
                if (package != null) return Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
            }
            return Path.GetFullPath(path);
        }

        internal static string AssetPath(string path)
        {
            string full = Path.GetFullPath(path);
            return IsInside(full, Application.dataPath)
                ? "Assets/" + full.Substring(Path.GetFullPath(Application.dataPath).Length + 1).Replace('\\', '/') : null;
        }

        internal static IEnumerable<string> ProjectFiles(string extension)
        {
            projectPaths ??= AssetDatabase.GetAllAssetPaths();
            foreach (string path in projectPaths)
                if (path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) yield return path;
        }

        internal static IEnumerable<string> UserFiles(string folder, string extension)
        {
            if (!Directory.Exists(folder)) yield break;
            // Do not follow directory links into unrelated libraries or cycles.
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                string[] files, directories;
                try
                {
                    files = Directory.GetFiles(current);
                    directories = Directory.GetDirectories(current);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (string file in files)
                    if (file.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) yield return file;
                foreach (string directory in directories)
                {
                    bool follow;
                    try { follow = (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0; }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    if (follow) pending.Push(directory);
                }
            }
        }

        internal static string ValidateDestination(string path, string folder, string extension)
        {
            string full = Path.GetFullPath(path);
            if ((!IsInside(full, folder) && !IsInside(full, Application.dataPath)) ||
                !string.Equals(Path.GetExtension(full), "." + extension, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Save a ." + extension + " file inside " + folder + " or the project's Assets folder.");
            return full;
        }

        internal static void ImportSavedFile(string path)
        {
            projectPaths = null;
            string assetPath = AssetPath(path);
            if (assetPath != null) AssetDatabase.ImportAsset(assetPath);
        }
    }
}
