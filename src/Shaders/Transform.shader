Shader "Hidden/TextureCompositor/Transform"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
        #include "HdrColor.cginc"

            #if defined(SHADER_API_D3D11) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN)
                #define TRANSFORM_NATIVE_SAMPLERS
                Texture2D _MainTex;
                SamplerState transform_point_clamp_sampler;
                SamplerState transform_point_repeat_sampler;
                SamplerState transform_point_mirror_sampler;
                SamplerState transform_linear_clamp_sampler;
                SamplerState transform_linear_repeat_sampler;
                SamplerState transform_linear_mirror_sampler;
                SamplerState transform_trilinear_clamp_sampler;
                SamplerState transform_trilinear_repeat_sampler;
                SamplerState transform_trilinear_mirror_sampler;
            #else
                sampler2D _MainTex;
            #endif
            float _DecodeSource;
            float4 _MainTex_TexelSize;
            float3 _TransformRow0, _TransformRow1, _TransformRow2;
            float2 _OutputSize;
            int _ClipOutside;
            // Unity TextureWrapMode: Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3.
            int _WrapModeU;
            int _WrapModeV;
            // Unity FilterMode: Point = 0, Bilinear = 1, Trilinear = 2.
            int _FilterMode;
            int _SourceMipCount;

            float AddressPixel(float pixel, float size, int wrapMode)
            {
                if (wrapMode == 0)
                    return pixel - floor(pixel / size) * size;
                if (wrapMode == 2)
                {
                    float period = size * 2.0;
                    pixel -= floor(pixel / period) * period;
                    return min(pixel, period - 1.0 - pixel);
                }
                if (wrapMode == 3)
                    pixel = pixel < 0.0 ? -pixel - 1.0 : pixel;
                return clamp(pixel, 0.0, size - 1.0);
            }

            float4 FetchPixel(float2 pixel, float2 size, float mip)
            {
                pixel = float2(AddressPixel(pixel.x, size.x, _WrapModeU),
                    AddressPixel(pixel.y, size.y, _WrapModeV));
                #if defined(TRANSFORM_NATIVE_SAMPLERS)
                    return _MainTex.Load(int3((int2)pixel, (int)mip));
                #else
                    // Integer mip and texel centers bypass the source sampler's filtering.
                    return tex2Dlod(_MainTex, float4((pixel + 0.5) / size, 0, mip));
                #endif
            }

            float4 SampleMip(float2 uv, float mip)
            {
                float2 size = max(floor(_MainTex_TexelSize.zw / exp2(mip)), 1.0);
                float2 pixel = uv * size;
                if (_FilterMode == 0)
                    return FetchPixel(floor(pixel), size, mip);
                pixel -= 0.5;
                float2 blend = frac(pixel);
                float2 first = floor(pixel);
                float4 bottom = lerp(FetchPixel(first, size, mip),
                    FetchPixel(first + float2(1, 0), size, mip), blend.x);
                float4 top = lerp(FetchPixel(first + float2(0, 1), size, mip),
                    FetchPixel(first + float2(1, 1), size, mip), blend.x);
                return lerp(bottom, top, blend.y);
            }

            float SourceMipLevel(float2 uv)
            {
                if (_SourceMipCount <= 1)
                    return 0.0;
                // Derivatives before wrapping/clipping avoid false mip seams at tile boundaries.
                float2 dx = ddx(uv) * _MainTex_TexelSize.zw;
                float2 dy = ddy(uv) * _MainTex_TexelSize.zw;
                float mip = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 0.000001));
                return clamp(mip, 0.0, (float)(_SourceMipCount - 1));
            }

            float4 SampleFiltered(float2 uv, float mip)
            {
                if (_FilterMode != 2)
                    mip = floor(mip + 0.5);
                #if defined(TRANSFORM_NATIVE_SAMPLERS)
                    // Common wrap modes stay on hardware filtering (one sampling instruction).
                    // Only mixed U/V modes need the per-texel path below.
                    if (_WrapModeU == _WrapModeV)
                    {
                        int wrapMode = _WrapModeU;
                        if (wrapMode == 3)
                        {
                            uv = abs(uv);
                            wrapMode = 1;
                        }
                        if (_FilterMode == 0)
                        {
                            if (wrapMode == 0) return _MainTex.SampleLevel(transform_point_repeat_sampler, uv, mip);
                            if (wrapMode == 2) return _MainTex.SampleLevel(transform_point_mirror_sampler, uv, mip);
                            return _MainTex.SampleLevel(transform_point_clamp_sampler, uv, mip);
                        }
                        if (_FilterMode == 1)
                        {
                            if (wrapMode == 0) return _MainTex.SampleLevel(transform_linear_repeat_sampler, uv, mip);
                            if (wrapMode == 2) return _MainTex.SampleLevel(transform_linear_mirror_sampler, uv, mip);
                            return _MainTex.SampleLevel(transform_linear_clamp_sampler, uv, mip);
                        }
                        if (wrapMode == 0) return _MainTex.SampleLevel(transform_trilinear_repeat_sampler, uv, mip);
                        if (wrapMode == 2) return _MainTex.SampleLevel(transform_trilinear_mirror_sampler, uv, mip);
                        return _MainTex.SampleLevel(transform_trilinear_clamp_sampler, uv, mip);
                    }
                #endif
                float lowerMip = floor(mip);
                float4 lower = SampleMip(uv, lowerMip);
                if (_FilterMode != 2 || mip == lowerMip)
                    return lower;
                return lerp(lower, SampleMip(uv, min(lowerMip + 1.0, (float)(_SourceMipCount - 1))), frac(mip));
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float3 p = float3(input.uv, 1);
                float w = dot(_TransformRow2, p);
                if (w < 1e-8) return 0;
                float2 sourceUV = float2(dot(_TransformRow0, p), dot(_TransformRow1, p)) / w;


                float mip = SourceMipLevel(sourceUV);
                if (_ClipOutside != 0 &&
                    (sourceUV.x < 0.0 || sourceUV.x > 1.0 || sourceUV.y < 0.0 || sourceUV.y > 1.0))
                    return float4(0, 0, 0, 0);
                float4 color = SampleFiltered(sourceUV, mip);
                #if defined(UNITY_COLORSPACE_GAMMA)
                if (_DecodeSource > 0.5) color.rgb = SpriteDecode(color.rgb);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
