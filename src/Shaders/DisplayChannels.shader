Shader "Hidden/TextureCompositor/DisplayChannels"
{
    Properties
    {
        _MainTex ("Input", 2D) = "white" {}
        _Channels ("RGBA", Vector) = (1, 1, 1, 1)
        _Exposure ("Exposure multiplier", Float) = 1
        _Debug ("Numeric errors", Float) = 0
        _Errors ("Error mask", 2D) = "black" {}
        _ErrorColor ("Error display color", Vector) = (1, 0, 1, 1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
        #include "HdrColor.cginc"

            sampler2D _MainTex;
            sampler2D _Errors;
            float _Exposure, _Debug;
            float4 _Channels, _ErrorColor;

            float4 frag(v2f_img input) : SV_Target
            {
                if (_Debug > 0.5 && tex2D(_Errors, input.uv).r > 0.5) return float4(_ErrorColor.rgb, 1);
                float4 color = tex2D(_MainTex, input.uv);
                color.rgb = saturate(color.rgb * _Exposure);
                #if defined(UNITY_COLORSPACE_GAMMA)
                color.rgb = SpriteEncode(color.rgb);
                #endif
                float colorChannels = _Channels.r + _Channels.g + _Channels.b;
                if (colorChannels < 0.5)
                    return fixed4(color.aaa * _Channels.a, 1.0);
                if (colorChannels < 1.5)
                {
                    fixed value = dot(color.rgb, _Channels.rgb);
                    return fixed4(value, value, value, lerp(1.0, color.a, _Channels.a));
                }
                return fixed4(color.rgb * _Channels.rgb, lerp(1.0, color.a, _Channels.a));
            }
            ENDCG
        }
    }
}
