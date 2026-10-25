using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class ShaderFXCatalog
    {
        [Serializable]
        internal sealed class Entry
        {
            [SerializeField] internal string guid, path, menuPath, group, error;
            [SerializeField] internal bool user;
            [SerializeField] internal ShaderFXIconMetadata.Descriptor icon;
            internal bool HasError => !string.IsNullOrEmpty(error);
        }

        [Serializable]
        private sealed class Snapshot { public List<Entry> entries = new List<Entry>(); }
        // SessionState survives domain reloads but is discarded when the Editor closes.
        private const string SessionKey = "WhimTex.ShaderFXCatalog.Headers.v4";

        internal static string Folder => Path.GetFullPath(Path.Combine(WhimTexUserSettings.PresetsFolder, "ShaderFX"));

        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static bool initialized;
        private static bool queued;
        private static readonly HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (long length, long modified)> userFiles = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<ShaderFX>> shaderDependents =
            new Dictionary<string, HashSet<ShaderFX>>(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<Entry> sortedEntries = Array.Empty<Entry>();
        private static Task<HeaderScan> pendingScan;
        private static bool refreshProject = true;
        private static int revision, pendingRevision;
        private static string userFolder;

        private sealed class HeaderScan
        {
            internal readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            internal readonly Dictionary<string, (long length, long modified)> userFiles = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
            internal string folder;
            internal bool project;
        }

        static ShaderFXCatalog()
        {
            userFolder = Folder;
            WhimTexUserSettings.Changed += UserSettingsChanged;
            EditorApplication.focusChanged += focused => { if (focused) QueueRefresh(); };
            QueueRefresh();
            string saved = SessionState.GetString(SessionKey, "");
            if (saved.Length == 0) return;
            try
            {
                var snapshot = JsonUtility.FromJson<Snapshot>(saved);
                if (snapshot?.entries == null) return;
                foreach (var entry in snapshot.entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.path)) entries[entry.path] = entry;
                initialized = true;
                SortEntries();
            }
            catch (ArgumentException) { entries.Clear(); }
        }

        private static void SortEntries()
        {
            var list = new List<Entry>(entries.Values);
            list.Sort((a, b) => string.Compare(a.menuPath, b.menuPath, StringComparison.OrdinalIgnoreCase));
            sortedEntries = list;
            ShaderFXIconCache.Warm(sortedEntries);
        }

        private static void UserSettingsChanged()
        {
            if (string.Equals(userFolder, Folder, StringComparison.Ordinal)) return;
            userFolder = Folder;
            foreach (var entry in sortedEntries) if (entry.user) entries.Remove(entry.path);
            userFiles.Clear();
            SortEntries();
            UserPresetsChanged();
        }

        internal static void UserPresetsChanged()
        {
            revision++;
            QueueRefresh();
        }

        private static void QueueRefresh()
        {
            EditorApplication.delayCall -= BeginRefresh;
            EditorApplication.delayCall += BeginRefresh;
        }

        // Unity APIs run here, outside the menu click. Only file enumeration/header reads run on the worker.
        private static Func<HeaderScan> PrepareScan(string folder)
        {
            var scan = new HeaderScan { folder = folder, project = refreshProject || !initialized };
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            if (scan.project)
            {
                var packages = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string path in AssetDatabase.GetAllAssetPaths())
                {
                    if (!IsHlsl(path)) continue;
                    string physical = path;
                    if (path.StartsWith("Packages/", StringComparison.Ordinal))
                    {
                        int slash = path.IndexOf('/', "Packages/".Length);
                        string root = path.Substring(0, slash);
                        if (!packages.TryGetValue(root, out string resolved))
                        {
                            resolved = PackageInfo.FindForAssetPath(path)?.resolvedPath ?? root;
                            packages.Add(root, resolved);
                        }
                        physical = Path.Combine(resolved, path.Substring(slash + 1));
                    }
                    sources.Add(path, physical);
                }
            }
            else foreach (var entry in entries.Values) if (!entry.user) scan.entries.Add(entry.path, entry);
            var previousEntries = new Dictionary<string, Entry>(entries, StringComparer.Ordinal);
            var previousFiles = new Dictionary<string, (long length, long modified)>(userFiles, StringComparer.Ordinal);
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            // Initialize the shared path helper on the main thread before its file-only iterator runs.
            PresetLibraryPaths.IsInside(scan.folder, Application.dataPath);
            return () =>
            {
                foreach (var source in sources)
                {
                    var entry = ReadHeader(source.Key, source.Value, false);
                    if (entry != null) scan.entries.Add(entry.path, entry);
                }
                foreach (string path in PresetLibraryPaths.UserFiles(scan.folder, "hlsl"))
                {
                    if (Path.GetFullPath(path).StartsWith(assets, comparison)) continue;
                    try
                    {
                        var file = new FileInfo(path);
                        var stamp = (file.Length, file.LastWriteTimeUtc.Ticks);
                        Entry entry;
                        if (previousFiles.TryGetValue(path, out var previous) && previous == stamp)
                            previousEntries.TryGetValue(path, out entry);
                        else entry = ReadHeader(path, path, true);
                        if (entry != null) scan.entries[path] = entry;
                        scan.userFiles[path] = stamp;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return scan;
            };
        }

        private static void BeginRefresh()
        {
            if (pendingScan != null) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { QueueRefresh(); return; }
            UserSettingsChanged();
            var read = PrepareScan(Folder);
            pendingRevision = revision;
            pendingScan = Task.Run(read);
            EditorApplication.update += FinishRefresh;
        }

        private static void FinishRefresh()
        {
            if (pendingScan == null || !pendingScan.IsCompleted) return;
            var task = pendingScan;
            pendingScan = null;
            EditorApplication.update -= FinishRefresh;
            try
            {
                var scan = task.GetAwaiter().GetResult();
                if (pendingRevision == revision && scan.folder == Folder) Publish(scan);
                else QueueRefresh();
            }
            catch (Exception error) { Debug.LogWarning("WhimTex FX preset discovery: " + error.Message); }
        }

        private static void Publish(HeaderScan scan)
        {
            foreach (var entry in scan.entries.Values)
            {
                if (!entry.user && string.IsNullOrEmpty(entry.guid)) entry.guid = AssetDatabase.AssetPathToGUID(entry.path);
            }
            var previous = new Dictionary<string, Entry>(entries, StringComparer.Ordinal);
            entries.Clear();
            foreach (var pair in scan.entries)
                entries.Add(pair.Key, previous.TryGetValue(pair.Key, out var old) && SameEntry(old, pair.Value) ? old : pair.Value);
            userFiles.Clear();
            foreach (var pair in scan.userFiles) userFiles.Add(pair.Key, pair.Value);
            initialized = true;
            refreshProject = false;
            userFolder = scan.folder;
            SortEntries();
            if (scan.project) SaveHeaders();
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

        private static Entry ReadHeader(string path, string physical, bool user)
        {
            try
            {
                using var reader = new StreamReader(physical);
                string header = ShaderFXIconMetadata.ReadHeader(reader);
                using var firstLine = new StringReader(header);
                if (!ShaderFXMetadata.TryHeader(firstLine.ReadLine(), out string menuPath)) return null;
                var entry = new Entry { path = path, menuPath = menuPath, user = user, icon = ShaderFXIconMetadata.Read(header) };
                entry.group = ShaderFXMetadata.ReadPresetGroup(header, out entry.error);
                foreach (string segment in menuPath.Split('/'))
                    if (string.IsNullOrWhiteSpace(segment)) entry.error = "Effect category/name must not contain empty segments.";
                return entry;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static void Inspect(string path, bool user = false)
        {
            entries.Remove(path);
            if (path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
            {
                var entry = ReadHeader(path, PresetLibraryPaths.PhysicalPath(path), user);
                if (entry == null) return;
                entry.guid = user ? null : AssetDatabase.AssetPathToGUID(path);
                entries[path] = entry;
            }
        }

        internal static IReadOnlyList<Entry> GetEntries()
        {
            // Explicit API discovery is fresh and synchronous. UI uses GetMenuEntries instead.
            UserSettingsChanged();
            FinishRefresh();
            revision++; // An older background snapshot must not replace this newer one.
            var read = PrepareScan(Folder);
            Publish(read());
            return sortedEntries;
        }

        internal static IReadOnlyList<Entry> GetMenuEntries()
        {
            UserSettingsChanged();
            QueueRefresh();
            return sortedEntries;
        }

        internal static Entry InspectDroppedHlsl(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) return null;
            Inspect(path);
            revision++;
            SortEntries();
            return entries.TryGetValue(path, out var entry) ? entry : null;
        }

        internal static void ShowMenu(Action<Entry> select) => BuildMenu(select).ShowAsContext();

        internal static string EmptyMenuMessage => initialized
            ? "No effects found — add a marked .hlsl file" : "Loading effect presets…";

        internal static void OpenUserFolder()
        {
            try { Directory.CreateDirectory(Folder); EditorUtility.RevealInFinder(Folder); }
            catch (Exception error) { EditorUtility.DisplayDialog("Shader FX Presets", error.Message, "OK"); }
        }

        private static GenericMenu BuildMenu(Action<Entry> select)
        {
            var menu = new GenericMenu();
            var list = GetMenuEntries();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                string label = (entry.user ? "User/" : "") + entry.menuPath;
                labels.TryGetValue(label, out int count);
                labels[label] = count + 1;
            }
            if (list.Count == 0) menu.AddDisabledItem(new GUIContent(EmptyMenuMessage));
            foreach (var entry in list)
            {
                string label = (entry.user ? "User/" : "") + entry.menuPath;
                if (labels[label] > 1) label += " (" + entry.path.Replace('\\', '›').Replace('/', '›') + ")";
                var content = new GUIContent(label, entry.HasError ? entry.error : entry.path);
                if (entry.HasError) menu.AddDisabledItem(content);
                else menu.AddItem(content, false, () => select(entry));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open User Shader FX Folder"), false, OpenUserFolder);
            menu.AddItem(new GUIContent("Library Settings…"), false, WhimTexUserSettingsWindow.Open);
            return menu;
        }

        internal static void AssetsChanged(params string[][] batches)
        {
            foreach (var batch in batches)
            {
                foreach (string path in batch)
                {
                    if (!string.IsNullOrEmpty(path) && (IsHlsl(path) || IsInclude(path)))
                        changed.Add(path);
                }
            }
            if (changed.Count == 0) return;
            revision++; // Reject in-flight headers as soon as an import is reported.
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += FlushChanges;
        }

        internal static void DomainReloaded()
        {
            // Keep restored headers usable while imports from the reload are reconciled in the background.
            revision++;
            refreshProject = true;
            QueueRefresh();
        }

        private static void FlushChanges()
        {
            queued = false;
            if (changed.Count == 0) return;
            bool catalogChanged = false;
            bool shaderSourceChanged = false;
            foreach (string path in changed)
            {
                if (initialized && IsHlsl(path))
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
            if (initialized && catalogChanged) { SortEntries(); SaveHeaders(); }
            if (pendingScan != null || refreshProject) QueueRefresh();
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
                string.Equals(a.group, b.group, StringComparison.Ordinal) &&
                string.Equals(a.error, b.error, StringComparison.Ordinal) &&
                string.Equals(a.icon?.reference, b.icon?.reference, StringComparison.Ordinal) &&
                string.Equals(a.icon?.svg, b.icon?.svg, StringComparison.Ordinal) &&
                string.Equals(a.icon?.error, b.icon?.error, StringComparison.Ordinal) &&
                a.user == b.user;
        }

        private static bool IsHlsl(string path) => path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase);
        private static bool IsInclude(string path) => path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase);
        private static string NormalizePath(string path) => path.Replace('\\', '/');
    }

    internal sealed class ShaderFXCatalogPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            if (didDomainReload) ShaderFXCatalog.DomainReloaded();
            ShaderFXCatalog.AssetsChanged(imported, deleted, moved, movedFrom);
        }
    }
}
