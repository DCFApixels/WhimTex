#ifndef WHIMTEX_WT_NOISE_CAUSTICS_INCLUDED
#define WHIMTEX_WT_NOISE_CAUSTICS_INCLUDED
#include "WTNoiseCommon.hlsl"
#include "WTNoiseCausticsTypes.hlsl"

// OpenSimplex2S derivative kernel: KdotJPG, CC0, https://www.shadertoy.com/view/wlc3zr
// Permutation polynomial hash credit: Stefan Gustavson.
// Source snapshot supplied 2026-11-09; see ThirdPartyNotices.md.
// The signed kernel and ImproveXY orientation preserve the reference pattern.
// @wt.feature(Periodic)
// @wt.block(Kernel)
float4 WTNoiseInternalCausticsMod(float4 p,float divisor) { return p-floor(p/divisor)*divisor; }
float4 WTNoiseInternalCausticsPermute(float4 p) { return p*(p*34+133); }
float3 WTNoiseInternalCausticsGradient(float hash)
{
    float3 bits=floor(hash/float3(1,2,4));
    float3 cube=(bits-floor(bits*0.5)*2)*2-1;
    int axis=(int)(hash/16);
    float3 cuboct=cube*float3(axis!=0,axis!=1,axis!=2);
    float type=floor(hash/8); type-=floor(type*0.5)*2;
    float3 rhomb=(1-type)*cube+type*(cuboct+cross(cube,cuboct));
    return (cuboct*1.22474487139+rhomb)*((1-0.042942436724648037*type)*3.5946317686139184);
}
float4 WTNoiseInternalCausticsPart(float3 p,float3 seedOffset)
{
    float3 b=floor(p);
    float4 i=float4(p-b,2.5);
    float3 v1=b+floor(dot(i,float4(.25,.25,.25,.25)));
    float3 v2=b+float3(1,0,0)+float3(-1,1,1)*floor(dot(i,float4(-.25,.25,.25,.35)));
    float3 v3=b+float3(0,1,0)+float3(1,-1,1)*floor(dot(i,float4(.25,-.25,.25,.35)));
    float3 v4=b+float3(0,0,1)+float3(1,1,-1)*floor(dot(i,float4(.25,.25,-.25,.35)));
    float4 hashes=WTNoiseInternalCausticsPermute(WTNoiseInternalCausticsMod(float4(v1.x,v2.x,v3.x,v4.x)+seedOffset.x,289));
    hashes=WTNoiseInternalCausticsPermute(WTNoiseInternalCausticsMod(hashes+float4(v1.y,v2.y,v3.y,v4.y)+seedOffset.y,289));
    hashes=WTNoiseInternalCausticsMod(WTNoiseInternalCausticsPermute(WTNoiseInternalCausticsMod(hashes+float4(v1.z,v2.z,v3.z,v4.z)+seedOffset.z,289)),48);
    float3 d1=p-v1,d2=p-v2,d3=p-v3,d4=p-v4;
    float4 a=max(.75-float4(dot(d1,d1),dot(d2,d2),dot(d3,d3),dot(d4,d4)),0);
    float4 aa=a*a,aaaa=aa*aa;
    float3 g1=WTNoiseInternalCausticsGradient(hashes.x),g2=WTNoiseInternalCausticsGradient(hashes.y);
    float3 g3=WTNoiseInternalCausticsGradient(hashes.z),g4=WTNoiseInternalCausticsGradient(hashes.w);
    float4 e=float4(dot(d1,g1),dot(d2,g2),dot(d3,g3),dot(d4,g4));
    float4 slopes=-8*aa*a*e;
    float3 derivative=d1*slopes.x+d2*slopes.y+d3*slopes.z+d4*slopes.w
        +g1*aaaa.x+g2*aaaa.y+g3*aaaa.z+g4*aaaa.w;
    return float4(derivative,dot(aaaa,e));
}
float4 WTNoiseInternalCausticsBase(float3 p,float3 seedOffset)
{
    // Explicit columns avoid GLSL/HLSL matrix-constructor conventions.
    float3 u=float3(.788675134594813,-.211324865405187,-.577350269189626);
    float3 v=float3(-.211324865405187,.788675134594813,-.577350269189626);
    float3 w=float3(.577350269189626,.577350269189626,.577350269189626);
    float3 x=u*p.x+v*p.y+w*p.z;
    float4 r=WTNoiseInternalCausticsPart(x,seedOffset)+WTNoiseInternalCausticsPart(x+144.5,seedOffset);
    return float4(dot(r.xyz,u),dot(r.xyz,v),dot(r.xyz,w),r.w);
}
// @wt.end
// @wt.block(Field; Kernel)
float4 WTNoiseInternalCausticsField(float3 p,float3 seedOffset,float2 period)
{
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    float2 enabled=float2(period.x>0,period.y>0);
    float2 safePeriod=max(period,1);
    p.xy-=floor(p.xy/safePeriod)*safePeriod*enabled;
    float2 t=p.xy/safePeriod;
    float2 weight=t*t*t*(t*(t*6-15)+10)*enabled;
    float2 slope=30*t*t*(t-1)*(t-1)/safePeriod*enabled;
#endif
// @wt.endif
    float4 a=WTNoiseInternalCausticsBase(p,seedOffset);
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    if(period.x>0)
    {
        float4 b=WTNoiseInternalCausticsBase(p-float3(period.x,0,0),seedOffset);
        float delta=b.w-a.w;
        a=lerp(a,b,weight.x); a.x+=delta*slope.x;
    }
    if(period.y>0)
    {
        float4 b=WTNoiseInternalCausticsBase(p-float3(0,period.y,0),seedOffset);
        if(period.x>0)
        {
            float4 c=WTNoiseInternalCausticsBase(p-float3(period,0),seedOffset);
            float delta=c.w-b.w;
            b=lerp(b,c,weight.x); b.x+=delta*slope.x;
        }
        float delta=b.w-a.w;
        a=lerp(a,b,weight.y); a.y+=delta*slope.y;
    }
#endif
// @wt.endif
    return a;
}
// @wt.end
// @wt.block(Sample; Field)
float WTNoiseInternalCaustics(float2 p,WTNoiseSettings n,WTNoiseCausticsSettings s)
{
    // 578 evolution units shift both cubic sublattices by their 289-cell hash period.
    float evolution=s.evolution-floor(s.evolution/578)*578;
    WTNoiseFractal f=n.fractal;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(float3(p,0),n,frequency,2,1,period,origin,inputScale);
        q+=(float3)origin;
        q.z=evolution*0.8660254037844386;
        int seed=WTNoiseInternalSeed(n.seed,octave);
        uint hash=WTNoiseInternalHash(asuint(seed)^1337u);
        float3 seedOffset=seed==1337?0:float3(hash%289u,(hash/289u)%289u,(hash/83521u)%289u);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale);
        float value=0.5;
        if(visibility>0)
        {
            float3 gradient=WTNoiseInternalCausticsField(q,seedOffset,(float2)period.xy).xyz;
            value=0.5+0.5*WTNoiseInternalCausticsField(q-gradient*s.strength,seedOffset,(float2)period.xy).w;
        }
        WTNoiseInternalAccumulateFiltered(value,f,visibility,0.5,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    // Approximate octave filtering; nonlinear warping can retain subpixel detail.
    return 0.5+0.5*sum;
}
// @wt.end
float WTNoiseCaustics(float2 p,WTNoiseSettings n,WTNoiseCausticsSettings s) { return WTNoiseInternalCaustics(p,n,s); }
float WTNoiseCaustics(float2 p,WTNoiseSettings n) { return WTNoiseCaustics(p,n,WTNoiseCausticsDefaults()); }
#endif
