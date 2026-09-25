// @whimtex-effect Distortion/Polar Coordinates
// @param enum _Mode = 0 {ToPolar: 0, FromPolar: 1} // Wrap a strip into a circle, or unwrap a circle into a strip.
// @param float _AngleOffset = 0 [~-180 .. ~180] // Angular offset in degrees.
// @param float _RadialOffset = 0 [~-1 .. ~1] // Radial offset in normalized coordinates.
// @param transform2D _Area

float4 ApplyFX(float2 uv, float4 color)
{
    float2 sampleUV = uv;
    if (_Mode < 0.5)
    {
        float2 p = (_Area_ToLocal(uv) - 0.5) * 2.0;
        float radius = length(p);
        // The center has no unique angle. Choose zero without evaluating atan2(0, 0).
        float angle = 0.0;
        if (radius > 0.0) angle = atan2(p.y, p.x) / 6.28318530718;
        // Only the angular coordinate wraps; radius continues beyond the frame.
        sampleUV = float2(frac(angle - _AngleOffset / 360.0), radius - _RadialOffset);
    }
    else
    {
        float angle = (uv.x + _AngleOffset / 360.0) * 6.28318530718;
        float radius = uv.y + _RadialOffset;
        float sine, cosine;
        sincos(angle, sine, cosine);
        float2 localUV = 0.5 + float2(cosine, sine) * radius * 0.5;
        sampleUV = _Area_ToInput(localUV);
    }
    return SampleInput(sampleUV);
}
