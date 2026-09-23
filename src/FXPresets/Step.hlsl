// @whimtex-effect Stylization/Step
// @param float _Edge = 0.5 [~0 .. ~1] // Per-channel threshold.
// @param enum _Mode = 0 {Hard: 0, Smoothstep: 1}
// @if _Mode == 1
// @param float _Hardness = 0.75 [0 .. 1] // 1 is a hard edge; 0 gives the widest smooth transition.
// @endif

float4 ApplyFX(float2 uv, float4 color)
{
    if (_Mode < 0.5)
    {
        color.rgb = step(_Edge, color.rgb);
        return color;
    }

    float transitionWidth = 1.0 - saturate(_Hardness);
    if (transitionWidth <= 1e-5)
        color.rgb = step(_Edge, color.rgb);
    else
        color.rgb = smoothstep(_Edge - transitionWidth * 0.5,
                               _Edge + transitionWidth * 0.5,
                               color.rgb);

    return color;
}
