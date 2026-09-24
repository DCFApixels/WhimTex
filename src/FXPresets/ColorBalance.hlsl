// @whimtex-effect Color/Color Balance
// @group(Shadow Range; _ShadowRange)
// @param float _ShadowRange = 0.5 [0 .. 1] // Shadow influence range; zero disables it.
// @if _ShadowRange != 0
// @param float3 _Shadows = (0, 0, 0) // Signed RGB offsets for shadows.
// @endif
// @endgroup
// @header(Midtones)
// @param float3 _Midtones = (0, 0, 0) // Signed RGB offsets for midtones.
// @group(Highlight Range; _HighlightRange)
// @param float _HighlightRange = 0.5 [0 .. 1] // Highlight influence range; zero disables it.
// @if _HighlightRange != 0
// @param float3 _Highlights = (0, 0, 0) // Signed RGB offsets for highlights.
// @endif
// @endgroup
// @header(Luminance)
// @param bool _PreserveLuma = true // Preserve luminance without amplifying near-zero or negative colors.

float4 ApplyFX(float2 uv, float4 color)
{
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
    color.rgb = min(max(result, 0.0), 65504.0);
    return color;
}
