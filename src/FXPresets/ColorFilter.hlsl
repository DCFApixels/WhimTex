// @whimtex-effect Color/Color Filter
// @param color _FilterColor = (1.0, 0.6, 0.3, 1.0) // Multiplicative RGB tint; color alpha is ignored.
// @param float _Density = 0.3 [0 .. 1] // Blend with the tinted result; zero leaves the input unchanged.
// @param bool _PreserveLuminosity = true // Preserve luminance; a black tint falls back to the source color.

float4 ApplyFX(float2 uv, float4 color)
{
    float density = saturate(_Density);
    if (density == 0.0) return color;
    float3 original = max(color.rgb, 0.0);
    float3 filtered = original * max(_FilterColor.rgb, 0.0);
    if (_PreserveLuminosity > 0.5)
    {
        float3 weights = float3(0.2126, 0.7152, 0.0722);
        float peak = max(filtered.r, max(filtered.g, filtered.b));
        if (peak > 0.000001)
        {
            float3 chroma = filtered / peak;
            filtered = chroma * (dot(original, weights) / dot(chroma, weights));
        }
        else filtered = original;
    }
    return float4(min(lerp(original, filtered, density), 65504.0), color.a);
}
