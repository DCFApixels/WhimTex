#ifndef WHIMTEX_WT_NOISE_WARP_INCLUDED
#define WHIMTEX_WT_NOISE_WARP_INCLUDED
#include "WTNoiseBasis.hlsl"
#include "WTNoiseWarpTypes.hlsl"

// @wt.feature(Runtime; Dim1, Dim2, Dim3, OpenSimplex2, Reduced, BasicGrid, None, Progressive, Independent, Filtering)
// @wt.feature(Dim1; Plane)
// @wt.feature(Dim2; Plane)
// @wt.feature(Dim3)
// @wt.feature(Plane)
// @wt.feature(OpenSimplex2; Simplex)
// @wt.feature(Reduced; Simplex)
// @wt.feature(BasicGrid)
// @wt.feature(Simplex)
// @wt.feature(None)
// @wt.feature(Progressive; Fractal)
// @wt.feature(Independent; Fractal)
// @wt.feature(Fractal)
// @wt.feature(Filtering)

// @wt.block(Displacement)
float3 WTNoiseInternalDisplacement(float3 p,WTNoiseSettings n,WTNoiseWarpSettings w,int dimensions)
{
    if(w.strength==0) return 0;
    int layout=1;
// @wt.if(Simplex)
// @wt.if(Runtime)
    if(w.algorithm<2)
    {
// @wt.endif
// @wt.if(Plane)
// @wt.if(Runtime)
        if(dimensions!=3)
// @wt.endif
            layout=2;
// @wt.endif
// @wt.if(Dim3)
// @wt.if(Runtime)
        if(dimensions==3)
// @wt.endif
            layout=3;
// @wt.endif
// @wt.if(Runtime)
    }
// @wt.endif
// @wt.endif
    int3 savedPeriod=WTNoiseInternalPeriod;
    int3 savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
// @wt.if(Runtime)
    fnl_state state=fnlCreateState(n.seed);
    state.domain_warp_type=clamp(w.algorithm,0,2);
// @wt.endif
    // Warp has its own accumulation modes; count/frequency/gain use common settings.
    WTNoiseFractal f=n.fractal;
    f.mode=WT_NOISE_NONE;
// @wt.if(Fractal)
// @wt.if(Runtime)
    if(w.mode!=WT_NOISE_WARP_NONE)
// @wt.endif
        f.mode=WT_NOISE_FBM;
// @wt.endif
    float amplitude=w.strength*WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    float3 displacement=0;
    int octave=0;
// @wt.if(Fractal)
    [loop] for(;octave<WTNoiseInternalOctaves(f);octave++)
// @wt.endif
    {
        int3 period,origin; float3 inputScale;
        float3 source=p+float3(0,0,0);
// @wt.if(Progressive)
// @wt.if(Runtime)
        if(w.mode==WT_NOISE_WARP_PROGRESSIVE)
// @wt.endif
            source=p+displacement;
// @wt.endif
        float3 q=WTNoiseInternalPosition(source,n,frequency,dimensions,layout,period,origin,inputScale);
        WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
        float3 delta=0;
        int seed=WTNoiseInternalSeed(n.seed,octave);
// @wt.if(Dim3)
// @wt.if(Runtime)
        if(dimensions==3)
// @wt.endif
            _fnlDoSingleDomainWarp3D(state,seed,amplitude,1,q.x,q.y,q.z,delta.x,delta.y,delta.z);
// @wt.endif
// @wt.if(Runtime)
        else
// @wt.endif
// @wt.if(Plane)
            _fnlDoSingleDomainWarp2D(state,seed,amplitude,1,q.x,q.y,delta.x,delta.y);
// @wt.endif
// @wt.if(Plane)
// @wt.if(Simplex)
        if(layout==2) delta.xy=float2(delta.x+delta.y,delta.y-delta.x)*0.7071067811865475;
// @wt.endif
// @wt.endif
// @wt.if(Plane)
// @wt.if(Runtime)
        if(dimensions<3)
// @wt.endif
            delta.z=0;
// @wt.endif
// @wt.if(Dim1)
// @wt.if(Runtime)
        if(dimensions<2)
// @wt.endif
            delta.y=0;
// @wt.endif
        float visibility=1;
// @wt.if(Filtering)
        visibility=WTNoiseInternalFilterWeight(n,inputScale);
// @wt.endif
        displacement+=delta*visibility;
// @wt.if(Fractal)
        amplitude*=saturate(f.gain); frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
// @wt.endif
    }
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return displacement;
}
// @wt.end
// Public + Runtime selects the ordinary helper and every public overload.
// The caller still supplies WarpTypes and the ordinary FNL declarations.
// @wt.block(Public; Displacement)
float WTNoiseDisplacement(float p,WTNoiseSettings n,WTNoiseWarpSettings w) { return WTNoiseInternalDisplacement(float3(p,0,0),n,w,1).x; }
float2 WTNoiseDisplacement(float2 p,WTNoiseSettings n,WTNoiseWarpSettings w) { return WTNoiseInternalDisplacement(float3(p,0),n,w,2).xy; }
float3 WTNoiseDisplacement(float3 p,WTNoiseSettings n,WTNoiseWarpSettings w) { return WTNoiseInternalDisplacement(p,n,w,3); }
float WTNoiseDisplacement(float p,WTNoiseSettings n) { return WTNoiseDisplacement(p,n,WTNoiseWarpDefaults()); }
float2 WTNoiseDisplacement(float2 p,WTNoiseSettings n) { return WTNoiseDisplacement(p,n,WTNoiseWarpDefaults()); }
float3 WTNoiseDisplacement(float3 p,WTNoiseSettings n) { return WTNoiseDisplacement(p,n,WTNoiseWarpDefaults()); }
// @wt.end
#endif
