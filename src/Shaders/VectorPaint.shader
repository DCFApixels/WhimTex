Shader "Hidden/WhimTex/VectorPaint"
{
    Properties { _MainTex ("Coverage", 2D) = "black" {} }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "GradientPalette.cginc"
        #define WHIMTEX_FREEFORM_STROKE
        #include "FreeformGradient.cginc"
        #include "FigureGradient.cginc"
        sampler2D _MainTex, _StrokeGradientPalette;
        float4 _StrokeGradientPalette_TexelSize, _StrokeGradientIntervals[129], _StrokeGradientStart;
        int _StrokeGradientWrapMode, _StrokeGradientIntervalCount;
        float4 _TargetSize, _CoverageRect, _FillColor, _StrokeColor, _FillAnchors, _StrokeAnchors, _Repetitions;
        float3 _LocalRow0, _LocalRow1, _LocalRow2;
        int _FillMode, _StrokeMode;
        float Coordinate(float2 uv, float4 anchors, int mode, float repetitions)
        {
            return FigureGradientCoordinate(uv, _TargetSize.zw, anchors, mode, repetitions);
        }
        float4 Paint(v2f_img i)
        {
            float2 maskUv = (i.uv * _TargetSize.xy - _CoverageRect.xy) / _CoverageRect.zw;
            if (any(maskUv < 0) || any(maskUv >= 1)) return 0;
            float3 coverage = saturate(tex2Dlod(_MainTex, float4(maskUv, 0, 0)).rgb);
            if (all(coverage.xy == 0)) return 0;
            float3 canvas = float3(i.uv, 1);
            float2 uv = float2(dot(_LocalRow0, canvas), dot(_LocalRow1, canvas)) / dot(_LocalRow2, canvas);
            float4 paint = _FillColor, ink = _StrokeColor;
            if (coverage.x > 0 && _FillMode == 7) paint = FreeformColor(uv);
            if (coverage.y > 0 && _StrokeMode == 7) ink = StrokeFreeformColor(uv);
            if (coverage.x > 0 && _FillMode >= 2 && _FillMode < 7) paint = SampleGradientPalette(Coordinate(uv, _FillAnchors, _FillMode, _Repetitions.x));
            if (coverage.y > 0 && _StrokeMode >= 2 && _StrokeMode < 7)
                ink = SampleGradientPaletteAt(Coordinate(uv, _StrokeAnchors, _StrokeMode, _Repetitions.y),
                    _StrokeGradientPalette, _StrokeGradientPalette_TexelSize, _StrokeGradientIntervals,
                    _StrokeGradientIntervalCount, _StrokeGradientStart, _StrokeGradientWrapMode);
            // Preserve the integrated intersection, not a product of averaged edge coverages.
            float sa = ink.a * coverage.y;
            float fa = paint.a * max(0, coverage.x - ink.a * coverage.z);
            return float4(paint.rgb * fa + ink.rgb * sa, saturate(sa + fa));
        }
        float4 Straight(v2f_img i) : SV_Target
        { float4 c = Paint(i); return c.a > 1e-12 ? float4(c.rgb / c.a, c.a) : 0; }
        float4 Premultiplied(v2f_img i) : SV_Target { return Paint(i); }
        float4 Unpremultiply(v2f_img i) : SV_Target
        { float4 c = tex2D(_MainTex, i.uv); return c.a > 1e-12 ? float4(c.rgb / c.a, c.a) : 0; }
        ENDCG
        Pass
        {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Premultiplied
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Unpremultiply
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Straight
            ENDCG
        }
    }
}
