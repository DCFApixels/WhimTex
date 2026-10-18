# Brush pipeline performance baseline

Opt-in diagnostics only. These shaders are verbatim LF-normalized copies from
`e5486ca609cec3b0e4cd57543295661395ff24db`, before the unified stroke buffer.
`provenance.json` authenticates both copies and the shared stamp renderer, mesh
builder and shader includes. No Git objects are needed when running the tests.
Do not update the fingerprints to accept changed shared behavior; re-audit the
comparison when the renderer changes.

`Cases/Painting/BrushPipelineBenchmark.cs` reconstructs the old orchestration:

- Normal full-opacity paint writes directly to the source.
- Opacity and non-normal blends allocate separate base and stroke textures and
  assemble back into the surface after each nonempty update.
- Selection is sampled by each stamp. Channel protection runs after each update.
- Preview uses the old alpha-conversion shader; finishing releases stroke buffers.

The current branch calls the actual accumulation, preview and commit methods.
Both branches share precomputed batches, the audited current stamp renderer and the same
source, brush and full-resolution preview. Frozen shader names and include paths
are rewritten in memory; compilation uses the public `ShaderUtil` API and creates
no asset. Package-owned private methods use precompiled adapters; no Unity internal
reflection is involved.

The 2026-10-17 region optimization re-audited the shared renderer fingerprints:
geometry and blend arithmetic remain the same, but bounds tracking and compact
stamp-backdrop copies now run on both sides. Historical reports made before this
change retain their own source fingerprints. Current runs isolate orchestration
cost and do not reproduce the former full-size stamp scratch. For the later unified
full-assembly comparison, see [BrushFullAssembly](../BrushFullAssembly/README.md).

The subsequent 8K spike investigation re-audited the renderer after removing the
unused `_StrokeBase` binding from non-sequential accumulation. Old ordinary
orchestration already supplied no source; current ordinary accumulation now also
leaves that sampler unbound. Sequential stamp/pressure modes retain the source.
Geometry, shader formulas and frozen reference shaders are unchanged. Both current
compute and raster correctness suites passed; the manifest retains the preceding
renderer hash and the scope of this audit. This is not a claim that all observed
driver waits were caused by the binding.

## Workload and limits

Each stroke has 16 input/preview updates along the same diagonal path, hardness
0.65, spacing 0.15, flow 0.35 and brush alpha 0.8. Diameter changes the number of
stamps, as it does during normal painting; it is not a fixed-stamp overdraw test.
Opacity scenarios use 0.4, pressure uses a constant 0.5 input, and blend scenarios
use Multiply in Stroke or Stamp mode. Selection has uniform fractional coverage.
The lock scenario protects alpha. HDR uses signed and above-one RGB.

Two warmups per branch precede nine samples in alternating A/B order. Whole-stroke
timing includes setup, drawing, a preview per update, commit and completion through
sampling into a dedicated 1x1 RT followed by `ReadPixels` from that tiny RT. This is main-thread wall latency with GPU completion,
**not GPU timestamp duration, frame rate or interactive pointer latency**. Stroke
generation, document FX, downstream composition, thumbnails, Undo and saving are
outside the timing. Source/storage textures are initialized by CPU upload;
there is no full-size readback anywhere in the diagnostic. Separate phase diagnostics insert a completion barrier after
every stage; they are illustrative and
must not be added to reproduce the throughput median.

The output comparison samples nine positions. Unmasked unlocked cases allow
half-precision accumulation error; this is a harness sanity check, not a full
rendering regression suite. Selection and channel-lock outputs intentionally
differ because clipping/protection moved from each update to stroke assembly.
The buffered-stroke regression suite owns correctness assertions for that change.

Memory reports distinguish live texture estimates from logical RT payload:

- `activeTextureBytes` uses Unity's `GetRuntimeMemorySizeLong` on the owned
  surface, full-size preview, stored pixels, optional readable R8 selection and
  active stroke/protection textures. Per-texture descriptors accompany it.
- `activeRtPayloadBytes` counts active RGBAHalf texels during drawing.
- `peakRtPayloadBytes` adds the maximum simultaneous stamp scratch or commit
  buffer during the stroke. It excludes subsequent storage readback, CPU copies,
  shared meshes/materials, other documents, and allocations retained by pools.
- Neither measure is a physical VRAM/process peak. Managed byte counters are
  diagnostic only and include harness overhead; zero is not proof of no allocation.

Runs require an idle matching graphical Editor with profiler recording off. An
8K case also requires at least 7 GB reported VRAM. All owned resources are disposed
and render state restored on exit. No window, input or user document is touched.

Review and run `brush-pipeline-benchmark-smoke` first, then
`brush-pipeline-benchmark`, with `--allow-effects temp-files` and explicit
`--project-path`. Each result gets a unique JSON path under project
`Temp/WhimTex/brush-pipeline-benchmark`; runner reports retain the source fingerprint.
Keep aborted or superseded captures for diagnosis, but identify the exact accepted
capture set when reporting results.

The initial 2026-10-16 harness used a 1-pixel rectangle read directly from the
full-size RT. On this Editor/backend that still allocated a full-size readback
image. A repeated 4K run failed to allocate 128 MiB (`8007000e`) and the Editor
terminated. That harness has been removed. Reports whose `timing` does not state
`No full-size readback` are superseded and must not be mixed with final results.
This failure is not evidence of the brush pipeline exhausting VRAM: the native
log explicitly reported that local/non-local GPU budgets were not exhausted.


The 2026-10-18 atlas change re-audits only the two shared production renderer fingerprints.
The optional tiled destination does not change the full-target branch's geometry or shader math;
the new full-target-versus-atlas tests separately compare accumulation across seams and modes.
Frozen reference shaders, inputs and expected render values were not regenerated.
The historical full-assembly control now materializes sparse input as a test adapter. Its timings
include that work; use `BrushTileBufferTests` for the current accumulation A/B comparison.
