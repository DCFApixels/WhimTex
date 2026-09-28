using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Import callbacks only enqueue. Rendering/writing happens on the main thread after import.
    internal static class WhimTexOutputEncoding
    {
        private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Updating = new(StringComparer.OrdinalIgnoreCase);

        internal static void AfterImport(string[] paths)
        {
            foreach (string path in paths)
            {
                if (Updating.Contains(path) ||
                    !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !(path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))) continue;
                if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                    WhimTexTiffCarrier.TryReadCarrierFlags(path, out bool srgb, out bool document, out _) &&
                    document && srgb != importer.sRGBTexture) Pending.Add(path);
            }
            if (Pending.Count == 0) return;
            EditorApplication.update -= ProcessPending;
            EditorApplication.update += ProcessPending;
        }

        private static void ProcessPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var paths = new List<string>(Pending);
            Pending.Clear();
            EditorApplication.update -= ProcessPending;
            foreach (string path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                try { Change(path, importer.sRGBTexture); }
                catch (OperationCanceledException) { }
                catch (Exception error) { Debug.LogError("WhimTex: could not change output encoding for " + path + ": " + error.Message); }
            }
        }

        internal static void Change(string path, bool srgb, TextureCompositor owner = null)
        {
            path = WhimTexDocumentService.NormalizeDestination(path);
            if (!WhimTexTiffCarrier.TryReadCarrierFlags(path, out bool stored, out bool document, out string error) || !document)
                throw new WhimTexDocumentException(error ?? "This file is not a WhimTex TIFF document.");
            Pending.Remove(path);
            if (!Updating.Add(path)) throw new WhimTexDocumentException("Output encoding is already being changed.");
            try
            {
                if (srgb != stored) WhimTexDocumentFile.ReencodeOutput(path, srgb, owner);
            }
            finally
            {
                try
                {
                    // Cancellation/failure must not leave importer interpretation mismatched to disk.
                    if (WhimTexTiffCarrier.TryReadCarrierFlags(path, out bool actual, out bool valid, out _) && valid &&
                        AssetImporter.GetAtPath(path) is TextureImporter importer && importer.sRGBTexture != actual)
                    {
                        importer.sRGBTexture = actual;
                        importer.SaveAndReimport();
                    }
                }
                finally { Updating.Remove(path); }
            }
        }
    }
}
