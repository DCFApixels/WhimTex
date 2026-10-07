// @whimtex-effect Transform/UV Transform
// @param transform2D _Area
// @param enum _InputTiling = Clip {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3} // Sampling outside the input image: extend its edge, repeat, mirror, or return transparency.

float4 ApplyFX(float2 uv, float4 color)
{
    float2 localUV = _Area_ToLocal(uv);
    return SampleInput(localUV, _InputTiling);
}
