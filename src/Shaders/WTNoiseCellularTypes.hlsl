#ifndef WHIMTEX_WT_NOISE_CELLULAR_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_CELLULAR_TYPES_INCLUDED
#include "WTNoiseCommon.hlsl"

#define WT_NOISE_CELL_UNIFORM 0
#define WT_NOISE_CELL_FIXED_RADIUS 1
#define WT_NOISE_DISTANCE_EUCLIDEAN 0
#define WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED 1
#define WT_NOISE_DISTANCE_MANHATTAN 2
#define WT_NOISE_DISTANCE_HYBRID 3
#define WT_NOISE_CELL_VALUE 0
#define WT_NOISE_CELL_F1 1
#define WT_NOISE_CELL_F2 2
#define WT_NOISE_CELL_ADD 3
#define WT_NOISE_CELL_SUBTRACT 4
#define WT_NOISE_CELL_MULTIPLY 5
#define WT_NOISE_CELL_DIVIDE 6
#define WT_NOISE_CELL_DISTANCE_TO_EDGE 8

struct WTNoiseCellularSettings
{
    int distribution;
    int metric;
    int output;
    float jitter;
    float smoothness;
    float evolution;
};
WTNoiseCellularSettings WTNoiseCellularDefaults()
{
    WTNoiseCellularSettings c;
    c.distribution=WT_NOISE_CELL_UNIFORM; c.metric=WT_NOISE_DISTANCE_EUCLIDEAN;
    c.output=WT_NOISE_CELL_F1; c.jitter=1; c.smoothness=0; c.evolution=0;
    return c;
}
struct WTNoiseCellularSample1D { float distance1,distance2,value; uint id; float toPoint; };
struct WTNoiseCellularSample2D { float distance1,distance2,value; uint id; float2 toPoint; };
struct WTNoiseCellularSample3D { float distance1,distance2,value; uint id; float3 toPoint; };
struct WTNoiseInternalCell
{
    float distance1,distance2,value;
    uint id;
    float3 toPoint;
    int3 cell;
};
WTNoiseInternalCell WTNoiseInternalCellZero()
{
    WTNoiseInternalCell v;
    v.distance1=0; v.distance2=0; v.value=0; v.id=0;
    v.toPoint=0; v.cell=0;
    return v;
}
#endif
