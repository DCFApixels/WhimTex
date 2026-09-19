using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "NormalMapLayerBehaviour")]
    [Serializable]
    public sealed partial class NormalMapLayerBehaviour : TargetedLayerBehaviour
    {
        public enum GenerationMode { HeightMap, Texture }
        public enum HeightChannel { Luminance, Red, Green, Blue, Alpha, Maximum }
        public enum InputSpace { ColorValues, Linear }
        public enum EdgeMode { Clamp, Repeat, Mirror }
        public enum DerivativeFilter { Sobel, Scharr, CentralDifference }
        public enum AlphaMode { Opaque, Source }
        public enum OutputMode { Normal, Height }
        public enum OutputEncoding { PackedColor, LinearData }

        public GenerationMode mode;
        public HeightChannel sourceChannel;
        public InputSpace inputSpace;
        public EdgeMode edges;
        public DerivativeFilter derivative;
        public AlphaMode alphaMode;
        public OutputMode output;
        public OutputEncoding encoding;
        public float strength = 4f;
        public float blackLevel;
        public float whiteLevel = 1f;
        public float gamma = 1f;
        public float smoothing = 1f;
        public float mediumRadius = 4f;
        public float largeRadius = 32f;
        public float fineDetail = 1f;
        public float mediumDetail = 1f;
        public float largeDetail = .5f;
        public float lightRemoval = .75f;
        public bool inverted;
        public bool flipX;
        public bool flipY;
        public bool ignoreTransparent = true;

        public override string ToString() => "Normal Map";
        internal override bool RequiresColorInput => true;

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            if (context.input == null) return null;
            Material material = WhimTexMaterials.NormalMap;
            if (material == null) throw new InvalidOperationException("Normal Map shader is unavailable.");
            int width = context.width, height = context.height;
            float scale = context.scaleMultiplier;
            var buffers = new List<RenderTexture>(6);
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                RenderTexture Allocate(bool color = false)
                {
                    RenderTextureFormat format = !color && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat)
                        ? RenderTextureFormat.RGFloat : RenderTextureFormat.ARGBFloat;
                    var buffer = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
                    buffer.filterMode = FilterMode.Bilinear;
                    buffer.wrapMode = edges == EdgeMode.Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                    buffers.Add(buffer);
                    return buffer;
                }
                material.SetFloat("_Channel", (int)sourceChannel);
                material.SetFloat("_InputSpace", (int)inputSpace);
                material.SetFloat("_Edges", (int)edges);
                material.SetFloat("_IgnoreTransparent", ignoreTransparent ? 1f : 0f);
                material.SetVector("_Levels", new Vector4(blackLevel, Mathf.Max(whiteLevel, blackLevel + .0001f),
                    Mathf.Clamp(gamma, .05f, 8f), inverted ? 1f : 0f));
                material.SetVector("_Pixel", new Vector4(1f / width, 1f / height, width, height));
                var fine = Allocate();
                var scratch = Allocate();
                var work = Allocate();
                Graphics.Blit(context.input, fine, material, 0);

                void Blur(RenderTexture source, RenderTexture destination, float radius)
                {
                    Graphics.Blit(source, destination);
                    // Growing convolution steps avoid sparse, widely separated samples at large radii.
                    float remaining = radius * radius;
                    for (float step = .5f; remaining > .0001f; step *= 2f)
                    {
                        float distance = Mathf.Min(step, Mathf.Sqrt(remaining));
                        material.SetVector("_BlurStep", new Vector4(distance / width, 0f, 0f, 0f));
                        Graphics.Blit(destination, scratch, material, 1);
                        material.SetVector("_BlurStep", new Vector4(0f, distance / height, 0f, 0f));
                        Graphics.Blit(scratch, destination, material, 1);
                        remaining -= distance * distance;
                    }
                }

                Blur(fine, work, Mathf.Clamp(smoothing, 0f, 64f) / scale);
                var swap = fine; fine = work; work = swap;
                RenderTexture medium = fine, large = fine;
                if (mode == GenerationMode.Texture)
                {
                    medium = work;
                    Blur(fine, medium, Mathf.Clamp(mediumRadius, .5f, 128f) / scale);
                    large = Allocate();
                    Blur(fine, large, Mathf.Clamp(largeRadius, .5f, 512f) / scale);
                }
                material.SetTexture("_Fine", fine);
                material.SetTexture("_Medium", medium);
                material.SetTexture("_Large", large);
                material.SetTexture("_Input", context.input);
                material.SetFloat("_Mode", (int)mode);
                material.SetFloat("_Strength", Mathf.Clamp(strength, 0f, 128f) / scale);
                material.SetVector("_Details", new Vector4(Mathf.Clamp(fineDetail, 0f, 8f),
                    Mathf.Clamp(mediumDetail, 0f, 8f), Mathf.Clamp(largeDetail, 0f, 8f), Mathf.Clamp01(lightRemoval)));
                material.SetFloat("_Derivative", (int)derivative);
                material.SetVector("_Flip", new Vector4(flipX ? -1f : 1f, flipY ? -1f : 1f, 0f, 0f));
                material.SetFloat("_AlphaMode", (int)alphaMode);
                material.SetFloat("_Output", (int)output);
                material.SetFloat("_Encoding", (int)encoding);
                var result = Allocate(true);
                Graphics.Blit(context.input, result, material, 2);
                return ApplyTransformAndModifiers(result, context);
            }
            finally
            {
                material.SetTexture("_Fine", null);
                material.SetTexture("_Medium", null);
                material.SetTexture("_Large", null);
                material.SetTexture("_Input", null);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                foreach (RenderTexture buffer in buffers) RenderTexture.ReleaseTemporary(buffer);
            }
        }
    }
}
