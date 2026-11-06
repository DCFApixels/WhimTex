#ifndef WHIMTEX_WT_NOISE_BASISFIELDS_INCLUDED
#define WHIMTEX_WT_NOISE_BASISFIELDS_INCLUDED
#include "WTNoiseBasis.hlsl"

float3 WTNoiseInternalGradient(float3 p,WTNoiseSettings n,WTNoiseFractal f,int dimensions,int kind)
{
    return WTNoiseInternalBasisGradient(p,n,f,dimensions,kind);
}
float3 WTNoiseInternalCurl(float3 p,WTNoiseSettings n,WTNoiseFractal f,int kind)
{
    float3 gx=WTNoiseInternalGradient(p,n,f,3,kind);
    n.seed^=0x68bc21eb;
    float3 gy=WTNoiseInternalGradient(p,n,f,3,kind);
    n.seed^=0x68bc21eb^0x63d83595;
    float3 gz=WTNoiseInternalGradient(p,n,f,3,kind);
    return float3(gz.y-gy.z,gx.z-gz.x,gy.x-gx.y);
}
// Derivative operators share the same domain/fractal settings as their scalar basis.
float WTNoiseGradient(float p,WTNoiseSettings n,int algorithm=WT_NOISE_PERLIN) { return WTNoiseInternalGradient(float3(p,0,0),n,n.fractal,1,algorithm).x; }
float2 WTNoiseGradient(float2 p,WTNoiseSettings n,int algorithm=WT_NOISE_PERLIN) { return WTNoiseInternalGradient(float3(p,0),n,n.fractal,2,algorithm).xy; }
float3 WTNoiseGradient(float3 p,WTNoiseSettings n,int algorithm=WT_NOISE_PERLIN) { return WTNoiseInternalGradient(p,n,n.fractal,3,algorithm); }
float2 WTNoiseCurl(float2 p,WTNoiseSettings n,int algorithm=WT_NOISE_PERLIN) { float2 g=WTNoiseGradient(p,n,algorithm); return float2(g.y,-g.x); }
float3 WTNoiseCurl(float3 p,WTNoiseSettings n,int algorithm=WT_NOISE_PERLIN) { return WTNoiseInternalCurl(p,n,n.fractal,algorithm); }

#endif
