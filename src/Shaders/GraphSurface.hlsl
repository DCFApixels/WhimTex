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
    o.uv=GraphRotatePoint(frac(p)-0.5,-angle*360)/scale+0.5;
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
float3 GraphRotateVector(float3 value,float3 axis,float angle,int unit)
{
    if(unit==1) angle=radians(angle);
    else if(unit==2) angle*=6.28318530718;
    axis=normalize(axis);
    float s,c; sincos(angle,s,c);
    return value*c+cross(axis,value)*s+axis*dot(axis,value)*(1-c);
}

float4 GraphQuaternionNormalize(float4 q)
{
    float scale=max(max(abs(q.x),abs(q.y)),max(abs(q.z),abs(q.w)));
    if(scale==0) return float4(0,0,0,1);
    q/=scale;
    return q*rsqrt(dot(q,q));
}
float GraphQuaternionAngleScale(int unit)
{
    return unit==1 ? 0.017453292519943295 : unit==2 ? 6.283185307179586 : 1;
}
float4 GraphQuaternionMultiply(float4 a,float4 b)
{
    return float4(a.w*b.xyz+b.w*a.xyz+cross(a.xyz,b.xyz),a.w*b.w-dot(a.xyz,b.xyz));
}
float4 GraphEulerToQuaternion(float3 euler,int unit,int order)
{
    float3 s,c;
    sincos(euler*(0.5*GraphQuaternionAngleScale(unit)),s,c);
    float4 base=float4(s.x*c.y*c.z,c.x*s.y*c.z,c.x*c.y*s.z,c.x*c.y*c.z);
    float4 mixed=float4(c.x*s.y*s.z,s.x*c.y*s.z,s.x*s.y*c.z,s.x*s.y*s.z);
    float4 signs=order==0 ? float4(-1,1,-1,1) : order==1 ? float4(1,1,-1,-1) :
                 order==2 ? float4(-1,1,1,-1) : order==3 ? float4(-1,-1,1,1) :
                 order==4 ? float4(1,-1,-1,1) : float4(1,-1,1,-1);
    return base+mixed*signs;
}
float3 GraphQuaternionEulerTriplet(float m00,float m10,float m20,float m21,float m22,float m01,float m11)
{
    float cosine=length(float2(m00,m10));
    float3 angles=float3(0,atan2(-m20,cosine),0);
    if(cosine>1e-4) angles.xz=float2(atan2(m21,m22),atan2(m10,m00));
    else angles.z=atan2(-m01,m11);
    return angles;
}
float3 GraphQuaternionToEuler(float4 rotation,int unit,int order)
{
    float4 q=GraphQuaternionNormalize(rotation);
    float m00=1-2*(q.y*q.y+q.z*q.z), m01=2*(q.x*q.y-q.z*q.w), m02=2*(q.x*q.z+q.y*q.w);
    float m10=2*(q.x*q.y+q.z*q.w), m11=1-2*(q.x*q.x+q.z*q.z), m12=2*(q.y*q.z-q.x*q.w);
    float m20=2*(q.x*q.z-q.y*q.w), m21=2*(q.y*q.z+q.x*q.w), m22=1-2*(q.x*q.x+q.y*q.y);
    float3 angles;
    if(order==0) angles=GraphQuaternionEulerTriplet(m00,m10,m20,m21,m22,m01,m11);
    else if(order==1) angles=-GraphQuaternionEulerTriplet(m00,m20,m10,m12,m11,m02,m22).xzy;
    else if(order==2) angles=-GraphQuaternionEulerTriplet(m11,m01,m21,m20,m22,m10,m00).yxz;
    else if(order==3) angles=GraphQuaternionEulerTriplet(m11,m21,m01,m02,m00,m12,m22).zxy;
    else if(order==4) angles=GraphQuaternionEulerTriplet(m22,m02,m12,m10,m11,m20,m00).yzx;
    else angles=-GraphQuaternionEulerTriplet(m22,m12,m02,m01,m00,m21,m11).zyx;
    return angles/GraphQuaternionAngleScale(unit);
}
float3 GraphRotateByQuaternion(float3 value,float4 rotation)
{
    float4 q=GraphQuaternionNormalize(rotation);
    return value+2*cross(q.xyz,cross(q.xyz,value)+q.w*value);
}
float4 GraphQuaternionInverse(float4 q)
{
    float scale=max(max(abs(q.x),abs(q.y)),max(abs(q.z),abs(q.w)));
    if(scale==0) return float4(0,0,0,1);
    q/=scale;
    return (float4(-q.xyz,q.w)/dot(q,q))/scale;
}
float4 GraphQuaternionSlerp(float4 a,float4 b,float t)
{
    a=GraphQuaternionNormalize(a); b=GraphQuaternionNormalize(b);
    float cosine=dot(a,b);
    if(cosine<0) { b=-b; cosine=-cosine; }
    if(cosine>0.9995) return GraphQuaternionNormalize(lerp(a,b,t));
    float angle=acos(clamp(cosine,0,1));
    return GraphQuaternionNormalize((sin((1-t)*angle)*a+sin(t*angle)*b)/sin(angle));
}
