#ifndef WHIMTEX_WT_NOISE_GRAIN_INCLUDED
#define WHIMTEX_WT_NOISE_GRAIN_INCLUDED
#include "WTNoiseCommon.hlsl"

#include "WTNoiseGrainTypes.hlsl"

// @wt.feature(Dim1)
// @wt.feature(Dim2)
// @wt.feature(Color)
// @wt.feature(Inverted)

// @wt.block(Cell)
int2 WTNoiseInternalGrainCell(float2 uv,WTNoiseGrainSettings n)
{
    float2 pixel=uv*n.imageSize;
// @wt.if(Dim1)
    if(n.oneD)
    {
        float angle=radians(n.direction);
        float2 axis=float2(cos(angle),sin(angle));
        axis=float2(abs(axis.x)<0.000001 ? 0 : axis.x,abs(axis.y)<0.000001 ? 0 : axis.y);
        pixel=float2(dot(pixel-0.5*n.imageSize,axis),0);
    }
// @wt.endif
    return (int2)floor((pixel+n.offset)/max(n.grainSize,max(max(n.imageSize.x,n.imageSize.y)*1e-9,1e-6)));
}
// @wt.end
// @wt.block(White; Cell)
float3 WTNoiseWhite(float2 uv,WTNoiseGrainSettings n)
{
    int2 cell=WTNoiseInternalGrainCell(uv,n);
    uint key=WTNoiseInternalHash(asuint(cell.x))^WTNoiseInternalHash(asuint(cell.y)^0x9e3779b9u)
        ^WTNoiseInternalHash(asuint(n.seed)^0x68bc21ebu);
    float r=WTNoiseInternalUnit(WTNoiseInternalHash(key));
    float3 value=r.xxx;
// @wt.if(Color)
    if(n.color) value=float3(r,WTNoiseInternalUnit(WTNoiseInternalHash(key^0xa511e9b3u)),
        WTNoiseInternalUnit(WTNoiseInternalHash(key^0x63d83595u)));
// @wt.endif
// @wt.if(Inverted)
    if(n.inverted) return 1-value;
// @wt.endif
    return value;
}
// @wt.end
// @wt.block(BlueKernel)
float WTNoiseInternalBlue(int2 cell,uint channel,WTNoiseGrainSettings n)
{
    uint key=WTNoiseInternalHash(asuint(n.seed)^(0x68bc21ebu+channel*0x9e3779b9u));
    float4 ranks=0;
// @wt.if(Dim1)
    if(n.oneD)
    {
        key=WTNoiseInternalHash(key^asuint(cell.y));
        int x=(key&256u)!=0 ? -cell.x : cell.x;
        ranks=_WTNoiseBlue1D.Load(int3((x+(int)(key&255u))&255,0,0));
    }
// @wt.endif
// @wt.if(Dim2)
    if(!n.oneD)
    {
        if((key&16384u)!=0) cell=cell.yx;
        if((key&32768u)!=0) cell.x=-cell.x;
        if((key&65536u)!=0) cell.y=-cell.y;
        cell=(cell+int2(key&127u,(key>>7)&127u))&127;
        ranks=_WTNoiseBlue2D.Load(int3(cell,0));
    }
// @wt.endif
    return channel==0 ? ranks.r : channel==1 ? ranks.g : ranks.b;
}
// @wt.end
// @wt.block(Blue; Cell, BlueKernel)
float3 WTNoiseBlue(float2 uv,WTNoiseGrainSettings n)
{
    int2 cell=WTNoiseInternalGrainCell(uv,n);
    float r=WTNoiseInternalBlue(cell,0,n);
    float3 value=r.xxx;
// @wt.if(Color)
    if(n.color) value=float3(r,WTNoiseInternalBlue(cell,1,n),WTNoiseInternalBlue(cell,2,n));
// @wt.endif
// @wt.if(Inverted)
    if(n.inverted) return 1-value;
// @wt.endif
    return value;
}
// @wt.end
#endif
