using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        [NonSerialized] private RenderTexture numericErrors;
        [NonSerialized] private bool collectingErrors;
        [NonSerialized] private int diagnosticRevision;
        [NonSerialized] private bool hasNumericErrors;
        internal Texture NumericErrorMask => numericErrors;
        internal bool HasNumericErrors => hasNumericErrors;

        private void BeginDiagnostics(int w, int h)
        {
            ReleaseDiagnostics();
            numericErrors = ErrorTarget(w, h);
            var previous = RenderTexture.active;
            RenderTexture.active = numericErrors;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
            collectingErrors = true;
        }

        private static RenderTexture ErrorTarget(int w, int h)
        {
            var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Point;
            target.wrapMode = TextureWrapMode.Clamp;
            return target;
        }

        // Inspect actual render-stage pixels before saturation or nonfinite display sanitization.
        internal RenderTexture FinishStage(RenderTexture raw, bool saturate = false, LayerSwizzle swizzle = default)
        {
            if (raw == null) return null;
            Material material = WhimTexMaterials.Hdr;
            RenderTexture result = null;
            try
            {
                material.SetVector("_Swizzle", swizzle.ShaderValue);
                material.SetFloat("_UseSwizzle", swizzle.IsIdentity ? 0f : 1f);
                if (collectingErrors && numericErrors != null)
                {
                    var next = ErrorTarget(numericErrors.width, numericErrors.height);
                    try { material.SetTexture("_Errors", numericErrors); Graphics.Blit(raw, next, material, 1); }
                    catch { RenderTexture.ReleaseTemporary(next); throw; }
                    RenderTexture.ReleaseTemporary(numericErrors);
                    numericErrors = next;
                }
                result = HdrUtility.Temporary(raw.width, raw.height);
                result.filterMode = raw.filterMode;
                material.SetFloat("_Saturate", saturate ? 1f : 0f);
                material.SetFloat("_Encode", 0f);
                Graphics.Blit(raw, result, material, 0);
                RenderTexture.ReleaseTemporary(raw);
                return result;
            }
            catch { if (result != null) RenderTexture.ReleaseTemporary(result); throw; }
        }

        private void EndDiagnostics()
        {
            collectingErrors = false;
            if (numericErrors == null || !SystemInfo.supportsAsyncGPUReadback) return;
            RenderTexture reduced = null;
            try
            {
                Texture source = numericErrors;
                do
                {
                    var next = ErrorTarget(Mathf.Max(1, (source.width + 1) / 2), Mathf.Max(1, (source.height + 1) / 2));
                    Graphics.Blit(source, next, WhimTexMaterials.Hdr, 2);
                    if (reduced != null) RenderTexture.ReleaseTemporary(reduced);
                    source = reduced = next;
                } while (reduced.width > 1 || reduced.height > 1);
                int revision = diagnosticRevision;
                RenderTexture pending = reduced;
                AsyncGPUReadback.Request(pending, 0, TextureFormat.RGBA32, request =>
                {
                    try
                    {
                        if (this != null && revision == diagnosticRevision && !request.hasError)
                            hasNumericErrors = request.GetData<Color32>()[0].r != 0;
                    }
                    finally { RenderTexture.ReleaseTemporary(pending); }
                });
                reduced = null;
            }
            finally { if (reduced != null) RenderTexture.ReleaseTemporary(reduced); }
        }

        private void ReleaseDiagnostics()
        {
            diagnosticRevision++;
            collectingErrors = false;
            hasNumericErrors = false;
            if (numericErrors != null) RenderTexture.ReleaseTemporary(numericErrors);
            numericErrors = null;
        }
    }
}
