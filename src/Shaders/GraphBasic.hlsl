float4 GraphRangeMask(float4 value, float center, float width, float feather, int falloff)
{
    if (width <= 0) return 0;
    float radius = width * 0.5;
    float edge = clamp(feather, 0, radius);
    float4 distance = abs(value - center);
    float4 mask = saturate((radius - distance) / (edge > 0 ? edge : 1));
    if (falloff == 1) mask = mask * mask * (3 - 2 * mask);
    return edge > 0 ? mask : step(distance, radius);
}
float2 GraphTransform(float2 uv, float2 offset, float2 scale, float rotation, float2 center)
{
    float s, c; sincos(radians(rotation), s, c); float2 p = (uv-center)*scale;
    return float2(c*p.x-s*p.y,s*p.x+c*p.y)+center+offset;
}
