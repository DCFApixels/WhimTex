using System;
using System.Collections.Generic;
using System.IO;
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
        private const string ModelField = "outputTexture";
        private sealed class SaveSnapshot
        {
            internal string path, signature, importer;
            internal long length;
            internal DateTime written;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextureCompositor, SaveSnapshot> Saves =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TextureCompositor, SaveSnapshot>();

        /// <summary>True when the file carries a WhimTex document, used to distinguish documents from plain images.</summary>
        public static bool IsDocument(string assetPath) => WhimTexTiffCarrier.IsDocument(assetPath);

        internal static (int blocks, long modelBytes, long textureBytes) InspectStorage(string path)
        {
            // Directory-only inspection: no textures, shaders, editor window, or pixel inflation.
            using var container = WhimTexTiffCarrier.OpenContainer(path);
            long pixels = 0;
            foreach (string name in container.Names)
                if (name.StartsWith("texture:", StringComparison.Ordinal) && !name.EndsWith(":sampling", StringComparison.Ordinal))
                    pixels += container.LengthOf(name);
            return (container.Count, container.LengthOf(WhimTexDocumentContainer.DocumentBlock), pixels);
        }

        /// <summary>Saves the document and returns the TIFF path; HDR never changes its extension.</summary>
        public static string Save(TextureCompositor document, string path, bool deferImport = false)
        {
            if (document == null) throw new WhimTexDocumentException("There is no document to save.");
            if (string.IsNullOrEmpty(path)) throw new WhimTexDocumentException("The document path is empty.");
            if (!string.IsNullOrEmpty(document.documentLoadWarning))
                throw new WhimTexDocumentException("Saving is blocked to prevent data loss. Reopen this document with all required types, fields and assets available. " + document.documentLoadWarning);
            WhimTexDocumentOperation.Report("Checking document limits", .02f);
            WhimTexDocumentLimits.Validate(document);
            // Converting a legacy asset must never rebind or mutate that asset's output.
            if (AssetDatabase.Contains(document))
            {
                var copy = CreateEditableCopy(document);
                try { return Save(copy, path, deferImport); }
                finally { UnityEngine.Object.DestroyImmediate(copy); }
            }
            path = WhimTexDocumentService.NormalizeDestination(path);
            using var writeLease = WhimTexDocumentService.BeginWrite(document, path);
            ValidateEffectsForSave(document);
            foreach (var effect in EnumerateEffects(document)) effect.PreserveDocumentIncludeBase();
            bool wasLive = WhimTexDocumentSession.IsLiveFor(document);
            using var liveSave = WhimTexDocumentSession.SuspendForSave(document);
            if (wasLive) deferImport = false;
            Undo.FlushUndoRecordObjects();
            document.NormalizeModel();
            document.SyncDrawingLayerTextures();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long modelMs = 0, carrierMs = 0, writeMs = 0, importMs = 0;
            using var container = new WhimTexDocumentContainer();
            container.Set(WhimTexDocumentContainer.DocumentBlock,
                WhimTexDocumentSerializer.Serialize(document, container), System.IO.Compression.CompressionLevel.Optimal);
            WhimTexDocumentOperation.Report("Compressing and verifying Drawing blocks", .2f);
            container.PrepareStoredBlocks();
            modelMs = stopwatch.ElapsedMilliseconds;
            string signature = container.HasExternalInputs ? null : container.ContentSignature();
            var existingImporter = AssetImporter.GetAtPath(path) as TextureImporter;
            string importerState = existingImporter == null ? null : EditorJsonUtility.ToJson(existingImporter);
            var fileState = new FileInfo(path);
            if (signature != null && Saves.TryGetValue(document, out SaveSnapshot snapshot) && snapshot.path == path &&
                snapshot.signature == signature && snapshot.importer == importerState && fileState.Exists &&
                snapshot.written == fileState.LastWriteTimeUtc && snapshot.length == fileState.Length &&
                !ImportHasErrors(path) && AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
            {
                BindImportedComposite(document, path);
                WhimTexDocumentService.Bind(document, path);
                return path;
            }

            Texture2D composite = null;
            bool firstSave = false;
            bool wrote = true;
            try
            {
                WhimTexDocumentOperation.Report("Rendering composite", .4f);
                composite = document.Compose();
                if (composite == null) throw new WhimTexDocumentException("The document produced no composite image.");
                if (!composite.isReadable)
                    throw new WhimTexDocumentException("The composite image of the document is not readable and cannot be saved.");
                // One carrier format for every document: the extension is part of the asset path, so
                // switching it later would break every reference that points at this texture.
                firstSave = !File.Exists(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                // Saving an unchanged document must not write the file or pay for an import: the carrier
                // is byte for byte deterministic, so an identical file means identical content. Auto refresh
                // stays off while the file changes, otherwise Unity imports it once for the write itself and
                // again for the explicit import below.
                AssetDatabase.DisallowAutoRefresh();
                try
                {
                    wrote = WhimTexDocumentContainer.WriteStaged(path, stream =>
                    {
                        WhimTexTiffCarrier.WriteTo(stream, container, composite, importer == null ? null : (bool?)importer.sRGBTexture, document.outputPrecision);
                        carrierMs = stopwatch.ElapsedMilliseconds - modelMs;
                        stream.Position = stream.Length - 16;
                        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
                        long payloadLength = reader.ReadInt64();
                        WhimTexDocumentContainer.ReadSmallBlock(stream, stream.Length - 16 - payloadLength,
                            payloadLength, WhimTexTiffCarrier.FlagsBlock);
                    });
                }
                finally
                {
                    AssetDatabase.AllowAutoRefresh();
                }
                writeMs = stopwatch.ElapsedMilliseconds - modelMs - carrierMs;
                // An identical staged file has not committed yet. Stop cancellation before any .meta write too.
                WhimTexDocumentOperation.Commit();
                // Float TIFF samples are linear. LDR encoding follows the existing importer;
                // first import gets its flag from the carrier, never from the temporary compose texture.
                bool srgb = (container.Get(WhimTexTiffCarrier.FlagsBlock)[0] & 1) != 0;
                if (importer != null && importer.sRGBTexture != srgb)
                {
                    importer.sRGBTexture = srgb;
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                    wrote = true;
                }
            }
            finally
            {
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
            }
            long beforeImport = stopwatch.ElapsedMilliseconds;
            // The disk transaction has succeeded even if Unity's following import fails.
            // Keep that revision so retrying Save is not mistaken for an external conflict.
            WhimTexDocumentService.Bind(document, path);
            document.documentBinding.dirty = true;
            // Identical bytes after a failed import do not prove a usable artifact exists.
            if (wrote || firstSave || ImportHasErrors(path) || AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                // A deferred import lets a save return without waiting for Unity to decode the carrier and
                // compress the texture. The texture object stays the same, so references and bindings remain
                // valid while the new pixels arrive a moment later. A first save must import synchronously,
                // because the asset has to exist before anything can point at it.
                ImportAssetOptions options = ImportAssetOptions.ForceUpdate;
                if (!deferImport || firstSave) options |= ImportAssetOptions.ForceSynchronousImport;
                AssetDatabase.ImportAsset(path, options);
            }
            if (!deferImport || firstSave) ValidateImportedTexture(path);
            importMs = stopwatch.ElapsedMilliseconds - beforeImport;
            stopwatch.Stop();
            // Reported only when a save is slow enough to be felt, so the numbers come from a real document
            // instead of a synthetic one.
            if (stopwatch.ElapsedMilliseconds > 250)
                Debug.Log("WhimTex: saved " + path + " in " + stopwatch.ElapsedMilliseconds + "ms (model "
                    + modelMs + "ms, carrier " + carrierMs + "ms, file " + writeMs + "ms, import " + importMs
                    + "ms, written " + wrote + ", deferred " + (deferImport && !firstSave) + ")");
            // The file image is the composite: the saved document must point at it, not at a stale texture.
            BindImportedComposite(document, path);
            WhimTexDocumentService.Bind(document, path);
            if (deferImport && !firstSave) ScheduleImportValidation(document);
            if (signature != null)
            {
                fileState.Refresh();
                Saves.Remove(document);
                Saves.Add(document, new SaveSnapshot { path = path, signature = signature, length = fileState.Length,
                    written = fileState.LastWriteTimeUtc, importer = EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path)) });
            }
            return path;
        }

        private static void ScheduleImportValidation(TextureCompositor document)
        {
            var binding = document.documentBinding;
            string path = binding.path;
            long ticks = binding.writeTicks, length = binding.length;
            var owner = new WeakReference<TextureCompositor>(document);
            void Verify()
            {
                if (!owner.TryGetTarget(out var current) || current == null || current.documentBinding != binding || binding == null ||
                    binding.path != path || binding.writeTicks != ticks || binding.length != length)
                { EditorApplication.update -= Verify; return; }
                if (EditorApplication.isUpdating || EditorApplication.isCompiling)
                    return;
                EditorApplication.update -= Verify;
                try { ValidateImportedTexture(path); }
                catch (WhimTexDocumentException error)
                {
                    // Disk save succeeded; keep its revision but leave a retryable, visible failure.
                    binding.dirty = true;
                    Debug.LogException(error);
                }
            }
            // Unlike delayCall, update does not depend on an Inspector GUI refresh.
            EditorApplication.update += Verify;
        }

        /// <summary>Independent working copy, also used to migrate legacy assets without changing them.</summary>
        internal static TextureCompositor CreateEditableCopy(TextureCompositor source)
        {
            if (source == null || !string.IsNullOrEmpty(source.documentLoadWarning))
                throw new WhimTexDocumentException("An incomplete document cannot be copied safely.");
            ValidateEffectsForSave(source);
            source.SyncDrawingLayerTextures();
            // An independent in-memory copy needs raw snapshots, not compressed-cache lookup/inflation.
            using var container = new WhimTexDocumentContainer { ReusePixelCache = false };
            var model = WhimTexDocumentSerializer.Serialize(source, container);
            var copy = (TextureCompositor)WhimTexDocumentSerializer.Deserialize(model, container, typeof(TextureCompositor));
            if (copy == null || copy == source || AssetDatabase.Contains(copy))
                throw new WhimTexDocumentException("The document copy did not produce an independent working model.");
            try
            {
                if (WhimTexDocumentSerializer.LastSkippedFields.Count != 0 || WhimTexDocumentSerializer.LastMissingTypes.Count != 0 ||
                    WhimTexDocumentSerializer.LastUnresolvedReferences.Count != 0)
                    throw new WhimTexDocumentException("The document cannot be copied without losing data.");
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.name = source.name;
                copy.SpriteOutputSettings.linkedTextureGuid = null;
                // Resolve relative includes against the source until the new document is saved.
                BindImportedComposite(copy, AssetDatabase.GetAssetPath(source.OutputTexture));
                foreach (var effect in EnumerateEffects(copy))
                    if (!AssetDatabase.Contains(effect))
                    {
                        effect.RestoreDocumentOwner(copy);
                        effect.RestoreDocumentShader();
                    }
                return copy;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
        }

        /// <summary>Loads an editable model; incomplete models are protected against lossy saves.</summary>
        public static TextureCompositor Load(string path)
        {
            if (!TryLoad(path, out TextureCompositor document, out string error))
                throw new WhimTexDocumentException(error);
            return document;
        }

        public static bool TryLoad(string path, out TextureCompositor document, out string error)
            => TryLoad(path, out document, out error, true);

        internal static bool TryLoad(string path, out TextureCompositor document, out string error, bool prepareEffects)
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
                using var container = WhimTexTiffCarrier.OpenContainer(path);
                WhimTexDocumentOperation.Report("Checking document directory", .02f);
                WhimTexDocumentLimits.ValidateDirectory(container);
                if (container.LengthOf(WhimTexDocumentContainer.DocumentBlock) > 64L * 1024 * 1024)
                    throw new WhimTexDocumentException("The document model exceeds the 64 MiB load budget.");
                if (!container.TryGet(WhimTexDocumentContainer.DocumentBlock, out byte[] model))
                {
                    error = "The document has no model block.";
                    return false;
                }
                document = WhimTexDocumentSerializer.Deserialize(model, container, typeof(TextureCompositor),
                    Path.GetFullPath(path), deferDrawingTextures: true) as TextureCompositor;
                if (document == null)
                {
                    error = "The document model could not be reconstructed.";
                    return false;
                }
                if (WhimTexDocumentSerializer.LastSkippedFields.Count > 0)
                    Debug.LogWarning("WhimTex: the document carries fields this build no longer declares: " +
                        string.Join(", ", WhimTexDocumentSerializer.LastSkippedFields) +
                        ". Saving is blocked to protect the original data.");
                if (WhimTexDocumentSerializer.LastMissingTypes.Count > 0)
                    Debug.LogWarning("WhimTex: the document references layer types this build does not have: " +
                        string.Join(", ", WhimTexDocumentSerializer.LastMissingTypes) +
                        ". Saving is blocked to protect the original data.");
                var warnings = new List<string>();
                warnings.AddRange(WhimTexDocumentSerializer.LastSkippedFields);
                warnings.AddRange(WhimTexDocumentSerializer.LastMissingTypes);
                warnings.AddRange(WhimTexDocumentSerializer.LastUnresolvedReferences);
                document.documentLoadWarning = warnings.Count == 0 ? null : string.Join(", ", warnings);
                if (WhimTexDocumentSerializer.LastUnresolvedReferences.Count > 0)
                    Debug.LogWarning("WhimTex: some referenced assets are missing. Saving is blocked until they are restored: " + document.documentLoadWarning);
                document.hideFlags = HideFlags.HideAndDontSave;
                document.name = Path.GetFileNameWithoutExtension(path);
                if (document.name.EndsWith(".whimtex", StringComparison.OrdinalIgnoreCase))
                    document.name = document.name.Substring(0, document.name.Length - ".whimtex".Length);
                BindImportedComposite(document, path);
                WhimTexDocumentService.Bind(document, path);
                foreach (var effect in EnumerateEffects(document))
                    if (!AssetDatabase.Contains(effect))
                    {
                        effect.RestoreDocumentOwner(document);
                        effect.SuspendDocumentCatalogReload();
                    }
                if (prepareEffects) CompileEmbeddedEffects(document);
                return true;
            }
            catch (OperationCanceledException)
            {
                if (document != null) UnityEngine.Object.DestroyImmediate(document);
                document = null;
                throw;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (document != null) UnityEngine.Object.DestroyImmediate(document);
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

        internal static bool ImportHasErrors(string path)
        {
            var log = AssetImporter.GetImportLog(path);
            if (log == null) return false;
            foreach (var entry in log.logEntries)
                if ((entry.flags & UnityEditor.AssetImporters.ImportLogFlags.Error) != 0) return true;
            return false;
        }

        internal static void ValidateImportedTexture(string path)
        {
            if (ImportHasErrors(path) || AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                throw new WhimTexDocumentException("The TIFF was written, but Unity could not import its texture. Fix the import error and retry Save or Reimport: " + path);
        }

        /// <summary>Shaders are not stored: every embedded effect is compiled again after loading.</summary>
        private static void CompileEmbeddedEffects(TextureCompositor document)
        {
            foreach (ShaderFX effect in EnumerateEffects(document))
            {
                if (AssetDatabase.Contains(effect)) continue;
                WhimTexDocumentOperation.Report("Preparing Shader FX", .9f);
                try { effect.RestoreDocumentShader(); }
                catch (Exception error) { Debug.LogWarning("WhimTex: could not compile an embedded effect: " + error.Message); }
            }
        }

        private static void ValidateEffectsForSave(TextureCompositor document)
        {
            foreach (var effect in EnumerateEffects(document))
                if (effect.LastApplyFailed || effect.HasPendingChanges)
                    throw new WhimTexDocumentException("Apply or fix Shader FX '" + effect.name + "' before saving. The existing TIFF was not changed. " + effect.Diagnostics);
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

    }
}
