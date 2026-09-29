# Make Seamless optimization verification

Opt-in connected-Editor scripts; no player build or document changes. Captures and comparisons use transient textures. Numerical claims below apply to the tested Editor/backend, not every device.

## Run

Use Unity Pipeline `run_script` with the explicit project path and these file/entry pairs:

| File | Entry | Purpose |
| --- | --- | --- |
| SeamlessOptimizationSmoke.cs | SeamlessOptimizationSmoke.Capture | Capture the pre-change baseline once |
| SeamlessOptimizationSmoke.cs | SeamlessOptimizationSmoke.Compare | Compare current output with that baseline |
| SeamlessOptimizationSmoke.cs | SeamlessOptimizationSmoke.Benchmark | Warm end-to-end timing, including final GPU readback |
| SeamlessOptimizationSmoke.cs | SeamlessOptimizationSmoke.PairedPoisson | Alternating frozen/current solver timing and output comparison |
| HistogramArithmeticAudit.cs | HistogramArithmeticAudit.Main | Managed/Burst LUT and covariance agreement |
| HistogramArithmeticAudit.cs | HistogramArithmeticAudit.Memory | Reuse, nested ownership, idle expiry and disposal |

Example from the project root:

```powershell
unity command run_script --file 'Packages/com.dcfapixels.whimtex/Tests~/SeamlessOptimizationSmoke.cs' --entry SeamlessOptimizationSmoke.Compare --project-path 'D:/DCFA/Projects/Test6.6' --format json
```

`Capture` writes float snapshots under `output/seamless-optimization`. Do not run it on the optimized version to validate the optimization: that would overwrite the independent baseline. Snapshots were captured before changing production code. Without those local snapshots, run capture on the pre-change source first. `PairedPoisson` contains a frozen orchestration/FFT-planning baseline and uses the unchanged production shaders.

## Scope and precision

- Mirror/Offset: unchanged stratified sample selection (at most 128²), 4096-entry transfer tables, covariance formulas and GPU blend. Channel preparation now runs in parallel strict Burst jobs. Native image scratch, readback and LUT textures are reused by one idle owner; nested rents remain isolated. Storage expires after 30 seconds or reload/quit. Maximum retained CPU arrays are about 2.2 MiB, plus textures.
- Quantiles depend only on sample count. Histogram uses the original managed evaluation cached by count; Quilting caches its original Burst evaluation inside the existing workspace budget. Changing the source still rebuilds distributions/covariance. No sampled image is reused incorrectly.
- Straight float-only Burst conversion was rejected: transparent HDR output amplified changed LUT rounding. Explicit widened intermediate arithmetic before float storage preserves the original results; this is covered by transparent/HDR fixtures, not only opaque noise.
- Poisson keeps 16 iterations, convergence guards, boundary conditions, float textures and all FFT passes. Cached factorization metadata (at most 16 plans) and direct reduction/scratch swaps remove setup work and 67 texture-copy calls per solve. Tiny managed uniform arrays are required by Unity's material API; pixel scratch remains native.
- Quilting keeps donor candidates, ordering, seed, cost accumulation, cuts, quality budgets and search resolution. Wrapped indexing uses one conditional subtraction in place of integer remainder within its proven range.
- Ordinary uncompensated Mirror was already a cheap GPU path and remains unchanged. No shader approximations, precision reductions, asynchronous result changes or document migration were introduced.

## Results, 2026-09-28

- 252 captured rendered fixtures: maximum component difference **0**. Covers all four methods, compensation on/off where applicable, all three edge directions, 1×7/17×13/64×48/65×63, gray, color, constant, HDR and transparent inputs.
- 324 frozen/current Quilting equivalence cases: maximum difference **0**.
- Histogram arithmetic audit: LUT and covariance statistics identical; workspace reuse, nested isolation and expiry/disposal passed.
- Independent Screened reference: maximum error 1.19209e-6; large/NPOT cases (600², 257×511, 1024×512, 4×1024): 8.9407e-7.
- Histogram, Quilting compensation/memory, empty edges, Mirror integration and all Make Seamless compositor integration checks passed, including group input, clipping, thumbnails, export and caches.

Warm 512² colored/HDR/alpha source, all edges, nine samples after three warm-ups, median milliseconds including final GPU readback:

| Path | Before | After |
| --- | ---: | ---: |
| Mirror, compensation on | 39.583 | 6.284 |
| Offset Blend, compensation on | 40.282 | 6.234 |
| Quilting High/Independent, compensation off | 11.030 | 12.194 |
| Quilting High/Independent, compensation on | 23.110 | 22.727 |

The compensated histogram paths improved substantially in repeated warm runs (roughly 6–7 ms). Small differences elsewhere are not evidence of a reliable speedup: Editor/GPU load varies, and these before/after runs were not paired. Cached quantiles and simplified indexing remove repeated work, but the overall Quilting gain was not demonstrated by this measurement.

Alternating paired Poisson runs (15 samples after warm-up), baseline → optimized medians: All Edges 31.295 → 32.058 ms; Top & Bottom 48.433 → 48.490 ms; Left & Right 48.379 → 46.775 ms. This confirms no substantial overall solver speedup in this test despite reduced CPU preparation and copy submission. Do not advertise a universal Poisson timing improvement.

Thread-local managed allocation counters returned zero even for known allocations in this runtime, so they were discarded. No zero-GC claim is made. The retained scratch-memory tradeoff is explicit above.
