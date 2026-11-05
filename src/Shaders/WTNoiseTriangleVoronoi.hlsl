#ifndef WHIMTEX_WT_NOISE_TRIANGLE_VORONOI_INCLUDED
#define WHIMTEX_WT_NOISE_TRIANGLE_VORONOI_INCLUDED
#include "WTNoiseCellular.hlsl"

struct WTNoiseTriangleVoronoiSettings
{
    int distribution;
    float jitter;
    float evolution;
    // Bounded anisotropy keeps the exact local search finite and practical.
    float2 stretch;
};
WTNoiseTriangleVoronoiSettings WTNoiseTriangleVoronoiDefaults()
{
    WTNoiseTriangleVoronoiSettings t;
    t.distribution=WT_NOISE_CELL_UNIFORM; t.jitter=1; t.evolution=0; t.stretch=1; return t;
}
struct WTNoiseTriangleVoronoiSample2D
{
    float distance,edgeDistance,value;
    uint id;
    float2 toPoint,borderNormal;
};
float2 WTNoiseInternalTriangleAxis(int i,float2 stretch)
{
    return (i==0 ? float2(0,-1) : i==1 ? float2(0.8660254037844386,0.5) : float2(-0.8660254037844386,0.5))*stretch;
}
float WTNoiseInternalTriangleMetric(float2 p,float2 stretch)
{
    p*=stretch;
    return max(-p.y,0.8660254037844386*abs(p.x)+0.5*p.y);
}
float WTNoiseInternalTriangleBox(float2 lower,float2 upper,float2 stretch)
{
    return max(0,max(-upper.y*stretch.y,max(
        0.8660254037844386*lower.x*stretch.x+0.5*lower.y*stretch.y,
        -0.8660254037844386*upper.x*stretch.x+0.5*lower.y*stretch.y)));
}
void WTNoiseInternalTriangleBounds(float radius,float2 stretch,float2 local,float anchor,float jitterRadius,out int2 lo,out int2 hi)
{
    float2 lower=float2(-1.7320508075688772*radius/stretch.x,-radius/stretch.y);
    float2 upper=float2(1.7320508075688772*radius/stretch.x,2*radius/stretch.y);
    // With stretch in 0.5..2, the seed sites bound both passes inside +/-64.
    // The clamp also prevents invalid coordinates from producing unbounded loops.
    lo=clamp((int2)ceil(local-anchor-jitterRadius+lower-0.00001),-64,64);
    hi=clamp((int2)floor(local-anchor+jitterRadius+upper+0.00001),-64,64);
}
void WTNoiseInternalTriangleConsider(float2 p,inout float squared,inout float2 direction)
{
    float d=dot(p,p);
    if(d<squared) { squared=d; direction=p; }
}
void WTNoiseInternalTriangleBorder(float2 a,float2 b,float2 stretch,inout float squared,inout float2 direction)
{
    // max_j b_j <= max_i a_i is a union of three wedges: b_j <= a_i for every j.
    // The parallel j=i inequality is constant; the other two bound each wedge.
    // Project onto either ray or their vertex to obtain the exact closest boundary.
    [unroll] for(int i=0;i<3;i++)
    {
        float2 axis=WTNoiseInternalTriangleAxis(i,stretch);
        float height=dot(axis,a);
        // Coincident affine faces are equality plateaus, not an interior border.
        if(dot(axis,b)>=height) continue;
        float2 axis1=WTNoiseInternalTriangleAxis(i==2 ? 0 : i+1,stretch);
        float2 axis2=WTNoiseInternalTriangleAxis(i==0 ? 2 : i-1,stretch);
        float2 n1=axis-axis1,n2=axis-axis2;
        float c1=height-dot(axis1,b),c2=height-dot(axis2,b);
        if(c1>=0 && c2>=0) { squared=0; direction=0; continue; }
        float2 p1=n1*(c1/dot(n1,n1)),p2=n2*(c2/dot(n2,n2));
        if(dot(n2,p1)<=c2) WTNoiseInternalTriangleConsider(p1,squared,direction);
        if(dot(n1,p2)<=c1) WTNoiseInternalTriangleConsider(p2,squared,direction);
        float determinant=n1.x*n2.y-n1.y*n2.x;
        float2 corner=float2(c1*n2.y-c2*n1.y,n1.x*c2-n2.x*c1)/determinant;
        WTNoiseInternalTriangleConsider(corner,squared,direction);
    }
}
WTNoiseTriangleVoronoiSample2D WTNoiseInternalTriangleSample(float2 p,int2 origin,int seed,int2 period,WTNoiseTriangleVoronoiSettings t)
{
    WTNoiseCellularSettings c=WTNoiseCellularDefaults(); c.distribution=t.distribution; c.jitter=t.jitter; c.evolution=t.evolution;
    float2 stretch=clamp(abs(t.stretch),0.5,2);
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    int2 center=(int2)floor(p+(anchor==0 ? 0.5 : 0));
    float2 local=p-center; center+=origin;
    int3 tile=int3(period,0),baseCell=int3(center,0);
    uint id;
    float2 nearest=WTNoiseInternalFeature(baseCell,seed,tile,2,c,id).xy-local;
    int2 nearestCell=center;
    float nearestMetric=WTNoiseInternalTriangleMetric(nearest,stretch);
    uint nearestId=id;
    float radius=WTNoiseInternalCellRadius(2,c);
    int2 lo,hi;
    WTNoiseInternalTriangleBounds(nearestMetric,stretch,local,anchor,radius,lo,hi);
    [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        int2 shift=int2(x,y);
        if(x==0 && y==0) continue;
        float2 box=shift+anchor-local;
        if(WTNoiseInternalTriangleBox(box-radius,box+radius,stretch)>nearestMetric+0.00001) continue;
        int2 cell=center+shift;
        float2 featureVector=shift+WTNoiseInternalFeature(int3(cell,0),seed,tile,2,c,id).xy-local;
        float metric=WTNoiseInternalTriangleMetric(featureVector,stretch);
        if(metric<nearestMetric || (metric==nearestMetric && WTNoiseInternalCellBefore(int3(cell,0),int3(nearestCell,0))))
        { nearestMetric=metric; nearest=featureVector; nearestCell=cell; nearestId=id; }
    }
    float squared=1e20; float2 direction=0;
    // Two distinct nearby sites provide a finite upper bound before neighbor pruning.
    [unroll] for(int initial=0;initial<2;initial++)
    {
        int2 cell=center+int2(initial,0);
        if(all(cell==nearestCell)) continue;
        float2 featureVector=float2(initial,0)+WTNoiseInternalFeature(int3(cell,0),seed,tile,2,c,id).xy-local;
        WTNoiseInternalTriangleBorder(nearest,featureVector,stretch,squared,direction);
    }
    float lipschitz=max(stretch.y,length(float2(0.8660254037844386*stretch.x,0.5*stretch.y)));
    WTNoiseInternalTriangleBounds(nearestMetric+2*lipschitz*sqrt(squared),stretch,local,anchor,radius,lo,hi);
    [loop] for(int by=lo.y;by<=hi.y;by++) [loop] for(int bx=lo.x;bx<=hi.x;bx++)
    {
        int2 shift=int2(bx,by),cell=center+shift;
        if(all(cell==nearestCell)) continue;
        float2 box=shift+anchor-local;
        if(WTNoiseInternalTriangleBox(box-radius,box+radius,stretch)>nearestMetric+2*lipschitz*sqrt(squared)+0.00001) continue;
        float2 featureVector=shift+WTNoiseInternalFeature(int3(cell,0),seed,tile,2,c,id).xy-local;
        WTNoiseInternalTriangleBorder(nearest,featureVector,stretch,squared,direction);
    }
    WTNoiseTriangleVoronoiSample2D result;
    result.distance=length(nearest); result.edgeDistance=sqrt(max(squared,0));
    result.id=nearestId;
    result.value=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? asint(nearestId)*(0.5/2147483648.0)+0.5 : WTNoiseInternalUnit(WTNoiseInternalHash(nearestId^0x68bc21ebu));
    result.toPoint=nearest;
    result.borderNormal=squared>1e-20 ? direction*rsqrt(squared) : float2(0,0);
    return result;
}
float WTNoiseInternalTriangle(float2 p,WTNoiseSettings n,WTNoiseTriangleVoronoiSettings t,bool auxiliary,out WTNoiseTriangleVoronoiSample2D firstOctave)
{
    firstOctave=(WTNoiseTriangleVoronoiSample2D)0;
    WTNoiseFractal f=n.fractal;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f),mean=0; bool meanReady=false;
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(float3(p,0),n,frequency,2,1,period,origin,inputScale);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility<1 && !meanReady)
        {
            // Shared cell-family estimate, not the exact mean of a triangle tile.
            WTNoiseCellularSettings c=WTNoiseCellularDefaults(); c.distribution=t.distribution; c.jitter=t.jitter; c.evolution=t.evolution;
            mean=WTNoiseInternalCellEdgeMean(c,2,f); meanReady=true;
        }
        if(visibility>0 || (auxiliary && octave==0))
        {
            WTNoiseTriangleVoronoiSample2D data=WTNoiseInternalTriangleSample(q.xy,origin.xy,WTNoiseInternalSeed(n.seed,octave),period.xy,t);
            value=saturate(data.edgeDistance*0.7071067811865475);
            if(octave==0) firstOctave=data;
        }
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(0.5+0.5*sum);
}
float WTNoiseTriangleVoronoi(float2 p,WTNoiseSettings n,WTNoiseTriangleVoronoiSettings t,out WTNoiseTriangleVoronoiSample2D firstOctave)
{ return WTNoiseInternalTriangle(p,n,t,true,firstOctave); }
float WTNoiseTriangleVoronoi(float2 p,WTNoiseSettings n,WTNoiseTriangleVoronoiSettings t)
{ WTNoiseTriangleVoronoiSample2D unused; return WTNoiseInternalTriangle(p,n,t,false,unused); }
float WTNoiseTriangleVoronoi(float2 p,WTNoiseSettings n,out WTNoiseTriangleVoronoiSample2D firstOctave)
{ return WTNoiseTriangleVoronoi(p,n,WTNoiseTriangleVoronoiDefaults(),firstOctave); }
float WTNoiseTriangleVoronoi(float2 p,WTNoiseSettings n)
{ return WTNoiseTriangleVoronoi(p,n,WTNoiseTriangleVoronoiDefaults()); }
#endif
