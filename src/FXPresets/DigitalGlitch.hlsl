// @whimtex-effect Stylization/Digital Glitch
// @param float _Blend = 1 [0 .. 1] // Blend the corrupted signal with the original image.
// @header(Line Tearing)
// @param float _TearDensity = 0.035 [0 .. 1] // Fraction of scanline bands that receive a large displacement.
// @if _TearDensity != 0
// @param float _TearLineHeight = 2 [1 .. ~32] // Height of each independently randomized horizontal band in pixels.
// @param float _TearJitter = 0.25 [0 .. ~12] // Small horizontal wobble applied to every band in pixels.
// @param float _TearShift = 24 [0 .. ~128] // Maximum extra horizontal shift on selected bands in pixels.
// @endif
// @header(Block Corruption)
// @param float _BlockDensity = 0.025 [0 .. 1] // Fraction of grid cells that become corrupted blocks.
// @if _BlockDensity != 0
// @param float _BlockWidth = 48 [2 .. ~256] // Width of the block grid in canvas pixels.
// @param float _BlockHeight = 8 [2 .. ~128] // Height of the block grid in canvas pixels.
// @param float _BlockSizeRandomness = 0.75 [0 .. 1] // Randomize shared grid boundaries while keeping blocks packed without gaps.
// @param enum _BlockOrder = RowsFirst {RowsFirst: 0, ColumnsFirst: 1} // RowsFirst jitters line heights, then gives each line an independent width layout. ColumnsFirst jitters column widths, then gives each column an independent height layout.
// @param float _BlockShiftX = 24 [0 .. ~256] // Maximum horizontal block displacement in pixels.
// @param float _BlockShiftY = 4 [0 .. ~128] // Maximum vertical block displacement in pixels.
// @param float _Dropout = 0 [0 .. 1] // Chance for a selected block to drop to black.
// @param float _BlockVoidChance = 0 [0 .. 1] // Chance for a selected block to become transparent instead of black.
// @endif
// @header(Color Artifacts)
// @param float _ColorJitterDensity = 0.025 [0 .. 1] // Chance per block for random color jitter, independent of Block Density.
// @if _ColorJitterDensity != 0
// @param float _BlockColorJitter = 0.15 [0 .. 1] // Strength of the random color tint.
// @endif
// @param float _GradientDensity = 0 [0 .. 1] // Chance per block for gradient tint, independent of Block Density and Color Jitter Density.
// @if _GradientDensity != 0
// @param enum _GradientBlendMode = Overlay {Add: 0, Multiply: 1, Overlay: 2, Overwrite: 3} // Add, multiply, overlay the gradient over the image, or blend directly toward the gradient color.
// @param float _GradientOpacity = 1 [0 .. 1] // Strength of the selected gradient blend mode.
// @param gradient _BlockTintGradient = #7EF3FFFF -> #B270FFFF // Palette used to tint corrupted blocks.
// @param enum _GradientMapping = RandomPerBlock {RandomPerBlock: 0, Vertical: 1, Horizontal: 2} // Sample a random palette position per block, or map it across the canvas.
// @endif
// @header(Color Channels)
// @param float _RGBSplit = 1 [0 .. ~16] // Red/blue channel separation in pixels.
// @if _RGBSplit != 0
// @param float _RGBAngle = 0 [~-180 .. ~180] // Direction of the red shift; blue shifts oppositely.
// @endif
// @param float _ColorLoss = 0.08 [0 .. 1] // Reduce saturation in the corrupted signal.
// @param float _PosterizeAmount = 0 [0 .. 1] // Blend in low-bit color quantization.
// @if _PosterizeAmount != 0
// @param float _PosterizeLevels = 8 [2 .. ~32] // Number of tonal levels per channel.
// @endif
// @header(Noise)
// @param float _NoiseAmount = 0.015 [0 .. ~0.15] // Random luma noise amplitude.
// @if _NoiseAmount != 0
// @param float _NoiseColor = 0.35 [0 .. 1] // Blend from monochrome grain to independent RGB noise.
// @endif
// @header(Alpha)
// @param float _AlphaFollowChance = 0 [0 .. 1] // Per block, chance for alpha to follow the same displacement as the corrupted color.
// @param float _AlphaJitter = 0 [0 .. ~32] // Maximum additional alpha-only offset in canvas pixels when alpha follows.
// @param float _AlphaJitterFlip = 0.5 [0 .. 1] // Chance to reverse the alpha-only offset relative to the color displacement.
// @param float _AlphaNoise = 0 [0 .. 1] // Random per-pixel opacity variation.
// @header(Playback)
// @param float _Seed = 0 [0 .. ~10000] // Changes the deterministic corruption pattern.
// @param float _EffectTime = 0 [0 .. ~3600] // Time in seconds; change it to inspect another glitch frame.
// @param float _FrameRate = 12 [1 .. ~60] // Rate at which randomized artifacts change over time.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc"

float BlockBoundary(float boundaryIndex, float cellSize, float randomness, float variationKey, float seedOffset)
{
    float boundaryNoise = HashNoise(float2(boundaryIndex + _Seed * 31.7 + seedOffset, variationKey + _Seed * 13.1));
    float offset = (boundaryNoise * 2.0 - 1.0) * (0.45 * randomness * cellSize);
    return boundaryIndex * cellSize + offset;
}

float3 FindBlockCell(float position, float cellSize, float randomness, float variationKey, float seedOffset)
{
    cellSize = max(cellSize, 2.0);
    float index = floor(position / cellSize);
    float start = BlockBoundary(index, cellSize, randomness, variationKey, seedOffset);
    float end = BlockBoundary(index + 1.0, cellSize, randomness, variationKey, seedOffset);

    // Boundary jitter is capped below half a cell, so only one neighboring cell can overlap the nominal index.
    if (position < start)
    {
        index -= 1.0;
        end = start;
        start = BlockBoundary(index, cellSize, randomness, variationKey, seedOffset);
    }
    else if (position >= end)
    {
        index += 1.0;
        start = end;
        end = BlockBoundary(index + 1.0, cellSize, randomness, variationKey, seedOffset);
    }

    return float3(index, start, max(end - start, 1.0));
}

float4 ApplyFX(float2 uv, float4 color)
{
    float4 result = color;
    float2 pixel = uv * _CanvasSize.xy;
    float2 texel = _CanvasSize.zw;
    float frameIndex = floor(max(_EffectTime, 0.0) * max(_FrameRate, 1.0));

    float tearLineHeight = max(_TearLineHeight, 1.0);
    float lineIndex = floor(pixel.y / tearLineHeight);
    float lineKey = lineIndex + _Seed * 17.31;
    float tearRoll = HashNoise(float2(lineKey, frameIndex + _Seed * 29.7 + 3.1));
    float tearMask = step(1.0 - _TearDensity, tearRoll);
    float lineJitter = (HashNoise(float2(lineKey + 91.7, frameIndex + 7.3)) * 2.0 - 1.0) * _TearJitter;
    float tearShift = (HashNoise(float2(lineKey + 173.9, frameIndex + 19.1)) * 2.0 - 1.0)
                    * _TearShift * tearMask;

    float3 blockColumn;
    float3 blockRow;
    if (_BlockOrder < 0.5)
    {
        // First make variable-height rows, then independently segment each row into variable-width blocks.
        blockRow = FindBlockCell(pixel.y, _BlockHeight, _BlockSizeRandomness, frameIndex, 151.7);
        blockColumn = FindBlockCell(pixel.x, _BlockWidth, _BlockSizeRandomness,
                                    blockRow.x + frameIndex * 13.3, 271.9);
    }
    else
    {
        // First make variable-width columns, then independently segment each column into variable-height blocks.
        blockColumn = FindBlockCell(pixel.x, _BlockWidth, _BlockSizeRandomness, frameIndex, 271.9);
        blockRow = FindBlockCell(pixel.y, _BlockHeight, _BlockSizeRandomness,
                                 blockColumn.x + frameIndex * 13.3, 151.7);
    }
    float2 blockGrid = float2(blockColumn.x, blockRow.x);
    float2 blockKey = blockGrid + float2(_Seed * 37.1, frameIndex + _Seed * 11.3);
    float blockRoll = HashNoise(blockKey + float2(13.7, 41.9));
    float blockMask = step(1.0 - _BlockDensity, blockRoll);
    float blockShiftX = (HashNoise(blockKey + float2(53.1, 7.7)) * 2.0 - 1.0) * _BlockShiftX * blockMask;
    float blockShiftY = (HashNoise(blockKey + float2(89.3, 23.5)) * 2.0 - 1.0) * _BlockShiftY * blockMask;

    float2 shiftedUV = uv + float2(lineJitter + tearShift + blockShiftX, blockShiftY) * texel;
    float2 halfTexel = 0.5 * texel;
    float2 safeUV = clamp(shiftedUV, halfTexel, 1.0 - halfTexel);
    float4 signal = SampleInput(safeUV);

    float3 blockTint = float3(1.0, 1.0, 1.0);
    if (_ColorJitterDensity > 0.0 && _BlockColorJitter > 0.0)
    {
        float colorJitterRoll = HashNoise(blockKey + float2(113.1, 59.7));
        float colorJitterMask = step(1.0 - _ColorJitterDensity, colorJitterRoll);
        if (colorJitterMask > 0.0)
        {
    float tintR = HashNoise(blockKey + float2(179.3, 97.1));
            float tintG = HashNoise(blockKey + float2(239.7, 131.9));
            float tintB = HashNoise(blockKey + float2(293.1, 173.3));
            float3 randomTint = float3(tintR, tintG, tintB) * 1.5;
            blockTint = lerp(float3(1.0, 1.0, 1.0), randomTint, saturate(_BlockColorJitter));
        }
    }
    float4 gradientSample = float4(0.0, 0.0, 0.0, 0.0);
    float gradientMask = 0.0;
    if (_GradientDensity > 0.0)
    {
        float gradientRoll = HashNoise(blockKey + float2(347.3, 271.7));
        gradientMask = step(1.0 - _GradientDensity, gradientRoll);
        if (gradientMask > 0.0)
        {
            float gradientT = HashNoise(blockKey + float2(521.1, 431.7));
            if (_GradientMapping > 0.5 && _GradientMapping < 1.5)
                gradientT = saturate((blockRow.y + blockRow.z * 0.5) / max(_CanvasSize.y, 1.0));
            else if (_GradientMapping >= 1.5)
                gradientT = saturate((blockColumn.y + blockColumn.z * 0.5) / max(_CanvasSize.x, 1.0));
            gradientSample = _BlockTintGradient_Sample(gradientT);
        }
    }
    float dropoutRoll = HashNoise(blockKey + float2(307.9, 211.3));
    float dropoutMask = step(1.0 - _Dropout, dropoutRoll) * blockMask;
    float blockVoidMask = 0.0;
    if (_BlockVoidChance > 0.0 && blockMask > 0.0)
    {
        float voidRoll = HashNoise(blockKey + float2(347.3, 271.7));
        blockVoidMask = step(1.0 - _BlockVoidChance, voidRoll);
    }
    float3 video = signal.rgb;

    if (_RGBSplit > 0.0)
    {
        float sine, cosine;
        sincos(radians(_RGBAngle), sine, cosine);
        float2 channelOffset = float2(cosine, sine) * (_RGBSplit * texel);
        float red = SampleInput(clamp(safeUV + channelOffset, halfTexel, 1.0 - halfTexel)).r;
        float blue = SampleInput(clamp(safeUV - channelOffset, halfTexel, 1.0 - halfTexel)).b;
        video = float3(red, signal.g, blue);
    }

    float3 shiftedVideo = video;
    float3 jitteredVideo = shiftedVideo * blockTint;
    video = jitteredVideo;
    float gradientOpacity = saturate(_GradientOpacity * gradientSample.a);
    if (gradientMask > 0.0 && gradientOpacity > 0.0)
    {
        float3 gradientColor = gradientSample.rgb;
        if (_GradientBlendMode < 0.5) // Add
            video = jitteredVideo + gradientColor * gradientOpacity;
        else if (_GradientBlendMode < 1.5) // Multiply
            video = lerp(jitteredVideo, jitteredVideo * gradientColor, gradientOpacity);
        else if (_GradientBlendMode < 2.5) // Overlay the gradient while retaining image contrast.
        {
            float3 overlay = lerp(
                2.0 * jitteredVideo * gradientColor,
                1.0 - 2.0 * (1.0 - jitteredVideo) * (1.0 - gradientColor),
                step(0.5, jitteredVideo));
            video = lerp(jitteredVideo, overlay, gradientOpacity);
        }
        else // Overwrite: blend directly toward the gradient color.
            video = lerp(jitteredVideo, gradientColor, gradientOpacity);
    }
    video *= 1.0 - dropoutMask;

    float noiseKey = frameIndex + _Seed * 53.9;
    float grayNoise = HashNoise(pixel + float2(_Seed * 67.1, noiseKey + 17.3));
    float3 colorNoise = float3(
        HashNoise(pixel + float2(_Seed * 67.1 + 7.1, noiseKey + 31.7)),
        HashNoise(pixel + float2(_Seed * 67.1 + 19.3, noiseKey + 47.9)),
        HashNoise(pixel + float2(_Seed * 67.1 + 43.7, noiseKey + 71.1)));
    float3 noise = lerp(grayNoise.xxx, colorNoise, _NoiseColor) - 0.5;
    video = max(video + noise * _NoiseAmount, 0.0);

    float luma = dot(video, float3(0.2126, 0.7152, 0.0722));
    video = lerp(video, luma.xxx, _ColorLoss);

    if (_PosterizeAmount > 0.0)
    {
        float levels = max(floor(_PosterizeLevels + 0.5), 2.0);
        float3 quantized = floor(saturate(video) * levels + 0.5) / levels;
        video = lerp(video, quantized, _PosterizeAmount);
    }

    float alpha = color.a;
    if (_AlphaFollowChance > 0.0 || _AlphaNoise > 0.0 || blockVoidMask > 0.0)
    {
        if (_AlphaFollowChance > 0.0)
        {
            float alphaRoll = HashNoise(blockKey + float2(389.9, 317.3));
            float alphaFollows = step(1.0 - _AlphaFollowChance, alphaRoll);
            float alphaJitter = HashNoise(blockKey + float2(479.3, 397.1)) * _AlphaJitter;
            float2 colorOffset = float2(lineJitter + tearShift + blockShiftX, blockShiftY);
            float colorOffsetLengthSq = dot(colorOffset, colorOffset);
            float2 jitterDirection = colorOffsetLengthSq > 1e-5
                ? colorOffset * rsqrt(colorOffsetLengthSq)
                : float2(1.0, 0.0);
            float flipRoll = HashNoise(blockKey + float2(431.7, 353.9));
            float jitterSign = step(flipRoll, _AlphaJitterFlip) > 0.0 ? -1.0 : 1.0;
            float2 alphaOnlyOffset = jitterDirection * alphaJitter * jitterSign;
            float2 alphaUV = clamp(uv + (colorOffset + alphaOnlyOffset) * texel,
                                   halfTexel, 1.0 - halfTexel);
            float shiftedAlpha = SampleInput(alphaUV).a;
            alpha = lerp(color.a, shiftedAlpha, alphaFollows);
        }

        if (_AlphaNoise > 0.0)
        {
            float alphaNoise = HashNoise(pixel + float2(_Seed * 83.3, noiseKey + 101.9)) - 0.5;
            alpha = saturate(alpha + alphaNoise * _AlphaNoise);
        }

        alpha *= 1.0 - blockVoidMask;
    }

    result.rgb = lerp(color.rgb, video, saturate(_Blend));
    result.a = lerp(color.a, alpha, saturate(_Blend));
    return result;
}
