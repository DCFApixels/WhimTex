Shader "Hidden/WhimTex/MotionBlur"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
        [HideInInspector] _SourceTex ("Original", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _SourceTex;
        float4 _MainTex_TexelSize;
        float2 _CanvasSize, _Motion, _Center;
        float _Arc, _Bias, _Strength;
        int _Edges, _SampleLimit;

        float2 address(float2 pixel)
        {
            float2 size = _MainTex_TexelSize.zw;
            // Integral addressing remains exact at repeat seams on non-power-of-two textures.
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
        int segments(float pathLength)
        {
            return (int)clamp(ceil(pathLength), 1, _SampleLimit - 1);
        }
        float4 linearBlur(v2f_img i) : SV_Target
        {
            float2 delta = _Motion / _CanvasSize;
            int count = segments(length(delta * _MainTex_TexelSize.zw));
            float4 total = 0;
            [loop] for (int k = 0; k <= count; k++)
            {
                float t = (float)k / count - .5 + _Bias;
                float weight = k == 0 || k == count ? .5 : 1;
                total += sampleEdge(i.uv - delta * t) * weight;
            }
            return total / count;
        }
        float2 rotate(float2 p, float sine, float cosine)
        {
            return float2(cosine * p.x - sine * p.y, sine * p.x + cosine * p.y);
        }
        float4 circularBlur(v2f_img i) : SV_Target
        {
            float2 p = (i.uv - _Center) * _CanvasSize;
            float2 origin = p;
            float density = max(_MainTex_TexelSize.z / _CanvasSize.x, _MainTex_TexelSize.w / _CanvasSize.y);
            int count = segments(length(p) * _Arc * density);
            float initialSin, initialCos, stepSin, stepCos;
            sincos((.5 - _Bias) * _Arc, initialSin, initialCos);
            sincos(-_Arc / count, stepSin, stepCos);
            p = rotate(p, initialSin, initialCos);
            float4 total = 0;
            [loop] for (int k = 0; k <= count; k++)
            {
                if (k > 0 && (k & 31) == 0)
                {
                    float resetSin, resetCos;
                    sincos((.5 - _Bias - (float)k / count) * _Arc, resetSin, resetCos);
                    p = rotate(origin, resetSin, resetCos);
                }
                float weight = k == 0 || k == count ? .5 : 1;
                total += sampleEdge(_Center + p / _CanvasSize) * weight;
                p = rotate(p, stepSin, stepCos);
            }
            return total / count;
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
            #pragma fragment linearBlur
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment circularBlur
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment unpremultiply
        ENDCG }
    }
}
