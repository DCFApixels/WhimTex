# Offset Blend verification

Run the opt-in `HistogramSeamlessSmoke.cs` snippet through the connected Editor's
Pipeline `run_script`, explicitly targeting the intended project. Entries:

- `Main`: shader diagnostics, constant/HDR/transparent/colored/grayscale inputs,
  unchanged center, render-state restoration, odd and degenerate dimensions.
- `Preview`: optional local noise fixture in `output/copy-blend-lab-2026-09-28`;
  writes a PNG and decoded-input/output float dumps there, not in Assets.
- `ReferenceFixtures`: writes small random HDR/RGBA fixtures to the same optional
  directory for independent numerical comparison.
- `Benchmark`: warm median of three, including source analysis, CPU preparation,
  GPU rendering, allocations and synchronous readback. Not pure GPU time.

Run `MakeSeamlessIntegration.Histogram` for preview/composite/export render,
cache reuse/source invalidation, group target, clipping, UI events, dirty state,
Undo/Redo, document and portable JSON roundtrips. `Main` retains Screened Poisson
coverage, including unchanged stored legacy numbers. No user document is edited.

## Algorithm contract

Run `OffsetOptionsSmoke.Main` for the ordinary copy-blend CPU reference, optional
global correction, automatic/manual radius, UI events/applicability, independent
method parameters and rejection of the removed API mode name. The test creates a
temporary window and restores focus. No migration or compatibility alias is used.

Offset Blend exposes `offsetContrastCompensation`, `offsetSeamCorrection` and
`offsetAutoRadius` (all default true), plus `offsetCorrectionRadius` (default .05,
range .005–.25). Correction runs independently of the copy edges, with `offsetPoissonEdges`
AllEdges/TopAndBottom/LeftAndRight (default AllEdges). Automatic radius remains
max(.005, edgeWidth/4); manual radius is independent of Mirror/Screened parameters.
Disabled or zero compensation skips analysis/LUT/Gaussian allocation and uses plain
copy blending. Disabling correction skips the global solve and can expose the seam.

Mode 3, `OffsetBlend`; this is the new-layer default. Existing modes are not migrated.
Copies are shifted by floor(width/2), floor(height/2). Quintic weights affect
Default 20% edge bands on both axes, with a pure-copy core of 6.5%. Adjustable edge
selection, widths, compensation and global correction are covered by [SeamlessControls.md](SeamlessControls.md). Singleton axes are
disabled. Four-way weights avoid separately modifying intersecting corner bands.
Premultiplied linear RGBA is analyzed per channel; hidden RGB at zero alpha is
discarded. Output alpha is clamped before unpremultiplication.

Analysis samples at most 128x128 pixels using deterministic stratified point
sampling, with paired jitter for half-period offsets. No averaging/downsampling
filter is applied to the histogram. Statistics are approximate, especially for
large, sparse, strongly periodic or nonstationary sources and odd dimensions.
Changing source/resolution rebuilds analysis when the effect cache misses.
There is synchronous bounded GPU readback: this is not a GPU-only implementation.

Midrank CDF knots map values to Gaussian scores, with two Newton refinements of
an inverse-normal estimate. A 4096-entry inverse LUT uses a fixed [-5,5] Gaussian
domain. Forward mapping inverts this monotone LUT, avoiding large errors from
uniform value-domain tables around densely clustered near-zero samples.
Global autocovariances of the four translated Gaussian fields normalize blending
variance. Covariance is estimated after the same table transform used by the GPU.
One-source regions bypass all histogram remapping and retain their samples.

Each channel is processed independently, not in a decorrelated color space.
Colors and the joint RGBA distribution can change. Small alpha can amplify
straight-RGB values/errors; evaluate premultiplied/composited error as well.
HDR is not clamped to 0..1. This is not a normal-map renormalizer or a guarantee
of exact histogram preservation, smooth feature continuation or invisible seams.

Scratch: three full-size RGBAFloat RTs (source, transformed field, output), a
bounded analysis RT/readback and small CPU arrays/LUT. Unchanged final results
reuse the existing EffectRenderCache; there is no persistent per-document bake
or independent statistics cache with potentially stale source ownership.

## Observed validation

Main: 2,773,394 checks passed in the connected Editor. Histogram integration:
71,763 checks including thumbnails, optional corrections, Undo/Redo and portable settings.
Screened integration: 38,979 checks. Offset options: 79,157 checks, including an
independent ordinary-blend reference and method switching without parameter transfer.
Node contracts/docs also pass.

The optional independent NumPy prototype compares actual decoded GPU input,
not an assumed sRGB decode. On the 600x600 grayscale noise, display RMS error
is about 0.10/255, maximum 2.73/255 versus full-data covariance blending.
Small random HDR/RGBA fixtures at 64x48, 17x13 and 128x128 have premultiplied
RMS errors below 0.0013 and maxima below 0.013. Straight-RGB error is larger
near zero alpha; it is not hidden by these premultiplied metrics.

Diagnostic render/readback medians: 256x256 22.93 ms, 512x512 25.74 ms,
1024x1024 74.97 ms on the test machine. This implementation is not uniformly
faster than Screened Poisson at small sizes. Timings vary with source statistics,
hardware and Editor load; these are not interactive frame-budget guarantees.

Independent adaptation, no upstream implementation copied:
[Heitz/Neyret, HPG 2018](https://eheitzresearch.wordpress.com/722-2/).
