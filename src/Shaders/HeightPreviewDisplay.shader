Shader "Hidden/WhimTex/HeightPreviewDisplay"
{
    Properties
    {
        _MainTex ("Surface coordinates", 2D) = "black" {}
        _Source ("Source color", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            Texture2D<float4> _MainTex, _Source;
            SamplerState sampler_point_clamp, sampler_linear_clamp;
            float4 _Channels;
            float _Textured, _EncodeGamma;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 geometry = _MainTex.SampleLevel(sampler_point_clamp, i.uv, 0);
                if (geometry.a < .5) return 0;
                float4 color = _Source.SampleLevel(sampler_linear_clamp, geometry.rg, 0);
                float channels = dot(_Channels.rgb, 1);
                if (channels < .5) color = float4((color.a * _Channels.a).xxx, 1);
                else
                {
                    color.rgb = channels < 1.5 ? dot(color.rgb, _Channels.rgb).xxx : color.rgb * _Channels.rgb;
                    color.a = lerp(1, color.a, _Channels.a);
                }
                color.rgb = lerp(.65.xxx, color.rgb, _Textured) * geometry.b;
                #if defined(UNITY_COLORSPACE_GAMMA)
                if (_EncodeGamma > .5) color.rgb = SpriteEncode(color.rgb);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
