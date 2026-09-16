// @whimtex-effect Color/Threshold
// @param float _Threshold = 0.25 [0 .. 1] // Threshold in the source channel.
// @param float _Smooth = 0.0 [0 .. 0.2] // Transition width; zero gives a hard threshold.
// @param color _LowColor = (0, 0, 0, 1) // RGB below the threshold; color alpha is ignored.
// @param color _HighColor = (1, 1, 1, 1) // RGB above the threshold; color alpha is ignored.
// @param bool _UseAlpha = false // Test source alpha instead of luminance; output alpha is preserved.

float4 ApplyFX(float2 uv, float4 color)
{
    float v = (_UseAlpha > 0.5) ? color.a : dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
    float width = max(_Smooth, 0.0);
    float t;
    if (width <= 0.0) t = step(_Threshold, v);
    else t = smoothstep(_Threshold - width, _Threshold + width, v);
    color.rgb = lerp(_LowColor.rgb, _HighColor.rgb, t);
    return color;
}
