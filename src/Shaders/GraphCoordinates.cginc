#ifndef WHIMTEX_GRAPH_COORDINATES_INCLUDED
#define WHIMTEX_GRAPH_COORDINATES_INCLUDED

int _GraphCanvasCoordinates, _GraphTiling;
float3 _GraphCanvasRow0, _GraphCanvasRow1, _GraphCanvasRow2;
static float2 _GraphCanvasUV, _GraphRawUV, _GraphSourceUV, _GraphUvDirection;
float2 GraphSourceUv(float2 canvasUV)
{
    _GraphCanvasUV = canvasUV;
    _GraphRawUV = ProceduralSourceUv(canvasUV);
    _GraphSourceUV = _GraphRawUV;
    _GraphUvDirection = 1;
    if (_GraphCanvasCoordinates != 0)
    {
        if (_GraphTiling == 0) clip(min(min(_GraphRawUV.x, _GraphRawUV.y), min(1 - _GraphRawUV.x, 1 - _GraphRawUV.y)));
        else if (_GraphTiling == 1) _GraphSourceUV = frac(_GraphRawUV);
        else if (_GraphTiling == 2)
        {
            float2 phase = frac(_GraphRawUV * 0.5) * 2;
            _GraphSourceUV = 1 - abs(phase - 1);
            _GraphUvDirection = 1 - 2 * step(1, phase);
        }
        else if (_GraphTiling == 4) _GraphSourceUV = saturate(_GraphRawUV);
    }
    return _GraphSourceUV;
}
float2 GraphLocalToCanvas(float2 localUV)
{
    float3 p = float3(localUV, 1);
    float w = dot(_GraphCanvasRow2, p);
    return float2(dot(_GraphCanvasRow0, p), dot(_GraphCanvasRow1, p)) * (abs(w) > 1e-8 ? rcp(w) : 0);
}
float2 GraphCanvasPosition(float2 localUV)
{
    if (_GraphCanvasCoordinates == 0) return localUV;
    float2 offset = (localUV - _GraphSourceUV) * _GraphUvDirection;
    return _GraphCanvasUV + (GraphLocalToCanvas(_GraphRawUV + offset) - GraphLocalToCanvas(_GraphRawUV));
}

#endif
