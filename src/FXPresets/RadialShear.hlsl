// @whimtex-effect Distortion/Radial Shear
// @group(Shear Strength; _Strength)
// @param hidden float _Strength = 1 [~-4 .. ~4]
// @if _Strength != 0
// @param point _Center = (0.5, 0.5)
// @endif
// @param float2 _Offset = (0, 0)
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    float2 delta = uv - _Center;
    float delta2 = dot(delta, delta);
    float2 shear = float2(delta.y, -delta.x) * (delta2 * _Strength);
    return SampleInput(uv + shear + _Offset);
}
