# Test API and migration

Migration is local and incremental. All **466 original files** are archived byte-for-byte in
`Legacy/`, including the old runner, reports and fixtures. `legacy-manifest.json` freezes their
SHA-256 hashes and original paths from commit `1269df5`. Do not edit, delete or regenerate the
archive/manifest to make a test pass. The active `Fixtures/` is an unchanged shared data copy;
it does not mean the archived test source has been migrated.

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
| `--profile migration-pilot` | The three old/new pairs, six scenarios |
| `--all` | Every **catalogued** scenario, not the entire archived suite |

`quick` checks framework/archive/frozen files without Unity. `core` has two Unity regressions
and the frozen-file Node regression. `compatibility` is a small subset, **not** the full file
roundtrip suite. `async-protocol` checks Start/Poll/Cleanup using owned SessionState keys, not UI
scheduling. `legacy-*` profiles retain selected old scenarios during migration.
`new-unity-a/b/c/d` include explicitly declared diagnostics as well as regressions; use their
`-regressions` variants for verification. The independent `runner-live-v2` tests actual Editor
verdicts and cleanup. Source mappings classify helpers and manual diagnostics separately;
they are not silently counted as passing tests.

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

Old C# snippets/classes keep their declared entry and result protocols. Old Node cases are
copied unchanged into an isolated Temp mirror with their **original** path layout and current
repository sources. Do not execute relocated Node files directly or rewrite their imports.
Mirrors/compiled inputs are retained as report evidence; they are not new Unity assets.

## Safety and results

Unity requires explicit `--project-path`; only an idle matching Editor is accepted. No tool
starts another Editor, installs dependencies or compiles via dotnet/MSBuild. A Player build
runs only through the explicitly selected `player-release-workflow-v2` after separate human
authorization. Selecting a profile or acknowledging effects is not build permission.
Read-only Node profiles also work in a standalone repository checkout, e.g. CI.

The runner verifies all archive hashes and checks source fingerprints between scenarios.
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
Asset mutation/import/deletion still needs user authority. Do not run `legacy-export` merely
because it is catalogued. Review receipts include invocation, selected/support sources,
declared dependencies and dispatcher helpers, not a guarantee of sufficient coverage.

Node child-process restrictions (`spawn EPERM`) require the normal execution permission;
do not introduce OS-specific bypasses. Direct framework tests can run without children.

## Comparing replacements

Run `migration-pilot`, then:

```sh
node Tests~/scripts/check-migration.mjs "<paired-run-report.json>"
```

Comparison requires both cases in the **same** report, unambiguous passes, expected check
counts/facts and an explicit review of inputs, assertions and cleanup in `migration.json`.
Green results alone are not coverage equivalence. New tests must not simply call the old ones.
The comparison only proves the three declared pilot pairs. Full-suite mappings and current
runtime reports are separate evidence; gaps, manual entries and external prerequisites still
prevent automatic archive removal. `archiveRemovalAllowed` remains false. Keep Legacy intact.

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
Player validation remains a separately authorized workflow; never infer build permission.
The workflow targets the current supported Standalone platform without switching project
settings, builds only its owned scene to Temp/WhimTex, executes the built Player and verifies
runtime pixels, packed contents, saved document/meta preservation and native helper removal.

## Final Legacy inventory

`node Tests~/CoverageAudit/coverage-gate.mjs` reads the immutable archive, per-file source
reviews and retained runner reports. It checks raw source/support hashes, exact native input
bundles and invocation arguments; Node proofs also require the current selected receipt.
The latest same-source failure or SKIP overrides an earlier green. Diagnostics, compilation,
entry validation and recovery-only reports do not prove regression equivalence.
Historical image inputs require reviewed path/hash/provenance; an existing source PNG is not
an algorithm golden. Never regenerate missing pre-change references with the current renderer
to obtain a pass. A diagnostic SKIP can contain useful generated artifacts without certifying
their visual correctness.
Its inventory always keeps `archiveRemovalAllowed:false`: source review, runtime success and
historical visual/binary equivalence remain separate claims. Pure inventory tests use
`node --test --test-isolation=none Tests~/CoverageAudit/coverage-gate.test.mjs`.
