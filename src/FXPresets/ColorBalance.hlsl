// @whimtex-effect Color/Color Balance
// @control(_Opacity)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend between the original and balanced colors.
// @group
// @header(Shadows)
// @param label(Range) float _ShadowRange = 0.5 [0 .. 1] // Extend the shadow correction into brighter tones; zero disables it.
// @if _ShadowRange != 0
// @param label(RGB Offset) float3 _Shadows = (0, 0, 0) // Add or subtract red, green and blue in shadows; zero leaves each channel unchanged.
// @endif
// @header(Midtones)
// @param label(RGB Offset) float3 _Midtones = (0, 0, 0) // Add or subtract red, green and blue between the shadow and highlight ranges.
// @header(Highlights)
// @param label(Range) float _HighlightRange = 0.5 [0 .. 1] // Extend the highlight correction into darker tones; zero disables it.
// @if _HighlightRange != 0
// @param label(RGB Offset) float3 _Highlights = (0, 0, 0) // Add or subtract red, green and blue in highlights; zero leaves each channel unchanged.
// @endif
// @header(Luminance)
// @param label(Preserve Luma) bool _PreserveLuma = true // Keep the original luminance while adjusting color; alpha is always unchanged.
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    if (_Opacity == 0.0) return color;
    float3 weights = float3(0.2126, 0.7152, 0.0722);
    float3 original = max(color.rgb, 0.0);
    float luma = dot(original, weights);
    float shadowW = 0.0;
    float highlightW = 0.0;
    if (_ShadowRange > 0.0) shadowW = 1.0 - smoothstep(0.0, _ShadowRange, luma);
    if (_HighlightRange > 0.0) highlightW = smoothstep(1.0 - _HighlightRange, 1.0, luma);
    float midW = max(0.0, 1.0 - shadowW - highlightW);
    float3 shift = _Shadows.xyz * shadowW + _Midtones.xyz * midW + _Highlights.xyz * highlightW;
    float3 result = original + shift;
    if (_PreserveLuma > 0.5)
    {
        result += luma - dot(result, weights);
        float lowest = min(result.r, min(result.g, result.b));
        if (lowest < 0.0)
            result = lerp(luma.xxx, result, luma / max(luma - lowest, 0.000001));
    }
    result = min(max(result, 0.0), 65504.0);
    return float4(lerp(color.rgb, result, _Opacity), color.a);
}
