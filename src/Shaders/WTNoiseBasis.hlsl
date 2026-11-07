#ifndef WHIMTEX_WT_NOISE_BASIS_INCLUDED
#define WHIMTEX_WT_NOISE_BASIS_INCLUDED
#include "WTNoiseBasisKernels.hlsl"
#include "WTNoiseDerivatives.hlsl"

// @wt.feature(Dim1; Plane)
// @wt.feature(Dim2; Plane)
// @wt.feature(Dim3)
// @wt.feature(Plane)
// @wt.feature(Grid)
// @wt.feature(Perlin; CurlLoop)
// @wt.feature(Value; CurlLoop)
// @wt.feature(CurlLoop)
// @wt.feature(ValueCubic)
// @wt.feature(OpenSimplex2)
// @wt.feature(OpenSimplex2S)
// @wt.feature(Filtering)
// @wt.feature(Curl3)


// @wt.block(Basis1D)
float WTNoiseInternalBasis1D(float p,int seed,int kind)
{
    int i=(int)floor(p);
    float t=p-floor(p);
// @wt.if(Perlin)
    if(kind==WT_NOISE_PERLIN)
    {
        float a=(WTNoiseInternalHash(asuint(_fnlHash2D(seed,i*PRIME_X,0)))&1u)!=0 ? t : -t;
        float b=(WTNoiseInternalHash(asuint(_fnlHash2D(seed,(i+1)*PRIME_X,0)))&1u)!=0 ? t-1 : 1-t;
        return lerp(a,b,_fnlInterpQuintic(t))*2;
    }
// @wt.endif
// @wt.if(!Perlin)
    float a=_fnlValCoord2D(seed,i*PRIME_X,0),b=_fnlValCoord2D(seed,(i+1)*PRIME_X,0);
// @wt.if(ValueCubic)
    if(kind==WT_NOISE_VALUE_CUBIC)
        return _fnlCubicLerp(_fnlValCoord2D(seed,(i-1)*PRIME_X,0),a,b,_fnlValCoord2D(seed,(i+2)*PRIME_X,0),t)/1.5;
// @wt.endif
    if(kind!=WT_NOISE_PERLIN) return lerp(a,b,_fnlInterpHermite(t));
// @wt.endif
    return 0;
}
// @wt.end
// Generated scalar graph paths select this block instead of the FNL kernel.
// Keep the same four row interpolations and final cubic interpolation; only
// share their hash call sites. Public WT calls retain _fnlSingleValueCubic2D.
// @wt.block(ValueCubic2DRows)
float WTNoiseInternalValueCubic2DRows(int seed,float x,float y)
{
    int x1=_fnlFastFloor(x), y1=_fnlFastFloor(y);
    float xs=x-(float)x1, ys=y-(float)y1;
    x1*=PRIME_X; y1*=PRIME_Y;
    int x0=x1-PRIME_X, y0=y1-PRIME_Y;
    int x2=x1+PRIME_X, y2=y1+PRIME_Y;
    int x3=x1+PRIME_X*2, y3=y1+FNL_DOUBLE_PRIME(PRIME_Y);
    float4 rows=0;
    [loop] for(int row=0;row<4;row++)
    {
        int rowY=row==0 ? y0 : row==1 ? y1 : row==2 ? y2 : y3;
        float cubicRow=_fnlCubicLerp(_fnlValCoord2D(seed,x0,rowY),_fnlValCoord2D(seed,x1,rowY),
            _fnlValCoord2D(seed,x2,rowY),_fnlValCoord2D(seed,x3,rowY),xs);
        rows=float4(rows.yzw,cubicRow);
    }
    return _fnlCubicLerp(rows.x,rows.y,rows.z,rows.w,ys)*(1/(1.5f*1.5f));
}
// @wt.end
// @wt.block(BasisValue)
float WTNoiseInternalBasisValue(float3 q,int seed,int dimensions,int kind)
{
// @wt.if(Grid)
// @wt.if(Dim1)
    if(dimensions==1 && kind>=3) return WTNoiseInternalBasis1D(q.x,seed,kind);
// @wt.endif
// @wt.endif
// @wt.if(Dim3)
    if(dimensions==3)
    {
// @wt.if(Perlin)
        if(kind==WT_NOISE_PERLIN) return _fnlSinglePerlin3D(seed,q.x,q.y,q.z);
// @wt.endif
// @wt.if(Value)
        if(kind==WT_NOISE_VALUE) return _fnlSingleValue3D(seed,q.x,q.y,q.z);
// @wt.endif
// @wt.if(ValueCubic)
        if(kind==WT_NOISE_VALUE_CUBIC) return _fnlSingleValueCubic3D(seed,q.x,q.y,q.z);
// @wt.endif
// @wt.if(OpenSimplex2)
        if(kind==WT_NOISE_OPENSIMPLEX2) return _fnlSingleOpenSimplex23D(seed,q.x,q.y,q.z);
// @wt.endif
// @wt.if(OpenSimplex2S)
        if(kind==WT_NOISE_OPENSIMPLEX2S) return _fnlSingleOpenSimplex2S3D(seed,q.x,q.y,q.z);
// @wt.endif
    }
// @wt.endif
// @wt.if(Plane)
    if(dimensions!=3)
    {
// @wt.if(Perlin)
        if(kind==WT_NOISE_PERLIN) return _fnlSinglePerlin2D(seed,q.x,q.y);
// @wt.endif
// @wt.if(Value)
        if(kind==WT_NOISE_VALUE) return _fnlSingleValue2D(seed,q.x,q.y);
// @wt.endif
// @wt.if(ValueCubic)
        if(kind==WT_NOISE_VALUE_CUBIC) return _fnlSingleValueCubic2D(seed,q.x,q.y);
// @wt.endif
// @wt.if(OpenSimplex2)
        if(kind==WT_NOISE_OPENSIMPLEX2) return _fnlSingleSimplex2D(seed,q.x,q.y);
// @wt.endif
// @wt.if(OpenSimplex2S)
        if(kind==WT_NOISE_OPENSIMPLEX2S) return _fnlSingleOpenSimplex2S2D(seed,q.x,q.y);
// @wt.endif
    }
// @wt.endif
    return 0;
}
// @wt.end
float WTNoiseInternalBasis(float3 p,WTNoiseSettings n,WTNoiseFractal f,int dimensions,int kind)
{
    int layout=kind<2 ? (dimensions==3 ? 3 : 2) : 1;
    int3 savedPeriod=WTNoiseInternalPeriod;
    int3 savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,layout,period,origin,inputScale);
        WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
        int seed=WTNoiseInternalSeed(n.seed,octave);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility>0) value=WTNoiseInternalBasisValue(q,seed,dimensions,kind);
        WTNoiseInternalAccumulateFiltered(value*0.5+0.5,f,visibility,0.5,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return saturate(sum*0.5+0.5);
}
float3 WTNoiseInternalBasisGradient(float3 p,WTNoiseSettings n,WTNoiseFractal f,int dimensions,int kind)
{
    int layout=kind<2 ? (dimensions==3 ? 3 : 2) : 1;
    int3 savedPeriod=WTNoiseInternalPeriod,savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float3 gradient=0,amplitudeGradient=0;
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,layout,period,origin,inputScale);
        WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
        float3 derivative=0;
        float visibility=WTNoiseInternalFilterWeight(n,inputScale),value=0;
        if(visibility>0) value=WTNoiseInternalBasisDerivative(q,WTNoiseInternalSeed(n.seed,octave),dimensions,kind,derivative);
        WTNoiseInternalAccumulateFiltered(value*0.5+0.5,derivative*inputScale*0.5,f,
            visibility,0.5,sum,amplitude,gradient,amplitudeGradient);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return sum>-1 && sum<1 ? gradient*0.5 : float3(0,0,0);
}
// Built-in graph nodes are single-octave sources. Keep their inner evaluation
// loop-free when an outer graph operation evaluates the source repeatedly.
// Retain the bounded accumulation steps rather than algebraically simplifying
// them: filtering, clamp boundaries and floating-point rounding are shared.
// @wt.block(Single; BasisValue)
float WTNoiseInternalBasisSingle(float3 p,WTNoiseSettings n,int dimensions,int kind)
{
    int layout=kind<2 ? (dimensions==3 ? 3 : 2) : 1;
    int3 savedPeriod=WTNoiseInternalPeriod,savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
    int3 period,origin; float3 inputScale;
    float3 q=WTNoiseInternalPosition(p,n,float2(1,0),dimensions,layout,period,origin,inputScale);
    WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
    float visibility=1,value=0;
// @wt.if(Filtering)
    visibility=WTNoiseInternalFilterWeight(n,inputScale);
// @wt.endif
    if(visibility>0) value=WTNoiseInternalBasisValue(q,WTNoiseInternalSeed(n.seed,0),dimensions,kind);
    WTNoiseFractal f=n.fractal; f.mode=WT_NOISE_NONE;
    float sum=0,amplitude=1;
    WTNoiseInternalAccumulateFiltered(value*0.5+0.5,f,visibility,0.5,sum,amplitude);
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return saturate(sum*0.5+0.5);
}
// @wt.end
// The caller owns the prepared domain and restores the kernel context.
// Keep each seed's accumulation and clamp independent.
// @wt.block(SamplePrepared)
float WTNoiseInternalBasisSamplePrepared(float3 q,int seed,WTNoiseFractal f,float3 inputScale,
    float visibility,int dimensions,int kind,out float3 gradient)
{
    float3 derivative=0;
    float value=0;
    if(visibility>0) value=WTNoiseInternalBasisDerivative(q,WTNoiseInternalSeed(seed,0),dimensions,kind,derivative);
    f.mode=WT_NOISE_NONE;
    float sum=0,amplitude=1;
    gradient=0; float3 amplitudeGradient=0;
    WTNoiseInternalAccumulateFiltered(value*0.5+0.5,derivative*inputScale*0.5,f,
        visibility,0.5,sum,amplitude,gradient,amplitudeGradient);
    gradient=sum>-1 && sum<1 ? gradient*0.5 : float3(0,0,0);
    return saturate(sum*0.5+0.5);
}
// @wt.end
// Value, gradient and the three independently seeded Curl fields share preparation.
// @wt.block(SampleSingle; SamplePrepared)
float WTNoiseInternalBasisSampleSingle(float3 p,WTNoiseSettings n,int dimensions,int kind,
    bool curl3,out float3 gradient,out float3 curl)
{
    int layout=kind<2 ? (dimensions==3 ? 3 : 2) : 1;
    int3 savedPeriod=WTNoiseInternalPeriod,savedOrigin=WTNoiseInternalOrigin;
    int savedLayout=WTNoiseInternalLayout;
    int3 period,origin; float3 inputScale;
    float3 q=WTNoiseInternalPosition(p,n,float2(1,0),dimensions,layout,period,origin,inputScale);
    WTNoiseInternalPeriod=period; WTNoiseInternalLayout=layout; WTNoiseInternalOrigin=origin;
    float visibility=1;
// @wt.if(Filtering)
    visibility=WTNoiseInternalFilterWeight(n,inputScale);
// @wt.endif
    float value=0; gradient=0; curl=0;
// @wt.if(Curl3)
    if(curl3 && dimensions==3)
    {
        float3 gy=0,gz=0;
// @wt.if(CurlLoop)
        if(kind>=3 && kind!=WT_NOISE_VALUE_CUBIC)
        {
            // One lattice kernel call site inside outer graph loops. Preserve
            // seed order and each field's independent accumulation and clamp.
            [loop] for(int fieldIndex=0;fieldIndex<3;fieldIndex++)
            {
                int seed=fieldIndex==0 ? n.seed : n.seed^(fieldIndex==1 ? 0x68bc21eb : 0x63d83595);
                float3 derivative;
                float fieldValue=WTNoiseInternalBasisSamplePrepared(q,seed,n.fractal,inputScale,visibility,dimensions,kind,derivative);
                if(fieldIndex==0) { value=fieldValue; gradient=derivative; }
                else if(fieldIndex==1) gy=derivative;
                else gz=derivative;
            }
        }
// @wt.endif
// @wt.if(!CurlLoop)
        if(kind<3 || kind==WT_NOISE_VALUE_CUBIC)
        {
            value=WTNoiseInternalBasisSamplePrepared(q,n.seed,n.fractal,inputScale,visibility,dimensions,kind,gradient);
            WTNoiseInternalBasisSamplePrepared(q,n.seed^0x68bc21eb,n.fractal,inputScale,visibility,dimensions,kind,gy);
            WTNoiseInternalBasisSamplePrepared(q,n.seed^0x63d83595,n.fractal,inputScale,visibility,dimensions,kind,gz);
        }
// @wt.endif
        curl=float3(gz.y-gy.z,gradient.z-gz.x,gy.x-gradient.y);
    }
    else
// @wt.endif
        value=WTNoiseInternalBasisSamplePrepared(q,n.seed,n.fractal,inputScale,visibility,dimensions,kind,gradient);
    WTNoiseInternalPeriod=savedPeriod; WTNoiseInternalLayout=savedLayout; WTNoiseInternalOrigin=savedOrigin;
    return value;
}
float WTNoiseInternalBasisSampleSingle(float3 p,WTNoiseSettings n,int dimensions,int kind,out float3 gradient)
{
    float3 curl;
    return WTNoiseInternalBasisSampleSingle(p,n,dimensions,kind,false,gradient,curl);
}
// @wt.end
// @wt.block(GradientSingle; SampleSingle)
float3 WTNoiseInternalBasisGradientSingle(float3 p,WTNoiseSettings n,int dimensions,int kind)
{
    float3 gradient;
    WTNoiseInternalBasisSampleSingle(p,n,dimensions,kind,gradient);
    return gradient;
}
// @wt.end
// @wt.block(CurlSingle; GradientSingle)
float3 WTNoiseInternalBasisCurlSingle(float3 p,WTNoiseSettings n,int dimensions,int kind)
{
    float3 gx=WTNoiseInternalBasisGradientSingle(p,n,dimensions,kind);
    if(dimensions==2) return float3(gx.y,-gx.x,0);
    n.seed^=0x68bc21eb;
    float3 gy=WTNoiseInternalBasisGradientSingle(p,n,dimensions,kind);
    n.seed^=0x68bc21eb^0x63d83595;
    float3 gz=WTNoiseInternalBasisGradientSingle(p,n,dimensions,kind);
    return float3(gz.y-gy.z,gx.z-gz.x,gy.x-gx.y);
}
// @wt.end
#define WT_NOISE_BASIS_FUNCTION(Name,Kind) \
float Name(float p,WTNoiseSettings n) { return WTNoiseInternalBasis(float3(p,0,0),n,n.fractal,1,Kind); } \
float Name(float2 p,WTNoiseSettings n) { return WTNoiseInternalBasis(float3(p,0),n,n.fractal,2,Kind); } \
float Name(float3 p,WTNoiseSettings n) { return WTNoiseInternalBasis(p,n,n.fractal,3,Kind); }
WT_NOISE_BASIS_FUNCTION(WTNoisePerlin,WT_NOISE_PERLIN)
WT_NOISE_BASIS_FUNCTION(WTNoiseValue,WT_NOISE_VALUE)
WT_NOISE_BASIS_FUNCTION(WTNoiseValueCubic,WT_NOISE_VALUE_CUBIC)
WT_NOISE_BASIS_FUNCTION(WTNoiseOpenSimplex2,WT_NOISE_OPENSIMPLEX2)
WT_NOISE_BASIS_FUNCTION(WTNoiseOpenSimplex2S,WT_NOISE_OPENSIMPLEX2S)
#undef WT_NOISE_BASIS_FUNCTION
#endif
