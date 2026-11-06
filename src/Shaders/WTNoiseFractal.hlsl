#ifndef WHIMTEX_WT_NOISE_FRACTAL_INCLUDED
#define WHIMTEX_WT_NOISE_FRACTAL_INCLUDED
// Included by WTNoiseCommon after the settings and precision types.
int WTNoiseInternalSeed(int seed,int octave,int step)
{
    return asint(asuint(seed)+asuint(step)*(uint)octave);
}
int WTNoiseInternalSeed(int seed,int octave) { return WTNoiseInternalSeed(seed,octave,1); }
int WTNoiseInternalOctaves(WTNoiseFractal f) { return f.mode==WT_NOISE_NONE ? 1 : clamp(f.octaves,1,8); }
float WTNoiseInternalAmplitude(WTNoiseFractal f)
{
    float total=1,amplitude=1;
    int octaves=WTNoiseInternalOctaves(f);
    [unroll] for(int i=1;i<8;i++)
        if(i<octaves) { amplitude*=saturate(f.gain); total+=amplitude; }
    return rcp(total);
}
float WTNoiseInternalNextFrequency(float frequency,float lacunarity)
{
    return frequency*clamp(lacunarity,0.01,16);
}
float2 WTNoiseInternalNextFrequency(float2 frequency,float lacunarity)
{
    WTNoiseWide3 value=WTNoiseWideMultiply(WTNoiseWide(frequency.x.xxx,frequency.y.xxx),WTNoiseWide(clamp(lacunarity,0.01,16).xxx));
    return float2(value.high.x,value.low.x);
}
// Largest singular value of the 3x2 footprint, including diagonal/sheared pixels.
float WTNoiseInternalFootprint(float3 dx,float3 dy)
{
    float a=dot(dx,dx),b=dot(dx,dy),c=dot(dy,dy);
    return sqrt(max(0,0.5*(a+c+sqrt(max(0,(a-c)*(a-c)+4*b*b)))));
}
float WTFractalInternalVisibility(float3 dx,float3 dy)
{
    return 1-smoothstep(0.25,1.0,WTNoiseInternalFootprint(dx,dy));
}

// Bounded noise and unbounded numeric branches share folds and weights. Their
// accumulation policies remain distinct: fixed noise amplitude vs weighted mean.
#define WTFractalValue float
#include "WTNoiseFractalValues.hlsl"
#undef WTFractalValue
#define WTFractalValue float2
#include "WTNoiseFractalValues.hlsl"
#undef WTFractalValue
#define WTFractalValue float3
#include "WTNoiseFractalValues.hlsl"
#undef WTFractalValue
#define WTFractalValue float4
#include "WTNoiseFractalValues.hlsl"
#undef WTFractalValue

float WTNoiseInternalFractalValue(float value,WTNoiseFractal f,out float slope)
{
    return WTFractalInternalFold(value,f,true,slope);
}
void WTNoiseInternalAccumulate(float value,WTNoiseFractal f,inout float sum,inout float amplitude)
{
    float ignored;
    value=WTNoiseInternalFractalValue(value,f,ignored);
    sum+=(value*2-1)*amplitude;
    amplitude*=WTFractalInternalWeight(value,f);
}
void WTNoiseInternalAccumulate(float value,float3 valueGradient,WTNoiseFractal f,
    inout float sum,inout float amplitude,inout float3 sumGradient,inout float3 amplitudeGradient)
{
    float slope;
    value=WTNoiseInternalFractalValue(value,f,slope);
    valueGradient*=slope;
    sumGradient+=2*valueGradient*amplitude+(value*2-1)*amplitudeGradient;
    sum+=(value*2-1)*amplitude;
    float weight=WTFractalInternalWeight(value,f);
    // Weighted octaves make amplitude position-dependent: retain its derivative too.
    amplitudeGradient=amplitudeGradient*weight+amplitude*saturate(f.gain)*saturate(f.weightedStrength)*valueGradient;
    amplitude*=weight;
}
// Family-specific means are supplied AFTER folding; retain noise's fixed normalization.
void WTNoiseInternalAccumulateFiltered(float value,WTNoiseFractal f,float visibility,float mean,
    inout float sum,inout float amplitude)
{
    if(visibility>=1) { WTNoiseInternalAccumulate(value,f,sum,amplitude); return; }
    float ignored;
    value=lerp(mean,WTNoiseInternalFractalValue(value,f,ignored),visibility);
    sum+=(value*2-1)*amplitude;
    amplitude*=WTFractalInternalWeight(value,f);
}
void WTNoiseInternalAccumulateFiltered(float value,float3 gradient,WTNoiseFractal f,float visibility,float mean,
    inout float sum,inout float amplitude,inout float3 sumGradient,inout float3 amplitudeGradient)
{
    if(visibility>=1) { WTNoiseInternalAccumulate(value,gradient,f,sum,amplitude,sumGradient,amplitudeGradient); return; }
    float slope;
    value=lerp(mean,WTNoiseInternalFractalValue(value,f,slope),visibility);
    gradient*=slope*visibility;
    sumGradient+=2*gradient*amplitude+(value*2-1)*amplitudeGradient;
    sum+=(value*2-1)*amplitude;
    amplitudeGradient=amplitudeGradient*WTFractalInternalWeight(value,f)
        +amplitude*saturate(f.gain)*saturate(f.weightedStrength)*gradient;
    amplitude*=WTFractalInternalWeight(value,f);
}

float3 WTFractalInternalVector(float p) { return float3(p,0,0); }
float3 WTFractalInternalVector(float2 p) { return float3(p,0); }
float3 WTFractalInternalVector(float3 p) { return p; }
float WTFractalInternalCoordinate_float(float3 p) { return p.x; }
float2 WTFractalInternalCoordinate_float2(float3 p) { return p.xy; }
float3 WTFractalInternalCoordinate_float3(float3 p) { return p; }
#define WT_FRACTAL_INTERNAL_DIM_float 1
#define WT_FRACTAL_INTERNAL_DIM_float2 2
#define WT_FRACTAL_INTERNAL_DIM_float3 3

// A callback accepts ordinary floats. Keep high/low preparation until that boundary;
// it cannot recover features smaller than the final coordinate's float spacing.
float3 WTFractalInternalPosition(float3 p,WTNoiseSettings n,float2 frequency,int dimensions,out float3 inputScale)
{
    float3 enabled=float3(n.period.x>0,n.period.y>0 && dimensions>=2,n.period.z>0 && dimensions==3);
    WTNoiseWide3 period=WTNoiseWideSelect(enabled,WTNoiseWide(float3(1,1,1)),WTNoiseWide(n.period,n.periodLow));
    float3 signScale=float3(n.scale.x<0 ? -1 : 1,n.scale.y<0 ? -1 : 1,n.scale.z<0 ? -1 : 1);
    WTNoiseWide3 scale=WTNoiseWideMultiply(WTNoiseWide(abs(n.scale),n.scaleLow*signScale),WTNoiseWide(frequency.x.xxx,frequency.y.xxx));
    scale=WTNoiseWideSelect(float3(scale.high.x<0.00001,scale.high.y<0.00001,scale.high.z<0.00001),scale,WTNoiseWide(float3(0.00001,0.00001,0.00001)));
    inputScale=scale.high+scale.low;
    if(dimensions<3) inputScale.z=0;
    if(dimensions<2) inputScale.y=0;
    WTNoiseWide3 position=WTNoiseWideAdd(WTNoiseWide(p),WTNoiseWide(n.offset,n.offsetLow));
    position=WTNoiseWideSelect(enabled,position,WTNoiseWideRepeat(position,period));
    position=WTNoiseWideMultiply(position,scale);
    return position.high+position.low;
}

// Source must be ValueType Source(PositionType position, int seed), declared first.
// The macro belongs at global scope. It generates one ordinary HLSL function.
// Evaluate a single octave directly; retain rolled loops for arbitrary callbacks.
#define WT_DEFINE_FRACTAL(Name,Source,PositionType,ValueType) \
ValueType Name(PositionType position,WTNoiseSettings settings) \
{ \
    WTNoiseFractal f=settings.fractal; \
    int count=WTNoiseInternalOctaves(f); \
    ValueType result=(ValueType)0; \
    if(count==1) \
    { \
        float3 ignoredScale; ValueType ignoredSlope; \
        float3 p=WTFractalInternalPosition(WTFractalInternalVector(position),settings,float2(1,0),WT_FRACTAL_INTERNAL_DIM_##PositionType,ignoredScale); \
        ValueType value=Source(WTFractalInternalCoordinate_##PositionType(p),settings.seed); \
        result=WTFractalInternalFold(value,f,false,ignoredSlope); \
    } \
    else \
    { \
        ValueType sum=(ValueType)0,total=(ValueType)0,amplitude=(ValueType)1; \
        float2 frequency=float2(1,0); \
        [loop] for(int octave=0;octave<max(count,2);octave++) \
        { \
            float3 inputScale; \
            float3 p=WTFractalInternalPosition(WTFractalInternalVector(position),settings,frequency,WT_FRACTAL_INTERNAL_DIM_##PositionType,inputScale); \
            ValueType value=Source(WTFractalInternalCoordinate_##PositionType(p),WTNoiseInternalSeed(settings.seed,octave)); \
            float visibility=WTFractalInternalVisibility(settings.dpdx*inputScale,settings.dpdy*inputScale); \
            WTFractalInternalAccumulate(value,f,visibility,octave,sum,total,amplitude); \
            frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity); \
        } \
        result=WTFractalInternalResolve(sum,total); \
    } \
    return result; \
}
// Graph evaluation binds the source expression and dynamic evaluation context.
// Kept out of the public include; the template emits this block inside its caller.
#if defined(WHIMTEX_WT_FRACTAL_GRAPH_TEMPLATE)
// @wt.feature(Cascade)
// @wt.feature(Mean)
// @wt.feature(Filtering)
// @wt.block(GraphEvaluation)
WTFractalValue WTFractalGraphResult;
{
    WTNoiseFractal f;
    f.mode=WTFractalGraphMode; f.octaves=(int)(WTFractalGraphOctaves);
    f.lacunarity=(WTFractalGraphLacunarity); f.gain=(WTFractalGraphGain);
    f.weightedStrength=(WTFractalGraphWeighted); f.pingPongStrength=(WTFractalGraphPingPong);
    int seedStep=(int)(WTFractalGraphSeedStep), originalSeed=_GraphNoiseSeed;
    float2 originalDx=_GraphSampleDx, originalDy=_GraphSampleDy;
    // @wt.if(Cascade)
    WTFractalValue sum=0, weight=1; float amplitude=1;
    // @wt.endif
    // @wt.if(Mean)
    WTFractalValue sum=0, total=0, amplitude=1;
    // @wt.endif
    float frequency=1;
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        _GraphNoiseSeed=WTNoiseInternalSeed(originalSeed,octave,seedStep);
        _GraphSampleDx=originalDx*frequency; _GraphSampleDy=originalDy*frequency;
        WTFractalValue value=WTFractalGraphSource;
        float visibility=1;
        // @wt.if(Filtering)
        visibility=WTFractalInternalVisibility(float3(_GraphSampleDx,0),float3(_GraphSampleDy,0));
        // @wt.endif
        // @wt.if(Cascade)
        WTFractalInternalCascade(value,WTFractalGraphCascade,f.gain,(WTFractalGraphBias),(WTFractalGraphFeedback),visibility,octave,sum,weight,amplitude);
        // @wt.endif
        // @wt.if(Mean)
        WTFractalInternalAccumulate(value,f,visibility,octave,sum,total,amplitude);
        // @wt.endif
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    // @wt.if(Cascade)
    WTFractalGraphResult=sum;
    // @wt.endif
    // @wt.if(Mean)
    WTFractalGraphResult=WTFractalInternalResolve(sum,total);
    // @wt.endif
    _GraphNoiseSeed=originalSeed; _GraphSampleDx=originalDx; _GraphSampleDy=originalDy;
}
// @wt.end
#endif
#endif
