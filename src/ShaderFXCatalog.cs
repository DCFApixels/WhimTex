using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    internal static class ShaderFXCatalog
    {
        internal sealed class Entry
        {
            internal string guid, path, menuPath, source, error, hash;
            internal ShaderFX asset;
            internal bool user;
        }

        internal static string Folder => Path.GetFullPath(Path.Combine(SpriteEditorUserSettings.PresetsFolder, "ShaderFX"));

        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static bool initialized;
        private static bool queued;
        private static readonly HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);

        internal static string ReadSource(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new IOException("The HLSL effect source is missing. The last applied shader is retained.");
            string physical = path;
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                PackageInfo package = PackageInfo.FindForAssetPath(path);
                if (package != null) physical = Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
            }
            if (new FileInfo(physical).Length > 2 * 1024 * 1024) throw new IOException("HLSL effect exceeds 2 MiB.");
            return File.ReadAllText(physical);
        }

        private static void Inspect(string path, bool user = false)
        {
            entries.Remove(path);
            if (path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
            {
                string physical = path;
                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    PackageInfo package = PackageInfo.FindForAssetPath(path);
                    if (package != null) physical = Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
                }
                try
                {
                    using var reader = new StreamReader(physical);
                    // Discovery reads only the first physical line of unrelated HLSL files.
                    if (!ShaderFXMetadata.TryHeader(reader.ReadLine(), out string menuPath)) return;
                    var entry = new Entry { path = path, guid = user ? null : AssetDatabase.AssetPathToGUID(path), menuPath = menuPath, user = user };
                    try
                    {
                        entry.source = ReadSource(path);
                        ShaderFXMetadata.Parse(entry.source, true, out _);
                        entry.hash = user ? Hash128.Compute(entry.source).ToString() : AssetDatabase.GetAssetDependencyHash(path).ToString();
                    }
                    catch (Exception error) { entry.error = error.Message; }
                    entries[path] = entry;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            else if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path) as ShaderFX;
                if (asset != null && asset.EmbeddedOwner == null)
                    entries[path] = new Entry { path = path, guid = AssetDatabase.AssetPathToGUID(path), menuPath = "Shader FX/" + asset.name, asset = asset };
            }
        }

        internal static IReadOnlyList<Entry> GetEntries()
        {
            if (!initialized)
            {
                initialized = true;
                foreach (string path in AssetDatabase.GetAllAssetPaths())
                    if (path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) Inspect(path);
                foreach (string guid in AssetDatabase.FindAssets("t:ShaderFX")) Inspect(AssetDatabase.GUIDToAssetPath(guid));
            }
            // User files are outside AssetDatabase. Refresh on opening the menu, not every repaint.
            var stale = new List<string>();
            foreach (var pair in entries) if (pair.Value.user) stale.Add(pair.Key);
            foreach (string path in stale) entries.Remove(path);
            foreach (string path in PresetLibraryPaths.UserFiles(Folder, "hlsl"))
                if (PresetLibraryPaths.AssetPath(path) == null) Inspect(path, true);
            var list = new List<Entry>(entries.Values);
            list.Sort((a, b) => string.Compare(a.menuPath, b.menuPath, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        internal static void ShowMenu(Action<Entry> select)
        {
            var menu = new GenericMenu();
            var list = GetEntries();
            if (list.Count == 0) menu.AddDisabledItem(new GUIContent("No effects found — add a marked .hlsl file"));
            foreach (var entry in list)
            {
                bool duplicate = false;
                string label = (entry.user ? "User/" : "") + entry.menuPath;
                foreach (var other in list) if (other != entry && (other.user ? "User/" : "") + other.menuPath == label) { duplicate = true; break; }
                if (duplicate) label += " (" + entry.path.Replace('\\', '›').Replace('/', '›') + ")";
                var content = new GUIContent(label, entry.error ?? entry.path);
                if (entry.error != null) menu.AddDisabledItem(content);
                else menu.AddItem(content, false, () => select(entry));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open User Shader FX Folder"), false, () =>
            {
                try { Directory.CreateDirectory(Folder); EditorUtility.RevealInFinder(Folder); }
                catch (Exception error) { EditorUtility.DisplayDialog("Shader FX Presets", error.Message, "OK"); }
            });
            menu.AddItem(new GUIContent("Library Settings…"), false, SpriteEditorUserSettingsWindow.Open);
            menu.ShowAsContext();
        }

        internal static void AssetsChanged(params string[][] batches)
        {
            foreach (var batch in batches) foreach (string path in batch) changed.Add(path);
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += FlushChanges;
        }

        private static void FlushChanges()
        {
            queued = false;
            bool shaderChanged = false;
            foreach (string path in changed)
            {
                if (initialized) Inspect(path);
                shaderChanged |= path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase);
            }
            if (shaderChanged)
                foreach (var fx in Resources.FindObjectsOfTypeAll<ShaderFX>())
                    if (fx != null && fx.IsCatalogLinked) fx.OnCatalogFilesChanged(changed);
            changed.Clear();
        }
    }

    internal sealed class ShaderFXCatalogPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
            ShaderFXCatalog.AssetsChanged(imported, deleted, moved, movedFrom);
    }
}
