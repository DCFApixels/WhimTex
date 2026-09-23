// @whimtex-effect Stylization/CRT
// @header(Screen)
// @param float _Curvature = 0.08 [0 .. ~0.35] // Barrel curvature; stronger values crop more of the screen edges.
// @param float _ScanlineStrength = 0.2 [0 .. 1] // Darkness of the scanlines.
// @param float _ScanlineSpacing = 2 [1 .. ~8] // Distance between scanline peaks in canvas pixels.
// @param float _PhosphorStrength = 0.12 [0 .. 1] // Strength of the RGB phosphor stripe mask.
// @param float _Vignette = 0.15 [0 .. 1] // Darken the screen toward its corners.
// @header(Signal)
// @param float _ChromaticAberration = 0.5 [0 .. ~8] // Radial red/blue separation in canvas pixels.
// @param float _NoiseAmount = 0.01 [0 .. ~0.1] // Static grain amplitude.
// @param float _Flicker = 0.02 [0 .. ~0.1] // Frame brightness variation; evaluates from Effect Time and Seed.
// @header(Playback)
// @param float _Seed = 0 [0 .. ~10000] // Changes the deterministic grain and flicker pattern.
// @param float _EffectTime = 0 [0 .. ~3600] // Time in seconds; change it to inspect another animation frame.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float4 result = color;
    float2 pixel = uv * _CanvasSize.xy;
    float2 centered = (uv - 0.5) * 2.0;
    float radialSq = dot(centered, centered);
    float2 sampleUV = 0.5 + centered * (0.5 + 0.5 * _Curvature * radialSq);
    float2 halfTexel = 0.5 * _CanvasSize.zw;
    float inBounds = step(0.0, sampleUV.x) * step(sampleUV.x, 1.0)
                   * step(0.0, sampleUV.y) * step(sampleUV.y, 1.0);
    float2 safeUV = clamp(sampleUV, halfTexel, 1.0 - halfTexel);
    float4 screen = SampleInput(safeUV);

    if (_ChromaticAberration > 0.0)
    {
        float2 radial = centered * rsqrt(max(radialSq, 1e-6));
        float2 fringe = radial * (_ChromaticAberration * _CanvasSize.zw);
        screen.r = SampleInput(clamp(safeUV + fringe, halfTexel, 1.0 - halfTexel)).r;
        screen.b = SampleInput(clamp(safeUV - fringe, halfTexel, 1.0 - halfTexel)).b;
    }

    float scanPhase = 3.14159265 * pixel.y / max(_ScanlineSpacing, 1.0);
    float scanline = 1.0 - _ScanlineStrength * (0.5 + 0.5 * cos(scanPhase));

    float stripe = fmod(floor(pixel.x), 3.0);
    float3 phosphorMask = float3(0.72, 0.72, 0.72);
    if (stripe < 1.0) phosphorMask.r = 1.0;
    else if (stripe < 2.0) phosphorMask.g = 1.0;
    else phosphorMask.b = 1.0;
    phosphorMask = lerp(1.0, phosphorMask, _PhosphorStrength);

    float frame = floor(max(_EffectTime, 0.0) * 24.0);
    float seededFrame = frame + _Seed * 31.7;
    float grain = HashNoise(pixel + float2(_Seed * 17.13, seededFrame)) - 0.5;
    float flickerNoise = HashNoise(float2(_Seed * 13.1 + 7.7, seededFrame + 19.3)) * 2.0 - 1.0;
    float flicker = 1.0 + flickerNoise * _Flicker;
    float vignette = 1.0 - _Vignette * saturate(radialSq * 0.5);

    screen.rgb = max(screen.rgb * (scanline * vignette * flicker) * phosphorMask
                   + grain * _NoiseAmount, 0.0);
    result.rgb = lerp(0.0, screen.rgb, inBounds);
    return result;
}
