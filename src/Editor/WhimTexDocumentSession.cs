using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Editor-side owner of a document's file link and of the Live Update session.
    ///
    /// Live Update works on the carrier image exactly like the mechanism proven in
    /// Assets/TextureLiveUpdateLab: the imported texture is switched to an uncompressed GPU format
    /// in place (so materials keep the same object), the composition is uploaded with GPU-only
    /// copies, and the imported format plus the original pixels are restored when the session ends.
    /// The session must not survive a domain reload, a play mode transition or a project save.
    /// </summary>
    [InitializeOnLoad]
    internal static class WhimTexDocumentSession
    {
        public static string Status { get; private set; } = "No document session.";
        public static bool IsLive => _live != null;
        public static string LivePath => _live == null ? null : _live.path;

        private static Live _live;
        private static double _lastPublish;

        static WhimTexDocumentSession()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => Stop("domain reload");
            EditorApplication.quitting += () => Stop("editor quit");
            EditorApplication.playModeStateChanged += _ => Stop("play mode transition");
            EditorApplication.update += Tick;
        }

        /// <summary>Stops the session first: a reimport during Live Update would reset the uncompressed surface anyway.</summary>
        public static bool Save(TextureCompositor document, string path)
        {
            bool wasLive = _live != null;
            Stop("document save");
            try
            {
                string written = WhimTexDocumentFile.Save(document, path);
                Status = "Saved " + written;
                if (wasLive && !Start(document, written)) return false;
                return true;
            }
            catch (Exception error)
            {
                Status = "Save failed: " + error.Message;
                Debug.LogException(error);
                return false;
            }
        }

        public static bool Start(TextureCompositor document, string path)
        {
            if (document == null)
            {
                Status = "There is no document to edit live.";
                return false;
            }
            Stop("restart");
            var target = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (target == null)
            {
                Status = "The document image is missing: " + path;
                return false;
            }
            try
            {
                _live = new Live(document, target, path);
                Status = "Live Update on " + path;
                return true;
            }
            catch (Exception error)
            {
                _live = null;
                Status = "Live Update unavailable: " + error.Message;
                Debug.LogException(error);
                return false;
            }
        }

        public static void Publish()
        {
            if (_live == null) return;
            try { _live.Publish(); }
            catch (Exception error)
            {
                Stop("publish failed");
                Debug.LogException(error);
            }
        }

        public static void Stop(string reason)
        {
            if (_live == null) return;
            try
            {
                _live.Dispose();
                Status = "Live Update stopped (" + reason + ").";
            }
            catch (Exception error) { Debug.LogException(error); }
            finally { _live = null; }
            SceneView.RepaintAll();
        }

        private static void Tick()
        {
            if (_live == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastPublish < 1.0 / 30) return;
            _lastPublish = now;
            Publish();
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private sealed class Live : IDisposable
        {
            private readonly TextureCompositor _document;
            private readonly Texture2D _target;
            private readonly byte[] _backup;
            private readonly TextureFormat _format;
            private readonly int _size;
            private readonly int _mips;
            private readonly EntityId _entity;
            private readonly bool _wasDirty;
            private readonly FilterMode _filter;
            private readonly TextureWrapMode _wrapU;
            private readonly TextureWrapMode _wrapV;
            private RenderTexture _staging;
            private bool _switched;

            public readonly string path;

            public Live(TextureCompositor document, Texture2D target, string path)
            {
                if (!target.isReadable)
                    throw new InvalidOperationException("Live Update needs Read/Write enabled on the document image.");
                _document = document;
                _target = target;
                this.path = path;
                _backup = target.GetRawTextureData();
                _format = target.format;
                _size = target.width;
                _mips = target.mipmapCount;
                _entity = target.GetEntityId();
                _wasDirty = EditorUtility.IsDirty(target);
                _filter = target.filterMode;
                _wrapU = target.wrapModeU;
                _wrapV = target.wrapModeV;
                GraphicsFormat liveFormat = LiveFormat(target, _format);
                if (!target.Reinitialize(_size, _size, liveFormat, _mips > 1))
                    throw new InvalidOperationException("Unity refused to switch the document image to an uncompressed format.");
                _switched = true;
                // The new GPU resource must exist before any GPU-only write; applying afterwards
                // would instead push stale CPU pixels over the live image.
                target.Apply(false, false);
                _staging = new RenderTexture(new RenderTextureDescriptor(_size, _size)
                {
                    graphicsFormat = liveFormat, depthBufferBits = 0, msaaSamples = 1,
                    useMipMap = _mips > 1, autoGenerateMips = false
                })
                { name = "WhimTex Live Staging", hideFlags = HideFlags.HideAndDontSave };
                _staging.Create();
                // Publish immediately: after Reinitialize the surface holds undefined data until the
                // first GPU copy, so a session must never be left with an unpublished surface.
                Publish();
            }

            private static GraphicsFormat LiveFormat(Texture2D target, TextureFormat format)
            {
                if (format == TextureFormat.BC6H || format == TextureFormat.RGBAHalf || format == TextureFormat.RGBAFloat)
                    return GraphicsFormat.R16G16B16A16_SFloat;
                return GraphicsFormatUtility.IsSRGBFormat(target.graphicsFormat)
                    ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            }

            public void Publish()
            {
                if (_target == null || _target.GetEntityId() != _entity)
                    throw new InvalidOperationException("The document image was replaced or destroyed.");
                Texture2D composite = null;
                RenderTexture previous = RenderTexture.active;
                bool previousSrgb = GL.sRGBWrite;
                try
                {
                    composite = _document.Compose();
                    if (composite == null) return;
                    GL.sRGBWrite = GraphicsFormatUtility.IsSRGBFormat(_staging.graphicsFormat);
                    Graphics.Blit(composite, _staging);
                    if (_mips > 1) _staging.GenerateMips();
                    for (int mip = 0; mip < _mips; mip++) Graphics.CopyTexture(_staging, 0, mip, _target, 0, mip);
                }
                finally
                {
                    RenderTexture.active = previous;
                    GL.sRGBWrite = previousSrgb;
                    if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
                }
            }

            public void Dispose()
            {
                try
                {
                    if (_switched && _target != null)
                    {
                        if (!_target.Reinitialize(_size, _size, _format, _mips > 1))
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
                    if (_staging != null)
                    {
                        _staging.Release();
                        UnityEngine.Object.DestroyImmediate(_staging);
                        _staging = null;
                    }
                }
            }
        }
    }

    /// <summary>A project save must never capture the temporary uncompressed state of a live document.</summary>
    internal sealed class WhimTexDocumentSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            WhimTexDocumentSession.Stop("project save");
            return paths;
        }
    }
}
