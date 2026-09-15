// @whimtex-effect Normal Map/Normalize
// @param bool _PackedColor = true

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float3 channels = _PackedColor > 0.5 ? SpriteEncode(color.rgb) : color.rgb;
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
    return float4(_PackedColor > 0.5 ? SpriteDecode(channels) : channels, color.a);
}
