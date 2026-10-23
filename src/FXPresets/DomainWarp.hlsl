// @whimtex-effect Distortion/Domain Warp
// @control(_Strength)
// @param hidden float _Strength = 1 [~-4 .. ~4] // Domain-warp amplitude. Zero outputs a neutral map. Negative values reverse the warp amplitude; progressive octaves can also change the pattern.
// @group(Warp)
// @param enum _WarpType = OpenSimplex2 {OpenSimplex2: 0, OpenSimplex2Reduced: 1, BasicGrid: 2}
// @param seed float _Seed = 1337 [0 .. ~10000]
// @param float _Scale = 4 [0.01 .. ~64] // Frequency in canvas UV coordinates. Higher values create finer patterns; this is not output-vector normalization.
// @param float2 _Offset = (0, 0) // Offset the procedural coordinates before warping. Does not add a constant displacement to the output.
// @endgroup
// @group(Fractal; _Fractal)
// @param hidden enum _Fractal = None {None: 0, Progressive: 4, Independent: 5} // Progressive warps the coordinates used by later octaves. Independent samples each octave at the original coordinates.
// @if _Fractal != 0
// @param float _Octaves = 3 [1 .. 8] // Rounded to an integer octave count.
// @param float _Lacunarity = 2 [1 .. 4] // Frequency multiplier per octave.
// @param float _Gain = 0.5 [0 .. 1] // Amplitude multiplier per octave.
// @endif
// @endgroup
// @group(Output; _Output)
// @param hidden enum _Output = PackedRG {PackedRG: 0, SignedRG: 1} // Packed RG: Neutral 0.5 in Displacement Map. Signed RG: Neutral 0 and half the X/Y strengths for the same displacement. Use HDR Color Range to retain signed/out-of-range values.
// @param bool _FlipX = false
// @param bool _FlipY = false
// @endgroup

float4 ApplyFX(float2 uv, float4 color)
{
    float2 displacement = 0.0;
    if (_Strength != 0.0)
    {
        fnl_state warp = fnlCreateState((int)round(_Seed));
        warp.domain_warp_type = (int)round(_WarpType);
        warp.domain_warp_amp = _Strength;
        warp.frequency = _Scale;
        warp.fractal_type = (int)round(_Fractal);
        // In single-warp mode hidden fractal values must not attenuate amplitude.
        warp.octaves = _Fractal < 0.5 ? 1 : (int)clamp(round(_Octaves), 1.0, 8.0);
        warp.lacunarity = _Lacunarity;
        warp.gain = _Gain;
        float2 original = uv + _Offset;
        float2 warped = original;
        fnlDomainWarp2D(warp, warped.x, warped.y);
        displacement = (warped - original) * float2(_FlipX > 0.5 ? -1.0 : 1.0, _FlipY > 0.5 ? -1.0 : 1.0);
    }
    // Raw displacement, not warped coordinates or a unit direction. Affine
    // packing preserves magnitude and intentionally permits values outside 0..1.
    float2 encoded = _Output < 0.5 ? displacement * 0.5 + 0.5 : displacement;
    return float4(encoded, 0.0, 1.0);
}
