#ifndef WT_NOISE_SPOTS_INCLUDED
#define WT_NOISE_SPOTS_INCLUDED
#include "WTNoiseCommon.hlsl"

#include "WTNoiseSpotsTypes.hlsl"

// @wt.feature(Dim1)
// @wt.feature(Dim2; MultiDim)
// @wt.feature(Dim3; MultiDim)
// @wt.feature(MultiDim)
// @wt.feature(Filtering)
// @wt.feature(Fractal)
// @wt.feature(Ordinary)

// @wt.block(Direction)
float3 WTNoiseInternalSpotDirection(float3 direction,int dimensions)
{
// @wt.if(Dim1)
// @wt.if(Ordinary)
    if(dimensions==1)
// @wt.endif
        return float3(1,0,0);
// @wt.endif
// @wt.if(MultiDim)
// @wt.if(Dim2)
    if(dimensions==2) direction.z=0;
// @wt.endif
    float largest=max(max(abs(direction.x),abs(direction.y)),abs(direction.z));
    if(largest==0) return float3(1,0,0);
    direction/=largest;
    return direction*rsqrt(dot(direction,direction));
// @wt.endif
}
// @wt.end
// @wt.block(Kernel)
float WTNoiseInternalSpotKernel(float distance,float softness)
{
    // Positive, nonoscillating compact kernel, with a flat center and C2 joins
    // at both ends of the soft edge. No random phase or signed impulse weights.
    float t=saturate((1-distance)/softness);
    return t*t*t*(10+t*(-15+6*t));
}
// @wt.end
// @wt.block(Value; Kernel)
float WTNoiseInternalSpotsValue(float3 p,int3 origin,int seed,int3 period,int dimensions,WTNoiseSpotSettings s)
{
    if(s.radius<=0 || s.density<=0) return 0;
    int3 baseCell=(int3)floor(p);
    float3 local=p-baseCell;
    baseCell+=origin;
    int extent=clamp((int)ceil(s.radius),1,2);
    int y=0,z=0;
    float uncovered=1;
    // Four candidate centers/cell, independently thinned by density/4. Fixed
    // integer limits: at most 20/100/500 candidates per octave in 1D/2D/3D.
    // Radius/elongation cannot enlarge this search; fractals use at most 8 octaves.
// @wt.if(Dim3)
    [loop] for(z=-(dimensions==3 ? extent : 0);z<=(dimensions==3 ? extent : 0);z++)
// @wt.endif
// @wt.if(MultiDim)
    [loop] for(y=-(dimensions>=2 ? extent : 0);y<=(dimensions>=2 ? extent : 0);y++)
// @wt.endif
    [loop] for(int x=-extent;x<=extent;x++)
    {
        int3 shift=int3(x,y,z);
        // Wrap only keys, retaining each periodic image's actual displacement.
        uint state=WTNoiseInternalHash(WTNoiseInternalHashCell(baseCell+shift,seed,period)^0xb5297a4du);
        [loop] for(int spot=0;spot<4;spot++)
        {
            state=WTNoiseInternalHash(state^0x9e3779b9u);
            float active=WTNoiseInternalUnit(state);
            state=WTNoiseInternalHash(state^0x68bc21ebu);
            float3 center=float3(WTNoiseInternalUnit(state),0,0);
// @wt.if(MultiDim)
            if(dimensions>=2)
            {
                state=WTNoiseInternalHash(state^0x63d83595u);
                center.y=WTNoiseInternalUnit(state);
            }
// @wt.endif
// @wt.if(Dim3)
            if(dimensions==3)
            {
                state=WTNoiseInternalHash(state^0xa511e9b3u);
                center.z=WTNoiseInternalUnit(state);
            }
// @wt.endif
            // Advance the same stream even for inactive centers: density edits
            // reveal/hide existing centers without moving the remaining ones.
            if(active>=s.density*0.25) continue;
            float3 d=(local-(float3)shift-center)/s.radius;
            float along=dot(d,s.direction);
            float3 across=(d-along*s.direction)*s.elongation;
            float squared=along*along+dot(across,across);
            if(squared>=1) continue;
            float kernel=WTNoiseInternalSpotKernel(sqrt(max(squared,0)),s.softness);
            // Smooth bounded union. No clamp ridge at overlaps or signed carrier.
            uncovered*=1-kernel;
        }
    }
    return saturate(1-uncovered);
}
// @wt.end
// @wt.block(Mean)
float WTNoiseInternalSpotMean(WTNoiseSpotSettings s,int dimensions)
{
    // Integral of the radial kernel: its falling edge is the survival function
    // of radius (1-softness)+softness*Beta(3,3). The first three radius moments
    // give exact kernel volumes. The overlap mean is a Poisson approximation to
    // the bounded cell distribution, not an exact integral of a particular seed.
    float edge=s.softness,volume=2*s.radius*(1-0.5*edge);
// @wt.if(Dim2)
    if(dimensions==2)
        volume=3.14159265359*s.radius*s.radius/s.elongation*(1-edge+(2.0/7.0)*edge*edge);
// @wt.endif
// @wt.if(Dim3)
    if(dimensions==3)
        volume=(4.18879020479*s.radius*s.radius*s.radius)/(s.elongation*s.elongation)*
            (1-1.5*edge+(6.0/7.0)*edge*edge-(5.0/28.0)*edge*edge*edge);
// @wt.endif
    return saturate(1-exp(-s.density*volume));
}
// @wt.end
// @wt.block(Sample; Direction, Value)
float WTNoiseInternalSpots(float3 p,WTNoiseSettings n,WTNoiseSpotSettings s,int dimensions)
{
    s.density=clamp(s.density,0,4); s.radius=clamp(s.radius,0,2);
    s.softness=clamp(s.softness,0.01,1);
    s.elongation=dimensions==1 ? 1 : clamp(s.elongation,1,8);
    s.direction=WTNoiseInternalSpotDirection(s.direction,dimensions);
    WTNoiseFractal f=n.fractal;
    float mean=0;
// @wt.if(Filtering)
    float ignored;
    mean=WTNoiseInternalFractalValue(WTNoiseInternalSpotMean(s,dimensions),f,ignored);
// @wt.endif
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    // Fade unresolved edges using the smallest feature width. This conservative
    // bounded filter needs no extra spatial samples, even for huge footprints.
// @wt.if(Filtering)
    float featureWidth=max(s.radius*s.softness/s.elongation,0.00001);
// @wt.endif
    int octave=0;
// @wt.if(Fractal)
    [loop] for(octave=0;octave<WTNoiseInternalOctaves(f);octave++)
// @wt.endif
    {
        int3 period,origin;
        float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float visibility=1;
// @wt.if(Filtering)
        visibility=WTNoiseInternalFilterWeight(n,inputScale/featureWidth);
// @wt.endif
        float value=0;
        if(visibility>0)
            value=WTNoiseInternalSpotsValue(q,origin,WTNoiseInternalSeed(n.seed,octave),period,dimensions,s);
        WTNoiseInternalAccumulateFiltered(value,f,visibility,mean,sum,amplitude);
// @wt.if(Fractal)
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
// @wt.endif
    }
    return saturate(sum*0.5+0.5);
}
// @wt.end
// Shares input units, precision, per-axis period fitting and fractal semantics with
// the other WT fields. n.dpdx/dpdy are explicit unwrapped input footprints; zero
// preserves point sampling. Settings/footprints must be constant across the pixel.
// Minification approaches an approximate mean; nonlinear fractal folds/weighted
// amplitudes are not integrated exactly. No analytic gradient/curl API for Spots.
// Unlike frequency-zero Gabor, these are positive spots on an empty background.
#define WT_NOISE_SPOTS_OVERLOAD(Type,Position,Dimensions) \
float WTNoiseSpots(Type p,WTNoiseSettings n,WTNoiseSpotSettings s) { return WTNoiseInternalSpots(Position,n,s,Dimensions); } \
float WTNoiseSpots(Type p,WTNoiseSettings n) { return WTNoiseSpots(p,n,WTNoiseSpotDefaults()); }
WT_NOISE_SPOTS_OVERLOAD(float,float3(p,0,0),1)
WT_NOISE_SPOTS_OVERLOAD(float2,float3(p,0),2)
WT_NOISE_SPOTS_OVERLOAD(float3,p,3)
#undef WT_NOISE_SPOTS_OVERLOAD
#endif
