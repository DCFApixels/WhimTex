# Filled 8K preview: reuse draw bindings — 2026-11-17

The renderer now keeps unchanged texture/map bindings while recording one tile
command buffer. The first visible tile always writes every binding; the next
Render clears the property block and starts again. Texture references are cleared
in finally. The shader, draw order/count, tile ownership, filtering and compositing
calculations are unchanged. There is no persistent per-tile cache or extra GPU
resource: only two fixed eight-entry CPU arrays per surface.

This follows the [stage audit](TiledDrawingPreviewBenchmark.md). It removes repeated
native SetTexture/SetVector calls, reuses the black-texture reference within one
render and uses a property ID for the tile key. Exact Vector4.Equals is intentional;
approximate vector equality must not suppress a changed map.

## Comparison

Same fixture and hardware as the audit: Unity 6000.7.0a6, RTX 5060, D3D12,
8192² filled signed HDR Drawing, 1024² output. Full view covers the whole canvas;
native view covers a centered 1024² source region. Measurements cover actual
RenderCanvasViewport during pending paint, not input-to-screen latency or the
entire painting operation.

Sequence: baseline plain/sync, candidate plain/sync twice, restored baseline
plain/sync, then candidate restored and compiled. Only the owned renderer file was
swapped, guarded by its expected hash; no Git reset. Two runs per version/mode,
four warmup and 24 measured events per workload/view/run: **48 samples per cell**.
The fixture hash is identical in all eight comparison runs. Neighboring Unity
work was paused during capture. Medians in milliseconds:

| Workload | Full CPU before → after | Native CPU before → after |
|---|---:|---:|
| Idle repaint | 9.231 → 5.772 | 1.957 → 1.735 |
| Brush 81 | 9.395 → 6.115 | 1.579 → 1.548 |
| Brush 2048 | 9.109 → 6.047 | 1.674 → 1.858 |
| Erase 2048 | 9.123 → 6.050 | 1.677 → 1.809 |
| Stamp Multiply 1024 | 9.419 → 5.805 | 1.644 → 1.595 |

Full-view CPU medians fall by 34–38%. Native-view CPU calls do **not** consistently
improve: Brush/Erase 2048 increase by .18/.13 ms in the alternating-view sequence.
Do not report a general zoomed-view speedup or hide these increases.

Separate synchronized runs drain prior work with the same view, then measure
RenderCanvasViewport plus a one-pixel readback. They include CPU, render-thread
scheduling, GPU completion and readback, **not pure GPU time**:

| Workload | Full completion before → after | Native completion before → after |
|---|---:|---:|
| Idle repaint | 11.543 → 7.049 | 2.191 → 1.825 |
| Brush 81 | 11.114 → 7.631 | 2.111 → 1.937 |
| Brush 2048 | 11.383 → 7.627 | 2.186 → 2.032 |
| Erase 2048 | 11.006 → 7.763 | 2.119 → 2.044 |
| Stamp Multiply 1024 | 11.329 → 7.583 | 2.210 → 2.054 |

These completion medians improve for both views. CPU/GPU overlap differs in the
ordinary alternating sequence; it is a possible explanation for the small native
CPU increases, not a proven attribution. Native GPU timestamps remain unusable on
this Editor/device, as documented in the original audit.

Tail latency is not uniformly better. Full Brush 2048 p95/max falls from
11.17/12.14 to 7.18/7.86 ms, but the first candidate Erase run contains two CPU
outliers at 23.95/25.18 ms without a collection inside those timed calls. Combined
Erase p95/max therefore rises from 10.29/11.69 to 12.70/25.18 ms. The second
candidate run did not repeat these spikes. One measured synchronized native Brush
81 event includes a GC collection (11.14 ms total); no samples were filtered out.
The isolated spikes' cause is unverified; this optimization does not guarantee
lower worst-case latency.

## Correctness and memory

- All ten whole-output 1024² RGBAFloat hashes match across all eight comparison
  runs; 2288 fixture checks passed. Source and rendered HDR retain negative and
  above-one components.
- GPU pool peak is unchanged: idle 512 MiB, Brush 81 520 MiB, large paint/erase
  and later Stamp Blend 576 MiB. The shared pool remains warm across workloads.
  The new fixed CPU arrays hold eight references and eight Vector4 values;
  their payload is 192 bytes on a 64-bit runtime, plus array/object field overhead.
  There are no new per-tile/per-frame arrays or textures.
- `tiled-drawing-view` now checks Point/Bilinear, clipping, affine/projective/zoom,
  distinct pages, missing tiles, pending masked HDR ink, protected channels,
  Cancel, Commit and snapshot Undo/Redo against a dense regional reference.
  Every render also checks caller state and unchanged GPU allocation. It passed
  before and after the optimization (216 checks each).
- Final `tiled-drawing-consumers`, `tiled-drawing-native-raster` and
  `tiled-paint-readiness-core` also passed: composition, groups, Target/Layer input,
  clipping, thumbnails/export, native brush/Pencil exact Half output, masks,
  signed HDR, exact retained history, memory-cap and resource-lifetime checks.
  Receipt: `Temp/WhimTex/test-runs/preview-bindings-regressions2.json`.
  Together with the view case, four suites passed 672 checks.

A separate final `bindings-after-control` run passed another 286 checks and all
ten image hashes. It is not pooled into the balanced comparison. Full Brush/Erase
2048 medians were 4.72/4.68 ms, maxima 5.65/5.00 ms; the earlier full-view spikes
did not recur. One native Erase event had a GC collection and took 10.03 ms.
Different absolute timings reinforce that these captures do not bound Editor
tail latency or eliminate background-load variation.

The native storage/history suites are broader regressions, not fresh end-to-end
8K journal benchmarks. Multi-document timing, other GPUs/backends and live pointer
input latency were not measured. No user document or other chat's source was changed.

## Evidence

Raw runs are in project `Temp/WhimTex/filled-preview/`:
`bindings-before-plain2/3`, `bindings-before-sync1/2`,
`bindings-after-plain1/2`, `bindings-after-sync1/2` (all `.json`).
`Temp/WhimTex/preview-bindings/comparison.json` records medians/p95/max, GPU pool
sizes and source hashes; `compare.py` aggregates the paired runs without removing
outliers. The ordinary analyzer also validates the shared fixture and image hashes.

Baseline renderer SHA-256: `5c1def16ea48fb7a504ea8e5d9fc99f14d9e7752694b7535dfb1d3c78e34dd42`.
Candidate: `e38c6d8a91ee45af32092c75d15a25cbc7e8fd45c91488b736dc29a5eb8cfa06`.
Measured fixture: `38c2942c41113e845788a668146b233ca1429fb3e0c5524da30327b7f50fa140`.
After all captures, the fixture gained Play Mode guards at setup and between
frames, outside the measured region. Its current file hash therefore differs.
The timing body is unchanged; future before/after pairs must use one fixture hash.

Excluded: `bindings-before-plain1` overlapped a neighboring import-error state;
the first expanded-view test used pixel bounds where the batch contract requires
source UV and hit the owned surface budget. It was corrected before baseline
validation. `preview-bindings-regressions.json` was stopped by preflight while the
user was in Play Mode; no test body ran. Testing resumed after the user stopped Play.
