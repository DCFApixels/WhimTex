#ifndef WHIMTEX_WT_NOISE_WARP_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_WARP_TYPES_INCLUDED
#include "WTNoiseCommon.hlsl"

#define WT_NOISE_WARP_OPENSIMPLEX2 0
#define WT_NOISE_WARP_OPENSIMPLEX2_REDUCED 1
#define WT_NOISE_WARP_BASIC_GRID 2
#define WT_NOISE_WARP_NONE 0
#define WT_NOISE_WARP_PROGRESSIVE 1
#define WT_NOISE_WARP_INDEPENDENT 2
struct WTNoiseWarpSettings
{
    int algorithm;
    int mode;
    float strength;
};
WTNoiseWarpSettings WTNoiseWarpDefaults()
{
    WTNoiseWarpSettings w;
    w.algorithm=WT_NOISE_WARP_OPENSIMPLEX2; w.mode=WT_NOISE_WARP_NONE;
    w.strength=0.1;
    return w;
}

#endif
