Shader "Hidden/TextureCompositor/HealingBrush"
{
    Properties { _MainTex ("Base", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Patch, _Coverage;
            float4 _PatchRect, _CanvasSize;
            float _WrapPatch;
            float4 _SourceToCanvas0, _SourceToCanvas1, _SourceToCanvas2;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 original = tex2D(_MainTex, i.uv);
                float3 p = float3(i.uv, 1);
                float denominator = dot(_SourceToCanvas2.xyz, p);
                if (abs(denominator) < 1e-8) return original;
                float2 canvas = float2(dot(_SourceToCanvas0.xyz, p), dot(_SourceToCanvas1.xyz, p)) / denominator;
                float2 offset = canvas - _PatchRect.xy / _CanvasSize.xy;
                if (_WrapPatch > .5)
                {
                    if (any(canvas < 0) || any(canvas >= 1)) return original;
                    offset = frac(offset);
                }
                float2 uv = offset * _CanvasSize.xy / _PatchRect.zw;
                if (any(uv < 0) || any(uv > 1)) return original;
                float coverage = saturate(tex2D(_Coverage, uv).r);
                float4 patch = tex2D(_Patch, uv);
                // Both operands are premultiplied. Replacement can also repair alpha holes.
                patch.rgb *= patch.a;
                return lerp(original, patch, coverage);
            }
            ENDCG
        }
    }
}
