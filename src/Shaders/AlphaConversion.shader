Shader "Hidden/TextureCompositor/AlphaConversion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Mode ("Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
        #include "HdrColor.cginc"

            sampler2D _MainTex;
            float _Mode;
            float _DecodeSource;
            sampler2D _OriginalStraight;
            float _StraightFallback;
            float safe(float v) { return (asuint(v) & 0x7fffffffu) >= 0x7f800000u ? 0.0 : clamp(v, -65504.0, 65504.0); }

            float4 frag(v2f_img input) : SV_Target
            {
                float4 color = tex2D(_MainTex, input.uv);
                #if defined(UNITY_COLORSPACE_GAMMA)
                if (_Mode < 0.5 && _DecodeSource > 0.5) color.rgb = SpriteDecode(color.rgb);
                #endif
                if (_Mode < 0.5)
                    return float4(color.rgb * color.a, color.a);
                if (color.a <= 0.00001)
                {
                    if (_StraightFallback < .5) return float4(0.0, 0.0, 0.0, 0.0);
                    color = float4(tex2D(_OriginalStraight, input.uv).rgb, 0.0);
                    #if defined(UNITY_COLORSPACE_GAMMA)
                    if (_StraightFallback > 1.5) color.rgb = SpriteDecode(color.rgb);
                    #endif
                }
                else color = float4(color.rgb / color.a, color.a);
                // Half-precision premultiplied storage can round RGB and alpha differently.
                // Bound finite unpremultiplied values before the half target; keep nonfinite
                // render values observable by the compositor's existing diagnostics.
                if (_Mode < 1.5)
                {
                    if ((asuint(color.r) & 0x7fffffffu) < 0x7f800000u) color.r = clamp(color.r, -65504.0, 65504.0);
                    if ((asuint(color.g) & 0x7fffffffu) < 0x7f800000u) color.g = clamp(color.g, -65504.0, 65504.0);
                    if ((asuint(color.b) & 0x7fffffffu) < 0x7f800000u) color.b = clamp(color.b, -65504.0, 65504.0);
                }
                if (_Mode > 1.5)
                {
                    color = float4(safe(color.r), safe(color.g), safe(color.b), saturate(safe(color.a)));
                    if (_Mode < 2.5)
                    {
                        color.rgb = SpriteEncode(saturate(color.rgb));
                    }
                }
                return color;
            }
            ENDCG
        }
    }
}
