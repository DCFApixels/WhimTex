#ifndef WHIMTEX_HDR_COLOR_INCLUDED
#define WHIMTEX_HDR_COLOR_INCLUDED
float3 SpriteEncode(float3 color)
{
    float3 value = abs(color);
    return sign(color) * lerp(value * 12.92, 1.055 * pow(value, 1.0 / 2.4) - .055, step(.0031308, value));
}
float3 SpriteDecode(float3 color)
{
    float3 value = abs(color);
    return sign(color) * lerp(value / 12.92, pow((value + .055) / 1.055, 2.4), step(.04045, value));
}
#endif
