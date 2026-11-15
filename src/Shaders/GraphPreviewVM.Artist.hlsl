#ifndef WT_GRAPH_PREVIEW_VM_ARTIST_INCLUDED
#define WT_GRAPH_PREVIEW_VM_ARTIST_INCLUDED

#include "GraphArtist.hlsl"

float4 VMArtistOperation(int kind,int mode,float4 a,float4 b,float4 c,float4 d,float4 e)
{
    float4 result=0;
    switch(kind)
    {
        case 198: result=float4(GraphFadeTransition(a.x,b.x,c.x),0,0,0); break;
        case 199: result=GraphReplaceColor(a,b.rgb,c.x); break;
        case 200: result=GraphContrast(a,b.x); break;
        case 201: result=float4(GraphNormalReconstructZ(a.xy),0); break;
        case 202: result=float4(GraphNormalUnpack(a,mode),0); break;
        case 203: result=float4(GraphRadialShear(a.xy,b.xy,c.xy,d.xy),0,0); break;
    }
    return result;
}

#endif
