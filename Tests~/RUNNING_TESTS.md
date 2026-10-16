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

### Vector prototype

`vector-clipper-reuse-v1` compares scoped reuse with fresh Clipper instances,
including exact contour order/seams, mixed operations and fill rules, retained
outputs, nested scopes, reentrant isolation, workload exceptions and scope exit.
It owns detached polygon data and does not retain engines between builds.

`vector-prepared-geometry-v1` compares direct prepared contours with the original
normalized-anchor route, including float round trips, duplicate points, contour
seams, original input limits, snapshot ownership and warmed geometry replacement.
Extreme-coordinate cancellation retains the original flattening path and is
checked against both scalar and Jobs preparation.
`vector-prepared-fill-v1`, `vector-prepared-stroke-v1` and
`vector-prepared-dashes-v1` each cover 96 exact CPU/coverage comparisons and eight
GPU pixel comparisons. Cases include holes, fractional and negative coordinates,
degenerate contours, ordered figures, AA/Step, clipping, projective/reflected
transforms and every stroke position/join/cap combination. All four cases passed
on 2026-10-16; the five new regression IDs are included in the `vector` profile.

`vector-prepared-before-v1` / `vector-prepared-after-v1` measure the scoped-engine
and direct Boolean-contour changes. They use the same six cold fixtures and frozen
RGBAFloat references as below, writing immutable `prepared-before` / `prepared-after`
reports under `Temp/WhimTex/vector-clipper-v1`. Nine fresh uncached samples exclude
two engine warmups; compile/JIT time is not part of the cold geometry metric.

Measured 2026-10-16 in the same Editor, completed median milliseconds including
GPU readback (all six exact pixel deltas were zero):

| Fixture | Before | After |
| --- | ---: | ---: |
| Vector 512 | 74.15 | 72.89 |
| Boolean 4, styled/dashed outline, 512 | 196.40 | 190.52 |
| Boolean 16, fill, 512 | 188.54 | 158.23 |
| Vector 1024 | 113.49 | 119.80 |
| Boolean 4, styled/dashed outline, 1024 | 291.78 | 294.88 |
| Boolean 16, fill, 1024 | 253.83 | 228.96 |

The 16-operand cases improved about 10–16%; the small differences in the other
cases are inconclusive in this capture and must not be advertised as a universal
speedup. Resolver call counts remain 4/17/62; no additional Union passes were
removed. Separate phase measurements show input preparation for the 16-operand
cases dropping from 11.97/11.11 to 4.30/5.95 ms. Phase medians come from separate
renders and must not be summed as an exact decomposition of whole-render medians.

`vector-clipper-paired-1-v1` through `vector-clipper-paired-4-v1` investigate timing
variation without editing production code. Each independent block alternates AB/BA
renders of new Vector documents: 16 pairs at 1024 and eight at 512, plus separate
phase samples. The control uses the wrapper's existing reentrant-isolation path to
create a fresh Clipper per operation; it is not a historical binary, and current
validation/final Clear remain enabled. The direct Boolean-contour path is unused.
All temporary scope state is restored synchronously; profiler capture and user
documents are untouched. Raw samples, GC collection deltas and CPU/readback times
are retained in unique `Temp/WhimTex/diagnostics/vector-clipper-paired-<GUID>` folders.

The 2026-10-16 repeat produced 64 samples per mode at 1024: fresh-engine/scoped
medians were 122.77/117.97 ms completed and 104.78/99.09 ms CPU submission. Scoped
completed medians by block were 117.44, 117.64, 119.93 and 117.36 ms, versus fresh
117.87, 126.08, 116.56 and 126.49 ms. At 512, 32 samples per mode gave 77.81/75.51 ms.
No consistent slowdown was observed; the broad tails preclude a universal speedup
claim. The earlier 1024 CPU submission medians were already 103.12/102.76 ms:
the apparent +5.6% was in the GPU-completed metric, not an observed CPU regression.
All 32 frozen-reference pixel comparisons were exact. The initial diagnostic
attempt lost its nested sample array during serialization and is excluded from
these statistics; subsequent runs preserve all samples with managed serialization.

`vector-union-fill-v1`, `vector-union-stroke-v1` and `vector-union-dashes-v1`
separately compare rendered pixels against the original two-pass normalization:
48 fill and 216 cases for each stroke mode. Inputs cover holes, shared edges,
point touches, duplicate contours, self-intersections, acute stars and random
contours, plus compound inputs with mixed fill rules. Stroke variants cover all
positions, joins and caps, three widths, AA/Step, relative/absolute and odd dash
patterns with negative offset. The retained touching-partition counterexample
must have identical fill but differing centered stroke/dash pixels when Union is
blindly skipped. An isolated counter asserts that the exact fixed-point fast path
actually skips one Clipper call. Owned previews go to
`Temp/WhimTex/vector-union-appearance`; no user document or capture is changed.

`vector-union-before-v1` and `vector-union-after-v1` repeat the six cold fixtures
below as `union-before` / `union-after`, with the Burst budget guard already in
both versions. Original RGBAFloat references are reused read-only; timings are
new immutable reports. Vector-only fixtures are controls: their path is unchanged,
so their timing variation must not be attributed to the Boolean optimization.

`vector-clipper-regions-v1` retains the previous Boolean-normalization sequence as
an independent oracle over unchanged Clipper2. It compares exact oriented vertices
up to seam rotation and path ordering on 1,200 cases, including touching/self-crossing
contours, all four Boolean operations and mixed fill rules. It retains a counterexample
to skipping normalization of prior Clipper results. `vector-clipper-budgets-v1`
compares exact visit counts and rejections to the old active-list sweep, including both sides of
the four-million-visit limit. Both use test-owned geometry; the production budget
check uses short-lived native arrays and an integer-only Burst job.

`vector-clipper-before-v1`, `vector-clipper-before-timings-v1` and
`vector-clipper-after-v1` are opt-in diagnostics under
`Temp/WhimTex/vector-clipper-v1`. Before phases require the original wrapper and must
never be used to regenerate goldens after optimization. The first capture retained
whole-render timings and pixels; `before-timings` adds complete structured phase
data without replacing those captures. The after entry writes `after-burst` reports
(`after` and `after-final` retain intermediate managed candidates)
and compares to the frozen RGBAFloat pixels
exactly. Each phase uses the same six fixtures as the cold benchmark: nine fresh
uncached documents after two engine warmups. Three additional fresh renders measure
the stage breakdown separately, excluding timing-sink overhead from whole-render
medians. Counts must agree across those three renders. Parent timings are inclusive.
The scoped sink and instrumentation suppression preserve user profiler captures;
no open document, permanent asset or profiler baseline is replaced.

`vector-preparation-flatten-v1` compares Jobs/Burst cubic subdivision against an
independent scalar reference: exact points and order, seams, tolerances, degenerate
curves and repeated execution. `vector-preparation-coverage-aa-v1` and
`vector-preparation-coverage-step-v1` compare masks to scalar scan conversion,
including holes, narrow features, all joins/caps/positions, dashes, transforms,
clipping and overlapping translucent figures. Fill/stroke coverage is exact;
intersection reconstruction and GPU paint allow only their stated floating-point
rounding tolerances. These cases use owned transient data and restore render state.
`vector-preparation-coverage-budgets-v1` checks pre-dispatch work limits, accounting
for previous figures, fractional edge spans and crop bounds. Oversized synthetic
inputs cannot reach dispatch; exact-limit positives use only six pixels.

`vector-cold-benchmark-v1` is an opt-in diagnostic. It requires the six frozen
pre-change RGBAFloat references in `Temp/WhimTex/vector-cold-jobs-v1` and enabled
Burst. It never regenerates those references during comparison, and refuses to
overwrite `after-final` reports. Each of nine measured samples creates a fresh
document, so geometry, Boolean results and coverage start uncached. Two engine
warmups are excluded; this does not measure script/Burst compilation. It records
CPU submission separately from completion including GPU readback, not UI FPS.
Cases cover 512/1024 curved Vector, four-operand Boolean with a styled outline,
and sixteen-operand Boolean fill. Exact raw-pixel differences are reported against
the pre-change captures; inspect the retained PNGs as well.

`vector-paint-cache-v1` checks retained coverage across fill/stroke paint edits,
geometry invalidation, Boolean-result reuse, translucent overlap and recovery after
the owned paint RenderTexture loses storage. It uses transient objects and checks
colors against an independent CPU calculation, without changing the open document.
`vector-paint-benchmark-v1` is an opt-in diagnostic requiring the frozen pre-change
reference in `Temp/WhimTex/vector-paint-benchmark-v2`; it does not create a baseline
from the current renderer. Timings distinguish CPU submission from completion with
GPU readback and are not whole-editor interaction FPS.

`vector` selects geometry, native rasterization/FX/Target integration, mask-brush
rebuilds, JSON/native-TIFF persistence, Shape conversion and 94 Shape/Vector pixel
comparisons, including shared styled outlines. `shape-strokes-v1` also checks relative
dashes, Feather, transform width, clipping/Target, thumbnails and export.
It uses owned transient data only. `vector-tools` selects nine functional
UI scenarios: figure/node transforms, Pen, Brush, cancellation/capture loss, stale
layer removal, window focus loss, Position/Join/Cap icon controls and dash editing. It needs `--allow-effects windows,user-state`;
all gestures run in owned utility windows and restore preferences/focus afterward.
Native Undo/Redo is exercised; no user document is used. These are functional checks,
not pixel-perfect UI goldens or a restart/reload acceptance test.
`boolean-group-mode-v1` checks the five group Mode icon buttons, Combine appearance selection, uniform/mixed state,
empty groups, native Undo/Redo, repeated clicks and unchanged nested operations.
It requires `windows,user-state,temp-files` and captures the owned window to
`Temp/WhimTex/boolean-group-mode.png` for visual inspection.
`vector-stroke-style-v1` checks all nine icon buttons, selected-state synchronization,
Undo/Redo, repeated-choice no-op, disabled Stroke and retained brush source.
The separate `vector-stroke-style-preview-v1` also requires `temp-files` and writes
the actual owned inspector to `Temp/WhimTex/vector-stroke-style-preview.png` for visual review.
`vector-dashes-v1` checks text/pair/preset synchronization, odd cycles, Enter/blur/Escape,
invalid input, Undo/Redo, cap/width/offset preview updates and disabled Stroke.
`vector-dashes-preview-v1` requires `temp-files` and captures the controls and preset
gallery to `Temp/WhimTex/vector-dashes-editor.png` and `vector-dashes-presets.png`.
These are actual UI captures for inspection, not pixel-golden comparisons.
`shape-stroke-ui-v1` requires `windows,user-state,temp-files` and checks the same
shared controls on Shape, native Undo/Redo and preserved Feather/Transform settings.
Its actual inspector/canvas capture is `Temp/WhimTex/shape-strokes-editor.png`.
`stroke-seams-v1` requires `temp-files`: it checks 1,080 round capsules and 621
round joins for artificial interior boundaries, not just covered area. The shared
Shape/Vector star preview is written to `Temp/WhimTex/stroke-seams.png`.
`stroke-alignment-v1` requires `temp-files`: it checks full round dots/caps for both
offset sides, three widths, two dash lengths and all joins, plus holes/reversed winding,
collapsed insets, split narrow necks and unchanged open paths. Its actual render is
`Temp/WhimTex/stroke-alignment.png`: top Inside, bottom Outside; each pair is Shape
then Vector, with dots on the left and round-ended dashes on the right. The parity
matrix includes 24 offset-dot/dash comparisons on rectangles and stars.

The separate `vector-render-preview-v1` diagnostic requires `--allow-effects temp-files`
and writes actual composition pixels to project `Temp/WhimTex/vector-render-preview.png`.
Inspect that image; the existence of the PNG alone is not a visual-quality verdict.
`vector-brush-preview-v1` uses the same effects and renders two cubic fits of the
same saved mask, with control handles, to `Temp/WhimTex/vector-brush-curves-preview.png`.
Left: 0.25 reference-pixel tolerance; right: 4. `vector-brush-v1` checks smooth
closed joins, cubic density, reversible precision, unchanged mask bytes, holes and
narrow features. `vector-tools-brush-v1` also checks precision Undo/Redo through Properties.
`document-json-schema-v2` covers the additive frozen Vector defaults and source-mask
metadata. Schema validation does not decode/validate mask byte counts; native
`vector-persistence-v1` checks those rejection paths.

### Runner selection

`figure-gradient-canvas-v1` exercises shared handles on all five Shape/Vector/Boolean Group gradient fills:
anchor movement under parent transforms, unchanged figure transforms, circular range/Ctrl,
palette keys, Undo/Escape and Boolean appearance gating. It requires
`windows,user-state,temp-files`; actual Shape/Vector/Boolean Group canvas previews remain in a GUID diagnostic directory.
It also checks every Boolean mode and Combine Group/Layers handle availability, preserving child geometry and appearance.
`figure-fills-v1` checks CPU/GPU circular repetitions and palette wrapping;
`figure-fill-persistence-v1` checks repetitions in JSON, API, conversion, native payload and TIFF.

`gradient-circular-canvas-v1` checks split endpoint colors, both opening directions,
closing, rotation/translation, color keys/midpoints, Undo/Redo, Escape, mirrored hit testing
and JSON roundtrips in an owned window. It requires `windows,user-state,temp-files` and
retains closed/open canvas captures under `Temp/WhimTex/diagnostics/<GUID>/`.

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

`comment-editing-v1` checks local draft transitions and reload snapshots, content conflicts
independent of author colors, atomic message edits, exact-link and text queries, feedback
revisions with mask replies, retained row ordering and connector contact geometry around
rounded card corners. It uses an owned in-memory document
and detached UI elements, without windows, input, focus changes or global Undo.

The `comments` profile checks optional TIFF comment storage and canvas geometry in memory,
then exercises the Comment tool in an owned temporary window. `comment-author-v1` uses injected
organization/email providers to check organization priority, email fallback, persistence, retries, manual overrides and the settings
field without changing the Unity account. It also checks deterministic hue, unchanged HSV saturation/value,
custom colors, profile ownership across nickname changes and manual overrides. It isolates and restores
author, profile and color preferences. The UI case checks post/reply authors, retained authorship during
edits, automatic draft/posted colors, manual colors and reset controls. Review all declared sources
and allow `assets,windows,user-state,temp-files` for the whole profile (the file case owns its GUID TIFF fixture). It tests only its own Undo records, restores tool
preferences and focus, and writes UI previews to `Temp/WhimTex/comment-tool-preview.png`,
`comment-list-preview.png` and `comment-list-narrow-preview.png` in the same directory.
The UI case also covers shared Open/Resolved/All filters and full-message search, preserved per-thread drafts,
permanent numbering, reply deletion/Undo, edited timestamps, floating card move/resize/connector geometry,
simultaneous cards with independent drafts/attachments/actions, focus stacking, per-card close/Escape,
outside-click disposal of untouched new drafts while retaining configured drafts and published discussions,
Close All in the list header with filtered cards, retained reply/new/edit drafts, masks, empty slots and manual color,
automatic placement after closing via the card, Escape or Close All while retaining the chosen size,
filter/tool restoration and individual connectors (`Temp/WhimTex/comment-multiple-preview.png`),
the shared comments drawer, colored row stripes, resolved rows,
virtualization, scrolling, short-window layout, attachment editing and drops, read-only posted references,
and asset/layer ping without selection changes. Attachment UI captures use Temp/WhimTex/comment-attachments-preview.png.
The Comment UI case polls every 5 seconds to reduce Pipeline compilation overhead.
Day separators cover same-day messages, a local-midnight boundary, later edits and repeated refreshes without document changes; the card render is saved to `Temp/WhimTex/comment-days-preview.png`.
Ping phase checks pause only the owned window's scheduled animation and advance its production
update with explicit elapsed times. Each phase is checked after UI layout/style updates: scale,
hold, outer border, stacking, fade and cleanup, including a frame delayed beyond the lifetime.
A separate ping uses the live scheduler and must finish automatically. No short wall-clock phase
needs to coincide with a test continuation; production animation timing is unchanged.
Selection-mask coverage includes lossless TIFF/JSON/Undo roundtrips, soft edges and holes,
crop/expand/scale/orientation, malformed inputs and decoded-pixel budgets. Agent cases check
selection/PNG/reference attachments, exact mask export, strict completion, unchanged working
selection and stale-mask guards. UI cases check attach, preview, Use, draft-only replacement,
cancellation and texture cleanup; the rendered card/overlay is saved to `Temp/WhimTex/comment-mask-preview.png`.
Show on Canvas and list double-click checks cover preserved zoom/rotation/drafts, marker highlighting,
off-canvas anchors and keeping the card clear of the marker beside a full-height list.
`comment-workflow-v1` checks combined context exports, independent application coverage, expansion
and feather, exact preservation of source pixels outside coverage, detached review images,
separate-mask/fork guards and serialized recovery conflicts/cancellation/corrupt data.
It also reproduces a completed masked edit followed by a reply with layer, asset, review-mask and
copied-mask attachments: feedbackRevision must survive refresh, Undo/Redo, retry and JSON roundtrip.
Editing the reply's mask or explicitly changing status must still invalidate feedback. Thread-wide
Unity managed-reference tables must not leak excluded agent masks into this revision.
`comment-navigation-v1` covers participant/date/sort controls, invalid ranges, zoom/overlap clustering,
native chooser selection, API query boundaries and temporary link navigation through excluding filters.
It also checks text/author substrings and full case-sensitive link search in the UI and API, surrounding
whitespace, rejected partial/altered/normalized/other-document links, retained filters and unchanged data.
Marker gestures cover closed-card dragging without opening, returning to the drag origin, click jitter,
Undo/Redo and preservation of an already open card and its draft.
Status choices cover capture on the initiating field and press/drag/release through the source window's mouse event path, ordinary
two-click selection, release outside options, Escape and keyboard submit; each selection commits once.
Its render is `Temp/WhimTex/comment-navigation-preview.png`.
`comment-file-v1` creates a GUID-owned TIFF fixture and exercises Headless preview/context/complete,
all attachment kinds, exact mask export, file dry-run and guarded write, reply provenance and feedback
revision after save/reopen, stale request rejection, Headless cancel/commit and persistent link navigation
after asset rename. It removes its own assets/window and restores tool preferences; user files are untouched.
The separately selected `agent-reload-v1` performs one real native script reload, then resumes the
same reservation with retained masks and completes it. It also cancels an interrupted edit lock and verifies
that multiple open cards, their focused discussion, independent drafts, an unposted message edit,
attached mask, empty attachment slot and separate floating layouts survive the real reload.
It requires an idle Editor with no borrowed pending/recoverable operations; all fixture windows,
documents and textures are transient. It never saves user documents. Use the same
`windows,user-state,temp-files` effects; do not run it concurrently with compilation or other tests.

`comment-agent-v1` checks unsaved discussion inspection, context PNGs/crops, guarded replies
and status changes, atomic batch Undo and changed/deleted feedback during reservation/fork/lock
workflows. It also checks attachment aliases/assets, idempotent work/request retries and conflicts,
receipt Undo/Redo, agent progress during generation and completed-work recovery without transient jobs.
Creation checks include required fields, text limits, attachments, returned IDs, dry-run, atomic rejection,
Undo/Redo and receipt replay. Color changes preserve feedback guards while full revisions stay strict.
The comments model case covers every InProgress / AwaitingReview / Resolved transition and TIFF/JSON
roundtrips, invalid combinations and replies preserving status. UI checks cover review badges, the global
badge surviving filters, reading without document changes, status filters and Undo/Redo. The review-state
render is saved to `Temp/WhimTex/comment-review-preview.png`; the tool badge has a transparent clearance.
The status picker is opened through its UI event and a choice is submitted from its panel overlay.
Choices use vector icons with hover names. The initiating field retains pointer capture for a held gesture;
desktop focus loss cancels it. Card/list icons appear in the review render.
Agent checks cover exact status strings, dry-run isolation, invalid/conflicting inputs, feedback revisions
and a result reply with its status in one Undo group.
It owns a temporary window and Drawing texture, writes previews under `Temp/WhimTex/Agent`,
and does not create or change Unity assets. Saved-path and Headless storage use the shared model/API
paths; this case targets the unsaved Assistant workflow.

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
If another beforeAssemblyReload subscriber throws, the native reload can finish without our
before-event marker. Verify still fails. Cleanup may release the owned fixture only after the
old AppDomain marker has disappeared and the Editor is idle; it never fabricates a passing marker.

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

`image-clipboard` selects only `image-clipboard-write-v1` and
`image-clipboard-native-v1`. The first checks DIBV5 encoding and internal HDR paste
placement without accessing the OS clipboard. The native case checks Windows
PNG/DIBV5 publication, selection alpha, HDR snapshot priority, merged copy and
replacement-image invalidation using owned transient documents and windows.
After source review, use `--allow-effects windows,user-state` for this profile.

Native checks return SKIP before mutation unless every original clipboard format
can be captured safely: at most 64 supported memory formats and 4 MiB total.
Unsupported handles, OLE data and concurrent changes prevent the run. Cleanup
verifies every restored format and byte; a subsequent external clipboard change
is left untouched. If `recoveryRequired` is true, retain the loaded fixture's
in-memory capture and resolve its owned recovery before another native run.

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

`text-font-atlas` checks large native bitmap atlases against independently loaded
system-font glyphs, including a nonzero face in a TTC collection with distinct glyph
metrics. It prefers Arial and a monospaced/proportional collection when available.
A readable TTF is required, and missing TTC coverage returns SKIP. The result
retains the resolved faces and coverage facts. The case restores the initial public
FontEngine face and releases only owned resources. `text-edge-modes` also rejects
font-face load warnings across all three edge modes and large-font styles.

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

`profiler-allocations-v1` checks per-stage main-thread allocation counts and nested self
counts, excludes an owned worker from stage counts while including its bytes in the
Editor-wide total, crosses real Editor frames, and verifies overflow/recovery and frozen
stopped totals. It joins its worker, disposes recorders and restores the stopped user
capture. Allow `user-state`; no assets or user documents are changed.

`profiler-diagnostics-v1` checks texture/native-cache ownership, duplicate release, actual runtime
GC-counter availability, suppression, observed source-property changes and dependent cache misses,
bounded cause explanations, filters and memory/cause snapshot roundtrips. It owns transient objects
and preserves the user's stopped capture/baseline. Allow `user-state`.

`profiler-diagnostics-ui-v1` uses an owned attached window to check unavailable/zero memory values,
cause evidence/limits, benchmark controls and progress, serialization/restore, selection/filter/scroll
preservation and repeated CreateGUI. It restores focus and preserves capture/session tokens.
Allow `windows,user-state`. This is a functional UI test, not a screenshot comparison.

`profiler-gpu-coverage-v1` checks instrumented non-FX boundary placement and on/off rendering parity;
it does not wait for native timestamp delivery. Allow `user-state`. Actual delayed native GPU
delivery for composition, layer generation, transforms and encoded copies is checked by `profiler-fx-v1`.

`profiler-benchmark-statistics-v1` checks deterministic median, interpolated p95, population deviation,
invalid samples and bounds. `profiler-benchmark-v1` additionally performs small real snapshot renders,
checks cold/warm ownership, frozen readable/unreadable textures and pending Drawing pixels, default
null brush references, unchanged source state/capture, cancel/timeout/failure/reload cleanup and
time-dependent FX rejection. Allow `user-state`. It invokes the reload callback, not a real reload;
its small render timings are functional evidence, not performance-comparison measurements.

`profiler-analysis-v1` checks baseline/current identity, immutable snapshots, workload versus mean
cost, null/zero/partial GPU and inclusive/self allocation deltas, integer precision, filters and paging, bounded agent summaries, cache/reason counts
and baseline persistence through the save/restore functions. It uses deterministic synthetic spans,
isolates and restores the user's capture, baseline and SessionState token, and deletes only its own
Temp snapshot. Allow `temp-files,user-state`. This tests the reload path but does not trigger a native
domain reload; it does not benchmark rendering or generate GPU samples.

`profiler-comparison-ui-v1` opens an owned temporary window and checks baseline actions, all eight
comparison metrics, shared filters, operation-filter warnings, 205-stage paging, nullable/partial GPU
rows, inclusive/self allocation units and coverage, recycled tooltips/styles, serialized view settings and repeated CreateGUI scheduling. It restores
the user's current/baseline references, reload tokens and focus. Allow `windows,user-state`.
It is a functional UI check, not a screenshot/golden-layout comparison.

`profiler-table-layout-v1` measures actual UI Toolkit header/realized-row column bounds across
Summary, Comparison and Events, 680/1000-point windows, light/dark styles, with/without vertical
scrollbars and after vertical/horizontal scrolling. It also checks both allocation headers, ascending/
descending numeric sorting, unavailable-last ordering, partial/zero/recycled cells and serialized sort settings.
Comparison delta colors are checked in both themes for all metrics, including recycled rows,
rounded zeros, missing percentages and incomplete measurements; call-count changes stay neutral.
It uses synthetic rows in an owned window across real Editor frames;
the user's capture is never replaced. Allow `windows,user-state`. No screenshot goldens or assets.

`profiler-coverage-v1` checks editor instrumentation with owned transient documents and a hidden window:
per-document change provenance, nested edits, root/filter behavior, coalesced render requests and
capture boundaries, real cache miss/hit/release reasons, selection-mask reuse, brush/composite pixel
parity, JSON roundtrip and the in-memory reload serialization codec. It preserves a stopped user
capture and its reload backup; requires no pending GPU queries. Allow `windows`; no Unity assets
or document files are created. This is not a native reload test or end-to-end file-import/dialog test.

`profiler-fx-v1` uses an isolated capture only during synchronous renders of owned transient
FX child/group/Target/material fixtures; it preserves any stopped user capture. It checks CPU
preparation/submission accounting, distinct FX summary rows, real delayed GPU results over three
batches (including recorder reuse and draining after Stop), pending export/reload handling, exact
off/on pixel parity, UI columns and snapshot roundtrip. It also exercises the automatic UI callback:
warmup/ready status, delayed completion, read throttling, unchanged-capture no-op, preserved selection,
filters/sort/folding/scroll, and scheduler cleanup when rebuilding or disabling the window.
It does not enable the Unity Profiler window or use internal Unity APIs. Allow `temp-files,windows`.
The GPU backend must produce samples when supported; unsupported hardware returns SKIP, not a GPU PASS.
The fixture pumps ordinary Editor frames while waiting; no user document or project asset is changed.
Its JSON results are under `Temp/WhimTex/profiler-fx-<run-id>.json`.

`profiler-reload-v1` uses one real native compilation/domain reload, with the shared GUID-owned
reload protocol. It verifies automatic retention of exact completed events/timings, interruption
of an open root, unchanged capture ID, and automatic UI population. It refuses an existing user
capture and requires `temp-files,windows,user-state`; it creates no project assets.
`profiler-v1` also checks stopped-capture snapshot roundtrips, interrupted scopes, invalid snapshot
rejection and Clear preventing resurrection, without claiming these substitute for a real reload.

`profiler-v1` checks the opt-in capture API on owned transient documents: nested
wall/self accounting, change and layer identities, document filters, cache hits and
invalidation, bounded capacity/deadline, pagination and full JSON export. It compares
rendered pixels with recording off/on and builds an owned unattached profiler UI
tree for control/list checks, including layer-name snapshots, filtered hierarchy/bar scale,
recycled row colors, split-pane structure and tree folding across pages with filtered ancestor paths.
It also opens a temporary profiler window for real summary-header submit events, checks all four
numeric sort keys in both directions, missing timings, stable ties, unchanged bar scale and serialized
sort state, then closes that window and restores focus.
Group checks cover all six classifications, multi-selection/solo/none, pre-pagination filtering,
context-only ancestors, unchanged timings, counts for hidden groups and serialized UI selection.
`profiler-groups-readonly-v1` runs the filter/UI checks against a stopped retained capture of at most
2000 events containing `document.render`, without starting, stopping, clearing or replacing it. It checks
event and metadata identity afterward and owns only temporary windows (`--allow-effects windows`).
Synthetic cross-group hierarchy cases remain in `profiler-v1`.
Allow `temp-files,windows`; exports use unique names
under `Temp/WhimTex`. An existing user capture causes SKIP without modification.
The test does not enable Unity Profiler or claim GPU/worker timing coverage.

`distance-rbf-opt-capture-*` / `distance-rbf-opt-compare-*` are paired, opt-in
session checks for the RBF cold-build optimization. Capture the `small`, `star512`,
`rings512` and `star1024` suites BEFORE modifying the kernel; capture refuses to
overwrite a snapshot. Compare authenticates its SHA256 and checks every raster
and output byte, or reports SKIP if the session snapshot is missing. The small
suite has 30 fields covering both sharp modes, thresholds, channels and tiny/odd
dimensions. Each large suite measures five fresh CPU field constructions with
no retained geometry, excluding raster generation and input/output allocation.
Phase CSVs and source hashes stay in `Temp/WhimTex/rbf-optimization-20261014`.
Timings share Editor/compiler state and are diagnostics, not speed assertions.

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

For a paired CPU-only comparison of the first six modes, explicitly select
`distance-classic-opt-capture-{small,star512,rings512,star1024}` **before** editing
production, then the corresponding `distance-classic-opt-compare-*` IDs afterward.
These are individual IDs, not a brace-expanding runner profile. Both need reviewed
sources and `--allow-effects temp-files`. The session snapshots under
`Temp/WhimTex/classic-sdf-optimization-20261015` cannot be overwritten by capture;
compare authenticates their SHA256 and requires identical input and output bytes,
including all four MTSDF channels. Missing snapshots produce SKIP, not PASS.
The small matrix covers 180 fields; each large fixture measures seven fresh builds
per mode in Guarded Sharp. CSVs retain all samples and nested phase timings, and
`.sources` files record production hashes, Editor version and CPU. The interval
includes geometry/field workspace construction and disposal, but excludes source
generation, input/output allocation, GPU transfer and rendering. No persistent
field cache or explicit warmup is used; Editor/compiler state remains shared.
PASS means exact field equivalence and finite/repeatable output, not a speed claim.
Use matching source hashes to identify runs when comparing a restored baseline.

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
