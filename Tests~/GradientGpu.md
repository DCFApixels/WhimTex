# Gradient GPU validation

Rounded is built in; there is no Transition setting or migration. Retired transition source tests are preserved in
`ArchivedTransitions/*.cs.txt` for research only and are not current executable tests.

Run `GradientGpuSmoke.cs` through the connected Unity Pipeline `eval_file` command.
It creates only transient objects and compares GPU output with the retained CPU thumbnail
generator. No user documents, scenes, assets, or Undo state are changed.

Coverage: six coordinate shapes; Blend, Fixed, PerceptualBlend; Gamma/Linear gradient
interpolation; narrow color-key intervals; independent alpha keys; signed HDR; odd-sized
angular seams and exact center; Ping Pong; zero repetitions; null gradient fallback;
palette reuse across geometry/size changes; thumbnail mode invalidation; settings-copy
mode/color-space preservation and hash invalidation; resource release.

Native PerceptualBlend quantizes RGB. Palette interpolation can differ by one encoded
8-bit step; its comparison tolerance is .01 in linear light (other modes: .002 relative,
including the compositor's existing half-float output stage). This is not bit-exact rendering.

Verified in Unity 6000.7.0a6 / DX12: 394,765 checks passed. Largest observed absolute
channel difference: .00831, within the PerceptualBlend tolerance.

## Performance spot check

Transient document with one default Gradient, warmed `RenderCachedPreview`, six samples.
These are local measurements, not portable frame-rate guarantees:

| Size | Main-thread render call | Call plus blocking one-pixel GPU readback |
| --- | --- | --- |
| 512 × 512 | .086–.136 ms | 1.32–2.90 ms |
| 1024 × 1024 | .086–.146 ms | 4.36–9.38 ms |

Before this change, the same 512 × 512 document took 85–98 ms per render call.
The old CPU generator alone took 356–389 ms at 1024 × 1024.
No full-resolution gradient cache is retained. The encoded RGBAFloat palette uses
256 samples per interval: 4 KiB for a two-key default gradient, at most 68 KiB of texel
data per CPU/GPU copy. Rebuilding occurs only on gradient key/mode/color-space changes.

`node Tests~/GradientGpu.test.mjs` checks source-level hot-path/lifecycle contracts.

## Rounded transition (distributed-speed refinement of prototype 68)

Run these entry points with `unity command run_script --file <file> --entry <entry>`
and an explicit `--project-path` targeting the editor under test. Files are relative to
the project, under `Packages/com.dcfapixels.whimtex/Tests~`. No saved documents are edited.

| File | Entry | Verified checks |
| --- | --- | --- |
| WhimTexGradientContractSmoke.cs | WhimTexGradientContractSmoke.Main | 526,284: modes/spaces, midpoint extremes, smoothness, bounds, ignored legacy fields, serialization/clipboard, absence of Transition UI and Smoothness Undo/Redo, zero hot-path allocations |
| WhimTexGradientRoundedSmoke.cs | WhimTexGradientRoundedSmoke.Main | 68,512: analytic neutral formula, redundant stops, independent alpha, domain boundaries, peak clock speed below 1.19 |
| GradientGpuRunSmoke.cs | GradientGpuRunSmoke.Rounded | 2,858,482: shapes, GPU/CPU, cache; maximum absolute difference 0.008546 within existing Perceptual tolerance |
| GradientSoftFXSmoke.cs | GradientSoftFXSmoke.Main | 12,336: Rounded, FX sampling, rebind without shader recompilation, lossy export guard |
| WhimTexGradientPipelineSmoke.cs | WhimTexGradientPipelineSmoke.Main | 102,400: composition, groups, Specific Target, clipping, export |

`WhimTexGradientRoundedSmoke.Capture` writes 12,291 actual Editor samples to
`Temp/WhimTex/rounded-capture.csv`. Compared with the independent double-precision
prototype on two colored ramps and the radial-mask ramp, absolute RGB differences must
remain below 0.00001 (encoded RGB, before GPU/LUT quantization).
Rounded intentionally approximates stop values at interior held boundaries; tests must
not impose exact interpolation there. Old transition fields are ignored. Old appearance is not preserved; no migration/resave is required.
`WhimTexGradientRetiredFieldSmoke.Main` checks six binary snapshots captured before field removal:
6,174 checks verify direct loading, unchanged key data, Rounded samples, no load warnings, and no retired field in output.
The 12,291-sample capture before/after transition removal is byte-identical (SHA256
`639f32bb5e4dc928515f00531d58c6ecec77f1d4b87d39b06054f03ab52930aa`).
