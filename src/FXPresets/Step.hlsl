// @whimtex-effect Stylization/Step
// @param float _Edge = 0.5 [~0 .. ~1] // Per-channel threshold.
// @header(Channels)
// @param bool _Red = true
// @param bool _Green = true
// @param bool _Blue = true
// @param bool _Alpha = false
// @param enum _Mode = 0 {Hard: 0, Smoothstep: 1}
// @if _Mode == 1
// @param float _Hardness = 0.75 [0 .. 1] // 1 is a hard edge; 0 gives the widest smooth transition.
// @endif

float4 ApplyFX(float2 uv, float4 color)
{
    float4 stepped = color;
    if (_Mode < 0.5)
    {
        stepped = step(_Edge, color);
        if (_Red > 0.5) color.r = stepped.r;
        if (_Green > 0.5) color.g = stepped.g;
        if (_Blue > 0.5) color.b = stepped.b;
        if (_Alpha > 0.5) color.a = stepped.a;
        return color;
    }

    float transitionWidth = 1.0 - saturate(_Hardness);
    if (transitionWidth <= 1e-5)
        stepped = step(_Edge, color);
    else
        stepped = smoothstep(_Edge - transitionWidth * 0.5,
                             _Edge + transitionWidth * 0.5,
                             color);

    if (_Red > 0.5) color.r = stepped.r;
    if (_Green > 0.5) color.g = stepped.g;
    if (_Blue > 0.5) color.b = stepped.b;
    if (_Alpha > 0.5) color.a = stepped.a;
    return color;
}
