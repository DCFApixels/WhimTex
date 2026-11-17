# Path blur density and Spin study

2026-11-17, Unity 6000.7.0a6, RTX 5060, Direct3D11, 256×256 RGBAFloat.

## Behavior

- Directional Radius retains the half-path in document pixels; Spin retains ±Angle degrees in document-pixel space; Zoom retains factors 1±abs(Strength).
- Standard half-extent=max(1,ceil(half-path length in document pixels)); High doubles it. Sample count=2*extent+1 with normalized exp(-2*t*t) weights. No fixed cap or implicit Buffer.
- Spin evaluates symmetric pairs, sharing their Gaussian weight and rotation. Incremental rotation reanchors with sincos every 32 steps. The sample set and weight profile are unchanged.
- RGBA components remain independent, signed and HDR. Explicit alpha operations remain available. The two filters retain native HLSL fallback and SVM continuation downstream of a prepared Buffer. No new VM opcodes; Bilateral is unchanged.
- Stored filterQuality is Standard/High, independent of Radial option Spin/Zoom. Samples is no longer a valid input; incompatible documents receive the existing unknown-input diagnostic. No migration is installed.

## Setup and limits

The texture has thin HDR red lines, a green checker, a narrow blue Gaussian and a signed alpha ramp. The procedural source is unfiltered 2D Gabor at Scale=24, followed by three FBm octaves (Lacunarity=2, Gain=.5). Buffer uses canvas resolution, bilinear sampling and Clamp. Directional uses Radius=32 and Angle=27°; Spin uses Angle=20°; Zoom uses Strength=.3. Standard has 65 Directional samples, High 129; radial counts vary by pixel.

The original 17-sample shaders were captured **before production edits**, together with graphs and SHA-256 digests. Comparison replays those authenticated shader sources with matching material bindings in the same session as the new shaders. It also renders a four-times-Standard-density direct-trigonometry reference, and direct-trigonometry Standard/High variants to isolate the Spin optimization. This dense reference is a numerical convergence target, not an exact continuous integral. Buffer and direct paths each have their own matching reference; Buffer resampling is not claimed equivalent to direct procedural evaluation.

Final-pass timing uses two warmups and nine rotating batches of two renders, followed by a one-pixel synchronized readback. These are CPU submission plus GPU-completion timings, **not GPU timestamp queries or shader-compilation timings**. Values near 0.1 ms are sensitive to synchronization overhead. Full refresh uses five renders with owned Buffer caches explicitly invalidated, including Buffer preparation. No user document, settings or assets are changed.

## Final pass, milliseconds (median)

Old shaders and new shaders are interleaved in the same measurement run.

| Input | Mode | Old 17 | Standard | High |
|---|---|---:|---:|---:|
| Texture | Directional | 0.162 | 0.339 | 0.602 |
| Texture | Spin | 0.145 | 0.377 | 0.654 |
| Texture | Zoom | 0.171 | 0.360 | 0.602 |
| Texture + Buffer | Directional | 0.092 | 0.127 | 0.175 |
| Texture + Buffer | Spin | 0.094 | 0.145 | 0.211 |
| Texture + Buffer | Zoom | 0.099 | 0.138 | 0.197 |
| Gabor + Fractal | Directional | 1.559 | 5.524 | 10.575 |
| Gabor + Fractal | Spin | 1.566 | 6.901 | 13.260 |
| Gabor + Fractal | Zoom | 1.521 | 5.885 | 11.310 |
| Gabor + Fractal + Buffer | Directional | 0.084 | 0.119 | 0.161 |
| Gabor + Fractal + Buffer | Spin | 0.105 | 0.144 | 0.204 |
| Gabor + Fractal + Buffer | Zoom | 0.084 | 0.123 | 0.167 |

Automatic density trades extra work for fewer sparse trails. In these long-path tests, direct Gabor/Fractal became several times more expensive than 17 samples; High roughly doubles Standard cost. With an explicit Buffer the filtering pass remains cheap, although preparing or refreshing that Buffer still costs time.

## Full refresh, milliseconds (median)

These measurements include Buffer preparation, runtime binding and synchronization. Old/new are separate capture runs; small differences at sub-millisecond scale should not be read as speedups.

| Input | Mode | Old 17 | Standard | High |
|---|---|---:|---:|---:|
| Texture | Directional | 0.429 | 0.482 | 0.678 |
| Texture | Spin | 0.360 | 0.551 | 0.778 |
| Texture | Zoom | 0.285 | 0.586 | 0.721 |
| Texture + Buffer | Directional | 0.258 | 0.326 | 0.330 |
| Texture + Buffer | Spin | 0.360 | 0.295 | 0.334 |
| Texture + Buffer | Zoom | 0.237 | 0.291 | 0.376 |
| Gabor + Fractal | Directional | 1.714 | 5.736 | 11.352 |
| Gabor + Fractal | Spin | 1.687 | 7.500 | 15.358 |
| Gabor + Fractal | Zoom | 1.727 | 6.632 | 12.233 |
| Gabor + Fractal + Buffer | Directional | 0.345 | 0.355 | 0.374 |
| Gabor + Fractal + Buffer | Spin | 0.340 | 0.521 | 0.433 |
| Gabor + Fractal + Buffer | Zoom | 0.429 | 0.350 | 0.432 |

## Quality against the dense reference

RMSE over the central 192×192 pixels, all four unclamped channels. PNG sheets show full images in this order: **old 17 / Standard / High / dense reference**. Their display clips negative/HDR values, while numerical comparisons use Float data.

| Input | Mode | Old 17 RMSE | Standard RMSE | High RMSE |
|---|---|---:|---:|---:|
| Texture | Directional | 0.039992 | 0.003880 | 0.000547 |
| Texture | Spin | 0.054424 | 0.003700 | 0.000998 |
| Texture | Zoom | 0.037908 | 0.004181 | 0.001068 |
| Texture + Buffer | Directional | 0.039992 | 0.003880 | 0.000547 |
| Texture + Buffer | Spin | 0.054424 | 0.003700 | 0.000998 |
| Texture + Buffer | Zoom | 0.037908 | 0.004181 | 0.001068 |
| Gabor + Fractal | Directional | 0.021910 | 0.001877 | 0.000076 |
| Gabor + Fractal | Spin | 0.016515 | 0.001541 | 0.000153 |
| Gabor + Fractal | Zoom | 0.014072 | 0.001651 | 0.000184 |
| Gabor + Fractal + Buffer | Directional | 0.016540 | 0.000719 | 0.000298 |
| Gabor + Fractal + Buffer | Spin | 0.012481 | 0.000936 | 0.000248 |
| Gabor + Fractal + Buffer | Zoom | 0.009883 | 0.001032 | 0.000268 |

Standard reduces error roughly 9–15× on the texture and 9–12× on the direct procedural branch. High reduces the remaining error further. It does not recover detail already lost by the source or an undersized Buffer.

## Isolated Spin optimization

Same density, same sample positions, direct trigonometry versus paired incremental rotation:

| Input | Standard direct → optimized, ms | High direct → optimized, ms |
|---|---:|---:|
| Texture | 0.373 → 0.377 | 0.662 → 0.654 |
| Texture + Buffer | 0.156 → 0.145 | 0.235 → 0.211 |
| Gabor + Fractal | 6.896 → 6.901 | 13.370 → 13.260 |
| Gabor + Fractal + Buffer | 0.158 → 0.144 | 0.217 → 0.204 |

Maximum per-component deviation from direct trigonometry at the same density: 0.00009036. The buffered cases improved about 6–10%; direct procedural evaluation dominates so its speed difference is negligible. This is not a general large speedup for Spin.

## Validation and reproduction

- `shader-path-blur`: 24 analytic CPU-quadrature GPU cases across both qualities; zero, fractional, negative and connected controls; all three paths; six hybrid Buffer→SVM continuation cases; JSON, discovery and compiler fingerprint invalidation.
- Integration: composite, isolated/pass-through groups, hidden Layer input, clipping, thumbnail and raster export; caller render state restored.
- UI: six native Quality controls, bounds, mode changes, Adaptive visibility for existing Sharpen, JSON persistence. Radial Mode and Quality remain independent.
- `shader-path-blur-comparison`: all four source configurations passed with clean diagnostics and finite output. Maximum Spin parity error is reported above.
- Current receipts: project `Temp/WhimTex/test-runs/path-blur-check2.json` and `path-blur-after2.json`. Raw reports/images and authenticated sources: `Temp/WhimTex/PathBlurStudy/`. Temporary outputs are intentionally not committed.
- The diagnostic baseline profile is only for the old fixed-Samples implementation before its edit. Comparison requires those retained authenticated files; it does not reconstruct an old result with the new compiler. Regression tests do not require those historical files.
