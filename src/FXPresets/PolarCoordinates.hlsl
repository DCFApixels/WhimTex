// @whimtex-effect Distortion/Polar Coordinates
// @control(_Amount)
// @param hidden float _Amount = 1 [0 .. 1] // Coordinate distortion amount. Zero keeps the original image; one applies the full mapping.
// @group(Polar Coordinates Mode; _Mode)
// @param hidden enum _Mode = 0 {ToPolar: 0, FromPolar: 1} // Wrap a strip into a circle, or unwrap a circle into a strip.
// @param float _AngleOffset = 0 [~-180 .. ~180] // Angular offset in degrees.
// @param float _RadialOffset = 0 [~-1 .. ~1] // Radial offset in normalized coordinates.
// @param transform2D _Input // Input frame: position, size and rotation of the source strip or circle.
// @param transform2D _Output // Output frame: position, size and rotation of the resulting circle or strip.
// @param enum _Tiling = Clamp {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3} // Sampling outside the input image: extend its edge, repeat, mirror, or return transparency.
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    if (_Amount <= 0.0) return color;
    float2 localUV = _Output_ToLocal(uv);
    float2 mappedUV;
    if (_Mode < 0.5)
    {
        float2 p = (localUV - 0.5) * 2.0;
        float radius = length(p);
        // The center has no unique angle. Choose zero without evaluating atan2(0, 0).
        float angle = 0.0;
        if (radius > 0.0) angle = atan2(p.y, p.x) / 6.28318530718;
        // Only the angular coordinate wraps; radius continues beyond the frame.
        mappedUV = float2(frac(angle - _AngleOffset / 360.0), radius - _RadialOffset);
    }
    else
    {
        float angle = (localUV.x + _AngleOffset / 360.0) * 6.28318530718;
        float radius = localUV.y + _RadialOffset;
        float sine, cosine;
        sincos(angle, sine, cosine);
        mappedUV = 0.5 + float2(cosine, sine) * radius * 0.5;
    }
    float2 sampleUV = _Input_ToInput(mappedUV);
    return SampleInput(lerp(uv, sampleUV, _Amount), _Tiling);
}
