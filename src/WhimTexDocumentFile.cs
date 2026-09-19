using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Reads and writes a WhimTex document as a single native image asset.
    ///
    /// The image itself is the composite, so Unity imports it with the ordinary TextureImporter and the
    /// user keeps compression, mipmaps, sprite slicing and platform overrides. The document model and the
    /// drawing layer pixels travel in container blocks inside the same file.
    /// </summary>
    public static class WhimTexDocumentFile
    {
        public const string PngExtension = ".png";
        public const string ExrExtension = ".exr";
        private const string ModelField = "outputTexture";

        /// <summary>True when the file carries a WhimTex document, used to distinguish documents from plain images.</summary>
        public static bool IsDocument(string assetPath) => WhimTexTiffCarrier.IsDocument(assetPath);

        /// <summary>Saves the document and returns the path actually written: the carrier extension depends on the storage format.</summary>
        public static string Save(TextureCompositor document, string path, bool deferImport = false)
        {
            if (document == null) throw new WhimTexDocumentException("There is no document to save.");
            if (string.IsNullOrEmpty(path)) throw new WhimTexDocumentException("The document path is empty.");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long modelMs = 0, carrierMs = 0, writeMs = 0, importMs = 0;
            var container = new WhimTexDocumentContainer();
            container.Set(WhimTexDocumentContainer.DocumentBlock,
                WhimTexDocumentSerializer.Serialize(document, container), System.IO.Compression.CompressionLevel.Optimal);
            modelMs = stopwatch.ElapsedMilliseconds;

            Texture2D composite = null;
            bool firstSave = false;
            bool compositeIsSrgb = false;
            bool wrote = true;
            try
            {
                composite = document.Compose();
                if (composite == null) throw new WhimTexDocumentException("The document produced no composite image.");
                if (!composite.isReadable)
                    throw new WhimTexDocumentException("The composite image of the document is not readable and cannot be saved.");
                compositeIsSrgb = composite.isDataSRGB;
                // One carrier format for every document: the extension is part of the asset path, so
                // switching it later would break every reference that points at this texture.
                path = WithExtension(path, WhimTexTiffCarrier.Extension);
                firstSave = !File.Exists(path);
                byte[] carrier = WhimTexTiffCarrier.Write(container, composite);
                carrierMs = stopwatch.ElapsedMilliseconds - modelMs;
                if (!WhimTexTiffCarrier.TryRead(carrier, out byte[] verification, out string error))
                    throw new WhimTexDocumentException("The produced document could not be read back: " + error);
                WhimTexDocumentContainer.Parse(verification);
                // Saving an unchanged document must not write the file or pay for an import: the carrier
                // is byte for byte deterministic, so an identical file means identical content. Auto refresh
                // stays off while the file changes, otherwise Unity imports it once for the write itself and
                // again for the explicit import below.
                AssetDatabase.DisallowAutoRefresh();
                try
                {
                    wrote = !CarrierMatches(path, carrier);
                    if (wrote) WhimTexDocumentContainer.WriteFileAtomic(path, carrier);
                }
                finally
                {
                    AssetDatabase.AllowAutoRefresh();
                }
                writeMs = stopwatch.ElapsedMilliseconds - modelMs - carrierMs;
            }
            finally
            {
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
            }
            long beforeImport = stopwatch.ElapsedMilliseconds;
            if (wrote || firstSave)
            {
                // A deferred import lets a save return without waiting for Unity to decode the carrier and
                // compress the texture. The texture object stays the same, so references and bindings remain
                // valid while the new pixels arrive a moment later. A first save must import synchronously,
                // because the asset has to exist before anything can point at it.
                ImportAssetOptions options = ImportAssetOptions.ForceUpdate;
                if (!deferImport || firstSave) options |= ImportAssetOptions.ForceSynchronousImport;
                AssetDatabase.ImportAsset(path, options);
            }
            importMs = stopwatch.ElapsedMilliseconds - beforeImport;
            stopwatch.Stop();
            // Reported only when a save is slow enough to be felt, so the numbers come from a real document
            // instead of a synthetic one.
            if (stopwatch.ElapsedMilliseconds > 250)
                Debug.Log("WhimTex: saved " + path + " in " + stopwatch.ElapsedMilliseconds + "ms (model "
                    + modelMs + "ms, carrier " + carrierMs + "ms, file " + writeMs + "ms, import " + importMs
                    + "ms, written " + wrote + ", deferred " + (deferImport && !firstSave) + ")");
            ConfigureImportedCarrier(path, firstSave, compositeIsSrgb);
            // The file image is the composite: the saved document must point at it, not at a stale texture.
            BindImportedComposite(document, path);
            return path;
        }

        /// <summary>Compares the file with the bytes about to be written, without loading the file into memory.</summary>
        private static bool CarrierMatches(string path, byte[] bytes)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != bytes.LongLength) return false;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buffer = new byte[64 * 1024];
                long offset = 0;
                while (offset < bytes.LongLength)
                {
                    int wanted = (int)Math.Min(buffer.Length, bytes.LongLength - offset);
                    int total = 0;
                    while (total < wanted)
                    {
                        int read = stream.Read(buffer, total, wanted - total);
                        if (read <= 0) return false;
                        total += read;
                    }
                    for (int i = 0; i < wanted; i++)
                        if (buffer[i] != bytes[offset + i]) return false;
                    offset += wanted;
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Marks the file as a WhimTex document in the importer's .meta, and on the first save sets the
        /// sRGB flag from the document itself. Later saves keep whatever the user configured there,
        /// because that flag decides how Unity reads the 8-bit samples.
        /// </summary>
        private static void ConfigureImportedCarrier(string path, bool firstSave, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            bool changed = false;
            if (string.IsNullOrEmpty(importer.userData) || !importer.userData.Contains(WhimTexTiffCarrier.MetaMarker))
            {
                importer.userData = WhimTexTiffCarrier.MetaMarker;
                changed = true;
            }
            if (firstSave && importer.sRGBTexture != srgb)
            {
                importer.sRGBTexture = srgb;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        public static TextureCompositor Load(string path)
        {
            if (!TryLoad(path, out TextureCompositor document, out string error))
                throw new WhimTexDocumentException(error);
            return document;
        }

        public static bool TryLoad(string path, out TextureCompositor document, out string error)
        {
            document = null;
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "The document file was not found: " + path;
                return false;
            }
            try
            {
                byte[] file = File.ReadAllBytes(path);
                if (!WhimTexTiffCarrier.TryRead(file, out byte[] payload, out error)) return false;
                var container = WhimTexDocumentContainer.Parse(payload);
                if (!container.TryGet(WhimTexDocumentContainer.DocumentBlock, out byte[] model))
                {
                    error = "The document has no model block.";
                    return false;
                }
                document = WhimTexDocumentSerializer.Deserialize(model, container, typeof(TextureCompositor)) as TextureCompositor;
                if (document == null)
                {
                    error = "The document model could not be reconstructed.";
                    return false;
                }
                if (WhimTexDocumentSerializer.LastMissingTypes.Count > 0)
                    Debug.LogWarning("WhimTex: the document references layer types this build does not have: " +
                        string.Join(", ", WhimTexDocumentSerializer.LastMissingTypes) +
                        ". Those layers opened without their behaviour; the document is otherwise intact.");
                document.hideFlags = HideFlags.HideAndDontSave;
                BindImportedComposite(document, path);
                CompileEmbeddedEffects(document);
                return true;
            }
            catch (WhimTexDocumentException exception)
            {
                error = exception.Message;
                document = null;
                return false;
            }
        }

        /// <summary>The file image is the composite, so the loaded document points at it instead of at a sub-asset.</summary>
        private static void BindImportedComposite(TextureCompositor document, string path)
        {
            var composite = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (composite == null) return;
            FieldInfo field = typeof(TextureCompositor).GetField(ModelField,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field?.SetValue(document, composite);
        }

        /// <summary>Shaders are not stored: every embedded effect is compiled again after loading.</summary>
        private static void CompileEmbeddedEffects(TextureCompositor document)
        {
            foreach (ShaderFX effect in EnumerateEffects(document))
            {
                try { effect.Apply(); }
                catch (Exception) { }
            }
        }

        private static IEnumerable<ShaderFX> EnumerateEffects(TextureCompositor document)
        {
            var seen = new HashSet<ShaderFX>();
            var stack = new Stack<Layer>();
            if (document.layers == null) yield break;
            foreach (Layer layer in document.layers) stack.Push(layer);
            while (stack.Count > 0)
            {
                Layer layer = stack.Pop();
                if (layer == null) continue;
                if (layer.modifiers != null)
                    foreach (UnityEngine.Object modifier in layer.modifiers)
                        if (modifier is ShaderFX effect && seen.Add(effect)) yield return effect;
                if (layer.children != null)
                    foreach (Layer child in layer.children) stack.Push(child);
            }
        }

        private static string WithExtension(string path, string extension)
        {
            string current = Path.GetExtension(path);
            if (string.Equals(current, extension, StringComparison.OrdinalIgnoreCase)) return path;
            return string.IsNullOrEmpty(current) ? path + extension : path.Substring(0, path.Length - current.Length) + extension;
        }
    }
}
