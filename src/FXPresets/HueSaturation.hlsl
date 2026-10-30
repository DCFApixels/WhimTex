// @whimtex-effect Color/Hue & Saturation
// @preset("7d65518083fa4efd924f9770bc3e2ade", 1)
// @control(_Opacity)
// @group("Color")
// @icon("../Editor/Icons/FX/Color.svg")
// @param hidden float _Opacity = 1 [0 .. 1] // Blend with the selected output. Zero preserves the original RGBA, including in mask outputs.
// @tabs
// @tab(Master)
// @param label("Hue (°)") preview(PreviewMasterCorrection, _MasterHue, _MasterSaturation, _MasterLightness) float _MasterHue = 0 [-180 .. 180] // Hue shift for the entire image, except where enabled ranges override it.
// @param label(Saturation) preview(PreviewMasterCorrection, _MasterHue, _MasterSaturation, _MasterLightness) float _MasterSaturation = 1 [0 .. ~4] // Scale color distance from weighted gray: 0 removes color, 1 is unchanged, above 1 boosts saturation.
// @param label(Lightness) preview(PreviewMasterCorrection, _MasterHue, _MasterSaturation, _MasterLightness) float _MasterLightness = 0 [-100 .. 100] // Lightness adjustment: -100 is black, 0 unchanged, +100 white. This is not a brightness multiplier.
// @endtab
// @tab(Range 1; _Range1Enabled)
// @param label(Enabled) toggle(float) _Range1Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @enableif _Range1Enabled == 1
// @param label("Hue Range (°)") preview(PreviewHueSelection, _Range1, _Range1Feather) float2 _Range1 = (330, 30) [0 % 360] // Cyclic hue interval. X > Y crosses red; (0, 360) selects all hues. Equal endpoints have zero width.
// @param label("Feather (°)") float _Range1Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") preview(PreviewRangeCorrection, _Range1, _Range1Hue, _Range1Saturation, _Range1Lightness) float _Range1Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) preview(PreviewRangeCorrection, _Range1, _Range1Hue, _Range1Saturation, _Range1Lightness) float _Range1Saturation = 1 [0 .. ~4] // Scale source color distance from weighted gray; 1 is unchanged. Feather blends toward Master.
// @param label(Lightness) preview(PreviewRangeCorrection, _Range1, _Range1Hue, _Range1Saturation, _Range1Lightness) float _Range1Lightness = 0 [-100 .. 100] // Lightness adjustment from the source: -100 black, 0 unchanged, +100 white. Feather blends toward Master.
// @endenableif
// @endtab
// @tab(Range 2; _Range2Enabled)
// @param label(Enabled) toggle(float) _Range2Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @enableif _Range2Enabled == 1
// @param label("Hue Range (°)") preview(PreviewHueSelection, _Range2, _Range2Feather) float2 _Range2 = (90, 150) [0 % 360] // Cyclic hue interval. X > Y crosses red; (0, 360) selects all hues. Equal endpoints have zero width.
// @param label("Feather (°)") float _Range2Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") preview(PreviewRangeCorrection, _Range2, _Range2Hue, _Range2Saturation, _Range2Lightness) float _Range2Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) preview(PreviewRangeCorrection, _Range2, _Range2Hue, _Range2Saturation, _Range2Lightness) float _Range2Saturation = 1 [0 .. ~4] // Scale source color distance from weighted gray; 1 is unchanged. Feather blends toward Master.
// @param label(Lightness) preview(PreviewRangeCorrection, _Range2, _Range2Hue, _Range2Saturation, _Range2Lightness) float _Range2Lightness = 0 [-100 .. 100] // Lightness adjustment from the source: -100 black, 0 unchanged, +100 white. Feather blends toward Master.
// @endenableif
// @endtab
// @tab(Range 3; _Range3Enabled)
// @param label(Enabled) toggle(float) _Range3Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @enableif _Range3Enabled == 1
// @param label("Hue Range (°)") preview(PreviewHueSelection, _Range3, _Range3Feather) float2 _Range3 = (210, 270) [0 % 360] // Cyclic hue interval. X > Y crosses red; (0, 360) selects all hues. Equal endpoints have zero width.
// @param label("Feather (°)") float _Range3Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") preview(PreviewRangeCorrection, _Range3, _Range3Hue, _Range3Saturation, _Range3Lightness) float _Range3Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) preview(PreviewRangeCorrection, _Range3, _Range3Hue, _Range3Saturation, _Range3Lightness) float _Range3Saturation = 1 [0 .. ~4] // Scale source color distance from weighted gray; 1 is unchanged. Feather blends toward Master.
// @param label(Lightness) preview(PreviewRangeCorrection, _Range3, _Range3Hue, _Range3Saturation, _Range3Lightness) float _Range3Lightness = 0 [-100 .. 100] // Lightness adjustment from the source: -100 black, 0 unchanged, +100 white. Feather blends toward Master.
// @endenableif
// @endtab
// @tab(Range 4; _Range4Enabled)
// @param label(Enabled) toggle(float) _Range4Enabled = false // Override Master inside this range and include it in masks. Neutral settings preserve original colors; overlapping ranges are averaged.
// @enableif _Range4Enabled == 1
// @param label("Hue Range (°)") preview(PreviewHueSelection, _Range4, _Range4Feather) float2 _Range4 = (30, 90) [0 % 360] // Cyclic hue interval. X > Y crosses red; (0, 360) selects all hues. Equal endpoints have zero width.
// @param label("Feather (°)") float _Range4Feather = 15 [0 .. 180] // Extra smooth falloff on each side, limited at the opposite hue. Zero width and feather select nothing.
// @param label("Hue (°)") preview(PreviewRangeCorrection, _Range4, _Range4Hue, _Range4Saturation, _Range4Lightness) float _Range4Hue = 0 [-180 .. 180] // Shift from source hue, not from Master. Feather blends toward Master; signed shifts are averaged in overlaps.
// @param label(Saturation) preview(PreviewRangeCorrection, _Range4, _Range4Hue, _Range4Saturation, _Range4Lightness) float _Range4Saturation = 1 [0 .. ~4] // Scale source color distance from weighted gray; 1 is unchanged. Feather blends toward Master.
// @param label(Lightness) preview(PreviewRangeCorrection, _Range4, _Range4Hue, _Range4Saturation, _Range4Lightness) float _Range4Lightness = 0 [-100 .. 100] // Lightness adjustment from the source: -100 black, 0 unchanged, +100 white. Feather blends toward Master.
// @endenableif
// @endtab
// @endtabs
// @param label("Source Saturation (HSV)") preview(PreviewSaturationSelection) float2 _SaturationRange = (0.05, 0.1) [0 .. 1] // Selection uses original HSV saturation, not the correction multiplier. X excludes weaker saturation; Y reaches full range strength. Rejected colors keep Master. Equal endpoints give a hard threshold.
// @param enum _Output = Result {Result: 0, AllRangesMask: 1, Range1Mask: 2, Range2Mask: 3, Range3Mask: 4, Range4Mask: 5} // Result preserves source alpha. Masks show selected coverage times source alpha, with opaque alpha at full opacity.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float HueSaturationRangeWidth(float2 range)
{
    float width = range.y - range.x;
    if (width > 0.0 && abs(width - 360.0) <= max(360.0, max(abs(range.x), abs(range.y))) / 16777216.0) return 360.0;
    return width < 0.0 ? width + 360.0 : width;
}

float HueSaturationRangeCenter(float2 range)
{
    return range.x + HueSaturationRangeWidth(range) * 0.5;
}

float HueSaturationHueWeight(float hue, float enabled, float2 range, float feather)
{
    if (enabled < 0.5) return 0.0;
    float center = HueSaturationRangeCenter(range);
    float width = HueSaturationRangeWidth(range);
    if (width >= 360.0) return 1.0;
    float distance = abs(frac(hue - center / 360.0 + 0.5) - 0.5) * 360.0;
    float radius = width * 0.5;
    float falloff = min(clamp(feather, 0.0, 180.0), 180.0 - radius);
    if (falloff > 0.0) return 1.0 - smoothstep(radius, radius + falloff, distance);
    return width > 0.0 && distance <= radius ? 1.0 : 0.0;
}

float HueSaturationSaturationGate(float saturation)
{
    float minimum = saturate(_SaturationRange.x);
    float end = saturate(_SaturationRange.y);
    if (end <= minimum) return step(minimum, saturation);
    return smoothstep(minimum, end, saturation);
}


// The correction is evaluated in display-encoded RGB, matching the hue controls.
// Saturation scales distance from weighted gray, not the HSV S component.
float3 HueSaturationAdjust(float3 rgb, float saturation, float lightness)
{
    float gray = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    rgb = max(lerp(gray.xxx, rgb, max(saturation, 0.0)), 0.0);
    float lift = clamp(lightness * 0.01, -1.0, 1.0);
    rgb = lift < 0.0 ? rgb * (1.0 + lift) : lerp(rgb, 1.0, lift);
    return min(SpriteDecode(rgb), 65504.0);
}

float3 HueSaturationShift(float3 rgb, float hueShift)
{
    if (hueShift == 0.0) return rgb;
    float value = max(rgb.r, max(rgb.g, rgb.b));
    float chroma = value - min(rgb.r, min(rgb.g, rgb.b));
    float hue = 0.0;
    if (chroma > 0.0)
    {
        if (value == rgb.r) hue = (rgb.g - rgb.b) / chroma;
        else if (value == rgb.g) hue = 2.0 + (rgb.b - rgb.r) / chroma;
        else hue = 4.0 + (rgb.r - rgb.g) / chroma;
    }
    hue = frac(hue / 6.0 + hueShift / 360.0);
    float saturation = value > 0.0 ? saturate(chroma / value) : 0.0;
    float3 hueRGB = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return value * lerp(1.0, hueRGB, saturation);
}

#ifdef WHIMTEX_PREVIEW
float4 PreviewCorrectionReference(float sourceHue, float hueShift, float saturation, float lightness)
{
    float3 hue = WhimTexHue((sourceHue + hueShift) / 360.0);
    return float4(HueSaturationAdjust(hue, saturation, lightness), 1.0);
}
float4 PreviewMasterCorrection(float2 uv, float hue, float saturation, float lightness)
{
    return PreviewCorrectionReference(0, hue, saturation, lightness);
}
float4 PreviewRangeCorrection(float2 uv, float2 range, float hue, float saturation, float lightness)
{
    return PreviewCorrectionReference(HueSaturationRangeCenter(range), hue, saturation, lightness);
}

float4 PreviewSaturationSelection(float2 uv)
{
    float weight = HueSaturationSaturationGate(uv.x) * step(1e-5, uv.x);
    float3 source = SpriteDecode(lerp(1.0, WhimTexHue(0), uv.x));
    return float4(lerp(weight.xxx, source * lerp(0.22, 1.0, weight), step(0.3, uv.y)), 1);
}
float4 PreviewHueSelection(float2 uv, float2 range, float feather)
{
    float hue = uv.x;
    float weight = HueSaturationHueWeight(hue, 1, range, feather);
    float3 spectrum = SpriteDecode(WhimTexHue(hue)) * lerp(0.22, 1.0, weight);
    return float4(lerp(weight.xxx, spectrum, step(0.3, uv.y)), 1);
}

#endif

float4 HueSaturationApplyMaster(float4 color, float amount)
{
    if (color.a <= 0.0 || (_MasterHue == 0.0 && _MasterSaturation == 1.0 && _MasterLightness == 0.0))
        return color;

    float3 rgb = SpriteEncode(max(color.rgb, 0.0));
    rgb = HueSaturationShift(rgb, _MasterHue);
    float3 corrected = HueSaturationAdjust(rgb, _MasterSaturation, _MasterLightness);
    return float4(lerp(color.rgb, corrected, amount), color.a);
}

float4 ApplyFX(float4 color)
{
    float amount = saturate(_Opacity);
    if (amount == 0.0) return color;

    [branch] if (_Range1Enabled < 0.5 && _Range2Enabled < 0.5 && _Range3Enabled < 0.5 && _Range4Enabled < 0.5)
    {
        if (_Output >= 0.5) return lerp(color, float4(0.0, 0.0, 0.0, 1.0), amount);
        return HueSaturationApplyMaster(color, amount);
    }

    float hue = 0.0;
    float saturation = 0.0;
    // Range selection always uses the source HSV, before any correction.
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
        gate = HueSaturationSaturationGate(saturation);
        weights = float4(
            HueSaturationHueWeight(hue, _Range1Enabled, _Range1, _Range1Feather),
            HueSaturationHueWeight(hue, _Range2Enabled, _Range2, _Range2Feather),
            HueSaturationHueWeight(hue, _Range3Enabled, _Range3, _Range3Feather),
            HueSaturationHueWeight(hue, _Range4Enabled, _Range4, _Range4Feather));
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
    float lightness = masterWeight * clamp(_MasterLightness, -100.0, 100.0) + strength * dot(weights,
        clamp(float4(_Range1Lightness, _Range2Lightness, _Range3Lightness, _Range4Lightness), -100.0, 100.0));
    // Preserve signed/HDR and hidden RGB exactly whenever the aggregate correction is neutral.
    if (hueShift == 0.0 && saturationOffset == 0.0 && lightness == 0.0) return color;

    rgb = HueSaturationShift(rgb, hueShift);
    float3 corrected = HueSaturationAdjust(rgb, 1.0 + saturationOffset, lightness);
    return float4(lerp(color.rgb, corrected, amount), color.a);
}
