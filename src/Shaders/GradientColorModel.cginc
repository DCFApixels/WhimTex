#ifndef WHIMTEX_GRADIENT_COLOR_MODEL_INCLUDED
#define WHIMTEX_GRADIENT_COLOR_MODEL_INCLUDED

float3 GradientResolveHue(float3 lab, float chroma)
{
    float magnitude = length(lab.yz);
    float agreement = magnitude / max(chroma, 1e-20);
    float t = saturate((agreement - .25) * 4);
    float blend = t*t*t * (t*(t*6-15)+10);
    lab.yz *= 1 + blend * max(0, chroma-magnitude) / max(magnitude, 1e-20);
    return lab;
}

float3 GradientModelToLinear(float3 color, int colorMode, float chroma)
{
    if (colorMode == 1)
    {
        float3 magnitude = abs(color);
        return sign(color) * lerp(magnitude / 12.92,
            pow((magnitude + .055) / 1.055, 2.4), step(.04045, magnitude));
    }
    if (colorMode == 2 || colorMode == 3)
    {
        if (colorMode == 3) color = GradientResolveHue(color, chroma);
        float3 lms = float3(color.r + .3963377774*color.g + .2158037573*color.b,
            color.r - .1055613458*color.g - .0638541728*color.b,
            color.r - .0894841775*color.g - 1.2914855480*color.b);
        lms = lms*lms*lms;
        return float3(4.0767416621*lms.x - 3.3077115913*lms.y + .2309699292*lms.z,
            -1.2684380046*lms.x + 2.6097574011*lms.y - .3413193965*lms.z,
            -.0041960863*lms.x - .7034186147*lms.y + 1.7076147010*lms.z);
    }
    return color;
}
#endif
