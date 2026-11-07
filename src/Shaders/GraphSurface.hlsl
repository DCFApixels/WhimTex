float2 GraphHeightBlend(float bottom,float top,float offset,float smoothness,float opacity)
{
    top+=offset; smoothness=max(smoothness,0);
    float highest=max(bottom,top), a=max(top-highest+smoothness,0), b=max(bottom-highest+smoothness,0);
    float mask=smoothness<=1e-7 ? (top==bottom ? 0.5 : (top>bottom ? 1 : 0)) : a/max(a+b,1e-20);
    mask*=saturate(opacity);
    return float2(lerp(bottom,top,mask),mask);
}
struct GraphTileData { float2 uv,cell,center; float random,mask; };
GraphTileData GraphTileUV(float2 uv,float2 count,float2 offset,float rowOffset,float rotation,float scaleRandom,float seed)
{
    GraphTileData o;
    count=max(round(abs(count)),1);
    float2 p=uv*count+offset; float shift=frac(floor(p.y)*0.5)*2*rowOffset;
    p.x+=shift; o.cell=floor(p);
    uint h=GraphHashPoint(o.cell,seed);
    o.random=GraphHashUnit(h);
    float angle=(GraphHashUnit(GraphHash(h^0x9e3779b9u))*2-1)*rotation;
    float scale=lerp(1,0.5+GraphHashUnit(GraphHash(h^0x68bc21ebu)),saturate(scaleRandom));
    o.uv=GraphRotatePoint(frac(p)-0.5,-angle)/scale+0.5;
    o.center=(o.cell+0.5-float2(shift,0)-offset)/count;
    o.mask=all(o.uv>=0) && all(o.uv<=1) ? 1 : 0;
    return o;
}
float GraphCross2(float2 a,float2 b) { return a.x*b.y-a.y*b.x; }
float3 GraphPerspectiveUV(float2 p,float2 q0,float2 q1,float2 q2,float2 q3)
{
    // Normalize the solve to the quad's own extent, including small translated quads.
    float size=max(max(length(q1-q0),length(q2-q0)),length(q3-q0));
    if(size<=1e-20) return 0;
    p=(p-q0)/size; q1=(q1-q0)/size; q2=(q2-q0)/size; q3=(q3-q0)/size; q0=0;
    float4 turns=float4(GraphCross2(q1,q2-q1),GraphCross2(q2-q1,q3-q2),GraphCross2(q3-q2,-q3),GraphCross2(-q3,q1));
    if(!(all(turns>1e-7)||all(turns< -1e-7))) return 0;
    float2 c=q2-q1-q3, d1=q1-q2, d2=q3-q2;
    float det=GraphCross2(d1,d2);
    if(abs(det)<=1e-8) return 0;
    float g=GraphCross2(c,d2)/det, h=GraphCross2(d1,c)/det;
    float2 a=q1*(1+g)-p*g, b=q3*(1+h)-p*h;
    det=GraphCross2(a,b);
    if(abs(det)<=1e-8) return 0;
    float2 result=float2(GraphCross2(p,b),GraphCross2(a,p))/det;
    return float3(result,all(result>=0)&&all(result<=1) ? 1 : 0);
}
float3 GraphRotateVector(float3 value,float3 axis,float angle)
{
    float size=max(max(abs(axis.x),abs(axis.y)),abs(axis.z));
    if(size==0) return value;
    axis/=size; axis*=rsqrt(dot(axis,axis));
    float s,c; sincos(radians(angle),s,c);
    return value*c+cross(axis,value)*s+axis*dot(axis,value)*(1-c);
}
