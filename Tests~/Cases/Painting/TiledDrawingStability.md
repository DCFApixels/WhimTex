# Raster painting stability — 2026-11-17

## Scope and method

The existing production cycle diagnostic now has a bounded long-session mode,
not another painting backend. It uses an owned filled 8192² HDR Drawing and the
real Prepare/Begin/Paint/End, active-paint viewport and native Undo paths.
One 32-stroke cycle alternates long paths and two-point short strokes, Brush and
Erase, 2048/4096 px, plain painting, a soft mask, protected channels and Pressure
Opacity with Stamp Multiply. The outside case offsets source UV by +0.75 and
the layer by -6144 canvas pixels: the visible path stays the same, while stored
pixels extend beyond the original Transform. Strokes are not clipped to canvas.

Each run paints for at least 240 seconds, finishing its current stroke before
draining queues and disposing the fixture. Native Undo/Redo verifies exact 8×8
Half-byte probes every 16 strokes. These synchronous verification events are
separately marked; they are not ordinary paint latency. EndStroke timing stops
before verification, so its statistics include every completed stroke.

Unity 6000.7.0a6, Direct3D12, RTX 5060. Runs are sequential, without competing
Unity tests from neighboring work. The production GPU pool cap remains 2 GiB;
history remains 8 GiB / 50 steps. The existing test DiskScope isolates the journal
under project `Temp/WhimTex/paint-undo-lifecycle` on D:, then restores the previous
journal. It does not change user settings or clear user history. Preflight refuses
an active gesture, live retained user history, Play Mode or insufficient disk space.

## Confirmed defects and changes

**History retirement.** Sparse steps already store complete Before/After tile
bytes; the current sparse source persists exact Half pixels independently.
Retiring an old step nevertheless compressed a second set of per-tile Half
checkpoints. `RestoreCurrentHalf`, the checkpoint consumer, skips sparse blocks.
At the disk quota, synchronous retirement therefore spent tens of seconds
producing data that reconstruction did not use.

Sparse retirement now advances the same boundary and deletes the retired step
without validating/recompressing that redundant checkpoint. Dense history keeps
its existing checkpoint path. Version reuse is still only a capture optimization:
retained steps have their own complete serialized bytes. No history format or
async worker was added. The dedicated regression covers background and foreground
retirement, exact restoration down to the retained boundary, rebuilding GPU state,
clearing history and painting/undoing again with signed HDR and a mask.

**GPU page fragmentation.** Individual committed tiles could consume an empty
page before filling a hole in a later occupied page. Pending ink needs whole pages,
so long outside strokes with channel protection exhausted the pool despite unused
slots. RentVersion now prefers existing partial pages and only then empty pages.
It retains excluded-page/copy-on-write rules and performs no pixel relocation,
additional copy, compaction pass or memory-limit increase. A regression empties the
earlier page, keeps one tile in a later page, imports another tile and verifies one
page survives idle trimming with exact HDR contents.

## Initial evidence

| Inside Transform, 240-second target | Before | Sparse-retirement fix only |
| --- | ---: | ---: |
| Completed strokes | 69 | 121 |
| EndStroke median | 18.008 ms | 16.828 ms |
| EndStroke p95 | 592.314 ms | 462.812 ms |
| EndStroke maximum | 58,860.677 ms | 608.738 ms |
| Sampled GPU-pool peak | 1904 MiB | 1912 MiB |
| Sampled staging peak | 979.5 MiB | 900 MiB |
| Final staging / writer / active native buffers | 0 / 0 / 0 | 0 / 0 / 0 |

The baseline overshot its target while blocked in EndStroke. It passed correctness
assertions but exposed a performance defect; PASS alone is not a latency guarantee.
Its final delayed Undo was skipped after the global Undo group changed; four
in-callback exact probes passed. The first corrected inside run passed seven probes
and final Undo/Redo. Ordinary paint/render times did not progressively increase
between complete 32-stroke cycles.

The first outside run, before the allocation-order fix, stopped around 39 seconds
on stroke 20 (4096 px, long, channel protection) at the 2 GiB pool limit. It canceled
and disposed its own resources. The first repeat with both fixes completed 121
strokes and its exact probes/final Undo, but failed the final global-console check:
two exceptions came from `UnityEditor.Connect.ServicesConfiguration.BuildPaths`
for missing `services-gateway`. The original failed report is retained. The fixture
now records that exact message/stack combination separately as `externalErrors`;
every other Error/Exception/Assert remains a failure, and full stacks are recorded.

## Final verification with both changes

Both final 240-second runs passed, with no paint errors, external errors or
readback failures. Their final staging, writer and active-native-buffer counts
were zero. Both owned GPU pools reached zero after disposal; the isolated disk
sessions were removed. No transfer-capacity waits occurred.

| Measurement | Inside before | Inside final | Outside final |
| --- | ---: | ---: | ---: |
| Completed strokes | 69 | 144 | 121 |
| EndStroke median | 18.008 ms | 17.692 ms | 16.468 ms |
| EndStroke p95 | 592.314 ms | 452.183 ms | 454.912 ms |
| EndStroke maximum | 58,860.677 ms | 666.953 ms | 642.193 ms |
| Sampled GPU-pool peak | 1904 MiB | 1864 MiB | 2048 MiB |
| Sampled staging peak | 979.5 MiB | 748 MiB | 748.5 MiB |
| Sampled writer peak | 64 MiB | 64 MiB | 48 MiB |
| Sampled native-buffer peak | 144 MiB | 128 MiB | 112 MiB |
| Periodic exact Undo/Redo probes | 4 | 9 | 7 |
| Final Undo / Redo | skipped | 342 / 339 ms | 504 / 454 ms |

The outside pre-fix failure is not a complete timing baseline, so it is reported
as failure → completed session, not a speedup percentage. The final inside result
reduces the reproduced worst EndStroke pause by about 88×; it is a sampled maximum,
not an upper bound for other machines or future workloads. The completed-stroke
count includes Editor scheduling, final-stroke overshoot and verification overhead;
it is not a brush-throughput benchmark.

For complete 32-stroke cycles, inside paint medians were 1.47/1.50/1.13/1.42 ms
and render medians 7.48/6.98/5.53/7.03 ms. Outside paint medians were
1.48/1.57/1.40 ms and render medians 6.96/6.85/6.35 ms. There was no progressive
slowdown over these runs; sampled pool use stayed bounded. The outside case still
touches the cap and therefore has no demonstrated headroom for larger extents.

The two final long runs passed 13316 assertions. The final independent native
Raster comparisons passed 142 checks and view comparisons passed 216 checks:
full Half-byte Brush/Pencil results, dynamic tips, sequence/motion, HDR, masks,
pressure/stamp modes, protected channels, outside-frame pending/committed pixels,
snapshot Undo/Redo, affine/projective mapping, Point/Bilinear seams and caller
render state. The saved final small viewport PNGs were inspected; they are display
previews, not HDR reference data. Together with the earlier 150 history and 174
allocation/lifecycle checks, this verification set contains 13998 passing checks.

## Limits

This is a bounded mixed-workload session, not an hours-long leak proof. Per-frame
pool/staging/writer samples are not within-call peaks or total physical VRAM.
The 8K Undo checks sample exact 8×8 regions; smaller native-raster and view tests
provide full-image comparisons. Editor polling compiles a small diagnostic bundle
every five seconds; frame intervals include that overhead, Editor scheduling and
explicit verification, so they must not be reported as on-screen FPS. No mouse,
tablet driver or screen-presentation latency is measured.

Removing redundant checkpoints does not make quota retirement free: subsecond
EndStroke spikes still occur. Large source extents can legitimately reach the
unchanged GPU cap; improved packing is not an unlimited-canvas memory guarantee.

## Receipts

Generated evidence stays in project `Temp/WhimTex`:

- `test-runs/drawing-stability-smoke1.json`: 20-second 2K fixture smoke.
- `test-runs/drawing-stability-8k1.json`: low-C:-space preflight refusal, no document created.
- `test-runs/drawing-stability-8k2.json`: baseline inside; outside did not start because
  the two scenarios initially shared a report filename. Paths now include location/size.
- `test-runs/drawing-stability-8k3.json`: first inside correction and outside memory-limit reproduction.
- `test-runs/paint-sparse-retention1.json`: sparse retirement, reuse fallback, dense
  retention/boundary/quota release; 150 checks passed.
- `test-runs/stability-packing1.json`: allocation budget/packing, GPU edge cases,
  queued history, HDR conversion and dense/sparse bridges; 174 checks passed.
- `test-runs/drawing-stability-8k4.json`: first packing repeat, blocked by the unrelated
  Unity online-services exceptions described above.
- `test-runs/drawing-stability-8k5.json`: both final 8K runs passed, 13316 checks.
  Raw ID: `16ab431e-3013-4e79-8483-3c3943d21e0a-{inside,outside}-8192.json`.
- `test-runs/stability-render1.json`: final native Raster and view comparisons,
  358 checks passed.

Raw frame/stroke JSON is under `raster-cycle-audit`; each runner receipt identifies
its run ID and paths. Failed/preflight reports are evidence, not successful runs.
