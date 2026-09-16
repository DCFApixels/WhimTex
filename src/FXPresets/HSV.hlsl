// @whimtex-effect Color/HSV
// @param float _Hue = 0 [-180 .. 180] // Hue shift in degrees; 0 leaves the hue unchanged.
// @param float _Saturation = 1 [0 .. ~4] // Saturation multiplier; 0 removes color, 1 is unchanged.
// @param float _Value = 1 [0 .. ~4] // HSV brightness multiplier; 1 is unchanged. HDR values remain supported.
// @param float _Amount = 1 [0 .. 1] // Blend between the original and corrected colors.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float amount = saturate(_Amount);
    if (amount == 0.0 || (_Hue == 0.0 && _Saturation == 1.0 && _Value == 1.0))
        return color;

    float3 rgb = SpriteEncode(max(color.rgb, 0.0));
    float v = max(rgb.r, max(rgb.g, rgb.b));
    float delta = v - min(rgb.r, min(rgb.g, rgb.b));
    float h = 0.0;
    if (delta > 0.0)
    {
        if (v == rgb.r) h = (rgb.g - rgb.b) / delta;
        else if (v == rgb.g) h = 2.0 + (rgb.b - rgb.r) / delta;
        else h = 4.0 + (rgb.r - rgb.g) / delta;
    }
    h = frac(h / 6.0 + _Hue / 360.0);
    float s = v > 0.0 ? saturate(delta / v * max(_Saturation, 0.0)) : 0.0;
    v *= max(_Value, 0.0);
    float3 hue = saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    float3 corrected = min(SpriteDecode(v * lerp(1.0, hue, s)), 65504.0);
    return float4(lerp(color.rgb, corrected, amount), color.a);
}
