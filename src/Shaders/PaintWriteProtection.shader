Shader "Hidden/WhimTex/PaintWriteProtection"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment protect
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            sampler2D _MainTex, _PaintBefore, _OriginalStraight;
            float4 _WriteChannels;
            float _StraightFallback;
            float4 protect(v2f_img input) : SV_Target
            {
                float4 before = tex2D(_PaintBefore, input.uv);
                float4 after = tex2D(_MainTex, input.uv);
                if (_WriteChannels.a < .5 && before.a <= 0.0) return before;
                float3 oldRgb;
                if (before.a > 0.00001) oldRgb = before.rgb / before.a;
                else
                {
                    oldRgb = _StraightFallback > .5 ? tex2D(_OriginalStraight, input.uv).rgb : 0.0;
                    #if defined(UNITY_COLORSPACE_GAMMA)
                    if (_StraightFallback > 1.5) oldRgb = SpriteDecode(oldRgb);
                    #endif
                }
                float3 nextRgb = after.a > 0.00001 ? after.rgb / after.a : oldRgb;
                float alpha = _WriteChannels.a > .5 ? after.a : before.a;
                float3 rgb = lerp(oldRgb, nextRgb, _WriteChannels.rgb);
                return float4(rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
