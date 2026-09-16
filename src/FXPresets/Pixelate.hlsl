// @whimtex-effect Pixel Art/Pixelate
// @param float _PixelSize = 4 [1 .. ~64] // Block size in canvas pixels.
// @param bool _Average = false // Average the block over a 4x4 grid instead of sampling its center; ignored for single-pixel blocks.
// @param enum _Dither = None {None: 0, Bayer2: 1, Bayer4: 2, Bayer8: 3, Interleaved: 4, Checker: 5, Halftone: 6, Hash: 7} // Dither pattern, evaluated on the block grid so it stays visible after pixelation.
// @param float _Amount = 1 [0 .. 1] // Dither strength; zero rounds each channel to the nearest level.
// @param float _Levels = 8 [2 .. ~64] // Output levels per channel, rounded to an integer.
// @param float _Gamma = 1 [0.1 .. 5] // Distribution of the tonal steps; ignored in One-bit mode.
// @param bool _OneBit = false // Reduce to the two colors below instead of quantizing each channel.
// @param color _LowColor = (0, 0, 0, 1) // Dark color in One-bit mode; color alpha is ignored.
// @param color _HighColor = (1, 1, 1, 1) // Light color in One-bit mode; color alpha is ignored.
#include "Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc"

float4 SampleBlock(float2 blockUv, float2 blockSize, float size)
{
    if (_Average <= 0.5 || size < 2.0) return SampleInput(blockUv);
    // Four taps per axis; a block larger than four pixels still samples a fixed 4x4 grid.
    float4 sum = 0.0;
    for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            float2 offset = (float2(x, y) + 0.5) * 0.25 - 0.5;
            sum += SampleInput(blockUv + offset * blockSize);
        }
    return sum * 0.0625;
}

float4 ApplyFX(float2 uv, float4 color)
{
    float size = max(1.0, floor(_PixelSize + 0.5));
    float2 canvas = max(_CanvasSize.xy, 1.0);
    float2 block = floor(floor(uv * canvas) / size);
    float2 blockUv = (block + 0.5) * size / canvas;
    float4 source = SampleBlock(blockUv, size / canvas, size);
    // Amount 0 keeps the plain rounding threshold, so the effect degrades to clean quantization.
    float threshold = lerp(0.5, DitherThreshold(block, _Dither), saturate(_Amount));
    float gamma = clamp(_Gamma, 0.1, 5.0);
    if (_OneBit > 0.5)
    {
        float luminance = saturate(dot(max(source.rgb, 0.0), float3(0.2126, 0.7152, 0.0722)));
        // The clamp keeps a Halftone threshold of exactly 1.0 from producing a third color.
        source.rgb = lerp(_LowColor.rgb, _HighColor.rgb, floor(saturate(luminance + threshold)));
        return source;
    }
    float steps = clamp(floor(_Levels + 0.5), 2.0, 64.0) - 1.0;
    float3 toned = pow(saturate(source.rgb), gamma);
    // The clamp keeps a Halftone threshold of exactly 1.0 from pushing a channel above 1.0.
    source.rgb = pow(min(floor(toned * steps + threshold), steps) / steps, 1.0 / gamma);
    return source;
}
