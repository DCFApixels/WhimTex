#ifndef WHIMTEX_WT_NOISE_WAVELET_INCLUDED
#define WHIMTEX_WT_NOISE_WAVELET_INCLUDED
#include "WTNoiseCommon.hlsl"

struct WTNoiseWaveletSettings { float frequency; float phase; };
WTNoiseWaveletSettings WTNoiseWaveletDefaults()
{ WTNoiseWaveletSettings w; w.frequency=2; w.phase=0; return w; }

float WTNoiseInternalWaveletValue(float3 p,int3 origin,int seed,int3 period,int dimensions,WTNoiseWaveletSettings w,float3 dx,float3 dy)
{
    float sum=0;
    [unroll] for(int grid=0;grid<2;grid++)
    {
        float3 position=p+0.5*grid;
        int3 cell=(int3)floor(position);
        float3 local=position-cell-0.5;
        if(dimensions<3) { local.z=0; cell.z=0; }
        if(dimensions<2) { local.y=0; cell.y=0; }
        float envelope=max(1-2*length(local),0);
        uint key=WTNoiseInternalHashCell(cell+origin,WTNoiseInternalSeed(seed,grid,0x6c8e9cf5),period);
        float u=WTNoiseInternalUnit(key);
        float3 direction=float3(u<0.5 ? -1 : 1,0,0);
        if(dimensions>=2)
        {
            float sine,cosine; sincos(6.283185307179586*u,sine,cosine);
            direction=float3(cosine,sine,0);
            if(dimensions==3)
            {
                float z=2*WTNoiseInternalUnit(WTNoiseInternalHash(key^0x9e3779b9u))-1;
                direction=float3(direction.xy*sqrt(max(1-z*z,0)),z);
            }
        }
        float frequency=abs(w.frequency);
        float fx=dot(direction,dx)*frequency,fy=dot(direction,dy)*frequency;
        float attenuation=exp(-1.6449340668482264*(fx*fx+fy*fy));
        float wave=sin(6.283185307179586*frac(dot(direction,local)*frequency+frac(w.phase)));
        sum+=envelope*(0.5+0.5*wave*attenuation);
    }
    return saturate(sum);
}
float WTNoiseInternalWaveletMean(int dimensions,WTNoiseWaveletSettings w,WTNoiseFractal f)
{
    // Exact envelope mean for zero phase; the odd carrier integrates to zero.
    float envelope=dimensions==1 ? 0.5 : dimensions==2 ? 0.2617993877991494 : 0.1308996938995747;
    if(f.mode==WT_NOISE_NONE || f.mode==WT_NOISE_FBM)
    {
        if(frac(w.phase)==0 || frac(w.phase)==0.5) return envelope;
        if(w.frequency==0) return envelope*(1+sin(6.283185307179586*frac(w.phase)));
    }
    // Small deterministic ensemble estimates the phase-dependent/folded mean.
    // Independent of position and seed, so minification cannot shimmer by cell.
    float mean=0;
    [loop] for(int i=0;i<32;i++)
    {
        float3 p=frac((i+0.5)*float3(0.754877666,0.569840296,0.438579021));
        if(dimensions<3) p.z=0;
        if(dimensions<2) p.y=0;
        float value=WTNoiseInternalWaveletValue(p,0,i*6271,0,dimensions,w,0,0),ignored;
        mean+=WTNoiseInternalFractalValue(value,f,ignored)*(1.0/32);
    }
    return mean;
}
float WTNoiseInternalWavelet(float3 p,WTNoiseSettings n,WTNoiseWaveletSettings w,int dimensions)
{
    WTNoiseFractal f=n.fractal;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f),mean=0; bool meanReady=false;
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility<1 && !meanReady) { mean=WTNoiseInternalWaveletMean(dimensions,w,f); meanReady=true; }
        if(visibility>0) value=WTNoiseInternalWaveletValue(q,origin,WTNoiseInternalSeed(n.seed,octave),period,dimensions,w,n.dpdx*inputScale,n.dpdy*inputScale);
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(0.5+0.5*sum);
}
#define WT_NOISE_WAVELET_OVERLOAD(Type,Position,Dimensions) \
float WTNoiseWavelet(Type p,WTNoiseSettings n,WTNoiseWaveletSettings w) { return WTNoiseInternalWavelet(Position,n,w,Dimensions); } \
float WTNoiseWavelet(Type p,WTNoiseSettings n) { return WTNoiseWavelet(p,n,WTNoiseWaveletDefaults()); }
WT_NOISE_WAVELET_OVERLOAD(float,float3(p,0,0),1)
WT_NOISE_WAVELET_OVERLOAD(float2,float3(p,0),2)
WT_NOISE_WAVELET_OVERLOAD(float3,p,3)
#undef WT_NOISE_WAVELET_OVERLOAD
#endif
