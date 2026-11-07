float3 GraphNormalStrength(float3 normal,float strength,int encoding)
{
    float3 n=GraphUnitNormal(GraphDecodeNormal(normal,encoding));
    // Strength 0 is flat, 1 is the original unit normal; negative values reverse XY.
    n=float3(n.xy*strength,lerp(1,n.z,saturate(abs(strength))));
    return GraphEncodeNormal(GraphUnitNormal(n),encoding);
}
float GraphExtendedSpecular(float3 normal,float3 lightDirection,float roughness)
{
    // Orthographic view V=(0,0,1), with the same finite sharpness rule as the FX.
    float3 h=lightDirection+float3(0,0,1);
    float h2=dot(h,h);
    if(h2<=1e-12 || normal.z<=0) return 0;
    h*=rsqrt(h2);
    roughness=max(saturate(roughness),0.01);
    float exponent=max(2/(roughness*roughness)-2,1);
    return pow(saturate(dot(normal,h)),exponent)*saturate(dot(normal,lightDirection));
}
float4 GraphNormalLighting(float4 surface,float3 normal,float3 lightDirection,float4 lightColor,
    float intensity,float ambient,float4 specularColor,float specularIntensity,float roughness,bool specularOnly,int encoding)
{
    float3 n=GraphUnitNormal(GraphDecodeNormal(normal,encoding));
    float3 l=GraphUnitNormal(lightDirection);
    // Tint alpha scales light strength; surface alpha passes through in both modes.
    // RGB and intensities are unrestricted, including negative values and HDR.
    float3 rgb=0;
    if(!specularOnly) rgb=surface.rgb*(ambient+lightColor.rgb*(max(dot(n,l),0)*intensity*lightColor.a));
    if(specularIntensity!=0)
        rgb+=specularColor.rgb*(specularColor.a*specularIntensity*GraphExtendedSpecular(n,l,roughness));
    return float4(rgb,surface.a);
}
float4 GraphUnpremultiply(float4 color)
{
    // Exactly zero alpha has no recoverable straight RGB. Nonzero alpha, however
    // small or negative, is divided as supplied without an epsilon or RGB clipping.
    float divisor=color.a==0 ? 1.0 : color.a;
    return float4(color.a==0 ? float3(0,0,0) : color.rgb/divisor,color.a);
}
float2 GraphFlipbook(float2 uv,float columns,float rows,float frame,int rowOrder)
{
    // Integer dimensions in [1,4096]; the product remains exactly representable.
    float2 grid=clamp(floor(float2(columns,rows)),1,4096);
    float count=grid.x*grid.y;
    frame=floor(frame); frame-=floor(frame/count)*count;
    float row=floor(frame/grid.x), column=frame-row*grid.x;
    if(rowOrder==1) row=grid.y-1-row;
    // Frame 0 is bottom-left by default. Top-to-bottom changes row order only,
    // keeping the local UV orientation. Negative frames wrap around the grid.
    return (uv+float2(column,row))/grid;
}
float GraphBayerThreshold(float2 pixel,int mode)
{
    int bits=mode==1 ? 1 : mode==2 ? 3 : 2;
    float side=(float)(1<<bits);
    // Positive wrapping also gives a consistent pattern for negative UV.
    float2 wrapped=pixel-floor(pixel/side)*side;
    int2 q=(int2)wrapped;
    int rank=0;
    [unroll] for(int bit=0;bit<3;bit++)
    {
        if(bit<bits)
        {
            int x=(q.x>>bit)&1, y=(q.y>>bit)&1;
            rank=4*rank+((x^y)<<1)+y;
        }
    }
    return (rank+0.5)/(side*side);
}
float4 GraphDither(float4 color,float2 uv,float levels,float pixelSize,int mode)
{
    float steps=max(floor(levels+0.5),2)-1;
    float2 pixel=floor(uv*max(_CanvasSize.xy,1)/max(abs(pixelSize),1));
    float threshold=GraphBayerThreshold(pixel,mode);
    // Quantization continues beyond [0,1] with spacing 1/(Levels-1). Alpha is
    // untouched, including for transparent colors with nonzero RGB.
    return float4(floor(color.rgb*steps+1-threshold)/steps,color.a);
}
