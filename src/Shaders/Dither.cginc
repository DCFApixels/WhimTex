#ifndef WHIMTEX_DITHER_INCLUDED
#define WHIMTEX_DITHER_INCLUDED

// Ordered thresholds in 0..1, compared against the fractional part of a level step.
// The matrices are truncated to their own period so the pattern stays exact on large canvases.
float BayerDither2(float2 p)
{
    p = floor(p);
    return frac(p.x * 0.5 + p.y * p.y * 0.75);
}

float BayerDither4(float2 p)
{
    p = fmod(floor(p), 4.0);
    return BayerDither2(p * 0.5) * 0.25 + BayerDither2(p);
}

float BayerDither8(float2 p)
{
    p = fmod(floor(p), 8.0);
    return BayerDither4(p * 0.5) * 0.25 + BayerDither2(p);
}

// Interleaved gradient noise: as cheap as a hash, without the visible grid of a Bayer matrix.
float InterleavedNoise(float2 p)
{
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

// Stable hash: the same pixel always gets the same threshold, so the layer does not shimmer.
float HashNoise(float2 p)
{
    return frac(sin(dot(floor(p), float2(12.9898, 78.233))) * 43758.5453);
}

float CheckerPattern(float2 p)
{
    return frac((floor(p.x) + floor(p.y)) * 0.5);
}

// Clustered-dot screen: the threshold peaks at the center of every 4x4 cell.
// This is the one pattern that can reach 1.0, so callers clamp the result of the level step.
float HalftonePattern(float2 p)
{
    p = fmod(floor(p), 4.0) * 0.25;
    return saturate(1.0 - length(p * 2.0 - 1.0) * 1.25);
}

// Shared by every preset that exposes a dither mode: keep the option values in @param enums in this order.
float DitherThreshold(float2 pixel, float mode)
{
    int index = (int)floor(mode + 0.5);
    if (index == 1) return BayerDither2(pixel);
    if (index == 2) return BayerDither4(pixel);
    if (index == 3) return BayerDither8(pixel);
    if (index == 4) return InterleavedNoise(pixel);
    if (index == 5) return CheckerPattern(pixel);
    if (index == 6) return HalftonePattern(pixel);
    if (index == 7) return HashNoise(pixel);
    return 0.5;
}
#endif
