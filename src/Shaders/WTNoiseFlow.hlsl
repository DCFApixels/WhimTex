#ifndef WHIMTEX_WT_NOISE_FLOW_INCLUDED
#define WHIMTEX_WT_NOISE_FLOW_INCLUDED
#include "WTNoiseBasis.hlsl"

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
float WTNoiseInternalFlow(float3 p,WTNoiseSettings n,WTNoiseFlowSettings flow,int dimensions)
{
    float2 saved=WTNoiseInternalRotation;
    WTNoiseInternalRotation=WTNoiseInternalFlowRotation(flow.phase);
    float value=WTNoiseInternalBasis(p,n,n.fractal,dimensions,WT_NOISE_OPENSIMPLEX2S);
    WTNoiseInternalRotation=saved;
    return value;
}
float3 WTNoiseInternalFlowGradient(float3 p,WTNoiseSettings n,WTNoiseFlowSettings flow,int dimensions)
{
    float2 saved=WTNoiseInternalRotation;
    WTNoiseInternalRotation=WTNoiseInternalFlowRotation(flow.phase);
    float3 value=WTNoiseInternalBasisGradient(p,n,n.fractal,dimensions,WT_NOISE_OPENSIMPLEX2S);
    WTNoiseInternalRotation=saved;
    return value;
}
float WTNoiseFlow(float p,WTNoiseSettings n,WTNoiseFlowSettings flow) { return WTNoiseInternalFlow(float3(p,0,0),n,flow,1); }
float WTNoiseFlow(float2 p,WTNoiseSettings n,WTNoiseFlowSettings flow) { return WTNoiseInternalFlow(float3(p,0),n,flow,2); }
float WTNoiseFlow(float3 p,WTNoiseSettings n,WTNoiseFlowSettings flow) { return WTNoiseInternalFlow(p,n,flow,3); }
float WTNoiseFlow(float p,WTNoiseSettings n) { return WTNoiseFlow(p,n,WTNoiseFlowDefaults()); }
float WTNoiseFlow(float2 p,WTNoiseSettings n) { return WTNoiseFlow(p,n,WTNoiseFlowDefaults()); }
float WTNoiseFlow(float3 p,WTNoiseSettings n) { return WTNoiseFlow(p,n,WTNoiseFlowDefaults()); }
float WTNoiseGradient(float p,WTNoiseSettings n,WTNoiseFlowSettings flow) { return WTNoiseInternalFlowGradient(float3(p,0,0),n,flow,1).x; }
float2 WTNoiseGradient(float2 p,WTNoiseSettings n,WTNoiseFlowSettings flow) { return WTNoiseInternalFlowGradient(float3(p,0),n,flow,2).xy; }
float3 WTNoiseGradient(float3 p,WTNoiseSettings n,WTNoiseFlowSettings flow) { return WTNoiseInternalFlowGradient(p,n,flow,3); }
float2 WTNoiseCurl(float2 p,WTNoiseSettings n,WTNoiseFlowSettings flow)
{
    float2 gradient=WTNoiseGradient(p,n,flow); return float2(gradient.y,-gradient.x);
}
float3 WTNoiseCurl(float3 p,WTNoiseSettings n,WTNoiseFlowSettings flow)
{
    float3 gx=WTNoiseGradient(p,n,flow);
    n.seed^=0x68bc21eb;
    float3 gy=WTNoiseGradient(p,n,flow);
    n.seed^=0x68bc21eb^0x63d83595;
    float3 gz=WTNoiseGradient(p,n,flow);
    return float3(gz.y-gy.z,gx.z-gz.x,gy.x-gx.y);
}
#endif
