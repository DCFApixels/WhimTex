// @whimtex-effect Distortion/Twirl
// @param float _Angle = 180 [~-720 .. ~720]
// @param transform2D _Area

float4 ApplyFX(float2 uv, float4 color)
{
    if (_Angle == 0) return color;

    float2 p = (_Area_ToLocal(uv) - 0.5) * 2.0;
    // Angle is the visible rotation at local radius 1; it continues beyond it.
    float angle = -radians(_Angle) * length(p);
    float sine, cosine;
    sincos(angle, sine, cosine);
    float2 rotated = float2(cosine * p.x - sine * p.y, sine * p.x + cosine * p.y);
    return SampleInput(_Area_ToInput(0.5 + rotated * 0.5));
}
