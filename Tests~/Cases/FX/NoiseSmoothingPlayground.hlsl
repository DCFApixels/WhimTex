// Independent prototype kernels and a frozen pre-unification baseline.
#include "Packages/com.dcfapixels.whimtex/src/Shaders/WTNoise.hlsl"
#include "Packages/com.dcfapixels.whimtex/Tests~/Cases/FX/CellularNoiseReference.hlsl"
float _Smoothness, _Evolution, _Jitter;
float _Period, _Footprint, _Offset, _Fractal;
float4 _Region;
int _Seed;
struct LabVertex { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
LabVertex LabVert(uint id:SV_VertexID)
{
    LabVertex v; float2 p=float2((id<<1)&2,id&2);
    v.position=float4(p*2-1,0,1); v.uv=_Region.xy+p*_Region.zw; return v;
}
float LabBound(int d,int metric)
{
    float squared=d+1.25,manhattan=d+0.5;
    return metric==0 ? sqrt(squared) : metric==1 ? squared : metric==2 ? manhattan : manhattan+squared;
}
// Preserve the current Euclidean Voronoise exactly; the other metrics use the
// square of their distance as the energy. Smoothness is intentionally not yet
// perceptually calibrated between metrics. Compact support is in metric units.
float LabBlend(float3 p,int dimensions,WTNoiseCellularSettings c,int seed)
{
    WTNoiseInternalCell nearest=WTNoiseInternalCells(p,0,seed,0,dimensions,c);
    float temperature=0.5*saturate(_Smoothness)*saturate(_Smoothness);
    if(temperature<=0.000001) return nearest.value;
    float covering=WTNoiseInternalCellCovering(dimensions,c),support=covering+1;
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    int3 center=(int3)floor(p+(c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    float radius=WTNoiseInternalCellRadius(dimensions,c)+0.00001;
    float extent=WTNoiseInternalCellMetricExtent(support,c.metric);
    int3 lo,hi;
    WTNoiseInternalCellBounds(local-anchor-radius-extent,local-anchor+radius+extent,dimensions,lo,hi);
    float sum=0,weight=0,minimum=nearest.distance1*nearest.distance1;
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(WTNoiseInternalMetric(box,c.metric)>=support) continue;
        uint id;
        float3 delta=(float3)shift+WTNoiseInternalFeature(center+shift,seed,0,dimensions,c,id)-local;
        float distance=WTNoiseInternalMetric(delta,c.metric);
        if(distance>=support) continue;
        float energy=distance*distance;
        // Avoid sqrt/square roundoff in the Euclidean reference comparison.
        if(c.metric==0) { energy=dot(delta,delta); minimum=dot(nearest.toPoint,nearest.toPoint); }
        float w=exp2(-(energy-minimum)*(1.442695040889/temperature))*WTNoiseInternalCellWindow(distance,covering,1);
        float value=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? asint(id)*(0.5/2147483648.0)+0.5 : WTNoiseInternalUnit(WTNoiseInternalHash(id^0x68bc21ebu));
        sum+=w*value; weight+=w;
    }
    return saturate(sum/max(weight,0.000001));
}
// Soft first/second order statistics from elementary symmetric sums:
// S1=-T log(sum exp(-d/T)), S2=-T log(sum_{i<j} exp(-(di+dj)/T)).
// F1=S1, F2=S2-S1. Shift separately by the hard first and second distances
// to avoid cancellation and underflow from subtracting nearly equal sums.
float2 LabPair(float3 p,int dimensions,WTNoiseCellularSettings c,int seed)
{
    WTNoiseInternalCell nearest=WTNoiseInternalCells(p,0,seed,0,dimensions,c);
    float temperature=0.25*saturate(_Smoothness);
    if(temperature<=0.000001) return float2(nearest.distance1,nearest.distance2);
    float covering=LabBound(dimensions,c.metric),width=2*temperature,support=covering+width;
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    int3 center=(int3)floor(p+(c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    float radius=WTNoiseInternalCellRadius(dimensions,c)+0.00001;
    float extent=WTNoiseInternalCellMetricExtent(support,c.metric);
    int3 lo,hi;
    WTNoiseInternalCellBounds(local-anchor-radius-extent,local-anchor+radius+extent,dimensions,lo,hi);
    float others=0,pairs=0;
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        int3 shift=int3(x,y,z),cell=center+shift;
        if(all(cell==nearest.cell)) continue;
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(WTNoiseInternalMetric(box,c.metric)>=support) continue;
        uint id;
        float3 delta=(float3)shift+WTNoiseInternalFeature(cell,seed,0,dimensions,c,id)-local;
        float distance=WTNoiseInternalMetric(delta,c.metric);
        if(distance>=support) continue;
        float w=exp2(-(distance-nearest.distance2)*(1.442695040889/temperature))*WTNoiseInternalCellWindow(distance,covering,width);
        pairs+=others*w; others+=w;
    }
    float q=exp2(-(nearest.distance2-nearest.distance1)*(1.442695040889/temperature));
    float firstSum=1+q*others,secondSum=max(others+q*pairs,0.000001);
    float f1=nearest.distance1-temperature*log(firstSum);
    float f2=nearest.distance2-temperature*log(secondSum/firstSum);
    return max(float2(f1,f2),0);
}
// Positive smooth minimum of facet distances. Preserves zero at a true facet,
// softens interior ridges, and is not an exact Euclidean distance when enabled.
float LabEdge(float3 p,int dimensions,WTNoiseCellularSettings c,int seed)
{
    WTNoiseInternalCell nearest=WTNoiseInternalCells(p,0,seed,0,dimensions,c);
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    float radius=WTNoiseInternalCellRadius(dimensions,c);
    float covering=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? sqrt((float)dimensions)*0.5+radius : sqrt((float)dimensions)*(0.5+radius);
    radius+=0.00001; covering+=0.00001;
    uint ignored;
    float3 offset=WTNoiseInternalFeature(nearest.cell,seed,0,dimensions,c,ignored);
    int3 lo,hi;
    WTNoiseInternalCellBounds(offset-anchor-radius-2*covering,offset-anchor+radius+2*covering,dimensions,lo,hi);
    float edge=1e20,aggregate=0,power=2/max(saturate(_Smoothness),0.001);
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        if(x==0 && y==0 && z==0) continue;
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-offset)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(dot(box,box)>4*covering*covering) continue;
        float3 between=(float3)shift+WTNoiseInternalFeature(nearest.cell+shift,seed,0,dimensions,c,ignored)-offset;
        float squared=dot(between,between);
        if(squared<=0) continue;
        float d=max(dot(nearest.toPoint+0.5*between,between*rsqrt(squared)),0);
        if(d<edge) { aggregate=aggregate*pow(d/max(edge,1e-20),power)+1; edge=d; }
        else aggregate+=pow(edge/max(d,1e-20),power);
    }
    if(_Smoothness<=0) return edge;
    return edge*pow(max(aggregate,1),-1/power);
}
float4 LabFrag(LabVertex v):SV_Target
{
    float3 p=float3(v.uv*16+float2(0.123,0.234),2.371);
    if(LAB_DIM<3) p.z=0;
    if(LAB_DIM<2) p.y=0;
    WTNoiseCellularSettings c=WTNoiseCellularDefaults();
    c.metric=LAB_METRIC; c.output=LAB_OUTPUT; c.distribution=LAB_DISTRIBUTION;
    c.jitter=_Jitter; c.evolution=_Evolution; c.smoothness=_Smoothness;
    WTNoiseSettings n=WTNoiseDefaults(_Seed); n.scale=1;
#if !LAB_STATIC_DOMAIN
    n.period=_Period; n.offset=_Offset; n.dpdx=float3(_Footprint,0,0); n.fractal.mode=(int)_Fractal; n.fractal.octaves=3;
#endif
    ReferenceWTNoiseCellularSettings rc=ReferenceWTNoiseCellularDefaults();
    rc.metric=c.metric; rc.output=c.output; rc.distribution=c.distribution;
    rc.jitter=c.jitter; rc.evolution=c.evolution; rc.smoothness=c.smoothness;
    float value=0; float2 pair=0;
#if LAB_KIND==0
    value=v.uv.x;
#elif LAB_KIND==1
    ReferenceWTNoiseInternalCell data;
    value=ReferenceWTNoiseInternalCellular(p,n,rc,LAB_DIM,data);
    pair=float2(data.distance1,data.distance2);
#elif LAB_KIND==2
    ReferenceWTNoiseVoronoiseSettings s=ReferenceWTNoiseVoronoiseDefaults();
    s.jitter=_Jitter; s.evolution=_Evolution; s.smoothness=_Smoothness; s.distribution=LAB_DISTRIBUTION;
    value=ReferenceWTNoiseInternalVoronoise(p,n,s,LAB_DIM);
#elif LAB_KIND==3
    value=LabBlend(p,LAB_DIM,c,_Seed);
#elif LAB_KIND==4
    pair=LabPair(p,LAB_DIM,c,_Seed);
    WTNoiseInternalCell data=(WTNoiseInternalCell)0;
    data.distance1=pair.x; data.distance2=pair.y;
    value=WTNoiseInternalCellValue(data,c,LAB_DIM);
#elif LAB_KIND==5
    ReferenceWTNoiseVoronoiSettings s=ReferenceWTNoiseVoronoiDefaults();
    s.jitter=_Jitter; s.evolution=_Evolution; s.distribution=LAB_DISTRIBUTION;
    value=ReferenceWTNoiseInternalVoronoi(p,n,s,LAB_DIM);
#elif LAB_KIND==6
    value=LabEdge(p,LAB_DIM,c,_Seed)/sqrt((float)LAB_DIM);
#elif LAB_KIND==7
    WTNoiseInternalCell data;
    value=WTNoiseInternalCellular(p,n,c,LAB_DIM,data);
    pair=float2(data.distance1,data.distance2);
#endif
    return float4(value,pair,1);
}
