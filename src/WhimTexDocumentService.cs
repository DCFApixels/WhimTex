using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Storage identity belongs to the document, not its editor window. The binding survives
    // Unity reload/Undo but is not part of the portable file model.
    internal static class WhimTexDocumentService
    {
        private static readonly Dictionary<UnityEngine.Object, TextureCompositor> Views = new();
        private static readonly HashSet<string> Writers = new(StringComparer.OrdinalIgnoreCase);

        internal static string PathOf(TextureCompositor document)
        {
            var binding = document == null ? null : document.documentBinding;
            if (binding == null) return null;
            string path = string.IsNullOrEmpty(binding.guid) ? binding.path : AssetDatabase.GUIDToAssetPath(binding.guid);
            if (string.IsNullOrEmpty(path)) return binding.path;
            binding.path = path;
            return path;
        }

        internal static void Bind(TextureCompositor document, string path)
        {
            var info = new FileInfo(path);
            if (document.documentBinding == null || document.documentBinding.owner != document)
            {
                document.documentBinding = ScriptableObject.CreateInstance<WhimTexDocumentBinding>();
                document.documentBinding.hideFlags = HideFlags.HideAndDontSave;
                document.documentBinding.owner = document;
            }
            var binding = document.documentBinding;
            binding.path = path;
            binding.guid = AssetDatabase.AssetPathToGUID(path);
            binding.length = info.Exists ? info.Length : 0;
            binding.writeTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
            binding.dirty = false;
        }

        internal static void Attach(UnityEngine.Object view, TextureCompositor document) => Views[view] = document;
        internal static void Detach(UnityEngine.Object view) => Views.Remove(view);

        internal static TextureCompositor FindDisplayed(string path)
        {
            foreach (var pair in Views)
                if (pair.Key != null && pair.Value != null && string.Equals(PathOf(pair.Value), path, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            return null;
        }

        internal static bool IsDocumentAsset(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && (WhimTexDocumentFile.IsDocument(path) || TextureCompositor.FindDocument(asset) != null);
        }

        internal static bool IsOwnOutput(TextureCompositor document, UnityEngine.Object asset) => document != null && asset != null &&
            (asset == document.OutputTexture || TextureCompositor.FindDocument(asset) == document || !string.IsNullOrEmpty(PathOf(document)) &&
                string.Equals(PathOf(document), AssetDatabase.GetAssetPath(asset), StringComparison.OrdinalIgnoreCase));

        internal static IDisposable BeginWrite(TextureCompositor document, string path)
        {
            path = NormalizeDestination(path);
            var displayed = FindDisplayed(path);
            if (displayed != null && displayed != document)
                throw new WhimTexDocumentException("This file is already open in another WhimTex document.");
            var binding = document.documentBinding;
            if (binding != null && string.Equals(PathOf(document), path, StringComparison.OrdinalIgnoreCase))
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != binding.length || info.LastWriteTimeUtc.Ticks != binding.writeTicks ||
                    !string.IsNullOrEmpty(binding.guid) && AssetDatabase.AssetPathToGUID(path) != binding.guid)
                    throw new WhimTexDocumentException("The document file changed outside this session. Reopen it or use Save As; the external version was not overwritten.");
            }
            if (!Writers.Add(path)) throw new WhimTexDocumentException("The document is already being saved.");
            return new WriteLease(path);
        }

        internal static string NormalizeDestination(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new WhimTexDocumentException("The document path is empty.");
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase))
                throw new WhimTexDocumentException("Save documents inside Assets, outside StreamingAssets.");
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new WhimTexDocumentException("Invalid document path.");
            if (!path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
                path = System.IO.Path.ChangeExtension(path, "tiff");
            string root = System.IO.Path.GetFullPath(Application.dataPath);
            for (string current = System.IO.Path.GetFullPath(path); !string.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = System.IO.Path.GetDirectoryName(current))
            {
                if (current == null || !current.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new WhimTexDocumentException("Document path escapes Assets.");
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new WhimTexDocumentException("Saving through symbolic links or junctions is not supported.");
            }
            return path;
        }

        private sealed class WriteLease : IDisposable
        {
            private readonly string path;
            internal WriteLease(string path) => this.path = path;
            public void Dispose() => Writers.Remove(path);
        }
    }
}
