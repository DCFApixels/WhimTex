using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class WhimTexDocumentSession
    {
        private static readonly string RecoveryKey = "WhimTex.DocumentLive.ReadableGuid." + Hash128.Compute(Application.dataPath);
        public static string Status { get; private set; } = "No document session.";
        public static bool IsLive => _live != null;
        public static string LivePath => _live?.Path;
        internal static event Action StateChanged;
        public static bool IsLiveFor(TextureCompositor document) => document != null && _live?.Document == document;
        private static Live _live;
        private static double _lastPublish;
        private static bool _dirty, _enablingReadable;
        private static SavePause _saving;

        internal static IDisposable SuspendForSave(TextureCompositor document)
        {
            if (!IsLiveFor(document)) return null;
            var pause = new SavePause(document, _live.Path);
            Live previous = _live;
            _live = null;
            _saving = pause;
            try { previous.Dispose(); }
            catch { _saving = null; RecoverReadable(); throw; }
            StateChanged?.Invoke();
            return pause;
        }

        private sealed class SavePause : IDisposable
        {
            private readonly TextureCompositor document;
            internal readonly string path;
            internal SavePause(TextureCompositor document, string path) { this.document = document; this.path = path; }
            public void Dispose()
            {
                if (_saving != this) return;
                _saving = null;
                string savedPath = WhimTexDocumentService.PathOf(document) ?? path;
                try
                {
                    // Save As may change the importer; finish the old recovery before switching.
                    if (!string.Equals(savedPath, path, StringComparison.OrdinalIgnoreCase)) { RecoverReadable(); Start(document, savedPath); return; }
                    WhimTexDocumentFile.ValidateImportedTexture(path);
                    var target = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    _live = new Live(document, target, path);
                    _dirty = false;
                    Status = "Live Update on " + path;
                }
                catch (Exception error)
                {
                    RecoverReadable();
                    Status = "Live Update could not resume: " + error.Message;
                    Debug.LogWarning("WhimTex: " + Status);
                }
                StateChanged?.Invoke();
            }
        }

        static WhimTexDocumentSession()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            AssemblyReloadEvents.beforeAssemblyReload += () => Stop("domain reload");
            EditorApplication.quitting += () => Stop("editor quit");
            EditorApplication.playModeStateChanged += _ => Stop("play mode transition");
            TextureCompositor.Changed += document => { if (IsLiveFor(document)) _dirty = true; };
            Undo.undoRedoPerformed += () => _dirty = true;
            EditorApplication.update += Tick;
            EditorApplication.delayCall += RecoverReadable;
        }

        public static bool Save(TextureCompositor document, string path)
        {
            try
            {
                string written = WhimTexDocumentFile.Save(document, path);
                Status = "Saved " + written;
                return true;
            }
            catch (Exception error) { Status = "Save failed: " + error.Message; Debug.LogException(error); return false; }
        }

        public static bool Start(TextureCompositor document, string path)
        {
            if (document == null) { Status = "There is no document to edit live."; return false; }
            if (BuildPipeline.isBuildingPlayer) { Status = "Live Update is unavailable during a Player build."; return false; }
            Stop("restart");
            try
            {
                RecoverReadable();
                if (EditorPrefs.HasKey(RecoveryKey))
                    throw new InvalidOperationException("Resolve the pending WhimTex texture recovery before starting another Live Update session.");
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("The document image is missing: " + path);
                if (importer.textureShape != TextureImporterShape.Texture2D ||
                    importer.textureType != TextureImporterType.Default && importer.textureType != TextureImporterType.Sprite)
                    throw new InvalidOperationException("Live Update supports 2D Default and Sprite textures. Other import types update on Save.");
                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (imported != null && imported.format.ToString().Contains("Crunched"))
                    throw new InvalidOperationException("Live Update cannot reinitialize Crunch textures. Disable Crunch or update on Save.");
                if (!importer.isReadable)
                {
                    EditorPrefs.SetString(RecoveryKey, AssetDatabase.AssetPathToGUID(path));
                    _enablingReadable = true;
                    try { importer.isReadable = true; importer.SaveAndReimport(); }
                    finally { _enablingReadable = false; }
                }
                var target = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                WhimTexDocumentFile.ValidateImportedTexture(path);
                _live = new Live(document, target, path);
                _dirty = false;
                Status = "Live Update on " + path;
                StateChanged?.Invoke();
                return true;
            }
            catch (Exception error)
            {
                RecoverReadable();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Status = "Live Update unavailable: " + error.Message;
                Debug.LogException(error);
                return false;
            }
        }

        public static void Publish() => Publish(null, null);

        internal static void Publish(TextureCompositor document, RenderTexture source)
        {
            if (_live == null || document != null && !IsLiveFor(document)) return;
            try
            {
                _live.Publish(source);
                _dirty = false;
                _lastPublish = EditorApplication.timeSinceStartup;
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Stop("publish failed"); Debug.LogException(error); }
        }

        public static void StopFor(TextureCompositor document, string reason)
        { if (IsLiveFor(document)) Stop(reason); }

        public static void Stop(string reason)
        {
            string path = LivePath;
            try
            {
                StopCore(reason);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (!string.IsNullOrEmpty(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        private static void StopCore(string reason)
        {
            Live previous = _live;
            _live = null;
            try { previous?.Dispose(); }
            finally
            {
                // Restore Read/Write even if restoring GPU pixels failed. Keep the journal
                // on an import failure so a subsequent reload/build can retry recovery.
                try { RecoverReadable(); }
                finally
                {
                    if (previous != null)
                    {
                        Status = "Live Update stopped (" + reason + ").";
                        SceneView.RepaintAll();
                    }
                    StateChanged?.Invoke();
                }
            }
        }

        internal static void PrepareForBuild()
        {
            if (_saving != null || _enablingReadable)
                throw new InvalidOperationException("Wait for the WhimTex document save/import to finish before building.");
            string path = LivePath;
            bool hadLive = _live != null;
            // Unlike UI Stop, failure must propagate: a build must never consume the
            // temporary readable/uncompressed representation. Do not save user edits.
            StopCore("Player build");
            if (EditorPrefs.HasKey(RecoveryKey))
                throw new InvalidOperationException("WhimTex could not restore temporary Read/Write. Resolve the texture import before building.");
            if (hadLive)
            {
                if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not TextureImporter)
                    throw new InvalidOperationException("The live WhimTex texture is missing. Restore or reimport it before building.");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                WhimTexDocumentFile.ValidateImportedTexture(path);
                Debug.Log("WhimTex: Live Update stopped for the build. The saved TIFF is used; unsaved document edits remain in the editor.");
            }
        }

        internal static void BeforeImport(string path, TextureImporter importer)
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (_live != null && string.Equals(_live.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                Live previous = _live;
                _live = null;
                previous.Abandon();
                Status = "Live Update stopped (texture reimported).";
                StateChanged?.Invoke();
            }
            string guid = EditorPrefs.GetString(RecoveryKey, "");
            if (_live == null && _saving == null && !string.IsNullOrEmpty(guid) && AssetDatabase.GUIDToAssetPath(guid) == path && !_enablingReadable)
            {
                importer.isReadable = false;
            }
        }

        internal static void AfterImport(string[] paths)
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            foreach (string path in paths)
            {
                if (_live != null && string.Equals(path, _live.Path, StringComparison.OrdinalIgnoreCase))
                {
                    Live previous = _live;
                    _live = null;
                    previous.Abandon();
                    Status = "Live Update stopped (texture reimported).";
                    StateChanged?.Invoke();
                }
                string recoveryGuid = EditorPrefs.GetString(RecoveryKey, "");
                if (_saving == null && !_enablingReadable && !string.IsNullOrEmpty(recoveryGuid) && AssetDatabase.GUIDToAssetPath(recoveryGuid) == path)
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && !importer.isReadable &&
                        !WhimTexDocumentFile.ImportHasErrors(path) && AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                        EditorPrefs.DeleteKey(RecoveryKey);
                    else if (importer != null && importer.isReadable)
                        EditorApplication.delayCall += RecoverReadable;
                }
            }
        }

        private static void RecoverReadable()
        {
            if (_live != null || _saving != null || _enablingReadable || AssetDatabase.IsAssetImportWorkerProcess()) return;
            string guid = EditorPrefs.GetString(RecoveryKey, "");
            if (string.IsNullOrEmpty(guid)) return;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // The asset may be temporarily missing during an external update. Do not
            // discard recovery information or silently approve a build in that state.
            if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not TextureImporter) return;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.isReadable)
            {
                importer.isReadable = false;
                try
                {
                    importer.SaveAndReimport();
                    WhimTexDocumentFile.ValidateImportedTexture(path);
                    if (AssetImporter.GetAtPath(path) is not TextureImporter restored || restored.isReadable)
                        throw new InvalidOperationException("WhimTex could not restore the texture's Read/Write setting.");
                }
                catch { EditorPrefs.SetString(RecoveryKey, guid); throw; }
            }
            WhimTexDocumentFile.ValidateImportedTexture(path);
            EditorPrefs.DeleteKey(RecoveryKey);
        }

        private static void Tick()
        {
            if (_live == null) return;
            if (_live.Document == null) { Stop("document destroyed"); return; }
            if ((!_dirty && !_live.Continuous) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup - _lastPublish < 1.0 / 30) return;
            Publish();
        }

        private sealed class Live : IDisposable
        {
            public readonly TextureCompositor Document;
            private readonly Texture2D _target;
            private readonly byte[] _backup;
            private readonly GraphicsFormat _format;
            private readonly int _width, _height, _mips;
            private readonly bool _wasDirty;
            private readonly FilterMode _filter;
            private readonly TextureWrapMode _wrapU, _wrapV;
            private RenderTexture _staging;
            private bool _switched;
            private readonly EffectRenderCache _cache = new EffectRenderCache();
            public bool Continuous { get; private set; }
            private readonly string _guid, _path;
            public string Path => string.IsNullOrEmpty(_guid) ? _path : AssetDatabase.GUIDToAssetPath(_guid);

            public Live(TextureCompositor document, Texture2D target, string path)
            {
                if (target == null || !target.isReadable) throw new InvalidOperationException("Live Update needs a readable document image.");
                Document = document;
                _target = target;
                _path = path;
                _guid = AssetDatabase.AssetPathToGUID(path);
                _backup = target.GetRawTextureData();
                _format = target.graphicsFormat;
                _width = target.width;
                _height = target.height;
                _mips = target.mipmapCount;
                if (_mips != 1 && _mips != 1 + (int)Math.Floor(Math.Log(Math.Max(_width, _height), 2)))
                    throw new InvalidOperationException("Live Update requires a complete mip chain; Unity cannot reinitialize a partial chain in place.");
                _wasDirty = EditorUtility.IsDirty(target);
                _filter = target.filterMode;
                _wrapU = target.wrapModeU;
                _wrapV = target.wrapModeV;
                try
                {
                    GraphicsFormat liveFormat = target.format == TextureFormat.BC6H || target.format == TextureFormat.RGBAHalf || target.format == TextureFormat.RGBAFloat
                        ? GraphicsFormat.R16G16B16A16_SFloat
                        : GraphicsFormatUtility.IsSRGBFormat(_format) ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
                    if (!target.Reinitialize(_width, _height, liveFormat, _mips > 1))
                        throw new InvalidOperationException("Unity refused to switch the document image to an uncompressed format.");
                    _switched = true;
                    target.Apply(false, false);
                    _staging = new RenderTexture(new RenderTextureDescriptor(_width, _height)
                    {
                        graphicsFormat = liveFormat, depthBufferBits = 0, msaaSamples = 1,
                        useMipMap = _mips > 1, autoGenerateMips = false
                    }) { name = "WhimTex Live Staging", hideFlags = HideFlags.HideAndDontSave };
                    if (!_staging.Create()) throw new InvalidOperationException("Cannot create the Live Update surface.");
                    Publish(null);
                }
                catch { Dispose(); throw; }
            }

            public void Publish(RenderTexture source)
            {
                if (_target == null || _target.width != _width || _target.height != _height ||
                    _target.graphicsFormat != _staging.graphicsFormat || _target.mipmapCount != _mips)
                {
                    _switched = false;
                    throw new InvalidOperationException("The document image was reimported or destroyed.");
                }
                RenderTexture owned = null;
                RenderTexture previous = RenderTexture.active;
                bool previousSrgb = GL.sRGBWrite;
                try
                {
                    // Reuse the window's GPU preview. API-only changes also stay entirely on the GPU.
                    if (source == null) source = owned = Document.RenderCachedPreview(Math.Max(Document.width, Document.height), _cache, false, null);
                    GL.sRGBWrite = GraphicsFormatUtility.IsSRGBFormat(_staging.graphicsFormat);
                    Graphics.Blit(source, _staging);
                    if (_mips > 1) _staging.GenerateMips();
                    for (int mip = 0; mip < _mips; mip++) Graphics.CopyTexture(_staging, 0, mip, _target, 0, mip);
                    Continuous = HasDynamicInputs(Document.layers);
                    Document.NotifyOutputTextureChanged();
                }
                finally
                {
                    RenderTexture.active = previous;
                    GL.sRGBWrite = previousSrgb;
                    if (owned != null) RenderTexture.ReleaseTemporary(owned);
                }
            }

            public void Abandon() { _switched = false; Dispose(); }

            private static bool HasDynamicInputs(System.Collections.Generic.List<Layer> layers)
            {
                if (layers == null) return false;
                foreach (Layer layer in layers)
                {
                    if (layer == null) continue;
                    if (layer.Behaviour is FileLayerBehaviour || layer.Behaviour is ShaderProcessorLayerBehaviour) return true;
                    if (layer.modifiers != null)
                        foreach (var modifier in layer.modifiers) if (modifier != null) return true;
                    if (HasDynamicInputs(layer.children)) return true;
                }
                return false;
            }

            public void Dispose()
            {
                try
                {
                    if (_switched && _target != null)
                    {
                        if (!_target.Reinitialize(_width, _height, _format, _mips > 1))
                            throw new InvalidOperationException("Cannot restore the imported texture format.");
                        _target.LoadRawTextureData(_backup);
                        _target.Apply(false, false);
                        _target.filterMode = _filter;
                        _target.wrapModeU = _wrapU;
                        _target.wrapModeV = _wrapV;
                        if (!_wasDirty) EditorUtility.ClearDirty(_target);
                        _switched = false;
                    }
                }
                finally
                {
                    _cache.Dispose();
                    if (_staging != null) { _staging.Release(); UnityEngine.Object.DestroyImmediate(_staging); _staging = null; }
                    if (Document != null) Document.NotifyOutputTextureChanged();
                }
            }
        }
    }

    internal sealed class WhimTexDocumentSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            string livePath = WhimTexDocumentSession.LivePath;
            if (!string.IsNullOrEmpty(livePath))
                foreach (string path in paths)
                    if (string.Equals(path, livePath, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(path, livePath + ".meta", StringComparison.OrdinalIgnoreCase))
                    { WhimTexDocumentSession.Stop("document asset save"); break; }
            return paths;
        }
    }
}
