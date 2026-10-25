#ifndef WHIMTEX_FREEFORM_GRADIENT_INCLUDED
#define WHIMTEX_FREEFORM_GRADIENT_INCLUDED
#include "GradientColorModel.cginc"
int _FreeformCount, _FreeformColorMode;
float4 _FreeformPositions[64], _FreeformColors[64];

float FreeformDistance(float2 uv, float4 control)
{
    float2 delta = uv - control.xy;
    return max(1e-8, dot(delta, delta) * control.z);
}

float4 SampleFreeform(float2 uv, int count, float4 positions[64], float4 colors[64], int colorMode)
{
    float nearest = 3.402823e+38;
    [loop] for (int i = 0; i < count; i++) nearest = min(nearest, FreeformDistance(uv, positions[i]));
    float4 color = 0;
    float total = 0, chroma = 0;
    [loop] for (int j = 0; j < count; j++)
    {
        float weight = nearest / FreeformDistance(uv, positions[j]);
        weight *= weight;
        color += colors[j] * weight;
        if (colorMode == 3) chroma += positions[j].w * weight;
        total += weight;
    }
    color /= max(total, 1e-8);
    color.rgb = GradientModelToLinear(color.rgb, colorMode, chroma / max(total, 1e-8));
    return color;
}

float4 FreeformColor(float2 uv) { return SampleFreeform(uv, _FreeformCount, _FreeformPositions, _FreeformColors, _FreeformColorMode); }
#ifdef WHIMTEX_FREEFORM_STROKE
int _StrokeFreeformCount, _StrokeFreeformColorMode;
float4 _StrokeFreeformPositions[64], _StrokeFreeformColors[64];
float4 StrokeFreeformColor(float2 uv) { return SampleFreeform(uv, _StrokeFreeformCount, _StrokeFreeformPositions, _StrokeFreeformColors, _StrokeFreeformColorMode); }
#endif
#endif
