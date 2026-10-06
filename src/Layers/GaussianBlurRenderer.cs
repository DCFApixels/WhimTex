using System;
using UnityEngine;
using static DCFApixels.WhimTex.BlurLayerBehaviour;

namespace DCFApixels.WhimTex
{
    internal static class GaussianBlurRenderer
    {
        internal static RenderTexture RenderBlur(BlurLayerBehaviour layer, in LayerRenderContext context)
        {
            if (context.input == null) return null;
            float strength = layer.strength, radius = layer.radius;
            EdgeMode edges = layer.edges;
            float amount = float.IsNaN(strength) || float.IsInfinity(strength) ? 1f : Mathf.Clamp(strength, 0f, MaximumStrength);
            float pixels = Mathf.Clamp(float.IsNaN(radius) ? 0f : radius, 0f, MaximumRadius) / context.scaleMultiplier;
            if (amount == 0f || pixels <= .0001f) return layer.ApplyTransformAndFx(context.input, context);
            Material material = WhimTexMaterials.GaussianBlur;
            if (material == null) throw new InvalidOperationException("Gaussian Blur shader is unavailable.");
            RenderTexture current = null, scratch = null, straight = null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                material.SetInt("_Edges", (int)edges);
                current = Allocate(context.width, context.height);
                Graphics.Blit(context.input, current, material, 0);
                int reduction = 1;
                if (context.compositor.InteractiveEffects)
                    while (pixels / reduction > 24f && reduction < 16 &&
                        context.width / reduction >= 4 && context.height / reduction >= 4)
                        reduction *= 2;
                for (int step = 1; step < reduction; step *= 2)
                {
                    var smaller = Allocate(Mathf.Max(1, (current.width + 1) / 2), Mathf.Max(1, (current.height + 1) / 2));
                    try { Graphics.Blit(current, smaller, material, 1); }
                    catch { RenderTexture.ReleaseTemporary(smaller); throw; }
                    RenderTexture.ReleaseTemporary(current);
                    current = smaller;
                }
                float sigma = Mathf.Max(pixels / 3f, 1f / 3f);
                // Account approximately for the variance introduced by down/up sampling.
                float variance = reduction == 1 ? 0f : (3f * reduction * reduction - 1f) / 12f;
                sigma = Mathf.Sqrt(Mathf.Max(.01f, sigma * sigma - variance));
                scratch = Allocate(current.width, current.height);
                GaussianKernel.Set(material, sigma * current.width / context.width, pixels * current.width / context.width);
                material.SetVector("_Direction", new Vector4(1f / current.width, 0f, 0f, 0f));
                Graphics.Blit(current, scratch, material, 2);
                GaussianKernel.Set(material, sigma * current.height / context.height, pixels * current.height / context.height);
                material.SetVector("_Direction", new Vector4(0f, 1f / current.height, 0f, 0f));
                Graphics.Blit(scratch, current, material, 2);
                straight = Allocate(context.width, context.height);
                material.SetFloat("_Strength", amount);
                material.SetTexture("_SourceTex", amount < 1f ? context.input : null);
                Graphics.Blit(current, straight, material, 3);
                return layer.ApplyTransformAndFx(straight, context);
            }
            finally
            {
                material.SetTexture("_SourceTex", null);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (current != null) RenderTexture.ReleaseTemporary(current);
                if (scratch != null) RenderTexture.ReleaseTemporary(scratch);
                if (straight != null) RenderTexture.ReleaseTemporary(straight);
            }
        }


        private static RenderTexture Allocate(int width, int height)
        {
            var texture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}
