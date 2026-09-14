#ifndef WHIMTEX_PROCEDURAL_UV
#define WHIMTEX_PROCEDURAL_UV
int _UnboundedUv;
float2 _UvCanvas, _UvPivot, _UvPosition, _UvScale;
float _UvRotation;
float2 ProceduralSourceUv(float2 uv)
{
    if (_UnboundedUv == 0) return uv;
    float2 local = (uv - _UvPivot) * _UvCanvas - _UvPosition;
    float sine = sin(-_UvRotation), cosine = cos(-_UvRotation);
    local = float2(cosine * local.x - sine * local.y, sine * local.x + cosine * local.y);
    float2 safeScale = float2(_UvScale.x < 0 ? min(_UvScale.x, -1e-5) : max(_UvScale.x, 1e-5),
        _UvScale.y < 0 ? min(_UvScale.y, -1e-5) : max(_UvScale.y, 1e-5));
    return _UvPivot + local / safeScale / _UvCanvas;
}
#endif
