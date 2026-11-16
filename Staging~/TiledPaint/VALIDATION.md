# Raster candidate validation — 2026-11-16

Validated in the connected Test6.6 Editor: Unity 6000.7.0a6, RTX 5060,
Direct3D12. No production drawing/document/render path was enabled or modified
by this candidate. All 33 retained production paint-source hashes match the
preparation baseline; temporary shader fixture assets were removed.

## Results

- **35/35 reliability scenarios, 460 assertions passed.** C# diagnostics empty;
  staged raster/compute shader diagnostics and captured runtime warnings/errors empty.
- **Six 8K comparisons passed**, with five measured trials per path and a fresh
  warmup before each. Actual native `PaintStrokeBuffer` / brush renderer / compute
  composite were the reference. The complete affected rectangles were read in
  512-square chunks, independently of timing.
- **117,174,029 pixels / 468,696,116 channels compared; zero bit differences.**
  Negative and above-one HDR values were retained. No visual tolerance was used.
- Exact snapshots and Undo/Redo, ten retained deltas, branching, cancellation,
  import/copy, leases surviving surface disposal, allocation failures, invalid
  late input, callback reentry and lost source recovery passed.
- Source dimensions 1×1, 255×257, 333×257, 1000×333, 8192×4096 and 8192²;
  negative/outside coordinates, partial edge tiles, rotated/projective mask
  mapping, native Pencil shapes, batch partitioning and persistent rasterizer
  growth/shrink passed. UV remains tied to the original source frame.
- A full 8192² R8 selection was frozen, its original destroyed, then used for
  pending preview and commit. Results matched; outside-canvas coverage remained
  zero and the frozen selection was released on commit.

## Current → prepared Raster

Medians, milliseconds; texture payload in MiB. “First update” is the first
**input batch**, not one dab and not input-to-present latency. A stroke is ten
input batches. First/final/commit completion waits use a synchronous 1×1 GPU
readback. The remaining batches are submitted without individual waits.

| Brush / spacing | First update, ms | Whole stroke, ms | Commit, ms | Texture payload, MiB |
| --- | ---: | ---: | ---: | ---: |
| Pencil 1 / 0.16 | 0.49 → 0.55 | 11.17 → 7.61 | 1.87 → 1.06 | 528 → 24 |
| Brush 81 / 0.05 | 0.43 → 0.54 | 4.07 → 3.18 | 1.82 → 0.97 | 528 → 24 |
| Brush 2048 / 0.005 | 0.98 → 0.87 | 41.25 → 44.23 | 4.10 → 3.15 | 648 → 144 |
| Brush 2048 / 0.05 | 1.01 → 0.84 | 11.91 → 10.86 | 3.84 → 3.13 | 648 → 144 |
| Brush 4096 / 0.005 | 6.78 → 2.01 | 88.21 → 89.35 | 7.02 → 7.26 | 816 → 312 |
| Brush 4096 / 0.05 | 7.09 → 1.93 | 30.46 → 23.55 | 6.58 → 7.16 | 816 → 312 |

Raster is not faster in every phase: the very dense 2048 stress stroke remains
about 7% slower overall, and 4096 commit costs about 0.2–0.6 ms more. Small first
batches have roughly 0.06–0.10 ms overhead. The selection favors exact native
raster behavior, lower large-brush first-result latency, smaller memory use and
one renderer implementation. Gather/hybrid code is not in the candidate.

Preparation is separate: the pool allocates/clears 2 / 2 / 8 / 22 pages for
diameters 1 / 81 / 2048 / 4096 using only the brush footprint, not the future
stroke path. Its measured cost is approximately 1–2 / 1–2 / 4 / 10–11 ms and
must be scheduled before drawing. Just-in-time preparation is not free: the
raw report includes preparation-plus-first-result separately.

Memory is sampled owned texture payload, not total VRAM: includes prepared,
pending/source/history pages and frozen input while alive. Driver, Mesh/Material,
managed arrays, the native global idle pool and CPU/disk Undo are excluded.
The common comparison mask is a 64-byte spatial R8 ramp; the full-size R8 mask
has a separate reliability case. Editor UI/composition/export and physical
input-to-display latency are outside these measurements.

## Fixed during the audit

- Transactional allocation counters and commit publication; failed appends cancel
  pending work without changing committed pixels.
- Immutable inputs, exact frozen mask/straight resources, explicit copy support
  checks and single-sample/2D validation.
- External history ownership; no fixed four-step stack, no source/history alias
  writes, same-surface Restore and explicit GPU-content-loss recovery.
- Partial edge tiles no longer lose strokes just outside the original frame.
- Perspective transforms use one logical pixel coordinate for selection/straight
  sampling in preview and commit, avoiding target-dependent rounding differences.
- Owner-thread/disposal/reentry checks and bounded geometry/resource growth.
- Reusable material/mesh/geometry buffers; page-level target changes; common
  compute parameters recorded once rather than for every tile. Empty-source
  validation is not repeated for every plain-ink pointer update.

## Scope and reproducibility

This validates the backend and simple-tip adapter. Full native dynamics/texture
tips/backdrop integration, actual editor Undo/persistence, consumer rendering
and other graphics APIs remain the later integration work described in
[README](README.md). No claim is made that those editor paths are already wired.

Run the opt-in profiles/builders documented there. Raw receipts:
`Temp/WhimTex/tiled-paint-readiness/readiness-all.json`,
`native-comparison-*.json`, `final-*-cli.json` and `final-verification.json`.
The final verification includes source hashes, comparison totals and untouched
production hashes. These reports are outputs, not required test inputs.

Catalog validation and focused registration checks passed. The broader runner
unit suite reported two existing catalog inconsistencies unrelated to these
diagnostic IDs (profile membership/order and the node-preview-persistence group);
they were not rewritten as part of the paint candidate.
