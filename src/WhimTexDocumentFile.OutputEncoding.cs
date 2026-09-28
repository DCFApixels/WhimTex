using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentFile
    {
        /// <summary>Output encoding, not the working space or Drawing storage format.</summary>
        public static bool GetOutputSrgb(TextureCompositor document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.outputPrecision == WhimTexOutputPrecision.Float32) return false;
            return document.outputSrgb;
        }

        /// <summary>
        /// Sets pending output encoding and marks the document changed; Save writes it to the TIFF.
        /// Float output always remains linear. Call on the Editor main thread; callers own Undo.
        /// </summary>
        public static void SetOutputSrgb(TextureCompositor document, bool srgb)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (AssetDatabase.Contains(document))
                throw new WhimTexDocumentException("Save this legacy document as TIFF before changing its output encoding.");
            if (document.outputPrecision == WhimTexOutputPrecision.Float32) srgb = false;
            if (document.outputSrgb == srgb) return;
            document.outputSrgb = srgb;
            document.MarkChanged();
        }

        internal static void ReencodeOutput(string path, bool srgb, TextureCompositor owner)
        {
            using var operation = new WhimTexDocumentOperation("Change TIFF output encoding");
            TextureCompositor saved = null;
            Texture2D composite = null;
            try
            {
                saved = Load(path);
                if (!string.IsNullOrEmpty(saved.documentLoadWarning))
                    throw new WhimTexDocumentException("Output encoding cannot be changed for an incomplete document: " + saved.documentLoadWarning);
                owner = owner != null ? owner : WhimTexDocumentService.FindDisplayed(path) ?? saved;
                using var lease = WhimTexDocumentService.BeginWrite(owner, path);
                var previousFile = new FileInfo(path);
                long previousLength = previousFile.Length, previousTicks = previousFile.LastWriteTimeUtc.Ticks;
                ValidateEffectsForSave(saved);
                WhimTexDocumentOperation.Report("Rendering saved layers", .25f);
                composite = saved.Compose();
                if (composite == null || !composite.isReadable)
                    throw new WhimTexDocumentException("The saved document produced no readable composite.");
                WhimTexDocumentContainer detached;
                using (var source = WhimTexTiffCarrier.OpenContainer(path)) detached = source.CopyStored();
                using (detached)
                {
                    // Never serialize the open working model or re-encode its Drawing pixels.
                    bool dirty = owner.documentBinding != null && owner.documentBinding.dirty;
                    WhimTexDocumentSession.StopFor(owner, "Output encoding changed. Live Update stopped.");
                    AssetDatabase.DisallowAutoRefresh();
                    try
                    {
                        WhimTexDocumentContainer.WriteStaged(path, stream =>
                            WhimTexTiffCarrier.WriteTo(stream, detached, composite, srgb, saved.outputPrecision));
                        WhimTexDocumentOperation.Commit();
                        bool actual = (detached.Get(WhimTexTiffCarrier.FlagsBlock)[0] & 1) != 0;
                        // Rebind immediately after commit, including when the subsequent import fails.
                        WhimTexDocumentService.Bind(owner, path);
                        owner.documentBinding.dirty = dirty;
                        RebindDeferredDrawing(owner, path, previousLength, previousTicks);
                        owner.outputSrgb = actual;
                        Saves.Remove(owner);
                        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        if (importer != null && importer.sRGBTexture != actual)
                        {
                            importer.sRGBTexture = actual;
                            AssetDatabase.WriteImportSettingsIfDirty(path);
                        }
                    }
                    finally { AssetDatabase.AllowAutoRefresh(); }
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                ValidateImportedTexture(path);
                BindImportedComposite(owner, path);
            }
            finally
            {
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
                if (saved != null) UnityEngine.Object.DestroyImmediate(saved);
            }
        }

        private static void RebindDeferredDrawing(TextureCompositor owner, string path, long oldLength, long oldTicks)
        {
            string absolutePath = Path.GetFullPath(path);
            var pending = new Stack<Layer>(owner.layers);
            var visited = new HashSet<Layer>();
            while (pending.Count > 0)
            {
                var layer = pending.Pop();
                if (layer == null || !visited.Add(layer)) continue;
                if (layer.Behaviour is DrawingLayerBehaviour drawing)
                    drawing.RebindDeferredTexture(absolutePath, oldLength, oldTicks,
                        owner.documentBinding.length, owner.documentBinding.writeTicks);
                if (layer.children != null) foreach (var child in layer.children) pending.Push(child);
            }
        }
    }
}
