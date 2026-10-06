Shader "Hidden/TextureCompositor/SmudgeBrush"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _Carry, _Backdrop, _Selection;
        float2 _Center, _TipSize;
        float _Wrap, _PeriodicTip, _Hardness, _Strength, _Flow;
        float4 _PickupRow0, _PickupRow1, _PickupRow2;
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
            float2 canvas = _Center + (i.uv - .5) * _TipSize;
            if (_Wrap > .5) canvas = frac(canvas);
            float2 uv = map(canvas, _PickupRow0, _PickupRow1, _PickupRow2);
            if (any(uv < 0) || any(uv > 1)) return 0;
            return tex2D(_MainTex, uv);
        }
        float4 carry(v2f_img i) : SV_Target
        {
            return lerp(tex2D(_MainTex, i.uv), tex2D(_Carry, i.uv), _Strength);
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
            if (radius >= 1 || any(tip < 0) || any(tip > 1)) return original;
            float coverage = _Hardness >= .9999 ? 1 : 1 - smoothstep(_Hardness, 1, radius);
            float2 selectionUv = map(canvas, _SelectionRow0, _SelectionRow1, _SelectionRow2);
            if (any(selectionUv < 0) || any(selectionUv > 1)) return original;
            coverage *= saturate(tex2D(_Selection, selectionUv).r) * _Flow;
            // Both images stay premultiplied; RGB is not clamped to the SDR range.
            return lerp(original, tex2D(_Carry, tip), coverage);
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
            #pragma fragment carry
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
    }
}
