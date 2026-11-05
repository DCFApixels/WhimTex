// Frozen before unification; source SHA256 dca6816e36f62406f4ef8772b4fc22c1370a3493ab5f4d4931f0570d681bf088.
// Test-only symbols are prefixed; algorithms and arithmetic are unchanged.
#ifndef ReferenceWHIMTEX_WT_NOISE_CELLULAR_INCLUDED
#define ReferenceWHIMTEX_WT_NOISE_CELLULAR_INCLUDED
#include "Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseBasis.hlsl"

#define ReferenceWT_NOISE_CELL_UNIFORM 0
#define ReferenceWT_NOISE_CELL_FIXED_RADIUS 1
#define ReferenceWT_NOISE_DISTANCE_EUCLIDEAN 0
#define ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED 1
#define ReferenceWT_NOISE_DISTANCE_MANHATTAN 2
#define ReferenceWT_NOISE_DISTANCE_HYBRID 3
#define ReferenceWT_NOISE_CELL_VALUE 0
#define ReferenceWT_NOISE_CELL_F1 1
#define ReferenceWT_NOISE_CELL_F2 2
#define ReferenceWT_NOISE_CELL_ADD 3
#define ReferenceWT_NOISE_CELL_SUBTRACT 4
#define ReferenceWT_NOISE_CELL_MULTIPLY 5
#define ReferenceWT_NOISE_CELL_DIVIDE 6
#define ReferenceWT_NOISE_CELL_SMOOTH_F1 7

struct ReferenceWTNoiseCellularSettings
{
    int distribution;
    int metric;
    int output;
    float jitter;
    float smoothness;
    float evolution;
};
ReferenceWTNoiseCellularSettings ReferenceWTNoiseCellularDefaults()
{
    ReferenceWTNoiseCellularSettings c;
    c.distribution=ReferenceWT_NOISE_CELL_UNIFORM; c.metric=ReferenceWT_NOISE_DISTANCE_EUCLIDEAN;
    c.output=ReferenceWT_NOISE_CELL_F1; c.jitter=1; c.smoothness=0.5; c.evolution=0;
    return c;
}
struct ReferenceWTNoiseCellularSample1D { float distance1,distance2,value; uint id; float toPoint; };
struct ReferenceWTNoiseCellularSample2D { float distance1,distance2,value; uint id; float2 toPoint; };
struct ReferenceWTNoiseCellularSample3D { float distance1,distance2,value; uint id; float3 toPoint; };
struct ReferenceWTNoiseInternalCell
{
    float distance1,distance2,value;
    uint id;
    float3 toPoint;
    int3 cell;
};
float ReferenceWTNoiseInternalMetric(float3 d,int metric)
{
    d=abs(d);
    float squared=dot(d,d),manhattan=d.x+d.y+d.z;
    return metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN ? sqrt(squared) :
        metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? squared :
        metric==ReferenceWT_NOISE_DISTANCE_MANHATTAN ? manhattan : manhattan+squared;
}
float ReferenceWTNoiseInternalCellRadius(int dimensions,ReferenceWTNoiseCellularSettings c)
{
    // The 1D distribution uses the same displacement magnitude as 2D.
    return (c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? (dimensions==3 ? 0.39614353 : 0.43701595) : 0.5)*saturate(c.jitter);
}
bool ReferenceWTNoiseInternalCellBefore(int3 a,int3 b)
{
    // Order unwrapped cells, not hashed IDs: repeats are distinct sites and hash
    // collisions must not affect ties. Translation by a period preserves this order.
    return a.x<b.x || (a.x==b.x && (a.y<b.y || (a.y==b.y && a.z<b.z)));
}
float ReferenceWTNoiseInternalEvolutionSpeed(uint key)
{
    return (0.5+WTNoiseInternalUnit(key))*((key&1u)==0 ? 1 : -1);
}
float3 ReferenceWTNoiseInternalEvolveFeature(float3 offset,uint id,int dimensions,float evolution,bool radial)
{
    float3 speed=float3(
        ReferenceWTNoiseInternalEvolutionSpeed(WTNoiseInternalHash(id^0xb5297a4du)),
        ReferenceWTNoiseInternalEvolutionSpeed(WTNoiseInternalHash(id^0x1b56c4e9u)),
        ReferenceWTNoiseInternalEvolutionSpeed(WTNoiseInternalHash(id^0x7f4a7c15u)));
    float3 sine,cosine; sincos(evolution*speed,sine,cosine);
    if(radial)
    {
        // 1D is the projection of a rotation; 2D/3D preserve displacement length.
        if(dimensions==1) return float3(offset.x*cosine.x,0,0);
        offset.xy=float2(offset.x*cosine.x-offset.y*sine.x,offset.x*sine.x+offset.y*cosine.x);
        if(dimensions==3)
            offset.yz=float2(offset.y*cosine.y-offset.z*sine.y,offset.y*sine.y+offset.z*cosine.y);
        return offset;
    }
    // sin(asin(q) + angle), avoiding an inverse trig evaluation for every site.
    // Each component stays in its original cell; Evolution=0 uses the old path.
    float3 q=2*offset-1;
    return saturate(0.5+0.5*(q*cosine+sqrt(max(1-q*q,0))*sine));
}
float3 ReferenceWTNoiseInternalFeature(int3 cell,int seed,int3 period,int dimensions,ReferenceWTNoiseCellularSettings c,out uint id)
{
    float3 offset;
    if(c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS)
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
        if(c.evolution!=0 && c.jitter>0)
            offset=ReferenceWTNoiseInternalEvolveFeature(offset,id,dimensions,c.evolution,true);
        offset*=ReferenceWTNoiseInternalCellRadius(dimensions,c);
    }
    else
    {
        id=WTNoiseInternalHashCell(cell,seed,period);
        float3 random=float3(WTNoiseInternalUnit(id),WTNoiseInternalUnit(WTNoiseInternalHash(id^0x9e3779b9u)),
            WTNoiseInternalUnit(WTNoiseInternalHash(id^0x68bc21ebu)));
        if(c.evolution!=0 && c.jitter>0)
            random=ReferenceWTNoiseInternalEvolveFeature(random,id,dimensions,c.evolution,false);
        offset=0.5+(random-0.5)*saturate(c.jitter);
    }
    if(dimensions<3) offset.z=0;
    if(dimensions<2) offset.y=0;
    return offset;
}
void ReferenceWTNoiseInternalCellCandidate(int3 cell,int3 center,float3 local,int seed,int3 period,int dimensions,
    ReferenceWTNoiseCellularSettings c,inout ReferenceWTNoiseInternalCell result)
{
    uint id;
    float3 d=(float3)(cell-center)+ReferenceWTNoiseInternalFeature(cell,seed,period,dimensions,c,id)-local;
    float distance=ReferenceWTNoiseInternalMetric(d,c.metric);
    if(distance<result.distance1 || (distance==result.distance1 && ReferenceWTNoiseInternalCellBefore(cell,result.cell)))
    {
        result.distance2=result.distance1; result.distance1=distance;
        result.toPoint=d; result.cell=cell; result.id=id;
        result.value=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? asint(id)*(0.5/2147483648.0)+0.5 :
            WTNoiseInternalUnit(WTNoiseInternalHash(id^0x68bc21ebu));
    }
    else result.distance2=min(result.distance2,distance);
}
void ReferenceWTNoiseInternalCellBounds(float3 lower,float3 upper,int dimensions,out int3 lo,out int3 hi)
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
ReferenceWTNoiseInternalCell ReferenceWTNoiseInternalCells(float3 p,int3 origin,int seed,int3 period,int dimensions,ReferenceWTNoiseCellularSettings c)
{
    int3 center=(int3)floor(p+(c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    center+=origin;
    ReferenceWTNoiseInternalCell result;
    result.distance1=1e20; result.distance2=1e20; result.value=0; result.id=0; result.toPoint=0; result.cell=center;
    ReferenceWTNoiseInternalCellCandidate(center,center,local,seed,period,dimensions,c,result);
    ReferenceWTNoiseInternalCellCandidate(center+int3(1,0,0),center,local,seed,period,dimensions,c,result);
    float anchor=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    float radius=ReferenceWTNoiseInternalCellRadius(dimensions,c);
    // Invert the one-axis metric to bound every site that can improve F2.
    // The rationalized hybrid inverse also works for very small distances.
    float extent=c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? sqrt(result.distance2) :
        c.metric==ReferenceWT_NOISE_DISTANCE_HYBRID ? 2*result.distance2/(sqrt(1+4*result.distance2)+1) : result.distance2;
    // Conservative rounding allowance affects pruning only, never feature positions.
    radius+=0.00001;
    int3 lo,hi;
    ReferenceWTNoiseInternalCellBounds(local-anchor-radius-extent,local-anchor+radius+extent,dimensions,lo,hi);
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        // These sites have already seeded F1/F2. Enumerating either twice would
        // make F2 equal to F1 and bias the difference/ratio outputs.
        if(y==0 && z==0 && (x==0 || x==1)) continue;
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(ReferenceWTNoiseInternalMetric(box,c.metric)>result.distance2) continue;
        ReferenceWTNoiseInternalCellCandidate(center+shift,center,local,seed,period,dimensions,c,result);
    }
    return result;
}
float ReferenceWTNoiseInternalCellMetricExtent(float distance,int metric)
{
    return metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? sqrt(distance) :
        metric==ReferenceWT_NOISE_DISTANCE_HYBRID ? 2*distance/(sqrt(1+4*distance)+1) : distance;
}
float ReferenceWTNoiseInternalCellCovering(int dimensions,ReferenceWTNoiseCellularSettings c)
{
    // The feature of the containing/nearest cell is at most 0.5+radius away
    // on each active axis. This bound also covers the fixed-radius distribution.
    float axis=0.5+ReferenceWTNoiseInternalCellRadius(dimensions,c);
    return ReferenceWTNoiseInternalMetric(float3(axis,dimensions>=2 ? axis : 0,dimensions==3 ? axis : 0),c.metric);
}
float ReferenceWTNoiseInternalCellWindow(float distance,float covering,float width)
{
    // Compact C1 support, independent of the query's nearest-site identity.
    float t=saturate((distance-covering)/width);
    return (1-t)*(1-t)*(1+2*t);
}
float ReferenceWTNoiseInternalSmoothF1(float3 p,int3 origin,int seed,int3 period,int dimensions,
    ReferenceWTNoiseCellularSettings c,float nearest)
{
    // Log-sum-exp soft minimum. Subtracting the exact nearest distance in the
    // exponent prevents underflow; it cancels algebraically from the soft minimum.
    // Smoothness is 0..1, mapping to a temperature of 0..0.25 metric units.
    float width=0.25*saturate(c.smoothness);
    if(width<=0.000001) return nearest;
    float covering=ReferenceWTNoiseInternalCellCovering(dimensions,c),support=covering+8*width;
    // In the largest case (Manhattan), support <= dimensions+2. The anchor/radius
    // offset adds less than one integer cell, so CellBounds' existing cap suffices.
    float anchor=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    int3 center=(int3)floor(p+(c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    center+=origin;
    float radius=ReferenceWTNoiseInternalCellRadius(dimensions,c)+0.00001;
    float extent=ReferenceWTNoiseInternalCellMetricExtent(support,c.metric);
    int3 lo,hi;
    ReferenceWTNoiseInternalCellBounds(local-anchor-radius-extent,local-anchor+radius+extent,dimensions,lo,hi);
    float weight=0;
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(ReferenceWTNoiseInternalMetric(box,c.metric)>=support) continue;
        uint id;
        float3 d=(float3)shift+ReferenceWTNoiseInternalFeature(center+shift,seed,period,dimensions,c,id)-local;
        float distance=ReferenceWTNoiseInternalMetric(d,c.metric);
        if(distance<support)
            weight+=exp2(-(distance-nearest)*(1.442695040889/width))*ReferenceWTNoiseInternalCellWindow(distance,covering,8*width);
    }
    // At least the nearest site has unit weight. The final scalar normalization
    // clamps negative soft distances; auxiliary geometry remains the hard F1/F2.
    return max(nearest-width*log(max(weight,1)),0);
}
float ReferenceWTNoiseInternalCellValue(ReferenceWTNoiseInternalCell v,ReferenceWTNoiseCellularSettings c,int dimensions)
{
    if(c.output==ReferenceWT_NOISE_CELL_VALUE) return v.value;
    // One fixed distance scale for F1 and F2, independent of distribution/jitter.
    // Two distinct sites always exist in the containing/nearest cell and a neighbor
    // toward the query. With jitter <= 1 their axis distances are at most 1.5 on
    // that axis and 1 elsewhere: squared <= d+1.25, Manhattan <= d+0.5.
    // Euclidean and squared Euclidean therefore retain their square relationship.
    float squaredBound=dimensions+1.25,manhattanBound=dimensions+0.5;
    float bound=c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN ? sqrt(squaredBound) :
        c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? squaredBound :
        c.metric==ReferenceWT_NOISE_DISTANCE_MANHATTAN ? manhattanBound : manhattanBound+squaredBound;
    if(c.output==ReferenceWT_NOISE_CELL_F2) return saturate(v.distance2/bound);
    if(c.output==ReferenceWT_NOISE_CELL_ADD) return saturate((v.distance1+v.distance2)/(2*bound));
    if(c.output==ReferenceWT_NOISE_CELL_SUBTRACT) return saturate((v.distance2-v.distance1)/bound);
    if(c.output==ReferenceWT_NOISE_CELL_MULTIPLY) return saturate((v.distance1*v.distance2)/(bound*bound));
    if(c.output==ReferenceWT_NOISE_CELL_DIVIDE) return v.distance2>0 ? saturate(v.distance1/v.distance2) : 0;
    return saturate(v.distance1/bound);
}
float ReferenceWTNoiseInternalCellTriangleIntegral(float t)
{
    float cycle=floor(t*0.5),r=t-2*cycle;
    return cycle+(r<=1 ? 0.5*r*r : 1-0.5*(2-r)*(2-r));
}
float ReferenceWTNoiseInternalCellUniformMean(float center,float halfWidth,WTNoiseFractal f)
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
        return saturate((ReferenceWTNoiseInternalCellTriangleIntegral(tb)-ReferenceWTNoiseInternalCellTriangleIntegral(ta))/(tb-ta));
    }
    return (a+b)*0.5;
}
float3 ReferenceWTNoiseInternalCellMeanPoint(int i,int dimensions)
{
    // A bounded 16-point cubature of the positive half of a regular unit cell.
    float3 u=(float3(i,(i*9+3)%16,(i*11+13)%16)+0.5)*(0.5/16);
    if(dimensions<3) u.z=0;
    if(dimensions<2) u.y=0;
    return u;
}
float2 ReferenceWTNoiseInternalCellMeanScale(int dimensions,ReferenceWTNoiseCellularSettings c)
{
    // Approximate F1/F2 ensemble mean ratios at full jitter relative to a regular
    // lattice. Rounded estimates from an independent CPU geometry experiment:
    // 4000 (position,seed) pairs per dimension/distribution, all four metrics.
    // The 1D uniform endpoints use the spacing moments. Interpolation is quadratic
    // in jitter. These are not per-seed/per-period integrals or exact higher moments.
    float2 ratio;
    bool fixedRadius=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS;
    if(dimensions==1)
        ratio=c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? (fixedRadius ? float2(2.146,1.18) : float2(1.5,1.071429)) :
            c.metric==ReferenceWT_NOISE_DISTANCE_HYBRID ? (fixedRadius ? float2(1.573,1.08) : float2(1.25,1.03125)) :
            (fixedRadius ? float2(1.382,1) : float2(1.166667,1));
    else if(dimensions==2)
        ratio=c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN ? (fixedRadius ? float2(1.14,1) : float2(1.12,1)) :
            c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? (fixedRadius ? float2(1.37,1.04) : float2(1.32,1.03)) :
            c.metric==ReferenceWT_NOISE_DISTANCE_MANHATTAN ? (fixedRadius ? float2(1.10,1.05) : float2(1.08,1.05)) :
            (fixedRadius ? float2(1.17,1.05) : float2(1.14,1.05));
    else
        ratio=c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN ? (fixedRadius ? float2(1.06,1.02) : float2(1.08,1.02)) :
            c.metric==ReferenceWT_NOISE_DISTANCE_EUCLIDEAN_SQUARED ? (fixedRadius ? float2(1.14,1.06) : float2(1.20,1.07)) :
            c.metric==ReferenceWT_NOISE_DISTANCE_MANHATTAN ? float2(1.01,1.05) :
            (fixedRadius ? float2(1.04,1.06) : float2(1.07,1.07));
    float jitter=saturate(c.jitter);
    return lerp(float2(1,1),ratio,jitter*jitter);
}
float ReferenceWTNoiseInternalCellularMean(ReferenceWTNoiseCellularSettings c,int dimensions,WTNoiseFractal f)
{
    if(c.output==ReferenceWT_NOISE_CELL_VALUE) return ReferenceWTNoiseInternalCellUniformMean(0.5,0.5,f);
    // The surrogate retains dimension, metric, output, jitter and fractal folding.
    // Nonlinear distance combinations and Smooth F1 are approximate. No feature
    // searches, texture/cache state or dependence on the current query are needed.
    // Highly folded PingPong approaches its cycle mean; avoid cubature aliasing.
    if(f.mode==WT_NOISE_PINGPONG && f.pingPongStrength>=16) return 0.5;
    float2 scale=ReferenceWTNoiseInternalCellMeanScale(dimensions,c);
    float mean=0;
    [unroll] for(int i=0;i<16;i++)
    {
        float3 u=ReferenceWTNoiseInternalCellMeanPoint(i,dimensions),other=u;
        if(u.x>=u.y && u.x>=u.z) other.x=1-u.x;
        else if(u.y>=u.z) other.y=1-u.y;
        else other.z=1-u.z;
        ReferenceWTNoiseInternalCell v=(ReferenceWTNoiseInternalCell)0;
        v.distance1=ReferenceWTNoiseInternalMetric(u,c.metric)*scale.x;
        v.distance2=max(v.distance1,ReferenceWTNoiseInternalMetric(other,c.metric)*scale.y);
        float width=0.25*saturate(c.smoothness);
        if(c.output==ReferenceWT_NOISE_CELL_SMOOTH_F1 && width>0.000001)
            v.distance1=max(v.distance1-width*log(1+dimensions*exp2(-(v.distance2-v.distance1)*(1.442695040889/width))),0);
        float ignored;
        mean+=WTNoiseInternalFractalValue(ReferenceWTNoiseInternalCellValue(v,c,dimensions),f,ignored)*(1.0/16);
    }
    float cycleBlend=f.mode==WT_NOISE_PINGPONG ? saturate((f.pingPongStrength-8)*(1.0/8)) : 0;
    return saturate(lerp(mean,0.5,cycleBlend));
}
float ReferenceWTNoiseInternalCellular(float3 p,WTNoiseSettings n,ReferenceWTNoiseCellularSettings c,int dimensions,out ReferenceWTNoiseInternalCell firstOctave)
{
    WTNoiseFractal f=n.fractal;
    firstOctave=(ReferenceWTNoiseInternalCell)0;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    float mean=0;
    bool meanReady=false;
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin;
        float3 inputScale,q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility<1 && !meanReady) { mean=ReferenceWTNoiseInternalCellularMean(c,dimensions,f); meanReady=true; }
        if(octave==0 || visibility>0)
        {
            int seed=WTNoiseInternalSeed(n.seed,octave);
            ReferenceWTNoiseInternalCell v=ReferenceWTNoiseInternalCells(q,origin,seed,period,dimensions,c);
            if(octave==0) firstOctave=v;
            if(visibility>0)
            {
                if(c.output==ReferenceWT_NOISE_CELL_SMOOTH_F1)
                    v.distance1=ReferenceWTNoiseInternalSmoothF1(q,origin,seed,period,dimensions,c,v.distance1);
                value=ReferenceWTNoiseInternalCellValue(v,c,dimensions);
            }
        }
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(sum*0.5+0.5);
}
// Optional geometry describes the first octave. The scalar return still uses n.fractal.
#define ReferenceWT_NOISE_CELLULAR_OVERLOAD(Type,Position,Dimensions,Sample,Direction) \
float ReferenceWTNoiseCellular(Type p,WTNoiseSettings n,ReferenceWTNoiseCellularSettings c,out Sample firstOctave) { ReferenceWTNoiseInternalCell v; float value=ReferenceWTNoiseInternalCellular(Position,n,c,Dimensions,v); firstOctave.distance1=v.distance1; firstOctave.distance2=v.distance2; firstOctave.value=v.value; firstOctave.id=v.id; firstOctave.toPoint=Direction; return value; } \
float ReferenceWTNoiseCellular(Type p,WTNoiseSettings n,ReferenceWTNoiseCellularSettings c) { ReferenceWTNoiseInternalCell ignored; return ReferenceWTNoiseInternalCellular(Position,n,c,Dimensions,ignored); } \
float ReferenceWTNoiseCellular(Type p,WTNoiseSettings n) { return ReferenceWTNoiseCellular(p,n,ReferenceWTNoiseCellularDefaults()); }
ReferenceWT_NOISE_CELLULAR_OVERLOAD(float,float3(p,0,0),1,ReferenceWTNoiseCellularSample1D,v.toPoint.x)
ReferenceWT_NOISE_CELLULAR_OVERLOAD(float2,float3(p,0),2,ReferenceWTNoiseCellularSample2D,v.toPoint.xy)
ReferenceWT_NOISE_CELLULAR_OVERLOAD(float3,p,3,ReferenceWTNoiseCellularSample3D,v.toPoint)
#undef ReferenceWT_NOISE_CELLULAR_OVERLOAD

struct ReferenceWTNoiseVoronoiSettings { int distribution; float jitter; float evolution; };
ReferenceWTNoiseVoronoiSettings ReferenceWTNoiseVoronoiDefaults()
{
    ReferenceWTNoiseVoronoiSettings v; v.distribution=ReferenceWT_NOISE_CELL_UNIFORM; v.jitter=1; v.evolution=0; return v;
}
float ReferenceWTNoiseInternalEdge(float3 p,int3 origin,int seed,int3 period,int dimensions,ReferenceWTNoiseVoronoiSettings v)
{
    ReferenceWTNoiseCellularSettings c=ReferenceWTNoiseCellularDefaults();
    c.distribution=v.distribution; c.jitter=v.jitter; c.evolution=v.evolution;
    ReferenceWTNoiseInternalCell nearest=ReferenceWTNoiseInternalCells(p,origin,seed,period,dimensions,c);
    float anchor=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    float radius=ReferenceWTNoiseInternalCellRadius(dimensions,c);
    // Every position is within covering of some feature. Each Voronoi facet
    // therefore joins sites at most 2*covering apart; this bounds the complete
    // search, including distinct repeated sites when a period is only one cell.
    float covering=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? sqrt((float)dimensions)*0.5+radius :
        sqrt((float)dimensions)*(0.5+radius);
    radius+=0.00001; covering+=0.00001;
    uint ignored;
    float3 offset=ReferenceWTNoiseInternalFeature(nearest.cell,seed,period,dimensions,c,ignored);
    int3 lo,hi;
    ReferenceWTNoiseInternalCellBounds(offset-anchor-radius-2*covering,offset-anchor+radius+2*covering,dimensions,lo,hi);
    float edge=1e20;
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        if(x==0 && y==0 && z==0) continue;
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-offset)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(dot(box,box)>4*covering*covering) continue;
        float3 between=(float3)shift+ReferenceWTNoiseInternalFeature(nearest.cell+shift,seed,period,dimensions,c,ignored)-offset;
        float squared=dot(between,between);
        // Coincident sites have no bisector. Do not discard merely close sites:
        // they can define the nearest facet, including at a periodic seam.
        if(squared>0)
            edge=min(edge,dot(nearest.toPoint+0.5*between,between*rsqrt(squared)));
    }
    return max(edge,0);
}
float ReferenceWTNoiseInternalVoronoiMean(ReferenceWTNoiseVoronoiSettings v,int dimensions,WTNoiseFractal f)
{
    // Regular-lattice edge cubature with a small ensemble jitter correction from
    // the same geometry experiment as Cellular. This is an approximate folded
    // distance mean, not 0.5 and not the exact mean of an arbitrary periodic tile.
    if(f.mode==WT_NOISE_PINGPONG && f.pingPongStrength>=16) return 0.5;
    // In 1D a Voronoi interval has length 1+(offset[i+1]-offset[i-1])/2;
    // its spatial mean edge distance is E[length^2]/4. Use that moment directly.
    float fullJitter=dimensions==1 ? (v.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 1.09549147 : 1.041666667) :
        dimensions==2 ? 1 : 1.04;
    float jitter=saturate(v.jitter),scale=lerp(1,fullJitter,jitter*jitter)/sqrt((float)dimensions),mean=0;
    [unroll] for(int i=0;i<16;i++)
    {
        float3 u=ReferenceWTNoiseInternalCellMeanPoint(i,dimensions);
        float edge=(0.5-max(u.x,max(u.y,u.z)))*scale,ignored;
        mean+=WTNoiseInternalFractalValue(edge,f,ignored)*(1.0/16);
    }
    float cycleBlend=f.mode==WT_NOISE_PINGPONG ? saturate((f.pingPongStrength-8)*(1.0/8)) : 0;
    return saturate(lerp(mean,0.5,cycleBlend));
}
float ReferenceWTNoiseInternalVoronoi(float3 p,WTNoiseSettings n,ReferenceWTNoiseVoronoiSettings v,int dimensions)
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
        if(visibility<1 && !meanReady) { mean=ReferenceWTNoiseInternalVoronoiMean(v,dimensions,f); meanReady=true; }
        if(visibility>0)
            value=ReferenceWTNoiseInternalEdge(q,origin,WTNoiseInternalSeed(n.seed,octave),period,dimensions,v)/sqrt((float)dimensions);
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(sum*0.5+0.5);
}
#define ReferenceWT_NOISE_VORONOI_OVERLOAD(Type,Position,Dimensions) \
float ReferenceWTNoiseVoronoiEdge(Type p,WTNoiseSettings n,ReferenceWTNoiseVoronoiSettings v) { return ReferenceWTNoiseInternalVoronoi(Position,n,v,Dimensions); } \
float ReferenceWTNoiseVoronoiEdge(Type p,WTNoiseSettings n) { return ReferenceWTNoiseVoronoiEdge(p,n,ReferenceWTNoiseVoronoiDefaults()); }
ReferenceWT_NOISE_VORONOI_OVERLOAD(float,float3(p,0,0),1)
ReferenceWT_NOISE_VORONOI_OVERLOAD(float2,float3(p,0),2)
ReferenceWT_NOISE_VORONOI_OVERLOAD(float3,p,3)
#undef ReferenceWT_NOISE_VORONOI_OVERLOAD

struct ReferenceWTNoiseVoronoiseSettings { int distribution; float jitter; float smoothness; float evolution; };
ReferenceWTNoiseVoronoiseSettings ReferenceWTNoiseVoronoiseDefaults()
{
    ReferenceWTNoiseVoronoiseSettings v;
    v.distribution=ReferenceWT_NOISE_CELL_UNIFORM; v.jitter=1; v.smoothness=0.5; v.evolution=0;
    return v;
}
float ReferenceWTNoiseInternalVoronoiseSample(float3 p,int3 origin,int seed,int3 period,int dimensions,ReferenceWTNoiseVoronoiseSettings v)
{
    ReferenceWTNoiseCellularSettings c=ReferenceWTNoiseCellularDefaults();
    c.distribution=v.distribution; c.jitter=v.jitter; c.evolution=v.evolution;
    ReferenceWTNoiseInternalCell nearest=ReferenceWTNoiseInternalCells(p,origin,seed,period,dimensions,c);
    float smoothness=saturate(v.smoothness),temperature=0.5*smoothness*smoothness;
    if(temperature<=0.000001) return nearest.value;
    // Normalize positive weights of random cell values, never distances. The
    // nearest-site subtraction stabilizes exponentials without changing ratios.
    // A fixed compact window makes entering/leaving candidate sites C1 continuous.
    float covering=ReferenceWTNoiseInternalCellCovering(dimensions,c),support=covering+1;
    float anchor=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 0 : 0.5;
    int3 center=(int3)floor(p+(c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? 0.5 : 0));
    if(dimensions<3) center.z=0;
    if(dimensions<2) center.y=0;
    float3 local=p-center;
    center+=origin;
    float radius=ReferenceWTNoiseInternalCellRadius(dimensions,c)+0.00001;
    int3 lo,hi;
    ReferenceWTNoiseInternalCellBounds(local-anchor-radius-support,local-anchor+radius+support,dimensions,lo,hi);
    float sum=0,weight=0,nearestSquared=dot(nearest.toPoint,nearest.toPoint);
    [loop] for(int z=lo.z;z<=hi.z;z++) [loop] for(int y=lo.y;y<=hi.y;y++) [loop] for(int x=lo.x;x<=hi.x;x++)
    {
        int3 shift=int3(x,y,z);
        float3 box=max(abs((float3)shift+anchor-local)-radius,0);
        if(dimensions<3) box.z=0;
        if(dimensions<2) box.y=0;
        if(dot(box,box)>=support*support) continue;
        uint id;
        float3 d=(float3)shift+ReferenceWTNoiseInternalFeature(center+shift,seed,period,dimensions,c,id)-local;
        float squared=dot(d,d);
        if(squared>=support*support) continue;
        float w=exp2(-(squared-nearestSquared)*(1.442695040889/temperature))*
            ReferenceWTNoiseInternalCellWindow(sqrt(squared),covering,1);
        float value=c.distribution==ReferenceWT_NOISE_CELL_FIXED_RADIUS ? asint(id)*(0.5/2147483648.0)+0.5 :
            WTNoiseInternalUnit(WTNoiseInternalHash(id^0x68bc21ebu));
        sum+=value*w; weight+=w;
    }
    return saturate(sum/max(weight,0.000001));
}
float ReferenceWTNoiseInternalVoronoiseMean(ReferenceWTNoiseVoronoiseSettings v,int dimensions,WTNoiseFractal f)
{
    // Approximate folded ensemble mean. Model the reduced spread of the weighted
    // average with the effective sample count of a Gaussian kernel on unit-density
    // sites. The uniform surrogate is not the exact weighted-sum distribution;
    // jitter, correlated site values and short periodic tiles can change its mean.
    float width=1.772453850906*saturate(v.smoothness);
    float count=dimensions==1 ? width : dimensions==2 ? width*width : width*width*width;
    return ReferenceWTNoiseInternalCellUniformMean(0.5,0.5*rsqrt(max(count,1)),f);
}
float ReferenceWTNoiseInternalVoronoise(float3 p,WTNoiseSettings n,ReferenceWTNoiseVoronoiseSettings v,int dimensions)
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
        if(visibility<1 && !meanReady) { mean=ReferenceWTNoiseInternalVoronoiseMean(v,dimensions,f); meanReady=true; }
        if(visibility>0)
            value=ReferenceWTNoiseInternalVoronoiseSample(q,origin,WTNoiseInternalSeed(n.seed,octave),period,dimensions,v);
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    return saturate(sum*0.5+0.5);
}
#define ReferenceWT_NOISE_VORONOISE_OVERLOAD(Type,Position,Dimensions) \
float ReferenceWTNoiseVoronoise(Type p,WTNoiseSettings n,ReferenceWTNoiseVoronoiseSettings v) { return ReferenceWTNoiseInternalVoronoise(Position,n,v,Dimensions); } \
float ReferenceWTNoiseVoronoise(Type p,WTNoiseSettings n) { return ReferenceWTNoiseVoronoise(p,n,ReferenceWTNoiseVoronoiseDefaults()); }
ReferenceWT_NOISE_VORONOISE_OVERLOAD(float,float3(p,0,0),1)
ReferenceWT_NOISE_VORONOISE_OVERLOAD(float2,float3(p,0),2)
ReferenceWT_NOISE_VORONOISE_OVERLOAD(float3,p,3)
#undef ReferenceWT_NOISE_VORONOISE_OVERLOAD
#endif
