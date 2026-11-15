// Shared sampled-field operations. Distances and heights are in canvas pixels.
struct GraphImageFilter
{
    int mode, color;
    float2 origin, texel;
    float4 pixel, p, q;
};
struct GraphImageFilterState
{
    int index;
    float4 sum, height, best, auxiliary;
    float variance;
};
float GraphFilterFinite(float v) { return (asuint(v)&0x7f800000u)!=0x7f800000u ? v : 0; }
int GraphFilterCount(float v, int maximum) { return (int)clamp(floor(GraphFilterFinite(v)+0.5),1,maximum); }
GraphImageFilter GraphFilterSetup(int mode, int color, float2 uv, float2 texel, float4 pixel, float4 p, float4 q)
{
    GraphImageFilter c;
    c.mode=mode; c.color=color; c.origin=uv; c.texel=texel; c.pixel=pixel;
    c.p=max(float4(GraphFilterFinite(p.x),GraphFilterFinite(p.y),GraphFilterFinite(p.z),GraphFilterFinite(p.w)),0);
    c.q=q;
    if(mode==1) c.q=float4(GraphFilterCount(q.x,16),GraphFilterCount(q.y,16),0,0);
    if(mode==2)
    {
        float3 direction=float3(GraphFilterFinite(q.x),GraphFilterFinite(q.y),GraphFilterFinite(q.z));
        direction/=max(max(abs(direction.x),abs(direction.y)),max(abs(direction.z),1e-20));
        float xy=length(direction.xy);
        c.q=float4(xy>1e-7?direction.xy/max(xy,1e-7):0,xy>1e-7?direction.z/max(xy,1e-7):(direction.z<0?-1e10:1e10),GraphFilterCount(q.w,64));
    }
    if(mode==3) c.p.x=GraphFilterCount(p.x,4);
    if(mode==8) { c.p.x=GraphFilterFinite(p.x); c.p.y=GraphFilterCount(p.y,32); }
    return c;
}
int GraphFilterSamples(GraphImageFilter c)
{
    if(c.mode==0) return 5;
    if(c.mode==1) return 5+(int)(c.q.x*c.q.y);
    if(c.mode==2) return 1+(int)c.q.w;
    if(c.mode==3) return (int)(c.p.x*c.p.x);
    if(c.mode==5) return 4;
    if(c.mode==6 || c.mode==9) return 9;
    if(c.mode==7) return 36;
    if(c.mode==8) return 2*(int)c.p.y;
    return 4;
}
float2 GraphFilterAxis(int i)
{
    return i==0?float2(1,0):i==1?float2(-1,0):i==2?float2(0,1):float2(0,-1);
}
float2 GraphFilterDirection(GraphImageFilter c, int i)
{
    float angle=6.283185307179586*((uint)(i-5)/max((uint)c.q.y,1u)+0.5)/max(c.q.x,1);
    return float2(cos(angle),sin(angle));
}
float2 GraphFilterUV(GraphImageFilter c, int i)
{
    if(c.mode==0) return c.origin+(i==0?0:GraphFilterAxis(i-1)*c.p.x*c.texel);
    if(c.mode==1)
    {
        if(i<5) return c.origin+(i==0?0:GraphFilterAxis(i-1)*c.texel);
        float distance=c.p.x*((uint)(i-5)%max((uint)c.q.y,1u)+1)/max(c.q.y,1);
        return c.origin+GraphFilterDirection(c,i)*distance*c.texel;
    }
    if(c.mode==2) return c.origin+c.q.xy*(c.p.x*i/max(c.q.w,1))*c.texel;
    if(c.mode==3)
    {
        float2 offset=(float2((uint)i%max((uint)c.p.x,1u),(uint)i/max((uint)c.p.x,1u))+0.5)/max(c.p.x,1)-0.5;
        return c.origin+c.pixel.xy*offset.x+c.pixel.zw*offset.y;
    }
    if(c.mode==5) return c.origin+GraphFilterAxis(i)*c.p.x*c.texel;
    if(c.mode==6 || c.mode==9) return c.origin+(float2((uint)i%3u,(uint)i/3u)-1)*c.p.x*c.texel;
    if(c.mode==7)
    {
        uint quadrant=(uint)i/9u, sampleIndex=(uint)i%9u;
        float2 direction=float2((quadrant&1u)==0?-1:1,(quadrant&2u)==0?-1:1);
        return c.origin+float2(sampleIndex%3u,sampleIndex/3u)*0.5*direction*c.p.x*c.texel;
    }
    return frac(c.origin+float2(i&1,i>>1)*0.5);
}
float2 GraphFilterNextUV(GraphImageFilter c, GraphImageFilterState s)
{
    return c.mode==8 ? (s.index==0 ? c.origin : s.sum.xy) : GraphFilterUV(c,s.index);
}
float GraphFilterSlopeSin(float slope) { slope=clamp(slope,-1e10,1e10); return slope*rsqrt(1+slope*slope); }
void GraphFilterAccumulate(GraphImageFilter c, inout GraphImageFilterState s, float4 value)
{
    int i=s.index;
    if(c.mode==8)
    {
        float2 flow=float2(GraphFilterFinite(value.x),GraphFilterFinite(value.y));
        float2 delta=flow*c.p.x*c.texel/max(c.p.y,1);
        if((i&1)==0) { s.height.xy=i==0?c.origin:s.sum.xy; s.sum.xy=s.height.xy+delta*0.5; }
        else s.sum.xy=s.height.xy+delta;
    }
    else if(c.mode==5)
    {
        float3 normal=GraphUnitNormal(GraphDecodeNormal(value.xyz,(int)c.p.z));
        s.sum.x+=dot(normal.xy,GraphFilterAxis(i))*0.5;
    }
    else if(c.mode==6)
    {
        if(c.color!=0) value.rgb*=value.a;
        if(i==0) { s.height=value; s.best=value; }
        s.height=min(s.height,value); s.best=max(s.best,value);
        if(i==4) s.auxiliary=value;
        if(i==1 || i==3 || i==5 || i==7) s.sum+=value*0.25;
    }
    else if(c.mode==7)
    {
        if(c.color!=0) value.rgb*=value.a;
        s.sum+=value/9;
        if(c.color!=0) { float luminance=dot(value.rgb,float3(.2126,.7152,.0722)); s.height.x+=luminance/9; s.height.y+=luminance*luminance/9; }
        else s.height+=value*value/9;
        if((uint)i%9u==8u)
        {
            float4 variances=max(s.height-s.sum*s.sum,0);
            float variance=c.color!=0?max(s.height.y-s.height.x*s.height.x,0):
                (variances.x+(c.p.z>1?variances.y:0)+(c.p.z>2?variances.z:0)+(c.p.z>3?variances.w:0))/max(c.p.z,1);
            if(i==8 || variance<s.variance) { s.variance=variance; s.best=s.sum; }
            s.sum=0; s.height=0;
        }
    }
    else if(c.mode<3)
    {
        float height=GraphFilterFinite(value.x);
        if(i==0) { s.height.x=height; s.sum.x=c.mode==2?(c.q.z<0?0:1):0; }
        else if(c.mode==0) s.sum.x+=(s.height.x-height)*0.25;
        else if(c.mode==1)
        {
            if(i<5)
            {
                if(i<3) s.height.y+=height*(i==1?0.5:-0.5)*c.p.y;
                else s.height.z+=height*(i==3?0.5:-0.5)*c.p.y;
            }
            else
            {
                int step=(uint)(i-5)%max((uint)c.q.y,1u);
                float tangent=dot(s.height.yz,GraphFilterDirection(c,i));
                if(step==0) s.height.w=tangent;
                float distance=c.p.x*(step+1)/max(c.q.y,1);
                float slope=((height-s.height.x)*c.p.y-c.p.w)/max(distance,1e-6);
                s.height.w=max(s.height.w,slope);
                if(step==(int)c.q.y-1 && c.p.x>0)
                    s.sum.x+=max(GraphFilterSlopeSin(s.height.w)-GraphFilterSlopeSin(tangent),0)/max(c.q.x,1);
            }
        }
        else
        {
            float distance=c.p.x*i/max(c.q.w,1);
            float clearance=distance*c.q.z-(height-s.height.x)*c.p.y+c.p.z;
            float visibility=c.p.w>0?saturate(clearance/max(distance*c.p.w,1e-6)):(clearance>=0?1:0);
            if(c.p.x>0) s.sum.x=min(s.sum.x,visibility);
        }
    }
    else
    {
        if(c.color!=0) value.rgb*=value.a;
        float weight=1.0/GraphFilterSamples(c);
        if(c.mode==4)
        {
            float2 f=frac(c.origin); f=2*min(f,1-f); f=f*f*f*(f*(f*6-15)+10);
            weight=((i&1)!=0?1-f.x:f.x)*((i&2)!=0?1-f.y:f.y);
        }
        s.sum+=value*weight;
    }
    s.index++;
}
float4 GraphFilterResult(GraphImageFilter c, GraphImageFilterState s)
{
    if(c.mode==8) return float4(s.sum.xy,0,0);
    if(c.mode==5) { float v=c.p.x>0?s.sum.x*c.p.y/max(c.p.x,1e-6):0; return float4(saturate(v),saturate(-v),v,0); }
    if(c.mode==0) { float v=s.sum.x*c.p.y; return float4(saturate(v),saturate(-v),v,0); }
    if(c.mode==1) return float4(saturate(1-s.sum.x*c.p.z),0,0,0);
    if(c.mode==2) return float4(s.sum.x,0,0,0);
    if(c.mode==6)
    {
        float4 contrast=saturate((s.best-s.height)/max(abs(s.best)+abs(s.height),1e-6));
        float amount=c.p.y/(1+c.p.y);
        s.sum=clamp(s.auxiliary+(s.auxiliary-s.sum)*amount*(1-contrast),s.height,s.best);
        if(c.color!=0) s.sum.a=s.auxiliary.a;
    }
    if(c.mode==7) s.sum=s.best;
    if(c.color!=0) s.sum.rgb=s.sum.a==0?0:s.sum.rgb/(s.sum.a==0?1:s.sum.a);
    return s.sum;
}
float3 GraphPaletteCoordinates(float3 color, int mode) { return mode==1?GraphRGBToOklab(color):color; }
float GraphPaletteDistance(float3 a, float3 b) { float3 d=a-b; return dot(d,d); }
void GraphMedianPair(inout float4 a, inout float4 b) { float4 lower=min(a,b); b=max(a,b); a=lower; }
float4 GraphMedian9(float4 a0,float4 a1,float4 a2,float4 a3,float4 a4,float4 a5,float4 a6,float4 a7,float4 a8,int color)
{
    GraphMedianPair(a0,a1);
    GraphMedianPair(a2,a3);
    GraphMedianPair(a4,a5);
    GraphMedianPair(a6,a7);
    GraphMedianPair(a1,a2);
    GraphMedianPair(a3,a4);
    GraphMedianPair(a5,a6);
    GraphMedianPair(a7,a8);
    GraphMedianPair(a0,a1);
    GraphMedianPair(a2,a3);
    GraphMedianPair(a4,a5);
    GraphMedianPair(a6,a7);
    GraphMedianPair(a1,a2);
    GraphMedianPair(a3,a4);
    GraphMedianPair(a5,a6);
    GraphMedianPair(a7,a8);
    GraphMedianPair(a0,a1);
    GraphMedianPair(a2,a3);
    GraphMedianPair(a4,a5);
    GraphMedianPair(a6,a7);
    GraphMedianPair(a1,a2);
    GraphMedianPair(a3,a4);
    GraphMedianPair(a5,a6);
    GraphMedianPair(a7,a8);
    GraphMedianPair(a0,a1);
    GraphMedianPair(a2,a3);
    GraphMedianPair(a4,a5);
    GraphMedianPair(a6,a7);
    GraphMedianPair(a1,a2);
    GraphMedianPair(a3,a4);
    GraphMedianPair(a5,a6);
    GraphMedianPair(a7,a8);
    GraphMedianPair(a0,a1);
    GraphMedianPair(a2,a3);
    GraphMedianPair(a4,a5);
    GraphMedianPair(a6,a7);
    if(color!=0) a4.rgb=a4.a==0?0:a4.rgb/(a4.a==0?1:a4.a);
    return a4;
}
