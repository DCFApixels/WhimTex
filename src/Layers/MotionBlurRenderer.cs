using System;
using UnityEngine;
using static DCFApixels.WhimTex.BlurLayerBehaviour;

namespace DCFApixels.WhimTex
{
    internal static class MotionBlurRenderer
    {
        internal const int FullSampleLimit = 1024;
        internal const int InteractiveSampleLimit = 32;

        internal static float Limit(float value, float min, float max, float fallback = 0f) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        internal static RenderTexture RenderBlur(BlurLayerBehaviour layer, in LayerRenderContext context)
        {
            if (context.input == null) return null;
            float strength = layer.strength, distance = layer.distance, angle = layer.angle, arc = layer.arc;
            Vector2 center = layer.center;
            MotionDirection direction = layer.direction;
            EdgeMode edges = layer.edges;
            float amount = Limit(strength, 0f, MaximumStrength, 1f);
            bool circular = layer.mode == BlurType.Circular;
            float pixels = Limit(distance, 0f, MaximumDistance) / context.scaleMultiplier;
            float radians = Limit(arc, 0f, 360f) * Mathf.Deg2Rad;
            if (amount == 0f || (circular ? radians : pixels) <= .0001f)
                return layer.ApplyTransformAndModifiers(context.input, context);

            Material material = WhimTexMaterials.MotionBlur;
            if (material == null) throw new InvalidOperationException("Motion Blur shader is unavailable.");
            var pivot = new Vector2(Limit(center.x, 0f, 1f, .5f), Limit(center.y, 0f, 1f, .5f));
            float orientation = Limit(angle, -180f, 180f) * Mathf.Deg2Rad;
            bool interactive = context.compositor.InteractiveEffects;
            RenderTexture current = null, blurred = null, straight = null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                material.SetInt("_Edges", (int)edges);
                current = Allocate(context.width, context.height);
                Graphics.Blit(context.input, current, material, 0);

                float reach = circular ? radians * new Vector2(
                    Mathf.Max(pivot.x, 1f - pivot.x) * context.width,
                    Mathf.Max(pivot.y, 1f - pivot.y) * context.height).magnitude : pixels;
                int reduction = 1;
                while (interactive && reach / reduction > 32f && reduction < 4 &&
                    current.width >= 8 && current.height >= 8)
                {
                    var smaller = Allocate((current.width + 1) / 2, (current.height + 1) / 2);
                    try { Graphics.Blit(current, smaller, material, 1); }
                    catch { RenderTexture.ReleaseTemporary(smaller); throw; }
                    RenderTexture.ReleaseTemporary(current);
                    current = smaller;
                    reduction *= 2;
                }

                material.SetVector("_CanvasSize", new Vector4(context.width, context.height, 0f, 0f));
                material.SetVector("_Motion", new Vector4(Mathf.Cos(orientation) * pixels,
                    Mathf.Sin(orientation) * pixels, 0f, 0f));
                material.SetVector("_Center", new Vector4(pivot.x, pivot.y, 0f, 0f));
                material.SetFloat("_Arc", radians);
                material.SetFloat("_Bias", direction == MotionDirection.Forward ? .5f :
                    direction == MotionDirection.Backward ? -.5f : 0f);
                material.SetInt("_SampleLimit", interactive ? InteractiveSampleLimit : FullSampleLimit);
                blurred = Allocate(current.width, current.height);
                Graphics.Blit(current, blurred, material, circular ? 3 : 2);
                RenderTexture.ReleaseTemporary(current);
                current = null;
                straight = Allocate(context.width, context.height);
                material.SetFloat("_Strength", amount);
                material.SetTexture("_SourceTex", amount < 1f ? context.input : null);
                Graphics.Blit(blurred, straight, material, 4);
                RenderTexture.ReleaseTemporary(blurred);
                blurred = null;
                return layer.ApplyTransformAndModifiers(straight, context);
            }
            finally
            {
                material.SetTexture("_SourceTex", null);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (current != null) RenderTexture.ReleaseTemporary(current);
                if (blurred != null) RenderTexture.ReleaseTemporary(blurred);
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
