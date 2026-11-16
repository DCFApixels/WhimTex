# Paint history readback: two versus four pages

Measured 2026-11-16 UTC (completed 2026-11-17 local), Unity 6000.7.0a6,
Direct3D12, RTX 5060. This follows the
[Undo commit comparison](PaintUndoCommitBenchmark.md).

## Status: experiment retired on 2026-11-17

At the user's request, the independent scheduler, four-page default, additional
native admission/recovery logic and experimental counters were removed. Production
uses the previous two-page FIFO scheduler. Earlier Undo commit/quota optimizations
remain unchanged. The tables below record the retired experiment, not current
production behavior or a recommendation to enable it.

Raw reports and summaries remain in the locations listed below. A source snapshot
of the retired implementation, its runner, fixtures and catalog is retained under
project `Temp/WhimTex/readback-rollback-20261117/`, with SHA256 manifests of the
preserved evidence and Undo source files. The active package no longer exposes the
experimental override/API/profiles. Generic 4096px exact-history tests remain in
the ordinary tiled Drawing suite without a scheduling override.

Rollback validation passed in the connected Editor: native script compilation,
10 Undo contract/ownership/failure cases (499 assertions), and four exact 8K
2048/4096 inside/outside cases (6576 assertions). Total: **14 cases / 7075
assertions**. Receipts are `Temp/WhimTex/test-runs/readback-rollback-undo.json`
and `readback-rollback-8k.json`. The latter scenarios' setup descriptions were
clarified afterward; their executable code and assertions did not change.

The rollback used targeted source edits, not Git reset/restore/checkout. The
production transfer and common cycle benchmark now match the pre-experiment
revision `cf94e5e`. SHA256 verification found all 49 protected Undo files and 145
original evidence files unchanged. No user document, setting or unrelated source
was edited. Historical 8K timing files were copied into `prior-8k-timings/` in the
snapshot before the verification cases wrote their new timing output.

## Historical implementation

The experiment used four outstanding GPU readback pages by default, refilling a slot when its
requests complete, independently of the FIFO CPU consumer. Keep CPU application
and history publication ordered. Completed arrays remain owned and charged until
their page or writer releases them; completion does not make their memory free.

The shared native admission ceiling is the writer allowance plus 80 MiB, matching
the previous worst-case allowance. Reserve 8 MiB for the dense CPU head's stored
before-image. The existing GPU staging, writer and idle-pool limits remain intact.
Neither the painting rasterizer, source UV, selection masks nor pixel encoding changes.

Added counters separate queue admission, observed request completion, waiting for
CPU application, CPU work, blocking GPU waits and writer admission. Existing writer
timestamps provide worker queue, compression, file and hash timings. These counters
do not allocate per frame and do not enable the optional profiler capture.

## Matched method

The original two-slot scheduler was measured first, with the same instrumentation
and native guard. Then the new scheduler was measured with two and four slots in
two rounds, reversing their order in round two. The benchmark input bundle is
byte-identical before/after. Baseline has one round; each new arm has two. This is
a local characterization, not a hardware-independent guarantee.

All 40 measured cases passed. Each uses an 8192 x 8192 source, the actual native
Brush, signed HDR colors, fixed geometry and a requested 16.667 ms input interval.
The Editor may deliver updates later. Each stamp case has eight strokes; each
spam case has three points per stroke and 24 strokes at 2048 px or 12 at 4096 px.
No history drain occurs between strokes. Outside-frame placement is checked in
the rendered canvas, and each run checks its final Undo/Redo. No Undo check was
skipped. This is API/update-loop timing, not mouse-to-display latency.

Source initialization is excluded. EndStroke includes the actual main-thread
commit and any staging-capacity wait. Final drain measures how long the remaining
asynchronous queue takes to empty after drawing stops; it is **not** a single
blocking pause or pure GPU execution time. Its explicit final FlushWrites is
reported separately in the raw reports.

## EndStroke latency

Median / maximum milliseconds, including the first stroke. New values pool both
rounds (16, 48 or 24 strokes respectively); baseline uses 8, 24 or 12.

| Scenario | Original, 2 pages | New, 2 pages | New, 4 pages |
| --- | ---: | ---: | ---: |
| 2048 stamp, inside | 9.37 / 25.80 | 10.25 / 22.55 | 10.75 / 25.82 |
| 2048 stamp, outside | 8.42 / 13.91 | 9.78 / 13.23 | 9.82 / 28.24 |
| 4096 stamp, inside | 72.35 / 522.73 | 60.15 / 469.03 | 54.33 / 327.45 |
| 4096 stamp, outside | 62.30 / 232.69 | 64.80 / 365.79 | 54.14 / 260.42 |
| 2048 spam, inside | 16.82 / 305.58 | 10.33 / 36.32 | 10.41 / 81.79 |
| 2048 spam, outside | 14.08 / 41.10 | 10.88 / 42.89 | 10.86 / 41.08 |
| 4096 spam, inside | 141.23 / 755.14 | 99.02 / 534.60 | 97.05 / 546.96 |
| 4096 spam, outside | 104.35 / 324.23 | 89.12 / 384.78 | 91.58 / 446.52 |

Four pages are not a universal EndStroke improvement. Ordinary 2048 stamps regress
slightly against baseline; several maxima worsen. In round two, inside 4096 spam
had a median of 158.47 ms with four pages versus 114.94 ms with two (round one:
91.97 versus 98.10). The retained maxima and round-level reports matter; the pooled
median alone does not describe those stalls.

## Remaining asynchronous queue

Seconds after the final stroke, median of the two new rounds:

| Scenario | Original, 2 pages | New, 2 pages | New, 4 pages |
| --- | ---: | ---: | ---: |
| 2048 stamp, inside | 2.586 | 2.426 | 1.493 |
| 2048 stamp, outside | 3.038 | 2.188 | 1.375 |
| 4096 stamp, inside | 5.601 | 5.064 | 3.467 |
| 4096 stamp, outside | 5.636 | 5.280 | 3.345 |
| 2048 spam, inside | 6.386 | 5.371 | 3.104 |
| 2048 spam, outside | 5.987 | 5.178 | 3.297 |
| 4096 spam, inside | 5.331 | 5.315 | 3.533 |
| 4096 spam, outside | 5.480 | 5.751 | 3.401 |

Four pages reduce this remaining queue time by 34–55% against the original and
32–42% against the new two-page arm. This consistent throughput benefit, under the
same memory ceiling, is the reason for the default. Saturated 4096 spam still
reaches the staging cap and can block EndStroke.

For inside 4096 spam, mean queue time before readback was 2011 ms originally,
1664 ms with new two-page scheduling and 1335 ms with four. Mean writer queue time
was under 1 ms in all three arms. Four pages increase ready-to-CPU waiting to
about 140 ms, versus 11 ms with two, as more completed data is prefetched. This
identifies where backlog moves; it does not mean the disk or CPU costs vanish.
Observed readback time includes Editor callback/poll cadence. CPU timing includes
writer admission, and blocking capacity timing can include both: do not sum these
overlapping counters as independent elapsed phases.

## Memory and correctness

- GPU immutable staging stays at or below **1536 MiB**, unchanged.
- Active native transfer/writer ceiling on this machine is **336 MiB**:
  256 MiB writer allowance plus 80 MiB. Actual peak increases from **144 MiB**
  originally to **176 MiB** with new two-page scheduling and **328 MiB** with four.
  Preserving the ceiling does not mean preserving the former typical consumption.
- The separate idle native pool stays capped at 64 MiB. Base textures, live Raster
  pages, driver caches and codec workspaces are not included in the 336 MiB figure;
  this is not a total application-memory cap.
- All measured healthy runs stayed within their native and staging limits and
  used no failed-write recovery admission.

`paint-readback-8k` passed **8133 assertions**: two/four-page 2048 strokes inside and
outside the Transform, plus four-page 4096 strokes in both positions. Three ordered
strokes include Brush and masked Pencil. Exact raw Half tile bytes, negative/HDR
channels, every Undo/Redo revision and rebuilt GPU source state are checked.

`paint-readback-safety` passed **607 assertions** across nine cases: submitted and
waiting cancellation, partial CPU-head application, FIFO ordering, synchronous
recovery of an injected failed readback, dense/sparse history, quota-release
failure and real page-write failures. The last case retains 320 MiB from actual
failed writers and verifies serialized recovery of two healthy FIFO pages.
Failed immutable payloads retain the pre-existing exceptional right to exceed
normal memory limits rather than discard Undo data. Only the blocking FIFO head
gets this recovery credit after writers join; speculative prefetch does not.

An additional masked/immediate-Undo timing series was blocked by its **4 GiB free
disk preflight** (C: had 3,662,401,536 bytes). It created no document and supplies
no timings. The protection was not lowered. Exact masked 8K and immediate native
Undo correctness are covered by the passing suites above; no fresh 8K immediate
Undo latency comparison is claimed.

## Historical reproduction and preserved evidence

- Archived runner: `Tests~/scripts/compare-paint-readback.ps1` in the snapshot above;
  it requires the retired experimental implementation. Its invocation used a unique `-Prefix`,
  `-Pages @(2,4) -Repeats 2 -Selection '^(dab|spam)'`.
- Former reviewed profiles: `paint-readback-8k`, `paint-readback-safety`,
  `paint-readback-scheduling`, `paint-readback-fault` (removed from the active catalog).
  All fixtures own their
  textures/history; test concurrency overrides restore the prior value.
- Raw reports: project `Temp/WhimTex/raster-cycle-audit/readback-before-*.json`
  and `readback-after-*.json`; generated CSV/JSON summary and baseline source
  snapshot: `Temp/WhimTex/readback-comparison/`.
- Exact receipts: `Temp/WhimTex/test-runs/readback-exact-8k.json` and
  `readback-safety-final.json`. Blocked extra: `readback-extra-r1-p2-mask2048-inside.json`.
- Before source SHA256: `1c591356b7e4ec1f45fbf159bf079d88d8a8618dd387fb75e8c463ff3622615a`.
- Retired experiment source SHA256: `a37b443c363556acb6c747bbae63389c395cc2d6131d399a49c83a11beba4e43`.
- Identical benchmark bundles SHA256:
  `183e3e0706733d513ae296d71b7be9d072a2f4c7414585dd4d814bc5d92b0d3a`.

The normal two/four paths were benchmarked before the final default/recovery-only
change. The exact and safety suites ran afterward. No old production files were
restored for A/B runs; no user document, project asset or persistent setting changed.
