using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    /// <summary>Tests a borrowed, full-resolution layer output on the main thread.</summary>
    internal sealed class WhimTexAlphaPresence : IDisposable
    {
        private static readonly int SourceSize = Shader.PropertyToID("_SourceSize");
        private static readonly int SourceIsPresence = Shader.PropertyToID("_SourceIsPresence");
        private Material material;
        private Texture2D readbackPixel;
        private Pending pending;
        private bool disposed;
        private bool updating;

        internal bool isPending => pending != null;

        /// <summary>
        /// True means every alpha is exactly zero, false means at least one alpha is nonzero,
        /// and null means the result is unknown. Completion can be synchronous. The source is
        /// borrowed only during this call; only the owned final pixel survives until readback.
        /// Returns false when this helper is busy or disposed.
        /// </summary>
        internal bool Begin(RenderTexture source, Action<bool?> completed)
        {
            if (disposed || pending != null) return false;
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            var operation = new Pending { completed = completed };
            pending = operation;
            bool asynchronous = false;
            bool? result = null;
            try
            {
                if (UsableSource(source) && EnsureMaterial() &&
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32))
                {
                    operation.pixel = Reduce(source);
                    if (SystemInfo.supportsAsyncGPUReadback)
                    {
                        operation.request = AsyncGPUReadback.Request(operation.pixel, 0, TextureFormat.RGBA32,
                            request => ReadbackCompleted(operation, request));
                        operation.requestIssued = true;
                        operation.request.forcePlayerLoopUpdate = true;
                        // Submit queued work even when no view is repainting; do not wait for the GPU.
                        GL.Flush();
                        if (!operation.finished)
                        {
                            EditorApplication.update += Update;
                            updating = true;
                        }
                        asynchronous = true;
                    }
                    else result = ReadSynchronously(operation.pixel);
                }
            }
            catch (Exception) { result = null; }
            if (!asynchronous) Finish(operation, result);
            return true;
        }

        /// <summary>Suppresses completion; an in-flight readback keeps its pixel until it finishes.</summary>
        internal void Cancel()
        {
            if (pending != null) pending.cancelled = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var operation = pending;
            if (operation != null)
            {
                operation.cancelled = true;
                // Wait only for this helper's single-pixel request so domain reload/close can
                // release its texture safely even when future callbacks will no longer run.
                if (operation.requestIssued && !operation.finished)
                    operation.request.WaitForCompletion();
                Finish(operation, null);
            }
            DestroyResources();
        }

        private bool EnsureMaterial()
        {
            if (material != null) return material.shader != null && material.shader.isSupported;
            Shader shader = Shader.Find("Hidden/WhimTex/AlphaPresenceReduce");
            if (shader == null || !shader.isSupported) return false;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private static bool UsableSource(RenderTexture source)
        {
            return source != null && source.IsCreated() && source.width > 0 && source.height > 0 &&
                source.dimension == TextureDimension.Tex2D && source.antiAliasing == 1 && !source.bindTextureMS;
        }

        private RenderTexture Reduce(RenderTexture source)
        {
            RenderTexture previousActive = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture reduced = null;
            try
            {
                GL.sRGBWrite = false;
                Texture input = source;
                bool firstPass = true;
                do
                {
                    RenderTexture next = ProfiledRenderTexture.GetTemporary((input.width + 1) / 2,
                        (input.height + 1) / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    next.filterMode = FilterMode.Point;
                    next.wrapMode = TextureWrapMode.Clamp;
                    try
                    {
                        material.SetVector(SourceSize, new Vector4(input.width, input.height, 0f, 0f));
                        material.SetFloat(SourceIsPresence, firstPass ? 0f : 1f);
                        Graphics.Blit(input, next, material, 0);
                    }
                    catch { ProfiledRenderTexture.ReleaseTemporary(next); throw; }
                    if (reduced != null) ProfiledRenderTexture.ReleaseTemporary(reduced);
                    input = reduced = next;
                    firstPass = false;
                } while (reduced.width > 1 || reduced.height > 1);
                RenderTexture result = reduced;
                reduced = null;
                return result;
            }
            finally
            {
                RenderTexture.active = previousActive;
                GL.sRGBWrite = previousSrgb;
                if (reduced != null) ProfiledRenderTexture.ReleaseTemporary(reduced);
            }
        }

        private bool ReadSynchronously(RenderTexture pixel)
        {
            if (readbackPixel == null)
                readbackPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = pixel;
                WhimTexProfiler.ReadPixels(readbackPixel, new Rect(0f, 0f, 1f, 1f), 0, 0, false);
                return readbackPixel.GetRawTextureData<Color32>()[0].r == 0;
            }
            finally { RenderTexture.active = previousActive; }
        }

        private void ReadbackCompleted(Pending operation, AsyncGPUReadbackRequest request)
        {
            if (operation.finished) return;
            bool? result = null;
            try
            {
                if (!request.hasError)
                {
                    var data = request.GetData<Color32>();
                    if (data.Length == 1) result = data[0].r == 0;
                }
            }
            catch (Exception) { result = null; }
            Finish(operation, result);
        }

        private void Update()
        {
            var operation = pending;
            if (operation == null || operation.finished || !operation.requestIssued) return;
            try
            {
                operation.request.Update();
                if (operation.finished) return;
                if (operation.request.done) ReadbackCompleted(operation, operation.request);
            }
            catch (Exception) { Finish(operation, null); }
        }

        private void Finish(Pending operation, bool? result)
        {
            if (operation.finished) return;
            operation.finished = true;
            if (updating)
            {
                EditorApplication.update -= Update;
                updating = false;
            }
            if (operation.pixel != null) ProfiledRenderTexture.ReleaseTemporary(operation.pixel);
            operation.pixel = null;
            if (ReferenceEquals(pending, operation)) pending = null;
            Action<bool?> completed = operation.completed;
            operation.completed = null;
            if (!operation.cancelled && !disposed) completed?.Invoke(result);
        }

        private void DestroyResources()
        {
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (readbackPixel != null) UnityEngine.Object.DestroyImmediate(readbackPixel);
            material = null;
            readbackPixel = null;
        }

        private sealed class Pending
        {
            internal RenderTexture pixel;
            internal AsyncGPUReadbackRequest request;
            internal Action<bool?> completed;
            internal bool requestIssued;
            internal bool cancelled;
            internal bool finished;
        }
    }
}
