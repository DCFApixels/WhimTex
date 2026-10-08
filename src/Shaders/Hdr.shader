Shader "Hidden/WhimTex/Hdr"
{
    Properties
    {
        // Graphics.Blit only binds its source automatically when this texture is declared.
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
        [HideInInspector] _Errors ("Accumulated errors", 2D) = "black" {}
        [HideInInspector] _Saturate ("Clamp layer output", Float) = 0
        [HideInInspector] _Encode ("Encode LDR output", Float) = 0
        [HideInInspector] _ChannelMapping ("Output channel sources", Vector) = (0, 1, 2, 3)
        [HideInInspector] _UseChannelMapping ("Remap output channels", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "HdrColor.cginc"
        sampler2D _MainTex, _Errors;
        float4 _MainTex_TexelSize;
        float _Saturate, _Encode;
        float4 _ChannelMapping;
        float _UseChannelMapping;
        float channel(float4 c, float source)
        {
            if (source > 12.5)
            {
                float luminance = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
                return source < 13.5 ? luminance : luminance * c.a;
            }
            if (source > 9.5) return (source < 10.5 ? c.r : source < 11.5 ? c.g : c.b) * c.a;
            if (source > 8.5) return 1.0;
            if (source > 7.5) return 0.0;
            bool invert = source > 3.5;
            float index = invert ? source - 4.0 : source;
            float value = index < .5 ? c.r : index < 1.5 ? c.g : index < 2.5 ? c.b : c.a;
            return invert ? 1.0 - value : value;
        }
        float4 channelMapped(float4 c)
        {
            if (_UseChannelMapping < .5) return c;
            return float4(channel(c, _ChannelMapping.r), channel(c, _ChannelMapping.g), channel(c, _ChannelMapping.b), channel(c, _ChannelMapping.a));
        }
        // Inspect bits so fast-math cannot turn NaN comparisons into an always-finite result.
        bool invalid(float v) { return (asuint(v) & 0x7fffffffu) > 0x477fe000u; }
        float safe(float v) { return (asuint(v) & 0x7fffffffu) >= 0x7f800000u ? 0.0 : clamp(v, -65504.0, 65504.0); }
        float4 clean(v2f_img i) : SV_Target
        {
            float4 c = channelMapped(tex2D(_MainTex, i.uv));
            c = float4(safe(c.r), safe(c.g), safe(c.b), saturate(safe(c.a)));
            if (_Saturate > 0.5) c.rgb = saturate(c.rgb);
            if (_Encode > 0.5) c.rgb = SpriteEncode(saturate(c.rgb));
            return c;
        }
        float4 errors(v2f_img i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            float bad = invalid(c.r) || invalid(c.g) || invalid(c.b) || invalid(c.a);
            c = channelMapped(c);
            bad = max(bad, invalid(c.r) || invalid(c.g) || invalid(c.b) || invalid(c.a));
            return max(bad, tex2D(_Errors, i.uv).r);
        }
        float4 reduce(v2f_img i) : SV_Target
        {
            // Destination dimensions are ceil(source / 2); clamp handles odd edges.
            float2 p = floor(i.pos.xy) * 2.0;
            float2 t = _MainTex_TexelSize.xy;
            return max(max(tex2D(_MainTex, (p + float2(.5,.5))*t).r,
                           tex2D(_MainTex, (p + float2(1.5,.5))*t).r),
                       max(tex2D(_MainTex, (p + float2(.5,1.5))*t).r,
                           tex2D(_MainTex, (p + float2(1.5,1.5))*t).r));
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment clean
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment errors
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment reduce
            ENDCG }
    }
}
