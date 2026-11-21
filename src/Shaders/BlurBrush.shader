Shader "Hidden/WhimTex/BlurBrush"
{
    Properties
    {
        _MainTex ("Base", 2D) = "black" {}
        _BlurTex ("Blurred", 2D) = "black" {}
        _MaskTex ("Mask", 2D) = "black" {}
        _Strength ("Strength", Range(0,1)) = 1
        _BlurMap ("Blur region", Vector) = (0,0,1,1)
        _BlurClamp ("Blur bounds", Vector) = (0,0,1,1)
        _MaskMap ("Mask region", Vector) = (0,0,1,1)
        _Channels ("Write channels", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            sampler2D _MainTex, _BlurTex, _MaskTex, _Before, _OriginalStraight, _Selection;
            float4 _BlurTex_TexelSize, _BlurMap, _Region, _Channels, _MaskMap, _BlurClamp;
            float3 _CanvasRow0, _CanvasRow1, _CanvasRow2;
            float _Strength, _UseSelection, _Wrap, _Decode;
            float4 SampleBlur(float2 uv) { return tex2D(_BlurTex, clamp(uv, _BlurClamp.xy, _BlurClamp.zw)); }
            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = _BlurMap.xy + i.uv * _BlurMap.zw;
                float2 d = _BlurTex_TexelSize.xy;
                float4 b = SampleBlur(uv) * 4.0;
                b += SampleBlur(uv + float2(d.x, 0));
                b += SampleBlur(uv - float2(d.x, 0));
                b += SampleBlur(uv + float2(0, d.y));
                b += SampleBlur(uv - float2(0, d.y));
                b += SampleBlur(uv + d);
                b += SampleBlur(uv - d);
                b += SampleBlur(uv + float2(d.x, -d.y));
                b += SampleBlur(uv + float2(-d.x, d.y));
                b /= 12.0;
                float2 maskUv = _MaskMap.xy + i.uv * _MaskMap.zw;
                float mask = all(maskUv >= 0) && all(maskUv < 1) ? saturate(tex2D(_MaskTex, maskUv).r * _Strength) : 0;
                float2 sourceUv = _Region.xy + i.uv * _Region.zw;
                if (_UseSelection > .5)
                {
                    float3 p = float3(sourceUv, 1);
                    float2 canvasUv = float2(dot(_CanvasRow0, p), dot(_CanvasRow1, p)) / dot(_CanvasRow2, p);
                    if (_Wrap > .5) canvasUv = frac(canvasUv);
                    mask *= all(canvasUv >= 0) && all(canvasUv <= 1) ? tex2D(_Selection, canvasUv).r : 0;
                }
                float4 current = tex2D(_MainTex, i.uv);
                if (mask <= 0) return current;
                // Match the Half publication before the existing channel-protection step.
                float4 after = lerp(current, b, mask);
                if (all(_Channels > .5)) return after;
                after = f16tof32(f32tof16(after));
                float4 before = tex2D(_Before, i.uv);
                if (_Channels.a < .5 && before.a <= 0) return before;
                float3 original = all(sourceUv >= 0) && all(sourceUv <= 1) ? tex2D(_OriginalStraight, sourceUv).rgb : 0;
                if (_Decode > .5) original = SpriteDecode(original);
                float3 oldRgb = before.a > .00001 ? before.rgb / before.a : original;
                float3 nextRgb = after.a > .00001 ? after.rgb / after.a : oldRgb;
                float alpha = _Channels.a > .5 ? after.a : before.a;
                return float4(lerp(oldRgb, nextRgb, _Channels.rgb) * alpha, alpha);
            }
            ENDCG
        }
        Pass
        {
            Name "GatherCanvas"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment gatherCanvas
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _SampleRegion;
            float3 _CanvasRow0, _CanvasRow1, _CanvasRow2;
            float _Wrap;

            float4 gatherCanvas(v2f_img i) : SV_Target
            {
                float2 sourceUv = _SampleRegion.xy + i.uv * _SampleRegion.zw;
                if (_Wrap > .5) sourceUv = frac(sourceUv);
                float3 p = float3(sourceUv, 1);
                float w = dot(_CanvasRow2, p);
                if (abs(w) < 1e-8) return 0;
                float2 canvasUv = float2(dot(_CanvasRow0, p), dot(_CanvasRow1, p)) / w;
                return tex2Dlod(_MainTex, float4(canvasUv, 0, 0));
            }
            ENDCG
        }
    }
}
