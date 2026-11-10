#ifndef WHIMTEX_WT_NOISE_CAUSTICS_INCLUDED
#define WHIMTEX_WT_NOISE_CAUSTICS_INCLUDED
#include "WTNoiseCommon.hlsl"
#include "WTNoiseCausticsTypes.hlsl"

// OpenSimplex2S derivative kernel: KdotJPG, CC0, https://www.shadertoy.com/view/wlc3zr
// Permutation polynomial hash credit: Stefan Gustavson.
// Source snapshot supplied 2026-11-09; see ThirdPartyNotices.md.
// The nonperiodic path preserves the reference ImproveXY orientation.
// @wt.feature(Periodic)
// @wt.feature(OpenSimplex)
// @wt.feature(Simplex)
// @wt.block(Kernel)
// @wt.if(OpenSimplex)
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
float4 WTNoiseInternalCausticsPart(float3 p,float3 seedOffset,float3 period,float latticeShift)
{
    float3 b=floor(p);
    float4 i=float4(p-b,2.5);
    float3 v1=b+floor(dot(i,float4(.25,.25,.25,.25)));
    float3 v2=b+float3(1,0,0)+float3(-1,1,1)*floor(dot(i,float4(-.25,.25,.25,.35)));
    float3 v3=b+float3(0,1,0)+float3(1,-1,1)*floor(dot(i,float4(.25,-.25,.25,.35)));
    float3 v4=b+float3(0,0,1)+float3(1,1,-1)*floor(dot(i,float4(.25,.25,-.25,.35)));
    float4 hx=float4(v1.x,v2.x,v3.x,v4.x),hy=float4(v1.y,v2.y,v3.y,v4.y),hz=float4(v1.z,v2.z,v3.z,v4.z);
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    // Keep the half-lattice offset out of local distances to avoid loss of precision.
    hx+=latticeShift;hy+=latticeShift;hz+=latticeShift;
    // Wrap corner identities on each cubic half-lattice, never the local distances.
    if(period.x>0) hx=WTNoiseInternalCausticsMod(hx,period.x);
    if(period.y>0) hy=WTNoiseInternalCausticsMod(hy,period.y);
    if(period.z>0) hz=WTNoiseInternalCausticsMod(hz,period.z);
#endif
// @wt.endif
    float4 hashes=WTNoiseInternalCausticsPermute(WTNoiseInternalCausticsMod(hx+seedOffset.x,289));
    hashes=WTNoiseInternalCausticsPermute(WTNoiseInternalCausticsMod(hashes+hy+seedOffset.y,289));
    hashes=WTNoiseInternalCausticsMod(WTNoiseInternalCausticsPermute(WTNoiseInternalCausticsMod(hashes+hz+seedOffset.z,289)),48);
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
float4 WTNoiseInternalCausticsBase(float3 p,float3 seedOffset,float2 period)
{
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    if(period.x>0 || period.y>0)
    {
        // Axis-aligned BCC lattice admits every integer spatial period.
        // 501 is the nearest integer to the original 578*sqrt(3)/2 time span.
        float3 latticePeriod=float3(max(period,0),501);
        // Keep warp samples close to the origin, including around the time-loop seam.
        p-=floor(p/max(latticePeriod,1)+0.5)*latticePeriod;
        return WTNoiseInternalCausticsPart(p,seedOffset,latticePeriod,0)
            +WTNoiseInternalCausticsPart(p+0.5,seedOffset,latticePeriod,144);
    }
#endif
// @wt.endif
    // Explicit columns avoid GLSL/HLSL matrix-constructor conventions.
    float3 u=float3(.788675134594813,-.211324865405187,-.577350269189626);
    float3 v=float3(-.211324865405187,.788675134594813,-.577350269189626);
    float3 w=float3(.577350269189626,.577350269189626,.577350269189626);
    float3 x=u*p.x+v*p.y+w*p.z;
    float4 r=WTNoiseInternalCausticsPart(x,seedOffset,0,0)+WTNoiseInternalCausticsPart(x+144.5,seedOffset,0,0);
    return float4(dot(r.xyz,u),dot(r.xyz,v),dot(r.xyz,w),r.w);
}
// @wt.endif
// @wt.if(Simplex)
// Copyright (C) 2011 by Ashima Arts (Simplex noise)
// Copyright (C) 2011-2016 by Stefan Gustavson (Classic noise and others)
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

float4 WTNoiseInternalCausticsSimplexMod(float4 x)
{
    return x - floor(x / 289.0) * 289.0;
}

float4 WTNoiseInternalCausticsSimplexPermute(float4 x)
{
    return WTNoiseInternalCausticsSimplexMod((x * 34.0 + 1.0) * x);
}

float4 WTNoiseInternalCausticsSimplex(float3 p,float3 seedOffset,float2 period)
{
    float3 v=p.xzy;
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    float3 i,x0;
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    // Orthogonal rotation aligns integer spatial periods with lattice translations.
    // Skew the rotated lattice directly, then rotate local distances. Rotating the
    // global position first can round translated copies onto different simplex corners.
    bool tiled=period.x>0 || period.y>0;
    if(tiled)
    {
        i=floor(v.yzx+v.zxy);
        float3 local=v-(dot(i,float3(0.5,0.5,0.5))-i);
        x0=dot(local,float3(2.0/3.0,2.0/3.0,2.0/3.0))-local;
    }
    else
#endif
// @wt.endif
    {
        i=floor(v+dot(v,C.yyy));
        x0=v-i+dot(i,C.xxx);
    }
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);
    float3 x1 = x0 - i1 + C.x;
    float3 x2 = x0 - i2 + C.y;
    float3 x3 = x0 - 0.5;

    float4 hx=i.x+float4(0.0,i1.x,i2.x,1.0);
    float4 hy=i.y+float4(0.0,i1.y,i2.y,1.0);
    float4 hz=i.z+float4(0.0,i1.z,i2.z,1.0);
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    // After rotation, spatial shifts translate indices by P*(0,1,1) or P*(1,1,0).
    // Fold only hash identities, leaving corner distances and the -6 field intact.
    float4 foldX=0,foldY=0;
    if(period.x>0) foldX=floor((-hx+hy+hz)/(2*period.x))*period.x;
    if(period.y>0) foldY=floor((hx+hy-hz)/(2*period.y))*period.y;
    hx-=foldY;
    hy-=foldX+foldY;
    hz-=foldX;
#endif
// @wt.endif
    float4 hashes = WTNoiseInternalCausticsSimplexPermute(WTNoiseInternalCausticsSimplexPermute(WTNoiseInternalCausticsSimplexPermute(
        hz+seedOffset.z)+hy+seedOffset.y)+hx+seedOffset.x);
    float4 j = hashes - 49.0 * floor(hashes / 49.0);
    float4 x_ = floor(j / 7.0);
    float4 y_ = floor(j - 7.0 * x_);
    float4 x = (x_ * 2.0 + 0.5) / 7.0 - 1.0;
    float4 y = (y_ * 2.0 + 0.5) / 7.0 - 1.0;
    float4 h = 1.0 - abs(x) - abs(y);
    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);
    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, float4(0.0, 0.0, 0.0, 0.0));
    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
    float3 g0 = float3(a0.xy, h.x);
    float3 g1 = float3(a0.zw, h.y);
    float3 g2 = float3(a1.xy, h.z);
    float3 g3 = float3(a1.zw, h.w);

    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    float4 m2 = m * m;
    float4 m3 = m2 * m;
    float4 m4 = m2 * m2;
    // Keep the reference's artistic -6 direction field, not the exact -8 derivative.
    float3 gradient =
        -6.0 * m3.x * x0 * dot(x0, g0) + m4.x * g0 +
        -6.0 * m3.y * x1 * dot(x1, g1) + m4.y * g1 +
        -6.0 * m3.z * x2 * dot(x2, g2) + m4.z * g2 +
        -6.0 * m3.w * x3 * dot(x3, g3) + m4.w * g3;
    float4 px = float4(dot(x0, g0), dot(x1, g1), dot(x2, g2), dot(x3, g3));
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    if(tiled) gradient=dot(gradient,float3(2.0/3.0,2.0/3.0,2.0/3.0))-gradient;
#endif
// @wt.endif
    return 42.0 * float4(gradient.xzy, dot(m4, px));
}

// @wt.endif
// @wt.end
// @wt.block(Field; Kernel)
float4 WTNoiseInternalCausticsField(float3 p,float3 seedOffset,float2 period,int algorithm)
{
    float4 result=0;
// @wt.if(Simplex)
    if(algorithm==1) result=WTNoiseInternalCausticsSimplex(p,seedOffset,period);
// @wt.endif
// @wt.if(OpenSimplex)
    if(algorithm==0) result=WTNoiseInternalCausticsBase(p,seedOffset,period);
// @wt.endif
    return result;
}
// @wt.end
// @wt.block(Sample; Field)
float WTNoiseInternalCausticsSample(float2 p,WTNoiseSettings n,WTNoiseCausticsSettings s,int algorithm,bool profile)
{
    bool simplex=algorithm==1;
    if(profile)
    {
        if(simplex) s.evolution*=2;
        else { n.scale*=1.125; s.strength*=2; }
    }
    float strength=s.strength*(simplex?0.14:0.1);
    // Simplex repeats after an axis-aligned translation of 3*289 lattice units.
    float evolution=simplex?s.evolution*0.4330127018922193:
        (s.evolution-floor(s.evolution/578)*578)*0.8660254037844386;
    if(simplex) evolution-=floor(evolution/867+0.5)*867;
// @wt.if(Periodic)
#if !defined(WT_NOISE_INTERNAL_NONPERIODIC)
    // Fit the same 578-unit animation loop to an integer temporal lattice period.
    if(!simplex && (n.period.x>0 || n.period.y>0))
        evolution=(s.evolution-floor(s.evolution/578+0.5)*578)*(501.0/578.0);
#endif
// @wt.endif
    WTNoiseFractal f=n.fractal;
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin; float3 inputScale;
        float3 q=WTNoiseInternalPosition(float3(p,0),n,frequency,2,1,period,origin,inputScale);
        q+=(float3)origin;
        q.z=evolution;
        int seed=WTNoiseInternalSeed(n.seed,octave);
        uint hash=WTNoiseInternalHash(asuint(seed)^1337u);
        float3 seedOffset=seed==1337?0:float3(hash%289u,(hash/289u)%289u,(hash/83521u)%289u);
        float visibility=WTNoiseInternalFilterWeight(n,inputScale);
        float value=0.5;
        if(visibility>0)
        {
            float magnitude=abs(strength);
            float stepSize=max(simplex?0.07:0.0625,magnitude/8);
            float firstDistance=min(magnitude*(simplex?0.5:1),stepSize);
            float direction=sign(strength);
            float4 field=WTNoiseInternalCausticsField(q,seedOffset,(float2)period.xy,algorithm);
            q-=field.xyz*(direction*firstDistance);
            field=WTNoiseInternalCausticsField(q,seedOffset,(float2)period.xy,algorithm);
            if(simplex)
            {
                q-=field.xyz*(direction*firstDistance);
                field=WTNoiseInternalCausticsField(q,seedOffset,(float2)period.xy,algorithm);
            }
            float remaining=magnitude-firstDistance*(simplex?2:1);
            float integratedValue=0,integratedDistance=0;
            [loop] for(int causticsStep=simplex?2:1;causticsStep<8 && remaining>0;causticsStep++)
            {
                float distance=min(remaining,stepSize);
                q-=field.xyz*(direction*distance);
                float previousValue=field.w;
                field=WTNoiseInternalCausticsField(q,seedOffset,(float2)period.xy,algorithm);
                integratedValue+=(previousValue+field.w)*0.5*distance;
                integratedDistance+=distance;
                remaining-=distance;
            }
            if(integratedDistance>0) field.w=integratedValue/integratedDistance;
            value=simplex?exp(field.w*3-1.5):0.5+0.5*field.w;
            if(profile)
            {
                value=simplex?pow(value,1.0/1.7):0.5*exp(value*3-1.5);
                value=smoothstep(0,1,value);
            }
        }
        WTNoiseInternalAccumulateFiltered(value,f,visibility,0.5,sum,amplitude);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    // Approximate octave filtering; nonlinear warping can retain subpixel detail.
    return 0.5+0.5*sum;
}
float WTNoiseInternalCaustics(float2 p,WTNoiseSettings n,WTNoiseCausticsSettings s)
{
    return WTNoiseInternalCausticsSample(p,n,s,0,false);
}
// @wt.end
float WTNoiseCaustics(float2 p,WTNoiseSettings n,WTNoiseCausticsSettings s) { return WTNoiseInternalCaustics(p,n,s); }
float WTNoiseCaustics(float2 p,WTNoiseSettings n) { return WTNoiseCaustics(p,n,WTNoiseCausticsDefaults()); }
#endif
