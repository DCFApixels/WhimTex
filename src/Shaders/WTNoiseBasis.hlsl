#ifndef WHIMTEX_WT_NOISE_BASIS_INCLUDED
#define WHIMTEX_WT_NOISE_BASIS_INCLUDED
#include "WTNoiseCommon.hlsl"
#if defined(WHIMTEX_FASTNOISELITE_INCLUDED) && !defined(WHIMTEX_WT_NOISE_HASH)
    #error Include WTNoise.hlsl before FastNoiseLite.hlsl.
#endif
#define WHIMTEX_WT_NOISE_HASH
#include "ThirdParty/FastNoiseLite.hlsl"
#include "WTNoiseDerivatives.hlsl"

float WTNoiseInternalBasis1D(float p,int seed,int kind)
{
    int i=(int)floor(p);
    float t=p-floor(p);
    if(kind==WT_NOISE_PERLIN)
    {
        float a=(WTNoiseInternalHash(asuint(_fnlHash2D(seed,i*PRIME_X,0)))&1u)!=0 ? t : -t;
        float b=(WTNoiseInternalHash(asuint(_fnlHash2D(seed,(i+1)*PRIME_X,0)))&1u)!=0 ? t-1 : 1-t;
        return lerp(a,b,_fnlInterpQuintic(t))*2;
    }
    float a=_fnlValCoord2D(seed,i*PRIME_X,0),b=_fnlValCoord2D(seed,(i+1)*PRIME_X,0);
    if(kind==WT_NOISE_VALUE_CUBIC)
        return _fnlCubicLerp(_fnlValCoord2D(seed,(i-1)*PRIME_X,0),a,b,_fnlValCoord2D(seed,(i+2)*PRIME_X,0),t)/1.5;
    return lerp(a,b,_fnlInterpHermite(t));
}
float WTNoiseInternalBasis(float3 p,WTNoiseSettings n,WTNoiseFractal f,int dimensions,int kind)
{
    int layout=kind<2 ? (dimensions==3 ? 3 : 2) : 1;
    int3 savedPeriod=WTNoiseInternalPeriod;
    int3 savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    fnl_state state=fnlCreateState(n.seed);
    state.frequency=1; state.noise_type=kind;
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,layout,period,origin,inputScale);
        WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
        int seed=WTNoiseInternalSeed(n.seed,octave);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility>0)
        {
            if(dimensions==1 && kind>=3) value=WTNoiseInternalBasis1D(q.x,seed,kind);
            else if(dimensions==3) value=_fnlGenNoiseSingle3D(state,seed,q.x,q.y,q.z);
            else value=_fnlGenNoiseSingle2D(state,seed,q.x,q.y);
        }
        WTNoiseInternalAccumulateFiltered(value*0.5+0.5,f,visibility,0.5,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return saturate(sum*0.5+0.5);
}
float3 WTNoiseInternalBasisGradient(float3 p,WTNoiseSettings n,WTNoiseFractal f,int dimensions,int kind)
{
    int layout=kind<2 ? (dimensions==3 ? 3 : 2) : 1;
    int3 savedPeriod=WTNoiseInternalPeriod,savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float3 gradient=0,amplitudeGradient=0;
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,layout,period,origin,inputScale);
        WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
        float3 derivative=0;
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility>0) value=WTNoiseInternalBasisDerivative(q,WTNoiseInternalSeed(n.seed,octave),dimensions,kind,derivative);
        WTNoiseInternalAccumulateFiltered(value*0.5+0.5,derivative*inputScale*0.5,f,
            visibility,0.5,sum,amplitude,gradient,amplitudeGradient);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return sum>-1 && sum<1 ? gradient*0.5 : float3(0,0,0);
}
#define WT_NOISE_BASIS_FUNCTION(Name,Kind) \
float Name(float p,WTNoiseSettings n) { return WTNoiseInternalBasis(float3(p,0,0),n,n.fractal,1,Kind); } \
float Name(float2 p,WTNoiseSettings n) { return WTNoiseInternalBasis(float3(p,0),n,n.fractal,2,Kind); } \
float Name(float3 p,WTNoiseSettings n) { return WTNoiseInternalBasis(p,n,n.fractal,3,Kind); }
WT_NOISE_BASIS_FUNCTION(WTNoisePerlin,WT_NOISE_PERLIN)
WT_NOISE_BASIS_FUNCTION(WTNoiseValue,WT_NOISE_VALUE)
WT_NOISE_BASIS_FUNCTION(WTNoiseValueCubic,WT_NOISE_VALUE_CUBIC)
WT_NOISE_BASIS_FUNCTION(WTNoiseOpenSimplex2,WT_NOISE_OPENSIMPLEX2)
WT_NOISE_BASIS_FUNCTION(WTNoiseOpenSimplex2S,WT_NOISE_OPENSIMPLEX2S)
#undef WT_NOISE_BASIS_FUNCTION
#endif
