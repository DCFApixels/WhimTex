Shader "Hidden/WhimTex/AlphaPresenceReduce"
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
            float _SourceIsPresence;

            uint Present(int2 pixel)
            {
                if (pixel.x >= (int)_SourceSize.x || pixel.y >= (int)_SourceSize.y) return 0u;
                float4 color = _MainTex.Load(int3(pixel, 0));
                float value = _SourceIsPresence > 0.5 ? color.r : color.a;
                // Both signed zeros are empty; negative, infinite and NaN alpha are present.
                return (asuint(value) & 0x7fffffffu) != 0u ? 1u : 0u;
            }

            float4 frag(v2f_img input) : SV_Target
            {
                int2 origin = (int2)input.pos.xy * 2;
                uint present = Present(origin) | Present(origin + int2(1, 0)) |
                    Present(origin + int2(0, 1)) | Present(origin + int2(1, 1));
                float value = present != 0u ? 1.0 : 0.0;
                return float4(value, value, value, value);
            }
            ENDCG
        }
    }
    Fallback Off
}
