// @whimtex-effect Color/Selective HSV
// @control(_Opacity)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend with the selected output. Zero preserves the original RGBA, including in mask outputs.
// @group(Master)
// @param label("Hue (°)") float _MasterHue = 0 [-180 .. 180] // Hue shift for the entire image, except where enabled ranges override it.
// @param label(Saturation) float _MasterSaturation = 1 [0 .. ~4] // Saturation multiplier for the entire image. Zero removes color; a neutral range can preserve its original colors.
// @param label(Value) float _MasterValue = 1 [0 .. ~4] // HSV brightness multiplier for the entire image, including gray pixels. HDR values remain supported.
// @endgroup
// @param float _MinSaturation = 0.05 [0 .. 1] // Exclude colors below this saturation from range selection, not Master. Gray, black and invisible pixels never select a range.
// @param float _SaturationFeather = 0.05 [0 .. 1] // Smoothly enable range overrides above Min Saturation. Rejected colors keep the Master correction.
// @param enum _Output = Result {Result: 0, AllRangesMask: 1, Range1Mask: 2, Range2Mask: 3, Range3Mask: 4, Range4Mask: 5} // Result preserves source alpha. Masks show selected coverage times source alpha, with opaque alpha at full opacity.
// @group(Range 1; _Range1Enabled)
// @param hidden label(Enabled) bool _Range1Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @if _Range1Enabled == 1
// @param label("Center (°)") float _Range1Center = 0 [0 .. 360] // Source hue at the center; 360 and 0 select the same hue.
// @param label("Width (°)") float _Range1Width = 60 [0 .. 360] // Full width of the region with full strength. 360 selects the entire hue circle.
// @param label("Feather (°)") float _Range1Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") float _Range1Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) float _Range1Saturation = 1 [0 .. ~4] // Source saturation multiplier; 1 preserves original saturation. Feather blends toward Master, not toward 1.
// @param label(Value) float _Range1Value = 1 [0 .. ~4] // Source HSV brightness multiplier; 1 preserves original brightness. Feather blends toward Master. HDR remains supported.
// @endif
// @endgroup
// @group(Range 2; _Range2Enabled)
// @param hidden label(Enabled) bool _Range2Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @if _Range2Enabled == 1
// @param label("Center (°)") float _Range2Center = 120 [0 .. 360] // Source hue at the center; 360 and 0 select the same hue.
// @param label("Width (°)") float _Range2Width = 60 [0 .. 360] // Full width of the region with full strength. 360 selects the entire hue circle.
// @param label("Feather (°)") float _Range2Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") float _Range2Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) float _Range2Saturation = 1 [0 .. ~4] // Source saturation multiplier; 1 preserves original saturation. Feather blends toward Master, not toward 1.
// @param label(Value) float _Range2Value = 1 [0 .. ~4] // Source HSV brightness multiplier; 1 preserves original brightness. Feather blends toward Master. HDR remains supported.
// @endif
// @endgroup
// @group(Range 3; _Range3Enabled)
// @param hidden label(Enabled) bool _Range3Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @if _Range3Enabled == 1
// @param label("Center (°)") float _Range3Center = 240 [0 .. 360] // Source hue at the center; 360 and 0 select the same hue.
// @param label("Width (°)") float _Range3Width = 60 [0 .. 360] // Full width of the region with full strength. 360 selects the entire hue circle.
// @param label("Feather (°)") float _Range3Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") float _Range3Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) float _Range3Saturation = 1 [0 .. ~4] // Source saturation multiplier; 1 preserves original saturation. Feather blends toward Master, not toward 1.
// @param label(Value) float _Range3Value = 1 [0 .. ~4] // Source HSV brightness multiplier; 1 preserves original brightness. Feather blends toward Master. HDR remains supported.
// @endif
// @endgroup
// @group(Range 4; _Range4Enabled)
// @param hidden label(Enabled) bool _Range4Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @if _Range4Enabled == 1
// @param label("Center (°)") float _Range4Center = 60 [0 .. 360] // Source hue at the center; 360 and 0 select the same hue.
// @param label("Width (°)") float _Range4Width = 60 [0 .. 360] // Full width of the region with full strength. 360 selects the entire hue circle.
// @param label("Feather (°)") float _Range4Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") float _Range4Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) float _Range4Saturation = 1 [0 .. ~4] // Source saturation multiplier; 1 preserves original saturation. Feather blends toward Master, not toward 1.
// @param label(Value) float _Range4Value = 1 [0 .. ~4] // Source HSV brightness multiplier; 1 preserves original brightness. Feather blends toward Master. HDR remains supported.
// @endif
// @endgroup

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float SelectiveHSVHueWeight(float hue, float enabled, float center, float width, float feather)
{
    if (enabled < 0.5) return 0.0;
    width = clamp(width, 0.0, 360.0);
    if (width >= 360.0) return 1.0;
    float distance = abs(frac(hue - center / 360.0 + 0.5) - 0.5) * 360.0;
    float radius = width * 0.5;
    float falloff = min(clamp(feather, 0.0, 180.0), 180.0 - radius);
    if (falloff > 0.0) return 1.0 - smoothstep(radius, radius + falloff, distance);
    return width > 0.0 && distance <= radius ? 1.0 : 0.0;
}

float SelectiveHSVSaturationGate(float saturation)
{
    float minimum = saturate(_MinSaturation);
    float end = min(1.0, minimum + saturate(_SaturationFeather));
    if (end <= minimum) return step(minimum, saturation);
    return smoothstep(minimum, end, saturation);
}

float4 ApplyFX(float2 uv, float4 color)
{
    float amount = saturate(_Opacity);
    if (amount == 0.0) return color;

    float hue = 0.0;
    float saturation = 0.0;
    // Master uses the full source HSV, even when its hue is too weak for range selection.
    float3 rgb = SpriteEncode(max(color.rgb, 0.0));
    float value = max(rgb.r, max(rgb.g, rgb.b));
    float chroma = value - min(rgb.r, min(rgb.g, rgb.b));
    if (value > 0.0) saturation = saturate(chroma / value);
    if (chroma > 0.0)
    {
        if (value == rgb.r) hue = (rgb.g - rgb.b) / chroma;
        else if (value == rgb.g) hue = 2.0 + (rgb.b - rgb.r) / chroma;
        else hue = 4.0 + (rgb.r - rgb.g) / chroma;
        hue = frac(hue / 6.0);
    }
    float gate = 0.0;
    float4 weights = 0.0;
    if (color.a > 0.0 && value > 1e-5 && chroma > 1e-5 && saturation > 1e-5)
    {
        gate = SelectiveHSVSaturationGate(saturation);
        weights = float4(
            SelectiveHSVHueWeight(hue, _Range1Enabled, _Range1Center, _Range1Width, _Range1Feather),
            SelectiveHSVHueWeight(hue, _Range2Enabled, _Range2Center, _Range2Width, _Range2Feather),
            SelectiveHSVHueWeight(hue, _Range3Enabled, _Range3Center, _Range3Width, _Range3Feather),
            SelectiveHSVHueWeight(hue, _Range4Enabled, _Range4Center, _Range4Width, _Range4Feather));
    }

    float total = dot(weights, float4(1.0, 1.0, 1.0, 1.0));
    if (_Output >= 0.5)
    {
        float selected = saturate(total);
        if (_Output >= 1.5 && _Output < 2.5) selected = weights.x;
        else if (_Output >= 2.5 && _Output < 3.5) selected = weights.y;
        else if (_Output >= 3.5 && _Output < 4.5) selected = weights.z;
        else if (_Output >= 4.5) selected = weights.w;
        float coverage = gate * selected * color.a;
        return lerp(color, float4(coverage.xxx, 1.0), amount);
    }

    if (color.a <= 0.0) return color;
    // Range coverage replaces Master; only ranges participate in overlap normalization.
    float masterWeight = 1.0 - gate * saturate(total);
    float strength = gate / max(1.0, total);
    float hueShift = masterWeight * _MasterHue + strength * dot(weights, float4(_Range1Hue, _Range2Hue, _Range3Hue, _Range4Hue));
    float saturationOffset = masterWeight * (max(_MasterSaturation, 0.0) - 1.0) + strength * dot(weights,
        max(float4(_Range1Saturation, _Range2Saturation, _Range3Saturation, _Range4Saturation), 0.0) - 1.0);
    float valueOffset = masterWeight * (max(_MasterValue, 0.0) - 1.0) + strength * dot(weights,
        max(float4(_Range1Value, _Range2Value, _Range3Value, _Range4Value), 0.0) - 1.0);
    // Preserve signed/HDR and hidden RGB exactly whenever the aggregate correction is neutral.
    if (hueShift == 0.0 && saturationOffset == 0.0 && valueOffset == 0.0) return color;

    hue = frac(hue + hueShift / 360.0);
    saturation = saturate(saturation * max(1.0 + saturationOffset, 0.0));
    value *= max(1.0 + valueOffset, 0.0);
    float3 hueRGB = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    float3 corrected = min(SpriteDecode(value * lerp(1.0, hueRGB, saturation)), 65504.0);
    return float4(lerp(color.rgb, corrected, amount), color.a);
}
