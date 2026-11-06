#ifndef WHIMTEX_WT_NOISE_DERIVATIVES_INCLUDED
#define WHIMTEX_WT_NOISE_DERIVATIVES_INCLUDED

// @wt.feature(Dim1; Plane)
// @wt.feature(Dim2; Plane)
// @wt.feature(Dim3)
// @wt.feature(Plane)
// @wt.feature(Grid)
// @wt.feature(Perlin)
// @wt.feature(Value)
// @wt.feature(ValueCubic)
// @wt.feature(OpenSimplex2)
// @wt.feature(OpenSimplex2S)
// @wt.feature(Filtering)


// Tensor-product interpolation weights and derivatives in lattice coordinates.
// @wt.block(Weights)
void WTNoiseInternalWeights(float t,int kind,out float4 w,out float4 d)
{
    w=0; d=0;
// @wt.if(ValueCubic)
    if(kind==WT_NOISE_VALUE_CUBIC)
    {
        float t2=t*t,t3=t2*t;
        w=float4(-t+2*t2-t3,1-2*t2+t3,t+t2-t3,-t2+t3);
        d=float4(-1+4*t-3*t2,-4*t+3*t2,1+2*t-3*t2,-2*t+3*t2);
    }
// @wt.endif
// @wt.if(!ValueCubic)
    if(kind!=WT_NOISE_VALUE_CUBIC)
    {
        float f=kind==WT_NOISE_PERLIN ? _fnlInterpQuintic(t) : _fnlInterpHermite(t);
        float slope=kind==WT_NOISE_PERLIN ? 30*t*t*(t-1)*(t-1) : 6*t*(1-t);
        w=float4(1-f,f,0,0); d=float4(-slope,slope,0,0);
    }
// @wt.endif
}
// @wt.end
// @wt.block(GridDerivative; Weights)
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
    // Preserve X-fastest accumulation. Two-point kernels use one flat traversal;
    // cubic interpolation keeps rows to let the GPU reuse their weights.
    int axisBits=kind==WT_NOISE_VALUE_CUBIC ? 2 : 1;
    int samples=1<<(axisBits*dimensions),mask=count-1;
// @wt.if(ValueCubic)
    if(kind==WT_NOISE_VALUE_CUBIC) samples=count;
    [loop] for(int latticeZ=0;latticeZ<(kind==WT_NOISE_VALUE_CUBIC && dimensions==3 ? count : 1);latticeZ++)
    [loop] for(int latticeY=0;latticeY<(kind==WT_NOISE_VALUE_CUBIC && dimensions>=2 ? count : 1);latticeY++)
// @wt.endif
    [loop] for(int latticeIndex=0;latticeIndex<samples;latticeIndex++)
    {
        int x=kind==WT_NOISE_VALUE_CUBIC ? latticeIndex : latticeIndex&mask;
        int y=dimensions>=2 ? (latticeIndex>>axisBits)&mask : 0;
        int z=dimensions==3 ? (latticeIndex>>(axisBits*2))&mask : 0;
// @wt.if(ValueCubic)
        if(kind==WT_NOISE_VALUE_CUBIC) { y=latticeY; z=latticeZ; }
// @wt.endif
        int3 c=cell+int3(x+baseOffset,dimensions>=2 ? y+baseOffset : 0,dimensions==3 ? z+baseOffset : 0);
        int3 primed=c*int3(PRIME_X,PRIME_Y,PRIME_Z);
        float sampleValue=0; float3 sampleGradient=0;
// @wt.if(Perlin)
        if(kind==WT_NOISE_PERLIN)
        {
// @wt.if(Dim1)
            if(dimensions==1) sampleGradient.x=(WTNoiseInternalHash(asuint(_fnlHash2D(seed,primed.x,0)))&1u)!=0 ? 1 : -1;
// @wt.endif
// @wt.if(Dim2)
            if(dimensions==2) sampleGradient.xy=_fnlGradient2D(seed,primed.x,primed.y);
// @wt.endif
// @wt.if(Dim3)
            if(dimensions!=1 && dimensions!=2) sampleGradient=_fnlGradient3D(seed,primed.x,primed.y,primed.z);
// @wt.endif
            sampleValue=dot(sampleGradient,p-c);
        }
// @wt.endif
// @wt.if(!Perlin)
// @wt.if(Dim3)
        if(kind!=WT_NOISE_PERLIN && dimensions==3) sampleValue=_fnlValCoord3D(seed,primed.x,primed.y,primed.z);
// @wt.endif
// @wt.if(Plane)
        if(kind!=WT_NOISE_PERLIN && dimensions!=3) sampleValue=_fnlValCoord2D(seed,primed.x,dimensions==1 ? 0 : primed.y);
// @wt.endif
// @wt.endif
        float weight=wx[x]*wy[y]*wz[z];
        gradient+=sampleGradient*weight+sampleValue*float3(dx[x]*wy[y]*wz[z],wx[x]*dy[y]*wz[z],wx[x]*wy[y]*dz[z]);
        value+=sampleValue*weight;
    }
    float normalization=kind==WT_NOISE_PERLIN ? (dimensions==1 ? 2 : dimensions==2 ? 1.4247691104677813 : 0.9649214148521423)
        : kind==WT_NOISE_VALUE_CUBIC ? (dimensions==1 ? 1.0/1.5 : dimensions==2 ? 1.0/(1.5*1.5) : 1.0/(1.5*1.5*1.5)) : 1;
    gradient*=normalization;
    return value*normalization;
}
// @wt.end
// @wt.block(BasisDerivative)
float WTNoiseInternalBasisDerivative(float3 p,int seed,int dimensions,int kind,out float3 gradient)
{
    gradient=0;
// @wt.if(Grid)
    if(kind>=3) return WTNoiseInternalGridDerivative(p,seed,dimensions,kind,gradient);
// @wt.endif
// @wt.if(!Grid)
    float value=0;
// @wt.if(Dim3)
    if(dimensions==3)
    {
// @wt.if(OpenSimplex2)
        if(kind==WT_NOISE_OPENSIMPLEX2) value=_fnlSingleOpenSimplex23D(seed,p.x,p.y,p.z,gradient);
// @wt.endif
// @wt.if(OpenSimplex2S)
        if(kind!=WT_NOISE_OPENSIMPLEX2) value=_fnlSingleOpenSimplex2S3D(seed,p.x,p.y,p.z,gradient);
// @wt.endif
        // Transpose of the orthogonal domain rotation (the matrix is symmetric).
        gradient=dot(gradient,float3(2.0/3.0,2.0/3.0,2.0/3.0))-gradient;
    }
// @wt.endif
// @wt.if(Plane)
    if(dimensions!=3)
    {
// @wt.if(OpenSimplex2)
        if(kind==WT_NOISE_OPENSIMPLEX2) value=_fnlSingleSimplex2D(seed,p.x,p.y,gradient);
// @wt.endif
// @wt.if(OpenSimplex2S)
        if(kind!=WT_NOISE_OPENSIMPLEX2) value=_fnlSingleOpenSimplex2S2D(seed,p.x,p.y,gradient);
// @wt.endif
        // Kernels differentiate unskewed coordinates, leaving only rotation here.
        gradient.xy=float2(gradient.x+gradient.y,gradient.y-gradient.x)*0.7071067811865475;
        if(dimensions==1) gradient.y=0;
    }
// @wt.endif
    return value;
// @wt.endif
    gradient=0; return 0;
}
// @wt.end
#endif
