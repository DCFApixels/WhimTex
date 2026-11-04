#ifndef WHIMTEX_WT_NOISE_FIELDS_INCLUDED
#define WHIMTEX_WT_NOISE_FIELDS_INCLUDED
#include "WTNoiseBasis.hlsl"
#include "WTNoiseGabor.hlsl"

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
float3 WTNoiseInternalDisplacement(float3 p,WTNoiseSettings n,WTNoiseWarpSettings w,int dimensions)
{
    if(w.strength==0) return 0;
    int layout=w.algorithm<2 ? (dimensions==3 ? 3 : 2) : 1;
    int3 savedPeriod=WTNoiseInternalPeriod;
    int3 savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
    fnl_state state=fnlCreateState(n.seed);
    state.domain_warp_type=clamp(w.algorithm,0,2);
    // Warp has its own accumulation modes; count/frequency/gain use common settings.
    WTNoiseFractal f=n.fractal;
    f.mode=w.mode==WT_NOISE_WARP_NONE ? WT_NOISE_NONE : WT_NOISE_FBM;
    float amplitude=w.strength*WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    float3 displacement=0;
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 source=p+(w.mode==WT_NOISE_WARP_PROGRESSIVE ? displacement : float3(0,0,0));
        float3 q=WTNoiseInternalPosition(source,n,frequency,dimensions,layout,period,origin,inputScale);
        WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
        float3 delta=0;
        int seed=WTNoiseInternalSeed(n.seed,octave);
        if(dimensions==3) _fnlDoSingleDomainWarp3D(state,seed,amplitude,1,q.x,q.y,q.z,delta.x,delta.y,delta.z);
        else _fnlDoSingleDomainWarp2D(state,seed,amplitude,1,q.x,q.y,delta.x,delta.y);
        if(layout==2) delta.xy=float2(delta.x+delta.y,delta.y-delta.x)*0.7071067811865475;
        if(dimensions<3) delta.z=0;
        if(dimensions<2) delta.y=0;
        displacement+=delta*WTNoiseInternalFilterWeight(n,inputScale);
        amplitude*=saturate(f.gain); frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return displacement;
}
float WTNoiseDisplacement(float p,WTNoiseSettings n,WTNoiseWarpSettings w) { return WTNoiseInternalDisplacement(float3(p,0,0),n,w,1).x; }
float2 WTNoiseDisplacement(float2 p,WTNoiseSettings n,WTNoiseWarpSettings w) { return WTNoiseInternalDisplacement(float3(p,0),n,w,2).xy; }
float3 WTNoiseDisplacement(float3 p,WTNoiseSettings n,WTNoiseWarpSettings w) { return WTNoiseInternalDisplacement(p,n,w,3); }
float WTNoiseDisplacement(float p,WTNoiseSettings n) { return WTNoiseDisplacement(p,n,WTNoiseWarpDefaults()); }
float2 WTNoiseDisplacement(float2 p,WTNoiseSettings n) { return WTNoiseDisplacement(p,n,WTNoiseWarpDefaults()); }
float3 WTNoiseDisplacement(float3 p,WTNoiseSettings n) { return WTNoiseDisplacement(p,n,WTNoiseWarpDefaults()); }
#endif
