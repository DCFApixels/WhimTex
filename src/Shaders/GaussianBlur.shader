Shader "Hidden/WhimTex/GaussianBlur"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
        [HideInInspector] _SourceTex ("Original", 2D) = "black" {}
        [HideInInspector] _Strength ("Strength", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _SourceTex;
        float4 _MainTex_TexelSize;
        float4 _Kernel[128];
        float2 _Direction;
        float _CenterWeight, _Strength;
        int _PairCount, _Edges;

        float2 address(float2 pixel)
        {
            float2 size = _MainTex_TexelSize.zw;
            // Pixel addresses are integral. Integer modulo avoids reciprocal rounding
            // mapping the first repeated texel back to the last texel on some GPUs.
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
        float4 sampleEdge(float2 uv)
        {
            // Resolve each neighbour explicitly, including interpolation across repeat seams.
            float2 p = uv * _MainTex_TexelSize.zw - .5;
            float2 low = floor(p), f = frac(p);
            if (all(low >= 0) && all(low + 1 < _MainTex_TexelSize.zw))
                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            return lerp(lerp(at(low), at(low + float2(1,0)), f.x),
                lerp(at(low + float2(0,1)), at(low + 1), f.x), f.y);
        }
        float4 premultiply(v2f_img i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            return float4(c.rgb * c.a, c.a);
        }
        float4 downsample(v2f_img i) : SV_Target
        {
            float2 d = .5 * _MainTex_TexelSize.xy;
            return .25 * (sampleEdge(i.uv + d) + sampleEdge(i.uv - d) +
                sampleEdge(i.uv + float2(d.x,-d.y)) + sampleEdge(i.uv + float2(-d.x,d.y)));
        }
        float4 blur(v2f_img i) : SV_Target
        {
            float4 c = sampleEdge(i.uv) * _CenterWeight;
            [loop] for (int k = 0; k < _PairCount; k++)
            {
                float2 delta = _Direction * _Kernel[k].x;
                c += (sampleEdge(i.uv + delta) + sampleEdge(i.uv - delta)) * _Kernel[k].y;
            }
            return c;
        }
        float4 unpremultiply(v2f_img i) : SV_Target
        {
            float4 c = sampleEdge(i.uv);
            if (_Strength < 1)
            {
                float4 source = tex2D(_SourceTex, i.uv);
                source.rgb *= source.a;
                c = lerp(source, c, _Strength);
            }
            float3 color = c.a > 0 ? c.rgb / c.a : 0;
            float alpha = c.a;
            if (_Strength > 1)
            {
                float a = saturate(alpha);
                alpha = a * _Strength / (1 + a * (_Strength - 1));
            }
            return float4(color, alpha);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment premultiply
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment downsample
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment blur
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment unpremultiply
        ENDCG }
    }
}
