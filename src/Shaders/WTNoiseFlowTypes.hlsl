#ifndef WHIMTEX_WT_NOISE_FLOW_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_FLOW_TYPES_INCLUDED

struct WTNoiseFlowSettings { float phase; };
WTNoiseFlowSettings WTNoiseFlowDefaults()
{
    WTNoiseFlowSettings flow; flow.phase=0; return flow;
}
// OpenSimplex2S with rotating gradients. Phase is in cycles, independent of time.
// The 1D form samples a fixed slice of the 2D field, like OpenSimplex2S itself.
float2 WTNoiseInternalFlowRotation(float phase)
{
    float sine,cosine; sincos(6.283185307179586*frac(phase),sine,cosine);
    return float2(sine,cosine);
}
#endif
