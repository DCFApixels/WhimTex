Shader "Hidden/WhimTex/LightCookieCopy"
{
    Properties { _MainTex ("Cookie", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _CookieInterior, _CookieTileSize;
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = (input.uv * _CookieTileSize.xy - _CookieInterior.xy) / _CookieInterior.zw;
                uv = clamp(uv, abs(_MainTex_TexelSize.xy) * .5, 1 - abs(_MainTex_TexelSize.xy) * .5);
                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }
            ENDCG
        }
    }
}
