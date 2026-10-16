#ifndef WHIMTEX_GRADIENT_PALETTE_INCLUDED
#define WHIMTEX_GRADIENT_PALETTE_INCLUDED
#include "HdrColor.cginc"
sampler2D _GradientPalette;
float4 _GradientPalette_TexelSize, _GradientIntervals[129], _GradientStart;
int _GradientWrapMode, _GradientIntervalCount;

float WrapGradientTime(float t, int wrapMode)
{
    if (wrapMode == 1) return frac(t);
    if (wrapMode == 2)
    {
        float mirrored = t - floor(t * .5) * 2;
        return mirrored <= 1 ? mirrored : 2 - mirrored;
    }
    return saturate(t);
}
float4 SampleGradientPaletteAt(float coordinate, sampler2D palette, float4 texelSize,
    float4 intervals[129], int intervalCount, float4 startColor, int wrapMode)
{
    float t = WrapGradientTime(coordinate, wrapMode);
    int row = 0;
    [loop] for (int i = 0; i < intervalCount - 1; i++)
        if (t > intervals[i].y) row = i + 1;
    float4 interval = intervals[row];
    float local = saturate((t - interval.x) * interval.z);
    float2 uv = float2(lerp(.5 * texelSize.x,
        1 - .5 * texelSize.x, local), (row + .5) * texelSize.y);
    float4 color = t <= 0 ? startColor : tex2Dlod(palette, float4(uv, 0, 0));
    color.rgb = SpriteDecode(color.rgb);
    return color;
}
float4 SampleGradientPalette(float coordinate)
{
    return SampleGradientPaletteAt(coordinate, _GradientPalette, _GradientPalette_TexelSize,
        _GradientIntervals, _GradientIntervalCount, _GradientStart, _GradientWrapMode);
}
#endif
