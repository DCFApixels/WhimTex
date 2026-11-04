#ifndef WHIMTEX_WT_NOISE_CELLULAR_INCLUDED
#define WHIMTEX_WT_NOISE_CELLULAR_INCLUDED
#include "WTNoiseBasis.hlsl"

#define WT_NOISE_CELL_UNIFORM 0
#define WT_NOISE_CELL_FIXED_RADIUS 1
#define WT_NOISE_DISTANCE_EUCLIDEAN 0
#define WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED 1
#define WT_NOISE_DISTANCE_MANHATTAN 2
#define WT_NOISE_DISTANCE_HYBRID 3
#define WT_NOISE_CELL_VALUE 0
#define WT_NOISE_CELL_F1 1
#define WT_NOISE_CELL_F2 2
#define WT_NOISE_CELL_ADD 3
#define WT_NOISE_CELL_SUBTRACT 4
#define WT_NOISE_CELL_MULTIPLY 5
#define WT_NOISE_CELL_DIVIDE 6
#define WT_NOISE_CELL_SMOOTH_F1 7

struct WTNoiseCellularSettings
{
    int distribution;
    int metric;
    int output;
    float jitter;
    float smoothness;
};
WTNoiseCellularSettings WTNoiseCellularDefaults()
{
    WTNoiseCellularSettings c;
    c.distribution=WT_NOISE_CELL_UNIFORM; c.metric=WT_NOISE_DISTANCE_EUCLIDEAN;
    c.output=WT_NOISE_CELL_F1; c.jitter=1; c.smoothness=0.5;
    return c;
}
struct WTNoiseCellularSample1D { float distance1,distance2,value; uint id; float toPoint; };
struct WTNoiseCellularSample2D { float distance1,distance2,value; uint id; float2 toPoint; };
struct WTNoiseCellularSample3D { float distance1,distance2,value; uint id; float3 toPoint; };
struct WTNoiseInternalCell
{
    float distance1,distance2,value;
    uint id;
    float3 toPoint;
    int3 cell;
};
float WTNoiseInternalMetric(float3 d,int metric)
{
    d=abs(d);
    float squared=dot(d,d),manhattan=d.x+d.y+d.z;
    return metric==WT_NOISE_DISTANCE_EUCLIDEAN ? sqrt(squared) :
        metric==WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? squared :
        metric==WT_NOISE_DISTANCE_MANHATTAN ? manhattan : manhattan+squared;
}
float WTNoiseInternalCellRadius(int dimensions,WTNoiseCellularSettings c)
{
    // The 1D distribution uses the same displacement magnitude as 2D.
    return (c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? (dimensions==3 ? 0.39614353 : 0.43701595) : 0.5)*saturate(c.jitter);
}
bool WTNoiseInternalCellBefore(int3 a,int3 b)
{
    // Order unwrapped cells, not hashed IDs: repeats are distinct sites and hash
    // collisions must not affect ties. Translation by a period preserves this order.
    return a.x<b.x || (a.x==b.x && (a.y<b.y || (a.y==b.y && a.z<b.z)));
}
float3 WTNoiseInternalFeature(int3 cell,int seed,int3 period,int dimensions,WTNoiseCellularSettings c,out uint id)
{
    float3 offset;
    if(c.distribution==WT_NOISE_CELL_FIXED_RADIUS)
    {
        int3 wrapped=WTNoiseInternalWrap(cell,period);
        int h=(seed^(wrapped.x*501125321)^(wrapped.y*1136930381)^(wrapped.z*1720413743))*0x27d4eb2d;
        id=asuint(h);
        if(dimensions==1) offset=float3((WTNoiseInternalHash(id)&1u)!=0 ? 1 : -1,0,0);
        else if(dimensions==2)
        {
            int i=h&(255<<1);
            offset=float3(RAND_VECS_2D[i],RAND_VECS_2D[i|1],0);
        }
        else
        {
            int i=h&(255<<2);
            offset=float3(RAND_VECS_3D[i],RAND_VECS_3D[i|1],RAND_VECS_3D[i|2]);
        }
        offset*=WTNoiseInternalCellRadius(dimensions,c);
    }
    else
    {
        id=WTNoiseInternalHashCell(cell,seed,period);
        float3 random=float3(WTNoiseInternalUnit(id),WTNoiseInternalUnit(WTNoiseInternalHash(id^0x9e3779b9u)),
            WTNoiseInternalUnit(WTNoiseInternalHash(id^0x68bc21ebu)));
        offset=0.5+(random-0.5)*saturate(c.jitter);
    }
    if(dimensions<3) offset.z=0;
    if(dimensions<2) offset.y=0;
    return offset;
}
void WTNoiseInternalCellCandidate(int3 cell,int3 center,float3 local,int seed,int3 period,int dimensions,
    WTNoiseCellularSettings c,inout WTNoiseInternalCell result)
{
    uint id;
    float3 d=(float3)(cell-center)+WTNoiseInternalFeature(cell,seed,period,dimensions,c,id)-local;
    float distance=WTNoiseInternalMetric(d,c.metric);
    if(distance<result.distance1 || (distance==result.distance1 && WTNoiseInternalCellBefore(cell,result.cell)))
    {
        result.distance2=result.distance1; result.distance1=distance;
        result.toPoint=d; result.cell=cell; result.id=id;
        result.value=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? asint(id)*(0.5/2147483648.0)+0.5 :
            WTNoiseInternalUnit(WTNoiseInternalHash(id^0x68bc21ebu));
    }
    else result.distance2=min(result.distance2,distance);
}
void WTNoiseInternalCellBounds(float3 lower,float3 upper,int dimensions,out int3 lo,out int3 hi)
{
    // With jitter <= 1, local-anchor and feature-anchor are each within +/-0.5.
    // The center and +X seed sites bound F2 by d+1 in Manhattan distance,
    // sqrt(d+3) in Euclidean distance, and 2*d+4 in Hybrid distance.
    // Inverting any metric gives an axis extent <= d+1; the cell offset is <= d+2.
    // Voronoi neighbors also fit: 2*radius + 2*covering <= 1+2*sqrt(d) <= d+2.
    // Clamp integer loop bounds, not density: even invalid float-to-int results
    // cannot turn a local search into an unbounded loop. Valid sites are unchanged.
    int extent=dimensions+2;
    lo=clamp((int3)ceil(lower),-extent,extent);
    hi=clamp((int3)floor(upper),-extent,extent);
    if(dimensions<3) { lo.z=0; hi.z=0; }
    if(dimensions<2) { lo.y=0; hi.y=0; }
}
WTNoiseInternalCell WTNoiseInternalCells(float3 p,int3 origin,int seed,int3 period,int dimensions,WTNoiseCellularSettings c)
{
    int3 center=(int3)floor(p+(c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    center+=origin;
    WTNoiseInternalCell result;
    result.distance1=1e20; result.distance2=1e20; result.value=0; result.id=0; result.toPoint=0; result.cell=center;
    WTNoiseInternalCellCandidate(center,center,local,seed,period,dimensions,c,result);
    WTNoiseInternalCellCandidate(center+int3(1,0,0),center,local,seed,period,dimensions,c,result);
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    float radius=WTNoiseInternalCellRadius(dimensions,c);
    // Invert the one-axis metric to bound every site that can improve F2.
    // The rationalized hybrid inverse also works for very small distances.
    float extent=c.metric==WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? sqrt(result.distance2) :
        c.metric==WT_NOISE_DISTANCE_HYBRID ? 2*result.distance2/(sqrt(1+4*result.distance2)+1) : result.distance2;
    // Conservative rounding allowance affects pruning only, never feature positions.
    radius+=0.00001;
    int3 lo,hi;
    WTNoiseInternalCellBounds(local-anchor-radius-extent,local-anchor+radius+extent,dimensions,lo,hi);
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        // These sites have already seeded F1/F2. Enumerating either twice would
        // make F2 equal to F1 and bias the difference/ratio outputs.
        if(y==0 && z==0 && (x==0 || x==1)) continue;
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(WTNoiseInternalMetric(box,c.metric)>result.distance2) continue;
        WTNoiseInternalCellCandidate(center+shift,center,local,seed,period,dimensions,c,result);
    }
    return result;
}
float WTNoiseInternalCellMetricExtent(float distance,int metric)
{
    return metric==WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? sqrt(distance) :
        metric==WT_NOISE_DISTANCE_HYBRID ? 2*distance/(sqrt(1+4*distance)+1) : distance;
}
float WTNoiseInternalCellCovering(int dimensions,WTNoiseCellularSettings c)
{
    // The feature of the containing/nearest cell is at most 0.5+radius away
    // on each active axis. This bound also covers the fixed-radius distribution.
    float axis=0.5+WTNoiseInternalCellRadius(dimensions,c);
    return WTNoiseInternalMetric(float3(axis,dimensions>=2 ? axis : 0,dimensions==3 ? axis : 0),c.metric);
}
float WTNoiseInternalCellWindow(float distance,float covering,float width)
{
    // Compact C1 support, independent of the query's nearest-site identity.
    float t=saturate((distance-covering)/width);
    return (1-t)*(1-t)*(1+2*t);
}
float WTNoiseInternalSmoothF1(float3 p,int3 origin,int seed,int3 period,int dimensions,
    WTNoiseCellularSettings c,float nearest)
{
    // Log-sum-exp soft minimum. Subtracting the exact nearest distance in the
    // exponent prevents underflow; it cancels algebraically from the soft minimum.
    // Smoothness is 0..1, mapping to a temperature of 0..0.25 metric units.
    float width=0.25*saturate(c.smoothness);
    if(width<=0.000001) return nearest;
    float covering=WTNoiseInternalCellCovering(dimensions,c),support=covering+8*width;
    // In the largest case (Manhattan), support <= dimensions+2. The anchor/radius
    // offset adds less than one integer cell, so CellBounds' existing cap suffices.
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    int3 center=(int3)floor(p+(c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    center+=origin;
    float radius=WTNoiseInternalCellRadius(dimensions,c)+0.00001;
    float extent=WTNoiseInternalCellMetricExtent(support,c.metric);
    int3 lo,hi;
    WTNoiseInternalCellBounds(local-anchor-radius-extent,local-anchor+radius+extent,dimensions,lo,hi);
    float weight=0;
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(WTNoiseInternalMetric(box,c.metric)>=support) continue;
        uint id;
        float3 d=(float3)shift+WTNoiseInternalFeature(center+shift,seed,period,dimensions,c,id)-local;
        float distance=WTNoiseInternalMetric(d,c.metric);
        if(distance<support)
            weight+=exp2(-(distance-nearest)*(1.442695040889/width))*WTNoiseInternalCellWindow(distance,covering,8*width);
    }
    // At least the nearest site has unit weight. The final scalar normalization
    // clamps negative soft distances; auxiliary geometry remains the hard F1/F2.
    return max(nearest-width*log(max(weight,1)),0);
}
float WTNoiseInternalCellValue(WTNoiseInternalCell v,WTNoiseCellularSettings c,int dimensions)
{
    if(c.output==WT_NOISE_CELL_VALUE) return v.value;
    // One fixed distance scale for F1 and F2, independent of distribution/jitter.
    // Two distinct sites always exist in the containing/nearest cell and a neighbor
    // toward the query. With jitter <= 1 their axis distances are at most 1.5 on
    // that axis and 1 elsewhere: squared <= d+1.25, Manhattan <= d+0.5.
    // Euclidean and squared Euclidean therefore retain their square relationship.
    float squaredBound=dimensions+1.25,manhattanBound=dimensions+0.5;
    float bound=c.metric==WT_NOISE_DISTANCE_EUCLIDEAN ? sqrt(squaredBound) :
        c.metric==WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? squaredBound :
        c.metric==WT_NOISE_DISTANCE_MANHATTAN ? manhattanBound : manhattanBound+squaredBound;
    if(c.output==WT_NOISE_CELL_F2) return saturate(v.distance2/bound);
    if(c.output==WT_NOISE_CELL_ADD) return saturate((v.distance1+v.distance2)/(2*bound));
    if(c.output==WT_NOISE_CELL_SUBTRACT) return saturate((v.distance2-v.distance1)/bound);
    if(c.output==WT_NOISE_CELL_MULTIPLY) return saturate((v.distance1*v.distance2)/(bound*bound));
    if(c.output==WT_NOISE_CELL_DIVIDE) return v.distance2>0 ? saturate(v.distance1/v.distance2) : 0;
    return saturate(v.distance1/bound);
}
float WTNoiseInternalCellTriangleIntegral(float t)
{
    float cycle=floor(t*0.5),r=t-2*cycle;
    return cycle+(r<=1 ? 0.5*r*r : 1-0.5*(2-r)*(2-r));
}
float WTNoiseInternalCellUniformMean(float center,float halfWidth,WTNoiseFractal f)
{
    // Exact folded mean for a uniform surrogate on [a,b]. For random cell values
    // [0,1] is the ensemble model; a specific short periodic tile can differ.
    float a=saturate(center-halfWidth),b=saturate(center+halfWidth),ignored;
    if(b-a<0.00001) return WTNoiseInternalFractalValue(center,f,ignored);
    if(f.mode==WT_NOISE_RIDGED)
        return saturate(1-((b-0.5)*abs(b-0.5)-(a-0.5)*abs(a-0.5))/(b-a));
    if(f.mode==WT_NOISE_PINGPONG)
    {
        float strength=max(f.pingPongStrength,0),ta=a*2*strength,tb=b*2*strength;
        if(tb-ta<0.00001) return WTNoiseInternalFractalValue(center,f,ignored);
        if(tb-ta>65536) return 0.5;
        return saturate((WTNoiseInternalCellTriangleIntegral(tb)-WTNoiseInternalCellTriangleIntegral(ta))/(tb-ta));
    }
    return (a+b)*0.5;
}
float3 WTNoiseInternalCellMeanPoint(int i,int dimensions)
{
    // A bounded 16-point cubature of the positive half of a regular unit cell.
    float3 u=(float3(i,(i*9+3)%16,(i*11+13)%16)+0.5)*(0.5/16);
    if(dimensions<3) u.z=0;
    if(dimensions<2) u.y=0;
    return u;
}
float2 WTNoiseInternalCellMeanScale(int dimensions,WTNoiseCellularSettings c)
{
    // Approximate F1/F2 ensemble mean ratios at full jitter relative to a regular
    // lattice. Rounded estimates from an independent CPU geometry experiment:
    // 4000 (position,seed) pairs per dimension/distribution, all four metrics.
    // The 1D uniform endpoints use the spacing moments. Interpolation is quadratic
    // in jitter. These are not per-seed/per-period integrals or exact higher moments.
    float2 ratio;
    bool fixedRadius=c.distribution==WT_NOISE_CELL_FIXED_RADIUS;
    if(dimensions==1)
        ratio=c.metric==WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? (fixedRadius ? float2(2.146,1.18) : float2(1.5,1.071429)) :
            c.metric==WT_NOISE_DISTANCE_HYBRID ? (fixedRadius ? float2(1.573,1.08) : float2(1.25,1.03125)) :
            (fixedRadius ? float2(1.382,1) : float2(1.166667,1));
    else if(dimensions==2)
        ratio=c.metric==WT_NOISE_DISTANCE_EUCLIDEAN ? (fixedRadius ? float2(1.14,1) : float2(1.12,1)) :
            c.metric==WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? (fixedRadius ? float2(1.37,1.04) : float2(1.32,1.03)) :
            c.metric==WT_NOISE_DISTANCE_MANHATTAN ? (fixedRadius ? float2(1.10,1.05) : float2(1.08,1.05)) :
            (fixedRadius ? float2(1.17,1.05) : float2(1.14,1.05));
    else
        ratio=c.metric==WT_NOISE_DISTANCE_EUCLIDEAN ? (fixedRadius ? float2(1.06,1.02) : float2(1.08,1.02)) :
            c.metric==WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? (fixedRadius ? float2(1.14,1.06) : float2(1.20,1.07)) :
            c.metric==WT_NOISE_DISTANCE_MANHATTAN ? float2(1.01,1.05) :
            (fixedRadius ? float2(1.04,1.06) : float2(1.07,1.07));
    float jitter=saturate(c.jitter);
    return lerp(float2(1,1),ratio,jitter*jitter);
}
float WTNoiseInternalCellularMean(WTNoiseCellularSettings c,int dimensions,WTNoiseFractal f)
{
    if(c.output==WT_NOISE_CELL_VALUE) return WTNoiseInternalCellUniformMean(0.5,0.5,f);
    // The surrogate retains dimension, metric, output, jitter and fractal folding.
    // Nonlinear distance combinations and Smooth F1 are approximate. No feature
    // searches, texture/cache state or dependence on the current query are needed.
    // Highly folded PingPong approaches its cycle mean; avoid cubature aliasing.
    if(f.mode==WT_NOISE_PINGPONG && f.pingPongStrength>=16) return 0.5;
    float2 scale=WTNoiseInternalCellMeanScale(dimensions,c);
    float mean=0;
    [unroll] for(int i=0;i<16;i++)
    {
        float3 u=WTNoiseInternalCellMeanPoint(i,dimensions),other=u;
        if(u.x>=u.y && u.x>=u.z) other.x=1-u.x;
        else if(u.y>=u.z) other.y=1-u.y;
        else other.z=1-u.z;
        WTNoiseInternalCell v=(WTNoiseInternalCell)0;
        v.distance1=WTNoiseInternalMetric(u,c.metric)*scale.x;
        v.distance2=max(v.distance1,WTNoiseInternalMetric(other,c.metric)*scale.y);
        float width=0.25*saturate(c.smoothness);
        if(c.output==WT_NOISE_CELL_SMOOTH_F1 && width>0.000001)
            v.distance1=max(v.distance1-width*log(1+dimensions*exp2(-(v.distance2-v.distance1)*(1.442695040889/width))),0);
        float ignored;
        mean+=WTNoiseInternalFractalValue(WTNoiseInternalCellValue(v,c,dimensions),f,ignored)*(1.0/16);
    }
    float cycleBlend=f.mode==WT_NOISE_PINGPONG ? saturate((f.pingPongStrength-8)*(1.0/8)) : 0;
    return saturate(lerp(mean,0.5,cycleBlend));
}
float WTNoiseInternalCellular(float3 p,WTNoiseSettings n,WTNoiseCellularSettings c,int dimensions,out WTNoiseInternalCell firstOctave)
{
    WTNoiseFractal f=n.fractal;
    firstOctave=(WTNoiseInternalCell)0;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    float mean=0;
    bool meanReady=false;
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin;
        float3 inputScale,q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility<1 && !meanReady) { mean=WTNoiseInternalCellularMean(c,dimensions,f); meanReady=true; }
        if(octave==0 || visibility>0)
        {
            int seed=WTNoiseInternalSeed(n.seed,octave);
            WTNoiseInternalCell v=WTNoiseInternalCells(q,origin,seed,period,dimensions,c);
            if(octave==0) firstOctave=v;
            if(visibility>0)
            {
                if(c.output==WT_NOISE_CELL_SMOOTH_F1)
                    v.distance1=WTNoiseInternalSmoothF1(q,origin,seed,period,dimensions,c,v.distance1);
                value=WTNoiseInternalCellValue(v,c,dimensions);
            }
        }
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(sum*0.5+0.5);
}
// Optional geometry describes the first octave. The scalar return still uses n.fractal.
#define WT_NOISE_CELLULAR_OVERLOAD(Type,Position,Dimensions,Sample,Direction) \
float WTNoiseCellular(Type p,WTNoiseSettings n,WTNoiseCellularSettings c,out Sample firstOctave) { WTNoiseInternalCell v; float value=WTNoiseInternalCellular(Position,n,c,Dimensions,v); firstOctave.distance1=v.distance1; firstOctave.distance2=v.distance2; firstOctave.value=v.value; firstOctave.id=v.id; firstOctave.toPoint=Direction; return value; } \
float WTNoiseCellular(Type p,WTNoiseSettings n,WTNoiseCellularSettings c) { WTNoiseInternalCell ignored; return WTNoiseInternalCellular(Position,n,c,Dimensions,ignored); } \
float WTNoiseCellular(Type p,WTNoiseSettings n) { return WTNoiseCellular(p,n,WTNoiseCellularDefaults()); }
WT_NOISE_CELLULAR_OVERLOAD(float,float3(p,0,0),1,WTNoiseCellularSample1D,v.toPoint.x)
WT_NOISE_CELLULAR_OVERLOAD(float2,float3(p,0),2,WTNoiseCellularSample2D,v.toPoint.xy)
WT_NOISE_CELLULAR_OVERLOAD(float3,p,3,WTNoiseCellularSample3D,v.toPoint)
#undef WT_NOISE_CELLULAR_OVERLOAD

struct WTNoiseVoronoiSettings { int distribution; float jitter; };
WTNoiseVoronoiSettings WTNoiseVoronoiDefaults()
{
    WTNoiseVoronoiSettings v; v.distribution=WT_NOISE_CELL_UNIFORM; v.jitter=1; return v;
}
float WTNoiseInternalEdge(float3 p,int3 origin,int seed,int3 period,int dimensions,WTNoiseVoronoiSettings v)
{
    WTNoiseCellularSettings c=WTNoiseCellularDefaults();
    c.distribution=v.distribution; c.jitter=v.jitter;
    WTNoiseInternalCell nearest=WTNoiseInternalCells(p,origin,seed,period,dimensions,c);
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    float radius=WTNoiseInternalCellRadius(dimensions,c);
    // Every position is within covering of some feature. Each Voronoi facet
    // therefore joins sites at most 2*covering apart; this bounds the complete
    // search, including distinct repeated sites when a period is only one cell.
    float covering=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? sqrt((float)dimensions)*0.5+radius :
        sqrt((float)dimensions)*(0.5+radius);
    radius+=0.00001; covering+=0.00001;
    uint ignored;
    float3 offset=WTNoiseInternalFeature(nearest.cell,seed,period,dimensions,c,ignored);
    int3 lo,hi;
    WTNoiseInternalCellBounds(offset-anchor-radius-2*covering,offset-anchor+radius+2*covering,dimensions,lo,hi);
    float edge=1e20;
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        if(x==0 && y==0 && z==0) continue;
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-offset)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(dot(box,box)>4*covering*covering) continue;
        float3 between=(float3)shift+WTNoiseInternalFeature(nearest.cell+shift,seed,period,dimensions,c,ignored)-offset;
        float squared=dot(between,between);
        // Coincident sites have no bisector. Do not discard merely close sites:
        // they can define the nearest facet, including at a periodic seam.
        if(squared>0)
            edge=min(edge,dot(nearest.toPoint+0.5*between,between*rsqrt(squared)));
    }
    return max(edge,0);
}
float WTNoiseInternalVoronoiMean(WTNoiseVoronoiSettings v,int dimensions,WTNoiseFractal f)
{
    // Regular-lattice edge cubature with a small ensemble jitter correction from
    // the same geometry experiment as Cellular. This is an approximate folded
    // distance mean, not 0.5 and not the exact mean of an arbitrary periodic tile.
    if(f.mode==WT_NOISE_PINGPONG && f.pingPongStrength>=16) return 0.5;
    // In 1D a Voronoi interval has length 1+(offset[i+1]-offset[i-1])/2;
    // its spatial mean edge distance is E[length^2]/4. Use that moment directly.
    float fullJitter=dimensions==1 ? (v.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 1.09549147 : 1.041666667) :
        dimensions==2 ? 1 : 1.04;
    float jitter=saturate(v.jitter),scale=lerp(1,fullJitter,jitter*jitter)/sqrt((float)dimensions),mean=0;
    [unroll] for(int i=0;i<16;i++)
    {
        float3 u=WTNoiseInternalCellMeanPoint(i,dimensions);
        float edge=(0.5-max(u.x,max(u.y,u.z)))*scale,ignored;
        mean+=WTNoiseInternalFractalValue(edge,f,ignored)*(1.0/16);
    }
    float cycleBlend=f.mode==WT_NOISE_PINGPONG ? saturate((f.pingPongStrength-8)*(1.0/8)) : 0;
    return saturate(lerp(mean,0.5,cycleBlend));
}
float WTNoiseInternalVoronoi(float3 p,WTNoiseSettings n,WTNoiseVoronoiSettings v,int dimensions)
{
    WTNoiseFractal f=n.fractal;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    float mean=0;
    bool meanReady=false;
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin;
        float3 inputScale,q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility<1 && !meanReady) { mean=WTNoiseInternalVoronoiMean(v,dimensions,f); meanReady=true; }
        if(visibility>0)
            value=WTNoiseInternalEdge(q,origin,WTNoiseInternalSeed(n.seed,octave),period,dimensions,v)/sqrt((float)dimensions);
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(sum*0.5+0.5);
}
#define WT_NOISE_VORONOI_OVERLOAD(Type,Position,Dimensions) \
float WTNoiseVoronoiEdge(Type p,WTNoiseSettings n,WTNoiseVoronoiSettings v) { return WTNoiseInternalVoronoi(Position,n,v,Dimensions); } \
float WTNoiseVoronoiEdge(Type p,WTNoiseSettings n) { return WTNoiseVoronoiEdge(p,n,WTNoiseVoronoiDefaults()); }
WT_NOISE_VORONOI_OVERLOAD(float,float3(p,0,0),1)
WT_NOISE_VORONOI_OVERLOAD(float2,float3(p,0),2)
WT_NOISE_VORONOI_OVERLOAD(float3,p,3)
#undef WT_NOISE_VORONOI_OVERLOAD

struct WTNoiseVoronoiseSettings { int distribution; float jitter; float smoothness; };
WTNoiseVoronoiseSettings WTNoiseVoronoiseDefaults()
{
    WTNoiseVoronoiseSettings v;
    v.distribution=WT_NOISE_CELL_UNIFORM; v.jitter=1; v.smoothness=0.5;
    return v;
}
float WTNoiseInternalVoronoiseSample(float3 p,int3 origin,int seed,int3 period,int dimensions,WTNoiseVoronoiseSettings v)
{
    WTNoiseCellularSettings c=WTNoiseCellularDefaults();
    c.distribution=v.distribution; c.jitter=v.jitter;
    WTNoiseInternalCell nearest=WTNoiseInternalCells(p,origin,seed,period,dimensions,c);
    float smoothness=saturate(v.smoothness),temperature=0.5*smoothness*smoothness;
    if(temperature<=0.000001) return nearest.value;
    // Normalize positive weights of random cell values, never distances. The
    // nearest-site subtraction stabilizes exponentials without changing ratios.
    // A fixed compact window makes entering/leaving candidate sites C1 continuous.
    float covering=WTNoiseInternalCellCovering(dimensions,c),support=covering+1;
    float anchor=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    int3 center=(int3)floor(p+(c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    center+=origin;
    float radius=WTNoiseInternalCellRadius(dimensions,c)+0.00001;
    int3 lo,hi;
    WTNoiseInternalCellBounds(local-anchor-radius-support,local-anchor+radius+support,dimensions,lo,hi);
    float sum=0,weight=0,nearestSquared=dot(nearest.toPoint,nearest.toPoint);
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(dot(box,box)>=support*support) continue;
        uint id;
        float3 d=(float3)shift+WTNoiseInternalFeature(center+shift,seed,period,dimensions,c,id)-local;
        float squared=dot(d,d);
        if(squared>=support*support) continue;
        float w=exp2(-(squared-nearestSquared)*(1.442695040889/temperature))*
            WTNoiseInternalCellWindow(sqrt(squared),covering,1);
        float value=c.distribution==WT_NOISE_CELL_FIXED_RADIUS ? asint(id)*(0.5/2147483648.0)+0.5 :
            WTNoiseInternalUnit(WTNoiseInternalHash(id^0x68bc21ebu));
        sum+=value*w; weight+=w;
    }
    return saturate(sum/max(weight,0.000001));
}
float WTNoiseInternalVoronoiseMean(WTNoiseVoronoiseSettings v,int dimensions,WTNoiseFractal f)
{
    // Approximate folded ensemble mean. Model the reduced spread of the weighted
    // average with the effective sample count of a Gaussian kernel on unit-density
    // sites. The uniform surrogate is not the exact weighted-sum distribution;
    // jitter, correlated site values and short periodic tiles can change its mean.
    float width=1.772453850906*saturate(v.smoothness);
    float count=dimensions==1 ? width : dimensions==2 ? width*width : width*width*width;
    return WTNoiseInternalCellUniformMean(0.5,0.5*rsqrt(max(count,1)),f);
}
float WTNoiseInternalVoronoise(float3 p,WTNoiseSettings n,WTNoiseVoronoiseSettings v,int dimensions)
{
    WTNoiseFractal f=n.fractal;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f),mean=0;
    float2 frequency=float2(1,0);
    bool meanReady=false;
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin;
        float3 inputScale,q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility<1 && !meanReady) { mean=WTNoiseInternalVoronoiseMean(v,dimensions,f); meanReady=true; }
        if(visibility>0)
            value=WTNoiseInternalVoronoiseSample(q,origin,WTNoiseInternalSeed(n.seed,octave),period,dimensions,v);
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(sum*0.5+0.5);
}
#define WT_NOISE_VORONOISE_OVERLOAD(Type,Position,Dimensions) \
float WTNoiseVoronoise(Type p,WTNoiseSettings n,WTNoiseVoronoiseSettings v) { return WTNoiseInternalVoronoise(Position,n,v,Dimensions); } \
float WTNoiseVoronoise(Type p,WTNoiseSettings n) { return WTNoiseVoronoise(p,n,WTNoiseVoronoiseDefaults()); }
WT_NOISE_VORONOISE_OVERLOAD(float,float3(p,0,0),1)
WT_NOISE_VORONOISE_OVERLOAD(float2,float3(p,0),2)
WT_NOISE_VORONOISE_OVERLOAD(float3,p,3)
#undef WT_NOISE_VORONOISE_OVERLOAD
#endif
