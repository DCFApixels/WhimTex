#ifndef WHIMTEX_WT_NOISE_GABORFIELDS_INCLUDED
#define WHIMTEX_WT_NOISE_GABORFIELDS_INCLUDED
#include "WTNoiseGabor.hlsl"

// Gabor differentiates its normalized, filtered fractal analytically in input units.
// No finite-difference step and no unit-length normalization are applied.
float WTNoiseGradient(float p,WTNoiseSettings n,WTNoiseGaborSettings g) { return WTNoiseInternalGaborGradient(float3(p,0,0),n,g,1).x; }
float2 WTNoiseGradient(float2 p,WTNoiseSettings n,WTNoiseGaborSettings g) { return WTNoiseInternalGaborGradient(float3(p,0),n,g,2).xy; }
float3 WTNoiseGradient(float3 p,WTNoiseSettings n,WTNoiseGaborSettings g) { return WTNoiseInternalGaborGradient(p,n,g,3); }
float2 WTNoiseCurl(float2 p,WTNoiseSettings n,WTNoiseGaborSettings g) { float2 gradient=WTNoiseGradient(p,n,g); return float2(gradient.y,-gradient.x); }
float3 WTNoiseCurl(float3 p,WTNoiseSettings n,WTNoiseGaborSettings g)
{
    float3 gx=WTNoiseGradient(p,n,g);
    n.seed^=0x68bc21eb;
    float3 gy=WTNoiseGradient(p,n,g);
    n.seed^=0x68bc21eb^0x63d83595;
    float3 gz=WTNoiseGradient(p,n,g);
    return float3(gz.y-gy.z,gx.z-gz.x,gy.x-gx.y);
}

#endif
