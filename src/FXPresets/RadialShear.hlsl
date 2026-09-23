// @whimtex-effect Distortion/Radial Shear
// @param point _Center = (0.5, 0.5)
// @param float _Strength = 1 [~-4 .. ~4]
// @param float2 _Offset = (0, 0)

float4 ApplyFX(float2 uv, float4 color)
{
    float2 delta = uv - _Center;
    float delta2 = dot(delta, delta);
    float2 shear = float2(delta.y, -delta.x) * (delta2 * _Strength);
    return SampleInput(uv + shear + _Offset);
}
