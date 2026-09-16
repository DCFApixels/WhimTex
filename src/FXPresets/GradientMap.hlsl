// @whimtex-effect Color/Gradient Map
// @param gradient _Gradient // Colors from shadows to highlights.
// @param float _Amount = 1 [0 .. 1] // Blend between the original colors and the gradient map.
// @param bool _Reverse = false // Map highlights to the beginning of the gradient instead of shadows.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float luminance = saturate(dot(max(color.rgb, 0.0), float3(0.2126, 0.7152, 0.0722)));
    float brightness = SpriteEncode(float3(luminance, luminance, luminance)).r;
    float t = _Reverse > 0.5 ? 1.0 - brightness : brightness;
    float4 mapped = _Gradient_Sample(t);
    return float4(lerp(color.rgb, mapped.rgb, saturate(_Amount)), color.a);
}
