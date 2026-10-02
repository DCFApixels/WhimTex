# Small-scale periodic noise experiment

Initially an opt-in prototype; integrated into production on 2026-10-01. The runner now verifies
production against the approved per-octave period policy and checks joins on production materials.
It does not edit document settings or saved assets.
Run `NoiseSmallScaleExperiment.cs` with Pipeline `run_script`, entry
`NoiseSmallScaleExperiment.Run`, arguments `[0]` for OpenSimplex2 or `[1]` for OpenSimplex2S.
Target the intended connected Unity project explicitly. No standalone compilation is required.

## Candidate

The candidate removes the early 2D simplex base-period rounding. Each octave instead uses
`max(1, round(scaleAxis * lacunarity^octave / latticeUnitAxis))`. The lattice basis,
hash wrapping, compensated coordinate arithmetic and warp algorithm remain unchanged.
The prototype retains the previous period formula for before/after images and verifies
the candidate against the current production-prepared lattice, with zero difference in this run.
Material uniforms are copied explicitly, including integer shader uniforms, to ensure the
detached comparison uses the requested seed, octaves, fractal and warp settings.

At Scale 0.68 and Lacunarity 2.84, the three X/Y periods change from
`1x1, 3x3, 8x8` to `1x1, 2x1, 7x4`. This reduces the excessive high-frequency detail.
The base octave still has a minimum whole lattice cell; discrete period changes remain.
This is not continuous scaling below one cell, and single-octave noise cannot benefit from
removing rounding that happened before octave multiplication.

## Checks and images

Verified on the connected Editor on 2026-10-01:

- 144 configurations per noise type, 288 total: three scales, X/Y/XY, all four fractal choices,
  all four warp choices. Tests use independent X/Y scale values and finite GPU readback.
- Samples straddle the join at +/-1/65536. Largest second difference: 3.40e-6 for OpenSimplex2,
  2.38e-6 for OpenSimplex2S; no observed boundary discontinuity in these cases.
- This is not a benchmark or an exhaustive precision, 3D, extreme-warp or platform test.

PNG output is in the project's `Temp/WhimTex/noise-small-scale/` folder.
`type0-comparison.png` and `type1-comparison.png` have columns ordinary / current periodic /
candidate periodic, and rows Scale 0.25 / 0.5 / 0.68 / 1 from top to bottom.
Here "current" in the filenames means the old implementation, retained for comparison.
Each tile is also saved separately, plus 2x2 repeats of the candidate at Scale 0.68.
PNG output applies display gamma to the raw linear render, without contrast normalization.

The comparison uses seed -139361330, offset (723.9, -340.202026), PingPong with three octaves,
Lacunarity 2.84, Gain 1, Weighted Strength 0.61, PingPong Strength 1 and BasicGrid Warp 0.5.
It shows the noise generator alone, without the original document's other layers or blending.

The runner uses the default Warp Scale multipliers [1,1] in all comparison columns.
It compares period-rounding policies. Separate anisotropic Warp Scale coverage is provided
by NoiseWarpPeriodSmoke, not by the historical boundary measurements above.

## Integration caveat

Per-octave-only rounding also changes some existing 2D simplex patterns above Scale 1.
Integration regenerated both independent reference fixtures with the external double-coordinate
CPU model (`noise-3d-feasibility-lab/HighPrecisionField.cs`, `PeriodicNoise64.cs` and
`ExportGpuReference.cs`). The model's Period method now rounds width*frequency/unit directly,
away from zero, without base rounding. The adapted field source was staged as
`Temp/WhimTex/HighPrecisionFieldPerOctave.cs`; no GPU readbacks were used as expected values.
All 24 GPU/reference suites pass: 6,480 samples, maximum error below 0.0005.
