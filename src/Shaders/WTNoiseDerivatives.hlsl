#ifndef WHIMTEX_WT_NOISE_DERIVATIVES_INCLUDED
#define WHIMTEX_WT_NOISE_DERIVATIVES_INCLUDED

// Tensor-product interpolation weights and derivatives in lattice coordinates.
void WTNoiseInternalWeights(float t,int kind,out float4 w,out float4 d)
{
    if(kind==WT_NOISE_VALUE_CUBIC)
    {
        float t2=t*t,t3=t2*t;
        w=float4(-t+2*t2-t3,1-2*t2+t3,t+t2-t3,-t2+t3);
        d=float4(-1+4*t-3*t2,-4*t+3*t2,1+2*t-3*t2,-2*t+3*t2);
    }
    else
    {
        float f=kind==WT_NOISE_PERLIN ? _fnlInterpQuintic(t) : _fnlInterpHermite(t);
        float slope=kind==WT_NOISE_PERLIN ? 30*t*t*(t-1)*(t-1) : 6*t*(1-t);
        w=float4(1-f,f,0,0); d=float4(-slope,slope,0,0);
    }
}
float WTNoiseInternalGridDerivative(float3 p,int seed,int dimensions,int kind,out float3 gradient)
{
    int3 cell=(int3)floor(p);
    float3 t=p-floor(p);
    float4 wx,wy,wz,dx,dy,dz;
    WTNoiseInternalWeights(t.x,kind,wx,dx);
    WTNoiseInternalWeights(t.y,kind,wy,dy);
    WTNoiseInternalWeights(t.z,kind,wz,dz);
    if(dimensions<3) { wz=float4(1,0,0,0); dz=0; }
    if(dimensions<2) { wy=float4(1,0,0,0); dy=0; }
    int count=kind==WT_NOISE_VALUE_CUBIC ? 4 : 2;
    int baseOffset=kind==WT_NOISE_VALUE_CUBIC ? -1 : 0;
    gradient=0; float value=0;
    [loop] for(int z=0;z<(dimensions==3 ? count : 1);z++)
    [loop] for(int y=0;y<(dimensions>=2 ? count : 1);y++)
    [loop] for(int x=0;x<count;x++)
    {
        int3 c=cell+int3(x+baseOffset,dimensions>=2 ? y+baseOffset : 0,dimensions==3 ? z+baseOffset : 0);
        int3 primed=c*int3(PRIME_X,PRIME_Y,PRIME_Z);
        float sampleValue; float3 sampleGradient=0;
        if(kind==WT_NOISE_PERLIN)
        {
            if(dimensions==1) sampleGradient.x=(WTNoiseInternalHash(asuint(_fnlHash2D(seed,primed.x,0)))&1u)!=0 ? 1 : -1;
            else if(dimensions==2) sampleGradient.xy=_fnlGradient2D(seed,primed.x,primed.y);
            else sampleGradient=_fnlGradient3D(seed,primed.x,primed.y,primed.z);
            sampleValue=dot(sampleGradient,p-c);
        }
        else sampleValue=dimensions==3 ? _fnlValCoord3D(seed,primed.x,primed.y,primed.z)
            : _fnlValCoord2D(seed,primed.x,dimensions==1 ? 0 : primed.y);
        float weight=wx[x]*wy[y]*wz[z];
        gradient+=sampleGradient*weight+sampleValue*float3(dx[x]*wy[y]*wz[z],wx[x]*dy[y]*wz[z],wx[x]*wy[y]*dz[z]);
        value+=sampleValue*weight;
    }
    float normalization=kind==WT_NOISE_PERLIN ? (dimensions==1 ? 2 : dimensions==2 ? 1.4247691104677813 : 0.9649214148521423)
        : kind==WT_NOISE_VALUE_CUBIC ? (dimensions==1 ? 1.0/1.5 : dimensions==2 ? 1.0/(1.5*1.5) : 1.0/(1.5*1.5*1.5)) : 1;
    gradient*=normalization;
    return value*normalization;
}
float WTNoiseInternalBasisDerivative(float3 p,int seed,int dimensions,int kind,out float3 gradient)
{
    if(kind>=3) return WTNoiseInternalGridDerivative(p,seed,dimensions,kind,gradient);
    float value;
    if(dimensions==3)
    {
        if(kind==WT_NOISE_OPENSIMPLEX2) value=_fnlSingleOpenSimplex23D(seed,p.x,p.y,p.z,gradient);
        else value=_fnlSingleOpenSimplex2S3D(seed,p.x,p.y,p.z,gradient);
        // Transpose of the orthogonal domain rotation (the matrix is symmetric).
        gradient=dot(gradient,float3(2.0/3.0,2.0/3.0,2.0/3.0))-gradient;
    }
    else
    {
        if(kind==WT_NOISE_OPENSIMPLEX2) value=_fnlSingleSimplex2D(seed,p.x,p.y,gradient);
        else value=_fnlSingleOpenSimplex2S2D(seed,p.x,p.y,gradient);
        // Kernels differentiate unskewed coordinates, leaving only rotation here.
        gradient.xy=float2(gradient.x+gradient.y,gradient.y-gradient.x)*0.7071067811865475;
        if(dimensions==1) gradient.y=0;
    }
    return value;
}
#endif
