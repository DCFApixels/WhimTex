# Stroke completion and cancellation

Verified on 2026-11-17 in Test6.6, Unity 6000.7.0a6. This changes lifecycle and
failure recovery, not rasterization, UV mapping, shader math or GPU page budgets.

## Findings and changes

| Before | Change |
| --- | --- |
| A failed commit bypassed the layer's common cleanup. Repeat/wrap state could survive a rejected stroke. | Completion rolls back on failure and shares resource cleanup with cancellation. The tiled commit resets its publication flags on both outcomes. |
| A GPU page-limit failure ended the low-level pending buffer but left the window's layer, pointer and smoothing state active. | Begin/update failures cancel through the same canvas gesture reset used by Undo. Pointer release clears the owner first, so capture-loss callbacks cannot commit twice. |
| The window's Undo handler committed pending paint and duplicated only part of gesture cleanup. | Native Undo cancels GPU edits before restoring native pixels/history. Window cleanup uses the common reset, including old behaviours detached by native model restoration. |
| Direct selection changes could leave a stroke attached to the previous selection. | The shared single-layer selection path completes paint before changing the selected layer. |
| Reload's history drain could run before window/document OnDisable committed an active stroke. | The final drain first finishes buffered strokes in all live documents. OnDisable becomes an idempotent second completion. |
| An exception during window/document teardown could prevent later resources or layers from being released. | Drawing detach/destruction reuse resource release; close logs a failed commit and continues cleanup of the remaining resources. |

No additional paint cache, GPU buffer, alternate raster path or lifecycle manager
was introduced. Existing bounded input snapshots remain reusable after a stroke.

## Verification

The new `stroke-finalization` profile owns its documents, window, textures and disk
journal. It refuses to run over active/retained user paint history. Tests compare
raw RGBAHalf bytes; the fixture includes signed HDR, a soft selection mask, hidden
straight RGB and negative source tile coordinates. They also check the next stroke
and exact native Undo/Redo after each transition.

- Cancel; normal tool/selection changes; window disable; layer detach; document
  disable; actual document replacement and disposal.
- Direct window Undo handling and actual native Undo while another stroke is pending.
- Repeated EndStroke creates no additional Undo operation.
- GPU allocation refusal at the existing pool boundary, rejected disk quota and
  failed journal reservation. The tests use owned limits/paths, never fill the real
  drive or exhaust physical RAM/VRAM.
- Pending-stroke reload preparation, drained transfers and released staging.
- Independent real compilation/domain reload with a shown owned window: pending
  masked HDR paint survives, the gesture clears, and native Undo/Redo remain exact.
- Existing native-raster comparison, buffered painting, dense retouch bridge,
  HDR-to-8-bit native Undo, failed background page publication and quota metadata
  recovery regressions.
- Four 8K exact-history cases: 2048/4096 px, inside/outside Transform. These compare
  touched tile versions and native Undo/Redo, including Pencil and selection masking.

The initial run reproduced stale constraints after I/O refusal, unwanted commit on
Undo, missing completion through direct selection/reload preparation, and a stale
canvas gesture after GPU allocation refusal. Cancellation, tool change and window
disable already passed. All corrected cases passed afterward.

Final receipts under project `Temp/WhimTex/test-runs/`:

| Receipt | Result |
| --- | --- |
| `stroke-finalization-regressions.json` | 20 scenarios, 467,582 assertions |
| `stroke-finalization-io.json` | 3 scenarios, 220 assertions |
| `stroke-real-reload-verify.json` | Actual domain reload, 11 assertions |

Most assertions count individual channel/pixel checks, not distinct user scenarios.
The real reload was armed by `stroke-real-reload-begin2.json`; the first attempt
rejected an invalid fixture GUID before creating anything. The first recompile
reported up-to-date; the subsequent changed-source compile completed successfully
and the verifier confirmed that the managed domain actually changed.

## Follow-up simplification

Removed the three forwarding methods `BeginCanvasStroke`, `ApplyPaintingPoint`
and `CommitPaintingStroke`; their callers now own the operation directly.
The three copies of segment dispatch (continuation, clipped continuation and
first point after a gap) share one path. Pressure updates at repeated coordinates
and continuation reset at the repeat boundary are unchanged.

This refinement removes 26 production lines from the initial lifecycle fix, with
no new fields or abstractions. The complete lifecycle fix still adds 34 net
production lines relative to the pre-audit Git version: failure cleanup and
reload/Undo ordering account for that remaining growth. This is a local reduction,
not a claim that the whole audit reduced total code size. Tests and documentation
are excluded from both counts.

A frozen pre-refactor render was compared with the new implementation for Brush,
Pencil, Blur and Smudge, with and without repeat clipping, varying pressure,
repeated points, leaving the region and re-entering it. All eight raw RGBAHalf
outputs and the per-point continuation states match exactly (92 checks).
The one-off comparison source and captured bytes stay with the experiment in
`Temp/WhimTex/stroke-simplify/`, not in the permanent test catalogue.

| New receipt | Result |
| --- | --- |
| `stroke-input-paths-after.json` | Eight tool/boundary combinations, exact output and state |
| `stroke-simplify-lifecycle.json` | All 13 lifecycle scenarios passed, 251 checks |
| `stroke-simplify-regressions.json` | Input smoothing, native raster and dense retouch bridge passed, 229 checks |

These are new runs after simplification. The 8K and real domain-reload results
above belong to the preceding lifecycle fix; those runs were not repeated for
this local removal of forwarding and duplicated dispatch code.

## Stroke start ordering — 2026-11-17

Preparation now completes the old operation before creating storage or assigning
its Undo label. `BeginStroke` only resets the new gesture's spacing, pressure,
direction and repeat/wrap state. Canvas, agent Paint and Smudge repair keep their
Prepare/Begin sequence; other pixel edits use Prepare directly. Disposable tip
previews explicitly finish old work before clearing their target.

Removed `PrepareStrokeStorage` and its always-true `blockHistory` argument,
duplicate tiled commit in dense preparation, and the special return that kept an
untouched dense edit alive solely because Begin used to call End. End and Cancel
now release the Undo label in their shared cleanup. This step changes four existing
production files and removes three net lines versus its captured starting state;
there are no new production fields/classes or raster paths.

The new `stroke-begin-order` regression first failed on the old version: starting
preparation left an old Raster stroke pending. It now checks that the old masked
HDR pixels are committed before preparation returns, the new label and dense
surface survive Begin, and an empty completed operation releases its state.
It is included in `stroke-finalization`.

Fresh verification:

- `stroke-begin-before5.json` / `stroke-begin-after1.json`: 48 frozen raw RGBAHalf
  Canvas outputs match exactly. Four tools (Brush/Pencil/Blur/Smudge), plain,
  repeated/clipped, mirrored and wrapped drawing; single click, Shift connection,
  another stroke extending outside the source frame. Soft selection is active;
  native Undo/Redo restores the last stroke exactly for each combination.
- `stroke-begin-preview-before.json` / `stroke-begin-preview-after.json`: three
  exact tip-preview surfaces, pressure on/off and erasing, reusing the same layer.
- `stroke-begin-lifecycle.json`: all 13 previous completion/cancellation cases pass.
- `stroke-begin-regressions1.json`: six existing scenarios pass: native Raster,
  dense retouch bridge, Smudge model/API, queued history and HDR conversion history.
- `stroke-begin-8k.json`: all four exact-history cases pass, 2048/4096 px inside
  and outside Transform, with HDR and masking. No speedup is claimed.
- `stroke-begin-order-final.json`: permanent ordering test passes after removing
  the one-off capture helpers from the test catalogue.

Two broader older suites (`brush-dynamics-v2`, `brush-hlsl-v2`) still fail. The
same failures and values were reproduced with the pre-change production files in
`stroke-begin-legacy-baseline.json`: opacity reads 0 instead of 1; the HLSL coverage
probe reads -431602080 instead of 1. Their probes use `StoredTexture` and the dense
`paintSurface` directly. They have not been repaired here and are not counted as
passing coverage. The simplified production files were restored and compiled
successfully after this controlled comparison; unrelated files were untouched.

One-off comparison code, source snapshots and pixel oracles are retained under
`Temp/WhimTex/stroke-begin/`. Earlier numbered capture attempts failed during test
fixture setup/teardown; `before5` is the complete baseline captured before edits.
These API-driven windows test real Canvas methods and pixels, not physical input
or presentation latency. The preceding long-session measurements remain separate.

## Reproduction and limits

Review and run `stroke-finalization` through the normal runner, allowing
`temp-files,user-state,windows`. The ordinary profile invokes the shutdown hook
without reloading Unity. For the separate actual reload check, the same reviewed
source bundle exposes `BeginStrokeReload(freshNFormatGuid)` and
`VerifyStrokeReload(theSameGuid)`: invoke Begin, run one normal Unity compilation,
wait for ready, then invoke Verify to check and close the owned window. Do not run
another history test between those stages. Expected bytes remain in
`Temp/WhimTex/stroke-reload/<guid>/`; an existing GUID is rejected.

Tests invoke the real product callbacks through the Editor API without simulated
mouse/keyboard input. They do not measure physical input latency or force actual
hardware out-of-memory/device loss. Performance comparisons were not requested for
this lifecycle change, so the test durations are not presented as speed gains.

If a background journal write fails after a stroke has already committed, the
existing fallback retains that page in memory and supports exact Undo/Redo in the
current session; new strokes are refused. This test verifies that fallback.
Persistence of such failed, memory-only history across a simultaneous reload or
Editor shutdown is not guaranteed and was not established by the successful-disk
reload test. A full drive is not a durable backing store.
