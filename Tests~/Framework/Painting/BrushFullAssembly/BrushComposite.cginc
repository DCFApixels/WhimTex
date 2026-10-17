#ifndef WHIMTEX_BRUSH_COMPOSITE_INCLUDED
#define WHIMTEX_BRUSH_COMPOSITE_INCLUDED
#include "HdrColor.cginc"
#include "ColorBlend.cginc"

sampler2D _BrushStroke, _BrushSelection, _BrushOriginalStraight;
float _PendingBrush, _StrokeOpacity, _StrokeMode;
float _BrushErase, _BrushStandard, _BrushStampAccumulation, _BrushPressureOpacity;
float _BrushUseSelection, _BrushSelectionWrap, _BrushStraightFallback;
float3 _BrushCanvasRow0, _BrushCanvasRow1, _BrushCanvasRow2;
float4 _BrushWriteChannels;

float4 CompositePendingBrush(float4 before, float2 uv)
{
    float selection = 1.0;
    if (_BrushUseSelection > .5)
    {
        float3 p = float3(uv, 1.0);
        float w = dot(_BrushCanvasRow2, p);
        if (abs(w) < 1e-8) return before;
        float2 canvasUv = float2(dot(_BrushCanvasRow0, p), dot(_BrushCanvasRow1, p)) / w;
        if (_BrushSelectionWrap > .5) canvasUv = frac(canvasUv);
        else if (any(canvasUv < 0.0) || any(canvasUv >= 1.0)) return before;
        selection = saturate(tex2D(_BrushSelection, canvasUv).r);
        if (selection <= 0.0) return before;
    }
    float4 stroke = tex2D(_BrushStroke, uv);
    float4 after;
    if (_BrushStampAccumulation > .5)
    {
        if (_BrushPressureOpacity > .5) stroke.a = before.a + stroke.a * (1.0 - before.a);
        after = lerp(before, stroke, saturate(_StrokeOpacity));
    }
    else after = CompositeBrushPixel(before, stroke, _StrokeOpacity, _StrokeMode, _BrushErase, _BrushStandard);
    after = lerp(before, after, selection);
    if (all(_BrushWriteChannels > .5)) return after;
    if (_BrushWriteChannels.a < .5 && before.a <= 0.0) return before;
    float3 oldRgb;
    if (before.a > .00001) oldRgb = before.rgb / before.a;
    else
    {
        oldRgb = _BrushStraightFallback > .5 ? tex2D(_BrushOriginalStraight, uv).rgb : 0.0;
        #if defined(UNITY_COLORSPACE_GAMMA)
        if (_BrushStraightFallback > 1.5) oldRgb = SpriteDecode(oldRgb);
        #endif
    }
    float3 nextRgb = after.a > .00001 ? after.rgb / after.a : oldRgb;
    float alpha = _BrushWriteChannels.a > .5 ? after.a : before.a;
    return float4(lerp(oldRgb, nextRgb, _BrushWriteChannels.rgb) * alpha, alpha);
}
#endif
