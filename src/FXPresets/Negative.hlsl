// @whimtex-effect Color/Negative
// @control(_Opacity)
// @formerserializedas(_Amount)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend between the original and corrected colors.
// @group
// @param bool _InvertAlpha = false // Also invert alpha instead of preserving it.
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    float amount = saturate(_Opacity);
    color.rgb = lerp(color.rgb, 1.0 - color.rgb, amount);
    float invertAlpha = step(0.5001, _InvertAlpha);
    color.a = lerp(color.a, 1.0 - color.a, amount * invertAlpha);
    return color;
}
