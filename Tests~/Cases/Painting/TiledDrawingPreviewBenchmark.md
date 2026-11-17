# Filled 8K preview audit — 2026-11-17

The main measured CPU expense is binding textures and parameters for many separate
tile draws. Showing the full canvas costs substantially more than showing a small
region, even though both outputs have the same pixel dimensions. **No production
optimization was made during this audit.** Temporary instrumentation was removed
byte-for-byte and the restored renderer compiled successfully through Unity.
The subsequent [binding-reuse optimization and before/after comparison](TiledDrawingPreviewBindings.md)
are recorded separately.

Native GPU timing was attempted but **did not return valid durations**. The report
separates CPU submission, instrumented CPU stages and synchronous completion waits.
None of them is labelled pure GPU execution time or screen/input latency.

## Setup

- Unity 6000.7.0a6, NVIDIA RTX 5060, Direct3D12, MultiThreaded device.
  `SystemInfo.supportsGpuRecorder == true`; no Graphics Jobs or project settings changed.
- Owned 8192×8192 Drawing, RGBAHalf source, all 1024 source tiles occupied.
  Exact uniform source `(2, .25, -.25, 1)`, retained signed HDR. No FX, selection,
  channel protection, transform rotation or user documents/windows in this test.
- Real `PrepareBufferedStroke`, `BeginStroke`, `PaintPoint`, `PaintSegment` and
  `RenderCanvasViewport`, with the Drawing supplied as the active painting source.
  Strokes are cancelled after each case; commit, journal and Undo cost are excluded.
- Brush 81/2048, Erase 2048, Stamp Multiply 1024, plus unpainted rendering control.
  Circle brush, Hardness .72, Spacing .05, ink `(.8, 3.5, -.3, .6)`.
  Each stroke moves 640 source pixels horizontally around the canvas center.
  Large brushes do not emit a new stamp on every input event at this spacing.
- Two 1024×1024 ARGBFloat viewport outputs: full 8192×8192 canvas at step 8;
  centered source rectangle `(3584,3584,1024,1024)` at step 1.
  Separate effect caches prevent view switching from invalidating the other case.
  Views alternate AB/BA order after the **same** paint event, driven by Editor updates.
- Four warmup events and 24 measured events per workload/view; setup, imports,
  hash readback and cleanup excluded. Timing wrappers use reflection into product
  APIs; no Unity internal API reflection, native timestamp plugin or external profiler.

## Ordinary CPU viewport cost

Restored, uninstrumented production, `preview-plain4`. Milliseconds; median / maximum.
These are main-thread wall times, including driver submission or waits incurred
inside the call, not GPU durations. No GC collection occurred inside these timed calls.

| Workload | Whole canvas | Native view |
|---|---:|---:|
| Unpainted control | 9.07 / 14.03 | 1.83 / 2.25 |
| Brush 81 | 8.98 / 11.69 | 1.77 / 2.05 |
| Brush 2048 | 9.74 / 12.20 | 1.86 / 2.29 |
| Erase 2048 | 9.62 / 11.99 | 1.58 / 2.41 |
| Stamp Multiply 1024 | 9.34 / 12.65 | 1.66 / 2.29 |

The earlier complete baseline `preview-plain3` measured about 9.48–11.68 ms for
painting/full and 1.72–2.05 ms for painting/native. This session variation is retained;
the measurements do not establish stable tail percentiles or a before/after speedup.

## CPU stage attribution

Timing-only patch in `preview-stages2`; Brush 2048, milliseconds, medians. Other
workloads showed the same dominant stage. Each stage counts only its own interval;
whole render/viewport values overlap them and must not be added to them.

| Stage | Whole canvas | Native view |
|---|---:|---:|
| Validate source and resources | 0.083 | 0.082 |
| Configure common material/transform | 0.016 | 0.015 |
| Collect tile keys and filtering neighbors | 0.237 | 0.237 |
| Test tile bounds against view | 0.560 | 0.476 |
| Bind tile textures and properties | **4.681** | **0.157** |
| Record draw/scissor commands | 0.604 | 0.026 |
| ExecuteCommandBuffer CPU call | **1.243** | **0.055** |
| Clear command buffer / restore state | 0.081 | 0.002 |
| Entire native tile Render | 7.628 | 1.092 |
| Entire document viewport | 7.765 | 1.235 |
| Candidate keys | 1089 | 1089 |
| Recorded tile draws | **1089** | **36** |

Candidate keys include neighboring cells needed for bilinear filtering. A full
source grid is 32×32; the extra border produces 33×33 candidates. The native view
still walks them all, then binds/draws only the intersecting 6×6 cells including
filter/conservative borders. These counts were constant throughout each matrix.

Binding currently clears a property block, sets the tile key and supplies four
neighbor pairs of source/ink textures and their maps for every visible tile. The
large full/native difference therefore occurs **after** candidate collection.
Early culling alone would not remove the full-view binding cost.

The earlier stage run `preview-stages1` measured 5.93 ms binding and 2.18 ms submission
for Brush 2048/full, versus .282/.093 ms native. Both runs agree about the expensive
stage, but absolute timings vary. Instrumentation itself adds per-tile timestamps,
local counters and branches. Stage medians come from another run and are not expected
to sum to the ordinary viewport median; the difference is not an optimization.

## GPU attempt and independent completion measurement

`preview-gpu1` brackets the unchanged tile command buffer with unique single-use
`ProfilerMarker` GPU samples and reads `ProfilerRecorder`. Warmup excluded, **all
240 viewport queries returned Count=1 / Value=0**. Two earlier warmup queries had
no sample; all remaining samples were zero. Twelve independent 2048² controls
(four repeats of 1/16/64 blits) also returned Count=1 / Value=0.

An independent `CustomSampler` + legacy `Recorder.gpuElapsedNanoseconds` probe used
continuous warmup/heartbeat rendering and `ProfilerRecorder.CopyTo(reset:false)`.
After 100 updates (5.12 seconds), the 1/16/64-blit scopes still reported zero through
both APIs. This rules out treating the initial zeroes as a fast render or merely
an early recorder reset. The current Editor/device combination did not provide
usable timestamps; the exact engine/driver cause was not established.

Official contracts: [GPU Recorder timings](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/profiling/recorder/gpuelapsednanoseconds)
and [CommandBuffer samples](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/rendering/commandbuffer/beginsample).
Advertised support alone did not validate the returned data here.

`preview-sync1` provides a different measurement: render and one-pixel readback to
drain prior work, then time another viewport call plus its one-pixel ReadPixels wait.
Milliseconds, medians; total is calculated per event before aggregation.

| Workload | Full: CPU + completion total | Native: CPU + completion total | Full: wait alone | Native: wait alone |
|---|---:|---:|---:|---:|
| Unpainted control | 11.12 | 1.97 | 2.16 | .69 |
| Brush 81 | 10.99 | 2.08 | 2.18 | .77 |
| Brush 2048 | 14.42 | 2.45 | 2.91 | .93 |
| Erase 2048 | 11.57 | 2.06 | 2.34 | .76 |
| Stamp Multiply 1024 | 11.07 | 2.02 | 2.22 | .77 |

The wait includes render-thread scheduling, outstanding GPU work and readback
overhead; GPU execution can overlap CPU work. Subtracting CPU from this total does
**not** recover pure GPU time. This deliberately synchronized sequence also differs
from normal asynchronous painting and must not be used as its FPS prediction.

## Validation, restoration and reproduction

- Four final matrices completed their fixtures: 1132 rows including warmups and
  GPU controls; 1429 checks. The GPU matrix completion confirms fixture execution,
  **not** valid GPU timing. Invalid query samples remain in the raw report.
- All ten complete 1024² **RGBAFloat** viewport hashes agree between instrumented,
  restored ordinary and synchronized runs. Signed HDR was checked on the untouched
  source; painted/erased corners are not incorrectly required to retain its values.
- Temporary production edit was confined to `TiledPaintSurface.Render.cs`.
  Restored SHA-256: `5c1def16ea48fb7a504ea8e5d9fc99f14d9e7752694b7535dfb1d3c78e34dd42`.
  Unity recompilation completed without errors, followed by ordinary/sync runs.
  Other chats' source, catalogs and documentation were preserved.
- No production code, shader, user setting, resource format or rendering behavior
  change remains. Setup and retained source/preview memory are not a new memory audit.
  More complex documents, rotation/projective transforms and other GPUs remain unmeasured.

Registered diagnostic IDs: `filled-preview-plain`, `filled-preview-stages`,
`filled-preview-gpu`, `filled-preview-sync`. Use the standard test runner's review
and explicit run flow. Stages/GPU require the reviewed
[opt-in patch](../../Fixtures/Painting/FilledPreviewInstrumentation.patch), followed
by one Unity compilation; plain/sync should run on uninstrumented production source.
The patch now targets the renderer with binding reuse; the historical stage numbers
above were captured before that optimization.
Restore only that patch and compile again. Do not reset a shared working tree.

Analyzer:

```powershell
node Tests~/Cases/Painting/TiledDrawingPreviewAnalysis.mjs D:/DCFA/Projects/Test6.6 preview-plain4 preview-stages2 preview-gpu1 preview-sync1
```

It verifies completed fixture statuses, identical fixture hashes and all available
image hashes, and reports GPU statistics only for validated positive single samples.
Raw JSONs, summary, restoration copies and the independent GPU probe live in project
`Temp/WhimTex/filled-preview/`; generated results are not committed.

`preview-plain1` failed on a test argument type; `preview-plain2` incorrectly checked
original HDR at an already painted corner. Both are excluded. `plain3/stages1` are
preliminary complete runs using Half-converted image hashes; final runs use Float.
The final fixture hash was `cf8735fe9efc1de3a040f2499a90fdccff1bb142e3a9e387f5d56b412562e692`.
After capture, only the duplicate-run-ID guard was moved before resource creation,
preventing an error report from overwriting previous evidence; measured code is unchanged.
