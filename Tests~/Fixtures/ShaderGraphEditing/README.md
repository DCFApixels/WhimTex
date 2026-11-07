# Shader editing playground

Frozen copy of **hartX / Shader 1**, captured with permission on 2026-11-07:
37 nodes, 42 connections, nested Fractal branches, Caustics, height derivatives,
two lighting nodes, inline gradients and seven document lights. Canvas: 512×512.
`manifest.json` authenticates the inputs. The original TIFF is not required.
This is a workload, not a pixel oracle. Do not replace it to improve benchmark results.

## Run

From the package directory, use the regular [reviewed runner](../../RUNNING_TESTS.md):

```powershell
node Tests~/scripts/run-tests.mjs --review --profile shader-editing-playground
node Tests~/scripts/run-tests.mjs --run --profile shader-editing-playground --reviewed <fingerprint> --allow-effects windows,user-state,temp-files --project-path <project>
```

- `shader-editing-model`: verifies fixture hashes, 24 deterministic edits, valid
  generated HLSL and exact replay of the serialized plan. No native shaders.
- `shader-editing-smoke`: six edits through an owned Graph window.
- `shader-editing-sequence`: twelve edits, then the same sequence on a fresh copy.
  Run this opt-in diagnostic separately with `--id shader-editing-sequence`.
- `shader-editing-cold`: the same twelve edits with fresh node identifiers per
  traversal. Generated uniform/function names are new; unchanged branches and
  returning edits retain normal cache reuse. `symbols-0.json` records the mapping.
- `shader-cold-before` / `shader-cold-after`: serial native compilation of the
  result, required buffers and selected expensive branches. Each shader gets a
  unique fragment entry name; default lighting variant, normal optimization flags.
  These isolate compiler cost and are not the edit-to-ready metric. Branch costs
  include their upstream dependencies and must not be added together.
- `shader-parity-before` / `shader-parity-after`: original plus twelve edited
  512×512 float frames, with the fixture's active light cookies. Capture `before`
  only from the unchanged implementation and retain its source provenance. `after`
  reads the saved reference; never regenerate it from a candidate to obtain a pass.
  Five render/readback samples per frame are a CPU submission + GPU readback proxy,
  not isolated GPU timestamps. Baseline/captures live under `Temp/WhimTex/shader-parity`.
- `shader-render-pair`: alternates 31 draws of the retained old and current shader
  in one Editor callback after six warmups each. The old source bundle is selected
  by `Temp/WhimTex/shader-cold/baseline.txt`, written by the `before` native study.
  It checks exact pixels and reports a synchronized render/readback proxy with
  unchanged buffers cached, reducing between-run Editor-load differences.

The seed is 1138. The case entry `Start(runId, seed, steps, repeats)` accepts
1–24 edits and 1–2 traversals; `Replay(runId, planPath, repeats)` consumes a saved
plan directly. Use the AsyncD Poll/Cancel/Cleanup protocol when invoking manually.
Plans cover numeric values, dimensions, noise algorithm, Fractal, lighting,
disconnect/reconnect, connected output selection, insertion/removal, layout and
filtering. Targets and values are randomized within this fixed category order.
No failed edit is silently skipped or replaced.

## Measurements

The primary metric is **edit callback entry → acceptance of the current final
preview shader**, including every required Buffer program. New programs use the
resource-ready notification; retained programs use Editor-update observation.
The readiness check rejects a stale successful shader and compilation errors.
It also checks the actual material's active lighting variant, not just the default
variant. Both required buffer programs must be ready. A bounded 90-second deadline
allows the slower baseline's visible-node queue to settle without redefining the target.
**2 s is the desired target; 3 s is the upper target.** Exceeding it is reported
as a performance miss, not a failed correctness assertion.

`Temp/WhimTex/shader-editing/<runId>/` retains `plan.json` and incremental
`measurements.json`, including failures/timeouts. Reports record the final-source
hash, retained/replaced runtime, required Buffer programs, edit-call duration,
observed queue/preparation/native-start milestones, total native starts/cache hits,
maximum concurrent compilers, visible preview count and maximum observation gap.
Stage observations are not isolated CPU or GPU timings. `-1` means unobserved.
Counters are global and include competing node previews.

Secondary `allVisibleCompileReadyMs` requires all visible selected outputs to have
current programs. Rendering, pixel readback and actual paint completion are outside
both metrics. Initial `open-settle` starts after window layout and is excluded from
edit statistics; it is not a measurement of the entire window-opening operation.

Use an idle Editor and leave the owned benchmark window visible. The test fits the
graph in a 1100×760 window with HLSL previews enabled; keep window/display conditions
the same between comparisons. Do not edit other graphs while measuring.
Application and driver caches are retained. In ordinary smoke/sequence runs **the
first traversal is not guaranteed cold**, and the second is a replay, not a guaranteed
cache hit. The `cold` case uses fresh source identities, not a driver-cache flush.
Compare corresponding
edits and native-start counts, not only the aggregate median mixing cheap and costly
edits. The window case uses no shader nonce or forced synchronous compilation;
the separate native-cost study explicitly renames the fragment entry.

```powershell
node Tests~/scripts/compare-shader-editing.mjs <before/measurements.json> <after/measurements.json>
```

The comparator rejects unfinished runs, different plans, fixture hashes, visible
preview counts or device/runtime identities. It prints matched timings and compile
counts. It cannot equalize the driver cache or unrelated Editor workload.
Only test-owned windows/documents/Undo/runtime resources are released; focus and
state are restored. The source document and project assets remain untouched.
