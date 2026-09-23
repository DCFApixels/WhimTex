// @whimtex-effect Color/Negative
// @param float _Amount = 1 [0 .. 1] // Blend between the source color and its negative.
// @param bool _InvertAlpha = false // Also invert alpha instead of preserving it.

float4 ApplyFX(float2 uv, float4 color)
{
    float amount = saturate(_Amount);
    color.rgb = lerp(color.rgb, 1.0 - color.rgb, amount);
    if (_InvertAlpha > 0.5)
        color.a = lerp(color.a, 1.0 - color.a, amount);
    return color;
}
