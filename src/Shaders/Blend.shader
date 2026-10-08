Shader "Hidden/WhimTex/Blend"
{
    Properties
    {
        _MainTex ("Base", 2D) = "black" {}
        _Blend ("Layer", 2D) = "black" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Mode ("Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        CGINCLUDE
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Blend;
            float _Mode;
            float _Opacity;
            float _HdrBlend;
            float _PreserveAlpha;
            float _BrushErase, _BrushStandard, _BrushStampAccumulation;
            #include "ColorBlend.cginc"

            float4 frag(v2f_img input) : SV_Target
            {
                float4 backdrop = tex2D(_MainTex, input.uv);
                float4 source = tex2D(_Blend, input.uv);
                float opacity = saturate(_Opacity);

                if (_Mode == 102.0)
                    return float4(backdrop.rgb, saturate(backdrop.a) * saturate(source.a) * opacity);

                // Alpha-only union used when a group is an effect target.
                if (_Mode == 100.0)
                {
                    float alpha = 1.0 - (1.0 - saturate(backdrop.a)) * (1.0 - saturate(source.a * opacity));
                    return float4(alpha, alpha, alpha, alpha);
                }

                // Pass-through opacity interpolates the before/after group in premultiplied linear light.
                if (_Mode == 101.0)
                {
                    float a = lerp(backdrop.a, source.a, opacity);
                    float3 rgb = lerp(backdrop.rgb * backdrop.a, source.rgb * source.a, opacity);
                    return float4(a > 0.000001 ? rgb / a : 0.0, a);
                }

                if (opacity <= 0.0) return backdrop;
                if (_PreserveAlpha > 0.5)
                {
                    // All members share the base alpha. Mixing their coverage by
                    // source-over would incorrectly make soft base edges opaque.
                    if (_Mode == 3 || backdrop.a <= 0.0 || source.a <= 0.0) return backdrop;
                    bool hdr = _HdrBlend > 0.5;
                    float3 b = hdr ? backdrop.rgb : ToSrgbBlendSpace(backdrop.rgb);
                    float3 s = hdr ? source.rgb : ToSrgbBlendSpace(source.rgb);
                    float3 color = bound(EvaluateBlend(bound(b), bound(s), _Mode));
                    color = lerp(b, color, saturate(source.a * opacity));
                    return float4(hdr ? color : FromSrgbBlendSpace(color), backdrop.a);
                }

                if (_Mode == 2)
                    // Outside a clipping chain, overwrite replaces the complete RGBA pixel.
                    return lerp(backdrop, source, opacity);

                // DMBlend.None is a true no-op for both color and alpha.
                if (_Mode == 3)
                    return backdrop;

                float sourceAlpha = saturate(source.a * opacity);
                if (sourceAlpha <= 0.0) return backdrop;
                float backdropAlpha = saturate(backdrop.a);
                if (backdropAlpha <= 0.0) return float4(source.rgb, sourceAlpha);
                float outputAlpha = sourceAlpha + backdropAlpha * (1.0 - sourceAlpha);
                bool hdr = _HdrBlend > 0.5;
                float3 backdropColor = hdr ? backdrop.rgb : ToSrgbBlendSpace(backdrop.rgb);
                float3 sourceColor = hdr ? source.rgb : ToSrgbBlendSpace(source.rgb);
                float3 blendedColor = bound(EvaluateBlend(bound(backdropColor), bound(sourceColor), _Mode));

                // The blend function applies only to overlap. Non-overlap retains unbounded RGB.
                float3 premultiplied = sourceAlpha * (1.0 - backdropAlpha) * sourceColor
                                     + sourceAlpha * backdropAlpha * blendedColor
                                     + (1.0 - sourceAlpha) * backdropAlpha * backdropColor;
                float3 outputColor = premultiplied / outputAlpha;
                return float4(hdr ? outputColor : FromSrgbBlendSpace(outputColor), outputAlpha);
            }
            float4 fragBrush(v2f_img input) : SV_Target
            {
                float4 before = tex2D(_MainTex, input.uv);
                float4 stroke = tex2D(_Blend, input.uv);
                if (_BrushStampAccumulation > .5) return lerp(before, stroke, saturate(_Opacity));
                return CompositeBrushPixel(before, stroke, _Opacity, _Mode, _BrushErase, _BrushStandard);
            }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragBrush
            #pragma target 3.0
            ENDCG
        }
    }
}
