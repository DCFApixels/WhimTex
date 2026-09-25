// @whimtex-effect Color/Brightness Contrast
// @param float _Brightness = 0 [~-100 .. ~100] // Adjust midtone brightness while retaining black and white. Soft bounds allow stronger values.
// @param float _Contrast = 0 [~-100 .. ~100] // Positive increases tonal separation; negative brings tones toward middle gray. Soft bounds allow stronger values.

// Rational tone curves avoid hard clipping and exponential overflow at extended settings.
// Work in the incoming RGB space, retaining signed/HDR values outside the tonal interval.
float AdjustTone(float value, float brightnessGain, float contrastGain)
{
    if (value <= 0.0 || value >= 1.0) return value;
    float low = value;
    float high = 1.0 - value;
    if (_Brightness >= 0.0) high /= brightnessGain;
    else low /= brightnessGain;
    // Carry the unnormalized pair into contrast: normalizing brightness first loses
    // the highlight distance to rounding, which extreme negative contrast amplifies.
    float nearEdge = 2.0 * min(low, high);
    float nearMiddle = abs(high - low);
    if (_Contrast >= 0.0) nearEdge /= contrastGain;
    else nearMiddle /= contrastGain;
    float mapped = 0.5 * (nearEdge / (nearEdge + nearMiddle));
    return low <= high ? mapped : 1.0 - mapped;
}

float4 ApplyFX(float2 uv, float4 color)
{
    if (_Brightness == 0.0 && _Contrast == 0.0) return color;
    float brightnessGain = 1.0 + abs(_Brightness) / 50.0;
    float contrastGain = 1.0 + abs(_Contrast) / 50.0;
    color.rgb = float3(AdjustTone(color.r, brightnessGain, contrastGain),
        AdjustTone(color.g, brightnessGain, contrastGain),
        AdjustTone(color.b, brightnessGain, contrastGain));
    return color;
}
