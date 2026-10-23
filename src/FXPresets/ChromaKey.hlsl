// @whimtex-effect Color/Chroma Key
// @control(_Opacity)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend the selected RGBA output with the input. Zero preserves the input, including in Matte mode.
// @group(Key)
// @param color _KeyColor = (0, 1, 0, 1) // Background color to remove. Alpha is ignored.
// @param enum _Distance = Chroma {Chroma: 0, RGB: 1} // Chroma compares color differences with less sensitivity to brightness. Use RGB for black, white or gray backgrounds.
// @param float _Tolerance = 0.1 [0 .. 1] // Colors within this distance are removed completely.
// @param float _Softness = 0.05 [0 .. 1] // Smooth transition outside Tolerance. Zero gives a hard color threshold, not a spatial blur.
// @endgroup
// @group(Spill)
// @param float _Despill = 0 [0 .. 1] // Remove color contamination in the key direction without changing the matte. Zero preserves RGB.
// @if _Despill != 0
// @param float _SpillRange = 0.1 [0 .. 1] // Extend color similarity for spill suppression beyond the key transition. Does not change alpha.
// @endif
// @endgroup
// @header(Output)
// @param enum _Output = Result {Result: 0, Matte: 1} // Matte shows final coverage as opaque grayscale, including the input alpha.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float ChromaKeyDistance(float3 input, float3 key)
{
    // Analyze display-encoded SDR colors; retain the original linear HDR RGB.
    float3 difference = saturate(SpriteEncode(input)) - saturate(SpriteEncode(key));
    if (_Distance > 0.5)
        return length(difference) * 0.5773502692;
    float luma = dot(difference, float3(0.2126, 0.7152, 0.0722));
    float2 chroma = float2((difference.b - luma) / 1.8556, (difference.r - luma) / 1.5748);
    return length(chroma) * 0.7071067812;
}

float ChromaKeyKeep(float distance, float tolerance, float softness)
{
    if (tolerance >= 1.0) return 0.0;
    if (softness <= 0.0) return distance <= tolerance ? 0.0 : 1.0;
    return smoothstep(tolerance, tolerance + softness, distance);
}

float3 ChromaKeyDespill(float3 rgb, float distance)
{
    // Signed data is not a color-keying target; never clip it during despill.
    if (_Despill <= 0.0 || any(rgb < 0.0)) return rgb;
    float3 key = max(_KeyColor.rgb, 0.0);
    float3 direction = key - dot(key, float3(0.2126, 0.7152, 0.0722));
    float magnitudeSquared = dot(direction, direction);
    if (magnitudeSquared <= 1e-12) return rgb;
    float outer = _Tolerance + _Softness + _SpillRange;
    float weight = outer > _Tolerance
        ? 1.0 - smoothstep(_Tolerance, outer, distance)
        : (distance <= _Tolerance ? 1.0 : 0.0);
    float3 chroma = rgb - dot(rgb, float3(0.2126, 0.7152, 0.0722));
    float amount = max(0.0, dot(chroma, direction) / magnitudeSquared) * _Despill * weight;
    // Limit subtraction, not the HDR result. The zero-luma direction preserves
    // brightness and leaves neutral and unrelated colors alone.
    if (direction.r > 0.0) amount = min(amount, rgb.r / direction.r);
    if (direction.g > 0.0) amount = min(amount, rgb.g / direction.g);
    if (direction.b > 0.0) amount = min(amount, rgb.b / direction.b);
    return rgb - direction * amount;
}

float4 ApplyFX(float4 color)
{
    if (_Opacity <= 0.0) return color;
    float distance = ChromaKeyDistance(color.rgb, _KeyColor.rgb);
    float alpha = color.a * ChromaKeyKeep(distance, _Tolerance, _Softness);
    float4 result;
    if (_Output > 0.5)
        result = float4(alpha.xxx, 1.0);
    else
        result = float4(color.a > 0.0 ? ChromaKeyDespill(color.rgb, distance) : color.rgb, alpha);
    return lerp(color, result, _Opacity);
}
