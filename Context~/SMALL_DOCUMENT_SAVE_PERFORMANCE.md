# Small-document Save: investigation and completion-wait fix

2026-09-20, Unity 6000.7.0a6, current Windows Editor. Target: 1–5 Drawing layers;
512²/1024² chosen as explicit small-canvas cases. Layer count alone does not bound pixel cost.
The initial investigation changed no production code. The completion-wait fix below is now implemented;
the other candidates remain proposals. No test Assets, documents or scenes were created.

## Verified without Assets

`Tests~/SmallDocumentSaveProbe.cs`, run through the connected Unity Editor:

- `Scheduling(20)`: existing `WhimTexDocumentOperation.Run`, empty work / empty container preparation,
  with and without an operation scope. Progress callback never cancels and replaces only the visible dialog.
- `Carrier(512,5)` and `Carrier(1024,5)`: synthetic RGBAHalf native bytes entirely inside 0..1;
  precision scan, TIFF encoding to MemoryStream and validation. No render, disk write or TextureImporter.
- Stopwatch timing, portable APIs only. No forced GC, global Burst changes or compiler changes.
  First JIT use is warmed for the precision probe. Scope/no-scope order alternates for carrier trials.

Before the fix: medians, milliseconds (20 scheduling samples; 5 carrier samples per configuration):

| Operation | Without progress scope | Original progress scope (Sleep) |
| --- | ---: | ---: |
| Empty work | 0.001 | 31.131 |
| Empty PrepareStoredBlocks | 0.023 | 31.385 |
| Auto range scan, 512² | 18.368 | 31.688 |
| Half → LDR TIFF conversion, LUT construction and compression, 512² | 13.362 | 31.151 |
| TIFF validation, 512² | 2.880 | 31.043 |
| Auto range scan, 1024² | 64.376 | 85.508 |
| Half → LDR TIFF conversion, LUT construction and compression, 1024² | 42.353 | 62.998 |
| TIFF validation, 1024² | 10.660 | 30.643 |

1024² Auto scan was noisy (38.3–67.2 ms without scope), so do not overinterpret its exact median.
These rows are component timings, **not** full Save timings and not cumulative speedup claims.

## Ranked candidates

1. **Wake on task completion instead of unconditional Sleep(20) — implemented, see below.**
   The original `WhimTexDocumentOperation.Run` checked completion, polled progress, then slept for 20 ms even if the
   task just finished. On this Editor the empty case costs ~31 ms. A separate test of `Task.Wait(20)`
   on empty work returned in median 0.008 ms (isolated proposal measurement, not a Save benchmark).
   Keep periodic cancellation checks, exception propagation and waiting for workers before freeing buffers.
   This is a portable completion wait, not a high-resolution OS timer or busy spin.
   [Task.Wait API](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.wait).

2. **Skip empty preparation and avoid pointless worker dispatch.**
   Save calls `PrepareStoredBlocks`; `ContentSignature` calls it again; the latter normally has no work.
   It still enters `Operation.Run`. Writing the carrier then prepares its tiny flags block separately.
   Early return for zero pending blocks is a low-risk candidate; small-work scheduling thresholds need
   measurements. Do not bypass cancellation on genuinely long preparation.

3. **Accelerate the Auto precision scan.**
   Compose returns RGBAHalf even for LDR content. Auto then scans all channels in managed code,
   calling `Mathf.HalfToFloat` for every sample. This is measurable independently of Drawing count.
   A portable Burst reduction or carefully verified half-bit classification can keep exactly the current
   thresholds/NaN behavior. Do not infer LDR solely from layer settings: FX may create HDR values.

4. **Remove repeated conversion setup / unnecessary output upload.**
   TIFF builds the same 65,536-entry half conversion tables on each Save; immutable lazy LUTs could reuse them.
   Compose calls `HdrUtility.ReadLinear` with default `upload:true`: CPU readback is followed by `Apply`,
   although TIFF only consumes CPU data and immediately destroys the temporary composite.
   A save-only CPU-readback path can skip that upload without changing normal preview behavior.
   Neither item has a measured standalone/full-Save gain yet.

5. **Distinguish storage-only edits from image edits, later.**
   Renaming a layer changes the model signature, triggering full Compose/TIFF encode/import despite the same
   visible image. Reusing the encoded carrier requires a trustworthy render/dependency signature. Likewise
   every ShaderFX currently disables the early no-op shortcut, even with unchanged local code/parameters.
   Do not weaken this conservative protection until external inputs/time-dependent FX are accounted for.
   A native TextureImporter can still reimport when the monolithic TIFF's embedded model changes;
   reusing carrier bytes alone does not prove import can be skipped safely.

## Important benchmark correction

Earlier `DocumentPerformanceProbe` invoked `WhimTexDocumentFile.Save` **without** a
`WhimTexDocumentOperation`. Actual window Save creates that scope, so the earlier API benchmark omitted
the repeated task polling delay. Also the internal Save log starts after normalization/SyncDrawing;
it is not the whole user-visible operation. Future totals must wrap the public Save call and its scope.

## Implemented: completion-aware waiting

`WhimTexDocumentOperation.Run` now uses `Task.Wait(20)` instead of unconditional sleep. Completion wakes
the waiter immediately; the timeout allows periodic progress/cancellation checks during long work.
Worker faults are unwrapped through `GetAwaiter().GetResult()`, retaining the original exception rather
than exposing the extra `AggregateException` from `Wait`. The existing catch path still drains workers
before propagating cancellation or a progress callback failure, preserving snapshot/buffer lifetime.
No busy loop, native API, format change or asynchronous editor lifecycle was introduced.

Connected Editor recompile passed. `Tests~/DocumentOperationWaitSmoke.cs`: **28 checks passed** for
sync/scoped work, polling, worker exceptions (including cancellation and a worker's own AggregateException),
callback cancellation/failure, draining with secondary worker errors, scope restoration, commit behavior
and byte-identical container compression/integrity round-trip. All checks are memory-only.

`Scheduling(20,"scheduling-completion-wait")`, same Editor, post-fix medians:

| Operation with progress scope | Before (ms) | After (ms) |
| --- | ---: | ---: |
| Empty work | 31.131 | 0.0121 |
| Empty PrepareStoredBlocks | 31.385 | 0.0138 |

These isolate removal of a waiting delay, **not full Save performance**. No post-fix carrier or full-Save
timing is claimed. Baseline reports were preserved; the new report is
`Temp/WhimTex/TiffValidationResults/small-save-scheduling-completion-wait.json`.

## Pending full-Save test

The broader 1–5 synthetic Drawing benchmark below remains unrun. A later, separately approved
**post-save stall diagnosis on a copy of an actual small document** is recorded in the next section.

`SmallDocumentSaveProbe.Run(size,layers,5)` is prepared but **not executed**: explicit permission for its
unique temporary `Assets/WhimTexSmallSaveProbe_*` folder is pending. It measures synchronous Save with import,
alternating scope/no-scope, unchanged / name-only / pending GPU brush stamp. First save is recorded separately;
subsequent categories have five samples each. It checks no-op timestamp, reports actual imported dimensions,
and cleans only its own folder in `finally`. No real window is opened; visual progress UI and window bookkeeping
are excluded, and Live Update is off. First-save statistics need multiple independently created cases.

Local component reports: `Temp/WhimTex/TiffValidationResults/small-save-scheduling.json`,
`small-save-carrier-512.json`, `small-save-carrier-1024.json` under the Unity project root.
Full Save duration, import share and net benefit of these proposals remain **unmeasured**.

## Confirmed post-save stall: legacy Project icons (diagnosis only)

2026-09-20, same Editor. User reported that Project's image was already updated while the editor
still stalled. With explicit permission, `Tests~/DocumentSaveTailProbe.cs` copied `Assets/Г.tiff`
(512², one Drawing layer) into a unique temporary Assets folder, loaded it into a separate window,
added a very faint Color Fill to vary each saved image, and measured repeat saves after warm-up.
Original TIFF and `.meta` bytes were verified unchanged. Every test folder/window was removed;
selection/focus, profiler settings and the temporarily detached callback were restored.

**No production fix applied.** The A/B test detached only
`TextureCompositorProjectPreview.DrawProjectIcon` from the public Project-window GUI callback,
then reattached it. It did not disable built-in Unity previews or alter save/import algorithms.

Without Profiler recording, two repeats per variant:

| Variant | Save call (ms) | Save return → next Editor update (ms) |
| --- | --- | --- |
| Actual window Save, normal icons | 150.2 / 140.9 | 1933.8 / 1958.9 |
| Mirrored save tail, no Selection/Ping/log | 156.3 / 148.7 | 1911.7 / 1935.1 |
| Actual window Save, legacy icon callback detached | 140.1 / 154.5 | 96.7 / 137.6 |

The measured split tail itself was only ~2–3 ms: binding/dirty UI refresh <~1.1 ms combined,
Ping ~1.1–1.2 ms, log ~0.4–0.6 ms, operation disposal negligible (progress was never shown).
Thus synchronous Selection/Ping/log and the removed worker sleeps do not explain the large tail.

CPU capture independently places ~1.8–2.0 s in `ProjectBrowser.Paint`, dominated by
`Loading.LoadFileHeaders`, after the save callback returns. This was not merely background idle time.
The exact 2 s is specific to the currently visible assets and this test setup, **not** a promise
that the user's perceived half-second pause always has that duration. Editor update latency is
not a direct measurement of mouse/keyboard dispatch latency.

Cause in our code: `TextureCompositorProjectPreview` clears its entire `Outputs` cache on every
`EditorApplication.projectChanged`. Its next repaint calls `LoadMainAssetAtPath` for visible `.asset`
entries and, for old compositor outputs, `FindDocument` → `LoadAllAssetsAtPath`. Even a small TIFF
save can therefore reload expensive unrelated legacy assets in Project. Temporarily suppressing
that callback removes the dominant stall while keeping the same save/import pipeline.

Next fix to design: selective cache invalidation and metadata-only filtering before loading legacy
compositors; no eager asset loading in Project repaint. Preserve old `.asset` icon behavior, and test
rename/delete/reimport/output changes. This diagnosis does not authorize removal of the old format.

Reports under the project root: `Temp/WhimTex/TiffValidationResults/save-tail-probe.json`,
`save-tail-probe-no-profiler.json`, `save-tail-profile.json` (plus `*-initial.json` baseline copies).
`ReadProfile` reads recorded frames after the run, because profiler frame delivery can lag the callback;
the per-trial frame lists alone can be empty. Profiler history is not cleared and may include earlier runs.
