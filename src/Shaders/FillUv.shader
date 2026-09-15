Shader "Hidden/TextureCompositor/FillUv"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "ProceduralUv.cginc"
            float4 frag(v2f_img input) : SV_Target
            {
                return float4(ProceduralSourceUv(input.uv), 0, 1);
            }
            ENDCG
        }
    }
}
