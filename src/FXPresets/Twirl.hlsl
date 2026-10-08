// @whimtex-effect Distortion/Twirl
// @control(_Angle)
// @group
// @param float _Angle = 180 [~-720 .. ~720] // Twist angle in degrees at local radius 1. Zero removes the twist; the sign reverses direction.
// @param transform2D _Area
// @param enum _Tiling = Clamp {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3} // Sampling outside the input image: extend its edge, repeat, mirror, or return transparency.
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    if (_Angle == 0) return color;

    float2 p = (_Area_ToLocal(uv) - 0.5) * 2.0;
    // Angle is the visible rotation at local radius 1; it continues beyond it.
    float angle = -radians(_Angle) * length(p);
    float sine, cosine;
    sincos(angle, sine, cosine);
    float2 rotated = float2(cosine * p.x - sine * p.y, sine * p.x + cosine * p.y);
    return SampleInput(_Area_ToInput(0.5 + rotated * 0.5), _Tiling);
}
