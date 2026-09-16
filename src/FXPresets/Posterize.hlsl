// @whimtex-effect Color/Posterize
// @param float _Levels = 8 [2 .. 64] // Number of output levels per channel, rounded to an integer.
// @param float _Gamma = 1 [0.1 .. 5] // Distribution of the tonal steps.
// @param bool _Dither = false // Add stable per-pixel noise before quantization.

float4 ApplyFX(float2 uv, float4 color)
{
    float steps = clamp(floor(_Levels + 0.5), 2.0, 64.0) - 1.0;
    float gamma = clamp(_Gamma, 0.1, 5.0);
    float3 c = pow(saturate(color.rgb), gamma);
    if (_Dither > 0.5)
    {
        float2 pixel = floor(uv * _CanvasSize.xy);
        float n = frac(sin(dot(pixel, float2(12.9898, 78.233))) * 43758.5453);
        c += (n - 0.5) / steps;
    }
    color.rgb = pow(floor(saturate(c) * steps + 0.5) / steps, 1.0 / gamma);
    return color;
}
