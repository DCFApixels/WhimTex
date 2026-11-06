#ifndef WHIMTEX_WT_NOISE_SPOTS_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_SPOTS_TYPES_INCLUDED

struct WTNoiseSpotSettings
{
    // Expected active centers per lattice cell, clamped to 0..4. Zero is empty.
    float density;
    // Longest support semi-axis in lattice units, clamped to 0..2. Zero is empty.
    float radius;
    // Fraction of the radius occupied by the smooth edge, clamped to 0.01..1.
    // Zero requests the minimum soft edge, never a discontinuous hard threshold.
    float softness;
    // Major/minor axis ratio, clamped to 1..8. Compresses transverse axes at fixed
    // radius; direction is the major axis. Both are ignored in 1D.
    float elongation;
    // Normalized internally (XY in 2D). Zero falls back to +X.
    float3 direction;
};
WTNoiseSpotSettings WTNoiseSpotDefaults()
{
    WTNoiseSpotSettings s;
    s.density=1; s.radius=0.35; s.softness=0.5;
    s.elongation=1; s.direction=float3(1,0,0);
    return s;
}
#endif
