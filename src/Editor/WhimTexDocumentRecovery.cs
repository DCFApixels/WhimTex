using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexDocumentRecovery
    {
        [MenuItem("Tools/WhimTex/Recovery/Recover Staged TIFF…")]
        private static void Recover()
        {
            string source = EditorUtility.OpenFilePanel("Recover an interrupted WhimTex save", "Assets", "whimtex-tmp");
            if (string.IsNullOrEmpty(source)) return;
            string path = EditorUtility.SaveFilePanelInProject("Recover to a new TIFF", "Recovered.whimtex", "tiff",
                "Creates a new document. The original document and temporary file are kept.");
            if (string.IsNullOrEmpty(path)) return;
            using var operation = new WhimTexDocumentOperation("Recover WhimTex document");
            try
            {
                RecoverTo(source, path);
                EditorUtility.DisplayDialog("WhimTex", "Recovered to " + path + ". The original and temporary files were not removed.", "OK");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error); EditorUtility.DisplayDialog("WhimTex", error.Message, "OK"); }
        }

        internal static void RecoverTo(string source, string destination)
        {
            if (!source.EndsWith(".whimtex-tmp", StringComparison.OrdinalIgnoreCase))
                throw new WhimTexDocumentException("Select a WhimTex staging file ending in .whimtex-tmp.");
            destination = WhimTexDocumentService.NormalizeDestination(destination);
            if (File.Exists(destination) || File.Exists(destination + ".meta"))
                throw new WhimTexDocumentException("Recovery needs a new destination; existing files are never overwritten.");
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length < 24 || input.Length > 4L * 1024 * 1024 * 1024)
                throw new WhimTexDocumentException("Invalid staged document size.");
            WhimTexTiffImage.ValidateStream(input);
            input.Position = input.Length - 16;
            using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true);
            long length = reader.ReadInt64();
            if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8)) != "WHIMTEXD")
                throw new WhimTexDocumentException("The interrupted save is incomplete; the original document was not changed.");
            using var container = WhimTexDocumentContainer.Open(input, input.Length - 16 - length, length);
            WhimTexDocumentLimits.ValidateDirectory(container);
            if (!container.HasDocument) throw new WhimTexDocumentException("The staged file has no document model.");
            var names = new List<string>(container.Names);
            for (int i = 0; i < names.Count; i++)
            {
                WhimTexDocumentOperation.Report("Verifying staged document blocks", .1f + .65f * i / names.Count);
                string name = names[i];
                WhimTexDocumentOperation.Run(() => container.Get(name));
                container.Remove(name);
            }
            WhimTexDocumentContainer.WriteStaged(destination, output =>
            {
                if (File.Exists(destination) || File.Exists(destination + ".meta"))
                    throw new WhimTexDocumentException("The recovery destination now exists; choose a new path.");
                input.Position = 0;
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    WhimTexDocumentOperation.Report("Copying recovered document", .8f + .1f * input.Position / input.Length);
                    output.Write(buffer, 0, read);
                }
            });
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            WhimTexDocumentFile.ValidateImportedTexture(destination);
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(destination);
        }
    }
}
