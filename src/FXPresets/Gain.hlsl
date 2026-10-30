// @whimtex-effect Color/Gain
// @preset("c6c64ac0946d46bbadc8c1475121db0c", 1)
// @control(_Opacity)
// @group("Tone")
// @icon("../Editor/Icons/FX/Tone.svg")
// @param hidden float _Opacity = 1 [0 .. 1] // Blend between the original and corrected colors.
// @group
// @param float _Gain = 1 [0 .. ~4]
// @param color _Tint = (1, 1, 1, 1)
// @endgroup

float4 ApplyFX(float4 color)
{
    float4 correctedColor = float4(color.rgb * _Tint.rgb * _Gain, color.a * _Tint.a);
	return lerp(color, correctedColor, _Opacity);
}
