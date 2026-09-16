#ifndef WHIMTEX_PROCEDURAL_UV
#define WHIMTEX_PROCEDURAL_UV
int _UnboundedUv;
float3 _UvRow0, _UvRow1, _UvRow2;
float2 ProceduralSourceUv(float2 uv)
{
    if (_UnboundedUv == 0) return uv;
    float3 p = float3(uv, 1);
    float w = dot(_UvRow2, p);
    clip(w - 1e-8);
    return float2(dot(_UvRow0, p), dot(_UvRow1, p)) / w;
}
#endif
