# Paint Undo commit: before / after

Measured on 2026-11-16 in Unity 6000.7.0a6, Direct3D12, NVIDIA RTX 5060.
The existing production Raster adapter is used on both sides. This comparison
isolates the Undo bookkeeping change; it is not the earlier dense → Raster comparison.

## Change

- Validate filesystem entries once instead of Exists/Exists/GetAttributes, and
  validate shared ancestors once when atomically replacing metadata. There is no
  path, free-space or cross-editor quota cache.
- The last page writer supplies exact published file sizes, avoiding a page-tree
  scan while holding the shared quota lock.
- Charge reservations before publishing their marker, roll back failed publication,
  and restore a retry marker if releasing unused quota fails.
- A quota-release error after successful pixel publication no longer faults the
  pixel writer. Exact data remains readable; idle maintenance retries the release.
  A double metadata failure emits a reconciliation warning.

Pixel codecs, HDR values, masks, source UV, page formats and memory budgets are unchanged.

## Measurements

`TiledDrawingCycleBenchmark` drives real Prepare/Begin/PaintPoint/PaintSegment/
EndStroke and the native viewport through Editor updates. Both sides use identical
configurations, source dimensions, colors and outside-frame placements. Outside
placement is also verified in the rendered canvas. No mouse or screen timing is used.

The table measures main-thread **EndStroke**, including Undo reservation and GPU
snapshot submission. It does not measure completion of background storage. Values
are median / maximum milliseconds; 8 stamps per dab case, 24 strokes per spam case,
6 per mask/channel case. The first operation is included. Source initialization
and explicit final drain are separate.

| 8K scenario | Inside: before → after | Outside: before → after |
| --- | --- | --- |
| Brush 81 px, single stamps | 18.10 / 67.02 → 15.18 / 46.22 | 16.76 / 67.75 → 11.11 / 18.89 |
| Brush 2048 px, single stamps | 17.18 / 27.64 → 14.76 / 19.15 | 14.58 / 18.38 → 11.49 / 19.92 |
| Brush 4096 px, single stamps | 42.16 / 345.72 → 78.67 / 270.79 | 71.58 / 180.76 → 71.47 / 287.21 |
| Brush 2048 px, rapid short strokes | 15.50 / 45.88 → 15.34 / 36.30 | 17.45 / 55.06 → 14.24 / 214.42 |
| Brush 2048 px with selection mask | 15.58 / 20.07 → 14.16 / 18.93 | 17.59 / 23.87 → 16.84 / 19.63 |
| Brush 81 px, R/A write channels | 17.71 / 87.68 → 11.63 / 14.19 | 15.41 / 18.31 → 11.14 / 15.63 |

The separate four-stroke profile measured `brush.history.reserveSparse` median
12.48 → 9.71 ms. Worker page-write median was 11.53 → 11.83 ms; codec throughput
did not improve. Immediate Undo after six masked 2048 px strokes remained exact:
EndStroke median 16.69 → 17.01 ms, Undo 128.1 → 150.6 ms, Redo 88.4 → 81.9 ms.

### Heavy-case repeat and limits

A second post-change run retained the same inputs:

| Scenario | Inside median / max | Outside median / max |
| --- | ---: | ---: |
| 2048 px stamps | 15.66 / 19.57 | 11.35 / 28.03 |
| 4096 px stamps | 89.52 / 193.91 | 97.51 / 354.01 |
| Rapid 2048 px strokes | 15.97 / 42.91 | 12.94 / 45.59 |

Ordinary reservation overhead decreased, but heavy-case regressions are retained
in the results. This change does **not** establish a large-brush speedup. At 4096 px
and sustained spam the 1.5 GiB snapshot queue still fills, forcing EndStroke to wait.
The inside 4096 px case counted 63 → 67 capacity waits; rapid inside 2048 px counted
6 → 18 (21 on repeat). GPU readback and Editor scheduling remain limiting factors.

Editor-update cadence also differed: the inside 4096 px final drain changed from
0.86 to 6.18 seconds while median drain-update spacing changed from 20.9 to 114.7 ms.
Outside the same case was already 5.48 seconds before and 5.66 after. These are
wall-clock observations, not GPU execution times or proof of a causal regression
in the metadata change. The repeat confirmed long drains; their elimination is
not claimed. Only the retained original baseline is used as “before”.

Sampled maximum source-pool payload stayed 328 MiB; snapshot staging stayed
1529 MiB across both matrices. Individual ordinary cases can retain more pending
work (2048 px inside: 700 → 800 MiB). Writer payload peaked at 80 MiB in both first
matrices and 96 MiB on repeat, below the existing budget. These are owned-resource
samples, not total process/VRAM peaks; initialization retains its 512 MiB original
CPU image. No new full-canvas snapshot was introduced.

## Correctness

12 final runtime cases passed, **2056 assertions**:

- Disk ownership, quota races, repeated Reserve/Complete, known-size completion,
  reopen and injected metadata failures: 65 + 15.
- Sustained reservation release, retention, exact native page storage/failure:
  64 + 40 + 180.
- Published-pixel quota failure and recovery through real dense and Raster paths:
  29 + 32; masked HDR, exact stored/GPU Undo/Redo and acceptance of the next stroke.
- Production new-layer, existing-source and Fill/history bridge: 17 + 31 + 26.
- 8K Brush/Pencil with 2048 px tools, signed HDR and selection mask:
  748 outside + 809 inside, including every stored tile byte through all Undo/Redo.

Older dense test fixtures initially failed because they read the retired dense
surface after switching to Raster. They now explicitly exercise the retained dense
adapter; their exact assertions are unchanged. Production Raster has separate
coverage. A reflected payload constructor was updated to its current signature.

The 13 + 13 ordinary benchmark cases and six post-change repeats passed (332 strokes).
One baseline spam case intentionally skipped its final preview Undo because the
global Undo group changed between Editor updates; it did not touch that unrelated
group. Separate synchronous exact history tests passed. No tests changed user documents.
Process-crash/power-loss durability was not fault-injected; this journal is disposable
Undo, not saved-document recovery. Two additional profile cases are separate from
the ordinary timing table.

## Evidence

Generated data remains in project `Temp/WhimTex`:

- `raster-cycle-audit/undo-before-*.json`, `undo-after-*.json`,
  `undo-after-repeat-*.json`: original reports, configurations and rendered previews.
- `undo-commit-optimization/before-after.csv`, `before-after.json`, `compare.py`,
  `source-sha256.json`: all rows, regeneration and baseline/final source identities.
- `test-runs/paint-undo-commit-final.json`, `paint-undo-commit-rest.json`:
  initial runtime results, including the identified fixture failures.
- `test-runs/paint-undo-commit-fixtures.json`: all five corrected/fault cases passed.
- `test-runs/paint-undo-commit-8k.json`: both exact 8K cases passed.

The focused profiles are `paint-undo-commit` and `paint-undo-commit-8k` in the test
catalog. The latter also includes the separate TIFF scenario, which was not rerun
for this metadata-only change.
