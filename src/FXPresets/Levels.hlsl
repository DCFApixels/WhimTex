// @whimtex-effect Color/Levels
// @control(_Opacity)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend between the original and corrected colors.
// @group
// @header(Input Levels)
// @param label(Input Black) float _InBlack = 0 [0 .. ~1] // Input black point. At or above white, the mapping becomes a hard threshold.
// @param label(Input White) float _InWhite = 1 [0 .. ~1] // Input white point.
// @param float _Gamma = 1 [0.1 .. ~5] // Midtone brightness.
// @header(Output Levels)
// @param label(Output Black) float _OutBlack = 0 [0 .. ~1] // Output black point, including originally black pixels.
// @param label(Output White) float _OutWhite = 1 [0 .. ~1] // Output white point; values below black invert the output.
// @param bool _PreserveColor = true // On adjusts luminance; off applies levels independently to RGB.
// @endgroup
// @group(Curves; _CurveChannel)
// @param hidden enum _CurveChannel = RGB {RGB:0,R:1,G:2,B:3,Alpha:4} // Select the curve to edit. All curves remain active.
// @if _CurveChannel == 0
// @param label(Curve) curve _Curve // Shared RGB curve after input levels and Gamma, before output levels. Preserve Color applies it to luminance.
// @endif
// @if _CurveChannel == 1
// @param label(Curve) curve _RedCurve // Red correction after shared levels. Linear is neutral, including HDR values.
// @endif
// @if _CurveChannel == 2
// @param label(Curve) curve _GreenCurve // Green correction after shared levels. Linear is neutral, including HDR values.
// @endif
// @if _CurveChannel == 3
// @param label(Curve) curve _BlueCurve // Blue correction after shared levels. Linear is neutral, including HDR values.
// @endif
// @if _CurveChannel == 4
// @param label(Curve) curve _AlphaCurve // Independent alpha correction; input/output levels and Gamma only affect RGB. Output alpha is limited to 0–1.
// @endif
// @endgroup

float MapLevels(float value)
{
    float mapped;
    if (_InWhite <= _InBlack) mapped = step(_InBlack, value);
    else mapped = saturate((value - _InBlack) / (_InWhite - _InBlack));
    mapped = pow(mapped, 1.0 / max(_Gamma, 0.0001));
    mapped = _Curve_Sample(mapped);
    return lerp(_OutBlack, _OutWhite, mapped);
}

float4 ApplyFX(float2 uv, float4 color)
{
	float4 result = color;
    float3 rgb = max(color.rgb, 0.0);
    if (_PreserveColor > 0.5)
    {
        float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
        float mapped = MapLevels(luma);
		result.rgb = luma > 0.000001 ? rgb * (mapped / max(luma, 0.000001)) : mapped.xxx;
	}
	else
	{
		result.rgb = float3(MapLevels(rgb.r), MapLevels(rgb.g), MapLevels(rgb.b));
	}
    // Add the curve's deviation from linear so neutral channel curves do not clip HDR.
    float3 curveInput = saturate(result.rgb);
    result.rgb += float3(_RedCurve_Sample(curveInput.r),
                        _GreenCurve_Sample(curveInput.g),
                        _BlueCurve_Sample(curveInput.b)) - curveInput;
    result.a = saturate(_AlphaCurve_Sample(color.a));
	return lerp(color, result, _Opacity);
}
