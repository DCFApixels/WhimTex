using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    // Reduces exact positive-alpha pixel edges on the GPU; only sixteen bytes are read back.
    internal sealed class WhimTexAlphaBounds : IDisposable
    {
        private static readonly int SourceSize = Shader.PropertyToID("_SourceSize");
        private static readonly int SourceIsBounds = Shader.PropertyToID("_SourceIsBounds");
        private Material material;
        private Texture2D readbackPixel;
        private Pending pending;
        private bool updating, disposed;
        internal bool isPending => pending != null;

        // A null result is unavailable; default(RectInt) is a known empty source.
        internal bool Begin(RenderTexture source, Action<RectInt?> completed, bool synchronous = false)
        {
            if (disposed || pending != null) return false;
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            var operation = new Pending { completed = completed, width = source != null ? source.width : 0,
                height = source != null ? source.height : 0 };
            pending = operation;
            bool asynchronous = false;
            RectInt? result = null;
            try
            {
                if (source != null && source.IsCreated() && source.width > 0 && source.height > 0 &&
                    source.dimension == TextureDimension.Tex2D && source.antiAliasing == 1 && !source.bindTextureMS &&
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) && EnsureMaterial())
                {
                    operation.pixel = Reduce(source);
                    if (!synchronous && SystemInfo.supportsAsyncGPUReadback)
                    {
                        operation.request = AsyncGPUReadback.Request(operation.pixel, 0, TextureFormat.RGBAFloat,
                            request => ReadbackCompleted(operation, request));
                        operation.requestIssued = true;
                        operation.deadline = EditorApplication.timeSinceStartup + .25d;
                        operation.request.forcePlayerLoopUpdate = true;
                        if (!operation.finished)
                        {
                            EditorApplication.update += Update;
                            updating = true;
                        }
                        asynchronous = true;
                    }
                    else result = ReadSynchronously(operation);
                }
            }
            catch (Exception) { result = null; }
            if (!asynchronous) Finish(operation, result);
            return true;
        }

        internal void Cancel() { if (pending != null) pending.cancelled = true; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (pending is Pending operation)
            {
                operation.cancelled = true;
                if (operation.requestIssued && !operation.finished) operation.request.WaitForCompletion();
                Finish(operation, null);
            }
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (readbackPixel != null) UnityEngine.Object.DestroyImmediate(readbackPixel);
            material = null; readbackPixel = null;
        }

        private bool EnsureMaterial()
        {
            if (material != null) return material.shader != null && material.shader.isSupported;
            Shader shader = Shader.Find("Hidden/WhimTex/AlphaBoundsReduce");
            if (shader == null || !shader.isSupported) return false;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private RenderTexture Reduce(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active, reduced = null;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                Texture input = source;
                bool first = true;
                do
                {
                    RenderTexture next = RenderTexture.GetTemporary((input.width + 1) / 2,
                        (input.height + 1) / 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    next.filterMode = FilterMode.Point; next.wrapMode = TextureWrapMode.Clamp;
                    try
                    {
                        material.SetVector(SourceSize, new Vector4(input.width, input.height, 0f, 0f));
                        material.SetFloat(SourceIsBounds, first ? 0f : 1f);
                        Graphics.Blit(input, next, material, 0);
                    }
                    catch { RenderTexture.ReleaseTemporary(next); throw; }
                    if (reduced != null) RenderTexture.ReleaseTemporary(reduced);
                    input = reduced = next;
                    first = false;
                } while (reduced.width > 1 || reduced.height > 1);
                RenderTexture result = reduced;
                reduced = null;
                return result;
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
                if (reduced != null) RenderTexture.ReleaseTemporary(reduced);
            }
        }

        private RectInt? ReadSynchronously(Pending operation)
        {
            if (readbackPixel == null)
                readbackPixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = operation.pixel;
                readbackPixel.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, false);
                return Decode(readbackPixel.GetRawTextureData<Color>()[0], operation.width, operation.height);
            }
            finally { RenderTexture.active = previous; }
        }

        internal static RectInt? Decode(Color edges, int width, int height)
        {
            if (float.IsInfinity(edges.r) || float.IsInfinity(edges.g) || float.IsInfinity(edges.b) ||
                float.IsInfinity(edges.a) || float.IsNaN(edges.r) || float.IsNaN(edges.g) ||
                float.IsNaN(edges.b) || float.IsNaN(edges.a)) return null;
            if (!(edges.b > edges.r) || !(edges.a > edges.g)) return default(RectInt);
            int x0 = Mathf.RoundToInt(edges.r), y0 = Mathf.RoundToInt(edges.g),
                x1 = Mathf.RoundToInt(edges.b), y1 = Mathf.RoundToInt(edges.a);
            if (x0 < 0 || y0 < 0 || x1 > width || y1 > height || x0 >= x1 || y0 >= y1) return null;
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        private void ReadbackCompleted(Pending operation, AsyncGPUReadbackRequest request)
        {
            if (operation.finished) return;
            RectInt? result = null;
            try
            {
                if (!request.hasError)
                {
                    var data = request.GetData<Color>();
                    if (data.Length == 1) result = Decode(data[0], operation.width, operation.height);
                }
            }
            catch (Exception) { result = null; }
            Finish(operation, result);
        }

        private void Update()
        {
            Pending operation = pending;
            if (operation == null || operation.finished || !operation.requestIssued) return;
            try
            {
                operation.request.Update();
                if (operation.finished) return;
                if (operation.request.done) ReadbackCompleted(operation, operation.request);
                else if (EditorApplication.timeSinceStartup >= operation.deadline)
                {
                    operation.request.WaitForCompletion();
                    if (!operation.finished) ReadbackCompleted(operation, operation.request);
                }
            }
            catch (Exception) { Finish(operation, null); }
        }

        private void Finish(Pending operation, RectInt? result)
        {
            if (operation.finished) return;
            operation.finished = true;
            if (updating) { EditorApplication.update -= Update; updating = false; }
            if (operation.pixel != null) RenderTexture.ReleaseTemporary(operation.pixel);
            operation.pixel = null;
            if (ReferenceEquals(pending, operation)) pending = null;
            Action<RectInt?> completed = operation.completed;
            operation.completed = null;
            if (!operation.cancelled && !disposed) completed?.Invoke(result);
        }

        private sealed class Pending
        {
            internal RenderTexture pixel;
            internal AsyncGPUReadbackRequest request;
            internal Action<RectInt?> completed;
            internal int width, height;
            internal double deadline;
            internal bool requestIssued, cancelled, finished;
        }
    }
}
