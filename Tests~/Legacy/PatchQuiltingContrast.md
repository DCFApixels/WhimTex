# Patch Quilting contrast compensation

Run through the connected Unity `run_script` command; explicitly target this project.
`PatchQuiltingContrastSmoke.cs` entry points:

- `Main`: constants, HDR, transparent input, masks, pure samples, protected center, zero Feather and enable/disable reuse.
- `Variance`: fixed uniform-noise fixture, variance over entire affected boundary strips; no selection by measured improvement. Observed 0.071746 off vs 0.080879 on (input variance approximately 0.0833).
- `Memory`: lazy allocation, budget accounting and NativeArray/texture disposal.
- `Capture`: uses existing `output/offset-regression-audit-2026-09-28/live_4.bin`, writes `output/quilting-contrast/compensation-0-50-100.png`. Columns 0/50/100% compensation, rows Patch Width 20/45%, Feather 100%, Normal/Linked/All, no Poisson. Tile joins centered.

Additional integration entries in `PatchQuiltingSmoke.cs`: `IntegrationCompensated`, `Cache`.
They exercise composite/target/group/clipping/thumbnail/export rendering, cache invalidation,
portable roundtrip, UI visibility and percentage mapping. Existing `Integration` tests the off path.
Shared transfer regression: `HistogramSeamlessSmoke.Main`, `MakeSeamlessIntegration.Histogram`.

Warm 512² capture fixture, 15 samples after 3 warm-ups, synchronous final test readback included:
median 5.92 ms disabled / 14.62 ms enabled. This is a local observation, not a frame-time guarantee.
Compensation adds per-channel sampled histogram/covariance work and one full-resolution Gaussian
pass per processed axis. It does not add another GPU readback to the production path.
