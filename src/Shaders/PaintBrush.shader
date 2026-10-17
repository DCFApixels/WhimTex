Shader "Hidden/WhimTex/PaintBrush"
{
    Properties
    {
        _Color ("Linear brush RGBA", Vector) = (1, 1, 1, 1)
        _BrushTip ("Brush Tip", 2D) = "white" {}
        _SrcBlend ("Source Blend", Float) = 1
        _DstBlend ("Destination Blend", Float) = 10
        _BlendOperation ("Blend Operation", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend [_SrcBlend] [_DstBlend]
        BlendOp [_BlendOperation]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local __ BRUSH_DYNAMICS BRUSH_TEXTURE
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            float _HdrBlend;
            #include "ColorBlend.cginc"
            float _StampBlendEnabled, _StampBlendMode, _PressureOpacity;
            sampler2D _StrokeBase;
            // WHIMTEX_BRUSH_PROGRAM
            // WHIMTEX_BRUSH_CAPABILITIES
            #if defined(BRUSH_SEQUENCE)
            float4 _WhimTex_StrokeSequence;
            #endif
            #if defined(BRUSH_MOTION)
            float _WhimTex_BrushStrokeDirection;
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 clipMin : TEXCOORD1;
                float3 clipMax : TEXCOORD2;
                float3 clipData : TEXCOORD3;
                float4 tileData : TEXCOORD4;
                #if defined(BRUSH_DYNAMICS) || defined(BRUSH_TEXTURE)
                float3 color : TEXCOORD5;
                float4 size : TEXCOORD6;
                #endif
                #if defined(BRUSH_SEQUENCE)
                float4 context : TEXCOORD7;
                #endif
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 brushUv : TEXCOORD0;
                float2 canvasUv : TEXCOORD1;
                float3 clipMin : TEXCOORD2;
                float3 clipMax : TEXCOORD3;
                float4 clipData : TEXCOORD4;
                float4 tileData : TEXCOORD5;
                #if defined(BRUSH_DYNAMICS) || defined(BRUSH_TEXTURE)
                float4 color : TEXCOORD6;
                float4 shape : TEXCOORD7;
                #endif
            };

            float4 _Color;
            sampler2D _BrushTip;
            float4 _BrushTip_TexelSize;
            float2 _TipAspect;
            float _TipChannel, _TipDecode, _TipEncodeMask, _TipStandard;
            float _TipSdf;
            sampler2D _BrushSdfGradient;
            float4 _BrushSdfGradient_TexelSize;
            sampler2D _Backdrop;
            float4 _BackdropTransform;
            sampler2D _SelectionMask;
            float _UseSelection, _SelectionWrap;
            float _PrepareStandard;
            float _Hardness;
            float _PencilShape;
            float2 _CanvasSize;
            float2 _PatternCenter;
            float _BrushSize;
            float _CanvasWrap;
            float3 _PaintRow0, _PaintRow1, _PaintRow2;
            float _CoverageMask;
            float3 _MaskRow0, _MaskRow1, _MaskRow2;

            float4 SdfTipGradient(float value)
            {
                float u = saturate(value) * (1.0 - _BrushSdfGradient_TexelSize.x) + .5 * _BrushSdfGradient_TexelSize.x;
                return tex2D(_BrushSdfGradient, float2(u, .5));
            }

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.brushUv = input.uv;
                output.canvasUv = input.vertex.xy;
                output.clipMin = input.clipMin;
                output.clipMax = input.clipMax;
                output.clipData = float4(input.clipData, 0);
                output.tileData = input.tileData;
                #if defined(BRUSH_SEQUENCE)
                // Context is constant over each quad. Reuse spare components, staying within eight interpolators.
                output.brushUv = input.context.zw;
                output.clipData.w = input.context.x;
                output.tileData.z = input.context.y;
                #endif
                #if defined(BRUSH_DYNAMICS) || defined(BRUSH_TEXTURE)
                output.color = float4(input.color, input.size.y);
                output.shape = float4(input.size.x, cos(input.size.z), sin(input.size.z), input.size.w);
                #endif
                return output;
            }

            float4 frag(v2f input) : SV_Target
            {
                float4 color = _Color;
                float brushSize = _BrushSize;
                #if defined(BRUSH_DYNAMICS) || defined(BRUSH_TEXTURE)
                color = input.color;
                brushSize = input.shape.x;
                #endif
                float3 p = float3(input.canvasUv, 1);
                float w = dot(_PaintRow2, p);
                if (abs(w) < 1e-8) discard;
                float2 documentUv = float2(dot(_PaintRow0,p),dot(_PaintRow1,p))/w;
                if (_CoverageMask > .5)
                {
                    float3 maskPoint = float3(documentUv, 1);
                    float maskW = dot(_MaskRow2, maskPoint);
                    if (abs(maskW) < 1e-8) discard;
                    float2 sourceUv = float2(dot(_MaskRow0, maskPoint), dot(_MaskRow1, maskPoint)) / maskW;
                    if (any(sourceUv < 0) || any(sourceUv >= 1)) discard;
                }
                bool tiled = _CanvasWrap > .5;
                float2 delta = documentUv - input.tileData.xy;
                if (tiled)
                {
                    if (any(documentUv < 0) || any(documentUv >= 1)) discard;
                    delta -= floor(delta + .5);
                }
                float2 clipUv = input.tileData.xy + delta;
                float2 brushDelta = delta * _CanvasSize * (2.0 / brushSize);
                #if !defined(BRUSH_TEXTURE)
                // Evaluate derivatives before pattern clipping; width is in normalized tip coordinates.
                float pixelWidth = max(length(ddx(brushDelta)), length(ddy(brushDelta)));
                #endif
                if (input.clipData.x > 0.5 && input.clipData.x < 1.5)
                {
                    if (((!tiled || input.clipMin.x > 0.0) && clipUv.x < input.clipMin.x) ||
                        ((!tiled || input.clipMin.y > 0.0) && clipUv.y < input.clipMin.y) ||
                        ((!tiled || input.clipMax.x < 1.0) && clipUv.x > input.clipMax.x) ||
                        ((!tiled || input.clipMax.y < 1.0) && clipUv.y > input.clipMax.y))
                    {
                        discard;
                    }
                }
                else if (input.clipData.x > 2.5)
                {
                    float2 delta = (clipUv - _PatternCenter) * _CanvasSize;
                    float x = dot(delta, input.clipMin.xy);
                    float y = dot(delta, float2(-input.clipMin.y, input.clipMin.x));
                    if ((input.clipMax.x > 0.5 && x < 0.0) || (input.clipMax.x < -0.5 && x >= 0.0) ||
                        (input.clipMax.y > 0.5 && y < 0.0) || (input.clipMax.y < -0.5 && y >= 0.0))
                        discard;
                }
                else if (input.clipData.x > 1.5)
                {
                    float2 delta = (clipUv - _PatternCenter) * _CanvasSize;
                    float angle = atan2(delta.y, delta.x);
                    float difference = atan2(
                        sin(angle - input.clipData.y),
                        cos(angle - input.clipData.y));
                    if (abs(difference) > input.clipData.z)
                        discard;
                }

                float radius = length(brushDelta);
                float coverage;
                #if defined(BRUSH_TEXTURE)
                brushDelta = float2(input.shape.y * brushDelta.x + input.shape.z * brushDelta.y,
                    -input.shape.z * brushDelta.x + input.shape.y * brushDelta.y);
                float flipY = step(1.5, input.shape.w);
                float flipX = input.shape.w - 2.0 * flipY;
                brushDelta *= 1.0 - 2.0 * float2(flipX, flipY);
                float2 tipUv = brushDelta / _TipAspect * 0.5 + 0.5;
                if (any(tipUv < 0.0) || any(tipUv > 1.0)) discard;
                #if defined(BRUSH_HLSL)
                #if defined(BRUSH_SEQUENCE)
                BrushSequence sequence;
                sequence.seed = (uint)round(_WhimTex_StrokeSequence.x) | ((uint)round(_WhimTex_StrokeSequence.y) << 16);
                sequence.strokeIndex = (uint)round(_WhimTex_StrokeSequence.z) | ((uint)round(_WhimTex_StrokeSequence.w) << 16);
                sequence.stampIndex = (uint)round(input.clipData.w) | ((uint)round(input.tileData.z) << 16);
                sequence.distance = input.brushUv.x;
                sequence.totalDistance = input.brushUv.y;
                #endif
                #if defined(BRUSH_MOTION)
                BrushMotion motion;
                motion.deltaPixels = float2(input.clipMin.z, input.clipMax.z);
                motion.strokeDirection = _WhimTex_BrushStrokeDirection;
                #endif
                float4 tip = WHIMTEX_BRUSH_CALL;
                tip = float4(clamp(tip.rgb, -65504.0, 65504.0), saturate(tip.a));
                #else
                tipUv = clamp(tipUv, _BrushTip_TexelSize.xy * .5, 1.0 - _BrushTip_TexelSize.xy * .5);
                float4 tip = tex2D(_BrushTip, tipUv);
                #endif
                float tipValue = tip.a;
                float tipOpacity = 1.0;
                if (_TipChannel > 0.5 && _TipChannel < 2.5)
                {
                    float3 maskColor = _TipEncodeMask > .5 ? SpriteEncode(tip.rgb) : tip.rgb;
                    float value = saturate(dot(maskColor, float3(.2126, .7152, .0722)));
                    tipValue = _TipChannel > 1.5 ? 1.0 - value : value;
                    tipOpacity = tip.a;
                }
                coverage = tipValue * tipOpacity;
                if (_TipSdf > .5)
                {
                    float4 gradient = SdfTipGradient(1.0 - tipValue);
                    coverage = gradient.a * tipOpacity;
                    color.rgb *= gradient.rgb / max(gradient.a, .00001);
                }
                if (_TipChannel > 2.5)
                {
                    color.rgb *= _TipDecode > .5 ? SpriteDecode(tip.rgb) : tip.rgb;
                }
                color.rgb = _TipStandard > .5 ? saturate(color.rgb) : clamp(color.rgb, -65504.0, 65504.0);
                #else
                if (_PencilShape > 0.5)
                {
                    if (_PencilShape > 2.5) radius = abs(brushDelta.x) + abs(brushDelta.y);
                    else if (_PencilShape > 1.5) radius = max(abs(brushDelta.x), abs(brushDelta.y));
                    if (radius > 1.00001) discard;
                    coverage = 1.0;
                }
                else
                {
                    if (_TipSdf > .5)
                    {
                        if (radius > 1.0) discard;
                        float4 gradient = SdfTipGradient(radius);
                        coverage = gradient.a;
                        color.rgb *= gradient.rgb / max(gradient.a, .00001);
                        color.rgb = _TipStandard > .5 ? saturate(color.rgb) : clamp(color.rgb, -65504.0, 65504.0);
                    }
                    else
                    {
                        float halfPixel = max(pixelWidth * .5, .00001);
                        float hardness = saturate(_Hardness);
                        // Preserve the soft profile; only subpixel falloff widens to a pixel-sized edge.
                        float inner = min(hardness, 1.0 - halfPixel);
                        float outer = max(1.0, hardness + halfPixel);
                        coverage = 1.0 - smoothstep(inner, outer, radius);
                        // Subpixel dabs must fade with their area instead of becoming an opaque AA pixel.
                        coverage *= saturate(4.0 / max(pixelWidth * pixelWidth, .00001));
                    }
                }
                #endif
                if (_UseSelection > 0.5)
                {
                    float2 uv = documentUv;
                    if (_SelectionWrap > 0.5) uv = frac(uv);
                    else if (any(uv < 0.0) || any(uv >= 1.0)) discard;
                    coverage *= tex2D(_SelectionMask, uv).r;
                }
                float alpha = saturate(color.a * coverage);
                if (alpha <= 0.0) discard;
                if (_CoverageMask > .5) return float4(1, 1, 1, alpha);
                if (_PrepareStandard > 0.5)
                {
                    float4 before = tex2D(_Backdrop, input.canvasUv * _BackdropTransform.xy + _BackdropTransform.zw);
                    return float4(clamp(before.rgb, 0.0, before.a), before.a);
                }
                float4 stamp = float4(color.rgb * alpha, alpha);
                if (_PressureOpacity > .5)
                {
                    float4 before = tex2D(_Backdrop, input.canvasUv * _BackdropTransform.xy + _BackdropTransform.zw);
                    float limit = saturate(input.tileData.w);
                    float newAlpha = before.a + max(limit - before.a, 0.0) * alpha;
                    if (_StampBlendEnabled > .5)
                    {
                        float baseAlpha = tex2D(_StrokeBase, input.canvasUv).a;
                        float oldAlpha = baseAlpha + before.a * (1.0 - baseAlpha);
                        float added = max(newAlpha - before.a, 0.0) / max(1.0 - before.a, .00001);
                        float4 result = CompositeBrushPixel(float4(before.rgb, oldAlpha),
                            float4(color.rgb * added, added), 1.0, _StampBlendMode, 0.0, _TipStandard);
                        return float4(result.rgb, newAlpha);
                    }
                    float3 oldColor = before.a > .00001 ? before.rgb / before.a : color.rgb;
                    float3 mixed = lerp(oldColor, color.rgb, alpha * limit);
                    return float4(mixed * newAlpha, newAlpha);
                }
                if (_StampBlendEnabled > .5)
                    return CompositeBrushPixel(tex2D(_Backdrop, input.canvasUv * _BackdropTransform.xy + _BackdropTransform.zw), stamp, 1.0, _StampBlendMode, 0.0, _TipStandard);
                return stamp;
            }
            ENDCG
        }
    }
}
