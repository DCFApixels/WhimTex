Shader "Hidden/WhimTex/SmudgeBrush"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _Carry, _Backdrop, _Selection;
        float2 _Center, _TipSize;
        float2 _ColorOrigin, _ColorSize, _ColorDimensions, _OldCarrySize, _ColorPhase;
        float _Wrap, _PeriodicTip, _NativeDeposit, _Hardness, _Strength, _Flow, _PickupRetention, _FrozenCarry;
        float4 _ColorRow0, _ColorRow1, _ColorRow2;
        float4 _CanvasRow0, _CanvasRow1, _CanvasRow2;
        float4 _DepositRow0, _DepositRow1, _DepositRow2;
        float4 _SelectionRow0, _SelectionRow1, _SelectionRow2;
        float2 map(float2 uv, float4 a, float4 b, float4 c)
        {
            float3 p = float3(uv, 1);
            float w = dot(c.xyz, p);
            if (abs(w) < 1e-8) return float2(-1e9, -1e9);
            return float2(dot(a.xyz, p), dot(b.xyz, p)) / w;
        }
        float4 pickup(v2f_img i) : SV_Target
        {
            float2 uv = (_ColorOrigin + floor(i.uv * _ColorSize) + .5) / _ColorDimensions;
            if (_Wrap > .5)
            {
                float2 canvas = map(uv, _CanvasRow0, _CanvasRow1, _CanvasRow2);
                // Avoid a matrix round trip for pixels that do not cross a seam.
                if (any(canvas < 0) || any(canvas >= 1))
                    uv = map(frac(canvas), _ColorRow0, _ColorRow1, _ColorRow2);
            }
            float4 sampled = 0;
            if (all(uv >= 0) && all(uv < 1))
            {
                uv = (floor(uv * _ColorDimensions) + .5) / _ColorDimensions;
                sampled = tex2Dlod(_MainTex, float4(uv, 0, 0));
            }
            if (_PickupRetention <= 0) return sampled;
            float4 retained = tex2Dlod(_Carry, float4(i.uv, 0, 0));
            // Refresh premultiplied RGBA without high-pass or contrast compensation.
            return sampled + (retained - sampled) * _PickupRetention;
        }
        float4 deposit(v2f_img i) : SV_Target
        {
            float4 original = tex2D(_Backdrop, i.uv);
            float2 canvas = map(i.uv, _DepositRow0, _DepositRow1, _DepositRow2);
            if (_Wrap > .5 && (any(canvas < 0) || any(canvas >= 1))) return original;
            float2 delta = canvas - _Center;
            if (_PeriodicTip > .5) delta -= floor(delta + .5);
            float2 tip = delta / _TipSize + .5;
            float radius = length((tip - .5) * 2);
            // Keep a pixel-wide edge even on a fully hard tip. Evaluate derivatives
            // before branching so diagonal edges have a stable antialiasing width.
            float edge = max(fwidth(radius), 1e-5);
            float coverage = _Hardness >= .9999 ? saturate((1 - radius) / edge + .5) :
                1 - smoothstep(_Hardness, 1, radius);
            if (coverage <= 0) return original;
            float2 selectionUv = map(canvas, _SelectionRow0, _SelectionRow1, _SelectionRow2);
            if (any(selectionUv < 0) || any(selectionUv > 1)) return original;
            coverage *= saturate(tex2D(_Selection, selectionUv).r) * _Flow * _Strength;
            float2 color = _NativeDeposit > .5 && _PeriodicTip < .5 ? i.uv : map(_Center + delta, _ColorRow0, _ColorRow1, _ColorRow2);
            // A frozen image can be reconstructed at the continuous pointer position
            // without accumulating blur: this reconstructed result never replaces it.
            float2 phase = _FrozenCarry > .5 ? _ColorPhase : 0;
            float2 carryUv = (color * _ColorDimensions - _ColorOrigin - phase) / _ColorSize;
            if (any(carryUv < 0) || any(carryUv >= 1)) return original;
            // Bilinear reconstruction is only needed when depositing a canvas sample
            // into a transformed Drawing. Feedback stays aligned to its own grid.
            // Both images stay premultiplied; RGB is not clamped to the SDR range.
            float4 transported = tex2Dlod(_Carry, float4(carryUv, 0, 0));
            return original + (transported - original) * coverage;
        }
        float4 grow(v2f_img i) : SV_Target
        {
            float2 oldUv = (floor(i.uv * _ColorSize) + .5 - (_ColorSize - _OldCarrySize) * .5) / _OldCarrySize;
            if (any(oldUv < 0) || any(oldUv >= 1)) return tex2Dlod(_MainTex, float4(i.uv, 0, 0));
            return tex2Dlod(_Carry, float4(oldUv, 0, 0));
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment pickup
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment deposit
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment grow
            ENDCG
        }
    }
}
