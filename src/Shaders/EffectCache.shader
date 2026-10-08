Shader "Hidden/WhimTex/EffectCache"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
        [HideInInspector] _Errors ("Errors", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _Errors;
        float _PackedAlpha;
        float4 pack(v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv).aaaa; }
        float4 coverage(v2f_img i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            float a = _PackedAlpha > .5 ? c.r : c.a;
            return float4(a, a, a, a);
        }
        float4 errors(v2f_img i) : SV_Target { return max(tex2D(_MainTex, i.uv).r, tex2D(_Errors, i.uv).r); }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment pack
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment coverage
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment errors
        ENDCG }
    }
}
