using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // No import hooks or frame callbacks. Explicit refresh changes discovery, never applied graphs.
    internal static class ShaderGraphFunctionCatalog
    {
        internal sealed class Entry
        {
            internal string menuPath, source, path;
            internal ShaderGraphFunction definition;
            string closureFingerprint;
            bool closureChecked;

            internal ShaderGraphFunction Import(ShaderGraph graph)
            {
                if (graph == null) throw new ArgumentNullException(nameof(graph));
                if (graph.functions == null || graph.sources == null) throw new FormatException("The graph function/source collections are missing.");
                var imported = JsonUtility.FromJson<ShaderGraphFunction>(JsonUtility.ToJson(definition));
                imported.id = Guid.NewGuid().ToString("N");
                imported.sourceId = "pending";
                imported.Validate();
                // Capture into a staging archive; a failed include or definition never changes the graph.
                var captured = new List<ShaderGraphSource>(graph.sources);
                imported.sourceId = ShaderGraphSources.Capture(source, path, captured);
                imported.sourceFingerprint = imported.sourceId; // Content key includes the complete include closure.
                imported.sourcePath = path;
                imported.Validate();
                graph.sources.AddRange(captured.GetRange(graph.sources.Count, captured.Count - graph.sources.Count));
                graph.functions.Add(imported);
                closureFingerprint = imported.sourceFingerprint;
                closureChecked = true;
                return imported;
            }

            internal bool MatchesBackup(string fingerprint)
            {
                if (string.IsNullOrEmpty(fingerprint)) return false;
                if (!closureChecked)
                {
                    closureChecked = true;
                    try
                    {
                        // Explicit link resolution only. Entries/search do not read include closures.
                        closureFingerprint = ShaderGraphSources.Capture(source, path, new List<ShaderGraphSource>());
                    }
                    catch (Exception error) when (IsSourceError(error)) { Report(path, error.Message); }
                }
                return string.Equals(closureFingerprint, fingerprint, StringComparison.Ordinal);
            }
        }

        static IReadOnlyList<Entry> entries = Array.Empty<Entry>();
        static readonly List<string> errors = new List<string>();
        static readonly IReadOnlyList<string> errorView = errors.AsReadOnly();
        static readonly Dictionary<string, IReadOnlyList<Entry>> fileChoices = new Dictionary<string, IReadOnlyList<Entry>>(StringComparer.Ordinal);
        static bool initialized;
        internal static IReadOnlyList<Entry> Entries { get { if (!initialized) Refresh(); return entries; } }
        internal static IReadOnlyList<string> Errors => errorView;

        internal static void Refresh()
        {
            errors.Clear();
            fileChoices.Clear();
            var next = new List<Entry>();
            var visited = new HashSet<string>(Application.platform == RuntimePlatform.WindowsEditor ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string path in AssetDatabase.GetAllAssetPaths())
                if (path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) ReadCatalogFile(path);
            foreach (string path in PresetLibraryPaths.UserFiles(WhimTexUserSettings.PresetsFolder, "hlsl")) ReadCatalogFile(path);
            next.Sort((a, b) =>
            {
                int order = StringComparer.OrdinalIgnoreCase.Compare(a.menuPath, b.menuPath);
                if (order == 0) order = StringComparer.Ordinal.Compare(a.path, b.path);
                if (order == 0) order = StringComparer.Ordinal.Compare(a.definition.Signature(), b.definition.Signature());
                return order;
            });
            Disambiguate(next);
            entries = next.AsReadOnly();
            initialized = true;

            void ReadCatalogFile(string path)
            {
                try
                {
                    string physical = PresetLibraryPaths.PhysicalPath(path);
                    if (!visited.Add(physical)) return;
                    next.AddRange(ReadFile(path, false));
                }
                catch (Exception error) when (IsSourceError(error)) { Report(path, error.Message); }
            }
        }

        internal static IReadOnlyList<Entry> LoadFile(string path)
        {
            try { var result = ReadFile(path, true); Disambiguate(result); return result.AsReadOnly(); }
            catch (Exception error) when (IsSourceError(error)) { Report(path, error.Message); throw; }
        }

        // Call after Refresh for an explicit link check. Never changes the saved function or source.
        internal static Entry Find(ShaderGraphFunction descriptor)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.sourceFingerprint)) return null;
            return FindCore(descriptor, true);
        }

        // Explicit refresh permits changed code, but never an identity, version or signature switch.
        internal static Entry FindForRefresh(ShaderGraphFunction descriptor) => descriptor == null ? null : FindCore(descriptor, false);

        static Entry FindCore(ShaderGraphFunction descriptor, bool exactBackup)
        {
            Entry match = null;
            int count = 0;
            Consider(Entries);
            if (count == 0 && !string.IsNullOrEmpty(descriptor.sourcePath))
            {
                if (!fileChoices.TryGetValue(descriptor.sourcePath, out var choices))
                {
                    try { choices = LoadFile(descriptor.sourcePath); }
                    catch (Exception error) when (IsSourceError(error)) { choices = Array.Empty<Entry>(); }
                    fileChoices[descriptor.sourcePath] = choices;
                }
                Consider(choices);
            }
            if (count > 1) { Report(descriptor.sourcePath, "The HLSL function link is ambiguous; the document backup remains active."); return null; }
            return count == 1 && (!exactBackup || match.MatchesBackup(descriptor.sourceFingerprint)) ? match : null;

            void Consider(IReadOnlyList<Entry> choices)
            {
                foreach (var entry in choices)
                {
                    bool identified = !string.IsNullOrEmpty(descriptor.presetId);
                    if (identified ? entry.definition.presetId != descriptor.presetId || entry.definition.presetVersion != descriptor.presetVersion
                        : !SamePath(entry.path, descriptor.sourcePath)) continue;
                    if (!ShaderGraphFunctionParser.SameVariant(entry.definition, descriptor)) continue;
                    match = entry;
                    count++;
                }
            }
        }

        static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(PresetLibraryPaths.PhysicalPath(a), PresetLibraryPaths.PhysicalPath(b),
                Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        static List<Entry> ReadFile(string path, bool includeUnmarked)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Choose an .hlsl function library.");
            string physical = PresetLibraryPaths.PhysicalPath(path);
            var file = new FileInfo(physical);
            if (file.Length > ShaderGraphSources.MaximumSourceBytes) throw new FormatException("An HLSL source exceeds 8 MiB.");
            string source = File.ReadAllText(physical, ShaderGraphSources.Utf8);
            bool marked = source.IndexOf("@whimtex-node", StringComparison.Ordinal) >= 0;
            var definitions = includeUnmarked || marked ? ShaderGraphFunctionParser.Parse(source, path, includeUnmarked) : new List<ShaderGraphFunction>();
            // Engine libraries are not automatically exposed as portable fragment functions.
            // The file picker may still attempt them, with ordinary capture diagnostics.
            if (includeUnmarked || !path.Replace('\\', '/').StartsWith("Packages/com.unity.", StringComparison.Ordinal))
                ShaderGraphFunctionReflection.Append(source, path, definitions, Report);
            var result = new List<Entry>(definitions.Count);
            foreach (var definition in definitions)
                result.Add(new Entry { menuPath = definition.menuPath, source = source, path = path, definition = definition });
            return result;
        }

        static void Disambiguate(List<Entry> choices)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in choices) { counts.TryGetValue(entry.menuPath, out int count); counts[entry.menuPath] = count + 1; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in choices)
            {
                if (counts[entry.menuPath] > 1)
                    entry.menuPath += " (" + entry.definition.Signature() + ") [" + entry.path.Replace('/', '\\') + "]";
                string candidate = entry.menuPath;
                int suffix = 2;
                while (!names.Add(entry.menuPath)) entry.menuPath = candidate + " #" + suffix++;
            }
        }

        internal static void Report(string path, string message)
        {
            string diagnostic = (path ?? "HLSL") + ": " + message;
            if (!errors.Contains(diagnostic)) errors.Add(diagnostic);
        }
        static bool IsSourceError(Exception error) => error is IOException || error is UnauthorizedAccessException ||
            error is FormatException || error is ArgumentException || error is NotSupportedException;
    }
}
