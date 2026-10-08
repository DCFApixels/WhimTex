// @whimtex-effect Distortion/Radial Shear
// @control(_Strength)
// @group
// @param float _Strength = 1 [~-4 .. ~4] // Shear amount. Zero removes the shear but keeps Offset; the sign reverses direction.
// @if _Strength != 0
// @param point _Center = (0.5, 0.5)
// @endif
// @param float2 _Offset = (0, 0)
// @param enum _Tiling = Clamp {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3} // Sampling outside the input image: extend its edge, repeat, mirror, or return transparency.
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    float2 delta = uv - _Center;
    float delta2 = dot(delta, delta);
    float2 shear = float2(delta.y, -delta.x) * (delta2 * _Strength);
    return SampleInput(uv + shear + _Offset, _Tiling);
}
