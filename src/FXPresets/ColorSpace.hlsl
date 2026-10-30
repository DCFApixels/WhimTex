// @whimtex-effect Color/Color Space
// @preset("595be499113e4616b9961efc20abc6d3", 1)
// @group("Color")
// @icon("../Editor/Icons/FX/Color.svg")
// @param enum _InputSpace = Gamma {Linear: 0, Gamma: 1, HSV: 2} // Match the source output setting in Noise or Normal Map. Linear carries raw RGB data; Gamma carries data prepared for sRGB output. HSV stores H/S/V in R/G/B, based on Linear RGB.
// @param enum _OutputSpace = Linear {Linear: 0, Gamma: 1, HSV: 2} // Match the result of choosing this output on the generator. Alpha is unchanged; matching spaces pass through. HSV preserves HDR Value but does not represent negative RGB.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float3 ColorSpaceRgbToHsv(float3 rgb)
{
    rgb = max(rgb, 0.0);
    float value = max(rgb.r, max(rgb.g, rgb.b));
    float chroma = value - min(rgb.r, min(rgb.g, rgb.b));
    float hue = 0.0;
    if (chroma > 0.0)
    {
        if (value == rgb.r) hue = (rgb.g - rgb.b) / chroma;
        else if (value == rgb.g) hue = 2.0 + (rgb.b - rgb.r) / chroma;
        else hue = 4.0 + (rgb.r - rgb.g) / chroma;
        hue = frac(hue / 6.0);
    }
    return float3(hue, value > 0.0 ? chroma / value : 0.0, value);
}

float3 ColorSpaceHsvToRgb(float3 hsv)
{
    float3 hue = saturate(abs(frac(hsv.x + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return max(hsv.z, 0.0) * lerp(1.0, hue, saturate(hsv.y));
}

float4 ApplyFX(float4 color)
{
    if (_InputSpace == _OutputSpace) return color;

    // Recover the generator's raw Linear data, then prepare the requested output.
    // Gamma generators decode their values into the linear working buffer; undo that first.
    float3 rgb = color.rgb;
    if (_InputSpace > 0.5 && _InputSpace < 1.5) rgb = SpriteEncode(rgb);
    else if (_InputSpace > 1.5) rgb = ColorSpaceHsvToRgb(rgb);

    if (_OutputSpace > 0.5 && _OutputSpace < 1.5) rgb = SpriteDecode(rgb);
    else if (_OutputSpace > 1.5) rgb = ColorSpaceRgbToHsv(rgb);
    return float4(rgb, color.a);
}
