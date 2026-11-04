#ifndef WHIMTEX_WT_NOISE_COMMON_INCLUDED
#define WHIMTEX_WT_NOISE_COMMON_INCLUDED
#include "NoisePrecision.hlsl"

#define WT_NOISE_NONE 0
#define WT_NOISE_FBM 1
#define WT_NOISE_RIDGED 2
#define WT_NOISE_PINGPONG 3
#define WT_NOISE_OPENSIMPLEX2 0
#define WT_NOISE_OPENSIMPLEX2S 1
#define WT_NOISE_PERLIN 3
#define WT_NOISE_VALUE_CUBIC 4
#define WT_NOISE_VALUE 5

struct WTNoiseFractal
{
    int mode;
    int octaves;
    float lacunarity;
    float gain;
    float weightedStrength;
    float pingPongStrength;
};
struct WTNoiseSettings
{
    int seed;
    float3 scale;
    float3 offset;
    float3 period;
    WTNoiseFractal fractal;
    float3 scaleLow;
    float3 offsetLow;
    float3 periodLow;
    // Pixel footprint in unwrapped input units; zero preserves point evaluation.
    float3 dpdx;
    float3 dpdy;
};
WTNoiseSettings WTNoiseDefaults(int seed=1337)
{
    WTNoiseSettings n;
    n.seed=seed; n.scale=1; n.offset=0; n.period=0;
    n.scaleLow=0; n.offsetLow=0; n.periodLow=0;
    n.dpdx=0; n.dpdy=0;
    n.fractal.mode=WT_NOISE_NONE; n.fractal.octaves=3; n.fractal.lacunarity=2;
    n.fractal.gain=0.5; n.fractal.weightedStrength=0; n.fractal.pingPongStrength=2;
    return n;
}
// CPU WTNoiseDomainBinding supplies these six vectors under a caller-owned prefix.
WTNoiseSettings WTNoiseDefaults(int seed,float3 scale,float3 scaleLow,float3 offset,float3 offsetLow,float3 period,float3 periodLow)
{
    WTNoiseSettings n=WTNoiseDefaults(seed);
    n.scale=scale; n.scaleLow=scaleLow; n.offset=offset; n.offsetLow=offsetLow;
    n.period=period; n.periodLow=periodLow; return n;
}
int WTNoiseInternalMod(int x,int period)
{
    // Unsigned magnitude also represents abs(INT_MIN), without signed overflow.
    uint divisor=(uint)max(period,1);
    uint magnitude=x<0 ? 0u-asuint(x) : asuint(x);
    uint remainder=magnitude%divisor;
    return (int)(x<0 && remainder!=0 ? divisor-remainder : remainder);
}
int WTNoiseInternalDivide(int x,int positiveDivisor)
{
    uint magnitude=x<0 ? 0u-asuint(x) : asuint(x);
    uint quotient=magnitude/(uint)positiveDivisor;
    // Retain signed division's truncation toward zero, including negative odd values.
    return asint(x<0 ? 0u-quotient : quotient);
}
int3 WTNoiseInternalWrap(int3 cell,int3 period)
{
    return int3(period.x>0 ? WTNoiseInternalMod(cell.x,period.x) : cell.x,
        period.y>0 ? WTNoiseInternalMod(cell.y,period.y) : cell.y,
        period.z>0 ? WTNoiseInternalMod(cell.z,period.z) : cell.z);
}
uint WTNoiseInternalHash(uint x)
{
    x^=x>>16; x*=0x7feb352du; x^=x>>15; x*=0x846ca68bu; return x^(x>>16);
}
uint WTNoiseInternalHashCell(int3 cell,int seed,int3 period)
{
    cell=WTNoiseInternalWrap(cell,period);
    return WTNoiseInternalHash(asuint(cell.x)^WTNoiseInternalHash(asuint(cell.y)^0x9e3779b9u)
        ^WTNoiseInternalHash(asuint(cell.z)^0x68bc21ebu)^WTNoiseInternalHash(asuint(seed)));
}
float WTNoiseInternalUnit(uint hash) { return (hash>>8)*(1.0/16777216.0); }
int WTNoiseInternalSeed(int seed,int octave) { return asint(asuint(seed)+(uint)octave); }
int WTNoiseInternalOctaves(WTNoiseFractal f) { return f.mode==WT_NOISE_NONE ? 1 : clamp(f.octaves,1,8); }
float WTNoiseInternalAmplitude(WTNoiseFractal f)
{
    float total=1, amplitude=1;
    // At most seven scalar additions; specialize None without a zero-trip loop.
    int octaves=WTNoiseInternalOctaves(f);
    [unroll] for(int i=1;i<8;i++)
        if(i<octaves) { amplitude*=saturate(f.gain); total+=amplitude; }
    return rcp(total);
}
// Slope is with respect to the incoming value. At clamp/fold cusps choose zero.
float WTNoiseInternalFractalValue(float value,WTNoiseFractal f,out float slope)
{
    slope=value>0 && value<1 ? 1 : 0;
    value=saturate(value);
    if(f.mode==WT_NOISE_RIDGED)
    {
        slope*=-2*sign(value*2-1);
        value=1-abs(value*2-1);
    }
    else if(f.mode==WT_NOISE_PINGPONG)
    {
        float strength=max(f.pingPongStrength,0);
        float t=value*2*strength;
        t-=floor(t*0.5)*2;
        slope*=t==0 ? 0 : -2*strength*sign(t-1);
        value=1-abs(t-1);
    }
    return value;
}
void WTNoiseInternalAccumulate(float value,WTNoiseFractal f,inout float sum,inout float amplitude)
{
    float ignored;
    value=WTNoiseInternalFractalValue(value,f,ignored);
    sum+=(value*2-1)*amplitude;
    amplitude*=saturate(f.gain)*lerp(1,value,saturate(f.weightedStrength));
}
void WTNoiseInternalAccumulate(float value,float3 valueGradient,WTNoiseFractal f,
    inout float sum,inout float amplitude,inout float3 sumGradient,inout float3 amplitudeGradient)
{
    float slope;
    value=WTNoiseInternalFractalValue(value,f,slope);
    valueGradient*=slope;
    sumGradient+=2*valueGradient*amplitude+(value*2-1)*amplitudeGradient;
    sum+=(value*2-1)*amplitude;
    float weight=saturate(f.gain)*lerp(1,value,saturate(f.weightedStrength));
    // Weighted octaves make amplitude position-dependent: retain its derivative too.
    amplitudeGradient=amplitudeGradient*weight+amplitude*saturate(f.gain)*saturate(f.weightedStrength)*valueGradient;
    amplitude*=weight;
}
// Period is in input-coordinate units. Each octave fits an integer number of cells.
// Layout 2 is the rectangular 2D simplex lattice; layout 3 is the rotated 3D lattice.
float3 WTNoiseInternalPosition(float3 p,WTNoiseSettings n,float2 frequency,int dimensions,int layout,
    out int3 period,out int3 origin,out float3 inputScale)
{
    WTNoiseWide3 unit=WTNoiseWide(float3(1,1,1));
    if(layout==3) unit=WTNoiseWide(float3(3,3,3));
    else if(layout==2) unit=WTNoiseWide(float3(0.816496580927726,1.414213562373095,1),float3(-2.9713753346709564e-8,2.4203234305630872e-8,0));
    float3 enabled=float3(n.period.x>0,n.period.y>0 && dimensions>=2,n.period.z>0 && dimensions==3);
    WTNoiseWide3 requested=WTNoiseWideSelect(enabled,WTNoiseWide(float3(1,1,1)),WTNoiseWide(n.period,n.periodLow));
    float3 signScale=float3(n.scale.x<0 ? -1 : 1,n.scale.y<0 ? -1 : 1,n.scale.z<0 ? -1 : 1);
    WTNoiseWide3 scale=WTNoiseWideMultiply(WTNoiseWide(abs(n.scale),n.scaleLow*signScale),WTNoiseWide(frequency.x.xxx,frequency.y.xxx));
    float3 minimum=float3(scale.high.x<0.00001,scale.high.y<0.00001,scale.high.z<0.00001);
    scale=WTNoiseWideSelect(minimum,scale,WTNoiseWide(float3(0.00001,0.00001,0.00001)));
    WTNoiseWide3 count=WTNoiseWideFloor(WTNoiseWideAdd(WTNoiseWideDivide(WTNoiseWideMultiply(requested,scale),unit),WTNoiseWide(float3(0.5,0.5,0.5))));
    // Keep rounded counts split: above 2^24 a float cannot represent every integer.
    // The caller must keep periods and transformed indices within the int lattice range.
    WTNoiseWide3 cells=WTNoiseWideSelect(float3(count.high.x<1,count.high.y<1,count.high.z<1),count,WTNoiseWide(float3(1,1,1)));
    period=(int3)(cells.high*enabled)+(int3)(cells.low*enabled);
    if(layout==3) period*=3;
    WTNoiseWide3 fitted=WTNoiseWideDivide(WTNoiseWideMultiply(cells,unit),requested);
    scale=WTNoiseWideSelect(enabled,scale,fitted);
    // Unrotated domain Jacobian; layout 1 uses it directly. Keep fitted periods and
    // high/low preparation shared with the sampled coordinate, including minimum scale.
    inputScale=scale.high+scale.low;
    if(dimensions<3) inputScale.z=0;
    if(dimensions<2) inputScale.y=0;
    WTNoiseWide3 position=WTNoiseWideAdd(WTNoiseWide(p),WTNoiseWide(n.offset,n.offsetLow));
    WTNoiseWide3 wrapped=WTNoiseWideRepeat(position,requested);
    position=WTNoiseWideSelect(enabled,position,wrapped);
    position=WTNoiseWideMultiply(position,scale);
    if(dimensions<3) { position.high.z=0; position.low.z=0; }
    if(dimensions<2) { position.high.y=layout==2 ? 0.371 : 0; position.low.y=0; }
    if(layout==2)
    {
        WTNoiseWide3 a=WTNoiseWide(position.high.xyx,position.low.xyx);
        WTNoiseWide3 b=WTNoiseWide(position.high.yxx*float3(-1,1,0),position.low.yxx*float3(-1,1,0));
        position=WTNoiseWideMultiply(WTNoiseWideAdd(a,b),WTNoiseWide(float3(0.7071067811865475,0.7071067811865475,0),float3(1.2101617152815436e-8,1.2101617152815436e-8,0)));
        WTNoiseWide3 sum=WTNoiseWideAdd(WTNoiseWide(position.high.xxx,position.low.xxx),WTNoiseWide(position.high.yyy,position.low.yyy));
        WTNoiseWide3 skew=WTNoiseWideMultiply(sum,WTNoiseWide(float3(0.3660254037844386,0.3660254037844386,0),float3(-1.4258698000091381e-8,-1.4258698000091381e-8,0)));
        position=WTNoiseWideAdd(position,skew);
    }
    else if(layout==3)
    {
        WTNoiseWide3 sum=WTNoiseWideAdd(WTNoiseWideAdd(WTNoiseWide(position.high.xxx,position.low.xxx),WTNoiseWide(position.high.yyy,position.low.yyy)),WTNoiseWide(position.high.zzz,position.low.zzz));
        sum=WTNoiseWideMultiply(sum,WTNoiseWide(float3(2.0/3.0,2.0/3.0,2.0/3.0),float3(-1.9868214962137642e-8,-1.9868214962137642e-8,-1.9868214962137642e-8)));
        position=WTNoiseWideAdd(sum,WTNoiseWideNegate(position));
    }
    return WTNoiseWideFraction(position,origin);
}
float3 WTNoiseInternalPosition(float3 p,WTNoiseSettings n,float2 frequency,int dimensions,int layout,out int3 period,out int3 origin)
{
    float3 ignored;
    return WTNoiseInternalPosition(p,n,frequency,dimensions,layout,period,origin,ignored);
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
float WTNoiseInternalFilterWeight(WTNoiseSettings n,float3 inputScale)
{
    float footprint=WTNoiseInternalFootprint(n.dpdx*inputScale,n.dpdy*inputScale);
    return 1-smoothstep(0.25,1.0,footprint);
}
// Bounded octave filtering; mean is a family-specific approximation AFTER folding.
// This is not exact integration of discontinuities or nonlinear transformations.
void WTNoiseInternalAccumulateFiltered(float value,WTNoiseFractal f,float visibility,float mean,
    inout float sum,inout float amplitude)
{
    if(visibility>=1) { WTNoiseInternalAccumulate(value,f,sum,amplitude); return; }
    float ignored;
    value=lerp(mean,WTNoiseInternalFractalValue(value,f,ignored),visibility);
    sum+=(value*2-1)*amplitude;
    amplitude*=saturate(f.gain)*lerp(1,value,saturate(f.weightedStrength));
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
    amplitudeGradient=amplitudeGradient*(saturate(f.gain)*lerp(1,value,saturate(f.weightedStrength)))
        +amplitude*saturate(f.gain)*saturate(f.weightedStrength)*gradient;
    amplitude*=saturate(f.gain)*lerp(1,value,saturate(f.weightedStrength));
}

// Invocation-local hash context. Public calls restore it before returning.
static int3 WTNoiseInternalPeriod=0;
static int3 WTNoiseInternalOrigin=0;
static int WTNoiseInternalLayout=0;
// sin/cos of a flow cycle; restored by each Flow entry point.
static float2 WTNoiseInternalRotation=float2(0,1);
int WTNoiseInternalFnlHash(int seed,int3 p)
{
    p+=WTNoiseInternalOrigin;
    if(WTNoiseInternalLayout==2)
    {
        int h=p.x+p.y,v=p.x-p.y;
        if(WTNoiseInternalPeriod.x>0) h=WTNoiseInternalMod(h,2*WTNoiseInternalPeriod.x);
        if(WTNoiseInternalPeriod.y>0) v=WTNoiseInternalMod(v,2*WTNoiseInternalPeriod.y);
        p.xy=int2(WTNoiseInternalDivide(h+v,2),WTNoiseInternalDivide(h-v,2));
    }
    else if(WTNoiseInternalLayout==3)
    {
        [unroll] for(int axis=0;axis<3;axis++)
            if(WTNoiseInternalPeriod[axis]>0)
            {
                int3 basis=axis==0 ? int3(-1,2,2) : axis==1 ? int3(2,-1,2) : int3(2,2,-1);
                int h=p.x*basis.x+p.y*basis.y+p.z*basis.z;
                int length=max(3*WTNoiseInternalPeriod[axis],1);
                int q=WTNoiseInternalDivide(h-WTNoiseInternalMod(h,length),length);
                p-=q*(int)((uint)WTNoiseInternalPeriod[axis]/3u)*basis;
            }
    }
    else p=WTNoiseInternalWrap(p,WTNoiseInternalPeriod);
    return (seed^(p.x*501125321)^(p.y*1136930381)^(p.z*1720413743))*0x27d4eb2d;
}
#endif
