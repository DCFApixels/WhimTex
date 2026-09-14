#ifndef WHIMTEX_COLOR_BLEND_INCLUDED
#define WHIMTEX_COLOR_BLEND_INCLUDED

float3 bound(float3 c) { return _HdrBlend > 0.5 ? c : saturate(c); }

float3 ToSrgbBlendSpace(float3 c)
{
    float3 magnitude = abs(c);
    float3 encoded = lerp(magnitude * 12.92,
        1.055 * pow(magnitude, 1.0 / 2.4) - 0.055, step(0.0031308, magnitude));
    return sign(c) * encoded;
}

float3 FromSrgbBlendSpace(float3 c)
{
    float3 magnitude = abs(c);
    float3 decoded = lerp(magnitude / 12.92,
        pow((magnitude + 0.055) / 1.055, 2.4), step(0.04045, magnitude));
    return sign(c) * decoded;
}

float3 BlendColorDodge(float3 backdrop, float3 source)
{
    if (_HdrBlend > 0.5) return backdrop / (1.0 - source);
    const float epsilon = 0.000001;
    float3 result = saturate(backdrop / max(1.0 - source, epsilon));
    result = lerp(result, 1.0, step(1.0 - epsilon, source));
    result = lerp(result, 0.0, 1.0 - step(epsilon, backdrop));
    return result;
}

float3 BlendColorBurn(float3 backdrop, float3 source)
{
    if (_HdrBlend > 0.5) return 1.0 - (1.0 - backdrop) / source;
    const float epsilon = 0.000001;
    float3 result = 1.0 - saturate((1.0 - backdrop) / max(source, epsilon));
    result = lerp(result, 0.0, 1.0 - step(epsilon, source));
    result = lerp(result, 1.0, step(1.0 - epsilon, backdrop));
    return result;
}

float3 BlendSoftLight(float3 backdrop, float3 source)
{
    float3 polynomial = ((16.0 * backdrop - 12.0) * backdrop + 4.0) * backdrop;
    float3 d = lerp(polynomial, sqrt(max(backdrop, 0.0)), step(0.25, backdrop));
    float3 dark = backdrop - (1.0 - 2.0 * source) * backdrop * (1.0 - backdrop);
    float3 light = backdrop + (2.0 * source - 1.0) * (d - backdrop);
    return lerp(dark, light, step(0.5, source));
}

float3 EvaluateBlend(float3 backdrop, float3 source, float mode)
{
    if (mode == 1.0)  return backdrop * source;
    if (mode == 4.0)  return bound(backdrop + source);
    if (mode == 5.0)  return bound(backdrop - source);
    if (mode == 6.0)  return bound(backdrop / (_HdrBlend > 0.5 ? source : max(source, 0.000001)));
    if (mode == 7.0)  return 1.0 - (1.0 - backdrop) * (1.0 - source);
    if (mode == 8.0)
    {
        float3 multiply = 2.0 * backdrop * source;
        float3 screen = 1.0 - 2.0 * (1.0 - backdrop) * (1.0 - source);
        return lerp(multiply, screen, step(0.5, backdrop));
    }
    if (mode == 9.0)  return min(backdrop, source);
    if (mode == 10.0) return max(backdrop, source);
    if (mode == 11.0) return BlendColorDodge(backdrop, source);
    if (mode == 12.0) return BlendColorBurn(backdrop, source);
    if (mode == 13.0) return bound(backdrop + source);
    if (mode == 14.0) return bound(backdrop + source - 1.0);
    if (mode == 15.0) return bound(backdrop + 2.0 * source - 1.0);
    if (mode == 16.0) return bound(source + 2.0 * backdrop - 1.0);
    if (mode == 17.0)
    {
        float3 burn = BlendColorBurn(backdrop, bound(2.0 * source));
        float3 dodge = BlendColorDodge(backdrop, bound(2.0 * source - 1.0));
        return lerp(burn, dodge, step(0.5, source));
    }
    if (mode == 18.0)
    {
        float3 dark = min(backdrop, 2.0 * source);
        float3 light = max(backdrop, 2.0 * source - 1.0);
        return lerp(dark, light, step(0.5, source));
    }
    if (mode == 19.0) return step(1.0, backdrop + source);
    if (mode == 20.0)
    {
        float3 multiply = 2.0 * backdrop * source;
        float3 screen = 1.0 - 2.0 * (1.0 - backdrop) * (1.0 - source);
        return lerp(multiply, screen, step(0.5, source));
    }
    if (mode == 21.0) return BlendSoftLight(backdrop, source);
    if (mode == 22.0) return abs(backdrop - source);
    if (mode == 23.0) return backdrop + source - 2.0 * backdrop * source;
    if (mode == 24.0) return 1.0 - abs(1.0 - backdrop - source);
    return source;
}

float4 CompositeBrushPixel(float4 before, float4 stroke, float opacity, float mode, float erase, float standard)
{
    float alpha = saturate(stroke.a * opacity);
    if (alpha <= 0.0) return before;
    if (erase > .5) return before * (1.0 - alpha);
    if (mode == 3.0) return before;
    float ba = saturate(before.a);
    float3 b = ba > .000001 ? before.rgb / ba : 0.0;
    float3 s = stroke.a > .000001 ? stroke.rgb / stroke.a : 0.0;
    if (standard > .5) { b = saturate(b); s = saturate(s); }
    float outputAlpha = alpha + ba * (1.0 - alpha);
    if (mode == 0.0 || mode == 2.0)
        return float4(s * alpha + b * ba * (1.0 - alpha), outputAlpha);
    bool hdr = _HdrBlend > .5;
    b = hdr ? b : ToSrgbBlendSpace(b);
    s = hdr ? s : ToSrgbBlendSpace(s);
    float3 blended = bound(EvaluateBlend(bound(b), bound(s), mode));
    float3 rgb = (alpha * (1.0 - ba) * s + alpha * ba * blended + (1.0 - alpha) * ba * b) / max(outputAlpha, .000001);
    return float4((hdr ? rgb : FromSrgbBlendSpace(rgb)) * outputAlpha, outputAlpha);
}

#endif
