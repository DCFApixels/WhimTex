#ifndef WHIMTEX_WT_NOISE_TRIANGLEVORONOI_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_TRIANGLEVORONOI_TYPES_INCLUDED
#include "WTNoiseCellularTypes.hlsl"

struct WTNoiseTriangleVoronoiSettings
{
    int distribution;
    float jitter;
    float evolution;
    // Bounded anisotropy keeps the exact local search finite and practical.
    float2 stretch;
};
WTNoiseTriangleVoronoiSettings WTNoiseTriangleVoronoiDefaults()
{
    WTNoiseTriangleVoronoiSettings t;
    t.distribution=WT_NOISE_CELL_UNIFORM; t.jitter=1; t.evolution=0; t.stretch=1; return t;
}
struct WTNoiseTriangleVoronoiSample2D
{
    float distance,edgeDistance,value;
    uint id;
    float2 toPoint,borderNormal;
};
#endif
