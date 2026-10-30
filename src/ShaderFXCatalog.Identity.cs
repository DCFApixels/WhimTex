using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderFXCatalog
    {
        [Serializable] private sealed class Owner { public string id, path; }
        [Serializable] private sealed class OwnerIndex { public List<Owner> owners = new List<Owner>(); }
        private static Dictionary<string, string> owners;
        private static readonly HashSet<ShaderFX> linkedEffects = new HashSet<ShaderFX>();
        private static readonly Dictionary<string, Entry> identifiedEntries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly HashSet<string> ambiguousIds = new HashSet<string>(StringComparer.Ordinal);
        private const string OwnerIndexPath = "Library/WhimTex/FXPresetOwners.json";

        private static void LoadOwners()
        {
            if (owners != null) return;
            owners = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(OwnerIndexPath)) return;
                var index = JsonUtility.FromJson<OwnerIndex>(File.ReadAllText(OwnerIndexPath));
                if (index?.owners != null)
                    foreach (var owner in index.owners)
                        if (!string.IsNullOrEmpty(owner.id) && !string.IsNullOrEmpty(owner.path)) owners[owner.id] = owner.path;
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
            { Debug.LogWarning("WhimTex preset ownership cache: " + error.Message); }
        }

        private static void SaveOwners()
        {
            var index = new OwnerIndex();
            foreach (var pair in owners) index.owners.Add(new Owner { id = pair.Key, path = pair.Value });
            index.owners.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OwnerIndexPath));
                string json = JsonUtility.ToJson(index);
                if (File.Exists(OwnerIndexPath))
                    ShaderFXPresetIdentity.ReplaceFile(OwnerIndexPath, File.ReadAllText(OwnerIndexPath), json);
                else File.WriteAllText(OwnerIndexPath, json);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { Debug.LogWarning("WhimTex could not remember preset ownership: " + error.Message); }
        }

        internal static void AssetsMoved(string[] moved, string[] from)
        {
            LoadOwners();
            bool dirty = false;
            for (int i = 0; i < moved.Length && i < from.Length; i++)
            {
                if (!IsHlsl(moved[i])) continue;
                var ids = new List<string>();
                foreach (var pair in owners) if (pair.Value == from[i]) ids.Add(pair.Key);
                foreach (string id in ids) { owners[id] = moved[i]; dirty = true; }
            }
            if (dirty) SaveOwners();
        }

        private static long CreationTime(Entry entry)
        {
            try
            {
                long value = File.GetCreationTimeUtc(PresetLibraryPaths.PhysicalPath(entry.path)).Ticks;
                return value > DateTime.FromFileTimeUtc(0).Ticks ? value : long.MaxValue;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return long.MaxValue; }
        }

        private static void ResolveIdentities(Dictionary<string, Entry> source)
        {
            LoadOwners();
            var groups = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            foreach (var cached in new List<Entry>(source.Values))
            {
                var entry = cached;
                if (entry.error?.StartsWith("Duplicate preset ID ", StringComparison.Ordinal) == true)
                {
                    entry = ReadHeader(entry.path, PresetLibraryPaths.PhysicalPath(entry.path), entry.user);
                    if (entry == null) { source.Remove(cached.path); continue; }
                    source[entry.path] = entry;
                }
                if (string.IsNullOrEmpty(entry.id)) continue;
                if (!groups.TryGetValue(entry.id, out var group)) groups.Add(entry.id, group = new List<Entry>());
                group.Add(entry);
            }
            bool dirty = false;
            identifiedEntries.Clear();
            ambiguousIds.Clear();
            foreach (var pair in groups)
            {
                var group = pair.Value;
                owners.TryGetValue(pair.Key, out string previous);
                var owner = group.Find(e => e.path == previous);
                if (owner == null)
                {
                    group.Sort((a, b) =>
                    {
                        int comparison = CreationTime(a).CompareTo(CreationTime(b));
                        return comparison != 0 ? comparison : string.CompareOrdinal(NormalizePath(a.path), NormalizePath(b.path));
                    });
                    owner = group[0];
                }
                if (previous != owner.path) { owners[pair.Key] = owner.path; dirty = true; }
                foreach (var duplicate in group)
                {
                    if (duplicate == owner) continue;
                    try
                    {
                        string physical = PresetLibraryPaths.PhysicalPath(duplicate.path);
                        if (PresetLibraryPaths.IsInside(physical, "Library/PackageCache"))
                            throw new IOException("Package cache presets cannot be rewritten.");
                        string original = ReadSource(duplicate.path);
                        var current = ShaderFXPresetIdentity.Read(original);
                        if (current.id != pair.Key) throw new IOException("The preset changed during discovery.");
                        var replacement = ShaderFXPresetIdentity.Create();
                        ShaderFXPresetIdentity.ReplaceFile(physical, original, ShaderFXPresetIdentity.Write(original, replacement));
                        var updated = ReadHeader(duplicate.path, physical, duplicate.user);
                        source[duplicate.path] = updated;
                        owners[replacement.id] = duplicate.path;
                        identifiedEntries[replacement.id] = updated;
                        dirty = true;
                        if (!duplicate.user) AssetDatabase.ImportAsset(duplicate.path);
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException)
                    {
                        duplicate.error = "Duplicate preset ID " + pair.Key + ": " + error.Message;
                        ambiguousIds.Add(pair.Key);
                    }
                }
                if (ambiguousIds.Contains(pair.Key)) owner.error = "Duplicate preset ID " + pair.Key + "; resolve the conflicting files before linking.";
                else identifiedEntries[pair.Key] = owner;
            }
            if (dirty) SaveOwners();
        }

        internal static Entry FindPreset(string id)
        {
            if (!initialized || refreshProject) GetEntries();
            if (ambiguousIds.Contains(id)) return null;
            return identifiedEntries.TryGetValue(id, out var entry) && !entry.HasError ? entry : null;
        }

        internal static void TrackLinked(ShaderFX effect) { if (effect != null && effect.IsCatalogLinked) linkedEffects.Add(effect); }
        internal static void UntrackLinked(ShaderFX effect) => linkedEffects.Remove(effect);
        private static void QueueLinkedRefresh()
        {
            EditorApplication.delayCall -= RefreshLinked;
            EditorApplication.delayCall += RefreshLinked;
        }
        private static void RefreshLinked()
        {
            // A source may reappear under another path, including in the external user library.
            var effects = new List<ShaderFX>(linkedEffects);
            foreach (var effect in effects)
                if (effect == null || !effect.IsCatalogLinked) linkedEffects.Remove(effect);
                else effect.ReloadCatalogSource();
        }
    }
}
