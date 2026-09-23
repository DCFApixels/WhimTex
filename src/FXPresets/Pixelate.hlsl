// @whimtex-effect Stylization/Pixelate
// @header(Pixel Grid)
// @param float _PixelSize = 4 [1 .. ~64] // Block size in canvas pixels.
// @param float2 _Offset = (0, 0) // Shift the pixel grid origin in canvas pixels without moving the layer.
// @param bool _Average = false // Average the block over a 4x4 grid instead of sampling its center; ignored for single-pixel blocks.
/*
// Disabled experimental upscalers; kept here for later rework.
// @param enum _Scaler = None {None: 0, ScaleFX: 1, HQ3x: 2} // Single-pass edge smoothing on the pixel grid; these are visual styles, not bit-exact ports.
// @if _Scaler != 0
// @header(Edge Smoothing)
// @param float _EdgeRadius = 0.68 [0.1 .. ~1.5] // Smoothing reach as a fraction of a pixel block; larger values smooth farther into each block.
// @param float _Smoothing = 0.8 [0 .. ~3] // Blend strength inside the edge region; values above 1 increase smoothing, capped to prevent color overshoot.
// @param float _EdgeThreshold = 0.08 [0.01 .. ~0.5] // Lower values detect subtler tonal edges; raise it to ignore small color changes.
// @endif
*/
// @header(Dithering)
// @param enum _Dither = None {None: 0, Bayer2: 1, Bayer4: 2, Bayer8: 3, Interleaved: 4, Checker: 5, Halftone: 6, Hash: 7} // Dither pattern, evaluated on the block grid so it stays visible after pixelation.
// @if _Dither != 0
// @param float _Amount = 1 [0 .. 1] // Dither strength; zero rounds each channel to the nearest level.
// @endif
// @param bool _OneBit = false // Reduce to the two colors below instead of quantizing each channel.
// @if _OneBit != 1
// @header(Color Quantization)
// @param float _Levels = 8 [2 .. ~64] // Output levels per channel, rounded to an integer.
// @param float _Gamma = 1 [0.1 .. ~5] // Distribution of the tonal steps.
// @endif
// @if _OneBit == 1
// @header(One-bit Colors)
// @param color _LowColor = (0, 0, 0, 1) // Dark color in One-bit mode; color alpha is ignored.
// @param color _HighColor = (1, 1, 1, 1) // Light color in One-bit mode; color alpha is ignored.
// @endif
// @header(Transparency)
// @param bool _AlphaClip = false // Convert partially transparent pixels to a hard transparent/opaque edge.
// @if _AlphaClip == 1
// @param float _AlphaCutoff = 0.5 [0 .. 1] // Alpha at or above this value becomes opaque; lower alpha becomes transparent.
// @endif
#include "Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc"

float4 SampleBlock(float2 blockUv, float2 blockSize, float size)
{
    float4 sampled = 0.0;
    // Four taps per axis; a block larger than four pixels still samples a fixed 4x4 grid.
    if (_Average > 0.5 && size >= 2.0)
    {
        float4 sum = 0.0;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                float2 offset = (float2(x, y) + 0.5) * 0.25 - 0.5;
                sum += SampleInput(blockUv + offset * blockSize);
            }
        sampled = sum * 0.0625;
    }
    else sampled = SampleInput(blockUv);
    return sampled;
}

/* Disabled experimental upscaler helpers; retain for later rework.
float4 QuantizeBlock(float2 block, float size, float2 canvas)
{
    float2 blockCount = ceil(canvas / size);
    block = clamp(block, 0.0, blockCount - 1.0);
    float2 blockSize = size / canvas;
    float2 blockUv = (block + 0.5) * blockSize;
    // Keep the centers of partial edge blocks inside the canvas.
    blockUv = clamp(blockUv, 0.5 / canvas, 1.0 - 0.5 / canvas);
    float4 source = SampleBlock(blockUv, blockSize, size);

    float threshold = lerp(0.5, DitherThreshold(block, _Dither), saturate(_Amount));
    float gamma = max(_Gamma, 0.1);
    if (_OneBit > 0.5)
    {
        float luminance = saturate(dot(max(source.rgb, 0.0), float3(0.2126, 0.7152, 0.0722)));
        source.rgb = lerp(_LowColor.rgb, _HighColor.rgb, floor(saturate(luminance + threshold)));
        return source;
    }

    float steps = max(floor(_Levels + 0.5), 2.0) - 1.0;
    float3 toned = pow(saturate(source.rgb), gamma);
    source.rgb = pow(min(floor(toned * steps + threshold), steps) / steps, 1.0 / gamma);
    return source;
}

float ColorDistance(float3 a, float3 b)
{
    // A compact luma/chroma distance makes the edge test less sensitive to blue noise.
    float3 delta = abs(a - b);
    return dot(delta, float3(0.25, 0.5, 0.25)) + max(delta.r, max(delta.g, delta.b)) * 0.25;
}

float ColorSimilarity(float3 a, float3 b)
{
    // Fixed, looser tolerance recognizes neighboring samples from the same region.
    return 1.0 - saturate(ColorDistance(a, b) * 5.0);
}

float CornerWeight(float3 center, float3 horizontal, float3 vertical, float3 diagonal,
                   float2 local, float2 corner, float radius)
{
    float support = ColorSimilarity(horizontal, diagonal) * ColorSimilarity(vertical, diagonal);
    float threshold = max(_EdgeThreshold, 0.001);
    float contrastH = smoothstep(threshold * 0.5, threshold * 1.5, ColorDistance(horizontal, center));
    float contrastV = smoothstep(threshold * 0.5, threshold * 1.5, ColorDistance(vertical, center));
    float cornerDistance = length(local - corner);
    return support * contrastH * contrastV * (1.0 - smoothstep(0.0, radius, cornerDistance));
}

float4 SmoothPixelGrid(float2 block, float2 local, float size, float2 canvas, float4 center)
{
    float4 nw = QuantizeBlock(block + float2(-1, -1), size, canvas);
    float4 n  = QuantizeBlock(block + float2( 0, -1), size, canvas);
    float4 ne = QuantizeBlock(block + float2( 1, -1), size, canvas);
    float4 w  = QuantizeBlock(block + float2(-1,  0), size, canvas);
    float4 e  = QuantizeBlock(block + float2( 1,  0), size, canvas);
    float4 sw = QuantizeBlock(block + float2(-1,  1), size, canvas);
    float4 s  = QuantizeBlock(block + float2( 0,  1), size, canvas);
    float4 se = QuantizeBlock(block + float2( 1,  1), size, canvas);

    // Keep reconstruction confined to the outer part of each cell so the flat pixel
    // centers retain their full size. ScaleFX-style edges are softer and wider than HQ3x-style.
    bool hqxStyle = _Scaler > 1.5;
    float radius = max(_EdgeRadius, 0.001) * (hqxStyle ? 0.53 : 1.0);
    float nwWeight = CornerWeight(center.rgb, w.rgb, n.rgb, nw.rgb, local, float2(0, 0), radius);
    float neWeight = CornerWeight(center.rgb, e.rgb, n.rgb, ne.rgb, local, float2(1, 0), radius);
    float swWeight = CornerWeight(center.rgb, w.rgb, s.rgb, sw.rgb, local, float2(0, 1), radius);
    float seWeight = CornerWeight(center.rgb, e.rgb, s.rgb, se.rgb, local, float2(1, 1), radius);

    float totalWeight = nwWeight + neWeight + swWeight + seWeight;
    float3 neighborColor = (nw.rgb * nwWeight + ne.rgb * neWeight + sw.rgb * swWeight + se.rgb * seWeight) /
        max(totalWeight, 1e-5);
    // Limit the maximum color contribution so stronger smoothing softens corners
    // without shrinking the apparent pixel or turning the edge into a blur band.
    float maxBlend = hqxStyle ? 0.38 : 0.42;
    float blend = saturate(min(totalWeight, maxBlend) * max(_Smoothing, 0.0));
    float3 rgb = lerp(center.rgb, neighborColor, blend);
    return float4(rgb, center.a);
}
*/

float4 ApplyFX(float2 uv, float4 color)
{
    float size = max(1.0, floor(_PixelSize + 0.5));
    float2 canvas = max(_CanvasSize.xy, 1.0);
    float2 pixel = floor(uv * canvas - _Offset);
    float2 block = floor(pixel / size);
    float2 blockUv = ((block + 0.5) * size + _Offset) / canvas;
    float4 source = SampleBlock(blockUv, size / canvas, size);

    float threshold = lerp(0.5, DitherThreshold(block, _Dither), saturate(_Amount));
    float gamma = max(_Gamma, 0.1);
    if (_OneBit > 0.5)
    {
        float luminance = saturate(dot(max(source.rgb, 0.0), float3(0.2126, 0.7152, 0.0722)));
        source.rgb = lerp(_LowColor.rgb, _HighColor.rgb, floor(saturate(luminance + threshold)));
    }
    else
    {
        float steps = max(floor(_Levels + 0.5), 2.0) - 1.0;
        float3 toned = pow(saturate(source.rgb), gamma);
        source.rgb = pow(min(floor(toned * steps + threshold), steps) / steps, 1.0 / gamma);
    }

    if (_AlphaClip > 0.5)
        source.a = source.a >= _AlphaCutoff ? 1.0 : 0.0;
    return source;
}
