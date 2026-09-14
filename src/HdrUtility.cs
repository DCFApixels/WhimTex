using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>Linear working pixels; explicit conversion at encoded image boundaries.</summary>
    internal static class HdrUtility
    {
        internal const float MaxValue = 65504f;
        internal static Color ApplyChannelMask(Color value, Vector4 mask) => new Color(
            mask.x > 0f ? value.r : 0f, mask.y > 0f ? value.g : 0f,
            mask.z > 0f ? value.b : 0f, mask.w > 0f ? value.a : 0f);
        internal static void SetShaderColor(Material material, string name, Color encoded)
        {
            // Previously applied FX have Color properties: Unity decodes those on upload in Linear projects.
            // New FX use Vector properties so signed HDR uniforms receive exactly one explicit conversion.
            int index = material.shader.FindPropertyIndex(name);
            bool automaticDecode = QualitySettings.activeColorSpace == ColorSpace.Linear && index >= 0 &&
                material.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Color;
            material.SetVector(name, (Vector4)(automaticDecode ? encoded : Decode(encoded)));
        }

        internal static Color Decode(Color color)
        {
            float3 value = new float3(color.r, color.g, color.b);
            float3 magnitude = math.abs(value);
            value = math.sign(value) * math.select(magnitude / 12.92f,
                math.pow((magnitude + .055f) / 1.055f, 2.4f), magnitude > .04045f);
            return new Color(value.x, value.y, value.z, color.a);
        }
        internal static Color Encode(Color color)
        {
            float3 value = new float3(color.r, color.g, color.b);
            float3 magnitude = math.abs(value);
            value = math.sign(value) * math.select(magnitude * 12.92f,
                1.055f * math.pow(magnitude, 1f / 2.4f) - .055f, magnitude > .0031308f);
            return new Color(value.x, value.y, value.z, color.a);
        }
        // Limit paint intensity before narrowing to float/half, preserving signed linear RGB ratios.
        internal static Color DecodePaintColor(Color encoded)
        {
            double r = DecodePaintComponent(encoded.r), g = DecodePaintComponent(encoded.g), b = DecodePaintComponent(encoded.b);
            double peak = Math.Max(Math.Abs(r), Math.Max(Math.Abs(g), Math.Abs(b)));
            double scale = peak > MaxValue ? MaxValue / peak : 1.0;
            return new Color((float)(r * scale), (float)(g * scale), (float)(b * scale), Mathf.Clamp01(Safe(encoded.a)));
        }
        private static double DecodePaintComponent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.0;
            double magnitude = Math.Abs((double)value);
            return Math.Sign(value) * (magnitude <= .04045 ? magnitude / 12.92 : Math.Pow((magnitude + .055) / 1.055, 2.4));
        }

        internal static RenderTexture Temporary(int width, int height, bool fullPrecision = false)
        {
            var result = RenderTexture.GetTemporary(width, height, 0,
                fullPrecision ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            result.filterMode = FilterMode.Bilinear;
            result.wrapMode = TextureWrapMode.Clamp;
            return result;
        }

        internal static bool IsHdr(Texture2D texture) => texture != null &&
            (texture.format == TextureFormat.RGBAHalf || texture.format == TextureFormat.RGBAFloat);

        internal static Color Safe(Color value)
        {
            value.r = Safe(value.r); value.g = Safe(value.g); value.b = Safe(value.b);
            value.a = Mathf.Clamp01(Safe(value.a));
            return value;
        }
        private static float Safe(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -MaxValue, MaxValue);
        internal static Color Saturate(Color value) => new Color(Mathf.Clamp01(value.r),
            Mathf.Clamp01(value.g), Mathf.Clamp01(value.b), Mathf.Clamp01(value.a));

        internal static Texture2D ReadLinear(RenderTexture source, bool upload = true)
        {
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                if (upload) result.Apply(false, false);
                return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
            finally { RenderTexture.active = previous; }
        }

        internal static NativeArray<Color> ReadPixels(Texture2D source, Allocator allocator)
        {
            var result = new NativeArray<Color>(source.width * source.height, allocator);
            bool encoded = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            if (source.format == TextureFormat.RGBAHalf)
            {
                var values = source.GetRawTextureData<half4>();
                for (int i = 0; i < result.Length; i++) { float4 c = values[i]; result[i] = new Color(c.x, c.y, c.z, c.w); }
            }
            else if (source.format == TextureFormat.RGBAFloat) result.CopyFrom(source.GetRawTextureData<Color>());
            else if (source.format == TextureFormat.RGBA32)
            {
                var values = source.GetRawTextureData<Color32>();
                for (int i = 0; i < result.Length; i++) { Color c = values[i]; result[i] = encoded ? Decode(c) : c; }
            }
            else
            {
                Color[] values = source.GetPixels();
                for (int i = 0; i < values.Length; i++) result[i] = encoded ? Decode(values[i]) : values[i];
            }
            return result;
        }

        internal static void WritePixels(Texture2D target, NativeArray<Color> values)
        {
            if (target.format == TextureFormat.RGBAFloat)
            {
                var output = target.GetRawTextureData<Color>();
                for (int i = 0; i < output.Length; i++) output[i] = Safe(values[i]);
            }
            else if (target.format == TextureFormat.RGBAHalf)
            {
                var output = target.GetRawTextureData<half4>();
                for (int i = 0; i < output.Length; i++) { Color c = Safe(values[i]); output[i] = new half4(new float4(c.r, c.g, c.b, c.a)); }
            }
            else
            {
                var output = target.GetRawTextureData<Color32>();
                bool encoded = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(target.graphicsFormat);
                for (int i = 0; i < output.Length; i++)
                {
                    Color color = Saturate(Safe(values[i]));
                    output[i] = encoded ? Encode(color) : color;
                }
            }
            target.Apply(false, false);
        }

        internal static Texture2D ToLdr(Texture2D source, bool whiteBackground = false)
        {
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = source.filterMode, wrapMode = source.wrapMode };
            using var values = ReadPixels(source, Allocator.Temp);
            var bytes = result.GetRawTextureData<Color32>();
            for (int i = 0; i < values.Length; i++)
            {
                Color color = Encode(Saturate(Safe(values[i])));
                if (whiteBackground) { color = Color.Lerp(Color.white, color, color.a); color.a = 1f; }
                bytes[i] = color;
            }
            result.Apply(false, false);
            return result;
        }
    }
}
