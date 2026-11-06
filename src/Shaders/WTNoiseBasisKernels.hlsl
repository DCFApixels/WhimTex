#ifndef WHIMTEX_WT_NOISE_BASIS_KERNELS_INCLUDED
#define WHIMTEX_WT_NOISE_BASIS_KERNELS_INCLUDED
#include "WTNoiseCommon.hlsl"
#if defined(WHIMTEX_FASTNOISELITE_INCLUDED) && !defined(WHIMTEX_WT_NOISE_HASH)
    #error Include WTNoise.hlsl before FastNoiseLite.hlsl.
#endif
#define WHIMTEX_WT_NOISE_HASH
#include "ThirdParty/FastNoiseLite.hlsl"
#endif
