# Height preview playground

Status: isolated experiment; no production preview integration.

The catalog profile `height-preview-playground` runs a smoke case followed by five
source/output combinations: 512→128, 512→256, 8192→128, 8192→256 and 8192→512.
Use the normal reviewed runner; allow `assets,temp-files`. Run serially in the matching
Editor. No Player build, windows, OS input, user documents or settings are required.

## Implementations

- Procedural raster grid: 64/128/256/512 cells per axis, positions computed from
  `SV_VertexID`, no vertex/index buffers, hardware depth testing.
- Indexed raster grid: identical triangles, a persistent Mesh per density; uploaded
  once, with `UploadMeshData(true)`. Creation/upload time and Unity-reported memory are
  separate from warmed draw time.
- Serial Floating Horizon: one compute invocation per screen column, near-to-far scan.
- Parallel Floating Horizon: one group of 64 invocations per screen column. A portable
  shared-memory prefix min/max scan exposes non-overlapping visible spans. Upper and
  lower horizons retain back-facing portions of the open surface, including signed
  heights. There are no hardware-specific intrinsics or atomics.

The camera is orthographic at 45 degrees, aligned with a map axis. Lighting, texture
filtering, projection and height scale are shared. This is an open opaque heightfield,
without walls, cast shadows, AO, transparency holes or multisampling. Arbitrary camera
rotation is not covered. Both algorithms are sampled; a small step count can miss a
thin peak, regardless of output resolution.

## Inputs and quality

Six deterministic fixtures: flat, smooth hills, steps/circular plateau, a four-source-
pixel diagonal ridge with a small peak, multiscale noise, and signed/HDR heights.
One RHalf source is generated on the GPU and reused across methods. Each case also
tests the same source after a bilinear blit to output resolution. This reduction is
not an area-average filter or conservative maximum pyramid and can lose narrow peaks.

Reference images use parallel Horizon at `2*sourceSize` depth intervals. For this camera
this visits every vertical texel-center breakpoint of the bilinear source. The reference
shares code with one candidate: it is **not** an independent golden image. Independent
double-precision CPU ray/segment intersection checks eight selected screen columns
against the same GPU-sampled heights (at most 8×16384 floats), including coverage and
geometric height. CPU comparison does not reread the full 8K source. Dense 1024/2048-cell
raster grids record convergence; serial/parallel Horizon must also agree.

The numerical output stores geometric height, illumination, source depth and coverage
in RGBAFloat. PNGs visualize illumination with the same tint. Comparisons report coverage
IoU and height/lighting MAE on pixels covered by both renders. The reference has no
subpixel coverage integration, so it does not establish antialiased accuracy for details
narrower than a screen pixel across the viewing columns.

## Timing and memory

Nine warmed batch samples, with alternating method order and two discarded warmups.
Ordinary batches contain 32 renders; the maximum-depth reference workload uses 8.
Each batch ends with a separate 1×1 completion readback, and elapsed time is divided by
the batch count. These numbers include amortized submission and completion overhead;
they are **not isolated GPU timestamps, displayed FPS, or a painting regression test**.
Batch command construction, reference validation and PNG capture are outside timing.
The smaller reference batch has a larger amortized completion overhead.

Source generation, first measured configuration draw, source reduction and Mesh setup
have separate fields. The first configuration draw can reuse already-warmed shaders;
it is not a process-cold startup measurement.
`reduceMs` includes a whole synchronous completion; `reduceBatchMs` is the warmed
32-blit throughput estimate. Source generation is test fixture preparation, not a cost
the proposed preview must incur when the source already exists.

Logical source memory is 128 MiB for 8192² RHalf and 0.5 MiB for 512². Output RGBAFloat
costs 1 MiB at 256²; rasterization additionally needs about 0.25 MiB of depth storage.
Parallel Horizon uses 520 bytes of group-shared storage per group, with no persistent
geometry buffer. `geometryBytes`/`outputUnityBytes` are Unity Profiler accounting, not an
independent physical VRAM measurement. The benchmark retains both output targets and
several grid densities simultaneously; their total is not the memory requirement of
one chosen implementation. Ordinary RGBA/Half input bandwidth is not measured here.

## Results, 2026-11-09

Unity 6000.7.0a6, Direct3D12, multithreaded rendering, GeForce RTX 5060.
All five final cases passed: GPU diagnostics, flat-plane agreement, CPU ray checks,
serial/parallel agreement, finite HDR output and cleanup. 990 measurement rows were
retained under project `Temp/WhimTex/height-preview`; no production rendering changed.

At 8K input and 256² output, per-pattern medians were approximately:

| Candidate | Time per render | Main observation |
| --- | --- | --- |
| Indexed 128² grid | 0.023–0.024 ms | Cheapest coarse representation; misses thin ridges |
| Procedural 256² grid | 0.045–0.070 ms | More detail, still limited by grid density |
| Parallel Horizon, 512 steps | 0.023–0.047 ms | Better rough-surface detail at similar cost |
| Parallel Horizon, 2048 steps | 0.040–0.059 ms | Better depth sampling; four-pixel 8K ridge still undersampled |
| Parallel Horizon, 16384 steps | 0.227–0.322 ms | Preserves the fixture's narrow ridge along depth |

These are one-device results for a fixed camera. They favor parallel Horizon for the
tested quality/cost tradeoff, but do not establish its superiority for freely rotated
views, other GPUs, many simultaneous previews, RGBA input textures or live painting.

## Viewing and reproducing

`HeightPreviewReport.mjs <output.html> <report.json>...` creates a comparison page with
rendered images, aggregate timings, CSV and links to raw reports. Pass explicit completed
report paths; it rejects partial reports. The final run receipt is
`Temp/WhimTex/test-runs/2026-11-09T05-17-23.663Z-16680.json` in this workspace.
Use the normal runner review step for a new machine/run; do not reuse old fingerprints.
