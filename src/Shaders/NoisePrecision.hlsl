#ifndef WHIMTEX_NOISE_PRECISION_INCLUDED
#define WHIMTEX_NOISE_PRECISION_INCLUDED

// A main part and a residual, not GPU double precision. Keep them separate until
// lattice indices and the local fraction have been extracted.
struct WTNoiseWide3 { float3 high,low; };
WTNoiseWide3 WTNoiseWide(float3 high,float3 low)
{
    WTNoiseWide3 v; v.high=high; v.low=low; return v;
}
WTNoiseWide3 WTNoiseWide(float3 value) { return WTNoiseWide(value,0); }
WTNoiseWide3 WTNoiseWideSelect(float3 selectB,WTNoiseWide3 a,WTNoiseWide3 b)
{
    return WTNoiseWide(float3(selectB.x!=0 ? b.high.x : a.high.x,selectB.y!=0 ? b.high.y : a.high.y,selectB.z!=0 ? b.high.z : a.high.z),
        float3(selectB.x!=0 ? b.low.x : a.low.x,selectB.y!=0 ? b.low.y : a.low.y,selectB.z!=0 ? b.low.z : a.low.z));
}
WTNoiseWide3 WTNoiseWideAdd(WTNoiseWide3 a,WTNoiseWide3 b)
{
    precise float3 sum=a.high+b.high;
    // Subtract the rounded sum from the larger operand first. Besides avoiding
    // cancellation, this order retains the residual through shader optimization.
    precise float3 fromA=(a.high-sum)+b.high;
    precise float3 fromB=(b.high-sum)+a.high;
    precise float3 error=float3(abs(a.high.x)>=abs(b.high.x) ? fromA.x : fromB.x,
        abs(a.high.y)>=abs(b.high.y) ? fromA.y : fromB.y,abs(a.high.z)>=abs(b.high.z) ? fromA.z : fromB.z);
    error+=a.low+b.low;
    precise float3 high=sum+error;
    precise float3 low=(sum-high)+error;
    return WTNoiseWide(high,low);
}
WTNoiseWide3 WTNoiseWideNegate(WTNoiseWide3 a) { return WTNoiseWide(-a.high,-a.low); }
WTNoiseWide3 WTNoiseWideMultiply(WTNoiseWide3 a,WTNoiseWide3 b)
{
    precise float3 splitA=a.high*4097.0,splitB=b.high*4097.0;
    precise float3 ah=splitA-(splitA-a.high),bh=splitB-(splitB-b.high);
    precise float3 al=a.high-ah,bl=b.high-bh;
    precise float3 product=a.high*b.high;
    precise float3 error=((ah*bh-product)+ah*bl+al*bh)+al*bl;
    error+=a.high*b.low+a.low*b.high;
    error+=a.low*b.low;
    precise float3 high=product+error;
    precise float3 low=(product-high)+error;
    return WTNoiseWide(high,low);
}
WTNoiseWide3 WTNoiseWideDivide(WTNoiseWide3 a,WTNoiseWide3 b)
{
    precise float3 quotient=a.high/b.high;
    WTNoiseWide3 remainder=WTNoiseWideAdd(a,WTNoiseWideNegate(WTNoiseWideMultiply(b,WTNoiseWide(quotient))));
    precise float3 correction=(remainder.high+remainder.low)/b.high;
    return WTNoiseWideAdd(WTNoiseWide(quotient),WTNoiseWide(correction));
}
WTNoiseWide3 WTNoiseWideFloor(WTNoiseWide3 a)
{
    precise float3 high=floor(a.high);
    precise float3 low=floor((a.high-high)+a.low);
    return WTNoiseWideAdd(WTNoiseWide(high),WTNoiseWide(low));
}
WTNoiseWide3 WTNoiseWideRepeat(WTNoiseWide3 a,WTNoiseWide3 period)
{
    WTNoiseWide3 quotient=WTNoiseWideFloor(WTNoiseWideDivide(a,period));
    WTNoiseWide3 r=WTNoiseWideAdd(a,WTNoiseWideNegate(WTNoiseWideMultiply(quotient,period)));
    // The division estimate may round to the neighboring integer at a boundary.
    float3 negative=float3(r.high.x<0 || (r.high.x==0 && r.low.x<0),r.high.y<0 || (r.high.y==0 && r.low.y<0),r.high.z<0 || (r.high.z==0 && r.low.z<0));
    r=WTNoiseWideAdd(r,WTNoiseWideMultiply(period,WTNoiseWide(negative)));
    WTNoiseWide3 difference=WTNoiseWideAdd(r,WTNoiseWideNegate(period));
    float3 above=float3(difference.high.x>0 || (difference.high.x==0 && difference.low.x>=0),difference.high.y>0 || (difference.high.y==0 && difference.low.y>=0),difference.high.z>0 || (difference.high.z==0 && difference.low.z>=0));
    return WTNoiseWideAdd(r,WTNoiseWideNegate(WTNoiseWideMultiply(period,WTNoiseWide(above))));
}
precise float3 WTNoiseWideFraction(WTNoiseWide3 value,out int3 whole)
{
    int3 integer=(int3)floor(value.high);
    precise float3 fraction=(value.high-(float3)integer)+value.low;
    int3 carry=(int3)floor(fraction);
    whole=integer+carry;
    return fraction-(float3)carry;
}

// Shared with the existing CPU-prepared Noise Layer. Keep this accumulation order:
// its input vectors already contain transformed double-precision basis coefficients.
void WTNoiseAccumulateProduct(inout int3 whole,inout float3 fraction,float value,float3 high,float3 low)
{
    precise float splitV=value*4097.0;
    precise float vh=splitV-(splitV-value),vl=value-vh;
    precise float3 splitA=high*4097.0;
    precise float3 ah=splitA-(splitA-high),al=high-ah;
    precise float3 product=high*value;
    precise float3 error=((ah*vh-product)+ah*vl+al*vh)+al*vl;
    error+=low*value;
    int3 integer=(int3)floor(product);
    precise float3 part=(product-(float3)integer)+error;
    int3 carry=(int3)floor(part);
    whole+=integer+carry;
    fraction+=part-(float3)carry;
}
#endif
