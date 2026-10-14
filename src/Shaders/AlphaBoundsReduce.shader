Shader "Hidden/WhimTex/AlphaBoundsReduce"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
    }
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

            Texture2D<float4> _MainTex;
            float4 _SourceSize;
            float _SourceIsBounds;

            float4 Bounds(int2 pixel)
            {
                if (pixel.x >= (int)_SourceSize.x || pixel.y >= (int)_SourceSize.y)
                    return float4(16777216.0, 16777216.0, 0.0, 0.0);
                float4 value = _MainTex.Load(int3(pixel, 0));
                if (_SourceIsBounds > 0.5) return value;
                if (!(value.a > 0.0)) return float4(16777216.0, 16777216.0, 0.0, 0.0);
                return float4(pixel, pixel + 1);
            }

            float4 Union(float4 a, float4 b) { return float4(min(a.xy, b.xy), max(a.zw, b.zw)); }
            float4 frag(v2f_img input) : SV_Target
            {
                int2 origin = (int2)input.pos.xy * 2;
                return Union(Union(Bounds(origin), Bounds(origin + int2(1, 0))),
                    Union(Bounds(origin + int2(0, 1)), Bounds(origin + int2(1, 1))));
            }
            ENDCG
        }
    }
    Fallback Off
}
