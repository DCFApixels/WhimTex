// @whimtex-effect Stylization/VHS
// @header(Tape Signal)
// @param float _ChromaBleed = 1 [0 .. ~8] // Horizontal red/blue separation in canvas pixels.
// @param float _LineJitter = 1.5 [0 .. ~12] // Seeded horizontal wobble per scanline in canvas pixels.
// @param float _ColorLoss = 0.12 [0 .. 1] // Reduce color saturation like an analog tape signal.
// @param float _NoiseAmount = 0.025 [0 .. ~0.15] // Luma noise amplitude.
// @group(Tracking Strength; _TrackingStrength)
// @param hidden float _TrackingStrength = 0.35 [0 .. 1] // Strength of the moving horizontal tracking tear.
// @if _TrackingStrength != 0
// @param float _TrackingHeight = 8 [1 .. ~64] // Height of the tracking band in canvas pixels.
// @param float _TrackingOffset = 18 [0 .. ~128] // Maximum horizontal displacement in the tracking band.
// @param float _TrackingSpeed = 0.35 [0 .. ~4] // Tracking-band travel speed per second.
// @endif
// @endgroup
// @header(Playback)
// @param float _Seed = 0 [0 .. ~10000] // Changes the deterministic line jitter and noise pattern.
// @param float _EffectTime = 0 [0 .. ~3600] // Time in seconds; change it to inspect another animation frame.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float4 result = color;
    float2 pixel = uv * _CanvasSize.xy;
    float2 texel = _CanvasSize.zw;
    float lineIndex = floor(pixel.y);
    float frame = floor(max(_EffectTime, 0.0) * 24.0);
    float seededFrame = frame + _Seed * 29.7;

    float lineNoise = HashNoise(float2(lineIndex + _Seed * 17.31, seededFrame + 3.1));
    float horizontalShift = (lineNoise - 0.5) * _LineJitter;

    float bandSeed = HashNoise(float2(_Seed * 11.7 + 5.3, floor(_EffectTime * 3.0) + 7.1));
    float bandCenter = frac(_EffectTime * _TrackingSpeed + bandSeed);
    float bandDistance = abs(frac(uv.y - bandCenter + 0.5) - 0.5);
    float bandHalfHeight = _TrackingHeight * texel.y;
    float trackingBand = 1.0 - smoothstep(bandHalfHeight, bandHalfHeight + 2.0 * texel.y, bandDistance);
    float bandNoise = HashNoise(float2(_Seed * 23.9 + 1.7, floor(_EffectTime * 8.0) + 41.0)) * 2.0 - 1.0;
    horizontalShift += trackingBand * bandNoise * _TrackingOffset * _TrackingStrength;

    float2 sampleUV = uv + float2(horizontalShift * texel.x, 0.0);
    float2 halfTexel = 0.5 * texel;
    float2 safeUV = clamp(sampleUV, halfTexel, 1.0 - halfTexel);
    float4 center = SampleInput(safeUV);
    float red = center.r;
    float blue = center.b;
    if (_ChromaBleed > 0.0)
    {
        float2 chromaOffset = float2(_ChromaBleed * texel.x, 0.0);
        red = SampleInput(clamp(safeUV + chromaOffset, halfTexel, 1.0 - halfTexel)).r;
        blue = SampleInput(clamp(safeUV - chromaOffset, halfTexel, 1.0 - halfTexel)).b;
    }

    float3 video = float3(red, center.g, blue);
    float luma = dot(video, float3(0.2126, 0.7152, 0.0722));
    video = lerp(video, luma.xxx, _ColorLoss);

    float grain = (HashNoise(pixel + float2(_Seed * 37.1, seededFrame + 83.3)) - 0.5) * _NoiseAmount;
    video = max(video + grain, 0.0);
    result.rgb = video;
    return result;
}
