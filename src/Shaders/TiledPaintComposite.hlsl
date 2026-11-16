#ifndef WHIMTEX_TILED_PAINT_COMPOSITE
#define WHIMTEX_TILED_PAINT_COMPOSITE
Texture2D<float4> _Before, _OriginalStraight;
SamplerState sampler_OriginalStraight;
float4 _BeforeOffset, _BrushSurfaceSize, _LogicalOffset, _WriteChannels;
float _HasBefore, _HasOriginalStraight, _OriginalStraightGamma, _HdrBlend;
#define WHIMTEX_BRUSH_COMPUTE
#include "Packages/com.dcfapixels.whimtex/src/Shaders/BrushComposite.cginc"

float4 TiledCompositeValue(float4 before, float4 stroke, float2 sourcePixel)
{
    // Use the shared blend and selection functions directly. Channel protection
    // below uses logical source UV, while pending ink uses physical page UV.
    float selection = BrushSelectionCoverage(sourcePixel);
    if (selection <= 0) return before;
    float4 after = before;
    if (_BrushStampAccumulation > .5)
    {
        if (_BrushPressureOpacity > .5) stroke.a = before.a + stroke.a * (1 - before.a);
        after = lerp(before, stroke, saturate(_StrokeOpacity));
    }
    else after = CompositeBrushPixel(before, stroke, _StrokeOpacity, _StrokeMode, _BrushErase, _BrushStandard);
    after = lerp(before, after, selection);
    if (all(_WriteChannels > .5)) return after;
    if (_WriteChannels.a < .5 && before.a <= 0) return before;
    float3 oldRgb = 0;
    if (before.a > .00001) oldRgb = before.rgb / before.a;
    else if (_HasOriginalStraight > .5)
    {
        float2 originalUv = sourcePixel * _LogicalOffset.zw;
        if (all(originalUv >= 0) && all(originalUv < 1))
        {
            oldRgb = _OriginalStraight.SampleLevel(sampler_OriginalStraight, originalUv, 0).rgb;
            if (_OriginalStraightGamma > .5) oldRgb = SpriteDecode(oldRgb);
        }
    }
    float3 nextRgb = after.a > .00001 ? after.rgb / after.a : oldRgb;
    float alpha = _WriteChannels.a > .5 ? after.a : before.a;
    return float4(lerp(oldRgb, nextRgb, _WriteChannels.rgb) * alpha, alpha);
}
float4 TiledComposite(uint2 pixel)
{
    int2 beforePixel = _HasBefore > .5 ? (int2)pixel + (int2)_BeforeOffset.xy : int2(0,0);
    float4 before = _Before.Load(int3(beforePixel, 0)) * _HasBefore;
    float2 uv = (pixel + .5) * _BrushSurfaceSize.zw;
    float4 stroke = _BrushStroke.SampleLevel(sampler_BrushStroke, uv * _BrushStrokeMap.xy + _BrushStrokeMap.zw, 0);
    return TiledCompositeValue(before, stroke, (int2)pixel + (int2)_LogicalOffset.xy + .5);
}
#endif
