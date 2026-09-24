// @whimtex-effect Color/Mask
// @group(Mask Channel; _MaskChannel)
// @param hidden enum _MaskChannel = R {Alpha: 0, Luminance: 1, R: 2, G: 3, B: 4}
// @param texture2D _Mask = self // Mask source. Self uses the image before this FX.
// @param transform2D _Transform = (0.5, 0.5, 1, 1, 0) // Position, scale and rotate the mask.
// @endgroup
// @group(Mask Shape)
// @param label(Invert) bool _Invert = false
// @param curve _Profile // Remap mask values; linear preserves the source.
// @endgroup
// @group(Apply To; _ApplyMode)
// @param hidden enum _ApplyMode = Channels {Channels: 0, Color: 1}
// @if _ApplyMode == 0
// @param label(R) bool _ApplyRed = false
// @param label(G) bool _ApplyGreen = false
// @param label(B) bool _ApplyBlue = false
// @param label(A) bool _ApplyAlpha = true
// @endif
// @if _ApplyMode == 1
// @param label(Color) color _ApplyColor = (1, 1, 1, 0)
// @endif
// @param label(Amount) float _Amount = 1 [0 .. 1] // Blend the mask into selected channels or the color.
// @endgroup

float ReadMaskInput(float4 value)
{
    if (_MaskChannel < 0.5) return value.a;
    if (_MaskChannel < 1.5) return dot(value.rgb, float3(0.2126, 0.7152, 0.0722));
    if (_MaskChannel < 2.5) return value.r;
    if (_MaskChannel < 3.5) return value.g;
    return value.b;
}

float4 ApplyFX(float2 uv, float4 color)
{
    float2 maskUV = _Transform_ToLocal(uv);
    float2 halfTexel = 0.5 * _Mask_TexelSize.xy;
    maskUV = clamp(maskUV, halfTexel, 1.0 - halfTexel);

    float mask = saturate(_Profile_Sample(saturate(ReadMaskInput(tex2D(_Mask, maskUV)))));
    if (_Invert > 0.5) mask = 1.0 - mask;
    float multiplier = lerp(1.0, mask, saturate(_Amount));
    float4 channelMask = float4(_ApplyRed, _ApplyGreen, _ApplyBlue, _ApplyAlpha);
    float4 applyWeights = lerp(channelMask, 1 - _ApplyColor, _ApplyMode);
    color *= lerp(float4(1.0, 1.0, 1.0, 1.0), float4(multiplier, multiplier, multiplier, multiplier), applyWeights);
    return color;
}
