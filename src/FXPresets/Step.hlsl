// @whimtex-effect Stylization/Step
// @group(Step; _Mode)
// @param label(Edge) float _Edge = 0.5 [~0 .. ~1] // Threshold for each selected channel.
// @param hidden enum _Mode = Hard {Hard: 0, Smoothstep: 1}
// @if _Mode == 1
// @param float _Hardness = 0.75 [0 .. 1] // 1 is a hard edge; 0 gives the widest smooth transition.
// @endif
// @endgroup
// @group(Apply To; _ApplyMode)
// @param hidden enum _ApplyMode = Channels {Channels: 0, Color: 1}
// @if _ApplyMode == 0
// @param label(R) bool _Red = true
// @param label(G) bool _Green = true
// @param label(B) bool _Blue = true
// @param label(A) bool _Alpha = false
// @endif
// @if _ApplyMode == 1
// @param label(Color) color _ApplyColor = (1, 1, 1, 0) // Per-channel blend weight from the source to the stepped result.
// @endif
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    float4 stepped = color;
    if (_Mode < 0.5)
    {
        stepped = step(_Edge, color);
    }
    else
    {
        float transitionWidth = 1.0 - saturate(_Hardness);
        if (transitionWidth <= 1e-5)
            stepped = step(_Edge, color);
        else
            stepped = smoothstep(_Edge - transitionWidth * 0.5,
                                 _Edge + transitionWidth * 0.5,
                                 color);
    }

    float4 channelMask = float4(_Red, _Green, _Blue, _Alpha);
    float4 applyWeights = lerp(channelMask, _ApplyColor, _ApplyMode);
    return lerp(color, stepped, applyWeights);
}
