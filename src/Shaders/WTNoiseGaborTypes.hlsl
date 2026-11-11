#ifndef WT_NOISE_GABOR_TYPES_INCLUDED
#define WT_NOISE_GABOR_TYPES_INCLUDED
#include "WTNoiseCommon.hlsl"

#define WT_NOISE_PHASOR_SINE 0
#define WT_NOISE_PHASOR_TRIANGLE 1
#define WT_NOISE_PHASOR_PULSE 2

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
    int profile;       // First-octave phasor profile.
    float phaseOffset; // Common carrier offset in cycles; integer offsets repeat.
    float width;       // Pulse duty cycle in 0..1.
};
WTNoiseGaborSettings WTNoiseGaborDefaults()
{
    WTNoiseGaborSettings g;
    g.frequency=2; g.bandwidth=1; g.anisotropy=0.8; g.direction=float3(1,0,0);
    g.profile=WT_NOISE_PHASOR_SINE; g.phaseOffset=0; g.width=0.5;
    return g;
}
struct WTNoiseGaborSample
{
    float value;     // 0..1; neutral value is 0.5.
    float phase;     // Turns in [0,1); 0 when intensity is negligible.
    float intensity; // Magnitude of the complex signal in 0..1.
    float phasor;    // Filtered periodic profile in 0..1, without the amplitude envelope.
    float2 phaseFilter; // Internal profile reuse: footprint in cycles, phase confidence.
    float2 carrier;     // Unshifted complex sample, shared by graph outputs.
    float3 carrierRealGradient,carrierImaginaryGradient; // In Position units.
};

float2 WTNoiseInternalGaborPhaseAxis(float offset)
{
    float s,c;
    sincos(6.28318530718*frac(offset),s,c);
    return float2(c,-s);
}

float WTNoiseInternalGaborOffsetPhase(float phase,float intensity,float offset)
{
    return intensity>1e-12 ? frac(phase+frac(offset)) : 0;
}

float WTNoiseInternalGaborProjectSample(WTNoiseGaborSample sample,float2 axis,out float3 gradient)
{
    float value=0.5+0.5*dot(sample.carrier,axis);
    gradient=value>0 && value<1 ? 0.5*(axis.x*sample.carrierRealGradient+axis.y*sample.carrierImaginaryGradient) : float3(0,0,0);
    return saturate(value);
}

#endif
