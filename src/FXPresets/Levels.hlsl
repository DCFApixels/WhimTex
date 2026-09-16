// @whimtex-effect Color/Levels
// @param float _InBlack = 0 [0 .. 1] // Input black point. At or above white, the mapping becomes a hard threshold.
// @param float _InWhite = 1 [0 .. 1] // Input white point.
// @param float _Gamma = 1 [0.1 .. 5] // Midtone brightness.
// @param float _OutBlack = 0 [0 .. 1] // Output black point, including originally black pixels.
// @param float _OutWhite = 1 [0 .. 1] // Output white point; values below black invert the output.
// @param bool _PreserveColor = true // On adjusts luminance; off applies levels independently to RGB.

float3 MapLevels(float3 value)
{
    float3 mapped;
    if (_InWhite <= _InBlack) mapped = step(_InBlack, value);
    else mapped = saturate((value - _InBlack) / (_InWhite - _InBlack));
    mapped = pow(mapped, 1.0 / max(_Gamma, 0.0001));
    return lerp(_OutBlack, _OutWhite, mapped);
}

float4 ApplyFX(float2 uv, float4 color)
{
    float3 rgb = max(color.rgb, 0.0);
    if (_PreserveColor > 0.5)
    {
        float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
        float mapped = MapLevels(luma.xxx).x;
        color.rgb = luma > 0.000001 ? rgb * (mapped / max(luma, 0.000001)) : mapped.xxx;
    }
    else color.rgb = MapLevels(rgb);
    return color;
}
