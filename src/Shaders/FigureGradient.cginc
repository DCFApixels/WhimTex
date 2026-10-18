#ifndef WHIMTEX_FIGURE_GRADIENT_INCLUDED
#define WHIMTEX_FIGURE_GRADIENT_INCLUDED
float FigureGradientCoordinate(float2 uv, float2 size, float4 anchors, int mode, float repetitions)
{
    float2 axis = (anchors.zw - anchors.xy) * size;
    float squared = dot(axis, axis);
    if (squared <= 1e-20) return 0;
    float2 delta = (uv - anchors.xy) * size;
    float2 p = float2(dot(delta, axis), delta.y * axis.x - delta.x * axis.y) / squared;
    if (mode == 2) return p.x;
    if (mode == 3) return length(delta) / sqrt(squared);
    if (mode == 4) return max(abs(p.x), abs(p.y));
    if (mode == 5) return abs(p.x) + abs(p.y);
    return all(p == 0) ? 0 : frac(atan2(p.y, p.x) / (2 * UNITY_PI)) * repetitions;
}
#endif
