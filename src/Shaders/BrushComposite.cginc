#ifndef WHIMTEX_BRUSH_COMPOSITE_INCLUDED
#define WHIMTEX_BRUSH_COMPOSITE_INCLUDED
#include "HdrColor.cginc"
#include "ColorBlend.cginc"

#if defined(WHIMTEX_BRUSH_COMPUTE)
Texture2D<float4> _BrushStroke, _BrushSelection, _BrushOriginalStraight;
SamplerState sampler_BrushStroke, sampler_BrushSelection, sampler_BrushOriginalStraight;
#define BRUSH_SAMPLE(texture, uv) texture.SampleLevel(sampler##texture, uv, 0)
#else
sampler2D _BrushStroke, _BrushSelection, _BrushOriginalStraight;
#define BRUSH_SAMPLE(texture, uv) tex2D(texture, uv)
#endif
float _PendingBrush, _StrokeOpacity, _StrokeMode;
float _BrushErase, _BrushStandard, _BrushStampAccumulation, _BrushPressureOpacity;
float _BrushUseSelection, _BrushSelectionWrap, _BrushStraightFallback;
float _BrushGamma;
float3 _BrushCanvasRow0, _BrushCanvasRow1, _BrushCanvasRow2;
float4 _BrushWriteChannels;
float4 _BrushDirtyRect;

float BrushSelectionCoverage(float2 uv)
{
    if (_BrushUseSelection <= .5) return 1.0;
    float3 p = float3(uv, 1.0);
    float w = dot(_BrushCanvasRow2, p);
    if (abs(w) < 1e-8) return 0.0;
    float2 canvasUv = float2(dot(_BrushCanvasRow0, p), dot(_BrushCanvasRow1, p)) / w;
    if (_BrushSelectionWrap > .5) canvasUv = frac(canvasUv);
    else if (any(canvasUv < 0.0) || any(canvasUv >= 1.0)) return 0.0;
    return saturate(BRUSH_SAMPLE(_BrushSelection, canvasUv).r);
}

float4 CompositePendingBrush(float4 before, float2 uv)
{
    // Storage tiles can extend beyond the submitted stamp bounds.
    [branch] if (any(uv < _BrushDirtyRect.xy) || any(uv > _BrushDirtyRect.zw)) return before;
    float selection = BrushSelectionCoverage(uv);
    if (selection <= 0.0) return before;
    float4 stroke = BRUSH_SAMPLE(_BrushStroke, uv);
    float4 after = before;
    if (_BrushStampAccumulation > .5)
    {
        if (_BrushPressureOpacity > .5) stroke.a = before.a + stroke.a * (1.0 - before.a);
        after = lerp(before, stroke, saturate(_StrokeOpacity));
    }
    else after = CompositeBrushPixel(before, stroke, _StrokeOpacity, _StrokeMode, _BrushErase, _BrushStandard);
    after = lerp(before, after, selection);
    if (all(_BrushWriteChannels > .5)) return after;
    if (_BrushWriteChannels.a < .5 && before.a <= 0.0) return before;
    float3 oldRgb = 0.0;
    if (before.a > .00001) oldRgb = before.rgb / before.a;
    else
    {
        oldRgb = _BrushStraightFallback > .5 ? BRUSH_SAMPLE(_BrushOriginalStraight, uv).rgb : 0.0;
        if (_BrushGamma > .5 && _BrushStraightFallback > 1.5) oldRgb = SpriteDecode(oldRgb);
    }
    float3 nextRgb = after.a > .00001 ? after.rgb / after.a : oldRgb;
    float alpha = _BrushWriteChannels.a > .5 ? after.a : before.a;
    return float4(lerp(oldRgb, nextRgb, _BrushWriteChannels.rgb) * alpha, alpha);
}
#undef BRUSH_SAMPLE
#endif
