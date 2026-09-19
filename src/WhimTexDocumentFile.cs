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
        public static bool IsDocument(string assetPath)
        {
            return WhimTexTiffCarrier.TryRead(assetPath, out _, out _);
        }

        /// <summary>Saves the document and returns the path actually written: the carrier extension depends on the storage format.</summary>
        public static string Save(TextureCompositor document, string path)
        {
            if (document == null) throw new WhimTexDocumentException("There is no document to save.");
            if (string.IsNullOrEmpty(path)) throw new WhimTexDocumentException("The document path is empty.");
            var container = new WhimTexDocumentContainer();
            container.Set(WhimTexDocumentContainer.DocumentBlock,
                WhimTexDocumentSerializer.Serialize(document, container), System.IO.Compression.CompressionLevel.Optimal);

            Texture2D composite = null;
            try
            {
                composite = document.Compose();
                if (composite == null) throw new WhimTexDocumentException("The document produced no composite image.");
                if (!composite.isReadable)
                    throw new WhimTexDocumentException("The composite image of the document is not readable and cannot be saved.");
                // One carrier format for every document: the extension is part of the asset path, so
                // switching it later would break every reference that points at this texture.
                path = WithExtension(path, WhimTexTiffCarrier.Extension);
                byte[] carrier = WhimTexTiffCarrier.Write(container, composite);
                if (!WhimTexTiffCarrier.TryRead(carrier, out byte[] verification, out string error))
                    throw new WhimTexDocumentException("The produced document could not be read back: " + error);
                WhimTexDocumentContainer.Parse(verification);
                WhimTexDocumentContainer.WriteFileAtomic(path, carrier);
            }
            finally
            {
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            // The file image is the composite: the saved document must point at it, not at a stale texture.
            BindImportedComposite(document, path);
            return path;
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
