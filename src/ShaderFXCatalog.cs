using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXCatalog
    {
        [Serializable]
        internal sealed class Entry
        {
            [SerializeField] internal string guid, path, menuPath, error;
            [SerializeField] internal bool user, assetPreset;
            internal bool HasError => !string.IsNullOrEmpty(error);
        }

        [Serializable]
        private sealed class Snapshot { public List<Entry> entries = new List<Entry>(); }
        // SessionState survives domain reloads but is discarded when the Editor closes.
        private const string SessionKey = "WhimTex.ShaderFXCatalog.Headers.v1";

        internal static string Folder => Path.GetFullPath(Path.Combine(WhimTexUserSettings.PresetsFolder, "ShaderFX"));

        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static bool initialized;
        private static bool queued;
        private static readonly HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (long length, long modified)> userFiles = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<ShaderFX>> shaderDependents =
            new Dictionary<string, HashSet<ShaderFX>>(StringComparer.OrdinalIgnoreCase);

        static ShaderFXCatalog()
        {
            string saved = SessionState.GetString(SessionKey, "");
            if (saved.Length == 0) return;
            try
            {
                var snapshot = JsonUtility.FromJson<Snapshot>(saved);
                if (snapshot?.entries == null) return;
                foreach (var entry in snapshot.entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.path)) entries[entry.path] = entry;
                initialized = true;
            }
            catch (ArgumentException) { entries.Clear(); }
        }

        private static void SaveHeaders()
        {
            var snapshot = new Snapshot();
            foreach (var entry in entries.Values) if (!entry.user) snapshot.entries.Add(entry);
            SessionState.SetString(SessionKey, JsonUtility.ToJson(snapshot));
        }

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
                    foreach (string segment in menuPath.Split('/'))
                        if (string.IsNullOrWhiteSpace(segment)) entry.error = "Effect category/name must not contain empty segments.";
                    entries[path] = entry;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            else if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                // Do not instantiate assets here: ShaderFX.OnEnable can restore GPU shaders.
                if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(ShaderFX))
                    entries[path] = new Entry { path = path, guid = AssetDatabase.AssetPathToGUID(path),
                        menuPath = "Shader FX/" + Path.GetFileNameWithoutExtension(path), assetPreset = true };
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
                SaveHeaders();
            }
            // User files are outside AssetDatabase. Refresh on opening the menu, not every repaint.
            var remaining = new HashSet<string>(userFiles.Keys, StringComparer.Ordinal);
            foreach (string path in PresetLibraryPaths.UserFiles(Folder, "hlsl"))
            {
                if (PresetLibraryPaths.AssetPath(path) != null) continue;
                remaining.Remove(path);
                try
                {
                    var file = new FileInfo(path);
                    var stamp = (file.Length, file.LastWriteTimeUtc.Ticks);
                    if (userFiles.TryGetValue(path, out var previous) && previous == stamp) continue;
                    Inspect(path, true);
                    userFiles[path] = stamp;
                }
                catch (IOException) { entries.Remove(path); userFiles.Remove(path); }
                catch (UnauthorizedAccessException) { entries.Remove(path); userFiles.Remove(path); }
            }
            foreach (string path in remaining) { entries.Remove(path); userFiles.Remove(path); }
            var list = new List<Entry>(entries.Values);
            list.Sort((a, b) => string.Compare(a.menuPath, b.menuPath, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        internal static Entry InspectDroppedHlsl(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) return null;
            Inspect(path);
            return entries.TryGetValue(path, out var entry) ? entry : null;
        }

        internal static void ShowMenu(Action<Entry> select)
        {
            var menu = new GenericMenu();
            var list = GetEntries();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                string label = (entry.user ? "User/" : "") + entry.menuPath;
                labels.TryGetValue(label, out int count);
                labels[label] = count + 1;
            }
            if (list.Count == 0) menu.AddDisabledItem(new GUIContent("No effects found — add a marked .hlsl file"));
            foreach (var entry in list)
            {
                string label = (entry.user ? "User/" : "") + entry.menuPath;
                if (labels[label] > 1) label += " (" + entry.path.Replace('\\', '›').Replace('/', '›') + ")";
                var content = new GUIContent(label, entry.HasError ? entry.error : entry.path);
                if (entry.HasError) menu.AddDisabledItem(content);
                else menu.AddItem(content, false, () => select(entry));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open User Shader FX Folder"), false, () =>
            {
                try { Directory.CreateDirectory(Folder); EditorUtility.RevealInFinder(Folder); }
                catch (Exception error) { EditorUtility.DisplayDialog("Shader FX Presets", error.Message, "OK"); }
            });
            menu.AddItem(new GUIContent("Library Settings…"), false, WhimTexUserSettingsWindow.Open);
            menu.ShowAsContext();
        }

        internal static void AssetsChanged(params string[][] batches)
        {
            foreach (var batch in batches)
            {
                foreach (string path in batch)
                {
                    if (!string.IsNullOrEmpty(path) && (IsHlsl(path) || IsInclude(path) || IsAsset(path)))
                        changed.Add(path);
                }
            }
            if (changed.Count == 0 || queued) return;
            queued = true;
            EditorApplication.delayCall += FlushChanges;
        }

        private static void FlushChanges()
        {
            queued = false;
            bool catalogChanged = false;
            bool shaderSourceChanged = false;
            foreach (string path in changed)
            {
                if (initialized && (IsHlsl(path) || IsAsset(path)))
                {
                    entries.TryGetValue(path, out Entry previous);
                    Inspect(path);
                    entries.TryGetValue(path, out Entry current);
                    catalogChanged |= !SameEntry(previous, current);
                }
                shaderSourceChanged |= IsHlsl(path) || IsInclude(path);
            }
            if (shaderSourceChanged) ReloadShaderDependents(changed);
            changed.Clear();
            if (initialized && catalogChanged) SaveHeaders();
        }

        internal static void SetShaderDependencies(ShaderFX effect, HashSet<string> dependencies)
        {
            if (effect == null || dependencies == null) return;
            foreach (string path in dependencies)
            {
                string key = NormalizePath(path);
                if (!shaderDependents.TryGetValue(key, out HashSet<ShaderFX> dependents))
                    shaderDependents.Add(key, dependents = new HashSet<ShaderFX>());
                dependents.Add(effect);
            }
        }

        internal static void RemoveShaderDependencies(ShaderFX effect, HashSet<string> dependencies)
        {
            if (effect == null || dependencies == null) return;
            foreach (string path in dependencies)
            {
                string key = NormalizePath(path);
                if (!shaderDependents.TryGetValue(key, out HashSet<ShaderFX> dependents)) continue;
                dependents.Remove(effect);
                if (dependents.Count == 0) shaderDependents.Remove(key);
            }
        }

        private static void ReloadShaderDependents(HashSet<string> paths)
        {
            var affected = new HashSet<ShaderFX>();
            foreach (string path in paths)
                if (shaderDependents.TryGetValue(NormalizePath(path), out HashSet<ShaderFX> dependents))
                    affected.UnionWith(dependents);
            foreach (ShaderFX effect in affected)
                if (effect != null && effect.IsCatalogLinked) effect.ReloadCatalogSource(true);
        }

        private static bool SameEntry(Entry a, Entry b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return string.Equals(a.guid, b.guid, StringComparison.Ordinal) &&
                string.Equals(a.path, b.path, StringComparison.Ordinal) &&
                string.Equals(a.menuPath, b.menuPath, StringComparison.Ordinal) &&
                string.Equals(a.error, b.error, StringComparison.Ordinal) &&
                a.user == b.user && a.assetPreset == b.assetPreset;
        }

        private static bool IsHlsl(string path) => path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase);
        private static bool IsInclude(string path) => path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase);
        private static bool IsAsset(string path) => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        private static string NormalizePath(string path) => path.Replace('\\', '/');
    }

    internal sealed class ShaderFXCatalogPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
            ShaderFXCatalog.AssetsChanged(imported, deleted, moved, movedFrom);
    }
}
