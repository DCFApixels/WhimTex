#ifndef WT_NOISE_GABOR_INCLUDED
#define WT_NOISE_GABOR_INCLUDED
#include "WTNoiseCommon.hlsl"

struct WTNoiseGaborSettings
{
    // Carrier cycles per lattice unit; negative frequency uses its magnitude.
    float frequency;
    // Inverse envelope width, clamped to 0.5..4; support radius is 1/bandwidth.
    // Independent of carrier frequency. One retains the original envelope/result.
    float bandwidth;
    // 0: random orientations, 1: the specified direction. Ignored in 1D.
    float anisotropy;
    // Normalized internally (XY in 2D). Zero falls back to +X. Ignored in 1D.
    float3 direction;
};
WTNoiseGaborSettings WTNoiseGaborDefaults()
{
    WTNoiseGaborSettings g;
    g.frequency=2; g.bandwidth=1; g.anisotropy=0.8; g.direction=float3(1,0,0);
    return g;
}
struct WTNoiseGaborSample
{
    float value;     // 0..1; neutral value is 0.5.
    float phase;     // Turns in [0,1); 0 when intensity is negligible.
    float intensity; // Magnitude of the complex signal in 0..1.
};

float3 WTNoiseInternalGaborDirection(float3 direction,int dimensions)
{
    if(dimensions==1) return float3(1,0,0);
    if(dimensions==2) direction.z=0;
    float largest=max(max(abs(direction.x),abs(direction.y)),abs(direction.z));
    if(largest==0) return float3(1,0,0);
    direction/=largest;
    return direction*rsqrt(dot(direction,direction));
}

float2 WTNoiseInternalGaborComplex(float3 p,int3 origin,int seed,int3 period,int dimensions,
    WTNoiseGaborSettings g,bool filtered,float3 dx,float3 dy,out float3 realGradient)
{
    int3 baseCell=(int3)floor(p);
    float3 local=p-baseCell;
    baseCell+=origin;
    float3 axis=WTNoiseInternalGaborDirection(g.direction,dimensions);
    float3 tangent=0,bitangent=0;
    float anisotropy=saturate(g.anisotropy),frequency=abs(g.frequency);
    float bandwidth=clamp(g.bandwidth,0.5,4),bandwidthSquared=bandwidth*bandwidth;
    float coneCos=0;
    if(dimensions==3)
    {
        // Uniform solid-angle distribution inside a cone, including a full sphere at 0.
        float3 reference=abs(axis.z)<0.999 ? float3(0,0,1) : float3(0,1,0);
        tangent=normalize(cross(reference,axis));
        bitangent=cross(axis,tangent);
        coneCos=cos(3.14159265359*(1-anisotropy));
    }

    float envelopeGain=1;
    if(filtered)
    {
        // Gaussian-envelope minification, also at zero carrier frequency.
        // Covariance is (dx*dx^T+dy*dy^T)/12. The Gram determinant works in 1D/2D/3D.
        float3 envelopeDx=dx*bandwidth,envelopeDy=dy*bandwidth;
        float xx=dot(envelopeDx,envelopeDx),yy=dot(envelopeDy,envelopeDy);
        float3 area=cross(envelopeDx,envelopeDy);
        float areaSquared=dot(area,area);
        envelopeGain=rsqrt(1+1.0471975512*(xx+yy)+1.09662271123*areaSquared);
    }

    float2 sum=0;
    float total=0;
    float3 sumGradient=0,totalGradient=0;
    // Narrow envelopes use the radius-one reference for normalization. Normalizing
    // a lone narrow impulse by itself would cancel its smooth edge and leave a jump.
    // Wide envelopes normalize by their own weights, keeping the signal in -1..1.
    float supportRadius=max(1,rcp(bandwidth));
    int extent=clamp((int)ceil(supportRadius),1,2);
    int extentY=dimensions>=2 ? extent : 0,extentZ=dimensions==3 ? extent : 0;
    // Three impulses/cell: at most 15/75/375 in 1D/2D/3D per octave, never an
    // unbounded radius search. Bandwidth >= 1 retains the old 9/27/81 traversal.
    // The shared fractal limit is eight octaves. Keep these bounded loops rolled.
    [loop] for(int z=-extentZ;z<=extentZ;z++)
    [loop] for(int y=-extentY;y<=extentY;y++)
    [loop] for(int x=-extent;x<=extent;x++)
    {
        int3 shift=int3(x,y,z);
        // Only seed keys wrap: the actual image of each impulse keeps its displacement.
        uint state=WTNoiseInternalHashCell(baseCell+shift,seed,period);
        [loop] for(int impulse=0;impulse<3;impulse++)
        {
            state=WTNoiseInternalHash(state^0x9e3779b9u);
            float3 impulsePosition=float3(WTNoiseInternalUnit(state),0,0);
            if(dimensions>=2)
            {
                state=WTNoiseInternalHash(state^0x68bc21ebu);
                impulsePosition.y=WTNoiseInternalUnit(state);
            }
            if(dimensions==3)
            {
                state=WTNoiseInternalHash(state^0x63d83595u);
                impulsePosition.z=WTNoiseInternalUnit(state);
            }
            state=WTNoiseInternalHash(state^0x02e5be93u);
            float orientation=WTNoiseInternalUnit(state),coneSample=0;
            if(dimensions==3)
            {
                state=WTNoiseInternalHash(state^0xa511e9b3u);
                coneSample=WTNoiseInternalUnit(state);
            }
            state=WTNoiseInternalHash(state^0x967a889bu);
            float impulsePhase=WTNoiseInternalUnit(state);

            float3 d=local-(float3)shift-impulsePosition;
            float r2=dot(d,d);
            // Random state is advanced before rejection so subsequent impulses stay stable.
            if(r2>=supportRadius*supportRadius) continue;
            float3 direction=axis;
            if(dimensions==2)
            {
                float s,c;
                sincos(6.28318530718*(orientation-0.5)*(1-anisotropy),s,c);
                direction=float3(axis.x*c-axis.y*s,axis.x*s+axis.y*c,0);
            }
            else if(dimensions==3)
            {
                float s,c;
                sincos(6.28318530718*orientation,s,c);
                float along=lerp(1,coneCos,coneSample);
                direction=axis*along+(tangent*c+bitangent*s)*sqrt(max(1-along*along,0));
            }

            float gaussian=exp(-3.14159265359*(r2*bandwidthSquared));
            float envelopeBase=max(gaussian-0.04321391826,0)/0.95678608174;
            float envelope=envelopeBase*envelopeBase;
            float3 envelopeGradient=(-12.56637061436/0.95678608174)*envelopeBase*gaussian*d*bandwidthSquared;
            float normalizer=envelope;
            float3 normalizerGradient=envelopeGradient;
            if(bandwidth>1)
            {
                float referenceGaussian=exp(-3.14159265359*r2);
                float referenceBase=max(referenceGaussian-0.04321391826,0)/0.95678608174;
                normalizer=referenceBase*referenceBase;
                normalizerGradient=(-12.56637061436/0.95678608174)*referenceBase*referenceGaussian*d;
            }
            float attenuation=1;
            if(filtered)
            {
                float fx=dot(direction,dx)*frequency,fy=dot(direction,dy)*frequency;
                attenuation=exp(-1.64493406685*(fx*fx+fy*fy));
            }
            float s,c;
            sincos(6.28318530718*frac(dot(direction,d)*frequency+impulsePhase),s,c);
            sum+=float2(c,s)*(envelope*attenuation);
            total+=normalizer;
            // Settings/footprint are constant in p. Differentiate both the compact
            // envelope and carrier during this same impulse traversal.
            sumGradient+=attenuation*(c*envelopeGradient-envelope*s*(6.28318530718*frequency)*direction);
            totalGradient+=normalizerGradient;
        }
    }
    float denominator=max(total,1e-20);
    float multiplier=envelopeGain/denominator;
    // Quotient rule: the envelope normalization varies with the sampled position.
    realGradient=(sumGradient-(total>1e-20 ? sum.x/denominator : 0)*totalGradient)*multiplier;
    return sum*multiplier;
}

float WTNoiseInternalGabor(float3 p,WTNoiseSettings n,WTNoiseGaborSettings g,
    int dimensions,out WTNoiseGaborSample firstOctave,out float3 gradient)
{
    WTNoiseFractal f=n.fractal;
    firstOctave=(WTNoiseGaborSample)0;
    if(dimensions<3) { n.dpdx.z=0; n.dpdy.z=0; }
    if(dimensions<2) { n.dpdx.y=0; n.dpdy.y=0; }
    bool filtered=any(n.dpdx!=0) || any(n.dpdy!=0);
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float3 sumGradient=0,amplitudeGradient=0;
    float2 frequency=float2(1,0);
    [loop] for(int octave=0;octave<WTNoiseInternalOctaves(f);octave++)
    {
        int3 period,origin;
        float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float3 dx=0,dy=0;
        if(filtered)
        {
            // Use the sampled domain's exact unwrapped Jacobian, including fitted
            // periods and high/low components. Never difference wrapped positions.
            dx=n.dpdx*inputScale;
            dy=n.dpdy*inputScale;
        }
        float3 signalGradient;
        float2 signal=WTNoiseInternalGaborComplex(q,origin,WTNoiseInternalSeed(n.seed,octave),period,
            dimensions,g,filtered,dx,dy,signalGradient);
        if(octave==0)
        {
            float intensity=length(signal);
            firstOctave.value=saturate(0.5+0.5*signal.x);
            firstOctave.phase=intensity>1e-12 ? frac(atan2(signal.y,signal.x)/6.28318530718) : 0;
            firstOctave.intensity=saturate(intensity);
        }
        WTNoiseInternalAccumulate(0.5+0.5*signal.x,0.5*signalGradient*inputScale,f,
            sum,amplitude,sumGradient,amplitudeGradient);
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
    }
    float value=0.5+0.5*sum;
    gradient=value>0 && value<1 ? 0.5*sumGradient : float3(0,0,0);
    return saturate(value);
}
float WTNoiseInternalGabor(float3 p,WTNoiseSettings n,WTNoiseGaborSettings g,
    int dimensions,out WTNoiseGaborSample firstOctave)
{
    float3 ignored;
    return WTNoiseInternalGabor(p,n,g,dimensions,firstOctave,ignored);
}
float3 WTNoiseInternalGaborGradient(float3 p,WTNoiseSettings n,WTNoiseGaborSettings g,int dimensions)
{
    WTNoiseGaborSample ignored;
    float3 gradient;
    WTNoiseInternalGabor(p,n,g,dimensions,ignored,gradient);
    return gradient;
}

// n.dpdx/dpdy are in INPUT units, before WT scale/offset/period; use X/XY/XYZ for 1D/2D/3D.
// Supply derivatives from the caller or an explicit footprint; none are evaluated implicitly.
// Settings are assumed constant across the footprint: spatial changes of scale, offset,
// period, seed, frequency, bandwidth, direction, anisotropy or fractal settings are NOT filtered.
// The approximation filters each octave's carrier/envelope before the shared fractal
// combiner; it is not exact integration of nonlinear Ridged/PingPong/weighted transforms.
// Zero footprints equal unfiltered sampling. Optional phase/intensity describe the first octave.
#define WT_NOISE_GABOR_OVERLOAD(Type,Position,Dimensions) \
float WTNoiseGabor(Type p,WTNoiseSettings n,WTNoiseGaborSettings g,out WTNoiseGaborSample firstOctave) { return WTNoiseInternalGabor(Position,n,g,Dimensions,firstOctave); } \
float WTNoiseGabor(Type p,WTNoiseSettings n,WTNoiseGaborSettings g) { WTNoiseGaborSample ignored; return WTNoiseInternalGabor(Position,n,g,Dimensions,ignored); } \
float WTNoiseGabor(Type p,WTNoiseSettings n) { return WTNoiseGabor(p,n,WTNoiseGaborDefaults()); }
WT_NOISE_GABOR_OVERLOAD(float,float3(p,0,0),1)
WT_NOISE_GABOR_OVERLOAD(float2,float3(p,0),2)
WT_NOISE_GABOR_OVERLOAD(float3,p,3)
#undef WT_NOISE_GABOR_OVERLOAD
#endif
