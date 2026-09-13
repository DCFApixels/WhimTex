# Noise validation

Offline checks, without compiling or opening Unity:

```text
node Tests~/NoiseContract.test.mjs
```

`NoiseApiSmoke.cs` and `NoiseSmoke.cs` are opt-in C# snippets for the connected Editor,
after compilation through the Unity Editor pipeline (never standalone MSBuild/dotnet).
The API snippet checks partial settings, full signed seed range, validation, discovery and round trips.
The GPU snippet creates only a small transient document and textures; it checks all eight algorithms and
four noise fractals, seed determinism, high-bit seed precision, domain warp, inversion, color encoding,
opaque grayscale and matching sample coordinates at different resolutions. White Noise checks also cover
independent RGB channels, mean/correlation sanity checks, pixel grain size, pixel offsets, 1D bands,
inversion, color encoding and bypassing fractal/warp. It does not save assets.

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
