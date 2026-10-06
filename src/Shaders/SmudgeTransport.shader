Shader "Hidden/TextureCompositor/SmudgeTransport"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _Map, _Paint, _Original, _Carry, _Selection, _Backdrop;
        float4 _GridRect;
        float2 _Dimensions, _Center, _Delta, _TipSize;
        float2 _ColorOrigin, _ColorSize, _ColorDimensions, _ColorPhase;
        float _Hardness, _Strength, _Flow, _Mixing, _Wrap, _FrozenCarry;
        float4 _CanvasRow0, _CanvasRow1, _CanvasRow2;
        float4 _SourceRow0, _SourceRow1, _SourceRow2;
        float4 _SelectionRow0, _SelectionRow1, _SelectionRow2;
        float2 mapPoint(float2 uv, float4 a, float4 b, float4 c)
        {
            float3 p = float3(uv, 1);
            float w = dot(c.xyz, p);
            return abs(w) < 1e-8 ? float2(-1e9, -1e9) : float2(dot(a.xyz, p), dot(b.xyz, p)) / w;
        }
        float4 gridMap(float2 pixel)
        {
            float2 uv = (pixel - _GridRect.xy) / _GridRect.zw;
            if (any(uv < 0) || any(uv >= 1)) return float4(pixel, 1, 1);
            return tex2Dlod(_Map, float4(uv, 0, 0));
        }
        float4 gridPaint(float2 pixel)
        {
            if (_Mixing <= 0) return 0;
            float2 uv = (pixel - _GridRect.xy) / _GridRect.zw;
            if (any(uv < 0) || any(uv >= 1)) return 0;
            return tex2Dlod(_Paint, float4(uv, 0, 0));
        }
        float coverage(float2 uv)
        {
            float2 canvas = mapPoint(uv, _CanvasRow0, _CanvasRow1, _CanvasRow2);
            float2 delta = canvas - _Center;
            if (_Wrap > .5) delta -= floor(delta + .5);
            float radius = length(delta * 2 / _TipSize);
            float edge = max(fwidth(radius), 1e-5);
            float amount = _Hardness >= .9999 ? saturate((1 - radius) / edge + .5) : 1 - smoothstep(_Hardness, 1, radius);
            float2 selected = mapPoint(canvas, _SelectionRow0, _SelectionRow1, _SelectionRow2);
            if (any(selected < 0) || any(selected > 1) || (_Wrap > .5 && (any(canvas < 0) || any(canvas >= 1)))) return 0;
            return amount * saturate(tex2Dlod(_Selection, float4(selected, 0, 0)).r) * _Strength * _Flow;
        }
        float2 previousPixel(float2 uv)
        {
            float amount = coverage(uv) * (1 - _Mixing);
            if (amount <= 0) return uv * _Dimensions;
            float2 canvas = mapPoint(uv, _CanvasRow0, _CanvasRow1, _CanvasRow2);
            canvas -= _Delta * amount;
            if (_Wrap > .5) canvas = frac(canvas);
            return mapPoint(canvas, _SourceRow0, _SourceRow1, _SourceRow2) * _Dimensions;
        }
        float4 initialize(v2f_img i) : SV_Target { return float4(i.uv * _Dimensions, 1, 1); }
        float4 warpMap(v2f_img i) : SV_Target
        {
            float2 pixel = previousPixel(i.uv);
            if (any(pixel < .5) || any(pixel > _Dimensions - .5)) return 0;
            return gridMap(pixel);
        }
        float4 warpPaint(v2f_img i) : SV_Target
        {
            float2 pixel = previousPixel(i.uv);
            if (any(pixel < .5) || any(pixel > _Dimensions - .5)) return 0;
            return gridPaint(pixel);
        }
        float4 reconstruct(v2f_img i) : SV_Target
        {
            float4 provenance = gridMap(i.uv * _Dimensions);
            float2 uv = provenance.xy / _Dimensions;
            float2 dx = ddx(uv), dy = ddy(uv);
            float4 color = tex2Dgrad(_Original, uv, dx, dy);
            if (provenance.a < .999 || any(uv < 0) || any(uv >= 1)) color = 0;
            return color * provenance.z + gridPaint(i.uv * _Dimensions);
        }
        float depositCoverage(float2 uv, out float2 carryUv)
        {
            float2 color = uv;
            if (_Wrap > .5)
            {
                float2 canvas = mapPoint(uv, _CanvasRow0, _CanvasRow1, _CanvasRow2);
                float2 delta = canvas - _Center; delta -= floor(delta + .5);
                color = mapPoint(_Center + delta, _SourceRow0, _SourceRow1, _SourceRow2);
            }
            float2 phase = _FrozenCarry > .5 ? _ColorPhase : 0;
            carryUv = (color * _ColorDimensions - _ColorOrigin - phase) / _ColorSize;
            float amount = coverage(uv) * _Mixing;
            return any(carryUv < 0) || any(carryUv >= 1) ? 0 : amount;
        }
        float4 depositMap(v2f_img i) : SV_Target
        {
            float2 carryUv;
            float amount = depositCoverage(i.uv, carryUv);
            float4 value = gridMap(i.uv * _Dimensions);
            value.z *= 1 - amount;
            return value;
        }
        float4 depositPaint(v2f_img i) : SV_Target
        {
            float2 carryUv;
            float amount = depositCoverage(i.uv, carryUv);
            float4 carried = tex2Dlod(_Carry, float4(carryUv, 0, 0));
            return gridPaint(i.uv * _Dimensions) * (1 - amount) + carried * amount;
        }
        float4 copy(v2f_img i) : SV_Target { return tex2Dlod(_MainTex, float4(i.uv, 0, 0)); }
        float4 sampledDeposit(v2f_img i) : SV_Target
        {
            float2 canvas = mapPoint(i.uv, _CanvasRow0, _CanvasRow1, _CanvasRow2);
            float4 original = tex2Dlod(_Backdrop, float4(i.uv, 0, 0));
            float4 sampled = tex2Dlod(_MainTex, float4(canvas, 0, 0));
            return original + (sampled - original) * coverage(i.uv);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment initialize
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment warpMap
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment warpPaint
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment reconstruct
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment depositMap
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment depositPaint
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment copy
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment sampledDeposit
        ENDCG }
    }
}
