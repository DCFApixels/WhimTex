Shader "Hidden/WhimTex/OutputPreview"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Mip, _Channel;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 color = tex2Dlod(_MainTex, float4(input.uv, 0, _Mip));
                if (_Channel > 1.5) return float4(color.aaa, 1);
                if (_Channel > .5) color.a = 1;
                return color;
            }
            ENDCG
        }
    }
}
