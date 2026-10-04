# Opt-in test profiles

Run from the **package root**. These scripts do not install dependencies, start an Editor, build a player or run automatically on import.

The [catalog](scripts/test-catalog.json) is a small reviewed pilot, **not the full regression suite**. Each scenario declares its invocation, prerequisites, effects, cleanup, timeout and final-result protocol. [The runner](scripts/run-tests.mjs) uses that contract, not filename heuristics. [audit-sources.mjs](scripts/audit-sources.mjs) remains a read-only inventory helper.

## Inspect, then run

```sh
node Tests~/scripts/run-tests.mjs --list
node Tests~/scripts/run-tests.mjs --review --profile core
```

Read the selected sources and declared review dependencies in full. Check the current implementation, user state and authority for the effects. Review is not an approval mechanism or a replacement for source inspection.

Copy the current fingerprint from that review output:

```sh
node Tests~/scripts/run-tests.mjs --run --profile core --reviewed "<fingerprint-from-review>" --project-path "<project-root>"
```

Replace placeholders with actual values. Every Unity invocation targets that explicit project. The runner checks that this package belongs to its Packages directory and the connected Editor is idle, not compiling/reloading/playing. It does not fall back to another Editor or install Pipeline.

Changing a selected source, declared dependency, scenario protocol or the dispatcher invalidates the fingerprint. Sources are checked again before each scenario. This receipt does not freeze all production dependencies or establish that the selected checks are sufficient for a change.

Use `--id <scenario-id>` instead of `--profile` to select one scenario. Running without explicit selection is rejected. `--list` also reports uncatalogued top-level C#/Node files; they are not classified as disposable, diagnostic or safe-to-run just because they are absent from the catalog. Nested suites need their own inspection too.

## Profiles

| Profile | Scope | Effects requiring explicit acknowledgement |
| --- | --- | --- |
| runner-unit | 24 mock/catalog/dispatcher test cases; no Unity connection | None |
| source | R01–R04 and final legacy source guards | None |
| core | Six in-memory reader/render/copy regressions | None beyond owned in-memory resources |
| layer-preview | Existing scheduled Layer Preview regression | windows,user-state |
| export | TIFF fixture and API raster export parity | assets,temp-files,user-state |
| runner-protocol | Synchronous and polled positive protocol fixtures | user-state |
| runner-live | Deliberate negative protocol fixtures through the Editor and cleanup verification | user-state |

For example, after reading and receiving authority for the relevant effects:

```sh
node Tests~/scripts/run-tests.mjs --review --profile layer-preview
node Tests~/scripts/run-tests.mjs --run --profile layer-preview --reviewed "<fingerprint>" --project-path "<project-root>" --allow-effects windows,user-state
```

`--allow-effects` records acknowledgement, **not new permission**. Project/user rules still apply. Do not run the export profile without authority to create/import/delete its temporary assets or while the user is editing Undo state. Preconditions and teardown descriptions are reviewed instructions; the runner cannot prove every behavior of arbitrary test code.

## Results and failures

Runs are serial under a per-project `Temp/WhimTex/test-runs/runner.lock`. A second runner refuses that lock. This does not lock other agents/tools or prevent the user from starting compilation: coordinate Editor use separately.

Machine reports are written under `Temp/WhimTex/test-runs/`. They include the reviewed fingerprint, selected scenarios, Editor preflight, actual command envelopes, each attempt/poll/cleanup, final verdicts and unrun scenarios. `--output` may select a **new** JSON file directly in that directory; existing reports and source files are never overwritten by that option.

The runner distinguishes transport/API/compile-execute failures, assertions, missing final verdict, SKIP, timeout and cleanup failure. `Started` and `Running` are never passes. A Node process must finish successfully; explicit SKIP or a nonzero Node-test skipped count is not an all-green result. Categories distinguish regressions, source guards, runner checks and diagnostics; a measurement is not a behavioral regression just because the process exited successfully.

The first non-pass stops the profile, including SKIP. Remaining scenarios are recorded as `notRun`. Exit code `0` means every selected scenario passed; `1` means a run/preflight failed; `2` means invalid invocation/catalog/review selection or lock acquisition failed. PowerShell automation should forward the native `$LASTEXITCODE`, not infer it from a surrounding shell's success flag.

The scenario timeout bounds the CLI wait and async polling. `run_script` also receives a bounded `timeout_ms`. CLI `--timeout` is **seconds**, not the `eval_file` milliseconds parameter: snippets currently use Pipeline's own default evaluation timeout. Long snippets require a separately reviewed invocation, not blindly increasing the CLI wait.

On an ambiguous Unity timeout or failed async transport, the report marks uncertainty and retains the lock. Stopping the CLI does **not** stop an Editor task. Inspect actual completion and cleanup before removing that exact lock; being reachable/not compiling is not proof that a detached task finished. Do not automatically retry adds, strokes or file writes. A successful cleanup call does not make an earlier timeout a pass.

The existing Layer Preview test owns its cleanup in the scheduled callback but has no external cancel entry. A timeout there requires inspection. The protocol fixture has an explicit cleanup entry; it erases only its dedicated SessionState keys. No general-purpose closure of user windows or deletion of user assets is attempted.

If this agent environment blocks Node child-process creation (`spawn EPERM`), obtain the normal execution permission. Do not add platform-specific bypass code or weaken failure checks. Offline logic can be checked directly with `node Tests~/TestRunner.test.mjs`; live profiles require child-process permission and the correct Editor.

## Extending the catalog

1. Read the complete test, its invoked helpers and dependencies; determine the real final verdict, effects and cleanup.
2. Add an explicit id/runner/entry/args/protocol. Do not generate executable entries from inventory guesses.
3. Use `reviewFiles` for test-side dependencies needed to understand the invocation; `requiresUnity` for Node integration checks that call the Editor themselves.
4. Add to an appropriate opt-in profile and verify the scenario. Do not claim a profile is the full suite unless its coverage is actually established.
5. Keep signature checks local to package-owned APIs when needed. This runner does not require a production test bridge, changes to test assemblies or new Unity internal reflection.

[R14 implementation and verification report](TestRunner.ru.md).
