#ifndef WHIMTEX_WT_NOISE_SHARD_INCLUDED
#define WHIMTEX_WT_NOISE_SHARD_INCLUDED
#include "WTNoiseCommon.hlsl"

#include "WTNoiseShardTypes.hlsl"

// Preserve the nested traversal and octave preparation: pruning their one-step
// branches can increase generated GPU work even when the native source is smaller.
// @wt.block(Kernel)
// Independent WT implementation of a shard field: normalized local ramps with
// Gaussian weights and soft-sign shaping. Each dimension has its own lattice.
float WTNoiseInternalShardValue(float3 p,int3 origin,int seed,int3 period,int dimensions,
    float sharpness,float3 dx,float3 dy)
{
    int3 cell=(int3)floor(p);
    float3 local=p-cell;
    cell+=origin;
    float sum=0,total=0;
    int extentY=dimensions>=2 ? 2 : 0,extentZ=dimensions==3 ? 2 : 0;
    // Fixed work: at most 5/25/125 candidates per level in 1D/2D/3D.
    [loop] for(int z=-extentZ;z<=extentZ;z++)
    [loop] for(int y=-extentY;y<=extentY;y++)
    [loop] for(int x=-2;x<=2;x++)
    {
        int3 neighbor=int3(x,y,z);
        uint key=WTNoiseInternalHashCell(cell+neighbor,seed,period);
        float3 center=float3(WTNoiseInternalUnit(key),0,0);
        float3 direction=float3(WTNoiseInternalUnit(WTNoiseInternalHash(key^0x243f6a88u))-0.5,0,0);
        if(dimensions>=2)
        {
            center.y=WTNoiseInternalUnit(WTNoiseInternalHash(key^0x85a308d3u));
            direction.y=WTNoiseInternalUnit(WTNoiseInternalHash(key^0x13198a2eu))-0.5;
        }
        if(dimensions==3)
        {
            center.z=WTNoiseInternalUnit(WTNoiseInternalHash(key^0x03707344u));
            direction.z=WTNoiseInternalUnit(WTNoiseInternalHash(key^0xa4093822u))-0.5;
        }
        float3 delta=local-(float3)neighbor-center;
        float squared=dot(delta,delta);
        // Fade the negligible Gaussian tail to exactly zero before a candidate
        // leaves the search window, avoiding a jump at integer cell boundaries.
        float tail=1-smoothstep(2.25,4.0,squared);
        float weight=exp2(-6.28318530718*squared)*tail;
        float3 slope=direction*sharpness;
        float ramp=dot(slope,delta);
        float sx=dot(slope,dx),sy=dot(slope,dy);
        // Approximate pixel filtering broadens the sharp transition itself;
        // high Sharpness does not prematurely fade the entire pattern to grey.
        float filtered=ramp*rsqrt(1+ramp*ramp+(sx*sx+sy*sy)*(1.0/12.0));
        sum+=weight*filtered;
        total+=weight;
    }
    return 0.5+0.5*sum/max(total,1e-20);
}

// @wt.end
// @wt.block(Sample; Kernel)
float WTNoiseInternalShard(float3 p,WTNoiseSettings n,WTNoiseShardSettings s,int dimensions)
{
    float sharpness=exp2(8*saturate(s.sharpness)*saturate(s.sharpness));
    WTNoiseFractal f=n.fractal;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin;
        float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale);
        float value=0.5;
        if(visibility>0)
            value=WTNoiseInternalShardValue(q,origin,WTNoiseInternalSeed(n.seed,octave),period,dimensions,
                sharpness,n.dpdx*inputScale,n.dpdy*inputScale);
        WTNoiseInternalAccumulateFiltered(value,f,visibility,0.5,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(0.5+0.5*sum);
}

// @wt.end

// Shared input-space period, high/low domain, fractal and explicit footprint rules.
// Filtering is approximate; settings are assumed constant across a pixel.
#define WT_NOISE_SHARD_OVERLOAD(Type,Position,Dimensions) \
float WTNoiseShard(Type p,WTNoiseSettings n,WTNoiseShardSettings s) { return WTNoiseInternalShard(Position,n,s,Dimensions); } \
float WTNoiseShard(Type p,WTNoiseSettings n) { return WTNoiseShard(p,n,WTNoiseShardDefaults()); }
WT_NOISE_SHARD_OVERLOAD(float,float3(p,0,0),1)
WT_NOISE_SHARD_OVERLOAD(float2,float3(p,0),2)
WT_NOISE_SHARD_OVERLOAD(float3,p,3)
#undef WT_NOISE_SHARD_OVERLOAD
#endif
