# Gradient GPU validation

Run `GradientGpuSmoke.cs` through the connected Unity Pipeline `eval_file` command.
It creates only transient objects and compares GPU output with the retained CPU thumbnail
generator. No user documents, scenes, assets, or Undo state are changed.

Coverage: six coordinate shapes; Blend, Fixed, PerceptualBlend; Gamma/Linear gradient
interpolation; narrow color-key intervals; independent alpha keys; signed HDR; odd-sized
angular seams and exact center; Ping Pong; zero radius/repetitions; null gradient fallback;
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
