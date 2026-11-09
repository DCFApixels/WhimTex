Shader "Hidden/WhimTex/Tests/HeightPreviewRaster" {
SubShader {
Pass {
 Cull Off ZWrite On ZTest LEqual
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex Vertex
 #pragma fragment Fragment
 #pragma multi_compile_local __ GRID_MESH
 #include "UnityCG.cginc"
 __COMMON__
 struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float height : TEXCOORD1; };
 #ifdef GRID_MESH
 Varyings Vertex(float4 vertex : POSITION)
 {
     float2 uv = vertex.xy;
 #else
 Varyings Vertex(uint id : SV_VertexID)
 {
     uint cell = id / 6, corner = id % 6;
     uint2 q = uint2(cell % _Steps, cell / _Steps);
     // Shared diagonal; no CPU vertex/index buffers or mesh rebuilding.
     uint2 offsets[6] = {uint2(0,0),uint2(0,1),uint2(1,1),uint2(0,0),uint2(1,1),uint2(1,0)};
     float2 uv = (q + offsets[corner]) / (float)_Steps;
 #endif
     float h = HeightAt(uv);
     float z = .5 + ((uv.y - .5) * 2 - h * _HeightScale) * .08;
     Varyings o; o.uv = uv; o.height = h;
     o.position = float4((uv.x - .5) * 1.68, ProjectY(uv.y, h) * 2 - 1, z, 1);
     // RenderTexture coordinates agree with RWTexture coordinates on every backend.
     #if UNITY_UV_STARTS_AT_TOP
     o.position.y = -o.position.y;
     #endif
     #if defined(UNITY_REVERSED_Z)
     o.position.z = 1 - o.position.z;
     #endif
     return o;
 }
 float4 Fragment(Varyings i) : SV_Target { float4 value = Surface(i.uv); value.r = i.height; return value; }
 ENDHLSL
}
}
}
