#ifndef WT_NOISE_GABOR_INCLUDED
#define WT_NOISE_GABOR_INCLUDED
#include "WTNoiseCommon.hlsl"

#include "WTNoiseGaborTypes.hlsl"

// @wt.feature(Dim1)
// @wt.feature(Dim2; MultiDim)
// @wt.feature(Dim3; MultiDim)
// @wt.feature(MultiDim)
// @wt.feature(Filtering)
// @wt.feature(Fractal)
// @wt.feature(RealGradient; Derivatives)
// @wt.feature(ImaginaryGradient; Derivatives)
// @wt.feature(Derivatives)
// @wt.feature(Phase)
// @wt.feature(Intensity)
// @wt.feature(PhaseFilter; Phase, RealGradient, ImaginaryGradient)
// @wt.feature(Phasor; PhaseFilter)

// @wt.block(Profile)
// Integrate a profile over [a,b] within a single cycle, avoiding subtraction of
// nearly equal antiderivatives for narrow footprints. Peaks are centered on phase 0.
float WTNoiseInternalPhasorArea(float a,float b,int profile,float width)
{
    if(profile==WT_NOISE_PHASOR_PULSE)
        return max(0,min(b,width*0.5)-a)+max(0,b-max(a,1-width*0.5));
    float lo=min(a,0.5),hi=min(b,0.5);
    float area=(hi-lo)*(1-lo-hi);
    lo=max(a,0.5); hi=max(b,0.5);
    return area+(hi-lo)*(lo+hi-1);
}

float WTNoiseInternalPhasorProfile(float phase,float footprint,int profile,float width)
{
    width=saturate(width);
    float mean=profile==WT_NOISE_PHASOR_PULSE ? width : 0.5;
    if(profile==WT_NOISE_PHASOR_PULSE && (width<=0 || width>=1)) return width;
    if(footprint>=10000) return mean;
    phase=frac(phase);
    if(profile==WT_NOISE_PHASOR_SINE)
    {
        float angle=3.14159265359*footprint;
        float attenuation=angle>0.001 ? sin(angle)/angle : 1-angle*angle/6;
        return saturate(0.5+0.5*cos(6.28318530718*phase)*attenuation);
    }
    if(footprint<=1e-6)
        return profile==WT_NOISE_PHASOR_PULSE ? (min(phase,1-phase)<width*0.5 ? 1 : 0) : abs(2*phase-1);
    float whole=floor(footprint),remainder=footprint-whole;
    float a=frac(phase-0.5*footprint),b=a+remainder;
    float area=WTNoiseInternalPhasorArea(a,min(b,1),profile,width);
    if(b>1) area+=WTNoiseInternalPhasorArea(0,b-1,profile,width);
    return saturate((whole*mean+area)/footprint);
}

float2 WTNoiseInternalPhasorFilter(float2 signal,float3 realGradient,float3 imaginaryGradient,
    float3 dx,float3 dy)
{
    float magnitude=length(signal);
    if(magnitude<=1e-12) return float2(0,0);
    float2 unit=signal/magnitude;
    // Analytic angular derivative: no finite differences across the phase wrap.
    float3 phaseGradient=(unit.x*imaginaryGradient-unit.y*realGradient)/(6.28318530718*magnitude);
    float footprint=abs(dot(phaseGradient,dx))+abs(dot(phaseGradient,dy));
    // Near signal zeros the local linear phase approximation is unreliable.
    // Fade only when the amplitude can reach zero inside this pixel's footprint.
    float3 amplitudeGradient=unit.x*realGradient+unit.y*imaginaryGradient;
    float span=abs(dot(amplitudeGradient,dx))+abs(dot(amplitudeGradient,dy));
    float confidence=saturate(2*magnitude/max(span,1e-20));
    confidence=confidence*confidence*(3-2*confidence);
    return float2(footprint,confidence);
}

float WTNoiseInternalGaborProfile(WTNoiseGaborSample sample,int profile,float phaseOffset,float width)
{
    float mean=profile==WT_NOISE_PHASOR_PULSE ? saturate(width) : 0.5;
    float value=WTNoiseInternalPhasorProfile(sample.phase+frac(phaseOffset),sample.phaseFilter.x,profile,width);
    return lerp(mean,value,sample.phaseFilter.y);
}

// @wt.end

// @wt.block(Direction)
float3 WTNoiseInternalGaborDirection(float3 direction,int dimensions)
{
    if(dimensions==1) return float3(1,0,0);
    if(dimensions==2) direction.z=0;
    float largest=max(max(abs(direction.x),abs(direction.y)),abs(direction.z));
    if(largest==0) return float3(1,0,0);
    direction/=largest;
    return direction*rsqrt(dot(direction,direction));
}

// @wt.end

// @wt.block(Kernel; Direction)
float2 WTNoiseInternalGaborComplex(float3 p,int3 origin,int seed,int3 period,int dimensions,
    WTNoiseGaborSettings g,bool filtered,float3 dx,float3 dy,out float3 realGradient,out float3 imaginaryGradient)
{
    int3 baseCell=(int3)floor(p);
    float3 local=p-baseCell;
    baseCell+=origin;
    float3 axis=WTNoiseInternalGaborDirection(g.direction,dimensions);
    float3 tangent=0,bitangent=0;
    float anisotropy=saturate(g.anisotropy),frequency=abs(g.frequency);
    float bandwidth=clamp(g.bandwidth,0.5,4),bandwidthSquared=bandwidth*bandwidth;
    float coneCos=0;
// @wt.if(Dim3)
    if(dimensions==3)
    {
        // Uniform solid-angle distribution inside a cone, including a full sphere at 0.
        float3 reference=abs(axis.z)<0.999 ? float3(0,0,1) : float3(0,1,0);
        tangent=normalize(cross(reference,axis));
        bitangent=cross(axis,tangent);
        coneCos=cos(3.14159265359*(1-anisotropy));
    }
// @wt.endif

    float envelopeGain=1;
// @wt.if(Filtering)
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
// @wt.endif

    float2 sum=0;
    float total=0;
// @wt.if(Derivatives)
    float3 sumGradient=0,imaginarySumGradient=0,totalGradient=0;
// @wt.endif
    // Narrow envelopes use the radius-one reference for normalization. Normalizing
    // a lone narrow impulse by itself would cancel its smooth edge and leave a jump.
    // Wide envelopes normalize by their own weights, keeping the signal in -1..1.
    float supportRadius=max(1,rcp(bandwidth));
    int extent=clamp((int)ceil(supportRadius),1,2);
    int extentY=dimensions>=2 ? extent : 0,extentZ=dimensions==3 ? extent : 0;
    // Three impulses/cell: at most 15/75/375 in 1D/2D/3D per octave, never an
    // unbounded radius search. Bandwidth >= 1 retains the old 9/27/81 traversal.
    // The shared fractal limit is eight octaves. Keep these bounded loops rolled.
    int z=0,y=0;
// @wt.if(Dim3)
    [loop] for(z=-extentZ;z<=extentZ;z++)
// @wt.endif
// @wt.if(MultiDim)
    [loop] for(y=-extentY;y<=extentY;y++)
// @wt.endif
    [loop] for(int x=-extent;x<=extent;x++)
    {
        int3 shift=int3(x,y,z);
        // Only seed keys wrap: the actual image of each impulse keeps its displacement.
        uint state=WTNoiseInternalHashCell(baseCell+shift,seed,period);
        [loop] for(int impulse=0;impulse<3;impulse++)
        {
            state=WTNoiseInternalHash(state^0x9e3779b9u);
            float3 impulsePosition=float3(WTNoiseInternalUnit(state),0,0);
// @wt.if(MultiDim)
            if(dimensions>=2)
            {
                state=WTNoiseInternalHash(state^0x68bc21ebu);
                impulsePosition.y=WTNoiseInternalUnit(state);
            }
// @wt.endif
// @wt.if(Dim3)
            if(dimensions==3)
            {
                state=WTNoiseInternalHash(state^0x63d83595u);
                impulsePosition.z=WTNoiseInternalUnit(state);
            }
// @wt.endif
            state=WTNoiseInternalHash(state^0x02e5be93u);
            float orientation=WTNoiseInternalUnit(state),coneSample=0;
// @wt.if(Dim3)
            if(dimensions==3)
            {
                state=WTNoiseInternalHash(state^0xa511e9b3u);
                coneSample=WTNoiseInternalUnit(state);
            }
// @wt.endif
            state=WTNoiseInternalHash(state^0x967a889bu);
            float impulsePhase=WTNoiseInternalUnit(state);

            float3 d=local-(float3)shift-impulsePosition;
            float r2=dot(d,d);
            // Random state is advanced before rejection so subsequent impulses stay stable.
            if(r2>=supportRadius*supportRadius) continue;
            float3 direction=axis;
// @wt.if(Dim2)
            if(dimensions==2)
            {
                float s,c;
                sincos(6.28318530718*(orientation-0.5)*(1-anisotropy),s,c);
                direction=float3(axis.x*c-axis.y*s,axis.x*s+axis.y*c,0);
            }
// @wt.endif
// @wt.if(Dim3)
            if(dimensions==3)
            {
                float s,c;
                sincos(6.28318530718*orientation,s,c);
                float along=lerp(1,coneCos,coneSample);
                direction=axis*along+(tangent*c+bitangent*s)*sqrt(max(1-along*along,0));
            }
// @wt.endif

            float gaussian=exp(-3.14159265359*(r2*bandwidthSquared));
            float envelopeBase=max(gaussian-0.04321391826,0)/0.95678608174;
            float envelope=envelopeBase*envelopeBase;
// @wt.if(Derivatives)
            float3 envelopeGradient=(-12.56637061436/0.95678608174)*envelopeBase*gaussian*d*bandwidthSquared;
// @wt.endif
            float normalizer=envelope;
// @wt.if(Derivatives)
            float3 normalizerGradient=envelopeGradient;
// @wt.endif
            if(bandwidth>1)
            {
                float referenceGaussian=exp(-3.14159265359*r2);
                float referenceBase=max(referenceGaussian-0.04321391826,0)/0.95678608174;
                normalizer=referenceBase*referenceBase;
// @wt.if(Derivatives)
                normalizerGradient=(-12.56637061436/0.95678608174)*referenceBase*referenceGaussian*d;
// @wt.endif
            }
            float attenuation=1;
// @wt.if(Filtering)
            if(filtered)
            {
                float fx=dot(direction,dx)*frequency,fy=dot(direction,dy)*frequency;
                attenuation=exp(-1.64493406685*(fx*fx+fy*fy));
            }
// @wt.endif
            float s,c;
            sincos(6.28318530718*frac(dot(direction,d)*frequency+impulsePhase),s,c);
            sum+=float2(c,s)*(envelope*attenuation);
            total+=normalizer;
            // Settings/footprint are constant in p. Differentiate both the compact
            // envelope and carrier during this same impulse traversal.
// @wt.if(RealGradient)
            sumGradient+=attenuation*(c*envelopeGradient-envelope*s*(6.28318530718*frequency)*direction);
// @wt.endif
// @wt.if(ImaginaryGradient)
            imaginarySumGradient+=attenuation*(s*envelopeGradient+envelope*c*(6.28318530718*frequency)*direction);
// @wt.endif
// @wt.if(Derivatives)
            totalGradient+=normalizerGradient;
// @wt.endif
        }
    }
    float denominator=max(total,1e-20);
    float multiplier=envelopeGain/denominator;
    realGradient=0; imaginaryGradient=0;
    // Quotient rule: the envelope normalization varies with the sampled position.
// @wt.if(RealGradient)
    realGradient=(sumGradient-(total>1e-20 ? sum.x/denominator : 0)*totalGradient)*multiplier;
// @wt.endif
// @wt.if(ImaginaryGradient)
    imaginaryGradient=(imaginarySumGradient-(total>1e-20 ? sum.y/denominator : 0)*totalGradient)*multiplier;
// @wt.endif
    return sum*multiplier;
}

// @wt.end

// @wt.block(Sample; Kernel)
float WTNoiseInternalGabor(float3 p,WTNoiseSettings n,WTNoiseGaborSettings g,
    int dimensions,out WTNoiseGaborSample firstOctave,out float3 gradient)
{
    WTNoiseFractal f=n.fractal;
    firstOctave.value=0;
    firstOctave.phase=0;
    firstOctave.intensity=0;
    firstOctave.phaseFilter=0;
    firstOctave.phasor=0;
    if(dimensions<3) { n.dpdx.z=0; n.dpdy.z=0; }
    if(dimensions<2) { n.dpdx.y=0; n.dpdy.y=0; }
    bool filtered=any(n.dpdx!=0) || any(n.dpdy!=0);
    float sum=0,amplitude=WTNoiseInternalAmplitude(f);
    float3 sumGradient=0,amplitudeGradient=0;
    float2 frequency=float2(1,0);
    int octave=0;
// @wt.if(Fractal)
    [loop] for(octave=0;octave<WTNoiseInternalOctaves(f);octave++)
// @wt.endif
    {
        int3 period,origin;
        float3 inputScale;
        float3 q=WTNoiseInternalPosition(p,n,frequency,dimensions,1,period,origin,inputScale);
        float3 dx=0,dy=0;
// @wt.if(Filtering)
        if(filtered)
        {
            // Use the sampled domain's exact unwrapped Jacobian, including fitted
            // periods and high/low components. Never difference wrapped positions.
            dx=n.dpdx*inputScale;
            dy=n.dpdy*inputScale;
        }
// @wt.endif
        float3 signalGradient,imaginaryGradient;
        float2 signal=WTNoiseInternalGaborComplex(q,origin,WTNoiseInternalSeed(n.seed,octave),period,
            dimensions,g,filtered,dx,dy,signalGradient,imaginaryGradient);
        if(octave==0)
        {
            float intensity=length(signal);
            firstOctave.value=saturate(0.5+0.5*signal.x);
// @wt.if(Phase)
            firstOctave.phase=intensity>1e-12 ? frac(atan2(signal.y,signal.x)/6.28318530718) : 0;
// @wt.endif
// @wt.if(Intensity)
            firstOctave.intensity=saturate(intensity);
// @wt.endif
// @wt.if(PhaseFilter)
            firstOctave.phaseFilter=WTNoiseInternalPhasorFilter(signal,signalGradient,imaginaryGradient,dx,dy);
// @wt.endif
// @wt.if(Phasor)
            firstOctave.phasor=WTNoiseInternalGaborProfile(firstOctave,g.profile,g.phaseOffset,g.width);
// @wt.endif
        }
        WTNoiseInternalAccumulate(0.5+0.5*signal.x,0.5*signalGradient*inputScale,f,
            sum,amplitude,sumGradient,amplitudeGradient);
// @wt.if(Fractal)
        frequency=WTNoiseInternalNextFrequency(frequency,f.lacunarity);
// @wt.endif
    }
    float value=0.5+0.5*sum;
    gradient=value>0 && value<1 ? 0.5*sumGradient : float3(0,0,0);
    return saturate(value);
}
// @wt.end

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
