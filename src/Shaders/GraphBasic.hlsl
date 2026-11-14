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
float4 GraphVectorProjection(float4 a, float4 b)
{
    float scale = max(max(abs(b.x), abs(b.y)), max(abs(b.z), abs(b.w)));
    if (scale == 0.0) return 0.0;
    b /= scale;
    return b * (dot(a, b) / dot(b, b));
}
float GraphFresnelEffect(float3 normal, float3 viewDir, float power)
{
    float normalLength2 = dot(normal, normal), viewLength2 = dot(viewDir, viewDir);
    float cosine = dot(normal * rsqrt(max(normalLength2, 1e-20)), viewDir * rsqrt(max(viewLength2, 1e-20)));
    return power <= 0.0 ? 1.0 : pow(1.0 - saturate(cosine), power);
}
float GraphSphereMask(float4 coords, float4 center, float radius, float hardness)
{
    float distanceToCenter = length(coords - center);
    float width = 1.0 - saturate(hardness);
    return width > 0.0 ? 1.0 - saturate((distanceToCenter - radius) / width) : step(distanceToCenter, radius);
}
