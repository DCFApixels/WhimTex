// @whimtex-effect Distortion/UV Transform
// @preset("8e70c9d86d4a4d08b7ddd9bf7d00dfd0", 1)
// @group("Transform")
// @icon("../Editor/Icons/FX/Transform.svg")
// @param transform2D _Area
// @param enum _InputTiling = Clip {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3} // Sampling outside the input image: extend its edge, repeat, mirror, or return transparency.

float4 ApplyFX(PinnedUV uv, float4 color)
{
    float2 localUV = _Area_ToLocal(uv);
    return SampleInput(localUV, _InputTiling);
}
