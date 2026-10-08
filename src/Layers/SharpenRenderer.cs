using System;
using UnityEngine;
using static DCFApixels.WhimTex.SharpenLayerBehaviour;

namespace DCFApixels.WhimTex
{
    internal static class SharpenRenderer
    {
        internal static RenderTexture RenderSharpen(SharpenLayerBehaviour layer, in LayerRenderContext context)
        {
            if (context.input == null) return null;
            float strength = Safe(layer.strength, 0f, MaximumStrength, 1f);
            float radius = Safe(layer.radius, 0f, MaximumRadius, 1f) / context.scaleMultiplier;
            if (strength <= 0f || radius <= .0001f)
                return layer.ApplyTransformAndFx(context.input, context);

            Material material = WhimTexMaterials.Sharpen;
            Material blurMaterial = WhimTexMaterials.GaussianBlur;
            if (material == null || blurMaterial == null) throw new InvalidOperationException("Sharpen shaders are unavailable.");
            RenderTexture result = null, premultiplied = null, horizontal = null, blurred = null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                material.SetFloat("_Strength", strength);
                material.SetFloat("_Radius", radius);
                material.SetFloat("_Threshold", Mathf.Clamp01(layer.threshold));
                material.SetFloat("_NoiseReduction", Mathf.Clamp01(layer.noiseReduction));
                material.SetFloat("_HaloSuppression", Mathf.Clamp01(layer.haloSuppression));
                material.SetInt("_Algorithm", (int)layer.algorithm);
                material.SetInt("_ChannelMode", (int)layer.channelMode);
                material.SetInt("_Edges", (int)layer.edges);
                // During painting use the same low-cost approximation as the
                // preview path. The settled render switches to a wider,
                // Gaussian-weighted neighbourhood in the shader.
                material.SetInt("_Fast", context.activeDocument.InteractiveEffects ? 1 : 0);
                if (context.activeDocument.InteractiveEffects)
                {
                    result = Allocate(context.width, context.height);
                    material.SetInt("_UseBlur", 0);
                    Graphics.Blit(context.input, result, material, 0);
                }
                else
                {
                    // Reuse the production Gaussian blur passes instead of a
                    // sparse fixed sharpening kernel. This removes visible
                    // sample shapes at the cost of two additional full-size
                    // blur passes only after the gesture settles.
                    blurMaterial.SetInt("_Edges", (int)layer.edges);
                    premultiplied = Allocate(context.width, context.height);
                    Graphics.Blit(context.input, premultiplied, blurMaterial, 0);
                    horizontal = Allocate(context.width, context.height);
                    blurred = Allocate(context.width, context.height);
                    GaussianKernel.Set(blurMaterial, radius / 3f, radius);
                    blurMaterial.SetVector("_Direction", new Vector4(1f / context.width, 0f, 0f, 0f));
                    Graphics.Blit(premultiplied, horizontal, blurMaterial, 2);
                    blurMaterial.SetVector("_Direction", new Vector4(0f, 1f / context.height, 0f, 0f));
                    Graphics.Blit(horizontal, blurred, blurMaterial, 2);
                    material.SetInt("_UseBlur", 1);
                    material.SetTexture("_BlurTex", blurred);
                    result = Allocate(context.width, context.height);
                    Graphics.Blit(context.input, result, material, 0);
                }
                return layer.ApplyTransformAndFx(result, context);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                material.SetTexture("_BlurTex", null);
                material.SetInt("_UseBlur", 0);
                if (result != null) RenderTexture.ReleaseTemporary(result);
                if (premultiplied != null) RenderTexture.ReleaseTemporary(premultiplied);
                if (horizontal != null) RenderTexture.ReleaseTemporary(horizontal);
                if (blurred != null) RenderTexture.ReleaseTemporary(blurred);
            }
        }


        private static RenderTexture Allocate(int width, int height)
        {
            var texture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static float Safe(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
