#ifndef WHIMTEX_GRAPH_KUWAHARA_ANISOTROPIC
#define WHIMTEX_GRAPH_KUWAHARA_ANISOTROPIC

float GraphKuwGaussian(float offset, float radius)
{
    float x=offset/max(radius,1.0);
    return exp(-4.5*x*x);
}
float4 GraphKuwComponents(int count)
{
    return float4(1,count>1?1:0,count>2?1:0,count>3?1:0);
}
float3 GraphKuwTensor(float4 dx, float4 dy, int count)
{
    float4 active=GraphKuwComponents(count);
    return float3(dot(dx*dx,active),dot(dx*dy,active),dot(dy*dy,active));
}
float3 GraphKuwTensorSamples(float4 nw,float4 n,float4 ne,float4 w,float4 e,float4 sw,float4 s,float4 se,int count)
{
    float4 dx=0.182*(ne+se-nw-sw)+0.636*(e-w);
    float4 dy=0.182*(nw+ne-sw-se)+0.636*(n-s);
    return GraphKuwTensor(dx,dy,count);
}

struct GraphKuwAniso
{
    float4 sum[8];
    float4 square[8];
    float weight[8];
    float4 reference;
    float2 direction;
    float2 axes;
    int2 bounds;
    float zeta,eta,sharpness;
    int components;
};
GraphKuwAniso GraphKuwAnisoSetup(float3 tensor,float4 center,float radius,float sharpness,float eccentricity,int components)
{
    GraphKuwAniso state;
    state.reference=center;
    state.components=components;
    state.sharpness=16*saturate(sharpness)*saturate(sharpness);
    float discriminant=length(float2(tensor.x-tensor.z,2*tensor.y));
    float trace=tensor.x+tensor.z;
    float anisotropy=trace>0?saturate(discriminant/trace):0;
    float2 tangent=float2(0.5*(trace+discriminant)-tensor.x,-tensor.y);
    float lengthSquared=dot(tangent,tangent);
    state.direction=lengthSquared>0?tangent*rsqrt(max(lengthSquared,1e-30)):(tensor.x>tensor.z?float2(0,1):float2(1,0));
    float stretch=1+anisotropy*max(0.01,clamp(eccentricity,0,2));
    float r=max(radius,1e-5);
    state.axes=float2(r*stretch,r/stretch);
    float2 major=state.direction*state.axes.x;
    float2 minor=float2(-state.direction.y,state.direction.x)*state.axes.y;
    state.bounds=(int2)ceil(sqrt(major*major+minor*minor));
    state.zeta=2/r;
    // Eight overlapping polynomial sectors, envelope angle 3*pi/16.
    state.eta=(state.zeta+0.8314696123)/0.3086582838;
    [unroll] for(int k=0;k<8;k++) { state.sum[k]=0;state.square[k]=0;state.weight[k]=0.125; }
    return state;
}
float2 GraphKuwAnisoPoint(GraphKuwAniso state,int2 offset)
{
    return float2(dot((float2)offset,state.direction),dot((float2)offset,float2(-state.direction.y,state.direction.x)))/state.axes;
}
void GraphKuwAnisoAdd(inout GraphKuwAniso state,float2 p,float4 value)
{
    float weights[8];float total=0;
    float2 v=p;
    [unroll] for(int k=0;k<8;k++)
    {
        float w=max(0,v.y+state.zeta-state.eta*v.x*v.x);
        weights[k]=w*w;total+=w*w;
        v=0.7071067812*float2(v.x-v.y,v.x+v.y);
    }
    float radial=exp(-3.1415926536*dot(p,p))/max(total,1e-30);
    float4 delta=value-state.reference;
    [unroll] for(int sector=0;sector<8;sector++)
    {
        float w=weights[sector]*radial;
        state.weight[sector]+=w;
        state.sum[sector]+=w*delta;
        state.square[sector]+=w*delta*delta;
    }
}
float4 GraphKuwAnisoResult(GraphKuwAniso state)
{
    float deviation[8];float minimum=3.402823e38;
    [unroll] for(int k=0;k<8;k++)
    {
        float4 mean=state.sum[k]/state.weight[k];
        float4 variance=max(state.square[k]/state.weight[k]-mean*mean,0);
        deviation[k]=max(0.02,dot(sqrt(variance),GraphKuwComponents(state.components)));
        minimum=min(minimum,deviation[k]);
    }
    float4 result=0;float total=0;
    [unroll] for(int sector=0;sector<8;sector++)
    {
        // Rescaling all weights by their largest value avoids overflow in quiet HDR areas.
        float w=pow(minimum/deviation[sector],state.sharpness);
        result+=state.sum[sector]*(w/state.weight[sector]);total+=w;
    }
    return state.reference+result/total;
}
#endif
