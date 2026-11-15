#ifndef WT_GRAPH_ARTIST_INCLUDED
#define WT_GRAPH_ARTIST_INCLUDED

// Formulas adapted from com.unity.shadergraph 17.7.0.
// Copyright (c) 2020 Unity Technologies ApS. Unity Companion License:
// https://unity.com/legal/licenses/unity-companion-license
// See ThirdPartyNotices.md. UnityCG.cginc must precede this module.
float GraphFadeTransition(float noiseValue,float fadeValue,float fadeContrast)
{
    return saturate(fadeValue*(fadeContrast+1)+(noiseValue-1)*fadeContrast);
}
float4 GraphReplaceColor(float4 color,float3 from,float3 to,float range,float fuzziness)
{
    float blend=saturate((distance(from,color.rgb)-range)/max(fuzziness,1e-5));
    return float4(lerp(to,color.rgb,blend),color.a);
}
float4 GraphContrast(float4 color,float contrast)
{
    float midpoint=pow(0.5,2.2);
    return float4((color.rgb-midpoint)*contrast+midpoint,color.a);
}
float3 GraphNormalReconstructZ(float2 xy)
{
    return normalize(float3(xy,sqrt(1-saturate(dot(xy,xy)))));
}
float3 GraphNormalUnpack(float4 packed,int mode)
{
    if(mode==1) return packed.xyz*2-1;
    return UnpackNormal(packed);
}
float2 GraphRadialShear(float2 uv,float2 center,float2 strength,float2 offset)
{
    float2 delta=uv-center;
    return uv+float2(delta.y,-delta.x)*dot(delta,delta)*strength+offset;
}

#endif
