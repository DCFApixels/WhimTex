# Healing / Content-Aware Fill: texture-preserving reconstruction

Implemented 2026-09-25. Shared managed worker: `src/ContentAwareFill.cs`.
The brush mask, GPU commit, selection, transforms and Undo paths are retained.

## Soft source-usage penalty

Implemented 2026-09-25 in the shared worker, without new UI or document fields.
Source-coverage regularization is motivated by Kaspar et al.,
[Self Tuning Texture Optimization (2015)](https://onlinelibrary.wiley.com/doi/10.1111/cgf.12565).
The need to avoid forcing unsuitable samples is also discussed in Fišer et al.,
[StyLit (2016)](https://dcgi.felk.cvut.cz/home/sykorad/Fiser16-SIG.pdf).
These are methodological references; our bounded selection rule and constants below
are an independent engineering adaptation, not a reproduction of either paper.

- Before each Match pass, count source pixels covered by all target assignments.
  Rectangle differences and two prefix sums build footprint coverage and its integral
  in O(image pixels + targets + candidates). Query each valid donor footprint in O(1).
- Freeze the resulting penalty during the entire pass, rebuild between passes.
  No persistent history, no updated-in-scan usage counts, no source-offset penalty.
- With patch area A, T targets and D allowed source pixels, permit mean footprint
  coverage B = 2 A max(1, T/D). Ordinary overlaps in a coherent translated region
  therefore incur no penalty; limited source area raises the allowance.
- For average coverage U, p = max(0, 1 - B/U), with zero for U = 0.
  Match keeps the best raw appearance error E and nearby evaluated alternatives.
  Only candidates with raw error <= 1.15 E are eligible; choose by error + .2 E p.
  The gate uses the final raw best found for this target in the pass, not a sequence
  of successively penalized incumbents. Exact zero-error matches are not sacrificed.
- Search still follows raw-best coordinates; clipped early-out estimates are discarded.
  Voting and final reconstruction retain raw normalized appearance errors only.
  Radius-zero fallback is unchanged. Intermediate votes and final blending are unchanged.

This is deliberately conservative: it does not guarantee unique patches, eliminate
cloning in perfectly self-consistent regions, or fix every gray band / patch seam.
The quality bound is per candidate selection, not a bound on final image error over
all iterations. Additional arrays cost 8 bytes per prepared pixel (int scratch + float
penalty), reused between passes and released with coarse-level buffers. Up to 8 MiB
at the brush's working-region cap or 32 MiB at the fill cap, per full-resolution level;
these are array payloads, not measured process peak memory. No extra image copies.

Measured against an exact snapshot immediately before this change, Test6.6,
Unity 6000.7.0a6: same captured 192x256 noise-seam inputs, Balanced, seed 1.
One warm-up each, alternating order, median of three complete managed worker runs;
excludes shader compilation, GPU readback, UI and commit. Source usage below counts
footprint coverage in the final matching field, not the final blended pixel provenance.
Effective area = (sum coverage)^2 / sum(coverage^2), a concentration measure, not
an independent perceptual quality score.

| Case | Before / after ms | Peak source coverage | Effective source area | Repair std. deviation | Repair curvature |
| --- | --- | --- | --- | --- | --- |
| Smooth noise | 604.5 / 644.3 | 434 / 425 | 4960.9 / 5262.9 | .088632 / .088489 | .003829 / .003835 |
| FBm noise | 644.0 / 675.1 | 465 / 421 | 6260.3 / 7033.6 | .059857 / .060047 | .008979 / .008887 |

On these inputs the overhead is about 5–7%, with modest diversification and similar
contrast/roughness. Timings fluctuate; no universal speed or visual-quality claim.
Comparison images inspected under Temp/WhimTex/NoiseSeams/reuse-*.png.
Actual 512x512 full-perimeter brush integration applied four consecutive strokes,
preserved untouched pixels, and passed seam gates (X/Y .004453/.003073).

ContentAwareReuseSmoke is self-contained: brute-force footprint reference, overlapping
center shifts, coherent copies, scarce donors, buffer reuse, cancellation, exact/HDR
selection and the final-best quality gate. Match integration verifies frozen usage
and actual unclipped appearance errors. Also passed: Quality (all qualities/three
seeds), Cost, Onion, Pyramid, Reconstruction, Fill, HealingBrush and noise/perimeter
integration. These tests don't guarantee a visually invisible repair on arbitrary input.

## Release handling, full-perimeter crops and search cost

Two independently reproduced cancellation paths were fixed: a notification for another
document cancelled Healing before the owner check; a released-button PointerMove
discarded the mask before PointerUp could submit it. PointerUp now owns submission;
Escape, capture loss, focus loss and relevant context changes still cancel.
This does not prove either event ordering was the exact cause in the user's window.

When a tiled crop spans a complete axis, its cut now uses the midpoint of the longest
least-painted band. Coverage is shifted by the same integer offset before source
readback; world-space coverage and commit coordinates remain unchanged. Uniform
projections keep the old crop (including the single vertical/horizontal seam fixtures).
This is not a fully periodic PatchMatch solver; strokes crossing the relocated cut
can still expose transitions.

The self-contained HealingPerimeterSmoke uses 512x512 FBm, scale 8, seed 1337,
size 64, hardness .8, search 64, Balanced, Current Layer. Four successive whole-perimeter
strokes applied, preserved untouched pixels and passed both seam gates. Actual stroke
latency remains about 3.5–3.7 seconds including readback, worker, commit and polling.
In an isolated before/after PNG comparison (gamma/display values), seam jumps X/Y
fell from .0840/.1114 to .00454/.00347; repair-strip curvature .00860 to .00462,
standard deviation .07069 to .06665. These fixture-specific measures are not proof
that all patchwork artifacts or contrast loss have been solved.

Candidate search now prepares the target patch once per pixel rather than repeating
bounds, donor validity and premultiplied target math for every candidate.
One small sample array is reused per pass; no new full-resolution descriptor buffer.
Iteration counts, random sequence and accumulation order are unchanged.
ContentAwareCostSmoke checks 121,488 exact comparisons with the original scalar formula,
including border patches, HDR/alpha, invalid known pixels, onion support and early-outs.
Twelve full-worker A/B runs on the two captured 256x256 noise fixtures (single seam and
perimeter) had zero differing output pixels. Median milliseconds, immediate pre-change
worker / optimized: smooth seam 811/613, smooth perimeter 1030/787, FBm seam 877/640,
FBm perimeter 1152/817. This is roughly 24–29% less managed computation, not total UI
latency; initialization/JIT, scheduling, GPU work and commit are separate costs.

## Basis and implementation

Independent implementation of ideas described in Newson et al.,
[Non-Local Patch-Based Image Inpainting, IPOL 2017](https://www.ipol.im/pub/art/2017/189/),
sections 3.2–3.4. No reference implementation code was copied or added as a dependency.
This is an adaptation, not a reproduction of all settings or stages of that method.

- Build directional texture descriptors from a 3×3 average of absolute differences
  between known adjacent pixels. This implementation uses premultiplied RGB and alpha
  variation, including finite HDR values, rather than grayscale only. Missing pixels
  and invalid/transparent samples do not become artificial feature edges.
- Propagate descriptors through pyramid levels by nearest-neighbor sampling. Do not
  recalculate them from downsampled colors: that loses fine texture information.
- Patch matching adds descriptor distance to premultiplied RGBA distance with a fixed
  engineering weight of 4. Intermediate reconstruction votes both colors and descriptors.
- Appearance comparisons use raw cost and early termination; the soft reuse rule above
  chooses between close alternatives. Errors used across
  neighboring patches are normalized by their available weighted support, so border
  patches do not win merely because they contain fewer samples.
- After the ordinary iterations, match once more against the converged image. For each
  missing pixel, choose the proposal from the covering patch with the lowest normalized
  error. Preserve its original color for detail; smooth, compatible donor neighborhoods
  use selective voting as described below. Progress reaches 100% only after this final
  reconstruction. Cancellation remains available.

No neural model, native dependency, platform-specific path or Unity internal API.
No new document fields or migration. Illumination matching is not implemented.

### Selective final reconstruction

Keep the best normalized-error proposal as an anchor. Classify its original donor
neighborhood using the ratio of squared second differences to first-difference energy
in premultiplied RGBA. A smooth confidence ramp between .04 and .5 rejects edges and
high-frequency texture; a missing permitted neighbor or zero variation retains the
anchor. This source-aware blend is an engineering adaptation, not an implementation
of the paper's mean-shift clustering or best-only reconstruction.

Other overlapping proposals must also be locally smooth. Compare both their color
and signed horizontal/vertical gradients against the anchor, with bandwidth relative
to its source variation (not fixed LDR brightness). A compact, squared falloff rejects
incompatible proposals. Vote premultiplied RGBA, weighted by match error, and blend
with the anchor according to smoothness. Identical proposals remain bit-exact; alpha
and negative/HDR RGB are neither clamped nor independently weighted. Radius-zero and
insufficient-context cases retain the old result. Only target pixels are written.

A 2,048-entry direct-mapped cache stores donor profiles during this final pass only.
It uses 120 KiB of entry payload, independent of image size, with no per-pixel allocations.
Keys include the exact donor index; collisions replace entries, never change results.
It is discarded between runs, so it cannot retain stale source data after Undo or painting.
`ContentAwareReconstructionSmoke.Main` tests blending, pattern/hue rejection, sharp
fallback, missing context, radius zero, HDR scaling, premultiplied alpha, cancellation,
and bit-exact cached/uncached equivalence across changed source data.

### Boundary-to-center initialization

At the coarsest available pyramid level, initialize the target in one-pixel rings
(8-connected boundary). Partial patch costs ignore uninitialized pixels entirely.
Each ring's matches are computed before publishing any of its reconstructed colors or
texture features; future rings can use that context, but donor patches always come from
the original permitted source. Finer levels transfer donor coordinates as described below.

This adaptation copies the best center proposal, rather than the paper's weighted
reconstruction from known-centered patches. It tests propagated offsets and up to 512
stratified donor candidates per pixel (exhaustive below the cap), with independent seeded
random state. This bounds search when sparse donors prevent a small coarse level.
The initializer adds two bool arrays and an int queue at that level, not every level.
Radius-zero cases and targets isolated by invalid/transparent context retain the safe
nearest-donor initialization. The original soft coverage mask is never eroded or changed.

`ContentAwareOnionSmoke.Main` verifies concave/disconnected/edge masks, soft coverage,
isolated fallback, single pixels, one-pixel-wide images, HDR/alpha and cancellation.
Poisoned unknown colors/features must not influence initialization. A diagonal bar
crossing holes of 17/33/49 pixels is checked with all qualities and three seeds.
The generated `Temp/WhimTex/OnionStructure.png` shows truth / damaged / restored.

A separate A/B run compared the immediate pre-onion worker, not the older `fb7f6c5`
baseline below: 97×73 synthetic images, Balanced, seed 123, mean absolute red error:

| Structure / hole | Before onion | With onion |
| --- | --- | --- |
| Diagonal bar / 33×33 | 0.1146 | 0.0000 |
| Diagonal boundary / 49×49 | 0.1136 | 0.0027 |
| Corner / 49×49 | 0.0393 | 0.0700 |
| Circle / 49×49 | 0.7060 | 0.7054 |

This improves continuation in the tested line cases, not every missing shape. Large
ambiguous corners can be worse; missing unique geometry still cannot be inferred reliably.
On the existing 129×97 Balanced texture-flat fixture, warm median time changed from
25.18 to 29.55 ms; noisy edge from 34.81 to 45.63 ms. These are managed-only observations,
not end-to-end brush timings or a universal overhead ratio.

### Fine-resolution donor transfer

When preparing a finer level, upsample the coarse donor coordinates with pixel parity,
validate the complete fine donor patch, and seed BOTH working colors and texture features
from that fine level's original donor samples. Do not copy the coarse working image or
its averaged features. Invalid projected patches retain the existing nearest valid fine
donor fallback. This is a direct-center seed followed by normal matching/voting, not
an extra weighted reconstruction pass from the reference paper. No additional buffers,
settings, serialization changes or contrast postprocessing are involved.

`ContentAwarePyramidSmoke.Main` checks odd/even sizes, one-pixel-wide images, valid
coordinate parity, invalid-projection fallback, finite HDR/partial-alpha source samples,
soft coverage and cancellation. Coarse working buffers are deliberately null: no coarse
color or feature can leak into fine initialization. The real tiled-noise regression also
requires a modest contrast improvement over the immediate pre-transfer worker.

On the same captured 256x256 Noise stroke (Balanced, seed 1), strip standard deviation
changed from .086862 to .090351 for smooth Perlin (+4.0%) and .059922 to .064038 for FBm
(+6.9%). Source values were .150829 / .092345, so **the gray-band issue remains**; this is
an incremental correction, not its complete solution. Managed worker single-run times
were 840 / 922 ms; complete brush strokes were 0.88–0.99 s in one integration run and
1.07–1.32 s on a later repeat, including readback, commit and polling. No significant
timing claim follows from these individual observations.
Post-commit curvature / seam: smooth .003884 / .008713; FBm .009005 / .009920.

Two intermediate-voting experiments were rejected and removed, leaving the existing
Vote behavior unchanged. Relative-error weighting gave smooth/FBm deviation .088165 /
.065651 but curvature .004582 / .010177. Anchor-color weighting gave .087517 / .069695
and curvature .004878 / .009848, with extra cost. Neither materially fixed smooth-noise
contrast, and both increased patch discontinuities compared with coordinate transfer alone.
Do not present either experiment as a shipped feature or weaken seam/detail tests to adopt it.

## Verification and limits

### Tiled noise seam reproduction

The comparison below records the preceding selective-blend stage; current transfer results
and its remaining contrast limitation are documented above.

`HealingNoiseSeamDiagnostic.Main` creates its own transient Noise → Drawing fixture,
then runs actual vertical and horizontal Healing strokes in Tiled. It saves shifted
previews (original / vertical / both), worker-input crops and metrics under
`Temp/WhimTex/NoiseSeams`. Bounded regression gates cover roughness, seam reduction
and tonal variation for this fixture, not arbitrary-image quality.
Settings: 256×256 Perlin, None or FBm, scale 8, seed 1337; Size 64, Hardness .8,
Search 64, Balanced, Current Layer. No user document or Assets file is changed.

Reproduced the reported patch-like artifacts. For smooth noise, the mean vertical seam
jump dropped from .080097 to .008907, but mean absolute second differences inside the
repair strip (excluding the original seam) rose from .003613 to .006090.
An isolated worker A/B with identical captured input, omitting only ReconstructBest,
reduced that latter metric to .003532; FBm changed from .011069 to .006496.
These are roughness indicators, not a universal quality score. Weighted reconstruction
reduces the fine discontinuities here but also softens texture; it is not a shipped fix.
Selective reconstruction now reduces the worker-only strip roughness to .003829 for
smooth noise and .008568 for FBm (37% and 23% below the preceding best-only worker).
Strip standard deviation changes from .086895 to .086862 and .060118 to .059922,
respectively: this reduction is not obtained by flattening the whole repaired strip.
The older worker already reduced standard deviation substantially from the source
(.150829 / .092345); this change does not solve that separate contrast limitation.
The regression gates retain at least 90% of the preceding worker's deviation rather
than falsely requiring the original source contrast. Plain weighted reconstruction
above is a comparison only, not the production path.

Actual brush output after RGBAHalf commit: smooth curvature .003832, seam .008339;
FBm curvature .008569, seam .010903. Thin-line, categorical edge, texture, HDR/alpha,
cancellation and Undo/Redo tests also pass. Patch transitions can still remain in
ambiguous areas; this is not illumination matching or a general seamless synthesis solver.

Same captured input, Balanced, seed 1, one warm-up + median of three managed runs:
best-only / selective = 633 / 804 ms (smooth), 746 / 881 ms (FBm). These measurements
include the whole worker, exclude GPU/UI/commit, and compare the immediate pre-blend
worker, not `fb7f6c5`. The bounded cache replaced an initial uncached version measured
at 995 / 1,084 ms. Timings fluctuate and are not performance guarantees. Actual complete
strokes in the final integration run took about 0.89–1.00 seconds including polling.

`Tests~/ContentAwareQualitySmoke.cs` is a standalone Pipeline `run_script` test,
entry `ContentAwareQualitySmoke.Main`. It generates all images in memory and writes
reports and a comparison under `Temp/WhimTex/FillQuality`, never Assets. It runs all
three qualities, compares repeated runs for determinism, and checks quality over three
seeds (7, 123, 877), source preservation, finite output, exact donor colors for categorical
edges/HDR boundaries and bounded source-range colors for smooth reconstruction,
monotonic completion and cancellation. Eight cases cover straight/diagonal edges,
stripes, texture beside a flat area, linear gradient, HDR/alpha, noisy edges and shading.

Optional `RecordBaseline` records outputs before a change. Main does not require those
files. Comparison columns are truth / damaged / baseline / current (without a baseline,
the third column repeats damaged and the CSV explicitly says unavailable).

Historical comparison before onion initialization and selective blending, against worker
revision `fb7f6c5`, Unity 6000.7.0a6 in Test6.6:
129×97 region, 19×25 hole, seed 123, Balanced. Times are medians of three warmed managed
calls, including test input setup/reflection but **excluding** GPU readback, UI, commit
and the first compilation. They are observations, not performance guarantees.

| Case | Old / new masked premultiplied RGBA MAE | Old / new texture contrast ratio | Old / new ms |
| --- | --- | --- | --- |
| Repeated texture next to flat area | 0.06949 / 0.00000 | 0.485 / 1.000 | 19.37 / 26.66 |
| Noisy edge | 0.01408 / 0.02102 | 0.912 / 1.009 | 19.94 / 32.72 |
| Smooth shading with missing peak | 0.04832 / 0.04687 | 2.486 / 2.756 | 26.15 / 42.37 |

Exact patterned reconstruction is a favorable synthetic case, not proof for arbitrary
images. Best-proposal output preserves noise too: pixel error may increase while local
contrast is better preserved. A unique missing brightness peak cannot be recreated by
copying donors, and smooth shading can still reveal the repaired region. The shading
test explicitly bounds this known limitation rather than asserting exact recovery.
Winner selection can expose donor transitions; this is not illumination/seam correction.

Work costs more: three Vector2 descriptor buffers plus one float normalization buffer
add 28 bytes per prepared pixel, plus a temporary 16-byte gradient buffer per base pixel
and descriptors for pyramid levels. These figures describe arrays, not measured peak
managed memory (GC lifetime and existing color buffers add overhead). Large strokes
remain bounded by the existing working-region caps.

Integration checks also passed: `ContentAwareFillSmoke` (24,326 checks) and
`HealingBrushSmoke` (59 checks), including actual GPU writes, soft masks, tiled edges
and corners, HDR/alpha, native Drawing dimensions, cancellation and Undo/Redo.
