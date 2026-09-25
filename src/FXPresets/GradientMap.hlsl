// @whimtex-effect Color/Gradient Map
// @control(_Opacity)
// @group
// @formerlyserializedas(_Amount)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend between the original colors and the gradient map.
// @param gradient _Gradient // Colors from shadows to highlights.
// @param curve _Mapping // Remap brightness before sampling the gradient; the default is unchanged.
// @param bool _Reverse = false // Map highlights to the beginning of the gradient instead of shadows.
// @endgroup

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float luminance = saturate(dot(max(color.rgb, 0.0), float3(0.2126, 0.7152, 0.0722)));
    float brightness = SpriteEncode(float3(luminance, luminance, luminance)).r;
    float t = _Reverse > 0.5 ? 1.0 - brightness : brightness;
    float4 mapped = _Gradient_Sample(_Mapping_Sample(t));
	return float4(lerp(color.rgb, mapped.rgb, saturate(_Opacity)), color.a);
}
