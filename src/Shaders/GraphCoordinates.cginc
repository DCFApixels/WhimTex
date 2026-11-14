#ifndef WHIMTEX_GRAPH_COORDINATES_INCLUDED
#define WHIMTEX_GRAPH_COORDINATES_INCLUDED

int _GraphCanvasCoordinates, _GraphTiling, _GraphTransformUsesFxFrame;
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
float2 GraphInversePosition(float2 uv, float3 row0, float3 row1, float3 row2)
{
    float3 p = cross(row1, row2) * uv.x + cross(row2, row0) * uv.y + cross(row0, row1);
    return p.z != 0.0 ? p.xy / p.z : 0.0;
}
float2 GraphTransformToCanvas(float2 layerUV)
{
    if (_GraphTransformUsesFxFrame != 0)
        return GraphInversePosition(layerUV, _WhimTex_LayerToLocalRow0.xyz, _WhimTex_LayerToLocalRow1.xyz, _WhimTex_LayerToLocalRow2.xyz);
    if (_GraphCanvasCoordinates == 0) return layerUV;
    return GraphLocalToCanvas(_GraphRawUV + (layerUV - _GraphSourceUV) * _GraphUvDirection);
}
float2 GraphTransformToLayer(float2 canvasUV)
{
    if (_GraphTransformUsesFxFrame != 0)
    {
        float3 q = float3(canvasUV, 1);
        float w = dot(_WhimTex_LayerToLocalRow2.xyz, q);
        return float2(dot(_WhimTex_LayerToLocalRow0.xyz, q), dot(_WhimTex_LayerToLocalRow1.xyz, q)) * (abs(w) > 1e-8 ? rcp(w) : 0);
    }
    if (_GraphCanvasCoordinates == 0) return canvasUV;
    float2 rawUV = GraphInversePosition(canvasUV, _GraphCanvasRow0, _GraphCanvasRow1, _GraphCanvasRow2);
    return _GraphSourceUV + (rawUV - _GraphRawUV) * _GraphUvDirection;
}
float2 GraphTransformPosition(float2 uv, int direction, bool implicitPosition)
{
    if (implicitPosition && (direction == 1) != (_GraphTransformUsesFxFrame != 0)) return uv;
    return direction == 0 ? GraphTransformToCanvas(uv) : GraphTransformToLayer(uv);
}

#endif
