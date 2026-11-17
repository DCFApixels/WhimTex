# Pencil long-motion stage audit — 2026-11-17

Diagnostic only. No production optimization or behavior change was retained.
Baseline: `2014ddaa0f9d1dc5e599736dcdd1fc4c900a0545` plus the diagnostic fixture.
Unity 6000.7.0a6, NVIDIA GeForce RTX 5060, D3D12, multithreaded rendering, Graphics Jobs off.

## Workload

- Actual Drawing `BeginStroke` / `PaintPoint` / `PaintSegment` / `EndStroke` and viewport rendering,
  driven through Editor updates; no simulated input or user windows/documents.
- 8192² HDR RGBAHalf Drawing; Circle Pencil; color `(0.8, 3.5, -0.3, 0.6)`.
  All channels writable, no selection mask, FX, dynamics or Stamp Blend. Fitted 1024² viewport.
- Sizes 1, 9, 81 and 256 px. Each of 10 strokes starts at U=0.1 and jumps to U=0.9:
  about 6554 source pixels in one motion event. The first two strokes are excluded from
  warm statistics, as are initial point stamps: **8 measured segments per case**.
- Additional 81 px path: 9 points along a sine curve, eight roughly 870–1080 px motion segments
  per stroke, 5 strokes; **24 measured segments** after excluding two warm-up strokes.
- Outside variants offset source U by +1.25 and translate Transform left by 10240 px.
  The whole path lies outside the original source frame, at U=1.35–2.15, while appearing
  at the same canvas location as the inside case. Both sides have identical stamp,
  vertex and index counts. These are positive-U, translation-only cases.
- No deliberate delay between input callbacks and no forced history drain between strokes.
  EndStroke, viewport rendering, final drain and Undo/Redo are outside the paint timer.
  The isolated test journal lives under project `Temp/WhimTex/paint-undo-lifecycle` on D:;
  the user's history location is restored. Each matrix: 90 strokes / 250 input events.

## Ordinary paint cost

Milliseconds per motion event with capture disabled and no temporary instrumentation.
Separate inside/outside cases, not paired frame measurements. This is synchronous submission
time, not GPU execution or screen latency; it includes the reflection harness.

| Pencil / motion | Stamps per event | Inside median / max | Outside median / max |
| --- | ---: | ---: | ---: |
| 1 px / full jump | 6553 | 26.67 / 33.51 | 23.40 / 31.49 |
| 9 px / full jump | 6553 | 25.10 / 36.38 | 24.49 / 31.24 |
| 81 px / full jump | 547 | 2.86 / 3.69 | 3.55 / 6.28 |
| 256 px / full jump | 164 | 1.31 / 1.69 | 1.31 / 2.10 |
| 81 px / streamed segment | 73–90 | 0.65 / 1.42 | 0.58 / 1.47 |

The existing Pencil spacing is `max(1, floor(size * 0.16))`: respectively 1, 1, 12 and
40 pixels. Larger Pencil therefore generates fewer stamps over the same distance.
This audit does not change that spacing or compare different Pencil shapes.

## Stage attribution

Warm medians in milliseconds from a **separate instrumented matrix**. Geometry includes
vertex construction, raster preparation and index construction. Upload is wall time in
Unity mesh-update APIs, including validation/copy/driver work; actual DMA/GPU execution
can happen later. It is **not a measured device transfer interval**.

| Pencil / location | Generate stamps | Geometry | Upload APIs | Other raster submit | Full paint |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 / inside | 6.97 | 11.35 | 0.60 | 3.63 | 23.16 |
| 1 / outside | 8.22 | 12.71 | 0.49 | 5.12 | 27.12 |
| 9 / inside | 6.95 | 11.38 | 0.47 | 3.74 | 22.66 |
| 9 / outside | 7.00 | 11.36 | 0.48 | 3.87 | 23.54 |
| 81 / inside | 0.90 | 1.27 | 0.09 | 0.73 | 3.35 |
| 81 / outside | 0.81 | 1.48 | 0.10 | 0.82 | 3.24 |
| 256 / inside | 0.24 | 0.55 | 0.08 | 0.66 | 1.76 |
| 256 / outside | 0.21 | 0.45 | 0.07 | 0.51 | 1.49 |
| 81 streamed / inside | 0.09 | 0.19 | 0.04 | 0.16 | 0.71 |
| 81 streamed / outside | 0.12 | 0.21 | 0.03 | 0.16 | 0.65 |

Medians of stages do not add; Full paint also includes material/transform setup and
remaining call overhead. Separate runs differ in scheduling, GC and instrumentation cost;
do not subtract their medians to claim exact profiler overhead or a performance change.
Only one matrix of each kind was run: this establishes the dominant stage, not a stable
1–2 ms inside/outside difference or confidence intervals.

For the 1/9 px jumps, vertex construction alone takes 10.60–11.75 ms median. Raster
preparation adds 0.19–0.37 ms; index construction 0.41–0.52 ms. Vertex upload takes
0.43–0.54 ms and index upload 0.040–0.046 ms. Each event has 26,212 vertices and about
2.70 MiB of logical vertex/index payload; repeated indices for tiles are included.
This is not a physical VRAM measurement.

One profiled 1 px outside event reached **84.55 ms**. Its `brush.mesh_submit` span took
54.89 ms, and that callback also observed a GC collection. The capture does not place the
collection precisely inside that span, nor distinguish driver waiting there. The ordinary
outside run peaked at 31.49 ms. The outlier is retained, not discarded or attributed to
outside coordinates without evidence. Per-thread allocation accounting did not respond
on this Editor, so zero reported allocated bytes must not be treated as no allocations.

The main candidate for later work is CPU stamp/vertex generation for thin, long Pencil
motions. The measured mesh-upload calls are a small part of that cost. No repeatable extra
penalty for drawing outside Transform is established by these runs. No optimization is
proposed as already proven: reducing work must preserve pixel coverage and stamp order.

## Instrumentation and validation

Existing scopes: `brush.segment`, `brush.build_stamps`, `brush.mesh_upload`, `brush.mesh_submit`.
Temporary scopes are preserved as [an opt-in patch](../../Fixtures/Painting/PencilStageInstrumentation.patch):
`brush.mesh_build`, `brush.raster_prepare`, `brush.index_build`, `brush.index_upload`.
The patch adds measurements only and is **not applied in the committed renderer**.

- Geometry = mesh_build + raster_prepare + index_build.
- Upload APIs = mesh_upload + index_upload (the latter includes SetSubMeshes).
- Other raster submit = mesh_submit − index_build − index_upload; this includes tile
  binning/page work and command recording/submission. No overlapping spans are summed twice.
- All 250 instrumented input events have a matching completed segment root and the required stages;
  there are no dropped capture events. The analyzer checks counts, parent ownership and
  non-overlap accounting, and refuses incomplete/missing instrumentation.
- Both matrices passed 120 fixture checks each. Canvas placement and source dimensions
  were checked. Final Undo/Redo revised the owned history and restored an **8×8 Half probe
  byte-for-byte** in all cases; no Undo check was skipped. This is not a full-image oracle.
- All ten 256² preview PNGs are byte-identical between ordinary and instrumented runs.
  The previews are sanity checks, not proof of whole-8K HDR/mask correctness. No mask
  matrix was rerun because production behavior was not changed.
- All **2010 files under src** matched their pre-experiment SHA-256 after removing the
  temporary markers, before handing Unity to the neighboring chat. Restoration compiled
  successfully. Later edits from that chat are independent and were left untouched.

`gpuObservedMs` is async 1×1 viewport-readback callback latency, including queueing and
Editor callback cadence; it is deliberately not used as GPU raster/upload duration.
Cold start/JIT and first-event allocation are present in raw reports but excluded above.

## Reproduction and artifacts

Run `pencil-stages-plain` through the normal test runner on an uninstrumented renderer.
For `pencil-stages-profile`, first review/apply the opt-in patch and compile through Unity;
the catalog entry does not mutate production source. Then remove only those markers and
compile again. Do not use repository-wide reset/restore in a shared working tree. Both
entries require exclusive Editor access, no live painting/history/profiler work and at
least 4 GiB free on the project Temp volume; journals and captures are generated there.
The marker patch is version-specific; verify its sites instead of forcing a fuzzy apply.

Analyze existing runs without Unity, from the package directory:

```powershell
node Tests~/Cases/Painting/PencilStageAnalysis.mjs D:/DCFA/Projects/Test6.6 a7f7dc2a-ad6a-44fc-85cf-6ea2588f4fb9 ccfca714-6301-426a-aabb-85828c484aff
```

Artifacts are relative to project `Temp/WhimTex`:

- `test-runs/pencil-stages-plain-full.json`, `test-runs/pencil-stages-profile.json`: PASS receipts.
- `pencil-stages/a7f7dc2a-ad6a-44fc-85cf-6ea2588f4fb9.json`: ordinary matrix manifest.
- `pencil-stages/ccfca714-6301-426a-aabb-85828c484aff.json`: instrumented manifest.
- `raster-cycle-audit/<matrix-id>-0..9.json`, `.profile.json`, `.png`: individual cases;
  profile JSON exists only for the instrumented matrix.
- `pencil-stages/a7f7dc2a-ad6a-44fc-85cf-6ea2588f4fb9-analysis.json`: samples and summary statistics.
- `pencil-stages/source-before.json`, `source-restored.json`, source `.before` / `.instrumented`
  copies and `instrumentation.diff`: restoration evidence.

The earlier `pencil-stages-plain.json` receipt is a failed **preflight**, excluded from
measurements: the user's C: history volume had less than the required 4 GiB. The completed
matrices used an isolated D: test journal; no user files were cleaned or moved.
