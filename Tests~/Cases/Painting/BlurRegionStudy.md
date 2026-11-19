# Blur source regions

The drawing path uses the existing sparse Raster source and pixel-history journal.
Brush/Pencil/Blur share buffered preparation and the same first-point/segment dispatch;
Smudge keeps its dense retouch adapter, and Healing keeps its asynchronous algorithm.
No additional tool manager or history system was introduced.

## Contract

- Source UV stays tied to the original frame. Signed tile coordinates retain paint outside
  Transform and canvas boundaries. Missing source tiles are transparent.
- CurrentLayer pins the stroke-start tile versions. Below/All retain one composition snapshot.
- The Gaussian coefficients, sampling direction, Half publication and channel protection
  stay the same. Coverage uses the original canvas grid, in one RFloat channel.
- Each changed region includes the Gaussian halo and the final 3×3 neighborhood. Temporary
  strips preserve the original filtered-axis dimensions to retain hardware filtering rounding.
- Segment results share the existing tiled preview. A union of changed tile keys produces
  one history delta at stroke end. Cancellation or failure restores the pinned start revision.
- The source page pool retains its existing 2 GiB budget. Filter strips have a 512 MiB
  allocation guard (1 GiB for the two native-repeat filter buffers). These are separate budgets: the RFloat coverage mask is 256 MiB at 8K,
  and the small current/before/output regions and normal history staging are additional.

The original frame is no longer a clamp boundary: blur can read already painted adjacent
source tiles. Exact comparison with the old bounded implementation therefore applies to
interior sampling. Outside tests use its equations on an extended, offset source frame.

## Verification

The fixture stores the pre-change Gaussian and composite sources from `ed3433c`, with
SHA-256 authentication. It does not regenerate a golden image from the new regional path.
The extended outside reference shares only the coordinate remap, not tiled gathering or publication.

Reports are in project `Temp/WhimTex/test-runs/`. The `blur-regions` profile covers repeated
segments, masks with 0/0.5/1 coverage, channel/alpha protection, 513 px sources, rotation/scale,
native source resolution distinct from canvas, external input, exact native Undo/Redo, cancel,
invalid-segment rollback and caller render state. Each pixel comparison has zero differing
Half components. Repeat-seam cases also verify the complete intermediate Gaussian image
at 512² and no writes beyond the repeated frame. At 513², filtering retains the native
full-frame grid inside the same region transaction: reconstructing fractional raster UV
introduced one-Half-step differences. The experimental coordinate LUT was removed.
This exception does not apply to ordinary unbounded Blur or the 8K cases. The shared Gaussian's existing signed-modulus performance warning is
compared with the frozen shader; new warnings/errors are rejected.

`blur-regions-release.json` passed all 11 regional cases.
`blur-shared-release.json` passed mixed pixel-tool history (158 checks), write protection
(18,635), shared Gaussian kernels (4,098), native Raster (142), rendering consumers (52),
buffered strokes (460,430) and old-stroke completion before the next stroke (23).
The earlier Raster fixture run also emitted console errors for five unset reference-material
uniforms and warnings when destroying an active test RT. Pixel assertions did not catch these
diagnostics. The fixture now explicitly configures inactive modes, detaches its active owned
textures before disposal, and rejects console warnings/errors across the run and cleanup.
The follow-up `painting-console-fix-final.json` passed 143 checks with no new console diagnostics.
Production shader arithmetic was unchanged by this test correction.

## 8K measurements

8K timings below are diagnostic, not editor FPS or a controlled old/new performance benchmark.
They include submission, region copy and a readback fence; source initialization is excluded.
The full-frame oracle has different allocation/residency behavior from the former live tool.
Do not use its `beforeMs` values to claim a production speedup factor.

Final run: `blur-regions-8k-release.json`, Unity 6000.7.0a6, D3D12, RTX 5060.
All four cases passed with zero differing Half components, exact Undo/Redo/cancel,
and completed cleanup. The Editor was ready and stopped afterwards; no runner lock remained.

| Brush / position | Regional segment + fence, ms | Commit + storage flush, ms | Allocated source GPU pages, MiB |
| --- | ---: | ---: | ---: |
| 2048 / inside Transform | 266.2 | 95.7 | 568 |
| 2048 / outside Transform | 278.1 | 86.7 | 1080 |
| 4096 / inside Transform | 939.0 | 265.0 | 680 |
| 4096 / outside Transform | 1051.8 | 180.0 | 1192 |

These are single diagnostic samples, not median/percentile benchmarks. Commit includes
waiting for history writes to finish. Source-page allocation is not peak or total GPU memory:
it excludes the coverage mask, filtering workspace, reference textures and other documents.
Outside fixtures seed an additional source frame, which accounts for their higher residency.
Large-radius Blur remains expensive; this change does not establish smooth interactive
latency for 4096 px brushes. The mask/channel cases ran separately at smaller resolutions.

An earlier 4096-outside run timed out after entering the case. Its original report remains
`blur-regions-8k-final-v2.json`, with a separate recovery note. Subsequent complete runs passed;
the original timeout's cause was not established. Per-phase journals now distinguish test
progress, verification and cleanup instead of treating an ambiguous timeout as a pass.

Implementation scope: the duplicated first-dab dispatch is removed and the common input
flow is 14 lines shorter. The overall production change is 209 lines larger because bounded filtering and
rollback replace the former whole-image operation; this is not a net code-size reduction.
Smudge and Healing have not been converted to sparse filtering in this change.
