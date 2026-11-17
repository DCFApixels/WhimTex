# Bilateral Blur: density and arithmetic study

2026-11-17, Unity 6000.7.0a6, NVIDIA GeForce RTX 5060, Direct3D12.

## Change

- Standard retains the existing full square and sample positions:
  extent=max(1,ceil(abs(Radius))), (2*extent+1)^2 samples.
- Optional High doubles the extent, retaining Standard samples and halving spacing
  in both axes. Radius, Tolerance and Source/Custom guide semantics are unchanged.
- The positive-tolerance spatial and similarity Gaussian weights use one exponential
  of their summed exponents. Zero tolerance still accepts exact guide matches only.
- Row-invariant arithmetic was also trialed, but did not provide a consistent benefit;
  production keeps the simpler fused expression and existing coordinate calculation.
- No implicit texture preparation, separable approximation, range quantization,
  clamping, premultiplication or native Bilateral SVM opcode was introduced.

## Measurement

Owned transient graphs render 128x128 ARGBFloat. Three sources: an HDR texture with
sharp edges and signed alpha; Gabor -> FBm (3 octaves) evaluated at every sample;
the same procedural source behind an explicit canvas-resolution Buffer. Both guide
modes are tested; Custom uses UV.x. Radius is 2, 8 or 16, Tolerance=0.2.

Baseline generated shader sources and graphs were captured before production edits,
then SHA256 checked and compiled alongside the new shaders. There is no cache reset
or user document mutation. Timings measure a final-pass draw plus a 1x1 synchronous
readback: they include CPU submission and synchronization, and are **not isolated GPU
hardware timings**, compilation times or full editor update latency. Buffer production
is prepared outside the timed draw; these numbers do not include its first/update cost.

Trial and after runs used seven rotating timing rounds, including High and a denser
reference. Because those expensive draws can alter GPU load, the final timing run
alternates only old and new Standard for 31 rounds per scenario after warmup. Small
values are dominated by synchronization and normal measurement variation. Most timing
intervals overlap; the observed gains should not be treated as guaranteed speedups.

## Paired Standard timings

Median milliseconds (31 draws each). Source=guide from In; Custom=UV.x guide.

| Input | Guide | Radius | Before | After | Before / after interquartile ranges |
| --- | --- | ---: | ---: | ---: | --- |
| Texture | Source | 2 | 0.268 | 0.267 | 0.250–0.285 / 0.233–0.292 |
| Texture | Source | 8 | 0.568 | 0.567 | 0.557–0.613 / 0.561–0.595 |
| Texture | Source | 16 | 1.533 | 1.532 | 1.499–1.592 / 1.489–1.628 |
| Texture | Custom | 2 | 0.220 | 0.233 | 0.213–0.251 / 0.210–0.256 |
| Texture | Custom | 8 | 0.577 | 0.567 | 0.549–0.607 / 0.545–0.614 |
| Texture | Custom | 16 | 1.539 | 1.545 | 1.486–1.584 / 1.497–1.866 |
| Gabor + Fractal | Source | 2 | 1.100 | 1.072 | 1.063–1.160 / 1.038–1.198 |
| Gabor + Fractal | Source | 8 | 9.422 | 9.122 | 8.810–9.784 / 8.470–9.572 |
| Gabor + Fractal | Source | 16 | 34.305 | 32.539 | 32.814–36.157 / 31.593–34.178 |
| Gabor + Fractal | Custom | 2 | 1.035 | 1.046 | 1.008–1.055 / 1.009–1.069 |
| Gabor + Fractal | Custom | 8 | 9.528 | 9.290 | 8.938–9.842 / 8.809–9.802 |
| Gabor + Fractal | Custom | 16 | 33.118 | 33.004 | 32.739–35.240 / 32.683–35.312 |
| Explicit Buffer | Source | 2 | 0.215 | 0.215 | 0.199–0.239 / 0.203–0.245 |
| Explicit Buffer | Source | 8 | 0.286 | 0.279 | 0.263–0.325 / 0.265–0.303 |
| Explicit Buffer | Source | 16 | 0.457 | 0.463 | 0.447–0.479 / 0.453–0.503 |
| Explicit Buffer | Custom | 2 | 0.205 | 0.205 | 0.178–0.262 / 0.176–0.326 |
| Explicit Buffer | Custom | 8 | 0.264 | 0.266 | 0.259–0.302 / 0.260–0.293 |
| Explicit Buffer | Custom | 16 | 0.471 | 0.473 | 0.451–0.517 / 0.455–0.519 |

The largest tested procedural Source case changes from 34.305 to 32.539 ms (about
5%). Texture and already-buffered cases remain essentially unchanged. Repeated input
branch evaluation dominates; this algebraic simplification is a small improvement.
The explicit Buffer comparison is not a result-preserving automatic optimization:
it rasterizes a continuous procedural field and therefore changes off-grid samples.

## Density and image checks

The density reference uses four times the Standard extent, rather than an analytic
continuous-filter oracle. RMSE uses all four unclamped float channels in the central
96x96 pixels. A denser reference indicates convergence, not physical ground truth.
At Radius=8, Standard/High/reference use 289/1089/4225 source samples per output pixel.
The following are from the seven-round after run, not the separate paired timings.

| Input | Guide | Standard RMSE | High RMSE | Standard ms | High ms |
| --- | --- | ---: | ---: | ---: | ---: |
| Texture | Source | 0.045517 | 0.030241 | 0.551 | 1.494 |
| Texture | Custom | 0.001665 | 0.000624 | 0.580 | 1.599 |
| Gabor + Fractal | Source | 0.002581 | 0.000323 | 9.719 | 32.490 |
| Gabor + Fractal | Custom | 0.002569 | 0.000140 | 9.889 | 33.317 |
| Explicit Buffer | Source | 0.012878 | 0.002248 | 0.279 | 0.455 |
| Explicit Buffer | Custom | 0.000310 | 0.000115 | 0.278 | 0.440 |

High reduces error in every tested case, but can cost almost four times as much on
an expensive procedural branch. Keep Standard as the default. High is useful for
fine detail and narrow guide transitions, rather than being a speed optimization.

Production Standard differs from the frozen baseline by at most 5.96e-7. All
production High results also match an independently densified baseline within 1e-4.
Rendered contact sheets were inspected for the HDR texture and procedural pattern;
PNG display clips signed/HDR values, whereas numeric comparisons use full floats.

## Regressions and reproduction

`shader-bilateral` passed 24 GPU cases: 20 native HLSL fallback and four explicit
Buffer cases with SVM continuation. Independent CPU quadrature covers signed HDR,
alpha, Source/Custom guides of different widths, zero/negative/fractional/varying
Radius and zero/negative Tolerance. Maximum CPU error: 2.88e-6; HLSL/SVM difference: 0.
Integration verifies composite, pass-through/isolated groups, hidden Layer input,
clipping, thumbnails, raster export and caller render state. The UI test checks all
seven Quality controls, default/selection, mode independence, label bounds and JSON.
Total regression checks: 279304. Shader diagnostics were clean.

Profiles in `Tests~/scripts/test-catalog.json`:
- `shader-bilateral-trial`: only before production edits; captures immutable baseline
  graphs/sources, trials arithmetic, and compares sampling densities.
- `shader-bilateral-after`: requires those authenticated captures; compares actual
  Standard/High to baseline and the dense reference.
- `shader-bilateral-timing`: requires the same captures; 31 alternating old/new draws.
- `shader-bilateral`: repeatable current-behavior regression suite without captures.

Use the runner's review fingerprint and the normal sequential Unity procedure.
Diagnostics require `--allow-effects temp-files`. The study source is
`ShaderGraphBilateralStudy.cs`; regressions use `ShaderGraphImageFiltersTests.cs` and
`ShaderGraphFilterQualityUiTests.cs`. Raw measurements, contact sheets and baseline
hashes are project-local under `Temp/WhimTex/BilateralStudy/`. Run receipts are
`Temp/WhimTex/test-runs/bilateral-{trial,after,timing,regression}.json`.
