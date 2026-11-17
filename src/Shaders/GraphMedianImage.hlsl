#include "GraphMedian.hlsl"
Texture2D<float4> _MedianSource;
float4 _MedianSize;
float4 GraphMedianImage(float2 uv)
{
    uint radius=(uint)_MedianSize.z;
    float2 center=min(floor(uv*_MedianSize.xy),_MedianSize.xy-1)+radius;
    float2 texel=1;
    GRAPH_MEDIAN_DENSE(_MedianSource.Load(int3((int2)sampleUV,0)));
}
