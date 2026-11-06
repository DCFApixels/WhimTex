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
    // These controls affect only the first-octave phasor output.
    int profile;
    float phaseOffset; // Cycles; integer offsets repeat the same profile.
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
};

#endif
