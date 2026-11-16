Shader "Hidden/WhimTex/TiledPaintCommit"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "TiledPaintComposite.hlsl"
            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            { return float4(id == 2 ? 3 : -1, id == 1 ? 3 : -1, 0, 1); }
            float4 Frag(float4 position : SV_POSITION) : SV_Target
            { return TiledComposite((uint2)position.xy); }
            ENDHLSL
        }
    }
}
