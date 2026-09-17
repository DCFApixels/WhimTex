// @whimtex-effect Color/Posterize
// @header(Tonal Steps)
// @param float _Levels = 8 [2 .. ~64] // Number of output levels per channel, rounded to an integer.
// @param float _Gamma = 1 [0.1 .. ~5] // Distribution of the tonal steps.
// @header(Dithering)
// @param enum _Dither = None {None: 0, Bayer2: 1, Bayer4: 2, Bayer8: 3, Interleaved: 4, Checker: 5, Halftone: 6, Hash: 7} // Dither pattern, applied per pixel; the same list as in Pixelate.
// @param float _Amount = 1 [0 .. 1] // Dither strength; zero rounds each channel to the nearest level.
#include "Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float steps = max(floor(_Levels + 0.5), 2.0) - 1.0;
    float gamma = max(_Gamma, 0.1);
    float3 c = pow(saturate(color.rgb), gamma);
    float threshold = lerp(0.5, DitherThreshold(floor(uv * _CanvasSize.xy), _Dither), saturate(_Amount));
    // The clamp keeps a Halftone threshold of exactly 1.0 from pushing a channel above 1.0.
    color.rgb = pow(min(floor(c * steps + threshold), steps) / steps, 1.0 / gamma);
    return color;
}
