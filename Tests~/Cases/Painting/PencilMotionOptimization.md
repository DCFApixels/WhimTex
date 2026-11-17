# Thin Pencil CPU preparation — 2026-11-17

The production change skips pattern preparation for Pencil when Repeat is None,
passes each stamp to the vertex builder by readonly reference and skips the old
bounded tile/index lists in native Raster. Native Raster already bins the same quads
into signed tiles and builds its own indices. The bounded renderer still uses its
original lists. No shader, geometry coordinate calculation, stamp spacing/order,
UV mapping, storage format, mask/composite or history algorithm was changed.

The renderer diff is two files, 22 insertions / 14 deletions. No new GPU resources,
buffers, persistent caches or asynchronous paths were introduced.

## Method

Baseline production: `a403a8e`. Unity 6000.7.0a6, RTX 5060, D3D12.
Each timing case uses an owned empty 8192² RGBAHalf Drawing and a fitted 1024² viewport.
Circle Pencil is 1 or 9 px; motion is exactly 250, 1000 or 6500 source pixels horizontally.
The first point is at `(0.1, 0.5)`. Outside cases add 1.25 to source U and move Transform
left by 10240 pixels, keeping the same visible canvas position; the full stroke is
outside the original frame. These are translation-only timing cases.

Each case has 12 strokes, one motion event per stroke, paced by Editor updates without
an artificial delay. Exclude the initial point and the first two strokes: **10 warm
samples per case**. Ordinary production `PaintSegment` wall time includes the small
reflection harness, but excludes Begin/EndStroke, viewport rendering, Undo, readback
verification and initialization. Capture is disabled; no temporary profiling patch
is applied. These are CPU submission timings, not screen latency or GPU duration.
Baseline and candidate each ran once; the maxima below are observations from these
samples, not worst-case guarantees or statistically established tail percentiles.

The fixture records actual stamp/vertex/index counts after the paint timer. Counts
and logical vertex/index payloads are identical before and after in every input event.
Pencil spacing is 1 px for both tested diameters: 250, 1000 and 6500 stamps respectively.

## Before → after

Milliseconds per motion event. Each cell is baseline → candidate.

| Pencil | Motion | Inside median | Outside median | Inside maximum | Outside maximum |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 px | 250 px | 1.386 → 0.838 | 1.301 → 0.787 | 1.784 → 1.532 | 2.050 → 1.085 |
| 1 px | 1000 px | 4.855 → 2.553 | 4.286 → 2.846 | 6.948 → 3.389 | 5.272 → 3.113 |
| 1 px | 6500 px | 24.814 → 13.779 | 25.230 → 15.766 | 33.960 → 16.575 | 32.603 → 20.785 |
| 9 px | 250 px | 1.181 → 0.914 | 1.340 → 1.075 | 2.142 → 1.106 | 1.585 → 1.648 |
| 9 px | 1000 px | 4.608 → 2.930 | 4.306 → 3.005 | 5.578 → 3.195 | 5.304 → 3.510 |
| 9 px | 6500 px | 23.968 → 13.128 | 25.266 → 13.165 | 28.493 → 18.652 | 30.667 → 20.453 |

Median reduction is 20–48%, about 9.5–12.1 ms for a 6500 px jump. The 9 px outside
250 px maximum increased by 0.063 ms despite the lower median; no tail improvement
is claimed for every short case. This is a measured whole-paint improvement; this
run does not separately apportion the savings among the three local changes.

Sampled GPU pool maxima match before/after in every case: 24 MiB for 250/1000 px,
64 MiB for Pencil 1 at 6500 px, and 88 MiB for Pencil 9 at 6500 px. No capacity waits.
Queued readback staging varies with scheduling (overall sampled maximum 140 → 130 MiB;
Pencil 1 / 6500 / inside individually rises 18 → 26 MiB). No cap was increased.
These are sampled component figures, not total process memory or physical VRAM.

## Exactness

Separate correctness matrix: Pencil 1/9 × inside/outside × ordinary/soft-mask +
protected R, eight real 8K cases with three strokes each. Input color is
`(0.8, 3.5, -0.3, 0.6)`; the masked variant writes G/B/A and preserves R.
No timing claims are drawn from the readback-heavy correctness matrix.

The complete 6534×34 Half strip covers the 6500 px stroke and a border on all sides.
The original renderer's hashes were captured **before** changing production source.
SHA-256 agrees before/after for all **32 states**: pending preview, committed pixels,
Undo and Redo in all eight cases. Pending equals committed and Redo equals committed;
Undo restores the full strip captured before the last stroke, byte-for-byte, and
differs from Redo. All history checks actually ran. No mask/HDR quantization was allowed.
This is an exact whole-stroke strip comparison, not a hash of all 8192² canvas pixels.

The timing matrix also passed its existing source-frame, placement and exact 8×8 Redo
probe checks in all twelve cases. No performance or pixel golden was regenerated from
the candidate to manufacture a baseline.

Seven existing regression suites passed, **13,609 checks**:
`pencil-v2`, `drawing-pattern-smoke-v2`, `tiled-drawing-native-raster`,
`tiled-drawing-consumers`, `tiled-drawing-tip-preview`, `tiled-drawing-history-new-layer`
and `tiled-drawing-history-existing`. They cover odd/even Circle/Square/Diamond footprints,
erasing, all line octants, Wrap/HDR, mirrors/repetition, native-vs-bounded exact raster,
projective and partial tiles, procedural tips/sequence/motion, Pressure Opacity/Stamp Blend,
composition/group/Target/clipping, thumbnails/export, native history and cancelled/branched
edits. These supplement the eight 8K strip oracles; they are not eight more 8K matrices.

The first regression attempt exposed an outdated `PencilTests` reader: `StoredTexture`
is now the immutable base, so it read transparent pixels instead of the edited tiles.
The fixture now reads `ReadSourcePixels` once after each stroke, retaining every original
expected footprint, endpoint, eraser, HDR and symmetry assertion. The corrected suite
passes 12,492 checks. No production fix or weakened tolerance was used to make it pass.

## Reproduce

Registered entries:

- `pencil-motion-timing`: twelve cases, ordinary uninstrumented paint timing.
- `pencil-motion-exact`: eight full-strip comparisons through the real history path.
- `pencil-motion-profile`: optional stage diagnostic, requiring the separately reviewed
  temporary instrumentation patch; it was not used for this comparison.

All use the existing test runner, with isolated journals under project Temp on D:.
The wrapper now gives each matrix its own artifact suffix, so a combined runner call
cannot overwrite another matrix. No user document, saved setting or asset is modified.
The tests require exclusive Unity/GPU access, sufficient memory and 4 GiB free on Temp.

Run the analyzer from the package directory after capturing both versions:

```powershell
node Tests~/Cases/Painting/PencilMotionAnalysis.mjs D:/DCFA/Projects/Test6.6 1f4bfc31-d91c-4875-bec6-80495c94ddbd-motion-timing 74bcf7a5-5a1f-436e-8fa1-b4d6e1f1a40c-motion-timing bc5fcd2b-2ea6-4afb-8588-8ed87d0d7e3d 74bcf7a5-5a1f-436e-8fa1-b4d6e1f1a40c-motion-exact
```

It rejects config/fixture differences, missing Undo checks, changed stamp/mesh counts,
and any changed reference digest. It writes its comparison under project
`Temp/WhimTex/pencil-motion`; raw case JSON/PNGs remain in `raster-cycle-audit`, and
source/fixture fingerprints plus case IDs are in `pencil-stages` manifests.

Receipts under project `Temp/WhimTex/test-runs`:

- `pencil-motion-before.json`: baseline correctness; its first timing output was
  overwritten by the second matrix before artifact suffixes were added, so that timing
  is **excluded** from the comparison.
- `pencil-motion-before-timing.json`: separate complete retained baseline timing.
- `pencil-motion-after.json`: candidate timing and correctness.
- `pencil-motion-regressions.json`: first attempt with the outdated base-texture reader,
  failed and retained as evidence.
- `pencil-motion-regressions-fixed.json`: all seven regression suites PASS.

Original production files are retained under `Temp/WhimTex/pencil-motion/*.before`.
Neighboring Shader Graph and thumbnail work was not changed.
