// @whimtex-effect Normal Map/Normalize
// @preset("c1cd81e1dcb14c5a8ca6eb3d0ec94065", 1)
// @group("Normal")
// @icon("../Editor/Icons/FX/Normal.svg")
// @param enum _Encoding = Gamma {Linear: 0, Gamma: 1} // Input and output encoding. Gamma interprets channels as sRGB values; Linear uses raw values.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float4 ApplyFX(float4 color)
{
    float3 channels = _Encoding > 0.5 ? SpriteEncode(color.rgb) : color.rgb;
    float3 normal = channels * 2.0 - 1.0;
    float3 magnitude = abs(normal);
    float largest = max(magnitude.x, max(magnitude.y, magnitude.z));
    if (largest <= 1e-6)
        normal = float3(0.0, 0.0, 1.0);
    else
    {
        normal /= largest;
        normal *= rsqrt(dot(normal, normal));
    }
    channels = normal * 0.5 + 0.5;
    return float4(_Encoding > 0.5 ? SpriteDecode(channels) : channels, color.a);
}
