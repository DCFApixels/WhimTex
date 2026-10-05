# Test API and migration

All **466 original files** have immutable hashes/paths in `legacy-manifest.json`.
`archive-descriptor.json` pins their complete recovery snapshot at commit
`ca8603c0961ce36064280f952259f8a6142d46cc`; the older `baselineCommit` is not the complete
final archive. Active dispatch never reads or executes the physical `Legacy/` folder.
Archive integrity and source audits authenticate the pinned Git blobs against every frozen
hash and byte count. Missing Git objects fail verification; no working-copy fallback exists.
Never edit historical bytes or regenerate the manifest to make a test pass. Physical
Legacy retirement requires explicit human authorization, completed final validation and
a verified recoverable copy; the pinned Git originals remain available afterward. The active
`Fixtures/` is independent required test data and must remain after archive retirement.

`Framework/` contains test-only C#/Node helpers; `Cases/` contains replacement cases;
`scripts/test-catalog.json` owns scenario invocation metadata. `migration.json` tracks original
files and explicit old/new assertion coverage. All **360 source files** now have an explicit
replacement/support/diagnostic mapping in `Batches/`; this is not proof that every entry has
passed or that all coverage gaps are closed. See `MigrationFull.ru.md` for verification limits.
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
| `--profile new-node` | Independent read-only Node tests; no Unity |
| `--profile new-unity-b-regressions` | Independent document/import/render cases, diagnostics excluded |
| `--profile archive-detachment` | Opt-in GUID Temp mirror without Legacy; requires Git and `temp-files` acknowledgement |
| `--all` | Every **catalogued** scenario, not the entire archived suite |

`quick` checks framework/archive/frozen files without Unity. `core` has two Unity regressions
and the frozen-file Node regression. `compatibility` is a small subset, **not** the full file
roundtrip suite. `async-protocol` checks Start/Poll/Cleanup using owned SessionState keys, not UI
scheduling. Old `legacy-*` and `migration-pilot` profiles have been retired. Their original
metadata is preserved in `retired-catalog.json`, not registered for execution.
`new-unity-a/b/c/d` include explicitly declared diagnostics as well as regressions; use their
`-regressions` variants for verification. The independent `runner-live-v2` tests actual Editor
verdicts and cleanup. Source mappings classify helpers and manual diagnostics separately;
they are not silently counted as passing tests.

Gradient reload is one paired lifecycle: select `--id whimtex-gradient-reload-v2` to execute
Begin → native Trigger → ReloadPoll → End → Cleanup with the same GUID. The two remaining
standalone `whimtex-gradient-reload-begin-v2` / `-end-v2` entries are historical helper mappings,
not independently runnable coverage: generic cleanup destroys Begin's fixture, and selecting
both entries does not insert a real reload. Their separate inventory rows remain pending;
the current wrapper's phase receipt is documented in `CoverageAudit/gradient-reload-helper-phases.json`.

## Compile and validate entry points

Use the same reviewed selector and explicit project for these separate actions:

```sh
node Tests~/scripts/run-tests.mjs --compile --profile new-unity-b --reviewed "<fingerprint>" --project-path "<project-root>"
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

New cases return one structured result: `status`, nonnegative `checks`, `message`, `failures`;
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

Active scenarios require the structured protocol, including cleanup. Archived case/support/
review paths are rejected, including normalized path aliases. Synthetic historical metadata
in `Framework/Fixtures/HistoricalProtocol.mjs` exercises negative text/JSON/exit classifiers
through mock invokers only; those fixtures do not register or dispatch archived code.
The `archive-detachment` mirror copies current independent sources but omits Legacy. It
retains its GUID-owned Temp files/reports as evidence; it does not rename the real archive.

## Safety and results

Unity requires explicit `--project-path`; only an idle matching Editor is accepted. No tool
starts another Editor, installs dependencies or compiles via dotnet/MSBuild. A Player build
runs only through the explicitly selected `player-release-workflow-v2` after separate human
authorization. Selecting a profile or acknowledging effects is not build permission.
Read-only Node profiles also work in a standalone repository checkout, e.g. CI.

The runner validates immutable archive metadata and checks source fingerprints between
scenarios. It does not imply archive content verification: reports explicitly record
`contentsVerified:false`. Select `archive-integrity` for authenticated pinned Git bytes.
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

## Comparing replacements

Historical pilot comparison is read-only; the retired six-case profile cannot be dispatched:

```sh
node Tests~/scripts/check-migration.mjs "<paired-run-report.json>"
```

Comparison requires both cases in the **same** report, unambiguous passes, expected check
counts/facts and an explicit review of inputs, assertions and cleanup in `migration.json`.
Green results alone are not coverage equivalence. New tests must not simply call the old ones.
The comparison authenticates only the three historical pairs using fixed raw-report,
retired-catalog and coverage-mapping hashes. It never recalculates the old fingerprint with
current sources: `currentRuntimeEquivalence:false`. The pre-retirement 402-PASS snapshot is
retained in `CoverageAudit/pre-retirement.results.json`; fresh limited runs are separate.
The gate does not grant deletion authority: `archiveRemovalAllowed` remains false.

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
The authorized 2026-10-05 run used ag-psd 31.0.2 under project
`Temp/WhimTex/psd-reader-20261005-9c771397/node_modules/ag-psd/dist/index.js` and passed.
See `CoverageAudit/psd-reader-final-runtime-review.json` for provenance. Installation
remains a separate permissioned operation; the runner never installs a decoder itself.
Player validation remains a separately authorized workflow; never infer build permission.
The workflow targets the current supported Standalone platform without switching project
settings, builds only its owned scene to Temp/WhimTex, executes the built Player and verifies
runtime pixels, packed contents, saved document/meta preservation and native helper removal.

## Final Legacy inventory

`node Tests~/CoverageAudit/coverage-gate.mjs` reads the immutable archive, per-file source
reviews and retained runner reports. It checks raw source/support hashes, exact native input
bundles and invocation arguments. Both native and Node proofs require the current selected
receipt, including the dispatcher/archive-descriptor dependencies. Matching a native bundle
alone cannot promote an old report after retirement. All earlier evidence remains historical.
The latest same-source failure or SKIP overrides an earlier green. Diagnostics, compilation,
entry validation and recovery-only reports do not prove regression equivalence.
Historical image inputs require reviewed path/hash/provenance; an existing source PNG is not
an algorithm golden. Never regenerate missing pre-change references with the current renderer
to obtain a pass. A diagnostic SKIP can contain useful generated artifacts without certifying
their visual correctness.
Its inventory always keeps `archiveRemovalAllowed:false`: source review, runtime success and
historical visual/binary equivalence remain separate claims. Pure inventory tests use
`node --test --test-isolation=none Tests~/CoverageAudit/coverage-gate.test.mjs`.

`workflowPhaseCoverage` records the verified same-GUID native gradient Begin/End phases
from the complete raw reload receipt. It never promotes those paired helpers to standalone
PASS. Use `whimtex-gradient-reload-v2`, not separate Begin/End runs.

`color-picker-eyedropper-native-inspection-v2` is an opt-in15-second diagnostic. The public
`InspectNativeSession(runId)` getter is repeatable between serial lifecycle calls; raw Poll
responses retain fresh-assembly observations. Do not inspect in parallel with another Unity
CLI invocation. Joined Cancel/Cleanup closes only this test's picker and releases its GUID.
Fixture/capture success is not a native sampling-duration or image-golden verdict.

Seamless Release `Status` is an explicit SKIP/redirect: actual synchronous Stress outcomes and
timings live in the selected Stress raw receipt after cleanup, not the old shared SessionState.
Historical Capture must never regenerate pre-change references with the current renderer.
Runner/catalog retirement is complete; see `ArchiveRetirement.ru.md`. Physical removal is a
separate authorized operation after preserving the current dirty/untracked review evidence.
Keep the descriptor, frozen manifest, active fixtures, migration mappings and historical receipts.

Generated diagnostics must retain their actual samples/artifacts before owned cleanup.
Successful timing/manual producers can still return SKIP; a saved PNG, CSV or timing array
does not establish historical equivalence or a performance acceptance threshold. Unavailable
process-memory counters must be labelled unavailable, not interpreted as zero memory usage.
