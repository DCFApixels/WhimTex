#ifndef WHIMTEX_WT_NOISE_GRAIN_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_GRAIN_TYPES_INCLUDED

// Shared guarded declarations also support graphs that include authored HLSL
// and therefore fall back to the complete WT library.
Texture2D<float4> _WTNoiseBlue1D, _WTNoiseBlue2D;

struct WTNoiseGrainSettings
{
    int seed;
    float2 imageSize;
    float2 offset;
    float grainSize;
    float direction;
    bool oneD;
    bool color;
    bool inverted;
};
WTNoiseGrainSettings WTNoiseGrainDefaults(float2 imageSize,int seed=1337)
{
    WTNoiseGrainSettings n;
    n.seed=seed; n.imageSize=imageSize; n.offset=0; n.grainSize=1; n.direction=0;
    n.oneD=false; n.color=false; n.inverted=false;
    return n;
}
#endif
