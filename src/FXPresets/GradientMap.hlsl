// @whimtex-effect Color/Gradient Map
// @control(_Opacity)
// @group
// @formerlyserializedas(_Amount)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend the mapped colors and gradient opacity with the original image.
// @param enum _SourceChannel = Luminance {Luminance: 0, R: 1, G: 2, B: 3, Alpha: 4} // Select the input used to look up the full gradient color. RGB uses sRGB values; alpha is read directly.
// @param gradient _Gradient // Colors from shadows to highlights; gradient alpha multiplies source alpha.
// @param curve _Mapping // Remap the selected input before sampling the gradient; the default is unchanged.
// @param bool _Reverse = false // Map highlights to the beginning of the gradient instead of shadows.
// @endgroup

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float luminance = saturate(dot(max(color.rgb, 0.0), float3(0.2126, 0.7152, 0.0722)));
    float brightness = SpriteEncode(float3(luminance, luminance, luminance)).r;
    if (_SourceChannel > 3.5) brightness = saturate(color.a);
    else if (_SourceChannel > .5)
    {
        float channel = _SourceChannel < 1.5 ? color.r : _SourceChannel < 2.5 ? color.g : color.b;
        brightness = SpriteEncode(saturate(channel).xxx).r;
    }
    float t = _Reverse > 0.5 ? 1.0 - brightness : brightness;
    float4 mapped = _Gradient_Sample(_Mapping_Sample(t));
    float amount = saturate(_Opacity);
    return float4(lerp(color.rgb, mapped.rgb, amount), color.a * lerp(1.0, mapped.a, amount));
}
