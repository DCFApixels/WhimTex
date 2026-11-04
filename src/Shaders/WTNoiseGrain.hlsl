#ifndef WHIMTEX_WT_NOISE_GRAIN_INCLUDED
#define WHIMTEX_WT_NOISE_GRAIN_INCLUDED
#include "WTNoiseCommon.hlsl"

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
int2 WTNoiseInternalGrainCell(float2 uv,WTNoiseGrainSettings n)
{
    float2 pixel=uv*n.imageSize;
    if(n.oneD)
    {
        float angle=radians(n.direction);
        float2 axis=float2(cos(angle),sin(angle));
        axis=float2(abs(axis.x)<0.000001 ? 0 : axis.x,abs(axis.y)<0.000001 ? 0 : axis.y);
        pixel=float2(dot(pixel-0.5*n.imageSize,axis),0);
    }
    return (int2)floor((pixel+n.offset)/max(n.grainSize,max(max(n.imageSize.x,n.imageSize.y)*1e-9,1e-6)));
}
float3 WTNoiseWhite(float2 uv,WTNoiseGrainSettings n)
{
    int2 cell=WTNoiseInternalGrainCell(uv,n);
    uint key=WTNoiseInternalHash(asuint(cell.x))^WTNoiseInternalHash(asuint(cell.y)^0x9e3779b9u)
        ^WTNoiseInternalHash(asuint(n.seed)^0x68bc21ebu);
    float r=WTNoiseInternalUnit(WTNoiseInternalHash(key));
    float3 value=n.color ? float3(r,WTNoiseInternalUnit(WTNoiseInternalHash(key^0xa511e9b3u)),
        WTNoiseInternalUnit(WTNoiseInternalHash(key^0x63d83595u))) : r.xxx;
    return n.inverted ? 1-value : value;
}
float WTNoiseInternalBlue(int2 cell,uint channel,WTNoiseGrainSettings n)
{
    uint key=WTNoiseInternalHash(asuint(n.seed)^(0x68bc21ebu+channel*0x9e3779b9u));
    float4 ranks;
    if(n.oneD)
    {
        key=WTNoiseInternalHash(key^asuint(cell.y));
        int x=(key&256u)!=0 ? -cell.x : cell.x;
        ranks=_WTNoiseBlue1D.Load(int3((x+(int)(key&255u))&255,0,0));
    }
    else
    {
        if((key&16384u)!=0) cell=cell.yx;
        if((key&32768u)!=0) cell.x=-cell.x;
        if((key&65536u)!=0) cell.y=-cell.y;
        cell=(cell+int2(key&127u,(key>>7)&127u))&127;
        ranks=_WTNoiseBlue2D.Load(int3(cell,0));
    }
    return channel==0 ? ranks.r : channel==1 ? ranks.g : ranks.b;
}
float3 WTNoiseBlue(float2 uv,WTNoiseGrainSettings n)
{
    int2 cell=WTNoiseInternalGrainCell(uv,n);
    float r=WTNoiseInternalBlue(cell,0,n);
    float3 value=n.color ? float3(r,WTNoiseInternalBlue(cell,1,n),WTNoiseInternalBlue(cell,2,n)) : r.xxx;
    return n.inverted ? 1-value : value;
}
#endif
