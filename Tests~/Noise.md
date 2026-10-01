# Noise validation

Offline checks, without compiling or opening Unity:

```text
node Tests~/NoiseContract.test.mjs
```

`NoiseApiSmoke.cs` and `NoiseSmoke.cs` are opt-in C# snippets for the connected Editor,
after compilation through the Unity Editor pipeline (never standalone MSBuild/dotnet).
The API snippet checks partial settings, full signed seed range, validation, discovery and round trips.
The GPU snippet creates only a small transient document and textures and disables gradient mapping
to isolate the generator's original scalar/RGB output. It tests all eight algorithms,
four noise fractals, seed determinism, high-bit seed precision, domain warp, inversion, color encoding,
opaque grayscale and matching sample coordinates at different resolutions. White Noise checks also cover
independent RGB channels, mean/correlation sanity checks, pixel grain size, pixel offsets, 1D bands,
inversion, color encoding and bypassing fractal/warp. It does not save assets.

`NoiseRandomizeSmoke.cs` runs with Pipeline `run_script`, entry `NoiseRandomizeSmoke.Run`.
It tests 256 Random All combinations starting from all eight noise types, group-local type selection,
preservation of both 1D/2D modes, the gradient palette and selected Gradient output, other generator fields, enum coverage and legal ranges. Starting from either ColorValues or LinearData, both raw outputs must be reachable and Gradient must never be selected. Inverted can still vary in every mode.
It also checks 64 GPU renders, untouched layer settings and Unity random state. A temporary Properties window
checks real button activation, field refresh, one-step Undo/Redo and the seed-only Random button.
The window and document are destroyed afterwards; no project assets are saved.

`OptionalLayerGradientSmoke.cs` runs with Pipeline `run_script`, entry `OptionalLayerGradientSmoke.Main`.
It checks SDF/Noise Perceptual gradient defaults, Output, conditional Gradient visibility and persistent Inverted controls in both editors, all eight noise types,
HDR/alpha palette sampling against CPU evaluation, RGB grain fallback, inversion before palette sampling, raw linear SDF values and inversion,
clipboard/Unity serialization, retained explicit modes, thumbnail invalidation, cached/composite parity,
group/clipping paths, and graphics-state preservation. It uses temporary documents and a temporary UI
window, and writes comparison PNGs only to `Temp/WhimTex/optional-gradient-Noise.png` and
`Temp/WhimTex/optional-gradient-SDF.png`. Verified in Unity 6000.7.0a6 alongside the generator, SDF LUT,
SDF controls, default-gradient, Random All and procedural-thumbnail regression checks.

`BlueNoise.test.mjs` checks the baked 1D/2D tables: uniform per-channel histograms, low-frequency
power for scalar values and several binary thresholds, independent RGB and deterministic rank generation.
`BlueNoiseSmoke.cs` checks actual GPU sampling, Seed, RGB, inversion, grain/preview coordinates,
negative offsets, periodicity, 1D bands and resource recreation. It writes a temporary comparison PNG
under the project's Temp folder (white left, blue right), not an imported asset.

The tables and `GenerateBlueNoise.mjs` are WhimTex's own code/data, based on Robert Ulichney's
[void-and-cluster algorithm](https://cv.ulichney.com/papers/1993-void-cluster.pdf), with a periodic Gaussian
kernel (2D sigma 1.5, radius 6; 1D sigma 3, radius 18). No third-party code or texture is bundled.
`node Tests~/GenerateBlueNoise.mjs` prints reproducible C# source as JSON for `src/BlueNoiseData.cs`.
Generation is offline only; the Editor lazily uploads a shared immutable table, without mipmaps or sRGB
decoding. Resources are released before assembly reload and on quit. The shared GPU tables total 65 KiB.

## Independent scales, 3D slices and lattice periodicity

`NoisePeriodicGpuSmoke.cs` runs with Pipeline `run_script`, entry `NoisePeriodicGpuSmoke.Run`,
arguments `[type, dimensions, stress]`: type 0–5, dimensions 2 or 3, stress false or true.
Run all 24 combinations serially. The two `NoisePeriodicReference*.csv` fixtures contain
independent double-coordinate CPU results from the external periodic-noise feasibility prototype,
not readbacks from the shader under test. Ordinary cases cover X/Y/XY, all four fractals and
all four warp choices. Stress cases cover eight octaves, lacunarity 4, extreme offsets/scales,
and a separate strength-100 BasicGrid warp case. Tests also render exact opposing endpoints
and compare one-sided slopes, so texture wrap sampling cannot hide a generator seam.

`NoiseLatticePrecisionSmoke.Run` checks compensated lattice coordinates against double arithmetic.
`NoiseControlsSmoke.Run` checks linked/unlinked scales, ratio-preserving clamps, conditional XYZ
Offset, retained Z, grain exclusions, real UI callbacks, Z-dependent renders, serialization and
thumbnail invalidation. Periodic uses the shared Make Seamless square styling: opposite edges
toggle together, deselecting both pairs leaves the control enabled, and external value changes
refresh selection. Both run with Pipeline `run_script`. All objects are temporary.
`NoiseIntegrationSmoke.Run` additionally checks isolated/pass-through groups, clipping coverage
and a Specific target reading the changing Z slice of a nested Noise layer.

Verified on the connected Unity Editor/DX12:

- GPU/reference: 5,184 ordinary and 1,296 stress comparisons; maximum error below 0.0005.
- Lattice precision: 96 checks, maximum fractional-coordinate error below 1.2e-7.
- Controls/slices/cache: 121 checks; API: 40 checks; Random All: 6,465 checks.
- Existing Noise: 99,755 checks; White/Blue regression: 589,832 checks; thumbnails: 153 checks.
- Group/clipping/target integration: 2,051 checks.

These are correctness checks, not performance benchmarks or validation of every GPU backend.
Periodicity is available for the six lattice noises in 2D/3D only; Z stays non-periodic.
White/Blue and 1D retain their previous path. Small periods quantize; transformed layers or
downstream effects can break canvas tiling. Very high-frequency details can alias.

Manual interaction checks:

1. Add Noise Layer. Drag Scale, Seed and Offset labels continuously; the pattern must update before release.
2. Change fractal/algorithm; conditional controls should show/hide without replacing existing input elements.
3. Edit through Properties as well as Layer Settings. Undo/Redo should restore generator parameters and output.
4. Duplicate, nest in a group, transform, Swizzle and rasterize using the normal layer commands.
5. Save/reopen a document and verify that the seed/pattern is retained.

For a full-size performance measurement, explicitly render/export a 5000 x 5000 canvas with both simple
noise and Cellular + 8 octaves + Domain Warp. Record algorithm, device, dimensions and warm-up separately.
Do not treat the normal 512 px preview as a full-resolution benchmark. This implementation has no
automatic low/full-resolution refinement stage and makes no fixed frame-time guarantee.
