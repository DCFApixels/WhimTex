# Empty Drawing initialization — 2026-11-17

The first-paint interpretation in the original five-sample table is superseded by
the paired follow-up below. Source-creation timing and the separate exact pixel/history
checks remain valid. No production changes were needed for the follow-up.

## Change and scope

Empty Drawing storage is cleared directly through its existing readable Texture2D CPU
buffer, then uploaded once. It no longer creates a full-size premultiplied working RT,
a straight conversion RT or a GPU-to-CPU ReadPixels transfer. Explicit InitializeCanvas
and lazy first-stroke initialization share the implementation. Existing/deferred textures
and pending paint surfaces retain their data; the original texture still defines source
UV and native history identity.

The optimization does not remove the dense original, alter stamp rasterization, change
page/history budgets, or change the Undo format. HDR remains RGBAHalf linear; Standard
remains RGBA32 sRGB. No extra managed pixel array is allocated for clearing.

## Method

Unity 6000.7.0a6, D3D12, NVIDIA GeForce RTX 5060, Linear project, 8192 × 8192.
Baseline Drawing source: commit `5aa0af1c91874c081d6db68501a1be0c5f9ee5c3`.
Other-chat graph changes were held fixed during measurements; only the two Drawing
production files changed for this comparison.

Each case uses six fresh owned documents: one warmup, then five measured samples.
The same test source and a soft R8 selection mask are used before and after.
The outside-Transform case uses negative source U with a translated layer; it maps to
the same visible document position as the inside case. The journal is isolated on the
project volume. No user document, window, mouse or keyboard is involved.

- Creation includes model normalization and InitializeCanvas. Lazy creation skips
  InitializeCanvas, so its first paint also pays for source initialization.
- `Submit` measures the synchronous method call. `Ready` adds a 1 × 1 GPU readback
  fence; it is a completion measurement, not screen presentation latency.
- First paint includes PrepareBufferedStroke, BeginStroke and PaintPoint, followed by
  a readback fence. The original fixture accidentally used CopyRegion without
  includePending: it fenced committed storage, not the visible pending composite.
  The follow-up fixes this and asserts a visible pending pixel before accepting a sample.
- EndStroke, full-byte verification, Undo/Redo, journal drain, cleanup and selection
  setup are outside first-result timing. Commit samples are retained in the raw data
  but this experiment does not establish a commit optimization.

## Results

Medians in milliseconds, before → after. Brush size identifies the following first
stroke; it does not affect the dimensions of the blank source.

| Case | Creation submit | Creation ready | First paint submit | Original post-call fence |
|---|---:|---:|---:|---:|
| HDR Brush 81, inside | 400.2 → 94.8 | 543.6 → 338.4 | 1.8 → 1.5 | 42.3 → 56.1 |
| HDR Brush 2048, inside | 776.7 → 97.4 | 1051.2 → 331.4 | 2.1 → 2.1 | 54.4 → 52.2 |
| HDR Brush 4096, inside | 777.5 → 105.6 | 920.7 → 414.5 | 42.6 → 43.9 | 64.4 → 83.9 |
| Standard Brush 2048, inside | 167.4 → 44.8 | 295.1 → 143.5 | 2.3 → 1.6 | 27.9 → 24.0 |
| HDR Pencil 2048, outside | 418.4 → 89.9 | 585.0 → 282.5 | 2.8 → 1.6 | 47.3 → 44.5 |
| HDR Brush 2048, lazy | 0.05 → 0.05 | 0.06 → 0.06 | 414.9 → 93.5 | 586.6 → 259.4 |

Explicit source creation is faster in every tested case. The lazy creation/paint/fence
measurement falls from 586.6 to 259.4 ms. First paint on an already-created source has no consistent
speedup: the 81/4096 ready medians are worse by 13.7/19.5 ms in this run, while their
submission times remain close. The change targets initialization; these samples are
not evidence of faster sustained drawing. Allocation and driver-fence variability is
visible in the raw samples; do not extrapolate five samples into tail-latency guarantees.

After explicit creation, retained working-surface storage changes from 512 MiB to zero.
Creation also avoids a transient conversion RT: 512 MiB for HDR or 256 MiB for Standard.
Those are texture payload sizes, not a process/driver peak-memory measurement. The
original readable source still occupies 512 MiB HDR / 256 MiB Standard on CPU plus
its GPU copy. Page and history budgets remain unchanged.

## Verification and provenance

All six before/after rendered-region SHA-256 hashes match exactly. Each matrix passed
330 checks including the entire blank CPU source, encoding, dimensions, source UV,
masked visible pixels, exact first Undo/Redo and preservation on repeated initialization.
The separate edge fixture passed 45 checks: exact CPU/GPU zeros in both formats, no
working RT, a frozen non-square source frame, existing hidden RGB and a pending signed
HDR premultiplied surface.

The existing regression set passed another 6806 checks: 2048/4096 Brush/Pencil on
8K inside/outside Transform, masks, signed HDR, repeated exact pixel Undo/Redo,
native HDR-to-8-bit conversion and immediate native Undo of a newly created layer.
Together the final edge, matrix and regression runs passed 7181 checks.

The original raw data and receipts were session artifacts under project Temp/WhimTex;
Unity cleared them when the Editor restarted during the follow-up. Their historical
paths are listed below; the original human summary and test sources remain available.
Current follow-up receipts are listed in its own section.

- `Temp/WhimTex/test-runs/drawing-init-before-r2.json`
- `Temp/WhimTex/test-runs/drawing-init-edges-final.json`
- `Temp/WhimTex/test-runs/drawing-init-after-final.json`
- `Temp/WhimTex/test-runs/drawing-init-final-regressions.json`

Earlier attempts are excluded: the original JsonUtility report omitted sample lists;
an initial `recompile` returned `up_to_date` while the Editor still ran old Drawing code.
The latter was detected by retainedSurfaceBytes = 512 MiB. Explicit imports of the two
edited scripts and Unity's CompilationPipeline loaded the new implementation; the edge
fixture now checks for absence of that surface before timing. A subsequent correctness
check required explicit CPU zeroing rather than relying on Texture2D's default contents.
Only the final implementation and complete valid matrices appear in the comparison.

## Paired first-stroke follow-up — 2026-11-17

The original apparent +13.7/+19.5 ms regression was not established by five sequential
samples. The new fixture alternates old/new initialization in the same compiled Editor,
reverses AB/BA order, and checks the visible **pending** HDR/masked pixel. The old
initializer is the unchanged EnsurePaintSurface → SyncSurfaceToTexture sequence with
the empty-base flag; no production source rollback or rebuild is involved.

Each invocation contains one warmup pair and two measured pairs. Editor frames run
between invocations. Sixteen measured pairs per diameter cover the ordinary timing;
another sixteen 4096 pairs add two pre-paint fences as a diagnostic control. These
extra fences are test-only and are not an optimization or production behavior.

| Measurement | Old initialization | New initialization | Paired median difference, new − old |
|---|---:|---:|---:|
| 81: call through pending-result fence | 42.39 ms | 39.56 ms | −3.00 ms |
| 81: CPU submission | 1.38 ms | 1.27 ms | −0.13 ms |
| 4096: call through pending-result fence, ordinary boundary | 72.59 ms | 93.11 ms | +8.71 ms |
| 4096: CPU submission, ordinary boundary | 2.71 ms | 2.69 ms | −0.31 ms |
| 4096: pending result after pre-paint synchronization | 45.74 ms | 45.47 ms | +0.32 ms |

The 81 regression does not reproduce. For 4096, ordinary completion times range from
52–120 ms before and 55–107 ms after. Nine of sixteen paired differences are positive;
the descriptive paired bootstrap interval is −6.46 to +31.85 ms. Those samples alone
cannot establish a slowdown, despite the difference between their unpaired medians.

With the creation/paint boundary isolated, the 4096 paired interval is −1.11 to +3.74 ms
and the +20 ms gap disappears. In the eight control pairs that also record fence cost,
pre-paint waiting itself is 36.42 → 35.70 ms. The driver stall moves from readback waiting
into CPU submission: submission becomes 43.83 → 43.53 ms across the sixteen control pairs,
while completed pending results remain 45.74 → 45.47 ms. This shows that the timing
boundary changes where synchronization cost is charged; it does not identify individual
GPU instructions or establish an exact driver-internal cause.

Including the recorded control waits, creation + first result is 609.57 → 381.91 ms.
The new end-to-end path is faster in every measured pair, including ordinary-boundary
pairs. The evidence supports retaining the empty-source optimization; no repeatable
regression of stamp processing was found. This is a single-machine diagnostic, not a
guarantee about other drivers or tail latency. Intervals use 20,000 deterministic
paired bootstrap resamples of the median difference; neighboring pairs share an Editor
session and are not independent hardware repetitions.

All 24 successful short invocations passed, totaling 840 checks, including distinct
old/new initialization state and identical visible signed HDR/masked pending pixels.
Native pixel history was not changed in this task; the previous exact Undo/Redo checks
remain the correctness coverage for the production change.

An earlier long invocation failed when Unity's D3D12 backend could not allocate a
512 MiB buffer for the old full-image ReadPixels path (`8007000e`), then the Editor
restarted. That run produced no usable comparison and is excluded. Short invocations
avoid accumulating those allocations in one long Editor call and all completed.

Current session artifacts under the project root:

- `Temp/WhimTex/test-runs/drawing-first-stroke-paired-short.json`
- `Temp/WhimTex/test-runs/drawing-first-stroke-settled.json`
- `Temp/WhimTex/test-runs/drawing-first-stroke-settled-timing.json`
- `Temp/WhimTex/drawing-initialization/first-stroke-analysis.json` (raw samples and statistics)

Reproduction: review/run `drawing-first-stroke-paired` four times for sixteen pairs per
size, and `drawing-first-stroke-settled` four times for sixteen control pairs. Keep
invocations short and allow the runner to return to the Editor between scenarios.
Do not concatenate them into one large synchronous 8K allocation loop.
