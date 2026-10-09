// @whimtex-effect Stylization/Threshold
// @header(Threshold)
// @param label(Source Channel) enum _SourceChannel = Luminance {Luminance:0,R:1,G:2,B:3,Alpha:4} // Channel compared with the threshold. This does not change output alpha.
// @param float _Threshold = 0.25 [0 .. ~1] // Threshold in the source channel.
// @param label(Transition Width) float _Smooth = 0.0 [0 .. ~0.2] // Transition width; zero gives a hard threshold.
// @header(Output Colors)
// @param color _LowColor = (0, 0, 0, 1) // RGB below the threshold; color alpha is ignored.
// @param color _HighColor = (1, 1, 1, 1) // RGB above the threshold; color alpha is ignored.

float4 ApplyFX(float2 uv, float4 color)
{
    float v = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
    if (_SourceChannel == 1.0) v = color.r;
    else if (_SourceChannel == 2.0) v = color.g;
    else if (_SourceChannel == 3.0) v = color.b;
    else if (_SourceChannel == 4.0) v = color.a;
    float width = max(_Smooth, 0.0);
    float t;
    if (width <= 0.0) t = step(_Threshold, v);
    else t = smoothstep(_Threshold - width, _Threshold + width, v);
    color.rgb = lerp(_LowColor.rgb, _HighColor.rgb, t);
    return color;
}
