// @whimtex-effect Color/Gain
// @param float _Gain = 1 [0 .. ~4]
// @param color _Tint = (1, 1, 1, 1)

float4 ApplyFX(float2 uv, float4 color)
{
    return float4(color.rgb * _Tint.rgb * _Gain, color.a * _Tint.a);
}
