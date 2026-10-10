using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentFile
    {
        /// <summary>Exports JSON without rebinding or marking the source document saved.</summary>
        public static WhimTexJsonWriteResult ExportJson(WhimTexDocument document, string path, WhimTexJsonWriteOptions options = null)
        {
            if (!WhimTexDocumentJson.IsJsonPath(path)) throw new WhimTexDocumentException("Expected a .json destination.");
            path = WhimTexDocumentService.NormalizeDestination(path);
            using var lease = WhimTexDocumentService.BeginWrite(document, path);
            if (string.Equals(WhimTexDocumentService.PathOf(document), path, StringComparison.OrdinalIgnoreCase))
                throw new WhimTexDocumentException("Export must not overwrite the source document. Use Save for its own path.");
            var result = WriteJsonFile(document, path, options);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return result;
        }

        public static string SaveJson(WhimTexDocument document, string path, WhimTexJsonWriteOptions options = null, bool deferImport = false)
        {
            if (document == null) throw new WhimTexDocumentException("There is no document to save.");
            options ??= new WhimTexJsonWriteOptions { Mode = document.JsonWriteMode };
            if (!WhimTexDocumentJson.IsJsonPath(path)) throw new WhimTexDocumentException("Expected a .json destination.");
            path = WhimTexDocumentService.NormalizeDestination(path);
            using var lease = WhimTexDocumentService.BeginWrite(document, path);
            var result = WriteJsonFile(document, path, options);
            if (!deferImport) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            else EditorApplication.delayCall += () => { if (File.Exists(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); };
            WhimTexDocumentService.Bind(document, path);
            document.jsonWriteMode = options.Mode;
            // Exporting without pixels does not make the live, pixel-bearing model match the file.
            if (result.DrawingPixelsOmitted) document.documentBinding.dirty = true;
            foreach (string warning in result.Warnings) Debug.LogWarning("WhimTex: " + warning);
            if (options.AllowDataLoss) document.documentLoadWarning = null;
            return path;
        }

        internal static void SaveJsonSnapshot(WhimTexDocument source, WhimTexDocument snapshot, string path, WhimTexJsonWriteOptions options)
        {
            path = WhimTexDocumentService.NormalizeDestination(path);
            using var lease = WhimTexDocumentService.BeginWrite(source, path);
            WriteJsonFile(snapshot, path, options);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            WhimTexDocumentService.Bind(snapshot, path);
            snapshot.jsonWriteMode = options?.Mode ?? WhimTexJsonWriteMode.FullOptimized;
        }

        private static WhimTexJsonWriteResult WriteJsonFile(WhimTexDocument document, string path, WhimTexJsonWriteOptions options)
        {
            if (document == null || AssetDatabase.Contains(document))
                throw new WhimTexDocumentException("Editable documents must be in-memory models, not Unity assets.");
            var result = WhimTexDocumentJson.Write(document, options);
            using (WhimTexDocumentJson.Read(result.Json, false)) { }
            byte[] data = new UTF8Encoding(false).GetBytes(result.Json);
            WhimTexDocumentContainer.WriteStaged(path, stream => stream.Write(data, 0, data.Length));
            return result;
        }

        private static bool TryLoadJson(string path, out WhimTexDocument document, out string error, bool prepareEffects,
            out IReadOnlyList<string> warnings)
        {
            document = null;
            error = null;
            warnings = Array.Empty<string>();
            try
            {
                if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new WhimTexDocumentException("JSON file exceeds the load budget.");
                using var read = WhimTexDocumentJson.Read(File.ReadAllText(path), prepareEffects);
                warnings = read.Warnings;
                foreach (string warning in read.Warnings)
                    if (!read.Effects.Exists(effect => effect.DiagnosticNotice == warning)) Debug.LogWarning("WhimTex: " + warning);
                document = read.TakeDocument();
                document.name = Path.GetFileNameWithoutExtension(path);
                if (document.name.EndsWith(".whimtex", StringComparison.OrdinalIgnoreCase)) document.name = document.name.Substring(0, document.name.Length - 8);
                WhimTexDocumentService.Bind(document, path);
                return true;
            }
            catch (Exception exception)
            {
                if (document != null) UnityEngine.Object.DestroyImmediate(document);
                document = null;
                error = exception.Message;
                return false;
            }
        }
    }
}
