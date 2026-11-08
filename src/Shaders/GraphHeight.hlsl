// Shared texture-height stencil; callers supply texture type, size and sampling.
float GraphReadHeight(WT_GRAPH_HEIGHT_TEXTURE tex,float2 uv,int channel,int wrap,int filter)
{
    return GraphChannel(WT_GRAPH_HEIGHT_SAMPLE(tex,uv,wrap,filter),channel);
}
float2 GraphHeightSlope(WT_GRAPH_HEIGHT_TEXTURE tex,float2 uv,float radius,int channel,int derivative,int wrap,int filter)
{
    radius=max(radius,0.0001); float2 stepUV=radius/WT_GRAPH_HEIGHT_SIZE(tex);
    float l=GraphReadHeight(tex,uv-float2(stepUV.x,0),channel,wrap,filter);
    float r=GraphReadHeight(tex,uv+float2(stepUV.x,0),channel,wrap,filter);
    float b=GraphReadHeight(tex,uv-float2(0,stepUV.y),channel,wrap,filter);
    float t=GraphReadHeight(tex,uv+float2(0,stepUV.y),channel,wrap,filter);
    if(derivative==2) return float2(r-l,t-b)/(2*radius);
    float tl=GraphReadHeight(tex,uv+stepUV*float2(-1,1),channel,wrap,filter);
    float tr=GraphReadHeight(tex,uv+stepUV,channel,wrap,filter);
    float bl=GraphReadHeight(tex,uv-stepUV,channel,wrap,filter);
    float br=GraphReadHeight(tex,uv+stepUV*float2(1,-1),channel,wrap,filter);
    float side=derivative==0 ? 3:1, center=derivative==0 ? 10:2;
    return float2(side*(br+tr-bl-tl)+center*(r-l),side*(tl+tr-bl-br)+center*(t-b))/((4*side+2*center)*radius);
}
