# Running WhimTex tests

Start with the [test map](README.md): `Cases/` is organized by subsystem, `Framework/`
contains shared support, and `Fixtures/` contains required inputs. The explicit
`scripts/test-catalog.json` owns IDs, invocation metadata and limited profiles; directory
contents are not automatically discovered as runnable tests.

Tests are self-contained in the package: they do not require a Legacy archive, migration
maps, Git objects or the old project's `output` directory. Frozen reference inputs and
their provenance are in [Fixtures/Oracles](Fixtures/Oracles/README.md).
Ordinary cases use ephemeral test assemblies. Opt-in native-fixture/Player workflows
temporarily install GUID-owned native scripts under the authorized test subtree.
Explicit human permission covers test-only internal bindings for eyedropper, dock/tab,
profiler, and ReadScreenPixel on test-created windows. It does not authorize other internals.

## Inspect and select

From the package root:

```sh
node Tests~/scripts/run-tests.mjs --list
node Tests~/scripts/run-tests.mjs --review --profile core
```

Read selected sources and declared dependencies in full. Copy the current fingerprint, then run:

```sh
node Tests~/scripts/run-tests.mjs --run --profile core --reviewed "<fingerprint>" --project-path "<project-root>"
```

Choose **one** selector. None expands into unrelated scenarios or runs inferred dependencies:

| Selector | Scope |
| --- | --- |
| `--id canonical-reader-v2` | One scenario |
| `--ids canonical-reader-v2,display-channels-v2` | Exact list, in this order |
| `--group rendering` | Scenarios with this explicit group |
| `--profile quick` | Reviewed named subset |
| `--profile node` | Independent read-only Node tests; no Unity |
| `--profile documents` | Document/import cases and source guards, diagnostics excluded |
| `--profile regressions` | Only regression scenarios |
| `--profile source-guards` | Only source/contract guards |
| `--profile verify` | All non-diagnostic scenarios, including runner infrastructure |
| `--all` | Every **catalogued** scenario, including diagnostics and permissioned workflows |

`quick` checks the runner, 0.12.5 files and all registered references without Unity. `core` has two Unity regressions
and the frozen-file Node regression. `compatibility` is a small subset, **not** the full file
roundtrip suite. `async-protocol` checks Start/Poll/Cleanup using owned SessionState keys, not UI
scheduling.
Feature profiles such as `documents`, `layers`, `fx` and `painting` exclude diagnostics;
`diagnostics` selects them explicitly. The independent `runner-live-v2` tests actual Editor
verdicts and cleanup. Diagnostics are not silently counted as passing regressions.

Gradient reload is one paired lifecycle: select `--id whimtex-gradient-reload-v2` to execute
Begin → native Trigger → ReloadPoll → End → Cleanup with the same GUID. The two remaining
standalone `whimtex-gradient-reload-begin-v2` / `-end-v2` entries are historical helper mappings,
not independently runnable coverage: generic cleanup destroys Begin's fixture, and selecting
both entries does not insert a real reload. Use the complete paired workflow for coverage.

## Compile and validate entry points

Use the same reviewed selector and explicit project for these separate actions:

```sh
node Tests~/scripts/run-tests.mjs --compile --profile documents --reviewed "<fingerprint>" --project-path "<project-root>"
node Tests~/scripts/run-tests.mjs --check-entries --id psd-writer-v2 --reviewed "<fingerprint-for-this-id>" --project-path "<project-root>"
```

`--compile` uses Unity's `run_script dry_run`: the assembly is not loaded and no assertions
run. Node uses syntax checking. `--check-entries` loads reviewed C# into an ephemeral assembly
and reflects only its own test types: public/static entries, unique method names (private
overloads also matter to Pipeline), and argument counts for every lifecycle phase. It invokes
no scenario body. Reports distinguish `compiled`, `entry-validated`, and executed `passed`.
Entry validation requires a C#-only selector; subsystem profiles containing Node reload
orchestrators must be narrowed with `--id` / `--ids`. Review each chosen selector separately.

## Common API

Cases return one structured result: `status`, nonnegative `checks`, `message`, `failures`;
Node can also include named `cases` and comparison `facts`. Statuses are `running`, `passed`,
`failed`, `skipped`, `cancelled`. Passed results cannot include failures. SKIP/cancellation
never count as an all-green run. Empty cases and Node case lists fail.

- C#: one static `Run` calls `TestContext.Run`; use `True`, `Equal`, `Near`. The body owns
  explicit using/finally cleanup. A cleanup exception prevents a passed verdict.
- Node: register named cases in `TestContext`, use its `assert`, finish once. Cases execute
  serially. stdout contains exactly one `WHIMTEX_TEST_RESULT <JSON>` marker; exit must agree.
- Async C#: explicit `Start`, `Poll`, optional `Cancel`, and `Cleanup`; pass a per-run GUID
  instead of sharing generic state keys. Running is not success. Cancellation must actually
  stop and drain owned work/finally before acknowledgement. UI batches use test-owned
  carriers/BCL bridges; Poll never relies on statics in a freshly compiled assembly.

Pipeline compiles one ephemeral input file. The runner concatenates the selected case and
explicit support sources under project `Temp/WhimTex/test-runs/`, with source line mappings
and hashes in the report. Global imports are deduplicated before types; source line mappings
remain intact. Ordinary cases do not install a production test bridge. Asset tests declare
their imports. Actual reload tests defer one supported
`CompilationPipeline.RequestScriptCompilation()` until after Trigger acknowledgement,
poll Editor status without running C# while compiling, then require the persisted real
before-reload marker and loss of the old AppDomain callback. A CLI `up_to_date` response
or rebuilding the UI alone is never accepted as domain-reload evidence.

All scenarios require the structured protocol, including cleanup. Text PASS/FAIL strings,
bare JSON success and exit codes cannot replace assertion results. Executable sources must
belong to Cases/Framework (except the explicitly declared current PSD writer); path aliases
cannot turn input data or scripts into executable test sources.

## Safety and results

Unity requires explicit `--project-path`; only an idle matching Editor is accepted. No tool
starts another Editor, installs dependencies or compiles via dotnet/MSBuild. A Player build
runs only through the explicitly selected `player-release-workflow-v2` after separate human
authorization. Selecting a profile or acknowledging effects is not build permission.
Read-only Node profiles also work in a standalone repository checkout, e.g. CI.

The runner checks reviewed source fingerprints between scenarios; no archival checks are
part of listing, reviewing or running current tests.
Reports separate transport, API, compile/execute, assertion, protocol, timeout and cleanup
failures. By default the first non-pass stops the profile; remaining scenarios are `notRun`.
Explicit `--keep-going` collects known failures, but still stops after uncertain execution,
cancellation or cleanup. Failed bodies and failed cleanup are recorded separately.
Only that selected set can be reported green. Production hashes cover package `src/`, not
every external project dependency or user action; coordinate Editor use separately.

Runs are serial under `Temp/WhimTex/test-runs/runner.lock`. Ambiguous Unity timeouts retain
the lock: killing the CLI does not stop Editor work. Cancel has a separate wait budget;
even successful cancellation/cleanup does not erase earlier uncertainty. Inspect completion
before removing the exact lock or retrying. The lock does not block other tools or the user.

An inner lifecycle may report `recoveryRequired:true` when its own journal or cleanup is
unresolved. This retains uncertainty and the outer lock even if the result is malformed or
contradictorily says `passed`. The dispatcher does not then replay generic Cancel/Cleanup:
inspect and recover the exact owned lifecycle first. A completed cleanup never hides the
original assertion failure.

`--allow-effects windows,user-state` acknowledges declared effects; it grants no permission.
Asset mutation/import/deletion still needs user authority. Review receipts include invocation, selected/support sources,
declared dependencies and dispatcher helpers, not a guarantee of sufficient coverage.

Node child-process restrictions (`spawn EPERM`) require the normal execution permission;
do not introduce OS-specific bypasses. Direct framework tests can run without children.

## Documentation CI

Documentation CI runs `node Tests~/scripts/ci-docs.mjs`: the existing eleven read-only Node
checks through independent `ci-docs`, with a programmatic receipt for the checked-in CI profile.
It rejects Unity and effectful metadata; it is not an interactive permission bypass.

Exit codes: 0 selected set passed; 1 run/preflight failed; 2 invalid invocation/catalog/receipt
or lock acquisition. PowerShell automation should forward `$LASTEXITCODE`.

## Optional prerequisites

`fault-release-workflow-v2` owns installation, native reload verification, Faults/Deferred
assertions, removal and another verified native reload. Its generated postprocessor can
trigger only within its exact GUID folder. Its explicit `native-fixture` workflow has a
bounded 180-second lifecycle budget; ordinary scenarios remain limited to 60 seconds.
Installing/removing this fixture requires human asset authority. On uncertain completion,
retain the lock and fixture, inspect the exact recorded GUID, and never replay a mutation.

The HLSL grammar Node test uses an already installed TextMate/Oniguruma runtime specified by
`WHIMTEX_VSCODE_APP` (the installation's `resources/app` directory). No dependency is installed
automatically; an unavailable runtime is a non-green prerequisite skip. The optional PSD
reader source accepts an explicit preinstalled reader module and generated fixture path.
Select `--id psd-reader-roundtrip-v2` with `--allow-effects temp-files` and an explicit
matching project. Set `WHIMTEX_PSD_READER` to the installed module's absolute entry path;
the workflow creates its own GUID PSD through the native Editor before decoding it.
Installation remains a separate permissioned operation; the runner never installs a decoder itself.
Player validation remains a separately authorized workflow; never infer build permission.
The workflow targets the current supported Standalone platform without switching project
settings, builds only its owned scene to Temp/WhimTex, executes the built Player and verifies
runtime pixels, packed contents, saved document/meta preservation and native helper removal.

## Reference inputs and diagnostics

`--id reference-fixtures` authenticates all 255 registered files, their dimensions/layouts
and reference-reader safety contracts without Unity. `ReviewedOracle` authenticates bytes
again before native use. The reference paths are relative to package Fixtures, not absolute
machine paths. Missing or altered files are errors; no automatic generation or substitution.
Source PNG/live inputs are not algorithm output goldens. Never regenerate pre-change
snapshots with the current renderer to obtain a pass. Diagnostics, compilation, entry
validation and old reports do not prove current regression or visual equivalence.

`color-picker-eyedropper-native-inspection-v2` is an opt-in15-second diagnostic. The public
`InspectNativeSession(runId)` getter is repeatable between serial lifecycle calls; raw Poll
responses retain fresh-assembly observations. Do not inspect in parallel with another Unity
CLI invocation. Joined Cancel/Cleanup closes only this test's picker and releases its GUID.
Fixture/capture success is not a native sampling-duration or image-golden verdict.

Seamless Release `Status` is an explicit SKIP/redirect: actual synchronous Stress outcomes and
timings live in the selected Stress raw receipt after cleanup, not the old shared SessionState.
Historical Capture must never regenerate pre-change references with the current renderer.
Original tests and completed migration evidence remain only in Git history; recovery
references are in the [test map](README.md#история).

## Generated artifacts

`distance-round-contour` is an opt-in performance comparison, not a regression
verdict. It measures seven fresh-resource builds per mode at 512/1024 on a star,
36 rings and native HintedAA text, alternating Round/Contour AB/BA pairs. The
source raster/upload is shared and untimed; no field result or geometry is reused.
The Editor/compiler state and Unity RT pool remain shared. Reports retain the
resolved font, CPU field phase, full Render and synchronous completion/readback.
Use `--allow-effects temp-files --keep-going` after review; successful diagnostic
sample collection returns SKIP. Files are under `Temp/WhimTex/distance-fields/performance`.

`distance-contour` checks the separate Contour AA mode: distances to raw finite
threshold segments, soft diagonal ramps, circles/rings, ambiguous cells, exact
threshold contacts, small islands, degenerate dimensions, settings and native
SDF/Outline render/UI. It does not replace Round AA or AAEDT. The text diagnostic
is `Temp/WhimTex/distance-fields/contour/text-source-round-aaedt-contour.png`:
columns are source, Round AA, AAEDT and Contour; row pairs are Hinted AA,
Antialiased and Pixel, each with identity then fractional layer translation.
These generated comparisons are not goldens or proof of universal superiority.
Use `--allow-effects temp-files,windows,user-state` after reviewing its sources.

`distance-aaedt` checks the additional AAEDT + 8SED mode: bounded independent
coverage/geometry tests, a native comparison with Round AA on the authenticated
fifth star at thresholds 77/128/192, existing six-mode numerical regressions, and
eight-mode settings/render/UI checks. The comparison sheet and CSV go to
`Temp/WhimTex/distance-fields/aaedt`: thresholds left to right, Round AA above,
AAEDT below. The general SDF/Outline sheet is `eight-modes.png`. These outputs are
diagnostics, not accuracy or speed rankings. Select this profile with
`--allow-effects temp-files,windows,user-state` after reviewing its sources.

`distance-merge` is the opt-in contour-merge regression profile. It compares merge
0/1/2 for Sharp/MTSDF: bounded geometry, seven analytic rasters (42 CPU calls), and
18 native renders of the authenticated fifth capture at thresholds 77/128/192.
It also runs the six-mode kernel/boundary, settings roundtrip and render/UI cases.
Contact sheets and delta CSV go to `Temp/WhimTex/distance-fields/merge`; columns are
0/1/2 pixels, top row Sharp, bottom row MTSDF. These are diagnostic comparisons,
not output goldens or evidence that merging restores sharpness. Caller documents
are untouched; the UI test uses an owned transient window. Select this profile
with `--allow-effects temp-files,windows,user-state` after reviewing its sources.

The `distance-fields` profile includes independent raster-angle and five captured-input
checks in addition to the kernel, UI and render cases. `DistanceRasterCornerTests`
checks source hashes and analytic geometry, never newly rendered output goldens.
`DistanceCornerFitTests` still compares fit-window arithmetic with the frozen original;
whole-contour recovery is no longer asserted bit-identical after the raster-quality changes.
The fifth originally failing input is retained in [DistanceRaster](Fixtures/DistanceRaster/README.md)
and participates in the captured suite and `distance-safety-v1`. The three `distance-safety-*`
entries add independent finite-contour bounds, full RGB interpolation before median,
common-angle tips, thin strokes and holes. Their oracle excludes only raw/independent analytic
near-boundary bands, never bands derived from the current output. Each body stays within 30s;
35/106/60 production calls respectively. Raster reconstruction may extend a sampled-off tip,
so a raw-mask-only sign oracle is not valid at that independently known analytic boundary.
Sharp resolves ownership per contour before combining fields; these tests do not demand
unlimited acute miters or treat the Euclidean-nearest island as the sole artistic contributor.
The 15-case profile passed on 2026-10-13, receipt `2026-10-13T08-18-49.604Z-82572.json`.
See F19 for the finite-distance compromise and measured cold costs.

Runner reports, native input bundles and recovery logs stay under project `Temp/WhimTex`.
Do not copy them into `Tests~` or commit expanded inventory snapshots. The package ignores
generated result files. Fixture manifests and provenance are required input data, not reports.
A compact human summary can link to the historical Git snapshot without embedding raw results.

Generated diagnostics must retain their actual samples/artifacts before owned cleanup.
The opt-in `distance-field-performance` profile measures actual SDF/Outline Render
calls on owned antialiased star/ring inputs, bypassing the outer document cache.
Review the profile, then run with its receipt, `--allow-effects temp-files` and
`--keep-going` to collect all diagnostic SKIPs. Raw JSON is retained in
`Temp/WhimTex/distance-fields/performance`; nested phase times overlap. Render time
includes input readback/output submission, not final GPU completion. The separate
completion readback includes full RGBAFloat transfer and is not GPU-only timing.

`distance-field-cold` selects six opt-in diagnostics with zero algorithm warmups:
SDF star 512/1024/2048, Outline star 512/1024 and SDF rings 512. Each sample creates
a new behaviour/material/LUT and rebuilds the field, geometry and scratch buffers;
it does not reuse a persistent result cache. The source fixture, running Editor,
compiler state and Unity's global temporary-RT pool remain shared: this measures
cold resource construction, not a cold process/JIT or first shader compilation.
The same review, `--allow-effects temp-files` and `--keep-going` requirements apply.
Raw reports retain `creationMs`, `renderMs`, separate `completionReadbackMs`,
`firstSampleForMode` and every sample, so compare creation + Render consistently.
Cleanup is outside this construction interval. Diagnostics return SKIP even when
all timing samples were collected; compare the matching production fingerprints
with a separately passed `distance-fields` quality run.

`distance-reconstruction` checks the experimental Corner Fit AA and Spring Simulation
modes independently of the legacy Sharp/MTSDF guards. It includes exact pixel-area
oracles, an AA-capped triangle that must actually recover a corner, 10–90 degree
corners at 16–64 pixels, rotations/subpixel phases, holes, thin bars, tiny islands,
channel selection, binary/ambiguous fallbacks and the authenticated false-ray input.
`distance-spring-contribution-v1` compares 12 independently rasterized CornerFit/Spring
pairs and requires measurable Spring influence in the final field. The existing sign,
supported-side accuracy and remote-extension bounds also apply to these inputs.
The native comparison writes `distance-fields/reconstruction` under project Temp;
its single-render times are diagnostics, not a controlled performance benchmark.
The final native render check covers all ten modes and writes `ten-modes.png`.
Use the normal reviewed receipt and declared effect grants; no borrowed working
document is changed. A passed matrix is not a guarantee of recovering lost geometry.

`distance-preparation` covers retained CornerFit/Spring preparation and distance evaluation.
The native render-cache case compares 512px range/width and miter edits against fresh full
rebuilds, checks phase counts and all rendered channels, and writes `preparation/interaction.csv`
under project `Temp/WhimTex/distance-fields`. Render times exclude the subsequent verification
readback and are interaction diagnostics in the same Editor, not cold-process benchmarks.

`distance-corner-parameters` includes an opt-in comparison with this session's pre-change
native float snapshot. `distance-corner-parameters-capture-v1` is a diagnostic producer:
run it only BEFORE changing the production kernel; it refuses overwrite and returns SKIP.
`distance-corner-parameters-v1` authenticates the frozen SHA256 and checks the 24 CornerFit
fields bitwise. The 24 Spring fields now record historical differences, without asserting
equivalence: Spring deliberately stopped rejecting relaxation against source AA coverage
after that snapshot. It records prepared-kernel timings and differences in project
`Temp/WhimTex/distance-fields/corner-parameters`.
If the session snapshot is unavailable, comparison is SKIP; it must never be rebuilt using
the optimized kernel. Portable analytic/render/cache regressions remain independently runnable.

Successful timing/manual producers can still return SKIP; a saved PNG, CSV or timing array
does not establish historical equivalence or a performance acceptance threshold. Unavailable
process-memory counters must be labelled unavailable, not interpreted as zero memory usage.
