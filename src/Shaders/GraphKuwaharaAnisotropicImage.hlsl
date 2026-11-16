#include "GraphKuwaharaAnisotropic.hlsl"
Texture2D<float4> _KuwSource,_KuwTensorImage;
float4 _KuwSize; // tile width, height, halo, active components
float4 _KuwAnisotropic; // radius, sharpness, uniformity, eccentricity
int _KuwHasTensor;

float4 GraphKuwAnisoLoad(int2 p) { return _KuwSource.Load(int3(p,0)); }
float3 GraphKuwAnisoTensorAt(int2 p)
{
    return GraphKuwTensorSamples(GraphKuwAnisoLoad(p+int2(-1,1)),GraphKuwAnisoLoad(p+int2(0,1)),GraphKuwAnisoLoad(p+int2(1,1)),
        GraphKuwAnisoLoad(p+int2(-1,0)),GraphKuwAnisoLoad(p+int2(1,0)),
        GraphKuwAnisoLoad(p+int2(-1,-1)),GraphKuwAnisoLoad(p+int2(0,-1)),GraphKuwAnisoLoad(p+int2(1,-1)),(int)_KuwSize.w);
}
float3 GraphKuwAnisoTensorDirect(int2 p)
{
    int radius=(int)_KuwAnisotropic.z;
    float3 tensor=0;float total=0;
    [loop] for(int y=-radius;y<=radius;y++)
    [loop] for(int x=-radius;x<=radius;x++)
    {
        float w=GraphKuwGaussian(x,radius)*GraphKuwGaussian(y,radius);
        tensor+=w*GraphKuwAnisoTensorAt(p+int2(x,y));total+=w;
    }
    return tensor/total;
}
float4 GraphKuwaharaAnisotropicImage(float2 uv)
{
    int2 center=min((int2)(uv*_KuwSize.xy),(int2)_KuwSize.xy-1)+(int)_KuwSize.z;
    float4 value=GraphKuwAnisoLoad(center);
    if(_KuwAnisotropic.x<=0) return value;
    float3 tensor=_KuwHasTensor!=0?_KuwTensorImage.Load(int3(center,0)).xyz:GraphKuwAnisoTensorDirect(center);
    GraphKuwAniso state=GraphKuwAnisoSetup(tensor,value,_KuwAnisotropic.x,_KuwAnisotropic.y,_KuwAnisotropic.w,(int)_KuwSize.w);
    [loop] for(int y=-state.bounds.y;y<=state.bounds.y;y++)
    [loop] for(int x=-state.bounds.x;x<=state.bounds.x;x++)
    {
        if(x==0&&y==0) continue;
        float2 p=GraphKuwAnisoPoint(state,int2(x,y));
        if(dot(p,p)<=1) GraphKuwAnisoAdd(state,p,GraphKuwAnisoLoad(center+int2(x,y)));
    }
    return GraphKuwAnisoResult(state);
}
