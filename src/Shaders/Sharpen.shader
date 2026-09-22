Shader "Hidden/TextureCompositor/Sharpen"
{
        Properties { [HideInInspector] _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
        CGPROGRAM
        #pragma target 3.5
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex, _BlurTex;
        float4 _MainTex_TexelSize;
        float _Strength, _Radius, _Threshold, _NoiseReduction, _HaloSuppression;
        int _Edges, _Fast, _Algorithm, _ChannelMode, _UseBlur;

        float2 address(float2 pixel)
        {
            float2 size = _MainTex_TexelSize.zw;
            if (_Edges == 2) return (int2(pixel) % int2(size) + int2(size)) % int2(size);
            if (_Edges == 3)
            {
                float2 p = (int2(pixel) % int2(2 * size) + int2(2 * size)) % int2(2 * size);
                return min(p, 2 * size - 1 - p);
            }
            return clamp(pixel, 0, size - 1);
        }
        float4 at(float2 pixel)
        {
            if (_Edges == 0 && (any(pixel < 0) || any(pixel >= _MainTex_TexelSize.zw))) return 0;
            return tex2Dlod(_MainTex, float4((address(pixel) + .5) * _MainTex_TexelSize.xy, 0, 0));
        }
        float4 frag(v2f_img i) : SV_Target
        {
            float2 p = i.uv * _MainTex_TexelSize.zw - .5;
            float2 d = float2(_Radius, 0);
            float4 c = at(p);
            float4 n;
            if (_UseBlur != 0)
            {
                // Final path: _BlurTex was produced by the same separable
                // Gaussian pipeline used by the Blur layer.
                n = tex2D(_BlurTex, i.uv);
            }
            else if (_Fast != 0)
            {
                // Cheap interactive approximation: four cardinal samples.
                n = (at(p - d) + at(p + d) + at(p - d.yx) + at(p + d.yx)) * .25;
            }
            else
            {
                // Settled render: a compact Gaussian-like footprint. The
                // weights sum to one and keep the comparison in premultiplied
                // space, avoiding bright/dark halos around transparent edges.
                float2 h = d * .5;
                float2 q = d * .70710678;
                float4 weighted = c * .20;
                weighted += (at(p - h) + at(p + h) + at(p - h.yx) + at(p + h.yx)) * .10;
                weighted += (at(p - d) + at(p + d) + at(p - d.yx) + at(p + d.yx)) * .05;
                weighted += (at(p - q - q.yx) + at(p + q + q.yx) +
                    at(p - q + q.yx) + at(p + q - q.yx)) * .05;
                n = weighted;
            }
            float alpha = c.a;
            float3 center = c.rgb * alpha;
            float3 blurred = n.rgb * n.a;
            float3 detail = center - blurred;
            if (_ChannelMode == 1)
            {
                float luminance = dot(detail, float3(.2126, .7152, .0722));
                detail = luminance.xxx;
            }
            float contrast = max(max(abs(detail.r), abs(detail.g)), abs(detail.b));
            float thresholdWeight = _Threshold <= 0.00001
                ? 1.0 : saturate((contrast - _Threshold) / max(.0001, 1.0 - _Threshold));
            float adaptive = _Algorithm == 1 ? saturate(contrast * 4.0) : 1.0;
            float noiseWeight = lerp(1.0, thresholdWeight, _NoiseReduction);
            detail *= thresholdWeight * adaptive * noiseWeight;
            float3 sharpened = center + detail * _Strength;
            float3 localMin = min(center, blurred);
            float3 localMax = max(center, blurred);
            float3 limited = lerp(sharpened, clamp(sharpened, localMin, localMax), _HaloSuppression);
            // Keep HDR values intact. The compositor applies the layer's color
            // range and output policy later; sharpening must not clip here.
            float3 color = 0;
            if (alpha > 0) color = limited / alpha;
            return float4(color, alpha);
        }
        ENDCG
        }
    }
}
