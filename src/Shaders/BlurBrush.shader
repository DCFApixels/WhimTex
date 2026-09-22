Shader "Hidden/TextureCompositor/BlurBrush"
{
    Properties
    {
        _MainTex ("Base", 2D) = "black" {}
        _BlurTex ("Blurred", 2D) = "black" {}
        _MaskTex ("Mask", 2D) = "black" {}
        _Strength ("Strength", Range(0,1)) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _BlurTex, _MaskTex;
            float4 _BlurTex_TexelSize;
            float _Strength;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 d = _BlurTex_TexelSize.xy;
                float4 b = tex2D(_BlurTex, i.uv) * 4.0;
                b += tex2D(_BlurTex, i.uv + float2(d.x, 0));
                b += tex2D(_BlurTex, i.uv - float2(d.x, 0));
                b += tex2D(_BlurTex, i.uv + float2(0, d.y));
                b += tex2D(_BlurTex, i.uv - float2(0, d.y));
                b += tex2D(_BlurTex, i.uv + d);
                b += tex2D(_BlurTex, i.uv - d);
                b += tex2D(_BlurTex, i.uv + float2(d.x, -d.y));
                b += tex2D(_BlurTex, i.uv + float2(-d.x, d.y));
                b /= 12.0;
                float4 m = tex2D(_MaskTex, i.uv);
                float mask = saturate(max(m.a, m.r) * _Strength);
                return lerp(tex2D(_MainTex, i.uv), b, mask);
            }
            ENDCG
        }
    }
}
