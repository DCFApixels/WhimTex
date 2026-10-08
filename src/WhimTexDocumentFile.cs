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
    public static partial class WhimTexDocumentFile
    {
        private const string ModelField = "outputTexture";
        private sealed class SaveSnapshot
        {
            internal string path, signature, importer;
            internal long length;
            internal DateTime written;
            internal bool srgb;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WhimTexDocument, SaveSnapshot> Saves =
            new System.Runtime.CompilerServices.ConditionalWeakTable<WhimTexDocument, SaveSnapshot>();

        /// <summary>True when the file carries a WhimTex document, used to distinguish documents from plain images.</summary>
        public static bool IsDocument(string assetPath) => WhimTexDocumentJson.IsJsonPath(assetPath)
            ? WhimTexDocumentJson.IsDocumentFile(assetPath) : IsTiffDocumentPath(assetPath) && WhimTexTiffCarrier.IsDocument(assetPath);

        private static bool IsTiffDocumentPath(string path) =>
            string.Equals(Path.GetExtension(path), ".tiff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(path), ".tif", StringComparison.OrdinalIgnoreCase);

        internal static (int blocks, long modelBytes, long textureBytes, string[] names, long[] sizes) InspectStorage(string path)
        {
            // Directory-only inspection: no textures, shaders, editor window, or pixel inflation.
            using var container = WhimTexTiffCarrier.OpenContainer(path);
            long pixels = 0;
            var names = new List<string>();
            var sizes = new List<long>();
            foreach (string name in container.Names)
            {
                long size = container.LengthOf(name);
                names.Add(name);
                sizes.Add(size);
                if (name.StartsWith("texture:", StringComparison.Ordinal) && !name.EndsWith(":sampling", StringComparison.Ordinal))
                    pixels += size;
            }
            return (container.Count, container.LengthOf(WhimTexDocumentContainer.DocumentBlock), pixels, names.ToArray(), sizes.ToArray());
        }

        /// <summary>Saves the document and returns the TIFF path; HDR never changes its extension.</summary>
        public static string Save(WhimTexDocument document, string path, bool deferImport = false, bool allowDataLoss = false)
        {
            if (WhimTexDocumentJson.IsJsonPath(path)) return SaveJson(document, path,
                new WhimTexJsonWriteOptions { Mode = document == null ? WhimTexJsonWriteMode.FullOptimized : document.JsonWriteMode,
                    AllowDataLoss = allowDataLoss }, deferImport: deferImport);
            if (document == null) throw new WhimTexDocumentException("There is no document to save.");
            if (string.IsNullOrEmpty(path)) throw new WhimTexDocumentException("The document path is empty.");
            if (!allowDataLoss && !string.IsNullOrEmpty(document.documentLoadWarning))
                throw new WhimTexDocumentException("Saving is blocked to prevent data loss. Restore the missing data and reopen, or explicitly allow saving only the data that was loaded. " + document.documentLoadWarning);
            WhimTexDocumentOperation.Report("Checking document limits", .02f);
            WhimTexDocumentLimits.Validate(document);
            if (AssetDatabase.Contains(document))
                throw new WhimTexDocumentException("Editable documents must be in-memory models, not Unity assets.");
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
                snapshot.signature == signature && snapshot.importer == importerState && snapshot.srgb == document.outputSrgb && fileState.Exists &&
                snapshot.written == fileState.LastWriteTimeUtc && snapshot.length == fileState.Length &&
                !ImportHasErrors(path) && AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
            {
                BindImportedComposite(document, path);
                WhimTexDocumentService.Bind(document, path);
                if (allowDataLoss) document.documentLoadWarning = null;
                return path;
            }

            Texture2D composite = null;
            bool firstSave = false;
            bool wrote = true;
            try
            {
                WhimTexDocumentOperation.Report("Rendering composite", .4f);
                composite = document.ComposeCanvas();
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
                        WhimTexTiffCarrier.WriteTo(stream, container, composite, document.outputSrgb, document.outputPrecision);
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
                // Float TIFF samples are linear. LDR encoding follows the pending document setting;
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
            // Enable the WHIMTEX_DEBUG scripting define when profiling real saves is needed.
#if WHIMTEX_DEBUG
            if (stopwatch.ElapsedMilliseconds > 250)
                Debug.Log("WhimTex: saved " + path + " in " + stopwatch.ElapsedMilliseconds + "ms (model "
                    + modelMs + "ms, carrier " + carrierMs + "ms, file " + writeMs + "ms, import " + importMs
                    + "ms, written " + wrote + ", deferred " + (deferImport && !firstSave) + ")");
#endif
            // The file image is the composite: the saved document must point at it, not at a stale texture.
            BindImportedComposite(document, path);
            WhimTexDocumentService.Bind(document, path);
            if (deferImport && !firstSave) ScheduleImportValidation(document);
            if (signature != null)
            {
                fileState.Refresh();
                Saves.Remove(document);
                Saves.Add(document, new SaveSnapshot { path = path, signature = signature, length = fileState.Length,
                    written = fileState.LastWriteTimeUtc, importer = EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path)), srgb = document.outputSrgb });
            }
            if (allowDataLoss) document.documentLoadWarning = null;
            return path;
        }

        private static void ScheduleImportValidation(WhimTexDocument document)
        {
            var binding = document.documentBinding;
            string path = binding.path;
            long ticks = binding.writeTicks, length = binding.length;
            var owner = new WeakReference<WhimTexDocument>(document);
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

        /// <summary>Independent in-memory document snapshot with its own Drawing pixels and FX.</summary>
        internal static WhimTexDocument CreateEditableCopy(WhimTexDocument source)
        {
            if (source == null || !string.IsNullOrEmpty(source.documentLoadWarning))
                throw new WhimTexDocumentException("An incomplete document cannot be copied safely.");
            if (AssetDatabase.Contains(source))
                throw new WhimTexDocumentException("Editable documents must be in-memory models, not Unity assets.");
            ValidateEffectsForSave(source);
            source.SyncDrawingLayerTextures();
            // An independent in-memory copy needs raw snapshots, not compressed-cache lookup/inflation.
            using var container = new WhimTexDocumentContainer { ReusePixelCache = false };
            var model = WhimTexDocumentSerializer.Serialize(source, container);
            var read = WhimTexDocumentSerializer.Deserialize(model, container, typeof(WhimTexDocument));
            var copy = (WhimTexDocument)read.Model;
            if (copy == null || copy == source || AssetDatabase.Contains(copy))
                throw new WhimTexDocumentException("The document copy did not produce an independent working model.");
            try
            {
                if (read.SkippedFields.Count != 0 || read.MissingTypes.Count != 0 ||
                    read.UnresolvedReferences.Count != 0)
                    throw new WhimTexDocumentException("The document cannot be copied without losing data.");
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.name = source.name;
                // Resolve relative includes against the source until the new document is saved.
                BindImportedComposite(copy, AssetDatabase.GetAssetPath(source.OutputTexture));
                copy.outputSrgb = GetOutputSrgb(source);
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
        public static WhimTexDocument Load(string path)
        {
            if (!TryLoad(path, out WhimTexDocument document, out string error))
                throw new WhimTexDocumentException(error);
            return document;
        }

        public static bool TryLoad(string path, out WhimTexDocument document, out string error)
            => TryLoad(path, out document, out error, true);

        internal static bool TryLoad(string path, out WhimTexDocument document, out string error, bool prepareEffects)
            => TryLoad(path, out document, out error, prepareEffects, out _);

        internal static bool TryLoad(string path, out WhimTexDocument document, out string error, bool prepareEffects,
            out IReadOnlyList<string> loadWarnings)
        {
            loadWarnings = Array.Empty<string>();
            if (WhimTexDocumentJson.IsJsonPath(path)) return TryLoadJson(path, out document, out error, prepareEffects, out loadWarnings);
            document = null;
            error = null;
            if (!IsTiffDocumentPath(path))
            {
                error = "Editable documents use TIFF or JSON files.";
                return false;
            }
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
                var read = WhimTexDocumentSerializer.Deserialize(model, container, typeof(WhimTexDocument),
                    Path.GetFullPath(path), deferDrawingTextures: true);
                document = read.Model as WhimTexDocument;
                if (document == null)
                {
                    error = "The document model could not be reconstructed.";
                    return false;
                }
                if (read.SkippedFields.Count > 0)
                    Debug.LogWarning("WhimTex: the document carries fields this build no longer declares: " +
                        string.Join(", ", read.SkippedFields) +
                        ". Saving requires explicit confirmation before discarding unread data.");
                if (read.MissingTypes.Count > 0)
                    Debug.LogWarning("WhimTex: the document references layer types this build does not have: " +
                        string.Join(", ", read.MissingTypes) +
                        ". Saving requires explicit confirmation before discarding unread data.");
                var warnings = new List<string>();
                warnings.AddRange(read.SkippedFields);
                warnings.AddRange(read.MissingTypes);
                warnings.AddRange(read.UnresolvedReferences);
                document.documentLoadWarning = warnings.Count == 0 ? null : string.Join(", ", warnings);
                if (read.UnresolvedReferences.Count > 0)
                    Debug.LogWarning("WhimTex: some referenced assets are missing. Restore them or explicitly confirm saving without their references: " + document.documentLoadWarning);
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
        private static void BindImportedComposite(WhimTexDocument document, string path)
        {
            // Unsaved document copies have no imported composite or TIFF carrier to inspect.
            if (string.IsNullOrEmpty(path)) return;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                document.outputSrgb = importer.sRGBTexture;
            else if (WhimTexTiffCarrier.TryReadCarrierFlags(path, out bool srgb, out bool isDocument, out _) && isDocument)
                document.outputSrgb = srgb;
            var composite = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (composite == null) return;
            FieldInfo field = typeof(WhimTexDocument).GetField(ModelField,
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
        private static void CompileEmbeddedEffects(WhimTexDocument document)
        {
            foreach (ShaderFX effect in EnumerateEffects(document))
            {
                if (AssetDatabase.Contains(effect)) continue;
                WhimTexDocumentOperation.Report("Preparing Shader FX", .9f);
                try { effect.RestoreDocumentShader(); }
                catch (Exception error) { Debug.LogWarning("WhimTex: could not compile an embedded effect: " + error.Message); }
            }
        }

        private static void ValidateEffectsForSave(WhimTexDocument document)
        {
            foreach (var effect in EnumerateEffects(document))
                if (effect.LastApplyFailed || effect.HasPendingChanges)
                    throw new WhimTexDocumentException("Apply or fix Shader FX '" + effect.name + "' before saving. The existing TIFF was not changed. " + effect.Diagnostics);
        }

        private static IEnumerable<ShaderFX> EnumerateEffects(WhimTexDocument document)
        {
            var seen = new HashSet<ShaderFX>();
            var stack = new Stack<Layer>();
            if (document.layers == null) yield break;
            foreach (Layer layer in document.layers) stack.Push(layer);
            while (stack.Count > 0)
            {
                Layer layer = stack.Pop();
                if (layer == null) continue;
                if (layer.fx != null)
                    foreach (UnityEngine.Object fxEntry in layer.fx)
                        if (fxEntry is ShaderFX effect && seen.Add(effect)) yield return effect;
                if (layer.children != null)
                    foreach (Layer child in layer.children) stack.Push(child);
            }
        }

    }
}
